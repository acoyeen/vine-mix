using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

internal sealed class UpdateController(bool preview) : Observable, IDisposable
{
    private CancellationTokenSource? _operation;
    private bool _busy, _installing;
    private string _message = "Check for a newer VineMix release on GitHub.";
    private UpdateOffer? _offer;
    private DownloadedUpdate? _download;
    internal DownloadedUpdate? Download => _download;
    public string CurrentVersion => ReleaseInfo.DisplayVersion;
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string Notes => _offer?.Manifest.Notes ?? "";
    public bool IsBusy => _busy;
    public bool IsInstalling => _installing;
    public bool CanCheck => !_busy;
    public bool CanDownload => !_busy && _offer is not null && _download is null;
    public bool CanInstall => !_busy && _download is not null && !preview;
    public bool CanCancel => _busy && !_installing;

    private void Refresh()
    {
        foreach (string property in new[] { nameof(IsBusy), nameof(IsInstalling), nameof(CanCheck), nameof(CanDownload), nameof(CanInstall), nameof(CanCancel), nameof(Notes) }) Changed(property);
    }
    internal async Task CheckAsync()
    {
        if (_busy) return;
        if (preview) { Message = "Preview only · update checks are not sent to GitHub."; return; }
        _busy = true; Refresh();
        _operation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Message = "Checking GitHub for updates…";
        try
        {
            using var source = new GitHubUpdateSource(UpdateTrust.Production);
            bool beta = SemanticVersion.Parse(ReleaseInfo.Version).IsPrerelease;
            var result = await source.CheckAsync(ReleaseInfo.Version, beta, _operation.Token);
            if (result.Offer is not null)
            {
                if (_offer?.EnvelopeSha256 != result.Offer.EnvelopeSha256) _download = null;
                _offer = result.Offer;
            }
            else if (result.Status == UpdateCheckStatus.UpToDate) { _offer = null; _download = null; }
            Message = result.Message;
        }
        catch (Exception error) when (Expected(error)) { Message = "Could not check for updates. " + error.Message; }
        finally { _operation.Dispose(); _operation = null; _busy = false; Refresh(); }
    }
    internal async Task DownloadAsync()
    {
        if (!CanDownload || _offer is null) return;
        _busy = true; _operation = new CancellationTokenSource(TimeSpan.FromMinutes(12)); Refresh();
        Message = "Downloading and verifying the update… You can keep using your audio.";
        try
        {
            using var downloader = new UpdateDownloader();
            UpdateFiles.CreatePrivateDirectory(UpdateTransaction.Root);
            var kind = ReleaseInfo.Distribution == "Standalone" ? UpdateAssetKind.Standalone : UpdateAssetKind.Installer;
            _download = await downloader.DownloadAsync(_offer, kind, Path.Combine(UpdateTransaction.Root, "downloads"), _operation.Token);
            Message = $"VineMix {_offer.Version} is ready. Install and restart will briefly stop audio, then reopen your saved workspace.";
        }
        catch (OperationCanceledException) { Message = "Download cancelled or timed out. Your current version is still running."; }
        catch (Exception error) when (Expected(error)) { Message = "The update was not installed. " + error.Message; }
        finally { _operation.Dispose(); _operation = null; _busy = false; Refresh(); }
    }
    internal void SetInstalling(bool value, string? message = null)
    { _busy = _installing = value; if (message is not null) Message = message; Refresh(); }
    internal void Cancel() { if (!_installing) _operation?.Cancel(); }
    internal void Report(string message) => Message = message;
    private static bool Expected(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException
        or System.Net.Http.HttpRequestException or System.Security.Cryptography.CryptographicException or UpdateValidationException
        or System.Text.Json.JsonException or OperationCanceledException or System.Security.SecurityException
        or System.ComponentModel.Win32Exception;
    public void Dispose() { Cancel(); }
}
