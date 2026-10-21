using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace VineMix.Desktop;

/// <summary>Copy-only migration. Call after normalization, with session locks held.</summary>
internal static class WorkspaceMigration
{
    private static readonly string[] Names = ["settings.json", "settings.json.bak", "ui.json", "settings.json.established", "settings.json.new", "settings.json.v1.bak"];
    internal sealed record Entry(string Name, string Hash);
    internal sealed record Journal(string Source, Entry[] Files);
    internal static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VineMix");
    internal static string[] LegacyDirectories()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string> { Path.Combine(local, "AudioMixer") };
        string packages = Path.Combine(local, "Packages");
        if (Directory.Exists(packages))
            candidates.AddRange(Directory.EnumerateDirectories(packages, "OpenAI.Codex_*")
                .Select(p => Path.Combine(p, "LocalCache", "Local", "AudioMixer")));
        return candidates.Where(HasWorkspace).ToArray();
    }
    private static bool HasWorkspace(string directory) => Names.Where(n => n != "ui.json").Any(n => File.Exists(Path.Combine(directory, n)))
        || File.Exists(Path.Combine(directory, "migration.complete.json"));
    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static Entry[] Describe(string directory) => Names.Where(n => File.Exists(Path.Combine(directory, n)))
        .Select(n => new Entry(n, Hash(Path.Combine(directory, n)))).ToArray();
    private static void DurableWrite(string path, string text)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text); file.Write(bytes); file.Flush(true);
    }
    internal static void Run(string destination, string[] sources, Func<string[], string?> select, Action<string>? checkpoint = null,
        Action<string>? validate = null)
    {
        string stage = Path.Combine(destination, ".migration-staging"), journalPath = Path.Combine(stage, "journal.json");
        string complete = Path.Combine(destination, "migration.complete.json");
        if (File.Exists(complete)) return;
        if (!File.Exists(journalPath))
        {
            // Even an unreadable destination wins. Its engine presents recovery.
            if (HasWorkspace(destination)) return;
            var candidates = sources.Where(HasWorkspace).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length == 0) return;
            string source = candidates[0];
            string signature = JsonSerializer.Serialize(Describe(source));
            if (candidates.Skip(1).Any(c => JsonSerializer.Serialize(Describe(c)) != signature))
            {
                source = select(candidates) ?? throw new IOException("Workspace migration was cancelled. Your existing files have not been changed.");
                if (!candidates.Contains(source, StringComparer.OrdinalIgnoreCase)) throw new IOException("Choose one of the existing workspace copies.");
            }
            // An interrupted pre-journal copy is retained as evidence, then rebuilt.
            if (Directory.Exists(stage)) Directory.Move(stage, stage + ".incomplete-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var entries = Describe(source);
            foreach (var entry in entries)
            {
                string from = Path.Combine(source, entry.Name), to = Path.Combine(stage, entry.Name);
                using (var input = File.OpenRead(from))
                using (var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                if (Hash(from) != entry.Hash || Hash(to) != entry.Hash) throw new IOException("The source workspace changed while being copied. Retry after closing its application.");
            }
            // Ensure no file appeared/disappeared after the snapshot was taken.
            if (JsonSerializer.Serialize(entries) != JsonSerializer.Serialize(Describe(source)))
                throw new IOException("The source workspace changed during migration. Close it and retry.");
            validate?.Invoke(stage);
            string pendingJournal = Path.Combine(stage, "journal.pending.json");
            DurableWrite(pendingJournal, JsonSerializer.Serialize(new Journal(source, entries), Protocol.Json));
            File.Move(pendingJournal, journalPath);
            checkpoint?.Invoke("journal");
        }
        Journal journal;
        try
        {
            journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath), Protocol.Json)
                ?? throw new IOException("The migration journal could not be read. Existing files have been retained.");
        }
        catch (JsonException error) { throw new IOException("The migration record is damaged. Original and staged workspace files have been retained.",error); }
        if (string.IsNullOrWhiteSpace(journal.Source) || !Path.IsPathFullyQualified(journal.Source)
            || journal.Files is null || journal.Files.Length == 0 || journal.Files.Any(f => f is null)
            || journal.Files.Select(f => f.Name).Distinct().Count() != journal.Files.Length)
            throw new IOException("The migration journal is incomplete.");
        if (HasWorkspace(journal.Source) && JsonSerializer.Serialize(journal.Files) != JsonSerializer.Serialize(Describe(journal.Source)))
            throw new IOException("The legacy workspace changed after migration was interrupted. Both copies have been retained; do not restore the older staging copy over it.");
        foreach (var existing in Describe(destination))
            if (!journal.Files.Any(entry => entry.Name == existing.Name)
                && !(existing.Name == "settings.json.established" && File.ReadAllText(Path.Combine(destination,existing.Name)) == "VineMix migrated workspace\n"))
                throw new IOException("New workspace files appeared after migration was interrupted. No existing files were replaced.");
        // Validate the whole transaction before promoting even one file.
        foreach (var entry in journal.Files)
        {
            if (!Names.Contains(entry.Name) || Hash(Path.Combine(stage, entry.Name)) != entry.Hash)
                throw new IOException("Migration staging is incomplete or damaged. Your original workspace is preserved.");
            string target = Path.Combine(destination, entry.Name);
            if (File.Exists(target) && Hash(target) != entry.Hash)
                throw new IOException("A newer workspace conflicts with the interrupted migration. No files were replaced.");
        }
        validate?.Invoke(stage);
        foreach (var entry in journal.Files)
        {
            string target = Path.Combine(destination, entry.Name);
            if (!File.Exists(target))
            {
                string pending = Path.Combine(destination, ".migration-" + entry.Name + ".new");
                // Only this transaction owns the temporary file; preserve an
                // interrupted attempt before producing a new complete copy.
                if (File.Exists(pending)) File.Move(pending, pending + ".interrupted-" + Guid.NewGuid().ToString("N"));
                using (var input = File.OpenRead(Path.Combine(stage, entry.Name)))
                using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
                if (Hash(pending) != entry.Hash) throw new IOException("The staged workspace could not be committed intact.");
                File.Move(pending, target);
            }
            checkpoint?.Invoke(entry.Name);
        }
        string established = Path.Combine(destination, "settings.json.established");
        if (!File.Exists(established)) DurableWrite(established, "VineMix migrated workspace\n");
        // Malformed settings are intentionally preserved byte for byte. The
        // native loader classifies them and requires recovery before any audio.
        string pendingComplete = Path.Combine(destination, ".migration-complete.new");
        if (File.Exists(pendingComplete)) File.Move(pendingComplete, pendingComplete + ".interrupted-" + Guid.NewGuid().ToString("N"));
        DurableWrite(pendingComplete, JsonSerializer.Serialize(journal, Protocol.Json));
        File.Move(pendingComplete, complete);
    }

    internal static void ValidateStaging(string stage, string engine)
    {
        var start = new ProcessStartInfo(engine) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--validate-settings"); start.ArgumentList.Add(Path.Combine(stage, "settings.json"));
        using var process = Process.Start(start) ?? throw new IOException("The staged workspace could not be validated.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000)) { process.Kill(); throw new IOException("Workspace validation timed out; no migrated files were activated."); }
        if (process.ExitCode is not (0 or 2)) throw new IOException("The workspace validator failed: " + errors.GetAwaiter().GetResult());
        try
        {
            using var report = JsonDocument.Parse(output.GetAwaiter().GetResult());
            if (!report.RootElement.TryGetProperty("settingsState",out var outcome)
                || outcome.GetString() is not ("loaded" or "backupAvailable" or "recoveryRequired" or "unsupported"))
                throw new IOException("The staged workspace did not have an established recovery identity.");
        }
        catch (JsonException error) { throw new IOException("The workspace validator did not return a readable result.",error); }
    }

    internal static string? ChooseSource(string[] sources)
    {
        string? choice = null;
        var window = new Window { Title = "VineMix · Choose your saved workspace", Width = 720, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Different saved workspaces were found. Choose the copy you want to keep using. Both originals will be preserved.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        var list = new ListBox { ItemsSource = sources, Height = 130 }; panel.Children.Add(list);
        var use = new Button { Content = "Use selected workspace", Margin = new Thickness(0,16,0,0), Padding = new Thickness(12,6,12,6) };
        use.Click += (_, _) => { if (list.SelectedItem is string selected) { choice = selected; window.DialogResult = true; } };
        panel.Children.Add(use); window.Content = panel; window.ShowDialog(); return choice;
    }
}
