// Browser transport only: the existing Dialogue service owns normalization, graph and lifecycle decisions.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserDialogue
{
    public static void MapBrowserDialogue(this WebApplication app)
    {
        var dialogue = app.MapGroup("/studio/api/dialogue");
        dialogue.MapGet("", async (string? search, DialogueAuthoringService service, CancellationToken ct) => Result(await service.ListAsync(search, ct)));
        dialogue.MapGet("/options", async (DialogueAuthoringService service, CancellationToken ct) => Result(await service.LoadOptionsAsync(ct)));
        dialogue.MapGet("/{id}", async (string id, DialogueAuthoringService service, CancellationToken ct) => Result(await service.LoadAsync(id, ct)));
        // Read-only saved-definition analysis. Its signature never becomes an Apply permission in the UI.
        dialogue.MapGet("/{id}/diagnostics", async (string id, string operation, DialogueAuthoringService service, CancellationToken ct) =>
        {
            var loaded = await service.LoadAsync(id, ct);
            if (!loaded.Succeeded || loaded.Value is not { } definition) return Result(loaded);
            var request = new PreviewDialogueRequest(definition.DisplayName, definition.SchemaVersion, definition.EntryPoints,
                definition.Nodes, definition.MetadataDescription, definition.Notes, definition.UpdatedAtUtc, operation);
            var result = await service.PreviewAsync(id, request, ct);
            return !result.Succeeded ? Result(result) : Json(new { success = true,
                data = new { definition_version = definition.UpdatedAtUtc, preview = result.Value }, errors = Array.Empty<ApiError>() });
        });
        // Saved simulation is available to read-only viewers. Its state is supplied by
        // this tab on each call; no session, authored row or live character is written.
        dialogue.MapGet("/{id}/playthrough", async (string id, HttpRequest request, DialogueAuthoringService service, CancellationToken ct) =>
        {
            var state = request.Headers["X-Studio-Playthrough"].ToString();
            if (state.Length > 20000) return Error("invalid_request", "Simulation state exceeds its size limit.", 413);
            var value = System.Text.Json.JsonSerializer.Deserialize<PreviewDialoguePlaythroughRequest>(state, JsonOptions);
            if (value is null || value.Draft is not null) return Error("invalid_request", "Saved simulation does not accept a draft.");
            var loaded = await service.LoadAsync(id, ct);
            if (!loaded.Succeeded || loaded.Value is not { } definition) return Result(loaded);
            var draft = new DialogueDraft(definition.DisplayName, definition.SchemaVersion, definition.EntryPoints, definition.Nodes,
                definition.MetadataDescription, definition.Notes, definition.UpdatedAtUtc, null);
            var result = await service.PreviewPlaythroughAsync(id, value with { Draft = draft }, ct);
            return !result.Succeeded ? Result(result) : Json(new { success = true,
                data = new { definition_version = definition.UpdatedAtUtc, simulation = result.Value }, errors = Array.Empty<ApiError>() });
        });
        dialogue.MapPost("/{id}/playthrough", async (string id, HttpRequest request, DialogueAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<PreviewDialoguePlaythroughRequest>(request, ct);
            if (value is null) return Error("invalid_request", "A simulation request is required.");
            if (value.Draft is not null && PreserveNodes(value.Draft.Nodes) is { } loss) return loss;
            return Result(await service.PreviewPlaythroughAsync(id, value, ct));
        });
        dialogue.MapPost("/{id}/preview", async (string id, HttpRequest request, DialogueAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<PreviewDialogueRequest>(request, ct);
            if (value is null) return Error("invalid_request", "A complete dialogue is required.");
            if (PreserveNodes(value.Nodes) is { } loss) return loss;
            var result = await service.PreviewAsync(id, value, ct);
            if (!result.Succeeded) return Result(result);
            var normalized = DialogueAuthoringService.Normalize(value);
            return Json(new { success = true, data = new { preview = result.Value,
                normalized_draft = new { display_name = normalized.DisplayName, schema_version = normalized.SchemaVersion, entry_points = normalized.EntryPoints, nodes = normalized.Nodes, metadata_description = normalized.MetadataDescription, notes = normalized.Notes } }, errors = Array.Empty<ApiError>() });
        });
        dialogue.MapPut("/{id}/draft", async (string id, HttpRequest request, DialogueAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<DialogueMutationRequest>(request, ct);
            if (value is not null && PreserveNodes(value.Nodes) is { } loss) return loss;
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview the complete dialogue first.")
                : Mutation(await service.SaveDraftAsync(id, value, ct), false);
        });
        dialogue.MapPost("/{id}/publish", async (string id, HttpRequest request, DialogueAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<DialogueLifecycleRequest>(request, ct);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview publication first.")
                : Mutation(await service.PublishAsync(id, value, ct), true);
        });
        dialogue.MapPost("/{id}/disable", async (string id, HttpRequest request, DialogueAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<DialogueLifecycleRequest>(request, ct);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview disabling first.")
                : Mutation(await service.DisableAsync(id, value, ct), false);
        });
        dialogue.MapPost("/{id}/delete", async (string id, HttpRequest request, DialogueAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<DialogueDeleteRequest>(request, ct);
            if (string.IsNullOrWhiteSpace(value?.PreviewSignature)) return Error("preview_required", "Preview deletion first.");
            var result = await service.DeleteAsync(id, value, ct);
            return !result.Succeeded || result.Value is not { } saved ? Result(result) : Json(new { success = true,
                data = new { saved.Operation, saved.DeletedId, saved.Messages, definition = (object?)null,
                    database_committed = true, catalog_export = Export(saved.Messages, false), live_game_restarted = false }, errors = Array.Empty<ApiError>() }, catalogName: "Dialogue");
        });
    }
    // The legacy normalizer intentionally removes incompatible node fields. A browser
    // edit must remove them explicitly instead of silently losing a connection or choice.
    private static IResult? PreserveNodes(IReadOnlyList<DialogueNode>? nodes)
    {
        if (nodes is null) return Error("invalid_request", "The complete node array is required.");
        foreach (var node in nodes)
        {
            if (node is null || node.Choices is null) return Error("invalid_request", "Complete nodes and choice arrays are required.");
            var normalized = DialogueDomainRules.NormalizeNode(node);
            if (node.Choices.Count > 0 && normalized.Choices.Count == 0
                || !string.IsNullOrWhiteSpace(node.NextNodeId) && normalized.NextNodeId is null
                || !node.Dismissible && normalized.Dismissible)
                return Error("dialogue_fields_would_be_removed", $"Node '{node.NodeId}' has fields incompatible with its type. Explicitly remove choices or next target, or enable Dismissible for an End node, before continuing.");
        }
        return null;
    }
    private static IResult Mutation(AuthoringOperationResult<DialogueMutationResponse> result, bool exportRequested) =>
        !result.Succeeded || result.Value is not { } saved ? Result(result) : Json(new { success = true,
            data = new { saved.Operation, definition = saved.Dialogue, saved.Messages, database_committed = true,
                catalog_export = Export(saved.Messages, exportRequested), live_game_restarted = false }, errors = Array.Empty<ApiError>() }, catalogName: "Dialogue");
    private static string Export(IReadOnlyList<ApiError> messages, bool requested) => !requested ? "not_requested"
        : messages.Any(error => error.Code == "map_catalog_publish_skipped") ? "skipped"
        : messages.Any(error => error.Code == "map_catalog_publish_warning") ? "failed" : "succeeded";
    private static IResult Result<T>(AuthoringOperationResult<T> result)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(error => error.Code == "dialogue_not_found") ? 404
            : result.Errors.Any(error => error.Code == "dialogue_version_conflict") ? 409
            : result.Errors.Any(error => error.Code == "dialogue_reload_verification_failed") ? 500
            : result.Errors.Any(error => error.Code is "database_unavailable" or "dialogue_database_unavailable" or "dialogue_schema_unavailable") ? 503 : 400;
        var errors = result.Errors.Select(error => error.Code is "database_unavailable" or "dialogue_database_unavailable" or "dialogue_schema_unavailable"
            ? error with { Message = "Dialogue database operation failed. Check schema health and host logs." } : error).ToArray();
        return Json(new { success = result.Succeeded, data = result.Value, errors }, status, "Dialogue");
    }
}
