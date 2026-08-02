using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ContentCatalogService
{
    private readonly BasicItemAuthoringService _basicItems;
    private readonly ConsumableItemAuthoringService _consumables;

    public ContentCatalogService(
        BasicItemAuthoringService basicItems,
        ConsumableItemAuthoringService consumables)
    {
        _basicItems = basicItems;
        _consumables = consumables;
    }

    public async Task<ContentCatalogResponse> LoadAsync(CancellationToken cancellationToken = default)
    {
        var itemResult = await _basicItems.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> itemEntries =
            itemResult.Succeeded && itemResult.Value is not null
                ? itemResult.Value.Items.Select(item => new ContentCatalogEntry(
                    item.ItemId,
                    item.DisplayName,
                    item.PublicationState)).ToArray()
                : [];

        var consumableResult = await _consumables.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> consumableEntries =
            consumableResult.Succeeded && consumableResult.Value is not null
                ? consumableResult.Value.Items
                    .Where(item => item.HasConsumableProfile)
                    .Select(item => new ContentCatalogEntry(
                        item.ItemId,
                        item.DisplayName,
                        item.PublicationState))
                    .ToArray()
                : [];

        return new ContentCatalogResponse(
            DateTimeOffset.UtcNow,
            [
                new ContentCatalogSection("items", "Items", true, itemEntries),
                new ContentCatalogSection("consumables", "Consumables", true, consumableEntries),
                new ContentCatalogSection("mobs", "Mobs", false, []),
                new ContentCatalogSection("npcs", "NPCs", false, [])
            ]);
    }
}
