using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

internal static class WorkspaceFeedbackVerification
{
    internal static async Task RunAsync(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<string> capture)
    {
        var snapshot = UiPreview.CreateSnapshot([]);
        snapshot.Configuration.Graph = new();
        mixer.ApplyPreviewSnapshot(snapshot); await layout();
        workspace.DismissMessage(); await layout();
        var status = Descendants<TextBlock>(workspace).Single(t => t.Name == "SaveIndicator");
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(new Point(), element.RenderSize));
        require(!workspace.HasFeedback && workspace.FeedbackText.Length == 0 && VisibleInTree(status)
            && !Descendants<TextBlock>(workspace).Any(t => t.Text.StartsWith("Dials:")),
            "The idle footer shows save status without the old controls hint or dismiss control.");
        require(Math.Abs(Bounds(status).Left + Bounds(status).Width / 2 - root.ActualWidth / 2) < 1
            && Bounds(status).Top > root.ActualHeight - 40 && status.TextAlignment == TextAlignment.Center,
            "Save status is centred across the footer at the bottom of the window.");
        require(BindingOperations.GetBinding(status, TextBlock.TextProperty)?.Path.Path == nameof(MixerViewModel.SaveStatus)
            && status.Text == "Preview" && Equals(status.ToolTip, mixer.SaveStatus)
            && AutomationProperties.GetName(status) == mixer.SaveStatus,
            "The moved save status keeps its compact binding and full tooltip and accessible status.");
        capture("feedback-idle-status.png");
        var undo = Descendants<Button>(workspace).Single(b => Equals(b.Content, "Undo"));
        var redo = Descendants<Button>(workspace).Single(b => Equals(b.Content, "Redo"));
        require(!undo.IsEnabled && !redo.IsEnabled, "Undo and Redo start disabled for a newly loaded graph.");

        string output = workspace.Spawn("output", new Point(60, 40)); await layout();
        require(workspace.HasFeedback && workspace.FeedbackText.Contains("muted") && workspace.FeedbackText.Contains("Choose a device"),
            "Adding an output immediately explains its initial mute and required device selection in the visible footer.");
        require(!VisibleInTree(status) && Equals(status.ToolTip, mixer.SaveStatus)
            && AutomationProperties.GetName(status) == mixer.SaveStatus && mixer.SaveStatus.Contains("changes stay"),
            "Action feedback temporarily hides save status while its binding continues to follow new state.");
        require(undo.IsEnabled && !redo.IsEnabled && AutomationProperties.GetName(undo).Contains("Added"),
            "The Undo button becomes available and exposes the next action's description.");
        capture("feedback-output-added.png");
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await layout();
        require(mixer.GraphConfig.Nodes.Count == 0 && workspace.FeedbackText.StartsWith("Undone:") && redo.IsEnabled,
            "The actual Undo button restores the graph and immediately reports the action.");
        redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await layout();
        require(mixer.GraphConfig.Nodes.Single().Id == output && workspace.FeedbackText.StartsWith("Redone:"),
            "The actual Redo button restores the same node identity and reports the action.");

        var entry = new TextBox { Text = "uncommitted value" };
        require(!workspace.TryHistoryShortcut(Key.Z, ModifierKeys.Control, entry) && mixer.GraphConfig.Nodes.Count == 1,
            "Graph Undo never steals Ctrl+Z from a focused text editor.");
        require(workspace.TryHistoryShortcut(Key.Z, ModifierKeys.Control, undo) && mixer.GraphConfig.Nodes.Count == 0,
            "Ctrl+Z invokes graph Undo outside text editors.");
        require(workspace.TryHistoryShortcut(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, redo)
            && mixer.GraphConfig.Nodes.Single().Id == output,
            "Ctrl+Shift+Z invokes Redo outside text editors.");
        require(!workspace.TryHistoryShortcut(Key.Z, ModifierKeys.Alt, undo), "Unrelated modifier combinations do not invoke history.");

        int edits = mixer.EditHistory.Entries.Count;
        require(!workspace.PasteSelection("invalid clipboard content", new Point(20, 20)) && workspace.HasFeedback
            && workspace.FeedbackText.StartsWith("Attention:") && mixer.EditHistory.Entries.Count == edits,
            "Rejected paste immediately displays an error and creates no undo step.");
        var feedback = Descendants<TextBlock>(workspace).Single(t => t.Text == workspace.FeedbackText);
        require(AutomationProperties.GetLiveSetting(feedback) == AutomationLiveSetting.Polite,
            "Action feedback is exposed as a polite accessible live region.");
        workspace.SetMessage("This connection cannot be used because the selected destination is unavailable. Choose another device in the output node, then reconnect this cable. The original routing has been kept.", isError: true);
        double oldWidth = root.Width, oldHeight = root.Height;
        try
        {
            root.Width = 900; root.Height = 580; await layout();
            var bounds = feedback.TransformToAncestor(root).TransformBounds(new Rect(new Point(), feedback.RenderSize));
            require(bounds.Left >= 0 && bounds.Right <= root.ActualWidth && bounds.Bottom <= root.ActualHeight
                && feedback.TextWrapping == TextWrapping.Wrap && feedback.ActualHeight > 20,
                "Long recovery messages wrap within the minimum window instead of being clipped or tooltip-only.");
            capture("feedback-error-900x580.png");
            ConsoleTheme.SetPreviewHighContrast(true); await layout(); capture("feedback-high-contrast.png");
        }
        finally
        {
            ConsoleTheme.SetPreviewHighContrast(null);
            root.Width = oldWidth; root.Height = oldHeight; await layout();
        }
        var dismiss = Descendants<Button>(workspace).Single(b => AutomationProperties.GetName(b) == "Dismiss workspace message");
        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await layout();
        require(!workspace.HasFeedback && workspace.FeedbackText.Length == 0 && VisibleInTree(status)
            && !workspace.FeedbackCountdownTimerRunning && !workspace.FeedbackCountdownAnimationActive
            && workspace.CountdownElement.Visibility != Visibility.Visible && mixer.EditHistory.Entries.Count == edits,
            "Manual dismissal restores save status and stops the countdown without changing the graph or edit history.");
        require(workspace.CreateHistoryMenu().Items.OfType<MenuItem>().Any(i => i.Header is TextBlock t && t.Text.Contains("original routing")),
            "Dismissed messages remain available in Activity.");
        var warningSnapshot = UiPreview.CreateSnapshot([]);
        warningSnapshot.Warning = "Waiting for the assigned device.";
        mixer.ApplyPreviewSnapshot(warningSnapshot);
        require(workspace.FeedbackText.Contains(warningSnapshot.Warning), "Engine warnings appear in the visible feedback footer.");
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([]));
        require(!workspace.HasFeedback, "A resolved engine warning clears its stale footer message.");
        mixer.ApplyPreviewSnapshot(warningSnapshot);
        workspace.SetMessage("Connection selected.");
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([]));
        require(workspace.FeedbackText == "Connection selected.", "Warning recovery does not dismiss a newer unrelated action message.");
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); workspace.DismissMessage(); await layout();
        await VerifyCountdownAsync(root, workspace, mixer, status, layout, require, capture);
    }

    private static async Task VerifyCountdownAsync(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer,
        TextBlock status, Func<Task> layout, Action<bool, string> require, Action<string> capture)
    {
        var dormant = new GraphWorkspace(mixer, useSystemClipboard: false);
        try
        {
            workspace.PreviewFeedbackMotion = true;
            dormant.PreviewFeedbackMotion = true;
            dormant.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            dormant.SetMessage("Temporary lifecycle message.");
            dormant.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            require(!dormant.FeedbackCountdownTimerRunning && !dormant.FeedbackCountdownAnimationActive,
                "Unloading a workspace releases its message timer and animation clock.");

            workspace.SetMessage("Connection selected."); await layout();
            var bar = (Border)workspace.CountdownElement;
            require(bar.Height == 2 && bar.RenderTransformOrigin.X == 0 && !bar.IsHitTestVisible
                && Math.Abs(bar.ActualWidth - workspace.ActualWidth) < 1
                && Equals(bar.Background, ConsoleTheme.Get("ConsoleMeterLowBrush"))
                && workspace.FeedbackCountdownTimerRunning && workspace.FeedbackCountdownAnimationActive,
                "Action messages start a thin green full-width countdown anchored at the left edge.");
            double footerTop = ((FrameworkElement)VisualTreeHelper.GetParent(bar)).TranslatePoint(new Point(), root).Y;
            require(Math.Abs(bar.TranslatePoint(new Point(), root).Y - footerTop) < .1,
                "The countdown sits across the top edge of the footer.");
            await Task.Delay(1100);
            workspace.SeekFeedbackCountdown(2.5);
            require(Math.Abs(workspace.FeedbackCountdownFraction - .5) < .01,
                "Halfway through the five-second animation the bar has exactly half its width.");
            capture("feedback-countdown-half.png");
            dormant.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            require(dormant.HasFeedback && dormant.FeedbackCountdownTimerRunning
                && dormant.FeedbackCountdownFraction < .85 && dormant.FeedbackCountdownFraction > .5,
                "Reloading resumes only the time remaining instead of restarting the countdown.");
            dormant.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

            workspace.SetMessage("Connection selected."); await layout();
            require(workspace.FeedbackCountdownFraction > .95 && !VisibleInTree(status),
                "Repeating identical feedback restarts the full countdown and keeps save status hidden.");
            await Task.Delay(4200);
            require(workspace.HasFeedback && workspace.FeedbackCountdownFraction is > 0 and < .2,
                "A repeated message survives the previous deadline and depletes from right to left.");
            await Task.Delay(1100); await layout();
            require(!workspace.HasFeedback && VisibleInTree(status) && !workspace.FeedbackCountdownTimerRunning
                && !workspace.FeedbackCountdownAnimationActive && bar.Visibility != Visibility.Visible,
                "After five seconds the real dispatcher timer dismisses feedback and restores save status.");
            require(workspace.CreateHistoryMenu().Items.OfType<MenuItem>().Any(i => i.Header is TextBlock t && t.Text.Contains("Connection selected")),
                "Automatically dismissed feedback remains available in Activity.");
            capture("feedback-auto-dismissed.png");
            dormant.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            require(!dormant.HasFeedback && !dormant.FeedbackCountdownTimerRunning,
                "A message that expires while unloaded stays dismissed when the workspace returns.");

            workspace.PreviewFeedbackMotion = false;
            workspace.SetMessage("Reduced-motion message."); await layout();
            require(!workspace.FeedbackCountdownAnimationActive && workspace.FeedbackCountdownTimerRunning
                && workspace.FeedbackCountdownFraction == 1,
                "Reduced motion keeps a static bar while retaining automatic dismissal.");
            await Task.Delay(5200); await layout();
            require(!workspace.HasFeedback && VisibleInTree(status),
                "Reduced-motion feedback also expires after five seconds.");
        }
        finally
        {
            dormant.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            workspace.PreviewFeedbackMotion = null;
            workspace.DismissMessage();
        }
    }

    private static bool VisibleInTree(DependencyObject current)
    {
        for (DependencyObject? element = current; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return true;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
