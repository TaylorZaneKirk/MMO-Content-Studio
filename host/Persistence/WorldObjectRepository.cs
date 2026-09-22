// Reads and transactionally writes one reusable definition and its ordered children.
// The parent row lock and timestamp protect the entire authoring aggregate.
using System.Data;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class WorldObjectRepository(AuthoringDatabaseConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<WorldObjectSummary>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT definition_id, display_name, publication_state FROM world_object_definitions
            WHERE @search = '' OR definition_id ILIKE '%' || @search || '%' OR display_name ILIKE '%' || @search || '%'
            ORDER BY display_name, definition_id
            """, connection);
        command.Parameters.AddWithValue("search", search?.Trim() ?? "");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<WorldObjectSummary>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return items;
    }

    public async Task<WorldObjectDefinition?> LoadAsync(string definitionId, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        // All three reads see the same committed aggregate.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        return await LoadAsync(connection, transaction, definitionId, false, cancellationToken);
    }

    private static async Task<WorldObjectDefinition?> LoadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string definitionId, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT display_name, blocks_movement, footprint_width_tiles, footprint_height_tiles,
                   visual_texture_path, source_width, source_height, visual_anchor_offset_x,
                   visual_anchor_offset_y, visual_render_scale, visual_animation_fps,
                   publication_state, updated_at_utc
            FROM world_object_definitions WHERE definition_id = @id
            """ + (forUpdate ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue("id", definitionId);
        WorldObjectDraft draft;
        string state;
        DateTimeOffset updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            draft = new(reader.GetString(0), reader.GetBoolean(1), reader.GetInt32(2), reader.GetInt32(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetDouble(7), reader.GetDouble(8),
                reader.GetDouble(9), reader.IsDBNull(10) ? null : reader.GetDouble(10), [], []);
            state = reader.GetString(11);
            updated = reader.GetFieldValue<DateTimeOffset>(12);
        }
        var interactions = new List<WorldObjectInteraction>();
        command.CommandText = "SELECT action_id, label, is_default FROM world_object_interactions WHERE definition_id = @id ORDER BY interaction_order";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                interactions.Add(new(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
        var frames = new List<string>();
        command.CommandText = "SELECT texture_path FROM world_object_animation_frames WHERE definition_id = @id ORDER BY frame_order";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) frames.Add(reader.GetString(0));
        return new(definitionId, state, draft with { PublicInteractions = interactions, VisualAnimationFrames = frames }, updated);
    }

    // Validate the expected version under the parent lock, replace ordered children
    // for a draft save, then reload before commit. Unique IDs guard concurrent creates.
    public async Task<WorldObjectDefinition?> MutateAsync(string definitionId, string operation,
        WorldObjectDraft draft, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, definitionId, true, cancellationToken);
        if (existing?.UpdatedAtUtc != expectedUpdatedAtUtc || (existing is null && operation != "save_draft"))
            throw new WorldObjectConcurrencyException();
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
        command.Parameters.AddWithValue("id", definitionId);
        if (operation == "delete")
        {
            if (existing!.PublicationState == "Published") throw new WorldObjectConcurrencyException();
            command.CommandText = "DELETE FROM world_object_definitions WHERE definition_id = @id";
        }
        else if (operation == "save_draft")
        {
            command.CommandText = """
                INSERT INTO world_object_definitions (definition_id, display_name, publication_state, blocks_movement,
                    footprint_width_tiles, footprint_height_tiles, visual_texture_path, source_width, source_height,
                    visual_anchor_offset_x, visual_anchor_offset_y, visual_render_scale, visual_animation_fps)
                VALUES (@id, @name, 'Draft', @blocks, @fw, @fh, @texture, @sw, @sh, @x, @y, @scale, @fps)
                """;
            if (existing is not null)
                command.CommandText = """
                    UPDATE world_object_definitions SET display_name=@name, publication_state='Draft', blocks_movement=@blocks,
                        footprint_width_tiles=@fw, footprint_height_tiles=@fh, visual_texture_path=@texture,
                        source_width=@sw, source_height=@sh, visual_anchor_offset_x=@x, visual_anchor_offset_y=@y,
                        visual_render_scale=@scale, visual_animation_fps=@fps,
                        updated_at_utc=greatest(clock_timestamp(), updated_at_utc + interval '1 microsecond') WHERE definition_id=@id
                    """;
            command.Parameters.AddWithValue("name", draft.DisplayName);
            command.Parameters.AddWithValue("blocks", draft.BlocksMovement);
            command.Parameters.AddWithValue("fw", draft.FootprintWidthTiles);
            command.Parameters.AddWithValue("fh", draft.FootprintHeightTiles);
            command.Parameters.AddWithValue("texture", draft.VisualTexturePath);
            command.Parameters.AddWithValue("sw", draft.SourceWidth);
            command.Parameters.AddWithValue("sh", draft.SourceHeight);
            command.Parameters.AddWithValue("x", draft.VisualAnchorOffsetX);
            command.Parameters.AddWithValue("y", draft.VisualAnchorOffsetY);
            command.Parameters.AddWithValue("scale", draft.VisualRenderScale);
            command.Parameters.Add("fps", NpgsqlDbType.Double).Value = (object?)draft.VisualAnimationFps ?? DBNull.Value;
        }
        else
        {
            command.CommandText = """
                UPDATE world_object_definitions SET publication_state=@state,
                    updated_at_utc=greatest(clock_timestamp(), updated_at_utc + interval '1 microsecond') WHERE definition_id=@id
                """;
            command.Parameters.AddWithValue("state", operation == "publish" ? "Published" : "Disabled");
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (operation == "save_draft")
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("id", definitionId);
            command.CommandText = "DELETE FROM world_object_interactions WHERE definition_id=@id; DELETE FROM world_object_animation_frames WHERE definition_id=@id";
            await command.ExecuteNonQueryAsync(cancellationToken);
            for (var index = 0; index < draft.PublicInteractions.Count; index++)
            {
                var action = draft.PublicInteractions[index];
                await using var child = new NpgsqlCommand("INSERT INTO world_object_interactions VALUES (@id,@index,@action,@label,@default)", connection, transaction);
                child.Parameters.AddWithValue("id", definitionId);
                child.Parameters.AddWithValue("index", index);
                child.Parameters.AddWithValue("action", action.ActionId);
                child.Parameters.AddWithValue("label", action.Label);
                child.Parameters.AddWithValue("default", action.IsDefault);
                await child.ExecuteNonQueryAsync(cancellationToken);
            }
            for (var index = 0; index < draft.VisualAnimationFrames.Count; index++)
            {
                await using var child = new NpgsqlCommand("INSERT INTO world_object_animation_frames VALUES (@id,@index,@path)", connection, transaction);
                child.Parameters.AddWithValue("id", definitionId);
                child.Parameters.AddWithValue("index", index);
                child.Parameters.AddWithValue("path", draft.VisualAnimationFrames[index]);
                await child.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        var saved = await LoadAsync(connection, transaction, definitionId, false, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }
}

public sealed class WorldObjectConcurrencyException : Exception;
