using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached overview, gesture and JSON preference checks; no app settings or audio access.</summary>
internal static class MinimapVerification
{
    internal static void Run(Action<bool, string> require, Action<FrameworkElement, string>? render = null)
    {
        VerifyPresentation(require, render);
        var nodeBounds = new[] { new MinimapNode(new Rect(-1600, -900, 320, 280), false), new MinimapNode(new Rect(2800, 1800, 320, 280), true) };
        foreach (Rect viewport in new[] { new Rect(0, 0, 1200, 700), new Rect(-8000, 4000, 12000, 7000), new Rect(12000, -10000, 600, 350) })
        {
            var projection = MinimapProjection.Create(nodeBounds, viewport, new Size(220, 144));
            require(projection.WorldBounds.Contains(viewport) && nodeBounds.All(node => projection.WorldBounds.Contains(node.Bounds)),
                "The minimap includes every node and the complete viewport even far outside the graph.");
            Point point = new(viewport.X + viewport.Width * .37, viewport.Y + viewport.Height * .61);
            require((projection.ToWorld(projection.ToMap(point)) - point).Length < .000001,
                "Minimap conversion round-trips world coordinates at negative positions and wide camera extents.");
            Rect visible = projection.ToMap(viewport);
            require(visible.Left >= 9.9 && visible.Right <= 210.1 && visible.Top >= 9.9 && visible.Bottom <= 134.1,
                "The visible-canvas outline remains within the padded minimap drawing area.");
        }

        using var mixer = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
        require(mixer.ShowMinimap, "The canvas minimap is visible by default.");
        var snapshot = UiPreview.CreateSnapshot([]);
        var nodes = Enumerable.Range(0, 32).Select(index =>
        {
            var node = GraphDefaults.NewNode("gain", (index % 8) * 520 - 500, (index / 8) * 450 - 200);
            node.Id = "overview-example-" + index; return node;
        }).ToList();
        var edges = (from sourceIndex in Enumerable.Range(0, 32)
                     from offset in Enumerable.Range(1, 4)
                     where sourceIndex + offset < 32 && (offset < 4 || sourceIndex < 6)
                     select new GraphEdge { Id = $"overview-edge-{sourceIndex}-{offset}", From = nodes[sourceIndex].Id, To = nodes[sourceIndex + offset].Id, Kind = "audio" }).ToList();
        snapshot.Configuration.Graph = new() { Nodes = nodes, Edges = edges, PanX = 30, PanY = 25, Zoom = .5 };
        mixer.ApplyPreviewSnapshot(snapshot);
        var workspace = new GraphWorkspace(mixer, useSystemClipboard: false) { Width = 1200, Height = 740 };
        workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        void Layout()
        {
            workspace.Measure(new Size(workspace.Width, workspace.Height));
            workspace.Arrange(new Rect(0, 0, workspace.Width, workspace.Height)); workspace.UpdateLayout();
        }
        try
        {
            Layout();
            var map = workspace.Minimap;
            string audio = JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json);
            int history = mixer.EditHistory.Entries.Count, pending = mixer.PendingEditCount;
            long presentations = mixer.ConfigurationPresentationCount;
            string saveStatus = mixer.SaveStatus;
            int writes = 0;
            mixer.PreviewUiPreferencesWriter = _ => writes++;
            require(map.Visibility == Visibility.Visible && map.Nodes.Count == 32 && edges.Count == 96
                && map.Nodes.All(node => node.Bounds.Width > 1 && node.Bounds.Height > 1),
                "A 32-node, 96-edge graph produces correctly sized overview silhouettes without creating more graph controls.");
            require(map.HorizontalAlignment == HorizontalAlignment.Right && map.VerticalAlignment == VerticalAlignment.Bottom
                && AutomationProperties.GetName(map) == "Canvas minimap" && !map.Focusable,
                "The overview stays in the bottom-right corner and has accessible context without adding a redundant Tab stop.");
            var settings = MainWindow.CreateSettingsMenu(mixer);
            var toggle = settings.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Show minimap"));
            var binding = toggle.GetBindingExpression(MenuItem.IsCheckedProperty)!;
            require(toggle.IsCheckable && toggle.StaysOpenOnClick && binding.ParentBinding.Mode == BindingMode.TwoWay,
                "Settings exposes Show minimap as an ordinary saved checkable preference.");
            toggle.SetCurrentValue(MenuItem.IsCheckedProperty, false); binding.UpdateSource();
            require(!mixer.ShowMinimap && map.Visibility == Visibility.Collapsed && writes == 1,
                "Disabling Show minimap hides it immediately and saves only interface preferences.");
            using (var restored = new MixerViewModel(preview: true))
            {
                restored.LoadUiPreferencesJson(mixer.SerializeUiPreferences());
                require(!restored.ShowMinimap, "The hidden minimap preference survives the real JSON persistence format.");
                restored.LoadUiPreferencesJson("{\"rightClickDragToPan\":true}");
                require(restored.ShowMinimap && restored.RightClickDragToPan, "Older interface preferences default to a visible minimap while preserving other settings.");
            }
            mixer.ShowMinimap = true; Layout();
            require(writes == 2, "Re-enabling the minimap writes one interface preference update.");

            Point OriginalPan() => new(mixer.GraphConfig.PanX, mixer.GraphConfig.PanY);
            Point original = OriginalPan();
            Point target = map.Projection.ToMap(new Point(3300, 1300));
            var frozen = map.Projection;
            require(map.BeginPan(target) && workspace.HasActiveGesture, "Clicking outside the viewport outline begins a camera-only minimap gesture.");
            require(OriginalPan() == original && map.Projection == frozen,
                "A minimap preview leaves the saved camera unchanged and freezes the mapping while dragging.");
            Rect firstPreview = map.VisibleWorld;
            mixer.RefreshPreviewTelemetry(); Layout();
            require(map.VisibleWorld == firstPreview && map.Projection == frozen,
                "Telemetry refresh does not snap a minimap drag back to the previous camera.");
            map.MovePan(target + new Vector(18, -9));
            require(map.Projection == frozen && map.VisibleWorld != firstPreview,
                "Dragging pans using the initial projection even when the view moves outside the graph.");
            map.EndPan();
            require(OriginalPan() != original && !workspace.HasActiveGesture,
                "Release keeps the final camera in session state and ends the minimap gesture.");
            require(writes == 2 && mixer.ConfigurationPresentationCount == presentations && mixer.PendingEditCount == pending
                && mixer.EditHistory.Entries.Count == history && mixer.SaveStatus == saveStatus,
                "Minimap clicks and drags neither write settings immediately nor queue configuration, change save status or create Undo history.");

            Point settled = OriginalPan();
            Rect settledView = map.VisibleWorld;
            Point grab = map.Projection.ToMap(new Point(settledView.X + settledView.Width * .3, settledView.Y + settledView.Height * .4));
            require(map.BeginPan(grab) && map.VisibleWorld == settledView,
                "Grabbing inside the viewport outline preserves its pointer offset without an initial jump.");
            map.MovePan(grab + new Vector(24, 18)); workspace.CancelActiveGesture();
            require(OriginalPan() == settled && map.VisibleWorld == settledView && !map.IsDragging,
                "Cancelling a minimap drag restores the previous camera without a configuration edit.");
            var dial = DescendantDial(workspace)!;
            var entry = DescendantTextBox(dial)!;
            dial.BeginTextEntry(); entry.Text = "-";
            map.BeginPan(grab); map.MovePan(grab + new Vector(12, 10));
            double gestureZoom = mixer.GraphConfig.Zoom;
            Rect gestureView = map.VisibleWorld;
            require(new[] { Key.Home, Key.D0, Key.Delete, Key.Tab, Key.D1 }.All(workspace.TryMinimapGestureKey)
                && mixer.GraphConfig.Zoom == gestureZoom && map.VisibleWorld == gestureView && entry.Text == "-" && dial.HasPendingEntry,
                "An active minimap gesture consumes canvas and editor keys without changing zoom, graph or the retained numeric draft.");
            require(workspace.TryMinimapGestureKey(Key.Escape) && !map.IsDragging && OriginalPan() == settled
                && map.VisibleWorld == settledView && entry.Text == "-" && dial.HasPendingEntry,
                "Escape cancels the map before editor handling and preserves an incomplete numeric draft.");
            require(!workspace.TryMinimapGestureKey(Key.Home), "Ordinary keyboard routing resumes after the minimap gesture ends.");
            dial.CancelPendingEntry();
            Point clickWorld = map.Projection.ToWorld(grab);
            map.BeginPan(grab); map.EndPan();
            Point clickedCenter = new(map.VisibleWorld.X + map.VisibleWorld.Width / 2, map.VisibleWorld.Y + map.VisibleWorld.Height / 2);
            require((clickedCenter - clickWorld).Length <= 2,
                "A click inside the viewport outline still centres that world point; dragging alone retains the grab offset.");
            settled = OriginalPan(); settledView = map.VisibleWorld;
            grab = map.Projection.ToMap(new Point(settledView.X + settledView.Width * .3, settledView.Y + settledView.Height * .4));
            map.BeginPan(grab); map.MovePan(grab + new Vector(-30, 12)); mixer.ShowMinimap = false;
            require(!map.IsDragging && OriginalPan() == settled,
                "Hiding the overview during a drag cancels the gesture safely.");
            mixer.ShowMinimap = true; Layout();
            Point beforeBoundedPan = OriginalPan();
            map.BeginPan(new Point(map.ActualWidth / 2, map.ActualHeight / 2));
            map.MovePan(new Point(1_000_000_000, -1_000_000_000)); map.EndPan();
            require(Math.Abs(mixer.GraphConfig.PanX) <= 100000 && Math.Abs(mixer.GraphConfig.PanY) <= 100000
                && double.IsFinite(map.VisibleWorld.X) && double.IsFinite(map.VisibleWorld.Y),
                "A captured minimap drag far outside the control stays within the native camera bounds.");
            require(mixer.PendingEditCount == pending && mixer.EditHistory.Entries.Count == history
                && mixer.ConfigurationPresentationCount == presentations,
                "Clamping an extreme minimap drag remains camera-only and cannot queue an audio save or history entry.");
            mixer.SetCanvasView(beforeBoundedPan.X, beforeBoundedPan.Y, mixer.GraphConfig.Zoom);
            mixer.RefreshPreviewTelemetry(); Layout();
            workspace.Width = 600; workspace.Height = 400; Layout();
            require(map.Width < 220 && map.Height < 144 && map.Width >= 96 && map.Height >= 72,
                "The minimap adapts to a smaller viewport while retaining a usable drawing area.");
            require(JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json) == audio
                && mixer.PendingEditCount == pending && mixer.EditHistory.Entries.Count == history,
                "All overview interactions preserve every node, cable, pending audio edit and history entry.");
            render?.Invoke(workspace, "canvas-minimap.png");
        }
        finally { workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    private static void VerifyPresentation(Action<bool, string> require, Action<FrameworkElement, string>? render)
    {
        ConsoleTheme.Initialize();
        bool? previous = ConsoleTheme.PreviewHighContrast;
        try
        {
            foreach (bool contrast in new[] { false, true })
            {
                ConsoleTheme.SetPreviewHighContrast(contrast);
                var map = new GraphMinimap { Width = 220, Height = 144 };
                var nodes = new[] { new MinimapNode(new Rect(40, 100, 200, 280), false),
                    new MinimapNode(new Rect(760, 620, 200, 280), true) };
                map.SetScene(nodes, new Rect(0, 0, 1000, 1000));
                map.Measure(new Size(220, 144)); map.Arrange(new Rect(0, 0, 220, 144)); map.UpdateLayout();
                var bitmap = new RenderTargetBitmap(220, 144, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(map);
                Color At(int x, int y)
                {
                    byte[] bytes = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
                    return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
                }
                Color Center(MinimapNode node)
                {
                    Rect rect = map.Projection.ToMap(node.Bounds);
                    return At((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
                }
                Color background = ((SolidColorBrush)ConsoleTheme.Get("ConsoleWindowBrush")).Color;
                Color muted = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMutedBrush")).Color;
                Color selected = ((SolidColorBrush)ConsoleTheme.Get("NodeSelectedBrush")).Color;
                require(Enumerable.Range(12, 24).All(x => Enumerable.Range(8, 11).All(y => At(x, y) == background)),
                    "The minimap has no visible caption in its former header area, including in high contrast.");
                Rect world = map.Projection.ToMap(map.Projection.WorldBounds);
                require(Math.Abs(world.Top - 10) < .001 && Math.Abs(world.Bottom - 134) < .001,
                    "A tall minimap scene uses symmetric ten-DIP vertical padding with no reserved header space.");
                Color normal = Center(nodes[0]);
                Color overview = ((SolidColorBrush)ConsoleTheme.Get("CanvasOverviewBrush")).Color;
                require(normal == overview && (contrast ? normal == muted : normal.R < muted.R && normal.G < muted.G && normal.B < muted.B
                    && normal.R > background.R && normal.G > background.G && normal.B > background.B),
                    "Unselected minimap nodes use the shared dark overview colour without background blending; high contrast retains the system text colour.");
                require(Center(nodes[1]) == selected && AutomationProperties.GetName(map) == "Canvas minimap"
                    && AutomationProperties.GetHelpText(map).Contains("Click to centre"),
                    "Selected minimap nodes and the accessible navigation name/help remain intact without the visible title.");
                render?.Invoke(map, contrast ? "minimap-graphic-high-contrast.png" : "minimap-graphic.png");
            }
        }
        finally { ConsoleTheme.SetPreviewHighContrast(previous); }
    }

    private static RotaryValueControl? DescendantDial(DependencyObject root)
    {
        if (root is RotaryValueControl dial) return dial;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (DescendantDial(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    private static TextBox? DescendantTextBox(DependencyObject root)
    {
        if (root is TextBox entry) return entry;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (DescendantTextBox(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
}
