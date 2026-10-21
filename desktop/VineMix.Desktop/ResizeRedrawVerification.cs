using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached layout/pixel and repaint scheduling checks; no native window or input.</summary>
internal static class ResizeRedrawVerification
{
    internal static async Task RunAsync(FrameworkElement root, MainWindow window, GraphWorkspace workspace,
        MixerViewModel mixer, Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(root) is null && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "Resize regression checks use detached synthetic content, without a window, engine or settings.");
        await CheckScheduling(root.Dispatcher, require);
        var content = (System.Windows.Controls.Grid)((System.Windows.Controls.Border)root).Child;
        require(IsOpaque(content.Background) && content.ClipToBounds
            && IsOpaque(window.HeaderBar.Background) && window.HeaderBar.ClipToBounds,
            "The resize surface and header explicitly cover and clip their full bounds with opaque theme brushes.");
        Visual dpiRoot = root;
        while (VisualTreeHelper.GetParent(dpiRoot) is Visual parent) dpiRoot = parent;
        var previousDpi = VisualTreeHelper.GetDpi(dpiRoot);
        double previousWidth = root.Width, previousHeight = root.Height;
        long presentations = mixer.ConfigurationPresentationCount;
        int history = mixer.EditHistory.Entries.Count;
        string saved = mixer.SaveStatus;
        int pending = mixer.PendingEditCount;
        var graph = mixer.GraphConfig.Copy();
        var committed = mixer.CurrentUpdateHealthSnapshot?.Configuration.Copy();
        long transforms = workspace.ViewTransformUpdates;
        try
        {
            foreach (double scale in new[] { 1d, 1.25d, 1.5d, 2d })
            {
                VisualTreeHelper.SetRootDpi(dpiRoot, new DpiScale(scale, scale));
                // SetRootDpi on a detached visual is not a native WM_DPICHANGED
                // transition. Establish the same explicit geometry baseline as
                // the renderer's other synthetic-DPI checks before testing resize.
                root.InvalidateMeasure(); root.InvalidateArrange();
                await layout(); workspace.UpdateWires(); await layout();
                bool dpiSettled = false;
                var accumulated = new RenderTargetBitmap((int)(1920 * scale), (int)(1080 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                // Exercise both direction changes and fractional pixel sizes,
                // including the minimum, normal and a large/maximised layout.
                foreach (var size in new[] { new Size(900, 580), new Size(1063, 697), new Size(1280, 800),
                    new Size(1601, 943), new Size(1920, 1080), new Size(1280, 800), new Size(1063, 697), new Size(900, 580) })
                {
                    var beforeAnchors = Anchors(workspace, mixer);
                    long beforeWires = workspace.WireGeometryBuilds, beforeTransforms = workspace.ViewTransformUpdates;
                    root.Width = size.Width; root.Height = size.Height;
                    await layout();
                    // Compare complete pixels inside the client area; a
                    // fractional outer edge is a clip boundary, not a stale glyph.
                    int width = (int)Math.Floor(root.ActualWidth * scale), height = (int)Math.Floor(root.ActualHeight * scale);
                    var fresh = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    fresh.Render(root);
                    // Render onto the same uncleared target to make any missing
                    // background coverage leave the previous glyphs in place.
                    accumulated.Render(root);
                    var header = new Int32Rect(0, 0, width, (int)Math.Ceiling((MainWindow.TitleBarHeight + 1 + GraphWorkspace.ToolbarHeight) * scale));
                    var footer = new Int32Rect(0, Math.Max(0, height - (int)Math.Ceiling(48 * scale)), width, Math.Min(height, (int)Math.Ceiling(48 * scale)));
                    require(EqualPixels(fresh, accumulated, header) && EqualPixels(fresh, accumulated, footer),
                        $"Growing/shrinking chrome replaces old glyph pixels with the same pixels as a fresh render ({size.Width:0}×{size.Height:0}, {scale * 100:0}%); this checks detached coverage, not compositor/live-drag behavior.");
                    // Rendering and fractional-DPI layout can settle socket
                    // positions in another layout pass. Measure the real anchors;
                    // a necessary cable update must not be mistaken for churn.
                    await layout();
                    var afterAnchors = Anchors(workspace, mixer);
                    var moved = afterAnchors.Keys.Where(id => !Same(beforeAnchors[id].From, afterAnchors[id].From)
                        || !Same(beforeAnchors[id].To, afterAnchors[id].To)).ToArray();
                    bool cablesTrackSockets = afterAnchors.All(pair => Tracks(workspace, pair.Key, pair.Value));
                    require(cablesTrackSockets && workspace.ViewTransformUpdates == beforeTransforms
                        && (!dpiSettled || moved.Length != 0 || workspace.WireGeometryBuilds == beforeWires),
                        $"Resized cables follow actual rounded socket anchors; unchanged anchors reuse cached geometry ({size.Width:0}×{size.Height:0}, {scale * 100:0}%); changed anchors {moved.Length}, wires {beforeWires}→{workspace.WireGeometryBuilds}, transforms {beforeTransforms}→{workspace.ViewTransformUpdates}; "
                        + string.Join("; ", moved.Take(2).Select(id => $"{id}: {beforeAnchors[id]}→{afterAnchors[id]}"))
                        + string.Join("; ", afterAnchors.Where(pair => !Tracks(workspace, pair.Key, pair.Value)).Take(2)
                            .Select(pair => $" {pair.Key}: path {PathEndpoints(workspace, pair.Key)}, expected {pair.Value}")));
                    dpiSettled = true;
                }
            }
        }
        finally
        {
            root.Width = previousWidth; root.Height = previousHeight;
            VisualTreeHelper.SetRootDpi(dpiRoot, previousDpi);
            root.InvalidateMeasure(); root.InvalidateArrange();
            await layout(); workspace.UpdateWires(); await layout();
        }
        require(mixer.ConfigurationPresentationCount == presentations && mixer.EditHistory.Entries.Count == history && mixer.SaveStatus == saved
            && mixer.PendingEditCount == pending && workspace.ViewTransformUpdates == transforms
            && WorkspaceHistory.SameValue(graph, mixer.GraphConfig)
            && WorkspaceHistory.SameValue(committed, mixer.CurrentUpdateHealthSnapshot?.Configuration),
            $"Resize/DPI layout preserves the complete committed configuration, graph/camera, pending edits, history and save status; presentations {presentations}→{mixer.ConfigurationPresentationCount}, history {history}→{mixer.EditHistory.Entries.Count}, pending {pending}→{mixer.PendingEditCount}, transforms {transforms}→{workspace.ViewTransformUpdates}.");
        require(PresentationSource.FromVisual(root) is null && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "Resize coverage verification finishes without creating a native window; physical live-resize ghosting remains a separate check.");
    }

    private static async Task CheckScheduling(Dispatcher dispatcher, Action<bool, string> require)
    {
        require(ResizeRedraw.IsResizeNotification(0x0005, IntPtr.Zero)
            && ResizeRedraw.IsResizeNotification(0x0005, new IntPtr(2))
            && ResizeRedraw.IsResizeNotification(0x0232, IntPtr.Zero)
            && !ResizeRedraw.IsResizeNotification(0x0005, new IntPtr(1))
            && !ResizeRedraw.IsResizeNotification(0x0005, new IntPtr(3))
            && !ResizeRedraw.IsResizeNotification(0x0005, new IntPtr(4))
            && !ResizeRedraw.IsResizeNotification(0x0003, IntPtr.Zero)
            && !ResizeRedraw.IsResizeNotification(0x000F, IntPtr.Zero),
            "Only restored/maximised size notifications and final sizing-loop exit request the repaint; minimise, move and paint do not create a repaint loop.");
        var calls = new List<string>();
        using var redraw = new ResizeRedraw(dispatcher, () => calls.Add("repaint"));
        redraw.Request(); redraw.Request(); redraw.Request();
        _ = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => calls.Add("render")));
        require(calls.Count == 0, "Native resize notifications never synchronously force layout or painting from the window hook.");
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        require(calls.SequenceEqual(new[] { "render", "repaint" }),
            "A burst of size notifications produces one repaint after the pending Render operation.");
        redraw.Request();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        require(calls.Count == 3 && calls[2] == "repaint", "A later size change schedules a new repaint after the first batch has finished.");
        redraw.Request(); redraw.Dispose(); redraw.Request();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        require(calls.Count == 3, "Closing cancels the pending repaint and prevents later callbacks from touching a disposed native surface.");
    }

    private static bool IsOpaque(Brush? brush) => brush is SolidColorBrush solid && solid.Opacity == 1 && solid.Color.A == 255;
    private static Dictionary<string, (Point From, Point To)> Anchors(GraphWorkspace workspace, MixerViewModel mixer)
    {
        var nodes = Descendants<GraphNodeView>(workspace).ToDictionary(node => node.Id);
        return mixer.GraphConfig.Edges.ToDictionary(edge => edge.Id, edge =>
            (workspace.SocketPosition(nodes[edge.From].FindSocket(edge.Kind == "sidechain" ? "audio" : edge.Kind, true)!),
                workspace.SocketPosition(nodes[edge.To].FindSocket(edge.Kind, false)!)));
    }
    private static bool Tracks(GraphWorkspace workspace, string id, (Point From, Point To) anchors)
        => Endpoints(workspace, id, out var first, out var last) && Same(first, anchors.From) && Same(last, anchors.To);
    private static string PathEndpoints(GraphWorkspace workspace, string id)
        => Endpoints(workspace, id, out var first, out var last) ? $"({first} → {last})" : "missing/unsupported curve";
    private static bool Endpoints(GraphWorkspace workspace, string id, out Point first, out Point last)
    {
        first = last = default;
        var path = workspace.ConnectionPaths(id)?.Visible.Data;
        if (path is null) return false;
        var geometry = PathGeometry.CreateFromGeometry(path);
        if (geometry.Figures.Count != 1 || geometry.Figures[0].Segments.Count == 0) return false;
        var figure = geometry.Figures[0];
        Point? end = figure.Segments[^1] switch
        {
            BezierSegment curve => curve.Point3,
            PolyBezierSegment curves when curves.Points.Count != 0 => curves.Points[^1],
            LineSegment line => line.Point,
            PolyLineSegment lines when lines.Points.Count != 0 => lines.Points[^1],
            _ => null
        };
        if (end is not { } point) return false;
        first = figure.StartPoint; last = point; return true;
    }
    private static bool Same(Point first, Point second) => Math.Abs(first.X - second.X) <= .000001 && Math.Abs(first.Y - second.Y) <= .000001;
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static bool EqualPixels(BitmapSource first, BitmapSource second, Int32Rect region)
    {
        int stride = region.Width * 4;
        var a = new byte[stride * region.Height];
        var b = new byte[a.Length];
        first.CopyPixels(region, a, stride, 0); second.CopyPixels(region, b, stride, 0);
        return a.AsSpan().SequenceEqual(b);
    }
}
