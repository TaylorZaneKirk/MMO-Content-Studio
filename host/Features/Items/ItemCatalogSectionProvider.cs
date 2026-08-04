using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Items;

public sealed class ItemCatalogSectionProvider(
    UnifiedItemAuthoringService items) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "items";

    public int SortOrder => 100;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await items.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items.Select(item => new ContentCatalogEntry(
                    item.ItemId,
                    item.DisplayName,
                    item.PublicationState)).ToArray()
                : [];

        return new ContentCatalogSection(ContentType, "Items", true, entries);
    }
}
