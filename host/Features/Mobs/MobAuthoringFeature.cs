using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Mobs;

public static class MobAuthoringFeature
{
    public static IServiceCollection AddMobAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<MobRepository>();
        services.AddSingleton<MobAuthoringRegistry>();
        services.AddSingleton<MobDefinitionValidator>();
        services.AddSingleton<MobAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, MobSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, MobCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapMobAuthoring(
        this IEndpointRouteBuilder endpoints)
    {
        var mobs = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/mobs");

        mobs.MapGet("/options", async (
            HttpContext context,
            MobAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadOptionsAsync(cancellationToken)));

        mobs.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            MobAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ListAsync(search, cancellationToken)));

        mobs.MapGet("/{mobDefinitionId}", async (
            HttpContext context,
            string mobDefinitionId,
            MobAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(mobDefinitionId, cancellationToken)));

        mobs.MapPost("/{mobDefinitionId}/preview", async (
            HttpContext context,
            string mobDefinitionId,
            MobPreviewRequest request,
            MobAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewAsync(mobDefinitionId, request, cancellationToken)));

        mobs.MapPut("/{mobDefinitionId}/draft", async (
            HttpContext context,
            string mobDefinitionId,
            SaveMobDraftRequest request,
            MobAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveDraftAsync(mobDefinitionId, request, cancellationToken)));

        mobs.MapPost("/{mobDefinitionId}/publish", async (
            HttpContext context,
            string mobDefinitionId,
            MobPublicationRequest request,
            MobAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PublishAsync(mobDefinitionId, request, cancellationToken)));

        mobs.MapPost("/{mobDefinitionId}/disable", async (
            HttpContext context,
            string mobDefinitionId,
            MobPublicationRequest request,
            MobAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DisableAsync(mobDefinitionId, request, cancellationToken)));

        mobs.MapPost("/{mobDefinitionId}/delete", async (
            HttpContext context,
            string mobDefinitionId,
            DeleteMutationRequest request,
            MobAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DeleteAsync(mobDefinitionId, request, cancellationToken)));

        return endpoints;
    }
}
