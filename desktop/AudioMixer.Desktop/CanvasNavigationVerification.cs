using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AudioMixer.Desktop;

/// <summary>Detached gesture checks: no native menus, pointer injection, settings writes or engine access.</summary>
internal static class CanvasNavigationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview, "Canvas gesture verification uses only the detached preview model.");
        await VerifySettingsAsync(mixer, layout, require);
        bool originalPreference = mixer.RightClickDragToPan;
        var fixture = UiPreview.CreateSnapshot([]);
        var input = GraphDefaults.NewNode("microphone", 32, 40); input.Id = "pan-input";
        var output = GraphDefaults.NewNode("output", 1240, 40); output.Id = "pan-output";
        fixture.Configuration.Graph = new()
        {
            Nodes = [input, output],
            Edges = [new() { Id = "pan-cable", From = input.Id, To = output.Id, Kind = "audio" }],
            PanX = 32, PanY = 24, Zoom = 1
        };
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        var world = Descendants<Canvas>(workspace).Single(canvas => canvas.RenderTransform is MatrixTransform transform
            && !transform.Matrix.IsIdentity);
        var blank = new Canvas();
        Point start = new(700, 400);
        const double panX = 32, panY = 24;
        long edits = mixer.ConfigurationPresentationCount;
        int history = mixer.EditHistory.Entries.Count;
        string saveStatus = mixer.SaveStatus;
        string audio = JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges });
        Matrix View() => ((MatrixTransform)world.RenderTransform).Matrix;
        bool At(double x, double y) => Math.Abs(View().OffsetX - x) < .01 && Math.Abs(View().OffsetY - y) < .01;
        async Task Reset(double zoom = 1)
        {
            workspace.CancelActiveGesture();
            mixer.SetCanvasView(panX, panY, zoom);
            mixer.RefreshPreviewTelemetry(); await layout();
        }
        void Begin(Point point)
        {
            require(workspace.TryBeginRightCanvasGesture(point, blank, ModifierKeys.None),
                "Enabled right-click panning arms a gesture on empty canvas.");
        }

        try
        {
            mixer.RightClickDragToPan = false;
            require(!workspace.TryBeginRightCanvasGesture(start, blank, ModifierKeys.None) && !workspace.HasCanvasPanGesture,
                "Disabling right-click panning leaves ordinary canvas right-click handling available.");
            mixer.RightClickDragToPan = true;
            await Reset();
            var node = Descendants<GraphNodeView>(workspace).First();
            var cable = Descendants<Path>(workspace).First(path => path.IsHitTestVisible);
            foreach (DependencyObject source in new DependencyObject[] { node, cable, new TextBox(), new ComboBox(), new Button() })
                require(!workspace.TryBeginRightCanvasGesture(start, source, ModifierKeys.None),
                    $"Right-click panning leaves {source.GetType().Name} interaction to its existing handler.");
            require(!workspace.TryBeginRightCanvasGesture(start, blank, ModifierKeys.Control) && !workspace.HasCanvasPanGesture,
                "Ctrl+right remains reserved for cable cutting instead of arming a canvas pan.");

            foreach (double zoom in new[] { .3, 1, 2 })
            {
                await Reset(zoom);
                Begin(start);
                workspace.UpdateCanvasPan(start);
                require(workspace.HasCanvasPanGesture && !workspace.IsCanvasPanning && At(panX, panY),
                    $"Holding right-click without movement remains a click at {zoom:P0} zoom.");
                workspace.UpdateCanvasPan(start + new Vector(3, 2));
                require(!workspace.IsCanvasPanning && At(panX, panY),
                    $"Small pointer jitter does not move the canvas at {zoom:P0} zoom.");
                bool consumed = workspace.FinishCanvasPan(MouseButton.Right, start + new Vector(4, 2), out Point? menu);
                Point expectedMenu = new((start.X - panX) / zoom, (start.Y - panY) / zoom);
                require(consumed && !workspace.HasCanvasPanGesture && menu is Point location && (location - expectedMenu).Length < .001
                    && At(panX, panY),
                    $"A subthreshold right-click release requests the menu at its original world position at {zoom:P0} zoom.");

                Begin(start);
                workspace.UpdateCanvasPan(start + new Vector(7, 0));
                require(workspace.IsCanvasPanning && At(panX + 7, panY),
                    $"Crossing the six-DIP threshold starts a screen-space pan at {zoom:P0} zoom.");
                workspace.UpdateCanvasPan(start + new Vector(40, -20));
                require(At(panX + 40, panY - 20) && mixer.GraphConfig.PanX == panX && mixer.GraphConfig.PanY == panY,
                    "Pan preview follows the pointer without committing the camera before release.");
                consumed = workspace.FinishCanvasPan(MouseButton.Left, start + new Vector(40, -20), out menu);
                require(consumed && menu is null && workspace.IsCanvasPanning,
                    "An unrelated left-button release cannot finish an active right-button pan.");
                consumed = workspace.FinishCanvasPan(MouseButton.Right, start + new Vector(44, -22), out menu);
                require(consumed && menu is null && !workspace.HasCanvasPanGesture && At(panX + 44, panY - 22)
                    && mixer.GraphConfig.PanX == panX + 44 && mixer.GraphConfig.PanY == panY - 22 && mixer.GraphConfig.Zoom == zoom,
                    $"Right-button release applies its final movement, keeps zoom and suppresses the menu at {zoom:P0} zoom.");
                mixer.RefreshPreviewTelemetry(); await layout();
                require(At(panX + 44, panY - 22), "A completed right-button pan survives telemetry refresh.");

                await Reset(zoom); Begin(start);
                consumed = workspace.FinishCanvasPan(MouseButton.Right, start + new Vector(20, 10), out menu);
                require(consumed && menu is null && At(panX + 20, panY + 10),
                    "Movement first observed on release still pans instead of opening a context menu.");

                await Reset(zoom); Begin(start);
                workspace.UpdateCanvasPan(start + new Vector(20, 10)); workspace.UpdateCanvasPan(start);
                workspace.FinishCanvasPan(MouseButton.Right, start, out menu);
                require(menu is null && At(panX, panY) && !workspace.HasCanvasPanGesture,
                    "Returning a pan to its starting point does not turn it back into a context-menu click.");
            }

            foreach (bool moved in new[] { false, true })
            {
                foreach (bool captureLoss in new[] { false, true })
                {
                    await Reset(); Begin(start);
                    if (moved) workspace.UpdateCanvasPan(start + new Vector(40, -20));
                    if (captureLoss) workspace.HandleCaptureLoss(); else workspace.CancelActiveGesture();
                    require(!workspace.HasCanvasPanGesture && !workspace.IsCanvasPanning && At(panX, panY)
                        && mixer.GraphConfig.PanX == panX && mixer.GraphConfig.PanY == panY,
                        $"{(captureLoss ? "Capture loss" : "Escape")} clears a {(moved ? "moving" : "pending")} right gesture and restores the original view.");
                    require(!workspace.FinishCanvasPan(MouseButton.Right, start, out Point? menu) && menu is null,
                        "A release after cancellation cannot open a stale menu or resume the cancelled pan.");
                }
            }

            await Reset(); Begin(new Point(1, 1));
            workspace.FinishCanvasPan(MouseButton.Right, new Point(-1, 1), out Point? outsideMenu);
            require(outsideMenu is null && !workspace.HasCanvasPanGesture && At(panX, panY),
                "A subthreshold release outside the viewport does not open the canvas menu.");

            foreach (bool enabled in new[] { false, true })
            {
                mixer.RightClickDragToPan = enabled;
                foreach (MouseButton button in new[] { MouseButton.Middle, MouseButton.Left })
                {
                    await Reset(); workspace.BeginCanvasPan(button, start);
                    workspace.UpdateCanvasPan(start + new Vector(2, 1));
                    require(workspace.IsCanvasPanning && At(panX + 2, panY + 1),
                        $"{(button == MouseButton.Middle ? "Middle-button" : "Space and left-button")} pan still starts immediately with the right-pan option {(enabled ? "enabled" : "disabled")}.");
                    require(workspace.FinishCanvasPan(MouseButton.Right, start, out Point? menu) && menu is null && workspace.HasCanvasPanGesture,
                        "An unrelated right-button release cannot end an existing middle or Space pan or open a menu.");
                    workspace.FinishCanvasPan(button, start + new Vector(12, 8), out menu);
                    require(menu is null && !workspace.HasCanvasPanGesture && At(panX + 12, panY + 8),
                        "The original pan button completes its gesture with the final pointer position.");
                }
            }

            await Reset(); mixer.RightClickDragToPan = true;
            var socket = Descendants<GraphNodeView>(workspace).First(view => view.Id == input.Id).FindSocket("audio", true)!;
            workspace.BeginConnectionPreview(socket, start, dragging: true);
            require(!workspace.TryBeginRightCanvasGesture(start, blank, ModifierKeys.None) && workspace.HasConnectionPreview,
                "An active cable drag cannot be replaced by right-click panning.");
            workspace.CancelActiveGesture();
            workspace.BeginBoxSelection(start, false);
            workspace.UpdateBoxSelection(start + new Vector(20, 10));
            require(!workspace.TryBeginRightCanvasGesture(start, blank, ModifierKeys.None) && workspace.SelectionBoxVisible,
                "An active box selection cannot be replaced by right-click panning.");
            workspace.CancelActiveGesture();
            workspace.BeginCableCut(start);
            require(!workspace.TryBeginRightCanvasGesture(start, blank, ModifierKeys.None) && workspace.HasCableCutPreview,
                "An active cable-cut gesture retains ownership while right-click panning is enabled.");
            workspace.CancelActiveGesture();

            require(mixer.ConfigurationPresentationCount == edits && mixer.EditHistory.Entries.Count == history && mixer.SaveStatus == saveStatus
                && JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }) == audio,
                "Navigation, cancellation and the right-pan preference leave audio routes, settings dispatch, save status and edit history unchanged.");
        }
        finally
        {
            workspace.CancelActiveGesture();
            mixer.RightClickDragToPan = originalPreference;
        }
    }

    internal static async Task VerifySettingsAsync(MixerViewModel mixer, Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview && mixer.CanEdit, "Settings binding verification uses an editable preview model without settings writes or Windows registration.");
        var menu = MainWindow.CreateSettingsMenu(mixer);
        foreach (var preference in new (string Property, Func<bool> Read, Action<bool> Write)[]
        {
            (nameof(MixerViewModel.RightClickDragToPan), () => mixer.RightClickDragToPan, value => mixer.RightClickDragToPan = value),
            (nameof(MixerViewModel.ReducedAnimation), () => mixer.ReducedAnimation, value => mixer.ReducedAnimation = value),
            (nameof(MixerViewModel.StartAtLogin), () => mixer.StartAtLogin, value => mixer.StartAtLogin = value)
        })
        {
            bool original = preference.Read();
            try
            {
                var toggle = menu.Items.OfType<MenuItem>().Single(item =>
                    item.GetBindingExpression(MenuItem.IsCheckedProperty)?.ParentBinding.Path.Path == preference.Property);
                var binding = toggle.GetBindingExpression(MenuItem.IsCheckedProperty)!;
                binding.UpdateTarget();
                require(toggle.IsChecked == original && toggle.IsCheckable && toggle.StaysOpenOnClick
                    && binding.ParentBinding.Mode == BindingMode.TwoWay,
                    $"Settings displays {preference.Property} as a current, editable toggle that keeps the menu open.");
                toggle.SetCurrentValue(MenuItem.IsCheckedProperty, !original);
                binding.UpdateSource();
                require(preference.Read() == !original,
                    $"The Settings {preference.Property} toggle updates its actual preference through the two-way binding.");
                preference.Write(original); await layout();
                require(toggle.IsChecked == original,
                    $"Changing {preference.Property} in the model updates its Settings toggle.");
            }
            finally { preference.Write(original); }
        }
        require(!menu.IsOpen && PresentationSource.FromVisual(menu) is null,
            "Settings binding checks never open a native popup or take desktop focus.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
