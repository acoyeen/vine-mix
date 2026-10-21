using System;
using System.IO;

namespace VineMix.Desktop;

internal static class WorkspacePaths
{
    internal static string? CustomSettings { get; set; }
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VineMix");
    internal static string Settings => CustomSettings ?? Path.Combine(DataDirectory, "settings.json");
    internal static string Preferences => Path.Combine(Path.GetDirectoryName(Settings)!, "ui.json");
}
