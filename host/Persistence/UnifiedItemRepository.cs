using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public interface IUnifiedItemRepository
{
    Task<IReadOnlyList<UnifiedItemRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default);

    Task<UnifiedItemRecord?> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EquipmentSlotRecord>> LoadSlotsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EquipmentSkillRecord>> LoadSkillsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EquipmentSkillRecord>> LoadGatheringCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthoringOption>> LoadPublishedItemOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> HasLiveReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<bool> HasPublishedConsumableResultReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<bool> HasPublishedDeathTransformReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> LoadPublishedDialogueReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<ReferencedItemRecord?> LoadReferencedItemAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    Task<UnifiedItemRecord> SaveDraftAsync(
        string itemId,
        NormalizedItemDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        bool expectNew,
        CancellationToken cancellationToken = default);

    Task<UnifiedItemRecord> SetPublicationAsync(
        string itemId,
        bool runtimeEnabled,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class UnifiedItemRepository : IUnifiedItemRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public UnifiedItemRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<UnifiedItemRecord>> ListAsync(
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
                i.reference_value,
                i.trade_policy,
                i.death_behavior,
                i.death_transform_item_id,
                i.shop_policy,
                i.npc_buy_price,
                i.npc_sell_price,
                i.reclaim_policy,
                i.reclaim_value,
                i.condition_policy_id,
                i.repair_policy_id,
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
            order by i.item_name, i.item_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<UnifiedItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadBaseRecord(reader, null, [], [], [], null, null, []));
        }

        return records;
    }

    public async Task<UnifiedItemRecord?> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAggregateAsync(connection, null, itemId, false, cancellationToken);
    }

    public async Task<IReadOnlyList<EquipmentSlotRecord>> LoadSlotsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select slot_id, display_name
            from equipment_slot_definitions
            order by sort_order, display_name, slot_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<EquipmentSlotRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new EquipmentSlotRecord(
                reader.GetString(reader.GetOrdinal("slot_id")),
                reader.GetString(reader.GetOrdinal("display_name"))));
        }

        return records;
    }

    public async Task<IReadOnlyList<EquipmentSkillRecord>> LoadSkillsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select skill_id, display_name
            from skill_definitions
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

    public async Task<IReadOnlyList<AuthoringOption>> LoadPublishedItemOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select item_id, item_name
            from item_definitions
            where runtime_enabled = true
            order by item_name, item_id;
            """;
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<AuthoringOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new AuthoringOption(
                reader.GetString(reader.GetOrdinal("item_id")),
                reader.GetString(reader.GetOrdinal("item_name"))));
        }

        return records;
    }

    public async Task<bool> HasLiveReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select exists (
                select 1 from character_inventory where item_id = @item_id
                union all
                select 1 from character_equipment where item_id = @item_id
                union all
                select 1 from ground_items where item_id = @item_id
            );
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> HasPublishedConsumableResultReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select exists (
                select 1
                from item_consumable_profiles profile
                join item_definitions source_item on source_item.item_id = profile.item_id
                where profile.result_item_id = @item_id
                  and source_item.runtime_enabled = true
            );
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> HasPublishedDeathTransformReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select exists (
                select 1 from item_definitions
                where runtime_enabled = true
                  and death_behavior = 'transform'
                  and death_transform_item_id = @item_id
            );
            """;
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<IReadOnlyList<string>> LoadPublishedDialogueReferencesAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadPublishedDialogueReferencesAsync(connection, null, itemId, cancellationToken);
    }

    public async Task<ReferencedItemRecord?> LoadReferencedItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select item_id, item_name, runtime_enabled
            from item_definitions
            where item_id = @item_id;
            """;
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReferencedItemRecord(
            reader.GetString(reader.GetOrdinal("item_id")),
            reader.GetString(reader.GetOrdinal("item_name")),
            reader.GetBoolean(reader.GetOrdinal("runtime_enabled")));
    }

    public async Task<UnifiedItemRecord> SaveDraftAsync(
        string itemId,
        NormalizedItemDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        bool expectNew,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, itemId, true, cancellationToken);
        if (expectNew && existing is not null)
        {
            throw new UnifiedItemConcurrencyException(itemId, existing.UpdatedAtUtc);
        }
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, itemId);

        const string sql = """
            insert into item_definitions (
                item_id,
                item_name,
                icon_texture_path,
                equipment_slot_id,
                runtime_enabled,
                required_strength,
                reference_value,
                trade_policy,
                death_behavior,
                death_transform_item_id,
                shop_policy,
                npc_buy_price,
                npc_sell_price,
                reclaim_policy,
                reclaim_value,
                condition_policy_id,
                repair_policy_id,
                updated_at
            ) values (
                @item_id,
                @item_name,
                @icon_texture_path,
                @equipment_slot_id,
                false,
                @required_strength,
                @reference_value,
                @trade_policy,
                @death_behavior,
                @death_transform_item_id,
                @shop_policy,
                @npc_buy_price,
                @npc_sell_price,
                @reclaim_policy,
                @reclaim_value,
                @condition_policy_id,
                @repair_policy_id,
                now()
            )
            on conflict (item_id)
            do update set
                item_name = excluded.item_name,
                icon_texture_path = excluded.icon_texture_path,
                equipment_slot_id = excluded.equipment_slot_id,
                required_strength = excluded.required_strength,
                reference_value = excluded.reference_value,
                trade_policy = excluded.trade_policy,
                death_behavior = excluded.death_behavior,
                death_transform_item_id = excluded.death_transform_item_id,
                shop_policy = excluded.shop_policy,
                npc_buy_price = excluded.npc_buy_price,
                npc_sell_price = excluded.npc_sell_price,
                reclaim_policy = excluded.reclaim_policy,
                reclaim_value = excluded.reclaim_value,
                condition_policy_id = excluded.condition_policy_id,
                repair_policy_id = excluded.repair_policy_id,
                runtime_enabled = false,
                updated_at = now();
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("item_name", draft.DisplayName);
            command.Parameters.AddWithValue("icon_texture_path", draft.IconTexturePath);
            command.Parameters.Add("equipment_slot_id", NpgsqlDbType.Text).Value =
                (object?)draft.Equipment?.EquipmentSlotId ?? DBNull.Value;
            command.Parameters.AddWithValue("required_strength", draft.Equipment?.RequiredStrength ?? 1);
            command.Parameters.AddWithValue("reference_value", draft.EconomyLifecycle.ReferenceValue);
            command.Parameters.AddWithValue("trade_policy", draft.EconomyLifecycle.TradePolicy ?? string.Empty);
            command.Parameters.AddWithValue("death_behavior", draft.EconomyLifecycle.DeathBehavior ?? string.Empty);
            AddNullableText(command, "death_transform_item_id", draft.EconomyLifecycle.DeathTransformItemId);
            command.Parameters.AddWithValue("shop_policy", draft.EconomyLifecycle.ShopPolicy ?? string.Empty);
            command.Parameters.Add("npc_buy_price", NpgsqlDbType.Bigint).Value = (object?)draft.EconomyLifecycle.NpcBuyPrice ?? DBNull.Value;
            command.Parameters.Add("npc_sell_price", NpgsqlDbType.Bigint).Value = (object?)draft.EconomyLifecycle.NpcSellPrice ?? DBNull.Value;
            command.Parameters.AddWithValue("reclaim_policy", draft.EconomyLifecycle.ReclaimPolicy ?? string.Empty);
            command.Parameters.Add("reclaim_value", NpgsqlDbType.Bigint).Value = (object?)draft.EconomyLifecycle.ReclaimValue ?? DBNull.Value;
            AddNullableText(command, "condition_policy_id", draft.EconomyLifecycle.ConditionPolicyId);
            AddNullableText(command, "repair_policy_id", draft.EconomyLifecycle.RepairPolicyId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplaceConsumableAsync(connection, transaction, itemId, draft.ConsumableBehavior, cancellationToken);
        await ReplaceEquipmentAsync(connection, transaction, itemId, draft.Equipment, cancellationToken);
        await ReplaceToolCapabilitiesAsync(connection, transaction, itemId, draft.ToolCapabilities, cancellationToken);

        var saved = await LoadAggregateAsync(connection, transaction, itemId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved item aggregate could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<UnifiedItemRecord> SetPublicationAsync(
        string itemId,
        bool runtimeEnabled,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new UnifiedItemNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, itemId);
        if (existing.RuntimeEnabled && !runtimeEnabled)
        {
            await EnsureNoPublishedDialogueReferencesAsync(connection, transaction, itemId, "disable", cancellationToken);
        }
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
            ?? throw new InvalidOperationException("Item publication change could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new UnifiedItemNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, itemId);
        if (existing.RuntimeEnabled)
        {
            throw new UnifiedItemPublishedDeleteException(itemId);
        }
        await EnsureNoPublishedDialogueReferencesAsync(connection, transaction, itemId, "delete", cancellationToken);

        await ExecuteDeleteAsync(connection, transaction, "item_tool_capabilities", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_skill_requirements", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_skill_modifiers", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_combat_profiles", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_combat_bonuses", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_equipped_visual_pose_anchors", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_equipped_visuals", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_consumable_requirements", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_consumable_effects", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_consumable_profiles", itemId, cancellationToken);

        const string sql = "delete from item_definitions where item_id = @item_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<UnifiedItemRecord?> LoadAggregateAsync(
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

        var consumable = await LoadConsumableBehaviorAsync(connection, transaction, itemId, cancellationToken);
        return baseRecord with
        {
            ConsumableBehavior = consumable,
            ConsumableRequirements = consumable is null
                ? []
                : await LoadConsumableRequirementsAsync(connection, transaction, itemId, cancellationToken),
            ConsumableEffects = consumable is null
                ? []
                : await LoadConsumableEffectsAsync(connection, transaction, itemId, cancellationToken),
            Requirements = await LoadRequirementsAsync(connection, transaction, itemId, cancellationToken),
            SkillModifiers = await LoadModifiersAsync(connection, transaction, itemId, cancellationToken),
            WeaponProfile = await LoadWeaponProfileAsync(connection, transaction, itemId, cancellationToken),
            CombatBonuses = await LoadCombatBonusesAsync(connection, transaction, itemId, cancellationToken),
            EquippedVisual = await LoadEquippedVisualAsync(connection, transaction, itemId, cancellationToken),
            ToolCapabilities = await LoadToolCapabilitiesAsync(connection, transaction, itemId, cancellationToken)
        };
    }

    private static async Task<IReadOnlyList<string>> LoadPublishedDialogueReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select distinct d.dialogue_definition_id
            from dialogue_definitions d
            join (
                select dialogue_definition_id, item_id
                from dialogue_entry_conditions
                where item_id = @item_id
                union all
                select dialogue_definition_id, item_id
                from dialogue_choice_conditions
                where item_id = @item_id
            ) condition on condition.dialogue_definition_id = d.dialogue_definition_id
            where d.publication_state = 'Published'
            order by d.dialogue_definition_id;
            """;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var dialogueIds = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                dialogueIds.Add(reader.GetString(0));
            }

            return dialogueIds;
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return [];
        }
    }

    private static async Task EnsureNoPublishedDialogueReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        string operation,
        CancellationToken cancellationToken)
    {
        var dialogueIds = await LoadPublishedDialogueReferencesAsync(
            connection,
            transaction,
            itemId,
            cancellationToken);
        if (dialogueIds.Count > 0)
        {
            throw new UnifiedItemReferencedByPublishedDialogueException(itemId, operation, dialogueIds);
        }
    }

    private static async Task<UnifiedItemRecord?> LoadBaseAsync(
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
                i.reference_value,
                i.trade_policy,
                i.death_behavior,
                i.death_transform_item_id,
                i.shop_policy,
                i.npc_buy_price,
                i.npc_sell_price,
                i.reclaim_policy,
                i.reclaim_value,
                i.condition_policy_id,
                i.repair_policy_id,
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
            ? ReadBaseRecord(reader, null, [], [], [], null, null, [])
            : null;
    }

    private static async Task<ConsumableProfileDraft?> LoadConsumableBehaviorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select use_action, consume_quantity, result_item_id, success_message,
                usable_in_combat, cooldown_ms, use_animation_id, use_sound_resource_path
            from item_consumable_profiles
            where item_id = @item_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ConsumableProfileDraft(
            reader.GetString(reader.GetOrdinal("use_action")),
            reader.GetInt32(reader.GetOrdinal("consume_quantity")),
            ReadNullableString(reader, "result_item_id"),
            ReadNullableString(reader, "success_message"),
            reader.GetBoolean(reader.GetOrdinal("usable_in_combat")),
            reader.GetInt32(reader.GetOrdinal("cooldown_ms")),
            ReadNullableString(reader, "use_animation_id"),
            ReadNullableString(reader, "use_sound_resource_path"));
    }

    private static async Task<IReadOnlyList<ConsumableRequirementDefinition>> LoadConsumableRequirementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select requirement_index, requirement_type, target_id, minimum_value
            from item_consumable_requirements
            where item_id = @item_id
            order by requirement_index, target_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<ConsumableRequirementDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ConsumableRequirementDefinition(
                reader.GetInt32(reader.GetOrdinal("requirement_index")),
                reader.GetString(reader.GetOrdinal("requirement_type")),
                reader.GetString(reader.GetOrdinal("target_id")),
                reader.GetInt32(reader.GetOrdinal("minimum_value"))));
        }

        return records;
    }

    private static async Task<IReadOnlyList<ConsumableEffectDefinition>> LoadConsumableEffectsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select effect_index, effect_type, target_id, minimum_amount, maximum_amount
            from item_consumable_effects
            where item_id = @item_id
            order by effect_index, target_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<ConsumableEffectDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ConsumableEffectDefinition(
                reader.GetInt32(reader.GetOrdinal("effect_index")),
                reader.GetString(reader.GetOrdinal("effect_type")),
                reader.GetString(reader.GetOrdinal("target_id")),
                reader.GetInt32(reader.GetOrdinal("minimum_amount")),
                reader.GetInt32(reader.GetOrdinal("maximum_amount"))));
        }

        return records;
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

        return new EquipmentCombatProfileDefinition(
            reader.GetString(reader.GetOrdinal("profile_id")),
            reader.GetString(reader.GetOrdinal("attack_type")),
            ReadNullableString(reader, "accuracy_style"),
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

    private static async Task<IReadOnlyList<ItemToolCapabilityDefinition>> LoadToolCapabilitiesAsync(
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
        var records = new List<ItemToolCapabilityDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ItemToolCapabilityDefinition(
                reader.GetString(reader.GetOrdinal("capability_id")),
                reader.GetString(reader.GetOrdinal("capability_display_name")),
                reader.GetInt32(reader.GetOrdinal("capability_order")),
                reader.GetInt32(reader.GetOrdinal("power_tier")),
                ReadNullableString(reader, "action_animation_id"),
                ReadNullableString(reader, "effect_resource_id")));
        }

        return records;
    }

    private static async Task<ItemEquippedVisualDefinition?> LoadEquippedVisualAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select asset_key, rig_id, binding_type, render_layer_id, socket_id, secondary_socket_id, nudge_x, nudge_y
            from item_equipped_visuals
            where item_id = @item_id;
            """;
        string assetKey;
        string rigId;
        string bindingType;
        string renderLayerId;
        string? socketId;
        string? secondarySocketId;
        SourcePixelPointDefinition nudge;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            assetKey = reader.GetString(reader.GetOrdinal("asset_key"));
            rigId = reader.GetString(reader.GetOrdinal("rig_id"));
            bindingType = reader.GetString(reader.GetOrdinal("binding_type"));
            renderLayerId = reader.GetString(reader.GetOrdinal("render_layer_id"));
            socketId = ReadNullableString(reader, "socket_id");
            secondarySocketId = ReadNullableString(reader, "secondary_socket_id");
            nudge = new SourcePixelPointDefinition(
                reader.GetInt32(reader.GetOrdinal("nudge_x")),
                reader.GetInt32(reader.GetOrdinal("nudge_y")));
        }

        var gripAnchors = await LoadEquippedVisualGripAnchorsAsync(
            connection,
            transaction,
            itemId,
            cancellationToken);
        var flipXByPose = await LoadEquippedVisualFlipXByPoseAsync(
            connection,
            transaction,
            itemId,
            cancellationToken);
        var hiddenPoses = await LoadEquippedVisualHiddenPosesAsync(
            connection,
            transaction,
            itemId,
            cancellationToken);
        var itemOverGripByPose = await LoadEquippedVisualItemOverGripByPoseAsync(
            connection,
            transaction,
            itemId,
            cancellationToken);
        return new ItemEquippedVisualDefinition(
            assetKey,
            rigId,
            bindingType,
            renderLayerId,
            socketId,
            secondarySocketId,
            nudge,
            gripAnchors,
            flipXByPose,
            hiddenPoses,
            itemOverGripByPose);
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>> LoadEquippedVisualGripAnchorsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select direction, frame, grip_anchor_x, grip_anchor_y
            from item_equipped_visual_pose_anchors
            where item_id = @item_id
              and grip_anchor_x is not null
              and grip_anchor_y is not null
            order by direction, frame;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var anchors = new Dictionary<string, Dictionary<string, SourcePixelPointDefinition>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var direction = reader.GetString(reader.GetOrdinal("direction"));
            var frame = reader.GetInt32(reader.GetOrdinal("frame")).ToString();
            if (!anchors.TryGetValue(direction, out var frames))
            {
                frames = new Dictionary<string, SourcePixelPointDefinition>(StringComparer.Ordinal);
                anchors[direction] = frames;
            }

            frames[frame] = new SourcePixelPointDefinition(
                reader.GetInt32(reader.GetOrdinal("grip_anchor_x")),
                reader.GetInt32(reader.GetOrdinal("grip_anchor_y")));
        }

        return anchors.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, SourcePixelPointDefinition>)pair.Value,
            StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>> LoadEquippedVisualFlipXByPoseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select direction, frame
            from item_equipped_visual_pose_anchors
            where item_id = @item_id
              and flip_x = true
            order by direction, frame;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var poses = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var direction = reader.GetString(reader.GetOrdinal("direction"));
            var frame = reader.GetInt32(reader.GetOrdinal("frame")).ToString();
            if (!poses.TryGetValue(direction, out var frames))
            {
                frames = new Dictionary<string, bool>(StringComparer.Ordinal);
                poses[direction] = frames;
            }

            frames[frame] = true;
        }

        return poses.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, bool>)pair.Value,
            StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>> LoadEquippedVisualHiddenPosesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select direction, frame
            from item_equipped_visual_pose_anchors
            where item_id = @item_id
              and hidden = true
            order by direction, frame;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var poses = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var direction = reader.GetString(reader.GetOrdinal("direction"));
            var frame = reader.GetInt32(reader.GetOrdinal("frame")).ToString();
            if (!poses.TryGetValue(direction, out var frames))
            {
                frames = new Dictionary<string, bool>(StringComparer.Ordinal);
                poses[direction] = frames;
            }

            frames[frame] = true;
        }

        return poses.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, bool>)pair.Value,
            StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>> LoadEquippedVisualItemOverGripByPoseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select direction, frame
            from item_equipped_visual_pose_anchors
            where item_id = @item_id
              and item_over_grip = true
            order by direction, frame;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var poses = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var direction = reader.GetString(reader.GetOrdinal("direction"));
            var frame = reader.GetInt32(reader.GetOrdinal("frame")).ToString();
            if (!poses.TryGetValue(direction, out var frames))
            {
                frames = new Dictionary<string, bool>(StringComparer.Ordinal);
                poses[direction] = frames;
            }

            frames[frame] = true;
        }

        return poses.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, bool>)pair.Value,
            StringComparer.Ordinal);
    }

    private static async Task ReplaceConsumableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        NormalizedItemConsumableBehavior? consumable,
        CancellationToken cancellationToken)
    {
        if (consumable is null)
        {
            await ExecuteDeleteAsync(connection, transaction, "item_consumable_requirements", itemId, cancellationToken);
            await ExecuteDeleteAsync(connection, transaction, "item_consumable_effects", itemId, cancellationToken);
            await ExecuteDeleteAsync(connection, transaction, "item_consumable_profiles", itemId, cancellationToken);
            return;
        }

        const string sql = """
            insert into item_consumable_profiles (
                item_id,
                use_action,
                consume_quantity,
                result_item_id,
                success_message,
                usable_in_combat,
                cooldown_ms,
                use_animation_id,
                use_sound_resource_path,
                updated_at
            ) values (
                @item_id,
                @use_action,
                @consume_quantity,
                @result_item_id,
                @success_message,
                @usable_in_combat,
                @cooldown_ms,
                @use_animation_id,
                @use_sound_resource_path,
                now()
            )
            on conflict (item_id)
            do update set
                use_action = excluded.use_action,
                consume_quantity = excluded.consume_quantity,
                result_item_id = excluded.result_item_id,
                success_message = excluded.success_message,
                usable_in_combat = excluded.usable_in_combat,
                cooldown_ms = excluded.cooldown_ms,
                use_animation_id = excluded.use_animation_id,
                use_sound_resource_path = excluded.use_sound_resource_path,
                updated_at = now();
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("use_action", consumable.UseAction);
            command.Parameters.AddWithValue("consume_quantity", consumable.ConsumeQuantity);
            AddNullableText(command, "result_item_id", consumable.ResultItemId);
            AddNullableText(command, "success_message", consumable.SuccessMessage);
            command.Parameters.AddWithValue("usable_in_combat", consumable.UsableInCombat);
            command.Parameters.AddWithValue("cooldown_ms", consumable.CooldownMs);
            AddNullableText(command, "use_animation_id", consumable.UseAnimationId);
            AddNullableText(command, "use_sound_resource_path", consumable.UseSoundResourcePath);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplaceConsumableRequirementsAsync(connection, transaction, itemId, consumable.Requirements, cancellationToken);
        await ReplaceConsumableEffectsAsync(connection, transaction, itemId, consumable.Effects, cancellationToken);
    }

    private static async Task ReplaceConsumableRequirementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        IReadOnlyList<ConsumableRequirementDefinition> requirements,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "item_consumable_requirements", itemId, cancellationToken);
        const string sql = """
            insert into item_consumable_requirements (
                item_id,
                requirement_index,
                requirement_type,
                target_id,
                minimum_value,
                updated_at
            ) values (
                @item_id,
                @requirement_index,
                @requirement_type,
                @target_id,
                @minimum_value,
                now()
            );
            """;
        for (var index = 0; index < requirements.Count; index++)
        {
            var requirement = requirements[index];
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("requirement_index", index);
            command.Parameters.AddWithValue("requirement_type", requirement.RequirementType);
            command.Parameters.AddWithValue("target_id", requirement.TargetId);
            command.Parameters.AddWithValue("minimum_value", requirement.MinimumValue);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceConsumableEffectsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        IReadOnlyList<ConsumableEffectDefinition> effects,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "item_consumable_effects", itemId, cancellationToken);
        const string sql = """
            insert into item_consumable_effects (
                item_id,
                effect_index,
                effect_type,
                target_id,
                minimum_amount,
                maximum_amount,
                updated_at
            ) values (
                @item_id,
                @effect_index,
                @effect_type,
                @target_id,
                @minimum_amount,
                @maximum_amount,
                now()
            );
            """;
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("effect_index", index);
            command.Parameters.AddWithValue("effect_type", effect.EffectType);
            command.Parameters.AddWithValue("target_id", effect.TargetId);
            command.Parameters.AddWithValue("minimum_amount", effect.MinimumAmount);
            command.Parameters.AddWithValue("maximum_amount", effect.MaximumAmount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceEquipmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        NormalizedItemEquipmentMetadata? equipment,
        CancellationToken cancellationToken)
    {
        if (equipment is null)
        {
            await ExecuteDeleteAsync(connection, transaction, "item_skill_requirements", itemId, cancellationToken);
            await ExecuteDeleteAsync(connection, transaction, "item_skill_modifiers", itemId, cancellationToken);
            await ExecuteDeleteAsync(connection, transaction, "item_combat_profiles", itemId, cancellationToken);
            await ExecuteDeleteAsync(connection, transaction, "item_combat_bonuses", itemId, cancellationToken);
            await ExecuteDeleteAsync(connection, transaction, "item_equipped_visual_pose_anchors", itemId, cancellationToken);
            await ExecuteDeleteAsync(connection, transaction, "item_equipped_visuals", itemId, cancellationToken);
            return;
        }

        await ReplaceRequirementsAsync(connection, transaction, itemId, equipment.Requirements, cancellationToken);
        await ReplaceModifiersAsync(connection, transaction, itemId, equipment.SkillModifiers, cancellationToken);
        await ReplaceWeaponProfileAsync(connection, transaction, itemId, equipment.WeaponProfile, cancellationToken);
        await ReplaceCombatBonusesAsync(connection, transaction, itemId, equipment.CombatBonuses, cancellationToken);
        await ReplaceEquippedVisualAsync(connection, transaction, itemId, equipment.EquippedVisual, cancellationToken);
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

    private static async Task ReplaceEquippedVisualAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        NormalizedItemEquippedVisual? equippedVisual,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "item_equipped_visual_pose_anchors", itemId, cancellationToken);
        if (equippedVisual is null)
        {
            await ExecuteDeleteAsync(connection, transaction, "item_equipped_visuals", itemId, cancellationToken);
            return;
        }

        const string visualSql = """
            insert into item_equipped_visuals (
                item_id,
                asset_key,
                rig_id,
                binding_type,
                render_layer_id,
                socket_id,
                secondary_socket_id,
                nudge_x,
                nudge_y,
                updated_at
            ) values (
                @item_id,
                @asset_key,
                @rig_id,
                @binding_type,
                @render_layer_id,
                @socket_id,
                @secondary_socket_id,
                @nudge_x,
                @nudge_y,
                now()
            )
            on conflict (item_id) do update set
                asset_key = excluded.asset_key,
                rig_id = excluded.rig_id,
                binding_type = excluded.binding_type,
                render_layer_id = excluded.render_layer_id,
                socket_id = excluded.socket_id,
                secondary_socket_id = excluded.secondary_socket_id,
                nudge_x = excluded.nudge_x,
                nudge_y = excluded.nudge_y,
                updated_at = now();
            """;
        await using (var command = new NpgsqlCommand(visualSql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("asset_key", (object?)equippedVisual.AssetKey ?? string.Empty);
            command.Parameters.AddWithValue("rig_id", (object?)equippedVisual.RigId ?? string.Empty);
            command.Parameters.AddWithValue("binding_type", (object?)equippedVisual.BindingType ?? string.Empty);
            command.Parameters.AddWithValue("render_layer_id", (object?)equippedVisual.RenderLayerId ?? string.Empty);
            command.Parameters.Add("socket_id", NpgsqlDbType.Text).Value =
                (object?)equippedVisual.SocketId ?? DBNull.Value;
            command.Parameters.Add("secondary_socket_id", NpgsqlDbType.Text).Value =
                (object?)equippedVisual.SecondarySocketId ?? DBNull.Value;
            command.Parameters.AddWithValue("nudge_x", equippedVisual.Nudge.X);
            command.Parameters.AddWithValue("nudge_y", equippedVisual.Nudge.Y);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string anchorSql = """
            insert into item_equipped_visual_pose_anchors (
                item_id,
                direction,
                frame,
                grip_anchor_x,
                grip_anchor_y,
                flip_x,
                hidden,
                item_over_grip,
                updated_at
            ) values (
                @item_id,
                @direction,
                @frame,
                @grip_anchor_x,
                @grip_anchor_y,
                @flip_x,
                @hidden,
                @item_over_grip,
                now()
            );
            """;
        var persistedPoses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var direction in equippedVisual.GripAnchors.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var frame in direction.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                await using var command = new NpgsqlCommand(anchorSql, connection, transaction);
                command.Parameters.AddWithValue("item_id", itemId);
                command.Parameters.AddWithValue("direction", direction.Key);
                command.Parameters.AddWithValue("frame", int.Parse(frame.Key));
                command.Parameters.AddWithValue("grip_anchor_x", frame.Value.X);
                command.Parameters.AddWithValue("grip_anchor_y", frame.Value.Y);
                command.Parameters.AddWithValue(
                    "flip_x",
                    IsFlipX(
                        equippedVisual.FlipXByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal),
                        direction.Key,
                        frame.Key));
                command.Parameters.AddWithValue(
                    "hidden",
                    IsHidden(
                        equippedVisual.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal),
                        direction.Key,
                        frame.Key));
                command.Parameters.AddWithValue(
                    "item_over_grip",
                    IsItemOverGrip(
                        equippedVisual.ItemOverGripByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal),
                        direction.Key,
                        frame.Key));
                await command.ExecuteNonQueryAsync(cancellationToken);
                persistedPoses.Add($"{direction.Key}|{frame.Key}");
            }
        }

        var nonAnchorPoses = (equippedVisual.FlipXByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal))
            .SelectMany(direction => direction.Value.Where(frame => frame.Value).Select(frame => (Direction: direction.Key, Frame: frame.Key)))
            .Concat((equippedVisual.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal))
                .SelectMany(direction => direction.Value.Where(frame => frame.Value).Select(frame => (Direction: direction.Key, Frame: frame.Key))))
            .Concat((equippedVisual.ItemOverGripByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal))
                .SelectMany(direction => direction.Value.Where(frame => frame.Value).Select(frame => (Direction: direction.Key, Frame: frame.Key))))
            .Distinct()
            .OrderBy(pose => pose.Direction, StringComparer.Ordinal)
            .ThenBy(pose => pose.Frame, StringComparer.Ordinal);
        foreach (var pose in nonAnchorPoses)
        {
            if (persistedPoses.Contains($"{pose.Direction}|{pose.Frame}"))
            {
                continue;
            }

            await using var command = new NpgsqlCommand(anchorSql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("direction", pose.Direction);
            command.Parameters.AddWithValue("frame", int.Parse(pose.Frame));
            command.Parameters.Add("grip_anchor_x", NpgsqlDbType.Integer).Value = DBNull.Value;
            command.Parameters.Add("grip_anchor_y", NpgsqlDbType.Integer).Value = DBNull.Value;
            command.Parameters.AddWithValue(
                "flip_x",
                IsFlipX(
                    equippedVisual.FlipXByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal),
                    pose.Direction,
                    pose.Frame));
            command.Parameters.AddWithValue(
                "hidden",
                IsHidden(
                    equippedVisual.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal),
                    pose.Direction,
                    pose.Frame));
            command.Parameters.AddWithValue(
                "item_over_grip",
                IsItemOverGrip(
                    equippedVisual.ItemOverGripByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal),
                    pose.Direction,
                    pose.Frame));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static bool IsFlipX(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> flipXByPose,
        string direction,
        string frame) =>
        flipXByPose.TryGetValue(direction, out var frames)
        && frames.TryGetValue(frame, out var flipX)
        && flipX;

    private static bool IsHidden(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> hiddenPoses,
        string direction,
        string frame) =>
        hiddenPoses.TryGetValue(direction, out var frames)
        && frames.TryGetValue(frame, out var hidden)
        && hidden;

    private static bool IsItemOverGrip(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> itemOverGripByPose,
        string direction,
        string frame) =>
        itemOverGripByPose.TryGetValue(direction, out var frames)
        && frames.TryGetValue(frame, out var itemOverGrip)
        && itemOverGrip;

    private static async Task ReplaceToolCapabilitiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        IReadOnlyList<ItemToolCapabilityDraft> capabilities,
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

    private static UnifiedItemRecord ReadBaseRecord(
        NpgsqlDataReader reader,
        ConsumableProfileDraft? consumableBehavior,
        IReadOnlyList<ConsumableRequirementDefinition> consumableRequirements,
        IReadOnlyList<ConsumableEffectDefinition> consumableEffects,
        IReadOnlyList<EquipmentSkillRequirementDefinition> requirements,
        EquipmentCombatProfileDefinition? weaponProfile,
        EquipmentCombatBonusDefinition? combatBonuses,
        IReadOnlyList<ItemToolCapabilityDefinition> toolCapabilities)
    {
        var slotOrdinal = reader.GetOrdinal("equipment_slot_id");
        var slotDisplayOrdinal = reader.GetOrdinal("equipment_slot_display_name");
        return new UnifiedItemRecord(
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
            consumableBehavior,
            consumableRequirements,
            consumableEffects,
            requirements,
            [],
            weaponProfile,
            combatBonuses,
            null,
            toolCapabilities,
            ReadUtc(reader, "updated_at"),
            new ItemEconomyLifecycleDefinition(
                reader.GetInt64(reader.GetOrdinal("reference_value")),
                reader.GetString(reader.GetOrdinal("trade_policy")),
                reader.GetString(reader.GetOrdinal("death_behavior")),
                ReadNullableString(reader, "death_transform_item_id"),
                reader.GetString(reader.GetOrdinal("shop_policy")),
                ReadNullableInt64(reader, "npc_buy_price"),
                ReadNullableInt64(reader, "npc_sell_price"),
                reader.GetString(reader.GetOrdinal("reclaim_policy")),
                ReadNullableInt64(reader, "reclaim_value"),
                ReadNullableString(reader, "condition_policy_id"),
                ReadNullableString(reader, "repair_policy_id")));
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(reader.GetOrdinal(column)), DateTimeKind.Utc));

    private static long? ReadNullableInt64(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Text).Value = (object?)value ?? DBNull.Value;

    private static void EnsureExpectedVersion(
        UnifiedItemRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc,
        string itemId)
    {
        if (existing is null)
        {
            return;
        }

        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new UnifiedItemConcurrencyException(itemId, existing.UpdatedAtUtc);
        }
    }
}

public sealed record UnifiedItemRecord(
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
    ConsumableProfileDraft? ConsumableBehavior,
    IReadOnlyList<ConsumableRequirementDefinition> ConsumableRequirements,
    IReadOnlyList<ConsumableEffectDefinition> ConsumableEffects,
    IReadOnlyList<EquipmentSkillRequirementDefinition> Requirements,
    IReadOnlyList<EquipmentSkillModifierDefinition> SkillModifiers,
    EquipmentCombatProfileDefinition? WeaponProfile,
    EquipmentCombatBonusDefinition? CombatBonuses,
    ItemEquippedVisualDefinition? EquippedVisual,
    IReadOnlyList<ItemToolCapabilityDefinition> ToolCapabilities,
    DateTimeOffset UpdatedAtUtc,
    ItemEconomyLifecycleDefinition? EconomyLifecycle = null);

public sealed record ConsumableProfileDraft(
    string UseAction,
    int ConsumeQuantity,
    string? ResultItemId,
    string? SuccessMessage,
    bool UsableInCombat,
    int CooldownMs,
    string? UseAnimationId,
    string? UseSoundResourcePath);

public sealed record EquipmentSlotRecord(string SlotId, string DisplayName);

public sealed record EquipmentSkillRecord(string SkillId, string DisplayName);

public sealed record ReferencedItemRecord(string ItemId, string DisplayName, bool RuntimeEnabled);

public sealed class UnifiedItemNotFoundException : Exception
{
    public UnifiedItemNotFoundException(string itemId)
        : base($"Item '{itemId}' does not exist.")
    {
    }
}

public sealed class UnifiedItemConcurrencyException : Exception
{
    public UnifiedItemConcurrencyException(string itemId, DateTimeOffset currentUpdatedAtUtc)
        : base($"Item '{itemId}' changed after it was loaded. Reload it before saving.")
    {
        CurrentUpdatedAtUtc = currentUpdatedAtUtc;
    }

    public DateTimeOffset CurrentUpdatedAtUtc { get; }
}

public sealed class UnifiedItemPublishedDeleteException : Exception
{
    public UnifiedItemPublishedDeleteException(string itemId)
        : base($"Item '{itemId}' must be disabled before it can be deleted.")
    {
    }
}

public sealed class UnifiedItemReferencedByPublishedDialogueException : Exception
{
    public UnifiedItemReferencedByPublishedDialogueException(
        string itemId,
        string operation,
        IReadOnlyList<string> dialogueDefinitionIds)
        : base($"Item '{itemId}' cannot {operation} while referenced by published dialogue conditions.")
    {
        ItemId = itemId;
        Operation = operation;
        DialogueDefinitionIds = dialogueDefinitionIds;
    }

    public string ItemId { get; }
    public string Operation { get; }
    public IReadOnlyList<string> DialogueDefinitionIds { get; }
}
