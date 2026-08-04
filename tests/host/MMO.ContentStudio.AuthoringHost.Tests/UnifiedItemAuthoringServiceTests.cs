using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class UnifiedItemAuthoringServiceTests : IDisposable
{
    private const string ItemId = "battle_pick";
    private const string IconPath = "res://assets/items/battle_pick.png";

    private readonly string _assetRoot;

    public UnifiedItemAuthoringServiceTests()
    {
        _assetRoot = Path.Combine(Path.GetTempPath(), $"content-studio-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_assetRoot, "items"));
        File.WriteAllBytes(Path.Combine(_assetRoot, "items", "battle_pick.png"), [0]);
        File.WriteAllBytes(Path.Combine(_assetRoot, "items", "renamed_pick.png"), [0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_assetRoot))
        {
            Directory.Delete(_assetRoot, true);
        }
    }

    [Fact]
    public async Task CompleteAggregateRoundTripsWithSemanticEquality()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository);
        var request = UnifiedSaveRequest(null);
        var preview = await service.PreviewAsync(ItemId, ToPreview(request, "save_draft"), TestContext.Current.CancellationToken);

        var saved = await service.SaveDraftAsync(
            ItemId,
            request with { PreviewSignature = preview.Value!.PreviewSignature },
            TestContext.Current.CancellationToken);

        AssertSucceeded(saved);
        var persisted = Assert.Contains(ItemId, repository.Records);
        AssertSemanticallyEqual(
            UnifiedItemDomainRules.Normalize(
                request.DisplayName,
                request.IconTexturePath,
                request.ConsumableBehavior,
                request.Equipment,
                request.ToolCapabilities),
            UnifiedItemDomainRules.FromRecord(persisted));

        var loaded = await service.LoadAsync(ItemId, TestContext.Current.CancellationToken);
        AssertSucceeded(loaded);
        Assert.NotNull(loaded.Value!.ConsumableBehavior);
        Assert.NotNull(loaded.Value.Equipment?.WeaponProfile);
        Assert.Single(loaded.Value.ToolCapabilities);
    }

    [Fact]
    public async Task ChildOnlyEditsAdvanceRootTimestamp()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId].UpdatedAtUtc;

        var result = await service.SaveEquipmentDraftAsync(
            ItemId,
            EquipmentSaveRequest(before) with
            {
                RequiredStrength = 9,
                Requirements = [new EquipmentSkillRequirementDraft("strength", 7)]
            },
            TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.True(after.UpdatedAtUtc > before);
        Assert.Equal("Battle Pick", after.DisplayName);
        Assert.Equal(7, Assert.Single(after.Requirements).RequiredValue);
    }

    [Fact]
    public async Task EquipmentEditsPreserveHiddenConsumableBehavior()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId];

        var result = await service.SaveEquipmentDraftAsync(
            ItemId,
            EquipmentSaveRequest(before.UpdatedAtUtc) with { RequiredStrength = 12 },
            TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Equal(before.ConsumableBehavior, after.ConsumableBehavior);
        Assert.Equal(before.ConsumableEffects, after.ConsumableEffects);
    }

    [Fact]
    public async Task ConsumableEditsPreserveHiddenEquipmentAndWeaponProfile()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId];

        var result = await service.SaveConsumableDraftAsync(
            ItemId,
            ConsumableSaveRequest(before.UpdatedAtUtc) with { SuccessMessage = "Crunch." },
            TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Equal(before.EquipmentSlotId, after.EquipmentSlotId);
        Assert.Equal(before.RequiredStrength, after.RequiredStrength);
        AssertSemanticallyEqual(
            UnifiedItemDomainRules.FromRecord(before).Equipment!,
            UnifiedItemDomainRules.FromRecord(after).Equipment!);
    }

    [Fact]
    public async Task HandEquipmentEditsPreserveHiddenConsumableBehavior()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId];
        var request = HandEquipmentSaveRequest(before.UpdatedAtUtc) with { RequiredStrength = 14 };
        var preview = await service.PreviewHandEquipmentAsync(
            ItemId,
            ToPreview(request, "save_draft"),
            TestContext.Current.CancellationToken);

        var result = await service.SaveHandEquipmentDraftAsync(
            ItemId,
            request with { PreviewSignature = preview.Value!.PreviewSignature },
            TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Equal(before.ConsumableBehavior, after.ConsumableBehavior);
        Assert.Equal(before.ConsumableEffects, after.ConsumableEffects);
    }

    [Fact]
    public async Task EquipmentDisablePreservesToolCapabilities()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;

        var result = await service.SaveEquipmentDraftAsync(
            ItemId,
            EquipmentSaveRequest(expected) with { Equippable = false, EquipmentSlotId = null },
            TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Null(after.EquipmentSlotId);
        Assert.Single(after.ToolCapabilities);
    }

    [Fact]
    public async Task HandEquipmentDisablePreservesSubmittedToolCapabilities()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;
        var request = HandEquipmentSaveRequest(expected) with { Equippable = false, EquipmentSlotId = null };
        var preview = await service.PreviewHandEquipmentAsync(
            ItemId,
            ToPreview(request, "save_draft"),
            TestContext.Current.CancellationToken);

        var result = await service.SaveHandEquipmentDraftAsync(
            ItemId,
            request with { PreviewSignature = preview.Value!.PreviewSignature },
            TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Null(after.EquipmentSlotId);
        Assert.Single(after.ToolCapabilities);
    }

    [Fact]
    public async Task ExplicitEmptyCapabilityCollectionDeletesCapabilities()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;
        var request = HandEquipmentSaveRequest(expected) with
        {
            Equippable = false,
            EquipmentSlotId = null,
            ToolCapabilities = []
        };
        var preview = await service.PreviewHandEquipmentAsync(
            ItemId,
            ToPreview(request, "save_draft"),
            TestContext.Current.CancellationToken);

        var result = await service.SaveHandEquipmentDraftAsync(
            ItemId,
            request with { PreviewSignature = preview.Value!.PreviewSignature },
            TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        Assert.Empty(repository.Records[ItemId].ToolCapabilities);
    }

    [Fact]
    public async Task BasicAdapterPreservesEverySpecialization()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId];

        var result = await service.SaveBasicDraftAsync(
            ItemId,
            new SaveBasicItemDraftRequest("Renamed Pick", "res://assets/items/renamed_pick.png", before.UpdatedAtUtc),
            TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Equal("Renamed Pick", after.DisplayName);
        var expected = UnifiedItemDomainRules.FromRecord(before) with
        {
            DisplayName = after.DisplayName,
            IconTexturePath = after.IconTexturePath
        };
        AssertSemanticallyEqual(expected, UnifiedItemDomainRules.FromRecord(after));
    }

    [Fact]
    public async Task HiddenInvalidSpecializationBlocksPublication()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord(consumableEffects: []));
        var service = CreateService(repository);

        var result = await service.PublishEquipmentAsync(
            ItemId,
            repository.Records[ItemId].UpdatedAtUtc,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "consumable_has_no_effects");
        Assert.False(repository.Records[ItemId].RuntimeEnabled);
    }

    [Fact]
    public async Task StaleConcurrencyIsEnforcedThroughEveryCompatibilityAdapter()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var stale = repository.Records[ItemId].UpdatedAtUtc.AddMinutes(-1);

        var basic = await service.SaveBasicDraftAsync(
            ItemId,
            new SaveBasicItemDraftRequest("Battle Pick", IconPath, stale),
            TestContext.Current.CancellationToken);
        var consumable = await service.SaveConsumableDraftAsync(ItemId, ConsumableSaveRequest(stale), TestContext.Current.CancellationToken);
        var equipment = await service.SaveEquipmentDraftAsync(ItemId, EquipmentSaveRequest(stale), TestContext.Current.CancellationToken);
        var handEquipment = await service.SaveHandEquipmentDraftAsync(ItemId, HandEquipmentSaveRequest(stale), TestContext.Current.CancellationToken);
        var publishBasic = await service.PublishBasicAsync(ItemId, stale, TestContext.Current.CancellationToken);
        var publishConsumable = await service.PublishConsumableAsync(ItemId, stale, TestContext.Current.CancellationToken);
        var publishEquipment = await service.PublishEquipmentAsync(ItemId, stale, TestContext.Current.CancellationToken);
        var publishHandEquipment = await service.PublishAsync(
            ItemId,
            new HandEquipmentPublicationRequest(stale, null),
            TestContext.Current.CancellationToken);

        foreach (var result in new AuthoringOperationResult<object>[]
        {
            CastFailure(basic),
            CastFailure(consumable),
            CastFailure(equipment),
            CastFailure(handEquipment),
            CastFailure(publishBasic),
            CastFailure(publishConsumable),
            CastFailure(publishEquipment),
            CastFailure(publishHandEquipment)
        })
        {
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, error => error.Code == "item_version_conflict");
        }
    }

    [Fact]
    public async Task UnifiedRoutesRejectPreviewSignatureMismatch()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;
        var request = UnifiedSaveRequest(expected) with { DisplayName = "Signed Pick" };
        var preview = await service.PreviewAsync(ItemId, ToPreview(request, "save_draft"), TestContext.Current.CancellationToken);

        var result = await service.SaveDraftAsync(
            ItemId,
            request with { PreviewSignature = $"{preview.Value!.PreviewSignature}-stale" },
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "preview_signature_mismatch");
    }

    [Fact]
    public async Task PublishDisableAndDeleteUseSavedCompleteAggregate()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;

        var publish = await service.PublishAsync(
            ItemId,
            new HandEquipmentPublicationRequest(expected, null),
            TestContext.Current.CancellationToken);
        AssertSucceeded(publish);
        Assert.True(repository.Records[ItemId].RuntimeEnabled);

        var publishedAt = repository.Records[ItemId].UpdatedAtUtc;
        var disable = await service.DisableAsync(
            ItemId,
            new HandEquipmentPublicationRequest(publishedAt, null),
            TestContext.Current.CancellationToken);
        AssertSucceeded(disable);
        Assert.False(repository.Records[ItemId].RuntimeEnabled);

        var disabledAt = repository.Records[ItemId].UpdatedAtUtc;
        var previewDelete = await service.PreviewAsync(
            ItemId,
            ToPreview(UnifiedSaveRequest(disabledAt), "delete"),
            TestContext.Current.CancellationToken);
        var delete = await service.DeleteAsync(
            ItemId,
            new DeleteMutationRequest(disabledAt, previewDelete.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        AssertSucceeded(delete);
        Assert.False(repository.Records.ContainsKey(ItemId));
    }

    [Fact]
    public async Task ReloadVerificationFailureReturnsStructuredError()
    {
        var repository = new InMemoryUnifiedItemRepository { CorruptNextReloadAfterSave = true };
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;

        var result = await service.SaveBasicDraftAsync(
            ItemId,
            new SaveBasicItemDraftRequest("Renamed Pick", "res://assets/items/renamed_pick.png", expected),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "database_unavailable");
    }

    [Fact]
    public void RepositoryReplacesChildCollectionsInsideTheRootTransaction()
    {
        var source = File.ReadAllText(FindRepositoryRootFile("host/Persistence/UnifiedItemRepository.cs"));
        var saveBody = Between(source, "public async Task<UnifiedItemRecord> SaveDraftAsync", "public async Task<UnifiedItemRecord> SetPublicationAsync");

        Assert.Contains("BeginTransactionAsync", saveBody);
        Assert.Contains("updated_at = now()", saveBody);
        Assert.Contains("await ReplaceConsumableAsync(connection, transaction", saveBody);
        Assert.Contains("await ReplaceEquipmentAsync(connection, transaction", saveBody);
        Assert.Contains("await ReplaceToolCapabilitiesAsync(connection, transaction", saveBody);
        Assert.Contains("LoadAggregateAsync(connection, transaction", saveBody);
        Assert.Contains("CommitAsync", saveBody);

        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_consumable_requirements\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_consumable_effects\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_skill_requirements\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_skill_modifiers\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_combat_profiles\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_combat_bonuses\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_tool_capabilities\"", source);
    }

    private UnifiedItemAuthoringService CreateService(InMemoryUnifiedItemRepository repository)
    {
        var assetService = new ItemAssetService(Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = _assetRoot
            }
        }));
        var registry = new HandEquipmentAuthoringRegistry();
        var validator = new UnifiedItemValidator(repository, registry, assetService);
        return new UnifiedItemAuthoringService(
            repository,
            validator,
            registry,
            assetService,
            NullLogger<UnifiedItemAuthoringService>.Instance);
    }

    private static SaveItemDraftRequest UnifiedSaveRequest(DateTimeOffset? expected) =>
        new(
            "Battle Pick",
            IconPath,
            ConsumableDraft(),
            EquipmentDraft(),
            [ToolDraft()],
            expected,
            null);

    private static PreviewItemRequest ToPreview(SaveItemDraftRequest request, string operation) =>
        new(
            request.DisplayName,
            request.IconTexturePath,
            request.ConsumableBehavior,
            request.Equipment,
            request.ToolCapabilities,
            request.ExpectedUpdatedAtUtc,
            operation);

    private static SaveConsumableDraftRequest ConsumableSaveRequest(DateTimeOffset? expected) =>
        new(
            "Battle Pick",
            IconPath,
            "eat",
            1,
            null,
            "Restored.",
            false,
            0,
            null,
            null,
            [],
            [new ConsumableEffectDefinition(0, "restore_resource", "health", 1, 3)],
            expected);

    private static SaveEquipmentDraftRequest EquipmentSaveRequest(DateTimeOffset? expected) =>
        new(
            "Battle Pick",
            IconPath,
            true,
            "right_hand",
            5,
            [new EquipmentSkillRequirementDraft("strength", 3)],
            [new EquipmentSkillModifierDraft("attack", 1)],
            EquipmentCombatBonusDefinition.Zero,
            expected);

    private static SaveHandEquipmentDraftRequest HandEquipmentSaveRequest(DateTimeOffset? expected) =>
        new(
            "Battle Pick",
            IconPath,
            true,
            "right_hand",
            5,
            [new EquipmentSkillRequirementDraft("strength", 3)],
            [new EquipmentSkillModifierDraft("attack", 1)],
            WeaponProfile(),
            EquipmentCombatBonusDefinition.Zero,
            [ToolDraft()],
            expected,
            null);

    private static HandEquipmentPreviewRequest ToPreview(SaveHandEquipmentDraftRequest request, string operation) =>
        new(
            request.DisplayName,
            request.IconTexturePath,
            request.Equippable,
            request.EquipmentSlotId,
            request.RequiredStrength,
            request.Requirements,
            request.SkillModifiers,
            request.WeaponProfile,
            request.CombatBonuses,
            request.ToolCapabilities,
            request.ExpectedUpdatedAtUtc,
            operation);

    private static ItemConsumableBehaviorDraft ConsumableDraft() =>
        new(
            "eat",
            1,
            null,
            "Restored.",
            false,
            0,
            null,
            null,
            [],
            [new ConsumableEffectDefinition(0, "restore_resource", "health", 1, 3)]);

    private static ItemEquipmentMetadataDraft EquipmentDraft() =>
        new(
            "right_hand",
            5,
            [new EquipmentSkillRequirementDraft("strength", 3)],
            [new EquipmentSkillModifierDraft("attack", 1)],
            EquipmentCombatBonusDefinition.Zero,
            WeaponProfile());

    private static EquipmentCombatProfileDefinition WeaponProfile() =>
        new("battle_pick", "melee", "crush", 1, 1, 4);

    private static HandEquipmentToolCapabilityDraft ToolDraft() =>
        new("mining", 1, "swing", null);

    private static UnifiedItemRecord CompleteRecord(
        IReadOnlyList<ConsumableEffectDefinition>? consumableEffects = null) =>
        new(
            ItemId,
            "Battle Pick",
            IconPath,
            "right_hand",
            "Right Hand",
            false,
            5,
            true,
            true,
            false,
            true,
            true,
            true,
            new ConsumableProfileDraft("eat", 1, null, "Restored.", false, 0, null, null),
            [],
            consumableEffects ?? [new ConsumableEffectDefinition(0, "restore_resource", "health", 1, 3)],
            [new EquipmentSkillRequirementDefinition("strength", "Strength", 3)],
            [new EquipmentSkillModifierDefinition("attack", "Attack", 1)],
            WeaponProfile(),
            EquipmentCombatBonusDefinition.Zero,
            [new HandEquipmentToolCapabilityDefinition("mining", "Mining", 0, 1, "swing", null)],
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

    private static AuthoringOperationResult<object> CastFailure<T>(AuthoringOperationResult<T> result) =>
        result.Succeeded
            ? AuthoringOperationResult<object>.Success(result.Value!)
            : AuthoringOperationResult<object>.Failure(result.Errors);

    private static void AssertSucceeded<T>(AuthoringOperationResult<T> result) =>
        Assert.True(
            result.Succeeded,
            string.Join("; ", result.Errors.Select(error => $"{error.Code}:{error.Field}:{error.Message}:{error.Remediation}")));

    private static void AssertSemanticallyEqual<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private static string FindRepositoryRootFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find {start}.");
        Assert.True(endIndex > startIndex, $"Could not find {end}.");
        return source[startIndex..endIndex];
    }

    private sealed class InMemoryUnifiedItemRepository : IUnifiedItemRepository
    {
        private DateTimeOffset _clock = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        public Dictionary<string, UnifiedItemRecord> Records { get; } = new(StringComparer.Ordinal);

        public bool CorruptNextReloadAfterSave { get; init; }

        private bool _corruptNextLoad;

        public void Put(UnifiedItemRecord record) => Records[record.ItemId] = record;

        public Task<IReadOnlyList<UnifiedItemRecord>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnifiedItemRecord>>(Records.Values.ToArray());

        public Task<UnifiedItemRecord?> LoadAsync(
            string itemId,
            CancellationToken cancellationToken = default)
        {
            Records.TryGetValue(itemId, out var record);
            if (record is not null && _corruptNextLoad)
            {
                _corruptNextLoad = false;
                record = record with { DisplayName = $"{record.DisplayName} Corrupt" };
            }

            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<EquipmentSlotRecord>> LoadSlotsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EquipmentSlotRecord>>(
            [
                new("right_hand", "Right Hand"),
                new("left_hand", "Left Hand"),
                new("body", "Body")
            ]);

        public Task<IReadOnlyList<EquipmentSkillRecord>> LoadSkillsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EquipmentSkillRecord>>(
            [
                new("attack", "Attack"),
                new("strength", "Strength"),
                new("defence", "Defence"),
                new("mining", "Mining")
            ]);

        public Task<IReadOnlyList<EquipmentSkillRecord>> LoadGatheringCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EquipmentSkillRecord>>([new("mining", "Mining")]);

        public Task<IReadOnlyList<AuthoringOption>> LoadPublishedItemOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthoringOption>>([]);

        public Task<bool> HasLiveReferencesAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> HasPublishedConsumableResultReferencesAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<ReferencedItemRecord?> LoadReferencedItemAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferencedItemRecord?>(null);

        public Task<UnifiedItemRecord> SaveDraftAsync(
            string itemId,
            NormalizedItemDraft draft,
            DateTimeOffset? expectedUpdatedAtUtc,
            bool expectNew,
            CancellationToken cancellationToken = default)
        {
            Records.TryGetValue(itemId, out var existing);
            if (expectNew && existing is not null)
            {
                throw new UnifiedItemConcurrencyException(itemId, existing.UpdatedAtUtc);
            }
            EnsureExpectedVersion(itemId, existing, expectedUpdatedAtUtc);

            var saved = ToRecord(itemId, draft, false, NextTimestamp());
            Records[itemId] = saved;
            _corruptNextLoad = CorruptNextReloadAfterSave;
            return Task.FromResult(saved);
        }

        public Task<UnifiedItemRecord> SetPublicationAsync(
            string itemId,
            bool runtimeEnabled,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!Records.TryGetValue(itemId, out var existing))
            {
                throw new UnifiedItemNotFoundException(itemId);
            }
            EnsureExpectedVersion(itemId, existing, expectedUpdatedAtUtc);

            var saved = existing with
            {
                RuntimeEnabled = runtimeEnabled,
                UpdatedAtUtc = existing.RuntimeEnabled == runtimeEnabled ? existing.UpdatedAtUtc : NextTimestamp()
            };
            Records[itemId] = saved;
            return Task.FromResult(saved);
        }

        public Task DeleteAsync(
            string itemId,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!Records.TryGetValue(itemId, out var existing))
            {
                throw new UnifiedItemNotFoundException(itemId);
            }
            EnsureExpectedVersion(itemId, existing, expectedUpdatedAtUtc);
            if (existing.RuntimeEnabled)
            {
                throw new UnifiedItemPublishedDeleteException(itemId);
            }

            Records.Remove(itemId);
            return Task.CompletedTask;
        }

        private DateTimeOffset NextTimestamp()
        {
            _clock = _clock.AddMinutes(1);
            return _clock;
        }

        private static UnifiedItemRecord ToRecord(
            string itemId,
            NormalizedItemDraft draft,
            bool runtimeEnabled,
            DateTimeOffset updatedAtUtc)
        {
            var equipment = draft.Equipment;
            var consumable = draft.ConsumableBehavior;
            return new UnifiedItemRecord(
                itemId,
                draft.DisplayName,
                draft.IconTexturePath,
                equipment?.EquipmentSlotId,
                equipment?.EquipmentSlotId is null ? null : equipment.EquipmentSlotId,
                runtimeEnabled,
                equipment?.RequiredStrength ?? 1,
                consumable is not null,
                equipment?.WeaponProfile is not null,
                equipment?.CombatBonuses.IsZero == false,
                equipment?.Requirements.Count > 0,
                equipment?.SkillModifiers.Count > 0,
                draft.ToolCapabilities.Count > 0,
                consumable is null
                    ? null
                    : new ConsumableProfileDraft(
                        consumable.UseAction,
                        consumable.ConsumeQuantity,
                        consumable.ResultItemId,
                        consumable.SuccessMessage,
                        consumable.UsableInCombat,
                        consumable.CooldownMs,
                        consumable.UseAnimationId,
                        consumable.UseSoundResourcePath),
                consumable?.Requirements ?? [],
                consumable?.Effects ?? [],
                equipment?.Requirements
                    .Select(value => new EquipmentSkillRequirementDefinition(value.SkillId, value.SkillId, value.RequiredValue))
                    .ToArray() ?? [],
                equipment?.SkillModifiers
                    .Select(value => new EquipmentSkillModifierDefinition(value.SkillId, value.SkillId, value.ModifierValue))
                    .ToArray() ?? [],
                equipment?.WeaponProfile,
                equipment?.CombatBonuses,
                draft.ToolCapabilities
                    .Select((value, index) => new HandEquipmentToolCapabilityDefinition(
                        value.CapabilityId,
                        value.CapabilityId,
                        index,
                        value.PowerTier,
                        value.ActionAnimationId,
                        value.EffectResourceId))
                    .ToArray(),
                updatedAtUtc);
        }

        private static void EnsureExpectedVersion(
            string itemId,
            UnifiedItemRecord? existing,
            DateTimeOffset? expectedUpdatedAtUtc)
        {
            if (existing is null && expectedUpdatedAtUtc is null)
            {
                return;
            }
            if (existing is null
                || expectedUpdatedAtUtc is null
                || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
            {
                throw new UnifiedItemConcurrencyException(itemId, existing?.UpdatedAtUtc ?? DateTimeOffset.MinValue);
            }
        }
    }
}
