using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class MobDomainRulesTests
{
    [Theory]
    [InlineData(" Green_Slime ", "green_slime", true)]
    [InlineData("green_slime_2", "green_slime_2", true)]
    [InlineData("green-slime", "green-slime", false)]
    [InlineData("green__slime", "green__slime", false)]
    [InlineData(" ", "", false)]
    public void NormalizeStableIdKeepsRuntimeSafeIdentifierShape(
        string value,
        string expected,
        bool supported)
    {
        Assert.Equal(expected, MobDomainRules.NormalizeStableId(value));
        Assert.Equal(supported, MobDomainRules.IsStableId(value));
    }

    [Theory]
    [InlineData("draft", "Draft")]
    [InlineData("Published", "Published")]
    [InlineData("disabled", "Disabled")]
    public void NormalizePublicationStatePreservesPersistedCasing(
        string value,
        string expected)
    {
        Assert.Equal(expected, MobDomainRules.NormalizePublicationState(value));
        Assert.True(MobDomainRules.IsSupportedPublicationState(value));
    }

    [Fact]
    public void RegistryExposesLockedT4AValues()
    {
        var registry = new MobAuthoringRegistry();

        Assert.Equal(600, MobAuthoringRegistry.CombatUnitMilliseconds);
        Assert.Equal(["Draft", "Published", "Disabled"], registry.LoadPublicationStates().Select(option => option.Id));
        Assert.Equal(["melee"], registry.LoadAttackTypes().Select(option => option.Id));
        Assert.Equal(["thrust", "slash", "crush"], registry.LoadAccuracyStyles().Select(option => option.Id));
        Assert.Equal(["hostile", "neutral"], registry.LoadFactionDispositions().Select(option => option.Id));
        Assert.Equal("melee", registry.Defaults.AttackType);
        Assert.Equal("crush", registry.Defaults.AccuracyStyle);
        Assert.Equal(4, registry.Defaults.AttackSpeedUnits);
        Assert.Equal(1.25, registry.Defaults.MovementSpeedTilesPerSecond);
        Assert.Equal(0.25, registry.Defaults.VisualRenderScale);
        Assert.False(registry.Defaults.CanProactivelyTargetHostileMobs);
    }

    [Theory]
    [InlineData(1, 1, 1, 5, 2)]
    [InlineData(3, 3, 2, 8, 4)]
    [InlineData(4, 3, 3, 10, 5)]
    [InlineData(99, 99, 99, 99, 113)]
    public void DerivedMobCombatLevelUsesApprovedIntegerFormula(
        int attack,
        int strength,
        int defence,
        int maxHealth,
        int expected)
    {
        Assert.Equal(expected, MobDomainRules.CalculateDerivedCombatLevel(
            attack,
            strength,
            defence,
            maxHealth));
    }

    [Theory]
    [InlineData("melee", true)]
    [InlineData("ranged", false)]
    public void AttackTypeRegistryIsNarrow(string attackType, bool supported)
    {
        Assert.Equal(supported, MobDomainRules.IsSupportedAttackType(attackType));
    }

    [Theory]
    [InlineData("thrust", true)]
    [InlineData("slash", true)]
    [InlineData("crush", true)]
    [InlineData(null, false)]
    [InlineData("magic", false)]
    public void AccuracyStyleRegistryIsNarrow(string? accuracyStyle, bool supported)
    {
        Assert.Equal(supported, MobDomainRules.IsSupportedAccuracyStyle(accuracyStyle));
    }

    [Theory]
    [InlineData(false, null, 0, 0, 0, true)]
    [InlineData(false, null, 4, 0, 0, true)]
    [InlineData(true, "mobs", 4, 600, 16, true)]
    [InlineData(true, null, 4, 600, 16, false)]
    [InlineData(true, "mobs", 0, 600, 16, false)]
    [InlineData(true, "mobs", 4, 0, 16, false)]
    [InlineData(true, "mobs", 4, 600, 0, false)]
    public void ProactiveTargetingRequiresFactionAndPositiveScanValues(
        bool proactive,
        string? factionId,
        int radius,
        int intervalMs,
        int candidateLimit,
        bool expected)
    {
        Assert.Equal(
            expected,
            MobDomainRules.IsProactiveTargetingConsistent(
                proactive,
                factionId,
                radius,
                intervalMs,
                candidateLimit));
    }

    [Fact]
    public void NormalizeGuaranteedDropsSortsAndCanonicalizesIds()
    {
        var drops = MobDomainRules.NormalizeGuaranteedDrops(
        [
            new(2, " Apple ", 1),
            new(1, "iron_ore", 4),
            new(1, "Coal", 2)
        ]);

        Assert.Equal([1, 1, 2], drops.Select(drop => drop.DropOrder));
        Assert.Equal(["coal", "iron_ore", "apple"], drops.Select(drop => drop.ItemId));
    }

    [Fact]
    public void DuplicateDropChecksUsePersistedIdentity()
    {
        var drops = new[]
        {
            new MobDropDraft(0, "apple", 1),
            new MobDropDraft(0, "iron_ore", 1),
            new MobDropDraft(2, " Apple ", 1)
        };

        Assert.True(MobDomainRules.HasDuplicateDropOrders(drops));
        Assert.True(MobDomainRules.HasDuplicateDropItems(drops));
    }
}
