using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Features.Consumables;
using MMO.ContentStudio.AuthoringHost.Features.Equipment;
using MMO.ContentStudio.AuthoringHost.Features.HandEquipment;
using MMO.ContentStudio.AuthoringHost.Features.Items;
using MMO.ContentStudio.AuthoringHost.Features.Mobs;

namespace MMO.ContentStudio.AuthoringHost.Features;

public static class AuthoringFeatureExtensions
{
    public static IServiceCollection AddAuthoringFeatures(this IServiceCollection services)
    {
        services.AddItemAuthoring();
        services.AddConsumableAuthoring();
        services.AddEquipmentAuthoring();
        services.AddHandEquipmentAuthoring();
        services.AddMobAuthoring();
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
        endpoints.MapHandEquipmentAuthoring();
        endpoints.MapMobAuthoring();
        return endpoints;
    }
}
