// Adds reusable definitions to the existing combined Studio catalog.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.WorldObjects;

public sealed class WorldObjectCatalogSectionProvider(WorldObjectAuthoringService objects) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "world_objects";
    public int SortOrder => 550;
    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await objects.ListAsync(null, cancellationToken);
        return new(ContentType, "World Objects", true, result.Value?.Items.Select(item =>
            new ContentCatalogEntry(item.DefinitionId, item.DisplayName, item.PublicationState)).ToArray() ?? []);
    }
}
