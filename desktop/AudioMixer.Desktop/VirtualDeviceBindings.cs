using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioMixer.Desktop;

/// <summary>Identity-based defaults for the two supported virtual transports.</summary>
internal static class VirtualDeviceBindings
{
    internal sealed record Assignment(string NodeId, string Kind, string Id, string StableId, string Name)
    {
        internal string AttemptKey => string.Join('\0', NodeId, Kind, Id, StableId);
        internal Endpoint Endpoint => new() { Id = Id, StableId = StableId };
        internal DeviceBinding Binding => new() { Id = Id, StableId = StableId, Name = Name, Channel = -1 };
    }

    internal static bool IsVirtual(string kind) => kind is "virtualInput" or "virtualOutput";
    internal static bool IsAssigned(DeviceBinding binding) => binding.Id.Length > 0 || binding.StableId.Length > 0;
    internal static bool Matches(string kind, Endpoint endpoint) => kind switch
    {
        "virtualInput" => endpoint.Direction == "capture" && endpoint.Transport == "vbCable",
        "virtualOutput" => endpoint.Direction == "render" && endpoint.Transport == "vacLite",
        _ => false
    };
    internal static bool Same(DeviceBinding binding, Endpoint endpoint)
        => binding.Id.Length > 0 && binding.Id == endpoint.Id
            || binding.StableId.Length > 0 && binding.StableId == endpoint.StableId;

    internal static bool IsUsed(GraphConfiguration graph, string nodeId, Endpoint endpoint)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        // Sources can feed independent input nodes. A render endpoint still has
        // one output owner so separate node workers cannot contend for it.
        if (node?.Kind is not ("output" or "virtualOutput")) return false;
        return graph.Nodes.Any(other => other.Id != nodeId
            && (other.Kind is "output" or "virtualOutput")
            && Same(other.Device, endpoint));
    }

    internal static IReadOnlyList<Assignment> FindAutomaticAssignments(GraphConfiguration graph, IReadOnlyList<Endpoint> endpoints)
    {
        var assignments = new List<Assignment>();
        foreach (var node in graph.Nodes)
        {
            // A saved identity remains authoritative even while its device is
            // missing or a reinstall has introduced a similarly named endpoint.
            if (!IsVirtual(node.Kind) || IsAssigned(node.Device)) continue;
            var candidates = endpoints.Where(e => Matches(node.Kind, e) && e.State == "Active"
                && (e.Id.Length > 0 || e.StableId.Length > 0)).Take(2).ToArray();
            if (candidates.Length != 1) continue;
            var endpoint = candidates[0];
            if (IsUsed(graph, node.Id, endpoint)
                || (node.Kind == "virtualOutput" && assignments.Any(a => a.Kind == node.Kind && Same(a.Binding, endpoint)))) continue;
            assignments.Add(new(node.Id, node.Kind, endpoint.Id, endpoint.StableId, endpoint.Name));
        }
        return assignments;
    }

    internal static int Apply(GraphConfiguration graph, IReadOnlyList<Assignment> assignments)
    {
        int changed = 0;
        foreach (var assignment in assignments)
        {
            var node = graph.Nodes.FirstOrDefault(n => n.Id == assignment.NodeId && n.Kind == assignment.Kind);
            // Pending edits can be rebased over a newer configuration. Do not
            // replace an intervening manual choice or claim an occupied output.
            if (node is null || IsAssigned(node.Device) || IsUsed(graph, node.Id, assignment.Endpoint)) continue;
            node.Device = assignment.Binding;
            changed++;
        }
        return changed;
    }
}
