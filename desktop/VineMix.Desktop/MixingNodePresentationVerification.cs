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

/// <summary>Detached mixing-node controls and socket checks; no audio, windows or live settings.</summary>
internal static class MixingNodePresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> renderNode)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Mixing-node presentation uses a detached preview without touching the running engine.");
        string[] kinds = ["channelSwap", "stereoWidth", "stereoPan", "crossfade", "stereoCombine", "stereoSplit"];
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new() { Nodes = kinds.Select((kind, i) => GraphDefaults.NewNode(kind, i * 400, 50)).ToList(), Zoom = 1 };
        var graph = fixture.Configuration.Graph;
        GraphNode Node(string kind) => graph.Nodes.Single(n => n.Kind == kind);
        graph.Edges = [
            new() { Id = "split-left", From = Node("stereoSplit").Id, FromPort = "left", To = Node("stereoCombine").Id, ToPort = "left" },
            new() { Id = "split-right", From = Node("stereoSplit").Id, FromPort = "right", To = Node("stereoCombine").Id, ToPort = "right" },
            new() { Id = "blend-a", From = Node("stereoCombine").Id, To = Node("crossfade").Id, ToPort = "a" }
        ];
        fixture.Status.Nodes = graph.Nodes.Select(n => new GraphNodeStatus { Id = n.Id, PeakLeft = .3f, PeakRight = .2f, Channels = 2, Path = new() { State = "Running" } }).ToList();
        require(GraphDefaults.Validate(graph).Length == 0, "A split/recombine route can connect the same two nodes through different named sockets.");
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        var views = Descendants<GraphNodeView>(workspace).ToDictionary(v => v.Id);
        GraphNodeView View(string kind) => views[Node(kind).Id];
        foreach (string kind in kinds)
        {
            var view = View(kind);
            require(NodeCatalog.CategoryFor(kind).Title == "Mixing" && NodeCatalog.CanCreate(kind)
                && view.HeaderBrushKey == "NodeUtilityHeaderBrush",
                kind + " appears in Mixing and shares the existing mixing-node colour.");
            require(Descendants<Button>(view).Count(b => AutomationProperties.GetName(b) == "Bypass")
                == (kind is "stereoSplit" or "stereoCombine" ? 0 : 1),
                kind + " has the intended bypass presentation; structural channel routing stays explicit.");
            require(Descendants<GraphWorkspace.Socket>(view).All(s => s.Width == 24 && s.Height == 20),
                kind + " retains the established socket targets.");
            renderNode(view, "mixing-" + kind + ".png");
        }
        var split = View("stereoSplit"); var combine = View("stereoCombine"); var crossfade = View("crossfade");
        var left = split.FindSocket("audio", true, "left"); var right = split.FindSocket("audio", true, "right");
        require(left is { IsConnected: true } && right is { IsConnected: true } && split.FindSocket("audio", true) is null
            && Math.Abs(left.TranslatePoint(new Point(), split).Y - right.TranslatePoint(new Point(), split).Y) >= 27,
            "Stereo Split exposes two independently connected, vertically separated mono outputs with no ambiguous generic output.");
        require(combine.FindSocket("audio", false, "left") is { IsConnected: true }
            && combine.FindSocket("audio", false, "right") is { IsConnected: true } && combine.FindSocket("audio", false) is null,
            "Stereo Combine exposes two independently connected channel inputs with no generic input.");
        require(crossfade.FindSocket("audio", false, "a") is { IsConnected: true }
            && crossfade.FindSocket("audio", false, "b") is { IsConnected: false },
            "Crossfade only fills the socket with a connected cable; the other stereo input remains empty.");
        var before = mixer.GraphConfig.Copy();
        var expected = before.Copy();
        expected.Nodes.Single(n => n.Kind == "stereoWidth").StereoWidth = 1.35;
        expected.Nodes.Single(n => n.Kind == "stereoPan").PanPosition = -.45;
        expected.Nodes.Single(n => n.Kind == "crossfade").CrossfadePosition = .65;
        expected.Nodes.Single(n => n.Kind == "crossfade").CrossfadeCurve = "linear";
        Descendants<Slider>(View("stereoWidth")).Single(s => AutomationProperties.GetName(s) == "Width").Value = 135;
        Descendants<Slider>(View("stereoPan")).Single(s => AutomationProperties.GetName(s) == "L / R").Value = -.45;
        Descendants<Slider>(crossfade).Single(s => AutomationProperties.GetName(s) == "A / B").Value = 65;
        var curve = Descendants<ComboBox>(crossfade).Single(c => AutomationProperties.GetName(c) == "Crossfade curve");
        curve.SelectedItem = curve.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "linear"));
        require(Json(mixer.GraphConfig) == Json(expected), "Width, Pan, blend and fade-curve controls edit only their intended parameters, preserving both split routes.");
        await layout();
        var size = crossfade.RenderSize;
        var positions = Descendants<GraphWorkspace.Socket>(crossfade).Select(s => s.TranslatePoint(new Point(), crossfade)).ToArray();
        var bypass = Descendants<Button>(crossfade).Single(b => AutomationProperties.GetName(b) == "Bypass");
        bypass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(mixer.GraphConfig.Nodes.Single(n => n.Kind == "crossfade").Bypass
            && NodeCatalog.Description("crossfade").Contains("Bypass plays A only", StringComparison.Ordinal),
            "Crossfade exposes its explicit A-only bypass and changes the saved bypass state.");
        curve.SelectedItem = curve.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "equalPower"));
        await layout();
        require(crossfade.RenderSize == size && positions.SequenceEqual(Descendants<GraphWorkspace.Socket>(crossfade).Select(s => s.TranslatePoint(new Point(), crossfade))),
            "Crossfade curve and bypass changes preserve node size and every socket position.");
        foreach (string kind in kinds) workspace.ResetNode(Node(kind).Id);
        require(mixer.GraphConfig.Nodes.Single(n => n.Kind == "stereoWidth").StereoWidth == 1
            && mixer.GraphConfig.Nodes.Single(n => n.Kind == "stereoPan").PanPosition == 0
            && mixer.GraphConfig.Nodes.Single(n => n.Kind == "crossfade") is { CrossfadePosition: 0, CrossfadeCurve: "equalPower", Bypass: false }
            && Json(mixer.GraphConfig.Edges) == Json(before.Edges),
            "Reset returns mixing controls to their defaults without changing named connections.");
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
