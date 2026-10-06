// Browser transport only: the existing Quest service owns normalization, graph and lifecycle decisions.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserQuests
{
    public static void MapBrowserQuests(this WebApplication app)
    {
        var quests = app.MapGroup("/studio/api/quests");
        quests.MapGet("", async (string? search, QuestAuthoringService service, CancellationToken ct) => Result(await service.ListAsync(search, ct)));
        quests.MapGet("/options", async (QuestAuthoringService service, CancellationToken ct) => Result(await service.LoadOptionsAsync(ct)));
        quests.MapGet("/{id}", async (string id, QuestAuthoringService service, CancellationToken ct) => Result(await service.LoadAsync(id, ct)));
        // Read-only saved-definition analysis. Its signature never becomes an Apply permission in the UI.
        quests.MapGet("/{id}/diagnostics", async (string id, string operation, QuestAuthoringService service, CancellationToken ct) =>
        {
            var loaded = await service.LoadAsync(id, ct);
            if (!loaded.Succeeded || loaded.Value is not { } definition) return Result(loaded);
            var request = new PreviewQuestRequest(definition.DisplayName, definition.SchemaVersion, definition.Steps,
                definition.Transitions, definition.UpdatedAtUtc, operation);
            var result = await service.PreviewAsync(id, request, ct);
            return !result.Succeeded ? Result(result) : Json(new { success = true,
                data = new { definition_version = definition.UpdatedAtUtc, preview = result.Value }, errors = Array.Empty<ApiError>() });
        });
        quests.MapPost("/{id}/preview", async (string id, HttpRequest request, QuestAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<PreviewQuestRequest>(request, ct);
            if (value is null) return Error("invalid_request", "A complete quest is required.");
            var result = await service.PreviewAsync(id, value, ct);
            if (!result.Succeeded) return Result(result);
            var normalized = QuestAuthoringService.Normalize(value);
            return Json(new { success = true, data = new { preview = result.Value,
                normalized_draft = new { display_name = normalized.DisplayName, schema_version = normalized.SchemaVersion, steps = normalized.Steps, transitions = normalized.Transitions } }, errors = Array.Empty<ApiError>() });
        });
        quests.MapPut("/{id}/draft", async (string id, HttpRequest request, QuestAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<QuestMutationRequest>(request, ct);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview the complete quest first.")
                : Mutation(await service.SaveDraftAsync(id, value, ct), false);
        });
        quests.MapPost("/{id}/publish", async (string id, HttpRequest request, QuestAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<QuestLifecycleRequest>(request, ct);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview publication first.")
                : Mutation(await service.PublishAsync(id, value, ct), true);
        });
        quests.MapPost("/{id}/disable", async (string id, HttpRequest request, QuestAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<QuestLifecycleRequest>(request, ct);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview disabling first.")
                : Mutation(await service.DisableAsync(id, value, ct), true);
        });
        quests.MapPost("/{id}/delete", async (string id, HttpRequest request, QuestAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<QuestDeleteRequest>(request, ct);
            if (string.IsNullOrWhiteSpace(value?.PreviewSignature)) return Error("preview_required", "Preview deletion first.");
            var result = await service.DeleteAsync(id, value, ct);
            return !result.Succeeded || result.Value is not { } saved ? Result(result) : Json(new { success = true,
                data = new { saved.Operation, saved.DeletedId, saved.Messages, definition = (object?)null,
                    database_committed = true, catalog_export = Export(saved.Messages, true), live_game_restarted = false }, errors = Array.Empty<ApiError>() }, catalogName: "Quest");
        });
    }
    private static IResult Mutation(AuthoringOperationResult<QuestMutationResponse> result, bool exportRequested) =>
        !result.Succeeded || result.Value is not { } saved ? Result(result) : Json(new { success = true,
            data = new { saved.Operation, definition = saved.Quest, saved.Messages, database_committed = true,
                catalog_export = Export(saved.Messages, exportRequested), live_game_restarted = false }, errors = Array.Empty<ApiError>() }, catalogName: "Quest");
    private static string Export(IReadOnlyList<ApiError> messages, bool requested) => !requested ? "not_requested"
        : messages.Any(error => error.Code == "map_catalog_publish_skipped") ? "skipped"
        : messages.Any(error => error.Code == "map_catalog_publish_warning") ? "failed" : "succeeded";
    private static IResult Result<T>(AuthoringOperationResult<T> result)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(error => error.Code == "quest_not_found") ? 404
            : result.Errors.Any(error => error.Code == "quest_version_conflict") ? 409
            : result.Errors.Any(error => error.Code == "quest_reload_verification_failed") ? 500
            : result.Errors.Any(error => error.Code == "database_unavailable") ? 503 : 400;
        var errors = result.Errors.Select(error => error.Code == "database_unavailable"
            ? error with { Message = "Quest database operation failed. Check schema health and host logs." } : error).ToArray();
        return Json(new { success = result.Succeeded, data = result.Value, errors }, status, "Quest");
    }
}
