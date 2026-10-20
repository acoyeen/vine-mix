using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace AudioMixer.Desktop;

internal sealed record GraphPaste(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, int ClearedAssignments);

// A private, versioned clipboard format. This class never reads the system
// clipboard or edits a graph; the caller commits one complete, validated paste.
internal static class GraphClipboard
{
    internal const string FormatName = "AudioMixer.NodeGroup.v1";
    internal const int MaximumBytes = 256 * 1024;
    private const int MaximumNodes = 32;
    private const int MaximumEdges = 96;
    private const double MaximumPosition = 100000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new(Protocol.Json)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    private sealed class NodeGroup
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = 1;
        public List<GraphNode> Nodes { get; set; } = [];
        public List<GraphEdge> Edges { get; set; } = [];
    }

    internal static string Serialize(GraphConfiguration graph, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.Ordinal);
        var nodes = graph.Nodes.Where(n => selected.Contains(n.Id)).ToList();
        if (nodes.Count == 0) return "";
        var included = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        return JsonSerializer.Serialize(new NodeGroup
        {
            Nodes = nodes,
            Edges = graph.Edges.Where(e => included.Contains(e.From) && included.Contains(e.To)).ToList()
        }, Json);
    }

    internal static bool TryCreatePaste(string json, GraphConfiguration target, Point anchor, out GraphPaste paste, out string error)
    {
        paste = new GraphPaste([], [], 0);
        error = "The clipboard does not contain a valid Vine Mix node group.";
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            if (json.Length > MaximumBytes || Utf8.GetByteCount(json) > MaximumBytes)
            {
                error = "The copied node group is too large.";
                return false;
            }
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (!ValidShape(document.RootElement)) return false;
            var group = JsonSerializer.Deserialize<NodeGroup>(json, Json);
            if (group is null || group.Format != FormatName || group.Version != 1) return false;
            if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y))
            {
                error = "Choose a valid position on the canvas.";
                return false;
            }
            if (group.Nodes.Count == 0)
            {
                error = "The copied group contains no nodes.";
                return false;
            }
            if (target.Nodes.Count + group.Nodes.Count > MaximumNodes)
            {
                error = "Pasting this group would exceed the workspace limit of 32 nodes.";
                return false;
            }
            if (target.Edges.Count + group.Edges.Count > MaximumEdges)
            {
                error = "Pasting this group would exceed the workspace limit of 96 connections.";
                return false;
            }
            var source = new GraphConfiguration { Nodes = group.Nodes, Edges = group.Edges };
            error = ValidateValues(source);
            if (error.Length > 0) return false;
            error = GraphDefaults.Validate(source);
            if (error.Length > 0) return false;

            // Deserializing already gives detached copies of all nested settings.
            // Only internal edges were serialized, so no new route reaches an
            // original node. Input copies retain their source identity and own
            // independent controls. Outputs keep exclusive endpoint ownership;
            // clear only conflicting output assignments for the user to choose.
            double left = group.Nodes.Min(n => n.X), top = group.Nodes.Min(n => n.Y);
            var reservedNodes = target.Nodes.Concat(group.Nodes).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var reservedEdges = target.Edges.Concat(group.Edges).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var remap = new Dictionary<string, string>(StringComparer.Ordinal);
            var assigned = new List<GraphNode>(target.Nodes);
            int clearedAssignments = 0;
            foreach (var node in group.Nodes)
            {
                string originalId = node.Id;
                node.Id = NewId(reservedNodes);
                remap.Add(originalId, node.Id);
                node.X = anchor.X + (node.X - left);
                node.Y = anchor.Y + (node.Y - top);
                if (assigned.Any(existing => ConflictingDevice(existing, node)))
                {
                    node.Device = new DeviceBinding { Channel = node.Device.Channel };
                    ++clearedAssignments;
                }
                assigned.Add(node);
            }
            foreach (var edge in group.Edges)
            {
                edge.Id = NewId(reservedEdges);
                edge.From = remap[edge.From];
                edge.To = remap[edge.To];
            }
            var combined = new GraphConfiguration
            {
                Nodes = assigned,
                Edges = [.. target.Edges, .. group.Edges],
                PanX = target.PanX, PanY = target.PanY, Zoom = target.Zoom
            };
            error = ValidateValues(combined);
            if (error.Length > 0) return false;
            error = GraphDefaults.Validate(combined);
            if (error.Length > 0) return false;
            paste = new GraphPaste(group.Nodes.AsReadOnly(), group.Edges.AsReadOnly(), clearedAssignments);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or EncoderFallbackException)
        {
            error = "The clipboard does not contain a valid Vine Mix node group.";
            return false;
        }
    }

    // Pending edits can be replayed against a newer engine revision. Recheck
    // the whole addition before mutating that revision, keeping the same IDs so
    // later edits still refer to these copies. A previously applied complete
    // group is an idempotent success; a partial identity collision is rejected.
    internal static bool TryAppend(GraphConfiguration target, GraphPaste paste, out string error)
    {
        error = "The copied node group is invalid.";
        if (paste.Nodes.Count == 0) return false;
        var additions = new GraphConfiguration { Nodes = paste.Nodes.ToList(), Edges = paste.Edges.ToList() };
        try
        {
            error = ValidateValues(additions);
            if (error.Length > 0) return false;
            error = GraphDefaults.Validate(additions);
            if (error.Length > 0) return false;
            error = ValidateValues(target);
            if (error.Length > 0) return false;
            error = GraphDefaults.Validate(target);
            if (error.Length > 0) return false;

            var nodeIds = target.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            var edgeIds = target.Edges.Select(edge => edge.Id).ToHashSet(StringComparer.Ordinal);
            bool allNodesPresent = additions.Nodes.All(node => nodeIds.Contains(node.Id));
            bool allEdgesPresent = additions.Edges.All(edge => edgeIds.Contains(edge.Id));
            if (allNodesPresent && allEdgesPresent) { error = ""; return true; }
            if (additions.Nodes.Any(node => nodeIds.Contains(node.Id)) || additions.Edges.Any(edge => edgeIds.Contains(edge.Id)))
            {
                error = "The workspace changed while pasting. Copy the group again.";
                return false;
            }
            if (target.Nodes.Count + additions.Nodes.Count > MaximumNodes || target.Edges.Count + additions.Edges.Count > MaximumEdges)
            {
                error = "The workspace changed and no longer has room for the whole group (32 nodes, 96 connections).";
                return false;
            }

            var copiedNodes = additions.Nodes.Select(node => node.Copy()).ToList();
            var copiedEdges = additions.Edges.Select(edge => edge.Copy()).ToList();
            var assigned = new List<GraphNode>(target.Nodes);
            foreach (var node in copiedNodes)
            {
                if (assigned.Any(existing => ConflictingDevice(existing, node)))
                    node.Device = new DeviceBinding { Channel = node.Device.Channel };
                assigned.Add(node);
            }
            var combined = new GraphConfiguration
            {
                Nodes = assigned, Edges = [.. target.Edges, .. copiedEdges],
                PanX = target.PanX, PanY = target.PanY, Zoom = target.Zoom
            };
            error = ValidateValues(combined);
            if (error.Length > 0) return false;
            error = GraphDefaults.Validate(combined);
            if (error.Length > 0) return false;
            target.Nodes.AddRange(copiedNodes);
            target.Edges.AddRange(copiedEdges);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or EncoderFallbackException)
        {
            error = "The copied node group is invalid.";
            return false;
        }
    }

    private static string NewId(HashSet<string> reserved)
    {
        string value;
        do { value = Guid.NewGuid().ToString("N"); } while (!reserved.Add(value));
        return value;
    }

    private static bool ConflictingDevice(GraphNode first, GraphNode second)
    {
        bool render = (first.Kind is "output" or "virtualOutput") && (second.Kind is "output" or "virtualOutput");
        return render && ((second.Device.Id.Length > 0 && first.Device.Id == second.Device.Id)
            || (second.Device.StableId.Length > 0 && first.Device.StableId == second.Device.StableId));
    }

    private static string ValidateValues(GraphConfiguration graph)
    {
        foreach (var node in graph.Nodes)
        {
            if (node is null || !ValidId(node.Id) || !ValidText(node.Name, 256) || !GraphDefaults.Kinds.Contains(node.Kind))
                return "A copied node has an invalid name or type.";
            if (!InRange(node.X, -MaximumPosition, MaximumPosition) || !InRange(node.Y, -MaximumPosition, MaximumPosition))
                return "The pasted group would extend beyond the canvas position limit. Paste closer to the centre.";
            var device = node.Device;
            var app = node.App;
            var compressor = node.Compressor;
            if (device is null || !ValidText(device.Id, 4096) || !ValidText(device.StableId, 4096)
                || !ValidText(device.Name, 1024) || device.Channel is < -1 or > 255)
                return "A copied node has an invalid device assignment.";
            if (app is null || !ValidText(app.Name, 1024) || !ValidText(app.ExecutablePath, 32768)
                || (node.Kind == "appInput" && app.ProcessId != 0 && app.ExecutablePath.Length == 0))
                return "A copied node has an invalid application assignment.";
            if (!InRange(node.Balance, -1, 1) || compressor is null
                || !InRange(compressor.ThresholdDb, -60, 0) || !InRange(compressor.Ratio, 1, 20)
                || !InRange(compressor.AttackMs, .1, 200) || !InRange(compressor.ReleaseMs, 5, 2000)
                || !InRange(compressor.MakeupDb, 0, 24))
                return "A copied node has an invalid balance or compressor setting.";
        }
        foreach (var edge in graph.Edges)
            if (edge is null || !ValidId(edge.Id) || !ValidId(edge.From) || !ValidId(edge.To))
                return "A copied connection has an invalid identity.";
        return "";
    }

    private static bool InRange(double value, double minimum, double maximum) => double.IsFinite(value) && value >= minimum && value <= maximum;
    private static bool ValidText(string? text, int maximumBytes) => text is not null && Utf8.GetByteCount(text) <= maximumBytes;
    private static bool ValidId(string? id) => !string.IsNullOrWhiteSpace(id) && ValidText(id, 64) && !id.Any(c => c < 32);

    private static bool Properties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var remaining = names.ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!remaining.Remove(property.Name)) return false;
        return remaining.Count == 0;
    }

    private static bool ValidShape(JsonElement root)
    {
        if (!Properties(root, "format", "version", "nodes", "edges")) return false;
        var nodes = root.GetProperty("nodes");
        var edges = root.GetProperty("edges");
        if (nodes.ValueKind != JsonValueKind.Array || edges.ValueKind != JsonValueKind.Array
            || nodes.GetArrayLength() > MaximumNodes || edges.GetArrayLength() > MaximumEdges) return false;
        foreach (var node in nodes.EnumerateArray())
        {
            string[] fields = ["id", "kind", "name", "x", "y", "gainDb", "muted", "device", "app", "bypass", "collapsed", "balance", "eqBands", "compressor"];
            if (node.ValueKind != JsonValueKind.Object) return false;
            foreach (var optional in new[] { "mono", "denoiser", "deEsser", "plosive", "reverb", "voiceColor", "expander", "alignmentDelay", "dynamicsUiVersion", "ducking" })
                if (node.TryGetProperty(optional, out _)) fields = [.. fields, optional];
            if (!Properties(node, fields)) return false;
            if (!Properties(node.GetProperty("device"), "id", "stableId", "name", "channel")
                || !Properties(node.GetProperty("app"), "processId", "executablePath", "name")) return false;
            var compressor = node.GetProperty("compressor");
            if (compressor.ValueKind != JsonValueKind.Object) return false;
            string[] compressorFields = ["thresholdDb", "ratio", "attackMs", "releaseMs", "makeupDb"];
            foreach (var optional in new[] { "mode", "detector", "ceilingDb" })
                if (compressor.TryGetProperty(optional, out _)) compressorFields = [.. compressorFields, optional];
            if (!Properties(compressor, compressorFields)) return false;
            if (node.TryGetProperty("ducking", out var ducking))
            {
                if (ducking.ValueKind != JsonValueKind.Object) return false;
                string[] duckingFields = ["thresholdDb", "amountDb", "attackMs", "releaseMs"];
                foreach (var optional in new[] { "mode", "ratio" })
                    if (ducking.TryGetProperty(optional, out _)) duckingFields = [.. duckingFields, optional];
                if (!Properties(ducking, duckingFields)) return false;
            }
            if (node.TryGetProperty("denoiser", out var denoiser))
            {
                if (denoiser.ValueKind != JsonValueKind.Object) return false;
                string[] denoiserFields = ["suppressionDb"];
                foreach (var optional in new[] { "algorithm", "rnnoiseMix" })
                    if (denoiser.TryGetProperty(optional, out _)) denoiserFields = [.. denoiserFields, optional];
                if (!Properties(denoiser, denoiserFields)) return false;
            }
            if (node.TryGetProperty("deEsser", out var deEsser) && !Properties(deEsser, "frequencyHz", "thresholdDb", "reductionDb")) return false;
            if (node.TryGetProperty("plosive", out var plosive) && !Properties(plosive, "frequencyHz", "thresholdDb", "reductionDb")) return false;
            if (node.TryGetProperty("reverb", out var reverb) && !Properties(reverb, "mix", "decaySeconds", "damping", "preDelayMs")) return false;
            if (node.TryGetProperty("voiceColor", out var voiceColor) && !Properties(voiceColor, "tone", "ambience")) return false;
            if (node.TryGetProperty("expander", out var expander) && !Properties(expander, "mode", "thresholdDb", "ratio", "attackMs", "holdMs", "releaseMs", "rangeDb", "hysteresisDb")) return false;
            if (node.TryGetProperty("alignmentDelay", out var alignmentDelay) && !Properties(alignmentDelay, "samples")) return false;
            var bands = node.GetProperty("eqBands");
            if (bands.ValueKind != JsonValueKind.Array || bands.GetArrayLength() > GraphDefaults.MaximumEqBands) return false;
            foreach (var band in bands.EnumerateArray())
            {
                if (band.ValueKind != JsonValueKind.Object) return false;
                string[] bandFields = ["type", "enabled", "frequencyHz", "gainDb", "q"];
                if (band.TryGetProperty("resetFrequencyHz", out _)) bandFields = [.. bandFields, "resetFrequencyHz"];
                if (!Properties(band, bandFields)) return false;
            }
        }
        foreach (var edge in edges.EnumerateArray())
            if (!Properties(edge, "id", "from", "to", "kind", "gainDb", "muted")) return false;
        return true;
    }
}
