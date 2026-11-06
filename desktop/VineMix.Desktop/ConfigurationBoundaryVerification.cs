using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VineMix.Desktop;

/// <summary>
/// Real managed/native configuration serialization followed by the production
/// acknowledgement and history path. All models are detached preview models;
/// the only child process is the inert stdin/stdout fixture serializer.
/// </summary>
internal static class ConfigurationBoundaryVerification
{
    private sealed record Check(string Name, bool Passed);
    private sealed class Reply
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = "";
        public MixerConfiguration Configuration { get; set; } = new();
        public float[] PlanNodeGains { get; set; } = [];
        public float[] PlanEdgeGains { get; set; } = [];
        public PathState[] StatusFixtures { get; set; } = [];
    }

    private sealed class NativeFixtures : IDisposable
    {
        private readonly Process _process;
        private readonly Action<bool, string> _require;
        internal NativeFixtures(string path, Action<bool, string> require)
        {
            _require = require;
            _process = Process.Start(new ProcessStartInfo(Path.GetFullPath(path))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                ArgumentList = { "--stdio-fixtures" }
            }) ?? throw new InvalidOperationException("Could not start the inert configuration serializer.");
        }
        internal Reply Send(string json)
        {
            _process.StandardInput.WriteLine(json); _process.StandardInput.Flush();
            string line = _process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("The inert serializer ended without a reply.");
            return JsonSerializer.Deserialize<Reply>(line, Protocol.Json) ?? throw new JsonException("Missing serializer reply.");
        }
        internal MixerConfiguration RoundTrip(MixerConfiguration value, string context, bool acknowledge = false)
        {
            var sent = value.Copy();
            if (acknowledge) sent.Revision++;
            var reply = Send(JsonSerializer.Serialize(sent, Protocol.Json));
            if (!reply.Ok) throw new InvalidOperationException(context + ": " + reply.Error);
            _require(reply.PlanNodeGains.SequenceEqual(reply.Configuration.Graph.Nodes.Select(n => (float)n.GainDb))
                && reply.PlanEdgeGains.SequenceEqual(reply.Configuration.Graph.Edges.Select(e => (float)e.GainDb)),
                context + ": compiled DSP gains retain the established float conversion");
            return reply.Configuration;
        }
        public void Dispose()
        {
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(5000)) _process.Kill();
            }
            finally { _process.Dispose(); }
        }
    }

    internal static int Run(string[] args)
    {
        string Argument(string name)
        {
            int index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Missing " + name);
        }
        string reportPath = Path.GetFullPath(Argument("--report"));
        if (File.Exists(reportPath)) throw new IOException("Choose a new boundary verification report; prior evidence is retained.");
        var checks = new List<Check>();
        string? error = null;
        void Require(bool passed, string name) => checks.Add(new(name, passed));
        try
        {
            using var native = new NativeFixtures(Argument("--native-probe"), Require);
            VerifyFreshDefaults(Argument("--native-probe"), native, Require);
            VerifyGains(native, Require);
            VerifyPendingEdits(native, Require);
            VerifyConflicts(native, Require);
            VerifyLegacyAndNumericContracts(native, Require);
            VerifyCreativeSettings(native, Require);
            VerifySharedOutputs(native, Require);
        }
        catch (Exception exception) { error = exception.ToString(); }
        bool passed = error is null && checks.Count > 0 && checks.All(c => c.Passed);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            passed, error,
            scope = "Production C#/C++ configuration serialization and detached production acknowledgement/history. Synthetic fixtures only; no engine IPC, personal settings, windows, audio devices or live input.",
            checks
        }, new JsonSerializerOptions(Protocol.Json) { WriteIndented = true }));
        return passed ? 0 : 1;
    }

    private static void VerifyFreshDefaults(string probePath, NativeFixtures native, Action<bool, string> require)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(probePath))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            ArgumentList = { "--fresh-defaults" }
        }) ?? throw new InvalidOperationException("Could not start the inert fresh-defaults exporter.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill();
            throw new TimeoutException("The inert fresh-defaults exporter did not finish.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Fresh-defaults export failed: " + error.GetAwaiter().GetResult());
        var configuration = JsonSerializer.Deserialize<MixerConfiguration>(output.GetAwaiter().GetResult(), Protocol.Json)
            ?? throw new JsonException("Missing native fresh configuration.");
        var expected = JsonSerializer.SerializeToNode(GraphDefaults.Fresh(), Protocol.Json);
        require(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(configuration.Graph, Protocol.Json)),
            "Native and desktop fresh factories agree on every starter node, edge, setting, device policy and camera value.");
        var accepted = native.RoundTrip(configuration, "Fresh voice starter");
        require(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(accepted.Graph, Protocol.Json)),
            "The seven-node voice starter compiles and round-trips through the real native boundary without changing its defaults.");
    }

    private static MixerConfiguration Fixture()
    {
        var input = GraphDefaults.NewNode("virtualInput", 20, 30); input.Id = "boundary-input"; input.AutoAssignDevice = false;
        var gain = GraphDefaults.NewNode("gain", 400, 30); gain.Id = "boundary-gain";
        var output = GraphDefaults.NewNode("output", 800, 30); output.Id = "boundary-output"; output.AutoAssignDevice = false;
        return new()
        {
            Version = 2, Revision = 1,
            Graph = new()
            {
                Nodes = [input, gain, output],
                Edges = [new() { Id = "boundary-in", From = input.Id, To = gain.Id }, new() { Id = "boundary-out", From = gain.Id, To = output.Id }]
            }
        };
    }
    private static MixerSnapshot Snapshot(MixerConfiguration config) => new() { Configuration = config };
    private static MixerViewModel Queued(MixerConfiguration config)
    {
        var mixer = new MixerViewModel(preview: true) { QueuePreviewEdits = true };
        mixer.ApplyPreviewSnapshot(Snapshot(config)); return mixer;
    }
    private static MixerConfiguration Acknowledge(NativeFixtures native, MixerViewModel mixer, MixerConfiguration basis, string context)
    {
        var batch = mixer.PreparePendingConfiguration(basis);
        var accepted = native.RoundTrip(batch.Configuration, context, acknowledge: true);
        mixer.AcknowledgePending(batch, Snapshot(accepted)); return accepted;
    }
    private static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);

    private static void VerifySharedOutputs(NativeFixtures native, Action<bool, string> require)
    {
        foreach (string kind in new[] { "output", "virtualOutput" })
        {
            var initial = Fixture();
            var first = initial.Graph.Nodes[2]; first.Kind = kind;
            first.Device = new() { Id = "example-shared-render", StableId = "example-render-stable", Name = "Example destination" };
            first.Muted = false; first.GainDb = -6;
            using var mixer = Queued(initial);
            var second = first.Copy(); second.Id = "boundary-output-copy"; second.Name = "Second destination";
            second.Muted = true; second.GainDb = -12;
            mixer.Edit(c => { c.Graph.Nodes.Add(second.Copy()); c.Graph.Edges.Add(new() { Id = "boundary-output-branch", From = c.Graph.Nodes[1].Id, To = second.Id }); });
            var accepted = Acknowledge(native, mixer, initial, kind + " shared destination creation");
            require(accepted.Graph.Nodes.Count == 4 && accepted.Graph.Nodes[3].Device.Id == first.Device.Id
                && accepted.Graph.Nodes[2] is { Muted: false, GainDb: -6 }
                && accepted.Graph.Nodes[3] is { Muted: true, GainDb: -12 } && mixer.CanUndo,
                kind + " repeated binding survives native serialization/acknowledgement with independent controls and history");
            require(mixer.Undo(), kind + " shared-output creation can be undone");
            accepted = Acknowledge(native, mixer, accepted, kind + " shared destination undo");
            require(accepted.Graph.Nodes.Count == 3 && mixer.Redo(), kind + " undo removes only the added output and permits redo");
            accepted = Acknowledge(native, mixer, accepted, kind + " shared destination redo");
            require(accepted.Graph.Nodes.Count == 4 && accepted.Graph.Nodes[3].Id == second.Id,
                kind + " redo restores the same output identity and repeated binding");
            mixer.Edit(c => { var node = c.Graph.Nodes.Single(n => n.Id == second.Id); node.Muted = false; node.GainDb = -9.125; });
            accepted = Acknowledge(native, mixer, accepted, kind + " independent shared output edit");
            require(accepted.Graph.Nodes[2] is { GainDb: -6, Muted: false }
                && accepted.Graph.Nodes[3] is { GainDb: -9.125, Muted: false },
                kind + " editing the second shared output preserves the first across the real native boundary");
        }
    }

    private static void VerifyCreativeSettings(NativeFixtures native, Action<bool, string> require)
    {
        var missing = JsonNode.Parse(Json(Fixture()))!;
        missing["graph"]!["nodes"]![1]!["kind"] = "echo";
        missing["graph"]!["nodes"]![1]!.AsObject().Remove("echo");
        var defaults = native.Send(missing.ToJsonString());
        require(defaults.Ok && defaults.Configuration.Graph.Nodes[1].Echo is { TimeMs: 300, Repeats: 3, Mix: .25 },
            "A wholly absent native Echo settings record receives the same three-repeat defaults as desktop.");
        foreach (var item in CreativeEffectsVerification.EchoJsonCases)
        {
            var payload = JsonNode.Parse(Json(Fixture()))!;
            payload["graph"]!["nodes"]![1]!["kind"] = "echo";
            payload["graph"]!["nodes"]![1]!["echo"] = JsonNode.Parse(item.Json);
            string json = payload.ToJsonString();
            var reply = native.Send(json);
            require(reply.Ok == item.Repeats.HasValue, "Native Echo accepts/rejects the same raw payload as desktop: " + item.Json);
            if (reply.Ok)
            {
                var managed = JsonSerializer.Deserialize<MixerConfiguration>(json, Protocol.Json)!;
                require(Json(reply.Configuration.Graph.Nodes[1].Echo) == Json(managed.Graph.Nodes[1].Echo)
                    && reply.Configuration.Graph.Nodes[1].Echo.Repeats == item.Repeats,
                    "Native and desktop Echo canonicalization match exactly: " + item.Json);
            }
        }
        foreach (string kind in new[] { "echo", "distortion" })
        {
            var initial = Fixture(); initial.Graph.Nodes[1].Kind = kind;
            var baseline = native.RoundTrip(initial, kind + " defaults");
            require(Json(baseline.Graph.Nodes[1].Echo) == Json(initial.Graph.Nodes[1].Echo)
                && Json(baseline.Graph.Nodes[1].Distortion) == Json(initial.Graph.Nodes[1].Distortion),
                kind + ": new default fields survive real native serialization exactly");
            using var mixer = Queued(baseline);
            mixer.Edit(configuration =>
            {
                var node = configuration.Graph.Nodes[1];
                node.Echo = new() { TimeMs = 417.125, Repeats = 5, Mix = .375 };
                node.Distortion = new() { DriveDb = 18.25, ToneHz = 4321.5, Mix = .625, OutputDb = -9.25 };
            });
            string wanted = Json(mixer.GraphConfig.Nodes[1]);
            var accepted = Acknowledge(native, mixer, baseline, kind + " edited settings");
            require(Json(accepted.Graph.Nodes[1]) == wanted && Json(mixer.GraphConfig.Nodes[1]) == wanted
                && mixer.CanUndo && mixer.PendingEditCount == 0,
                kind + ": decimal creative settings acknowledge without precision loss or history reset");
            require(mixer.Undo() && Json(mixer.GraphConfig.Nodes[1]) == Json(baseline.Graph.Nodes[1]),
                kind + ": Undo restores creative defaults after a real native acknowledgement");
            accepted = Acknowledge(native, mixer, accepted, kind + " undo acknowledgement");
            require(mixer.Redo() && Json(mixer.GraphConfig.Nodes[1]) == wanted,
                kind + ": Redo restores creative settings after undo acknowledgement");
            accepted = Acknowledge(native, mixer, accepted, kind + " redo acknowledgement");
            require(Json(native.RoundTrip(accepted, kind + " saved reload")) == Json(accepted),
                kind + ": saved creative settings remain exact on another native reload");
        }
        var routed = Fixture(); routed.Graph.Nodes[1].Kind = "echo";
        var reverb = GraphDefaults.NewNode("reverb", 600, 300); reverb.Id = "boundary-reverb";
        routed.Graph.Nodes.Add(reverb);
        routed.Graph.Edges.Add(new() { Id = "boundary-echo-only", From = routed.Graph.Nodes[1].Id, FromPort = "wet", To = reverb.Id });
        routed.Graph.Edges.Add(new() { Id = "boundary-reverb-out", From = reverb.Id, To = routed.Graph.Nodes[2].Id });
        require(Json(native.RoundTrip(routed, "Echo-only branch into Reverb")) == Json(routed),
            "The Echo-only source port survives real native serialization alongside its unchanged mixed Audio route.");
    }

    private static void VerifyGains(NativeFixtures native, Action<bool, string> require)
    {
        double[] values = [-60, -59.999999999, -6, -6.5, -6.1, -.3, .1 + .2, 1.2, 11.999999999, 12];
        foreach (string kind in new[] { "virtualInput", "microphone", "appInput", "gain", "mix", "cable" })
        foreach (double value in values)
        {
            string context = kind + " " + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var initial = Fixture();
            int index = kind is "gain" or "mix" ? 1 : 0;
            if (kind != "cable") initial.Graph.Nodes[index].Kind = kind;
            var baseline = native.RoundTrip(initial, context + " baseline");
            using var mixer = Queued(baseline);
            void Set(MixerConfiguration c, double gain)
            {
                if (kind == "cable") c.Graph.Edges[0].GainDb = gain;
                else c.Graph.Nodes[index].GainDb = gain;
            }
            double Get(GraphConfiguration graph) => kind == "cable" ? graph.Edges[0].GainDb : graph.Nodes[index].GainDb;
            mixer.Edit(c => Set(c, value));
            var accepted = Acknowledge(native, mixer, baseline, context);
            require(Get(accepted.Graph) == value && Get(mixer.GraphConfig) == value && mixer.CanUndo && mixer.PendingEditCount == 0,
                context + ": acknowledgement preserves exact gain and usable history");
            require(mixer.Undo() && Get(mixer.GraphConfig) == 0, context + ": Undo restores the original gain");
            accepted = Acknowledge(native, mixer, accepted, context + " undo");
            require(mixer.Redo() && Get(mixer.GraphConfig) == value, context + ": Redo restores the decimal gain");
            accepted = Acknowledge(native, mixer, accepted, context + " redo");
            for (int cycle = 0; cycle < 3; ++cycle) accepted = native.RoundTrip(accepted, context + " reload " + cycle);
            mixer.ApplyPreviewSnapshot(Snapshot(accepted));
            require(Get(accepted.Graph) == value && mixer.CanUndo, context + ": repeated serialization/reload preserves history");
        }
        var gestureBaseline = native.RoundTrip(Fixture(), "gesture baseline");
        using var gesture = Queued(gestureBaseline);
        gesture.BeginEditGesture();
        foreach (double value in new[] { -.1, -.2, -.3 }) gesture.Edit(c => c.Graph.Nodes[0].GainDb = value);
        gesture.EndEditGesture();
        Acknowledge(native, gesture, gestureBaseline, "decimal drag gesture");
        require(gesture.EditHistory.Entries.Count == 1 && gesture.Undo() && gesture.GraphConfig.Nodes[0].GainDb == 0,
            "A coalesced decimal drag gesture remains one reversible history entry after native acknowledgement");

        foreach (double invalid in new[] { Math.BitDecrement(-60d), Math.BitIncrement(12d) })
        foreach (bool cable in new[] { false, true })
        {
            var config = Fixture();
            if (cable) config.Graph.Edges[0].GainDb = invalid; else config.Graph.Nodes[0].GainDb = invalid;
            require(!native.Send(Json(config)).Ok, (cable ? "Cable" : "Node") + " gain outside the exact range is rejected before DSP narrowing");
        }
    }

    private static void VerifyPendingEdits(NativeFixtures native, Action<bool, string> require)
    {
        var baseline = native.RoundTrip(Fixture(), "pending baseline");
        using (var mixer = Queued(baseline))
        {
            mixer.Edit(c => c.Graph.Nodes[0].GainDb = -6.1);
            var first = mixer.PreparePendingConfiguration(baseline);
            mixer.Edit(c => c.Graph.Nodes[0].GainDb = -.3);
            var accepted = native.RoundTrip(first.Configuration, "earlier acknowledgement", acknowledge: true);
            mixer.AcknowledgePending(first, Snapshot(accepted));
            require(mixer.PendingEditCount == 1 && mixer.GraphConfig.Nodes[0].GainDb == -.3 && mixer.CanUndo,
                "A newer decimal edit survives an earlier acknowledgement");
            Acknowledge(native, mixer, accepted, "newer acknowledgement");
            require(mixer.Undo() && mixer.GraphConfig.Nodes[0].GainDb == -6.1 && mixer.Undo() && mixer.GraphConfig.Nodes[0].GainDb == 0,
                "Both decimal edits retain their individual Undo steps");
        }
        using (var mixer = Queued(baseline))
        {
            mixer.Edit(c => c.Graph.Edges[0].GainDb = 1.2);
            var first = mixer.PreparePendingConfiguration(baseline);
            require(mixer.Undo(), "Undo is allowed while a decimal cable edit is saving");
            var accepted = native.RoundTrip(first.Configuration, "in-flight acknowledgement", acknowledge: true);
            mixer.AcknowledgePending(first, Snapshot(accepted));
            require(mixer.PendingEditCount == 1 && mixer.GraphConfig.Edges[0].GainDb == 0 && mixer.CanRedo,
                "An in-flight acknowledgement preserves the newer queued Undo");
            var undo = mixer.PreparePendingConfiguration(accepted);
            var saved = native.RoundTrip(undo.Configuration, "lost restore acknowledgement", acknowledge: true);
            mixer.ApplyPreviewSnapshot(Snapshot(saved));
            var replay = mixer.PreparePendingConfiguration(saved);
            require(replay.Configuration.Graph.Edges[0].GainDb == 0 && mixer.CanRedo,
                "Reconnect after a lost restore acknowledgement keeps the intended graph and Redo");
            mixer.AcknowledgePending(undo, Snapshot(saved));
            require(mixer.PendingEditCount == 0 && mixer.Redo() && mixer.GraphConfig.Edges[0].GainDb == 1.2,
                "A late acknowledgement removes only its batch and retains decimal Redo");
        }
        using (var mixer = Queued(baseline))
        {
            mixer.Edit(c => c.Graph.Nodes[0].GainDb = -.3);
            var lost = mixer.PreparePendingConfiguration(baseline);
            var saved = native.RoundTrip(lost.Configuration, "lost edit acknowledgement", acknowledge: true);
            mixer.ApplyPreviewSnapshot(Snapshot(saved));
            require(mixer.PreparePendingConfiguration(saved).Configuration.Graph.Nodes[0].GainDb == -.3 && mixer.CanUndo,
                "Reconnect with an already-applied decimal edit is idempotent");
            mixer.AcknowledgePending(lost, Snapshot(saved));
            require(mixer.Undo() && mixer.GraphConfig.Nodes[0].GainDb == 0,
                "Undo still works after an edit acknowledgement was lost and later recovered");
        }
    }

    private static void VerifyConflicts(NativeFixtures native, Action<bool, string> require)
    {
        foreach (string conflict in new[] { "name", "device", "gain", "cable" })
        {
            var baseline = native.RoundTrip(Fixture(), conflict + " conflict baseline");
            using var mixer = Queued(baseline);
            mixer.Edit(c => { if (conflict == "cable") c.Graph.Edges[0].GainDb = -6.1; else c.Graph.Nodes[0].GainDb = -6.1; });
            var accepted = Acknowledge(native, mixer, baseline, conflict + " conflict preparation");
            mixer.Undo();
            var remote = accepted.Copy(); remote.Revision++;
            if (conflict == "name") remote.Graph.Nodes[0].Name = "Independent edit";
            if (conflict == "device") remote.Graph.Nodes[0].Device = new() { Id = "example-capture", StableId = "example-capture", Name = "Example device", Channel = -1 };
            if (conflict == "gain") remote.Graph.Nodes[0].GainDb = -.3;
            if (conflict == "cable") remote.Graph.Edges[0].GainDb = -.3;
            remote = native.RoundTrip(remote, conflict + " independent edit");
            mixer.ApplyPreviewSnapshot(Snapshot(remote));
            require(mixer.PendingEditCount == 0 && !mixer.CanUndo && !mixer.CanRedo && Json(mixer.GraphConfig) == Json(remote.Graph)
                && mixer.Warning.Contains("workspace changed", StringComparison.OrdinalIgnoreCase),
                "A genuine independent " + conflict + " conflict refuses a stale Undo and preserves the latest state");
        }
        var original = native.RoundTrip(Fixture(), "unrelated change baseline");
        using var unrelated = Queued(original);
        unrelated.Edit(c => c.Graph.Nodes[0].GainDb = -6.1);
        var saved = Acknowledge(native, unrelated, original, "unrelated change preparation");
        unrelated.Undo(); saved.Graph.Nodes[2].Name = "Unrelated node"; saved.Revision++;
        saved = native.RoundTrip(saved, "unrelated change");
        unrelated.ApplyPreviewSnapshot(Snapshot(saved));
        require(unrelated.CanRedo && unrelated.GraphConfig.Nodes[0].GainDb == 0 && unrelated.GraphConfig.Nodes[2].Name == "Unrelated node",
            "An unrelated external node change does not discard compatible history");
    }

    private static void VerifyLegacyAndNumericContracts(NativeFixtures native, Action<bool, string> require)
    {
        var statuses = native.Send(Json(Fixture())).StatusFixtures;
        require(statuses.Length == 2 && statuses[0].Reason == "device_disabled" && StatusReasons.For(statuses[0]) == "device_disabled"
            && statuses[0].Message == "Reworded fixture diagnostic",
            "A production native status reason survives desktop deserialization independently of diagnostic wording");
        require(statuses.Length == 2 && statuses[1].Reason == "future_reason" && StatusReasons.For(statuses[1]) == "future_reason",
            "An unknown native reason is retained without reclassifying its familiar diagnostic text");
        var precise = Fixture(); precise.Revision = 9007199254740993;
        precise.Graph.PanX = -123.1; precise.Graph.PanY = 42.2; precise.Graph.Zoom = .3333333333333333;
        var node = precise.Graph.Nodes[1]; node.X = 400.1; node.Y = 30.2;
        node.GainDb = -.3; node.ControlMultiplier = 1.2; node.Balance = .3; node.StereoWidth = 1.2;
        node.PanPosition = -.3; node.CrossfadePosition = .1;
        node.EqBands[0].FrequencyHz = 123.4; node.EqBands[0].GainDb = 1.2; node.EqBands[0].Q = .7;
        node.Compressor.ThresholdDb = -18.1; node.Compressor.Ratio = 3.3; node.Compressor.AttackMs = 1.2;
        node.Ducking.AmountDb = 17.3; node.Expander.ThresholdDb = -40.1; node.Denoiser.RnnoiseMix = .3;
        node.DeEsser.FrequencyHz = 6000.1; node.Plosive.ReductionDb = 1.2; node.Reverb.Mix = .3;
        node.Reverb.DecaySeconds = 1.2; node.VoiceColor.Tone = .3; node.AlignmentDelay.Samples = 96000;
        node.App.ProcessId = uint.MaxValue;
        var received = native.RoundTrip(precise, "numeric contract audit");
        require(Json(received.Graph) == Json(precise.Graph) && received.Revision == precise.Revision,
            "Graph doubles and bounded integer settings round-trip exactly; revisions above 2^53 remain exact");

        // Old version-2 files can already contain the widened form of a float.
        var old = Fixture(); old.Graph.Nodes[0].GainDb = -6.099999904632568; old.Graph.Edges[0].GainDb = -.30000001192092896;
        var legacyJson = JsonNode.Parse(Json(old))!;
        foreach (var oldNode in legacyJson["graph"]!["nodes"]!.AsArray())
            foreach (string field in new[] { "controlMultiplier", "expander", "alignmentDelay", "voiceColor", "dynamicsUiVersion" }) oldNode!.AsObject().Remove(field);
        var legacyReply = native.Send(legacyJson.ToJsonString());
        require(legacyReply.Ok && legacyReply.Configuration.Graph.Nodes[0].GainDb == old.Graph.Nodes[0].GainDb
            && legacyReply.Configuration.Graph.Edges[0].GainDb == old.Graph.Edges[0].GainDb
            && legacyReply.Configuration.Graph.Nodes[0].ControlMultiplier == -1,
            "Older version-2 gains retain their exact saved values and absent optional settings receive production defaults");
        var stable = native.RoundTrip(legacyReply.Configuration, "old version-2 reload");
        require(Json(stable) == Json(legacyReply.Configuration), "An older workspace is stable on repeated reload");

        // The legacy source/bus schema intentionally remains float in native
        // code. Migrating it must preserve that established one-time rounding.
        var version1 = Fixture(); version1.Version = 1; version1.BusA.GainDb = -6.1; version1.Sources[0].A.GainDb = -.3;
        var migrated = native.RoundTrip(version1, "version-1 migration");
        require(migrated.Version == 2 && migrated.Graph.Nodes.Single(n => n.Id == "mix-a").GainDb == (double)(float)-6.1
            && migrated.Graph.Edges.Single(e => e.Id == "mic1-a").GainDb == (double)(float)-.3,
            "Version-1 source/bus gains preserve established float migration semantics");
        require(Json(native.RoundTrip(migrated, "migrated reload")) == Json(migrated),
            "Migrated legacy values remain stable in the new graph schema");
    }
}
