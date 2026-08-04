using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Tests;

internal static class DialogueTestData
{
    public static DialogueDraft ValidDraft(DateTimeOffset? expected = null, string? signature = null) => new(
        "Test NPC Greeting",
        1,
        [new DialogueEntryPoint("default", "start", 0, 0, [])],
        [
            Speaker("start", "Welcome.", "choice"),
            new DialogueNode("choice", "player_choice", null, "What do you say?", null, true, 100, 0, null,
            [
                new DialogueChoice("goodbye", "Goodbye.", "end", 0, [])
            ]),
            End("end")
        ],
        "Runtime-compatible greeting.",
        "Authoring notes.",
        expected,
        signature);

    public static DialogueNode Speaker(string nodeId, string text, string? nextNodeId) =>
        new(nodeId, "speaker_text", "Test NPC", text, nextNodeId, true, 0, 0, null, []);

    public static DialogueNode End(string nodeId) =>
        new(nodeId, "end", "Test NPC", "Goodbye.", null, true, 200, 0, null, []);
}
