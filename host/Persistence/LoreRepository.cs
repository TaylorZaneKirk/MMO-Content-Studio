using System.Data;
using System.Text.Json;
using MMO.Project.Lore;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;
namespace MMO.ContentStudio.AuthoringHost.Persistence;
// One editable rules document; published_settings retains the last reviewed
// publication while a replacement draft is edited.
public sealed class LoreRepository(AuthoringDatabaseConnectionFactory factory)
{
    public async Task<IReadOnlyList<LoreSummary>> ListAsync(string? search, CancellationToken ct)
    {
        var row = await LoadAsync("v1", ct);
        return row is null ? [] : [new("v1", "Insight — subject family mastery", row.PublicationState)];
    }
    public async Task<LoreDefinition?> LoadAsync(string id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await Load(connection, null, id, false, ct);
    }
    private static async Task<LoreDefinition?> Load(NpgsqlConnection connection, NpgsqlTransaction? transaction, string id, bool locked, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT settings,publication_state,updated_at FROM lore_definitions WHERE definition_id=@id" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(id, reader.GetString(1), JsonSerializer.Deserialize<LoreSettings>(reader.GetString(0))!, reader.GetFieldValue<DateTimeOffset>(2));
    }
    public async Task<IReadOnlyList<string>> ValidatePublishedAsync(LoreSettings settings, CancellationToken ct)
    {
        var shapeErrors = settings.Validate();
        if (shapeErrors.Count > 0) return shapeErrors;
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT item_id,stackable FROM item_definitions WHERE runtime_enabled", connection);
        var items = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) items.Add(reader.GetString(0), reader.GetBoolean(1));
        var errors = settings.Validate(items.Keys.ToHashSet(StringComparer.Ordinal)).Concat(settings.ValidateItemShapes(items)).ToList();
        command.CommandText = "SELECT count(*) FROM mob_definitions WHERE mob_definition_id=ANY(@members) AND publication_state='Published'";
        command.Parameters.AddWithValue("members", settings.Families.SelectMany(f => f.MobDefinitionIds).ToArray());
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != settings.Families.Sum(f => f.MobDefinitionIds.Length))
            errors.Add("Every authored family member must reference a published Mob definition.");
        command.CommandText = "SELECT count(*) FROM item_definitions WHERE item_id=ANY(@specimens) AND runtime_enabled AND NOT stackable AND trade_policy='tradeable'";
        command.Parameters.AddWithValue("specimens", settings.Specimens.Select(s => s.ItemId).ToArray());
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) != settings.Specimens.Length)
            errors.Add("Every specimen must reference an enabled, nonstackable, tradeable item.");
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM world_object_definitions WHERE definition_id=@lectern AND publication_state='Published' AND footprint_width_tiles=1 AND footprint_height_tiles=1) AND (SELECT count(*) FROM world_object_interactions WHERE definition_id=@lectern AND action_id IN ('restore_concentration','study_manual','study_auto'))=3 AND NOT EXISTS(SELECT 1 FROM character_stats WHERE concentration_drain_ticks>=@drainTicks)";
        command.Parameters.AddWithValue("lectern", settings.LecternDefinitionId);
        command.Parameters.AddWithValue("drainTicks", TimeSpan.FromMilliseconds(settings.FocusDrainMs).Ticks);
        if (await command.ExecuteScalarAsync(ct) is not true) errors.Add("Publish a one-tile lectern with restore/manual/automatic actions. A shorter drain interval requires saved remainders to be reconciled before publication.");
        return errors;
    }

    public async Task<LoreDefinition?> MutateAsync(string id, string operation, LoreSettings draft, DateTimeOffset? expected, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var existing = await Load(connection, transaction, id, true, ct);
        if (existing?.UpdatedAtUtc != expected) throw new LoreConcurrencyException();
        var sql = operation == "save_draft"
        ? existing is null
        ? "INSERT INTO lore_definitions(definition_id,settings) VALUES(@id,@settings)"
        : "UPDATE lore_definitions SET settings=@settings,publication_state='Draft',updated_at=clock_timestamp() WHERE definition_id=@id"
        : "UPDATE lore_definitions SET published_settings=settings,publication_state='Published',updated_at=clock_timestamp() WHERE definition_id=@id";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("settings", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(draft));
        await command.ExecuteNonQueryAsync(ct);
        var result = await Load(connection, transaction, id, false, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
public sealed class LoreConcurrencyException : Exception;
