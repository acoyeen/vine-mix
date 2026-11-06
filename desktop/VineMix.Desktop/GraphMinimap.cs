using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

internal readonly record struct MinimapNode(Rect Bounds, bool Selected);

/// <summary>A uniform world-to-map projection which includes the visible empty canvas.</summary>
internal readonly record struct MinimapProjection(Rect WorldBounds, double Scale, Vector Offset)
{
    internal static MinimapProjection Create(IEnumerable<MinimapNode> nodes, Rect viewport, Size size)
    {
        Rect bounds = Usable(viewport) ? viewport : new Rect(0, 0, 1000, 600);
        foreach (var node in nodes) if (Usable(node.Bounds)) bounds.Union(node.Bounds);
        bounds.Inflate(32, 32);
        double width = Math.Max(1, size.Width - 20), height = Math.Max(1, size.Height - 20);
        double scale = Math.Min(width / bounds.Width, height / bounds.Height);
        return new(bounds, scale, new Vector(10 + (width - bounds.Width * scale) / 2 - bounds.X * scale,
            10 + (height - bounds.Height * scale) / 2 - bounds.Y * scale));
    }

    internal Point ToMap(Point point) => new(point.X * Scale + Offset.X, point.Y * Scale + Offset.Y);
    internal Point ToWorld(Point point) => new((point.X - Offset.X) / Scale, (point.Y - Offset.Y) / Scale);
    internal Rect ToMap(Rect bounds) => new(ToMap(bounds.TopLeft), new Size(bounds.Width * Scale, bounds.Height * Scale));
    internal static bool Usable(Rect bounds) => !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0
        && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Right) && double.IsFinite(bounds.Bottom);
}

/// <summary>Small canvas overview. Navigation changes only the workspace camera.</summary>
internal sealed class GraphMinimap : FrameworkElement
{
    private MinimapNode[] _nodes = [];
    private Rect _viewport = new(0, 0, 1000, 600);
    private MinimapProjection _projection;
    private Vector _grabOffset;
    private Point _panStart, _lastPoint;
    private bool _panMoved;
    internal bool IsDragging { get; private set; }
    internal MinimapProjection Projection => _projection;
    internal Rect VisibleWorld => _viewport;
    internal IReadOnlyList<MinimapNode> Nodes => _nodes;
    internal Func<bool>? CanBeginNavigation { get; set; }
    internal event Action? NavigationStarted;
    internal event Action<Point>? CenterRequested;
    internal event Action<bool>? NavigationFinished;

    internal GraphMinimap()
    {
        Focusable = false; ClipToBounds = true; Cursor = Cursors.Hand; SnapsToDevicePixels = true;
        AutomationProperties.SetName(this, "Canvas minimap");
        AutomationProperties.SetHelpText(this, "Shows the nodes and the visible canvas. Click to centre or drag to pan. Show minimap in Settings hides or shows this overview.");
        ToolTip = "Canvas overview · click to centre, drag to pan";
        SizeChanged += (_, _) => { if (!IsDragging) RebuildProjection(); };
    }

    internal void SetScene(IEnumerable<MinimapNode> nodes, Rect viewport)
    {
        if (!MinimapProjection.Usable(viewport)) return;
        var next = nodes.Where(node => MinimapProjection.Usable(node.Bounds)).ToArray();
        if (_viewport == viewport && _nodes.SequenceEqual(next)) return;
        _nodes = next; _viewport = viewport;
        if (!IsDragging) RebuildProjection();
        InvalidateVisual();
    }

    private void RebuildProjection()
    {
        _projection = MinimapProjection.Create(_nodes, _viewport, RenderSize);
        InvalidateVisual();
    }

    internal bool BeginPan(Point point)
    {
        if (IsDragging || !(CanBeginNavigation?.Invoke() ?? true) || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
        RebuildProjection(); // Freeze this projection until release, even if the camera leaves the node bounds.
        Point center = new(_viewport.X + _viewport.Width / 2, _viewport.Y + _viewport.Height / 2);
        _grabOffset = _projection.ToMap(_viewport).Contains(point) ? _projection.ToWorld(point) - center : new Vector();
        _panStart = _lastPoint = point; _panMoved = false;
        IsDragging = true; NavigationStarted?.Invoke();
        MovePan(point);
        return true;
    }

    internal void MovePan(Point point)
    {
        if (!IsDragging || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        _lastPoint = point;
        if ((point - _panStart).Length > 3) _panMoved = true;
        CenterRequested?.Invoke(_projection.ToWorld(point) - _grabOffset);
    }

    internal void EndPan(bool cancel = false)
    {
        if (!IsDragging) return;
        // A click also centres when it lands inside the outline. A drag keeps
        // the initial grab offset so picking up the outline never jumps.
        if (!cancel && !_panMoved) CenterRequested?.Invoke(_projection.ToWorld(_lastPoint));
        IsDragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        NavigationFinished?.Invoke(cancel);
        RebuildProjection();
    }

    protected override void OnMouseDown(MouseButtonEventArgs args)
    {
        base.OnMouseDown(args);
        args.Handled = true;
        if (args.ChangedButton == MouseButton.Left && BeginPan(args.GetPosition(this)) && !CaptureMouse()) EndPan(cancel: true);
    }
    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (IsDragging) { MovePan(args.GetPosition(this)); args.Handled = true; }
    }
    protected override void OnMouseUp(MouseButtonEventArgs args)
    {
        base.OnMouseUp(args); args.Handled = true;
        if (IsDragging && args.ChangedButton == MouseButton.Left) { MovePan(args.GetPosition(this)); EndPan(); }
    }
    protected override void OnLostMouseCapture(MouseEventArgs args) { base.OnLostMouseCapture(args); EndPan(cancel: true); }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
        var background = Brush("ConsoleWindowBrush", Brushes.Black);
        var border = Brush("ConsoleBorderBrush", Brushes.Gray);
        var overview = Brush("CanvasOverviewBrush", Brushes.Gray);
        var selected = Brush("NodeSelectedBrush", Brushes.White);
        var foreground = Brush("ConsoleTextBrush", Brushes.White);
        drawing.DrawRoundedRectangle(background, new Pen(border, 1), new Rect(.5, .5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 5, 5);
        drawing.PushClip(new RectangleGeometry(new Rect(5, 5, Math.Max(0, ActualWidth - 10), Math.Max(0, ActualHeight - 10))));
        foreach (var node in _nodes)
        {
            drawing.DrawRectangle(node.Selected ? selected : overview, null, _projection.ToMap(node.Bounds));
        }
        var visible = _projection.ToMap(_viewport);
        drawing.DrawRectangle(null, new Pen(foreground, 1.5), visible);
        drawing.Pop();
    }
}
