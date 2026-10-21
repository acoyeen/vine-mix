using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

// Real detached controls, model persistence and undo. Never opens audio devices.
internal static class DynamicsOrganizationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Dynamics organization is checked in a detached workspace without live audio.");
        var fixture = UiPreview.CreateSnapshot([]);
        var expander = GraphDefaults.NewNode("expander", 20, 20);
        var gate = GraphDefaults.NewNode("gate", 500, 20);
        var compressor = GraphDefaults.NewNode("compressor", 1000, 20);
        var duck = GraphDefaults.NewNode("ducking", 1500, 20);
        var trigger = GraphDefaults.NewNode("mix", 20, 600);
        fixture.Configuration.Graph = new() { Nodes = [expander, gate, compressor, duck, trigger], Zoom = .75 };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNode Node(string id) => mixer.GraphConfig.Nodes.Single(node => node.Id == id);
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        var expanderView = View(expander.Id); var gateView = View(gate.Id);
        var compressorView = View(compressor.Id); var duckView = View(duck.Id);
        require(NodeCatalog.CanCreate("expander") && NodeCatalog.CanCreate("gate")
            && NodeCatalog.Title("expander") == "Expander" && NodeCatalog.Title("gate") == "Gate"
            && NodeCatalog.MatchesSearch("gate", "gate") && !NodeCatalog.MatchesSearch("expander", "gate"),
            "Expander and Gate have separate, directly searchable creation entries.");
        require(VisibleLabels(expanderView).ToHashSet().SetEquals(["Threshold", "Ratio", "Attack", "Release", "Range"])
            && VisibleLabels(gateView).ToHashSet().SetEquals(["Threshold", "Attack", "Hold", "Release", "Range", "Hysteresis"])
            && !Descendants<ComboBox>(expanderView).Any() && !Descendants<ComboBox>(gateView).Any(),
            "Expander has five focused controls and Gate has six, with no combined mode selector.");
        require(Node(gate.Id).Expander.Mode == "gate" && Node(expander.Id).Expander.Mode == "expander",
            "Each distinct node creates the intended processor mode.");
        require(compressorView.FindSocket("sidechain", false) is null && !Descendants<ComboBox>(compressorView).Any()
            && GraphDefaults.ConnectionError(mixer.GraphConfig, new() { From = trigger.Id, To = compressor.Id, Kind = "sidechain" }).Length > 0,
            "Compressor has no external detector selector or socket and rejects trigger cables.");
        require(workspace.ConnectNodes(trigger.Id, duck.Id, "sidechain").Length == 0,
            "Ducking alone exposes a valid detector-only trigger connection.");
        await layout();
        var choice = Descendants<ComboBox>(duckView).Single(combo => AutomationProperties.GetName(combo) == "Ducking mode");
        void Select(string mode) => choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, mode));
        require(Node(duck.Id).Ducking.Mode == "static" && VisibleLabels(duckView).ToHashSet().SetEquals(["Threshold", "Duck amount", "Attack", "Release"]),
            "Existing/default Ducking opens in Static mode with a fixed attenuation control.");
        var size = duckView.RenderSize;
        var sockets = SocketPoints(duckView);
        workspace.NodeChanged(duck.Id, node => { node.Ducking.AmountDb = 23.5; node.Ducking.Ratio = 5.25; });
        await layout();
        string before = Json(mixer.GraphConfig);
        Select("dynamic"); await layout();
        require(Node(duck.Id).Ducking.Mode == "dynamic" && Node(duck.Id).Ducking.AmountDb == 23.5
            && Node(duck.Id).Ducking.Ratio == 5.25 && VisibleLabels(duckView).ToHashSet().SetEquals(["Threshold", "Ratio", "Attack", "Release"]),
            "Dynamic Ducking swaps Amount for Ratio while retaining both mode-specific values.");
        string dynamic = Json(mixer.GraphConfig);
        require(mixer.Undo() && Json(mixer.GraphConfig) == before && mixer.Redo() && Json(mixer.GraphConfig) == dynamic,
            "Ducking mode selection is one reversible configuration edit.");
        await layout();
        require(duckView.RenderSize == size && SocketPoints(duckView).Zip(sockets).All(pair => (pair.First - pair.Second).Length < .001),
            "Changing Ducking mode leaves its node dimensions and all cable anchors fixed.");
        var loaded = JsonSerializer.Deserialize<GraphConfiguration>(dynamic, Protocol.Json)!;
        require(GraphDefaults.Validate(loaded).Length == 0 && Json(loaded) == dynamic,
            "Gate identity and Dynamic Ducking settings survive an exact configuration round trip.");
        string payload = GraphClipboard.Serialize(mixer.GraphConfig, mixer.GraphConfig.Nodes.Select(node => node.Id));
        require(GraphClipboard.TryCreatePaste(payload, new(), new Point(20, 20), out var paste, out _)
            && paste.Nodes.Single(node => node.Kind == "gate").Expander.Mode == "gate"
            && paste.Nodes.Single(node => node.Kind == "ducking").Ducking is { Mode: "dynamic", Ratio: 5.25, AmountDb: 23.5 }
            && paste.Edges.Single().Kind == "sidechain",
            "Copy/paste preserves separate Gate identity, both Ducking mode values and detector-only routing.");
        render(expanderView, "expander-dedicated.png"); render(gateView, "gate-dedicated.png");
        render(compressorView, "compressor-main-only.png"); render(duckView, "ducking-dynamic.png");
        Select("static"); await layout(); render(duckView, "ducking-static.png");
        require(Node(duck.Id).Ducking.Ratio == 5.25 && Node(duck.Id).Ducking.AmountDb == 23.5,
            "Returning to Static Ducking does not overwrite the saved ratio or amount.");
        workspace.NodeChanged(gate.Id, node => { node.Expander.HoldMs = 121; node.Expander.ThresholdDb = -32; });
        string gateEdited = Json(Node(gate.Id));
        workspace.ResetNode(gate.Id);
        require(Node(gate.Id).Kind == "gate" && Node(gate.Id).Expander.Mode == "gate"
            && Node(gate.Id).Expander.HoldMs == 50 && mixer.Undo() && Json(Node(gate.Id)) == gateEdited,
            "Resetting a Gate retains its dedicated identity and can undo to its exact edited settings.");

        // Earlier combined nodes retain their saved mode without rewriting their identity.
        workspace.NodeChanged(expander.Id, node => { node.Expander.Mode = "gate"; node.Expander.HoldMs = 178; });
        await layout();
        require(VisibleLabels(View(expander.Id)).Contains("Hold") && !VisibleLabels(View(expander.Id)).Contains("Ratio"),
            "An earlier expander-kind Gate receives the dedicated Gate controls for its stored mode.");
        workspace.ResetNode(expander.Id);
        require(Node(expander.Id).Kind == "expander" && Node(expander.Id).Expander.Mode == "gate",
            "Resetting an earlier combined Gate does not turn it into an Expander.");
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); await layout();
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static Point[] SocketPoints(GraphNodeView view) => Descendants<GraphWorkspace.Socket>(view)
        .Select(socket => socket.TranslatePoint(new(socket.ActualWidth / 2, socket.ActualHeight / 2), view)).ToArray();
    private static string[] VisibleLabels(GraphNodeView view) => Descendants<RotaryValueControl>(view)
        .Where(control => Visible(control, view)).Select(control => AutomationProperties.GetName(Descendants<Slider>(control).Single())).ToArray();
    private static bool Visible(DependencyObject item, DependencyObject root)
    {
        for (DependencyObject? current = item; current is not null; current = VisualTreeHelper.GetParent(current))
        { if (current is UIElement element && element.Visibility != Visibility.Visible) return false; if (ReferenceEquals(current, root)) return true; }
        return false;
    }
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
