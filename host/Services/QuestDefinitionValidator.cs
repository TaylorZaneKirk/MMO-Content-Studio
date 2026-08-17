using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class QuestDefinitionValidator
{
    private static readonly HashSet<string> DraftBlockingValidationCodes = new(StringComparer.Ordinal)
    {
        "quest_invalid_definition",
        "quest_invalid_graph",
        "quest_state_reference_blocked"
    };

    public QuestValidationOutcome Validate(
        string questId,
        QuestDraft draft,
        QuestDefinitionRecord? existing,
        bool forPublication)
    {
        var messages = new List<ApiError>();
        ValidateIdentity(questId, draft, existing, messages);
        ValidateShape(draft, messages);
        var analysis = Analyze(draft, messages);

        if (forPublication)
        {
            AddPublicationDiagnostics(analysis, messages);
        }

        if (forPublication && messages.Any(message => message.Severity == ValidationSeverity.Error))
        {
            messages.Add(new ApiError(
                "quest_publish_blocked",
                "Quest publication is blocked until graph validation errors are resolved.",
                ValidationSeverity.Error,
                "publication_state"));
        }

        if (existing is not null && existing.PublicationState == "Published" && !forPublication)
        {
            messages.Add(new ApiError(
                "save_will_unpublish_quest",
                "Saving this published quest changes its Content Studio lifecycle state to Draft; publish again before exporting it to the runtime catalog.",
                ValidationSeverity.Warning,
                "publication_state"));
        }

        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        var hasDraftBlockingErrors = messages.Any(IsDraftBlocking);
        return new QuestValidationOutcome(!hasDraftBlockingErrors, !hasErrors, messages, analysis);
    }

    public static bool IsDraftBlocking(ApiError message) =>
        message.Severity == ValidationSeverity.Error
        && DraftBlockingValidationCodes.Contains(message.Code);

    private static void ValidateIdentity(
        string questId,
        QuestDraft draft,
        QuestDefinitionRecord? existing,
        ICollection<ApiError> messages)
    {
        if (!QuestDomainRules.IsStableId(questId))
        {
            messages.Add(new ApiError(
                "quest_invalid_definition",
                "Quest IDs must be lowercase snake-case stable identifiers starting with a letter.",
                ValidationSeverity.Error,
                "quest_id"));
        }

        if (existing is not null && existing.QuestId != questId)
        {
            messages.Add(new ApiError(
                "quest_invalid_definition",
                "Quest identity is immutable after creation.",
                ValidationSeverity.Error,
                "quest_id"));
        }

        if (draft.DisplayName.Length is < 1 or > QuestAuthoringRegistry.MaxDisplayNameLength
            || draft.DisplayName.Any(char.IsControl))
        {
            messages.Add(new ApiError(
                "quest_invalid_definition",
                "Display name must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "display_name"));
        }

        if (draft.SchemaVersion != QuestAuthoringRegistry.CurrentSchemaVersion)
        {
            messages.Add(new ApiError(
                "quest_invalid_definition",
                $"Quest schema version must be {QuestAuthoringRegistry.CurrentSchemaVersion}.",
                ValidationSeverity.Error,
                "schema_version"));
        }
    }

    private static void ValidateShape(QuestDraft draft, ICollection<ApiError> messages)
    {
        AddDuplicateMessages(draft.Steps.Select(step => step.StepId), "Step IDs must be unique within a quest definition.", "steps", messages);
        AddDuplicateMessages(draft.Transitions.Select(transition => transition.TransitionId), "Transition IDs must be unique within a quest definition.", "transitions", messages);

        if (draft.Steps.Count is <= 0 or > QuestAuthoringRegistry.MaxSteps)
        {
            messages.Add(new ApiError(
                "quest_invalid_graph",
                $"A quest definition must contain 1-{QuestAuthoringRegistry.MaxSteps} steps.",
                ValidationSeverity.Error,
                "steps"));
        }

        if (draft.Transitions.Count is <= 0 or > QuestAuthoringRegistry.MaxTransitions)
        {
            messages.Add(new ApiError(
                "quest_invalid_graph",
                $"A quest definition must contain 1-{QuestAuthoringRegistry.MaxTransitions} transitions.",
                ValidationSeverity.Error,
                "transitions"));
        }

        foreach (var step in draft.Steps)
        {
            if (!QuestDomainRules.IsStableId(step.StepId))
            {
                messages.Add(InvalidId("step_id", step.StepId));
            }
            if (string.IsNullOrWhiteSpace(step.DisplayName))
            {
                messages.Add(new ApiError(
                    "quest_invalid_definition",
                    $"Step '{step.StepId}' display name is required.",
                    ValidationSeverity.Error,
                    "steps.display_name"));
            }
            if (step.StepOrder is < 0 or > QuestAuthoringRegistry.MaxOrderValue)
            {
                messages.Add(InvalidOrder("step_order"));
            }
        }

        foreach (var transition in draft.Transitions)
        {
            if (!QuestDomainRules.IsStableId(transition.TransitionId))
            {
                messages.Add(InvalidId("transition_id", transition.TransitionId));
            }
            if (transition.TransitionOrder is < 0 or > QuestAuthoringRegistry.MaxOrderValue)
            {
                messages.Add(InvalidOrder("transition_order"));
            }

            ValidateState("source", transition.SourceStatus, transition.SourceStepId, allowNotStarted: true, allowActive: true, allowCompleted: false, messages);
            ValidateState("target", transition.TargetStatus, transition.TargetStepId, allowNotStarted: false, allowActive: true, allowCompleted: true, messages);
        }
    }

    private static QuestGraphAnalysis Analyze(QuestDraft draft, ICollection<ApiError> messages)
    {
        var stepIds = draft.Steps.Select(step => step.StepId).ToHashSet(StringComparer.Ordinal);
        foreach (var transition in draft.Transitions)
        {
            if (transition.SourceStatus == "active" && !string.IsNullOrWhiteSpace(transition.SourceStepId) && !stepIds.Contains(transition.SourceStepId))
            {
                messages.Add(MissingStep("source_step_id", transition.SourceStepId));
            }
            if (transition.TargetStatus == "active" && !string.IsNullOrWhiteSpace(transition.TargetStepId) && !stepIds.Contains(transition.TargetStepId))
            {
                messages.Add(MissingStep("target_step_id", transition.TargetStepId));
            }
        }

        var reachableStates = new HashSet<(string Status, string? StepId)>();
        var reachableTransitions = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string Status, string? StepId)>();
        pending.Enqueue(("not_started", null));
        reachableStates.Add(("not_started", null));

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var transition in draft.Transitions.Where(transition =>
                transition.SourceStatus == current.Status &&
                string.Equals(transition.SourceStepId, current.StepId, StringComparison.Ordinal)))
            {
                reachableTransitions.Add(transition.TransitionId);
                var target = (transition.TargetStatus, transition.TargetStepId);
                if (reachableStates.Add(target) && target.TargetStatus != "completed")
                {
                    pending.Enqueue(target);
                }
            }
        }

        var reachableStepIds = reachableStates
            .Where(state => state.Status == "active" && state.StepId is not null)
            .Select(state => state.StepId!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var deadEndStepIds = reachableStepIds
            .Where(stepId => !CanReachCompleted(("active", stepId), draft.Transitions))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unreachableStepIds = stepIds
            .Except(reachableStepIds, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unreachableTransitionIds = draft.Transitions
            .Select(transition => transition.TransitionId)
            .Except(reachableTransitions, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new QuestGraphAnalysis(
            reachableStepIds,
            unreachableStepIds,
            unreachableTransitionIds,
            deadEndStepIds,
            draft.Transitions.Any(transition => transition.SourceStatus == "not_started"),
            reachableStates.Any(state => state.Status == "completed"));
    }

    private static void AddPublicationDiagnostics(QuestGraphAnalysis analysis, ICollection<ApiError> messages)
    {
        if (!analysis.HasStartTransition)
        {
            messages.Add(GraphError("Quest definitions must define at least one not_started start transition.", "transitions"));
        }
        if (!analysis.HasCompletionPath)
        {
            messages.Add(GraphError("Quest definitions must include at least one reachable completion path.", "transitions"));
        }
        foreach (var stepId in analysis.UnreachableStepIds)
        {
            messages.Add(GraphError($"Step '{stepId}' is not reachable from a valid start path.", "steps"));
        }
        foreach (var transitionId in analysis.UnreachableTransitionIds)
        {
            messages.Add(GraphError($"Transition '{transitionId}' is not reachable from a valid start path.", "transitions"));
        }
        foreach (var stepId in analysis.DeadEndStepIds)
        {
            messages.Add(GraphError($"Reachable step '{stepId}' must have at least one path to completed.", "steps"));
        }
    }

    private static bool CanReachCompleted(
        (string Status, string? StepId) start,
        IReadOnlyList<QuestTransition> transitions)
    {
        var visited = new HashSet<(string Status, string? StepId)>();
        var pending = new Queue<(string Status, string? StepId)>();
        pending.Enqueue(start);
        visited.Add(start);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var transition in transitions.Where(transition =>
                transition.SourceStatus == current.Status &&
                string.Equals(transition.SourceStepId, current.StepId, StringComparison.Ordinal)))
            {
                if (transition.TargetStatus == "completed")
                {
                    return true;
                }

                var target = (transition.TargetStatus, transition.TargetStepId);
                if (visited.Add(target))
                {
                    pending.Enqueue(target);
                }
            }
        }

        return false;
    }

    private static void ValidateState(
        string role,
        string status,
        string? stepId,
        bool allowNotStarted,
        bool allowActive,
        bool allowCompleted,
        ICollection<ApiError> messages)
    {
        if (status == "not_started" && allowNotStarted || status == "completed" && allowCompleted)
        {
            if (!string.IsNullOrWhiteSpace(stepId))
            {
                messages.Add(GraphError($"{role} status '{status}' must not retain an active step.", $"{role}_step_id"));
            }
            return;
        }

        if (status == "active" && allowActive)
        {
            if (string.IsNullOrWhiteSpace(stepId))
            {
                messages.Add(GraphError($"{role} active state is missing step_id.", $"{role}_step_id"));
            }
            return;
        }

        messages.Add(GraphError($"{role} status '{status}' is unsupported.", $"{role}_status"));
    }

    private static void AddDuplicateMessages(IEnumerable<string> ids, string message, string field, ICollection<ApiError> messages)
    {
        if (ids.GroupBy(id => id, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            messages.Add(GraphError(message, field));
        }
    }

    private static ApiError InvalidId(string field, string value) =>
        new("quest_invalid_definition", $"{field} '{value}' must be a lowercase stable id starting with a letter.", ValidationSeverity.Error, field);

    private static ApiError InvalidOrder(string field) =>
        new("quest_invalid_graph", $"{field} must be between 0 and {QuestAuthoringRegistry.MaxOrderValue}.", ValidationSeverity.Error, field);

    private static ApiError MissingStep(string field, string stepId) =>
        new("quest_invalid_graph", $"{field} references missing step '{stepId}'.", ValidationSeverity.Error, field);

    private static ApiError GraphError(string message, string field) =>
        new("quest_invalid_graph", message, ValidationSeverity.Error, field);
}

public sealed record QuestValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    QuestGraphAnalysis Analysis);
