using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached alignment-key routing checks. No native focus, physical keys, windows, clipboard or engine access.</summary>
internal static class NodeAlignmentShortcutVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<string>? render = null)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Alignment shortcut checks use a detached preview and explicit key/modifier values.");
        string[] ids = ["shortcut-input", "shortcut-eq", "shortcut-control"];
        const string outputId = "shortcut-output";
        MixerSnapshot Fixture(bool tall)
        {
            var snapshot = UiPreview.CreateSnapshot([]);
            var input = GraphDefaults.NewNode("microphone", tall ? -240 : -500, tall ? -300 : -160);
            input.Id = ids[0]; input.GainDb = -4;
            input.Device = new() { Id = "preview-mic1", Name = "Studio microphone" };
            var eq = GraphDefaults.NewNode("eq", tall ? -160 : 0, tall ? 200 : -80);
            eq.EqBands = EqBand.Defaults(); // Configured EQ geometry fixture.
            eq.Id = ids[1]; eq.EqBands[1].GainDb = 3;
            var control = GraphDefaults.NewNode("control", tall ? -80 : 600, tall ? 700 : 0);
            control.Id = ids[2]; control.GainDb = -2;
            var output = GraphDefaults.NewNode("output", -1100, -900); output.Id = outputId;
            output.Device = new() { Id = "preview-a1", Name = "Monitor headphones" };
            snapshot.Configuration.Graph = new()
            {
                Nodes = [input, eq, control, output],
                Edges = [new() { Id = "shortcut-in", From = input.Id, To = eq.Id, GainDb = -3 },
                    new() { Id = "shortcut-out", From = eq.Id, To = output.Id, Muted = true },
                    new() { Id = "shortcut-control-link", From = control.Id, To = eq.Id, Kind = "control" }],
                PanX = 420, PanY = 180, Zoom = .5
            };
            return snapshot;
        }
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
        string Graph() => Json(mixer.GraphConfig);
        string Sound()
        {
            var graph = mixer.GraphConfig.Copy();
            foreach (var node in graph.Nodes) { node.X = 0; node.Y = 0; }
            return Json(new { graph.Nodes, graph.Edges });
        }
        void SelectGroup()
        {
            workspace.SelectNode(ids[0]);
            workspace.SelectNodeForPointer(ids[1], ModifierKeys.Shift);
            workspace.SelectNodeForPointer(ids[2], ModifierKeys.Shift);
        }
        async Task Reset(bool tall)
        {
            workspace.CancelActiveGesture(); mixer.ApplyPreviewSnapshot(Fixture(tall));
            await layout(); SelectGroup(); await layout(); workspace.FrameSelection();
        }
        MenuItem Child(MenuItem menu, string name) => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, name));
        Matrix Camera() => ((MatrixTransform)((Canvas)View(ids[0]).Parent).RenderTransform).Matrix;
        void RequireSpaced(NodeAlignment alignment)
        {
            // The input's audio cable and the linked control both feed EQ.
            // Stable source order therefore places input, control, then EQ.
            string[] orderedIds = [ids[0], ids[2], ids[1]];
            var bounds = orderedIds.Select(id =>
            {
                var view = View(id);
                return view.TransformToAncestor((Visual)view.Parent).TransformBounds(new Rect(view.RenderSize));
            }).ToArray();
            bool column = alignment == NodeAlignment.Left;
            require(bounds.All(rect => Math.Abs((column ? rect.Left : rect.Top) - (column ? bounds[0].Left : bounds[0].Top)) < .00001),
                "The shortcut creates a common " + (column ? "left edge for a vertical column" : "top edge for a horizontal row") + ".");
            for (int i = 1; i < bounds.Length; i++)
                require(Math.Abs((column ? bounds[i].Top - bounds[i - 1].Bottom : bounds[i].Left - bounds[i - 1].Right) - 48) < .00001,
                    "Shortcut alignment leaves exactly 48 DIPs between differently sized rendered nodes " + orderedIds[i - 1] + " and " + orderedIds[i] + ".");
        }
        async Task VerifyAction(Key key, bool tall, NodeAlignment expected, bool useMenu = false)
        {
            MenuItem? staleAuto = null;
            if (useMenu)
            {
                // Build the action for a wide selection, then change the current
                // geometry before invoking it. Auto must not capture the old axis.
                await Reset(tall: false);
                staleAuto = Child(workspace.CreateNodeAlignmentMenuItem()!, "Auto Align");
            }
            await Reset(tall);
            string before = Graph(), sound = Sound(); Matrix camera = Camera();
            var output = mixer.GraphConfig.Nodes.Single(node => node.Id == outputId).Copy();
            long presentations = mixer.ConfigurationPresentationCount;
            int history = mixer.EditHistory.Entries.Count;
            if (useMenu) staleAuto!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            else require(workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace),
                "Alt+" + key + " is handled when the canvas owns an eligible selection.");
            await layout();
            string after = Graph();
            require(mixer.ConfigurationPresentationCount == presentations + 1 && mixer.EditHistory.Entries.Count == history + 1
                && !mixer.EditHistory.HasGestureEdit && after != before,
                (useMenu ? "The Auto Align menu action" : "Alt+" + key) + " performs one completed group edit and one undo step.");
            RequireSpaced(expected);
            require(Sound() == sound && Camera() == camera && workspace.SelectedNodeIds.ToHashSet().SetEquals(ids)
                && Json(mixer.GraphConfig.Nodes.Single(node => node.Id == outputId)) == Json(output),
                "Alignment shortcuts preserve audio, routes, bindings, the unselected output, selection and camera.");
            presentations = mixer.ConfigurationPresentationCount;
            require(workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace) && Graph() == after
                && mixer.ConfigurationPresentationCount == presentations && mixer.EditHistory.Entries.Count == history + 1,
                "An already aligned eligible Alt+" + key + " is consumed without position creep or another edit/history entry.");
            require(mixer.Undo() && Graph() == before && !mixer.CanUndo,
                "One Undo restores all original positions after " + (useMenu ? "Auto Align" : "Alt+" + key) + ".");
            await layout();
            require(mixer.Redo() && Graph() == after && !mixer.CanRedo,
                "One Redo restores the exact shortcut-aligned group.");
            await layout(); RequireSpaced(expected);
            require(Camera() == camera && Sound() == sound && workspace.SelectedNodeIds.ToHashSet().SetEquals(ids),
                "Undo and Redo retain the canvas view, selection and complete audio configuration.");
        }
        async Task VerifySignalChain(Key key)
        {
            var snapshot = UiPreview.CreateSnapshot([]);
            var source = GraphDefaults.NewNode("microphone", 800, 650);
            source.Id = "chain-source"; source.Name = "Voice input";
            source.Device = new() { Id = "preview-mic1", Name = "Studio microphone" }; source.GainDb = -3;
            var noise = GraphDefaults.NewNode("denoiser", -500, 200);
            noise.Id = "chain-noise"; noise.Name = "Denoiser"; noise.Denoiser.RnnoiseMix = .85;
            var gate = GraphDefaults.NewNode("expander", 250, -300);
            gate.Id = "chain-gate"; gate.Name = "Gate"; gate.Expander.Mode = "gate"; gate.Expander.ThresholdDb = -35;
            var bus = GraphDefaults.NewNode("mix", -150, 900);
            bus.Id = "chain-bus"; bus.Name = "Voice mix"; bus.GainDb = -4;
            var sink = GraphDefaults.NewNode("output", 550, 20);
            sink.Id = "chain-sink"; sink.Name = "Listening output";
            sink.Device = new() { Id = "preview-a1", Name = "Monitor headphones" };
            snapshot.Configuration.Graph = new()
            {
                // Neither graph order nor X/Y order describes the signal chain.
                Nodes = [bus, source, sink, gate, noise],
                Edges = [new() { Id = "chain-gate-bus", From = gate.Id, To = bus.Id },
                    new() { Id = "chain-source-noise", From = source.Id, To = noise.Id, GainDb = -2 },
                    new() { Id = "chain-bus-sink", From = bus.Id, To = sink.Id },
                    new() { Id = "chain-noise-gate", From = noise.Id, To = gate.Id, Muted = true }],
                PanX = 150, PanY = 100, Zoom = .5
            };
            mixer.ApplyPreviewSnapshot(snapshot); await layout();
            string[] chainIds = snapshot.Configuration.Graph.Nodes.Select(node => node.Id).ToArray();
            workspace.SelectNode(chainIds[0]);
            foreach (string id in chainIds.Skip(1)) workspace.SelectNodeForPointer(id, ModifierKeys.Shift);
            await layout(); workspace.FrameSelection();
            var world = (Canvas)View(source.Id).Parent;
            Matrix camera = ((MatrixTransform)world.RenderTransform).Matrix;
            string before = Graph(), sound = Sound();
            long presentations = mixer.ConfigurationPresentationCount;
            int history = mixer.EditHistory.Entries.Count;
            bool row = key == Key.X;
            require(workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace),
                "Alt+" + key + " accepts a selected chain whose graph list and visible positions are scrambled.");
            await layout();
            string after = Graph();
            void RequireCableOrder()
            {
                var bounds = chainIds.ToDictionary(id => id, id =>
                {
                    var view = View(id);
                    return view.TransformToAncestor(world).TransformBounds(new Rect(view.RenderSize));
                });
                Rect first = bounds[source.Id];
                require(bounds.Values.All(rect => Math.Abs((row ? rect.Top : rect.Left) - (row ? first.Top : first.Left)) < .00001),
                    "The recovered signal chain shares one " + (row ? "top edge" : "left edge") + ".");
                // The explicit audio cables, including the muted one, are the
                // ordering oracle. No node-kind ranking is used by this check.
                foreach (var edge in mixer.GraphConfig.Edges.Where(edge => edge.Kind == "audio"))
                {
                    Rect from = bounds[edge.From], to = bounds[edge.To];
                    double gap = row ? to.Left - from.Right : to.Top - from.Bottom;
                    require(Math.Abs(gap - 48) < .00001,
                        "Alt+" + key + " puts audio source " + edge.From + " before destination " + edge.To
                        + " with 48 DIPs of clearance, including muted cables (actual gap " + gap + ").");
                }
                require(Math.Abs(first.Left + 500) < .00001 && Math.Abs(first.Top + 300) < .00001,
                    "A reordered signal chain starts at the original selection's minimum X/Y instead of keeping scrambled empty gaps.");
            }
            RequireCableOrder();
            require(after != before && mixer.ConfigurationPresentationCount == presentations + 1
                && mixer.EditHistory.Entries.Count == history + 1 && !mixer.EditHistory.HasGestureEdit
                && Sound() == sound && ((MatrixTransform)world.RenderTransform).Matrix == camera
                && workspace.SelectedNodeIds.ToHashSet().SetEquals(chainIds),
                "Signal-chain alignment is one position-only edit that preserves every cable, setting, binding, selected ID and camera value.");
            presentations = mixer.ConfigurationPresentationCount;
            require(workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace) && Graph() == after
                && mixer.ConfigurationPresentationCount == presentations && mixer.EditHistory.Entries.Count == history + 1,
                "Repeating the recovered chain alignment is consumed without moving nodes or adding history.");
            require(mixer.Undo() && Graph() == before && !mixer.CanUndo,
                "One Undo restores the exact scrambled positions and unchanged signal graph.");
            await layout();
            require(mixer.Redo() && Graph() == after && !mixer.CanRedo,
                "One Redo restores the complete ordered chain.");
            await layout(); RequireCableOrder();
            require(Sound() == sound && ((MatrixTransform)world.RenderTransform).Matrix == camera
                && workspace.SelectedNodeIds.ToHashSet().SetEquals(chainIds),
                "Signal-chain Undo/Redo retain routing, audio, selection and camera.");
            // Explicit inspection framing occurs after testing the operation's unchanged camera.
            workspace.FrameSelection(); await layout();
            var viewport = (FrameworkElement)VisualTreeHelper.GetParent(world);
            require(chainIds.All(id =>
            {
                var view = View(id);
                var bounds = view.TransformToAncestor(viewport).TransformBounds(new Rect(view.RenderSize));
                return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= viewport.ActualWidth + 1
                    && bounds.Bottom <= viewport.ActualHeight + 1;
            }), "The " + (row ? "row" : "column") + " regression image includes all five connected nodes after explicit framing.");
            render?.Invoke(row ? "alignment-signal-chain-row.png" : "alignment-signal-chain-column.png");
        }
        try
        {
            await Reset(tall: false);
            var menu = workspace.CreateNodeAlignmentMenuItem()!;
            require(Child(menu, "Left edges").InputGestureText == "Alt+Y"
                && Child(menu, "Top edges").InputGestureText == "Alt+X"
                && Child(menu, "Auto Align").InputGestureText == "Alt+A"
                && Child(menu, "Auto Align").IsEnabled,
                "The alignment submenu advertises Alt+Y for a column, Alt+X for a row and Alt+A for Auto Align.");
            // Explicit axes deliberately oppose each fixture's predominant spread.
            await VerifyAction(Key.X, tall: true, NodeAlignment.Top);
            await VerifyAction(Key.Y, tall: false, NodeAlignment.Left);
            await VerifyAction(Key.A, tall: false, NodeAlignment.Top);
            await VerifyAction(Key.A, tall: true, NodeAlignment.Left);
            await VerifyAction(Key.A, tall: true, NodeAlignment.Left, useMenu: true);
            await VerifySignalChain(Key.X);
            await VerifySignalChain(Key.Y);

            await Reset(tall: false);
            string original = Graph(); long count = mixer.ConfigurationPresentationCount;
            var dial = Descendants<RotaryValueControl>(View(ids[0])).First();
            var entry = Descendants<TextBox>(dial).Single();
            entry.Text = "-12.75";
            var eq = Descendants<EqualizerEditor>(View(ids[1])).Single();
            foreach (Key key in new[] { Key.X, Key.Y, Key.A })
            {
                foreach (ModifierKeys modifiers in new[] { ModifierKeys.None, ModifierKeys.Control, ModifierKeys.Shift,
                    ModifierKeys.Alt | ModifierKeys.Shift, ModifierKeys.Alt | ModifierKeys.Control, ModifierKeys.Alt | ModifierKeys.Windows })
                    require(!workspace.TryNodeAlignmentShortcut(key, modifiers, workspace),
                        "Modified alignment keys require exactly Alt: " + key + " with " + modifiers + ".");
                foreach (DependencyObject? focus in new DependencyObject?[] { null, entry, new RichTextBox(), new PasswordBox(),
                    dial, eq, new Slider(), new ComboBox(), new Button(), View(ids[0]) })
                    require(!workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, focus),
                        "Alt+" + key + " stays local when focus belongs to " + (focus?.GetType().Name ?? "another surface") + ".");
            }
            require(!workspace.TryNodeAlignmentShortcut(Key.F, ModifierKeys.Alt, workspace)
                && !workspace.TryNodeAlignmentShortcut(Key.System, ModifierKeys.Alt, workspace)
                && entry.Text == "-12.75" && Graph() == original && mixer.ConfigurationPresentationCount == count
                && mixer.EditHistory.Entries.Count == 0,
                "Unrecognized or unnormalized keys and non-canvas focus preserve the real numeric draft, graph and history.");
            workspace.SelectNode(ids[0]);
            foreach (Key key in new[] { Key.X, Key.Y, Key.A })
                require(!workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace),
                    "Alt+" + key + " remains unhandled when fewer than two current nodes are selected.");
            SelectGroup();
            workspace.BeginCanvasPan(MouseButton.Middle, new Point(100, 100));
            foreach (Key key in new[] { Key.X, Key.Y, Key.A })
                require(!workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace) && workspace.HasCanvasPanGesture,
                    "Alt+" + key + " neither interrupts nor edits during an active canvas pan.");
            workspace.CancelActiveGesture();
            require(Graph() == original && mixer.ConfigurationPresentationCount == count && mixer.EditHistory.Entries.Count == 0,
                "Selection and gesture guards introduce no audio, position or history changes.");

            mixer.BeginEditGesture();
            try
            {
                workspace.NodeChanged(ids[0], node => node.GainDb = -7);
                string during = Graph(); long duringCount = mixer.ConfigurationPresentationCount;
                foreach (Key key in new[] { Key.X, Key.Y, Key.A })
                    require(!workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace)
                        && mixer.EditHistory.HasGestureEdit && Graph() == during && mixer.ConfigurationPresentationCount == duringCount,
                        "Alt+" + key + " leaves an in-progress control edit open and unchanged.");
            }
            finally { mixer.EndEditGesture(); }
            require(mixer.EditHistory.Entries.Count == 1,
                "Only the original control gesture enters history after rejected alignment shortcuts.");
            await Reset(tall: false); original = Graph(); count = mixer.ConfigurationPresentationCount;
            workspace.IsEnabled = false;
            try
            {
                foreach (Key key in new[] { Key.X, Key.Y, Key.A })
                    require(!workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace),
                        "A disabled modal workspace cannot consume Alt+" + key + " as a hidden edit.");
            }
            finally { workspace.IsEnabled = true; }
            require(Graph() == original && mixer.ConfigurationPresentationCount == count && mixer.EditHistory.Entries.Count == 0,
                "Disabled-workspace shortcuts preserve positions and history.");
            mixer.GraphConfig.Nodes.RemoveAll(node => node.Id == ids[1] || node.Id == ids[2]);
            mixer.GraphConfig.Edges.Clear(); original = Graph();
            foreach (Key key in new[] { Key.X, Key.Y, Key.A })
                require(!workspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, workspace) && Graph() == original,
                    "Stale selected IDs cannot make Alt+" + key + " consume or move one surviving node.");

            using var offline = new MixerViewModel(preview: true);
            offline.GraphConfig.Nodes.AddRange(Fixture(false).Configuration.Graph.Nodes.Select(node => node.Copy()));
            var offlineWorkspace = new GraphWorkspace(offline);
            offlineWorkspace.SelectNode(ids[0]); offlineWorkspace.SelectNodeForPointer(ids[1], ModifierKeys.Shift);
            string offlineBefore = Json(offline.GraphConfig);
            foreach (Key key in new[] { Key.X, Key.Y, Key.A })
                require(!offlineWorkspace.TryNodeAlignmentShortcut(key, ModifierKeys.Alt, offlineWorkspace),
                    "Offline Alt+" + key + " remains unhandled and never requests an engine connection.");
            require(Json(offline.GraphConfig) == offlineBefore && offline.PendingEditCount == 0 && offline.EditHistory.Entries.Count == 0
                && offline.ConfigurationPresentationCount == 0 && PresentationSource.FromVisual(offlineWorkspace) is null,
                "Offline checks preserve the graph and never create native UI, pending audio work or history.");
        }
        finally { workspace.IsEnabled = true; workspace.CancelActiveGesture(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
