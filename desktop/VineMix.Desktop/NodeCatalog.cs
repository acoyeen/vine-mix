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
    }

    internal static IReadOnlyList<Category> Categories { get; } =
    [
        new("Inputs", "NodeInputHeaderBrush", ["microphone", "virtualInput", "appInput"]),
        new("Outputs", "NodeOutputHeaderBrush", ["output", "virtualOutput"]),
        new("Effects", "NodeProcessorHeaderBrush", ["eq", "tone", "voiceColor", "highPass", "lowPass", "compressor", "limiter", "ducking", "expander", "gate", "alignmentDelay", "denoiser", "plosive", "deEsser", "reverb"]),
        new("Mixing", "NodeUtilityHeaderBrush", ["mix", "pan", "mono"]),
        new("Controls", "NodeControllerHeaderBrush", ["control"])
    ];

    internal static Category CategoryFor(string kind) => Categories.First(category => category.Kinds.Contains(kind is "gain" or "reroute" ? "mix" : kind));
    internal static bool CanCreate(string kind) => Categories.Any(category => category.Kinds.Contains(kind));
    internal static bool HasLevelControl(string kind) => kind is "microphone" or "virtualInput" or "appInput" or "output" or "virtualOutput" or "mix" or "gain" or "control" or "reroute";
    internal static string Title(string kind) => GraphDefaults.KindLabel(kind);
    internal static string Description(string kind) => kind switch
    {
        "compressor" => "Reduce louder passages of its own audio with threshold, ratio, attack, release and make-up gain.",
        "limiter" => "Limit stereo sample peaks to a ceiling, with no lookahead.",
        "ducking" => "Static ducking lowers by a fixed amount; dynamic ducking follows trigger loudness above threshold. Trigger audio is detector only.",
        "expander" => "Gradually reduce quiet audio below the threshold using downward expansion.",
        "gate" => "Reduce quiet audio using a noise gate with hold, range and hysteresis.",
        _ => ""
    };
    internal static bool MatchesSearch(string kind, string query)
        => (Title(kind) + " " + kind + " " + CategoryFor(kind).Title + " " + Description(kind)
            + (kind == "ducking" ? " sidechain music voice trigger" : "")).Contains(query, System.StringComparison.OrdinalIgnoreCase);
    internal static string HeaderBrushKey(string kind) => CategoryFor(kind).HeaderBrushKey;
    internal static Brush HeaderBrush(string kind) => CategoryFor(kind).HeaderBrush;
}
