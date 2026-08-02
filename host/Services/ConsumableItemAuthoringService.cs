using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class ConsumableItemAuthoringService
{
    private readonly ConsumableItemRepository _repository;
    private readonly BasicItemRepository _basicItemRepository;
    private readonly ConsumableItemValidator _validator;
    private readonly ItemAssetService _assetService;
    private readonly ILogger<ConsumableItemAuthoringService> _logger;

    public ConsumableItemAuthoringService(
        ConsumableItemRepository repository,
        BasicItemRepository basicItemRepository,
        ConsumableItemValidator validator,
        ItemAssetService assetService,
        ILogger<ConsumableItemAuthoringService> logger)
    {
        _repository = repository;
        _basicItemRepository = basicItemRepository;
        _validator = validator;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<ConsumableCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<ConsumableCatalogResponse>.Success(
                new ConsumableCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ConsumableCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<ConsumableAuthoringOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var skills = await _repository.LoadSkillOptionsAsync(cancellationToken);
            return AuthoringOperationResult<ConsumableAuthoringOptionsResponse>.Success(
                new ConsumableAuthoringOptionsResponse(
                    [
                        new AuthoringOption("eat", "Eat"),
                        new AuthoringOption("drink", "Drink"),
                        new AuthoringOption("use", "Use")
                    ],
                    [new AuthoringOption("restore_resource", "Restore Resource")],
                    [
                        new AuthoringOption("health", "Health"),
                        new AuthoringOption("concentration", "Concentration"),
                        new AuthoringOption("special", "Special")
                    ],
                    [new AuthoringOption("skill_minimum", "Minimum Skill")],
                    skills,
                    false,
                    "True per-instance charges are not supported by the current inventory schema. Use consume_quantity and result_item_id for portions or empty containers."));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ConsumableAuthoringOptionsResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<ConsumableItemDefinition>> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _repository.LoadAsync(itemId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<ConsumableItemDefinition>.Failure(ItemNotFound(itemId))
                : AuthoringOperationResult<ConsumableItemDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ConsumableItemDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<ConsumableValidationResponse>> PreviewAsync(
        string itemId,
        ConsumablePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<ConsumableValidationResponse>(itemId);
            }

            var targetOperation = NormalizePreviewOperation(request.TargetOperation ?? string.Empty);
            if (targetOperation is null)
            {
                return AuthoringOperationResult<ConsumableValidationResponse>.Failure(new ApiError(
                    "invalid_target_operation",
                    "Target operation must be save_draft, publish, or disable.",
                    ValidationSeverity.Error,
                    "target_operation"));
            }

            if ((targetOperation is "publish" or "disable") && existing is null)
            {
                return AuthoringOperationResult<ConsumableValidationResponse>.Failure(ItemNotFound(itemId));
            }

            if ((targetOperation is "publish" or "disable") && existing?.HasConsumableProfile != true)
            {
                return AuthoringOperationResult<ConsumableValidationResponse>.Failure(new ApiError(
                    "consumable_profile_missing",
                    $"Item '{itemId}' must be saved as a consumable draft before changing publication state.",
                    ValidationSeverity.Error,
                    "item_id"));
            }

            var requested = Normalize(request);
            var effective = targetOperation == "save_draft"
                ? requested
                : existing is null ? requested : FromRecord(existing);
            var hasUnsavedChanges = existing is not null
                && (targetOperation is "publish" or "disable")
                && !EquivalentDraft(existing, requested);
            var validation = await _validator.ValidateAsync(
                itemId,
                effective,
                existing,
                targetOperation == "publish",
                cancellationToken);
            var messages = validation.Messages.ToList();
            if (hasUnsavedChanges)
            {
                messages.Add(new ApiError(
                    "unsaved_consumable_changes",
                    "Save the edited consumable definition as a draft before changing publication state.",
                    ValidationSeverity.Error,
                    "publication_state"));
            }

            var operationWouldDisable = existing?.RuntimeEnabled == true
                && (targetOperation is "save_draft" or "disable");
            if (operationWouldDisable)
            {
                if (await _basicItemRepository.HasLiveReferencesAsync(itemId, cancellationToken))
                {
                    messages.Add(LiveReferenceError(itemId));
                }
                if (await _basicItemRepository.HasPublishedConsumableResultReferencesAsync(itemId, cancellationToken))
                {
                    messages.Add(PublishedConsumableReferenceError(itemId));
                }
            }

            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            return AuthoringOperationResult<ConsumableValidationResponse>.Success(
                new ConsumableValidationResponse(
                    targetOperation,
                    validation.ValidForDraft && !hasErrors,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(existing, requested, targetOperation),
                    validation.AssetPreviewFilePath));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ConsumableValidationResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<ConsumableMutationResponse>> SaveDraftAsync(
        string itemId,
        SaveConsumableDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            var draft = Normalize(request);
            var validation = await _validator.ValidateAsync(
                itemId,
                draft,
                existing,
                false,
                cancellationToken);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<ConsumableMutationResponse>.Failure(validation.Messages);
            }

            if (existing?.RuntimeEnabled == true)
            {
                if (await _basicItemRepository.HasLiveReferencesAsync(itemId, cancellationToken))
                {
                    return AuthoringOperationResult<ConsumableMutationResponse>.Failure(LiveReferenceError(itemId));
                }
                if (await _basicItemRepository.HasPublishedConsumableResultReferencesAsync(itemId, cancellationToken))
                {
                    return AuthoringOperationResult<ConsumableMutationResponse>.Failure(PublishedConsumableReferenceError(itemId));
                }
            }

            var saved = await _repository.SaveDraftAsync(
                itemId,
                draft,
                request.ExpectedUpdatedAtUtc,
                existing is null,
                cancellationToken);
            var verified = await _repository.LoadAsync(itemId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified))
            {
                throw new InvalidOperationException("The saved consumable failed reload-and-verify.");
            }

            return AuthoringOperationResult<ConsumableMutationResponse>.Success(
                new ConsumableMutationResponse(
                    "save_draft",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (PostgresException exception) when (IsPublishedConsumableReferenceGuard(exception))
        {
            return AuthoringOperationResult<ConsumableMutationResponse>.Failure(PublishedConsumableReferenceError(itemId));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<ConsumableMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (ConsumableConcurrencyException)
        {
            return VersionConflict<ConsumableMutationResponse>(itemId);
        }
        catch (ConsumableKindConflictException exception)
        {
            return KindConflict<ConsumableMutationResponse>(exception.Message);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ConsumableMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<ConsumableMutationResponse>> PublishAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, true, expectedUpdatedAtUtc, cancellationToken);

    public Task<AuthoringOperationResult<ConsumableMutationResponse>> DisableAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, false, expectedUpdatedAtUtc, cancellationToken);

    private async Task<AuthoringOperationResult<ConsumableMutationResponse>> SetPublicationAsync(
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
                return AuthoringOperationResult<ConsumableMutationResponse>.Failure(ItemNotFound(itemId));
            }
            if (!existing.HasConsumableProfile)
            {
                return AuthoringOperationResult<ConsumableMutationResponse>.Failure(new ApiError(
                    "consumable_profile_missing",
                    $"Item '{itemId}' has no consumable profile. Save a draft first.",
                    ValidationSeverity.Error,
                    "item_id"));
            }

            var validation = await _validator.ValidateAsync(
                itemId,
                FromRecord(existing),
                existing,
                published,
                cancellationToken);
            var validForOperation = published
                ? validation.ValidForPublication
                : validation.ValidForDraft;
            if (!validForOperation)
            {
                return AuthoringOperationResult<ConsumableMutationResponse>.Failure(validation.Messages);
            }

            if (!published && existing.RuntimeEnabled)
            {
                if (await _basicItemRepository.HasLiveReferencesAsync(itemId, cancellationToken))
                {
                    return AuthoringOperationResult<ConsumableMutationResponse>.Failure(LiveReferenceError(itemId));
                }
                if (await _basicItemRepository.HasPublishedConsumableResultReferencesAsync(itemId, cancellationToken))
                {
                    return AuthoringOperationResult<ConsumableMutationResponse>.Failure(PublishedConsumableReferenceError(itemId));
                }
            }

            var saved = await _repository.SetPublicationAsync(
                itemId,
                published,
                expectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(itemId, cancellationToken);
            if (verified is null
                || !Equivalent(saved, verified)
                || verified.RuntimeEnabled != published)
            {
                throw new InvalidOperationException("The consumable publication change failed reload-and-verify.");
            }

            return AuthoringOperationResult<ConsumableMutationResponse>.Success(
                new ConsumableMutationResponse(
                    published ? "publish" : "disable",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (PostgresException exception) when (IsPublishedConsumableReferenceGuard(exception))
        {
            return AuthoringOperationResult<ConsumableMutationResponse>.Failure(PublishedConsumableReferenceError(itemId));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<ConsumableMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (ConsumableConcurrencyException)
        {
            return VersionConflict<ConsumableMutationResponse>(itemId);
        }
        catch (ConsumableKindConflictException exception)
        {
            return KindConflict<ConsumableMutationResponse>(exception.Message);
        }
        catch (ConsumableProfileMissingException exception)
        {
            return AuthoringOperationResult<ConsumableMutationResponse>.Failure(new ApiError(
                "consumable_profile_missing",
                exception.Message,
                ValidationSeverity.Error,
                "item_id"));
        }
        catch (ConsumableNotFoundException)
        {
            return AuthoringOperationResult<ConsumableMutationResponse>.Failure(ItemNotFound(itemId));
        }
        catch (ConsumablePublicationIntegrityException exception)
        {
            return AuthoringOperationResult<ConsumableMutationResponse>.Failure(new ApiError(
                exception.Code,
                exception.Message,
                ValidationSeverity.Error,
                exception.Field));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ConsumableMutationResponse>(exception);
        }
    }

    private ConsumableItemDefinition ToDefinition(ConsumableItemRecord record)
    {
        var asset = _assetService.Resolve(record.IconTexturePath);
        return new ConsumableItemDefinition(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            AuthoringKind(record),
            record.HasConsumableProfile,
            IsConsumableEditable(record),
            record.UseAction,
            record.ConsumeQuantity,
            record.ResultItemId,
            record.SuccessMessage,
            record.UsableInCombat,
            record.CooldownMs,
            record.UseAnimationId,
            record.UseSoundResourcePath,
            record.Requirements,
            record.Effects,
            record.UpdatedAtUtc,
            asset.FilePath);
    }

    private static ConsumableItemSummary ToSummary(ConsumableItemRecord record) =>
        new(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            AuthoringKind(record),
            record.HasConsumableProfile,
            IsConsumableEditable(record),
            record.UpdatedAtUtc);

    private static string AuthoringKind(ConsumableItemRecord record) =>
        !IsConsumableEditable(record)
            ? "Equipment"
            : record.HasConsumableProfile ? "Consumable" : "Basic";

    private static bool IsConsumableEditable(ConsumableItemRecord record) =>
        record.EquipmentSlotId is null && record.RequiredStrength == 1;

    private static NormalizedConsumableDraft Normalize(SaveConsumableDraftRequest request) =>
        Normalize(
            request.DisplayName,
            request.IconTexturePath,
            request.UseAction,
            request.ConsumeQuantity,
            request.ResultItemId,
            request.SuccessMessage,
            request.UsableInCombat,
            request.CooldownMs,
            request.UseAnimationId,
            request.UseSoundResourcePath,
            request.Requirements,
            request.Effects);

    private static NormalizedConsumableDraft Normalize(ConsumablePreviewRequest request) =>
        Normalize(
            request.DisplayName,
            request.IconTexturePath,
            request.UseAction,
            request.ConsumeQuantity,
            request.ResultItemId,
            request.SuccessMessage,
            request.UsableInCombat,
            request.CooldownMs,
            request.UseAnimationId,
            request.UseSoundResourcePath,
            request.Requirements,
            request.Effects);

    private static NormalizedConsumableDraft Normalize(
        string? displayName,
        string? iconTexturePath,
        string? useAction,
        int consumeQuantity,
        string? resultItemId,
        string? successMessage,
        bool usableInCombat,
        int cooldownMs,
        string? useAnimationId,
        string? useSoundResourcePath,
        IReadOnlyList<ConsumableRequirementDefinition>? requirements,
        IReadOnlyList<ConsumableEffectDefinition>? effects)
    {
        var normalizedRequirements = (requirements ?? Array.Empty<ConsumableRequirementDefinition>())
            .Select((requirement, index) => new ConsumableRequirementDefinition(
                index,
                (requirement.RequirementType ?? string.Empty).Trim().ToLowerInvariant(),
                (requirement.TargetId ?? string.Empty).Trim().ToLowerInvariant(),
                requirement.MinimumValue))
            .ToArray();
        var normalizedEffects = (effects ?? Array.Empty<ConsumableEffectDefinition>())
            .Select((effect, index) => new ConsumableEffectDefinition(
                index,
                (effect.EffectType ?? string.Empty).Trim().ToLowerInvariant(),
                (effect.TargetId ?? string.Empty).Trim().ToLowerInvariant(),
                effect.MinimumAmount,
                effect.MaximumAmount))
            .ToArray();
        return new NormalizedConsumableDraft(
            (displayName ?? string.Empty).Trim(),
            (iconTexturePath ?? string.Empty).Trim(),
            (useAction ?? string.Empty).Trim().ToLowerInvariant(),
            consumeQuantity,
            EmptyToNull(resultItemId)?.ToLowerInvariant(),
            EmptyToNull(successMessage),
            usableInCombat,
            cooldownMs,
            EmptyToNull(useAnimationId)?.ToLowerInvariant(),
            EmptyToNull(useSoundResourcePath),
            normalizedRequirements,
            normalizedEffects);
    }

    private static NormalizedConsumableDraft FromRecord(ConsumableItemRecord record) =>
        new(
            record.DisplayName,
            record.IconTexturePath,
            record.UseAction,
            record.ConsumeQuantity,
            record.ResultItemId,
            record.SuccessMessage,
            record.UsableInCombat,
            record.CooldownMs,
            record.UseAnimationId,
            record.UseSoundResourcePath,
            record.Requirements,
            record.Effects);

    private static string? EmptyToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static IReadOnlyList<BasicItemChange> CalculateChanges(
        ConsumableItemRecord? existing,
        NormalizedConsumableDraft requested,
        string operation)
    {
        var changes = new List<BasicItemChange>();
        if (existing is null)
        {
            changes.Add(new BasicItemChange("item_id", null, "new item"));
            changes.Add(new BasicItemChange("consumable_profile", null, "create"));
        }
        else if (!existing.HasConsumableProfile && operation == "save_draft")
        {
            changes.Add(new BasicItemChange("consumable_profile", "absent", "create"));
        }

        if (operation == "save_draft")
        {
            AddChange(changes, "display_name", existing?.DisplayName, requested.DisplayName);
            AddChange(changes, "icon_texture_path", existing?.IconTexturePath, requested.IconTexturePath);
            AddChange(changes, "use_action", existing?.UseAction, requested.UseAction);
            AddChange(changes, "consume_quantity", existing?.ConsumeQuantity.ToString(), requested.ConsumeQuantity.ToString());
            AddChange(changes, "result_item_id", existing?.ResultItemId, requested.ResultItemId);
            AddChange(changes, "success_message", existing?.SuccessMessage, requested.SuccessMessage);
            AddChange(changes, "usable_in_combat", existing?.UsableInCombat.ToString(), requested.UsableInCombat.ToString());
            AddChange(changes, "cooldown_ms", existing?.CooldownMs.ToString(), requested.CooldownMs.ToString());
            AddChange(changes, "use_animation_id", existing?.UseAnimationId, requested.UseAnimationId);
            AddChange(changes, "use_sound_resource_path", existing?.UseSoundResourcePath, requested.UseSoundResourcePath);
            AddChange(changes, "requirements", Serialize(existing?.Requirements), Serialize(requested.Requirements));
            AddChange(changes, "effects", Serialize(existing?.Effects), Serialize(requested.Effects));
        }

        var beforeState = existing is null ? null : existing.RuntimeEnabled ? "Published" : "Draft";
        var afterState = operation == "publish" ? "Published" : "Draft";
        AddChange(changes, "publication_state", beforeState, afterState);
        return changes;
    }

    private static string Serialize<T>(IReadOnlyList<T>? values) =>
        JsonSerializer.Serialize(values ?? Array.Empty<T>());

    private static void AddChange(
        ICollection<BasicItemChange> changes,
        string field,
        string? before,
        string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new BasicItemChange(field, before, after));
        }
    }

    private static bool EquivalentDraft(ConsumableItemRecord existing, NormalizedConsumableDraft requested) =>
        string.Equals(existing.DisplayName, requested.DisplayName, StringComparison.Ordinal)
        && string.Equals(existing.IconTexturePath, requested.IconTexturePath, StringComparison.Ordinal)
        && string.Equals(existing.UseAction, requested.UseAction, StringComparison.Ordinal)
        && existing.ConsumeQuantity == requested.ConsumeQuantity
        && string.Equals(existing.ResultItemId, requested.ResultItemId, StringComparison.Ordinal)
        && string.Equals(existing.SuccessMessage, requested.SuccessMessage, StringComparison.Ordinal)
        && existing.UsableInCombat == requested.UsableInCombat
        && existing.CooldownMs == requested.CooldownMs
        && string.Equals(existing.UseAnimationId, requested.UseAnimationId, StringComparison.Ordinal)
        && string.Equals(existing.UseSoundResourcePath, requested.UseSoundResourcePath, StringComparison.Ordinal)
        && SequenceEqual(existing.Requirements, requested.Requirements)
        && SequenceEqual(existing.Effects, requested.Effects);

    private static bool Equivalent(ConsumableItemRecord left, ConsumableItemRecord right) =>
        left.ItemId == right.ItemId
        && left.DisplayName == right.DisplayName
        && left.IconTexturePath == right.IconTexturePath
        && left.EquipmentSlotId == right.EquipmentSlotId
        && left.RuntimeEnabled == right.RuntimeEnabled
        && left.RequiredStrength == right.RequiredStrength
        && left.HasConsumableProfile == right.HasConsumableProfile
        && left.UseAction == right.UseAction
        && left.ConsumeQuantity == right.ConsumeQuantity
        && left.ResultItemId == right.ResultItemId
        && left.SuccessMessage == right.SuccessMessage
        && left.UsableInCombat == right.UsableInCombat
        && left.CooldownMs == right.CooldownMs
        && left.UseAnimationId == right.UseAnimationId
        && left.UseSoundResourcePath == right.UseSoundResourcePath
        && SequenceEqual(left.Requirements, right.Requirements)
        && SequenceEqual(left.Effects, right.Effects)
        && left.UpdatedAtUtc.ToUniversalTime() == right.UpdatedAtUtc.ToUniversalTime();

    private static bool SequenceEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) =>
        left.Count == right.Count && left.SequenceEqual(right);

    private static bool HasVersionConflict(ConsumableItemRecord? existing, DateTimeOffset? expected) =>
        existing is not null
        && (expected is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expected.Value.ToUniversalTime());

    private static string? NormalizePreviewOperation(string operation) =>
        operation.Trim().ToLowerInvariant() switch
        {
            "save_draft" => "save_draft",
            "publish" => "publish",
            "disable" => "disable",
            _ => null
        };

    private static ApiError ItemNotFound(string itemId) =>
        new(
            "item_not_found",
            $"Item '{itemId}' does not exist.",
            ValidationSeverity.Error,
            "item_id");

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

    private static bool IsPublishedConsumableReferenceGuard(PostgresException exception) =>
        exception.MessageText.Contains(
            "published consumable references it",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsLiveReferenceGuard(PostgresException exception) =>
        exception.MessageText.Contains("Cannot disable runtime item", StringComparison.OrdinalIgnoreCase);

    private static AuthoringOperationResult<T> VersionConflict<T>(string itemId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "item_version_conflict",
            $"Item '{itemId}' changed after it was loaded. Reload it before continuing.",
            ValidationSeverity.Error,
            "expected_updated_at_utc"));

    private static AuthoringOperationResult<T> KindConflict<T>(string message) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "wrong_authoring_workspace",
            message,
            ValidationSeverity.Error,
            "item_id"));

    private AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception)
    {
        _logger.LogWarning(exception, "Consumable authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            "database_unavailable",
            "The configured development database is unavailable or missing the T2 consumable schema.",
            ValidationSeverity.Error,
            Remediation: "Apply integrations/mmo-project/prototype/sql/017_item_consumable_profiles.sql, then review the Environment tab."));
    }

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException
            or InvalidOperationException;
}
