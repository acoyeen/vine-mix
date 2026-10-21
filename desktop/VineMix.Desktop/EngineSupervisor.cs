using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VineMix.Desktop;

public sealed class EngineSupervisor : IDisposable
{
    private readonly Queue<DateTime> _launches = new();
    private DateTime _lastAttempt = DateTime.MinValue;
    private Process? _process;
    private (int Pid, long Started)? _launchedIdentity;
    private static (int Pid, long Started)? _verifiedIdentity;
    internal static void ConfirmEngineIdentity(int pid)
    {
        using var process = Process.GetProcessById(pid);
        _verifiedIdentity = (pid, process.StartTime.ToUniversalTime().Ticks);
    }
    private static bool MatchesIdentity(Process process, (int Pid, long Started)? identity)
        => identity is { } value && process.Id == value.Pid && process.StartTime.ToUniversalTime().Ticks == value.Started;
    internal static string? RuntimeEnginePath { get; set; }
    private readonly string? _enginePath = RuntimeEnginePath;
    private readonly string _settingsPath = WorkspacePaths.Settings;
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    public string EnginePath => _enginePath ?? "VineMix.Engine.exe was not found next to the application.";
    public bool CanRestart => _enginePath is not null;
    public bool IsRunning => FindEngine() is not null;

    internal static ProcessStartInfo CreateStartInfo(string enginePath, string settingsPath)
    {
        string executable = Path.GetFullPath(enginePath);
        var start = new ProcessStartInfo(executable)
        { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true };
        // Every cold start and recovery must open the controller's saved
        // workspace explicitly instead of relying on the engine's default.
        start.ArgumentList.Add("--settings");
        start.ArgumentList.Add(Path.GetFullPath(settingsPath));
        return start;
    }

    private bool HasLaunchBudget
    {
        get
        {
            while (_launches.TryPeek(out var time) && DateTime.UtcNow - time > TimeSpan.FromMinutes(1)) _launches.Dequeue();
            return _launches.Count < 3;
        }
    }

    private Process? FindEngine()
    {
        if (_process is not null)
        {
            try { if (IsOwnedProcess(_process)) return _process; }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            _process.Dispose(); _process = null;
        }
        if (_enginePath is null) return null;
        using var current = Process.GetCurrentProcess();
        int session = current.SessionId;
        foreach (var candidate in Process.GetProcessesByName("VineMix.Engine"))
        {
            try
            {
                if (_process is null && candidate.SessionId == session && string.Equals(candidate.MainModule?.FileName, _enginePath, StringComparison.OrdinalIgnoreCase))
                {
                    _process = candidate;
                }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            if (!ReferenceEquals(candidate, _process)) candidate.Dispose();
        }
        return _process;
    }

    private bool IsOwnedProcess(Process process)
    {
        using var current = Process.GetCurrentProcess();
        return !process.HasExited && process.SessionId == current.SessionId
            && string.Equals(process.MainModule?.FileName, _enginePath, StringComparison.OrdinalIgnoreCase)
            && (MatchesIdentity(process,_launchedIdentity) || MatchesIdentity(process,_verifiedIdentity));
    }

    public void ValidateRestartTarget()
    {
        if (_enginePath is null) throw new InvalidOperationException(EnginePath);
        if (!File.Exists(_enginePath)) throw new IOException("The audio engine executable is missing. Restore it before restarting audio.");
        SessionOwnership.ValidateEngine(_enginePath);
        using var current = Process.GetCurrentProcess();
        foreach (var candidate in Process.GetProcessesByName("AudioMixer.Engine"))
        {
            using (candidate)
            {
                try
                {
                    if (candidate.HasExited || candidate.SessionId != current.SessionId) continue;
                    if (!string.Equals(candidate.MainModule?.FileName, _enginePath, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Another VineMix installation is running. Close it before restarting this engine.");
                }
                catch (InvalidOperationException) { } // It exited during enumeration.
                catch (System.ComponentModel.Win32Exception error)
                { throw new IOException("The running audio engine could not be identified safely.", error); }
            }
        }
    }

    public async Task RestartExplicitAsync(CancellationToken token)
    {
        await _restartGate.WaitAsync(token);
        try
        {
            ValidateRestartTarget();
            var process = FindEngine();
            if (process is not null)
            {
                token.ThrowIfCancellationRequested();
                if (!IsOwnedProcess(process)) throw new IOException("The audio engine changed before it could be restarted.");
                process.Kill();
                using var stopping = CancellationTokenSource.CreateLinkedTokenSource(token);
                stopping.CancelAfter(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(stopping.Token);
                process.Dispose(); _process = null;
            }
            token.ThrowIfCancellationRequested();
            ValidateRestartTarget();
            // A deliberate recovery is allowed even after automatic recovery
            // exhausted its budget. Still count this launch against that budget.
            _lastAttempt = DateTime.UtcNow; _launches.Enqueue(_lastAttempt);
            _process = Process.Start(CreateStartInfo(_enginePath!, _settingsPath))
                ?? throw new IOException("The audio engine could not be started.");
            _launchedIdentity = (_process.Id, _process.StartTime.ToUniversalTime().Ticks);
            await Task.Delay(150, token);
            if (!IsOwnedProcess(_process)) throw new IOException("The audio engine exited before it could reconnect.");
        }
        finally { _restartGate.Release(); }
    }

    public string EnsureStarted()
    {
        if (_enginePath is null) return EnginePath;
        if (FindEngine() is not null) return "Connecting to the audio engine…";
        if (!HasLaunchBudget) return "Automatic recovery limit reached. Waiting before another attempt…";
        if (DateTime.UtcNow - _lastAttempt < TimeSpan.FromSeconds(3)) return "Starting the audio engine…";
        _lastAttempt = DateTime.UtcNow;
        // Failed launch attempts also count, so a missing runtime cannot create a restart loop.
        _launches.Enqueue(_lastAttempt);
        try
        {
            SessionOwnership.ValidateEngine(_enginePath);
            _process = Process.Start(CreateStartInfo(_enginePath, _settingsPath));
            if (_process is not null) _launchedIdentity = (_process.Id, _process.StartTime.ToUniversalTime().Ticks);
            return "Starting the audio engine…";
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return "Could not start the audio engine: " + error.Message;
        }
    }

    public async Task<string> RecoverStalledAsync(CancellationToken token)
    {
        if (!HasLaunchBudget) return "Audio processing stalled. Recovery is paused to prevent repeated restarts.";
        var process = FindEngine();
        if (process is not null)
        {
            try
            {
                if (!IsOwnedProcess(process)) return "This engine's workspace has not been verified. Quit its owning session before switching.";
                process.Kill();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
            {
                if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                return "Audio processing stalled. Could not restart the engine: " + error.Message;
            }
        }
        return EnsureStarted();
    }
    public async Task StopForQuitAsync()
    {
        var process = FindEngine();
        if (process is null) return;
        if (!IsOwnedProcess(process)) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // Explicit Quit may terminate this controller's verified engine.
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
    internal async Task WaitForUpdateExitAsync(int expectedPid, CancellationToken token)
    {
        if (_verifiedIdentity is not { } identity || identity.Pid != expectedPid)
            throw new IOException("The audio engine owner was not verified for this update.");
        Process process;
        try { process = Process.GetProcessById(expectedPid); }
        catch (ArgumentException) { return; } // The verified process already exited cooperatively.
        using (process)
        {
            if (process.HasExited) return;
            if (!MatchesIdentity(process, identity)
                || !string.Equals(process.MainModule?.FileName, _enginePath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The audio engine identity changed before update shutdown completed.");
            await process.WaitForExitAsync(token);
        }
    }
    // Disposing a controller releases its process handle only. The engine outlives the UI.
    public void Dispose() => _process?.Dispose();
}

public static class StartupRegistration
{
    internal static bool Owns(string? command, string executable)
        => command is not null && command.Trim().Equals("\"" + Path.GetFullPath(executable) + "\" --background", StringComparison.OrdinalIgnoreCase);
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        string executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "VineMix.exe");
        if (enabled)
        {
            // An entry belonging to another launcher remains authoritative until
            // that copy is deliberately moved or upgraded.
            string? current = key.GetValue("VineMix") as string;
            string? legacy = key.GetValue("AudioMixer") as string;
            if (legacy is not null && !Owns(legacy,executable))
                throw new IOException("An older VineMix copy is registered to start at login. Disable that copy's startup setting before enabling this copy.");
            if (current is not null && !Owns(current, executable))
                throw new IOException("Start at login belongs to another VineMix copy. Change it from that copy before switching launchers.");
            key.SetValue("VineMix", "\"" + executable + "\" --background", RegistryValueKind.String);
        }
        else if (Owns(key.GetValue("VineMix") as string, executable)) key.DeleteValue("VineMix", false);
        // Only this executable may retire its own legacy registration.
        if (Owns(key.GetValue("AudioMixer") as string, executable)) key.DeleteValue("AudioMixer", false);
    }
}
