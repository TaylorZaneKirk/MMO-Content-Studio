using System.Text.Json;
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
    public void NormalizeAndSerializePreserveOneAuthoredRestoreAmount()
    {
        var draft = UnifiedItemDomainRules.Normalize(
            "Fixture", "res://assets/items/fixture.png",
            new ItemConsumableBehaviorDraft(" eat ", 1, null, null, false, 0, null, null, [],
                [new ConsumableEffectDefinition(0, " restore_resource ", " health ", 17)]),
            null, []);

        var effect = Assert.Single(draft.ConsumableBehavior!.Effects);
        Assert.Equal(new ConsumableEffectDefinition(0, "restore_resource", "health", 17), effect);
        var json = JsonSerializer.SerializeToElement(effect);
        Assert.Equal(new[] { "effect_index", "effect_type", "target_id", "amount" },
            json.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(17, json.GetProperty("amount").GetInt32());
        Assert.Equal(effect, json.Deserialize<ConsumableEffectDefinition>());
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
