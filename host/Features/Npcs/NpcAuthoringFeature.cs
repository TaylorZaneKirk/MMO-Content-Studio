using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Npcs;

public static class NpcAuthoringFeature
{
    public static IServiceCollection AddNpcAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<ActorAppearanceCatalogService>();
        services.AddSingleton<INpcRepository, NpcRepository>();
        services.AddSingleton<NpcAuthoringRegistry>();
        services.AddSingleton<NpcDialogueReferenceProvider>();
        services.AddSingleton<NpcDefinitionValidator>();
        services.AddSingleton<NpcAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, NpcSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, NpcCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapNpcAuthoring(
        this IEndpointRouteBuilder endpoints)
    {
        var npcs = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/npcs");

        npcs.MapGet("/options", async (
            HttpContext context,
            NpcAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadOptionsAsync(cancellationToken)));

        npcs.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            NpcAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ListAsync(search, cancellationToken)));

        npcs.MapGet("/{npcDefinitionId}", async (
            HttpContext context,
            string npcDefinitionId,
            NpcAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(npcDefinitionId, cancellationToken)));

        npcs.MapPost("/{npcDefinitionId}/preview", async (
            HttpContext context,
            string npcDefinitionId,
            PreviewNpcRequest request,
            NpcAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewAsync(npcDefinitionId, request, cancellationToken)));

        npcs.MapPut("/{npcDefinitionId}/draft", async (
            HttpContext context,
            string npcDefinitionId,
            SaveNpcDraftRequest request,
            NpcAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveDraftAsync(npcDefinitionId, request, cancellationToken)));

        npcs.MapPost("/{npcDefinitionId}/publish", async (
            HttpContext context,
            string npcDefinitionId,
            NpcPublicationRequest request,
            NpcAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PublishAsync(npcDefinitionId, request, cancellationToken)));

        npcs.MapPost("/{npcDefinitionId}/disable", async (
            HttpContext context,
            string npcDefinitionId,
            NpcPublicationRequest request,
            NpcAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DisableAsync(npcDefinitionId, request, cancellationToken)));

        npcs.MapPost("/{npcDefinitionId}/delete", async (
            HttpContext context,
            string npcDefinitionId,
            NpcDeleteRequest request,
            NpcAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DeleteAsync(npcDefinitionId, request, cancellationToken)));

        return endpoints;
    }
}
