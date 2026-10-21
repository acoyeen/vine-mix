using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VineMix.Desktop;

/// <summary>Real detached cable layout bounds; no tooltip popup, input or native window.</summary>
internal static class CableHoverBoundsVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Cable hover bounds use the detached preview without an engine, popup or native window.");
        mixer.ApplyPreviewSnapshot(Fixture()); await layout();
        Visual dpiRoot = workspace;
        while (VisualTreeHelper.GetParent(dpiRoot) is Visual parent) dpiRoot = parent;
        DpiScale originalDpi = VisualTreeHelper.GetDpi(dpiRoot);
        string audioBefore = Audio(); int historyBefore = mixer.EditHistory.Entries.Count;
        string Audio() => JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json);

        void Verify(GraphEdge edge, string context)
        {
            var paths = workspace.ConnectionPaths(edge.Id)!.Value;
            var hit = paths.Hit;
            var canvas = (Canvas)VisualTreeHelper.GetParent(hit);
            Rect owner = new(new Point(), hit.RenderSize);
            Rect ink = hit.RenderedGeometry.GetRenderBounds(new Pen(hit.Stroke, hit.StrokeThickness));
            require(!ink.IsEmpty && owner.Contains(ink) && ink.Left > .5 && ink.Top > .5
                && owner.Right - ink.Right > .5 && owner.Bottom - ink.Bottom > .5,
                context + " contains the entire actual hit stroke inside its tooltip owner's RenderSize with a rounding margin. "
                + $"owner={owner}; ink={ink}");
            require(!hit.UseLayoutRounding && !hit.SnapsToDevicePixels && hit.Stretch == Stretch.None
                && Near(hit.StrokeThickness * mixer.GraphConfig.Zoom, 16)
                && Near(workspace.ConnectionHitWidth(edge.Id), 16),
                context + " keeps an unrounded, unstretched 16-screen-DIP hit target.");
            require(ReferenceEquals(paths.Visible.Data, paths.Outline.Data) && paths.Visible.Data.IsFrozen
                && hit.Data.IsFrozen && !ReferenceEquals(paths.Visible.Data, hit.Data)
                && ReferenceEquals(hit.ToolTip, workspace.ConnectionToolTip(edge.Id)),
                context + " retains the visible world curve and outline while the same tooltip owner uses separate frozen local geometry.");
            var toWorld = hit.TransformToAncestor(canvas);
            var views = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id);
            var source = views[edge.From].FindSocket(edge.Kind == "sidechain" ? "audio" : edge.Kind, true)!;
            var target = views[edge.To].FindSocket(edge.Kind, false)!;
            Point sourceAnchor = workspace.SocketPosition(source), targetAnchor = workspace.SocketPosition(target);
            Rect expectedWorldInk = paths.Visible.RenderedGeometry.GetRenderBounds(new Pen(hit.Stroke, hit.StrokeThickness));
            Rect actualWorldInk = toWorld.TransformBounds(ink);
            double boundsDelta = Math.Max((expectedWorldInk.TopLeft - actualWorldInk.TopLeft).Length,
                (expectedWorldInk.BottomRight - actualWorldInk.BottomRight).Length);

            // Compare the actual cubic's defining points and affine mapping
            // independently of WPF's native flattening/arc-length approximation.
            double reach = Math.Max(70, Math.Abs(targetAnchor.X - sourceAnchor.X) * .48);
            Point[] expected = [sourceAnchor, new(sourceAnchor.X + reach, sourceAnchor.Y),
                new(targetAnchor.X - reach, targetAnchor.Y), targetAnchor];
            var worldCubic = AnalyticCurve(paths.Visible.Data);
            var localCubic = AnalyticCurve(hit.Data);
            double controlDelta = double.PositiveInfinity;
            if (worldCubic.Points.Length == 4 && localCubic.Points.Length == 4)
            {
                controlDelta = 0;
                for (int i = 0; i < expected.Length; i++)
                {
                    controlDelta = Math.Max(controlDelta, (worldCubic.Transform.Transform(worldCubic.Points[i]) - expected[i]).Length);
                    controlDelta = Math.Max(controlDelta, (toWorld.Transform(localCubic.Transform.Transform(localCubic.Points[i])) - expected[i]).Length);
                }
            }
            double matrixDelta = 0;
            foreach (Point basis in new Point[] { new(0, 0), new(1, 0), new(0, 1) })
                matrixDelta = Math.Max(matrixDelta,
                    (toWorld.Transform(hit.Data.Transform.Transform(basis)) - paths.Visible.Data.Transform.Transform(basis)).Length);
            string deltas = FormattableString.Invariant($" World DIP deltas: controls={controlDelta:G9}; matrix={matrixDelta:G9}; strokeBounds={boundsDelta:G9}.");
            require(controlDelta <= .000001 && matrixDelta <= .000001,
                context + " keeps the analytic Bezier control points and local-to-world matrix equal to the original socket-defined curve." + deltas);
            var toLocal = toWorld.Inverse;
            require(toLocal is not null, context + " has an invertible actual hit-to-world mapping.");
            var actualPen = new Pen(hit.Stroke, hit.StrokeThickness);
            var visiblePen = new Pen(paths.Visible.Stroke, paths.Visible.StrokeThickness);
            int firstOutsideOwner = -1, firstOutsideStroke = -1, firstOutsideVisible = -1;
            double roundTripDelta = 0;
            for (int sample = 0; sample <= 16; sample++)
            {
                Point worldPoint = Cubic(expected, sample / 16d);
                Point localPoint = toLocal!.Transform(worldPoint);
                roundTripDelta = Math.Max(roundTripDelta, (toWorld.Transform(localPoint) - worldPoint).Length);
                if (!owner.Contains(localPoint) && firstOutsideOwner < 0) firstOutsideOwner = sample;
                // Flat-cap boundary inclusion is float-sensitive; exact endpoints
                // are already proved by the strict control-point comparison.
                if (sample is > 0 and < 16)
                {
                    if (!hit.RenderedGeometry.StrokeContains(actualPen, localPoint) && firstOutsideStroke < 0) firstOutsideStroke = sample;
                    if (!paths.Visible.RenderedGeometry.StrokeContains(visiblePen, worldPoint) && firstOutsideVisible < 0) firstOutsideVisible = sample;
                }
            }
            require(firstOutsideOwner < 0 && firstOutsideStroke < 0 && firstOutsideVisible < 0 && roundTripDelta <= .000001,
                context + " contains all independent Bezier samples inside the owner and its interior samples in both rendered curve strokes."
                + FormattableString.Invariant($" First outside owner={firstOutsideOwner}; first outside hit stroke={firstOutsideStroke}; first outside visible stroke={firstOutsideVisible}; round-trip delta={roundTripDelta:G9} world DIP.") + deltas);
            // Native ink-bound queries may round at large coordinates; .001
            // world DIP is at most .004 device pixel in this test matrix.
            const double geometryQueryTolerance = .001;
            require(boundsDelta <= geometryQueryTolerance,
                context + " preserves the stroked world-space hit bounds within native geometry-query precision." + deltas);
            Point onCurve = toLocal!.Transform(Cubic(expected, .5));
            var clearance = new Pen(hit.Stroke, hit.StrokeThickness + 20 / mixer.GraphConfig.Zoom);
            Point? empty = new[]
            {
                new Point(owner.Width * .1, owner.Height * .9), new Point(owner.Width * .9, owner.Height * .1),
                new Point(owner.Width * .1, owner.Height * .1), new Point(owner.Width * .9, owner.Height * .9)
            }.Where(point => !hit.RenderedGeometry.StrokeContains(clearance, point)).Select(point => (Point?)point).FirstOrDefault();
            require(ReferenceEquals(DetachedHit(hit, onCurve), hit) && empty is Point emptyPoint
                && owner.Contains(emptyPoint) && DetachedHit(hit, emptyPoint) is null,
                context + " hits the actual cable stroke but leaves empty space inside its larger tooltip bounds available to the canvas.");
        }

        try
        {
            foreach (double dpi in new[] { 96d, 120, 144, 192 })
            {
                VisualTreeHelper.SetRootDpi(dpiRoot, new DpiScale(dpi / 96, dpi / 96));
                InvalidateTree(dpiRoot); await layout(); workspace.UpdateWires(); await layout();
                foreach (double zoom in new[] { .3, .75, 1, 1.5, 2 })
                {
                    var beforeZoom = mixer.GraphConfig.Edges.ToDictionary(edge => edge.Id,
                        edge => workspace.ConnectionPaths(edge.Id)!.Value);
                    var oldData = beforeZoom.ToDictionary(pair => pair.Key, pair => pair.Value.Hit.Data);
                    double oldZoom = mixer.GraphConfig.Zoom;
                    mixer.Edit(configuration => configuration.Graph.Zoom = zoom);
                    await layout(); workspace.UpdateWires(); await layout();
                    foreach (var edge in mixer.GraphConfig.Edges)
                    {
                        string context = $"{edge.Id} at {dpi} DPI / {zoom:P0}";
                        var paths = workspace.ConnectionPaths(edge.Id)!.Value;
                        require(Near(VisualTreeHelper.GetDpi(paths.Hit).PixelsPerInchX, dpi),
                            context + " exercises actual visual-tree DPI rather than bitmap enlargement.");
                        if (oldZoom != zoom)
                            require(ReferenceEquals(paths.Hit, beforeZoom[edge.Id].Hit)
                                && !ReferenceEquals(paths.Hit.Data, oldData[edge.Id]),
                                context + " refreshes local stroke padding on zoom even when world endpoints stay unchanged.");
                        Verify(edge, context);
                    }
                    var stable = mixer.GraphConfig.Edges.ToDictionary(edge => edge.Id, edge =>
                    {
                        var paths = workspace.ConnectionPaths(edge.Id)!.Value;
                        return (paths.Hit, HitData: paths.Hit.Data, Placement: paths.Hit.RenderTransform,
                            WorldData: paths.Visible.Data, Tip: paths.Hit.ToolTip);
                    });
                    long builds = workspace.WireGeometryBuilds;
                    for (int refresh = 0; refresh < 3; refresh++)
                    { mixer.RefreshPreviewTelemetry(); await layout(); workspace.UpdateWires(); await layout(); }
                    require(workspace.WireGeometryBuilds == builds && mixer.GraphConfig.Edges.All(edge =>
                    {
                        var paths = workspace.ConnectionPaths(edge.Id)!.Value; var previous = stable[edge.Id];
                        return ReferenceEquals(paths.Hit, previous.Hit) && ReferenceEquals(paths.Hit.Data, previous.HitData)
                            && ReferenceEquals(paths.Hit.RenderTransform, previous.Placement)
                            && ReferenceEquals(paths.Visible.Data, previous.WorldData) && ReferenceEquals(paths.Hit.ToolTip, previous.Tip);
                    }), $"At {dpi} DPI / {zoom:P0}, repeated telemetry and layout preserve the hit owner, geometry, placement and tooltip.");
                }
            }
            require(Audio() == audioBefore && mixer.EditHistory.Entries.Count == historyBefore,
                "Hover bounds, DPI and zoom verification change no audio settings, routes or Undo history.");

            var movingEdge = mixer.GraphConfig.Edges.Single(edge => edge.Id == "negative-both");
            var original = workspace.ConnectionPaths(movingEdge.Id)!.Value;
            var originalData = original.Hit.Data; var originalTip = original.Hit.ToolTip;
            workspace.NodeChanged(movingEdge.From, node => { node.X -= 37.25; node.Y += 41.25; });
            await layout(); workspace.UpdateWires(); await layout();
            var moved = workspace.ConnectionPaths(movingEdge.Id)!.Value;
            require(ReferenceEquals(original.Hit, moved.Hit) && ReferenceEquals(originalTip, moved.Hit.ToolTip)
                && !ReferenceEquals(originalData, moved.Hit.Data),
                "Moving a cable endpoint rebuilds only the needed geometry while retaining its hover owner and unchanged details.");
            Verify(movingEdge, "Moved negative-coordinate cable");
            var movedData = moved.Hit.Data;
            workspace.SelectEdge(movingEdge.Id); await layout();
            require(ReferenceEquals(movedData, workspace.ConnectionPaths(movingEdge.Id)!.Value.Hit.Data),
                "Cable selection changes the visible emphasis without replacing the hit geometry or its hover owner.");
        }
        finally
        {
            VisualTreeHelper.SetRootDpi(dpiRoot, originalDpi); InvalidateTree(dpiRoot);
            mixer.ApplyPreviewSnapshot(Fixture()); workspace.DismissMessage();
            await layout(); workspace.UpdateWires(); await layout();
        }
    }

    private static MixerSnapshot Fixture()
    {
        var graph = new GraphConfiguration { PanX = 38, PanY = 27, Zoom = 1 };
        foreach (var (id, kind, from, to) in new[]
        {
            ("negative-x", "audio", new Point(-2100.25, 250.4), new Point(-1400.2, 650.35)),
            ("negative-y", "control", new Point(250.25, -2700.4), new Point(950.2, -2100.35)),
            ("negative-both", "audio", new Point(-2100.25, -2700.4), new Point(-1400.2, -2100.35)),
            ("crosses-origin", "sidechain", new Point(-700.25, -700.4), new Point(400.2, 100.35)),
            ("positive", "audio", new Point(250.25, 250.4), new Point(950.2, 850.35)),
            ("reverse", "audio", new Point(900.25, -700.4), new Point(-500.2, 400.35))
        })
        {
            var source = GraphDefaults.NewNode(kind == "control" ? "control" : "microphone", from.X, from.Y);
            var target = GraphDefaults.NewNode(kind == "sidechain" ? "ducking" : "mix", to.X, to.Y);
            source.Id = id + "-from"; target.Id = id + "-to";
            graph.Nodes.Add(source); graph.Nodes.Add(target);
            graph.Edges.Add(new() { Id = id, From = source.Id, To = target.Id, Kind = kind, GainDb = -3.5, Muted = true });
        }
        return new() { Configuration = new() { Version = 2, Revision = 1, Graph = graph },
            Status = new() { Nodes = graph.Nodes.Select(node => new GraphNodeStatus { Id = node.Id, Path = new() { State = "Running" } }).ToList() } };
    }
    private static bool Near(double first, double second) => Math.Abs(first - second) < .00001;
    private static Point Cubic(Point[] points, double t)
    {
        double u = 1 - t;
        return new(u * u * u * points[0].X + 3 * u * u * t * points[1].X + 3 * u * t * t * points[2].X + t * t * t * points[3].X,
            u * u * u * points[0].Y + 3 * u * u * t * points[1].Y + 3 * u * t * t * points[2].Y + t * t * t * points[3].Y);
    }
    private static (Point[] Points, Matrix Transform) AnalyticCurve(Geometry geometry)
    {
        var path = PathGeometry.CreateFromGeometry(geometry);
        if (path.Figures.Count != 1 || path.Figures[0].Segments.Count != 1) return ([], path.Transform.Value);
        var figure = path.Figures[0];
        return figure.Segments[0] switch
        {
            BezierSegment curve => ([figure.StartPoint, curve.Point1, curve.Point2, curve.Point3], path.Transform.Value),
            PolyBezierSegment curve when curve.Points.Count == 3
                => ([figure.StartPoint, curve.Points[0], curve.Points[1], curve.Points[2]], path.Transform.Value),
            _ => ([], path.Transform.Value)
        };
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void InvalidateTree(DependencyObject root)
    {
        if (root is UIElement element) { element.InvalidateMeasure(); element.InvalidateArrange(); }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) InvalidateTree(VisualTreeHelper.GetChild(root, i));
    }
    private static DependencyObject? DetachedHit(Visual root, Point point)
    {
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(root,
            visual => visual is UIElement element && (element.Visibility != Visibility.Visible || !element.IsHitTestVisible)
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        return hit;
    }
}
