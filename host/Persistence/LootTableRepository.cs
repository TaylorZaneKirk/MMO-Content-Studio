using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public interface ILootTableRepository
{
    Task<IReadOnlyList<LootTableRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default);

    Task<LootTableRecord?> LoadAsync(
        string lootTableId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LootItemRecord>> LoadItemsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LootTableOptionRecord>> LoadTableOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LootMobBindingRecord>> LoadMobBindingsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> HasPublishedDependentsAsync(
        string lootTableId,
        CancellationToken cancellationToken = default);

    Task<LootTableRecord> SaveDraftAsync(
        string lootTableId,
        NormalizedLootTableDraft draft,
        string contentFingerprint,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<LootTableRecord> SetPublicationAsync(
        string lootTableId,
        string publicationState,
        string contentFingerprint,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string lootTableId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class LootTableRepository : ILootTableRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public LootTableRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<LootTableRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                t.loot_table_id,
                t.display_name,
                t.description,
                t.publication_state,
                t.content_fingerprint,
                t.updated_at,
                (
                    select count(*)::int
                    from loot_table_roll_groups g
                    where g.loot_table_id = t.loot_table_id
                ) as group_count,
                (
                    select count(*)::int
                    from loot_table_outcomes o
                    where o.loot_table_id = t.loot_table_id
                ) as outcome_count
            from loot_tables t
            where @search is null
               or t.loot_table_id ilike '%' || @search || '%'
               or t.display_name ilike '%' || @search || '%'
            order by t.display_name, t.loot_table_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<LootTableRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadTableRecord(reader, []));
        }

        return records;
    }

    public async Task<LootTableRecord?> LoadAsync(
        string lootTableId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await LoadAggregateAsync(connection, null, lootTableId, false, cancellationToken);
    }

    public async Task<IReadOnlyList<LootItemRecord>> LoadItemsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select item_id, item_name, runtime_enabled, reference_value
            from item_definitions
            order by item_name, item_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<LootItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new LootItemRecord(
                reader.GetString(reader.GetOrdinal("item_id")),
                reader.GetString(reader.GetOrdinal("item_name")),
                reader.GetBoolean(reader.GetOrdinal("runtime_enabled")),
                reader.GetInt64(reader.GetOrdinal("reference_value"))));
        }

        return records;
    }

    public async Task<IReadOnlyList<LootTableOptionRecord>> LoadTableOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select loot_table_id, display_name, publication_state
            from loot_tables
            order by display_name, loot_table_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<LootTableOptionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new LootTableOptionRecord(
                reader.GetString(reader.GetOrdinal("loot_table_id")),
                reader.GetString(reader.GetOrdinal("display_name")),
                reader.GetString(reader.GetOrdinal("publication_state"))));
        }

        return records;
    }

    public async Task<IReadOnlyList<LootMobBindingRecord>> LoadMobBindingsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                m.mob_definition_id,
                m.publication_state,
                m.root_loot_table_id,
                (
                    select count(*)::int
                    from mob_drops d
                    where d.mob_definition_id = m.mob_definition_id
                ) as legacy_guaranteed_drop_count
            from mob_definitions m
            order by m.mob_definition_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<LootMobBindingRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new LootMobBindingRecord(
                reader.GetString(reader.GetOrdinal("mob_definition_id")),
                reader.GetString(reader.GetOrdinal("publication_state")),
                ReadNullableString(reader, "root_loot_table_id"),
                reader.GetInt32(reader.GetOrdinal("legacy_guaranteed_drop_count"))));
        }

        return records;
    }

    public async Task<bool> HasPublishedDependentsAsync(
        string lootTableId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select exists (
                select 1
                from loot_table_outcomes o
                join loot_tables parent on parent.loot_table_id = o.loot_table_id
                where o.nested_loot_table_id = @loot_table_id
                  and parent.publication_state = 'Published'
                union all
                select 1
                from mob_definitions m
                where m.root_loot_table_id = @loot_table_id
                  and m.publication_state = 'Published'
            );
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("loot_table_id", lootTableId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<LootTableRecord> SaveDraftAsync(
        string lootTableId,
        NormalizedLootTableDraft draft,
        string contentFingerprint,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, lootTableId, true, cancellationToken);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, lootTableId);

        await UpsertTableAsync(connection, transaction, lootTableId, draft, LootTableDomainRules.Draft, contentFingerprint, cancellationToken);
        await ReplaceGroupsAsync(connection, transaction, lootTableId, draft.Groups, cancellationToken);

        var saved = await LoadAggregateAsync(connection, transaction, lootTableId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved loot table could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<LootTableRecord> SetPublicationAsync(
        string lootTableId,
        string publicationState,
        string contentFingerprint,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, lootTableId, true, cancellationToken)
            ?? throw new LootTableNotFoundException(lootTableId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, lootTableId);

        const string sql = """
            update loot_tables
            set publication_state = @publication_state,
                content_fingerprint = @content_fingerprint,
                updated_at = now()
            where loot_table_id = @loot_table_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("loot_table_id", lootTableId);
            command.Parameters.AddWithValue("publication_state", publicationState);
            command.Parameters.AddWithValue("content_fingerprint", contentFingerprint);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var saved = await LoadAggregateAsync(connection, transaction, lootTableId, false, cancellationToken)
            ?? throw new InvalidOperationException("Loot table publication change could not be reloaded inside its transaction.");
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(
        string lootTableId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadAggregateAsync(connection, transaction, lootTableId, true, cancellationToken)
            ?? throw new LootTableNotFoundException(lootTableId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc, lootTableId);

        const string sql = "delete from loot_tables where loot_table_id = @loot_table_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("loot_table_id", lootTableId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<LootTableRecord?> LoadAggregateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string lootTableId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            select
                t.loot_table_id,
                t.display_name,
                t.description,
                t.publication_state,
                t.content_fingerprint,
                t.updated_at,
                (
                    select count(*)::int
                    from loot_table_roll_groups g
                    where g.loot_table_id = t.loot_table_id
                ) as group_count,
                (
                    select count(*)::int
                    from loot_table_outcomes o
                    where o.loot_table_id = t.loot_table_id
                ) as outcome_count
            from loot_tables t
            where t.loot_table_id = @loot_table_id
            """ + (forUpdate ? " for update of t;" : ";");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("loot_table_id", lootTableId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var table = ReadTableRecord(reader, []);
        await reader.DisposeAsync();
        return table with
        {
            Groups = await LoadGroupsAsync(connection, transaction, lootTableId, cancellationToken)
        };
    }

    private static async Task<IReadOnlyList<LootRollGroupRecord>> LoadGroupsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string lootTableId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select roll_group_id, section_kind, group_order, roll_kind, roll_count,
                pre_roll_failure_behavior, pre_roll_success_sequence_behavior,
                pre_roll_success_main_behavior, display_name
            from loot_table_roll_groups
            where loot_table_id = @loot_table_id
            order by section_kind, group_order, roll_group_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("loot_table_id", lootTableId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var groups = new List<LootRollGroupRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            groups.Add(new LootRollGroupRecord(
                reader.GetString(reader.GetOrdinal("roll_group_id")),
                reader.GetInt32(reader.GetOrdinal("group_order")),
                reader.GetString(reader.GetOrdinal("section_kind")),
                reader.GetString(reader.GetOrdinal("roll_kind")),
                reader.GetInt32(reader.GetOrdinal("roll_count")),
                ReadNullableString(reader, "pre_roll_failure_behavior"),
                ReadNullableString(reader, "pre_roll_success_sequence_behavior"),
                ReadNullableString(reader, "pre_roll_success_main_behavior"),
                ReadNullableString(reader, "display_name"),
                []));
        }

        await reader.DisposeAsync();
        var result = new List<LootRollGroupRecord>(groups.Count);
        foreach (var group in groups)
        {
            result.Add(group with
            {
                Outcomes = await LoadOutcomesAsync(connection, transaction, lootTableId, group.RollGroupId, cancellationToken)
            });
        }

        return result
            .OrderBy(group => LootTableDomainRules.SectionSort(group.SectionKind))
            .ThenBy(group => group.Order)
            .ThenBy(group => group.RollGroupId, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<IReadOnlyList<LootOutcomeRecord>> LoadOutcomesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string lootTableId,
        string rollGroupId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select o.outcome_id, o.outcome_order, o.outcome_kind, o.item_id,
                i.item_name, o.nested_loot_table_id, o.min_quantity, o.max_quantity,
                o.weight, o.probability_numerator, o.probability_denominator
            from loot_table_outcomes o
            left join item_definitions i on i.item_id = o.item_id
            where o.loot_table_id = @loot_table_id
              and o.roll_group_id = @roll_group_id
            order by o.outcome_order, o.outcome_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("loot_table_id", lootTableId);
        command.Parameters.AddWithValue("roll_group_id", rollGroupId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var outcomes = new List<LootOutcomeRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            outcomes.Add(new LootOutcomeRecord(
                reader.GetString(reader.GetOrdinal("outcome_id")),
                reader.GetInt32(reader.GetOrdinal("outcome_order")),
                reader.GetString(reader.GetOrdinal("outcome_kind")),
                ReadNullableString(reader, "item_id"),
                ReadNullableString(reader, "item_name"),
                ReadNullableString(reader, "nested_loot_table_id"),
                ReadNullableInt32(reader, "min_quantity"),
                ReadNullableInt32(reader, "max_quantity"),
                ReadNullableInt32(reader, "weight"),
                ReadNullableInt64(reader, "probability_numerator"),
                ReadNullableInt64(reader, "probability_denominator")));
        }

        return outcomes;
    }

    private static async Task UpsertTableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string lootTableId,
        NormalizedLootTableDraft draft,
        string publicationState,
        string contentFingerprint,
        CancellationToken cancellationToken)
    {
        const string sql = """
            insert into loot_tables (
                loot_table_id, display_name, description, publication_state,
                content_fingerprint, created_at, updated_at
            ) values (
                @loot_table_id, @display_name, @description, @publication_state,
                @content_fingerprint, now(), now()
            )
            on conflict (loot_table_id) do update set
                display_name = excluded.display_name,
                description = excluded.description,
                publication_state = excluded.publication_state,
                content_fingerprint = excluded.content_fingerprint,
                updated_at = now();
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("loot_table_id", lootTableId);
        command.Parameters.AddWithValue("display_name", draft.DisplayName);
        command.Parameters.AddWithValue("description", draft.Description);
        command.Parameters.AddWithValue("publication_state", publicationState);
        command.Parameters.AddWithValue("content_fingerprint", contentFingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceGroupsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string lootTableId,
        IReadOnlyList<NormalizedLootRollGroup> groups,
        CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand("delete from loot_table_roll_groups where loot_table_id = @loot_table_id;", connection, transaction))
        {
            delete.Parameters.AddWithValue("loot_table_id", lootTableId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        const string groupSql = """
            insert into loot_table_roll_groups (
                loot_table_id, roll_group_id, section_kind, group_order, roll_kind,
                roll_count, pre_roll_failure_behavior, pre_roll_success_sequence_behavior,
                pre_roll_success_main_behavior, display_name, created_at, updated_at
            ) values (
                @loot_table_id, @roll_group_id, @section_kind, @group_order, @roll_kind,
                @roll_count, @pre_roll_failure_behavior, @pre_roll_success_sequence_behavior,
                @pre_roll_success_main_behavior, @display_name, now(), now()
            );
            """;
        const string outcomeSql = """
            insert into loot_table_outcomes (
                loot_table_id, roll_group_id, outcome_id, outcome_order, outcome_kind,
                item_id, nested_loot_table_id, min_quantity, max_quantity, weight,
                probability_numerator, probability_denominator, created_at, updated_at
            ) values (
                @loot_table_id, @roll_group_id, @outcome_id, @outcome_order, @outcome_kind,
                @item_id, @nested_loot_table_id, @min_quantity, @max_quantity, @weight,
                @probability_numerator, @probability_denominator, now(), now()
            );
            """;

        foreach (var group in groups)
        {
            await using (var command = new NpgsqlCommand(groupSql, connection, transaction))
            {
                command.Parameters.AddWithValue("loot_table_id", lootTableId);
                command.Parameters.AddWithValue("roll_group_id", group.RollGroupId);
                command.Parameters.AddWithValue("section_kind", group.SectionKind);
                command.Parameters.AddWithValue("group_order", group.Order);
                command.Parameters.AddWithValue("roll_kind", group.RollKind);
                command.Parameters.AddWithValue("roll_count", group.RollCount);
                AddNullableText(command, "pre_roll_failure_behavior", group.PreRollFailureBehavior);
                AddNullableText(command, "pre_roll_success_sequence_behavior", group.PreRollSuccessSequenceBehavior);
                AddNullableText(command, "pre_roll_success_main_behavior", group.PreRollSuccessMainBehavior);
                AddNullableText(command, "display_name", group.DisplayName);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var outcome in group.Outcomes)
            {
                await using var command = new NpgsqlCommand(outcomeSql, connection, transaction);
                command.Parameters.AddWithValue("loot_table_id", lootTableId);
                command.Parameters.AddWithValue("roll_group_id", group.RollGroupId);
                command.Parameters.AddWithValue("outcome_id", outcome.OutcomeId);
                command.Parameters.AddWithValue("outcome_order", outcome.Order);
                command.Parameters.AddWithValue("outcome_kind", outcome.OutcomeKind);
                AddNullableText(command, "item_id", outcome.ItemId);
                AddNullableText(command, "nested_loot_table_id", outcome.NestedLootTableId);
                AddNullableInt32(command, "min_quantity", outcome.MinQuantity);
                AddNullableInt32(command, "max_quantity", outcome.MaxQuantity);
                AddNullableInt32(command, "weight", outcome.Weight);
                AddNullableInt64(command, "probability_numerator", outcome.ProbabilityNumerator);
                AddNullableInt64(command, "probability_denominator", outcome.ProbabilityDenominator);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static LootTableRecord ReadTableRecord(
        NpgsqlDataReader reader,
        IReadOnlyList<LootRollGroupRecord> groups) =>
        new(
            reader.GetString(reader.GetOrdinal("loot_table_id")),
            reader.GetString(reader.GetOrdinal("display_name")),
            reader.GetString(reader.GetOrdinal("description")),
            reader.GetString(reader.GetOrdinal("publication_state")),
            ReadNullableString(reader, "content_fingerprint"),
            groups,
            reader.GetInt32(reader.GetOrdinal("group_count")),
            reader.GetInt32(reader.GetOrdinal("outcome_count")),
            ReadUtc(reader, "updated_at"));

    private static void EnsureExpectedVersion(
        LootTableRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc,
        string lootTableId)
    {
        if (existing is null)
        {
            if (expectedUpdatedAtUtc is not null)
            {
                throw new LootTableConcurrencyException(lootTableId, null);
            }

            return;
        }

        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new LootTableConcurrencyException(lootTableId, existing.UpdatedAtUtc);
        }
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Text).Value = (object?)value ?? DBNull.Value;

    private static void AddNullableInt32(NpgsqlCommand command, string name, int? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Integer).Value = (object?)value ?? DBNull.Value;

    private static void AddNullableInt64(NpgsqlCommand command, string name, long? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Bigint).Value = (object?)value ?? DBNull.Value;

    private static string? ReadNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt32(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? ReadNullableInt64(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(reader.GetOrdinal(column)), DateTimeKind.Utc));
}

public sealed record LootTableRecord(
    string LootTableId,
    string DisplayName,
    string Description,
    string PublicationState,
    string? ContentFingerprint,
    IReadOnlyList<LootRollGroupRecord> Groups,
    int GroupCount,
    int OutcomeCount,
    DateTimeOffset UpdatedAtUtc);

public sealed record LootRollGroupRecord(
    string RollGroupId,
    int Order,
    string SectionKind,
    string RollKind,
    int RollCount,
    string? PreRollFailureBehavior,
    string? PreRollSuccessSequenceBehavior,
    string? PreRollSuccessMainBehavior,
    string? DisplayName,
    IReadOnlyList<LootOutcomeRecord> Outcomes);

public sealed record LootOutcomeRecord(
    string OutcomeId,
    int Order,
    string OutcomeKind,
    string? ItemId,
    string? ItemDisplayName,
    string? NestedLootTableId,
    int? MinQuantity,
    int? MaxQuantity,
    int? Weight,
    long? ProbabilityNumerator,
    long? ProbabilityDenominator);

public sealed record LootItemRecord(
    string ItemId,
    string DisplayName,
    bool RuntimeEnabled,
    long ReferenceValue);

public sealed record LootTableOptionRecord(
    string LootTableId,
    string DisplayName,
    string PublicationState);

public sealed record LootMobBindingRecord(
    string MobDefinitionId,
    string PublicationState,
    string? RootLootTableId,
    int LegacyGuaranteedDropCount);

public sealed class LootTableNotFoundException : Exception
{
    public LootTableNotFoundException(string lootTableId)
        : base($"Loot table '{lootTableId}' does not exist.")
    {
    }
}

public sealed class LootTableConcurrencyException : Exception
{
    public LootTableConcurrencyException(
        string lootTableId,
        DateTimeOffset? currentUpdatedAtUtc)
        : base($"Loot table '{lootTableId}' changed after it was loaded. Reload it before saving.")
    {
        CurrentUpdatedAtUtc = currentUpdatedAtUtc;
    }

    public DateTimeOffset? CurrentUpdatedAtUtc { get; }
}
