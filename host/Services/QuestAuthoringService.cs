using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class QuestAuthoringService
{
    private readonly IQuestRepository _repository;
    private readonly QuestDefinitionValidator _validator;
    private readonly QuestAuthoringRegistry _registry;
    private readonly IRuntimeCatalogPublisher? _runtimeCatalogPublisher;

    public QuestAuthoringService(
        IQuestRepository repository,
        QuestDefinitionValidator validator,
        QuestAuthoringRegistry registry,
        IRuntimeCatalogPublisher? runtimeCatalogPublisher = null)
    {
        _repository = repository;
        _validator = validator;
        _registry = registry;
        _runtimeCatalogPublisher = runtimeCatalogPublisher;
    }

    public Task<AuthoringOperationResult<QuestOptionsResponse>> LoadOptionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthoringOperationResult<QuestOptionsResponse>.Success(
            new QuestOptionsResponse(
                _registry.LoadPublicationStates(),
                _registry.LoadQuestStatuses(),
                _registry.LoadSupportedLimits(),
                _registry.LoadCapabilities(),
                _registry.Defaults)));

    public async Task<AuthoringOperationResult<QuestCatalogResponse>> ListAsync(string? search, CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<QuestCatalogResponse>.Success(
                new QuestCatalogResponse(DateTimeOffset.UtcNow, records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<QuestCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<QuestDefinition>> LoadAsync(string questId, CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = QuestDomainRules.NormalizeStableId(questId);
            var record = await _repository.LoadAsync(stableId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<QuestDefinition>.Failure(QuestNotFound(stableId))
                : AuthoringOperationResult<QuestDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<QuestDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<QuestPreviewResponse>> PreviewAsync(
        string questId,
        PreviewQuestRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = QuestDomainRules.NormalizeStableId(questId);
            var operation = NormalizeOperation(request.TargetOperation);
            if (operation is null)
            {
                return AuthoringOperationResult<QuestPreviewResponse>.Failure(InvalidOperation());
            }

            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<QuestPreviewResponse>(stableId);
            }
            if (existing is null && operation is "publish" or "disable" or "delete")
            {
                return AuthoringOperationResult<QuestPreviewResponse>.Failure(QuestNotFound(stableId));
            }

            var requested = Normalize(request);
            var effective = operation == "save_draft" ? requested : FromRecord(existing!);
            var validation = _validator.Validate(stableId, effective, existing, operation == "publish");
            var messages = validation.Messages.ToList();
            if (operation is "publish" or "disable" or "delete" && !EquivalentDraft(existing!, requested))
            {
                messages.Add(new ApiError(
                    "quest_unsaved_changes",
                    "Save the edited quest definition as a draft before changing publication state or deleting it.",
                    ValidationSeverity.Error,
                    "preview_signature"));
            }
            if (operation == "delete" && existing!.PublicationState != "Disabled")
            {
                messages.Add(DeleteRequiresDisabledError(stableId));
            }

            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            var validForDraft = operation == "save_draft"
                ? validation.ValidForDraft && !messages.Any(QuestDefinitionValidator.IsDraftBlocking)
                : validation.ValidForDraft && !hasErrors;

            return AuthoringOperationResult<QuestPreviewResponse>.Success(
                new QuestPreviewResponse(
                    operation,
                    validForDraft,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(stableId, existing, requested, operation),
                    validation.Analysis,
                    ComputePreviewSignature(stableId, operation, effective, request.ExpectedUpdatedAtUtc)));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<QuestPreviewResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<QuestMutationResponse>> SaveDraftAsync(
        string questId,
        QuestMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = QuestDomainRules.NormalizeStableId(questId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            var draft = Normalize(request);
            if (!IsMatchingPreview(stableId, "save_draft", draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<QuestMutationResponse>.Failure(PreviewMismatch("save_draft"));
            }

            var validation = _validator.Validate(stableId, draft, existing, false);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<QuestMutationResponse>.Failure(validation.Messages);
            }

            var saved = await _repository.ReplaceDraftAsync(stableId, draft, request.ExpectedUpdatedAtUtc, cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified))
            {
                return ReloadVerificationFailure<QuestMutationResponse>(stableId);
            }

            return AuthoringOperationResult<QuestMutationResponse>.Success(
                new QuestMutationResponse("save_draft", ToDefinition(verified), validation.Messages));
        }
        catch (QuestDefinitionConcurrencyException)
        {
            return VersionConflict<QuestMutationResponse>(QuestDomainRules.NormalizeStableId(questId));
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            return AuthoringOperationResult<QuestMutationResponse>.Failure(new ApiError(
                "quest_invalid_definition",
                exception.MessageText,
                ValidationSeverity.Error));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<QuestMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<QuestMutationResponse>> PublishAsync(string questId, QuestLifecycleRequest request, CancellationToken cancellationToken = default) =>
        SetPublicationAsync(questId, "Published", "publish", request, cancellationToken);

    public Task<AuthoringOperationResult<QuestMutationResponse>> DisableAsync(string questId, QuestLifecycleRequest request, CancellationToken cancellationToken = default) =>
        SetPublicationAsync(questId, "Disabled", "disable", request, cancellationToken);

    public async Task<AuthoringOperationResult<QuestDeleteResponse>> DeleteAsync(
        string questId,
        QuestDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = QuestDomainRules.NormalizeStableId(questId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<QuestDeleteResponse>.Failure(QuestNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(stableId, "delete", draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<QuestDeleteResponse>.Failure(PreviewMismatch("delete"));
            }
            if (existing.PublicationState != "Disabled")
            {
                return AuthoringOperationResult<QuestDeleteResponse>.Failure(DeleteRequiresDisabledError(stableId));
            }

            await _repository.DeleteAsync(stableId, request.ExpectedUpdatedAtUtc, cancellationToken);
            return AuthoringOperationResult<QuestDeleteResponse>.Success(
                new QuestDeleteResponse("delete", stableId, []));
        }
        catch (QuestDefinitionConcurrencyException)
        {
            return VersionConflict<QuestDeleteResponse>(QuestDomainRules.NormalizeStableId(questId));
        }
        catch (QuestDefinitionDeleteRequiresDisabledException)
        {
            return AuthoringOperationResult<QuestDeleteResponse>.Failure(DeleteRequiresDisabledError(questId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<QuestDeleteResponse>(exception);
        }
    }

    public static QuestDraft Normalize(PreviewQuestRequest request) =>
        QuestDomainRules.NormalizeDraft(
            request.DisplayName,
            request.SchemaVersion,
            request.Steps,
            request.Transitions,
            request.ExpectedUpdatedAtUtc,
            null);

    public static QuestDraft Normalize(QuestMutationRequest request) =>
        QuestDomainRules.NormalizeDraft(
            request.DisplayName,
            request.SchemaVersion,
            request.Steps,
            request.Transitions,
            request.ExpectedUpdatedAtUtc,
            request.PreviewSignature);

    public static QuestDraft FromRecord(QuestDefinitionRecord record) =>
        QuestDomainRules.NormalizeDraft(
            record.DisplayName,
            record.SchemaVersion,
            record.Steps,
            record.Transitions,
            null,
            null);

    public static string ComputePreviewSignature(
        string questId,
        string operation,
        QuestDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        var payload = JsonSerializer.Serialize(new
        {
            quest_id = questId,
            operation,
            expected_updated_at_utc = expectedUpdatedAtUtc?.ToUniversalTime(),
            draft = new
            {
                draft.DisplayName,
                draft.SchemaVersion,
                draft.Steps,
                draft.Transitions
            }
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public static bool IsMatchingPreview(string questId, string operation, QuestDraft draft, DateTimeOffset? expectedUpdatedAtUtc, string? suppliedSignature) =>
        string.Equals(suppliedSignature, ComputePreviewSignature(questId, operation, draft, expectedUpdatedAtUtc), StringComparison.Ordinal);

    public static bool EquivalentDraft(QuestDefinitionRecord record, QuestDraft draft) =>
        record.DisplayName == draft.DisplayName
        && record.SchemaVersion == draft.SchemaVersion
        && record.Steps.SequenceEqual(draft.Steps)
        && record.Transitions.SequenceEqual(draft.Transitions);

    public static bool Equivalent(QuestDefinitionRecord left, QuestDefinitionRecord right) =>
        left.QuestId == right.QuestId
        && left.DisplayName == right.DisplayName
        && left.PublicationState == right.PublicationState
        && left.SchemaVersion == right.SchemaVersion
        && left.Steps.SequenceEqual(right.Steps)
        && left.Transitions.SequenceEqual(right.Transitions);

    private async Task<AuthoringOperationResult<QuestMutationResponse>> SetPublicationAsync(
        string questId,
        string publicationState,
        string operation,
        QuestLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var stableId = QuestDomainRules.NormalizeStableId(questId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<QuestMutationResponse>.Failure(QuestNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(stableId, operation, draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<QuestMutationResponse>.Failure(PreviewMismatch(operation));
            }

            var validation = _validator.Validate(stableId, draft, existing, operation == "publish");
            if (!validation.ValidForDraft || validation.Messages.Any(message => message.Severity == ValidationSeverity.Error))
            {
                return AuthoringOperationResult<QuestMutationResponse>.Failure(validation.Messages);
            }

            var saved = await _repository.SetPublicationAsync(stableId, publicationState, request.ExpectedUpdatedAtUtc, cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified) || verified.PublicationState != publicationState)
            {
                return ReloadVerificationFailure<QuestMutationResponse>(stableId);
            }

            var messages = validation.Messages.ToList();
            if (operation == "publish" && _runtimeCatalogPublisher is not null)
            {
                messages.AddRange(await _runtimeCatalogPublisher.PublishCatalogsAsync(
                    RuntimeCatalogPublicationScope.Quest,
                    cancellationToken));
            }

            return AuthoringOperationResult<QuestMutationResponse>.Success(
                new QuestMutationResponse(operation, ToDefinition(verified), messages));
        }
        catch (QuestDefinitionConcurrencyException)
        {
            return VersionConflict<QuestMutationResponse>(QuestDomainRules.NormalizeStableId(questId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<QuestMutationResponse>(exception);
        }
    }

    private static QuestDefinition ToDefinition(QuestDefinitionRecord record) =>
        new(record.QuestId, record.DisplayName, record.PublicationState, record.SchemaVersion, record.Steps, record.Transitions, record.CreatedAtUtc, record.UpdatedAtUtc);

    private static QuestDefinitionSummary ToSummary(QuestDefinitionRecord record) =>
        new(record.QuestId, record.DisplayName, record.PublicationState, record.SchemaVersion, record.StepCount, record.TransitionCount, record.UpdatedAtUtc);

    private static IReadOnlyList<AuthoringChange> CalculateChanges(string questId, QuestDefinitionRecord? existing, QuestDraft requested, string operation)
    {
        var targetState = operation switch
        {
            "publish" => "Published",
            "disable" => "Disabled",
            "delete" => "Deleted",
            _ => "Draft"
        };
        var changes = new List<AuthoringChange>();
        AddChange(changes, "quest_id", existing?.QuestId, questId);
        AddChange(changes, "display_name", existing?.DisplayName, requested.DisplayName);
        AddChange(changes, "publication_state", existing?.PublicationState, targetState);
        AddChange(changes, "steps", existing?.Steps.Count.ToString(), requested.Steps.Count.ToString());
        AddChange(changes, "transitions", existing?.Transitions.Count.ToString(), requested.Transitions.Count.ToString());
        return changes;
    }

    private static void AddChange(ICollection<AuthoringChange> changes, string field, string? before, string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new AuthoringChange(field, before, after));
        }
    }

    private static bool HasVersionConflict(QuestDefinitionRecord? existing, DateTimeOffset? expectedUpdatedAtUtc) =>
        existing is not null &&
        expectedUpdatedAtUtc is not null &&
        existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime();

    private static string? NormalizeOperation(string operation) =>
        QuestDomainRules.NormalizeStableId(operation) switch
        {
            "save_draft" => "save_draft",
            "publish" => "publish",
            "disable" => "disable",
            "delete" => "delete",
            _ => null
        };

    private static ApiError QuestNotFound(string questId) =>
        new("quest_not_found", $"Quest definition '{questId}' was not found.", ValidationSeverity.Error, "quest_id");

    private static ApiError InvalidOperation() =>
        new("quest_invalid_operation", "Quest preview target operation must be save_draft, publish, disable, or delete.", ValidationSeverity.Error, "target_operation");

    private static ApiError PreviewMismatch(string operation) =>
        new("quest_preview_mismatch", $"Preview the current quest draft before applying {operation}.", ValidationSeverity.Error, "preview_signature");

    private static ApiError DeleteRequiresDisabledError(string questId) =>
        new("quest_delete_requires_disabled", $"Quest definition '{questId}' must be Disabled before deletion.", ValidationSeverity.Error, "publication_state");

    private static AuthoringOperationResult<T> VersionConflict<T>(string questId) =>
        AuthoringOperationResult<T>.Failure(new ApiError("quest_version_conflict", $"Quest definition '{questId}' changed before the mutation could be applied.", ValidationSeverity.Error, "expected_updated_at_utc"));

    private static AuthoringOperationResult<T> ReloadVerificationFailure<T>(string questId) =>
        AuthoringOperationResult<T>.Failure(new ApiError("quest_reload_verification_failed", $"Quest definition '{questId}' could not be verified after mutation.", ValidationSeverity.Error, "quest_id"));

    private static AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception) =>
        AuthoringOperationResult<T>.Failure(new ApiError("database_unavailable", exception.Message, ValidationSeverity.Error));

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is NpgsqlException or InvalidOperationException;
}
