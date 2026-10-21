using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Informational peak hover integration using inert snapshots and detached tooltip rendering.</summary>
internal static class PeakMeterPresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<FrameworkElement, string> render)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null && mixer.PendingEditCount == 0,
            "Peak presentation checks use a detached preview with no native window, desktop input or live audio.");
        var fixture = Fixture();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        string sourceId = fixture.Configuration.Graph.Nodes[0].Id;
        var view = Descendants<GraphNodeView>(workspace).Single(node => node.Id == sourceId);
        var display = view.PeakMeterDisplay;
        var meter = display.Meter;
        Size initialSize = view.RenderSize;
        var sockets = SocketPositions(view);
        string originalGraph = Json(mixer.GraphConfig);
        int originalHistory = mixer.EditHistory.Entries.Count;
        var status = fixture.Status.Nodes.Single(node => node.Id == sourceId);
        GraphNode Source() => mixer.GraphConfig.Nodes.Single(node => node.Id == sourceId);
        try
        {
            require(display.Width == 28 && display.ActualWidth == 28 && meter.ActualWidth == 28
                && ReferenceEquals(display.Content, meter) && !meter.IsHitTestVisible
                && view.IsNodeDragSurface(display) && view.IsNodeDragSurface(meter),
                "Informational peak hover preserves the original 28-DIP meter without an extra label or control column.");
            require(display.Focusable && KeyboardNavigation.GetIsTabStop(display) && display.Cursor == Cursors.Arrow
                && AutomationProperties.GetLiveSetting(display) == AutomationLiveSetting.Off,
                "The informational meter remains keyboard-accessible without a hand cursor or live meter announcements.");
            var peer = UIElementAutomationPeer.CreatePeerForElement(display);
            require(peer is not null && peer.GetAutomationControlType() == AutomationControlType.Text
                && peer.GetPattern(PatternInterface.Invoke) is null
                && peer.GetName().Contains("peak", StringComparison.OrdinalIgnoreCase),
                "Peak information remains accessible without exposing a clickable action or Invoke pattern.");
            var hoverText = string.Join(" ", display.PeakToolTipContent.Children.OfType<TextBlock>().Select(text => text.Text));
            var accessibleText = AutomationProperties.GetHelpText(display) + " " + AutomationProperties.GetItemStatus(display) + " " + peer!.GetName();
            require(new[] { "click", "reset", "Enter/Space" }.All(action => !hoverText.Contains(action, StringComparison.OrdinalIgnoreCase)
                && !accessibleText.Contains(action, StringComparison.OrdinalIgnoreCase)),
                "Hover help and accessibility describe readings without click, reset or keyboard-action instructions.");
            require(display.PeakReadingText == "Left · −6.0 dBFS; Right · −12.0 dBFS" && meter.ClipLatched
                && display.ClipReadingText == "Over 0 dBFS detected",
                "Hover readings show independent sample peaks to one decimal and separate aggregate clipping evidence.");
            require(!Descendants<TextBlock>(view).Any(text => text.Text.Contains("dBFS", StringComparison.Ordinal)),
                "Peak numbers stay in hover help and accessibility, not permanent node labels.");
            RenderNode(view, "peak-meter-latched-node.png", render);
            RenderToolTip(display, "peak-meter-stereo-tooltip.png", require, render);
            require(meter.ClipLatched && meter.RetainedPeakLeft == .5 && meter.RetainedPeakRight == .25,
                "Presenting hover information preserves its observed peaks and clip latch.");
            var stableTip = display.PeakToolTipContent;
            // Internal lifecycle clearing remains independently testable; the
            // presentation exposes no user action for this operation.
            meter.ClearPeakReadings(); await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched && display.PeakReadingText == "No readings yet"
                && display.ClipReadingText.Length == 0 && meter.Level > 0,
                "An internal history clear updates hover information while the live bars remain visible.");
            for (int index = 0; index < 5; index++) view.Apply(Source(), status, selected: index % 2 == 0);
            await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched && ReferenceEquals(stableTip, display.PeakToolTipContent),
                "Selection and ordinary repaints do not re-record stale telemetry after an internal clear or rebuild the tooltip.");
            require(Json(mixer.GraphConfig) == originalGraph && mixer.EditHistory.Entries.Count == originalHistory
                && mixer.PendingEditCount == 0 && !workspace.IsNodeDragging,
                "Hover presentation and internal history clearing never edit audio settings, start a drag, queue a save or create undo history.");
            mixer.RefreshPreviewTelemetry(); await layout();
            require(meter.ClipLatched && meter.RetainedPeakLeft == .5 && meter.RetainedPeakRight == .25,
                "A later fresh clipped telemetry packet can relatch and record peaks after an internal clear.");

            mixer.Edit(c => c.Graph.Nodes.Single(node => node.Id == sourceId).Muted = true); await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched && meter.Level == 0,
                "Muting the node clears its peak history and clip indication.");
            mixer.Edit(c => c.Graph.Nodes.Single(node => node.Id == sourceId).Muted = false);
            status.PeakLeft = .25f; status.PeakRight = .125f; status.Clipped = false;
            mixer.RefreshPreviewTelemetry(); await layout();
            require(meter.RetainedPeakLeft == .25 && meter.RetainedPeakRight == .125 && !meter.ClipLatched,
                "The next unmuted sample starts fresh peak readings without the old clip latch.");
            mixer.Edit(c => c.Graph.Nodes.Single(node => node.Kind == "control").Muted = true); await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched && meter.Level == 0,
                "Mute from a connected volume-control node also clears the target's peak readings.");
            mixer.Edit(c => c.Graph.Nodes.Single(node => node.Kind == "control").Muted = false);
            mixer.RefreshPreviewTelemetry(); await layout();
            require(meter.HasPeakReadings && meter.RetainedPeakLeft == .25,
                "Releasing a linked mute resumes observation on fresh telemetry.");

            meter.ClearPeakReadings(); await layout();
            status.PeakLeft = status.PeakRight = 0; status.Channels = 2;
            mixer.RefreshPreviewTelemetry(); await layout();
            require(display.PeakReadingText == "Left · −∞ dBFS; Right · −∞ dBFS", "Observed stereo silence has two explicit minus-infinity readings.");
            status.Channels = 1; mixer.RefreshPreviewTelemetry(); await layout();
            require(display.PeakReadingText == "Peak · −∞ dBFS"
                && ((TextBlock)display.PeakToolTipContent.Children[2]).Visibility == Visibility.Collapsed,
                "Silent mono telemetry removes the unused right reading even when both retained amplitudes remain zero.");
            RenderToolTip(display, "peak-meter-mono-tooltip.png", require, render);
            status.Channels = 2; mixer.RefreshPreviewTelemetry(); await layout();
            require(display.PeakReadingText == "Left · −∞ dBFS; Right · −∞ dBFS",
                "Returning to silent stereo immediately restores both hover readings.");
            require(view.RenderSize == initialSize && SocketPositions(view).SequenceEqual(sockets),
                "Peak observation, internal clearing, mute and channel changes preserve node size and socket positions.");

            // Start a clean queued fixture to exercise real optimistic source
            // changes, rather than presenting a node that the model never saved.
            fixture = Fixture(); mixer.ApplyPreviewSnapshot(fixture); await layout();
            view = Descendants<GraphNodeView>(workspace).Single(node => node.Id == sourceId);
            display = view.PeakMeterDisplay; meter = display.Meter;
            mixer.QueuePreviewEdits = true;
            mixer.Edit(c => c.Graph.Nodes.Single(node => node.Id == sourceId).Device = new()
                { Id = "preview-mic2", Name = "Second microphone", Channel = -1 });
            await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched && mixer.PendingEditCount == 1,
                "Changing the selected source clears its old peak history while the source edit awaits acknowledgement.");
            mixer.RefreshPreviewTelemetry(); await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched,
                "Old-source telemetry cannot populate the newly selected source before acknowledgement.");
            var batch = mixer.PreparePendingConfiguration(fixture.Configuration);
            var acknowledged = Fixture(); acknowledged.Configuration = batch.Configuration.Copy(); acknowledged.Configuration.Revision++;
            acknowledged.Status.AudioFrames = 100;
            var acknowledgedStatus = acknowledged.Status.Nodes.Single(node => node.Id == sourceId);
            acknowledgedStatus.Path.EndpointId = "preview-mic2";
            mixer.AcknowledgePending(batch, acknowledged); await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched && mixer.PendingEditCount == 0,
                "A source acknowledgement packet is skipped because it can still contain accumulated peaks from the previous source.");
            acknowledgedStatus.PeakLeft = .125f; acknowledgedStatus.PeakRight = .0625f; acknowledgedStatus.Clipped = false;
            mixer.RefreshPreviewTelemetry(); await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched,
                "Repeated matching-source packets at the acknowledgement's audio frame cannot repopulate cleared history.");
            acknowledged.Status.AudioFrames = 101;
            mixer.RefreshPreviewTelemetry(); await layout();
            require(!meter.HasPeakReadings && !meter.ClipLatched,
                "The first advancing packet drains a possible in-flight old-source block without recording it.");
            acknowledged.Status.AudioFrames = 102;
            mixer.RefreshPreviewTelemetry(); await layout();
            require(meter.HasPeakReadings && meter.RetainedPeakLeft == .125 && meter.RetainedPeakRight == .0625 && !meter.ClipLatched,
                "The following matching source packet starts new history without old maxima or clipping.");
            string afterSourceEdit = Json(mixer.GraphConfig); int sourceHistory = mixer.EditHistory.Entries.Count;
            RenderToolTip(display, "peak-meter-new-source-tooltip.png", require, render);
            require(Json(mixer.GraphConfig) == afterSourceEdit && mixer.EditHistory.Entries.Count == sourceHistory && mixer.PendingEditCount == 0,
                "Reading hover information after source acknowledgement preserves the graph and its existing undo step.");
        }
        finally
        {
            mixer.QueuePreviewEdits = false;
        }
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); await layout();
    }

    private static MixerSnapshot Fixture()
    {
        var snapshot = UiPreview.CreateSnapshot([]);
        var microphone = GraphDefaults.NewNode("microphone", 20, 20); microphone.Id = "peak-source";
        microphone.Device = new() { Id = "preview-mic1", Name = "Studio microphone", Channel = -1 };
        var control = GraphDefaults.NewNode("control", 440, 20); control.Id = "peak-control";
        snapshot.Configuration.Revision = 10;
        snapshot.Configuration.Graph = new() { Nodes = [microphone, control], Edges =
            [new() { Id = "peak-control-link", From = control.Id, To = microphone.Id, Kind = "control" }], Zoom = .75 };
        snapshot.Endpoints.Add(new() { Id = "preview-mic2", Name = "Second microphone", Direction = "capture", State = "Active", Channels = 2, Rate = 48000 });
        snapshot.Status.Nodes = [new() { Id = microphone.Id, Channels = 2, PeakLeft = .5f, PeakRight = .25f, Clipped = true,
            Path = new() { State = "Running", EndpointId = microphone.Device.Id, Channels = 2 } }];
        return snapshot;
    }

    private static void RenderToolTip(MeterPeakDisplay display, string filename, Action<bool, string> require,
        Action<FrameworkElement, string> render)
    {
        var tip = new ToolTip { Content = display.PeakToolTipContent };
        try
        {
            tip.Measure(new Size(326, double.PositiveInfinity));
            tip.Arrange(new Rect(new Point(), tip.DesiredSize)); tip.UpdateLayout();
            require(!tip.IsOpen && PresentationSource.FromVisual(tip) is null && tip.ActualWidth <= 326 && tip.ActualWidth > 40,
                "Peak hover preview uses the real tooltip template at its normal width without opening a popup.");
            RenderNode(tip, filename, render);
        }
        finally { tip.Content = null; }
    }

    private static void RenderNode(FrameworkElement element, string filename, Action<FrameworkElement, string> render)
    {
        // Paint the actual existing control without reparenting it or cloning
        // telemetry, which would erase deliberately latched/cleared history.
        var frame = new Border
        {
            Width = element.ActualWidth + 32, Height = element.ActualHeight + 32, Padding = new Thickness(16),
            Child = new System.Windows.Shapes.Rectangle { Width = element.ActualWidth, Height = element.ActualHeight,
                Fill = new VisualBrush(element) { AutoLayoutContent = false, Stretch = Stretch.None } }
        };
        frame.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
        render(frame, filename);
    }
    private static Point[] SocketPositions(GraphNodeView view) => Descendants<GraphWorkspace.Socket>(view)
        .Select(socket => socket.TranslatePoint(new(socket.ActualWidth / 2, socket.ActualHeight / 2), view)).ToArray();
    private static string Json(GraphConfiguration graph) => JsonSerializer.Serialize(graph, Protocol.Json);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
