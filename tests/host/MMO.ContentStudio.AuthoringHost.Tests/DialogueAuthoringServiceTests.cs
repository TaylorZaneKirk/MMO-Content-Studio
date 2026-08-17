using Microsoft.Extensions.Logging.Abstractions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class DialogueAuthoringServiceTests
{
    private const string DialogueId = "test_npc_greeting";

    [Fact]
    public async Task OptionsExposeLockedD4Capabilities()
    {
        var service = CreateService(new InMemoryDialogueRepository());

        var result = await service.LoadOptionsAsync(TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        Assert.Contains(result.Value!.NodeTypes, option => option.Id == "speaker_text");
        Assert.Equal(["quest_status", "quest_step", "has_item"], result.Value.ConditionTypes.Select(option => option.Id));
        Assert.Empty(result.Value.EffectTypes);
        Assert.True(result.Value.Capabilities.SupportsRuntimeDialogueCatalog);
        Assert.True(result.Value.Capabilities.SupportsConditions);
        Assert.False(result.Value.Capabilities.SupportsEffects);
        Assert.True(result.Value.Capabilities.SupportsQuestConditions);
        Assert.False(result.Value.Capabilities.SupportsQuestEffects);
        Assert.False(result.Value.Capabilities.SupportsHotReload);
        Assert.NotNull(result.Value.QuestReferences);
        Assert.NotNull(result.Value.ItemReferences);
    }

    [Fact]
    public async Task OptionsExposeQuestStepAndItemConditionSelectors()
    {
        var repository = new InMemoryDialogueRepository();
        repository.QuestConditionOptions.Add(new DialogueQuestConditionOption(
            "starter_quest",
            "Starter Quest",
            [new AuthoringOption("talk_to_npc", "Talk to NPC")]));
        repository.ItemConditionOptions.Add(new AuthoringOption("replacement_ingredient", "Replacement Ingredient"));
        var service = CreateService(repository);

        var result = await service.LoadOptionsAsync(TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        var quest = Assert.Single(result.Value!.QuestReferences!);
        Assert.Equal("starter_quest", quest.QuestId);
        Assert.Equal("talk_to_npc", Assert.Single(quest.Steps).Id);
        Assert.Equal("replacement_ingredient", Assert.Single(result.Value.ItemReferences!).Id);
    }

    [Fact]
    public async Task CatalogFilteringOrderingAndCompleteLoadWork()
    {
        var repository = new InMemoryDialogueRepository();
        repository.Put(Record("z_dialogue", "Zulu", "Draft"));
        repository.Put(Record("a_dialogue", "Alpha", "Published"));
        var service = CreateService(repository);

        var list = await service.ListAsync("dialogue", TestContext.Current.CancellationToken);
        var loaded = await service.LoadAsync("a_dialogue", TestContext.Current.CancellationToken);

        AssertSucceeded(list);
        Assert.Equal(["a_dialogue", "z_dialogue"], list.Value!.Items.Select(item => item.DialogueDefinitionId));
        AssertSucceeded(loaded);
        Assert.Equal("default", loaded.Value!.EntryPoints[0].EntryId);
        Assert.Equal("goodbye", loaded.Value.Nodes.Single(node => node.NodeId == "choice").Choices[0].ChoiceId);
    }

    [Fact]
    public async Task NewSaveExistingSaveAndChildOnlyEditAdvanceRootTimestamp()
    {
        var repository = new InMemoryDialogueRepository();
        var service = CreateService(repository);

        var preview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(), "save_draft"),
            TestContext.Current.CancellationToken);
        var created = await service.SaveDraftAsync(
            DialogueId,
            ToMutationRequest(DialogueTestData.ValidDraft(), preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);
        var createdAt = created.Value!.Dialogue.UpdatedAtUtc;

        var editedDraft = DialogueTestData.ValidDraft(createdAt) with
        {
            Nodes =
            [
                DialogueTestData.Speaker("start", "Edited text.", "choice"),
                DialogueTestData.ValidDraft().Nodes[1],
                DialogueTestData.ValidDraft().Nodes[2]
            ]
        };
        var previewUpdate = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(editedDraft, "save_draft"),
            TestContext.Current.CancellationToken);
        var updated = await service.SaveDraftAsync(
            DialogueId,
            ToMutationRequest(editedDraft, previewUpdate.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        AssertSucceeded(created);
        AssertSucceeded(updated);
        Assert.Equal("Edited text.", updated.Value!.Dialogue.Nodes[0].Text);
        Assert.True(updated.Value.Dialogue.UpdatedAtUtc > createdAt);
    }

    [Fact]
    public async Task StaleSaveAndSignatureMismatchFail()
    {
        var repository = new InMemoryDialogueRepository();
        repository.Put(Record(DialogueId, "Test NPC Greeting", "Draft"));
        var service = CreateService(repository);
        var expected = repository.Records[DialogueId].UpdatedAtUtc;
        var stale = expected.AddMinutes(-1);

        var staleResult = await service.SaveDraftAsync(
            DialogueId,
            ToMutationRequest(DialogueTestData.ValidDraft(stale), "wrong"),
            TestContext.Current.CancellationToken);
        var preview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected), "save_draft"),
            TestContext.Current.CancellationToken);
        var mismatch = await service.SaveDraftAsync(
            DialogueId,
            ToMutationRequest(DialogueTestData.ValidDraft(expected) with { DisplayName = "Edited" }, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(staleResult.Succeeded);
        Assert.Contains(staleResult.Errors, error => error.Code == "dialogue_preview_mismatch");
        Assert.False(mismatch.Succeeded);
        Assert.Contains(mismatch.Errors, error => error.Code == "dialogue_preview_mismatch");
    }

    [Fact]
    public async Task PublishUsesSavedGraphAndRejectsUnsavedChanges()
    {
        var repository = new InMemoryDialogueRepository();
        repository.Put(Record(DialogueId, "Test NPC Greeting", "Draft"));
        var service = CreateService(repository);
        var expected = repository.Records[DialogueId].UpdatedAtUtc;

        var unsavedPreview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected) with { DisplayName = "Unsaved" }, "publish"),
            TestContext.Current.CancellationToken);
        var preview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected), "publish"),
            TestContext.Current.CancellationToken);
        var publish = await service.PublishAsync(
            DialogueId,
            new DialogueLifecycleRequest(expected, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        AssertSucceeded(unsavedPreview);
        Assert.Contains(unsavedPreview.Value!.Messages, message => message.Code == "dialogue_unsaved_changes");
        AssertSucceeded(publish);
        Assert.Equal("Published", publish.Value!.Dialogue.PublicationState);
    }

    [Fact]
    public async Task PublishCallsRuntimeCatalogExporterAndSaveDraftDoesNot()
    {
        var repository = new InMemoryDialogueRepository();
        repository.Put(Record(DialogueId, "Test NPC Greeting", "Draft"));
        var catalogPublisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, catalogPublisher);
        var expected = repository.Records[DialogueId].UpdatedAtUtc;

        var savePreview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected), "save_draft"),
            TestContext.Current.CancellationToken);
        AssertSucceeded(savePreview);
        await service.SaveDraftAsync(
            DialogueId,
            ToMutationRequest(DialogueTestData.ValidDraft(expected), savePreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);
        Assert.Empty(catalogPublisher.PublishScopes);

        expected = repository.Records[DialogueId].UpdatedAtUtc;

        var publishPreview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected), "publish"),
            TestContext.Current.CancellationToken);
        AssertSucceeded(publishPreview);
        var publish = await service.PublishAsync(
            DialogueId,
            new DialogueLifecycleRequest(expected, publishPreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        AssertSucceeded(publish);
        Assert.Equal([RuntimeCatalogPublicationScope.Dialogue], catalogPublisher.PublishScopes);
        Assert.DoesNotContain(publish.Value!.Messages, message => message.Code == "map_catalog_publish_warning");
    }

    [Fact]
    public async Task PublishBlocksInvalidTypedConditionReferences()
    {
        var repository = new InMemoryDialogueRepository();
        var draft = DialogueTestData.ValidDraft() with
        {
            EntryPoints =
            [
                new DialogueEntryPoint(
                    "default",
                    "start",
                    0,
                    0,
                    [new DialogueCondition("quest_status", "meal", "active", null, null, null)])
            ],
            Nodes =
            [
                DialogueTestData.Speaker("start", "Welcome.", "choice"),
                new("choice", "player_choice", null, "Choose.", null, true, 100, 0, null,
                [
                    new("step", "Step.", "end", 0, [new DialogueCondition("quest_step", "meal", null, "missing_step", null, null)]),
                    new("item", "Item.", "end", 1, [new DialogueCondition("has_item", null, null, null, "replacement_ingredient", 1)])
                ]),
                DialogueTestData.End("end")
            ]
        };
        var now = DateTimeOffset.UtcNow;
        repository.Put(new DialogueDefinitionRecord(
            DialogueId,
            draft.DisplayName,
            "Draft",
            draft.SchemaVersion,
            draft.EntryPoints,
            draft.Nodes,
            draft.MetadataDescription,
            draft.Notes,
            now,
            now,
            draft.EntryPoints.Count,
            draft.Nodes.Count,
            draft.Nodes.Sum(node => node.Choices.Count)));
        repository.QuestReferences["meal"] = new DialogueQuestReferenceRecord("meal", "Draft", ["return_to_inn"]);
        repository.ItemReferences["replacement_ingredient"] = new DialogueItemReferenceRecord("replacement_ingredient", "Replacement Ingredient", false);
        var service = CreateService(repository);
        var expected = repository.Records[DialogueId].UpdatedAtUtc;

        var preview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(draft with { ExpectedUpdatedAtUtc = expected }, "publish"),
            TestContext.Current.CancellationToken);

        AssertSucceeded(preview);
        Assert.Contains(preview.Value!.Messages, message => message.Code == "dialogue_condition_unpublished_quest");
        Assert.Contains(preview.Value.Messages, message => message.Code == "dialogue_condition_missing_quest_step");
        Assert.Contains(preview.Value.Messages, message => message.Code == "dialogue_condition_runtime_disabled_item");
        Assert.False(preview.Value.ValidForPublication);
    }

    [Fact]
    public async Task DisableAndDeleteReferencePolicyIsEnforced()
    {
        var repository = new InMemoryDialogueRepository();
        repository.Put(Record(DialogueId, "Test NPC Greeting", "Published"));
        repository.ReferenceSummaries[DialogueId] = new DialogueReferenceSummaryRecord(
            DialogueId,
            1,
            1,
            ["npc:test_npc:Published"],
            true);
        var service = CreateService(repository);
        var expected = repository.Records[DialogueId].UpdatedAtUtc;

        var disablePreview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected), "disable"),
            TestContext.Current.CancellationToken);
        var disable = await service.DisableAsync(
            DialogueId,
            new DialogueLifecycleRequest(expected, disablePreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        repository.Put(repository.Records[DialogueId] with { PublicationState = "Disabled" });
        expected = repository.Records[DialogueId].UpdatedAtUtc;
        repository.ReferenceSummaries[DialogueId] = repository.ReferenceSummaries[DialogueId] with
        {
            PublishedReferenceCount = 0,
            ReferenceSources = ["npc:test_npc:Draft"]
        };
        var deletePreview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected), "delete"),
            TestContext.Current.CancellationToken);
        var delete = await service.DeleteAsync(
            DialogueId,
            new DialogueDeleteRequest(expected, deletePreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(disable.Succeeded);
        Assert.Contains(disable.Errors, error => error.Code == "dialogue_disable_blocked_by_reference");
        Assert.False(delete.Succeeded);
        Assert.Contains(delete.Errors, error => error.Code == "dialogue_delete_blocked_by_reference");
    }

    [Fact]
    public async Task DeleteRequiresDisabledAndThenRemovesAggregate()
    {
        var repository = new InMemoryDialogueRepository();
        repository.Put(Record(DialogueId, "Test NPC Greeting", "Draft"));
        var service = CreateService(repository);
        var expected = repository.Records[DialogueId].UpdatedAtUtc;

        var draftPreview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected), "delete"),
            TestContext.Current.CancellationToken);
        var draftDelete = await service.DeleteAsync(
            DialogueId,
            new DialogueDeleteRequest(expected, draftPreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        repository.Put(repository.Records[DialogueId] with { PublicationState = "Disabled" });
        expected = repository.Records[DialogueId].UpdatedAtUtc;
        var deletePreview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected), "delete"),
            TestContext.Current.CancellationToken);
        var delete = await service.DeleteAsync(
            DialogueId,
            new DialogueDeleteRequest(expected, deletePreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(draftDelete.Succeeded);
        Assert.Contains(draftDelete.Errors, error => error.Code == "dialogue_delete_requires_disabled");
        AssertSucceeded(delete);
        Assert.False(repository.Records.ContainsKey(DialogueId));
    }

    [Fact]
    public async Task PreviewReportsExactLogicalChanges()
    {
        var repository = new InMemoryDialogueRepository();
        repository.Put(Record(DialogueId, "Test NPC Greeting", "Draft"));
        var service = CreateService(repository);
        var expected = repository.Records[DialogueId].UpdatedAtUtc;

        var preview = await service.PreviewAsync(
            DialogueId,
            ToPreviewRequest(DialogueTestData.ValidDraft(expected) with
            {
                Nodes =
                [
                    DialogueTestData.Speaker("start", "Changed text.", "choice"),
                    DialogueTestData.ValidDraft().Nodes[1],
                    DialogueTestData.ValidDraft().Nodes[2]
                ]
            }, "save_draft"),
            TestContext.Current.CancellationToken);

        AssertSucceeded(preview);
        Assert.Contains(preview.Value!.Changes, change => change.Field == "nodes.start.text");
    }

    private static DialogueAuthoringService CreateService(
        IDialogueRepository repository,
        IRuntimeCatalogPublisher? runtimeCatalogPublisher = null)
    {
        var analyzer = new DialogueGraphAnalyzer();
        return new DialogueAuthoringService(
            repository,
            new DialogueDefinitionValidator(analyzer),
            new DialogueAuthoringRegistry(),
            analyzer,
            new DialoguePlaythroughService(),
            NullLogger<DialogueAuthoringService>.Instance,
            runtimeCatalogPublisher);
    }

    private sealed class TestRuntimeCatalogPublisher : IRuntimeCatalogPublisher
    {
        public List<RuntimeCatalogPublicationScope> PublishScopes { get; } = [];

        public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(
            RuntimeCatalogPublicationScope scope,
            CancellationToken cancellationToken)
        {
            PublishScopes.Add(scope);
            return Task.FromResult<IReadOnlyList<ApiError>>([]);
        }
    }

    private static PreviewDialogueRequest ToPreviewRequest(DialogueDraft draft, string operation) =>
        new(
            draft.DisplayName,
            draft.SchemaVersion,
            draft.EntryPoints,
            draft.Nodes,
            draft.MetadataDescription,
            draft.Notes,
            draft.ExpectedUpdatedAtUtc,
            operation);

    private static DialogueMutationRequest ToMutationRequest(DialogueDraft draft, string signature) =>
        new(
            draft.DisplayName,
            draft.SchemaVersion,
            draft.EntryPoints,
            draft.Nodes,
            draft.MetadataDescription,
            draft.Notes,
            draft.ExpectedUpdatedAtUtc,
            signature);

    private static DialogueDefinitionRecord Record(string id, string displayName, string publicationState)
    {
        var now = DateTimeOffset.UtcNow;
        var draft = DialogueTestData.ValidDraft(now);
        return new DialogueDefinitionRecord(
            id,
            displayName,
            publicationState,
            draft.SchemaVersion,
            draft.EntryPoints,
            draft.Nodes,
            draft.MetadataDescription,
            draft.Notes,
            now,
            now,
            draft.EntryPoints.Count,
            draft.Nodes.Count,
            draft.Nodes.Sum(node => node.Choices.Count));
    }

    private static void AssertSucceeded<T>(AuthoringOperationResult<T> result)
    {
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.NotNull(result.Value);
    }

    private sealed class InMemoryDialogueRepository : IDialogueRepository
    {
        public Dictionary<string, DialogueDefinitionRecord> Records { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, DialogueReferenceSummaryRecord> ReferenceSummaries { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, DialogueQuestReferenceRecord> QuestReferences { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, DialogueItemReferenceRecord> ItemReferences { get; } = new(StringComparer.Ordinal);

        public List<DialogueQuestConditionOption> QuestConditionOptions { get; } = [];

        public List<AuthoringOption> ItemConditionOptions { get; } = [];

        public Task<IReadOnlyList<DialogueDefinitionRecord>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default)
        {
            var filtered = Records.Values
                .Where(record => string.IsNullOrWhiteSpace(search)
                    || record.DialogueDefinitionId.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || record.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => record.DisplayName, StringComparer.Ordinal)
                .ThenBy(record => record.DialogueDefinitionId, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult<IReadOnlyList<DialogueDefinitionRecord>>(filtered);
        }

        public Task<DialogueDefinitionRecord?> LoadAsync(
            string dialogueDefinitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Records.GetValueOrDefault(dialogueDefinitionId));

        public Task<DialogueDefinitionRecord?> LoadForUpdateAsync(
            string dialogueDefinitionId,
            CancellationToken cancellationToken = default) =>
            LoadAsync(dialogueDefinitionId, cancellationToken);

        public Task<DialogueDefinitionRecord> InsertDraftAsync(
            string dialogueDefinitionId,
            DialogueDraft draft,
            CancellationToken cancellationToken = default)
        {
            if (Records.ContainsKey(dialogueDefinitionId))
            {
                throw new DialogueDefinitionDuplicateException(dialogueDefinitionId);
            }

            var record = CreateRecord(dialogueDefinitionId, draft, "Draft", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            Records[dialogueDefinitionId] = record;
            return Task.FromResult(record);
        }

        public Task<DialogueDefinitionRecord> ReplaceDraftAsync(
            string dialogueDefinitionId,
            DialogueDraft draft,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var existing = Records.GetValueOrDefault(dialogueDefinitionId);
            EnsureExpectedVersion(existing, expectedUpdatedAtUtc, dialogueDefinitionId);
            if (existing is null)
            {
                return InsertDraftAsync(dialogueDefinitionId, draft, cancellationToken);
            }

            var updated = CreateRecord(
                dialogueDefinitionId,
                draft,
                "Draft",
                existing.CreatedAtUtc,
                existing.UpdatedAtUtc.AddTicks(1));
            Records[dialogueDefinitionId] = updated;
            return Task.FromResult(updated);
        }

        public Task<DialogueDefinitionRecord> SetPublicationAsync(
            string dialogueDefinitionId,
            string publicationState,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var existing = Records.GetValueOrDefault(dialogueDefinitionId)
                ?? throw new DialogueDefinitionNotFoundException(dialogueDefinitionId);
            EnsureExpectedVersion(existing, expectedUpdatedAtUtc, dialogueDefinitionId);
            var updated = existing with
            {
                PublicationState = publicationState,
                UpdatedAtUtc = existing.UpdatedAtUtc.AddTicks(1)
            };
            Records[dialogueDefinitionId] = updated;
            return Task.FromResult(updated);
        }

        public Task DeleteAsync(
            string dialogueDefinitionId,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            var existing = Records.GetValueOrDefault(dialogueDefinitionId)
                ?? throw new DialogueDefinitionNotFoundException(dialogueDefinitionId);
            EnsureExpectedVersion(existing, expectedUpdatedAtUtc, dialogueDefinitionId);
            if (existing.PublicationState != "Disabled")
            {
                throw new DialogueDefinitionDeleteRequiresDisabledException(dialogueDefinitionId);
            }

            Records.Remove(dialogueDefinitionId);
            return Task.CompletedTask;
        }

        public Task<DialogueReferenceSummaryRecord> LoadNpcReferencesAsync(
            string dialogueDefinitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReferenceSummaries.GetValueOrDefault(dialogueDefinitionId)
                ?? new DialogueReferenceSummaryRecord(dialogueDefinitionId, 0, 0, [], true));

        public Task<IReadOnlyDictionary<string, DialogueQuestReferenceRecord>> LoadQuestReferencesAsync(
            IReadOnlyCollection<string> questIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DialogueQuestReferenceRecord>>(QuestReferences
                .Where(pair => questIds.Contains(pair.Key, StringComparer.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        public Task<IReadOnlyDictionary<string, DialogueItemReferenceRecord>> LoadItemReferencesAsync(
            IReadOnlyCollection<string> itemIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DialogueItemReferenceRecord>>(ItemReferences
                .Where(pair => itemIds.Contains(pair.Key, StringComparer.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        public Task<IReadOnlyList<DialogueQuestConditionOption>> LoadPublishedQuestConditionOptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DialogueQuestConditionOption>>(QuestConditionOptions);

        public Task<IReadOnlyList<AuthoringOption>> LoadRuntimeItemConditionOptionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthoringOption>>(ItemConditionOptions);

        public void Put(DialogueDefinitionRecord record)
        {
            Records[record.DialogueDefinitionId] = record with
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        private static DialogueDefinitionRecord CreateRecord(
            string id,
            DialogueDraft draft,
            string state,
            DateTimeOffset createdAt,
            DateTimeOffset updatedAt)
        {
            var normalized = DialogueDomainRules.NormalizeDraft(draft);
            return new DialogueDefinitionRecord(
                id,
                normalized.DisplayName,
                state,
                normalized.SchemaVersion,
                normalized.EntryPoints,
                normalized.Nodes,
                normalized.MetadataDescription,
                normalized.Notes,
                createdAt,
                updatedAt,
                normalized.EntryPoints.Count,
                normalized.Nodes.Count,
                normalized.Nodes.Sum(node => node.Choices.Count));
        }

        private static void EnsureExpectedVersion(
            DialogueDefinitionRecord? existing,
            DateTimeOffset? expectedUpdatedAtUtc,
            string id)
        {
            if (existing is null)
            {
                if (expectedUpdatedAtUtc is not null)
                {
                    throw new DialogueDefinitionConcurrencyException(id, null);
                }

                return;
            }

            if (expectedUpdatedAtUtc is null
                || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
            {
                throw new DialogueDefinitionConcurrencyException(id, existing.UpdatedAtUtc);
            }
        }
    }
}
