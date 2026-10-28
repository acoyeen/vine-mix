using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace VineMix.Desktop;

/// <summary>Detached persistence/replay checks; no settings files or engine connection.</summary>
internal static class NamedFramePersistenceVerification
{
    internal static void Run(Action<bool, string> require)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        var first = GraphDefaults.NewNode("gain", 40, 80); first.Id = "frame-persist-first";
        var second = GraphDefaults.NewNode("mix", 500, 80); second.Id = "frame-persist-second";
        fixture.Configuration.Graph = new GraphConfiguration { Nodes = [first, second],
            Edges = [new() { Id = "frame-persist-cable", From = first.Id, To = second.Id }] };
        using var mixer = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
        mixer.ApplyPreviewSnapshot(fixture);
        string engineBefore = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        mixer.Edit(configuration => configuration.Graph.Frames.Add(new GraphFrame
        { Id = "frame-persist", Name = "Voice chain", ColorKey = "sage", NodeIds = [first.Id, second.Id] }));
        require(mixer.PendingEditCount == 0 && mixer.CanUndo
            && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == engineBefore,
            "Creating an organizational frame changes local history/preferences without changing engine JSON or queueing configuration.");
        string saved = mixer.SerializeUiPreferences();
        require(saved.Contains("Voice chain", StringComparison.Ordinal), "Frames are included in desktop workspace preferences.");
        var copy = mixer.GraphConfig.Copy(); copy.Frames[0].Name = "Detached"; copy.Frames[0].NodeIds.Clear();
        require(mixer.GraphConfig.Frames[0].Name == "Voice chain" && mixer.GraphConfig.Frames[0].NodeIds.Count == 2,
            "Graph copies isolate frame names and member lists.");
        require(mixer.Undo() && mixer.GraphConfig.Frames.Count == 0 && mixer.PendingEditCount == 0
            && mixer.Redo() && mixer.GraphConfig.Frames.Count == 1 && mixer.PendingEditCount == 0,
            "Frame-only Undo and Redo stay entirely in the desktop metadata layer.");

        using (var reopened = new MixerViewModel(preview: true))
        {
            reopened.LoadUiPreferencesJson(saved);
            reopened.ApplyPreviewSnapshot(fixture);
            require(GraphFrames.Same(reopened.GraphConfig.Frames, mixer.GraphConfig.Frames),
                "Frame identity, name, color and membership reload from preferences after an engine snapshot arrives.");
            reopened.RefreshPreviewTelemetry();
            require(reopened.GraphConfig.Frames.Count == 1, "Telemetry refresh leaves reloaded frame metadata intact.");
        }

        mixer.Edit(configuration => configuration.Graph.Nodes[0].X += 32);
        require(mixer.PendingEditCount == 1, "Moving a framed node uses the existing one-edit layout save path.");
        var batch = mixer.PreparePendingConfiguration(fixture.Configuration);
        mixer.Edit(configuration => configuration.Graph.Frames[0].Name = "Renamed while saving");
        require(mixer.PendingEditCount == 1, "A frame rename during a pending layout save does not enqueue another engine change.");
        var acknowledgement = UiPreview.CreateSnapshot([]);
        acknowledgement.Configuration = batch.Configuration.Copy(); acknowledgement.Configuration.Revision++;
        // A real engine protocol round trip contains no Frames property.
        acknowledgement.Configuration = JsonSerializer.Deserialize<MixerConfiguration>(
            JsonSerializer.Serialize(acknowledgement.Configuration, Protocol.Json), Protocol.Json)!;
        mixer.AcknowledgePending(batch, acknowledgement);
        require(mixer.PendingEditCount == 0 && mixer.GraphConfig.Frames[0].Name == "Renamed while saving"
            && mixer.GraphConfig.Nodes[0].X == 72 && mixer.CanUndo,
            "An engine acknowledgement cannot erase a newer local frame rename or invalidate its Undo history.");
        require(mixer.Undo() && mixer.GraphConfig.Frames[0].Name == "Voice chain" && mixer.PendingEditCount == 0,
            "Undoing the newer rename leaves the acknowledged layout untouched and needs no engine request.");
        require(mixer.Undo() && mixer.GraphConfig.Nodes[0].X == 40 && mixer.GraphConfig.Frames.Count == 1
            && mixer.PendingEditCount == 1, "Undoing an acknowledged member move preserves its frame while queueing the layout restoration.");
        var undoBatch = mixer.PreparePendingConfiguration(acknowledgement.Configuration);
        require(undoBatch.Configuration.Graph.Nodes[0].X == 40 && mixer.PendingEditCount == 1,
            "Queued layout Undo replays against an engine graph without requiring desktop frame metadata.");
        acknowledgement.Configuration = undoBatch.Configuration.Copy(); acknowledgement.Configuration.Revision++;
        mixer.AcknowledgePending(undoBatch, acknowledgement);
        require(mixer.GraphConfig.Frames.Count == 1 && mixer.GraphConfig.Frames[0].NodeIds.Count == 2,
            "Acknowledging layout Undo retains the complete local frame membership.");

        mixer.Edit(configuration =>
        {
            configuration.Graph.Nodes.RemoveAll(node => node.Id == first.Id);
            configuration.Graph.Edges.Clear();
        });
        require(mixer.GraphConfig.Frames.Single().NodeIds.SequenceEqual([second.Id]),
            "Deleting a member prunes that membership without removing the remaining frame.");
        require(mixer.Undo() && mixer.GraphConfig.Frames.Single().NodeIds.SequenceEqual([first.Id, second.Id]),
            "Undoing node deletion restores its exact original frame membership.");
        mixer.Edit(configuration => { configuration.Graph.Nodes.Clear(); configuration.Graph.Edges.Clear(); });
        require(mixer.GraphConfig.Frames.Count == 0, "Deleting the last members removes an empty frame.");
        require(mixer.Undo() && mixer.GraphConfig.Frames.Count == 1,
            "Undoing deletion of all members restores the frame and its nodes together.");

        using (var malformed = new MixerViewModel(preview: true))
        {
            malformed.LoadUiPreferencesJson("""
                {"frames":[null,{"id":"","name":"invalid","nodeIds":["frame-persist-first"]},
                  {"id":"good","name":"  ","colorKey":"unknown","nodeIds":["frame-persist-first","frame-persist-first","missing",null]},
                  {"id":"second","name":"Second","colorKey":"blue","nodeIds":["frame-persist-first","frame-persist-second"]},
                  {"id":"empty","name":"Empty","nodeIds":null}]}
                """);
            malformed.ApplyPreviewSnapshot(fixture);
            require(malformed.GraphConfig.Frames.Count == 2
                && malformed.GraphConfig.Frames[0] is { Name: "Group", ColorKey: "sage" }
                && malformed.GraphConfig.Frames[0].NodeIds.SequenceEqual([first.Id])
                && malformed.GraphConfig.Frames[1].NodeIds.SequenceEqual([second.Id]),
                "Malformed preferences discard invalid/empty groups, unknown nodes and duplicate membership with safe name/color defaults.");
        }
        using (var old = new MixerViewModel(preview: true))
        {
            old.LoadUiPreferencesJson("{}"); old.ApplyPreviewSnapshot(fixture);
            require(old.GraphConfig.Frames.Count == 0, "Older workspace preferences load without requiring any frame migration.");
        }
        using (var rejected = new MixerViewModel(preview: true) { QueuePreviewEdits = true })
        {
            rejected.LoadUiPreferencesJson(saved); rejected.ApplyPreviewSnapshot(fixture);
            rejected.Edit(configuration =>
            {
                configuration.Graph.Nodes.RemoveAll(node => node.Id == first.Id);
                configuration.Graph.Edges.Clear();
            });
            var rejectedBatch = rejected.PreparePendingConfiguration(fixture.Configuration);
            rejected.Edit(configuration => configuration.Graph.Frames.Single().Name = "Keep newer name");
            rejected.RejectPending(rejectedBatch, fixture, "Detached simulated rejection");
            require(rejected.GraphConfig.Nodes.Count == 2 && rejected.GraphConfig.Frames.Single().NodeIds.Count == 2
                && rejected.GraphConfig.Frames.Single().Name == "Keep newer name" && rejected.PendingEditCount == 0,
                "Rejecting node deletion restores its previous frame membership while preserving a newer local rename.");
            rejected.Edit(configuration => { configuration.Graph.Nodes.Clear(); configuration.Graph.Edges.Clear(); });
            rejectedBatch = rejected.PreparePendingConfiguration(fixture.Configuration);
            rejected.RejectPending(rejectedBatch, fixture, "Detached simulated rejection");
            require(rejected.GraphConfig.Frames.Single().Name == "Keep newer name"
                && rejected.GraphConfig.Frames.Single().NodeIds.Count == 2,
                "Rejecting deletion of all members restores the previously removed frame.");
        }

        var clipboardGraph = fixture.Configuration.Graph.Copy();
        clipboardGraph.Frames = [new GraphFrame { Id = "copied-frame", Name = "Copy", ColorKey = "teal", NodeIds = [first.Id, second.Id] }];
        string clipboard = GraphClipboard.Serialize(clipboardGraph, [first.Id, second.Id]);
        var malicious = JsonNode.Parse(clipboard)!.AsObject();
        malicious["frames"]![0]!["nodeIds"]!.AsArray().Add("outside-selection");
        require(!GraphClipboard.TryCreatePaste(malicious.ToJsonString(), new(), new Point(0, 0), out _, out _),
            "Clipboard frame metadata cannot claim nodes outside the copied selection.");
        malicious = JsonNode.Parse(clipboard)!.AsObject();
        malicious["frames"]![0]!["colorKey"] = "arbitrary-color";
        require(!GraphClipboard.TryCreatePaste(malicious.ToJsonString(), new(), new Point(0, 0), out _, out _),
            "Clipboard frames accept curated palette keys only.");

        using (var queuedPaste = new MixerViewModel(preview: true) { QueuePreviewEdits = true })
        {
            queuedPaste.ApplyPreviewSnapshot(fixture);
            require(GraphClipboard.TryCreatePaste(clipboard, queuedPaste.GraphConfig, new Point(900, 400), out var paste, out _),
                "A framed clipboard fixture prepares for queued replay.");
            require(queuedPaste.TryEdit((MixerConfiguration configuration, out string error) =>
                GraphClipboard.TryAppend(configuration.Graph, paste, out error), out _),
                "A framed paste queues through the real checked edit path.");
            queuedPaste.Edit(configuration => configuration.Graph.Frames.Single().Name = "Newest name");
            var pasteBatch = queuedPaste.PreparePendingConfiguration(fixture.Configuration);
            var pasted = UiPreview.CreateSnapshot([]); pasted.Configuration = pasteBatch.Configuration.Copy(); pasted.Configuration.Revision++;
            queuedPaste.AcknowledgePending(pasteBatch, pasted);
            require(queuedPaste.GraphConfig.Frames.Single().Name == "Newest name" && queuedPaste.PendingEditCount == 0,
                "Replaying and acknowledging an older framed paste retains a newer local rename.");
            require(queuedPaste.Undo() && queuedPaste.GraphConfig.Frames.Single().Name == "Copy",
                "The rename remains undoable after the paste is acknowledged.");
            require(queuedPaste.Undo() && queuedPaste.GraphConfig.Frames.Count == 0,
                "Undoing a framed paste removes its copied frame with its nodes.");
            var removeBatch = queuedPaste.PreparePendingConfiguration(pasted.Configuration);
            pasted.Configuration = removeBatch.Configuration.Copy(); pasted.Configuration.Revision++;
            queuedPaste.AcknowledgePending(removeBatch, pasted);
            require(queuedPaste.Redo() && queuedPaste.GraphConfig.Frames.Count == 1,
                "Redoing a framed paste restores the metadata with the copied nodes.");
            queuedPaste.Edit(configuration => configuration.Graph.Frames.Clear());
            var redoBatch = queuedPaste.PreparePendingConfiguration(pasted.Configuration);
            pasted.Configuration = redoBatch.Configuration.Copy(); pasted.Configuration.Revision++;
            queuedPaste.AcknowledgePending(redoBatch, pasted);
            require(queuedPaste.GraphConfig.Frames.Count == 0 && queuedPaste.PendingEditCount == 0,
                "An acknowledged queued framed paste cannot resurrect a group removed by a newer local edit.");
        }

        foreach (bool duringPreparation in new[] { false, true })
        {
            using var conflict = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
            conflict.ApplyPreviewSnapshot(fixture);
            require(GraphClipboard.TryCreatePaste(clipboard, conflict.GraphConfig, new Point(900, 400), out var paste, out _)
                && conflict.TryEdit((MixerConfiguration configuration, out string error) =>
                    GraphClipboard.TryAppend(configuration.Graph, paste, out error), out _),
                "A framed paste is prepared for the rebase-conflict fixture.");
            var pasteBatch = conflict.PreparePendingConfiguration(fixture.Configuration);
            var acknowledged = UiPreview.CreateSnapshot([]);
            acknowledged.Configuration = JsonSerializer.Deserialize<MixerConfiguration>(
                JsonSerializer.Serialize(pasteBatch.Configuration, Protocol.Json), Protocol.Json)!;
            acknowledged.Configuration.Revision++;
            conflict.AcknowledgePending(pasteBatch, acknowledged);
            string frameId = conflict.GraphConfig.Frames.Single().Id;
            require(conflict.Undo() && conflict.Redo() && conflict.Undo()
                && conflict.GraphConfig.Frames.Count == 0 && conflict.PendingEditCount == 3,
                "Undo/Redo/Undo optimistically removes a pasted frame before its queued restoration is acknowledged.");
            var changed = UiPreview.CreateSnapshot([]); changed.Configuration = acknowledged.Configuration.Copy();
            changed.Configuration.Graph.Nodes.Single(node => node.Id == paste.Nodes[0].Id).GainDb = -17;
            changed.Configuration.Revision++;
            if (duringPreparation) conflict.PreparePendingConfiguration(changed.Configuration);
            else conflict.ApplyPreviewSnapshot(changed);
            require(conflict.PendingEditCount == 0 && !conflict.CanUndo && !conflict.CanRedo
                && conflict.GraphConfig.Frames.Single().Id == frameId
                && conflict.GraphConfig.Frames.Single().NodeIds.ToHashSet().SetEquals(paste.Nodes.Select(node => node.Id))
                && conflict.GraphConfig.Nodes.Single(node => node.Id == paste.Nodes[0].Id).GainDb == -17,
                (duringPreparation ? "Preparing a pending batch" : "Accepting an engine revision")
                + " restores the lost frame when a conflicting Undo and downstream restores are dropped, retaining the newer engine node value.");
            using var reopened = new MixerViewModel(preview: true);
            reopened.LoadUiPreferencesJson(conflict.SerializeUiPreferences()); reopened.ApplyPreviewSnapshot(changed);
            require(reopened.GraphConfig.Frames.Single().Id == frameId,
                "Rebase-conflict frame restoration is included in the persisted workspace preferences.");
        }
    }
}
