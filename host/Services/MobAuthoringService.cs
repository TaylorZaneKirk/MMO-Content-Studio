using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class MobAuthoringService
{
    private const string MobVisualResourcePrefix = "res://assets/maps/objects/mobs/";

    private readonly IMobRepository _repository;
    private readonly MobDefinitionValidator _validator;
    private readonly MobAuthoringRegistry _registry;
    private readonly ItemAssetService _assetService;
    private readonly ActorAppearanceCatalogService _actorAppearanceCatalogService;
    private readonly RiggedSpritePreviewResolver _riggedSpritePreviewResolver;
    private readonly IRuntimeCatalogPublisher? _runtimeCatalogPublisher;
    private readonly ILogger<MobAuthoringService> _logger;

    public MobAuthoringService(
        IMobRepository repository,
        MobDefinitionValidator validator,
        MobAuthoringRegistry registry,
        ItemAssetService assetService,
        ActorAppearanceCatalogService actorAppearanceCatalogService,
        RiggedSpritePreviewResolver riggedSpritePreviewResolver,
        ILogger<MobAuthoringService> logger,
        IRuntimeCatalogPublisher? runtimeCatalogPublisher = null)
    {
        _repository = repository;
        _validator = validator;
        _registry = registry;
        _assetService = assetService;
        _actorAppearanceCatalogService = actorAppearanceCatalogService;
        _riggedSpritePreviewResolver = riggedSpritePreviewResolver;
        _runtimeCatalogPublisher = runtimeCatalogPublisher;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<MobAuthoringOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var factions = await _repository.LoadFactionsAsync(cancellationToken);
            var dropItems = await _repository.LoadDropItemsAsync(cancellationToken);
            return AuthoringOperationResult<MobAuthoringOptionsResponse>.Success(
                new MobAuthoringOptionsResponse(
                    _registry.LoadPublicationStates(),
                    _registry.LoadAttackTypes(),
                    _registry.LoadAccuracyStyles(),
                    _registry.LoadMovementBehaviors(),
                    _registry.LoadAggressionModes(),
                    _registry.LoadReturnHomeBehaviors(),
                    _registry.LoadFactionDispositions(),
                    _registry.LoadCombatBonusFields(),
                    MobAuthoringRegistry.CombatUnitMilliseconds,
                    _registry.LoadSupportedLimits(),
                    factions.Select(faction => new MobFactionOption(
                        faction.FactionId,
                        faction.DisplayName)).ToArray(),
                    dropItems
                        .Where(item => item.RuntimeEnabled)
                        .Select(item => new MobDropItemOption(
                            item.ItemId,
                            item.DisplayName))
                        .ToArray(),
                    new MobVisualAssetOptions(
                        _assetService.GetGameAssetsRoot() is not null,
                        MobVisualResourcePrefix,
                        _assetService.GetGameAssetsRoot()),
                    _registry.Defaults,
                    _actorAppearanceCatalogService.LoadOptions()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<MobAuthoringOptionsResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<MobCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<MobCatalogResponse>.Success(
                new MobCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<MobCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<MobDefinition>> LoadAsync(
        string mobDefinitionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = MobDomainRules.NormalizeStableId(mobDefinitionId);
            var record = await _repository.LoadAsync(stableId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<MobDefinition>.Failure(MobNotFound(stableId))
                : AuthoringOperationResult<MobDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<MobDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<MobValidationResponse>> PreviewAsync(
        string mobDefinitionId,
        MobPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = MobDomainRules.NormalizeStableId(mobDefinitionId);
            var operation = NormalizePreviewOperation(request.TargetOperation);
            if (operation is null)
            {
                return AuthoringOperationResult<MobValidationResponse>.Failure(InvalidTargetOperation());
            }

            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<MobValidationResponse>(stableId);
            }
            if (existing is null && operation is "publish" or "disable" or "delete")
            {
                return AuthoringOperationResult<MobValidationResponse>.Failure(MobNotFound(stableId));
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
                    "unsaved_mob_changes",
                    "Save the edited mob definition as a draft before changing publication state or deleting it.",
                    ValidationSeverity.Error,
                    "publication_state"));
            }
            if (operation is "disable" or "delete")
            {
                AddDeferredSpawnReferenceWarning(messages);
            }
            if (operation == "delete" && existing!.PublicationState == "Published")
            {
                messages.Add(DeleteRequiresDisabledError(stableId));
            }

            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            var validForDraft = operation == "save_draft"
                ? validation.ValidForDraft && !messages.Any(MobDefinitionValidator.IsDraftBlocking)
                : validation.ValidForDraft && !hasErrors;
            return AuthoringOperationResult<MobValidationResponse>.Success(
                new MobValidationResponse(
                    operation,
                    validForDraft,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(stableId, existing, requested, operation),
                    validation.AssetPreviewFilePath,
                    ComputePreviewSignature(
                        stableId,
                        operation,
                        effective,
                        request.ExpectedUpdatedAtUtc),
                    _riggedSpritePreviewResolver.Resolve(
                        validation.AssetPreviewFilePath ?? string.Empty,
                        effective.SourceWidth,
                        effective.SourceHeight,
                        effective.CompositeVisual,
                        request.PreviewDirection,
                        request.PreviewFrame),
                    CalculateDerivedCombatLevel(effective)));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<MobValidationResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<MobMutationResponse>> SaveDraftAsync(
        string mobDefinitionId,
        SaveMobDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = MobDomainRules.NormalizeStableId(mobDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            var draft = Normalize(request);
            if (!IsMatchingPreview(
                    stableId,
                    "save_draft",
                    draft,
                    request.ExpectedUpdatedAtUtc,
                    request.PreviewSignature))
            {
                return AuthoringOperationResult<MobMutationResponse>.Failure(PreviewMismatch("save_draft"));
            }

            var validation = await _validator.ValidateAsync(
                stableId,
                draft,
                existing,
                false,
                cancellationToken);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<MobMutationResponse>.Failure(validation.Messages);
            }

            var saved = await _repository.SaveDraftAsync(
                stableId,
                draft,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified))
            {
                throw new InvalidOperationException("The saved mob aggregate failed reload-and-verify.");
            }

            return AuthoringOperationResult<MobMutationResponse>.Success(
                new MobMutationResponse(
                    "save_draft",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (MobDefinitionConcurrencyException)
        {
            return VersionConflict<MobMutationResponse>(MobDomainRules.NormalizeStableId(mobDefinitionId));
        }
        catch (PostgresException exception) when (IsUniqueViolation(exception))
        {
            return AuthoringOperationResult<MobMutationResponse>.Failure(DuplicateMobId(mobDefinitionId));
        }
        catch (PostgresException exception) when (IsInvalidReference(exception))
        {
            return AuthoringOperationResult<MobMutationResponse>.Failure(InvalidReference(exception));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<MobMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<MobMutationResponse>> PublishAsync(
        string mobDefinitionId,
        MobPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(mobDefinitionId, "Published", "publish", request, cancellationToken);

    public Task<AuthoringOperationResult<MobMutationResponse>> DisableAsync(
        string mobDefinitionId,
        MobPublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(mobDefinitionId, "Disabled", "disable", request, cancellationToken);

    public async Task<AuthoringOperationResult<DeleteMutationResponse>> DeleteAsync(
        string mobDefinitionId,
        DeleteMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = MobDomainRules.NormalizeStableId(mobDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(MobNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(
                    stableId,
                    "delete",
                    draft,
                    request.ExpectedUpdatedAtUtc,
                    request.PreviewSignature))
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(PreviewMismatch("delete"));
            }
            if (existing.PublicationState == "Published")
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteRequiresDisabledError(stableId));
            }

            await _repository.DeleteAsync(stableId, request.ExpectedUpdatedAtUtc, cancellationToken);
            return AuthoringOperationResult<DeleteMutationResponse>.Success(
                new DeleteMutationResponse("delete", stableId, []));
        }
        catch (MobDefinitionNotFoundException)
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(MobNotFound(mobDefinitionId));
        }
        catch (MobDefinitionConcurrencyException)
        {
            return VersionConflict<DeleteMutationResponse>(MobDomainRules.NormalizeStableId(mobDefinitionId));
        }
        catch (PostgresException exception) when (IsInvalidReference(exception))
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteReferenceError(mobDefinitionId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DeleteMutationResponse>(exception);
        }
    }

    public static NormalizedMobDraft Normalize(SaveMobDraftRequest request) =>
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
            request.MaxHealth,
            request.MovementSpeedTilesPerSecond,
            request.MovementBehavior,
            request.WanderRadiusTiles,
            request.AggressionMode,
            request.AggressionRadiusTiles,
            request.LeashRadiusTiles,
            request.ReturnHomeBehavior,
            request.CombatFactionId,
            request.CanProactivelyTargetHostileMobs,
            request.MobDetectionRadiusTiles,
            request.MobTargetScanIntervalMs,
            request.MobTargetScanCandidateLimit,
            request.PrimaryCombatProfile,
            request.CombatBonuses,
            request.GuaranteedDrops,
            request.VisualMode,
            request.CompositeVisual);

    public static NormalizedMobDraft Normalize(MobPreviewRequest request) =>
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
            request.MaxHealth,
            request.MovementSpeedTilesPerSecond,
            request.MovementBehavior,
            request.WanderRadiusTiles,
            request.AggressionMode,
            request.AggressionRadiusTiles,
            request.LeashRadiusTiles,
            request.ReturnHomeBehavior,
            request.CombatFactionId,
            request.CanProactivelyTargetHostileMobs,
            request.MobDetectionRadiusTiles,
            request.MobTargetScanIntervalMs,
            request.MobTargetScanCandidateLimit,
            request.PrimaryCombatProfile,
            request.CombatBonuses,
            request.GuaranteedDrops,
            request.VisualMode,
            request.CompositeVisual);

    public static NormalizedMobDraft Normalize(
        string displayName,
        string visualTexturePath,
        int sourceWidth,
        int sourceHeight,
        double visualAnchorOffsetX,
        double visualAnchorOffsetY,
        double visualRenderScale,
        int footprintWidthTiles,
        int footprintHeightTiles,
        int maxHealth,
        double movementSpeedTilesPerSecond,
        string movementBehavior,
        int wanderRadiusTiles,
        string aggressionMode,
        int aggressionRadiusTiles,
        int leashRadiusTiles,
        string returnHomeBehavior,
        string? combatFactionId,
        bool canProactivelyTargetHostileMobs,
        int mobDetectionRadiusTiles,
        int mobTargetScanIntervalMs,
        int mobTargetScanCandidateLimit,
        MobCombatProfileDefinition? primaryCombatProfile,
        EquipmentCombatBonusDefinition? combatBonuses,
        IReadOnlyList<MobDropDraft>? guaranteedDrops,
        string? visualMode = ActorVisualModes.FlatSprite,
        RiggedSpriteVisualDescriptor? compositeVisual = null)
    {
        var proactive = canProactivelyTargetHostileMobs;
        var presentation = RiggedSpriteVisualDescriptorNormalizer.Normalize(visualMode, compositeVisual);
        return new NormalizedMobDraft(
            MobDomainRules.NormalizeRequired(displayName),
            MobDomainRules.NormalizeRequired(visualTexturePath),
            sourceWidth,
            sourceHeight,
            visualAnchorOffsetX,
            visualAnchorOffsetY,
            visualRenderScale,
            footprintWidthTiles,
            footprintHeightTiles,
            maxHealth,
            movementSpeedTilesPerSecond,
            MobDomainRules.NormalizeMovementBehavior(movementBehavior),
            MobDomainRules.NormalizeMovementBehavior(movementBehavior) == "static" ? 0 : wanderRadiusTiles,
            MobDomainRules.NormalizeAggressionMode(aggressionMode),
            MobDomainRules.NormalizeAggressionMode(aggressionMode) == "proactive" ? aggressionRadiusTiles : 0,
            leashRadiusTiles,
            MobDomainRules.NormalizeReturnHomeBehavior(returnHomeBehavior),
            MobDomainRules.NormalizeOptional(combatFactionId)?.ToLowerInvariant(),
            proactive,
            proactive ? mobDetectionRadiusTiles : 0,
            proactive ? mobTargetScanIntervalMs : 0,
            proactive ? mobTargetScanCandidateLimit : 0,
            primaryCombatProfile is null
                ? null
                : new MobCombatProfileDefinition(
                    MobDomainRules.NormalizeAttackType(primaryCombatProfile.AttackType),
                    MobDomainRules.NormalizeAccuracyStyle(primaryCombatProfile.AccuracyStyle),
                    primaryCombatProfile.MinimumRangeTiles,
                    primaryCombatProfile.MaximumRangeTiles,
                    primaryCombatProfile.AttackSpeedUnits,
                    primaryCombatProfile.AttackLevel,
                    primaryCombatProfile.StrengthLevel,
                    primaryCombatProfile.DefenceLevel),
            combatBonuses ?? EquipmentCombatBonusDefinition.Zero,
            MobDomainRules.NormalizeGuaranteedDrops(guaranteedDrops),
            presentation.VisualMode,
            presentation.CompositeVisual);
    }

    public static NormalizedMobDraft FromRecord(MobDefinitionRecord record) =>
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
            record.MaxHealth,
            record.MovementSpeedTilesPerSecond,
            record.MovementBehavior,
            record.WanderRadiusTiles,
            record.AggressionMode,
            record.AggressionRadiusTiles,
            record.LeashRadiusTiles,
            record.ReturnHomeBehavior,
            record.CombatFactionId,
            record.CanProactivelyTargetHostileMobs,
            record.MobDetectionRadiusTiles,
            record.MobTargetScanIntervalMs,
            record.MobTargetScanCandidateLimit,
            record.PrimaryCombatProfile,
            record.CombatBonuses,
            record.GuaranteedDrops
                .Select(drop => new MobDropDraft(
                    drop.DropOrder,
                    drop.ItemId,
                    drop.StackCount))
                .ToArray(),
            record.VisualMode,
            record.CompositeVisual);

    public static string ComputePreviewSignature(
        string mobDefinitionId,
        string operation,
        NormalizedMobDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        var payload = JsonSerializer.Serialize(new
        {
            mob_definition_id = mobDefinitionId,
            operation,
            expected_updated_at_utc = expectedUpdatedAtUtc?.ToUniversalTime(),
            draft
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool IsMatchingPreview(
        string mobDefinitionId,
        string operation,
        NormalizedMobDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? suppliedSignature) =>
        string.Equals(
            suppliedSignature,
            ComputePreviewSignature(mobDefinitionId, operation, draft, expectedUpdatedAtUtc),
            StringComparison.Ordinal);

    public static bool EquivalentDraft(MobDefinitionRecord record, NormalizedMobDraft draft) =>
        string.Equals(record.DisplayName, draft.DisplayName, StringComparison.Ordinal)
        && string.Equals(record.VisualTexturePath, draft.VisualTexturePath, StringComparison.Ordinal)
        && record.SourceWidth == draft.SourceWidth
        && record.SourceHeight == draft.SourceHeight
        && record.VisualAnchorOffsetX.Equals(draft.VisualAnchorOffsetX)
        && record.VisualAnchorOffsetY.Equals(draft.VisualAnchorOffsetY)
        && record.VisualRenderScale.Equals(draft.VisualRenderScale)
        && record.FootprintWidthTiles == draft.FootprintWidthTiles
        && record.FootprintHeightTiles == draft.FootprintHeightTiles
        && record.MaxHealth == draft.MaxHealth
        && record.MovementSpeedTilesPerSecond.Equals(draft.MovementSpeedTilesPerSecond)
        && record.MovementBehavior == draft.MovementBehavior
        && record.WanderRadiusTiles == draft.WanderRadiusTiles
        && record.AggressionMode == draft.AggressionMode
        && record.AggressionRadiusTiles == draft.AggressionRadiusTiles
        && record.LeashRadiusTiles == draft.LeashRadiusTiles
        && record.ReturnHomeBehavior == draft.ReturnHomeBehavior
        && string.Equals(record.CombatFactionId, draft.CombatFactionId, StringComparison.Ordinal)
        && record.CanProactivelyTargetHostileMobs == draft.CanProactivelyTargetHostileMobs
        && record.MobDetectionRadiusTiles == draft.MobDetectionRadiusTiles
        && record.MobTargetScanIntervalMs == draft.MobTargetScanIntervalMs
        && record.MobTargetScanCandidateLimit == draft.MobTargetScanCandidateLimit
        && record.PrimaryCombatProfile == draft.PrimaryCombatProfile
        && (record.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero) == draft.CombatBonuses
        && SerializeDrops(record.GuaranteedDrops) == JsonSerializer.Serialize(draft.GuaranteedDrops)
        && record.VisualMode == draft.VisualMode
        && RiggedSpriteVisualDescriptorNormalizer.Equivalent(record.CompositeVisual, draft.CompositeVisual);

    public static bool Equivalent(MobDefinitionRecord left, MobDefinitionRecord right) =>
        left.MobDefinitionId == right.MobDefinitionId
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
        && left.MaxHealth == right.MaxHealth
        && left.MovementSpeedTilesPerSecond.Equals(right.MovementSpeedTilesPerSecond)
        && left.MovementBehavior == right.MovementBehavior
        && left.WanderRadiusTiles == right.WanderRadiusTiles
        && left.AggressionMode == right.AggressionMode
        && left.AggressionRadiusTiles == right.AggressionRadiusTiles
        && left.LeashRadiusTiles == right.LeashRadiusTiles
        && left.ReturnHomeBehavior == right.ReturnHomeBehavior
        && left.CombatFactionId == right.CombatFactionId
        && left.CanProactivelyTargetHostileMobs == right.CanProactivelyTargetHostileMobs
        && left.MobDetectionRadiusTiles == right.MobDetectionRadiusTiles
        && left.MobTargetScanIntervalMs == right.MobTargetScanIntervalMs
        && left.MobTargetScanCandidateLimit == right.MobTargetScanCandidateLimit
        && left.PrimaryCombatProfile == right.PrimaryCombatProfile
        && (left.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero) == (right.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero)
        && SerializeDrops(left.GuaranteedDrops) == SerializeDrops(right.GuaranteedDrops)
        && left.VisualMode == right.VisualMode
        && RiggedSpriteVisualDescriptorNormalizer.Equivalent(left.CompositeVisual, right.CompositeVisual);

    private async Task<AuthoringOperationResult<MobMutationResponse>> SetPublicationAsync(
        string mobDefinitionId,
        string publicationState,
        string operation,
        MobPublicationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var stableId = MobDomainRules.NormalizeStableId(mobDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<MobMutationResponse>.Failure(MobNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(
                    stableId,
                    operation,
                    draft,
                    request.ExpectedUpdatedAtUtc,
                    request.PreviewSignature))
            {
                return AuthoringOperationResult<MobMutationResponse>.Failure(PreviewMismatch(operation));
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
                AddDeferredSpawnReferenceWarning(messages);
            }

            var valid = operation == "publish"
                ? validation.ValidForPublication
                : validation.ValidForDraft;
            if (!valid || messages.Any(message => message.Severity == ValidationSeverity.Error))
            {
                return AuthoringOperationResult<MobMutationResponse>.Failure(messages);
            }

            var saved = await _repository.SetPublicationAsync(
                stableId,
                publicationState,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified) || verified.PublicationState != publicationState)
            {
                throw new InvalidOperationException("The mob publication change failed reload-and-verify.");
            }

            if (operation == "publish" && _runtimeCatalogPublisher is not null)
            {
                messages.AddRange(await _runtimeCatalogPublisher.PublishCatalogsAsync(
                    RuntimeCatalogPublicationScope.Mob,
                    cancellationToken));
            }

            return AuthoringOperationResult<MobMutationResponse>.Success(
                new MobMutationResponse(
                    operation,
                    ToDefinition(verified),
                    messages));
        }
        catch (MobDefinitionNotFoundException)
        {
            return AuthoringOperationResult<MobMutationResponse>.Failure(MobNotFound(mobDefinitionId));
        }
        catch (MobDefinitionConcurrencyException)
        {
            return VersionConflict<MobMutationResponse>(MobDomainRules.NormalizeStableId(mobDefinitionId));
        }
        catch (PostgresException exception) when (IsInvalidReference(exception))
        {
            return AuthoringOperationResult<MobMutationResponse>.Failure(InvalidReference(exception));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<MobMutationResponse>(exception);
        }
    }

    private MobDefinition ToDefinition(MobDefinitionRecord record)
    {
        var asset = _assetService.ResolveGameAssetPng(record.VisualTexturePath, "mob visual texture");
        return new MobDefinition(
            record.MobDefinitionId,
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
            record.MaxHealth,
            record.MovementSpeedTilesPerSecond,
            record.MovementBehavior,
            record.WanderRadiusTiles,
            record.AggressionMode,
            record.AggressionRadiusTiles,
            record.LeashRadiusTiles,
            record.ReturnHomeBehavior,
            record.CombatFactionId,
            record.CombatFactionDisplayName,
            record.CanProactivelyTargetHostileMobs,
            record.MobDetectionRadiusTiles,
            record.MobTargetScanIntervalMs,
            record.MobTargetScanCandidateLimit,
            record.PrimaryCombatProfile,
            record.CombatBonuses,
            record.GuaranteedDrops,
            record.UpdatedAtUtc,
            asset.FilePath,
            record.VisualMode,
            record.CompositeVisual,
            ResolvePersistedRiggedSpritePreview(record, asset),
            CalculateDerivedCombatLevel(FromRecord(record)));
    }

    private RiggedSpritePreviewDefinition? ResolvePersistedRiggedSpritePreview(
        MobDefinitionRecord record,
        ItemAssetResolution asset) =>
        record.VisualMode == ActorVisualModes.CompositeRig
        && record.CompositeVisual is not null
        && asset.Exists
        && asset.FilePath is not null
            ? _riggedSpritePreviewResolver.Resolve(
                asset.FilePath,
                record.SourceWidth,
                record.SourceHeight,
                record.CompositeVisual,
                null,
                null)
            : null;

    private static MobDefinitionSummary ToSummary(MobDefinitionRecord record) =>
        new(
            record.MobDefinitionId,
            record.DisplayName,
            record.PublicationState,
            record.VisualTexturePath,
            record.MaxHealth,
            record.MovementBehavior,
            record.AggressionMode,
            record.CombatFactionId,
            record.CombatFactionDisplayName,
            record.PrimaryCombatProfile is not null || record.HasCombatProfile,
            record.GuaranteedDropCount,
            true,
            record.UpdatedAtUtc,
            record.VisualMode,
            record.CompositeVisual,
            CalculateDerivedCombatLevel(FromRecord(record)));

    private static int? CalculateDerivedCombatLevel(NormalizedMobDraft draft)
    {
        return draft.PrimaryCombatProfile is null
            ? null
            : MobDomainRules.CalculateDerivedCombatLevel(
                draft.PrimaryCombatProfile.AttackLevel,
                draft.PrimaryCombatProfile.StrengthLevel,
                draft.PrimaryCombatProfile.DefenceLevel,
                draft.MaxHealth);
    }

    private static IReadOnlyList<AuthoringChange> CalculateChanges(
        string mobDefinitionId,
        MobDefinitionRecord? existing,
        NormalizedMobDraft requested,
        string operation)
    {
        var changes = new List<AuthoringChange>();
        AddChange(changes, "mob_definition_id", existing?.MobDefinitionId, mobDefinitionId);
        AddChange(changes, "display_name", existing?.DisplayName, requested.DisplayName);
        AddChange(changes, "visual_texture_path", existing?.VisualTexturePath, requested.VisualTexturePath);
        AddChange(changes, "visual_mode", existing?.VisualMode, requested.VisualMode);
        AddChange(changes, "composite_visual", SerializeCompositeVisual(existing?.CompositeVisual), SerializeCompositeVisual(requested.CompositeVisual));
        AddChange(changes, "source_width", existing?.SourceWidth.ToString(), requested.SourceWidth.ToString());
        AddChange(changes, "source_height", existing?.SourceHeight.ToString(), requested.SourceHeight.ToString());
        AddChange(changes, "visual_anchor_offset_x", existing?.VisualAnchorOffsetX.ToString("R"), requested.VisualAnchorOffsetX.ToString("R"));
        AddChange(changes, "visual_anchor_offset_y", existing?.VisualAnchorOffsetY.ToString("R"), requested.VisualAnchorOffsetY.ToString("R"));
        AddChange(changes, "visual_render_scale", existing?.VisualRenderScale.ToString("R"), requested.VisualRenderScale.ToString("R"));
        AddChange(changes, "footprint_width_tiles", existing?.FootprintWidthTiles.ToString(), requested.FootprintWidthTiles.ToString());
        AddChange(changes, "footprint_height_tiles", existing?.FootprintHeightTiles.ToString(), requested.FootprintHeightTiles.ToString());
        AddChange(changes, "max_health", existing?.MaxHealth.ToString(), requested.MaxHealth.ToString());
        AddChange(changes, "movement_speed_tiles_per_second", existing?.MovementSpeedTilesPerSecond.ToString("R"), requested.MovementSpeedTilesPerSecond.ToString("R"));
        AddChange(changes, "movement_behavior", existing?.MovementBehavior, requested.MovementBehavior);
        AddChange(changes, "wander_radius_tiles", existing?.WanderRadiusTiles.ToString(), requested.WanderRadiusTiles.ToString());
        AddChange(changes, "aggression_mode", existing?.AggressionMode, requested.AggressionMode);
        AddChange(changes, "aggression_radius_tiles", existing?.AggressionRadiusTiles.ToString(), requested.AggressionRadiusTiles.ToString());
        AddChange(changes, "leash_radius_tiles", existing?.LeashRadiusTiles.ToString(), requested.LeashRadiusTiles.ToString());
        AddChange(changes, "return_home_behavior", existing?.ReturnHomeBehavior, requested.ReturnHomeBehavior);
        AddChange(changes, "combat_faction_id", existing?.CombatFactionId, requested.CombatFactionId);
        AddChange(changes, "can_proactively_target_hostile_mobs", existing?.CanProactivelyTargetHostileMobs.ToString(), requested.CanProactivelyTargetHostileMobs.ToString());
        AddChange(changes, "mob_detection_radius_tiles", existing?.MobDetectionRadiusTiles.ToString(), requested.MobDetectionRadiusTiles.ToString());
        AddChange(changes, "mob_target_scan_interval_ms", existing?.MobTargetScanIntervalMs.ToString(), requested.MobTargetScanIntervalMs.ToString());
        AddChange(changes, "mob_target_scan_candidate_limit", existing?.MobTargetScanCandidateLimit.ToString(), requested.MobTargetScanCandidateLimit.ToString());
        AddChange(changes, "primary_combat_profile", JsonSerializer.Serialize(existing?.PrimaryCombatProfile), JsonSerializer.Serialize(requested.PrimaryCombatProfile));
        AddChange(changes, "combat_bonuses", JsonSerializer.Serialize(existing?.CombatBonuses ?? EquipmentCombatBonusDefinition.Zero), JsonSerializer.Serialize(requested.CombatBonuses));
        AddChange(changes, "guaranteed_drops", SerializeDrops(existing?.GuaranteedDrops ?? []), JsonSerializer.Serialize(requested.GuaranteedDrops));
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

    private static string SerializeDrops(IEnumerable<MobDropDefinition> drops) =>
        JsonSerializer.Serialize(drops.Select(drop => new MobDropDraft(
            drop.DropOrder,
            drop.ItemId,
            drop.StackCount)));

    private static string? SerializeCompositeVisual(RiggedSpriteVisualDescriptor? descriptor) =>
        descriptor is null ? null : JsonSerializer.Serialize(descriptor);

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

    private static string? NormalizePreviewOperation(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "save_draft" or "publish" or "disable" or "delete" ? normalized : null;
    }

    public static bool HasVersionConflict(
        MobDefinitionRecord? existing,
        DateTimeOffset? expected) =>
        existing is null
            ? expected is not null
            : expected is null
                || existing.UpdatedAtUtc.ToUniversalTime() != expected.Value.ToUniversalTime();

    private static void AddDeferredSpawnReferenceWarning(ICollection<ApiError> messages)
    {
        messages.Add(new ApiError(
            "mob_spawn_reference_guard_deferred",
            "Generated and published EnemySpawn reference checks are deferred to the T4E/runtime-integration slice.",
            ValidationSeverity.Warning,
            "publication_state"));
    }

    private static ApiError InvalidTargetOperation() => new(
        "invalid_target_operation",
        "Target operation must be save_draft, publish, disable, or delete.",
        ValidationSeverity.Error,
        "target_operation");

    private static ApiError DeleteRequiresDisabledError(string mobDefinitionId) => new(
        "delete_requires_disabled_mob",
        $"Mob definition '{mobDefinitionId}' must be disabled before it can be deleted.",
        ValidationSeverity.Error,
        "publication_state",
        "Disable the mob definition, preview Delete again, then apply the delete operation.");

    private static ApiError MobNotFound(string mobDefinitionId) => new(
        "mob_not_found",
        $"Mob definition '{mobDefinitionId}' does not exist.",
        ValidationSeverity.Error,
        "mob_definition_id");

    private static ApiError DuplicateMobId(string mobDefinitionId) => new(
        "duplicate_mob_definition_id",
        $"Mob definition '{MobDomainRules.NormalizeStableId(mobDefinitionId)}' already exists.",
        ValidationSeverity.Error,
        "mob_definition_id");

    private static ApiError PreviewMismatch(string operation) => new(
        "preview_signature_mismatch",
        $"Preview the {operation} operation again before applying it.",
        ValidationSeverity.Error,
        "preview_signature");

    private static ApiError InvalidReference(PostgresException exception) => new(
        "invalid_mob_reference",
        "The mob definition references a faction or drop item that does not exist.",
        ValidationSeverity.Error,
        exception.ConstraintName?.Contains("faction", StringComparison.OrdinalIgnoreCase) == true
            ? "combat_faction_id"
            : "guaranteed_drops");

    private static ApiError DeleteReferenceError(string mobDefinitionId) => new(
        "mob_delete_blocked_by_references",
        $"Mob definition '{MobDomainRules.NormalizeStableId(mobDefinitionId)}' cannot be deleted while another table references it.",
        ValidationSeverity.Error,
        "mob_definition_id",
        "Remove generated, spawn, or other content references before deleting.");

    private static AuthoringOperationResult<T> VersionConflict<T>(string mobDefinitionId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "mob_version_conflict",
            $"Mob definition '{mobDefinitionId}' changed after it was loaded. Reload before applying changes.",
            ValidationSeverity.Error,
            "updated_at_utc"));

    private AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception)
    {
        _logger.LogWarning(exception, "Mob authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            "database_unavailable",
            "The configured development database is unavailable or missing the T4 mob-authoring schema.",
            ValidationSeverity.Error,
            Remediation: "Review the Environment tab and apply the MMO Project mob-authoring migration handoff when runtime integration is approved."));
    }

    private static bool IsUniqueViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation;

    private static bool IsInvalidReference(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.ForeignKeyViolation;

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException
            or InvalidOperationException;
}
