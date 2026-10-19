using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached visibility preferences, menu bindings and JSON persistence; no personal settings or engine I/O.</summary>
internal static class DeviceVisibilitySettingsVerification
{
    internal static void Run(Action<bool, string> require)
    {
        ConsoleTheme.Initialize();
        VerifyOfflineMenu(require);
        VerifyPersistence(require);
        VerifyGraphIsolation(require);
        VerifyMetadataRefresh(require);
    }

    private static void VerifyOfflineMenu(Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true);
        require(!mixer.CanEdit && !mixer.ShowDisabledAudioDevices && !mixer.ShowDisconnectedAudioDevices && !mixer.ShowAdvancedAudioDevices,
            "Device visibility defaults hide unavailable and advanced endpoints even before the engine connects.");
        var menu = MainWindow.CreateSettingsMenu(mixer);
        var visibility = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Audio device visibility"));
        require(visibility.Items.Count == 3 && !menu.IsOpen && PresentationSource.FromVisual(menu) is null,
            "Settings exposes the three audio visibility preferences in a detached submenu without opening a popup or native window.");
        string before = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        int graphChanges = 0;
        mixer.GraphChanged += (_, _) => graphChanges++;
        foreach (var preference in new (string Header, string Property, Func<bool> Read)[]
        {
            ("Show disabled devices", nameof(MixerViewModel.ShowDisabledAudioDevices), () => mixer.ShowDisabledAudioDevices),
            ("Show disconnected or missing devices", nameof(MixerViewModel.ShowDisconnectedAudioDevices), () => mixer.ShowDisconnectedAudioDevices),
            ("Show advanced inputs and outputs", nameof(MixerViewModel.ShowAdvancedAudioDevices), () => mixer.ShowAdvancedAudioDevices)
        })
        {
            var toggle = visibility.Items.OfType<MenuItem>().Single(item => Equals(item.Header, preference.Header));
            var binding = toggle.GetBindingExpression(MenuItem.IsCheckedProperty);
            binding?.UpdateTarget();
            require(toggle.IsEnabled && toggle.IsCheckable && toggle.StaysOpenOnClick && toggle.IsChecked == false
                && binding is not null && binding.ParentBinding.Path.Path == preference.Property
                && binding.ParentBinding.Mode == BindingMode.TwoWay && ReferenceEquals(binding.ParentBinding.Source, mixer)
                && !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(toggle)),
                preference.Header + " stays usable offline, has accessible help and uses the existing two-way checkable Settings style.");
            int changesBefore = graphChanges;
            toggle.SetCurrentValue(MenuItem.IsCheckedProperty, true);
            binding!.UpdateSource();
            require(preference.Read() && graphChanges == changesBefore + 1,
                preference.Header + " changes its preference and immediately refreshes device lists once.");
            binding.UpdateSource();
            require(graphChanges == changesBefore + 1,
                "Reapplying an unchanged " + preference.Header + " value does not churn the dropdowns.");
        }
        require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == before
            && mixer.PendingEditCount == 0 && mixer.EditHistory.Entries.Count == 0 && !mixer.CanUndo,
            "Changing all visibility preferences offline does not change routing, queue engine configuration or add Undo history.");
    }

    private static void VerifyPersistence(Action<bool, string> require)
    {
        for (int flags = 0; flags < 8; flags++)
        {
            using var source = new MixerViewModel(preview: true)
            {
                ReducedAnimation = true, RightClickDragToPan = true,
                ShowDisabledAudioDevices = (flags & 1) != 0,
                ShowDisconnectedAudioDevices = (flags & 2) != 0,
                ShowAdvancedAudioDevices = (flags & 4) != 0
            };
            using var restored = new MixerViewModel(preview: true);
            restored.LoadUiPreferencesJson(source.SerializeUiPreferences());
            require(restored.ReducedAnimation && restored.RightClickDragToPan
                && restored.ShowDisabledAudioDevices == source.ShowDisabledAudioDevices
                && restored.ShowDisconnectedAudioDevices == source.ShowDisconnectedAudioDevices
                && restored.ShowAdvancedAudioDevices == source.ShowAdvancedAudioDevices,
                "UI preferences preserve navigation and animation alongside independent device visibility combination " + flags + " through the real JSON format.");
        }
        using var legacy = new MixerViewModel(preview: true)
        {
            ShowDisabledAudioDevices = true, ShowDisconnectedAudioDevices = true, ShowAdvancedAudioDevices = true
        };
        legacy.LoadUiPreferencesJson("{\"reducedAnimation\":true,\"rightClickDragToPan\":true}");
        require(legacy.ReducedAnimation && legacy.RightClickDragToPan && !legacy.ShowDisabledAudioDevices
            && !legacy.ShowDisconnectedAudioDevices && !legacy.ShowAdvancedAudioDevices,
            "Existing ui.json files retain their saved preferences while absent visibility fields safely use clean defaults.");
        legacy.ReducedAnimation = false;
        legacy.LoadUiPreferencesJson("{}");
        require(!legacy.ReducedAnimation && !legacy.RightClickDragToPan && !legacy.ShowDisabledAudioDevices
            && !legacy.ShowDisconnectedAudioDevices && !legacy.ShowAdvancedAudioDevices,
            "Missing animation preferences retain the current Windows-derived fallback; missing visibility preferences stay off.");
    }

    private static void VerifyGraphIsolation(Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
        var gain = GraphDefaults.NewNode("gain", 120, 80);
        gain.Id = "visibility-settings-gain";
        var configuration = new MixerConfiguration { Version = 2, Graph = new() { Nodes = [gain] } };
        bool sawApplying = false;
        mixer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MixerViewModel.StartAtLogin) || !mixer.Applying) return;
            sawApplying = true;
            mixer.ShowDisabledAudioDevices = true;
            mixer.ShowDisconnectedAudioDevices = true;
            mixer.ShowAdvancedAudioDevices = true;
        };
        configuration.StartAtLogin = true;
        mixer.ApplyPreviewSnapshot(new() { Configuration = configuration });
        require(sawApplying && !mixer.ShowDisabledAudioDevices && !mixer.ShowDisconnectedAudioDevices && !mixer.ShowAdvancedAudioDevices,
            "Snapshot application guards visibility setters against reentrant preference changes.");
        mixer.Edit(c => c.Graph.Nodes.Single().GainDb = -9);
        string before = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        int history = mixer.EditHistory.Entries.Count, pending = mixer.PendingEditCount;
        long presentations = mixer.ConfigurationPresentationCount;
        mixer.ShowDisabledAudioDevices = true;
        mixer.ShowDisconnectedAudioDevices = true;
        mixer.ShowAdvancedAudioDevices = true;
        require(mixer.CanEdit && mixer.CanUndo && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == before
            && history > 0 && mixer.EditHistory.Entries.Count == history && pending > 0 && mixer.PendingEditCount == pending
            && mixer.ConfigurationPresentationCount == presentations,
            "Visibility changes preserve a live graph edit, pending configuration and history without presenting a new engine configuration.");
    }

    private static void VerifyMetadataRefresh(Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true);
        Endpoint Endpoint() => new()
        {
            Id = "visibility-metadata", StableId = "stable-visibility-metadata", Name = "Audio endpoint", State = "Active",
            Direction = "capture", Description = "Recording endpoint", InterfaceName = "Audio interface", FormFactor = 4
        };
        void Apply(Endpoint endpoint) => mixer.ApplyPreviewSnapshot(new() { Configuration = new() { Version = 2 }, Endpoints = [endpoint] });
        Apply(Endpoint());
        var inventory = mixer.Endpoints;
        Apply(Endpoint());
        require(ReferenceEquals(inventory, mixer.Endpoints),
            "Unchanged endpoint metadata keeps the inventory identity stable for dropdown caching.");
        foreach (var change in new (string Label, Action<Endpoint> Apply)[]
        {
            ("description", endpoint => endpoint.Description = "Changed recording endpoint"),
            ("interface name", endpoint => endpoint.InterfaceName = "Changed audio interface"),
            ("form factor", endpoint => endpoint.FormFactor = 0)
        })
        {
            Apply(Endpoint());
            inventory = mixer.Endpoints;
            var next = Endpoint();
            change.Apply(next);
            Apply(next);
            require(!ReferenceEquals(inventory, mixer.Endpoints) && ReferenceEquals(next, mixer.Endpoints.Single()),
                "A change only to endpoint " + change.Label + " refreshes the inventory so classification and disambiguation cannot stay stale.");
        }
    }
}
