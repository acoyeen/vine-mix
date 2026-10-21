using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace VineMix.Desktop;

/// <summary>Detached launch decisions and the Windows command-line parser; never starts a process.</summary>
internal static class StandaloneLaunchVerification
{
    internal static void Run(Action<bool, string> require)
    {
        const string executable = @"C:\VineMix builds\音声\VineMix.exe";
        const string directory = @"C:\VineMix builds\音声";
        string[] original = ["--background", "", "two words", "a\"quoted\"value", @"D:\Saved workspace\", "\\\"", "plain", "日本語"];
        int launches = 0;
        string? requestedExecutable = null, requestedDirectory = null;
        IReadOnlyList<string>? requestedArguments = null;
        void Record(string path, IReadOnlyList<string> arguments, string workingDirectory)
        {
            launches++; requestedExecutable = path; requestedArguments = arguments; requestedDirectory = workingDirectory;
        }
        var first = StandaloneLaunch.EnsureStandalone(executable, original, directory, Record);
        require(first.State == StandaloneLaunchState.Relaunched && first.Error.Length == 0 && launches == 1,
            "A normal startup performs exactly one desktop-context relaunch before opening preferences, the singleton or the engine.");
        require(requestedExecutable == executable && requestedDirectory == directory && requestedArguments is not null
            && requestedArguments.SequenceEqual(original.Append(StandaloneLaunch.ChildMarker))
            && !original.Contains(StandaloneLaunch.ChildMarker),
            "Relaunch preserves every original argument and background mode, appends one internal marker and never mutates the caller's arguments.");
        var second = StandaloneLaunch.EnsureStandalone(executable, requestedArguments!, directory, Record);
        require(second.State == StandaloneLaunchState.Continue && launches == 1,
            "The child marker allows the standalone child to continue without another process or a relaunch loop.");
        require(StandaloneLaunch.EnsureStandalone(executable, ["--STANDALONE-CONTEXT", "--background"], directory, Record).State == StandaloneLaunchState.Continue
            && launches == 1, "The internal child marker follows the application's case-insensitive argument handling.");
        foreach (string mode in new[] { "--render-design", "--preview", "--verify-ui" })
            require(StandaloneLaunch.EnsureStandalone(executable, [mode], directory, Record).State == StandaloneLaunchState.Continue
                && launches == 1, "The inert " + mode + " mode bypasses the process launcher entirely.");
        var failed = StandaloneLaunch.EnsureStandalone(executable, original, directory,
            (_, _, _) => throw new Win32Exception(87));
        require(failed.State == StandaloneLaunchState.Failed && failed.Error.Contains("87", StringComparison.Ordinal),
            "A failed desktop-context launch reports its Windows error and never falls through into another settings view.");

        string command = StandaloneLaunch.BuildCommandLine(executable, original);
        var parsed = Parse(command);
        require(parsed.SequenceEqual(new[] { executable }.Concat(original)),
            "Windows parses the exact executable and all empty, spaced, quoted, Unicode and trailing-backslash arguments without splitting or corruption.");
        require(Parse(StandaloneLaunch.BuildCommandLine(executable, [])).SequenceEqual(new[] { executable }),
            "A launch without user arguments still quotes the Unicode executable correctly.");
        bool rejected = false;
        try { StandaloneLaunch.BuildCommandLine(executable, [new string('x', 32767)]); }
        catch (ArgumentException) { rejected = true; }
        require(rejected, "An overlong command line is rejected before calling the Windows process API.");
    }

    private static string[] Parse(string command)
    {
        IntPtr arguments = CommandLineToArgvW(command, out int count);
        if (arguments == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new string[count];
            for (int index = 0; index < count; index++)
                result[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, index * IntPtr.Size))!;
            return result;
        }
        finally { LocalFree(arguments); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
