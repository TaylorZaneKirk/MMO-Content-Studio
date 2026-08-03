using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Mobs;

public sealed class MobCatalogSectionProvider(
    MobAuthoringService mobs) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "mobs";

    public int SortOrder => 400;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await mobs.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items
                    .Select(mob => new ContentCatalogEntry(
                        mob.MobDefinitionId,
                        mob.DisplayName,
                        mob.PublicationState))
                    .ToArray()
                : [];

        return new ContentCatalogSection(
            ContentType,
            "Mobs",
            true,
            entries);
    }
}
