// Registers the focused Cooking workspace through the existing host.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.Cooking;

public static class CookingAuthoringFeature
{
    public static IServiceCollection AddCookingAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<CookingRepository>();
        services.AddSingleton<CookingAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, CookingSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, CookingCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapCookingAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var objects = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/cooking-recipes");
        objects.MapGet("", async (HttpContext context, string? search, CookingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.ListAsync(search, ct)));
        objects.MapGet("/{definitionId}", async (HttpContext context, string definitionId, CookingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadAsync(definitionId, ct)));
        objects.MapPost("/{definitionId}/preview", async (HttpContext context, string definitionId, CookingRequest request, CookingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.PreviewAsync(definitionId, request, ct)));
        objects.MapPut("/{definitionId}/draft", async (HttpContext context, string definitionId, CookingRequest request, CookingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_draft", request, ct)));
        objects.MapPost("/{definitionId}/publish", async (HttpContext context, string definitionId, CookingRequest request, CookingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "publish", request, ct)));
        objects.MapPost("/{definitionId}/disable", async (HttpContext context, string definitionId, CookingRequest request, CookingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "disable", request, ct)));
        objects.MapPost("/{definitionId}/delete", async (HttpContext context, string definitionId, CookingRequest request, CookingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "delete", request, ct)));
        return endpoints;
    }
}
