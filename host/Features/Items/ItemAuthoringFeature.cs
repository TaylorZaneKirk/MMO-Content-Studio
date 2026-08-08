using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Items;

public static class ItemAuthoringFeature
{
    public static IServiceCollection AddItemAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<ActorAppearanceCatalogService>();
        services.AddSingleton<ItemAssetService>();
		services.AddSingleton<CompositeActorVisualValidator>();
        services.AddSingleton<ItemAssetAuthoringService>();
        services.AddSingleton<ItemAuthoringRegistry>();
        services.AddSingleton<IUnifiedItemRepository, UnifiedItemRepository>();
        services.AddSingleton<UnifiedItemValidator>();
        services.AddSingleton<UnifiedItemAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, ItemSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, ItemCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapItemAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var root = endpoints.MapGroup(AuthoringApi.RoutePrefix);
        var items = root.MapGroup("/items");

        root.MapGet("/assets/items", (
            HttpContext context,
            ItemAssetService assetService) =>
            AuthoringHttpResults.Ok(context, assetService.LoadCatalog()));

        root.MapPost("/assets/items/import", async (
            HttpContext context,
            ImportItemAssetRequest request,
            ItemAssetAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ImportAsync(request, cancellationToken)));

        items.MapGet("/options", async (
            HttpContext context,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadOptionsAsync(cancellationToken)));

        items.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ListAsync(search, cancellationToken)));

        items.MapGet("/{itemId}", async (
            HttpContext context,
            string itemId,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(itemId, cancellationToken)));

        items.MapPost("/{itemId}/preview", async (
            HttpContext context,
            string itemId,
            PreviewItemRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewAsync(itemId, request, cancellationToken)));

        items.MapPut("/{itemId}/draft", async (
            HttpContext context,
            string itemId,
            SaveItemDraftRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveDraftAsync(itemId, request, cancellationToken)));

        items.MapPost("/{itemId}/publish", async (
            HttpContext context,
            string itemId,
            ItemPublicationRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PublishAsync(itemId, request, cancellationToken)));

        items.MapPost("/{itemId}/disable", async (
            HttpContext context,
            string itemId,
            ItemPublicationRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DisableAsync(itemId, request, cancellationToken)));

        items.MapPost("/{itemId}/delete", async (
            HttpContext context,
            string itemId,
            DeleteMutationRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DeleteAsync(itemId, request, cancellationToken)));

        return endpoints;
    }
}
