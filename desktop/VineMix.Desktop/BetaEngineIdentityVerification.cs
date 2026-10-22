using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VineMix.Desktop;

internal static class BetaEngineIdentityVerification
{
    internal static async Task RunAsync(Action<bool,string> require)
    {
        string? previous = WorkspacePaths.CustomSettings;
        string root = Path.Combine(Path.GetTempPath(),"VineMix-Identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        WorkspacePaths.CustomSettings = Path.Combine(root,"settings.json");
        try
        {
            const string workspaceMismatch = "The engine uses a different workspace. Quit the current session before opening another settings file.";
            const string physicalMismatch = "The engine opened a different physical settings location. Saving has been blocked to protect your workspace.";
            const string releaseMismatch = "The audio engine does not match this VineMix release.";
            var cases = new (string Name, Action<MixerSnapshot>? Alter, bool IncludeSnapshot, string? Error)[]
            {
                ("matching identity", null, true, null),
                ("physical-workspace mismatch", snapshot => snapshot.PhysicalSettingsPath = Path.Combine(root,"other-settings.json"), true, physicalMismatch),
                ("logical-workspace mismatch", snapshot => snapshot.SettingsPath = Path.Combine(root,"other-settings.json"), true, workspaceMismatch),
                ("release mismatch", snapshot => snapshot.EngineVersion = "incompatible-fixture", true, releaseMismatch),
                ("protocol mismatch", snapshot => snapshot.EngineProtocolVersion = 2, true, releaseMismatch),
                ("process mismatch", snapshot => snapshot.EnginePid = 0, true, releaseMismatch),
                ("executable mismatch", snapshot => snapshot.ExecutablePath = Path.Combine(root,"Other.Engine.exe"), true, releaseMismatch),
                ("missing identity", null, false, "The engine did not verify its workspace before the requested action.")
            };
            foreach (var scenario in cases)
            {
                string name = "VineMix.PrivateIdentity." + Guid.NewGuid().ToString("N");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var server = new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                var commands = new List<string?>();
                var serving = Task.Run(async () =>
                {
                    await server.WaitForConnectionAsync(deadline.Token);
                    using var reader = new StreamReader(server,new UTF8Encoding(false),false,4096,true);
                    string? first = await reader.ReadLineAsync(deadline.Token);
                    using var request = JsonDocument.Parse(first ?? throw new EndOfStreamException("The private client did not send its identity request."));
                    commands.Add(request.RootElement.GetProperty("command").GetString());
                    var snapshot = new MixerSnapshot
                    {
                        Configuration = new() { Version = 2, Graph = GraphDefaults.Fresh() },
                        SettingsPath = WorkspacePaths.Settings,
                        PhysicalSettingsPath = WorkspacePhysicalPath.Resolve(WorkspacePaths.Settings),
                        EnginePid = Environment.ProcessId, EngineVersion = ReleaseInfo.Version, EngineProtocolVersion = 1,
                        ExecutablePath = Environment.ProcessPath!
                    };
                    scenario.Alter?.Invoke(snapshot);
                    await ReplyAsync(server, request, scenario.IncludeSnapshot ? snapshot : null, deadline.Token);
                    string? second = await reader.ReadLineAsync(deadline.Token);
                    if (second is null) return;
                    using var mutation = JsonDocument.Parse(second);
                    commands.Add(mutation.RootElement.GetProperty("command").GetString());
                    await ReplyAsync(server, mutation, snapshot, deadline.Token);
                },deadline.Token);
                using var connection = new EngineConnection(name,Environment.ProcessPath!);
                bool accepted = false;
                IOException? rejection = null;
                try
                {
                    try { accepted = (await connection.RequestAsync("configure",new() { Version = 2, Graph = GraphDefaults.Fresh() },0,deadline.Token)).Ok; }
                    catch (IOException error) when (scenario.Error is not null) { rejection = error; }
                    await serving;
                    require(commands.Count > 0 && commands[0] == "snapshot",
                        "A fresh mutating connection first requests a read-only identity snapshot (" + scenario.Name + ").");
                    require(scenario.Error is null
                        ? accepted && commands.Count == 2 && commands[1] == "configure"
                        : !accepted && rejection?.Message == scenario.Error && commands.Count == 1,
                        scenario.Error is null ? "An identified private engine can receive a subsequent configuration request."
                        : "A " + scenario.Name + " closes the connection with its identity error before any mutating request is sent.");
                }
                finally
                {
                    // Unblock and observe the private server even when the client
                    // or an assertion fails. Never leave a pending test pipe alive.
                    connection.Dispose(); deadline.Cancel(); server.Dispose();
                    try { await serving; }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                    catch (IOException) { }
                }
            }
        }
        finally { WorkspacePaths.CustomSettings = previous; }
    }

    private static async Task ReplyAsync(NamedPipeServerStream server, JsonDocument request, MixerSnapshot? snapshot,
        CancellationToken token)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { protocolVersion = 1,
            requestId = request.RootElement.GetProperty("requestId").GetString(), ok = true, snapshot }, Protocol.Json) + "\n");
        // Write each complete response explicitly. A StreamWriter flushes again
        // on disposal, after a correctly rejected client has closed its pipe;
        // that teardown flush can report a broken pipe and mask the real result.
        await server.WriteAsync(bytes.AsMemory(), token);
    }
}
