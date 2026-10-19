using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

// In-memory visual and interaction checks only. Menus are constructed but never
// opened; no presentation source, endpoint, clipboard or live settings are used.
internal static class NodeRefinementVerification
{
    internal static FrameworkElement CheckMeterZones(Action<bool, string> require)
    {
        var root = new Canvas { Width = 272, Height = 260, Background = Brushes.Black };
        var meters = new List<GraphNodeView.NodeMeter>();
        string[] names = ["Silent", "Green", "Yellow", "Red"];
        double[] levels = [0, Math.Pow(10, -18 / 20d), Math.Pow(10, -6 / 20d), Math.Pow(10, -1 / 20d)];
        for (int index = 0; index < levels.Length; ++index)
        {
            var meter = new GraphNodeView.NodeMeter { Width = 28, Height = 210 };
            meter.Update(levels[index], levels[index] / 2, 2, false, false, false, 0);
            meters.Add(meter); root.Children.Add(meter);
            Canvas.SetLeft(meter, index * 66 + 18); Canvas.SetTop(meter, 32);
            var label = new TextBlock { Text = names[index], Foreground = ConsoleTheme.Get("ConsoleTextBrush"), FontSize = 12 };
            root.Children.Add(label); Canvas.SetLeft(label, index * 66 + 12); Canvas.SetTop(label, 10);
        }
        root.Measure(new Size(root.Width, root.Height)); root.Arrange(new Rect(0, 0, root.Width, root.Height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)root.Width, (int)root.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        Color Pixel(int index, int channel, double db)
        {
            var lane = meters[index].GetLaneBounds()[channel];
            var point = meters[index].TranslatePoint(new Point(lane.Left + lane.Width / 2, lane.Bottom - (db + 60) / 60 * lane.Height), root);
            var bytes = new byte[4];
            bitmap.CopyPixels(new Int32Rect((int)Math.Floor(point.X), (int)Math.Floor(point.Y), 1, 1), bytes, 4, 0);
            return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
        }
        static bool Near(Color first, Color second) => Math.Abs(first.R - second.R) <= 2
            && Math.Abs(first.G - second.G) <= 2 && Math.Abs(first.B - second.B) <= 2;
        var yellow = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMeterMidBrush")).Color;
        var red = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMeterHighBrush")).Color;
        var green = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMeterLowBrush")).Color;
        require(yellow.R > yellow.B * 2 && yellow.G > yellow.B * 2 && red.R > red.G * 1.5,
            "Meter warning and peak zones resolve to yellow and red, distinct from the green normal range.");
        var fadedYellow = Pixel(0, 0, -9); var fadedRed = Pixel(0, 0, -2);
        require(fadedYellow.R > fadedYellow.B && fadedYellow.G > fadedYellow.B && fadedYellow.R < yellow.R / 2
            && fadedRed.R > fadedRed.G && fadedRed.R < red.R / 2,
            "Actual silent-meter pixels retain faded yellow and red zones rather than an empty black scale.");
        require(Near(Pixel(1, 0, -30), green) && Near(Pixel(1, 0, -9), fadedYellow)
            && Near(Pixel(2, 0, -9), yellow) && Near(Pixel(2, 0, -2), fadedRed) && Near(Pixel(3, 0, -2), red),
            "Rendered fill lights green, then yellow at -12 dB, then red at -3 dB, leaving unreached areas faded.");
        require(Near(Pixel(3, 1, -2), fadedRed) && Near(Pixel(3, 0, -2), red),
            "Stereo meter zones fill independently; a loud left channel does not illuminate the quieter right channel's red zone.");
        require(meters.All(meter => !meter.IsAnimationRunning) && PresentationSource.FromVisual(root) is null,
            "Colour verification uses detached pixel rendering without timers or native windows.");
        return root;
    }

    internal static async Task RunAsync(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<GraphNodeView, string> renderNode)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        string[] kinds = ["eq", "tone", "compressor", "pan", "mono", "highPass", "lowPass", "mix", "gain", "reroute", "control", "denoiser", "deEsser", "plosive", "reverb"];
        var nodes = kinds.Select((kind, index) => GraphDefaults.NewNode(kind, (index % 3) * 680, (index / 3) * 760)).ToList();
        var source = GraphDefaults.NewNode("microphone", -400, 0); nodes.Add(source);
        var edge = new GraphEdge { Id = "legacy-weight", From = source.Id, To = nodes[0].Id, GainDb = -9, Muted = true };
        fixture.Configuration.Graph = new() { Nodes = nodes, Edges = [edge], Zoom = 1 };
        fixture.Status.Nodes = nodes.Select(node => new GraphNodeStatus { Id = node.Id, PeakLeft = .8f, PeakRight = .3f, Channels = 2,
            Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        require(GraphDefaults.Validate(mixer.GraphConfig).Length == 0, "The node library creates valid standalone High-pass and Low-pass nodes alongside all existing effects.");
        foreach (string kind in kinds)
        {
            var model = mixer.GraphConfig.Nodes.Single(node => node.Kind == kind);
            var view = Descendants<GraphNodeView>(root).Single(node => node.Id == model.Id);
            var toggle = Descendants<Button>(view).Single(button => AutomationProperties.GetName(button) == "Bypass");
            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(view).TransformBounds(new Rect(new Point(), element.RenderSize));
            var options = Descendants<FrameworkElement>(view).Where(element => element is RotaryValueControl or EqualizerEditor or GraphWorkspace.Socket).ToArray();
            require(toggle.ActualHeight >= 36 && toggle.ActualWidth >= view.ActualWidth - 30
                && options.All(option => Bounds(option).Bottom <= Bounds(toggle).Top + 1)
                && Equals(toggle.Content, "Bypass") && AutomationProperties.GetItemStatus(toggle) == "Off"
                && ReferenceEquals(toggle.Background, ConsoleTheme.Get("ConsoleButtonFaceBrush"))
                && !Descendants<Button>(view).Any(button => AutomationProperties.GetName(button) is "Mute" or "Effect"),
                kind + " has one grey full-width Bypass button below all controls and sockets, with no duplicate mute or effect toggle.");
            long presentations = mixer.ConfigurationPresentationCount;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(mixer.GraphConfig.Nodes.Single(node => node.Id == model.Id).Bypass
                && Equals(toggle.Content, "Bypass") && AutomationProperties.GetItemStatus(toggle) == "On"
                && ReferenceEquals(toggle.Background, ConsoleTheme.Get("ConsoleMuteBrush"))
                && mixer.ConfigurationPresentationCount == presentations + 1,
                kind + " footer enables bypass with one in-memory edit, turns red and keeps the label Bypass.");
            if (kind == "highPass") { await layout(); renderNode(view, "high-pass-bypassed-closeup.png"); }
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(!mixer.GraphConfig.Nodes.Single(node => node.Id == model.Id).Bypass
                && Equals(toggle.Content, "Bypass") && AutomationProperties.GetItemStatus(toggle) == "Off"
                && ReferenceEquals(toggle.Background, ConsoleTheme.Get("ConsoleButtonFaceBrush")),
                kind + " footer restores processing and returns to grey without changing its label.");
            foreach (bool alreadyBypassed in new[] { false, true })
            {
                workspace.NodeChanged(model.Id, node => { node.Muted = true; node.Bypass = alreadyBypassed; });
                require(mixer.GraphConfig.Nodes.Single(node => node.Id == model.Id).Muted
                    && Equals(toggle.Content, "Bypass") && AutomationProperties.GetItemStatus(toggle) == "Legacy muted"
                    && Descendants<TextBlock>(view).Any(text => text.Visibility == Visibility.Visible && text.Text == "Muted · click Bypass to restore audio"),
                    kind + " visibly identifies a saved legacy mute without silently restoring its audio or introducing a second toggle.");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var restored = mixer.GraphConfig.Nodes.Single(node => node.Id == model.Id);
                require(!restored.Muted && restored.Bypass && AutomationProperties.GetItemStatus(toggle) == "On",
                    kind + " explicitly restores a legacy-muted node as bypassed, even when its old bypass flag was already set.");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                require(!mixer.GraphConfig.Nodes.Single(node => node.Id == model.Id).Bypass
                    && !mixer.GraphConfig.Nodes.Single(node => node.Id == model.Id).Muted,
                    kind + " resumes normal processing after the legacy mute has been cleared.");
            }
            if (kind is "highPass" or "lowPass")
            {
                require(!Descendants<ComboBox>(view).Any() && model.EqBands.Count == 1
                    && model.EqBands[0].Type == kind && model.EqBands[0].Enabled && model.EqBands[0].GainDb == 0,
                    kind + " has a fixed filter type without a dropdown and one valid enabled filter.");
                var before = mixer.GraphConfig.Copy();
                var expected = before.Copy();
                var expectedBand = expected.Nodes.Single(node => node.Id == model.Id).EqBands[0];
                expectedBand.FrequencyHz = 1000; expectedBand.Q = 2;
                Descendants<Slider>(view).Single(slider => AutomationProperties.GetName(slider) == "Cutoff").Value = 1000;
                Descendants<Slider>(view).Single(slider => AutomationProperties.GetName(slider) == "Resonance / Q").Value = 2;
                require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expected, Protocol.Json),
                    kind + " cutoff and resonance controls update exactly their intended filter values without altering routes, gains or other nodes.");
                workspace.NodeChanged(model.Id, node => node.EqBands = before.Nodes.Single(item => item.Id == model.Id).Copy().EqBands);
                await layout();
                renderNode(view, kind == "highPass" ? "high-pass-closeup.png" : "low-pass-closeup.png");
            }
            if (kind is "denoiser" or "deEsser" or "plosive" or "reverb")
            {
                await layout(); renderNode(view, kind + "-closeup.png");
            }
        }
        require(!Descendants<Expander>(root).Any() && !Descendants<Button>(root).Any(button =>
                button.Content is string text && text.Contains("Incoming connections", StringComparison.Ordinal))
            && mixer.GraphConfig.Edges.Single().GainDb == -9 && mixer.GraphConfig.Edges.Single().Muted,
            "Incoming connection panels and duplicate send controls are gone; opening the UI preserves existing cable levels and mutes.");
        var equalizer = Descendants<EqualizerEditor>(root).Single();
        CheckEqualizerDials(Descendants<GraphNodeView>(root).Single(view => view.Id == nodes[0].Id), equalizer, workspace, mixer, require);
        CheckEqualizerMenus(equalizer, mixer, require);
        await layout();
        renderNode(Descendants<GraphNodeView>(root).Single(view => view.Id == nodes[0].Id), "eq-shapes-closeup.png");
        require(!Descendants<Button>(root).Any(button => AutomationProperties.GetName(button) is "Expand node" or "Collapse node"),
            "Nodes have no collapsing buttons.");
        foreach (var model in mixer.GraphConfig.Nodes.ToArray()) workspace.NodeChanged(model.Id, node => node.Collapsed = true);
        await layout();
        require(Descendants<GraphNodeView>(root).All(view => view.DetailsVisible && view.ActualHeight > 100),
            "Legacy collapsed flags never hide settings in the always-expanded node interface.");
        string copied = GraphClipboard.Serialize(mixer.GraphConfig, nodes.Where(node => node.Kind is "highPass" or "lowPass").Select(node => node.Id));
        require(GraphClipboard.TryCreatePaste(copied, new GraphConfiguration(), new Point(20, 20), out var paste, out _)
            && paste.Nodes.Count == 2 && paste.Nodes.All(node => node.EqBands.Single().Type == node.Kind),
            "Both dedicated pass-filter nodes survive the strict clipboard round trip with their filter settings intact.");
    }

    private static void CheckEqualizerDials(GraphNodeView view, EqualizerEditor editor, GraphWorkspace workspace, MixerViewModel mixer,
        Action<bool, string> require)
    {
        editor.SelectBand(2);
        var before = mixer.GraphConfig.Copy();
        var expected = before.Copy();
        var band = expected.Nodes.Single(node => node.Id == view.Id).EqBands[2];
        band.FrequencyHz = 1730; band.GainDb = -4.25; band.Q = 1.37;
        var dials = Descendants<RotaryValueControl>(view).ToDictionary(dial =>
            AutomationProperties.GetName(Descendants<Slider>(dial).Single()));
        require(dials["Frequency"].TryCommitText("1730 Hz")
            && dials["Band gain"].TryCommitText((-4.25).ToString(System.Globalization.CultureInfo.CurrentCulture) + " dB")
            && dials["Width / Q"].TryCommitText(1.37.ToString(System.Globalization.CultureInfo.CurrentCulture))
            && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expected, Protocol.Json),
            "The real EQ dial entries update frequency, gain and Q of only the selected band, retaining precise values, other bands and connections.");
        band.FrequencyHz = band.ResetFrequencyHz;
        dials["Frequency"].ResetToDefault();
        require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expected, Protocol.Json),
            "Resetting the frequency dial uses the selected band's saved anchor without changing its gain, width or routing.");
        workspace.NodeChanged(view.Id, node => node.EqBands = before.Nodes.Single(item => item.Id == view.Id).Copy().EqBands);
        editor.SelectBand(0);
    }

    private static void CheckEqualizerMenus(EqualizerEditor editor, MixerViewModel mixer, Action<bool, string> require)
    {
        var eq = mixer.GraphConfig.Nodes.Single(node => node.Kind == "eq");
        string otherBands = JsonSerializer.Serialize(eq.EqBands.Skip(1), Protocol.Json);
        foreach (string label in new[] { "Bell", "Low shelf", "High shelf", "Notch" })
        {
            var menu = editor.BuildBandMenu(0, new Point(100, 100));
            require(!menu.IsOpen, "EQ context menu construction remains detached and unopened.");
            var shapeMenu = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Shape"));
            var choice = shapeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, label));
            choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            string expected = label switch { "Bell" => "peak", "Low shelf" => "lowShelf", "High shelf" => "highShelf", _ => "notch" };
            require(mixer.GraphConfig.Nodes.Single(node => node.Kind == "eq").EqBands[0].Type == expected
                && JsonSerializer.Serialize(mixer.GraphConfig.Nodes.Single(node => node.Kind == "eq").EqBands.Skip(1), Protocol.Json) == otherBands,
                "The actual " + label + " context-menu action changes only the chosen band's calculation.");
            var checkedMenu = editor.BuildBandMenu(0, new Point(100, 100)).Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Shape"));
            require(checkedMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, label)).IsChecked,
                "The shape menu marks the current " + label + " choice.");
        }
        require(!editor.BuildBandMenu(0, new Point(100, 100)).Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Width")),
            "The EQ menu omits duplicate width presets; Width / Q remains the single width adjustment.");
        var notch = new EqBand { Type = "notch", FrequencyHz = 1000, Q = 1 };
        require(EqualizerEditor.CombinedResponseDb([notch], false, 1000) < -80
            && Math.Abs(EqualizerEditor.CombinedResponseDb([notch], false, 50)) < .1
            && Math.Abs(EqualizerEditor.CombinedResponseDb([notch], true, 1000)) < .00001,
            "The drawn notch response rejects its centre frequency, leaves distant audio intact, and becomes flat when bypassed.");
        var isolated = new EqualizerEditor();
        isolated.Apply([new EqBand { Type = "highPass", FrequencyHz = 120 }], false, 0);
        var legacy = JsonSerializer.Serialize(isolated.SnapshotBands(), Protocol.Json);
        _ = isolated.BuildBandMenu(0, new Point(100, 100));
        require(JsonSerializer.Serialize(isolated.SnapshotBands(), Protocol.Json) == legacy
            && !isolated.SetBandShape(0, "unknown") && !isolated.SetBandQ(0, double.NaN),
            "Opening shape options preserves legacy EQ pass filters; unsupported shapes and non-finite widths are rejected.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
