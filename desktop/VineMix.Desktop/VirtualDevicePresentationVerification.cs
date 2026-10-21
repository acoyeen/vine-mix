using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace VineMix.Desktop;

// Detached UI checks; never opens a production pipe, audio stream or native window.
internal static class VirtualDevicePresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Virtual device presentation checks use a detached preview without live audio.");
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new(); fixture.Status.Nodes = [];
        mixer.ApplyPreviewSnapshot(fixture);
        string inputId = workspace.Spawn("virtualInput", new(32, 32));
        string outputId = workspace.Spawn("virtualOutput", new(440, 32));
        string micId = workspace.Spawn("microphone", new(32, 450));
        string speakerId = workspace.Spawn("output", new(440, 450));
        fixture.Configuration.Graph = mixer.GraphConfig.Copy();
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(n => new GraphNodeStatus
        { Id = n.Id, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        var views = Descendants<GraphNodeView>(workspace).ToDictionary(v => v.Id);
        var sizes = views.ToDictionary(v => v.Key, v => v.Value.RenderSize);
        TextBlock Name(string id) => Descendants<TextBlock>(views[id]).Single(t => AutomationProperties.GetName(t) == "Virtual audio device");
        foreach (string id in new[] { inputId, outputId })
        {
            var node = mixer.GraphConfig.Nodes.Single(n => n.Id == id);
            require(!Descendants<ComboBox>(views[id]).Any() && Name(id).Text == node.Device.Name,
                node.Kind + " shows its automatically bound device name without a dropdown.");
            require(Descendants<TextBlock>(views[id]).Any(t => t.Visibility == Visibility.Visible
                && t.Text == (node.Muted ? "Connected · muted" : "Connected")),
                node.Kind + " keeps connection status visible, including when muted.");
            render(views[id], node.Kind + "-fixed-device.png");
        }
        require(Descendants<ComboBox>(views[micId]).Count() == 1 && Descendants<ComboBox>(views[speakerId]).Count() == 1,
            "Physical microphones and listening outputs retain their device dropdowns.");

        var savedInput = mixer.GraphConfig.Nodes.Single(n => n.Id == inputId).Copy();
        fixture.Endpoints = fixture.Endpoints.Where(e => !VirtualDeviceBindings.Same(savedInput.Device, e)).ToList();
        fixture.Status.Nodes.Single(n => n.Id == inputId).Path.State = "Waiting";
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        require(Name(inputId).Text == savedInput.Device.Name && mixer.GraphConfig.Nodes.Single(n => n.Id == inputId).Device.Id == savedInput.Device.Id,
            "A missing virtual device retains its saved name and identity.");
        require(views[inputId].AvailabilityNoticeLabel == "INPUT MISSING" && views[inputId].AvailabilityWarningVisible
            && !views[inputId].AvailabilityNoticeVisible && views[inputId].AvailabilityDeviceTextElement == Name(inputId)
            && views[inputId].AvailabilityNoticeDetailTextElement.Visibility == Visibility.Hidden && views[inputId].RenderSize == sizes[inputId],
            "The missing virtual input highlights its saved device name with a subtle glow, without extra text or resizing.");

        var replacement = new Endpoint { Id = "replacement-cable", StableId = "replacement-stable", Name = "Replacement cable",
            Direction = "capture", Transport = "vbCable", IsVirtual = true, State = "Active", Channels = 2 };
        fixture.Endpoints.Add(replacement);
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        require(Name(inputId).Text == savedInput.Device.Name,
            "A newly detected replacement never silently displaces a saved virtual assignment.");
        MenuItem DeviceMenu(string id)
        {
            var menu = new ContextMenu(); workspace.PopulateNodeMenu(menu, id, () => { });
            return menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Troubleshooting")
                .Items.OfType<MenuItem>().Single(i => (string?)i.Header == "Reassign device");
        }
        var devices = DeviceMenu(inputId);
        require(devices.Items.OfType<MenuItem>().Count() == 1,
            "Virtual-input troubleshooting lists only VB-CABLE recording endpoints.");
        devices.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await layout();
        var reassigned = mixer.GraphConfig.Nodes.Single(n => n.Id == inputId);
        require(reassigned.Device.Id == replacement.Id && reassigned.Device.StableId == replacement.StableId
            && reassigned.Muted == savedInput.Muted && reassigned.GainDb == savedInput.GainDb
            && Name(inputId).Text == replacement.Name && views[inputId].RenderSize == sizes[inputId],
            "Troubleshooting reassignment updates the fixed name while preserving controls and node dimensions.");
        require(DeviceMenu(inputId).Items.OfType<MenuItem>().Single().IsChecked,
            "Troubleshooting marks the currently assigned virtual endpoint.");

        string duplicateId = workspace.Spawn("virtualInput", new(840, 32)); await layout();
        require(mixer.GraphConfig.Nodes.Single(n => n.Id == duplicateId).Device.Id == replacement.Id
            && DeviceMenu(duplicateId).Items.OfType<MenuItem>().Single() is { IsEnabled: true, IsChecked: true },
            "A second virtual input shares the detected source and keeps its troubleshooting choice available.");
        workspace.NodeChanged(duplicateId, n => { n.GainDb = -12; n.Muted = true; n.Device.Channel = 0; }); await layout();
        require(mixer.GraphConfig.Nodes.Single(n => n.Id == inputId) is { GainDb: 0, Muted: false, Device.Channel: -1 }
            && mixer.GraphConfig.Nodes.Single(n => n.Id == duplicateId) is { GainDb: -12, Muted: true, Device.Channel: 0 },
            "Shared virtual input controls remain independent in the workspace.");
        string duplicateOutputId = workspace.Spawn("virtualOutput", new(840, 450)); await layout();
        require(!VirtualDeviceBindings.IsAssigned(mixer.GraphConfig.Nodes.Single(n => n.Id == duplicateOutputId).Device)
            && DeviceMenu(duplicateOutputId).Items.OfType<MenuItem>().All(i => !i.IsEnabled),
            "A second virtual output still cannot claim a render endpoint already owned by another output.");
        var physicalMenu = new ContextMenu(); workspace.PopulateNodeMenu(physicalMenu, micId, () => { });
        require(!physicalMenu.Items.OfType<MenuItem>().Any(i => (string?)i.Header == "Troubleshooting"),
            "The virtual-device troubleshooting menu does not replace physical device selection.");

        await CheckSharedSourcePickersAsync(workspace, mixer, layout, require);

        fixture = UiPreview.CreateSnapshot([]); fixture.Endpoints = [];
        fixture.Configuration.Graph = new() { Nodes = [GraphDefaults.NewNode("virtualInput", 32, 32), GraphDefaults.NewNode("virtualOutput", 440, 32)] };
        fixture.Status.Nodes = [];
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        foreach (var view in Descendants<GraphNodeView>(workspace))
        {
            var node = mixer.GraphConfig.Nodes.Single(n => n.Id == view.Id);
            require(!view.AvailabilityNoticeVisible && view.AvailabilityWarningVisible
                && view.AvailabilityNoticeLabel == (node.Kind == "virtualInput" ? "VB-CABLE NOT DETECTED" : "VAC LITE NOT DETECTED")
                && view.AvailabilityDeviceTextElement is not null && view.AvailabilityNoticeDetailTextElement.Visibility == Visibility.Hidden,
                node.Kind + " retains its device name and matching unavailable-transport warning.");
        }
    }
    private static async Task CheckSharedSourcePickersAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require)
    {
        var fixture = UiPreview.CreateSnapshot([]); fixture.Configuration.Graph = new(); fixture.Status.Nodes = [];
        mixer.ApplyPreviewSnapshot(fixture);
        string mic1 = workspace.Spawn("microphone", new(32, 32)), mic2 = workspace.Spawn("microphone", new(440, 32));
        string app1 = workspace.Spawn("appInput", new(32, 450)), app2 = workspace.Spawn("appInput", new(440, 450));
        string output1 = workspace.Spawn("output", new(840, 32)), output2 = workspace.Spawn("output", new(840, 450));
        await layout();
        ComboBox Picker(string id) => Descendants<ComboBox>(Descendants<GraphNodeView>(workspace).Single(v => v.Id == id)).Single();
        void ChooseDevice(string id, string endpointId)
        {
            var picker = Picker(id); picker.SelectedItem = picker.Items.OfType<EndpointChoice>().Single(e => e.Id == endpointId);
        }
        string capture = fixture.Endpoints.Single(e => e.Direction == "capture" && !e.IsVirtual).Id;
        string render = fixture.Endpoints.Single(e => e.Direction == "render" && !e.IsVirtual).Id;
        ChooseDevice(mic1, capture); await layout();
        ChooseDevice(mic2, capture); await layout();
        require(mixer.GraphConfig.Nodes.Where(n => n.Kind == "microphone").All(n => n.Device.Id == capture),
            "The physical microphone pickers accept the same capture endpoint on two independent inputs.");
        ChooseDevice(output1, render); await layout();
        ChooseDevice(output2, render); await layout();
        require(mixer.GraphConfig.Nodes.Single(n => n.Id == output1).Device.Id == render
            && !VirtualDeviceBindings.IsAssigned(mixer.GraphConfig.Nodes.Single(n => n.Id == output2).Device)
            && Picker(output2).SelectedItem is EndpointChoice { Id: "", StableId: "" },
            "The physical output picker still refuses a duplicate render endpoint and restores its unassigned selection.");
        var app = fixture.Applications.First();
        foreach (string id in new[] { app1, app2 })
        {
            var picker = Picker(id); picker.SelectedItem = picker.Items.OfType<AudioApplication>().Single(a => a.ProcessId == app.ProcessId);
            await layout();
        }
        require(mixer.GraphConfig.Nodes.Where(n => n.Kind == "appInput")
            .All(n => n.App.ProcessId == app.ProcessId && n.App.ExecutablePath == app.ExecutablePath),
            "The running-app pickers accept the same process and executable on two independent inputs.");
        workspace.NodeChanged(mic2, n => { n.GainDb = -15; n.Muted = true; n.Mono = true; });
        workspace.NodeChanged(app2, n => { n.GainDb = -9; n.Muted = true; }); await layout();
        require(mixer.GraphConfig.Nodes.Single(n => n.Id == mic1) is { GainDb: 0, Muted: false, Mono: false }
            && mixer.GraphConfig.Nodes.Single(n => n.Id == app1) is { GainDb: 0, Muted: false },
            "Changing the level, mute and Mono controls of shared inputs leaves their sibling inputs unchanged.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
