using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;

namespace MMO.ContentStudio.AuthoringHost.Features.Npcs;

public sealed class NpcCatalogSectionProvider : IAuthoringCatalogSectionProvider
{
    public string ContentType => "npcs";

    public int SortOrder => 500;

    public Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ContentCatalogSection(
            ContentType,
            "NPCs",
            false,
            []));
    }
}
