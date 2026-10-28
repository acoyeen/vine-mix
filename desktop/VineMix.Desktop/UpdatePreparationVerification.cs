using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VineMix.Desktop;

/// <summary>
/// Strict update save/shutdown checks using only in-memory protocol delegates.
/// No processes, pipe, settings file, native window or driver is accessed.
/// </summary>
internal static class UpdatePreparationVerification
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private const string NodeId = "preview-update-mix";

    internal static async Task RunAsync(Action<bool, string> require)
    {
        await SaveAndMaintenance(require);
        await LostConfigureReply(require);
        await UncertainSaveAcrossAttempts(require);
        await StaleRevision(require);
        await PreparationFailures(require);
        await CancelledSave(require);
        await NewEndpointDoesNotQueueAfterSave(require);
        await ShutdownGuards(require);
        await CooperativeShutdownFailure(require);
        await LatePreferencesPreventShutdown(require);
        VerifyPreferences(require);
        VerifyHealthReadOnly(require);
    }

    private static async Task SaveAndMaintenance(Action<bool, string> require)
    {
        var engine = new Engine { HoldConfigure = Signal() };
        using var mixer = Connected(engine);
        mixer.Edit(configuration => configuration.Graph.Nodes.Single(node => node.Id == NodeId).GainDb = -9);
        var addition = GraphDefaults.NewNode("eq", 320, 40); addition.Id = "preview-update-added";
        mixer.Edit(configuration => configuration.Graph.Nodes.Add(addition.Copy()));
        mixer.SetCanvasView(80, 40, .75);
        int history = mixer.EditHistory.Entries.Count;
        // A newer saved base must be reconciled, not overwritten by an older
        // full desired snapshot. The queued edits address stable node IDs.
        var external = GraphDefaults.NewNode("mono", 640, 40); external.Id = "preview-update-external";
        engine.Saved.Graph.Nodes.Add(external); engine.Saved.Revision++;
        var saving = mixer.PrepareForUpdateAsync(CancellationToken.None);
        await engine.ConfigureEntered.Task.WaitAsync(Deadline);
        try
        {
            require(mixer.IsPreparingUpdate && !mixer.CanEdit && !mixer.CanRestartEngine && mixer.PendingEditCount == 2
                && engine.ShutdownCalls == 0 && engine.RestartCalls == 0,
                "Update preparation disables editing and restart while queued changes are still awaiting an engine acknowledgement.");
            var duplicate = await Failure(() => mixer.PrepareForUpdateAsync(CancellationToken.None));
            var restart = await mixer.RestartEngineAsync(); await mixer.RetryAsync();
            mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = 6);
            require(duplicate is InvalidOperationException && !restart.Success && engine.ConfigureCalls == 1
                && engine.Calls.All(command => command is "snapshot" or "configure") && engine.RestartCalls == 0
                && mixer.GraphConfig.Nodes.Single(node => node.Id == NodeId).GainDb == -9,
                "Duplicate update preparation, restart, device retry and graph edits cannot race the held maintenance save.");
        }
        finally { engine.HoldConfigure.TrySetResult(); }
        var prepared = await saving.WaitAsync(Deadline);
        require(mixer.PendingEditCount == 0 && mixer.IsPreparingUpdate && !mixer.CanEdit && !mixer.CanRestartEngine
            && engine.Saved.Graph.Nodes.Single(node => node.Id == NodeId).GainDb == -9
            && engine.Saved.Graph.Nodes.Count(node => node.Id == addition.Id) == 1
            && engine.Saved.Graph.Nodes.Any(node => node.Id == external.Id)
            && engine.Saved.Graph.PanX == 80 && engine.Saved.Graph.Zoom == .75
            && Equal(prepared.Configuration, engine.Saved) && mixer.EditHistory.Entries.Count == history,
            "Preparation returns only after the reconciled edits and current canvas view are saved and acknowledged, preserving newer saved nodes and undo history.");
        await mixer.ShutdownForUpdateAsync(prepared, CancellationToken.None);
        require(engine.ShutdownCalls == 1 && engine.Calls.TakeLast(2).SequenceEqual(["snapshot", "shutdown"])
            && mixer.IsPreparingUpdate && !mixer.CanEdit && mixer.PendingEditCount == 0,
            "A fresh exact workspace/PID check precedes the sole cooperative shutdown, and maintenance remains active until handoff completes.");
        mixer.ResumeAfterUpdatePreparation();
        require(!mixer.IsPreparingUpdate && mixer.CanEdit && mixer.CanRestartEngine && mixer.PendingEditCount == 0,
            "Cancelling a completed preparation restores editing without discarding or replaying acknowledged work.");
    }

    private static async Task LostConfigureReply(Action<bool, string> require)
    {
        var engine = new Engine { LoseConfigureReply = true };
        using var mixer = Connected(engine);
        var addition = GraphDefaults.NewNode("mix", 160, 40); addition.Id = "preview-update-uncertain-append";
        mixer.Edit(configuration => configuration.Graph.Nodes.Add(addition.Copy()));
        var prepared = await mixer.PrepareForUpdateAsync(CancellationToken.None);
        require(engine.ConfigureCalls == 1 && engine.Calls.SequenceEqual(["snapshot", "configure", "snapshot"])
            && mixer.PendingEditCount == 0 && engine.Saved.Graph.Nodes.Count(node => node.Id == addition.Id) == 1
            && prepared.Configuration.Graph.Nodes.Count(node => node.Id == addition.Id) == 1 && engine.ShutdownCalls == 0,
            "A configure committed before its reply was lost is confirmed by exact readback without replaying the queued append or stopping audio.");
        mixer.ResumeAfterUpdatePreparation();
    }

    private static async Task StaleRevision(Action<bool, string> require)
    {
        var engine = new Engine { StaleConfigureOnce = true };
        using var mixer = Connected(engine);
        mixer.Edit(configuration => configuration.Graph.Nodes.Single(node => node.Id == NodeId).GainDb = -14);
        await mixer.PrepareForUpdateAsync(CancellationToken.None);
        require(engine.ConfigureCalls == 2 && mixer.PendingEditCount == 0
            && engine.Saved.Graph.Nodes.Single(node => node.Id == NodeId).GainDb == -14 && engine.ShutdownCalls == 0,
            "A stale-revision refusal reads the current saved basis and retries the queued edit before reporting preparation complete.");
        mixer.ResumeAfterUpdatePreparation();
    }

    private static async Task UncertainSaveAcrossAttempts(Action<bool, string> require)
    {
        foreach (bool cancelAfterCommit in new[] { false, true })
        foreach (bool newerEdit in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var engine = new Engine(); using var mixer = Connected(engine);
            var addition = GraphDefaults.NewNode("mix", 160, 40); addition.Id = "preview-update-cross-attempt-append";
            mixer.Edit(configuration => configuration.Graph.Nodes.Add(addition.Copy()));
            if (cancelAfterCommit) engine.CancelAfterCommit = cancellation;
            else engine.Transform = (command, response) => command == "configure" ? new() { Ok = true } : response;
            var error = await Failure(() => mixer.PrepareForUpdateAsync(cancellation.Token));
            require(error is IOException or OperationCanceledException && mixer.PendingEditCount == 1
                && !mixer.IsPreparingUpdate && engine.Saved.Graph.Nodes.Count(node => node.Id == addition.Id) == 1,
                "An uncertain committed save retains its unacknowledged append when its "
                    + (cancelAfterCommit ? "deadline/cancellation arrives" : "confirmation is malformed") + ".");
            engine.CancelAfterCommit = null; engine.Transform = null;
            if (newerEdit) mixer.Edit(configuration => configuration.Graph.Nodes.Single(node => node.Id == NodeId).GainDb = -5);
            var prepared = await mixer.PrepareForUpdateAsync(CancellationToken.None);
            require(mixer.PendingEditCount == 0 && engine.ConfigureCalls == (newerEdit ? 2 : 1)
                && engine.Saved.Graph.Nodes.Count(node => node.Id == addition.Id) == 1
                && prepared.Configuration.Graph.Nodes.Count(node => node.Id == addition.Id) == 1
                && (!newerEdit || engine.Saved.Graph.Nodes.Single(node => node.Id == NodeId).GainDb == -5)
                && Equal(prepared.Configuration, engine.Saved) && engine.ShutdownCalls == 0,
                "A later preparation reconciles the uncertain append exactly once"
                    + (newerEdit ? " and also saves the newer queued edit before returning." : " without sending a duplicate configure."));
            mixer.ResumeAfterUpdatePreparation();
        }
    }

    private static async Task PreparationFailures(Action<bool, string> require)
    {
        foreach (var scenario in new (string Name, Action<Engine> Set, bool NoConfigure)[]
        {
            ("rejected save", engine => engine.RejectConfigure = true, false),
            ("missing snapshot", engine => engine.Transform = (command, response) => command == "snapshot" ? new() { Ok = true } : response, true),
            ("unsupported workspace", engine => engine.Transform = (command, response) => { if (command == "snapshot") response.Snapshot!.Configuration.Version = 99; return response; }, true),
            ("incomplete workspace shape", engine => engine.Transform = (command, response) => { if (command == "snapshot") response.Snapshot!.Configuration.Sources = []; return response; }, true),
            ("incomplete nested node", engine => engine.Transform = (command, response) => { if (command == "snapshot") response.Snapshot!.Configuration.Graph.Nodes[0].Device = null!; return response; }, true),
            ("unsupported engine protocol", engine => engine.Transform = (command, response) => { if (command == "snapshot") response.Snapshot!.EngineProtocolVersion = 99; return response; }, true),
            ("workspace recovery required", engine => engine.Transform = (command, response) => { if (command == "snapshot") response.Snapshot!.SettingsState = "backupAvailable"; return response; }, true),
            ("missing save confirmation", engine => engine.Transform = (command, response) => command == "configure" ? new() { Ok = true } : response, false),
            ("different saved content", engine => engine.Transform = (command, response) => { if (command == "configure") response.Snapshot!.Configuration.Graph.Nodes[0].GainDb = 3; return response; }, false),
            ("recovery in save confirmation", engine => engine.Transform = (command, response) => { if (command == "configure") response.Snapshot!.SettingsState = "unsupported"; return response; }, false)
        })
        {
            var engine = new Engine(); using var mixer = Connected(engine);
            mixer.Edit(configuration => configuration.Graph.Nodes.Single(node => node.Id == NodeId).GainDb = -12);
            string before = Json(mixer.GraphConfig); int history = mixer.EditHistory.Entries.Count;
            scenario.Set(engine);
            var error = await Failure(() => mixer.PrepareForUpdateAsync(CancellationToken.None));
            require(error is IOException or InvalidOperationException && !mixer.IsPreparingUpdate && mixer.CanEdit && mixer.CanRestartEngine
                && mixer.PendingEditCount == 1 && Json(mixer.GraphConfig) == before && mixer.EditHistory.Entries.Count == history
                && engine.ShutdownCalls == 0 && engine.RestartCalls == 0 && (!scenario.NoConfigure || engine.ConfigureCalls == 0),
                "Update preparation stops safely after " + scenario.Name + ", retains queued work/history and leaves the audio session available.");
        }
        var unsafeEngine = new Engine();
        using var unsafeTarget = new MixerViewModel(unsafeEngine.Operations() with { ValidateTarget = () => throw new IOException("preview update foreign target") });
        unsafeTarget.ApplyPreviewSnapshot(unsafeEngine.Snapshot());
        var unsafeError = await Failure(() => unsafeTarget.PrepareForUpdateAsync(CancellationToken.None));
        require(unsafeError is IOException && unsafeEngine.Calls.Count == 0 && !unsafeTarget.IsPreparingUpdate,
            "A target-identity refusal happens before saving or shutting down any audio engine.");
    }

    private static async Task CancelledSave(Action<bool, string> require)
    {
        var engine = new Engine { HoldConfigure = Signal() };
        using var mixer = Connected(engine);
        mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = -16);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var error = await Failure(() => mixer.PrepareForUpdateAsync(timeout.Token));
        require(error is OperationCanceledException && !mixer.IsPreparingUpdate && mixer.CanEdit && mixer.CanRestartEngine
            && mixer.PendingEditCount == 1 && mixer.GraphConfig.Nodes[0].GainDb == -16
            && engine.Saved.Graph.Nodes[0].GainDb == 0 && engine.ShutdownCalls == 0,
            "A preparation deadline expiring during save releases maintenance, preserves the queued edit and never sends shutdown.");
        engine.HoldConfigure.TrySetResult();
    }

    private static async Task NewEndpointDoesNotQueueAfterSave(Action<bool, string> require)
    {
        var engine = new Engine();
        var input = GraphDefaults.NewNode("virtualInput", 160, 0); input.Id = "preview-update-virtual-input";
        engine.Saved.Graph.Nodes.Add(input);
        using var mixer = Connected(engine);
        engine.Endpoints = [new() { Id = "preview-update-cable", Name = "Example cable input", Direction = "capture", Transport = "vbCable", State = "Active" }];
        var prepared = await mixer.PrepareForUpdateAsync(CancellationToken.None);
        require(mixer.PendingEditCount == 0 && !VirtualDeviceBindings.IsAssigned(mixer.GraphConfig.Nodes.Single(node => node.Id == input.Id).Device)
            && Equal(prepared.Configuration, engine.Saved),
            "A newly discovered endpoint cannot auto-assign and queue fresh work after the update's final save acknowledgement.");
        mixer.ResumeAfterUpdatePreparation();
    }

    private static async Task ShutdownGuards(Action<bool, string> require)
    {
        foreach (var scenario in new (string Name, Action<Engine, MixerViewModel> Change)[]
        {
            ("different saved workspace", (engine, _) => engine.Saved.Graph.Nodes[0].GainDb = -21),
            ("different engine PID", (engine, _) => engine.EnginePid++),
            ("recovery state", (engine, _) => engine.SettingsState = "unsupported"),
            ("missing readback", (engine, _) => engine.Transform = (_, _) => new() { Ok = true }),
            ("malformed readback", (engine, _) => engine.Transform = (_, response) => { response.Snapshot!.Configuration.Sources = []; return response; })
        })
        {
            var engine = new Engine(); using var mixer = Connected(engine);
            var prepared = await mixer.PrepareForUpdateAsync(CancellationToken.None);
            scenario.Change(engine, mixer);
            var error = await Failure(() => mixer.ShutdownForUpdateAsync(prepared, CancellationToken.None));
            require(error is IOException && engine.ShutdownCalls == 0 && !mixer.IsPreparingUpdate && mixer.CanEdit && mixer.PendingEditCount == 0,
                "Shutdown revalidation refuses a " + scenario.Name + " without stopping the engine or leaving maintenance stuck.");
        }
        var idleEngine = new Engine(); using var idle = Connected(idleEngine);
        var premature = await Failure(() => idle.ShutdownForUpdateAsync(idleEngine.Snapshot(), CancellationToken.None));
        require(premature is InvalidOperationException && idleEngine.Calls.Count == 0,
            "Shutdown cannot be requested before a successful workspace preparation.");

        // A failed preparation cannot authorize a later shutdown while its
        // real, unacknowledged queued operation is still retained.
        var queuedEngine = new Engine { RejectConfigure = true }; using var queued = Connected(queuedEngine);
        queued.Edit(configuration => configuration.Graph.Nodes[0].GainDb = -18);
        await Failure(() => queued.PrepareForUpdateAsync(CancellationToken.None));
        var refused = await Failure(() => queued.ShutdownForUpdateAsync(queuedEngine.Snapshot(), CancellationToken.None));
        require(refused is InvalidOperationException && queued.PendingEditCount == 1 && queuedEngine.ShutdownCalls == 0,
            "A refused save leaves its queue intact, and a following shutdown attempt cannot bypass the missing preparation.");
    }

    private static async Task CooperativeShutdownFailure(Action<bool, string> require)
    {
        var engine = new Engine { RejectShutdown = true }; using var mixer = Connected(engine);
        mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = -7);
        int history = mixer.EditHistory.Entries.Count;
        var prepared = await mixer.PrepareForUpdateAsync(CancellationToken.None);
        var error = await Failure(() => mixer.ShutdownForUpdateAsync(prepared, CancellationToken.None));
        require(error is IOException && engine.ShutdownCalls == 1 && !mixer.IsPreparingUpdate && mixer.CanEdit && mixer.CanRestartEngine
            && mixer.PendingEditCount == 0 && mixer.EditHistory.Entries.Count == history && mixer.GraphConfig.Nodes[0].GainDb == -7,
            "A cooperative shutdown refusal restores normal maintenance state while retaining the acknowledged workspace and undo history.");
        mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = -8);
        require(mixer.PendingEditCount == 1 && mixer.GraphConfig.Nodes[0].GainDb == -8,
            "The retained session accepts and queues a new edit after a cooperative shutdown refusal.");
    }

    private static void VerifyPreferences(Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true);
        require(mixer.AutomaticallyCheckForUpdates, "Automatic update checking defaults on without reading a personal preferences file.");
        mixer.LoadUiPreferencesJson("{\"rightClickDragToPan\":true,\"showAdvancedAudioDevices\":true,\"reducedAnimation\":true}");
        require(mixer.AutomaticallyCheckForUpdates && mixer.RightClickDragToPan && mixer.ShowAdvancedAudioDevices && mixer.ReducedAnimation,
            "Older preference JSON lacking an update field retains its existing choices and uses the compatible update-check default.");
        string graph = Json(mixer.GraphConfig); mixer.AutomaticallyCheckForUpdates = false;
        using var restored = new MixerViewModel(preview: true); restored.LoadUiPreferencesJson(mixer.SerializeUiPreferences());
        require(!restored.AutomaticallyCheckForUpdates && restored.RightClickDragToPan && restored.ShowAdvancedAudioDevices
            && restored.ReducedAnimation && mixer.PendingEditCount == 0 && mixer.EditHistory.Entries.Count == 0 && Json(mixer.GraphConfig) == graph,
            "The automatic-check opt-out survives the persisted JSON format independently of graph edits, queued engine work and other preferences.");
    }

    private static void VerifyHealthReadOnly(Action<bool, string> require)
    {
        var engine = new Engine(); using var mixer = Connected(engine);
        mixer.SetUpdateHealthMode(true); string before = Json(mixer.GraphConfig);
        mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = 10);
        require(mixer.IsUpdateHealthMode && !mixer.CanEdit && !mixer.CanRestartEngine && Json(mixer.GraphConfig) == before
            && mixer.PendingEditCount == 0 && engine.Calls.Count == 0,
            "Candidate health mode keeps the workspace read-only without issuing configure, restart or shutdown requests.");
        mixer.SetUpdateHealthMode(false);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static bool Equal(MixerConfiguration left, MixerConfiguration right) => Json(left) == Json(right);
    private static MixerViewModel Connected(Engine engine)
    {
        var mixer = new MixerViewModel(engine.Operations()); mixer.ApplyPreviewSnapshot(engine.Snapshot()); return mixer;
    }
    private static async Task<Exception?> Failure(Func<Task> action)
    {
        try { await action().WaitAsync(Deadline); return null; }
        catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException) { return error; }
    }

    private static async Task LatePreferencesPreventShutdown(Action<bool, string> require)
    {
        var engine = new Engine();
        using var mixer = Connected(engine);
        var prepared = await mixer.PrepareForUpdateAsync(CancellationToken.None);
        bool failWrite = true;
        string savedPreferences = "";
        mixer.PreviewUiPreferencesWriter = json =>
        {
            if (failWrite) throw new IOException("Example late preference failure");
            savedPreferences = json;
        };
        engine.HoldSnapshot = Signal();
        var stopping = mixer.ShutdownForUpdateAsync(prepared, CancellationToken.None);
        await engine.SnapshotEntered.Task.WaitAsync(Deadline);
        mixer.AutomaticallyCheckForUpdates = false;
        engine.HoldSnapshot.SetResult();
        var error = await Failure(() => stopping);
        require(error is IOException && engine.ShutdownCalls == 0 && !mixer.IsPreparingUpdate && mixer.CanEdit
            && !mixer.AutomaticallyCheckForUpdates,
            "A preference-write failure during the awaited final snapshot blocks update shutdown and returns the retained session to editing.");
        failWrite = false; engine.HoldSnapshot = null;
        prepared = await mixer.PrepareForUpdateAsync(CancellationToken.None);
        await mixer.ShutdownForUpdateAsync(prepared, CancellationToken.None);
        require(engine.ShutdownCalls == 1 && savedPreferences.Contains("\"automaticallyCheckForUpdates\":false", StringComparison.Ordinal),
            "Retry saves the late preference value before the sole cooperative update shutdown.");
        mixer.ResumeAfterUpdatePreparation();
    }

    private sealed class Engine
    {
        internal MixerConfiguration Saved = new() { Version = 2, Revision = 7, Graph = new()
            { Nodes = [new GraphNode { Id = NodeId, Kind = "mix", Name = "Example update mix" }] } };
        internal readonly List<string> Calls = [];
        internal readonly TaskCompletionSource ConfigureEntered = Signal();
        internal TaskCompletionSource? HoldConfigure;
        internal TaskCompletionSource? HoldSnapshot;
        internal readonly TaskCompletionSource SnapshotEntered = Signal();
        internal CancellationTokenSource? CancelAfterCommit;
        internal Func<string, ProtocolResponse, ProtocolResponse>? Transform;
        internal List<Endpoint> Endpoints = [];
        internal string SettingsState = "loaded";
        internal int EnginePid = 4242, ConfigureCalls, ShutdownCalls, RestartCalls;
        internal bool RejectConfigure, LoseConfigureReply, StaleConfigureOnce, RejectShutdown;

        internal EngineRestartOperations Operations() => new(Request, _ => { RestartCalls++; return Task.CompletedTask; }, () => { })
            { Deadline = Deadline, RetryDelay = TimeSpan.Zero, IsRunning = () => true };
        internal MixerSnapshot Snapshot() => new()
        {
            Configuration = Saved.Copy(), SettingsState = SettingsState, EnginePid = EnginePid,
            EngineProtocolVersion = 1, EngineVersion = ReleaseInfo.Version, Endpoints = Endpoints.ToList(),
            SettingsPath = "preview-update-no-file", PhysicalSettingsPath = "preview-update-no-physical-file"
        };

        private async Task<ProtocolResponse> Request(string command, MixerConfiguration? configuration, ulong? revision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls.Add(command);
            if (command == "configure")
            {
                ConfigureCalls++; ConfigureEntered.TrySetResult();
                if (HoldConfigure is not null) await HoldConfigure.Task.WaitAsync(token);
                if (RejectConfigure) return new() { Error = "preview save refused" };
                if (StaleConfigureOnce) { StaleConfigureOnce = false; Saved.Revision++; return new() { Error = "stale revision" }; }
                if (revision != Saved.Revision || configuration is null) return new() { Error = "preview revision mismatch" };
                Saved = configuration.Copy(); Saved.Revision++;
                if (CancelAfterCommit is not null) { CancelAfterCommit.Cancel(); token.ThrowIfCancellationRequested(); }
                if (LoseConfigureReply) { LoseConfigureReply = false; throw new IOException("preview lost configure reply"); }
            }
            else if (command == "shutdown")
            {
                ShutdownCalls++;
                return new() { Ok = !RejectShutdown, Error = RejectShutdown ? "preview cooperative refusal" : "" };
            }
            else if (command != "snapshot") throw new InvalidOperationException("Unexpected detached update command: " + command);
            else if (HoldSnapshot is not null)
            {
                SnapshotEntered.TrySetResult();
                await HoldSnapshot.Task.WaitAsync(token);
            }
            var response = new ProtocolResponse { Ok = true, Snapshot = Snapshot() };
            return Transform?.Invoke(command, response) ?? response;
        }
    }
}
