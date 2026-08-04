using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class DialogueDomainRulesTests
{
    [Fact]
    public void NormalizeDraftAppliesStableIdsAndAuthoringDefaults()
    {
        var draft = DialogueDomainRules.NormalizeDraft(new DialogueDraft(
            "  Greeting  ",
            1,
            [new DialogueEntryPoint(" Default ", " Start ", 0, 0, [])],
            [
                new DialogueNode(
                    " Start ",
                    " Speaker_Text ",
                    " NPC ",
                    " Hello ",
                    " Choice ",
                    false,
                    10,
                    20,
                    " note ",
                    []),
                new DialogueNode(
                    " Choice ",
                    " Player_Choice ",
                    null,
                    null,
                    "ignored",
                    true,
                    20,
                    20,
                    null,
                    [new DialogueChoice(" Goodbye ", " Bye ", " End ", 0, [])]),
                new DialogueNode(" End ", " End ", null, null, "ignored", false, 30, 20, null, [])
            ],
            " desc ",
            " notes ",
            null,
            null));

        Assert.Equal("Greeting", draft.DisplayName);
        Assert.Equal("default", draft.EntryPoints[0].EntryId);
        Assert.Equal("start", draft.Nodes[0].NodeId);
        Assert.Equal("speaker_text", draft.Nodes[0].NodeType);
        Assert.Equal("choice", draft.Nodes[0].NextNodeId);
        Assert.Null(draft.Nodes[1].NextNodeId);
        Assert.True(draft.Nodes[2].Dismissible);
        Assert.Equal("goodbye", draft.Nodes[1].Choices[0].ChoiceId);
        Assert.Equal("end", draft.Nodes[1].Choices[0].TargetNodeId);
        Assert.Equal("desc", draft.MetadataDescription);
    }

    [Theory]
    [InlineData("test_npc_greeting", true)]
    [InlineData("bank_clerk_intro", true)]
    [InlineData("Bad", false)]
    [InlineData("bad__id", false)]
    [InlineData("1_bad", false)]
    public void StableIdRulesAreLowerSnakeCase(string value, bool expected)
    {
        Assert.Equal(expected, DialogueDomainRules.IsStableId(value));
    }

    [Fact]
    public void RegistryExposesEmptyConditionAndEffectVocabularies()
    {
        var registry = new DialogueAuthoringRegistry();

        Assert.Empty(registry.LoadConditionTypes());
        Assert.Empty(registry.LoadEffectTypes());
        Assert.False(registry.LoadCapabilities().SupportsConditions);
        Assert.False(registry.LoadCapabilities().SupportsEffects);
        Assert.False(registry.LoadCapabilities().SupportsQuestConditions);
        Assert.False(registry.LoadCapabilities().SupportsQuestEffects);
        Assert.False(registry.LoadCapabilities().SupportsRuntimeDialogueCatalog);
    }
}
