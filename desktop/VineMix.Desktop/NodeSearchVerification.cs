using System;
using System.Linq;
using System.Text.Json;

namespace VineMix.Desktop;

/// <summary>Pure snapshot search checks; no UI, settings, engine or process access.</summary>
internal static class NodeSearchVerification
{
    internal static void Run(Action<bool, string> require)
    {
        var graph = new GraphConfiguration
        {
            Nodes =
            [
                new() { Id = "opaque-node-token", Kind = "microphone", Name = "Broadcast Voice",
                    Device = new() { Id = "opaque-device-token", StableId = "opaque-stable-token", Name = "Studio USB" } },
                new() { Id = "second-voice", Kind = "microphone", Name = "Broadcast Voice",
                    Device = new() { StableId = "stable-only", Name = "Studio USB" } },
                new() { Id = "music", Kind = "appInput", Name = "Music",
                    App = new() { ExecutablePath = @"C:\Private\opaque-app-bin.exe", Name = "Spotify" } },
                new() { Id = "output", Kind = "output", Name = "Monitor",
                    Device = new() { Id = "render-device", Name = "Desk headphones" } },
                new() { Id = "eq", Kind = "eq", Name = "Vocal tone",
                    Device = new() { Id = "dormant-device", Name = "DormantDevice" },
                    App = new() { ProcessId = 938471, ExecutablePath = @"C:\Dormant\processor.exe", Name = "DormantApp" } },
                new() { Id = "backup", Kind = "microphone", Name = "Backup", Device = new() { Name = "Old unassigned device" } },
                new() { Id = "mix", Kind = "mix", Name = "Spotlight" },
                new() { Id = "headset", Kind = "microphone", Name = "Headset", Device = new() { Id = "headset-device", Name = "HEADSET" } },
                new() { Id = "spare", Kind = "microphone", Name = "Spare", Device = new() { Id = "spare-device", Name = "MICROPHONE" } },
                new() { Id = "legacy-app", Kind = "appInput", Name = "Legacy player", App = new() { ProcessId = 42, Name = "Radio" } },
                new() { Id = "unassigned-app", Kind = "appInput", Name = "Awaiting app", App = new() { Name = "Stale app" } },
                new() { Id = "unnamed", Kind = "denoiser", Name = "" }
            ],
            PanX = -143.25, PanY = 87.5, Zoom = .75
        };
        string before = JsonSerializer.Serialize(graph, Protocol.Json);
        string[] Ids(string? query) => NodeSearch.Match(graph, query).Select(result => result.Id).ToArray();
        foreach (string? query in new string?[] { null, "", " \t\r\n\u00a0 " })
            require(Ids(query).SequenceEqual(graph.Nodes.Select(node => node.Id)),
                "An empty or whitespace-only node search returns every current node in graph order.");
        var voices = NodeSearch.Match(graph, "VOICE");
        require(voices.Select(result => result.Id).SequenceEqual(["opaque-node-token", "second-voice"])
            && voices.All(result => result.Name == "Broadcast Voice" && result.Detail == "Microphone · Studio USB"),
            "Case-insensitive name search preserves separate IDs and stable order for identically named nodes.");
        require(Ids("\tvoice\u00a0STUDIO\r\nmicrophone ").SequenceEqual(["opaque-node-token", "second-voice"])
            && Ids("voice headphones").Length == 0,
            "Whitespace-separated terms use AND matching across node name, kind and assigned source name.");
        require(Ids("EQUALIZER").SequenceEqual(["eq"]) && Ids("listening desk").SequenceEqual(["output"])
            && Ids("spot mix").SequenceEqual(["mix"]),
            "Readable kind labels and assigned output names are searchable with partial terms.");
        require(Ids("Spotify running").SequenceEqual(["music"])
            && NodeSearch.Match(graph, "Spotify").Single().Detail == "Running app · Spotify"
            && Ids("radio").SequenceEqual(["legacy-app"]),
            "Saved app names remain findable from executable identity or a legacy process assignment without probing running apps.");
        require(Ids("DormantDevice").Length == 0 && Ids("DormantApp").Length == 0
            && Ids("unassigned").Length == 0 && Ids("stale").Length == 0,
            "Dormant processor fields and leftover names without an assignment are not presented as active node sources.");
        foreach (string query in new[] { "opaque-node-token", "opaque-device-token", "opaque-stable-token", "opaque-app-bin", "Private", "938471", "42" })
            require(Ids(query).Length == 0, "Node search never matches raw IDs, executable paths or process numbers: " + query + ".");
        require(NodeSearch.Match(graph, "headset").Single().Detail == "Microphone"
            && NodeSearch.Match(graph, "spare").Single().Detail == "Microphone"
            && NodeSearch.Match(graph, "denoiser").Single() is { Name: "Denoiser", Detail: "Denoiser" },
            "Details avoid repeating a source that equals the node name or kind, and an unnamed node has a readable kind fallback.");
        require(Ids("\u0131").Length == 0 && Ids("not-present-anywhere").Length == 0
            && NodeSearch.Match(new GraphConfiguration(), "").Count == 0,
            "Search uses ordinal case matching and handles no-match queries and an empty graph without special syntax.");
        require(JsonSerializer.Serialize(graph, Protocol.Json) == before,
            "Searching preserves all node settings, assignments, IDs, ordering and viewport values.");

        var savedResults = NodeSearch.Match(graph, "voice");
        graph.Nodes[0].Name = "Interview";
        graph.Nodes[0].Device.Name = "Field recorder";
        graph.Nodes.RemoveAll(node => node.Id == "second-voice");
        require(savedResults.Count == 2 && savedResults[0] is { Name: "Broadcast Voice", Detail: "Microphone · Studio USB" }
            && Ids("voice").Length == 0 && Ids("interview field").SequenceEqual(["opaque-node-token"]),
            "Results are immutable values from their snapshot; a new query immediately reflects renames, source changes and removals.");
        graph.Nodes.Reverse();
        require(Ids("").SequenceEqual(graph.Nodes.Select(node => node.Id)),
            "Search always follows the current graph order rather than a stale cache or alphabetic reordering.");
    }
}
