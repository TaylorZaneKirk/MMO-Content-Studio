// Browser-only Alchemy transport. AlchemyAuthoringService remains the lifecycle decision owner.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserAlchemy
{
    public static void MapBrowserAlchemy(this WebApplication app)
    {
        var recipes = app.MapGroup("/studio/api/alchemy-definitions");
        recipes.MapGet("", async (string? search, AlchemyAuthoringService service, CancellationToken ct) =>
            Result(await service.ListAsync(search, ct)));
        recipes.MapGet("/{id}", async (string id, AlchemyAuthoringService service, CancellationToken ct) =>
            Result(await service.LoadAsync(id, ct)));
        recipes.MapPost("/{id}/preview", async (string id, HttpRequest request, AlchemyAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<AlchemyRequest>(request, ct);
            return value is null ? Error("invalid_request", "A complete Alchemy recipe is required.") : Result(await service.PreviewAsync(id, value, ct));
        });
        recipes.MapPut("/{id}/draft", (string id, HttpRequest request, AlchemyAuthoringService service, CancellationToken ct) =>
            Mutate(id, "save_draft", request, service, ct));
        foreach (var operation in new[] { "publish", "disable", "delete" })
        {
            var action = operation;
            recipes.MapPost("/{id}/" + action.Replace('_', '-'), (string id, HttpRequest request, AlchemyAuthoringService service, CancellationToken ct) =>
                Mutate(id, action, request, service, ct));
        }
    }

    private static async Task<IResult> Mutate(string id, string operation, HttpRequest request, AlchemyAuthoringService service, CancellationToken ct)
    {
        var value = await Read<AlchemyRequest>(request, ct);
        if (value is null || string.IsNullOrWhiteSpace(value.PreviewSignature))
            return Error("preview_required", "Preview the complete Alchemy recipe before applying it.");
        var before = await service.LoadAsync(id, ct);
        if (!before.Succeeded && !before.Errors.All(error => error.Code == "alchemy_not_found")) return Result(before);
        var exportRequested = operation == "publish" || before.Value?.PublicationState == "Published";
        var result = await service.MutateAsync(id, operation, value, ct);
        if (!result.Succeeded || result.Value is not { } saved) return Result(result);
        var export = !exportRequested ? "not_requested"
            : saved.Messages.Any(error => error.Code == "map_catalog_publish_skipped") ? "skipped"
            : saved.Messages.Any(error => error.Code == "map_catalog_publish_warning") ? "failed" : "succeeded";
        return Json(new { success = true, data = new { saved.Operation, saved.Definition, saved.Messages,
            database_committed = true, catalog_export = export, live_game_restarted = false }, errors = Array.Empty<ApiError>() }, catalogName: "Alchemy");
    }

    private static IResult Result<T>(AuthoringOperationResult<T> result)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(error => error.Code == "alchemy_not_found") ? 404
            : result.Errors.Any(error => error.Code == "alchemy_version_conflict") ? 409
            : result.Errors.Any(error => error.Code == "database_unavailable") ? 503 : 400;
        return Json(new { success = result.Succeeded, data = result.Value, errors = result.Errors }, status, "Alchemy");
    }
}
