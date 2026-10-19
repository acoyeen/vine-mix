using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AudioMixer.Desktop;

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
    }
}
