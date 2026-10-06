// Mob browser transport; the existing service owns all authoring/lifecycle decisions.
using System.Text.Json;
using System.Text.Json.Nodes;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using static MMO.ContentStudio.AuthoringHost.Browser.BrowserJson;
namespace MMO.ContentStudio.AuthoringHost.Browser;
public static class BrowserMobs
{
    public static void MapBrowserMobs(this WebApplication app)
    {
        var routes = app.MapGroup("/studio/api/mobs");
        routes.MapGet("", async (string? search, MobAuthoringService service, BrowserActorAssets assets, CancellationToken ct) => Result(await service.ListAsync(search, ct), assets));
        routes.MapGet("/options", async (MobAuthoringService service, BrowserActorAssets assets, CancellationToken ct) => Result(await service.LoadOptionsAsync(ct), assets));
        routes.MapGet("/{id}", async (string id, MobAuthoringService service, BrowserActorAssets assets, CancellationToken ct) => Result(await service.LoadAsync(id, ct), assets));
        routes.MapPost("/{id}/preview", async (string id, HttpRequest request, MobAuthoringService service, BrowserActorAssets assets, CancellationToken ct) =>
        {
            var value = await Read<MobPreviewRequest>(request, ct);
            if (value is null) return Error("invalid_request", "A complete Mob draft is required.");
            if (value.TargetOperation?.Trim().ToLowerInvariant() == "save_draft"
                && await PreserveStoredMetadata(id, value.CompositeVisual, service, assets, ct) is { } storedLoss) return storedLoss;
            var normalized = MobAuthoringService.Normalize(value);
            if (Preserve(value.CompositeVisual, normalized.CompositeVisual, value.WanderRadiusTiles, normalized.WanderRadiusTiles, value.AggressionRadiusTiles, normalized.AggressionRadiusTiles, value.MobDetectionRadiusTiles, normalized.MobDetectionRadiusTiles, value.MobTargetScanIntervalMs, normalized.MobTargetScanIntervalMs, value.MobTargetScanCandidateLimit, normalized.MobTargetScanCandidateLimit) is { } loss) return loss;
            var result = await service.PreviewAsync(id, value, ct);
            if (!result.Succeeded) return Result(result, assets);
            var draft = JsonSerializer.SerializeToNode(normalized, new JsonSerializerOptions(JsonOptions) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!.AsObject();
            draft.Remove("expected_updated_at_utc"); draft.Remove("preview_signature");
            return assets.Response(new { success = true, data = new { preview = result.Value, normalized_draft = draft }, errors = Array.Empty<ApiError>() });
        });
        routes.MapPut("/{id}/draft", async (string id, HttpRequest request, MobAuthoringService service, BrowserActorAssets assets, CancellationToken ct) =>
        {
            var value = await Read<SaveMobDraftRequest>(request, ct);
            if (value is null || string.IsNullOrWhiteSpace(value.PreviewSignature)) return Error("preview_required", "Preview the complete Mob first.");
            if (await PreserveStoredMetadata(id, value.CompositeVisual, service, assets, ct) is { } storedLoss) return storedLoss;
            var normalized = MobAuthoringService.Normalize(value);
            if (Preserve(value.CompositeVisual, normalized.CompositeVisual, value.WanderRadiusTiles, normalized.WanderRadiusTiles, value.AggressionRadiusTiles, normalized.AggressionRadiusTiles, value.MobDetectionRadiusTiles, normalized.MobDetectionRadiusTiles, value.MobTargetScanIntervalMs, normalized.MobTargetScanIntervalMs, value.MobTargetScanCandidateLimit, normalized.MobTargetScanCandidateLimit) is { } loss) return loss;
            return Mutation(await service.SaveDraftAsync(id, value, ct), false, assets);
        });
        foreach (var action in new[] { "publish", "disable" })
        {
            var operation = action;
            routes.MapPost("/{id}/" + operation, async (string id, HttpRequest request, MobAuthoringService service, BrowserActorAssets assets, CancellationToken ct) =>
            {
                var value = await Read<MobPublicationRequest>(request, ct);
                if (string.IsNullOrWhiteSpace(value?.PreviewSignature)) return Error("preview_required", "Preview this operation first.");
                return Mutation(operation == "publish" ? await service.PublishAsync(id, value, ct) : await service.DisableAsync(id, value, ct), operation == "publish", assets);
            });
        }
        routes.MapPost("/{id}/delete", async (string id, HttpRequest request, MobAuthoringService service, BrowserActorAssets assets, CancellationToken ct) =>
        {
            var value = await Read<DeleteMutationRequest>(request, ct);
            if (string.IsNullOrWhiteSpace(value?.PreviewSignature)) return Error("preview_required", "Preview deletion first.");
            var result = await service.DeleteAsync(id, value, ct);
            return !result.Succeeded || result.Value is not { } saved ? Result(result, assets) : assets.Response(new { success = true,
                data = new { saved.Operation, saved.DeletedId, saved.Messages, definition = (object?)null, database_committed = true, catalog_export = "not_requested", live_game_restarted = false }, errors = Array.Empty<ApiError>() });
        });
    }
    // Sensitive extension fields may be omitted from browser responses. Inspect the
    // stored descriptor too, so an unrelated edit cannot unknowingly erase metadata.
    private static async Task<IResult?> PreserveStoredMetadata(string id, RiggedSpriteVisualDescriptor? replacement,
        MobAuthoringService service, BrowserActorAssets assets, CancellationToken ct)
    {
        if (replacement is null) return null; // Explicit whole-descriptor removal.
        var loaded = await service.LoadAsync(id, ct);
        if (!loaded.Succeeded)
            return loaded.Errors.Count > 0 && loaded.Errors.All(e => e.Code == "mob_not_found") ? null : Result(loaded, assets);
        return loaded.Value?.CompositeVisual?.ExtensionData?.Count > 0
            ? Error("mob_metadata_not_roundtrippable", "Stored composite metadata cannot be rewritten losslessly in this browser. Keep it unchanged, or explicitly remove the complete descriptor.")
            : null;
    }
    private static IResult? Preserve(RiggedSpriteVisualDescriptor? raw, RiggedSpriteVisualDescriptor? normalized, int radius, int normalizedRadius, int aggression, int normalizedAggression, int detection, int normalizedDetection, int interval, int normalizedInterval, int limit, int normalizedLimit)
    {
        if (raw is not null && (normalized is null || raw.SchemaVersion != normalized.SchemaVersion || raw.ExtensionData?.Count > 0
            || raw.FixedFrame != normalized.FixedFrame || !string.IsNullOrWhiteSpace(raw.FixedDirection) && normalized.FixedDirection is null)
            || radius != normalizedRadius || aggression != normalizedAggression || detection != normalizedDetection || interval != normalizedInterval || limit != normalizedLimit)
            return Error("mob_fields_would_be_removed", "Explicitly remove incompatible composite/fixed-pose/wander/aggression/target-scan fields before continuing. Unsupported descriptor metadata cannot be silently rewritten.");
        return null;
    }
    private static IResult Mutation(AuthoringOperationResult<MobMutationResponse> result, bool export, BrowserActorAssets assets)
    {
        if (!result.Succeeded || result.Value is not { } saved) return Result(result, assets);
        var outcome = !export ? "not_requested" : saved.Messages.Any(e => e.Code == "map_catalog_publish_skipped") ? "skipped"
            : saved.Messages.Any(e => e.Code == "map_catalog_publish_warning") ? "failed" : "succeeded";
        return assets.Response(new { success = true, data = new { saved.Operation, definition = saved.Mob, saved.Messages, database_committed = true, catalog_export = outcome, live_game_restarted = false }, errors = Array.Empty<ApiError>() });
    }
    private static IResult Result<T>(AuthoringOperationResult<T> result, BrowserActorAssets assets)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(e => e.Code == "mob_not_found") ? 404
            : result.Errors.Any(e => e.Code == "mob_version_conflict") ? 409
            : result.Errors.Any(e => e.Code == "mob_reload_verification_failed") ? 500
            : result.Errors.Any(e => e.Code is "database_unavailable") ? 503 : 400;
        return assets.Response(new { success = result.Succeeded, data = result.Value, errors = result.Errors }, status);
    }
}
