using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static class QuestDomainRules
{
    public static string NormalizeStableId(string value) =>
        DialogueDomainRules.NormalizeStableId(value);

    public static bool IsStableId(string value) =>
        DialogueDomainRules.IsStableId(value);

    public static string NormalizePublicationState(string value) =>
        DialogueDomainRules.NormalizePublicationState(value);

    public static QuestDraft NormalizeDraft(QuestDraft draft) =>
        NormalizeDraft(
            draft.DisplayName,
            draft.SchemaVersion,
            draft.Steps,
            draft.Transitions,
            draft.ExpectedUpdatedAtUtc,
            draft.PreviewSignature);

    public static QuestDraft NormalizeDraft(
        string displayName,
        int schemaVersion,
        IReadOnlyList<QuestStep> steps,
        IReadOnlyList<QuestTransition> transitions,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? previewSignature) =>
        new(
            DialogueDomainRules.NormalizeRequired(displayName),
            schemaVersion,
            steps
                .Select(step => new QuestStep(
                    NormalizeStableId(step.StepId),
                    DialogueDomainRules.NormalizeRequired(step.DisplayName),
                    step.StepOrder))
                .OrderBy(step => step.StepOrder)
                .ThenBy(step => step.StepId, StringComparer.Ordinal)
                .ToArray(),
            transitions
                .Select(transition => new QuestTransition(
                    NormalizeStableId(transition.TransitionId),
                    NormalizeStatus(transition.SourceStatus),
                    NormalizeOptionalStableId(transition.SourceStepId),
                    NormalizeStatus(transition.TargetStatus),
                    NormalizeOptionalStableId(transition.TargetStepId),
                    transition.TransitionOrder))
                .OrderBy(transition => transition.TransitionOrder)
                .ThenBy(transition => transition.TransitionId, StringComparer.Ordinal)
                .ToArray(),
            expectedUpdatedAtUtc,
            previewSignature);

    private static string NormalizeStatus(string value) =>
        DialogueDomainRules.NormalizeRequired(value).ToLowerInvariant();

    private static string? NormalizeOptionalStableId(string? value)
    {
        var normalized = DialogueDomainRules.NormalizeOptional(value);
        return normalized is null ? null : NormalizeStableId(normalized);
    }
}
