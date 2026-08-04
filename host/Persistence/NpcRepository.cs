using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public interface INpcRepository
{
    Task<IReadOnlyList<NpcDefinitionRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default);

    Task<NpcDefinitionRecord?> LoadAsync(
        string npcDefinitionId,
        CancellationToken cancellationToken = default);

    Task<NpcDefinitionRecord?> LoadForUpdateAsync(
        string npcDefinitionId,
        CancellationToken cancellationToken = default);

    Task<NpcDefinitionRecord> SaveDraftAsync(
        string npcDefinitionId,
        NpcDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<NpcDefinitionRecord> SetPublicationAsync(
        string npcDefinitionId,
        string publicationState,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string npcDefinitionId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<NpcReferenceSummaryRecord> LoadKnownSpawnReferencesAsync(
        string npcDefinitionId,
        CancellationToken cancellationToken = default);
}

public sealed class NpcRepository : INpcRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public NpcRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<NpcDefinitionRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                npc_definition_id,
                display_name,
                publication_state,
                visual_texture_path,
                source_width,
                source_height,
                visual_anchor_offset_x,
                visual_anchor_offset_y,
                visual_render_scale,
                footprint_width_tiles,
                footprint_height_tiles,
                movement_behavior,
                wander_radius_tiles,
                tick_interval_ms,
                idle_chance,
                interaction_enabled,
                interaction_range_tiles,
                default_interaction,
                default_dialogue_id,
                notes,
                created_at_utc,
                updated_at_utc
            from npc_definitions
            where @search is null
               or npc_definition_id ilike '%' || @search || '%'
               or display_name ilike '%' || @search || '%'
            order by display_name, npc_definition_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<NpcDefinitionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public async Task<NpcDefinitionRecord?> LoadAsync(
        string npcDefinitionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAsync(connection, null, npcDefinitionId, false, cancellationToken);
    }

    public async Task<NpcDefinitionRecord?> LoadForUpdateAsync(
        string npcDefinitionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var record = await LoadAsync(connection, transaction, npcDefinitionId, true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<NpcDefinitionRecord> SaveDraftAsync(
        string npcDefinitionId,
        NpcDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, npcDefinitionId, true, cancellationToken);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, npcDefinitionId);

        if (existing is null)
        {
            await InsertAsync(connection, transaction, npcDefinitionId, draft, cancellationToken);
        }
        else
        {
            await UpdateAsync(connection, transaction, npcDefinitionId, draft, cancellationToken);
        }

        var saved = await LoadAsync(connection, transaction, npcDefinitionId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved NPC definition could not be reloaded inside its transaction.");
        if (!NpcAuthoringService.EquivalentDraft(saved, draft) || saved.PublicationState != "Draft")
        {
            throw new InvalidOperationException("Saved NPC definition failed transactional semantic verification.");
        }

        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<NpcDefinitionRecord> SetPublicationAsync(
        string npcDefinitionId,
        string publicationState,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, npcDefinitionId, true, cancellationToken)
            ?? throw new NpcDefinitionNotFoundException(npcDefinitionId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, npcDefinitionId);

        const string sql = """
            update npc_definitions
            set publication_state = @publication_state,
                updated_at_utc = now()
            where npc_definition_id = @npc_definition_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("npc_definition_id", npcDefinitionId);
            command.Parameters.AddWithValue("publication_state", publicationState);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var saved = await LoadAsync(connection, transaction, npcDefinitionId, false, cancellationToken)
            ?? throw new InvalidOperationException("NPC publication change could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(
        string npcDefinitionId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, npcDefinitionId, true, cancellationToken)
            ?? throw new NpcDefinitionNotFoundException(npcDefinitionId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, npcDefinitionId);
        if (existing.PublicationState != "Disabled")
        {
            throw new NpcDefinitionDeleteRequiresDisabledException(npcDefinitionId);
        }

        const string sql = "delete from npc_definitions where npc_definition_id = @npc_definition_id;";
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("npc_definition_id", npcDefinitionId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<NpcReferenceSummaryRecord> LoadKnownSpawnReferencesAsync(
        string npcDefinitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select c.map_id
            from world_region_chunks c
            where exists (
                select 1
                from jsonb_array_elements(coalesce(c.chunk_json->'npc_spawns', '[]'::jsonb)) spawn
                where spawn->>'npc_definition_id' = @npc_definition_id
                   or spawn->'properties'->>'npc_definition_id' = @npc_definition_id
            )
            order by c.map_id;
            """;

        var sources = new List<string>();
        try
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add("npc_definition_id", NpgsqlDbType.Text).Value = npcDefinitionId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                sources.Add($"database:world_region_chunks:{reader.GetString(0)}");
            }
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return new NpcReferenceSummaryRecord(
                npcDefinitionId,
                0,
                [],
                false);
        }

        return new NpcReferenceSummaryRecord(
            npcDefinitionId,
            sources.Count,
            sources,
            true);
    }

    private static async Task<NpcDefinitionRecord?> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string npcDefinitionId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            select
                npc_definition_id,
                display_name,
                publication_state,
                visual_texture_path,
                source_width,
                source_height,
                visual_anchor_offset_x,
                visual_anchor_offset_y,
                visual_render_scale,
                footprint_width_tiles,
                footprint_height_tiles,
                movement_behavior,
                wander_radius_tiles,
                tick_interval_ms,
                idle_chance,
                interaction_enabled,
                interaction_range_tiles,
                default_interaction,
                default_dialogue_id,
                notes,
                created_at_utc,
                updated_at_utc
            from npc_definitions
            where npc_definition_id = @npc_definition_id
            """ + (forUpdate ? " for update;" : ";");

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("npc_definition_id", npcDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string npcDefinitionId,
        NpcDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into npc_definitions (
                npc_definition_id,
                display_name,
                publication_state,
                visual_texture_path,
                source_width,
                source_height,
                visual_anchor_offset_x,
                visual_anchor_offset_y,
                visual_render_scale,
                footprint_width_tiles,
                footprint_height_tiles,
                movement_behavior,
                wander_radius_tiles,
                tick_interval_ms,
                idle_chance,
                interaction_enabled,
                interaction_range_tiles,
                default_interaction,
                default_dialogue_id,
                notes,
                created_at_utc,
                updated_at_utc
            ) values (
                @npc_definition_id,
                @display_name,
                'Draft',
                @visual_texture_path,
                @source_width,
                @source_height,
                @visual_anchor_offset_x,
                @visual_anchor_offset_y,
                @visual_render_scale,
                @footprint_width_tiles,
                @footprint_height_tiles,
                @movement_behavior,
                @wander_radius_tiles,
                @tick_interval_ms,
                @idle_chance,
                @interaction_enabled,
                @interaction_range_tiles,
                @default_interaction,
                @default_dialogue_id,
                @notes,
                now(),
                now()
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(command, npcDefinitionId, draft);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string npcDefinitionId,
        NpcDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update npc_definitions
            set display_name = @display_name,
                publication_state = 'Draft',
                visual_texture_path = @visual_texture_path,
                source_width = @source_width,
                source_height = @source_height,
                visual_anchor_offset_x = @visual_anchor_offset_x,
                visual_anchor_offset_y = @visual_anchor_offset_y,
                visual_render_scale = @visual_render_scale,
                footprint_width_tiles = @footprint_width_tiles,
                footprint_height_tiles = @footprint_height_tiles,
                movement_behavior = @movement_behavior,
                wander_radius_tiles = @wander_radius_tiles,
                tick_interval_ms = @tick_interval_ms,
                idle_chance = @idle_chance,
                interaction_enabled = @interaction_enabled,
                interaction_range_tiles = @interaction_range_tiles,
                default_interaction = @default_interaction,
                default_dialogue_id = @default_dialogue_id,
                notes = @notes,
                updated_at_utc = now()
            where npc_definition_id = @npc_definition_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(command, npcDefinitionId, draft);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException($"Expected to update one NPC definition '{npcDefinitionId}', but updated {affected} rows.");
        }
    }

    private static void AddParameters(
        NpgsqlCommand command,
        string npcDefinitionId,
        NpcDraft draft)
    {
        command.Parameters.AddWithValue("npc_definition_id", npcDefinitionId);
        command.Parameters.AddWithValue("display_name", draft.DisplayName);
        command.Parameters.AddWithValue("visual_texture_path", draft.VisualTexturePath);
        command.Parameters.AddWithValue("source_width", draft.SourceWidth);
        command.Parameters.AddWithValue("source_height", draft.SourceHeight);
        command.Parameters.AddWithValue("visual_anchor_offset_x", draft.VisualAnchorOffsetX);
        command.Parameters.AddWithValue("visual_anchor_offset_y", draft.VisualAnchorOffsetY);
        command.Parameters.AddWithValue("visual_render_scale", draft.VisualRenderScale);
        command.Parameters.AddWithValue("footprint_width_tiles", draft.FootprintWidthTiles);
        command.Parameters.AddWithValue("footprint_height_tiles", draft.FootprintHeightTiles);
        command.Parameters.AddWithValue("movement_behavior", draft.MovementBehavior);
        command.Parameters.AddWithValue("wander_radius_tiles", draft.WanderRadiusTiles);
        command.Parameters.AddWithValue("tick_interval_ms", draft.TickIntervalMs);
        command.Parameters.AddWithValue("idle_chance", draft.IdleChance);
        command.Parameters.AddWithValue("interaction_enabled", draft.InteractionEnabled);
        command.Parameters.AddWithValue("interaction_range_tiles", draft.InteractionRangeTiles);
        command.Parameters.AddWithValue("default_interaction", draft.DefaultInteraction);
        command.Parameters.Add("default_dialogue_id", NpgsqlDbType.Text).Value =
            (object?)draft.DefaultDialogueId ?? DBNull.Value;
        command.Parameters.Add("notes", NpgsqlDbType.Text).Value =
            (object?)draft.Notes ?? DBNull.Value;
    }

    private static NpcDefinitionRecord ReadRecord(NpgsqlDataReader reader)
    {
        var dialogueOrdinal = reader.GetOrdinal("default_dialogue_id");
        var notesOrdinal = reader.GetOrdinal("notes");
        return new NpcDefinitionRecord(
            reader.GetString(reader.GetOrdinal("npc_definition_id")),
            reader.GetString(reader.GetOrdinal("display_name")),
            reader.GetString(reader.GetOrdinal("publication_state")),
            reader.GetString(reader.GetOrdinal("visual_texture_path")),
            reader.GetInt32(reader.GetOrdinal("source_width")),
            reader.GetInt32(reader.GetOrdinal("source_height")),
            reader.GetDouble(reader.GetOrdinal("visual_anchor_offset_x")),
            reader.GetDouble(reader.GetOrdinal("visual_anchor_offset_y")),
            reader.GetDouble(reader.GetOrdinal("visual_render_scale")),
            reader.GetInt32(reader.GetOrdinal("footprint_width_tiles")),
            reader.GetInt32(reader.GetOrdinal("footprint_height_tiles")),
            reader.GetString(reader.GetOrdinal("movement_behavior")),
            reader.GetInt32(reader.GetOrdinal("wander_radius_tiles")),
            reader.GetInt32(reader.GetOrdinal("tick_interval_ms")),
            reader.GetDouble(reader.GetOrdinal("idle_chance")),
            reader.GetBoolean(reader.GetOrdinal("interaction_enabled")),
            reader.GetInt32(reader.GetOrdinal("interaction_range_tiles")),
            reader.GetString(reader.GetOrdinal("default_interaction")),
            reader.IsDBNull(dialogueOrdinal) ? null : reader.GetString(dialogueOrdinal),
            reader.IsDBNull(notesOrdinal) ? null : reader.GetString(notesOrdinal),
            ReadUtc(reader, "created_at_utc"),
            ReadUtc(reader, "updated_at_utc"));
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(reader.GetOrdinal(column)), DateTimeKind.Utc));

    private static void EnsureExpectedVersion(
        NpcDefinitionRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc,
        string npcDefinitionId)
    {
        if (existing is null)
        {
            if (expectedUpdatedAtUtc is not null)
            {
                throw new NpcDefinitionConcurrencyException(npcDefinitionId, null);
            }

            return;
        }

        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new NpcDefinitionConcurrencyException(existing.NpcDefinitionId, existing.UpdatedAtUtc);
        }
    }
}

public sealed record NpcDefinitionRecord(
    string NpcDefinitionId,
    string DisplayName,
    string PublicationState,
    string VisualTexturePath,
    int SourceWidth,
    int SourceHeight,
    double VisualAnchorOffsetX,
    double VisualAnchorOffsetY,
    double VisualRenderScale,
    int FootprintWidthTiles,
    int FootprintHeightTiles,
    string MovementBehavior,
    int WanderRadiusTiles,
    int TickIntervalMs,
    double IdleChance,
    bool InteractionEnabled,
    int InteractionRangeTiles,
    string DefaultInteraction,
    string? DefaultDialogueId,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NpcReferenceSummaryRecord(
    string NpcDefinitionId,
    int KnownReferenceCount,
    IReadOnlyList<string> ReferenceSources,
    bool ReferenceCheckComplete);

public sealed class NpcDefinitionNotFoundException : Exception
{
    public NpcDefinitionNotFoundException(string npcDefinitionId)
        : base($"NPC definition '{npcDefinitionId}' does not exist.")
    {
    }
}

public sealed class NpcDefinitionConcurrencyException : Exception
{
    public NpcDefinitionConcurrencyException(
        string npcDefinitionId,
        DateTimeOffset? currentUpdatedAtUtc)
        : base($"NPC definition '{npcDefinitionId}' changed after it was loaded. Reload it before saving.")
    {
        CurrentUpdatedAtUtc = currentUpdatedAtUtc;
    }

    public DateTimeOffset? CurrentUpdatedAtUtc { get; }
}

public sealed class NpcDefinitionDeleteRequiresDisabledException : Exception
{
    public NpcDefinitionDeleteRequiresDisabledException(string npcDefinitionId)
        : base($"NPC definition '{npcDefinitionId}' must be disabled before deletion.")
    {
    }
}
