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
        new("0.3.0-beta.5", "Automatic checks keep trying", "Enabling automatic checks schedules a check shortly afterwards. Checks repeat daily and retry after temporary failures. Downloading and installing remain your choice."),
        new("0.4.0-beta", "Organize nodes into named groups", "Select nodes and press Ctrl+G. Colored frames fit their members and move them together. Rename with F2 or a double-click; Undo, Redo and complete-group copying preserve your organization."),
        new("0.4.0-beta", "More ways to mix stereo audio", "Mix, Balance, Mono, Channel Swap, Stereo Width, Pan, Crossfade, Stereo Combine and Stereo Split have their own Mixing category. Split and combine channels or blend two sources without losing their port assignments."),
        new("0.4.0-beta", "One clear Level control", "Level replaces duplicate Volume and Gain controls where both changed the same value. Linked control uses a Multiplier: 100% is unchanged, and 0% is silent."),
        new("0.4.0-beta", "Keep project credit close", "About VineMix now opens Credits and licences, including the full MIT licence and attribution policy offline. Beta releases now advance the version number, starting with 0.4.0 Beta."),
        new("0.4.0-beta", "Click away to apply exact values", "Valid numeric edits apply when you click away. Invalid text stays visible with an error. Right-click for Apply value or Cancel edit; Enter, Tab and Escape keep their existing roles."),
        new("0.4.0-beta", "Aligned Settings search", "The empty search prompt now lines up with the insertion caret and typed text, including at higher display scaling."),
        new("0.4.0-beta", "A quieter footer when you want it", "Turn off Show action notifications in Settings to hide routine timed messages. Errors and save status remain visible, and Activity keeps recent edits and the latest message. Your choice is remembered."),
        new("0.4.0-beta", "Consistent information windows", "What's New, About and Updates share a compact, centred style. Close them with Escape, Close or a click on the workspace; an update installation in progress remains protected."),
        new("0.4.0-beta", "A tidier workspace", "The node count sits beneath the logo. Settings groups workspace controls, audio tools, app preferences and help between familiar separators. Activity sits beside Find node; Help, What's new? and About stay together above the separate Quit action."),
        new("0.4.0-beta", "A matching monochrome identity", "The app logo follows the website's single-colour design. Brighter, slightly larger soft-white icons carry the same identity into the taskbar, tray and shortcuts."),
        new("0.4.0-beta", "Clearer virtual-route setup", "Setup explains which drivers virtual inputs and outputs need, selects missing drivers and installs VAC Lite before VB-CABLE. You can clear a selection; driver terms still require explicit acceptance.")
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
