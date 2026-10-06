// Adds reusable definitions to the existing combined Studio catalog.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.MagicSpells;

public sealed class MagicSpellCatalogSectionProvider(MagicSpellAuthoringService spells) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "spells";
    public int SortOrder => 570;
    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await spells.ListAsync(null, cancellationToken);
        return new(ContentType, "Spells", true, result.Value?.Items.Select(item =>
            new ContentCatalogEntry(item.DefinitionId, item.DisplayName, item.PublicationState)).ToArray() ?? []);
    }
}
