using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioMixer.Desktop;

/// <summary>Explicitly inert design fixtures. These identifiers never reach WASAPI or the pipe.</summary>
internal static class UiPreview
{
    public static MixerSnapshot CreateSnapshot(string[] args)
    {
        var config = new MixerConfiguration { Revision = 1, BusA = new() { Muted = true, GainDb = -24 } };
        DeviceBinding Binding(string id, string name) => new() { Id = "preview-" + id, Name = name };
        config.Microphones[0] = Binding("mic1", "USB microphone");
        config.MainOutputs[0] = Binding("a1", "Listening device 1");
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
            config.MainOutputs[1] = Binding("a2", "Bluetooth headphones");
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
