using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace AudioMixer.Desktop;

internal enum NodeAlignment { Left, Top, Auto }

/// <summary>Plans position-only alignment without changing the graph or its selection.</summary>
internal static class GraphNodeAlignment
{
    internal const double Gap = 48;
    private static readonly IReadOnlyDictionary<string, Point> Empty =
        new ReadOnlyDictionary<string, Point>(new Dictionary<string, Point>(StringComparer.Ordinal));

    internal static NodeAlignment ResolveAuto(GraphConfiguration graph, IEnumerable<string> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(selectedIds);
        var nodes = SelectedNodes(graph, selectedIds);
        return ValidNodes(nodes) ? ResolveAuto(nodes) : NodeAlignment.Top;
    }

    internal static IReadOnlyDictionary<string, Point> Plan(GraphConfiguration graph,
        IEnumerable<string> selectedIds, NodeAlignment alignment, IReadOnlyDictionary<string, Size>? nodeSizes = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(selectedIds);
        if (alignment is not (NodeAlignment.Left or NodeAlignment.Top or NodeAlignment.Auto)) return Empty;

        var nodes = SelectedNodes(graph, selectedIds);
        // Stale selection entries cannot turn one surviving node into a group.
        // Ambiguous IDs or invalid positions must never produce a partial move.
        if (!ValidNodes(nodes) || nodeSizes is null
            || nodes.Any(node => !nodeSizes.TryGetValue(node.Id, out var size)
                || !double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)) return Empty;

        if (alignment == NodeAlignment.Auto) alignment = ResolveAuto(nodes);
        bool left = alignment == NodeAlignment.Left;
        double anchor = left ? nodes.Min(node => node.X) : nodes.Min(node => node.Y);
        // OrderBy is stable: exact position ties retain graph order. Nodes on
        // one row/column follow their existing left-to-right/top-to-bottom order.
        var spatial = (left ? nodes.OrderBy(node => node.Y).ThenBy(node => node.X)
            : nodes.OrderBy(node => node.X).ThenBy(node => node.Y)).ToArray();
        var ordered = SignalOrder(graph, spatial);
        if (ordered is null) return Empty;
        double start = left ? spatial[0].Y : spatial[0].X;
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        double next = start;
        foreach (var node in ordered)
        {
            double position = next;
            if (!ValidPosition(position)) return Empty;
            positions.Add(node.Id, left ? new Point(anchor, position) : new Point(position, anchor));
            var size = nodeSizes[node.Id];
            next = position + (left ? size.Height : size.Width) + Gap;
            if (!double.IsFinite(next)) return Empty;
        }
        var changes = new Dictionary<string, Point>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var position = positions[node.Id];
            if (node.X != position.X || node.Y != position.Y) changes.Add(node.Id, position);
        }
        return changes.Count == 0 ? Empty : new ReadOnlyDictionary<string, Point>(changes);
    }

    private static GraphNode[]? SignalOrder(GraphConfiguration graph, GraphNode[] spatial)
    {
        // Graphs contain at most 32 nodes. Reachability includes unselected
        // intermediates and counts each precedence pair once, even at merges.
        int count = graph.Nodes.Count;
        if (count > 32) return null;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
            if (string.IsNullOrWhiteSpace(graph.Nodes[i].Id) || !indices.TryAdd(graph.Nodes[i].Id, i)) return null;
        var reaches = new bool[count, count];
        foreach (var edge in graph.Edges)
            if ((edge.Kind is "audio" or "sidechain" or "control")
                && indices.TryGetValue(edge.From, out int from) && indices.TryGetValue(edge.To, out int to))
                reaches[from, to] = true;
        for (int via = 0; via < count; via++)
            for (int from = 0; from < count; from++)
                if (reaches[from, via])
                    for (int to = 0; to < count; to++) reaches[from, to] |= reaches[via, to];
        for (int i = 0; i < count; i++) if (reaches[i, i]) return null; // Invalid dependency cycle: never return a partial layout.

        int[] selected = spatial.Select(node => indices[node.Id]).ToArray();
        int[] indegrees = selected.Select(to => selected.Count(from => reaches[from, to])).ToArray();
        var ordered = new List<GraphNode>(spatial.Length);
        while (ordered.Count < spatial.Length)
        {
            // Kahn's next ready node is always the earliest spatial candidate.
            int ready = Array.FindIndex(indegrees, degree => degree == 0);
            if (ready < 0) return null;
            indegrees[ready] = -1;
            ordered.Add(spatial[ready]);
            for (int i = 0; i < selected.Length; i++)
                if (indegrees[i] > 0 && reaches[selected[ready], selected[i]]) indegrees[i]--;
        }
        return ordered.ToArray();
    }

    private static GraphNode[] SelectedNodes(GraphConfiguration graph, IEnumerable<string> selectedIds)
    {
        var selected = new HashSet<string>(selectedIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        return graph.Nodes.Where(node => selected.Contains(node.Id)).ToArray();
    }

    private static bool ValidNodes(GraphNode[] nodes) => nodes.Length >= 2
        && nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() == nodes.Length
        && nodes.All(node => ValidPosition(node.X) && ValidPosition(node.Y));

    // Origins express the user's arrangement without a large processor's body
    // biasing the axis. Ties follow the usual left-to-right audio flow.
    private static NodeAlignment ResolveAuto(GraphNode[] nodes)
        => nodes.Max(node => node.X) - nodes.Min(node => node.X) >= nodes.Max(node => node.Y) - nodes.Min(node => node.Y)
            ? NodeAlignment.Top : NodeAlignment.Left;

    private static bool ValidPosition(double value) => double.IsFinite(value) && value is >= -100000 and <= 100000;
}
