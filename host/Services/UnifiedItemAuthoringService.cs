using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class UnifiedItemAuthoringService
{
    private readonly IUnifiedItemRepository _repository;
    private readonly UnifiedItemValidator _validator;
    private readonly ItemAuthoringRegistry _registry;
    private readonly ItemAssetService _assetService;
    private readonly ActorAppearanceCatalogService _actorAppearanceCatalogService;
    private readonly ILogger<UnifiedItemAuthoringService> _logger;
    private readonly IRuntimeCatalogPublisher? _runtimeCatalogPublisher;

    public UnifiedItemAuthoringService(
        IUnifiedItemRepository repository,
        UnifiedItemValidator validator,
        ItemAuthoringRegistry registry,
        ItemAssetService assetService,
        ActorAppearanceCatalogService actorAppearanceCatalogService,
        ILogger<UnifiedItemAuthoringService> logger,
        IRuntimeCatalogPublisher? runtimeCatalogPublisher = null)
    {
        _repository = repository;
        _validator = validator;
        _registry = registry;
        _assetService = assetService;
        _actorAppearanceCatalogService = actorAppearanceCatalogService;
        _runtimeCatalogPublisher = runtimeCatalogPublisher;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<ItemOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var slots = await _repository.LoadSlotsAsync(cancellationToken);
            var skills = await _repository.LoadSkillsAsync(cancellationToken);
            var capabilities = await _repository.LoadGatheringCapabilitiesAsync(cancellationToken);
            var publishedItems = await _repository.LoadPublishedItemOptionsAsync(cancellationToken);
            var actorRigCatalog = _actorAppearanceCatalogService.LoadRigCatalog();
            return AuthoringOperationResult<ItemOptionsResponse>.Success(
                new ItemOptionsResponse(
                    slots.Select(slot => new AuthoringOption(slot.SlotId, slot.DisplayName)).ToArray(),
                    slots
                        .Where(slot => slot.SlotId == ItemAuthoringRegistry.ActiveWeaponSlotId)
                        .Select(slot => new AuthoringOption(slot.SlotId, slot.DisplayName))
                        .ToArray(),
                    skills.Select(skill => new AuthoringOption(skill.SkillId, skill.DisplayName)).ToArray(),
                    CombatBonusOptions(),
                    _registry.LoadAttackFamilies(),
                    _registry.LoadAttackStyles(),
                    capabilities.Select(skill => new AuthoringOption(skill.SkillId, skill.DisplayName)).ToArray(),
                    [new("eat", "Eat"), new("drink", "Drink"), new("use", "Use")],
                    [new("restore_resource", "Restore Resource")],
                    [new("health", "Health"), new("concentration", "Concentration"), new("special", "Special")],
                    [new("skill_minimum", "Skill Minimum")],
                    publishedItems,
                    [new("rig_layer", "Rig Layer"), new("socket", "Socket")],
                    actorRigCatalog,
                    ItemAuthoringRegistry.CombatUnitMilliseconds,
                    UnifiedItemDomainRules.MaximumPowerTier,
                    true));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ItemOptionsResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<ItemCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<ItemCatalogResponse>.Success(
                new ItemCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ItemCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<ItemDefinition>> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _repository.LoadAsync(itemId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<ItemDefinition>.Failure(ItemNotFound(itemId))
                : AuthoringOperationResult<ItemDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ItemDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<ItemPreviewResponse>> PreviewAsync(
        string itemId,
        PreviewItemRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<ItemPreviewResponse>(itemId);
            }

            var operation = NormalizePreviewOperation(request.TargetOperation);
            if (operation is null)
            {
                return AuthoringOperationResult<ItemPreviewResponse>.Failure(InvalidTargetOperation());
            }
            if (operation is "publish" or "disable" or "delete" && existing is null)
            {
                return AuthoringOperationResult<ItemPreviewResponse>.Failure(ItemNotFound(itemId));
            }

            var requested = Normalize(request);
            var hasUnsavedOperationChanges =
                existing is not null
                && operation is "publish" or "disable" or "delete"
                && !EquivalentDraft(existing, requested);
            var effective = operation == "save_draft" || hasUnsavedOperationChanges
                ? requested
                : UnifiedItemDomainRules.FromRecord(existing!);
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
                    "unsaved_item_changes",
                    "Save the edited complete item definition as a draft before changing publication state or deleting it.",
                    ValidationSeverity.Error,
                    "publication_state"));
            }
            await AddPublicationLifecycleErrorsAsync(itemId, existing, operation, messages, cancellationToken);
            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            return AuthoringOperationResult<ItemPreviewResponse>.Success(
                new ItemPreviewResponse(
                    operation,
                    validation.ValidForDraft && !hasErrors,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(existing, requested, operation),
                    validation.AssetPreviewFilePath,
                    ComputePreviewSignature(itemId, operation, effective, request.ExpectedUpdatedAtUtc)));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ItemPreviewResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<ItemMutationResponse>> SaveDraftAsync(
        string itemId,
        SaveItemDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            var wasRuntimeEnabled = existing?.RuntimeEnabled == true;
            var draft = Normalize(request);
            if (!IsMatchingPreview(itemId, "save_draft", draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<ItemMutationResponse>.Failure(PreviewMismatch("save_draft"));
            }

            var validation = await _validator.ValidateAsync(itemId, draft, existing, false, cancellationToken);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<ItemMutationResponse>.Failure(validation.Messages);
            }
            if (wasRuntimeEnabled)
            {
                var referenceErrors = new List<ApiError>();
                await AddDisableReferenceErrorsAsync(itemId, referenceErrors, cancellationToken);
                if (referenceErrors.Count > 0)
                {
                    return AuthoringOperationResult<ItemMutationResponse>.Failure(referenceErrors);
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
                throw new InvalidOperationException("The saved item aggregate failed reload-and-verify.");
            }

            var messages = validation.Messages.ToList();
            if (wasRuntimeEnabled && _runtimeCatalogPublisher is not null)
            {
                messages.AddRange(await _runtimeCatalogPublisher.PublishCatalogsAsync(cancellationToken));
            }

            return AuthoringOperationResult<ItemMutationResponse>.Success(
                new ItemMutationResponse("save_draft", ToDefinition(verified), messages));
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<ItemMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (UnifiedItemConcurrencyException)
        {
            return VersionConflict<ItemMutationResponse>(itemId);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ItemMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<ItemMutationResponse>> PublishAsync(
        string itemId,
        ItemPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, true, request.ExpectedUpdatedAtUtc, request.PreviewSignature, cancellationToken);

    public Task<AuthoringOperationResult<ItemMutationResponse>> DisableAsync(
        string itemId,
        ItemPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(itemId, false, request.ExpectedUpdatedAtUtc, request.PreviewSignature, cancellationToken);

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
            var draft = UnifiedItemDomainRules.FromRecord(existing);
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
        catch (UnifiedItemNotFoundException)
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(ItemNotFound(itemId));
        }
        catch (UnifiedItemConcurrencyException)
        {
            return VersionConflict<DeleteMutationResponse>(itemId);
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DeleteMutationResponse>(exception);
        }
    }

    private async Task<AuthoringOperationResult<ItemMutationResponse>> SetPublicationAsync(
        string itemId,
        bool published,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? previewSignature,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _repository.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<ItemMutationResponse>.Failure(ItemNotFound(itemId));
            }
            var draft = UnifiedItemDomainRules.FromRecord(existing);
            var operation = published ? "publish" : "disable";
            if (previewSignature is not null
                && !IsMatchingPreview(itemId, operation, draft, expectedUpdatedAtUtc, previewSignature))
            {
                return AuthoringOperationResult<ItemMutationResponse>.Failure(PreviewMismatch(operation));
            }
            var validation = await _validator.ValidateAsync(itemId, draft, existing, published, cancellationToken);
            var messages = validation.Messages.ToList();
            if (!published)
            {
                await AddDisableReferenceErrorsAsync(itemId, messages, cancellationToken);
            }
            if (messages.Any(message => message.Severity == ValidationSeverity.Error))
            {
                return AuthoringOperationResult<ItemMutationResponse>.Failure(messages);
            }
            var saved = await _repository.SetPublicationAsync(itemId, published, expectedUpdatedAtUtc, cancellationToken);
            var verified = await _repository.LoadAsync(itemId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified) || verified.RuntimeEnabled != published)
            {
                throw new InvalidOperationException("The item publication mutation failed reload-and-verify.");
            }

            if (_runtimeCatalogPublisher is not null)
            {
                messages.AddRange(await _runtimeCatalogPublisher.PublishCatalogsAsync(cancellationToken));
            }

            return AuthoringOperationResult<ItemMutationResponse>.Success(
                new ItemMutationResponse(operation, ToDefinition(verified), messages));
        }
        catch (UnifiedItemNotFoundException)
        {
            return AuthoringOperationResult<ItemMutationResponse>.Failure(ItemNotFound(itemId));
        }
        catch (UnifiedItemConcurrencyException)
        {
            return VersionConflict<ItemMutationResponse>(itemId);
        }
        catch (PostgresException exception) when (IsLiveReferenceGuard(exception))
        {
            return AuthoringOperationResult<ItemMutationResponse>.Failure(LiveReferenceError(itemId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<ItemMutationResponse>(exception);
        }
    }

    private async Task AddPublicationLifecycleErrorsAsync(
        string itemId,
        UnifiedItemRecord? existing,
        string operation,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
    {
        if (existing?.RuntimeEnabled == true && operation is "save_draft" or "disable")
        {
            await AddDisableReferenceErrorsAsync(itemId, messages, cancellationToken);
        }
        if (operation == "delete" && existing?.RuntimeEnabled == true)
        {
            messages.Add(DeleteRequiresDisabledError(itemId));
        }
    }

    private async Task AddDisableReferenceErrorsAsync(
        string itemId,
        ICollection<ApiError> messages,
        CancellationToken cancellationToken)
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

    private NormalizedItemDraft Normalize(PreviewItemRequest request) =>
        UnifiedItemDomainRules.Normalize(
            request.DisplayName,
            request.IconTexturePath,
            request.ConsumableBehavior,
            request.Equipment,
            request.ToolCapabilities);

    private NormalizedItemDraft Normalize(SaveItemDraftRequest request) =>
        UnifiedItemDomainRules.Normalize(
            request.DisplayName,
            request.IconTexturePath,
            request.ConsumableBehavior,
            request.Equipment,
            request.ToolCapabilities);

    private ItemDefinitionSummary ToSummary(UnifiedItemRecord record)
    {
        var hasConsumable = record.HasConsumableProfile;
        var hasEquipment = UnifiedItemDomainRules.HasEquipmentMetadata(record);
        var hasWeapon = record.HasCombatProfile;
        var hasTools = record.HasToolCapabilities;
        var label = ClassifySummary(hasConsumable, hasEquipment, hasWeapon, hasTools);
        return new ItemDefinitionSummary(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            PublicationState(record.RuntimeEnabled),
            label,
            label,
            hasConsumable,
            hasEquipment,
            hasWeapon,
            hasTools,
            record.UpdatedAtUtc);
    }

    private ItemDefinition ToDefinition(UnifiedItemRecord record)
    {
        var draft = UnifiedItemDomainRules.FromRecord(record);
        var label = UnifiedItemDomainRules.Classify(draft.ConsumableBehavior is not null, draft.Equipment, draft.ToolCapabilities);
        var asset = _assetService.Resolve(record.IconTexturePath);
        return new ItemDefinition(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            PublicationState(record.RuntimeEnabled),
            label,
            label,
            draft.ConsumableBehavior is null ? null : ToConsumableDefinition(draft.ConsumableBehavior),
            draft.Equipment is null ? null : ToEquipmentDefinition(draft.Equipment, record.EquipmentSlotDisplayName),
            record.ToolCapabilities,
            record.UpdatedAtUtc,
            asset.FilePath);
    }

    private static ItemConsumableBehaviorDefinition ToConsumableDefinition(NormalizedItemConsumableBehavior consumable) =>
        new(
            consumable.UseAction,
            consumable.ConsumeQuantity,
            consumable.ResultItemId,
            consumable.SuccessMessage,
            consumable.UsableInCombat,
            consumable.CooldownMs,
            consumable.UseAnimationId,
            consumable.UseSoundResourcePath,
            consumable.Requirements,
            consumable.Effects);

    private static ItemEquipmentMetadataDefinition ToEquipmentDefinition(
        NormalizedItemEquipmentMetadata equipment,
        string? displayName) =>
        new(
            equipment.EquipmentSlotId,
            displayName,
            equipment.RequiredStrength,
            equipment.Requirements
                .Select(value => new EquipmentSkillRequirementDefinition(value.SkillId, value.SkillId, value.RequiredValue))
                .ToArray(),
            equipment.SkillModifiers
                .Select(value => new EquipmentSkillModifierDefinition(value.SkillId, value.SkillId, value.ModifierValue))
                .ToArray(),
            equipment.CombatBonuses,
            equipment.WeaponProfile,
            equipment.EquippedVisual is null
                ? null
                : new ItemEquippedVisualDefinition(
                    equipment.EquippedVisual.AssetKey ?? string.Empty,
                    equipment.EquippedVisual.RigId ?? string.Empty,
                    equipment.EquippedVisual.BindingType ?? string.Empty,
                    equipment.EquippedVisual.RenderLayerId ?? string.Empty,
                    equipment.EquippedVisual.SocketId,
                    equipment.EquippedVisual.SecondarySocketId,
                    equipment.EquippedVisual.Nudge,
                    equipment.EquippedVisual.GripAnchors));

    private static string ClassifySummary(
        bool hasConsumable,
        bool hasEquipment,
        bool hasWeapon,
        bool hasTools)
    {
        var labels = new List<string>();
        if (hasConsumable)
        {
            labels.Add("Consumable");
        }
        if (hasWeapon)
        {
            labels.Add("Weapon");
        }
        else if (hasEquipment)
        {
            labels.Add("Equipment");
        }
        if (hasTools)
        {
            labels.Add("Tool");
        }

        return labels.Count == 0 ? "Basic" : string.Join(" + ", labels);
    }
    private static IReadOnlyList<AuthoringChange> CalculateChanges(
        UnifiedItemRecord? existing,
        NormalizedItemDraft requested,
        string operation)
    {
        var current = existing is null ? null : UnifiedItemDomainRules.FromRecord(existing);
        var changes = new List<AuthoringChange>();
        AddChange(changes, "display_name", current?.DisplayName, requested.DisplayName);
        AddChange(changes, "icon_texture_path", current?.IconTexturePath, requested.IconTexturePath);
        AddChange(changes, "consumable_behavior", Serialize(current?.ConsumableBehavior), Serialize(requested.ConsumableBehavior));
        AddChange(changes, "equipment", Serialize(current?.Equipment), Serialize(requested.Equipment));
        AddChange(changes, "tool_capabilities", Serialize(current?.ToolCapabilities ?? []), Serialize(requested.ToolCapabilities));
        var before = existing?.RuntimeEnabled == true ? "Published" : existing is null ? null : "Draft";
        var after = operation switch
        {
            "publish" => "Published",
            "delete" => "Deleted",
            _ => "Draft"
        };
        AddChange(changes, "publication_state", before, after);
        return changes;
    }

    private static void AddChange(ICollection<AuthoringChange> changes, string field, string? before, string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new AuthoringChange(field, before, after));
        }
    }

    private static bool EquivalentDraft(UnifiedItemRecord record, NormalizedItemDraft draft) =>
        EquivalentDrafts(UnifiedItemDomainRules.FromRecord(record), draft);

    private static bool Equivalent(UnifiedItemRecord left, UnifiedItemRecord right) =>
        left.ItemId == right.ItemId
        && EquivalentDrafts(UnifiedItemDomainRules.FromRecord(left), UnifiedItemDomainRules.FromRecord(right))
        && left.RuntimeEnabled == right.RuntimeEnabled;

    private static bool EquivalentDrafts(NormalizedItemDraft left, NormalizedItemDraft right) =>
        string.Equals(Serialize(left), Serialize(right), StringComparison.Ordinal);

    private static string ComputePreviewSignature(
        string itemId,
        string operation,
        NormalizedItemDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        var payload = JsonSerializer.Serialize(new
        {
            item_id = itemId,
            operation,
            draft,
            expected_updated_at_utc = expectedUpdatedAtUtc?.ToUniversalTime()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static bool IsMatchingPreview(
        string itemId,
        string operation,
        NormalizedItemDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? previewSignature) =>
        !string.IsNullOrWhiteSpace(previewSignature)
        && string.Equals(
            previewSignature,
            ComputePreviewSignature(itemId, operation, draft, expectedUpdatedAtUtc),
            StringComparison.Ordinal);

    private static string? NormalizePreviewOperation(string? operation)
    {
        var normalized = (operation ?? string.Empty).Trim();
        return normalized is "save_draft" or "publish" or "disable" or "delete"
            ? normalized
            : null;
    }

    private static string PublicationState(bool runtimeEnabled) => runtimeEnabled ? "Published" : "Draft";

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    private static IReadOnlyList<AuthoringOption> CombatBonusOptions() =>
    [
        new("attack_thrust", "Attack Thrust"),
        new("attack_slash", "Attack Slash"),
        new("attack_crush", "Attack Crush"),
        new("attack_ranged", "Attack Ranged"),
        new("attack_magic", "Attack Magic"),
        new("strength_melee", "Strength Melee"),
        new("strength_ranged", "Strength Ranged"),
        new("strength_magic", "Strength Magic"),
        new("defence_thrust", "Defence Thrust"),
        new("defence_slash", "Defence Slash"),
        new("defence_crush", "Defence Crush"),
        new("defence_ranged", "Defence Ranged"),
        new("defence_magic", "Defence Magic")
    ];

    private static ApiError ItemNotFound(string itemId) => new(
        "item_not_found",
        $"Item '{itemId}' does not exist.",
        ValidationSeverity.Error,
        "item_id");

    private static ApiError InvalidTargetOperation() => new(
        "invalid_target_operation",
        "Target operation must be save_draft, publish, disable, or delete.",
        ValidationSeverity.Error,
        "target_operation");

    private static ApiError PreviewMismatch(string operation) => new(
        "preview_signature_mismatch",
        $"Preview the {operation} operation again before applying it.",
        ValidationSeverity.Error,
        "preview_signature");

    private static ApiError DeleteRequiresDisabledError(string itemId) => new(
        "delete_requires_disabled_item",
        $"Item '{itemId}' must be disabled before it can be deleted.",
        ValidationSeverity.Error,
        "publication_state");

    private static ApiError LiveReferenceError(string itemId) => new(
        "item_has_live_references",
        $"Item '{itemId}' is referenced by live inventory, equipment, or ground-item state.",
        ValidationSeverity.Error,
        "publication_state");

    private static ApiError PublishedConsumableReferenceError(string itemId) => new(
        "item_has_published_consumable_references",
        $"Item '{itemId}' is the result item for a published consumable.",
        ValidationSeverity.Error,
        "publication_state");

    private static AuthoringOperationResult<T> VersionConflict<T>(string itemId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "item_version_conflict",
            $"Item '{itemId}' changed after it was loaded. Reload before applying changes.",
            ValidationSeverity.Error,
            "expected_updated_at_utc"));

    private static AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "database_unavailable",
            "The configured development database is unavailable or missing the item-authoring schema.",
            ValidationSeverity.Error,
            null,
            exception.Message));

    private static bool HasVersionConflict(
        UnifiedItemRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc) =>
        existing is not null
        && (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime());

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException
            or InvalidOperationException;

    private static bool IsLiveReferenceGuard(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.RaiseException
        && exception.MessageText.Contains("runtime-enabled item", StringComparison.OrdinalIgnoreCase);
}
