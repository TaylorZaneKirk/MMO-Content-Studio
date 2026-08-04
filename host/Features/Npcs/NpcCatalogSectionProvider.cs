using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Npcs;

public sealed class NpcCatalogSectionProvider(
    NpcAuthoringService npcs) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "npcs";

    public int SortOrder => 500;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await npcs.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items
                    .Select(npc => new ContentCatalogEntry(
                        npc.NpcDefinitionId,
                        npc.DisplayName,
                        npc.PublicationState))
                    .ToArray()
                : [];

        return new ContentCatalogSection(
            ContentType,
            "NPCs",
            true,
            entries);
    }
}
