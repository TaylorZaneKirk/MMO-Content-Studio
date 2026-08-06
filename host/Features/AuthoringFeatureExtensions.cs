using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Features.Dialogues;
using MMO.ContentStudio.AuthoringHost.Features.Items;
using MMO.ContentStudio.AuthoringHost.Features.Mobs;
using MMO.ContentStudio.AuthoringHost.Features.Npcs;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features;

public static class AuthoringFeatureExtensions
{
    public static IServiceCollection AddAuthoringFeatures(this IServiceCollection services)
    {
        services.AddItemAuthoring();
        services.AddMobAuthoring();
        services.AddNpcAuthoring();
        services.AddDialogueAuthoring();
        services.AddSingleton<IRuntimeCatalogPublisher, RuntimeCatalogPublisherService>();
        return services;
    }

    public static IEndpointRouteBuilder MapAuthoringFeatures(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapItemAuthoring();
        endpoints.MapMobAuthoring();
        endpoints.MapNpcAuthoring();
        endpoints.MapDialogueAuthoring();
        return endpoints;
    }
}
