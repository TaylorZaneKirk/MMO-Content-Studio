using MMO.Project.Prototype.Server.Game;
// Method lifecycle payloads; the mirrored method contract owns authored fields.
using System.Text.Json.Serialization;
namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record FishingDefinition(
    [property: JsonPropertyName("action_id")] string DefinitionId,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("draft")] FishingMethod Draft,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("has_published_snapshot")] bool HasPublishedSnapshot = false);

public sealed record FishingSummary(
    [property: JsonPropertyName("action_id")] string DefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState);

public sealed record FishingCatalogResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<FishingSummary> Items);

public sealed record FishingRequest(
    [property: JsonPropertyName("draft")] FishingMethod Draft,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature,
    [property: JsonPropertyName("target_operation")] string? TargetOperation);

public sealed record FishingPreview(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("applicable")] bool Applicable,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<FishingChange> Changes);

public sealed record FishingChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] object? Before,
    [property: JsonPropertyName("after")] object? After);

public sealed record FishingMutation(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("definition")] FishingDefinition? Definition,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);
