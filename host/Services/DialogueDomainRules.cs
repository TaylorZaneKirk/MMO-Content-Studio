using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static partial class DialogueDomainRules
{
    public static string NormalizeStableId(string value) =>
        NormalizeRequired(value).ToLowerInvariant();

    public static string NormalizeRequired(string value) => value.Trim();

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    public static string NormalizeNodeType(string value) =>
        NormalizeStableId(value);

    public static string NormalizePublicationState(string value) =>
        NormalizeRequired(value) switch
        {
            "draft" or "Draft" => "Draft",
            "published" or "Published" => "Published",
            "disabled" or "Disabled" => "Disabled",
            var normalized => normalized
        };

    public static bool IsStableId(string value)
    {
        var trimmed = NormalizeRequired(value);
        var normalized = trimmed.ToLowerInvariant();
        return normalized.Length <= DialogueAuthoringRegistry.MaxIdentifierLength
            && string.Equals(trimmed, normalized, StringComparison.Ordinal)
            && StableIdentifierRegex().IsMatch(normalized);
    }

    public static bool IsSupportedNodeType(string value) =>
        NormalizeNodeType(value) is DialogueAuthoringRegistry.SpeakerTextNodeType
            or DialogueAuthoringRegistry.PlayerChoiceNodeType
            or DialogueAuthoringRegistry.EndNodeType;

    public static string NormalizeConditionType(string value) =>
        NormalizeStableId(value);

    public static bool IsSupportedPublicationState(string value) =>
        NormalizePublicationState(value) is "Draft" or "Published" or "Disabled";

    public static bool IsFinite(double value) => double.IsFinite(value);

    public static bool IsOrderValueSupported(int value) =>
        value is >= 0 and <= DialogueAuthoringRegistry.MaxOrderValue;

    public static DialogueDraft NormalizeDraft(DialogueDraft draft) =>
        NormalizeDraft(
            draft.DisplayName,
            draft.SchemaVersion,
            draft.EntryPoints,
            draft.Nodes,
            draft.MetadataDescription,
            draft.Notes,
            draft.ExpectedUpdatedAtUtc,
            draft.PreviewSignature);

    public static DialogueDraft NormalizeDraft(
        string displayName,
        int schemaVersion,
        IReadOnlyList<DialogueEntryPoint> entryPoints,
        IReadOnlyList<DialogueNode> nodes,
        string? metadataDescription,
        string? notes,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? previewSignature)
    {
        var normalizedNodes = nodes
            .Select(NormalizeNode)
            .OrderBy(node => node.CanvasY)
            .ThenBy(node => node.CanvasX)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();
        var orderedNodes = normalizedNodes
            .Select((node, index) => node with { Choices = NormalizeChoices(node.Choices) })
            .ToArray();

        return new DialogueDraft(
            NormalizeRequired(displayName),
            schemaVersion,
            entryPoints
                .Select(NormalizeEntryPoint)
                .OrderByDescending(entry => entry.Priority)
                .ThenBy(entry => entry.EntryOrder)
                .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
                .ToArray(),
            orderedNodes,
            NormalizeOptional(metadataDescription),
            NormalizeOptional(notes),
            expectedUpdatedAtUtc,
            previewSignature);
    }

    public static DialogueEntryPoint NormalizeEntryPoint(DialogueEntryPoint entryPoint) =>
        new(
            NormalizeStableId(entryPoint.EntryId),
            NormalizeStableId(entryPoint.NodeId),
            entryPoint.Priority,
            entryPoint.EntryOrder,
            NormalizeConditions(entryPoint.Conditions));

    public static DialogueNode NormalizeNode(DialogueNode node)
    {
        var nodeType = NormalizeNodeType(node.NodeType);
        var nextNodeId = nodeType == DialogueAuthoringRegistry.PlayerChoiceNodeType
            || nodeType == DialogueAuthoringRegistry.EndNodeType
                ? null
                : NormalizeOptional(node.NextNodeId)?.ToLowerInvariant();
        var choices = nodeType == DialogueAuthoringRegistry.PlayerChoiceNodeType
            ? NormalizeChoices(node.Choices)
            : [];

        return new DialogueNode(
            NormalizeStableId(node.NodeId),
            nodeType,
            NormalizeOptional(node.Speaker),
            NormalizeOptional(node.Text),
            nextNodeId,
            nodeType == DialogueAuthoringRegistry.EndNodeType || node.Dismissible,
            node.CanvasX,
            node.CanvasY,
            NormalizeOptional(node.EditorNotes),
            choices);
    }

    public static IReadOnlyList<DialogueChoice> NormalizeChoices(IReadOnlyList<DialogueChoice> choices) =>
        choices
            .Select(choice => new DialogueChoice(
                NormalizeStableId(choice.ChoiceId),
                NormalizeRequired(choice.Text),
                NormalizeStableId(choice.TargetNodeId),
                choice.ChoiceOrder,
                NormalizeConditions(choice.Conditions)))
            .OrderBy(choice => choice.ChoiceOrder)
            .ThenBy(choice => choice.ChoiceId, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<DialogueCondition> NormalizeConditions(IReadOnlyList<DialogueCondition> conditions) =>
        conditions
            .Select(NormalizeCondition)
            .ToArray();

    public static DialogueCondition NormalizeCondition(DialogueCondition condition) =>
        new(
            NormalizeConditionType(condition.ConditionType),
            NormalizeOptional(condition.QuestId)?.ToLowerInvariant(),
            NormalizeOptional(condition.Status)?.ToLowerInvariant(),
            NormalizeOptional(condition.StepId)?.ToLowerInvariant(),
            NormalizeOptional(condition.ItemId)?.ToLowerInvariant(),
            condition.Quantity);

    public static string BuildSemanticComparisonInput(DialogueDraft draft)
    {
        var normalized = NormalizeDraft(draft);
        return string.Join(
            "\n",
            normalized.DisplayName,
            normalized.SchemaVersion,
            string.Join("|", normalized.EntryPoints.Select(entry =>
                $"{entry.EntryId}:{entry.NodeId}:{entry.Priority}:{entry.EntryOrder}:{ConditionsSignature(entry.Conditions)}")),
            string.Join("|", normalized.Nodes.Select(node =>
                $"{node.NodeId}:{node.NodeType}:{node.Speaker}:{node.Text}:{node.NextNodeId}:{node.Dismissible}:{node.CanvasX:R}:{node.CanvasY:R}:{node.EditorNotes}:{string.Join(",", node.Choices.Select(choice => $"{choice.ChoiceId}>{choice.TargetNodeId}:{choice.ChoiceOrder}:{choice.Text}:{ConditionsSignature(choice.Conditions)}"))}")),
            normalized.MetadataDescription ?? string.Empty,
            normalized.Notes ?? string.Empty);
    }

    private static string ConditionsSignature(IReadOnlyList<DialogueCondition> conditions) =>
        string.Join(",", conditions.Select(condition =>
            $"{condition.ConditionType}:{condition.QuestId}:{condition.Status}:{condition.StepId}:{condition.ItemId}:{condition.Quantity}"));

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierRegex();
}
