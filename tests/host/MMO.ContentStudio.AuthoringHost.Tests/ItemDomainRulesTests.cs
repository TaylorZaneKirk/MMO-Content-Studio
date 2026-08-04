using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class ItemDomainRulesTests
{
    [Fact]
    public void NormalizeToolCapabilitiesSortsAndTrimsCapabilities()
    {
        var capabilities = ItemDomainRules.NormalizeToolCapabilities(
        [
            new(" mining ", 2, " swing ", null),
            new("fishing", 1, null, " splash ")
        ]);

        Assert.Equal(["fishing", "mining"], capabilities.Select(value => value.CapabilityId));
        Assert.Equal("swing", capabilities[1].ActionAnimationId);
        Assert.Equal("splash", capabilities[0].EffectResourceId);
    }

    [Fact]
    public void NormalizeToolCapabilitiesPreservesDuplicateCanonicalIdsForValidation()
    {
        var capabilities = ItemDomainRules.NormalizeToolCapabilities(
        [
            new(" mining ", 1, null, null),
            new("mining", 2, null, null)
        ]);

        Assert.Equal(2, capabilities.Count);
        Assert.All(capabilities, value => Assert.Equal("mining", value.CapabilityId));
        Assert.True(ItemDomainRules.HasDuplicateToolCapabilities(capabilities));
    }

    [Fact]
    public void NormalizeToolCapabilitiesTreatsBlankOptionalReferencesAsNull()
    {
        var capability = Assert.Single(ItemDomainRules.NormalizeToolCapabilities(
        [
            new("mining", 1, "   ", "\t")
        ]));

        Assert.Null(capability.ActionAnimationId);
        Assert.Null(capability.EffectResourceId);
    }

    [Fact]
    public void HasDuplicateToolCapabilitiesUsesCanonicalIds()
    {
        var capabilities = ItemDomainRules.NormalizeToolCapabilities(
        [
            new("mining", 1, null, null),
            new(" mining ", 2, null, null)
        ]);

        Assert.True(ItemDomainRules.HasDuplicateToolCapabilities(capabilities));
    }

    [Theory]
    [InlineData(true, false, "Weapon")]
    [InlineData(false, true, "Tool")]
    [InlineData(true, true, "Weapon + Tool")]
    [InlineData(false, false, "Equipment")]
    public void ClassifyDerivesFeatureOwnedLabels(
        bool weapon,
        bool tool,
        string expected)
    {
        var profile = weapon
            ? new EquipmentCombatProfileDefinition("test_profile", "melee", "slash", 1, 1, 4)
            : null;
        var tools = tool
            ? new[]
            {
                new ItemToolCapabilityDefinition("mining", "Mining", 0, 1, null, null)
            }
            : [];

        Assert.Equal(
            expected,
            ItemDomainRules.Classify(false, "right_hand", profile, tools));
    }

    [Fact]
    public void ClassifyKeepsConsumablesOwnedByConsumablesWorkspace()
    {
        var profile = new EquipmentCombatProfileDefinition("test_profile", "melee", "slash", 1, 1, 4);
        var tools = new[]
        {
            new ItemToolCapabilityDefinition("mining", "Mining", 0, 1, null, null)
        };

        Assert.Equal(
            "Consumable",
            ItemDomainRules.Classify(true, "right_hand", profile, tools));
    }

    [Fact]
    public void ClassifyDoesNotInferWeaponOrToolFromHandSlotAlone()
    {
        Assert.Equal(
            "Equipment",
            ItemDomainRules.Classify(false, "right_hand", null, []));
        Assert.Equal(
            "Equipment",
            ItemDomainRules.Classify(false, "left_hand", null, []));
    }

    [Fact]
    public void ClassifyTreatsToolCapabilitiesAsItemLevelSpecialization()
    {
        var tools = new[]
        {
            new ItemToolCapabilityDefinition("mining", "Mining", 0, 1, null, null)
        };

        Assert.Equal(
            "Tool",
            ItemDomainRules.Classify(false, null, null, tools));
    }

    [Fact]
    public void ActiveRuntimeWeaponRequiresRightHandProfile()
    {
        var profile = new EquipmentCombatProfileDefinition("test_profile", "melee", "slash", 1, 1, 4);

        Assert.True(ItemDomainRules.IsActiveRuntimeWeapon("right_hand", profile));
        Assert.False(ItemDomainRules.IsActiveRuntimeWeapon("left_hand", profile));
        Assert.False(ItemDomainRules.IsActiveRuntimeWeapon("right_hand", null));
        Assert.False(ItemDomainRules.IsActiveRuntimeWeapon(null, profile));
    }

    [Theory]
    [InlineData(" right_hand ", "right_hand")]
    [InlineData(" mining ", "mining")]
    [InlineData("\t", "")]
    public void NormalizeRequiredTrimsPersistedIdentifiers(string value, string expected)
    {
        Assert.Equal(expected, ItemDomainRules.NormalizeRequired(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeOptionalReturnsNullForMissingValues(string? value)
    {
        Assert.Null(ItemDomainRules.NormalizeOptional(value));
    }

    [Fact]
    public void RegistryExposesOnlyRuntimePersistableWeaponTaxonomy()
    {
        var registry = new ItemAuthoringRegistry();

        Assert.Equal(["melee"], registry.LoadAttackFamilies().Select(option => option.Id));
        Assert.Equal(["thrust", "slash", "crush"], registry.LoadAttackStyles().Select(option => option.Id));
        Assert.Contains("mining", registry.DefaultToolCapabilityIds);
        Assert.DoesNotContain("left_hand", registry.LoadAttackFamilies().Select(option => option.Id));
    }
}
