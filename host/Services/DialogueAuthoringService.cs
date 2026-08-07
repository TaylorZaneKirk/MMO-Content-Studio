using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class DialogueAuthoringService
{
    private readonly IDialogueRepository _repository;
    private readonly DialogueDefinitionValidator _validator;
    private readonly DialogueAuthoringRegistry _registry;
    private readonly DialogueGraphAnalyzer _analyzer;
    private readonly DialoguePlaythroughService _playthrough;
    private readonly IRuntimeCatalogPublisher? _runtimeCatalogPublisher;
    private readonly ILogger<DialogueAuthoringService> _logger;

    public DialogueAuthoringService(
        IDialogueRepository repository,
        DialogueDefinitionValidator validator,
        DialogueAuthoringRegistry registry,
        DialogueGraphAnalyzer analyzer,
        DialoguePlaythroughService playthrough,
        ILogger<DialogueAuthoringService> logger,
        IRuntimeCatalogPublisher? runtimeCatalogPublisher = null)
    {
        _repository = repository;
        _validator = validator;
        _registry = registry;
        _analyzer = analyzer;
        _playthrough = playthrough;
        _runtimeCatalogPublisher = runtimeCatalogPublisher;
        _logger = logger;
    }

    public Task<AuthoringOperationResult<DialogueOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthoringOperationResult<DialogueOptionsResponse>.Success(
            new DialogueOptionsResponse(
                _registry.LoadPublicationStates(),
                _registry.LoadNodeTypes(),
                _registry.LoadConditionTypes(),
                _registry.LoadEffectTypes(),
                _registry.LoadSupportedLimits(),
                _registry.LoadCapabilities(),
                _registry.Defaults)));

    public async Task<AuthoringOperationResult<DialogueCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<DialogueCatalogResponse>.Success(
                new DialogueCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DialogueCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<DialogueDefinition>> LoadAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = DialogueDomainRules.NormalizeStableId(dialogueDefinitionId);
            var record = await _repository.LoadAsync(stableId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<DialogueDefinition>.Failure(DialogueNotFound(stableId))
                : AuthoringOperationResult<DialogueDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DialogueDefinition>(exception);
        }
    }

    public async Task<AuthoringOperationResult<DialoguePreviewResponse>> PreviewAsync(
        string dialogueDefinitionId,
        PreviewDialogueRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = DialogueDomainRules.NormalizeStableId(dialogueDefinitionId);
            var operation = NormalizePreviewOperation(request.TargetOperation);
            if (operation is null)
            {
                return AuthoringOperationResult<DialoguePreviewResponse>.Failure(InvalidTargetOperation());
            }

            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (HasVersionConflict(existing, request.ExpectedUpdatedAtUtc))
            {
                return VersionConflict<DialoguePreviewResponse>(stableId);
            }
            if (existing is null && operation is "publish" or "disable" or "delete")
            {
                return AuthoringOperationResult<DialoguePreviewResponse>.Failure(DialogueNotFound(stableId));
            }

            var requested = Normalize(request);
            var effective = operation == "save_draft" ? requested : FromRecord(existing!);
            var validation = _validator.Validate(
                stableId,
                effective,
                existing,
                operation == "publish");
            var messages = validation.Messages.ToList();

            if (operation is "publish" or "disable" or "delete" && !EquivalentDraft(existing!, requested))
            {
                messages.Add(new ApiError(
                    "dialogue_unsaved_changes",
                    "Save the edited dialogue definition as a draft before changing publication state or deleting it.",
                    ValidationSeverity.Error,
                    "preview_signature"));
            }
            if (operation == "delete" && existing!.PublicationState != "Disabled")
            {
                messages.Add(DeleteRequiresDisabledError(stableId));
            }

            var references = await LoadReferenceSummaryAsync(stableId, cancellationToken);
            AddReferenceDiagnostics(messages, operation, references);
            var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
            var validForDraft = operation == "save_draft"
                ? validation.ValidForDraft && !messages.Any(DialogueDefinitionValidator.IsDraftBlocking)
                : validation.ValidForDraft && !hasErrors;

            return AuthoringOperationResult<DialoguePreviewResponse>.Success(
                new DialoguePreviewResponse(
                    operation,
                    validForDraft,
                    validation.ValidForPublication && !hasErrors,
                    messages,
                    CalculateChanges(stableId, existing, requested, operation),
                    validation.Analysis,
                    ToReferenceSummary(references),
                    ComputePreviewSignature(
                        stableId,
                        operation,
                        effective,
                        request.ExpectedUpdatedAtUtc)));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DialoguePreviewResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<DialoguePlaythroughResponse>> PreviewPlaythroughAsync(
        string dialogueDefinitionId,
        PreviewDialoguePlaythroughRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = DialogueDomainRules.NormalizeStableId(dialogueDefinitionId);
            var draft = request.Draft is not null
                ? DialogueDomainRules.NormalizeDraft(request.Draft)
                : await LoadDraftAsync(stableId, cancellationToken);
            if (draft is null)
            {
                return AuthoringOperationResult<DialoguePlaythroughResponse>.Failure(DialogueNotFound(stableId));
            }

            var validation = _validator.Validate(stableId, draft, null, false);
            var response = _playthrough.Preview(draft, request);
            return AuthoringOperationResult<DialoguePlaythroughResponse>.Success(
                response with
                {
                    Warnings = response.Warnings.Concat(validation.Messages.Where(message => message.Severity != ValidationSeverity.Error)).ToArray()
                });
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DialoguePlaythroughResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<DialogueMutationResponse>> SaveDraftAsync(
        string dialogueDefinitionId,
        DialogueMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = DialogueDomainRules.NormalizeStableId(dialogueDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            var draft = Normalize(request);
            if (!IsMatchingPreview(
                    stableId,
                    "save_draft",
                    draft,
                    request.ExpectedUpdatedAtUtc,
                    request.PreviewSignature))
            {
                return AuthoringOperationResult<DialogueMutationResponse>.Failure(PreviewMismatch("save_draft"));
            }

            var validation = _validator.Validate(stableId, draft, existing, false);
            if (!validation.ValidForDraft)
            {
                return AuthoringOperationResult<DialogueMutationResponse>.Failure(validation.Messages);
            }

            var saved = existing is null
                ? await _repository.InsertDraftAsync(stableId, draft, cancellationToken)
                : await _repository.ReplaceDraftAsync(stableId, draft, request.ExpectedUpdatedAtUtc, cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified))
            {
                return ReloadVerificationFailure<DialogueMutationResponse>(stableId);
            }

            return AuthoringOperationResult<DialogueMutationResponse>.Success(
                new DialogueMutationResponse(
                    "save_draft",
                    ToDefinition(verified),
                    validation.Messages));
        }
        catch (DialogueDefinitionConcurrencyException)
        {
            return VersionConflict<DialogueMutationResponse>(DialogueDomainRules.NormalizeStableId(dialogueDefinitionId));
        }
        catch (Exception exception) when (exception is DialogueDefinitionDuplicateException or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return AuthoringOperationResult<DialogueMutationResponse>.Failure(DuplicateDialogueId(dialogueDefinitionId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DialogueMutationResponse>(exception);
        }
    }

    public Task<AuthoringOperationResult<DialogueMutationResponse>> PublishAsync(
        string dialogueDefinitionId,
        DialogueLifecycleRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(dialogueDefinitionId, "Published", "publish", request, cancellationToken);

    public Task<AuthoringOperationResult<DialogueMutationResponse>> DisableAsync(
        string dialogueDefinitionId,
        DialogueLifecycleRequest request,
        CancellationToken cancellationToken = default) =>
        SetPublicationAsync(dialogueDefinitionId, "Disabled", "disable", request, cancellationToken);

    public async Task<AuthoringOperationResult<DialogueDeleteResponse>> DeleteAsync(
        string dialogueDefinitionId,
        DialogueDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stableId = DialogueDomainRules.NormalizeStableId(dialogueDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<DialogueDeleteResponse>.Failure(DialogueNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(stableId, "delete", draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<DialogueDeleteResponse>.Failure(PreviewMismatch("delete"));
            }
            if (existing.PublicationState != "Disabled")
            {
                return AuthoringOperationResult<DialogueDeleteResponse>.Failure(DeleteRequiresDisabledError(stableId));
            }

            var references = await LoadReferenceSummaryAsync(stableId, cancellationToken);
            if (references.KnownReferenceCount > 0)
            {
                return AuthoringOperationResult<DialogueDeleteResponse>.Failure(DeleteBlockedByReference(stableId, references));
            }

            await _repository.DeleteAsync(stableId, request.ExpectedUpdatedAtUtc, cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is not null)
            {
                return ReloadVerificationFailure<DialogueDeleteResponse>(stableId);
            }

            var messages = new List<ApiError>();
            AddReferenceDiagnostics(messages, "delete", references);
            return AuthoringOperationResult<DialogueDeleteResponse>.Success(
                new DialogueDeleteResponse("delete", stableId, messages));
        }
        catch (DialogueDefinitionNotFoundException)
        {
            return AuthoringOperationResult<DialogueDeleteResponse>.Failure(DialogueNotFound(dialogueDefinitionId));
        }
        catch (DialogueDefinitionDeleteRequiresDisabledException)
        {
            return AuthoringOperationResult<DialogueDeleteResponse>.Failure(DeleteRequiresDisabledError(dialogueDefinitionId));
        }
        catch (DialogueDefinitionConcurrencyException)
        {
            return VersionConflict<DialogueDeleteResponse>(DialogueDomainRules.NormalizeStableId(dialogueDefinitionId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DialogueDeleteResponse>(exception);
        }
    }

    public static DialogueDraft Normalize(PreviewDialogueRequest request) =>
        DialogueDomainRules.NormalizeDraft(
            request.DisplayName,
            request.SchemaVersion,
            request.EntryPoints,
            request.Nodes,
            request.MetadataDescription,
            request.Notes,
            request.ExpectedUpdatedAtUtc,
            null);

    public static DialogueDraft Normalize(DialogueMutationRequest request) =>
        DialogueDomainRules.NormalizeDraft(
            request.DisplayName,
            request.SchemaVersion,
            request.EntryPoints,
            request.Nodes,
            request.MetadataDescription,
            request.Notes,
            request.ExpectedUpdatedAtUtc,
            request.PreviewSignature);

    public static DialogueDraft FromRecord(DialogueDefinitionRecord record) =>
        DialogueDomainRules.NormalizeDraft(
            record.DisplayName,
            record.SchemaVersion,
            record.EntryPoints,
            record.Nodes,
            record.MetadataDescription,
            record.Notes,
            null,
            null);

    public static string ComputePreviewSignature(
        string dialogueDefinitionId,
        string operation,
        DialogueDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        var payload = JsonSerializer.Serialize(new
        {
            dialogue_definition_id = dialogueDefinitionId,
            operation,
            expected_updated_at_utc = expectedUpdatedAtUtc?.ToUniversalTime(),
            draft = ToSignatureDraft(draft)
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool IsMatchingPreview(
        string dialogueDefinitionId,
        string operation,
        DialogueDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        string? suppliedSignature) =>
        string.Equals(
            suppliedSignature,
            ComputePreviewSignature(dialogueDefinitionId, operation, draft, expectedUpdatedAtUtc),
            StringComparison.Ordinal);

    public static bool EquivalentDraft(DialogueDefinitionRecord record, DialogueDraft draft) =>
        string.Equals(record.DisplayName, draft.DisplayName, StringComparison.Ordinal)
        && record.SchemaVersion == draft.SchemaVersion
        && string.Equals(record.MetadataDescription, draft.MetadataDescription, StringComparison.Ordinal)
        && string.Equals(record.Notes, draft.Notes, StringComparison.Ordinal)
        && EntriesEquivalent(record.EntryPoints, draft.EntryPoints)
        && NodesEquivalent(record.Nodes, draft.Nodes);

    public static bool Equivalent(DialogueDefinitionRecord left, DialogueDefinitionRecord right) =>
        left.DialogueDefinitionId == right.DialogueDefinitionId
        && left.DisplayName == right.DisplayName
        && left.PublicationState == right.PublicationState
        && left.SchemaVersion == right.SchemaVersion
        && left.MetadataDescription == right.MetadataDescription
        && left.Notes == right.Notes
        && EntriesEquivalent(left.EntryPoints, right.EntryPoints)
        && NodesEquivalent(left.Nodes, right.Nodes);

    private async Task<AuthoringOperationResult<DialogueMutationResponse>> SetPublicationAsync(
        string dialogueDefinitionId,
        string publicationState,
        string operation,
        DialogueLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var stableId = DialogueDomainRules.NormalizeStableId(dialogueDefinitionId);
            var existing = await _repository.LoadAsync(stableId, cancellationToken);
            if (existing is null)
            {
                return AuthoringOperationResult<DialogueMutationResponse>.Failure(DialogueNotFound(stableId));
            }

            var draft = FromRecord(existing);
            if (!IsMatchingPreview(stableId, operation, draft, request.ExpectedUpdatedAtUtc, request.PreviewSignature))
            {
                return AuthoringOperationResult<DialogueMutationResponse>.Failure(PreviewMismatch(operation));
            }

            var validation = _validator.Validate(stableId, draft, existing, operation == "publish");
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
                return AuthoringOperationResult<DialogueMutationResponse>.Failure(messages);
            }

            var saved = await _repository.SetPublicationAsync(
                stableId,
                publicationState,
                request.ExpectedUpdatedAtUtc,
                cancellationToken);
            var verified = await _repository.LoadAsync(stableId, cancellationToken);
            if (verified is null || !Equivalent(saved, verified) || verified.PublicationState != publicationState)
            {
                return ReloadVerificationFailure<DialogueMutationResponse>(stableId);
            }

            if (operation == "publish" && _runtimeCatalogPublisher is not null)
            {
                messages.AddRange(await _runtimeCatalogPublisher.PublishCatalogsAsync(
                    RuntimeCatalogPublicationScope.Dialogue,
                    cancellationToken));
            }

            return AuthoringOperationResult<DialogueMutationResponse>.Success(
                new DialogueMutationResponse(operation, ToDefinition(verified), messages));
        }
        catch (DialogueDefinitionNotFoundException)
        {
            return AuthoringOperationResult<DialogueMutationResponse>.Failure(DialogueNotFound(dialogueDefinitionId));
        }
        catch (DialogueDefinitionConcurrencyException)
        {
            return VersionConflict<DialogueMutationResponse>(DialogueDomainRules.NormalizeStableId(dialogueDefinitionId));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<DialogueMutationResponse>(exception);
        }
    }

    private async Task<DialogueDraft?> LoadDraftAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken)
    {
        var record = await _repository.LoadAsync(dialogueDefinitionId, cancellationToken);
        return record is null ? null : FromRecord(record);
    }

    private async Task<DialogueReferenceSummaryRecord> LoadReferenceSummaryAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken) =>
        await _repository.LoadNpcReferencesAsync(dialogueDefinitionId, cancellationToken);

    private static DialogueDefinition ToDefinition(DialogueDefinitionRecord record) =>
        new(
            record.DialogueDefinitionId,
            record.DisplayName,
            record.PublicationState,
            record.SchemaVersion,
            record.EntryPoints,
            record.Nodes,
            record.MetadataDescription,
            record.Notes,
            record.CreatedAtUtc,
            record.UpdatedAtUtc);

    private static DialogueDefinitionSummary ToSummary(DialogueDefinitionRecord record) =>
        new(
            record.DialogueDefinitionId,
            record.DisplayName,
            record.PublicationState,
            record.SchemaVersion,
            record.EntryPointCount,
            record.NodeCount,
            record.ChoiceCount,
            record.UpdatedAtUtc);

    private static IReadOnlyList<AuthoringChange> CalculateChanges(
        string dialogueDefinitionId,
        DialogueDefinitionRecord? existing,
        DialogueDraft requested,
        string operation)
    {
        var changes = new List<AuthoringChange>();
        AddChange(changes, "dialogue_definition_id", existing?.DialogueDefinitionId, dialogueDefinitionId);
        AddChange(changes, "display_name", existing?.DisplayName, requested.DisplayName);
        AddChange(changes, "schema_version", existing?.SchemaVersion.ToString(), requested.SchemaVersion.ToString());
        AddChange(changes, "metadata_description", existing?.MetadataDescription, requested.MetadataDescription);
        AddChange(changes, "notes", existing?.Notes, requested.Notes);
        AddEntryChanges(changes, existing?.EntryPoints ?? [], requested.EntryPoints);
        AddNodeChanges(changes, existing?.Nodes ?? [], requested.Nodes);
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

    private static void AddEntryChanges(
        ICollection<AuthoringChange> changes,
        IReadOnlyList<DialogueEntryPoint> existing,
        IReadOnlyList<DialogueEntryPoint> requested)
    {
        var before = existing.ToDictionary(entry => entry.EntryId, StringComparer.Ordinal);
        var after = requested.ToDictionary(entry => entry.EntryId, StringComparer.Ordinal);
        foreach (var entryId in before.Keys.Except(after.Keys, StringComparer.Ordinal).Order())
        {
            changes.Add(new AuthoringChange($"entry_points.{entryId}", "present", null));
        }
        foreach (var entryId in after.Keys.Except(before.Keys, StringComparer.Ordinal).Order())
        {
            changes.Add(new AuthoringChange($"entry_points.{entryId}", null, "present"));
        }
        foreach (var entryId in before.Keys.Intersect(after.Keys, StringComparer.Ordinal).Order())
        {
            AddChange(changes, $"entry_points.{entryId}.node_id", before[entryId].NodeId, after[entryId].NodeId);
            AddChange(changes, $"entry_points.{entryId}.priority", before[entryId].Priority.ToString(), after[entryId].Priority.ToString());
            AddChange(changes, $"entry_points.{entryId}.entry_order", before[entryId].EntryOrder.ToString(), after[entryId].EntryOrder.ToString());
        }
    }

    private static void AddNodeChanges(
        ICollection<AuthoringChange> changes,
        IReadOnlyList<DialogueNode> existing,
        IReadOnlyList<DialogueNode> requested)
    {
        var before = existing.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var after = requested.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        foreach (var nodeId in before.Keys.Except(after.Keys, StringComparer.Ordinal).Order())
        {
            changes.Add(new AuthoringChange($"nodes.{nodeId}", "present", null));
        }
        foreach (var nodeId in after.Keys.Except(before.Keys, StringComparer.Ordinal).Order())
        {
            changes.Add(new AuthoringChange($"nodes.{nodeId}", null, "present"));
        }
        foreach (var nodeId in before.Keys.Intersect(after.Keys, StringComparer.Ordinal).Order())
        {
            var left = before[nodeId];
            var right = after[nodeId];
            AddChange(changes, $"nodes.{nodeId}.node_type", left.NodeType, right.NodeType);
            AddChange(changes, $"nodes.{nodeId}.speaker", left.Speaker, right.Speaker);
            AddChange(changes, $"nodes.{nodeId}.text", left.Text, right.Text);
            AddChange(changes, $"nodes.{nodeId}.next_node_id", left.NextNodeId, right.NextNodeId);
            AddChange(changes, $"nodes.{nodeId}.dismissible", left.Dismissible.ToString(), right.Dismissible.ToString());
            AddChange(changes, $"nodes.{nodeId}.canvas_x", left.CanvasX.ToString("R"), right.CanvasX.ToString("R"));
            AddChange(changes, $"nodes.{nodeId}.canvas_y", left.CanvasY.ToString("R"), right.CanvasY.ToString("R"));
            AddChange(changes, $"nodes.{nodeId}.editor_notes", left.EditorNotes, right.EditorNotes);
            AddChoiceChanges(changes, nodeId, left.Choices, right.Choices);
        }
    }

    private static void AddChoiceChanges(
        ICollection<AuthoringChange> changes,
        string nodeId,
        IReadOnlyList<DialogueChoice> existing,
        IReadOnlyList<DialogueChoice> requested)
    {
        var before = existing.ToDictionary(choice => choice.ChoiceId, StringComparer.Ordinal);
        var after = requested.ToDictionary(choice => choice.ChoiceId, StringComparer.Ordinal);
        foreach (var choiceId in before.Keys.Except(after.Keys, StringComparer.Ordinal).Order())
        {
            changes.Add(new AuthoringChange($"nodes.{nodeId}.choices.{choiceId}", "present", null));
        }
        foreach (var choiceId in after.Keys.Except(before.Keys, StringComparer.Ordinal).Order())
        {
            changes.Add(new AuthoringChange($"nodes.{nodeId}.choices.{choiceId}", null, "present"));
        }
        foreach (var choiceId in before.Keys.Intersect(after.Keys, StringComparer.Ordinal).Order())
        {
            AddChange(changes, $"nodes.{nodeId}.choices.{choiceId}.text", before[choiceId].Text, after[choiceId].Text);
            AddChange(changes, $"nodes.{nodeId}.choices.{choiceId}.target_node_id", before[choiceId].TargetNodeId, after[choiceId].TargetNodeId);
            AddChange(changes, $"nodes.{nodeId}.choices.{choiceId}.choice_order", before[choiceId].ChoiceOrder.ToString(), after[choiceId].ChoiceOrder.ToString());
        }
    }

    private static bool EntriesEquivalent(
        IReadOnlyList<DialogueEntryPoint> left,
        IReadOnlyList<DialogueEntryPoint> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            pair.First.EntryId == pair.Second.EntryId
            && pair.First.NodeId == pair.Second.NodeId
            && pair.First.Priority == pair.Second.Priority
            && pair.First.EntryOrder == pair.Second.EntryOrder
            && pair.First.Conditions.SequenceEqual(pair.Second.Conditions, StringComparer.Ordinal));

    private static bool NodesEquivalent(
        IReadOnlyList<DialogueNode> left,
        IReadOnlyList<DialogueNode> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            pair.First.NodeId == pair.Second.NodeId
            && pair.First.NodeType == pair.Second.NodeType
            && pair.First.Speaker == pair.Second.Speaker
            && pair.First.Text == pair.Second.Text
            && pair.First.NextNodeId == pair.Second.NextNodeId
            && pair.First.Dismissible == pair.Second.Dismissible
            && pair.First.CanvasX.Equals(pair.Second.CanvasX)
            && pair.First.CanvasY.Equals(pair.Second.CanvasY)
            && pair.First.EditorNotes == pair.Second.EditorNotes
            && ChoicesEquivalent(pair.First.Choices, pair.Second.Choices));

    private static bool ChoicesEquivalent(
        IReadOnlyList<DialogueChoice> left,
        IReadOnlyList<DialogueChoice> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            pair.First.ChoiceId == pair.Second.ChoiceId
            && pair.First.Text == pair.Second.Text
            && pair.First.TargetNodeId == pair.Second.TargetNodeId
            && pair.First.ChoiceOrder == pair.Second.ChoiceOrder
            && pair.First.Conditions.SequenceEqual(pair.Second.Conditions, StringComparer.Ordinal));

    private static DialogueReferenceSummary ToReferenceSummary(DialogueReferenceSummaryRecord record) =>
        new(record.KnownReferenceCount, record.ReferenceSources, record.ReferenceCheckComplete);

    private static void AddReferenceDiagnostics(
        ICollection<ApiError> messages,
        string operation,
        DialogueReferenceSummaryRecord references)
    {
        if (operation == "disable" && references.PublishedReferenceCount > 0)
        {
            messages.Add(DisableBlockedByReference(references.DialogueDefinitionId, references));
        }
        if (operation == "delete" && references.KnownReferenceCount > 0)
        {
            messages.Add(DeleteBlockedByReference(references.DialogueDefinitionId, references));
        }
        if (operation is "disable" or "delete" && !references.ReferenceCheckComplete)
        {
            messages.Add(new ApiError(
                "dialogue_reference_check_incomplete",
                "Dialogue NPC references could not be checked completely because the NPC authoring schema is unavailable.",
                ValidationSeverity.Warning,
                "dialogue_definition_id"));
        }
    }

    private static object ToSignatureDraft(DialogueDraft draft)
    {
        var normalized = DialogueDomainRules.NormalizeDraft(draft);
        return new
        {
            normalized.DisplayName,
            normalized.SchemaVersion,
            normalized.EntryPoints,
            normalized.Nodes,
            normalized.MetadataDescription,
            normalized.Notes
        };
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

    private static string? NormalizePreviewOperation(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "save_draft" or "publish" or "disable" or "delete" ? normalized : null;
    }

    public static bool HasVersionConflict(
        DialogueDefinitionRecord? existing,
        DateTimeOffset? expected) =>
        existing is null
            ? expected is not null
            : expected is null
                || existing.UpdatedAtUtc.ToUniversalTime() != expected.Value.ToUniversalTime();

    private static ApiError InvalidTargetOperation() => new(
        "dialogue_invalid_definition",
        "Target operation must be save_draft, publish, disable, or delete.",
        ValidationSeverity.Error,
        "target_operation");

    private static ApiError DialogueNotFound(string dialogueDefinitionId) => new(
        "dialogue_not_found",
        $"Dialogue definition '{DialogueDomainRules.NormalizeStableId(dialogueDefinitionId)}' does not exist.",
        ValidationSeverity.Error,
        "dialogue_definition_id");

    private static ApiError DuplicateDialogueId(string dialogueDefinitionId) => new(
        "dialogue_duplicate_id",
        $"Dialogue definition '{DialogueDomainRules.NormalizeStableId(dialogueDefinitionId)}' already exists.",
        ValidationSeverity.Error,
        "dialogue_definition_id");

    private static ApiError PreviewMismatch(string operation) => new(
        "dialogue_preview_mismatch",
        $"Preview the {operation} operation again before applying it.",
        ValidationSeverity.Error,
        "preview_signature");

    private static ApiError DeleteRequiresDisabledError(string dialogueDefinitionId) => new(
        "dialogue_delete_requires_disabled",
        $"Dialogue definition '{DialogueDomainRules.NormalizeStableId(dialogueDefinitionId)}' must be disabled before it can be deleted.",
        ValidationSeverity.Error,
        "publication_state",
        "Disable the dialogue definition, preview Delete again, then apply the delete operation.");

    private static ApiError DisableBlockedByReference(
        string dialogueDefinitionId,
        DialogueReferenceSummaryRecord references) => new(
            "dialogue_disable_blocked_by_reference",
            $"Dialogue definition '{DialogueDomainRules.NormalizeStableId(dialogueDefinitionId)}' is referenced by {references.PublishedReferenceCount} published NPC definition(s).",
            ValidationSeverity.Error,
            "dialogue_definition_id",
            string.Join("; ", references.ReferenceSources));

    private static ApiError DeleteBlockedByReference(
        string dialogueDefinitionId,
        DialogueReferenceSummaryRecord references) => new(
            "dialogue_delete_blocked_by_reference",
            $"Dialogue definition '{DialogueDomainRules.NormalizeStableId(dialogueDefinitionId)}' is referenced by {references.KnownReferenceCount} known NPC definition(s).",
            ValidationSeverity.Error,
            "dialogue_definition_id",
            string.Join("; ", references.ReferenceSources));

    private static AuthoringOperationResult<T> VersionConflict<T>(string dialogueDefinitionId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "dialogue_version_conflict",
            $"Dialogue definition '{dialogueDefinitionId}' changed after it was loaded. Reload before applying changes.",
            ValidationSeverity.Error,
            "updated_at_utc"));

    private static AuthoringOperationResult<T> ReloadVerificationFailure<T>(string dialogueDefinitionId) =>
        AuthoringOperationResult<T>.Failure(new ApiError(
            "dialogue_reload_verification_failed",
            $"Dialogue definition '{dialogueDefinitionId}' did not match after reload verification.",
            ValidationSeverity.Error,
            "dialogue_definition_id"));

    private AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception)
    {
        _logger.LogWarning(exception, "Dialogue authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            IsUndefinedTable(exception) ? "dialogue_schema_unavailable" : "dialogue_database_unavailable",
            IsUndefinedTable(exception)
                ? "The configured development database is missing the D2 dialogue authoring schema."
                : "The configured development database is unavailable.",
            ValidationSeverity.Error,
            Remediation: "Review the Environment tab and apply the dialogue authoring migration handoff when D2 is approved."));
    }

    private static bool IsUndefinedTable(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable };

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException
            or InvalidOperationException;
}
