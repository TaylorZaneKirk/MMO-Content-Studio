using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class QuestDefinitionValidatorTests
{
    private readonly QuestDefinitionValidator _validator = new();

    [Fact]
    public void PublicationRejectsReachableDeadEndStep()
    {
        var outcome = _validator.Validate(
            "test_quest",
            Draft(
                steps: [
                    Step("a"),
                    Step("b"),
                    Step("dead_end")
                ],
                transitions: [
                    Transition("accept", "not_started", null, "active", "a", 0),
                    Transition("branch_complete", "active", "a", "active", "b", 1),
                    Transition("finish", "active", "b", "completed", null, 2),
                    Transition("branch_dead", "active", "a", "active", "dead_end", 3)
                ]),
            existing: null,
            forPublication: true);

        Assert.False(outcome.ValidForPublication);
        Assert.Contains("dead_end", outcome.Analysis.DeadEndStepIds);
        Assert.Contains(outcome.Messages, message =>
            message.Code == "quest_invalid_graph" &&
            message.Message.Contains("dead_end", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicationAllowsBranchingGraphWhenEveryReachableStepCanComplete()
    {
        var outcome = _validator.Validate(
            "test_quest",
            Draft(
                steps: [
                    Step("a"),
                    Step("b"),
                    Step("optional")
                ],
                transitions: [
                    Transition("accept", "not_started", null, "active", "a", 0),
                    Transition("branch_direct", "active", "a", "active", "b", 1),
                    Transition("branch_optional", "active", "a", "active", "optional", 2),
                    Transition("return_from_optional", "active", "optional", "active", "b", 3),
                    Transition("finish", "active", "b", "completed", null, 4)
                ]),
            existing: null,
            forPublication: true);

        Assert.True(outcome.ValidForPublication);
        Assert.Empty(outcome.Analysis.DeadEndStepIds);
    }

    private static QuestDraft Draft(
        IReadOnlyList<QuestStep>? steps = null,
        IReadOnlyList<QuestTransition>? transitions = null) =>
        new(
            "Test Quest",
            1,
            steps ?? [Step("first")],
            transitions ?? [
                Transition("accept", "not_started", null, "active", "first", 0),
                Transition("finish", "active", "first", "completed", null, 1)
            ],
            null,
            null);

    private static QuestStep Step(string stepId) =>
        new(stepId, stepId.Replace('_', ' '), 0);

    private static QuestTransition Transition(
        string transitionId,
        string sourceStatus,
        string? sourceStepId,
        string targetStatus,
        string? targetStepId,
        int transitionOrder) =>
        new(transitionId, sourceStatus, sourceStepId, targetStatus, targetStepId, transitionOrder);
}
