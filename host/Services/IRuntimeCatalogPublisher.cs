using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

[Flags]
public enum RuntimeCatalogPublicationScope
{
    None = 0,
    Npc = 1 << 0,
    Mob = 1 << 1,
    Dialogue = 1 << 2,
    EquipmentVisual = 1 << 3,
    Quest = 1 << 4
}

public interface IRuntimeCatalogPublisher
{
    Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(
        RuntimeCatalogPublicationScope scope,
        CancellationToken cancellationToken);
}
