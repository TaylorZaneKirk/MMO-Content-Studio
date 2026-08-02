using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Features.Consumables;
using MMO.ContentStudio.AuthoringHost.Features.Equipment;
using MMO.ContentStudio.AuthoringHost.Features.Items;

namespace MMO.ContentStudio.AuthoringHost.Features;

public static class AuthoringFeatureExtensions
{
    public static IServiceCollection AddAuthoringFeatures(this IServiceCollection services)
    {
        services.AddItemAuthoring();
        services.AddConsumableAuthoring();
        services.AddEquipmentAuthoring();
        services.AddSingleton<IAuthoringCatalogSectionProvider>(
            new PlannedCatalogSectionProvider("mobs", "Mobs", 400));
        services.AddSingleton<IAuthoringCatalogSectionProvider>(
            new PlannedCatalogSectionProvider("npcs", "NPCs", 500));
        return services;
    }

    public static IEndpointRouteBuilder MapAuthoringFeatures(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapItemAuthoring();
        endpoints.MapConsumableAuthoring();
        endpoints.MapEquipmentAuthoring();
        return endpoints;
    }
}
