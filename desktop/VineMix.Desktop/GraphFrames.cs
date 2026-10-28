using System;
using System.Collections.Generic;
using System.Linq;

namespace VineMix.Desktop;

/// <summary>Desktop-only organization. A frame has no audio ports or processing state.</summary>
public sealed class GraphFrame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Group";
    public string ColorKey { get; set; } = "sage";
    public List<string> NodeIds { get; set; } = [];
    public GraphFrame Copy() => new() { Id = Id, Name = Name, ColorKey = ColorKey, NodeIds = [.. NodeIds] };
}

internal static class GraphFrames
{
    internal const int MaximumNameLength = 80;
    internal const int MaximumFrames = 32;

    internal static string NormalizeName(string? name)
    {
        string text = new((name ?? "").Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        text = text.Trim();
        if (text.Length == 0) return "Group";
        if (text.Length <= MaximumNameLength) return text;
        int length = MaximumNameLength;
        if (char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].TrimEnd();
    }

    internal static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id)
        && id.Length <= 64 && !id.Any(char.IsControl);

    // The first valid membership wins. This makes malformed/older preferences
    // deterministic and prevents nested or ambiguous moving groups. A null
    // node set is used only while preferences load before the engine snapshot.
    internal static List<GraphFrame> Normalize(IEnumerable<GraphFrame>? frames, IEnumerable<string>? nodeIds = null)
    {
        var available = nodeIds?.ToHashSet(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GraphFrame>();
        foreach (var frame in frames ?? [])
        {
            if (result.Count == MaximumFrames) break;
            if (frame is null || !ValidId(frame.Id) || !identities.Add(frame.Id)) continue;
            var included = (frame.NodeIds ?? []).Where(id => ValidId(id) && (available is null || available.Contains(id))
                && !members.Contains(id)).Distinct(StringComparer.Ordinal).Take(32).ToList();
            if (included.Count == 0) continue;
            members.UnionWith(included);
            result.Add(new GraphFrame
            {
                Id = frame.Id, Name = NormalizeName(frame.Name),
                ColorKey = GraphFramePalette.NormalizeKey(frame.ColorKey), NodeIds = included
            });
        }
        return result;
    }

    internal static void Normalize(GraphConfiguration graph)
        => graph.Frames = Normalize(graph.Frames, graph.Nodes.Select(node => node.Id));

    internal static bool Same(IReadOnlyList<GraphFrame> left, IReadOnlyList<GraphFrame> right)
        => WorkspaceHistory.SameValue(left, right);
}
