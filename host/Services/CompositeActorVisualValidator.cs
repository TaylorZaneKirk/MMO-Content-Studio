using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed partial class CompositeActorVisualValidator
{
    private static readonly IReadOnlyDictionary<string, string> LayerDirectories =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["head"] = "head",
            ["body"] = "body",
            ["legs"] = "legs",
            ["boots"] = "boots",
            ["right_hand"] = "right_hand",
            ["left_hand"] = "left_hand",
            ["gloves"] = "gloves",
            ["cape"] = "cape"
        };

    private readonly ActorAppearanceCatalogService _rigCatalogService;
    private readonly IUnifiedItemRepository _itemRepository;
    private readonly ItemAssetService _assetService;

    public CompositeActorVisualValidator(
        ActorAppearanceCatalogService rigCatalogService,
        IUnifiedItemRepository itemRepository,
        ItemAssetService assetService)
    {
        _rigCatalogService = rigCatalogService;
        _itemRepository = itemRepository;
        _assetService = assetService;
    }

    public async Task ValidateAsync(
        System.Text.Json.JsonElement? compositeVisual,
        string errorPrefix,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (!CompositeActorVisualDescriptor.TryParse(compositeVisual, out var descriptor))
        {
            messages.Add(new ApiError(
                $"invalid_{errorPrefix}_composite_visual",
                "Composite visuals require a rig_id, base_layers object, and optional cosmetic_item_ids object of non-empty strings.",
                ValidationSeverity.Error,
                "composite_visual"));
            return;
        }

        var catalog = _rigCatalogService.LoadRigCatalog();
        if (!catalog.Available)
        {
            messages.Add(new ApiError(
                $"{errorPrefix}_composite_rig_catalog_unavailable",
                catalog.Message ?? "The canonical actor rig catalog is unavailable.",
                ValidationSeverity.Error,
                "composite_visual.rig_id"));
            return;
        }

        var rig = catalog.Rigs.SingleOrDefault(value => value.RigId == descriptor!.RigId);
        if (rig is null)
        {
            messages.Add(new ApiError(
                $"unknown_{errorPrefix}_composite_rig",
                $"Composite rig '{descriptor!.RigId}' is not defined by the canonical actor rig catalog.",
                ValidationSeverity.Error,
                "composite_visual.rig_id"));
            return;
        }

        var layers = rig.Layers.ToDictionary(layer => layer.LayerId, StringComparer.Ordinal);
        foreach (var (layerId, assetKey) in descriptor!.BaseLayers)
        {
            if (!layers.ContainsKey(layerId) || !LayerDirectories.TryGetValue(layerId, out var directory))
            {
                messages.Add(new ApiError($"invalid_{errorPrefix}_composite_layer", $"'{layerId}' is not a supported base layer for rig '{rig.RigId}'.", ValidationSeverity.Error, $"composite_visual.base_layers.{layerId}"));
                continue;
            }
            if (!AssetKeyRegex().IsMatch(assetKey) || !ResolvesActorArt(directory, assetKey))
            {
                messages.Add(new ApiError($"unresolved_{errorPrefix}_composite_base_layer", $"Base layer '{layerId}' must name canonical actor art for rig '{rig.RigId}'.", ValidationSeverity.Error, $"composite_visual.base_layers.{layerId}"));
            }
        }

        foreach (var (layerId, itemId) in descriptor.CosmeticItemIds)
        {
            if (!layers.ContainsKey(layerId))
            {
                messages.Add(new ApiError($"invalid_{errorPrefix}_composite_cosmetic_layer", $"'{layerId}' is not a supported rig layer.", ValidationSeverity.Error, $"composite_visual.cosmetic_item_ids.{layerId}"));
                continue;
            }
            if (!StableItemIdRegex().IsMatch(itemId))
            {
                messages.Add(new ApiError($"invalid_{errorPrefix}_composite_cosmetic_item", "Cosmetic item IDs must be stable item identifiers.", ValidationSeverity.Error, $"composite_visual.cosmetic_item_ids.{layerId}"));
                continue;
            }

            var item = await _itemRepository.LoadAsync(itemId, cancellationToken);
            if (item is null || !item.RuntimeEnabled || item.EquippedVisual is null)
            {
                messages.Add(new ApiError($"unresolved_{errorPrefix}_composite_cosmetic", $"Cosmetic item '{itemId}' must exist, be runtime-enabled, and have a published equipped visual.", ValidationSeverity.Error, $"composite_visual.cosmetic_item_ids.{layerId}"));
                continue;
            }

            var visual = item.EquippedVisual;
            if (!string.Equals(visual.RigId, rig.RigId, StringComparison.Ordinal)
                || !string.Equals(visual.RenderLayerId, layerId, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(visual.SecondarySocketId))
            {
                messages.Add(new ApiError($"incompatible_{errorPrefix}_composite_cosmetic", $"Cosmetic item '{itemId}' is not compatible with rig '{rig.RigId}' layer '{layerId}', or uses an unsupported secondary socket.", ValidationSeverity.Error, $"composite_visual.cosmetic_item_ids.{layerId}"));
            }
        }
    }

    private bool ResolvesActorArt(string directory, string assetKey)
    {
        foreach (var direction in new[] { "N", "E", "S", "W" })
        {
            foreach (var frame in new[] { 1, 2, 3, 4 })
            {
                var result = _assetService.ResolveGameAssetPng($"res://assets/actors/player/{directory}/{assetKey}-F{frame}-{direction}.png", "composite base layer");
                if (result.Exists)
                {
                    return true;
                }
            }
        }
        return false;
    }

    [GeneratedRegex("^[a-z0-9]+(?:_[a-z0-9]+)*$")]
    private static partial Regex AssetKeyRegex();

    [GeneratedRegex("^[a-z0-9]+(?:_[a-z0-9]+)*$")]
    private static partial Regex StableItemIdRegex();
}
