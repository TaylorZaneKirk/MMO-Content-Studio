// Shared actor asset/calibration transport. Calibration remains a catalog-file operation,
// separate from the NPC/Mob database lifecycle and without a game restart/export.
using System.Text.Json;
using System.Text.Json.Nodes;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;

public sealed class BrowserActorAssets(ItemAssetService assets)
    : BrowserPngAssets(assets, "", "actors", "/studio/api/actor-appearance/image")
{
    public IResult Response<T>(T value, int status = 200)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonOptions);
        Sanitize(node);
        return Json(node, status, "NPC");
    }
    private void Sanitize(JsonNode? node)
    {
        if (node is JsonArray array) { foreach (var item in array) Sanitize(item); return; }
        if (node is not JsonObject obj) return;
        if (obj.ContainsKey("cosmetic_item_ids")) obj.Remove("containsLegacyBaseLayers");
        foreach (var key in new[] { "base_file_path", "file_path", "asset_preview_file_path" })
        {
            if (obj[key] is JsonValue path && path.TryGetValue<string>(out var value))
                obj[key == "base_file_path" ? "base_url" : key == "file_path" ? "url" : "asset_url"] = UrlForFile(value);
            obj.Remove(key);
        }
        foreach (var key in new[] { "rig_catalog_path", "calibration_catalog_path", "equipped_visual_catalog_path", "source_path", "game_assets_root" }) obj.Remove(key);
        foreach (var key in new[] { "rig_message", "calibration_message", "equipped_visual_message" })
            if (obj[key] is not null) obj[key] = "Inspect host logs for catalog availability details.";
        if (obj.ContainsKey("rigs") && obj["message"] is not null) obj["message"] = "Actor catalog availability is reported below. Inspect host logs for details.";
        foreach (var child in obj.ToArray()) Sanitize(child.Value);
    }
}

public static class BrowserActorAppearance
{
    public static void MapBrowserActorAppearance(this WebApplication app)
    {
        var routes = app.MapGroup("/studio/api/actor-appearance");
        routes.MapGet("/image", (string resource, BrowserActorAssets assets) => assets.Image(resource));
        routes.MapGet("/assets", (BrowserActorAssets assets) => Json(assets.Catalog()));
        routes.MapGet("/options", (ActorAppearanceCatalogService service, BrowserActorAssets assets) => assets.Response(service.LoadOptions()));
        routes.MapGet("/frames", (string actorKind, string resource, ActorCalibrationFrameResolver resolver, BrowserActorAssets assets) =>
        {
            if (!assets.CanRead(resource)) return Error("invalid_asset", "Choose an available canonical PNG in the configured asset folder.");
            return Result(resolver.Resolve(new CalibrationFrameRequest(actorKind, resource)), assets);
        });
        routes.MapGet("/calibrations/{id}", async (string id, ActorRigCalibrationAuthoringService service, BrowserActorAssets assets, CancellationToken ct) =>
            Result(await service.LoadAsync(id, ct), assets));
        routes.MapPut("/calibrations/{id}", async (string id, HttpRequest request, ActorRigCalibrationAuthoringService service, BrowserActorAssets assets, CancellationToken ct) =>
        {
            var value = await Read<SaveActorCalibrationRequest>(request, ct);
            if (value is null || string.IsNullOrWhiteSpace(value.ExpectedCatalogHash)) return Error("catalog_hash_required", "Load and review this calibration before saving.");
            if (value.VisualTexturePath is not null && !assets.CanRead(value.VisualTexturePath)) return Error("invalid_asset", "The calibration source must be a confined available PNG.");
            var result = await service.SaveAsync(id, value, ct);
            return !result.Succeeded ? Result(result, assets) : assets.Response(new { success = true,
                data = new { catalog_file_written = true, calibration = result.Value, database_committed = false, live_game_restarted = false }, errors = Array.Empty<ApiError>() });
        });
    }
    private static IResult Result<T>(AuthoringOperationResult<T> result, BrowserActorAssets assets)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(e => e.Code == "actor_calibration_catalog_conflict") ? 409
            : result.Errors.Any(e => e.Code.EndsWith("unavailable", StringComparison.Ordinal)) ? 503 : 400;
        return assets.Response(new { success = result.Succeeded, data = result.Value, errors = result.Errors }, status);
    }
}
