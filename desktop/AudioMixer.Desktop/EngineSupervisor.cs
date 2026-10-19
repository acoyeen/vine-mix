using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AudioMixer.Desktop;

public sealed class EngineSupervisor : IDisposable
{
    private readonly Queue<DateTime> _launches = new();
    private DateTime _lastAttempt = DateTime.MinValue;
    private Process? _process;
    private readonly string? _enginePath = LocateEngine();
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    public string EnginePath => _enginePath ?? "AudioMixer.Engine.exe was not found next to the application.";
    public bool CanRestart => _enginePath is not null;
    public bool IsRunning => FindEngine() is not null;

    private static string? LocateEngine()
    {
        string? custom = Environment.GetEnvironmentVariable("AUDIOMIXER_ENGINE_PATH");
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return Path.GetFullPath(custom);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; directory is not null && depth < 9; depth++, directory = directory.Parent)
        {
            foreach (string relative in new[] { "AudioMixer.Engine.exe", "artifacts/app/AudioMixer.Engine.exe", "artifacts/package/AudioMixer.Engine.exe", "build/native/AudioMixer.Engine.exe", "build/native/Release/AudioMixer.Engine.exe", "build/Release/AudioMixer.Engine.exe", "build/AudioMixer.Engine.exe", "native/build/AudioMixer.Engine.exe", "native/build/Release/AudioMixer.Engine.exe" })
            {
                string candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        return null;
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
        foreach (var candidate in Process.GetProcessesByName("AudioMixer.Engine"))
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
            && string.Equals(process.MainModule?.FileName, _enginePath, StringComparison.OrdinalIgnoreCase);
    }

    public void ValidateRestartTarget()
    {
        if (_enginePath is null) throw new InvalidOperationException(EnginePath);
        if (!File.Exists(_enginePath)) throw new IOException("The audio engine executable is missing. Restore it before restarting audio.");
        using var current = Process.GetCurrentProcess();
        foreach (var candidate in Process.GetProcessesByName("AudioMixer.Engine"))
        {
            using (candidate)
            {
                try
                {
                    if (candidate.HasExited || candidate.SessionId != current.SessionId) continue;
                    if (!string.Equals(candidate.MainModule?.FileName, _enginePath, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Another Vine Mix installation is running. Close it before restarting this engine.");
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
            _process = Process.Start(new ProcessStartInfo(_enginePath!)
            { WorkingDirectory = Path.GetDirectoryName(_enginePath)!, UseShellExecute = false, CreateNoWindow = true })
                ?? throw new IOException("The audio engine could not be started.");
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
            _process = Process.Start(new ProcessStartInfo(_enginePath) { WorkingDirectory = Path.GetDirectoryName(_enginePath)!, UseShellExecute = false, CreateNoWindow = true });
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
    // Disposing a controller releases its process handle only. The engine outlives the UI.
    public void Dispose() => _process?.Dispose();
}

public static class StartupRegistration
{
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled)
        {
            string executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AudioMixer.exe");
            key.SetValue("AudioMixer", "\"" + executable + "\" --background", RegistryValueKind.String);
        }
        else key.DeleteValue("AudioMixer", false);
    }
}
