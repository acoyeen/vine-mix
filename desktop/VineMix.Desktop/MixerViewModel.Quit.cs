using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VineMix.Desktop;

internal sealed record QuitSaveResult(bool Saved, string Error, int PendingChanges);

public sealed partial class MixerViewModel
{
    private bool _preparingQuit;
    private PendingBatch? _unconfirmedQuitBatch;
    private MixerConfiguration? _quitRecoveryConfiguration;
    private bool _failedUpdateHealthQuitAllowed;
    internal bool IsPreparingQuit => _preparingQuit;
    internal bool CanQuitAfterFailedUpdateHealth => IsUpdateHealthMode && _failedUpdateHealthQuitAllowed;

    internal void AllowQuitAfterFailedUpdateHealth()
    {
        if (!IsUpdateHealthMode) throw new InvalidOperationException("Only a failed update health session can enter recovery shutdown.");
        // Keep the health/save barriers intact. This authorizes only the final
        // quit path after health recovery already failed to stop the candidate.
        _failedUpdateHealthQuitAllowed = true;
    }

    private void SetPreparingQuit(bool value)
    {
        if (!Set(ref _preparingQuit, value, nameof(IsPreparingQuit))) return;
        Changed(nameof(CanEdit)); Changed(nameof(CanRestartEngine)); NotifyHistory();
        InvalidatePresentation();
    }

    // Saving is reversible: polling is suspended, but its lifetime token and the
    // audio engine remain alive until the user has resolved any save failure.
    internal async Task<QuitSaveResult> PrepareForQuitAsync(CancellationToken token)
    {
        if (IsRestartingEngine || (IsPreparingUpdate || IsUpdateHealthMode) && !CanQuitAfterFailedUpdateHealth || _stop.IsCancellationRequested)
            throw new InvalidOperationException("Wait for the current audio engine operation to finish before quitting.");
        SetPreparingQuit(true);
        bool entered = false;
        try
        {
            EnsureUiPreferencesSaved();
            await _engineGate.WaitAsync(token); entered = true;
            EnsureUiPreferencesSaved();
            _quitRecoveryConfiguration ??= (_recoveryPending ?? _desired).Copy();
            if (_pending.Count == 0) return new(true, "", 0);
            if (IsUpdateHealthMode)
                return new(false, "The update health check failed. Keep a recovery copy of these queued changes or cancel quitting; saving to the update workspace remains blocked.", _pending.Count);
            if (_restartOperations is null)
                return new(false, "The audio engine is unavailable. Your queued edits are still in this window.", _pending.Count);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var current = Read(await _restartOperations.Request("snapshot", null, null, token));
                ReconcileUnconfirmedQuit(current.Configuration);
                if (_pending.Count == 0)
                {
                    Accept(current, forceConfiguration: true);
                    EnsureUiPreferencesSaved();
                    return new(true, "", 0);
                }
                // This strict replay throws on a conflict instead of rejecting
                // queued edits. The untouched desired copy remains recoverable.
                var batch = PrepareRestartConfiguration(current.Configuration);
                _quitRecoveryConfiguration = batch.Configuration.Copy();
                _unconfirmedQuitBatch = batch;
                ProtocolResponse saved;
                try { saved = await _restartOperations.Request("configure", batch.Configuration, current.Configuration.Revision, token); }
                catch (Exception error) when (RestartFailure(error) && !token.IsCancellationRequested && attempt < 2)
                { continue; } // Read back a committed-but-unconfirmed append before replaying it.
                if (!saved.Ok)
                {
                    _unconfirmedQuitBatch = null;
                    if (saved.Error.Contains("revision", StringComparison.OrdinalIgnoreCase)
                        || saved.Error.Contains("stale", StringComparison.OrdinalIgnoreCase)) continue;
                    throw new IOException(saved.Error.Length > 0 ? saved.Error : "The audio engine could not save your changes.");
                }
                var confirmed = Read(saved);
                if (!SameSavedConfiguration(batch.Configuration, confirmed.Configuration))
                    throw new IOException("The audio engine did not confirm the saved changes.");
                AcknowledgePending(batch, null); _unconfirmedQuitBatch = null;
                Accept(confirmed, forceConfiguration: true);
                EnsureUiPreferencesSaved();
                return new(true, "", 0);
            }
            throw new IOException("The saved workspace kept changing. Your queued edits have been retained.");
        }
        catch (Exception error) when (RestartFailure(error))
        {
            string detail = error is OperationCanceledException ? "The audio engine did not finish saving in time." : error.Message;
            _saveError = "Your latest changes have not been saved. " + detail;
            SaveStatus = "Changes not saved · quit paused";
            UpdateWarning();
            return new(false, detail, _pending.Count);
        }
        finally { if (entered) _engineGate.Release(); }

        static MixerSnapshot Read(ProtocolResponse response)
        {
            var snapshot = response.Ok ? response.Snapshot : null;
            if (snapshot is null)
                throw new IOException(response.Error.Length > 0 ? response.Error : "The audio engine did not confirm its saved workspace.");
            if (snapshot.SettingsState is not ("loaded" or "new")
                || snapshot.Configuration is not { Version: 2, IsValidShape: true })
                throw new IOException("Workspace recovery is required. Your queued edits can still be saved to a recovery copy.");
            return snapshot;
        }
    }

    private void ReconcileUnconfirmedQuit(MixerConfiguration saved)
    {
        if (_unconfirmedQuitBatch is not { } batch || !SameSavedConfiguration(batch.Configuration, saved)) return;
        AcknowledgePending(batch, null);
        _unconfirmedQuitBatch = null;
    }

    internal void CancelQuitPreparation()
    {
        if (_stop.IsCancellationRequested) return;
        SetPreparingQuit(false);
        _quitRecoveryConfiguration = null;
        SaveStatus = _pending.Count > 0 ? PendingSaveStatus
            : _uiPreferencesNeedSave ? "Workspace preferences need attention"
            : _preview ? "Preview · no settings are saved" : $"Saved · revision {_snapshot?.Configuration.Revision ?? 0}";
        ConnectionStatus = _pending.Count > 0 ? "Quit cancelled · your changes are still queued" : "Quit cancelled · audio session kept open";
    }

    internal string SerializeQuitRecovery()
    {
        if (!IsPreparingQuit) throw new InvalidOperationException("Prepare the pending workspace before making a quit recovery copy.");
        // This file contains the engine workspace. Do not let a successful
        // engine-only copy silently discard unsaved desktop group metadata.
        EnsureUiPreferencesSaved();
        var configuration = (_quitRecoveryConfiguration ?? _recoveryPending ?? _desired).Copy();
        if (_canvasView is { } view)
        { configuration.Graph.PanX = view.X; configuration.Graph.PanY = view.Y; configuration.Graph.Zoom = view.Zoom; }
        return JsonSerializer.Serialize(configuration, Protocol.Json);
    }

    internal void WriteQuitRecoveryCopy(string path)
    {
        string json = SerializeQuitRecovery();
        path = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".vinemix-recovery-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // A failed write never leaves a valid-looking partial recovery file,
            // and a recovery operation cannot replace existing saved work.
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                file.Write(bytes); file.Flush(true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    // This is the irreversible phase. App calls it only after a successful save,
    // a successful recovery copy or an explicit choice to discard pending edits.
    public async Task<string> StopAsync()
    {
        _stop.Cancel(); Changed(nameof(CanRestartEngine));
        if (_loop is not null) await _loop;
        await _engineGate.WaitAsync();
        try
        {
            CanEdit = false; InvalidatePresentation();
            if (_preview) return "";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try { await _connection!.RequestAsync("shutdown", null, null, timeout.Token); }
            catch (Exception error) when (RestartFailure(error)) { }
            await _supervisor!.StopForQuitAsync();
            return "";
        }
        finally { _engineGate.Release(); }
    }
}
