using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class NpcDomainRulesTests
{
    [Theory]
    [InlineData(" Test_Npc ", "test_npc", true)]
    [InlineData("fisherman_001", "fisherman_001", true)]
    [InlineData("1_test_npc", "1_test_npc", false)]
    [InlineData("test-npc", "test-npc", false)]
    [InlineData("test__npc", "test__npc", false)]
    [InlineData(" ", "", false)]
    public void NormalizeStableIdKeepsRuntimeSafeIdentifierShape(
        string value,
        string expected,
        bool supported)
    {
        Assert.Equal(expected, NpcDomainRules.NormalizeStableId(value));
        Assert.Equal(supported, NpcDomainRules.IsStableId(value));
    }

    [Theory]
    [InlineData("draft", "Draft")]
    [InlineData("Published", "Published")]
    [InlineData("disabled", "Disabled")]
    public void NormalizePublicationStatePreservesPersistedCasing(
        string value,
        string expected)
    {
        Assert.Equal(expected, NpcDomainRules.NormalizePublicationState(value));
        Assert.True(NpcDomainRules.IsSupportedPublicationState(value));
    }

    [Fact]
    public void RegistryExposesLockedT5BValues()
    {
        var registry = new NpcAuthoringRegistry();

        Assert.Equal(600, NpcAuthoringRegistry.MinimumTickIntervalMilliseconds);
        Assert.Equal(1, NpcAuthoringRegistry.MinimumInteractionRangeTiles);
        Assert.Equal(1, NpcAuthoringRegistry.InitialFootprintWidthTiles);
        Assert.Equal(1, NpcAuthoringRegistry.InitialFootprintHeightTiles);
        Assert.Equal(["Draft", "Published", "Disabled"], registry.LoadPublicationStates().Select(option => option.Id));
        Assert.Equal(["static", "random_wander"], registry.LoadMovementBehaviors().Select(option => option.Id));
        Assert.Equal(["talk"], registry.LoadInteractionTypes().Select(option => option.Id));
        Assert.Empty(registry.LoadDialogueReferences());
        Assert.False(registry.CanValidateDialogueReferences);
        Assert.Equal("static", registry.Defaults.MovementBehavior);
        Assert.Equal("talk", registry.Defaults.DefaultInteraction);
        Assert.Equal(0.25, registry.Defaults.VisualRenderScale);
    }

    [Fact]
    public void StaticMovementZeroesWanderRadiusDuringNormalization()
    {
        var draft = CreateDraft(
            movementBehavior: " static ",
            wanderRadiusTiles: 7);

        var normalized = NpcDomainRules.NormalizeDraft(draft);

        Assert.Equal("static", normalized.MovementBehavior);
        Assert.Equal(0, normalized.WanderRadiusTiles);
    }

    [Fact]
    public void RandomWanderPreservesPositiveWanderRadiusDuringNormalization()
    {
        var draft = CreateDraft(
            movementBehavior: " Random_Wander ",
            wanderRadiusTiles: 5);

        var normalized = NpcDomainRules.NormalizeDraft(draft);

        Assert.Equal("random_wander", normalized.MovementBehavior);
        Assert.Equal(5, normalized.WanderRadiusTiles);
    }

    [Fact]
    public void DisabledInteractionClearsDialogueId()
    {
        var draft = CreateDraft(
            interactionEnabled: false,
            defaultDialogueId: " test_npc_greeting ");

        var normalized = NpcDomainRules.NormalizeDraft(draft);

        Assert.False(normalized.InteractionEnabled);
        Assert.Null(normalized.DefaultDialogueId);
    }

    [Fact]
    public void EnabledInteractionTrimsDialogueId()
    {
        var draft = CreateDraft(
            interactionEnabled: true,
            defaultDialogueId: " test_npc_greeting ");

        var normalized = NpcDomainRules.NormalizeDraft(draft);

        Assert.True(normalized.InteractionEnabled);
        Assert.Equal("test_npc_greeting", normalized.DefaultDialogueId);
    }

    [Fact]
    public void OptionalStringsTrimAndBlankToNull()
    {
        Assert.Equal("hello", NpcDomainRules.NormalizeOptional(" hello "));
        Assert.Null(NpcDomainRules.NormalizeOptional("   "));
        Assert.Null(NpcDomainRules.NormalizeOptional(null));

        var normalized = NpcDomainRules.NormalizeDraft(CreateDraft(notes: "  "));

        Assert.Null(normalized.Notes);
    }

    [Theory]
    [InlineData("static", 0, true)]
    [InlineData("static", 1, false)]
    [InlineData("random_wander", 1, true)]
    [InlineData("random_wander", 0, false)]
    public void MovementConsistencyIsValidatedSeparatelyFromNormalization(
        string movementBehavior,
        int wanderRadiusTiles,
        bool expected)
    {
        Assert.Equal(
            expected,
            NpcDomainRules.IsMovementConsistent(movementBehavior, wanderRadiusTiles));
    }

    [Fact]
    public void SemanticComparisonInputIsDeterministicAfterNormalization()
    {
        var first = CreateDraft(
            displayName: " Test NPC ",
            movementBehavior: "STATIC",
            wanderRadiusTiles: 8,
            interactionEnabled: false,
            defaultDialogueId: "test_npc_greeting",
            notes: " ");
        var second = CreateDraft(
            displayName: "Test NPC",
            movementBehavior: "static",
            wanderRadiusTiles: 0,
            interactionEnabled: false,
            defaultDialogueId: null,
            notes: null);

        Assert.Equal(
            NpcDomainRules.BuildSemanticComparisonInput(second),
            NpcDomainRules.BuildSemanticComparisonInput(first));
    }

    private static NpcDraft CreateDraft(
        string displayName = "Test NPC",
        string visualTexturePath = "res://assets/actors/npcs/Chars_139_200-F2-S.png",
        string movementBehavior = "static",
        int wanderRadiusTiles = 0,
        bool interactionEnabled = true,
        string? defaultDialogueId = "test_npc_greeting",
        string? notes = "note") =>
        new(
            displayName,
            visualTexturePath,
            32,
            32,
            0,
            0,
            0.25,
            1,
            1,
            movementBehavior,
            wanderRadiusTiles,
            600,
            0.15,
            interactionEnabled,
            1,
            "talk",
            defaultDialogueId,
            notes,
            null,
            null);
}
