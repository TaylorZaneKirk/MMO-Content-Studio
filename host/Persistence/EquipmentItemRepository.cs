using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class EquipmentItemRepository
{
    private static readonly HashSet<string> WearableSlotIds = new(StringComparer.Ordinal)
    {
        "head",
        "cape",
        "body",
        "legs",
        "boots",
        "gloves",
        "ring"
    };

    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public EquipmentItemRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
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

    public async Task<IReadOnlyList<EquipmentItemRecord>> ListAsync(
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
                exists (select 1 from item_skill_modifiers m where m.item_id = i.item_id) as has_skill_modifiers
            from item_definitions i
            left join equipment_slot_definitions slot on slot.slot_id = i.equipment_slot_id
            where @search is null
               or i.item_id ilike '%' || @search || '%'
               or i.item_name ilike '%' || @search || '%'
               or i.equipment_slot_id ilike '%' || @search || '%'
            order by
                case when i.equipment_slot_id is null then 1 else 0 end,
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
        var records = new List<EquipmentItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader, [], [], null, null));
        }

        return records;
    }

    public async Task<EquipmentItemRecord?> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAggregateAsync(connection, null, itemId, false, cancellationToken);
    }

    public async Task<EquipmentItemRecord> SaveDraftAsync(
        string itemId,
        NormalizedEquipmentDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new EquipmentItemNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureNotConsumable(existing);

        if (draft.Equippable && existing.HasCombatProfile)
        {
            throw new EquipmentKindConflictException(
                itemId,
                "Items with combat profiles must be edited in the T3B Weapons and Tools workspace. Turn off Equippable to deliberately remove all equipment and combat metadata.");
        }

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

        if (draft.Equippable)
        {
            await ReplaceRequirementsAsync(connection, transaction, itemId, draft.Requirements, cancellationToken);
            await ReplaceModifiersAsync(connection, transaction, itemId, draft.SkillModifiers, cancellationToken);
            await ReplaceCombatBonusesAsync(connection, transaction, itemId, draft.CombatBonuses, cancellationToken);
        }
        else
        {
            await DeleteEquipmentMetadataAsync(connection, transaction, itemId, cancellationToken);
        }

        var saved = await LoadAggregateAsync(connection, transaction, itemId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved equipment item could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<EquipmentItemRecord> SetPublicationAsync(
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
            ?? throw new InvalidOperationException("Equipment publication change could not be reloaded inside its transaction.");
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
            ?? throw new EquipmentItemNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureNotConsumable(existing);
        if (existing.RuntimeEnabled)
        {
            throw new EquipmentPublishedDeleteException(itemId);
        }

        await DeleteEquipmentMetadataAsync(connection, transaction, itemId, cancellationToken);

        const string sql = "delete from item_definitions where item_id = @item_id;";
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public static bool IsWearableSlot(string? slotId) =>
        slotId is not null && WearableSlotIds.Contains(slotId);

    public static bool IsHandSlot(string? slotId) =>
        slotId is "left_hand" or "right_hand";

    public static bool HasEquipmentMetadata(EquipmentItemRecord record) =>
        record.EquipmentSlotId is not null
        || record.RequiredStrength != 1
        || record.HasCombatProfile
        || record.HasCombatBonuses
        || record.HasSkillRequirements
        || record.HasSkillModifiers;

    private static async Task<EquipmentItemRecord?> LoadAggregateAsync(
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
            CombatProfile = await LoadCombatProfileAsync(connection, transaction, itemId, cancellationToken),
            CombatBonuses = await LoadCombatBonusesAsync(connection, transaction, itemId, cancellationToken)
        };
    }

    private static async Task<EquipmentItemRecord?> LoadBaseAsync(
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
                exists (select 1 from item_skill_modifiers m where m.item_id = i.item_id) as has_skill_modifiers
            from item_definitions i
            left join equipment_slot_definitions slot on slot.slot_id = i.equipment_slot_id
            where i.item_id = @item_id
            """ + (forUpdate ? " for update of i;" : ";");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRecord(reader, [], [], null, null)
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

    private static async Task<EquipmentCombatProfileDefinition?> LoadCombatProfileAsync(
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

    private static async Task DeleteEquipmentMetadataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        await ExecuteDeleteAsync(connection, transaction, "item_skill_requirements", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_skill_modifiers", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_combat_profiles", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_combat_bonuses", itemId, cancellationToken);
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

    private static EquipmentItemRecord ReadRecord(
        NpgsqlDataReader reader,
        IReadOnlyList<EquipmentSkillRequirementDefinition> requirements,
        IReadOnlyList<EquipmentSkillModifierDefinition> modifiers,
        EquipmentCombatProfileDefinition? combatProfile,
        EquipmentCombatBonusDefinition? combatBonuses)
    {
        var slotOrdinal = reader.GetOrdinal("equipment_slot_id");
        var slotDisplayOrdinal = reader.GetOrdinal("equipment_slot_display_name");
        return new EquipmentItemRecord(
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
            requirements,
            modifiers,
            combatProfile,
            combatBonuses,
            ReadUtc(reader, "updated_at"));
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(reader.GetOrdinal(column)), DateTimeKind.Utc));

    private static void EnsureNotConsumable(EquipmentItemRecord existing)
    {
        if (existing.HasConsumableProfile)
        {
            throw new EquipmentKindConflictException(
                existing.ItemId,
                "Consumable items must be edited in the Consumables workspace.");
        }
    }

    private static void EnsureExpectedVersion(
        EquipmentItemRecord existing,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new EquipmentConcurrencyException(existing.ItemId, existing.UpdatedAtUtc);
        }
    }
}

public sealed record EquipmentSlotRecord(string SlotId, string DisplayName);
public sealed record EquipmentSkillRecord(string SkillId, string DisplayName);

public sealed record EquipmentItemRecord(
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
    IReadOnlyList<EquipmentSkillRequirementDefinition> Requirements,
    IReadOnlyList<EquipmentSkillModifierDefinition> SkillModifiers,
    EquipmentCombatProfileDefinition? CombatProfile,
    EquipmentCombatBonusDefinition? CombatBonuses,
    DateTimeOffset UpdatedAtUtc);

public sealed class EquipmentItemNotFoundException : Exception
{
    public EquipmentItemNotFoundException(string itemId) : base($"Item '{itemId}' does not exist.") { }
}

public sealed class EquipmentKindConflictException : Exception
{
    public EquipmentKindConflictException(string itemId, string message)
        : base($"Item '{itemId}' cannot be edited here. {message}") { }
}

public sealed class EquipmentConcurrencyException : Exception
{
    public EquipmentConcurrencyException(string itemId, DateTimeOffset currentUpdatedAtUtc)
        : base($"Item '{itemId}' changed after it was loaded. Reload it before saving.")
    {
        CurrentUpdatedAtUtc = currentUpdatedAtUtc;
    }

    public DateTimeOffset CurrentUpdatedAtUtc { get; }
}

public sealed class EquipmentPublishedDeleteException : Exception
{
    public EquipmentPublishedDeleteException(string itemId)
        : base($"Equipment item '{itemId}' must be disabled before it can be deleted.")
    {
    }
}
