// Browser-only Fishing transport. FishingAuthoringService remains the lifecycle decision owner.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserFishing
{
    public static void MapBrowserFishing(this WebApplication app)
    {
        var methods = app.MapGroup("/studio/api/fishing");
        methods.MapGet("", async (string? search, FishingAuthoringService service, CancellationToken ct) =>
            Result(await service.ListAsync(search, ct)));
        methods.MapGet("/{id}", async (string id, FishingAuthoringService service, CancellationToken ct) =>
            Result(await service.LoadAsync(id, ct)));
        methods.MapPost("/{id}/preview", async (string id, HttpRequest request, FishingAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<FishingRequest>(request, ct);
            return value is null ? Error("invalid_request", "A complete Fishing method is required.") : Result(await service.PreviewAsync(id, value, ct));
        });
        methods.MapPut("/{id}/draft", (string id, HttpRequest request, FishingAuthoringService service, CancellationToken ct) =>
            Mutate(id, "save_draft", request, service, ct));
        foreach (var operation in new[] { "publish", "disable", "delete" })
        {
            var action = operation;
            methods.MapPost("/{id}/" + action.Replace('_', '-'), (string id, HttpRequest request, FishingAuthoringService service, CancellationToken ct) =>
                Mutate(id, action, request, service, ct));
        }
    }

    private static async Task<IResult> Mutate(string id, string operation, HttpRequest request, FishingAuthoringService service, CancellationToken ct)
    {
        var value = await Read<FishingRequest>(request, ct);
        if (value is null || string.IsNullOrWhiteSpace(value.PreviewSignature))
            return Error("preview_required", "Preview the complete Fishing method before applying it.");
        var before = await service.LoadAsync(id, ct);
        if (!before.Succeeded && !before.Errors.All(error => error.Code == "fishing_not_found")) return Result(before);
        var exportRequested = operation is "publish" or "disable" or "delete";
        var result = await service.MutateAsync(id, operation, value, ct);
        if (!result.Succeeded || result.Value is not { } saved) return Result(result);
        var export = !exportRequested ? "not_requested"
            : saved.Messages.Any(error => error.Code == "map_catalog_publish_skipped") ? "skipped"
            : saved.Messages.Any(error => error.Code == "map_catalog_publish_warning") ? "failed" : "succeeded";
        return Json(new { success = true, data = new { saved.Operation, saved.Definition, saved.Messages,
            database_committed = true, catalog_export = export, live_game_restarted = false }, errors = Array.Empty<ApiError>() }, catalogName: "Fishing");
    }

    private static IResult Result<T>(AuthoringOperationResult<T> result)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(error => error.Code == "fishing_not_found") ? 404
            : result.Errors.Any(error => error.Code == "fishing_version_conflict") ? 409
            : result.Errors.Any(error => error.Code == "database_unavailable") ? 503 : 400;
        return Json(new { success = result.Succeeded, data = result.Value, errors = result.Errors }, status, "Fishing");
    }
}
