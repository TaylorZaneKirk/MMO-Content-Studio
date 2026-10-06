// Browser-only World Object transport. WorldObjectAuthoringService remains the lifecycle decision owner.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserWorldObjects
{
    public static void MapBrowserWorldObjects(this WebApplication app)
    {
        var worldObjects = app.MapGroup("/studio/api/world-objects");
        worldObjects.MapGet("", async (string? search, WorldObjectAuthoringService service, CancellationToken ct) =>
            Result(await service.ListAsync(search, ct)));
        worldObjects.MapGet("/options", (WorldObjectAuthoringService service) => Result(service.LoadOptions()));
        worldObjects.MapGet("/assets", (BrowserWorldObjectAssets assets) => Json(assets.Catalog()));
        worldObjects.MapGet("/asset", (string resource, BrowserWorldObjectAssets assets) => assets.Image(resource));
        worldObjects.MapPost("/assets/upload", (HttpRequest request, string name, BrowserWorldObjectAssets assets, CancellationToken ct) => assets.Upload(request, name, ct));
        worldObjects.MapGet("/{id}", async (string id, WorldObjectAuthoringService service, CancellationToken ct) =>
            Result(await service.LoadAsync(id, ct)));
        worldObjects.MapPost("/{id}/preview", async (string id, HttpRequest request, WorldObjectAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<WorldObjectRequest>(request, ct);
            return value is null ? Error("invalid_request", "A complete World Object is required.") : Result(await service.PreviewAsync(id, value, ct));
        });
        worldObjects.MapPut("/{id}/draft", (string id, HttpRequest request, WorldObjectAuthoringService service, CancellationToken ct) =>
            Mutate(id, "save_draft", request, service, ct));
        foreach (var operation in new[] { "publish", "disable", "delete" })
        {
            var action = operation;
            worldObjects.MapPost("/{id}/" + action.Replace('_', '-'), (string id, HttpRequest request, WorldObjectAuthoringService service, CancellationToken ct) =>
                Mutate(id, action, request, service, ct));
        }
    }

    private static async Task<IResult> Mutate(string id, string operation, HttpRequest request, WorldObjectAuthoringService service, CancellationToken ct)
    {
        var value = await Read<WorldObjectRequest>(request, ct);
        if (value is null || string.IsNullOrWhiteSpace(value.PreviewSignature))
            return Error("preview_required", "Preview the complete World Object before applying it.");
        var before = await service.LoadAsync(id, ct);
        if (!before.Succeeded && !before.Errors.All(error => error.Code == "world_object_not_found")) return Result(before);
        var exportRequested = operation == "publish" || before.Value?.PublicationState == "Published";
        var result = await service.MutateAsync(id, operation, value, ct);
        if (!result.Succeeded || result.Value is not { } saved) return Result(result);
        var export = !exportRequested ? "not_requested"
            : saved.Messages.Any(error => error.Code == "map_catalog_publish_skipped") ? "skipped"
            : saved.Messages.Any(error => error.Code == "map_catalog_publish_warning") ? "failed" : "succeeded";
        return Json(new { success = true, data = new { saved.Operation, saved.Definition, saved.Messages,
            database_committed = true, catalog_export = export, live_game_restarted = false }, errors = Array.Empty<ApiError>() }, catalogName: "World Object");
    }

    private static IResult Result<T>(AuthoringOperationResult<T> result)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(error => error.Code == "world_object_not_found") ? 404
            : result.Errors.Any(error => error.Code == "world_object_version_conflict") ? 409
            : result.Errors.Any(error => error.Code == "database_unavailable") ? 503 : 400;
        return Json(new { success = result.Succeeded, data = result.Value, errors = result.Errors }, status, "World Object");
    }
}
