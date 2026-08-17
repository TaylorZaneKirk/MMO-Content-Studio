using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class QuestRepositoryIntegrationTests
{
    [Fact]
    public async Task ReferenceSafetyUsesPersistedCharacterQuestRowsWhenIntegrationDatabaseIsConfigured()
    {
        var connectionString = AcceptanceConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await using var fixture = await QuestRepositoryFixture.CreateAsync(connectionString);
        await fixture.SeedCharacterAsync();
        var repository = CreateRepository(connectionString);
        var questId = fixture.TrackQuestId("quest_ref_" + Guid.NewGuid().ToString("N"));
        var draft = Draft();
        var saved = await repository.ReplaceDraftAsync(questId, draft, null, TestContext.Current.CancellationToken);
        var published = await repository.SetPublicationAsync(questId, "Published", saved.UpdatedAtUtc, TestContext.Current.CancellationToken);
        await fixture.InsertQuestStateAsync(questId, "active", "first");

        var references = await repository.LoadStateReferencesAsync(questId, TestContext.Current.CancellationToken);
        var disableError = await Assert.ThrowsAsync<QuestDefinitionReferencedByStateException>(() =>
            repository.SetPublicationAsync(questId, "Disabled", published.UpdatedAtUtc, TestContext.Current.CancellationToken));

        Assert.Equal(1, references.TotalCount);
        Assert.Equal(1, references.ActiveCount);
        Assert.Equal(0, references.CompletedCount);
        Assert.Equal(["first"], references.ActiveStepIds);
        Assert.Equal("disable", disableError.Operation);

        await fixture.ForcePublicationStateAsync(questId, "Disabled");
        var disabled = await repository.LoadAsync(questId, TestContext.Current.CancellationToken);

        var deleteError = await Assert.ThrowsAsync<QuestDefinitionReferencedByStateException>(() =>
            repository.DeleteAsync(questId, disabled!.UpdatedAtUtc, TestContext.Current.CancellationToken));

        Assert.Equal("delete", deleteError.Operation);
        Assert.NotNull(await repository.LoadAsync(questId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishRequiresAllPersistedActiveStepsWhenIntegrationDatabaseIsConfigured()
    {
        var connectionString = AcceptanceConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await using var fixture = await QuestRepositoryFixture.CreateAsync(connectionString);
        await fixture.SeedCharacterAsync();
        var repository = CreateRepository(connectionString);
        var questId = fixture.TrackQuestId("quest_step_ref_" + Guid.NewGuid().ToString("N"));
        var saved = await repository.ReplaceDraftAsync(questId, Draft(
            steps: [Step("first", 0), Step("second", 1)],
            transitions: [
                Transition("accept", "not_started", null, "active", "first", 0),
                Transition("advance", "active", "first", "active", "second", 1),
                Transition("finish", "active", "second", "completed", null, 2)
            ]), null, TestContext.Current.CancellationToken);
        var published = await repository.SetPublicationAsync(questId, "Published", saved.UpdatedAtUtc, TestContext.Current.CancellationToken);
        await fixture.InsertQuestStateAsync(questId, "active", "first");

        var incompatibleDraft = await repository.ReplaceDraftAsync(questId, Draft(
            steps: [Step("second", 0)],
            transitions: [
                Transition("accept", "not_started", null, "active", "second", 0),
                Transition("finish", "active", "second", "completed", null, 1)
            ]), published.UpdatedAtUtc, TestContext.Current.CancellationToken);

        var missingStepError = await Assert.ThrowsAsync<QuestDefinitionMissingActiveStepException>(() =>
            repository.SetPublicationAsync(questId, "Published", incompatibleDraft.UpdatedAtUtc, TestContext.Current.CancellationToken));

        Assert.Equal(["first"], missingStepError.MissingStepIds);

        var compatibleDraft = await repository.ReplaceDraftAsync(questId, Draft(
            steps: [Step("first", 0), Step("second", 1), Step("replacement", 2)],
            transitions: [
                Transition("accept", "not_started", null, "active", "first", 0),
                Transition("advance", "active", "first", "active", "replacement", 1),
                Transition("finish", "active", "replacement", "completed", null, 2),
                Transition("legacy_finish", "active", "second", "completed", null, 3)
            ]), incompatibleDraft.UpdatedAtUtc, TestContext.Current.CancellationToken);

        var republished = await repository.SetPublicationAsync(questId, "Published", compatibleDraft.UpdatedAtUtc, TestContext.Current.CancellationToken);

        Assert.Equal("Published", republished.PublicationState);
    }

    [Fact]
    public async Task SaveDraftIsRejectedForReferencedExistingQuestWhenIntegrationDatabaseIsConfigured()
    {
        var connectionString = AcceptanceConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await using var fixture = await QuestRepositoryFixture.CreateAsync(connectionString);
        await fixture.SeedCharacterAsync();
        var repository = CreateRepository(connectionString);
        var questId = fixture.TrackQuestId("quest_draft_ref_" + Guid.NewGuid().ToString("N"));
        var saved = await repository.ReplaceDraftAsync(questId, Draft(), null, TestContext.Current.CancellationToken);
        var published = await repository.SetPublicationAsync(questId, "Published", saved.UpdatedAtUtc, TestContext.Current.CancellationToken);
        await fixture.InsertQuestStateAsync(questId, "active", "first");

        var error = await Assert.ThrowsAsync<QuestDefinitionReferencedByStateException>(() =>
            repository.ReplaceDraftAsync(
                questId,
                Draft(
                    steps: [Step("replacement", 0)],
                    transitions: [
                        Transition("accept", "not_started", null, "active", "replacement", 0),
                        Transition("finish", "active", "replacement", "completed", null, 1)
                    ]),
                published.UpdatedAtUtc,
                TestContext.Current.CancellationToken));

        Assert.Equal("save_draft", error.Operation);
        Assert.Equal(1, error.References.ActiveCount);
        var reloaded = await repository.LoadAsync(questId, TestContext.Current.CancellationToken);
        Assert.Equal("Published", reloaded!.PublicationState);
        Assert.Equal("first", Assert.Single(reloaded.Steps).StepId);
    }

    [Fact]
    public async Task SaveDraftReferenceCheckRunsAfterQuestContentStateLockWhenIntegrationDatabaseIsConfigured()
    {
        var connectionString = AcceptanceConnectionString();
        if (connectionString is null)
        {
            return;
        }

        await using var fixture = await QuestRepositoryFixture.CreateAsync(connectionString);
        await fixture.SeedCharacterAsync();
        var repository = CreateRepository(connectionString);
        var questId = fixture.TrackQuestId("quest_lock_ref_" + Guid.NewGuid().ToString("N"));
        var saved = await repository.ReplaceDraftAsync(questId, Draft(), null, TestContext.Current.CancellationToken);

        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using var lockTransaction = await lockConnection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await AcquireQuestContentStateLockAsync(
            lockConnection,
            lockTransaction,
            questId,
            TestContext.Current.CancellationToken);

        var replaceTask = repository.ReplaceDraftAsync(
            questId,
            Draft(
                steps: [Step("replacement", 0)],
                transitions: [
                    Transition("accept", "not_started", null, "active", "replacement", 0),
                    Transition("finish", "active", "replacement", "completed", null, 1)
                ]),
            saved.UpdatedAtUtc,
            TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
        Assert.False(replaceTask.IsCompleted);

        await fixture.InsertQuestStateAsync(questId, "active", "first");
        await lockTransaction.CommitAsync(TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<QuestDefinitionReferencedByStateException>(() => replaceTask);

        Assert.Equal("save_draft", error.Operation);
        var reloaded = await repository.LoadAsync(questId, TestContext.Current.CancellationToken);
        Assert.Equal("first", Assert.Single(reloaded!.Steps).StepId);
    }

    private static QuestRepository CreateRepository(string connectionString) =>
        new(new AuthoringDatabaseConnectionFactory(
            Options.Create(new ConnectionProfilesOptions
            {
                Active = "integration",
                Profiles = new Dictionary<string, ConnectionProfileOptions>(StringComparer.OrdinalIgnoreCase)
                {
                    ["integration"] = new()
                    {
                        ConnectionString = connectionString,
                        CommandTimeoutSeconds = 5
                    }
                }
            })));

    private static string? AcceptanceConnectionString()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("QUEST_V1_ACCEPTANCE_CONNECTION_STRING") ??
            Environment.GetEnvironmentVariable("MAP_PUBLISHER_INTEGRATION_CONNECTION_STRING");
        var allowMutation = Environment.GetEnvironmentVariable("QUEST_V1_ACCEPTANCE_ALLOW_MUTATION");
        return string.IsNullOrWhiteSpace(connectionString) ||
               !string.Equals(allowMutation, "true", StringComparison.OrdinalIgnoreCase)
            ? null
            : connectionString;
    }

    private static QuestDraft Draft(
        IReadOnlyList<QuestStep>? steps = null,
        IReadOnlyList<QuestTransition>? transitions = null) =>
        new(
            "Test Quest",
            1,
            steps ?? [Step("first", 0)],
            transitions ?? [
                Transition("accept", "not_started", null, "active", "first", 0),
                Transition("finish", "active", "first", "completed", null, 1)
            ],
            null,
            null);

    private static QuestStep Step(string stepId, int order) =>
        new(stepId, stepId.Replace('_', ' '), order);

    private static QuestTransition Transition(
        string transitionId,
        string sourceStatus,
        string? sourceStepId,
        string targetStatus,
        string? targetStepId,
        int transitionOrder) =>
        new(transitionId, sourceStatus, sourceStepId, targetStatus, targetStepId, transitionOrder);

    private static async Task AcquireQuestContentStateLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string questId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            select pg_advisory_xact_lock(hashtextextended(@identity, 0));
            """, connection, transaction);
        command.Parameters.AddWithValue("identity", $"quest_content_state_v1|{questId}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class QuestRepositoryFixture : IAsyncDisposable
    {
        private const string AcceptanceMapId = "quest_authoring_acceptance_map";
        private readonly string _connectionString;
        private readonly string _accountId;
        private readonly List<string> _questIds = [];

        private QuestRepositoryFixture(string connectionString)
        {
            _connectionString = connectionString;
            _accountId = Guid.NewGuid().ToString();
            CharacterId = Guid.NewGuid().ToString();
        }

        private string CharacterId { get; }

        public static async Task<QuestRepositoryFixture> CreateAsync(string connectionString)
        {
            var fixture = new QuestRepositoryFixture(connectionString);
            await fixture.ApplyMigrationsAsync();
            return fixture;
        }

        public string TrackQuestId(string questId)
        {
            _questIds.Add(questId);
            return questId;
        }

        public async Task SeedCharacterAsync()
        {
            const string sql = """
                insert into maps (id, display_name, width, height, base_layer_json, unresolved_segments_json)
                values (@mapId, 'Quest Authoring Acceptance', 8, 8, '{}'::jsonb, '[]'::jsonb)
                on conflict (id) do nothing;

                insert into accounts (account_id, account_name, password_hash)
                values (@accountId, @accountName, 'test')
                on conflict (account_id) do nothing;

                insert into characters (id, account_id, name, map_id, tile_x, tile_y)
                values (@characterId, @accountId, @characterName, @mapId, 1, 1)
                on conflict (id) do nothing;
                """;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("mapId", AcceptanceMapId);
            command.Parameters.AddWithValue("accountId", Guid.Parse(_accountId));
            command.Parameters.AddWithValue("accountName", $"quest_authoring_acceptance_{_accountId}");
            command.Parameters.AddWithValue("characterId", Guid.Parse(CharacterId));
            command.Parameters.AddWithValue("characterName", $"quest_authoring_acceptance_{CharacterId[..8]}");
            await command.ExecuteNonQueryAsync();
        }

        public async Task InsertQuestStateAsync(string questId, string status, string? currentStepId)
        {
            const string sql = """
                insert into character_quests (
                    character_id,
                    quest_id,
                    status,
                    current_step_id,
                    state_revision,
                    started_at,
                    updated_at,
                    completed_at
                ) values (
                    @characterId,
                    @questId,
                    @status,
                    @currentStepId,
                    1,
                    now(),
                    now(),
                    case when @status = 'completed' then now() else null end
                )
                on conflict (character_id, quest_id) do update
                set status = excluded.status,
                    current_step_id = excluded.current_step_id,
                    completed_at = excluded.completed_at,
                    updated_at = now();
                """;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("characterId", Guid.Parse(CharacterId));
            command.Parameters.AddWithValue("questId", questId);
            command.Parameters.AddWithValue("status", status);
            command.Parameters.Add("currentStepId", NpgsqlDbType.Text).Value = currentStepId is null ? DBNull.Value : currentStepId;
            await command.ExecuteNonQueryAsync();
        }

        public async Task ForcePublicationStateAsync(string questId, string publicationState)
        {
            const string sql = """
                update quest_definitions
                set publication_state = @publicationState
                where quest_id = @questId;
                """;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("questId", questId);
            command.Parameters.AddWithValue("publicationState", publicationState);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("""
                delete from characters
                where id = @characterId;

                delete from accounts
                where account_id = @accountId;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("characterId", Guid.Parse(CharacterId));
                command.Parameters.AddWithValue("accountId", Guid.Parse(_accountId));
                await command.ExecuteNonQueryAsync();
            }

            foreach (var questId in _questIds)
            {
                await using var command = new NpgsqlCommand("""
                    delete from quest_definitions
                    where quest_id = @questId;
                    """, connection, transaction);
                command.Parameters.AddWithValue("questId", questId);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        private async Task ApplyMigrationsAsync()
        {
            if (!await TableExistsAsync("accounts"))
            {
                await ApplyMigrationAsync("001_initial_schema.sql");
            }

            await ApplyMigrationAsync("042_persistent_quest_state_v1.sql");
            await ApplyMigrationAsync("043_quest_transition_evidence_lifecycle_delete.sql");
            await ApplyMigrationAsync("044_quest_definition_authoring_v1.sql");
        }

        private async Task<bool> TableExistsAsync(string tableName)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("select to_regclass(@tableName) is not null;", connection);
            command.Parameters.AddWithValue("tableName", tableName);
            return (bool)(await command.ExecuteScalarAsync() ?? false);
        }

        private async Task ApplyMigrationAsync(string fileName)
        {
            var migration = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "prototype", "sql", fileName));
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(migration, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "prototype", "sql", "044_quest_definition_authoring_v1.sql")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Expected to find repository root.");
    }
}
