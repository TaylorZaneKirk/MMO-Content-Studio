using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class RiggedSpriteVisualDescriptorTests
{
    [Fact]
    public void Normalize_ActorPoseClearsFixedFieldsAndSortsCosmeticItems()
    {
        var result = RiggedSpriteVisualDescriptorNormalizer.Normalize(
            " COMPOSITE_RIG ",
            new RiggedSpriteVisualDescriptor(
                42,
                " humanoid_v1 ",
                " ",
                " ACTOR_POSE ",
                "s",
                4,
                new Dictionary<string, string>
                {
                    ["right_hand"] = " inventory_154_axe ",
                    ["left_hand"] = " inventory_37_small_shield "
                }));

        Assert.Equal(ActorVisualModes.CompositeRig, result.VisualMode);
        Assert.Equal(1, result.CompositeVisual!.SchemaVersion);
        Assert.Null(result.CompositeVisual.CalibrationId);
        Assert.Equal("actor_pose", result.CompositeVisual.PosePolicy);
        Assert.Null(result.CompositeVisual.FixedDirection);
        Assert.Null(result.CompositeVisual.FixedFrame);
        Assert.Equal(["left_hand", "right_hand"], result.CompositeVisual.CosmeticItemIds.Keys);
    }

    [Fact]
    public void DescriptorJson_RoundTripsCanonicalFixedPose()
    {
        var descriptor = RiggedSpriteVisualDescriptorNormalizer.Normalize(
            ActorVisualModes.CompositeRig,
            new RiggedSpriteVisualDescriptor(
                1,
                "humanoid_v1",
                "orc_v1",
                "fixed",
                "s",
                1,
                new Dictionary<string, string> { ["right_hand"] = "inventory_154_axe" })).CompositeVisual!;

        var reloaded = JsonSerializer.Deserialize<RiggedSpriteVisualDescriptor>(JsonSerializer.Serialize(descriptor));

        Assert.Equal("S", reloaded!.FixedDirection);
        Assert.Equal(descriptor.RigId, reloaded.RigId);
        Assert.Equal(descriptor.CalibrationId, reloaded.CalibrationId);
        Assert.Equal(descriptor.CosmeticItemIds, reloaded.CosmeticItemIds);
        Assert.True(RiggedSpriteVisualDescriptorNormalizer.Equivalent(descriptor, reloaded));
    }

    [Theory]
    [InlineData("unknown_rig", null, "right_hand", "inventory_154_axe", "unknown")]
    [InlineData("humanoid_v1", "wrong_rig", "right_hand", "inventory_154_axe", "calibration")]
    [InlineData("humanoid_v1", null, "missing_layer", "inventory_154_axe", "layer")]
    [InlineData("humanoid_v1", null, "right_hand", "inventory_missing", "equipped visual")]
    [InlineData("humanoid_v1", null, "left_hand", "inventory_154_axe", "render layer")]
    [InlineData("humanoid_v1", null, "left_hand", "inventory_37_small_shield", "socket")]
    public void Validate_RejectsInvalidRiggedSpriteReferences(
        string rigId,
        string? calibrationId,
        string layerId,
        string itemId,
        string expected)
    {
        var messages = new List<ApiError>();
        RiggedSpriteVisualDescriptorValidator.Validate(
            ActorVisualModes.CompositeRig,
            new RiggedSpriteVisualDescriptor(
                1,
                rigId,
                calibrationId,
                "actor_pose",
                null,
                null,
                new Dictionary<string, string> { [layerId] = itemId }),
            Catalog(),
            new HashSet<string>(["inventory_154_axe", "inventory_37_small_shield", "inventory_missing"], StringComparer.Ordinal),
            "npc",
            messages);

        Assert.Contains(messages, message => message.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsLegacyBaseLayers()
    {
        var messages = new List<ApiError>();
        var descriptor = new RiggedSpriteVisualDescriptor(
            1,
            "humanoid_v1",
            null,
            "actor_pose",
            null,
            null,
            new Dictionary<string, string>())
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["base_layers"] = JsonDocument.Parse("{}").RootElement.Clone()
            }
        };

        RiggedSpriteVisualDescriptorValidator.Validate(
            ActorVisualModes.CompositeRig,
            descriptor,
            Catalog(),
            new HashSet<string>(StringComparer.Ordinal),
            "mob",
            messages);

        Assert.Contains(messages, message => message.Field == "composite_visual.base_layers");
    }

    private static ActorRiggedSpriteCatalogDefinition Catalog() => new(
        true,
        null,
        [
            new ActorRigDefinition(
                "humanoid_v1",
                1,
                [
                    new ActorRigLayerDefinition("left_hand", "socket", "above", new Dictionary<string, int>()),
                    new ActorRigLayerDefinition("right_hand", "socket", "above", new Dictionary<string, int>())
                ],
                [],
                [])
        ],
        [
            new ActorRigCalibrationDefinition("orc_v1", "humanoid_v1"),
            new ActorRigCalibrationDefinition("wrong_rig", "other_v1")
        ],
        [
            new PublishedEquippedVisualDefinition("inventory_154_axe", "humanoid_v1", "socket", "right_hand"),
            new PublishedEquippedVisualDefinition("inventory_37_small_shield", "humanoid_v1", "rig_layer", "left_hand")
        ]);
}
