using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

// Detached creation, persistence and history checks. No personal settings,
// system clipboard, native window, engine connection or audio devices.
internal static class EqDefaultsVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<GraphNodeView, string>? render = null)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "EQ creation verification runs only in a detached preview.");
        var empty = GraphDefaults.NewNode("eq", 120, 80);
        var second = GraphDefaults.NewNode("eq", 520, 80);
        require(empty.EqBands.Count == 0 && empty.Copy().EqBands.Count == 0
            && !ReferenceEquals(empty.EqBands, second.EqBands),
            "Each newly created EQ starts with its own empty band collection and remains empty when copied.");
        var emptyGraph = new GraphConfiguration { Nodes = [empty] };
        string emptyJson = Json(emptyGraph);
        var restored = JsonSerializer.Deserialize<GraphConfiguration>(emptyJson, Protocol.Json)!;
        require(restored.Nodes.Single().EqBands.Count == 0 && Json(restored) == emptyJson
            && GraphDefaults.Validate(restored).Length == 0,
            "An explicitly empty EQ survives a complete graph save/load as zero bands and remains valid.");
        CheckClipboard(emptyGraph, 0, require);

        var legacy = JsonSerializer.Deserialize<GraphNode>("{\"id\":\"legacy-eq\",\"kind\":\"eq\"}", Protocol.Json)!;
        require(Json(legacy.EqBands) == Json(EqBand.Defaults()),
            "Older saved EQ nodes without an EqBands field retain their established five-band default.");
        legacy.EqBands[0] = new() { Type = "notch", FrequencyHz = 243, ResetFrequencyHz = 180, Q = 3.7, Enabled = false };
        legacy.EqBands[2].FrequencyHz = 1740; legacy.EqBands[2].GainDb = -5.25; legacy.EqBands[2].Q = 1.8;
        var saved = JsonSerializer.Deserialize<GraphNode>(Json(legacy), Protocol.Json)!;
        require(Json(saved.EqBands) == Json(legacy.EqBands),
            "Loading an existing populated EQ preserves every band, shape, enable state, anchor, frequency, gain and width.");
        require(Json(GraphDefaults.NewNode("tone", 0, 0).EqBands) == Json(EqBand.ToneDefaults())
            && Json(GraphDefaults.NewNode("highPass", 0, 0).EqBands) == Json(EqBand.FilterDefaults("highPass"))
            && Json(GraphDefaults.NewNode("lowPass", 0, 0).EqBands) == Json(EqBand.FilterDefaults("lowPass")),
            "The empty EQ creation default leaves Tone's three bands and both dedicated pass filters unchanged.");
        GraphDefaults.ResetNodeSettings(empty);
        require(empty.EqBands.Count == 0,
            "Reset node settings keeps an already-empty EQ empty instead of restoring old factory bands.");

        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        string id = workspace.Spawn("eq", new Point(120, 80)); await layout();
        GraphNode Current() => mixer.GraphConfig.Nodes.Single(node => node.Id == id);
        GraphNodeView View() => Descendants<GraphNodeView>(workspace).Single(node => node.Id == id);
        EqualizerEditor Editor() => Descendants<EqualizerEditor>(workspace).Single();
        require(id.Length > 0 && Current().EqBands.Count == 0 && Editor().BandCount == 0
            && Editor().SelectedBandIndex == -1
            && Descendants<RotaryValueControl>(workspace).All(dial => !dial.IsEnabled),
            "The real workspace creation action presents a zero-band EQ with no selection and inactive band dials.");
        render?.Invoke(View(), "eq-new-empty-node.png");
        require(Enumerable.Range(0, 101).All(index => Math.Abs(EqualizerEditor.CombinedResponseDb(Current().EqBands,
            false, 20 * Math.Pow(1000, index / 100d))) < .000001),
            "An empty EQ has a flat response across the complete 20 Hz–20 kHz plot.");
        require(mixer.Undo() && mixer.GraphConfig.Nodes.Count == 0 && mixer.Redo() && Current().EqBands.Count == 0,
            "Undo removes a newly created empty EQ and Redo restores that same node with zero bands.");
        await layout();

        string beforeAdd = Json(mixer.GraphConfig);
        int beforeEntries = mixer.EditHistory.Entries.Count;
        require(Editor().AddBandAt(725, 4) && Current().EqBands.Count == 1
            && Current().EqBands[0] is { Type: "peak", Enabled: true, FrequencyHz: 725, GainDb: 4, Q: 1, ResetFrequencyHz: 725 }
            && Editor().SelectedBandIndex == 0
            && Descendants<RotaryValueControl>(workspace).All(dial => dial.IsEnabled)
            && mixer.EditHistory.Entries.Count == beforeEntries + 1,
            "Adding the first actual plot band selects it, enables its dials and saves exactly one correctly anchored bell band in one edit.");
        await layout();
        render?.Invoke(View(), "eq-first-band-node.png");
        string firstBand = Json(mixer.GraphConfig);
        var firstLoaded = JsonSerializer.Deserialize<GraphConfiguration>(firstBand, Protocol.Json)!;
        require(Json(firstLoaded) == firstBand && firstLoaded.Nodes.Single().EqBands.Count == 1,
            "The first user-added band survives graph serialization without inserting factory bands.");
        CheckClipboard(mixer.GraphConfig, 1, require);
        require(mixer.Undo() && Json(mixer.GraphConfig) == beforeAdd && Editor().BandCount == 0
            && mixer.Redo() && Json(mixer.GraphConfig) == firstBand && Editor().BandCount == 1,
            "Undoing the first band returns to the exact empty EQ; Redo restores its one edited band without defaults.");
        Editor().SelectBand(0);
        require(Editor().RemoveSelectedBand() && Current().EqBands.Count == 0 && Editor().BandCount == 0,
            "Removing the final band returns to an empty EQ rather than repopulating defaults.");

        require(Editor().AddBandAt(730, 5) && Editor().AddBandAt(2800, -4) && Editor().SetBandShape(1, "notch"),
            "Both reset actions use a meaningful custom two-band EQ fixture.");
        string beforeReset = Json(mixer.GraphConfig);
        int resetEntries = mixer.EditHistory.Entries.Count;
        var menu = Editor().BuildBandMenu(-1, new Point(100, 100));
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Reset EQ to flat"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(!menu.IsOpen && Current().EqBands.Count == 0 && Editor().BandCount == 0
            && Editor().SelectedBandIndex == -1 && Descendants<RotaryValueControl>(workspace).All(dial => !dial.IsEnabled)
            && mixer.EditHistory.Entries.Count == resetEntries + 1,
            "Reset EQ to flat removes all custom bands in one edit, clears selection and disables the band dials.");
        string cleared = Json(mixer.GraphConfig);
        require(Enumerable.Range(0, 101).All(index => Math.Abs(EqualizerEditor.CombinedResponseDb(Current().EqBands,
            false, 20 * Math.Pow(1000, index / 100d))) < .000001),
            "The reset EQ has a truly flat 20 Hz–20 kHz response with no filter bands.");
        require(mixer.Undo() && Json(mixer.GraphConfig) == beforeReset
            && mixer.Redo() && Json(mixer.GraphConfig) == cleared,
            "Undo restores both exact custom EQ bands and Redo removes them again as one transaction.");
        long noOpPresentations = mixer.ConfigurationPresentationCount;
        int noOpEntries = mixer.EditHistory.Entries.Count;
        require(!Editor().ResetEq() && Json(mixer.GraphConfig) == cleared
            && mixer.ConfigurationPresentationCount == noOpPresentations && mixer.EditHistory.Entries.Count == noOpEntries,
            "Resetting an already-empty EQ is a no-op with no settings dispatch or history entry.");
        require(mixer.Undo() && Json(mixer.GraphConfig) == beforeReset,
            "The custom EQ is restored before checking the separate node reset action.");
        workspace.ResetNode(id);
        require(Current().EqBands.Count == 0 && Editor().BandCount == 0 && Editor().SelectedBandIndex == -1
            && Descendants<RotaryValueControl>(workspace).All(dial => !dial.IsEnabled)
            && Json(mixer.GraphConfig) == cleared && mixer.Undo() && Json(mixer.GraphConfig) == beforeReset
            && mixer.Redo() && Json(mixer.GraphConfig) == cleared,
            "The separate workspace Reset node action clears the custom EQ, disables its band dials and supports exact Undo/Redo.");

        // Reload through the real preview model after history checks so this
        // verifies that presentation refresh does not repopulate empty settings.
        fixture.Configuration.Graph = JsonSerializer.Deserialize<GraphConfiguration>(cleared, Protocol.Json)!;
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        require(Current().EqBands.Count == 0 && Editor().BandCount == 0 && Editor().SelectedBandIndex == -1,
            "Reloading a saved empty EQ through the complete workspace keeps its editor empty and unselected.");
        for (int band = 0; band < EqualizerEditor.MaximumBands; band++) Editor().AddBandAt(100 + band * 300, 1);
        require(!Editor().AddBandAt(6000, 1), "The maximum-band warning is active before checking reset feedback.");
        workspace.ResetNode(id);
        require(Current().EqBands.Count == 0 && Editor().ToolTip is StackPanel help
            && help.Children.OfType<TextBlock>().First().Text == "Equalizer",
            "Reset node clears the old maximum-band warning as well as the bands, restoring ordinary empty-EQ help.");
    }

    private static void CheckClipboard(GraphConfiguration graph, int bands, Action<bool, string> require)
    {
        string before = Json(graph);
        string payload = GraphClipboard.Serialize(graph, graph.Nodes.Select(node => node.Id));
        require(GraphClipboard.TryCreatePaste(payload, new(), new Point(400, 80), out var pasted, out _)
            && pasted.Nodes.Count == 1 && pasted.Nodes[0].EqBands.Count == bands
            && Json(pasted.Nodes[0].EqBands) == Json(graph.Nodes[0].EqBands) && Json(graph) == before,
            "Clipboard round-trip preserves the EQ's exact " + bands + "-band state without changing the original node.");
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
