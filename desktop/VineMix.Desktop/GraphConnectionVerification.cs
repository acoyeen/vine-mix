using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>In-memory cable edits only: no engine, window, clipboard or input injection.</summary>
internal static class GraphConnectionVerification
{
    internal static void Run(Action<bool, string> require)
    {
        VerifyDetectorRouting(require);
        GraphConfiguration Fixture(string kind = "gain") => new()
        {
            Nodes = [Node("microphone", "source"), Node(kind, "middle"), Node("output", "a"), Node("virtualOutput", "b")],
            Edges = [Edge("in", "source", "middle", -3), Edge("out-a", "middle", "a", -2), Edge("out-b", "middle", "b", -7, true)]
        };
        var graph = Fixture(); graph.Nodes[1].GainDb = -4;
        string before = JsonSerializer.Serialize(graph);
        require(GraphConnectionEditing.TryPrepareDetach(graph, "middle", out var detach, out _), "A single-input processing node can detach while keeping both output branches.");
        require(JsonSerializer.Serialize(graph) == before, "Preparing a detach preview never mutates audio connections.");
        require(GraphConnectionEditing.TryApply(graph, detach!, out _) && graph.Nodes.Count == 4 && graph.Edges.Count == 2,
            "Detach keeps the node and replaces its incident cables atomically.");
        require(graph.Edges.Single(e => e.To == "a").GainDb == -5 && !graph.Edges.Single(e => e.To == "a").Muted
            && graph.Edges.Single(e => e.To == "b").GainDb == -10 && graph.Edges.Single(e => e.To == "b").Muted
            && graph.Nodes[1].GainDb == -4,
            "Detach preserves each branch's cable attenuation/mute while the node keeps its own settings.");
        string applied = JsonSerializer.Serialize(graph);
        require(GraphConnectionEditing.TryApply(graph, detach!, out _) && JsonSerializer.Serialize(graph) == applied,
            "Replaying a detach acknowledgement does not duplicate cables or alter gains.");
        graph = Fixture(); graph.Nodes[1].Muted = true;
        require(GraphConnectionEditing.TryPrepareDetach(graph, "middle", out detach, out _) && GraphConnectionEditing.TryApply(graph, detach!, out _)
            && !graph.Edges.Single(e => e.To == "a").Muted && graph.Nodes[1].Muted,
            "The detached node's mute leaves the path with that node; it is not baked into hidden cable controls.");
        graph = Fixture(); graph.Edges.RemoveAll(e => e.To == "b"); graph.Nodes[1].GainDb = -6; graph.Nodes[1].Muted = true;
        for (int pass = 0; pass < 3; pass++)
        {
            require(GraphConnectionEditing.TryPrepareDetach(graph, "middle", out detach, out _) && GraphConnectionEditing.TryApply(graph, detach!, out _)
                && graph.Edges.Count == 1 && graph.Edges[0].GainDb == -5 && !graph.Edges[0].Muted,
                "Repeated detach preserves cable attenuation without accumulating the node's own gain or mute.");
            require(GraphConnectionEditing.TryPrepareInsertion(graph, "middle", graph.Edges[0].Id, out var reinsert, out _)
                && GraphConnectionEditing.TryApply(graph, reinsert!, out _) && graph.Edges.Sum(e => e.GainDb) == -5 && graph.Edges.All(e => !e.Muted)
                && graph.Nodes[1].GainDb == -6 && graph.Nodes[1].Muted,
                "Repeated reinsertion restores the node's original gain/mute exactly once without sticky cable mute.");
        }

        void RejectDetach(GraphConfiguration invalid, string description)
        {
            string original = JsonSerializer.Serialize(invalid);
            require(!GraphConnectionEditing.TryPrepareDetach(invalid, "middle", out _, out string error) && error.Length > 0
                && JsonSerializer.Serialize(invalid) == original, description);
        }
        graph = Fixture("mix"); graph.Nodes.Add(Node("virtualInput", "other")); graph.Edges.Add(Edge("other-in", "other", "middle"));
        RejectDetach(graph, "Ambiguous multiple-input mix detachment reports a reason and leaves every edge unchanged.");
        graph = Fixture(); graph.Nodes.Add(Node("control", "controller")); graph.Edges.Add(new() { Id = "control", From = "controller", To = "middle", Kind = "control" });
        RejectDetach(graph, "Linked control detachment is rejected without silently losing the control connection.");
        graph = Fixture(); graph.Edges.Add(Edge("existing", "source", "a"));
        RejectDetach(graph, "Detach cannot introduce duplicate parallel cables or merge their different gains.");
        graph = Fixture(); graph.Edges[0].GainDb = -60;
        RejectDetach(graph, "Unrepresentable attenuation is rejected instead of clamping upward and increasing the level.");
        graph = Fixture(); require(GraphConnectionEditing.TryPrepareDetach(graph, "middle", out detach, out _), "Prepare a concurrent-change fixture.");
        graph.Edges[0].GainDb = -1; before = JsonSerializer.Serialize(graph);
        require(!GraphConnectionEditing.TryApply(graph, detach!, out _) && JsonSerializer.Serialize(graph) == before,
            "An incident cable-level change invalidates a pending detach without a partial edge mutation.");

        foreach (string kind in GraphDefaults.Kinds.Where(k => GraphConnectionEditing.IsInsertable(GraphDefaults.NewNode(k, 0, 0))))
        {
            graph = Fixture(kind); graph.Edges = [Edge("original", "source", "a", -11, true)];
            before = JsonSerializer.Serialize(graph);
            require(GraphConnectionEditing.TryPrepareInsertion(graph, "middle", "original", out var insert, out _) && JsonSerializer.Serialize(graph) == before,
                $"{GraphDefaults.KindLabel(kind)} prepares insertion without mutating the live route.");
            require(GraphConnectionEditing.TryApply(graph, insert!, out _) && graph.Edges.Count == 2 && GraphDefaults.Validate(graph).Length == 0
                && graph.Edges.Single(e => e.To == "middle").GainDb == -11 && graph.Edges.Single(e => e.To == "middle").Muted
                && graph.Edges.Single(e => e.From == "middle").GainDb == 0 && !graph.Edges.Single(e => e.From == "middle").Muted,
                $"{GraphDefaults.KindLabel(kind)} insertion keeps the original cable level/mute once and uses unity for the second segment.");
            applied = JsonSerializer.Serialize(graph);
            require(GraphConnectionEditing.TryApply(graph, insert!, out _) && JsonSerializer.Serialize(graph) == applied,
                $"{GraphDefaults.KindLabel(kind)} insertion replays without duplicate connections.");
        }
        graph = Fixture(); before = JsonSerializer.Serialize(graph);
        require(!GraphConnectionEditing.TryPrepareInsertion(graph, "middle", "out-a", out _, out _) && JsonSerializer.Serialize(graph) == before,
            "A connected node is never inserted by discarding its existing cables.");
        foreach (string kind in new[] { "microphone", "virtualInput", "appInput", "output", "virtualOutput", "control" })
        {
            graph = Fixture(kind); graph.Edges = [Edge("original", "source", "a")];
            require(!GraphConnectionEditing.TryPrepareInsertion(graph, "middle", "original", out _, out _), $"{GraphDefaults.KindLabel(kind)} is excluded from cable insertion.");
        }
        graph = Fixture(); graph.Edges = [Edge("original", "source", "a")];
        require(GraphConnectionEditing.TryPrepareInsertion(graph, "middle", "original", out var stale, out _), "Prepare a cable-edit concurrency fixture.");
        graph.Edges[0].GainDb = -6; before = JsonSerializer.Serialize(graph);
        require(!GraphConnectionEditing.TryApply(graph, stale!, out _) && JsonSerializer.Serialize(graph) == before,
            "A changed original cable invalidates insertion without losing its new gain.");
        graph = Fixture(); graph.Edges = [new() { Id = "control", From = "source", To = "a", Kind = "control" }];
        require(!GraphConnectionEditing.TryPrepareInsertion(graph, "middle", "control", out _, out _), "Control cables cannot be split with an audio processor.");

        graph = new() { Nodes = Enumerable.Range(0, 16).Select(i => Node("gain", "n" + i)).Append(Node("gain", "middle")).ToList() };
        foreach (int from in Enumerable.Range(0, 16)) foreach (int to in Enumerable.Range(from + 1, 15 - from))
            if (graph.Edges.Count < 96) graph.Edges.Add(Edge($"{from}-{to}", "n" + from, "n" + to));
        require(GraphDefaults.Validate(graph).Length == 0 && !GraphConnectionEditing.TryPrepareInsertion(graph, "middle", graph.Edges[0].Id, out _, out _),
            "Insertion rejects a full 96-edge graph before splitting or removing any cable.");
        // Applying a prepared plan to a concurrently introduced cycle must fail as a whole.
        graph = Fixture(); graph.Nodes[0].Kind = "gain"; graph.Nodes[2].Kind = "gain"; graph.Edges = [Edge("original", "source", "a")];
        require(GraphConnectionEditing.TryPrepareInsertion(graph, "middle", "original", out stale, out _), "Prepare a cycle-validation fixture.");
        graph.Edges.Add(Edge("reverse", "a", "source")); before = JsonSerializer.Serialize(graph);
        require(!GraphConnectionEditing.TryApply(graph, stale!, out _) && JsonSerializer.Serialize(graph) == before,
            "Full graph validation prevents committing a split after a concurrent cycle is introduced.");

        graph = Fixture();
        var original = graph.Edges[0].Copy(); before = JsonSerializer.Serialize(graph);
        require(GraphConnectionEditing.TryPrepareRewire(graph, original, "b", out var rewire, out _)
            && JsonSerializer.Serialize(graph) == before, "Preparing a cable rewire leaves the current graph untouched.");
        require(GraphConnectionEditing.TryApply(graph, rewire!, out _) && graph.Edges.Single(e => e.Id == original.Id).To == "b"
            && graph.Edges.Single(e => e.Id == original.Id).GainDb == original.GainDb && graph.Edges.Count == 3,
            "A prepared rewire changes only the destination while retaining its identity and level.");
        applied = JsonSerializer.Serialize(graph);
        require(GraphConnectionEditing.TryApply(graph, rewire!, out _) && JsonSerializer.Serialize(graph) == applied,
            "Replaying a rewire acknowledgement leaves one copy of the same cable.");
        graph = Fixture(); graph.Nodes.Add(Node("control", "controller"));
        graph.Edges.Add(new() { Id = "linked", From = "controller", To = "middle", Kind = "control" });
        before = JsonSerializer.Serialize(graph);
        require(GraphConnectionEditing.TryPrepareRemoval(graph, graph.Edges.Where(e => e.Id is "in" or "linked"), out var removal, out _)
            && JsonSerializer.Serialize(graph) == before, "A cut can prepare audio and linked-control removal together without editing either cable.");
        graph.Edges.Single(e => e.Id == "in").GainDb = -14; before = JsonSerializer.Serialize(graph);
        require(!GraphConnectionEditing.TryApply(graph, removal!, out _) && JsonSerializer.Serialize(graph) == before,
            "If one crossed cable changes before release, the entire pending cut is rejected without removing the other cable.");
        require(GraphConnectionEditing.TryPrepareRemoval(graph, graph.Edges.Where(e => e.Id is "in" or "linked"), out removal, out _)
            && GraphConnectionEditing.TryApply(graph, removal!, out _) && graph.Edges.Count == 2
            && graph.Edges.All(e => e.From == "middle") && graph.Nodes.Count == 5,
            "A validated cut removes exactly its audio and linked-control cables while preserving all nodes and other branches.");
        applied = JsonSerializer.Serialize(graph);
        require(GraphConnectionEditing.TryApply(graph, removal!, out _) && JsonSerializer.Serialize(graph) == applied,
            "Replaying a cut acknowledgement cannot remove any additional cables.");
    }

    private static void VerifyDetectorRouting(Action<bool, string> require)
    {
        var graph = new GraphConfiguration
        {
            Nodes = [Node("mix", "music"), Node("microphone", "voice"), Node("ducking", "duck"), Node("ducking", "other-duck"), Node("compressor", "compress"), Node("limiter", "limit"), Node("output", "listen")],
            Edges = [Edge("main", "music", "listen", -7, true), new() { Id = "trigger", From = "voice", To = "duck", Kind = "sidechain", GainDb = -2 }]
        };
        require(GraphDefaults.Validate(graph).Length == 0, "A Ducking trigger is a valid independent detector cable.");
        require(GraphDefaults.ConnectionError(graph, new() { From = "music", To = "duck", Kind = "sidechain" }).Length > 0,
            "Ducking accepts exactly one trigger cable.");
        require(GraphDefaults.ConnectionError(graph, new() { From = "voice", To = "limit", Kind = "sidechain" }).Length > 0,
            "Limiter rejects detector inputs because its ceiling follows the main signal.");
        require(GraphDefaults.ConnectionError(graph, new() { From = "voice", To = "compress", Kind = "sidechain" }).Length > 0,
            "Compressor rejects trigger cables because it follows its own main signal.");
        require(GraphDefaults.ConnectionError(graph, new() { From = "voice", To = "duck", Kind = "control" }).Length > 0,
            "Detector audio cannot be relabelled as a linked parameter control.");
        string original = JsonSerializer.Serialize(graph);
        require(GraphConnectionEditing.TryPrepareInsertion(graph, "duck", "main", out var insertion, out _)
            && JsonSerializer.Serialize(graph) == original && GraphConnectionEditing.TryApply(graph, insertion!, out _)
            && graph.Edges.Single(e => e.Id == "trigger").Kind == "sidechain"
            && graph.Edges.Single(e => e.Kind == "audio" && e.To == "duck").GainDb == -7,
            "Inserting Ducking in an audio cable retains its existing detector and the main cable's gain and mute.");
        original = JsonSerializer.Serialize(graph);
        require(!GraphConnectionEditing.TryPrepareDetach(graph, "duck", out _, out _) && JsonSerializer.Serialize(graph) == original,
            "Detach with a trigger refuses an ambiguous edit and leaves both audio and detector routing intact.");
        require(GraphDefaults.ConnectionError(graph, new() { From = "duck", To = "music", Kind = "audio" }).Length > 0,
            "Ducking main-path cycles are rejected.");
        var trigger = graph.Edges.Single(e => e.Id == "trigger");
        require(GraphConnectionEditing.TryPrepareRewire(graph, trigger, "other-duck", out var rewire, out _)
            && GraphConnectionEditing.TryApply(graph, rewire!, out _)
            && graph.Edges.Single(e => e.Id == "trigger").Kind == "sidechain"
            && graph.Edges.Single(e => e.Id == "trigger").GainDb == -2,
            "Rewiring a trigger to another Ducking node keeps its detector-only kind, cable identity and level.");
        require(!GraphConnectionEditing.TryPrepareRewire(graph, graph.Edges.Single(e => e.Id == "trigger"), "limit", out _, out _),
            "Rewiring a detector to Limiter cannot turn it into an audible connection.");
        require(GraphConnectionEditing.TryPrepareDetach(graph, "duck", out var detach, out _)
            && GraphConnectionEditing.TryApply(graph, detach!, out _)
            && graph.Edges.Any(e => e.From == "music" && e.To == "listen" && e.Kind == "audio")
            && graph.Edges.Single(e => e.Id == "trigger").To == "other-duck",
            "Ducking detaches normally after its detector is removed, without touching other detector connections.");

        graph = new GraphConfiguration { Nodes = [Node("mix", "music"), Node("ducking", "duck"), Node("mix", "after")],
            Edges = [Edge("main", "music", "after"), new() { Id = "trigger", From = "after", To = "duck", Kind = "sidechain" }] };
        original = JsonSerializer.Serialize(graph);
        require(!GraphConnectionEditing.TryPrepareInsertion(graph, "duck", "main", out _, out _) && JsonSerializer.Serialize(graph) == original,
            "Insertion rejects a cycle through a preconnected detector and does not modify either cable.");
        graph.Edges = [Edge("forward", "duck", "after")];
        require(GraphDefaults.ConnectionError(graph, new() { From = "after", To = "duck", Kind = "sidechain" }).Length > 0,
            "Cycle validation includes detector dependencies as well as audible cables.");
    }

    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout, Action<bool, string> require, Action<string>? capture = null)
    {
        require(mixer.IsPreview, "Cable gesture verification uses an inert preview model.");
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new()
        {
            Nodes = [Node("microphone", "snap-source", 32, 100), Node("gain", "snap-insert", 620, 600), Node("output", "snap-output", 1240, 100)],
            Edges = [Edge("snap-cable", "snap-source", "snap-output", -9, true)], PanX = 35, PanY = 22, Zoom = 1
        };
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(n => n.Id == id);
        Point Screen(Point p) => new(p.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX, p.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
        foreach (double zoom in new[] { .3, .75, 1, 2 })
        {
            mixer.Edit(c => c.Graph.Zoom = zoom); await layout();
            var source = View("snap-source").FindSocket("audio", true)!;
            var target = View("snap-insert").FindSocket("audio", false)!;
            var anchor = Screen(workspace.SocketPosition(target));
            require(workspace.FindSnapSocket(anchor + new Vector(-17, 0), source) == target,
                $"A compatible input snaps from 17 screen DIPs away at {zoom:P0} zoom.");
            require(workspace.FindSnapSocket(anchor + new Vector(-20, 0), source) is null,
                $"The socket snap radius stays bounded at {zoom:P0} zoom.");
            require(Math.Abs(workspace.ConnectionHitWidth("snap-cable") - 16) < .001,
                $"Cable selection retains a 16-screen-DIP hit region at {zoom:P0} zoom.");
            var duplicate = View("snap-output").FindSocket("audio", false)!;
            require(workspace.FindSnapSocket(Screen(workspace.SocketPosition(duplicate)), source) is null,
                "Socket preview refuses an already-connected destination.");
            var shape = (Shape)target.Content;
            var socketSize = target.RenderSize; var shapeSize = shape.RenderSize;
            double stroke = shape.StrokeThickness;
            workspace.BeginConnectionPreview(source, Screen(workspace.SocketPosition(source)), dragging: true);
            workspace.UpdateConnectionPointer(anchor); await layout();
            require(target.IsConnectionHighlighted && ReferenceEquals(shape.Stroke, ConsoleTheme.Get("NodeSelectedBrush"))
                && ReferenceEquals(shape.Fill, ConsoleTheme.Get("NodeCanvasBrush"))
                && target.RenderSize == socketSize && shape.RenderSize == shapeSize && shape.StrokeThickness == stroke
                && Descendants<Shape>(target).Count() == 1,
                $"At {zoom:P0} zoom, a connection target highlights the existing port outline without a growing ring, extra shape or geometry change.");
            workspace.HandleCaptureLoss(); await layout();
            require(!target.IsConnectionHighlighted && ReferenceEquals(shape.Stroke, ConsoleTheme.Get("NodeAudioBrush"))
                && ReferenceEquals(shape.Fill, ConsoleTheme.Get("NodeCanvasBrush"))
                && target.RenderSize == socketSize && shape.RenderSize == shapeSize,
                $"Cancelling the connection restores the normal port colour and unchanged geometry at {zoom:P0} zoom.");
        }
        mixer.Edit(c => c.Graph.Zoom = 1); await layout();
        var sourceSocket = View("snap-source").FindSocket("audio", true)!;
        var insertSocket = View("snap-insert").FindSocket("audio", false)!;
        Point sourceScreen = Screen(workspace.SocketPosition(sourceSocket)), insertScreen = Screen(workspace.SocketPosition(insertSocket));
        long beforeGesture = mixer.ConfigurationPresentationCount;
        workspace.BeginConnectionPreview(sourceSocket, sourceScreen, dragging: true); workspace.UpdateConnectionPointer(insertScreen);
        await layout(); capture?.Invoke("cable-socket-snap-preview");
        require(workspace.SnapPreviewNodeId == "snap-insert", "A cable drag highlights the compatible target before any connection exists.");
        workspace.FinishConnectionPointer(MouseButton.Right, insertScreen);
        require(workspace.HasConnectionPreview && mixer.GraphConfig.Edges.Count == 1 && mixer.ConfigurationPresentationCount == beforeGesture,
            "Releasing an unrelated mouse button cannot finish an armed left-button cable drag.");
        workspace.HandleCaptureLoss(); workspace.FinishConnectionPointer(MouseButton.Left, insertScreen);
        require(!workspace.HasConnectionPreview && workspace.SnapPreviewNodeId is null && mixer.GraphConfig.Edges.Count == 1
            && mixer.ConfigurationPresentationCount == beforeGesture,
            "Losing mouse capture cancels a cable gesture; a later button release cannot connect stale endpoints.");
        workspace.BeginConnectionPreview(sourceSocket, sourceScreen, dragging: true); workspace.UpdateConnectionPointer(insertScreen);
        mixer.Edit(c => c.Graph.Nodes.RemoveAll(n => n.Id == "snap-insert")); await layout();
        require(workspace.HasConnectionPreview && workspace.SnapPreviewNodeId is null && mixer.GraphConfig.Edges.Count == 1,
            "Removing a hovered target during a configuration refresh clears its stale snap without throwing or changing routes.");
        workspace.HandleCaptureLoss(); mixer.ApplyPreviewSnapshot(fixture); await layout();
        sourceSocket = View("snap-source").FindSocket("audio", true)!; insertSocket = View("snap-insert").FindSocket("audio", false)!;
        sourceScreen = Screen(workspace.SocketPosition(sourceSocket)); insertScreen = Screen(workspace.SocketPosition(insertSocket));
        workspace.BeginConnectionPreview(sourceSocket, sourceScreen, dragging: true); workspace.UpdateConnectionPointer(insertScreen);
        mixer.Edit(c => { c.Graph.Nodes.RemoveAll(n => n.Id == "snap-source"); c.Graph.Edges.Clear(); }); await layout();
        require(!workspace.HasConnectionPreview && workspace.SnapPreviewNodeId is null,
            "Removing the source clears both the cable preview and its target colour without leaving a captured gesture armed.");
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        var moving = View("snap-insert"); var from = workspace.SocketPosition(View("snap-source").FindSocket("audio", true)!);
        var to = workspace.SocketPosition(View("snap-output").FindSocket("audio", false)!);
        // A symmetric Bezier has the endpoint midpoint at t=.5.
        var cableCenter = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        var origin = new Point(Canvas.GetLeft(moving), Canvas.GetTop(moving));
        var destination = new Point(cableCenter.X - moving.ActualWidth / 2, cableCenter.Y - moving.ActualHeight / 2);
        Point start = Screen(origin + new Vector(80, 15)), end = start + (destination - origin);
        long edits = mixer.ConfigurationPresentationCount;
        workspace.BeginNodeMove(moving.Id, start); workspace.UpdateNodeMove(end);
        await layout(); capture?.Invoke("cable-insertion-preview");
        require(workspace.InsertionPreviewId == "snap-cable" && mixer.ConfigurationPresentationCount == edits && mixer.GraphConfig.Edges.Count == 1,
            "Moving an unconnected processor onto a cable highlights it without sending any audio edits.");
        workspace.FinishNodeMove(cancel: true); await layout();
        require(mixer.GraphConfig.Edges.Count == 1 && mixer.GraphConfig.Nodes.Single(n => n.Id == moving.Id).X == origin.X && workspace.InsertionPreviewId is null,
            "Escape-style cancellation restores position and keeps the original cable.");
        workspace.BeginNodeMove(moving.Id, start); workspace.UpdateNodeMove(end); workspace.FinishNodeMove(); await layout();
        require(mixer.GraphConfig.Edges.Count == 2 && mixer.GraphConfig.Edges.All(e => e.From == moving.Id || e.To == moving.Id)
            && mixer.ConfigurationPresentationCount == edits + 1,
            "Dropping the processor commits its position and cable split in one configuration edit.");
        var saved = mixer.GraphConfig.Copy();
        start = Screen(new Point(Canvas.GetLeft(moving) + 80, Canvas.GetTop(moving) + 15));
        workspace.BeginNodeMove(moving.Id, start, detach: true); workspace.UpdateNodeMove(start + new Vector(0, 240));
        require(mixer.GraphConfig.Edges.Count == 2, "Alt-drag leaves the audio graph untouched until release.");
        workspace.FinishNodeMove(cancel: true); await layout();
        require(JsonSerializer.Serialize(mixer.GraphConfig) == JsonSerializer.Serialize(saved), "Cancelling Alt-drag retains all settings, node position and cable identities.");
        workspace.BeginNodeMove(moving.Id, start, detach: true); workspace.UpdateNodeMove(start + new Vector(0, 240)); workspace.FinishNodeMove(); await layout();
        require(mixer.GraphConfig.Edges.Count == 1 && mixer.GraphConfig.Edges[0].GainDb == -9 && mixer.GraphConfig.Edges[0].Muted
            && mixer.GraphConfig.Nodes.Any(n => n.Id == moving.Id), "Alt-drag release detaches the processor, retains it, and preserves the muted attenuated path.");

        fixture.Configuration.Graph.Nodes.Add(Node("microphone", "close-source", 32, 104));
        fixture.Configuration.Graph.Nodes.Add(Node("output", "close-output", 1240, 104));
        fixture.Configuration.Graph.Edges.Add(Edge("close-cable", "close-source", "close-output", -17));
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        moving = View("snap-insert"); origin = new Point(Canvas.GetLeft(moving), Canvas.GetTop(moving));
        from = workspace.SocketPosition(View("snap-source").FindSocket("audio", true)!); to = workspace.SocketPosition(View("snap-output").FindSocket("audio", false)!);
        cableCenter = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        double phase = cableCenter.Y - moving.ActualHeight / 2 - origin.Y;
        double phaseShift = Math.Round(phase / 8) * 8 - 1.5 - phase;
        mixer.Edit(c => { foreach (var node in c.Graph.Nodes.Where(n => n.Id != moving.Id)) node.Y += phaseShift; }); await layout();
        from = workspace.SocketPosition(View("snap-source").FindSocket("audio", true)!); to = workspace.SocketPosition(View("snap-output").FindSocket("audio", false)!);
        cableCenter = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        start = Screen(origin + new Vector(80, 15));
        // Choose a phase where rounding to the eight-unit move grid makes the
        // neighbouring cable nearer, then ensure release honours the preview.
        bool checkedRounding = false;
        foreach (double offset in new[] { -3.5, -2.5, -1.5, -.5, .5, 1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5 })
        {
            destination = new Point(cableCenter.X - moving.ActualWidth / 2, cableCenter.Y + offset - moving.ActualHeight / 2);
            workspace.BeginNodeMove(moving.Id, start); workspace.UpdateNodeMove(start + (destination - origin));
            string? highlighted = workspace.InsertionPreviewId;
            var delta = destination - origin;
            Canvas.SetLeft(moving, origin.X + Math.Round(delta.X / 8) * 8); Canvas.SetTop(moving, origin.Y + Math.Round(delta.Y / 8) * 8);
            string? roundedNearest = workspace.FindInsertionCandidate(moving.Id);
            if (highlighted is not null && roundedNearest is not null && highlighted != roundedNearest)
            {
                workspace.FinishNodeMove(); await layout();
                require(mixer.GraphConfig.Edges.All(e => e.Id != highlighted) && mixer.GraphConfig.Edges.Any(e => e.Id == roundedNearest),
                    "Grid rounding cannot swap the cable being inserted: release splits exactly the highlighted cable.");
                checkedRounding = true; break;
            }
            workspace.FinishNodeMove(cancel: true); await layout();
        }
        require(checkedRounding, "The close-cable fixture exercises a different nearest cable before and after grid rounding.");
        await RunInputRewiringAsync(workspace, mixer, layout, require, capture);
        await RunCableCuttingAsync(workspace, mixer, layout, require, capture);
    }

    private static async Task RunInputRewiringAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<string>? capture)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new()
        {
            Nodes = [Node("microphone", "rewire-a", 32, 100), Node("virtualInput", "rewire-z", 32, 500),
                Node("mix", "rewire-mix", 620, 100), Node("gain", "rewire-target", 620, 600),
                Node("output", "rewire-output", 1240, 100), Node("control", "rewire-control", 32, 900)],
            Edges = [Edge("z-in", "rewire-z", "rewire-mix", -12), Edge("a-in", "rewire-a", "rewire-mix", -3, true),
                Edge("rewire-out", "rewire-mix", "rewire-output", -5), Edge("unrelated", "rewire-z", "rewire-target", -2),
                new() { Id = "linked", From = "rewire-control", To = "rewire-mix", Kind = "control" }],
            PanX = 35, PanY = 22, Zoom = 1
        };
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(n => n.Id == id);
        Point Screen(Point p) => new(p.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX, p.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
        async Task Reset()
        { mixer.ApplyPreviewSnapshot(fixture); await layout(); }
        (GraphWorkspace.Socket Input, Point Start, Point Target) Points()
        {
            var input = View("rewire-mix").FindSocket("audio", false)!;
            var target = View("rewire-target").FindSocket("audio", false)!;
            return (input, Screen(workspace.SocketPosition(input)), Screen(workspace.SocketPosition(target)));
        }

        await Reset();
        var points = Points(); string before = JsonSerializer.Serialize(mixer.GraphConfig);
        long edits = mixer.ConfigurationPresentationCount;
        require(workspace.BeginInputLinkDrag(points.Input, points.Start, "a-in"), "A specifically selected incoming cable can be picked up from its input socket.");
        workspace.UpdateConnectionPointer(points.Target);
        await layout(); capture?.Invoke("cable-input-rewire-preview");
        require(workspace.HasConnectionPreview && workspace.SnapPreviewNodeId == "rewire-target"
            && JsonSerializer.Serialize(mixer.GraphConfig) == before && mixer.ConfigurationPresentationCount == edits,
            "Dragging a connected input picks up one existing cable and previews a compatible new input without changing routes.");
        workspace.HandleCaptureLoss(); workspace.FinishConnectionPointer(MouseButton.Left, points.Target);
        require(!workspace.HasConnectionPreview && JsonSerializer.Serialize(mixer.GraphConfig) == before
            && mixer.ConfigurationPresentationCount == edits,
            "Losing capture while rewiring preserves every connection, including the original cable and its settings.");

        points = Points();
        workspace.BeginInputLinkDrag(points.Input, points.Start, "a-in"); workspace.UpdateConnectionPointer(points.Target);
        workspace.CancelActiveGesture(); workspace.FinishConnectionPointer(MouseButton.Left, points.Target);
        require(!workspace.HasConnectionPreview && JsonSerializer.Serialize(mixer.GraphConfig) == before
            && mixer.ConfigurationPresentationCount == edits,
            "The same cancellation path used by Escape restores an input rewire without changing any connection.");
        var targetView = View("rewire-target");
        Point body = Screen(new(Canvas.GetLeft(targetView) + targetView.ActualWidth / 2, Canvas.GetTop(targetView) + targetView.ActualHeight / 2));
        Point incompatible = Screen(workspace.SocketPosition(View("rewire-a").FindSocket("audio", true)!));
        foreach (Point invalid in new[] { body, incompatible })
        {
            workspace.BeginInputLinkDrag(points.Input, points.Start, "a-in"); workspace.UpdateConnectionPointer(invalid);
            workspace.FinishConnectionPointer(MouseButton.Left, invalid, blankCanvas: false);
            require(!workspace.HasConnectionPreview && JsonSerializer.Serialize(mixer.GraphConfig) == before
                && mixer.ConfigurationPresentationCount == edits,
                "Dropping a cable over a node body or incompatible output socket cancels without disconnecting or editing it.");
        }
        workspace.BeginInputLinkDrag(points.Input, points.Start, "z-in"); workspace.UpdateConnectionPointer(points.Target);
        workspace.FinishConnectionPointer(MouseButton.Left, points.Target, blankCanvas: false);
        require(!workspace.HasConnectionPreview && JsonSerializer.Serialize(mixer.GraphConfig) == before
            && mixer.ConfigurationPresentationCount == edits,
            "A rewire that would duplicate an existing connection is rejected while preserving its original destination.");
        workspace.BeginInputLinkDrag(points.Input, points.Start, "a-in");
        workspace.FinishConnectionPointer(MouseButton.Left, points.Start);
        require(JsonSerializer.Serialize(mixer.GraphConfig) == before && mixer.ConfigurationPresentationCount == edits,
            "Clicking a connected input without moving never disconnects or rewrites its cable.");
        workspace.CancelActiveGesture();
        workspace.BeginInputLinkDrag(points.Input, points.Start, "a-in"); workspace.UpdateConnectionPointer(points.Target);
        workspace.UpdateConnectionPointer(points.Start); workspace.FinishConnectionPointer(MouseButton.Left, points.Start);
        require(!workspace.HasConnectionPreview && JsonSerializer.Serialize(mixer.GraphConfig) == before
            && mixer.ConfigurationPresentationCount == edits,
            "Returning a picked-up cable to its original input is a no-op with no configuration edit.");

        workspace.BeginInputLinkDrag(points.Input, points.Start, "a-in"); workspace.UpdateConnectionPointer(points.Target);
        workspace.FinishConnectionPointer(MouseButton.Left, points.Target); await layout();
        var moved = mixer.GraphConfig.Edges.Single(e => e.Id == "a-in");
        require(moved.From == "rewire-a" && moved.To == "rewire-target" && moved.GainDb == -3 && moved.Muted
            && mixer.GraphConfig.Edges.Count == 5 && mixer.ConfigurationPresentationCount == edits + 1,
            "Dropping an existing cable onto another input rewires it in one edit while retaining its ID, source, gain and mute.");
        require(fixture.Configuration.Graph.Edges.Where(e => e.Id != "a-in").All(original =>
                mixer.GraphConfig.Edges.Any(current => JsonSerializer.Serialize(current) == JsonSerializer.Serialize(original))),
            "Rewiring one incoming cable preserves the other incoming cable, output branch and linked-control connection exactly.");

        foreach (bool removeOriginal in new[] { false, true })
        {
            await Reset(); points = Points();
            workspace.BeginInputLinkDrag(points.Input, points.Start, "a-in"); workspace.UpdateConnectionPointer(points.Target);
            mixer.Edit(c =>
            {
                if (removeOriginal) c.Graph.Edges.RemoveAll(e => e.Id == "a-in");
                else c.Graph.Edges.Single(e => e.Id == "a-in").GainDb = -18;
            });
            await layout(); string concurrent = JsonSerializer.Serialize(mixer.GraphConfig); long concurrentEdits = mixer.ConfigurationPresentationCount;
            workspace.FinishConnectionPointer(MouseButton.Left, points.Target); await layout();
            require(!workspace.HasConnectionPreview && JsonSerializer.Serialize(mixer.GraphConfig) == concurrent
                && mixer.ConfigurationPresentationCount == concurrentEdits,
                removeOriginal ? "Deleting the original cable during a preview cannot resurrect it on release."
                    : "A cable changed during a preview retains its new settings instead of committing a stale rewire.");
        }

        string? removedId = null;
        foreach (bool reverse in new[] { false, true })
        {
            if (reverse) fixture.Configuration.Graph.Edges.Reverse();
            await Reset(); points = Points(); edits = mixer.ConfigurationPresentationCount;
            Point blank = new(-800, -600);
            var from = workspace.SocketPosition(View("rewire-a").FindSocket("audio", true)!);
            var to = workspace.SocketPosition(points.Input);
            Point onChosenCable = Screen(new((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            workspace.BeginInputLinkDrag(points.Input, points.Start); workspace.UpdateConnectionPointer(onChosenCable); workspace.UpdateConnectionPointer(blank);
            workspace.FinishConnectionPointer(MouseButton.Left, blank); await layout();
            var removed = fixture.Configuration.Graph.Edges.Where(original => mixer.GraphConfig.Edges.All(e => e.Id != original.Id)).ToArray();
            require(removed.Length == 1 && removed[0].Id == "a-in" && mixer.GraphConfig.Edges.Count == 4
                && mixer.ConfigurationPresentationCount == edits + 1,
                "Following one distinguishable incoming cable and releasing on empty canvas disconnects only that cable.");
            require(removedId is null || removedId == removed[0].Id,
                "The chosen incoming cable stays deterministic when the serialized connection order changes.");
            removedId = removed[0].Id;
        }

        await Reset();
        var outputInput = View("rewire-output").FindSocket("audio", false)!;
        Point outputStart = Screen(workspace.SocketPosition(outputInput));
        workspace.BeginInputLinkDrag(outputInput, outputStart); workspace.UpdateConnectionPointer(new(-800, -600));
        workspace.FinishConnectionPointer(MouseButton.Left, new(-800, -600)); await layout();
        require(mixer.GraphConfig.Edges.Count == 4 && mixer.GraphConfig.Edges.All(e => e.Id != "rewire-out"),
            "A socket with one incoming cable picks up that cable directly without requiring a separate selection.");

        // Coincident curves cannot identify which source the user intended.
        fixture.Configuration.Graph.Nodes.Single(n => n.Id == "rewire-z").Kind = "microphone";
        fixture.Configuration.Graph.Nodes.Single(n => n.Id == "rewire-z").Y = 100;
        await Reset(); points = Points(); before = JsonSerializer.Serialize(mixer.GraphConfig); edits = mixer.ConfigurationPresentationCount;
        var sameFrom = workspace.SocketPosition(View("rewire-a").FindSocket("audio", true)!);
        var sameTo = workspace.SocketPosition(points.Input);
        Point overlap = Screen(new((sameFrom.X + sameTo.X) / 2, (sameFrom.Y + sameTo.Y) / 2));
        workspace.BeginInputLinkDrag(points.Input, points.Start); workspace.UpdateConnectionPointer(overlap);
        workspace.FinishConnectionPointer(MouseButton.Left, new(-800, -600)); await layout();
        require(!workspace.HasConnectionPreview && JsonSerializer.Serialize(mixer.GraphConfig) == before
            && mixer.ConfigurationPresentationCount == edits,
            "Overlapping incoming curves remain unchanged when the intended cable is ambiguous.");
    }

    private static async Task RunCableCuttingAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<string>? capture)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new()
        {
            Nodes = [Node("microphone", "cut-source-a", 32, 40), Node("output", "cut-output-a", 1240, 40),
                Node("virtualInput", "cut-source-b", 32, 440), Node("virtualOutput", "cut-output-b", 1240, 440),
                Node("control", "cut-controller", 32, 900), Node("gain", "cut-controlled", 1240, 900)],
            Edges = [Edge("cut-a", "cut-source-a", "cut-output-a", -9, true), Edge("cut-b", "cut-source-b", "cut-output-b", -3),
                new() { Id = "cut-unrelated", From = "cut-controller", To = "cut-controlled", Kind = "control", GainDb = -5 }],
            PanX = 31, PanY = 19, Zoom = 1
        };
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(n => n.Id == id);
        Point Screen(Point p) => new(p.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX, p.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
        Point Midpoint(string fromId, string toId)
        {
            var from = workspace.SocketPosition(View(fromId).FindSocket("audio", true)!);
            var to = workspace.SocketPosition(View(toId).FindSocket("audio", false)!);
            return new((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        }
        foreach (double zoom in new[] { .3, 1, 2 })
        {
            fixture.Configuration.Graph.Zoom = zoom; mixer.ApplyPreviewSnapshot(fixture); await layout();
            Point upper = Midpoint("cut-source-a", "cut-output-a"), lower = Midpoint("cut-source-b", "cut-output-b");
            Point start = Screen(new(upper.X, upper.Y - 30)), end = Screen(new(lower.X, lower.Y + 30));
            string before = JsonSerializer.Serialize(mixer.GraphConfig); long edits = mixer.ConfigurationPresentationCount;
            workspace.BeginCableCut(start); workspace.UpdateCableCut(end);
            await layout(); if (zoom == 1) capture?.Invoke("cable-cut-preview");
            require(workspace.HasCableCutPreview && workspace.CutPreviewIds.ToHashSet().SetEquals(["cut-a", "cut-b"])
                && JsonSerializer.Serialize(mixer.GraphConfig) == before && mixer.ConfigurationPresentationCount == edits,
                $"A cut stroke previews exactly the two crossed audio cables without changing routes at {zoom:P0} zoom.");
            workspace.CancelActiveGesture();
            require(!workspace.HasCableCutPreview && JsonSerializer.Serialize(mixer.GraphConfig) == before
                && mixer.ConfigurationPresentationCount == edits,
                "Escape cancels the cut preview without removing any cable.");
            workspace.BeginCableCut(start); workspace.UpdateCableCut(end); workspace.HandleCaptureLoss();
            require(!workspace.HasCableCutPreview && JsonSerializer.Serialize(mixer.GraphConfig) == before,
                "Capture loss cancels a cut stroke and preserves all connection identities and settings.");
            workspace.BeginCableCut(start); workspace.UpdateCableCut(end); workspace.FinishCableCut(); await layout();
            require(mixer.GraphConfig.Edges.Count == 1 && mixer.GraphConfig.Edges[0].Id == "cut-unrelated"
                && JsonSerializer.Serialize(mixer.GraphConfig.Edges[0]) == JsonSerializer.Serialize(fixture.Configuration.Graph.Edges[2])
                && mixer.GraphConfig.Nodes.Count == 6 && mixer.ConfigurationPresentationCount == edits + 1,
                $"Releasing the cut gesture removes only crossed cables in one edit, retaining every node and the untouched control cable at {zoom:P0} zoom.");
        }
        workspace.BeginCableCut(new(-800, -600)); workspace.UpdateCableCut(new(-700, -500));
        long untouched = mixer.ConfigurationPresentationCount; workspace.FinishCableCut();
        require(!workspace.HasCableCutPreview && mixer.GraphConfig.Edges.Count == 1 && mixer.ConfigurationPresentationCount == untouched,
            "A cut stroke that crosses no cable makes no configuration edit.");
    }

    private static GraphNode Node(string kind, string id, double x = 0, double y = 0)
    { var node = GraphDefaults.NewNode(kind, x, y); node.Id = id; return node; }
    private static GraphEdge Edge(string id, string from, string to, double gain = 0, bool muted = false)
        => new() { Id = id, From = from, To = to, GainDb = gain, Muted = muted };
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
