using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record BasicItemCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<BasicItemSummary> Items);

public sealed record BasicItemSummary(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record BasicItemDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("editable_in_basic_items")] bool EditableInBasicItems,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record SaveBasicItemDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc);

public sealed record BasicItemPreviewRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record BasicItemValidationResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<BasicItemChange> Changes,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record BasicItemChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] string? Before,
    [property: JsonPropertyName("after")] string? After);

public sealed record BasicItemMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("item")] BasicItemDefinition Item,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record ItemCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<ItemDefinitionSummary> Items);

public sealed record ItemDefinitionSummary(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("classification_label")] string ClassificationLabel,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("has_consumable_behavior")] bool HasConsumableBehavior,
    [property: JsonPropertyName("has_equipment_metadata")] bool HasEquipmentMetadata,
    [property: JsonPropertyName("has_weapon_profile")] bool HasWeaponProfile,
    [property: JsonPropertyName("has_tool_capabilities")] bool HasToolCapabilities,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record ItemDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("classification_label")] string ClassificationLabel,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("editable_in_basic_items")] bool EditableInBasicItems,
    [property: JsonPropertyName("consumable_behavior")] ItemConsumableBehaviorDefinition? ConsumableBehavior,
    [property: JsonPropertyName("equipment")] ItemEquipmentMetadataDefinition? Equipment,
    [property: JsonPropertyName("tool_capabilities")] IReadOnlyList<HandEquipmentToolCapabilityDefinition> ToolCapabilities,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record ItemConsumableBehaviorDefinition(
    [property: JsonPropertyName("use_action")] string UseAction,
    [property: JsonPropertyName("consume_quantity")] int ConsumeQuantity,
    [property: JsonPropertyName("result_item_id")] string? ResultItemId,
    [property: JsonPropertyName("success_message")] string? SuccessMessage,
    [property: JsonPropertyName("usable_in_combat")] bool UsableInCombat,
    [property: JsonPropertyName("cooldown_ms")] int CooldownMs,
    [property: JsonPropertyName("use_animation_id")] string? UseAnimationId,
    [property: JsonPropertyName("use_sound_resource_path")] string? UseSoundResourcePath,
    [property: JsonPropertyName("requirements")] IReadOnlyList<ConsumableRequirementDefinition> Requirements,
    [property: JsonPropertyName("effects")] IReadOnlyList<ConsumableEffectDefinition> Effects);

public sealed record ItemEquipmentMetadataDefinition(
    [property: JsonPropertyName("equipment_slot_id")] string EquipmentSlotId,
    [property: JsonPropertyName("equipment_slot_display_name")] string? EquipmentSlotDisplayName,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDefinition> Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDefinition> SkillModifiers,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition CombatBonuses,
    [property: JsonPropertyName("weapon_profile")] EquipmentCombatProfileDefinition? WeaponProfile);

public sealed record SaveItemDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("consumable_behavior")] ItemConsumableBehaviorDraft? ConsumableBehavior,
    [property: JsonPropertyName("equipment")] ItemEquipmentMetadataDraft? Equipment,
    [property: JsonPropertyName("tool_capabilities")] IReadOnlyList<HandEquipmentToolCapabilityDraft>? ToolCapabilities,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record PreviewItemRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("consumable_behavior")] ItemConsumableBehaviorDraft? ConsumableBehavior,
    [property: JsonPropertyName("equipment")] ItemEquipmentMetadataDraft? Equipment,
    [property: JsonPropertyName("tool_capabilities")] IReadOnlyList<HandEquipmentToolCapabilityDraft>? ToolCapabilities,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record ItemConsumableBehaviorDraft(
    [property: JsonPropertyName("use_action")] string UseAction,
    [property: JsonPropertyName("consume_quantity")] int ConsumeQuantity,
    [property: JsonPropertyName("result_item_id")] string? ResultItemId,
    [property: JsonPropertyName("success_message")] string? SuccessMessage,
    [property: JsonPropertyName("usable_in_combat")] bool UsableInCombat,
    [property: JsonPropertyName("cooldown_ms")] int CooldownMs,
    [property: JsonPropertyName("use_animation_id")] string? UseAnimationId,
    [property: JsonPropertyName("use_sound_resource_path")] string? UseSoundResourcePath,
    [property: JsonPropertyName("requirements")] IReadOnlyList<ConsumableRequirementDefinition>? Requirements,
    [property: JsonPropertyName("effects")] IReadOnlyList<ConsumableEffectDefinition>? Effects);

public sealed record ItemEquipmentMetadataDraft(
    [property: JsonPropertyName("equipment_slot_id")] string? EquipmentSlotId,
    [property: JsonPropertyName("required_strength")] int RequiredStrength,
    [property: JsonPropertyName("requirements")] IReadOnlyList<EquipmentSkillRequirementDraft>? Requirements,
    [property: JsonPropertyName("skill_modifiers")] IReadOnlyList<EquipmentSkillModifierDraft>? SkillModifiers,
    [property: JsonPropertyName("combat_bonuses")] EquipmentCombatBonusDefinition? CombatBonuses,
    [property: JsonPropertyName("weapon_profile")] EquipmentCombatProfileDefinition? WeaponProfile);

public sealed record ItemPreviewResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<BasicItemChange> Changes,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature);

public sealed record ItemMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("item")] ItemDefinition Item,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record ItemOptionsResponse(
    [property: JsonPropertyName("equipment_slots")] IReadOnlyList<AuthoringOption> EquipmentSlots,
    [property: JsonPropertyName("weapon_capable_slots")] IReadOnlyList<AuthoringOption> WeaponCapableSlots,
    [property: JsonPropertyName("skills")] IReadOnlyList<AuthoringOption> Skills,
    [property: JsonPropertyName("combat_bonus_fields")] IReadOnlyList<AuthoringOption> CombatBonusFields,
    [property: JsonPropertyName("attack_families")] IReadOnlyList<AuthoringOption> AttackFamilies,
    [property: JsonPropertyName("attack_styles")] IReadOnlyList<AuthoringOption> AttackStyles,
    [property: JsonPropertyName("tool_capabilities")] IReadOnlyList<AuthoringOption> ToolCapabilities,
    [property: JsonPropertyName("use_actions")] IReadOnlyList<AuthoringOption> UseActions,
    [property: JsonPropertyName("effect_types")] IReadOnlyList<AuthoringOption> EffectTypes,
    [property: JsonPropertyName("resource_targets")] IReadOnlyList<AuthoringOption> ResourceTargets,
    [property: JsonPropertyName("requirement_types")] IReadOnlyList<AuthoringOption> RequirementTypes,
    [property: JsonPropertyName("published_item_references")] IReadOnlyList<AuthoringOption> PublishedItemReferences,
    [property: JsonPropertyName("combat_unit_milliseconds")] int CombatUnitMilliseconds,
    [property: JsonPropertyName("maximum_tool_power_tier")] int MaximumToolPowerTier,
    [property: JsonPropertyName("supports_runtime_tool_resolution")] bool SupportsRuntimeToolResolution);

public sealed record ItemAssetCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("assets")] IReadOnlyList<ItemAssetEntry> Assets);

public sealed record ItemAssetEntry(
    [property: JsonPropertyName("resource_path")] string ResourcePath,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("file_path")] string FilePath);


public sealed record ImportItemAssetRequest(
    [property: JsonPropertyName("source_file_path")] string SourceFilePath,
    [property: JsonPropertyName("target_file_name")] string? TargetFileName);

public sealed record ImportItemAssetResponse(
    [property: JsonPropertyName("asset")] ItemAssetEntry Asset,
    [property: JsonPropertyName("created")] bool Created,
    [property: JsonPropertyName("message")] string Message);
