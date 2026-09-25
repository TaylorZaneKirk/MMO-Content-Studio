// Registers the reusable Shop workspace through the existing authoring host.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.Shops;

public static class ShopAuthoringFeature
{
    public static IServiceCollection AddShopAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<ShopRepository>();
        services.AddSingleton<ShopAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, ShopSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, ShopCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapShopAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var shops = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/shops");
        shops.MapGet("/options", async (HttpContext context, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadOptionsAsync(ct)));
        shops.MapGet("", async (HttpContext context, string? search, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.ListAsync(search, ct)));
        shops.MapGet("/{definitionId}", async (HttpContext context, string definitionId, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadAsync(definitionId, ct)));
        shops.MapPost("/{definitionId}/preview", async (HttpContext context, string definitionId, ShopRequest request, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.PreviewAsync(definitionId, request, ct)));
        shops.MapPut("/{definitionId}/draft", async (HttpContext context, string definitionId, ShopRequest request, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_draft", request, ct)));
        shops.MapPost("/{definitionId}/save-and-publish", async (HttpContext context, string definitionId, ShopRequest request, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_and_publish", request, ct)));
        shops.MapPost("/{definitionId}/publish", async (HttpContext context, string definitionId, ShopRequest request, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "publish", request, ct)));
        shops.MapPost("/{definitionId}/disable", async (HttpContext context, string definitionId, ShopRequest request, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "disable", request, ct)));
        shops.MapPost("/{definitionId}/delete", async (HttpContext context, string definitionId, ShopRequest request, ShopAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "delete", request, ct)));
        return endpoints;
    }
}
