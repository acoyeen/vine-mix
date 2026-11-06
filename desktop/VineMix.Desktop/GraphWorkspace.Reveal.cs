using System;
using System.Windows;

namespace VineMix.Desktop;

public sealed partial class GraphWorkspace
{
    // Control focus needs a readable view. Unlike Fit, this never settles below
    // full-detail scale for a single tall node; its controls remain reachable.
    internal bool RevealNodeForControls(string id)
    {
        if (!CanFrameSelection || !_nodes.TryGetValue(id, out var view)
            || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return false;
        _world.UpdateLayout();
        _zoom = Math.Max(.6, _zoom);
        var bounds = new Rect(CanvasPosition(view), view.RenderSize);
        // The saved pan range is finite. At extreme graph coordinates, keeping
        // a 200% view would leave the destination off-screen after clamping.
        var centred = GraphCanvasPolicy.Centre(bounds, _viewport.RenderSize, _zoom);
        if (_zoom > 1 && (Math.Abs(centred.PanX) > GraphCanvasPolicy.MaximumPan
            || Math.Abs(centred.PanY) > GraphCanvasPolicy.MaximumPan)) _zoom = 1;
        _panX = (_viewport.ActualWidth - bounds.Width * _zoom) / 2 - bounds.Left * _zoom;
        _panY = Math.Max(24, (_viewport.ActualHeight - bounds.Height * _zoom) / 2) - bounds.Top * _zoom;
        ApplyTransform(); PersistView(); return true;
    }

    private static Point CanvasPosition(GraphNodeView view)
        => new(System.Windows.Controls.Canvas.GetLeft(view), System.Windows.Controls.Canvas.GetTop(view));
}
