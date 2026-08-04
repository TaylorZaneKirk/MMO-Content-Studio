using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class NpcAuthoringServiceTests : IDisposable
{
    private const string NpcId = "test_npc";
    private readonly string _root;
    private readonly string _assetRoot;

    public NpcAuthoringServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"npc-service-{Guid.NewGuid():N}");
        _assetRoot = Path.Combine(_root, "prototype", "client", "assets");
        Directory.CreateDirectory(Path.Combine(_assetRoot, "actors", "npcs"));
        Directory.CreateDirectory(Path.Combine(_root, "prototype", "shared", "dialogues"));
        WritePng(Path.Combine(_assetRoot, "actors", "npcs", "test_npc.png"), 32, 32);
        File.WriteAllText(
            Path.Combine(_root, "prototype", "shared", "dialogues", "catalog.json"),
            """
            { "schema_version": 1, "dialogues": [ { "dialogue_id": "test_npc_greeting" } ] }
            """);
    }

    [Fact]
    public async Task OptionsExposeDialogueReferencesAndHonestCapabilities()
    {
        var service = CreateService(new InMemoryNpcRepository());

        var result = await service.LoadOptionsAsync(TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        Assert.Contains(result.Value!.DialogueReferences, reference => reference.Id == "test_npc_greeting");
        Assert.True(result.Value.CanValidateDialogueReferences);
        Assert.True(result.Value.Capabilities.SupportsCompleteDialogueReferenceValidation);
        Assert.False(result.Value.Capabilities.SupportsRuntimeNpcCatalog);
        Assert.False(result.Value.Capabilities.SupportsMultipleInteractions);
        Assert.False(result.Value.Capabilities.SupportsQuestAuthoring);
    }

    [Fact]
    public async Task CatalogFilteringOrderingAndLoadUseCompleteAggregate()
    {
        var repository = new InMemoryNpcRepository();
        repository.Put(Record("z_npc", "Zulu", "Draft"));
        repository.Put(Record("a_npc", "Alpha", "Published"));
        var service = CreateService(repository);

        var list = await service.ListAsync("npc", TestContext.Current.CancellationToken);
        var loaded = await service.LoadAsync("a_npc", TestContext.Current.CancellationToken);

        AssertSucceeded(list);
        Assert.Equal(["a_npc", "z_npc"], list.Value!.Items.Select(item => item.NpcDefinitionId));
        AssertSucceeded(loaded);
        Assert.Equal("test_npc_greeting", loaded.Value!.DefaultDialogueId);
        Assert.Equal("notes", loaded.Value.Notes);
    }

    [Fact]
    public async Task NewSaveAndExistingSaveAdvanceRootTimestamp()
    {
        var repository = new InMemoryNpcRepository();
        var service = CreateService(repository);

        var preview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(null), "save_draft"),
            TestContext.Current.CancellationToken);
        var created = await service.SaveDraftAsync(
            NpcId,
            ValidSaveRequest(null) with { PreviewSignature = preview.Value!.PreviewSignature },
            TestContext.Current.CancellationToken);
        var createdAt = created.Value!.Npc.UpdatedAtUtc;

        var previewUpdate = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(createdAt) with { DisplayName = "Renamed NPC" }, "save_draft"),
            TestContext.Current.CancellationToken);
        var updated = await service.SaveDraftAsync(
            NpcId,
            ValidSaveRequest(createdAt) with
            {
                DisplayName = "Renamed NPC",
                PreviewSignature = previewUpdate.Value!.PreviewSignature
            },
            TestContext.Current.CancellationToken);

        AssertSucceeded(created);
        AssertSucceeded(updated);
        Assert.Equal("Renamed NPC", updated.Value!.Npc.DisplayName);
        Assert.True(updated.Value.Npc.UpdatedAtUtc > createdAt);
    }

    [Fact]
    public async Task StaleSaveAndSignatureMismatchFail()
    {
        var repository = new InMemoryNpcRepository();
        repository.Put(Record(NpcId, "Test NPC", "Draft"));
        var service = CreateService(repository);
        var stale = repository.Records[NpcId].UpdatedAtUtc.AddMinutes(-1);

        var staleResult = await service.SaveDraftAsync(
            NpcId,
            ValidSaveRequest(stale) with { PreviewSignature = "wrong" },
            TestContext.Current.CancellationToken);
        var preview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(repository.Records[NpcId].UpdatedAtUtc), "save_draft"),
            TestContext.Current.CancellationToken);
        var mismatch = await service.SaveDraftAsync(
            NpcId,
            ValidSaveRequest(repository.Records[NpcId].UpdatedAtUtc) with
            {
                DisplayName = "Edited",
                PreviewSignature = preview.Value!.PreviewSignature
            },
            TestContext.Current.CancellationToken);

        Assert.False(staleResult.Succeeded);
        Assert.Contains(staleResult.Errors, error => error.Code == "npc_preview_mismatch");
        Assert.False(mismatch.Succeeded);
        Assert.Contains(mismatch.Errors, error => error.Code == "npc_preview_mismatch");
    }

    [Fact]
    public async Task PublishUsesSavedAggregateAndRejectsUnsavedPreviewChanges()
    {
        var repository = new InMemoryNpcRepository();
        repository.Put(Record(NpcId, "Test NPC", "Draft"));
        var service = CreateService(repository);
        var expected = repository.Records[NpcId].UpdatedAtUtc;

        var unsavedPreview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(expected) with { DisplayName = "Unsaved" }, "publish"),
            TestContext.Current.CancellationToken);
        var preview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(expected), "publish"),
            TestContext.Current.CancellationToken);
        var publish = await service.PublishAsync(
            NpcId,
            new NpcPublicationRequest(expected, preview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        AssertSucceeded(unsavedPreview);
        Assert.Contains(unsavedPreview.Value!.Messages, message => message.Code == "unsaved_npc_changes");
        AssertSucceeded(publish);
        Assert.Equal("Published", publish.Value!.Npc.PublicationState);
    }

    [Fact]
    public async Task DisableAndDeleteAreBlockedByKnownReferences()
    {
        var repository = new InMemoryNpcRepository();
        repository.Put(Record(NpcId, "Test NPC", "Published"));
        repository.ReferenceSummaries[NpcId] = new NpcReferenceSummaryRecord(
            NpcId,
            1,
            ["generated/starter_region/chunks/0_0.json"],
            true);
        var service = CreateService(repository);
        var expected = repository.Records[NpcId].UpdatedAtUtc;

        var disablePreview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(expected), "disable"),
            TestContext.Current.CancellationToken);
        var disable = await service.DisableAsync(
            NpcId,
            new NpcPublicationRequest(expected, disablePreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        repository.Put(repository.Records[NpcId] with { PublicationState = "Disabled" });
        expected = repository.Records[NpcId].UpdatedAtUtc;
        var deletePreview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(expected), "delete"),
            TestContext.Current.CancellationToken);
        var delete = await service.DeleteAsync(
            NpcId,
            new NpcDeleteRequest(expected, deletePreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(disable.Succeeded);
        Assert.Contains(disable.Errors, error => error.Code == "npc_disable_blocked_by_reference");
        Assert.False(delete.Succeeded);
        Assert.Contains(delete.Errors, error => error.Code == "npc_delete_blocked_by_reference");
    }

    [Fact]
    public async Task DeleteRequiresDisabledAndThenRemovesAggregate()
    {
        var repository = new InMemoryNpcRepository();
        repository.Put(Record(NpcId, "Test NPC", "Draft"));
        var service = CreateService(repository);
        var expected = repository.Records[NpcId].UpdatedAtUtc;

        var draftPreview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(expected), "delete"),
            TestContext.Current.CancellationToken);
        var draftDelete = await service.DeleteAsync(
            NpcId,
            new NpcDeleteRequest(expected, draftPreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        repository.Put(repository.Records[NpcId] with { PublicationState = "Disabled" });
        expected = repository.Records[NpcId].UpdatedAtUtc;
        var disabledPreview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(expected), "delete"),
            TestContext.Current.CancellationToken);
        var disabledDelete = await service.DeleteAsync(
            NpcId,
            new NpcDeleteRequest(expected, disabledPreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        Assert.False(draftDelete.Succeeded);
        Assert.Contains(draftDelete.Errors, error => error.Code == "npc_delete_requires_disabled");
        AssertSucceeded(disabledDelete);
        Assert.False(repository.Records.ContainsKey(NpcId));
    }

    [Fact]
    public async Task ReloadVerificationFailureReturnsStructuredError()
    {
        var repository = new InMemoryNpcRepository { CorruptNextReloadAfterSave = true };
        repository.Put(Record(NpcId, "Test NPC", "Draft"));
        var service = CreateService(repository);
        var expected = repository.Records[NpcId].UpdatedAtUtc;
        var preview = await service.PreviewAsync(
            NpcId,
            ToPreviewRequest(ValidSaveRequest(expected) with { DisplayName = "Edited" }, "save_draft"),
            TestContext.Current.CancellationToken);

        var result = await service.SaveDraftAsync(
            NpcId,
            ValidSaveRequest(expected) with
            {
                DisplayName = "Edited",
                PreviewSignature = preview.Value!.PreviewSignature
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "npc_reload_verification_failed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private NpcAuthoringService CreateService(InMemoryNpcRepository repository)
    {
        var options = Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = _assetRoot
            }
        });
        var assetService = new ItemAssetService(options);
        var dialogueProvider = new NpcDialogueReferenceProvider(options);
        return new NpcAuthoringService(
            repository,
            new NpcDefinitionValidator(assetService, dialogueProvider),
            new NpcAuthoringRegistry(),
            dialogueProvider,
            assetService,
            NullLogger<NpcAuthoringService>.Instance);
    }

    private static SaveNpcDraftRequest ValidSaveRequest(DateTimeOffset? expected) => new(
        "Test NPC",
        "res://assets/actors/npcs/test_npc.png",
        32,
        32,
        0,
        0,
        0.25,
        1,
        1,
        "static",
        0,
        600,
        0.15,
        true,
        1,
        "talk",
        "test_npc_greeting",
        "notes",
        expected,
        null);

    private static PreviewNpcRequest ToPreviewRequest(SaveNpcDraftRequest request, string operation) => new(
        request.DisplayName,
        request.VisualTexturePath,
        request.SourceWidth,
        request.SourceHeight,
        request.VisualAnchorOffsetX,
        request.VisualAnchorOffsetY,
        request.VisualRenderScale,
        request.FootprintWidthTiles,
        request.FootprintHeightTiles,
        request.MovementBehavior,
        request.WanderRadiusTiles,
        request.TickIntervalMs,
        request.IdleChance,
        request.InteractionEnabled,
        request.InteractionRangeTiles,
        request.DefaultInteraction,
        request.DefaultDialogueId,
        request.Notes,
        request.ExpectedUpdatedAtUtc,
        operation);

    private static NpcDefinitionRecord Record(
        string npcDefinitionId,
        string displayName,
        string publicationState) =>
        new(
            npcDefinitionId,
            displayName,
            publicationState,
            "res://assets/actors/npcs/test_npc.png",
            32,
            32,
            0,
            0,
            0.25,
            1,
            1,
            "static",
            0,
            600,
            0.15,
            true,
            1,
            "talk",
            "test_npc_greeting",
            "notes",
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

    private static void AssertSucceeded<T>(AuthoringOperationResult<T> result) =>
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Code)));

    private static void WritePng(string path, int width, int height)
    {
        Span<byte> header =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52,
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height
        ];
        File.WriteAllBytes(path, header.ToArray());
    }

    private sealed class InMemoryNpcRepository : INpcRepository
    {
        private DateTimeOffset _clock = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        private bool _corruptNextLoad;

        public Dictionary<string, NpcDefinitionRecord> Records { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, NpcReferenceSummaryRecord> ReferenceSummaries { get; } = new(StringComparer.Ordinal);

        public bool CorruptNextReloadAfterSave { get; init; }

        public void Put(NpcDefinitionRecord record) => Records[record.NpcDefinitionId] = record;

        public Task<IReadOnlyList<NpcDefinitionRecord>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default)
        {
            var query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            var records = Records.Values
                .Where(record => query is null
                    || record.NpcDefinitionId.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || record.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(record => record.DisplayName, StringComparer.Ordinal)
                .ThenBy(record => record.NpcDefinitionId, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult<IReadOnlyList<NpcDefinitionRecord>>(records);
        }

        public Task<NpcDefinitionRecord?> LoadAsync(
            string npcDefinitionId,
            CancellationToken cancellationToken = default)
        {
            Records.TryGetValue(npcDefinitionId, out var record);
            if (record is not null && _corruptNextLoad)
            {
                _corruptNextLoad = false;
                record = record with { DisplayName = $"{record.DisplayName} Corrupt" };
            }

            return Task.FromResult(record);
        }

        public Task<NpcDefinitionRecord?> LoadForUpdateAsync(
            string npcDefinitionId,
            CancellationToken cancellationToken = default) =>
            LoadAsync(npcDefinitionId, cancellationToken);

        public Task<NpcDefinitionRecord> SaveDraftAsync(
            string npcDefinitionId,
            NpcDraft draft,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Records.TryGetValue(npcDefinitionId, out var existing);
            EnsureExpectedVersion(npcDefinitionId, existing, expectedUpdatedAtUtc);
            var timestamp = NextTimestamp();
            var saved = new NpcDefinitionRecord(
                npcDefinitionId,
                draft.DisplayName,
                "Draft",
                draft.VisualTexturePath,
                draft.SourceWidth,
                draft.SourceHeight,
                draft.VisualAnchorOffsetX,
                draft.VisualAnchorOffsetY,
                draft.VisualRenderScale,
                draft.FootprintWidthTiles,
                draft.FootprintHeightTiles,
                draft.MovementBehavior,
                draft.WanderRadiusTiles,
                draft.TickIntervalMs,
                draft.IdleChance,
                draft.InteractionEnabled,
                draft.InteractionRangeTiles,
                draft.DefaultInteraction,
                draft.DefaultDialogueId,
                draft.Notes,
                existing?.CreatedAtUtc ?? timestamp,
                timestamp);
            Records[npcDefinitionId] = saved;
            _corruptNextLoad = CorruptNextReloadAfterSave;
            return Task.FromResult(saved);
        }

        public Task<NpcDefinitionRecord> SetPublicationAsync(
            string npcDefinitionId,
            string publicationState,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!Records.TryGetValue(npcDefinitionId, out var existing))
            {
                throw new NpcDefinitionNotFoundException(npcDefinitionId);
            }
            EnsureExpectedVersion(npcDefinitionId, existing, expectedUpdatedAtUtc);
            var saved = existing with
            {
                PublicationState = publicationState,
                UpdatedAtUtc = NextTimestamp()
            };
            Records[npcDefinitionId] = saved;
            return Task.FromResult(saved);
        }

        public Task DeleteAsync(
            string npcDefinitionId,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!Records.TryGetValue(npcDefinitionId, out var existing))
            {
                throw new NpcDefinitionNotFoundException(npcDefinitionId);
            }
            EnsureExpectedVersion(npcDefinitionId, existing, expectedUpdatedAtUtc);
            if (existing.PublicationState != "Disabled")
            {
                throw new NpcDefinitionDeleteRequiresDisabledException(npcDefinitionId);
            }

            Records.Remove(npcDefinitionId);
            return Task.CompletedTask;
        }

        public Task<NpcReferenceSummaryRecord> LoadKnownSpawnReferencesAsync(
            string npcDefinitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReferenceSummaries.TryGetValue(npcDefinitionId, out var summary)
                ? summary
                : new NpcReferenceSummaryRecord(npcDefinitionId, 0, [], false));

        private DateTimeOffset NextTimestamp()
        {
            _clock = _clock.AddMinutes(1);
            return _clock;
        }

        private static void EnsureExpectedVersion(
            string npcDefinitionId,
            NpcDefinitionRecord? existing,
            DateTimeOffset? expected)
        {
            if (existing is null)
            {
                if (expected is not null)
                {
                    throw new NpcDefinitionConcurrencyException(npcDefinitionId, null);
                }
                return;
            }

            if (expected is null || existing.UpdatedAtUtc != expected.Value)
            {
                throw new NpcDefinitionConcurrencyException(npcDefinitionId, existing.UpdatedAtUtc);
            }
        }
    }
}
