using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace VineMix.Desktop;

public sealed record WorkspaceEdit(DateTime Time, string Description, string Key);

/// <summary>Bounded, session-only graph history. Camera and application preferences are not edits.</summary>
public sealed class WorkspaceHistory
{
    private sealed record Step(WorkspaceEdit Entry, GraphConfiguration Before, GraphConfiguration After);
    private readonly List<Step> _steps = [];
    private int _position, _gestureDepth;
    private GraphConfiguration? _gestureBefore, _gestureAfter;
    private string? _gestureDescription;
    public const int Capacity = 100;
    public IReadOnlyList<WorkspaceEdit> Entries => _steps.Take(_position).Reverse().Select(step => step.Entry).ToArray();
    public bool HasGestureEdit => _gestureBefore is not null && _gestureAfter is not null && !SameContents(_gestureBefore, _gestureAfter);
    public bool CanUndo => HasGestureEdit || _position > 0;
    public bool CanRedo => !HasGestureEdit && _position < _steps.Count;
    public string UndoDescription => HasGestureEdit ? _gestureDescription ?? Describe(_gestureBefore!, _gestureAfter!).Description
        : _position > 0 ? _steps[_position - 1].Entry.Description : "";
    public string RedoDescription => CanRedo ? _steps[_position].Entry.Description : "";
    public bool UndoChangesLayout => HasGestureEdit
        ? ChangesLayout(_gestureBefore!, _gestureAfter!)
        : _position > 0 && ChangesLayout(_steps[_position - 1].Before, _steps[_position - 1].After);
    public bool RedoChangesLayout => CanRedo && ChangesLayout(_steps[_position].Before, _steps[_position].After);

    // Compare topology and placement, never the activity description. Device
    // assignment, names, sound controls and cable level/mute remain adjustable.
    // A mixed structural/parameter edit is structural and cannot be half-undone.
    internal static bool ChangesLayout(GraphConfiguration before, GraphConfiguration after)
        => !SameValue(before.Nodes.OrderBy(node => node.Id).Select(node => new { node.Id, node.Kind, node.X, node.Y }),
            after.Nodes.OrderBy(node => node.Id).Select(node => new { node.Id, node.Kind, node.X, node.Y }))
        || !SameValue(before.Edges.OrderBy(edge => edge.Id).Select(edge => new { edge.Id, edge.From, edge.To, edge.Kind, edge.FromPort, edge.ToPort }),
            after.Edges.OrderBy(edge => edge.Id).Select(edge => new { edge.Id, edge.From, edge.To, edge.Kind, edge.FromPort, edge.ToPort }))
        || !SameValue(before.Frames.OrderBy(frame => frame.Id), after.Frames.OrderBy(frame => frame.Id));

    public void BeginGesture(string? description = null)
    {
        if (_gestureDepth++ == 0) _gestureDescription = description;
    }

    public void EndGesture()
    {
        if (_gestureDepth == 0 || --_gestureDepth != 0) return;
        FinishGesture();
    }

    internal void CompleteGesture()
    {
        _gestureDepth = 0;
        FinishGesture();
    }

    private void FinishGesture()
    {
        var before = _gestureBefore; var after = _gestureAfter; string? description = _gestureDescription;
        _gestureBefore = _gestureAfter = null; _gestureDescription = null;
        if (before is not null && after is not null) AddStep(before, after, description);
    }

    public void Record(GraphConfiguration before, GraphConfiguration after)
    {
        if (SameContents(before, after)) return;
        if (_gestureDepth > 0)
        {
            _gestureBefore ??= before.Copy();
            _gestureAfter = after.Copy();
        }
        else AddStep(before, after, null);
    }

    private void AddStep(GraphConfiguration before, GraphConfiguration after, string? description)
    {
        if (SameContents(before, after)) return;
        if (_position < _steps.Count) _steps.RemoveRange(_position, _steps.Count - _position);
        var entry = Describe(before, after);
        if (!string.IsNullOrWhiteSpace(description)) entry = entry with { Description = description };
        _steps.Add(new(entry, before.Copy(), after.Copy()));
        if (_steps.Count > Capacity) _steps.RemoveAt(0);
        _position = _steps.Count;
    }

    public void Clear()
    {
        _steps.Clear(); _position = _gestureDepth = 0;
        _gestureBefore = _gestureAfter = null; _gestureDescription = null;
    }

    internal bool TryUndo(GraphConfiguration current, out GraphConfiguration restored, out WorkspaceGraphRestore? restore, out string error)
        => TryMove(current, undo: true, out restored, out restore, out error);
    internal bool TryRedo(GraphConfiguration current, out GraphConfiguration restored, out WorkspaceGraphRestore? restore, out string error)
        => TryMove(current, undo: false, out restored, out restore, out error);

    private bool TryMove(GraphConfiguration current, bool undo, out GraphConfiguration restored, out WorkspaceGraphRestore? restore, out string error)
    {
        CompleteGesture();
        restored = current; restore = null; error = "";
        if (undo ? _position == 0 : _position == _steps.Count) return false;
        var step = _steps[undo ? _position - 1 : _position];
        var transition = new WorkspaceGraphRestore(undo ? step.After : step.Before, undo ? step.Before : step.After);
        if (!transition.TryApply(current, out restored, out error)) return false;
        _position += undo ? -1 : 1;
        restore = transition;
        return true;
    }

    internal bool IsCompatible(GraphConfiguration current)
    {
        if (HasGestureEdit)
            return new WorkspaceGraphRestore(_gestureAfter!, _gestureBefore!).TryApply(current, out _, out _);
        if (_position > 0)
        {
            var step = _steps[_position - 1];
            if (!new WorkspaceGraphRestore(step.After, step.Before).TryApply(current, out _, out _)) return false;
        }
        if (_position < _steps.Count)
        {
            var step = _steps[_position];
            if (!new WorkspaceGraphRestore(step.Before, step.After).TryApply(current, out _, out _)) return false;
        }
        return true;
    }

    internal static bool SameContents(GraphConfiguration left, GraphConfiguration right)
        => SameValue(new { left.Nodes, left.Edges, left.Frames }, new { right.Nodes, right.Edges, right.Frames });
    internal static bool SameValue<T>(T left, T right)
        // Preserve the existing serialized-value comparison, including member
        // and collection order, without allocating two UTF-16 JSON strings.
        => JsonSerializer.SerializeToUtf8Bytes(left, Protocol.Json).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, Protocol.Json));

    private static WorkspaceEdit Describe(GraphConfiguration before, GraphConfiguration after)
    {
        var added = after.Nodes.Where(n => before.Nodes.All(old => old.Id != n.Id)).ToArray();
        var removed = before.Nodes.Where(n => after.Nodes.All(next => next.Id != n.Id)).ToArray();
        var changed = after.Nodes.Where(n => before.Nodes.Any(old => old.Id == n.Id && !SameValue(old, n))).ToArray();
        bool cables = !SameValue(before.Edges, after.Edges);
        string description, key;
        if (added.Length > 0) { description = added.Length == 1 ? "Added " + added[0].Name : $"Added {added.Length} nodes"; key = "add:" + added[0].Id; }
        else if (removed.Length > 0) { description = removed.Length == 1 ? "Removed " + removed[0].Name : $"Removed {removed.Length} nodes"; key = "remove:" + removed[0].Id; }
        else if (cables) { description = after.Edges.Count < before.Edges.Count ? "Removed connections" : after.Edges.Count > before.Edges.Count ? "Connected nodes" : "Changed connections"; key = Guid.NewGuid().ToString(); }
        else if (!GraphFrames.Same(before.Frames, after.Frames))
        {
            var addedFrame = after.Frames.FirstOrDefault(frame => before.Frames.All(old => old.Id != frame.Id));
            var removedFrame = before.Frames.FirstOrDefault(frame => after.Frames.All(next => next.Id != frame.Id));
            var changedFrame = after.Frames.FirstOrDefault(frame => before.Frames.Any(old => old.Id == frame.Id && !SameValue(old, frame)));
            var oldFrame = changedFrame is null ? null : before.Frames.Single(frame => frame.Id == changedFrame.Id);
            description = addedFrame is not null ? "Created group “" + addedFrame.Name + "”"
                : removedFrame is not null ? "Ungrouped “" + removedFrame.Name + "”"
                : changedFrame is null ? "Reordered groups"
                : oldFrame!.Name != changedFrame.Name ? "Renamed group “" + changedFrame.Name + "”"
                : oldFrame.ColorKey != changedFrame.ColorKey ? "Recolored group “" + changedFrame.Name + "”"
                : "Changed members of “" + changedFrame.Name + "”";
            key = "frame:" + (addedFrame ?? removedFrame ?? changedFrame)?.Id;
        }
        else
        {
            bool moved = changed.All(n => { var old = before.Nodes.Single(o => o.Id == n.Id).Copy(); old.X = n.X; old.Y = n.Y; return SameValue(old, n); });
            description = (moved ? "Moved " : "Adjusted ") + (changed.Length == 1 ? changed[0].Name : $"{changed.Length} nodes");
            key = (moved ? "move:" : "parameter:") + string.Join(",", changed.Select(n => n.Id));
        }
        return new(DateTime.Now, description, key);
    }
}

/// <summary>
/// A guarded graph transition, safe to replay after an engine revision changes.
/// Only changed entities are restored. Unrelated edits and the current camera survive;
/// conflicting edits to the same node/cable refuse the whole transition atomically.
/// </summary>
internal sealed class WorkspaceGraphRestore
{
    private static bool SameNode(GraphNode? actual, GraphNode? expected)
    {
        if (actual is not null && expected is not null && VirtualDeviceBindings.IsVirtual(actual.Kind)
            && !VirtualDeviceBindings.IsAssigned(actual.Device) && !VirtualDeviceBindings.IsAssigned(expected.Device))
        {
            // Restoring an unassigned node deliberately suppresses automatic
            // matching. This policy must not invalidate unrelated undo/redo.
            var normalized = actual.Copy(); normalized.AutoAssignDevice = expected.AutoAssignDevice;
            return WorkspaceHistory.SameValue(normalized,expected);
        }
        return WorkspaceHistory.SameValue(actual,expected);
    }
    private readonly GraphConfiguration _expected, _target;
    internal WorkspaceGraphRestore(GraphConfiguration expected, GraphConfiguration target)
    { _expected = expected.Copy(); _target = target.Copy(); }

    internal bool TryApply(GraphConfiguration current, out GraphConfiguration restored, out string error, bool restoreFrames = true)
    {
        restored = current; error = "";
        var next = current.Copy();
        foreach (string id in _expected.Nodes.Select(n => n.Id).Union(_target.Nodes.Select(n => n.Id)))
        {
            var before = _expected.Nodes.FirstOrDefault(n => n.Id == id);
            var after = _target.Nodes.FirstOrDefault(n => n.Id == id);
            if (WorkspaceHistory.SameValue(before, after)) continue;
            var actual = next.Nodes.FirstOrDefault(n => n.Id == id);
            if (SameNode(actual, after)) continue; // Already applied before a lost acknowledgement.
            if (!SameNode(actual, before))
            { error = $"The node “{before?.Name ?? after?.Name ?? id}” changed since this edit."; return false; }
            int index = next.Nodes.FindIndex(n => n.Id == id);
            if (after is null) next.Nodes.RemoveAt(index);
            else if (index >= 0) next.Nodes[index] = after.Copy();
            else next.Nodes.Insert(Math.Min(_target.Nodes.FindIndex(n => n.Id == id), next.Nodes.Count), after.Copy());
        }
        foreach (string id in _expected.Edges.Select(e => e.Id).Union(_target.Edges.Select(e => e.Id)))
        {
            var before = _expected.Edges.FirstOrDefault(e => e.Id == id);
            var after = _target.Edges.FirstOrDefault(e => e.Id == id);
            if (WorkspaceHistory.SameValue(before, after)) continue;
            var actual = next.Edges.FirstOrDefault(e => e.Id == id);
            if (WorkspaceHistory.SameValue(actual, after)) continue;
            if (!WorkspaceHistory.SameValue(actual, before))
            { error = "A connection changed since this edit."; return false; }
            int index = next.Edges.FindIndex(e => e.Id == id);
            if (after is null) next.Edges.RemoveAt(index);
            else if (index >= 0) next.Edges[index] = after.Copy();
            else next.Edges.Insert(Math.Min(_target.Edges.FindIndex(e => e.Id == id), next.Edges.Count), after.Copy());
        }
        if (restoreFrames)
        {
            foreach (string id in _expected.Frames.Select(frame => frame.Id).Union(_target.Frames.Select(frame => frame.Id)))
            {
                var before = _expected.Frames.FirstOrDefault(frame => frame.Id == id);
                var after = _target.Frames.FirstOrDefault(frame => frame.Id == id);
                if (WorkspaceHistory.SameValue(before, after)) continue;
                var actual = next.Frames.FirstOrDefault(frame => frame.Id == id);
                if (WorkspaceHistory.SameValue(actual, after)) continue;
                if (!WorkspaceHistory.SameValue(actual, before))
                { error = $"The group “{before?.Name ?? after?.Name ?? id}” changed since this edit."; return false; }
                int index = next.Frames.FindIndex(frame => frame.Id == id);
                if (after is null) next.Frames.RemoveAt(index);
                else if (index >= 0) next.Frames[index] = after.Copy();
                else next.Frames.Insert(Math.Min(_target.Frames.FindIndex(frame => frame.Id == id), next.Frames.Count), after.Copy());
            }
            // A restored membership cannot silently steal a node from a newer
            // frame or reference a node that no longer exists.
            var normalized = GraphFrames.Normalize(next.Frames, next.Nodes.Select(node => node.Id));
            if (!GraphFrames.Same(next.Frames, normalized))
            { error = "A group membership changed since this edit."; return false; }
        }
        error = GraphDefaults.Validate(next);
        if (error.Length > 0) return false;
        foreach (var node in next.Nodes)
        {
            // Input lanes may intentionally share one capture device or app;
            // only physical/virtual playback sinks must remain exclusive.
            if (node.Kind is "output" or "virtualOutput"
                && VirtualDeviceBindings.IsAssigned(node.Device)
                && VirtualDeviceBindings.IsUsed(next, node.Id, new Endpoint { Id = node.Device.Id, StableId = node.Device.StableId }))
            { error = $"The device for “{node.Name}” is now used by another node."; return false; }
        }
        restored = next;
        return true;
    }
}
