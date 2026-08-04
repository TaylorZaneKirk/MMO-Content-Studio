using MMO.ContentStudio.AuthoringHost.Services;
using Xunit;

namespace MMO.ContentStudio.AuthoringHost.Tests;

public sealed class DialogueGraphAnalyzerTests
{
    [Fact]
    public void AnalyzeFindsReachabilityTerminalPathsAndCycles()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            Nodes =
            [
                ..DialogueTestData.ValidDraft().Nodes,
                DialogueTestData.Speaker("loop", "Loop", "loop"),
                DialogueTestData.End("unused")
            ]
        };

        var analysis = new DialogueGraphAnalyzer().Analyze(draft);

        Assert.Contains("start", analysis.ReachableNodeIds);
        Assert.Contains("unused", analysis.UnreachableNodeIds);
        Assert.Contains("loop", analysis.CycleNodeIds);
        Assert.Contains("loop", analysis.NodesWithoutTerminalPath);
        Assert.Contains("end", analysis.TerminalNodeIds);
    }

    [Fact]
    public void AnalyzeReportsDanglingTargets()
    {
        var draft = DialogueTestData.ValidDraft() with
        {
            Nodes = [DialogueTestData.Speaker("start", "Hello", "missing")]
        };

        var analysis = new DialogueGraphAnalyzer().Analyze(draft);

        Assert.Contains("missing", analysis.DanglingTargetNodeIds);
    }
}
