using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace VineMix.Desktop;

internal static class NodeDefaultsVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout, Action<bool, string> require)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new() { Nodes = GraphDefaults.Kinds.Select((kind, index) =>
            GraphDefaults.NewNode(kind, index * 680, 32)).ToList(), PanX = 45, PanY = 26, Zoom = .75 };
        var graph = fixture.Configuration.Graph;
        GraphNode Node(string kind) => graph.Nodes.Single(node => node.Kind == kind);
        graph.Edges = [Edge("microphone", "eq"), Edge("eq", "reverb"), Edge("reverb", "output"),
            Edge("appInput", "mix"), Edge("mix", "virtualOutput"), Edge("control", "deEsser", "control")];
        GraphEdge Edge(string from, string to, string kind = "audio") => new()
            { Id = from + "-" + to, From = Node(from).Id, To = Node(to).Id, Kind = kind, GainDb = -7, Muted = true };
        foreach (var node in graph.Nodes)
        {
            // This reset fixture represents an already configured five-band EQ.
            if (node.Kind == "eq") node.EqBands = EqBand.Defaults();
            node.Name = "Saved " + node.Kind;
            node.Device = new() { Id = "preview-device-" + node.Kind, StableId = "preview-stable-" + node.Kind, Name = "Saved device", Channel = 2 };
            node.App = new() { Name = "Saved application", ProcessId = 42, ExecutablePath = @"C:\Preview\Application.exe" };
            node.GainDb = -11; node.Muted = !node.Muted; node.Bypass = true; node.Mono = true; node.Balance = .4; node.Collapsed = true;
            foreach (var band in node.EqBands)
                if (node.Kind is "highPass" or "lowPass") { band.FrequencyHz = 350; band.Q = 2; }
                else band.GainDb = 5;
            node.Compressor.ThresholdDb = -30; node.Compressor.Ratio = 8; node.Compressor.AttackMs = 35;
            node.Compressor.ReleaseMs = 250; node.Compressor.MakeupDb = 3;
            node.Compressor.Mode = "limit"; node.Compressor.Detector = "external"; node.Compressor.CeilingDb = -7;
            node.Ducking = new() { ThresholdDb = -36.25, AmountDb = 26.5, AttackMs = .125, ReleaseMs = 376.125 };
            node.Expander = new() { Mode = "gate", ThresholdDb = -51, Ratio = 7, AttackMs = 2, HoldMs = 111, ReleaseMs = 420, RangeDb = 38, HysteresisDb = 5 };
            node.AlignmentDelay.Samples = 719;
            node.Denoiser.SuppressionDb = -32; node.Denoiser.RnnoiseMix = .47;
            if (node.Kind == "denoiser") node.Denoiser.Algorithm = "speex";
            node.DeEsser = new() { FrequencyHz = 8500, ThresholdDb = -35, ReductionDb = 16 };
            node.Plosive = new() { FrequencyHz = 230, ThresholdDb = -38, ReductionDb = 18 };
            node.Reverb = new() { Mix = .65, DecaySeconds = 3.5, Damping = .8, PreDelayMs = 60 };
            node.VoiceColor = new() { Tone = -.65, Ambience = .7 };
        }
        require(GraphDefaults.Validate(graph).Length == 0, "The reset fixture contains valid edited controls, saved assignments, and audio/control connections for all supported node kinds.");
        fixture.Status.Nodes = graph.Nodes.Select(node => new GraphNodeStatus { Id = node.Id, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        foreach (var original in graph.Nodes)
        {
            var expected = mixer.GraphConfig.Copy();
            var target = expected.Nodes.Single(node => node.Id == original.Id);
            var defaults = GraphDefaults.NewNode(original.Kind, original.X, original.Y);
            if (original.Kind == "expander" && original.Expander.Mode == "gate") defaults.Expander.Mode = "gate";
            defaults.Id = original.Id; defaults.Name = original.Name;
            defaults.DynamicsUiVersion = original.DynamicsUiVersion;
            if (original.Kind == "denoiser") defaults.Denoiser.Algorithm = original.Denoiser.Algorithm;
            defaults.Device = original.Device.Copy(); defaults.Device.Channel = -1;
            defaults.App = new() { Name = original.App.Name, ProcessId = original.App.ProcessId, ExecutablePath = original.App.ExecutablePath };
            expected.Nodes[expected.Nodes.IndexOf(target)] = defaults;
            long count = mixer.ConfigurationPresentationCount;
            workspace.ResetNode(original.Id);
            require(mixer.ConfigurationPresentationCount == count + 1 && Json(mixer.GraphConfig) == Json(expected),
                original.Kind + " Reset node restores every control while preserving names, IDs, positions, device/app selections, the denoiser algorithm, other nodes, and all weighted/muted cables.");
            string restored = Json(mixer.GraphConfig);
            count = mixer.ConfigurationPresentationCount; int history = mixer.EditHistory.Entries.Count;
            workspace.ResetNode(original.Id);
            require(mixer.ConfigurationPresentationCount == count && mixer.EditHistory.Entries.Count == history && Json(mixer.GraphConfig) == restored,
                original.Kind + " Reset node is idempotent and does not save or record an unchanged reset again.");
        }
        require(mixer.GraphConfig.Nodes.Where(node => node.Kind is "output" or "virtualOutput").All(node => node.Muted)
            && mixer.GraphConfig.Nodes.Single(node => node.Kind == "output").GainDb == -24
            && mixer.GraphConfig.Nodes.All(node => node.Device.Channel == -1 && !node.Mono && !node.Collapsed),
            "Whole-node reset uses safe muted destination defaults and restores stereo source selection without dropping device bindings.");
        require(mixer.GraphConfig.Nodes.Single(node => node.Kind == "denoiser").Denoiser is { Algorithm: "speex", SuppressionDb: -18, RnnoiseMix: 1 },
            "Resetting the saved Speex fixture restores both control sets while preserving its processor choice.");
        require(mixer.GraphConfig.Nodes.Single(node => node.Kind == "eq").EqBands.Count == 0,
            "Resetting a configured EQ removes all bands while preserving its saved identity and weighted/muted connections.");
        await layout();
        var cases = new (string Kind, string Label, double Value, Action<GraphNode> Expected)[]
        {
            ("denoiser", "Reduction", 30, node => node.Denoiser.SuppressionDb = -30),
            ("deEsser", "Frequency", 8000, node => node.DeEsser.FrequencyHz = 8000),
            ("deEsser", "Threshold", -30, node => node.DeEsser.ThresholdDb = -30),
            ("deEsser", "Max reduction", 12, node => node.DeEsser.ReductionDb = 12),
            ("plosive", "Frequency limit", 240, node => node.Plosive.FrequencyHz = 240),
            ("plosive", "Threshold", -45, node => node.Plosive.ThresholdDb = -45),
            ("plosive", "Max reduction", 18, node => node.Plosive.ReductionDb = 18),
            ("reverb", "Mix", 50, node => node.Reverb.Mix = .5),
            ("reverb", "Decay", 2, node => node.Reverb.DecaySeconds = 2),
            ("reverb", "Damping", 70, node => node.Reverb.Damping = .7),
            ("reverb", "Pre-delay", 35, node => node.Reverb.PreDelayMs = 35),
            ("ducking", "Threshold", -28.25, node => node.Ducking.ThresholdDb = -28.25),
            ("ducking", "Duck amount", 21.75, node => node.Ducking.AmountDb = 21.75),
            ("ducking", "Attack", .125, node => node.Ducking.AttackMs = .125),
            ("ducking", "Release", 377.125, node => node.Ducking.ReleaseMs = 377.125),
            ("limiter", "Ceiling", -3.25, node => node.Compressor.CeilingDb = -3.25),
            ("limiter", "Release", 215.125, node => node.Compressor.ReleaseMs = 215.125)
        };
        foreach (var item in cases)
        {
            var model = mixer.GraphConfig.Nodes.Single(node => node.Kind == item.Kind);
            var view = Descendants<GraphNodeView>(workspace).Single(node => node.Id == model.Id);
            var expected = mixer.GraphConfig.Copy(); item.Expected(expected.Nodes.Single(node => node.Id == model.Id));
            Descendants<Slider>(view).Single(slider => AutomationProperties.GetName(slider) == item.Label).Value = item.Value;
            require(Json(mixer.GraphConfig) == Json(expected), item.Kind + " " + item.Label + " inline control changes exactly its intended effect parameter.");
        }
        var copied = mixer.GraphConfig.Copy();
        require(Json(copied) == Json(mixer.GraphConfig) && GraphDefaults.Validate(copied).Length == 0,
            "All new effects and reset anchors survive the normal graph copy/JSON round trip without changing routes or values.");
        string payload = GraphClipboard.Serialize(copied, copied.Nodes.Select(node => node.Id));
        require(GraphClipboard.TryCreatePaste(payload, new(), new Point(16, 32), out var pasted, out _)
            && pasted.Nodes.Count == copied.Nodes.Count
            && pasted.Nodes.All(node => EffectValues(node) == EffectValues(copied.Nodes.Single(original => original.Kind == node.Kind))),
            "Clipboard groups preserve every new effect parameter and EQ reset anchor alongside legacy node kinds.");
        var invalidCases = new (string Name, Action<GraphNode> Change)[]
        {
            ("denoiser lower bound", node => node.Denoiser.SuppressionDb = -41), ("denoiser nonfinite", node => node.Denoiser.SuppressionDb = double.NaN),
            ("de-esser frequency", node => node.DeEsser.FrequencyHz = 12001), ("de-esser threshold", node => node.DeEsser.ThresholdDb = -61),
            ("de-esser reduction", node => node.DeEsser.ReductionDb = 25), ("plosive frequency", node => node.Plosive.FrequencyHz = 59),
            ("plosive threshold", node => node.Plosive.ThresholdDb = 1), ("plosive reduction", node => node.Plosive.ReductionDb = -1),
            ("reverb mix", node => node.Reverb.Mix = 1.01), ("reverb decay", node => node.Reverb.DecaySeconds = 5.1),
            ("reverb damping", node => node.Reverb.Damping = -.01), ("reverb delay", node => node.Reverb.PreDelayMs = 101),
            ("Voice Color tone lower bound", node => node.VoiceColor.Tone = -1.01), ("Voice Color tone upper bound", node => node.VoiceColor.Tone = 1.01),
            ("Voice Color ambience lower bound", node => node.VoiceColor.Ambience = -.01), ("Voice Color ambience upper bound", node => node.VoiceColor.Ambience = 1.01),
            ("Voice Color nonfinite tone", node => node.VoiceColor.Tone = double.NaN), ("Voice Color nonfinite ambience", node => node.VoiceColor.Ambience = double.PositiveInfinity),
            ("Voice Color null settings", node => node.VoiceColor = null!),
            ("reset anchor", node => node.EqBands[0].ResetFrequencyHz = 10)
        };
        foreach (var invalid in invalidCases)
        {
            var bad = copied.Copy(); invalid.Change(bad.Nodes[0]);
            require(GraphDefaults.Validate(bad).Length > 0, "Graph validation rejects an invalid " + invalid.Name + " before configuration dispatch.");
        }
        require(PresentationSource.FromVisual(workspace) is null && mixer.IsPreview,
            "Node resets and new effect controls are verified only in detached preview state, without live settings or desktop input.");
    }

    private static string Json(GraphConfiguration graph) => JsonSerializer.Serialize(graph, Protocol.Json);
    private static string EffectValues(GraphNode node) => JsonSerializer.Serialize(new
        { node.EqBands, node.Compressor, node.Ducking, node.DynamicsUiVersion, node.Expander, node.AlignmentDelay, node.Denoiser, node.DeEsser, node.Plosive, node.Reverb, node.VoiceColor }, Protocol.Json);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
