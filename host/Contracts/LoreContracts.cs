using MMO.Project.Lore;
// Insight lifecycle payloads; the mirrored rules contract owns authored fields.
using System.Text.Json.Serialization;
namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record LoreDefinition(
    [property: JsonPropertyName("definition_id")] string DefinitionId,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("draft")] LoreSettings Draft,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc)
{
    // Read-only approved progression preview, shared by native and browser editors.
    [JsonPropertyName("mastery_preview")]
    public IReadOnlyList<LoreMilestonePreview> MasteryPreview => Enumerable.Range(1, SlimeMastery.MaximumMilestones)
        .Select(n => new LoreMilestonePreview(n, SlimeMastery.Threshold(n), SlimeMastery.RequiredLevel,
            new[] { "Melee accuracy", "Ranged accuracy", "Magic accuracy", "Melee defence", "Melee strength", "Ranged strength", "Magic strength" }[(n - 1) % 7],
            ((n - 1) / 7 + 1) * 50)).ToArray();
}
public sealed record LoreMilestonePreview(
    [property: JsonPropertyName("milestone")] int Milestone,
    [property: JsonPropertyName("points")] long Points,
    [property: JsonPropertyName("required_level")] int RequiredLevel,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("total_basis_points")] int TotalBasisPoints);

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
