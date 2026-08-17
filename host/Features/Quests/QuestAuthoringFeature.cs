using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Quests;

public static class QuestAuthoringFeature
{
    public static IServiceCollection AddQuestAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<IQuestRepository, QuestRepository>();
        services.AddSingleton<QuestAuthoringRegistry>();
        services.AddSingleton<QuestDefinitionValidator>();
        services.AddSingleton<QuestAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, QuestSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, QuestCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapQuestAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var quests = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/quests");

        quests.MapGet("/options", async (
            HttpContext context,
            QuestAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadOptionsAsync(cancellationToken)));

        quests.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            QuestAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(context, await service.ListAsync(search, cancellationToken)));

        quests.MapGet("/{questId}", async (
            HttpContext context,
            string questId,
            QuestAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadAsync(questId, cancellationToken)));

        quests.MapPost("/{questId}/preview", async (
            HttpContext context,
            string questId,
            PreviewQuestRequest request,
            QuestAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(context, await service.PreviewAsync(questId, request, cancellationToken)));

        quests.MapPut("/{questId}/draft", async (
            HttpContext context,
            string questId,
            QuestMutationRequest request,
            QuestAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(context, await service.SaveDraftAsync(questId, request, cancellationToken)));

        quests.MapPost("/{questId}/publish", async (
            HttpContext context,
            string questId,
            QuestLifecycleRequest request,
            QuestAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(context, await service.PublishAsync(questId, request, cancellationToken)));

        quests.MapPost("/{questId}/disable", async (
            HttpContext context,
            string questId,
            QuestLifecycleRequest request,
            QuestAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(context, await service.DisableAsync(questId, request, cancellationToken)));

        quests.MapPost("/{questId}/delete", async (
            HttpContext context,
            string questId,
            QuestDeleteRequest request,
            QuestAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(context, await service.DeleteAsync(questId, request, cancellationToken)));

        return endpoints;
    }
}
