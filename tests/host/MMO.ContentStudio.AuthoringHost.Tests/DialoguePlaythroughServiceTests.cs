using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class DialoguePlaythroughServiceTests
{
    [Fact]
    public void StartContinueChooseAndAcknowledgeEnd()
    {
        var service = new DialoguePlaythroughService();
        var draft = DialogueTestData.ValidDraft();

        var start = service.Preview(draft, Request(restart: true));
        var choice = service.Preview(draft, Request(currentNodeId: "start", visited: start.VisitedNodeIds));
        var end = service.Preview(draft, Request(currentNodeId: "choice", selectedChoiceId: "goodbye", visited: choice.VisitedNodeIds));
        var closed = service.Preview(draft, Request(currentNodeId: "end", acknowledgeEnd: true, visited: end.VisitedNodeIds));

        Assert.Equal("start", start.CurrentNode!.NodeId);
        Assert.True(start.CanContinue);
        Assert.Equal("choice", choice.CurrentNode!.NodeId);
        Assert.Single(choice.VisibleChoices);
        Assert.Equal("end", end.CurrentNode!.NodeId);
        Assert.True(end.IsEnd);
        Assert.Null(closed.CurrentNode);
    }

    [Fact]
    public void StartUsesPriorityThenEntryOrderAndChoicesUseChoiceOrder()
    {
        var service = new DialoguePlaythroughService();
        var draft = DialogueTestData.ValidDraft() with
        {
            EntryPoints =
            [
                new DialogueEntryPoint("z_entry", "alternate", 0, 0, []),
                new DialogueEntryPoint("a_entry", "start", 0, 1, [])
            ],
            Nodes =
            [
                DialogueTestData.Speaker("alternate", "Alternate.", "choice"),
                DialogueTestData.Speaker("start", "Welcome.", "choice"),
                new DialogueNode("choice", "player_choice", null, "What do you say?", null, true, 100, 0, null,
                [
                    new DialogueChoice("second", "Second.", "end", 1, []),
                    new DialogueChoice("first", "First.", "end", 0, [])
                ]),
                DialogueTestData.End("end")
            ]
        };

        var start = service.Preview(draft, Request(restart: true));
        var choice = service.Preview(draft, Request(currentNodeId: "alternate"));

        Assert.Equal("alternate", start.CurrentNode!.NodeId);
        Assert.Equal("first", choice.VisibleChoices[0].ChoiceId);
        Assert.Equal("second", choice.VisibleChoices[1].ChoiceId);
    }

    [Fact]
    public void SelectedChoiceReportsEffectsWithoutMutating()
    {
        var service = new DialoguePlaythroughService();
        var draft = DialogueTestData.ValidDraft() with
        {
            Nodes =
            [
                DialogueTestData.Speaker("start", "Welcome.", "choice"),
                new DialogueNode("choice", "player_choice", null, "What do you say?", null, true, 100, 0, null,
                [
                    new DialogueChoice(
                        "accept",
                        "Accept.",
                        "end",
                        0,
                        [],
                        [new DialogueEffect("start_quest", 0, "start_quest", "meal", "accept", null, null, null, null)])
                ]),
                DialogueTestData.End("end")
            ]
        };

        var end = service.Preview(draft, Request(currentNodeId: "choice", selectedChoiceId: "accept"));

        Assert.Equal("end", end.CurrentNode!.NodeId);
        Assert.Equal("start_quest", Assert.Single(end.WouldApplyEffects).EffectType);
    }


    [Fact]
    public void InvalidChoiceStaleNodeAndLoopProtectionReturnWarnings()
    {
        var service = new DialoguePlaythroughService();
        var invalidChoice = service.Preview(
            DialogueTestData.ValidDraft(),
            Request(currentNodeId: "choice", selectedChoiceId: "missing"));
        var staleNode = service.Preview(
            DialogueTestData.ValidDraft(),
            Request(currentNodeId: "missing"));
        var loop = service.Preview(
            DialogueTestData.ValidDraft() with
            {
                Nodes =
                [
                    DialogueTestData.Speaker("start", "Hello", "start")
                ]
            },
            Request(currentNodeId: "start", visited: ["start"]));

        Assert.Contains(invalidChoice.Warnings, warning => warning.Code == "dialogue_playthrough_invalid_state");
        Assert.Contains(staleNode.Warnings, warning => warning.Code == "dialogue_playthrough_invalid_state");
        Assert.Contains(loop.Warnings, warning => warning.Code == "dialogue_playthrough_invalid_state");
    }

    private static PreviewDialoguePlaythroughRequest Request(
        string? currentNodeId = null,
        string? selectedChoiceId = null,
        bool acknowledgeEnd = false,
        bool restart = false,
        IReadOnlyList<string>? visited = null) =>
        new(null, null, currentNodeId, selectedChoiceId, acknowledgeEnd, restart, visited ?? [], null);
}
