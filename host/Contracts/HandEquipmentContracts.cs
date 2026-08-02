using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record HandEquipmentCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<HandEquipmentItemSummary> Items);

public sealed record HandEquipmentItemSummary(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("classification_label")] string ClassificationLabel,
    [property: JsonPropertyName("equippable")] bool Equippable,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("equipment_slot_display_name")] string? EquipmentSlotDisplayName,
    [property: JsonPropertyName("has_weapon_profile")] bool HasWeaponProfile,
    [property: JsonPropertyName("has_tool_capabilities")] bool HasToolCapabilities,
    [property: JsonPropertyName("editable_in_hand_equipment")] bool EditableInHandEquipment,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record HandEquipmentItemDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("classification_label")] string ClassificationLabel,
    [property: JsonPropertyName("equippable")] bool Equippable,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("equipment_slot_display_name")] string? EquipmentSlotDisplayName,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDefinition> Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDefinition> SkillModifiers,
    [property: JsonPropertyName("weapon_profile")] EquipmentCombatProfileDefinition? WeaponProfile,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("tool_capabilities")] IReadOnlyList<HandEquipmentToolCapabilityDefinition> ToolCapabilities,
    [property: JsonPropertyName("editable_in_hand_equipment")] bool EditableInHandEquipment,
    [property: JsonPropertyName("visual_asset_key")] string? VisualAssetKey,
    [property: JsonPropertyName("visual_asset_model")] string VisualAssetModel,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record HandEquipmentToolCapabilityDefinition(
    [property: JsonPropertyName("capability_id")] string CapabilityId,
    [property: JsonPropertyName("capability_display_name")] string CapabilityDisplayName,
    [property: JsonPropertyName("capability_order")] int CapabilityOrder,
    [property: JsonPropertyName("power_tier")] int PowerTier,
    [property: JsonPropertyName("action_animation_id")] string? ActionAnimationId,
    [property: JsonPropertyName("effect_resource_id")] string? EffectResourceId);

public sealed record HandEquipmentToolCapabilityDraft(
    [property: JsonPropertyName("capability_id")] string CapabilityId,
    [property: JsonPropertyName("power_tier")] int PowerTier,
    [property: JsonPropertyName("action_animation_id")] string? ActionAnimationId,
    [property: JsonPropertyName("effect_resource_id")] string? EffectResourceId);

public sealed record SaveHandEquipmentDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("equippable")] bool Equippable,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDraft>? Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDraft>? SkillModifiers,
    [property: JsonPropertyName("weapon_profile")] EquipmentCombatProfileDefinition? WeaponProfile,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("tool_capabilities")] IReadOnlyList<HandEquipmentToolCapabilityDraft>? ToolCapabilities,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record HandEquipmentPreviewRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("equippable")] bool Equippable,
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDraft>? Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDraft>? SkillModifiers,
    [property: JsonPropertyName("weapon_profile")] EquipmentCombatProfileDefinition? WeaponProfile,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("tool_capabilities")] IReadOnlyList<HandEquipmentToolCapabilityDraft>? ToolCapabilities,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record HandEquipmentPublicationRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record HandEquipmentValidationResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<BasicItemChange> Changes,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature);

public sealed record HandEquipmentMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("item")] HandEquipmentItemDefinition Item,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record HandEquipmentAuthoringOptionsResponse(
    [property: JsonPropertyName("hand_slots")] IReadOnlyList<AuthoringOption> HandSlots,
    [property: JsonPropertyName("wearable_slots")] IReadOnlyList<AuthoringOption> WearableSlots,
    [property: JsonPropertyName("skills")] IReadOnlyList<AuthoringOption> Skills,
    [property: JsonPropertyName("combat_bonus_fields")] IReadOnlyList<AuthoringOption> CombatBonusFields,
    [property: JsonPropertyName("attack_families")] IReadOnlyList<AuthoringOption> AttackFamilies,
    [property: JsonPropertyName("attack_styles")] IReadOnlyList<AuthoringOption> AttackStyles,
    [property: JsonPropertyName("tool_capabilities")] IReadOnlyList<AuthoringOption> ToolCapabilities,
    [property: JsonPropertyName("weapon_animation_refs")] IReadOnlyList<AuthoringOption> WeaponAnimationRefs,
    [property: JsonPropertyName("supports_direct_visual_asset_override")] bool SupportsDirectVisualAssetOverride,
    [property: JsonPropertyName("visual_asset_model_message")] string VisualAssetModelMessage);
