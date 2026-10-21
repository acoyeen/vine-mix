using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace VineMix.UpdateCore;

public sealed record UpdateAsset
{
    public required string FileName { get; init; }
    public required string Url { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record UpdateManifest
{
    [JsonRequired] public int FormatVersion { get; init; } = 1;
    [JsonRequired] public string Product { get; init; } = "VineMix";
    public required string Version { get; init; }
    public required string NumericVersion { get; init; }
    [JsonRequired] public string Architecture { get; init; } = "x64";
    [JsonRequired] public int MinimumWindowsBuild { get; init; } = 22000;
    [JsonRequired] public int SettingsSchema { get; init; } = 2;
    [JsonRequired] public int ProtocolVersion { get; init; } = 1;
    [JsonRequired] public string Notes { get; init; } = "";
    public required UpdateAsset Installer { get; init; }
    public required UpdateAsset Standalone { get; init; }
}

public sealed record UpdateCompatibility
{
    public int WindowsBuild { get; init; } = Environment.OSVersion.Version.Build;
    public string Architecture { get; init; } = RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64 ? "x64" : "unsupported";
    public int SettingsSchema { get; init; } = 2;
    public int ProtocolVersion { get; init; } = 1;
}

public enum UpdateValidationFailure { Invalid, Incompatible }
public sealed class UpdateValidationException(string message, UpdateValidationFailure failure = UpdateValidationFailure.Invalid) : Exception(message)
{
    public UpdateValidationFailure Failure { get; } = failure;
}

public sealed class UpdateOffer
{
    private readonly byte[] _envelope;
    internal UpdateOffer(UpdateManifest manifest, string keyId, byte[] envelope)
    {
        Manifest = manifest; KeyId = keyId; _envelope = (byte[])envelope.Clone();
        EnvelopeSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_envelope)).ToLowerInvariant();
    }
    public UpdateManifest Manifest { get; }
    public string Version => Manifest.Version;
    public string KeyId { get; }
    public string EnvelopeSha256 { get; }
    public byte[] EnvelopeBytes => (byte[])_envelope.Clone();
}

public enum UpdateCheckStatus { Available, UpToDate, Unavailable, RateLimited, InvalidRelease, Incompatible, Cancelled }
public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, UpdateOffer? Offer = null);
public enum UpdateAssetKind { Installer, Standalone }
public sealed record DownloadedUpdate(string FilePath, string StagingDirectory, UpdateAsset Asset, UpdateOffer Offer);
