using System;
using System.Globalization;
using System.IO;
using System.Windows;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

public partial class MainWindow
{
    private AutomaticUpdateSchedule? _automaticUpdates;
    private bool _showAutomaticUpdateResult;

    private void InitializeAutomaticUpdates()
    {
        if (_mixer.IsPreview || _automaticUpdates is not null || ((App)Application.Current).IsQuitting) return;
        // Previous builds recorded attempts before contacting GitHub. Do not
        // treat that legacy file as proof that a check actually succeeded.
        string stamp = Path.Combine(UpdateTransaction.Root, "last-successful-check.txt");
        _automaticUpdates = new AutomaticUpdateSchedule(
            token => (_updates ??= new UpdateController(false)).CheckForScheduleAsync(token),
            () =>
            {
                try
                {
                    return File.Exists(stamp) && DateTimeOffset.TryParse(File.ReadAllText(stamp), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var last) ? last : null;
                }
                catch (Exception error) when (StampFailure(error)) { return null; }
            },
            successfulAt =>
            {
                try
                {
                    UpdateFiles.CreatePrivateDirectory(UpdateTransaction.Root);
                    File.WriteAllText(stamp, successfulAt.ToString("o", CultureInfo.InvariantCulture));
                }
                catch (Exception error) when (StampFailure(error)) { } // Keep the in-memory daily cadence when storage is unavailable.
            },
            result =>
            {
                if (result == UpdateCheckStatus.Available)
                    Workspace.SetMessage("A VineMix update is available. Open Settings → Check for updates.", false);
                else if (_showAutomaticUpdateResult && result != UpdateCheckStatus.Cancelled)
                    Workspace.SetMessage(result == UpdateCheckStatus.UpToDate ? "VineMix is up to date. Automatic checks are on."
                        : "The automatic update check could not finish. It will retry in 15 minutes; you can also check from Settings.", false);
                _showAutomaticUpdateResult = false;
            });
        _mixer.AutomaticUpdatePreferenceChanged += AutomaticUpdatePreferenceChanged;
        _automaticUpdates.SetEnabled(_mixer.AutomaticallyCheckForUpdates);
    }

    private void AutomaticUpdatePreferenceChanged(bool enabled)
    {
        _showAutomaticUpdateResult = enabled;
        _automaticUpdates?.SetEnabled(enabled, checkSoon: enabled);
        Workspace.SetMessage(enabled ? "Automatic update checks are on. Checking shortly…"
            : "Automatic update checks are off. You can still check from Settings.", false);
    }

    private static bool StampFailure(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
