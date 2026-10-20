using System;
using System.Linq;
using System.Text.Json;

namespace AudioMixer.Desktop;

/// <summary>Detached history and engine-queue reconciliation checks. No process, pipe, device or settings I/O.</summary>
internal static class WorkspaceHistoryVerification
{
    private static MixerConfiguration Configuration()
    {
        var input = GraphDefaults.NewNode("microphone", 20, 30); input.Id = "history-input";
        var effect = GraphDefaults.NewNode("eq", 400, 30); effect.Id = "history-effect";
        effect.EqBands = EqBand.Defaults(); // Existing nested-settings isolation fixture.
        var output = GraphDefaults.NewNode("output", 800, 30); output.Id = "history-output";
        return new()
        {
            Version = 2, Revision = 1,
            Graph = new()
            {
                Nodes = [input, effect, output],
                Edges = [new() { Id = "history-in", From = input.Id, To = effect.Id }, new() { Id = "history-out", From = effect.Id, To = output.Id }]
            }
        };
    }
    private static MixerSnapshot Snapshot(MixerConfiguration configuration) => new() { Configuration = configuration.Copy() };
    private static GraphNode Effect(MixerConfiguration configuration) => configuration.Graph.Nodes.Single(node => node.Id == "history-effect");
    private static GraphNode Effect(MixerViewModel mixer) => mixer.GraphConfig.Nodes.Single(node => node.Id == "history-effect");
    private static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static MixerViewModel Queued(MixerConfiguration configuration)
    {
        var mixer = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
        mixer.ApplyPreviewSnapshot(Snapshot(configuration));
        return mixer;
    }

    internal static void Run(Action<bool, string> require)
    {
        using (var mixer = new MixerViewModel(preview: true))
        {
            mixer.ApplyPreviewSnapshot(Snapshot(Configuration()));
            mixer.Edit(c => Effect(c).Bypass = true);
            mixer.Edit(c => Effect(c).Bypass = false);
            require(mixer.EditHistory.Entries.Count == 2 && mixer.Undo() && Effect(mixer).Bypass,
                "Two rapid bypass toggles are separate undo steps, not a time-based merged action.");
            require(mixer.Redo() && !Effect(mixer).Bypass && !mixer.CanRedo,
                "Redo restores the second toggle and reaches the end of history.");
            mixer.Undo();
            mixer.Edit(c => Effect(c).Name = "New branch");
            require(!mixer.CanRedo, "A new graph edit after Undo discards only the abandoned redo branch.");

            mixer.ApplyPreviewSnapshot(Snapshot(Configuration()));
            mixer.BeginEditGesture(); mixer.BeginEditGesture();
            mixer.Edit(c => Effect(c).GainDb = -4);
            mixer.Edit(c => Effect(c).GainDb = -9);
            mixer.EndEditGesture();
            require(mixer.EditHistory.Entries.Count == 0 && mixer.CanUndo,
                "Nested pointer edits stay one pending transaction until the outer gesture ends.");
            mixer.EndEditGesture(); mixer.EndEditGesture();
            require(mixer.EditHistory.Entries.Count == 1 && mixer.Undo() && Effect(mixer).GainDb == 0
                && mixer.Redo() && Effect(mixer).GainDb == -9,
                "A complete multi-value gesture is one reversible step; extra end notifications are harmless.");
            int count = mixer.EditHistory.Entries.Count;
            mixer.BeginEditGesture(); mixer.Edit(c => Effect(c).GainDb = -10); mixer.Edit(c => Effect(c).GainDb = -9); mixer.EndEditGesture();
            require(mixer.EditHistory.Entries.Count == count, "A gesture returning to its initial value creates no undo step.");

            mixer.StartAtLogin = true;
            mixer.SetCanvasView(123, -45, .75);
            require(mixer.EditHistory.Entries.Count == count && mixer.Undo()
                && mixer.StartAtLogin && mixer.GraphConfig.PanX == 123 && mixer.GraphConfig.PanY == -45 && mixer.GraphConfig.Zoom == .75,
                "Undo preserves current camera navigation and unrelated start-at-login preferences.");

            mixer.ApplyPreviewSnapshot(Snapshot(Configuration()));
            string original = Json(mixer.GraphConfig);
            mixer.Edit(c => { c.Graph.Nodes.RemoveAll(node => node.Id == "history-effect"); c.Graph.Edges.Clear(); });
            require(mixer.Undo() && Json(mixer.GraphConfig) == original && mixer.Redo()
                && mixer.GraphConfig.Nodes.Count == 2 && mixer.GraphConfig.Edges.Count == 0,
                "Delete restores node settings, identity, original ordering and attached cables as one transaction.");
            mixer.ApplyPreviewSnapshot(Snapshot(Configuration()));
            require(!mixer.CanUndo && !mixer.CanRedo, "A fresh detached preview fixture starts with empty undo/redo history.");
        }

        VerifySnapshotIsolationAndCapacity(require);
        VerifyGuardedRestores(require);
        VerifyAcknowledgementAndReconnect(require);
        VerifyRejectionAndExternalRevision(require);
        VerifyAssignmentRestoration(require);
        VerifySharedInputHistory(require);
    }

    private static void VerifySnapshotIsolationAndCapacity(Action<bool, string> require)
    {
        var before = Configuration().Graph; var after = before.Copy(); after.Nodes[1].EqBands[0].GainDb = 3;
        var history = new WorkspaceHistory(); history.Record(before, after);
        before.Nodes[1].EqBands[0].GainDb = 9;
        require(history.TryUndo(after, out var restored, out _, out _) && restored.Nodes[1].EqBands[0].GainDb == 0,
            "History owns deep copies of nested sound settings instead of retaining mutable caller snapshots.");
        history.Clear(); var current = Configuration().Graph;
        for (int i = 0; i < WorkspaceHistory.Capacity + 5; i++)
        { var next = current.Copy(); next.Nodes[1].X += 8; history.Record(current, next); current = next; }
        int undos = 0;
        while (history.TryUndo(current, out var previous, out _, out _)) { current = previous; undos++; }
        require(undos == WorkspaceHistory.Capacity && current.Nodes[1].X == 440,
            "The bounded history retains exactly its last 100 steps without restoring beyond its retained baseline.");
    }

    private static void VerifyGuardedRestores(Action<bool, string> require)
    {
        var before = Configuration().Graph; var after = before.Copy(); after.Nodes[1].GainDb = -6;
        var restore = new WorkspaceGraphRestore(after, before);
        var remote = after.Copy(); remote.Nodes[2].Name = "External output name";
        var extra = GraphDefaults.NewNode("mix", 1200, 30); extra.Id = "external-node"; remote.Nodes.Add(extra);
        remote.PanX = 321; remote.Zoom = .5;
        require(restore.TryApply(remote, out var merged, out _) && merged.Nodes[1].GainDb == 0
            && merged.Nodes[2].Name == "External output name" && merged.Nodes.Any(node => node.Id == extra.Id)
            && merged.PanX == 321 && merged.Zoom == .5,
            "A rebased undo changes only its affected entity, preserving unrelated external graph edits and navigation.");
        string mergedJson = Json(merged);
        require(restore.TryApply(merged, out var repeated, out _) && Json(repeated) == mergedJson,
            "A restore replayed after a lost acknowledgement is idempotent.");
        remote.Nodes[1].Name = "Externally edited effect"; string unchanged = Json(remote);
        require(!restore.TryApply(remote, out _, out string error) && error.Length > 0 && Json(remote) == unchanged,
            "A conflicting edit on the same node refuses the complete undo atomically without changing the external graph.");
        var withNewNode = before.Copy(); withNewNode.Nodes.Add(extra.Copy());
        var undoAdd = new WorkspaceGraphRestore(withNewNode, before);
        withNewNode.Edges.Add(new() { Id = "external-link", From = "external-node", To = "history-output" });
        require(!undoAdd.TryApply(withNewNode, out _, out _),
            "Undo refuses to remove a node when a new external cable would otherwise become dangling.");
        var assignedBefore = before.Copy(); assignedBefore.Nodes[0].Device.Id = "original-device";
        var assignedAfter = assignedBefore.Copy(); assignedAfter.Nodes[0].Device.Id = "replacement-device";
        var assignmentRestore = new WorkspaceGraphRestore(assignedAfter, assignedBefore);
        var otherInput = GraphDefaults.NewNode("microphone", 1200, 30); otherInput.Device.Id = "original-device";
        assignedAfter.Nodes.Add(otherInput);
        require(assignmentRestore.TryApply(assignedAfter, out var sharedInput, out _)
            && sharedInput.Nodes.Count(node => node.Device.Id == "original-device") == 2,
            "Undo restores a shared capture assignment while preserving another input lane using the same device.");
        foreach (string kind in new[] { "output", "virtualOutput" })
        {
            var outputBefore = before.Copy(); outputBefore.Nodes[2].Kind = kind; outputBefore.Nodes[2].Device.Id = "original-output";
            var outputAfter = outputBefore.Copy(); outputAfter.Nodes[2].Device.Id = "replacement-output";
            var outputRestore = new WorkspaceGraphRestore(outputAfter, outputBefore);
            var otherOutput = GraphDefaults.NewNode(kind == "output" ? "virtualOutput" : "output", 1200, 30);
            otherOutput.Device.Id = "original-output"; outputAfter.Nodes.Add(otherOutput);
            string unchangedOutputs = Json(outputAfter);
            require(!outputRestore.TryApply(outputAfter, out _, out _) && Json(outputAfter) == unchangedOutputs,
                "Undo keeps " + kind + " assignments exclusive when another playback node claimed the former device.");
        }
    }

    private static void VerifyAcknowledgementAndReconnect(Action<bool, string> require)
    {
        var baseline = Configuration(); using var mixer = Queued(baseline);
        mixer.Edit(c => Effect(c).GainDb = -6);
        var inFlight = mixer.PreparePendingConfiguration(baseline);
        require(mixer.Undo(), "An optimistic graph edit can be undone while its configure request is in flight.");
        var accepted = inFlight.Configuration.Copy(); accepted.Revision++;
        mixer.AcknowledgePending(inFlight, Snapshot(accepted));
        require(mixer.PendingEditCount == 1 && Effect(mixer).GainDb == 0 && mixer.CanRedo,
            "Acknowledging an earlier batch preserves the newer queued Undo and its redo history.");
        var undoBatch = mixer.PreparePendingConfiguration(accepted);
        require(Effect(undoBatch.Configuration).GainDb == 0, "The next engine request contains the pending restored value.");
        var undoAccepted = undoBatch.Configuration.Copy(); undoAccepted.Revision++;
        // Simulate the engine applying the restore but its response being lost.
        mixer.ApplyPreviewSnapshot(Snapshot(undoAccepted));
        var replay = mixer.PreparePendingConfiguration(undoAccepted);
        require(Effect(mixer).GainDb == 0 && Effect(replay.Configuration).GainDb == 0 && mixer.CanRedo,
            "Reconnect and a lost restore acknowledgement retain the restored graph and usable redo history.");
        mixer.AcknowledgePending(undoBatch, Snapshot(undoAccepted));
        require(mixer.PendingEditCount == 0 && mixer.Redo() && Effect(mixer).GainDb == -6,
            "Acknowledgement removes only its original edits and Redo remains usable after reconnection.");
    }

    private static void VerifyRejectionAndExternalRevision(Action<bool, string> require)
    {
        var baseline = Configuration();
        using (var mixer = Queued(baseline))
        {
            mixer.Edit(c => Effect(c).GainDb = -6);
            var rejected = mixer.PreparePendingConfiguration(baseline);
            mixer.Undo(); mixer.StartAtLogin = true;
            mixer.RejectPending(rejected, Snapshot(baseline), "Rejected verification change");
            var remainder = mixer.PreparePendingConfiguration(baseline);
            require(mixer.PendingEditCount == 1 && Effect(mixer).GainDb == 0 && mixer.StartAtLogin
                && remainder.Configuration.StartAtLogin && !mixer.CanUndo && !mixer.CanRedo,
                "A hard engine rejection drops dependent Undo/Redo, clears stale history and preserves later unrelated preference edits.");
        }
        using (var mixer = Queued(baseline))
        {
            mixer.Edit(c => Effect(c).GainDb = -6);
            var acceptedBatch = mixer.PreparePendingConfiguration(baseline);
            var accepted = acceptedBatch.Configuration.Copy(); accepted.Revision++;
            mixer.AcknowledgePending(acceptedBatch, Snapshot(accepted)); mixer.Undo();
            var remote = accepted.Copy(); remote.Revision++; Effect(remote).Name = "External revision";
            mixer.ApplyPreviewSnapshot(Snapshot(remote));
            var retry = mixer.PreparePendingConfiguration(remote);
            require(mixer.PendingEditCount == 0 && retry.Edits.Count == 0 && Effect(mixer).GainDb == -6
                && Effect(mixer).Name == "External revision" && !mixer.CanUndo && !mixer.CanRedo
                && mixer.Warning.Contains("workspace changed", StringComparison.OrdinalIgnoreCase),
                "A conflicting revision refuses a queued restore, keeps the external result and explains why history was reset.");
        }
        using (var mixer = Queued(baseline))
        {
            mixer.Edit(c => Effect(c).GainDb = -6);
            var acceptedBatch = mixer.PreparePendingConfiguration(baseline);
            var accepted = acceptedBatch.Configuration.Copy(); accepted.Revision++;
            mixer.AcknowledgePending(acceptedBatch, Snapshot(accepted)); mixer.Undo();
            var remote = accepted.Copy(); remote.Revision++; remote.Graph.Nodes[2].Name = "Unrelated remote change";
            mixer.ApplyPreviewSnapshot(Snapshot(remote));
            var retry = mixer.PreparePendingConfiguration(remote);
            require(Effect(retry.Configuration).GainDb == 0 && retry.Configuration.Graph.Nodes[2].Name == "Unrelated remote change"
                && mixer.CanRedo, "A stale-revision retry preserves unrelated external edits while applying the intended Undo.");
        }
    }

    private static void VerifyAssignmentRestoration(Action<bool, string> require)
    {
        using var mixer = Queued(Configuration());
        var input = GraphDefaults.NewNode("virtualInput", 1200, 0); input.Id = "restored-virtual";
        var waiting = GraphDefaults.NewNode("virtualOutput", 1600, 0); waiting.Id = "untouched-waiting";
        mixer.Edit(c => { c.Graph.Nodes.Add(input.Copy()); c.Graph.Nodes.Add(waiting.Copy()); });
        var initial = mixer.PreparePendingConfiguration(Configuration());
        mixer.AcknowledgePending(initial, Snapshot(initial.Configuration));
        mixer.Edit(c => c.Graph.Nodes.Single(node => node.Id == input.Id).Device = new() { Id = "manual", Channel = -1 });
        require(mixer.Undo(), "Undo can explicitly restore a virtual node to its previous unassigned state.");
        var restored = mixer.PreparePendingConfiguration(initial.Configuration);
        mixer.AcknowledgePending(restored, Snapshot(restored.Configuration));
        var inventory = Snapshot(restored.Configuration);
        inventory.Endpoints = [new() { Id = "receive", StableId = "receive", Name = "Cable", Direction = "capture", Transport = "vbCable", State = "Active" },
            new() { Id = "send", StableId = "send", Name = "Cable", Direction = "render", Transport = "vacLite", State = "Active" }];
        mixer.ApplyPreviewSnapshot(inventory);
        require(!VirtualDeviceBindings.IsAssigned(mixer.GraphConfig.Nodes.Single(node => node.Id == input.Id).Device)
            && mixer.GraphConfig.Nodes.Single(node => node.Id == waiting.Id).Device.Id == "send",
            "Undo's explicit unassignment survives inventory refresh without suppressing automatic binding on an untouched waiting node.");
        using var automatic = Queued(new MixerConfiguration { Version = 2, Graph = new() { Nodes = [input.Copy()] } });
        var automaticSnapshot = new MixerSnapshot { Configuration = new() { Version = 2, Graph = new() { Nodes = [input.Copy()] } }, Endpoints = inventory.Endpoints };
        automatic.ApplyPreviewSnapshot(automaticSnapshot);
        require(automatic.GraphConfig.Nodes[0].Device.Id == "receive" && !automatic.CanUndo,
            "Automatic inventory assignment does not create a user undo step.");
    }

    private static void VerifySharedInputHistory(Action<bool, string> require)
    {
        foreach (string kind in new[] { "microphone", "virtualInput", "appInput" })
        {
            var baseline = Configuration();
            var source = baseline.Graph.Nodes[0]; source.Kind = kind;
            if (kind == "appInput")
                source.App = new() { ProcessId = 42, ExecutablePath = @"C:\AudioMixerVerification\shared-app.exe", Name = "Shared app" };
            else source.Device = new() { Id = "shared-capture", StableId = "shared-capture-stable", Name = "Shared source", Channel = -1 };
            var duplicate = source.Copy(); duplicate.Id = "history-duplicate-input"; duplicate.Name += " copy"; duplicate.X += 24;
            using var mixer = Queued(baseline);
            GraphNode Copy(MixerConfiguration configuration) => configuration.Graph.Nodes.Single(node => node.Id == duplicate.Id);
            GraphNode CurrentCopy() => mixer.GraphConfig.Nodes.Single(node => node.Id == duplicate.Id);
            GraphNode Original() => mixer.GraphConfig.Nodes.Single(node => node.Id == source.Id);
            mixer.Edit(c => c.Graph.Nodes.Add(duplicate.Copy()));
            string duplicatedGraph = Json(mixer.GraphConfig);
            require(mixer.Undo() && Json(mixer.GraphConfig) == Json(baseline.Graph)
                && mixer.Redo() && Json(mixer.GraphConfig) == duplicatedGraph,
                "Duplicating a " + kind + " with its shared source assignment is one complete undo/redo step.");

            mixer.Edit(c => Copy(c).GainDb = -12);
            mixer.Edit(c => Copy(c).Muted = true);
            require(CurrentCopy().GainDb == -12 && CurrentCopy().Muted && Original().GainDb == source.GainDb
                && Original().Muted == source.Muted && WorkspaceHistory.SameValue(Original().Device, source.Device)
                && WorkspaceHistory.SameValue(Original().App, source.App),
                "A duplicated " + kind + " has independent level and mute while the original source lane remains unchanged.");
            require(mixer.Undo() && !CurrentCopy().Muted && CurrentCopy().GainDb == -12
                && mixer.Undo() && CurrentCopy().GainDb == source.GainDb
                && mixer.Redo() && CurrentCopy().GainDb == -12 && mixer.Redo() && CurrentCopy().Muted,
                "Independent control edits remain individually reversible when " + kind + " assignments are shared.");
            var batch = mixer.PreparePendingConfiguration(baseline);
            require(WorkspaceHistory.SameContents(batch.Configuration.Graph, mixer.GraphConfig),
                "Queued duplication, controls and repeated Undo/Redo rebase to the intended shared " + kind + " graph.");
            mixer.AcknowledgePending(batch, Snapshot(batch.Configuration));
            require(mixer.PendingEditCount == 0 && mixer.CanUndo && Original().GainDb == source.GainDb,
                "Engine acknowledgement retains usable history and independent original controls for shared " + kind + " inputs.");

            if (kind == "appInput")
                mixer.Edit(c => Copy(c).App = new() { ProcessId = 43, ExecutablePath = @"C:\AudioMixerVerification\other-app.exe", Name = "Other app" });
            else mixer.Edit(c => Copy(c).Device = new() { Id = "other-capture", Name = "Other source", Channel = -1 });
            require(mixer.Undo() && WorkspaceHistory.SameValue(CurrentCopy().Device, source.Device)
                && WorkspaceHistory.SameValue(CurrentCopy().App, source.App)
                && CurrentCopy().Muted && CurrentCopy().GainDb == -12,
                "Undo restores a " + kind + " assignment already used by its original lane without changing the duplicate's controls.");
            require(mixer.Redo() && (kind == "appInput" ? CurrentCopy().App.ProcessId == 43 : CurrentCopy().Device.Id == "other-capture")
                && WorkspaceHistory.SameValue(Original().Device, source.Device) && WorkspaceHistory.SameValue(Original().App, source.App),
                "Redo reapplies only the duplicated " + kind + " assignment and leaves its original lane intact.");
        }
    }
}
