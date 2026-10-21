using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached presentation checks: no native window, focus, audio or input injection.</summary>
internal static class NodePresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Node geometry and cursor checks use a detached preview workspace.");
        var catalogKinds = NodeCatalog.Categories.SelectMany(category => category.Kinds).ToArray();
        require(NodeCatalog.Categories.Select(category => category.Title).SequenceEqual(["Inputs", "Outputs", "Effects", "Mixing", "Controls"])
            && catalogKinds.Length == GraphDefaults.Kinds.Length - 2 && catalogKinds.Distinct().Count() == catalogKinds.Length
            && catalogKinds.ToHashSet().SetEquals(GraphDefaults.Kinds.Where(kind => kind is not "reroute" and not "gain"))
            && !NodeCatalog.CanCreate("reroute") && !NodeCatalog.CanCreate("gain"),
            "The shared catalog groups every creatable node exactly once; legacy Gain and Reroute remain supported without appearing in Add node.");

        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new()
        {
            Nodes = GraphDefaults.Kinds.Select((kind, index) => GraphDefaults.NewNode(kind, index * 480, 80)).ToList(), Zoom = 1
        };
        fixture.Configuration.Graph.Nodes.Single(node => node.Kind == "eq").EqBands = EqBand.Defaults(); // Populated-to-empty geometry comparison.
        foreach (var node in fixture.Configuration.Graph.Nodes) node.Muted = false;
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => Status(node.Id, "Running")).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        var views = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id);
        var baseline = views.ToDictionary(pair => pair.Key, pair => Geometry(pair.Value));
        void Stable(string reason)
        {
            var changed = views.Where(pair => !Same(baseline[pair.Key], Geometry(pair.Value)))
                .Select(pair => $"{NodeCatalog.Title(fixture.Configuration.Graph.Nodes.Single(node => node.Id == pair.Key).Kind)}: "
                    + $"{baseline[pair.Key].Size} -> {pair.Value.RenderSize}").ToArray();
            require(changed.Length == 0, reason + (changed.Length > 0 ? " Changed: " + string.Join("; ", changed) : ""));
        }
        foreach (var node in fixture.Configuration.Graph.Nodes)
        {
            var view = views[node.Id];
            require(view.HeaderBrushKey == NodeCatalog.HeaderBrushKey(node.Kind)
                && Descendants<Border>(view).Any(border => ReferenceEquals(border.Background, NodeCatalog.HeaderBrush(node.Kind))),
                NodeCatalog.Title(node.Kind) + " uses its creation-menu category colour on the actual node header.");
            var buttons = Descendants<Button>(view).ToArray();
            bool deviceNode = node.Kind is "microphone" or "virtualInput" or "appInput" or "output" or "virtualOutput";
            require(buttons.Count(button => AutomationProperties.GetName(button) == "Mute") == (deviceNode ? 1 : 0)
                && buttons.Count(button => AutomationProperties.GetName(button) == "Bypass") == (deviceNode ? 0 : 1)
                && !buttons.Any(button => AutomationProperties.GetName(button) == "Effect"),
                NodeCatalog.Title(node.Kind) + (deviceNode ? " has only Mute for its signal state." : " has only Bypass for its signal state, without a duplicate mute or effect button."));
            var levelDials = Descendants<RotaryValueControl>(view).Where(dial =>
                Descendants<Slider>(dial).Any(slider => AutomationProperties.GetName(slider) == "Level")).ToArray();
            require(levelDials.Length == (NodeCatalog.HasLevelControl(node.Kind) ? 1 : 0),
                NodeCatalog.Title(node.Kind) + " exposes one rotary Level control only when volume is part of that node's purpose, with no remaining Level slider row.");
            if (node.Kind == "eq")
                require(Descendants<RotaryValueControl>(view).Count() == 3
                    && Descendants<RotaryValueControl>(view).SelectMany(dial => Descendants<Slider>(dial))
                        .Select(AutomationProperties.GetName).SequenceEqual(["Frequency", "Band gain", "Width / Q"]),
                    "The equalizer has exactly three rotary band controls under its plot, with no duplicate numeric slider rows.");
        }
        foreach (string state in new[] { "Waiting", "AccessDenied", "Running" })
        {
            foreach (var node in fixture.Configuration.Graph.Nodes)
                views[node.Id].Apply(node, Status(node.Id, state, clipped: state == "Running"), selected: true);
            await layout(); Stable("Selection and " + state + " telemetry preserve every node's world size and socket positions.");
        }
        foreach (var original in fixture.Configuration.Graph.Nodes)
        {
            var changed = original.Copy(); changed.Name = new string('W', 80); changed.GainDb = 12;
            changed.Muted = true; changed.Bypass = true; changed.Mono = true; changed.Balance = -1;
            changed.Compressor.ThresholdDb = -60; changed.Compressor.Ratio = 20;
            changed.Compressor.AttackMs = 200; changed.Compressor.ReleaseMs = 2000; changed.Compressor.MakeupDb = 24;
            foreach (var band in changed.EqBands)
            { band.FrequencyHz = 20000; band.Q = 20; band.GainDb = changed.Kind == "tone" ? 12 : changed.Kind is "highPass" or "lowPass" ? 0 : 24; }
            views[original.Id].Apply(changed, Status(original.Id, "Running"), selected: false);
        }
        await layout(); Stable("Long names, parameter extremes, mute, bypass and Mono change controls without resizing nodes or moving sockets.");
        var eq = fixture.Configuration.Graph.Nodes.Single(node => node.Kind == "eq").Copy(); eq.EqBands.Clear();
        views[eq.Id].Apply(eq, Status(eq.Id, "Running"), false); await layout();
        Stable("An empty EQ keeps the plot and dial-row allocation of a populated EQ.");

        var source = fixture.Configuration.Graph.Nodes[0]; var sourceView = views[source.Id];
        workspace.BeginNodeMove(source.Id, new(100, 100)); workspace.UpdateNodeMove(new(268, 220));
        await layout(); Stable("Moving a node changes its position without changing its dimensions or relative socket geometry.");
        workspace.FinishNodeMove(cancel: true); await layout(); Stable("Cancelling a node move restores position without changing presentation size.");

        var title = Descendants<TextBlock>(sourceView).First(text => text.Text == source.Name);
        var titleCursor = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent };
        title.RaiseEvent(titleCursor);
        require(titleCursor.Handled && titleCursor.Cursor == Cursors.Arrow && sourceView.CursorForSurface(sourceView) == Cursors.Arrow,
            "The real cursor event keeps an ordinary arrow over idle node titles and unused node body surfaces.");
        workspace.BeginNodeMove(source.Id, new(100, 100));
        var draggingCursor = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent };
        title.RaiseEvent(draggingCursor);
        require(draggingCursor.Handled && draggingCursor.Cursor == Cursors.SizeAll,
            "The four-way move cursor appears only while a node is actively held for dragging.");
        workspace.FinishNodeMove(cancel: true);
        require(sourceView.CursorForSurface(sourceView) == Cursors.Arrow,
            "Releasing or cancelling a node drag restores the ordinary arrow.");
        var entry = Descendants<TextBox>(sourceView).First(text => text.Visibility == Visibility.Visible);
        var entryCursor = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent };
        entry.RaiseEvent(entryCursor);
        require(entryCursor.Cursor == Cursors.IBeam && sourceView.CursorForSurface(entry) == Cursors.IBeam
            && !sourceView.IsNodeDragSurface(entry), "Actual editable text keeps its I-beam cursor and cannot initiate a node drag.");
        require(Descendants<Slider>(sourceView).All(slider => sourceView.CursorForSurface(slider) is null)
            && Descendants<RotaryValueControl>(sourceView).All(dial => sourceView.CursorForSurface(dial) is null)
            && Descendants<Button>(sourceView).All(button => sourceView.CursorForSurface(button) is null),
            "Node grab cursor handling leaves rotary, slider, button and socket cursors to those interactive controls.");

        var choices = UiPreview.CreateSnapshot([]);
        var microphone = GraphDefaults.NewNode("microphone", 20, 20);
        microphone.Device = new() { Id = "microphone-id", StableId = "old-stable-id", Name = "Test microphone", Channel = -1 };
        var application = GraphDefaults.NewNode("appInput", 420, 20);
        application.App = new() { ProcessId = 12, ExecutablePath = @"C:\test\player.exe", Name = "Player" };
        choices.Configuration.Graph = new() { Nodes = [microphone, application] };
        choices.Endpoints = [new() { Id = microphone.Device.Id, StableId = microphone.Device.StableId, Name = microphone.Device.Name,
            Direction = "capture", State = "Active", Channels = 2, Rate = 48000 }];
        choices.Applications = [new() { ProcessId = 12, ExecutablePath = application.App.ExecutablePath, Name = "Player" }];
        mixer.ApplyPreviewSnapshot(choices); await layout();
        var choiceViews = Descendants<GraphNodeView>(workspace).ToDictionary(view => view.Id);
        var microphoneView = choiceViews[microphone.Id]; var applicationView = choiceViews[application.Id];
        int endpointBuilds = microphoneView.DeviceChoiceBuildCount, appBuilds = applicationView.AppChoiceBuildCount;
        for (int i = 0; i < 100; i++) mixer.RefreshPreviewTelemetry();
        require(microphoneView.DeviceChoiceBuildCount == endpointBuilds && applicationView.AppChoiceBuildCount == appBuilds,
            "One hundred meter refreshes reuse endpoint/app choices instead of rebuilding inventory lists or selection bindings.");
        choices.Endpoints = [new() { Id = microphone.Device.Id, StableId = "updated-stable-id", Name = microphone.Device.Name,
            Direction = "capture", State = "Active", Channels = 2, Rate = 48000 }];
        mixer.ApplyPreviewSnapshot(choices); await layout();
        var microphoneChoice = Descendants<ComboBox>(microphoneView).Single().SelectedItem as EndpointChoice;
        require(microphoneChoice?.StableId == "updated-stable-id" && microphoneView.DeviceChoiceBuildCount == endpointBuilds + 1,
            "Changing only endpoint stable identity refreshes the chooser metadata rather than retaining its stale persisted binding.");
        choices.Applications = [new() { ProcessId = 12, ExecutablePath = application.App.ExecutablePath, Name = "Renamed player" }];
        mixer.ApplyPreviewSnapshot(choices); await layout();
        require((Descendants<ComboBox>(applicationView).Single().SelectedItem as AudioApplication)?.Name == "Renamed player"
            && applicationView.AppChoiceBuildCount == appBuilds + 1,
            "Application name changes rebuild their chooser once while preserving the exact process/path selection.");
        choices.Applications = [];
        mixer.ApplyPreviewSnapshot(choices); await layout();
        workspace.NodeChanged(application.Id, node => node.App.Name = "Saved application name");
        require((Descendants<ComboBox>(applicationView).Single().SelectedItem as AudioApplication)?.Name == "Saved application name · saved selection",
            "Renaming an application absent from the picker updates its saved placeholder without claiming capture has stopped.");
    }

    private sealed record NodeGeometry(Size Size, IReadOnlyList<Point> Sockets);
    private static NodeGeometry Geometry(GraphNodeView view) => new(view.RenderSize,
        Descendants<GraphWorkspace.Socket>(view).Select(socket => socket.TranslatePoint(new(socket.ActualWidth / 2, socket.ActualHeight / 2), view)).ToArray());
    private static bool Same(NodeGeometry first, NodeGeometry second) => Math.Abs(first.Size.Width - second.Size.Width) < .001
        && Math.Abs(first.Size.Height - second.Size.Height) < .001 && first.Sockets.Count == second.Sockets.Count
        && first.Sockets.Zip(second.Sockets).All(pair => (pair.First - pair.Second).Length < .001);
    private static GraphNodeStatus Status(string id, string state, bool clipped = false) => new()
    { Id = id, Channels = 2, PeakLeft = .8f, PeakRight = .2f, Clipped = clipped, Path = new() { State = state } };
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
