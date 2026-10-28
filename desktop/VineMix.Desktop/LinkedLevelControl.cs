using System;
using System.Linq;

namespace VineMix.Desktop;

/// <summary>Shared-volume presentation policy; older audio routes remain unchanged.</summary>
internal static class LinkedLevelControl
{
    internal static bool SupportsInput(string kind) => kind is "microphone" or "virtualInput" or "appInput"
        or "output" or "virtualOutput" or "mix" or "gain" or "reroute";

    internal static bool HasLegacyConnection(GraphConfiguration graph, GraphNode node)
        => !SupportsInput(node.Kind) && node.Kind != "control"
            && graph.Edges.Any(edge => edge.Kind == "control" && edge.To == node.Id);

    internal static bool ShowInput(GraphConfiguration graph, GraphNode node)
        => SupportsInput(node.Kind) || HasLegacyConnection(graph, node);

    internal static double GetPercent(GraphNode node)
        => node.ControlMultiplier >= 0 ? node.ControlMultiplier * 100 : 100 * Math.Pow(10, node.GainDb / 20);

    internal static bool HasLegacyValue(GraphNode node)
        => node.ControlMultiplier < 0 && GetPercent(node) > 200;

    internal static void SetPercent(GraphNode node, double percent)
    {
        if (node.Kind != "control" || !double.IsFinite(percent) || percent < 0 || percent > 200)
            throw new ArgumentOutOfRangeException(nameof(percent), "A linked multiplier must be between 0 and 200%.");
        node.ControlMultiplier = percent / 100;
        node.GainDb = 0;
        // An explicit user adjustment replaces an old controller mute. Merely
        // reading or displaying a saved value never unmutes or migrates it.
        node.Muted = false;
    }
}
