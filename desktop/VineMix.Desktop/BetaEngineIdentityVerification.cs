using System;
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
            foreach (bool match in new[] { true, false })
            {
                string name = "VineMix.PrivateIdentity." + Guid.NewGuid().ToString("N");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var server = new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                bool mutationReceived = false;
                var serving = Task.Run(async () =>
                {
                    await server.WaitForConnectionAsync(deadline.Token);
                    using var reader = new StreamReader(server,new UTF8Encoding(false),false,4096,true);
                    using var writer = new StreamWriter(server,new UTF8Encoding(false),4096,true) { AutoFlush = true };
                    string? first = await reader.ReadLineAsync(deadline.Token);
                    using var request = JsonDocument.Parse(first!);
                    require(request.RootElement.GetProperty("command").GetString() == "snapshot", "A fresh mutating connection first requests a read-only identity snapshot.");
                    var snapshot = new MixerSnapshot
                    {
                        Configuration = new() { Version = 2, Graph = GraphDefaults.Fresh() },
                        SettingsPath = WorkspacePaths.Settings,
                        PhysicalSettingsPath = match ? WorkspacePhysicalPath.Resolve(WorkspacePaths.Settings) : Path.Combine(root,"other-settings.json"),
                        EnginePid = Environment.ProcessId, EngineVersion = ReleaseInfo.Version, EngineProtocolVersion = 1,
                        ExecutablePath = Environment.ProcessPath!
                    };
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { protocolVersion = 1,
                        requestId = request.RootElement.GetProperty("requestId").GetString(), ok = true, snapshot },Protocol.Json));
                    string? second = await reader.ReadLineAsync(deadline.Token);
                    if (second is null) return;
                    using var mutation = JsonDocument.Parse(second);
                    mutationReceived = mutation.RootElement.GetProperty("command").GetString() == "configure";
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { protocolVersion = 1,
                        requestId = mutation.RootElement.GetProperty("requestId").GetString(), ok = true, snapshot },Protocol.Json));
                },deadline.Token);
                using var connection = new EngineConnection(name,Environment.ProcessPath!);
                bool accepted = false;
                try { accepted = (await connection.RequestAsync("configure",new() { Version = 2, Graph = GraphDefaults.Fresh() },0,deadline.Token)).Ok; }
                catch (IOException) when (!match) { }
                await serving;
                require(match ? accepted && mutationReceived : !accepted && !mutationReceived,
                    match ? "An identified private engine can receive a subsequent configuration request."
                    : "A physical-workspace mismatch closes the connection before any mutating request is sent.");
            }
        }
        finally { WorkspacePaths.CustomSettings = previous; }
    }
}
