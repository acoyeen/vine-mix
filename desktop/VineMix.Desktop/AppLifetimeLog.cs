using System;
using System.IO;

namespace VineMix.Desktop;

/// <summary>Bounded local evidence for exits that cannot otherwise be reconstructed.</summary>
internal static class AppLifetimeLog
{
    private static readonly object Gate = new();
    private static string? _path;

    internal static void Start()
    {
        try
        {
            string directory = Path.GetDirectoryName(WorkspacePaths.Settings)!;
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "desktop.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > 512 * 1024)
                File.Move(_path, _path + ".previous", true);
            Write("Started");
        }
        catch (Exception) { /* Diagnostic I/O must never prevent startup. */ }
    }

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                if (_path is not null)
                    File.AppendAllText(_path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* Diagnostic I/O must never change application behavior. */ }
    }
}
