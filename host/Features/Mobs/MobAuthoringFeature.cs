using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Mobs;

public static class MobAuthoringFeature
{
    public static IServiceCollection AddMobAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<MobAuthoringRegistry>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, MobSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, MobCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapMobAuthoring(
        this IEndpointRouteBuilder endpoints) =>
        endpoints;
}
