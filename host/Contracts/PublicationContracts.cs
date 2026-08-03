using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record PublicationMutationRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc);

public sealed record DeleteMutationRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record DeleteMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("deleted_id")] string DeletedId,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);
