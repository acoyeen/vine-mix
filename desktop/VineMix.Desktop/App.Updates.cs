using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

public partial class App
{
    private bool _updating;
    private string? _updateHealthTransaction;
    private static string? ReadUpdateRollbackNotice(string[] arguments)
    {
        int at = Array.FindIndex(arguments, argument => argument.Equals("--update-reverted", StringComparison.OrdinalIgnoreCase));
        if (at < 0) return null;
        try
        {
            if (at + 1 >= arguments.Length) throw new IOException("Missing update recovery record.");
            var context = UpdateTransaction.Verify(arguments[at + 1]);
            var journal = UpdateTransaction.ReadJournal(context);
            var current = UpdateProcessIdentity.Capture(Environment.ProcessId);
            if (journal.Phase != UpdatePhase.RolledBack || ReleaseInfo.Version != context.Data.CurrentVersion
                || !UpdateFiles.SamePath(current.ImagePath, context.TargetPath)
                || !UpdateFiles.EqualHash(current.Sha256, context.Data.Parent.Sha256))
                throw new IOException("The restored application identity did not match its recovery record.");
            return "The update to " + context.Data.Version + " did not finish. VineMix " + ReleaseInfo.Version
                + " was restored and reopened. Your saved workspace was preserved. Keep the update recovery folder if you report this problem.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Security.SecurityException or System.ComponentModel.Win32Exception or UpdateValidationException
            or System.Text.Json.JsonException or ArgumentException)
        {
            return "A previous update reported a problem. This version has opened; keep its update recovery folder and contact the maintainer before retrying.";
        }
    }
    internal async Task InstallUpdateAsync(DownloadedUpdate download)
    {
        if (_quitting || _updating || _window is null || _mixer is null)
            throw new InvalidOperationException("An update or shutdown is already in progress.");
        if (!_window.TryPrepareUpdateEditors()) return;
        _updating = true;
        _window.IsEnabled = false;
        PreparedUpdate? prepared = null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        try
        {
            var snapshot = await _mixer.PrepareForUpdateAsync(deadline.Token);
            if (snapshot.EnginePid <= 0) throw new IOException("The audio engine identity could not be confirmed for updating.");
            string physicalSettings = WorkspacePhysicalPath.Normalize(snapshot.PhysicalSettingsPath);
            if (string.IsNullOrWhiteSpace(physicalSettings) || !Path.IsPathFullyQualified(physicalSettings)
                || !UpdateFiles.SamePath(physicalSettings, WorkspacePhysicalPath.Resolve(WorkspacePaths.Settings)))
                throw new IOException("The engine's physical workspace location could not be confirmed. The current session has been kept open.");
            var identity = UpdateProcessIdentity.Capture(snapshot.EnginePid);
            prepared = await UpdateTransaction.PrepareAsync(download, ReleaseInfo.Distribution == "Standalone", identity,
                deadline.Token, physicalSettings);
            await prepared.StartWorkerAndWaitReadyAsync(deadline.Token);
            await _mixer.ShutdownForUpdateAsync(snapshot, deadline.Token);
            prepared.Commit();
            _quitting = true;
            if (_tray is not null) _tray.Visible = false;
            Shutdown();
        }
        finally
        {
            prepared?.Dispose();
            if (!_quitting)
            {
                _mixer.ResumeAfterUpdatePreparation();
                _window.IsEnabled = true;
                _updating = false;
            }
        }
    }

    private bool HandlePendingUpdates()
    {
        if (_updateHealthTransaction is not null) return false;
        using (var existing = TryOpenUpdateMutex())
        {
            if (existing is not null)
            {
                bool acquired;
                try { acquired = existing.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    MessageBox.Show("VineMix is finishing an update. It will reopen when the update is ready.", "VineMix update",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }
                existing.ReleaseMutex();
            }
        }
        foreach (var pending in UpdateTransaction.FindPending())
        {
            if (pending.Phase is UpdatePhase.Prepared or UpdatePhase.Ready && !pending.CanResume)
            { UpdateTransaction.CancelUncommitted(pending); continue; }
            if (pending.CanResume)
            {
                var answer = MessageBox.Show("A VineMix update was interrupted. Resume its recovery now? Your saved workspace will be preserved.",
                    "VineMix update recovery", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer == MessageBoxResult.Yes) UpdateTransaction.Resume(pending);
            }
            else MessageBox.Show("An earlier update needs repair before VineMix can open safely. Keep the update backup and contact the maintainer, or use the trusted installer for repair.\n\n" + pending.Message,
                "VineMix update recovery", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        return false;

        static Mutex? TryOpenUpdateMutex()
        {
            try { return Mutex.OpenExisting(WindowsUpdatePlatform.MutexName); }
            catch (WaitHandleCannotBeOpenedException) { return null; }
        }
    }

    private async Task ObserveUpdatedCandidateAsync(string transactionPath)
    {
        if (_mixer is null || _window is null) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var expected = await UpdateTransaction.WaitForCandidateRegistrationAsync(transactionPath, deadline.Token);
            ulong? firstFrames = null;
            int? enginePid = null;
            MixerSnapshot? healthy = null;
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var snapshot = _mixer.CurrentUpdateHealthSnapshot;
                if (snapshot is not null)
                {
                    if (snapshot.SettingsState != "loaded" || snapshot.Configuration.Version != expected.SettingsSchema
                        || snapshot.EngineProtocolVersion != expected.ProtocolVersion || snapshot.EngineVersion != expected.Version
                        || snapshot.EnginePid <= 0)
                        throw new IOException("The updated engine could not load the expected workspace and version.");
                    if (enginePid is null)
                    {
                        enginePid = snapshot.EnginePid;
                        UpdateTransaction.RecordCandidateEngine(transactionPath, UpdateProcessIdentity.Capture(snapshot.EnginePid));
                        firstFrames = snapshot.Status.AudioFrames;
                    }
                    else if (enginePid != snapshot.EnginePid) throw new IOException("The updated audio engine restarted during its health check.");
                    if (firstFrames is ulong first && snapshot.Status.AudioFrames > first) { healthy = snapshot; break; }
                }
                await Task.Delay(100, deadline.Token);
            }
            if (expected.SettingsPath is null
                || healthy is null
                || !UpdateFiles.SamePath(WorkspacePhysicalPath.Normalize(healthy.PhysicalSettingsPath), expected.SettingsPath)
                || !UpdateFiles.SamePath(WorkspacePhysicalPath.Resolve(WorkspacePaths.Settings), expected.SettingsPath)
                || !HashFile(WorkspacePaths.Settings).Equals(expected.SettingsSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The saved workspace changed before the update was confirmed. Its current contents have been preserved.");
            UpdateTransaction.ReportHealth(transactionPath, true, "Expected workspace, engine identity/protocol and advancing audio frames confirmed.");
            using var commitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (!await UpdateTransaction.WaitForHealthCommitAsync(transactionPath, commitDeadline.Token))
                throw new IOException("The update worker did not commit its successful restart.");
            _mixer.SetUpdateHealthMode(false);
            _window.IsEnabled = true;
            _window.Workspace.SetMessage("VineMix updated to " + ReleaseInfo.DisplayVersion + ". Your workspace is ready.", false);
            _ = _window.BeginAutomaticUpdateCheckAsync();
        }
        catch (Exception error)
        {
            try { UpdateTransaction.ReportHealth(transactionPath, false, "The updated application did not finish its workspace and engine checks."); }
            catch (Exception reportError) { Debug.WriteLine(reportError); }
            bool stopped = await _mixer.StopUpdateHealthAsync();
            if (stopped) { _quitting = true; Shutdown(1); }
            else
            {
                _window.Show();
                _window.IsEnabled = true;
                _window.Workspace.SetMessage("The update needs recovery. Quit VineMix from its tray menu, then reopen it to recover. " + error.Message, true);
            }
        }

        static string HashFile(string path)
        { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    }
}
