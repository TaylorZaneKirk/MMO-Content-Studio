using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record NpcCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<NpcDefinitionSummary> Items);

public sealed record NpcDefinitionSummary(
    [property: JsonPropertyName("npc_definition_id")] string NpcDefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("movement_behavior")] string MovementBehavior,
    [property: JsonPropertyName("interaction_enabled")] bool InteractionEnabled,
    [property: JsonPropertyName("default_dialogue_id")] string? DefaultDialogueId,
    [property: JsonPropertyName("editable_in_npcs")] bool EditableInNpcs,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("visual_mode")] string VisualMode = ActorVisualModes.FlatSprite,
    [property: JsonPropertyName("composite_visual")] RiggedSpriteVisualDescriptor? CompositeVisual = null);

public sealed record NpcDefinition(
    [property: JsonPropertyName("npc_definition_id")] string NpcDefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("visual_anchor_offset_x")] double VisualAnchorOffsetX,
    [property: JsonPropertyName("visual_anchor_offset_y")] double VisualAnchorOffsetY,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("movement_behavior")] string MovementBehavior,
    [property: JsonPropertyName("wander_radius_tiles")] int WanderRadiusTiles,
    [property: JsonPropertyName("tick_interval_ms")] int TickIntervalMs,
    [property: JsonPropertyName("idle_chance")] double IdleChance,
    [property: JsonPropertyName("interaction_enabled")] bool InteractionEnabled,
    [property: JsonPropertyName("interaction_range_tiles")] int InteractionRangeTiles,
    [property: JsonPropertyName("default_interaction")] string DefaultInteraction,
    [property: JsonPropertyName("default_dialogue_id")] string? DefaultDialogueId,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath,
    [property: JsonPropertyName("visual_mode")] string VisualMode = ActorVisualModes.FlatSprite,
    [property: JsonPropertyName("composite_visual")] RiggedSpriteVisualDescriptor? CompositeVisual = null);

public sealed record NpcDraft(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("visual_anchor_offset_x")] double VisualAnchorOffsetX,
    [property: JsonPropertyName("visual_anchor_offset_y")] double VisualAnchorOffsetY,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("movement_behavior")] string MovementBehavior,
    [property: JsonPropertyName("wander_radius_tiles")] int WanderRadiusTiles,
    [property: JsonPropertyName("tick_interval_ms")] int TickIntervalMs,
    [property: JsonPropertyName("idle_chance")] double IdleChance,
    [property: JsonPropertyName("interaction_enabled")] bool InteractionEnabled,
    [property: JsonPropertyName("interaction_range_tiles")] int InteractionRangeTiles,
    [property: JsonPropertyName("default_interaction")] string DefaultInteraction,
    [property: JsonPropertyName("default_dialogue_id")] string? DefaultDialogueId,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature,
    [property: JsonPropertyName("visual_mode")] string VisualMode = ActorVisualModes.FlatSprite,
    [property: JsonPropertyName("composite_visual")] RiggedSpriteVisualDescriptor? CompositeVisual = null);

public sealed record PreviewNpcRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("visual_anchor_offset_x")] double VisualAnchorOffsetX,
    [property: JsonPropertyName("visual_anchor_offset_y")] double VisualAnchorOffsetY,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("movement_behavior")] string MovementBehavior,
    [property: JsonPropertyName("wander_radius_tiles")] int WanderRadiusTiles,
    [property: JsonPropertyName("tick_interval_ms")] int TickIntervalMs,
    [property: JsonPropertyName("idle_chance")] double IdleChance,
    [property: JsonPropertyName("interaction_enabled")] bool InteractionEnabled,
    [property: JsonPropertyName("interaction_range_tiles")] int InteractionRangeTiles,
    [property: JsonPropertyName("default_interaction")] string DefaultInteraction,
    [property: JsonPropertyName("default_dialogue_id")] string? DefaultDialogueId,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("visual_mode")] string VisualMode = ActorVisualModes.FlatSprite,
    [property: JsonPropertyName("composite_visual")] RiggedSpriteVisualDescriptor? CompositeVisual = null);

public sealed record SaveNpcDraftRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("visual_anchor_offset_x")] double VisualAnchorOffsetX,
    [property: JsonPropertyName("visual_anchor_offset_y")] double VisualAnchorOffsetY,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("movement_behavior")] string MovementBehavior,
    [property: JsonPropertyName("wander_radius_tiles")] int WanderRadiusTiles,
    [property: JsonPropertyName("tick_interval_ms")] int TickIntervalMs,
    [property: JsonPropertyName("idle_chance")] double IdleChance,
    [property: JsonPropertyName("interaction_enabled")] bool InteractionEnabled,
    [property: JsonPropertyName("interaction_range_tiles")] int InteractionRangeTiles,
    [property: JsonPropertyName("default_interaction")] string DefaultInteraction,
    [property: JsonPropertyName("default_dialogue_id")] string? DefaultDialogueId,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature,
    [property: JsonPropertyName("visual_mode")] string VisualMode = ActorVisualModes.FlatSprite,
    [property: JsonPropertyName("composite_visual")] RiggedSpriteVisualDescriptor? CompositeVisual = null);

public sealed record NpcPublicationRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record NpcDeleteRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record NpcPreviewResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<AuthoringChange> Changes,
    [property: JsonPropertyName("asset_preview_file_path")] string? AssetPreviewFilePath,
    [property: JsonPropertyName("reference_summary")] NpcReferenceSummary ReferenceSummary,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature);

public sealed record NpcMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("npc")] NpcDefinition Npc,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record NpcDeleteResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("deleted_id")] string DeletedId,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record NpcOptionsResponse(
    [property: JsonPropertyName("publication_states")] IReadOnlyList<AuthoringOption> PublicationStates,
    [property: JsonPropertyName("movement_behaviors")] IReadOnlyList<AuthoringOption> MovementBehaviors,
    [property: JsonPropertyName("interaction_types")] IReadOnlyList<AuthoringOption> InteractionTypes,
    [property: JsonPropertyName("dialogue_references")] IReadOnlyList<AuthoringOption> DialogueReferences,
    [property: JsonPropertyName("can_validate_dialogue_references")] bool CanValidateDialogueReferences,
    [property: JsonPropertyName("supported_limits")] NpcSupportedLimits SupportedLimits,
    [property: JsonPropertyName("visual_assets")] NpcVisualAssetOptions VisualAssets,
    [property: JsonPropertyName("capabilities")] NpcOperationCapabilities Capabilities,
    [property: JsonPropertyName("defaults")] NpcAuthoringDefaults Defaults);

public sealed record NpcOperationCapabilities(
    [property: JsonPropertyName("supports_runtime_npc_catalog")] bool SupportsRuntimeNpcCatalog,
    [property: JsonPropertyName("supports_complete_dialogue_reference_validation")] bool SupportsCompleteDialogueReferenceValidation,
    [property: JsonPropertyName("supports_multiple_interactions")] bool SupportsMultipleInteractions,
    [property: JsonPropertyName("supports_quest_authoring")] bool SupportsQuestAuthoring);

public sealed record NpcReferenceSummary(
    [property: JsonPropertyName("known_reference_count")] int KnownReferenceCount,
    [property: JsonPropertyName("reference_sources")] IReadOnlyList<string> ReferenceSources,
    [property: JsonPropertyName("reference_check_complete")] bool ReferenceCheckComplete);

public sealed record NpcSupportedLimits(
    [property: JsonPropertyName("minimum_tick_interval_ms")] int MinimumTickIntervalMs,
    [property: JsonPropertyName("minimum_interaction_range_tiles")] int MinimumInteractionRangeTiles,
    [property: JsonPropertyName("initial_footprint_width_tiles")] int InitialFootprintWidthTiles,
    [property: JsonPropertyName("initial_footprint_height_tiles")] int InitialFootprintHeightTiles,
    [property: JsonPropertyName("max_wander_radius_tiles")] int MaxWanderRadiusTiles);

public sealed record NpcVisualAssetOptions(
    [property: JsonPropertyName("can_resolve_previews")] bool CanResolvePreviews,
    [property: JsonPropertyName("resource_prefix")] string ResourcePrefix,
    [property: JsonPropertyName("game_assets_root")] string? GameAssetsRoot);

public sealed record NpcAuthoringDefaults(
    [property: JsonPropertyName("movement_behavior")] string MovementBehavior,
    [property: JsonPropertyName("wander_radius_tiles")] int WanderRadiusTiles,
    [property: JsonPropertyName("tick_interval_ms")] int TickIntervalMs,
    [property: JsonPropertyName("idle_chance")] double IdleChance,
    [property: JsonPropertyName("interaction_enabled")] bool InteractionEnabled,
    [property: JsonPropertyName("interaction_range_tiles")] int InteractionRangeTiles,
    [property: JsonPropertyName("default_interaction")] string DefaultInteraction,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale);
