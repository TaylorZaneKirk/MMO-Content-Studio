using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class NpcDefinitionValidatorTests : IDisposable
{
    private readonly string _root;
    private readonly string _assetRoot;

    public NpcDefinitionValidatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"npc-validator-{Guid.NewGuid():N}");
        _assetRoot = Path.Combine(_root, "prototype", "client", "assets");
        Directory.CreateDirectory(Path.Combine(_assetRoot, "actors", "npcs"));
        Directory.CreateDirectory(Path.Combine(_root, "prototype", "shared", "dialogues"));
        WritePng(Path.Combine(_assetRoot, "actors", "npcs", "test_npc.png"), 32, 32);
        File.WriteAllText(
            Path.Combine(_root, "prototype", "shared", "dialogues", "catalog.json"),
            """
            { "schema_version": 1, "dialogues": [ { "dialogue_id": "test_npc_greeting" } ] }
            """);
    }

    [Fact]
    public async Task ValidPublishAcceptsKnownDialogueAndResolvedVisual()
    {
        var outcome = await CreateValidator().ValidateAsync(
            "test_npc",
            ValidDraft(),
            null,
            true,
            TestContext.Current.CancellationToken);

        Assert.True(outcome.ValidForDraft);
        Assert.True(outcome.ValidForPublication);
        Assert.DoesNotContain(outcome.Messages, message => message.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public async Task MissingVisualIsDraftWarningAndPublicationError()
    {
        var validator = CreateValidator();
        var draft = ValidDraft() with { VisualTexturePath = "res://assets/actors/npcs/missing.png" };

        var draftOutcome = await validator.ValidateAsync(
            "test_npc",
            draft,
            null,
            false,
            TestContext.Current.CancellationToken);
        var publishOutcome = await validator.ValidateAsync(
            "test_npc",
            draft,
            null,
            true,
            TestContext.Current.CancellationToken);

        Assert.True(draftOutcome.ValidForDraft);
        Assert.Contains(draftOutcome.Messages, message => message.Code == "npc_visual_unresolved" && message.Severity == ValidationSeverity.Warning);
        Assert.False(publishOutcome.ValidForPublication);
        Assert.Contains(publishOutcome.Messages, message => message.Code == "npc_visual_unresolved" && message.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData("Bad_Id", "npc_invalid_definition")]
    [InlineData("bad__id", "npc_invalid_definition")]
    public void InvalidStableIdIsRejected(string npcDefinitionId, string code)
    {
        var messages = new List<ApiError>();
        NpcDefinitionValidator.ValidateIdentity(npcDefinitionId, ValidDraft(), null, messages);

        Assert.Contains(messages, message => message.Code == code);
    }

    [Fact]
    public async Task DimensionMismatchBlocksPublication()
    {
        var outcome = await CreateValidator().ValidateAsync(
            "test_npc",
            ValidDraft() with { SourceWidth = 16 },
            null,
            true,
            TestContext.Current.CancellationToken);

        Assert.False(outcome.ValidForPublication);
        Assert.Contains(outcome.Messages, message => message.Code == "npc_visual_dimension_mismatch");
    }

    [Fact]
    public void VisualMovementAndFootprintRulesRejectUnsupportedValues()
    {
        var messages = new List<ApiError>();
        NpcDefinitionValidator.ValidateVisualFields(
            ValidDraft() with
            {
                VisualRenderScale = double.NaN,
                FootprintWidthTiles = 2
            },
            messages);
        NpcDefinitionValidator.ValidateMovement(
            ValidDraft() with
            {
                MovementBehavior = "random_wander",
                WanderRadiusTiles = 0,
                TickIntervalMs = 100,
                IdleChance = 2
            },
            messages);

        Assert.Contains(messages, message => message.Code == "npc_invalid_visual_render_scale");
        Assert.Contains(messages, message => message.Code == "npc_unsupported_footprint");
        Assert.Contains(messages, message => message.Code == "npc_invalid_wander_radius");
        Assert.Contains(messages, message => message.Code == "npc_invalid_tick_interval");
        Assert.Contains(messages, message => message.Code == "npc_invalid_idle_chance");
    }

    [Fact]
    public void DisabledInteractionClearsDialogueDuringNormalization()
    {
        var normalized = NpcAuthoringService.Normalize(
            "Test NPC",
            "res://assets/actors/npcs/test_npc.png",
            32,
            32,
            0,
            0,
            0.25,
            1,
            1,
            "static",
            4,
            600,
            0.15,
            false,
            1,
            "talk",
            "test_npc_greeting",
            "hello",
            null,
            null);

        Assert.False(normalized.InteractionEnabled);
        Assert.Null(normalized.DefaultDialogueId);
        Assert.Equal(0, normalized.WanderRadiusTiles);
        Assert.Equal("hello", normalized.Notes);
    }

    [Fact]
    public async Task InteractionEnabledRequiresDialogue()
    {
        var outcome = await CreateValidator().ValidateAsync(
            "test_npc",
            ValidDraft() with { DefaultDialogueId = null },
            null,
            true,
            TestContext.Current.CancellationToken);

        Assert.False(outcome.ValidForPublication);
        Assert.Contains(outcome.Messages, message => message.Code == "npc_dialogue_reference_invalid");
    }

    [Fact]
    public async Task UnknownDialogueReferenceIsRejectedWhenCatalogIsComplete()
    {
        var outcome = await CreateValidator().ValidateAsync(
            "test_npc",
            ValidDraft() with { DefaultDialogueId = "missing_dialogue" },
            null,
            true,
            TestContext.Current.CancellationToken);

        Assert.Contains(outcome.Messages, message => message.Code == "npc_dialogue_reference_invalid");
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Disabled")]
    public async Task PublishUsesAuthoringDialogueStateInsteadOfStaleRuntimeJson(string publicationState)
    {
        var validator = CreateValidator(new FakeDialogueRepository([
            DialogueRecord("test_npc_greeting", publicationState)
        ]));

        var outcome = await validator.ValidateAsync(
            "test_npc",
            ValidDraft(),
            null,
            true,
            TestContext.Current.CancellationToken);

        Assert.False(outcome.ValidForPublication);
        Assert.Contains(outcome.Messages, message => message.Code == "npc_dialogue_reference_invalid");
    }

    [Fact]
    public async Task DialogueValidationFallsBackToSyntaxOnlyWhenCatalogIsMissing()
    {
        File.Delete(Path.Combine(_root, "prototype", "shared", "dialogues", "catalog.json"));

        var outcome = await CreateValidator().ValidateAsync(
            "test_npc",
            ValidDraft(),
            null,
            true,
            TestContext.Current.CancellationToken);

        Assert.Contains(outcome.Messages, message => message.Code == "npc_dialogue_reference_validation_incomplete");
    }

    [Fact]
    public void InvalidNotesAreRejected()
    {
        var messages = new List<ApiError>();
        NpcDefinitionValidator.ValidateNotes(
            ValidDraft() with { Notes = new string('x', NpcAuthoringRegistry.MaxNotesLength + 1) },
            messages);

        Assert.Contains(messages, message => message.Code == "npc_invalid_notes");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private NpcDefinitionValidator CreateValidator(IDialogueRepository? dialogueRepository = null)
    {
        var options = Options.Create(new AssetRootsOptions
        {
            Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_client_assets"] = _assetRoot
            }
        });
        return new NpcDefinitionValidator(
            new ItemAssetService(options),
            new NpcDialogueReferenceProvider(options, dialogueRepository));
    }

    private static DialogueDefinitionRecord DialogueRecord(
        string dialogueDefinitionId,
        string publicationState) => new(
            dialogueDefinitionId,
            dialogueDefinitionId,
            publicationState,
            1,
            [],
            [],
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            0,
            0);

    private static NpcDraft ValidDraft() => new(
        "Test NPC",
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
        "test_npc_greeting",
        "notes",
        null,
        null);

    private static void WritePng(string path, int width, int height)
    {
        Span<byte> header =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52,
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height
        ];
        File.WriteAllBytes(path, header.ToArray());
    }

    private sealed class FakeDialogueRepository : IDialogueRepository
    {
        private readonly IReadOnlyList<DialogueDefinitionRecord> _records;

        public FakeDialogueRepository(IReadOnlyList<DialogueDefinitionRecord> records)
        {
            _records = records;
        }

        public Task<IReadOnlyList<DialogueDefinitionRecord>> ListAsync(
            string? search,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records);
        }

        public Task<DialogueDefinitionRecord?> LoadAsync(
            string dialogueDefinitionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DialogueDefinitionRecord?> LoadForUpdateAsync(
            string dialogueDefinitionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DialogueDefinitionRecord> InsertDraftAsync(
            string dialogueDefinitionId,
            DialogueDraft draft,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DialogueDefinitionRecord> ReplaceDraftAsync(
            string dialogueDefinitionId,
            DialogueDraft draft,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DialogueDefinitionRecord> SetPublicationAsync(
            string dialogueDefinitionId,
            string publicationState,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string dialogueDefinitionId,
            DateTimeOffset? expectedUpdatedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DialogueReferenceSummaryRecord> LoadNpcReferencesAsync(
            string dialogueDefinitionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
