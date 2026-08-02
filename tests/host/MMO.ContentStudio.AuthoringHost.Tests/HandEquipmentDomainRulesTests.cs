using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class HandEquipmentDomainRulesTests
{
    [Fact]
    public void NormalizeToolCapabilitiesSortsAndTrimsCapabilities()
    {
        var capabilities = HandEquipmentDomainRules.NormalizeToolCapabilities(
        [
            new(" mining ", 2, " swing ", null),
            new("fishing", 1, null, " splash ")
        ]);

        Assert.Equal(["fishing", "mining"], capabilities.Select(value => value.CapabilityId));
        Assert.Equal("swing", capabilities[1].ActionAnimationId);
        Assert.Equal("splash", capabilities[0].EffectResourceId);
    }

    [Fact]
    public void HasDuplicateToolCapabilitiesUsesCanonicalIds()
    {
        var capabilities = HandEquipmentDomainRules.NormalizeToolCapabilities(
        [
            new("mining", 1, null, null),
            new(" mining ", 2, null, null)
        ]);

        Assert.True(HandEquipmentDomainRules.HasDuplicateToolCapabilities(capabilities));
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
                new HandEquipmentToolCapabilityDefinition("mining", "Mining", 0, 1, null, null)
            }
            : [];

        Assert.Equal(
            expected,
            HandEquipmentDomainRules.Classify(false, "right_hand", profile, tools));
    }

    [Fact]
    public void ActiveRuntimeWeaponRequiresRightHandProfile()
    {
        var profile = new EquipmentCombatProfileDefinition("test_profile", "melee", "slash", 1, 1, 4);

        Assert.True(HandEquipmentDomainRules.IsActiveRuntimeWeapon("right_hand", profile));
        Assert.False(HandEquipmentDomainRules.IsActiveRuntimeWeapon("left_hand", profile));
        Assert.False(HandEquipmentDomainRules.IsActiveRuntimeWeapon("right_hand", null));
    }

    [Fact]
    public void RegistryExposesOnlyRuntimePersistableWeaponTaxonomy()
    {
        var registry = new HandEquipmentAuthoringRegistry();

        Assert.Equal(["melee"], registry.LoadAttackFamilies().Select(option => option.Id));
        Assert.Equal(["thrust", "slash", "crush"], registry.LoadAttackStyles().Select(option => option.Id));
        Assert.Contains("mining", registry.DefaultToolCapabilityIds);
    }
}
