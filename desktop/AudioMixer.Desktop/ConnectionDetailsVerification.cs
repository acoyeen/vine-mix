using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached cable descriptions and menu actions; no popup, input, engine or settings access.</summary>
internal static class ConnectionDetailsVerification
{
    private const string ResetLabel = "Reset connection level to 0 dB";

    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<FrameworkElement, string>? render = null)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Cable details are verified on the detached preview without a live engine or native window.");
        mixer.ApplyPreviewSnapshot(Fixture()); await layout();
        string before = Json(mixer.GraphConfig); long presentations = mixer.ConfigurationPresentationCount;
        int history = mixer.EditHistory.Entries.Count;
        foreach (var item in new[]
        {
            (Id: "audio", Title: "Audio connection", State: "Connection off · Saved level -9 dB"),
            (Id: "control", Title: "Linked control connection", State: "Linked control off · Saved offset +2.5 dB"),
            (Id: "detector", Title: "Sidechain / trigger connection", State: "Detector off · Saved level -3.25 dB")
        })
        {
            var edge = mixer.GraphConfig.Edges.Single(e => e.Id == item.Id);
            var details = GraphWorkspace.DescribeConnection(mixer.GraphConfig, edge);
            var tip = (StackPanel)workspace.ConnectionToolTip(item.Id)!;
            require(details.Title == item.Title && details.State == item.State && Lines(tip).SequenceEqual([details.Route]),
                item.Id + " hover shows only the actual source → destination, without a title, level, state, role or gesture instructions.");
            var hit = workspace.ConnectionPaths(item.Id)!.Value.Hit;
            string accessibleDetails = AutomationProperties.GetHelpText(hit);
            require(AutomationProperties.GetName(hit) == details.Route && accessibleDetails.Contains(item.Title)
                && accessibleDetails.Contains(item.State),
                item.Id + " keeps its accurate endpoint name, connection kind, saved level and off state available to accessibility tools.");
            if (item.Id != "audio")
                require(accessibleDetails.Contains(item.Id == "control" ? "carries no audio" : "adds no audible audio"),
                    item.Id + " accessible details retain the control/detector role without adding it to the hover text.");
            workspace.SelectEdge(item.Id);
            require(workspace.FeedbackText.Contains(details.Route) && workspace.FeedbackText.Contains(item.Title)
                && workspace.FeedbackText.Contains(item.State),
                item.Id + " selection reports the same endpoints, kind, saved level and off state.");
            var menu = workspace.CreateEdgeMenu(item.Id);
            require(!menu.IsOpen && PresentationSource.FromVisual(menu) is null && Reset(menu).IsEnabled
                && menu.Items.OfType<MenuItem>().Any(i => (i.Header as string)?.StartsWith("Turn connection") == true) == (item.Id == "audio"),
                item.Id + " menu is inspectable without opening a popup; on/off remains available only for audio cables.");
            require(Lines((StackPanel)Reset(menu).ToolTip).Any(line => line.Contains("Can increase volume")),
                "The saved-level reset explains that removing attenuation can increase volume.");
            RenderTip(tip, item.Id + "-connection-details.png", render);
        }
        require(Json(mixer.GraphConfig) == before && mixer.ConfigurationPresentationCount == presentations
            && mixer.EditHistory.Entries.Count == history,
            "Inspecting cable hover, selection and unopened menus changes no graph values, saves or Undo history.");

        var originalTip = workspace.ConnectionToolTip("audio");
        mixer.RefreshPreviewTelemetry(); await layout();
        require(ReferenceEquals(originalTip, workspace.ConnectionToolTip("audio")),
            "Ordinary telemetry refresh reuses the existing tooltip visual tree.");
        workspace.NodeChanged("source", node => node.Name = "Renamed microphone with a deliberately long source label");
        workspace.NodeChanged("mix", node => node.Name = "Renamed destination"); await layout();
        var renamedTip = (StackPanel)workspace.ConnectionToolTip("audio")!;
        const string renamedRoute = "Renamed microphone with a deliberately long source label → Renamed destination";
        require(!ReferenceEquals(originalTip, renamedTip) && Lines(renamedTip).SequenceEqual([renamedRoute]),
            "A reused wire refreshes its single hover route when either node is renamed.");
        workspace.EdgeChanged("audio", edge => { edge.GainDb = -.125; edge.Muted = false; }); await layout();
        workspace.SelectEdge("audio");
        require(ReferenceEquals(renamedTip, workspace.ConnectionToolTip("audio"))
            && Lines((StackPanel)workspace.ConnectionToolTip("audio")!).SequenceEqual([renamedRoute])
            && workspace.FeedbackText.Contains("Connection on · Saved level -0.125 dB")
            && AutomationProperties.GetHelpText(workspace.ConnectionPaths("audio")!.Value.Hit).Contains("Connection on · Saved level -0.125 dB"),
            "A level or on/off change reuses the same route-only hover while selection and accessibility retain the exact updated state.");
        workspace.EdgeChanged("audio", edge => edge.To = "alternate"); await layout();
        require(Lines((StackPanel)workspace.ConnectionToolTip("audio")!).SequenceEqual([
            "Renamed microphone with a deliberately long source label → Alternative mix"]),
            "Rewiring an existing cable refreshes its single hover route without creating a new cable ID.");
        var stableTip = workspace.ConnectionToolTip("audio");
        mixer.RefreshPreviewTelemetry(); await layout();
        require(ReferenceEquals(stableTip, workspace.ConnectionToolTip("audio")),
            "The refreshed endpoint tooltip remains cached on subsequent meter packets.");
        var detectorTip = workspace.ConnectionToolTip("detector");
        workspace.EdgeChanged("detector", edge => edge.Kind = "audio"); await layout();
        require(ReferenceEquals(detectorTip, workspace.ConnectionToolTip("detector"))
            && Lines((StackPanel)detectorTip!).SequenceEqual([
                "Renamed microphone with a deliberately long source label → Compressor"])
            && AutomationProperties.GetHelpText(workspace.ConnectionPaths("detector")!.Value.Hit).StartsWith("Audio connection."),
            "Changing cable kind without changing its endpoint names reuses the route-only hover and refreshes the accessible role.");

        foreach (string id in new[] { "audio", "control", "detector" })
        {
            mixer.ApplyPreviewSnapshot(Fixture()); await layout();
            string original = Json(mixer.GraphConfig);
            var expected = mixer.GraphConfig.Copy(); expected.Edges.Single(edge => edge.Id == id).GainDb = 0;
            var menu = workspace.CreateEdgeMenu(id); long count = mixer.ConfigurationPresentationCount;
            Reset(menu).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await layout();
            require(Json(mixer.GraphConfig) == Json(expected) && mixer.EditHistory.Entries.Count == 1
                && mixer.ConfigurationPresentationCount == count + 1,
                id + " saved-level reset changes only that cable's gain in one undoable edit, preserving its mute, identity, endpoints and kind.");
            require(!workspace.CreateEdgeMenu(id).Items.OfType<MenuItem>().Any(item => Equals(item.Header, ResetLabel)),
                id + " menu omits the reset action once its connection level is already 0 dB.");
            count = mixer.ConfigurationPresentationCount;
            Reset(menu).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(mixer.ConfigurationPresentationCount == count && mixer.EditHistory.Entries.Count == 1,
                "Reusing the old reset action at 0 dB does not save or create another history entry.");
            require(mixer.Undo() && Json(mixer.GraphConfig) == original && mixer.Redo() && Json(mixer.GraphConfig) == Json(expected),
                id + " reset Undo/Redo restores the exact original and reset graphs.");
        }

        mixer.ApplyPreviewSnapshot(Fixture()); await layout();
        var staleMenu = workspace.CreateEdgeMenu("audio");
        workspace.EdgeChanged("audio", edge => edge.To = "alternate");
        before = Json(mixer.GraphConfig); history = mixer.EditHistory.Entries.Count;
        foreach (var item in staleMenu.Items.OfType<MenuItem>()) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(Json(mixer.GraphConfig) == before && mixer.EditHistory.Entries.Count == history,
            "Actions from a stale menu do not change a cable that has since been rewired under the same ID.");
        staleMenu = workspace.CreateEdgeMenu("audio"); workspace.RemoveEdge("audio");
        before = Json(mixer.GraphConfig); history = mixer.EditHistory.Entries.Count;
        foreach (var item in staleMenu.Items.OfType<MenuItem>()) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(Json(mixer.GraphConfig) == before && mixer.EditHistory.Entries.Count == history
            && workspace.CreateEdgeMenu("audio").Items.Count == 0,
            "A removed cable has no new menu actions and old menu callbacks cannot recreate or edit it.");

        foreach (string action in new[] { ResetLabel, "Turn connection on", "Remove connection" })
        {
            using var queued = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
            queued.ApplyPreviewSnapshot(Fixture()); var queuedWorkspace = new GraphWorkspace(queued);
            queuedWorkspace.CreateEdgeMenu("audio").Items.OfType<MenuItem>().Single(item => Equals(item.Header, action))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(queued.PendingEditCount == 1, action + " uses the ordinary pending-edit queue.");
            var external = Fixture(); external.Configuration.Revision = 2;
            external.Configuration.Graph.Edges.Single(edge => edge.Id == "audio").To = "alternate";
            queued.ApplyPreviewSnapshot(external);
            require(Json(queued.GraphConfig) == Json(external.Configuration.Graph) && queued.PendingEditCount == 0 && !queued.CanUndo,
                action + " refuses queued replay after the same cable ID is rewired externally, preserving the new route and its settings.");
        }
        using (var offline = new MixerViewModel(preview: true))
        {
            offline.ApplyPreviewSnapshot(Fixture()); var offlineWorkspace = new GraphWorkspace(offline);
            var openBeforeDisconnect = offlineWorkspace.CreateEdgeMenu("audio");
            await offline.StopAsync();
            var menu = offlineWorkspace.CreateEdgeMenu("audio"); before = Json(offline.GraphConfig);
            require(!offline.CanEdit && menu.Items.OfType<MenuItem>().All(item => !item.IsEnabled)
                && openBeforeDisconnect.Items.OfType<MenuItem>().All(item => !item.IsEnabled),
                "Offline cable actions are disabled, including an existing menu when the engine disconnects.");
            foreach (var item in openBeforeDisconnect.Items.OfType<MenuItem>()) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(Json(offline.GraphConfig) == before && offline.EditHistory.Entries.Count == 0 && !menu.IsOpen
                && PresentationSource.FromVisual(offlineWorkspace) is null,
                "Even a stale routed menu click cannot change the offline preview; no popup or native window is created.");
        }
        mixer.ApplyPreviewSnapshot(Fixture()); workspace.DismissMessage(); await layout();
    }

    private static MixerSnapshot Fixture()
    {
        GraphNode Node(string kind, string id, string name, double x, double y)
        { var node = GraphDefaults.NewNode(kind, x, y); node.Id = id; node.Name = name; return node; }
        var graph = new GraphConfiguration
        {
            Nodes = [Node("microphone", "source", "Microphone", 20, 20), Node("mix", "mix", "Main mix", 420, 20),
                Node("control", "linked", "Linked level", 20, 390), Node("compressor", "processor", "Compressor", 820, 20),
                Node("mix", "alternate", "Alternative mix", 420, 390)],
            Edges = [new() { Id="audio", From="source", To="mix", GainDb=-9, Muted=true },
                new() { Id="control", From="linked", To="mix", Kind="control", GainDb=2.5, Muted=true },
                new() { Id="detector", From="source", To="processor", Kind="sidechain", GainDb=-3.25, Muted=true }]
        };
        return new() { Configuration = new() { Version=2, Revision=1, Graph=graph }, Status = new()
            { Nodes=graph.Nodes.Select(node => new GraphNodeStatus { Id=node.Id, Path=new() { State="Running" } }).ToList() } };
    }
    private static MenuItem Reset(ContextMenu menu) => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, ResetLabel));
    private static string[] Lines(StackPanel panel) => panel.Children.OfType<TextBlock>().Select(line => line.Text).ToArray();
    private static string Json(GraphConfiguration graph) => JsonSerializer.Serialize(graph, Protocol.Json);
    private static void RenderTip(StackPanel tip, string filename, Action<FrameworkElement, string>? render)
    {
        if (render is null) return;
        var host = new Border { Width=340, Padding=new Thickness(12), Child=tip };
        host.SetResourceReference(Border.BackgroundProperty, "ConsolePanelBrush");
        try
        {
            host.Measure(new Size(340, double.PositiveInfinity)); host.Height=Math.Ceiling(host.DesiredSize.Height);
            host.Arrange(new Rect(0, 0, host.Width, host.Height)); host.UpdateLayout(); render(host, filename);
        }
        finally { host.Child=null; }
    }
}
