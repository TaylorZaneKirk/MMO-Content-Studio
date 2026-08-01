using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ContentCatalogService
{
    private readonly BasicItemAuthoringService _basicItems;

    public ContentCatalogService(BasicItemAuthoringService basicItems)
    {
        _basicItems = basicItems;
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

        return new ContentCatalogResponse(
            DateTimeOffset.UtcNow,
            [
                new ContentCatalogSection("items", "Items", true, itemEntries),
                new ContentCatalogSection("mobs", "Mobs", false, []),
                new ContentCatalogSection("npcs", "NPCs", false, [])
            ]);
    }
}
