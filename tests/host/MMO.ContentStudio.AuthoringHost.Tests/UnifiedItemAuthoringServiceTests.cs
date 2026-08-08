using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class UnifiedItemAuthoringServiceTests : IDisposable
{
    private const string ItemId = "battle_pick";
    private const string IconPath = "res://assets/items/battle_pick.png";

    private readonly string _clientRoot;
    private readonly string _assetRoot;

    public UnifiedItemAuthoringServiceTests()
    {
        _clientRoot = Path.Combine(Path.GetTempPath(), $"content-studio-tests-{Guid.NewGuid():N}", "client");
        _assetRoot = Path.Combine(_clientRoot, "assets");
        Directory.CreateDirectory(Path.Combine(_assetRoot, "items"));
        Directory.CreateDirectory(Path.Combine(_clientRoot, "actors", "appearance", "data", "rigs"));
        File.WriteAllBytes(Path.Combine(_assetRoot, "items", "battle_pick.png"), [0]);
        File.WriteAllBytes(Path.Combine(_assetRoot, "items", "renamed_pick.png"), [0]);
        File.WriteAllText(
            Path.Combine(_clientRoot, "actors", "appearance", "data", "rigs", "catalog_v1.json"),
            """
            {
              "schema_version": 1,
              "rigs": [
                {
                  "rig_id": "humanoid_v1",
                  "schema_version": 1,
                  "layers": {
                    "head": {
                      "binding_type": "rig_layer",
                      "default_render_plane": "body",
                      "z_index_by_direction": { "N": 30, "E": 30, "S": 30, "W": 30 }
                    },
                    "body": {
                      "binding_type": "rig_layer",
                      "default_render_plane": "body",
                      "z_index_by_direction": { "N": 20, "E": 20, "S": 20, "W": 20 }
                    },
                    "legs": {
                      "binding_type": "rig_layer",
                      "default_render_plane": "body",
                      "z_index_by_direction": { "N": 20, "E": 20, "S": 20, "W": 20 }
                    },
                    "right_hand": {
                      "binding_type": "rig_layer",
                      "default_render_plane": "body",
                      "z_index_by_direction": { "N": 30, "E": 30, "S": 30, "W": 0 }
                    },
                    "left_hand": {
                      "binding_type": "rig_layer",
                      "default_render_plane": "body",
                      "z_index_by_direction": { "N": 30, "E": 30, "S": 30, "W": 0 }
                    }
                  },
                  "sockets": {
                    "right_hand_primary": {
                      "N": {
                        "1": { "x": 80, "y": 80 },
                        "2": { "x": 80, "y": 80 },
                        "3": { "x": 80, "y": 80 },
                        "4": { "x": 80, "y": 80 }
                      },
                      "E": {
                        "1": { "x": 84, "y": 78 },
                        "2": { "x": 84, "y": 78 },
                        "3": { "x": 84, "y": 78 },
                        "4": { "x": 84, "y": 78 }
                      },
                      "S": {
                        "1": { "x": 82, "y": 86 },
                        "2": { "x": 82, "y": 86 },
                        "3": { "x": 82, "y": 86 },
                        "4": { "x": 82, "y": 86 }
                      },
                      "W": {
                        "1": { "x": 76, "y": 78 },
                        "2": { "x": 76, "y": 78 },
                        "3": { "x": 76, "y": 78 },
                        "4": { "x": 76, "y": 78 }
                      }
                    },
                    "left_hand_primary": {
                      "N": {
                        "1": { "x": 52, "y": 84 },
                        "2": { "x": 52, "y": 84 },
                        "3": { "x": 52, "y": 84 },
                        "4": { "x": 52, "y": 84 }
                      },
                      "E": {
                        "1": { "x": 108, "y": 80 },
                        "2": { "x": 108, "y": 80 },
                        "3": { "x": 108, "y": 80 },
                        "4": { "x": 108, "y": 80 }
                      },
                      "S": {
                        "1": { "x": 120, "y": 84 },
                        "2": { "x": 120, "y": 84 },
                        "3": { "x": 120, "y": 84 },
                        "4": { "x": 120, "y": 84 }
                      },
                      "W": {
                        "1": { "x": 76, "y": 78 },
                        "2": { "x": 76, "y": 78 },
                        "3": { "x": 76, "y": 78 },
                        "4": { "x": 76, "y": 78 }
                      }
                    }
                  },
                  "foreground_overlays": {
                    "right_hand_primary_grip": {
                      "socket_id": "right_hand_primary",
                      "source_layer_id": "body",
                      "z_index_by_direction": { "N": 40, "E": 40, "S": 40, "W": 40 },
                      "source_rect_by_direction": {
                        "N": {
                          "1": { "x": 72, "y": 72, "width": 8, "height": 8 },
                          "2": null
                        },
                        "E": null
                      }
                    }
                  }
                }
              ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_clientRoot))
        {
            Directory.Delete(_clientRoot, true);
        }
    }

    [Fact]
    public async Task CompleteAggregateRoundTripsWithSemanticEquality()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository);
        var request = UnifiedSaveRequest(null);
        var preview = await service.PreviewAsync(ItemId, ToPreview(request, "save_draft"), TestContext.Current.CancellationToken);

        var saved = await service.SaveDraftAsync(
            ItemId,
            request with { PreviewSignature = preview.Value!.PreviewSignature },
            TestContext.Current.CancellationToken);

        AssertSucceeded(saved);
        var persisted = Assert.Contains(ItemId, repository.Records);
        AssertSemanticallyEqual(
            UnifiedItemDomainRules.Normalize(
                request.DisplayName,
                request.IconTexturePath,
                request.ConsumableBehavior,
                request.Equipment,
                request.ToolCapabilities),
            UnifiedItemDomainRules.FromRecord(persisted));

        var loaded = await service.LoadAsync(ItemId, TestContext.Current.CancellationToken);
        AssertSucceeded(loaded);
        Assert.NotNull(loaded.Value!.ConsumableBehavior);
        Assert.NotNull(loaded.Value.Equipment?.WeaponProfile);
        Assert.Single(loaded.Value.ToolCapabilities);
    }

    [Fact]
    public async Task ChildOnlyEditsAdvanceRootTimestamp()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId].UpdatedAtUtc;

        var result = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            UnifiedSaveRequest(before) with
            {
                Equipment = EquipmentDraft() with
                {
                    RequiredStrength = 9,
                    Requirements = [new EquipmentSkillRequirementDraft("strength", 7)]
                }
            });

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.True(after.UpdatedAtUtc > before);
        Assert.Equal("Battle Pick", after.DisplayName);
        Assert.Equal(7, Assert.Single(after.Requirements).RequiredValue);
    }

    [Fact]
    public async Task SaveDraftReturnsFreshTimestampUsableForImmediatePublish()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId].UpdatedAtUtc;

        var save = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(repository.Records[ItemId], before) with
            {
                DisplayName = "Battle Pick Mk II"
            });

        AssertSucceeded(save);
        var saved = save.Value!.Item;
        Assert.True(saved.UpdatedAtUtc > before);
        Assert.Equal(repository.Records[ItemId].UpdatedAtUtc, saved.UpdatedAtUtc);

        var publishPreview = await service.PreviewAsync(
            ItemId,
            ToPreview(ToSaveRequest(repository.Records[ItemId], saved.UpdatedAtUtc), "publish"),
            TestContext.Current.CancellationToken);

        AssertSucceeded(publishPreview);
        var publish = await service.PublishAsync(
            ItemId,
            new ItemPublicationRequest(saved.UpdatedAtUtc, publishPreview.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        AssertSucceeded(publish);
        Assert.True(repository.Records[ItemId].RuntimeEnabled);
    }

    [Fact]
    public async Task EquipmentEditsPreserveHiddenConsumableBehavior()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId];

        var result = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(before, before.UpdatedAtUtc) with
            {
                Equipment = EquipmentDraft() with { RequiredStrength = 12 }
            });

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Equal(before.ConsumableBehavior, after.ConsumableBehavior);
        Assert.Equal(before.ConsumableEffects, after.ConsumableEffects);
    }

    [Fact]
    public async Task ConsumableEditsPreserveHiddenEquipmentAndWeaponProfile()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId];

        var result = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(before, before.UpdatedAtUtc) with
            {
                ConsumableBehavior = ConsumableDraft() with
                {
                    SuccessMessage = "Crunch."
                }
            });

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Equal(before.EquipmentSlotId, after.EquipmentSlotId);
        Assert.Equal(before.RequiredStrength, after.RequiredStrength);
        AssertSemanticallyEqual(
            UnifiedItemDomainRules.FromRecord(before).Equipment!,
            UnifiedItemDomainRules.FromRecord(after).Equipment!);
    }

    [Fact]
    public async Task WeaponAndToolEditsPreserveHiddenConsumableBehavior()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId];
        var result = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(before, before.UpdatedAtUtc) with
            {
                Equipment = EquipmentDraft() with { RequiredStrength = 14 }
            });

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Equal(before.ConsumableBehavior, after.ConsumableBehavior);
        Assert.Equal(before.ConsumableEffects, after.ConsumableEffects);
    }

    [Fact]
    public async Task EquipmentDisablePreservesToolCapabilities()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;

        var result = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(repository.Records[ItemId], expected) with { Equipment = null });

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Null(after.EquipmentSlotId);
        Assert.Single(after.ToolCapabilities);
    }

    [Fact]
    public async Task EquipmentDisablePreservesSubmittedToolCapabilities()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;
        var result = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(repository.Records[ItemId], expected) with
            {
                Equipment = null,
                ToolCapabilities = [ToolDraft()]
            });

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Null(after.EquipmentSlotId);
        Assert.Single(after.ToolCapabilities);
    }

    [Fact]
    public async Task ExplicitEmptyCapabilityCollectionDeletesCapabilities()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;
        var request = ToSaveRequest(repository.Records[ItemId], expected) with
        {
            Equipment = null,
            ToolCapabilities = []
        };
        var result = await SaveDraftWithPreviewAsync(service, ItemId, request);

        AssertSucceeded(result);
        Assert.Empty(repository.Records[ItemId].ToolCapabilities);
    }

    [Fact]
    public async Task IdentityEditPreservesEverySpecialization()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var before = repository.Records[ItemId];

        var result = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(before, before.UpdatedAtUtc) with
            {
                DisplayName = "Renamed Pick",
                IconTexturePath = "res://assets/items/renamed_pick.png"
            });

        AssertSucceeded(result);
        var after = repository.Records[ItemId];
        Assert.Equal("Renamed Pick", after.DisplayName);
        var expected = UnifiedItemDomainRules.FromRecord(before) with
        {
            DisplayName = after.DisplayName,
            IconTexturePath = after.IconTexturePath
        };
        AssertSemanticallyEqual(expected, UnifiedItemDomainRules.FromRecord(after));
    }

    [Fact]
    public async Task HiddenInvalidSpecializationBlocksPublication()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord(consumableEffects: []));
        var service = CreateService(repository);

        var result = await service.PublishAsync(
            ItemId,
            new ItemPublicationRequest(repository.Records[ItemId].UpdatedAtUtc, null),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "consumable_has_no_effects");
        Assert.False(repository.Records[ItemId].RuntimeEnabled);
    }

    [Fact]
    public async Task PreviewSignatureChangesWhenEquippedVisualChanges()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository);
        var request = UnifiedSaveRequest(null);

        var firstPreview = await service.PreviewAsync(
            ItemId,
            ToPreview(request, "save_draft"),
            TestContext.Current.CancellationToken);
        var secondPreview = await service.PreviewAsync(
            ItemId,
            ToPreview(
                request with
                {
                    Equipment = EquipmentDraft() with
                    {
                        EquippedVisual = EquippedVisualDraft() with
                        {
                            GripAnchors = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>
                            {
                                ["N"] = new Dictionary<string, SourcePixelPointDefinition>
                                {
                                    ["1"] = new(32, 18)
                                }
                            }
                        }
                    }
                },
                "save_draft"),
            TestContext.Current.CancellationToken);

        AssertSucceeded(firstPreview);
        AssertSucceeded(secondPreview);
        Assert.NotEqual(firstPreview.Value!.PreviewSignature, secondPreview.Value!.PreviewSignature);
    }

    [Fact]
    public async Task AppearanceOnlyPreviewDoesNotChangeExistingIconPath()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var existing = repository.Records[ItemId];
        var request = ToSaveRequest(existing, existing.UpdatedAtUtc) with
        {
            Equipment = EquipmentDraft() with
            {
                EquippedVisual = EquippedVisualDraft() with { AssetKey = "war_hammer" }
            }
        };

        var preview = await service.PreviewAsync(
            ItemId,
            ToPreview(request, "save_draft"),
            TestContext.Current.CancellationToken);

        AssertSucceeded(preview);
        Assert.DoesNotContain(preview.Value!.Changes, change => change.Field == "icon_texture_path");
    }

    [Fact]
    public async Task DisablingEquipabilityClearsEquippedVisual()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);

        var result = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(repository.Records[ItemId], repository.Records[ItemId].UpdatedAtUtc) with
            {
                Equipment = null
            });

        AssertSucceeded(result);
        Assert.Null(repository.Records[ItemId].EquippedVisual);
    }

    [Fact]
    public async Task LoadOptionsReadsActorRigCatalogFromCanonicalClientRoot()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository);

        var result = await service.LoadOptionsAsync(TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        Assert.True(result.Value!.ActorRigCatalog.Available);
        var rig = Assert.Single(result.Value.ActorRigCatalog.Rigs);
        Assert.Equal("humanoid_v1", rig.RigId);
        Assert.Contains(rig.Layers, value => value.LayerId == "right_hand");
        Assert.Contains(rig.Sockets, value => value.SocketId == "right_hand_primary");
        var overlay = Assert.Single(rig.ForegroundOverlays);
        Assert.Equal("right_hand_primary_grip", overlay.OverlayId);
        Assert.Equal("right_hand_primary", overlay.SocketId);
        Assert.Equal("body", overlay.SourceLayerId);
        Assert.Equal(40, overlay.ZIndexByDirection["N"]);
        Assert.Equal(8, overlay.SourceRectByDirection["N"]["1"]!.Width);
        Assert.Null(overlay.SourceRectByDirection["N"]["2"]);
        Assert.Null(overlay.SourceRectByDirection["E"]["1"]);
    }

    [Fact]
    public async Task LoadOptionsReadsActorRigCatalogWhenGameClientAssetsPointsToClientRoot()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository, _clientRoot);

        var result = await service.LoadOptionsAsync(TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        Assert.True(result.Value!.ActorRigCatalog.Available);
        Assert.Equal(
            Path.Combine(_clientRoot, "actors", "appearance", "data", "rigs", "catalog_v1.json"),
            result.Value.ActorRigCatalog.SourcePath);
        Assert.Contains(result.Value.ActorRigCatalog.Rigs, value => value.RigId == "humanoid_v1");
    }

    [Fact]
    public async Task SaveDraftAllowsIncompleteSocketAnchorsButPublishRejectsThem()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository);
        var request = UnifiedSaveRequest(null) with
        {
            Equipment = EquipmentDraft() with
            {
                EquippedVisual = new ItemEquippedVisualDraft(
                    "dark_sword",
                    "humanoid_v1",
                    "socket",
                    "right_hand",
                    "right_hand_primary",
                    null,
                    new SourcePixelPointDefinition(0, 0),
                    new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>
                    {
                        ["N"] = new Dictionary<string, SourcePixelPointDefinition>
                        {
                            ["1"] = new(30, 12)
                        }
                    })
            }
        };

        var preview = await service.PreviewAsync(
            ItemId,
            ToPreview(request, "save_draft"),
            TestContext.Current.CancellationToken);
        AssertSucceeded(preview);
        var save = await service.SaveDraftAsync(
            ItemId,
            request with { PreviewSignature = preview.Value!.PreviewSignature },
            TestContext.Current.CancellationToken);
        AssertSucceeded(save);

        var publish = await service.PublishAsync(
            ItemId,
            new ItemPublicationRequest(repository.Records[ItemId].UpdatedAtUtc, null),
            TestContext.Current.CancellationToken);
        Assert.False(publish.Succeeded);
        Assert.Contains(publish.Errors, error => error.Code == "missing_grip_anchor_direction");
    }

    [Fact]
    public async Task PublishAllowsExplicitlyHiddenChampionShieldEastPosesButRejectsMissingVisibleNorthAnchor()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository);
        var gripAnchors = EquippedVisualDraft().GripAnchors!
            .Where(pair => pair.Key != "E")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var hiddenPoses = new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal)
        {
            ["E"] = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["1"] = true,
                ["2"] = true,
                ["3"] = true,
                ["4"] = true
            }
        };
        var request = UnifiedSaveRequest(null) with
        {
            Equipment = EquipmentDraft() with
            {
                EquipmentSlotId = "left_hand",
                WeaponProfile = null,
                EquippedVisual = EquippedVisualDraft() with
                {
                    AssetKey = "champions_shield",
                    RenderLayerId = "left_hand",
                    SocketId = "left_hand_primary",
                    GripAnchors = gripAnchors,
                    HiddenPoses = hiddenPoses
                }
            }
        };

        var save = await SaveDraftWithPreviewAsync(service, ItemId, request);
        AssertSucceeded(save);
        Assert.True(repository.Records[ItemId].EquippedVisual!.HiddenPoses!["E"]["4"]);
        Assert.False(repository.Records[ItemId].EquippedVisual!.GripAnchors.ContainsKey("E"));

        var publish = await service.PublishAsync(
            ItemId,
            new ItemPublicationRequest(repository.Records[ItemId].UpdatedAtUtc, null),
            TestContext.Current.CancellationToken);
        AssertSucceeded(publish);

        var missingVisibleNorthAnchor = request with
        {
            ExpectedUpdatedAtUtc = repository.Records[ItemId].UpdatedAtUtc,
            Equipment = request.Equipment! with
            {
                EquippedVisual = request.Equipment.EquippedVisual! with
                {
                    GripAnchors = gripAnchors.ToDictionary(
                        pair => pair.Key,
                        pair => (IReadOnlyDictionary<string, SourcePixelPointDefinition>)pair.Value
                            .Where(frame => frame.Key != "1" || pair.Key != "N")
                            .ToDictionary(frame => frame.Key, frame => frame.Value, StringComparer.Ordinal),
                        StringComparer.Ordinal)
                }
            }
        };
        var invalidSave = await SaveDraftWithPreviewAsync(service, ItemId, missingVisibleNorthAnchor);
        AssertSucceeded(invalidSave);

        var invalidPublish = await service.PublishAsync(
            ItemId,
            new ItemPublicationRequest(repository.Records[ItemId].UpdatedAtUtc, null),
            TestContext.Current.CancellationToken);
        Assert.False(invalidPublish.Succeeded);
        Assert.Contains(invalidPublish.Errors, error => error.Code == "missing_grip_anchor_frame");
    }

    [Fact]
    public async Task SaveDraft_PersistsOnlyTheSelectedEquippedVisualFlipPose()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository);
        var request = UnifiedSaveRequest(null) with
        {
            Equipment = EquipmentDraft() with
            {
                EquippedVisual = EquippedVisualDraft() with
                {
                    FlipXByPose = new Dictionary<string, IReadOnlyDictionary<string, bool>>
                    {
                        ["N"] = new Dictionary<string, bool>
                        {
                            ["1"] = true,
                            ["2"] = false
                        }
                    }
                }
            }
        };

        var result = await SaveDraftWithPreviewAsync(service, ItemId, request);

        AssertSucceeded(result);
        var flipXByPose = repository.Records[ItemId].EquippedVisual!.FlipXByPose!;
        Assert.True(flipXByPose["N"]["1"]);
        Assert.DoesNotContain("2", flipXByPose["N"].Keys);
        Assert.False(repository.Records[ItemId].EquippedVisual!.FlipXByPose!.ContainsKey("E"));
    }

    [Fact]
    public async Task SignedAndOutOfBoundsAttachmentAnchorsRoundTripWithoutClamp()
    {
        var repository = new InMemoryUnifiedItemRepository();
        var service = CreateService(repository);
        var outOfBoundsAnchors = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>
        {
            ["N"] = new Dictionary<string, SourcePixelPointDefinition>
            {
                ["1"] = new(-24, 80),
                ["2"] = new(72, 100),
                ["3"] = new(100, -12),
                ["4"] = new(80, 24)
            },
            ["E"] = new Dictionary<string, SourcePixelPointDefinition>
            {
                ["1"] = new(-40, 16),
                ["2"] = new(96, -32),
                ["3"] = new(128, 64),
                ["4"] = new(80, 40)
            },
            ["S"] = new Dictionary<string, SourcePixelPointDefinition>
            {
                ["1"] = new(24, 100),
                ["2"] = new(-16, -24),
                ["3"] = new(48, 72),
                ["4"] = new(64, 88)
            },
            ["W"] = new Dictionary<string, SourcePixelPointDefinition>
            {
                ["1"] = new(36, -40),
                ["2"] = new(71, 16),
                ["3"] = new(80, 16),
                ["4"] = new(100, 24)
            }
        };
        var request = UnifiedSaveRequest(null) with
        {
            Equipment = EquipmentDraft() with
            {
                EquippedVisual = EquippedVisualDraft() with
                {
                    GripAnchors = outOfBoundsAnchors
                }
            }
        };

        var preview = await service.PreviewAsync(
            ItemId,
            ToPreview(request, "save_draft"),
            TestContext.Current.CancellationToken);
        AssertSucceeded(preview);

        var changedPreview = await service.PreviewAsync(
            ItemId,
            ToPreview(
                request with
                {
                    Equipment = request.Equipment! with
                    {
                        EquippedVisual = request.Equipment.EquippedVisual! with
                        {
                            GripAnchors = new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>(outOfBoundsAnchors)
                            {
                                ["W"] = new Dictionary<string, SourcePixelPointDefinition>(outOfBoundsAnchors["W"])
                                {
                                    ["4"] = new(101, 24)
                                }
                            }
                        }
                    }
                },
                "save_draft"),
            TestContext.Current.CancellationToken);
        AssertSucceeded(changedPreview);
        Assert.NotEqual(preview.Value!.PreviewSignature, changedPreview.Value!.PreviewSignature);

        var save = await service.SaveDraftAsync(
            ItemId,
            request with { PreviewSignature = preview.Value.PreviewSignature },
            TestContext.Current.CancellationToken);
        AssertSucceeded(save);

        var savedVisual = repository.Records[ItemId].EquippedVisual;
        Assert.NotNull(savedVisual);
        Assert.Equal(-24, savedVisual!.GripAnchors["N"]["1"].X);
        Assert.Equal(100, savedVisual.GripAnchors["N"]["2"].Y);
        Assert.Equal(80, savedVisual.GripAnchors["W"]["3"].X);
        Assert.Equal(100, savedVisual.GripAnchors["W"]["4"].X);
        AssertSemanticallyEqual(
            outOfBoundsAnchors,
            repository.Records[ItemId].EquippedVisual!.GripAnchors);
    }

    [Fact]
    public async Task PublishRejectsUnknownEquippedVisualRig()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var request = UnifiedSaveRequest(repository.Records[ItemId].UpdatedAtUtc) with
        {
            Equipment = EquipmentDraft() with
            {
                EquippedVisual = EquippedVisualDraft() with { RigId = "missing_rig" }
            }
        };

        var preview = await service.PreviewAsync(
            ItemId,
            ToPreview(request, "publish"),
            TestContext.Current.CancellationToken);

        AssertSucceeded(preview);
        Assert.Contains(preview.Value!.Messages, message => message.Code == "unknown_equipped_visual_rig");
        Assert.False(preview.Value.ValidForPublication);
    }

    [Fact]
    public async Task StaleConcurrencyIsEnforcedThroughUnifiedMutations()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var stale = repository.Records[ItemId].UpdatedAtUtc.AddMinutes(-1);

        var save = await service.SaveDraftAsync(
            ItemId,
            UnifiedSaveRequest(stale) with { PreviewSignature = "stale" },
            TestContext.Current.CancellationToken);
        Assert.False(save.Succeeded);
        Assert.Contains(save.Errors, error => error.Code == "preview_signature_mismatch");

        var publish = await service.PublishAsync(
            ItemId,
            new ItemPublicationRequest(stale, null),
            TestContext.Current.CancellationToken);
        var disable = await service.DisableAsync(
            ItemId,
            new ItemPublicationRequest(stale, null),
            TestContext.Current.CancellationToken);
        var delete = await service.DeleteAsync(
            ItemId,
            new DeleteMutationRequest(stale, null),
            TestContext.Current.CancellationToken);
        Assert.False(delete.Succeeded);
        Assert.Contains(delete.Errors, error => error.Code == "preview_signature_mismatch");

        foreach (var result in new AuthoringOperationResult<object>[]
        {
            CastFailure(publish),
            CastFailure(disable)
        })
        {
            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, error => error.Code == "item_version_conflict");
        }
    }

    [Fact]
    public async Task UnifiedRoutesRejectPreviewSignatureMismatch()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;
        var request = UnifiedSaveRequest(expected) with { DisplayName = "Signed Pick" };
        var preview = await service.PreviewAsync(ItemId, ToPreview(request, "save_draft"), TestContext.Current.CancellationToken);

        var result = await service.SaveDraftAsync(
            ItemId,
            request with { PreviewSignature = $"{preview.Value!.PreviewSignature}-stale" },
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "preview_signature_mismatch");
    }

    [Fact]
    public async Task PublishDisableAndDeleteUseSavedCompleteAggregate()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var service = CreateService(repository);
        var expected = repository.Records[ItemId].UpdatedAtUtc;

        var publish = await service.PublishAsync(
            ItemId,
            new ItemPublicationRequest(expected, null),
            TestContext.Current.CancellationToken);
        AssertSucceeded(publish);
        Assert.True(repository.Records[ItemId].RuntimeEnabled);

        var publishedAt = repository.Records[ItemId].UpdatedAtUtc;
        var disable = await service.DisableAsync(
            ItemId,
            new ItemPublicationRequest(publishedAt, null),
            TestContext.Current.CancellationToken);
        AssertSucceeded(disable);
        Assert.False(repository.Records[ItemId].RuntimeEnabled);

        var disabledAt = repository.Records[ItemId].UpdatedAtUtc;
        var previewDelete = await service.PreviewAsync(
            ItemId,
            ToPreview(UnifiedSaveRequest(disabledAt), "delete"),
            TestContext.Current.CancellationToken);
        var delete = await service.DeleteAsync(
            ItemId,
            new DeleteMutationRequest(disabledAt, previewDelete.Value!.PreviewSignature),
            TestContext.Current.CancellationToken);

        AssertSucceeded(delete);
        Assert.False(repository.Records.ContainsKey(ItemId));
    }

    [Fact]
    public async Task PublishRunsRuntimeCatalogPublisher()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, runtimeCatalogPublisher: publisher);
        var expected = repository.Records[ItemId].UpdatedAtUtc;

        var publish = await service.PublishAsync(
            ItemId,
            new ItemPublicationRequest(expected, null),
            TestContext.Current.CancellationToken);

        AssertSucceeded(publish);
        Assert.Equal([RuntimeCatalogPublicationScope.EquipmentVisual], publisher.PublishScopes);
    }

    [Fact]
    public async Task SaveDraftDoesNotRunRuntimeCatalogPublisher()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord());
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, runtimeCatalogPublisher: publisher);

        var save = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(repository.Records[ItemId], repository.Records[ItemId].UpdatedAtUtc) with
            {
                DisplayName = "Draft Pick"
            });

        AssertSucceeded(save);
        Assert.Empty(publisher.PublishScopes);
    }

    [Fact]
    public async Task RuntimeEnabledSaveDraftUnpublishesAndRefreshesRuntimeCatalogs()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord() with { RuntimeEnabled = true });
        var publisher = new TestRuntimeCatalogPublisher
        {
            OnPublish = () => Assert.False(repository.Records[ItemId].RuntimeEnabled)
        };
        var service = CreateService(repository, runtimeCatalogPublisher: publisher);

        var save = await SaveDraftWithPreviewAsync(
            service,
            ItemId,
            ToSaveRequest(repository.Records[ItemId], repository.Records[ItemId].UpdatedAtUtc) with
            {
                Equipment = EquipmentDraft() with
                {
                    EquippedVisual = EquippedVisualDraft() with { AssetKey = "war_hammer" }
                }
            });

        AssertSucceeded(save);
        Assert.Equal("Draft", save.Value!.Item.PublicationState);
        Assert.False(repository.Records[ItemId].RuntimeEnabled);
        Assert.Equal([RuntimeCatalogPublicationScope.EquipmentVisual], publisher.PublishScopes);
    }

    [Fact]
    public async Task DisableRunsRuntimeCatalogPublisher()
    {
        var repository = new InMemoryUnifiedItemRepository();
        repository.Put(CompleteRecord() with { RuntimeEnabled = true });
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, runtimeCatalogPublisher: publisher);

        var disable = await service.DisableAsync(
            ItemId,
            new ItemPublicationRequest(repository.Records[ItemId].UpdatedAtUtc, null),
            TestContext.Current.CancellationToken);

        AssertSucceeded(disable);
        Assert.False(repository.Records[ItemId].RuntimeEnabled);
        Assert.Equal([RuntimeCatalogPublicationScope.EquipmentVisual], publisher.PublishScopes);
    }

    [Fact]
    public async Task ReloadVerificationFailureReturnsStructuredError()
    {
        var repository = new InMemoryUnifiedItemRepository { CorruptNextReloadAfterSave = true };
        repository.Put(CompleteRecord() with { RuntimeEnabled = true });
        var publisher = new TestRuntimeCatalogPublisher();
        var service = CreateService(repository, runtimeCatalogPublisher: publisher);
        var expected = repository.Records[ItemId].UpdatedAtUtc;

        var preview = await service.PreviewAsync(
            ItemId,
            ToPreview(
                ToSaveRequest(repository.Records[ItemId], expected) with
                {
                    DisplayName = "Renamed Pick",
                    IconTexturePath = "res://assets/items/renamed_pick.png"
                },
                "save_draft"),
            TestContext.Current.CancellationToken);
        var result = await service.SaveDraftAsync(
            ItemId,
            ToSaveRequest(repository.Records[ItemId], expected) with
            {
                DisplayName = "Renamed Pick",
                IconTexturePath = "res://assets/items/renamed_pick.png",
                PreviewSignature = preview.Value!.PreviewSignature
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "item_operation_failed");
        Assert.Empty(publisher.PublishScopes);
    }

    [Fact]
    public void RepositoryReplacesChildCollectionsInsideTheRootTransaction()
    {
        var source = File.ReadAllText(FindRepositoryRootFile("host/Persistence/UnifiedItemRepository.cs"));
        var saveBody = Between(source, "public async Task<UnifiedItemRecord> SaveDraftAsync", "public async Task<UnifiedItemRecord> SetPublicationAsync");

        Assert.Contains("BeginTransactionAsync", saveBody);
        Assert.Contains("updated_at = now()", saveBody);
        Assert.Contains("await ReplaceConsumableAsync(connection, transaction", saveBody);
        Assert.Contains("await ReplaceEquipmentAsync(connection, transaction", saveBody);
        Assert.Contains("await ReplaceToolCapabilitiesAsync(connection, transaction", saveBody);
        Assert.Contains("LoadAggregateAsync(connection, transaction", saveBody);
        Assert.Contains("CommitAsync", saveBody);
        Assert.Contains("runtime_enabled = false", saveBody);

        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_consumable_requirements\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_consumable_effects\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_skill_requirements\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_skill_modifiers\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_combat_profiles\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_combat_bonuses\"", source);
        Assert.Contains("ExecuteDeleteAsync(connection, transaction, \"item_tool_capabilities\"", source);
    }

    private UnifiedItemAuthoringService CreateService(
        InMemoryUnifiedItemRepository repository,
        string? assetRoot = null,
        IRuntimeCatalogPublisher? runtimeCatalogPublisher = null)
    {
        var assetRoots = Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = assetRoot ?? _assetRoot
            }
        });
        var assetService = new ItemAssetService(assetRoots);
        var actorAppearanceCatalogService = new ActorAppearanceCatalogService(assetRoots);
        var registry = new ItemAuthoringRegistry();
        var validator = new UnifiedItemValidator(repository, registry, assetService, actorAppearanceCatalogService);
        return new UnifiedItemAuthoringService(
            repository,
            validator,
            registry,
            assetService,
            actorAppearanceCatalogService,
            NullLogger<UnifiedItemAuthoringService>.Instance,
            runtimeCatalogPublisher);
    }

    private static SaveItemDraftRequest UnifiedSaveRequest(DateTimeOffset? expected) =>
        new(
            "Battle Pick",
            IconPath,
            ConsumableDraft(),
            EquipmentDraft(),
            [ToolDraft()],
            expected,
            null);

    private static PreviewItemRequest ToPreview(SaveItemDraftRequest request, string operation) =>
        new(
            request.DisplayName,
            request.IconTexturePath,
            request.ConsumableBehavior,
            request.Equipment,
            request.ToolCapabilities,
            request.ExpectedUpdatedAtUtc,
            operation);

    private static SaveItemDraftRequest ToSaveRequest(UnifiedItemRecord record, DateTimeOffset? expected)
    {
        var draft = UnifiedItemDomainRules.FromRecord(record);
        return new SaveItemDraftRequest(
            draft.DisplayName,
            draft.IconTexturePath,
            draft.ConsumableBehavior is null
                ? null
                : new ItemConsumableBehaviorDraft(
                    draft.ConsumableBehavior.UseAction,
                    draft.ConsumableBehavior.ConsumeQuantity,
                    draft.ConsumableBehavior.ResultItemId,
                    draft.ConsumableBehavior.SuccessMessage,
                    draft.ConsumableBehavior.UsableInCombat,
                    draft.ConsumableBehavior.CooldownMs,
                    draft.ConsumableBehavior.UseAnimationId,
                    draft.ConsumableBehavior.UseSoundResourcePath,
                    draft.ConsumableBehavior.Requirements,
                    draft.ConsumableBehavior.Effects),
            draft.Equipment is null
                ? null
                : new ItemEquipmentMetadataDraft(
                    draft.Equipment.EquipmentSlotId,
                    draft.Equipment.RequiredStrength,
                    draft.Equipment.Requirements,
                    draft.Equipment.SkillModifiers,
                    draft.Equipment.CombatBonuses,
                    draft.Equipment.WeaponProfile,
                    draft.Equipment.EquippedVisual is null
                        ? null
                        : new ItemEquippedVisualDraft(
                            draft.Equipment.EquippedVisual.AssetKey,
                            draft.Equipment.EquippedVisual.RigId,
                            draft.Equipment.EquippedVisual.BindingType,
                            draft.Equipment.EquippedVisual.RenderLayerId,
                            draft.Equipment.EquippedVisual.SocketId,
                            draft.Equipment.EquippedVisual.SecondarySocketId,
                            draft.Equipment.EquippedVisual.Nudge,
                            draft.Equipment.EquippedVisual.GripAnchors,
                            draft.Equipment.EquippedVisual.FlipXByPose,
                            draft.Equipment.EquippedVisual.HiddenPoses)),
            draft.ToolCapabilities,
            expected,
            null);
    }

    private static async Task<AuthoringOperationResult<ItemMutationResponse>> SaveDraftWithPreviewAsync(
        UnifiedItemAuthoringService service,
        string itemId,
        SaveItemDraftRequest request)
    {
        var preview = await service.PreviewAsync(
            itemId,
            ToPreview(request, "save_draft"),
            TestContext.Current.CancellationToken);
        AssertSucceeded(preview);
        return await service.SaveDraftAsync(
            itemId,
            request with { PreviewSignature = preview.Value!.PreviewSignature },
            TestContext.Current.CancellationToken);
    }

    private static ItemConsumableBehaviorDraft ConsumableDraft() =>
        new(
            "eat",
            1,
            null,
            "Restored.",
            false,
            0,
            null,
            null,
            [],
            [new ConsumableEffectDefinition(0, "restore_resource", "health", 1, 3)]);

    private static ItemEquipmentMetadataDraft EquipmentDraft() =>
        new(
            "right_hand",
            5,
            [new EquipmentSkillRequirementDraft("strength", 3)],
            [new EquipmentSkillModifierDraft("attack", 1)],
            EquipmentCombatBonusDefinition.Zero,
            WeaponProfile(),
            EquippedVisualDraft());

    private static ItemEquippedVisualDraft EquippedVisualDraft() =>
        new(
            "dark_sword",
            "humanoid_v1",
            "socket",
            "right_hand",
            "right_hand_primary",
            null,
            new SourcePixelPointDefinition(0, 0),
            new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>
            {
                ["N"] = new Dictionary<string, SourcePixelPointDefinition>
                {
                    ["1"] = new(30, 12),
                    ["2"] = new(30, 12),
                    ["3"] = new(30, 12),
                    ["4"] = new(30, 12)
                },
                ["E"] = new Dictionary<string, SourcePixelPointDefinition>
                {
                    ["1"] = new(32, 18),
                    ["2"] = new(32, 18),
                    ["3"] = new(32, 18),
                    ["4"] = new(32, 18)
                },
                ["S"] = new Dictionary<string, SourcePixelPointDefinition>
                {
                    ["1"] = new(28, 20),
                    ["2"] = new(28, 20),
                    ["3"] = new(28, 20),
                    ["4"] = new(28, 20)
                },
                ["W"] = new Dictionary<string, SourcePixelPointDefinition>
                {
                    ["1"] = new(24, 18),
                    ["2"] = new(24, 18),
                    ["3"] = new(24, 18),
                    ["4"] = new(24, 18)
                }
            });

    private static EquipmentCombatProfileDefinition WeaponProfile() =>
        new("battle_pick", "melee", "crush", 1, 1, 4);

    private static ItemToolCapabilityDraft ToolDraft() =>
        new("mining", 1, "swing", null);

    private static UnifiedItemRecord CompleteRecord(
        IReadOnlyList<ConsumableEffectDefinition>? consumableEffects = null) =>
        new(
            ItemId,
            "Battle Pick",
            IconPath,
            "right_hand",
            "Right Hand",
            false,
            5,
            true,
            true,
            false,
            true,
            true,
            true,
            new ConsumableProfileDraft("eat", 1, null, "Restored.", false, 0, null, null),
            [],
            consumableEffects ?? [new ConsumableEffectDefinition(0, "restore_resource", "health", 1, 3)],
            [new EquipmentSkillRequirementDefinition("strength", "Strength", 3)],
            [new EquipmentSkillModifierDefinition("attack", "Attack", 1)],
            WeaponProfile(),
            EquipmentCombatBonusDefinition.Zero,
            new ItemEquippedVisualDefinition(
                "dark_sword",
                "humanoid_v1",
                "socket",
                "right_hand",
                "right_hand_primary",
                null,
                new SourcePixelPointDefinition(0, 0),
                EquippedVisualDraft().GripAnchors!),
            [new ItemToolCapabilityDefinition("mining", "Mining", 0, 1, "swing", null)],
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

    private static AuthoringOperationResult<object> CastFailure<T>(AuthoringOperationResult<T> result) =>
        result.Succeeded
            ? AuthoringOperationResult<object>.Success(result.Value!)
            : AuthoringOperationResult<object>.Failure(result.Errors);

    private static void AssertSucceeded<T>(AuthoringOperationResult<T> result) =>
        Assert.True(
            result.Succeeded,
            string.Join("; ", result.Errors.Select(error => $"{error.Code}:{error.Field}:{error.Message}:{error.Remediation}")));

    private static void AssertSemanticallyEqual<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private static string FindRepositoryRootFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find {start}.");
        Assert.True(endIndex > startIndex, $"Could not find {end}.");
        return source[startIndex..endIndex];
    }

    private sealed class InMemoryUnifiedItemRepository : IUnifiedItemRepository
    {
        private DateTimeOffset _clock = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        public Dictionary<string, UnifiedItemRecord> Records { get; } = new(StringComparer.Ordinal);

        public bool CorruptNextReloadAfterSave { get; init; }

        private bool _corruptNextLoad;

        public void Put(UnifiedItemRecord record) => Records[record.ItemId] = record;

        public Task<IReadOnlyList<UnifiedItemRecord>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnifiedItemRecord>>(Records.Values.ToArray());

        public Task<UnifiedItemRecord?> LoadAsync(
            string itemId,
            CancellationToken cancellationToken = default)
        {
            Records.TryGetValue(itemId, out var record);
            if (record is not null && _corruptNextLoad)
            {
                _corruptNextLoad = false;
                record = record with { DisplayName = $"{record.DisplayName} Corrupt" };
            }

            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<EquipmentSlotRecord>> LoadSlotsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EquipmentSlotRecord>>(
            [
                new("right_hand", "Right Hand"),
                new("left_hand", "Left Hand"),
                new("body", "Body")
            ]);

        public Task<IReadOnlyList<EquipmentSkillRecord>> LoadSkillsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EquipmentSkillRecord>>(
            [
                new("attack", "Attack"),
                new("strength", "Strength"),
                new("defence", "Defence"),
                new("mining", "Mining")
            ]);

        public Task<IReadOnlyList<EquipmentSkillRecord>> LoadGatheringCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EquipmentSkillRecord>>([new("mining", "Mining")]);

        public Task<IReadOnlyList<AuthoringOption>> LoadPublishedItemOptionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthoringOption>>([]);

        public Task<bool> HasLiveReferencesAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> HasPublishedConsumableResultReferencesAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<ReferencedItemRecord?> LoadReferencedItemAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferencedItemRecord?>(null);

        public Task<UnifiedItemRecord> SaveDraftAsync(
            string itemId,
            NormalizedItemDraft draft,
            DateTimeOffset? expectedUpdatedAtUtc,
            bool expectNew,
            CancellationToken cancellationToken = default)
        {
            Records.TryGetValue(itemId, out var existing);
            if (expectNew && existing is not null)
            {
                throw new UnifiedItemConcurrencyException(itemId, existing.UpdatedAtUtc);
            }
            EnsureExpectedVersion(itemId, existing, expectedUpdatedAtUtc);

            var saved = ToRecord(itemId, draft, false, NextTimestamp());
            Records[itemId] = saved;
            _corruptNextLoad = CorruptNextReloadAfterSave;
            return Task.FromResult(saved);
        }

        public Task<UnifiedItemRecord> SetPublicationAsync(
            string itemId,
            bool runtimeEnabled,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!Records.TryGetValue(itemId, out var existing))
            {
                throw new UnifiedItemNotFoundException(itemId);
            }
            EnsureExpectedVersion(itemId, existing, expectedUpdatedAtUtc);

            var saved = existing with
            {
                RuntimeEnabled = runtimeEnabled,
                UpdatedAtUtc = existing.RuntimeEnabled == runtimeEnabled ? existing.UpdatedAtUtc : NextTimestamp()
            };
            Records[itemId] = saved;
            return Task.FromResult(saved);
        }

        public Task DeleteAsync(
            string itemId,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!Records.TryGetValue(itemId, out var existing))
            {
                throw new UnifiedItemNotFoundException(itemId);
            }
            EnsureExpectedVersion(itemId, existing, expectedUpdatedAtUtc);
            if (existing.RuntimeEnabled)
            {
                throw new UnifiedItemPublishedDeleteException(itemId);
            }

            Records.Remove(itemId);
            return Task.CompletedTask;
        }

        private DateTimeOffset NextTimestamp()
        {
            _clock = _clock.AddMinutes(1);
            return _clock;
        }

        private static UnifiedItemRecord ToRecord(
            string itemId,
            NormalizedItemDraft draft,
            bool runtimeEnabled,
            DateTimeOffset updatedAtUtc)
        {
            var equipment = draft.Equipment;
            var consumable = draft.ConsumableBehavior;
            return new UnifiedItemRecord(
                itemId,
                draft.DisplayName,
                draft.IconTexturePath,
                equipment?.EquipmentSlotId,
                equipment?.EquipmentSlotId is null ? null : equipment.EquipmentSlotId,
                runtimeEnabled,
                equipment?.RequiredStrength ?? 1,
                consumable is not null,
                equipment?.WeaponProfile is not null,
                equipment?.CombatBonuses.IsZero == false,
                equipment?.Requirements.Count > 0,
                equipment?.SkillModifiers.Count > 0,
                draft.ToolCapabilities.Count > 0,
                consumable is null
                    ? null
                    : new ConsumableProfileDraft(
                        consumable.UseAction,
                        consumable.ConsumeQuantity,
                        consumable.ResultItemId,
                        consumable.SuccessMessage,
                        consumable.UsableInCombat,
                        consumable.CooldownMs,
                        consumable.UseAnimationId,
                        consumable.UseSoundResourcePath),
                consumable?.Requirements ?? [],
                consumable?.Effects ?? [],
                equipment?.Requirements
                    .Select(value => new EquipmentSkillRequirementDefinition(value.SkillId, value.SkillId, value.RequiredValue))
                    .ToArray() ?? [],
                equipment?.SkillModifiers
                    .Select(value => new EquipmentSkillModifierDefinition(value.SkillId, value.SkillId, value.ModifierValue))
                    .ToArray() ?? [],
                equipment?.WeaponProfile,
                equipment?.CombatBonuses,
                equipment?.EquippedVisual is null
                    ? null
                    : new ItemEquippedVisualDefinition(
                        equipment.EquippedVisual.AssetKey ?? string.Empty,
                        equipment.EquippedVisual.RigId ?? string.Empty,
                        equipment.EquippedVisual.BindingType ?? string.Empty,
                        equipment.EquippedVisual.RenderLayerId ?? string.Empty,
                        equipment.EquippedVisual.SocketId,
                        equipment.EquippedVisual.SecondarySocketId,
                        equipment.EquippedVisual.Nudge,
                        equipment.EquippedVisual.GripAnchors,
                        equipment.EquippedVisual.FlipXByPose,
                        equipment.EquippedVisual.HiddenPoses),
                draft.ToolCapabilities
                    .Select((value, index) => new ItemToolCapabilityDefinition(
                        value.CapabilityId,
                        value.CapabilityId,
                        index,
                        value.PowerTier,
                        value.ActionAnimationId,
                        value.EffectResourceId))
                    .ToArray(),
                updatedAtUtc);
        }

        private static void EnsureExpectedVersion(
            string itemId,
            UnifiedItemRecord? existing,
            DateTimeOffset? expectedUpdatedAtUtc)
        {
            if (existing is null && expectedUpdatedAtUtc is null)
            {
                return;
            }
            if (existing is null
                || expectedUpdatedAtUtc is null
                || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
            {
                throw new UnifiedItemConcurrencyException(itemId, existing?.UpdatedAtUtc ?? DateTimeOffset.MinValue);
            }
        }
    }

    private sealed class TestRuntimeCatalogPublisher : IRuntimeCatalogPublisher
    {
        public List<RuntimeCatalogPublicationScope> PublishScopes { get; } = [];

        public Action? OnPublish { get; init; }

        public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(
            RuntimeCatalogPublicationScope scope,
            CancellationToken cancellationToken)
        {
            PublishScopes.Add(scope);
            OnPublish?.Invoke();
            return Task.FromResult<IReadOnlyList<ApiError>>([]);
        }
    }
}
