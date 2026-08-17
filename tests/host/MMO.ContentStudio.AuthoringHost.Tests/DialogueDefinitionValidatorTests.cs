using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class DialogueDefinitionValidatorTests
{
    [Fact]
    public void MinimalRuntimeCompatibleDialoguePublishes()
    {
        var outcome = CreateValidator().Validate(
            "test_npc_greeting",
            DialogueTestData.ValidDraft(),
            null,
            true);

        Assert.True(outcome.ValidForDraft);
        Assert.True(outcome.ValidForPublication);
        Assert.DoesNotContain(outcome.Messages, message => message.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData("Bad_Id", "dialogue_invalid_definition")]
    [InlineData("test_npc_greeting", "dialogue_invalid_graph")]
    public void InvalidIdsAndDuplicateIdsAreRejected(string dialogueDefinitionId, string expectedCode)
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            Nodes =
            [
                DialogueTestData.Speaker("start", "Hello", "end"),
                DialogueTestData.End("start")
            ]
        };

        var outcome = CreateValidator().Validate(dialogueDefinitionId, draft, null, false);

        Assert.Contains(outcome.Messages, message => message.Code == expectedCode);
    }

    [Fact]
    public void UnsupportedNodeTypeAndConditionsAreDraftBlocking()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            EntryPoints = [new DialogueEntryPoint("default", "start", 0, 0, [new DialogueCondition("future_flag", null, null, null, null, null)])],
            Nodes = [new DialogueNode("start", "quest_branch", null, null, null, true, 0, 0, null, [])]
        };

        var outcome = CreateValidator().Validate("test_npc_greeting", draft, null, false);

        Assert.False(outcome.ValidForDraft);
        Assert.Contains(outcome.Messages, message => message.Code == "dialogue_unsupported_node_type");
        Assert.Contains(outcome.Messages, message => message.Code == "dialogue_unsupported_condition");
    }

    [Fact]
    public void TypedConditionsAreValidWhenShapeMatchesType()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            EntryPoints =
            [
                new DialogueEntryPoint(
                    "default",
                    "start",
                    0,
                    0,
                    [new DialogueCondition("quest_status", "meal", "not_started", null, null, null)])
            ],
            Nodes =
            [
                DialogueTestData.Speaker("start", "Hello", "choice"),
                new("choice", "player_choice", null, "Choose.", null, true, 100, 0, null,
                [
                    new("ready", "Ready.", "end", 0, [new DialogueCondition("quest_step", "meal", null, "return_to_inn", null, null)]),
                    new("item", "I have it.", "end", 1, [new DialogueCondition("has_item", null, null, null, "replacement_ingredient", 1)])
                ]),
                DialogueTestData.End("end")
            ]
        };

        var outcome = CreateValidator().Validate("test_npc_greeting", draft, null, false);

        Assert.True(outcome.ValidForDraft);
        Assert.DoesNotContain(outcome.Messages, message => message.Code is "dialogue_invalid_condition" or "dialogue_unsupported_condition");
    }

    [Fact]
    public void MalformedTypedConditionsAreRejected()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            EntryPoints =
            [
                new DialogueEntryPoint(
                    "default",
                    "start",
                    0,
                    0,
                    [new DialogueCondition("has_item", "meal", null, null, "replacement_ingredient", 0)])
            ]
        };

        var outcome = CreateValidator().Validate("test_npc_greeting", draft, null, false);

        Assert.False(outcome.ValidForDraft);
        Assert.Contains(outcome.Messages, message => message.Code == "dialogue_invalid_condition" && message.Field == "entry_points.conditions.quantity");
        Assert.Contains(outcome.Messages, message => message.Code == "dialogue_invalid_condition" && message.Field == "entry_points.conditions.quest_id");
    }

    [Fact]
    public void MissingTargetsAreDraftWarningsAndPublicationErrors()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            Nodes = [DialogueTestData.Speaker("start", "Hello", "missing")]
        };

        var draftOutcome = CreateValidator().Validate("test_npc_greeting", draft, null, false);
        var publishOutcome = CreateValidator().Validate("test_npc_greeting", draft, null, true);

        Assert.True(draftOutcome.ValidForDraft);
        Assert.Contains(draftOutcome.Messages, message => message.Code == "dialogue_transition_target_missing" && message.Severity == ValidationSeverity.Warning);
        Assert.False(publishOutcome.ValidForPublication);
        Assert.Contains(publishOutcome.Messages, message => message.Code == "dialogue_transition_target_missing" && message.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void PublishRejectsNodeSpecificRuntimeViolations()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            Nodes =
            [
                new("start", "speaker_text", "NPC", null, "choice", true, 0, 0, null, []),
                new("choice", "player_choice", null, null, null, true, 0, 0, null, []),
                new("end", "end", null, null, "start", true, 0, 0, null, [])
            ]
        };

        var outcome = CreateValidator().Validate("test_npc_greeting", draft, null, true);

        Assert.False(outcome.ValidForPublication);
        Assert.Contains(outcome.Messages, message => message.Message.Contains("needs text", StringComparison.Ordinal));
        Assert.Contains(outcome.Messages, message => message.Message.Contains("needs at least one choice", StringComparison.Ordinal));
        Assert.Contains(outcome.Messages, message => message.Message.Contains("cannot have outgoing transitions", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateEntryAndChoiceRuntimeOrdersAreRejected()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            EntryPoints =
            [
                new DialogueEntryPoint("default", "start", 0, 0, []),
                new DialogueEntryPoint("fallback", "start", 0, 0, [])
            ],
            Nodes =
            [
                DialogueTestData.Speaker("start", "Hello", "choice"),
                new("choice", "player_choice", null, "Choose.", null, true, 100, 0, null,
                [
                    new("first", "First.", "end", 0, []),
                    new("second", "Second.", "end", 0, [])
                ]),
                DialogueTestData.End("end")
            ]
        };

        var outcome = CreateValidator().Validate("test_npc_greeting", draft, null, true);

        Assert.False(outcome.ValidForPublication);
        Assert.Contains(outcome.Messages, message => message.Field == "entry_points.entry_order:0");
        Assert.Contains(outcome.Messages, message => message.Field == "nodes.choice.choices.choice_order:0");
    }

    [Fact]
    public void CyclesAreAllowedOnlyWhenTheyStillReachEnd()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            Nodes =
            [
                DialogueTestData.Speaker("start", "Hello", "choice"),
                new("choice", "player_choice", null, null, null, true, 0, 0, null,
                [
                    new("again", "Again", "start", 0, []),
                    new("bye", "Bye", "end", 1, [])
                ]),
                DialogueTestData.End("end")
            ]
        };

        var outcome = CreateValidator().Validate("test_npc_greeting", draft, null, true);

        Assert.True(outcome.ValidForPublication);
        Assert.Contains("start", outcome.Analysis.CycleNodeIds);
    }

    private static DialogueDefinitionValidator CreateValidator() =>
        new(new DialogueGraphAnalyzer());
}
