using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Catalog fixtures and detached real selectors: no window, pipe, real endpoint or personal preferences.</summary>
internal static class DeviceChoicesVerification
{
    internal static FrameworkElement Run(Action<bool, string> require)
    {
        Endpoint Device(string id, string direction = "capture", string state = "Active", int form = 4)
            => new() { Id = id, StableId = "stable-" + id, Name = id, Direction = direction,
                State = state, FormFactor = form, Channels = 2, Rate = 48000 };
        var inventory = new List<Endpoint>
        {
            Device("Microphone"), Device("Disabled mic", state: "Disabled"), Device("Unplugged mic", state: "Unplugged"),
            Device("Missing mic", state: "Missing"), Device("Line input", form: 2), Device("Unknown", form: 10),
            Device("Old engine", form: -1), Device("Headphones", "render", form: 3), Device("Speakers", "render", form: 1),
            Device("Display audio", "render", form: 9), Device("S/PDIF", "render", form: 8), Device("Passthrough", "render", form: 7),
            Device("Disabled line", state: "Disabled", form: 2)
        };
        var cable = Device("Virtual cable"); cable.Transport = "vbCable"; cable.IsVirtual = true; inventory.Add(cable);
        var unsupported = Device("Unsupported transport"); unsupported.Transport = "otherVirtual"; inventory.Add(unsupported);
        var defaults = DeviceChoices.Build(inventory, "capture", new(), default);
        require(defaults.Where(choice => choice.Id.Length > 0).Select(choice => choice.Id).Order().SequenceEqual(new[] { "Microphone", "Old engine", "Unknown" }.Order()),
            "Default input choices hide unavailable and known advanced endpoints, retaining unknown active devices.");
        var outputs = DeviceChoices.Build(inventory, "render", new(), default);
        require(outputs.Where(choice => choice.Id.Length > 0).Select(choice => choice.Id).Order().SequenceEqual(new[] { "Display audio", "Headphones", "Speakers" }.Order()),
            "HDMI/display audio, speakers and headphones remain ordinary output choices while S/PDIF and passthrough are advanced.");
        foreach (bool disabled in new[] { false, true })
        foreach (bool disconnected in new[] { false, true })
        foreach (bool advanced in new[] { false, true })
        {
            var choices = DeviceChoices.Build(inventory, "capture", new(), new(disabled, disconnected, advanced));
            bool Has(string id) => choices.Any(choice => choice.Id == id);
            require(Has("Disabled mic") == disabled && Has("Missing mic") == disconnected && Has("Unplugged mic") == disconnected
                && Has("Line input") == advanced && Has("Disabled line") == (disabled && advanced),
                $"Visibility flags combine independently for Disabled={disabled}, Disconnected={disconnected}, Advanced={advanced}.");
            require(!Has(cable.Id) && !Has(unsupported.Id) && !Has("Headphones")
                && choices.Where(choice => choice.State is "Disabled" or "Missing" or "Unplugged").All(choice => !choice.IsSelectable),
                "Every visibility combination respects direction/transport and exposes unavailable devices as informational entries.");
        }
        foreach (string id in new[] { "Disabled mic", "Unplugged mic", "Missing mic", "Line input", cable.Id, unsupported.Id, "Absent saved mic" })
        {
            var binding = new DeviceBinding { Id = id, Name = id, Channel = -1 };
            var choices = DeviceChoices.Build(inventory, "capture", binding, default);
            var selected = DeviceChoices.Selected(choices, binding);
            require(selected?.Id == id && choices.Count(choice => choice.IsCurrent) == 1,
                "A saved " + id + " remains selected independently of visibility and compatibility.");
        }
        var exact = Device("exact", state: "Disabled"); exact.StableId = "shared";
        var replacement = Device("replacement"); replacement.StableId = "shared";
        var saved = new DeviceBinding { Id = exact.Id, StableId = "shared", Name = "Saved" };
        require(DeviceChoices.Selected(DeviceChoices.Build([replacement, exact], "capture", saved, default), saved)?.Id == "replacement",
            "A unique active stable identity replaces an unavailable exact identity in presentation, matching the engine resolver.");
        require(DeviceChoices.Selected(DeviceChoices.Build([exact], "capture", saved, default), saved)?.Id == "exact",
            "An unavailable exact saved identity remains visible when no active stable replacement exists.");
        saved.Id = "gone";
        require(DeviceChoices.Selected(DeviceChoices.Build([replacement], "capture", saved, default), saved)?.Id == "replacement",
            "A unique active stable identity restores presentation without modifying the saved binding.");
        var ambiguous = Device("ambiguous"); ambiguous.StableId = "shared";
        saved.Id = "";
        var uncertain = DeviceChoices.Selected(DeviceChoices.Build([replacement, ambiguous], "capture", saved, default), saved);
        require(uncertain is { Id: "", StableId: "shared", State: "Missing", IsCurrent: true },
            "Ambiguous stable-only identities retain a saved missing placeholder rather than selecting an arbitrary device.");
        require(saved.Id == "" && saved.StableId == "shared" && saved.Name == "Saved",
            "Filtering and identity resolution never rewrite a persisted device binding.");
        var stale = new EndpointChoice { Id = "Microphone", Name = "Microphone", State = "Active" };
        require(DeviceChoices.CanAssign(stale, inventory, "capture")
            && !DeviceChoices.CanAssign(stale, [Device("Microphone", state: "Missing")], "capture")
            && !DeviceChoices.CanAssign(stale, [Device("Microphone", "render")], "capture")
            && !DeviceChoices.CanAssign(stale, [], "capture") && DeviceChoices.CanAssign(new(), [], "capture"),
            "Selection validates the latest inventory so a frozen open list cannot assign a disappeared, disabled or incompatible endpoint.");
        replacement.Name = ambiguous.Name = "Identical name"; replacement.InterfaceName = "USB interface"; ambiguous.InterfaceName = "PCI interface";
        var duplicates = DeviceChoices.Build([replacement, ambiguous], "capture", new(), default).Where(choice => choice.Id.Length > 0).ToArray();
        require(duplicates.Length == 2 && duplicates.Select(choice => choice.Label).Distinct().Count() == 2
            && duplicates.All(choice => choice.Name == "Identical name") && duplicates.Any(choice => choice.Label.Contains("USB interface")),
            "Duplicate names retain separate identities and meaningful display details without changing saved names.");
        var empty = DeviceChoices.Build([], "render", new(), default);
        require(empty.Count == 2 && empty[0].IsSelectable && !empty[1].IsSelectable
            && empty[1].Label.Contains("Settings") && empty[1].Help.Contains("Audio device visibility"),
            "An empty selector retains Not assigned and explains the route to visibility settings.");
        CheckRealSelector(inventory, require);
        return Preview(inventory);
    }

    private static void CheckRealSelector(List<Endpoint> inventory, Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true);
        var node = GraphDefaults.NewNode("microphone", 20, 20); node.Device = new() { Id = "Microphone", Name = "Microphone", Channel = -1 };
        var snapshot = UiPreview.CreateSnapshot([]); snapshot.Configuration.Graph = new() { Nodes = [node] }; snapshot.Endpoints = inventory;
        mixer.ApplyPreviewSnapshot(snapshot);
        var workspace = new GraphWorkspace(mixer);
        workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, workspace));
        workspace.Measure(new Size(1280, 800)); workspace.Arrange(new Rect(0, 0, 1280, 800)); workspace.UpdateLayout();
        try
        {
            var view = Descendants<GraphNodeView>(workspace).Single();
            var picker = Descendants<ComboBox>(view).Single();
            string before = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
            int builds = view.DeviceChoiceBuildCount;
            mixer.ShowDisabledAudioDevices = true;
            require(picker.Items.OfType<EndpointChoice>().Any(choice => choice.Id == "Disabled mic")
                && view.DeviceChoiceBuildCount == builds + 1 && !picker.IsDropDownOpen,
                "A settings change refreshes the real closed selector immediately without opening a native popup.");
            var disabled = picker.Items.OfType<EndpointChoice>().Single(choice => choice.Id == "Disabled mic");
            require(disabled.Help.Contains("Windows") && picker.ItemContainerStyle.Setters.OfType<Setter>()
                .Any(setter => setter.Property == UIElement.IsEnabledProperty && setter.Value is Binding { Path.Path: nameof(EndpointChoice.IsSelectable) }),
                "The real picker binds unavailable entries to disabled item containers with Windows recovery guidance.");
            picker.SelectedItem = disabled;
            require(mixer.GraphConfig.Nodes.Single().Device.Id == "Microphone" && picker.SelectedItem is EndpointChoice { Id: "Microphone" },
                "Even a programmatic selection event cannot assign an unavailable informational device.");
            mixer.ShowAdvancedAudioDevices = true;
            require(picker.Items.OfType<EndpointChoice>().Any(choice => choice.Id == "Line input")
                && picker.Items.OfType<EndpointChoice>().All(choice => choice.Id != "Virtual cable"),
                "Advanced settings reveal supported line capture in the real selector without exposing virtual transports.");
            require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == before && !mixer.CanUndo && mixer.PendingEditCount == 0,
                "Visibility and informational choices leave the graph, Undo stack and engine edit queue unchanged.");
            builds = view.DeviceChoiceBuildCount;
            for (int i = 0; i < 50; ++i) mixer.RefreshPreviewTelemetry();
            require(view.DeviceChoiceBuildCount == builds,
                "Fifty telemetry refreshes reuse the filtered inventory and item bindings.");
        }
        finally { workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, workspace)); }
    }

    private static FrameworkElement Preview(List<Endpoint> inventory)
    {
        var panel = new StackPanel { Width = 540, Height = 180, Margin = new Thickness(16), Background = ConsoleTheme.Get("ConsolePanelBrush") };
        panel.Children.Add(GraphWorkspace.Text("Default input choices", 14));
        panel.Children.Add(new ComboBox { ItemsSource = DeviceChoices.Build(inventory, "capture", new(), default), DisplayMemberPath = "Label", SelectedIndex = 0 });
        var outputLabel = GraphWorkspace.Text("Default output choices", 14); outputLabel.Margin = new Thickness(0, 20, 0, 0); panel.Children.Add(outputLabel);
        panel.Children.Add(new ComboBox { ItemsSource = DeviceChoices.Build(inventory, "render", new(), default), DisplayMemberPath = "Label", SelectedIndex = 0 });
        return panel;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            foreach (var descendant in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return descendant;
    }
}
