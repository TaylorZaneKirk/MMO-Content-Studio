using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class NpcRepositoryIntegrationTests
{
    [Fact]
    public async Task SaveDraftAllowsInteractionWithoutDialogueButPublishRemainsRejectedWhenIntegrationDatabaseIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CONTENT_STUDIO_INTEGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var npcDefinitionId = $"codex_npc_{Guid.NewGuid():N}";
        var repository = CreateRepository(connectionString);
        try
        {
            var saved = await repository.SaveDraftAsync(
                npcDefinitionId,
                IncompleteInteractionDraft(),
                null,
                TestContext.Current.CancellationToken);

            Assert.Equal("Draft", saved.PublicationState);
            Assert.True(saved.InteractionEnabled);
            Assert.Null(saved.DefaultDialogueId);

            var exception = await Assert.ThrowsAsync<PostgresException>(() => repository.SetPublicationAsync(
                npcDefinitionId,
                "Published",
                saved.UpdatedAtUtc,
                TestContext.Current.CancellationToken));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("npc_definitions_dialogue_reference_check", exception.ConstraintName);
        }
        finally
        {
            await DeleteNpcAsync(connectionString, npcDefinitionId);
        }
    }

    private static NpcRepository CreateRepository(string connectionString) => new(new AuthoringDatabaseConnectionFactory(
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

    private static NpcDraft IncompleteInteractionDraft() => new(
        "Codex NPC",
        "res://assets/actors/npcs/test_npc.png",
        32,
        32,
        0,
        0,
        0.25,
        1,
        1,
        "static",
        0,
        600,
        0.15,
        true,
        1,
        "talk",
        null,
        null,
        null,
        null);

    private static async Task DeleteNpcAsync(string connectionString, string npcDefinitionId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "delete from npc_definitions where npc_definition_id = @npc_definition_id;",
            connection);
        command.Parameters.AddWithValue("npc_definition_id", npcDefinitionId);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
