// Browser-only Spell transport. MagicSpellAuthoringService remains the lifecycle decision owner.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserSpells
{
    public static void MapBrowserSpells(this WebApplication app)
    {
        var spells = app.MapGroup("/studio/api/spells");
        spells.MapGet("/media", (string? kind, BrowserSpellMedia media) => Json(media.Catalog(kind == "audio")));
        spells.MapGet("/image", (string resource, BrowserSpellMedia media) => media.Read(resource, false));
        spells.MapGet("/audio", (string resource, BrowserSpellMedia media) => media.Read(resource, true));
        spells.MapGet("", async (string? search, MagicSpellAuthoringService service, CancellationToken ct) =>
            Result(await service.ListAsync(search, ct)));
        spells.MapGet("/options", () =>
            Json(new { defaults = new MagicSpellDraft("", 1, "air", 1, 1, 0, 0, 0) }));
        spells.MapGet("/{id}", async (string id, MagicSpellAuthoringService service, CancellationToken ct) =>
            Result(await service.LoadAsync(id, ct)));
        spells.MapPost("/{id}/preview", async (string id, HttpRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
        {
            var value = await Read<MagicSpellRequest>(request, ct);
            return value is null ? Error("invalid_request", "A complete Spell is required.") : Result(await service.PreviewAsync(id, value, ct));
        });
        spells.MapPut("/{id}/draft", (string id, HttpRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
            Mutate(id, "save_draft", request, service, ct));
        foreach (var operation in new[] { "save_and_publish", "publish", "disable", "delete" })
        {
            var action = operation;
            spells.MapPost("/{id}/" + action.Replace('_', '-'), (string id, HttpRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
                Mutate(id, action, request, service, ct));
        }
    }

    private static async Task<IResult> Mutate(string id, string operation, HttpRequest request, MagicSpellAuthoringService service, CancellationToken ct)
    {
        var value = await Read<MagicSpellRequest>(request, ct);
        if (value is null || string.IsNullOrWhiteSpace(value.PreviewSignature))
            return Error("preview_required", "Preview the complete Spell before applying it.");
        var result = await service.MutateAsync(id, operation, value, ct);
        if (!result.Succeeded || result.Value is not { } saved) return Result(result);
        return Json(new { success = true, data = new { saved.Operation, saved.Definition, saved.Messages,
            database_committed = true, catalog_export = "not_requested", live_game_restarted = false }, errors = Array.Empty<ApiError>() }, catalogName: "Spell");
    }

    private static IResult Result<T>(AuthoringOperationResult<T> result)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(error => error.Code == "spell_not_found") ? 404
            : result.Errors.Any(error => error.Code == "spell_version_conflict") ? 409
            : result.Errors.Any(error => error.Code == "database_unavailable") ? 503 : 400;
        return Json(new { success = result.Succeeded, data = result.Value, errors = result.Errors }, status, "Spell");
    }
}
