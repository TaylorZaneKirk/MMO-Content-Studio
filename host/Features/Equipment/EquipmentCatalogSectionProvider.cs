using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Equipment;

public sealed class EquipmentCatalogSectionProvider(
    EquipmentItemAuthoringService equipment) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "equipment";

    public int SortOrder => 300;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await equipment.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items
                    .Where(item => item.Equippable)
                    .Select(item => new ContentCatalogEntry(
                        item.ItemId,
                        item.DisplayName,
                        item.PublicationState))
                    .ToArray()
                : [];

        return new ContentCatalogSection(ContentType, "Equipment", true, entries);
    }
}
