using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static class MobDomainRules
{
    public static string NormalizeVisualMode(string? value) =>
        string.Equals(value?.Trim(), "composite_rig", StringComparison.OrdinalIgnoreCase)
            ? "composite_rig"
            : "flat_sprite";

    public static string NormalizeStableId(string value) =>
        NormalizeRequired(value).ToLowerInvariant();

    public static bool IsStableId(string value)
    {
        var normalized = NormalizeStableId(value);
        if (normalized.Length == 0)
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

    public static string NormalizeAttackType(string value) =>
        NormalizeStableId(value);

    public static string? NormalizeAccuracyStyle(string? value) =>
        NormalizeOptional(value)?.ToLowerInvariant();

    public static string NormalizeMovementBehavior(string value) =>
        NormalizeStableId(value);

    public static string NormalizeAggressionMode(string value) =>
        NormalizeStableId(value);

    public static string NormalizeReturnHomeBehavior(string value) =>
        NormalizeStableId(value);

    public static string NormalizeFactionDisposition(string value) =>
        NormalizeStableId(value);

    public static bool IsSupportedPublicationState(string value) =>
        NormalizePublicationState(value) is "Draft" or "Published" or "Disabled";

    public static bool IsSupportedAttackType(string value) =>
        NormalizeAttackType(value) == "melee";

    public static bool IsSupportedAccuracyStyle(string? value) =>
        NormalizeAccuracyStyle(value) is "thrust" or "slash" or "crush";

    public static bool IsSupportedMovementBehavior(string value) =>
        NormalizeMovementBehavior(value) is "static" or "random_wander";

    public static bool IsSupportedAggressionMode(string value) =>
        NormalizeAggressionMode(value) is "passive" or "retaliatory" or "proactive";

    public static bool IsSupportedReturnHomeBehavior(string value) =>
        NormalizeReturnHomeBehavior(value) is "none" or "return_to_spawn";

    public static bool IsSupportedFactionDisposition(string value) =>
        NormalizeFactionDisposition(value) is "hostile" or "neutral";

    public static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0;

    public static bool IsFinite(double value) =>
        double.IsFinite(value);

    public static bool AreSourceDimensionsValid(int width, int height) =>
        width > 0 && height > 0;

    public static bool IsFootprintValid(int widthTiles, int heightTiles) =>
        widthTiles > 0 && heightTiles > 0;

    public static bool IsAttackSpeedSupported(int attackSpeedUnits) =>
        attackSpeedUnits is >= MobAuthoringRegistry.MinAttackSpeedUnits
            and <= MobAuthoringRegistry.MaxAttackSpeedUnits;

    public static bool IsRangeSupported(int minimumRangeTiles, int maximumRangeTiles) =>
        minimumRangeTiles >= 0
        && maximumRangeTiles >= minimumRangeTiles
        && maximumRangeTiles <= MobAuthoringRegistry.MaxRangeTiles;

    public static bool IsWanderRadiusSupported(int wanderRadiusTiles) =>
        wanderRadiusTiles is >= 0 and <= MobAuthoringRegistry.MaxWanderRadiusTiles;

    public static bool IsAggressionRadiusSupported(int aggressionRadiusTiles) =>
        aggressionRadiusTiles is >= 0 and <= MobAuthoringRegistry.MaxAggressionRadiusTiles;

    public static bool IsLeashRadiusSupported(int leashRadiusTiles) =>
        leashRadiusTiles is >= 0 and <= MobAuthoringRegistry.MaxLeashRadiusTiles;

    public static bool IsBehaviorRadiusConsistent(
        string movementBehavior,
        int wanderRadiusTiles,
        string aggressionMode,
        int aggressionRadiusTiles,
        int leashRadiusTiles)
    {
        var normalizedMovement = NormalizeMovementBehavior(movementBehavior);
        var normalizedAggression = NormalizeAggressionMode(aggressionMode);
        if (normalizedMovement == "static" && wanderRadiusTiles != 0)
        {
            return false;
        }

        if (normalizedMovement == "random_wander" && wanderRadiusTiles <= 0)
        {
            return false;
        }

        if (normalizedAggression is "passive" or "retaliatory" && aggressionRadiusTiles != 0)
        {
            return false;
        }

        if (normalizedAggression == "proactive" && aggressionRadiusTiles <= 0)
        {
            return false;
        }

        return leashRadiusTiles >= wanderRadiusTiles
            && leashRadiusTiles >= aggressionRadiusTiles;
    }

    public static bool IsLevelSupported(int level) =>
        level is >= 0 and <= MobAuthoringRegistry.MaxMobLevel;

    public static bool IsCombatBonusSupported(int value) =>
        value is >= -MobAuthoringRegistry.MaxCombatBonusMagnitude
            and <= MobAuthoringRegistry.MaxCombatBonusMagnitude;

    public static bool IsDropOrderSupported(int dropOrder) =>
        dropOrder is >= 0 and <= MobAuthoringRegistry.MaxDropOrder;

    public static bool IsStackCountSupported(int stackCount) =>
        stackCount is >= 1 and <= MobAuthoringRegistry.MaxStackCount;

    public static bool IsProactiveTargetingConsistent(
        bool canProactivelyTargetHostileMobs,
        string? combatFactionId,
        int mobDetectionRadiusTiles,
        int mobTargetScanIntervalMs,
        int mobTargetScanCandidateLimit)
    {
        if (!canProactivelyTargetHostileMobs)
        {
            return mobDetectionRadiusTiles >= 0
                && mobTargetScanIntervalMs >= 0
                && mobTargetScanCandidateLimit >= 0;
        }

        return NormalizeOptional(combatFactionId) is not null
            && mobDetectionRadiusTiles > 0
            && mobTargetScanIntervalMs > 0
            && mobTargetScanCandidateLimit > 0;
    }

    public static IReadOnlyList<MobDropDraft> NormalizeGuaranteedDrops(
        IEnumerable<MobDropDraft>? drops) =>
        (drops ?? [])
            .Select(drop => new MobDropDraft(
                drop.DropOrder,
                NormalizeStableId(drop.ItemId),
                drop.StackCount))
            .OrderBy(drop => drop.DropOrder)
            .ThenBy(drop => drop.ItemId, StringComparer.Ordinal)
            .ToArray();

    public static bool HasDuplicateDropOrders(IEnumerable<MobDropDraft> drops) =>
        drops
            .GroupBy(drop => drop.DropOrder)
            .Any(group => group.Count() > 1);

    public static bool HasDuplicateDropItems(IEnumerable<MobDropDraft> drops) =>
        drops
            .GroupBy(drop => NormalizeStableId(drop.ItemId))
            .Any(group => group.Count() > 1);

    private static bool IsIdentifierSegment(string segment) =>
        segment.Length > 0
            && segment.All(character =>
                char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character));
}
