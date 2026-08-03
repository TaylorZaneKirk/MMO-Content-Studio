using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Consumables;

public static class ConsumableAuthoringFeature
{
    public static IServiceCollection AddConsumableAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<ConsumableItemRepository>();
        services.AddSingleton<ConsumableItemValidator>();
        services.AddSingleton<ConsumableItemAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, ConsumableSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, ConsumableCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapConsumableAuthoring(
        this IEndpointRouteBuilder endpoints)
    {
        var consumables = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/consumables");

        consumables.MapGet("/options", async (
            HttpContext context,
            ConsumableItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadOptionsAsync(cancellationToken)));

        consumables.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            ConsumableItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ListAsync(search, cancellationToken)));

        consumables.MapGet("/{itemId}", async (
            HttpContext context,
            string itemId,
            ConsumableItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(itemId, cancellationToken)));

        consumables.MapPost("/{itemId}/preview", async (
            HttpContext context,
            string itemId,
            ConsumablePreviewRequest request,
            ConsumableItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewAsync(itemId, request, cancellationToken)));

        consumables.MapPut("/{itemId}/draft", async (
            HttpContext context,
            string itemId,
            SaveConsumableDraftRequest request,
            ConsumableItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveDraftAsync(itemId, request, cancellationToken)));

        consumables.MapPost("/{itemId}/publish", async (
            HttpContext context,
            string itemId,
            PublicationMutationRequest request,
            ConsumableItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PublishAsync(
                    itemId,
                    request.ExpectedUpdatedAtUtc,
                    cancellationToken)));

        consumables.MapPost("/{itemId}/disable", async (
            HttpContext context,
            string itemId,
            PublicationMutationRequest request,
            ConsumableItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DisableAsync(
                    itemId,
                    request.ExpectedUpdatedAtUtc,
                    cancellationToken)));

        consumables.MapPost("/{itemId}/delete", async (
            HttpContext context,
            string itemId,
            DeleteMutationRequest request,
            ConsumableItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DeleteAsync(itemId, request, cancellationToken)));

        return endpoints;
    }
}
