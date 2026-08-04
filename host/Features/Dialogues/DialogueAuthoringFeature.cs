using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.Dialogues;

public static class DialogueAuthoringFeature
{
    public static IServiceCollection AddDialogueAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<IDialogueRepository, DialogueRepository>();
        services.AddSingleton<DialogueAuthoringRegistry>();
        services.AddSingleton<DialogueGraphAnalyzer>();
        services.AddSingleton<DialogueDefinitionValidator>();
        services.AddSingleton<DialoguePlaythroughService>();
        services.AddSingleton<DialogueAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, DialogueSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, DialogueCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapDialogueAuthoring(
        this IEndpointRouteBuilder endpoints)
    {
        var dialogues = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/dialogues");

        dialogues.MapGet("/options", async (
            HttpContext context,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadOptionsAsync(cancellationToken)));

        dialogues.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ListAsync(search, cancellationToken)));

        dialogues.MapGet("/{dialogueDefinitionId}", async (
            HttpContext context,
            string dialogueDefinitionId,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(dialogueDefinitionId, cancellationToken)));

        dialogues.MapPost("/{dialogueDefinitionId}/preview", async (
            HttpContext context,
            string dialogueDefinitionId,
            PreviewDialogueRequest request,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewAsync(dialogueDefinitionId, request, cancellationToken)));

        dialogues.MapPost("/{dialogueDefinitionId}/playthrough", async (
            HttpContext context,
            string dialogueDefinitionId,
            PreviewDialoguePlaythroughRequest request,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewPlaythroughAsync(dialogueDefinitionId, request, cancellationToken)));

        dialogues.MapPut("/{dialogueDefinitionId}/draft", async (
            HttpContext context,
            string dialogueDefinitionId,
            DialogueMutationRequest request,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveDraftAsync(dialogueDefinitionId, request, cancellationToken)));

        dialogues.MapPost("/{dialogueDefinitionId}/publish", async (
            HttpContext context,
            string dialogueDefinitionId,
            DialogueLifecycleRequest request,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PublishAsync(dialogueDefinitionId, request, cancellationToken)));

        dialogues.MapPost("/{dialogueDefinitionId}/disable", async (
            HttpContext context,
            string dialogueDefinitionId,
            DialogueLifecycleRequest request,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DisableAsync(dialogueDefinitionId, request, cancellationToken)));

        dialogues.MapPost("/{dialogueDefinitionId}/delete", async (
            HttpContext context,
            string dialogueDefinitionId,
            DialogueDeleteRequest request,
            DialogueAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DeleteAsync(dialogueDefinitionId, request, cancellationToken)));

        return endpoints;
    }
}
