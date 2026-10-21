using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VineMix.Desktop;

internal static class WorkspacePhysicalPath
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);

    internal static string Resolve(string candidate)
    {
        string path = Path.GetFullPath(candidate);
        var suffix = new Stack<string>();
        while (!File.Exists(path) && !Directory.Exists(path))
        {
            suffix.Push(Path.GetFileName(path));
            path = Path.GetDirectoryName(path) ?? throw new IOException("Cannot locate the workspace volume.");
        }
        using var handle = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Cannot resolve the physical workspace location.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        var resolved = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, resolved, (uint)resolved.Capacity, 0);
        if (length == 0 || length >= resolved.Capacity) throw new IOException("Windows did not return the physical workspace location.");
        string result = resolved.ToString();
        while (suffix.Count > 0) result = Path.Combine(result, suffix.Pop());
        return Normalize(result);
    }
    internal static string Normalize(string path) => path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
        ? @"\\" + path[8..] : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
}
