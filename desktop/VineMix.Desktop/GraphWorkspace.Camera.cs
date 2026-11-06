using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;

namespace VineMix.Desktop;

// Canvas coordinates and view navigation are independent of audio graph edits.
public sealed partial class GraphWorkspace
{
    private void ApplyTransform()
    {
        // Render transforms run after layout rounding. Align the translation
        // to the current monitor's device pixels as well, including at 100%.
        var dpi = VisualTreeHelper.GetDpi(this);
        _panX = GraphCanvasPolicy.ClampPan(Math.Round(_panX * dpi.DpiScaleX) / dpi.DpiScaleX);
        _panY = GraphCanvasPolicy.ClampPan(Math.Round(_panY * dpi.DpiScaleY) / dpi.DpiScaleY);
        var matrix = new Matrix(_zoom, 0, 0, _zoom, _panX, _panY);
        if (_viewTransform.Matrix != matrix) { _viewTransform.Matrix = matrix; ViewTransformUpdates++; }
        _grid.SetView(_panX, _panY, _zoom);
        if (_displayedZoom != _zoom)
        {
            _displayedZoom = _zoom; _zoomReset.Content = $"{_zoom * 100:0}%";
            AutomationProperties.SetHelpText(_zoomReset, $"Current zoom {_zoom * 100:0} percent. Activate to return to 100 percent.");
            foreach (var view in _nodes.Values) view.SetViewScale(_zoom);
            _cableDetailLevel = GraphNodeDetailPolicy.At(_zoom, _cableDetailLevel);
            foreach (var pair in _wires) SetWireWidth(pair.Key, pair.Value);
            RefreshFrames();
        }
        if (_snapSocket is not null) SetSnapSocket(IsCurrentSocket(_snapSocket) ? _snapSocket : null);
        RefreshMinimap();
    }
    private void PersistView()
    {
        double x = _panX, y = _panY, z = _zoom;
        if (Graph.PanX == x && Graph.PanY == y && Graph.Zoom == z) return;
        _mixer.SetCanvasView(x, y, z);
    }
    private void ZoomAt(Point anchor, double zoom)
    {
        double next = GraphCanvasPolicy.ClampZoom(zoom);
        if (_zoom == next) return;
        var view = GraphCanvasPolicy.ZoomAround(new(_panX, _panY, _zoom), anchor, next);
        _zoom = view.Zoom; _panX = view.PanX; _panY = view.PanY; ApplyTransform(); PersistView();
    }
    internal void ResetZoom() { _wheelRemainder = 0; ZoomAt(new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2), 1); }
    private void StepZoom(Point anchor, int steps)
    {
        // Deliberate stops always include 100%; repeated input does not drift
        // through arbitrary fractional scales that make small text hard to read.
        double next = _zoom;
        for (int step = 0; step < Math.Min(Math.Abs((long)steps), ZoomStops.Length); step++)
            next = steps > 0
                ? ZoomStops.FirstOrDefault(value => value > next + .0001, ZoomStops[^1])
                : ZoomStops.LastOrDefault(value => value < next - .0001, ZoomStops[0]);
        ZoomAt(anchor, next);
    }
    internal void ApplyWheelDelta(Point anchor, int delta)
    {
        if (delta == 0) return;
        // High-resolution wheels can report a fraction of the standard 120
        // units per notch. Accumulate them instead of zooming a whole step for
        // every tiny event; a multi-notch event is applied in one transform.
        if (Math.Sign(delta) != Math.Sign(_wheelRemainder)) _wheelRemainder = 0;
        long accumulated = (long)_wheelRemainder + delta;
        int steps = (int)Math.Clamp(accumulated / 120, -ZoomStops.Length, ZoomStops.Length);
        _wheelRemainder = (int)(accumulated % 120);
        if (steps != 0) StepZoom(anchor, steps);
    }
    public void FitAll()
    {
        _wheelRemainder = 0;
        if (_nodes.Count == 0) { _panX = 20; _panY = 20; _zoom = 1; ApplyTransform(); PersistView(); return; }
        double availableWidth = Math.Max(0, _viewport.ActualWidth - 64), availableHeight = Math.Max(0, _viewport.ActualHeight - 54);
        // Every node has fixed world-space dimensions at every zoom. Settle any
        // pending control changes once, then fit those bounds.
        _world.UpdateLayout();
        var bounds = Rect.Empty;
        foreach (var view in _nodes.Values)
        {
            view.Measure(new Size(view.Width, double.PositiveInfinity));
            bounds.Union(new Rect(Canvas.GetLeft(view), Canvas.GetTop(view), view.Width, view.DesiredSize.Height));
        }
        var nodeBounds = bounds;
        for (int pass = 0; pass < 4; pass++)
        {
            _zoom = GraphCanvasPolicy.FitZoom(bounds, new Size(availableWidth, availableHeight));
            bounds = IncludeFrameBounds(nodeBounds, _zoom, _nodes.Keys);
        }
        var camera = GraphCanvasPolicy.Centre(bounds, _viewport.RenderSize, _zoom);
        _panX = camera.PanX; _panY = camera.PanY;
        ApplyTransform(); PersistView();
    }
    private bool CanFrameSelection => !KeyboardNudgeActive && _dragged is null && _lockedLayoutGestureStart is null && !HasCanvasPanGesture && !_minimap.IsDragging && !_boxSelecting && !_cutting
        && _pendingSocket is null && !_socketDragging && !IsFrameSelectionPointerBusy(Mouse.Captured) && !_mixer.EditHistory.HasGestureEdit;
    internal bool CanOpenNodeSearch => CanFrameSelection;
    internal bool FindAndFrameNode(string id)
    {
        if (!CanFrameSelection || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0
            || !Graph.Nodes.Any(node => node.Id == id) || !_nodes.TryGetValue(id, out var view)) return false;
        _world.UpdateLayout();
        if (view.RenderSize.Width <= 0 || view.RenderSize.Height <= 0) return false;
        SelectNode(id);
        return FrameSelection();
    }
    // A menu may retain capture while invoking its command. That capture is
    // navigation, unlike a captured dial, selector, slider or canvas gesture.
    internal static bool IsFrameSelectionPointerBusy(IInputElement? captured) => captured is not (null or MenuBase or MenuItem);
    internal bool FrameSelection()
    {
        if (!CanFrameSelection) return false;
        var selected = _selectedNodes.Where(id => Graph.Nodes.Any(node => node.Id == id))
            .Select(id => _nodes.GetValueOrDefault(id)).OfType<GraphNodeView>().ToArray();
        if (selected.Length == 0) { SetMessage("Select one or more nodes to frame."); return false; }
        if (_viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return false;
        // Settle layout before using the rendered controls, including taller
        // processors and nodes whose controls have just changed size.
        _world.UpdateLayout();
        var bounds = Rect.Empty;
        foreach (var view in selected)
        {
            double x = Canvas.GetLeft(view), y = Canvas.GetTop(view);
            if (!double.IsFinite(x) || !double.IsFinite(y) || view.RenderSize.Width <= 0 || view.RenderSize.Height <= 0) continue;
            bounds.Union(new Rect(new Point(x, y), view.RenderSize));
        }
        if (bounds.IsEmpty) return false;
        _wheelRemainder = 0;
        double availableWidth = Math.Max(0, _viewport.ActualWidth - 64), availableHeight = Math.Max(0, _viewport.ActualHeight - 54);
        var nodeBounds = bounds;
        for (int pass = 0; pass < 4; pass++)
        {
            _zoom = GraphCanvasPolicy.FitZoom(bounds, new Size(availableWidth, availableHeight));
            bounds = IncludeFrameBounds(nodeBounds, _zoom, _selectedNodes);
        }
        var camera = GraphCanvasPolicy.Centre(bounds, _viewport.RenderSize, _zoom);
        _panX = camera.PanX; _panY = camera.PanY;
        ApplyTransform(); PersistView();
        return true;
    }
}
