using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

internal static class WorkspaceFeedbackVerification
{
    internal static async Task RunAsync(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<string> capture)
    {
        var snapshot = UiPreview.CreateSnapshot([]);
        snapshot.Configuration.Graph = new();
        mixer.ApplyPreviewSnapshot(snapshot); await layout();
        workspace.DismissMessage();
        require(!workspace.HasFeedback && workspace.FeedbackText.StartsWith("Dials:"),
            "The feedback footer starts with the shared dial guide, without a dismiss control.");
        var undo = Descendants<Button>(workspace).Single(b => Equals(b.Content, "Undo"));
        var redo = Descendants<Button>(workspace).Single(b => Equals(b.Content, "Redo"));
        require(!undo.IsEnabled && !redo.IsEnabled, "Undo and Redo start disabled for a newly loaded graph.");

        string output = workspace.Spawn("output", new Point(60, 40)); await layout();
        require(workspace.HasFeedback && workspace.FeedbackText.Contains("muted") && workspace.FeedbackText.Contains("Choose a device"),
            "Adding an output immediately explains its initial mute and required device selection in the visible footer.");
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
        require(!workspace.HasFeedback && workspace.FeedbackText.StartsWith("Dials:") && mixer.EditHistory.Entries.Count == edits,
            "Dismissing feedback restores the shared guide without changing the graph or edit history.");
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
