using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record SourcePixelPointDefinition(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

public sealed record SourcePixelRectangleDefinition(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height);

public sealed record ActorRigCatalogDefinition(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("source_path")] string? SourcePath,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("rigs")] IReadOnlyList<ActorRigDefinition> Rigs);

public sealed record ActorRigDefinition(
    [property: JsonPropertyName("rig_id")] string RigId,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("layers")] IReadOnlyList<ActorRigLayerDefinition> Layers,
    [property: JsonPropertyName("sockets")] IReadOnlyList<ActorRigSocketDefinition> Sockets,
    [property: JsonPropertyName("foreground_overlays")] IReadOnlyList<ActorRigForegroundOverlayDefinition> ForegroundOverlays,
    [property: JsonPropertyName("solid_sprite_base_layer_id")] string? SolidSpriteBaseLayerId = null);

public sealed record ActorRigLayerDefinition(
    [property: JsonPropertyName("layer_id")] string LayerId,
    [property: JsonPropertyName("binding_type")] string BindingType,
    [property: JsonPropertyName("default_render_plane")] string DefaultRenderPlane,
    [property: JsonPropertyName("z_index_by_direction")] IReadOnlyDictionary<string, int> ZIndexByDirection);

public sealed record ActorRigSocketDefinition(
    [property: JsonPropertyName("socket_id")] string SocketId,
    [property: JsonPropertyName("positions")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>> Positions);

public sealed record ActorRigForegroundOverlayDefinition(
    [property: JsonPropertyName("overlay_id")] string OverlayId,
    [property: JsonPropertyName("socket_id")] string SocketId,
    [property: JsonPropertyName("source_layer_id")] string SourceLayerId,
    [property: JsonPropertyName("z_index_by_direction")] IReadOnlyDictionary<string, int> ZIndexByDirection,
    [property: JsonPropertyName("source_rect_by_direction")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition?>> SourceRectByDirection);

public sealed record ActorRiggedSpriteCatalogDefinition(
    bool Available,
    string? Message,
    IReadOnlyList<ActorRigDefinition> Rigs,
    IReadOnlyList<ActorRigCalibrationDefinition> Calibrations,
    IReadOnlyList<PublishedEquippedVisualDefinition> EquippedVisuals,
    bool RigsAvailable = false,
    string? RigMessage = null,
    string? RigCatalogPath = null,
    bool CalibrationsAvailable = false,
    string? CalibrationMessage = null,
    string? CalibrationCatalogPath = null,
    bool EquippedVisualsAvailable = false,
    string? EquippedVisualMessage = null,
    string? EquippedVisualCatalogPath = null);

public sealed record ActorRigCalibrationDefinition(
    [property: JsonPropertyName("calibration_id")] string CalibrationId,
    [property: JsonPropertyName("rig_id")] string RigId,
    [property: JsonPropertyName("socket_overrides")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>>? SocketOverrides = null,
    [property: JsonPropertyName("foreground_overlay_overrides")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition>>>? ForegroundOverlayOverrides = null);

public sealed record PublishedEquippedVisualDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("rig_id")] string RigId,
    [property: JsonPropertyName("binding_type")] string BindingType,
    [property: JsonPropertyName("render_layer_id")] string RenderLayerId,
    [property: JsonPropertyName("asset_key")] string? AssetKey = null,
    [property: JsonPropertyName("socket_id")] string? SocketId = null,
    [property: JsonPropertyName("nudge")] SourcePixelPointDefinition? Nudge = null,
    [property: JsonPropertyName("grip_anchors")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>? GripAnchors = null,
    [property: JsonPropertyName("flip_poses")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? FlipPoses = null,
    [property: JsonPropertyName("hidden_poses")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? HiddenPoses = null,
    [property: JsonPropertyName("item_over_grip_poses")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? ItemOverGripPoses = null);

public sealed record ActorAppearanceOptionsDefinition(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("visual_modes")] IReadOnlyList<AuthoringOption> VisualModes,
    [property: JsonPropertyName("rigs")] IReadOnlyList<ActorRigDefinition> Rigs,
    [property: JsonPropertyName("calibrations")] IReadOnlyList<ActorRigCalibrationDefinition> Calibrations,
    [property: JsonPropertyName("equipped_visuals")] IReadOnlyList<PublishedEquippedVisualDefinition> EquippedVisuals,
    [property: JsonPropertyName("rigs_available")] bool RigsAvailable = false,
    [property: JsonPropertyName("rig_message")] string? RigMessage = null,
    [property: JsonPropertyName("rig_catalog_path")] string? RigCatalogPath = null,
    [property: JsonPropertyName("calibrations_available")] bool CalibrationsAvailable = false,
    [property: JsonPropertyName("calibration_message")] string? CalibrationMessage = null,
    [property: JsonPropertyName("calibration_catalog_path")] string? CalibrationCatalogPath = null,
    [property: JsonPropertyName("equipped_visuals_available")] bool EquippedVisualsAvailable = false,
    [property: JsonPropertyName("equipped_visual_message")] string? EquippedVisualMessage = null,
    [property: JsonPropertyName("equipped_visual_catalog_path")] string? EquippedVisualCatalogPath = null);

public sealed record RiggedSpritePreviewDefinition(
    [property: JsonPropertyName("base_file_path")] string BaseFilePath,
    [property: JsonPropertyName("source_width")] int SourceWidth,
    [property: JsonPropertyName("source_height")] int SourceHeight,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("frame")] int Frame,
    [property: JsonPropertyName("cosmetics")] IReadOnlyList<RiggedSpritePreviewCosmeticDefinition> Cosmetics,
    [property: JsonPropertyName("foreground_overlays")] IReadOnlyList<RiggedSpritePreviewOverlayDefinition> ForegroundOverlays);

public sealed record RiggedSpritePreviewCosmeticDefinition(
    [property: JsonPropertyName("item_id")] string ItemId,
    [property: JsonPropertyName("file_path")] string FilePath,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("z_index")] int ZIndex,
    [property: JsonPropertyName("flip_x")] bool FlipX);

public sealed record RiggedSpritePreviewOverlayDefinition(
    [property: JsonPropertyName("overlay_id")] string OverlayId,
    [property: JsonPropertyName("source_rect")] SourcePixelRectangleDefinition SourceRect,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("z_index")] int ZIndex);

public sealed record ActorCalibrationLoadResponse(
    [property: JsonPropertyName("exists")] bool Exists,
    [property: JsonPropertyName("catalog_hash")] string CatalogHash,
    [property: JsonPropertyName("calibration")] JsonElement? Calibration);

public sealed record SaveActorCalibrationRequest(
    [property: JsonPropertyName("expected_catalog_hash")] string ExpectedCatalogHash,
    [property: JsonPropertyName("rig_id")] string RigId,
    [property: JsonPropertyName("socket_overrides")] JsonElement SocketOverrides,
    [property: JsonPropertyName("foreground_overlay_overrides")] JsonElement? ForegroundOverlayOverrides = null,
    [property: JsonPropertyName("actor_kind")] string? ActorKind = null,
    [property: JsonPropertyName("visual_texture_path")] string? VisualTexturePath = null);

public sealed record CalibrationFrameRequest(
    [property: JsonPropertyName("actor_kind")] string ActorKind,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath);

public sealed record ActorCalibrationFrameDefinition(
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("frame")] int Frame,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("file_path")] string? FilePath,
    [property: JsonPropertyName("source_width")] int? SourceWidth,
    [property: JsonPropertyName("source_height")] int? SourceHeight);

public sealed record ActorCalibrationFramesResponse(
    [property: JsonPropertyName("actor_kind")] string ActorKind,
    [property: JsonPropertyName("visual_texture_path")] string VisualTexturePath,
    [property: JsonPropertyName("frames")] IReadOnlyList<ActorCalibrationFrameDefinition> Frames);
