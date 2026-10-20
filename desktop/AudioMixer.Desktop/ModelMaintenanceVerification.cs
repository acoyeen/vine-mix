using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace AudioMixer.Desktop;

internal static class ModelMaintenanceVerification
{
    internal static object Run(Action<bool, string> require)
    {
        VerifyVoiceColor(require);
        VerifyDynamicsCompatibility(require);
        var original = UiPreview.CreateSnapshot(["--signal"]).Configuration;
        original.Graph.Nodes = GraphDefaults.Kinds.Select((kind, i) => GraphDefaults.NewNode(kind, i * 430, 80)).ToList();
        // Populate every writable leaf, including future scalar settings, so a
        // forgotten copy field cannot hide behind its constructor's default.
        MutateLeaves(original);
        string baseline = JsonSerializer.Serialize(original, Protocol.Json);
        var copy = original.Copy();
        require(JsonSerializer.Serialize(copy, Protocol.Json) == baseline,
            "Direct configuration copies preserve every serialized setting, including legacy migration fields and all effect types.");
        AssertIndependent(original, copy, "configuration", require);
        MutateLeaves(copy);
        require(JsonSerializer.Serialize(original, Protocol.Json) == baseline
            && JsonSerializer.Serialize(copy, Protocol.Json) != baseline,
            "Editing every copied setting leaves the original configuration, nested effects, devices, bands and edges unchanged.");

        using var model = new MixerViewModel(preview: true);
        var fixture = UiPreview.CreateSnapshot([]); model.ApplyPreviewSnapshot(fixture);
        var endpoints = model.Endpoints; var apps = model.Applications;
        var repeated = JsonSerializer.Deserialize<MixerSnapshot>(JsonSerializer.Serialize(fixture, Protocol.Json), Protocol.Json)!;
        model.ApplyPreviewSnapshot(repeated);
        require(ReferenceEquals(endpoints, model.Endpoints) && ReferenceEquals(apps, model.Applications),
            "Equivalent inventories retain their list identity across new snapshots without rebuilding selector data.");
        repeated = JsonSerializer.Deserialize<MixerSnapshot>(JsonSerializer.Serialize(fixture, Protocol.Json), Protocol.Json)!;
        repeated.Endpoints[0].StableId = "changed-stable-identity";
        repeated.Applications[0].Name = "Changed display name";
        model.ApplyPreviewSnapshot(repeated);
        require(!ReferenceEquals(endpoints, model.Endpoints) && !ReferenceEquals(apps, model.Applications)
            && model.Endpoints[0].StableId == "changed-stable-identity" && model.Applications[0].Name == "Changed display name",
            "Stable endpoint identity and application-name changes invalidate cached inventories immediately.");

        var graph = new MixerConfiguration { Version = 2 };
        graph.Graph.Nodes = Enumerable.Range(0, 32).Select(i => GraphDefaults.NewNode(GraphDefaults.Kinds[i % GraphDefaults.Kinds.Length], i * 300, 80)).ToList();
        graph.Graph.Edges = Enumerable.Range(0, 31).Select(i => new GraphEdge
            { Id = "copy-" + i, From = graph.Graph.Nodes[i].Id, To = graph.Graph.Nodes[i + 1].Id }).ToList();
        MixerConfiguration JsonCopy() => JsonSerializer.Deserialize<MixerConfiguration>(JsonSerializer.Serialize(graph, Protocol.Json), Protocol.Json)!;
        for (int i = 0; i < 30; i++) { _ = JsonCopy(); _ = graph.Copy(); }
        const int iterations = 500;
        static (long Bytes, double Milliseconds) Measure(Func<MixerConfiguration> clone)
        {
            long start = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) GC.KeepAlive(clone());
            return (GC.GetAllocatedBytesForCurrentThread() - start, timer.Elapsed.TotalMilliseconds);
        }
        var before = Measure(JsonCopy); var after = Measure(graph.Copy);
        require(after.Bytes < before.Bytes / 2,
            "Copying a maximum-size graph avoids JSON buffers and allocates less than half the previous copy implementation.");
        var comparisons = VerifySerializedComparisons(graph.Graph, require);
        return new { scope = "In-memory copies only; same populated 32-node fixture and process. Elapsed time is diagnostic, not an audio-latency measurement.",
            iterations, before = new { bytes = before.Bytes, milliseconds = before.Milliseconds },
            after = new { bytes = after.Bytes, milliseconds = after.Milliseconds }, comparisons };
    }

    private static object VerifySerializedComparisons(GraphConfiguration graph, Action<bool, string> require)
    {
        static bool PreviousComparison(GraphConfiguration left, GraphConfiguration right)
            => JsonSerializer.Serialize(new { left.Nodes, left.Edges }, Protocol.Json)
                == JsonSerializer.Serialize(new { right.Nodes, right.Edges }, Protocol.Json);
        var equal = graph.Copy();
        equal.PanX += 12; equal.PanY -= 34; equal.Zoom = .75;
        require(WorkspaceHistory.SameContents(graph, equal) && !WorkspaceHistory.SameValue(graph, equal),
            "History comparisons still ignore camera-only changes while complete configuration comparisons retain them.");
        require(WorkspaceHistory.SameValue<string?>(null, null) && !WorkspaceHistory.SameValue<string?>(null, ""),
            "Serialized comparisons preserve null equality and distinguish null from an empty value.");
        foreach (var variant in new (string Name, Action<GraphConfiguration> Change)[]
        {
            ("Unicode and escaped names", next => next.Nodes[0].Name = "音声 · \"mic\"\\\n🎙"),
            ("precise parameter values", next => next.Nodes[0].GainDb = -.000000000001),
            ("nested device assignment", next => next.Nodes[0].Device.Id = "changed-device"),
            ("nested effect settings", next => next.Nodes[0].Compressor.AttackMs = 11.125),
            ("EQ band ordering", next => next.Nodes[0].EqBands.Reverse()),
            ("node ordering", next => next.Nodes.Reverse()),
            ("connection ordering", next => next.Edges.Reverse()),
            ("connection values", next => next.Edges[0].Muted = true)
        })
        {
            var changed = graph.Copy(); variant.Change(changed);
            bool previous = PreviousComparison(graph, changed);
            require(!previous && WorkspaceHistory.SameContents(graph, changed) == previous,
                "UTF-8 history comparisons preserve the previous serialized semantics for " + variant.Name + ".");
        }
        for (int i = 0; i < 30; i++)
        { _ = PreviousComparison(graph, equal); _ = WorkspaceHistory.SameContents(graph, equal); }
        const int iterations = 100;
        static (long Bytes, double Milliseconds) Measure(Func<bool> compare)
        {
            long start = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) if (!compare()) throw new InvalidOperationException("Equivalent comparison fixture changed.");
            return (GC.GetAllocatedBytesForCurrentThread() - start, timer.Elapsed.TotalMilliseconds);
        }
        var before = Measure(() => PreviousComparison(graph, equal));
        var after = Measure(() => WorkspaceHistory.SameContents(graph, equal));
        require(after.Bytes < before.Bytes * .6,
            "Comparing maximum-size graph contents allocates less than 60 percent of the previous UTF-16 comparison implementation.");
        return new { scope = "Equal 32-node graph contents; camera excluded by both implementations.", iterations,
            before = new { bytes = before.Bytes, milliseconds = before.Milliseconds },
            after = new { bytes = after.Bytes, milliseconds = after.Milliseconds } };
    }

    private static void VerifyDynamicsCompatibility(Action<bool, string> require)
    {
        foreach (string kind in new[] { "compressor", "limiter", "ducking" })
        {
            var node = GraphDefaults.NewNode(kind, 314, 159);
            require(NodeCatalog.CanCreate(kind) && NodeCatalog.CategoryFor(kind).Title == "Effects"
                && GraphDefaults.HasAudioInput(kind) && GraphDefaults.HasAudioOutput(kind),
                kind + " is a distinct processing node in the Effects catalogue.");
            require(node.DynamicsUiVersion == (kind == "compressor" ? 1 : 0),
                kind + " has the intended new-node presentation version.");
            node.Ducking = new() { ThresholdDb = -32.125, AmountDb = 21.75, AttackMs = .125, ReleaseMs = 410.125 };
            node.Compressor.CeilingDb = -3.25; node.Compressor.ReleaseMs = 270.125;
            node.GainDb = -3; node.Bypass = true;
            var graph = new GraphConfiguration { Nodes = [node], PanX = -29, PanY = 81, Zoom = .75 };
            string json = JsonSerializer.Serialize(graph, Protocol.Json);
            var loaded = JsonSerializer.Deserialize<GraphConfiguration>(json, Protocol.Json)!;
            require(GraphDefaults.Validate(loaded).Length == 0 && JsonSerializer.Serialize(loaded, Protocol.Json) == json,
                kind + " saves and loads exact dynamics values, inherited gain, bypass and view.");
            require(GraphClipboard.TryCreatePaste(GraphClipboard.Serialize(graph, [node.Id]), new(), new Point(600, 300), out var paste, out _)
                && paste.Nodes.Single().Ducking.AttackMs == .125 && paste.Nodes.Single().Ducking.AmountDb == 21.75
                && paste.Nodes.Single().Compressor.CeilingDb == -3.25 && paste.Nodes.Single().DynamicsUiVersion == node.DynamicsUiVersion,
                kind + " clipboard copies preserve precise settings and the compatibility presentation marker.");
            var copy = node.Copy(); copy.Ducking.AmountDb = 0;
            require(node.Ducking.AmountDb == 21.75, "Copied Ducking settings are independently editable.");
            GraphDefaults.ResetNodeSettings(copy);
            require(copy.Id == node.Id && copy.X == node.X && copy.Y == node.Y && copy.Ducking.ThresholdDb == -30
                && copy.Ducking.AmountDb == 18 && copy.Ducking.AttackMs == 10 && copy.Ducking.ReleaseMs == 250
                && copy.Compressor.CeilingDb == -1 && copy.Compressor.ReleaseMs == 120,
                kind + " reset restores its sound defaults without changing identity or placement.");
        }
        require(NodeCatalog.MatchesSearch("ducking", "sidechain") && NodeCatalog.MatchesSearch("ducking", "music")
            && NodeCatalog.MatchesSearch("limiter", "peak"), "Dynamics can be found by purpose as well as name.");

        foreach (var setting in new[] { ("compress", "internal"), ("compress", "external"), ("limit", "external") })
        {
            var legacy = new GraphNode { Id = "saved-dynamics", Kind = "compressor", Name = "My original dynamics", X = 92, Y = 143,
                GainDb = 7.25, Bypass = true, Compressor = new() { Mode = setting.Item1, Detector = setting.Item2,
                    CeilingDb = -3.5, ThresholdDb = -23.75, Ratio = 5.125, AttackMs = .125, ReleaseMs = 231.125, MakeupDb = 4.25 } };
            var graph = new GraphConfiguration { Nodes = [legacy, new() { Id = "trigger", Kind = "microphone" }, new() { Id = "linked", Kind = "control", GainDb = -4.5 }, new() { Id = "duck", Kind = "ducking" }],
                Edges = [new() { Id = "detector", From = "trigger", To = "duck", Kind = "sidechain", GainDb = -3.5, Muted = true },
                    new() { Id = "control", From = "linked", To = legacy.Id, Kind = "control" }] };
            var oldJson = JsonNode.Parse(JsonSerializer.Serialize(graph, Protocol.Json))!;
            static void RemoveNewFields(JsonNode root)
            {
                foreach (var node in root["nodes"]!.AsArray())
                { node!.AsObject().Remove("dynamicsUiVersion"); node.AsObject().Remove("ducking"); }
            }
            RemoveNewFields(oldJson);
            var loaded = JsonSerializer.Deserialize<GraphConfiguration>(oldJson.ToJsonString(), Protocol.Json)!;
            require(GraphDefaults.Validate(loaded).Length == 0 && loaded.Nodes[0].Kind == "compressor" && loaded.Nodes[0].DynamicsUiVersion == 0,
                "An older " + setting + " compressor remains a legacy compressor with the original routing.");
            var roundTrip = JsonNode.Parse(JsonSerializer.Serialize(loaded, Protocol.Json))!; RemoveNewFields(roundTrip);
            require(JsonNode.DeepEquals(oldJson, roundTrip),
                "Loading an older compressor preserves every existing setting, name, position, cable, bypass and gain value.");
            string group = GraphClipboard.Serialize(loaded, loaded.Nodes.Select(node => node.Id));
            require(GraphClipboard.TryCreatePaste(group, new(), new Point(0, 0), out var copied, out _)
                && copied.Nodes.Single(node => node.Kind == "compressor").DynamicsUiVersion == 0
                && copied.Edges.Count == 2 && copied.Edges.Single(edge => edge.Kind == "sidechain").Muted,
                "Legacy compressor settings and independent Ducking detector wiring survive copying alongside linked controls.");
            GraphDefaults.ResetNodeSettings(loaded.Nodes[0]);
            require(loaded.Nodes[0].DynamicsUiVersion == 0, "Explicit reset preserves a saved compressor's legacy presentation choice.");
        }

        var valid = new GraphConfiguration { Nodes = [GraphDefaults.NewNode("ducking", 0, 0)] };
        foreach (Action<GraphNode> invalidate in new Action<GraphNode>[]
        {
            n => n.Ducking.ThresholdDb = -60.1, n => n.Ducking.ThresholdDb = double.NaN,
            n => n.Ducking.AmountDb = -.1, n => n.Ducking.AmountDb = 60.1,
            n => n.Ducking.AttackMs = .09, n => n.Ducking.AttackMs = 201,
            n => n.Ducking.ReleaseMs = 4.9, n => n.Ducking.ReleaseMs = 2001,
            n => n.Ducking.Mode = "unknown", n => n.Ducking.Ratio = .99, n => n.Ducking.Ratio = 20.01,
            n => n.Ducking.Ratio = double.NaN,
            n => n.Ducking = null!, n => n.DynamicsUiVersion = 2
        })
        {
            var invalid = valid.Copy(); invalidate(invalid.Nodes[0]);
            require(GraphDefaults.Validate(invalid).Length > 0, "Invalid Ducking values and unknown presentation versions are rejected before dispatch.");
        }
        string payload = GraphClipboard.Serialize(valid, [valid.Nodes[0].Id]);
        foreach (string malformed in new[] { "null", "{}", "{\"thresholdDb\":-30,\"amountDb\":18,\"attackMs\":10}",
            "{\"thresholdDb\":-30,\"amountDb\":61,\"attackMs\":10,\"releaseMs\":250}" })
        {
            var bad = JsonNode.Parse(payload)!; bad["nodes"]![0]!["ducking"] = JsonNode.Parse(malformed);
            require(!GraphClipboard.TryCreatePaste(bad.ToJsonString(), new(), new Point(), out _, out _),
                "Clipboard rejects incomplete or invalid Ducking settings: " + malformed);
        }
    }

    private static void VerifyVoiceColor(Action<bool, string> require)
    {
        var node = GraphDefaults.NewNode("voiceColor", 314, 159);
        node.Id = "voice-color-model";
        require(node.Name == "Voice Color" && node.VoiceColor.Tone == 0 && node.VoiceColor.Ambience == 0 && !node.Bypass,
            "Voice Color defaults to flat, dry, processing-enabled bottom centre.");
        require(GraphDefaults.HasAudioInput(node.Kind) && GraphDefaults.HasAudioOutput(node.Kind)
            && NodeCatalog.CanCreate(node.Kind) && NodeCatalog.CategoryFor(node.Kind).Title == "Effects"
            && !NodeCatalog.HasLevelControl(node.Kind),
            "Voice Color appears in the Effects library/search with audio input and output and no duplicate level control.");
        foreach (double tone in new[] { -1d, -.43, 0, .67, 1 })
        foreach (double ambience in new[] { 0d, .38, 1 })
        {
            node.VoiceColor = new() { Tone = tone, Ambience = ambience }; node.Bypass = true;
            var graph = new GraphConfiguration { Nodes = [node], PanX = 97, PanY = -83, Zoom = .5 };
            string json = JsonSerializer.Serialize(graph, Protocol.Json);
            var loaded = JsonSerializer.Deserialize<GraphConfiguration>(json, Protocol.Json)!;
            require(GraphDefaults.Validate(loaded).Length == 0 && JsonSerializer.Serialize(loaded, Protocol.Json) == json,
                $"Voice Color saves/loads exact tone {tone} and ambience {ambience}, bypass, layout and view.");
            string group = GraphClipboard.Serialize(graph, [node.Id]);
            require(GraphClipboard.TryCreatePaste(group, new(), new Point(600, 300), out var pasted, out _)
                && pasted.Nodes.Single().VoiceColor.Tone == tone && pasted.Nodes.Single().VoiceColor.Ambience == ambience
                && pasted.Nodes.Single().Bypass && pasted.Nodes.Single().Id != node.Id,
                "Voice Color clipboard copies preserve exact axes and bypass with a fresh node identity.");
            var committed = new GraphConfiguration();
            require(GraphClipboard.TryAppend(committed, pasted, out _), "Voice Color copies append through the normal validated edit path.");
            pasted.Nodes.Single().VoiceColor.Tone = .234;
            require(committed.Nodes.Single().VoiceColor.Tone == tone,
                "An appended Voice Color copy owns independent settings during pending-edit replay.");
        }

        var before = new GraphConfiguration { Nodes = [GraphDefaults.NewNode("voiceColor", 12, 34)] };
        var after = before.Copy(); after.Nodes[0].VoiceColor.Ambience = .65;
        var history = new WorkspaceHistory(); history.Record(before, after);
        require(history.Entries.Single().Description == "Adjusted Voice Color",
            "Voice Color axis edits use ordinary parameter history, rather than being misclassified as canvas movement.");
        var legacy = UiPreview.CreateSnapshot([]).Configuration;
        var legacyJson = JsonNode.Parse(JsonSerializer.Serialize(legacy, Protocol.Json))!;
        foreach (var savedNode in legacyJson["graph"]!["nodes"]!.AsArray()) savedNode!.AsObject().Remove("voiceColor");
        string legacyText = legacyJson.ToJsonString();
        var restored = JsonSerializer.Deserialize<MixerConfiguration>(legacyText, Protocol.Json)!;
        require(restored.Graph.Nodes.All(n => n.VoiceColor.Tone == 0 && n.VoiceColor.Ambience == 0),
            "Saved graphs without Voice Color settings receive only an unused neutral default.");
        var restoredJson = JsonNode.Parse(JsonSerializer.Serialize(restored, Protocol.Json))!;
        foreach (var savedNode in restoredJson["graph"]!["nodes"]!.AsArray()) savedNode!.AsObject().Remove("voiceColor");
        require(JsonNode.DeepEquals(legacyJson, restoredJson),
            "Loading a pre-Voice Color graph preserves every prior setting, node, cable and layout value.");

        node = GraphDefaults.NewNode("voiceColor", 0, 0);
        string payload = GraphClipboard.Serialize(new() { Nodes = [node] }, [node.Id]);
        foreach (string malformed in new[] { "null", "{}", "{\"tone\":0}", "{\"tone\":0,\"ambience\":0,\"extra\":1}", "{\"tone\":2,\"ambience\":0}", "{\"tone\":0,\"ambience\":-1}" })
        {
            var document = JsonNode.Parse(payload)!;
            document["nodes"]![0]!["voiceColor"] = JsonNode.Parse(malformed);
            require(!GraphClipboard.TryCreatePaste(document.ToJsonString(), new(), new Point(0, 0), out _, out _),
                "Clipboard rejects malformed or out-of-range Voice Color settings: " + malformed);
        }
        var oldGroup = JsonNode.Parse(payload)!; oldGroup["nodes"]![0]!.AsObject().Remove("voiceColor");
        require(GraphClipboard.TryCreatePaste(oldGroup.ToJsonString(), new(), new Point(0, 0), out var oldPaste, out _)
            && oldPaste.Nodes[0].VoiceColor.Tone == 0 && oldPaste.Nodes[0].VoiceColor.Ambience == 0,
            "Clipboard payloads predating Voice Color settings retain neutral defaults without a format-version change.");
    }

    private static void AssertIndependent(object original, object copy, string path, Action<bool, string> require)
    {
        Type type = original.GetType();
        if (type.IsValueType || original is string) return;
        require(!ReferenceEquals(original, copy), path + " owns an independent mutable copy.");
        if (original is IList list && copy is IList other)
        {
            require(list.Count == other.Count, path + " retains collection members.");
            for (int i = 0; i < list.Count; i++) AssertIndependent(list[i]!, other[i]!, path + "[" + i + "]", require);
        }
        else foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanWrite))
            if (property.GetValue(original) is { } value) AssertIndependent(value, property.GetValue(copy)!, path + "." + property.Name, require);
    }

    private static void MutateLeaves(object value)
    {
        if (value is IList list) { foreach (var item in list) if (item is not null) MutateLeaves(item); return; }
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanWrite))
        {
            object? current = property.GetValue(value);
            object? changed = current switch
            {
                string text => text + " copy-check", bool flag => !flag, int n => n + 1,
                uint n => n + 1, ulong n => n + 1, double n => n + .125, _ => null
            };
            if (changed is not null) property.SetValue(value, changed);
            else if (current is not null) MutateLeaves(current);
        }
    }
}
