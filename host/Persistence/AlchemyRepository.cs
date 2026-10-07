using System.Data;
using System.Text.Json;
using MMO.Project.Alchemy;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;
namespace MMO.ContentStudio.AuthoringHost.Persistence;
// One editable rules document; published_settings retains the last reviewed
// publication while a replacement draft is edited.
public sealed class AlchemyRepository(AuthoringDatabaseConnectionFactory factory)
{
    public async Task<IReadOnlyList<AlchemySummary>> ListAsync(string? search, CancellationToken ct)
    {
        var row = await LoadAsync("v1", ct);
        return row is null ? [] : [new("v1", "Alchemy V1", row.PublicationState)];
    }
    public async Task<AlchemyDefinition?> LoadAsync(string id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await Load(connection, null, id, false, ct);
    }
    private static async Task<AlchemyDefinition?> Load(NpgsqlConnection connection, NpgsqlTransaction? transaction, string id, bool locked, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT settings,publication_state,updated_at FROM alchemy_definitions WHERE definition_id=@id" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(id, reader.GetString(1), JsonSerializer.Deserialize<AlchemySettings>(reader.GetString(0))!, reader.GetFieldValue<DateTimeOffset>(2));
    }
    public async Task<IReadOnlyList<string>> ValidatePublishedAsync(AlchemySettings settings, CancellationToken ct)
    {
        var shapeErrors = settings.Validate();
        if (shapeErrors.Count > 0) return shapeErrors;
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT item_id,stackable FROM item_definitions WHERE runtime_enabled", connection);
        var items = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) items.Add(reader.GetString(0), reader.GetBoolean(1));
        var errors = settings.Validate(items.Keys.ToHashSet(StringComparer.Ordinal)).Concat(settings.ValidateItemShapes(items)).ToList();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM world_object_definitions w JOIN world_object_interactions x USING(definition_id) WHERE w.definition_id=@station AND w.publication_state='Published' AND x.action_id='alchemy')";
        command.Parameters.AddWithValue("station", settings.StationDefinitionId);
        if (await command.ExecuteScalarAsync(ct) is not true) errors.Add("Publish an Alchemy station with the alchemy action first.");
        return errors;
    }

    public async Task<AlchemyDefinition?> MutateAsync(string id, string operation, AlchemySettings draft, DateTimeOffset? expected, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var existing = await Load(connection, transaction, id, true, ct);
        if (existing?.UpdatedAtUtc != expected) throw new AlchemyConcurrencyException();
        var sql = operation == "save_draft"
        ? existing is null
        ? "INSERT INTO alchemy_definitions(definition_id,settings) VALUES(@id,@settings)"
        : "UPDATE alchemy_definitions SET settings=@settings,publication_state='Draft',updated_at=clock_timestamp() WHERE definition_id=@id"
        : "UPDATE alchemy_definitions SET published_settings=settings,publication_state='Published',updated_at=clock_timestamp() WHERE definition_id=@id";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("settings", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(draft));
        await command.ExecuteNonQueryAsync(ct);
        var result = await Load(connection, transaction, id, false, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
public sealed class AlchemyConcurrencyException : Exception;
