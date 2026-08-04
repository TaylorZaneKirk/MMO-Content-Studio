using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

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

public sealed record AuthoringOption(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("display_name")] string DisplayName);
