using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class UnifiedItemDomainRulesTests
{
    [Fact]
    public void NormalizeKeepsToolCapabilitiesIndependentOfEquipmentMetadata()
    {
        var draft = UnifiedItemDomainRules.Normalize(
            " Mining Pick ",
            " res://assets/items/pick.png ",
            null,
            null,
            [
                new ItemToolCapabilityDraft(" mining ", 2, " swing ", " spark ")
            ]);

        Assert.Null(draft.Equipment);
        var capability = Assert.Single(draft.ToolCapabilities);
        Assert.Equal("mining", capability.CapabilityId);
        Assert.Equal(2, capability.PowerTier);
        Assert.Equal("swing", capability.ActionAnimationId);
        Assert.Equal("spark", capability.EffectResourceId);
    }

    [Fact]
    public void ClassifyAllowsCrossSpecializationLabels()
    {
        var equipment = new ItemEquipmentMetadataDraft(
            "right_hand",
            1,
            [],
            [],
            EquipmentCombatBonusDefinition.Zero,
            new EquipmentCombatProfileDefinition("pickaxe", "melee", "crush", 1, 1, 4),
            null);
        var draft = UnifiedItemDomainRules.Normalize(
            "Battle Pick",
            "res://assets/items/battle_pick.png",
            new ItemConsumableBehaviorDraft("eat", 1, null, null, false, 0, null, null, [], []),
            equipment,
            [
                new ItemToolCapabilityDraft("mining", 1, null, null)
            ]);

        Assert.Equal(
            "Consumable + Weapon + Tool",
            UnifiedItemDomainRules.Classify(draft.ConsumableBehavior is not null, draft.Equipment, draft.ToolCapabilities));
    }
}
