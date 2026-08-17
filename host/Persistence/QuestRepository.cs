using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public interface IQuestRepository
{
    Task<IReadOnlyList<QuestDefinitionRecord>> ListAsync(string? search, CancellationToken cancellationToken = default);
    Task<QuestDefinitionRecord?> LoadAsync(string questId, CancellationToken cancellationToken = default);
    Task<QuestStateReferenceSummary> LoadStateReferencesAsync(string questId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> LoadPublishedDialogueReferencesAsync(string questId, CancellationToken cancellationToken = default);
    Task<QuestDefinitionRecord> ReplaceDraftAsync(string questId, QuestDraft draft, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default);
    Task<QuestDefinitionRecord> SetPublicationAsync(string questId, string publicationState, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default);
    Task DeleteAsync(string questId, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default);
}

public sealed class QuestRepository : IQuestRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public QuestRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<QuestDefinitionRecord>> ListAsync(string? search, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                q.quest_id,
                q.display_name,
                q.publication_state,
                q.schema_version,
                q.created_at_utc,
                q.updated_at_utc,
                count(distinct s.step_id)::integer as step_count,
                count(distinct t.transition_id)::integer as transition_count
            from quest_definitions q
            left join quest_steps s on s.quest_id = q.quest_id
            left join quest_transitions t on t.quest_id = q.quest_id
            where @search is null
               or q.quest_id ilike '%' || @search || '%'
               or q.display_name ilike '%' || @search || '%'
            group by q.quest_id
            order by q.display_name, q.quest_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<QuestDefinitionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new QuestDefinitionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                [],
                [],
                ReadUtc(reader, "created_at_utc"),
                ReadUtc(reader, "updated_at_utc"),
                reader.GetInt32(reader.GetOrdinal("step_count")),
                reader.GetInt32(reader.GetOrdinal("transition_count"))));
        }

        return records;
    }

    public async Task<QuestDefinitionRecord?> LoadAsync(string questId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAsync(connection, null, questId, false, cancellationToken);
    }

    public async Task<QuestStateReferenceSummary> LoadStateReferencesAsync(string questId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadStateReferencesAsync(connection, null, questId, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> LoadPublishedDialogueReferencesAsync(string questId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadPublishedDialogueReferencesAsync(connection, null, questId, cancellationToken);
    }

    public async Task<QuestDefinitionRecord> ReplaceDraftAsync(
        string questId,
        QuestDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockQuestContentStateAsync(connection, transaction, questId, cancellationToken);
        var existing = await LoadAsync(connection, transaction, questId, true, cancellationToken);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, questId);
        var references = await LoadStateReferencesAsync(connection, transaction, questId, cancellationToken);
        EnsureReferenceSafeDraftReplacement(questId, references);
        await EnsureNoPublishedDialogueReferencesAsync(connection, transaction, questId, "save_draft", cancellationToken);

        if (existing is null)
        {
            await InsertRootAsync(connection, transaction, questId, draft, cancellationToken);
        }
        else
        {
            await UpdateRootAsync(connection, transaction, questId, draft, cancellationToken);
            await DeleteChildrenAsync(connection, transaction, questId, cancellationToken);
        }

        await ReplaceChildrenAsync(connection, transaction, questId, draft, cancellationToken);
        var saved = await LoadAsync(connection, transaction, questId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved quest definition could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<QuestDefinitionRecord> SetPublicationAsync(
        string questId,
        string publicationState,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockQuestContentStateAsync(connection, transaction, questId, cancellationToken);
        var existing = await LoadAsync(connection, transaction, questId, true, cancellationToken)
            ?? throw new QuestDefinitionNotFoundException(questId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, questId);
        var references = await LoadStateReferencesAsync(connection, transaction, questId, cancellationToken);
        EnsureReferenceSafePublication(questId, publicationState, existing, references);
        if (publicationState != "Published")
        {
            await EnsureNoPublishedDialogueReferencesAsync(connection, transaction, questId, "disable", cancellationToken);
        }

        const string sql = """
            update quest_definitions
            set publication_state = @publication_state,
                updated_at_utc = now()
            where quest_id = @quest_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("quest_id", questId);
            command.Parameters.AddWithValue("publication_state", publicationState);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var saved = await LoadAsync(connection, transaction, questId, false, cancellationToken)
            ?? throw new InvalidOperationException("Quest publication change could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(string questId, DateTimeOffset? expectedUpdatedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockQuestContentStateAsync(connection, transaction, questId, cancellationToken);
        var existing = await LoadAsync(connection, transaction, questId, true, cancellationToken)
            ?? throw new QuestDefinitionNotFoundException(questId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, questId);
        if (existing.PublicationState != "Disabled")
        {
            throw new QuestDefinitionDeleteRequiresDisabledException(questId);
        }
        var references = await LoadStateReferencesAsync(connection, transaction, questId, cancellationToken);
        if (references.HasReferences)
        {
            throw new QuestDefinitionReferencedByStateException(questId, "delete", references);
        }
        await EnsureNoPublishedDialogueReferencesAsync(connection, transaction, questId, "delete", cancellationToken);

        await using var command = new NpgsqlCommand("delete from quest_definitions where quest_id = @quest_id;", connection, transaction);
        command.Parameters.AddWithValue("quest_id", questId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<QuestDefinitionRecord?> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string questId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var rootSql = """
            select quest_id, display_name, publication_state, schema_version, created_at_utc, updated_at_utc
            from quest_definitions
            where quest_id = @quest_id
            """ + (forUpdate ? " for update;" : ";");

        QuestDefinitionRecord? root;
        await using (var command = new NpgsqlCommand(rootSql, connection, transaction))
        {
            command.Parameters.AddWithValue("quest_id", questId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            root = new QuestDefinitionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                [],
                [],
                ReadUtc(reader, "created_at_utc"),
                ReadUtc(reader, "updated_at_utc"),
                0,
                0);
        }

        var steps = await LoadStepsAsync(connection, transaction, questId, cancellationToken);
        var transitions = await LoadTransitionsAsync(connection, transaction, questId, cancellationToken);
        return root with
        {
            Steps = steps,
            Transitions = transitions,
            StepCount = steps.Count,
            TransitionCount = transitions.Count
        };
    }

    private static async Task<QuestStateReferenceSummary> LoadStateReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string questId,
        CancellationToken cancellationToken)
    {
        const string countsSql = """
            select
                count(*)::integer as total_count,
                count(*) filter (where status = 'active')::integer as active_count,
                count(*) filter (where status = 'completed')::integer as completed_count
            from character_quests
            where quest_id = @quest_id;
            """;

        int totalCount;
        int activeCount;
        int completedCount;
        await using (var command = new NpgsqlCommand(countsSql, connection, transaction))
        {
            command.Parameters.AddWithValue("quest_id", questId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Quest state reference query returned no count row.");
            }

            totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
            activeCount = reader.GetInt32(reader.GetOrdinal("active_count"));
            completedCount = reader.GetInt32(reader.GetOrdinal("completed_count"));
        }

        const string activeStepsSql = """
            select distinct current_step_id
            from character_quests
            where quest_id = @quest_id
              and status = 'active'
              and current_step_id is not null
            order by current_step_id;
            """;
        var activeStepIds = new List<string>();
        await using (var command = new NpgsqlCommand(activeStepsSql, connection, transaction))
        {
            command.Parameters.AddWithValue("quest_id", questId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                activeStepIds.Add(reader.GetString(0));
            }
        }

        var pendingSettlementCount = await LoadPendingSettlementReferenceCountAsync(
            connection,
            transaction,
            questId,
            cancellationToken);

        return new QuestStateReferenceSummary(
            questId,
            totalCount,
            activeCount,
            completedCount,
            activeStepIds,
            pendingSettlementCount);
    }

    private static async Task<int> LoadPendingSettlementReferenceCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string questId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select count(*)::integer
            from character_dialogue_choice_effect_settlements settlement
            join character_dialogue_choice_effect_plan_rows plan
              on plan.settlement_id = settlement.settlement_id
            where settlement.settlement_status <> 'settled'
              and plan.effect_type in ('start_quest', 'advance_quest', 'complete_quest')
              and plan.quest_id = @quest_id;
            """;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("quest_id", questId);
            return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return 0;
        }
    }

    private static async Task<IReadOnlyList<QuestStep>> LoadStepsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string questId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select step_id, display_name, step_order
            from quest_steps
            where quest_id = @quest_id
            order by step_order, step_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("quest_id", questId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var steps = new List<QuestStep>();
        while (await reader.ReadAsync(cancellationToken))
        {
            steps.Add(new QuestStep(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return steps;
    }

    private static async Task<IReadOnlyList<QuestTransition>> LoadTransitionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string questId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select transition_id, source_status, source_step_id, target_status, target_step_id, transition_order
            from quest_transitions
            where quest_id = @quest_id
            order by transition_order, transition_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("quest_id", questId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var transitions = new List<QuestTransition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            transitions.Add(new QuestTransition(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5)));
        }

        return transitions;
    }

    private static async Task InsertRootAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string questId,
        QuestDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into quest_definitions (quest_id, display_name, publication_state, schema_version, created_at_utc, updated_at_utc)
            values (@quest_id, @display_name, 'Draft', @schema_version, now(), now());
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddRootParameters(command, questId, draft);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateRootAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string questId,
        QuestDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update quest_definitions
            set display_name = @display_name,
                publication_state = 'Draft',
                schema_version = @schema_version,
                updated_at_utc = now()
            where quest_id = @quest_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddRootParameters(command, questId, draft);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteChildrenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string questId,
        CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
        {
            "delete from quest_transitions where quest_id = @quest_id;",
            "delete from quest_steps where quest_id = @quest_id;"
        })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("quest_id", questId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceChildrenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string questId,
        QuestDraft draft,
        CancellationToken cancellationToken)
    {
        foreach (var step in draft.Steps)
        {
            await using var command = new NpgsqlCommand("""
                insert into quest_steps (quest_id, step_id, display_name, step_order)
                values (@quest_id, @step_id, @display_name, @step_order);
                """, connection, transaction);
            command.Parameters.AddWithValue("quest_id", questId);
            command.Parameters.AddWithValue("step_id", step.StepId);
            command.Parameters.AddWithValue("display_name", step.DisplayName);
            command.Parameters.AddWithValue("step_order", step.StepOrder);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var transition in draft.Transitions)
        {
            await using var command = new NpgsqlCommand("""
                insert into quest_transitions (
                    quest_id,
                    transition_id,
                    source_status,
                    source_step_id,
                    target_status,
                    target_step_id,
                    transition_order
                ) values (
                    @quest_id,
                    @transition_id,
                    @source_status,
                    @source_step_id,
                    @target_status,
                    @target_step_id,
                    @transition_order
                );
                """, connection, transaction);
            command.Parameters.AddWithValue("quest_id", questId);
            command.Parameters.AddWithValue("transition_id", transition.TransitionId);
            command.Parameters.AddWithValue("source_status", transition.SourceStatus);
            command.Parameters.Add("source_step_id", NpgsqlDbType.Text).Value = transition.SourceStepId is null ? DBNull.Value : transition.SourceStepId;
            command.Parameters.AddWithValue("target_status", transition.TargetStatus);
            command.Parameters.Add("target_step_id", NpgsqlDbType.Text).Value = transition.TargetStepId is null ? DBNull.Value : transition.TargetStepId;
            command.Parameters.AddWithValue("transition_order", transition.TransitionOrder);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddRootParameters(NpgsqlCommand command, string questId, QuestDraft draft)
    {
        command.Parameters.AddWithValue("quest_id", questId);
        command.Parameters.AddWithValue("display_name", draft.DisplayName);
        command.Parameters.AddWithValue("schema_version", draft.SchemaVersion);
    }

    private static void EnsureExpectedVersion(QuestDefinitionRecord? existing, DateTimeOffset? expectedUpdatedAtUtc, string questId)
    {
        if (existing is null)
        {
            if (expectedUpdatedAtUtc is not null)
            {
                throw new QuestDefinitionConcurrencyException(questId);
            }
            return;
        }

        if (expectedUpdatedAtUtc is null || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new QuestDefinitionConcurrencyException(questId);
        }
    }

    private static void EnsureReferenceSafePublication(
        string questId,
        string publicationState,
        QuestDefinitionRecord definition,
        QuestStateReferenceSummary references)
    {
        if (publicationState == "Disabled" && references.HasReferences)
        {
            throw new QuestDefinitionReferencedByStateException(questId, "disable", references);
        }

        if (publicationState != "Published")
        {
            return;
        }

        var stepIds = definition.Steps.Select(step => step.StepId).ToHashSet(StringComparer.Ordinal);
        var missingStepIds = references.ActiveStepIds
            .Where(stepId => !stepIds.Contains(stepId))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missingStepIds.Length > 0)
        {
            throw new QuestDefinitionMissingActiveStepException(questId, missingStepIds, references);
        }
    }

    private static void EnsureReferenceSafeDraftReplacement(
        string questId,
        QuestStateReferenceSummary references)
    {
        if (references.HasReferences)
        {
            throw new QuestDefinitionReferencedByStateException(questId, "save_draft", references);
        }
    }

    private static async Task LockQuestContentStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string questId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select pg_advisory_xact_lock(hashtextextended(@identity, 0));
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("identity", $"quest_content_state_v1|{questId}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> LoadPublishedDialogueReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string questId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select distinct d.dialogue_definition_id
            from dialogue_definitions d
            join (
                select dialogue_definition_id, quest_id
                from dialogue_entry_conditions
                where quest_id = @quest_id
                union all
                select dialogue_definition_id, quest_id
                from dialogue_choice_conditions
                where quest_id = @quest_id
                union all
                select dialogue_definition_id, quest_id
                from dialogue_choice_effects
                where quest_id = @quest_id
            ) condition on condition.dialogue_definition_id = d.dialogue_definition_id
            where d.publication_state = 'Published'
            order by d.dialogue_definition_id;
            """;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("quest_id", questId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var dialogueIds = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                dialogueIds.Add(reader.GetString(0));
            }

            return dialogueIds;
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return [];
        }
    }

    private static async Task EnsureNoPublishedDialogueReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string questId,
        string operation,
        CancellationToken cancellationToken)
    {
        var dialogueIds = await LoadPublishedDialogueReferencesAsync(
            connection,
            transaction,
            questId,
            cancellationToken);
        if (dialogueIds.Count > 0)
        {
            throw new QuestDefinitionReferencedByPublishedDialogueException(questId, operation, dialogueIds);
        }
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(column)).ToUniversalTime();
}

public sealed record QuestDefinitionRecord(
    string QuestId,
    string DisplayName,
    string PublicationState,
    int SchemaVersion,
    IReadOnlyList<QuestStep> Steps,
    IReadOnlyList<QuestTransition> Transitions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int StepCount,
    int TransitionCount);

public sealed class QuestDefinitionNotFoundException(string questId) : Exception($"Quest definition '{questId}' was not found.");
public sealed class QuestDefinitionConcurrencyException(string questId) : Exception($"Quest definition '{questId}' changed before the mutation could be applied.");
public sealed class QuestDefinitionDeleteRequiresDisabledException(string questId) : Exception($"Quest definition '{questId}' must be Disabled before deletion.");
public sealed class QuestDefinitionReferencedByStateException : Exception
{
    public QuestDefinitionReferencedByStateException(
        string questId,
        string operation,
        QuestStateReferenceSummary references)
        : base($"Quest definition '{questId}' cannot {operation} while persisted character quest state exists.")
    {
        QuestId = questId;
        Operation = operation;
        References = references;
    }

    public string QuestId { get; }
    public string Operation { get; }
    public QuestStateReferenceSummary References { get; }
}

public sealed class QuestDefinitionMissingActiveStepException : Exception
{
    public QuestDefinitionMissingActiveStepException(
        string questId,
        IReadOnlyList<string> missingStepIds,
        QuestStateReferenceSummary references)
        : base($"Quest definition '{questId}' cannot publish because active character quest state references missing step ids.")
    {
        QuestId = questId;
        MissingStepIds = missingStepIds;
        References = references;
    }

    public string QuestId { get; }
    public IReadOnlyList<string> MissingStepIds { get; }
    public QuestStateReferenceSummary References { get; }
}

public sealed class QuestDefinitionReferencedByPublishedDialogueException : Exception
{
    public QuestDefinitionReferencedByPublishedDialogueException(
        string questId,
        string operation,
        IReadOnlyList<string> dialogueDefinitionIds)
        : base($"Quest definition '{questId}' cannot {operation} while referenced by published dialogue conditions.")
    {
        QuestId = questId;
        Operation = operation;
        DialogueDefinitionIds = dialogueDefinitionIds;
    }

    public string QuestId { get; }
    public string Operation { get; }
    public IReadOnlyList<string> DialogueDefinitionIds { get; }
}
