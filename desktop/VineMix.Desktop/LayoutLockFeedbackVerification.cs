using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VineMix.Desktop;

/// <summary>Exercise the real lock-refusal signal without windows, input injection or audio access.</summary>
internal static class LayoutLockFeedbackVerification
{
    internal static void Run(Action<bool, string> require, Action<FrameworkElement, string>? render = null)
    {
        using var mixer = new MixerViewModel(preview: true);
        var source = GraphDefaults.NewNode("microphone", 32, 32); source.Id = "feedback-input";
        var output = GraphDefaults.NewNode("output", 500, 32); output.Id = "feedback-output";
        mixer.ApplyPreviewSnapshot(new() { Configuration = new() { Version = 2, Revision = 1,
            Graph = new() { Nodes = [source, output], Edges = [new() { Id = "feedback-cable", From = source.Id, To = output.Id }] } } });
        var workspace = new GraphWorkspace(mixer, useSystemClipboard: false) { Width = 1000, Height = 650 };
        workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        workspace.Measure(new Size(1000, 650)); workspace.Arrange(new Rect(0, 0, 1000, 650)); workspace.UpdateLayout();
        int notifications = 0;
        mixer.LayoutEditBlocked += (_, _) => notifications++;
        string Original() => JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        string original = Original();
        try
        {
            mixer.IsLayoutLocked = true;
            workspace.SelectNode(source.Id); workspace.SelectEdge("feedback-cable");
            _ = workspace.CreateAddMenu(new Point()); _ = workspace.CreateEdgeMenu("feedback-cable");
            _ = mixer.CanUndo; _ = mixer.CanRedo; _ = mixer.CanEditLayout;
            _ = GraphDefaults.ConnectionError(mixer.GraphConfig, new() { Id = "check", From = source.Id, To = output.Id });
            require(notifications == 0, "Lock toggle, node/cable selection, menu construction and read-only validation never signal a blocked edit.");

            require(workspace.BeginNodePointerGesture(source.Id, new Point(100, 100), ModifierKeys.None),
                "A locked node pointer press enters quiet selection tracking without moving the node.");
            workspace.UpdateLockedLayoutGesture(new Point(102, 101)); workspace.FinishGesture();
            require(notifications == 0 && Original() == original && !workspace.HasActiveGesture,
                "A locked node click with ordinary pointer jitter stays silent and preserves graph, selection and history.");
            workspace.BeginNodePointerGesture(source.Id, new Point(100, 100), ModifierKeys.None);
            workspace.UpdateLockedLayoutGesture(new Point(104, 100));
            for (int step = 1; step <= 100; step++) workspace.UpdateLockedLayoutGesture(new Point(104 + step, 110));
            require(notifications == 1 && !workspace.IsNodeDragging && Original() == original && !mixer.CanUndo,
                "Real locked node drag intent signals once after the movement threshold; further samples never move the graph or repeat the signal.");
            workspace.HandleCaptureLoss(); workspace.UpdateLockedLayoutGesture(new Point(400, 400));
            require(notifications == 1 && !workspace.HasActiveGesture,
                "Capture loss clears locked drag tracking; later pointer movement cannot trigger a stale pulse.");

            workspace.SelectEdge("feedback-cable"); workspace.BeginLockedLayoutGesture(new Point(200, 200)); workspace.FinishGesture();
            require(notifications == 1, "Selecting a locked cable without moving it remains quiet.");
            workspace.BeginLockedLayoutGesture(new Point(200, 200)); workspace.UpdateLockedLayoutGesture(new Point(210, 200)); workspace.FinishGesture();
            require(notifications == 2 && Original() == original,
                "A locked cable drag signals once without disconnecting or previewing a moved cable.");
            var inputSocket = Descendants<GraphNodeView>(workspace).Single(view => view.Id == output.Id).FindSocket("audio", false)!;
            var outputSocket = Descendants<GraphNodeView>(workspace).Single(view => view.Id == source.Id).FindSocket("audio", true)!;
            outputSocket.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            workspace.BeginInputLinkDrag(inputSocket, new Point()); workspace.RemoveEdge("feedback-cable");
            require(notifications == 5 && !workspace.HasConnectionPreview && Original() == original,
                "Socket activation, explicit cable reconnect and removal each emit one lock refusal without a graph mutation.");

            mixer.Edit(c => c.Graph.Nodes[0].GainDb = -6);
            require(notifications == 5 && mixer.Undo() && notifications == 5 && Original() == original,
                "Allowed sound changes and their Undo never emit blocked-layout feedback.");
            mixer.Edit(c => c.Graph.Nodes[0].X += 8);
            require(notifications == 6 && Original() == original,
                "The central model guard emits one refusal for a structural callback that bypasses workspace controls.");
            mixer.IsLayoutLocked = false;
            mixer.Edit(c => c.Graph.Nodes[0].X += 8);
            mixer.IsLayoutLocked = true;
            string moved = Original();
            require(!mixer.Undo() && notifications == 7 && Original() == moved,
                "Structural Undo emits one refusal and retains both the graph and history position.");
            mixer.IsLayoutLocked = false; mixer.Undo(); mixer.IsLayoutLocked = true;
            require(!mixer.Redo() && notifications == 8 && Original() == original,
                "Structural Redo emits one refusal and retains its pending history step.");
            workspace.BeginNodePointerGesture(source.Id, new Point(100, 100), ModifierKeys.None);
            mixer.IsLayoutLocked = false;
            workspace.UpdateLockedLayoutGesture(new Point(200, 200)); mixer.NotifyLayoutEditBlocked();
            require(notifications == 8 && !workspace.HasActiveGesture,
                "Unlocking cancels pending locked-pointer tracking; neither the toggle nor unlocked notifications emit a refusal.");
            require(PresentationSource.FromVisual(workspace) is null,
                "Lock-refusal feedback verification stays detached from the desktop and audio engine.");
        }
        finally { workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        VerifyLockedSelection(require, render);
    }
    private static void VerifyLockedSelection(Action<bool, string> require, Action<FrameworkElement, string>? render)
    {
        using var mixer = new MixerViewModel(preview: true);
        var source = GraphDefaults.NewNode("microphone", 32, 32); source.Id = "selection-input";
        var output = GraphDefaults.NewNode("output", 500, 32); output.Id = "selection-output";
        var other = GraphDefaults.NewNode("gain", 950, 32); other.Id = "selection-other";
        mixer.ApplyPreviewSnapshot(new() { Configuration = new() { Version = 2, Graph = new()
        {
            Nodes = [source, output, other],
            Edges = [new() { Id = "selection-cable", From = source.Id, To = output.Id }]
        } } });
        var workspace = new GraphWorkspace(mixer, useSystemClipboard: false) { Width = 1400, Height = 600 };
        workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        void Arrange() { workspace.Measure(new Size(1400, 600)); workspace.Arrange(new Rect(0, 0, 1400, 600)); workspace.UpdateLayout(); }
        Arrange();
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        bool Outlined(params string[] ids) => Descendants<GraphNodeView>(workspace).All(view => view.SelectionOutlineVisible == ids.Contains(view.Id));
        string GraphJson() => JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        try
        {
            workspace.SelectNode(source.Id); workspace.SelectNodeForPointer(other.Id, ModifierKeys.Control);
            require(Outlined(source.Id, other.Id), "Unlocked single and group selections retain their normal node outlines.");
            string original = GraphJson();
            var sizes = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id, view => view.RenderSize);
            render?.Invoke(workspace, "layout-selection-unlocked.png");
            mixer.IsLayoutLocked = true; Arrange();
            require(Outlined() && workspace.SelectedNodeIds.ToHashSet().SetEquals([source.Id, other.Id]) && GraphJson() == original,
                "Locking immediately hides existing node selection outlines without clearing selection or editing the graph.");
            render?.Invoke(workspace, "layout-selection-locked.png");
            workspace.SelectNode(output.Id); workspace.SelectNodeForPointer(source.Id, ModifierKeys.Control);
            require(Outlined() && workspace.SelectedNodeIds.ToHashSet().SetEquals([source.Id, output.Id])
                && !string.IsNullOrEmpty(workspace.CopySelection()),
                "Selection changes while locked remain available for Copy and inspection without showing node outlines.");
            workspace.BeginNodePointerGesture(source.Id, new Point(100, 100), ModifierKeys.None);
            workspace.UpdateLockedLayoutGesture(new Point(110, 100)); workspace.FinishGesture();
            workspace.RemoveEdge("selection-cable");
            require(Outlined() && GraphJson() == original && !mixer.CanUndo,
                "Blocked node and cable edits leave nodes visually quiet and preserve the graph and history.");
            foreach (double zoom in new[] { .5, 1.5, 2d })
            {
                mixer.Edit(config => config.Graph.Zoom = zoom); Arrange();
                require(Outlined() && Descendants<GraphNodeView>(workspace).All(view => view.RenderSize == sizes[view.Id]),
                    "Locked selection remains hidden through refresh and zoom without changing node geometry at " + zoom * 100 + "%.");
            }
            mixer.Edit(config => { config.Graph.Zoom = 1; config.Graph.Nodes[0].GainDb = -6; }); Arrange();
            require(Outlined() && mixer.GraphConfig.Nodes[0].GainDb == -6,
                "Sound controls remain usable while their selected node has no outline in a locked layout.");
            workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Arrange();
            require(Outlined() && mixer.IsLayoutLocked, "Reopening the same locked workspace keeps node selection outlines hidden.");
            mixer.IsLayoutLocked = false; Arrange();
            require(Outlined(source.Id, output.Id) && !View(other.Id).SelectionOutlineVisible,
                "Unlocking restores outlines for exactly the current selection, including nodes selected while locked.");
            render?.Invoke(workspace, "layout-selection-restored.png");
            require(PresentationSource.FromVisual(workspace) is null, "Selection checks remain detached from the personal workspace and audio engine.");
        }
        finally { workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
