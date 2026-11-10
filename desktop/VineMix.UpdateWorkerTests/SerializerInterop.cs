using System.Text.Json;
using VineMix.Desktop;

/// <summary>
/// The 0.5 side of the update-record cross-check with the Rust desktop
/// (<c>crates/vinemix-ui/src/update/tests.rs</c>, <c>interop_write</c> and
/// <c>interop_read</c>). Both applications read and write the same transaction
/// files during an upgrade, so each must accept exactly what the other writes.
///
/// <c>--serializer-interop-write &lt;directory&gt;</c> writes records with the
/// production 0.5 serializer options into <c>to-rust</c>.
/// <c>--serializer-interop-read &lt;directory&gt;</c> reads the records the Rust
/// serializer wrote into <c>from-rust</c> with the production 0.5 reader, which
/// rejects any member it does not know, and checks every value.
///
/// Only the given directory is touched. <c>scripts/verify-rust.ps1</c> runs
/// the write step, the two Rust tests, then the read step.
/// </summary>
internal static class SerializerInterop
{
    private static int _checks;

    internal static bool TryRun(string[] args, out int result)
    {
        result = 0;
        if (args.Length != 2 || args[0] is not ("--serializer-interop-write" or "--serializer-interop-read")) return false;
        try
        {
            string directory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directory)) throw new IOException("The interop exchange directory does not exist.");
            if (args[0] == "--serializer-interop-write") Write(Path.Combine(directory, "to-rust"));
            else Read(Path.Combine(directory, "from-rust"));
            Console.WriteLine($"PASS: {args[0][2..]}, {_checks} checks with the production 0.5 update serializer. Only the exchange directory was used.");
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); result = 1; }
        return true;
    }

    private static void Require(bool value, string message)
    { ++_checks; if (!value) throw new InvalidOperationException(message); }

    private static void Put<T>(string directory, string name, T value)
    {
        File.WriteAllBytes(Path.Combine(directory, name), JsonSerializer.SerializeToUtf8Bytes(value, UpdateTransaction.Json));
        ++_checks;
    }

    private static T Get<T>(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        if (!File.Exists(path)) throw new IOException("The Rust serializer did not write " + name + ".");
        ++_checks;
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), UpdateTransaction.Json) ?? throw new IOException(name + " is empty.");
    }

    /// <summary>The values <c>interop_read</c> asserts on the Rust side.</summary>
    private static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var parent = new UpdateProcessIdentity(77, 638_999_999_999_999_999, @"C:\Programs\VineMix\VineMix.exe", new string('1', 64));
        Put(directory, "transaction.json", new UpdateTransactionData
        {
            Id = new string('3', 32), Version = "0.6.0", CurrentVersion = "0.5.0-beta", Token = new string('A', 64),
            EnvelopeSha256 = new string('2', 64), AssetFileName = "VineMix-0.6.0-Setup-x64.exe", Standalone = true,
            Parent = parent, Engine = null, SettingsPath = null, CreatedUtc = DateTimeOffset.UtcNow
        });
        var registry = new UpdateRegistrySnapshot("key", @"C:\Programs\VineMix", new[]
        {
            new UpdateRegistryValue("Binary", 3, Array.Empty<string>(), new byte[] { 9, 8, 7 }, 0),
            new UpdateRegistryValue("Number", 4, Array.Empty<string>(), Array.Empty<byte>(), -5)
        });
        Put(directory, "journal.json", new UpdateJournal
        {
            Phase = UpdatePhase.AwaitingHealth, Attempts = 2,
            Backup = new UpdateBackup(new[] { new UpdateFileEntry("VineMix.exe", 10, new string('4', 64)) }, registry),
            Candidate = new UpdateBackup(new[] { new UpdateFileEntry("VineMix.exe", 12, new string('5', 64)) }, null),
            CandidateEngineSha256 = new string('6', 64), UpdatedUtc = DateTimeOffset.UtcNow
        });
        Put(directory, "ready.json", new UpdateSignal(new string('A', 64), new string('2', 64)));
    }

    /// <summary>The values <c>interop_write</c> writes on the Rust side.</summary>
    private static void Read(string directory)
    {
        if (!Directory.Exists(directory)) throw new IOException("The Rust serializer has not written from-rust.");
        string token = new('F', 64), envelope = new('b', 64);
        bool SameIdentity(UpdateProcessIdentity? identity) => identity is { ProcessId: 4242, StartTimeUtcTicks: 639_000_000_000_000_000 }
            && identity.ImagePath == @"C:\Programs\VineMix\VineMix.exe" && identity.Sha256 == new string('a', 64);

        var data = Get<UpdateTransactionData>(directory, "transaction.json");
        Require(data.FormatVersion == 1 && data.Id == new string('2', 32) && data.Version == "0.6.0" && data.CurrentVersion == "0.5.0-beta",
            "Rust transaction identity fields differ.");
        Require(data.Token == token && data.EnvelopeSha256 == envelope && data.AssetFileName == "VineMix-0.6.0-Setup-x64.exe" && !data.Standalone,
            "Rust transaction asset fields differ.");
        Require(SameIdentity(data.Parent) && data.Engine is null, "Rust transaction process identities differ.");
        Require(data.SettingsPath == @"C:\Users\Example\AppData\Local\VineMix\settings.json", "Rust transaction settings path differs.");
        Require(data.CreatedUtc == new DateTimeOffset(2026, 11, 7, 2, 3, 4, TimeSpan.Zero).AddTicks(1_234_567),
            "Rust transaction timestamp lost precision: " + data.CreatedUtc.ToString("o"));

        var journal = Get<UpdateJournal>(directory, "journal.json");
        Require(journal.Phase == UpdatePhase.AwaitingHealth && journal.ResumePhase == UpdatePhase.RollingBack && journal.Attempts == 1,
            "Rust journal phases differ.");
        Require(journal.Backup is { Files.Length: 1 } backup && backup.Files[0] == new UpdateFileEntry("VineMix.exe", 10, new string('d', 64)),
            "Rust journal backup inventory differs.");
        var registry = journal.Backup?.Registry;
        Require(registry is { Key: "k", Values.Length: 1 } && registry.TargetDirectory == @"C:\Programs\VineMix", "Rust journal registry snapshot differs.");
        var value = registry!.Values[0];
        Require(value.Name == "Bin" && value.Kind == 3 && value.Strings.Length == 0 && value.Number == 0
            && value.Bytes.AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 250 }), "Rust journal registry value differs.");
        Require(journal.CandidateEngineSha256 == new string('c', 64) && journal.Candidate is null && journal.Message == "",
            "Rust journal candidate fields differ.");

        var health = Get<UpdateSignal>(directory, "health.json");
        Require(health == new UpdateSignal(token, envelope, 4243, 639_000_000_000_000_001, true, "ok"), "Rust health signal differs.");
        var workspace = Get<UpdateWorkspaceCheckpoint>(directory, "workspace.json");
        Require(workspace == new UpdateWorkspaceCheckpoint(token, envelope, @"C:\x\settings.json", 5, new string('e', 64)), "Rust workspace checkpoint differs.");
        var engine = Get<UpdateEngineSignal>(directory, "candidate-engine.json");
        Require(engine.Token == token && engine.EnvelopeSha256 == envelope && SameIdentity(engine.Identity), "Rust engine signal differs.");
    }
}
