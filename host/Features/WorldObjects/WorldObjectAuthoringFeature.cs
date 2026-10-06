// Registers the reusable World Object workspace through the existing authoring host.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.WorldObjects;

public static class WorldObjectAuthoringFeature
{
    public static IServiceCollection AddWorldObjectAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<WorldObjectRepository>();
        services.AddSingleton<WorldObjectAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, WorldObjectSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, WorldObjectCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapWorldObjectAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var objects = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/world-objects");
        objects.MapGet("/options", (HttpContext context, WorldObjectAuthoringService service) =>
            AuthoringHttpResults.FromOperation(context, service.LoadOptions()));
        objects.MapGet("", async (HttpContext context, string? search, WorldObjectAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.ListAsync(search, ct)));
        objects.MapGet("/{definitionId}", async (HttpContext context, string definitionId, WorldObjectAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadAsync(definitionId, ct)));
        objects.MapPost("/{definitionId}/preview", async (HttpContext context, string definitionId, WorldObjectRequest request, WorldObjectAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.PreviewAsync(definitionId, request, ct)));
        objects.MapPut("/{definitionId}/draft", async (HttpContext context, string definitionId, WorldObjectRequest request, WorldObjectAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_draft", request, ct)));
        objects.MapPost("/{definitionId}/publish", async (HttpContext context, string definitionId, WorldObjectRequest request, WorldObjectAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "publish", request, ct)));
        objects.MapPost("/{definitionId}/disable", async (HttpContext context, string definitionId, WorldObjectRequest request, WorldObjectAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "disable", request, ct)));
        objects.MapPost("/{definitionId}/delete", async (HttpContext context, string definitionId, WorldObjectRequest request, WorldObjectAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "delete", request, ct)));
        return endpoints;
    }
}
