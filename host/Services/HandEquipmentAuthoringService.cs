using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class HandEquipmentAuthoringService
{
    private const string VisualAssetModelMessage =
        "The current runtime derives hand-equipment player-layer asset keys from item name and slot. Direct visual asset overrides remain deferred.";

    private readonly HandEquipmentRepository _repository;
    private readonly BasicItemRepository _basicItemRepository;
    private readonly HandEquipmentItemValidator _validator;
    private readonly HandEquipmentAuthoringRegistry _registry;
    private readonly ItemAssetService _assetService;
    private readonly ILogger<HandEquipmentAuthoringService> _logger;

    public HandEquipmentAuthoringService(
        HandEquipmentRepository repository,
        BasicItemRepository basicItemRepository,
        HandEquipmentItemValidator validator,
        HandEquipmentAuthoringRegistry registry,
        ItemAssetService assetService,
        ILogger<HandEquipmentAuthoringService> logger)
    {
        _repository = repository;
        _basicItemRepository = basicItemRepository;
        _validator = validator;
        _registry = registry;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<HandEquipmentAuthoringOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var slots = await _repository.LoadSlotsAsync(cancellationToken);
            var skills = await _repository.LoadSkillsAsync(cancellationToken);
            var capabilities = await _repository.LoadGatheringCapabilitiesAsync(cancellationToken);
            return AuthoringOperationResult<HandEquipmentAuthoringOptionsResponse>.Success(
                new HandEquipmentAuthoringOptionsResponse(
                    slots
                        .Where(slot => EquipmentItemRepository.IsHandSlot(slot.SlotId))
                        .Select(slot => new AuthoringOption(slot.SlotId, slot.DisplayName))
                        .ToArray(),
                    slots
                        .Where(slot => EquipmentItemRepository.IsWearableSlot(slot.SlotId))
                        .Select(slot => new AuthoringOption(slot.SlotId, slot.DisplayName))
                        .ToArray(),
                    skills
                        .Select(skill => new AuthoringOption(skill.SkillId, skill.DisplayName))
                        .ToArray(),
                    CombatBonusOptions(),
                    _registry.LoadAttackFamilies(),
                    _registry.LoadAttackStyles(),
                    capabilities
                        .Select(skill => new AuthoringOption(skill.SkillId, skill.DisplayName))
                        .ToArray(),
                    _registry.LoadWeaponAnimationRefs(),
                    false,
                    VisualAssetModelMessage));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<HandEquipmentAuthoringOptionsResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<HandEquipmentCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<HandEquipmentCatalogResponse>.Success(
                new HandEquipmentCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<HandEquipmentCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<HandEquipmentItemDefinition>> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _repository.LoadAsync(itemId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<HandEquipmentItemDefinition>.Failure(ItemNotFound(itemId))
                : AuthoringOperationResult<HandEquipmentItemDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<HandEquipmentItemDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<HandEquipmentValidationResponse>> PreviewAsync(
        string itemId,
        HandEquipmentPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<HandEquipmentValidationResponse>.Failure(ItemNotFound(itemId));
            }
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<HandEquipmentValidationResponse>(itemId);
            }

            var operation = NormalizePreviewOperation(request.TargetOperation);
            if (operation is null)
            {
                return AuthoringOperationResult<HandEquipmentValidationResponse>.Failure(new ApiError(
                    "invalid_target_operation",
                    "Target operation must be save_draft, publish, disable, or delete.",
                    ValidationSeverity.Error,
                    "target_operation"));
            }

            var requested = Normalize(request);
            var hasUnsavedOperationChanges =
                operation is "publish" or "disable" or "delete"
                && !EquivalentDraft(existing, requested);
            var effective = operation == "save_draft" || hasUnsavedOperationChanges
                ? requested
                : FromRecord(existing);
            var validation = await _validator.ValidateAsync(
                itemId,
                effective,
                existing,
                operation == "publish" && !hasUnsavedOperationChanges,
                cancellationToken);
            var messages = validation.Messages.ToList();

            if (hasUnsavedOperationChanges)
            {
                messages.Add(new ApiError(
                    "unsaved_hand_equipment_changes",
                    "Save the edited hand-equipment definition as a draft before changing publication state or deleting it.",
                    ValidationSeverity.Error,
                    "publication_state"));
            }

            if (existing.RuntimeEnabled && (operation == "save_draft" || operation == "disable"))
            {
                await AddDisableReferenceErrorsAsync(itemId, messages, cancellationToken);
            }
            if (operation == "delete" && existing.RuntimeEnabled)
            {
                messages.Add(DeleteRequiresDisabledError(itemId));
            }

            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            var signature = ComputePreviewSignature(itemId, operation, effective, request.ExpectedUpdatedAtUtc);
            return AuthoringOperationResult<HandEquipmentValidationResponse>.Success(
                new HandEquipmentValidationResponse(
                    operation,
                    validation.ValidForDraft && !hasErrors,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(existing, requested, operation),
                    validation.AssetPreviewFilePath,
                    signature));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<HandEquipmentValidationResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<HandEquipmentMutationResponse>> SaveDraftAsync(
        string itemId,
        SaveHandEquipmentDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(ItemNotFound(itemId));
            }

            var draft = Normalize(request);
            if (!IsMatchingPreview(itemId, "save_draft", draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(PreviewMismatch("save_draft"));
            }

            var validation = await _validator.ValidateAsync(
                itemId,
                draft,
                existing,
                false,
                cancellationToken);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(validation.Messages);
            }

            if (existing.RuntimeEnabled)
            {
                var referenceErrors = new List<ApiError>();
                await AddDisableReferenceErrorsAsync(itemId, referenceErrors, cancellationToken);
                if (referenceErrors.Count > 0)
                {
                    return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(referenceErrors);
                }
            }

            var saved = await _repository.SaveDraftAsync(
                itemId,
                draft,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(itemId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified))
            {
                throw new InvalidOperationException("The saved hand-equipment aggregate failed reload-and-verify.");
            }

            return AuthoringOperationResult<HandEquipmentMutationResponse>.Success(
                new HandEquipmentMutationResponse(
                    "save_draft",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (EquipmentItemNotFoundException)
        {
            return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(ItemNotFound(itemId));
        }
        catch (EquipmentConcurrencyException)
        {
            return VersionConflict<HandEquipmentMutationResponse>(itemId);
        }
        catch (EquipmentKindConflictException exception)
        {
            return KindConflict<HandEquipmentMutationResponse>(exception.Message);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<HandEquipmentMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<HandEquipmentMutationResponse>> PublishAsync(
        string itemId,
        HandEquipmentPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, true, request, cancellationToken);

    public Task<AuthoringOperationResult<HandEquipmentMutationResponse>> DisableAsync(
        string itemId,
        HandEquipmentPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, false, request, cancellationToken);

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
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(ItemNotFound(itemId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(itemId, "delete", draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(PreviewMismatch("delete"));
            }

            if (existing.RuntimeEnabled)
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteRequiresDisabledError(itemId));
            }

            await _repository.DeleteAsync(itemId, request.ExpectedUpdatedAtUtc, cancellationToken);
            return AuthoringOperationResult<DeleteMutationResponse>.Success(
                new DeleteMutationResponse("delete", itemId, []));
        }
        catch (EquipmentPublishedDeleteException)
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteRequiresDisabledError(itemId));
        }
        catch (PostgresException exception) when (IsForeignKeyViolation(exception))
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteReferenceError(itemId));
        }
        catch (EquipmentItemNotFoundException)
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(ItemNotFound(itemId));
        }
        catch (EquipmentConcurrencyException)
        {
            return VersionConflict<DeleteMutationResponse>(itemId);
        }
        catch (EquipmentKindConflictException exception)
        {
            return KindConflict<DeleteMutationResponse>(exception.Message);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DeleteMutationResponse>(exception);
        }
    }

    private async Task<AuthoringOperationResult<HandEquipmentMutationResponse>> SetPublicationAsync(
        string itemId,
        bool published,
        HandEquipmentPublicationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(ItemNotFound(itemId));
            }

            var draft = FromRecord(existing);
            var operation = published ? "publish" : "disable";
            if (!IsMatchingPreview(itemId, operation, draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(PreviewMismatch(operation));
            }

            var validation = await _validator.ValidateAsync(
                itemId,
                draft,
                existing,
                published,
                cancellationToken);
            var valid = published ? validation.ValidForPublication : validation.ValidForDraft;
            if (!valid)
            {
                return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(validation.Messages);
            }

            if (!published && existing.RuntimeEnabled)
            {
                var referenceErrors = new List<ApiError>();
                await AddDisableReferenceErrorsAsync(itemId, referenceErrors, cancellationToken);
                if (referenceErrors.Count > 0)
                {
                    return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(referenceErrors);
                }
            }

            var saved = await _repository.SetPublicationAsync(
                itemId,
                published,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(itemId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified) || verified.RuntimeEnabled != published)
            {
                throw new InvalidOperationException("The hand-equipment publication change failed reload-and-verify.");
            }

            return AuthoringOperationResult<HandEquipmentMutationResponse>.Success(
                new HandEquipmentMutationResponse(
                    operation,
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (EquipmentItemNotFoundException)
        {
            return AuthoringOperationResult<HandEquipmentMutationResponse>.Failure(ItemNotFound(itemId));
        }
        catch (EquipmentConcurrencyException)
        {
            return VersionConflict<HandEquipmentMutationResponse>(itemId);
        }
        catch (EquipmentKindConflictException exception)
        {
            return KindConflict<HandEquipmentMutationResponse>(exception.Message);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<HandEquipmentMutationResponse>(exception);
        }
    }

    private async Task AddDisableReferenceErrorsAsync(
        string itemId,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (await _basicItemRepository.HasLiveReferencesAsync(itemId, cancellationToken))
        {
            messages.Add(LiveReferenceError(itemId));
        }
        if (await _basicItemRepository.HasPublishedConsumableResultReferencesAsync(itemId, cancellationToken))
        {
            messages.Add(new ApiError(
                "item_has_published_consumable_references",
                $"Item '{itemId}' is the result of a published consumable and cannot be disabled.",
                ValidationSeverity.Error,
                "publication_state"));
        }
    }

    private HandEquipmentItemDefinition ToDefinition(HandEquipmentItemRecord record)
    {
        var asset = _assetService.Resolve(record.IconTexturePath);
        return new HandEquipmentItemDefinition(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            AuthoringKind(record),
            ClassificationLabel(record),
            record.EquipmentSlotId is not null,
            record.EquipmentSlotId,
            record.EquipmentSlotDisplayName,
            record.RequiredStrength,
            record.Requirements,
            record.SkillModifiers,
            record.WeaponProfile,
            record.CombatBonuses,
            record.ToolCapabilities,
            EditableInHandEquipment(record),
            DeriveVisualAssetKey(record.EquipmentSlotId, record.DisplayName),
            VisualAssetModelMessage,
            record.UpdatedAtUtc,
            asset.FilePath);
    }

    private static HandEquipmentItemSummary ToSummary(HandEquipmentItemRecord record) =>
        new(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            AuthoringKind(record),
            ClassificationLabel(record),
            record.EquipmentSlotId is not null,
            record.EquipmentSlotId,
            record.EquipmentSlotDisplayName,
            record.WeaponProfile is not null || record.HasCombatProfile,
            record.ToolCapabilities.Count > 0 || record.HasToolCapabilities,
            EditableInHandEquipment(record),
            record.UpdatedAtUtc);

    private static bool EditableInHandEquipment(HandEquipmentItemRecord record) =>
        !record.HasConsumableProfile
        && (record.EquipmentSlotId is null
            || EquipmentItemRepository.IsHandSlot(record.EquipmentSlotId)
            || EquipmentItemRepository.IsWearableSlot(record.EquipmentSlotId)
            || record.HasCombatProfile
            || record.HasToolCapabilities);

    private static string AuthoringKind(HandEquipmentItemRecord record) =>
        record.HasConsumableProfile ? "Consumable" : ClassificationLabel(record);

    private static string ClassificationLabel(HandEquipmentItemRecord record) =>
        HandEquipmentDomainRules.Classify(
            record.HasConsumableProfile,
            record.EquipmentSlotId,
            record.WeaponProfile,
            record.ToolCapabilities);

    private static NormalizedHandEquipmentDraft Normalize(SaveHandEquipmentDraftRequest request) =>
        Normalize(
            request.DisplayName,
            request.IconTexturePath,
            request.Equippable,
            request.EquipmentSlotId,
            request.RequiredStrength,
            request.Requirements,
            request.SkillModifiers,
            request.WeaponProfile,
            request.CombatBonuses,
            request.ToolCapabilities);

    private static NormalizedHandEquipmentDraft Normalize(HandEquipmentPreviewRequest request) =>
        Normalize(
            request.DisplayName,
            request.IconTexturePath,
            request.Equippable,
            request.EquipmentSlotId,
            request.RequiredStrength,
            request.Requirements,
            request.SkillModifiers,
            request.WeaponProfile,
            request.CombatBonuses,
            request.ToolCapabilities);

    private static NormalizedHandEquipmentDraft Normalize(
        string displayName,
        string iconTexturePath,
        bool equippable,
        string? equipmentSlotId,
        int requiredStrength,
        IReadOnlyList<EquipmentSkillRequirementDraft>? requirements,
        IReadOnlyList<EquipmentSkillModifierDraft>? modifiers,
        EquipmentCombatProfileDefinition? weaponProfile,
        EquipmentCombatBonusDefinition? bonuses,
        IReadOnlyList<HandEquipmentToolCapabilityDraft>? capabilities)
    {
        if (!equippable)
        {
            return new NormalizedHandEquipmentDraft(
                HandEquipmentDomainRules.NormalizeRequired(displayName),
                HandEquipmentDomainRules.NormalizeRequired(iconTexturePath),
                false,
                null,
                1,
                [],
                [],
                null,
                EquipmentCombatBonusDefinition.Zero,
                []);
        }

        return new NormalizedHandEquipmentDraft(
            HandEquipmentDomainRules.NormalizeRequired(displayName),
            HandEquipmentDomainRules.NormalizeRequired(iconTexturePath),
            true,
            HandEquipmentDomainRules.NormalizeOptional(equipmentSlotId),
            requiredStrength,
            (requirements ?? [])
                .Select(value => new EquipmentSkillRequirementDraft(
                    HandEquipmentDomainRules.NormalizeRequired(value.SkillId),
                    value.RequiredValue))
                .OrderBy(value => value.SkillId, StringComparer.Ordinal)
                .ToArray(),
            (modifiers ?? [])
                .Select(value => new EquipmentSkillModifierDraft(
                    HandEquipmentDomainRules.NormalizeRequired(value.SkillId),
                    value.ModifierValue))
                .OrderBy(value => value.SkillId, StringComparer.Ordinal)
                .ToArray(),
            weaponProfile is null
                ? null
                : new EquipmentCombatProfileDefinition(
                    HandEquipmentDomainRules.NormalizeRequired(weaponProfile.ProfileId),
                    HandEquipmentDomainRules.NormalizeRequired(weaponProfile.AttackType),
                    HandEquipmentDomainRules.NormalizeOptional(weaponProfile.AccuracyStyle),
                    weaponProfile.MinimumRangeTiles,
                    weaponProfile.MaximumRangeTiles,
                    weaponProfile.AttackSpeedUnits),
            bonuses ?? EquipmentCombatBonusDefinition.Zero,
            HandEquipmentDomainRules.NormalizeToolCapabilities(capabilities));
    }

    private static NormalizedHandEquipmentDraft FromRecord(HandEquipmentItemRecord record) =>
        Normalize(
            record.DisplayName,
            record.IconTexturePath,
            record.EquipmentSlotId is not null,
            record.EquipmentSlotId,
            record.RequiredStrength,
            record.Requirements.Select(value => new EquipmentSkillRequirementDraft(value.SkillId, value.RequiredValue)).ToArray(),
            record.SkillModifiers.Select(value => new EquipmentSkillModifierDraft(value.SkillId, value.ModifierValue)).ToArray(),
            record.WeaponProfile,
            record.CombatBonuses,
            record.ToolCapabilities.Select(value => new HandEquipmentToolCapabilityDraft(
                value.CapabilityId,
                value.PowerTier,
                value.ActionAnimationId,
                value.EffectResourceId)).ToArray());

    private static IReadOnlyList<BasicItemChange> CalculateChanges(
        HandEquipmentItemRecord existing,
        NormalizedHandEquipmentDraft requested,
        string operation)
    {
        var changes = new List<BasicItemChange>();
        AddChange(changes, "display_name", existing.DisplayName, requested.DisplayName);
        AddChange(changes, "icon_texture_path", existing.IconTexturePath, requested.IconTexturePath);
        AddChange(changes, "equippable", (existing.EquipmentSlotId is not null).ToString(), requested.Equippable.ToString());
        AddChange(changes, "equipment_slot_id", existing.EquipmentSlotId, requested.EquipmentSlotId);
        AddChange(changes, "required_strength", existing.RequiredStrength.ToString(), requested.RequiredStrength.ToString());
        AddChange(changes, "requirements", SerializeRequirements(existing), JsonSerializer.Serialize(requested.Requirements));
        AddChange(changes, "skill_modifiers", SerializeModifiers(existing), JsonSerializer.Serialize(requested.SkillModifiers));
        AddChange(changes, "weapon_profile", JsonSerializer.Serialize(existing.WeaponProfile), JsonSerializer.Serialize(requested.WeaponProfile));
        AddChange(changes, "combat_bonuses", JsonSerializer.Serialize(existing.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero), JsonSerializer.Serialize(requested.CombatBonuses));
        AddChange(changes, "tool_capabilities", SerializeToolCapabilities(existing), JsonSerializer.Serialize(requested.ToolCapabilities));
        var targetState = operation switch
        {
            "publish" => "Published",
            "delete" => "Deleted",
            _ => "Draft"
        };
        AddChange(changes, "publication_state", existing.RuntimeEnabled ? "Published" : "Draft", targetState);
        return changes;
    }

    private static string SerializeRequirements(HandEquipmentItemRecord record) =>
        JsonSerializer.Serialize(record.Requirements.Select(value => new EquipmentSkillRequirementDraft(value.SkillId, value.RequiredValue)));

    private static string SerializeModifiers(HandEquipmentItemRecord record) =>
        JsonSerializer.Serialize(record.SkillModifiers.Select(value => new EquipmentSkillModifierDraft(value.SkillId, value.ModifierValue)));

    private static string SerializeToolCapabilities(HandEquipmentItemRecord record) =>
        JsonSerializer.Serialize(record.ToolCapabilities.Select(value => new HandEquipmentToolCapabilityDraft(
            value.CapabilityId,
            value.PowerTier,
            value.ActionAnimationId,
            value.EffectResourceId)));

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

    private static bool EquivalentDraft(HandEquipmentItemRecord record, NormalizedHandEquipmentDraft draft) =>
        string.Equals(record.DisplayName, draft.DisplayName, StringComparison.Ordinal)
        && string.Equals(record.IconTexturePath, draft.IconTexturePath, StringComparison.Ordinal)
        && (record.EquipmentSlotId is not null) == draft.Equippable
        && string.Equals(record.EquipmentSlotId, draft.EquipmentSlotId, StringComparison.Ordinal)
        && record.RequiredStrength == draft.RequiredStrength
        && SerializeRequirements(record) == JsonSerializer.Serialize(draft.Requirements)
        && SerializeModifiers(record) == JsonSerializer.Serialize(draft.SkillModifiers)
        && record.WeaponProfile == draft.WeaponProfile
        && (record.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero) == draft.CombatBonuses
        && SerializeToolCapabilities(record) == JsonSerializer.Serialize(draft.ToolCapabilities);

    private static bool Equivalent(HandEquipmentItemRecord left, HandEquipmentItemRecord right) =>
        left.ItemId == right.ItemId
        && left.DisplayName == right.DisplayName
        && left.IconTexturePath == right.IconTexturePath
        && left.EquipmentSlotId == right.EquipmentSlotId
        && left.RuntimeEnabled == right.RuntimeEnabled
        && left.RequiredStrength == right.RequiredStrength
        && left.HasConsumableProfile == right.HasConsumableProfile
        && left.HasCombatProfile == right.HasCombatProfile
        && left.HasCombatBonuses == right.HasCombatBonuses
        && left.HasToolCapabilities == right.HasToolCapabilities
        && SerializeRequirements(left) == SerializeRequirements(right)
        && SerializeModifiers(left) == SerializeModifiers(right)
        && left.WeaponProfile == right.WeaponProfile
        && (left.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero) == (right.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero)
        && SerializeToolCapabilities(left) == SerializeToolCapabilities(right);

    private static string? DeriveVisualAssetKey(string? slotId, string displayName)
    {
        if (slotId is null)
        {
            return null;
        }

        var builder = new StringBuilder(displayName.Trim().Length);
        var previousWasSeparator = false;
        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            if (character is '\'' or '\u2019')
            {
                continue;
            }
            if (character is '-' or '/' || char.IsWhiteSpace(character))
            {
                if (!previousWasSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }
                continue;
            }
            builder.Append(character);
            previousWasSeparator = false;
        }

        return builder.ToString().Trim('_');
    }

    private static string ComputePreviewSignature(
        string itemId,
        string operation,
        NormalizedHandEquipmentDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        var payload = JsonSerializer.Serialize(new
        {
            item_id = itemId,
            operation,
            expected_updated_at_utc = expectedUpdatedAtUtc?.ToUniversalTime(),
            draft
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsMatchingPreview(
        string itemId,
        string operation,
        NormalizedHandEquipmentDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? suppliedSignature) =>
        string.Equals(
            suppliedSignature,
            ComputePreviewSignature(itemId, operation, draft, expectedUpdatedAtUtc),
            StringComparison.Ordinal);

    private static IReadOnlyList<AuthoringOption> CombatBonusOptions() =>
    [
        new AuthoringOption("attack_thrust", "Attack Thrust"),
        new AuthoringOption("attack_slash", "Attack Slash"),
        new AuthoringOption("attack_crush", "Attack Crush"),
        new AuthoringOption("attack_ranged", "Attack Ranged"),
        new AuthoringOption("attack_magic", "Attack Magic"),
        new AuthoringOption("strength_melee", "Strength Melee"),
        new AuthoringOption("strength_ranged", "Strength Ranged"),
        new AuthoringOption("strength_magic", "Strength Magic"),
        new AuthoringOption("defence_thrust", "Defence Thrust"),
        new AuthoringOption("defence_slash", "Defence Slash"),
        new AuthoringOption("defence_crush", "Defence Crush"),
        new AuthoringOption("defence_ranged", "Defence Ranged"),
        new AuthoringOption("defence_magic", "Defence Magic")
    ];

    private static string? NormalizePreviewOperation(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "save_draft" or "publish" or "disable" or "delete" ? normalized : null;
    }

    private static bool HasVersionConflict(HandEquipmentItemRecord existing, DateTimeOffset? expected) =>
        expected is null || existing.UpdatedAtUtc.ToUniversalTime() != expected.Value.ToUniversalTime();

    private static ApiError ItemNotFound(string itemId) => new(
        "item_not_found",
        $"Item '{itemId}' does not exist. Create the base item in Basic Items first.",
        ValidationSeverity.Error,
        "item_id");

    private static ApiError PreviewMismatch(string operation) => new(
        "preview_signature_mismatch",
        $"Preview the {operation} operation again before applying it.",
        ValidationSeverity.Error,
        "preview_signature");

    private static ApiError LiveReferenceError(string itemId) => new(
        "item_has_live_references",
        $"Item '{itemId}' is referenced by live inventory, equipment, or ground-item state and cannot be unpublished.",
        ValidationSeverity.Error,
        "publication_state");

    private static ApiError DeleteRequiresDisabledError(string itemId) => new(
        "delete_requires_disabled_item",
        $"Hand-equipment item '{itemId}' must be disabled before it can be deleted.",
        ValidationSeverity.Error,
        "publication_state",
        "Disable the hand-equipment item, preview Delete again, then apply the delete operation.");

    private static ApiError DeleteReferenceError(string itemId) => new(
        "item_delete_blocked_by_references",
        $"Hand-equipment item '{itemId}' cannot be deleted while another table references it.",
        ValidationSeverity.Error,
        "item_id",
        "Remove inventory, equipment, ground-item, result-item, mob-drop, or other content references before deleting.");

    private static AuthoringOperationResult<T> VersionConflict<T>(string itemId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "item_version_conflict",
            $"Item '{itemId}' changed after it was loaded. Reload before applying changes.",
            ValidationSeverity.Error,
            "updated_at_utc"));

    private static AuthoringOperationResult<T> KindConflict<T>(string message) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "wrong_authoring_workspace",
            message,
            ValidationSeverity.Error,
            "item_id"));

    private AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception)
    {
        _logger.LogWarning(exception, "Hand-equipment authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            "database_unavailable",
            "The configured development database is unavailable or missing the T3B hand-equipment schema.",
            ValidationSeverity.Error,
            Remediation: "Review the Environment tab and apply the MMO Project equipment, combat, runtime-publication, and tool-capability migrations."));
    }

    private static bool IsLiveReferenceGuard(PostgresException exception) =>
        exception.MessageText.Contains("Cannot disable runtime item", StringComparison.OrdinalIgnoreCase)
        || exception.ConstraintName?.Contains("runtime", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsForeignKeyViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException
            or InvalidOperationException;
}
