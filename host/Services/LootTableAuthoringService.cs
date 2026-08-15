using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class LootTableAuthoringService
{
    private readonly ILootTableRepository _repository;
    private readonly LootTableDefinitionValidator _validator;
    private readonly LootTableExpectedValueCalculator _expectedValueCalculator;
    private readonly ILogger<LootTableAuthoringService> _logger;

    public LootTableAuthoringService(
        ILootTableRepository repository,
        LootTableDefinitionValidator validator,
        LootTableExpectedValueCalculator expectedValueCalculator,
        ILogger<LootTableAuthoringService> logger)
    {
        _repository = repository;
        _validator = validator;
        _expectedValueCalculator = expectedValueCalculator;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<LootTableAuthoringOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await _repository.LoadItemsAsync(cancellationToken);
            var tables = await _repository.LoadTableOptionsAsync(cancellationToken);
            return AuthoringOperationResult<LootTableAuthoringOptionsResponse>.Success(
                new LootTableAuthoringOptionsResponse(
                    [
                        new(LootTableDomainRules.Draft, "Draft"),
                        new(LootTableDomainRules.Published, "Published"),
                        new(LootTableDomainRules.Disabled, "Disabled")
                    ],
                    [
                        new(LootTableDomainRules.SectionGuaranteed, "Guaranteed"),
                        new(LootTableDomainRules.SectionPreRoll, "Pre-roll"),
                        new(LootTableDomainRules.SectionMain, "Main"),
                        new(LootTableDomainRules.SectionTertiary, "Tertiary")
                    ],
                    [
                        new(LootTableDomainRules.RollGuaranteedAll, "Guaranteed All"),
                        new(LootTableDomainRules.RollWeightedOne, "Weighted One"),
                        new(LootTableDomainRules.RollIndependent, "Independent")
                    ],
                    [
                        new(LootTableDomainRules.OutcomeItem, "Item"),
                        new(LootTableDomainRules.OutcomeLootTable, "Loot Table"),
                        new(LootTableDomainRules.OutcomeNoDrop, "No Drop")
                    ],
                    [
                        new(LootTableDomainRules.FailureContinue, "Continue"),
                        new(LootTableDomainRules.FailureFallthroughToMain, "Fall Through To Main"),
                        new(LootTableDomainRules.FailureStop, "Stop")
                    ],
                    [
                        new(LootTableDomainRules.SuccessSequenceContinue, "Continue"),
                        new(LootTableDomainRules.SuccessSequenceStop, "Stop")
                    ],
                    [
                        new(LootTableDomainRules.SuccessMainKeep, "Keep Main"),
                        new(LootTableDomainRules.SuccessMainSuppress, "Suppress Main")
                    ],
                    items
                        .Select(item => new LootItemOption(
                            item.ItemId,
                            item.DisplayName,
                            item.RuntimeEnabled,
                            item.ReferenceValue))
                        .ToArray(),
                    tables
                        .Select(table => new LootTableOption(
                            table.LootTableId,
                            table.DisplayName,
                            table.PublicationState))
                        .ToArray(),
                    new LootTableSupportedLimits(
                        LootTableDomainRules.MaxNestingDepth,
                        LootTableDomainRules.MaxBoundedExpansion)));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<LootTableAuthoringOptionsResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<LootTableCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<LootTableCatalogResponse>.Success(
                new LootTableCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<LootTableCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<LootTableDefinition>> LoadAsync(
        string lootTableId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = LootTableDomainRules.NormalizeStableId(lootTableId);
            var record = await _repository.LoadAsync(stableId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<LootTableDefinition>.Failure(LootTableNotFound(stableId))
                : AuthoringOperationResult<LootTableDefinition>.Success(
                    await ToDefinitionAsync(record, [], cancellationToken));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<LootTableDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<LootTableValidationResponse>> PreviewAsync(
        string lootTableId,
        LootTablePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = LootTableDomainRules.NormalizeStableId(lootTableId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<LootTableValidationResponse>(stableId);
            }

            var operation = NormalizePreviewOperation(request.TargetOperation);
            if (operation is null)
            {
                return AuthoringOperationResult<LootTableValidationResponse>.Failure(InvalidTargetOperation());
            }

            if (operation is "publish" or "disable" or "delete" && existing is null)
            {
                return AuthoringOperationResult<LootTableValidationResponse>.Failure(LootTableNotFound(stableId));
            }

            var requested = Normalize(request);
            var hasUnsavedOperationChanges =
                existing is not null
                && operation is "publish" or "disable" or "delete"
                && !EquivalentDraft(existing, requested);
            var effective = operation == "save_draft" || hasUnsavedOperationChanges
                ? requested
                : FromRecord(existing!);
            var validation = await _validator.ValidateAsync(
                stableId,
                effective,
                existing,
                hasUnsavedOperationChanges ? "save_draft" : operation,
                cancellationToken);
            var messages = validation.Messages.ToList();
            if (hasUnsavedOperationChanges)
            {
                messages.Add(new ApiError(
                    "unsaved_loot_table_changes",
                    "Save the edited loot table as a draft before changing publication state or deleting it.",
                    ValidationSeverity.Error,
                    "publication_state"));
            }
            AddLifecycleErrors(stableId, existing, operation, messages);
            var allTables = await LoadAllTablesAsync(stableId, existing, effective, cancellationToken);
            var ev = _expectedValueCalculator.Calculate(
                stableId,
                allTables,
                await _repository.LoadItemsAsync(cancellationToken),
                messages);
            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            return AuthoringOperationResult<LootTableValidationResponse>.Success(
                new LootTableValidationResponse(
                    operation,
                    validation.ValidForDraft && !hasErrors,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(stableId, existing, requested, operation),
                    ComputePreviewSignature(stableId, operation, effective, request.ExpectedUpdatedAtUtc),
                    ev));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<LootTableValidationResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<LootTableMutationResponse>> SaveDraftAsync(
        string lootTableId,
        SaveLootTableDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = LootTableDomainRules.NormalizeStableId(lootTableId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            var draft = Normalize(request);
            if (!IsMatchingPreview(stableId, "save_draft", draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<LootTableMutationResponse>.Failure(PreviewMismatch("save_draft"));
            }

            var validation = await _validator.ValidateAsync(stableId, draft, existing, "save_draft", cancellationToken);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<LootTableMutationResponse>.Failure(validation.Messages);
            }

            var saved = await _repository.SaveDraftAsync(
                stableId,
                draft,
                ComputeContentFingerprint(stableId, draft),
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified))
            {
                throw new InvalidOperationException("The saved loot table aggregate failed reload-and-verify.");
            }

            return AuthoringOperationResult<LootTableMutationResponse>.Success(
                new LootTableMutationResponse(
                    "save_draft",
                    await ToDefinitionAsync(verified, validation.Messages, cancellationToken),
                    validation.Messages));
        }
        catch (LootTableConcurrencyException)
        {
            return VersionConflict<LootTableMutationResponse>(LootTableDomainRules.NormalizeStableId(lootTableId));
        }
        catch (PostgresException exception) when (IsInvalidReference(exception))
        {
            return AuthoringOperationResult<LootTableMutationResponse>.Failure(InvalidReference(exception));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<LootTableMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<LootTableMutationResponse>> PublishAsync(
        string lootTableId,
        LootTablePublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(
            lootTableId,
            LootTableDomainRules.Published,
            "publish",
            request,
            cancellationToken);

    public Task<AuthoringOperationResult<LootTableMutationResponse>> DisableAsync(
        string lootTableId,
        LootTablePublicationRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(
            lootTableId,
            LootTableDomainRules.Disabled,
            "disable",
            request,
            cancellationToken);

    public async Task<AuthoringOperationResult<DeleteMutationResponse>> DeleteAsync(
        string lootTableId,
        DeleteMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = LootTableDomainRules.NormalizeStableId(lootTableId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(LootTableNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(stableId, "delete", draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(PreviewMismatch("delete"));
            }

            if (existing.PublicationState != LootTableDomainRules.Disabled)
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(DeleteRequiresDisabledError(stableId));
            }

            var validation = await _validator.ValidateAsync(stableId, draft, existing, "delete", cancellationToken);
            if (validation.Messages.Any(message => message.Severity == ValidationSeverity.Error))
            {
                return AuthoringOperationResult<DeleteMutationResponse>.Failure(validation.Messages);
            }

            await _repository.DeleteAsync(stableId, request.ExpectedUpdatedAtUtc, cancellationToken);
            return AuthoringOperationResult<DeleteMutationResponse>.Success(new DeleteMutationResponse(
                "delete",
                stableId,
                []));
        }
        catch (LootTableNotFoundException)
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(
                LootTableNotFound(LootTableDomainRules.NormalizeStableId(lootTableId)));
        }
        catch (LootTableConcurrencyException)
        {
            return VersionConflict<DeleteMutationResponse>(LootTableDomainRules.NormalizeStableId(lootTableId));
        }
        catch (PostgresException exception) when (IsInvalidReference(exception))
        {
            return AuthoringOperationResult<DeleteMutationResponse>.Failure(InvalidReference(exception));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DeleteMutationResponse>(exception);
        }
    }

    public static NormalizedLootTableDraft Normalize(SaveLootTableDraftRequest request) =>
        LootTableDomainRules.Normalize(request.DisplayName, request.Description, request.Groups);

    public static NormalizedLootTableDraft Normalize(LootTablePreviewRequest request) =>
        LootTableDomainRules.Normalize(request.DisplayName, request.Description, request.Groups);

    public static NormalizedLootTableDraft FromRecord(LootTableRecord record) =>
        LootTableDomainRules.Normalize(
            record.DisplayName,
            record.Description,
            record.Groups
                .Select(group => new LootRollGroupDraft(
                    group.RollGroupId,
                    group.Order,
                    group.SectionKind,
                    group.RollKind,
                    group.RollCount,
                    group.PreRollFailureBehavior,
                    group.PreRollSuccessSequenceBehavior,
                    group.PreRollSuccessMainBehavior,
                    group.DisplayName,
                    group.Outcomes
                        .Select(outcome => new LootOutcomeDraft(
                            outcome.OutcomeId,
                            outcome.Order,
                            outcome.OutcomeKind,
                            outcome.ItemId,
                            outcome.NestedLootTableId,
                            outcome.MinQuantity,
                            outcome.MaxQuantity,
                            outcome.Weight,
                            outcome.ProbabilityNumerator,
                            outcome.ProbabilityDenominator))
                        .ToArray()))
                .ToArray());

    public static string ComputePreviewSignature(
        string lootTableId,
        string operation,
        NormalizedLootTableDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        var payload = JsonSerializer.Serialize(new
        {
            loot_table_id = lootTableId,
            operation,
            draft,
            expected_updated_at_utc = expectedUpdatedAtUtc?.ToUniversalTime()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool IsMatchingPreview(
        string lootTableId,
        string operation,
        NormalizedLootTableDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? previewSignature) =>
        !string.IsNullOrWhiteSpace(previewSignature)
        && string.Equals(
            previewSignature,
            ComputePreviewSignature(lootTableId, operation, draft, expectedUpdatedAtUtc),
            StringComparison.Ordinal);

    public static string ComputeContentFingerprint(string lootTableId, NormalizedLootTableDraft draft)
    {
        var payload = JsonSerializer.Serialize(new
        {
            loot_table_id = lootTableId,
            draft
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private async Task<AuthoringOperationResult<LootTableMutationResponse>> SetPublicationAsync(
        string lootTableId,
        string publicationState,
        string operation,
        LootTablePublicationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var stableId = LootTableDomainRules.NormalizeStableId(lootTableId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<LootTableMutationResponse>.Failure(LootTableNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(stableId, operation, draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<LootTableMutationResponse>.Failure(PreviewMismatch(operation));
            }

            var validation = await _validator.ValidateAsync(
                stableId,
                draft,
                existing,
                operation,
                cancellationToken);
            if (validation.Messages.Any(message => message.Severity == ValidationSeverity.Error))
            {
                return AuthoringOperationResult<LootTableMutationResponse>.Failure(validation.Messages);
            }

            var saved = await _repository.SetPublicationAsync(
                stableId,
                publicationState,
                ComputeContentFingerprint(stableId, draft),
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified) || verified.PublicationState != publicationState)
            {
                throw new InvalidOperationException("The loot table publication mutation failed reload-and-verify.");
            }

            return AuthoringOperationResult<LootTableMutationResponse>.Success(
                new LootTableMutationResponse(
                    operation,
                    await ToDefinitionAsync(verified, validation.Messages, cancellationToken),
                    validation.Messages));
        }
        catch (LootTableNotFoundException)
        {
            return AuthoringOperationResult<LootTableMutationResponse>.Failure(
                LootTableNotFound(LootTableDomainRules.NormalizeStableId(lootTableId)));
        }
        catch (LootTableConcurrencyException)
        {
            return VersionConflict<LootTableMutationResponse>(LootTableDomainRules.NormalizeStableId(lootTableId));
        }
        catch (PostgresException exception) when (IsInvalidReference(exception))
        {
            return AuthoringOperationResult<LootTableMutationResponse>.Failure(InvalidReference(exception));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<LootTableMutationResponse>(exception);
        }
    }

    private async Task<LootTableDefinition> ToDefinitionAsync(
        LootTableRecord record,
        IReadOnlyList<ApiError> diagnostics,
        CancellationToken cancellationToken)
    {
        var tables = await LoadAllTablesAsync(record.LootTableId, record, FromRecord(record), cancellationToken);
        var ev = _expectedValueCalculator.Calculate(
            record.LootTableId,
            tables,
            await _repository.LoadItemsAsync(cancellationToken),
            diagnostics);
        return ToDefinition(record, ev);
    }

    private static LootTableDefinition ToDefinition(
        LootTableRecord record,
        LootExpectedValueReport expectedValue) =>
        new(
            record.LootTableId,
            record.DisplayName,
            record.Description,
            record.PublicationState,
            record.ContentFingerprint,
            record.Groups
                .Select(group => new LootRollGroupDefinition(
                    group.RollGroupId,
                    group.Order,
                    group.SectionKind,
                    group.RollKind,
                    group.RollCount,
                    group.PreRollFailureBehavior,
                    group.PreRollSuccessSequenceBehavior,
                    group.PreRollSuccessMainBehavior,
                    group.DisplayName,
                    group.Outcomes
                        .Select(outcome => new LootOutcomeDefinition(
                            outcome.OutcomeId,
                            outcome.Order,
                            outcome.OutcomeKind,
                            outcome.ItemId,
                            outcome.ItemDisplayName,
                            outcome.NestedLootTableId,
                            outcome.MinQuantity,
                            outcome.MaxQuantity,
                            outcome.Weight,
                            outcome.ProbabilityNumerator,
                            outcome.ProbabilityDenominator))
                        .ToArray()))
                .ToArray(),
            expectedValue,
            record.UpdatedAtUtc);

    private static LootTableDefinition ToDefinition(
        string lootTableId,
        NormalizedLootTableDraft draft,
        string publicationState,
        DateTimeOffset updatedAtUtc,
        LootExpectedValueReport expectedValue) =>
        ToDefinition(
            ToTransientRecord(lootTableId, draft, publicationState, updatedAtUtc),
            expectedValue);

    private static LootTableRecord ToTransientRecord(
        string lootTableId,
        NormalizedLootTableDraft draft,
        string publicationState,
        DateTimeOffset updatedAtUtc) =>
        new(
            lootTableId,
            draft.DisplayName,
            draft.Description,
            publicationState,
            ComputeContentFingerprint(lootTableId, draft),
            draft.Groups
                .Select(group => new LootRollGroupRecord(
                    group.RollGroupId,
                    group.Order,
                    group.SectionKind,
                    group.RollKind,
                    group.RollCount,
                    group.PreRollFailureBehavior,
                    group.PreRollSuccessSequenceBehavior,
                    group.PreRollSuccessMainBehavior,
                    group.DisplayName,
                    group.Outcomes
                        .Select(outcome => new LootOutcomeRecord(
                            outcome.OutcomeId,
                            outcome.Order,
                            outcome.OutcomeKind,
                            outcome.ItemId,
                            null,
                            outcome.NestedLootTableId,
                            outcome.MinQuantity,
                            outcome.MaxQuantity,
                            outcome.Weight,
                            outcome.ProbabilityNumerator,
                            outcome.ProbabilityDenominator))
                        .ToArray()))
                .ToArray(),
            draft.Groups.Count,
            draft.Groups.Sum(group => group.Outcomes.Count),
            updatedAtUtc);

    private static LootTableSummary ToSummary(LootTableRecord record) =>
        new(
            record.LootTableId,
            record.DisplayName,
            record.PublicationState,
            record.GroupCount,
            record.OutcomeCount,
            LootTableDomainRules.ToContract(ExactRational.Zero),
            record.UpdatedAtUtc);

    private async Task<IReadOnlyList<LootTableRecord>> LoadAllTablesAsync(
        string lootTableId,
        LootTableRecord? existing,
        NormalizedLootTableDraft effective,
        CancellationToken cancellationToken)
    {
        var records = await _repository.ListAsync(null, cancellationToken);
        var aggregateRecords = new List<LootTableRecord>(records.Count + 1);
        foreach (var record in records)
        {
            var loaded = await _repository.LoadAsync(record.LootTableId, cancellationToken);
            if (loaded is not null && loaded.LootTableId != lootTableId)
            {
                aggregateRecords.Add(loaded);
            }
        }

        aggregateRecords.Add(ToTransientRecord(
            lootTableId,
            effective,
            existing?.PublicationState ?? LootTableDomainRules.Draft,
            existing?.UpdatedAtUtc ?? DateTimeOffset.UtcNow));
        return aggregateRecords;
    }

    private static IReadOnlyList<AuthoringChange> CalculateChanges(
        string lootTableId,
        LootTableRecord? existing,
        NormalizedLootTableDraft requested,
        string operation)
    {
        var current = existing is null ? null : FromRecord(existing);
        var changes = new List<AuthoringChange>();
        AddChange(changes, "loot_table_id", existing?.LootTableId, lootTableId);
        AddChange(changes, "display_name", current?.DisplayName, requested.DisplayName);
        AddChange(changes, "description", current?.Description, requested.Description);
        AddChange(changes, "groups", current is null ? null : JsonSerializer.Serialize(current.Groups), JsonSerializer.Serialize(requested.Groups));
        var targetState = operation switch
        {
            "publish" => LootTableDomainRules.Published,
            "disable" => LootTableDomainRules.Disabled,
            "delete" => "Deleted",
            _ => LootTableDomainRules.Draft
        };
        AddChange(changes, "publication_state", existing?.PublicationState, targetState);
        return changes;
    }

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

    private static bool EquivalentDraft(LootTableRecord record, NormalizedLootTableDraft draft) =>
        JsonSerializer.Serialize(FromRecord(record)) == JsonSerializer.Serialize(draft);

    private static bool Equivalent(LootTableRecord left, LootTableRecord right) =>
        left.LootTableId == right.LootTableId
        && left.DisplayName == right.DisplayName
        && left.Description == right.Description
        && left.PublicationState == right.PublicationState
        && left.ContentFingerprint == right.ContentFingerprint
        && JsonSerializer.Serialize(left.Groups) == JsonSerializer.Serialize(right.Groups);

    private static void AddLifecycleErrors(
        string lootTableId,
        LootTableRecord? existing,
        string operation,
        ICollection<ApiError> messages)
    {
        if (operation == "delete" && existing?.PublicationState != LootTableDomainRules.Disabled)
        {
            messages.Add(DeleteRequiresDisabledError(lootTableId));
        }
    }

    private static bool HasVersionConflict(
        LootTableRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc) =>
        existing is not null
        && (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime());

    private static string? NormalizePreviewOperation(string? operation)
    {
        var normalized = (operation ?? string.Empty).Trim();
        return normalized is "save_draft" or "publish" or "disable" or "delete"
            ? normalized
            : null;
    }

    private static ApiError LootTableNotFound(string lootTableId) => new(
        "loot_table_not_found",
        $"Loot table '{lootTableId}' does not exist.",
        ValidationSeverity.Error,
        "loot_table_id");

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

    private static ApiError DeleteRequiresDisabledError(string lootTableId) => new(
        "delete_requires_disabled_loot_table",
        $"Loot table '{lootTableId}' must be disabled before it can be deleted.",
        ValidationSeverity.Error,
        "publication_state");

    private static ApiError InvalidReference(PostgresException exception) => new(
        "invalid_loot_table_reference",
        exception.MessageText,
        ValidationSeverity.Error,
        null,
        "Reload reference data and preview again.");

    private static AuthoringOperationResult<T> VersionConflict<T>(string lootTableId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "loot_table_version_conflict",
            $"Loot table '{lootTableId}' changed after it was loaded. Reload before applying changes.",
            ValidationSeverity.Error,
            "expected_updated_at_utc"));

    private AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception)
    {
        _logger.LogWarning(exception, "Loot table authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            "database_unavailable",
            "The configured development database is unavailable or missing the loot-table authoring schema.",
            ValidationSeverity.Error,
            null,
            "Verify the configured development database and migrations 039/040."));
    }

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException;

    private static bool IsInvalidReference(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.ForeignKeyViolation
        || exception.SqlState == PostgresErrorCodes.CheckViolation
        || exception.SqlState == PostgresErrorCodes.UniqueViolation
        || exception.SqlState == PostgresErrorCodes.RaiseException;
}
