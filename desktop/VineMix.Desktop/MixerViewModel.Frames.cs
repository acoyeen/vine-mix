using System;
using System.Collections.Generic;
using System.Linq;

namespace VineMix.Desktop;

public sealed partial class MixerViewModel
{
    // A detached UI metadata layer: engine snapshots/configuration remain
    // unaware of frames and can never turn them into routing or DSP objects.
    private List<GraphFrame> _visualFrames = [];
    private bool _visualFramesNeedSave;

    private void SaveVisualFrames()
    {
        _visualFrames = _desired.Graph.Frames.Select(frame => frame.Copy()).ToList();
        SaveUiPreferences();
        _visualFramesNeedSave = !_preview && (HasSettingsRecovery || _uiError.Length > 0);
        if (_pending.Count == 0)
            SaveStatus = _preview ? "Preview · changes stay in this window"
                : _uiError.Length > 0 ? "Group changes are not saved"
                : _editError.Length > 0 ? "Change was not saved"
                : _snapshot is not null ? $"Saved · revision {_snapshot.Configuration.Revision}" : "Saved";
    }

    // Node deletion/paste saves its local membership change before the engine
    // acknowledges the node edit. If that edit is rejected, restore only fields
    // that still match its result; later explicit group edits keep precedence.
    private bool RestoreRejectedVisualFrames(IReadOnlyList<PendingEdit> rejected)
    {
        var beforeRollback = _visualFrames.Select(frame => frame.Copy()).ToList();
        foreach (var edit in rejected.Reverse())
        {
            if (edit.FramesBefore is not { } before || edit.FramesAfter is not { } after) continue;
            foreach (string id in before.Select(frame => frame.Id).Union(after.Select(frame => frame.Id)))
            {
                var original = before.FirstOrDefault(frame => frame.Id == id);
                var applied = after.FirstOrDefault(frame => frame.Id == id);
                if (WorkspaceHistory.SameValue(original, applied)) continue;
                var current = _visualFrames.FirstOrDefault(frame => frame.Id == id);
                if (current is null)
                {
                    if (applied is null && original is not null) _visualFrames.Add(original.Copy());
                    continue;
                }
                if (original is null)
                {
                    if (WorkspaceHistory.SameValue(current, applied)) _visualFrames.Remove(current);
                    continue;
                }
                if (applied is null) continue;
                if (current.Name == applied.Name) current.Name = original.Name;
                if (current.ColorKey == applied.ColorKey) current.ColorKey = original.ColorKey;
                if (current.NodeIds.SequenceEqual(applied.NodeIds, StringComparer.Ordinal))
                    current.NodeIds = [.. original.NodeIds];
            }
        }
        return !GraphFrames.Same(beforeRollback, _visualFrames);
    }
}
