// Registers the focused Lore workspace through the existing host.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.Lore;

public static class LoreAuthoringFeature
{
    public static IServiceCollection AddLoreAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<LoreRepository>();
        services.AddSingleton<LoreAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, LoreSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, LoreCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapLoreAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var objects = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/lore-definitions");
        objects.MapGet("", async (HttpContext context, string? search, LoreAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.ListAsync(search, ct)));
        objects.MapGet("/{definitionId}", async (HttpContext context, string definitionId, LoreAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadAsync(definitionId, ct)));
        objects.MapPost("/{definitionId}/preview", async (HttpContext context, string definitionId, LoreRequest request, LoreAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.PreviewAsync(definitionId, request, ct)));
        objects.MapPut("/{definitionId}/draft", async (HttpContext context, string definitionId, LoreRequest request, LoreAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_draft", request, ct)));
        objects.MapPost("/{definitionId}/publish", async (HttpContext context, string definitionId, LoreRequest request, LoreAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "publish", request, ct)));
        return endpoints;
    }
}
