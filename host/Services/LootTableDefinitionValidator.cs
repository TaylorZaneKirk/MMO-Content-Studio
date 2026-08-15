using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class LootTableDefinitionValidator
{
    private static readonly HashSet<string> DraftBlockingCodes = new(StringComparer.Ordinal)
    {
        "invalid_loot_table_id",
        "loot_table_id_immutable",
        "invalid_loot_table_display_name",
        "invalid_loot_roll_group_id",
        "duplicate_loot_roll_group_id",
        "duplicate_loot_roll_group_order",
        "unsupported_loot_section_kind",
        "unsupported_loot_roll_kind",
        "invalid_loot_roll_count",
        "invalid_loot_outcome_id",
        "duplicate_loot_outcome_id",
        "duplicate_loot_outcome_order",
        "unsupported_loot_outcome_kind",
        "invalid_loot_weight",
        "invalid_loot_probability",
        "invalid_loot_quantity_range",
        "invalid_loot_outcome_shape",
        "invalid_loot_preroll_shape"
    };

    private readonly ILootTableRepository _repository;

    public LootTableDefinitionValidator(ILootTableRepository repository)
    {
        _repository = repository;
    }

    public async Task<LootTableValidationOutcome> ValidateAsync(
        string lootTableId,
        NormalizedLootTableDraft draft,
        LootTableRecord? existing,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ApiError>();
        ValidateIdentity(lootTableId, draft, existing, messages);
        ValidateShape(draft, messages);

        var allTables = await _repository.ListAsync(null, cancellationToken);
        var items = await _repository.LoadItemsAsync(cancellationToken);
        var mobBindings = await _repository.LoadMobBindingsAsync(cancellationToken);
        ValidateReferences(lootTableId, draft, existing, allTables, items, mobBindings, operation, messages);

        if (operation is "disable" or "delete")
        {
            if (existing is not null && existing.PublicationState == LootTableDomainRules.Published
                && await _repository.HasPublishedDependentsAsync(lootTableId, cancellationToken))
            {
                messages.Add(new ApiError(
                    "loot_table_published_dependents",
                    $"Loot table '{lootTableId}' is referenced by Published content and cannot be disabled or deleted yet.",
                    ValidationSeverity.Error,
                    "loot_table_id",
                    "Remove Published parent-table or mob root references first."));
            }
        }

        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        var hasDraftBlocking = messages.Any(IsDraftBlocking);
        return new LootTableValidationOutcome(
            !hasDraftBlocking,
            !hasErrors,
            messages);
    }

    public static bool IsDraftBlocking(ApiError error) =>
        error.Severity == ValidationSeverity.Error
        && DraftBlockingCodes.Contains(error.Code);

    public static void ValidateIdentity(
        string lootTableId,
        NormalizedLootTableDraft draft,
        LootTableRecord? existing,
        ICollection<ApiError> messages)
    {
        if (!LootTableDomainRules.IsStableId(lootTableId))
        {
            messages.Add(new ApiError(
                "invalid_loot_table_id",
                "Loot table IDs must be lower snake case.",
                ValidationSeverity.Error,
                "loot_table_id"));
        }

        if (existing is not null && existing.LootTableId != lootTableId)
        {
            messages.Add(new ApiError(
                "loot_table_id_immutable",
                "Loot table identity is immutable after creation.",
                ValidationSeverity.Error,
                "loot_table_id"));
        }

        if (draft.DisplayName.Length is < 1 or > 100 || draft.DisplayName.Any(char.IsControl))
        {
            messages.Add(new ApiError(
                "invalid_loot_table_display_name",
                "Display name must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "display_name"));
        }
    }

    public static void ValidateShape(
        NormalizedLootTableDraft draft,
        ICollection<ApiError> messages)
    {
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var groupOrders = new HashSet<(string SectionKind, int Order)>();
        var hasMain = draft.Groups.Any(group => group.SectionKind == LootTableDomainRules.SectionMain);

        foreach (var group in draft.Groups)
        {
            ValidateGroup(group, hasMain, groupIds, groupOrders, messages);
        }
    }

    private static void ValidateGroup(
        NormalizedLootRollGroup group,
        bool hasMain,
        HashSet<string> groupIds,
        HashSet<(string SectionKind, int Order)> groupOrders,
        ICollection<ApiError> messages)
    {
        var groupField = $"groups.{group.RollGroupId}";
        if (!LootTableDomainRules.IsStableId(group.RollGroupId))
        {
            messages.Add(new ApiError(
                "invalid_loot_roll_group_id",
                "Roll group IDs must be lower snake case.",
                ValidationSeverity.Error,
                groupField));
        }
        else if (!groupIds.Add(group.RollGroupId))
        {
            messages.Add(new ApiError(
                "duplicate_loot_roll_group_id",
                $"Roll group '{group.RollGroupId}' is duplicated.",
                ValidationSeverity.Error,
                groupField));
        }

        if (!LootTableDomainRules.IsSectionKind(group.SectionKind))
        {
            messages.Add(new ApiError(
                "unsupported_loot_section_kind",
                "Section kind must be guaranteed, pre_roll, main, or tertiary.",
                ValidationSeverity.Error,
                $"{groupField}.section_kind"));
        }
        else if (!groupOrders.Add((group.SectionKind, group.Order)))
        {
            messages.Add(new ApiError(
                "duplicate_loot_roll_group_order",
                "Roll group orders must be unique within a section.",
                ValidationSeverity.Error,
                $"{groupField}.order"));
        }

        if (!LootTableDomainRules.IsRollKind(group.RollKind))
        {
            messages.Add(new ApiError(
                "unsupported_loot_roll_kind",
                "Roll kind must be guaranteed_all, weighted_one, or independent.",
                ValidationSeverity.Error,
                $"{groupField}.roll_kind"));
        }

        if (group.Order < 0 || group.RollCount <= 0)
        {
            messages.Add(new ApiError(
                "invalid_loot_roll_count",
                "Roll order must be nonnegative and roll count must be positive.",
                ValidationSeverity.Error,
                $"{groupField}.roll_count"));
        }

        ValidatePreRollShape(group, hasMain, groupField, messages);
        ValidateSectionCompatibility(group, groupField, messages);
        ValidateOutcomes(group, groupField, messages);
    }

    private static void ValidatePreRollShape(
        NormalizedLootRollGroup group,
        bool hasMain,
        string groupField,
        ICollection<ApiError> messages)
    {
        if (group.SectionKind == LootTableDomainRules.SectionPreRoll)
        {
            if (!LootTableDomainRules.IsPreRollFailureBehavior(group.PreRollFailureBehavior)
                || !LootTableDomainRules.IsPreRollSuccessSequenceBehavior(group.PreRollSuccessSequenceBehavior)
                || !LootTableDomainRules.IsPreRollSuccessMainBehavior(group.PreRollSuccessMainBehavior))
            {
                messages.Add(new ApiError(
                    "invalid_loot_preroll_shape",
                    "Pre-roll groups require failure behavior plus success sequence and main behavior.",
                    ValidationSeverity.Error,
                    groupField));
            }

            if (group.PreRollFailureBehavior == LootTableDomainRules.FailureFallthroughToMain && !hasMain)
            {
                messages.Add(new ApiError(
                    "loot_preroll_missing_main",
                    "Pre-roll fallthrough_to_main requires at least one main group.",
                    ValidationSeverity.Error,
                    $"{groupField}.pre_roll_failure_behavior"));
            }

            return;
        }

        if (group.PreRollFailureBehavior is not null
            || group.PreRollSuccessSequenceBehavior is not null
            || group.PreRollSuccessMainBehavior is not null)
        {
            messages.Add(new ApiError(
                "invalid_loot_preroll_shape",
                "Only pre_roll groups may define pre-roll behavior fields.",
                ValidationSeverity.Error,
                groupField));
        }
    }

    private static void ValidateSectionCompatibility(
        NormalizedLootRollGroup group,
        string groupField,
        ICollection<ApiError> messages)
    {
        if (group.SectionKind == LootTableDomainRules.SectionGuaranteed
            && (group.RollKind != LootTableDomainRules.RollGuaranteedAll || group.RollCount != 1))
        {
            messages.Add(new ApiError(
                "invalid_loot_guaranteed_shape",
                "Guaranteed groups must use guaranteed_all with roll count 1.",
                ValidationSeverity.Error,
                groupField));
        }

        if (group.SectionKind == LootTableDomainRules.SectionTertiary
            && group.RollKind != LootTableDomainRules.RollIndependent)
        {
            messages.Add(new ApiError(
                "invalid_loot_tertiary_shape",
                "Tertiary groups must use independent probabilities.",
                ValidationSeverity.Error,
                groupField));
        }
    }

    private static void ValidateOutcomes(
        NormalizedLootRollGroup group,
        string groupField,
        ICollection<ApiError> messages)
    {
        if (group.Outcomes.Count == 0)
        {
            messages.Add(new ApiError(
                "loot_group_outcomes_missing",
                "Roll groups must define at least one outcome.",
                ValidationSeverity.Error,
                $"{groupField}.outcomes"));
        }

        var outcomeIds = new HashSet<string>(StringComparer.Ordinal);
        var outcomeOrders = new HashSet<int>();
        var weightedTotal = 0L;
        var independentPositive = 0;
        foreach (var outcome in group.Outcomes)
        {
            var outcomeField = $"{groupField}.outcomes.{outcome.OutcomeId}";
            ValidateOutcomeIdentity(outcome, outcomeIds, outcomeOrders, outcomeField, messages);
            ValidateOutcomeShape(group, outcome, outcomeField, messages);
            if (group.RollKind == LootTableDomainRules.RollWeightedOne)
            {
                if (outcome.Weight is null or <= 0)
                {
                    messages.Add(new ApiError(
                        "invalid_loot_weight",
                        "Weighted outcomes require positive integer weights.",
                        ValidationSeverity.Error,
                        $"{outcomeField}.weight"));
                }
                else
                {
                    weightedTotal += outcome.Weight.Value;
                }

                if (outcome.ProbabilityNumerator is not null || outcome.ProbabilityDenominator is not null)
                {
                    messages.Add(new ApiError(
                        "invalid_loot_probability",
                        "Weighted outcomes must not define exact probability fields.",
                        ValidationSeverity.Error,
                        outcomeField));
                }
            }
            else if (outcome.Weight is not null)
            {
                messages.Add(new ApiError(
                    "invalid_loot_weight",
                    "Only weighted_one groups may define weights.",
                    ValidationSeverity.Error,
                    $"{outcomeField}.weight"));
            }

            if (group.RollKind == LootTableDomainRules.RollIndependent)
            {
                if (outcome.ProbabilityNumerator is null || outcome.ProbabilityDenominator is null)
                {
                    messages.Add(new ApiError(
                        "invalid_loot_probability",
                        "Independent outcomes require numerator and denominator.",
                        ValidationSeverity.Error,
                        outcomeField));
                }
                else
                {
                    if (outcome.ProbabilityDenominator <= 0
                        || outcome.ProbabilityNumerator < 0
                        || outcome.ProbabilityNumerator > outcome.ProbabilityDenominator)
                    {
                        messages.Add(new ApiError(
                            "invalid_loot_probability",
                            "Exact probability must satisfy denominator > 0 and 0 <= numerator <= denominator.",
                            ValidationSeverity.Error,
                            outcomeField));
                    }

                    if (outcome.ProbabilityNumerator > 0)
                    {
                        independentPositive++;
                    }
                }
            }
            else if (group.RollKind != LootTableDomainRules.RollWeightedOne
                     && (outcome.ProbabilityNumerator is not null || outcome.ProbabilityDenominator is not null))
            {
                messages.Add(new ApiError(
                    "invalid_loot_probability",
                    "Only independent groups may define exact probabilities.",
                    ValidationSeverity.Error,
                    outcomeField));
            }
        }

        if (group.RollKind == LootTableDomainRules.RollWeightedOne && weightedTotal <= 0)
        {
            messages.Add(new ApiError(
                "invalid_loot_weight",
                "Weighted groups require at least one selectable positive weight.",
                ValidationSeverity.Error,
                $"{groupField}.weight"));
        }

        if (group.RollKind == LootTableDomainRules.RollIndependent && independentPositive == 0)
        {
            messages.Add(new ApiError(
                "invalid_loot_probability",
                "Independent groups require at least one positive probability.",
                ValidationSeverity.Error,
                $"{groupField}.probability"));
        }
    }

    private static void ValidateOutcomeIdentity(
        NormalizedLootOutcome outcome,
        HashSet<string> outcomeIds,
        HashSet<int> outcomeOrders,
        string outcomeField,
        ICollection<ApiError> messages)
    {
        if (!LootTableDomainRules.IsStableId(outcome.OutcomeId))
        {
            messages.Add(new ApiError(
                "invalid_loot_outcome_id",
                "Outcome IDs must be lower snake case.",
                ValidationSeverity.Error,
                outcomeField));
        }
        else if (!outcomeIds.Add(outcome.OutcomeId))
        {
            messages.Add(new ApiError(
                "duplicate_loot_outcome_id",
                $"Outcome '{outcome.OutcomeId}' is duplicated.",
                ValidationSeverity.Error,
                outcomeField));
        }

        if (outcome.Order < 0 || !outcomeOrders.Add(outcome.Order))
        {
            messages.Add(new ApiError(
                outcome.Order < 0 ? "invalid_loot_outcome_order" : "duplicate_loot_outcome_order",
                "Outcome orders must be unique and nonnegative within a group.",
                ValidationSeverity.Error,
                $"{outcomeField}.order"));
        }
    }

    private static void ValidateOutcomeShape(
        NormalizedLootRollGroup group,
        NormalizedLootOutcome outcome,
        string outcomeField,
        ICollection<ApiError> messages)
    {
        if (!LootTableDomainRules.IsOutcomeKind(outcome.OutcomeKind))
        {
            messages.Add(new ApiError(
                "unsupported_loot_outcome_kind",
                "Outcome kind must be item, loot_table, or no_drop.",
                ValidationSeverity.Error,
                $"{outcomeField}.outcome_kind"));
            return;
        }

        if (outcome.OutcomeKind == LootTableDomainRules.OutcomeItem)
        {
            if (!LootTableDomainRules.IsStableId(outcome.ItemId)
                || outcome.NestedLootTableId is not null
                || outcome.MinQuantity is null
                || outcome.MaxQuantity is null)
            {
                messages.Add(new ApiError(
                    "invalid_loot_outcome_shape",
                    "Item outcomes require item_id and min/max quantity, and must not define nested table.",
                    ValidationSeverity.Error,
                    outcomeField));
            }

            if (outcome.MinQuantity is <= 0
                || outcome.MaxQuantity is <= 0
                || outcome.MinQuantity > outcome.MaxQuantity)
            {
                messages.Add(new ApiError(
                    "invalid_loot_quantity_range",
                    "Item quantity range must be positive with min <= max.",
                    ValidationSeverity.Error,
                    $"{outcomeField}.quantity"));
            }

            return;
        }

        if (outcome.OutcomeKind == LootTableDomainRules.OutcomeLootTable)
        {
            if (!LootTableDomainRules.IsStableId(outcome.NestedLootTableId)
                || outcome.ItemId is not null
                || outcome.MinQuantity is not null
                || outcome.MaxQuantity is not null)
            {
                messages.Add(new ApiError(
                    "invalid_loot_outcome_shape",
                    "Nested-table outcomes require nested_loot_table_id and must not define item or quantity fields.",
                    ValidationSeverity.Error,
                    outcomeField));
            }

            return;
        }

        if (outcome.ItemId is not null
            || outcome.NestedLootTableId is not null
            || outcome.MinQuantity is not null
            || outcome.MaxQuantity is not null
            || group.RollKind == LootTableDomainRules.RollGuaranteedAll)
        {
            messages.Add(new ApiError(
                "invalid_loot_outcome_shape",
                "No-drop outcomes produce nothing and cannot appear in guaranteed_all groups.",
                ValidationSeverity.Error,
                outcomeField));
        }
    }

    private static void ValidateReferences(
        string lootTableId,
        NormalizedLootTableDraft draft,
        LootTableRecord? existing,
        IReadOnlyList<LootTableRecord> allTables,
        IReadOnlyList<LootItemRecord> items,
        IReadOnlyList<LootMobBindingRecord> mobBindings,
        string operation,
        ICollection<ApiError> messages)
    {
        var publicationState = operation == "publish"
            ? LootTableDomainRules.Published
            : existing?.PublicationState ?? LootTableDomainRules.Draft;
        var tableRecords = allTables
            .Where(table => table.LootTableId != lootTableId)
            .Append(ToCandidateRecord(lootTableId, draft, publicationState, existing))
            .ToDictionary(table => table.LootTableId, StringComparer.Ordinal);
        var itemLookup = items.ToDictionary(item => item.ItemId, StringComparer.Ordinal);

        foreach (var group in draft.Groups)
        {
            foreach (var outcome in group.Outcomes)
            {
                if (outcome.OutcomeKind == LootTableDomainRules.OutcomeItem)
                {
                    if (outcome.ItemId is null || !itemLookup.TryGetValue(outcome.ItemId, out var item))
                    {
                        messages.Add(new ApiError(
                            "loot_item_missing",
                            $"Item '{outcome.ItemId}' does not exist.",
                            ValidationSeverity.Error,
                            $"groups.{group.RollGroupId}.outcomes.{outcome.OutcomeId}.item_id"));
                    }
                    else if (publicationState == LootTableDomainRules.Published && !item.RuntimeEnabled)
                    {
                        messages.Add(new ApiError(
                            "loot_item_not_runtime_enabled",
                            $"Published loot tables may only reference runtime-enabled items. '{item.ItemId}' is not published.",
                            ValidationSeverity.Error,
                            $"groups.{group.RollGroupId}.outcomes.{outcome.OutcomeId}.item_id"));
                    }
                }

                if (outcome.OutcomeKind == LootTableDomainRules.OutcomeLootTable)
                {
                    if (outcome.NestedLootTableId is null || !tableRecords.TryGetValue(outcome.NestedLootTableId, out var nested))
                    {
                        messages.Add(new ApiError(
                            "nested_loot_table_missing",
                            $"Nested loot table '{outcome.NestedLootTableId}' does not exist.",
                            ValidationSeverity.Error,
                            $"groups.{group.RollGroupId}.outcomes.{outcome.OutcomeId}.nested_loot_table_id"));
                    }
                    else if (outcome.NestedLootTableId == lootTableId)
                    {
                        messages.Add(new ApiError(
                            "loot_table_direct_cycle",
                            "Loot tables cannot reference themselves directly.",
                            ValidationSeverity.Error,
                            $"groups.{group.RollGroupId}.outcomes.{outcome.OutcomeId}.nested_loot_table_id"));
                    }
                    else if (publicationState == LootTableDomainRules.Published
                             && nested.PublicationState != LootTableDomainRules.Published)
                    {
                        messages.Add(new ApiError(
                            "nested_loot_table_not_published",
                            $"Published loot tables may only reference Published nested tables. '{nested.LootTableId}' is {nested.PublicationState}.",
                            ValidationSeverity.Error,
                            $"groups.{group.RollGroupId}.outcomes.{outcome.OutcomeId}.nested_loot_table_id"));
                    }
                }
            }
        }

        ValidateGraph(lootTableId, tableRecords, messages);

        if (publicationState == LootTableDomainRules.Published)
        {
            foreach (var binding in mobBindings.Where(binding => binding.RootLootTableId == lootTableId))
            {
                if (binding.PublicationState == LootTableDomainRules.Published && binding.LegacyGuaranteedDropCount > 0)
                {
                    messages.Add(new ApiError(
                        "published_mob_legacy_and_root_conflict",
                        $"Published mob '{binding.MobDefinitionId}' cannot use both legacy guaranteed drops and root loot table '{lootTableId}'.",
                        ValidationSeverity.Error,
                        "loot_table_id"));
                }
            }
        }
    }

    private static void ValidateGraph(
        string rootLootTableId,
        IReadOnlyDictionary<string, LootTableRecord> tables,
        ICollection<ApiError> messages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ValidateGraphFrom(rootLootTableId, rootLootTableId, 1, tables, [], seen, messages);
        var expansion = EstimateExpansion(rootLootTableId, tables, []);
        if (expansion > LootTableDomainRules.MaxBoundedExpansion)
        {
            messages.Add(new ApiError(
                "loot_table_expansion_limit_exceeded",
                $"Loot table bounded expansion must not exceed {LootTableDomainRules.MaxBoundedExpansion}.",
                ValidationSeverity.Error,
                "groups"));
        }
    }

    private static void ValidateGraphFrom(
        string rootLootTableId,
        string currentLootTableId,
        int depth,
        IReadOnlyDictionary<string, LootTableRecord> tables,
        List<string> path,
        HashSet<string> seenOnPath,
        ICollection<ApiError> messages)
    {
        if (depth > LootTableDomainRules.MaxNestingDepth)
        {
            messages.Add(new ApiError(
                "loot_table_depth_limit_exceeded",
                $"Loot table nesting cannot exceed depth {LootTableDomainRules.MaxNestingDepth}.",
                ValidationSeverity.Error,
                "groups"));
            return;
        }

        if (!seenOnPath.Add(currentLootTableId))
        {
            messages.Add(new ApiError(
                "loot_table_indirect_cycle",
                $"Loot table graph contains a cycle: {string.Join(" -> ", path.Append(currentLootTableId))}.",
                ValidationSeverity.Error,
                "groups"));
            return;
        }

        if (!tables.TryGetValue(currentLootTableId, out var table))
        {
            seenOnPath.Remove(currentLootTableId);
            return;
        }

        foreach (var nestedId in NestedTableIds(table))
        {
            if (nestedId == rootLootTableId && currentLootTableId != rootLootTableId)
            {
                messages.Add(new ApiError(
                    "loot_table_indirect_cycle",
                    $"Loot table graph cycles back to '{rootLootTableId}'.",
                    ValidationSeverity.Error,
                    "groups"));
                continue;
            }

            ValidateGraphFrom(rootLootTableId, nestedId, depth + 1, tables, [.. path, currentLootTableId], seenOnPath, messages);
        }

        seenOnPath.Remove(currentLootTableId);
    }

    private static int EstimateExpansion(
        string lootTableId,
        IReadOnlyDictionary<string, LootTableRecord> tables,
        HashSet<string> seen)
    {
        if (!seen.Add(lootTableId) || !tables.TryGetValue(lootTableId, out var table))
        {
            return 0;
        }

        var total = 1;
        foreach (var group in table.Groups)
        {
            total += Math.Max(1, group.RollCount) * Math.Max(1, group.Outcomes.Count);
            foreach (var nestedId in NestedTableIds(group))
            {
                total += EstimateExpansion(nestedId, tables, new HashSet<string>(seen, StringComparer.Ordinal));
            }
        }

        return total;
    }

    private static IEnumerable<string> NestedTableIds(LootTableRecord table) =>
        table.Groups.SelectMany(NestedTableIds);

    private static IEnumerable<string> NestedTableIds(LootRollGroupRecord group) =>
        group.Outcomes
            .Where(outcome => outcome.OutcomeKind == LootTableDomainRules.OutcomeLootTable &&
                              !string.IsNullOrWhiteSpace(outcome.NestedLootTableId))
            .Select(outcome => outcome.NestedLootTableId!);

    private static LootTableRecord ToCandidateRecord(
        string lootTableId,
        NormalizedLootTableDraft draft,
        string publicationState,
        LootTableRecord? existing) =>
        new(
            lootTableId,
            draft.DisplayName,
            draft.Description,
            publicationState,
            existing?.ContentFingerprint,
            draft.Groups.Select(group => new LootRollGroupRecord(
                group.RollGroupId,
                group.Order,
                group.SectionKind,
                group.RollKind,
                group.RollCount,
                group.PreRollFailureBehavior,
                group.PreRollSuccessSequenceBehavior,
                group.PreRollSuccessMainBehavior,
                group.DisplayName,
                group.Outcomes.Select(outcome => new LootOutcomeRecord(
                    outcome.OutcomeId,
                    outcome.Order,
                    outcome.OutcomeKind,
                    outcome.ItemId,
                    null,
                    outcome.NestedLootTableId,
                    outcome.MinQuantity,
                    outcome.MaxQuantity,
                    outcome.Weight,
                    outcome.ProbabilityNumerator,
                    outcome.ProbabilityDenominator)).ToArray())).ToArray(),
            draft.Groups.Count,
            draft.Groups.Sum(group => group.Outcomes.Count),
            existing?.UpdatedAtUtc ?? DateTimeOffset.UnixEpoch);
}

public sealed record LootTableValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages);
