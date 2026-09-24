// Owns Shop preview and lifecycle decisions. The repository owns transactions;
// the existing runtime publisher exports committed changes, never live game state.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ShopAuthoringService(
    ShopRepository repository,
    IRuntimeCatalogPublisher publisher, ILogger<ShopAuthoringService> logger)
{
    public Task<AuthoringOperationResult<ShopOptions>> LoadOptionsAsync(CancellationToken cancellationToken) =>
        GuardAsync(async () => AuthoringOperationResult<ShopOptions>.Success(new(await repository.LoadItemsAsync(cancellationToken), await repository.ListAsync(null, cancellationToken))));

    public Task<AuthoringOperationResult<ShopCatalogResponse>> ListAsync(string? search, CancellationToken cancellationToken) =>
        GuardAsync(async () => AuthoringOperationResult<ShopCatalogResponse>.Success(
            new(await repository.ListAsync(search, cancellationToken))));

    public Task<AuthoringOperationResult<ShopDefinition>> LoadAsync(string definitionId, CancellationToken cancellationToken) =>
        GuardAsync(async () => await repository.LoadAsync(definitionId, cancellationToken) is { } definition
            ? AuthoringOperationResult<ShopDefinition>.Success(definition)
            : AuthoringOperationResult<ShopDefinition>.Failure(Error("shop_not_found", "Definition not found.")));

    public Task<AuthoringOperationResult<ShopPreview>> PreviewAsync(string definitionId,
        ShopRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var operation = request.TargetOperation ?? "save_draft";
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = await ValidateOperationAsync(definitionId, operation, request, existing, cancellationToken);
        var changes = new List<ShopChange>();
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
        return AuthoringOperationResult<ShopPreview>.Success(new(operation,
            !messages.Any(message => message.Severity == ValidationSeverity.Error),
            Signature(definitionId, operation, request), messages, changes));
    });

    // Revalidate the preview against current durable facts, apply with the expected
    // version, then reload and verify before refreshing any runtime-visible catalog.
    public Task<AuthoringOperationResult<ShopMutation>> MutateAsync(string definitionId, string operation,
        ShopRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = await ValidateOperationAsync(definitionId, operation, request, existing, cancellationToken);
        if (request.PreviewSignature != Signature(definitionId, operation, request))
            messages.Add(Error("preview_signature_mismatch", "Preview this operation again before applying it."));
        if (messages.Any(message => message.Severity == ValidationSeverity.Error))
            return AuthoringOperationResult<ShopMutation>.Failure(messages);
        var saved = await repository.MutateAsync(definitionId, operation, request.Draft,
            request.ExpectedUpdatedAtUtc, cancellationToken);
        var verified = await repository.LoadAsync(definitionId, cancellationToken);
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(verified))
            throw new InvalidOperationException("Shop mutation failed reload-and-verify.");
        if (operation is "publish" or "disable" or "delete" || existing?.PublicationState == "Published")
            messages.AddRange(await publisher.PublishCatalogsAsync(RuntimeCatalogPublicationScope.Shop, cancellationToken));
        return AuthoringOperationResult<ShopMutation>.Success(new(operation, verified, messages));
    });

    private async Task<List<ApiError>> ValidateOperationAsync(string definitionId, string operation, ShopRequest request,
        ShopDefinition? existing, CancellationToken cancellationToken)
    {
        var messages = new List<ApiError>();
        if (operation is not ("save_draft" or "publish" or "disable" or "delete"))
            messages.Add(Error("invalid_operation", "Choose Save Draft, Publish, Disable or Delete."));
        if (!StableId(definitionId)) messages.Add(Error("invalid_definition_id", "Use a lowercase snake-case definition ID.", "definition_id"));
        if (existing?.UpdatedAtUtc != request.ExpectedUpdatedAtUtc)
            messages.Add(Error("shop_version_conflict", "Definition changed; reload before editing."));
        if (existing is null && operation != "save_draft")
            messages.Add(Error("shop_not_found", "Save a draft first."));
        if (operation != "save_draft" && existing is not null && JsonSerializer.Serialize(existing.Draft) != JsonSerializer.Serialize(request.Draft))
            messages.Add(Error("unsaved_shop_changes", "Save edited fields as a draft before this operation."));
        if (operation == "delete" && existing?.PublicationState != "Disabled")
            messages.Add(Error("shop_still_published", "Disable the definition before deleting it."));
        var draft = request.Draft;
        if (draft is null)
        {
            messages.Add(Error("missing_draft", "The complete definition draft is required."));
            return messages;
        }
        if (string.IsNullOrWhiteSpace(draft.DisplayName)) messages.Add(Error("missing_name", "Display name is required.", "display_name"));
        if (draft.PriceChangePerStockPercent is < 0 or > 100)
            messages.Add(Error("invalid_price_rate", "Price change per stock must be an integer from 0 through 100.", "price_change_per_stock_percent"));
        if (draft.Stock is null)
        {
            messages.Add(Error("missing_stock", "Stock must be an ordered array."));
            return messages;
        }
        var items = (await repository.LoadItemsAsync(cancellationToken)).ToDictionary(item => item.ItemId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in draft.Stock)
        {
            if (row is null || !StableId(row.ItemId) || !seen.Add(row.ItemId) || row.DefaultStock < 0 || row.RestockTicks <= 0)
            {
                messages.Add(Error("invalid_stock", "Stock requires unique item IDs, non-negative default stock and positive restock ticks.", "stock"));
                continue;
            }
            if (!items.TryGetValue(row.ItemId, out var item))
                messages.Add(Error("missing_stock_item", $"Item '{row.ItemId}' does not exist.", "stock"));
            else if (operation == "publish" && (!item.RuntimeEnabled ||
                (row.DefaultStock > 0
                    ? item.ShopPolicy is not ("npc_sells" or "npc_buys_and_sells") || item.NpcSellPrice is null or < 0
                    : item.ShopPolicy is not ("npc_buys" or "npc_buys_and_sells") || item.NpcBuyPrice is null or < 0)))
                messages.Add(Error("incompatible_stock_item", $"Item '{row.ItemId}' must be Published and permit NPC selling for positive stock or NPC buying for zero stock.", "stock"));
        }
        if (operation is "save_draft" or "disable" or "delete" &&
            await repository.HasNpcReferencesAsync(definitionId, operation != "delete", cancellationToken))
            messages.Add(Error("shop_npc_dependency", "NPC definitions reference this Shop. Fix/unpublish Published NPCs before unpublishing the Shop; remove every NPC reference before deleting it."));
        return messages;
    }

    private static bool StableId(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9_]*$");
    private static ApiError Error(string code, string message, string? field = null) => new(code, message, ValidationSeverity.Error, field);
    private static string Signature(string definitionId, string operation, ShopRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { definitionId, operation, request.Draft, expected = request.ExpectedUpdatedAtUtc?.ToUniversalTime() })))).ToLowerInvariant();

    private async Task<AuthoringOperationResult<T>> GuardAsync<T>(Func<Task<AuthoringOperationResult<T>>> action)
    {
        try { return await action(); }
        catch (ShopConcurrencyException)
        { return AuthoringOperationResult<T>.Failure(Error("shop_version_conflict", "Definition changed; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState == "23505")
        { return AuthoringOperationResult<T>.Failure(Error("shop_version_conflict", "Definition or child identity already exists; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState is "P0001" or "23503" or "23514")
        { return AuthoringOperationResult<T>.Failure(Error("shop_dependency_or_validation", error.MessageText)); }
        catch (Exception error) when (error is NpgsqlException or AuthoringDatabaseUnavailableException)
        {
            logger.LogError(error, "Shop database operation failed.");
            return AuthoringOperationResult<T>.Failure(Error("database_unavailable", "Shop database operation failed. Check schema health and host logs."));
        }
    }
}
