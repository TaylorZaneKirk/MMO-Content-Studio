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
    [property: JsonPropertyName("accuracy_style")] string? AccuracyStyle,
    [property: JsonPropertyName("minimum_range_tiles")] int MinimumRangeTiles,
    [property: JsonPropertyName("maximum_range_tiles")] int MaximumRangeTiles,
    [property: JsonPropertyName("attack_speed_units")] int AttackSpeedUnits);

public sealed record EquipmentCombatBonusDefinition(
    [property: JsonPropertyName("attack_thrust")] int AttackThrust,
    [property: JsonPropertyName("attack_slash")] int AttackSlash,
    [property: JsonPropertyName("attack_crush")] int AttackCrush,
    [property: JsonPropertyName("attack_ranged")] int AttackRanged,
    [property: JsonPropertyName("attack_magic")] int AttackMagic,
    [property: JsonPropertyName("strength_melee")] int StrengthMelee,
    [property: JsonPropertyName("strength_ranged")] int StrengthRanged,
    [property: JsonPropertyName("strength_magic")] int StrengthMagic,
    [property: JsonPropertyName("defence_thrust")] int DefenceThrust,
    [property: JsonPropertyName("defence_slash")] int DefenceSlash,
    [property: JsonPropertyName("defence_crush")] int DefenceCrush,
    [property: JsonPropertyName("defence_ranged")] int DefenceRanged,
    [property: JsonPropertyName("defence_magic")] int DefenceMagic)
{
    public static EquipmentCombatBonusDefinition Zero { get; } = new(
        0, 0, 0, 0, 0,
        0, 0, 0,
        0, 0, 0, 0, 0);

    public bool IsZero => this == Zero;
}
