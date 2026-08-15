using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class LootTableAuthoringServiceTests
{
    [Fact]
    public void ExpectedValueHonorsPreRollSuccessMainSuppression()
    {
        var table = new LootTableRecord(
            "slime_root",
            "Slime Root",
            "",
            LootTableDomainRules.Published,
            null,
            [
                new LootRollGroupRecord(
                    "rare_gate",
                    0,
                    LootTableDomainRules.SectionPreRoll,
                    LootTableDomainRules.RollIndependent,
                    1,
                    LootTableDomainRules.FailureContinue,
                    LootTableDomainRules.SuccessSequenceStop,
                    LootTableDomainRules.SuccessMainSuppress,
                    null,
                    [
                        ItemOutcome(
                            "rare_shard",
                            0,
                            "rare_shard",
                            minQuantity: 1,
                            maxQuantity: 1,
                            probabilityNumerator: 1,
                            probabilityDenominator: 2)
                    ]),
                new LootRollGroupRecord(
                    "main",
                    0,
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollGuaranteedAll,
                    1,
                    null,
                    null,
                    null,
                    null,
                    [
                        ItemOutcome("coin", 0, "coin", minQuantity: 1, maxQuantity: 1)
                    ])
            ],
            2,
            2,
            DateTimeOffset.Parse("2026-08-15T12:00:00Z"));
        var items = new[]
        {
            new LootItemRecord("coin", "Coin", true, 100),
            new LootItemRecord("rare_shard", "Rare Shard", true, 20)
        };

        var report = new LootTableExpectedValueCalculator().Calculate("slime_root", [table], items);

        Assert.True(report.Valid);
        Assert.Equal("60", report.TotalReferenceValue.Display);
        Assert.Equal("1/2", report.ItemTotals.Single(item => item.ItemId == "coin").ExpectedQuantity.Display);
        Assert.Equal("1/2", report.ItemTotals.Single(item => item.ItemId == "rare_shard").ExpectedQuantity.Display);
    }

    [Fact]
    public async Task ValidatorRejectsPreRollBehaviorFieldsOutsidePreRollGroups()
    {
        var repository = new InMemoryLootTableRepository();
        repository.Items["coin"] = new LootItemRecord("coin", "Coin", true, 1);
        var validator = new LootTableDefinitionValidator(repository);
        var draft = LootTableDomainRules.Normalize(
            "Bad Table",
            "",
            [
                new LootRollGroupDraft(
                    "main",
                    0,
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollGuaranteedAll,
                    1,
                    null,
                    LootTableDomainRules.SuccessSequenceContinue,
                    null,
                    null,
                    [
                        new LootOutcomeDraft(
                            "coin",
                            0,
                            LootTableDomainRules.OutcomeItem,
                            "coin",
                            null,
                            1,
                            1,
                            null,
                            null,
                            null)
                    ])
            ]);

        var result = await validator.ValidateAsync(
            "bad_table",
            draft,
            null,
            "save_draft",
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Messages, message => message.Code == "invalid_loot_preroll_shape");
        Assert.False(result.ValidForDraft);
    }

    [Fact]
    public async Task PublishRequiresReferencedNestedLootTablesToBePublished()
    {
        var repository = new InMemoryLootTableRepository();
        repository.Items["coin"] = new LootItemRecord("coin", "Coin", true, 1);
        repository.Tables["child"] = new LootTableRecord(
            "child",
            "Child",
            "",
            LootTableDomainRules.Draft,
            null,
            [],
            0,
            0,
            DateTimeOffset.Parse("2026-08-15T12:00:00Z"));
        var validator = new LootTableDefinitionValidator(repository);
        var draft = LootTableDomainRules.Normalize(
            "Parent",
            "",
            [
                new LootRollGroupDraft(
                    "main",
                    0,
                    LootTableDomainRules.SectionMain,
                    LootTableDomainRules.RollGuaranteedAll,
                    1,
                    null,
                    null,
                    null,
                    null,
                    [
                        new LootOutcomeDraft(
                            "child",
                            0,
                            LootTableDomainRules.OutcomeLootTable,
                            null,
                            "child",
                            null,
                            null,
                            null,
                            null,
                            null)
                    ])
            ]);

        var result = await validator.ValidateAsync(
            "parent",
            draft,
            null,
            "publish",
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Messages, message => message.Code == "nested_loot_table_not_published");
        Assert.False(result.ValidForPublication);
    }

    private static LootOutcomeRecord ItemOutcome(
        string outcomeId,
        int order,
        string itemId,
        int minQuantity,
        int maxQuantity,
        long? probabilityNumerator = null,
        long? probabilityDenominator = null) =>
        new(
            outcomeId,
            order,
            LootTableDomainRules.OutcomeItem,
            itemId,
            itemId,
            null,
            minQuantity,
            maxQuantity,
            null,
            probabilityNumerator,
            probabilityDenominator);

    private sealed class InMemoryLootTableRepository : ILootTableRepository
    {
        public Dictionary<string, LootTableRecord> Tables { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, LootItemRecord> Items { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, LootMobBindingRecord> MobBindings { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<LootTableRecord>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LootTableRecord>>(Tables.Values.ToArray());

        public Task<LootTableRecord?> LoadAsync(
            string lootTableId,
            CancellationToken cancellationToken = default)
        {
            Tables.TryGetValue(lootTableId, out var record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<LootItemRecord>> LoadItemsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LootItemRecord>>(Items.Values.ToArray());

        public Task<IReadOnlyList<LootTableOptionRecord>> LoadTableOptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LootTableOptionRecord>>(
                Tables.Values
                    .Select(table => new LootTableOptionRecord(
                        table.LootTableId,
                        table.DisplayName,
                        table.PublicationState))
                    .ToArray());

        public Task<IReadOnlyList<LootMobBindingRecord>> LoadMobBindingsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LootMobBindingRecord>>(MobBindings.Values.ToArray());

        public Task<bool> HasPublishedDependentsAsync(
            string lootTableId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<LootTableRecord> SaveDraftAsync(
            string lootTableId,
            NormalizedLootTableDraft draft,
            string contentFingerprint,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<LootTableRecord> SetPublicationAsync(
            string lootTableId,
            string publicationState,
            string contentFingerprint,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteAsync(
            string lootTableId,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
