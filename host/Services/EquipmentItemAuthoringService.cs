using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class EquipmentItemAuthoringService
{
    private const string VisualAssetModelMessage =
        "The current runtime derives player-layer asset keys from item name and slot. T3A does not store a separate visual asset override yet.";

    private readonly EquipmentItemRepository _repository;
    private readonly BasicItemRepository _basicItemRepository;
    private readonly EquipmentItemValidator _validator;
    private readonly ItemAssetService _assetService;
    private readonly ILogger<EquipmentItemAuthoringService> _logger;

    public EquipmentItemAuthoringService(
        EquipmentItemRepository repository,
        BasicItemRepository basicItemRepository,
        EquipmentItemValidator validator,
        ItemAssetService assetService,
        ILogger<EquipmentItemAuthoringService> logger)
    {
        _repository = repository;
        _basicItemRepository = basicItemRepository;
        _validator = validator;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<EquipmentAuthoringOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var slots = await _repository.LoadSlotsAsync(cancellationToken);
            var skills = await _repository.LoadSkillsAsync(cancellationToken);
            return AuthoringOperationResult<EquipmentAuthoringOptionsResponse>.Success(
                new EquipmentAuthoringOptionsResponse(
                    slots
                        .Where(slot => EquipmentItemRepository.IsWearableSlot(slot.SlotId))
                        .Select(slot => new AuthoringOption(slot.SlotId, slot.DisplayName))
                        .ToArray(),
                    slots
                        .Where(slot => EquipmentItemRepository.IsHandSlot(slot.SlotId))
                        .Select(slot => new AuthoringOption(slot.SlotId, slot.DisplayName))
                        .ToArray(),
                    skills
                        .Select(skill => new AuthoringOption(skill.SkillId, skill.DisplayName))
                        .ToArray(),
                    CombatBonusOptions(),
                    false,
                    VisualAssetModelMessage));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentAuthoringOptionsResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<EquipmentCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<EquipmentCatalogResponse>.Success(
                new EquipmentCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<EquipmentItemDefinition>> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _repository.LoadAsync(itemId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<EquipmentItemDefinition>.Failure(ItemNotFound(itemId))
                : AuthoringOperationResult<EquipmentItemDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentItemDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<EquipmentValidationResponse>> PreviewAsync(
        string itemId,
        EquipmentPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<EquipmentValidationResponse>.Failure(ItemNotFound(itemId));
            }
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<EquipmentValidationResponse>(itemId);
            }

            var operation = NormalizePreviewOperation(request.TargetOperation);
            if (operation is null)
            {
                return AuthoringOperationResult<EquipmentValidationResponse>.Failure(new ApiError(
                    "invalid_target_operation",
                    "Target operation must be save_draft, publish, disable, or delete.",
                    ValidationSeverity.Error,
                    "target_operation"));
            }

            var requested = Normalize(request);
            var effective = operation == "save_draft" ? requested : FromRecord(existing);
            var validation = await _validator.ValidateAsync(
                itemId,
                effective,
                existing,
                operation == "publish",
                cancellationToken);
            var messages = validation.Messages.ToList();

            if (operation is "publish" or "disable" or "delete")
            {
                if (!EquivalentDraft(existing, requested))
                {
                    messages.Add(new ApiError(
                        "unsaved_equipment_changes",
                        "Save the edited equipment definition as a draft before changing publication state.",
                        ValidationSeverity.Error,
                        "publication_state"));
                }
                if (existing.HasCombatProfile || EquipmentItemRepository.IsHandSlot(existing.EquipmentSlotId))
                {
                    messages.Add(new ApiError(
                        "weapon_or_tool_requires_t3b",
                        "Publication and delete changes for hand-held weapons and tools remain in T3B.",
                        ValidationSeverity.Error,
                        "equipment_slot_id"));
                }
                else if (operation is "publish" or "disable" && !EquipmentItemRepository.IsWearableSlot(existing.EquipmentSlotId))
                {
                    messages.Add(NotWearableEquipment(itemId));
                }
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
            return AuthoringOperationResult<EquipmentValidationResponse>.Success(
                new EquipmentValidationResponse(
                    operation,
                    validation.ValidForDraft && !hasErrors,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(existing, requested, operation),
                    validation.AssetPreviewFilePath));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentValidationResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<EquipmentMutationResponse>> SaveDraftAsync(
        string itemId,
        SaveEquipmentDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<EquipmentMutationResponse>.Failure(ItemNotFound(itemId));
            }

            var draft = Normalize(request);
            var validation = await _validator.ValidateAsync(
                itemId,
                draft,
                existing,
                false,
                cancellationToken);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<EquipmentMutationResponse>.Failure(validation.Messages);
            }

            if (existing.RuntimeEnabled)
            {
                var referenceErrors = new List<ApiError>();
                await AddDisableReferenceErrorsAsync(itemId, referenceErrors, cancellationToken);
                if (referenceErrors.Count > 0)
                {
                    return AuthoringOperationResult<EquipmentMutationResponse>.Failure(referenceErrors);
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
                throw new InvalidOperationException("The saved equipment aggregate failed reload-and-verify.");
            }

            return AuthoringOperationResult<EquipmentMutationResponse>.Success(
                new EquipmentMutationResponse(
                    "save_draft",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<EquipmentMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (EquipmentItemNotFoundException)
        {
            return AuthoringOperationResult<EquipmentMutationResponse>.Failure(ItemNotFound(itemId));
        }
        catch (EquipmentConcurrencyException)
        {
            return VersionConflict<EquipmentMutationResponse>(itemId);
        }
        catch (EquipmentKindConflictException exception)
        {
            return KindConflict<EquipmentMutationResponse>(exception.Message);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<EquipmentMutationResponse>> PublishAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, true, expectedUpdatedAtUtc, cancellationToken);

    public Task<AuthoringOperationResult<EquipmentMutationResponse>> DisableAsync(
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
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(ItemNotFound(itemId));
            }
            if (existing.HasCombatProfile || EquipmentItemRepository.IsHandSlot(existing.EquipmentSlotId))
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(new ApiError(
                    "weapon_or_tool_requires_t3b",
                    "Hand-held weapons and tools must be deleted from the T3B workspace.",
                    ValidationSeverity.Error,
                    "equipment_slot_id"));
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

    private async Task<AuthoringOperationResult<EquipmentMutationResponse>> SetPublicationAsync(
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
                return AuthoringOperationResult<EquipmentMutationResponse>.Failure(ItemNotFound(itemId));
            }
            if (existing.HasCombatProfile || EquipmentItemRepository.IsHandSlot(existing.EquipmentSlotId))
            {
                return AuthoringOperationResult<EquipmentMutationResponse>.Failure(new ApiError(
                    "weapon_or_tool_requires_t3b",
                    "Hand-held weapons and tools must be published from the T3B workspace.",
                    ValidationSeverity.Error,
                    "equipment_slot_id"));
            }
            if (!EquipmentItemRepository.IsWearableSlot(existing.EquipmentSlotId))
            {
                return AuthoringOperationResult<EquipmentMutationResponse>.Failure(NotWearableEquipment(itemId));
            }

            var draft = FromRecord(existing);
            var validation = await _validator.ValidateAsync(
                itemId,
                draft,
                existing,
                published,
                cancellationToken);
            var valid = published ? validation.ValidForPublication : validation.ValidForDraft;
            if (!valid)
            {
                return AuthoringOperationResult<EquipmentMutationResponse>.Failure(validation.Messages);
            }

            if (!published && existing.RuntimeEnabled)
            {
                var referenceErrors = new List<ApiError>();
                await AddDisableReferenceErrorsAsync(itemId, referenceErrors, cancellationToken);
                if (referenceErrors.Count > 0)
                {
                    return AuthoringOperationResult<EquipmentMutationResponse>.Failure(referenceErrors);
                }
            }

            var saved = await _repository.SetPublicationAsync(
                itemId,
                published,
                expectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(itemId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified) || verified.RuntimeEnabled != published)
            {
                throw new InvalidOperationException("The equipment publication change failed reload-and-verify.");
            }

            return AuthoringOperationResult<EquipmentMutationResponse>.Success(
                new EquipmentMutationResponse(
                    published ? "publish" : "disable",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<EquipmentMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (EquipmentItemNotFoundException)
        {
            return AuthoringOperationResult<EquipmentMutationResponse>.Failure(ItemNotFound(itemId));
        }
        catch (EquipmentConcurrencyException)
        {
            return VersionConflict<EquipmentMutationResponse>(itemId);
        }
        catch (EquipmentKindConflictException exception)
        {
            return KindConflict<EquipmentMutationResponse>(exception.Message);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentMutationResponse>(exception);
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

    private EquipmentItemDefinition ToDefinition(EquipmentItemRecord record)
    {
        var asset = _assetService.Resolve(record.IconTexturePath);
        return new EquipmentItemDefinition(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            AuthoringKind(record),
            record.EquipmentSlotId is not null,
            record.EquipmentSlotId,
            record.EquipmentSlotDisplayName,
            record.RequiredStrength,
            record.Requirements,
            record.SkillModifiers,
            record.CombatProfile,
            record.CombatBonuses,
            EditableInEquipment(record),
            CanRemoveEquipability(record),
            DeriveVisualAssetKey(record.EquipmentSlotId, record.DisplayName),
            VisualAssetModelMessage,
            record.UpdatedAtUtc,
            asset.FilePath);
    }

    private static EquipmentItemSummary ToSummary(EquipmentItemRecord record) =>
        new(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            AuthoringKind(record),
            record.EquipmentSlotId is not null,
            record.EquipmentSlotId,
            record.EquipmentSlotDisplayName,
            EditableInEquipment(record),
            CanRemoveEquipability(record),
            record.UpdatedAtUtc);

    private static bool EditableInEquipment(EquipmentItemRecord record) =>
        !record.HasConsumableProfile
        && !record.HasCombatProfile
        && (record.EquipmentSlotId is null || EquipmentItemRepository.IsWearableSlot(record.EquipmentSlotId));

    private static bool CanRemoveEquipability(EquipmentItemRecord record) =>
        !record.HasConsumableProfile && EquipmentItemRepository.HasEquipmentMetadata(record);

    private static string AuthoringKind(EquipmentItemRecord record)
    {
        if (record.HasConsumableProfile)
        {
            return "Consumable";
        }
        if (EquipmentItemRepository.IsHandSlot(record.EquipmentSlotId) || record.HasCombatProfile)
        {
            return "WeaponOrTool";
        }
        return EquipmentItemRepository.HasEquipmentMetadata(record) ? "Equipment" : "Basic";
    }

    private static NormalizedEquipmentDraft Normalize(SaveEquipmentDraftRequest request) =>
        Normalize(
            request.DisplayName,
            request.IconTexturePath,
            request.Equippable,
            request.EquipmentSlotId,
            request.RequiredStrength,
            request.Requirements,
            request.SkillModifiers,
            request.CombatBonuses);

    private static NormalizedEquipmentDraft Normalize(EquipmentPreviewRequest request) =>
        Normalize(
            request.DisplayName,
            request.IconTexturePath,
            request.Equippable,
            request.EquipmentSlotId,
            request.RequiredStrength,
            request.Requirements,
            request.SkillModifiers,
            request.CombatBonuses);

    private static NormalizedEquipmentDraft Normalize(
        string displayName,
        string iconTexturePath,
        bool equippable,
        string? equipmentSlotId,
        int requiredStrength,
        IReadOnlyList<EquipmentSkillRequirementDraft>? requirements,
        IReadOnlyList<EquipmentSkillModifierDraft>? modifiers,
        EquipmentCombatBonusDefinition? bonuses)
    {
        if (!equippable)
        {
            return new NormalizedEquipmentDraft(
                (displayName ?? string.Empty).Trim(),
                (iconTexturePath ?? string.Empty).Trim(),
                false,
                null,
                1,
                [],
                [],
                EquipmentCombatBonusDefinition.Zero);
        }

        return new NormalizedEquipmentDraft(
            (displayName ?? string.Empty).Trim(),
            (iconTexturePath ?? string.Empty).Trim(),
            true,
            NormalizeOptional(equipmentSlotId),
            requiredStrength,
            (requirements ?? [])
                .Select(value => new EquipmentSkillRequirementDraft((value.SkillId ?? string.Empty).Trim(), value.RequiredValue))
                .OrderBy(value => value.SkillId, StringComparer.Ordinal)
                .ToArray(),
            (modifiers ?? [])
                .Select(value => new EquipmentSkillModifierDraft((value.SkillId ?? string.Empty).Trim(), value.ModifierValue))
                .OrderBy(value => value.SkillId, StringComparer.Ordinal)
                .ToArray(),
            bonuses ?? EquipmentCombatBonusDefinition.Zero);
    }

    private static NormalizedEquipmentDraft FromRecord(EquipmentItemRecord record) =>
        Normalize(
            record.DisplayName,
            record.IconTexturePath,
            record.EquipmentSlotId is not null,
            record.EquipmentSlotId,
            record.RequiredStrength,
            record.Requirements.Select(value => new EquipmentSkillRequirementDraft(value.SkillId, value.RequiredValue)).ToArray(),
            record.SkillModifiers.Select(value => new EquipmentSkillModifierDraft(value.SkillId, value.ModifierValue)).ToArray(),
            record.CombatBonuses);

    private static IReadOnlyList<BasicItemChange> CalculateChanges(
        EquipmentItemRecord existing,
        NormalizedEquipmentDraft requested,
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
        AddChange(changes, "combat_bonuses", JsonSerializer.Serialize(existing.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero), JsonSerializer.Serialize(requested.CombatBonuses));
        var targetState = operation switch
        {
            "publish" => "Published",
            "delete" => "Deleted",
            _ => "Draft"
        };
        AddChange(changes, "publication_state", existing.RuntimeEnabled ? "Published" : "Draft", targetState);
        if (!requested.Equippable && existing.HasCombatProfile)
        {
            changes.Add(new BasicItemChange("combat_profile", JsonSerializer.Serialize(existing.CombatProfile), null));
        }
        return changes;
    }

    private static string SerializeRequirements(EquipmentItemRecord record) =>
        JsonSerializer.Serialize(record.Requirements.Select(value => new EquipmentSkillRequirementDraft(value.SkillId, value.RequiredValue)));

    private static string SerializeModifiers(EquipmentItemRecord record) =>
        JsonSerializer.Serialize(record.SkillModifiers.Select(value => new EquipmentSkillModifierDraft(value.SkillId, value.ModifierValue)));

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

    private static bool EquivalentDraft(EquipmentItemRecord record, NormalizedEquipmentDraft draft) =>
        string.Equals(record.DisplayName, draft.DisplayName, StringComparison.Ordinal)
        && string.Equals(record.IconTexturePath, draft.IconTexturePath, StringComparison.Ordinal)
        && (record.EquipmentSlotId is not null) == draft.Equippable
        && string.Equals(record.EquipmentSlotId, draft.EquipmentSlotId, StringComparison.Ordinal)
        && record.RequiredStrength == draft.RequiredStrength
        && SerializeRequirements(record) == JsonSerializer.Serialize(draft.Requirements)
        && SerializeModifiers(record) == JsonSerializer.Serialize(draft.SkillModifiers)
        && (record.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero) == draft.CombatBonuses;

    private static bool Equivalent(EquipmentItemRecord left, EquipmentItemRecord right) =>
        left.ItemId == right.ItemId
        && left.DisplayName == right.DisplayName
        && left.IconTexturePath == right.IconTexturePath
        && left.EquipmentSlotId == right.EquipmentSlotId
        && left.RuntimeEnabled == right.RuntimeEnabled
        && left.RequiredStrength == right.RequiredStrength
        && left.HasConsumableProfile == right.HasConsumableProfile
        && left.HasCombatProfile == right.HasCombatProfile
        && SerializeRequirements(left) == SerializeRequirements(right)
        && SerializeModifiers(left) == SerializeModifiers(right)
        && (left.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero) == (right.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero);

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

        var key = builder.ToString().Trim('_');
        return slotId == "legs" && key.EndsWith("_legs", StringComparison.Ordinal)
            ? key[..^"_legs".Length]
            : key;
    }

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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool HasVersionConflict(EquipmentItemRecord existing, DateTimeOffset? expected) =>
        expected is null || existing.UpdatedAtUtc.ToUniversalTime() != expected.Value.ToUniversalTime();

    private static ApiError ItemNotFound(string itemId) => new(
        "item_not_found",
        $"Item '{itemId}' does not exist. Create the base item in Basic Items first.",
        ValidationSeverity.Error,
        "item_id");


    private static ApiError NotWearableEquipment(string itemId) => new(
        "wrong_authoring_workspace",
        $"Item '{itemId}' is not saved as wearable equipment.",
        ValidationSeverity.Error,
        "equipment_slot_id",
        "Enable Equippable, choose a wearable slot, and save a draft before publishing; otherwise use Basic Items for publication changes.");

    private static ApiError LiveReferenceError(string itemId) => new(
        "item_has_live_references",
        $"Item '{itemId}' is referenced by live inventory, equipment, or ground-item state and cannot be unpublished.",
        ValidationSeverity.Error,
        "publication_state");

    private static ApiError DeleteRequiresDisabledError(string itemId) => new(
        "delete_requires_disabled_item",
        $"Equipment item '{itemId}' must be disabled before it can be deleted.",
        ValidationSeverity.Error,
        "publication_state",
        "Disable the equipment item, preview Delete again, then apply the delete operation.");

    private static ApiError DeleteReferenceError(string itemId) => new(
        "item_delete_blocked_by_references",
        $"Equipment item '{itemId}' cannot be deleted while another table references it.",
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
        _logger.LogWarning(exception, "Equipment authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            "database_unavailable",
            "The configured development database is unavailable or missing the T3A equipment schema.",
            ValidationSeverity.Error,
            Remediation: "Review the Environment tab and apply the MMO Project equipment, skill, and combat-bonus migrations."));
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
