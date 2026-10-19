using System;
using AudioMixer.Desktop.Controls;
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

namespace AudioMixer.Desktop;

/// <summary>Real controls and cable gestures, detached from devices and the user's graph.</summary>
internal static class CoreDynamicsVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        CheckModels(require);
        var fixture = UiPreview.CreateSnapshot([]);
        var source = GraphDefaults.NewNode("mix", 20, 25);
        var compressor = GraphDefaults.NewNode("compressor", 530, 25);
        compressor.DynamicsUiVersion = 0; compressor.Compressor = CompressorSettings.Ducking();
        var expander = GraphDefaults.NewNode("expander", 850, 25);
        var delay = GraphDefaults.NewNode("alignmentDelay", 1270, 25);
        fixture.Configuration.Graph = new() { Nodes = [source, compressor, expander, delay], Zoom = .75, PanX = 20, PanY = 20 };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus { Id = node.Id, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNode Current(GraphNode original) => mixer.GraphConfig.Nodes.Single(node => node.Id == original.Id);
        GraphNodeView View(GraphNode node) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == node.Id);
        ComboBox Choice(GraphNodeView view, string label) => Descendants<ComboBox>(view).Single(item => AutomationProperties.GetName(item) == label);
        void Select(ComboBox choice, string value) => choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, value));
        Dictionary<string, RotaryValueControl> Numbers(GraphNodeView view) => Descendants<RotaryValueControl>(view)
            .GroupBy(number => AutomationProperties.GetName(Descendants<Slider>(number).Single())).ToDictionary(group => group.Key, group => group.First());
        var compressorView = View(compressor); var expanderView = View(expander); var delayView = View(delay);
        var compressorNumbers = Numbers(compressorView); var expanderNumbers = Numbers(expanderView); var delayNumbers = Numbers(delayView);
        var sizes = new[] { compressorView, expanderView, delayView }.ToDictionary(view => view.Id, view => view.RenderSize);
        require(!Visible(expanderNumbers["Hold"], expanderView) && !Visible(expanderNumbers["Hysteresis"], expanderView)
            && Visible(expanderNumbers["Ratio"], expanderView),
            "Expander shows only its five applicable dials, with saved gate settings retained outside the visible layout.");
        render(expanderView, "expander-clean.png");
        require(compressorView.FindSocket("sidechain", false) is not null && compressorView.FindSocket("audio", false) is not null
            && compressorView.FindSocket("audio", true) is not null && compressorView.FindSocket("sidechain", true) is null,
            "Compressor exposes a separate detector-only input while keeping its ordinary programme input/output.");
        require(Json(Current(compressor).Compressor) == Json(CompressorSettings.Ducking()) && mixer.GraphConfig.Edges.Count == 0,
            "A saved external compressor keeps its original ratio-based ducking settings without creating audio routes.");
        require(compressorView.DynamicsFeedbackText == "Connect a sidechain detector", "Ducking clearly indicates its missing external detector.");
        var output = View(source).FindSocket("audio", true)!; var detector = compressorView.FindSocket("sidechain", false)!;
        Point Screen(GraphWorkspace.Socket socket) { var point = workspace.SocketPosition(socket); return new(point.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX, point.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY); }
        require(workspace.FindSnapSocket(Screen(detector) + new Vector(8, 0), output) == detector
            && workspace.FindSnapSocket(Screen(output) + new Vector(8, 0), detector) == output,
            "Forgiving socket snapping supports ordinary audio outputs to the separate sidechain in both drag directions.");
        workspace.BeginConnectionPreview(output, Screen(output), dragging: true);
        workspace.UpdateConnectionPointer(Screen(detector));
        workspace.FinishConnectionPointer(MouseButton.Left, Screen(detector)); await layout();
        require(mixer.GraphConfig.Edges.Count == 1 && mixer.GraphConfig.Edges[0].Kind == "sidechain",
            "The actual cable gesture creates a sidechain edge, never an audible programme edge.");
        string edgeId = mixer.GraphConfig.Edges[0].Id;
        require(GraphConnectionEditing.InsertionError(mixer.GraphConfig, delay.Id, edgeId).Length > 0,
            "Dropping a processing node on a detector cable cannot insert it into a programme path.");
        render(compressorView, "compressor-ducking.png");
        Select(Choice(compressorView, "Compressor mode"), "limit"); await layout();
        require(Current(compressor).Compressor.Mode == "limit" && Current(compressor).Compressor.Detector == "external"
            && !Choice(compressorView, "Compressor detector").IsEnabled
            && !Visible(compressorNumbers["Threshold"], compressorView)
            && Visible(compressorNumbers["Ceiling"], compressorView)
            && mixer.GraphConfig.Edges.Single().Id == edgeId && compressorView.RenderSize == sizes[compressorView.Id],
            "Limit exposes its ceiling and release, hides inapplicable compression controls, and preserves the detector cable and node dimensions.");
        require(compressorNumbers["Ceiling"].TryCommitText("-7.3 dBFS") && Math.Abs(Current(compressor).Compressor.CeilingDb + 7.3) < 1e-12,
            "Limit's actual inline editor commits a precise ceiling.");
        render(compressorView, "compressor-limit.png");
        Select(Choice(compressorView, "Compressor mode"), "compress");
        require(Current(compressor).Compressor.Ratio == 6 && Current(compressor).Compressor.ThresholdDb == -30
            && Choice(compressorView, "Compressor detector").IsEnabled, "Returning to Compress retains the saved Ducking controls and detector selection.");
        Select(Choice(expanderView, "Expander mode"), "gate"); await layout();
        require(Current(expander).Expander.Mode == "gate" && !expanderNumbers["Ratio"].IsEnabled
            && !Visible(expanderNumbers["Ratio"], expanderView) && Visible(expanderNumbers["Hold"], expanderView)
            && Visible(expanderNumbers["Hysteresis"], expanderView) && expanderView.RenderSize == sizes[expanderView.Id],
            "Gate replaces Ratio with its Hold and Hysteresis dials in the same two-row layout, without losing saved settings or moving sockets.");
        require(expanderNumbers["Threshold"].TryCommitText("-45.5") && expanderNumbers["Hold"].TryCommitText("125")
            && expanderNumbers["Hysteresis"].TryCommitText("4.2") && Current(expander).Expander.ThresholdDb == -45.5
            && Current(expander).Expander.HoldMs == 125 && Math.Abs(Current(expander).Expander.HysteresisDb - 4.2) < 1e-12,
            "Gate threshold, hold and hysteresis editors update the intended engine parameters.");
        render(expanderView, "expander-gate.png");
        Select(Choice(expanderView, "Expander mode"), "expander");
        require(expanderNumbers["Ratio"].IsEnabled && Current(expander).Expander.HoldMs == 125, "Expander restores ratio adjustment while retaining envelope settings.");
        require(delayNumbers["Milliseconds"].TryCommitText("12.375 ms") && Current(delay).AlignmentDelay.Samples == 594
            && Descendants<TextBox>(delayNumbers["Samples"]).Single().Text == "594", "Milliseconds edits quantize once to exact 48 kHz samples and synchronize the sample editor.");
        require(delayNumbers["Samples"].TryCommitText("1") && Current(delay).AlignmentDelay.Samples == 1
            && Descendants<TextBox>(delayNumbers["Milliseconds"]).Single().Text.StartsWith("0.021"), "A one-sample delay is visibly represented with three decimal milliseconds.");
        require(delayNumbers["Samples"].TryCommitText("96000") && Current(delay).AlignmentDelay.Milliseconds == 2000
            && !delayNumbers["Samples"].TryCommitText("96001") && !delayNumbers["Milliseconds"].TryCommitText("-1"),
            "Alignment editors enforce the two-second buffer boundary.");
        delayNumbers["Samples"].TryCommitText("594"); render(delayView, "alignment-delay.png");
        foreach (var view in new[] { compressorView, expanderView, delayView })
        {
            var button = Descendants<Button>(view).Single(item => AutomationProperties.GetName(item) == "Bypass");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(mixer.GraphConfig.Nodes.Single(node => node.Id == view.Id).Bypass && Equals(button.Content, "Bypass"),
                view.Id + " uses the standard Bypass control and state.");
            workspace.ResetNode(view.Id);
            require(!mixer.GraphConfig.Nodes.Single(node => node.Id == view.Id).Bypass && mixer.GraphConfig.Edges.Count == 1,
                "Reset restores processing defaults while retaining the sidechain and workspace routing.");
        }
        require(Json(Current(compressor).Compressor) == Json(new CompressorSettings()) && Json(Current(expander).Expander) == Json(new ExpanderSettings())
            && Current(delay).AlignmentDelay.Samples == 0, "Whole-node reset restores each new mode, detector and delay default.");
        foreach (double zoom in new[] { .3, .4, .5, .6, .75, .9, 1, 1.1, 1.25, 1.5, 1.75, 2 })
        {
            mixer.Edit(config => config.Graph.Zoom = zoom); await layout();
            foreach (var view in new[] { compressorView, expanderView, delayView })
                require(view.RenderSize == sizes[view.Id] && view.DetailsVisible,
                    $"{view.Id} keeps complete controls and fixed node geometry at {zoom * 100:0}% zoom.");
        }
        mixer.Edit(config => config.Graph.Zoom = .75); await layout();
        require(workspace.BeginInputLinkDrag(detector, Screen(detector)), "A sidechain uses the same input-end detach gesture as other cables.");
        require(workspace.FindSnapSocket(Screen(delayView.FindSocket("audio", false)!), output) is null,
            "An existing sidechain cannot snap to a programme input and silently become audible.");
        workspace.HandleCaptureLoss();
        require(mixer.GraphConfig.Edges.Single().Id == edgeId, "Cancelling a sidechain rewire preserves its original detector route.");
        require(workspace.ConnectNodes(source.Id, expander.Id).Length == 0, "Prepare a separate programme cable for drag isolation.");
        await layout();
        require(workspace.BeginInputLinkDrag(expanderView.FindSocket("audio", false)!, Screen(expanderView.FindSocket("audio", false)!)), "An ordinary cable enters rewire mode.");
        require(workspace.FindSnapSocket(Screen(detector), output) is null,
            "An existing programme cable cannot snap to a detector input and silently retain an audible edge kind.");
        workspace.HandleCaptureLoss();
        workspace.RemoveEdge(mixer.GraphConfig.Edges.Single(edge => edge.Kind == "audio").Id);
        require(workspace.BeginInputLinkDrag(detector, Screen(detector)), "The retained sidechain can still be detached normally.");
        workspace.UpdateConnectionPointer(new Point(1800, 800)); workspace.FinishConnectionPointer(MouseButton.Left, new Point(1800, 800));
        require(mixer.GraphConfig.Edges.Count == 0, "Dragging the detector cable off its input removes only that sidechain.");
    }

    private static void CheckModels(Action<bool, string> require)
    {
        var a = GraphDefaults.NewNode("mix", 1, 2); var b = GraphDefaults.NewNode("compressor", 3, 4);
        var c = GraphDefaults.NewNode("expander", 5, 6); var d = GraphDefaults.NewNode("alignmentDelay", 7, 8);
        var graph = new GraphConfiguration { Nodes = [a, b, c, d], Edges = [new() { Id = "side", From = a.Id, To = b.Id, Kind = "sidechain" }] };
        b.Compressor = CompressorSettings.Ducking(); b.Compressor.Mode = "limit"; b.Compressor.CeilingDb = -5.7;
        c.Expander = new() { Mode = "gate", ThresholdDb = -52.5, Ratio = 7, AttackMs = 4.5, HoldMs = 81, ReleaseMs = 370, RangeDb = 48, HysteresisDb = 4 };
        d.AlignmentDelay.Samples = 719; d.Bypass = true;
        require(GraphDefaults.Validate(graph).Length == 0 && GraphDefaults.Validate(JsonSerializer.Deserialize<GraphConfiguration>(Json(graph), Protocol.Json)!).Length == 0,
            "New dynamics, detector connections and exact delay values validate and load from normal configuration JSON.");
        require(Json(graph.Copy()) == Json(graph) && !ReferenceEquals(c.Expander, graph.Copy().Nodes[2].Expander)
            && !ReferenceEquals(d.AlignmentDelay, graph.Copy().Nodes[3].AlignmentDelay), "Graph copies preserve all new controls and own independent settings.");
        require(GraphClipboard.TryCreatePaste(GraphClipboard.Serialize(graph, graph.Nodes.Select(node => node.Id)), new(), new(100, 200), out var paste, out _)
            && paste.Edges.Single().Kind == "sidechain" && Json(paste.Nodes[1].Compressor) == Json(b.Compressor)
            && Json(paste.Nodes[2].Expander) == Json(c.Expander) && paste.Nodes[3].AlignmentDelay.Samples == 719 && paste.Nodes[3].Bypass,
            "Copy/paste keeps the complete node group, detector-only cable, both modes, exact delay and bypass.");
        var older = JsonNode.Parse(GraphClipboard.Serialize(new() { Nodes = [a] }, [a.Id]))!;
        var oldNode = older["nodes"]![0]!.AsObject(); oldNode.Remove("expander"); oldNode.Remove("alignmentDelay");
        var oldCompressor = oldNode["compressor"]!.AsObject(); oldCompressor.Remove("mode"); oldCompressor.Remove("detector"); oldCompressor.Remove("ceilingDb");
        require(GraphClipboard.TryCreatePaste(older.ToJsonString(), new(), new(), out var legacy, out _)
            && legacy.Nodes[0].Compressor.Mode == "compress" && legacy.Nodes[0].Compressor.Detector == "internal",
            "Older copied graphs without new fields keep ordinary internal compression and receive inert defaults.");
        require(GraphDefaults.ConnectionError(graph, new() { From = c.Id, To = b.Id, Kind = "sidechain" }).Length > 0,
            "A second external sidechain is rejected instead of silently mixing detectors.");
        require(GraphDefaults.ConnectionError(graph, new() { From = b.Id, To = a.Id }).Length > 0,
            "Sidechain dependencies participate in cycle detection.");
        require(GraphDefaults.ConnectionError(graph, new() { From = a.Id, To = c.Id, Kind = "sidechain" }).Length > 0,
            "Expander rejects detector-only cables.");
        require(!GraphConnectionEditing.TryPrepareDetach(graph, b.Id, out _, out string detachError) && detachError.Contains("detector"),
            "Detaching a sidechain-dependent node cannot create an automatic audible bridge.");
        var bad = new Action<GraphNode>[] { n => n.Compressor.Mode = "other", n => n.Compressor.Detector = "other", n => n.Compressor.CeilingDb = 1,
            n => n.Expander.Mode = "other", n => n.Expander.ThresholdDb = -81, n => n.Expander.Ratio = 21, n => n.Expander.AttackMs = 0,
            n => n.Expander.HoldMs = 1001, n => n.Expander.ReleaseMs = 4, n => n.Expander.RangeDb = 81, n => n.Expander.HysteresisDb = -1,
            n => n.Expander.ThresholdDb = double.NaN, n => n.AlignmentDelay.Samples = -1, n => n.AlignmentDelay.Samples = 96001 };
        foreach (var invalid in bad) { var copy = graph.Copy(); invalid(copy.Nodes[0]); require(GraphDefaults.Validate(copy).Length > 0, "Invalid dynamics/delay settings cannot enter the graph."); }
        string payload = GraphClipboard.Serialize(graph, graph.Nodes.Select(node => node.Id));
        foreach (var malformed in new[] { "null", "{}", "{\"samples\":1.5}", "{\"samples\":96001}", "{\"samples\":1,\"extra\":true}" })
        {
            var document = JsonNode.Parse(payload)!; document["nodes"]![0]!["alignmentDelay"] = JsonNode.Parse(malformed);
            require(!GraphClipboard.TryCreatePaste(document.ToJsonString(), new(), new(), out _, out _), "Clipboard rejects malformed or non-integral alignment settings.");
        }
        foreach (var field in new[] { "expander", "compressor" })
        {
            var document = JsonNode.Parse(payload)!; document["nodes"]![0]![field] = null;
            require(!GraphClipboard.TryCreatePaste(document.ToJsonString(), new(), new(), out _, out _), "Clipboard rejects null dynamics settings.");
        }
    }
    private static bool Visible(DependencyObject child, DependencyObject root)
    {
        for (DependencyObject? current = child; current is not null && current != root; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
