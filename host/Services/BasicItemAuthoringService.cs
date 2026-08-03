using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class BasicItemAuthoringService
{
    private readonly BasicItemRepository _repository;
    private readonly BasicItemValidator _validator;
    private readonly ItemAssetService _assetService;
    private readonly ILogger<BasicItemAuthoringService> _logger;

    public BasicItemAuthoringService(
        BasicItemRepository repository,
        BasicItemValidator validator,
        ItemAssetService assetService,
        ILogger<BasicItemAuthoringService> logger)
    {
        _repository = repository;
        _validator = validator;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<BasicItemCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<BasicItemCatalogResponse>.Success(
                new BasicItemCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<BasicItemCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<BasicItemDefinition>> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _repository.LoadAsync(itemId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<BasicItemDefinition>.Failure(new ApiError(
                    "item_not_found",
                    $"Item '{itemId}' does not exist.",
                    ValidationSeverity.Error,
                    "item_id"))
                : AuthoringOperationResult<BasicItemDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<BasicItemDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<BasicItemValidationResponse>> PreviewAsync(
        string itemId,
        BasicItemPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<BasicItemValidationResponse>(itemId);
            }

            var targetOperation = NormalizePreviewOperation(request.TargetOperation ?? string.Empty);
            if (targetOperation is null)
            {
                return AuthoringOperationResult<BasicItemValidationResponse>.Failure(new ApiError(
                    "invalid_target_operation",
                    "Target operation must be save_draft, publish, disable, or delete.",
                    ValidationSeverity.Error,
                    "target_operation"));
            }

            if ((targetOperation is "publish" or "disable" or "delete") && existing is null)
            {
                var verb = targetOperation switch
                {
                    "publish" => "published",
                    "disable" => "disabled",
                    _ => "deleted"
                };
                return AuthoringOperationResult<BasicItemValidationResponse>.Failure(new ApiError(
                    "item_not_found",
                    $"Item '{itemId}' must be saved as a draft before it can be {verb}.",
                    ValidationSeverity.Error,
                    "item_id"));
            }

            var requestedName = (request.DisplayName ?? string.Empty).Trim();
            var requestedIcon = (request.IconTexturePath ?? string.Empty).Trim();
            var hasUnsavedChanges = existing is not null
                && (targetOperation is "publish" or "disable" or "delete")
                && (!string.Equals(existing.DisplayName, requestedName, StringComparison.Ordinal)
                    || !string.Equals(existing.IconTexturePath, requestedIcon, StringComparison.Ordinal));

            var validation = _validator.Validate(
                itemId,
                targetOperation == "save_draft" ? requestedName : existing?.DisplayName ?? requestedName,
                targetOperation == "save_draft" ? requestedIcon : existing?.IconTexturePath ?? requestedIcon,
                existing,
                targetOperation == "publish");
            var messages = validation.Messages.ToList();
            var operationWouldDisable = existing?.RuntimeEnabled == true
                && (targetOperation is "save_draft" or "disable");
            if (operationWouldDisable)
            {
                if (await _repository.HasLiveReferencesAsync(itemId, cancellationToken))
                {
                    messages.Add(LiveReferenceError(itemId));
                }
                if (await _repository.HasPublishedConsumableResultReferencesAsync(itemId, cancellationToken))
                {
                    messages.Add(PublishedConsumableReferenceError(itemId));
                }
            }
            if (targetOperation == "delete" && existing?.RuntimeEnabled == true)
            {
                messages.Add(DeleteRequiresDisabledError(itemId));
            }
            if (hasUnsavedChanges)
            {
                messages.Add(new ApiError(
                    "unsaved_item_changes",
                    "Save the edited name or icon as a draft before changing publication state.",
                    ValidationSeverity.Error,
                    "publication_state"));
            }

            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            var changes = CalculateChanges(
                existing,
                requestedName,
                requestedIcon,
                targetOperation);
            return AuthoringOperationResult<BasicItemValidationResponse>.Success(
                new BasicItemValidationResponse(
                    targetOperation,
                    validation.ValidForDraft && !hasErrors,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    changes,
                    validation.AssetPreviewFilePath));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<BasicItemValidationResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<BasicItemMutationResponse>> SaveDraftAsync(
        string itemId,
        SaveBasicItemDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            var validation = _validator.Validate(
                itemId,
                request.DisplayName ?? string.Empty,
                request.IconTexturePath ?? string.Empty,
                existing,
                false);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<BasicItemMutationResponse>.Failure(validation.Messages);
            }
            if (existing?.RuntimeEnabled == true)
            {
                if (await _repository.HasLiveReferencesAsync(itemId, cancellationToken))
                {
                    return AuthoringOperationResult<BasicItemMutationResponse>.Failure(LiveReferenceError(itemId));
                }
                if (await _repository.HasPublishedConsumableResultReferencesAsync(itemId, cancellationToken))
                {
                    return AuthoringOperationResult<BasicItemMutationResponse>.Failure(PublishedConsumableReferenceError(itemId));
                }
            }

            var saved = await _repository.SaveDraftAsync(
                itemId,
                (request.DisplayName ?? string.Empty).Trim(),
                (request.IconTexturePath ?? string.Empty).Trim(),
                request.ExpectedUpdatedAtUtc,
                existing is null,
                cancellationToken);
            var verified = await _repository.LoadAsync(itemId, cancellationToken);
            if (verified is null || verified != saved)
            {
                throw new InvalidOperationException("The saved item failed reload-and-verify.");
            }

            return AuthoringOperationResult<BasicItemMutationResponse>.Success(
                new BasicItemMutationResponse("save_draft", ToDefinition(verified), validation.Messages));
        }
        catch (PostgresException exception) when (IsPublishedConsumableReferenceGuard(exception))
        {
            return AuthoringOperationResult<BasicItemMutationResponse>.Failure(PublishedConsumableReferenceError(itemId));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<BasicItemMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (BasicItemConcurrencyException)
        {
            return VersionConflict<BasicItemMutationResponse>(itemId);
        }
        catch (BasicItemKindConflictException exception)
        {
            return KindConflict<BasicItemMutationResponse>(exception);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<BasicItemMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<BasicItemMutationResponse>> PublishAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, true, expectedUpdatedAtUtc, cancellationToken);

    public Task<AuthoringOperationResult<BasicItemMutationResponse>> DisableAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, false, expectedUpdatedAtUtc, cancellationToken);

    public async Task<AuthoringOperationResult<DeleteMutationResponse>> DeleteAsync(
        string itemId,
        DeleteMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(new ApiError(
                    "item_not_found",
                    $"Item '{itemId}' does not exist.",
                    ValidationSeverity.Error,
                    "item_id"));
            }
            if (existing.RuntimeEnabled)
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteRequiresDisabledError(itemId));
            }

            await _repository.DeleteAsync(itemId, request.ExpectedUpdatedAtUtc, cancellationToken);
            return AuthoringOperationResult<DeleteMutationResponse>.Success(
                new DeleteMutationResponse("delete", itemId, []));
        }
        catch (BasicItemPublishedDeleteException)
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteRequiresDisabledError(itemId));
        }
        catch (PostgresException exception) when (IsForeignKeyViolation(exception))
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteReferenceError(itemId));
        }
        catch (BasicItemNotFoundException)
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(new ApiError(
                "item_not_found",
                $"Item '{itemId}' does not exist.",
                ValidationSeverity.Error,
                "item_id"));
        }
        catch (BasicItemConcurrencyException)
        {
            return VersionConflict<DeleteMutationResponse>(itemId);
        }
        catch (BasicItemKindConflictException exception)
        {
            return KindConflict<DeleteMutationResponse>(exception);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DeleteMutationResponse>(exception);
        }
    }

    private async Task<AuthoringOperationResult<BasicItemMutationResponse>> SetPublicationAsync(
        string itemId,
        bool published,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<BasicItemMutationResponse>.Failure(new ApiError(
                    "item_not_found",
                    $"Item '{itemId}' does not exist.",
                    ValidationSeverity.Error,
                    "item_id"));
            }

            var validation = _validator.Validate(
                itemId,
                existing.DisplayName,
                existing.IconTexturePath,
                existing,
                published);
            var validForOperation = published
                ? validation.ValidForPublication
                : validation.ValidForDraft;
            if (!validForOperation)
            {
                return AuthoringOperationResult<BasicItemMutationResponse>.Failure(validation.Messages);
            }

            if (!published && existing.RuntimeEnabled)
            {
                if (await _repository.HasLiveReferencesAsync(itemId, cancellationToken))
                {
                    return AuthoringOperationResult<BasicItemMutationResponse>.Failure(LiveReferenceError(itemId));
                }
                if (await _repository.HasPublishedConsumableResultReferencesAsync(itemId, cancellationToken))
                {
                    return AuthoringOperationResult<BasicItemMutationResponse>.Failure(PublishedConsumableReferenceError(itemId));
                }
            }

            var saved = await _repository.SetPublicationAsync(
                itemId,
                published,
                expectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(itemId, cancellationToken);
            if (verified is null || verified != saved || verified.RuntimeEnabled != published)
            {
                throw new InvalidOperationException("The publication change failed reload-and-verify.");
            }

            return AuthoringOperationResult<BasicItemMutationResponse>.Success(
                new BasicItemMutationResponse(
                    published ? "publish" : "disable",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (PostgresException exception) when (IsPublishedConsumableReferenceGuard(exception))
        {
            return AuthoringOperationResult<BasicItemMutationResponse>.Failure(PublishedConsumableReferenceError(itemId));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<BasicItemMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (BasicItemConcurrencyException)
        {
            return VersionConflict<BasicItemMutationResponse>(itemId);
        }
        catch (BasicItemKindConflictException exception)
        {
            return KindConflict<BasicItemMutationResponse>(exception);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<BasicItemMutationResponse>(exception);
        }
    }

    private BasicItemDefinition ToDefinition(BasicItemRecord record)
    {
        var asset = _assetService.Resolve(record.IconTexturePath);
        return new BasicItemDefinition(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            BasicAuthoringKind(record),
            IsBasicRecord(record),
            record.UpdatedAtUtc,
            asset.FilePath);
    }

    private static BasicItemSummary ToSummary(BasicItemRecord record) =>
        new(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            BasicAuthoringKind(record),
            record.UpdatedAtUtc);

    private static IReadOnlyList<BasicItemChange> CalculateChanges(
        BasicItemRecord? existing,
        string displayName,
        string iconTexturePath,
        string targetOperation)
    {
        var changes = new List<BasicItemChange>();
        if (existing is null)
        {
            changes.Add(new BasicItemChange("item_id", null, "new item"));
            changes.Add(new BasicItemChange("display_name", null, displayName));
            changes.Add(new BasicItemChange("icon_texture_path", null, iconTexturePath));
            changes.Add(new BasicItemChange("publication_state", null, "Draft"));
            return changes;
        }

        if (targetOperation == "save_draft")
        {
            AddChange(changes, "display_name", existing.DisplayName, displayName);
            AddChange(changes, "icon_texture_path", existing.IconTexturePath, iconTexturePath);
        }
        var beforeState = existing.RuntimeEnabled ? "Published" : "Draft";
        var afterState = targetOperation switch
        {
            "publish" => "Published",
            "disable" => "Draft",
            "delete" => "Deleted",
            _ => "Draft"
        };
        if (!string.Equals(beforeState, afterState, StringComparison.Ordinal))
        {
            changes.Add(new BasicItemChange("publication_state", beforeState, afterState));
        }

        return changes;
    }

    private static void AddChange(
        ICollection<BasicItemChange> changes,
        string field,
        string before,
        string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new BasicItemChange(field, before, after));
        }
    }


    private static ApiError LiveReferenceError(string itemId) =>
        new(
            "item_has_live_references",
            $"Item '{itemId}' cannot be disabled while inventory, equipment, or ground-item state references it.",
            ValidationSeverity.Error,
            "publication_state",
            "Remove all live references in the development database before disabling or editing the published definition.");

    private static ApiError PublishedConsumableReferenceError(string itemId) =>
        new(
            "item_has_published_consumable_references",
            $"Item '{itemId}' cannot be disabled because a published consumable produces it as a result item.",
            ValidationSeverity.Error,
            "publication_state",
            "Disable or update every published consumable that references this result item first.");

    private static ApiError DeleteRequiresDisabledError(string itemId) =>
        new(
            "delete_requires_disabled_item",
            $"Item '{itemId}' must be disabled before it can be deleted.",
            ValidationSeverity.Error,
            "publication_state",
            "Disable the item, preview Delete again, then apply the delete operation.");

    private static ApiError DeleteReferenceError(string itemId) =>
        new(
            "item_delete_blocked_by_references",
            $"Item '{itemId}' cannot be deleted while another table references it.",
            ValidationSeverity.Error,
            "item_id",
            "Remove inventory, equipment, ground-item, consumable-result, mob-drop, or other content references before deleting.");

    private static bool IsPublishedConsumableReferenceGuard(PostgresException exception) =>
        exception.MessageText.Contains(
            "published consumable references it",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsLiveReferenceGuard(PostgresException exception) =>
        exception.MessageText.Contains(
            "Cannot disable runtime item",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsForeignKeyViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    private static bool IsBasicRecord(BasicItemRecord record) =>
        record.EquipmentSlotId is null
        && record.RequiredStrength == 1
        && !record.HasConsumableProfile;

    private static string BasicAuthoringKind(BasicItemRecord record) =>
        record.HasConsumableProfile
            ? "Consumable"
            : IsBasicRecord(record) ? "Basic" : "Equipment";

    private static string? NormalizePreviewOperation(string operation) =>
        operation.Trim().ToLowerInvariant() switch
        {
            "save_draft" => "save_draft",
            "publish" => "publish",
            "disable" => "disable",
            "delete" => "delete",
            _ => null
        };

    private static bool HasVersionConflict(BasicItemRecord? existing, DateTimeOffset? expected) =>
        existing is not null
        && (expected is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expected.Value.ToUniversalTime());

    private static AuthoringOperationResult<T> VersionConflict<T>(string itemId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "item_version_conflict",
            $"Item '{itemId}' changed after it was loaded. Reload it before continuing.",
            ValidationSeverity.Error,
            "expected_updated_at_utc"));

    private static AuthoringOperationResult<T> KindConflict<T>(BasicItemKindConflictException exception) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "wrong_authoring_workspace",
            exception.Message,
            ValidationSeverity.Error,
            "item_id"));

    private AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception)
    {
        _logger.LogWarning(exception, "Basic item authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            "database_unavailable",
            "The configured development database is unavailable or incompatible.",
            ValidationSeverity.Error,
            Remediation: "Review the Environment tab and local host configuration."));
    }

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException
            or InvalidOperationException;
}
