using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class HandEquipmentRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public HandEquipmentRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<EquipmentSlotRecord>> LoadSlotsAsync(
        CancellationToken cancellationToken = default)
    {
        var equipment = new EquipmentItemRepository(_connectionFactory);
        return equipment.LoadSlotsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EquipmentSkillRecord>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        var equipment = new EquipmentItemRepository(_connectionFactory);
        return equipment.LoadSkillsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EquipmentSkillRecord>> LoadGatheringCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select skill_id, display_name
            from skill_definitions
            where category = 'gathering'
            order by sort_order, display_name, skill_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<EquipmentSkillRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new EquipmentSkillRecord(
                reader.GetString(reader.GetOrdinal("skill_id")),
                reader.GetString(reader.GetOrdinal("display_name"))));
        }

        return records;
    }

    public async Task<IReadOnlyList<HandEquipmentItemRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                i.item_id,
                i.item_name,
                i.icon_texture_path,
                i.equipment_slot_id,
                slot.display_name as equipment_slot_display_name,
                i.runtime_enabled,
                i.required_strength,
                i.updated_at,
                exists (select 1 from item_consumable_profiles p where p.item_id = i.item_id) as has_consumable_profile,
                exists (select 1 from item_combat_profiles p where p.item_id = i.item_id) as has_combat_profile,
                exists (select 1 from item_combat_bonuses b where b.item_id = i.item_id) as has_combat_bonuses,
                exists (select 1 from item_skill_requirements r where r.item_id = i.item_id) as has_skill_requirements,
                exists (select 1 from item_skill_modifiers m where m.item_id = i.item_id) as has_skill_modifiers,
                exists (select 1 from item_tool_capabilities t where t.item_id = i.item_id) as has_tool_capabilities
            from item_definitions i
            left join equipment_slot_definitions slot on slot.slot_id = i.equipment_slot_id
            where @search is null
               or i.item_id ilike '%' || @search || '%'
               or i.item_name ilike '%' || @search || '%'
               or i.equipment_slot_id ilike '%' || @search || '%'
            order by
                case
                    when i.equipment_slot_id in ('right_hand', 'left_hand') then 0
                    when exists (select 1 from item_combat_profiles p where p.item_id = i.item_id) then 1
                    when exists (select 1 from item_tool_capabilities t where t.item_id = i.item_id) then 2
                    when i.equipment_slot_id is not null then 3
                    else 4
                end,
                slot.sort_order nulls last,
                i.item_name,
                i.item_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<HandEquipmentItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadBaseRecord(reader, [], [], null, null, []));
        }

        return records;
    }

    public async Task<HandEquipmentItemRecord?> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAggregateAsync(connection, null, itemId, false, cancellationToken);
    }

    public async Task<HandEquipmentItemRecord> SaveDraftAsync(
        string itemId,
        NormalizedHandEquipmentDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new EquipmentItemNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureNotConsumable(existing);

        const string itemSql = """
            update item_definitions
            set item_name = @item_name,
                icon_texture_path = @icon_texture_path,
                equipment_slot_id = @equipment_slot_id,
                required_strength = @required_strength,
                runtime_enabled = false,
                updated_at = now()
            where item_id = @item_id;
            """;
        await using (var command = new NpgsqlCommand(itemSql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("item_name", draft.DisplayName);
            command.Parameters.AddWithValue("icon_texture_path", draft.IconTexturePath);
            command.Parameters.Add("equipment_slot_id", NpgsqlDbType.Text).Value =
                (object?)draft.EquipmentSlotId ?? DBNull.Value;
            command.Parameters.AddWithValue("required_strength", draft.RequiredStrength);
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
            {
                throw new InvalidOperationException($"Expected to update one item '{itemId}', but updated {affected} rows.");
            }
        }

        if (!draft.Equippable)
        {
            await DeleteAllEquipmentMetadataAsync(connection, transaction, itemId, cancellationToken);
        }
        else
        {
            await ReplaceRequirementsAsync(connection, transaction, itemId, draft.Requirements, cancellationToken);
            await ReplaceModifiersAsync(connection, transaction, itemId, draft.SkillModifiers, cancellationToken);
            await ReplaceCombatBonusesAsync(connection, transaction, itemId, draft.CombatBonuses, cancellationToken);

            if (EquipmentItemRepository.IsHandSlot(draft.EquipmentSlotId))
            {
                await ReplaceWeaponProfileAsync(connection, transaction, itemId, draft.WeaponProfile, cancellationToken);
                await ReplaceToolCapabilitiesAsync(connection, transaction, itemId, draft.ToolCapabilities, cancellationToken);
            }
            else
            {
                await ExecuteDeleteAsync(connection, transaction, "item_combat_profiles", itemId, cancellationToken);
                await ExecuteDeleteAsync(connection, transaction, "item_tool_capabilities", itemId, cancellationToken);
            }
        }

        var saved = await LoadAggregateAsync(connection, transaction, itemId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved hand-equipment item could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<HandEquipmentItemRecord> SetPublicationAsync(
        string itemId,
        bool runtimeEnabled,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new EquipmentItemNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureNotConsumable(existing);
        if (existing.RuntimeEnabled != runtimeEnabled)
        {
            const string sql = """
                update item_definitions
                set runtime_enabled = @runtime_enabled,
                    updated_at = now()
                where item_id = @item_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("runtime_enabled", runtimeEnabled);
            command.Parameters.AddWithValue("item_id", itemId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var saved = await LoadAggregateAsync(connection, transaction, itemId, false, cancellationToken)
            ?? throw new InvalidOperationException("Hand-equipment publication change could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public static bool HasHandMetadata(HandEquipmentItemRecord record) =>
        record.EquipmentSlotId is not null
        || record.RequiredStrength != 1
        || record.HasCombatProfile
        || record.HasCombatBonuses
        || record.HasSkillRequirements
        || record.HasSkillModifiers
        || record.HasToolCapabilities;

    private static async Task<HandEquipmentItemRecord?> LoadAggregateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var baseRecord = await LoadBaseAsync(connection, transaction, itemId, forUpdate, cancellationToken);
        if (baseRecord is null)
        {
            return null;
        }

        return baseRecord with
        {
            Requirements = await LoadRequirementsAsync(connection, transaction, itemId, cancellationToken),
            SkillModifiers = await LoadModifiersAsync(connection, transaction, itemId, cancellationToken),
            WeaponProfile = await LoadWeaponProfileAsync(connection, transaction, itemId, cancellationToken),
            CombatBonuses = await LoadCombatBonusesAsync(connection, transaction, itemId, cancellationToken),
            ToolCapabilities = await LoadToolCapabilitiesAsync(connection, transaction, itemId, cancellationToken)
        };
    }

    private static async Task<HandEquipmentItemRecord?> LoadBaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            select
                i.item_id,
                i.item_name,
                i.icon_texture_path,
                i.equipment_slot_id,
                slot.display_name as equipment_slot_display_name,
                i.runtime_enabled,
                i.required_strength,
                i.updated_at,
                exists (select 1 from item_consumable_profiles p where p.item_id = i.item_id) as has_consumable_profile,
                exists (select 1 from item_combat_profiles p where p.item_id = i.item_id) as has_combat_profile,
                exists (select 1 from item_combat_bonuses b where b.item_id = i.item_id) as has_combat_bonuses,
                exists (select 1 from item_skill_requirements r where r.item_id = i.item_id) as has_skill_requirements,
                exists (select 1 from item_skill_modifiers m where m.item_id = i.item_id) as has_skill_modifiers,
                exists (select 1 from item_tool_capabilities t where t.item_id = i.item_id) as has_tool_capabilities
            from item_definitions i
            left join equipment_slot_definitions slot on slot.slot_id = i.equipment_slot_id
            where i.item_id = @item_id
            """ + (forUpdate ? " for update of i;" : ";");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadBaseRecord(reader, [], [], null, null, [])
            : null;
    }

    private static async Task<IReadOnlyList<EquipmentSkillRequirementDefinition>> LoadRequirementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select r.skill_id, s.display_name, r.required_value
            from item_skill_requirements r
            join skill_definitions s on s.skill_id = r.skill_id
            where r.item_id = @item_id
            order by s.sort_order, s.display_name, r.skill_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<EquipmentSkillRequirementDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new EquipmentSkillRequirementDefinition(
                reader.GetString(reader.GetOrdinal("skill_id")),
                reader.GetString(reader.GetOrdinal("display_name")),
                reader.GetInt32(reader.GetOrdinal("required_value"))));
        }

        return records;
    }

    private static async Task<IReadOnlyList<EquipmentSkillModifierDefinition>> LoadModifiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select m.skill_id, s.display_name, m.modifier_value
            from item_skill_modifiers m
            join skill_definitions s on s.skill_id = m.skill_id
            where m.item_id = @item_id
            order by s.sort_order, s.display_name, m.skill_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<EquipmentSkillModifierDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new EquipmentSkillModifierDefinition(
                reader.GetString(reader.GetOrdinal("skill_id")),
                reader.GetString(reader.GetOrdinal("display_name")),
                reader.GetInt32(reader.GetOrdinal("modifier_value"))));
        }

        return records;
    }

    private static async Task<EquipmentCombatProfileDefinition?> LoadWeaponProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select profile_id, attack_type, accuracy_style,
                minimum_range_tiles, maximum_range_tiles, attack_speed_units
            from item_combat_profiles
            where item_id = @item_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var accuracyStyleOrdinal = reader.GetOrdinal("accuracy_style");
        return new EquipmentCombatProfileDefinition(
            reader.GetString(reader.GetOrdinal("profile_id")),
            reader.GetString(reader.GetOrdinal("attack_type")),
            reader.IsDBNull(accuracyStyleOrdinal) ? null : reader.GetString(accuracyStyleOrdinal),
            reader.GetInt32(reader.GetOrdinal("minimum_range_tiles")),
            reader.GetInt32(reader.GetOrdinal("maximum_range_tiles")),
            reader.GetInt32(reader.GetOrdinal("attack_speed_units")));
    }

    private static async Task<EquipmentCombatBonusDefinition?> LoadCombatBonusesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select attack_thrust, attack_slash, attack_crush, attack_ranged, attack_magic,
                strength_melee, strength_ranged, strength_magic,
                defence_thrust, defence_slash, defence_crush, defence_ranged, defence_magic
            from item_combat_bonuses
            where item_id = @item_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
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

    private static async Task<IReadOnlyList<HandEquipmentToolCapabilityDefinition>> LoadToolCapabilitiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                t.capability_id,
                coalesce(s.display_name, t.capability_id) as capability_display_name,
                t.capability_order,
                t.power_tier,
                t.action_animation_id,
                t.effect_resource_id
            from item_tool_capabilities t
            left join skill_definitions s on s.skill_id = t.capability_id
            where t.item_id = @item_id
            order by t.capability_order, t.capability_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<HandEquipmentToolCapabilityDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var actionOrdinal = reader.GetOrdinal("action_animation_id");
            var effectOrdinal = reader.GetOrdinal("effect_resource_id");
            records.Add(new HandEquipmentToolCapabilityDefinition(
                reader.GetString(reader.GetOrdinal("capability_id")),
                reader.GetString(reader.GetOrdinal("capability_display_name")),
                reader.GetInt32(reader.GetOrdinal("capability_order")),
                reader.GetInt32(reader.GetOrdinal("power_tier")),
                reader.IsDBNull(actionOrdinal) ? null : reader.GetString(actionOrdinal),
                reader.IsDBNull(effectOrdinal) ? null : reader.GetString(effectOrdinal)));
        }

        return records;
    }

    private static async Task ReplaceRequirementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        IReadOnlyList<EquipmentSkillRequirementDraft> requirements,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "item_skill_requirements", itemId, cancellationToken);
        const string sql = """
            insert into item_skill_requirements (item_id, skill_id, required_value)
            values (@item_id, @skill_id, @required_value);
            """;
        foreach (var requirement in requirements)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("skill_id", requirement.SkillId);
            command.Parameters.AddWithValue("required_value", requirement.RequiredValue);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceModifiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        IReadOnlyList<EquipmentSkillModifierDraft> modifiers,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "item_skill_modifiers", itemId, cancellationToken);
        const string sql = """
            insert into item_skill_modifiers (item_id, skill_id, modifier_value)
            values (@item_id, @skill_id, @modifier_value);
            """;
        foreach (var modifier in modifiers)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("skill_id", modifier.SkillId);
            command.Parameters.AddWithValue("modifier_value", modifier.ModifierValue);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceWeaponProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        EquipmentCombatProfileDefinition? profile,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            await ExecuteDeleteAsync(connection, transaction, "item_combat_profiles", itemId, cancellationToken);
            return;
        }

        const string sql = """
            insert into item_combat_profiles (
                item_id,
                profile_id,
                attack_type,
                accuracy_style,
                minimum_range_tiles,
                maximum_range_tiles,
                attack_speed_units,
                updated_at
            ) values (
                @item_id,
                @profile_id,
                @attack_type,
                @accuracy_style,
                @minimum_range_tiles,
                @maximum_range_tiles,
                @attack_speed_units,
                now()
            )
            on conflict (item_id) do update set
                profile_id = excluded.profile_id,
                attack_type = excluded.attack_type,
                accuracy_style = excluded.accuracy_style,
                minimum_range_tiles = excluded.minimum_range_tiles,
                maximum_range_tiles = excluded.maximum_range_tiles,
                attack_speed_units = excluded.attack_speed_units,
                updated_at = now();
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        command.Parameters.AddWithValue("profile_id", profile.ProfileId);
        command.Parameters.AddWithValue("attack_type", profile.AttackType);
        command.Parameters.Add("accuracy_style", NpgsqlDbType.Text).Value =
            (object?)profile.AccuracyStyle ?? DBNull.Value;
        command.Parameters.AddWithValue("minimum_range_tiles", profile.MinimumRangeTiles);
        command.Parameters.AddWithValue("maximum_range_tiles", profile.MaximumRangeTiles);
        command.Parameters.AddWithValue("attack_speed_units", profile.AttackSpeedUnits);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceCombatBonusesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        EquipmentCombatBonusDefinition bonuses,
        CancellationToken cancellationToken)
    {
        if (bonuses.IsZero)
        {
            await ExecuteDeleteAsync(connection, transaction, "item_combat_bonuses", itemId, cancellationToken);
            return;
        }

        const string sql = """
            insert into item_combat_bonuses (
                item_id,
                attack_thrust, attack_slash, attack_crush, attack_ranged, attack_magic,
                strength_melee, strength_ranged, strength_magic,
                defence_thrust, defence_slash, defence_crush, defence_ranged, defence_magic,
                updated_at
            ) values (
                @item_id,
                @attack_thrust, @attack_slash, @attack_crush, @attack_ranged, @attack_magic,
                @strength_melee, @strength_ranged, @strength_magic,
                @defence_thrust, @defence_slash, @defence_crush, @defence_ranged, @defence_magic,
                now()
            )
            on conflict (item_id) do update set
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
        command.Parameters.AddWithValue("item_id", itemId);
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
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceToolCapabilitiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        IReadOnlyList<HandEquipmentToolCapabilityDraft> capabilities,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "item_tool_capabilities", itemId, cancellationToken);
        const string sql = """
            insert into item_tool_capabilities (
                item_id,
                capability_id,
                capability_order,
                power_tier,
                action_animation_id,
                effect_resource_id,
                updated_at
            ) values (
                @item_id,
                @capability_id,
                @capability_order,
                @power_tier,
                @action_animation_id,
                @effect_resource_id,
                now()
            );
            """;
        for (var index = 0; index < capabilities.Count; index++)
        {
            var capability = capabilities[index];
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("capability_id", capability.CapabilityId);
            command.Parameters.AddWithValue("capability_order", index);
            command.Parameters.AddWithValue("power_tier", capability.PowerTier);
            command.Parameters.Add("action_animation_id", NpgsqlDbType.Text).Value =
                (object?)capability.ActionAnimationId ?? DBNull.Value;
            command.Parameters.Add("effect_resource_id", NpgsqlDbType.Text).Value =
                (object?)capability.EffectResourceId ?? DBNull.Value;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task DeleteAllEquipmentMetadataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "item_skill_requirements", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_skill_modifiers", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_combat_profiles", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_combat_bonuses", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_tool_capabilities", itemId, cancellationToken);
    }

    private static async Task ExecuteDeleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string itemId,
        CancellationToken cancellationToken)
    {
        var sql = $"delete from {table} where item_id = @item_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static HandEquipmentItemRecord ReadBaseRecord(
        NpgsqlDataReader reader,
        IReadOnlyList<EquipmentSkillRequirementDefinition> requirements,
        IReadOnlyList<EquipmentSkillModifierDefinition> modifiers,
        EquipmentCombatProfileDefinition? weaponProfile,
        EquipmentCombatBonusDefinition? combatBonuses,
        IReadOnlyList<HandEquipmentToolCapabilityDefinition> toolCapabilities)
    {
        var slotOrdinal = reader.GetOrdinal("equipment_slot_id");
        var slotDisplayOrdinal = reader.GetOrdinal("equipment_slot_display_name");
        return new HandEquipmentItemRecord(
            reader.GetString(reader.GetOrdinal("item_id")),
            reader.GetString(reader.GetOrdinal("item_name")),
            reader.GetString(reader.GetOrdinal("icon_texture_path")),
            reader.IsDBNull(slotOrdinal) ? null : reader.GetString(slotOrdinal),
            reader.IsDBNull(slotDisplayOrdinal) ? null : reader.GetString(slotDisplayOrdinal),
            reader.GetBoolean(reader.GetOrdinal("runtime_enabled")),
            reader.GetInt32(reader.GetOrdinal("required_strength")),
            reader.GetBoolean(reader.GetOrdinal("has_consumable_profile")),
            reader.GetBoolean(reader.GetOrdinal("has_combat_profile")),
            reader.GetBoolean(reader.GetOrdinal("has_combat_bonuses")),
            reader.GetBoolean(reader.GetOrdinal("has_skill_requirements")),
            reader.GetBoolean(reader.GetOrdinal("has_skill_modifiers")),
            reader.GetBoolean(reader.GetOrdinal("has_tool_capabilities")),
            requirements,
            modifiers,
            weaponProfile,
            combatBonuses,
            toolCapabilities,
            ReadUtc(reader, "updated_at"));
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(reader.GetOrdinal(column)), DateTimeKind.Utc));

    private static void EnsureNotConsumable(HandEquipmentItemRecord existing)
    {
        if (existing.HasConsumableProfile)
        {
            throw new EquipmentKindConflictException(
                existing.ItemId,
                "Consumable items must be edited in the Consumables workspace.");
        }
    }

    private static void EnsureExpectedVersion(
        HandEquipmentItemRecord existing,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new EquipmentConcurrencyException(existing.ItemId, existing.UpdatedAtUtc);
        }
    }
}

public sealed record HandEquipmentItemRecord(
    string ItemId,
    string DisplayName,
    string IconTexturePath,
    string? EquipmentSlotId,
    string? EquipmentSlotDisplayName,
    bool RuntimeEnabled,
    int RequiredStrength,
    bool HasConsumableProfile,
    bool HasCombatProfile,
    bool HasCombatBonuses,
    bool HasSkillRequirements,
    bool HasSkillModifiers,
    bool HasToolCapabilities,
    IReadOnlyList<EquipmentSkillRequirementDefinition> Requirements,
    IReadOnlyList<EquipmentSkillModifierDefinition> SkillModifiers,
    EquipmentCombatProfileDefinition? WeaponProfile,
    EquipmentCombatBonusDefinition? CombatBonuses,
    IReadOnlyList<HandEquipmentToolCapabilityDefinition> ToolCapabilities,
    DateTimeOffset UpdatedAtUtc);
