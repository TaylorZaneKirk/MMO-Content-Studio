using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record QuestDefinition(
    [property: JsonPropertyName("quest_id")] string QuestId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("steps")] IReadOnlyList<QuestStep> Steps,
    [property: JsonPropertyName("transitions")] IReadOnlyList<QuestTransition> Transitions,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record QuestDefinitionSummary(
    [property: JsonPropertyName("quest_id")] string QuestId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("step_count")] int StepCount,
    [property: JsonPropertyName("transition_count")] int TransitionCount,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record QuestCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<QuestDefinitionSummary> Items);

public sealed record QuestOptionsResponse(
    [property: JsonPropertyName("publication_states")] IReadOnlyList<AuthoringOption> PublicationStates,
    [property: JsonPropertyName("quest_statuses")] IReadOnlyList<AuthoringOption> QuestStatuses,
    [property: JsonPropertyName("supported_limits")] QuestSupportedLimits SupportedLimits,
    [property: JsonPropertyName("capabilities")] QuestOperationCapabilities Capabilities,
    [property: JsonPropertyName("defaults")] QuestAuthoringDefaults Defaults);

public sealed record QuestOperationCapabilities(
    [property: JsonPropertyName("supports_runtime_quest_catalog")] bool SupportsRuntimeQuestCatalog,
    [property: JsonPropertyName("supports_dialogue_conditions")] bool SupportsDialogueConditions,
    [property: JsonPropertyName("supports_dialogue_effects")] bool SupportsDialogueEffects,
    [property: JsonPropertyName("supports_objectives")] bool SupportsObjectives,
    [property: JsonPropertyName("supports_rewards")] bool SupportsRewards,
    [property: JsonPropertyName("supports_hot_reload")] bool SupportsHotReload);

public sealed record QuestSupportedLimits(
    [property: JsonPropertyName("max_identifier_length")] int MaxIdentifierLength,
    [property: JsonPropertyName("max_display_name_length")] int MaxDisplayNameLength,
    [property: JsonPropertyName("max_steps")] int MaxSteps,
    [property: JsonPropertyName("max_transitions")] int MaxTransitions);

public sealed record QuestAuthoringDefaults(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("step_id")] string StepId,
    [property: JsonPropertyName("transition_id")] string TransitionId);

public sealed record QuestStep(
    [property: JsonPropertyName("step_id")] string StepId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("step_order")] int StepOrder);

public sealed record QuestTransition(
    [property: JsonPropertyName("transition_id")] string TransitionId,
    [property: JsonPropertyName("source_status")] string SourceStatus,
    [property: JsonPropertyName("source_step_id")] string? SourceStepId,
    [property: JsonPropertyName("target_status")] string TargetStatus,
    [property: JsonPropertyName("target_step_id")] string? TargetStepId,
    [property: JsonPropertyName("transition_order")] int TransitionOrder);

public sealed record QuestDraft(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("steps")] IReadOnlyList<QuestStep> Steps,
    [property: JsonPropertyName("transitions")] IReadOnlyList<QuestTransition> Transitions,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record PreviewQuestRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("steps")] IReadOnlyList<QuestStep> Steps,
    [property: JsonPropertyName("transitions")] IReadOnlyList<QuestTransition> Transitions,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record QuestMutationRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("steps")] IReadOnlyList<QuestStep> Steps,
    [property: JsonPropertyName("transitions")] IReadOnlyList<QuestTransition> Transitions,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record QuestLifecycleRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record QuestDeleteRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record QuestPreviewResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<AuthoringChange> Changes,
    [property: JsonPropertyName("analysis")] QuestGraphAnalysis Analysis,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature);

public sealed record QuestMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("quest")] QuestDefinition Quest,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record QuestDeleteResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("deleted_id")] string DeletedId,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record QuestGraphAnalysis(
    [property: JsonPropertyName("reachable_step_ids")] IReadOnlyList<string> ReachableStepIds,
    [property: JsonPropertyName("unreachable_step_ids")] IReadOnlyList<string> UnreachableStepIds,
    [property: JsonPropertyName("unreachable_transition_ids")] IReadOnlyList<string> UnreachableTransitionIds,
    [property: JsonPropertyName("dead_end_step_ids")] IReadOnlyList<string> DeadEndStepIds,
    [property: JsonPropertyName("has_start_transition")] bool HasStartTransition,
    [property: JsonPropertyName("has_completion_path")] bool HasCompletionPath);

public sealed record QuestStateReferenceSummary(
    [property: JsonPropertyName("quest_id")] string QuestId,
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("active_count")] int ActiveCount,
    [property: JsonPropertyName("completed_count")] int CompletedCount,
    [property: JsonPropertyName("active_step_ids")] IReadOnlyList<string> ActiveStepIds)
{
    public bool HasReferences => TotalCount > 0;
}
