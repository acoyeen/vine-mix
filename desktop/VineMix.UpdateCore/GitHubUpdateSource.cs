using System.Net;
using System.Text.Json;

namespace VineMix.UpdateCore;

public sealed class GitHubUpdateSource : IDisposable
{
    private readonly UpdateTrust _trust;
    private readonly UpdateCompatibility _compatibility;
    private readonly UpdateHttp _http;
    public GitHubUpdateSource(UpdateTrust trust, UpdateCompatibility? compatibility = null, HttpMessageHandler? handler = null)
    { _trust = trust; _compatibility = compatibility ?? new(); _http = new UpdateHttp(handler); }
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, bool includePrereleases, CancellationToken token)
    {
        if (!SemanticVersion.TryParse(currentVersion, out var current)) return new(UpdateCheckStatus.Unavailable, "The installed version could not be compared with published releases.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var releases = new List<Release>();
            for (int page = 1; page <= 4; page++)
            {
                using var response = await _http.GetAsync(new Uri($"https://api.github.com/repos/{UpdateTrust.Repository}/releases?per_page=100&page={page}"), false, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return Failure(response);
                byte[] bytes = await UpdateHttp.ReadBoundedAsync(response.Content, 2 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 100)
                    return new(UpdateCheckStatus.InvalidRelease, "The release service returned invalid release information.");
                int count = document.RootElement.GetArrayLength();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.GetProperty("draft").GetBoolean() || item.GetProperty("published_at").ValueKind == JsonValueKind.Null) continue;
                    string? tag = item.GetProperty("tag_name").GetString();
                    if (tag is null || !tag.StartsWith('v') || !SemanticVersion.TryParse(tag[1..], out var version)) continue;
                    bool prerelease = item.GetProperty("prerelease").GetBoolean();
                    if (prerelease != version!.IsPrerelease || !includePrereleases && prerelease) continue;
                    var assets = item.GetProperty("assets");
                    if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > 200) throw new JsonException("Invalid release assets.");
                    releases.Add(new Release(version, assets.Clone()));
                }
                if (count < 100) break;
                if (page == 4) return new(UpdateCheckStatus.Unavailable, "The release history is larger than this updater can safely inspect. Check the releases page.");
            }
            if (releases.Count == 0) return new(UpdateCheckStatus.Unavailable, "No published releases are available on this update channel yet.");
            var newer = releases.Where(release => release.Version.CompareTo(current) > 0).OrderByDescending(release => release.Version).ToArray();
            if (newer.Length == 0) return new(UpdateCheckStatus.UpToDate, "This version is up to date on the selected release channel.");
            UpdateCheckResult? rejected = null;
            foreach (var release in newer)
            {
                string version = release.Version.ToString(), manifestName = UpdateTrust.ManifestFileName(version);
                try
                {
                    var manifests = release.Assets.EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == manifestName).ToArray();
                    if (manifests.Length != 1) throw new UpdateValidationException("A newer release does not contain exactly one signed update manifest.");
                    string expectedUrl = UpdateTrust.AssetUrl(version, manifestName);
                    long size = manifests[0].GetProperty("size").GetInt64();
                    if (size is <= 0 or > UpdateTrust.MaximumEnvelopeBytes || manifests[0].GetProperty("browser_download_url").GetString() != expectedUrl
                        || manifests[0].GetProperty("state").GetString() != "uploaded") throw new UpdateValidationException("The published update manifest has an invalid origin, state or size.");
                    using var response = await _http.GetAsync(new Uri(expectedUrl), true, timeout.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return Failure(response);
                    byte[] bytes = await UpdateHttp.ReadBoundedAsync(response.Content, UpdateTrust.MaximumEnvelopeBytes, timeout.Token).ConfigureAwait(false);
                    if (bytes.LongLength != size) throw new UpdateValidationException("The signed update manifest does not match the published asset length.");
                    var offer = _trust.Verify(bytes, version, _compatibility);
                    ValidatePublishedAsset(release.Assets, offer.Manifest.Installer);
                    ValidatePublishedAsset(release.Assets, offer.Manifest.Standalone);
                    return new(UpdateCheckStatus.Available, $"VineMix {version} is available.", offer);
                }
                catch (UpdateValidationException error)
                {
                    var status = error.Failure == UpdateValidationFailure.Incompatible ? UpdateCheckStatus.Incompatible : UpdateCheckStatus.InvalidRelease;
                    rejected ??= new(status, error.Message);
                }
            }
            return rejected ?? new(UpdateCheckStatus.Unavailable, "No compatible signed update is available.");
        }
        catch (OperationCanceledException) { return token.IsCancellationRequested ? new(UpdateCheckStatus.Cancelled, "Update check cancelled.") : new(UpdateCheckStatus.Unavailable, "The update service did not respond in time."); }
        catch (HttpRequestException) { return new(UpdateCheckStatus.Unavailable, "The update service could not be reached. Check your internet connection and try again."); }
        catch (IOException) { return new(UpdateCheckStatus.Unavailable, "The update response was interrupted. Try again."); }
        catch (UpdateValidationException error) { return new(UpdateCheckStatus.InvalidRelease, error.Message); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return new(UpdateCheckStatus.InvalidRelease, "The release service returned malformed update information."); }
    }
    private static void ValidatePublishedAsset(JsonElement assets, UpdateAsset expected)
    {
        var matching = assets.EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == expected.FileName).ToArray();
        if (matching.Length != 1 || matching[0].GetProperty("browser_download_url").GetString() != expected.Url
            || matching[0].GetProperty("size").GetInt64() != expected.Size || matching[0].GetProperty("state").GetString() != "uploaded")
            throw new UpdateValidationException("The signed update does not match the assets in its published release.");
    }
    private static UpdateCheckResult Failure(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) return new(UpdateCheckStatus.Unavailable, "Public update releases are not available yet, or the repository is private.");
        if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.Forbidden &&
            (response.Headers.RetryAfter is not null || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0")))
            return new(UpdateCheckStatus.RateLimited, "GitHub's update request limit was reached. Try again later.");
        return new(UpdateCheckStatus.Unavailable, $"The update service returned HTTP {(int)response.StatusCode}. Try again later.");
    }
    public void Dispose() => _http.Dispose();
    private sealed record Release(SemanticVersion Version, JsonElement Assets);
}
