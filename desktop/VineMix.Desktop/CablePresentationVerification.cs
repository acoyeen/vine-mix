using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

internal static class CablePresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<string> render)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Cable colour and marker checks use an inert detached workspace.");
        var fixture = UiPreview.CreateSnapshot([]);
        var echo = GraphDefaults.NewNode("echo", 40, 40);
        var gate = GraphDefaults.NewNode("gate", 840, 40);
        var reverb = GraphDefaults.NewNode("reverb", 40, 650);
        var mix = GraphDefaults.NewNode("mix", 840, 650);
        var split = GraphDefaults.NewNode("stereoSplit", 1500, 40);
        fixture.Configuration.Graph = new() { Nodes = [echo, gate, reverb, mix, split], Zoom = .55, PanX = 16, PanY = 16,
            Edges = [new() { Id = "trigger-gradient", From = echo.Id, To = gate.Id, Kind = "sidechain" },
                new() { Id = "echo-return", From = echo.Id, FromPort = "wet", To = reverb.Id },
                new() { Id = "reverb-return", From = reverb.Id, FromPort = "wet", To = mix.Id },
                new() { Id = "solid-audio", From = echo.Id, To = mix.Id }] };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Channels = 2, Path = new() { State = "Running" } }).ToList();
        mixer.IsLayoutLocked = false; mixer.ApplyPreviewSnapshot(fixture); workspace.SetMessage(""); await layout(); workspace.UpdateWires(); await layout();
        var views = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id);
        GraphWorkspace.Socket Socket(string id, bool output, string port = "audio", string kind = "audio") => views[id].FindSocket(kind, output, port)!;
        Color Colour(string key) => ((SolidColorBrush)ConsoleTheme.Get(key)).Color;
        foreach (var source in new[] { echo, reverb })
        {
            var socket = Socket(source.Id, true, "wet"); var glyph = Descendants<Shape>(socket).Single();
            require(glyph is Ellipse && ((SolidColorBrush)glyph.Fill).Color == Colour("NodeEffectReturnBrush")
                && Socket(source.Id, true).ColourBrushKey == "NodeAudioBrush",
                "Connected " + source.Kind + " return is light blue and circular while its main Audio socket remains green. "
                    + $"key={socket.ColourBrushKey}; connected={socket.IsConnected}; fill={glyph.Fill}; expected={Colour("NodeEffectReturnBrush")}");
        }
        require(Socket(split.Id, true, "right").ColourBrushKey == "NodeAudioBrush",
            "Stereo Split Right remains ordinary audio rather than being mistaken for an effect return.");
        foreach (var edge in mixer.GraphConfig.Edges)
        {
            var paths = workspace.ConnectionPaths(edge.Id)!.Value;
            var marker = workspace.ConnectionMarker(edge.Id)!;
            var from = workspace.SocketPosition(Socket(edge.From, true, edge.FromPort));
            var to = workspace.SocketPosition(Socket(edge.To, false, edge.ToPort, edge.Kind));
            var sourceColour = Colour(Socket(edge.From, true, edge.FromPort).ColourBrushKey);
            var targetColour = Colour(Socket(edge.To, false, edge.ToPort, edge.Kind).ColourBrushKey);
            require(sourceColour == targetColour ? paths.Visible.Stroke is SolidColorBrush solid && solid.Color == sourceColour
                : paths.Visible.Stroke is LinearGradientBrush gradient && gradient.MappingMode == BrushMappingMode.Absolute
                    && gradient.StartPoint == from && gradient.EndPoint == to && gradient.GradientStops[0].Color == sourceColour
                    && gradient.GradientStops[^1].Color == targetColour,
                edge.Id + " follows its actual source and destination socket colours, with equal endpoints remaining solid.");
            var circle = (EllipseGeometry)marker.Data; var middle = circle.Center;
            PathGeometry.CreateFromGeometry(paths.Visible.Data).GetPointAtFractionLength(.5, out var halfLength, out _);
            require((middle - halfLength).Length < .05 && marker.Visibility == Visibility.Visible
                && Math.Abs(circle.RadiusX * 2 - 7.5) < .001 && !marker.IsHitTestVisible && !marker.Focusable
                && ReferenceEquals(VisualTreeHelper.GetParent(marker), VisualTreeHelper.GetParent(paths.Visible))
                && workspace.ConnectionHitWidth(edge.Id) == 16,
                edge.Id + " has a passive 7.5-canvas-DIP marker at half the true curve length, in the original cable layer with unchanged hit width.");
            var painted = ((SolidColorBrush)marker.Fill).Color;
            require(Math.Abs(painted.R - (sourceColour.R + targetColour.R) / 2d) <= .5
                && Math.Abs(painted.G - (sourceColour.G + targetColour.G) / 2d) <= .5
                && Math.Abs(painted.B - (sourceColour.B + targetColour.B) / 2d) <= .5 && marker.Opacity == paths.Visible.Opacity,
                edge.Id + " marker matches the local cable colour and opacity.");
        }
        render("socket-colour-cables.png");
        var cached = mixer.GraphConfig.Edges.ToDictionary(edge => edge.Id, edge => workspace.ConnectionPaths(edge.Id)!.Value.Visible.Stroke);
        long builds = workspace.WireGeometryBuilds;
        mixer.RefreshPreviewTelemetry(); await layout();
        require(cached.All(pair => ReferenceEquals(pair.Value, workspace.ConnectionPaths(pair.Key)!.Value.Visible.Stroke))
            && builds == workspace.WireGeometryBuilds, "Unchanged telemetry reuses cable brushes and geometry.");

        string beforePreview = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        int beforePreviewHistory = mixer.EditHistory.Entries.Count;
        var input = Socket(reverb.Id, false); var anchor = workspace.SocketPosition(input);
        var screen = new Point(anchor.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX, anchor.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
        require(workspace.BeginInputLinkDrag(input, screen, "echo-return"), "The existing Echo cable can still begin an input-end rewire.");
        await layout();
        var preview = Descendants<System.Windows.Shapes.Path>(workspace).Single(path => path.Data is not null
            && path.StrokeDashArray is { Count: 2 } dash && dash[0] == 3 && dash[1] == 3);
        require(preview.Stroke is LinearGradientBrush previewGradient
            && previewGradient.GradientStops[0].Color == Colour("NodeEffectReturnBrush")
            && previewGradient.GradientStops[^1].Color == Colour("NodeAudioBrush")
            && workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Hidden,
            "Rewire preview retains the endpoint gradient and dashed cue while hiding the original cable's established marker.");
        workspace.CancelActiveGesture(); workspace.SelectNode(echo.Id); workspace.SetMessage(""); await layout();
        require(workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Visible
            && beforePreview == JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) && beforePreviewHistory == mixer.EditHistory.Entries.Count,
            "Cancelling the preview restores its marker without modifying connection identity, settings or undo history.");

        var selected = workspace.ConnectionPaths("echo-return")!.Value;
        workspace.SelectEdge("echo-return"); await layout();
        require(selected.Visible.Stroke is SolidColorBrush highlight && highlight.Color == Colour("NodeSelectedBrush")
            && ((SolidColorBrush)workspace.ConnectionMarker("echo-return")!.Fill).Color == highlight.Color
            && selected.Visible.Opacity == 1, "Cable selection retains its clear highlight, including the marker.");
        workspace.SelectNode(echo.Id);
        mixer.Edit(configuration => configuration.Graph.Edges.Single(edge => edge.Id == "echo-return").Muted = true); await layout();
        require(selected.Visible.Stroke is SolidColorBrush muted && muted.Color == Colour("ConsoleMutedBrush")
            && selected.Visible.StrokeDashArray.Count > 0 && selected.Visible.Opacity == .4
            && workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Hidden,
            "Disabled cables retain their grey dashed treatment and hide their midpoint marker.");
        render("socket-colour-cables-disabled.png");
        workspace.SelectEdge("echo-return"); await layout();
        require(selected.Visible.Stroke is SolidColorBrush disabledHighlight && disabledHighlight.Color == Colour("NodeSelectedBrush")
            && selected.Visible.StrokeDashArray.Count > 0 && selected.Hit.Visibility == Visibility.Visible
            && workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Hidden,
            "A disabled connection remains selectable and highlighted without restoring its marker.");
        foreach (double zoom in new[] { .1, .55, 2d, .55 })
        {
            mixer.SetCanvasView(16, 16, zoom); mixer.RefreshPreviewTelemetry(); await layout();
            require(workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Hidden,
                $"A selected disabled connection keeps its marker hidden at {zoom:P0} zoom.");
        }
        bool? mutedTheme = ConsoleTheme.PreviewHighContrast;
        try
        {
            ConsoleTheme.SetPreviewHighContrast(true); await layout();
            require(workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Hidden
                && selected.Visible.StrokeDashArray.Count > 0 && selected.Hit.Visibility == Visibility.Visible,
                "High contrast preserves the disabled cable and its hit target without restoring its marker.");
        }
        finally { ConsoleTheme.SetPreviewHighContrast(mutedTheme); await layout(); }
        workspace.SelectNode(echo.Id);
        var disabledDraft = Descendants<RotaryValueControl>(views[echo.Id]).First();
        disabledDraft.BeginTextEntry(); Descendants<TextBox>(disabledDraft).Single().Text = "-";
        mixer.SetCanvasView(16, 16, .1); mixer.RefreshPreviewTelemetry(); await layout();
        require(views[echo.Id].DetailLevel == NodeDetailLevel.Full
            && workspace.ConnectionMarker("trigger-gradient")!.Visibility == Visibility.Visible
            && workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Hidden,
            "Retained endpoint controls show enabled markers but never restore a disabled connection's marker.");
        disabledDraft.CancelPendingEntry(); mixer.SetCanvasView(16, 16, .55); mixer.RefreshPreviewTelemetry(); await layout();
        mixer.Edit(configuration => configuration.Graph.Edges.Single(edge => edge.Id == "echo-return").Muted = false); await layout();
        require(workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Visible
            && selected.Visible.StrokeDashArray is null,
            "Turning the connection back on restores its marker and solid cable.");
        require(mixer.Undo(), "The isolated connection enable can be undone."); await layout();
        require(workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Hidden
            && selected.Visible.StrokeDashArray is { Count: > 0 },
            "Undoing enable restores the disabled dashed cable without a marker.");
        require(mixer.Undo(), "The isolated original cable disable can be undone."); await layout();
        require(workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Visible,
            "Undoing disable restores the marker on the enabled connection.");
        string sound = JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json);
        int history = mixer.EditHistory.Entries.Count;
        foreach (var step in new[] { (.3, true), (.285, true), (.279, false), (.285, false), (.3, true), (.1, false), (.55, true), (1d, true), (2d, true) })
        {
            mixer.SetCanvasView(16, 16, step.Item1); mixer.RefreshPreviewTelemetry(); await layout();
            require(mixer.GraphConfig.Edges.All(edge => (workspace.ConnectionMarker(edge.Id)!.Visibility == Visibility.Visible) == step.Item2)
                && views.Values.All(view => (view.DetailLevel == NodeDetailLevel.Full) == step.Item2),
                $"At {step.Item1:P1}, markers follow the same full-detail threshold and hysteresis as node content.");
            require(mixer.GraphConfig.Edges.All(edge => MarkerOnCurve(workspace, edge.Id)),
                $"At {step.Item1:P1}, actual arranged marker geometry remains centred on the cable after hidden/full zoom transitions.");
            require(mixer.GraphConfig.Edges.All(edge =>
            {
                var marker = workspace.ConnectionMarker(edge.Id)!;
                Rect rendered = marker.TransformToAncestor(workspace).TransformBounds(marker.RenderedGeometry.Bounds);
                return Math.Abs(rendered.Width - 7.5 * step.Item1) < .001 && Math.Abs(rendered.Height - 7.5 * step.Item1) < .001;
            }), $"At {step.Item1:P1}, the actual rendered marker diameter is {7.5 * step.Item1:0.###} screen DIPs, preserving its size relative to nodes.");
            if (step.Item1 == .279) render("socket-colour-cables-quiet-blocks.png");
        }
        require(sound == JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json)
            && history == mixer.EditHistory.Entries.Count, "Zoom/marker presentation never changes routes, audio settings or undo history.");

        var dial = Descendants<RotaryValueControl>(views[echo.Id]).First();
        dial.BeginTextEntry(); Descendants<TextBox>(dial).Single().Text = "-";
        mixer.SetCanvasView(16, 16, .1); mixer.RefreshPreviewTelemetry(); await layout();
        require(views[echo.Id].DetailLevel == NodeDetailLevel.Full
            && workspace.ConnectionMarker("echo-return")!.Visibility == Visibility.Visible
            && workspace.ConnectionMarker("trigger-gradient")!.Visibility == Visibility.Visible
            && workspace.ConnectionMarker("reverb-return")!.Visibility == Visibility.Hidden,
            "A retained numeric draft keeps its endpoint's markers with its full details, without restoring unrelated low-detail markers.");
        dial.CancelPendingEntry(); mixer.RefreshPreviewTelemetry(); await layout();
        require(mixer.GraphConfig.Edges.All(edge => workspace.ConnectionMarker(edge.Id)!.Visibility == Visibility.Hidden),
            "Finishing the draft hides retained markers when its endpoint returns to simplified detail.");
        mixer.SetCanvasView(16, 16, .55); await layout();

        bool? priorTheme = ConsoleTheme.PreviewHighContrast;
        try
        {
            ConsoleTheme.SetPreviewHighContrast(true); await layout();
            require(Colour("NodeEffectReturnBrush") == ((SolidColorBrush)SystemColors.WindowTextBrush).Color
                && mixer.GraphConfig.Edges.All(edge => workspace.ConnectionPaths(edge.Id)!.Value.Visible.Stroke is SolidColorBrush
                    && ((SolidColorBrush)workspace.ConnectionMarker(edge.Id)!.Fill).Color == Colour("NodeAudioBrush")),
                "High contrast uses the system foreground for sockets, cables and markers instead of decorative endpoint colours.");
            require(mixer.GraphConfig.Edges.All(edge => MarkerOnCurve(workspace, edge.Id)),
                "Theme changes after zooming keep the rendered circles on their curves, without stale arranged-size offsets.");
            render("socket-colour-cables-high-contrast.png");
        }
        finally { ConsoleTheme.SetPreviewHighContrast(priorTheme); await layout(); }

        // Cover the marker with a normal node: the intended cue must remain
        // subject to ordinary node occlusion rather than floating above it.
        var covered = workspace.ConnectionMarker("trigger-gradient")!;
        var centre = ((EllipseGeometry)covered.Data).Center;
        mixer.Edit(configuration => { var node = configuration.Graph.Nodes.Single(node => node.Id == split.Id); node.X = centre.X - 150; node.Y = centre.Y - 120; });
        await layout();
        var world = (Canvas)views[split.Id].Parent;
        var hit = VisualTreeHelper.HitTest(world, centre)?.VisualHit;
        require(Ancestors(hit).Contains(views[split.Id]), "A node covering the midpoint remains above the passive marker in the actual visual hit stack. "
            + $"centre={centre}; node={Canvas.GetLeft(views[split.Id])},{Canvas.GetTop(views[split.Id])} size={views[split.Id].RenderSize}; hit={hit?.GetType().Name}");
        mixer.SetCanvasView(16, 16, .55); workspace.SetMessage(""); await layout(); render("socket-colour-cables-occlusion.png");

        VerifyGradientPixels(require);
        VerifyAdaptiveGeometry(require);
        await VerifyShortRouteAsync(workspace, mixer, layout, require, render);
    }

    private static bool MarkerOnCurve(GraphWorkspace workspace, string id)
    {
        var marker = workspace.ConnectionMarker(id)!; var curve = workspace.ConnectionPaths(id)!.Value.Visible;
        PathGeometry.CreateFromGeometry(curve.Data).GetPointAtFractionLength(.5, out var expected, out _);
        Rect ink = marker.RenderedGeometry.Bounds;
        Point actual = marker.TransformToAncestor(VisualTreeHelper.GetParent(curve) as Visual)
            .Transform(new Point(ink.Left + ink.Width / 2, ink.Top + ink.Height / 2));
        return (actual - expected).Length < .05;
    }

    private static void VerifyAdaptiveGeometry(Action<bool, string> require)
    {
        foreach (var end in new Point[] { new(8, 0), new(8, 4), new(2, -.5), new(), new(.00000001, -.00000001),
            new(0, 600), new(-300, 200), new(-400, 0), new(2000, -900) })
        {
            var from = new Point(-50, -90); var to = from + (Vector)end;
            var controls = GraphWorkspace.CableControls(from, to);
            var points = GraphWorkspace.CablePoints(from, to);
            double distance = (to - from).Length;
            require(Math.Abs((controls.First - from).Length - distance * .25) < .0000001
                && Math.Abs((to - controls.Second).Length - distance * .25) < .0000001
                && points.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
                && points[0] == from && points[^1] == to,
                $"Adaptive handles remain finite, proportional and attached for socket delta {end}, including zero, tiny, vertical, reverse and long routes.");
            require(points.Select((point, index) => (point + (Vector)points[points.Count - 1 - index]
                - (from + (Vector)to)).Length).All(error => error < .0000001),
                "Equal/opposite adaptive handles retain the central symmetry required by midpoint markers and colour interpolation.");
            if (end.X > 0 && Math.Abs(end.Y) <= end.X)
                require(points.Zip(points.Skip(1)).All(pair => pair.First.X <= pair.Second.X)
                    && points.All(point => point.X >= from.X && point.X <= to.X),
                    "Short forward aligned/diagonal links have no backward segment or overshoot beyond either socket.");
            if (distance > .01)
            {
                var geometry = GraphWorkspace.Cable(from, to);
                var middle = GraphWorkspace.CableMidpoint(from, to);
                PathGeometry.CreateFromGeometry(geometry).GetPointAtFractionLength(.5, out var actualMiddle, out _);
                require((actualMiddle - middle).Length < .05 && geometry.StrokeContains(new Pen(Brushes.Black, 2), middle)
                    && GraphConnectionEditing.PolylineIntersectsStroke(points, middle - new Vector(0, 4), middle + new Vector(0, 4), .1),
                    "Rendered geometry, sampled cutting route and half-length marker agree for adaptive routes.");
            }
        }
        var previous = GraphWorkspace.CableControls(new(), new(-.01, 20));
        bool continuous = true;
        for (int i = -9; i <= 10; i++)
        {
            var current = GraphWorkspace.CableControls(new(), new(i * .001, 20));
            continuous &= (current.First - previous.First).Length <= .00125
                && (current.Second - previous.Second).Length <= .00125;
            previous = current;
        }
        require(continuous, "Moving a destination through vertical alignment changes the handles continuously, without a threshold jump.");
    }

    private static async Task VerifyShortRouteAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<string> render)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        var echo = GraphDefaults.NewNode("echo", 160, 160); var target = GraphDefaults.NewNode("mix", 700, 40);
        fixture.Configuration.Graph = new() { Nodes = [echo, target], Zoom = 1, PanX = 16, PanY = 16,
            Edges = [new() { Id = "short-return", From = echo.Id, FromPort = "wet", To = target.Id }] };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Channels = 2, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); workspace.SetMessage(""); await layout();
        var views = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id);
        var output = views[echo.Id].FindSocket("audio", true, "wet")!; var input = views[target.Id].FindSocket("audio", false)!;
        var from = workspace.SocketPosition(output);
        Point localInput = input.TranslatePoint(new Point(input.ActualWidth / 2, input.ActualHeight / 2), views[target.Id]);
        foreach (var delta in new[] { new Vector(8, 0), new Vector(8, 4), new Vector(48, -12), new Vector(0, 600), new Vector(-300, 600) })
        {
            mixer.Edit(configuration => { var node = configuration.Graph.Nodes.Single(node => node.Id == target.Id);
                node.X = from.X + delta.X - localInput.X; node.Y = from.Y + delta.Y - localInput.Y; });
            await layout(); workspace.UpdateWires(); await layout();
            var to = workspace.SocketPosition(input); var paths = workspace.ConnectionPaths("short-return")!.Value;
            var world = (Visual)VisualTreeHelper.GetParent(paths.Visible);
            var toHit = paths.Hit.TransformToAncestor(world).Inverse!;
            var points = GraphWorkspace.CablePoints(from, to); var middle = GraphWorkspace.CableMidpoint(from, to);
            require(paths.Hit.RenderedGeometry.StrokeContains(new Pen(paths.Hit.Stroke, paths.Hit.StrokeThickness), toHit.Transform(middle))
                && MarkerOnCurve(workspace, "short-return"),
                $"The real cable's hit target and midpoint stay attached for socket delta {delta}.");
            if (delta == new Vector(8, 4))
            {
                // This was t=.2 on the former fixed-70-DIP curve. It sat far
                // outside the eight-DIP gap and must no longer be a ghost target.
                Point oldFold = from + new Vector(20.992, .416);
                require(!paths.Hit.RenderedGeometry.StrokeContains(new Pen(paths.Hit.Stroke, paths.Hit.StrokeThickness), toHit.Transform(oldFold))
                    && !GraphConnectionEditing.PolylineIntersectsStroke(points, oldFold - new Vector(0, 3), oldFold + new Vector(0, 3), .1),
                    "Regression: the removed short-cable fold is neither clickable nor cuttable after the visible curve is corrected.");
                var screen = new Point(to.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX, to.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
                require(workspace.BeginInputLinkDrag(input, screen, "short-return"), "The close Echo return supports input-end rewiring.");
                await layout();
                var preview = Descendants<System.Windows.Shapes.Path>(workspace).Single(path => path.Data is not null
                    && path.StrokeDashArray is { Count: 2 } dash && dash[0] == 3 && dash[1] == 3);
                require(preview.Data.ToString() == paths.Visible.Data.ToString(), "Rewire preview uses exactly the same adaptive curve as the established short cable.");
                workspace.CancelActiveGesture(); workspace.SelectNode(""); workspace.SetMessage(""); await layout();
            }
            mixer.SetCanvasView(16, 16, delta.Y >= 360 ? .65 : 1); mixer.RefreshPreviewTelemetry(); await layout();
            render(delta.Y == 0 ? "cable-short-aligned.png" : delta.Y == 4 ? "cable-short-diagonal.png"
                : delta.X == 48 ? "cable-close-flow.png" : delta.X == 0 ? "cable-tall-vertical.png" : "cable-reversed.png");
            mixer.SetCanvasView(16, 16, 1); mixer.RefreshPreviewTelemetry(); await layout();
        }
    }

    private static void VerifyGradientPixels(Action<bool, string> require)
    {
        Color source = Color.FromRgb(142, 200, 232), destination = Color.FromRgb(120, 190, 152);
        foreach (var ends in new[] { (new Point(10, 20), new Point(310, 220)), (new Point(310, 20), new Point(10, 220)),
            (new Point(150, 20), new Point(150, 320)), (new Point(-310, -220), new Point(-10, -20)) })
        {
            var from = ends.Item1; var to = ends.Item2;
            double reach = (to - from).Length * .25;
            var curve = new PathGeometry([new PathFigure(from, [new BezierSegment(new(from.X + reach, from.Y), new(to.X - reach, to.Y), to, true)], false)]);
            var bounds = curve.Bounds; bounds.Inflate(12, 12);
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-bounds.Left, -bounds.Top));
                dc.DrawGeometry(null, new Pen(GraphWorkspace.CableBrush(from, to, source, destination), 8), curve); dc.Pop();
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing); bool correct = true;
            foreach (double fraction in new[] { .03, .5, .97 })
            {
                curve.GetPointAtFractionLength(fraction, out var point, out _);
                int x = (int)Math.Round(point.X - bounds.Left), y = (int)Math.Round(point.Y - bounds.Top);
                byte[] pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                // Evaluate the brush at this pixel centre, independently of its
                // source/destination direction and the Bezier's curvature.
                var at = new Point(x + bounds.Left + .5, y + bounds.Top + .5); var axis = to - from;
                double amount = Math.Clamp(Vector.Multiply(at - from, axis) / axis.LengthSquared, 0, 1);
                correct &= pixel[3] == 255 && Math.Abs(pixel[2] - (source.R + (destination.R - source.R) * amount)) <= 2
                    && Math.Abs(pixel[1] - (source.G + (destination.G - source.G) * amount)) <= 2
                    && Math.Abs(pixel[0] - (source.B + (destination.B - source.B) * amount)) <= 2;
            }
            require(correct, $"Rendered gradient pixels keep source-to-destination colours for curve {from} to {to}.");
        }
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject? item)
    {
        while (item is not null) { yield return item; item = VisualTreeHelper.GetParent(item); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
