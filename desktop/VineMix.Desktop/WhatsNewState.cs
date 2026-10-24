using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

internal sealed record ReleaseHighlight(string Version, string Title, string Description);

/// <summary>Bundled plain text only. No remote content or graph state is involved.</summary>
internal static class WhatsNewContent
{
    internal static readonly ReleaseHighlight[] All =
    [
        new("0.3.0-beta.2", "More reliable device lists", "Find streamed microphones and keep assigned devices visible when they become unavailable."),
        new("0.3.0-beta.3", "A steadier workspace", "Normal Windows resizing, improved save retries and safer update recovery."),
        new("0.3.0-beta.4", "Listen through Virtual Desktop", "Choose your streamed headset's Windows playback device in Listening output. Dedicated cable routes keep their own nodes."),
        new("0.3.0-beta.4", "Lock your layout", "Use the top-right padlock to protect node positions and connections. Volume, mute and other sound controls remain available. The lock lasts until you quit."),
        new("0.3.0-beta.4", "Quit with your work protected", "Closing keeps audio running in the tray. Settings now has a red Quit VineMix action, with retry and recovery choices if the final save fails."),
        new("0.3.0-beta.4", "Choose denoising with delay in view", "The algorithm menu shows SpeexDSP at 20 ms and RNNoise at 30 ms. These are VineMix node delays, including bypass, rather than total system latency."),
        new("0.3.0-beta.4", "Clearer workspace feedback", "Save status sits in the centre of the footer. Action messages appear for five seconds with a countdown and a Dismiss option."),
        new("0.3.0-beta.4", "Catch up after updates", "These highlights appear after an updated launch when your workspace is ready. To reopen them, choose Settings → What's new?"),
        new("0.3.0-beta.5", "Updates that pass version checks", "Corrected package metadata prevents the update rejection that could restore your previous beta. You can skip intermediate betas."),
        new("0.3.0-beta.5", "A simpler update window", "See your installed version, release notes and the next action in a compact window that follows your theme."),
        new("0.3.0-beta.5", "Automatic checks keep trying", "Enabling automatic checks schedules a check shortly afterwards. Checks repeat daily and retry after temporary failures. Downloading and installing remain your choice.")
    ];

    internal static IReadOnlyList<ReleaseHighlight> Since(string? previous, string current)
    {
        if (!SemanticVersion.TryParse(current, out var limit)) return [];
        SemanticVersion.TryParse(previous, out var baseline);
        return All.Where(item => SemanticVersion.TryParse(item.Version, out var version)
            && version!.CompareTo(limit) <= 0 && (baseline is null || version.CompareTo(baseline) > 0)).ToArray();
    }
}

internal sealed record WhatsNewProgress
{
    public int FormatVersion { get; init; } = 1;
    public string? BaselineReleaseVersion { get; init; }
    public string? LastSeenReleaseVersion { get; init; }
}

internal sealed class WhatsNewState
{
    private readonly string _path;
    internal WhatsNewProgress Progress { get; private set; }
    internal string? PreviousVersion => Progress.LastSeenReleaseVersion ?? Progress.BaselineReleaseVersion;
    internal WhatsNewState(string path)
    {
        _path = path;
        try
        {
            Progress = File.Exists(path) ? JsonSerializer.Deserialize<WhatsNewProgress>(File.ReadAllText(path), Protocol.Json) ?? new() : new();
            if (Progress.FormatVersion != 1) Progress = new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException) { Progress = new(); }
    }
    internal bool Pending(string current, bool existingWorkspace)
    {
        if (!SemanticVersion.TryParse(current, out var version)) return false;
        if (SemanticVersion.TryParse(PreviousVersion, out var previous)) return version!.CompareTo(previous) > 0;
        // A legacy installation has no marker: show one transition notice.
        // A genuinely fresh workspace establishes its baseline without a notice.
        return existingWorkspace;
    }
    internal void InitializeFresh(string current)
    {
        if (PreviousVersion is not null) return;
        Save(new() { BaselineReleaseVersion = current });
    }
    internal void Dismiss(string current)
    {
        // Reopening an older build must not roll back the dismissal watermark.
        if (SemanticVersion.TryParse(PreviousVersion, out var previous)
            && SemanticVersion.TryParse(current, out var version) && previous!.CompareTo(version) > 0) return;
        Save(Progress with { LastSeenReleaseVersion = current });
    }
    private void Save(WhatsNewProgress progress)
    {
        string directory = Path.GetDirectoryName(_path)!;
        UpdateFiles.CreatePrivateDirectory(directory);
        string temporary = Path.Combine(directory, ".whats-new-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(progress, Protocol.Json));
            File.Move(temporary, _path, overwrite: true);
            Progress = progress;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
