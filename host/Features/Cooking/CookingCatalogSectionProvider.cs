// Adds reusable definitions to the existing combined Studio catalog.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.Cooking;

public sealed class CookingCatalogSectionProvider(CookingAuthoringService recipes) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "cooking";
    public int SortOrder => 580;
    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await recipes.ListAsync(null, cancellationToken);
        return new(ContentType, "Cooking", true, result.Value?.Items.Select(item =>
            new ContentCatalogEntry(item.DefinitionId, item.DisplayName, item.PublicationState)).ToArray() ?? []);
    }
}
