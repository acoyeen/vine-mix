using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

internal interface IUpdateWorkerPlatform
{
    IDisposable AcquireLock();
    bool IsSameLiveProcess(UpdateProcessIdentity identity);
    bool WaitForExit(UpdateProcessIdentity identity, TimeSpan timeout);
    void VerifyWorker(VerifiedUpdate context);
    void CheckFileVersion(string path, string numericVersion);
    UpdateRegistrySnapshot? ReadRegistration(VerifiedUpdate context);
    void RestoreRegistration(UpdateRegistrySnapshot original, UpdateRegistrySnapshot? expectedCurrent);
    void RegisterRecovery(VerifiedUpdate context, int attempt);
    void ClearRecovery(VerifiedUpdate context);
    int RunSetup(VerifiedUpdate context, Action<UpdateProcessIdentity> started);
    bool RunSmoke(VerifiedUpdate context, string evidenceDirectory, Action<UpdateProcessIdentity> started);
    string CandidateEngineHash(VerifiedUpdate context, string evidenceDirectory);
    UpdateProcessIdentity StartCandidate(VerifiedUpdate context);
    void ReopenPrevious(VerifiedUpdate context);
    bool WaitForCandidateAudioExit(VerifiedUpdate context, string? expectedEngineSha256, TimeSpan timeout);
    void Delay(int milliseconds);
}

/// <summary>A bounded, journaled worker; no process is killed and no settings are restored.</summary>
internal static class UpdateWorker
{
    internal static Task<int> RunAsync(string transactionPath) => Task.Run(() => Run(transactionPath,
        new WindowsUpdatePlatform(), UpdateTrust.Production));

    internal static int Run(string transactionPath, IUpdateWorkerPlatform platform, UpdateTrust trust, string? rootOverride = null)
    {
        VerifiedUpdate? context = null; UpdateJournal? journal = null; IDisposable? ownership = null;
        bool reopenPrevious = false;
        try
        {
            // Signature, asset bytes, private ACLs, path bounds and staged-worker
            // digest are checked before mutex/registry/IPC or target mutations.
            context = UpdateTransaction.Verify(transactionPath, trust, rootOverride);
            platform.VerifyWorker(context);
            platform.CheckFileVersion(context.AssetPath, context.Offer.Manifest.NumericVersion);
            ownership = platform.AcquireLock();
            journal = UpdateTransaction.ReadJournal(context);
            if (UpdateTransaction.Terminal(journal.Phase))
            {
                ClearTerminalRecovery(context, journal, platform);
                return 0;
            }
            if (journal.Attempts >= 3) throw new IOException("Automatic recovery reached its three-attempt limit. Retain the update backup and use manual repair.");
            if (journal.Phase == UpdatePhase.RecoveryRequired)
            {
                if (journal.ResumePhase is not { } resume) throw new IOException("This interrupted installation requires manual repair. Its backup is retained.");
                journal = journal with { Phase = resume, ResumePhase = null }; UpdateTransaction.SaveJournal(context, journal);
            }
            bool committed = UpdateFiles.TrySignal(context, "commit.json", out _);
            if (journal.Phase is UpdatePhase.Prepared or UpdatePhase.Ready && !committed)
            {
                if (DateTimeOffset.UtcNow - context.Data.CreatedUtc > TimeSpan.FromMinutes(30)
                    || !platform.IsSameLiveProcess(context.Data.Parent))
                    throw new IOException("The original application is no longer available to approve this update.");
                GuardOriginalTarget(context);
                platform.ReadRegistration(context);
                journal = journal with { Phase = UpdatePhase.Ready, Attempts = journal.Attempts + 1 };
                UpdateTransaction.SaveJournal(context, journal);
                platform.RegisterRecovery(context, journal.Attempts);
                UpdateFiles.WriteJson(Path.Combine(context.Directory, "ready.json"), context.Signal());
                var wait = Stopwatch.StartNew();
                while (!UpdateFiles.TrySignal(context, "commit.json", out _))
                {
                    if (UpdateFiles.TrySignal(context, "abort.json", out _) || wait.Elapsed > TimeSpan.FromMinutes(5)
                        || !platform.IsSameLiveProcess(context.Data.Parent))
                    {
                        journal = journal with { Phase = UpdatePhase.Cancelled, Message = "The update was cancelled before the application closed." };
                        UpdateTransaction.SaveJournal(context, journal); ClearTerminalRecovery(context, journal, platform); return 2;
                    }
                    platform.Delay(100);
                }
                committed = true;
            }
            else
            {
                journal = journal with { Attempts = journal.Attempts + 1 };
                UpdateTransaction.SaveJournal(context, journal);
                platform.RegisterRecovery(context, journal.Attempts);
            }
            if (!committed) throw new IOException("This update never received its durable commit marker.");
            if (journal.Phase == UpdatePhase.RollingBack) throw new IOException("Resuming the interrupted application rollback.");
            if (journal.Phase is UpdatePhase.Prepared or UpdatePhase.Ready)
            {
                journal = journal with { Phase = UpdatePhase.Committed }; UpdateTransaction.SaveJournal(context, journal);
            }
            if (journal.Phase is UpdatePhase.Committed or UpdatePhase.WaitingForExit)
            {
                journal = journal with { Phase = UpdatePhase.WaitingForExit }; UpdateTransaction.SaveJournal(context, journal);
                if (!platform.WaitForExit(context.Data.Parent, TimeSpan.FromSeconds(45))
                    || context.Data.Engine is { } engine && !platform.WaitForExit(engine, TimeSpan.FromSeconds(45)))
                    throw new IOException("The application or audio engine has not exited. No process was stopped; retry after quitting it normally.");
                GuardOriginalTarget(context);
                UpdateTransaction.VerifyWorkspace(context);
                var backup = Backup(context, platform);
                journal = journal with { Phase = UpdatePhase.BackedUp, Backup = backup };
                UpdateTransaction.SaveJournal(context, journal);
            }
            if (journal.Phase == UpdatePhase.BackedUp)
            {
                RequireBackup(journal); GuardOriginalTarget(context); GuardSnapshot(context.TargetDirectory, journal.Backup!.Files);
                // Recheck signed bytes immediately before launching Setup or
                // replacing the one launcher, even after a recovery restart.
                UpdateFiles.VerifyAsset(context.AssetPath, context.Data.Standalone ? context.Offer.Manifest.Standalone : context.Offer.Manifest.Installer);
                journal = journal with { Phase = UpdatePhase.Applying }; UpdateTransaction.SaveJournal(context, journal);
                int code = 0;
                if (context.Data.Standalone) ReplaceStandalone(context);
                else
                {
                    code = platform.RunSetup(context, identity =>
                    {
                        journal = journal with { SetupProcess = identity }; UpdateTransaction.SaveJournal(context, journal);
                    });
                }
                // Observe only explicit old/new payload inventories after this
                // bounded attempt. Unlisted user files never enter rollback.
                var observed = ObserveCandidate(context, journal.Backup!, platform, requireValidPackage: false);
                journal = journal with { Phase = UpdatePhase.CandidateInstalled, Candidate = observed };
                UpdateTransaction.SaveJournal(context, journal);
                if (code != 0) throw new IOException($"Setup did not complete successfully (exit code {code}). See setup.log in the update recovery folder. Restoring the prior owned application payload.");
            }
            else if (journal.Phase == UpdatePhase.Applying)
            {
                // A power loss can occur after Setup changes one file and before
                // the postimage is journaled. There is no transactional Inno/NTFS
                // commit across all files and registry. Do not guess those bytes.
                if (journal.SetupProcess is { } setup && !platform.WaitForExit(setup, TimeSpan.FromSeconds(60)))
                    throw new IOException("Setup is still running. Keep the recovery folder and wait for Setup to finish.");
                if (context.Data.Standalone && UpdateFiles.EqualHash(UpdateFiles.Hash(context.TargetPath), context.Offer.Manifest.Standalone.Sha256))
                {
                    journal = journal with { Phase = UpdatePhase.CandidateInstalled, Candidate = ObserveCandidate(context, journal.Backup!, platform, true) };
                    UpdateTransaction.SaveJournal(context, journal);
                }
                else if (context.Data.Standalone && UpdateFiles.EqualHash(UpdateFiles.Hash(context.TargetPath), context.Data.Parent.Sha256))
                {
                    ReplaceStandalone(context);
                    journal = journal with { Phase = UpdatePhase.CandidateInstalled, Candidate = ObserveCandidate(context, journal.Backup!, platform, true) };
                    UpdateTransaction.SaveJournal(context, journal);
                }
                else throw new IOException("Installation was interrupted before its postimage was recorded. The prior payload and uninstall registration are retained for manual repair; automatic rollback will not overwrite unknown changes.");
            }
            if (journal.Phase == UpdatePhase.CandidateInstalled)
            {
                RequireBackup(journal); GuardSnapshot(context.TargetDirectory, journal.Candidate!.Files);
                ValidateCandidate(context, platform);
                UpdateTransaction.VerifyWorkspace(context);
                string evidence = Path.Combine(context.Directory, "smoke-" + Guid.NewGuid().ToString("N"));
                if (!platform.RunSmoke(context, evidence, identity =>
                { journal = journal with { SmokeProcess = identity }; UpdateTransaction.SaveJournal(context, journal); }))
                    throw new IOException("The updated application failed its inert package check.");
                string engineHash = platform.CandidateEngineHash(context, evidence);
                journal = journal with { Phase = UpdatePhase.AwaitingHealth, CandidateEngineSha256 = engineHash };
                UpdateTransaction.SaveJournal(context, journal);
                var candidate = platform.StartCandidate(context);
                journal = journal with { CandidateProcess = candidate }; UpdateTransaction.SaveJournal(context, journal);
            }
            if (journal.Phase == UpdatePhase.AwaitingHealth)
            {
                if (journal.CandidateProcess is null) throw new IOException("The candidate launch was interrupted. Manual recovery must confirm no candidate is running before restoring the backup.");
                var watch = Stopwatch.StartNew(); UpdateSignal? health = null;
                while (watch.Elapsed < TimeSpan.FromSeconds(120))
                {
                    if (UpdateFiles.TrySignal(context, "health.json", out health)) break;
                    if (!platform.IsSameLiveProcess(journal.CandidateProcess)) break;
                    platform.Delay(100);
                }
                bool healthy = health is { Success: true } && health.ProcessId == journal.CandidateProcess.ProcessId
                    && health.StartTimeUtcTicks == journal.CandidateProcess.StartTimeUtcTicks
                    && platform.IsSameLiveProcess(journal.CandidateProcess);
                if (!healthy) throw new IOException("The updated application did not confirm saved-workspace, engine-protocol and advancing-audio health. The retained backup will be restored after the candidate exits normally.");
                GuardSnapshot(context.TargetDirectory, journal.Candidate!.Files);
                UpdateTransaction.VerifyWorkspace(context);
                journal = journal with { Phase = UpdatePhase.HealthCommitted, Message = "The new application confirmed workspace and audio-engine health." };
                UpdateTransaction.SaveJournal(context, journal);
            }
            if (journal.Phase == UpdatePhase.HealthCommitted)
            {
                journal = journal with { Phase = UpdatePhase.Completed }; UpdateTransaction.SaveJournal(context, journal);
                ClearTerminalRecovery(context, journal, platform); return 0;
            }
            throw new IOException("This update requires manual recovery. Its previous application payload is retained.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception
            or JsonException or UpdateValidationException or System.Security.SecurityException or ArgumentException)
        {
            if (context is null || journal is null) return 1; // Untrusted handoff: no writes.
            string message = error.Message;
            try
            {
                bool canRestore = journal.Backup is not null && journal.Candidate is not null
                    && journal.Phase is UpdatePhase.CandidateInstalled or UpdatePhase.AwaitingHealth or UpdatePhase.RollingBack;
                if (canRestore)
                {
                    if (journal.CandidateProcess is { } candidate && !platform.WaitForExit(candidate, TimeSpan.FromSeconds(30)))
                        throw new IOException("The candidate is still running. No process was killed and no application files were restored. Quit it normally before recovery.");
                    if (journal.SmokeProcess is { } smoke && !platform.WaitForExit(smoke, TimeSpan.FromSeconds(30)))
                        throw new IOException("The inert candidate check is still running. It was not killed and application files were not restored.");
                    if (!platform.WaitForCandidateAudioExit(context, journal.CandidateEngineSha256, TimeSpan.FromSeconds(45)))
                        throw new IOException("A candidate audio engine is still running. No process was killed and the prior application files were not restored.");
                    journal = journal with { Phase = UpdatePhase.RollingBack, Message = message }; UpdateTransaction.SaveJournal(context, journal);
                    Restore(context, journal.Backup!, journal.Candidate!, platform);
                    journal = journal with { Phase = UpdatePhase.RolledBack, Message = message + " The previous owned application payload was restored; personal settings were not replaced." };
                    UpdateTransaction.SaveJournal(context, journal); ClearTerminalRecovery(context, journal, platform);
                    reopenPrevious = true;
                    return 3;
                }
                journal = journal with { Phase = UpdatePhase.RecoveryRequired, ResumePhase = SafeResume(context, journal), Message = message };
                UpdateTransaction.SaveJournal(context, journal);
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
            {
                try { UpdateTransaction.SaveJournal(context, journal with { Phase = UpdatePhase.RecoveryRequired, ResumePhase = SafeResume(context, journal), Message = message + " Recovery stopped: " + recoveryError.Message }); }
                catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException) { Debug.WriteLine(journalError); }
            }
            return 1;
        }
        finally
        {
            ownership?.Dispose();
            // Ordinary startup must see the released global update mutex and
            // the durable terminal rollback checkpoint before reopening audio.
            if (reopenPrevious && context is not null && journal is not null)
            {
                try { platform.ReopenPrevious(context); }
                catch (Exception reopenError) when (reopenError is IOException or UnauthorizedAccessException
                    or InvalidOperationException or Win32Exception or ArgumentException or System.Security.SecurityException)
                {
                    try { UpdateTransaction.SaveJournal(context, journal with
                    { Phase = UpdatePhase.RolledBack, Message = journal.Message + " The previous application could not reopen automatically. Open the original launcher manually. " + reopenError.Message }); }
                    catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException) { Debug.WriteLine(journalError); }
                }
            }
        }
    }

    private static void ClearTerminalRecovery(VerifiedUpdate context, UpdateJournal journal, IUpdateWorkerPlatform platform)
    {
        string? failure = null;
        try { platform.ClearRecovery(context); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or Win32Exception or System.Security.SecurityException)
        { failure = error.Message.Length > 512 ? error.Message[..512] : error.Message; }

        // Registration housekeeping cannot undo a durably completed operation.
        // A retained RunOnce command re-enters only this terminal cleanup path;
        // normal startup ignores terminal history and never repeats the update.
        string receipt = Path.Combine(context.Directory, "recovery-cleanup.json");
        if (failure is null && !File.Exists(receipt)) return;
        try
        {
            UpdateFiles.WriteJson(receipt, new { pending = failure is not null, phase = journal.Phase.ToString(),
                updatedUtc = DateTimeOffset.UtcNow, message = failure ?? "Recovery registration cleanup completed." });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Security.SecurityException)
        { Debug.WriteLine(error); } // Retain the terminal journal even if diagnostic storage is unavailable.
    }

    private static UpdatePhase? SafeResume(VerifiedUpdate context, UpdateJournal journal) => journal.Phase switch
    {
        UpdatePhase.Committed or UpdatePhase.WaitingForExit or UpdatePhase.BackedUp or UpdatePhase.CandidateInstalled
            or UpdatePhase.AwaitingHealth or UpdatePhase.HealthCommitted or UpdatePhase.RollingBack => journal.Phase,
        UpdatePhase.Applying when journal.Backup is not null && journal.Candidate is not null => UpdatePhase.RollingBack,
        UpdatePhase.Applying when context.Data.Standalone && journal.Backup is not null && File.Exists(context.TargetPath)
            && (UpdateFiles.EqualHash(UpdateFiles.Hash(context.TargetPath), context.Offer.Manifest.Standalone.Sha256)
                || UpdateFiles.EqualHash(UpdateFiles.Hash(context.TargetPath), context.Data.Parent.Sha256)) => UpdatePhase.Applying,
        _ => null
    };

    private static void GuardOriginalTarget(VerifiedUpdate context)
    {
        if (!UpdateFiles.EqualHash(UpdateFiles.Hash(context.TargetPath), context.Data.Parent.Sha256))
            throw new IOException("The original application has changed since this update was prepared. Its files were not replaced.");
    }
    private static void RequireBackup(UpdateJournal journal)
    {
        if (journal.Backup is null || journal.Backup.Files.Length == 0) throw new IOException("The update recovery inventory is missing.");
    }
    internal static UpdateBackup Backup(VerifiedUpdate context, IUpdateWorkerPlatform platform)
    {
        var registry = platform.ReadRegistration(context);
        var files = context.Data.Standalone
            ? new[] { Entry(context.TargetDirectory, Path.GetFileName(context.TargetPath)) }
            : InstalledInventory(context, context.Data.CurrentVersion, null);
        string root = Path.Combine(context.Directory, "backup"); UpdateFiles.CreatePrivateDirectory(root);
        foreach (var file in files)
        {
            string source = UpdateFiles.RelativeFile(context.TargetDirectory, file.Path), destination = UpdateFiles.RelativeFile(root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination)) GuardSnapshot(root, [file]);
            else UpdateFiles.CopyPrivateChecked(source, destination, file.Size, file.Sha256);
        }
        return new(files, registry);
    }
    internal static void ReplaceStandalone(VerifiedUpdate context)
    {
        GuardOriginalTarget(context);
        string staged = context.TargetPath + ".vinemix-" + context.Data.Id + ".new";
        string retained = context.TargetPath + ".vinemix-" + context.Data.Id + ".old";
        var asset = context.Offer.Manifest.Standalone;
        if (File.Exists(staged)) UpdateFiles.VerifyAsset(staged, asset);
        else UpdateFiles.CopyChecked(context.AssetPath, staged, asset.Size, asset.Sha256);
        // Both files are beside the original launcher, so replacement remains
        // on its volume even when private recovery storage is on another drive.
        GuardOriginalTarget(context); UpdateFiles.VerifyAsset(staged, asset);
        File.Replace(staged, context.TargetPath, retained, false);
    }
    internal static UpdateBackup ObserveCandidate(VerifiedUpdate context, UpdateBackup original,
        IUpdateWorkerPlatform platform, bool requireValidPackage)
    {
        var files = new Dictionary<string, UpdateFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var previous in original.Files)
        {
            string path = UpdateFiles.RelativeFile(context.TargetDirectory, previous.Path);
            if (File.Exists(path)) files.Add(previous.Path, Entry(context.TargetDirectory, previous.Path));
        }
        if (!context.Data.Standalone)
        {
            try
            {
                foreach (var entry in InstalledInventory(context, context.Data.Version, context.Offer.Manifest.NumericVersion)) files[entry.Path] = entry;
            }
            catch (Exception error) when (!requireValidPackage && error is IOException or JsonException) { Debug.WriteLine(error); }
        }
        return new(files.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray(), platform.ReadRegistration(context));
    }
    private static void ValidateCandidate(VerifiedUpdate context, IUpdateWorkerPlatform platform)
    {
        platform.CheckFileVersion(context.TargetPath, context.Offer.Manifest.NumericVersion);
        if (context.Data.Standalone) UpdateFiles.VerifyAsset(context.TargetPath, context.Offer.Manifest.Standalone);
        else
        {
            InstalledInventory(context, context.Data.Version, context.Offer.Manifest.NumericVersion);
            platform.CheckFileVersion(Path.Combine(context.TargetDirectory, "VineMix.Engine.exe"), context.Offer.Manifest.NumericVersion);
        }
    }
    internal static UpdateFileEntry[] InstalledInventory(VerifiedUpdate context, string version, string? numericVersion)
    {
        try { return ReadInstalledInventory(context, version, numericVersion); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { throw new IOException("The installed package inventory is malformed. Retain the update backup and use Setup for manual repair.", error); }
    }
    private static UpdateFileEntry[] ReadInstalledInventory(VerifiedUpdate context, string version, string? numericVersion)
    {
        string path = Path.Combine(context.TargetDirectory, "SetupSupport", "package-manifest.json");
        using var document = JsonDocument.Parse(UpdateFiles.ReadBytes(path, 2 * 1024 * 1024));
        var root = document.RootElement;
        if (root.GetProperty("version").GetString() != version || numericVersion is not null && root.GetProperty("numericVersion").GetString() != numericVersion)
            throw new IOException("The installed package inventory does not match the expected release. Use Setup for manual repair.");
        var entries = new Dictionary<string, UpdateFileEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var item in root.GetProperty("files").EnumerateArray())
        {
            if (entries.Count >= 2048) throw new IOException("The package inventory contains too many files.");
            string relative = item.GetProperty("path").GetString() ?? "";
            long size = item.GetProperty("size").GetInt64(); string hash = item.GetProperty("sha256").GetString() ?? "";
            string file = UpdateFiles.RelativeFile(context.TargetDirectory, relative);
            ProtectSettings(context, file);
            if (size is <= 0 or > UpdateTrust.MaximumAssetBytes || (total += size) > 2L * UpdateTrust.MaximumAssetBytes
                || !UpdateFiles.EqualHash(hash, hash) || !entries.TryAdd(relative, new(relative, size, hash)))
                throw new IOException("The installed package inventory is invalid or ambiguous.");
            if (new FileInfo(file).Length != size || !UpdateFiles.EqualHash(UpdateFiles.Hash(file), hash))
                throw new IOException("An installed application payload file differs from its recorded inventory. Use manual repair.");
        }
        if (!entries.ContainsKey("VineMix.exe") || !entries.ContainsKey("VineMix.Engine.exe")
            || !UpdateFiles.SamePath(context.TargetPath, Path.Combine(context.TargetDirectory, "VineMix.exe")))
            throw new IOException("The inventory does not own this application and engine pair.");
        foreach (string relative in new[] { "SetupSupport/package-manifest.json", "installation.ini", "unins000.exe", "unins000.dat", "unins000.msg" })
        {
            string file = UpdateFiles.RelativeFile(context.TargetDirectory, relative);
            if (File.Exists(file)) entries[relative] = Entry(context.TargetDirectory, relative);
            else if (relative is "installation.ini" or "unins000.exe" or "unins000.dat") throw new IOException("The owned installation bookkeeping is incomplete. Use Setup for manual repair.");
        }
        return entries.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }
    private static UpdateFileEntry Entry(string root, string relative)
    {
        string path = UpdateFiles.RelativeFile(root, relative);
        return new(relative, new FileInfo(path).Length, UpdateFiles.Hash(path));
    }
    internal static void GuardSnapshot(string root, IReadOnlyList<UpdateFileEntry> entries)
    {
        foreach (var entry in entries)
        {
            string path = UpdateFiles.RelativeFile(root, entry.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Size || !UpdateFiles.EqualHash(UpdateFiles.Hash(path), entry.Sha256))
                throw new IOException("An application payload file changed after its update checkpoint. Recovery preserved that newer file.");
        }
    }
    internal static void Restore(VerifiedUpdate context, UpdateBackup original, UpdateBackup candidate, IUpdateWorkerPlatform platform)
    {
        string backup = Path.Combine(context.Directory, "backup");
        foreach (var entry in original.Files.Concat(candidate.Files)) ProtectSettings(context, UpdateFiles.RelativeFile(context.TargetDirectory, entry.Path));
        GuardSnapshot(backup, original.Files);
        var after = candidate.Files.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var before = original.Files.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        // Resuming rollback accepts only the checkpoint's postimage or the
        // known original bytes already restored by a prior interrupted pass.
        foreach (string relative in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase))
        {
            string path = UpdateFiles.RelativeFile(context.TargetDirectory, relative);
            if (!File.Exists(path))
            {
                if (after.ContainsKey(relative) && before.ContainsKey(relative)) throw new IOException("An expected payload file disappeared after the recovery checkpoint.");
                continue;
            }
            string hash = UpdateFiles.Hash(path);
            if (!(after.TryGetValue(relative, out var current) && UpdateFiles.EqualHash(hash, current.Sha256))
                && !(before.TryGetValue(relative, out var old) && UpdateFiles.EqualHash(hash, old.Sha256)))
                throw new IOException("An application payload file changed after its update checkpoint. Recovery preserved that newer file.");
        }
        foreach (var old in original.Files)
        {
            string target = UpdateFiles.RelativeFile(context.TargetDirectory, old.Path);
            if (!after.ContainsKey(old.Path) && File.Exists(target) && !UpdateFiles.EqualHash(UpdateFiles.Hash(target), old.Sha256))
                throw new IOException("A formerly missing payload file was recreated after the checkpoint; recovery retained it.");
        }
        // Check the uninstall postimage before any file mutation too.
        var currentRegistry = original.Registry is null ? null : platform.ReadRegistration(context);
        if (original.Registry is not null && !WindowsUpdatePlatform.SameRegistration(currentRegistry, candidate.Registry)
            && !WindowsUpdatePlatform.SameRegistration(currentRegistry, original.Registry))
            throw new IOException("Uninstall registration changed after the update checkpoint; recovery retained it.");
        foreach (var old in original.Files)
        {
            string target = UpdateFiles.RelativeFile(context.TargetDirectory, old.Path);
            if (File.Exists(target) && UpdateFiles.EqualHash(UpdateFiles.Hash(target), old.Sha256)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + ".vinemix-restore-" + context.Data.Id;
            if (File.Exists(temporary)) GuardSnapshot(context.TargetDirectory, [old with { Path = Path.GetRelativePath(context.TargetDirectory, temporary).Replace('\\', '/') }]);
            else UpdateFiles.CopyChecked(UpdateFiles.RelativeFile(backup, old.Path), temporary, old.Size, old.Sha256);
            if (File.Exists(target)) File.Replace(temporary, target, null, false); else File.Move(temporary, target);
        }
        var oldPaths = original.Files.Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var added in candidate.Files.Where(entry => !oldPaths.Contains(entry.Path)))
        {
            string path = UpdateFiles.RelativeFile(context.TargetDirectory, added.Path);
            if (!File.Exists(path)) continue;
            if (!UpdateFiles.EqualHash(UpdateFiles.Hash(path), added.Sha256)) throw new IOException("A new application file changed during recovery; it was retained.");
            File.Delete(path);
        }
        if (original.Registry is not null && !WindowsUpdatePlatform.SameRegistration(currentRegistry, original.Registry))
            platform.RestoreRegistration(original.Registry, candidate.Registry);
    }
    private static void ProtectSettings(VerifiedUpdate context, string path)
    {
        if (context.Data.SettingsPath is { } settings && (UpdateFiles.SamePath(settings, path)
            || Path.GetFullPath(path).StartsWith(Path.GetFullPath(settings) + ".", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("The application inventory includes the active custom workspace or its recovery files. Those settings will not be replaced.");
    }
}

internal sealed class WindowsUpdatePlatform : IUpdateWorkerPlatform
{
    internal const string ApplicationId = "bac29135-ce09-4029-af38-d22d1e177d4f";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ApplicationId + "_is1";
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    internal static string MutexName => @"Global\VineMix.Update." + (WindowsIdentity.GetCurrent().User?.Value
        ?? throw new IOException("The Windows user cannot be identified."));

    public IDisposable AcquireLock()
    {
        var mutex = new Mutex(false, MutexName); bool owned;
        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) { mutex.Dispose(); throw new IOException("Another update or update recovery is already active."); }
        return new MutexLease(mutex);
    }
    private sealed class MutexLease(Mutex mutex) : IDisposable
    { public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); } }
    internal static bool SameProcess(UpdateProcessIdentity left, UpdateProcessIdentity right)
        => left.ProcessId == right.ProcessId && left.StartTimeUtcTicks == right.StartTimeUtcTicks
            && UpdateFiles.SamePath(left.ImagePath, right.ImagePath) && UpdateFiles.EqualHash(left.Sha256, right.Sha256);
    public bool IsSameLiveProcess(UpdateProcessIdentity identity)
    {
        try { return SameProcess(UpdateProcessIdentity.Capture(identity.ProcessId), identity); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    public bool WaitForExit(UpdateProcessIdentity identity, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            // A reused PID is a different process, never a target to stop/wait.
            if (process.StartTime.ToUniversalTime().Ticks != identity.StartTimeUtcTicks) return true;
            string? image = UpdateProcessIdentity.TryReadImagePath(process);
            if (image is null) return true;
            if (!UpdateFiles.SamePath(image, identity.ImagePath)) throw new IOException("A process identity changed unexpectedly.");
            return process.WaitForExit(checked((int)timeout.TotalMilliseconds));
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }
    public void VerifyWorker(VerifiedUpdate context)
    {
        string worker = Environment.ProcessPath ?? throw new IOException("The update worker path cannot be identified.");
        if (!UpdateFiles.SamePath(worker, context.WorkerPath) || !UpdateFiles.EqualHash(UpdateFiles.Hash(worker), context.Data.Parent.Sha256)
            || context.Data.CurrentVersion != ReleaseInfo.Version)
            throw new IOException("Update recovery must use the copied original application worker.");
    }
    public void CheckFileVersion(string path, string numericVersion)
    {
        UpdateFiles.NoReparse(path);
        if (!Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var actual)
            || !Version.TryParse(numericVersion, out var expected) || actual != expected)
            throw new IOException("An update executable does not carry the expected release file version.");
    }
    public UpdateRegistrySnapshot? ReadRegistration(VerifiedUpdate context) => context.Data.Standalone ? null : ReadOwnedRegistration(context.TargetDirectory);
    internal static UpdateRegistrySnapshot ReadOwnedRegistration(string targetDirectory)
    {
        string target = Path.GetFullPath(targetDirectory); UpdateFiles.NoReparse(target);
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VineMix");
        if (UpdateFiles.SamePath(target, data) || UpdateFiles.IsInside(target, data)) throw new IOException("The application installation cannot be the user's saved-data directory.");
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = hive.OpenSubKey(UninstallKey, false) ?? throw new IOException("This copy has no owned per-user installation registration. Use Setup for manual repair.");
        if (key.SubKeyCount != 0 || key.ValueCount > 128 || key.GetValue("InstallLocation") is not string install
            || !UpdateFiles.SamePath(install, target) || key.GetValue("UninstallString") is not string uninstall
            || !StringComparer.OrdinalIgnoreCase.Equals(uninstall, Quote(Path.Combine(target, "unins000.exe"))))
            throw new IOException("The existing uninstall registration does not own this exact application folder.");
        string ini = Path.Combine(target, "installation.ini"); UpdateFiles.NoReparse(ini);
        string section = "", appId = "";
        foreach (string line in File.ReadAllLines(ini))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) section = trimmed[1..^1];
            else if (section.Equals("Installation", StringComparison.OrdinalIgnoreCase) && trimmed.StartsWith("ApplicationId=", StringComparison.OrdinalIgnoreCase))
            { if (appId.Length > 0) throw new IOException("The installation ownership marker is ambiguous."); appId = trimmed[14..].Trim(); }
        }
        if (!appId.Equals(ApplicationId, StringComparison.OrdinalIgnoreCase)) throw new IOException("This installation ownership marker is missing or belongs to another product.");
        return new(UninstallKey, target, SnapshotValues(key));
    }
    private static UpdateRegistryValue[] SnapshotValues(RegistryKey key) => key.GetValueNames().Order(StringComparer.Ordinal).Select(name =>
    {
        var kind = key.GetValueKind(name); object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString when value is string text => new UpdateRegistryValue(name, (int)kind, [text], [], 0),
            RegistryValueKind.MultiString when value is string[] strings => new(name, (int)kind, strings, [], 0),
            RegistryValueKind.Binary when value is byte[] bytes => new(name, (int)kind, [], bytes, 0),
            RegistryValueKind.DWord when value is int number => new(name, (int)kind, [], [], number),
            RegistryValueKind.QWord when value is long number => new(name, (int)kind, [], [], number),
            _ => throw new IOException("The owned uninstall key contains an unsupported value type; use manual repair.")
        };
    }).ToArray();
    internal static bool SameRegistration(UpdateRegistrySnapshot? left, UpdateRegistrySnapshot? right)
        => JsonSerializer.Serialize(left, UpdateTransaction.Json) == JsonSerializer.Serialize(right, UpdateTransaction.Json);
    public void RestoreRegistration(UpdateRegistrySnapshot original, UpdateRegistrySnapshot? expectedCurrent)
    {
        if (original.Key != UninstallKey || !SameRegistration(ReadOwnedRegistration(original.TargetDirectory), expectedCurrent))
            throw new IOException("Owned uninstall registration changed before rollback.");
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = hive.OpenSubKey(UninstallKey, true) ?? throw new IOException("The owned uninstall registration is no longer present.");
        var names = original.Values.Select(value => value.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string name in key.GetValueNames()) if (!names.Contains(name)) key.DeleteValue(name);
        foreach (var value in original.Values)
        {
            var kind = (RegistryValueKind)value.Kind;
            object data = kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => value.Strings.Single(),
                RegistryValueKind.MultiString => value.Strings,
                RegistryValueKind.Binary => value.Bytes,
                RegistryValueKind.DWord => checked((int)value.Number), RegistryValueKind.QWord => value.Number,
                _ => throw new IOException("The saved uninstall registration has an unsupported value type.")
            };
            key.SetValue(value.Name, data, kind);
        }
        key.Flush();
    }
    public void RegisterRecovery(VerifiedUpdate context, int attempt)
    {
        if (attempt >= 3) return;
        string command = RecoveryCommand(context);
        if (command.Length > 260) throw new IOException("This update recovery command exceeds the Windows RunOnce limit. Use manual Setup for this path.");
        using var key = Registry.CurrentUser.CreateSubKey(RunOnceKey, true);
        // Different names per bounded attempt prevent an exiting RunOnce stub
        // from deleting a normalized child's next recovery registration.
        key.SetValue("!VineMix.Update." + context.Data.Id + "." + (attempt + 1), command, RegistryValueKind.String); key.Flush();
    }
    public void ClearRecovery(VerifiedUpdate context)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunOnceKey, true);
        if (key is null) return;
        for (int attempt = 1; attempt <= 3; ++attempt)
        {
            string name = "!VineMix.Update." + context.Data.Id + "." + attempt;
            if (key.GetValue(name) is string command && command == RecoveryCommand(context)) key.DeleteValue(name);
        }
        key.Flush();
    }
    private static string RecoveryCommand(VerifiedUpdate context) => Quote(context.WorkerPath) + " --apply-update " + Quote(context.TransactionPath);
    private static string Quote(string value)
    {
        if (value.Contains('"') || value.Any(char.IsControl)) throw new IOException("The update path cannot be represented as a Windows launch argument.");
        return "\"" + value + "\"";
    }
    public int RunSetup(VerifiedUpdate context, Action<UpdateProcessIdentity> started)
    {
        ReadOwnedRegistration(context.TargetDirectory);
        UpdateFiles.NoReparse(Path.Combine(context.Directory, "setup.log"));
        var start = CreateSetupStart(context);
        using var process = Process.Start(start) ?? throw new IOException("The verified Setup could not start.");
        if (UpdateProcessIdentity.TryCaptureStarted(process, context.AssetPath) is { } identity) started(identity);
        // Setup is never killed. A timeout leaves its exact identity journaled.
        if (!process.WaitForExit(10 * 60 * 1000)) throw new IOException("Setup is still running. Its recovery journal was retained; no process was killed.");
        return process.ExitCode;
    }
    internal static ProcessStartInfo CreateSetupStart(VerifiedUpdate context)
    {
        var start = Start(context.AssetPath, context.Directory);
        foreach (string argument in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/ACCEPTTERMS=1", "/DRIVERS=none", "/DIR=" + context.TargetDirectory,
            "/LOG=" + Path.Combine(context.Directory, "setup.log") }) start.ArgumentList.Add(argument);
        return start;
    }
    public bool RunSmoke(VerifiedUpdate context, string evidenceDirectory, Action<UpdateProcessIdentity> started)
    {
        var start = Start(context.TargetPath, context.TargetDirectory);
        start.ArgumentList.Add("--release-smoke"); start.ArgumentList.Add(evidenceDirectory);
        using var process = Process.Start(start) ?? throw new IOException("The candidate package check could not start.");
        if (UpdateProcessIdentity.TryCaptureStarted(process, context.TargetPath) is { } identity) started(identity);
        if (!process.WaitForExit(60 * 1000)) throw new IOException("The inert candidate check is still running. It was not stopped; recovery was retained.");
        if (process.ExitCode != 0) return false;
        using var report = JsonDocument.Parse(UpdateFiles.ReadBytes(Path.Combine(evidenceDirectory, "results.json"), 128 * 1024));
        var root = report.RootElement;
        return root.GetProperty("passed").GetBoolean() && root.GetProperty("version").GetString() == context.Data.Version
            && root.GetProperty("mode").GetString() == (context.Data.Standalone ? "Standalone" : "Installed");
    }
    public string CandidateEngineHash(VerifiedUpdate context, string evidenceDirectory)
    {
        if (!context.Data.Standalone) return UpdateFiles.Hash(Path.Combine(context.TargetDirectory, "VineMix.Engine.exe"));
        using var report = JsonDocument.Parse(UpdateFiles.ReadBytes(Path.Combine(evidenceDirectory, "results.json"), 128 * 1024));
        string engine = report.RootElement.GetProperty("extractedEngine").GetString() ?? throw new IOException("The verified candidate runtime evidence has no engine path.");
        if (!UpdateFiles.IsInside(engine, evidenceDirectory) || Path.GetFileName(engine) != "VineMix.Engine.exe")
            throw new IOException("The verified engine evidence escapes its private extraction folder.");
        CheckFileVersion(engine, context.Offer.Manifest.NumericVersion);
        return UpdateFiles.Hash(engine);
    }
    public UpdateProcessIdentity StartCandidate(VerifiedUpdate context)
    {
        var start = Start(context.TargetPath, context.TargetDirectory);
        start.ArgumentList.Add("--update-health"); start.ArgumentList.Add(context.TransactionPath); start.ArgumentList.Add("--standalone-context");
        if (context.Data.SettingsPath is { } settings) { start.ArgumentList.Add("--settings"); start.ArgumentList.Add(settings); }
        using var process = Process.Start(start) ?? throw new IOException("The updated application could not start.");
        return UpdateProcessIdentity.TryCaptureStarted(process, context.TargetPath)
            ?? throw new IOException("The updated application exited before it could confirm startup health.");
    }
    public void ReopenPrevious(VerifiedUpdate context)
    {
        if (UpdateTransaction.ReadJournal(context).Phase != UpdatePhase.RolledBack
            || !UpdateFiles.EqualHash(UpdateFiles.Hash(context.TargetPath), context.Data.Parent.Sha256))
            throw new IOException("The restored launcher no longer matches its original application identity.");
        CheckFileVersion(context.TargetPath, ReleaseInfo.NumericVersion);
        using var process = Process.Start(CreatePreviousStart(context)) ?? throw new IOException("The restored application could not start.");
    }
    internal static ProcessStartInfo CreatePreviousStart(VerifiedUpdate context)
    {
        if (context.Data.SettingsPath is not { Length: > 0 } settings)
            throw new IOException("The prior workspace path is unavailable. Reopen the original launcher manually.");
        var start = Start(context.TargetPath, context.TargetDirectory);
        start.ArgumentList.Add("--update-reverted"); start.ArgumentList.Add(context.TransactionPath);
        start.ArgumentList.Add("--settings"); start.ArgumentList.Add(settings);
        start.ArgumentList.Add("--standalone-context");
        return start;
    }
    public bool WaitForCandidateAudioExit(VerifiedUpdate context, string? expectedEngineSha256, TimeSpan timeout)
    {
        if (expectedEngineSha256 is null) return true; // No candidate was launched before the inert check.
        string recorded = Path.Combine(context.Directory, "candidate-engine.json");
        if (File.Exists(recorded))
        {
            UpdateFiles.RequirePrivate(recorded); var signal = UpdateFiles.ReadJson<UpdateEngineSignal>(recorded, 32768);
            if (signal.Token != context.Data.Token || !UpdateFiles.EqualHash(signal.EnvelopeSha256, context.Data.EnvelopeSha256)
                || signal.Identity is null || !UpdateFiles.EqualHash(signal.Identity.Sha256, expectedEngineSha256))
                throw new IOException("The candidate engine recovery identity is invalid.");
            if (!WaitForExit(signal.Identity, timeout)) return false;
        }
        var watch = Stopwatch.StartNew(); using var current = Process.GetCurrentProcess();
        while (watch.Elapsed < timeout)
        {
            bool found = false;
            foreach (string name in new[] { "VineMix.Engine", "AudioMixer.Engine" })
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != current.SessionId) continue;
                        var identity = UpdateProcessIdentity.Capture(process.Id);
                        if (UpdateFiles.EqualHash(identity.Sha256, expectedEngineSha256)) found = true;
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception) { return false; }
                }
            }
            if (!found) return true;
            Thread.Sleep(100);
        }
        return false;
    }
    private static ProcessStartInfo Start(string path, string directory) => new(path) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
    public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
}
