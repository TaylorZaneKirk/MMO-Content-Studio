using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;
using MMO.Project.Prototype.Server.Game;
using MMO.ContentStudio.AuthoringHost.Contracts;
namespace MMO.ContentStudio.AuthoringHost.Persistence;

// One method document and its last published snapshot. Expected versions plus a
// row lock protect edits; the primary key protects simultaneous first saves.
public sealed class FishingRepository(AuthoringDatabaseConnectionFactory factory)
{
    public async Task<IReadOnlyList<FishingSummary>> ListAsync(string? search, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT action_id,draft->>'display_name',publication_state FROM fishing_methods WHERE @s='' OR action_id ILIKE '%'||@s||'%' ORDER BY action_id", connection);
        command.Parameters.AddWithValue("s", search?.Trim() ?? "");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<FishingSummary>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }
    public async Task<FishingDefinition?> LoadAsync(string id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await Load(connection, null, id, false, ct);
    }
    private static async Task<FishingDefinition?> Load(NpgsqlConnection connection, NpgsqlTransaction? transaction, string id, bool locked, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT draft,publication_state,updated_at,published_method IS NOT NULL FROM fishing_methods WHERE action_id=@id" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(id, reader.GetString(1), JsonSerializer.Deserialize<FishingMethod>(reader.GetString(0))!, reader.GetFieldValue<DateTimeOffset>(2), reader.GetBoolean(3)) : null;
    }
    public async Task<IReadOnlyList<string>> ValidatePublishedAsync(FishingMethod method, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT item_id,stackable FROM item_definitions WHERE runtime_enabled", connection);
        var items = new HashSet<string>(StringComparer.Ordinal);
        var stackable = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) { items.Add(reader.GetString(0)); if(reader.GetBoolean(1)) stackable.Add(reader.GetString(0)); }
        var errors = method.Validate(items).ToList();
        if (method.ConsumableItemId is not null && !stackable.Contains(method.ConsumableItemId))
            errors.Add("Fishing consumables must be stackable; quantity is taken from one available stack.");
        return errors;
    }
    public async Task<FishingDefinition?> MutateAsync(string id, string operation, FishingMethod draft, DateTimeOffset? expected, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var existing = await Load(connection, transaction, id, true, ct);
        if (existing?.UpdatedAtUtc != expected || (operation == "delete" && existing?.HasPublishedSnapshot == true))
            throw new FishingConcurrencyException();
        var sql = operation switch
        {
            "save_draft" when existing is null => "INSERT INTO fishing_methods(action_id,draft) VALUES(@id,@draft)",
            "save_draft" => "UPDATE fishing_methods SET draft=@draft,publication_state='Draft',updated_at=clock_timestamp() WHERE action_id=@id",
            "publish" => "UPDATE fishing_methods SET published_method=draft,publication_state='Published',updated_at=clock_timestamp() WHERE action_id=@id",
            "disable" => "UPDATE fishing_methods SET published_method=NULL,publication_state='Disabled',updated_at=clock_timestamp() WHERE action_id=@id",
            "delete" => "DELETE FROM fishing_methods WHERE action_id=@id",
            _ => throw new InvalidOperationException("Unsupported Fishing operation.")
        };
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("draft", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(draft));
        await command.ExecuteNonQueryAsync(ct);
        var result = await Load(connection, transaction, id, false, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
public sealed class FishingConcurrencyException : Exception;
