using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Actual detached socket layout and hit testing; no windows, focus changes or input injection.</summary>
internal static class SocketPresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string>? render = null)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Socket presentation checks use a detached preview without an audio engine or native window.");
        var fixture = UiPreview.CreateSnapshot([]);
        var microphone = Node("microphone", "socket-mic", 80.25, 80.4);
        var compressor = Node("ducking", "socket-ducking", 560.2, -80.35);
        var control = Node("control", "socket-control", -80.25, 650.2);
        var output = Node("output", "socket-output", 1100.1, 80.4);
        var denoiser = Node("denoiser", "socket-denoiser", 560.3, 950.35);
        fixture.Configuration.Graph = new()
        {
            Nodes = [microphone, compressor, control, output, denoiser],
            PanX = 35, PanY = 22, Zoom = 1
        };
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        GraphWorkspace.Socket Socket(string id, string kind, bool outgoing = false) => View(id).FindSocket(kind, outgoing)!;
        GraphWorkspace.Socket[] Sockets() => Descendants<GraphWorkspace.Socket>(workspace).ToArray();
        var world = (Canvas)View(microphone.Id).Parent;
        var viewport = (FrameworkElement)VisualTreeHelper.GetParent(world);
        Point Screen(Point point) => world.TransformToAncestor(viewport).Transform(point);
        string Audio() => JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json);
        var originalSizes = fixture.Configuration.Graph.Nodes.ToDictionary(node => node.Id, node => View(node.Id).RenderSize);
        var originalCenters = Sockets().ToDictionary(socket => (socket.NodeId, socket.Kind, socket.Output),
            socket => socket.TranslatePoint(Center(socket.RenderSize), View(socket.NodeId)));

        void RequireState(GraphWorkspace.Socket socket, bool connected, string description)
        {
            var shape = (Shape)socket.Content;
            string color = socket.Kind == "control" ? "NodeControlBrush" : socket.Kind == "sidechain" ? "NodeSidechainBrush" : "NodeAudioBrush";
            require(socket.IsConnected == connected
                && AutomationProperties.GetItemStatus(socket) == (connected ? "Connected" : "Not connected")
                && SameBrush(shape.Fill, ConsoleTheme.Get(connected ? color : "NodeCanvasBrush"))
                && SameBrush(shape.Stroke, ConsoleTheme.Get(connected ? "NodeCanvasBrush" : color)), description);
        }

        foreach (double zoom in new[] { .3, .75, 1, 1.5, 2 })
        {
            mixer.Edit(config => config.Graph.Zoom = zoom); await layout();
            string audioBefore = Audio(); int historyBefore = mixer.EditHistory.Entries.Count;
            foreach (var socket in Sockets())
            {
                var view = View(socket.NodeId);
                var shape = (Shape)socket.Content;
                string context = $"{socket.NodeId} {socket.Kind} {(socket.Output ? "output" : "input")} at {zoom:P0}";
                require(socket.RenderSize == new Size(24, 20) && !socket.UseLayoutRounding && !socket.SnapsToDevicePixels,
                    context + " retains its full 24 × 20 target without glyph-decentering pixel rounding.");
                double diameter = socket.Kind == "control" ? 10 : 12;
                require(shape.ActualWidth == diameter && shape.ActualHeight == diameter
                    && Near(shape.StrokeThickness, 1.6)
                    && (socket.Kind == "audio" ? shape is Ellipse : socket.Kind == "control" ? shape is Rectangle : shape is Polygon),
                    context + " uses the correct square-bounded circle, square or diamond.");
                if (shape is Polygon diamond)
                {
                    var points = diamond.Points;
                    require(diamond.Stretch == Stretch.Fill && points.Count == 4
                        && Near(points[0] + (Vector)points[2], points[1] + (Vector)points[3])
                        && Near((points.Max(point => point.X) - points.Min(point => point.X)),
                            (points.Max(point => point.Y) - points.Min(point => point.Y))),
                        context + " has a symmetric diamond with equal horizontal and vertical bounds.");
                }
                Point local = socket.TranslatePoint(Center(socket.RenderSize), view);
                Rect geometry = shape.RenderedGeometry.Bounds;
                Point glyph = shape.TranslatePoint(new(geometry.X + geometry.Width / 2, geometry.Y + geometry.Height / 2), view);
                string label = socket.Kind == "audio" ? "Audio" : socket.Kind == "sidechain" ? "Trigger"
                    : socket.Output ? "Control" : "Level control";
                var row = Descendants<TextBlock>(view).Where(text => text.Text == label
                    && text.HorizontalAlignment == (socket.Output ? HorizontalAlignment.Right : HorizontalAlignment.Left))
                    .Select(text => VisualTreeHelper.GetParent(text)).OfType<Grid>().Single(grid => grid.Height == 28);
                double rowCenter = row.TranslatePoint(Center(row.RenderSize), view).Y;
                double edge = BorderCenter(view, socket.Output);
                require(Near(local, glyph) && Near(local.X, edge) && Near(local.Y, rowCenter)
                    && Near(local, originalCenters[(socket.NodeId, socket.Kind, socket.Output)]),
                    context + " centers the actual glyph and cable target on the node border without moving its label row.");
                Point cable = workspace.SocketPosition(socket);
                Point transformed = socket.TransformToAncestor(viewport).Transform(Center(socket.RenderSize));
                require(Near(cable, socket.TransformToAncestor(world).Transform(Center(socket.RenderSize)))
                    && Near(Screen(cable), transformed) && view.RenderSize == originalSizes[view.Id],
                    context + " keeps cable, world and viewport coordinates aligned without changing node dimensions. "
                    + AnchorDetails(socket, view, world, viewport, cable));
                foreach (Point point in new Point[] { new(.5, 10), new(23.5, 10), new(12, .5), new(12, 19.5) })
                {
                    var inputHit = socket.InputHitTest(point) as DependencyObject;
                    var geometryHit = DetachedHit(socket, point);
                    string detail = HitDetails(socket, point, inputHit, geometryHit);
                    require(AncestorSocket(geometryHit) == socket,
                        context + " accepts detached geometry hits in its transparent target beyond the visible glyph. " + detail);
                    if (socket.IsVisible)
                        require(AncestorSocket(inputHit) == socket,
                            context + " also resolves an enabled visible InputHitTest to the socket. " + detail);
                }
                require(socket.Focusable && socket.IsTabStop && AutomationProperties.GetName(socket).Length > 0,
                    context + " remains a named keyboard-reachable socket.");
                RequireState(socket, false, context + " is hollow while disconnected.");
            }

            foreach (var (source, target) in new[]
            {
                (Socket(microphone.Id, "audio", true), Socket(compressor.Id, "audio")),
                (Socket(microphone.Id, "audio", true), Socket(compressor.Id, "sidechain")),
                (Socket(control.Id, "control", true), Socket(output.Id, "control"))
            })
            {
                Point anchor = Screen(workspace.SocketPosition(target));
                require(workspace.FindSnapSocket(anchor + new Vector(-17.75, 0), source) == target
                    && workspace.FindSnapSocket(anchor + new Vector(-18.25, 0), source) is null,
                    $"The {target.Kind} target keeps its 18-screen-DIP snap radius at {zoom:P0}, independent of glyph size.");
                var shape = (Shape)target.Content;
                Size targetSize = target.RenderSize, glyphSize = shape.RenderSize;
                double stroke = shape.StrokeThickness; Brush fill = shape.Fill;
                workspace.BeginConnectionPreview(source, Screen(workspace.SocketPosition(source)), dragging: true);
                workspace.UpdateConnectionPointer(anchor); await layout();
                require(target.IsConnectionHighlighted && SameBrush(shape.Fill, fill)
                    && SameBrush(shape.Stroke, ConsoleTheme.Get("NodeSelectedBrush"))
                    && target.RenderSize == targetSize && shape.RenderSize == glyphSize && shape.StrokeThickness == stroke,
                    $"Snapping to {target.Kind} at {zoom:P0} highlights only the existing outline without resizing or filling the port.");
                workspace.HandleCaptureLoss(); await layout();
                RequireState(target, false, "Cancelling a socket preview restores the disconnected outline and fill.");
            }
            require(Audio() == audioBefore && mixer.EditHistory.Entries.Count == historyBefore,
                "Socket presentation, hit testing and cancelled previews do not edit routes, parameters or undo history.");
        }

        mixer.Edit(config => config.Graph.Zoom = 1); await layout();
        render?.Invoke(View(compressor.Id), "sockets-disconnected.png");
        require(workspace.ConnectNodes(microphone.Id, compressor.Id, "sidechain").Length == 0,
            "A detector-only connection is valid for the socket state fixture.");
        await layout();
        RequireState(Socket(microphone.Id, "audio", true), true, "A sidechain-only cable fills its source's audio output.");
        RequireState(Socket(compressor.Id, "sidechain"), true, "A sidechain cable fills only the detector input.");
        RequireState(Socket(compressor.Id, "audio"), false, "A detector cable leaves the main audio input hollow.");
        RequireState(Socket(compressor.Id, "audio", true), false, "An incoming cable does not mark an unrelated output connected.");
        require(workspace.ConnectNodes(control.Id, output.Id, "control").Length == 0
            && workspace.ConnectNodes(compressor.Id, output.Id, "audio").Length == 0
            && workspace.ConnectNodes(microphone.Id, compressor.Id, "audio").Length == 0,
            "Audio and control connections are added using the actual workspace actions.");
        await layout(); workspace.UpdateWires(); await layout();
        foreach (var socket in new[] { Socket(control.Id, "control", true), Socket(output.Id, "control"),
            Socket(compressor.Id, "audio"), Socket(compressor.Id, "audio", true), Socket(output.Id, "audio") })
            RequireState(socket, true, "Each connected socket fills in its own signal-kind colour.");
        RequireState(Socket(microphone.Id, "control"), false, "An audio output connection leaves that node's control input hollow.");
        RequireState(Socket(denoiser.Id, "audio"), false, "Connections elsewhere do not fill an unrelated processor input.");
        foreach (var socket in Sockets().Where(socket => socket.IsConnected))
        {
            var shape = (Shape)socket.Content;
            Size targetSize = socket.RenderSize, glyphSize = shape.RenderSize;
            Brush fill = shape.Fill;
            socket.SetConnectionHighlight(true); await layout();
            require(SameBrush(shape.Fill, fill) && SameBrush(shape.Stroke, ConsoleTheme.Get("NodeSelectedBrush"))
                && socket.RenderSize == targetSize && shape.RenderSize == glyphSize && Near(shape.StrokeThickness, 1.6),
                "Highlighting a connected socket preserves its filled state and all target/glyph dimensions.");
            socket.SetConnectionHighlight(false);
            RequireState(socket, true, "Ending a connected-socket highlight restores its ordinary dark outline.");
        }
        foreach (var edge in mixer.GraphConfig.Edges)
        {
            Point from = workspace.SocketPosition(Socket(edge.From, edge.Kind == "sidechain" ? "audio" : edge.Kind, true));
            Point to = workspace.SocketPosition(Socket(edge.To, edge.Kind));
            require(Descendants<System.Windows.Shapes.Path>(world).Any(path => CableHasEndpoints(path, from, to)),
                $"The actual rendered {edge.Kind} cable terminates at the visual centers of both sockets.");
        }
        Visual dpiRoot = workspace;
        while (VisualTreeHelper.GetParent(dpiRoot) is Visual parent) dpiRoot = parent;
        DpiScale originalDpi = VisualTreeHelper.GetDpi(dpiRoot);
        double originalZoom = mixer.GraphConfig.Zoom;
        string beforeDpi = Audio(); int beforeDpiHistory = mixer.EditHistory.Entries.Count;
        bool exercisedRoundedBorder = false;
        try
        {
            foreach (double dpi in new[] { 120d, 144, 192 })
            {
                VisualTreeHelper.SetRootDpi(dpiRoot, new DpiScale(dpi / 96, dpi / 96));
                InvalidateTree(dpiRoot); await layout();
                foreach (double zoom in new[] { .3, .75, 1, 1.5, 2 })
                {
                    mixer.Edit(config => config.Graph.Zoom = zoom); await layout();
                    workspace.UpdateWires(); await layout();
                    var moving = View(control.Id);
                    var movingSocket = Socket(control.Id, "control", true);
                    Point savedPosition = new(Canvas.GetLeft(moving), Canvas.GetTop(moving));
                    Point previousAnchor = workspace.SocketPosition(movingSocket);
                    Point previousVisualOrigin = moving.TranslatePoint(new Point(), world);
                    try
                    {
                        Canvas.SetLeft(moving, savedPosition.X + 13.37);
                        Canvas.SetTop(moving, savedPosition.Y - 7.21);
                        Point predicted = workspace.SocketPosition(movingSocket);
                        require(!Near(predicted, previousAnchor) && Near(moving.TranslatePoint(new Point(), world), previousVisualOrigin),
                            $"At {dpi} DPI / {zoom:P0}, a fractional move updates the cable anchor before WPF rearranges the node.");
                        await layout();
                        Point actual = movingSocket.TransformToAncestor(world).Transform(Center(movingSocket.RenderSize));
                        require(Near(predicted, actual),
                            $"At {dpi} DPI / {zoom:P0}, the immediate move anchor matches the later arranged socket exactly. "
                            + $"predicted={predicted}; actual={actual}; " + AnchorDetails(movingSocket, moving, world, viewport, predicted));
                    }
                    finally
                    {
                        Canvas.SetLeft(moving, savedPosition.X); Canvas.SetTop(moving, savedPosition.Y);
                        await layout(); workspace.UpdateWires(); await layout();
                    }
                    foreach (var socket in Sockets())
                    {
                        var view = View(socket.NodeId);
                        var shape = (Shape)socket.Content;
                        string context = $"{socket.NodeId} {socket.Kind} {(socket.Output ? "output" : "input")} at {dpi} DPI / {zoom:P0}";
                        require(Near(VisualTreeHelper.GetDpi(view).PixelsPerInchX, dpi)
                            && Near(VisualTreeHelper.GetDpi(socket).PixelsPerInchY, dpi),
                            context + " uses the actual requested visual-tree DPI, not an enlarged bitmap.");
                        Point local = socket.TranslatePoint(Center(socket.RenderSize), view);
                        Rect geometry = shape.RenderedGeometry.Bounds;
                        Point glyph = shape.TranslatePoint(new(geometry.X + geometry.Width / 2, geometry.Y + geometry.Height / 2), view);
                        double renderedEdge = BorderCenter(view, socket.Output);
                        double rawEdge = socket.Output ? view.ActualWidth - view.BorderThickness.Right / 2 : view.BorderThickness.Left / 2;
                        exercisedRoundedBorder |= !Near(renderedEdge, rawEdge);
                        require(Near(local.X, renderedEdge) && Near(local, glyph)
                            && Near(shape.ActualWidth, shape.ActualHeight) && socket.RenderSize == new Size(24, 20),
                            context + " centers its symmetric glyph and full target on the measured border after DPI layout rounding.");
                        string label = socket.Kind == "audio" ? "Audio" : socket.Kind == "sidechain" ? "Trigger"
                            : socket.Output ? "Control" : "Level control";
                        var row = Descendants<TextBlock>(view).Where(text => text.Text == label
                            && text.HorizontalAlignment == (socket.Output ? HorizontalAlignment.Right : HorizontalAlignment.Left))
                            .Select(text => VisualTreeHelper.GetParent(text)).OfType<Grid>().Single(grid => grid.Height == 28);
                        require(Near(local.Y, row.TranslatePoint(Center(row.RenderSize), view).Y),
                            context + " remains centered on its existing label row after the rounded border inset changes.");
                        Point cable = workspace.SocketPosition(socket);
                        require(Near(cable, socket.TransformToAncestor(world).Transform(Center(socket.RenderSize)))
                            && Near(Screen(cable), shape.TransformToAncestor(viewport).Transform(
                                new Point(geometry.X + geometry.Width / 2, geometry.Y + geometry.Height / 2))),
                            context + " keeps the cable anchor and rendered glyph center identical in world and viewport space. "
                            + AnchorDetails(socket, view, world, viewport, cable));
                    }
                    foreach (var edge in mixer.GraphConfig.Edges)
                    {
                        Point from = workspace.SocketPosition(Socket(edge.From, edge.Kind == "sidechain" ? "audio" : edge.Kind, true));
                        Point to = workspace.SocketPosition(Socket(edge.To, edge.Kind));
                        require(Descendants<System.Windows.Shapes.Path>(world).Any(path => CableHasEndpoints(path, from, to)),
                            $"The {edge.Kind} cable follows both DPI-adjusted socket anchors at {dpi} DPI / {zoom:P0}.");
                    }
                }
            }
            require(exercisedRoundedBorder,
                "The display-scaling fixtures exercise a rendered border inset that differs from raw BorderThickness.");
            require(Audio() == beforeDpi && mixer.EditHistory.Entries.Count == beforeDpiHistory,
                "Actual DPI changes and canvas zooms preserve every audio setting, route and undo entry.");
        }
        finally
        {
            VisualTreeHelper.SetRootDpi(dpiRoot, originalDpi);
            InvalidateTree(dpiRoot);
            mixer.Edit(config => config.Graph.Zoom = originalZoom);
            await layout(); workspace.UpdateWires(); await layout();
        }
        render?.Invoke(View(compressor.Id), "sockets-connected.png");
        mixer.Edit(config => config.Graph.Edges.RemoveAll(edge => edge.Kind == "sidechain")); await layout();
        RequireState(Socket(compressor.Id, "sidechain"), false, "Removing a detector cable restores its input's hollow state.");
        RequireState(Socket(microphone.Id, "audio", true), true, "An audio output stays filled while another cable still uses it.");
        mixer.Edit(config => config.Graph.Edges.RemoveAll(edge => edge.From == microphone.Id)); await layout();
        RequireState(Socket(microphone.Id, "audio", true), false, "Removing the final outgoing cable restores the source's hollow state.");
        require(mixer.Undo(), "Undo restores the last removed cable for the connection-state fixture."); await layout();
        RequireState(Socket(microphone.Id, "audio", true), true, "Undo immediately restores the source's connected fill.");
        RequireState(Socket(compressor.Id, "audio"), true, "Undo immediately restores the destination's connected fill.");
        require(PresentationSource.FromVisual(workspace) is null, "Socket checks finish without creating a native presentation source.");
    }

    private static GraphNode Node(string kind, string id, double x, double y)
    { var node = GraphDefaults.NewNode(kind, x, y); node.Id = id; return node; }
    private static Point Center(Size size) => new(size.Width / 2, size.Height / 2);
    private static double BorderCenter(GraphNodeView node, bool output)
    {
        // Border.Child reflects WPF's measured inner edge, including physical-pixel
        // rounding. The logical BorderThickness is not that edge at every DPI.
        var child = node.Child;
        Point inner = child.TranslatePoint(new(output ? child.RenderSize.Width : 0, 0), node);
        return output ? (node.ActualWidth + inner.X) / 2 : inner.X / 2;
    }
    private static void InvalidateTree(DependencyObject root)
    {
        if (root is UIElement element) { element.InvalidateMeasure(); element.InvalidateArrange(); element.InvalidateVisual(); }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) InvalidateTree(VisualTreeHelper.GetChild(root, i));
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) < .01;
    private static bool Near(Point a, Point b) => (a - b).Length < .01;
    private static bool SameBrush(Brush? left, Brush? right) => ReferenceEquals(left, right)
        || left is SolidColorBrush a && right is SolidColorBrush b && a.Color == b.Color && a.Opacity == b.Opacity;
    private static string AnchorDetails(GraphWorkspace.Socket socket, GraphNodeView node, Visual world, Visual viewport, Point cable)
    {
        var shape = (Shape)socket.Content;
        Rect geometry = shape.RenderedGeometry.Bounds;
        Point glyphCenter = new(geometry.X + geometry.Width / 2, geometry.Y + geometry.Height / 2);
        return $"node requested=({Canvas.GetLeft(node)},{Canvas.GetTop(node)}), visual={node.TransformToAncestor(world).Transform(new Point())}; "
            + $"local socket={socket.TranslatePoint(Center(socket.RenderSize), node)}, glyph={shape.TranslatePoint(glyphCenter, node)}; "
            + $"world cable={cable}, socket={socket.TransformToAncestor(world).Transform(Center(socket.RenderSize))}, "
            + $"glyph={shape.TransformToAncestor(world).Transform(glyphCenter)}; "
            + $"viewport cable={world.TransformToAncestor(viewport).Transform(cable)}, "
            + $"glyph={shape.TransformToAncestor(viewport).Transform(glyphCenter)}.";
    }
    private static DependencyObject? DetachedHit(Visual root, Point point)
    {
        // The detached renderer keeps a never-shown Window as a logical resource
        // owner. Effective IsVisible may therefore be false, which makes ordinary
        // InputHitTest reject the entire tree. As in CaptionHitTestVerification,
        // test real drawn geometry with local visibility/hit eligibility instead.
        // This verifies the transparent target, not native mouse/input dispatch.
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(root,
            visual => visual is UIElement element && (element.Visibility != Visibility.Visible || !element.IsHitTestVisible)
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        while (hit is not null && hit is not UIElement { IsEnabled: true }) hit = VisualTreeHelper.GetParent(hit);
        return hit;
    }
    private static string HitDetails(GraphWorkspace.Socket socket, Point point, DependencyObject? inputHit, DependencyObject? geometryHit)
    {
        var template = VisualTreeHelper.GetChildrenCount(socket) > 0 ? VisualTreeHelper.GetChild(socket, 0) as FrameworkElement : null;
        return $"Point={point}; inputHit={inputHit?.GetType().Name ?? "null"}; geometryHit={geometryHit?.GetType().Name ?? "null"}; "
            + $"socket enabled={socket.IsEnabled}, visible={socket.IsVisible}/{socket.Visibility}, hitTest={socket.IsHitTestVisible}, size={socket.RenderSize}; "
            + $"template={template?.GetType().Name ?? "null"}, size={template?.RenderSize}, enabled={template?.IsEnabled}, "
            + $"visible={template?.IsVisible}/{template?.Visibility}, hitTest={template?.IsHitTestVisible}.";
    }
    private static GraphWorkspace.Socket? AncestorSocket(DependencyObject? item)
    {
        while (item is not null)
        {
            if (item is GraphWorkspace.Socket socket) return socket;
            item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
        }
        return null;
    }
    private static bool CableHasEndpoints(System.Windows.Shapes.Path path, Point from, Point to)
    {
        if (path.Data is not StreamGeometry) return false;
        var figures = PathGeometry.CreateFromGeometry(path.Data).Figures;
        if (figures.Count != 1 || !Near(figures[0].StartPoint, from)) return false;
        return figures[0].Segments.LastOrDefault() switch
        {
            BezierSegment segment => Near(segment.Point3, to),
            PolyBezierSegment segment when segment.Points.Count > 0 => Near(segment.Points[^1], to),
            _ => false
        };
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
