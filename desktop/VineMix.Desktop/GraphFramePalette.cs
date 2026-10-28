using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace VineMix.Desktop;

/// <summary>A small named palette, shared by saved frame metadata and its menu.</summary>
internal static class GraphFramePalette
{
    internal static IReadOnlyList<GraphFrameColor> All { get; } = Array.AsReadOnly<GraphFrameColor>(
    [
        new("sage", "Sage", Color.FromRgb(120, 190, 152)),
        new("teal", "Teal", Color.FromRgb(112, 183, 190)),
        new("blue", "Blue", Color.FromRgb(133, 164, 214)),
        new("violet", "Violet", Color.FromRgb(182, 157, 214)),
        new("rose", "Rose", Color.FromRgb(211, 148, 164)),
        new("amber", "Amber", Color.FromRgb(212, 180, 116)),
        new("slate", "Slate", Color.FromRgb(163, 173, 188))
    ]);

    internal static GraphFrameColor Get(string? key) => All.FirstOrDefault(color =>
        string.Equals(color.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    internal static string NormalizeKey(string? key) => Get(key).Key;
}

internal sealed record GraphFrameColor(string Key, string Name, Color Accent);
