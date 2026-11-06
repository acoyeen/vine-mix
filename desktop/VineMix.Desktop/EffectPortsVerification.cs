using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace VineMix.Desktop;

/// <summary>Effect-port editing against an inert preview graph, without clipboard or device access.</summary>
internal static class EffectPortsVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Effect port checks use a detached preview without a native window, audio engine or device access.");
        VerifyEditPlans(require);
        var fixture = UiPreview.CreateSnapshot([]);
        var source = GraphDefaults.NewNode("mix", 20, 30);
        var key = GraphDefaults.NewNode("mix", 20, 440);
        var reverb = GraphDefaults.NewNode("reverb", 400, 30);
        var gate = GraphDefaults.NewNode("gate", 850, 30);
        var expander = GraphDefaults.NewNode("expander", 850, 530);
        var legacyGate = GraphDefaults.NewNode("expander", 1350, 30); legacyGate.Expander.Mode = "gate";
        var audioDestination = GraphDefaults.NewNode("mix", 1350, 530);
        var wetDestination = GraphDefaults.NewNode("mix", 1750, 530);
        fixture.Configuration.Graph = new() { Nodes = [source, key, reverb, gate, expander, legacyGate, audioDestination, wetDestination], Zoom = 1 };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus { Id = node.Id, Path = new() { State = "Running" } }).ToList();
        mixer.IsLayoutLocked = false; mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNodeView View(GraphNode node) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == node.Id);
        Point Screen(GraphWorkspace.Socket socket)
        {
            Point world = workspace.SocketPosition(socket);
            return new(world.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX, world.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
        }
        foreach (var node in new[] { gate, expander, legacyGate })
            require(View(node).FindSocket("sidechain", false) is { Output: false } detector
                && AutomationProperties.GetName(detector).Contains("Trigger") && View(node).FindSocket("audio", false) is not null,
                "Gate, Expander and legacy expander-kind Gate retain Audio and expose a separate accessible Trigger input.");
        var wet = View(reverb).FindSocket("audio", true, "wet") ?? throw new InvalidOperationException("Reverb Wet socket is missing.");
        var main = View(reverb).FindSocket("audio", true) ?? throw new InvalidOperationException("Reverb Audio socket is missing.");
        require(!ReferenceEquals(wet, main)
            && AutomationProperties.GetName(wet).Contains("Wet") && GraphDefaults.AudioOutputPorts("reverb").SequenceEqual(new[] { "audio", "wet" }),
            "Reverb preserves its Audio output and provides a distinct named Wet output.");
        require(workspace.SocketPosition(wet).Y > workspace.SocketPosition(main).Y,
            "Wet has its own output row and cable anchor below the existing Audio socket.");
        var compatibleInput = View(wetDestination).FindSocket("audio", false)!;
        require(ReferenceEquals(workspace.FindSnapSocket(Screen(wet) + new Vector(17, 0), compatibleInput), wet),
            "Dragging a compatible audio input finds the Wet output within the 17-screen-DIP snap vicinity, beyond its direct hit target.");
        string before = Json(mixer.GraphConfig);
        require(workspace.ConnectNodes(key.Id, gate.Id, "sidechain").Length == 0, "Gate accepts a detector-only cable through the real connection command.");
        string withTrigger = Json(mixer.GraphConfig);
        require(mixer.Undo() && Json(mixer.GraphConfig) == before && mixer.Redo() && Json(mixer.GraphConfig) == withTrigger,
            "Adding a Trigger cable is one exact undoable and redoable routing edit.");
        await layout();
        require(workspace.ConnectNodes(source.Id, reverb.Id).Length == 0
            && workspace.ConnectNodes(source.Id, gate.Id).Length == 0
            && workspace.ConnectNodes(source.Id, expander.Id).Length == 0
            && workspace.ConnectNodes(key.Id, legacyGate.Id, "sidechain").Length == 0
            && workspace.ConnectNodes(reverb.Id, audioDestination.Id).Length == 0
            && workspace.ConnectNodes(reverb.Id, wetDestination.Id, "audio", "wet").Length == 0,
            "Audio, Wet and Trigger routes coexist without converting detector edges into audible programme cables.");
        await layout();
        wet = View(reverb).FindSocket("audio", true, "wet")!;
        var trigger = View(expander).FindSocket("sidechain", false)!;
        require(ReferenceEquals(workspace.FindSnapSocket(Screen(trigger), wet), trigger),
            "The Wet audio output can snap to an Expander Trigger input.");
        workspace.BeginConnectionPreview(wet, Screen(wet), dragging: true);
        workspace.UpdateConnectionPointer(Screen(trigger));
        workspace.FinishConnectionPointer(MouseButton.Left, Screen(trigger)); await layout();
        require(mixer.GraphConfig.Edges.Any(edge => edge.From == reverb.Id && edge.FromPort == "wet" && edge.To == expander.Id && edge.Kind == "sidechain")
            && View(reverb).FindSocket("audio", true, "wet")!.IsConnected && View(expander).FindSocket("sidechain", false)!.IsConnected,
            "A Wet-to-Trigger cable gesture preserves both the alternate output identity and detector-only edge kind.");
        require(workspace.ConnectNodes(source.Id, gate.Id, "sidechain").Length > 0,
            "A second Gate detector is rejected rather than silently mixed with the existing Trigger.");
        require(workspace.ConnectNodes(gate.Id, key.Id).Length > 0,
            "Trigger edges participate in desktop feedback-cycle validation.");
        var serialized = Json(mixer.GraphConfig);
        var reopened = JsonSerializer.Deserialize<GraphConfiguration>(serialized, Protocol.Json)!;
        require(Json(reopened) == serialized && GraphDefaults.Validate(reopened).Length == 0,
            "The full graph serializes and validates with old Audio ports, new Wet ports and all three Trigger-capable Gate/Expander identities.");
        require(GraphClipboard.TryCreatePaste(GraphClipboard.Serialize(mixer.GraphConfig, mixer.GraphConfig.Nodes.Select(node => node.Id)), new(), new(100, 200), out var paste, out _)
            && paste.Edges.Count(edge => edge.Kind == "sidechain") == 3
            && paste.Edges.Count(edge => edge.FromPort == "wet") == 2
            && GraphDefaults.Validate(new() { Nodes = paste.Nodes.ToList(), Edges = paste.Edges.ToList() }).Length == 0,
            "Serialized copy/paste preserves Wet source ports and Trigger edge kinds without reading or writing the system clipboard.");
        string wetEdge = mixer.GraphConfig.Edges.Single(edge => edge.To == wetDestination.Id).Id;
        workspace.RemoveEdge(wetEdge);
        require(mixer.Undo() && Json(mixer.GraphConfig) == serialized,
            "Undoing a Wet-route removal restores its exact port identity alongside the unchanged main Audio route.");
        render(View(reverb), "reverb-audio-wet-ports.png"); render(View(gate), "gate-trigger-port.png");
        render(View(expander), "expander-trigger-port.png");
        await VerifyEchoPorts(workspace, mixer, layout, require, render);
    }

    private static async Task VerifyEchoPorts(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        var echo = GraphDefaults.NewNode("echo", 20, 30);
        var reverb = GraphDefaults.NewNode("reverb", 430, 30);
        var destination = GraphDefaults.NewNode("mix", 850, 30);
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new() { Nodes = [echo, reverb, destination], Zoom = 1 };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Channels = 2, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNodeView View(GraphNode node) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == node.Id);
        var output = View(echo).FindSocket("audio", true, "wet") ?? throw new InvalidOperationException("Echo-only socket is missing.");
        var input = View(reverb).FindSocket("audio", false)!;
        var main = View(echo).FindSocket("audio", true)!;
        require(AutomationProperties.GetName(output) == "Output Echo socket"
            && GraphDefaults.PortLabel("wet", "echo") == "Echo" && GraphDefaults.PortLabel("wet", "reverb") == "Wet",
            "Echo-only output uses the Echo name consistently while Reverb retains Wet.");
        require(workspace.SocketPosition(output).Y > workspace.SocketPosition(main).Y,
            "Echo-only output has a separate row below the unchanged main Audio socket.");
        Point position = workspace.SocketPosition(output);
        var screen = new Point(position.X + mixer.GraphConfig.PanX, position.Y + mixer.GraphConfig.PanY);
        require(ReferenceEquals(workspace.FindSnapSocket(screen + new Vector(17, 0), input), output),
            "A Reverb input snaps to the Echo-only output through the real alternate-port target list.");
        require(workspace.ConnectNodes(echo.Id, destination.Id).Length == 0,
            "Echo keeps the existing mixed Audio route.");
        string before = Json(mixer.GraphConfig);
        require(workspace.ConnectNodes(echo.Id, reverb.Id, "audio", "wet").Length == 0,
            "Echoes can route independently into Reverb while mixed Audio remains connected.");
        string connected = Json(mixer.GraphConfig);
        require(mixer.Undo() && Json(mixer.GraphConfig) == before && mixer.Redo() && Json(mixer.GraphConfig) == connected,
            "The separate Echo route supports exact undo and redo.");
        await layout();
        var edge = mixer.GraphConfig.Edges.Single(item => item.FromPort == "wet");
        require(GraphWorkspace.DescribeConnection(mixer.GraphConfig, edge).Route.Contains(" · Echo →", StringComparison.Ordinal),
            "Cable menus and descriptions use Echo for its wet source port.");
        workspace.SelectNode(echo.Id);
        require(workspace.TryPipelineShortcut(Key.Tab, ModifierKeys.Control, workspace)
            && workspace.PipelineChoices.Any(choice => choice.FromPort == "wet" && choice.Label.Contains("Echo → Audio", StringComparison.Ordinal)),
            "Pipeline branch choices identify the Echo-only route separately from mixed Audio.");
        workspace.CancelActiveGesture();
        require(GraphClipboard.TryCreatePaste(GraphClipboard.Serialize(mixer.GraphConfig, mixer.GraphConfig.Nodes.Select(node => node.Id)),
                new(), new(100, 200), out var paste, out _)
            && paste.Edges.Count == 2 && paste.Edges.Count(item => item.FromPort == "wet") == 1,
            "Echo-to-Reverb clipboard groups preserve both mixed and echo-only source identities.");
        var invalid = mixer.GraphConfig.Copy(); invalid.Nodes.Single(node => node.Id == echo.Id).Kind = "distortion";
        require(GraphDefaults.Validate(invalid).Length > 0, "An Echo-only route cannot silently become an unsupported Distortion output.");
        render(View(echo), "echo-audio-echo-ports.png");
    }

    private static void VerifyEditPlans(Action<bool, string> require)
    {
        foreach (string kind in new[] { "reverb", "echo" })
        foreach (string sourcePort in new[] { "audio", "wet" })
        {
            var source = GraphDefaults.NewNode(kind, 0, 0);
            var inserted = GraphDefaults.NewNode(kind, 400, 0);
            var destination = GraphDefaults.NewNode("mix", 800, 0);
            var wetDestination = GraphDefaults.NewNode("mix", 800, 500);
            var cable = new GraphEdge { Id = "original-audio-route", From = source.Id, FromPort = sourcePort,
                To = destination.Id, GainDb = -3.25, Muted = true };
            var graph = new GraphConfiguration { Nodes = [source, inserted, destination, wetDestination], Edges = [cable] };
            require(GraphConnectionEditing.TryPrepareInsertion(graph, inserted.Id, cable.Id, out var insertion, out _)
                && GraphConnectionEditing.TryApply(graph, insertion!, out _)
                && graph.Edges.Single(edge => edge.To == inserted.Id) is { GainDb: -3.25, Muted: true } incoming
                && incoming.FromPort == sourcePort && graph.Edges.Single(edge => edge.From == inserted.Id).FromPort == "audio",
                kind + " remains insertable through its primary Audio output, retaining the source socket and cable weighting.");
            var detachable = graph.Copy();
            require(GraphConnectionEditing.TryPrepareDetach(detachable, inserted.Id, out var detach, out _)
                && GraphConnectionEditing.TryApply(detachable, detach!, out _)
                && detachable.Edges.Single() is { GainDb: -3.25, Muted: true } bridge && bridge.FromPort == sourcePort,
                "An Audio-only " + kind + " remains detachable and preserves an upstream alternate or Audio socket on the restored bridge.");
            graph.Edges.Add(new() { Id = "separate-wet-branch", From = inserted.Id, FromPort = "wet", To = wetDestination.Id });
            string before = Json(graph);
            require(!GraphConnectionEditing.TryPrepareDetach(graph, inserted.Id, out _, out _) && Json(graph) == before,
                "Detaching " + kind + " with a separate output branch is rejected without changing cables or replacing wet-only audio with dry signal.");
        }
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
