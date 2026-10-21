using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VineMix.Desktop;

// Entirely in-memory lifecycle and protocol fakes. No process, control pipe,
// native window, personal settings, device discovery or live audio is used.
internal static class EngineRestartVerification
{
    internal static async Task RunAsync(Action<bool, string> require)
    {
        await SavedRestart(require);
        await OfflinePendingRestart(require);
        await FailedSavePreservesPending(require);
        await LostConfigureReply(require);
        await FailedReadiness(require);
        await RetryAndRestartSerialize(require);
        await QuitCancelsRestart(require);
        await UnsafeTargetIsUntouched(require);
        await NewDeviceReconciliation(require);
    }

    private static async Task SavedRestart(Action<bool, string> require)
    {
        var engine = new FakeEngine { RestartRelease = Signal() };
        using var mixer = Connected(engine);
        mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = -7);
        int history = mixer.EditHistory.Entries.Count;
        var restart = mixer.RestartEngineAsync();
        require(mixer.IsRestartingEngine && !mixer.CanRestartEngine && !mixer.CanEdit
            && engine.Saved.Graph.Nodes[0].GainDb == -7 && engine.Restarts == 1,
            "Explicit restart saves queued edits before stopping and disables editing throughout the restart.");
        var duplicate = await mixer.RestartEngineAsync();
        mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = 8);
        require(!duplicate.Success && engine.Restarts == 1 && mixer.GraphConfig.Nodes[0].GainDb == -7,
            "Repeated restart clicks cannot launch another restart or accept edits while busy.");
        engine.RestartRelease.SetResult();
        var result = await restart;
        require(result.Success && mixer.CanEdit && mixer.CanRestartEngine && !mixer.IsRestartingEngine
            && mixer.PendingEditCount == 0 && mixer.EditHistory.Entries.Count == history && engine.Resets == 1
            && engine.SnapshotsAfterRestart >= 2,
            "Restart succeeds only after reconnecting with advancing audio frames and retains acknowledged edit history.");
    }

    private static async Task OfflinePendingRestart(Action<bool, string> require)
    {
        var engine = new FakeEngine();
        using var mixer = Connected(engine);
        mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = -11);
        engine.Online = false;
        var result = await mixer.RestartEngineAsync();
        require(result.Success && engine.Restarts == 1 && engine.Saved.Graph.Nodes[0].GainDb == -11
            && mixer.PendingEditCount == 0 && engine.Calls.IndexOf("restart") < engine.Calls.IndexOf("configure"),
            "An unavailable engine does not block explicit recovery: unsaved changes survive and save after reconnecting.");
        using var offline = new MixerViewModel(new FakeEngine().Operations());
        require(!offline.CanEdit && offline.CanRestartEngine,
            "Explicit restart remains available before the first engine connection.");
    }

    private static async Task FailedSavePreservesPending(Action<bool, string> require)
    {
        var engine = new FakeEngine { RejectConfigure = true };
        using var mixer = Connected(engine);
        mixer.Edit(configuration => configuration.Graph.Nodes[0].GainDb = -13);
        int history = mixer.EditHistory.Entries.Count;
        var result = await mixer.RestartEngineAsync();
        require(!result.Success && result.Message.Contains("still queued", StringComparison.Ordinal)
            && mixer.PendingEditCount == 1 && mixer.GraphConfig.Nodes[0].GainDb == -13
            && mixer.EditHistory.Entries.Count == history && mixer.CanRestartEngine && !mixer.IsRestartingEngine,
            "Failed saving after reconnect retains the desired graph, pending operation and history, and re-enables restart.");

        engine.RejectConfigure = false; engine.FailRestart = true; engine.Online = false;
        result = await mixer.RestartEngineAsync();
        require(!result.Success && mixer.PendingEditCount == 1 && mixer.GraphConfig.Nodes[0].GainDb == -13
            && mixer.CanRestartEngine && result.Message.Contains("fake start failed", StringComparison.Ordinal),
            "A failed engine launch cannot discard unsaved changes or leave the restart menu permanently busy.");
    }

    private static async Task LostConfigureReply(Action<bool, string> require)
    {
        var engine = new FakeEngine { LoseConfigureReply = true };
        using var mixer = Connected(engine);
        var addition = GraphDefaults.NewNode("mix", 50, 50);
        mixer.Edit(configuration => configuration.Graph.Nodes.Add(addition.Copy()));
        var result = await mixer.RestartEngineAsync();
        require(result.Success && engine.ConfigureCalls == 1 && mixer.PendingEditCount == 0
            && engine.Saved.Graph.Nodes.Count(node => node.Id == addition.Id) == 1
            && mixer.GraphConfig.Nodes.Count(node => node.Id == addition.Id) == 1,
            "A configure that committed before its reply was lost is acknowledged from saved state without duplicating an append.");
    }

    private static async Task FailedReadiness(Action<bool, string> require)
    {
        var engine = new FakeEngine { AdvanceFrames = false };
        using (var mixer = new MixerViewModel(engine.Operations() with { Deadline = TimeSpan.FromMilliseconds(80) }))
        {
            var result = await mixer.RestartEngineAsync();
            require(!result.Success && engine.Restarts == 1 && mixer.CanRestartEngine && !mixer.CanEdit,
                "Snapshots from a stalled engine never count as a successful restart.");
        }
        engine = new FakeEngine { ExitAfterRestart = true };
        using (var mixer = new MixerViewModel(engine.Operations()))
        {
            var result = await mixer.RestartEngineAsync();
            require(!result.Success && engine.SnapshotsAfterRestart == 0 && mixer.CanRestartEngine,
                "A launched engine that exits cannot borrow another process's responding pipe as proof of success.");
        }
    }

    private static async Task RetryAndRestartSerialize(Action<bool, string> require)
    {
        var engine = new FakeEngine { RetryRelease = Signal() };
        using var mixer = Connected(engine);
        var retry = mixer.RetryAsync();
        var restart = mixer.RestartEngineAsync();
        require(engine.Restarts == 0 && mixer.IsRestartingEngine && !mixer.CanEdit,
            "Restart waits for an existing device request instead of racing its connection and snapshot.");
        engine.RetryRelease.SetResult();
        await retry;
        var result = await restart;
        require(result.Success && engine.MaximumRequests == 1 && engine.Restarts == 1,
            "The lifecycle gate serializes device retry with restart and its reconnect requests.");
    }

    private static async Task QuitCancelsRestart(Action<bool, string> require)
    {
        var engine = new FakeEngine { RestartRelease = Signal() };
        using var mixer = Connected(engine);
        var restart = mixer.RestartEngineAsync();
        var quit = mixer.StopAsync();
        var result = await restart;
        await quit;
        require(!result.Success && engine.Starts == 0 && !mixer.CanRestartEngine && !mixer.IsRestartingEngine,
            "Quit cancels a restart before starting the engine and waits for lifecycle ownership to be released.");
    }

    private static async Task UnsafeTargetIsUntouched(Action<bool, string> require)
    {
        var engine = new FakeEngine();
        using var mixer = new MixerViewModel(engine.Operations() with
        { ValidateTarget = () => throw new IOException("fake foreign installation") });
        var result = await mixer.RestartEngineAsync();
        require(!result.Success && engine.Calls.Count == 0 && mixer.CanRestartEngine
            && result.Message.Contains("fake foreign installation", StringComparison.Ordinal),
            "A target-identity refusal occurs before any save request, process restart or connection reset.");
    }

    private static async Task NewDeviceReconciliation(Action<bool, string> require)
    {
        var engine = new FakeEngine();
        var input = GraphDefaults.NewNode("virtualInput", 0, 0);
        engine.Saved.Graph.Nodes.Add(input);
        using var mixer = Connected(engine);
        engine.EndpointsAfterRestart = UiPreview.CreateSnapshot([]).Endpoints
            .Where(endpoint => endpoint.Direction == "capture" && endpoint.Transport == "vbCable").ToList();
        var result = await mixer.RestartEngineAsync();
        require(result.Success && mixer.GraphConfig.Nodes.Single(node => node.Id == input.Id).Device.Id.Length > 0
            && mixer.PendingEditCount > 0 && !result.Message.Contains("changes are saved", StringComparison.Ordinal),
            "An endpoint first discovered during restart still receives normal automatic assignment, with its new save honestly queued.");
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static MixerViewModel Connected(FakeEngine engine)
    {
        var mixer = new MixerViewModel(engine.Operations());
        mixer.ApplyPreviewSnapshot(engine.Snapshot());
        return mixer;
    }

    private sealed class FakeEngine
    {
        internal MixerConfiguration Saved = new()
        { Version = 2, Graph = new() { Nodes = [GraphDefaults.NewNode("gain", 0, 0)] } };
        internal readonly List<string> Calls = [];
        internal List<Endpoint> EndpointsAfterRestart = [];
        internal bool Online = true, Running = true, AdvanceFrames = true;
        internal bool RejectConfigure, LoseConfigureReply, FailRestart, ExitAfterRestart;
        internal int Restarts, Starts, Resets, ConfigureCalls, SnapshotsAfterRestart, MaximumRequests;
        internal TaskCompletionSource? RestartRelease, RetryRelease;
        private int _requests;
        private ulong _frames;

        internal EngineRestartOperations Operations() => new(Request, Restart, () => { Calls.Add("reset"); Resets++; })
        { IsRunning = () => Running, RetryDelay = TimeSpan.FromMilliseconds(1) };

        internal MixerSnapshot Snapshot()
        {
            if (AdvanceFrames) _frames += 240;
            return new() { Configuration = Saved.Copy(), Status = new() { AudioFrames = _frames },
                Endpoints = Restarts > 0 ? EndpointsAfterRestart : [] };
        }

        private async Task<ProtocolResponse> Request(string command, MixerConfiguration? configuration, ulong? revision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _requests++; MaximumRequests = Math.Max(MaximumRequests, _requests);
            try
            {
                if (!Online) throw new IOException("fake offline engine");
                Calls.Add(command);
                if (command == "retry" && RetryRelease is not null) await RetryRelease.Task.WaitAsync(token);
                if (command == "configure")
                {
                    ConfigureCalls++;
                    if (RejectConfigure) return new() { Error = "fake save rejected" };
                    if (revision != Saved.Revision) return new() { Error = "stale revision" };
                    Saved = configuration!.Copy(); Saved.Revision++;
                    if (LoseConfigureReply) { LoseConfigureReply = false; throw new IOException("fake lost configure reply"); }
                }
                if (Restarts > 0 && command == "snapshot") SnapshotsAfterRestart++;
                return new() { Ok = true, Snapshot = Snapshot() };
            }
            finally { _requests--; }
        }

        private async Task Restart(CancellationToken token)
        {
            Calls.Add("restart"); Restarts++;
            if (RestartRelease is not null) await RestartRelease.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (FailRestart) throw new IOException("fake start failed");
            Starts++; Online = true; Running = !ExitAfterRestart; _frames = 0;
        }
    }
}
