// Owns combat-spell validation, preview and lifecycle decisions. The repository
// atomically persists scalar content; no casting or gameplay state lives here.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class MagicSpellAuthoringService(
    MagicSpellRepository repository,
    ILogger<MagicSpellAuthoringService> logger)
{
    public Task<AuthoringOperationResult<MagicSpellCatalogResponse>> ListAsync(string? search, CancellationToken cancellationToken) =>
        GuardAsync(async () => AuthoringOperationResult<MagicSpellCatalogResponse>.Success(
            new(await repository.ListAsync(search, cancellationToken))));

    public Task<AuthoringOperationResult<MagicSpellDefinition>> LoadAsync(string definitionId, CancellationToken cancellationToken) =>
        GuardAsync(async () => await repository.LoadAsync(definitionId, cancellationToken) is { } definition
            ? AuthoringOperationResult<MagicSpellDefinition>.Success(definition)
            : AuthoringOperationResult<MagicSpellDefinition>.Failure(Error("spell_not_found", "Definition not found.")));

    public Task<AuthoringOperationResult<MagicSpellPreview>> PreviewAsync(string definitionId,
        MagicSpellRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var operation = request.TargetOperation ?? "save_draft";
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        var changes = new List<MagicSpellChange>();
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
        var nextState = operation switch { "publish" or "save_and_publish" => "Published", "disable" => "Disabled", "delete" => null, _ => "Draft" };
        if (existing?.PublicationState != nextState) changes.Add(new("publication_state", existing?.PublicationState, nextState));
        return AuthoringOperationResult<MagicSpellPreview>.Success(new(operation,
            !messages.Any(message => message.Severity == ValidationSeverity.Error),
            Signature(definitionId, operation, request), messages, changes));
    });

    // Revalidate the preview against current durable facts, apply with the expected
    // version, then reload and verify the committed authoring result.
    public Task<AuthoringOperationResult<MagicSpellMutation>> MutateAsync(string definitionId, string operation,
        MagicSpellRequest request, CancellationToken cancellationToken) => GuardAsync(async () =>
    {
        var existing = await repository.LoadAsync(definitionId, cancellationToken);
        var messages = ValidateOperation(definitionId, operation, request, existing);
        if (request.PreviewSignature != Signature(definitionId, operation, request))
            messages.Add(Error("preview_signature_mismatch", "Preview this operation again before applying it."));
        if (messages.Any(message => message.Severity == ValidationSeverity.Error))
            return AuthoringOperationResult<MagicSpellMutation>.Failure(messages);
        var saved = await repository.MutateAsync(definitionId, operation, request.Draft,
            request.ExpectedUpdatedAtUtc, cancellationToken);
        var verified = await repository.LoadAsync(definitionId, cancellationToken);
        if (JsonSerializer.Serialize(saved) != JsonSerializer.Serialize(verified))
            throw new InvalidOperationException("MagicSpell mutation failed reload-and-verify.");
        return AuthoringOperationResult<MagicSpellMutation>.Success(new(operation, verified, messages));
    });

    private static List<ApiError> ValidateOperation(string definitionId, string operation, MagicSpellRequest request,
        MagicSpellDefinition? existing)
    {
        var messages = new List<ApiError>();
        if (operation is not ("save_draft" or "save_and_publish" or "publish" or "disable" or "delete"))
            messages.Add(Error("invalid_operation", "Choose Save Draft, Save & Publish, Publish, Disable or Delete."));
        if (!StableId(definitionId)) messages.Add(Error("invalid_definition_id", "Use a lowercase snake-case definition ID.", "definition_id"));
        if (existing?.UpdatedAtUtc != request.ExpectedUpdatedAtUtc)
            messages.Add(Error("spell_version_conflict", "Definition changed; reload before editing."));
        if (existing is null && operation is not ("save_draft" or "save_and_publish"))
            messages.Add(Error("spell_not_found", "Save a draft first."));
        if (operation is not ("save_draft" or "save_and_publish") && existing is not null && JsonSerializer.Serialize(existing.Draft) != JsonSerializer.Serialize(request.Draft))
            messages.Add(Error("unsaved_spell_changes", "Save edited fields as a draft before this operation."));
        if (operation == "delete" && existing?.PublicationState != "Disabled")
            messages.Add(Error("spell_still_published", "Disable the definition before deleting it."));
        var draft = request.Draft;
        if (draft is null)
        {
            messages.Add(Error("missing_draft", "The complete definition draft is required."));
            return messages;
        }
        if (string.IsNullOrWhiteSpace(draft.DisplayName)) messages.Add(Error("missing_name", "Display name is required.", "display_name"));
        if (draft.Tier is < 1 or > 4) messages.Add(Error("invalid_tier", "Tier must be 1 through 4.", "tier"));
        if (draft.Element is not ("air" or "earth" or "fire" or "water"))
            messages.Add(Error("invalid_element", "Choose Air, Earth, Fire or Water.", "element"));
        if (draft.RequiredMagicLevel is < 1 or > 99)
            messages.Add(Error("invalid_magic_level", "Required Magic level must be 1 through 99.", "required_magic_level"));
        if (draft.ShardCost <= 0) messages.Add(Error("invalid_shard_cost", "Shard cost must be positive.", "shard_cost"));
        if (draft.SuccessfulHitMinDamage < 0 || draft.BaseMaxHit < draft.SuccessfulHitMinDamage)
            messages.Add(Error("invalid_damage_band", "Damage must satisfy 0 <= minimum <= maximum.", "base_max_hit"));
        if (draft.BaseCastXpTenths < 0)
            messages.Add(Error("invalid_cast_xp", "Base cast XP tenths must be nonnegative.", "base_cast_xp_tenths"));
        return messages;
    }

    private static bool StableId(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9]*(_[a-z0-9]+)*$");
    private static ApiError Error(string code, string message, string? field = null) => new(code, message, ValidationSeverity.Error, field);
    private static string Signature(string definitionId, string operation, MagicSpellRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { definitionId, operation, request.Draft, expected = request.ExpectedUpdatedAtUtc?.ToUniversalTime() })))).ToLowerInvariant();

    private async Task<AuthoringOperationResult<T>> GuardAsync<T>(Func<Task<AuthoringOperationResult<T>>> action)
    {
        try { return await action(); }
        catch (MagicSpellConcurrencyException)
        { return AuthoringOperationResult<T>.Failure(Error("spell_version_conflict", "Definition changed; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState == "23505")
        { return AuthoringOperationResult<T>.Failure(Error("spell_version_conflict", "Definition or child identity already exists; reload and preview again.")); }
        catch (PostgresException error) when (error.SqlState is "P0001" or "23503" or "23514")
        { return AuthoringOperationResult<T>.Failure(Error("spell_dependency_or_validation", error.MessageText)); }
        catch (Exception error) when (error is NpgsqlException or AuthoringDatabaseUnavailableException)
        {
            logger.LogError(error, "MagicSpell database operation failed.");
            return AuthoringOperationResult<T>.Failure(Error("database_unavailable", "MagicSpell database operation failed. Check schema health and host logs."));
        }
    }
}
