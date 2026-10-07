// Owns Lore rules preview and lifecycle decisions. The repository owns transactions;
// the existing runtime publisher exports committed changes, never live game state.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class LoreAuthoringService(
    LoreRepository repository,
    IRuntimeCatalogPublisher publisher, ILogger<LoreAuthoringService> logger)
{
    public Task<AuthoringOperationResult<LoreCatalogResponse>> ListAsync(string? search, CancellationToken cancellationToken) =>
        GuardAsync(async () => AuthoringOperationResult<LoreCatalogResponse>.Success(
            new(await repository.ListAsync(search, cancellationToken))));

    public Task<AuthoringOperationResult<LoreDefinition>> LoadAsync(string definitionId, CancellationToken cancellationToken) =>
        GuardAsync(async () => await repository.LoadAsync(definitionId, cancellationToken) is { } definition
            ? AuthoringOperationResult<LoreDefinition>.Success(definition)
            : AuthoringOperationResult<LoreDefinition>.Failure(Error("lore_not_found", "Definition not found.")));

    public Task<AuthoringOperationResult<LorePreview>> PreviewAsync(string definitionId,
        LoreRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var operation = request.TargetOperation ?? "save_draft";
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        if (operation == "publish" && request.Draft is not null)
            messages.AddRange((await repository.ValidatePublishedAsync(request.Draft, cancellationToken)).Select(message => Error("invalid_reference", message)));
        var changes = new List<LoreChange>();
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
        var nextState = operation == "publish" ? "Published" : "Draft";
        if (existing?.PublicationState != nextState) changes.Add(new("publication_state", existing?.PublicationState, nextState));
        return AuthoringOperationResult<LorePreview>.Success(new(operation,
            !messages.Any(message => message.Severity == ValidationSeverity.Error),
            Signature(definitionId, operation, request), messages, changes));
    });

    // Revalidate the preview against current durable facts, apply with the expected
    // version, then reload and verify before refreshing any runtime-visible catalog.
    public Task<AuthoringOperationResult<LoreMutation>> MutateAsync(string definitionId, string operation,
        LoreRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        if (operation == "publish" && request.Draft is not null)
            messages.AddRange((await repository.ValidatePublishedAsync(request.Draft, cancellationToken)).Select(message => Error("invalid_reference", message)));
        if (request.PreviewSignature != Signature(definitionId, operation, request))
            messages.Add(Error("preview_signature_mismatch", "Preview this operation again before applying it."));
        if (messages.Any(message => message.Severity == ValidationSeverity.Error))
            return AuthoringOperationResult<LoreMutation>.Failure(messages);
        var saved = await repository.MutateAsync(definitionId, operation, request.Draft!,
            request.ExpectedUpdatedAtUtc, cancellationToken);
        var verified = await repository.LoadAsync(definitionId, cancellationToken);
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(verified))
            throw new InvalidOperationException("Lore rules mutation failed reload-and-verify.");
        if (existing?.PublicationState == "Published" || saved?.PublicationState == "Published")
            messages.AddRange(await publisher.PublishCatalogsAsync(RuntimeCatalogPublicationScope.Lore, cancellationToken));
        return AuthoringOperationResult<LoreMutation>.Success(new(operation, verified, messages));
    });

    private List<ApiError> ValidateOperation(string definitionId, string operation, LoreRequest request,
        LoreDefinition? existing)
    {
        var messages = new List<ApiError>();
        if (operation is not ("save_draft" or "publish"))
            messages.Add(Error("invalid_operation", "Choose Save Draft or Publish."));
        if (definitionId != "v1") messages.Add(Error("invalid_definition_id", "Use a lowercase snake-case definition ID.", "definition_id"));
        if (existing?.UpdatedAtUtc != request.ExpectedUpdatedAtUtc)
            messages.Add(Error("lore_version_conflict", "Definition changed; reload before editing."));
        if (existing is null && operation != "save_draft")
            messages.Add(Error("lore_not_found", "Save a draft first."));
        if (operation != "save_draft" && existing is not null && JsonSerializer.Serialize(existing.Draft) != JsonSerializer.Serialize(request.Draft))
            messages.Add(Error("unsaved_lore_changes", "Save edited fields as a draft before this operation."));
        var draft = request.Draft;
        if (draft is null)
        {
            messages.Add(Error("missing_draft", "The complete definition draft is required."));
            return messages;
        }
        if (definitionId != "v1") messages.Add(Error("definition_id_mismatch", "Route and draft recipe IDs must match."));
        messages.AddRange(draft.Validate().Select(message => Error("invalid_recipe", message)));
        return messages;
    }

    private static ApiError Error(string code, string message, string? field = null) => new(code, message, ValidationSeverity.Error, field);
    private static string Signature(string definitionId, string operation, LoreRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { definitionId, operation, request.Draft, expected = request.ExpectedUpdatedAtUtc?.ToUniversalTime() })))).ToLowerInvariant();

    private async Task<AuthoringOperationResult<T>> GuardAsync<T>(Func<Task<AuthoringOperationResult<T>>> action)
    {
        try { return await action(); }
        catch (LoreConcurrencyException)
        { return AuthoringOperationResult<T>.Failure(Error("lore_version_conflict", "Definition changed; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState == "23505")
        { return AuthoringOperationResult<T>.Failure(Error("lore_version_conflict", "Definition or child identity already exists; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState is "23514" or "P0001")
        { return AuthoringOperationResult<T>.Failure(Error("publication_reference_conflict", error.MessageText)); }
        catch (Exception error) when (error is NpgsqlException or AuthoringDatabaseUnavailableException)
        {
            logger.LogError(error, "Lore rules database operation failed.");
            return AuthoringOperationResult<T>.Failure(Error("database_unavailable", "Lore rules database operation failed. Check schema health and host logs."));
        }
    }
}
