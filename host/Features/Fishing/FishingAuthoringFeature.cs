// Registers the focused Fishing workspace through the existing host.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.Fishing;

public static class FishingAuthoringFeature
{
    public static IServiceCollection AddFishingAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<FishingRepository>();
        services.AddSingleton<FishingAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, FishingSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, FishingCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapFishingAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var objects = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/fishing-methods");
        objects.MapGet("", async (HttpContext context, string? search, FishingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.ListAsync(search, ct)));
        objects.MapGet("/{definitionId}", async (HttpContext context, string definitionId, FishingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadAsync(definitionId, ct)));
        objects.MapPost("/{definitionId}/preview", async (HttpContext context, string definitionId, FishingRequest request, FishingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.PreviewAsync(definitionId, request, ct)));
        objects.MapPut("/{definitionId}/draft", async (HttpContext context, string definitionId, FishingRequest request, FishingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_draft", request, ct)));
        objects.MapPost("/{definitionId}/publish", async (HttpContext context, string definitionId, FishingRequest request, FishingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "publish", request, ct)));
        objects.MapPost("/{definitionId}/disable", async (HttpContext context, string definitionId, FishingRequest request, FishingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "disable", request, ct)));
        objects.MapPost("/{definitionId}/delete", async (HttpContext context, string definitionId, FishingRequest request, FishingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "delete", request, ct)));
        return endpoints;
    }
}
