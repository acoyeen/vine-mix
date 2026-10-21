using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VineMix.Desktop;

internal enum StandaloneLaunchState { Continue, Relaunched, Failed }
internal sealed record StandaloneLaunchResult(StandaloneLaunchState State, string Error = "");

/// <summary>Give normal launches one desktop file-system context before opening any user data.</summary>
internal static class StandaloneLaunch
{
    internal const string ChildMarker = "--standalone-context";
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow = 0x08000000;
    private const nuint ParentProcessAttribute = 0x00020000;
    private const uint ProcessCreateProcess = 0x0080;

    internal static StandaloneLaunchResult EnsureStandalone(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, Action<string, IReadOnlyList<string>, string>? start = null)
    {
        // A process can inherit AppData redirection without a package identity.
        // The one-time marker, rather than GetCurrentPackageFullName, prevents
        // recursion. Inert design modes also never invoke the native launcher.
        if (arguments.Any(argument => argument.Equals(ChildMarker, StringComparison.OrdinalIgnoreCase)
            || argument.Equals("--render-design", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("--preview", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("--render-beta-docs", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("--verify-ui", StringComparison.OrdinalIgnoreCase)))
            return new(StandaloneLaunchState.Continue);
        string[] childArguments = [.. arguments, ChildMarker];
        try
        {
            (start ?? StartProcess)(executable, childArguments, workingDirectory);
            return new(StandaloneLaunchState.Relaunched);
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or OutOfMemoryException)
        {
            // Continuing here would reopen the other settings view and could
            // make a saved workspace appear to have reset again.
            string detail = error is Win32Exception native ? $"Windows error {native.NativeErrorCode}." : error.Message;
            return new(StandaloneLaunchState.Failed, detail);
        }
    }

    internal static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var command = new StringBuilder(QuoteArgument(executable));
        foreach (string argument in arguments) command.Append(' ').Append(QuoteArgument(argument));
        if (command.Length >= 32767) throw new ArgumentException("The launch arguments are too long.");
        return command.ToString();
    }

    private static string QuoteArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"') quoted.Append('\\', checked(backslashes * 2 + 1)).Append('"');
            else quoted.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return quoted.Append('\\', checked(backslashes * 2)).Append('"').ToString();
    }

    // Kept independent of application startup so a test-only diagnostic can
    // exercise this exact launch operation against an inert file probe.
    internal static void StartProcess(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        string path = Path.GetFullPath(executable);
        var command = new StringBuilder(BuildCommandLine(path, arguments));
        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero) throw new InvalidOperationException("The Windows desktop shell is not available.");
        if (GetWindowThreadProcessId(shell, out uint shellId) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using var current = Process.GetCurrentProcess();
        using var shellProcess = Process.GetProcessById(checked((int)shellId));
        if (current.SessionId != shellProcess.SessionId)
            throw new InvalidOperationException("The Windows desktop shell belongs to another session.");
        // A desktop-app breakaway policy alone does not remove inherited
        // AppData redirection. Use the current desktop's trusted shell parent
        // so this process and the audio engine inherit the ordinary user view.
        IntPtr parent = OpenProcess(ProcessCreateProcess, false, shellId);
        if (parent == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr attributes = IntPtr.Zero, parentValue = IntPtr.Zero;
        bool initialized = false, created = false;
        ProcessInformation process = default;
        try
        {
            nuint size = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            attributes = Marshal.AllocHGlobal(checked((nint)size));
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            parentValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(parentValue, parent);
            if (!UpdateProcThreadAttribute(attributes, 0, ParentProcessAttribute, parentValue, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx
            {
                Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() },
                AttributeList = attributes
            };
            if (!CreateProcessW(path, command, IntPtr.Zero, IntPtr.Zero, false, ExtendedStartupInfoPresent | CreateNoWindow,
                IntPtr.Zero, Path.GetFullPath(workingDirectory), ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            created = true;
        }
        finally
        {
            if (created && process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
            if (created && process.Process != IntPtr.Zero) CloseHandle(process.Process);
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (parentValue != IntPtr.Zero) Marshal.FreeHGlobal(parentValue);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            CloseHandle(parent);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedBytes;
        public IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo Startup; public IntPtr AttributeList; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr attributes, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr attributes, uint flags, nuint attribute,
        IntPtr value, nuint size, IntPtr previousValue, IntPtr returnSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint flags, IntPtr environment, string currentDirectory, ref StartupInfoEx startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
