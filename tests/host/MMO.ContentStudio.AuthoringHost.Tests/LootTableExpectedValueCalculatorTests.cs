using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class LootTableExpectedValueCalculatorTests
{
    [Fact]
    public void CalculatesGuaranteedItemExpectedValue()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "guaranteed",
                    LootTableDomainRules.SectionGuaranteed,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("coins", "coins")])),
            Item("coins", referenceValue: 5));

        AssertValid(report);
        AssertExact("1", ItemTotal(report, "coins").ExpectedQuantity);
        AssertExact("5", ItemTotal(report, "coins").ExpectedReferenceValue);
        AssertExact("5", report.TotalReferenceValue);
    }

    [Fact]
    public void CalculatesWeightedItemAndNoDropExpectedValue()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "main",
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollWeightedOne,
                    [
                        WeightedItemOutcome("gem", "gem", weight: 1),
                        NoDropOutcome("miss", weight: 3)
                    ])),
            Item("gem", referenceValue: 10));

        AssertValid(report);
        AssertExact("1/4", ItemTotal(report, "gem").ExpectedQuantity);
        AssertExact("5/2", report.TotalReferenceValue);
        AssertExact("3/4", report.NoDropProbability);
    }

    [Fact]
    public void CalculatesIndependentTertiaryOneInNExpectedValue()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "tertiary",
                    LootTableDomainRules.SectionTertiary,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("herb", "herb", numerator: 1, denominator: 5)])),
            Item("herb", referenceValue: 25));

        AssertValid(report);
        AssertExact("1/5", ItemTotal(report, "herb").ExpectedQuantity);
        AssertExact("5", report.TotalReferenceValue);
    }

    [Fact]
    public void CalculatesInclusiveQuantityRangeExpectedValue()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "guaranteed",
                    LootTableDomainRules.SectionGuaranteed,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("ore", "ore", minQuantity: 2, maxQuantity: 4)])),
            Item("ore", referenceValue: 3));

        AssertValid(report);
        AssertExact("3", ItemTotal(report, "ore").ExpectedQuantity);
        AssertExact("9", report.TotalReferenceValue);
    }

    [Fact]
    public void CalculatesFixedMultipleMainRollExpectedValue()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "main",
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollWeightedOne,
                    [
                        WeightedItemOutcome("ore", "ore", weight: 1),
                        NoDropOutcome("miss", weight: 1)
                    ],
                    rollCount: 3)),
            Item("ore", referenceValue: 4));

        AssertValid(report);
        AssertExact("3/2", ItemTotal(report, "ore").ExpectedQuantity);
        AssertExact("6", report.TotalReferenceValue);
    }

    [Fact]
    public void CalculatesNestedTableExpectedValue()
    {
        var child = Table(
            "child",
            Group(
                "child_drop",
                LootTableDomainRules.SectionGuaranteed,
                LootTableDomainRules.RollGuaranteedAll,
                [ItemOutcome("rune", "rune")]));
        var root = Table(
            "root",
            Group(
                "main",
                LootTableDomainRules.SectionMain,
                LootTableDomainRules.RollGuaranteedAll,
                [NestedOutcome("child_link", "child")]));

        var report = Calculate(root, [root, child], [Item("rune", referenceValue: 7)]);

        AssertValid(report);
        AssertExact("1", ItemTotal(report, "rune").ExpectedQuantity);
        AssertExact("7", report.TotalReferenceValue);
    }

    [Fact]
    public void AggregatesDuplicateSameItemPaths()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "first",
                    LootTableDomainRules.SectionGuaranteed,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("coins_a", "coins")],
                    order: 0),
                Group(
                    "second",
                    LootTableDomainRules.SectionGuaranteed,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("coins_b", "coins")],
                    order: 1)),
            Item("coins", referenceValue: 2));

        AssertValid(report);
        AssertExact("2", ItemTotal(report, "coins").ExpectedQuantity);
        AssertExact("4", report.TotalReferenceValue);
        Assert.Equal(2, report.PathContributions.Count(path => path.ItemId == "coins"));
    }

    [Fact]
    public void PreservesZeroReferenceValueItems()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "guaranteed",
                    LootTableDomainRules.SectionGuaranteed,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("shell", "shell", minQuantity: 5, maxQuantity: 5)])),
            Item("shell", referenceValue: 0));

        AssertValid(report);
        var total = ItemTotal(report, "shell");
        AssertExact("5", total.ExpectedQuantity);
        AssertExact("0", total.ExpectedReferenceValue);
        Assert.True(total.ZeroReferenceValue);
        AssertExact("0", report.TotalReferenceValue);
    }

    [Fact]
    public void PreRollSuccessKeepMainAllowsMainPath()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "rare_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("rare", "rare", numerator: 1, denominator: 2)],
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceStop,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainKeep),
                Group(
                    "main",
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("coins", "coins")])),
            Item("rare", referenceValue: 20),
            Item("coins", referenceValue: 100));

        AssertValid(report);
        AssertExact("1", ItemTotal(report, "coins").ExpectedQuantity);
        AssertExact("1/2", ItemTotal(report, "rare").ExpectedQuantity);
        AssertExact("110", report.TotalReferenceValue);
    }

    [Fact]
    public void PreRollSuccessSuppressMainSuppressesMainPath()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "rare_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("rare", "rare", numerator: 1, denominator: 2)],
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceStop,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainSuppress),
                Group(
                    "main",
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("coins", "coins")])),
            Item("rare", referenceValue: 20),
            Item("coins", referenceValue: 100));

        AssertValid(report);
        AssertExact("1/2", ItemTotal(report, "coins").ExpectedQuantity);
        AssertExact("1/2", ItemTotal(report, "rare").ExpectedQuantity);
        AssertExact("60", report.TotalReferenceValue);
    }

    [Fact]
    public void PreRollSuccessSequenceContinueKeepsEvaluatingLaterPreRolls()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "first_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("first", "first", numerator: 1, denominator: 2)],
                    preRollFailureBehavior: LootTableDomainRules.FailureStop,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceContinue,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainKeep,
                    order: 0),
                Group(
                    "second_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("second", "second", numerator: 1, denominator: 2)],
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceStop,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainKeep,
                    order: 1)),
            Item("first", referenceValue: 0),
            Item("second", referenceValue: 0));

        AssertValid(report);
        AssertExact("1/2", ItemTotal(report, "first").ExpectedQuantity);
        AssertExact("1/4", ItemTotal(report, "second").ExpectedQuantity);
    }

    [Fact]
    public void PreRollSuccessSequenceStopDoesNotEvaluateLaterPreRollsOnSuccess()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "first_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("first", "first", numerator: 1, denominator: 2)],
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceStop,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainKeep,
                    order: 0),
                Group(
                    "second_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("second", "second", numerator: 1, denominator: 2)],
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceStop,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainKeep,
                    order: 1)),
            Item("first", referenceValue: 0),
            Item("second", referenceValue: 0));

        AssertValid(report);
        AssertExact("1/2", ItemTotal(report, "first").ExpectedQuantity);
        AssertExact("1/4", ItemTotal(report, "second").ExpectedQuantity);
    }

    [Fact]
    public void IndependentPreRollMultipleRollsUsesAtLeastOneSuccessProbability()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "rare_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("rare", "rare", numerator: 1, denominator: 2)],
                    rollCount: 2,
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceStop,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainSuppress),
                Group(
                    "main",
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("coins", "coins")])),
            Item("rare", referenceValue: 1),
            Item("coins", referenceValue: 100));

        AssertValid(report);
        AssertExact("1", ItemTotal(report, "rare").ExpectedQuantity);
        AssertExact("1/4", ItemTotal(report, "coins").ExpectedQuantity);
        AssertExact("26", report.TotalReferenceValue);
    }

    [Fact]
    public void WeightedPreRollMultipleRollsKeepsAtLeastOneSuccessProbability()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "rare_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollWeightedOne,
                    [
                        WeightedItemOutcome("rare", "rare", weight: 1),
                        NoDropOutcome("miss", weight: 1)
                    ],
                    rollCount: 2,
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceStop,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainSuppress),
                Group(
                    "main",
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("coins", "coins")])),
            Item("rare", referenceValue: 1),
            Item("coins", referenceValue: 100));

        AssertValid(report);
        AssertExact("1", ItemTotal(report, "rare").ExpectedQuantity);
        AssertExact("1/4", ItemTotal(report, "coins").ExpectedQuantity);
        AssertExact("26", report.TotalReferenceValue);
    }

    [Fact]
    public void MultiStepPreRollsCalculateMainPathEligibilityExactly()
    {
        var report = Calculate(
            Table(
                "root",
                Group(
                    "suppressing_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("first", "first", numerator: 1, denominator: 2)],
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceContinue,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainSuppress,
                    order: 0),
                Group(
                    "final_gate",
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    [IndependentItemOutcome("second", "second", numerator: 1, denominator: 3)],
                    preRollFailureBehavior: LootTableDomainRules.FailureContinue,
                    preRollSuccessSequenceBehavior: LootTableDomainRules.SuccessSequenceStop,
                    preRollSuccessMainBehavior: LootTableDomainRules.SuccessMainKeep,
                    order: 1),
                Group(
                    "main",
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollGuaranteedAll,
                    [ItemOutcome("coins", "coins")])),
            Item("first", referenceValue: 0),
            Item("second", referenceValue: 0),
            Item("coins", referenceValue: 100));

        AssertValid(report);
        AssertExact("1/2", ItemTotal(report, "first").ExpectedQuantity);
        AssertExact("1/3", ItemTotal(report, "second").ExpectedQuantity);
        AssertExact("1/2", ItemTotal(report, "coins").ExpectedQuantity);
        AssertExact("50", report.TotalReferenceValue);
    }

    private static LootExpectedValueReport Calculate(LootTableRecord table, params LootItemRecord[] items) =>
        Calculate(table, [table], items);

    private static LootExpectedValueReport Calculate(
        LootTableRecord rootTable,
        IReadOnlyList<LootTableRecord> tables,
        IReadOnlyList<LootItemRecord> items) =>
        new LootTableExpectedValueCalculator().Calculate(rootTable.LootTableId, tables, items);

    private static LootTableRecord Table(string lootTableId, params LootRollGroupRecord[] groups) =>
        new(
            lootTableId,
            lootTableId,
            "",
            LootTableDomainRules.Published,
            null,
            groups,
            groups.Length,
            groups.Sum(group => group.Outcomes.Count),
            DateTimeOffset.Parse("2026-08-15T12:00:00Z"));

    private static LootRollGroupRecord Group(
        string rollGroupId,
        string sectionKind,
        string rollKind,
        IReadOnlyList<LootOutcomeRecord> outcomes,
        int rollCount = 1,
        string? preRollFailureBehavior = null,
        string? preRollSuccessSequenceBehavior = null,
        string? preRollSuccessMainBehavior = null,
        int order = 0) =>
        new(
            rollGroupId,
            order,
            sectionKind,
            rollKind,
            rollCount,
            preRollFailureBehavior,
            preRollSuccessSequenceBehavior,
            preRollSuccessMainBehavior,
            null,
            outcomes);

    private static LootOutcomeRecord ItemOutcome(
        string outcomeId,
        string itemId,
        int minQuantity = 1,
        int maxQuantity = 1) =>
        new(
            outcomeId,
            0,
            LootTableDomainRules.OutcomeItem,
            itemId,
            itemId,
            null,
            minQuantity,
            maxQuantity,
            null,
            null,
            null);

    private static LootOutcomeRecord WeightedItemOutcome(
        string outcomeId,
        string itemId,
        int weight) =>
        new(
            outcomeId,
            0,
            LootTableDomainRules.OutcomeItem,
            itemId,
            itemId,
            null,
            1,
            1,
            weight,
            null,
            null);

    private static LootOutcomeRecord IndependentItemOutcome(
        string outcomeId,
        string itemId,
        long numerator,
        long denominator) =>
        new(
            outcomeId,
            0,
            LootTableDomainRules.OutcomeItem,
            itemId,
            itemId,
            null,
            1,
            1,
            null,
            numerator,
            denominator);

    private static LootOutcomeRecord NoDropOutcome(
        string outcomeId,
        int weight) =>
        new(
            outcomeId,
            0,
            LootTableDomainRules.OutcomeNoDrop,
            null,
            null,
            null,
            null,
            null,
            weight,
            null,
            null);

    private static LootOutcomeRecord NestedOutcome(
        string outcomeId,
        string nestedLootTableId) =>
        new(
            outcomeId,
            0,
            LootTableDomainRules.OutcomeLootTable,
            null,
            null,
            nestedLootTableId,
            null,
            null,
            null,
            null,
            null);

    private static LootItemRecord Item(string itemId, long referenceValue) =>
        new(itemId, itemId, true, referenceValue);

    private static LootExpectedItemTotal ItemTotal(LootExpectedValueReport report, string itemId) =>
        report.ItemTotals.Single(item => item.ItemId == itemId);

    private static void AssertValid(LootExpectedValueReport report) => Assert.True(report.Valid);

    private static void AssertExact(string expected, LootExactValue actual) =>
        Assert.Equal(expected, actual.Display);
}
