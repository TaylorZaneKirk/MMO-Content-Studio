using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record ConsumableRequirementDefinition(
    [property: JsonPropertyName("requirement_index")] int RequirementIndex,
    [property: JsonPropertyName("requirement_type")] string RequirementType,
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("minimum_value")] int MinimumValue);

// Authored restoration is one fixed amount; gameplay applies it to the target resource.
public sealed record ConsumableEffectDefinition(
    [property: JsonPropertyName("effect_index")] int EffectIndex,
    [property: JsonPropertyName("effect_type")] string EffectType,
    [property: JsonPropertyName("target_id")] string TargetId,
    [property: JsonPropertyName("amount")] int Amount);

public sealed record AuthoringOption(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName);
