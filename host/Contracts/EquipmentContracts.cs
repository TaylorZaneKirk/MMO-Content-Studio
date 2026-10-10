// Defines the normalized equipment payload, including separate Magic accuracy and damage percentage facts.
using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record EquipmentSkillRequirementDefinition(
    [property: JsonPropertyName("skill_id")] string SkillId,
    [property: JsonPropertyName("skill_display_name")] string SkillDisplayName,
    [property: JsonPropertyName("required_value")] int RequiredValue);

public sealed record EquipmentSkillRequirementDraft(
    [property: JsonPropertyName("skill_id")] string SkillId,
    [property: JsonPropertyName("required_value")] int RequiredValue);

public sealed record EquipmentSkillModifierDefinition(
    [property: JsonPropertyName("skill_id")] string SkillId,
    [property: JsonPropertyName("skill_display_name")] string SkillDisplayName,
    [property: JsonPropertyName("modifier_value")] int ModifierValue);

public sealed record EquipmentSkillModifierDraft(
    [property: JsonPropertyName("skill_id")] string SkillId,
    [property: JsonPropertyName("modifier_value")] int ModifierValue);

public sealed record EquipmentCombatProfileDefinition(
    [property: JsonPropertyName("profile_id")] string ProfileId,
    [property: JsonPropertyName("attack_type")] string AttackType,
    [property: JsonPropertyName("minimum_range_tiles")] int MinimumRangeTiles,
    [property: JsonPropertyName("maximum_range_tiles")] int MaximumRangeTiles,
    [property: JsonPropertyName("attack_speed_units")] int AttackSpeedUnits,
    [property: JsonPropertyName("ranged_damage_type")] string? RangedDamageType = null,
    [property: JsonPropertyName("ammunition_family")] string? AmmunitionFamily = null,
    [property: JsonPropertyName("maximum_ammunition_tier")] int? MaximumAmmunitionTier = null,
    [property: JsonPropertyName("melee_combat_options")] IReadOnlyList<MeleeCombatOptionDefinition>? MeleeCombatOptions = null);

public sealed record MeleeCombatOptionDefinition(
    [property: JsonPropertyName("option_slot")] short OptionSlot,
    [property: JsonPropertyName("option_id")] string OptionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("combat_style")] string CombatStyle,
    [property: JsonPropertyName("accuracy_style")] string AccuracyStyle);

public sealed record ItemAmmunitionProfileDefinition(
    [property: JsonPropertyName("ammunition_family")] string AmmunitionFamily,
    [property: JsonPropertyName("ranged_damage_type")] string RangedDamageType,
    [property: JsonPropertyName("ammunition_tier")] int? AmmunitionTier = null);

public sealed record EquipmentCombatBonusDefinition(
    [property: JsonPropertyName("attack_thrust")] int AttackThrust,
    [property: JsonPropertyName("attack_slash")] int AttackSlash,
    [property: JsonPropertyName("attack_crush")] int AttackCrush,
    [property: JsonPropertyName("attack_ranged")] int AttackRanged,
    [property: JsonPropertyName("attack_magic")] int AttackMagic,
    [property: JsonPropertyName("strength_melee")] int StrengthMelee,
    [property: JsonPropertyName("strength_ranged")] int StrengthRanged,
    [property: JsonPropertyName("magic_damage_percent")] int MagicDamagePercent,
    [property: JsonPropertyName("defence_thrust")] int DefenceThrust,
    [property: JsonPropertyName("defence_slash")] int DefenceSlash,
    [property: JsonPropertyName("defence_crush")] int DefenceCrush,
    [property: JsonPropertyName("defence_ranged")] int DefenceRanged,
    [property: JsonPropertyName("defence_magic")] int DefenceMagic,
    // 0 disables eligibility. Positive values are explicit per-hand capabilities,
    // not additive defence bonuses; 100 basis points = one percentage point.
    [property: JsonPropertyName("parry_base_chance_basis_points")] int ParryBaseChanceBasisPoints = 0,
    [property: JsonPropertyName("block_base_chance_basis_points")] int BlockBaseChanceBasisPoints = 0)
{
    public static EquipmentCombatBonusDefinition Zero { get; } = new(
        0, 0, 0, 0, 0,
        0, 0, 0,
        0, 0, 0, 0, 0);

    public bool IsZero => this == Zero;
}
