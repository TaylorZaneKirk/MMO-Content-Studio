// Registers the focused Blacksmithing workspace through the existing host.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.Blacksmithing;

public static class BlacksmithingAuthoringFeature
{
    public static IServiceCollection AddBlacksmithingAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<BlacksmithingRepository>();
        services.AddSingleton<BlacksmithingAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, BlacksmithingSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, BlacksmithingCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapBlacksmithingAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var objects = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/blacksmithing-recipes");
        objects.MapGet("", async (HttpContext context, string? search, BlacksmithingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.ListAsync(search, ct)));
        objects.MapGet("/{definitionId}", async (HttpContext context, string definitionId, BlacksmithingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadAsync(definitionId, ct)));
        objects.MapPost("/{definitionId}/preview", async (HttpContext context, string definitionId, BlacksmithingRequest request, BlacksmithingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.PreviewAsync(definitionId, request, ct)));
        objects.MapPut("/{definitionId}/draft", async (HttpContext context, string definitionId, BlacksmithingRequest request, BlacksmithingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_draft", request, ct)));
        objects.MapPost("/{definitionId}/publish", async (HttpContext context, string definitionId, BlacksmithingRequest request, BlacksmithingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "publish", request, ct)));
        objects.MapPost("/{definitionId}/disable", async (HttpContext context, string definitionId, BlacksmithingRequest request, BlacksmithingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "disable", request, ct)));
        objects.MapPost("/{definitionId}/delete", async (HttpContext context, string definitionId, BlacksmithingRequest request, BlacksmithingAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "delete", request, ct)));
        return endpoints;
    }
}
