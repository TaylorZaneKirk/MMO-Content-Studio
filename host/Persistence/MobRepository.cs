using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class MobRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public MobRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<MobDefinitionRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                m.mob_definition_id,
                m.display_name,
                m.publication_state,
                m.visual_texture_path,
                m.source_width,
                m.source_height,
                m.visual_anchor_offset_x,
                m.visual_anchor_offset_y,
                m.visual_render_scale,
                m.footprint_width_tiles,
                m.footprint_height_tiles,
                m.max_health,
                m.movement_speed_tiles_per_second,
                m.movement_behavior,
                m.wander_radius_tiles,
                m.aggression_mode,
                m.aggression_radius_tiles,
                m.leash_radius_tiles,
                m.return_home_behavior,
                m.combat_faction_id,
                f.display_name as combat_faction_display_name,
                m.can_proactively_target_hostile_mobs,
                m.mob_detection_radius_tiles,
                m.mob_target_scan_interval_ms,
                m.mob_target_scan_candidate_limit,
                m.updated_at,
                exists (
                    select 1 from mob_combat_profiles profile
                    where profile.mob_definition_id = m.mob_definition_id
                ) as has_combat_profile,
                (
                    select count(*)::int
                    from mob_drops d
                    where d.mob_definition_id = m.mob_definition_id
                ) as guaranteed_drop_count
            from mob_definitions m
            left join mob_factions f on f.faction_id = m.combat_faction_id
            where @search is null
               or m.mob_definition_id ilike '%' || @search || '%'
               or m.display_name ilike '%' || @search || '%'
            order by m.display_name, m.mob_definition_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<MobDefinitionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadBaseRecord(reader, null, null, [], reader.GetBoolean(reader.GetOrdinal("has_combat_profile"))));
        }

        return records;
    }

    public async Task<MobDefinitionRecord?> LoadAsync(
        string mobDefinitionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAggregateAsync(connection, null, mobDefinitionId, false, cancellationToken);
    }

    public async Task<IReadOnlyList<MobFactionRecord>> LoadFactionsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select faction_id, display_name
            from mob_factions
            order by display_name, faction_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<MobFactionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new MobFactionRecord(
                reader.GetString(reader.GetOrdinal("faction_id")),
                reader.GetString(reader.GetOrdinal("display_name"))));
        }

        return records;
    }

    public async Task<IReadOnlyList<MobDropItemRecord>> LoadDropItemsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select item_id, item_name, runtime_enabled
            from item_definitions
            order by item_name, item_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<MobDropItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new MobDropItemRecord(
                reader.GetString(reader.GetOrdinal("item_id")),
                reader.GetString(reader.GetOrdinal("item_name")),
                reader.GetBoolean(reader.GetOrdinal("runtime_enabled"))));
        }

        return records;
    }

    public async Task<MobDefinitionRecord> SaveDraftAsync(
        string mobDefinitionId,
        NormalizedMobDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, mobDefinitionId, true, cancellationToken);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, mobDefinitionId);

        if (existing is null)
        {
            await InsertRootAsync(connection, transaction, mobDefinitionId, draft, "Draft", cancellationToken);
        }
        else
        {
            await UpdateRootAsync(connection, transaction, mobDefinitionId, draft, "Draft", cancellationToken);
        }

        await ReplaceCombatProfileAsync(connection, transaction, mobDefinitionId, draft.PrimaryCombatProfile, cancellationToken);
        await ReplaceCombatBonusesAsync(connection, transaction, mobDefinitionId, draft.CombatBonuses, cancellationToken);
        await ReplaceGuaranteedDropsAsync(connection, transaction, mobDefinitionId, draft.GuaranteedDrops, cancellationToken);

        var saved = await LoadAggregateAsync(connection, transaction, mobDefinitionId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved mob definition could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<MobDefinitionRecord> SetPublicationAsync(
        string mobDefinitionId,
        string publicationState,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, mobDefinitionId, true, cancellationToken)
            ?? throw new MobDefinitionNotFoundException(mobDefinitionId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, mobDefinitionId);

        const string sql = """
            update mob_definitions
            set publication_state = @publication_state,
                updated_at = now()
            where mob_definition_id = @mob_definition_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
            command.Parameters.AddWithValue("publication_state", publicationState);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var saved = await LoadAggregateAsync(connection, transaction, mobDefinitionId, false, cancellationToken)
            ?? throw new InvalidOperationException("Mob publication change could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(
        string mobDefinitionId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, mobDefinitionId, true, cancellationToken)
            ?? throw new MobDefinitionNotFoundException(mobDefinitionId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, mobDefinitionId);

        await ExecuteDeleteAsync(connection, transaction, "mob_drops", mobDefinitionId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "mob_combat_bonuses", mobDefinitionId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "mob_combat_profiles", mobDefinitionId, cancellationToken);

        const string sql = "delete from mob_definitions where mob_definition_id = @mob_definition_id;";
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<MobDefinitionRecord?> LoadAggregateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string mobDefinitionId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var baseRecord = await LoadBaseAsync(connection, transaction, mobDefinitionId, forUpdate, cancellationToken);
        if (baseRecord is null)
        {
            return null;
        }

        return baseRecord with
        {
            PrimaryCombatProfile = await LoadCombatProfileAsync(connection, transaction, mobDefinitionId, cancellationToken),
            CombatBonuses = await LoadCombatBonusesAsync(connection, transaction, mobDefinitionId, cancellationToken),
            GuaranteedDrops = await LoadGuaranteedDropsAsync(connection, transaction, mobDefinitionId, cancellationToken)
        };
    }

    private static async Task<MobDefinitionRecord?> LoadBaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string mobDefinitionId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            select
                m.mob_definition_id,
                m.display_name,
                m.publication_state,
                m.visual_texture_path,
                m.source_width,
                m.source_height,
                m.visual_anchor_offset_x,
                m.visual_anchor_offset_y,
                m.visual_render_scale,
                m.footprint_width_tiles,
                m.footprint_height_tiles,
                m.max_health,
                m.movement_speed_tiles_per_second,
                m.movement_behavior,
                m.wander_radius_tiles,
                m.aggression_mode,
                m.aggression_radius_tiles,
                m.leash_radius_tiles,
                m.return_home_behavior,
                m.combat_faction_id,
                f.display_name as combat_faction_display_name,
                m.can_proactively_target_hostile_mobs,
                m.mob_detection_radius_tiles,
                m.mob_target_scan_interval_ms,
                m.mob_target_scan_candidate_limit,
                m.updated_at,
                exists (
                    select 1 from mob_combat_profiles profile
                    where profile.mob_definition_id = m.mob_definition_id
                ) as has_combat_profile,
                (
                    select count(*)::int
                    from mob_drops d
                    where d.mob_definition_id = m.mob_definition_id
                ) as guaranteed_drop_count
            from mob_definitions m
            left join mob_factions f on f.faction_id = m.combat_faction_id
            where m.mob_definition_id = @mob_definition_id
            """ + (forUpdate ? " for update of m;" : ";");

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadBaseRecord(reader, null, null, [], reader.GetBoolean(reader.GetOrdinal("has_combat_profile")))
            : null;
    }

    private static async Task<MobCombatProfileDefinition?> LoadCombatProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string mobDefinitionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select attack_type, accuracy_style, minimum_range_tiles, maximum_range_tiles,
                attack_speed_units, attack_level, strength_level, defence_level
            from mob_combat_profiles
            where mob_definition_id = @mob_definition_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var accuracyStyleOrdinal = reader.GetOrdinal("accuracy_style");
        return new MobCombatProfileDefinition(
            reader.GetString(reader.GetOrdinal("attack_type")),
            reader.IsDBNull(accuracyStyleOrdinal) ? null : reader.GetString(accuracyStyleOrdinal),
            reader.GetInt32(reader.GetOrdinal("minimum_range_tiles")),
            reader.GetInt32(reader.GetOrdinal("maximum_range_tiles")),
            reader.GetInt32(reader.GetOrdinal("attack_speed_units")),
            reader.GetInt32(reader.GetOrdinal("attack_level")),
            reader.GetInt32(reader.GetOrdinal("strength_level")),
            reader.GetInt32(reader.GetOrdinal("defence_level")));
    }

    private static async Task<EquipmentCombatBonusDefinition?> LoadCombatBonusesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string mobDefinitionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select attack_thrust, attack_slash, attack_crush, attack_ranged, attack_magic,
                strength_melee, strength_ranged, strength_magic,
                defence_thrust, defence_slash, defence_crush, defence_ranged, defence_magic
            from mob_combat_bonuses
            where mob_definition_id = @mob_definition_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new EquipmentCombatBonusDefinition(
                reader.GetInt32(reader.GetOrdinal("attack_thrust")),
                reader.GetInt32(reader.GetOrdinal("attack_slash")),
                reader.GetInt32(reader.GetOrdinal("attack_crush")),
                reader.GetInt32(reader.GetOrdinal("attack_ranged")),
                reader.GetInt32(reader.GetOrdinal("attack_magic")),
                reader.GetInt32(reader.GetOrdinal("strength_melee")),
                reader.GetInt32(reader.GetOrdinal("strength_ranged")),
                reader.GetInt32(reader.GetOrdinal("strength_magic")),
                reader.GetInt32(reader.GetOrdinal("defence_thrust")),
                reader.GetInt32(reader.GetOrdinal("defence_slash")),
                reader.GetInt32(reader.GetOrdinal("defence_crush")),
                reader.GetInt32(reader.GetOrdinal("defence_ranged")),
                reader.GetInt32(reader.GetOrdinal("defence_magic")))
            : null;
    }

    private static async Task<IReadOnlyList<MobDropDefinition>> LoadGuaranteedDropsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string mobDefinitionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select d.drop_order, d.item_id, i.item_name, d.stack_count
            from mob_drops d
            join item_definitions i on i.item_id = d.item_id
            where d.mob_definition_id = @mob_definition_id
            order by d.drop_order, d.item_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<MobDropDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new MobDropDefinition(
                reader.GetInt32(reader.GetOrdinal("drop_order")),
                reader.GetString(reader.GetOrdinal("item_id")),
                reader.GetString(reader.GetOrdinal("item_name")),
                reader.GetInt32(reader.GetOrdinal("stack_count"))));
        }

        return records;
    }

    private static async Task InsertRootAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string mobDefinitionId,
        NormalizedMobDraft draft,
        string publicationState,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into mob_definitions (
                mob_definition_id,
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
                max_health,
                movement_speed_tiles_per_second,
                movement_behavior,
                wander_radius_tiles,
                aggression_mode,
                aggression_radius_tiles,
                leash_radius_tiles,
                return_home_behavior,
                combat_faction_id,
                can_proactively_target_hostile_mobs,
                mob_detection_radius_tiles,
                mob_target_scan_interval_ms,
                mob_target_scan_candidate_limit,
                created_at,
                updated_at
            ) values (
                @mob_definition_id,
                @display_name,
                @publication_state,
                @visual_texture_path,
                @source_width,
                @source_height,
                @visual_anchor_offset_x,
                @visual_anchor_offset_y,
                @visual_render_scale,
                @footprint_width_tiles,
                @footprint_height_tiles,
                @max_health,
                @movement_speed_tiles_per_second,
                @movement_behavior,
                @wander_radius_tiles,
                @aggression_mode,
                @aggression_radius_tiles,
                @leash_radius_tiles,
                @return_home_behavior,
                @combat_faction_id,
                @can_proactively_target_hostile_mobs,
                @mob_detection_radius_tiles,
                @mob_target_scan_interval_ms,
                @mob_target_scan_candidate_limit,
                now(),
                now()
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddRootParameters(command, mobDefinitionId, draft, publicationState);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateRootAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string mobDefinitionId,
        NormalizedMobDraft draft,
        string publicationState,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update mob_definitions
            set display_name = @display_name,
                publication_state = @publication_state,
                visual_texture_path = @visual_texture_path,
                source_width = @source_width,
                source_height = @source_height,
                visual_anchor_offset_x = @visual_anchor_offset_x,
                visual_anchor_offset_y = @visual_anchor_offset_y,
                visual_render_scale = @visual_render_scale,
                footprint_width_tiles = @footprint_width_tiles,
                footprint_height_tiles = @footprint_height_tiles,
                max_health = @max_health,
                movement_speed_tiles_per_second = @movement_speed_tiles_per_second,
                movement_behavior = @movement_behavior,
                wander_radius_tiles = @wander_radius_tiles,
                aggression_mode = @aggression_mode,
                aggression_radius_tiles = @aggression_radius_tiles,
                leash_radius_tiles = @leash_radius_tiles,
                return_home_behavior = @return_home_behavior,
                combat_faction_id = @combat_faction_id,
                can_proactively_target_hostile_mobs = @can_proactively_target_hostile_mobs,
                mob_detection_radius_tiles = @mob_detection_radius_tiles,
                mob_target_scan_interval_ms = @mob_target_scan_interval_ms,
                mob_target_scan_candidate_limit = @mob_target_scan_candidate_limit,
                updated_at = now()
            where mob_definition_id = @mob_definition_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddRootParameters(command, mobDefinitionId, draft, publicationState);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException($"Expected to update one mob definition '{mobDefinitionId}', but updated {affected} rows.");
        }
    }

    private static async Task ReplaceCombatProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string mobDefinitionId,
        MobCombatProfileDefinition? profile,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            await ExecuteDeleteAsync(connection, transaction, "mob_combat_profiles", mobDefinitionId, cancellationToken);
            return;
        }

        const string sql = """
            insert into mob_combat_profiles (
                mob_definition_id,
                attack_type,
                accuracy_style,
                minimum_range_tiles,
                maximum_range_tiles,
                attack_speed_units,
                attack_level,
                strength_level,
                defence_level,
                updated_at
            ) values (
                @mob_definition_id,
                @attack_type,
                @accuracy_style,
                @minimum_range_tiles,
                @maximum_range_tiles,
                @attack_speed_units,
                @attack_level,
                @strength_level,
                @defence_level,
                now()
            )
            on conflict (mob_definition_id) do update set
                attack_type = excluded.attack_type,
                accuracy_style = excluded.accuracy_style,
                minimum_range_tiles = excluded.minimum_range_tiles,
                maximum_range_tiles = excluded.maximum_range_tiles,
                attack_speed_units = excluded.attack_speed_units,
                attack_level = excluded.attack_level,
                strength_level = excluded.strength_level,
                defence_level = excluded.defence_level,
                updated_at = now();
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
        command.Parameters.AddWithValue("attack_type", profile.AttackType);
        command.Parameters.Add("accuracy_style", NpgsqlDbType.Text).Value =
            (object?)profile.AccuracyStyle ?? DBNull.Value;
        command.Parameters.AddWithValue("minimum_range_tiles", profile.MinimumRangeTiles);
        command.Parameters.AddWithValue("maximum_range_tiles", profile.MaximumRangeTiles);
        command.Parameters.AddWithValue("attack_speed_units", profile.AttackSpeedUnits);
        command.Parameters.AddWithValue("attack_level", profile.AttackLevel);
        command.Parameters.AddWithValue("strength_level", profile.StrengthLevel);
        command.Parameters.AddWithValue("defence_level", profile.DefenceLevel);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceCombatBonusesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string mobDefinitionId,
        EquipmentCombatBonusDefinition bonuses,
        CancellationToken cancellationToken)
    {
        if (bonuses.IsZero)
        {
            await ExecuteDeleteAsync(connection, transaction, "mob_combat_bonuses", mobDefinitionId, cancellationToken);
            return;
        }

        const string sql = """
            insert into mob_combat_bonuses (
                mob_definition_id,
                attack_thrust, attack_slash, attack_crush, attack_ranged, attack_magic,
                strength_melee, strength_ranged, strength_magic,
                defence_thrust, defence_slash, defence_crush, defence_ranged, defence_magic,
                updated_at
            ) values (
                @mob_definition_id,
                @attack_thrust, @attack_slash, @attack_crush, @attack_ranged, @attack_magic,
                @strength_melee, @strength_ranged, @strength_magic,
                @defence_thrust, @defence_slash, @defence_crush, @defence_ranged, @defence_magic,
                now()
            )
            on conflict (mob_definition_id) do update set
                attack_thrust = excluded.attack_thrust,
                attack_slash = excluded.attack_slash,
                attack_crush = excluded.attack_crush,
                attack_ranged = excluded.attack_ranged,
                attack_magic = excluded.attack_magic,
                strength_melee = excluded.strength_melee,
                strength_ranged = excluded.strength_ranged,
                strength_magic = excluded.strength_magic,
                defence_thrust = excluded.defence_thrust,
                defence_slash = excluded.defence_slash,
                defence_crush = excluded.defence_crush,
                defence_ranged = excluded.defence_ranged,
                defence_magic = excluded.defence_magic,
                updated_at = now();
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
        AddCombatBonusParameters(command, bonuses);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceGuaranteedDropsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string mobDefinitionId,
        IReadOnlyList<MobDropDraft> drops,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "mob_drops", mobDefinitionId, cancellationToken);
        const string sql = """
            insert into mob_drops (
                mob_definition_id,
                drop_order,
                item_id,
                stack_count,
                updated_at
            ) values (
                @mob_definition_id,
                @drop_order,
                @item_id,
                @stack_count,
                now()
            );
            """;
        foreach (var drop in drops)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
            command.Parameters.AddWithValue("drop_order", drop.DropOrder);
            command.Parameters.AddWithValue("item_id", drop.ItemId);
            command.Parameters.AddWithValue("stack_count", drop.StackCount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ExecuteDeleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string mobDefinitionId,
        CancellationToken cancellationToken)
    {
        var sql = $"delete from {table} where mob_definition_id = @mob_definition_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddRootParameters(
        NpgsqlCommand command,
        string mobDefinitionId,
        NormalizedMobDraft draft,
        string publicationState)
    {
        command.Parameters.AddWithValue("mob_definition_id", mobDefinitionId);
        command.Parameters.AddWithValue("display_name", draft.DisplayName);
        command.Parameters.AddWithValue("publication_state", publicationState);
        command.Parameters.AddWithValue("visual_texture_path", draft.VisualTexturePath);
        command.Parameters.AddWithValue("source_width", draft.SourceWidth);
        command.Parameters.AddWithValue("source_height", draft.SourceHeight);
        command.Parameters.AddWithValue("visual_anchor_offset_x", draft.VisualAnchorOffsetX);
        command.Parameters.AddWithValue("visual_anchor_offset_y", draft.VisualAnchorOffsetY);
        command.Parameters.AddWithValue("visual_render_scale", draft.VisualRenderScale);
        command.Parameters.AddWithValue("footprint_width_tiles", draft.FootprintWidthTiles);
        command.Parameters.AddWithValue("footprint_height_tiles", draft.FootprintHeightTiles);
        command.Parameters.AddWithValue("max_health", draft.MaxHealth);
        command.Parameters.AddWithValue("movement_speed_tiles_per_second", draft.MovementSpeedTilesPerSecond);
        command.Parameters.AddWithValue("movement_behavior", draft.MovementBehavior);
        command.Parameters.AddWithValue("wander_radius_tiles", draft.WanderRadiusTiles);
        command.Parameters.AddWithValue("aggression_mode", draft.AggressionMode);
        command.Parameters.AddWithValue("aggression_radius_tiles", draft.AggressionRadiusTiles);
        command.Parameters.AddWithValue("leash_radius_tiles", draft.LeashRadiusTiles);
        command.Parameters.AddWithValue("return_home_behavior", draft.ReturnHomeBehavior);
        command.Parameters.Add("combat_faction_id", NpgsqlDbType.Text).Value =
            (object?)draft.CombatFactionId ?? DBNull.Value;
        command.Parameters.AddWithValue("can_proactively_target_hostile_mobs", draft.CanProactivelyTargetHostileMobs);
        command.Parameters.AddWithValue("mob_detection_radius_tiles", draft.MobDetectionRadiusTiles);
        command.Parameters.AddWithValue("mob_target_scan_interval_ms", draft.MobTargetScanIntervalMs);
        command.Parameters.AddWithValue("mob_target_scan_candidate_limit", draft.MobTargetScanCandidateLimit);
    }

    private static void AddCombatBonusParameters(
        NpgsqlCommand command,
        EquipmentCombatBonusDefinition bonuses)
    {
        command.Parameters.AddWithValue("attack_thrust", bonuses.AttackThrust);
        command.Parameters.AddWithValue("attack_slash", bonuses.AttackSlash);
        command.Parameters.AddWithValue("attack_crush", bonuses.AttackCrush);
        command.Parameters.AddWithValue("attack_ranged", bonuses.AttackRanged);
        command.Parameters.AddWithValue("attack_magic", bonuses.AttackMagic);
        command.Parameters.AddWithValue("strength_melee", bonuses.StrengthMelee);
        command.Parameters.AddWithValue("strength_ranged", bonuses.StrengthRanged);
        command.Parameters.AddWithValue("strength_magic", bonuses.StrengthMagic);
        command.Parameters.AddWithValue("defence_thrust", bonuses.DefenceThrust);
        command.Parameters.AddWithValue("defence_slash", bonuses.DefenceSlash);
        command.Parameters.AddWithValue("defence_crush", bonuses.DefenceCrush);
        command.Parameters.AddWithValue("defence_ranged", bonuses.DefenceRanged);
        command.Parameters.AddWithValue("defence_magic", bonuses.DefenceMagic);
    }

    private static MobDefinitionRecord ReadBaseRecord(
        NpgsqlDataReader reader,
        MobCombatProfileDefinition? profile,
        EquipmentCombatBonusDefinition? bonuses,
        IReadOnlyList<MobDropDefinition> drops,
        bool hasCombatProfile)
    {
        var factionOrdinal = reader.GetOrdinal("combat_faction_id");
        var factionDisplayOrdinal = reader.GetOrdinal("combat_faction_display_name");
        return new MobDefinitionRecord(
            reader.GetString(reader.GetOrdinal("mob_definition_id")),
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
            reader.GetInt32(reader.GetOrdinal("max_health")),
            reader.GetDouble(reader.GetOrdinal("movement_speed_tiles_per_second")),
            reader.GetString(reader.GetOrdinal("movement_behavior")),
            reader.GetInt32(reader.GetOrdinal("wander_radius_tiles")),
            reader.GetString(reader.GetOrdinal("aggression_mode")),
            reader.GetInt32(reader.GetOrdinal("aggression_radius_tiles")),
            reader.GetInt32(reader.GetOrdinal("leash_radius_tiles")),
            reader.GetString(reader.GetOrdinal("return_home_behavior")),
            reader.IsDBNull(factionOrdinal) ? null : reader.GetString(factionOrdinal),
            reader.IsDBNull(factionDisplayOrdinal) ? null : reader.GetString(factionDisplayOrdinal),
            reader.GetBoolean(reader.GetOrdinal("can_proactively_target_hostile_mobs")),
            reader.GetInt32(reader.GetOrdinal("mob_detection_radius_tiles")),
            reader.GetInt32(reader.GetOrdinal("mob_target_scan_interval_ms")),
            reader.GetInt32(reader.GetOrdinal("mob_target_scan_candidate_limit")),
            profile,
            bonuses,
            drops,
            hasCombatProfile,
            reader.GetInt32(reader.GetOrdinal("guaranteed_drop_count")),
            ReadUtc(reader, "updated_at"));
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(reader.GetOrdinal(column)), DateTimeKind.Utc));

    private static void EnsureExpectedVersion(
        MobDefinitionRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc,
        string mobDefinitionId)
    {
        if (existing is null)
        {
            if (expectedUpdatedAtUtc is not null)
            {
                throw new MobDefinitionConcurrencyException(mobDefinitionId, null);
            }

            return;
        }

        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new MobDefinitionConcurrencyException(existing.MobDefinitionId, existing.UpdatedAtUtc);
        }
    }
}

public sealed record MobDefinitionRecord(
    string MobDefinitionId,
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
    int MaxHealth,
    double MovementSpeedTilesPerSecond,
    string MovementBehavior,
    int WanderRadiusTiles,
    string AggressionMode,
    int AggressionRadiusTiles,
    int LeashRadiusTiles,
    string ReturnHomeBehavior,
    string? CombatFactionId,
    string? CombatFactionDisplayName,
    bool CanProactivelyTargetHostileMobs,
    int MobDetectionRadiusTiles,
    int MobTargetScanIntervalMs,
    int MobTargetScanCandidateLimit,
    MobCombatProfileDefinition? PrimaryCombatProfile,
    EquipmentCombatBonusDefinition? CombatBonuses,
    IReadOnlyList<MobDropDefinition> GuaranteedDrops,
    bool HasCombatProfile,
    int GuaranteedDropCount,
    DateTimeOffset UpdatedAtUtc);

public sealed record MobFactionRecord(string FactionId, string DisplayName);

public sealed record MobDropItemRecord(string ItemId, string DisplayName, bool RuntimeEnabled);

public sealed class MobDefinitionNotFoundException : Exception
{
    public MobDefinitionNotFoundException(string mobDefinitionId)
        : base($"Mob definition '{mobDefinitionId}' does not exist.")
    {
    }
}

public sealed class MobDefinitionConcurrencyException : Exception
{
    public MobDefinitionConcurrencyException(
        string mobDefinitionId,
        DateTimeOffset? currentUpdatedAtUtc)
        : base($"Mob definition '{mobDefinitionId}' changed after it was loaded. Reload it before saving.")
    {
        CurrentUpdatedAtUtc = currentUpdatedAtUtc;
    }

    public DateTimeOffset? CurrentUpdatedAtUtc { get; }
}
