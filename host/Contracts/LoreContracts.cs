using MMO.Project.Lore;
// Recipe lifecycle payloads; the mirrored recipe contract owns authored fields.
using System.Text.Json.Serialization;
namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record LoreDefinition(
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("draft")] LoreSettings Draft,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record LoreSummary(
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState);

public sealed record LoreCatalogResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<LoreSummary> Items);

public sealed record LoreRequest(
    [property: JsonPropertyName("draft")] LoreSettings Draft,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature,
    [property: JsonPropertyName("target_operation")] string? TargetOperation);

public sealed record LorePreview(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("applicable")] bool Applicable,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<LoreChange> Changes);

public sealed record LoreChange(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("before")] object? Before,
    [property: JsonPropertyName("after")] object? After);

public sealed record LoreMutation(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("definition")] LoreDefinition? Definition,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);
