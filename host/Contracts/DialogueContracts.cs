using System.Text.Json.Serialization;

namespace MMO.ContentStudio.AuthoringHost.Contracts;

public sealed record DialogueDefinition(
    [property: JsonPropertyName("dialogue_definition_id")] string DialogueDefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("entry_points")] IReadOnlyList<DialogueEntryPoint> EntryPoints,
    [property: JsonPropertyName("nodes")] IReadOnlyList<DialogueNode> Nodes,
    [property: JsonPropertyName("metadata_description")] string? MetadataDescription,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("created_at_utc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record DialogueDefinitionSummary(
    [property: JsonPropertyName("dialogue_definition_id")] string DialogueDefinitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("publication_state")] string PublicationState,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("entry_point_count")] int EntryPointCount,
    [property: JsonPropertyName("node_count")] int NodeCount,
    [property: JsonPropertyName("choice_count")] int ChoiceCount,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset UpdatedAtUtc);

public sealed record DialogueCatalogResponse(
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<DialogueDefinitionSummary> Items);

public sealed record DialogueOptionsResponse(
    [property: JsonPropertyName("publication_states")] IReadOnlyList<AuthoringOption> PublicationStates,
    [property: JsonPropertyName("node_types")] IReadOnlyList<AuthoringOption> NodeTypes,
    [property: JsonPropertyName("condition_types")] IReadOnlyList<AuthoringOption> ConditionTypes,
    [property: JsonPropertyName("effect_types")] IReadOnlyList<AuthoringOption> EffectTypes,
    [property: JsonPropertyName("supported_limits")] DialogueSupportedLimits SupportedLimits,
    [property: JsonPropertyName("capabilities")] DialogueOperationCapabilities Capabilities,
    [property: JsonPropertyName("defaults")] DialogueAuthoringDefaults Defaults,
    [property: JsonPropertyName("quest_references")] IReadOnlyList<DialogueQuestConditionOption>? QuestReferences = null,
    [property: JsonPropertyName("item_references")] IReadOnlyList<AuthoringOption>? ItemReferences = null,
    [property: JsonPropertyName("skill_references")] IReadOnlyList<AuthoringOption>? SkillReferences = null);

public sealed record DialogueQuestConditionOption(
    [property: JsonPropertyName("quest_id")] string QuestId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("steps")] IReadOnlyList<AuthoringOption> Steps,
    [property: JsonPropertyName("transitions")] IReadOnlyList<DialogueQuestTransitionOption>? Transitions = null);

public sealed record DialogueQuestTransitionOption(
    [property: JsonPropertyName("transition_id")] string TransitionId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("source_status")] string SourceStatus,
    [property: JsonPropertyName("source_step_id")] string? SourceStepId,
    [property: JsonPropertyName("target_status")] string TargetStatus,
    [property: JsonPropertyName("target_step_id")] string? TargetStepId);

public sealed record DialogueOperationCapabilities(
    [property: JsonPropertyName("supports_runtime_dialogue_catalog")] bool SupportsRuntimeDialogueCatalog,
    [property: JsonPropertyName("supports_conditions")] bool SupportsConditions,
    [property: JsonPropertyName("supports_effects")] bool SupportsEffects,
    [property: JsonPropertyName("supports_quest_conditions")] bool SupportsQuestConditions,
    [property: JsonPropertyName("supports_quest_effects")] bool SupportsQuestEffects,
    [property: JsonPropertyName("supports_localization")] bool SupportsLocalization,
    [property: JsonPropertyName("supports_portraits")] bool SupportsPortraits,
    [property: JsonPropertyName("supports_hot_reload")] bool SupportsHotReload);

public sealed record DialogueCondition(
    [property: JsonPropertyName("condition_type")] string ConditionType,
    [property: JsonPropertyName("quest_id")] string? QuestId,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("step_id")] string? StepId,
    [property: JsonPropertyName("item_id")] string? ItemId,
    [property: JsonPropertyName("quantity")] int? Quantity);

public sealed record DialogueEffect(
    [property: JsonPropertyName("effect_id")] string EffectId,
    [property: JsonPropertyName("effect_order")] int EffectOrder,
    [property: JsonPropertyName("effect_type")] string EffectType,
    [property: JsonPropertyName("quest_id")] string? QuestId,
    [property: JsonPropertyName("transition_id")] string? TransitionId,
    [property: JsonPropertyName("item_id")] string? ItemId,
    [property: JsonPropertyName("quantity")] int? Quantity,
    [property: JsonPropertyName("skill_id")] string? SkillId,
    [property: JsonPropertyName("xp_amount")] long? XpAmount);

public sealed record DialogueSupportedLimits(
    [property: JsonPropertyName("max_identifier_length")] int MaxIdentifierLength,
    [property: JsonPropertyName("max_display_name_length")] int MaxDisplayNameLength,
    [property: JsonPropertyName("max_text_length")] int MaxTextLength,
    [property: JsonPropertyName("max_notes_length")] int MaxNotesLength,
    [property: JsonPropertyName("max_nodes")] int MaxNodes,
    [property: JsonPropertyName("max_choices_per_node")] int MaxChoicesPerNode,
    [property: JsonPropertyName("max_playthrough_steps")] int MaxPlaythroughSteps);

public sealed record DialogueAuthoringDefaults(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("entry_id")] string EntryId,
    [property: JsonPropertyName("start_node_id")] string StartNodeId,
    [property: JsonPropertyName("node_type")] string NodeType,
    [property: JsonPropertyName("dismissible")] bool Dismissible,
    [property: JsonPropertyName("canvas_x")] double CanvasX,
    [property: JsonPropertyName("canvas_y")] double CanvasY);

public sealed record DialogueEntryPoint(
    [property: JsonPropertyName("entry_id")] string EntryId,
    [property: JsonPropertyName("node_id")] string NodeId,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("entry_order")] int EntryOrder,
    [property: JsonPropertyName("conditions")] IReadOnlyList<DialogueCondition> Conditions);

public sealed record DialogueNode(
    [property: JsonPropertyName("node_id")] string NodeId,
    [property: JsonPropertyName("node_type")] string NodeType,
    [property: JsonPropertyName("speaker")] string? Speaker,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("next_node_id")] string? NextNodeId,
    [property: JsonPropertyName("dismissible")] bool Dismissible,
    [property: JsonPropertyName("canvas_x")] double CanvasX,
    [property: JsonPropertyName("canvas_y")] double CanvasY,
    [property: JsonPropertyName("editor_notes")] string? EditorNotes,
    [property: JsonPropertyName("choices")] IReadOnlyList<DialogueChoice> Choices);

public sealed record DialogueChoice(
    [property: JsonPropertyName("choice_id")] string ChoiceId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("target_node_id")] string TargetNodeId,
    [property: JsonPropertyName("choice_order")] int ChoiceOrder,
    [property: JsonPropertyName("conditions")] IReadOnlyList<DialogueCondition> Conditions,
    [property: JsonPropertyName("effects")] IReadOnlyList<DialogueEffect>? Effects = null);

public sealed record DialogueDraft(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("entry_points")] IReadOnlyList<DialogueEntryPoint> EntryPoints,
    [property: JsonPropertyName("nodes")] IReadOnlyList<DialogueNode> Nodes,
    [property: JsonPropertyName("metadata_description")] string? MetadataDescription,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record PreviewDialogueRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("entry_points")] IReadOnlyList<DialogueEntryPoint> EntryPoints,
    [property: JsonPropertyName("nodes")] IReadOnlyList<DialogueNode> Nodes,
    [property: JsonPropertyName("metadata_description")] string? MetadataDescription,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("target_operation")] string TargetOperation);

public sealed record PreviewDialoguePlaythroughRequest(
    [property: JsonPropertyName("draft")] DialogueDraft? Draft,
    [property: JsonPropertyName("entry_id")] string? EntryId,
    [property: JsonPropertyName("current_node_id")] string? CurrentNodeId,
    [property: JsonPropertyName("selected_choice_id")] string? SelectedChoiceId,
    [property: JsonPropertyName("acknowledge_end")] bool AcknowledgeEnd,
    [property: JsonPropertyName("restart")] bool Restart,
    [property: JsonPropertyName("visited_node_ids")] IReadOnlyList<string> VisitedNodeIds,
    [property: JsonPropertyName("maximum_step_count")] int? MaximumStepCount);

public sealed record DialogueMutationRequest(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("entry_points")] IReadOnlyList<DialogueEntryPoint> EntryPoints,
    [property: JsonPropertyName("nodes")] IReadOnlyList<DialogueNode> Nodes,
    [property: JsonPropertyName("metadata_description")] string? MetadataDescription,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record DialogueLifecycleRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record DialogueDeleteRequest(
    [property: JsonPropertyName("expected_updated_at_utc")] DateTimeOffset? ExpectedUpdatedAtUtc,
    [property: JsonPropertyName("preview_signature")] string? PreviewSignature);

public sealed record DialoguePreviewResponse(
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("valid_for_draft")] bool ValidForDraft,
    [property: JsonPropertyName("valid_for_publication")] bool ValidForPublication,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<AuthoringChange> Changes,
    [property: JsonPropertyName("analysis")] DialogueGraphAnalysis Analysis,
    [property: JsonPropertyName("reference_summary")] DialogueReferenceSummary ReferenceSummary,
    [property: JsonPropertyName("preview_signature")] string PreviewSignature);

public sealed record DialoguePlaythroughResponse(
    [property: JsonPropertyName("current_node")] DialogueNode? CurrentNode,
    [property: JsonPropertyName("speaker")] string? Speaker,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("visible_choices")] IReadOnlyList<DialogueChoice> VisibleChoices,
    [property: JsonPropertyName("can_continue")] bool CanContinue,
    [property: JsonPropertyName("is_end")] bool IsEnd,
    [property: JsonPropertyName("next_node_id")] string? NextNodeId,
    [property: JsonPropertyName("would_apply_effects")] IReadOnlyList<DialogueEffect> WouldApplyEffects,
    [property: JsonPropertyName("visited_node_ids")] IReadOnlyList<string> VisitedNodeIds,
    [property: JsonPropertyName("warnings")] IReadOnlyList<ApiError> Warnings);

public sealed record DialogueMutationResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("dialogue")] DialogueDefinition Dialogue,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record DialogueDeleteResponse(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("deleted_id")] string DeletedId,
    [property: JsonPropertyName("messages")] IReadOnlyList<ApiError> Messages);

public sealed record DialogueReferenceSummary(
    [property: JsonPropertyName("known_reference_count")] int KnownReferenceCount,
    [property: JsonPropertyName("reference_sources")] IReadOnlyList<string> ReferenceSources,
    [property: JsonPropertyName("reference_check_complete")] bool ReferenceCheckComplete);

public sealed record DialogueValidationContext(
    [property: JsonPropertyName("for_publication")] bool ForPublication,
    [property: JsonPropertyName("reference_summary")] DialogueReferenceSummary ReferenceSummary);

public sealed record DialogueGraphAnalysis(
    [property: JsonPropertyName("reachable_node_ids")] IReadOnlyList<string> ReachableNodeIds,
    [property: JsonPropertyName("unreachable_node_ids")] IReadOnlyList<string> UnreachableNodeIds,
    [property: JsonPropertyName("dangling_target_node_ids")] IReadOnlyList<string> DanglingTargetNodeIds,
    [property: JsonPropertyName("terminal_node_ids")] IReadOnlyList<string> TerminalNodeIds,
    [property: JsonPropertyName("cycle_node_ids")] IReadOnlyList<string> CycleNodeIds,
    [property: JsonPropertyName("nodes_without_terminal_path")] IReadOnlyList<string> NodesWithoutTerminalPath,
    [property: JsonPropertyName("duplicate_order_fields")] IReadOnlyList<string> DuplicateOrderFields);
