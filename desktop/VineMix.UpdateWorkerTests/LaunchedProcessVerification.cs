using System.Diagnostics;
using System.Text.Json;
using VineMix.Desktop;
using VineMix.UpdateCore;

internal static class LaunchedProcessVerification
{
    internal static bool TryRunChild(string[] args, out int result)
    {
        result = 0;
        if (args.Length != 2 || args[0] != "--identity-child") return false;
        if (args[1] == "live")
        {
            Console.WriteLine("ready");
            var input = Console.In.ReadLineAsync();
            result = input.Wait(TimeSpan.FromSeconds(15)) && input.Result == "finish" ? 0 : 1;
        }
        else result = args[1] == "exit" ? 0 : 7;
        return true;
    }

    internal static void Run(Action<bool, string> require, string[] args)
    {
        string executable = Path.ChangeExtension(typeof(LaunchedProcessVerification).Assembly.Location, ".exe");
        require(File.Exists(executable), "Real launch checks require this test program's inert apphost.");
        var platform = new WindowsUpdatePlatform();
        string hash = UpdateFiles.Hash(executable);

        for (int attempt = 0; attempt < 12; attempt++)
        {
            using var child = StartChild(executable, "live");
            try
            {
                // Capture immediately, before waiting for managed startup. Native
                // image lookup must not depend on the child's module list being ready.
                var identity = UpdateProcessIdentity.TryCaptureStarted(child, executable);
                require(identity is not null && identity.ProcessId == child.Id
                    && identity.StartTimeUtcTicks == child.StartTime.ToUniversalTime().Ticks
                    && UpdateFiles.SamePath(identity.ImagePath, executable) && identity.Sha256 == hash,
                    "An immediately launched live child retains its exact PID, creation time, image and digest.");
                require(platform.IsSameLiveProcess(identity!), "The recorded live identity matches the child before waiting for managed startup.");
                require(!platform.WaitForExit(identity!, TimeSpan.Zero), "The exact live child is still running before managed readiness without module enumeration.");
                var ready = child.StandardOutput.ReadLineAsync();
                require(ready.Wait(TimeSpan.FromSeconds(10)) && ready.Result == "ready",
                    "The inert live child becomes ready within its bounded lifetime.");
                require(platform.WaitForExit(identity! with { StartTimeUtcTicks = identity!.StartTimeUtcTicks + 1 }, TimeSpan.Zero)
                    && !child.HasExited, "A reused-PID creation-time mismatch never waits on or stops the current child.");
                require(!WindowsUpdatePlatform.SameProcess(identity!, identity! with { ProcessId = identity!.ProcessId + 1 })
                    && !WindowsUpdatePlatform.SameProcess(identity!, identity! with { ImagePath = Path.Combine(Path.GetDirectoryName(executable)!, "other.exe") })
                    && !WindowsUpdatePlatform.SameProcess(identity!, identity! with { Sha256 = new string('0', 64) }),
                    "PID, image path and digest remain independent identity guards.");

                string other = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                Reject(() => UpdateProcessIdentity.TryCaptureStarted(child, other), require,
                    "A launch cannot be recorded against a different expected executable.");
                child.StartInfo.FileName = other;
                try
                {
                    Reject(() => UpdateProcessIdentity.TryCaptureStarted(child, other), require,
                        "Native image validation rejects a misleading StartInfo path on a live process.");
                }
                finally { child.StartInfo.FileName = executable; }
                child.StandardInput.WriteLine("finish"); child.StandardInput.Flush();
                require(child.WaitForExit(10000) && child.ExitCode == 0, "The inert child exits normally without termination.");
                require(UpdateProcessIdentity.TryCaptureStarted(child, executable) is null,
                    "A retained child proven to have exited needs no recovery process identity.");
                require(platform.WaitForExit(identity!, TimeSpan.Zero), "The completed recorded child no longer blocks recovery.");
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.StandardInput.Close();
                    child.WaitForExit(16000); // Child's own bounded input wait exits; never kill it.
                }
            }
        }

        foreach (string mode in new[] { "exit", "fail" })
        for (int attempt = 0; attempt < 12; attempt++)
        {
            using var child = StartChild(executable, mode);
            require(child.WaitForExit(10000), "The fast inert child finishes before delayed identity capture.");
            require(UpdateProcessIdentity.TryCaptureStarted(child, executable) is null,
                "A fast completed child does not fail identity capture or reopen a potentially reused PID.");
            require(child.ExitCode == (mode == "exit" ? 0 : 7),
                "Omitting an exited child's identity preserves its real success or failure exit code.");
        }

        if (args.Length == 0) return;
        if (args.Length != 4 || args[0] != "--published-smoke")
            throw new ArgumentException("Optional arguments: --published-smoke <signed standalone executable> <update envelope> <new evidence directory>.");
        PublishedSmoke(args[1], args[2], args[3], require);
    }

    private static Process StartChild(string executable, string mode)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true
        };
        start.ArgumentList.Add("--identity-child"); start.ArgumentList.Add(mode);
        return Process.Start(start) ?? throw new IOException("The inert identity fixture could not start.");
    }

    private static void Reject(Action action, Action<bool, string> require, string message)
    {
        bool rejected = false;
        try { action(); } catch (IOException) { rejected = true; }
        require(rejected, message);
    }

    private static void PublishedSmoke(string executable, string envelopePath, string directory, Action<bool, string> require)
    {
        executable = Path.GetFullPath(executable); directory = Path.GetFullPath(directory);
        string fileName = Path.GetFileName(executable);
        const string prefix = "VineMix-", suffix = "-Standalone-x64.exe";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) || !fileName.EndsWith(suffix, StringComparison.Ordinal))
            throw new IOException("The inert published check requires a canonical standalone asset filename.");
        string version = fileName[prefix.Length..^suffix.Length];
        var offer = UpdateTrust.Production.Verify(File.ReadAllBytes(envelopePath), version);
        UpdateFiles.VerifyAsset(executable, offer.Manifest.Standalone);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Choose a new published-check evidence directory.");
        Directory.CreateDirectory(directory);
        var data = new UpdateTransactionData
        {
            Id = Guid.NewGuid().ToString("N"), Version = version, CurrentVersion = "1.0.0",
            Token = new string('0', 64), EnvelopeSha256 = offer.EnvelopeSha256,
            AssetFileName = offer.Manifest.Standalone.FileName, Standalone = true,
            Parent = new(1, 1, executable, offer.Manifest.Standalone.Sha256)
        };
        // Only RunSmoke is exercised. No transaction, registry, application
        // startup, engine, settings or installation operation is invoked.
        var context = new VerifiedUpdate(directory, data, offer);
        var platform = new WindowsUpdatePlatform();
        int identities = 0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            require(platform.RunSmoke(context, Path.Combine(directory, "smoke-" + attempt), identity =>
            {
                identities++;
                require(identity.ProcessId > 0 && identity.StartTimeUtcTicks > 0
                    && UpdateFiles.SamePath(identity.ImagePath, executable)
                    && UpdateFiles.EqualHash(identity.Sha256, offer.Manifest.Standalone.Sha256),
                    "The actual published inert child carries the verified asset's process identity.");
            }), "The actual signed published package passes the production inert smoke launch.");
        }
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--release-smoke"); start.ArgumentList.Add(Path.Combine(directory, "smoke-delayed-capture"));
        using (var child = Process.Start(start) ?? throw new IOException("The published inert child could not start."))
        {
            require(child.WaitForExit(60000) && child.ExitCode == 0, "The actual published inert package completes before deliberately delayed identity capture.");
            require(UpdateProcessIdentity.TryCaptureStarted(child, executable) is null,
                "A completed published inert check no longer causes a false process-image failure.");
        }
        File.WriteAllText(Path.Combine(directory, "launch-identity-results.json"), JsonSerializer.Serialize(new
        {
            passed = true, version, fileName, sha256 = offer.Manifest.Standalone.Sha256,
            signedEnvelopeSha256 = offer.EnvelopeSha256, productionSmokeRuns = 3,
            recordedLiveIdentities = identities, completedPublishedChildCapturePassed = true,
            scope = "Production smoke launch with the unchanged signed published standalone package, plus deliberate capture after exit. Inert package checks only; no installed transaction, ordinary application startup, engine, personal settings, drivers or registry operations."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
