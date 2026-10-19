using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>
/// Group-edit checks against the same operations used by pointer and keyboard
/// handlers. The caller supplies a detached preview workspace: no native window,
/// clipboard, input injection, engine connection or personal settings are used.
/// </summary>
internal static class GraphGroupVerification
{
    public static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<string>? capture = null)
    {
        require(mixer.IsPreview, "Group editing verification uses an inert preview model only.");
        CheckClipboardPayloads(require);
        var fixture = UiPreview.CreateSnapshot([]);
        var microphone = Node("microphone", "group-microphone", -160, 80);
        var eq = Node("eq", "group-eq", 400, 80);
        eq.EqBands[2].FrequencyHz = 1730; eq.EqBands[2].GainDb = -5.5; eq.EqBands[2].Q = 1.7;
        eq.GainDb = -4; eq.Bypass = true;
        var compressor = Node("compressor", "group-compressor", 1120, 96);
        compressor.Compressor.ThresholdDb = -23; compressor.Compressor.Ratio = 3.5;
        compressor.Compressor.AttackMs = 7; compressor.Compressor.ReleaseMs = 180;
        var output = Node("output", "group-output", 1700, 96);
        var control = Node("control", "group-control", 416, 800);
        control.GainDb = -8; control.Muted = true;
        var unrelatedGain = Node("gain", "unrelated-gain", 400, 1200);
        var unrelatedMix = Node("mix", "unrelated-mix", 1120, 1200);
        fixture.Configuration.Graph = new()
        {
            Nodes = [microphone, eq, compressor, output, control, unrelatedGain, unrelatedMix],
            Edges = [Edge("external-in", microphone, eq), Edge("internal-audio", eq, compressor, gain: -3),
                Edge("external-out", compressor, output), Edge("internal-control", control, eq, "control"),
                Edge("unrelated-edge", unrelatedGain, unrelatedMix)],
            Zoom = .5, PanX = 120, PanY = 72
        };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        require(GraphDefaults.Validate(mixer.GraphConfig).Length == 0, "The group fixture starts as a valid graph with independent audio and control cables.");
        var selectedIds = new[] { eq.Id, compressor.Id, control.Id };
        Point Screen(Point point) => new(point.X * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanX,
            point.Y * mixer.GraphConfig.Zoom + mixer.GraphConfig.PanY);
        GraphNode Current(string id) => mixer.GraphConfig.Nodes.Single(node => node.Id == id);
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(node => node.Id == id);
        Rect Bounds(string id)
        {
            var view = View(id);
            return new(Canvas.GetLeft(view), Canvas.GetTop(view), view.ActualWidth, view.ActualHeight);
        }
        void RequireSelection(IEnumerable<string> ids, string description) =>
            require(workspace.SelectedNodeIds.ToHashSet(StringComparer.Ordinal).SetEquals(ids), description);
        long presentations = mixer.ConfigurationPresentationCount;
        var groupBounds = Bounds(eq.Id); groupBounds.Union(Bounds(compressor.Id)); groupBounds.Union(Bounds(control.Id)); groupBounds.Inflate(8, 8);
        workspace.SelectRectangle(Screen(groupBounds.TopLeft), Screen(groupBounds.BottomRight), false);
        RequireSelection(selectedIds, "A drag rectangle selects all intersecting nodes at 50% zoom with a nonzero pan.");
        workspace.SelectRectangle(Screen(groupBounds.BottomRight), Screen(groupBounds.TopLeft), false);
        RequireSelection(selectedIds, "Reverse-direction rectangle selection selects the same group.");
        Rect eqBounds = Bounds(eq.Id);
        workspace.SelectRectangle(Screen(new(eqBounds.Right - 1, eqBounds.Top + 8)), Screen(new(eqBounds.Right + 4, eqBounds.Top + 20)), false);
        RequireSelection([eq.Id], "A rectangle touching part of a node selects that node without requiring full enclosure.");
        var otherBounds = Bounds(unrelatedGain.Id); otherBounds.Inflate(4, 4);
        workspace.SelectRectangle(Screen(otherBounds.TopLeft), Screen(otherBounds.BottomRight), true);
        RequireSelection([eq.Id, unrelatedGain.Id], "Additive box selection retains the prior selection and adds intersecting nodes.");
        workspace.SelectRectangle(Screen(new(-500, -500)), Screen(new(-400, -400)), true);
        RequireSelection([eq.Id, unrelatedGain.Id], "An empty additive rectangle retains the selected nodes.");
        workspace.SelectRectangle(Screen(new(-500, -500)), Screen(new(-400, -400)), false);
        RequireSelection([], "An empty replacement rectangle clears the selection.");
        require(mixer.ConfigurationPresentationCount == presentations, "Selection changes do not enqueue configuration edits or alter audio routing.");

        workspace.SelectNode(unrelatedGain.Id);
        Point boxStart = Screen(groupBounds.TopLeft), boxEnd = Screen(groupBounds.BottomRight);
        workspace.BeginBoxSelection(boxStart, false); workspace.UpdateBoxSelection(boxEnd); await layout();
        RequireSelection(selectedIds, "The actual box gesture selects nodes continuously while the pointer moves.");
        require(workspace.SelectionBoxVisible && workspace.SelectionBoxBounds == new Rect(boxStart, boxEnd),
            "The selection overlay is drawn in viewport coordinates independently of canvas zoom and pan.");
        capture?.Invoke("group-box-selection.png");
        Point smallerEnd = Screen(new(eqBounds.Right + 4, eqBounds.Bottom + 4));
        workspace.UpdateBoxSelection(smallerEnd);
        RequireSelection([eq.Id], "Contracting a selection rectangle drops nodes that are no longer inside it.");
        workspace.FinishGesture(cancel: true);
        RequireSelection([unrelatedGain.Id], "Cancelling a box gesture restores the selection that existed before the drag.");
        require(!workspace.SelectionBoxVisible, "Cancelling a box gesture hides its overlay.");
        workspace.BeginBoxSelection(boxStart, true); workspace.UpdateBoxSelection(boxEnd);
        RequireSelection([.. selectedIds, unrelatedGain.Id], "An additive box gesture includes its initial selection and current rectangle.");
        workspace.UpdateBoxSelection(smallerEnd); workspace.FinishGesture();
        RequireSelection([eq.Id, unrelatedGain.Id], "Contracting an additive rectangle retains only the base selection and current intersections.");
        require(!workspace.SelectionBoxVisible && mixer.ConfigurationPresentationCount == presentations,
            "Completing a box selection hides the overlay without committing any configuration changes.");

        workspace.SelectRectangle(Screen(groupBounds.TopLeft), Screen(groupBounds.BottomRight), false);
        var starting = selectedIds.ToDictionary(id => id, id => new Point(Current(id).X, Current(id).Y));
        var untouched = new Point(Current(unrelatedGain.Id).X, Current(unrelatedGain.Id).Y);
        Point start = Screen(new(eqBounds.Left + 50, eqBounds.Top + 12));
        presentations = mixer.ConfigurationPresentationCount;
        workspace.BeginNodeMove(eq.Id, start); workspace.UpdateNodeMove(start + new Vector(21, 13));
        require(selectedIds.All(id => Close(Canvas.GetLeft(View(id)), starting[id].X + 42)
            && Close(Canvas.GetTop(View(id)), starting[id].Y + 26)),
            "Dragging one selected header previews the same zoom-corrected movement for every selected node.");
        require(selectedIds.All(id => Current(id).X == starting[id].X && Current(id).Y == starting[id].Y)
            && mixer.ConfigurationPresentationCount == presentations,
            "A live group drag moves visuals while keeping its configuration uncommitted until release.");
        mixer.RefreshPreviewTelemetry(); await layout();
        require(selectedIds.All(id => Close(Canvas.GetLeft(View(id)), starting[id].X + 42)
            && Close(Canvas.GetTop(View(id)), starting[id].Y + 26)),
            "Telemetry refreshes do not snap any member of an active group drag back to its saved position.");
        workspace.FinishNodeMove(); await layout();
        require(selectedIds.All(id => Close(Current(id).X, starting[id].X + 40) && Close(Current(id).Y, starting[id].Y + 24)),
            "Releasing a group drag snaps one shared movement to the eight-unit grid and preserves every relative offset.");
        require(mixer.ConfigurationPresentationCount == presentations + 1,
            "A multi-node move commits exactly one configuration edit.");
        require(new Point(Current(unrelatedGain.Id).X, Current(unrelatedGain.Id).Y) == untouched,
            "Group movement leaves unselected nodes unchanged.");
        RequireSelection(selectedIds, "Moving a selected header retains the whole selection.");

        starting = selectedIds.ToDictionary(id => id, id => new Point(Current(id).X, Current(id).Y));
        presentations = mixer.ConfigurationPresentationCount;
        workspace.BeginNodeMove(compressor.Id, start); workspace.UpdateNodeMove(start + new Vector(-29, 23)); workspace.FinishNodeMove(cancel: true); await layout();
        require(selectedIds.All(id => Current(id).X == starting[id].X && Current(id).Y == starting[id].Y
            && Close(Canvas.GetLeft(View(id)), starting[id].X) && Close(Canvas.GetTop(View(id)), starting[id].Y)),
            "Cancelling a group drag restores every visual and saved position.");
        require(mixer.ConfigurationPresentationCount == presentations, "Cancelling a group move creates no configuration edit.");
        workspace.BeginNodeMove(eq.Id, start); workspace.FinishNodeMove();
        require(mixer.ConfigurationPresentationCount == presentations, "Clicking a selected header without moving does not commit a redundant layout edit.");

        string copied = workspace.CopySelection();
        require(!string.IsNullOrWhiteSpace(copied), "Copying a selected group produces a portable node payload without accessing the system clipboard.");
        var originalIds = mixer.GraphConfig.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        string originalEdges = JsonSerializer.Serialize(mixer.GraphConfig.Edges, Protocol.Json);
        workspace.NodeChanged(eq.Id, node => node.EqBands[2].GainDb = -1);
        presentations = mixer.ConfigurationPresentationCount;
        require(workspace.PasteSelection(copied, new(1800, 800)), "The copied group pastes through the workspace's real group action.");
        await layout();
        var clones = mixer.GraphConfig.Nodes.Where(node => !originalIds.Contains(node.Id)).ToArray();
        var cloneIds = clones.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        require(clones.Length == 3 && cloneIds.Count == 3, "Pasting creates a distinct new identity for every selected node.");
        RequireSelection(cloneIds, "Pasting selects only the newly created group for immediate repositioning.");
        var clonedEq = clones.Single(node => node.Kind == "eq");
        var clonedCompressor = clones.Single(node => node.Kind == "compressor");
        var clonedControl = clones.Single(node => node.Kind == "control");
        require(clonedEq.EqBands[2].GainDb == -5.5 && clonedEq.EqBands[2].FrequencyHz == 1730 && clonedEq.EqBands[2].Q == 1.7
            && clonedEq.Bypass && clonedEq.GainDb == -4 && clonedEq.Collapsed,
            "Copying snapshots EQ settings, bypass, gain and collapsed state independently of later edits to the original.");
        require(clonedCompressor.Compressor.ThresholdDb == -23 && clonedCompressor.Compressor.Ratio == 3.5
            && clonedCompressor.Compressor.AttackMs == 7 && clonedCompressor.Compressor.ReleaseMs == 180
            && clonedControl.GainDb == -8 && clonedControl.Muted,
            "Pasted processor and linked-control nodes retain their parameter and mute settings.");
        require(Close(clonedCompressor.X - clonedEq.X, starting[compressor.Id].X - starting[eq.Id].X)
            && Close(clonedCompressor.Y - clonedEq.Y, starting[compressor.Id].Y - starting[eq.Id].Y)
            && Close(clonedControl.X - clonedEq.X, starting[control.Id].X - starting[eq.Id].X)
            && Close(clonedControl.Y - clonedEq.Y, starting[control.Id].Y - starting[eq.Id].Y),
            "A pasted group retains its relative two-dimensional arrangement.");
        var clonedEdges = mixer.GraphConfig.Edges.Where(edge => cloneIds.Contains(edge.From) || cloneIds.Contains(edge.To)).ToArray();
        require(clonedEdges.Length == 2 && clonedEdges.All(edge => cloneIds.Contains(edge.From) && cloneIds.Contains(edge.To))
            && clonedEdges.Any(edge => edge.From == clonedEq.Id && edge.To == clonedCompressor.Id && edge.Kind == "audio" && edge.GainDb == -3)
            && clonedEdges.Any(edge => edge.From == clonedControl.Id && edge.To == clonedEq.Id && edge.Kind == "control"),
            "Paste recreates internal audio/control cables with settings, without reconnecting the group to external nodes.");
        require(mixer.ConfigurationPresentationCount == presentations + 1,
            "An entire pasted group and its cables commit in one configuration edit.");
        require(GraphDefaults.Validate(mixer.GraphConfig).Length == 0, "The pasted group preserves graph validation and unique cable identities.");
        if (capture is not null)
        {
            double savedPanX = mixer.GraphConfig.PanX, savedPanY = mixer.GraphConfig.PanY;
            mixer.Edit(configuration => { configuration.Graph.PanX = -500; configuration.Graph.PanY = 20; });
            await layout(); capture("group-pasted.png");
            mixer.Edit(configuration => { configuration.Graph.PanX = savedPanX; configuration.Graph.PanY = savedPanY; });
            await layout();
        }

        presentations = mixer.ConfigurationPresentationCount;
        workspace.DeleteSelection(); await layout();
        require(mixer.GraphConfig.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal).SetEquals(originalIds)
            && JsonSerializer.Serialize(mixer.GraphConfig.Edges, Protocol.Json) == originalEdges,
            "Deleting a pasted group removes exactly its nodes and internal cables, preserving the existing graph.");
        require(mixer.ConfigurationPresentationCount == presentations + 1, "Deleting a group commits exactly one configuration edit.");
        RequireSelection([], "Deleting the selected group clears stale selected identities.");

        require(workspace.PasteSelection(copied, new(1800, 800)), "The same copied group can be pasted again.");
        await layout();
        var firstRepeat = workspace.SelectedNodeIds.ToHashSet(StringComparer.Ordinal);
        require(workspace.PasteSelection(copied, new(2200, 1200)), "Repeated paste can place another independent group.");
        require(firstRepeat.Count == 3 && workspace.SelectedNodeIds.Count == 3 && !workspace.SelectedNodeIds.Any(firstRepeat.Contains)
            && mixer.GraphConfig.Nodes.Select(node => node.Id).Distinct().Count() == mixer.GraphConfig.Nodes.Count
            && mixer.GraphConfig.Edges.Select(edge => edge.Id).Distinct().Count() == mixer.GraphConfig.Edges.Count,
            "Repeated pastes never reuse a node or cable identity from an earlier group.");
        workspace.DeleteSelection(); await layout();
        var firstRepeatBounds = Bounds(firstRepeat.First());
        foreach (string id in firstRepeat.Skip(1)) firstRepeatBounds.Union(Bounds(id));
        firstRepeatBounds.Inflate(2, 2);
        workspace.SelectRectangle(Screen(firstRepeatBounds.TopLeft), Screen(firstRepeatBounds.BottomRight), false);
        workspace.DeleteSelection(); await layout();

        // Delete the original processing group, including the cables crossing
        // its boundary. The separate gain-to-mix route must remain byte-for-byte.
        groupBounds = Bounds(eq.Id); groupBounds.Union(Bounds(compressor.Id)); groupBounds.Union(Bounds(control.Id)); groupBounds.Inflate(8, 8);
        workspace.SelectRectangle(Screen(groupBounds.TopLeft), Screen(groupBounds.BottomRight), false);
        presentations = mixer.ConfigurationPresentationCount;
        workspace.DeleteSelection(); await layout();
        require(mixer.GraphConfig.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal)
            .SetEquals([microphone.Id, output.Id, unrelatedGain.Id, unrelatedMix.Id])
            && mixer.GraphConfig.Edges.Count == 1 && mixer.GraphConfig.Edges[0].Id == "unrelated-edge",
            "Deleting a group removes all incident audio/control cables and leaves unrelated nodes and routing intact.");
        require(mixer.ConfigurationPresentationCount == presentations + 1, "Deleting a group with external connections is one atomic configuration edit.");
        require(GraphDefaults.Validate(mixer.GraphConfig).Length == 0, "Group deletion leaves no dangling endpoints or invalid connections.");

        var pairBounds = Bounds(unrelatedGain.Id); pairBounds.Union(Bounds(unrelatedMix.Id)); pairBounds.Inflate(4, 4);
        workspace.SelectRectangle(Screen(pairBounds.TopLeft), Screen(pairBounds.BottomRight), false);
        double pairOffset = Current(unrelatedMix.Id).X - Current(unrelatedGain.Id).X;
        presentations = mixer.ConfigurationPresentationCount;
        workspace.BeginNodeMove(unrelatedGain.Id, new(0, 0)); workspace.UpdateNodeMove(new(100000, 100000)); workspace.FinishNodeMove();
        require(Current(unrelatedMix.Id).X == 100000 && Current(unrelatedGain.Id).Y == 100000
            && Current(unrelatedMix.Id).X - Current(unrelatedGain.Id).X == pairOffset
            && mixer.ConfigurationPresentationCount == presentations + 1,
            "A group crossing the positive canvas limit is clamped together without changing relative offsets.");
        workspace.BeginNodeMove(unrelatedGain.Id, new(0, 0)); workspace.UpdateNodeMove(new(-200000, -200000)); workspace.FinishNodeMove();
        require(Current(unrelatedGain.Id).X == -100000 && Current(unrelatedGain.Id).Y == -100000
            && Current(unrelatedMix.Id).X - Current(unrelatedGain.Id).X == pairOffset,
            "A group crossing the negative canvas limit is clamped together without changing relative offsets.");

        foreach (DependencyObject editor in new DependencyObject[]
        {
            new TextBox(), new RichTextBox(), new PasswordBox(), new ComboBox(), new Slider(),
            new EqualizerEditor(), new RotaryGainControl("Low", "200 Hz", _ => { }),
            new RotaryValueControl("Frequency", 20, 20000, 1, "Hz", 1000, _ => { }, logarithmic: true)
        })
            require(GraphWorkspace.IsEditingControl(editor), editor.GetType().Name + " keeps its own Delete and clipboard keys instead of triggering graph actions.");
        var template = new ControlTemplate(typeof(Slider)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        var host = new Slider { Template = template }; host.ApplyTemplate();
        var child = VisualTreeHelper.GetChild(host, 0);
        require(GraphWorkspace.IsEditingControl(child), "A child inside an editable control also keeps keyboard input local to that control.");
        require(!GraphWorkspace.IsEditingControl(new Border()) && !GraphWorkspace.IsEditingControl(null),
            "Canvas keyboard commands remain available when focus is outside node editors.");
    }

    private static void CheckClipboardPayloads(Action<bool, string> require)
    {
        var first = Node("eq", "copy-eq", 40, 56); var second = Node("compressor", "copy-compressor", 480, 80);
        var control = Node("control", "copy-control", 48, 240);
        var graph = new GraphConfiguration { Nodes = [first, second, control],
            Edges = [Edge("copy-audio", first, second, gain: -3), Edge("copy-control-edge", control, first, "control")] };
        string payload = GraphClipboard.Serialize(graph, graph.Nodes.Select(node => node.Id));
        require(GraphClipboard.Serialize(graph, []).Length == 0, "Copying an empty selection produces no node payload.");
        require(GraphClipboard.TryCreatePaste(payload, new(), new(800, 160), out var copy, out _)
            && copy.Nodes.Min(node => node.X) == 800 && copy.Nodes.Min(node => node.Y) == 160,
            "A valid clipboard group is positioned from its top-left corner in world coordinates.");
        require(copy.Nodes.All(node => graph.Nodes.All(original => original.Id != node.Id))
            && copy.Edges.All(edge => graph.Edges.All(original => original.Id != edge.Id)),
            "Pasted identities are fresh even when the destination does not contain the copied originals.");
        copy.Nodes[0].EqBands[0].GainDb = 9; copy.Nodes[1].Compressor.Ratio = 9;
        require(first.EqBands[0].GainDb == 0 && second.Compressor.Ratio == 4,
            "Pasted filter and compressor settings share no mutable objects with copied originals.");

        var target = new GraphConfiguration { Nodes = [Node("gain", "keep-node", 0, 0)] };
        string before = JsonSerializer.Serialize(target, Protocol.Json);
        void Reject(string bad, string description, Point? anchor = null)
        {
            require(!GraphClipboard.TryCreatePaste(bad, target, anchor ?? new Point(100, 100), out var rejected, out string error)
                && rejected.Nodes.Count == 0 && rejected.Edges.Count == 0 && error.Length > 0
                && JsonSerializer.Serialize(target, Protocol.Json) == before, description);
        }
        string Alter(Action<JsonObject> change)
        {
            var json = JsonNode.Parse(payload)!.AsObject(); change(json); return json.ToJsonString();
        }
        var oldEffects = JsonNode.Parse(payload)!.AsObject();
        foreach (var oldNode in oldEffects["nodes"]!.AsArray())
        {
            foreach (string optional in new[] { "denoiser", "deEsser", "plosive", "reverb" }) oldNode!.AsObject().Remove(optional);
            foreach (var band in oldNode!["eqBands"]!.AsArray()) band!.AsObject().Remove("resetFrequencyHz");
        }
        require(GraphClipboard.TryCreatePaste(oldEffects.ToJsonString(), new(), new Point(30, 50), out var oldEffectsCopy, out _)
            && oldEffectsCopy.Nodes.All(node => node.Denoiser.SuppressionDb == -18 && node.DeEsser.FrequencyHz == 6000
                && node.Plosive.FrequencyHz == 150 && node.Reverb.Mix == .2 && node.EqBands.All(band => band.ResetFrequencyHz == 0)),
            "Old version-one clipboard groups without new effect objects or EQ reset anchors remain accepted with compatible defaults.");
        foreach (var (name, field) in new[] { ("denoiser", "suppressionDb"), ("deEsser", "frequencyHz"), ("plosive", "frequencyHz"), ("reverb", "mix") })
        {
            Reject(Alter(json => json["nodes"]![0]![name]!["unexpected"] = true), "Unknown nested " + name + " fields reject the complete group.");
            Reject(Alter(json => json["nodes"]![0]![name]!.AsObject().Remove(field)), "A present but incomplete " + name + " object rejects the complete group.");
            Reject(Alter(json => json["nodes"]![0]![name] = null), "Null " + name + " settings reject the complete group.");
            var duplicateRoot = JsonNode.Parse(payload)!.AsObject();
            string nested = duplicateRoot["nodes"]![0]![name]!.ToJsonString();
            string duplicated = nested.Replace("\"" + field + "\":", "\"" + field + "\":0,\"" + field + "\":", StringComparison.Ordinal);
            Reject(duplicateRoot.ToJsonString().Replace(nested, duplicated, StringComparison.Ordinal),
                "Duplicate nested " + name + " fields are rejected instead of silently using one value.");
        }
        foreach (var (name, field, invalid) in new[] { ("denoiser", "suppressionDb", -41d), ("deEsser", "frequencyHz", 12001d),
            ("deEsser", "thresholdDb", -61d), ("deEsser", "reductionDb", 25d), ("plosive", "frequencyHz", 59d),
            ("plosive", "thresholdDb", 1d), ("plosive", "reductionDb", -1d), ("reverb", "mix", 1.1),
            ("reverb", "decaySeconds", 5.1), ("reverb", "damping", -.1), ("reverb", "preDelayMs", 101d) })
            Reject(Alter(json => json["nodes"]![0]![name]![field] = invalid), "Out-of-range clipboard " + name + " " + field + " rejects all nodes and routes together.");
        Reject(Alter(json => json["nodes"]![0]!["eqBands"]![0]!["resetFrequencyHz"] = 10), "Out-of-range clipboard reset-frequency anchors reject the group.");
        Reject(Alter(json => json["nodes"]![0]!["eqBands"]![0]!["unexpectedAnchor"] = 80), "Unknown EQ anchor fields reject the group.");
        Reject(payload.Replace("\"resetFrequencyHz\":", "\"resetFrequencyHz\":0,\"resetFrequencyHz\":", StringComparison.Ordinal),
            "Duplicate EQ reset-frequency fields reject the complete clipboard group.");
        Reject("", "Empty clipboard input is rejected without changing the graph.");
        Reject("not-json", "Unrelated clipboard text is rejected without changing the graph.");
        Reject(Alter(json => json["format"] = "different-app"), "A clipboard payload from another format is rejected.");
        Reject(Alter(json => json["version"] = 2), "An unsupported clipboard format version is rejected.");
        Reject(Alter(json => json["unexpected"] = true), "Unknown clipboard fields do not silently enter the graph.");
        Reject(Alter(json => json["nodes"]![0]!.AsObject().Remove("gainDb")), "Missing required node settings reject the entire clipboard group.");
        Reject(Alter(json => json["nodes"]![0]!["mono"] = "true"), "A non-boolean Mono setting rejects the clipboard group without changing the destination.");
        Reject(Alter(json => json["nodes"]![0] = null), "A null clipboard node is rejected without throwing or changing the graph.");
        Reject(Alter(json => json["nodes"]![0] = "invalid"), "A scalar clipboard node is rejected without throwing or changing the graph.");
        Reject(Alter(json => json["nodes"]![0]!["device"] = null), "Null nested device settings reject the entire clipboard group.");
        Reject(Alter(json => json["nodes"]![0]!["eqBands"]![0]!["frequencyHz"] = 0), "Out-of-range EQ settings reject the entire clipboard group.");
        Reject(Alter(json => json["nodes"]![1]!["id"] = "copy-eq"), "Duplicate node identities reject the entire clipboard group.");
        Reject(Alter(json => json["edges"]![0]!["to"] = "outside-node"), "Clipboard cables cannot refer to nodes outside the copied group.");
        Reject(Alter(json => json["edges"]![1]!["id"] = "copy-audio"), "Duplicate cable identities reject the entire clipboard group.");
        Reject(Alter(json => json["edges"]!.AsArray().Add(JsonSerializer.SerializeToNode(
            Edge("cycle", second, first), Protocol.Json))), "Clipboard audio feedback cycles reject the entire group.");
        Reject(new string('x', GraphClipboard.MaximumBytes + 1), "Oversized clipboard data is bounded and rejected before deserialization.");
        Reject(payload, "Non-finite paste coordinates reject the group without partial edits.", new Point(double.NaN, 0));
        Reject(payload, "Groups extending beyond the canvas coordinate limit are rejected intact.", new Point(99999, 0));

        var crowded = new GraphConfiguration { Nodes = Enumerable.Range(0, 30).Select(index => Node("gain", "full-" + index, index * 8, 0)).ToList() };
        string crowdedBefore = JsonSerializer.Serialize(crowded, Protocol.Json);
        require(!GraphClipboard.TryCreatePaste(payload, crowded, new(0, 0), out var tooMany, out _)
            && tooMany.Nodes.Count == 0 && JsonSerializer.Serialize(crowded, Protocol.Json) == crowdedBefore,
            "A paste exceeding 32 nodes rejects the whole group without inserting a partial selection.");
        var cableFull = new GraphConfiguration { Nodes = Enumerable.Range(0, 16).Select(index => Node("gain", "dense-" + index, index * 8, 0)).ToList() };
        for (int from = 0; from < cableFull.Nodes.Count; from++)
            for (int to = from + 1; to < cableFull.Nodes.Count && cableFull.Edges.Count < 96; to++)
                cableFull.Edges.Add(Edge("dense-edge-" + cableFull.Edges.Count, cableFull.Nodes[from], cableFull.Nodes[to]));
        require(GraphDefaults.Validate(cableFull).Length == 0, "The capacity fixture contains 96 valid acyclic connections.");
        string cableFullBefore = JsonSerializer.Serialize(cableFull, Protocol.Json);
        require(!GraphClipboard.TryCreatePaste(payload, cableFull, new(0, 0), out var tooManyEdges, out _)
            && tooManyEdges.Nodes.Count == 0 && JsonSerializer.Serialize(cableFull, Protocol.Json) == cableFullBefore,
            "A paste exceeding 96 connections rejects all nodes and cables together.");

        var mic = Node("microphone", "bound-mic", 0, 0);
        mic.Device = new() { Id = "capture-device", StableId = "capture-stable", Name = "Chosen microphone", Channel = 3 }; mic.GainDb = -7; mic.Mono = true;
        var output = Node("output", "bound-output", 400, 0);
        output.Device = new() { Id = "render-device", StableId = "render-stable", Name = "Chosen output", Channel = -1 }; output.Muted = false;
        var app = Node("appInput", "bound-app", 800, 0);
        app.App = new() { ProcessId = 123, ExecutablePath = "C:\\Preview\\Player.exe", Name = "Chosen app" };
        var virtualInput = Node("virtualInput", "bound-virtual-input", 0, 400);
        virtualInput.Device = mic.Device.Copy(); virtualInput.Device.Channel = 1; virtualInput.GainDb = -3; virtualInput.Muted = true;
        var bound = new GraphConfiguration { Nodes = [mic, output, app, virtualInput] };
        string bindings = GraphClipboard.Serialize(bound, bound.Nodes.Select(node => node.Id));
        var oldBindings = JsonNode.Parse(bindings)!.AsObject();
        foreach (var node in oldBindings["nodes"]!.AsArray()) node!.AsObject().Remove("mono");
        require(oldBindings["version"]!.GetValue<int>() == 1
            && GraphClipboard.TryCreatePaste(oldBindings.ToJsonString(), new(), new(100, 100), out var legacyCopy, out _)
            && legacyCopy.Nodes.All(node => !node.Mono),
            "Version-one clipboard data from before Mono was added remains accepted and defaults the omitted optional setting to false.");
        var captureConflict = Node("virtualInput", "existing-capture", 0, 0); captureConflict.Device.Id = mic.Device.Id;
        var renderConflict = Node("virtualOutput", "existing-render", 0, 0); renderConflict.Device.StableId = output.Device.StableId;
        var appConflict = Node("appInput", "existing-app", 0, 0); appConflict.App = new() { ExecutablePath = app.App.ExecutablePath.ToUpperInvariant() };
        var existing = new GraphConfiguration { Nodes = [captureConflict, renderConflict, appConflict] };
        string existingBefore = JsonSerializer.Serialize(existing, Protocol.Json);
        require(GraphClipboard.TryCreatePaste(bindings, existing, new(100, 100), out var conflicted, out _) && conflicted.ClearedAssignments == 1,
            "Pasting retains shared capture and app sources while clearing only the conflicting output assignment.");
        require(conflicted.Nodes.Single(node => node.Kind == "microphone") is { Device.Id: "capture-device", Device.StableId: "capture-stable", Device.Channel: 3, GainDb: -7, Mono: true }
            && conflicted.Nodes.Single(node => node.Kind == "output") is { Device.Id: "", Device.StableId: "", Muted: false }
            && conflicted.Nodes.Single(node => node.Kind == "appInput") is { App.ProcessId: 123, App.ExecutablePath: "C:\\Preview\\Player.exe" }
            && conflicted.Nodes.Single(node => node.Kind == "virtualInput") is { Device.Id: "capture-device", Device.Channel: 1, GainDb: -3, Muted: true },
            "Copies preserve all three input source bindings, Mono, channel selection, gain and mute independently of output ownership.");
        require(JsonSerializer.Serialize(existing, Protocol.Json) == existingBefore
            && mic.Device.Id == "capture-device" && app.App.ProcessId == 123,
            "Resolving clipboard assignment conflicts never changes any original node or endpoint binding.");
        require(GraphClipboard.TryCreatePaste(bindings, new(), new(100, 100), out var distinct, out _) && distinct.ClearedAssignments == 0
            && distinct.Nodes.Single(node => node.Kind == "microphone").Device.Id == mic.Device.Id
            && distinct.Nodes.Single(node => node.Kind == "output").Device.StableId == output.Device.StableId
            && distinct.Nodes.Single(node => node.Kind == "appInput").App.ProcessId == app.App.ProcessId,
            "A destination without an output conflict retains every copied binding, including two inputs sharing one capture endpoint.");
        var micCopy = conflicted.Nodes.Single(node => node.Kind == "microphone");
        var appCopy = conflicted.Nodes.Single(node => node.Kind == "appInput");
        var virtualCopy = conflicted.Nodes.Single(node => node.Kind == "virtualInput");
        micCopy.Device.Channel = 0; micCopy.GainDb = -18; micCopy.Muted = true; micCopy.Mono = false;
        appCopy.App.ProcessId = 456; virtualCopy.Device.Name = "Copy only";
        require(mic is { Device.Channel: 3, GainDb: -7, Muted: false, Mono: true }
            && app.App.ProcessId == 123 && virtualInput.Device.Name == "Chosen microphone"
            && virtualCopy is { Device.Channel: 1, GainDb: -3, Muted: true },
            "Changing a copied input's binding, channel, level or mute cannot modify its original or another copy sharing that source.");

        var replay = new GraphConfiguration();
        require(GraphClipboard.TryCreatePaste(payload, replay, new(0, 0), out var replayPaste, out _)
            && GraphClipboard.TryAppend(replay, replayPaste, out _) && replay.Nodes.Count == 3 && replay.Edges.Count == 2,
            "A queued paste appends its complete validated group in one operation.");
        string appended = JsonSerializer.Serialize(replay, Protocol.Json);
        require(GraphClipboard.TryAppend(replay, replayPaste, out _) && JsonSerializer.Serialize(replay, Protocol.Json) == appended,
            "Replaying an already-applied paste is idempotent and creates no duplicate nodes or cables.");
        require(!GraphClipboard.TryAppend(crowded, replayPaste, out _) && JsonSerializer.Serialize(crowded, Protocol.Json) == crowdedBefore,
            "If the destination fills while paste is queued, replay rejects the whole group atomically.");
        var collision = new GraphConfiguration { Nodes = [Node("gain", "collision-from", 0, 0), Node("gain", "collision-to", 0, 0)] };
        collision.Edges.Add(Edge(replayPaste.Edges[0].Id, collision.Nodes[0], collision.Nodes[1]));
        string collisionBefore = JsonSerializer.Serialize(collision, Protocol.Json);
        require(!GraphClipboard.TryAppend(collision, replayPaste, out _) && JsonSerializer.Serialize(collision, Protocol.Json) == collisionBefore,
            "A cable identity collision during replay rejects the group before adding any nodes.");
        require(GraphClipboard.TryAppend(existing, distinct, out _)
            && existing.Nodes.Single(node => node.Id == distinct.Nodes.Single(node => node.Kind == "microphone").Id).Device.Id == mic.Device.Id
            && existing.Nodes.Single(node => node.Id == distinct.Nodes.Single(node => node.Kind == "appInput").Id).App.ExecutablePath == app.App.ExecutablePath
            && existing.Nodes.Single(node => node.Id == distinct.Nodes.Single(node => node.Kind == "virtualInput").Id).Device.Id == virtualInput.Device.Id
            && existing.Nodes.Single(node => node.Id == distinct.Nodes.Single(node => node.Kind == "output").Id).Device.Id == ""
            && distinct.Nodes.Single(node => node.Kind == "microphone").Device.Id == mic.Device.Id,
            "Paste replay retains shared input assignments, clears only a newly conflicting output and preserves its IDs and queued template.");
    }

    private static GraphNode Node(string kind, string id, double x, double y)
    {
        var node = GraphDefaults.NewNode(kind, x, y); node.Id = id; node.Name = id; node.Collapsed = true; return node;
    }
    private static GraphEdge Edge(string id, GraphNode from, GraphNode to, string kind = "audio", double gain = 0) =>
        new() { Id = id, From = from.Id, To = to.Id, Kind = kind, GainDb = gain };
    private static bool Close(double first, double second) => Math.Abs(first - second) < .000001;
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
