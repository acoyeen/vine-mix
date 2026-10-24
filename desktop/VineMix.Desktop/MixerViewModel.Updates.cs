using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VineMix.Desktop;

public sealed partial class MixerViewModel
{
    private bool _preparingUpdate;
    private PendingBatch? _unconfirmedUpdateBatch;
    private bool _automaticallyCheckForUpdates = true;
    private bool _updateHealthMode;
    private bool _healthEngineStartAttempted;
    internal bool IsUpdateHealthMode => _updateHealthMode;
    internal MixerSnapshot? CurrentUpdateHealthSnapshot => _snapshot;
    internal void SetUpdateHealthMode(bool value)
    {
        _updateHealthMode = value;
        Changed(nameof(CanEdit)); Changed(nameof(CanRestartEngine)); NotifyHistory();
        InvalidatePresentation();
    }
    private string EnsureHealthEngineStarted()
    {
        if (_healthEngineStartAttempted) return "Checking the updated audio engine…";
        _healthEngineStartAttempted = true;
        return _supervisor!.EnsureStarted();
    }
    internal async Task<bool> StopUpdateHealthAsync()
    {
        if (_preview) return true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        bool entered = false;
        try
        {
            SetPreparingUpdate(true);
            await _engineGate.WaitAsync(deadline.Token); entered = true;
            var response = await _restartOperations!.Request("shutdown", null, null, deadline.Token);
            if (!response.Ok || _snapshot is null) return false;
            await _supervisor!.WaitForUpdateExitAsync(_snapshot.EnginePid, deadline.Token);
            _stop.Cancel();
            return true;
        }
        catch (Exception error) when (RestartFailure(error)) { return false; }
        finally { if (entered) _engineGate.Release(); }
    }
    public bool IsPreparingUpdate => _preparingUpdate;
    internal event Action<bool>? AutomaticUpdatePreferenceChanged;
    public bool AutomaticallyCheckForUpdates
    {
        get => _automaticallyCheckForUpdates;
        set
        {
            if (Applying || !Set(ref _automaticallyCheckForUpdates, value)) return;
            SaveUiPreferences();
            AutomaticUpdatePreferenceChanged?.Invoke(value);
        }
    }

    private void SetPreparingUpdate(bool value)
    {
        if (!Set(ref _preparingUpdate, value, nameof(IsPreparingUpdate))) return;
        Changed(nameof(CanEdit)); Changed(nameof(CanRestartEngine)); NotifyHistory();
        InvalidatePresentation();
    }

    // Maintenance preparation is deliberately independent of Quit: an inability
    // to save must leave this session open with its pending edits retained.
    internal async Task<MixerSnapshot> PrepareForUpdateAsync(CancellationToken token)
    {
        if (IsPreparingUpdate || !CanRestartEngine)
            throw new InvalidOperationException("Wait until the audio engine is connected and workspace recovery is complete before updating.");
        SetPreparingUpdate(true);
        bool entered = false;
        try
        {
            await _engineGate.WaitAsync(token); entered = true;
            _restartOperations!.ValidateTarget();
            ConnectionStatus = "Saving your workspace before updating…";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var response = await _restartOperations.Request("snapshot", null, null, token);
                var current = Read(response);
                if (current.SettingsState is not ("loaded" or "new"))
                    throw new IOException("Finish workspace recovery before updating. Your current session has been kept open.");
                if (_unconfirmedUpdateBatch is not null && SameSavedConfiguration(_unconfirmedUpdateBatch.Configuration, current.Configuration))
                {
                    AcknowledgePending(_unconfirmedUpdateBatch, null); _unconfirmedUpdateBatch = null;
                    if (_pending.Count == 0)
                    {
                        Accept(current, forceConfiguration: true);
                        return current;
                    }
                }
                var batch = PrepareRestartConfiguration(current.Configuration);
                _unconfirmedUpdateBatch = batch;
                ProtocolResponse saved;
                try { saved = await _restartOperations.Request("configure", batch.Configuration, current.Configuration.Revision, token); }
                catch (Exception error) when (RestartFailure(error) && !token.IsCancellationRequested && attempt < 2)
                { continue; } // Read back an exact result before replaying an uncertain append.
                if (!saved.Ok)
                {
                    _unconfirmedUpdateBatch = null;
                    if (saved.Error.Contains("revision", StringComparison.OrdinalIgnoreCase)
                        || saved.Error.Contains("stale", StringComparison.OrdinalIgnoreCase)) continue;
                    throw new IOException("The update was not started because your changes could not be saved. " + saved.Error);
                }
                var confirmed = Read(saved);
                if (confirmed.SettingsState is not ("loaded" or "new")
                    || !SameSavedConfiguration(batch.Configuration, confirmed.Configuration))
                    throw new IOException("The saved workspace could not be confirmed. The update was not started.");
                AcknowledgePending(batch, null); _unconfirmedUpdateBatch = null;
                Accept(confirmed, forceConfiguration: true);
                return confirmed;
            }
            throw new IOException("The workspace changed repeatedly while saving. Your edits have been retained; try the update again.");
        }
        catch
        {
            ResumeAfterUpdatePreparation();
            throw;
        }
        finally { if (entered) _engineGate.Release(); }

        MixerSnapshot Read(ProtocolResponse response)
        {
            var snapshot = response.Ok ? response.Snapshot : null;
            if (snapshot is null)
                throw new IOException(response.Error.Length > 0 ? response.Error : "The audio engine did not confirm its saved workspace.");
            if (snapshot.Configuration is not { Version: 2, IsValidShape: true }
                || snapshot.Endpoints is null || snapshot.Status is null || snapshot.Status.Nodes is null
                || snapshot.EngineProtocolVersion > 1
                || !_preview && (snapshot.EngineProtocolVersion != 1 || snapshot.EngineVersion != ReleaseInfo.Version || snapshot.EnginePid <= 0))
                throw new IOException("The audio engine returned an incompatible workspace. Finish recovery or reopen the matching VineMix build before updating.");
            try { _ = snapshot.Configuration.Copy(); }
            catch (Exception error) when (error is NullReferenceException or ArgumentException)
            { throw new IOException("The saved workspace has an incomplete structure. The update was not started.", error); }
            return snapshot;
        }
    }

    private void ReconcileUnconfirmedUpdate(MixerConfiguration saved)
    {
        if (_unconfirmedUpdateBatch is not { } batch || !SameSavedConfiguration(batch.Configuration, saved)) return;
        AcknowledgePending(batch, null);
        _unconfirmedUpdateBatch = null;
    }

    internal void ResumeAfterUpdatePreparation()
    {
        if (_stop.IsCancellationRequested) return;
        if (!_preview)
        {
            _connection?.Dispose(); _connection = new();
            CanEdit = false;
        }
        SetPreparingUpdate(false);
        ConnectionStatus = _pending.Count > 0 ? "Update cancelled · your changes are still queued" : "Update cancelled · audio session kept open";
    }

    internal async Task ShutdownForUpdateAsync(MixerSnapshot prepared, CancellationToken token)
    {
        if (!IsPreparingUpdate) throw new InvalidOperationException("Save the workspace before stopping audio for an update.");
        bool entered = false;
        try
        {
            await _engineGate.WaitAsync(token); entered = true;
            var latest = await _restartOperations!.Request("snapshot", null, null, token);
            if (!latest.Ok || latest.Snapshot is null || _pending.Count != 0
                || latest.Snapshot.SettingsState is not ("loaded" or "new")
                || latest.Snapshot.Configuration is not { Version: 2, IsValidShape: true }
                || !SameSavedConfiguration(prepared.Configuration, latest.Snapshot.Configuration)
                || latest.Snapshot.EnginePid != prepared.EnginePid)
                throw new IOException("The workspace or audio engine changed during update preparation. Try again after it has saved.");
            ConnectionStatus = "Stopping audio for the update…";
            var response = await _restartOperations.Request("shutdown", null, null, token);
            if (!response.Ok) throw new IOException("The audio engine did not accept a clean shutdown. " + response.Error);
            if (!_preview) await _supervisor!.WaitForUpdateExitAsync(prepared.EnginePid, token);
            // Polling stays suspended until the coordinator commits its journal
            // and exits the desktop. If that handoff fails, preparation can be
            // cancelled and normal supervision can reconnect the saved workspace.
        }
        catch
        {
            ResumeAfterUpdatePreparation();
            throw;
        }
        finally { if (entered) _engineGate.Release(); }
    }
}
