using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static class NpcDomainRules
{
    public static string NormalizeStableId(string value) =>
        NormalizeRequired(value).ToLowerInvariant();

    public static bool IsStableId(string value)
    {
        var normalized = NormalizeStableId(value);
        if (normalized.Length == 0 || !char.IsAsciiLetterLower(normalized[0]))
        {
            return false;
        }

        return normalized.Split('_').All(IsIdentifierSegment);
    }

    public static string NormalizeRequired(string value) => value.Trim();

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    public static string NormalizePublicationState(string value) =>
        NormalizeRequired(value) switch
        {
            "draft" or "Draft" => "Draft",
            "published" or "Published" => "Published",
            "disabled" or "Disabled" => "Disabled",
            var normalized => normalized
        };

    public static string NormalizeVisualMode(string? value) =>
        string.Equals(value?.Trim(), "composite_rig", StringComparison.OrdinalIgnoreCase)
            ? "composite_rig"
            : "flat_sprite";

    public static string NormalizeMovementBehavior(string value) =>
        NormalizeStableId(value);

    public static string NormalizeInteractionType(string value) =>
        NormalizeStableId(value);

    public static bool IsSupportedPublicationState(string value) =>
        NormalizePublicationState(value) is "Draft" or "Published" or "Disabled";

    public static bool IsSupportedMovementBehavior(string value) =>
        NormalizeMovementBehavior(value) is "static" or "random_wander";

    public static bool IsSupportedInteractionType(string value) =>
        NormalizeInteractionType(value) == NpcAuthoringRegistry.DefaultInteraction;

    public static bool IsFinite(double value) =>
        double.IsFinite(value);

    public static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;

    public static bool AreSourceDimensionsValid(int width, int height) =>
        width > 0 && height > 0;

    public static bool IsInitialFootprintSupported(int widthTiles, int heightTiles) =>
        widthTiles == NpcAuthoringRegistry.InitialFootprintWidthTiles
        && heightTiles == NpcAuthoringRegistry.InitialFootprintHeightTiles;

    public static bool IsWanderRadiusSupported(int wanderRadiusTiles) =>
        wanderRadiusTiles is >= 0 and <= NpcAuthoringRegistry.MaxWanderRadiusTiles;

    public static bool IsMovementConsistent(
        string movementBehavior,
        int wanderRadiusTiles)
    {
        var normalizedMovement = NormalizeMovementBehavior(movementBehavior);
        return normalizedMovement switch
        {
            "static" => wanderRadiusTiles == 0,
            "random_wander" => wanderRadiusTiles > 0,
            _ => false
        };
    }

    public static bool IsTickIntervalSupported(int tickIntervalMs) =>
        tickIntervalMs >= NpcAuthoringRegistry.MinimumTickIntervalMilliseconds;

    public static bool IsIdleChanceSupported(double idleChance) =>
        double.IsFinite(idleChance) && idleChance is >= 0 and <= 1;

    public static bool IsInteractionRangeSupported(int interactionRangeTiles) =>
        interactionRangeTiles >= NpcAuthoringRegistry.MinimumInteractionRangeTiles;

    public static bool IsDialogueReferenceConsistent(
        bool interactionEnabled,
        string? defaultDialogueId)
    {
        var normalizedDialogueId = NormalizeOptional(defaultDialogueId);
        return !interactionEnabled || normalizedDialogueId is not null;
    }

    public static NpcDraft NormalizeDraft(NpcDraft draft)
    {
        var movementBehavior = NormalizeMovementBehavior(draft.MovementBehavior);
        var interactionEnabled = draft.InteractionEnabled;
        return new NpcDraft(
            NormalizeRequired(draft.DisplayName),
            NormalizeRequired(draft.VisualTexturePath),
            draft.SourceWidth,
            draft.SourceHeight,
            draft.VisualAnchorOffsetX,
            draft.VisualAnchorOffsetY,
            draft.VisualRenderScale,
            draft.FootprintWidthTiles,
            draft.FootprintHeightTiles,
            movementBehavior,
            movementBehavior == "static" ? 0 : draft.WanderRadiusTiles,
            draft.TickIntervalMs,
            draft.IdleChance,
            interactionEnabled,
            draft.InteractionRangeTiles,
            NormalizeInteractionType(draft.DefaultInteraction),
            interactionEnabled ? NormalizeOptional(draft.DefaultDialogueId) : null,
            NormalizeOptional(draft.Notes),
            draft.ExpectedUpdatedAtUtc,
            draft.PreviewSignature)
        {
            VisualMode = NormalizeVisualMode(draft.VisualMode),
            CompositeVisual = draft.CompositeVisual?.Clone()
        };
    }

    public static string BuildSemanticComparisonInput(NpcDraft draft)
    {
        var normalized = NormalizeDraft(draft);
        return string.Join(
            "\n",
            normalized.DisplayName,
            normalized.VisualTexturePath,
            normalized.SourceWidth,
            normalized.SourceHeight,
            normalized.VisualAnchorOffsetX.ToString("R"),
            normalized.VisualAnchorOffsetY.ToString("R"),
            normalized.VisualRenderScale.ToString("R"),
            normalized.FootprintWidthTiles,
            normalized.FootprintHeightTiles,
            normalized.MovementBehavior,
            normalized.WanderRadiusTiles,
            normalized.TickIntervalMs,
            normalized.IdleChance.ToString("R"),
            normalized.InteractionEnabled,
            normalized.InteractionRangeTiles,
            normalized.DefaultInteraction,
            normalized.DefaultDialogueId ?? string.Empty,
            normalized.Notes ?? string.Empty,
            normalized.VisualMode,
            normalized.CompositeVisual?.GetRawText() ?? string.Empty);
    }

    private static bool IsIdentifierSegment(string segment) =>
        segment.Length > 0
            && segment.All(character =>
                char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character));
}
