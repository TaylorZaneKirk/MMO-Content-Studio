// Adds reusable definitions to the existing combined Studio catalog.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.Fishing;

public sealed class FishingCatalogSectionProvider(FishingAuthoringService methods) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "fishing";
    public int SortOrder => 580;
    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await methods.ListAsync(null, cancellationToken);
        return new(ContentType, "Fishing", true, result.Value?.Items.Select(item =>
            new ContentCatalogEntry(item.DefinitionId, item.DisplayName, item.PublicationState)).ToArray() ?? []);
    }
}
