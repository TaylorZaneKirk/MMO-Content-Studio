using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Npcs;

public static class NpcAuthoringFeature
{
    public static IServiceCollection AddNpcAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<NpcAuthoringRegistry>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, NpcSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, NpcCatalogSectionProvider>();
        return services;
    }
}
