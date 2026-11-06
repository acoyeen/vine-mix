using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

// Detached geometry and presentation checks; no input injection or audio I/O.
internal static class ZoomDetailVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<string> render)
    {
        require(mixer.IsPreview, "Wide-zoom qualification uses an inert model.");
        var camera = new GraphCanvasView(-230, 175, .75);
        var anchor = new Point(319, 207);
        foreach (double nextZoom in new[] { .1, .25, 1, 2 })
            require((GraphCanvasPolicy.ZoomAround(camera, anchor, nextZoom).ToWorld(anchor) - camera.ToWorld(anchor)).Length < .000001,
                "Shared camera mathematics keeps the same world point beneath the zoom anchor.");
        var original = UiPreview.CreateSnapshot([]);
        original.Configuration.Graph = mixer.GraphConfig.Copy();
        original.Status.Nodes = mixer.GraphStatus.ToList();
        var fixture = UiPreview.CreateSnapshot([]);
        var graph = new GraphConfiguration { Zoom = 1, PanX = 24, PanY = 24 };
        string[] variedKinds = ["gain", "mix", "compressor", "expander", "tone"];
        for (int index = 0; index < 32; index++)
        {
            string kind = variedKinds[index % variedKinds.Length];
            var node = GraphDefaults.NewNode(kind, index % 8 * 600 - 1000, index / 8 * 640 - 300);
            node.Id = "zoom-node-" + index; node.Name = "Example " + kind + " " + (index + 1);
            graph.Nodes.Add(node);
        }
        for (int from = 0; from < 31 && graph.Edges.Count < 96; from++)
            for (int to = from + 1; to < Math.Min(32, from + 5) && graph.Edges.Count < 96; to++)
                graph.Edges.Add(new() { Id = "zoom-edge-" + graph.Edges.Count, From = graph.Nodes[from].Id, To = graph.Nodes[to].Id });
        fixture.Configuration.Graph = graph;
        fixture.Status.Nodes = graph.Nodes.Select(node => new GraphNodeStatus { Id = node.Id, Channels = 2,
            PeakLeft = .2f, PeakRight = .1f, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        require(graph.Edges.Count == 96 && GraphDefaults.Validate(graph) == "", "The zoom fixture reaches the existing 32-node/96-cable limits.");
        var views = Descendants<GraphNodeView>(workspace).ToArray();
        var bounds = views.ToDictionary(view => view.Id, view => view.RenderSize);
        var anchors = views.SelectMany(view => Descendants<GraphWorkspace.Socket>(view)
            .Select(socket => (socket, view, point: socket.TranslatePoint(new Point(socket.ActualWidth / 2, socket.ActualHeight / 2), view)))).ToArray();
        string audio = JsonSerializer.Serialize(new { graph.Nodes, graph.Edges }, Protocol.Json);
        int history = mixer.EditHistory.Entries.Count, pending = mixer.PendingEditCount;
        async Task Zoom(double value)
        {
            mixer.SetCanvasView(24, 24, value); mixer.RefreshPreviewTelemetry(); await layout();
        }
        foreach (double zoom in new[] { .3, .25, .2, .15, .1, .2, .3, 2d, 1d })
        {
            await Zoom(zoom);
            var expected = zoom >= .3 ? NodeDetailLevel.Full : zoom >= .2 ? NodeDetailLevel.Compact : NodeDetailLevel.Overview;
            require(views.All(view => view.ViewScale == zoom && view.DetailLevel == expected && view.RenderSize == bounds[view.Id]),
                $"At {zoom:P0}, detail changes preserve every node's full world-space geometry.");
            require(anchors.All(item => (item.socket.TranslatePoint(new Point(item.socket.ActualWidth / 2, item.socket.ActualHeight / 2), item.view) - item.point).Length < .01),
                $"At {zoom:P0}, all cable anchors stay attached to the same node positions.");
            require(views.All(view => view.DetailsVisible == (expected == NodeDetailLevel.Full)
                && view.PeakMeterDisplay.Visibility == (expected == NodeDetailLevel.Full ? Visibility.Visible : Visibility.Hidden)),
                $"At {zoom:P0}, only full-detail nodes show dial bodies and audio meters; hidden controls do not take focus.");
            require(views.All(view => view.DetailHeaderVisible && view.DetailHeaderTextVisible == (expected == NodeDetailLevel.Full)),
                $"At {zoom:P0}, nodes retain their coloured header strip while simplified nodes hide its duplicate small name.");
            if (expected == NodeDetailLevel.Overview)
            {
                require(views.All(view => !view.PeakMeterDisplay.IsVisible)
                    && anchors.All(item => !item.socket.Focusable && !item.socket.IsHitTestVisible),
                    "Overview hides meter animation and disables tiny socket targets without deleting routes or retained meter state.");
                require(anchors.All(item =>
                {
                    Point position = workspace.SocketPosition(item.socket);
                    return workspace.FindSnapSocket(new Point(position.X * zoom + mixer.GraphConfig.PanX, position.Y * zoom + mixer.GraphConfig.PanY)) is null;
                }), "Canvas-level snapping cannot revive disabled overview sockets.");
            }
            if (zoom is .25 or .1 or 1) render($"wide-zoom-{zoom * 100:0}.png");
        }
        require(audio == JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json)
            && mixer.EditHistory.Entries.Count == history && mixer.PendingEditCount == pending,
            "Zoom and detail transitions change neither audio configuration nor queued edits/history.");
        require(GraphNodeView.DetailAt(.285, NodeDetailLevel.Full) == NodeDetailLevel.Full
            && GraphNodeView.DetailAt(.285, NodeDetailLevel.Compact) == NodeDetailLevel.Compact
            && GraphNodeView.DetailAt(.18, NodeDetailLevel.Overview) == NodeDetailLevel.Overview
            && GraphNodeView.DetailAt(.18, NodeDetailLevel.Compact) == NodeDetailLevel.Compact,
            "Detail thresholds have hysteresis instead of flickering at a boundary.");

        var first = views[0]; var dial = Descendants<RotaryValueControl>(first).First();
        var entry = Descendants<TextBox>(dial).Single();
        dial.BeginTextEntry(); entry.Text = "-";
        require(dial.HasPendingEntry, "A partial negative number is retained as an uncommitted draft.");
        await Zoom(.1);
        require(first.DetailsVisible && dial.HasPendingEntry && entry.Text == "-",
            "Zooming out never hides or discards an incomplete numeric draft.");
        dial.CancelPendingEntry(); mixer.RefreshPreviewTelemetry(); await layout();
        require(first.DetailLevel == NodeDetailLevel.Overview, "Simplification resumes after the draft is cancelled.");
        require(workspace.RevealNodeForControls(first.Id), "Control navigation can reveal a node from the overview.");
        await layout();
        require(first.DetailsVisible && mixer.GraphConfig.Zoom >= .6, "Control reveal restores readable controls before focus transfer.");
        workspace.FitAll(); await layout();
        require(mixer.GraphConfig.Zoom >= .1 && mixer.GraphConfig.Zoom < .3, "Fit can show the entire bounded graph below the former zoom floor.");
        var saved = JsonSerializer.Deserialize<GraphConfiguration>(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json), Protocol.Json)!;
        require(saved.Zoom == mixer.GraphConfig.Zoom && GraphDefaults.Validate(saved) == "", "Expanded zoom survives desktop persistence validation.");
        saved.Zoom = .099; require(GraphDefaults.Validate(saved).Length > 0, "Desktop validation rejects zoom below ten percent.");
        var edgeFixture = UiPreview.CreateSnapshot([]);
        var edgeNode = GraphDefaults.NewNode("mix", 100000, -100000); edgeNode.Id = "edge-reveal";
        edgeFixture.Configuration.Graph = new() { Nodes = [edgeNode], Zoom = 2 };
        mixer.ApplyPreviewSnapshot(edgeFixture); await layout();
        require(workspace.RevealNodeForControls(edgeNode.Id), "Pipeline reveal accepts a node at supported coordinate limits.");
        await layout();
        var edgeView = Descendants<GraphNodeView>(workspace).Single();
        var viewport = (FrameworkElement)VisualTreeHelper.GetParent(edgeView.Parent);
        var onScreen = edgeView.TransformToAncestor(viewport).TransformBounds(new Rect(edgeView.RenderSize));
        require(onScreen.IntersectsWith(new Rect(viewport.RenderSize)) && edgeView.DetailsVisible
            && Math.Abs(mixer.GraphConfig.PanX) <= GraphCanvasPolicy.MaximumPan && Math.Abs(mixer.GraphConfig.PanY) <= GraphCanvasPolicy.MaximumPan,
            "Revealing an extreme node reduces excessive zoom rather than leaving it invisible beyond the saved pan bounds.");
        mixer.ApplyPreviewSnapshot(original); await layout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
