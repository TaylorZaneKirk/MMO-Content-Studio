using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class DialogueGraphAnalyzer
{
    public DialogueGraphAnalysis Analyze(DialogueDraft draft)
    {
        var nodes = draft.Nodes
            .GroupBy(node => node.NodeId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var reachable = new SortedSet<string>(StringComparer.Ordinal);
        var dangling = new SortedSet<string>(StringComparer.Ordinal);
        var terminal = new SortedSet<string>(StringComparer.Ordinal);
        var cycleNodes = new SortedSet<string>(StringComparer.Ordinal);
        var duplicateOrders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var entry in draft.EntryPoints)
        {
            if (!nodes.ContainsKey(entry.NodeId))
            {
                dangling.Add(entry.NodeId);
                continue;
            }

            Walk(entry.NodeId, nodes, reachable, dangling);
        }

        foreach (var node in draft.Nodes)
        {
            if (node.NodeType == DialogueAuthoringRegistry.EndNodeType)
            {
                terminal.Add(node.NodeId);
            }

            foreach (var target in Targets(node))
            {
                if (!nodes.ContainsKey(target))
                {
                    dangling.Add(target);
                }
            }
        }

        DetectCycles(nodes, cycleNodes);
        AddDuplicateOrderFields(draft, duplicateOrders);

        var terminalPathCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        var withoutTerminalPath = draft.Nodes
            .Where(node => !HasTerminalPath(
                node.NodeId,
                nodes,
                terminalPathCache,
                new HashSet<string>(StringComparer.Ordinal)))
            .Select(node => node.NodeId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new DialogueGraphAnalysis(
            reachable.ToArray(),
            draft.Nodes.Select(node => node.NodeId).Where(nodeId => !reachable.Contains(nodeId)).Order(StringComparer.Ordinal).ToArray(),
            dangling.ToArray(),
            terminal.ToArray(),
            cycleNodes.ToArray(),
            withoutTerminalPath,
            duplicateOrders.ToArray());
    }

    private static void Walk(
        string nodeId,
        IReadOnlyDictionary<string, DialogueNode> nodes,
        ISet<string> reachable,
        ISet<string> dangling)
    {
        if (!reachable.Add(nodeId))
        {
            return;
        }

        if (!nodes.TryGetValue(nodeId, out var node))
        {
            dangling.Add(nodeId);
            return;
        }

        foreach (var target in Targets(node))
        {
            Walk(target, nodes, reachable, dangling);
        }
    }

    private static void DetectCycles(
        IReadOnlyDictionary<string, DialogueNode> nodes,
        ISet<string> cycleNodes)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodeId in nodes.Keys)
        {
            Visit(nodeId, nodes, visiting, visited, cycleNodes);
        }
    }

    private static void Visit(
        string nodeId,
        IReadOnlyDictionary<string, DialogueNode> nodes,
        ISet<string> visiting,
        ISet<string> visited,
        ISet<string> cycleNodes)
    {
        if (visited.Contains(nodeId) || !nodes.ContainsKey(nodeId))
        {
            return;
        }

        if (!visiting.Add(nodeId))
        {
            cycleNodes.Add(nodeId);
            return;
        }

        foreach (var target in Targets(nodes[nodeId]))
        {
            if (visiting.Contains(target))
            {
                cycleNodes.Add(nodeId);
                cycleNodes.Add(target);
            }
            else
            {
                Visit(target, nodes, visiting, visited, cycleNodes);
            }
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);
    }

    private static bool HasTerminalPath(
        string nodeId,
        IReadOnlyDictionary<string, DialogueNode> nodes,
        IDictionary<string, bool> cache,
        ISet<string> visiting)
    {
        if (cache.TryGetValue(nodeId, out var cached))
        {
            return cached;
        }
        if (!nodes.TryGetValue(nodeId, out var node))
        {
            cache[nodeId] = false;
            return false;
        }
        if (node.NodeType == DialogueAuthoringRegistry.EndNodeType)
        {
            cache[nodeId] = true;
            return true;
        }
        if (!visiting.Add(nodeId))
        {
            return false;
        }

        var result = Targets(node).Any(target => HasTerminalPath(target, nodes, cache, visiting));
        visiting.Remove(nodeId);
        cache[nodeId] = result;
        return result;
    }

    private static IEnumerable<string> Targets(DialogueNode node)
    {
        if (node.NextNodeId is not null)
        {
            yield return node.NextNodeId;
        }
        foreach (var choice in node.Choices)
        {
            yield return choice.TargetNodeId;
        }
    }

    private static void AddDuplicateOrderFields(DialogueDraft draft, ISet<string> duplicateOrders)
    {
        foreach (var group in draft.EntryPoints
                     .GroupBy(entry => (entry.Priority, entry.EntryOrder))
                     .Where(group => group.Count() > 1))
        {
            duplicateOrders.Add($"entry_points.entry_order:{group.Key.EntryOrder}");
        }
        foreach (var group in draft.Nodes.GroupBy(node => $"{node.CanvasX:R}:{node.CanvasY:R}").Where(group => group.Count() > 1))
        {
            duplicateOrders.Add($"nodes.canvas_position:{group.Key}");
        }
        foreach (var node in draft.Nodes)
        {
            foreach (var group in node.Choices.GroupBy(choice => choice.ChoiceOrder).Where(group => group.Count() > 1))
            {
                duplicateOrders.Add($"nodes.{node.NodeId}.choices.choice_order:{group.Key}");
            }
        }
    }
}
