using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace VineMix.Desktop;

public sealed partial class GraphWorkspace
{
    // Session-only paint order. It is independent of graph storage order and
    // belongs to node identities so recreating a view cannot change its place.
    private readonly List<string> _nodeStacking = [];
    private string[]? _boxStackingBefore;

    private void ReconcileNodeStacking()
    {
        var present = Graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        _nodeStacking.RemoveAll(id => !present.Contains(id));
        foreach (var node in Graph.Nodes)
            if (!_nodeStacking.Contains(node.Id)) _nodeStacking.Add(node.Id);
    }

    private void ApplyNodeStacking()
    {
        ReconcileNodeStacking();
        for (int index = 0; index < _nodeStacking.Count; index++)
            if (_nodes.TryGetValue(_nodeStacking[index], out var view)) Panel.SetZIndex(view, index + 1);
    }

    // A batch keeps its existing relative paint order. An actual dragged node
    // is the optional anchor and goes above the rest of the moving selection.
    private void BringNodesToFront(IEnumerable<string> ids, string? anchor = null)
    {
        ReconcileNodeStacking();
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var raised = _nodeStacking.Where(selected.Contains).ToList();
        if (raised.Count == 0) return;
        _nodeStacking.RemoveAll(selected.Contains);
        if (anchor is not null && raised.Remove(anchor)) raised.Add(anchor);
        _nodeStacking.AddRange(raised);
        ApplyNodeStacking();
    }

    private void RememberBoxStacking()
    {
        ReconcileNodeStacking();
        _boxStackingBefore = _nodeStacking.ToArray();
    }

    private void RestoreBoxStacking()
    {
        if (_boxStackingBefore is null) return;
        _nodeStacking.Clear(); _nodeStacking.AddRange(_boxStackingBefore);
        ApplyNodeStacking();
    }

    private void UpdateBoxStacking()
    {
        // Every live rectangle starts from the same order, so growing and
        // shrinking it cannot shuffle the selected nodes relative to each other.
        RestoreBoxStacking();
        BringNodesToFront(_selectedNodes.Except(_boxBaseSelection));
    }

    private void FinishBoxStacking(bool cancel)
    {
        if (cancel) RestoreBoxStacking();
        _boxStackingBefore = null;
    }
}
