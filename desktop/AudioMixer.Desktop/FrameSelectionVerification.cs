using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached camera checks: no native windows, clipboard, input injection or engine access.</summary>
internal static class FrameSelectionVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<string>? render = null)
    {
        require(mixer.IsPreview, "Frame-selection verification uses only an inert preview model.");
        var fixture = UiPreview.CreateSnapshot([]);
        var gain = GraphDefaults.NewNode("gain", -700, -500); gain.Id = "frame-gain";
        var eq = GraphDefaults.NewNode("eq", 0, 0); eq.Id = "frame-eq";
        var output = GraphDefaults.NewNode("output", 9000, 5000); output.Id = "frame-output";
        fixture.Configuration.Graph = new()
        {
            Nodes = [gain, eq, output],
            Edges = [new() { Id = "frame-in", From = gain.Id, To = eq.Id, Kind = "audio" },
                new() { Id = "frame-out", From = eq.Id, To = output.Id, Kind = "audio" }],
            PanX = -240, PanY = 110, Zoom = .5
        };
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        var world = (Canvas)View(gain.Id).Parent;
        var viewport = (FrameworkElement)VisualTreeHelper.GetParent(world);
        Matrix Camera() => ((MatrixTransform)world.RenderTransform).Matrix;
        Rect OnScreen(params string[] ids)
        {
            var result = Rect.Empty;
            foreach (string id in ids)
            {
                var view = View(id);
                result.Union(view.TransformToAncestor(viewport).TransformBounds(new Rect(view.RenderSize)));
            }
            return result;
        }
        void RequireCentered(string description, params string[] ids)
        {
            var bounds = OnScreen(ids);
            require(Math.Abs(bounds.Left + bounds.Width / 2 - viewport.ActualWidth / 2) <= 1
                && Math.Abs(bounds.Top + bounds.Height / 2 - viewport.ActualHeight / 2) <= 1, description);
        }
        void SelectPair()
        {
            workspace.SelectNode(gain.Id);
            workspace.SelectNodeForPointer(eq.Id, ModifierKeys.Shift);
        }
        void RequirePair(string description) =>
            require(workspace.SelectedNodeIds.ToHashSet().SetEquals([gain.Id, eq.Id]), description);
        string Audio() => JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json);
        string originalAudio = Audio(), originalSave = mixer.SaveStatus;
        long originalEdits = mixer.ConfigurationPresentationCount;
        int originalHistory = mixer.EditHistory.Entries.Count;

        try
        {
            workspace.SelectNode(gain.Id);
            require(workspace.TryFrameSelectionShortcut(Key.F, ModifierKeys.None, workspace),
                "Plain F on the focused canvas handles frame selection.");
            require(Camera().M11 == 1 && Camera().M22 == 1,
                "A small selected node is shown at readable 100 percent, without magnifying beyond its normal size.");
            RequireCentered("A selected node at negative world coordinates is centered using its rendered bounds.", gain.Id);
            require(!OnScreen(output.Id).IntersectsWith(new Rect(viewport.RenderSize)),
                "A distant unselected node does not dilute frame selection into Fit All.");

            SelectPair();
            var menu = new ContextMenu();
            var frame = workspace.CreateFrameSelectionMenuItem(); menu.Items.Add(frame);
            require(frame.IsEnabled && Equals(frame.Header, "Frame selection") && frame.InputGestureText == "F",
                "The node-menu action exposes its F shortcut and is enabled for an idle selection.");
            frame.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await layout();
            var pair = OnScreen(gain.Id, eq.Id);
            require(Camera().M11 >= .3 && Camera().M11 <= 1
                && pair.Left >= 31 && pair.Right <= viewport.ActualWidth - 31
                && pair.Top >= 26 && pair.Bottom <= viewport.ActualHeight - 26,
                "Framing a mixed-size group includes the complete rendered controls with the same margins as Fit.");
            RequireCentered("Frame selection centers the union of differently sized nodes, including negative positions.", gain.Id, eq.Id);
            RequirePair("Activating Frame selection preserves the existing multi-selection.");
            render?.Invoke("frame-selected-nodes.png");
            Matrix framed = Camera();
            mixer.RefreshPreviewTelemetry(); await layout();
            require(Camera() == framed, "The framed view survives telemetry refresh without reverting the camera.");
            require(!menu.IsOpen && PresentationSource.FromVisual(menu) is null,
                "Menu-action verification never opens a native popup or takes desktop focus.");

            workspace.FitAll();
            Matrix all = Camera();
            RequireCentered("Fit continues to center every rendered node.", gain.Id, eq.Id, output.Id);
            require(workspace.FrameSelection() && Camera() != all,
                "Fit continues to frame all nodes; Frame selection independently restores the selected group.");
            RequireCentered("Selection framing returns from Fit All to the chosen group.", gain.Id, eq.Id);
            require(!workspace.TryFrameSelectionShortcut(Key.Home, ModifierKeys.None, workspace),
                "The new shortcut handler leaves Home available to the existing Fit All handler.");

            Matrix beforeIgnoredKeys = Camera();
            foreach (ModifierKeys modifiers in new[] { ModifierKeys.Control, ModifierKeys.Shift, ModifierKeys.Alt,
                ModifierKeys.Windows, ModifierKeys.Control | ModifierKeys.Shift })
                require(!workspace.TryFrameSelectionShortcut(Key.F, modifiers, workspace),
                    $"F with {modifiers} does not invoke selection framing.");
            foreach (DependencyObject? focus in new DependencyObject?[] { null, new TextBox(), new RichTextBox(),
                new PasswordBox(), new ComboBox(), new Slider(), new EqualizerEditor(), new Button(), View(gain.Id) })
                require(!workspace.TryFrameSelectionShortcut(Key.F, ModifierKeys.None, focus),
                    $"F remains local to {(focus is null ? "focus outside the canvas" : focus.GetType().Name)}.");
            var editor = new TextBox { Template = new ControlTemplate(typeof(TextBox)) { VisualTree = new FrameworkElementFactory(typeof(Border)) } };
            editor.ApplyTemplate();
            require(!workspace.TryFrameSelectionShortcut(Key.F, ModifierKeys.None, VisualTreeHelper.GetChild(editor, 0))
                && Camera() == beforeIgnoredKeys,
                "A child inside a focused editor and all ignored key combinations leave the camera unchanged.");

            var socket = View(gain.Id).FindSocket("audio", true)!;
            require(!GraphWorkspace.IsFrameSelectionPointerBusy(null)
                && !GraphWorkspace.IsFrameSelectionPointerBusy(new ContextMenu())
                && !GraphWorkspace.IsFrameSelectionPointerBusy(new MenuItem()),
                "Menu capture does not prevent its own Frame selection command from running.");
            foreach (IInputElement captured in new IInputElement[] { new Slider(), new ComboBox(), new EqualizerEditor(),
                new RotaryValueControl("Level", -60, 12, 1, "dB", 0, _ => { }), viewport })
                require(GraphWorkspace.IsFrameSelectionPointerBusy(captured),
                    $"A captured {captured.GetType().Name} retains its pointer gesture instead of allowing framing.");
            Point start = new(100, 100);
            foreach (var gesture in new (string Name, Action Begin, Func<bool> Active)[]
            {
                ("node move", () => workspace.BeginNodeMove(gain.Id, start), () => workspace.IsNodeDragging),
                ("box selection", () => { workspace.BeginBoxSelection(start, true); workspace.UpdateBoxSelection(start + new Vector(40, 30)); }, () => workspace.SelectionBoxVisible),
                ("middle-button pan", () => workspace.BeginCanvasPan(MouseButton.Middle, start), () => workspace.HasCanvasPanGesture),
                ("pending right-button pan", () => workspace.BeginCanvasPan(MouseButton.Right, start), () => workspace.HasCanvasPanGesture),
                ("cable cut", () => workspace.BeginCableCut(start), () => workspace.HasCableCutPreview),
                ("cable drag", () => workspace.BeginConnectionPreview(socket, start, true), () => workspace.HasConnectionPreview),
                ("click-to-connect preview", () => workspace.BeginConnectionPreview(socket, start, false), () => workspace.HasConnectionPreview)
            })
            {
                SelectPair();
                var staleEnabledItem = workspace.CreateFrameSelectionMenuItem();
                gesture.Begin();
                Matrix before = Camera();
                require(!workspace.FrameSelection()
                    && !workspace.TryFrameSelectionShortcut(Key.F, ModifierKeys.None, workspace)
                    && !workspace.CreateFrameSelectionMenuItem().IsEnabled,
                    $"Frame selection and its shortcut/menu remain inactive during a {gesture.Name}.");
                staleEnabledItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                require(Camera() == before && gesture.Active(),
                    $"A previously enabled menu action rechecks the active {gesture.Name} without moving the camera or ending it.");
                workspace.CancelActiveGesture();
            }

            workspace.SelectNode("not-a-node"); workspace.DismissMessage();
            Matrix beforeEmpty = Camera();
            require(workspace.TryFrameSelectionShortcut(Key.F, ModifierKeys.None, workspace)
                && Camera() == beforeEmpty && workspace.HasFeedback
                && workspace.FeedbackText == "Select one or more nodes to frame."
                && !workspace.CreateFrameSelectionMenuItem().IsEnabled,
                "F with no selection explains how to proceed and leaves the current view unchanged.");
            require(AutomationProperties.GetHelpText(workspace).Contains("F frames the selected nodes")
                && AutomationProperties.GetHelpText(workspace).Contains("Home or Fit"),
                "Accessible canvas help distinguishes frame selection from Fit All.");
            require(Audio() == originalAudio && mixer.ConfigurationPresentationCount == originalEdits
                && mixer.EditHistory.Entries.Count == originalHistory && mixer.SaveStatus == originalSave,
                "Selection framing, menu activation, keys and cancelled gestures do not edit nodes, routes, save state or undo history.");

            workspace.SelectNode(gain.Id);
            workspace.SelectNodeForPointer(output.Id, ModifierKeys.Shift);
            require(workspace.FrameSelection() && Camera().M11 == .3 && Camera().M22 == .3,
                "A selection wider than the available canvas respects the 30 percent minimum zoom.");
            RequireCentered("Even an oversized selection remains centered at the bounded minimum zoom.", gain.Id, output.Id);

            // Simulate a selection that outlives a configuration change before
            // the next render refresh. Obsolete views must not affect framing.
            SelectPair();
            mixer.GraphConfig.Nodes.RemoveAll(node => node.Id == eq.Id);
            mixer.GraphConfig.Edges.Clear();
            require(workspace.FrameSelection() && Camera().M11 == 1,
                "A disappeared selected node is ignored even while its old rendered view still exists.");
            RequireCentered("Only a surviving selected node determines the framed center.", gain.Id);
            mixer.GraphConfig.Nodes.RemoveAll(node => node.Id == gain.Id);
            Matrix beforeMissing = Camera();
            require(!workspace.FrameSelection() && Camera() == beforeMissing
                && workspace.FeedbackText == "Select one or more nodes to frame.",
                "If every selected node disappears, framing leaves the camera unchanged and reports the empty selection.");

            mixer.ApplyPreviewSnapshot(fixture); await layout();
            workspace.SelectNode(gain.Id);
            mixer.BeginEditGesture();
            try
            {
                workspace.NodeChanged(gain.Id, node => node.GainDb = -6);
                Matrix beforeEdit = Camera();
                require(!workspace.FrameSelection() && Camera() == beforeEdit && !workspace.CreateFrameSelectionMenuItem().IsEnabled,
                    "An active control-edit gesture retains its view and cannot be interrupted by framing.");
            }
            finally { mixer.EndEditGesture(); }
            long editsAfterGesture = mixer.ConfigurationPresentationCount;
            int historyAfterGesture = mixer.EditHistory.Entries.Count;
            require(workspace.FrameSelection() && mixer.ConfigurationPresentationCount == editsAfterGesture
                && mixer.EditHistory.Entries.Count == historyAfterGesture,
                "Framing becomes available after a control gesture ends and adds no edit or undo step.");
        }
        finally { workspace.CancelActiveGesture(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
