using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class NpcAuthoringService
{
    private const string NpcVisualResourcePrefix = "res://assets/actors/npcs/";

    private readonly INpcRepository _repository;
    private readonly NpcDefinitionValidator _validator;
    private readonly NpcAuthoringRegistry _registry;
    private readonly NpcDialogueReferenceProvider _dialogueReferences;
    private readonly ItemAssetService _assetService;
    private readonly ILogger<NpcAuthoringService> _logger;

    public NpcAuthoringService(
        INpcRepository repository,
        NpcDefinitionValidator validator,
        NpcAuthoringRegistry registry,
        NpcDialogueReferenceProvider dialogueReferences,
        ItemAssetService assetService,
        ILogger<NpcAuthoringService> logger)
    {
        _repository = repository;
        _validator = validator;
        _registry = registry;
        _dialogueReferences = dialogueReferences;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<NpcOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var dialogueReferences = await _dialogueReferences.LoadAsync(cancellationToken);
        return AuthoringOperationResult<NpcOptionsResponse>.Success(
            new NpcOptionsResponse(
                _registry.LoadPublicationStates(),
                _registry.LoadMovementBehaviors(),
                _registry.LoadInteractionTypes(),
                dialogueReferences.DialogueReferences,
                dialogueReferences.Complete,
                _registry.LoadSupportedLimits(),
                new NpcVisualAssetOptions(
                    _assetService.GetGameAssetsRoot() is not null,
                    NpcVisualResourcePrefix,
                    _assetService.GetGameAssetsRoot()),
                new NpcOperationCapabilities(
                    true,
                    dialogueReferences.Complete,
                    false,
                    false),
                _registry.Defaults));
    }

    public async Task<AuthoringOperationResult<NpcCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<NpcCatalogResponse>.Success(
                new NpcCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<NpcCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<NpcDefinition>> LoadAsync(
        string npcDefinitionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = NpcDomainRules.NormalizeStableId(npcDefinitionId);
            var record = await _repository.LoadAsync(stableId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<NpcDefinition>.Failure(NpcNotFound(stableId))
                : AuthoringOperationResult<NpcDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<NpcDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<NpcPreviewResponse>> PreviewAsync(
        string npcDefinitionId,
        PreviewNpcRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = NpcDomainRules.NormalizeStableId(npcDefinitionId);
            var operation = NormalizePreviewOperation(request.TargetOperation);
            if (operation is null)
            {
                return AuthoringOperationResult<NpcPreviewResponse>.Failure(InvalidTargetOperation());
            }

            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<NpcPreviewResponse>(stableId);
            }
            if (existing is null && operation is "publish" or "disable" or "delete")
            {
                return AuthoringOperationResult<NpcPreviewResponse>.Failure(NpcNotFound(stableId));
            }

            var requested = Normalize(request);
            var effective = operation == "save_draft" ? requested : FromRecord(existing!);
            var validation = await _validator.ValidateAsync(
                stableId,
                effective,
                existing,
                operation == "publish",
                cancellationToken);
            var messages = validation.Messages.ToList();

            if (operation is "publish" or "disable" or "delete" && !EquivalentDraft(existing!, requested))
            {
                messages.Add(new ApiError(
                    "unsaved_npc_changes",
                    "Save the edited NPC definition as a draft before changing publication state or deleting it.",
                    ValidationSeverity.Error,
                    "publication_state"));
            }
            if (operation == "delete" && existing!.PublicationState != "Disabled")
            {
                messages.Add(DeleteRequiresDisabledError(stableId));
            }

            var referenceSummary = await LoadReferenceSummaryAsync(stableId, cancellationToken);
            AddReferenceDiagnostics(messages, operation, referenceSummary);

            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            var validForDraft = operation == "save_draft"
                ? validation.ValidForDraft && !messages.Any(NpcDefinitionValidator.IsDraftBlocking)
                : validation.ValidForDraft && !hasErrors;
            return AuthoringOperationResult<NpcPreviewResponse>.Success(
                new NpcPreviewResponse(
                    operation,
                    validForDraft,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(stableId, existing, requested, operation),
                    validation.AssetPreviewFilePath,
                    ToReferenceSummary(referenceSummary),
                    ComputePreviewSignature(
                        stableId,
                        operation,
                        effective,
                        request.ExpectedUpdatedAtUtc)));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<NpcPreviewResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<NpcMutationResponse>> SaveDraftAsync(
        string npcDefinitionId,
        SaveNpcDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = NpcDomainRules.NormalizeStableId(npcDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            var draft = Normalize(request);
            if (!IsMatchingPreview(
                    stableId,
                    "save_draft",
                    draft,
                    request.ExpectedUpdatedAtUtc,
                    request.PreviewSignature))
            {
                return AuthoringOperationResult<NpcMutationResponse>.Failure(PreviewMismatch("save_draft"));
            }

            var validation = await _validator.ValidateAsync(
                stableId,
                draft,
                existing,
                false,
                cancellationToken);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<NpcMutationResponse>.Failure(validation.Messages);
            }

            var saved = await _repository.SaveDraftAsync(
                stableId,
                draft,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified))
            {
                return ReloadVerificationFailure<NpcMutationResponse>(stableId);
            }

            return AuthoringOperationResult<NpcMutationResponse>.Success(
                new NpcMutationResponse(
                    "save_draft",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (NpcDefinitionConcurrencyException)
        {
            return VersionConflict<NpcMutationResponse>(NpcDomainRules.NormalizeStableId(npcDefinitionId));
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            return AuthoringOperationResult<NpcMutationResponse>.Failure(DuplicateNpcId(npcDefinitionId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<NpcMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<NpcMutationResponse>> PublishAsync(
        string npcDefinitionId,
        NpcPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(npcDefinitionId, "Published", "publish", request, cancellationToken);

    public Task<AuthoringOperationResult<NpcMutationResponse>> DisableAsync(
        string npcDefinitionId,
        NpcPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(npcDefinitionId, "Disabled", "disable", request, cancellationToken);

    public async Task<AuthoringOperationResult<NpcDeleteResponse>> DeleteAsync(
        string npcDefinitionId,
        NpcDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = NpcDomainRules.NormalizeStableId(npcDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<NpcDeleteResponse>.Failure(NpcNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(
                    stableId,
                    "delete",
                    draft,
                    request.ExpectedUpdatedAtUtc,
                    request.PreviewSignature))
            {
                return AuthoringOperationResult<NpcDeleteResponse>.Failure(PreviewMismatch("delete"));
            }
            if (existing.PublicationState != "Disabled")
            {
                return AuthoringOperationResult<NpcDeleteResponse>.Failure(DeleteRequiresDisabledError(stableId));
            }

            var references = await LoadReferenceSummaryAsync(stableId, cancellationToken);
            if (references.KnownReferenceCount > 0)
            {
                return AuthoringOperationResult<NpcDeleteResponse>.Failure(DeleteBlockedByReference(stableId, references));
            }

            await _repository.DeleteAsync(stableId, request.ExpectedUpdatedAtUtc, cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is not null)
            {
                return ReloadVerificationFailure<NpcDeleteResponse>(stableId);
            }

            var messages = new List<ApiError>();
            AddReferenceDiagnostics(messages, "delete", references);
            return AuthoringOperationResult<NpcDeleteResponse>.Success(
                new NpcDeleteResponse("delete", stableId, messages));
        }
        catch (NpcDefinitionNotFoundException)
        {
            return AuthoringOperationResult<NpcDeleteResponse>.Failure(NpcNotFound(npcDefinitionId));
        }
        catch (NpcDefinitionDeleteRequiresDisabledException)
        {
            return AuthoringOperationResult<NpcDeleteResponse>.Failure(DeleteRequiresDisabledError(npcDefinitionId));
        }
        catch (NpcDefinitionConcurrencyException)
        {
            return VersionConflict<NpcDeleteResponse>(NpcDomainRules.NormalizeStableId(npcDefinitionId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<NpcDeleteResponse>(exception);
        }
    }

    public static NpcDraft Normalize(SaveNpcDraftRequest request) =>
        Normalize(
            request.DisplayName,
            request.VisualTexturePath,
            request.SourceWidth,
            request.SourceHeight,
            request.VisualAnchorOffsetX,
            request.VisualAnchorOffsetY,
            request.VisualRenderScale,
            request.FootprintWidthTiles,
            request.FootprintHeightTiles,
            request.MovementBehavior,
            request.WanderRadiusTiles,
            request.TickIntervalMs,
            request.IdleChance,
            request.InteractionEnabled,
            request.InteractionRangeTiles,
            request.DefaultInteraction,
            request.DefaultDialogueId,
            request.Notes,
            request.ExpectedUpdatedAtUtc,
            request.PreviewSignature);

    public static NpcDraft Normalize(PreviewNpcRequest request) =>
        Normalize(
            request.DisplayName,
            request.VisualTexturePath,
            request.SourceWidth,
            request.SourceHeight,
            request.VisualAnchorOffsetX,
            request.VisualAnchorOffsetY,
            request.VisualRenderScale,
            request.FootprintWidthTiles,
            request.FootprintHeightTiles,
            request.MovementBehavior,
            request.WanderRadiusTiles,
            request.TickIntervalMs,
            request.IdleChance,
            request.InteractionEnabled,
            request.InteractionRangeTiles,
            request.DefaultInteraction,
            request.DefaultDialogueId,
            request.Notes,
            request.ExpectedUpdatedAtUtc,
            null);

    public static NpcDraft Normalize(
        string displayName,
        string visualTexturePath,
        int sourceWidth,
        int sourceHeight,
        double visualAnchorOffsetX,
        double visualAnchorOffsetY,
        double visualRenderScale,
        int footprintWidthTiles,
        int footprintHeightTiles,
        string movementBehavior,
        int wanderRadiusTiles,
        int tickIntervalMs,
        double idleChance,
        bool interactionEnabled,
        int interactionRangeTiles,
        string defaultInteraction,
        string? defaultDialogueId,
        string? notes,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? previewSignature)
    {
        var movement = NpcDomainRules.NormalizeMovementBehavior(movementBehavior);
        var interaction = interactionEnabled;
        return new NpcDraft(
            NpcDomainRules.NormalizeRequired(displayName),
            NpcDomainRules.NormalizeRequired(visualTexturePath),
            sourceWidth,
            sourceHeight,
            visualAnchorOffsetX,
            visualAnchorOffsetY,
            visualRenderScale,
            footprintWidthTiles,
            footprintHeightTiles,
            movement,
            movement == "static" ? 0 : wanderRadiusTiles,
            tickIntervalMs,
            idleChance,
            interaction,
            interactionRangeTiles,
            NpcDomainRules.NormalizeInteractionType(defaultInteraction),
            interaction ? NpcDomainRules.NormalizeOptional(defaultDialogueId) : null,
            NpcDomainRules.NormalizeOptional(notes),
            expectedUpdatedAtUtc,
            previewSignature);
    }

    public static NpcDraft FromRecord(NpcDefinitionRecord record) =>
        Normalize(
            record.DisplayName,
            record.VisualTexturePath,
            record.SourceWidth,
            record.SourceHeight,
            record.VisualAnchorOffsetX,
            record.VisualAnchorOffsetY,
            record.VisualRenderScale,
            record.FootprintWidthTiles,
            record.FootprintHeightTiles,
            record.MovementBehavior,
            record.WanderRadiusTiles,
            record.TickIntervalMs,
            record.IdleChance,
            record.InteractionEnabled,
            record.InteractionRangeTiles,
            record.DefaultInteraction,
            record.DefaultDialogueId,
            record.Notes,
            null,
            null);

    public static string ComputePreviewSignature(
        string npcDefinitionId,
        string operation,
        NpcDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        var payload = JsonSerializer.Serialize(new
        {
            npc_definition_id = npcDefinitionId,
            operation,
            expected_updated_at_utc = expectedUpdatedAtUtc?.ToUniversalTime(),
            draft = ToSignatureDraft(draft)
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool IsMatchingPreview(
        string npcDefinitionId,
        string operation,
        NpcDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? suppliedSignature) =>
        string.Equals(
            suppliedSignature,
            ComputePreviewSignature(npcDefinitionId, operation, draft, expectedUpdatedAtUtc),
            StringComparison.Ordinal);

    public static bool EquivalentDraft(NpcDefinitionRecord record, NpcDraft draft) =>
        string.Equals(record.DisplayName, draft.DisplayName, StringComparison.Ordinal)
        && string.Equals(record.VisualTexturePath, draft.VisualTexturePath, StringComparison.Ordinal)
        && record.SourceWidth == draft.SourceWidth
        && record.SourceHeight == draft.SourceHeight
        && record.VisualAnchorOffsetX.Equals(draft.VisualAnchorOffsetX)
        && record.VisualAnchorOffsetY.Equals(draft.VisualAnchorOffsetY)
        && record.VisualRenderScale.Equals(draft.VisualRenderScale)
        && record.FootprintWidthTiles == draft.FootprintWidthTiles
        && record.FootprintHeightTiles == draft.FootprintHeightTiles
        && record.MovementBehavior == draft.MovementBehavior
        && record.WanderRadiusTiles == draft.WanderRadiusTiles
        && record.TickIntervalMs == draft.TickIntervalMs
        && record.IdleChance.Equals(draft.IdleChance)
        && record.InteractionEnabled == draft.InteractionEnabled
        && record.InteractionRangeTiles == draft.InteractionRangeTiles
        && record.DefaultInteraction == draft.DefaultInteraction
        && string.Equals(record.DefaultDialogueId, draft.DefaultDialogueId, StringComparison.Ordinal)
        && string.Equals(record.Notes, draft.Notes, StringComparison.Ordinal);

    public static bool Equivalent(NpcDefinitionRecord left, NpcDefinitionRecord right) =>
        left.NpcDefinitionId == right.NpcDefinitionId
        && left.DisplayName == right.DisplayName
        && left.PublicationState == right.PublicationState
        && left.VisualTexturePath == right.VisualTexturePath
        && left.SourceWidth == right.SourceWidth
        && left.SourceHeight == right.SourceHeight
        && left.VisualAnchorOffsetX.Equals(right.VisualAnchorOffsetX)
        && left.VisualAnchorOffsetY.Equals(right.VisualAnchorOffsetY)
        && left.VisualRenderScale.Equals(right.VisualRenderScale)
        && left.FootprintWidthTiles == right.FootprintWidthTiles
        && left.FootprintHeightTiles == right.FootprintHeightTiles
        && left.MovementBehavior == right.MovementBehavior
        && left.WanderRadiusTiles == right.WanderRadiusTiles
        && left.TickIntervalMs == right.TickIntervalMs
        && left.IdleChance.Equals(right.IdleChance)
        && left.InteractionEnabled == right.InteractionEnabled
        && left.InteractionRangeTiles == right.InteractionRangeTiles
        && left.DefaultInteraction == right.DefaultInteraction
        && left.DefaultDialogueId == right.DefaultDialogueId
        && left.Notes == right.Notes;

    private async Task<AuthoringOperationResult<NpcMutationResponse>> SetPublicationAsync(
        string npcDefinitionId,
        string publicationState,
        string operation,
        NpcPublicationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var stableId = NpcDomainRules.NormalizeStableId(npcDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<NpcMutationResponse>.Failure(NpcNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(
                    stableId,
                    operation,
                    draft,
                    request.ExpectedUpdatedAtUtc,
                    request.PreviewSignature))
            {
                return AuthoringOperationResult<NpcMutationResponse>.Failure(PreviewMismatch(operation));
            }

            var validation = await _validator.ValidateAsync(
                stableId,
                draft,
                existing,
                operation == "publish",
                cancellationToken);
            var messages = validation.Messages.ToList();
            if (operation == "disable")
            {
                var references = await LoadReferenceSummaryAsync(stableId, cancellationToken);
                AddReferenceDiagnostics(messages, operation, references);
            }

            var valid = operation == "publish"
                ? validation.ValidForPublication
                : validation.ValidForDraft;
            if (!valid || messages.Any(message => message.Severity == ValidationSeverity.Error))
            {
                return AuthoringOperationResult<NpcMutationResponse>.Failure(messages);
            }

            var saved = await _repository.SetPublicationAsync(
                stableId,
                publicationState,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified) || verified.PublicationState != publicationState)
            {
                return ReloadVerificationFailure<NpcMutationResponse>(stableId);
            }

            return AuthoringOperationResult<NpcMutationResponse>.Success(
                new NpcMutationResponse(
                    operation,
                    ToDefinition(verified),
                    messages));
        }
        catch (NpcDefinitionNotFoundException)
        {
            return AuthoringOperationResult<NpcMutationResponse>.Failure(NpcNotFound(npcDefinitionId));
        }
        catch (NpcDefinitionConcurrencyException)
        {
            return VersionConflict<NpcMutationResponse>(NpcDomainRules.NormalizeStableId(npcDefinitionId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<NpcMutationResponse>(exception);
        }
    }

    private NpcDefinition ToDefinition(NpcDefinitionRecord record)
    {
        var asset = _assetService.ResolveGameAssetPng(record.VisualTexturePath, "NPC visual texture");
        return new NpcDefinition(
            record.NpcDefinitionId,
            record.DisplayName,
            record.PublicationState,
            record.VisualTexturePath,
            record.SourceWidth,
            record.SourceHeight,
            record.VisualAnchorOffsetX,
            record.VisualAnchorOffsetY,
            record.VisualRenderScale,
            record.FootprintWidthTiles,
            record.FootprintHeightTiles,
            record.MovementBehavior,
            record.WanderRadiusTiles,
            record.TickIntervalMs,
            record.IdleChance,
            record.InteractionEnabled,
            record.InteractionRangeTiles,
            record.DefaultInteraction,
            record.DefaultDialogueId,
            record.Notes,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            asset.FilePath);
    }

    private static NpcDefinitionSummary ToSummary(NpcDefinitionRecord record) =>
        new(
            record.NpcDefinitionId,
            record.DisplayName,
            record.PublicationState,
            record.VisualTexturePath,
            record.MovementBehavior,
            record.InteractionEnabled,
            record.DefaultDialogueId,
            true,
            record.UpdatedAtUtc);

    private static IReadOnlyList<AuthoringChange> CalculateChanges(
        string npcDefinitionId,
        NpcDefinitionRecord? existing,
        NpcDraft requested,
        string operation)
    {
        var changes = new List<AuthoringChange>();
        AddChange(changes, "npc_definition_id", existing?.NpcDefinitionId, npcDefinitionId);
        AddChange(changes, "display_name", existing?.DisplayName, requested.DisplayName);
        AddChange(changes, "visual_texture_path", existing?.VisualTexturePath, requested.VisualTexturePath);
        AddChange(changes, "source_width", existing?.SourceWidth.ToString(), requested.SourceWidth.ToString());
        AddChange(changes, "source_height", existing?.SourceHeight.ToString(), requested.SourceHeight.ToString());
        AddChange(changes, "visual_anchor_offset_x", existing?.VisualAnchorOffsetX.ToString("R"), requested.VisualAnchorOffsetX.ToString("R"));
        AddChange(changes, "visual_anchor_offset_y", existing?.VisualAnchorOffsetY.ToString("R"), requested.VisualAnchorOffsetY.ToString("R"));
        AddChange(changes, "visual_render_scale", existing?.VisualRenderScale.ToString("R"), requested.VisualRenderScale.ToString("R"));
        AddChange(changes, "footprint_width_tiles", existing?.FootprintWidthTiles.ToString(), requested.FootprintWidthTiles.ToString());
        AddChange(changes, "footprint_height_tiles", existing?.FootprintHeightTiles.ToString(), requested.FootprintHeightTiles.ToString());
        AddChange(changes, "movement_behavior", existing?.MovementBehavior, requested.MovementBehavior);
        AddChange(changes, "wander_radius_tiles", existing?.WanderRadiusTiles.ToString(), requested.WanderRadiusTiles.ToString());
        AddChange(changes, "tick_interval_ms", existing?.TickIntervalMs.ToString(), requested.TickIntervalMs.ToString());
        AddChange(changes, "idle_chance", existing?.IdleChance.ToString("R"), requested.IdleChance.ToString("R"));
        AddChange(changes, "interaction_enabled", existing?.InteractionEnabled.ToString(), requested.InteractionEnabled.ToString());
        AddChange(changes, "interaction_range_tiles", existing?.InteractionRangeTiles.ToString(), requested.InteractionRangeTiles.ToString());
        AddChange(changes, "default_interaction", existing?.DefaultInteraction, requested.DefaultInteraction);
        AddChange(changes, "default_dialogue_id", existing?.DefaultDialogueId, requested.DefaultDialogueId);
        AddChange(changes, "notes", existing?.Notes, requested.Notes);
        var targetState = operation switch
        {
            "publish" => "Published",
            "disable" => "Disabled",
            "delete" => "Deleted",
            _ => "Draft"
        };
        AddChange(changes, "publication_state", existing?.PublicationState, targetState);
        return changes;
    }

    private async Task<NpcReferenceSummaryRecord> LoadReferenceSummaryAsync(
        string npcDefinitionId,
        CancellationToken cancellationToken) =>
        await _repository.LoadKnownSpawnReferencesAsync(npcDefinitionId, cancellationToken);

    private static void AddReferenceDiagnostics(
        ICollection<ApiError> messages,
        string operation,
        NpcReferenceSummaryRecord references)
    {
        if (operation is not ("disable" or "delete"))
        {
            return;
        }

        if (references.KnownReferenceCount > 0)
        {
            messages.Add(operation == "disable"
                ? DisableBlockedByReference(references.NpcDefinitionId, references)
                : DeleteBlockedByReference(references.NpcDefinitionId, references));
            return;
        }

        if (!references.ReferenceCheckComplete)
        {
            messages.Add(new ApiError(
                "npc_reference_check_incomplete",
                "Known database-published NPC spawn references could not be checked because the world-content schema is unavailable; Tiled source validation remains a later hardening concern.",
                ValidationSeverity.Warning,
                "npc_definition_id"));
        }
    }

    private static NpcReferenceSummary ToReferenceSummary(NpcReferenceSummaryRecord record) =>
        new(record.KnownReferenceCount, record.ReferenceSources, record.ReferenceCheckComplete);

    private static void AddChange(
        ICollection<AuthoringChange> changes,
        string field,
        string? before,
        string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new AuthoringChange(field, before, after));
        }
    }

    private static object ToSignatureDraft(NpcDraft draft) => new
    {
        draft.DisplayName,
        draft.VisualTexturePath,
        draft.SourceWidth,
        draft.SourceHeight,
        draft.VisualAnchorOffsetX,
        draft.VisualAnchorOffsetY,
        draft.VisualRenderScale,
        draft.FootprintWidthTiles,
        draft.FootprintHeightTiles,
        draft.MovementBehavior,
        draft.WanderRadiusTiles,
        draft.TickIntervalMs,
        draft.IdleChance,
        draft.InteractionEnabled,
        draft.InteractionRangeTiles,
        draft.DefaultInteraction,
        draft.DefaultDialogueId,
        draft.Notes
    };

    private static string? NormalizePreviewOperation(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "save_draft" or "publish" or "disable" or "delete" ? normalized : null;
    }

    public static bool HasVersionConflict(
        NpcDefinitionRecord? existing,
        DateTimeOffset? expected) =>
        existing is null
            ? expected is not null
            : expected is null
                || existing.UpdatedAtUtc.ToUniversalTime() != expected.Value.ToUniversalTime();

    private static ApiError InvalidTargetOperation() => new(
        "npc_invalid_definition",
        "Target operation must be save_draft, publish, disable, or delete.",
        ValidationSeverity.Error,
        "target_operation");

    private static ApiError DeleteRequiresDisabledError(string npcDefinitionId) => new(
        "npc_delete_requires_disabled",
        $"NPC definition '{NpcDomainRules.NormalizeStableId(npcDefinitionId)}' must be disabled before it can be deleted.",
        ValidationSeverity.Error,
        "publication_state",
        "Disable the NPC definition, preview Delete again, then apply the delete operation.");

    private static ApiError NpcNotFound(string npcDefinitionId) => new(
        "npc_not_found",
        $"NPC definition '{NpcDomainRules.NormalizeStableId(npcDefinitionId)}' does not exist.",
        ValidationSeverity.Error,
        "npc_definition_id");

    private static ApiError DuplicateNpcId(string npcDefinitionId) => new(
        "npc_duplicate_id",
        $"NPC definition '{NpcDomainRules.NormalizeStableId(npcDefinitionId)}' already exists.",
        ValidationSeverity.Error,
        "npc_definition_id");

    private static ApiError PreviewMismatch(string operation) => new(
        "npc_preview_mismatch",
        $"Preview the {operation} operation again before applying it.",
        ValidationSeverity.Error,
        "preview_signature");

    private static ApiError DisableBlockedByReference(
        string npcDefinitionId,
        NpcReferenceSummaryRecord references) => new(
            "npc_disable_blocked_by_reference",
            $"NPC definition '{NpcDomainRules.NormalizeStableId(npcDefinitionId)}' is referenced by {references.KnownReferenceCount} known spawn reference(s).",
            ValidationSeverity.Error,
            "npc_definition_id",
            string.Join("; ", references.ReferenceSources));

    private static ApiError DeleteBlockedByReference(
        string npcDefinitionId,
        NpcReferenceSummaryRecord references) => new(
            "npc_delete_blocked_by_reference",
            $"NPC definition '{NpcDomainRules.NormalizeStableId(npcDefinitionId)}' is referenced by {references.KnownReferenceCount} known spawn reference(s).",
            ValidationSeverity.Error,
            "npc_definition_id",
            string.Join("; ", references.ReferenceSources));

    private static AuthoringOperationResult<T> VersionConflict<T>(string npcDefinitionId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "npc_version_conflict",
            $"NPC definition '{npcDefinitionId}' changed after it was loaded. Reload before applying changes.",
            ValidationSeverity.Error,
            "updated_at_utc"));

    private static AuthoringOperationResult<T> ReloadVerificationFailure<T>(string npcDefinitionId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "npc_reload_verification_failed",
            $"NPC definition '{npcDefinitionId}' did not match after reload verification.",
            ValidationSeverity.Error,
            "npc_definition_id"));

    private AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception)
    {
        _logger.LogWarning(exception, "NPC authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            IsUndefinedTable(exception) ? "npc_schema_unavailable" : "npc_database_unavailable",
            IsUndefinedTable(exception)
                ? "The configured development database is missing the T5 NPC authoring schema."
                : "The configured development database is unavailable.",
            ValidationSeverity.Error,
            Remediation: "Review the Environment tab and apply the NPC authoring migration handoff when T5 runtime integration is approved."));
    }

    private static bool IsUniqueViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation;

    private static bool IsUndefinedTable(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable };

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException
            or InvalidOperationException;
}
