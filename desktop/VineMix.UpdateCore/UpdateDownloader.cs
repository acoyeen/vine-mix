using System.Security.Cryptography;

namespace VineMix.UpdateCore;

public sealed class UpdateDownloader : IDisposable
{
    private readonly UpdateHttp _http;
    public UpdateDownloader(HttpMessageHandler? handler = null) => _http = new UpdateHttp(handler);
    public async Task<DownloadedUpdate> DownloadAsync(UpdateOffer offer, UpdateAssetKind kind, string stagingParent, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (kind is not UpdateAssetKind.Installer and not UpdateAssetKind.Standalone) throw new ArgumentOutOfRangeException(nameof(kind));
        UpdateAsset asset = kind == UpdateAssetKind.Installer ? offer.Manifest.Installer : offer.Manifest.Standalone;
        string parent = Path.GetFullPath(stagingParent);
        Directory.CreateDirectory(parent);
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("The update staging directory cannot be a link.");
        string staging = Path.Combine(parent, "vinemix-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string partial = Path.Combine(staging, asset.FileName + ".partial"), complete = Path.Combine(staging, asset.FileName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var response = await _http.GetAsync(new Uri(asset.Url), true, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"The update download returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
            if (response.Content.Headers.ContentLength is long declared && declared != asset.Size) throw new UpdateValidationException("The download length does not match its signed manifest.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[65536]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    total += count;
                    if (total > asset.Size || total > UpdateTrust.MaximumAssetBytes) throw new UpdateValidationException("The download exceeded its signed size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                }
                if (total != asset.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(asset.Sha256)))
                    throw new UpdateValidationException("The download was interrupted or its SHA-256 digest did not match.");
                await output.FlushAsync(timeout.Token).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(partial, complete);
            return new DownloadedUpdate(complete, staging, asset, offer);
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
    public void Dispose() => _http.Dispose();
}
