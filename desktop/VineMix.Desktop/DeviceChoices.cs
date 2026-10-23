using System;
using System.Collections.Generic;
using System.Linq;

namespace VineMix.Desktop;

public sealed class EndpointChoice
{
    public string Id { get; init; } = "";
    public string StableId { get; init; } = "";
    public string Name { get; init; } = "";
    public string State { get; init; } = "";
    public int Channels { get; init; }
    public bool IsSelectable { get; init; } = true;
    public bool IsCurrent { get; init; }
    public string? DisplayName { get; init; }
    public string Help { get; init; } = "Choose the device used by this node.";
    public string Label => DisplayName ?? (Id.Length == 0 && StableId.Length == 0 ? "Not assigned"
        : Name + (State == "Active" ? "" : " · " + State));
    public DeviceBinding Binding(int channel) => new() { Id = Id, StableId = StableId, Name = Name, Channel = channel };
}

internal readonly record struct DeviceVisibility(bool Disabled, bool Disconnected, bool Advanced)
{
    internal static DeviceVisibility For(MixerViewModel mixer)
        => new(mixer.ShowDisabledAudioDevices, mixer.ShowDisconnectedAudioDevices, mixer.ShowAdvancedAudioDevices);
}

/// <summary>Presentation policy only. The full engine catalog and saved bindings remain intact.</summary>
internal static class DeviceChoices
{
    internal static string AdvancedPurpose(Endpoint endpoint) => endpoint.FormFactor switch
    {
        0 => "Network audio endpoint",
        2 when endpoint.Direction == "capture" => "Line input for an external mixer or audio source",
        6 => "Telephone handset audio",
        7 => "Digital audio passthrough",
        8 => "S/PDIF digital audio",
        // Windows 10 = UnknownFormFactor. Missing and unknown metadata must not hide a usable device.
        _ => ""
    };

    internal static bool Compatible(Endpoint endpoint, string direction)
        // Streamed headsets expose ordinary Windows capture/render endpoints.
        // Vendor hints do not describe their ability to record or play sound.
        // Keep known routing cables out of Listening output so VAC's dedicated
        // direct-render path cannot be replaced with shared playback by mistake.
        => endpoint.Direction == direction && (direction == "capture"
            || endpoint.Transport is not ("vbCable" or "vacLite"));

    internal static bool IsAdvanced(Endpoint endpoint)
        // RemoteNetworkDevice describes how audio is reached, not an advanced
        // routing mode. Keep streamed microphones and headphones visible.
        => endpoint.FormFactor != 0 && AdvancedPurpose(endpoint).Length > 0;

    internal static IReadOnlyList<EndpointChoice> Build(IReadOnlyList<Endpoint> inventory, string direction,
        DeviceBinding binding, DeviceVisibility visibility)
    {
        bool assigned = VirtualDeviceBindings.IsAssigned(binding);
        var directional = inventory.Where(endpoint => endpoint.Direction == direction).ToArray();
        // Match the engine's active exact ID / unique active stable ID resolution.
        // An unresolved saved endpoint still stays visible for recovery. Names are never identities.
        var exact = assigned && binding.Id.Length > 0 ? directional.FirstOrDefault(endpoint => endpoint.Id == binding.Id) : null;
        var current = exact?.State == "Active" ? exact : null;
        if (current is null && assigned && binding.StableId.Length > 0)
        {
            var candidates = directional.Where(endpoint => endpoint.State == "Active" && endpoint.StableId == binding.StableId).Take(2).ToArray();
            if (candidates.Length == 1) current = candidates[0];
        }
        current ??= exact;
        bool Visible(Endpoint endpoint)
        {
            if (!Compatible(endpoint, direction)) return false;
            bool available = endpoint.State == "Active" || endpoint.State == "Disabled" && visibility.Disabled
                || (endpoint.State is "Missing" or "Unplugged") && visibility.Disconnected;
            return available && (visibility.Advanced || !IsAdvanced(endpoint));
        }
        var endpoints = directional.Where(endpoint => ReferenceEquals(endpoint, current) || Visible(endpoint))
            .OrderBy(endpoint => endpoint.State != "Active")
            .ThenBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(endpoint => endpoint.Id, StringComparer.Ordinal).ToList();
        if (assigned && current is null)
        {
            current = new Endpoint { Id = binding.Id, StableId = binding.StableId,
                Name = binding.Name.Length > 0 ? binding.Name : "Saved device", Direction = direction,
                State = "Missing", Channels = Math.Max(1, binding.Channel + 1) };
            endpoints.Add(current);
        }

        var choices = new List<EndpointChoice> { new() { Help = "Leave this node without a device assignment." } };
        var repeatedNames = endpoints.GroupBy(endpoint => endpoint.Name, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Count() > 1).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.CurrentCultureIgnoreCase);
        foreach (var endpoint in endpoints)
        {
            string display = endpoint.Name;
            if (repeatedNames.TryGetValue(endpoint.Name, out var duplicates))
            {
                string hardware = endpoint.InterfaceName.Length > 0 ? endpoint.InterfaceName : endpoint.Description;
                if (hardware.Length > 0 && !display.Contains(hardware, StringComparison.CurrentCultureIgnoreCase)) display += " · " + hardware;
                // Identical friendly names may represent genuinely different ports. Keep every identity.
                display += " · Device " + (Array.IndexOf(duplicates, endpoint) + 1);
            }
            bool compatible = Compatible(endpoint, direction);
            string state = endpoint.State == "Active" ? "" : " · " + endpoint.State;
            string help = endpoint.Name;
            if (endpoint.InterfaceName.Length > 0) help += "\n" + endpoint.InterfaceName;
            if (AdvancedPurpose(endpoint) is { Length: > 0 } purpose) help += "\n" + purpose;
            help += !compatible ? "\nThis is a routing cable. Choose a listening device here; use Virtual output for VAC."
                : endpoint.State == "Disabled" ? "\nDisabled in Windows. Enable it in Windows sound settings before use."
                : endpoint.State != "Active" ? "\nUnavailable. Reconnect or enable the device in Windows; saved assignments are retained."
                : direction == "capture" ? "\nAvailable Windows input, including streamed and virtual microphones."
                : "\nAvailable Windows output, including streamed and virtual listening devices.";
            choices.Add(new() { Id = endpoint.Id, StableId = endpoint.StableId, Name = endpoint.Name,
                State = endpoint.State, Channels = endpoint.Channels, DisplayName = display + state + (!compatible ? " · incompatible" : ""),
                IsSelectable = compatible && endpoint.State == "Active", IsCurrent = ReferenceEquals(endpoint, current), Help = help });
        }
        if (!endpoints.Any(endpoint => Compatible(endpoint, direction) && endpoint.State == "Active"))
            choices.Add(new() { DisplayName = "No available " + (direction == "capture" ? "inputs" : "outputs") + " · see Settings", IsSelectable = false,
                Help = "Connect an audio device, or open Settings → Audio device visibility to inspect hidden devices." });
        return choices;
    }

    internal static EndpointChoice? Selected(IReadOnlyList<EndpointChoice> choices, DeviceBinding binding)
    {
        if (!VirtualDeviceBindings.IsAssigned(binding)) return choices[0];
        var current = choices.FirstOrDefault(choice => choice.IsCurrent);
        if (current is not null) return current;
        var exact = binding.Id.Length > 0 ? choices.FirstOrDefault(choice => choice.Id == binding.Id) : null;
        return exact ?? (binding.StableId.Length > 0 ? choices.FirstOrDefault(choice => choice.StableId == binding.StableId) : null);
    }

    internal static bool CanAssign(EndpointChoice choice, IReadOnlyList<Endpoint> inventory, string direction)
        => choice.IsSelectable && (choice.Id.Length == 0 && choice.StableId.Length == 0
            || inventory.Any(endpoint => endpoint.Id == choice.Id && endpoint.State == "Active" && Compatible(endpoint, direction)));
}
