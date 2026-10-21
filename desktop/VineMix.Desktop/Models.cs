using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace VineMix.Desktop;

internal static class AudioLevel
{
    internal static bool IsValidGain(double value) => double.IsFinite(value) && value >= -60 && value <= 12;
}

public sealed class DeviceBinding
{
    public string Id { get; set; } = "";
    public string StableId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Channel { get; set; }
    public DeviceBinding Copy() => new() { Id = Id, StableId = StableId, Name = Name, Channel = Channel };
}

public sealed class SendControl
{
    public SendControl Copy() => (SendControl)MemberwiseClone();
    public bool Enabled { get; set; }
    public double GainDb { get; set; }
}
public sealed class SourceControl
{
    public SourceControl Copy()
    {
        var copy = (SourceControl)MemberwiseClone();
        copy.A = A.Copy(); copy.B = B.Copy(); return copy;
    }
    public bool Muted { get; set; }
    public SendControl A { get; set; } = new();
    public SendControl B { get; set; } = new();
}
public sealed class BusControl
{
    public BusControl Copy() => (BusControl)MemberwiseClone();
    public bool Muted { get; set; }
    public double GainDb { get; set; }
}
public sealed class MixerConfiguration
{
    public int Version { get; set; } = 1;
    public ulong Revision { get; set; }
    public DeviceBinding[] Microphones { get; set; } = [new() { Channel = -1 }, new() { Channel = -1 }];
    public DeviceBinding VirtualInputCapture { get; set; } = new() { Channel = -1 };
    public DeviceBinding VirtualInputPlayback { get; set; } = new() { Channel = -1 };
    public DeviceBinding[] MainOutputs { get; set; } = [new(), new()];
    public DeviceBinding VirtualOutputRender { get; set; } = new() { Channel = -1 };
    public DeviceBinding VirtualOutputCapture { get; set; } = new() { Channel = -1 };
    public SourceControl[] Sources { get; set; } = [new(), new(), new() { A = new() { Enabled = true } }];
    public BusControl BusA { get; set; } = new();
    public BusControl BusB { get; set; } = new();
    public bool StartAtLogin { get; set; }
    public GraphConfiguration Graph { get; set; } = new();
    public MixerConfiguration Copy()
    {
        var copy = (MixerConfiguration)MemberwiseClone();
        copy.Microphones = Microphones.Select(device => device.Copy()).ToArray();
        copy.MainOutputs = MainOutputs.Select(device => device.Copy()).ToArray();
        copy.VirtualInputCapture = VirtualInputCapture.Copy(); copy.VirtualInputPlayback = VirtualInputPlayback.Copy();
        copy.VirtualOutputRender = VirtualOutputRender.Copy(); copy.VirtualOutputCapture = VirtualOutputCapture.Copy();
        copy.Sources = Sources.Select(source => source.Copy()).ToArray();
        copy.BusA = BusA.Copy(); copy.BusB = BusB.Copy(); copy.Graph = Graph.Copy();
        return copy;
    }
    public bool IsValidShape => Version is 1 or 2 && Microphones is { Length: 2 } && MainOutputs is { Length: 2 } && Sources is { Length: 3 } && Graph is not null;
}
public sealed class Endpoint
{
    public string Id { get; set; } = "";
    public string StableId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Direction { get; set; } = "";
    public string State { get; set; } = "Missing";
    public bool IsVirtual { get; set; }
    public string Transport { get; set; } = "physical";
    public string Description { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public int FormFactor { get; set; } = -1;
    public int Channels { get; set; }
    public int Rate { get; set; }
    public string Details => $"{Direction} · {Transport} · {State} · {Channels} ch · {Rate / 1000.0:0.#} kHz";
}
public sealed class PathState
{
    public string State { get; set; } = "Unassigned";
    public string Message { get; set; } = "";
    public string EndpointId { get; set; } = "";
    public int Rate { get; set; }
    public int Channels { get; set; }
    public ulong Underruns { get; set; }
    public ulong Overruns { get; set; }
    public ulong Reconnects { get; set; }
    public double QueueMs { get; set; }
    public double PeriodMs { get; set; }
    public double BufferMs { get; set; }
    public double TargetQueueMs { get; set; }
    public double ResamplerPendingMs { get; set; }
    public string Display => State switch
    {
        "Unassigned" => "Not assigned",
        "Resolving" or "Opening" or "Priming" => "Connecting",
        "Running" => "Connected",
        "Waiting" => "Waiting for device",
        "Busy" => "Device busy",
        "AccessDenied" => "Microphone access blocked",
        "ServiceUnavailable" => "Audio service unavailable",
        "Recovering" => "Reconnecting",
        "Unsupported" => "App capture unavailable",
        "Error" => "Audio setup needs attention",
        _ => "Connecting"
    };
}
public sealed class EngineStatus
{
    public List<GraphNodeStatus> Nodes { get; set; } = [];
    public ulong AudioFrames { get; set; }
    public float[] SourcePeaks { get; set; } = [0, 0, 0];
    public float PeakA { get; set; }
    public float PeakB { get; set; }
    public bool ClipA { get; set; }
    public bool ClipB { get; set; }
    public PathState[] Inputs { get; set; } = [new(), new(), new()];
    public PathState[] Outputs { get; set; } = [new(), new(), new()];
}
public sealed class MixerSnapshot
{
    public string SettingsState { get; set; } = "loaded";
    public string BackupPath { get; set; } = "";
    public string PhysicalSettingsPath { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string EngineVersion { get; set; } = "";
    public int EngineProtocolVersion { get; set; }
    public int EnginePid { get; set; }
    public List<AudioApplication> Applications { get; set; } = [];
    public MixerConfiguration Configuration { get; set; } = new();
    public List<Endpoint> Endpoints { get; set; } = [];
    public EngineStatus Status { get; set; } = new();
    public string SettingsPath { get; set; } = "";
    public string LogPath { get; set; } = "";
    public string Warning { get; set; } = "";
}
public sealed class ProtocolResponse
{
    public int ProtocolVersion { get; set; }
    public string RequestId { get; set; } = "";
    public bool Ok { get; set; }
    public string Error { get; set; } = "";
    public MixerSnapshot? Snapshot { get; set; }
}
public static class Protocol
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
}
