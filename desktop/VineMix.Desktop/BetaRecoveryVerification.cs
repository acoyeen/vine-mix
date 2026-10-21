using System;
using System.Threading;
using System.Threading.Tasks;

namespace VineMix.Desktop;

internal static class BetaRecoveryVerification
{
    internal static async Task RunAsync(Action<bool,string> require)
    {
        var basis = new MixerConfiguration { Version = 2, Graph = GraphDefaults.Fresh() };
        string state = "backupAvailable";
        MixerSnapshot Snapshot() => new() { Configuration = basis.Copy(), SettingsState = state };
        var operations = new EngineRestartOperations((command,configuration,revision,token) =>
            Task.FromResult(new ProtocolResponse { Ok = true, Snapshot = Snapshot() }), _ => Task.CompletedTask, () => { });
        using var model = new MixerViewModel(operations);
        state = "loaded"; model.ApplyPreviewSnapshot(Snapshot());
        model.Edit(c => c.Graph.Nodes[0].Name = "Pending local edit");
        require(model.PendingEditCount == 1 && model.CanUndo, "A queued local edit has history before a simulated settings failure.");
        state = "backupAvailable"; model.ApplyPreviewSnapshot(Snapshot());
        require(model.PendingEditCount == 1 && model.HasSettingsRecovery && model.GraphConfig.Nodes[0].Name == "Physical input",
            "Recovery previews the actual backup while retaining pending edits separately.");
        await model.RecoverSettingsAsync("recoveryRetry");
        require(model.PendingEditCount == 1 && model.HasSettingsRecovery && !model.CanEdit,
            "An unsuccessful recovery Retry cannot discard pending local changes.");
        state = "loaded";
        await model.RecoverSettingsAsync("recoveryRetry");
        require(model.CanEdit && model.PendingEditCount == 1 && model.GraphConfig.Nodes[0].Name == "Pending local edit" && model.CanUndo,
            "Retry after an external repair replays pending edits and retains history without claiming they are saved.");

        var waiting = new EngineRestartOperations(async (_,_,_,token) =>
        { await Task.Delay(Timeout.Infinite,token); return new ProtocolResponse(); }, _ => Task.CompletedTask, () => { });
        var cancellation = new MixerViewModel(waiting);
        cancellation.ApplyPreviewSnapshot(new MixerSnapshot { Configuration = basis.Copy(), SettingsState = "recoveryRequired" });
        Task first = cancellation.RecoverSettingsAsync("recoveryRetry");
        Task second = cancellation.RecoverSettingsAsync("recoveryRetry");
        cancellation.Dispose();
        await Task.WhenAll(first,second);
        require(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully, "Closing while recovery waits for the lifecycle gate handles cancellation without an unhandled UI exception.");
    }
}
