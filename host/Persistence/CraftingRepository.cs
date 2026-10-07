using System.Data;
using System.Text.Json;
using MMO.Project.Crafting;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;
namespace MMO.ContentStudio.AuthoringHost.Persistence;
// One editable rules document; published_settings retains the last reviewed
// publication while a replacement draft is edited.
public sealed class CraftingRepository(AuthoringDatabaseConnectionFactory factory)
{
    public async Task<IReadOnlyList<CraftingSummary>> ListAsync(string? search, CancellationToken ct)
    {
        var row = await LoadAsync("v1", ct);
        return row is null ? [] : [new("v1", "Crafting V1", row.PublicationState)];
    }
    public async Task<CraftingDefinition?> LoadAsync(string id, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        return await Load(connection, null, id, false, ct);
    }
    private static async Task<CraftingDefinition?> Load(NpgsqlConnection connection, NpgsqlTransaction? transaction, string id, bool locked, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT settings,publication_state,updated_at FROM crafting_definitions WHERE definition_id=@id" + (locked ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(id, reader.GetString(1), JsonSerializer.Deserialize<CraftingSettings>(reader.GetString(0))!, reader.GetFieldValue<DateTimeOffset>(2));
    }
    public async Task<IReadOnlyList<string>> ValidatePublishedAsync(CraftingSettings settings, CancellationToken ct)
    {
        var shapeErrors = settings.Validate();
        if (shapeErrors.Count > 0) return shapeErrors;
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT item_id,stackable FROM item_definitions WHERE runtime_enabled", connection);
        var items = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) items.Add(reader.GetString(0), reader.GetBoolean(1));
        var errors = settings.Validate(items.Keys.ToHashSet(StringComparer.Ordinal)).Concat(settings.ValidateItemShapes(items)).ToList();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM world_object_definitions w JOIN world_object_interactions x USING(definition_id) WHERE w.definition_id=@station AND w.publication_state='Published' AND x.action_id='crafting' AND w.blocks_movement AND w.footprint_width_tiles=1 AND w.footprint_height_tiles=1)";
        command.Parameters.AddWithValue("station", settings.StationDefinitionId);
        if (await command.ExecuteScalarAsync(ct) is not true) errors.Add("Publish an Crafting station with the crafting action first.");
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM item_definitions WHERE item_id=@chair AND trade_policy='tradeable')";
        command.Parameters.AddWithValue("chair", settings.ChairItemId);
        if (await command.ExecuteScalarAsync(ct) is not true) errors.Add("The chair must be tradeable.");
        return errors;
    }

    public async Task<IReadOnlyList<string>> ValidateRetainedFurnitureAsync(string operation, CraftingSettings draft, CancellationToken ct)
    {
        if (operation == "save_draft") return [];
        await using var connection = await factory.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT published_settings FROM crafting_definitions WHERE definition_id='v1' AND EXISTS(SELECT 1 FROM character_home_furniture)", connection);
        var json = await command.ExecuteScalarAsync(ct) as string;
        if (json is null) return [];
        if (operation is "disable" or "delete") return ["Pick up retained home furniture before disabling or deleting Crafting."];
        var old = JsonSerializer.Deserialize<CraftingSettings>(json)!;
        if (!old.ItemIds.SequenceEqual(draft.ItemIds) || old.StationDefinitionId != draft.StationDefinitionId ||
            old.EastTexturePath != draft.EastTexturePath || old.WestTexturePath != draft.WestTexturePath ||
            old.FootprintWidthTiles != draft.FootprintWidthTiles || old.FootprintHeightTiles != draft.FootprintHeightTiles ||
            old.OccupiesFurnitureSpace != draft.OccupiesFurnitureSpace || old.BlocksMovement != draft.BlocksMovement)
            return ["Retained furniture prevents changing published item bindings, footprint, occupancy or directional art."];
        return [];
    }

    public async Task<CraftingDefinition?> MutateAsync(string id, string operation, CraftingSettings draft, DateTimeOffset? expected, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var existing = await Load(connection, transaction, id, true, ct);
        if (existing?.UpdatedAtUtc != expected) throw new CraftingConcurrencyException();
        var sql = operation switch
        {
            "save_draft" when existing is null => "INSERT INTO crafting_definitions(definition_id,settings) VALUES(@id,@settings)",
            "save_draft" => "UPDATE crafting_definitions SET settings=@settings,publication_state='Draft',updated_at=clock_timestamp() WHERE definition_id=@id",
            "publish" => "UPDATE crafting_definitions SET published_settings=settings,publication_state='Published',updated_at=clock_timestamp() WHERE definition_id=@id",
            "disable" => "UPDATE crafting_definitions SET published_settings=NULL,publication_state='Disabled',updated_at=clock_timestamp() WHERE definition_id=@id",
            "delete" => "DELETE FROM crafting_definitions WHERE definition_id=@id",
            _ => throw new InvalidOperationException("Unsupported Crafting operation.")
        };
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("settings", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(draft));
        await command.ExecuteNonQueryAsync(ct);
        var result = await Load(connection, transaction, id, false, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
public sealed class CraftingConcurrencyException : Exception;
