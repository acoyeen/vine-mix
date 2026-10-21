using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace VineMix.Desktop;

/// <summary>Pure alignment planning checks; no UI, settings, engine or process access.</summary>
internal static class GraphNodeAlignmentVerification
{
    internal static void Run(Action<bool, string> require)
    {
        var graph = new GraphConfiguration
        {
            Nodes =
            [
                new() { Id = "processor", Kind = "eq", Name = "Voice tone", X = 125.375, Y = -64.125, Bypass = true },
                new() { Id = "input", Kind = "microphone", Name = "Voice", X = -200.625, Y = 48.375, GainDb = -5.5,
                    Mono = true, Device = new() { Id = "capture", StableId = "capture-stable", Name = "Studio mic" } },
                new() { Id = "output", Kind = "output", Name = "Monitor", X = 42.5, Y = -310.875, Muted = true,
                    Device = new() { Id = "render", Name = "Headphones" } },
                new() { Id = "unselected", Kind = "mix", Name = "Other path", X = -900.25, Y = -1000.5, GainDb = -3 }
            ],
            PanX = -143.25, PanY = 87.5, Zoom = .75
        };
        var sizes = new Dictionary<string, Size>(StringComparer.Ordinal)
        {
            ["processor"] = new(620, 480.5), ["input"] = new(356, 220.25), ["output"] = new(356, 190.125)
        };
        string before = JsonSerializer.Serialize(graph, Protocol.Json);
        string[] selected = ["output", "input", "processor", "input", "missing", "", "PROCESSOR"];
        string[] originalSelection = selected.ToArray();
        var originalSizes = sizes.ToArray();
        var left = GraphNodeAlignment.Plan(graph, selected, NodeAlignment.Left, sizes);
        require(left.Keys.SequenceEqual(["processor", "input", "output"])
            && left["processor"] == new Point(-200.625, -72.75)
            && left["input"] == new Point(-200.625, 455.75)
            && left["output"] == new Point(-200.625, -310.875),
            "Left alignment anchors the earliest Y and normalizes both large gaps and overlaps to full rendered heights plus 48.");
        var top = GraphNodeAlignment.Plan(graph, selected, NodeAlignment.Top, sizes);
        require(top.Keys.SequenceEqual(["processor", "input", "output"])
            && top["processor"] == new Point(607.375, -310.875)
            && top["input"] == new Point(-200.625, -310.875)
            && top["output"] == new Point(203.375, -310.875),
            "Top alignment preserves the earliest X and places mixed-width nodes to its right without overlapping.");
        require(!left.ContainsKey("unselected") && !top.ContainsKey("unselected")
            && selected.SequenceEqual(originalSelection) && sizes.SequenceEqual(originalSizes)
            && JsonSerializer.Serialize(graph, Protocol.Json) == before,
            "Planning ignores stale, duplicate and unselected IDs without changing source selection, dimensions, graph settings, routes or view.");

        var row = graph.Copy();
        foreach (var node in row.Nodes) node.Y = -100.125;
        var rowPlan = GraphNodeAlignment.Plan(row, selected, NodeAlignment.Left, sizes);
        require(rowPlan.Keys.SequenceEqual(["processor", "output"])
            && rowPlan["output"] == new Point(-200.625, 168.125)
            && rowPlan["processor"] == new Point(-200.625, 406.25),
            "Left alignment turns a same-row selection into a separated column in left-to-right order, retaining the first node unchanged.");
        var column = graph.Copy();
        foreach (var node in column.Nodes) node.X = 42.125;
        var columnPlan = GraphNodeAlignment.Plan(column, selected, NodeAlignment.Top, sizes);
        require(columnPlan.Keys.SequenceEqual(["processor", "input"])
            && columnPlan["processor"] == new Point(446.125, -310.875)
            && columnPlan["input"] == new Point(1114.125, -310.875),
            "Top alignment turns a same-column selection into a separated row in top-to-bottom order using each node's rendered width.");

        foreach (NodeAlignment alignment in new[] { NodeAlignment.Left, NodeAlignment.Top })
        {
            var aligned = Apply(graph, GraphNodeAlignment.Plan(graph, selected, alignment, sizes));
            require(GraphNodeAlignment.Plan(aligned, selected, alignment, sizes).Count == 0,
                "Repeating " + alignment + " alignment is a no-op after the first complete layout.");
            var spaced = graph.Copy();
            double[] positions = alignment == NodeAlignment.Left ? [-700.125, 1000.25, -1000.875] : [300.375, -1000.875, -400.125];
            for (int i = 0; i < 3; i++)
                if (alignment == NodeAlignment.Left) { spaced.Nodes[i].X = -200.625; spaced.Nodes[i].Y = positions[i]; }
                else { spaced.Nodes[i].X = positions[i]; spaced.Nodes[i].Y = -310.875; }
            var compactPlan = GraphNodeAlignment.Plan(spaced, selected, alignment, sizes);
            var compacted = Apply(spaced, compactPlan);
            bool vertical = alignment == NodeAlignment.Left;
            double Primary(GraphNode node) => vertical ? node.Y : node.X;
            var compactOrder = compacted.Nodes.Where(node => sizes.ContainsKey(node.Id)).OrderBy(Primary).ToArray();
            require(compactPlan.Count > 0 && Primary(compactOrder[0]) == positions.Min()
                && compactOrder.Zip(compactOrder.Skip(1), (first, second) => Primary(second) - Primary(first)
                    - (vertical ? sizes[first.Id].Height : sizes[first.Id].Width)).All(gap => gap == 48)
                && GraphNodeAlignment.Plan(compacted, selected, alignment, sizes).Count == 0,
                "Already aligned nodes with oversized gaps close to exactly 48 DIPs and repeat without creeping for " + alignment + ".");
            var tied = graph.Copy();
            foreach (var node in tied.Nodes) { node.X = 16.25; node.Y = -40.5; }
            var tiePlan = GraphNodeAlignment.Plan(tied, selected, alignment, sizes);
            require(tiePlan.Keys.SequenceEqual(["input", "output"])
                && tiePlan["input"] == (alignment == NodeAlignment.Left ? new Point(16.25, 488) : new Point(684.25, -40.5))
                && tiePlan["output"] == (alignment == NodeAlignment.Left ? new Point(16.25, 756.25) : new Point(1088.25, -40.5)),
                "Exact position ties retain graph order, independent of selection order, for " + alignment + ".");
            foreach (var ids in new string[][] { [], ["missing", "", " "], ["input"], ["input", "input", "missing"], ["INPUT", "input"] })
                require(GraphNodeAlignment.Plan(graph, ids, alignment, sizes).Count == 0,
                    "A lone surviving node or empty selection never moves for " + alignment + ".");
            require(GraphNodeAlignment.Plan(graph, selected, alignment).Count == 0
                && GraphNodeAlignment.Plan(graph, selected, alignment, new Dictionary<string, Size> { ["input"] = sizes["input"] }).Count == 0,
                "Missing rendered dimensions reject the whole " + alignment + " plan rather than guessing node sizes.");
            foreach (var invalid in new Size[] { Size.Empty, new(0, 200), new(300, 0), new(double.NaN, 200),
                new(300, double.NaN), new(double.PositiveInfinity, 200), new(300, double.PositiveInfinity) })
            {
                var badSizes = new Dictionary<string, Size>(sizes) { ["processor"] = invalid };
                require(GraphNodeAlignment.Plan(graph, selected, alignment, badSizes).Count == 0,
                    "Invalid rendered width or height rejects all changes for " + alignment + ".");
            }
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -100000.125, 100000.125 })
                foreach (bool invalidX in new[] { true, false })
                {
                    var malformed = graph.Copy(); var node = malformed.Nodes[0];
                    if (invalidX) node.X = invalid; else node.Y = invalid;
                    double x = node.X, y = node.Y;
                    require(GraphNodeAlignment.Plan(malformed, selected, alignment, sizes).Count == 0
                        && node.X.Equals(x) && node.Y.Equals(y),
                        "Nonfinite or out-of-range selected positions reject the whole " + alignment + " plan without mutation.");
                }
            var limit = graph.Copy();
            foreach (var node in limit.Nodes) { node.X = 99900; node.Y = 99900; }
            require(GraphNodeAlignment.Plan(limit, selected, alignment, sizes).Count == 0,
                "Spacing that would exceed the graph coordinate limit rejects the entire " + alignment + " plan.");
            var hugeSizes = sizes.ToDictionary(pair => pair.Key, _ => new Size(double.MaxValue, double.MaxValue));
            require(GraphNodeAlignment.Plan(graph, selected, alignment, hugeSizes).Count == 0,
                "Extreme finite dimensions cannot create overflowing or out-of-range " + alignment + " coordinates.");
            var boundary = graph.Copy();
            boundary.Nodes.RemoveAll(node => node.Id is not ("input" or "output"));
            boundary.Nodes[0].X = boundary.Nodes[0].Y = -100000;
            boundary.Nodes[1].X = boundary.Nodes[1].Y = 100000;
            var boundaryPlan = GraphNodeAlignment.Plan(boundary, selected, alignment, sizes);
            require(boundaryPlan.Count == 1 && boundaryPlan["output"] == (alignment == NodeAlignment.Left
                ? new Point(-100000, -99731.75) : new Point(-99596, -100000)),
                "Exact inclusive graph coordinate boundaries remain valid for " + alignment + ".");
        }
        var reversed = graph.Copy(); reversed.Nodes.Reverse();
        require(GraphNodeAlignment.Plan(reversed, selected.Reverse(), NodeAlignment.Left, sizes).Keys.SequenceEqual(["output", "input", "processor"]),
            "Changed positions are returned in current graph order even when their packing order differs.");
        var ambiguous = graph.Copy(); ambiguous.Nodes.Add(ambiguous.Nodes[0].Copy());
        require(GraphNodeAlignment.Plan(ambiguous, selected, NodeAlignment.Left, sizes).Count == 0
            && GraphNodeAlignment.Plan(new GraphConfiguration(), selected, NodeAlignment.Left, sizes).Count == 0
            && GraphNodeAlignment.Plan(graph, selected, (NodeAlignment)(-1), sizes).Count == 0
            && GraphNodeAlignment.Plan(graph, selected, (NodeAlignment)3, sizes).Count == 0,
            "Ambiguous IDs, empty graphs and unsupported alignment values never produce partial moves.");

        VerifyAuto(graph, selected, sizes, require);
        VerifySignalOrder(require);
        var snapshot = left.ToArray();
        var mutable = left as IDictionary<string, Point>; bool refused = false;
        try { if (mutable is not null) mutable["processor"] = new Point(1, 2); }
        catch (NotSupportedException) { refused = true; }
        require((mutable is null || refused) && left.SequenceEqual(snapshot), "Returned position plans remain read-only through dictionary interfaces.");
        require(JsonSerializer.Serialize(graph, Protocol.Json) == before && sizes.SequenceEqual(originalSizes),
            "Successful, rejected and repeated planning leave original graph settings and supplied dimensions untouched.");
        graph.Nodes[0].X = 999; graph.Nodes.RemoveAll(node => node.Id == "output"); sizes["input"] = new(1, 1);
        require(left.SequenceEqual(snapshot), "Plans retain their value snapshots after source graph or dimension changes.");
    }

    private static void VerifyAuto(GraphConfiguration graph, string[] selected, Dictionary<string, Size> sizes, Action<bool, string> require)
    {
        foreach (var fixture in new (string Name, Point[] Origins, NodeAlignment Expected)[]
        {
            ("row", [new(-50.25, 10), new(30.75, 10), new(120.5, 10)], NodeAlignment.Top),
            ("column", [new(25, -300.25), new(25, 40.5), new(25, 900.75)], NodeAlignment.Left),
            ("wide diagonal", [new(-100, -50), new(250, 25), new(500, 100)], NodeAlignment.Top),
            ("tall diagonal", [new(-50, -100), new(25, 250), new(100, 500)], NodeAlignment.Left),
            ("equal-span diagonal", [new(-100.25, -80.5), new(10.5, 30.25), new(100.75, 120.5)], NodeAlignment.Top),
            ("coincident nodes", [new(12.75, -9.5), new(12.75, -9.5), new(12.75, -9.5)], NodeAlignment.Top)
        })
        {
            var current = graph.Copy();
            for (int i = 0; i < fixture.Origins.Length; i++)
            { current.Nodes[i].X = fixture.Origins[i].X; current.Nodes[i].Y = fixture.Origins[i].Y; }
            // An unselected distant node must not steer the chosen axis.
            current.Nodes[3].X = fixture.Expected == NodeAlignment.Left ? 99999 : 0;
            current.Nodes[3].Y = fixture.Expected == NodeAlignment.Top ? 99999 : 0;
            string before = JsonSerializer.Serialize(current, Protocol.Json);
            var actual = GraphNodeAlignment.Plan(current, selected, NodeAlignment.Auto, sizes);
            require(GraphNodeAlignment.ResolveAuto(current, selected.Reverse()) == fixture.Expected
                && actual.SequenceEqual(GraphNodeAlignment.Plan(current, selected, fixture.Expected, sizes)),
                "Auto chooses " + fixture.Expected + " for " + fixture.Name + " from selected origins only and matches explicit alignment exactly.");
            var aligned = Apply(current, actual);
            require(actual.Count > 0 && GraphNodeAlignment.Plan(aligned, selected, NodeAlignment.Auto, sizes).Count == 0
                && JsonSerializer.Serialize(current, Protocol.Json) == before,
                "Auto is idempotent for " + fixture.Name + " and does not change its source graph.");
            var skewedSizes = new Dictionary<string, Size>(sizes) { ["processor"] = new(12000, 18000) };
            require(GraphNodeAlignment.Plan(current, selected, NodeAlignment.Auto, skewedSizes)
                .SequenceEqual(GraphNodeAlignment.Plan(current, selected, fixture.Expected, skewedSizes)),
                "A much larger EQ body changes spacing without biasing the Auto orientation for " + fixture.Name + ".");
        }
        foreach (var ids in new string[][] { [], ["missing"], ["input", "input", "missing"], ["INPUT", "input"] })
            require(GraphNodeAlignment.ResolveAuto(graph, ids) == NodeAlignment.Top
                && GraphNodeAlignment.Plan(graph, ids, NodeAlignment.Auto, sizes).Count == 0,
                "Invalid or stale Auto selections fall back predictably but never move a lone surviving node.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -100001, 100001 })
            foreach (bool invalidX in new[] { true, false })
            {
                var malformed = graph.Copy();
                if (invalidX) malformed.Nodes[0].X = invalid; else malformed.Nodes[0].Y = invalid;
                require(GraphNodeAlignment.ResolveAuto(malformed, selected) == NodeAlignment.Top
                    && GraphNodeAlignment.Plan(malformed, selected, NodeAlignment.Auto, sizes).Count == 0,
                    "Auto rejects invalid selected coordinates before planning either axis.");
            }
        var ambiguous = graph.Copy(); ambiguous.Nodes.Add(ambiguous.Nodes[0].Copy());
        var badSizes = new Dictionary<string, Size>(sizes) { ["processor"] = Size.Empty };
        require(GraphNodeAlignment.ResolveAuto(ambiguous, selected) == NodeAlignment.Top
            && GraphNodeAlignment.Plan(ambiguous, selected, NodeAlignment.Auto, sizes).Count == 0
            && GraphNodeAlignment.Plan(graph, selected, NodeAlignment.Auto).Count == 0
            && GraphNodeAlignment.Plan(graph, selected, NodeAlignment.Auto, badSizes).Count == 0,
            "Auto never converts ambiguous IDs or missing/invalid rendered dimensions into an actionable plan.");
    }

    private static void VerifySignalOrder(Action<bool, string> require)
    {
        string[] graphOrder = ["color", "mic", "gate", "mix", "denoise", "output"];
        string[] kinds = ["voiceColor", "microphone", "expander", "mix", "denoiser", "output"];
        string[] chain = ["mic", "denoise", "gate", "color", "mix", "output"];
        var graph = new GraphConfiguration
        {
            Nodes = graphOrder.Select((id, i) => new GraphNode { Id = id, Name = id, Kind = kinds[i],
                X = -800.25 + i * 1000, Y = -600.5 + i * 1000, GainDb = -i, Bypass = i == 2 }).ToList(),
            Edges = chain.Zip(chain.Skip(1), (from, to) => new GraphEdge { Id = from + "-" + to,
                From = from, To = to, Kind = "audio", Muted = to == "gate", GainDb = -2.5 }).ToList(),
            PanX = -39.5, PanY = 80.25, Zoom = .75
        };
        var sizes = graph.Nodes.Select((node, i) => (node.Id, Size: new Size(300 + i * 20, 160 + i * 15)))
            .ToDictionary(pair => pair.Id, pair => pair.Size, StringComparer.Ordinal);
        string Settings(GraphConfiguration source)
        {
            var copy = source.Copy();
            foreach (var node in copy.Nodes) node.X = node.Y = 0;
            return JsonSerializer.Serialize(copy, Protocol.Json);
        }
        void Check(GraphConfiguration source, string[] expected, NodeAlignment alignment, string scenario)
        {
            string before = JsonSerializer.Serialize(source, Protocol.Json);
            string[] selected = expected.Reverse().Concat([expected[0], "stale"]).ToArray();
            bool vertical = (alignment == NodeAlignment.Auto ? GraphNodeAlignment.ResolveAuto(source, selected) : alignment) == NodeAlignment.Left;
            double Primary(GraphNode node) => vertical ? node.Y : node.X;
            var originals = source.Nodes.Where(node => expected.Contains(node.Id)).ToArray();
            var plan = GraphNodeAlignment.Plan(source, selected, alignment, sizes);
            var result = Apply(source, plan);
            var ordered = result.Nodes.Where(node => expected.Contains(node.Id)).OrderBy(Primary).ToArray();
            require(ordered.Select(node => node.Id).SequenceEqual(expected)
                && Primary(ordered[0]) == originals.Min(Primary)
                && ordered.All(node => vertical ? node.X == originals.Min(n => n.X) : node.Y == originals.Min(n => n.Y)),
                alignment + " follows cable precedence and retains the group anchor for " + scenario + ".");
            for (int i = 1; i < ordered.Length; i++)
            {
                double next = Primary(ordered[i - 1]) + (vertical ? sizes[ordered[i - 1].Id].Height : sizes[ordered[i - 1].Id].Width) + 48;
                require(Primary(ordered[i]) == next,
                    alignment + " leaves exactly 48 DIPs between complete node bodies for " + scenario + ".");
            }
            require(plan.Keys.SequenceEqual(source.Nodes.Where(node => plan.ContainsKey(node.Id)).Select(node => node.Id))
                && JsonSerializer.Serialize(source, Protocol.Json) == before && Settings(result) == Settings(source)
                && source.Nodes.Where(node => !expected.Contains(node.Id)).All(node => {
                    var current = result.Nodes.Single(other => other.Id == node.Id); return current.X == node.X && current.Y == node.Y;
                }) && GraphNodeAlignment.Plan(result, selected, alignment, sizes).Count == 0,
                alignment + " is idempotent, preserves graph-order plans, unselected positions and all audio/settings for " + scenario + ".");
        }

        var reversed = graph.Copy();
        for (int i = 0; i < chain.Length; i++)
        {
            var node = reversed.Nodes.Single(node => node.Id == chain[i]);
            node.X = -400.25 + (chain.Length - i) * 900; node.Y = -200.5 + (chain.Length - i) * 900;
        }
        var through = graph.Copy();
        through.Nodes.Single(node => node.Id == "mic").X = through.Nodes.Single(node => node.Id == "mic").Y = 5000;
        through.Nodes.Single(node => node.Id == "output").X = through.Nodes.Single(node => node.Id == "output").Y = -2000;
        var fork = graph.Copy();
        fork.Edges = [new() { Id = "a", From = "mic", To = "gate" }, new() { Id = "b", From = "mic", To = "denoise" },
            new() { Id = "c", From = "gate", To = "mix" }, new() { Id = "d", From = "denoise", To = "mix" }];
        var dependencies = graph.Copy();
        dependencies.Nodes.Single(node => node.Id == "color").Kind = "control";
        dependencies.Nodes.Single(node => node.Id == "gate").Kind = "microphone";
        dependencies.Nodes.Single(node => node.Id == "denoise").Kind = "ducking";
        string[] dependencySpatial = ["denoise", "mic", "mix", "output", "color", "gate"];
        for (int i = 0; i < dependencySpatial.Length; i++)
        {
            var node = dependencies.Nodes.Single(node => node.Id == dependencySpatial[i]); node.X = node.Y = i * 1000;
        }
        dependencies.Edges = [new() { Id = "control", From = "color", To = "denoise", Kind = "control", Muted = true },
            new() { Id = "program", From = "mic", To = "denoise" },
            new() { Id = "detector", From = "gate", To = "denoise", Kind = "sidechain", Muted = true },
            new() { Id = "mix", From = "denoise", To = "mix" }, new() { Id = "out", From = "mix", To = "output" }];
        var unrelated = graph.Copy();
        unrelated.Edges = [new() { Id = "unknown", From = "output", To = "color", Kind = "unknown" },
            new() { Id = "stale", From = "output", To = "missing" }, new() { Id = "stale-next", From = "missing", To = "color" }];
        var alreadyOrdered = graph.Copy();
        for (int i = 0; i < chain.Length; i++)
        {
            var node = alreadyOrdered.Nodes.Single(node => node.Id == chain[i]); node.X = -1000.25 + i * 1800; node.Y = -700.5 + i * 1800;
        }
        foreach (NodeAlignment alignment in Enum.GetValues<NodeAlignment>())
        {
            Check(graph, chain, alignment, "the scrambled muted audio chain");
            Check(reversed, chain, alignment, "the reverse audio chain");
            Check(through, ["mic", "output"], alignment, "an audio path through four unselected processors");
            Check(fork, ["color", "mic", "gate", "denoise", "mix", "output"], alignment, "a fork/merge with spatially stable unrelated nodes");
            Check(dependencies, ["mic", "color", "gate", "denoise", "mix", "output"], alignment, "muted control and sidechain dependencies");
            Check(dependencies, ["color", "output"], alignment, "control followed by audio through unselected nodes");
            Check(dependencies, ["gate", "output"], alignment, "sidechain followed by audio through unselected nodes");
            Check(unrelated, graphOrder, alignment, "disconnected nodes with unknown/dangling edges");
            Check(alreadyOrdered, chain, alignment, "an already ordered chain with wide gaps");
            foreach (string kind in new[] { "audio", "sidechain", "control" })
            {
                var cyclic = graph.Copy(); cyclic.Edges.Add(new() { Id = "cycle", From = "output", To = "mic", Kind = kind });
                string before = JsonSerializer.Serialize(cyclic, Protocol.Json);
                require(GraphNodeAlignment.Plan(cyclic, ["mic", "output"], alignment, sizes).Count == 0
                    && JsonSerializer.Serialize(cyclic, Protocol.Json) == before,
                    alignment + " rejects " + kind + " dependency cycles through unselected nodes without partial mutation.");
            }
        }
        var vertical = graph.Copy(); foreach (var node in vertical.Nodes) node.Y *= 2;
        require(GraphNodeAlignment.ResolveAuto(vertical, chain) == NodeAlignment.Left,
            "Connections do not change Auto's origin-span choice of a vertical column.");
        Check(vertical, chain, NodeAlignment.Auto, "a scrambled vertical audio chain");
    }

    private static GraphConfiguration Apply(GraphConfiguration source, IReadOnlyDictionary<string, Point> plan)
    {
        var copy = source.Copy();
        foreach (var node in copy.Nodes)
            if (plan.TryGetValue(node.Id, out var position)) { node.X = position.X; node.Y = position.Y; }
        return copy;
    }
}
