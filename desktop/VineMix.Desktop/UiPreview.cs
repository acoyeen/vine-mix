using System;
using System.Collections.Generic;
using System.Linq;

namespace VineMix.Desktop;

/// <summary>Explicitly inert design fixtures. These identifiers never reach WASAPI or the pipe.</summary>
internal static class UiPreview
{
    /// <summary>Sanitized three-node starter, using only synthetic endpoints and telemetry.</summary>
    public static MixerSnapshot CreateBetaSnapshot(bool configured = false)
    {
        var configuration = new MixerConfiguration { Version = 2, Revision = 1, Graph = GraphDefaults.Fresh() };
        var input = configuration.Graph.Nodes.Single(node => node.Id == "starter-input");
        var listening = configuration.Graph.Nodes.Single(node => node.Id == "starter-listening-output");
        var output = configuration.Graph.Nodes.Single(node => node.Id == "starter-virtual-output");
        var endpoints = new List<Endpoint>();
        if (configured)
        {
            input.Device = new() { Id = "preview-example-microphone", Name = "Example USB Microphone", Channel = -1 };
            listening.Device = new() { Id = "preview-example-headphones", Name = "Example Headphones", Channel = -1 };
            output.Device = new() { Id = "preview-example-vac-render", Name = "Line 1 (Virtual Audio Cable)", Channel = -1 };
            configuration.VirtualOutputRender = output.Device.Copy();
            configuration.VirtualOutputCapture = new() { Id = "preview-example-vac-capture", Name = "Line 1 (Virtual Audio Cable)" };
            void Add(DeviceBinding device, string direction, string transport, int channels) => endpoints.Add(new()
            { Id = device.Id, Name = device.Name, Direction = direction, Transport = transport, IsVirtual = transport != "physical", Channels = channels, Rate = 48000, State = "Active" });
            Add(input.Device, "capture", "physical", 1);
            Add(listening.Device, "render", "physical", 2);
            Add(output.Device, "render", "vacLite", 2);
            Add(configuration.VirtualOutputCapture, "capture", "vacLite", 2);
        }
        var status = new EngineStatus { AudioFrames = configured ? 48000UL : 0 };
        foreach (var node in configuration.Graph.Nodes)
            status.Nodes.Add(new() { Id = node.Id, Channels = node == input ? 1 : 2,
                Path = new() { EndpointId = node.Device.Id, State = configured ? "Running" : "Unassigned", Channels = node == input ? 1 : 2, Rate = 48000 } });
        return new() { Configuration = configuration, Endpoints = endpoints, Status = status,
            SettingsPath = "Sanitized preview · no settings file", LogPath = "Sanitized preview · no audio log" };
    }

    public static MixerSnapshot CreateSnapshot(string[] args)
    {
        var config = new MixerConfiguration { Revision = 1, BusA = new() { Muted = true, GainDb = -24 } };
        DeviceBinding Binding(string id, string name) => new() { Id = "preview-" + id, Name = name };
        config.Microphones[0] = Binding("mic1", "Example USB Microphone");
        config.MainOutputs[0] = Binding("a1", "Example Headphones");
        config.VirtualInputPlayback = Binding("apps-play", "CABLE Input (VB-Audio Virtual Cable)");
        config.VirtualInputCapture = Binding("apps-capture", "CABLE Output (VB-Audio Virtual Cable)");
        config.VirtualOutputRender = Binding("send-play", "Line 1 (Virtual Audio Cable)");
        config.VirtualOutputCapture = Binding("send-capture", "Line 1 (Virtual Audio Cable)");
        config.Sources[0].B.Enabled = true;
        var endpoints = new List<Endpoint>();
        void Add(DeviceBinding binding, string direction, string transport, int channels = 2) => endpoints.Add(new() { Id = binding.Id, Name = binding.Name, Direction = direction, Transport = transport, IsVirtual = transport != "physical", Channels = channels, Rate = 48000, State = "Active" });
        Add(config.Microphones[0], "capture", "physical", 1); Add(config.MainOutputs[0], "render", "physical");
        Add(config.VirtualInputPlayback, "render", "vbCable"); Add(config.VirtualInputCapture, "capture", "vbCable");
        Add(config.VirtualOutputRender, "render", "vacLite"); Add(config.VirtualOutputCapture, "capture", "vacLite");
        endpoints.Add(new() { Id = "preview-vb16", Name = "CABLE In 16ch (VB-Audio Virtual Cable)", Direction = "render", Transport = "vbCable", IsVirtual = true, Channels = 2, Rate = 48000, State = "Active" });
        PathState Running(DeviceBinding binding, int channels = 2) => new() { EndpointId = binding.Id, State = "Running", Channels = channels, Rate = 48000 };
        var status = new EngineStatus { AudioFrames = 48000, Inputs = [Running(config.Microphones[0], 1), new(), Running(config.VirtualInputCapture)], Outputs = [Running(config.MainOutputs[0]), new(), Running(config.VirtualOutputRender)] };
        if (args.Contains("--waiting"))
        {
            endpoints[0].State = "Missing"; status.Inputs[0].State = "Waiting";
            config.MainOutputs[1] = Binding("a2", "Example Speakers");
            status.Outputs[1] = new() { State = "Waiting", EndpointId = config.MainOutputs[1].Id };
        }
        if (args.Contains("--signal")) { status.SourcePeaks = [.12f, 0, .4f]; status.PeakB = .12f; }
        config.Graph = GraphDefaults.FromLegacy(config); config.Version = 2;
        for (int i = 0; i < 3; i++)
            status.Nodes.Add(new() { Id = new[] { "mic1", "mic2", "app-input" }[i], Peak = status.SourcePeaks[i], Path = status.Inputs[i] });
        status.Nodes.Add(new() { Id = "mix-a", Peak = status.PeakA, Path = new() { State = "Running" } });
        status.Nodes.Add(new() { Id = "mix-b", Peak = status.PeakB, Path = new() { State = "Running" } });
        for (int i = 0; i < 3; i++)
            status.Nodes.Add(new() { Id = new[] { "main-output-1", "main-output-2", "virtual-output" }[i], Peak = i < 2 ? status.PeakA : status.PeakB, Path = status.Outputs[i] });
        foreach (var meter in status.Nodes)
        {
            // These explicit fixture channels are inert preview signals only.
            meter.Channels = meter.Id is "mic1" or "mic2" or "mix-b" or "virtual-output" ? 1 : 2;
            meter.PeakLeft = meter.Peak;
            meter.PeakRight = meter.Channels == 1 ? meter.Peak : meter.Peak * .6f;
        }
        return new() { Configuration = config, Endpoints = endpoints, Status = status,
            Applications = [new() { ProcessId = 100, Name = "Spotify", ExecutablePath = "preview-spotify.exe" }, new() { ProcessId = 200, Name = "Browser", ExecutablePath = "preview-browser.exe" }],
            SettingsPath = "UI preview — no settings file", LogPath = "UI preview — no audio logging" };
    }
}
