using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VineMix.Desktop;

/// <summary>Named channel routing, persistence and guarded editing, using synthetic graphs only.</summary>
internal static class MixingRoutingVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout, Action<bool, string> require)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        var split = GraphDefaults.NewNode("stereoSplit", 50, 80); split.Id = "mixing-split";
        var combine = GraphDefaults.NewNode("stereoCombine", 450, 80); combine.Id = "mixing-combine";
        var fade = GraphDefaults.NewNode("crossfade", 850, 80); fade.Id = "mixing-fade";
        fixture.Configuration.Graph = new() { Nodes = [split, combine, fade], Zoom = 1 };
        mixer.IsLayoutLocked = false; mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNodeView View(string id) => Descendants(workspace).OfType<GraphNodeView>().Single(v => v.Id == id);
        Point Screen(GraphWorkspace.Socket socket)
        {
            var point = workspace.SocketPosition(socket);
            return new(point.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX, point.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
        }
        foreach (string port in new[] { "left", "right" })
        {
            var output = View(split.Id).FindSocket("audio", true, port)!;
            var input = View(combine.Id).FindSocket("audio", false, port)!;
            require(ReferenceEquals(workspace.FindSnapSocket(Screen(input), output), input), "Channel sockets snap to their exact named destination.");
            workspace.BeginConnectionPreview(output, Screen(output), dragging: true);
            workspace.UpdateConnectionPointer(Screen(input));
            workspace.FinishConnectionPointer(MouseButton.Left, Screen(input)); await layout();
            require(mixer.GraphConfig.Edges.Any(e => e.From == split.Id && e.FromPort == port && e.To == combine.Id && e.ToPort == port), "Cable gestures persist the selected source and destination channels.");
            require(View(split.Id).FindSocket("audio", true, port)!.IsConnected && View(combine.Id).FindSocket("audio", false, port)!.IsConnected,
                "Connected state fills the selected channel sockets.");
        }
        var left = View(combine.Id).FindSocket("audio", false, "left")!;
        var right = View(combine.Id).FindSocket("audio", false, "right")!;
        var edgeId = mixer.GraphConfig.Edges.Single(e => e.ToPort == "left").Id;
        require(workspace.BeginInputLinkDrag(left, Screen(left), edgeId), "Dragging a named input selects its own cable.");
        workspace.UpdateConnectionPointer(Screen(right)); workspace.FinishConnectionPointer(MouseButton.Left, Screen(right)); await layout();
        require(mixer.GraphConfig.Edges.Single(e => e.Id == edgeId).ToPort == "right"
            && !View(combine.Id).FindSocket("audio", false, "left")!.IsConnected, "A same-node channel rewire updates its endpoint and socket fill.");
        require(workspace.ConnectNodes(split.Id, fade.Id, "audio", "left", "a") == ""
            && workspace.ConnectNodes(split.Id, fade.Id, "audio", "right", "b") == "", "The workspace connection action supports both Crossfade inputs.");
        await layout();
        require(View(fade.Id).FindSocket("audio", false, "a")!.IsConnected && View(fade.Id).FindSocket("audio", false, "b")!.IsConnected,
            "Crossfade input sockets independently display their connections.");
        workspace.CancelActiveGesture();
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); ++index)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    internal static void Run(Action<bool, string> require)
    {
        static GraphNode Node(string kind, string id) { var node = GraphDefaults.NewNode(kind, 0, 0); node.Id = id; return node; }
        static GraphConfiguration Fixture() => new()
        {
            Nodes = [Node("microphone", "source"), Node("stereoSplit", "split"), Node("stereoCombine", "combine"), Node("stereoWidth", "width"), Node("crossfade", "fade"), Node("ducking", "duck"), Node("output", "out")],
            Edges = [new() { Id = "source", From = "source", To = "split" },
                new() { Id = "left", From = "split", FromPort = "left", To = "combine", ToPort = "left" },
                new() { Id = "right", From = "split", FromPort = "right", To = "combine", ToPort = "right" },
                new() { Id = "combined", From = "combine", To = "out" }]
        };
        var graph = Fixture();
        require(GraphDefaults.Validate(graph) == "", "Distinct split outputs can connect to distinct combine inputs on the same node pair.");
        foreach (var edge in graph.Edges)
            require(WorkspaceHistory.SameValue(edge, edge.Copy()), "Connection copies preserve named source and destination ports.");
        var clone = JsonSerializer.Deserialize<GraphConfiguration>(JsonSerializer.Serialize(graph, Protocol.Json), Protocol.Json)!;
        require(WorkspaceHistory.SameContents(graph, clone) && GraphDefaults.Validate(clone) == "", "Named channel routes survive engine-protocol JSON round trips.");
        var invalid = graph.Copy(); invalid.Edges[1].FromPort = "audio";
        require(GraphDefaults.Validate(invalid).Length > 0, "A splitter cannot silently use a nonexistent ordinary output.");
        invalid = graph.Copy(); invalid.Edges[1].ToPort = "audio";
        require(GraphDefaults.Validate(invalid).Length > 0, "A combiner requires an explicit left or right destination.");
        invalid = graph.Copy(); invalid.Edges.Add(graph.Edges[1].Copy()); invalid.Edges[^1].Id = "duplicate";
        require(GraphDefaults.Validate(invalid).Length > 0, "Repeated connections between the exact same named sockets are rejected.");
        invalid = graph.Copy(); invalid.Edges.Add(new() { Id = "wrong-trigger", From = "split", FromPort = "other", To = "duck", Kind = "sidechain" });
        require(GraphDefaults.Validate(invalid).Length > 0, "Unknown split outputs cannot be used as sidechain sources.");
        graph.Edges.Add(new() { Id = "trigger", From = "split", FromPort = "right", To = "duck", Kind = "sidechain" });
        require(GraphDefaults.Validate(graph) == "", "An extracted channel can independently drive a Ducking trigger.");
        graph.Edges.Add(new() { Id = "a", From = "source", To = "fade", ToPort = "a" });
        graph.Edges.Add(new() { Id = "b", From = "source", To = "fade", ToPort = "b" });
        require(GraphDefaults.Validate(graph) == "", "One source can feed both Crossfade inputs without false duplicate rejection.");

        var source = graph.Edges.Single(e => e.Id == "left");
        require(GraphConnectionEditing.TryPrepareRewire(graph, source, "combine", out var rewire, out _, "right")
            && GraphConnectionEditing.TryApply(graph, rewire!, out _) && graph.Edges.Single(e => e.Id == "left").ToPort == "right",
            "Rewiring to another socket on the same node changes the destination port while retaining the source port.");
        var applied = graph.Copy();
        require(GraphConnectionEditing.TryApply(graph, rewire!, out _) && WorkspaceHistory.SameContents(graph, applied), "Named-port rewiring can replay safely.");
        graph = Fixture();
        require(GraphConnectionEditing.TryPrepareRewire(graph, graph.Edges[1], "fade", out rewire, out _, "a"), "Prepare a named-port rewire for conflict verification.");
        graph.Edges[1].FromPort = "right";
        require(!GraphConnectionEditing.TryApply(graph, rewire!, out _), "Changing only a source port invalidates a stale rewire instead of overwriting the new route.");

        graph = Fixture();
        require(GraphConnectionEditing.TryPrepareInsertion(graph, "width", "left", out var insert, out _)
            && GraphConnectionEditing.TryApply(graph, insert!, out _)
            && graph.Edges.Single(e => e.To == "width").FromPort == "left"
            && graph.Edges.Single(e => e.From == "width").ToPort == "left", "Inserting a single-input effect preserves named sockets at both ends.");
        require(GraphConnectionEditing.TryPrepareDetach(graph, "width", out var detach, out _) && GraphConnectionEditing.TryApply(graph, detach!, out _)
            && graph.Edges.Any(e => e.From == "split" && e.FromPort == "left" && e.To == "combine" && e.ToPort == "left"), "Detaching an effect reconstructs the original channel route.");
        foreach (string kind in new[] { "stereoSplit", "stereoCombine", "crossfade" })
            require(!GraphConnectionEditing.IsInsertable(Node(kind, "multi")), "Multiport nodes require explicit wiring instead of ambiguous automatic insertion or detach.");

        graph = Fixture();
        string clipboard = GraphClipboard.Serialize(graph, graph.Nodes.Select(n => n.Id));
        require(GraphClipboard.TryCreatePaste(clipboard, new(), new Point(30, 40), out var pasted, out _)
            && pasted.Edges.Count(e => e.FromPort is "left" or "right") == 2
            && pasted.Edges.Count(e => e.ToPort is "left" or "right") == 2, "Copied node groups retain named channel sockets after node IDs are remapped.");
        var legacy = new GraphConfiguration { Nodes = [Node("microphone", "mic"), Node("mix", "mix")], Edges = [new() { Id = "legacy", From = "mic", To = "mix" }] };
        var oldClipboard = JsonNode.Parse(GraphClipboard.Serialize(legacy, ["mic", "mix"]))!;
        foreach (var node in oldClipboard["nodes"]!.AsArray())
            foreach (string field in new[] { "stereoWidth", "panPosition", "crossfadePosition", "crossfadeCurve" }) node!.AsObject().Remove(field);
        foreach (var edge in oldClipboard["edges"]!.AsArray()) { edge!.AsObject().Remove("fromPort"); edge.AsObject().Remove("toPort"); }
        require(GraphClipboard.TryCreatePaste(oldClipboard.ToJsonString(), new(), new Point(), out pasted, out _)
            && pasted.Edges[0].FromPort == "audio" && pasted.Edges[0].ToPort == "audio" && pasted.Nodes.All(n => n.StereoWidth == 1), "Earlier clipboard groups load with ordinary sockets and neutral mixing defaults.");

        var before = Fixture(); var after = before.Copy(); after.Edges[1].ToPort = "right";
        require(WorkspaceHistory.ChangesLayout(before, after), "Changing only a channel socket is a layout edit protected by layout locking.");
        var undo = new WorkspaceGraphRestore(after, before);
        require(undo.TryApply(after, out var restored, out _) && WorkspaceHistory.SameContents(before, restored), "Undo restores exact named channel routing.");
        foreach (var change in new Action<GraphNode>[] { n => n.StereoWidth = 2.1, n => n.PanPosition = double.NaN, n => n.CrossfadePosition = -1, n => n.CrossfadeCurve = "unknown" })
        {
            invalid = Fixture(); change(invalid.Nodes[3]);
            require(GraphDefaults.Validate(invalid).Length > 0, "Out-of-range or unknown mixing parameters are rejected before submission.");
        }
        var defaults = Node("crossfade", "defaults"); defaults.StereoWidth = 2; defaults.PanPosition = 1; defaults.CrossfadePosition = .8; defaults.CrossfadeCurve = "linear";
        GraphDefaults.ResetNodeSettings(defaults);
        require(defaults.StereoWidth == 1 && defaults.PanPosition == 0 && defaults.CrossfadePosition == 0 && defaults.CrossfadeCurve == "equalPower", "Reset restores neutral width, centred pan, and Crossfade A with its default curve.");
        var route = GraphWorkspace.DescribeConnection(before, before.Edges[1]);
        require(route.Route.Contains("Left") && !route.Route.Contains("Right"), "Cable descriptions identify named channel sockets.");
    }
}
