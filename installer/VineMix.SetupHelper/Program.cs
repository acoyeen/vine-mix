using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace VineMix.SetupHelper;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" })) return SelfTests.Run();
            if (args.Length == 0 || args[0] != "detect") return Usage();
            string? output = null, installDirectory = null;
            for (int i = 1; i < args.Length; ++i)
            {
                if (i + 1 >= args.Length) return Usage();
                if (args[i] == "--output" && output is null) output = args[++i];
                else if (args[i] == "--install-dir" && installDirectory is null) installDirectory = Path.GetFullPath(args[++i]);
                else return Usage();
            }
            if (string.IsNullOrWhiteSpace(output)) return Usage();
            var devices = WindowsDetection.ReadDevices();
            var packages = WindowsDetection.ReadPackages();
            var vb = DetectionPolicy.Evaluate(DriverFamily.VBCable, devices, packages);
            var vac = DetectionPolicy.Evaluate(DriverFamily.VAC, devices, packages);
            var application = WindowsDetection.ReadApplication(installDirectory);
            var text = new StringBuilder("; Vine Mix read-only setup detection. No devices or processes were changed.\r\n");
            WriteDriver(text, "VBCable", vb);
            WriteDriver(text, "VAC", vac);
            text.AppendLine("[Application]");
            Write(text, "DetectSucceeded", application.Succeeded);
            Write(text, "Running", application.Running);
            Write(text, "RunningInstalled", application.RunningInstalled);
            Write(text, "InstallPathSafe", application.InstallPathSafe);
            Write(text, "State", application.Running ? "Running" : application.Succeeded ? "Stopped" : "DetectionFailed");
            Write(text, "Detail", application.Detail);
            string destination = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, text.ToString(), Encoding.ASCII);
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return vb.Succeeded && vac.Succeeded && application.Succeeded ? 0 : 2;
        }
        catch (Exception error)
        {
            // A detection failure must never turn into permission to install a second driver.
            Console.Error.WriteLine($"Setup detection could not finish ({error.GetType().Name}).");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: VineMix.SetupHelper detect --output <file> [--install-dir <directory>] | --self-test");
        return 1;
    }
    private static void WriteDriver(StringBuilder text, string section, DriverResult result)
    {
        text.AppendLine("[" + section + "]");
        Write(text, "DetectSucceeded", result.Succeeded);
        Write(text, "Installed", result.Installed);
        Write(text, "State", result.State);
        Write(text, "Detail", result.Detail);
        Write(text, "Version", result.Version);
    }
    private static void Write(StringBuilder text, string key, bool value) => Write(text, key, value ? "1" : "0");
    private static void Write(StringBuilder text, string key, string value) => text.Append(key).Append('=').AppendLine(Sanitize(value));
    internal static string Sanitize(string value) => new(value.Select(c => c >= ' ' && c <= '~' ? c : ' ').ToArray());
}

internal enum DriverFamily { VBCable, VAC }
internal record DeviceEvidence(string[] HardwareIds, string Service, string Version, string State);
internal record PackageEvidence(DriverFamily Family, string Version, bool InDriverStore);
internal record Evidence<T>(IReadOnlyList<T> Items, bool Succeeded);
internal record DriverResult(bool Succeeded, bool Installed, string State, string Detail, string Version);
internal record ProcessEvidence(bool? SameUser, string? Path, bool StillRunning);
internal record ApplicationResult(bool Succeeded, bool Running, bool RunningInstalled, string Detail, bool InstallPathSafe = true);

internal static class DetectionPolicy
{
    internal static string HardwareId(DriverFamily family) => family == DriverFamily.VBCable ? "VBAudioVACWDM" : "VirtualAudioCable_83ed7f0e";
    internal static bool MatchesHardwareId(string value, DriverFamily family) =>
        string.Equals(value.Trim().Trim('"'), HardwareId(family), StringComparison.OrdinalIgnoreCase)
        || string.Equals(value.Trim().Trim('"'), "ROOT\\" + HardwareId(family), StringComparison.OrdinalIgnoreCase);

    internal static DriverResult Evaluate(DriverFamily family, Evidence<DeviceEvidence> devices, Evidence<PackageEvidence> packages)
    {
        var matches = devices.Items.Where(d => d.HardwareIds.Any(id => MatchesHardwareId(id, family))).ToArray();
        if (matches.Length > 0)
        {
            var best = matches.OrderBy(d => d.State == "DevicePresent" ? 0 : d.State == "DeviceDisabled" ? 1 : 2).First();
            return new(true, true, best.State,
                family == DriverFamily.VAC ? "VAC driver family detected; edition not verified. Existing driver will be kept."
                    : "VB-CABLE driver detected. Existing driver will be kept.", best.Version);
        }
        var staged = packages.Items.Where(p => p.Family == family).ToArray();
        if (staged.Length > 0)
        {
            var best = staged.FirstOrDefault(p => p.InDriverStore) ?? staged[0];
            return new(best.InDriverStore, true, best.InDriverStore ? "DriverStaged" : "PackageNeedsAttention",
                best.InDriverStore ? "Driver package already staged in Windows; repair or restart may be needed."
                    : "Matching installed INF found but its driver store state could not be verified.", best.Version);
        }
        if (!devices.Succeeded || !packages.Succeeded)
            return new(false, false, "DetectionFailed", "Windows driver state could not be completely checked. Automatic driver installation is disabled.", "");
        return new(true, false, "Missing", "No matching device or staged driver package found.", "");
    }

    internal static ApplicationResult EvaluateProcesses(IReadOnlyList<ProcessEvidence> processes, bool succeeded, string? installDirectory)
    {
        bool running = false, installed = false;
        foreach (var process in processes.Where(p => p.StillRunning && p.SameUser != false))
        {
            running = true;
            if (process.SameUser is null || process.Path is null) { succeeded = false; installed = true; continue; }
            if (installDirectory is not null && IsWithin(process.Path, installDirectory)) installed = true;
        }
        if (!succeeded)
            return new(false, true, true, "A possible Vine Mix process could not be checked safely. Close Vine Mix before continuing.");
        return new(true, running, installed, running ? "Vine Mix or its audio engine is already running. The current session will be left untouched." : "No Vine Mix session is running for this Windows user.");
    }

    private static bool IsWithin(string path, string directory)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

// Parse actual [Manufacturer] model declarations. Matching words in comments,
// friendly names or AddReg values do not establish that a package installs this device.
internal sealed class InfIdentity
{
    private readonly Dictionary<string, List<(string Key, string Value)>> sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> strings = new(StringComparer.OrdinalIgnoreCase);
    internal InfIdentity(string text)
    {
        string section = "";
        string continuation = "";
        foreach (string physical in text.Split('\n'))
        {
            string line = StripComment(physical).Trim();
            line = continuation + line;
            if (line.EndsWith('\\')) { continuation = line[..^1]; continue; }
            continuation = "";
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                if (!sections.ContainsKey(section)) sections[section] = new();
            }
            else if (line.Contains('=') && section.Length > 0)
            {
                int equals = line.IndexOf('=');
                sections[section].Add((line[..equals].Trim(), line[(equals + 1)..].Trim()));
            }
        }
        foreach (var pair in sections.Where(s => s.Key.Equals("Strings", StringComparison.OrdinalIgnoreCase)
            || s.Key.Equals("Strings.0409", StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Value))
            strings[pair.Key] = Unquote(pair.Value);
    }

    internal bool Identifies(DriverFamily family)
    {
        string expectedProvider = family == DriverFamily.VBCable ? "VB-Audio Software" : "Eugene Muzychenko";
        if (!Value("Version", "Class").Equals("MEDIA", StringComparison.OrdinalIgnoreCase)
            || !Value("Version", "Provider").Equals(expectedProvider, StringComparison.OrdinalIgnoreCase)) return false;
        if (!sections.TryGetValue("Manufacturer", out var manufacturers)) return false;
        foreach (var manufacturer in manufacturers)
        {
            var fields = SplitFields(Expand(manufacturer.Value));
            if (fields.Count == 0) continue;
            var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Unquote(fields[0]) };
            foreach (string decoration in fields.Skip(1)) models.Add(Unquote(fields[0]) + "." + Unquote(decoration));
            foreach (string model in models)
            {
                if (!sections.TryGetValue(model, out var entries)) continue;
                foreach (var entry in entries)
                {
                    var ids = SplitFields(Expand(entry.Value));
                    if (ids.Skip(1).Any(id => DetectionPolicy.MatchesHardwareId(id, family))) return true;
                }
            }
        }
        return false;
    }
    internal string Version
    {
        get
        {
            var fields = SplitFields(Value("Version", "DriverVer"));
            return fields.Count >= 2 ? Unquote(fields[1]) : "";
        }
    }
    private string Value(string section, string key) => sections.TryGetValue(section, out var entries)
        ? Unquote(Expand(entries.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? "")) : "";
    private string Expand(string value)
    {
        for (int i = 0; i < 8; ++i)
        {
            string expanded = Regex.Replace(value, "%([^%]+)%", m => strings.TryGetValue(m.Groups[1].Value, out string? s) ? s : m.Value);
            if (expanded == value) break;
            value = expanded;
        }
        return value;
    }
    private static string Unquote(string value) => value.Trim().Trim('"');
    private static string StripComment(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length; ++i)
        {
            if (line[i] == '"') quoted = !quoted;
            if (line[i] == ';' && !quoted) return line[..i];
        }
        return line;
    }
    private static List<string> SplitFields(string value)
    {
        var fields = new List<string>();
        bool quoted = false;
        int start = 0;
        for (int i = 0; i < value.Length; ++i)
        {
            if (value[i] == '"') quoted = !quoted;
            if (value[i] == ',' && !quoted) { fields.Add(value[start..i].Trim()); start = i + 1; }
        }
        fields.Add(value[start..].Trim());
        return fields;
    }
}

internal static class WindowsDetection
{
    private const uint DigcfAllClasses = 4, SpdrpHardwareId = 1, SpdrpService = 4, SpdrpDriver = 9;
    private const int ErrorNoMoreItems = 259, ErrorInvalidData = 13, ErrorInsufficientBuffer = 122;

    internal static Evidence<DeviceEvidence> ReadDevices()
    {
        var items = new List<DeviceEvidence>();
        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfAllClasses);
        if (set == new IntPtr(-1)) return new(items, false);
        bool succeeded = true;
        try
        {
            for (uint index = 0; ; ++index)
            {
                var data = new SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() != ErrorNoMoreItems) succeeded = false;
                    break;
                }
                string[] ids = Property(set, ref data, SpdrpHardwareId, ref succeeded).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (!Enum.GetValues<DriverFamily>().Any(f => ids.Any(id => DetectionPolicy.MatchesHardwareId(id, f)))) continue;
                string service = Property(set, ref data, SpdrpService, ref succeeded).TrimEnd('\0');
                string registryPath = Property(set, ref data, SpdrpDriver, ref succeeded).TrimEnd('\0');
                string version = "";
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + registryPath);
                    version = key?.GetValue("DriverVersion") as string ?? "";
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
                uint result = CM_Get_DevNode_Status(out uint status, out uint problem, data.DevInst, 0);
                string state = result != 0 ? "DeviceUnavailable" : problem == 22 ? "DeviceDisabled" : problem != 0 ? "DeviceNeedsAttention" : (status & 8) != 0 ? "DevicePresent" : "DeviceUnavailable";
                items.Add(new(ids, service, version, state));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return new(items, succeeded);
    }

    private static string Property(IntPtr set, ref SpDevinfoData data, uint property, ref bool succeeded)
    {
        SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, null, 0, out uint required);
        int error = Marshal.GetLastWin32Error();
        if (required == 0)
        {
            if (error != ErrorInvalidData) succeeded = false;
            return "";
        }
        if (error != ErrorInsufficientBuffer || required > 1024 * 1024) { succeeded = false; return ""; }
        var bytes = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, bytes, (uint)bytes.Length, out _))
        { succeeded = false; return ""; }
        return Encoding.Unicode.GetString(bytes);
    }

    internal static Evidence<PackageEvidence> ReadPackages()
    {
        var items = new List<PackageEvidence>();
        bool succeeded = true;
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
            foreach (string path in Directory.EnumerateFiles(directory, "oem*.inf", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    string text = ReadInfText(bytes);
                    var inf = new InfIdentity(text);
                    foreach (var family in Enum.GetValues<DriverFamily>())
                    {
                        if (!inf.Identifies(family)) continue;
                        var location = new StringBuilder(32768);
                        bool staged = SetupGetInfDriverStoreLocation(path, IntPtr.Zero, null, location, (uint)location.Capacity, out _);
                        items.Add(new(family, inf.Version, staged));
                    }
                }
                catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                { succeeded = false; }
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { succeeded = false; }
        return new(items, succeeded);
    }

    internal static string ReadInfText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        // Vendor names and device IDs are ASCII even in legacy ANSI INFs.
        return Encoding.Latin1.GetString(bytes);
    }

    internal static ApplicationResult ReadApplication(string? installDirectory)
    {
        var items = new List<ProcessEvidence>();
        bool succeeded = true;
        string? resolvedDirectory = installDirectory is null ? null : ResolveDestination(installDirectory);
        if (installDirectory is not null && resolvedDirectory is null) succeeded = false;
        bool installPathSafe = installDirectory is null || IsSafeDestination(resolvedDirectory);
        string ownUser = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Current user cannot be identified.");
        foreach (string name in new[] { "AudioMixer", "AudioMixer.Engine" })
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch (Win32Exception) { succeeded = false; continue; }
            foreach (var process in processes)
            {
                using (process)
                {
                    IntPtr handle = OpenProcess(0x1000, false, (uint)process.Id);
                    if (handle == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 87) items.Add(new(null, null, true)); // 87: process exited during enumeration.
                        continue;
                    }
                    try
                    {
                        bool? sameUser = ProcessUser(handle) is string user ? string.Equals(user, ownUser, StringComparison.Ordinal) : null;
                        var path = new StringBuilder(32768);
                        uint size = (uint)path.Capacity;
                        string? executable = QueryFullProcessImageName(handle, 0, path, ref size) ? ResolveExistingPath(path.ToString(), out _) : null;
                        items.Add(new(sameUser, executable, true));
                    }
                    finally { CloseHandle(handle); }
                }
            }
        }
        return DetectionPolicy.EvaluateProcesses(items, succeeded, resolvedDirectory) with { InstallPathSafe = installPathSafe };
    }

    private static bool IsSafeDestination(string? directory)
    {
        if (directory is null) return false;
        string? data = ResolveDestination(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioMixer"));
        if (data is null || IsSameOrWithin(directory, data)) return false;
        foreach (var special in new[] { Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Windows, Environment.SpecialFolder.System })
        {
            string folder = Environment.GetFolderPath(special);
            if (folder.Length == 0) continue;
            string? resolved = ResolveDestination(folder);
            if (resolved is null || string.Equals(Path.TrimEndingDirectorySeparator(directory), Path.TrimEndingDirectorySeparator(resolved), StringComparison.OrdinalIgnoreCase)) return false;
        }
        return !string.Equals(Path.TrimEndingDirectorySeparator(directory), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(directory)!), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSameOrWithin(string directory, string parent)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return normalized.Equals(root, StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // Resolve existing junction/symlink components before comparing executable
    // locations. A new install directory may have a nonexisting suffix.
    internal static string? ResolveDestination(string path)
    {
        string current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var suffix = new Stack<string>();
        while (true)
        {
            string? resolved = ResolveExistingPath(current, out bool missing);
            if (resolved is not null)
            {
                while (suffix.Count > 0) resolved = Path.Combine(resolved, suffix.Pop());
                return resolved;
            }
            if (!missing) return null;
            string? parent = Path.GetDirectoryName(current);
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) return null;
            suffix.Push(Path.GetFileName(current));
            current = parent;
        }
    }

    private static string? ResolveExistingPath(string path, out bool missing)
    {
        missing = false;
        IntPtr file = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (file == new IntPtr(-1))
        {
            int error = Marshal.GetLastWin32Error();
            missing = error is 2 or 3;
            return null;
        }
        try
        {
            var resolved = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandle(file, resolved, (uint)resolved.Capacity, 0);
            return length > 0 && length < resolved.Capacity ? NormalizeFinalPath(resolved.ToString()) : null;
        }
        finally { CloseHandle(file); }
    }

    internal static string NormalizeFinalPath(string value) => value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
        ? @"\\" + value[8..] : value.StartsWith(@"\\?\", StringComparison.Ordinal) ? value[4..] : value;

    private static string? ProcessUser(IntPtr process)
    {
        if (!OpenProcessToken(process, 8, out IntPtr token)) return null;
        try
        {
            GetTokenInformation(token, 1, IntPtr.Zero, 0, out uint required);
            if (required == 0 || required > 1024 * 1024) return null;
            IntPtr info = Marshal.AllocHGlobal((int)required);
            try
            {
                return GetTokenInformation(token, 1, info, required, out _) ? new SecurityIdentifier(Marshal.ReadIntPtr(info)).Value : null;
            }
            finally { Marshal.FreeHGlobal(info); }
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData { public uint cbSize; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevinfoData data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SpDevinfoData data, uint property, out uint type, byte[]? buffer, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
    [DllImport("setupapi.dll", EntryPoint = "SetupGetInfDriverStoreLocationW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupGetInfDriverStoreLocation(string fileName, IntPtr alternativePlatformInfo, string? localeName, StringBuilder result, uint resultSize, out uint required);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, uint informationClass, IntPtr information, uint length, out uint required);
}

internal static class SelfTests
{
    internal static int Run()
    {
        int checks = 0;
        void Require(bool condition, string label) { ++checks; if (!condition) throw new InvalidOperationException("Self-test failed: " + label); }
        string infText = """
            [Version]
            Class = MEDIA
            Provider = %Vendor%
            DriverVer=10/07/2024,3.3.1.7
            [Manufacturer]
            %Vendor%=Cable,NTamd64.10.0...16299,NTarm64.10.0...16299
            [Cable.NTamd64.10.0...16299]
            %Device%=Install, %HardwareId%
            [Strings]
            Vendor="VB-Audio Software"
            HardwareId="VBAudioVACWDM"
            Device="Name may be changed; semicolon is quoted"
            """;
        var inf = new InfIdentity(infText);
        Require(inf.Identifies(DriverFamily.VBCable), "decorated Manufacturer and string substitution");
        Require(!inf.Identifies(DriverFamily.VAC), "families isolated");
        Require(inf.Version == "3.3.1.7", "driver version extraction");
        Require(!new InfIdentity(infText.Replace("%HardwareId%", "VBAudioVACWDM_EXTRA")).Identifies(DriverFamily.VBCable), "similar hardware ID rejected");
        Require(!new InfIdentity(infText.Replace("VB-Audio Software", "Unrelated Vendor")).Identifies(DriverFamily.VBCable), "vendor impersonation in friendly name rejected");
        Require(!new InfIdentity("[Version]\nClass=MEDIA\nProvider=VB-Audio Software\n; VBAudioVACWDM\n[Strings]\nDevice=VBAudioVACWDM").Identifies(DriverFamily.VBCable), "comment or display name alone rejected");
        string vac = infText.Replace("VB-Audio Software", "Eugene Muzychenko").Replace("VBAudioVACWDM", "VirtualAudioCable_83ed7f0e");
        Require(new InfIdentity(vac).Identifies(DriverFamily.VAC), "VAC source identity");
        Require(new InfIdentity(infText.Replace("%HardwareId%", "ROOT\\VBAudioVACWDM")).Identifies(DriverFamily.VBCable), "root-enumerated exact hardware identity");
        Require(WindowsDetection.ReadInfText(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(infText)).ToArray()).Contains("VB-Audio Software"), "UTF-16 original INF");
        var noDevices = new Evidence<DeviceEvidence>(Array.Empty<DeviceEvidence>(), true);
        var noPackages = new Evidence<PackageEvidence>(Array.Empty<PackageEvidence>(), true);
        Require(DetectionPolicy.Evaluate(DriverFamily.VBCable, noDevices, noPackages) is { Succeeded: true, Installed: false, State: "Missing" }, "only complete negative evidence permits install");
        foreach (string state in new[] { "DevicePresent", "DeviceDisabled", "DeviceUnavailable", "DeviceNeedsAttention" })
        {
            var devices = new Evidence<DeviceEvidence>(new[] { new DeviceEvidence(new[] { "VBAudioVACWDM" }, "", "1", state) }, true);
            Require(DetectionPolicy.Evaluate(DriverFamily.VBCable, devices, noPackages) is { Installed: true, Succeeded: true }, state + " blocks duplicate install");
        }
        var staged = new Evidence<PackageEvidence>(new[] { new PackageEvidence(DriverFamily.VAC, "4.71", true) }, true);
        Require(DetectionPolicy.Evaluate(DriverFamily.VAC, noDevices, staged) is { Installed: true, State: "DriverStaged" }, "staged-only package blocks duplicate install");
        var questionable = new Evidence<PackageEvidence>(new[] { new PackageEvidence(DriverFamily.VAC, "", false) }, true);
        Require(DetectionPolicy.Evaluate(DriverFamily.VAC, noDevices, questionable) is { Installed: true, Succeeded: false }, "broken package requires attention rather than reinstall");
        Require(!DetectionPolicy.Evaluate(DriverFamily.VAC, new Evidence<DeviceEvidence>(Array.Empty<DeviceEvidence>(), false), noPackages).Succeeded, "partial Windows enumeration never means absent");
        var target = @"C:\Users\Example\AppData\Local\Programs\Vine Mix";
        Require(DetectionPolicy.EvaluateProcesses(new[] { new ProcessEvidence(true, target + @"\AudioMixer.exe", true) }, true, target) is { Running: true, RunningInstalled: true }, "running target blocks overwrite");
        Require(DetectionPolicy.EvaluateProcesses(new[] { new ProcessEvidence(true, @"E:\Other\AudioMixer.Engine.exe", true) }, true, target) is { Running: true, RunningInstalled: false }, "other folder session blocks second launch only");
        Require(DetectionPolicy.EvaluateProcesses(new[] { new ProcessEvidence(false, target + @"\AudioMixer.exe", true) }, true, target).Running == false, "other user is not current user session");
        Require(DetectionPolicy.EvaluateProcesses(new[] { new ProcessEvidence(null, null, true) }, true, target) is { Succeeded: false, Running: true, RunningInstalled: true }, "unreadable process conservatively blocks overwrite and launch");
        Require(!DetectionPolicy.EvaluateProcesses(new[] { new ProcessEvidence(true, target + @"2\AudioMixer.exe", true) }, true, target).RunningInstalled, "directory boundary avoids sibling prefix collision");
        Require(!DetectionPolicy.EvaluateProcesses(new[] { new ProcessEvidence(true, null, false) }, true, target).Running, "exited processes ignored");
        Require(!Program.Sanitize("value\r\nInjected=1\u00e9").Contains('\n'), "INI injection prevented");
        Require(WindowsDetection.NormalizeFinalPath(@"\\?\C:\Programs\Vine Mix") == @"C:\Programs\Vine Mix", "DOS final path normalization");
        Require(WindowsDetection.NormalizeFinalPath(@"\\?\UNC\server\share\Vine Mix") == @"\\server\share\Vine Mix", "UNC final path normalization");
        Require(WindowsDetection.IsSameOrWithin(@"C:\Data\AudioMixer\Nested", @"C:\Data\AudioMixer"), "protected data descendant blocked after final-path resolution");
        Require(WindowsDetection.IsSameOrWithin(@"C:\Data\AudioMixer", @"C:\Data\AudioMixer"), "protected data directory blocked after final-path resolution");
        Require(!WindowsDetection.IsSameOrWithin(@"C:\Data\AudioMixer2", @"C:\Data\AudioMixer"), "protected data sibling prefix remains distinct");
        Console.WriteLine($"PASS: {checks} setup detection checks. No devices or running processes changed.");
        return 0;
    }
}
