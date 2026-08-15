using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class LootTableExpectedValueCalculator
{
    public LootExpectedValueReport Calculate(
        string rootLootTableId,
        IReadOnlyList<LootTableRecord> tables,
        IReadOnlyList<LootItemRecord> items,
        IReadOnlyList<ApiError>? validationMessages = null)
    {
        var tableLookup = tables.ToDictionary(table => table.LootTableId, StringComparer.Ordinal);
        var itemLookup = items.ToDictionary(item => item.ItemId, StringComparer.Ordinal);
        var errors = validationMessages?.Where(message => message.Severity == ValidationSeverity.Error).ToArray() ?? [];
        if (errors.Length > 0 || !tableLookup.ContainsKey(rootLootTableId))
        {
            return Empty(false, validationMessages ?? []);
        }

        var accumulator = new EvAccumulator(itemLookup);
        EvaluateTable(
            rootLootTableId,
            tableLookup,
            itemLookup,
            ExactRational.One,
            rootLootTableId,
            accumulator,
            depth: 1,
            visited: []);
        return accumulator.ToReport(true, validationMessages ?? []);
    }

    private static void EvaluateTable(
        string lootTableId,
        IReadOnlyDictionary<string, LootTableRecord> tables,
        IReadOnlyDictionary<string, LootItemRecord> items,
        ExactRational invocationProbability,
        string path,
        EvAccumulator accumulator,
        int depth,
        HashSet<string> visited)
    {
        if (depth > LootTableDomainRules.MaxNestingDepth
            || !visited.Add(lootTableId)
            || !tables.TryGetValue(lootTableId, out var table))
        {
            return;
        }

        foreach (var group in table.Groups.Where(group => group.SectionKind == LootTableDomainRules.SectionGuaranteed))
        {
            AddGroupContributions(group, tables, items, invocationProbability, $"{path}/{group.RollGroupId}", accumulator, depth, visited);
        }

        var mainEligibleProbability = EvaluatePreRolls(table, tables, items, invocationProbability, path, accumulator, depth, visited);
        foreach (var group in table.Groups.Where(group => group.SectionKind == LootTableDomainRules.SectionMain))
        {
            AddGroupContributions(group, tables, items, mainEligibleProbability, $"{path}/{group.RollGroupId}", accumulator, depth, visited);
        }

        foreach (var group in table.Groups.Where(group => group.SectionKind == LootTableDomainRules.SectionTertiary))
        {
            AddGroupContributions(group, tables, items, invocationProbability, $"{path}/{group.RollGroupId}", accumulator, depth, visited);
        }

        visited.Remove(lootTableId);
    }

    private static ExactRational EvaluatePreRolls(
        LootTableRecord table,
        IReadOnlyDictionary<string, LootTableRecord> tables,
        IReadOnlyDictionary<string, LootItemRecord> items,
        ExactRational invocationProbability,
        string path,
        EvAccumulator accumulator,
        int depth,
        HashSet<string> visited)
    {
        var activeKeepMain = invocationProbability;
        var activeSuppressMain = ExactRational.Zero;
        var finishedKeepMain = ExactRational.Zero;
        var finishedSuppressMain = ExactRational.Zero;

        foreach (var group in table.Groups.Where(group => group.SectionKind == LootTableDomainRules.SectionPreRoll).OrderBy(group => group.Order))
        {
            if (activeKeepMain.Numerator.IsZero && activeSuppressMain.Numerator.IsZero)
            {
                break;
            }

            var activeProbability = activeKeepMain + activeSuppressMain;
            var result = PreRollResult(group, tables, items, activeProbability, $"{path}/{group.RollGroupId}", accumulator, depth, visited);
            ApplyPreRollTransition(
                group,
                result,
                activeKeepMain,
                alreadySuppressed: false,
                ref finishedKeepMain,
                ref finishedSuppressMain,
                out var nextKeepFromKeep,
                out var nextSuppressFromKeep);
            ApplyPreRollTransition(
                group,
                result,
                activeSuppressMain,
                alreadySuppressed: true,
                ref finishedKeepMain,
                ref finishedSuppressMain,
                out var nextKeepFromSuppress,
                out var nextSuppressFromSuppress);

            activeKeepMain = nextKeepFromKeep + nextKeepFromSuppress;
            activeSuppressMain = nextSuppressFromKeep + nextSuppressFromSuppress;
        }

        finishedKeepMain += activeKeepMain;
        finishedSuppressMain += activeSuppressMain;
        _ = finishedSuppressMain;
        return finishedKeepMain;
    }

    private static void ApplyPreRollTransition(
        LootRollGroupRecord group,
        PreRollGroupResult result,
        ExactRational incoming,
        bool alreadySuppressed,
        ref ExactRational finishedKeepMain,
        ref ExactRational finishedSuppressMain,
        out ExactRational nextKeepMain,
        out ExactRational nextSuppressMain)
    {
        nextKeepMain = ExactRational.Zero;
        nextSuppressMain = ExactRational.Zero;
        if (incoming.Numerator.IsZero)
        {
            return;
        }

        var successMass = incoming * result.SuccessProbability;
        var failureMass = incoming * result.FailureProbability;
        var successSuppresses = alreadySuppressed ||
                                group.PreRollSuccessMainBehavior == LootTableDomainRules.SuccessMainSuppress;

        if (group.PreRollSuccessSequenceBehavior == LootTableDomainRules.SuccessSequenceContinue)
        {
            if (successSuppresses)
            {
                nextSuppressMain += successMass;
            }
            else
            {
                nextKeepMain += successMass;
            }
        }
        else if (successSuppresses)
        {
            finishedSuppressMain += successMass;
        }
        else
        {
            finishedKeepMain += successMass;
        }

        if (group.PreRollFailureBehavior == LootTableDomainRules.FailureContinue)
        {
            if (alreadySuppressed)
            {
                nextSuppressMain += failureMass;
            }
            else
            {
                nextKeepMain += failureMass;
            }
        }
        else if (alreadySuppressed)
        {
            finishedSuppressMain += failureMass;
        }
        else
        {
            finishedKeepMain += failureMass;
        }
    }

    private static PreRollGroupResult PreRollResult(
        LootRollGroupRecord group,
        IReadOnlyDictionary<string, LootTableRecord> tables,
        IReadOnlyDictionary<string, LootItemRecord> items,
        ExactRational groupProbability,
        string path,
        EvAccumulator accumulator,
        int depth,
        HashSet<string> visited)
    {
        if (group.RollKind == LootTableDomainRules.RollWeightedOne)
        {
            var totalWeight = group.Outcomes.Sum(outcome => outcome.Weight ?? 0);
            if (totalWeight <= 0)
            {
                return new PreRollGroupResult(ExactRational.Zero, ExactRational.One);
            }

            var perRollNoDrop = ExactRational.Zero;
            foreach (var outcome in group.Outcomes)
            {
                var probability = new ExactRational(outcome.Weight ?? 0, totalWeight);
                if (outcome.OutcomeKind == LootTableDomainRules.OutcomeNoDrop)
                {
                    perRollNoDrop += probability;
                    accumulator.AddNoDrop(groupProbability * probability);
                }
                else
                {
                    AddOutcomeContribution(group, outcome, tables, items, groupProbability * probability * group.RollCount.ToExact(), path, accumulator, depth, visited);
                }
            }

            var failure = Pow(perRollNoDrop, group.RollCount);
            return new PreRollGroupResult(ExactRational.One - failure, failure);
        }

        if (group.RollKind == LootTableDomainRules.RollIndependent)
        {
            var perRollNoSuccess = ExactRational.One;
            foreach (var outcome in group.Outcomes)
            {
                var probability = new ExactRational(outcome.ProbabilityNumerator ?? 0, outcome.ProbabilityDenominator ?? 1);
                if (outcome.OutcomeKind == LootTableDomainRules.OutcomeNoDrop)
                {
                    accumulator.AddNoDrop(groupProbability * probability);
                    continue;
                }

                perRollNoSuccess *= ExactRational.One - probability;
                AddOutcomeContribution(group, outcome, tables, items, groupProbability * probability * group.RollCount.ToExact(), path, accumulator, depth, visited);
            }

            var failure = Pow(perRollNoSuccess, group.RollCount);
            return new PreRollGroupResult(ExactRational.One - failure, failure);
        }

        foreach (var outcome in group.Outcomes)
        {
            AddOutcomeContribution(group, outcome, tables, items, groupProbability, path, accumulator, depth, visited);
        }

        return new PreRollGroupResult(
            group.Outcomes.Any(outcome => outcome.OutcomeKind != LootTableDomainRules.OutcomeNoDrop) ? ExactRational.One : ExactRational.Zero,
            group.Outcomes.Any(outcome => outcome.OutcomeKind != LootTableDomainRules.OutcomeNoDrop) ? ExactRational.Zero : ExactRational.One);
    }

    private static void AddGroupContributions(
        LootRollGroupRecord group,
        IReadOnlyDictionary<string, LootTableRecord> tables,
        IReadOnlyDictionary<string, LootItemRecord> items,
        ExactRational groupProbability,
        string path,
        EvAccumulator accumulator,
        int depth,
        HashSet<string> visited)
    {
        if (groupProbability.Numerator.IsZero)
        {
            return;
        }

        if (group.RollKind == LootTableDomainRules.RollGuaranteedAll)
        {
            foreach (var outcome in group.Outcomes)
            {
                AddOutcomeContribution(group, outcome, tables, items, groupProbability, path, accumulator, depth, visited);
            }

            return;
        }

        if (group.RollKind == LootTableDomainRules.RollWeightedOne)
        {
            var totalWeight = group.Outcomes.Sum(outcome => outcome.Weight ?? 0);
            if (totalWeight <= 0)
            {
                return;
            }

            foreach (var outcome in group.Outcomes)
            {
                var probability = groupProbability * new ExactRational(outcome.Weight ?? 0, totalWeight) * group.RollCount.ToExact();
                AddOutcomeContribution(group, outcome, tables, items, probability, path, accumulator, depth, visited);
            }

            return;
        }

        foreach (var outcome in group.Outcomes)
        {
            var probability = groupProbability
                              * new ExactRational(outcome.ProbabilityNumerator ?? 0, outcome.ProbabilityDenominator ?? 1)
                              * group.RollCount.ToExact();
            AddOutcomeContribution(group, outcome, tables, items, probability, path, accumulator, depth, visited);
        }
    }

    private static void AddOutcomeContribution(
        LootRollGroupRecord group,
        LootOutcomeRecord outcome,
        IReadOnlyDictionary<string, LootTableRecord> tables,
        IReadOnlyDictionary<string, LootItemRecord> items,
        ExactRational probability,
        string path,
        EvAccumulator accumulator,
        int depth,
        HashSet<string> visited)
    {
        if (probability.Numerator.IsZero)
        {
            return;
        }

        var outcomePath = $"{path}/{outcome.OutcomeId}";
        if (outcome.OutcomeKind == LootTableDomainRules.OutcomeNoDrop)
        {
            accumulator.AddNoDrop(probability);
            accumulator.AddPath(outcomePath, group.SectionKind, null, null, ExactRational.Zero, ExactRational.Zero, probability);
            return;
        }

        if (outcome.OutcomeKind == LootTableDomainRules.OutcomeLootTable && outcome.NestedLootTableId is not null)
        {
            EvaluateTable(outcome.NestedLootTableId, tables, items, probability, outcomePath, accumulator, depth + 1, new HashSet<string>(visited, StringComparer.Ordinal));
            return;
        }

        if (outcome.ItemId is null || !items.TryGetValue(outcome.ItemId, out var item))
        {
            return;
        }

        var expectedQuantity = new ExactRational((outcome.MinQuantity ?? 0) + (outcome.MaxQuantity ?? 0), 2);
        var expectedQuantityContribution = probability * expectedQuantity;
        var expectedReferenceValue = expectedQuantityContribution * ExactRational.FromInteger(item.ReferenceValue);
        accumulator.AddItem(
            item,
            group.SectionKind,
            outcomePath,
            expectedQuantityContribution,
            expectedReferenceValue,
            probability);
    }

    private static ExactRational Pow(ExactRational value, int exponent)
    {
        var result = ExactRational.One;
        for (var index = 0; index < exponent; index++)
        {
            result *= value;
        }

        return result;
    }

    private static LootExpectedValueReport Empty(
        bool valid,
        IReadOnlyList<ApiError> diagnostics) =>
        new(
            valid,
            LootTableDomainRules.ToContract(ExactRational.Zero),
            [],
            [],
            [],
            LootTableDomainRules.ToContract(ExactRational.Zero),
            false,
            diagnostics);

    private sealed record PreRollGroupResult(
        ExactRational SuccessProbability,
        ExactRational FailureProbability);

    private sealed class EvAccumulator
    {
        private readonly IReadOnlyDictionary<string, LootItemRecord> _items;
        private readonly Dictionary<string, ItemAccumulator> _itemTotals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ExactRational> _sectionTotals = new(StringComparer.Ordinal);
        private readonly List<LootExpectedPathContribution> _paths = [];
        private ExactRational _noDropProbability = ExactRational.Zero;

        public EvAccumulator(IReadOnlyDictionary<string, LootItemRecord> items)
        {
            _items = items;
        }

        public void AddNoDrop(ExactRational probability) => _noDropProbability += probability;

        public void AddItem(
            LootItemRecord item,
            string sectionKind,
            string path,
            ExactRational expectedQuantity,
            ExactRational expectedReferenceValue,
            ExactRational effectiveProbability)
        {
            if (!_itemTotals.TryGetValue(item.ItemId, out var total))
            {
                total = new ItemAccumulator(item);
                _itemTotals[item.ItemId] = total;
            }

            total.ExpectedQuantity += expectedQuantity;
            total.ExpectedReferenceValue += expectedReferenceValue;
            total.EffectiveProbability += effectiveProbability;
            _sectionTotals[sectionKind] = (_sectionTotals.TryGetValue(sectionKind, out var sectionTotal)
                ? sectionTotal
                : ExactRational.Zero) + expectedReferenceValue;
            AddPath(path, sectionKind, item.ItemId, item.DisplayName, expectedQuantity, expectedReferenceValue, effectiveProbability);
        }

        public void AddPath(
            string path,
            string sectionKind,
            string? itemId,
            string? displayName,
            ExactRational expectedQuantity,
            ExactRational expectedReferenceValue,
            ExactRational effectiveProbability)
        {
            _paths.Add(new LootExpectedPathContribution(
                path,
                sectionKind,
                itemId,
                displayName,
                LootTableDomainRules.ToContract(expectedQuantity),
                LootTableDomainRules.ToContract(expectedReferenceValue),
                LootTableDomainRules.ToContract(effectiveProbability)));
        }

        public LootExpectedValueReport ToReport(
            bool valid,
            IReadOnlyList<ApiError> diagnostics)
        {
            var totalValue = _itemTotals.Values.Aggregate(ExactRational.Zero, (sum, item) => sum + item.ExpectedReferenceValue);
            return new LootExpectedValueReport(
                valid,
                LootTableDomainRules.ToContract(totalValue),
                _itemTotals.Values
                    .OrderBy(item => item.Item.ItemId, StringComparer.Ordinal)
                    .Select(item => new LootExpectedItemTotal(
                        item.Item.ItemId,
                        item.Item.DisplayName,
                        LootTableDomainRules.ToContract(item.ExpectedQuantity),
                        LootTableDomainRules.ToContract(item.ExpectedReferenceValue),
                        LootTableDomainRules.ToContract(item.EffectiveProbability),
                        item.Item.ReferenceValue,
                        item.Item.ReferenceValue == 0))
                    .ToArray(),
                _sectionTotals
                    .OrderBy(pair => LootTableDomainRules.SectionSort(pair.Key))
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new LootExpectedSectionTotal(
                        pair.Key,
                        LootTableDomainRules.ToContract(pair.Value)))
                    .ToArray(),
                _paths.OrderBy(path => path.Path, StringComparer.Ordinal).ToArray(),
                LootTableDomainRules.ToContract(_noDropProbability),
                false,
                diagnostics);
        }

        private sealed class ItemAccumulator
        {
            public ItemAccumulator(LootItemRecord item)
            {
                Item = item;
            }

            public LootItemRecord Item { get; }

            public ExactRational ExpectedQuantity { get; set; } = ExactRational.Zero;

            public ExactRational ExpectedReferenceValue { get; set; } = ExactRational.Zero;

            public ExactRational EffectiveProbability { get; set; } = ExactRational.Zero;
        }
    }
}

internal static class LootTableExactRationalExtensions
{
    public static ExactRational ToExact(this int value) => ExactRational.FromInteger(value);
}
