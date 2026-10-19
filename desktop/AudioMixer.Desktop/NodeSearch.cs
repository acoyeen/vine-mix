using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioMixer.Desktop;

internal sealed record NodeSearchResult(string Id, string Name, string Detail);

/// <summary>Searches one current graph snapshot without changing its nodes or view.</summary>
internal static class NodeSearch
{
    internal static IReadOnlyList<NodeSearchResult> Match(GraphConfiguration graph, string? query)
    {
        ArgumentNullException.ThrowIfNull(graph);
        string[] terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var results = new List<NodeSearchResult>();
        foreach (var node in graph.Nodes)
        {
            string kind = GraphDefaults.KindLabel(node.Kind);
            string name = string.IsNullOrWhiteSpace(node.Name) ? kind : node.Name;
            string source = AssignedName(node).Trim();
            if (!terms.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || kind.Contains(term, StringComparison.OrdinalIgnoreCase)
                || source.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            string detail = source.Length > 0 && !string.Equals(source, name.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(source, kind, StringComparison.OrdinalIgnoreCase) ? $"{kind} · {source}" : kind;
            results.Add(new(node.Id, name, detail));
        }
        return results.AsReadOnly();
    }

    private static string AssignedName(GraphNode node) => node.Kind switch
    {
        "appInput" when node.App.ProcessId != 0 || node.App.ExecutablePath.Length > 0 => node.App.Name,
        "microphone" or "virtualInput" or "output" or "virtualOutput"
            when VirtualDeviceBindings.IsAssigned(node.Device) => node.Device.Name,
        _ => ""
    };
}
