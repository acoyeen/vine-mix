using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VineMix.UpdateCore;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" })) { SelfTest(); return 0; }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i += 2)
                if (i + 1 >= args.Length || !options.TryAdd(args[i], args[i + 1])) throw new InvalidOperationException("Invalid preparation arguments.");
            string Get(string key) => options.TryGetValue("--" + key, out string? value) ? value : throw new InvalidOperationException("A required preparation argument is missing.");
            Prepare(Get("project"), Get("release"), Get("trust"), Get("setup"), Get("setup-receipt"), Get("standalone"),
                Get("standalone-receipt"), Get("private-key"), Get("notes"), Get("output"), Get("source-hash"), Get("git-head"), Get("git-dirty"));
            Console.WriteLine("Signed update information prepared and verified locally. Nothing was published."); return 0;
        }
        catch (InvalidOperationException error) { Console.Error.WriteLine(error.Message); return 1; }
        catch { Console.Error.WriteLine("Release preparation failed while validating files, metadata or signing material. No release was published."); return 1; }
    }
    private static void Prepare(string project, string releasePath, string trustPath, string setupPath, string setupReceiptPath,
        string standalonePath, string standaloneReceiptPath, string keyPath, string notesPath, string outputPath, string sourceHash, string gitHead, string gitDirty)
    {
        project = Path.GetFullPath(project); outputPath = Path.GetFullPath(outputPath); keyPath = Path.GetFullPath(keyPath);
        if (Inside(keyPath, project) || Inside(keyPath, outputPath)) throw new InvalidOperationException("The private signing key must stay outside the source checkout and release output.");
        if (Directory.Exists(outputPath) || File.Exists(outputPath)) throw new InvalidOperationException("Choose a new release output directory; existing candidates are retained.");
        if (!IsHash(sourceHash) || gitHead.Length is < 7 or > 64 || !gitHead.All(Uri.IsHexDigit) || gitDirty is not ("true" or "false"))
            throw new InvalidOperationException("The current source identity is invalid.");
        using var release = Document(releasePath, "Release metadata", 32768);
        string version = Text(release.RootElement, "version"), numeric = Text(release.RootElement, "numericVersion");
        _ = SemanticVersion.Parse(version);
        using var setupReceipt = Document(setupReceiptPath, "Setup receipt", 8 * 1024 * 1024);
        using var standaloneReceipt = Document(standaloneReceiptPath, "Standalone receipt", 8 * 1024 * 1024);
        ValidateReceipt(setupReceipt.RootElement, version, numeric, sourceHash, gitHead);
        ValidateReceipt(standaloneReceipt.RootElement, version, numeric, sourceHash, gitHead);
        if (Text(setupReceipt.RootElement, "product") != "VineMix" || Text(setupReceipt.RootElement, "architecture") != "x64"
            || Text(standaloneReceipt.RootElement, "buildMode") != "Standalone") throw new InvalidOperationException("The package receipts do not identify the required Setup and standalone distributions.");
        var setup = Asset(setupPath, version, "Setup", numeric);
        var standalone = Asset(standalonePath, version, "Standalone", numeric);
        if (setupReceipt.RootElement.GetProperty("size").GetInt64() != setup.Size || !HashEquals(Text(setupReceipt.RootElement, "sha256"), setup.Sha256))
            throw new InvalidOperationException("Setup bytes do not match their build receipt.");
        var standaloneFiles = standaloneReceipt.RootElement.GetProperty("files");
        if (standaloneFiles.ValueKind != JsonValueKind.Array || standaloneFiles.GetArrayLength() != 1) throw new InvalidOperationException("The standalone receipt must contain exactly one executable.");
        var entry = standaloneFiles[0];
        if (Text(entry, "path") != standalone.FileName || entry.GetProperty("size").GetInt64() != standalone.Size || !HashEquals(Text(entry, "sha256"), standalone.Sha256))
            throw new InvalidOperationException("Standalone bytes do not match their build receipt.");
        byte[] notesBytes = Read(notesPath, "Release notes", 65536);
        string notes = new UTF8Encoding(false, true).GetString(notesBytes);
        if (notes.Length > 32768) throw new InvalidOperationException("Release notes exceed the updater's text limit.");
        if (notes.Contains(project, StringComparison.OrdinalIgnoreCase) || notes.Contains(keyPath, StringComparison.OrdinalIgnoreCase)
            || notes.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new InvalidOperationException("Release notes contain local source/signing information or private key markers.");
        var manifest = new UpdateManifest { Version = version, NumericVersion = numeric, Notes = notes, Installer = setup, Standalone = standalone };
        var keys = ReadTrust(trustPath);
        using var privateKey = RSA.Create();
        RequirePrivateKeyAcl(keyPath);
        byte[] keyBytes = Read(keyPath, "Private signing key", 32768);
        try { privateKey.ImportFromPem(Encoding.UTF8.GetString(keyBytes)); }
        finally { CryptographicOperations.ZeroMemory(keyBytes); }
        if (privateKey.KeySize is < 3072 or > 8192) throw new InvalidOperationException("Signing requires an RSA private key of at least 3072 bits.");
        var matches = keys.Where(key => SamePublicKey(privateKey, key.Value)).Select(key => key.Key).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("The private signing key does not match exactly one pinned public key.");
        byte[] envelope = Sign(manifest, privateKey, matches[0]);
        var verified = new UpdateTrust(keys).Verify(envelope, version, new() { Architecture = "x64", WindowsBuild = int.MaxValue });
        // Freeze only the verified package bytes; receipts are never copied into
        // public output because build receipts may contain local diagnostic paths.
        NoReparse(outputPath); Directory.CreateDirectory(outputPath);
        string setupCopy = Path.Combine(outputPath, setup.FileName), standaloneCopy = Path.Combine(outputPath, standalone.FileName);
        CopyVerified(setupPath, setupCopy, setup); CopyVerified(standalonePath, standaloneCopy, standalone);
        string envelopeName = UpdateTrust.ManifestFileName(version);
        File.WriteAllBytes(Path.Combine(outputPath, envelopeName), envelope);
        var receipt = new
        {
            formatVersion = 1, product = "VineMix", version, numericVersion = numeric, preparedUtc = DateTimeOffset.UtcNow,
            repository = UpdateTrust.Repository, tag = "v" + version, published = false, publicationAttempted = false,
            keyId = verified.KeyId, algorithm = UpdateTrust.Algorithm, envelopeFileName = envelopeName, envelopeSha256 = verified.EnvelopeSha256,
            sourceIdentity = new { sourceSnapshotSha256 = sourceHash, gitHead, gitDirty = gitDirty == "true" },
            artifacts = new[] { setup, standalone }, authenticodeVerified = false,
            scope = "Local signed-manifest preparation only. No GitHub upload, release publication, installer execution or application activation."
        };
        File.WriteAllBytes(Path.Combine(outputPath, "preparation-receipt.json"), JsonSerializer.SerializeToUtf8Bytes(receipt, Json));
    }
    private static void ValidateReceipt(JsonElement receipt, string version, string numeric, string sourceHash, string gitHead)
    {
        if (Text(receipt, "version") != version || Text(receipt, "numericVersion") != numeric
            || !HashEquals(Text(receipt.GetProperty("sourceIdentity"), "sourceSnapshotSha256"), sourceHash)
            || Text(receipt.GetProperty("sourceIdentity"), "gitHead") != gitHead
            || receipt.GetProperty("published").GetBoolean()) throw new InvalidOperationException("Package receipts must match the current unpublished release version and source snapshot.");
    }
    private static UpdateAsset Asset(string path, string version, string kind, string numeric)
    {
        path = Path.GetFullPath(path); NoReparse(path);
        string name = $"VineMix-{version}-{kind}-x64.exe";
        if (Path.GetFileName(path) != name) throw new InvalidOperationException("A package filename does not match the authoritative release version and architecture.");
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > UpdateTrust.MaximumAssetBytes) throw new InvalidOperationException("A package exceeds its supported size bounds.");
        using (var image = File.OpenRead(path))
        using (var executable = new PEReader(image))
            if (executable.PEHeaders.CoffHeader.Machine != Machine.Amd64 || executable.PEHeaders.PEHeader is null)
                throw new InvalidOperationException("Update packages must be x64 Windows executables.");
        var metadata = FileVersionInfo.GetVersionInfo(path);
        if (!MetadataMatches(metadata.ProductName, metadata.ProductVersion, metadata.FileVersion, version, numeric))
            throw new InvalidOperationException("An executable's embedded product/version does not match the release metadata.");
        return new() { FileName = name, Url = UpdateTrust.AssetUrl(version, name), Size = info.Length, Sha256 = Hash(path) };
    }
    private static Dictionary<string, string> ReadTrust(string path)
    {
        using var document = Document(path, "Public update trust", 128 * 1024);
        if (document.RootElement.GetProperty("formatVersion").GetInt32() != 1) throw new InvalidOperationException("The public trust format is unsupported.");
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.GetProperty("keys").EnumerateArray())
        {
            string pem = Text(item, "publicKeyPem");
            if (Text(item, "algorithm") != UpdateTrust.Algorithm || pem.Contains("PRIVATE KEY", StringComparison.Ordinal)
                || !keys.TryAdd(Text(item, "keyId"), pem)) throw new InvalidOperationException("The public trust map is invalid or contains private signing material.");
        }
        _ = new UpdateTrust(keys); return keys;
    }
    private static byte[] Sign(UpdateManifest manifest, RSA key, string keyId)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        return JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, keyId, algorithm = UpdateTrust.Algorithm,
            payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) }, Json);
    }
    private static bool SamePublicKey(RSA privateKey, string pem)
    { using var key = RSA.Create(); key.ImportFromPem(pem); return privateKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key.ExportSubjectPublicKeyInfo()); }
    private static bool MetadataMatches(string? product, string? productVersion, string? fileVersion, string version, string numeric)
        // Inno Setup reserves trailing spaces in its mutable version resource.
        => product?.TrimEnd(' ', '\0') == "VineMix" && fileVersion?.TrimEnd(' ', '\0') == numeric
            && productVersion?.TrimEnd(' ', '\0').Split('+')[0] == version;
    private static void RequirePrivateKeyAcl(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Release preparation requires Windows private-key access checks.");
        var owner = WindowsIdentity.GetCurrent().User;
        var acl = new FileInfo(path).GetAccessControl();
        if (owner is null || !owner.Equals(acl.GetOwner(typeof(SecurityIdentifier))))
            throw new InvalidOperationException("The private key must be owned by and grant access only to the current maintainer account.");
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !owner.Equals(rule.IdentityReference))
                throw new InvalidOperationException("The private key must be owned by and grant access only to the current maintainer account.");
    }
    private static void CopyVerified(string source, string destination, UpdateAsset asset)
    {
        NoReparse(source);
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
        if (new FileInfo(destination).Length != asset.Size || !HashEquals(Hash(destination), asset.Sha256)) throw new InvalidOperationException("A package changed during release staging. This output is not a prepared release.");
    }
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(); }
    private static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool HashEquals(string left, string right) => IsHash(left) && IsHash(right) && StringComparer.OrdinalIgnoreCase.Equals(left, right);
    private static bool Inside(string path, string parent) => string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("Release metadata contains a null required string.");
    private static byte[] Read(string path, string label, int maximum)
    {
        try { NoReparse(path); using var input = File.OpenRead(path); if (input.Length is <= 0 || input.Length > maximum) throw new IOException(); byte[] bytes = new byte[(int)input.Length]; input.ReadExactly(bytes); return bytes; }
        catch { throw new InvalidOperationException(label + " could not be read within its required size and path bounds."); }
    }
    private static JsonDocument Document(string path, string label, int maximum) => JsonDocument.Parse(Read(path, label, maximum), new JsonDocumentOptions { MaxDepth = 24 });
    private static void NoReparse(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        { if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Links are not supported for release inputs."); if (Path.GetPathRoot(current) == current) break; }
    }
    private static void SelfTest()
    {
        using var key = RSA.Create(3072);
        var trust = new UpdateTrust(new Dictionary<string, string> { ["fixture-key"] = key.ExportSubjectPublicKeyInfoPem() });
        string version = "1.2.3-beta.2";
        UpdateAsset Asset(string kind) { string name = $"VineMix-{version}-{kind}-x64.exe"; return new() { FileName = name, Url = UpdateTrust.AssetUrl(version, name), Size = 17, Sha256 = new string('a', 64) }; }
        var manifest = new UpdateManifest { Version = version, NumericVersion = "1.2.3.2", Notes = "Synthetic notes.", Installer = Asset("Setup"), Standalone = Asset("Standalone") };
        byte[] envelope = Sign(manifest, key, "fixture-key");
        var offer = trust.Verify(envelope, version, new() { Architecture = "x64", WindowsBuild = 30000 });
        if (offer.Manifest != manifest || !offer.EnvelopeBytes.SequenceEqual(envelope)) throw new InvalidOperationException("Fixture envelope roundtrip failed.");
        envelope[^3] ^= 1; bool rejected = false;
        try { trust.Verify(envelope, version, new() { Architecture = "x64", WindowsBuild = 30000 }); } catch (UpdateValidationException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Fixture tampering was not rejected.");
        if (!MetadataMatches("VineMix     ", version + "    ", "1.2.3.2     ", version, "1.2.3.2")
            || MetadataMatches("Other product ", version, "1.2.3.2", version, "1.2.3.2")
            || MetadataMatches("VineMix ", "1.2.4 ", "1.2.3.2", version, "1.2.3.2"))
            throw new InvalidOperationException("Padded Setup version-resource validation failed.");
        if (!Version.TryParse("1.2.3.2             ", out var paddedVersion)
            || !Version.TryParse("1.2.3.2", out var expectedVersion) || paddedVersion != expectedVersion)
            throw new InvalidOperationException("The worker's version predicate rejected padded Setup metadata.");
        Console.WriteLine("PASS: release signing roundtrip, tamper rejection and padded Setup metadata; no release key, packages, network or settings used.");
    }
}
