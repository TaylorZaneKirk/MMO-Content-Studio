using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record ConsumableCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<ConsumableItemSummary> Items);

public sealed record ConsumableItemSummary(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("has_consumable_profile")] bool HasConsumableProfile,
    [property: JsonPropertyName("editable_in_consumables")] bool EditableInConsumables,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record ConsumableItemDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("authoring_kind")] string AuthoringKind,
    [property: JsonPropertyName("has_consumable_profile")] bool HasConsumableProfile,
    [property: JsonPropertyName("editable_in_consumables")] bool EditableInConsumables,
    [property: JsonPropertyName("use_action")] string UseAction,
    [property: JsonPropertyName("consume_quantity")] int ConsumeQuantity,
    [property: JsonPropertyName("result_item_id")] string? ResultItemId,
    [property: JsonPropertyName("success_message")] string? SuccessMessage,
    [property: JsonPropertyName("usable_in_combat")] bool UsableInCombat,
    [property: JsonPropertyName("cooldown_ms")] int CooldownMs,
    [property: JsonPropertyName("use_animation_id")] string? UseAnimationId,
    [property: JsonPropertyName("use_sound_resource_path")] string? UseSoundResourcePath,
    [property: JsonPropertyName("requirements")] IReadOnlyList<ConsumableRequirementDefinition> Requirements,
    [property: JsonPropertyName("effects")] IReadOnlyList<ConsumableEffectDefinition> Effects,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record ConsumableRequirementDefinition(
    [property: JsonPropertyName("requirement_index")] int RequirementIndex,
    [property: JsonPropertyName("requirement_type")] string RequirementType,
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("minimum_value")] int MinimumValue);

public sealed record ConsumableEffectDefinition(
    [property: JsonPropertyName("effect_index")] int EffectIndex,
    [property: JsonPropertyName("effect_type")] string EffectType,
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("minimum_amount")] int MinimumAmount,
    [property: JsonPropertyName("maximum_amount")] int MaximumAmount);

public sealed record SaveConsumableDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("use_action")] string UseAction,
    [property: JsonPropertyName("consume_quantity")] int ConsumeQuantity,
    [property: JsonPropertyName("result_item_id")] string? ResultItemId,
    [property: JsonPropertyName("success_message")] string? SuccessMessage,
    [property: JsonPropertyName("usable_in_combat")] bool UsableInCombat,
    [property: JsonPropertyName("cooldown_ms")] int CooldownMs,
    [property: JsonPropertyName("use_animation_id")] string? UseAnimationId,
    [property: JsonPropertyName("use_sound_resource_path")] string? UseSoundResourcePath,
    [property: JsonPropertyName("requirements")] IReadOnlyList<ConsumableRequirementDefinition>? Requirements,
    [property: JsonPropertyName("effects")] IReadOnlyList<ConsumableEffectDefinition>? Effects,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc);

public sealed record ConsumablePreviewRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("icon_texture_path")] string IconTexturePath,
    [property: JsonPropertyName("use_action")] string UseAction,
    [property: JsonPropertyName("consume_quantity")] int ConsumeQuantity,
    [property: JsonPropertyName("result_item_id")] string? ResultItemId,
    [property: JsonPropertyName("success_message")] string? SuccessMessage,
    [property: JsonPropertyName("usable_in_combat")] bool UsableInCombat,
    [property: JsonPropertyName("cooldown_ms")] int CooldownMs,
    [property: JsonPropertyName("use_animation_id")] string? UseAnimationId,
    [property: JsonPropertyName("use_sound_resource_path")] string? UseSoundResourcePath,
    [property: JsonPropertyName("requirements")] IReadOnlyList<ConsumableRequirementDefinition>? Requirements,
    [property: JsonPropertyName("effects")] IReadOnlyList<ConsumableEffectDefinition>? Effects,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record ConsumableValidationResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<BasicItemChange> Changes,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath);

public sealed record ConsumableMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("item")] ConsumableItemDefinition Item,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record ConsumableAuthoringOptionsResponse(
    [property: JsonPropertyName("use_actions")] IReadOnlyList<AuthoringOption> UseActions,
    [property: JsonPropertyName("effect_types")] IReadOnlyList<AuthoringOption> EffectTypes,
    [property: JsonPropertyName("resource_targets")] IReadOnlyList<AuthoringOption> ResourceTargets,
    [property: JsonPropertyName("requirement_types")] IReadOnlyList<AuthoringOption> RequirementTypes,
    [property: JsonPropertyName("skills")] IReadOnlyList<AuthoringOption> Skills,
    [property: JsonPropertyName("supports_instance_charges")] bool SupportsInstanceCharges,
    [property: JsonPropertyName("charge_model_message")] string ChargeModelMessage);

public sealed record AuthoringOption(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName);
