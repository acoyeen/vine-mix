using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Offline editor checks against the same operations used by its controls.</summary>
internal static class GraphUiVerification
{
    private static readonly List<string> Checks = [];
    private static readonly List<string> BindingErrors = [];
    private static string DirectoryFor(string[] args)
    {
        int at = Array.IndexOf(args, "--verification-dir");
        return at >= 0 && at + 1 < args.Length ? Path.GetFullPath(args[at + 1]) : Path.Combine(AppContext.BaseDirectory, "graph-ui-verification");
    }
    private static void Require(bool condition, string description)
    { if (!condition) throw new InvalidOperationException(description); Checks.Add(description); }
    private static async Task Layout(Window window)
    { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); }
    public static async Task RunAsync(MainWindow window, MixerViewModel mixer, string[] args)
    {
        string directory = DirectoryFor(args); Directory.CreateDirectory(directory);
        var listener = new BindingListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            Require(mixer.IsPreview, "The editor harness opens no control pipe or audio stream");
            await Layout(window);
            Require(window.Width == 1280 && window.Height == 800 && window.MinWidth == 900 && window.MinHeight == 580
                && double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight)
                && window.ResizeMode == ResizeMode.CanResize, "Resizable workspace starts at 1280 × 800 with a 900 × 580 minimum");
            Require(window.WindowStyle == WindowStyle.None && !window.AllowsTransparency, "Integrated window controls with opaque native window");
            Require(mixer.GraphConfig.Nodes.Count == 8 && mixer.GraphConfig.Edges.Count == 9, "Existing mixer migrates to eight nodes and all nine saved cables");
            Require(mixer.GraphConfig.Nodes.Single(n => n.Id == "mix-a") is { Muted: true, GainDb: -24 }, "Migration retains the muted -24 dB listening mix");
            for (int mask = 0; mask < 64; mask++)
            {
                var configuration = new MixerConfiguration();
                for (int i = 0; i < 3; i++)
                {
                    configuration.Sources[i].A.Enabled = (mask & 1 << (i * 2)) != 0;
                    configuration.Sources[i].B.Enabled = (mask & 1 << (i * 2 + 1)) != 0;
                    configuration.Sources[i].A.GainDb = -3 - i;
                    configuration.Sources[i].B.GainDb = -9 - i;
                }
                var graph = GraphDefaults.FromLegacy(configuration);
                string[] ids = ["mic1", "mic2", "app-input"];
                for (int i = 0; i < 3; i++)
                {
                    var a = graph.Edges.Single(e => e.Id == ids[i] + "-a"); var b = graph.Edges.Single(e => e.Id == ids[i] + "-b");
                    if (a.Muted == configuration.Sources[i].A.Enabled || b.Muted == configuration.Sources[i].B.Enabled || a.GainDb != -3 - i || b.GainDb != -9 - i)
                        throw new InvalidOperationException("Saved send migration mismatch for route mask " + mask);
                }
            }
            Checks.Add("All 64 prior routing combinations preserve independent send levels and enabled states");
            window.Workspace.FitAll(); await Layout(window);
            Require(Descendants<GraphNodeView>(window).Count() == 8, "The actual canvas renders every migrated node");
            await MenuHoverHelpVerification.RunAsync(window, mixer, () => Layout(window), Require);
            Render(window, Path.Combine(directory, "workspace.png"));

            var fresh = UiPreview.CreateSnapshot([]); fresh.Configuration.Graph = new(); mixer.ApplyPreviewSnapshot(fresh); await Layout(window);
            var workspace = window.Workspace;
            var spawned = new Dictionary<string, string>();
            int at = 0;
            foreach (string kind in GraphDefaults.Kinds.Where(NodeCatalog.CanCreate))
            {
                string id = workspace.Spawn(kind, new Point(36 + at % 4 * 310, 40 + at / 4 * 320));
                if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Could not add " + kind);
                spawned.Add(kind, id); at++;
            }
            Require(spawned.Count == NodeCatalog.Categories.Sum(category => category.Kinds.Count) && spawned.Count == mixer.GraphConfig.Nodes.Count,
                "Every current source, effect, mix, control and output type can be spawned");
            Require(workspace.Spawn("reroute", new Point(20, 20)).Length == 0 && workspace.Spawn("gain", new Point(20, 20)).Length == 0,
                "Redundant Gain and Reroute nodes cannot be created from the workspace action.");
            var legacy = GraphDefaults.NewNode("reroute", 40, 40); mixer.Edit(c => c.Graph.Nodes.Add(legacy));
            var legacyGain = GraphDefaults.NewNode("gain", 40, 400); mixer.Edit(c => c.Graph.Nodes.Add(legacyGain)); spawned.Add("gain", legacyGain.Id);
            Require(mixer.GraphConfig.Nodes.Where(n => n.Kind is "output" or "virtualOutput").All(n => n.Muted)
                && mixer.GraphConfig.Nodes.Single(n => n.Kind == "output").Device.Id == ""
                && mixer.GraphConfig.Nodes.Single(n => n.Kind == "virtualOutput").Device.Id == fresh.Configuration.VirtualOutputRender.Id,
                "New destinations begin muted; physical outputs need selection and the virtual output binds its unique cable automatically");
            Require(mixer.GraphConfig.Nodes.Single(n => n.Kind == "eq").EqBands.Count == 0, "New EQ nodes begin with no bands");
            Require(workspace.ConnectNodes(spawned["microphone"], spawned["eq"], "audio").Length == 0, "Source connects to EQ using the editor's connection action");
            Require(workspace.ConnectNodes(spawned["eq"], spawned["compressor"], "audio").Length == 0 && workspace.ConnectNodes(spawned["compressor"], spawned["mix"], "audio").Length == 0, "Effects connect into an Add/Mix node");
            Require(workspace.ConnectNodes(spawned["mix"], spawned["output"], "audio").Length == 0 && workspace.ConnectNodes(spawned["mix"], spawned["virtualOutput"], "audio").Length == 0, "One mix fans out to independent physical and virtual destinations");
            int cables = mixer.GraphConfig.Edges.Count;
            Require(workspace.ConnectNodes(spawned["mix"], spawned["eq"], "audio").Length > 0 && mixer.GraphConfig.Edges.Count == cables, "Feedback is rejected without mutating the graph");
            Require(workspace.ConnectNodes(spawned["microphone"], spawned["eq"], "audio").Length > 0, "Duplicate audio connections are rejected");
            Require(workspace.ConnectNodes(spawned["output"], spawned["gain"], "audio").Length > 0, "A listening destination cannot act as an audio source");
            Require(workspace.ConnectNodes(spawned["control"], spawned["eq"], "control").Length == 0, "A shared control connects through the separate control path");
            Require(workspace.ConnectNodes(spawned["control"], spawned["mix"], "audio").Length > 0, "Control signals cannot enter audio sockets");

            workspace.NodeChanged(spawned["eq"], n => { n.EqBands = EqBand.Defaults(); n.EqBands[2].FrequencyHz = 1800; n.EqBands[2].GainDb = -6; n.EqBands[2].Q = 1.4; n.Bypass = true; });
            workspace.NodeChanged(spawned["compressor"], n => { n.Compressor.ThresholdDb = -22; n.Compressor.Ratio = 3; });
            workspace.NodeChanged(spawned["pan"], n => n.Balance = -.4);
            workspace.NodeChanged(spawned["microphone"], n => { n.X = 150; n.Y = 200; });
            var copy = mixer.GraphConfig.Copy();
            Require(copy.Nodes.Single(n => n.Kind == "eq").EqBands[2].FrequencyHz == 1800 && copy.Nodes.Single(n => n.Kind == "eq").Bypass, "EQ parameters and bypass survive JSON persistence round trip");
            Require(copy.Nodes.Single(n => n.Kind == "compressor").Compressor.Ratio == 3 && copy.Nodes.Single(n => n.Kind == "pan").Balance == -.4, "Dynamics and balance parameters survive persistence round trip");
            Require(copy.Nodes.Single(n => n.Kind == "microphone").X == 150 && copy.Nodes.Single(n => n.Kind == "microphone").GainDb == 0, "Moving a node changes layout without changing its level");
            var send = mixer.GraphConfig.Edges.First(e => e.From == spawned["mix"] && e.To == spawned["output"]);
            workspace.EdgeChanged(send.Id, edge => { edge.GainDb = -9; edge.Muted = true; });
            Require(mixer.GraphConfig.Edges.Single(e => e.From == spawned["mix"] && e.To == spawned["virtualOutput"]) is { GainDb: 0, Muted: false }, "Adjusting one outgoing send preserves the other send");
            await Layout(window); workspace.FitAll(); await Layout(window);
            Render(window, Path.Combine(directory, "node-library.png"));
            Require(Descendants<GraphNodeView>(window).Count() == GraphDefaults.Kinds.Length, "All node types create real inline editor controls");
            workspace.RemoveNode(spawned["eq"]);
            Require(mixer.GraphConfig.Nodes.All(n => n.Id != spawned["eq"]) && mixer.GraphConfig.Edges.All(e => e.From != spawned["eq"] && e.To != spawned["eq"]), "Removing a node also removes its audio and control connections");
            Require(GraphDefaults.Validate(mixer.GraphConfig).Length == 0, "The edited graph passes structural validation");

            var effects = UiPreview.CreateSnapshot([]); effects.Configuration.Graph = new() { Zoom = .9, PanX = 20, PanY = 20 };
            effects.Configuration.Graph.Nodes.Add(GraphDefaults.NewNode("eq", 30, 35));
            effects.Configuration.Graph.Nodes.Add(GraphDefaults.NewNode("compressor", 410, 35));
            effects.Configuration.Graph.Nodes.Add(GraphDefaults.NewNode("pan", 790, 35));
            mixer.ApplyPreviewSnapshot(effects); await Layout(window);
            workspace.FitAll(); await Layout(window);
            Render(window, Path.Combine(directory, "effects.png"));
            ConsoleTheme.SetPreviewHighContrast(true); await Layout(window);
            Render(window, Path.Combine(directory, "effects-high-contrast.png"));
            ConsoleTheme.SetPreviewHighContrast(null);

            var ambiguousApp = UiPreview.CreateSnapshot([]);
            var savedApp = GraphDefaults.NewNode("appInput", 40, 40);
            savedApp.App = new() { ProcessId = 99, ExecutablePath = @"C:\same\app.exe", Name = "SameApp" };
            ambiguousApp.Configuration.Graph = new() { Nodes = [savedApp] };
            ambiguousApp.Applications = [
                new() { ProcessId = 100, ExecutablePath = @"C:\same\app.exe", Name = "SameApp" },
                new() { ProcessId = 101, ExecutablePath = @"C:\same\app.exe", Name = "SameApp" },
                new() { ProcessId = 99, ExecutablePath = @"C:\different\app.exe", Name = "SameApp" }
            ];
            ambiguousApp.Status.Nodes = [new() { Id = savedApp.Id, Path = new() { State = "Waiting" } }];
            mixer.ApplyPreviewSnapshot(ambiguousApp); await Layout(window);
            var appView = Descendants<GraphNodeView>(window).Single(n => n.Id == savedApp.Id);
            var appChooser = Descendants<ComboBox>(appView).Single();
            var selectedApp = appChooser.SelectedItem as AudioApplication;
            Require(selectedApp is { ProcessId: 99 } && selectedApp.ExecutablePath == savedApp.App.ExecutablePath && selectedApp.Name.Contains("saved selection")
                && appView.AvailabilityNoticeLabel == "CHOOSE APP INSTANCE"
                && ambiguousApp.Applications.All(candidate => !ReferenceEquals(candidate, selectedApp))
                && ambiguousApp.Applications.Select(a => a.Label).Distinct().Count() == ambiguousApp.Applications.Count,
                "Ambiguous app restarts retain a saved placeholder; recycled PIDs do not bind; process labels distinguish every candidate");
            workspace.NodeChanged(savedApp.Id, node => node.Collapsed = true);
            await Layout(window);
            Require(appView.DetailsVisible && !Descendants<Button>(appView).Any(button => AutomationProperties.GetName(button) is "Collapse node" or "Expand node"),
                "The node stays expanded without a collapse button, even when a legacy saved flag is present");
            bool previousReducedAnimation = mixer.ReducedAnimation;
            mixer.ReducedAnimation = true; await Layout(window);
            bool preferenceReachedMeters = Descendants<GraphNodeView.NodeMeter>(window).All(meter => meter.ReducedAnimation);
            var reducedMeter = new GraphNodeView.NodeMeter();
            reducedMeter.Update(.1, .05, 2, false, false, true, 0);
            reducedMeter.Update(.85, .15, 2, false, false, true, .06);
            reducedMeter.Update(.2, .65, 2, false, false, true, .2);
            bool displayHeld = reducedMeter.LeftLevel == .1 && reducedMeter.RightLevel == .05;
            reducedMeter.Update(.3, .1, 2, false, false, true, .251);
            bool peakPreserved = reducedMeter.LeftLevel == .85 && reducedMeter.RightLevel == .65;
            reducedMeter.Update(1.2, .1, 2, true, false, true, .26);
            bool clippingImmediate = reducedMeter.Clipped && reducedMeter.Level == 1.2;
            reducedMeter.Update(.3, .2, 2, false, true, true, .27);
            bool muteImmediate = reducedMeter.Muted && reducedMeter.LeftLevel == 0 && reducedMeter.RightLevel == 0 && !reducedMeter.Clipped;
            reducedMeter.Update(.3, .15, 2, false, false, false, .28);
            Require(preferenceReachedMeters && displayHeld && peakPreserved && clippingImmediate && muteImmediate
                && !reducedMeter.ReducedAnimation && reducedMeter.LeftLevel == .3 && reducedMeter.RightLevel == .15,
                "Reduced animation reaches rendered stereo meters, holds presentation for 250 ms, preserves each channel's peaks, and responds immediately to clipping, mute and preference changes");
            mixer.ReducedAnimation = previousReducedAnimation;

            mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot(["--waiting"])); await Layout(window); workspace.FitAll(); await Layout(window);
            Require(mixer.GraphConfig.Nodes.Single(n => n.Id == "mic1").Device.Name.Contains("USB microphone") && mixer.GraphStatus.Single(n => n.Id == "mic1").Path.State == "Waiting", "Disconnected source keeps its saved identity and waiting status");
            Render(window, Path.Combine(directory, "waiting.png"));
            await CanvasNavigationVerification.VerifySettingsAsync(mixer, () => Layout(window), Require);
            Require(Descendants<Button>(window).Any(b => AutomationProperties.GetName(b).Contains("Minimise")) && Descendants<Button>(window).Any(b => AutomationProperties.GetName(b).Contains("Close")), "Window actions remain accessible inside the application header");
            Require(BindingErrors.Count == 0, "No WPF binding errors during graph operations");
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = true, checks = Checks, bindingErrors = BindingErrors, scope = "Inert editor and persistence checks; no real audio or hardware qualification." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); ConsoleTheme.SetPreviewHighContrast(null); }
    }
    public static void WriteFailure(string[] args, Exception error)
    {
        string directory = DirectoryFor(args); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = false, checks = Checks, error = error.ToString(), bindingErrors = BindingErrors }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Render(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); png.Save(output);
    }
    private sealed class BindingListener : TraceListener
    {
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) BindingErrors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
