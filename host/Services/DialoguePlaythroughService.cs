using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class DialoguePlaythroughService
{
    public DialoguePlaythroughResponse Preview(DialogueDraft draft, PreviewDialoguePlaythroughRequest request)
    {
        var warnings = new List<ApiError>();
        var normalized = DialogueDomainRules.NormalizeDraft(draft);
        var nodes = normalized.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var visited = request.Restart ? [] : request.VisitedNodeIds.Select(DialogueDomainRules.NormalizeStableId).ToList();
        var maxSteps = request.MaximumStepCount.GetValueOrDefault(DialogueAuthoringRegistry.MaxPlaythroughSteps);
        if (maxSteps is < 1 or > DialogueAuthoringRegistry.MaxPlaythroughSteps)
        {
            maxSteps = DialogueAuthoringRegistry.MaxPlaythroughSteps;
        }

        var currentNodeId = ResolveCurrentNodeId(normalized, request, warnings);
        if (currentNodeId is null || !nodes.TryGetValue(currentNodeId, out var current))
        {
            return new DialoguePlaythroughResponse(
                null,
                null,
                null,
                [],
                false,
                false,
                null,
                [],
                visited,
                warnings.Count == 0 ? [InvalidState("Dialogue could not resolve a current node.", "current_node_id")] : warnings);
        }

        var nextNodeId = default(string?);
        IReadOnlyList<DialogueEffect> wouldApplyEffects = [];
        if (request.AcknowledgeEnd && current.NodeType == DialogueAuthoringRegistry.EndNodeType)
        {
            visited.Add(current.NodeId);
            return new DialoguePlaythroughResponse(
                null,
                null,
                null,
                [],
                false,
                true,
                null,
                [],
                visited.Distinct(StringComparer.Ordinal).ToArray(),
                warnings);
        }

        if (current.NodeType == DialogueAuthoringRegistry.SpeakerTextNodeType && request.CurrentNodeId is not null)
        {
            nextNodeId = current.NextNodeId;
            if (nextNodeId is null)
            {
                warnings.Add(InvalidState($"Speaker text node '{current.NodeId}' has no next node.", "next_node_id"));
            }
            else if (!nodes.TryGetValue(nextNodeId, out current!))
            {
                warnings.Add(InvalidState($"Next node '{nextNodeId}' does not exist.", "next_node_id"));
                current = nodes[currentNodeId];
            }
        }
        else if (current.NodeType == DialogueAuthoringRegistry.PlayerChoiceNodeType
                 && !string.IsNullOrWhiteSpace(request.SelectedChoiceId))
        {
            var choiceId = DialogueDomainRules.NormalizeStableId(request.SelectedChoiceId);
            var choice = current.Choices.FirstOrDefault(candidate => candidate.ChoiceId == choiceId);
            if (choice is null)
            {
                warnings.Add(InvalidState($"Choice '{choiceId}' is not available from node '{current.NodeId}'.", "selected_choice_id"));
            }
            else
            {
                nextNodeId = choice.TargetNodeId;
                wouldApplyEffects = choice.Effects ?? [];
                if (!nodes.TryGetValue(nextNodeId, out current!))
                {
                    warnings.Add(InvalidState($"Choice target node '{nextNodeId}' does not exist.", "target_node_id"));
                    current = nodes[currentNodeId];
                }
            }
        }

        visited.Add(current.NodeId);
        if (visited.Count > maxSteps || visited.Count(id => id == current.NodeId) > 1)
        {
            warnings.Add(InvalidState("Playthrough loop protection detected a repeated node or maximum-step overflow.", "visited_node_ids"));
        }

        return new DialoguePlaythroughResponse(
            current,
            current.Speaker,
            current.Text,
            current.NodeType == DialogueAuthoringRegistry.PlayerChoiceNodeType ? current.Choices : [],
            current.NodeType is DialogueAuthoringRegistry.SpeakerTextNodeType or DialogueAuthoringRegistry.EndNodeType,
            current.NodeType == DialogueAuthoringRegistry.EndNodeType,
            nextNodeId,
            wouldApplyEffects,
            visited,
            warnings);
    }

    private static string? ResolveCurrentNodeId(
        DialogueDraft draft,
        PreviewDialoguePlaythroughRequest request,
        ICollection<ApiError> warnings)
    {
        if (request.Restart || string.IsNullOrWhiteSpace(request.CurrentNodeId))
        {
            var normalizedEntryId = DialogueDomainRules.NormalizeOptional(request.EntryId)?.ToLowerInvariant();
            var entry = normalizedEntryId is null
                ? draft.EntryPoints.FirstOrDefault()
                : draft.EntryPoints.FirstOrDefault(candidate => candidate.EntryId == normalizedEntryId);
            if (entry is null)
            {
                warnings.Add(InvalidState("The requested entry point does not exist.", "entry_id"));
                return null;
            }

            return entry.NodeId;
        }

        return DialogueDomainRules.NormalizeStableId(request.CurrentNodeId);
    }

    private static ApiError InvalidState(string message, string field) => new(
        "dialogue_playthrough_invalid_state",
        message,
        ValidationSeverity.Warning,
        field);
}
