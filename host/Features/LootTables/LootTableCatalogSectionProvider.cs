using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.LootTables;

public sealed class LootTableCatalogSectionProvider(
    LootTableAuthoringService lootTables) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "loot_tables";

    public int SortOrder => 350;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await lootTables.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items
                    .Select(table => new ContentCatalogEntry(
                        table.LootTableId,
                        table.DisplayName,
                        table.PublicationState))
                    .ToArray()
                : [];

        return new ContentCatalogSection(
            ContentType,
            "Loot Tables",
            true,
            entries);
    }
}
