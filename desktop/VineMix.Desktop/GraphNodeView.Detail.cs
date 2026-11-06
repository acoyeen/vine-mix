using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

// Detail is a presentation policy, separate from node configuration and ports.
// Hidden (never Collapsed) retains the same measure, bounds and socket geometry.
internal sealed partial class GraphNodeView
{
    private NodeDetailLevel _detailLevel;
    private bool _detailInitialized;
    internal NodeDetailLevel DetailLevel => _detailLevel;
    internal bool DetailHeaderVisible => _headerFace.Visibility == Visibility.Visible;
    internal bool DetailHeaderTextVisible => _title.Visibility == Visibility.Visible;

    internal void SetViewScale(double scale)
    {
        _viewScale = GraphCanvasPolicy.ClampZoom(scale);
        RefreshDetailPresentation();
        UpdateInteractionAdornments();
    }

    internal static NodeDetailLevel DetailAt(double scale, NodeDetailLevel previous)
        => GraphNodeDetailPolicy.At(scale, previous);

    private bool DetailEditingActive => IsKeyboardFocusWithin || IsMouseCaptureWithin || HasPendingRename
        || _owner.IsNodeDragging || DescendantDials(_body).Any(dial => dial.HasPendingEntry);

    private static IEnumerable<RotaryValueControl> DescendantDials(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is RotaryValueControl dial) yield return dial;
            else foreach (var nested in DescendantDials(child)) yield return nested;
        }
    }

    private void RefreshDetailPresentation()
    {
        if (!_detailInitialized)
        {
            LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshDetailPresentation));
            LostMouseCapture += (_, _) => RefreshDetailPresentation();
            PreviewGotKeyboardFocus += (_, _) =>
            {
                if (_detailLevel != NodeDetailLevel.Full) _owner.RevealNodeForControls(Id);
            };
            _detailInitialized = true;
        }
        var next = DetailAt(_viewScale, _detailLevel);
        if (next > _detailLevel) next = GraphNodeDetailPolicy.At(_viewScale, _detailLevel, DetailEditingActive);
        bool changed = _detailLevel != next;
        _detailLevel = next;
        bool full = next == NodeDetailLevel.Full, overview = next == NodeDetailLevel.Overview;
        // Low zoom leaves a plain panel and coloured header. Hidden controls
        // still reserve their layout space, keeping every cable anchor stable.
        _headerFace.Visibility = Visibility.Visible;
        _title.Visibility = full ? Visibility.Visible : Visibility.Hidden;
        _body.Visibility = full ? Visibility.Visible : Visibility.Hidden;
        _meterPeakDisplay.Visibility = full ? Visibility.Visible : Visibility.Hidden;
        _inputs.Visibility = full ? Visibility.Visible : Visibility.Hidden;
        if (_outputRow is not null) _outputRow.Visibility = full ? Visibility.Visible : Visibility.Hidden;
        if (_effectToggle is not null) _effectToggle.Visibility = full ? Visibility.Visible : Visibility.Hidden;
        foreach (var socket in _sockets) { socket.IsHitTestVisible = !overview; socket.Focusable = !overview; }
        ToolTip = full ? null : NodeToolTip.Create(_node.Name, NodeCatalog.Title(_kind),
            _node.Muted ? "Muted" : _node.Bypass ? "Bypassed" : _status.Text, "Zoom in to edit controls and connections");
        if (changed)
        {
            _owner.UpdateCableDetail(Id);
            UpdateAvailabilityWarningText();
            if (!full) _eqPlot?.ApplySpectrum(null, 0, 0, fresh: false);
        }
    }

}
