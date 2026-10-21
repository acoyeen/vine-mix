using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using VineMix.Desktop;

internal static class EmbeddedRuntimeFixture
{
    private static int Main(string[] args)
    {
        var checks = new List<string>();
        string result = Path.GetFullPath(args.Single());
        try
        {
            Require(EmbeddedRuntime.BuildMode == "Standalone", "The harness uses the actual standalone build mode.");
            string hash = EmbeddedRuntime.ValidatePackage();
            Require(hash.Length == 64, "The delivered archive and hash/size allowlist validate.");
            string root = Path.Combine(result, "cache");
            string engine = EmbeddedRuntime.ResolveEnginePath(root);
            Require(File.Exists(engine) && Path.GetFileName(engine) == "VineMix.Engine.exe", "First extraction produced the allowlisted engine.");
            Require(Path.GetFileName(Path.GetDirectoryName(engine)) == hash.ToLowerInvariant(), "The cache is keyed by the embedded archive SHA-256.");
            string digest = Hash(engine);
            DateTime stamp = File.GetLastWriteTimeUtc(engine);
            Require(EmbeddedRuntime.ResolveEnginePath(root) == engine && Hash(engine) == digest && File.GetLastWriteTimeUtc(engine) == stamp,
                "Validated reuse preserved bytes and timestamps.");
            byte[] corrupt = File.ReadAllBytes(engine); corrupt[0] ^= 0x5A;
            File.WriteAllBytes(engine, corrupt);
            string corruptDigest = Hash(engine);
            bool refused = false;
            using (var busy = new FileStream(engine, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { EmbeddedRuntime.ResolveEnginePath(root); }
                catch (IOException) { refused = true; }
                Require(refused && busy.Length == corrupt.Length, "A corrupt runtime held in use was never overwritten.");
            }
            Require(Hash(engine) == corruptDigest, "The locked cache retained its exact corrupt fixture bytes.");
            Require(EmbeddedRuntime.ResolveEnginePath(root) == engine && Hash(engine) == digest, "After the lock released, staged repair restored the original bytes.");
            string[] preserved = Directory.GetDirectories(root, "*.invalid-*");
            Require(preserved.Length == 1 && Hash(Path.Combine(preserved[0], "VineMix.Engine.exe")) == corruptDigest,
                "Repair preserved the corrupt cache for diagnosis before atomic promotion.");
            string unlisted = Path.Combine(Path.GetDirectoryName(engine)!, "unlisted.txt");
            File.WriteAllText(unlisted, "preserve this unexpected file");
            EmbeddedRuntime.ResolveEnginePath(root);
            Require(!File.Exists(unlisted) && Directory.GetDirectories(root, "*.invalid-*").Any(dir => File.Exists(Path.Combine(dir, "unlisted.txt"))),
                "An unexpected cache file caused clean repair and remained preserved in the retired cache.");
            string concurrent = Path.Combine(result, "concurrent-cache");
            string[] paths = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => EmbeddedRuntime.ResolveEnginePath(concurrent)))).GetAwaiter().GetResult();
            Require(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 && paths.All(path => Hash(path) == digest),
                "Four simultaneous extraction requests promoted and reused one valid immutable cache.");
            MethodInfo validatePath = typeof(EmbeddedRuntime).GetMethod("ValidateRelativePath", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (string unsafePath in new[] { "../escape", "C:/escape", "dir\\escape", "CON", "dir/../escape", "dir/file. " })
            {
                bool rejected = false;
                try { validatePath.Invoke(null, new object[] { unsafePath }); }
                catch (TargetInvocationException error) when (error.InnerException is IOException) { rejected = true; }
                Require(rejected, "Unsafe archive path rejected: " + unsafePath);
            }
            Write(true, null);
            Console.WriteLine($"PASS: {checks.Count} standalone runtime integrity checks. No audio engine or user cache was used.");
            return 0;
        }
        catch (Exception error) { Write(false, error.ToString()); Console.Error.WriteLine(error.Message); return 1; }
        void Require(bool passed, string description) { if (!passed) throw new InvalidOperationException(description); checks.Add(description); }
        void Write(bool passed, string? error) => File.WriteAllText(Path.Combine(result, "results.json"), JsonSerializer.Serialize(new { passed, error, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
