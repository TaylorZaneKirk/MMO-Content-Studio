using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.HandEquipment;

public static class HandEquipmentAuthoringFeature
{
    public static IServiceCollection AddHandEquipmentAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<HandEquipmentRepository>();
        services.AddSingleton<HandEquipmentAuthoringRegistry>();
        services.AddSingleton<HandEquipmentItemValidator>();
        services.AddSingleton<HandEquipmentAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, HandEquipmentSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, HandEquipmentCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapHandEquipmentAuthoring(
        this IEndpointRouteBuilder endpoints)
    {
        var handEquipment = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/hand-equipment");

        handEquipment.MapGet("/options", async (
            HttpContext context,
            HandEquipmentAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadOptionsAsync(cancellationToken)));

        handEquipment.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            HandEquipmentAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ListAsync(search, cancellationToken)));

        handEquipment.MapGet("/{itemId}", async (
            HttpContext context,
            string itemId,
            HandEquipmentAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(itemId, cancellationToken)));

        handEquipment.MapPost("/{itemId}/preview", async (
            HttpContext context,
            string itemId,
            HandEquipmentPreviewRequest request,
            HandEquipmentAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewAsync(itemId, request, cancellationToken)));

        handEquipment.MapPut("/{itemId}/draft", async (
            HttpContext context,
            string itemId,
            SaveHandEquipmentDraftRequest request,
            HandEquipmentAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveDraftAsync(itemId, request, cancellationToken)));

        handEquipment.MapPost("/{itemId}/publish", async (
            HttpContext context,
            string itemId,
            HandEquipmentPublicationRequest request,
            HandEquipmentAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PublishAsync(itemId, request, cancellationToken)));

        handEquipment.MapPost("/{itemId}/disable", async (
            HttpContext context,
            string itemId,
            HandEquipmentPublicationRequest request,
            HandEquipmentAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DisableAsync(itemId, request, cancellationToken)));

        return endpoints;
    }
}
