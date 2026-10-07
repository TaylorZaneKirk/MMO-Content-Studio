using System.Data;
using System.Text.Json;
using MMO.Project.Farming;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;
namespace MMO.ContentStudio.AuthoringHost.Persistence;
// One editable rules document; published_settings retains the last reviewed
// publication while a replacement draft is edited. Live crops keep frozen rules.
public sealed class FarmingRepository(AuthoringDatabaseConnectionFactory factory)
{
    public async Task<IReadOnlyList<FarmingSummary>> ListAsync(string? search, CancellationToken ct)
    {
        var row = await LoadAsync("flowers", ct);
        return row is null ? [] : [new("flowers", "Flower farming", row.PublicationState)];
    }
    public async Task<FarmingDefinition?> LoadAsync(string id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await Load(connection, null, id, false, ct);
    }
    private static async Task<FarmingDefinition?> Load(NpgsqlConnection connection, NpgsqlTransaction? transaction, string id, bool locked, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT settings,publication_state,updated_at FROM farming_definitions WHERE definition_id=@id" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(id, reader.GetString(1), JsonSerializer.Deserialize<FarmingSettings>(reader.GetString(0))!, reader.GetFieldValue<DateTimeOffset>(2));
    }
    public async Task<IReadOnlyList<string>> ValidatePublishedAsync(FarmingSettings settings, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT item_id FROM item_definitions WHERE runtime_enabled", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct)) items.Add(reader.GetString(0));
        return settings.Validate(items);
    }
    public async Task<FarmingDefinition?> MutateAsync(string id, string operation, FarmingSettings draft, DateTimeOffset? expected, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var existing = await Load(connection, transaction, id, true, ct);
        if (existing?.UpdatedAtUtc != expected) throw new FarmingConcurrencyException();
        var sql = operation == "save_draft"
        ? existing is null
        ? "INSERT INTO farming_definitions(definition_id,settings) VALUES(@id,@settings)"
        : "UPDATE farming_definitions SET settings=@settings,publication_state='Draft',updated_at=clock_timestamp() WHERE definition_id=@id"
        : "UPDATE farming_definitions SET published_settings=settings,publication_state='Published',updated_at=clock_timestamp() WHERE definition_id=@id";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("settings", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(draft));
        await command.ExecuteNonQueryAsync(ct);
        var result = await Load(connection, transaction, id, false, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
public sealed class FarmingConcurrencyException : Exception;
