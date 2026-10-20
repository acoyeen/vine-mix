using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AudioMixer.Desktop;

internal static class DynamicsFeedbackVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout, Action<bool, string> require,
        Action<GraphNodeView, string> renderNode)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new() { Nodes = new[] { "deEsser", "plosive", "compressor", "expander", "gate", "denoiser", "reverb" }
            .Select((kind, index) => GraphDefaults.NewNode(kind, index * 400, 0)).ToList() };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus { Id = node.Id, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        string initial = System.Text.Json.JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        int history = mixer.EditHistory.Entries.Count;
        foreach (var node in mixer.GraphConfig.Nodes)
        {
            var view = Descendants<GraphNodeView>(workspace).Single(item => item.Id == node.Id);
            var before = view.RenderSize;
            var status = new GraphNodeStatus { Id = node.Id, DynamicsFrame = 123, GainReductionDb = 7.5f, DetectorDb = -12, Peak = .001f, Path = new() { State = "Running" } };
            view.ApplyDynamicsFeedbackAt(node, status, 1, true);
            if (node.Kind is "denoiser" or "reverb")
            {
                require(view.VisibleReductionDb == 0 && view.DynamicsFeedbackText == "", node.Kind + " has no invented scalar gain-reduction meter.");
                continue;
            }
            require(view.VisibleReductionDb == 7.5 && view.DynamicsFeedbackText.Contains("7.5"), node.Kind + " displays actual reported dynamics reduction independently of output peak.");
            object? helpContent = view.DynamicsHelpContent;
            var renderStatus = mixer.GraphStatus.Single(item => item.Id == node.Id);
            renderStatus.DynamicsFrame = 124; renderStatus.GainReductionDb = 7.5f; renderStatus.DetectorDb = -12;
            mixer.RefreshPreviewTelemetry();
            await layout(); renderNode(view, node.Kind + "-reduction-feedback.png");
            status.DynamicsFrame = 125; view.ApplyDynamicsFeedbackAt(node, status, 1, true);
            status.Peak = .9f; view.ApplyDynamicsFeedbackAt(node, status, 1.1, true);
            require(view.VisibleReductionDb == 7.5, node.Kind + " output volume does not invent or change measured reduction.");
            status.GainReductionDb = 0; status.DetectorDb = -96; view.ApplyDynamicsFeedbackAt(node, status, 1.15, true);
            require(view.VisibleReductionDb == 7.5, node.Kind + " duplicate snapshots cannot erase freshly consumed dynamics measurements.");
            view.ApplyDynamicsFeedbackAt(node, status, 1.3, true);
            require(view.VisibleReductionDb == 0 && view.DynamicsFeedbackText == "Waiting for audio", node.Kind + " unchanged dynamics frames expire after 250 ms.");
            status.DynamicsFrame = 1; status.GainReductionDb = 3; view.ApplyDynamicsFeedbackAt(node, status, 2, true);
            require(view.VisibleReductionDb == 3, node.Kind + " accepts fresh frames after an engine counter restarts.");
            node.Bypass = true; view.ApplyDynamicsFeedbackAt(node, status, 2.1, true);
            require(view.VisibleReductionDb == 0 && view.DynamicsFeedbackText == "Bypassed", node.Kind + " bypass immediately clears reduction feedback.");
            node.Bypass = false; node.Muted = true; view.ApplyDynamicsFeedbackAt(node, status, 2.1, true);
            require(view.VisibleReductionDb == 0 && view.DynamicsFeedbackText == "Muted", node.Kind + " legacy mute clears reduction feedback.");
            node.Muted = false; status.Path.State = "Waiting"; view.ApplyDynamicsFeedbackAt(node, status, 2.1, true);
            require(view.VisibleReductionDb == 0, node.Kind + " unavailable audio clears reduction feedback.");
            status.Path.State = "Running"; status.DynamicsFrame++; status.GainReductionDb = float.NaN; view.ApplyDynamicsFeedbackAt(node, status, 3, true);
            require(view.VisibleReductionDb == 0, node.Kind + " rejects nonfinite telemetry.");
            status.DynamicsFrame++; status.GainReductionDb = 0; status.DetectorDb = -70; view.ApplyDynamicsFeedbackAt(node, status, 4, true);
            require(view.DynamicsFeedbackText == (node.Kind is "expander" or "gate" ? "Open · Reduction 0.0 dB" : "Below threshold"),
                node.Kind + " explains its measured zero-reduction state without inventing attenuation from signal level.");
            require(helpContent is not null && ReferenceEquals(helpContent, view.DynamicsHelpContent),
                node.Kind + " reuses its structured hover help while updating live detector and threshold values in place.");
            status.DynamicsFrame++; status.GainReductionDb = 8; view.ApplyDynamicsFeedbackAt(node, status, 5, false);
            require(view.VisibleReductionDb == 0, node.Kind + " engine-unavailable presentation clears reduction.");
            await layout();
            require(view.RenderSize == before, node.Kind + " feedback, bypass and stale states never resize the node.");
        }
        require(System.Text.Json.JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == initial && mixer.EditHistory.Entries.Count == history,
            "Dynamics telemetry is presentation-only and never edits or saves graph settings.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) yield return typed;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
