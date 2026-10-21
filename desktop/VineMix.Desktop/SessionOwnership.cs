using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace VineMix.Desktop;

internal sealed class SessionOwnership : IDisposable
{
    private readonly List<Mutex> _locks = [];
    internal bool Acquire(string role)
    {
        foreach (string product in new[] { "AudioMixer", "VineMix" })
        {
            var gate = new Mutex(true, $"Local\\{product}.{role}.{EngineConnection.UserSid}", out bool created);
            if (!created) { gate.Dispose(); Dispose(); return false; }
            _locks.Add(gate);
        }
        return true;
    }
    internal static bool ValidateEngine(string expectedPath)
    {
        bool found = false;
        using var self = Process.GetCurrentProcess();
        foreach (string name in new[] { "AudioMixer.Engine", "VineMix.Engine" })
            foreach (var process in Process.GetProcessesByName(name)) using (process)
            {
                try
                {
                    if (process.HasExited || process.SessionId != self.SessionId) continue;
                    if (!string.Equals(process.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Another version of VineMix is running. Quit it from its tray menu before opening this version. Your audio and workspace have not been changed.");
                    found = true;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception error) { throw new IOException("The running engine could not be identified safely.", error); }
            }
        return found;
    }
    public void Dispose()
    {
        for (int i = _locks.Count - 1; i >= 0; --i) { _locks[i].ReleaseMutex(); _locks[i].Dispose(); }
        _locks.Clear();
    }
}
