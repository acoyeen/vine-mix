using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace VineMix.Desktop;

internal static class WorkspaceChromeVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout, Action<bool,string> require)
    {
        mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot([])); await layout();
        long count = mixer.ConfigurationPresentationCount;
        int history = mixer.EditHistory.Entries.Count;
        string saved = mixer.SaveStatus;
        mixer.SetCanvasView(130, 90, .75);
        mixer.RefreshPreviewTelemetry(); await layout();
        require(mixer.ConfigurationPresentationCount == count && mixer.EditHistory.Entries.Count == history && mixer.SaveStatus == saved,
            "Canvas navigation never queues a configuration edit, changes save status or enters edit history.");
        require(mixer.GraphConfig.PanX == 130 && mixer.GraphConfig.Zoom == .75, "Local canvas navigation survives a telemetry refresh.");
        workspace.ApplyWheelDelta(new(250, 200), -120); workspace.ResetZoom(); workspace.FitAll();
        require(mixer.ConfigurationPresentationCount == count && mixer.EditHistory.Entries.Count == history && mixer.SaveStatus == saved,
            "Actual wheel zoom, reset zoom and Fit leave save status, configuration dispatch and history untouched.");
        mixer.Edit(_ => { });
        require(mixer.ConfigurationPresentationCount == count && mixer.EditHistory.Entries.Count == history, "No-op edits create neither saves nor history entries.");
        var node = mixer.GraphConfig.Nodes.First();
        mixer.Edit(c => c.Graph.Nodes.Single(n => n.Id == node.Id).X += 16);
        require(mixer.ConfigurationPresentationCount == count + 1 && mixer.EditHistory.Entries[0].Description.StartsWith("Moved "), "An actual node move is one edit with a descriptive history entry.");
        int firstParameterCount = mixer.EditHistory.Entries.Count;
        mixer.BeginEditGesture();
        mixer.Edit(c => c.Graph.Nodes.Single(n => n.Id == node.Id).GainDb = -20);
        mixer.Edit(c => c.Graph.Nodes.Single(n => n.Id == node.Id).GainDb = -21);
        mixer.EndEditGesture();
        require(mixer.EditHistory.Entries.Count == firstParameterCount + 1 && mixer.EditHistory.Entries[0].Description.StartsWith("Adjusted "), "Continuous parameter adjustments coalesce in history while still updating the audio controls.");
        var menu = workspace.CreateAddMenu(new(20,20));
        var groups = menu.Items.OfType<MenuItem>().Where(i => i.Items.Count > 0).ToArray();
        require(groups.Select(g => (string)g.Header).SequenceEqual(NodeCatalog.Categories.Select(c => c.Title)), "Add menu exposes the five ordered node categories.");
        require(groups.SelectMany(g => g.Items.OfType<MenuItem>()).Count() == NodeCatalog.Categories.Sum(category => category.Kinds.Count)
            && groups.All(g => g.Icon is not null)
            && groups.SelectMany(g => g.Items.OfType<MenuItem>()).All(item => !Equals(item.Header, "Reroute") && !Equals(item.Header, "Gain")),
            "Every creatable node belongs to one coloured Add submenu; redundant legacy Gain and Reroute entries are omitted.");
        var search = ((Grid)((MenuItem)menu.Items[0]).Header).Children.OfType<TextBox>().Single();
        search.Text = "effects";
        require(groups.All(g => g.Visibility == Visibility.Collapsed) && menu.Items.OfType<MenuItem>().Count(i => i.Visibility == Visibility.Visible && i.Icon is not null)
                == NodeCatalog.Categories.Single(category => category.Title == "Effects").Kinds.Count,
            "Search includes category names and displays every matching effect directly.");
        search.Text = "no such node";
        require(menu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "No matching nodes") && i.Visibility == Visibility.Visible), "Add search reports an empty result explicitly.");
        foreach (string legacyName in new[] { "reroute", "gain" })
        {
            search.Text = legacyName;
            require(!menu.Items.OfType<MenuItem>().Any(i => i.Visibility == Visibility.Visible && i.Icon is not null
                && (Equals(i.Header, "Gain") || Equals(i.Header, "Reroute"))),
                "Searching for legacy " + legacyName + " cannot offer an uncreatable node that silently fails when clicked.");
        }
        search.Text = "";
        require(groups.All(g => g.Visibility == Visibility.Visible), "Clearing search restores all category submenus.");
        require(workspace.CreateHistoryMenu().Items.OfType<MenuItem>().Any(i => Equals(i.Header, mixer.EditHistory.Entries[0].Description)), "Activity menu retains the workspace edit descriptions.");
        int activityOpened = 0;
        long beforeActivity = mixer.ConfigurationPresentationCount;
        var settings = MainWindow.CreateSettingsMenu(mixer, showActivity: () => activityOpened++);
        var activity = settings.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Activity"));
        var find = settings.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Find node…"));
        activity.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(settings.Items.IndexOf(activity) == settings.Items.IndexOf(find) + 1
            && AutomationProperties.GetName(activity) == "Activity" && activityOpened == 1 && !settings.IsOpen
            && mixer.ConfigurationPresentationCount == beforeActivity,
            "Settings groups Activity with Find node; its action closes Settings and opens only session history without editing the workspace.");

        var notifications = settings.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Show action notifications"));
        require(notifications.IsCheckable && notifications.IsChecked && mixer.ShowActionNotifications,
            "Settings exposes action notifications as a checked preference by default.");
        string graphBefore = System.Text.Json.JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        int historyBefore = mixer.EditHistory.Entries.Count;
        string saveBefore = mixer.SaveStatus;
        workspace.SetMessage("Notification preference example.");
        notifications.IsChecked = false;
        await layout();
        require(!mixer.ShowActionNotifications && !workspace.HasFeedback && !workspace.FeedbackCountdownTimerRunning
            && !workspace.FeedbackCountdownAnimationActive && workspace.CountdownElement.Visibility == Visibility.Hidden,
            "Turning off notifications through Settings immediately dismisses a routine message and stops its timer and animation.");
        workspace.SetMessage("Hidden action retained in Activity.");
        require(!workspace.HasFeedback && workspace.FeedbackText.Length == 0 && !workspace.FeedbackCountdownTimerRunning
            && workspace.CreateHistoryMenu().Items.OfType<MenuItem>().Any(item => item.Header is TextBlock text && text.Text == "Hidden action retained in Activity."),
            "Suppressed messages do not occupy the footer or start a timer, but Activity retains the latest message.");
        workspace.SetMessage("Example save failure.", isError: true);
        await layout();
        require(workspace.HasFeedback && workspace.FeedbackText == "Attention: Example save failure." && workspace.FeedbackCountdownTimerRunning,
            "Errors remain visible with their normal dismissal behavior when action notifications are off.");
        workspace.SetMessage("Another hidden action.");
        require(workspace.FeedbackText == "Attention: Example save failure.",
            "A suppressed routine message does not overwrite an error already on screen.");
        notifications.IsChecked = true; notifications.IsChecked = false;
        await layout();
        require(workspace.HasFeedback && workspace.FeedbackText == "Attention: Example save failure.",
            "Changing the notification preference never dismisses an active error.");
        using (var restored = new MixerViewModel(preview: true))
        {
            restored.LoadUiPreferencesJson(mixer.SerializeUiPreferences());
            require(!restored.ShowActionNotifications, "The disabled notification preference survives serialization and reload.");
            restored.LoadUiPreferencesJson("{\"rightClickDragToPan\":true}");
            require(restored.ShowActionNotifications && restored.RightClickDragToPan,
                "Older preferences retain their settings and default action notifications to on.");
        }
        workspace.DismissMessage();
        notifications.IsChecked = true;
        await layout();
        require(mixer.ShowActionNotifications && !workspace.HasFeedback,
            "Re-enabling notifications does not replay previously hidden messages.");
        workspace.SetMessage("New visible action.");
        require(workspace.HasFeedback && workspace.FeedbackText == "New visible action." && workspace.FeedbackCountdownTimerRunning,
            "New routine messages resume their countdown after re-enabling notifications.");
        require(mixer.ConfigurationPresentationCount == beforeActivity && mixer.EditHistory.Entries.Count == historyBefore
            && mixer.SaveStatus == saveBefore && System.Text.Json.JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == graphBefore,
            "Notification preferences and messages do not edit routing, save status or graph history.");
        workspace.DismissMessage();
    }
}
