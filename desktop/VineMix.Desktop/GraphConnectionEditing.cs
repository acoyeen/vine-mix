using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace VineMix.Desktop;

/// <summary>Validated, replay-safe cable edits. Preparing a plan never changes a graph.</summary>
internal static class GraphConnectionEditing
{
    internal sealed record Plan(string NodeId, string NodeKind,
        IReadOnlyList<GraphEdge> Incident, IReadOnlyList<GraphEdge> Removed, IReadOnlyList<GraphEdge> Added);
    internal sealed record EdgePlan(IReadOnlyList<GraphEdge> Removed, IReadOnlyList<GraphEdge> Added);

    internal static bool TryPrepareRemoval(GraphConfiguration graph, IEnumerable<GraphEdge> originals, out EdgePlan? plan, out string error)
    {
        plan = new(originals.GroupBy(e => e.Id).Select(group => group.First().Copy()).ToArray(), []);
        if (plan.Removed.Count == 0) { plan = null; error = "No cables were selected."; return false; }
        if (ValidateEdges(graph, plan, out error)) return true;
        plan = null; return false;
    }
    internal static bool TryPrepareRewire(GraphConfiguration graph, GraphEdge original, string destination, out EdgePlan? plan, out string error)
    {
        var replacement = original.Copy(); replacement.To = destination;
        plan = new([original.Copy()], [replacement]);
        if (ValidateEdges(graph, plan, out error)) return true;
        plan = null; return false;
    }
    internal static bool TryApply(GraphConfiguration graph, EdgePlan plan, out string error)
    {
        // Rewiring deliberately preserves the edge ID, its level and its mute.
        // Recognize both removal and replacement acknowledgements on replay.
        if (plan.Removed.All(old => graph.Edges.All(e => e.Id != old.Id)
                || plan.Added.Any(added => added.Id == old.Id && graph.Edges.Any(e => SameEdge(e, added))))
            && plan.Added.All(added => graph.Edges.Any(e => SameEdge(e, added))))
        { error = ""; return true; }
        if (!ValidateEdges(graph, plan, out error)) return false;
        var removed = plan.Removed.Select(e => e.Id).ToHashSet();
        graph.Edges = graph.Edges.Where(e => !removed.Contains(e.Id)).Concat(plan.Added.Select(e => e.Copy())).ToList();
        return true;
    }
    private static bool ValidateEdges(GraphConfiguration graph, EdgePlan plan, out string error)
    {
        var removed = plan.Removed.Select(e => e.Id).ToHashSet();
        if (plan.Removed.Any(old => !graph.Edges.Any(e => SameEdge(e, old)))
            || plan.Added.Any(added => graph.Edges.Any(e => e.Id == added.Id && !removed.Contains(e.Id))))
        { error = "A cable changed during this gesture. Its current connections were kept."; return false; }
        error = GraphDefaults.Validate(EdgeOnlyCopy(graph, graph.Edges.Where(e => !removed.Contains(e.Id)).Concat(plan.Added)));
        return error.Length == 0;
    }

    internal static double DistanceToPolyline(IReadOnlyList<Point> points, Point point)
    {
        double distance = double.PositiveInfinity;
        for (int i = 1; i < points.Count; i++) distance = Math.Min(distance, SegmentDistanceSquared(points[i - 1], points[i], point));
        return Math.Sqrt(distance);
    }
    internal static bool PolylineIntersectsStroke(IReadOnlyList<Point> points, Point from, Point to, double tolerance)
    {
        double limit = tolerance * tolerance;
        for (int i = 1; i < points.Count; i++)
        {
            Point a = points[i - 1], b = points[i];
            double first = Vector.CrossProduct(b - a, from - a), second = Vector.CrossProduct(b - a, to - a);
            double third = Vector.CrossProduct(to - from, a - from), fourth = Vector.CrossProduct(to - from, b - from);
            if ((first < 0 && second > 0 || first > 0 && second < 0) && (third < 0 && fourth > 0 || third > 0 && fourth < 0)) return true;
            if (Math.Min(Math.Min(SegmentDistanceSquared(a, b, from), SegmentDistanceSquared(a, b, to)),
                    Math.Min(SegmentDistanceSquared(from, to, a), SegmentDistanceSquared(from, to, b))) <= limit) return true;
        }
        return false;
    }
    private static double SegmentDistanceSquared(Point from, Point to, Point point)
    {
        Vector direction = to - from;
        double fraction = direction.LengthSquared > 0 ? Math.Clamp(Vector.Multiply(point - from, direction) / direction.LengthSquared, 0, 1) : 0;
        return (point - (from + direction * fraction)).LengthSquared;
    }

    internal static bool IsInsertable(GraphNode node)
        => GraphDefaults.HasAudioInput(node.Kind) && GraphDefaults.HasAudioOutput(node.Kind);

    internal static string InsertionError(GraphConfiguration graph, string nodeId, string edgeId)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        var edge = graph.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (node is null || !IsInsertable(node)) return "Only processing, mix and reroute nodes can be inserted into an audio cable.";
        // A detector input is not an audible path. Keep a preconnected trigger
        // when inserting, but still check its dependency when testing cycles.
        if (graph.Edges.Any(e => (e.From == nodeId || e.To == nodeId) && !(e.To == nodeId && e.Kind == "sidechain")))
            return "Detach this node's existing audio or linked-control connections before inserting it into a cable.";
        if (edge is null || edge.Kind != "audio") return "Drop the node onto an audio cable.";
        if (graph.Edges.Count >= 96) return "Inserting a node needs one spare connection slot (96 maximum).";
        var candidate = EdgeOnlyCopy(graph, graph.Edges.Where(e => e.Id != edgeId));
        var incoming = new GraphEdge { From = edge.From, To = nodeId, GainDb = edge.GainDb, Muted = edge.Muted };
        string error = GraphDefaults.ConnectionError(candidate, incoming);
        if (error.Length > 0) return error;
        candidate.Edges.Add(incoming);
        return GraphDefaults.ConnectionError(candidate, new GraphEdge { From = nodeId, To = edge.To });
    }

    internal static bool TryPrepareInsertion(GraphConfiguration graph, string nodeId, string edgeId, out Plan? plan, out string error)
    {
        plan = null; error = InsertionError(graph, nodeId, edgeId);
        if (error.Length > 0) return false;
        var node = graph.Nodes.Single(n => n.Id == nodeId);
        var edge = graph.Edges.Single(e => e.Id == edgeId);
        plan = new(nodeId, node.Kind, graph.Edges.Where(e => e.From == nodeId || e.To == nodeId).Select(e => e.Copy()).ToArray(), [edge.Copy()],
            [new() { Id = Guid.NewGuid().ToString("N"), From = edge.From, To = nodeId, GainDb = edge.GainDb, Muted = edge.Muted },
             new() { Id = Guid.NewGuid().ToString("N"), From = nodeId, To = edge.To }]);
        return ValidatePlan(graph, plan, out error);
    }

    internal static bool TryPrepareDetach(GraphConfiguration graph, string nodeId, out Plan? plan, out string error)
    {
        plan = null;
        var node = graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || !IsInsertable(node)) { error = "Detach applies to a processing, mix or reroute node between audio connections."; return false; }
        var incident = graph.Edges.Where(e => e.From == nodeId || e.To == nodeId).ToArray();
        if (incident.Any(e => e.Kind != "audio")) { error = "This node has a linked control or detector cable. Remove those connections before detaching its audio path; no cables were changed."; return false; }
        var inputs = incident.Where(e => e.To == nodeId).ToArray();
        var outputs = incident.Where(e => e.From == nodeId).ToArray();
        if (inputs.Length != 1 || outputs.Length == 0)
        { error = "Detach needs exactly one incoming audio cable and at least one outgoing cable; these connections are unchanged."; return false; }
        var added = new List<GraphEdge>();
        foreach (var output in outputs)
        {
            // The node keeps its own settings and its processing leaves the
            // path with it. Baking those settings into the bridge would apply
            // them twice on reinsertion and accumulate attenuation or mute.
            double gain = inputs[0].GainDb + output.GainDb;
            if (!AudioLevel.IsValidGain(gain))
            { error = "The combined path level cannot fit in one cable's range. Detach left the connections unchanged."; return false; }
            added.Add(new() { Id = Guid.NewGuid().ToString("N"), From = inputs[0].From, To = output.To,
                GainDb = gain, Muted = inputs[0].Muted || output.Muted });
        }
        plan = new(node.Id, node.Kind, incident.Select(e => e.Copy()).ToArray(),
            incident.Select(e => e.Copy()).ToArray(), added);
        return ValidatePlan(graph, plan, out error);
    }

    internal static bool TryApply(GraphConfiguration graph, Plan plan, out string error)
    {
        // Edits can be replayed after a configure acknowledgement. Keep the same
        // generated IDs and recognize an already-applied plan without appending.
        if (plan.Removed.All(old => graph.Edges.All(e => e.Id != old.Id))
            && plan.Added.All(added => graph.Edges.Any(e => SameEdge(e, added))))
        { error = ""; return true; }
        if (!ValidatePlan(graph, plan, out error)) return false;
        var removed = plan.Removed.Select(e => e.Id).ToHashSet();
        graph.Edges = graph.Edges.Where(e => !removed.Contains(e.Id)).Concat(plan.Added.Select(e => e.Copy())).ToList();
        return true;
    }

    private static bool ValidatePlan(GraphConfiguration graph, Plan plan, out string error)
    {
        var node = graph.Nodes.FirstOrDefault(n => n.Id == plan.NodeId);
        if (node is null || node.Kind != plan.NodeKind)
        { error = "The node changed while editing; its connections were left unchanged."; return false; }
        var incident = graph.Edges.Where(e => e.From == plan.NodeId || e.To == plan.NodeId).ToArray();
        if (incident.Length != plan.Incident.Count || incident.Any(e => !plan.Incident.Any(expected => SameEdge(e, expected)))
            || plan.Removed.Any(old => !graph.Edges.Any(e => SameEdge(e, old)))
            || plan.Added.Any(added => graph.Edges.Any(e => e.Id == added.Id)))
        { error = "The cables changed while editing; no connections were replaced."; return false; }
        var removed = plan.Removed.Select(e => e.Id).ToHashSet();
        var candidate = EdgeOnlyCopy(graph, graph.Edges.Where(e => !removed.Contains(e.Id)).Concat(plan.Added));
        error = GraphDefaults.Validate(candidate);
        return error.Length == 0;
    }
    private static GraphConfiguration EdgeOnlyCopy(GraphConfiguration graph, IEnumerable<GraphEdge> edges)
        => new() { Nodes = graph.Nodes, Edges = edges.ToList(), PanX = graph.PanX, PanY = graph.PanY, Zoom = graph.Zoom };
    private static bool SameEdge(GraphEdge first, GraphEdge second)
        => first.Id == second.Id && first.From == second.From && first.To == second.To && first.Kind == second.Kind
            && first.GainDb == second.GainDb && first.Muted == second.Muted;
}
