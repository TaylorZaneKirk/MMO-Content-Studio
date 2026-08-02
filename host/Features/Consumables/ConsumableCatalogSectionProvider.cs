using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Consumables;

public sealed class ConsumableCatalogSectionProvider(
    ConsumableItemAuthoringService consumables) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "consumables";

    public int SortOrder => 200;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await consumables.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items
                    .Where(item => item.HasConsumableProfile)
                    .Select(item => new ContentCatalogEntry(
                        item.ItemId,
                        item.DisplayName,
                        item.PublicationState))
                    .ToArray()
                : [];

        return new ContentCatalogSection(ContentType, "Consumables", true, entries);
    }
}
