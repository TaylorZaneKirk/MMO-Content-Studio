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
    [property: JsonPropertyName("foreground_overlays")] IReadOnlyList<ActorRigForegroundOverlayDefinition> ForegroundOverlays);

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
    string RigId);

public sealed record PublishedEquippedVisualDefinition(
    string ItemId,
    string RigId,
    string BindingType,
    string RenderLayerId);
