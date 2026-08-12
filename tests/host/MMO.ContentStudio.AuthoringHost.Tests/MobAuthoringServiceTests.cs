using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class MobAuthoringServiceTests
{
    [Fact]
    public async Task LoadFlatSpriteDoesNotIncludeRiggedPresentation()
    {
        var repository = new InMemoryMobRepository();
        repository.Put(Record());
        var service = CreateService(repository, FindProjectClientAssetsRoot());

        var loaded = await service.LoadAsync("slime", TestContext.Current.CancellationToken);

        AssertSucceeded(loaded);
        Assert.Null(loaded.Value!.RiggedSpritePreview);
    }

    [Fact]
    public async Task LoadPersistedCompositeOrcIncludesStaticRiggedPresentation()
    {
        var repository = new InMemoryMobRepository();
        repository.Put(Record() with
        {
            MobDefinitionId = "orc_001",
            DisplayName = "Orc",
            VisualTexturePath = "res://assets/maps/objects/mobs/orc.png",
            SourceWidth = 160,
            SourceHeight = 192,
            VisualMode = ActorVisualModes.CompositeRig,
            CompositeVisual = new RiggedSpriteVisualDescriptor(
                1,
                "humanoid_v1",
                "orc_v1",
                "fixed",
                "S",
                1,
                new Dictionary<string, string> { ["right_hand"] = "inventory_154_axe" })
        });
        var service = CreateService(repository, FindProjectClientAssetsRoot());

        var loaded = await service.LoadAsync("orc_001", TestContext.Current.CancellationToken);

        AssertSucceeded(loaded);
        var preview = Assert.IsType<RiggedSpritePreviewDefinition>(loaded.Value!.RiggedSpritePreview);
        Assert.EndsWith("maps/objects/mobs/orc.png", preview.BaseFilePath.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Equal(160, preview.SourceWidth);
        Assert.Equal(192, preview.SourceHeight);
        Assert.Equal("S", preview.Direction);
        Assert.Equal(1, preview.Frame);
        Assert.Equal("inventory_154_axe", Assert.Single(preview.Cosmetics).ItemId);
        Assert.Equal("right_hand_primary_grip", Assert.Single(preview.ForegroundOverlays).OverlayId);
    }

    [Fact]
    public async Task LoadOptionsIncludesRiggedSpriteAppearanceWhenCanonicalRigDataIsAvailable()
    {
        var result = await CreateService(
            new InMemoryMobRepository(),
            FindProjectClientAssetsRoot()).LoadOptionsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value!.ActorAppearance);
        Assert.Contains(result.Value.ActorAppearance!.VisualModes, mode => mode.Id == "composite_rig");
        Assert.Contains(result.Value.ActorAppearance.Rigs, rig => rig.RigId == "humanoid_v1");
    }

    [Fact]
    public void NormalizeDraftTrimsValuesAndZeroesDisabledTargeting()
    {
        var draft = MobAuthoringService.Normalize(
            " Green Slime ",
            " res://assets/maps/objects/mobs/slime.png ",
            128,
            88,
            0,
            0,
            0.25,
            1,
            1,
            12,
            1.25,
            MobAuthoringRegistry.DefaultMovementBehavior,
            MobAuthoringRegistry.DefaultWanderRadiusTiles,
            MobAuthoringRegistry.DefaultAggressionMode,
            MobAuthoringRegistry.DefaultAggressionRadiusTiles,
            MobAuthoringRegistry.DefaultLeashRadiusTiles,
            MobAuthoringRegistry.DefaultReturnHomeBehavior,
            " mobs ",
            false,
            6,
            600,
            8,
            DefaultProfile(),
            null,
            [new(2, " Apple ", 1)]);

        Assert.Equal("Green Slime", draft.DisplayName);
        Assert.Equal("res://assets/maps/objects/mobs/slime.png", draft.VisualTexturePath);
        Assert.Equal("mobs", draft.CombatFactionId);
        Assert.Equal(0, draft.MobDetectionRadiusTiles);
        Assert.Equal(0, draft.MobTargetScanIntervalMs);
        Assert.Equal(0, draft.MobTargetScanCandidateLimit);
        Assert.Equal(EquipmentCombatBonusDefinition.Zero, draft.CombatBonuses);
        Assert.Equal("apple", Assert.Single(draft.GuaranteedDrops).ItemId);
    }

    [Fact]
    public void NormalizeDraftCanonicalizesCombatProfileAndDropOrder()
    {
        var draft = MobAuthoringService.Normalize(
            "Goblin",
            "res://assets/maps/objects/mobs/goblin.png",
            128,
            156,
            0,
            0,
            0.25,
            1,
            1,
            20,
            1.25,
            " Random_Wander ",
            3,
            " Proactive ",
            4,
            6,
            " Return_To_Spawn ",
            "MOBS",
            true,
            4,
            600,
            16,
            new MobCombatProfileDefinition(" Melee ", " Crush ", 1, 1, 4, 2, 3, 1),
            EquipmentCombatBonusDefinition.Zero,
            [new(2, "iron_ore", 1), new(1, "Apple", 3)]);

        Assert.Equal("mobs", draft.CombatFactionId);
        Assert.Equal("random_wander", draft.MovementBehavior);
        Assert.Equal("proactive", draft.AggressionMode);
        Assert.Equal("return_to_spawn", draft.ReturnHomeBehavior);
        Assert.Equal("melee", draft.PrimaryCombatProfile?.AttackType);
        Assert.Equal("crush", draft.PrimaryCombatProfile?.AccuracyStyle);
        Assert.Equal([1, 2], draft.GuaranteedDrops.Select(drop => drop.DropOrder));
        Assert.Equal(["apple", "iron_ore"], draft.GuaranteedDrops.Select(drop => drop.ItemId));
    }

    [Fact]
    public void DuplicateDropOrdersAreRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateGuaranteedDrops(
            DraftWithDrops([new(0, "apple", 1), new(0, "iron_ore", 1)]),
            KnownDropItems(),
            messages,
            false);

        Assert.Contains(messages, message => message.Code == "duplicate_mob_drop_order");
    }

    [Fact]
    public void DuplicateDropItemsAreRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateGuaranteedDrops(
            DraftWithDrops([new(0, "apple", 1), new(1, " apple ", 2)]),
            KnownDropItems(),
            messages,
            false);

        Assert.Contains(messages, message => message.Code == "duplicate_mob_drop_item");
    }

    [Fact]
    public void InvalidVisualDimensionsAreRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateVisualFields(SampleDraft() with { SourceWidth = 0 }, messages);

        Assert.Contains(messages, message => message.Code == "invalid_mob_visual_dimensions");
    }

    [Fact]
    public void NonFiniteRenderScaleIsRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateVisualFields(
            SampleDraft() with { VisualRenderScale = double.PositiveInfinity },
            messages);

        Assert.Contains(messages, message => message.Code == "invalid_mob_visual_render_scale");
    }

    [Fact]
    public void NonFiniteMovementSpeedIsRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateFootprintHealthMovement(
            SampleDraft() with { MovementSpeedTilesPerSecond = double.NaN },
            messages);

        Assert.Contains(messages, message => message.Code == "invalid_mob_movement_speed");
    }

    [Fact]
    public void ProactiveTargetingRequiresFaction()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateFactionAndTargeting(
            SampleDraft() with
            {
                CanProactivelyTargetHostileMobs = true,
                CombatFactionId = null,
                MobDetectionRadiusTiles = 4,
                MobTargetScanIntervalMs = 600,
                MobTargetScanCandidateLimit = 8
            },
            new HashSet<string>(StringComparer.Ordinal),
            messages);

        Assert.Contains(messages, message => message.Code == "invalid_mob_proactive_targeting");
    }

    [Theory]
    [InlineData(0, 600, 8)]
    [InlineData(4, 0, 8)]
    [InlineData(4, 600, 0)]
    public void ProactiveTargetingRequiresPositiveScanValues(
        int radius,
        int intervalMs,
        int candidateLimit)
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateFactionAndTargeting(
            SampleDraft() with
            {
                CombatFactionId = "mobs",
                CanProactivelyTargetHostileMobs = true,
                MobDetectionRadiusTiles = radius,
                MobTargetScanIntervalMs = intervalMs,
                MobTargetScanCandidateLimit = candidateLimit
            },
            new HashSet<string>(["mobs"], StringComparer.Ordinal),
            messages);

        Assert.Contains(messages, message => message.Code == "invalid_mob_proactive_targeting");
    }

    [Fact]
    public void UnknownFactionIsRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateFactionAndTargeting(
            SampleDraft() with { CombatFactionId = "unknown" },
            new HashSet<string>(["mobs"], StringComparer.Ordinal),
            messages);

        Assert.Contains(messages, message => message.Code == "invalid_mob_faction");
    }

    [Fact]
    public void BehaviorValidationReportsSpecificLeashCoverageError()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateBehavior(
            SampleDraft() with
            {
                MovementBehavior = "random_wander",
                WanderRadiusTiles = 10,
                AggressionMode = "proactive",
                AggressionRadiusTiles = 5,
                LeashRadiusTiles = 6
            },
            messages);

        var message = Assert.Single(messages, message => message.Code == "inconsistent_mob_behavior_radii");
        Assert.Equal("leash_radius_tiles", message.Field);
        Assert.Contains("at least 10 logical tiles", message.Message);
    }

    [Fact]
    public void UnsupportedAttackTypeIsRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateCombatProfile(
            SampleDraft() with { PrimaryCombatProfile = DefaultProfile() with { AttackType = "magic" } },
            messages,
            false);

        Assert.Contains(messages, message => message.Code == "unsupported_mob_attack_type");
    }

    [Fact]
    public void UnsupportedAccuracyStyleIsRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateCombatProfile(
            SampleDraft() with { PrimaryCombatProfile = DefaultProfile() with { AccuracyStyle = "magic" } },
            messages,
            false);

        Assert.Contains(messages, message => message.Code == "unsupported_mob_accuracy_style");
    }

    [Fact]
    public void InvalidRangeIsRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateCombatProfile(
            SampleDraft() with { PrimaryCombatProfile = DefaultProfile() with { MinimumRangeTiles = 3, MaximumRangeTiles = 1 } },
            messages,
            false);

        Assert.Contains(messages, message => message.Code == "invalid_mob_attack_range");
    }

    [Fact]
    public void InvalidAttackSpeedIsRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateCombatProfile(
            SampleDraft() with { PrimaryCombatProfile = DefaultProfile() with { AttackSpeedUnits = 0 } },
            messages,
            false);

        Assert.Contains(messages, message => message.Code == "invalid_mob_attack_speed_units");
    }

    [Fact]
    public void NegativeCombatLevelsAreRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateCombatProfile(
            SampleDraft() with { PrimaryCombatProfile = DefaultProfile() with { AttackLevel = -1 } },
            messages,
            false);

        Assert.Contains(messages, message => message.Code == "invalid_mob_combat_level");
    }

    [Fact]
    public void PublicationIsStricterThanDraftForMissingCombatProfile()
    {
        var draft = SampleDraft() with { PrimaryCombatProfile = null };
        var draftMessages = new List<ApiError>();
        var publicationMessages = new List<ApiError>();

        MobDefinitionValidator.ValidateCombatProfile(draft, draftMessages, false);
        MobDefinitionValidator.ValidateCombatProfile(draft, publicationMessages, true);

        Assert.DoesNotContain(draftMessages, message => message.Severity == ValidationSeverity.Error);
        Assert.Contains(publicationMessages, message => message.Code == "mob_combat_profile_required");
    }

    [Fact]
    public void DraftValidityIgnoresPublicationOnlyErrors()
    {
        Assert.False(MobDefinitionValidator.IsDraftBlocking(new ApiError(
            "mob_combat_profile_required",
            "Published mob definitions require a primary melee combat profile.",
            ValidationSeverity.Error,
            "primary_combat_profile")));
        Assert.False(MobDefinitionValidator.IsDraftBlocking(new ApiError(
            "unpublished_mob_drop_item",
            "Drop item must be published before the mob can be published.",
            ValidationSeverity.Error,
            "guaranteed_drops[0]")));
        Assert.False(MobDefinitionValidator.IsDraftBlocking(new ApiError(
            "invalid_mob_combat_bonus",
            "Combat bonus exceeds the runtime-supported authoring range.",
            ValidationSeverity.Error,
            "combat_bonuses.attack_crush")));
    }

    [Theory]
    [InlineData("invalid_mob_definition_id")]
    [InlineData("invalid_mob_visual_texture_path")]
    [InlineData("inconsistent_mob_behavior_radii")]
    [InlineData("invalid_mob_drop_item")]
    public void DraftValidityBlocksErrorsThatCannotBePersisted(string code)
    {
        Assert.True(MobDefinitionValidator.IsDraftBlocking(new ApiError(
            code,
            "Persistence-shape error.",
            ValidationSeverity.Error)));
    }

    [Fact]
    public void UnpublishedDropItemBlocksPublication()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateGuaranteedDrops(
            DraftWithDrops([new(0, "draft_item", 1)]),
            KnownDropItems(),
            messages,
            true);

        Assert.Contains(messages, message => message.Code == "unpublished_mob_drop_item");
    }

    [Fact]
    public void MissingDropItemIsRejected()
    {
        var messages = new List<ApiError>();
        MobDefinitionValidator.ValidateGuaranteedDrops(
            DraftWithDrops([new(0, "missing_item", 1)]),
            KnownDropItems(),
            messages,
            false);

        Assert.Contains(messages, message => message.Code == "invalid_mob_drop_item");
    }

    [Fact]
    public void PreviewSignatureIsDeterministic()
    {
        var expected = DateTimeOffset.Parse("2026-08-02T12:00:00Z");
        var draft = SampleDraft();

        var first = MobAuthoringService.ComputePreviewSignature("slime", "save_draft", draft, expected);
        var second = MobAuthoringService.ComputePreviewSignature("slime", "save_draft", draft, expected);

        Assert.Equal(first, second);
    }

    [Fact]
    public void PreviewSignatureChangesWithOperation()
    {
        var draft = SampleDraft();

        Assert.NotEqual(
            MobAuthoringService.ComputePreviewSignature("slime", "save_draft", draft, null),
            MobAuthoringService.ComputePreviewSignature("slime", "publish", draft, null));
    }

    [Fact]
    public void PreviewSignatureChangesWithAggregateContent()
    {
        Assert.NotEqual(
            MobAuthoringService.ComputePreviewSignature("slime", "save_draft", SampleDraft(), null),
            MobAuthoringService.ComputePreviewSignature("slime", "save_draft", SampleDraft() with { MaxHealth = 99 }, null));
    }

    [Fact]
    public async Task PreviewAndLoadedDefinitionsExposeReadOnlyDerivedCombatLevelAndInnateBonusDiagnostics()
    {
        var repository = new InMemoryMobRepository();
        repository.Put(Record() with
        {
            MobDefinitionId = "training_guard",
            MaxHealth = 10,
            PrimaryCombatProfile = new MobCombatProfileDefinition("melee", "slash", 1, 1, 4, 4, 3, 3),
            CombatBonuses = new EquipmentCombatBonusDefinition(
                AttackThrust: 0,
                AttackSlash: 64,
                AttackCrush: 0,
                AttackRanged: 0,
                AttackMagic: 0,
                StrengthMelee: 64,
                StrengthRanged: 0,
                StrengthMagic: 0,
                DefenceThrust: 0,
                DefenceSlash: 64,
                DefenceCrush: 128,
                DefenceRanged: 0,
                DefenceMagic: 0)
        });
        var service = CreateService(repository, Path.GetTempPath());

        var preview = await service.PreviewAsync(
            "training_guard",
            ToPreviewRequest(repository.Records["training_guard"], "save_draft"),
            TestContext.Current.CancellationToken);
        var loaded = await service.LoadAsync(
            "training_guard",
            TestContext.Current.CancellationToken);

        AssertSucceeded(preview);
        Assert.Equal(5, preview.Value!.DerivedCombatLevel);
        Assert.NotNull(preview.Value.CombatLevelDiagnostics);
        Assert.Equal("slash", preview.Value.CombatLevelDiagnostics!.SelectedAccuracyStyle);
        Assert.Equal(17d, preview.Value.CombatLevelDiagnostics.EquivalentAttackLevel);
        Assert.Equal(15d, preview.Value.CombatLevelDiagnostics.EquivalentStrengthLevel);
        Assert.Equal(27d, preview.Value.CombatLevelDiagnostics.EquivalentDefenceCrushLevel);
        AssertSucceeded(loaded);
        Assert.Equal(5, loaded.Value!.DerivedCombatLevel);
        Assert.NotNull(loaded.Value.CombatLevelDiagnostics);
        Assert.Equal(preview.Value.CombatLevelDiagnostics, loaded.Value.CombatLevelDiagnostics);
    }

    [Fact]
    public void MatchingPreviewRejectsMissingSignature()
    {
        Assert.False(MobAuthoringService.IsMatchingPreview(
            "slime",
            "save_draft",
            SampleDraft(),
            null,
            null));
    }

    [Fact]
    public void VersionConflictRequiresTokenForExistingDefinition()
    {
        Assert.True(MobAuthoringService.HasVersionConflict(Record(), null));
    }

    [Fact]
    public void VersionConflictRejectsStaleToken()
    {
        Assert.True(MobAuthoringService.HasVersionConflict(
            Record(),
            DateTimeOffset.Parse("2026-08-02T11:00:00Z")));
    }

    [Fact]
    public void VersionConflictAllowsNewDefinitionWithoutToken()
    {
        Assert.False(MobAuthoringService.HasVersionConflict(null, null));
    }

    [Fact]
    public void VersionConflictRejectsCreateWithFabricatedToken()
    {
        Assert.True(MobAuthoringService.HasVersionConflict(
            null,
            DateTimeOffset.Parse("2026-08-02T12:00:00Z")));
    }

    [Fact]
    public void SemanticEquivalenceIgnoresReloadTimestamp()
    {
        var first = Record();
        var second = first with { UpdatedAtUtc = first.UpdatedAtUtc.AddMinutes(5) };

        Assert.True(MobAuthoringService.Equivalent(first, second));
    }

    [Fact]
    public void SemanticEquivalenceDetectsChildChanges()
    {
        var first = Record();
        var second = first with { GuaranteedDrops = [new MobDropDefinition(0, "iron_ore", "Iron Ore", 1)] };

        Assert.False(MobAuthoringService.Equivalent(first, second));
    }

    [Fact]
    public void CompositeRigRecordRoundTripPreservesCanonicalActorPoseDescriptor()
    {
        var descriptor = new RiggedSpriteVisualDescriptor(
            1,
            "humanoid_v1",
            null,
            "actor_pose",
            null,
            null,
            new Dictionary<string, string> { ["right_hand"] = "inventory_154_axe" });
        var persisted = JsonSerializer.Deserialize<RiggedSpriteVisualDescriptor>(JsonSerializer.Serialize(descriptor));

        var roundTripped = MobAuthoringService.FromRecord(Record() with
        {
            VisualMode = ActorVisualModes.CompositeRig,
            CompositeVisual = persisted
        });

        Assert.Equal(ActorVisualModes.CompositeRig, roundTripped.VisualMode);
        Assert.True(RiggedSpriteVisualDescriptorNormalizer.Equivalent(descriptor, roundTripped.CompositeVisual));
        Assert.Null(roundTripped.CompositeVisual!.FixedDirection);
        Assert.Null(roundTripped.CompositeVisual.FixedFrame);
    }

    [Fact]
    public async Task PublishTriggersMobRuntimeCatalogRefreshOnly()
    {
        var assetsRoot = Path.Combine(
            Path.GetTempPath(),
            "mmo-content-studio-mob-assets",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(assetsRoot, "maps", "objects", "mobs"));
        await File.WriteAllBytesAsync(
            Path.Combine(assetsRoot, "maps", "objects", "mobs", "slime.png"),
            [0x89, 0x50, 0x4E, 0x47],
            TestContext.Current.CancellationToken);

        try
        {
            var repository = new InMemoryMobRepository();
            repository.Put(Record());
            var publisher = new TestRuntimeCatalogPublisher();
            var service = CreateService(repository, assetsRoot, publisher);

            var preview = await service.PreviewAsync(
                "slime",
                ToPreviewRequest(repository.Records["slime"], "publish"),
                TestContext.Current.CancellationToken);
            AssertSucceeded(preview);

            var publish = await service.PublishAsync(
                "slime",
                new MobPublicationRequest(
                    repository.Records["slime"].UpdatedAtUtc,
                    preview.Value!.PreviewSignature),
                TestContext.Current.CancellationToken);

            AssertSucceeded(publish);
            Assert.Equal("Published", repository.Records["slime"].PublicationState);
            Assert.Equal([RuntimeCatalogPublicationScope.Mob], publisher.PublishScopes);
        }
        finally
        {
            if (Directory.Exists(assetsRoot))
            {
                Directory.Delete(assetsRoot, true);
            }
        }
    }

    private static NormalizedMobDraft SampleDraft() =>
        MobAuthoringService.Normalize(
            "Slime",
            "res://assets/maps/objects/mobs/slime.png",
            128,
            88,
            0,
            0,
            0.25,
            1,
            1,
            12,
            1.25,
            MobAuthoringRegistry.DefaultMovementBehavior,
            MobAuthoringRegistry.DefaultWanderRadiusTiles,
            MobAuthoringRegistry.DefaultAggressionMode,
            MobAuthoringRegistry.DefaultAggressionRadiusTiles,
            MobAuthoringRegistry.DefaultLeashRadiusTiles,
            MobAuthoringRegistry.DefaultReturnHomeBehavior,
            null,
            false,
            0,
            0,
            0,
            DefaultProfile(),
            EquipmentCombatBonusDefinition.Zero,
            []);

    private static NormalizedMobDraft DraftWithDrops(IReadOnlyList<MobDropDraft> drops) =>
        SampleDraft() with { GuaranteedDrops = MobDomainRules.NormalizeGuaranteedDrops(drops) };

    private static MobCombatProfileDefinition DefaultProfile() =>
        new("melee", "crush", 1, 1, 4, 1, 1, 1);

    private static IReadOnlyDictionary<string, MobDropItemRecord> KnownDropItems() =>
        new Dictionary<string, MobDropItemRecord>(StringComparer.Ordinal)
        {
            ["apple"] = new("apple", "Apple", true),
            ["iron_ore"] = new("iron_ore", "Iron Ore", true),
            ["draft_item"] = new("draft_item", "Draft Item", false)
        };

    private static MobDefinitionRecord Record() =>
        new(
            "slime",
            "Slime",
            "Draft",
            "res://assets/maps/objects/mobs/slime.png",
            128,
            88,
            0,
            0,
            0.25,
            1,
            1,
            12,
            1.25,
            MobAuthoringRegistry.DefaultMovementBehavior,
            MobAuthoringRegistry.DefaultWanderRadiusTiles,
            MobAuthoringRegistry.DefaultAggressionMode,
            MobAuthoringRegistry.DefaultAggressionRadiusTiles,
            MobAuthoringRegistry.DefaultLeashRadiusTiles,
            MobAuthoringRegistry.DefaultReturnHomeBehavior,
            null,
            null,
            false,
            0,
            0,
            0,
            DefaultProfile(),
            EquipmentCombatBonusDefinition.Zero,
            [],
            true,
            0,
            DateTimeOffset.Parse("2026-08-02T12:00:00Z"));

    private static MobPreviewRequest ToPreviewRequest(MobDefinitionRecord record, string operation) =>
        new(
            record.DisplayName,
            record.VisualTexturePath,
            record.SourceWidth,
            record.SourceHeight,
            record.VisualAnchorOffsetX,
            record.VisualAnchorOffsetY,
            record.VisualRenderScale,
            record.FootprintWidthTiles,
            record.FootprintHeightTiles,
            record.MaxHealth,
            record.MovementSpeedTilesPerSecond,
            record.MovementBehavior,
            record.WanderRadiusTiles,
            record.AggressionMode,
            record.AggressionRadiusTiles,
            record.LeashRadiusTiles,
            record.ReturnHomeBehavior,
            record.CombatFactionId,
            record.CanProactivelyTargetHostileMobs,
            record.MobDetectionRadiusTiles,
            record.MobTargetScanIntervalMs,
            record.MobTargetScanCandidateLimit,
            record.PrimaryCombatProfile,
            record.CombatBonuses,
            record.GuaranteedDrops
                .Select(drop => new MobDropDraft(drop.DropOrder, drop.ItemId, drop.StackCount))
                .ToArray(),
            record.UpdatedAtUtc,
            operation);

    private static MobAuthoringService CreateService(
        IMobRepository repository,
        string assetsRoot,
        IRuntimeCatalogPublisher? runtimeCatalogPublisher = null)
    {
        var assetService = new ItemAssetService(Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["game_client_assets"] = assetsRoot
            }
        }));
        var catalogService = new ActorAppearanceCatalogService(Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["game_client_assets"] = assetsRoot
            }
        }));
        var validator = new MobDefinitionValidator(repository, assetService, catalogService);
        return new MobAuthoringService(
            repository,
            validator,
            new MobAuthoringRegistry(),
            assetService,
            catalogService,
            new RiggedSpritePreviewResolver(catalogService, assetService),
            NullLogger<MobAuthoringService>.Instance,
            runtimeCatalogPublisher);
    }

    private static string FindProjectClientAssetsRoot()
    {
        for (var current = new DirectoryInfo(Directory.GetCurrentDirectory()); current is not null; current = current.Parent)
        {
            var assetsRoot = Path.Combine(current.FullName, "prototype", "client", "assets");
            if (Directory.Exists(assetsRoot))
            {
                return assetsRoot;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the MMO Project client assets directory.");
    }

    private static void AssertSucceeded<T>(AuthoringOperationResult<T> result) =>
        Assert.True(
            result.Succeeded,
            string.Join("; ", result.Errors.Select(error => $"{error.Code}:{error.Field}:{error.Message}:{error.Remediation}")));

    private sealed class InMemoryMobRepository : IMobRepository
    {
        private DateTimeOffset _clock = DateTimeOffset.Parse("2026-08-02T12:00:00Z");

        public Dictionary<string, MobDefinitionRecord> Records { get; } = new(StringComparer.Ordinal);

        public void Put(MobDefinitionRecord record) => Records[record.MobDefinitionId] = record;

        public Task<IReadOnlyList<MobDefinitionRecord>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MobDefinitionRecord>>(Records.Values.ToArray());

        public Task<MobDefinitionRecord?> LoadAsync(
            string mobDefinitionId,
            CancellationToken cancellationToken = default)
        {
            Records.TryGetValue(mobDefinitionId, out var record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<MobFactionRecord>> LoadFactionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MobFactionRecord>>(
            [
                new("mobs", "Mobs")
            ]);

        public Task<IReadOnlyList<MobDropItemRecord>> LoadDropItemsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MobDropItemRecord>>(KnownDropItems().Values.ToArray());

        public Task<MobDefinitionRecord> SaveDraftAsync(
            string mobDefinitionId,
            NormalizedMobDraft draft,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Records.TryGetValue(mobDefinitionId, out var existing);
            EnsureExpectedVersion(mobDefinitionId, existing, expectedUpdatedAtUtc);
            var publicationState = existing?.PublicationState == "Published" ? "Draft" : existing?.PublicationState ?? "Draft";
            var saved = ToRecord(mobDefinitionId, draft, publicationState, NextTimestamp());
            Records[mobDefinitionId] = saved;
            return Task.FromResult(saved);
        }

        public Task<MobDefinitionRecord> SetPublicationAsync(
            string mobDefinitionId,
            string publicationState,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!Records.TryGetValue(mobDefinitionId, out var existing))
            {
                throw new MobDefinitionNotFoundException(mobDefinitionId);
            }

            EnsureExpectedVersion(mobDefinitionId, existing, expectedUpdatedAtUtc);
            var saved = existing with
            {
                PublicationState = publicationState,
                UpdatedAtUtc = existing.PublicationState == publicationState ? existing.UpdatedAtUtc : NextTimestamp()
            };
            Records[mobDefinitionId] = saved;
            return Task.FromResult(saved);
        }

        public Task DeleteAsync(
            string mobDefinitionId,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!Records.TryGetValue(mobDefinitionId, out var existing))
            {
                throw new MobDefinitionNotFoundException(mobDefinitionId);
            }

            EnsureExpectedVersion(mobDefinitionId, existing, expectedUpdatedAtUtc);
            Records.Remove(mobDefinitionId);
            return Task.CompletedTask;
        }

        private DateTimeOffset NextTimestamp()
        {
            _clock = _clock.AddMinutes(1);
            return _clock;
        }

        private static void EnsureExpectedVersion(
            string mobDefinitionId,
            MobDefinitionRecord? existing,
            DateTimeOffset? expectedUpdatedAtUtc)
        {
            if (existing is null)
            {
                return;
            }

            if (expectedUpdatedAtUtc is null
                || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
            {
                throw new MobDefinitionConcurrencyException(mobDefinitionId, existing.UpdatedAtUtc);
            }
        }

        private static MobDefinitionRecord ToRecord(
            string mobDefinitionId,
            NormalizedMobDraft draft,
            string publicationState,
            DateTimeOffset updatedAtUtc)
        {
            var dropDefinitions = draft.GuaranteedDrops
                .Select(drop =>
                {
                    KnownDropItems().TryGetValue(drop.ItemId, out var known);
                    return new MobDropDefinition(
                        drop.DropOrder,
                        drop.ItemId,
                        known?.DisplayName ?? drop.ItemId,
                        drop.StackCount);
                })
                .OrderBy(drop => drop.DropOrder)
                .ToArray();
            return new MobDefinitionRecord(
                mobDefinitionId,
                draft.DisplayName,
                publicationState,
                draft.VisualTexturePath,
                draft.SourceWidth,
                draft.SourceHeight,
                draft.VisualAnchorOffsetX,
                draft.VisualAnchorOffsetY,
                draft.VisualRenderScale,
                draft.FootprintWidthTiles,
                draft.FootprintHeightTiles,
                draft.MaxHealth,
                draft.MovementSpeedTilesPerSecond,
                draft.MovementBehavior,
                draft.WanderRadiusTiles,
                draft.AggressionMode,
                draft.AggressionRadiusTiles,
                draft.LeashRadiusTiles,
                draft.ReturnHomeBehavior,
                draft.CombatFactionId,
                draft.CombatFactionId is null ? null : "Mobs",
                draft.CanProactivelyTargetHostileMobs,
                draft.MobDetectionRadiusTiles,
                draft.MobTargetScanIntervalMs,
                draft.MobTargetScanCandidateLimit,
                draft.PrimaryCombatProfile,
                draft.CombatBonuses,
                dropDefinitions,
                draft.PrimaryCombatProfile is not null,
                dropDefinitions.Length,
                updatedAtUtc,
                draft.VisualMode,
                CloneCompositeVisual(draft.CompositeVisual));
        }

        private static RiggedSpriteVisualDescriptor? CloneCompositeVisual(
            RiggedSpriteVisualDescriptor? compositeVisual) =>
            compositeVisual is null
                ? null
                : JsonSerializer.Deserialize<RiggedSpriteVisualDescriptor>(JsonSerializer.Serialize(compositeVisual));
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
}
