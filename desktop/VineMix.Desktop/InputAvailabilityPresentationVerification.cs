using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

// In-memory layout, animation clocks and rendering only. No native windows,
// desktop automation, live audio, device discovery or personal settings.
internal static class InputAvailabilityPresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Input warning checks use a detached preview without desktop automation or live audio.");
        bool previousReduced = mixer.ReducedAnimation;
        var fixture = UiPreview.CreateSnapshot([]);
        var microphone = GraphDefaults.NewNode("microphone", 20, 20);
        microphone.Device = new() { Id = "preview-mic1", Name = "Saved microphone", Channel = -1 };
        var virtualInput = GraphDefaults.NewNode("virtualInput", 420, 20);
        virtualInput.Device = new() { Id = "preview-apps-capture", Name = "Saved VB-CABLE", Channel = -1 };
        var app = GraphDefaults.NewNode("appInput", 820, 20);
        app.App = new() { ProcessId = 100, ExecutablePath = "preview-spotify.exe", Name = "Saved app" };
        var output = GraphDefaults.NewNode("output", 1220, 20);
        fixture.Configuration.Graph = new() { Nodes = [microphone, virtualInput, app, output], Zoom = .75 };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Path = new() { State = "Running" }, Channels = 2, PeakLeft = 0, PeakRight = 0 }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); mixer.ReducedAnimation = false; await layout();
        mixer.Edit(c => c.Graph.Nodes[0].Name = "My microphone with a long saved name"); await layout();
        var views = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id);
        var geometry = views.ToDictionary(pair => pair.Key, pair => Geometry(pair.Value));
        string configuration = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        int history = mixer.EditHistory.Entries.Count, pending = mixer.PendingEditCount;
        GraphNode Node(string id) => mixer.GraphConfig.Nodes.Single(node => node.Id == id);
        GraphNodeStatus Status(string id) => fixture.Status.Nodes.Single(status => status.Id == id);
        var micView = views[microphone.Id]; var virtualView = views[virtualInput.Id]; var appView = views[app.Id];
        require(views.Values.All(view => !view.AvailabilityNoticeVisible && !view.AvailabilityWarningVisible && !view.AvailabilityWarningPulseActive && !view.AvailabilityWarningIconVisible),
            "Healthy silent inputs and outputs have no warning icon, pulse, banner or extra label.");
        foreach (var view in new[] { micView, virtualView, appView }) CheckOriginalLayout(view, require);
        require(Descendants<TextBlock>(virtualView).Any(text => text.Text == "Connected" && text.Visibility == Visibility.Visible),
            "A healthy virtual input retains its original connected status.");
        try
        {
            // Equal inventories intentionally keep their existing objects.
            // Mutate the preview model's retained inventory, not a discarded copy.
            var endpoint = mixer.Endpoints.Single(item => item.Id == microphone.Device.Id);
            Status(microphone.Id).Path.State = "Waiting";
            Status(microphone.Id).Path.Message = "Detailed device diagnostic retained for troubleshooting.";
            foreach (string state in new[] { "Missing", "Disabled", "Unplugged" })
            {
                endpoint.State = state; mixer.RefreshPreviewTelemetry(); await layout();
                require(micView.AvailabilityNoticeLabel == "INPUT " + state.ToUpperInvariant() && micView.AvailabilityWarningVisible
                    && Descendants<ComboBox>(micView).Single().IsEnabled,
                    state + " highlights the selected microphone while its chooser remains usable.");
                CheckWarning(micView, require); CheckPulse(micView, require);
                require(micView.AvailabilityDeviceElement!.ToolTip is StackPanel tip
                    && tip.Children.OfType<TextBlock>().Any(text => text.Text == Status(microphone.Id).Path.Message),
                    "Hover help retains the engine's precise diagnosis without adding fine print.");
                render(micView, "input-state-" + state.ToLowerInvariant() + ".png");
            }
            micView.SetSelected(true); await layout();
            require(micView.SelectionOutlineVisible && micView.AvailabilityWarningVisible
                && Math.Abs(micView.SelectionOutlineScreenThickness - 2) < .001 && Math.Abs(micView.AvailabilityWarningScreenRadius - 10) < .001,
                "Selection remains distinct from the small outer glow at 75 percent zoom.");
            render(micView, "input-unplugged-selected.png");
            var help = micView.AvailabilityDeviceElement!.ToolTip;
            var pulse = micView.AvailabilityWarningPulseIdentity;
            for (int tick = 0; tick < 20; tick++) mixer.RefreshPreviewTelemetry();
            require(ReferenceEquals(help, micView.AvailabilityDeviceElement.ToolTip) && ReferenceEquals(pulse, micView.AvailabilityWarningPulseIdentity),
                "Meter refreshes reuse warning help and keep the same animation clock instead of restarting the pulse.");
            render(micView, "input-device-pulse-red.png");
            render(micView, "input-device-pulse-normal.png");
            mixer.ReducedAnimation = true; mixer.RefreshPreviewTelemetry(); await layout();
            micView.SeekAvailabilityWarningPulse(0); var steady = SourceColor(micView);
            micView.SeekAvailabilityWarningPulse(.8);
            require(!micView.AvailabilityWarningPulseActive && SameColor(steady, SourceColor(micView)) && SameColor(steady, Palette("ConsoleMeterHighBrush")),
                "Reduced animation keeps the selected input steadily red without a flashing clock.");
            render(micView, "input-warning-reduced-animation.png"); mixer.ReducedAnimation = false;

            var muted = Node(microphone.Id).Copy(); muted.Muted = true;
            micView.Apply(muted, Status(microphone.Id), selected: true); await layout();
            require(micView.AvailabilityNoticeLabel == "INPUT UNPLUGGED" && micView.AvailabilityWarningVisible,
                "A disconnected input remains highlighted when it is also muted.");
            Status(microphone.Id).Path.State = "Running";
            micView.Apply(muted, Status(microphone.Id), selected: true); await layout();
            require(micView.AvailabilityNoticeLabel == "MUTED" && !micView.AvailabilityWarningVisible && !micView.AvailabilityWarningPulseActive && !micView.AvailabilityWarningIconVisible
                && SameColor(SourceColor(micView), Palette("ConsoleTextBrush")),
                "An intentionally muted healthy input has normal source text and no error glow or pulse.");
            CheckOriginalLayout(micView, require); RenderAs(micView, muted, "input-state-muted.png");
            mixer.RefreshPreviewTelemetry(); await layout();
            require(!micView.AvailabilityWarningVisible && !micView.AvailabilityWarningPulseActive && !micView.AvailabilityWarningIconVisible && SameColor(SourceColor(micView), Palette("ConsoleTextBrush")),
                "Recovery removes the warning icon, animation and glow immediately, even when inventory still lags.");
            render(micView, "input-state-recovered.png");

            var unassigned = Node(virtualInput.Id).Copy(); unassigned.Device = new();
            mixer.Endpoints.RemoveAll(item => item.Direction == "capture" && item.Transport == "vbCable");
            virtualView.Apply(unassigned, new GraphNodeStatus { Path = new() { State = "Unassigned" } }, selected: false); await layout();
            require(virtualView.AvailabilityNoticeLabel == "VB-CABLE NOT DETECTED" && virtualView.AvailabilityWarningVisible,
                "An undetected virtual input highlights its existing device-name text.");
            CheckWarning(virtualView, require); CheckPulse(virtualView, require);
            RenderAs(virtualView, unassigned, "input-vb-cable-not-detected.png");
            Status(app.Id).Path.State = "Unsupported";
            appView.Apply(Node(app.Id), Status(app.Id), selected: false); await layout();
            require(appView.AvailabilityNoticeLabel == "APP CAPTURE UNAVAILABLE" && appView.AvailabilityWarningVisible,
                "Unavailable app capture highlights its selected application without adding a banner.");
            CheckWarning(appView, require); CheckPulse(appView, require); render(appView, "input-app-unavailable.png");
            try
            {
                ConsoleTheme.SetPreviewHighContrast(true);
                virtualView.Apply(unassigned, new GraphNodeStatus { Path = new() { State = "Unassigned" } }, selected: true);
                await layout(); virtualView.SeekAvailabilityWarningPulse(0);
                require(SameColor(SourceColor(virtualView), Palette("ConsoleMeterHighBrush")) && virtualView.AvailabilityWarningVisible,
                    "High contrast uses system colours and retains the diagnosis in accessible help.");
                CheckWarning(virtualView, require); RenderAs(virtualView, unassigned, "input-warning-high-contrast.png");
            }
            finally { ConsoleTheme.SetPreviewHighContrast(null); }
            using (var offline = new MixerViewModel(preview: true))
            {
                var offlineWorkspace = new GraphWorkspace(offline);
                var offlineView = new GraphNodeView(offlineWorkspace, microphone.Copy());
                offlineView.Apply(microphone.Copy(), new() { Path = new() { State = "Running" } }, selected: true);
                require(offlineView.AvailabilityNoticeLabel == "ENGINE OFFLINE" && offlineView.AvailabilityWarningVisible
                    && !Descendants<ComboBox>(offlineView).Single().IsEnabled
                    && ToolTipService.GetShowOnDisabled(offlineView.AvailabilityDeviceElement!)
                    && offlineView.AvailabilityDeviceElement!.ToolTip is StackPanel offlineHelp
                    && Descendants<TextBlock>(offlineHelp).Any(text => text.Text.Contains("ENGINE OFFLINE", StringComparison.Ordinal)),
                    "Engine loss preserves disabled controls and exposes its actual reason in help.");
            }
            await layout();
            require(views.All(pair => Same(geometry[pair.Key], Geometry(pair.Value))), "Source warnings never change node dimensions or socket positions.");
            require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == configuration
                && mixer.EditHistory.Entries.Count == history && mixer.PendingEditCount == pending,
                "Warning display and animation never edit the graph, queue a save or change undo history.");
        }
        finally { mixer.ReducedAnimation = previousReduced; }
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); await layout();

        void RenderAs(GraphNodeView view, GraphNode presentationNode, string name)
        {
            // The shared renderer reconstructs the control from this inert model.
            int index = mixer.GraphConfig.Nodes.FindIndex(node => node.Id == view.Id);
            var original = mixer.GraphConfig.Nodes[index]; mixer.GraphConfig.Nodes[index] = presentationNode.Copy();
            try { view.SeekAvailabilityWarningPulse(0); render(view, name); }
            finally { mixer.GraphConfig.Nodes[index] = original; }
        }
    }

    private static void CheckOriginalLayout(GraphNodeView view, Action<bool, string> require)
    {
        var audio = Descendants<TextBlock>(view).Single(text => text.Text == "Audio");
        var row = (Grid)VisualTreeHelper.GetParent(audio);
        require(!view.AvailabilityNoticeVisible && row.Height == 28 && row.Children.Count == 1 && row.Background is null
            && !Descendants<TextBlock>(view).Any(text => text.Visibility == Visibility.Visible && text.Text.Length > 0
                && (text.Text == view.AvailabilityNoticeLabel || text.Text == view.AvailabilityNoticeDetail)),
            "The original Audio row has no warning band, large state label or added recovery line.");
        require(Descendants<UIElement>(view).Prepend(view).All(element => element.Effect is null && element.CacheMode is not BitmapCache),
            "Vector warning drawing never rasterizes node controls or text through effects or bitmap caches.");
    }
    private static void CheckWarning(GraphNodeView view, Action<bool, string> require)
    {
        CheckOriginalLayout(view, require);
        var text = view.AvailabilityDeviceTextElement;
        require(text is not null && text.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(text.Text)
            && view.AvailabilityNoticeDetailTextElement.Visibility == Visibility.Hidden
            && !view.AvailabilityGlowElement.IsHitTestVisible && view.AvailabilityGlowElement.Effect is null,
            "Selected source text carries the warning; the small status line stays hidden and the glow cannot intercept input.");
        var bounds = VisualTreeHelper.GetContentBounds(view.AvailabilityGlowElement);
        require(bounds.Left < 0 && bounds.Top < 0 && bounds.Right > view.AvailabilityGlowElement.ActualWidth && bounds.Bottom > view.AvailabilityGlowElement.ActualHeight,
            "The glow extends outside the node without adding a heavy inner outline or layout space.");
        var icon = view.AvailabilityWarningIconElement;
        var meter = Descendants<StereoNodeMeter>(view).Single();
        var iconCentre = icon.TranslatePoint(new(icon.ActualWidth / 2, icon.ActualHeight), view);
        var meterCentre = meter.TranslatePoint(new(meter.ActualWidth / 2, 0), view);
        require(view.AvailabilityWarningIconVisible && icon.ActualWidth > 0 && icon.ActualHeight > 0
            && iconCentre.Y < meterCentre.Y && Math.Abs(iconCentre.X - meterCentre.X) < 1
            && !icon.IsHitTestVisible && !icon.Focusable
            && icon.Fill is SolidColorBrush red && SameColor(red.Color, Palette("ConsoleMeterHighBrush")),
            "A steady red warning triangle sits above the volume meter without covering its bars or intercepting controls.");
        require(AutomationProperties.GetHelpText(view.AvailabilityDeviceElement!).Contains(view.AvailabilityNoticeLabel, StringComparison.Ordinal)
            && AutomationProperties.GetHelpText(view.AvailabilityDeviceElement!).Contains(view.AvailabilityNoticeDetail, StringComparison.Ordinal),
            "Accessible source help retains the availability reason and recovery action without extra visible text.");
    }
    private static void CheckPulse(GraphNodeView view, Action<bool, string> require)
    {
        view.SeekAvailabilityWarningPulse(0); Color red = SourceColor(view);
        view.SeekAvailabilityWarningPulse(.8); Color normal = SourceColor(view);
        view.SeekAvailabilityWarningPulse(1.6); Color repeated = SourceColor(view);
        require(SameColor(red, Palette("ConsoleMeterHighBrush")) && SameColor(red, repeated)
            && (SystemParameters.ClientAreaAnimation ? view.AvailabilityWarningPulseActive && !SameColor(red, normal) && SameColor(normal, Palette("ConsoleTextBrush"))
                : !view.AvailabilityWarningPulseActive && SameColor(red, normal)),
            "The real selected-text animation gently cycles red to normal in 1.6 seconds and respects Windows motion preferences.");
        view.SeekAvailabilityWarningPulse(0);
        require(view.AvailabilityDeviceTextElement?.Foreground is SolidColorBrush glyph && SameColor(glyph.Color, SourceColor(view)),
            "The selected text glyph inherits the animated brush rather than only changing the surrounding control.");
    }
    private static Color Palette(string key) => ((SolidColorBrush)ConsoleTheme.Get(key)).Color;
    private static Color SourceColor(GraphNodeView view) => ((SolidColorBrush)(view.AvailabilityDeviceElement switch
    {
        Control control => control.Foreground,
        TextBlock text => text.Foreground,
        _ => throw new InvalidOperationException("The input source has no text foreground.")
    })).Color;
    private static bool SameColor(Color left, Color right) => Math.Abs(left.R - right.R) <= 2 && Math.Abs(left.G - right.G) <= 2
        && Math.Abs(left.B - right.B) <= 2 && Math.Abs(left.A - right.A) <= 2;
    private sealed record NodeGeometry(Size Size, IReadOnlyList<Point> Sockets);
    private static NodeGeometry Geometry(GraphNodeView view) => new(view.RenderSize,
        Descendants<GraphWorkspace.Socket>(view).Select(socket => socket.TranslatePoint(new(socket.ActualWidth / 2, socket.ActualHeight / 2), view)).ToArray());
    private static bool Same(NodeGeometry left, NodeGeometry right) => left.Size == right.Size && left.Sockets.Count == right.Sockets.Count
        && left.Sockets.Zip(right.Sockets).All(pair => (pair.First - pair.Second).Length < .001);
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
