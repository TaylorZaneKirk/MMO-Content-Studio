// Browser-only Loot Table transport. Existing services own validation, EV and lifecycle.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserLootTables
{
    public static void MapBrowserLootTables(this WebApplication app)
    {
        var tables = app.MapGroup("/studio/api/loot-tables");
        tables.MapGet("", async (string? search, LootTableAuthoringService service, CancellationToken ct) => Result(await service.ListAsync(search, ct)));
        tables.MapGet("/options", async (LootTableAuthoringService service, CancellationToken ct) => Result(await service.LoadOptionsAsync(ct)));
        tables.MapGet("/{id}", async (string id, LootTableAuthoringService service, CancellationToken ct) => Result(await service.LoadAsync(id, ct)));
        tables.MapPost("/{id}/preview", async (string id, HttpRequest request, LootTableAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<LootTablePreviewRequest>(request, ct);
            return value is null ? Error("invalid_request", "A complete Loot Table is required.") : Result(await service.PreviewAsync(id, value, ct));
        });
        tables.MapPut("/{id}/draft", async (string id, HttpRequest request, LootTableAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<SaveLootTableDraftRequest>(request, ct);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview the complete table first.") : Mutation(await service.SaveDraftAsync(id, value, ct));
        });
        tables.MapPost("/{id}/publish", async (string id, HttpRequest request, LootTableAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<LootTablePublicationRequest>(request, ct);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview publication first.") : Mutation(await service.PublishAsync(id, value, ct));
        });
        tables.MapPost("/{id}/disable", async (string id, HttpRequest request, LootTableAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<LootTablePublicationRequest>(request, ct);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview disabling first.") : Mutation(await service.DisableAsync(id, value, ct));
        });
        tables.MapPost("/{id}/delete", async (string id, HttpRequest request, LootTableAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<DeleteMutationRequest>(request, ct);
            if (string.IsNullOrWhiteSpace(value?.PreviewSignature)) return Error("preview_required", "Preview deletion first.");
            var result = await service.DeleteAsync(id, value, ct);
            return !result.Succeeded || result.Value is not { } saved ? Result(result)
                : Json(new { success = true, data = new { saved.Operation, saved.DeletedId, saved.Messages, definition = (object?)null,
                    database_committed = true, catalog_export = "not_requested", live_game_restarted = false }, errors = Array.Empty<ApiError>() });
        });
    }
    private static IResult Mutation(AuthoringOperationResult<LootTableMutationResponse> result) =>
        !result.Succeeded || result.Value is not { } saved ? Result(result)
        : Json(new { success = true, data = new { saved.Operation, definition = saved.LootTable, saved.Messages,
            database_committed = true, catalog_export = "not_requested", live_game_restarted = false }, errors = Array.Empty<ApiError>() });

    private static IResult Result<T>(AuthoringOperationResult<T> result)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(error => error.Code == "loot_table_not_found") ? 404
            : result.Errors.Any(error => error.Code == "loot_table_version_conflict") ? 409
            : result.Errors.Any(error => error.Code == "database_unavailable") ? 503 : 400;
        return Json(new { success = result.Succeeded, data = result.Value, errors = result.Errors }, status);
    }
}
