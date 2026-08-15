using System.Numerics;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public static partial class LootTableDomainRules
{
    public const int MaxNestingDepth = 8;
    public const int MaxBoundedExpansion = 4096;

    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Disabled = "Disabled";

    public const string SectionGuaranteed = "guaranteed";
    public const string SectionPreRoll = "pre_roll";
    public const string SectionMain = "main";
    public const string SectionTertiary = "tertiary";

    public const string RollGuaranteedAll = "guaranteed_all";
    public const string RollWeightedOne = "weighted_one";
    public const string RollIndependent = "independent";

    public const string OutcomeItem = "item";
    public const string OutcomeLootTable = "loot_table";
    public const string OutcomeNoDrop = "no_drop";

    public const string FailureContinue = "continue";
    public const string FailureFallthroughToMain = "fallthrough_to_main";
    public const string FailureStop = "stop";

    public const string SuccessSequenceContinue = "continue";
    public const string SuccessSequenceStop = "stop";

    public const string SuccessMainKeep = "keep_main";
    public const string SuccessMainSuppress = "suppress_main";

    public static string NormalizeStableId(string value) =>
        NormalizeRequired(value).ToLowerInvariant();

    public static string NormalizeRequired(string value) => value.Trim();

    public static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    public static string NormalizePublicationState(string value) =>
        NormalizeRequired(value) switch
        {
            "draft" or "Draft" => Draft,
            "published" or "Published" => Published,
            "disabled" or "Disabled" => Disabled,
            var normalized => normalized
        };

    public static string NormalizeStableVocabulary(string value) =>
        NormalizeStableId(value);

    public static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && StableIdentifierRegex().IsMatch(value);

    public static bool IsPublicationState(string value) =>
        NormalizePublicationState(value) is Draft or Published or Disabled;

    public static bool IsSectionKind(string? value) =>
        value is SectionGuaranteed or SectionPreRoll or SectionMain or SectionTertiary;

    public static bool IsRollKind(string? value) =>
        value is RollGuaranteedAll or RollWeightedOne or RollIndependent;

    public static bool IsOutcomeKind(string? value) =>
        value is OutcomeItem or OutcomeLootTable or OutcomeNoDrop;

    public static bool IsPreRollFailureBehavior(string? value) =>
        value is FailureContinue or FailureFallthroughToMain or FailureStop;

    public static bool IsPreRollSuccessSequenceBehavior(string? value) =>
        value is SuccessSequenceContinue or SuccessSequenceStop;

    public static bool IsPreRollSuccessMainBehavior(string? value) =>
        value is SuccessMainKeep or SuccessMainSuppress;

    public static NormalizedLootTableDraft Normalize(
        string displayName,
        string? description,
        IReadOnlyList<LootRollGroupDraft>? groups)
    {
        return new NormalizedLootTableDraft(
            NormalizeRequired(displayName),
            NormalizeOptional(description) ?? string.Empty,
            (groups ?? [])
                .Select(NormalizeGroup)
                .OrderBy(group => SectionSort(group.SectionKind))
                .ThenBy(group => group.Order)
                .ThenBy(group => group.RollGroupId, StringComparer.Ordinal)
                .ToArray());
    }

    private static NormalizedLootRollGroup NormalizeGroup(LootRollGroupDraft group)
    {
        var sectionKind = NormalizeStableVocabulary(group.SectionKind);
        return new NormalizedLootRollGroup(
            NormalizeStableId(group.RollGroupId),
            group.Order,
            sectionKind,
            NormalizeStableVocabulary(group.RollKind),
            group.RollCount,
            NormalizeOptional(group.PreRollFailureBehavior) is { } failureBehavior
                ? NormalizeStableVocabulary(failureBehavior)
                : null,
            NormalizeOptional(group.PreRollSuccessSequenceBehavior) is { } successSequenceBehavior
                ? NormalizeStableVocabulary(successSequenceBehavior)
                : null,
            NormalizeOptional(group.PreRollSuccessMainBehavior) is { } successMainBehavior
                ? NormalizeStableVocabulary(successMainBehavior)
                : null,
            NormalizeOptional(group.DisplayName),
            (group.Outcomes ?? [])
                .Select(NormalizeOutcome)
                .OrderBy(outcome => outcome.Order)
                .ThenBy(outcome => outcome.OutcomeId, StringComparer.Ordinal)
                .ToArray());
    }

    private static NormalizedLootOutcome NormalizeOutcome(LootOutcomeDraft outcome)
    {
        return new NormalizedLootOutcome(
            NormalizeStableId(outcome.OutcomeId),
            outcome.Order,
            NormalizeStableVocabulary(outcome.OutcomeKind),
            NormalizeOptional(outcome.ItemId)?.ToLowerInvariant(),
            NormalizeOptional(outcome.NestedLootTableId)?.ToLowerInvariant(),
            outcome.MinQuantity,
            outcome.MaxQuantity,
            outcome.Weight,
            outcome.ProbabilityNumerator,
            outcome.ProbabilityDenominator);
    }

    public static int SectionSort(string sectionKind) =>
        sectionKind switch
        {
            SectionGuaranteed => 0,
            SectionPreRoll => 1,
            SectionMain => 2,
            SectionTertiary => 3,
            _ => 9
        };

    public static LootExactValue ToContract(ExactRational value)
    {
        var decimalValue = value.ToDecimal();
        var display = value.Denominator == BigInteger.One
            ? value.Numerator.ToString()
            : $"{value.Numerator}/{value.Denominator}";
        return new LootExactValue(
            value.Numerator.ToString(),
            value.Denominator.ToString(),
            decimalValue,
            display);
    }

    [GeneratedRegex("^[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierRegex();
}

public sealed record NormalizedLootTableDraft(
    string DisplayName,
    string Description,
    IReadOnlyList<NormalizedLootRollGroup> Groups);

public sealed record NormalizedLootRollGroup(
    string RollGroupId,
    int Order,
    string SectionKind,
    string RollKind,
    int RollCount,
    string? PreRollFailureBehavior,
    string? PreRollSuccessSequenceBehavior,
    string? PreRollSuccessMainBehavior,
    string? DisplayName,
    IReadOnlyList<NormalizedLootOutcome> Outcomes);

public sealed record NormalizedLootOutcome(
    string OutcomeId,
    int Order,
    string OutcomeKind,
    string? ItemId,
    string? NestedLootTableId,
    int? MinQuantity,
    int? MaxQuantity,
    int? Weight,
    long? ProbabilityNumerator,
    long? ProbabilityDenominator);

public readonly record struct ExactRational
{
    public static readonly ExactRational Zero = new(BigInteger.Zero, BigInteger.One);
    public static readonly ExactRational One = new(BigInteger.One, BigInteger.One);

    public ExactRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "Rational denominator must be nonzero.");
        }

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    public BigInteger Numerator { get; }

    public BigInteger Denominator { get; }

    public static ExactRational FromInteger(long value) => new(value, 1);

    public static ExactRational operator +(ExactRational left, ExactRational right) =>
        new(left.Numerator * right.Denominator + right.Numerator * left.Denominator, left.Denominator * right.Denominator);

    public static ExactRational operator -(ExactRational left, ExactRational right) =>
        new(left.Numerator * right.Denominator - right.Numerator * left.Denominator, left.Denominator * right.Denominator);

    public static ExactRational operator *(ExactRational left, ExactRational right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);

    public static ExactRational operator /(ExactRational left, ExactRational right) =>
        new(left.Numerator * right.Denominator, left.Denominator * right.Numerator);

    public decimal ToDecimal()
    {
        if (Numerator.IsZero)
        {
            return 0m;
        }

        return (decimal)Numerator / (decimal)Denominator;
    }
}
