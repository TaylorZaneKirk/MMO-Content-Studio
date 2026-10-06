// Owns World Object preview and lifecycle decisions. The repository owns transactions;
// the existing runtime publisher exports committed changes, never live game state.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class WorldObjectAuthoringService(
    WorldObjectRepository repository, ItemAssetService assets,
    IRuntimeCatalogPublisher publisher, ILogger<WorldObjectAuthoringService> logger)
{
    public AuthoringOperationResult<WorldObjectOptions> LoadOptions() =>
        AuthoringOperationResult<WorldObjectOptions>.Success(new(
            ["Draft", "Published", "Disabled"], assets.GetGameAssetsRoot(),
            new("New World Object", false, 1, 1, "res://assets/maps/objects/world_objects/", 32, 32, 0, 0, 1, null, [], [])));

    public Task<AuthoringOperationResult<WorldObjectCatalogResponse>> ListAsync(string? search, CancellationToken cancellationToken) =>
        GuardAsync(async () => AuthoringOperationResult<WorldObjectCatalogResponse>.Success(
            new(await repository.ListAsync(search, cancellationToken))));

    public Task<AuthoringOperationResult<WorldObjectDefinition>> LoadAsync(string definitionId, CancellationToken cancellationToken) =>
        GuardAsync(async () => await repository.LoadAsync(definitionId, cancellationToken) is { } definition
            ? AuthoringOperationResult<WorldObjectDefinition>.Success(definition)
            : AuthoringOperationResult<WorldObjectDefinition>.Failure(Error("world_object_not_found", "Definition not found.")));

    public Task<AuthoringOperationResult<WorldObjectPreview>> PreviewAsync(string definitionId,
        WorldObjectRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var operation = request.TargetOperation ?? "save_draft";
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        var changes = new List<WorldObjectChange>();
        if (request.Draft is not null)
        {
            var before = existing is null ? default : JsonSerializer.SerializeToElement(existing.Draft);
            foreach (var field in JsonSerializer.SerializeToElement(request.Draft).EnumerateObject())
            {
                JsonElement previous = default;
                if (before.ValueKind == JsonValueKind.Object) before.TryGetProperty(field.Name, out previous);
                if (previous.ValueKind == JsonValueKind.Undefined || previous.GetRawText() != field.Value.GetRawText())
                    changes.Add(new(field.Name, previous.ValueKind == JsonValueKind.Undefined ? null : previous.Clone(), field.Value.Clone()));
            }
        }
        var nextState = operation switch { "publish" => "Published", "disable" => "Disabled", "delete" => null, _ => "Draft" };
        if (existing?.PublicationState != nextState) changes.Add(new("publication_state", existing?.PublicationState, nextState));
        return AuthoringOperationResult<WorldObjectPreview>.Success(new(operation,
            !messages.Any(message => message.Severity == ValidationSeverity.Error),
            Signature(definitionId, operation, request), messages, changes));
    });

    // Revalidate the preview against current durable facts, apply with the expected
    // version, then reload and verify before refreshing any runtime-visible catalog.
    public Task<AuthoringOperationResult<WorldObjectMutation>> MutateAsync(string definitionId, string operation,
        WorldObjectRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        if (request.PreviewSignature != Signature(definitionId, operation, request))
            messages.Add(Error("preview_signature_mismatch", "Preview this operation again before applying it."));
        if (messages.Any(message => message.Severity == ValidationSeverity.Error))
            return AuthoringOperationResult<WorldObjectMutation>.Failure(messages);
        var saved = await repository.MutateAsync(definitionId, operation, request.Draft,
            request.ExpectedUpdatedAtUtc, cancellationToken);
        var verified = await repository.LoadAsync(definitionId, cancellationToken);
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(verified))
            throw new InvalidOperationException("World Object mutation failed reload-and-verify.");
        if (existing?.PublicationState == "Published" || saved?.PublicationState == "Published")
            messages.AddRange(await publisher.PublishCatalogsAsync(RuntimeCatalogPublicationScope.WorldObject, cancellationToken));
        return AuthoringOperationResult<WorldObjectMutation>.Success(new(operation, verified, messages));
    });

    private List<ApiError> ValidateOperation(string definitionId, string operation, WorldObjectRequest request,
        WorldObjectDefinition? existing)
    {
        var messages = new List<ApiError>();
        if (operation is not ("save_draft" or "publish" or "disable" or "delete"))
            messages.Add(Error("invalid_operation", "Choose Save Draft, Publish, Disable or Delete."));
        if (!StableId(definitionId)) messages.Add(Error("invalid_definition_id", "Use a lowercase snake-case definition ID.", "definition_id"));
        if (existing?.UpdatedAtUtc != request.ExpectedUpdatedAtUtc)
            messages.Add(Error("world_object_version_conflict", "Definition changed; reload before editing."));
        if (existing is null && operation != "save_draft")
            messages.Add(Error("world_object_not_found", "Save a draft first."));
        if (operation != "save_draft" && existing is not null && JsonSerializer.Serialize(existing.Draft) != JsonSerializer.Serialize(request.Draft))
            messages.Add(Error("unsaved_world_object_changes", "Save edited fields as a draft before this operation."));
        if (operation == "delete" && existing?.PublicationState == "Published")
            messages.Add(Error("world_object_still_published", "Disable the definition before deleting it."));
        var draft = request.Draft;
        if (draft is null)
        {
            messages.Add(Error("missing_draft", "The complete definition draft is required."));
            return messages;
        }
        if (string.IsNullOrWhiteSpace(draft.DisplayName)) messages.Add(Error("missing_name", "Display name is required.", "display_name"));
        if (draft.SourceWidth <= 0 || draft.SourceHeight <= 0 || draft.FootprintWidthTiles <= 0 || draft.FootprintHeightTiles <= 0)
            messages.Add(Error("invalid_dimensions", "Source and footprint dimensions must be positive integers."));
        if (!double.IsFinite(draft.VisualAnchorOffsetX) || !double.IsFinite(draft.VisualAnchorOffsetY)
            || !double.IsFinite(draft.VisualRenderScale) || draft.VisualRenderScale <= 0)
            messages.Add(Error("invalid_visual_numbers", "Anchors must be finite and render scale must be positive."));
        if (draft.VisualAnimationFps is { } fps && (!double.IsFinite(fps) || fps <= 0))
            messages.Add(Error("invalid_animation_fps", "Animation FPS must be positive when supplied."));
        if (draft.PublicInteractions is null || draft.VisualAnimationFrames is null)
        {
            messages.Add(Error("missing_children", "Interactions and animation frames must be arrays."));
            return messages;
        }
        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in draft.PublicInteractions)
            if (action is null || !StableId(action.ActionId) || string.IsNullOrWhiteSpace(action.Label) || !actionIds.Add(action.ActionId))
                messages.Add(Error("invalid_interaction", "Each interaction needs a unique snake-case action ID and a label."));
        if (draft.PublicInteractions.Count(action => action?.IsDefault == true) > 1)
            messages.Add(Error("multiple_default_actions", "At most one public interaction can be default."));
        if (draft.VisualAnimationFrames.Count > 0 && draft.VisualAnimationFps is null)
            messages.Add(Error("missing_animation_fps", "Supply FPS when animation frames exist."));
        foreach (var path in new[] { draft.VisualTexturePath }.Concat(draft.VisualAnimationFrames))
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("res://assets/", StringComparison.Ordinal)
                || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.Contains(".."))
                messages.Add(Error("invalid_texture_path", "Use a canonical res://assets/... PNG path."));
            else if (operation is "save_draft" or "publish")
            {
                var resolution = assets.ResolveGameAssetPng(path, "World Object texture");
                if (!resolution.Exists)
                    messages.Add(new("missing_texture", resolution.Message ?? "Texture is unavailable.",
                        operation == "publish" ? ValidationSeverity.Error : ValidationSeverity.Warning, "visual_texture_path"));
            }
        }
        if (existing?.PublicationState == "Published" && operation is "save_draft" or "disable")
            messages.Add(new("placement_references", "This removes the definition from runtime export. Update any Tiled placements that reference it before regenerating maps.", ValidationSeverity.Warning));
        return messages;
    }

    private static bool StableId(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9_]*$");
    private static ApiError Error(string code, string message, string? field = null) => new(code, message, ValidationSeverity.Error, field);
    private static string Signature(string definitionId, string operation, WorldObjectRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { definitionId, operation, request.Draft, expected = request.ExpectedUpdatedAtUtc?.ToUniversalTime() })))).ToLowerInvariant();

    private async Task<AuthoringOperationResult<T>> GuardAsync<T>(Func<Task<AuthoringOperationResult<T>>> action)
    {
        try { return await action(); }
        catch (WorldObjectConcurrencyException)
        { return AuthoringOperationResult<T>.Failure(Error("world_object_version_conflict", "Definition changed; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState == "23505")
        { return AuthoringOperationResult<T>.Failure(Error("world_object_version_conflict", "Definition or child identity already exists; reload and preview again.")); }
        catch (Exception error) when (error is NpgsqlException or AuthoringDatabaseUnavailableException)
        {
            logger.LogError(error, "World Object database operation failed.");
            return AuthoringOperationResult<T>.Failure(Error("database_unavailable", "World Object database operation failed. Check schema health and host logs."));
        }
    }
}
