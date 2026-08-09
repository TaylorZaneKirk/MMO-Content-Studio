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
    IReadOnlyList<PublishedEquippedVisualDefinition> EquippedVisuals);

public sealed record ActorRigCalibrationDefinition(
    string CalibrationId,
    string RigId,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>>? SocketOverrides = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelRectangleDefinition>>>? ForegroundOverlayOverrides = null);

public sealed record PublishedEquippedVisualDefinition(
    string ItemId,
    string RigId,
    string BindingType,
    string RenderLayerId,
    string? AssetKey = null,
    string? SocketId = null,
    SourcePixelPointDefinition? Nudge = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>? GripAnchors = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? FlipPoses = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? HiddenPoses = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>>? ItemOverGripPoses = null);

public sealed record ActorAppearanceOptionsDefinition(
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("visual_modes")] IReadOnlyList<AuthoringOption> VisualModes,
    [property: JsonPropertyName("rigs")] IReadOnlyList<ActorRigDefinition> Rigs,
    [property: JsonPropertyName("calibrations")] IReadOnlyList<ActorRigCalibrationDefinition> Calibrations,
    [property: JsonPropertyName("equipped_visuals")] IReadOnlyList<PublishedEquippedVisualDefinition> EquippedVisuals);

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
    [property: JsonPropertyName("source_rect")] SourcePixelRectangleDefinition SourceRect,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("z_index")] int ZIndex);
