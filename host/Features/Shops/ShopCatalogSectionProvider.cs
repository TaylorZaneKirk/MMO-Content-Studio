// Adds reusable definitions to the existing combined Studio catalog.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.Shops;

public sealed class ShopCatalogSectionProvider(ShopAuthoringService shops) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "shops";
    public int SortOrder => 560;
    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await shops.ListAsync(null, cancellationToken);
        return new(ContentType, "Shops", true, result.Value?.Items.Select(item =>
            new ContentCatalogEntry(item.DefinitionId, item.DisplayName, item.PublicationState)).ToArray() ?? []);
    }
}
