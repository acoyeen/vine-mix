using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

// Detached preview controls and animation clocks only; no device discovery,
// native windows, live audio or saved personal configuration.
internal static class OutputAvailabilityPresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Output warning checks use a detached preview without live audio or desktop automation.");
        bool previousReduced = mixer.ReducedAnimation;
        var fixture = UiPreview.CreateSnapshot([]);
        var output = GraphDefaults.NewNode("output", 20, 20);
        output.Muted = false;
        output.Device = new() { Id = "preview-a1", Name = "Saved listening output", Channel = -1 };
        var virtualOutput = GraphDefaults.NewNode("virtualOutput", 420, 20);
        virtualOutput.Muted = false;
        virtualOutput.Device = new() { Id = "preview-send-play", Name = "Saved VAC Lite", Channel = -1 };
        fixture.Configuration.Graph = new() { Nodes = [output, virtualOutput], Zoom = .75 };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Path = new() { State = "Running" }, Channels = 2, PeakLeft = 0, PeakRight = 0 }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); mixer.ReducedAnimation = false; await layout();
        mixer.Edit(c => c.Graph.Nodes[0].Name = "My saved listening output"); await layout();
        var views = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id);
        var outputView = views[output.Id]; var virtualView = views[virtualOutput.Id];
        string virtualDeviceName = virtualView.AvailabilityDeviceTextElement!.Text;
        var geometry = views.ToDictionary(pair => pair.Key, pair => Geometry(pair.Value));
        string configuration = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        int history = mixer.EditHistory.Entries.Count, pending = mixer.PendingEditCount;
        GraphNode Node(string id) => mixer.GraphConfig.Nodes.Single(node => node.Id == id);
        GraphNodeStatus Status(string id) => fixture.Status.Nodes.Single(status => status.Id == id);
        var physicalEndpoint = mixer.Endpoints.Single(endpoint => endpoint.Id == output.Device.Id);
        var virtualEndpoint = mixer.Endpoints.Single(endpoint => endpoint.Id == virtualOutput.Device.Id);
        require(views.Values.All(NoWarning),
            "Healthy silent physical and virtual outputs have normal device text and no warning icon, glow or pulse.");
        require(Descendants<TextBlock>(virtualView).Any(text => text.Text == "Connected" && text.Visibility == Visibility.Visible),
            "A healthy virtual output retains its connected status.");
        try
        {
            Status(output.Id).Path.State = "Waiting";
            Status(output.Id).Path.Message = "Detailed playback diagnostic retained for troubleshooting.";
            foreach (string state in new[] { "Missing", "Disabled", "Unplugged" })
            {
                physicalEndpoint.State = state; mixer.RefreshPreviewTelemetry(); await layout();
                require(outputView.AvailabilityNoticeLabel == "OUTPUT " + state.ToUpperInvariant()
                    && Descendants<ComboBox>(outputView).Single().IsEnabled,
                    state + " highlights the saved listening output while its device chooser remains usable.");
                CheckWarning(outputView, require); CheckPulse(outputView, require);
                require(outputView.AvailabilityDeviceElement!.ToolTip is StackPanel help
                    && Descendants<TextBlock>(help).Any(text => text.Text == Status(output.Id).Path.Message),
                    "Output hover help preserves the worker's actual diagnostic.");
                if (state == "Missing")
                {
                    render(outputView, "output-device-pulse-red.png");
                    render(outputView, "output-device-pulse-normal.png");
                }
            }
            outputView.SetSelected(true); await layout();
            require(outputView.SelectionOutlineVisible && outputView.AvailabilityWarningVisible
                && Math.Abs(outputView.SelectionOutlineScreenThickness - 2) < .001
                && Math.Abs(outputView.AvailabilityWarningScreenRadius - 10) < .001,
                "The selected output retains its selection outline alongside the small warning glow at 75 percent zoom.");
            render(outputView, "output-unplugged-selected.png");
            var tooltip = outputView.AvailabilityDeviceElement!.ToolTip;
            var clock = outputView.AvailabilityWarningPulseIdentity;
            for (int tick = 0; tick < 20; tick++) mixer.RefreshPreviewTelemetry();
            require(ReferenceEquals(tooltip, outputView.AvailabilityDeviceElement.ToolTip)
                && ReferenceEquals(clock, outputView.AvailabilityWarningPulseIdentity),
                "Repeated output telemetry reuses its help and pulse clock without restarting the flash.");

            mixer.ReducedAnimation = true; mixer.RefreshPreviewTelemetry(); await layout();
            outputView.SeekAvailabilityWarningPulse(0); Color steady = DeviceColor(outputView);
            outputView.SeekAvailabilityWarningPulse(.8);
            require(!outputView.AvailabilityWarningPulseActive && SameColor(steady, DeviceColor(outputView))
                && SameColor(steady, Palette("ConsoleMeterHighBrush")),
                "Reduced animation keeps missing output text steadily red without a flashing clock.");
            render(outputView, "output-warning-reduced-animation.png"); mixer.ReducedAnimation = false;

            var muted = Node(output.Id).Copy(); muted.Muted = true;
            outputView.Apply(muted, Status(output.Id), selected: true); await layout();
            require(outputView.AvailabilityNoticeLabel == "OUTPUT UNPLUGGED" && outputView.AvailabilityWarningVisible,
                "Muting a missing output preserves its device warning.");
            Status(output.Id).Path.State = "Running";
            outputView.Apply(muted, Status(output.Id), selected: true); await layout();
            require(outputView.AvailabilityNoticeLabel == "MUTED" && NoWarning(outputView)
                && SameColor(DeviceColor(outputView), Palette("ConsoleTextBrush")),
                "A healthy muted output has normal text and no device warning even while inventory still says unplugged.");
            mixer.RefreshPreviewTelemetry(); await layout();
            require(NoWarning(outputView) && SameColor(DeviceColor(outputView), Palette("ConsoleTextBrush")),
                "Running playback immediately clears the output icon, glow and flash before endpoint inventory catches up.");
            render(outputView, "output-state-recovered.png");

            var unassignedOutput = Node(output.Id).Copy(); unassignedOutput.Device = new();
            outputView.Apply(unassignedOutput, new() { Path = new() { State = "Unassigned" } }, selected: false); await layout();
            require(outputView.AvailabilityNoticeLabel == "CHOOSE OUTPUT DEVICE" && NoWarning(outputView),
                "A new unassigned listening output requests a device without flashing a missing-device warning.");
            outputView.Apply(Node(output.Id), null, selected: false); await layout();
            require(NoWarning(outputView), "An assigned output without its first telemetry sample does not flash an error.");
            foreach (string state in new[] { "Resolving", "Opening", "Priming", "Unassigned", "Recovering" })
            {
                outputView.Apply(Node(output.Id), new() { Path = new() { State = state } }, selected: false); await layout();
                require(NoWarning(outputView), state + " output playback is pending rather than a device failure.");
            }
            mixer.RefreshPreviewTelemetry(); await layout();

            outputView.Apply(Node(output.Id), new() { Path = new()
                { State = "AccessDenied", Message = "Microphone access blocked; check Windows microphone privacy settings" } }, selected: false);
            require(outputView.AvailabilityDeviceElement!.ToolTip is StackPanel accessHelp
                && Descendants<TextBlock>(accessHelp).Any(text => text.Text == "Output access blocked; check Windows sound device settings")
                && !Descendants<TextBlock>(accessHelp).Any(text => text.Text.Contains("Microphone", StringComparison.Ordinal)),
                "Output access help replaces the older engine's capture-specific privacy wording with playback guidance.");
            mixer.RefreshPreviewTelemetry(); await layout();

            virtualEndpoint.State = "Missing"; Status(virtualOutput.Id).Path.State = "Waiting";
            mixer.RefreshPreviewTelemetry(); await layout();
            require(virtualView.AvailabilityNoticeLabel == "OUTPUT MISSING"
                && virtualView.AvailabilityDeviceTextElement?.Text == virtualDeviceName
                && Node(virtualOutput.Id).Device.Id == virtualOutput.Device.Id,
                "An assigned missing VAC Lite output warns on its existing device name without losing the assignment.");
            CheckWarning(virtualView, require); CheckPulse(virtualView, require);
            render(virtualView, "output-vac-missing.png");
            virtualEndpoint.State = "Active";
            var unassignedVirtual = Node(virtualOutput.Id).Copy(); unassignedVirtual.Device = new();
            virtualView.Apply(unassignedVirtual, new() { Path = new() { State = "Unassigned" } }, selected: false); await layout();
            require(virtualView.AvailabilityNoticeLabel == "CHOOSE VAC LITE" && NoWarning(virtualView),
                "An unassigned virtual output with an available VAC Lite endpoint requests setup without an error pulse.");
            mixer.Endpoints.RemoveAll(endpoint => endpoint.Direction == "render" && endpoint.Transport == "vacLite");
            virtualView.Apply(unassignedVirtual, new() { Path = new() { State = "Unassigned" } }, selected: false); await layout();
            require(virtualView.AvailabilityNoticeLabel == "VAC LITE NOT DETECTED" && virtualView.AvailabilityWarningVisible,
                "An absent VAC Lite playback endpoint warns even when its recording counterpart is still listed.");
            CheckWarning(virtualView, require); RenderAs(virtualView, unassignedVirtual, "output-vac-not-detected.png");
            try
            {
                ConsoleTheme.SetPreviewHighContrast(true);
                virtualView.Apply(unassignedVirtual, new() { Path = new() { State = "Unassigned" } }, selected: true);
                await layout(); virtualView.SeekAvailabilityWarningPulse(0);
                require(SameColor(DeviceColor(virtualView), Palette("ConsoleMeterHighBrush")) && virtualView.AvailabilityWarningVisible,
                    "High contrast retains the output warning with the system warning colour and accessible diagnosis.");
                CheckWarning(virtualView, require); RenderAs(virtualView, unassignedVirtual, "output-warning-high-contrast.png");
            }
            finally { ConsoleTheme.SetPreviewHighContrast(null); }

            using (var offline = new MixerViewModel(preview: true))
            {
                var offlineWorkspace = new GraphWorkspace(offline);
                var offlineView = new GraphNodeView(offlineWorkspace, output.Copy());
                offlineView.Apply(output.Copy(), new() { Path = new() { State = "Running" } }, selected: true);
                require(offlineView.AvailabilityNoticeLabel == "ENGINE OFFLINE" && offlineView.AvailabilityWarningVisible
                    && !Descendants<ComboBox>(offlineView).Single().IsEnabled
                    && ToolTipService.GetShowOnDisabled(offlineView.AvailabilityDeviceElement!)
                    && offlineView.AvailabilityDeviceElement!.ToolTip is StackPanel offlineHelp
                    && Descendants<TextBlock>(offlineHelp).Any(text => text.Text.Contains("ENGINE OFFLINE", StringComparison.Ordinal)),
                    "An offline output keeps its disabled chooser and exposes the engine-offline reason in hover help.");
            }
            await layout();
            require(views.All(pair => Same(geometry[pair.Key], Geometry(pair.Value))),
                "Output warnings, recovery, muting and accessibility preferences leave node dimensions, controls and sockets fixed.");
            require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == configuration
                && mixer.EditHistory.Entries.Count == history && mixer.PendingEditCount == pending,
                "Output availability display and flashing never change graph configuration, undo history or queued edits.");
        }
        finally { mixer.ReducedAnimation = previousReduced; }
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); await layout();

        void RenderAs(GraphNodeView view, GraphNode presentationNode, string name)
        {
            // The shared renderer reconstructs a detached control from this inert model.
            int index = mixer.GraphConfig.Nodes.FindIndex(node => node.Id == view.Id);
            var original = mixer.GraphConfig.Nodes[index]; mixer.GraphConfig.Nodes[index] = presentationNode.Copy();
            try { view.SeekAvailabilityWarningPulse(0); render(view, name); }
            finally { mixer.GraphConfig.Nodes[index] = original; }
        }
    }

    private static bool NoWarning(GraphNodeView view) => !view.AvailabilityNoticeVisible && !view.AvailabilityWarningVisible
        && !view.AvailabilityWarningIconVisible && !view.AvailabilityWarningPulseActive;

    private static void CheckWarning(GraphNodeView view, Action<bool, string> require)
    {
        var source = view.AvailabilityDeviceElement!;
        var text = view.AvailabilityDeviceTextElement;
        var icon = view.AvailabilityWarningIconElement;
        var meter = Descendants<StereoNodeMeter>(view).Single();
        var iconBottom = icon.TranslatePoint(new(icon.ActualWidth / 2, icon.ActualHeight), view);
        var meterTop = meter.TranslatePoint(new(meter.ActualWidth / 2, 0), view);
        require(view.AvailabilityWarningVisible && !view.AvailabilityNoticeVisible && text is not null
            && text.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(text.Text)
            && view.AvailabilityNoticeDetailTextElement.Visibility == Visibility.Hidden,
            "Missing output feedback uses the existing device text, leaving the detail line hidden and no warning banner.");
        require(view.AvailabilityWarningIconVisible && icon.ActualWidth > 0 && icon.ActualHeight > 0
            && iconBottom.Y < meterTop.Y && Math.Abs(iconBottom.X - meterTop.X) < 1
            && !icon.IsHitTestVisible && !icon.Focusable
            && icon.Fill is SolidColorBrush red && SameColor(red.Color, Palette("ConsoleMeterHighBrush")),
            "A steady red warning triangle sits above the output meter without covering bars or intercepting controls.");
        var glow = view.AvailabilityGlowElement;
        var bounds = VisualTreeHelper.GetContentBounds(glow);
        require(!glow.IsHitTestVisible && glow.Effect is null && bounds.Left < 0 && bounds.Top < 0
            && bounds.Right > glow.ActualWidth && bounds.Bottom > glow.ActualHeight
            && Descendants<UIElement>(view).Prepend(view).All(element => element.Effect is null && element.CacheMode is not BitmapCache),
            "The output's vector red glow extends outside its bounds without intercepting controls or rasterizing text.");
        require(AutomationProperties.GetHelpText(source).Contains(view.AvailabilityNoticeLabel, StringComparison.Ordinal)
            && AutomationProperties.GetHelpText(source).Contains(view.AvailabilityNoticeDetail, StringComparison.Ordinal),
            "Accessible output help includes both the device diagnosis and recovery action.");
    }

    private static void CheckPulse(GraphNodeView view, Action<bool, string> require)
    {
        view.SeekAvailabilityWarningPulse(0); Color red = DeviceColor(view);
        view.SeekAvailabilityWarningPulse(.8); Color normal = DeviceColor(view);
        view.SeekAvailabilityWarningPulse(1.6); Color repeated = DeviceColor(view);
        require(SameColor(red, Palette("ConsoleMeterHighBrush")) && SameColor(red, repeated)
            && (SystemParameters.ClientAreaAnimation ? view.AvailabilityWarningPulseActive
                && !SameColor(red, normal) && SameColor(normal, Palette("ConsoleTextBrush"))
                : !view.AvailabilityWarningPulseActive && SameColor(red, normal)),
            "The selected output text cycles red to normal in 1.6 seconds and respects Windows animation preferences.");
        view.SeekAvailabilityWarningPulse(0);
        require(view.AvailabilityDeviceTextElement?.Foreground is SolidColorBrush glyph && SameColor(glyph.Color, DeviceColor(view)),
            "The output's selected text glyphs inherit the flash rather than only recolouring its surrounding control.");
    }

    private static Color Palette(string key) => ((SolidColorBrush)ConsoleTheme.Get(key)).Color;
    private static Color DeviceColor(GraphNodeView view) => ((SolidColorBrush)(view.AvailabilityDeviceElement switch
    {
        Control control => control.Foreground,
        TextBlock text => text.Foreground,
        _ => throw new InvalidOperationException("The output device has no text foreground.")
    })).Color;
    private static bool SameColor(Color left, Color right) => Math.Abs(left.R - right.R) <= 2 && Math.Abs(left.G - right.G) <= 2
        && Math.Abs(left.B - right.B) <= 2 && Math.Abs(left.A - right.A) <= 2;
    private sealed record NodeGeometry(Size Size, IReadOnlyList<Point> Sockets, Point Device, Point Meter);
    private static NodeGeometry Geometry(GraphNodeView view) => new(view.RenderSize,
        Descendants<GraphWorkspace.Socket>(view).Select(socket => socket.TranslatePoint(new(socket.ActualWidth / 2, socket.ActualHeight / 2), view)).ToArray(),
        view.AvailabilityDeviceElement!.TranslatePoint(new(0, 0), view), view.PeakMeterDisplay.TranslatePoint(new(0, 0), view));
    private static bool Same(NodeGeometry left, NodeGeometry right) => left.Size == right.Size
        && (left.Device - right.Device).Length < .001 && (left.Meter - right.Meter).Length < .001
        && left.Sockets.Count == right.Sockets.Count && left.Sockets.Zip(right.Sockets).All(pair => (pair.First - pair.Second).Length < .001);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
