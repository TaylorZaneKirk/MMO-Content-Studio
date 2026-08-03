using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;

namespace MMO.ContentStudio.AuthoringHost.Features.Mobs;

public sealed class MobCatalogSectionProvider : IAuthoringCatalogSectionProvider
{
    public string ContentType => "mobs";

    public int SortOrder => 400;

    public Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ContentCatalogSection(
            ContentType,
            "Mobs",
            false,
            []));
    }
}
