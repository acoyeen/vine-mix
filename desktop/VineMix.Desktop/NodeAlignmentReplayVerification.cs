using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VineMix.Desktop;

// Exercises real queued workspace edits with detached controls and supplied
// snapshots only; no windows, desktop input, personal settings or engine access.
internal static class NodeAlignmentReplayVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null && mixer.PendingEditCount == 0,
            "Alignment replay checks start in a detached preview with no outstanding edits.");
        try
        {
            foreach (NodeAlignment alignment in new[] { NodeAlignment.Left, NodeAlignment.Top })
            {
                var baseline = Fixture("success-" + alignment);
                await Load(baseline);
                string firstId = baseline.Configuration.Graph.Nodes[0].Id;
                string secondId = baseline.Configuration.Graph.Nodes[1].Id;
                Size size = Descendants<GraphNodeView>(workspace).Single(view => view.Id == firstId).RenderSize;
                require(size.Width > 0 && size.Height > 0 && workspace.AlignSelectedNodes(alignment)
                    && mixer.PendingEditCount == 1 && mixer.EditHistory.Entries.Count == 1,
                    alignment + " alignment queues one checked edit and one undo step using measured controls.");
                var aligned = mixer.GraphConfig.Copy();
                var first = aligned.Nodes.Single(node => node.Id == firstId);
                var second = aligned.Nodes.Single(node => node.Id == secondId);
                bool left = alignment == NodeAlignment.Left;
                double gap = left ? second.Y - first.Y - size.Height : second.X - first.X - size.Width;
                require(Math.Abs(gap - 48) < .00001 && (left ? first.X == second.X : first.Y == second.Y),
                    alignment + " replay fixture separates coincident nodes by their real dimension plus 48 DIPs.");

                var external = Snapshot(baseline.Configuration);
                external.Configuration.Graph.Nodes[2].Name = "Independently renamed";
                external.Configuration.Graph.Nodes[2].GainDb = -11;
                string externalGraph = Json(external.Configuration.Graph);
                var expected = external.Configuration.Graph.Copy();
                foreach (var node in expected.Nodes.Where(node => node.Id == firstId || node.Id == secondId))
                {
                    var moved = aligned.Nodes.Single(item => item.Id == node.Id);
                    node.X = moved.X; node.Y = moved.Y;
                }
                var batch = mixer.PreparePendingConfiguration(external.Configuration);
                require(batch.Edits.Count == 1 && mixer.PendingEditCount == 1
                    && Json(batch.Configuration.Graph) == Json(expected)
                    && Json(external.Configuration.Graph) == externalGraph,
                    alignment + " queued replay changes only selected positions and preserves the external graph's audio, camera, routes and unrelated node change.");
                mixer.AcknowledgePending(batch, Snapshot(batch.Configuration)); await layout();
                require(mixer.PendingEditCount == 0 && mixer.CanUndo && mixer.EditHistory.Entries.Count == 1
                    && Json(mixer.GraphConfig) == Json(expected),
                    alignment + " acknowledgement retains exactly one usable alignment undo step.");
                require(mixer.Undo() && !mixer.CanUndo && mixer.PendingEditCount == 1
                    && Json(mixer.GraphConfig) == externalGraph,
                    "One Undo restores all original positions after " + alignment + " while retaining the independent external change.");
                var undo = mixer.PreparePendingConfiguration(batch.Configuration);
                require(Json(undo.Configuration.Graph) == externalGraph,
                    "The queued " + alignment + " Undo also replays to the exact externally updated graph.");
                mixer.AcknowledgePending(undo, Snapshot(undo.Configuration));
            }

            {
                var baseline = Fixture("auto-axis-replay");
                await Load(baseline);
                string[] selected = baseline.Configuration.Graph.Nodes.Take(2).Select(node => node.Id).ToArray();
                Size size = Descendants<GraphNodeView>(workspace).Single(view => view.Id == selected[0]).RenderSize;
                require(GraphNodeAlignment.ResolveAuto(baseline.Configuration.Graph, selected) == NodeAlignment.Top
                    && workspace.AlignSelectedNodes(NodeAlignment.Auto) && mixer.PendingEditCount == 1,
                    "Auto queues one horizontal alignment for the initially coincident selected nodes.");
                var external = Snapshot(baseline.Configuration);
                external.Configuration.Graph.Nodes[0].Y = -600;
                external.Configuration.Graph.Nodes[1].Y = 1400;
                string externalGraph = Json(external.Configuration.Graph);
                require(GraphNodeAlignment.ResolveAuto(external.Configuration.Graph, selected) == NodeAlignment.Left,
                    "The competing selected origins form a tall column that would choose the opposite Auto axis.");
                var batch = mixer.PreparePendingConfiguration(external.Configuration);
                var first = batch.Configuration.Graph.Nodes[0];
                var second = batch.Configuration.Graph.Nodes[1];
                var expected = external.Configuration.Graph.Copy();
                expected.Nodes[1].Y = expected.Nodes[0].Y;
                expected.Nodes[1].X = expected.Nodes[0].X + size.Width + 48;
                require(batch.Edits.Count == 1 && mixer.PendingEditCount == 1
                    && first.Y == -600 && second.Y == first.Y
                    && Math.Abs(second.X - first.X - size.Width - 48) < .00001
                    && Json(batch.Configuration.Graph) == Json(expected)
                    && Json(external.Configuration.Graph) == externalGraph,
                    "Queued Auto retains its originally chosen row, provides 48-DIP horizontal body clearance, changes only positions, and leaves the external snapshot untouched.");
                // Selected positions changed externally, so normal history
                // conflict handling may clear Undo when this result is accepted.
                mixer.AcknowledgePending(batch, Snapshot(batch.Configuration)); await layout();
            }

            foreach (bool changeKind in new[] { true, false })
            {
                var baseline = Fixture(changeKind ? "kind-conflict" : "layout-conflict");
                await Load(baseline);
                require(workspace.AlignSelectedNodes(changeKind ? NodeAlignment.Left : NodeAlignment.Top)
                    && mixer.PendingEditCount == 1 && mixer.Undo() && mixer.PendingEditCount == 2,
                    "A pending alignment and its dependent Undo are queued before the selected layout changes.");
                var external = Snapshot(baseline.Configuration);
                var changed = external.Configuration.Graph.Nodes[0];
                if (changeKind) changed.Kind = "eq";
                else changed.DynamicsUiVersion = 0;
                changed.Name = "Externally changed selected node";
                changed.X = -640; changed.Y = -380;
                external.Configuration.Graph.Nodes[2].GainDb = -9;
                string externalGraph = Json(external.Configuration.Graph);
                mixer.ApplyPreviewSnapshot(external); await layout();
                require(Json(mixer.GraphConfig) == externalGraph && Json(external.Configuration.Graph) == externalGraph
                    && mixer.PendingEditCount == 0 && !mixer.CanUndo && !mixer.CanRedo,
                    "Changing a selected node's " + (changeKind ? "kind" : "presentation version")
                    + " refuses alignment atomically, preserves the exact external graph, and drops its dependent Undo.");
                require(mixer.Warning.Contains("A selected node changed.") && mixer.Warning.Contains("Undo and redo were reset")
                    && workspace.HasFeedback && workspace.FeedbackText.StartsWith("Attention:")
                    && workspace.FeedbackText.Contains("A selected node changed."),
                    "A layout conflict visibly explains why alignment was not saved and replaces its earlier success feedback.");
                var retry = mixer.PreparePendingConfiguration(external.Configuration);
                require(retry.Edits.Count == 0 && Json(retry.Configuration.Graph) == externalGraph,
                    "The refused alignment and dependent history never return on a subsequent save attempt.");
            }
        }
        finally { mixer.QueuePreviewEdits = false; }
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); workspace.DismissMessage(); await layout();

        async Task Load(MixerSnapshot snapshot)
        {
            workspace.CancelActiveGesture();
            mixer.QueuePreviewEdits = false; mixer.ApplyPreviewSnapshot(snapshot); mixer.QueuePreviewEdits = true;
            await layout();
            workspace.SelectNode(snapshot.Configuration.Graph.Nodes[0].Id);
            workspace.SelectNodeForPointer(snapshot.Configuration.Graph.Nodes[1].Id, ModifierKeys.Shift);
            await layout();
        }
    }

    private static MixerSnapshot Fixture(string suffix)
    {
        var snapshot = UiPreview.CreateSnapshot([]);
        var first = GraphDefaults.NewNode("compressor", -300, -120); first.Id = "align-replay-first-" + suffix;
        first.GainDb = -3; first.Compressor.ThresholdDb = -24;
        var second = GraphDefaults.NewNode("gain", -300, -120); second.Id = "align-replay-second-" + suffix;
        second.GainDb = -6; second.Bypass = true;
        var other = GraphDefaults.NewNode("gain", 900, 760); other.Id = "align-replay-other-" + suffix;
        snapshot.Configuration.Graph = new()
        {
            Nodes = [first, second, other],
            Edges = [new() { Id = "align-replay-edge-" + suffix, From = first.Id, To = second.Id, GainDb = -4, Muted = true }],
            PanX = 210, PanY = 80, Zoom = .75
        };
        snapshot.Configuration.Revision = 50; snapshot.Status.Nodes = [];
        return snapshot;
    }

    private static MixerSnapshot Snapshot(MixerConfiguration configuration)
    {
        var snapshot = UiPreview.CreateSnapshot([]); snapshot.Configuration = configuration.Copy();
        snapshot.Configuration.Revision++; snapshot.Status.Nodes = []; return snapshot;
    }

    private static string Json(GraphConfiguration graph) => JsonSerializer.Serialize(graph, Protocol.Json);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
