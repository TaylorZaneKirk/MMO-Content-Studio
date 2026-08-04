using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Features.Items;
using MMO.ContentStudio.AuthoringHost.Features.Mobs;
using MMO.ContentStudio.AuthoringHost.Features.Npcs;

namespace MMO.ContentStudio.AuthoringHost.Features;

public static class AuthoringFeatureExtensions
{
    public static IServiceCollection AddAuthoringFeatures(this IServiceCollection services)
    {
        services.AddItemAuthoring();
        services.AddMobAuthoring();
        services.AddNpcAuthoring();
        return services;
    }

    public static IEndpointRouteBuilder MapAuthoringFeatures(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapItemAuthoring();
        endpoints.MapMobAuthoring();
        return endpoints;
    }
}
