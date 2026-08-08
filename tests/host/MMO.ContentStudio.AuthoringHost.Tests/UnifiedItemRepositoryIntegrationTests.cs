using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class UnifiedItemRepositoryIntegrationTests
{
    private const string AxeItemId = "inventory_154_axe";
    private const string AxeIconTexturePath = "res://assets/items/Inventory_154_Axe.png";

    [Fact]
    public async Task SaveDraftReloadsAxeEquippedVisualWithAllGripAnchorsWhenIntegrationDatabaseIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CONTENT_STUDIO_INTEGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var repository = new UnifiedItemRepository(new AuthoringDatabaseConnectionFactory(
            Options.Create(new ConnectionProfilesOptions
            {
                Active = "integration",
                Profiles = new Dictionary<string, ConnectionProfileOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["integration"] = new()
                    {
                        ConnectionString = connectionString,
                        CommandTimeoutSeconds = 5
                    }
                }
            })));
        var existing = await repository.LoadAsync(AxeItemId, TestContext.Current.CancellationToken);

        Assert.NotNull(existing);
        Assert.NotNull(existing!.EquipmentSlotId);
        var draft = UnifiedItemDomainRules.FromRecord(existing);
        Assert.NotNull(draft.Equipment);
        var gripAnchors = AxeGripAnchors();
        var flipXByPose = new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal)
        {
            ["N"] = new Dictionary<string, bool>(StringComparer.Ordinal) { ["1"] = true }
        };
        var expectedVisual = new NormalizedItemEquippedVisual(
            "axe",
            "humanoid_v1",
            "socket",
            "right_hand",
            "right_hand_primary",
            null,
            new SourcePixelPointDefinition(0, 0),
            gripAnchors,
            flipXByPose);
        draft = draft with
        {
            Equipment = draft.Equipment! with { EquippedVisual = expectedVisual }
        };

        var saved = await repository.SaveDraftAsync(
            AxeItemId,
            draft,
            existing.UpdatedAtUtc,
            false,
            TestContext.Current.CancellationToken);
        var reloaded = await repository.LoadAsync(AxeItemId, TestContext.Current.CancellationToken);

        Assert.Equal(AxeIconTexturePath, saved.IconTexturePath);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.RuntimeEnabled);
        Assert.Equal(AxeIconTexturePath, reloaded.IconTexturePath);
        Assert.NotNull(reloaded.EquippedVisual);
        Assert.Equal("axe", reloaded.EquippedVisual!.AssetKey);
        Assert.Equal("humanoid_v1", reloaded.EquippedVisual.RigId);
        Assert.Equal("socket", reloaded.EquippedVisual.BindingType);
        Assert.Equal("right_hand", reloaded.EquippedVisual.RenderLayerId);
        Assert.Equal("right_hand_primary", reloaded.EquippedVisual.SocketId);
        Assert.Null(reloaded.EquippedVisual.SecondarySocketId);
        Assert.Equal(16, reloaded.EquippedVisual.GripAnchors.Sum(pair => pair.Value.Count));
        Assert.True(reloaded.EquippedVisual.FlipXByPose!["N"]["1"]);
        Assert.False(reloaded.EquippedVisual.FlipXByPose.ContainsKey("E"));
        foreach (var direction in gripAnchors)
        {
            Assert.True(reloaded.EquippedVisual.GripAnchors.TryGetValue(direction.Key, out var reloadedFrames));
            foreach (var frame in direction.Value)
            {
                Assert.True(reloadedFrames.TryGetValue(frame.Key, out var reloadedAnchor));
                Assert.Equal(frame.Value, reloadedAnchor);
            }
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>> AxeGripAnchors() =>
        new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>(StringComparer.Ordinal)
        {
            ["N"] = Frames(new(73, 43), new(73, 43), new(73, 43), new(73, 43)),
            ["E"] = Frames(new(83, 105), new(95, 93), new(103, 93), new(119, 77)),
            ["S"] = Frames(new(51, 90), new(56, 98), new(68, 106), new(68, 106)),
            ["W"] = Frames(new(100, 105), new(88, 93), new(80, 93), new(64, 77))
        };

    private static IReadOnlyDictionary<string, SourcePixelPointDefinition> Frames(
        SourcePixelPointDefinition first,
        SourcePixelPointDefinition second,
        SourcePixelPointDefinition third,
        SourcePixelPointDefinition fourth) =>
        new Dictionary<string, SourcePixelPointDefinition>(StringComparer.Ordinal)
        {
            ["1"] = first,
            ["2"] = second,
            ["3"] = third,
            ["4"] = fourth
        };
}
