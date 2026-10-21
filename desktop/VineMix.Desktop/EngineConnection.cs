using System;
using System.Buffers;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VineMix.Desktop;

public sealed class EngineConnection : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly byte[] _receive = new byte[4096];
    private readonly ArrayBufferWriter<byte> _response = new(4096);
    private readonly string _pipeName;
    private readonly string? _expectedEnginePath;
    private bool _identityVerified;
    private string? _physicalSettingsPath;
    private int _serverProcessId;
    private readonly CancellationTokenSource _closed = new();
    private readonly CancellationToken _closedToken;
    private int _disposed;
    public static string UserSid
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new InvalidOperationException("No Windows user identity is available."); }
    }
    public EngineConnection() : this("VineMix.Control." + UserSid,
        EngineSupervisor.RuntimeEnginePath ?? throw new InvalidOperationException("The VineMix runtime was not initialized.")) { }
    // Verification uses its own random pipe, never the running engine's endpoint.
    internal EngineConnection(string pipeName, string? expectedEnginePath = null)
    { _pipeName = pipeName; _expectedEnginePath = expectedEnginePath; _closedToken = _closed.Token; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    private void ValidateServer(NamedPipeClientStream pipe)
    {
        if (_expectedEnginePath is null) return; // Explicit private verification endpoint.
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint id)) throw new IOException("The audio engine owner could not be verified.");
        _serverProcessId = checked((int)id);
        using var process = Process.GetProcessById(checked((int)id));
        using var current = Process.GetCurrentProcess();
        if (process.SessionId != current.SessionId || !string.Equals(process.MainModule?.FileName, _expectedEnginePath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Another VineMix version owns this connection. Quit that version before switching.");
    }

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
                ValidateServer(pipe);
                writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { NewLine = "\n", AutoFlush = true };
                _writer = writer;
            }
            // Every new transport proves workspace identity with a read-only
            // snapshot before a first configure, recovery or shutdown request.
            if (_expectedEnginePath is not null && !_identityVerified && command != "snapshot")
            {
                var handshake = await Exchange("snapshot", null, null).ConfigureAwait(false);
                if (!handshake.Ok || handshake.Snapshot is null || !_identityVerified)
                    throw new IOException("The engine did not verify its workspace before the requested action.");
            }
            return await Exchange(command, configuration, revision).ConfigureAwait(false);

            async Task<ProtocolResponse> Exchange(string operation, MixerConfiguration? candidate, ulong? expected)
            {
            string id = Guid.NewGuid().ToString("N");
            var message = JsonSerializer.Serialize(new { protocolVersion = 1, requestId = id, command = operation, configuration = candidate, expectedRevision = expected }, Protocol.Json);
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
            if (_expectedEnginePath is not null && response.Snapshot is { } owned
                && !string.Equals(Path.GetFullPath(owned.SettingsPath), Path.GetFullPath(WorkspacePaths.Settings), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The engine uses a different workspace. Quit the current session before opening another settings file.");
            if (_expectedEnginePath is not null && response.Snapshot is { } identity)
            {
                if (identity.EngineProtocolVersion != 1 || identity.EngineVersion != ReleaseInfo.Version || identity.EnginePid != _serverProcessId
                    || !string.Equals(identity.ExecutablePath, _expectedEnginePath, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The audio engine does not match this VineMix release.");
                _physicalSettingsPath ??= WorkspacePhysicalPath.Resolve(WorkspacePaths.Settings);
                if (!string.Equals(WorkspacePhysicalPath.Normalize(identity.PhysicalSettingsPath), _physicalSettingsPath, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The engine opened a different physical settings location. Saving has been blocked to protect your workspace.");
                _identityVerified = true;
                EngineSupervisor.ConfirmEngineIdentity(identity.EnginePid);
            }
            return response;
            }
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
        _identityVerified = false; _physicalSettingsPath = null;
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
