using System.Text.Json;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class CompositeActorVisualValidatorTests : IDisposable
{
    private readonly string _clientRoot;
    private readonly string _assetRoot;
    private readonly TestUnifiedItemRepository _repository = new();
    private readonly CompositeActorVisualValidator _validator;

    public CompositeActorVisualValidatorTests()
    {
        _clientRoot = Path.Combine(Path.GetTempPath(), $"composite-validator-{Guid.NewGuid():N}", "client");
        _assetRoot = Path.Combine(_clientRoot, "assets");
        Directory.CreateDirectory(Path.Combine(_assetRoot, "actors", "player", "head"));
        File.WriteAllBytes(Path.Combine(_assetRoot, "actors", "player", "head", "head1-F1-N.png"), [0]);
        Directory.CreateDirectory(Path.Combine(_clientRoot, "actors", "appearance", "data", "rigs"));
        File.WriteAllText(
            Path.Combine(_clientRoot, "actors", "appearance", "data", "rigs", "catalog_v1.json"),
            """
            {
              "schema_version": 1,
              "rigs": [{
                "rig_id": "humanoid_v1",
                "schema_version": 1,
                "layers": {
                  "head": { "binding_type": "rig_layer", "default_render_plane": "body", "z_index_by_direction": { "N": 1, "E": 1, "S": 1, "W": 1 } },
                  "right_hand": { "binding_type": "rig_layer", "default_render_plane": "front", "z_index_by_direction": { "N": 2, "E": 2, "S": 2, "W": 2 } }
                },
                "sockets": {},
                "foreground_overlays": {}
              }]
            }
            """);
        var options = Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = _assetRoot
            }
        });
        _validator = new CompositeActorVisualValidator(
            new ActorAppearanceCatalogService(options),
            _repository,
            new ItemAssetService(options));
    }

    [Theory]
    [InlineData("{ \"rig_id\": \"humanoid_v1\", \"base_layers\": { \"head\": 4 } }", "invalid_npc_composite_visual")]
    [InlineData("{ \"rig_id\": \"unknown\", \"base_layers\": {} }", "unknown_npc_composite_rig")]
    [InlineData("{ \"rig_id\": \"humanoid_v1\", \"base_layers\": { \"cape\": \"cape1\" } }", "invalid_npc_composite_layer")]
    [InlineData("{ \"rig_id\": \"humanoid_v1\", \"base_layers\": { \"head\": \"missing\" } }", "unresolved_npc_composite_base_layer")]
    [InlineData("{ \"rig_id\": \"humanoid_v1\", \"base_layers\": {}, \"cosmetic_item_ids\": { \"right_hand\": \"bad-id\" } }", "invalid_npc_composite_cosmetic_item")]
    [InlineData("{ \"rig_id\": \"humanoid_v1\", \"base_layers\": {}, \"cosmetic_item_ids\": { \"cape\": \"inventory_missing\" } }", "invalid_npc_composite_cosmetic_layer")]
    public async Task ValidateAsyncRejectsMalformedRigsLayersAndIdentifiers(string payload, string errorCode)
    {
        var messages = await ValidateAsync(payload);

        Assert.Contains(messages, message => message.Code == errorCode);
    }

    [Theory]
    [InlineData("inventory_missing", "unresolved_npc_composite_cosmetic")]
    [InlineData("inventory_disabled", "unresolved_npc_composite_cosmetic")]
    [InlineData("inventory_no_visual", "unresolved_npc_composite_cosmetic")]
    [InlineData("inventory_wrong_rig", "incompatible_npc_composite_cosmetic")]
    [InlineData("inventory_wrong_layer", "incompatible_npc_composite_cosmetic")]
    [InlineData("inventory_secondary_socket", "incompatible_npc_composite_cosmetic")]
    public async Task ValidateAsyncRejectsUnusableOrIncompatibleCosmetics(string itemId, string errorCode)
    {
        _repository.Records["inventory_disabled"] = Record("inventory_disabled", false, Visual());
        _repository.Records["inventory_no_visual"] = Record("inventory_no_visual", true, null);
        _repository.Records["inventory_wrong_rig"] = Record("inventory_wrong_rig", true, Visual(rigId: "other_rig"));
        _repository.Records["inventory_wrong_layer"] = Record("inventory_wrong_layer", true, Visual(renderLayerId: "head"));
        _repository.Records["inventory_secondary_socket"] = Record("inventory_secondary_socket", true, Visual(secondarySocketId: "left_hand_primary"));

        var messages = await ValidateAsync(
            $"{{ \"rig_id\": \"humanoid_v1\", \"base_layers\": {{}}, \"cosmetic_item_ids\": {{ \"right_hand\": \"{itemId}\" }} }}");

        Assert.Contains(messages, message => message.Code == errorCode);
    }

    [Fact]
    public async Task ValidateAsyncAcceptsCompatibleRuntimeCosmeticAndCanonicalBaseLayer()
    {
        _repository.Records["inventory_154_axe"] = Record("inventory_154_axe", true, Visual());

        var messages = await ValidateAsync(
            """
            {
              "rig_id": "humanoid_v1",
              "base_layers": { "head": "head1" },
              "cosmetic_item_ids": { "right_hand": "inventory_154_axe" }
            }
            """);

        Assert.DoesNotContain(messages, message => message.Severity == ValidationSeverity.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_clientRoot))
        {
            Directory.Delete(_clientRoot, true);
        }
    }

    private async Task<IReadOnlyList<ApiError>> ValidateAsync(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var messages = new List<ApiError>();
        await _validator.ValidateAsync(document.RootElement, "npc", messages, CancellationToken.None);
        return messages;
    }

    private static UnifiedItemRecord Record(string itemId, bool runtimeEnabled, ItemEquippedVisualDefinition? visual) => new(
        itemId,
        itemId,
        "res://assets/items/test.png",
        "right_hand",
        "Right Hand",
        runtimeEnabled,
        1,
        false,
        false,
        false,
        false,
        false,
        false,
        null,
        [],
        [],
        [],
        [],
        null,
        null,
        visual,
        [],
        DateTimeOffset.UtcNow);

    private static ItemEquippedVisualDefinition Visual(
        string rigId = "humanoid_v1",
        string renderLayerId = "right_hand",
        string? secondarySocketId = null) => new(
        "dark_sword",
        rigId,
        "socket",
        renderLayerId,
        "right_hand_primary",
        secondarySocketId,
        new SourcePixelPointDefinition(0, 0),
        new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>());

    private sealed class TestUnifiedItemRepository : IUnifiedItemRepository
    {
        public Dictionary<string, UnifiedItemRecord> Records { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<UnifiedItemRecord>> ListAsync(string? search, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnifiedItemRecord>>(Records.Values.ToArray());

        public Task<UnifiedItemRecord?> LoadAsync(string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Records.TryGetValue(itemId, out var record) ? record : null);

        public Task<IReadOnlyList<EquipmentSlotRecord>> LoadSlotsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EquipmentSlotRecord>>([]);
        public Task<IReadOnlyList<EquipmentSkillRecord>> LoadSkillsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EquipmentSkillRecord>>([]);
        public Task<IReadOnlyList<EquipmentSkillRecord>> LoadGatheringCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EquipmentSkillRecord>>([]);
        public Task<IReadOnlyList<AuthoringOption>> LoadPublishedItemOptionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuthoringOption>>([]);
        public Task<bool> HasLiveReferencesAsync(string itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasPublishedConsumableResultReferencesAsync(string itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<ReferencedItemRecord?> LoadReferencedItemAsync(string itemId, CancellationToken cancellationToken = default) => Task.FromResult<ReferencedItemRecord?>(null);
        public Task<UnifiedItemRecord> SaveDraftAsync(string itemId, NormalizedItemDraft draft, DateTimeOffset? expectedUpdatedAtUtc, bool expectNew, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UnifiedItemRecord> SetPublicationAsync(string itemId, bool runtimeEnabled, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string itemId, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
