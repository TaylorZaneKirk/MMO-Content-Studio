using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ContentCatalogService
{
    public ContentCatalogResponse LoadEmptyFoundationCatalog() =>
        new(
            DateTimeOffset.UtcNow,
            [
                new ContentCatalogSection("items", "Items", false, []),
                new ContentCatalogSection("mobs", "Mobs", false, []),
                new ContentCatalogSection("npcs", "NPCs", false, [])
            ]);
}
