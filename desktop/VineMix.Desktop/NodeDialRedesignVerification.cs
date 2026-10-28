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

/// <summary>Detached controls and synthetic telemetry only; no engine, endpoint or native window.</summary>
internal static class NodeDialRedesignVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> renderNode, Action<string> renderWorkspace)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "The dial redesign is verified with a detached preview and no audio engine.");
        var expectedCounts = new Dictionary<string, int>
        {
            ["compressor"] = 5, ["limiter"] = 2, ["ducking"] = 5, ["expander"] = 5, ["gate"] = 6,
            ["highPass"] = 2, ["lowPass"] = 2, ["denoiser"] = 2, ["deEsser"] = 3,
            ["plosive"] = 3, ["reverb"] = 4, ["pan"] = 1, ["alignmentDelay"] = 2,
            ["voiceColor"] = 2, ["tone"] = 3, ["eq"] = 3
        };
        var graph = new GraphConfiguration
        {
            Nodes = expectedCounts.Keys.Select((kind, index) => GraphDefaults.NewNode(kind, index * 800, 40)).ToList(),
            Zoom = 1, PanX = 20, PanY = 20
        };
        graph.Nodes.Single(node => node.Kind == "eq").EqBands = EqBand.Defaults(); // Existing five-band dial fixture.
        mixer.ApplyPreviewSnapshot(Snapshot(graph)); await layout();
        GraphNode Current(string kind) => mixer.GraphConfig.Nodes.Single(node => node.Kind == kind);
        GraphNodeView View(string kind) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == Current(kind).Id);
        RotaryValueControl Dial(string kind, string label) => Dials(View(kind)).Single(dial => Label(dial) == label);

        foreach (var expected in expectedCounts)
        {
            var view = View(expected.Key); var dials = Dials(view);
            require(dials.Length == expected.Value && Descendants<Slider>(view).Count() == dials.Length,
                expected.Key + " exposes every scalar adjustment through the shared rotary control, with no horizontal numeric fields.");
            foreach (var dial in dials)
            {
                string label = Label(dial);
                var range = Descendants<Slider>(dial).Single(); var entry = Descendants<TextBox>(dial).Single();
                require(label.Length > 0 && Descendants<TextBlock>(dial).Any(text => text.Text == label)
                    && AutomationProperties.GetName(entry) == label + " value" && entry.Text.Length > 0
                    && AutomationProperties.GetHelpText(entry).Contains("Enter applies")
                    && AutomationProperties.GetHelpText(entry).Contains("Tab or Shift+Tab applies")
                    && AutomationProperties.GetHelpText(entry).Contains("Escape cancels")
                    && range.Focusable && Descendants<FrameworkElement>(dial).Any(element => element.GetType().Name == "DialFace"),
                    expected.Key + " " + label + " has a persistent label, visible value, keyboard range, exact editor and recognisable shared dial face.");
            }
            CheckLayout(view, require, expected.Key + " at 100%");
        }
        double Diameter(string kind, string label) => Descendants<Slider>(Dial(kind, label)).Single().ActualWidth;
        require(Dials(View("eq")).All(dial => Descendants<Slider>(dial).Single().ActualWidth == 86)
            && Diameter("compressor", "Threshold") > Diameter("compressor", "Attack")
            && Diameter("highPass", "Cutoff") > Diameter("highPass", "Resonance / Q")
            && Diameter("reverb", "Mix") > Diameter("reverb", "Damping"),
            "EQ retains three large dials, while primary threshold, cutoff and mix controls visibly outweigh secondary timing and refinement controls.");
        var eqEditor = Descendants<EqualizerEditor>(View("eq")).Single();
        var widthDial = Dial("eq", "Width / Q");
        string initialEq = Json(mixer.GraphConfig);
        for (int index = 0; index < Current("eq").EqBands.Count; index++)
        {
            eqEditor.SelectBand(index);
            require(widthDial.Value == widthDial.DefaultValue,
                "Each factory EQ band starts exactly at its shape-appropriate Width reset marker.");
            widthDial.ResetToDefault();
        }
        require(Json(mixer.GraphConfig) == initialEq,
            "Selecting and resetting the Width of untouched default EQ bands leaves all saved settings unchanged.");
        eqEditor.SelectBand(0);
        widthDial.TryCommitText("2.5");
        require(widthDial.Value == 2.5 && widthDial.DefaultValue == .70710678,
            "Editing a shelf width keeps its reset marker anchored to the shelf default rather than following the edited value.");
        eqEditor.SelectBand(2);
        require(widthDial.DefaultValue == 1, "Selecting a bell updates Width's reset marker to the bell default.");
        eqEditor.SelectBand(0);
        require(widthDial.Value == 2.5 && widthDial.DefaultValue == .70710678,
            "Selecting a saved edited shelf restores its exact Q without changing its reset anchor.");
        eqEditor.SetBandShape(0, "notch");
        require(widthDial.Value == 2.5 && widthDial.DefaultValue == 1,
            "Changing filter shape updates the Width reset target while preserving the current sound setting.");
        widthDial.ResetToDefault();
        require(Current("eq").EqBands[0].Q == 1, "Explicit Width reset applies the selected notch's default Q.");
        eqEditor.SetBandShape(0, "lowShelf"); widthDial.ResetToDefault();
        require(Current("eq").EqBands[0].Q == .70710678,
            "Returning to a shelf resets Width to the exact existing shelf default.");
        workspace.ResetNode(Current("eq").Id); await layout();
        var compressorView = View("compressor"); var limiterView = View("limiter"); var duckingView = View("ducking");
        require(Dials(compressorView).Select(Label).SequenceEqual(["Threshold", "Ratio", "Make-up", "Attack", "Release"])
            && !Descendants<ComboBox>(compressorView).Any() && compressorView.FindSocket("sidechain", false) is null
            && !Descendants<FrameworkElement>(compressorView).Any(element => AutomationProperties.GetName(element).Contains("preset", StringComparison.OrdinalIgnoreCase)),
            "A new Compressor has five sound dials and measures its own audio, without detector or legacy mode selectors.");
        require(Dials(limiterView).Select(Label).SequenceEqual(["Ceiling", "Release"])
            && !Descendants<ComboBox>(limiterView).Any() && limiterView.FindSocket("sidechain", false) is null
            && Descendants<TextBlock>(limiterView).Any(text => text.Text.Contains("No lookahead") && text.Text.Contains("Bypass removes limiting")),
            "A new Limiter has only Ceiling and Release, no detector input, and visible sample-peak/bypass guidance.");
        require(Dials(duckingView).Where(dial => Visible(dial, duckingView)).Select(Label).SequenceEqual(["Threshold", "Duck amount", "Attack", "Release"])
            && Descendants<ComboBox>(duckingView).Single() is var duckMode && AutomationProperties.GetName(duckMode) == "Ducking mode"
            && duckingView.FindSocket("audio", false) is not null && duckingView.FindSocket("audio", true) is not null
            && duckingView.FindSocket("sidechain", false) is not null && duckingView.FindSocket("control", false) is null
            && duckingView.FindSocket("sidechain", true) is null
            && Descendants<TextBlock>(duckingView).Any(text => text.Text == "Trigger"),
            "Ducking exposes four dials and separately labelled audio and detector-only trigger sockets, without an unrelated level input.");

        workspace.NodeChanged(Current("compressor").Id, node => { node.Compressor.AttackMs = .125; node.Compressor.ReleaseMs = 119.375; });
        workspace.NodeChanged(Current("alignmentDelay").Id, node => node.AlignmentDelay.Samples = 1); await layout();
        require(Descendants<TextBox>(Dial("compressor", "Attack")).Single().Text.Contains("0.125")
            && Descendants<TextBox>(Dial("compressor", "Release")).Single().Text.Contains("119.375"),
            "Short attack and fractional release remain visibly precise before opening the exact editor.");
        string precise = Json(mixer.GraphConfig); long presentations = mixer.ConfigurationPresentationCount;
        foreach (var item in new[] { (Kind: "compressor", Label: "Attack"), (Kind: "compressor", Label: "Release"), (Kind: "alignmentDelay", Label: "Samples") })
        {
            var dial = Dial(item.Kind, item.Label); dial.BeginTextEntry();
            require(dial.TryCommitText(Descendants<TextBox>(dial).Single().Text), item.Kind + " " + item.Label + " accepts its unchanged exact editor text.");
        }
        require(Json(mixer.GraphConfig) == precise && mixer.ConfigurationPresentationCount == presentations
            && Current("compressor").Compressor.AttackMs == .125 && Current("compressor").Compressor.ReleaseMs == 119.375
            && Current("alignmentDelay").AlignmentDelay.Samples == 1 && !Dial("alignmentDelay", "Samples").TryCommitText("1.5"),
            "Unchanged .125 ms attack, 119.375 ms release and one-sample edits preserve stored precision and do not save; sample counts reject fractions.");

        var duckSize = duckingView.RenderSize;
        ulong frame = 20;
        void DuckFeedback(float reduction, float detector)
        {
            var node = Current("ducking");
            duckingView.ApplyDynamicsFeedbackAt(node, new() { Id = node.Id, DynamicsFrame = ++frame,
                GainReductionDb = reduction, DetectorDb = detector, Path = new() { State = "Running" } }, 1, true);
        }
        DuckFeedback(0, -96);
        require(duckingView.DynamicsFeedbackText == "Connect a trigger", "Ducking identifies a missing detector connection explicitly.");
        require(workspace.ConnectNodes(Current("compressor").Id, Current("ducking").Id, "sidechain").Length == 0,
            "A Compressor output connects to Ducking using a detector-only cable.");
        DuckFeedback(0, -70);
        require(duckingView.DynamicsFeedbackText == "Below threshold" && duckingView.VisibleReductionDb == 0,
            "A quiet trigger shows Below threshold and zero reduction.");
        DuckFeedback(18, -12);
        require(duckingView.DynamicsFeedbackText == "Ducking" && duckingView.VisibleReductionDb == 18
            && Descendants<TextBlock>(duckingView).Any(text => AutomationProperties.GetName(text) == "Trigger level" && text.Text.Contains("dBFS")),
            "Synthetic active-trigger telemetry shows Ducking, measured trigger level and 18 dB reduction.");
        workspace.NodeChanged(Current("ducking").Id, node => node.Ducking.AmountDb = 0); DuckFeedback(0, -12);
        require(duckingView.DynamicsFeedbackText == "No attenuation · amount 0 dB",
            "Zero depth explains unity without falsely labelling an active trigger as below threshold.");
        workspace.NodeChanged(Current("ducking").Id, node => node.Ducking.AmountDb = 18); DuckFeedback(18, -12);
        var bypass = Descendants<Button>(duckingView).Single(button => AutomationProperties.GetName(button) == "Bypass");
        bypass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); DuckFeedback(18, -12);
        require(Current("ducking").Bypass && duckingView.DynamicsFeedbackText == "Bypassed" && duckingView.VisibleReductionDb == 0,
            "Ducking uses the standard bottom Bypass control and clears its active reduction feedback.");
        bypass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); DuckFeedback(18, -12); await layout();
        require(duckingView.RenderSize == duckSize, "Trigger, below-threshold, ducking and bypass states keep node geometry stable.");

        var sizes = expectedCounts.Keys.ToDictionary(kind => kind, kind => View(kind).RenderSize);
        foreach (double zoom in new[] { .3, .4, .5, .6, .75, .9, 1, 1.1, 1.25, 1.5, 1.75, 2 })
        {
            mixer.Edit(configuration => configuration.Graph.Zoom = zoom); await layout();
            foreach (string kind in expectedCounts.Keys)
            {
                var view = View(kind);
                require(view.RenderSize == sizes[kind] && view.DetailsVisible, kind + $" retains all controls and its node dimensions at {zoom * 100:0}% zoom.");
                CheckLayout(view, require, kind + $" at {zoom * 100:0}%");
            }
        }

        await PopulateGraph(workspace, mixer, layout, require, renderNode, renderWorkspace);
        require(mixer.Endpoints.Count == 0 && mixer.GraphConfig.Nodes.All(node => node.Device.Id.Length == 0 && node.Device.StableId.Length == 0)
            && mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "All redesign screenshots and checks use unassigned preview nodes, synthetic telemetry and no physical or virtual audio endpoints.");
    }

    private static async Task PopulateGraph(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> renderNode, Action<string> renderWorkspace)
    {
        var mic = GraphDefaults.NewNode("microphone", 20, 20); mic.Name = "Voice";
        var compressor = GraphDefaults.NewNode("compressor", 450, 20); compressor.Name = "Voice compressor";
        var music = GraphDefaults.NewNode("mix", 20, 680); music.Name = "Music"; music.GainDb = -7;
        var ducking = GraphDefaults.NewNode("ducking", 1030, 570); ducking.Name = "Music under voice";
        var limiter = GraphDefaults.NewNode("limiter", 1480, 570); limiter.Name = "Music peak protection";
        var output = GraphDefaults.NewNode("output", 1930, 570); output.Name = "Programme output";
        var voiceMonitor = GraphDefaults.NewNode("mix", 1030, 20); voiceMonitor.Name = "Audible voice branch";
        GraphEdge Cable(string id, GraphNode from, GraphNode to, string kind = "audio") => new() { Id = id, From = from.Id, To = to.Id, Kind = kind };
        var graph = new GraphConfiguration
        {
            Nodes = [mic, compressor, music, ducking, limiter, output, voiceMonitor],
            Edges = [Cable("voice-main", mic, compressor), Cable("voice-trigger", compressor, ducking, "sidechain"),
                Cable("voice-audible", compressor, voiceMonitor), Cable("music-main", music, ducking),
                Cable("music-limited", ducking, limiter), Cable("programme", limiter, output)],
            Zoom = .5, PanX = 20, PanY = 20
        };
        require(GraphDefaults.Validate(graph).Length == 0, "The populated example keeps Music on the main path and Voice on a separate detector and optional audible branch.");
        var snapshot = Snapshot(graph);
        foreach (var status in snapshot.Status.Nodes)
        {
            status.DynamicsFrame = 500; status.DetectorDb = -12;
            status.GainReductionDb = status.Id == ducking.Id ? 18 : status.Id == compressor.Id ? 3.5f : status.Id == limiter.Id ? 1.2f : 0;
            status.Channels = 2; status.PeakLeft = .35f; status.PeakRight = .25f;
        }
        mixer.ApplyPreviewSnapshot(snapshot); await layout();
        var views = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id);
        // Allow actual measured heights to determine the lower row so the
        // populated graph stays collision-free if labels or dial sizes change.
        double lowerRow = graph.Nodes.Where(node => node.Y == 20).Max(node => views[node.Id].ActualHeight + node.Y) + 100;
        mixer.Edit(configuration =>
        {
            foreach (var node in configuration.Graph.Nodes.Where(node => node.Id == music.Id || node.Id == ducking.Id || node.Id == limiter.Id || node.Id == output.Id))
                node.Y = lowerRow;
        });
        await layout();
        var bounds = mixer.GraphConfig.Nodes.Select(node => new Rect(node.X, node.Y, views[node.Id].ActualWidth, views[node.Id].ActualHeight)).ToArray();
        require(bounds.SelectMany((first, i) => bounds.Skip(i + 1).Select(second => !first.IntersectsWith(second))).All(value => value),
            "Measured node rectangles do not overlap in the populated dynamics workspace.");
        foreach (var pair in new[] { (Node: compressor, File: "compressor-new.png"), (Node: limiter, File: "limiter-new.png"), (Node: ducking, File: "ducking-new.png") })
            renderNode(views[pair.Node.Id], pair.File);
        workspace.FitAll(); await layout(); workspace.UpdateWires(); await layout();
        require(mixer.GraphConfig.Zoom is >= .3 and <= 1 && mixer.GraphConfig.Edges.Single(edge => edge.Kind == "sidechain").To == ducking.Id,
            "Fit frames the populated graph while retaining its separate detector cable.");
        renderWorkspace("dynamics-populated.png");
    }

    private static MixerSnapshot Snapshot(GraphConfiguration graph) => new()
    {
        Configuration = new() { Version = 2, Graph = graph }, Endpoints = [], Applications = [],
        Status = new() { Nodes = graph.Nodes.Select(node => new GraphNodeStatus { Id = node.Id, Path = new() { State = "Running" } }).ToList() }
    };
    private static string Label(RotaryValueControl dial) => AutomationProperties.GetName(Descendants<Slider>(dial).Single());
    private static RotaryValueControl[] Dials(GraphNodeView view) => Descendants<RotaryValueControl>(view).ToArray();
    private static string Json(GraphConfiguration graph) => JsonSerializer.Serialize(graph, Protocol.Json);
    private static void CheckLayout(GraphNodeView view, Action<bool, string> require, string name)
    {
        var dials = Dials(view).Where(dial => Visible(dial, view)).ToArray();
        var nodeBounds = new Rect(-.5, -.5, view.ActualWidth + 1, view.ActualHeight + 1);
        var bounds = dials.Select(dial => dial.TransformToAncestor(view).TransformBounds(new Rect(dial.RenderSize))).ToArray();
        require(bounds.All(rect => rect.Width > 0 && rect.Height > 0 && nodeBounds.Contains(rect)),
            name + " keeps complete dial groups inside its node boundary.");
        require(bounds.SelectMany((first, i) => bounds.Skip(i + 1).Select(second =>
            Rect.Intersect(first, second) is var overlap && (overlap.IsEmpty || overlap.Width < .5 || overlap.Height < .5))).All(value => value),
            name + " has no overlapping dial controls.");
        require(dials.All(dial => Descendants<TextBox>(dial).All(entry => entry.ActualWidth >= 80 && entry.ActualHeight >= 24)
            && Descendants<TextBlock>(dial).Where(text => text.Text == Label(dial)).All(text => text.ActualHeight >= 14)),
            name + " retains readable label and value allocations without collapsed or clipped fields.");
        var bypass = Descendants<Button>(view).SingleOrDefault(button => AutomationProperties.GetName(button) == "Bypass");
        require(bypass is not null && bounds.All(rect => rect.Bottom <= bypass.TranslatePoint(new Point(), view).Y),
            name + " keeps Bypass below all parameter groups.");
    }
    private static bool Visible(DependencyObject child, DependencyObject root)
    {
        for (DependencyObject? current = child; current is not null && current != root; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
