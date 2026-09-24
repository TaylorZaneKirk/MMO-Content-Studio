// Composes the supported authoring features into the existing host and route tree.
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Features.ActorAppearance;
using MMO.ContentStudio.AuthoringHost.Features.Dialogues;
using MMO.ContentStudio.AuthoringHost.Features.Items;
using MMO.ContentStudio.AuthoringHost.Features.LootTables;
using MMO.ContentStudio.AuthoringHost.Features.Mobs;
using MMO.ContentStudio.AuthoringHost.Features.Npcs;
using MMO.ContentStudio.AuthoringHost.Features.Quests;
using MMO.ContentStudio.AuthoringHost.Services;
using MMO.ContentStudio.AuthoringHost.Features.WorldObjects;
using MMO.ContentStudio.AuthoringHost.Features.Shops;

namespace MMO.ContentStudio.AuthoringHost.Features;

public static class AuthoringFeatureExtensions
{
    public static IServiceCollection AddAuthoringFeatures(this IServiceCollection services)
    {
        services.AddActorAppearanceAuthoring();
        services.AddItemAuthoring();
        services.AddLootTableAuthoring();
        services.AddMobAuthoring();
        services.AddNpcAuthoring();
        services.AddWorldObjectAuthoring();
        services.AddShopAuthoring();
        services.AddDialogueAuthoring();
        services.AddQuestAuthoring();
        services.AddSingleton<IRuntimeCatalogPublisher, RuntimeCatalogPublisherService>();
        return services;
    }

    public static IEndpointRouteBuilder MapAuthoringFeatures(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapActorAppearanceAuthoring();
        endpoints.MapItemAuthoring();
        endpoints.MapLootTableAuthoring();
        endpoints.MapMobAuthoring();
        endpoints.MapNpcAuthoring();
        endpoints.MapWorldObjectAuthoring();
        endpoints.MapShopAuthoring();
        endpoints.MapDialogueAuthoring();
        endpoints.MapQuestAuthoring();
        return endpoints;
    }
}
