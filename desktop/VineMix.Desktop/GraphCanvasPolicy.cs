using System;
using System.Windows;

namespace VineMix.Desktop;

internal readonly record struct GraphCanvasView(double PanX, double PanY, double Zoom)
{
    internal Point ToWorld(Point screen) => new((screen.X - PanX) / Zoom, (screen.Y - PanY) / Zoom);
}

// Shared desktop contract for navigation, persistence validation and adornments.
// Native compileGraph enforces the matching minimum; its historic upper bound
// remains wider so existing external configurations continue to load.
internal static class GraphCanvasPolicy
{
    internal const double MinimumZoom = .1;
    internal const double MaximumZoom = 2;
    internal const double MaximumPan = 100000;
    internal static readonly double[] ZoomStops = [.1, .15, .2, .25, .3, .4, .5, .6, .75, .9, 1, 1.1, 1.25, 1.5, 1.75, 2];
    internal static double ClampZoom(double zoom) => double.IsFinite(zoom) ? Math.Clamp(zoom, MinimumZoom, MaximumZoom) : 1;
    internal static double ClampPan(double pan) => double.IsNaN(pan) ? 20 : Math.Clamp(pan, -MaximumPan, MaximumPan);

    internal static GraphCanvasView ZoomAround(GraphCanvasView current, Point anchor, double zoom)
    {
        var world = current.ToWorld(anchor); double next = ClampZoom(zoom);
        return new(anchor.X - world.X * next, anchor.Y - world.Y * next, next);
    }

    internal static double FitZoom(Rect bounds, Size available)
        => bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ? 1
            : Math.Clamp(Math.Min(available.Width / bounds.Width, available.Height / bounds.Height), MinimumZoom, 1);

    internal static GraphCanvasView Centre(Rect bounds, Size viewport, double zoom)
        => new((viewport.Width - bounds.Width * zoom) / 2 - bounds.Left * zoom,
            (viewport.Height - bounds.Height * zoom) / 2 - bounds.Top * zoom, zoom);
}
