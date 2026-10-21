using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VineMix.UpdateCore;

public sealed class UpdateTrust
{
    public const int MaximumEnvelopeBytes = 256 * 1024;
    public const int MaximumPayloadBytes = 128 * 1024;
    public const long MaximumAssetBytes = 1024L * 1024 * 1024;
    public const string Repository = "acoyeen/vine-mix";
    public const string Algorithm = "RSA-PSS-SHA256";
    private static readonly Lazy<UpdateTrust> BuiltIn = new(LoadProduction);
    private readonly IReadOnlyDictionary<string, string> _keys;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12
    };
    public static UpdateTrust Production => BuiltIn.Value;
    public UpdateTrust(IReadOnlyDictionary<string, string> publicKeys)
    {
        _keys = new Dictionary<string, string>(publicKeys, StringComparer.Ordinal);
        if (_keys.Count == 0 || _keys.Any(key => !ValidKeyId(key.Key))) throw new ArgumentException("Update trust needs named public keys.", nameof(publicKeys));
        foreach (var key in _keys.Values)
        {
            using var rsa = RSA.Create(); rsa.ImportFromPem(key);
            if (rsa.KeySize is < 3072 or > 8192) throw new ArgumentException("Update trust requires RSA keys between 3072 and 8192 bits.", nameof(publicKeys));
        }
    }
    public UpdateOffer Verify(byte[] envelopeBytes, string expectedVersion, UpdateCompatibility? compatibility = null)
    {
        compatibility ??= new();
        if (envelopeBytes.Length is 0 or > MaximumEnvelopeBytes) throw Invalid("The signed update information is too large or empty.");
        try
        {
            ValidateJson(envelopeBytes);
            var envelope = JsonSerializer.Deserialize<Envelope>(envelopeBytes, Json) ?? throw Invalid("Signed update information is missing.");
            if (envelope.FormatVersion != 1 || envelope.Algorithm != Algorithm || !ValidKeyId(envelope.KeyId)
                || !_keys.TryGetValue(envelope.KeyId, out var pem)) throw Invalid("The update signature uses an unknown key or format.");
            if (string.IsNullOrEmpty(envelope.Payload) || envelope.Payload.Length > (MaximumPayloadBytes + 2) / 3 * 4
                || string.IsNullOrEmpty(envelope.Signature) || envelope.Signature.Length > 1400) throw Invalid("The signed update payload is invalid.");
            byte[] payload = Convert.FromBase64String(envelope.Payload), signature = Convert.FromBase64String(envelope.Signature);
            if (payload.Length is 0 or > MaximumPayloadBytes) throw Invalid("The signed update payload is too large or empty.");
            using var rsa = RSA.Create(); rsa.ImportFromPem(pem);
            if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw Invalid("The update signature could not be verified.");
            ValidateJson(payload);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(payload, Json) ?? throw Invalid("The update manifest is missing.");
            ValidateManifest(manifest, expectedVersion, compatibility);
            return new UpdateOffer(manifest, envelope.KeyId, envelopeBytes);
        }
        catch (Exception error) when (error is JsonException or FormatException or CryptographicException or ArgumentException)
        { throw Invalid("The signed update information is malformed or could not be verified."); }
    }
    public static string AssetUrl(string version, string fileName) => $"https://github.com/{Repository}/releases/download/v{version}/{fileName}";
    public static string ManifestFileName(string version) => $"VineMix-{version}-Updates.json";
    private static void ValidateManifest(UpdateManifest manifest, string expectedVersion, UpdateCompatibility compatible)
    {
        if (!SemanticVersion.TryParse(manifest.Version, out var version) || manifest.Version != expectedVersion
            || manifest.FormatVersion != 1 || manifest.Product != "VineMix" || manifest.Architecture != "x64"
            || manifest.Notes is null || manifest.Notes.Length > 32768 || manifest.MinimumWindowsBuild < 22000)
            throw Invalid("The update manifest identity, version or format is invalid.");
        if (!Version.TryParse(manifest.NumericVersion, out var numeric) || numeric.Revision < 0 || numeric.ToString() != manifest.NumericVersion
            || new[] { numeric.Major, numeric.Minor, numeric.Build, numeric.Revision }.Any(part => part > 65535)
            || numeric.Major != version!.Major || numeric.Minor != version.Minor || numeric.Build != version.Patch)
            throw Invalid("The update's numeric version does not match its release.");
        ValidateAsset(manifest.Installer, manifest.Version, "Setup");
        ValidateAsset(manifest.Standalone, manifest.Version, "Standalone");
        if (manifest.SettingsSchema != compatible.SettingsSchema || manifest.ProtocolVersion != compatible.ProtocolVersion
            || manifest.MinimumWindowsBuild > compatible.WindowsBuild || compatible.Architecture != "x64")
            throw new UpdateValidationException("This release needs a different Windows, settings or engine protocol version.", UpdateValidationFailure.Incompatible);
    }
    private static void ValidateAsset(UpdateAsset? asset, string version, string kind)
    {
        string expectedName = $"VineMix-{version}-{kind}-x64.exe";
        if (asset is null || asset.FileName != expectedName || asset.Url != AssetUrl(version, expectedName)
            || asset.Size is <= 0 or > MaximumAssetBytes || asset.Sha256 is null
            || !Regex.IsMatch(asset.Sha256, "\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant))
            throw Invalid("The update asset name, origin, size or digest is invalid.");
    }
    private static bool ValidKeyId(string? keyId) => keyId is { Length: > 0 and <= 80 }
        && keyId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static UpdateValidationException Invalid(string message) => new(message);
    private static void ValidateJson(byte[] data)
    {
        using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 12 });
        Check(document.RootElement);
        static void Check(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                { if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property."); Check(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Check(item);
        }
    }
    private static UpdateTrust LoadProduction()
    {
        using var stream = typeof(UpdateTrust).Assembly.GetManifestResourceStream("VineMix.ReleaseTrust.json")
            ?? throw new InvalidOperationException("The update trust resource is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 1) throw new InvalidOperationException("Unknown update trust format.");
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in root.GetProperty("keys").EnumerateArray())
        {
            if (key.GetProperty("algorithm").GetString() != Algorithm) throw new InvalidOperationException("Unknown update trust algorithm.");
            keys.Add(key.GetProperty("keyId").GetString()!, key.GetProperty("publicKeyPem").GetString()!);
        }
        return new UpdateTrust(keys);
    }
    private sealed record Envelope
    {
        public required int FormatVersion { get; init; }
        public required string KeyId { get; init; }
        public required string Algorithm { get; init; }
        public required string Payload { get; init; }
        public required string Signature { get; init; }
    }
}
