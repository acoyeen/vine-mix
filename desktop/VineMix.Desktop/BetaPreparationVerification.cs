using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VineMix.Desktop;

internal static class BetaPreparationVerification
{
    internal static void Run(Action<bool,string> require)
    {
        string root = Path.Combine(Path.GetTempPath(), "VineMix-Beta-Fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Case(string name) { string path = Path.Combine(root,name); Directory.CreateDirectory(path); return path; }
        string Saved(int revision) => JsonSerializer.Serialize(new MixerConfiguration { Version = 2, Revision = (ulong)revision, Graph = GraphDefaults.Fresh() }, Protocol.Json);
        void Refuse(Action action, string reason)
        {
            bool refused = false;
            try { action(); } catch (IOException) { refused = true; }
            require(refused,reason);
        }
        string source = Case("legacy");
        File.WriteAllText(Path.Combine(source,"settings.json"),Saved(12));
        File.WriteAllText(Path.Combine(source,"settings.json.bak"),Saved(11));
        File.WriteAllText(Path.Combine(source,"ui.json"),"{\"reducedAnimation\":true}");
        string destination = Path.Combine(root,"migrated");
        WorkspaceMigration.Run(destination,[source],_ => throw new Exception("No choice needed"));
        require(File.ReadAllText(Path.Combine(destination,"settings.json")) == Saved(12)
            && File.ReadAllText(Path.Combine(destination,"settings.json.bak")) == Saved(11)
            && File.Exists(Path.Combine(destination,"migration.complete.json")), "Legacy migration commits exact primary/backup and records completion.");
        File.WriteAllText(Path.Combine(source,"settings.json"),Saved(13));
        WorkspaceMigration.Run(destination,[source],_ => null);
        require(File.ReadAllText(Path.Combine(destination,"settings.json")) == Saved(12), "Completed migration never reimports newer or older legacy files.");
        string damaged = Case("damaged-destination"); File.WriteAllText(Path.Combine(damaged,"settings.json"),"broken destination");
        WorkspaceMigration.Run(damaged,[source],_ => null);
        require(File.ReadAllText(Path.Combine(damaged,"settings.json")) == "broken destination", "A damaged existing destination takes precedence over legacy recovery snapshots.");
        foreach (string artifact in new[] { "settings.json.new", "settings.json.v1.bak" })
        {
            string orphan = Case("existing-" + artifact);
            File.WriteAllText(Path.Combine(orphan,artifact),Saved(999));
            WorkspaceMigration.Run(orphan,[source],_ => null);
            require(!File.Exists(Path.Combine(orphan,"settings.json")) && File.ReadAllText(Path.Combine(orphan,artifact)) == Saved(999),
                "An interrupted or preserved workspace artifact prevents stale legacy import: " + artifact);
        }
        string conflict = Case("conflicting-legacy"); File.WriteAllText(Path.Combine(conflict,"settings.json"),Saved(99));
        string cancelled = Path.Combine(root,"cancelled");
        Refuse(() => WorkspaceMigration.Run(cancelled,[source,conflict],_ => null), "Conflicting legacy copies require explicit selection; cancellation preserves both.");
        require(!Directory.Exists(cancelled), "Cancelling a source choice does not establish an empty destination.");
        string selected = Path.Combine(root,"selected");
        WorkspaceMigration.Run(selected,[source,conflict],_ => conflict);
        require(File.ReadAllText(Path.Combine(selected,"settings.json")) == Saved(99), "Explicit conflict selection is honored without timestamp heuristics.");
        foreach (string point in new[] { "journal", "settings.json" })
        {
            string interrupted = Path.Combine(root,"interrupted-" + point);
            Refuse(() => WorkspaceMigration.Run(interrupted,[source],_ => null,p => { if(p == point) throw new IOException("Simulated interrupted migration"); }), "Migration interruption is surfaced after " + point + ".");
            WorkspaceMigration.Run(interrupted,[source],_ => null);
            require(File.ReadAllText(Path.Combine(interrupted,"settings.json")) == Saved(13)
                && File.Exists(Path.Combine(interrupted,"migration.complete.json")), "Interrupted migration resumes completely after " + point + ".");
        }
        string stale = Path.Combine(root,"stale-source");
        Refuse(() => WorkspaceMigration.Run(stale,[source],_ => null,p => { if(p == "journal") throw new IOException("Interrupted"); }), "Stage a restart-safe transaction.");
        File.WriteAllText(Path.Combine(source,"settings.json"),Saved(14));
        Refuse(() => WorkspaceMigration.Run(stale,[source],_ => null), "A legacy workspace changed after interruption cannot be replaced by stale staging.");
        string replaced = Path.Combine(root,"destination-changed");
        Refuse(() => WorkspaceMigration.Run(replaced,[source],_ => null,p => { if(p == "journal") throw new IOException("Interrupted"); }), "Stage a conflicting destination test.");
        File.WriteAllText(Path.Combine(replaced,"settings.json"),Saved(100));
        Refuse(() => WorkspaceMigration.Run(replaced,[source],_ => null), "Migration cannot overwrite a newer destination created after interruption.");
        require(File.ReadAllText(Path.Combine(replaced,"settings.json")) == Saved(100), "A newer workspace remains byte-identical after refused migration.");
        string unexpected = Path.Combine(root,"unexpected-artifact");
        Refuse(() => WorkspaceMigration.Run(unexpected,[source],_ => null,p => { if(p == "journal") throw new IOException("Interrupted"); }), "Stage an unexpected recovery artifact test.");
        File.WriteAllText(Path.Combine(unexpected,"settings.json.new"),Saved(101));
        Refuse(() => WorkspaceMigration.Run(unexpected,[source],_ => null), "An unexpected newer save artifact blocks stale migration even when absent from the journal.");
        foreach (string brokenJournal in new[] { "{", "null", "{\"source\":\"C:\\\\Legacy\",\"files\":null}" })
        {
            string bad = Case("bad-journal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(bad,".migration-staging"));
            File.WriteAllText(Path.Combine(bad,".migration-staging","journal.json"),brokenJournal);
            Refuse(() => WorkspaceMigration.Run(bad,[source],_ => null), "A damaged migration journal gives a recoverable startup error without writing defaults.");
        }
        var fresh = GraphDefaults.Fresh();
        require(fresh.Nodes.Count == 3 && fresh.Edges.Count == 2 && fresh.Nodes.Count(n => n.Kind == "microphone") == 1
            && fresh.Nodes.Where(n => n.Kind is "output" or "virtualOutput").All(n => n.Muted), "Fresh workspace has the required conservative three-node fanout.");
        var cable = fresh.Nodes.Single(n => n.Kind == "virtualOutput");
        cable.AutoAssignDevice = false;
        var restored = JsonSerializer.Deserialize<GraphConfiguration>(JsonSerializer.Serialize(fresh,Protocol.Json),Protocol.Json)!;
        Endpoint[] endpoint = [new() { Id = "private-example-vac", Name = "Virtual Cable", Transport = "vacLite", Direction = "render", State = "Active" }];
        require(VirtualDeviceBindings.FindAutomaticAssignments(restored,endpoint).Count == 0, "A deliberate unassignment survives serialization and reopening.");
        using (var history = new MixerViewModel(preview:true))
        {
            var historyGraph = new GraphConfiguration { Nodes = [GraphDefaults.NewNode("virtualOutput",0,0)] };
            history.ApplyPreviewSnapshot(new MixerSnapshot { Configuration = new() { Version = 2, Graph = historyGraph } });
            history.Edit(c => c.Graph.Nodes[0].Device = new DeviceBinding { Id = "private-manual-output" });
            require(history.Undo() && !history.GraphConfig.Nodes[0].AutoAssignDevice && history.Redo()
                && history.GraphConfig.Nodes[0].Device.Id == "private-manual-output", "Undoing and redoing a manual virtual-device choice preserves history after explicit unassignment.");
        }
        using var model = new MixerViewModel(preview:true) { QueuePreviewEdits = true };
        model.ApplyPreviewSnapshot(new MixerSnapshot { SettingsState = "backupAvailable", Configuration = new() { Version = 2, Graph = GraphDefaults.Fresh() }, Endpoints = endpoint.ToList() });
        require(model.HasSettingsRecovery && !model.CanEdit && model.CanRestoreBackup && !model.CanRestartEngine
            && model.PendingEditCount == 0 && model.GraphConfig.Nodes.All(n => !VirtualDeviceBindings.IsAssigned(n.Device)), "Backup inspection never assigns devices, queues saves or permits an engine restart.");
        string role = "Verification." + Guid.NewGuid().ToString("N");
        using (var owner = new SessionOwnership()) using (var competitor = new SessionOwnership())
            require(owner.Acquire(role) && !competitor.Acquire(role), "Current and legacy presence guards exclude a competing private session.");
        using (var reopened = new SessionOwnership()) require(reopened.Acquire(role), "Both session identities are released together on orderly shutdown.");
        require(StartupRegistration.Owns("\"C:\\Example\\VineMix.exe\" --background", @"C:\Example\VineMix.exe")
            && !StartupRegistration.Owns("\"C:\\Other\\VineMix.exe\" --background", @"C:\Example\VineMix.exe"), "Startup registration recognizes only its own executable.");
        require(WorkspacePhysicalPath.Resolve(Path.Combine(root,"not-created","settings.json"))
            .EndsWith(@"not-created\settings.json",StringComparison.OrdinalIgnoreCase), "A new workspace resolves through an existing physical ancestor without creating files.");
        // Evidence is intentionally retained under the unique fixture directory.
    }
}
