// Owns Blacksmithing recipe preview and lifecycle decisions. The repository owns transactions;
// the existing runtime publisher exports committed changes, never live game state.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class BlacksmithingAuthoringService(
    BlacksmithingRepository repository,
    IRuntimeCatalogPublisher publisher, ILogger<BlacksmithingAuthoringService> logger)
{
    public Task<AuthoringOperationResult<BlacksmithingCatalogResponse>> ListAsync(string? search, CancellationToken cancellationToken) =>
        GuardAsync(async () => AuthoringOperationResult<BlacksmithingCatalogResponse>.Success(
            new(await repository.ListAsync(search, cancellationToken))));

    public Task<AuthoringOperationResult<BlacksmithingDefinition>> LoadAsync(string definitionId, CancellationToken cancellationToken) =>
        GuardAsync(async () => await repository.LoadAsync(definitionId, cancellationToken) is { } definition
            ? AuthoringOperationResult<BlacksmithingDefinition>.Success(definition)
            : AuthoringOperationResult<BlacksmithingDefinition>.Failure(Error("blacksmithing_not_found", "Definition not found.")));

    public Task<AuthoringOperationResult<BlacksmithingPreview>> PreviewAsync(string definitionId,
        BlacksmithingRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var operation = request.TargetOperation ?? "save_draft";
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        if (operation == "publish" && request.Draft is not null)
            messages.AddRange((await repository.ValidatePublishedAsync(request.Draft, cancellationToken)).Select(message => Error("invalid_reference", message)));
        var changes = new List<BlacksmithingChange>();
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
        return AuthoringOperationResult<BlacksmithingPreview>.Success(new(operation,
            !messages.Any(message => message.Severity == ValidationSeverity.Error),
            Signature(definitionId, operation, request), messages, changes));
    });

    // Revalidate the preview against current durable facts, apply with the expected
    // version, then reload and verify before refreshing any runtime-visible catalog.
    public Task<AuthoringOperationResult<BlacksmithingMutation>> MutateAsync(string definitionId, string operation,
        BlacksmithingRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        if (operation == "publish" && request.Draft is not null)
            messages.AddRange((await repository.ValidatePublishedAsync(request.Draft, cancellationToken)).Select(message => Error("invalid_reference", message)));
        if (request.PreviewSignature != Signature(definitionId, operation, request))
            messages.Add(Error("preview_signature_mismatch", "Preview this operation again before applying it."));
        if (messages.Any(message => message.Severity == ValidationSeverity.Error))
            return AuthoringOperationResult<BlacksmithingMutation>.Failure(messages);
        var saved = await repository.MutateAsync(definitionId, operation, request.Draft!,
            request.ExpectedUpdatedAtUtc, cancellationToken);
        var verified = await repository.LoadAsync(definitionId, cancellationToken);
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(verified))
            throw new InvalidOperationException("Blacksmithing recipe mutation failed reload-and-verify.");
        if (existing?.PublicationState == "Published" || saved?.PublicationState == "Published")
            messages.AddRange(await publisher.PublishCatalogsAsync(RuntimeCatalogPublicationScope.Blacksmithing, cancellationToken));
        return AuthoringOperationResult<BlacksmithingMutation>.Success(new(operation, verified, messages));
    });

    private List<ApiError> ValidateOperation(string definitionId, string operation, BlacksmithingRequest request,
        BlacksmithingDefinition? existing)
    {
        var messages = new List<ApiError>();
        if (operation is not ("save_draft" or "publish" or "disable" or "delete"))
            messages.Add(Error("invalid_operation", "Choose Save Draft, Publish, Disable or Delete."));
        if (!StableId(definitionId)) messages.Add(Error("invalid_definition_id", "Use a lowercase snake-case definition ID.", "definition_id"));
        if (existing?.UpdatedAtUtc != request.ExpectedUpdatedAtUtc)
            messages.Add(Error("blacksmithing_version_conflict", "Definition changed; reload before editing."));
        if (existing is null && operation != "save_draft")
            messages.Add(Error("blacksmithing_not_found", "Save a draft first."));
        if (operation != "save_draft" && existing is not null && JsonSerializer.Serialize(existing.Draft) != JsonSerializer.Serialize(request.Draft))
            messages.Add(Error("unsaved_blacksmithing_changes", "Save edited fields as a draft before this operation."));
        if (operation == "delete" && existing?.PublicationState == "Published")
            messages.Add(Error("blacksmithing_still_published", "Disable the definition before deleting it."));
        var draft = request.Draft;
        if (draft is null)
        {
            messages.Add(Error("missing_draft", "The complete definition draft is required."));
            return messages;
        }
        if (draft.RecipeId != definitionId) messages.Add(Error("recipe_id_mismatch", "Route and draft recipe IDs must match."));
        messages.AddRange(draft.Validate().Select(message => Error("invalid_recipe", message)));
        return messages;
    }

    private static bool StableId(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9_]*$");
    private static ApiError Error(string code, string message, string? field = null) => new(code, message, ValidationSeverity.Error, field);
    private static string Signature(string definitionId, string operation, BlacksmithingRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { definitionId, operation, request.Draft, expected = request.ExpectedUpdatedAtUtc?.ToUniversalTime() })))).ToLowerInvariant();

    private async Task<AuthoringOperationResult<T>> GuardAsync<T>(Func<Task<AuthoringOperationResult<T>>> action)
    {
        try { return await action(); }
        catch (BlacksmithingConcurrencyException)
        { return AuthoringOperationResult<T>.Failure(Error("blacksmithing_version_conflict", "Definition changed; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState == "23505")
        { return AuthoringOperationResult<T>.Failure(Error("blacksmithing_version_conflict", "Definition or child identity already exists; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState is "23514" or "P0001")
        { return AuthoringOperationResult<T>.Failure(Error("publication_reference_conflict", error.MessageText)); }
        catch (Exception error) when (error is NpgsqlException or AuthoringDatabaseUnavailableException)
        {
            logger.LogError(error, "Blacksmithing recipe database operation failed.");
            return AuthoringOperationResult<T>.Failure(Error("database_unavailable", "Blacksmithing recipe database operation failed. Check schema health and host logs."));
        }
    }
}
