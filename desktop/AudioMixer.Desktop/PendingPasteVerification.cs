using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace AudioMixer.Desktop;

// Uses the real optimistic workspace action and pending replay without a pipe,
// native window, system clipboard, audio stream or personal settings.
internal static class PendingPasteVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null && mixer.PendingEditCount == 0,
            "Pending paste checks start in a detached preview with no live engine or outstanding edits.");
        try
        {
            var baseline = Fixture();
            Load(baseline);
            string before = Json(mixer.GraphConfig);
            require(!mixer.TryEdit((MixerConfiguration c, out string error) =>
                { c.Graph.Nodes.Clear(); c.StartAtLogin = !c.StartAtLogin; error = "Refused initial operation"; return false; }, out string initialError)
                && initialError == "Refused initial operation" && Json(mixer.GraphConfig) == before
                && mixer.PendingEditCount == 0 && !mixer.CanUndo && !mixer.CanRedo,
                "An initially refused checked edit returns its error without publishing partial changes, queuing a save or adding history.");

            foreach (bool dependentHistory in new[] { false, true })
            {
                baseline = Fixture(); Load(baseline); await layout();
                string sourceId = baseline.Configuration.Graph.Nodes[0].Id;
                require(workspace.DuplicateInput(sourceId) && mixer.GraphConfig.Nodes.Count == 32
                    && mixer.PendingEditCount == 1 && mixer.EditHistory.Entries.Count == 1,
                    "Duplicating the thirty-first node queues one real workspace paste and one undo step.");
                if (dependentHistory)
                {
                    require(mixer.Undo() && mixer.Redo(), "The pending duplicate can be undone and redone before acknowledgement.");
                    mixer.StartAtLogin = true;
                    require(mixer.PendingEditCount == 4, "Duplicate, Undo, Redo and a later independent preference are all pending.");
                }
                var external = Competing(baseline);
                string externalGraph = Json(external.Configuration.Graph);
                mixer.ApplyPreviewSnapshot(external); await layout();
                require(Json(mixer.GraphConfig) == externalGraph && Json(external.Configuration.Graph) == externalGraph
                    && mixer.PendingEditCount == (dependentHistory ? 1 : 0) && !mixer.CanUndo && !mixer.CanRedo,
                    "A competing full workspace drops the failed duplicate and dependent history while preserving the exact external graph.");
                require(mixer.Warning.Contains("32 nodes") && mixer.Warning.Contains("Undo and redo were reset")
                    && workspace.HasFeedback && workspace.FeedbackText.StartsWith("Attention:")
                    && workspace.FeedbackText.Contains("32 nodes"),
                    "A paste rejected during replay visibly explains the workspace limit and history reset instead of leaving a stale success message.");
                var remaining = mixer.PreparePendingConfiguration(external.Configuration);
                require(Json(remaining.Configuration.Graph) == externalGraph
                    && remaining.Edits.Count == (dependentHistory ? 1 : 0)
                    && (!dependentHistory || remaining.Configuration.StartAtLogin),
                    "The failed paste never retries; a later unrelated preference remains eligible to save.");
                if (dependentHistory)
                    mixer.AcknowledgePending(remaining, Snapshot(remaining.Configuration));
                require(mixer.PendingEditCount == 0 && !mixer.CanUndo && !mixer.CanRedo,
                    "After reconciliation no failed paste or dependent Undo/Redo remains queued or available in history.");
            }

            baseline = Fixture(); Load(baseline); await layout();
            require(workspace.DuplicateInput(baseline.Configuration.Graph.Nodes[0].Id),
                "A duplicate with available capacity still succeeds through the checked edit path.");
            string duplicated = Json(mixer.GraphConfig);
            var batch = mixer.PreparePendingConfiguration(baseline.Configuration);
            require(batch.Edits.Count == 1 && Json(batch.Configuration.Graph) == duplicated,
                "Normal queued duplication replays to the complete intended graph with the same generated node identity.");
            mixer.AcknowledgePending(batch, Snapshot(batch.Configuration)); await layout();
            require(mixer.PendingEditCount == 0 && mixer.EditHistory.Entries.Count == 1 && mixer.CanUndo && !mixer.CanRedo
                && Json(mixer.GraphConfig) == duplicated,
                "A successful duplicate acknowledgement retains exactly one usable undo step.");
            require(mixer.Undo() && Json(mixer.GraphConfig) == Json(baseline.Configuration.Graph)
                && mixer.Redo() && Json(mixer.GraphConfig) == duplicated,
                "Acknowledged duplication remains reversible without losing its original source or generated copy.");
            var restored = mixer.PreparePendingConfiguration(batch.Configuration);
            mixer.AcknowledgePending(restored, Snapshot(restored.Configuration));
        }
        finally
        {
            mixer.QueuePreviewEdits = false;
        }
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); workspace.DismissMessage(); await layout();

        void Load(MixerSnapshot snapshot)
        {
            mixer.QueuePreviewEdits = false;
            mixer.ApplyPreviewSnapshot(snapshot);
            mixer.QueuePreviewEdits = true;
        }
    }

    private static MixerSnapshot Fixture()
    {
        var fixture = UiPreview.CreateSnapshot([]); fixture.Configuration.Graph = new(); fixture.Status.Nodes = [];
        fixture.Configuration.Revision = 10;
        for (int index = 0; index < 31; index++)
        {
            var node = GraphDefaults.NewNode(index == 0 ? "microphone" : "gain", index * 8, 0);
            node.Id = "pending-paste-" + index; node.Name = index == 0 ? "Voice" : "Processor " + index;
            node.Collapsed = true;
            if (index == 0) node.Device = new() { Id = "preview-mic1", Name = "Studio microphone", Channel = -1 };
            fixture.Configuration.Graph.Nodes.Add(node);
        }
        return fixture;
    }

    private static MixerSnapshot Competing(MixerSnapshot baseline)
    {
        var next = Snapshot(baseline.Configuration);
        var added = GraphDefaults.NewNode("gain", 600, 32); added.Id = "competing-final-node"; added.Name = "External addition";
        added.GainDb = -3; added.Collapsed = true; next.Configuration.Graph.Nodes.Add(added);
        next.Configuration.Graph.Nodes[1].Name = "Externally renamed processor";
        return next;
    }

    private static MixerSnapshot Snapshot(MixerConfiguration configuration)
    {
        var snapshot = UiPreview.CreateSnapshot([]); snapshot.Configuration = configuration.Copy(); snapshot.Configuration.Revision++;
        snapshot.Status.Nodes = []; return snapshot;
    }

    private static string Json(GraphConfiguration graph) => JsonSerializer.Serialize(graph, Protocol.Json);
}
