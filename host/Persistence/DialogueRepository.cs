using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public interface IDialogueRepository
{
    Task<IReadOnlyList<DialogueDefinitionRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default);

    Task<DialogueDefinitionRecord?> LoadAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken = default);

    Task<DialogueDefinitionRecord?> LoadForUpdateAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken = default);

    Task<DialogueDefinitionRecord> InsertDraftAsync(
        string dialogueDefinitionId,
        DialogueDraft draft,
        CancellationToken cancellationToken = default);

    Task<DialogueDefinitionRecord> ReplaceDraftAsync(
        string dialogueDefinitionId,
        DialogueDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<DialogueDefinitionRecord> SetPublicationAsync(
        string dialogueDefinitionId,
        string publicationState,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string dialogueDefinitionId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<DialogueReferenceSummaryRecord> LoadNpcReferencesAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, DialogueQuestReferenceRecord>> LoadQuestReferencesAsync(
        IReadOnlyCollection<string> questIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, DialogueItemReferenceRecord>> LoadItemReferencesAsync(
        IReadOnlyCollection<string> itemIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DialogueQuestConditionOption>> LoadPublishedQuestConditionOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthoringOption>> LoadRuntimeItemConditionOptionsAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DialogueRepository : IDialogueRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public DialogueRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DialogueDefinitionRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                d.dialogue_definition_id,
                d.display_name,
                d.publication_state,
                d.schema_version,
                d.metadata_description,
                d.notes,
                d.created_at_utc,
                d.updated_at_utc,
                count(distinct ep.entry_id)::integer as entry_point_count,
                count(distinct n.node_id)::integer as node_count,
                (count(distinct (c.node_id, c.choice_id)) filter (where c.choice_id is not null))::integer as choice_count
            from dialogue_definitions d
            left join dialogue_entry_points ep on ep.dialogue_definition_id = d.dialogue_definition_id
            left join dialogue_nodes n on n.dialogue_definition_id = d.dialogue_definition_id
            left join dialogue_choices c on c.dialogue_definition_id = n.dialogue_definition_id
                and c.node_id = n.node_id
            where @search is null
               or d.dialogue_definition_id ilike '%' || @search || '%'
               or d.display_name ilike '%' || @search || '%'
            group by d.dialogue_definition_id
            order by d.display_name, d.dialogue_definition_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<DialogueDefinitionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new DialogueDefinitionRecord(
                reader.GetString(reader.GetOrdinal("dialogue_definition_id")),
                reader.GetString(reader.GetOrdinal("display_name")),
                reader.GetString(reader.GetOrdinal("publication_state")),
                reader.GetInt32(reader.GetOrdinal("schema_version")),
                [],
                [],
                ReadNullableString(reader, "metadata_description"),
                ReadNullableString(reader, "notes"),
                ReadUtc(reader, "created_at_utc"),
                ReadUtc(reader, "updated_at_utc"),
                reader.GetInt32(reader.GetOrdinal("entry_point_count")),
                reader.GetInt32(reader.GetOrdinal("node_count")),
                reader.GetInt32(reader.GetOrdinal("choice_count"))));
        }

        return records;
    }

    public async Task<DialogueDefinitionRecord?> LoadAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAsync(connection, null, dialogueDefinitionId, false, cancellationToken);
    }

    public async Task<DialogueDefinitionRecord?> LoadForUpdateAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var record = await LoadAsync(connection, transaction, dialogueDefinitionId, true, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<DialogueDefinitionRecord> InsertDraftAsync(
        string dialogueDefinitionId,
        DialogueDraft draft,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, dialogueDefinitionId, true, cancellationToken);
        if (existing is not null)
        {
            throw new DialogueDefinitionDuplicateException(dialogueDefinitionId);
        }

        await InsertRootAsync(connection, transaction, dialogueDefinitionId, draft, cancellationToken);
        await ReplaceChildrenAsync(connection, transaction, dialogueDefinitionId, draft, cancellationToken);
        var saved = await LoadAsync(connection, transaction, dialogueDefinitionId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved dialogue definition could not be reloaded inside its transaction.");
        if (!DialogueAuthoringService.EquivalentDraft(saved, draft) || saved.PublicationState != "Draft")
        {
            throw new InvalidOperationException("Saved dialogue definition failed transactional semantic verification.");
        }

        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<DialogueDefinitionRecord> ReplaceDraftAsync(
        string dialogueDefinitionId,
        DialogueDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, dialogueDefinitionId, true, cancellationToken);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, dialogueDefinitionId);
        if (existing is null)
        {
            await InsertRootAsync(connection, transaction, dialogueDefinitionId, draft, cancellationToken);
        }
        else
        {
            await UpdateRootAsync(connection, transaction, dialogueDefinitionId, draft, cancellationToken);
            await DeleteChildrenAsync(connection, transaction, dialogueDefinitionId, cancellationToken);
        }

        await ReplaceChildrenAsync(connection, transaction, dialogueDefinitionId, draft, cancellationToken);
        var saved = await LoadAsync(connection, transaction, dialogueDefinitionId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved dialogue definition could not be reloaded inside its transaction.");
        if (!DialogueAuthoringService.EquivalentDraft(saved, draft) || saved.PublicationState != "Draft")
        {
            throw new InvalidOperationException("Saved dialogue definition failed transactional semantic verification.");
        }

        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<DialogueDefinitionRecord> SetPublicationAsync(
        string dialogueDefinitionId,
        string publicationState,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, dialogueDefinitionId, true, cancellationToken)
            ?? throw new DialogueDefinitionNotFoundException(dialogueDefinitionId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, dialogueDefinitionId);

        const string sql = """
            update dialogue_definitions
            set publication_state = @publication_state,
                updated_at_utc = now()
            where dialogue_definition_id = @dialogue_definition_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
            command.Parameters.AddWithValue("publication_state", publicationState);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var saved = await LoadAsync(connection, transaction, dialogueDefinitionId, false, cancellationToken)
            ?? throw new InvalidOperationException("Dialogue publication change could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(
        string dialogueDefinitionId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAsync(connection, transaction, dialogueDefinitionId, true, cancellationToken)
            ?? throw new DialogueDefinitionNotFoundException(dialogueDefinitionId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, dialogueDefinitionId);
        if (existing.PublicationState != "Disabled")
        {
            throw new DialogueDefinitionDeleteRequiresDisabledException(dialogueDefinitionId);
        }

        const string sql = "delete from dialogue_definitions where dialogue_definition_id = @dialogue_definition_id;";
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DialogueReferenceSummaryRecord> LoadNpcReferencesAsync(
        string dialogueDefinitionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select npc_definition_id, publication_state
            from npc_definitions
            where default_dialogue_id = @dialogue_definition_id
            order by npc_definition_id;
            """;

        var sources = new List<string>();
        var published = 0;
        try
        {
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add("dialogue_definition_id", NpgsqlDbType.Text).Value = dialogueDefinitionId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var npcId = reader.GetString(0);
                var state = reader.GetString(1);
                if (state == "Published")
                {
                    published++;
                }
                sources.Add($"npc:{npcId}:{state}");
            }
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return new DialogueReferenceSummaryRecord(dialogueDefinitionId, 0, 0, [], false);
        }

        return new DialogueReferenceSummaryRecord(dialogueDefinitionId, sources.Count, published, sources, true);
    }

    public async Task<IReadOnlyDictionary<string, DialogueQuestReferenceRecord>> LoadQuestReferencesAsync(
        IReadOnlyCollection<string> questIds,
        CancellationToken cancellationToken = default)
    {
        if (questIds.Count == 0)
        {
            return new Dictionary<string, DialogueQuestReferenceRecord>(StringComparer.Ordinal);
        }

        const string sql = """
            select q.quest_id, q.publication_state, s.step_id
            from quest_definitions q
            left join quest_steps s on s.quest_id = q.quest_id
            where q.quest_id = any(@quest_ids)
            order by q.quest_id, s.step_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("quest_ids", questIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var builders = new Dictionary<string, DialogueQuestReferenceBuilder>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var questId = reader.GetString(0);
            if (!builders.TryGetValue(questId, out var builder))
            {
                builder = new DialogueQuestReferenceBuilder(questId, reader.GetString(1));
                builders[questId] = builder;
            }

            if (!reader.IsDBNull(2))
            {
                builder.StepIds.Add(reader.GetString(2));
            }
        }

        return builders.ToDictionary(
            pair => pair.Key,
            pair => new DialogueQuestReferenceRecord(
                pair.Value.QuestId,
                pair.Value.PublicationState,
                pair.Value.StepIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, DialogueItemReferenceRecord>> LoadItemReferencesAsync(
        IReadOnlyCollection<string> itemIds,
        CancellationToken cancellationToken = default)
    {
        if (itemIds.Count == 0)
        {
            return new Dictionary<string, DialogueItemReferenceRecord>(StringComparer.Ordinal);
        }

        const string sql = """
            select item_id, item_name, runtime_enabled
            from item_definitions
            where item_id = any(@item_ids)
            order by item_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_ids", itemIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new Dictionary<string, DialogueItemReferenceRecord>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            records[reader.GetString(0)] = new DialogueItemReferenceRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2));
        }

        return records;
    }

    public async Task<IReadOnlyList<DialogueQuestConditionOption>> LoadPublishedQuestConditionOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select q.quest_id, q.display_name, s.step_id, s.display_name
            from quest_definitions q
            left join quest_steps s on s.quest_id = q.quest_id
            where q.publication_state = 'Published'
            order by q.display_name, q.quest_id, s.step_order, s.step_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var builders = new Dictionary<string, DialogueQuestConditionOptionBuilder>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var questId = reader.GetString(0);
            if (!builders.TryGetValue(questId, out var builder))
            {
                builder = new DialogueQuestConditionOptionBuilder(questId, reader.GetString(1));
                builders[questId] = builder;
            }

            if (!reader.IsDBNull(2))
            {
                builder.Steps.Add(new AuthoringOption(reader.GetString(2), reader.GetString(3)));
            }
        }

        return builders.Values
            .Select(builder => new DialogueQuestConditionOption(builder.QuestId, builder.DisplayName, builder.Steps))
            .ToArray();
    }

    public async Task<IReadOnlyList<AuthoringOption>> LoadRuntimeItemConditionOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select item_id, item_name
            from item_definitions
            where runtime_enabled = true
            order by item_name, item_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var options = new List<AuthoringOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            options.Add(new AuthoringOption(reader.GetString(0), reader.GetString(1)));
        }

        return options;
    }

    private static async Task<DialogueDefinitionRecord?> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string dialogueDefinitionId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var rootSql = """
            select
                dialogue_definition_id,
                display_name,
                publication_state,
                schema_version,
                metadata_description,
                notes,
                created_at_utc,
                updated_at_utc
            from dialogue_definitions
            where dialogue_definition_id = @dialogue_definition_id
            """ + (forUpdate ? " for update;" : ";");

        DialogueDefinitionRecord? root;
        await using (var command = new NpgsqlCommand(rootSql, connection, transaction))
        {
            command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            root = new DialogueDefinitionRecord(
                reader.GetString(reader.GetOrdinal("dialogue_definition_id")),
                reader.GetString(reader.GetOrdinal("display_name")),
                reader.GetString(reader.GetOrdinal("publication_state")),
                reader.GetInt32(reader.GetOrdinal("schema_version")),
                [],
                [],
                ReadNullableString(reader, "metadata_description"),
                ReadNullableString(reader, "notes"),
                ReadUtc(reader, "created_at_utc"),
                ReadUtc(reader, "updated_at_utc"),
                0,
                0,
                0);
        }

        var entryPoints = await LoadEntryPointsAsync(connection, transaction, dialogueDefinitionId, cancellationToken);
        var nodes = await LoadNodesAsync(connection, transaction, dialogueDefinitionId, cancellationToken);
        var entryConditions = await LoadEntryConditionsAsync(connection, transaction, dialogueDefinitionId, cancellationToken);
        var choiceConditions = await LoadChoiceConditionsAsync(connection, transaction, dialogueDefinitionId, cancellationToken);
        return root with
        {
            EntryPoints = entryPoints
                .Select(entry => entry with
                {
                    Conditions = entryConditions.TryGetValue(entry.EntryId, out var conditions) ? conditions : []
                })
                .ToArray(),
            Nodes = nodes
                .Select(node => node with
                {
                    Choices = node.Choices
                        .Select(choice => choice with
                        {
                            Conditions = choiceConditions.TryGetValue((node.NodeId, choice.ChoiceId), out var conditions) ? conditions : []
                        })
                        .ToArray()
                })
                .ToArray(),
            EntryPointCount = entryPoints.Count,
            NodeCount = nodes.Count,
            ChoiceCount = nodes.Sum(node => node.Choices.Count)
        };
    }

    private static async Task<IReadOnlyList<DialogueEntryPoint>> LoadEntryPointsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string dialogueDefinitionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select entry_id, node_id, priority, entry_order
            from dialogue_entry_points
            where dialogue_definition_id = @dialogue_definition_id
            order by priority desc, entry_order, entry_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entryPoints = new List<DialogueEntryPoint>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entryPoints.Add(new DialogueEntryPoint(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                []));
        }

        return entryPoints;
    }

    private static async Task<IReadOnlyList<DialogueNode>> LoadNodesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string dialogueDefinitionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select node_id, node_type, speaker, text, next_node_id, dismissible, canvas_x, canvas_y, editor_notes
            from dialogue_nodes
            where dialogue_definition_id = @dialogue_definition_id
            order by node_order, node_id;
            """;
        var nodes = new List<DialogueNode>();
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                nodes.Add(new DialogueNode(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetBoolean(5),
                    reader.GetDouble(6),
                    reader.GetDouble(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    []));
            }
        }

        var choices = await LoadChoicesAsync(connection, transaction, dialogueDefinitionId, cancellationToken);
        return nodes
            .Select(node => node with
            {
                Choices = choices.TryGetValue(node.NodeId, out var nodeChoices) ? nodeChoices : []
            })
            .ToArray();
    }

    private static async Task<Dictionary<string, IReadOnlyList<DialogueChoice>>> LoadChoicesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string dialogueDefinitionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select node_id, choice_id, text, target_node_id, choice_order
            from dialogue_choices
            where dialogue_definition_id = @dialogue_definition_id
            order by node_id, choice_order, choice_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var choices = new Dictionary<string, List<DialogueChoice>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var nodeId = reader.GetString(0);
            if (!choices.TryGetValue(nodeId, out var nodeChoices))
            {
                nodeChoices = [];
                choices[nodeId] = nodeChoices;
            }

            nodeChoices.Add(new DialogueChoice(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                []));
        }

        return choices.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<DialogueChoice>)pair.Value,
            StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, IReadOnlyList<DialogueCondition>>> LoadEntryConditionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string dialogueDefinitionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select entry_id, condition_type, quest_id, quest_status, quest_step_id, item_id, item_quantity
            from dialogue_entry_conditions
            where dialogue_definition_id = @dialogue_definition_id
            order by entry_id, condition_order;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var conditions = new Dictionary<string, List<DialogueCondition>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entryId = reader.GetString(0);
            if (!conditions.TryGetValue(entryId, out var entryConditions))
            {
                entryConditions = [];
                conditions[entryId] = entryConditions;
            }

            entryConditions.Add(ReadCondition(reader, offset: 1));
        }

        return conditions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<DialogueCondition>)pair.Value,
            StringComparer.Ordinal);
    }

    private static async Task<Dictionary<(string NodeId, string ChoiceId), IReadOnlyList<DialogueCondition>>> LoadChoiceConditionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string dialogueDefinitionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select node_id, choice_id, condition_type, quest_id, quest_status, quest_step_id, item_id, item_quantity
            from dialogue_choice_conditions
            where dialogue_definition_id = @dialogue_definition_id
            order by node_id, choice_id, condition_order;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var conditions = new Dictionary<(string NodeId, string ChoiceId), List<DialogueCondition>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (reader.GetString(0), reader.GetString(1));
            if (!conditions.TryGetValue(key, out var choiceConditions))
            {
                choiceConditions = [];
                conditions[key] = choiceConditions;
            }

            choiceConditions.Add(ReadCondition(reader, offset: 2));
        }

        return conditions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<DialogueCondition>)pair.Value);
    }

    private static DialogueCondition ReadCondition(
        NpgsqlDataReader reader,
        int offset) =>
        new(
            reader.GetString(offset),
            reader.IsDBNull(offset + 1) ? null : reader.GetString(offset + 1),
            reader.IsDBNull(offset + 2) ? null : reader.GetString(offset + 2),
            reader.IsDBNull(offset + 3) ? null : reader.GetString(offset + 3),
            reader.IsDBNull(offset + 4) ? null : reader.GetString(offset + 4),
            reader.IsDBNull(offset + 5) ? null : reader.GetInt32(offset + 5));

    private static async Task InsertRootAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        DialogueDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into dialogue_definitions (
                dialogue_definition_id,
                display_name,
                publication_state,
                schema_version,
                metadata_description,
                notes,
                created_at_utc,
                updated_at_utc
            ) values (
                @dialogue_definition_id,
                @display_name,
                'Draft',
                @schema_version,
                @metadata_description,
                @notes,
                now(),
                now()
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddRootParameters(command, dialogueDefinitionId, draft);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateRootAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        DialogueDraft draft,
        CancellationToken cancellationToken)
    {
        const string sql = """
            update dialogue_definitions
            set display_name = @display_name,
                publication_state = 'Draft',
                schema_version = @schema_version,
                metadata_description = @metadata_description,
                notes = @notes,
                updated_at_utc = now()
            where dialogue_definition_id = @dialogue_definition_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddRootParameters(command, dialogueDefinitionId, draft);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException($"Expected to update one dialogue definition '{dialogueDefinitionId}', but updated {affected} rows.");
        }
    }

    private static async Task DeleteChildrenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
        {
            "delete from dialogue_choices where dialogue_definition_id = @dialogue_definition_id;",
            "delete from dialogue_entry_conditions where dialogue_definition_id = @dialogue_definition_id;",
            "delete from dialogue_choice_conditions where dialogue_definition_id = @dialogue_definition_id;",
            "delete from dialogue_entry_points where dialogue_definition_id = @dialogue_definition_id;",
            "delete from dialogue_nodes where dialogue_definition_id = @dialogue_definition_id;"
        })
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceChildrenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        DialogueDraft draft,
        CancellationToken cancellationToken)
    {
        var nodeOrder = 0;
        foreach (var node in draft.Nodes)
        {
            await InsertNodeAsync(connection, transaction, dialogueDefinitionId, node, nodeOrder++, cancellationToken);
        }
        foreach (var node in draft.Nodes)
        {
            foreach (var choice in node.Choices)
            {
                await InsertChoiceAsync(connection, transaction, dialogueDefinitionId, node.NodeId, choice, cancellationToken);
                await InsertChoiceConditionsAsync(connection, transaction, dialogueDefinitionId, node.NodeId, choice, cancellationToken);
            }
        }
        foreach (var entryPoint in draft.EntryPoints)
        {
            await InsertEntryPointAsync(connection, transaction, dialogueDefinitionId, entryPoint, cancellationToken);
            await InsertEntryConditionsAsync(connection, transaction, dialogueDefinitionId, entryPoint, cancellationToken);
        }
    }

    private static async Task InsertNodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        DialogueNode node,
        int nodeOrder,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into dialogue_nodes (
                dialogue_definition_id,
                node_id,
                node_type,
                speaker,
                text,
                next_node_id,
                dismissible,
                canvas_x,
                canvas_y,
                editor_notes,
                node_order
            ) values (
                @dialogue_definition_id,
                @node_id,
                @node_type,
                @speaker,
                @text,
                @next_node_id,
                @dismissible,
                @canvas_x,
                @canvas_y,
                @editor_notes,
                @node_order
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        command.Parameters.AddWithValue("node_id", node.NodeId);
        command.Parameters.AddWithValue("node_type", node.NodeType);
        command.Parameters.Add("speaker", NpgsqlDbType.Text).Value = (object?)node.Speaker ?? DBNull.Value;
        command.Parameters.Add("text", NpgsqlDbType.Text).Value = (object?)node.Text ?? DBNull.Value;
        command.Parameters.Add("next_node_id", NpgsqlDbType.Text).Value = (object?)node.NextNodeId ?? DBNull.Value;
        command.Parameters.AddWithValue("dismissible", node.Dismissible);
        command.Parameters.AddWithValue("canvas_x", node.CanvasX);
        command.Parameters.AddWithValue("canvas_y", node.CanvasY);
        command.Parameters.Add("editor_notes", NpgsqlDbType.Text).Value = (object?)node.EditorNotes ?? DBNull.Value;
        command.Parameters.AddWithValue("node_order", nodeOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertChoiceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        string nodeId,
        DialogueChoice choice,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into dialogue_choices (
                dialogue_definition_id,
                node_id,
                choice_id,
                text,
                target_node_id,
                choice_order
            ) values (
                @dialogue_definition_id,
                @node_id,
                @choice_id,
                @text,
                @target_node_id,
                @choice_order
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        command.Parameters.AddWithValue("node_id", nodeId);
        command.Parameters.AddWithValue("choice_id", choice.ChoiceId);
        command.Parameters.AddWithValue("text", choice.Text);
        command.Parameters.AddWithValue("target_node_id", choice.TargetNodeId);
        command.Parameters.AddWithValue("choice_order", choice.ChoiceOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEntryPointAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        DialogueEntryPoint entryPoint,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into dialogue_entry_points (
                dialogue_definition_id,
                entry_id,
                node_id,
                priority,
                entry_order
            ) values (
                @dialogue_definition_id,
                @entry_id,
                @node_id,
                @priority,
                @entry_order
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        command.Parameters.AddWithValue("entry_id", entryPoint.EntryId);
        command.Parameters.AddWithValue("node_id", entryPoint.NodeId);
        command.Parameters.AddWithValue("priority", entryPoint.Priority);
        command.Parameters.AddWithValue("entry_order", entryPoint.EntryOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEntryConditionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        DialogueEntryPoint entryPoint,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < entryPoint.Conditions.Count; index++)
        {
            await InsertConditionAsync(
                connection,
                transaction,
                """
                insert into dialogue_entry_conditions (
                    dialogue_definition_id,
                    entry_id,
                    condition_order,
                    condition_type,
                    quest_id,
                    quest_status,
                    quest_step_id,
                    item_id,
                    item_quantity
                ) values (
                    @dialogue_definition_id,
                    @entry_id,
                    @condition_order,
                    @condition_type,
                    @quest_id,
                    @quest_status,
                    @quest_step_id,
                    @item_id,
                    @item_quantity
                );
                """,
                dialogueDefinitionId,
                entryPoint.EntryId,
                null,
                null,
                index,
                entryPoint.Conditions[index],
                cancellationToken);
        }
    }

    private static async Task InsertChoiceConditionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string dialogueDefinitionId,
        string nodeId,
        DialogueChoice choice,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < choice.Conditions.Count; index++)
        {
            await InsertConditionAsync(
                connection,
                transaction,
                """
                insert into dialogue_choice_conditions (
                    dialogue_definition_id,
                    node_id,
                    choice_id,
                    condition_order,
                    condition_type,
                    quest_id,
                    quest_status,
                    quest_step_id,
                    item_id,
                    item_quantity
                ) values (
                    @dialogue_definition_id,
                    @node_id,
                    @choice_id,
                    @condition_order,
                    @condition_type,
                    @quest_id,
                    @quest_status,
                    @quest_step_id,
                    @item_id,
                    @item_quantity
                );
                """,
                dialogueDefinitionId,
                null,
                nodeId,
                choice.ChoiceId,
                index,
                choice.Conditions[index],
                cancellationToken);
        }
    }

    private static async Task InsertConditionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        string dialogueDefinitionId,
        string? entryId,
        string? nodeId,
        string? choiceId,
        int conditionOrder,
        DialogueCondition condition,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        if (entryId is not null)
        {
            command.Parameters.AddWithValue("entry_id", entryId);
        }
        if (nodeId is not null)
        {
            command.Parameters.AddWithValue("node_id", nodeId);
        }
        if (choiceId is not null)
        {
            command.Parameters.AddWithValue("choice_id", choiceId);
        }
        command.Parameters.AddWithValue("condition_order", conditionOrder);
        command.Parameters.AddWithValue("condition_type", condition.ConditionType);
        command.Parameters.Add("quest_id", NpgsqlDbType.Text).Value = (object?)condition.QuestId ?? DBNull.Value;
        command.Parameters.Add("quest_status", NpgsqlDbType.Text).Value = (object?)condition.Status ?? DBNull.Value;
        command.Parameters.Add("quest_step_id", NpgsqlDbType.Text).Value = (object?)condition.StepId ?? DBNull.Value;
        command.Parameters.Add("item_id", NpgsqlDbType.Text).Value = (object?)condition.ItemId ?? DBNull.Value;
        command.Parameters.Add("item_quantity", NpgsqlDbType.Integer).Value = (object?)condition.Quantity ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddRootParameters(
        NpgsqlCommand command,
        string dialogueDefinitionId,
        DialogueDraft draft)
    {
        command.Parameters.AddWithValue("dialogue_definition_id", dialogueDefinitionId);
        command.Parameters.AddWithValue("display_name", draft.DisplayName);
        command.Parameters.AddWithValue("schema_version", draft.SchemaVersion);
        command.Parameters.Add("metadata_description", NpgsqlDbType.Text).Value =
            (object?)draft.MetadataDescription ?? DBNull.Value;
        command.Parameters.Add("notes", NpgsqlDbType.Text).Value =
            (object?)draft.Notes ?? DBNull.Value;
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(reader.GetOrdinal(column)), DateTimeKind.Utc));

    private static void EnsureExpectedVersion(
        DialogueDefinitionRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc,
        string dialogueDefinitionId)
    {
        if (existing is null)
        {
            if (expectedUpdatedAtUtc is not null)
            {
                throw new DialogueDefinitionConcurrencyException(dialogueDefinitionId, null);
            }

            return;
        }

        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new DialogueDefinitionConcurrencyException(existing.DialogueDefinitionId, existing.UpdatedAtUtc);
        }
    }
}

public sealed record DialogueDefinitionRecord(
    string DialogueDefinitionId,
    string DisplayName,
    string PublicationState,
    int SchemaVersion,
    IReadOnlyList<DialogueEntryPoint> EntryPoints,
    IReadOnlyList<DialogueNode> Nodes,
    string? MetadataDescription,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int EntryPointCount,
    int NodeCount,
    int ChoiceCount);

public sealed record DialogueReferenceSummaryRecord(
    string DialogueDefinitionId,
    int KnownReferenceCount,
    int PublishedReferenceCount,
    IReadOnlyList<string> ReferenceSources,
    bool ReferenceCheckComplete);

public sealed record DialogueQuestReferenceRecord(
    string QuestId,
    string PublicationState,
    IReadOnlyList<string> StepIds);

public sealed record DialogueItemReferenceRecord(
    string ItemId,
    string DisplayName,
    bool RuntimeEnabled);

internal sealed class DialogueQuestReferenceBuilder
{
    public DialogueQuestReferenceBuilder(string questId, string publicationState)
    {
        QuestId = questId;
        PublicationState = publicationState;
    }

    public string QuestId { get; }

    public string PublicationState { get; }

    public List<string> StepIds { get; } = [];
}

internal sealed class DialogueQuestConditionOptionBuilder
{
    public DialogueQuestConditionOptionBuilder(string questId, string displayName)
    {
        QuestId = questId;
        DisplayName = displayName;
    }

    public string QuestId { get; }

    public string DisplayName { get; }

    public List<AuthoringOption> Steps { get; } = [];
}

public sealed class DialogueDefinitionNotFoundException : Exception
{
    public DialogueDefinitionNotFoundException(string dialogueDefinitionId)
        : base($"Dialogue definition '{dialogueDefinitionId}' does not exist.")
    {
    }
}

public sealed class DialogueDefinitionDuplicateException : Exception
{
    public DialogueDefinitionDuplicateException(string dialogueDefinitionId)
        : base($"Dialogue definition '{dialogueDefinitionId}' already exists.")
    {
    }
}

public sealed class DialogueDefinitionConcurrencyException : Exception
{
    public DialogueDefinitionConcurrencyException(
        string dialogueDefinitionId,
        DateTimeOffset? currentUpdatedAtUtc)
        : base($"Dialogue definition '{dialogueDefinitionId}' changed after it was loaded. Reload it before saving.")
    {
        CurrentUpdatedAtUtc = currentUpdatedAtUtc;
    }

    public DateTimeOffset? CurrentUpdatedAtUtc { get; }
}

public sealed class DialogueDefinitionDeleteRequiresDisabledException : Exception
{
    public DialogueDefinitionDeleteRequiresDisabledException(string dialogueDefinitionId)
        : base($"Dialogue definition '{dialogueDefinitionId}' must be disabled before deletion.")
    {
    }
}
