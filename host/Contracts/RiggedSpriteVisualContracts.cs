using System.Text.Json.Serialization;
using System.Text.Json;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public static class ActorVisualModes
{
    public const string FlatSprite = "flat_sprite";
    public const string CompositeRig = "composite_rig";
}

public sealed record RiggedSpriteVisualDescriptor(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("rig_id")] string RigId,
    [property: JsonPropertyName("calibration_id")] string? CalibrationId,
    [property: JsonPropertyName("pose_policy")] string PosePolicy,
    [property: JsonPropertyName("fixed_direction")] string? FixedDirection,
    [property: JsonPropertyName("fixed_frame")] int? FixedFrame,
    [property: JsonPropertyName("cosmetic_item_ids")] IReadOnlyDictionary<string, string> CosmeticItemIds)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    public bool ContainsLegacyBaseLayers => ExtensionData?.ContainsKey("base_layers") == true;
}
