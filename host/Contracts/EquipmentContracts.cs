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
    [property: JsonPropertyName("equippable")] bool Equippable,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("equipment_slot_display_name")] string? EquipmentSlotDisplayName,
    [property: JsonPropertyName("editable_in_equipment")] bool EditableInEquipment,
    [property: JsonPropertyName("can_remove_equipability")] bool CanRemoveEquipability,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record EquipmentItemDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("equippable")] bool Equippable,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("equipment_slot_display_name")] string? EquipmentSlotDisplayName,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDefinition> Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDefinition> SkillModifiers,
    [property: JsonPropertyName("combat_profile")] EquipmentCombatProfileDefinition? CombatProfile,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("editable_in_equipment")] bool EditableInEquipment,
    [property: JsonPropertyName("can_remove_equipability")] bool CanRemoveEquipability,
    [property: JsonPropertyName("visual_asset_key")] string? VisualAssetKey,
    [property: JsonPropertyName("visual_asset_model")] string VisualAssetModel,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

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

public sealed record SaveEquipmentDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("equippable")] bool Equippable,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDraft>? Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDraft>? SkillModifiers,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc);

public sealed record EquipmentPreviewRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("equippable")] bool Equippable,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDraft>? Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDraft>? SkillModifiers,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record EquipmentValidationResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<BasicItemChange> Changes,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record EquipmentMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("item")] EquipmentItemDefinition Item,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record EquipmentAuthoringOptionsResponse(
    [property: JsonPropertyName("wearable_slots")] IReadOnlyList<AuthoringOption> WearableSlots,
    [property: JsonPropertyName("deferred_hand_slots")] IReadOnlyList<AuthoringOption> DeferredHandSlots,
    [property: JsonPropertyName("skills")] IReadOnlyList<AuthoringOption> Skills,
    [property: JsonPropertyName("combat_bonus_fields")] IReadOnlyList<AuthoringOption> CombatBonusFields,
    [property: JsonPropertyName("supports_direct_visual_asset_override")] bool SupportsDirectVisualAssetOverride,
    [property: JsonPropertyName("visual_asset_model_message")] string VisualAssetModelMessage);
