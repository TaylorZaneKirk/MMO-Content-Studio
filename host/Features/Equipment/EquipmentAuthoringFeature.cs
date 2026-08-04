using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Equipment;

public static class EquipmentAuthoringFeature
{
    public static IServiceCollection AddEquipmentAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<EquipmentItemRepository>();
        services.AddSingleton<EquipmentItemValidator>();
        services.AddSingleton<EquipmentItemAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, EquipmentSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, EquipmentCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapEquipmentAuthoring(
        this IEndpointRouteBuilder endpoints)
    {
        var equipment = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/equipment");

        equipment.MapGet("/options", async (
            HttpContext context,
            EquipmentItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadOptionsAsync(cancellationToken)));

        equipment.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            EquipmentItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ListAsync(search, cancellationToken)));

        equipment.MapGet("/{itemId}", async (
            HttpContext context,
            string itemId,
            EquipmentItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(itemId, cancellationToken)));

        equipment.MapPost("/{itemId}/preview", async (
            HttpContext context,
            string itemId,
            EquipmentPreviewRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewEquipmentAsync(itemId, request, cancellationToken)));

        equipment.MapPut("/{itemId}/draft", async (
            HttpContext context,
            string itemId,
            SaveEquipmentDraftRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveEquipmentDraftAsync(itemId, request, cancellationToken)));

        equipment.MapPost("/{itemId}/publish", async (
            HttpContext context,
            string itemId,
            PublicationMutationRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PublishEquipmentAsync(
                    itemId,
                    request.ExpectedUpdatedAtUtc,
                    cancellationToken)));

        equipment.MapPost("/{itemId}/disable", async (
            HttpContext context,
            string itemId,
            PublicationMutationRequest request,
            UnifiedItemAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DisableEquipmentAsync(
                    itemId,
                    request.ExpectedUpdatedAtUtc,
                    cancellationToken)));

        equipment.MapPost("/{itemId}/delete", async (
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
