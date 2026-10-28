using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;

namespace VineMix.Desktop;

// Standalone payloads are immutable and keyed by the reviewed archive hash. The
// desktop launch normalizes its Windows data context before calling this class.
internal static class EmbeddedRuntime
{
    private const string Engine = "VineMix.Engine.exe";
    private const long MaximumPayload = 128L * 1024 * 1024;
    private sealed record RuntimeFile(string Path, long Size, string Sha256);
    private sealed record RuntimeManifest(int SchemaVersion, string Engine, long ArchiveSize, string ArchiveSha256, RuntimeFile[] Files);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static string BuildMode =>
#if VINEMIX_STANDALONE
        "Standalone";
#elif VINEMIX_INSTALLED
        "Installed";
#else
        throw new InvalidOperationException("The VineMix build mode was not specified.");
#endif

    internal static string ValidatePackage()
    {
        if (BuildMode == "Installed")
        {
            foreach (string name in new[] { Engine, "libc++.dll", "libunwind.dll", "libwinpthread-1.dll" })
                if (!File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, name)))
                    throw new FileNotFoundException("The installed VineMix runtime is incomplete.", name);
            return "Installed runtime present";
        }
        var manifest = ReadManifest();
        using var payload = ReadArchive(manifest);
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        VerifyArchive(zip, manifest, null);
        return manifest.ArchiveSha256;
    }

    internal static string ResolveEnginePath(string? runtimeRootOverride = null)
    {
        if (BuildMode == "Installed")
        {
            ValidatePackage();
            return System.IO.Path.Combine(AppContext.BaseDirectory, Engine);
        }
        RuntimeManifest manifest = ReadManifest();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) throw new IOException("Windows local application data is unavailable.");
        string root = System.IO.Path.Combine(local, "VineMix", "runtime");
        return ResolveStandalone(manifest, runtimeRootOverride is null ? root : System.IO.Path.GetFullPath(runtimeRootOverride));
    }

    private static string ResolveStandalone(RuntimeManifest manifest, string root)
    {
        RejectReparsePoints(root);
        Directory.CreateDirectory(root);
        string cache = System.IO.Path.Combine(root, manifest.ArchiveSha256.ToLowerInvariant());
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new IOException("Windows user identity is unavailable.");
        using var mutex = new Mutex(false, "Local\\VineMix.Runtime." + sid + "." + manifest.ArchiveSha256.ToLowerInvariant());
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(45)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("Another VineMix window is preparing its runtime. Please try again.");
            if (ValidateCache(cache, manifest)) return System.IO.Path.Combine(cache, Engine);
            // Never replace individual loaded files. Preserve a corrupt directory
            // for diagnosis, and refuse repair while any of its files are in use.
            if (Directory.Exists(cache))
            {
                RejectReparsePoints(cache);
                foreach (string file in SafeEntries(cache).Where(File.Exists))
                {
                    RejectReparsePoints(file);
                    using var lease = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                Directory.Move(cache, cache + ".invalid-" + Guid.NewGuid().ToString("N"));
            }
            string stage = System.IO.Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            // A failed stage is deliberately retained: cache repair never deletes
            // unfamiliar files or follows an externally supplied link.
            using (var payload = ReadArchive(manifest))
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read)) VerifyArchive(zip, manifest, stage);
            if (!ValidateCache(stage, manifest)) throw new IOException("The extracted VineMix runtime failed its integrity check.");
            Directory.Move(stage, cache);
            return System.IO.Path.Combine(cache, Engine);
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    private static RuntimeManifest ReadManifest()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VineMix.Runtime.manifest.json")
            ?? throw new IOException("The standalone VineMix runtime manifest is missing.");
        if (stream.Length > 1024 * 1024) throw new IOException("Runtime manifest exceeds its limit.");
        RuntimeManifest manifest = JsonSerializer.Deserialize<RuntimeManifest>(stream, JsonOptions)
            ?? throw new IOException("Runtime manifest is invalid.");
        if (manifest.SchemaVersion != 1 || manifest.Engine != Engine || manifest.ArchiveSize <= 0 ||
            manifest.ArchiveSize > MaximumPayload || !IsHash(manifest.ArchiveSha256) || manifest.Files is null ||
            manifest.Files.Length < 6 || manifest.Files.Length > 100 ||
            manifest.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Length)
            throw new IOException("Runtime manifest is invalid.");
        long total = 0;
        foreach (var file in manifest.Files)
        {
            ValidateRelativePath(file.Path);
            if (file.Size < 0 || file.Size > MaximumPayload || !IsHash(file.Sha256)) throw new IOException("Runtime file metadata is invalid.");
            total = checked(total + file.Size);
        }
        if (total > MaximumPayload || !new[] { Engine, "libc++.dll", "libunwind.dll", "libwinpthread-1.dll", "LICENSE", "ATTRIBUTION.md", "THIRD_PARTY_NOTICES.md" }
            .All(name => manifest.Files.Any(file => file.Path == name))) throw new IOException("Runtime allowlist is incomplete.");
        return manifest;
    }

    private static MemoryStream ReadArchive(RuntimeManifest manifest)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VineMix.Runtime.zip")
            ?? throw new IOException("The standalone VineMix runtime archive is missing.");
        if (stream.Length != manifest.ArchiveSize) throw new IOException("Runtime archive size mismatch.");
        var data = new MemoryStream(checked((int)manifest.ArchiveSize));
        stream.CopyTo(data);
        data.Position = 0;
        if (!Convert.ToHexString(SHA256.HashData(data)).Equals(manifest.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        { data.Dispose(); throw new IOException("Runtime archive hash mismatch."); }
        data.Position = 0;
        return data;
    }

    private static void VerifyArchive(ZipArchive zip, RuntimeManifest manifest, string? destination)
    {
        if (zip.Entries.Count != manifest.Files.Length) throw new IOException("Runtime archive entry count mismatch.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            ValidateRelativePath(entry.FullName);
            RuntimeFile? file = manifest.Files.FirstOrDefault(f => f.Path == entry.FullName);
            if (file is null || !seen.Add(entry.FullName) || entry.Length != file.Size ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Runtime archive contains an unexpected entry.");
            using Stream input = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            FileStream? output = null;
            try
            {
                if (destination is not null)
                {
                    string path = System.IO.Path.Combine(destination, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    RejectReparsePoints(path);
                    output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                byte[] buffer = new byte[65536]; long total = 0; int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > file.Size) throw new IOException("Runtime entry exceeded its allowed size.");
                    hash.AppendData(buffer, 0, read); output?.Write(buffer, 0, read);
                }
                if (total != file.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Runtime entry failed its integrity check.");
            }
            finally { output?.Dispose(); }
        }
    }

    private static bool ValidateCache(string directory, RuntimeManifest manifest)
    {
        if (!Directory.Exists(directory)) return false;
        RejectReparsePoints(directory);
        var entries = SafeEntries(directory).ToArray();
        string[] files = entries.Where(File.Exists).ToArray();
        if (files.Length != manifest.Files.Length) return false;
        foreach (var file in manifest.Files)
        {
            string path = System.IO.Path.Combine(directory, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size) return false;
            using var input = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private static IEnumerable<string> SafeEntries(string directory)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectReparsePoints(entry);
            yield return entry;
            if (Directory.Exists(entry)) foreach (string child in SafeEntries(entry)) yield return child;
        }
    }
    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || System.IO.Path.IsPathRooted(path) || path.Contains('\\')) throw new IOException("Unsafe runtime path.");
        foreach (string part in path.Split('/'))
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
                new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(part.Split('.')[0], StringComparer.OrdinalIgnoreCase))
                throw new IOException("Unsafe runtime path.");
    }
    private static void RejectReparsePoints(string path)
    {
        for (string? current = System.IO.Path.GetFullPath(path); current is not null; current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("VineMix runtime paths cannot use a symbolic link or junction.");
    }
}
