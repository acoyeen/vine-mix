using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached command routing with explicit keys; no desktop input, HWND, engine or audio device.</summary>
internal static class KeyboardNavigationVerification
{
    internal static async Task<int> RunDetachedAsync(string reportPath)
    {
        reportPath = Path.GetFullPath(reportPath);
        if (File.Exists(reportPath)) throw new IOException("Choose a new keyboard report path.");
        var checks = new List<string>(); string? failure = null;
        using var mixer = new MixerViewModel(preview: true);
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([]));
        var workspace = new GraphWorkspace(mixer, useSystemClipboard: false);
        var root = new Border { Width = 1280, Height = 800, DataContext = mixer, Child = workspace };
        async Task Layout()
        {
            root.Measure(new Size(1280, 800)); root.Arrange(new Rect(0, 0, 1280, 800)); root.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Background);
            root.UpdateLayout();
        }
        try
        {
            workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            await Layout();
            await RunAsync(workspace, mixer, Layout, (passed, description) =>
            { if (!passed) throw new InvalidOperationException(description); checks.Add(description); });
        }
        catch (Exception error) { failure = error.ToString(); }
        finally { workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = failure is null, error = failure, checks }, new JsonSerializerOptions(Protocol.Json) { WriteIndented = true }));
        return failure is null ? 0 : 1;
    }

    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null, "Keyboard checks run only in a detached preview workspace");
        var original = UiPreview.CreateSnapshot([]); original.Configuration.Graph = mixer.GraphConfig.Copy();
        bool wasLocked = mixer.IsLayoutLocked;
        MixerSnapshot Fixture()
        {
            var snapshot = UiPreview.CreateSnapshot([]);
            GraphNode Node(string id, string kind, double x, double y)
            { var node = GraphDefaults.NewNode(kind, x, y); node.Id = id; node.Name = "Example " + id; node.AutoAssignDevice = false; return node; }
            snapshot.Configuration.Graph = new()
            {
                Nodes = [Node("input", "virtualInput", 0, 0), Node("gain", "gain", 500, 0), Node("compressor", "ducking", 1000, 600),
                    Node("mix", "mix", 1500, 0), Node("output", "output", 2100, 0), Node("control", "control", 0, 600)],
                Edges = [new() { Id = "input-gain", From = "input", To = "gain" },
                    new() { Id = "gain-compressor", From = "gain", To = "compressor", Muted = true },
                    new() { Id = "gain-mix", From = "gain", To = "mix" },
                    new() { Id = "compressor-mix", From = "compressor", To = "mix" },
                    new() { Id = "mix-output", From = "mix", To = "output" },
                    new() { Id = "control-gain", From = "control", To = "gain", Kind = "control" },
                    new() { Id = "input-trigger", From = "input", To = "compressor", Kind = "sidechain" }],
                PanX = 24, PanY = 24, Zoom = 1
            };
            return snapshot;
        }
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(v => v.Id == id);
        GraphNode Node(string id) => mixer.GraphConfig.Nodes.Single(n => n.Id == id);
        bool Selected(string id) => workspace.SelectedNodeIds.SequenceEqual(new[] { id });
        bool Follow(bool forward, DependencyObject? focused = null) => workspace.TryPipelineShortcut(Key.Tab,
            forward ? ModifierKeys.Control : ModifierKeys.Control | ModifierKeys.Shift, focused ?? workspace);
        string Sound()
        {
            var graph = mixer.GraphConfig.Copy(); foreach (var node in graph.Nodes) { node.X = 0; node.Y = 0; }
            return JsonSerializer.Serialize(new { graph.Nodes, graph.Edges }, Protocol.Json);
        }
        async Task Reset()
        {
            workspace.CancelActiveGesture(); mixer.IsLayoutLocked = false;
            mixer.ApplyPreviewSnapshot(Fixture()); workspace.SelectNode("missing"); await layout();
        }
        try
        {
            require(GraphDefaults.Validate(Fixture().Configuration.Graph).Length == 0, "Keyboard navigation fixture obeys the production graph contract");
            await Reset();
            require(!workspace.TryPipelineShortcut(Key.Tab, ModifierKeys.None, workspace)
                && !workspace.TryPipelineShortcut(Key.Tab, ModifierKeys.Shift, workspace),
                "Tab and Shift+Tab retain ordinary control traversal");
            workspace.SelectNode("input");
            require(Follow(true) && Selected("gain"), "Pipeline navigation follows audio and ignores control/sidechain routes");
            require(Follow(true) && workspace.PipelineChoices.Count == 2 && workspace.PipelineChoices.Any(c => c.Label.Contains("muted cable")),
                "A branch offers named actual connections, including a muted audio cable");
            int branch = workspace.PipelineChoices.ToList().FindIndex(c => c.NodeId == "compressor");
            require(workspace.ChoosePipelineOption(branch) && Selected("compressor"), "Choosing a branch selects the requested node");
            require(Follow(true) && Selected("mix") && Follow(false) && Selected("compressor"),
                "Reverse navigation follows the actual previous edge at a merge");
            require(Follow(false) && Selected("gain") && Follow(false) && Selected("input"),
                "Reverse traversal retraces multiple audio connections without following a controller");
            require(Follow(false) && Selected("input") && workspace.FeedbackText.Contains("Start", StringComparison.Ordinal),
                "At a pipeline start, navigation stays put and gives feedback");
            workspace.SelectNode("output");
            require(Follow(true) && Selected("output") && workspace.FeedbackText.Contains("End", StringComparison.Ordinal),
                "At a pipeline endpoint, navigation stays put and gives feedback");

            workspace.SelectNode("gain"); workspace.SelectNodeForPointer("compressor", ModifierKeys.Shift);
            require(Follow(true) && workspace.PipelineChoices.Count == 2 && workspace.PipelineChoices.All(c => c.EdgeId is null),
                "An ambiguous starting selection offers named starting nodes");
            require(workspace.ChoosePipelineOption(0) && workspace.SelectedNodeIds.Count == 1,
                "Choosing a starting node makes the pipeline focus unambiguous");
            workspace.SelectNode("missing");
            require(Follow(true) && workspace.PipelineChoices.Count == 6, "No selection offers the graph's named starting choices");
            workspace.CancelActiveGesture();

            workspace.SelectNode("gain"); Follow(true);
            int staleChoice = workspace.PipelineChoices.ToList().FindIndex(c => c.NodeId == "compressor");
            mixer.Edit(c => c.Graph.Edges.Single(e => e.Id == "gain-compressor").To = "output");
            require(!workspace.ChoosePipelineOption(staleChoice) && Selected("gain") && workspace.FeedbackText.Contains("connection changed", StringComparison.Ordinal),
                "A branch retargeted while its menu is open cannot silently navigate to a different destination");

            await Reset();
            mixer.Edit(c =>
            {
                c.Graph.Nodes.Single(n => n.Id == "gain").Kind = "reverb";
                c.Graph.Edges.Single(e => e.Id == "gain-compressor").FromPort = "wet";
            });
            workspace.SelectNode("gain"); Follow(true);
            require(workspace.PipelineChoices.Any(c => c.Label.Contains("Wet", StringComparison.Ordinal)),
                "Alternate audio outputs retain their named ports in the pipeline chooser");
            int changedPort = workspace.PipelineChoices.ToList().FindIndex(c => c.NodeId == "compressor");
            mixer.Edit(c => c.Graph.Edges.Single(e => e.Id == "gain-compressor").FromPort = "audio");
            require(!workspace.ChoosePipelineOption(changedPort) && Selected("gain"),
                "A changed source port invalidates the labelled branch choice even when both endpoints are unchanged");

            await Reset(); workspace.SelectNode("input");
            mixer.SetCanvasView(-9000, -9000, .1); mixer.RefreshPreviewTelemetry(); await layout();
            require(Follow(true) && Selected("gain") && mixer.GraphConfig.Zoom >= .6 && View("gain").DetailLevel == NodeDetailLevel.Full,
                "Following an off-screen node first reveals readable controls from a distant low-zoom view");
            var dial = Descendants<RotaryValueControl>(View("gain")).First();
            var entry = Descendants<TextBox>(dial).First(); entry.Text = "-";
            string beforeInvalid = Sound();
            require(Follow(true, entry) && Selected("gain") && entry.Text == "-" && dial.HasPendingEntry && Sound() == beforeInvalid,
                "An incomplete numeric draft blocks pipeline movement and remains visible without a sound edit");
            entry.Text = "-6.1";
            require(Follow(true, entry) && Node("gain").GainDb == -6.1 && !dial.HasPendingEntry && workspace.PipelineChoices.Count == 2,
                "A valid numeric draft is committed before choosing the next audio route");
            workspace.CancelActiveGesture();

            await Reset(); workspace.SelectNode("gain"); string sound = Sound(); int history = mixer.EditHistory.Entries.Count;
            for (int repeat = 0; repeat < 3; ++repeat) require(workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace), "Canvas owns a right-arrow nudge repeat");
            require(Node("gain").X == 500 && Canvas.GetLeft(View("gain")) == 530 && mixer.EditHistory.Entries.Count == history,
                "Holding an arrow previews movement without partial saved edits");
            workspace.ReleaseCanvasArrow(Key.Left);
            require(workspace.KeyboardNudgeActive, "An unrelated key release does not finish the held arrow");
            workspace.ReleaseCanvasArrow(Key.Right);
            require(Node("gain").X == 530 && mixer.EditHistory.Entries.Count == history + 1 && Sound() == sound,
                "Releasing a held arrow makes exactly one layout edit without changing sound or connections");
            require(mixer.Undo() && Node("gain").X == 500 && mixer.Redo() && Node("gain").X == 530,
                "A complete held-arrow movement has one usable Undo and Redo step");
            mixer.SetCanvasView(24, 24, .1); mixer.RefreshPreviewTelemetry(); await layout();
            workspace.TryCanvasArrow(Key.Up, ModifierKeys.Shift, workspace); workspace.ReleaseCanvasArrow(Key.Up);
            require(Node("gain").Y == -1, "Shift+arrow moves one world unit independently of zoom");
            int beforeCancel = mixer.EditHistory.Entries.Count;
            workspace.TryCanvasArrow(Key.Left, ModifierKeys.None, workspace); workspace.CancelActiveGesture();
            require(Node("gain").X == 530 && Canvas.GetLeft(View("gain")) == 530 && mixer.EditHistory.Entries.Count == beforeCancel,
                "Cancelling a nudge restores its preview without adding an edit");
            workspace.TryCanvasArrow(Key.Left, ModifierKeys.None, workspace); workspace.FinishKeyboardNudge();
            require(Node("gain").X == 520, "Focus loss finishes the current keyboard move once");

            workspace.SelectNodeForPointer("compressor", ModifierKeys.Shift);
            workspace.TryCanvasArrow(Key.Down, ModifierKeys.None, workspace); workspace.ReleaseCanvasArrow(Key.Down);
            require(Node("gain").Y == 9 && Node("compressor").Y == 610, "Selected nodes move together by the same world delta");
            double pan = mixer.GraphConfig.PanX; double x = Node("gain").X;
            mixer.IsLayoutLocked = true;
            require(workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace) && Node("gain").X == x && mixer.GraphConfig.PanX == pan,
                "Locked selected nodes do not move and do not silently become canvas panning");
            mixer.IsLayoutLocked = false; workspace.SelectNode("missing");
            history = mixer.EditHistory.Entries.Count;
            require(workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace) && mixer.GraphConfig.PanX == pan - 40
                && mixer.EditHistory.Entries.Count == history, "Arrows with no selection pan by 40 screen units without adding history");
            require(!workspace.TryCanvasArrow(Key.Left, ModifierKeys.None, entry) && !workspace.TryCanvasArrow(Key.Down, ModifierKeys.None, new ComboBox())
                && !workspace.TryCanvasArrow(Key.Up, ModifierKeys.Control, workspace),
                "Focused editors, selectors and modifier chords keep their own arrow behaviour");

            await Reset(); workspace.SelectNode("gain");
            workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace);
            mixer.IsLayoutLocked = true;
            require(!workspace.KeyboardNudgeActive && Node("gain").X == 500 && Canvas.GetLeft(View("gain")) == 500 && !mixer.CanUndo,
                "Locking layout during a held key cancels the uncommitted movement");
            mixer.IsLayoutLocked = false;
            workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace);
            mixer.Edit(c => c.Graph.Nodes.Single(n => n.Id == "gain").X = 505);
            workspace.ReleaseCanvasArrow(Key.Right);
            require(Node("gain").X == 505 && Canvas.GetLeft(View("gain")) == 505 && mixer.EditHistory.Entries.Count == 1,
                "A concurrent position change is preserved instead of being overwritten by a stale nudge preview");
            mixer.Edit(c => c.Graph.Nodes.Single(n => n.Id == "gain").X = 99995);
            workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace); workspace.ReleaseCanvasArrow(Key.Right);
            require(Node("gain").X == 100000, "A keyboard nudge respects the production world-coordinate limit");

            await Reset(); workspace.SelectNode("gain");
            workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace);
            var removed = Fixture(); removed.Configuration.Graph.Nodes.RemoveAll(n => n.Id == "gain");
            removed.Configuration.Graph.Edges.RemoveAll(e => e.From == "gain" || e.To == "gain");
            mixer.ApplyPreviewSnapshot(removed);
            double afterRemovalPan = mixer.GraphConfig.PanX;
            require(!workspace.KeyboardNudgeActive && workspace.SelectedNodeIds.Count == 0 && !mixer.CanUndo,
                "Removing the selected node immediately cancels an uncommitted keyboard move");
            require(workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace, isRepeat: true)
                && workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace, isRepeat: true)
                && mixer.GraphConfig.PanX == afterRemovalPan && !mixer.CanUndo,
                "All remaining repeats of the cancelled held arrow are consumed instead of turning into panning");
            workspace.ReleaseCanvasArrow(Key.Right);
            require(workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace) && mixer.GraphConfig.PanX == afterRemovalPan - 40,
                "A fresh arrow press can pan normally after the cancelled key is released");

            await Reset();
            GraphCanvasView Camera() => new(mixer.GraphConfig.PanX, mixer.GraphConfig.PanY, mixer.GraphConfig.Zoom);
            var zoomIn = Descendants<Button>(workspace).Single(button => Equals(button.Content, "+"));
            var zoomOut = Descendants<Button>(workspace).Single(button => Equals(button.Content, "−"));
            zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var expectedIn = Camera();
            await Reset();
            zoomOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var expectedOut = Camera();
            foreach (var (key, modifiers, expected) in new[]
            {
                (Key.OemPlus, ModifierKeys.Control, expectedIn),
                (Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift, expectedIn),
                (Key.Add, ModifierKeys.Control, expectedIn),
                (Key.OemMinus, ModifierKeys.Control, expectedOut),
                (Key.Subtract, ModifierKeys.Control, expectedOut)
            })
            {
                await Reset();
                require(workspace.TryKeyboardZoom(key, modifiers, workspace) && Camera() == expected,
                    $"{modifiers}+{key} uses the zoom button's exact step and viewport-centre anchor");
            }
            await Reset(); sound = Sound(); history = mixer.EditHistory.Entries.Count;
            var beforeZoom = Camera();
            require(!workspace.TryKeyboardZoom(Key.OemPlus, ModifierKeys.None, workspace)
                && !workspace.TryKeyboardZoom(Key.OemMinus, ModifierKeys.None, workspace)
                && !workspace.TryKeyboardZoom(Key.OemPlus, ModifierKeys.Shift, workspace)
                && !workspace.TryKeyboardZoom(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Alt, workspace)
                && !workspace.TryKeyboardZoom(Key.OemMinus, ModifierKeys.Control | ModifierKeys.Shift, workspace)
                && !workspace.TryKeyboardZoom(Key.A, ModifierKeys.Control, workspace) && Camera() == beforeZoom,
                "Plain characters and unrelated modifier chords do not invoke canvas zoom");
            require(!workspace.TryKeyboardZoom(Key.Add, ModifierKeys.Control, null)
                && !workspace.TryKeyboardZoom(Key.Add, ModifierKeys.Control, new Button()) && Camera() == beforeZoom,
                "Keyboard zoom requires focus inside this workspace");
            dial = Descendants<RotaryValueControl>(View("gain")).First();
            entry = Descendants<TextBox>(dial).First(); entry.Text = "-";
            require(!workspace.TryKeyboardZoom(Key.OemPlus, ModifierKeys.Control, entry)
                && !workspace.TryKeyboardZoom(Key.Subtract, ModifierKeys.Control, dial)
                && Camera() == beforeZoom && entry.Text == "-" && dial.HasPendingEntry && Sound() == sound,
                "Focused value editors and dials retain their shortcuts and incomplete drafts without zoom or sound changes");
            dial.CancelPendingEntry();
            await Reset();
            workspace.SelectNode("gain"); workspace.TryCanvasArrow(Key.Right, ModifierKeys.None, workspace);
            beforeZoom = Camera();
            require(!workspace.TryKeyboardZoom(Key.Add, ModifierKeys.Control, workspace) && Camera() == beforeZoom,
                "Keyboard zoom cannot change the camera during an unfinished canvas gesture");
            workspace.CancelActiveGesture();
            mixer.IsLayoutLocked = true;
            require(workspace.TryKeyboardZoom(Key.OemPlus, ModifierKeys.Control, View("gain")) && Camera() == expectedIn,
                "A nonediting node surface can zoom while layout is locked");
            for (int repeat = 0; repeat < 30; ++repeat) workspace.TryKeyboardZoom(Key.Subtract, ModifierKeys.Control, workspace);
            require(mixer.GraphConfig.Zoom == .1 && workspace.TryKeyboardZoom(Key.Subtract, ModifierKeys.Control, workspace)
                && mixer.GraphConfig.Zoom == .1, "Repeated keyboard zoom-out stops at 10 percent and consumes the shortcut at the limit");
            for (int repeat = 0; repeat < 30; ++repeat) workspace.TryKeyboardZoom(Key.Add, ModifierKeys.Control, workspace);
            require(mixer.GraphConfig.Zoom == 2 && workspace.TryKeyboardZoom(Key.Add, ModifierKeys.Control, workspace)
                && mixer.GraphConfig.Zoom == 2, "Repeated keyboard zoom-in stops at 200 percent and consumes the shortcut at the limit");
            require(Sound() == sound && mixer.EditHistory.Entries.Count == history,
                "Keyboard zoom leaves sound, routes, node positions and graph edit history unchanged");

            await Reset();
            require(!workspace.TryRenameSelectionShortcut(Key.F2, ModifierKeys.None, workspace), "F2 ignores an empty selection");
            workspace.SelectNode("gain"); workspace.SelectNodeForPointer("compressor", ModifierKeys.Shift);
            require(!workspace.TryRenameSelectionShortcut(Key.F2, ModifierKeys.None, workspace), "F2 ignores multiple selected nodes");
            workspace.SelectNode("gain");
            require(workspace.TryRenameSelectionShortcut(Key.F2, ModifierKeys.None, workspace) && View("gain").HasPendingRename,
                "F2 begins renaming exactly one selected node");
            var name = Descendants<TextBox>(View("gain")).Single(t => AutomationProperties.GetName(t) == "Node name");
            name.Text = "Renamed example"; View("gain").TryCommitPendingRename();
            require(Node("gain").Name == "Renamed example" && AutomationProperties.GetName(View("gain")) == "Renamed example node",
                "Renaming updates the visible node and its screen-reader label");
            require(mixer.Undo() && AutomationProperties.GetName(View("gain")) == "Example gain node"
                && mixer.Redo() && AutomationProperties.GetName(View("gain")) == "Renamed example node",
                "Undo and Redo refresh the node's screen-reader label");
            mixer.IsLayoutLocked = true;
            require(workspace.TryRenameSelectionShortcut(Key.F2, ModifierKeys.None, workspace) && View("gain").HasPendingRename,
                "Node naming retains its existing availability while layout is locked");
            View("gain").CancelPracticeRename(); mixer.IsLayoutLocked = false;
            workspace.SelectNodeForPointer("compressor", ModifierKeys.Shift);
            string? frameId = workspace.CreateFrameFromSelection(); await layout();
            require(frameId is not null && workspace.TryRenameSelectionShortcut(Key.F2, ModifierKeys.None, workspace)
                && Descendants<GraphFrameView>(workspace).Any(f => f.IsRenaming),
                "One selected group is a single F2 target even when it contains multiple nodes");
            foreach (var frame in Descendants<GraphFrameView>(workspace)) frame.CancelRename();
            require(!workspace.TryRenameSelectionShortcut(Key.F2, ModifierKeys.None, name), "F2 does not intercept a text editor");
        }
        finally
        {
            workspace.CancelActiveGesture(); mixer.IsLayoutLocked = false; mixer.ApplyPreviewSnapshot(original);
            mixer.IsLayoutLocked = wasLocked; await layout();
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
