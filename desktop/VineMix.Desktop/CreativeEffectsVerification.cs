using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Creative-effect editing and presentation in a detached, synthetic workspace.</summary>
internal static class CreativeEffectsVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Creative-effect checks run without a native window, personal settings or audio devices.");
        require(NodeCatalog.Categories.Count(category => category.Title == "Effects") == 1
            && NodeCatalog.CategoryFor("tone").Title == "Effects" && NodeCatalog.CategoryFor("eq").Title == "Effects"
            && NodeCatalog.CategoryFor("plosive").Title == "Effects" && NodeCatalog.CategoryFor("deEsser").Title == "Effects",
            "Tone, EQ, detector and creative processors share one flat Effects category.");
        require(NodeCatalog.MatchesSearch("echo", "repeats") && NodeCatalog.MatchesSearch("distortion", "saturation")
            && NodeCatalog.MatchesSearch("tone", "effects") && NodeCatalog.MatchesSearch("eq", "effects")
            && NodeCatalog.HeaderBrushKey("echo") == NodeCatalog.HeaderBrushKey("eq"),
            "New names, effect purposes and category names are searchable while effect header colours stay unchanged.");
        var fixture = UiPreview.CreateSnapshot([]);
        var echo = GraphDefaults.NewNode("echo", 20, 30);
        var distortion = GraphDefaults.NewNode("distortion", 420, 30);
        fixture.Configuration.Graph = new() { Nodes = [echo, distortion], Zoom = 1 };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Channels = 2, PeakLeft = .1f, PeakRight = .05f, Path = new() { State = "Running" } }).ToList();
        mixer.IsLayoutLocked = false; mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNode Node(string kind) => mixer.GraphConfig.Nodes.Single(node => node.Kind == kind);
        GraphNodeView View(string kind) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == Node(kind).Id);
        RotaryValueControl Dial(string kind, string label) => Descendants<RotaryValueControl>(View(kind))
            .Single(dial => Descendants<Slider>(dial).Any(slider => AutomationProperties.GetName(slider) == label));
        require(echo.Echo is { TimeMs: 300, Repeats: 3, Mix: .25 }
            && distortion.Distortion is { DriveDb: 6, ToneHz: 6000, Mix: .5, OutputDb: -6 },
            "Echo and Distortion start with the exact shared desktop/native defaults.");
        foreach (string kind in new[] { "echo", "distortion" })
        {
            require(GraphDefaults.AudioInputPorts(kind).SequenceEqual(["audio"])
                && GraphDefaults.AudioOutputPorts(kind).SequenceEqual(kind == "echo" ? ["audio", "wet"] : ["audio"])
                && View(kind).Sockets.Count == (kind == "echo" ? 3 : 2) && !GraphDefaults.HasSidechainInput(Node(kind))
                && !NodeCatalog.HasLevelControl(kind),
                kind + " retains ordinary Audio routing, adds Echo-only output for Echo, and has no Trigger or generic Level control.");
            require(!Descendants<ComboBox>(View(kind)).Any()
                && Descendants<Button>(View(kind)).Count(button => AutomationProperties.GetName(button) == "Bypass") == 1,
                kind + " exposes its focused controls and existing Bypass action without speculative modes.");
        }
        require(Descendants<RotaryValueControl>(View("echo")).Count() == 3
            && Descendants<RotaryValueControl>(View("distortion")).Count() == 4,
            "Echo has Time, Repeats and Mix; Distortion has Drive, Tone, Mix and Output.");
        require(workspace.ConnectNodes(echo.Id, distortion.Id).Length == 0, "Creative effects connect through the standard audio route action.");
        var cases = new (string Kind, string Label, string Text, double Value, Func<GraphNode, double> Read)[]
        {
            ("echo", "Time", "417.125", 417.125, node => node.Echo.TimeMs),
            ("echo", "Repeats", "5", 5, node => node.Echo.Repeats),
            ("echo", "Mix", "37.5", .375, node => node.Echo.Mix),
            ("distortion", "Drive", "18.25", 18.25, node => node.Distortion.DriveDb),
            ("distortion", "Tone", "4321.5", 4321.5, node => node.Distortion.ToneHz),
            ("distortion", "Mix", "62.5", .625, node => node.Distortion.Mix),
            ("distortion", "Output", "-9.25", -9.25, node => node.Distortion.OutputDb)
        };
        foreach (var item in cases)
        {
            string before = Json(mixer.GraphConfig);
            var dial = Dial(item.Kind, item.Label);
            dial.BeginTextEntry();
            var entry = Descendants<TextBox>(dial).Single();
            entry.Text = item.Text.Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            require(dial.HandleEntryKey(Key.Enter, ModifierKeys.None) && item.Read(Node(item.Kind)) == item.Value,
                item.Kind + " " + item.Label + " commits exact numeric entry to its intended model field.");
            string edited = Json(mixer.GraphConfig);
            require(mixer.Undo() && Json(mixer.GraphConfig) == before && mixer.Redo() && Json(mixer.GraphConfig) == edited,
                item.Kind + " " + item.Label + " supports exact undo and redo without losing its route.");
            await layout();
        }
        var output = Dial("distortion", "Output");
        output.BeginTextEntry(); Descendants<TextBox>(output).Single().Text = "1";
        require(!output.TryCommitPendingEntry() && Node("distortion").Distortion.OutputDb == -9.25,
            "An invalid positive Distortion Output draft cannot change the effect.");
        output.CancelPendingEntry();
        var repeats = Dial("echo", "Repeats");
        foreach (string text in new[] { "2.5", "0", "9" })
        {
            repeats.BeginTextEntry();
            Descendants<TextBox>(repeats).Single().Text = text.Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            require(!repeats.TryCommitPendingEntry() && Node("echo").Echo.Repeats == 5,
                "Repeats rejects fractional or out-of-range entry without truncation or a model change.");
            repeats.CancelPendingEntry();
        }
        var saved = mixer.GraphConfig.Copy();
        var loaded = JsonSerializer.Deserialize<GraphConfiguration>(Json(saved), Protocol.Json)!;
        require(Json(loaded) == Json(saved) && GraphDefaults.Validate(loaded).Length == 0,
            "Creative settings survive the actual JSON save/load format exactly.");
        var copy = saved.Copy(); copy.Nodes[0].Echo.Mix = 0; copy.Nodes[1].Distortion.DriveDb = 0;
        require(saved.Nodes[0].Echo.Mix == .375 && saved.Nodes[1].Distortion.DriveDb == 18.25,
            "Graph copies independently own Echo and Distortion settings.");
        require(GraphClipboard.TryCreatePaste(GraphClipboard.Serialize(saved, saved.Nodes.Select(node => node.Id)), new(), new(40, 50), out var paste, out _)
            && paste.Nodes.Single(node => node.Kind == "echo").Echo.TimeMs == 417.125
            && paste.Nodes.Single(node => node.Kind == "distortion").Distortion.OutputDb == -9.25 && paste.Edges.Count == 1,
            "Serialized clipboard groups preserve both creative settings and their audio connection without using the system clipboard.");
        var legacy = JsonNode.Parse(GraphClipboard.Serialize(saved, saved.Nodes.Select(node => node.Id)))!;
        foreach (var node in legacy["nodes"]!.AsArray()) { node!.AsObject().Remove("echo"); node.AsObject().Remove("distortion"); }
        require(GraphClipboard.TryCreatePaste(legacy.ToJsonString(), new(), new(40, 50), out var legacyPaste, out _)
            && legacyPaste.Nodes.All(node => node.Echo.TimeMs == 300 && node.Distortion.OutputDb == -6),
            "Clipboard payloads without the optional creative settings retain the shared defaults.");
        var malformed = JsonNode.Parse(GraphClipboard.Serialize(saved, saved.Nodes.Select(node => node.Id)))!;
        malformed["nodes"]![0]!["echo"]!["unexpected"] = true;
        require(!GraphClipboard.TryCreatePaste(malformed.ToJsonString(), new(), new(40, 50), out _, out _),
            "Creative clipboard settings retain strict shape validation and reject unknown nested fields.");
        foreach (string kind in new[] { "echo", "distortion" })
        {
            string before = Json(mixer.GraphConfig); workspace.ResetNode(Node(kind).Id);
            require(kind == "echo" ? Json(Node(kind).Echo) == Json(new EchoSettings()) : Json(Node(kind).Distortion) == Json(new DistortionSettings()),
                kind + " Reset restores every creative control to the shared defaults.");
            require(mixer.Undo() && Json(mixer.GraphConfig) == before, kind + " reset is undoable without changing identity or routing.");
        }
        VerifyValidation(require);
        VerifyEchoCompatibility(saved, require);
        foreach (string kind in new[] { "echo", "distortion" }) render(View(kind), kind + "-controls.png");
    }

    // Shared raw payloads exercise the managed reader/clipboard and the real
    // native serializer, including boundary values in the legacy conversion.
    internal static readonly (string Json, int? Repeats)[] EchoJsonCases =
    [
        ("{}", 3), ("{\"timeMs\":417.125,\"mix\":0.375}", 3),
        ("{\"feedback\":0}", 1), ("{\"feedback\":0.0099}", 1),
        ("{\"feedback\":0.01}", 2), ("{\"feedback\":0.1}", 3),
        ("{\"feedback\":0.35}", 5), ("{\"feedback\":0.9}", 8),
        ("{\"timeMs\":417.125,\"feedback\":0.35,\"mix\":0.375}", 5),
        ("{\"repeats\":1}", 1), ("{\"repeats\":8}", 8),
        ("{\"repeats\":3.0}", 3), ("{\"repeats\":3e0}", 3),
        ("{\"feedback\":0.9,\"repeats\":2}", 2),
        ("{\"repeats\":2,\"feedback\":0.9}", 2),
        ("{\"repeats\":0}", null), ("{\"repeats\":9}", null),
        ("{\"repeats\":3.1}", null), ("{\"repeats\":3.5}", null),
        ("{\"repeats\":\"3\"}", null),
        ("{\"repeats\":null}", null), ("{\"repeats\":true}", null),
        ("{\"repeats\":1e40}", null), ("{\"feedback\":-0.01}", null),
        ("{\"feedback\":0.9001}", null), ("{\"feedback\":null}", null),
        ("{\"feedback\":\"0.35\"}", null),
        ("{\"repeats\":3,\"feedback\":0.91}", null),
        ("{\"repeats\":3,\"feedback\":null}", null),
        ("{\"timeMs\":19}", null), ("{\"mix\":1.01}", null)
    ];

    private static void VerifyEchoCompatibility(GraphConfiguration graph, Action<bool, string> require)
    {
        string group = GraphClipboard.Serialize(graph, graph.Nodes.Select(node => node.Id));
        foreach (var item in EchoJsonCases)
        {
            EchoSettings? value = null;
            try { value = JsonSerializer.Deserialize<EchoSettings>(item.Json, Protocol.Json); }
            catch (JsonException) { }
            require(item.Repeats.HasValue ? value?.Repeats == item.Repeats : value is null,
                "Echo JSON validates or deterministically converts " + item.Json);
            var payload = JsonNode.Parse(group)!;
            payload["nodes"]![0]!["echo"] = JsonNode.Parse(item.Json);
            bool pasted = GraphClipboard.TryCreatePaste(payload.ToJsonString(), new(), new(40, 50), out var paste, out _);
            require(pasted == item.Repeats.HasValue && (!pasted || paste.Nodes[0].Echo.Repeats == item.Repeats),
                "Echo clipboard applies the same defaults, integer validation and legacy conversion for " + item.Json);
            if (value is not null)
            {
                using var canonical = JsonDocument.Parse(Json(value));
                require(!canonical.RootElement.TryGetProperty("feedback", out _) && canonical.RootElement.GetProperty("repeats").GetInt32() == item.Repeats,
                    "Converted Echo settings save only canonical integer Repeats, never legacy Feedback.");
            }
        }
        var old = JsonSerializer.Deserialize<EchoSettings>("{\"timeMs\":417.125,\"feedback\":0.35,\"mix\":0.375}", Protocol.Json)!;
        require(old is { TimeMs: 417.125, Repeats: 5, Mix: .375 },
            "Legacy Echo conversion preserves exact Time and Mix while deliberately replacing its feedback tail with a finite count.");
    }

    private static void VerifyValidation(Action<bool, string> require)
    {
        var fields = new (string Name, double Low, double High, Action<GraphNode, double> Set)[]
        {
            ("echo time", 20, 2000, (node, value) => node.Echo.TimeMs = value),
            ("echo mix", 0, 1, (node, value) => node.Echo.Mix = value),
            ("distortion drive", 0, 36, (node, value) => node.Distortion.DriveDb = value),
            ("distortion tone", 500, 18000, (node, value) => node.Distortion.ToneHz = value),
            ("distortion mix", 0, 1, (node, value) => node.Distortion.Mix = value),
            ("distortion output", -24, 0, (node, value) => node.Distortion.OutputDb = value)
        };
        foreach (var field in fields)
        foreach (double value in new[] { field.Low, field.High, field.Low - .01, field.High + .01, double.NaN, double.PositiveInfinity })
        {
            var node = GraphDefaults.NewNode(field.Name.StartsWith("echo") ? "echo" : "distortion", 0, 0);
            field.Set(node, value);
            require((GraphDefaults.Validate(new() { Nodes = [node] }).Length == 0) == (value == field.Low || value == field.High),
                field.Name + " accepts inclusive endpoints and rejects out-of-range/nonfinite values.");
        }
        foreach (int count in new[] { 0, 1, 3, 8, 9 })
        {
            var node = GraphDefaults.NewNode("echo", 0, 0); node.Echo.Repeats = count;
            require((GraphDefaults.Validate(new() { Nodes = [node] }).Length == 0) == (count is >= 1 and <= 8),
                "Echo Repeats validates the inclusive 1–8 range.");
        }
        var old = JsonSerializer.Deserialize<GraphNode>("{\"kind\":\"gain\",\"id\":\"legacy-creative-defaults\"}", Protocol.Json)!;
        require(old.Echo.TimeMs == 300 && old.Echo.Repeats == 3 && old.Distortion.OutputDb == -6 && GraphDefaults.Validate(new() { Nodes = [old] }).Length == 0,
            "Older nodes without creative settings receive compatible defaults without changing their kind.");
        old.Echo = null!;
        require(GraphDefaults.Validate(new() { Nodes = [old] }).Length > 0, "Null Echo settings fail validation.");
        old.Echo = new(); old.Distortion = null!;
        require(GraphDefaults.Validate(new() { Nodes = [old] }).Length > 0, "Null Distortion settings fail validation.");
    }
    private static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
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
