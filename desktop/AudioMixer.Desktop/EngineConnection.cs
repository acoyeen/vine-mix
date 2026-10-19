using System;
using System.Buffers;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AudioMixer.Desktop;

public sealed class EngineConnection : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly byte[] _receive = new byte[4096];
    private readonly ArrayBufferWriter<byte> _response = new(4096);
    private readonly string _pipeName;
    private readonly CancellationTokenSource _closed = new();
    private readonly CancellationToken _closedToken;
    private int _disposed;
    public static string UserSid
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new InvalidOperationException("No Windows user identity is available."); }
    }
    public EngineConnection() : this("AudioMixer.Control." + UserSid) { }
    // Verification uses its own random pipe, never the running engine's endpoint.
    internal EngineConnection(string pipeName) { _pipeName = pipeName; _closedToken = _closed.Token; }

    public async Task<ProtocolResponse> RequestAsync(string command, MixerConfiguration? configuration, ulong? revision, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _closedToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            var pipe = _pipe; var writer = _writer;
            if (pipe?.IsConnected != true || writer is null)
            {
                Disconnect();
                pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                _pipe = pipe;
                await pipe.ConnectAsync(500, deadline.Token).ConfigureAwait(false);
                writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { NewLine = "\n", AutoFlush = true };
                _writer = writer;
            }
            string id = Guid.NewGuid().ToString("N");
            var message = JsonSerializer.Serialize(new { protocolVersion = 1, requestId = id, command, configuration, expectedRevision = revision }, Protocol.Json);
            // The native server bounds the complete line, including its one
            // UTF-8 newline byte. Reject before writing any oversized request.
            if (Encoding.UTF8.GetByteCount(message) >= 256 * 1024) throw new InvalidDataException("The configuration exceeds the control-message limit.");
            await writer.WriteLineAsync(message.AsMemory(), deadline.Token).ConfigureAwait(false);
            // Stop reading at the protocol limit rather than allowing an unbounded response allocation.
            var line = _response; line.Clear();
            while (true)
            {
                int count = await pipe.ReadAsync(_receive.AsMemory(), deadline.Token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("The audio engine closed its control connection.");
                int newline = Array.IndexOf(_receive, (byte)'\n', 0, count);
                int take = newline >= 0 ? newline : count;
                if (line.WrittenCount + take > 256 * 1024) throw new InvalidDataException("The engine response exceeds the control-message limit.");
                _receive.AsSpan(0, take).CopyTo(line.GetSpan(take)); line.Advance(take);
                if (newline >= 0)
                {
                    if (newline + 1 != count) throw new InvalidDataException("The engine sent an unsolicited control message.");
                    break;
                }
            }
            var response = JsonSerializer.Deserialize<ProtocolResponse>(line.WrittenSpan, Protocol.Json) ?? throw new InvalidDataException("The engine returned an empty response.");
            if (response.ProtocolVersion != 1 || response.RequestId != id) throw new InvalidDataException("The engine control protocol did not match this application.");
            if (response.Snapshot is { } snapshot && (snapshot.Configuration is null || !snapshot.Configuration.IsValidShape)) throw new InvalidDataException("The engine configuration has an unsupported shape.");
            return response;
        }
        catch
        {
            Disconnect();
            throw;
        }
        finally { _gate.Release(); }
    }

    private void Disconnect()
    {
        var writer = Interlocked.Exchange(ref _writer, null);
        var pipe = Interlocked.Exchange(ref _pipe, null);
        // Close transport before disposing the writer: disposal must not
        // synchronously flush a cancelled request into an unresponsive engine.
        pipe?.Dispose();
        try { writer?.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { } // An async write was interrupted by transport closure.
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _closed.Cancel();
        Disconnect();
        _closed.Dispose();
    }
}
