using System;
using System.Text.Json;

namespace VineMix.Desktop;

/// <summary>Real configure-response handling with in-memory edits, snapshots and a deterministic retry clock.</summary>
internal static class AutosaveVerification
{
    internal static void Run(Action<bool, string> require)
    {
        var baseline = new MixerConfiguration { Version = 2, Revision = 1,
            Graph = new() { Nodes = [GraphDefaults.NewNode("eq", 20, 30)] } };
        MixerSnapshot Snapshot(MixerConfiguration configuration) => new() { Configuration = configuration.Copy() };
        MixerViewModel Queued()
        {
            var mixer = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
            mixer.ApplyPreviewSnapshot(Snapshot(baseline)); return mixer;
        }
        var writeFailure = JsonSerializer.Deserialize<ProtocolResponse>(
            """{"ok":false,"error":"Could not write verification settings.","errorCode":"settings_write_failed"}""", Protocol.Json)!;
        var permanentFailure = JsonSerializer.Deserialize<ProtocolResponse>(
            """{"ok":false,"error":"Invalid graph configuration."}""", Protocol.Json)!;
        require(writeFailure.ErrorCode == "settings_write_failed" && permanentFailure.ErrorCode.Length == 0,
            "Optional persistence error codes deserialize without changing older untyped responses.");
        var now = TimeSpan.FromDays(1);
        using (var mixer = Queued())
        {
            mixer.Edit(c => c.Graph.Nodes[0].GainDb = -6);
            var failed = mixer.PreparePendingConfiguration(baseline);
            require(mixer.Undo() && mixer.CanRedo, "A queued Undo can arrive while an autosave request is in flight.");
            mixer.HandleConfigureResponse(failed, Snapshot(baseline), writeFailure, now);
            require(mixer.PendingEditCount == 2 && mixer.GraphConfig.Nodes[0].GainDb == 0 && mixer.CanRedo
                && mixer.EditHistory.Entries.Count == 0 && mixer.RedoDescription.Length > 0,
                "A settings write failure retains the original edit, newer Undo and its redo history.");
            require(mixer.CanEdit && mixer.SaveStatus == "Changes not saved · retrying…"
                && mixer.Warning.Contains("not saved", StringComparison.OrdinalIgnoreCase)
                && mixer.Warning.Contains(writeFailure.Error, StringComparison.Ordinal),
                "A connected engine with a write failure stays editable and clearly reports unsaved changes and the save error.");
            mixer.ApplyPreviewSnapshot(Snapshot(baseline));
            require(mixer.PendingEditCount == 2 && mixer.CanRedo && mixer.SaveStatus == "Changes not saved · retrying…"
                && mixer.Warning.Contains(writeFailure.Error, StringComparison.Ordinal),
                "Fresh telemetry cannot erase the unsaved warning or discard edits awaiting a persistence retry.");
            require(mixer.Redo() && mixer.GraphConfig.Nodes[0].GainDb == -6 && mixer.PendingEditCount == 3
                && mixer.SaveStatus == "Changes not saved · retrying…",
                "Redo remains usable after a write failure without falsely claiming that saving has recovered.");
            require(!mixer.IsPendingSaveDue(now + TimeSpan.FromMilliseconds(999))
                && mixer.IsPendingSaveDue(now + TimeSpan.FromSeconds(1)),
                "Persistence retries wait one second, and new edits cannot postpone or bypass that backoff.");
            var retry = mixer.PreparePendingConfiguration(baseline);
            require(retry.Edits.Count == 3 && retry.Configuration.Graph.Nodes[0].GainDb == -6,
                "The retry rebases the retained edit and Undo/Redo operations into their intended final value.");
            mixer.HandleConfigureResponse(retry, Snapshot(baseline), writeFailure, now + TimeSpan.FromSeconds(1));
            require(mixer.PendingEditCount == 3 && mixer.CanUndo && !mixer.IsPendingSaveDue(now + TimeSpan.FromMilliseconds(1999))
                && mixer.IsPendingSaveDue(now + TimeSpan.FromSeconds(2)),
                "Repeated write failures retain history and start another bounded retry interval.");
            mixer.Edit(c => c.Graph.Nodes[0].Name = "Newer queued edit");
            var accepted = retry.Configuration.Copy(); accepted.Revision++;
            mixer.HandleConfigureResponse(retry, Snapshot(baseline), new() { Ok = true, Snapshot = Snapshot(accepted) }, now + TimeSpan.FromSeconds(2));
            require(mixer.PendingEditCount == 1 && mixer.GraphConfig.Nodes[0].GainDb == -6
                && mixer.GraphConfig.Nodes[0].Name == "Newer queued edit" && mixer.EditHistory.Entries.Count == 2 && mixer.CanUndo,
                "A successful retry acknowledges only its captured operations and preserves edits made during that request.");
            require(mixer.Warning.Length == 0 && mixer.SaveStatus == "Saving changes…",
                "A confirmed successful save clears the persistence warning while accurately showing that newer edits are pending.");
            var last = mixer.PreparePendingConfiguration(accepted);
            var saved = last.Configuration.Copy(); saved.Revision++;
            mixer.HandleConfigureResponse(last, Snapshot(accepted), new() { Ok = true, Snapshot = Snapshot(saved) }, now + TimeSpan.FromMilliseconds(2100));
            require(mixer.PendingEditCount == 0 && !mixer.IsPendingSaveDue(now + TimeSpan.FromDays(1))
                && mixer.Warning.Length == 0 && mixer.CanUndo && mixer.GraphConfig.Nodes[0].Name == "Newer queued edit",
                "The final acknowledgement leaves no retry queued, keeps saved values and preserves useful undo history.");
            require(mixer.Undo() && mixer.GraphConfig.Nodes[0].GainDb == -6 && mixer.GraphConfig.Nodes[0].Name != "Newer queued edit",
                "History retained across a persistence failure still undoes the latest acknowledged edit correctly.");
        }
        using (var mixer = Queued())
        {
            mixer.Edit(c => c.Graph.Nodes[0].GainDb = -6);
            var rejected = mixer.PreparePendingConfiguration(baseline);
            mixer.Undo();
            mixer.HandleConfigureResponse(rejected, Snapshot(baseline), permanentFailure, now);
            require(mixer.PendingEditCount == 0 && !mixer.CanUndo && !mixer.CanRedo
                && mixer.GraphConfig.Nodes[0].GainDb == baseline.Graph.Nodes[0].GainDb
                && mixer.Warning.Contains("engine rejected", StringComparison.OrdinalIgnoreCase),
                "An invalid configuration remains a permanent rejection that discards dependent history and restores the saved graph.");
        }
        using (var mixer = Queued())
        {
            mixer.Edit(c => c.Graph.Nodes[0].GainDb = -6);
            var stale = mixer.PreparePendingConfiguration(baseline);
            mixer.HandleConfigureResponse(stale, Snapshot(baseline), new() { Error = "Stale revision." }, now);
            require(mixer.PendingEditCount == 1 && mixer.CanUndo && mixer.SaveStatus == "Reconciling changes…"
                && !mixer.IsPendingSaveDue(now + TimeSpan.FromMilliseconds(99)) && mixer.IsPendingSaveDue(now + TimeSpan.FromMilliseconds(100)),
                "Revision conflicts keep their existing reconciliation status and bounded retry behavior.");
        }
    }
}
