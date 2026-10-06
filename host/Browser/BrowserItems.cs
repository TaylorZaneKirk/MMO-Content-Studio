// Thin browser transport: the existing item service owns validation and mutations.
// Int64 values use decimal strings here; the desktop JSON contract is unchanged.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Browser;

public static class BrowserItems
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { NumberHandling = JsonNumberHandling.AllowReadingFromString, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new LongStringConverter());
        return options;
    }

    public static void MapBrowserItems(this WebApplication app)
    {
        var items = app.MapGroup("/studio/api/items");
        items.MapGet("", async (string? search, UnifiedItemAuthoringService service, CancellationToken cancellation) =>
            Operation(await service.ListAsync(search, cancellation)));
        items.MapGet("/options", async (UnifiedItemAuthoringService service, CancellationToken cancellation) =>
            Operation(await service.LoadOptionsAsync(cancellation)));
        items.MapGet("/{itemId}", async (string itemId, UnifiedItemAuthoringService service, IUnifiedItemRepository repository, CancellationToken cancellation) =>
        {
            var result = await service.LoadAsync(itemId, cancellation);
            if (result.Value is not { } item) return Operation(result);
            var raw = await repository.LoadAsync(itemId, cancellation);
            if (raw is null || raw.UpdatedAtUtc != item.UpdatedAtUtc) return Error("item_version_conflict", "The item changed while loading. Reload it.", 409);
            // Show every stored pose, including data the legacy normalizer would otherwise drop.
            if (item.Equipment is not null) item = item with { Equipment = item.Equipment with { EquippedVisual = raw.EquippedVisual } };
            return Operation(AuthoringOperationResult<ItemDefinition>.Success(item));
        });
        items.MapPost("/{itemId}/preview", async (string itemId, HttpRequest request, UnifiedItemAuthoringService service, IUnifiedItemRepository repository, CancellationToken cancellation) =>
        {
            var value = await Read<PreviewItemRequest>(request, cancellation);
            if (value is not null && await PreserveFields(itemId, value.Equipment, repository, cancellation) is { } error) return error;
            return value is null ? Error("invalid_request", "A complete item is required.") : Operation(await service.PreviewAsync(itemId, value, cancellation));
        });
        items.MapPut("/{itemId}/draft", async (string itemId, HttpRequest request, UnifiedItemAuthoringService service, IUnifiedItemRepository repository, CancellationToken cancellation) =>
        {
            var exportRequested = (await repository.LoadAsync(itemId, cancellation))?.RuntimeEnabled == true;
            var value = await Read<SaveItemDraftRequest>(request, cancellation);
            if (value is not null && await PreserveFields(itemId, value.Equipment, repository, cancellation) is { } error) return error;
            return value is null ? Error("invalid_request", "A complete item is required.") : Mutation(await service.SaveDraftAsync(itemId, value, cancellation), exportRequested);
        });
        items.MapPost("/{itemId}/save-and-publish", async (string itemId, HttpRequest request, UnifiedItemAuthoringService service, IUnifiedItemRepository repository, CancellationToken cancellation) =>
        {
            var exportRequested = (await repository.LoadAsync(itemId, cancellation))?.RuntimeEnabled == true;
            var value = await Read<SaveItemDraftRequest>(request, cancellation);
            if (value is not null && await PreserveFields(itemId, value.Equipment, repository, cancellation) is { } error) return error;
            return value is null ? Error("invalid_request", "A complete item is required.") : Mutation(await service.SaveAndPublishAsync(itemId, value, cancellation), exportRequested);
        });
        items.MapPost("/{itemId}/publish", async (string itemId, HttpRequest request, UnifiedItemAuthoringService service, IUnifiedItemRepository repository, CancellationToken cancellation) =>
        {
            var value = await Read<ItemPublicationRequest>(request, cancellation);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview this operation first.") : Mutation(await service.PublishAsync(itemId, value, cancellation), true);
        });
        items.MapPost("/{itemId}/disable", async (string itemId, HttpRequest request, UnifiedItemAuthoringService service, IUnifiedItemRepository repository, CancellationToken cancellation) =>
        {
            var value = await Read<ItemPublicationRequest>(request, cancellation);
            return string.IsNullOrWhiteSpace(value?.PreviewSignature) ? Error("preview_required", "Preview this operation first.") : Mutation(await service.DisableAsync(itemId, value, cancellation), true);
        });
        items.MapPost("/{itemId}/delete", async (string itemId, HttpRequest request, UnifiedItemAuthoringService service, IUnifiedItemRepository repository, CancellationToken cancellation) =>
        {
            var value = await Read<DeleteMutationRequest>(request, cancellation);
            return value is null ? Error("invalid_request", "Preview this operation first.") : Operation(await service.DeleteAsync(itemId, value, cancellation));
        });
        app.MapGet("/studio/api/assets", (BrowserItemAssets assets) => Json(assets.Catalog()));
        app.MapGet("/studio/api/asset", (string resource, BrowserItemAssets assets) => assets.Image(resource));
        app.MapPost("/studio/api/assets/upload", async (HttpRequest request, string name, BrowserItemAssets assets, CancellationToken cancellation) =>
            await assets.Upload(request, name, cancellation));
    }

    private static async Task<IResult?> PreserveFields(string itemId, ItemEquipmentMetadataDraft? equipment,
        IUnifiedItemRepository repository, CancellationToken cancellation)
    {
        if (equipment is null) return null; // Explicitly removing equipment is a reviewed domain operation.
        var normalized = UnifiedItemDomainRules.Normalize("", "", null, equipment, null).Equipment;
        if (normalized is null || equipment.WeaponProfile is not null && normalized.WeaponProfile is null)
            return Error("equipment_fields_would_be_removed", "Select a valid equipment slot, or explicitly remove the incompatible weapon profile before previewing.");
        if (equipment.EquippedVisual is not { } visual) return null;
        var normalizedVisual = normalized.EquippedVisual;
        if (normalizedVisual is null || !Same(visual.GripAnchors ?? new Dictionary<string, IReadOnlyDictionary<string, SourcePixelPointDefinition>>(), normalizedVisual.GripAnchors)
            || !Same(visual.FlipXByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(), normalizedVisual.FlipXByPose)
            || !Same(visual.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(), normalizedVisual.HiddenPoses)
            || !Same(visual.ItemOverGripByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(), normalizedVisual.ItemOverGripByPose))
            return Error("pose_data_not_roundtrippable", "Existing pose data cannot be saved losslessly by the current authoring rules. Keep the item unchanged and inspect it in desktop Studio.");
        var existing = await repository.LoadAsync(itemId, cancellation);
        if (existing?.EquippedVisual is { } original
            && (!Same(original.Nudge, visual.Nudge) || original.SecondarySocketId != visual.SecondarySocketId
                || !Same(original.GripAnchors, visual.GripAnchors)
                || !Same(original.FlipXByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(), visual.FlipXByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>())
                || !Same(original.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(), visual.HiddenPoses ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>())
                || !Same(original.ItemOverGripByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>(), visual.ItemOverGripByPose ?? new Dictionary<string, IReadOnlyDictionary<string, bool>>())))
            return Error("pose_settings_read_only", "Positioning and pose settings are read-only in the browser. Reload after editing them in desktop Studio.");
        return null;
    }
    private static bool Same(object? left, object? right) => JsonNode.DeepEquals(
        JsonSerializer.SerializeToNode(left, JsonOptions), JsonSerializer.SerializeToNode(right, JsonOptions));

    private static async Task<T?> Read<T>(HttpRequest request, CancellationToken cancellation)
    {
        // Whole aggregates are bounded independently of the larger PNG upload limit.
        if (request.ContentLength is > 1024 * 1024) throw new BadHttpRequestException("Item payload too large.", 413);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await request.Body.ReadAsync(bytes, cancellation)) > 0)
        {
            if (buffer.Length + count > 1024 * 1024) throw new BadHttpRequestException("Item payload too large.", 413);
            buffer.Write(bytes, 0, count);
        }
        return JsonSerializer.Deserialize<T>(buffer.ToArray(), JsonOptions);
    }

    private static IResult Mutation(AuthoringOperationResult<ItemMutationResponse> result, bool exportRequested)
    {
        if (!result.Succeeded || result.Value is not { } value) return Operation(result);
        var export = !exportRequested ? "not_requested"
            : value.Messages.Any(message => message.Code == "map_catalog_publish_skipped") ? "skipped"
            : value.Messages.Any(message => message.Code == "map_catalog_publish_warning") ? "failed" : "succeeded";
        return Json(new { success = true, data = new { value.Operation, value.Item, value.Messages,
            database_committed = true, catalog_export = export, live_game_restarted = false }, errors = Array.Empty<ApiError>() });
    }

    public static IResult Operation<T>(AuthoringOperationResult<T> result)
    {
        var status = result.Succeeded ? 200 : result.Errors.Any(error => error.Code == "item_not_found") ? 404
            : result.Errors.Any(error => error.Code == "item_version_conflict") ? 409
            : result.Errors.Any(error => error.Code == "database_unavailable") ? 503
            : result.Errors.Any(error => error.Code == "item_operation_failed") ? 500 : 400;
        return Json(new { success = result.Succeeded, data = result.Value, errors = result.Errors }, status);
    }
    public static IResult Error(string code, string message, int status = 400) =>
        Json(new { success = false, errors = new[] { new ApiError(code, message, ValidationSeverity.Error) } }, status);

    public static IResult Json<T>(T value, int status = 200)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonOptions);
        RemoveHostPaths(node);
        return Results.Json(node, JsonOptions, statusCode: status);
    }
    private static void RemoveHostPaths(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var name in new[] { "file_path", "asset_preview_file_path", "source_path" }) obj.Remove(name);
            if (obj["code"]?.GetValue<string>() is "map_catalog_publish_warning" or "map_catalog_publish_skipped")
            {
                obj["message"] = "The database change completed, but the equipment visual catalog was not refreshed.";
                obj["remediation"] = "Inspect host logs and run the existing export command on the host. Do not repeat the item mutation to retry an export.";
            }
            // Rig discovery diagnostics may include filesystem paths. The browser only needs availability.
            if (obj.ContainsKey("rigs") && obj.ContainsKey("available")) obj["message"] = obj["available"]?.GetValue<bool>() == true ? null : "Actor rig catalog is unavailable. Inspect host configuration.";
            foreach (var child in obj.ToArray()) RemoveHostPaths(child.Value);
        }
        else if (node is JsonArray array) foreach (var child in array) RemoveHostPaths(child);
    }
    private sealed class LongStringConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String ? long.Parse(reader.GetString()!, CultureInfo.InvariantCulture) : reader.GetInt64();
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
