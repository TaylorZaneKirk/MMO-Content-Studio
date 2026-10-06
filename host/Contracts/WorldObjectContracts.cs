// Complete reusable World Object authoring payloads; placements remain in Tiled.
using System.Text.Json.Serialization;
namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record WorldObjectDraft(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("blocks_movement")] bool BlocksMovement,
    [property: JsonPropertyName("footprint_width_tiles")] int FootprintWidthTiles,
    [property: JsonPropertyName("footprint_height_tiles")] int FootprintHeightTiles,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("visual_anchor_offset_x")] double VisualAnchorOffsetX,
    [property: JsonPropertyName("visual_anchor_offset_y")] double VisualAnchorOffsetY,
    [property: JsonPropertyName("visual_render_scale")] double VisualRenderScale,
    [property: JsonPropertyName("visual_animation_fps")] double? VisualAnimationFps,
    [property: JsonPropertyName("public_interactions")] IReadOnlyList<WorldObjectInteraction> PublicInteractions,
    [property: JsonPropertyName("visual_animation_frames")] IReadOnlyList<string> VisualAnimationFrames);

public sealed record WorldObjectInteraction(
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("is_default")] bool IsDefault);

public sealed record WorldObjectDefinition(
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("draft")] WorldObjectDraft Draft,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record WorldObjectSummary(
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState);

public sealed record WorldObjectCatalogResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<WorldObjectSummary> Items);

public sealed record WorldObjectRequest(
    [property: JsonPropertyName("draft")] WorldObjectDraft Draft,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature,
    [property: JsonPropertyName("target_operation")] string? TargetOperation);

public sealed record WorldObjectPreview(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("applicable")] bool Applicable,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<WorldObjectChange> Changes);

public sealed record WorldObjectChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] object? Before,
    [property: JsonPropertyName("after")] object? After);

public sealed record WorldObjectMutation(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("definition")] WorldObjectDefinition? Definition,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record WorldObjectOptions(
    [property: JsonPropertyName("publication_states")] string[] PublicationStates,
    [property: JsonPropertyName("game_assets_root")] string? GameAssetsRoot,
    [property: JsonPropertyName("defaults")] WorldObjectDraft Defaults);
