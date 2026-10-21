using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VineMix.Desktop;

/// <summary>Product identity comes from the same manifest used by packaging.</summary>
public static class ReleaseInfo
{
    private static readonly Assembly Assembly = typeof(ReleaseInfo).Assembly;
    private static readonly string? Manifest = ReadManifest();
    public static string Version => Field("version") ?? "development";
    public static string NumericVersion => Field("numericVersion") ?? Assembly.GetName().Version?.ToString() ?? "unavailable";
    public static string DisplayVersion => Field("displayVersion") ?? "Development build";
    public static string InformationalVersion => Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unavailable";
    public static string SourceRevision => Metadata("SourceRevisionId") ?? (InformationalVersion.Contains('+') ? InformationalVersion[(InformationalVersion.IndexOf('+') + 1)..] : "unavailable");
    public static string BuildConfiguration => Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unavailable";
    public static string SourceSnapshotHash => Metadata("SourceSnapshotHash") ?? "unavailable";
    public static string BuildTimestamp => Metadata("BuildTimestamp") ?? "unavailable";
#if VINEMIX_VERIFICATION
    public const string Distribution = "Verification";
#elif VINEMIX_STANDALONE
    public const string Distribution = "Standalone";
#else
    public const string Distribution = "Installed";
#endif
    public static string FullInformation => $"VineMix {DisplayVersion}\nRelease: {Version}\nFile version: {NumericVersion}\nBuild: {InformationalVersion}\nSource revision: {SourceRevision}\nSource snapshot SHA-256: {SourceSnapshotHash}\nBuilt at (UTC): {BuildTimestamp}\nConfiguration: {BuildConfiguration}\nDistribution: {Distribution}\nPlatform: Windows {RuntimeInformation.ProcessArchitecture}\n.NET: {Environment.Version}";

    private static string? Metadata(string name) => Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(item => item.Key == name)?.Value;
    private static string? ReadManifest()
    {
        using var stream = Assembly.GetManifestResourceStream("VineMix.Release.json");
        if (stream is null) return null;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static string? Field(string name)
    {
        if (Manifest is null) return null;
        try
        {
            using var document = JsonDocument.Parse(Manifest);
            return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
