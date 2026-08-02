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
