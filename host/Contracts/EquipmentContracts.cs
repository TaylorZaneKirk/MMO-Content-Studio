using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record EquipmentCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<EquipmentItemSummary> Items);

public sealed record EquipmentItemSummary(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("equipment_slot_display_name")] string? EquipmentSlotDisplayName,
    [property: JsonPropertyName("editable_in_equipment")] bool EditableInEquipment,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record EquipmentItemDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("equipment_slot_display_name")] string? EquipmentSlotDisplayName,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDefinition> Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDefinition> SkillModifiers,
    [property: JsonPropertyName("combat_profile")] EquipmentCombatProfileDefinition? CombatProfile,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("editable_in_equipment")] bool EditableInEquipment,
    [property: JsonPropertyName("visual_asset_key")] string? VisualAssetKey,
    [property: JsonPropertyName("visual_asset_model")] string VisualAssetModel,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record EquipmentSkillRequirementDefinition(
    [property: JsonPropertyName("skill_id")] string SkillId,
    [property: JsonPropertyName("skill_display_name")] string SkillDisplayName,
    [property: JsonPropertyName("required_value")] int RequiredValue);

public sealed record EquipmentSkillModifierDefinition(
    [property: JsonPropertyName("skill_id")] string SkillId,
    [property: JsonPropertyName("skill_display_name")] string SkillDisplayName,
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
    [property: JsonPropertyName("defence_magic")] int DefenceMagic);

public sealed record EquipmentAuthoringOptionsResponse(
    [property: JsonPropertyName("wearable_slots")] IReadOnlyList<AuthoringOption> WearableSlots,
    [property: JsonPropertyName("deferred_hand_slots")] IReadOnlyList<AuthoringOption> DeferredHandSlots,
    [property: JsonPropertyName("skills")] IReadOnlyList<AuthoringOption> Skills,
    [property: JsonPropertyName("combat_bonus_fields")] IReadOnlyList<AuthoringOption> CombatBonusFields,
    [property: JsonPropertyName("supports_direct_visual_asset_override")] bool SupportsDirectVisualAssetOverride,
    [property: JsonPropertyName("visual_asset_model_message")] string VisualAssetModelMessage);
