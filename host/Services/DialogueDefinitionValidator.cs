using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class DialogueDefinitionValidator
{
    private static readonly HashSet<string> DraftBlockingValidationCodes = new(StringComparer.Ordinal)
    {
        "dialogue_invalid_definition",
        "dialogue_invalid_graph",
        "dialogue_unsupported_node_type",
        "dialogue_unsupported_condition",
        "dialogue_unsupported_effect"
    };

    private readonly DialogueGraphAnalyzer _analyzer;

    public DialogueDefinitionValidator(DialogueGraphAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public DialogueValidationOutcome Validate(
        string dialogueDefinitionId,
        DialogueDraft draft,
        DialogueDefinitionRecord? existing,
        bool forPublication)
    {
        var messages = new List<ApiError>();
        ValidateIdentity(dialogueDefinitionId, draft, existing, messages);
        ValidateShape(draft, messages);
        var analysis = _analyzer.Analyze(draft);
        AddGraphDiagnostics(draft, analysis, messages, forPublication);
        ValidateNodeSemantics(draft, messages, forPublication);

        if (forPublication && messages.Any(message => message.Severity == ValidationSeverity.Error))
        {
            messages.Add(new ApiError(
                "dialogue_publish_blocked",
                "Dialogue publication is blocked until graph validation errors are resolved.",
                ValidationSeverity.Error,
                "publication_state"));
        }

        if (existing is not null && existing.PublicationState == "Published" && !forPublication)
        {
            messages.Add(new ApiError(
                "save_will_unpublish_dialogue",
                "Saving this published dialogue changes its Content Studio lifecycle state to Draft; runtime export remains D4 work.",
                ValidationSeverity.Warning,
                "publication_state"));
        }

        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        var hasDraftBlockingErrors = messages.Any(IsDraftBlocking);
        return new DialogueValidationOutcome(!hasDraftBlockingErrors, !hasErrors, messages, analysis);
    }

    public static bool IsDraftBlocking(ApiError message) =>
        message.Severity == ValidationSeverity.Error
        && DraftBlockingValidationCodes.Contains(message.Code);

    public static void ValidateIdentity(
        string dialogueDefinitionId,
        DialogueDraft draft,
        DialogueDefinitionRecord? existing,
        ICollection<ApiError> messages)
    {
        if (!DialogueDomainRules.IsStableId(dialogueDefinitionId))
        {
            messages.Add(new ApiError(
                "dialogue_invalid_definition",
                "Dialogue definition IDs must be lowercase snake-case stable identifiers starting with a letter.",
                ValidationSeverity.Error,
                "dialogue_definition_id"));
        }
        if (existing is not null
            && !string.Equals(existing.DialogueDefinitionId, dialogueDefinitionId, StringComparison.Ordinal))
        {
            messages.Add(new ApiError(
                "dialogue_invalid_definition",
                "Dialogue definition identity is immutable after creation.",
                ValidationSeverity.Error,
                "dialogue_definition_id"));
        }
        if (draft.DisplayName.Length is < 1 or > DialogueAuthoringRegistry.MaxDisplayNameLength
            || draft.DisplayName.Any(char.IsControl))
        {
            messages.Add(new ApiError(
                "dialogue_invalid_definition",
                "Display name must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "display_name"));
        }
        if (draft.SchemaVersion != DialogueAuthoringRegistry.CurrentSchemaVersion)
        {
            messages.Add(new ApiError(
                "dialogue_invalid_definition",
                $"Dialogue schema version must be {DialogueAuthoringRegistry.CurrentSchemaVersion}.",
                ValidationSeverity.Error,
                "schema_version"));
        }
    }

    public static void ValidateShape(DialogueDraft draft, ICollection<ApiError> messages)
    {
        AddDuplicateMessages(
            draft.EntryPoints.Select(entry => entry.EntryId),
            "dialogue_invalid_graph",
            "Entry point IDs must be unique within a dialogue definition.",
            "entry_points",
            messages);
        AddDuplicateMessages(
            draft.Nodes.Select(node => node.NodeId),
            "dialogue_invalid_graph",
            "Node IDs must be unique within a dialogue definition.",
            "nodes",
            messages);

        if (draft.Nodes.Count > DialogueAuthoringRegistry.MaxNodes)
        {
            messages.Add(new ApiError(
                "dialogue_invalid_graph",
                $"A dialogue definition can contain at most {DialogueAuthoringRegistry.MaxNodes} nodes.",
                ValidationSeverity.Error,
                "nodes"));
        }

        foreach (var entry in draft.EntryPoints)
        {
            if (!DialogueDomainRules.IsStableId(entry.EntryId))
            {
                messages.Add(InvalidId("entry_id", entry.EntryId));
            }
            if (!DialogueDomainRules.IsStableId(entry.NodeId))
            {
                messages.Add(InvalidId("entry_points.node_id", entry.NodeId));
            }
            if (!DialogueDomainRules.IsOrderValueSupported(entry.EntryOrder))
            {
                messages.Add(InvalidOrder("entry_order"));
            }
            if (entry.Conditions.Count > 0)
            {
                messages.Add(UnsupportedCondition("entry_points.conditions"));
            }
        }

        foreach (var node in draft.Nodes)
        {
            if (!DialogueDomainRules.IsStableId(node.NodeId))
            {
                messages.Add(InvalidId("node_id", node.NodeId));
            }
            if (!DialogueDomainRules.IsSupportedNodeType(node.NodeType))
            {
                messages.Add(new ApiError(
                    "dialogue_unsupported_node_type",
                    $"Node '{node.NodeId}' uses unsupported type '{node.NodeType}'.",
                    ValidationSeverity.Error,
                    "node_type"));
            }
            if (!DialogueDomainRules.IsFinite(node.CanvasX) || !DialogueDomainRules.IsFinite(node.CanvasY))
            {
                messages.Add(new ApiError(
                    "dialogue_invalid_graph",
                    $"Node '{node.NodeId}' canvas coordinates must be finite.",
                    ValidationSeverity.Error,
                    "canvas_x"));
            }
            if (node.Text is { Length: > DialogueAuthoringRegistry.MaxTextLength })
            {
                messages.Add(new ApiError(
                    "dialogue_invalid_definition",
                    $"Node '{node.NodeId}' text exceeds the supported length.",
                    ValidationSeverity.Error,
                    "text"));
            }
            if (node.EditorNotes is { Length: > DialogueAuthoringRegistry.MaxNotesLength }
                || node.EditorNotes?.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t') == true)
            {
                messages.Add(new ApiError(
                    "dialogue_invalid_definition",
                    $"Node '{node.NodeId}' editor notes must be printable authoring metadata.",
                    ValidationSeverity.Error,
                    "editor_notes"));
            }
            if (node.Choices.Count > DialogueAuthoringRegistry.MaxChoicesPerNode)
            {
                messages.Add(new ApiError(
                    "dialogue_invalid_graph",
                    $"Node '{node.NodeId}' has too many choices.",
                    ValidationSeverity.Error,
                    "choices"));
            }

            AddDuplicateMessages(
                node.Choices.Select(choice => choice.ChoiceId),
                "dialogue_invalid_graph",
                $"Choice IDs must be unique within node '{node.NodeId}'.",
                "choices",
                messages);
            foreach (var choice in node.Choices)
            {
                if (!DialogueDomainRules.IsStableId(choice.ChoiceId))
                {
                    messages.Add(InvalidId("choice_id", choice.ChoiceId));
                }
                if (!DialogueDomainRules.IsStableId(choice.TargetNodeId))
                {
                    messages.Add(InvalidId("target_node_id", choice.TargetNodeId));
                }
                if (!DialogueDomainRules.IsOrderValueSupported(choice.ChoiceOrder))
                {
                    messages.Add(InvalidOrder("choice_order"));
                }
                if (string.IsNullOrWhiteSpace(choice.Text))
                {
                    messages.Add(new ApiError(
                        "dialogue_invalid_graph",
                        $"Choice '{choice.ChoiceId}' text is required.",
                        ValidationSeverity.Error,
                        "choices.text"));
                }
                if (choice.Conditions.Count > 0)
                {
                    messages.Add(UnsupportedCondition("choices.conditions"));
                }
            }
        }

        if (draft.MetadataDescription is { Length: > DialogueAuthoringRegistry.MaxNotesLength }
            || draft.Notes is { Length: > DialogueAuthoringRegistry.MaxNotesLength })
        {
            messages.Add(new ApiError(
                "dialogue_invalid_definition",
                "Dialogue authoring metadata fields are too long.",
                ValidationSeverity.Error,
                "notes"));
        }
    }

    public static void AddGraphDiagnostics(
        DialogueDraft draft,
        DialogueGraphAnalysis analysis,
        ICollection<ApiError> messages,
        bool forPublication)
    {
        if (draft.EntryPoints.Count == 0)
        {
            messages.Add(new ApiError(
                forPublication ? "dialogue_entry_target_missing" : "dialogue_invalid_graph",
                "Dialogue definitions need at least one entry point before publication.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "entry_points"));
        }
        foreach (var target in analysis.DanglingTargetNodeIds)
        {
            messages.Add(new ApiError(
                "dialogue_transition_target_missing",
                $"Transition target node '{target}' does not exist.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "node_id"));
        }
        foreach (var nodeId in analysis.UnreachableNodeIds)
        {
            messages.Add(new ApiError(
                "dialogue_invalid_graph",
                $"Node '{nodeId}' is not reachable from an entry point.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "nodes"));
        }
        if (analysis.TerminalNodeIds.Count == 0)
        {
            messages.Add(new ApiError(
                "dialogue_invalid_graph",
                "At least one reachable end node is required before publication.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "nodes"));
        }
        foreach (var nodeId in analysis.NodesWithoutTerminalPath)
        {
            messages.Add(new ApiError(
                "dialogue_invalid_graph",
                $"Node '{nodeId}' does not have a path to an end node.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "nodes"));
        }
        if (draft.EntryPoints.Count > 1)
        {
            messages.Add(new ApiError(
                "dialogue_invalid_graph",
                "Multiple entry points are allowed for forward compatibility; with no condition vocabulary, lower-priority entries may be unreachable at runtime.",
                ValidationSeverity.Warning,
                "entry_points"));
        }
    }

    public static void ValidateNodeSemantics(
        DialogueDraft draft,
        ICollection<ApiError> messages,
        bool forPublication)
    {
        foreach (var node in draft.Nodes)
        {
            switch (node.NodeType)
            {
                case DialogueAuthoringRegistry.SpeakerTextNodeType:
                    if (forPublication && string.IsNullOrWhiteSpace(node.Text))
                    {
                        messages.Add(new ApiError(
                            "dialogue_invalid_graph",
                            $"Speaker text node '{node.NodeId}' needs text before publication.",
                            ValidationSeverity.Error,
                            "text"));
                    }
                    if (forPublication && string.IsNullOrWhiteSpace(node.Speaker))
                    {
                        messages.Add(new ApiError(
                            "dialogue_invalid_graph",
                            $"Speaker text node '{node.NodeId}' needs a speaker before publication.",
                            ValidationSeverity.Error,
                            "speaker"));
                    }
                    if (node.Choices.Count > 0)
                    {
                        messages.Add(new ApiError(
                            "dialogue_invalid_graph",
                            $"Speaker text node '{node.NodeId}' cannot own choices.",
                            ValidationSeverity.Error,
                            "choices"));
                    }
                    break;
                case DialogueAuthoringRegistry.PlayerChoiceNodeType:
                    if (node.NextNodeId is not null)
                    {
                        messages.Add(new ApiError(
                            "dialogue_invalid_graph",
                            $"Player choice node '{node.NodeId}' must transition through choices, not next_node_id.",
                            ValidationSeverity.Error,
                            "next_node_id"));
                    }
                    if (forPublication && node.Choices.Count == 0)
                    {
                        messages.Add(new ApiError(
                            "dialogue_invalid_graph",
                            $"Player choice node '{node.NodeId}' needs at least one choice before publication.",
                            ValidationSeverity.Error,
                            "choices"));
                    }
                    break;
                case DialogueAuthoringRegistry.EndNodeType:
                    if (node.NextNodeId is not null || node.Choices.Count > 0)
                    {
                        messages.Add(new ApiError(
                            "dialogue_invalid_graph",
                            $"End node '{node.NodeId}' cannot have outgoing transitions.",
                            ValidationSeverity.Error,
                            "next_node_id"));
                    }
                    break;
            }
        }
    }

    private static void AddDuplicateMessages(
        IEnumerable<string> ids,
        string code,
        string message,
        string field,
        ICollection<ApiError> messages)
    {
        if (ids.GroupBy(id => id, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            messages.Add(new ApiError(code, message, ValidationSeverity.Error, field));
        }
    }

    private static ApiError InvalidId(string field, string value) => new(
        "dialogue_invalid_graph",
        $"Dialogue stable identifier '{value}' in {field} is malformed.",
        ValidationSeverity.Error,
        field);

    private static ApiError InvalidOrder(string field) => new(
        "dialogue_invalid_graph",
        $"{field} must be between 0 and {DialogueAuthoringRegistry.MaxOrderValue}.",
        ValidationSeverity.Error,
        field);

    private static ApiError UnsupportedCondition(string field) => new(
        "dialogue_unsupported_condition",
        "No dialogue condition types are authorable in D2; condition arrays must be empty.",
        ValidationSeverity.Error,
        field);
}

public sealed record DialogueValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    DialogueGraphAnalysis Analysis);
