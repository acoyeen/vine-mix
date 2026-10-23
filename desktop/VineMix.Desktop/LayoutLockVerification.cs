using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached model and actual control actions; no desktop input, settings, pipe or engine access.</summary>
internal static class LayoutLockVerification
{
    internal static void Run(Action<bool, string> require, Action<FrameworkElement, string>? render = null)
    {
        CheckModelAndHistory(require);
        CheckWorkspaceActions(require);
        LayoutLockFeedbackVerification.Run(require, render);
        CheckHeader(require, render);
    }
    private static MixerSnapshot Snapshot()
    {
        var input = GraphDefaults.NewNode("microphone", 40, 40); input.Id = "lock-input";
        var effect = GraphDefaults.NewNode("eq", 420, 80); effect.Id = "lock-effect";
        var output = GraphDefaults.NewNode("output", 1100, 40); output.Id = "lock-output";
        return new() { Configuration = new() { Version = 2, Revision = 1, Graph = new()
        {
            Nodes = [input, effect, output], Edges =
            [new() { Id = "lock-a", From = input.Id, To = effect.Id }, new() { Id = "lock-b", From = effect.Id, To = output.Id }]
        } } };
    }
    private static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static void CheckModelAndHistory(Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
        mixer.ApplyPreviewSnapshot(Snapshot());
        string original = Json(mixer.GraphConfig);
        mixer.IsLayoutLocked = true;
        require(Json(mixer.GraphConfig) == original && !mixer.CanUndo && mixer.PendingEditCount == 0,
            "Layout lock is session UI state: toggling it never changes the graph, history, save queue or audio parameters.");
        Action<MixerConfiguration>[] blocked =
        [
            c => c.Graph.Nodes[0].X += 8,
            c => c.Graph.Nodes[0].Y += 8,
            c => c.Graph.Nodes.Add(GraphDefaults.NewNode("mix", 0, 0)),
            c => c.Graph.Nodes.RemoveAt(0),
            c => c.Graph.Nodes[1].Kind = "mix",
            c => c.Graph.Edges.RemoveAt(0),
            c => c.Graph.Edges.Add(new() { Id = "extra", From = "lock-input", To = "lock-output" }),
            c => c.Graph.Edges[0].To = "lock-output",
            c => c.Graph.Edges[0].Kind = "sidechain",
            c => { c.Graph.Nodes[0].GainDb = -5; c.Graph.Nodes[0].X += 8; }
        ];
        for (int i = 0; i < blocked.Length; i++)
        {
            mixer.Edit(blocked[i]);
            require(Json(mixer.GraphConfig) == original && !mixer.CanUndo && mixer.PendingEditCount == 0,
                $"Locked structural action {i + 1} is rejected atomically by the model, including any mixed sound change.");
        }
        require(!mixer.TryEdit((MixerConfiguration c, out string error) => { error = ""; c.Graph.Edges.Clear(); return true; }, out string rejection)
            && rejection == MixerViewModel.LayoutLockedMessage && Json(mixer.GraphConfig) == original,
            "Checked structural callbacks receive the same lock refusal without mutating graph or history.");
        mixer.Edit(c => { c.Graph.Nodes[0].GainDb = -6; c.Graph.Nodes[0].Muted = true; c.Graph.Nodes[1].Bypass = true;
            c.Graph.Nodes[1].Name = "Example voice EQ"; c.Graph.Nodes[0].Device = new() { Id = "example-capture", Name = "Example microphone" };
            c.Graph.Edges[0].GainDb = -3; c.Graph.Edges[0].Muted = true; });
        string sound = Json(mixer.GraphConfig);
        require(sound != original && mixer.CanUndo && mixer.Undo() && Json(mixer.GraphConfig) == original
            && mixer.CanRedo && mixer.Redo() && Json(mixer.GraphConfig) == sound,
            "Volume, mute, bypass, rename, device assignment and cable sound controls remain editable and undoable while locked.");
        mixer.SetCanvasView(88, -32, .75);
        require(mixer.IsLayoutLocked && mixer.GraphConfig.PanX == 88 && mixer.GraphConfig.Zoom == .75,
            "Canvas navigation changes remain available while the session layout is locked.");

        using var history = new MixerViewModel(preview: true);
        history.ApplyPreviewSnapshot(Snapshot());
        history.Edit(c => c.Graph.Nodes[0].X += 16);
        string moved = Json(history.GraphConfig);
        history.IsLayoutLocked = true;
        require(history.EditHistory.UndoChangesLayout && !history.CanUndo && !history.Undo() && Json(history.GraphConfig) == moved,
            "A structural Undo is blocked without consuming its history position.");
        history.Edit(c => c.Graph.Nodes[0].GainDb = -9);
        require(history.CanUndo && history.Undo() && Json(history.GraphConfig) == moved && !history.CanUndo,
            "A later sound edit can be undone while locked; Undo stops at the preceding structural edit.");
        history.IsLayoutLocked = false;
        require(history.Undo(), "Unlocking restores access to the existing structural Undo step.");
        string unmoved = Json(history.GraphConfig);
        history.IsLayoutLocked = true;
        require(history.EditHistory.RedoChangesLayout && !history.CanRedo && !history.Redo() && Json(history.GraphConfig) == unmoved,
            "A structural Redo is blocked without consuming its history position.");
        history.IsLayoutLocked = false;
        require(history.Redo() && Json(history.GraphConfig) == moved,
            "Unlocking restores the exact structural Redo step and keeps unrelated sound history.");
        using var newSession = new MixerViewModel(preview: true);
        newSession.ApplyPreviewSnapshot(Snapshot());
        require(!newSession.IsLayoutLocked, "A new session opens unlocked; lock state is not imported from workspace data.");
    }
    private static void CheckWorkspaceActions(Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true);
        mixer.ApplyPreviewSnapshot(Snapshot());
        var workspace = new GraphWorkspace(mixer, useSystemClipboard: false) { Width = 1280, Height = 720 };
        workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            Arrange(workspace);
            var node = Descendants<GraphNodeView>(workspace).Single(view => view.Id == "lock-input");
            var socket = node.FindSocket("audio", true)!;
            var input = Descendants<GraphNodeView>(workspace).Single(view => view.Id == "lock-effect").FindSocket("audio", false)!;
            workspace.SelectNode("lock-input");
            string copied = workspace.CopySelection(), original = Json(mixer.GraphConfig);
            var staleAdd = workspace.CreateAddMenu(new Point(10, 10)).Items.OfType<MenuItem>()
                .SelectMany(item => item.Items.OfType<MenuItem>()).First();
            var staleEdgeRemove = workspace.CreateEdgeMenu("lock-a").Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Remove connection"));
            workspace.BeginNodeMove("lock-input", new Point(100, 100));
            workspace.UpdateNodeMove(new Point(150, 150));
            mixer.IsLayoutLocked = true;
            require(!workspace.IsNodeDragging && Json(mixer.GraphConfig) == original && Canvas.GetLeft(node) == mixer.GraphConfig.Nodes[0].X,
                "Locking during a node drag cancels its visual preview and restores the original position without a history edit.");
            require(workspace.Spawn("mix", new Point()) == "" && workspace.ConnectNodes("lock-input", "lock-output").Length > 0
                && !workspace.PasteSelection(copied, new Point(20, 20)) && !workspace.DuplicateInput("lock-input")
                && !workspace.DetachNode("lock-effect"),
                "Add, connect, paste, duplicate and detach actions consistently refuse locked layout changes.");
            workspace.BeginNodeMove("lock-input", new Point()); workspace.UpdateNodeMove(new Point(40, 40)); workspace.FinishNodeMove();
            workspace.BeginCableCut(new Point()); workspace.UpdateCableCut(new Point(500, 500)); workspace.FinishCableCut();
            workspace.BeginConnectionPreview(socket, new Point(), dragging: true);
            socket.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(!workspace.BeginInputLinkDrag(input, new Point()) && !workspace.HasConnectionPreview
                && !workspace.IsNodeDragging && !workspace.HasCableCutPreview,
                "Locked node movement, cable cutting, socket clicks and cable reconnect cannot begin gesture previews.");
            workspace.DeleteSelection(); workspace.RemoveNode("lock-input"); workspace.RemoveEdge("lock-a");
            workspace.SelectNodeForPointer("lock-effect", ModifierKeys.Control);
            require(!workspace.AlignSelectedNodes(NodeAlignment.Left)
                && !workspace.TryNodeAlignmentShortcut(Key.Y, ModifierKeys.Alt, workspace),
                "Both direct alignment and its keyboard route refuse movement while locked.");
            staleAdd.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); staleEdgeRemove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(Json(mixer.GraphConfig) == original && !mixer.CanUndo && workspace.SelectedNodeIds.Count == 2,
                "Delete/remove actions and menus opened before locking cannot bypass the lock; selection still works.");
            var nodeMenu = new ContextMenu(); workspace.PopulateNodeMenu(nodeMenu, "lock-input", () => { });
            var actions = nodeMenu.Items.OfType<MenuItem>().ToArray();
            require(actions.Where(item => ((string)item.Header).StartsWith("Delete", StringComparison.Ordinal)
                || Equals(item.Header, "Paste") || Equals(item.Header, "Detach node (reconnect)") || Equals(item.Header, "Align selected nodes"))
                .All(item => !item.IsEnabled) && actions.Single(item => Equals(item.Header, "Copy")).IsEnabled,
                "Locked context menus disable structural actions while retaining Copy and inspection.");
            var edgeMenu = workspace.CreateEdgeMenu("lock-a");
            var edgeMute = edgeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Turn connection off"));
            require(edgeMute.IsEnabled && !edgeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Remove connection")).IsEnabled,
                "The cable menu retains sound control while disabling structural removal.");
            edgeMute.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(mixer.GraphConfig.Edges[0].Muted && workspace.TryHistoryShortcut(Key.Z, ModifierKeys.Control, workspace)
                && !mixer.GraphConfig.Edges[0].Muted, "The actual cable mute action and keyboard Undo remain usable while locked.");
            workspace.ResetZoom(); workspace.FitAll(); workspace.FrameSelection();
            workspace.BeginCanvasPan(MouseButton.Middle, new Point(100, 100)); workspace.UpdateCanvasPan(new Point(150, 120));
            workspace.FinishCanvasPan(MouseButton.Middle, new Point(150, 120), out _);
            require(!WorkspaceHistory.ChangesLayout(Snapshot().Configuration.Graph, mixer.GraphConfig)
                && workspace.SelectedNodeIds.Count == 2 && mixer.IsLayoutLocked,
                "Fit, frame selection, zoom and canvas pan preserve topology and remain usable while locked.");
            workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            require(mixer.IsLayoutLocked, "Detaching and reopening the same workspace preserves its session lock.");
        }
        finally { workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }
    private static void CheckHeader(Action<bool, string> require, Action<FrameworkElement, string>? render)
    {
        using var mixer = new MixerViewModel(preview: true);
        mixer.ApplyPreviewSnapshot(Snapshot());
        var window = new MainWindow(mixer);
        var content = (FrameworkElement)window.Content; window.Content = null;
        var root = new Border { Child = content, DataContext = mixer, Width = 900, Height = 580 };
        var button = (Button)window.FindName("LayoutLockButton");
        var settings = (Button)window.FindName("SettingsButton");
        var icon = ((Grid)button.Content).Children.OfType<Viewbox>().Single();
        var strokes = ((Canvas)icon.Child).Children.OfType<Path>().ToArray();
        var shackle = (RotateTransform)strokes[0].RenderTransform;
        var emphasis = (ScaleTransform)icon.RenderTransform;
        var ink = (SolidColorBrush)strokes[0].Stroke;
        bool? originalContrast = ConsoleTheme.PreviewHighContrast;
        try
        {
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            ConsoleTheme.SetPreviewHighContrast(false); window.UpdateLayoutLock(); Arrange(root);
            require(ink.Color == ((SolidColorBrush)ConsoleTheme.Get("ConsoleMutedBrush")).Color
                && ((SolidColorBrush)strokes[1].Stroke).Color == ((SolidColorBrush)window.SaveIndicator.Foreground).Color
                && icon.Width == 16 && icon.Height == 16 && shackle.Angle == 50
                && AutomationProperties.GetName(button).Contains("unlocked", StringComparison.Ordinal)
                && AutomationProperties.GetItemStatus(button) == "Unlocked" && button.Focusable && button.IsTabStop
                && System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(button),
                $"The 16-pixel open padlock matches Saved's muted brush while retaining its accessible, keyboard-focusable caption button (stroke {strokes[1].Stroke}, Saved {window.SaveIndicator.Foreground}, hinge {shackle.Angle}, state {AutomationProperties.GetItemStatus(button)}).");
            window.PreviewLayoutLockMotion = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(shackle.HasAnimatedProperties && (double)shackle.GetAnimationBaseValue(RotateTransform.AngleProperty) == 0,
                "Clicking the padlock animates the hinged shackle toward its closed position.");
            window.UpdateLayoutLock();
            require(mixer.IsLayoutLocked && ink.Color == Color.FromRgb(255, 100, 100)
                && shackle.Angle == 0 && AutomationProperties.GetName(button).Contains("locked", StringComparison.Ordinal)
                && AutomationProperties.GetItemStatus(button) == "Locked" && AutomationProperties.GetLiveSetting(button) == AutomationLiveSetting.Polite,
                "The locked padlock stays red with a closed shape and accessible Locked state.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(shackle.HasAnimatedProperties && (double)shackle.GetAnimationBaseValue(RotateTransform.AngleProperty) == 50,
                "Unlocking animates the same shackle back open without changing the button's click target.");
            window.PreviewLayoutLockMotion = false; mixer.IsLayoutLocked = true;
            require(!shackle.HasAnimatedProperties && shackle.Angle == 0,
                "Reduced motion updates the padlock immediately without a movement animation.");
            string graphBeforeFeedback = Json(mixer.GraphConfig);
            var focusBeforeFeedback = Keyboard.FocusedElement;
            window.Workspace.ConnectNodes("lock-input", "lock-output");
            require(ink.Color == Color.FromRgb(255, 100, 100) && !ink.HasAnimatedProperties && !emphasis.HasAnimatedProperties
                && emphasis.ScaleX == 1.4 && emphasis.ScaleY == 1.4
                && !((Grid)button.Content).Children.OfType<Border>().Any()
                && Json(mixer.GraphConfig) == graphBeforeFeedback && ReferenceEquals(Keyboard.FocusedElement, focusBeforeFeedback)
                && AutomationProperties.GetItemStatus(button).Contains("Unlock to move", StringComparison.Ordinal),
                "A refused cable edit keeps the padlock red and enlarges it by 40 percent without a surrounding card, graph edits, focus theft or interpolated motion in reduced-motion mode.");
            WaitForAnimation(850);
            require(ink.Color == Color.FromRgb(255, 100, 100)
                && emphasis.ScaleX == 1 && AutomationProperties.GetItemStatus(button) == "Locked",
                "The short blocked-edit emphasis returns to normal size automatically while keeping the padlock red and locked.");
            window.PreviewLayoutLockMotion = true; window.Workspace.ConnectNodes("lock-input", "lock-output");
            require(!ink.HasAnimatedProperties && emphasis.HasAnimatedProperties && ink.Color == Color.FromRgb(255, 100, 100),
                "With motion enabled, a refused edit animates only padlock size while its locked red colour stays constant.");
            WaitForAnimation(850);
            require(ink.Color == Color.FromRgb(255, 100, 100) && emphasis.ScaleX == 1 && emphasis.ScaleY == 1
                && !ink.HasAnimatedProperties && !emphasis.HasAnimatedProperties,
                "The quick enlargement and slower return complete and remove the size animation within the feedback interval without any colour animation.");
            window.Workspace.ConnectNodes("lock-input", "lock-output");
            mixer.IsLayoutLocked = false;
            require(ink.Color == ((SolidColorBrush)ConsoleTheme.Get("ConsoleMutedBrush")).Color
                && !ink.HasAnimatedProperties && !emphasis.HasAnimatedProperties && emphasis.ScaleX == 1,
                "Unlocking clears any blocked-edit emphasis immediately, including rapid repeated actions.");
            mixer.IsLayoutLocked = true; window.UpdateLayoutLock();
            ConsoleTheme.SetPreviewHighContrast(true); window.UpdateLayoutLock();
            require(ink.Color == ((SolidColorBrush)ConsoleTheme.Get("ConsoleTextBrush")).Color && shackle.Angle == 0,
                "High contrast uses the system text brush while retaining the closed-padlock shape and textual state.");
            foreach (double scale in new[] { 1d, 1.5, 2 })
            {
                window.UpdateLayoutLock(); window.PreviewLayoutLockMotion = false; window.ShowLayoutLockFeedback();
                VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale)); Arrange(root);
                Point lockOrigin = button.TranslatePoint(new Point(), root), settingsOrigin = settings.TranslatePoint(new Point(), root);
                Rect enlargedIconBounds = icon.TransformToAncestor(root).TransformBounds(new Rect(icon.RenderSize));
                require(button.ActualWidth >= 32 && button.ActualHeight >= 26 && lockOrigin.X + button.ActualWidth <= settingsOrigin.X
                    && lockOrigin.X >= 0 && settingsOrigin.X + settings.ActualWidth <= root.ActualWidth
                    && emphasis.ScaleX == 1.4 && emphasis.ScaleY == 1.4
                    && enlargedIconBounds.Left >= lockOrigin.X && enlargedIconBounds.Right <= settingsOrigin.X,
                    $"At {scale * 100:0}% display scale the padlock and its 40-percent enlarged icon fit the minimum-width header without covering Settings.");
            }
            require(PresentationSource.FromVisual(root) is null && new System.Windows.Interop.WindowInteropHelper(window).Handle == IntPtr.Zero,
                "Layout-lock header verification creates no native window and never injects desktop input.");
            if (render is not null)
            {
                // Reparent the real action after header checks: preview exactly
                // its production geometry, dimensions, style and state text.
                ((Panel)button.Parent).Children.Remove(button);
                var label = new TextBlock { Width = 170, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                label.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush");
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(button); row.Children.Add(label);
                var preview = new Border { Width = 280, Height = 72, Child = row };
                preview.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
                window.Content = preview; // Resource ownership only; never show or create an HWND.
                foreach (bool highContrast in new[] { false, true })
                foreach (bool locked in new[] { false, true })
                {
                    ConsoleTheme.SetPreviewHighContrast(highContrast); mixer.IsLayoutLocked = locked; window.UpdateLayoutLock();
                    label.Text = locked ? "Layout locked" : "Layout unlocked";
                    Arrange(preview);
                    render(preview, "layout-lock-" + (locked ? "locked" : "unlocked") + (highContrast ? "-high-contrast" : "") + ".png");
                }
                ConsoleTheme.SetPreviewHighContrast(false); mixer.IsLayoutLocked = true; window.UpdateLayoutLock();
                window.PreviewLayoutLockMotion = false; window.ShowLayoutLockFeedback();
                label.Text = "Blocked edit feedback"; Arrange(preview);
                render(preview, "layout-lock-feedback.png");
            }
        }
        finally
        {
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            ConsoleTheme.SetPreviewHighContrast(originalContrast);
            window.Workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
    }
    private static void Arrange(FrameworkElement element)
    { element.Measure(new Size(element.Width, element.Height)); element.Arrange(new Rect(0, 0, element.Width, element.Height)); element.UpdateLayout(); }
    private static void WaitForAnimation(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
