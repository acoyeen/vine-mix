using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

internal sealed record UpdateProcessIdentity(int ProcessId, long StartTimeUtcTicks, string ImagePath, string Sha256)
{
    internal static UpdateProcessIdentity Capture(int processId)
    {
        using var process = Process.GetProcessById(processId);
        string path = TryReadImagePath(process) ?? throw new InvalidOperationException("The process exited before its identity could be captured.");
        return new(processId, process.StartTime.ToUniversalTime().Ticks, path, UpdateFiles.Hash(path));
    }

    // Keep the handle returned by Process.Start: a fast child can exit before
    // module enumeration is possible, and reopening its PID can name a reuse.
    // An exited child needs no recovery wait; its exit code/results still decide
    // whether Setup or the inert check succeeded.
    internal static UpdateProcessIdentity? TryCaptureStarted(Process process, string expectedImagePath)
    {
        string expected = Path.GetFullPath(expectedImagePath);
        UpdateFiles.NoReparse(expected);
        if (process.StartInfo.UseShellExecute || !Path.IsPathFullyQualified(process.StartInfo.FileName)
            || !UpdateFiles.SamePath(process.StartInfo.FileName, expected))
            throw new IOException("Update process identity requires the directly started verified executable.");
        string? actual = TryReadImagePath(process);
        if (actual is null) return null;
        if (!UpdateFiles.SamePath(actual, expected))
            throw new IOException("The started update process does not use the verified executable.");
        return new(process.Id, process.StartTime.ToUniversalTime().Ticks, actual, UpdateFiles.Hash(actual));
    }

    internal static string? TryReadImagePath(Process process)
    {
        var handle = process.SafeHandle;
        if (process.HasExited) return null;
        var buffer = new StringBuilder(32768);
        int length = buffer.Capacity;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref length))
        {
            int error = Marshal.GetLastWin32Error();
            if (process.HasExited) return null;
            throw new Win32Exception(error, "The update process image cannot be identified.");
        }
        return Path.GetFullPath(buffer.ToString());
    }

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
}

internal enum UpdatePhase
{
    Prepared, Ready, Committed, WaitingForExit, BackedUp, Applying, CandidateInstalled,
    AwaitingHealth, HealthCommitted, Completed, RollingBack, RolledBack, Cancelled, RecoveryRequired
}

internal sealed record UpdateTransactionData
{
    public int FormatVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string CurrentVersion { get; init; }
    public required string Token { get; init; }
    public required string EnvelopeSha256 { get; init; }
    public required string AssetFileName { get; init; }
    public required bool Standalone { get; init; }
    public required UpdateProcessIdentity Parent { get; init; }
    public UpdateProcessIdentity? Engine { get; init; }
    public string? SettingsPath { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

internal sealed record UpdateFileEntry(string Path, long Size, string Sha256);
internal sealed record UpdateRegistryValue(string Name, int Kind, string[] Strings, byte[] Bytes, long Number);
internal sealed record UpdateRegistrySnapshot(string Key, string TargetDirectory, UpdateRegistryValue[] Values);
internal sealed record UpdateBackup(UpdateFileEntry[] Files, UpdateRegistrySnapshot? Registry);
internal sealed record UpdateJournal
{
    public UpdatePhase Phase { get; init; } = UpdatePhase.Prepared;
    public UpdatePhase? ResumePhase { get; init; }
    public int Attempts { get; init; }
    public string Message { get; init; } = "";
    public UpdateBackup? Backup { get; init; }
    public UpdateBackup? Candidate { get; init; }
    public UpdateProcessIdentity? SetupProcess { get; init; }
    public UpdateProcessIdentity? SmokeProcess { get; init; }
    public UpdateProcessIdentity? CandidateProcess { get; init; }
    public string? CandidateEngineSha256 { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}
internal sealed record UpdateSignal(string Token, string EnvelopeSha256, int ProcessId = 0,
    long StartTimeUtcTicks = 0, bool Success = false, string Message = "");
internal sealed record PendingUpdate(string TransactionPath, string Version, UpdatePhase Phase, string Message, bool CanResume);
internal sealed record UpdateWorkspaceCheckpoint(string Token, string EnvelopeSha256, string Path, long Size, string Sha256);
internal sealed record UpdateEngineSignal(string Token, string EnvelopeSha256, UpdateProcessIdentity Identity);
internal sealed record UpdateHealthContext(string? SettingsPath, string? SettingsSha256, string Version,
    int ProtocolVersion, int SettingsSchema, UpdatePhase Phase);
internal sealed record VerifiedUpdate(string Directory, UpdateTransactionData Data, UpdateOffer Offer)
{
    internal string TransactionPath => Path.Combine(Directory, "transaction.json");
    internal string WorkerPath => Path.Combine(Directory, "worker.exe");
    internal string AssetPath => Path.Combine(Directory, Data.AssetFileName);
    internal string TargetPath => Data.Parent.ImagePath;
    internal string TargetDirectory => Path.GetDirectoryName(TargetPath)!;
    internal string JournalPath => Path.Combine(Directory, "journal.json");
    internal UpdateSignal Signal(bool success = false, string message = "") => new(Data.Token, Data.EnvelopeSha256,
        Success: success, Message: message);
}

/// <summary>Private durable handoff. It never stops an engine or changes a user's workspace.</summary>
internal static class UpdateTransaction
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VineMix", "updates");
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16
    };

    internal static Task<PreparedUpdate> PrepareAsync(DownloadedUpdate download, bool standalone,
        UpdateProcessIdentity? engine, CancellationToken cancellationToken = default, string? settingsPath = null) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var offer = UpdateTrust.Production.Verify(download.Offer.EnvelopeBytes, download.Offer.Version, new());
        if (SemanticVersion.Parse(offer.Version).CompareTo(SemanticVersion.Parse(ReleaseInfo.Version)) <= 0)
            throw new IOException("The selected release is not newer than this application.");
        var expected = standalone ? offer.Manifest.Standalone : offer.Manifest.Installer;
        if (download.Asset != expected) throw new IOException("The downloaded asset is not the selected distribution.");
        UpdateFiles.VerifyAsset(download.FilePath, expected);
        var parent = UpdateProcessIdentity.Capture(Environment.ProcessId);
        UpdateFiles.NoReparse(parent.ImagePath);
        if (!standalone) WindowsUpdatePlatform.ReadOwnedRegistration(Path.GetDirectoryName(parent.ImagePath)!);
        UpdateFiles.CreatePrivateDirectory(Root);
        string id = Guid.NewGuid().ToString("N"), directory = Path.Combine(Root, ".preparing-" + id);
        UpdateFiles.CreatePrivateDirectory(directory);
        var data = new UpdateTransactionData
        {
            Id = id, Version = offer.Version, CurrentVersion = ReleaseInfo.Version, Standalone = standalone,
            Parent = parent, Engine = engine, CreatedUtc = DateTimeOffset.UtcNow,
            SettingsPath = settingsPath is null ? null : Path.GetFullPath(settingsPath),
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            EnvelopeSha256 = offer.EnvelopeSha256, AssetFileName = expected.FileName
        };
        UpdateFiles.CopyPrivateChecked(download.FilePath, Path.Combine(directory, expected.FileName), expected.Size, expected.Sha256);
        UpdateFiles.CopyPrivateChecked(parent.ImagePath, Path.Combine(directory, "worker.exe"), new FileInfo(parent.ImagePath).Length, parent.Sha256);
        UpdateFiles.WriteBytes(Path.Combine(directory, "envelope.json"), offer.EnvelopeBytes);
        UpdateFiles.WriteJson(Path.Combine(directory, "transaction.json"), data);
        UpdateFiles.WriteJson(Path.Combine(directory, "journal.json"), new UpdateJournal { UpdatedUtc = DateTimeOffset.UtcNow });
        cancellationToken.ThrowIfCancellationRequested();
        string complete = Path.Combine(Root, id);
        Directory.Move(directory, complete);
        return new PreparedUpdate(new(complete, data, offer));
    }, cancellationToken);

    internal static VerifiedUpdate Verify(string transactionPath, UpdateTrust? trust = null, string? rootOverride = null)
    {
        string root = Path.GetFullPath(rootOverride ?? Root), path = Path.GetFullPath(transactionPath);
        string directory = Path.GetDirectoryName(path) ?? throw new IOException("The update transaction path is invalid.");
        if (Path.GetFileName(path) != "transaction.json" || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _)
            || !UpdateFiles.SamePath(Path.GetDirectoryName(directory)!, root)) throw new IOException("The update transaction is outside its private staging directory.");
        UpdateFiles.RequirePrivate(root); UpdateFiles.RequirePrivate(directory); UpdateFiles.RequirePrivate(path);
        var data = UpdateFiles.ReadJson<UpdateTransactionData>(path, 32 * 1024);
        if (data.FormatVersion != 1 || data.Id != Path.GetFileName(directory) || data.Token is not { Length: 64 }
            || !data.Token.All(Uri.IsHexDigit) || !ValidIdentity(data.Parent)
            || data.Engine is not null && !ValidIdentity(data.Engine)
            || !SemanticVersion.TryParse(data.CurrentVersion, out _) || !SemanticVersion.TryParse(data.Version, out _)
            || !UpdateFiles.EqualHash(data.EnvelopeSha256, data.EnvelopeSha256)
            || data.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(5)) throw new IOException("The update handoff is malformed.");
        if (data.SettingsPath is not null && (data.SettingsPath.Length is 0 or > 1024 || !Path.IsPathFullyQualified(data.SettingsPath)
            || !UpdateFiles.SamePath(data.SettingsPath, Path.GetFullPath(data.SettingsPath)))) throw new IOException("The prepared workspace path is invalid.");
        string envelope = Path.Combine(directory, "envelope.json"); UpdateFiles.RequirePrivate(envelope);
        byte[] bytes = UpdateFiles.ReadBytes(envelope, UpdateTrust.MaximumEnvelopeBytes);
        var offer = (trust ?? UpdateTrust.Production).Verify(bytes, data.Version, new());
        if (!UpdateFiles.EqualHash(data.EnvelopeSha256, offer.EnvelopeSha256)
            || SemanticVersion.Parse(offer.Version).CompareTo(SemanticVersion.Parse(data.CurrentVersion)) <= 0)
            throw new IOException("The update handoff does not match its signed release.");
        var asset = data.Standalone ? offer.Manifest.Standalone : offer.Manifest.Installer;
        if (data.AssetFileName != asset.FileName) throw new IOException("The update distribution does not match its signed asset.");
        string target = Path.GetFullPath(data.Parent.ImagePath);
        if (target != data.Parent.ImagePath || !Path.IsPathFullyQualified(target) || target.StartsWith(@"\\", StringComparison.Ordinal)
            || !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) || UpdateFiles.IsInside(target, root)) throw new IOException("The original application path is invalid.");
        UpdateFiles.NoReparse(target);
        var context = new VerifiedUpdate(directory, data, offer);
        UpdateFiles.RequirePrivate(context.WorkerPath); UpdateFiles.RequirePrivate(context.AssetPath);
        if (!UpdateFiles.EqualHash(UpdateFiles.Hash(context.WorkerPath), data.Parent.Sha256))
            throw new IOException("The staged update worker no longer matches the original application.");
        UpdateFiles.VerifyAsset(context.AssetPath, asset);
        return context;
    }

    internal static IReadOnlyList<PendingUpdate> FindPending(UpdateTrust? trust = null, string? rootOverride = null,
        Func<UpdateProcessIdentity, bool>? isLive = null)
    {
        var pending = new List<PendingUpdate>();
        string root = rootOverride ?? Root;
        if (!Directory.Exists(root)) return pending;
        UpdateFiles.RequirePrivate(root);
        var directories = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            if (directories.Count >= 4096) throw new IOException("The update history contains more than 4096 transactions. Retain unfinished recovery folders and ask the maintainer to archive completed update history before continuing.");
            directories.Add(directory);
        }
        foreach (string directory in directories)
        {
            try
            {
                // Terminal history is inert. Check its protected bounded
                // journal before touching potentially large retained payloads.
                // Unfinished entries still receive full signature/hash checks.
                UpdateFiles.RequirePrivate(directory);
                string historyPath = Path.Combine(directory, "journal.json"); UpdateFiles.RequirePrivate(historyPath);
                var history = UpdateFiles.ReadJson<UpdateJournal>(historyPath, 8 * 1024 * 1024);
                if (Enum.IsDefined(history.Phase) && Terminal(history.Phase)) continue;
                var context = Verify(Path.Combine(directory, "transaction.json"), trust, root);
                var journal = ReadJournal(context);
                if (Terminal(journal.Phase)) continue;
                bool committed = UpdateFiles.TrySignal(context, "commit.json", out _);
                bool precommitPhase = journal.Phase is UpdatePhase.Prepared or UpdatePhase.Ready;
                if (precommitPhase && !committed && (isLive ?? new WindowsUpdatePlatform().IsSameLiveProcess)(context.Data.Parent)) continue;
                var phase = precommitPhase && committed ? UpdatePhase.Committed : journal.Phase;
                pending.Add(new(context.TransactionPath, context.Data.Version, phase, journal.Message,
                    (committed || !precommitPhase) && journal.Attempts < 3
                        && (journal.Phase != UpdatePhase.RecoveryRequired || journal.ResumePhase is not null)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or UpdateValidationException
                or ArgumentException or InvalidOperationException or System.Security.SecurityException)
            { pending.Add(new(Path.Combine(directory, "transaction.json"), "unknown", UpdatePhase.RecoveryRequired, "Update recovery information could not be verified. Keep this folder for manual repair.", false)); }
        }
        return pending;
    }

    internal static void Resume(PendingUpdate pending)
    {
        var context = Verify(pending.TransactionPath);
        var journal = ReadJournal(context);
        if (Terminal(journal.Phase) || journal.Attempts >= 3
            || journal.Phase is UpdatePhase.Prepared or UpdatePhase.Ready && !UpdateFiles.TrySignal(context, "commit.json", out _))
            throw new IOException("This transaction needs manual repair or was never committed.");
        StartWorker(context);
    }
    internal static void CancelUncommitted(PendingUpdate pending)
    {
        var context = Verify(pending.TransactionPath); var platform = new WindowsUpdatePlatform();
        using var ownership = platform.AcquireLock();
        var journal = ReadJournal(context);
        if (journal.Phase is not (UpdatePhase.Prepared or UpdatePhase.Ready) || File.Exists(Path.Combine(context.Directory, "commit.json"))
            || platform.IsSameLiveProcess(context.Data.Parent)) throw new IOException("The update is active, committed, or still owned by its original application.");
        SaveJournal(context, journal with { Phase = UpdatePhase.Cancelled, Message = "An abandoned update handoff was cancelled before any application or settings changes." });
        platform.ClearRecovery(context);
    }

    // Candidate calls this only after strict workspace load, compatible engine
    // handshake and observed advancing audio frames. Inert smoke is insufficient.
    internal static void ReportHealth(string transactionPath, bool success, string message)
    {
        var context = Verify(transactionPath);
        var process = UpdateProcessIdentity.Capture(Environment.ProcessId);
        var journal = ReadJournal(context);
        if (!UpdateFiles.SamePath(process.ImagePath, context.TargetPath) || journal.Phase != UpdatePhase.AwaitingHealth
            || journal.CandidateProcess is null || !WindowsUpdatePlatform.SameProcess(process, journal.CandidateProcess)
            || ReleaseInfo.Version != context.Data.Version || ReleaseInfo.NumericVersion != context.Offer.Manifest.NumericVersion)
            throw new IOException("This application is not the update candidate awaiting health confirmation.");
        if (success) VerifyWorkspace(context);
        UpdateFiles.WriteJson(Path.Combine(context.Directory, "health.json"), new UpdateSignal(context.Data.Token,
            context.Data.EnvelopeSha256, process.ProcessId, process.StartTimeUtcTicks, success, message.Length > 512 ? message[..512] : message));
    }

    internal static UpdateHealthContext GetHealthContext(string transactionPath)
    {
        var context = Verify(transactionPath); var checkpoint = ReadWorkspace(context);
        return new(checkpoint?.Path, checkpoint?.Sha256, context.Data.Version, context.Offer.Manifest.ProtocolVersion,
            context.Offer.Manifest.SettingsSchema, ReadJournal(context).Phase);
    }
    internal static async Task<UpdateHealthContext> WaitForCandidateRegistrationAsync(string transactionPath, CancellationToken cancellationToken = default)
    {
        var context = Verify(transactionPath); var current = UpdateProcessIdentity.Capture(Environment.ProcessId); var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            cancellationToken.ThrowIfCancellationRequested(); var journal = ReadJournal(context);
            if (journal.Phase == UpdatePhase.AwaitingHealth && journal.CandidateProcess is { } candidate && WindowsUpdatePlatform.SameProcess(current, candidate))
            {
                var checkpoint = ReadWorkspace(context);
                return new(checkpoint?.Path, checkpoint?.Sha256, context.Data.Version, context.Offer.Manifest.ProtocolVersion, context.Offer.Manifest.SettingsSchema, journal.Phase);
            }
            if (journal.Phase is UpdatePhase.RecoveryRequired or UpdatePhase.RollingBack or UpdatePhase.RolledBack) break;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        throw new IOException("The update worker did not register this candidate process for health confirmation.");
    }
    internal static void RecordCandidateEngine(string transactionPath, UpdateProcessIdentity identity)
    {
        var context = Verify(transactionPath); var journal = ReadJournal(context);
        var process = UpdateProcessIdentity.Capture(Environment.ProcessId);
        if (!ValidIdentity(identity) || journal.Phase != UpdatePhase.AwaitingHealth || journal.CandidateProcess is null
            || !WindowsUpdatePlatform.SameProcess(process, journal.CandidateProcess)
            || !UpdateFiles.EqualHash(identity.Sha256, journal.CandidateEngineSha256))
            throw new IOException("The candidate engine does not match this verified update transaction.");
        UpdateFiles.WriteJson(Path.Combine(context.Directory, "candidate-engine.json"), new UpdateEngineSignal(context.Data.Token, context.Data.EnvelopeSha256, identity));
    }
    internal static async Task<bool> WaitForHealthCommitAsync(string transactionPath, CancellationToken cancellationToken = default)
    {
        var context = Verify(transactionPath); var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(130))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var phase = ReadJournal(context).Phase;
            if (phase is UpdatePhase.HealthCommitted or UpdatePhase.Completed) return true;
            if (phase is UpdatePhase.RollingBack or UpdatePhase.RolledBack or UpdatePhase.RecoveryRequired or UpdatePhase.Cancelled) return false;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }
    internal static UpdateWorkspaceCheckpoint? ReadWorkspace(VerifiedUpdate context)
    {
        if (context.Data.SettingsPath is null) return null;
        string path = Path.Combine(context.Directory, "workspace.json"); UpdateFiles.RequirePrivate(path);
        var checkpoint = UpdateFiles.ReadJson<UpdateWorkspaceCheckpoint>(path, 32768);
        if (checkpoint.Token != context.Data.Token || !UpdateFiles.EqualHash(checkpoint.EnvelopeSha256, context.Data.EnvelopeSha256)
            || !UpdateFiles.SamePath(checkpoint.Path, context.Data.SettingsPath) || checkpoint.Size is <= 0 or > 32 * 1024 * 1024
            || !UpdateFiles.EqualHash(checkpoint.Sha256, UpdateFiles.Hash(Path.Combine(context.Directory, "workspace-before.json"))))
            throw new IOException("The saved-workspace update checkpoint is invalid.");
        return checkpoint;
    }
    internal static void VerifyWorkspace(VerifiedUpdate context)
    {
        var checkpoint = ReadWorkspace(context);
        if (checkpoint is not null && (new FileInfo(checkpoint.Path).Length != checkpoint.Size
            || !UpdateFiles.EqualHash(UpdateFiles.Hash(checkpoint.Path), checkpoint.Sha256)))
            throw new IOException("The saved workspace changed after the update checkpoint. The newer settings were retained and will not be restored from a backup.");
    }

    internal static UpdateJournal ReadJournal(VerifiedUpdate context)
    {
        UpdateFiles.RequirePrivate(context.JournalPath);
        var journal = UpdateFiles.ReadJson<UpdateJournal>(context.JournalPath, 8 * 1024 * 1024);
        if (!Enum.IsDefined(journal.Phase) || journal.ResumePhase is { } resume && !Enum.IsDefined(resume)
            || journal.Attempts is < 0 or > 3 || journal.Message is null || journal.Message.Length > 32768
            || journal.SetupProcess is not null && !ValidIdentity(journal.SetupProcess)
            || journal.SmokeProcess is not null && !ValidIdentity(journal.SmokeProcess)
            || journal.CandidateProcess is not null && !ValidIdentity(journal.CandidateProcess)
            || journal.CandidateEngineSha256 is not null && !UpdateFiles.EqualHash(journal.CandidateEngineSha256, journal.CandidateEngineSha256))
            throw new IOException("The update journal is malformed.");
        ValidateBackup(journal.Backup); ValidateBackup(journal.Candidate);
        if (journal.Phase is UpdatePhase.BackedUp or UpdatePhase.Applying or UpdatePhase.CandidateInstalled or UpdatePhase.AwaitingHealth
            or UpdatePhase.HealthCommitted or UpdatePhase.RollingBack && journal.Backup is null)
            throw new IOException("The update phase has no retained payload backup.");
        if (journal.Phase is UpdatePhase.CandidateInstalled or UpdatePhase.AwaitingHealth or UpdatePhase.HealthCommitted or UpdatePhase.RollingBack && journal.Candidate is null)
            throw new IOException("The update phase has no recorded candidate payload.");
        if (journal.Phase is UpdatePhase.AwaitingHealth or UpdatePhase.HealthCommitted && journal.CandidateEngineSha256 is null)
            throw new IOException("The update health checkpoint has no verified candidate engine digest.");
        return journal;
    }
    private static bool ValidIdentity(UpdateProcessIdentity? identity) => identity is not null
        && identity.ProcessId > 0 && identity.StartTimeUtcTicks > 0 && identity.StartTimeUtcTicks <= DateTime.MaxValue.Ticks
        && identity.ImagePath is { Length: > 0 and <= 1024 } && Path.IsPathFullyQualified(identity.ImagePath)
        && UpdateFiles.EqualHash(identity.Sha256, identity.Sha256);
    private static void ValidateBackup(UpdateBackup? backup)
    {
        if (backup is null) return;
        if (backup.Files is null || backup.Files.Length is 0 or > 4096
            || backup.Files.Any(file => file is null || file.Path is not { Length: > 0 and <= 240 }
                || file.Size is < 0 or > UpdateTrust.MaximumAssetBytes || !UpdateFiles.EqualHash(file.Sha256, file.Sha256))
            || backup.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != backup.Files.Length)
            throw new IOException("The update backup inventory is malformed.");
        if (backup.Registry is { } registry && (registry.Key is null || registry.TargetDirectory is null || registry.Values is null
            || registry.Values.Length > 128 || registry.Values.Any(value => value is null || value.Name is null || value.Strings is null || value.Bytes is null)))
            throw new IOException("The saved uninstall registration is malformed.");
    }
    internal static void SaveJournal(VerifiedUpdate context, UpdateJournal journal)
        => UpdateFiles.WriteJson(context.JournalPath, journal with { UpdatedUtc = DateTimeOffset.UtcNow });
    internal static bool Terminal(UpdatePhase phase) => phase is UpdatePhase.Completed or UpdatePhase.RolledBack or UpdatePhase.Cancelled;
    internal static void StartWorker(VerifiedUpdate context)
    {
        var start = new ProcessStartInfo(context.WorkerPath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = context.Directory };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(context.TransactionPath);
        using var process = Process.Start(start) ?? throw new IOException("The private update worker could not start.");
    }
}

internal sealed class PreparedUpdate(VerifiedUpdate context) : IDisposable
{
    private bool _committed;
    internal string TransactionPath => context.TransactionPath;
    internal string WorkerPath => context.WorkerPath;
    internal async Task StartWorkerAndWaitReadyAsync(CancellationToken cancellationToken = default)
    {
        UpdateTransaction.StartWorker(context);
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UpdateFiles.TrySignal(context, "ready.json", out _)) return;
            var journal = UpdateTransaction.ReadJournal(context);
            if (journal.Phase is UpdatePhase.RecoveryRequired or UpdatePhase.Cancelled) throw new IOException(journal.Message);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        throw new IOException("The update worker did not become ready. The application has not been closed.");
    }
    internal void Commit()
    {
        if (!UpdateFiles.TrySignal(context, "ready.json", out _) || UpdateTransaction.ReadJournal(context).Phase != UpdatePhase.Ready)
            throw new IOException("The update worker is not ready.");
        if (context.Data.SettingsPath is { } settings)
        {
            UpdateFiles.NoReparse(settings);
            var checkpoint = new UpdateWorkspaceCheckpoint(context.Data.Token, context.Data.EnvelopeSha256, settings,
                new FileInfo(settings).Length, UpdateFiles.Hash(settings));
            if (checkpoint.Size is <= 0 or > 32 * 1024 * 1024) throw new IOException("The saved workspace exceeds the update checkpoint size limit.");
            UpdateFiles.CopyPrivateChecked(settings, Path.Combine(context.Directory, "workspace-before.json"), checkpoint.Size, checkpoint.Sha256);
            UpdateFiles.WriteJson(Path.Combine(context.Directory, "workspace.json"), checkpoint);
        }
        UpdateFiles.WriteJson(Path.Combine(context.Directory, "commit.json"), context.Signal());
        _committed = true;
    }
    public void Dispose()
    {
        if (_committed) return;
        try { UpdateFiles.WriteJson(Path.Combine(context.Directory, "abort.json"), context.Signal()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Debug.WriteLine(error); }
    }
}

internal static class UpdateFiles
{
    internal static bool SamePath(string left, string right) => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar));
    internal static bool IsInside(string path, string directory) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static bool EqualHash(string? left, string? right) => left is { Length: 64 } && right is { Length: 64 } && left.All(Uri.IsHexDigit) && right.All(Uri.IsHexDigit) && StringComparer.OrdinalIgnoreCase.Equals(left, right);
    internal static string Hash(string path)
    {
        NoReparse(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    internal static void VerifyAsset(string path, UpdateAsset asset)
    {
        NoReparse(path);
        if (new FileInfo(path).Length != asset.Size || !EqualHash(Hash(path), asset.Sha256))
            throw new IOException("The update payload size or SHA-256 no longer matches its signed release.");
    }
    internal static void CopyChecked(string source, string destination, long size, string hash)
        => CopyChecked(source, destination, size, hash, privateDestination: false);
    internal static void CopyPrivateChecked(string source, string destination, long size, string hash)
        => CopyChecked(source, destination, size, hash, privateDestination: true);
    private static void CopyChecked(string source, string destination, long size, string hash, bool privateDestination)
    {
        NoReparse(source); NoReparse(destination);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = privateDestination ? CreatePrivateFile(destination)
            : new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (input.Length != size) throw new IOException("An update file changed size before copying.");
            input.CopyTo(output); output.Flush(true);
        }
        if (new FileInfo(destination).Length != size || !EqualHash(Hash(destination), hash)) throw new IOException("An update file changed while copying.");
    }
    private static FileStream CreatePrivateFile(string path)
    {
        // An elevated token can default new-file ownership to Administrators,
        // even inside a directory owned solely by the current user. Set owner
        // and access at creation; do not relax verification or repair old ACLs.
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new IOException("The Windows user cannot be identified.");
        var acl = new FileSecurity(); acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, acl);
    }
    internal static void CreatePrivateDirectory(string path)
    {
        NoReparse(path);
        var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException("The Windows user cannot be identified.");
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var acl = new DirectorySecurity(); acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).Create(acl);
        }
        RequirePrivate(path);
    }
    internal static void RequirePrivate(string path)
    {
        NoReparse(path);
        var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException("The Windows user cannot be identified.");
        FileSystemSecurity acl = Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        if (!sid.Equals(acl.GetOwner(typeof(SecurityIdentifier)))) throw new IOException("The update staging owner is not the current user.");
        bool allowed = false;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (!sid.Equals(rule.IdentityReference)) throw new IOException("The update staging directory grants access to another account.");
            if ((rule.FileSystemRights & FileSystemRights.Write) != 0) allowed = true;
        }
        if (!allowed) throw new IOException("The update staging directory is not writable by its owner.");
    }
    internal static void NoReparse(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Updates cannot use links, junctions or reparse-point paths.");
            if (Path.GetPathRoot(current) == current) break;
        }
    }
    internal static string RelativeFile(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Length > 240 || relative.Contains('\\') || relative.Contains(':')
            || relative.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
            throw new IOException("The update payload inventory has an unsafe relative path.");
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(full, root)) throw new IOException("The update inventory escapes its installation folder.");
        string name = Path.GetFileName(full);
        if (name.StartsWith("settings.json", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ui.json", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".log", StringComparison.OrdinalIgnoreCase) || relative.Split('/').Any(part => part.Equals("updates", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("User settings and update data cannot be part of an application payload inventory.");
        NoReparse(full); return full;
    }
    internal static byte[] ReadBytes(string path, int maximum)
    {
        NoReparse(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length <= 0 || input.Length > maximum) throw new IOException("An update metadata file exceeds its size limit.");
        byte[] data = new byte[checked((int)input.Length)]; input.ReadExactly(data); return data;
    }
    internal static T ReadJson<T>(string path, int maximum) => JsonSerializer.Deserialize<T>(ReadBytes(path, maximum), UpdateTransaction.Json) ?? throw new IOException("Update metadata is missing.");
    internal static void WriteJson<T>(string path, T value) => WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, UpdateTransaction.Json));
    internal static void WriteBytes(string path, byte[] bytes)
    {
        NoReparse(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // This writer is for private transaction metadata only. Installed
        // payload replacement uses CopyChecked and retains its normal ACLs.
        using (var output = CreatePrivateFile(temporary)) { output.Write(bytes); output.Flush(true); }
        if (File.Exists(path)) File.Replace(temporary, path, null, false); else File.Move(temporary, path);
    }
    internal static bool TrySignal(VerifiedUpdate context, string name, out UpdateSignal? signal)
    {
        string path = Path.Combine(context.Directory, name); signal = null;
        if (!File.Exists(path)) return false;
        RequirePrivate(path); var candidate = ReadJson<UpdateSignal>(path, 8192);
        if (candidate.Token != context.Data.Token || !EqualHash(candidate.EnvelopeSha256, context.Data.EnvelopeSha256))
            throw new IOException("An update handoff marker does not match its transaction.");
        signal = candidate; return true;
    }
}
