using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Shared node categories for the creation menu and node header colours.</summary>
internal static class NodeCatalog
{
    internal sealed record Category(string Title, string HeaderBrushKey, IReadOnlyList<string> Kinds)
    {
        internal Brush HeaderBrush => ConsoleTheme.Get(HeaderBrushKey);
        internal string Description => Title switch
        {
            "Inputs" => "Bring sound into the workspace from a microphone, a virtual cable or a running app.",
            "Outputs" => "Send the finished mix to a listening device or to a virtual microphone for another app.",
            "Effects" => "Shape tone, control loudness, reduce unwanted noise or add space to an audio path.",
            "Mixing" => "Combine, split and blend audio paths, or adjust their stereo position and width.",
            "Controls" => "Scale several node levels together using a linked multiplier.",
            _ => ""
        };
    }

    internal static IReadOnlyList<Category> Categories { get; } =
    [
        new("Inputs", "NodeInputHeaderBrush", ["microphone", "virtualInput", "appInput"]),
        new("Outputs", "NodeOutputHeaderBrush", ["output", "virtualOutput"]),
        new("Effects", "NodeProcessorHeaderBrush", ["eq", "tone", "voiceColor", "highPass", "lowPass", "compressor", "limiter", "ducking", "expander", "gate", "alignmentDelay", "denoiser", "plosive", "deEsser", "reverb"]),
        new("Mixing", "NodeUtilityHeaderBrush", ["mix", "pan", "mono", "channelSwap", "stereoWidth", "stereoPan", "crossfade", "stereoCombine", "stereoSplit"]),
        new("Controls", "NodeControllerHeaderBrush", ["control"])
    ];

    internal static Category CategoryFor(string kind) => Categories.First(category => category.Kinds.Contains(kind is "gain" or "reroute" ? "mix" : kind));
    internal static bool CanCreate(string kind) => Categories.Any(category => category.Kinds.Contains(kind));
    internal static bool HasLevelControl(string kind) => kind is "microphone" or "virtualInput" or "appInput" or "output" or "virtualOutput" or "mix" or "gain" or "reroute";
    internal static string Title(string kind) => GraphDefaults.KindLabel(kind);
    internal static string Description(string kind) => kind switch
    {
        "microphone" => "Bring audio from a Windows recording device into the workspace, including a physical, virtual or streamed microphone.",
        "virtualInput" => "Receive the combined audio sent to the VB-CABLE virtual cable by other apps.",
        "appInput" => "Capture a running app's audio as a separate source. Its original playback continues.",
        "output" => "Send audio to headphones, speakers or a streamed headset such as Virtual Desktop. Starts muted so you can choose a device and level first.",
        "virtualOutput" => "Send audio to VAC Lite so another app can use your mix as its microphone. Starts muted.",
        "eq" => "Shape specific parts of the sound with up to eight adjustable frequency bands.",
        "tone" => "Adjust bass, midrange and treble with three simple tone controls.",
        "voiceColor" => "Shape a voice from warm to bright and from natural to a fuller broadcast character.",
        "highPass" => "Remove low-frequency rumble below a chosen cutoff while keeping higher sounds.",
        "lowPass" => "Remove high-frequency hiss or brightness above a chosen cutoff while keeping lower sounds.",
        "compressor" => "Reduce louder passages of its own audio with threshold, ratio, attack, release and make-up gain.",
        "limiter" => "Limit stereo sample peaks to a ceiling, with no lookahead.",
        "ducking" => "Static ducking lowers by a fixed amount; dynamic ducking follows trigger loudness above threshold. Trigger audio is detector only.",
        "expander" => "Gradually reduce quiet audio below the threshold using downward expansion.",
        "gate" => "Reduce quiet audio using a noise gate with hold, range and hysteresis.",
        "alignmentDelay" => "Delay one audio path by a precise number of milliseconds or samples to align it with another.",
        "denoiser" => "Reduce background noise in speech with RNNoise or SpeexDSP. Adds a short processing delay, including when bypassed.",
        "plosive" => "Reduce low-frequency P and B thumps or microphone pops when they exceed a threshold.",
        "deEsser" => "Reduce harsh S and SH sounds when their high-frequency level exceeds a threshold.",
        "reverb" => "Add a room-like tail and a sense of space, blended with the original sound.",
        "mix" => "Combine several incoming audio paths and adjust their shared level, or use one input to control a single branch.",
        "pan" => "Adjust left/right stereo balance by reducing the opposite channel. Centre preserves the original stereo sound.",
        "mono" => "Combine left and right equally and send the same centred sound to both channels.",
        "channelSwap" => "Exchange the left and right channels without changing their levels.",
        "stereoWidth" => "Adjust stereo width: 0% is mono, 100% keeps the original sound, and 200% widens existing stereo detail.",
        "stereoPan" => "Position a mono signal from left to right using equal-power panning. Stereo input is first combined to mono.",
        "crossfade" => "Blend stereo inputs A and B with one control. Equal power suits different sources; Linear suits matching signals. Bypass plays A only.",
        "stereoCombine" => "Place the Left input in the left channel and the Right input in the right channel of one stereo output. Each input is combined to mono; an empty side stays silent.",
        "stereoSplit" => "Extract left and right from stereo audio as separate mono outputs, ready for independent processing or Stereo Combine.",
        "control" => "Scale connected node levels together. 100% is unchanged; 0% is silent.",
        "gain" => "Adjust the level of one audio path. Kept for existing workspaces; use Mix when adding a node.",
        "reroute" => "Pass audio through a named point in an existing route. Kept for existing workspaces.",
        _ => ""
    };
    internal static bool MatchesSearch(string kind, string query)
        => (Title(kind) + " " + kind + " " + CategoryFor(kind).Title + " " + Description(kind)
            + (kind == "ducking" ? " sidechain music voice trigger" : kind == "control" ? " multiplier volume" : "")).Contains(query, System.StringComparison.OrdinalIgnoreCase);
    internal static string HeaderBrushKey(string kind) => CategoryFor(kind).HeaderBrushKey;
    internal static Brush HeaderBrush(string kind) => CategoryFor(kind).HeaderBrush;
}
