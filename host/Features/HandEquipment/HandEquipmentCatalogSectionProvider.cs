using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.HandEquipment;

public sealed class HandEquipmentCatalogSectionProvider(
    HandEquipmentAuthoringService handEquipment) : IAuthoringCatalogSectionProvider
{
    public string ContentType => "hand_equipment";

    public int SortOrder => 350;

    public async Task<ContentCatalogSection> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await handEquipment.ListAsync(null, cancellationToken);
        IReadOnlyList<ContentCatalogEntry> entries =
            result.Succeeded && result.Value is not null
                ? result.Value.Items
                    .Where(item => item.Equippable
                        && (EquipmentItemRepository.IsHandSlot(item.EquipmentSlotId)
                            || item.HasWeaponProfile
                            || item.HasToolCapabilities))
                    .Select(item => new ContentCatalogEntry(
                        item.ItemId,
                        item.DisplayName,
                        item.PublicationState))
                    .DistinctBy(item => item.Id)
                    .ToArray()
                : [];

        return new ContentCatalogSection(ContentType, "Weapons and Tools", true, entries);
    }
}
