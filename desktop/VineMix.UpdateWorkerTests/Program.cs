using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VineMix.Desktop;
using VineMix.UpdateCore;

namespace VineMix.Desktop
{
    internal static class ReleaseInfo
    {
        internal const string Version = "1.0.0";
        internal const string NumericVersion = "1.0.0.0";
    }
}

internal static class Program
{
    private static int _checks;
    private static void Require(bool value, string message)
    { ++_checks; if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception error) when (error is IOException or UpdateValidationException or JsonException) { rejected = true; }
        Require(rejected, message);
    }
    private static int Main()
    {
        try
        {
            SignedBoundaryTests(); SuccessfulStandalone(); RollbackTests(); RecoveryTests(); WorkspaceTests(); ManifestTests(); InstalledAttemptTests(); PendingDiscoveryTests();
            Console.WriteLine($"PASS: {_checks} updater worker checks; ephemeral signed fixtures, private temporary directories and fake process/registry/Setup operations only. No production app, audio endpoint, settings, driver or registry changes.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); return 1; }
    }

    private static void SignedBoundaryTests()
    {
        using var fixture = new Fixture(); var platform = new FakePlatform(fixture);
        byte[] original = File.ReadAllBytes(fixture.Context.AssetPath);
        File.AppendAllText(fixture.Context.AssetPath, "changed");
        Require(fixture.Run(platform) == 1 && platform.Events.Count == 0 && fixture.OldTarget,
            "Tampered signed asset must be rejected before mutex, IPC, process or target actions.");
        File.WriteAllBytes(fixture.Context.AssetPath, original);
        string transaction = File.ReadAllText(fixture.Context.TransactionPath);
        foreach (string property in new[] { "token", "parent", "currentVersion", "envelopeSha256", "version" })
        {
            var malformed = JsonNode.Parse(transaction)!.AsObject(); malformed[property] = null;
            File.WriteAllText(fixture.Context.TransactionPath, malformed.ToJsonString());
            Reject(() => UpdateTransaction.Verify(fixture.Context.TransactionPath, fixture.Trust, fixture.Root), "Explicit null metadata must fail with a readable validation error: " + property);
            Require(UpdateTransaction.FindPending(fixture.Trust, fixture.Root).Single().CanResume == false,
                "Pending discovery must contain malformed metadata without crashing application startup.");
        }
        File.WriteAllText(fixture.Context.TransactionPath, transaction);
        Reject(() => UpdateTransaction.Verify(fixture.Context.TransactionPath, fixture.Trust, fixture.Install), "Transaction path must be a direct GUID child of the private update root.");
        File.AppendAllText(fixture.Context.WorkerPath, "different worker");
        Reject(() => UpdateTransaction.Verify(fixture.Context.TransactionPath, fixture.Trust, fixture.Root), "Staged helper must match the original parent hash.");
        File.WriteAllText(fixture.Context.WorkerPath, Fixture.OldBytes);
        var journal = JsonNode.Parse(File.ReadAllText(fixture.Context.JournalPath))!.AsObject();
        journal["backup"] = JsonNode.Parse("{\"files\":null,\"registry\":null}");
        File.WriteAllText(fixture.Context.JournalPath, journal.ToJsonString());
        Reject(() => UpdateTransaction.ReadJournal(fixture.Context), "Null backup arrays must not crash journal recovery.");
        UpdateFiles.WriteJson(fixture.Context.JournalPath, new UpdateJournal());
        var acl = new DirectoryInfo(fixture.Context.Directory).GetAccessControl();
        var broad = new DirectorySecurity(); broad.SetSecurityDescriptorBinaryForm(acl.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
        broad.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new DirectoryInfo(fixture.Context.Directory).SetAccessControl(broad);
        Reject(() => UpdateTransaction.Verify(fixture.Context.TransactionPath, fixture.Trust, fixture.Root), "Update transactions with access granted to another account must be rejected.");
        new DirectoryInfo(fixture.Context.Directory).SetAccessControl(acl);
    }
    private static void SuccessfulStandalone()
    {
        using var fixture = new Fixture(); var platform = new FakePlatform(fixture);
        Require(fixture.Run(platform) == 0 && File.ReadAllText(fixture.Context.TargetPath) == Fixture.NewBytes,
            "Verified standalone replacement must install the exact signed bytes.");
        Require(UpdateTransaction.ReadJournal(fixture.Context).Phase == UpdatePhase.Completed,
            "A successful update needs full candidate health, beyond inert smoke.");
        Require(File.ReadAllText(fixture.Context.TargetPath + ".vinemix-" + fixture.Context.Data.Id + ".old") == Fixture.OldBytes,
            "File.Replace must retain the original launcher beside its original path.");
        Require(platform.Events.IndexOf("parent-exit") < platform.Events.IndexOf("smoke")
            && platform.Events.IndexOf("engine-exit") < platform.Events.IndexOf("candidate-start")
            && platform.Events[^1] == "lock-released", "Process exits must precede replacement/launch and lock must span completion.");
        Require(platform.HealthWasRequired, "The candidate must provide its identity-bound health acknowledgment.");
    }
    private static void RollbackTests()
    {
        using (var fixture = new Fixture())
        {
            var platform = new FakePlatform(fixture) { Healthy = false };
            Require(fixture.Run(platform) == 3 && fixture.OldTarget, "Failed candidate health must restore prior bytes after cooperative candidate exit.");
            Require(platform.Events.IndexOf("candidate-exit") < platform.Events.IndexOf("lock-released")
                && platform.Events.Contains("candidate-audio-exit"), "Rollback must hold the mutex and wait for UI plus audio-engine exit.");
            Require(UpdateTransaction.ReadJournal(fixture.Context).Phase == UpdatePhase.RolledBack, "Rollback must be durably recorded.");
            Require(platform.Events.IndexOf("recovery-cleared") < platform.Events.IndexOf("lock-released")
                && platform.Events.IndexOf("lock-released") < platform.Events.IndexOf("previous-reopened"),
                "A restored application must reopen only after durable rollback, recovery cleanup and releasing the update mutex.");
        }
        using (var fixture = new Fixture())
        {
            var platform = new FakePlatform(fixture) { Healthy = false, CandidateEngineExits = false };
            Require(fixture.Run(platform) == 1 && !fixture.OldTarget, "A surviving candidate engine must prevent rollback without being killed.");
            Require(UpdateTransaction.ReadJournal(fixture.Context).ResumePhase == UpdatePhase.AwaitingHealth,
                "A blocked rollback must retain a resumable candidate-health checkpoint.");
            Require(!platform.Events.Contains("previous-reopened"), "A surviving candidate engine must suppress reopening the prior application.");
        }
        using (var fixture = new Fixture())
        {
            var platform = new FakePlatform(fixture) { Healthy = false, ChangeTargetAfterHealth = true };
            Require(fixture.Run(platform) == 1 && File.ReadAllText(fixture.Context.TargetPath) == "newer user payload",
                "Hash-guard rollback must preserve a target changed after the candidate checkpoint.");
            Require(!platform.Events.Contains("previous-reopened"), "A payload conflict must suppress unsafe reopening of the previous version.");
        }
        using (var fixture = new Fixture())
        {
            var platform = new FakePlatform(fixture) { SmokePasses = false };
            Require(fixture.Run(platform) == 3 && fixture.OldTarget && !platform.Events.Contains("candidate-start"),
                "Inert package failure must restore prior payload without launching normal candidate audio.");
        }
        using (var fixture = new Fixture(settings: true))
        {
            var start = WindowsUpdatePlatform.CreatePreviousStart(fixture.Context);
            Require(!start.UseShellExecute && start.ArgumentList.SequenceEqual(new[] { "--update-reverted", fixture.Context.TransactionPath,
                "--settings", fixture.Context.Data.SettingsPath!, "--standalone-context" }),
                "Restored startup must use only fixed rollback notice, physical workspace and normalized-context arguments.");
            var platform = new FakePlatform(fixture) { Healthy = false, ReopenFails = true };
            Require(fixture.Run(platform) == 3 && fixture.OldTarget
                && UpdateTransaction.ReadJournal(fixture.Context) is { Phase: UpdatePhase.RolledBack, Message: var message }
                && message.Contains("manually", StringComparison.Ordinal),
                "A failed reopen must retain successful rollback and record manual reopening guidance.");
        }
    }
    private static void RecoveryTests()
    {
        using (var fixture = new Fixture())
        {
            var platform = new FakePlatform(fixture) { ParentExits = false };
            Require(fixture.Run(platform) == 1 && fixture.OldTarget, "A parent exit timeout must not replace the launcher.");
            Require(UpdateTransaction.ReadJournal(fixture.Context).ResumePhase == UpdatePhase.WaitingForExit,
                "Exit timeout must retain its safe wait checkpoint.");
            platform.ParentExits = true;
            Require(fixture.Run(platform) == 0, "A later retry must resume a safe checkpoint and complete.");
        }
        using (var fixture = new Fixture())
        {
            UpdateFiles.WriteJson(fixture.Context.JournalPath, new UpdateJournal { Phase = UpdatePhase.Applying,
                Attempts = 1, Backup = UpdateWorker.Backup(fixture.Context, new FakePlatform(fixture)) });
            File.WriteAllText(fixture.Context.TargetPath, "unknown interrupted bytes");
            Require(fixture.Run(new FakePlatform(fixture)) == 1 && File.ReadAllText(fixture.Context.TargetPath) == "unknown interrupted bytes",
                "An interrupted apply with unknown postimage must retain backups and require manual repair.");
            Require(UpdateTransaction.ReadJournal(fixture.Context).ResumePhase is null, "Unknown postimage must not advertise automatic resumption.");
        }
        using (var fixture = new Fixture())
        {
            File.Delete(Path.Combine(fixture.Context.Directory, "commit.json"));
            var platform = new FakePlatform(fixture) { ParentAlive = false };
            Require(fixture.Run(platform) == 1 && fixture.OldTarget && !File.Exists(Path.Combine(fixture.Context.Directory, "ready.json")),
                "An uncommitted handoff without its original live parent must never arm or apply.");
        }
    }
    private static void PendingDiscoveryTests()
    {
        using (var fixture = new Fixture())
        {
            Require(UpdateTransaction.FindPending(fixture.Trust, fixture.Root, _ => false).Single() is { Phase: UpdatePhase.Committed, CanResume: true },
                "A durable commit before the Ready-to-Committed journal write must remain resumable after parent exit.");
            Require(fixture.Run(new FakePlatform(fixture) { ParentAlive = false }) == 0,
                "A committed Prepared handoff must resume without requiring its exited original parent to approve again.");
        }
        using (var fixture = new Fixture())
        {
            File.Delete(Path.Combine(fixture.Context.Directory, "commit.json"));
            UpdateFiles.WriteJson(fixture.Context.JournalPath, new UpdateJournal { Phase = UpdatePhase.Ready, Attempts = 1 });
            Require(UpdateTransaction.FindPending(fixture.Trust, fixture.Root, _ => true).Count == 0,
                "A live original UI's uncommitted handoff must not block a second launch from foregrounding that UI.");
            Require(UpdateTransaction.FindPending(fixture.Trust, fixture.Root, _ => false).Single() is { Phase: UpdatePhase.Ready, CanResume: false },
                "An abandoned uncommitted Ready handoff may be cancelled, but cannot be resumed as an approved update.");
        }
        using (var fixture = new Fixture())
        {
            for (int i = 0; i < 4096; ++i) Directory.CreateDirectory(Path.Combine(fixture.Root, Guid.NewGuid().ToString("N")));
            Reject(() => UpdateTransaction.FindPending(fixture.Trust, fixture.Root), "History overflow must fail actionably instead of silently omitting unfinished transactions.");
        }
        using (var fixture = new Fixture())
        {
            UpdateFiles.WriteJson(fixture.Context.JournalPath, new UpdateJournal { Phase = UpdatePhase.Completed });
            File.Delete(fixture.Context.AssetPath); File.Delete(fixture.Context.WorkerPath);
            File.WriteAllText(Path.Combine(fixture.Context.Directory, "envelope.json"), "corrupt retired envelope");
            Require(UpdateTransaction.FindPending(fixture.Trust, fixture.Root).Count == 0,
                "Protected terminal history with removed payloads must not rehash large retired files or block normal startup.");
        }
        using (var fixture = new Fixture())
        {
            File.AppendAllText(fixture.Context.AssetPath, "tampered unfinished payload");
            Require(UpdateTransaction.FindPending(fixture.Trust, fixture.Root).Single() is { Phase: UpdatePhase.RecoveryRequired, CanResume: false },
                "Unfinished history must still reject corrupt signed payloads and prevent automatic recovery.");
        }
        Require(WindowsUpdatePlatform.MutexName.StartsWith(@"Global\VineMix.Update.", StringComparison.Ordinal),
            "The update mutex must serialize the same per-user install across logon sessions.");
    }
    private static void WorkspaceTests()
    {
        using var fixture = new Fixture(settings: true);
        string settings = fixture.Context.Data.SettingsPath!;
        File.WriteAllText(settings, "newer workspace");
        Require(fixture.Run(new FakePlatform(fixture)) == 1 && fixture.OldTarget && File.ReadAllText(settings) == "newer workspace",
            "A workspace changed after commit must remain untouched and block applying the candidate.");
        Reject(() => UpdateFiles.RelativeFile(fixture.Install, "../settings.json"), "Payload inventories must reject traversal.");
        Reject(() => UpdateFiles.RelativeFile(fixture.Install, "settings.json"), "Settings must never be an application rollback member.");
        Reject(() => UpdateFiles.RelativeFile(fixture.Install, "ui.json"), "UI preferences must never be restored from application rollback.");
        Reject(() => UpdateFiles.RelativeFile(fixture.Install, "SetupSupport/../other.exe"), "Nested traversal must fail before file operations.");
    }
    private static void ManifestTests()
    {
        using var fixture = new Fixture(standalone: false);
        var platform = new FakePlatform(fixture);
        var backup = UpdateWorker.Backup(fixture.Context, platform);
        Require(backup.Files.Any(file => file.Path == "SetupSupport/package-manifest.json")
            && backup.Files.Any(file => file.Path == "installation.ini") && backup.Files.Any(file => file.Path == "unins000.dat")
            && backup.Files.All(file => file.Path != "unlisted-user-file.txt"), "Installed backup must use exact inventory plus exact owned bookkeeping and preserve unknown files.");
        File.WriteAllText(Path.Combine(fixture.Install, "unlisted-user-file.txt"), "newer notes");
        var observed = UpdateWorker.ObserveCandidate(fixture.Context, backup, platform, false);
        UpdateWorker.Restore(fixture.Context, backup, observed, platform);
        Require(File.ReadAllText(Path.Combine(fixture.Install, "unlisted-user-file.txt")) == "newer notes", "Rollback must leave unlisted user files untouched.");
        File.Delete(Path.Combine(fixture.Install, "SetupSupport", "package-manifest.json"));
        Reject(() => UpdateWorker.Backup(fixture.Context, platform), "An installed copy without an ownership inventory must require manual repair.");
    }
    private static void InstalledAttemptTests()
    {
        using (var fixture = new Fixture(standalone: false))
        {
            var start = WindowsUpdatePlatform.CreateSetupStart(fixture.Context);
            Require(!start.UseShellExecute && start.ArgumentList.SequenceEqual(new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/ACCEPTTERMS=1", "/DRIVERS=none", "/DIR=" + fixture.Install }),
                "Setup launch must use exactly the fixed silent, no-restart, accepted-terms, no-driver and owned-target arguments.");
            var platform = new FakePlatform(fixture) { SetupExitCode = 1 };
            Require(fixture.Run(platform) == 3 && fixture.OldTarget
                && File.ReadAllText(Path.Combine(fixture.Install, "VineMix.Engine.exe")) == "example old engine",
                "Failed Setup exit must restore the old owned desktop and engine payload from its preimage.");
            Require(File.ReadAllText(Path.Combine(fixture.Install, "unlisted-user-file.txt")) == "personal notes fixture",
                "Setup-failure rollback must retain files outside the exact payload inventory.");
        }
        using (var fixture = new Fixture(standalone: false))
        {
            var platform = new FakePlatform(fixture);
            Require(fixture.Run(platform) == 0 && !fixture.OldTarget,
                "Installed update needs new inventory integrity, desktop/engine versions, inert smoke and candidate health.");
        }
        using (var fixture = new Fixture(standalone: false))
        {
            var platform = new FakePlatform(fixture);
            var original = UpdateWorker.Backup(fixture.Context, platform);
            platform.RunSetup(fixture.Context, _ => { });
            var candidate = UpdateWorker.ObserveCandidate(fixture.Context, original, platform, true);
            UpdateWorker.Restore(fixture.Context, original, candidate, platform);
            UpdateWorker.Restore(fixture.Context, original, candidate, platform);
            Require(fixture.OldTarget, "Rollback must resume idempotently when prior files were already restored before interruption.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal const string OldBytes = "example old single-file launcher";
        internal const string NewBytes = "example signed new single-file launcher";
        internal string Base { get; } = Path.Combine(Path.GetTempPath(), "VineMix-UpdateWorker-" + Guid.NewGuid().ToString("N"));
        internal string Root { get; }
        internal string Install { get; }
        internal VerifiedUpdate Context { get; }
        internal UpdateTrust Trust { get; }
        internal bool OldTarget => File.ReadAllText(Context.TargetPath) == OldBytes;
        internal Fixture(bool standalone = true, bool settings = false)
        {
            Directory.CreateDirectory(Base); Root = Path.Combine(Base, "updates"); Install = Path.Combine(Base, "example-install");
            Directory.CreateDirectory(Install); UpdateFiles.CreatePrivateDirectory(Root);
            string id = Guid.NewGuid().ToString("N"), stage = Path.Combine(Root, id); UpdateFiles.CreatePrivateDirectory(stage);
            string target = Path.Combine(Install, "VineMix.exe"); File.WriteAllText(target, OldBytes);
            string oldHash = UpdateFiles.Hash(target);
            using var rsa = RSA.Create(3072); Trust = new(new Dictionary<string, string> { ["fixture"] = rsa.ExportSubjectPublicKeyInfoPem() });
            string version = "1.1.0";
            UpdateAsset Asset(string kind) => new() { FileName = $"VineMix-{version}-{kind}-x64.exe",
                Url = UpdateTrust.AssetUrl(version, $"VineMix-{version}-{kind}-x64.exe"), Size = Encoding.UTF8.GetByteCount(NewBytes),
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NewBytes))).ToLowerInvariant() };
            var manifest = new UpdateManifest { Version = version, NumericVersion = "1.1.0.0", Installer = Asset("Setup"), Standalone = Asset("Standalone") };
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateTransaction.Json);
            byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, keyId = "fixture", algorithm = UpdateTrust.Algorithm,
                payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) });
            var offer = Trust.Verify(envelope, version);
            var data = new UpdateTransactionData { Id = id, Version = version, CurrentVersion = "1.0.0", Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                EnvelopeSha256 = offer.EnvelopeSha256, AssetFileName = (standalone ? manifest.Standalone : manifest.Installer).FileName,
                Standalone = standalone, CreatedUtc = DateTimeOffset.UtcNow, Parent = new(101, 1001, target, oldHash),
                Engine = new(102, 1002, Path.Combine(Install, "VineMix.Engine.exe"), oldHash), SettingsPath = settings ? Path.Combine(Base, "saved-workspace.json") : null };
            Context = new(stage, data, offer);
            File.Copy(target, Context.WorkerPath); File.WriteAllText(Context.AssetPath, NewBytes);
            UpdateFiles.WriteBytes(Path.Combine(stage, "envelope.json"), envelope); UpdateFiles.WriteJson(Context.TransactionPath, data);
            UpdateFiles.WriteJson(Context.JournalPath, new UpdateJournal());
            UpdateFiles.WriteJson(Path.Combine(stage, "commit.json"), Context.Signal());
            if (settings)
            {
                File.WriteAllText(data.SettingsPath!, "saved workspace");
                string backup = Path.Combine(stage, "workspace-before.json"); File.Copy(data.SettingsPath!, backup);
                UpdateFiles.WriteJson(Path.Combine(stage, "workspace.json"), new UpdateWorkspaceCheckpoint(data.Token, data.EnvelopeSha256,
                    data.SettingsPath!, new FileInfo(backup).Length, UpdateFiles.Hash(backup)));
            }
            if (!standalone)
            {
                Directory.CreateDirectory(Path.Combine(Install, "SetupSupport")); File.WriteAllText(Path.Combine(Install, "VineMix.Engine.exe"), "example old engine");
                File.WriteAllText(Path.Combine(Install, "installation.ini"), "[Installation]\nApplicationId=" + WindowsUpdatePlatform.ApplicationId);
                File.WriteAllText(Path.Combine(Install, "unins000.exe"), "example uninstaller"); File.WriteAllText(Path.Combine(Install, "unins000.dat"), "example uninstall data");
                File.WriteAllText(Path.Combine(Install, "unlisted-user-file.txt"), "personal notes fixture");
                var entries = new[] { "VineMix.exe", "VineMix.Engine.exe" }.Select(path => new { path, size = new FileInfo(Path.Combine(Install, path)).Length, sha256 = UpdateFiles.Hash(Path.Combine(Install, path)) });
                File.WriteAllText(Path.Combine(Install, "SetupSupport", "package-manifest.json"), JsonSerializer.Serialize(new { version = "1.0.0", numericVersion = "1.0.0.0", files = entries }));
            }
        }
        internal int Run(FakePlatform platform) => UpdateWorker.Run(Context.TransactionPath, platform, Trust, Root);
        public void Dispose()
        {
            string resolved = Path.GetFullPath(Base);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("VineMix-UpdateWorker-", StringComparison.Ordinal)) throw new IOException("Unsafe fixture cleanup path.");
            Directory.Delete(resolved, true);
        }
    }

    private sealed class FakePlatform(Fixture fixture) : IUpdateWorkerPlatform
    {
        internal List<string> Events { get; } = [];
        internal bool Healthy = true, ParentAlive = true, ParentExits = true, CandidateEngineExits = true, SmokePasses = true,
            ChangeTargetAfterHealth, HealthWasRequired, ReopenFails;
        internal int SetupExitCode;
        private bool _candidateAlive;
        public IDisposable AcquireLock() { Events.Add("lock-held"); return new Lease(() => Events.Add("lock-released")); }
        private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
        public bool IsSameLiveProcess(UpdateProcessIdentity identity) => identity.ProcessId == 103 ? _candidateAlive : ParentAlive;
        public bool WaitForExit(UpdateProcessIdentity identity, TimeSpan timeout)
        {
            if (identity.ProcessId == 101) { Events.Add("parent-exit"); return ParentExits; }
            if (identity.ProcessId == 102) { Events.Add("engine-exit"); return true; }
            Events.Add("candidate-exit"); _candidateAlive = false; return true;
        }
        public void VerifyWorker(VerifiedUpdate context) { }
        public void CheckFileVersion(string path, string numericVersion) { Events.Add("version-check"); }
        public UpdateRegistrySnapshot? ReadRegistration(VerifiedUpdate context) => context.Data.Standalone ? null
            : new("example-owned-key", context.TargetDirectory, []);
        public void RestoreRegistration(UpdateRegistrySnapshot original, UpdateRegistrySnapshot? current) => Events.Add("registry-restored");
        public void RegisterRecovery(VerifiedUpdate context, int attempt) => Events.Add("recovery-registered");
        public void ClearRecovery(VerifiedUpdate context) => Events.Add("recovery-cleared");
        public int RunSetup(VerifiedUpdate context, Action<UpdateProcessIdentity> started)
        {
            Require(UpdateFiles.SamePath(context.TargetDirectory, fixture.Install), "Setup must retain the exact prior owned installation directory.");
            Events.Add("setup"); started(new(104, 1004, context.AssetPath, UpdateFiles.Hash(context.AssetPath)));
            File.WriteAllText(context.TargetPath, Fixture.NewBytes);
            File.WriteAllText(Path.Combine(context.TargetDirectory, "VineMix.Engine.exe"), "example new engine");
            if (SetupExitCode == 0)
            {
                var entries = new[] { "VineMix.exe", "VineMix.Engine.exe" }.Select(path => new { path,
                    size = new FileInfo(Path.Combine(context.TargetDirectory, path)).Length, sha256 = UpdateFiles.Hash(Path.Combine(context.TargetDirectory, path)) });
                File.WriteAllText(Path.Combine(context.TargetDirectory, "SetupSupport", "package-manifest.json"), JsonSerializer.Serialize(new
                    { version = context.Data.Version, numericVersion = context.Offer.Manifest.NumericVersion, files = entries }));
            }
            return SetupExitCode;
        }
        public bool RunSmoke(VerifiedUpdate context, string evidence, Action<UpdateProcessIdentity> started) { Events.Add("smoke"); return SmokePasses; }
        public string CandidateEngineHash(VerifiedUpdate context, string evidence) => context.Data.Parent.Sha256;
        public UpdateProcessIdentity StartCandidate(VerifiedUpdate context)
        {
            Events.Add("candidate-start"); _candidateAlive = true; HealthWasRequired = true;
            var identity = new UpdateProcessIdentity(103, 1003, context.TargetPath, UpdateFiles.Hash(context.TargetPath));
            UpdateFiles.WriteJson(Path.Combine(context.Directory, "health.json"), new UpdateSignal(context.Data.Token, context.Data.EnvelopeSha256,
                identity.ProcessId, identity.StartTimeUtcTicks, Healthy, "example candidate health"));
            if (ChangeTargetAfterHealth) File.WriteAllText(context.TargetPath, "newer user payload");
            return identity;
        }
        public void ReopenPrevious(VerifiedUpdate context)
        {
            Require(fixture.OldTarget && UpdateTransaction.ReadJournal(context).Phase == UpdatePhase.RolledBack,
                "Only the verified restored prior launcher may be reopened after rollback.");
            if (ReopenFails) throw new IOException("Example reopening failure.");
            Events.Add("previous-reopened");
        }
        public bool WaitForCandidateAudioExit(VerifiedUpdate context, string? expectedHash, TimeSpan timeout)
        { Events.Add("candidate-audio-exit"); return CandidateEngineExits; }
        public void Delay(int milliseconds) { }
    }
}
