using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace VineMix.Desktop;

public sealed class ApplicationBinding
{
    public ApplicationBinding Copy() => (ApplicationBinding)MemberwiseClone();
    public uint ProcessId { get; set; }
    public string ExecutablePath { get; set; } = "";
    public string Name { get; set; } = "";
}
public sealed class AudioApplication
{
    public uint ProcessId { get; set; }
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string Label => ProcessId == 0 ? (Name.Length > 0 ? Name : "Choose a running app") : $"{(Name.Length > 0 ? Name : "Application")} · PID {ProcessId}";
}
public sealed class GraphConfiguration
{
    public List<GraphNode> Nodes { get; set; } = [];
    public List<GraphEdge> Edges { get; set; } = [];
    // Organizational frames belong to the desktop workspace preferences. They
    // never enter the engine protocol or audio configuration JSON.
    [JsonIgnore]
    public List<GraphFrame> Frames { get; set; } = [];
    public double PanX { get; set; } = 20;
    public double PanY { get; set; } = 20;
    public double Zoom { get; set; } = 1;
    public GraphConfiguration Copy()
    {
        var copy = (GraphConfiguration)MemberwiseClone();
        copy.Nodes = Nodes.Select(node => node.Copy()).ToList();
        copy.Edges = Edges.Select(edge => edge.Copy()).ToList();
        copy.Frames = Frames.Select(frame => frame.Copy()).ToList();
        return copy;
    }
}
public sealed class GraphNode
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "mix";
    public string Name { get; set; } = "Mix";
    public double X { get; set; }
    public double Y { get; set; }
    public double GainDb { get; set; }
    // -1 preserves the exact dB setting of an older Linked control.
    public double ControlMultiplier { get; set; } = -1;
    public bool Muted { get; set; }
    public bool Mono { get; set; }
    public bool AutoAssignDevice { get; set; } = true;
    public DeviceBinding Device { get; set; } = new() { Channel = -1 };
    public ApplicationBinding App { get; set; } = new();
    public bool Bypass { get; set; }
    public bool Collapsed { get; set; }
    public double Balance { get; set; }
    public double StereoWidth { get; set; } = 1;
    public double PanPosition { get; set; }
    public double CrossfadePosition { get; set; }
    public string CrossfadeCurve { get; set; } = "equalPower";
    public List<EqBand> EqBands { get; set; } = EqBand.Defaults();
    public CompressorSettings Compressor { get; set; } = new();
    // Absent in existing workspaces: retain their full legacy compressor UI.
    // This is presentation metadata only, never a DSP mode or migration flag.
    public int DynamicsUiVersion { get; set; }
    public DuckingSettings Ducking { get; set; } = new();
    public DenoiserSettings Denoiser { get; set; } = new();
    public DeEsserSettings DeEsser { get; set; } = new();
    public PlosiveSettings Plosive { get; set; } = new();
    public ReverbSettings Reverb { get; set; } = new();
    public VoiceColorSettings VoiceColor { get; set; } = new();
    public ExpanderSettings Expander { get; set; } = new();
    public AlignmentDelaySettings AlignmentDelay { get; set; } = new();
    public GraphNode Copy()
    {
        var copy = (GraphNode)MemberwiseClone();
        copy.Device = Device.Copy(); copy.App = App.Copy();
        copy.EqBands = EqBands.Select(band => band.Copy()).ToList();
        copy.Compressor = Compressor.Copy(); copy.Ducking = Ducking.Copy(); copy.Denoiser = Denoiser.Copy();
        copy.DeEsser = DeEsser.Copy(); copy.Plosive = Plosive.Copy(); copy.Reverb = Reverb.Copy();
        copy.VoiceColor = VoiceColor.Copy();
        copy.Expander = Expander.Copy(); copy.AlignmentDelay = AlignmentDelay.Copy();
        return copy;
    }
}
public sealed class EqBand
{
    public EqBand Copy() => (EqBand)MemberwiseClone();
    public string Type { get; set; } = "peak";
    public bool Enabled { get; set; } = true;
    public double FrequencyHz { get; set; } = 1000;
    public double GainDb { get; set; }
    public double Q { get; set; } = 1;
    public double ResetFrequencyHz { get; set; }
    public static double DefaultQForType(string type) => type is "lowShelf" or "highShelf" or "highPass" or "lowPass" ? .70710678 : 1;
    public static List<EqBand> Defaults() => [new() { Type = "lowShelf", FrequencyHz = 80, ResetFrequencyHz = 80, Q = .70710678 }, new() { FrequencyHz = 250, ResetFrequencyHz = 250 }, new() { ResetFrequencyHz = 1000 }, new() { FrequencyHz = 4000, ResetFrequencyHz = 4000 }, new() { Type = "highShelf", FrequencyHz = 10000, ResetFrequencyHz = 10000, Q = .70710678 }];
    public static List<EqBand> ToneDefaults() => [new() { Type = "lowShelf", FrequencyHz = 200, Q = .70710678 }, new() { Type = "peak", FrequencyHz = 1000, Q = 1 }, new() { Type = "highShelf", FrequencyHz = 5000, Q = .70710678 }];
    public static List<EqBand> FilterDefaults(string kind) => kind is "highPass" or "lowPass"
        ? [new() { Type = kind, FrequencyHz = kind == "highPass" ? 80 : 12000, Q = .70710678 }]
        : throw new ArgumentOutOfRangeException(nameof(kind), "Choose a high-pass or low-pass filter.");
}
public sealed class CompressorSettings
{
    public CompressorSettings Copy() => (CompressorSettings)MemberwiseClone();
    public string Mode { get; set; } = "compress";
    public string Detector { get; set; } = "internal";
    public double CeilingDb { get; set; } = -1;
    public double ThresholdDb { get; set; } = -18;
    public double Ratio { get; set; } = 4;
    public double AttackMs { get; set; } = 10;
    public double ReleaseMs { get; set; } = 120;
    public double MakeupDb { get; set; }
    public static CompressorSettings Ducking() => new() { Detector = "external", ThresholdDb = -30, Ratio = 6, AttackMs = 10, ReleaseMs = 250 };
}
public sealed class DuckingSettings
{
    public string Mode { get; set; } = "static";
    public double Ratio { get; set; } = 4;
    public double ThresholdDb { get; set; } = -30;
    public double AmountDb { get; set; } = 18;
    public double AttackMs { get; set; } = 10;
    public double ReleaseMs { get; set; } = 250;
    public DuckingSettings Copy() => (DuckingSettings)MemberwiseClone();
}
public sealed class ExpanderSettings
{
    public string Mode { get; set; } = "expander";
    public double ThresholdDb { get; set; } = -40;
    public double Ratio { get; set; } = 2;
    public double AttackMs { get; set; } = 5;
    public double HoldMs { get; set; } = 50;
    public double ReleaseMs { get; set; } = 150;
    public double RangeDb { get; set; } = 60;
    public double HysteresisDb { get; set; } = 3;
    public ExpanderSettings Copy() => (ExpanderSettings)MemberwiseClone();
}
public sealed class AlignmentDelaySettings
{
    public int Samples { get; set; }
    public AlignmentDelaySettings Copy() => (AlignmentDelaySettings)MemberwiseClone();
    public static int FromMilliseconds(double value) => (int)Math.Clamp(Math.Round(value * 48, MidpointRounding.AwayFromZero), 0, 96000);
    [System.Text.Json.Serialization.JsonIgnore]
    public double Milliseconds => Samples / 48.0;
}
public sealed class DenoiserSettings
{
    public double SuppressionDb { get; set; } = -18;
    // Missing saved/clipboard fields keep their original Speex sound.
    // Only the new-node factory opts newly added denoisers into RNNoise.
    public string Algorithm { get; set; } = "speex";
    public double RnnoiseMix { get; set; } = 1;
    public DenoiserSettings Copy() => (DenoiserSettings)MemberwiseClone();
}
public sealed class DeEsserSettings
{
    public DeEsserSettings Copy() => (DeEsserSettings)MemberwiseClone();
    public double FrequencyHz { get; set; } = 6000;
    public double ThresholdDb { get; set; } = -24;
    public double ReductionDb { get; set; } = 9;
}
public sealed class PlosiveSettings
{
    public PlosiveSettings Copy() => (PlosiveSettings)MemberwiseClone();
    public double FrequencyHz { get; set; } = 150;
    public double ThresholdDb { get; set; } = -24;
    public double ReductionDb { get; set; } = 12;
}
public sealed class ReverbSettings
{
    public ReverbSettings Copy() => (ReverbSettings)MemberwiseClone();
    public double Mix { get; set; } = .2;
    public double DecaySeconds { get; set; } = 1.2;
    public double Damping { get; set; } = .5;
    public double PreDelayMs { get; set; } = 15;
}
public sealed class VoiceColorSettings
{
    public double Tone { get; set; }
    // Persist the original key so existing graphs/clipboard values survive;
    // the vertical axis is now presented as Natural-to-Broadcast Character.
    public double Ambience { get; set; }
    public VoiceColorSettings Copy() => (VoiceColorSettings)MemberwiseClone();
}
public sealed class GraphEdge
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Kind { get; set; } = "audio";
    public string FromPort { get; set; } = "audio";
    public string ToPort { get; set; } = "audio";
    public double GainDb { get; set; }
    public bool Muted { get; set; }
    public GraphEdge Copy() => (GraphEdge)MemberwiseClone();
}
public sealed class GraphNodeStatus
{
    public string Id { get; set; } = "";
    public float Peak { get; set; }
    public float? PeakLeft { get; set; }
    public float? PeakRight { get; set; }
    public int Channels { get; set; }
    public bool Clipped { get; set; }
    public float GainReductionDb { get; set; }
    public float DetectorDb { get; set; } = -96;
    public ulong DynamicsFrame { get; set; }
    public float[] SpectrumDb { get; set; } = [];
    public ulong SpectrumFrame { get; set; }
    public ulong SpectrumRevision { get; set; }
    public PathState Path { get; set; } = new();
}

public static class GraphDefaults
{
    public static GraphConfiguration Fresh()
    {
        var input = NewNode("microphone", 48, 200);
        input.Id = "starter-input"; input.Name = "Physical input"; input.AutoAssignDevice = false;
        var virtualOutput = NewNode("virtualOutput", 480, 48);
        virtualOutput.Id = "starter-virtual-output"; virtualOutput.Name = "Virtual output";
        var listening = NewNode("output", 480, 420);
        listening.Id = "starter-listening-output"; listening.Name = "Listening output"; listening.AutoAssignDevice = false;
        return new()
        {
            Nodes = [input, virtualOutput, listening],
            Edges = [new() { Id = "starter-virtual-route", From = input.Id, To = virtualOutput.Id },
                new() { Id = "starter-listening-route", From = input.Id, To = listening.Id }]
        };
    }
    public const int MaximumEqBands = 8;
    public static readonly string[] Kinds = ["microphone", "virtualInput", "appInput", "mix", "gain", "eq", "tone", "highPass", "lowPass", "compressor", "limiter", "ducking", "pan", "mono", "reroute", "output", "virtualOutput", "control", "denoiser", "deEsser", "plosive", "reverb", "voiceColor", "expander", "gate", "alignmentDelay", "channelSwap", "stereoWidth", "stereoPan", "crossfade", "stereoCombine", "stereoSplit"];
    public static bool HasAudioInput(string kind) => kind is "mix" or "gain" or "output" or "virtualOutput" or "eq" or "tone" or "highPass" or "lowPass" or "compressor" or "limiter" or "ducking" or "pan" or "mono" or "reroute" or "denoiser" or "deEsser" or "plosive" or "reverb" or "voiceColor" or "expander" or "gate" or "alignmentDelay" or "channelSwap" or "stereoWidth" or "stereoPan" or "crossfade" or "stereoCombine" or "stereoSplit";
    public static bool HasAudioOutput(string kind) => kind is "microphone" or "virtualInput" or "appInput" or "mix" or "gain" or "eq" or "tone" or "highPass" or "lowPass" or "compressor" or "limiter" or "ducking" or "pan" or "mono" or "reroute" or "denoiser" or "deEsser" or "plosive" or "reverb" or "voiceColor" or "expander" or "gate" or "alignmentDelay" or "channelSwap" or "stereoWidth" or "stereoPan" or "crossfade" or "stereoCombine" or "stereoSplit";
    public static bool HasSidechainInput(string kind) => kind == "ducking";
    public static bool HasSidechainInput(GraphNode node) => HasSidechainInput(node.Kind);
    public static string[] AudioInputPorts(string kind) => kind switch
    {
        "stereoCombine" => ["left", "right"], "crossfade" => ["a", "b"],
        _ => HasAudioInput(kind) ? ["audio"] : []
    };
    public static string[] AudioOutputPorts(string kind) => kind == "stereoSplit" ? ["left", "right"] : HasAudioOutput(kind) ? ["audio"] : [];
    public static string PortLabel(string port) => port switch { "left" => "Left", "right" => "Right", "a" => "A", "b" => "B", _ => "Audio" };
    public static string KindLabel(string kind) => kind switch
    {
        "microphone" => "Microphone", "virtualInput" => "Virtual input", "appInput" => "Running app", "expander" => "Expander", "gate" => "Gate", "alignmentDelay" => "Alignment Delay",
        "mix" => "Mix", "gain" => "Gain", "output" => "Listening output", "virtualOutput" => "Virtual output", "control" => "Linked control",
        "eq" => "Equalizer", "tone" => "Low / Mid / High", "highPass" => "High-pass filter", "lowPass" => "Low-pass filter", "compressor" => "Compressor", "limiter" => "Limiter", "ducking" => "Ducking", "pan" => "Balance", "mono" => "Mono", "reroute" => "Reroute", "denoiser" => "Denoiser", "deEsser" => "De-esser", "plosive" => "Plosive remover", "reverb" => "Reverb", "voiceColor" => "Voice Color", "channelSwap" => "Channel Swap", "stereoWidth" => "Stereo Width", "stereoPan" => "Pan", "crossfade" => "Crossfade", "stereoCombine" => "Stereo Combine", "stereoSplit" => "Stereo Split", _ => kind
    };
    public static GraphNode NewNode(string kind, double x, double y) => new()
    {
        Id = Guid.NewGuid().ToString("N"), Kind = kind, Name = KindLabel(kind), X = x, Y = y,
        Device = new() { Channel = -1 },
        Denoiser = new() { Algorithm = kind == "denoiser" ? "rnnoise" : "speex" },
        DynamicsUiVersion = kind == "compressor" ? 1 : 0,
        Expander = new() { Mode = kind == "gate" ? "gate" : "expander" },
        GainDb = kind == "output" ? -24 : 0,
        ControlMultiplier = kind == "control" ? 1 : -1,
        Muted = kind is "output" or "virtualOutput",
        // Keep GraphNode's initializer for older saved nodes that omit EqBands;
        // newly created or explicitly reset EQs start empty.
        EqBands = kind == "eq" ? [] : kind == "tone" ? EqBand.ToneDefaults()
            : kind is "highPass" or "lowPass" ? EqBand.FilterDefaults(kind) : EqBand.Defaults()
    };
    public static void ResetNodeSettings(GraphNode node)
    {
        var defaults = NewNode(node.Kind, node.X, node.Y);
        // Reset both processors' controls without switching the chosen processor
        // or changing its latency. RNNoise is the default for creation only.
        if (node.Kind == "denoiser") defaults.Denoiser.Algorithm = node.Denoiser.Algorithm;
        // Earlier Gate nodes were saved as expander records with gate mode.
        if (node.Kind == "expander" && node.Expander.Mode == "gate") defaults.Expander.Mode = "gate";
        // Reset sound controls, never identity, routing, placement or assignment.
        node.GainDb = defaults.GainDb; node.Muted = defaults.Muted;
        node.ControlMultiplier = defaults.ControlMultiplier;
        node.Mono = defaults.Mono; node.Device.Channel = -1;
        node.Bypass = defaults.Bypass; node.Balance = defaults.Balance; node.Collapsed = false;
        node.StereoWidth = defaults.StereoWidth; node.PanPosition = defaults.PanPosition;
        node.CrossfadePosition = defaults.CrossfadePosition; node.CrossfadeCurve = defaults.CrossfadeCurve;
        node.EqBands = defaults.EqBands; node.Compressor = defaults.Compressor; node.Ducking = defaults.Ducking;
        node.Denoiser = defaults.Denoiser; node.DeEsser = defaults.DeEsser;
        node.Plosive = defaults.Plosive; node.Reverb = defaults.Reverb;
        node.VoiceColor = defaults.VoiceColor;
        node.Expander = defaults.Expander; node.AlignmentDelay = defaults.AlignmentDelay;
    }
    public static GraphConfiguration FromLegacy(MixerConfiguration configuration)
    {
        var graph = new GraphConfiguration();
        // Only fresh/v1 layouts use these positions; existing graphs keep their saved view.
        string[] sources = ["mic1", "mic2", "app-input"];
        for (int i = 0; i < 3; i++)
            graph.Nodes.Add(new() { Id = sources[i], Kind = i < 2 ? "microphone" : "virtualInput", Name = i < 2 ? $"Microphone {i + 1}" : "App Audio", X = 48, Y = 60 + i * 330, Muted = configuration.Sources[i].Muted, Device = (i < 2 ? configuration.Microphones[i] : configuration.VirtualInputCapture).Copy() });
        graph.Nodes.Add(new() { Id = "mix-a", Kind = "mix", Name = "What I hear", X = 448, Y = 100, GainDb = configuration.BusA.GainDb, Muted = configuration.BusA.Muted });
        graph.Nodes.Add(new() { Id = "mix-b", Kind = "mix", Name = "What others hear", X = 448, Y = 550, GainDb = configuration.BusB.GainDb, Muted = configuration.BusB.Muted });
        for (int i = 0; i < 2; i++)
        {
            graph.Nodes.Add(new() { Id = "main-output-" + (i + 1), Kind = "output", Name = $"Listening output {i + 1}", X = 848, Y = 60 + i * 330, Device = configuration.MainOutputs[i].Copy() });
            graph.Edges.Add(new() { Id = "main-output-" + (i + 1) + "-in", From = "mix-a", To = "main-output-" + (i + 1) });
        }
        graph.Nodes.Add(new() { Id = "virtual-output", Kind = "virtualOutput", Name = "My virtual microphone", X = 848, Y = 720, Device = configuration.VirtualOutputRender.Copy() });
        graph.Edges.Add(new() { Id = "virtual-output-in", From = "mix-b", To = "virtual-output" });
        for (int i = 0; i < 3; i++)
        {
            graph.Edges.Add(new() { Id = sources[i] + "-a", From = sources[i], To = "mix-a", GainDb = configuration.Sources[i].A.GainDb, Muted = !configuration.Sources[i].A.Enabled });
            graph.Edges.Add(new() { Id = sources[i] + "-b", From = sources[i], To = "mix-b", GainDb = configuration.Sources[i].B.GainDb, Muted = !configuration.Sources[i].B.Enabled });
        }
        return graph;
    }
    public static string NewConnectionError(GraphConfiguration graph, GraphEdge candidate)
    {
        if (candidate.Kind == "control" && graph.Nodes.FirstOrDefault(n => n.Id == candidate.To) is { } target
            && !LinkedLevelControl.SupportsInput(target.Kind))
            return "Connect Level control to a node with a Level knob.";
        return ConnectionError(graph, candidate);
    }
    // Saved graphs, copied groups and history may retain older control routes.
    // New interactive links use NewConnectionError instead.
    public static string ConnectionError(GraphConfiguration graph, GraphEdge candidate)
    {
        if (graph.Edges.Count >= 96) return "This workspace can contain up to 96 connections.";
        var from = graph.Nodes.FirstOrDefault(n => n.Id == candidate.From);
        var to = graph.Nodes.FirstOrDefault(n => n.Id == candidate.To);
        if (from is null || to is null) return "Choose two nodes in this workspace.";
        if (from.Id == to.Id) return "A node cannot connect to itself.";
        if (graph.Edges.Any(e => e.From == from.Id && e.To == to.Id && e.Kind == candidate.Kind
            && e.FromPort == candidate.FromPort && e.ToPort == candidate.ToPort)) return "Those sockets are already connected.";
        if (candidate.Kind == "control")
        {
            if (from.Kind != "control" || to.Kind == "control" || candidate.FromPort != "audio" || candidate.ToPort != "audio") return "Connect a linked control to the small square control socket.";
            if (graph.Edges.Any(e => e.Kind == "control" && e.To == to.Id)) return "This node already has a linked control. Remove that connection first.";
            return "";
        }
        if (candidate.Kind == "sidechain")
        {
            if (!AudioOutputPorts(from.Kind).Contains(candidate.FromPort) || candidate.ToPort != "audio") return "Choose a valid audio output and trigger socket.";
            if (!HasAudioOutput(from.Kind) || !HasSidechainInput(to.Kind)) return "Connect an audio output to a Ducking trigger input.";
            if (graph.Edges.Any(e => e.Kind == "sidechain" && e.To == to.Id)) return "This node already has a detector cable. Remove that connection first.";
        }
        else if (candidate.Kind != "audio" || !AudioOutputPorts(from.Kind).Contains(candidate.FromPort) || !AudioInputPorts(to.Kind).Contains(candidate.ToPort)) return "Connect an audio output on the right to an audio input on the left.";
        var seen = new HashSet<string>();
        var pending = new Stack<string>(); pending.Push(to.Id);
        while (pending.TryPop(out string? id))
        {
            if (id == from.Id) return "This connection would send audio back into itself. Choose a different destination.";
            if (!seen.Add(id)) continue;
            foreach (var edge in graph.Edges.Where(e => e.Kind is "audio" or "sidechain" && e.From == id)) pending.Push(edge.To);
        }
        return "";
    }
    public static string Validate(GraphConfiguration graph)
    {
        if (graph.Nodes.Count > 32) return "This workspace can contain up to 32 nodes.";
        if (graph.Edges.Count > 96) return "This workspace can contain up to 96 connections.";
        if (!double.IsFinite(graph.PanX) || !double.IsFinite(graph.PanY) || !double.IsFinite(graph.Zoom) || graph.Zoom is < .3 or > 2) return "The canvas view is invalid.";
        if (graph.Nodes.Select(n => n.Id).Distinct().Count() != graph.Nodes.Count || graph.Nodes.Any(n => string.IsNullOrWhiteSpace(n.Id))) return "Node identities must be unique.";
        if (graph.Edges.Select(n => n.Id).Distinct().Count() != graph.Edges.Count || graph.Edges.Any(n => string.IsNullOrWhiteSpace(n.Id))) return "Connection identities must be unique.";
        foreach (var node in graph.Nodes)
        {
            if (!Kinds.Contains(node.Kind) || !double.IsFinite(node.X) || !double.IsFinite(node.Y) || !AudioLevel.IsValidGain(node.GainDb)
                || node.DynamicsUiVersion is < 0 or > 1) return "A node contains an invalid setting.";
            static bool InRange(double value, double low, double high) => double.IsFinite(value) && value >= low && value <= high;
            if (node.ControlMultiplier != -1 && !InRange(node.ControlMultiplier, 0, 2)) return "A linked multiplier must be between 0 and 200%.";
            if (!InRange(node.StereoWidth, 0, 2) || !InRange(node.PanPosition, -1, 1) || !InRange(node.CrossfadePosition, 0, 1)
                || node.CrossfadeCurve is not ("equalPower" or "linear")) return "A mixing node contains an invalid setting.";
            if (node.Denoiser is null || !InRange(node.Denoiser.SuppressionDb, -40, 0)
                || node.Denoiser.Algorithm is not ("speex" or "rnnoise") || !InRange(node.Denoiser.RnnoiseMix, 0, 1)
                || node.DeEsser is null || !InRange(node.DeEsser.FrequencyHz, 2000, 12000) || !InRange(node.DeEsser.ThresholdDb, -60, 0) || !InRange(node.DeEsser.ReductionDb, 0, 24)
                || node.Plosive is null || !InRange(node.Plosive.FrequencyHz, 60, 300) || !InRange(node.Plosive.ThresholdDb, -60, 0) || !InRange(node.Plosive.ReductionDb, 0, 24)
                || node.Reverb is null || !InRange(node.Reverb.Mix, 0, 1) || !InRange(node.Reverb.DecaySeconds, .1, 5) || !InRange(node.Reverb.Damping, 0, 1) || !InRange(node.Reverb.PreDelayMs, 0, 100)
                || node.VoiceColor is null || !InRange(node.VoiceColor.Tone, -1, 1) || !InRange(node.VoiceColor.Ambience, 0, 1)
                || node.Compressor is null || node.Compressor.Mode is not ("compress" or "limit") || node.Compressor.Detector is not ("internal" or "external")
                || !InRange(node.Compressor.CeilingDb, -24, 0) || !InRange(node.Compressor.ThresholdDb, -60, 0) || !InRange(node.Compressor.Ratio, 1, 20)
                || !InRange(node.Compressor.AttackMs, .1, 200) || !InRange(node.Compressor.ReleaseMs, 5, 2000) || !InRange(node.Compressor.MakeupDb, 0, 24)
                || node.Ducking is null || node.Ducking.Mode is not ("static" or "dynamic") || !InRange(node.Ducking.Ratio, 1, 20)
                || !InRange(node.Ducking.ThresholdDb, -60, 0) || !InRange(node.Ducking.AmountDb, 0, 60)
                || !InRange(node.Ducking.AttackMs, .1, 200) || !InRange(node.Ducking.ReleaseMs, 5, 2000)
                || node.Expander is null || node.Expander.Mode is not ("expander" or "gate") || !InRange(node.Expander.ThresholdDb, -80, 0) || !InRange(node.Expander.Ratio, 1, 20)
                || !InRange(node.Expander.AttackMs, .1, 200) || !InRange(node.Expander.HoldMs, 0, 1000) || !InRange(node.Expander.ReleaseMs, 5, 2000)
                || !InRange(node.Expander.RangeDb, 0, 80) || !InRange(node.Expander.HysteresisDb, 0, 12)
                || node.AlignmentDelay is null || node.AlignmentDelay.Samples is < 0 or > 96000)
                return "An effect contains an invalid setting.";
            if (node.Kind == "gate" && node.Expander.Mode != "gate") return "A Gate node requires gate processing mode.";
            if (node.EqBands is null || node.EqBands.Count > MaximumEqBands) return "An EQ node supports up to eight bands.";
            foreach (var band in node.EqBands)
                if (band is null || band.Type is not ("peak" or "lowShelf" or "highShelf" or "highPass" or "lowPass" or "notch")
                    || !double.IsFinite(band.FrequencyHz) || band.FrequencyHz is < 20 or > 20000
                    || !double.IsFinite(band.GainDb) || band.GainDb is < -24 or > 24
                    || !double.IsFinite(band.Q) || band.Q is < .1 or > 20
                    || !double.IsFinite(band.ResetFrequencyHz) || band.ResetFrequencyHz != 0 && !InRange(band.ResetFrequencyHz, 20, 20000)) return "An EQ band contains an invalid filter, frequency, gain or width.";
            if (node.Kind is "highPass" or "lowPass")
            {
                if (node.EqBands.Count != 1 || node.EqBands[0].Type != node.Kind || !node.EqBands[0].Enabled || node.EqBands[0].GainDb != 0)
                    return "High-pass and low-pass nodes require one enabled matching filter with zero band gain. Use bypass to disable the filter.";
            }
            if (node.Kind == "tone")
            {
                var expected = EqBand.ToneDefaults();
                if (node.EqBands.Count != expected.Count) return "Low / Mid / High requires exactly three tone bands.";
                for (int band = 0; band < expected.Count; ++band)
                {
                    var actual = node.EqBands[band]; var fixedBand = expected[band];
                    if (actual.GainDb is < -12 or > 12) return "Low / Mid / High levels must be between -12 and +12 dB.";
                    if (actual.Type != fixedBand.Type || !actual.Enabled || actual.FrequencyHz != fixedBand.FrequencyHz
                        || Math.Abs(actual.Q - fixedBand.Q) > 1e-7) return "Low / Mid / High uses fixed filters; use EQ for custom bands.";
                }
            }
        }
        var checking = new GraphConfiguration { Nodes = graph.Nodes };
        foreach (var edge in graph.Edges)
        {
            if (!AudioLevel.IsValidGain(edge.GainDb)) return "A connection contains an invalid volume.";
            var error = ConnectionError(checking, edge); if (error.Length > 0) return error;
            checking.Edges.Add(edge);
        }
        return "";
    }
}
