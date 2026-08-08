using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class CompositeActorVisualDescriptorTests
{
    [Fact]
    public void TryParseRetainsTypedBaseLayersAndCosmeticItemIds()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "rig_id": "humanoid_v1",
              "base_layers": { "head": "head1", "body": "defbod", "legs": "defbod" },
              "cosmetic_item_ids": { "right_hand": "inventory_154_axe", "left_hand": "inventory_209_champions_shield" }
            }
            """);

        var parsed = CompositeActorVisualDescriptor.TryParse(document.RootElement, out var descriptor);

        Assert.True(parsed);
        Assert.NotNull(descriptor);
        Assert.Equal("humanoid_v1", descriptor!.RigId);
        Assert.Equal("defbod", descriptor.BaseLayers["body"]);
        Assert.Equal("inventory_209_champions_shield", descriptor.CosmeticItemIds["left_hand"]);
    }

    [Fact]
    public void TryParseRejectsNonStringCompositeSemantics()
    {
        using var document = JsonDocument.Parse(
            """
            { "rig_id": "humanoid_v1", "base_layers": { "body": 3 } }
            """);

        Assert.False(CompositeActorVisualDescriptor.TryParse(document.RootElement, out _));
    }
}
