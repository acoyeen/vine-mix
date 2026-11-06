using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

public sealed partial class GraphWorkspace
{
    private NodeDetailLevel _cableDetailLevel;
    private CablePaintKey? _previewPaint;
    private readonly record struct CablePaintKey(Point From, Point To, Color Source, Color Destination);

    internal static string SocketBrushKey(string kind, bool output, string port, string? nodeKind)
        => kind == "control" ? "NodeControlBrush" : kind == "sidechain" ? "NodeSidechainBrush"
            : output && port == "wet" && nodeKind is "reverb" or "echo" ? "NodeEffectReturnBrush" : "NodeAudioBrush";

    private static Color SocketColor(string key) => ((SolidColorBrush)ConsoleTheme.Get(key)).Color;

    internal static Brush CableBrush(Point from, Point to, Color source, Color destination)
    {
        if (source == destination || (to - from).LengthSquared < .000000000001)
        {
            var solid = new SolidColorBrush(source); solid.Freeze(); return solid;
        }
        // Absolute world coordinates preserve source/destination direction even
        // for vertical, reversed or negative-coordinate routes. Relative bounds
        // would incorrectly flip colours or collapse a vertical gradient.
        var gradient = new LinearGradientBrush(source, destination, from, to)
        {
            MappingMode = BrushMappingMode.Absolute,
            ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation
        };
        gradient.Freeze(); return gradient;
    }

    internal static Point CableMidpoint(Point from, Point to)
        // The two horizontal Bezier handles are equal and opposite. This curve
        // is centrally symmetric, so t=.5 is also its half arc-length point.
        => from + (to - from) * .5;

    internal static (Point First, Point Second) CableControls(Point from, Point to)
    {
        // Scale horizontal tangents continuously with the actual socket gap.
        // A fixed minimum folds short forward links back over themselves.
        // Equal/opposite handles preserve symmetry for reverse/vertical routes
        // and keep the existing half-length marker calculation exact.
        double reach = (to - from).Length * .25;
        return (new Point(from.X + reach, from.Y), new Point(to.X - reach, to.Y));
    }

    private void PaintWire(GraphEdge edge, WireVisual wire, bool highlighted)
    {
        string sourceKind = edge.Kind == "sidechain" ? "audio" : edge.Kind;
        string sourceKey = _nodes.TryGetValue(edge.From, out var source)
            && source.FindSocket(sourceKind, true, edge.FromPort) is { } socket ? socket.ColourBrushKey
            : SocketBrushKey(sourceKind, true, edge.FromPort, null);
        string destinationKey = SocketBrushKey(edge.Kind, false, edge.ToPort, null);
        if (highlighted) sourceKey = destinationKey = "NodeSelectedBrush";
        else if (edge.Muted) sourceKey = destinationKey = "ConsoleMutedBrush";
        var key = new CablePaintKey(wire.From ?? new Point(), wire.To ?? new Point(), SocketColor(sourceKey), SocketColor(destinationKey));
        if (wire.Paint == key) return;
        wire.Paint = key;
        wire.Visible.Stroke = CableBrush(key.From, key.To, key.Source, key.Destination);
        // The midpoint lies halfway along the gradient axis as well as the
        // curve. Use the same sRGB interpolation as the cable itself.
        byte Half(byte a, byte b) => (byte)Math.Round((a + b) * .5);
        var middle = new SolidColorBrush(Color.FromArgb(Half(key.Source.A, key.Destination.A),
            Half(key.Source.R, key.Destination.R), Half(key.Source.G, key.Destination.G), Half(key.Source.B, key.Destination.B)));
        middle.Freeze(); wire.Marker.Fill = middle;
    }

    private void UpdateCableMarker(WireVisual wire)
    {
        bool full = _cableDetailLevel == NodeDetailLevel.Full
            || _nodes.TryGetValue(wire.SourceId, out var source) && source.DetailLevel == NodeDetailLevel.Full
            || _nodes.TryGetValue(wire.TargetId, out var target) && target.DetailLevel == NodeDetailLevel.Full;
        wire.Marker.Visibility = !wire.Muted && full && wire.Visible.Visibility == Visibility.Visible
            && wire.From is not null && wire.To is not null ? Visibility.Visible : Visibility.Hidden;
        wire.Marker.Opacity = wire.Visible.Opacity;
        // Keep the same size relative to nodes: 7.5 canvas DIPs across. The
        // shared canvas transform scales both together. It never receives hits.
        const double radius = 3.75;
        if (wire.From is Point from && wire.To is Point to)
        {
            var middle = CableMidpoint(from, to);
            if (wire.Marker.Data is not EllipseGeometry previous || previous.Center != middle || previous.RadiusX != radius)
            {
                // World-space geometry shares the cable's coordinate system.
                // Resizing a positioned Ellipse while hidden can retain its old
                // arranged centre for a frame when the zoom detail changes.
                var circle = new EllipseGeometry(middle, radius, radius); circle.Freeze(); wire.Marker.Data = circle;
            }
        }
    }

    internal void UpdateCableDetail(string nodeId)
    {
        // An active draft/focus can retain one endpoint's full details below
        // the canvas threshold. Follow it without adding any event handlers.
        foreach (var wire in _wires.Values)
            if (wire.SourceId == nodeId || wire.TargetId == nodeId) UpdateCableMarker(wire);
    }

    internal Path? ConnectionMarker(string id) => _wires.TryGetValue(id, out var wire) ? wire.Marker : null;
}
