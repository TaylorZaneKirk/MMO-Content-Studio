using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class QuestAuthoringServiceTests
{
    [Fact]
    public async Task DisableUnreferencedQuestRegeneratesRuntimeCatalog()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Published", Draft());
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, publisher);

        var preview = await PreviewAsync(service, record, "disable", TestContext.Current.CancellationToken);
        var result = await service.DisableAsync(
            record.QuestId,
            new QuestLifecycleRequest(record.UpdatedAtUtc, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("Disabled", result.Value!.Quest.PublicationState);
        Assert.Equal([RuntimeCatalogPublicationScope.Quest], publisher.Scopes);
    }

    [Fact]
    public async Task SaveDraftUnreferencedExistingQuestSucceeds()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Published", Draft());
        var service = CreateService(repository);
        var draft = Draft(
            steps: [Step("replacement", 0)],
            transitions: [
                Transition("accept", "not_started", null, "active", "replacement", 0),
                Transition("finish", "active", "replacement", "completed", null, 1)
            ]);
        var signature = QuestAuthoringService.ComputePreviewSignature(
            record.QuestId,
            "save_draft",
            draft,
            record.UpdatedAtUtc);

        var result = await service.SaveDraftAsync(
            record.QuestId,
            new QuestMutationRequest(
                draft.DisplayName,
                draft.SchemaVersion,
                draft.Steps,
                draft.Transitions,
                record.UpdatedAtUtc,
                signature),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("Draft", result.Value!.Quest.PublicationState);
        Assert.Equal("replacement", Assert.Single(result.Value.Quest.Steps).StepId);
    }

    [Fact]
    public async Task ReferencedPublishedQuestCannotSaveDraftAndPreviewReportsBlocker()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Published", Draft());
        repository.SetReferences(record.QuestId, ActiveReference(record.QuestId, "first"));
        var service = CreateService(repository);
        var draft = Draft(
            steps: [Step("replacement", 0)],
            transitions: [
                Transition("accept", "not_started", null, "active", "replacement", 0),
                Transition("finish", "active", "replacement", "completed", null, 1)
            ]);

        var preview = await service.PreviewAsync(
            record.QuestId,
            new PreviewQuestRequest(
                draft.DisplayName,
                draft.SchemaVersion,
                draft.Steps,
                draft.Transitions,
                record.UpdatedAtUtc,
                "save_draft"),
            TestContext.Current.CancellationToken);
        var result = await service.SaveDraftAsync(
            record.QuestId,
            new QuestMutationRequest(
                draft.DisplayName,
                draft.SchemaVersion,
                draft.Steps,
                draft.Transitions,
                record.UpdatedAtUtc,
                preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.True(preview.Succeeded);
        Assert.False(preview.Value!.ValidForDraft);
        Assert.Contains(preview.Value.Messages, IsReferenceBlocked);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, IsReferenceBlocked);
        var current = await repository.LoadAsync(record.QuestId, TestContext.Current.CancellationToken);
        Assert.Equal("Published", current!.PublicationState);
        Assert.Equal("first", Assert.Single(current.Steps).StepId);
    }

    [Fact]
    public async Task ReferencedDraftQuestCannotBeStructurallyReplacedBySaveDraft()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Draft", Draft());
        repository.SetReferences(record.QuestId, new QuestStateReferenceSummary(record.QuestId, 1, 0, 1, []));
        var service = CreateService(repository);
        var draft = Draft(
            steps: [Step("replacement", 0)],
            transitions: [
                Transition("accept", "not_started", null, "active", "replacement", 0),
                Transition("finish", "active", "replacement", "completed", null, 1)
            ]);
        var signature = QuestAuthoringService.ComputePreviewSignature(
            record.QuestId,
            "save_draft",
            draft,
            record.UpdatedAtUtc);

        var result = await service.SaveDraftAsync(
            record.QuestId,
            new QuestMutationRequest(
                draft.DisplayName,
                draft.SchemaVersion,
                draft.Steps,
                draft.Transitions,
                record.UpdatedAtUtc,
                signature),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, IsReferenceBlocked);
        var current = await repository.LoadAsync(record.QuestId, TestContext.Current.CancellationToken);
        Assert.Equal("Draft", current!.PublicationState);
        Assert.Equal("first", Assert.Single(current.Steps).StepId);
    }

    [Fact]
    public async Task DisableActiveReferencedQuestIsBlockedAndDoesNotPublish()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Published", Draft());
        repository.SetReferences(record.QuestId, ActiveReference(record.QuestId, "first"));
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, publisher);

        var preview = await PreviewAsync(service, record, "disable", TestContext.Current.CancellationToken);
        var result = await service.DisableAsync(
            record.QuestId,
            new QuestLifecycleRequest(record.UpdatedAtUtc, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(preview.Value!.ValidForPublication);
        Assert.Contains(preview.Value.Messages, IsReferenceBlocked);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, IsReferenceBlocked);
        Assert.Empty(publisher.Scopes);
        Assert.Equal("Published", (await repository.LoadAsync(record.QuestId, TestContext.Current.CancellationToken))!.PublicationState);
    }

    [Fact]
    public async Task DisableCompletedReferencedQuestIsBlockedAndDoesNotPublish()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Published", Draft());
        repository.SetReferences(record.QuestId, new QuestStateReferenceSummary(record.QuestId, 1, 0, 1, []));
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, publisher);

        var preview = await PreviewAsync(service, record, "disable", TestContext.Current.CancellationToken);
        var result = await service.DisableAsync(
            record.QuestId,
            new QuestLifecycleRequest(record.UpdatedAtUtc, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, IsReferenceBlocked);
        Assert.Empty(publisher.Scopes);
        Assert.Equal("Published", (await repository.LoadAsync(record.QuestId, TestContext.Current.CancellationToken))!.PublicationState);
    }

    [Fact]
    public async Task ReferencedQuestCannotDeleteAndDoesNotPublish()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Disabled", Draft());
        repository.SetReferences(record.QuestId, ActiveReference(record.QuestId, "first"));
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, publisher);

        var preview = await PreviewAsync(service, record, "delete", TestContext.Current.CancellationToken);
        var result = await service.DeleteAsync(
            record.QuestId,
            new QuestDeleteRequest(record.UpdatedAtUtc, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, IsReferenceBlocked);
        Assert.Empty(publisher.Scopes);
        Assert.NotNull(await repository.LoadAsync(record.QuestId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteUnreferencedDisabledQuestRegeneratesRuntimeCatalog()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Disabled", Draft());
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, publisher);

        var preview = await PreviewAsync(service, record, "delete", TestContext.Current.CancellationToken);
        var result = await service.DeleteAsync(
            record.QuestId,
            new QuestDeleteRequest(record.UpdatedAtUtc, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("test_quest", result.Value!.DeletedId);
        Assert.Equal([RuntimeCatalogPublicationScope.Quest], publisher.Scopes);
        Assert.Null(await repository.LoadAsync(record.QuestId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishCannotRemoveCurrentlyReferencedActiveStep()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Draft", Draft(
            steps: [Step("replacement", 0)],
            transitions: [
                Transition("accept", "not_started", null, "active", "replacement", 0),
                Transition("finish", "active", "replacement", "completed", null, 1)
            ]));
        repository.SetReferences(record.QuestId, ActiveReference(record.QuestId, "first"));
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, publisher);

        var preview = await PreviewAsync(service, record, "publish", TestContext.Current.CancellationToken);
        var result = await service.PublishAsync(
            record.QuestId,
            new QuestLifecycleRequest(record.UpdatedAtUtc, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(preview.Value.ValidForPublication);
        Assert.Contains(preview.Value.Messages, IsMissingActiveStep);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, IsMissingActiveStep);
        Assert.Empty(publisher.Scopes);
        Assert.Equal("Draft", (await repository.LoadAsync(record.QuestId, TestContext.Current.CancellationToken))!.PublicationState);
    }

    [Fact]
    public async Task CompatiblePublicationRetainingActiveStepsRegeneratesRuntimeCatalog()
    {
        var repository = new TestQuestRepository();
        var record = repository.Upsert("test_quest", "Draft", Draft(
            steps: [Step("first", 0), Step("second", 1)],
            transitions: [
                Transition("accept", "not_started", null, "active", "first", 0),
                Transition("advance", "active", "first", "active", "second", 1),
                Transition("finish", "active", "second", "completed", null, 2)
            ]));
        repository.SetReferences(record.QuestId, ActiveReference(record.QuestId, "first"));
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, publisher);

        var preview = await PreviewAsync(service, record, "publish", TestContext.Current.CancellationToken);
        var result = await service.PublishAsync(
            record.QuestId,
            new QuestLifecycleRequest(record.UpdatedAtUtc, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("Published", result.Value!.Quest.PublicationState);
        Assert.Equal([RuntimeCatalogPublicationScope.Quest], publisher.Scopes);
    }

    private static QuestAuthoringService CreateService(
        IQuestRepository repository,
        IRuntimeCatalogPublisher? publisher = null) =>
        new(repository, new QuestDefinitionValidator(), new QuestAuthoringRegistry(), publisher);

    private static async Task<AuthoringOperationResult<QuestPreviewResponse>> PreviewAsync(
        QuestAuthoringService service,
        QuestDefinitionRecord record,
        string operation,
        CancellationToken cancellationToken) =>
        await service.PreviewAsync(
            record.QuestId,
            new PreviewQuestRequest(
                record.DisplayName,
                record.SchemaVersion,
                record.Steps,
                record.Transitions,
                record.UpdatedAtUtc,
                operation),
            cancellationToken);

    private static bool IsReferenceBlocked(ApiError error) =>
        error.Code == "quest_state_reference_blocked";

    private static bool IsMissingActiveStep(ApiError error) =>
        error.Code == "quest_active_step_reference_missing";

    private static QuestStateReferenceSummary ActiveReference(string questId, string stepId) =>
        new(questId, 1, 1, 0, [stepId]);

    private static QuestDraft Draft(
        IReadOnlyList<QuestStep>? steps = null,
        IReadOnlyList<QuestTransition>? transitions = null) =>
        new(
            "Test Quest",
            1,
            steps ?? [Step("first", 0)],
            transitions ?? [
                Transition("accept", "not_started", null, "active", "first", 0),
                Transition("finish", "active", "first", "completed", null, 1)
            ],
            null,
            null);

    private static QuestStep Step(string stepId, int order) =>
        new(stepId, stepId.Replace('_', ' '), order);

    private static QuestTransition Transition(
        string transitionId,
        string sourceStatus,
        string? sourceStepId,
        string targetStatus,
        string? targetStepId,
        int transitionOrder) =>
        new(transitionId, sourceStatus, sourceStepId, targetStatus, targetStepId, transitionOrder);

    private sealed class TestQuestRepository : IQuestRepository
    {
        private readonly Dictionary<string, QuestDefinitionRecord> _records = new(StringComparer.Ordinal);
        private readonly Dictionary<string, QuestStateReferenceSummary> _references = new(StringComparer.Ordinal);
        private DateTimeOffset _clock = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        public QuestDefinitionRecord Upsert(string questId, string publicationState, QuestDraft draft)
        {
            _clock = _clock.AddSeconds(1);
            var record = new QuestDefinitionRecord(
                questId,
                draft.DisplayName,
                publicationState,
                draft.SchemaVersion,
                draft.Steps,
                draft.Transitions,
                _clock,
                _clock,
                draft.Steps.Count,
                draft.Transitions.Count);
            _records[questId] = record;
            return record;
        }

        public void SetReferences(string questId, QuestStateReferenceSummary references) =>
            _references[questId] = references;

        public Task<IReadOnlyList<QuestDefinitionRecord>> ListAsync(string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<QuestDefinitionRecord>>(_records.Values.OrderBy(record => record.QuestId, StringComparer.Ordinal).ToArray());

        public Task<QuestDefinitionRecord?> LoadAsync(string questId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.GetValueOrDefault(questId));

        public Task<QuestStateReferenceSummary> LoadStateReferencesAsync(string questId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_references.GetValueOrDefault(questId) ?? new QuestStateReferenceSummary(questId, 0, 0, 0, []));

        public Task<IReadOnlyList<string>> LoadPublishedDialogueReferencesAsync(string questId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<QuestDefinitionRecord> ReplaceDraftAsync(string questId, QuestDraft draft, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default)
        {
            var existing = _records.GetValueOrDefault(questId);
            if (existing is not null && expectedUpdatedAtUtc != existing.UpdatedAtUtc)
            {
                throw new QuestDefinitionConcurrencyException(questId);
            }
            var references = _references.GetValueOrDefault(questId);
            if (references is not null && references.HasReferences)
            {
                throw new QuestDefinitionReferencedByStateException(questId, "save_draft", references);
            }

            return Task.FromResult(Upsert(questId, "Draft", draft));
        }

        public Task<QuestDefinitionRecord> SetPublicationAsync(string questId, string publicationState, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default)
        {
            var existing = _records.GetValueOrDefault(questId) ?? throw new QuestDefinitionNotFoundException(questId);
            if (expectedUpdatedAtUtc != existing.UpdatedAtUtc)
            {
                throw new QuestDefinitionConcurrencyException(questId);
            }

            _clock = _clock.AddSeconds(1);
            var saved = existing with { PublicationState = publicationState, UpdatedAtUtc = _clock };
            _records[questId] = saved;
            return Task.FromResult(saved);
        }

        public Task DeleteAsync(string questId, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default)
        {
            var existing = _records.GetValueOrDefault(questId) ?? throw new QuestDefinitionNotFoundException(questId);
            if (expectedUpdatedAtUtc != existing.UpdatedAtUtc)
            {
                throw new QuestDefinitionConcurrencyException(questId);
            }
            if (existing.PublicationState != "Disabled")
            {
                throw new QuestDefinitionDeleteRequiresDisabledException(questId);
            }

            _records.Remove(questId);
            return Task.CompletedTask;
        }
    }

    private sealed class TestRuntimeCatalogPublisher : IRuntimeCatalogPublisher
    {
        public List<RuntimeCatalogPublicationScope> Scopes { get; } = [];

        public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope, CancellationToken cancellationToken)
        {
            Scopes.Add(scope);
            return Task.FromResult<IReadOnlyList<ApiError>>([]);
        }
    }
}
