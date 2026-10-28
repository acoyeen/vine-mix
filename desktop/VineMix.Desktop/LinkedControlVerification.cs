using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace VineMix.Desktop;

internal static class LinkedControlVerification
{
    internal static void Run(Action<bool, string> require)
    {
        static GraphNode Node(string kind, string id)
        {
            var node = GraphDefaults.NewNode(kind, 0, 0); node.Id = id; return node;
        }
        var control = Node("control", "control");
        require(control.ControlMultiplier == 1 && control.GainDb == 0 && !control.Muted
            && LinkedLevelControl.GetPercent(control) == 100, "New Linked control starts at a neutral 100% multiplier.");
        foreach (double percent in new[] { 0d, .1, 25, 50, 100, 125, 200 })
        {
            control.Muted = true; control.GainDb = -12;
            LinkedLevelControl.SetPercent(control, percent);
            require(control.ControlMultiplier == percent / 100 && LinkedLevelControl.GetPercent(control) == percent
                && control.GainDb == 0 && !control.Muted, "Explicit multiplier edits preserve their scalar, reset obsolete dB and release a legacy mute.");
            var roundtrip = JsonSerializer.Deserialize<GraphNode>(JsonSerializer.Serialize(control, Protocol.Json), Protocol.Json)!;
            require(roundtrip.ControlMultiplier == control.ControlMultiplier && roundtrip.Muted == control.Muted,
                "Multiplier values including exact silence survive engine-protocol persistence.");
        }
        foreach (double invalid in new[] { -.1, 200.1, double.NaN, double.PositiveInfinity })
        {
            string before = JsonSerializer.Serialize(control, Protocol.Json);
            bool rejected = false;
            try { LinkedLevelControl.SetPercent(control, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            require(rejected && JsonSerializer.Serialize(control, Protocol.Json) == before, "Invalid multiplier input is rejected without partial changes.");
        }
        var legacy = JsonSerializer.Deserialize<GraphNode>("{\"id\":\"legacy\",\"kind\":\"control\",\"gainDb\":-12,\"muted\":true}", Protocol.Json)!;
        string legacyJson = JsonSerializer.Serialize(legacy, Protocol.Json);
        require(legacy.ControlMultiplier == -1 && Math.Abs(LinkedLevelControl.GetPercent(legacy) - 25.1188643150958) < 1e-10
            && !LinkedLevelControl.HasLegacyValue(legacy), "Old dB controllers display the equivalent percentage without losing gain precision.");
        require(JsonSerializer.Serialize(legacy, Protocol.Json) == legacyJson && legacy.Muted,
            "Reading a saved percentage does not migrate, round, save or unmute the old controller.");
        legacy.GainDb = 12;
        require(LinkedLevelControl.HasLegacyValue(legacy) && LinkedLevelControl.GetPercent(legacy) > 398,
            "A saved boost above 200% is detected instead of silently clamped or displayed as 200%.");
        var reset = legacy.Copy(); GraphDefaults.ResetNodeSettings(reset);
        require(reset.ControlMultiplier == 1 && reset.GainDb == 0 && !reset.Muted, "An explicit reset converts an older controller to neutral 100%.");
        var graph = new GraphConfiguration { Nodes = [control] };
        foreach (double invalid in new[] { -2, -.1, 2.01, double.NaN, double.PositiveInfinity })
        {
            control.ControlMultiplier = invalid;
            require(GraphDefaults.Validate(graph).Length > 0, "Non-finite and out-of-range stored multipliers fail validation.");
        }
        control.ControlMultiplier = -1;
        require(GraphDefaults.Validate(graph) == "", "The legacy multiplier sentinel remains valid.");
        control.ControlMultiplier = 1;

        string[] supported = ["microphone", "virtualInput", "appInput", "output", "virtualOutput", "mix", "gain", "reroute"];
        foreach (string kind in GraphDefaults.Kinds)
        {
            var target = Node(kind, "target"); graph.Nodes = [control, target]; graph.Edges = [];
            var edge = new GraphEdge { Id = "link", From = control.Id, To = target.Id, Kind = "control" };
            bool accepts = supported.Contains(kind);
            require(LinkedLevelControl.SupportsInput(kind) == accepts && LinkedLevelControl.ShowInput(graph, target) == accepts,
                kind + " offers a new Level control input only when it has a dedicated Level knob.");
            require((GraphDefaults.NewConnectionError(graph, edge).Length == 0) == accepts,
                kind + " enforces the same policy for new control connections.");
            if (accepts || kind == "control") continue;
            graph.Edges.Add(edge);
            require(GraphDefaults.Validate(graph) == "" && LinkedLevelControl.ShowInput(graph, target)
                && LinkedLevelControl.HasLegacyConnection(graph, target),
                kind + " preserves an older linked connection as a visible compatibility socket.");
            string clipboard = GraphClipboard.Serialize(graph, [control.Id, target.Id]);
            require(GraphClipboard.TryCreatePaste(clipboard, new(), new Point(40, 40), out var pasted, out _)
                && pasted.Edges.Single().Kind == "control"
                && GraphDefaults.Validate(new() { Nodes = pasted.Nodes.ToList(), Edges = pasted.Edges.ToList() }) == "",
                kind + " retains older control connections when copied as a group.");
        }

        var effect = Node("denoiser", "effect"); var otherEffect = Node("ducking", "other-effect"); var mix = Node("mix", "mix");
        var savedEdge = new GraphEdge { Id = "saved-link", From = control.Id, To = effect.Id, Kind = "control", GainDb = -3, Muted = true };
        graph = new() { Nodes = [control, effect, otherEffect, mix], Edges = [savedEdge] };
        require(!GraphConnectionEditing.TryPrepareRewire(graph, savedEdge, otherEffect.Id, out _, out _),
            "Rewiring a saved control cable cannot create a new unsupported effect connection.");
        require(GraphConnectionEditing.TryPrepareRewire(graph, savedEdge, effect.Id, out _, out _),
            "Returning a dragged legacy cable to its original endpoint preserves the connection.");
        var beforeMove = graph.Copy();
        require(GraphConnectionEditing.TryPrepareRewire(graph, savedEdge, mix.Id, out var move, out _)
            && GraphConnectionEditing.TryApply(graph, move!, out _) && graph.Edges.Single().To == mix.Id
            && graph.Edges.Single().GainDb == -3 && graph.Edges.Single().Muted,
            "A legacy control cable can move to Mix while retaining cable level and mute settings.");
        var restore = new WorkspaceGraphRestore(graph.Copy(), beforeMove);
        require(restore.TryApply(graph, out var restored, out _) && WorkspaceHistory.SameContents(restored, beforeMove),
            "Undo can restore the exact old connection without converting or dropping it.");

        var clipboardLegacy = JsonNode.Parse(GraphClipboard.Serialize(beforeMove, beforeMove.Nodes.Select(n => n.Id)))!;
        foreach (var node in clipboardLegacy["nodes"]!.AsArray()) node!.AsObject().Remove("controlMultiplier");
        require(GraphClipboard.TryCreatePaste(clipboardLegacy.ToJsonString(), new(), new Point(), out var oldPaste, out _)
            && oldPaste.Nodes.All(n => n.ControlMultiplier == -1), "Older clipboard records retain dB semantics when the multiplier field is absent.");

        var zero = beforeMove.Copy(); LinkedLevelControl.SetPercent(zero.Nodes[0], 0); zero.Nodes[0].Bypass = true;
        var zeroCopy = zero.Copy();
        require(WorkspaceHistory.SameContents(zero, zeroCopy) && zeroCopy.Nodes[0].ControlMultiplier == 0 && zeroCopy.Nodes[0].Bypass,
            "Silence and bypass remain distinct stored settings during graph copies.");
        var zeroRestore = new WorkspaceGraphRestore(zero, beforeMove);
        require(zeroRestore.TryApply(zero, out restored, out _) && WorkspaceHistory.SameContents(restored, beforeMove),
            "Undo restores pre-multiplier control state exactly.");
    }
}
