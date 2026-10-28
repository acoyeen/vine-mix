using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VineMix.Desktop;

/// <summary>Session stacking through real selection/drag handlers, without input injection or audio access.</summary>
internal static class NodeStackingVerification
{
    internal static void Run(Action<bool, string> require, Action<FrameworkElement, string>? render = null)
    {
        using var mixer = new MixerViewModel(preview: true);
        var a = Node("gain", "stack-a", 64, 64);
        var b = Node("pan", "stack-b", 64, 64);
        var c = Node("mono", "stack-c", 64, 64);
        var d = Node("expander", "stack-d", 600, 64);
        var e = Node("control", "stack-e", 1120, 64);
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new GraphConfiguration
        {
            Nodes = [a, b, c, d, e], Edges = [new() { Id = "stack-cable", From = a.Id, To = b.Id }],
            Frames = [new() { Id = "stack-frame", Name = "Overlap example", NodeIds = [a.Id, b.Id, c.Id] }],
            Zoom = .75, PanX = 24, PanY = 40
        };
        mixer.ApplyPreviewSnapshot(fixture);
        var workspace = new GraphWorkspace(mixer, useSystemClipboard: false) { Width = 1500, Height = 800 };
        workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        void Layout()
        {
            workspace.Measure(new Size(1500, 800)); workspace.Arrange(new Rect(0, 0, 1500, 800)); workspace.UpdateLayout();
        }
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        int Z(string id) => Panel.GetZIndex(View(id));
        string[] Order() => Descendants<GraphNodeView>(workspace).OrderBy(Panel.GetZIndex).Select(view => view.Id).ToArray();
        string Json() => JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        string Positions() => JsonSerializer.Serialize(mixer.GraphConfig.Nodes.Select(node => new { node.Id, node.X, node.Y }), Protocol.Json);
        Rect Bounds(string id) => new(Canvas.GetLeft(View(id)), Canvas.GetTop(View(id)), View(id).ActualWidth, View(id).ActualHeight);
        Point Screen(Point point) => new(point.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX,
            point.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
        void Rectangle(Rect bounds, bool additive) => workspace.SelectRectangle(Screen(bounds.TopLeft), Screen(bounds.BottomRight), additive);
        void ExpectOrder(IEnumerable<string> expected, string message) => require(Order().SequenceEqual(expected), message);
        try
        {
            Layout();
            string initial = Json(), preferences = mixer.SerializeUiPreferences();
            long presentations = mixer.ConfigurationPresentationCount;
            ExpectOrder([a.Id, b.Id, c.Id, d.Id, e.Id], "New nodes initially retain the graph's existing paint order.");
            workspace.SelectNode(a.Id); workspace.SelectNode(b.Id);
            require(Z(b.Id) > Z(a.Id) && Z(a.Id) > Z(c.Id),
                "Programmatic selection places the most recently selected overlapping node above earlier selections.");
            workspace.SelectNode(a.Id); workspace.SelectNode(a.Id);
            require(Order()[^1] == a.Id, "Selecting an already-selected node keeps its session paint position at the front.");
            workspace.SelectNodeForPointer(b.Id, ModifierKeys.Shift);
            workspace.SelectNodeForPointer(c.Id, ModifierKeys.Control);
            require(Z(c.Id) > Z(b.Id) && Z(b.Id) > Z(a.Id) && workspace.SelectedNodeIds.Count == 3,
                "Additive pointer selection raises each newly selected node without losing the selection.");
            var beforeToggle = Order();
            workspace.SelectNodeForPointer(b.Id, ModifierKeys.Shift);
            ExpectOrder(beforeToggle, "Toggling a node out of the selection does not bring the deselected node forward.");
            workspace.SelectNodeForPointer(b.Id, ModifierKeys.Shift);
            workspace.PopulateNodeMenu(new ContextMenu(), a.Id, () => { });
            require(Order()[^1] == a.Id && workspace.SelectedNodeIds.Count == 3,
                "Right-clicking an already-selected node raises it while retaining the multi-selection.");
            workspace.SelectNodeForPointer(c.Id, ModifierKeys.None);
            require(Order()[^1] == c.Id && workspace.SelectedNodeIds.Count == 3,
                "An ordinary pointer click on an already-selected member raises that member without clearing its peers.");
            require(Json() == initial && mixer.SerializeUiPreferences() == preferences
                && mixer.ConfigurationPresentationCount == presentations && !mixer.CanUndo,
                "Selection stacking changes no graph values, node storage order, preferences, history or engine edits.");

            workspace.BeginNodeMove(a.Id, new Point(100, 100));
            require(Order()[^1] == a.Id && Z(c.Id) > Z(b.Id)
                && new[] { a.Id, b.Id, c.Id }.All(id => Z(id) > Z(d.Id) && Z(id) > Z(e.Id)),
                "Dragging a selected anchor raises the moving set together, keeps peer order and places the anchor on top.");
            workspace.UpdateNodeMove(new Point(112, 106));
            var duringDrag = Order();
            workspace.FinishNodeMove(); Layout();
            ExpectOrder(duringDrag, "Releasing an overlapping drag retains its interaction order instead of resetting node Z indices.");
            mixer.RefreshPreviewTelemetry(); Layout();
            ExpectOrder(duringDrag, "Telemetry refresh preserves the completed drag's stacking order.");
            string beforeCancel = Positions(); presentations = mixer.ConfigurationPresentationCount;
            workspace.BeginNodeMove(b.Id, new Point(100, 100)); workspace.UpdateNodeMove(new Point(150, 135));
            workspace.FinishNodeMove(cancel: true); Layout();
            require(Order()[^1] == b.Id && Positions() == beforeCancel && mixer.ConfigurationPresentationCount == presentations,
                "Cancelling a drag restores positions while keeping the explicitly interacted node in front, with no configuration edit.");

            mixer.IsLayoutLocked = true;
            beforeCancel = Positions(); presentations = mixer.ConfigurationPresentationCount;
            workspace.BeginNodePointerGesture(c.Id, new Point(100, 100), ModifierKeys.None);
            workspace.UpdateLockedLayoutGesture(new Point(150, 150)); workspace.FinishGesture();
            require(Order()[^1] == c.Id && Positions() == beforeCancel && mixer.ConfigurationPresentationCount == presentations,
                "A locked node can still be selected and raised while drag intent leaves every position unchanged.");
            mixer.IsLayoutLocked = false;

            workspace.SelectNode(d.Id); workspace.SelectNode(a.Id);
            var beforeRecreation = Order(); var oldView = View(d.Id);
            workspace.NodeChanged(d.Id, node => node.Expander.Mode = "gate"); Layout();
            require(!ReferenceEquals(oldView, View(d.Id)), "The stacking fixture exercises actual node-view recreation for a changed processor presentation.");
            ExpectOrder(beforeRecreation, "Recreating a node view retains its identity's paint position and cannot cover a newer selection.");
            workspace.NodeChanged(d.Id, node => node.GainDb = -9); Layout();
            ExpectOrder(beforeRecreation, "Changing a sound control without selecting its node does not promote it.");
            mixer.SetCanvasView(35, 45, .5); mixer.RefreshPreviewTelemetry(); Layout();
            ExpectOrder(beforeRecreation, "Zoom, pan and view-scale refresh retain session stacking.");
            mixer.Edit(configuration => configuration.Graph.Nodes.Reverse()); Layout();
            ExpectOrder(beforeRecreation, "A configuration refresh with another node storage order does not override interaction recency.");

            workspace.SelectNode(a.Id);
            Rect dInterior = Bounds(d.Id); dInterior.Inflate(-4, -4);
            Rectangle(dInterior, additive: true);
            require(workspace.SelectedNodeIds.ToHashSet().SetEquals([a.Id, d.Id]) && Order()[^1] == d.Id,
                "Adding a node by box selection raises the newly added member above the earlier selected node.");
            workspace.SelectNode(e.Id);
            var pairOrder = Order().Where(id => id == d.Id || id == e.Id).ToArray();
            var pairBounds = Bounds(d.Id); pairBounds.Union(Bounds(e.Id)); pairBounds.Inflate(3, 3);
            Rectangle(pairBounds, additive: false);
            require(Order().TakeLast(2).SequenceEqual(pairOrder) && workspace.SelectedNodeIds.ToHashSet().SetEquals([d.Id, e.Id]),
                "Replacing selection with a box-selected batch preserves that batch's relative front-to-back order.");

            var beforeBox = Order(); var selectedBeforeBox = workspace.SelectedNodeIds.ToHashSet();
            var cluster = Bounds(a.Id); cluster.Union(Bounds(b.Id)); cluster.Union(Bounds(c.Id)); cluster.Inflate(3, 3);
            workspace.BeginBoxSelection(Screen(cluster.TopLeft), additive: false);
            workspace.UpdateBoxSelection(Screen(cluster.BottomRight)); Layout();
            require(Order().TakeLast(3).SequenceEqual(beforeBox.Where(id => id == a.Id || id == b.Id || id == c.Id)),
                "A live box raises its members as a stable batch rather than reshuffling overlaps during pointer movement.");
            workspace.FinishGesture(cancel: true); Layout();
            require(Order().SequenceEqual(beforeBox) && workspace.SelectedNodeIds.ToHashSet().SetEquals(selectedBeforeBox),
                "Cancelling a box gesture restores both the original selection and its paint order.");
            workspace.BeginBoxSelection(Screen(cluster.TopLeft), additive: true);
            workspace.UpdateBoxSelection(Screen(cluster.BottomRight));
            var additiveOrder = Order(); workspace.UpdateBoxSelection(Screen(cluster.BottomRight)); workspace.FinishGesture(); Layout();
            ExpectOrder(additiveOrder, "Repeated box updates and successful completion retain the same stable added-member stacking.");

            var framedOrder = Order().Where(id => id == a.Id || id == b.Id || id == c.Id).ToArray();
            workspace.SelectFrame("stack-frame");
            require(Order().TakeLast(3).SequenceEqual(framedOrder)
                && workspace.SelectedNodeIds.ToHashSet().SetEquals([a.Id, b.Id, c.Id]),
                "Selecting an organizational frame raises its member batch without changing the members' relative overlap order.");
            var beforeFrameMove = Order(); beforeCancel = Positions();
            workspace.BeginFrameMove("stack-frame", new Point(100, 100)); workspace.UpdateNodeMove(new Point(130, 120));
            workspace.FinishNodeMove(cancel: true); Layout();
            require(Order().SequenceEqual(beforeFrameMove) && Positions() == beforeCancel,
                "Moving a frame does not arbitrarily promote its first member, and cancelling preserves member stacking and positions.");

            var world = (Canvas)VisualTreeHelper.GetParent(View(a.Id));
            var cableLayer = world.Children.OfType<Canvas>().Single();
            require(Descendants<GraphNodeView>(workspace).All(view => Panel.GetZIndex(view) > Panel.GetZIndex(cableLayer))
                && workspace.FrameViews.Values.All(frame => Panel.GetZIndex(frame) < Z(a.Id)),
                "All node stacking remains above the existing cable layer and organizational frame backgrounds.");
            var indices = Descendants<GraphNodeView>(workspace).Select(Panel.GetZIndex).Order().ToArray();
            require(indices.SequenceEqual(Enumerable.Range(1, indices.Length)),
                "Paint ranks stay bounded and distinct instead of growing a counter across repeated interactions.");
            workspace.SelectNode(a.Id); Layout();
            render?.Invoke(workspace, "node-stacking-overlap.png");
        }
        finally
        {
            workspace.CancelActiveGesture();
            workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
    }

    private static GraphNode Node(string kind, string id, double x, double y)
    {
        var node = GraphDefaults.NewNode(kind, x, y);
        node.Id = id; node.Name = id; node.Collapsed = true;
        return node;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
