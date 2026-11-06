using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VineMix.Desktop;

public sealed partial class GraphWorkspace
{
    internal sealed record PipelineChoice(string NodeId, string? EdgeId, string Label, string? FromPort = null, string? ToPort = null);
    private readonly Dictionary<(string NodeId, bool Forward), (string EdgeId, string OtherNodeId)> _pipelineReturnEdges = [];
    private IReadOnlyList<PipelineChoice> _pipelineChoices = [];
    private string? _pipelineOrigin;
    private bool _pipelineForward;
    private bool _pipelineSelecting;
    private ContextMenu? _pipelineMenu;
    private DependencyObject? _pipelineFocusReturn;
    private readonly Dictionary<string, Point> _keyboardNudgeOrigins = [];
    private readonly HashSet<Key> _cancelledNudgeKeys = [];
    private Key? _keyboardNudgeKey;
    private Vector _keyboardNudgeDelta;
    internal IReadOnlyList<PipelineChoice> PipelineChoices => _pipelineChoices;
    internal bool KeyboardNudgeActive => _keyboardNudgeKey is not null;

    private void InitializeKeyboardNavigation()
    {
        PreviewKeyUp += (_, args) => ReleaseCanvasArrow(args.Key);
        LostKeyboardFocus += (_, args) => { if (ReferenceEquals(args.OldFocus, this)) FinishKeyboardNudge(); };
    }

    internal bool TryPipelineShortcut(Key key, ModifierKeys modifiers, DependencyObject? focused)
    {
        if (key != Key.Tab || modifiers != ModifierKeys.Control && modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return false;
        var focusedNode = FindParent<GraphNodeView>(focused);
        if (!ReferenceEquals(focused, this) && focusedNode is null && FindParent<GraphFrameView>(focused) is null) return false;
        FinishKeyboardNudge();
        if (!CanFrameSelection) return true;
        // Do not make navigation a way around an incomplete numeric draft.
        if (!UpdateEditorCommit.TryCommit(focused))
        { SetMessage("Correct the value or press Escape before following the pipeline.", isError: true); return true; }
        bool forward = (modifiers & ModifierKeys.Shift) == 0;
        string? current = focusedNode?.Id ?? (_selectedNodes.Count == 1 ? _selectedNodes.Single() : null);
        if (current is null)
        {
            var candidates = Graph.Nodes.Where(n => _selectedNodes.Count == 0 || _selectedNodes.Contains(n.Id))
                .OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(n => n.Id, StringComparer.Ordinal)
                .Select(n => new PipelineChoice(n.Id, null, n.Name + " · " + GraphDefaults.KindLabel(n.Kind))).ToArray();
            ShowPipelineChoices(candidates, null, forward, focused); return true;
        }
        var routes = Graph.Edges.Where(e => e.Kind == "audio" && (forward ? e.From == current : e.To == current))
            .Where(e => _nodes.ContainsKey(forward ? e.To : e.From)).ToArray();
        if (routes.Length == 0)
        { SetMessage(forward ? "End of this audio pipeline." : "Start of this audio pipeline."); return true; }
        if (_pipelineReturnEdges.TryGetValue((current, forward), out var remembered)
            && routes.FirstOrDefault(e => e.Id == remembered.EdgeId && (forward ? e.To : e.From) == remembered.OtherNodeId) is { } previous)
            FollowPipelineRoute(current, previous.Id, forward);
        else if (routes.Length == 1) FollowPipelineRoute(current, routes[0].Id, forward);
        else
        {
            var choices = routes.Select(edge =>
            {
                string id = forward ? edge.To : edge.From;
                var target = Graph.Nodes.Single(n => n.Id == id);
                string ports = GraphDefaults.PortLabel(edge.FromPort, Graph.Nodes.FirstOrDefault(node => node.Id == edge.From)?.Kind)
                    + " → " + GraphDefaults.PortLabel(edge.ToPort, Graph.Nodes.FirstOrDefault(node => node.Id == edge.To)?.Kind);
                return new PipelineChoice(id, edge.Id, target.Name + " · " + ports + (edge.Muted ? " · muted cable" : ""), edge.FromPort, edge.ToPort);
            }).OrderBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.EdgeId, StringComparer.Ordinal).ToArray();
            ShowPipelineChoices(choices, current, forward, focused);
        }
        return true;
    }

    private void ShowPipelineChoices(IReadOnlyList<PipelineChoice> choices, string? origin, bool forward, DependencyObject? focused)
    {
        ClosePipelineChoices();
        if (choices.Count == 0) { SetMessage("Add a node to start navigating an audio pipeline."); return; }
        _pipelineChoices = choices; _pipelineOrigin = origin; _pipelineForward = forward; _pipelineFocusReturn = focused;
        var menu = _pipelineMenu = TrackPracticeMenu(new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Center, MaxHeight = 480 });
        menu.Items.Add(new MenuItem { Header = origin is null ? "Choose a starting node" : forward ? "Follow audio to…" : "Follow audio from…", IsEnabled = false });
        for (int index = 0; index < choices.Count; ++index)
        {
            int choice = index;
            var item = new MenuItem { Header = choices[index].Label };
            item.Click += (_, _) => ChoosePipelineOption(choice); menu.Items.Add(item);
        }
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_pipelineMenu, menu)) return;
            _pipelineMenu = null; _pipelineChoices = [];
            if (_pipelineFocusReturn is IInputElement previous) Keyboard.Focus(previous);
            _pipelineFocusReturn = null;
        };
        SetMessage(origin is null ? "Choose a starting node." : "Choose the audio connection to follow.");
        // Detached verification builds the same menu but never creates a popup
        // window or sends keyboard input to the desktop.
        if (PresentationSource.FromVisual(this) is not null)
        {
            menu.Opened += (_, _) => ((MenuItem)menu.Items[1]).Focus();
            menu.IsOpen = true;
        }
    }

    private void ClosePipelineChoices()
    {
        var menu = _pipelineMenu; _pipelineMenu = null;
        _pipelineChoices = []; _pipelineFocusReturn = null;
        if (menu is not null) { menu.IsOpen = false; _practiceMenus.Remove(menu); }
    }

    internal bool ChoosePipelineOption(int index)
    {
        if (index < 0 || index >= _pipelineChoices.Count) return false;
        var choice = _pipelineChoices[index]; string? origin = _pipelineOrigin; bool forward = _pipelineForward;
        ClosePipelineChoices();
        if (origin is not null && choice.EdgeId is not null) return FollowPipelineRoute(origin, choice.EdgeId, forward, choice);
        return FocusPipelineNode(choice.NodeId);
    }

    private bool FollowPipelineRoute(string origin, string edgeId, bool forward, PipelineChoice? offered = null)
    {
        var edge = Graph.Edges.FirstOrDefault(e => e.Id == edgeId && e.Kind == "audio" && (forward ? e.From == origin : e.To == origin));
        if (edge is null) { SetMessage("That connection changed. Choose the pipeline again."); return false; }
        string destination = forward ? edge.To : edge.From;
        if (offered is not null && (destination != offered.NodeId || edge.FromPort != offered.FromPort || edge.ToPort != offered.ToPort))
        { SetMessage("That connection changed. Choose the pipeline again."); return false; }
        if (!FocusPipelineNode(destination)) return false;
        // Remember the edge actually used, including distinct stereo ports.
        _pipelineReturnEdges[(destination, !forward)] = (edge.Id, origin);
        return true;
    }

    private bool FocusPipelineNode(string id)
    {
        if (!_nodes.TryGetValue(id, out var view) || !RevealNodeForControls(id)) return false;
        _pipelineSelecting = true;
        try { SelectNode(id); } finally { _pipelineSelecting = false; }
        view.UpdateLayout();
        if (PresentationSource.FromVisual(this) is not null && !view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))) Focus();
        SetMessage("Selected " + Graph.Nodes.Single(n => n.Id == id).Name + ". Tab moves through its controls; Ctrl+Tab follows its audio pipeline.");
        return true;
    }

    internal bool TryRenameSelectionShortcut(Key key, ModifierKeys modifiers, DependencyObject? focused)
    {
        if (key != Key.F2 || modifiers != ModifierKeys.None || IsEditingControl(focused) || !CanFrameSelection) return false;
        if (_selectedFrame is string frameId) { BeginFrameRename(frameId); return true; }
        if (_selectedEdge is not null || _selectedNodes.Count != 1) return false;
        if (_nodes.TryGetValue(_selectedNodes.Single(), out var node)) { node.BeginRename(); return true; }
        return false;
    }

    internal bool TryKeyboardZoom(Key key, ModifierKeys modifiers, DependencyObject? focused)
    {
        // WPF reports the shared +/= key as OemPlus, whether Shift is held or
        // not. Numpad operators have separate key values.
        int step = modifiers == ModifierKeys.Control
            ? key switch { Key.OemPlus or Key.Add => 1, Key.OemMinus or Key.Subtract => -1, _ => 0 }
            : key == Key.OemPlus && modifiers == (ModifierKeys.Control | ModifierKeys.Shift) ? 1 : 0;
        if (step == 0 || IsEditingControl(focused) || !ReferenceEquals(FindParent<GraphWorkspace>(focused), this)
            || !CanFrameSelection || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return false;
        _wheelRemainder = 0;
        StepZoom(new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2), step);
        return true;
    }

    internal bool TryCanvasArrow(Key key, ModifierKeys modifiers, DependencyObject? focused, bool isRepeat = false)
    {
        if (key is not (Key.Left or Key.Right or Key.Up or Key.Down) || modifiers is not (ModifierKeys.None or ModifierKeys.Shift)
            || !ReferenceEquals(focused, this)) return false;
        // A new physical press recovers even if its previous key-up happened
        // outside this window. Repeats of an invalidated hold stay consumed.
        if (!isRepeat) _cancelledNudgeKeys.Remove(key);
        InvalidateKeyboardNudge(Graph);
        if (_cancelledNudgeKeys.Contains(key)) return true;
        if (_keyboardNudgeKey != key) FinishKeyboardNudge();
        if (!KeyboardNudgeActive && !CanFrameSelection) return false;
        var direction = key switch { Key.Left => new Vector(-1, 0), Key.Right => new Vector(1, 0), Key.Up => new Vector(0, -1), _ => new Vector(0, 1) };
        var selected = Graph.Nodes.Where(n => _selectedNodes.Contains(n.Id)).ToArray();
        if (selected.Length == 0)
        {
            _panX = Math.Clamp(_panX - direction.X * 40, -GraphCanvasPolicy.MaximumPan, GraphCanvasPolicy.MaximumPan);
            _panY = Math.Clamp(_panY - direction.Y * 40, -GraphCanvasPolicy.MaximumPan, GraphCanvasPolicy.MaximumPan);
            ApplyTransform(); PersistView(); return true;
        }
        if (!RequireLayoutEdit()) return true; // A locked selection never becomes a pan.
        if (!KeyboardNudgeActive)
        {
            foreach (var node in selected) _keyboardNudgeOrigins[node.Id] = new Point(node.X, node.Y);
            _keyboardNudgeKey = key; _keyboardNudgeDelta = new Vector();
        }
        var delta = _keyboardNudgeDelta + direction * (modifiers == ModifierKeys.Shift ? 1 : 10);
        _keyboardNudgeDelta = new Vector(
            Math.Clamp(delta.X, -100000 - _keyboardNudgeOrigins.Values.Min(p => p.X), 100000 - _keyboardNudgeOrigins.Values.Max(p => p.X)),
            Math.Clamp(delta.Y, -100000 - _keyboardNudgeOrigins.Values.Min(p => p.Y), 100000 - _keyboardNudgeOrigins.Values.Max(p => p.Y)));
        foreach (var (id, origin) in _keyboardNudgeOrigins)
            if (_nodes.TryGetValue(id, out var node))
            { Canvas.SetLeft(node, origin.X + _keyboardNudgeDelta.X); Canvas.SetTop(node, origin.Y + _keyboardNudgeDelta.Y); }
        UpdateWires(); RefreshFrames(); RefreshMinimap(); return true;
    }

    private void InvalidateKeyboardNudge(GraphConfiguration graph)
    {
        if (_keyboardNudgeKey is not { } key || _keyboardNudgeOrigins.Keys.All(id => graph.Nodes.Any(n => n.Id == id))) return;
        _cancelledNudgeKeys.Add(key);
        _keyboardNudgeKey = null; _keyboardNudgeOrigins.Clear(); _keyboardNudgeDelta = new Vector();
        SetMessage("Move cancelled because a selected node was removed.");
    }

    internal void ReleaseCanvasArrow(Key key)
    {
        _cancelledNudgeKeys.Remove(key);
        if (key == _keyboardNudgeKey) FinishKeyboardNudge();
    }

    internal void FinishKeyboardNudge(bool cancel = false)
    {
        if (!KeyboardNudgeActive) return;
        var origins = new Dictionary<string, Point>(_keyboardNudgeOrigins); var delta = _keyboardNudgeDelta;
        if (cancel) _cancelledNudgeKeys.Add(_keyboardNudgeKey!.Value);
        _keyboardNudgeKey = null; _keyboardNudgeOrigins.Clear(); _keyboardNudgeDelta = new Vector();
        if (cancel || !_mixer.CanEditLayout || delta.LengthSquared == 0) { Refresh(); return; }
        // One held key makes one edit. Guard coordinates on replay, so a remote
        // movement is not overwritten by an old keyboard preview.
        if (!_mixer.TryEdit((MixerConfiguration configuration, out string error) =>
        {
            error = "";
            var nodes = origins.Keys.Select(id => configuration.Graph.Nodes.FirstOrDefault(n => n.Id == id)).ToArray();
            if (nodes.Any(n => n is null)) { error = "A selected node was removed during the move."; return false; }
            if (nodes.All(n => new Point(n!.X, n.Y) == origins[n.Id] + delta)) return true;
            if (nodes.Any(n => new Point(n!.X, n.Y) != origins[n.Id]))
            { error = "A selected node moved elsewhere. The latest position was kept."; return false; }
            foreach (var node in nodes) { var position = origins[node!.Id] + delta; node.X = position.X; node.Y = position.Y; }
            return true;
        }, out string moveError)) SetMessage(moveError, isError: true);
        Refresh();
    }
}
