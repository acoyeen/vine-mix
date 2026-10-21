using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VineMix.Desktop;

// The server name is private and random for each fixture. Nothing connects to
// VineMix.Control, launches the engine, opens devices or touches settings.
internal static class EngineConnectionVerification
{
    internal static async Task RunAsync(Action<bool, string> require)
    {
        await RepeatedFragmentedResponses(require).ConfigureAwait(false);
        await OutboundMessageBoundary(require).ConfigureAwait(false);
        await MalformedResponses(require).ConfigureAwait(false);
        await CancelAndReconnect(require).ConfigureAwait(false);
        await DisposePendingRequests(require).ConfigureAwait(false);
    }

    private static string PrivateName() => "VineMix.Verification." + Guid.NewGuid().ToString("N");

    private static async Task WithServer(string name,
        Func<NamedPipeServerStream, CancellationToken, Task> serve,
        Func<CancellationToken, Task> client, bool drain = true, bool allowClosedWrite = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 1024, 1024);
        var serving = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            try { await serve(server, timeout.Token).ConfigureAwait(false); }
            catch (IOException) when (allowClosedWrite) { } // Client deliberately rejects a large reply mid-write.
        });
        try
        {
            await client(timeout.Token).WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
            if (drain) await serving.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        finally
        {
            timeout.Cancel(); server.Dispose();
            // Every fixture tears its private server down even if an assertion
            // fails. Bound cleanup so a verification fault cannot hang the UI.
            try { await serving.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
        }
    }

    private static StreamReader Reader(NamedPipeServerStream server) =>
        new(server, new UTF8Encoding(false, true), false, 4096, leaveOpen: true);

    private static async Task<JsonDocument> ReadRequest(StreamReader reader, CancellationToken token) =>
        JsonDocument.Parse(await reader.ReadLineAsync(token).ConfigureAwait(false)
            ?? throw new EndOfStreamException("The verification client did not send a request."));

    private static string Reply(string id, string detail = "ok") => JsonSerializer.Serialize(new
    {
        protocolVersion = 1, requestId = id, ok = true,
        snapshot = new MixerSnapshot { Warning = detail }
    }, Protocol.Json) + "\n";

    private static async Task Write(NamedPipeServerStream server, string text, CancellationToken token, bool fragment = false)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        int offset = 0, step = 0;
        int[] chunks = [1, 2, 7, 63, 211, 4096];
        while (offset < bytes.Length)
        {
            int count = fragment ? Math.Min(chunks[Math.Min(step++, chunks.Length - 1)], bytes.Length - offset) : bytes.Length;
            await server.WriteAsync(bytes.AsMemory(offset, count), token).ConfigureAwait(false);
            offset += count;
        }
        await server.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task RepeatedFragmentedResponses(Action<bool, string> require)
    {
        string name = PrivateName();
        using var connection = new EngineConnection(name);
        var ids = new HashSet<string>();
        string large = new string('x', 18000) + " · α 音声";
        string[] warnings = [large, "short", "third", "fourth"];
        bool commandsMatched = true;
        await WithServer(name, async (server, token) =>
        {
            using var reader = Reader(server);
            for (int i = 0; i < warnings.Length; i++)
            {
                using var request = await ReadRequest(reader, token).ConfigureAwait(false);
                var root = request.RootElement;
                ids.Add(root.GetProperty("requestId").GetString()!);
                commandsMatched &= root.GetProperty("protocolVersion").GetInt32() == 1
                    && root.GetProperty("command").GetString() == "snapshot";
                await Write(server, Reply(root.GetProperty("requestId").GetString()!, warnings[i]), token, fragment: true).ConfigureAwait(false);
            }
        }, async token =>
        {
            var first = await connection.RequestAsync("snapshot", null, null, token).ConfigureAwait(false);
            require(first.Ok && first.Snapshot?.Warning == large,
                "A private-pipe response can span many reads, including split UTF-8 characters, without losing snapshot content.");
            var second = await connection.RequestAsync("snapshot", null, null, token).ConfigureAwait(false);
            require(second.Snapshot?.Warning == "short",
                "Reusing the receive buffer after a larger response clears old bytes and returns the exact shorter snapshot.");
            var third = connection.RequestAsync("snapshot", null, null, token);
            var fourth = connection.RequestAsync("snapshot", null, null, token);
            var values = await Task.WhenAll(third, fourth).ConfigureAwait(false);
            require(values[0].Snapshot?.Warning == "third" && values[1].Snapshot?.Warning == "fourth",
                "Concurrent callers serialize requests and receive their own matching replies on one private pipe.");
        }).ConfigureAwait(false);
        require(ids.Count == warnings.Length && commandsMatched,
            "Repeated private-pipe requests preserve the command and protocol and use distinct request identities.");
    }

    private static async Task OutboundMessageBoundary(Action<bool, string> require)
    {
        const int limit = 256 * 1024;
        string name = PrivateName();
        using var connection = new EngineConnection(name);
        var configuration = new MixerConfiguration();
        // Request IDs have a fixed 32-byte ASCII representation. Derive the
        // remaining payload from the actual serializer so model additions do
        // not silently move this fixture away from the wire-size boundary.
        string envelope = JsonSerializer.Serialize(new
        {
            protocolVersion = 1, requestId = new string('0', 32), command = "configure",
            configuration, expectedRevision = (ulong?)null
        }, Protocol.Json);
        configuration.Microphones[0].Name = new string('x', limit - Encoding.UTF8.GetByteCount(envelope) - 1);
        int acceptedBytes = 0;
        bool rejectedBeforeWrite = false;
        await WithServer(name, async (server, token) =>
        {
            using var reader = Reader(server);
            string line = await reader.ReadLineAsync(token).ConfigureAwait(false)
                ?? throw new EndOfStreamException("The boundary request was not sent.");
            acceptedBytes = Encoding.UTF8.GetByteCount(line) + 1;
            using var request = JsonDocument.Parse(line);
            await Write(server, Reply(request.RootElement.GetProperty("requestId").GetString()!), token).ConfigureAwait(false);
            // The second request is one byte too large. Local rejection closes
            // the connection before sending even a partial line to the server.
            rejectedBeforeWrite = await reader.ReadLineAsync(token).ConfigureAwait(false) is null;
        }, async token =>
        {
            var accepted = await connection.RequestAsync("configure", configuration, null, token).ConfigureAwait(false);
            require(accepted.Ok, "An outbound control line totalling exactly 262144 UTF-8 bytes, including its newline, is accepted.");
            configuration.Microphones[0].Name += "x";
            var error = await Failure(connection.RequestAsync("configure", configuration, null, token)).ConfigureAwait(false);
            require(error is InvalidDataException,
                "An outbound control line totalling 262145 bytes is rejected locally, including the newline in its size limit.");
        }).ConfigureAwait(false);
        require(acceptedBytes == limit, "The private boundary fixture observes exactly 262144 bytes on the accepted request's wire line.");
        require(rejectedBeforeWrite, "The oversized outbound request sends no partial JSON or newline before disconnecting.");
    }

    private static async Task MalformedResponses(Action<bool, string> require)
    {
        string name = PrivateName();
        using var connection = new EngineConnection(name);
        var cases = new (string Name, Func<string, string> Response)[]
        {
            ("a mismatched request identity", _ => Reply("wrong-request")),
            ("an incompatible protocol", id => $"{{\"protocolVersion\":2,\"requestId\":\"{id}\",\"ok\":true}}\n"),
            ("a null configuration", id => $"{{\"protocolVersion\":1,\"requestId\":\"{id}\",\"ok\":true,\"snapshot\":{{\"configuration\":null}}}}\n"),
            ("a response exceeding 256 KiB", _ => new string('x', 256 * 1024 + 1) + "\n")
        };
        foreach (var item in cases)
        {
            await WithServer(name, async (server, token) =>
            {
                using var reader = Reader(server);
                using var request = await ReadRequest(reader, token).ConfigureAwait(false);
                await Write(server, item.Response(request.RootElement.GetProperty("requestId").GetString()!), token).ConfigureAwait(false);
            }, async token =>
            {
                Exception? failure = await Failure(connection.RequestAsync("snapshot", null, null, token)).ConfigureAwait(false);
                require(failure is InvalidDataException, "The private-pipe client rejects " + item.Name + " as a bounded protocol error.");
            }, allowClosedWrite: true).ConfigureAwait(false);
            await SuccessfulReconnect(name, connection, require).ConfigureAwait(false);
        }
        await WithServer(name, async (server, token) =>
        {
            using var reader = Reader(server);
            using var request = await ReadRequest(reader, token).ConfigureAwait(false);
            server.Disconnect();
        }, async token =>
        {
            var error = await Failure(connection.RequestAsync("snapshot", null, null, token)).ConfigureAwait(false);
            require(error is EndOfStreamException or IOException, "A closed private pipe fails promptly rather than waiting for an unlimited response.");
        }).ConfigureAwait(false);
        await SuccessfulReconnect(name, connection, require).ConfigureAwait(false);
    }

    private static async Task SuccessfulReconnect(string name, EngineConnection connection, Action<bool, string> require)
    {
        await WithServer(name, async (server, token) =>
        {
            using var reader = Reader(server);
            using var request = await ReadRequest(reader, token).ConfigureAwait(false);
            await Write(server, Reply(request.RootElement.GetProperty("requestId").GetString()!, "recovered"), token).ConfigureAwait(false);
            // Make the successful reply the last exchange of this connection;
            // the next malformed fixture must start from a known disconnect.
            using var next = await ReadRequest(reader, token).ConfigureAwait(false);
            await Write(server, Reply("intentional-reset"), token).ConfigureAwait(false);
        }, async token =>
        {
            var value = await connection.RequestAsync("snapshot", null, null, token).ConfigureAwait(false);
            require(value.Ok && value.Snapshot?.Warning == "recovered",
                "The same connection reconnects after a rejected or cancelled response and discards all old receive bytes.");
            var reset = await Failure(connection.RequestAsync("snapshot", null, null, token)).ConfigureAwait(false);
            require(reset is InvalidDataException, "The private reconnect fixture closes its connection cleanly through protocol rejection.");
        }).ConfigureAwait(false);
    }

    private static async Task CancelAndReconnect(Action<bool, string> require)
    {
        string name = PrivateName();
        using var connection = new EngineConnection(name);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await WithServer(name, async (server, token) =>
        {
            using var reader = Reader(server);
            using var request = await ReadRequest(reader, token).ConfigureAwait(false);
            received.TrySetResult();
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
        }, async token =>
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pending = connection.RequestAsync("snapshot", null, null, cancel.Token);
            await received.Task.WaitAsync(token).ConfigureAwait(false);
            var timer = Stopwatch.StartNew(); cancel.Cancel();
            var error = await Failure(pending.WaitAsync(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
            require(error is OperationCanceledException && timer.Elapsed < TimeSpan.FromSeconds(1),
                "Cancelling a pending private-pipe read completes promptly and frees the serialized request gate.");
        }, drain: false).ConfigureAwait(false);
        await SuccessfulReconnect(name, connection, require).ConfigureAwait(false);
    }

    private static async Task DisposePendingRequests(Action<bool, string> require)
    {
        foreach (bool blockWrite in new[] { false, true })
        {
            string name = PrivateName();
            using var connection = new EngineConnection(name);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await WithServer(name, async (server, token) =>
            {
                if (!blockWrite)
                {
                    using var reader = Reader(server);
                    using var request = await ReadRequest(reader, token).ConfigureAwait(false);
                }
                ready.TrySetResult();
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            }, async token =>
            {
                MixerConfiguration? configuration = null;
                if (blockWrite) { configuration = new MixerConfiguration(); configuration.Microphones[0].Name = new string('x', 220000); }
                var pending = connection.RequestAsync("snapshot", configuration, null, token);
                var queued = connection.RequestAsync("snapshot", null, null, token);
                await ready.Task.WaitAsync(token).ConfigureAwait(false);
                if (blockWrite) await Task.Delay(20, token).ConfigureAwait(false);
                var timer = Stopwatch.StartNew(); connection.Dispose();
                require(timer.Elapsed < TimeSpan.FromSeconds(1),
                    "Disposing a private pipe during a pending " + (blockWrite ? "write" : "read") + " never synchronously waits for the fake server.");
                var activeError = await Failure(pending.WaitAsync(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                var queuedError = await Failure(queued.WaitAsync(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                require(activeError is OperationCanceledException or ObjectDisposedException or IOException,
                    "A disposed in-flight request ends with a transport/cancellation exception, never a shared-field null-reference failure.");
                require(queuedError is ObjectDisposedException,
                    "Disposal releases queued private-pipe callers and prevents reopening the disposed connection.");
            }, drain: false).ConfigureAwait(false);
        }
        using var connecting = new EngineConnection(PrivateName());
        var connect = connecting.RequestAsync("snapshot", null, null, CancellationToken.None);
        connecting.Dispose();
        var failure = await Failure(connect.WaitAsync(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        require(failure is OperationCanceledException or ObjectDisposedException or IOException,
            "Disposing during connection establishment cancels the private-pipe attempt without a timeout or leaked pending task.");
        var after = await Failure(connecting.RequestAsync("snapshot", null, null, CancellationToken.None)).ConfigureAwait(false);
        require(after is ObjectDisposedException, "Requests made after disposal are rejected immediately.");
    }

    private static async Task<Exception?> Failure(Task task)
    {
        try { await task.ConfigureAwait(false); return null; }
        catch (Exception error) { return error; }
    }
}
