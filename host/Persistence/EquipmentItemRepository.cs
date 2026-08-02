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
                item_definitions.item_id,
                item_definitions.item_name,
                item_definitions.icon_texture_path,
                item_definitions.equipment_slot_id,
                equipment_slot_definitions.display_name as equipment_slot_display_name,
                item_definitions.runtime_enabled,
                item_definitions.required_strength,
                item_definitions.updated_at,
                exists (
                    select 1 from item_consumable_profiles cp
                    where cp.item_id = item_definitions.item_id
                ) as has_consumable_profile,
                exists (
                    select 1 from item_combat_profiles profile
                    where profile.item_id = item_definitions.item_id
                ) as has_combat_profile,
                exists (
                    select 1 from item_combat_bonuses bonuses
                    where bonuses.item_id = item_definitions.item_id
                ) as has_combat_bonuses,
                exists (
                    select 1 from item_skill_requirements requirements
                    where requirements.item_id = item_definitions.item_id
                ) as has_skill_requirements,
                exists (
                    select 1 from item_skill_modifiers modifiers
                    where modifiers.item_id = item_definitions.item_id
                ) as has_skill_modifiers
            from item_definitions
            left join equipment_slot_definitions
                on equipment_slot_definitions.slot_id = item_definitions.equipment_slot_id
            where (
                    item_definitions.equipment_slot_id is not null
                    or item_definitions.required_strength > 1
                    or exists (
                        select 1 from item_combat_profiles profile
                        where profile.item_id = item_definitions.item_id
                    )
                    or exists (
                        select 1 from item_combat_bonuses bonuses
                        where bonuses.item_id = item_definitions.item_id
                    )
                    or exists (
                        select 1 from item_skill_requirements requirements
                        where requirements.item_id = item_definitions.item_id
                    )
                    or exists (
                        select 1 from item_skill_modifiers modifiers
                        where modifiers.item_id = item_definitions.item_id
                    )
                )
              and (
                    @search is null
                    or item_definitions.item_id ilike '%' || @search || '%'
                    or item_definitions.item_name ilike '%' || @search || '%'
                    or item_definitions.equipment_slot_id ilike '%' || @search || '%'
                )
            order by equipment_slot_definitions.sort_order nulls last,
                item_definitions.item_name,
                item_definitions.item_id;
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
        var record = await LoadBaseAsync(connection, itemId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var requirements = await LoadRequirementsAsync(connection, itemId, cancellationToken);
        var modifiers = await LoadModifiersAsync(connection, itemId, cancellationToken);
        var combatProfile = await LoadCombatProfileAsync(connection, itemId, cancellationToken);
        var combatBonuses = await LoadCombatBonusesAsync(connection, itemId, cancellationToken);
        return record with
        {
            Requirements = requirements,
            SkillModifiers = modifiers,
            CombatProfile = combatProfile,
            CombatBonuses = combatBonuses
        };
    }

    public static bool IsWearableSlot(string? slotId) =>
        slotId is not null && WearableSlotIds.Contains(slotId);

    public static bool IsHandSlot(string? slotId) =>
        slotId is "left_hand" or "right_hand";

    private static async Task<EquipmentItemRecord?> LoadBaseAsync(
        NpgsqlConnection connection,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                item_definitions.item_id,
                item_definitions.item_name,
                item_definitions.icon_texture_path,
                item_definitions.equipment_slot_id,
                equipment_slot_definitions.display_name as equipment_slot_display_name,
                item_definitions.runtime_enabled,
                item_definitions.required_strength,
                item_definitions.updated_at,
                exists (
                    select 1 from item_consumable_profiles cp
                    where cp.item_id = item_definitions.item_id
                ) as has_consumable_profile,
                exists (
                    select 1 from item_combat_profiles profile
                    where profile.item_id = item_definitions.item_id
                ) as has_combat_profile,
                exists (
                    select 1 from item_combat_bonuses bonuses
                    where bonuses.item_id = item_definitions.item_id
                ) as has_combat_bonuses,
                exists (
                    select 1 from item_skill_requirements requirements
                    where requirements.item_id = item_definitions.item_id
                ) as has_skill_requirements,
                exists (
                    select 1 from item_skill_modifiers modifiers
                    where modifiers.item_id = item_definitions.item_id
                ) as has_skill_modifiers
            from item_definitions
            left join equipment_slot_definitions
                on equipment_slot_definitions.slot_id = item_definitions.equipment_slot_id
            where item_definitions.item_id = @item_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRecord(reader, [], [], null, null)
            : null;
    }

    private static async Task<IReadOnlyList<EquipmentSkillRequirementDefinition>> LoadRequirementsAsync(
        NpgsqlConnection connection,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                item_skill_requirements.skill_id,
                skill_definitions.display_name,
                item_skill_requirements.required_value
            from item_skill_requirements
            join skill_definitions
                on skill_definitions.skill_id = item_skill_requirements.skill_id
            where item_skill_requirements.item_id = @item_id
            order by skill_definitions.sort_order, skill_definitions.display_name, item_skill_requirements.skill_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
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
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                item_skill_modifiers.skill_id,
                skill_definitions.display_name,
                item_skill_modifiers.modifier_value
            from item_skill_modifiers
            join skill_definitions
                on skill_definitions.skill_id = item_skill_modifiers.skill_id
            where item_skill_modifiers.item_id = @item_id
            order by skill_definitions.sort_order, skill_definitions.display_name, item_skill_modifiers.skill_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
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
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                profile_id,
                attack_type,
                accuracy_style,
                minimum_range_tiles,
                maximum_range_tiles,
                attack_speed_units
            from item_combat_profiles
            where item_id = @item_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
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
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                attack_thrust,
                attack_slash,
                attack_crush,
                attack_ranged,
                attack_magic,
                strength_melee,
                strength_ranged,
                strength_magic,
                defence_thrust,
                defence_slash,
                defence_crush,
                defence_ranged,
                defence_magic
            from item_combat_bonuses
            where item_id = @item_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
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
            new DateTimeOffset(
                DateTime.SpecifyKind(
                    reader.GetFieldValue<DateTime>(reader.GetOrdinal("updated_at")),
                    DateTimeKind.Utc)));
    }
}

public sealed record EquipmentSlotRecord(
    string SlotId,
    string DisplayName);

public sealed record EquipmentSkillRecord(
    string SkillId,
    string DisplayName);

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
