using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace VineMix.Desktop;

// Exercises the real workspace action in an inert, detached UI.
internal static class DuplicateInputVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<string> capture)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Duplicate input checks use a detached preview without live devices or desktop input.");
        foreach (string kind in new[] { "microphone", "virtualInput", "appInput" })
        {
            var source = GraphDefaults.NewNode(kind, 32, 32); source.Name = "Voice";
            source.Device = new() { Id = "shared-source", StableId = "stable-source", Name = "Studio microphone", Channel = -1 };
            source.App = new() { ProcessId = 1234, ExecutablePath = "C:\\Preview\\Player.exe", Name = "Player" };
            source.GainDb = -6; source.Muted = false;
            var output = GraphDefaults.NewNode("output", 800, 32);
            var fixture = UiPreview.CreateSnapshot([]);
            fixture.Configuration.Graph = new() { Nodes = [source, output], Edges = [new() { Id = "original-route", From = source.Id, To = output.Id }] };
            fixture.Status.Nodes = [];
            mixer.ApplyPreviewSnapshot(fixture); await layout();
            string original = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
            var menu = new ContextMenu(); workspace.PopulateNodeMenu(menu, source.Id, () => { });
            var duplicate = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Duplicate input"));
            require(duplicate.IsEnabled, kind + " offers a discoverable Duplicate input action.");
            duplicate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await layout();
            var copy = mixer.GraphConfig.Nodes.Single(n => n.Id != source.Id && n.Kind == kind);
            require(copy.Name == "Voice copy" && copy.X != source.X && workspace.SelectedNodeIds.SequenceEqual([copy.Id])
                && copy.Device.Id == source.Device.Id && copy.Device.StableId == source.Device.StableId
                && copy.App.ExecutablePath == source.App.ExecutablePath && copy.GainDb == source.GainDb && copy.Muted == source.Muted,
                kind + " duplicate retains its source and controls, gets a distinct name and position, and is selected for placement.");
            require(mixer.GraphConfig.Edges.Count == 1 && mixer.GraphConfig.Edges[0].From == source.Id
                && workspace.FeedbackText.Contains("Connect the new node"),
                kind + " duplicate starts disconnected and explains the next step without changing the original route.");
            require(mixer.Undo() && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == original
                && mixer.Redo() && mixer.GraphConfig.Nodes.Any(n => n.Id == copy.Id && n.Device.Id == source.Device.Id),
                kind + " duplication is one undo step and redo restores the same source binding and identity.");
            workspace.NodeChanged(copy.Id, n => { n.GainDb = -18; n.Muted = true; });
            require(mixer.GraphConfig.Nodes.Single(n => n.Id == source.Id).GainDb == -6
                && !mixer.GraphConfig.Nodes.Single(n => n.Id == source.Id).Muted,
                kind + " duplicate controls do not change the original node.");
            require(workspace.DuplicateInput(source.Id)
                && mixer.GraphConfig.Nodes.Any(n => n.Name == "Voice copy 2" && n.Y != copy.Y),
                kind + " repeated duplication produces a distinguishable name and avoids completely hiding the previous copy.");
            if (kind == "microphone") { workspace.FitAll(); await layout(); capture("duplicate-microphone-inputs.png"); }
            menu = new ContextMenu(); workspace.PopulateNodeMenu(menu, output.Id, () => { });
            require(!menu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "Duplicate input")) && !workspace.DuplicateInput(output.Id),
                "Duplicate input is unavailable for output nodes.");
        }
        foreach (string kind in new[] { "output", "virtualOutput" })
        {
            var originalOutput = GraphDefaults.NewNode(kind, 400, 32);
            originalOutput.Name = "Shared destination";
            originalOutput.Device = new() { Id = "example-render", StableId = "example-render-stable", Name = "Example destination" };
            originalOutput.GainDb = -6; originalOutput.Muted = false;
            var input = GraphDefaults.NewNode("microphone", 32, 32);
            var fixture = UiPreview.CreateSnapshot([]);
            fixture.Configuration.Graph = new() { Nodes = [input, originalOutput],
                Edges = [new() { Id = "existing-output-route", From = input.Id, To = originalOutput.Id }] };
            fixture.Status.Nodes = [];
            mixer.ApplyPreviewSnapshot(fixture); await layout();
            string original = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
            var menu = new ContextMenu(); workspace.PopulateNodeMenu(menu, originalOutput.Id, () => { });
            var duplicate = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Duplicate output"));
            require(duplicate.IsEnabled, kind + " exposes Duplicate output in its context menu.");
            duplicate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await layout();
            var copy = mixer.GraphConfig.Nodes.Single(n => n.Kind == kind && n.Id != originalOutput.Id);
            require(copy.Name == "Shared destination copy" && copy.Device.Id == originalOutput.Device.Id
                && copy.Device.StableId == originalOutput.Device.StableId && copy.GainDb == -6 && copy.Muted
                && !ReferenceEquals(copy.Device, mixer.GraphConfig.Nodes.Single(n => n.Id == originalOutput.Id).Device)
                && mixer.GraphConfig.Edges.Count == 1 && workspace.SelectedNodeIds.SequenceEqual([copy.Id]),
                kind + " duplicate retains an independent destination/Level binding, starts muted and disconnected, and is selected.");
            require(mixer.Undo() && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == original
                && mixer.Redo() && mixer.GraphConfig.Nodes.Single(n => n.Id == copy.Id).Device.Id == originalOutput.Device.Id,
                kind + " duplicate undo/redo restores the same identity and shared binding without a history conflict.");
            workspace.NodeChanged(copy.Id, n => { n.Muted = false; n.GainDb = -12; });
            require(mixer.GraphConfig.Nodes.Single(n => n.Id == originalOutput.Id) is { GainDb: -6, Muted: false },
                kind + " duplicate Level/mute edits leave the first output unchanged.");
            require(workspace.DuplicateOutput(originalOutput.Id)
                && mixer.GraphConfig.Nodes.Any(n => n.Name == "Shared destination copy 2" && n.Muted),
                kind + " repeated duplication remains independently named and muted.");
            if (kind == "output") { workspace.FitAll(); await layout(); capture("duplicate-output-nodes.png"); }
        }
        foreach (string name in new[] { new string('é', 128), string.Concat(Enumerable.Repeat("🎙", 64)) })
        {
            var unicode = UiPreview.CreateSnapshot([]); var input = GraphDefaults.NewNode("microphone", 0, 0); input.Name = name;
            unicode.Configuration.Graph = new() { Nodes = [input] }; unicode.Status.Nodes = [];
            mixer.ApplyPreviewSnapshot(unicode); await layout();
            require(workspace.DuplicateInput(input.Id), "A valid maximum-byte Unicode input name can be duplicated.");
            var copy = mixer.GraphConfig.Nodes.Single(n => n.Id != input.Id);
            require(copy.Name.EndsWith(" copy") && new UTF8Encoding(false, true).GetByteCount(copy.Name) <= 256
                && mixer.GraphConfig.Nodes.Single(n => n.Id == input.Id).Name == name,
                "Duplicate naming preserves complete Unicode characters within the byte limit without renaming the original.");
        }
        var crowded = UiPreview.CreateSnapshot([]); crowded.Configuration.Graph = new(); crowded.Status.Nodes = [];
        for (int i = 0; i < 32; i++) crowded.Configuration.Graph.Nodes.Add(GraphDefaults.NewNode("microphone", i * 8, 0));
        mixer.ApplyPreviewSnapshot(crowded); await layout();
        string fullBefore = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        require(!workspace.DuplicateInput(mixer.GraphConfig.Nodes[0].Id)
            && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == fullBefore
            && workspace.FeedbackText.Contains("32 nodes"),
            "Duplicating at the workspace limit reports the limit and leaves the graph intact.");
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); workspace.DismissMessage(); await layout();
    }
}
