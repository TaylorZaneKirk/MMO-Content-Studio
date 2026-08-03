using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class MobAuthoringServiceTests
{
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
}
