using System;
using System.IO;
using System.Linq;

namespace VineMix.Desktop;

/// <summary>Checks launch arguments without starting a process or reading any settings.</summary>
internal static class EngineLaunchVerification
{
    internal static void Run(Action<bool, string> require)
    {
        const string engine = @"C:\VineMix builds\音声\VineMix.Engine.exe";
        const string settings = @"C:\Users\Zoë Example\AppData\Local\VineMix\settings.json";
        var start = EngineSupervisor.CreateStartInfo(engine, settings);
        require(start.FileName == engine && start.WorkingDirectory == Path.GetDirectoryName(engine),
            "Engine launch keeps the full executable path and its working directory, including spaces and Unicode.");
        require(start.ArgumentList.SequenceEqual(new[] { "--settings", settings }) && start.Arguments.Length == 0,
            "Cold start and recovery supply the exact saved-workspace path as one structured argument without embedded quoting.");
        require(!start.UseShellExecute && start.CreateNoWindow,
            "Explicit settings selection retains the existing hidden, direct engine launch behavior.");

        var alternate = EngineSupervisor.CreateStartInfo(@"C:\VineMix\VineMix.Engine.exe", @"D:\Saved workspaces\日本語\settings.json");
        require(alternate.ArgumentList[1] == @"D:\Saved workspaces\日本語\settings.json"
            && alternate.WorkingDirectory == @"C:\VineMix",
            "An explicit workspace path remains independent of the engine package and launch directory.");
    }
}
