using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VineMix.UpdateCore;

internal static class Program
{
    private static readonly RSA Key = RSA.Create(3072);
    private static readonly UpdateTrust Trust = new(new Dictionary<string, string> { ["fixture-key"] = Key.ExportSubjectPublicKeyInfoPem() });
    private static readonly UpdateCompatibility Compatible = new() { WindowsBuild = 30000, Architecture = "x64" };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly byte[] Package = Encoding.UTF8.GetBytes("Synthetic update package. Never executable.");
    private static readonly List<string> Checks = [];

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Semver(); Signatures();
            await Sources(); await Downloads();
            Check(UpdateTrust.Production is not null, "Production embeds a loadable pinned RSA trust map.");
            Console.WriteLine($"PASS: {Checks.Count} isolated updater checks; no network, settings, audio or application processes used.");
            int report = Array.IndexOf(args, "--report");
            if (report >= 0 && report + 1 < args.Length)
                File.WriteAllText(args[report + 1], JsonSerializer.Serialize(new { passed = true, count = Checks.Count, checks = Checks }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); Console.Error.WriteLine(error.StackTrace); return 1; }
        finally { Key.Dispose(); }
    }
    private static void Semver()
    {
        string[] ordered = ["0.3.0-alpha", "0.3.0-alpha.1", "0.3.0-alpha.beta", "0.3.0-beta.2", "0.3.0-beta.10", "0.3.0-rc.1", "0.3.0", "0.3.1", "1.0.0"];
        Check(ordered.Zip(ordered.Skip(1)).All(pair => SemanticVersion.Parse(pair.First).CompareTo(SemanticVersion.Parse(pair.Second)) < 0), "Semantic precedence orders beta.2 before beta.10 and stable after prereleases.");
        Check(SemanticVersion.Parse("1.2.3+build.1").CompareTo(SemanticVersion.Parse("1.2.3+build.2")) == 0, "Build metadata does not change precedence.");
        foreach (string invalid in new[] { "v1.2.3", "01.2.3", "1.2", "1.2.3-beta.01", "1.2.3\n", "1.2.3/other", "1.2.3-", "1.2.3+", "1.2.3.4" })
            Check(!SemanticVersion.TryParse(invalid, out _), "Reject malformed semantic version: " + invalid.Replace("\n", "<newline>"));
    }
    private static void Signatures()
    {
        var manifest = Manifest("0.3.0-beta.2"); byte[] envelope = Sign(manifest);
        var offer = Trust.Verify(envelope, manifest.Version, Compatible);
        Check(offer.Version == manifest.Version && offer.KeyId == "fixture-key" && offer.Manifest.Installer == manifest.Installer, "An exact RSA-PSS-SHA256 payload produces an immutable verified offer.");
        byte[] copy = offer.EnvelopeBytes; copy[0] ^= 1;
        Check(offer.EnvelopeBytes.SequenceEqual(envelope), "Callers cannot mutate the verified envelope through its byte-array accessor.");
        var node = JsonNode.Parse(envelope)!.AsObject();
        node["keyId"] = "unknown"; Reject(Encoding.UTF8.GetBytes(node.ToJsonString()), manifest.Version, "Unknown signature key rejected.");
        node = JsonNode.Parse(envelope)!.AsObject(); node["algorithm"] = "RSA-SHA256"; Reject(Encoding.UTF8.GetBytes(node.ToJsonString()), manifest.Version, "Algorithm downgrade rejected.");
        node = JsonNode.Parse(envelope)!.AsObject();
        var signature = Convert.FromBase64String(node["signature"]!.GetValue<string>()); signature[0] ^= 1; node["signature"] = Convert.ToBase64String(signature);
        Reject(Encoding.UTF8.GetBytes(node.ToJsonString()), manifest.Version, "Invalid signature rejected.");
        node = JsonNode.Parse(envelope)!.AsObject(); node["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
        Reject(Encoding.UTF8.GetBytes(node.ToJsonString()), manifest.Version, "Payload tampering rejected before interpretation.");
        Reject(envelope, "0.3.0-beta.3", "Mixed release and manifest versions rejected.");
        Reject(Sign(manifest with { SettingsSchema = 99 }), manifest.Version, "Future settings schema rejected.", UpdateValidationFailure.Incompatible);
        Reject(Sign(manifest with { ProtocolVersion = 99 }), manifest.Version, "Future engine protocol rejected.", UpdateValidationFailure.Incompatible);
        Reject(Sign(manifest with { MinimumWindowsBuild = 40000 }), manifest.Version, "Unsupported Windows build rejected.", UpdateValidationFailure.Incompatible);
        Reject(Sign(manifest with { Architecture = "arm64" }), manifest.Version, "Unexpected architecture rejected.");
        Reject(Sign(manifest with { NumericVersion = "0.4.0.2" }), manifest.Version, "Unrelated numeric version rejected.");
        Reject(Sign(manifest with { NumericVersion = "0.3.0.65536" }), manifest.Version, "Overflowing Windows version rejected.");
        Reject(Sign(manifest with { Installer = manifest.Installer with { Url = UpdateTrust.AssetUrl("0.3.0-beta.3", manifest.Installer.FileName) } }), manifest.Version, "Mixed-version download URL rejected.");
        Reject(Sign(manifest with { Installer = manifest.Installer with { FileName = "../package.exe" } }), manifest.Version, "Package traversal name rejected.");
        Reject(Sign(manifest with { Installer = manifest.Installer with { Size = UpdateTrust.MaximumAssetBytes + 1 } }), manifest.Version, "Oversized signed package rejected.");
        Reject(Sign(manifest with { Installer = manifest.Installer with { Sha256 = new string('z', 64) } }), manifest.Version, "Malformed signed digest rejected.");
        var payload = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(manifest, Json))!.AsObject(); payload.Remove("settingsSchema");
        Reject(SignBytes(Encoding.UTF8.GetBytes(payload.ToJsonString())), manifest.Version, "Missing compatibility property rejected instead of defaulted.");
        payload = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(manifest, Json))!.AsObject(); payload["unexpected"] = true;
        Reject(SignBytes(Encoding.UTF8.GetBytes(payload.ToJsonString())), manifest.Version, "Unknown signed manifest fields rejected instead of silently interpreted differently.");
        string duplicate = JsonSerializer.Serialize(manifest, Json).Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1");
        Reject(SignBytes(Encoding.UTF8.GetBytes(duplicate)), manifest.Version, "Duplicate manifest properties rejected.");
        duplicate = Encoding.UTF8.GetString(envelope).Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1");
        Reject(Encoding.UTF8.GetBytes(duplicate), manifest.Version, "Duplicate envelope properties rejected.");
        Reject(SignBytes(new byte[UpdateTrust.MaximumPayloadBytes + 1]), manifest.Version, "Oversized decoded payload rejected before signature processing.");
        Reject(new byte[UpdateTrust.MaximumEnvelopeBytes + 1], manifest.Version, "Oversized envelope rejected.");
        using var weak = RSA.Create(2048); bool weakRejected = false;
        try { _ = new UpdateTrust(new Dictionary<string, string> { ["weak"] = weak.ExportSubjectPublicKeyInfoPem() }); }
        catch (ArgumentException) { weakRejected = true; }
        Check(weakRejected, "Pinned keys below RSA3072 are rejected.");
        using var rotated = RSA.Create(3072);
        var rotatedTrust = new UpdateTrust(new Dictionary<string, string> { ["fixture-key"] = Key.ExportSubjectPublicKeyInfoPem(), ["rotation-key"] = rotated.ExportSubjectPublicKeyInfoPem() });
        Check(rotatedTrust.Verify(SignBytes(JsonSerializer.SerializeToUtf8Bytes(manifest, Json), rotated, "rotation-key"), manifest.Version, Compatible).KeyId == "rotation-key", "A second explicitly pinned key supports planned signing-key rotation.");
    }
    private static async Task Sources()
    {
        foreach (var entry in new[] { (404, UpdateCheckStatus.Unavailable), (500, UpdateCheckStatus.Unavailable), (429, UpdateCheckStatus.RateLimited) })
        {
            using var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) => Response(entry.Item1, [])));
            var result = await source.CheckAsync("0.3.0-beta.1", true, default);
            Check(result.Status == entry.Item2 && result.Status != UpdateCheckStatus.UpToDate, $"HTTP {entry.Item1} has an explicit non-up-to-date status.");
        }
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) => throw new HttpRequestException("fixture offline"))))
            Check((await source.CheckAsync("0.3.0", false, default)).Status == UpdateCheckStatus.Unavailable, "Offline is unavailable, never up to date.");
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) => throw new TaskCanceledException("fixture timeout"))))
            Check((await source.CheckAsync("0.3.0", false, default)).Status == UpdateCheckStatus.Unavailable, "Transport timeout is unavailable rather than user cancellation or up to date.");
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) => Response(200, Encoding.UTF8.GetBytes("{}")))))
            Check((await source.CheckAsync("0.3.0", false, default)).Status == UpdateCheckStatus.InvalidRelease, "Malformed release response rejected.");
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) => Response(200, new byte[2 * 1024 * 1024 + 1]))))
            Check((await source.CheckAsync("0.3.0", false, default)).Status == UpdateCheckStatus.InvalidRelease, "Oversized release response rejected before parsing.");
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) => Response(200, Encoding.UTF8.GetBytes("[]")))))
            Check((await source.CheckAsync("0.3.0", false, default)).Status == UpdateCheckStatus.Unavailable, "An empty public release feed reports no releases yet.");
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) => Redirect("https://evil.example/releases"))))
            Check((await source.CheckAsync("0.3.0", false, default)).Status == UpdateCheckStatus.InvalidRelease, "The API cannot redirect into a different repository or origin.");
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) =>
        { var response = Response(403, []); response.Headers.Add("X-RateLimit-Remaining", "0"); return response; })))
            Check((await source.CheckAsync("0.3.0", false, default)).Status == UpdateCheckStatus.RateLimited, "GitHub 403 rate-limit evidence is distinguished from generic unavailability.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            using var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, token) => { token.ThrowIfCancellationRequested(); return Response(200, []); }));
            Check((await source.CheckAsync("0.3.0", false, cancelled.Token)).Status == UpdateCheckStatus.Cancelled, "Manual check cancellation is explicit.");
        }
        var beta2 = Manifest("0.3.0-beta.2"); var beta10 = Manifest("0.3.0-beta.10"); var stable = Manifest("0.3.0");
        var beta3 = Manifest("0.3.0-beta.3") with { NumericVersion = "0.3.0.3" };
        var beta4 = Manifest("0.3.0-beta.4") with { NumericVersion = "0.3.0.5" };
        using (var source = Source(beta2, beta4))
        {
            var result = await source.CheckAsync(beta2.Version, true, default);
            Check(result.Status == UpdateCheckStatus.Available && result.Offer?.Version == beta4.Version
                && result.Offer.Manifest.NumericVersion == beta4.NumericVersion,
                "Beta 2 can discover signed Beta 4 directly without Beta 3; its Windows file revision need not equal the beta number.");
        }
        using (var source = Source(beta3, beta4, beta2))
            Check((await source.CheckAsync(beta2.Version, true, default)).Offer?.Version == beta4.Version,
                "Beta 2 selects Beta 4 when Beta 3 is also published, without requiring an intermediate installation.");
        using (var source = Source(beta2, beta3, beta4))
            Check((await source.CheckAsync(beta4.Version, true, default)).Status == UpdateCheckStatus.UpToDate,
                "After updating to Beta 4, older Beta 2 and Beta 3 releases do not offer a downgrade or reinstall.");
        using (var source = Source(beta2, beta10))
            Check((await source.CheckAsync("0.3.0-beta.1", true, default)).Offer?.Version == beta10.Version, "Unsorted GitHub releases select the highest verified compatible semver.");
        using (var source = Source(beta10, stable))
            Check((await source.CheckAsync("0.3.0-beta.1", false, default)).Offer?.Version == stable.Version, "The stable channel excludes prereleases and offers a stable release.");
        using (var source = Source(beta10, stable))
            Check((await source.CheckAsync("0.3.0", true, default)).Status == UpdateCheckStatus.UpToDate, "Equal stable and older beta releases do not offer a downgrade.");
        using (var source = Source(beta2, beta10 with { SettingsSchema = 99 }))
            Check((await source.CheckAsync("0.3.0-beta.1", true, default)).Offer?.Version == beta2.Version, "An incompatible newest release does not hide the highest compatible signed update.");
        using (var source = Source(beta10 with { MinimumWindowsBuild = 40000 }))
            Check((await source.CheckAsync("0.3.0-beta.1", true, default)).Status == UpdateCheckStatus.Incompatible, "Only incompatible newer releases report incompatibility.");
        byte[] validEnvelope = Sign(beta2);
        var releaseNode = JsonNode.Parse(Releases((beta2, validEnvelope)))!.AsArray(); releaseNode[0]!["draft"] = true;
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((_, _) => Response(200, Encoding.UTF8.GetBytes(releaseNode.ToJsonString())))))
            Check((await source.CheckAsync("0.3.0-beta.1", true, default)).Status == UpdateCheckStatus.Unavailable, "Draft releases are excluded even when returned by the API.");
        releaseNode = JsonNode.Parse(Releases((beta2, validEnvelope)))!.AsArray();
        releaseNode[0]!["assets"]![1]!["browser_download_url"] = "https://github.com/other/repository/releases/download/v0.3.0-beta.2/package.exe";
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((request, _) => request.RequestUri!.Host == "api.github.com"
            ? Response(200, Encoding.UTF8.GetBytes(releaseNode.ToJsonString())) : Response(200, validEnvelope))))
            Check((await source.CheckAsync("0.3.0-beta.1", true, default)).Status == UpdateCheckStatus.InvalidRelease, "A valid signed manifest cannot be paired with packages from another published release origin.");
        byte[] tampered = Sign(beta2); tampered[^3] ^= 1;
        using (var source = new GitHubUpdateSource(Trust, Compatible, Handler((request, _) => request.RequestUri!.Host == "api.github.com"
            ? Response(200, Releases((beta2, tampered))) : Response(200, tampered))))
            Check((await source.CheckAsync("0.3.0-beta.1", true, default)).Status == UpdateCheckStatus.InvalidRelease, "A malformed published signed manifest is not offered.");
    }
    private static async Task Downloads()
    {
        var manifest = Manifest("0.3.0-beta.2"); var offer = Trust.Verify(Sign(manifest), manifest.Version, Compatible);
        string parent = Path.Combine(Path.GetTempPath(), "VineMix.UpdateCore.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try
        {
            using (var downloader = new UpdateDownloader(Handler((_, _) => Response(200, Package))))
            {
                var downloaded = await downloader.DownloadAsync(offer, UpdateAssetKind.Standalone, parent, default);
                Check(File.ReadAllBytes(downloaded.FilePath).SequenceEqual(Package) && !File.Exists(downloaded.FilePath + ".partial"), "Only exact-length SHA-256-verified bytes receive the final staged package name.");
                DeleteFixtureDirectory(downloaded.StagingDirectory, parent);
            }
            await FailedDownload(offer, parent, Handler((_, _) => Response(200, new byte[Package.Length])), "Same-length tampering rejects and removes partial staging.");
            await FailedDownload(offer, parent, Handler((_, _) => Response(200, Package[..^1])), "Interrupted/short download rejects and removes partial staging.");
            await FailedDownload(offer, parent, Handler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new MemoryStream(new byte[Package.Length + 1])) }), "Streaming overrun without a declared length rejects and cleans staging.");
            await FailedDownload(offer, parent, Handler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new InterruptedStream()) }), "Mid-stream IO failure cleans partial staging.");
            foreach (string target in new[] { "http://release-assets.githubusercontent.com/file", "https://evil.example/file", "https://github.com.evil.example/file", "https://github.com/other/repo/file", "https://user@release-assets.githubusercontent.com/file", "https://release-assets.githubusercontent.com:8443/file" })
                await FailedDownload(offer, parent, Handler((_, _) => Redirect(target)), "Reject untrusted redirect: " + target);
            int requests = 0;
            using (var downloader = new UpdateDownloader(Handler((request, _) => ++requests == 1 ? Redirect("https://release-assets.githubusercontent.com/github-production-release-asset/file?token=synthetic") : Response(200, Package))))
            {
                var downloaded = await downloader.DownloadAsync(offer, UpdateAssetKind.Installer, parent, default);
                Check(requests == 2 && File.Exists(downloaded.FilePath), "A bounded HTTPS GitHub release-asset CDN redirect is accepted.");
                DeleteFixtureDirectory(downloaded.StagingDirectory, parent);
            }
            await FailedDownload(offer, parent, Handler((_, _) => Redirect("https://release-assets.githubusercontent.com/loop")), "Redirect loops are bounded and clean staging.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await FailedDownload(offer, parent, Handler((_, token) => { token.ThrowIfCancellationRequested(); return Response(200, Package); }), "Cancelled download removes its isolated staging directory.", cancelled.Token);
            using var midstream = new CancellationTokenSource();
            await FailedDownload(offer, parent, Handler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new CancelledStream(midstream)) }), "Mid-stream cancellation closes and removes a written partial package.", midstream.Token);
        }
        finally { DeleteFixtureDirectory(parent, Path.GetTempPath()); }
    }
    private static async Task FailedDownload(UpdateOffer offer, string parent, HttpMessageHandler handler, string name, CancellationToken token = default)
    {
        using var downloader = new UpdateDownloader(handler);
        bool failed = false;
        try { await downloader.DownloadAsync(offer, UpdateAssetKind.Standalone, parent, token); }
        catch (Exception error) when (error is UpdateValidationException or IOException or HttpRequestException or OperationCanceledException) { failed = true; }
        Check(failed && Directory.GetDirectories(parent).Length == 0, name);
    }
    private static GitHubUpdateSource Source(params UpdateManifest[] manifests)
    {
        var signed = manifests.Select(manifest => (manifest, Sign(manifest))).ToArray();
        return new GitHubUpdateSource(Trust, Compatible, Handler((request, _) => request.RequestUri!.Host == "api.github.com"
            ? Response(200, Releases(signed)) : Response(200, signed.Single(item => request.RequestUri.AbsoluteUri == UpdateTrust.AssetUrl(item.manifest.Version, UpdateTrust.ManifestFileName(item.manifest.Version))).Item2)));
    }
    private static byte[] Releases(params (UpdateManifest Manifest, byte[] Envelope)[] releases) => JsonSerializer.SerializeToUtf8Bytes(releases.Select(release => new
    {
        tag_name = "v" + release.Manifest.Version, draft = false, prerelease = SemanticVersion.Parse(release.Manifest.Version).IsPrerelease, published_at = "2026-01-01T00:00:00Z",
        assets = new[] {
            new { name = UpdateTrust.ManifestFileName(release.Manifest.Version), size = release.Envelope.LongLength, browser_download_url = UpdateTrust.AssetUrl(release.Manifest.Version, UpdateTrust.ManifestFileName(release.Manifest.Version)), state = "uploaded" },
            new { name = release.Manifest.Installer.FileName, size = release.Manifest.Installer.Size, browser_download_url = release.Manifest.Installer.Url, state = "uploaded" },
            new { name = release.Manifest.Standalone.FileName, size = release.Manifest.Standalone.Size, browser_download_url = release.Manifest.Standalone.Url, state = "uploaded" }
        }
    }));
    private static UpdateManifest Manifest(string version) => new()
    {
        Version = version, NumericVersion = "0.3.0.2", Notes = "Synthetic release notes.",
        Installer = Asset(version, "Setup"), Standalone = Asset(version, "Standalone")
    };
    private static UpdateAsset Asset(string version, string kind)
    {
        string name = $"VineMix-{version}-{kind}-x64.exe";
        return new() { FileName = name, Url = UpdateTrust.AssetUrl(version, name), Size = Package.Length, Sha256 = Convert.ToHexString(SHA256.HashData(Package)).ToLowerInvariant() };
    }
    private static byte[] Sign(UpdateManifest manifest) => SignBytes(JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
    private static byte[] SignBytes(byte[] payload, RSA? signingKey = null, string keyId = "fixture-key") => JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, keyId, algorithm = UpdateTrust.Algorithm,
        payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String((signingKey ?? Key).SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) });
    private static void Reject(byte[] envelope, string version, string name, UpdateValidationFailure expected = UpdateValidationFailure.Invalid)
    {
        bool rejected = false;
        try { Trust.Verify(envelope, version, Compatible); }
        catch (UpdateValidationException error) { rejected = error.Failure == expected; }
        Check(rejected, name);
    }
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("FAIL: " + name); Checks.Add(name); }
    private static HttpResponseMessage Response(int status, byte[] bytes) => new((HttpStatusCode)status) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string target) { var response = Response(302, []); response.Headers.Location = new Uri(target); return response; }
    private static HttpMessageHandler Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) => new FixtureHandler(send);
    private static void DeleteFixtureDirectory(string path, string parent)
    {
        string full = Path.GetFullPath(path), fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(full), fullParent, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture cleanup escaped its named temporary parent.");
        Directory.Delete(full, recursive: true);
    }
    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization is not null || request.Headers.Contains("Cookie")) throw new InvalidOperationException("Updater sent credentials.");
            return Task.FromResult(send(request, cancellationToken));
        }
    }
    private sealed class UnknownLengthContent(Stream stream) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => stream.CopyToAsync(target);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(stream);
        protected override void Dispose(bool disposing) { if (disposing) stream.Dispose(); base.Dispose(disposing); }
    }
    private sealed class InterruptedStream : MemoryStream
    {
        private bool _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_read) throw new IOException("Synthetic interrupted stream.");
            _read = true; buffer.Span[0] = 1; return ValueTask.FromResult(1);
        }
    }
    private sealed class CancelledStream(CancellationTokenSource cancellation) : MemoryStream
    {
        private bool _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_read) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            _read = true; buffer.Span[0] = 1; return ValueTask.FromResult(1);
        }
    }
}
