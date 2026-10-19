using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace AudioMixer.Desktop;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(name); return true;
    }
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record EngineRestartResult(bool Success, string Message);

// Detached verification supplies all operations; constructing it never creates
// a process, control pipe, preferences file or Windows registration.
internal sealed record EngineRestartOperations(
    Func<string, MixerConfiguration?, ulong?, CancellationToken, Task<ProtocolResponse>> Request,
    Func<CancellationToken, Task> Restart, Action ResetConnection)
{
    internal Action ValidateTarget { get; init; } = () => { };
    internal Func<bool> IsRunning { get; init; } = () => true;
    internal TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(15);
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);
}

public sealed class MixerViewModel : Observable, IDisposable
{
    private readonly bool _preview;
    private EngineConnection? _connection;
    private readonly EngineSupervisor? _supervisor;
    private readonly EngineRestartOperations? _restartOperations;
    private readonly SemaphoreSlim _engineGate = new(1, 1);
    private bool _restartingEngine;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<PendingEdit> _pending = [];
    private readonly HashSet<string> _automaticAssignmentAttempts = [];
    private readonly HashSet<string> _automaticAssignmentSuppressedNodes = [];
    private readonly EditDispatchCadence _editDispatch = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Task? _loop;
    private MixerSnapshot? _snapshot;
    private MixerConfiguration _desired = new();
    private (double X, double Y, double Zoom)? _canvasView;
    public WorkspaceHistory EditHistory { get; } = new();
    public bool CanUndo => CanEdit && EditHistory.CanUndo;
    public bool CanRedo => CanEdit && EditHistory.CanRedo;
    public string UndoDescription => EditHistory.UndoDescription;
    public string RedoDescription => EditHistory.RedoDescription;
    // Detached verification can exercise the real pending/acknowledgement path
    // without creating a pipe, changing preferences or starting an engine.
    internal bool QueuePreviewEdits { get; set; }
    internal int PendingEditCount => _pending.Count;
    internal delegate bool CheckedEdit(MixerConfiguration configuration, out string error);
    internal sealed class PendingEdit
    {
        internal Action<MixerConfiguration>? Change { get; init; }
        internal CheckedEdit? CheckedChange { get; init; }
        internal WorkspaceGraphRestore? Restore { get; init; }
        internal bool TryApply(MixerConfiguration configuration, out string error)
        {
            error = "";
            if (CheckedChange is not null) return CheckedChange(configuration, out error);
            if (Restore is null) { Change!(configuration); return true; }
            if (!Restore.TryApply(configuration.Graph, out var restored, out error)) return false;
            configuration.Graph = restored;
            return true;
        }
    }
    internal sealed record PendingBatch(MixerConfiguration Configuration, IReadOnlyList<PendingEdit> Edits);
    private double _lastFrameAdvance, _lastPoll;
    private ulong _lastFrames;
    private bool _connected, _visible = true, _startAtLogin, _reducedAnimation, _rightClickDragToPan;
    private bool _showDisabledAudioDevices, _showDisconnectedAudioDevices, _showAdvancedAudioDevices;
    private bool? _registeredStartup;
    private string _connectionStatus = "Connecting to the audio engine…", _warning = "", _editError = "", _startupError = "", _uiError = "", _saveStatus = "", _frameLabel = "Engine offline";
    private List<Endpoint> _endpoints = [];
    private List<AudioApplication> _applications = [];
    public bool Applying { get; private set; }
    public bool IsPreview => _preview;
    internal long ConfigurationRefreshCount { get; private set; }
    internal long TelemetryOnlySnapshotCount { get; private set; }
    internal long MeterSampleSequence { get; private set; }
    internal ulong MeterAudioFrames => _snapshot?.Status.AudioFrames ?? 0;
    internal GraphNode? AcknowledgedGraphNode(string id) => _snapshot?.Configuration.Graph.Nodes.FirstOrDefault(node => node.Id == id);
    internal long ConfigurationPresentationCount { get; private set; }
    public bool CanEdit { get => _connected && !IsRestartingEngine; private set { if (Set(ref _connected, value)) NotifyHistory(); } }
    public bool IsRestartingEngine => _restartingEngine;
    public bool CanRestartEngine => _restartOperations is not null && !IsRestartingEngine && !_stop.IsCancellationRequested
        && (_preview || _supervisor?.CanRestart == true);
    public bool IsVisible { get => _visible; set => _visible = value; }
    public string ConnectionStatus { get => _connectionStatus; private set => Set(ref _connectionStatus, value); }
    public string Warning { get => _warning; private set => Set(ref _warning, value); }
    public string SaveStatus { get => _saveStatus; private set => Set(ref _saveStatus, value); }
    public string FrameLabel { get => _frameLabel; private set => Set(ref _frameLabel, value); }
    public string SettingsPath => _preview ? "Preview · no settings file" : _snapshot?.SettingsPath ?? "Not connected";
    public string LogPath => _preview ? "Preview · no engine log" : _snapshot?.LogPath ?? "Not connected";
    public string EnginePath => _preview ? "Preview · no audio engine" : _supervisor!.EnginePath;
    public List<Endpoint> Endpoints { get => _endpoints; private set => Set(ref _endpoints, value); }
    public GraphConfiguration GraphConfig => _desired.Graph;
    public IReadOnlyList<GraphNodeStatus> GraphStatus => _snapshot?.Status.Nodes ?? [];
    public List<AudioApplication> Applications => _applications;
    public event EventHandler? GraphChanged;
    public bool StartAtLogin { get => _startAtLogin; set { if (CanEdit && !Applying && Set(ref _startAtLogin, value)) Edit(c => c.StartAtLogin = value); } }
    public bool ReducedAnimation
    {
        get => _reducedAnimation;
        set
        {
            if (Applying || !Set(ref _reducedAnimation, value)) return;
            SaveUiPreferences();
        }
    }
    public bool RightClickDragToPan
    {
        get => _rightClickDragToPan;
        set
        {
            if (Applying || !Set(ref _rightClickDragToPan, value)) return;
            SaveUiPreferences();
        }
    }
    public bool ShowDisabledAudioDevices
    {
        get => _showDisabledAudioDevices;
        set => SetDeviceVisibility(ref _showDisabledAudioDevices, value);
    }
    public bool ShowDisconnectedAudioDevices
    {
        get => _showDisconnectedAudioDevices;
        set => SetDeviceVisibility(ref _showDisconnectedAudioDevices, value);
    }
    public bool ShowAdvancedAudioDevices
    {
        get => _showAdvancedAudioDevices;
        set => SetDeviceVisibility(ref _showAdvancedAudioDevices, value);
    }
    private void SetDeviceVisibility(ref bool field, bool value, [CallerMemberName] string? property = null)
    {
        if (Applying || !Set(ref field, value, property)) return;
        SaveUiPreferences();
        // List visibility is a local preference: refresh the pickers without
        // sending a configure request or adding an undo/history operation.
        InvalidatePresentation();
    }

    public MixerViewModel(bool preview = false)
    {
        _preview = preview;
        if (!_preview)
        {
            _connection = new(); _supervisor = new();
            _restartOperations = new((command, configuration, revision, token) => _connection!.RequestAsync(command, configuration, revision, token),
                _supervisor.RestartExplicitAsync, () => { _connection?.Dispose(); _connection = new(); })
            { ValidateTarget = _supervisor.ValidateRestartTarget, IsRunning = () => _supervisor.IsRunning };
        }
        _reducedAnimation = !SystemParameters.ClientAreaAnimation;
        if (!_preview)
        {
            try
            {
                if (File.Exists(UiPreferencesPath))
                {
                    LoadUiPreferencesJson(File.ReadAllText(UiPreferencesPath));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException)
            { _uiError = "Workspace preferences could not be read. Default navigation, device visibility and Windows animation preferences are being used."; }
        }
    }
    internal MixerViewModel(EngineRestartOperations operations) : this(preview: true)
    {
        _restartOperations = operations;
        QueuePreviewEdits = true;
    }
    private sealed class UiPreferences
    {
        public bool? ReducedAnimation { get; set; }
        public bool RightClickDragToPan { get; set; }
        public bool ShowDisabledAudioDevices { get; set; }
        public bool ShowDisconnectedAudioDevices { get; set; }
        public bool ShowAdvancedAudioDevices { get; set; }
    }
    // These JSON-only seams also let detached verification exercise the same
    // persistence format without reading or writing the user's settings file.
    internal void LoadUiPreferencesJson(string json)
    {
        var preferences = JsonSerializer.Deserialize<UiPreferences>(json, Protocol.Json);
        _reducedAnimation = preferences?.ReducedAnimation ?? _reducedAnimation;
        _rightClickDragToPan = preferences?.RightClickDragToPan ?? false;
        _showDisabledAudioDevices = preferences?.ShowDisabledAudioDevices ?? false;
        _showDisconnectedAudioDevices = preferences?.ShowDisconnectedAudioDevices ?? false;
        _showAdvancedAudioDevices = preferences?.ShowAdvancedAudioDevices ?? false;
    }
    internal string SerializeUiPreferences() => JsonSerializer.Serialize(new UiPreferences
    {
        ReducedAnimation = _reducedAnimation,
        RightClickDragToPan = _rightClickDragToPan,
        ShowDisabledAudioDevices = _showDisabledAudioDevices,
        ShowDisconnectedAudioDevices = _showDisconnectedAudioDevices,
        ShowAdvancedAudioDevices = _showAdvancedAudioDevices
    }, Protocol.Json);
    private static string UiPreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioMixer", "ui.json");
    private void SaveUiPreferences()
    {
        if (_preview) return;
        try
        {
            string path = UiPreferencesPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".new";
            File.WriteAllText(temporary, SerializeUiPreferences());
            File.Move(temporary, path, true);
            _uiError = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { _uiError = "Workspace preferences apply for this session, but could not be saved: " + error.Message; }
        UpdateWarning();
    }
    private void UpdateWarning() => Warning = string.Join("\n", new[] { _editError, _startupError, _uiError, _snapshot?.Warning ?? "" }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public void Start() { if (!_preview) _loop ??= PollAsync(); }

    // The inert preview accepts supplied design data only. It never opens the
    // control pipe, starts/restarts an engine or changes Windows registration.
    public void ApplyPreviewSnapshot(MixerSnapshot snapshot)
    {
        if (!_preview) throw new InvalidOperationException("Design data can only be applied to a preview model.");
        if (!snapshot.Configuration.IsValidShape) throw new ArgumentException("Preview configuration has an unsupported shape.", nameof(snapshot));
        if (!QueuePreviewEdits) { EditHistory.Clear(); NotifyHistory(); }
        _canvasView = null;
        Accept(snapshot);
    }

    public void RefreshPreviewTelemetry()
    {
        if (!_preview) throw new InvalidOperationException("Design telemetry can only be refreshed in a preview model.");
        if (!CanEdit || _snapshot is null) return;
        MeterSampleSequence++;
        ApplyDesired(configurationChanged: false);
    }

    public void Edit(Action<MixerConfiguration> change) => ApplyEdit(new() { Change = change }, out _);

    internal bool TryEdit(CheckedEdit change, out string error) => ApplyEdit(new() { CheckedChange = change }, out error);

    private bool ApplyEdit(PendingEdit operation, out string error)
    {
        error = "";
        if (Applying || !CanEdit) { error = "Wait for the audio engine before changing the workspace."; return false; }
        var before = _desired.Copy();
        // A checked operation may refuse a changing destination. Publish its
        // result only after success, with no partial graph or history on failure.
        var candidate = operation.CheckedChange is null ? _desired : _desired.Copy();
        if (!operation.TryApply(candidate, out error)) return false;
        _desired = candidate;
        var assignments = AutomaticAssignments();
        VirtualDeviceBindings.Apply(_desired.Graph, assignments);
        foreach (var assignment in assignments) _automaticAssignmentAttempts.Add(assignment.AttemptKey);
        if (WorkspaceHistory.SameValue(before, _desired)) return true;
        EditHistory.Record(before.Graph, _desired.Graph);
        NotifyHistory();
        if (_preview && !QueuePreviewEdits)
        {
            SaveStatus = "Preview · changes stay in this window";
            ApplyDesired();
            return true;
        }
        var view = _canvasView;
        void Complete(MixerConfiguration c)
        {
            VirtualDeviceBindings.Apply(c.Graph, assignments);
            if (view is { } v) { c.Graph.PanX = v.X; c.Graph.PanY = v.Y; c.Graph.Zoom = v.Zoom; }
        }
        _pending.Add(operation.CheckedChange is null
            ? new() { Change = c => { operation.Change!(c); Complete(c); } }
            : new() { CheckedChange = (MixerConfiguration c, out string replayError) =>
                { if (!operation.TryApply(c, out replayError)) return false; Complete(c); return true; } });
        _editDispatch.Queue(_clock.Elapsed);
        _editError = "";
        SaveStatus = "Saving changes…";
        ApplyDesired();
        return true;
    }

    private VirtualDeviceBindings.Assignment[] AutomaticAssignments()
        => VirtualDeviceBindings.FindAutomaticAssignments(_desired.Graph, _endpoints)
            .Where(assignment => !_automaticAssignmentSuppressedNodes.Contains(assignment.NodeId)).ToArray();

    public void BeginEditGesture(string? description = null)
    {
        if (!Applying && CanEdit) EditHistory.BeginGesture(description);
    }
    public void EndEditGesture() { EditHistory.EndGesture(); NotifyHistory(); }
    private void NotifyHistory()
    {
        Changed(nameof(CanUndo)); Changed(nameof(CanRedo));
        Changed(nameof(UndoDescription)); Changed(nameof(RedoDescription));
    }

    public bool Undo() => RestoreHistory(undo: true);
    public bool Redo() => RestoreHistory(undo: false);
    private bool RestoreHistory(bool undo)
    {
        if (Applying || !CanEdit) return false;
        GraphConfiguration restored; WorkspaceGraphRestore? restore; string error;
        bool changed = undo ? EditHistory.TryUndo(_desired.Graph, out restored, out restore, out error)
            : EditHistory.TryRedo(_desired.Graph, out restored, out restore, out error);
        if (!changed)
        {
            if (error.Length > 0) ResetHistoryAfterConflict(error);
            NotifyHistory(); return false;
        }
        var previousGraph = _desired.Graph;
        _desired.Graph = restored;
        // An explicit restoration of an unassigned node is authoritative for
        // this session, even if inventory changes would otherwise reassign it.
        foreach (var node in restored.Nodes.Where(node => VirtualDeviceBindings.IsVirtual(node.Kind)))
        {
            var previous = previousGraph.Nodes.FirstOrDefault(other => other.Id == node.Id);
            if (previous is not null && WorkspaceHistory.SameValue(previous.Device, node.Device)) continue;
            if (VirtualDeviceBindings.IsAssigned(node.Device)) _automaticAssignmentSuppressedNodes.Remove(node.Id);
            else _automaticAssignmentSuppressedNodes.Add(node.Id);
        }
        _editError = "";
        if (_preview && !QueuePreviewEdits) SaveStatus = "Preview · changes stay in this window";
        else
        {
            _pending.Add(new() { Restore = restore });
            _editDispatch.Queue(_clock.Elapsed);
            SaveStatus = "Saving changes…";
        }
        UpdateWarning(); NotifyHistory(); ApplyDesired();
        return true;
    }

    private void ResetHistoryAfterConflict(string detail)
    {
        EditHistory.Clear();
        _editError = "Undo and redo were reset because the workspace changed. " + detail;
        NotifyHistory(); UpdateWarning();
    }

    private MixerConfiguration ReplayPending(MixerConfiguration configuration)
    {
        bool conflict = false;
        foreach (var edit in _pending.ToArray())
        {
            if (conflict && edit.Restore is not null) { _pending.Remove(edit); continue; }
            var candidate = edit.CheckedChange is null ? configuration : configuration.Copy();
            if (edit.TryApply(candidate, out string error)) { configuration = candidate; continue; }
            // A refused operation must not leave later dependent redo/undo
            // commands in the queue. Ordinary newer edits remain rebasable.
            _pending.Remove(edit); conflict = true; ResetHistoryAfterConflict(error);
        }
        if (conflict) _editDispatch.Complete(_pending.Count > 0);
        return configuration;
    }

    internal PendingBatch PreparePendingConfiguration(MixerConfiguration basis)
    {
        var configuration = ReplayPending(basis.Copy());
        if (_canvasView is { } view) { configuration.Graph.PanX = view.X; configuration.Graph.PanY = view.Y; configuration.Graph.Zoom = view.Zoom; }
        return new(configuration, _pending.ToArray());
    }

    internal void AcknowledgePending(PendingBatch batch, MixerSnapshot? snapshot)
    {
        // Use identities, not a prefix count: newer edits or retry snapshots
        // can arrive while the configure request is awaiting its response.
        _pending.RemoveAll(edit => batch.Edits.Contains(edit));
        _editDispatch.Complete(_pending.Count > 0);
        if (snapshot is not null) Accept(snapshot, forceConfiguration: true);
    }

    internal void RejectPending(PendingBatch batch, MixerSnapshot snapshot, string error)
    {
        _pending.RemoveAll(edit => batch.Edits.Contains(edit) || edit.Restore is not null);
        _editDispatch.Complete(_pending.Count > 0);
        EditHistory.Clear(); NotifyHistory();
        _editError = "The engine rejected a change: " + error + " Undo and redo were reset to keep the saved workspace consistent.";
        Accept(snapshot, forceConfiguration: true);
    }

    internal void SetCanvasView(double x, double y, double zoom)
    {
        // Navigation never queues a configure request or creates an edit entry.
        // The next actual edit may include the latest view in its one save.
        _canvasView = (x, y, zoom);
        _desired.Graph.PanX = x; _desired.Graph.PanY = y; _desired.Graph.Zoom = zoom;
    }

    private void ApplyDesired(bool configurationChanged = true)
    {
        if (_snapshot is null) return;
        if (configurationChanged) ConfigurationPresentationCount++;
        Applying = true;
        try
        {
            if (configurationChanged) Set(ref _startAtLogin, _desired.StartAtLogin, nameof(StartAtLogin));
        }
        finally { Applying = false; }
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static bool RequiresConfigurationRefresh(MixerSnapshot? previous, MixerSnapshot next, bool preview, bool connected, bool force)
        => preview || force || previous is null || !connected
            || previous.Configuration.Revision != next.Configuration.Revision
            || next.Status.AudioFrames < previous.Status.AudioFrames
            || !string.Equals(previous.SettingsPath, next.SettingsPath, StringComparison.OrdinalIgnoreCase)
            || previous.Configuration.StartAtLogin != next.Configuration.StartAtLogin;

    private void Accept(MixerSnapshot snapshot, bool forceConfiguration = false)
    {
        if (snapshot.Configuration.Version == 1)
        {
            if (!_preview) throw new InvalidOperationException("The running engine needs the node-workspace update. Quit Audio Mixer and reopen the new version.");
            snapshot.Configuration.Graph = GraphDefaults.FromLegacy(snapshot.Configuration);
            snapshot.Configuration.Version = 2;
        }
        bool initial = _snapshot is null;
        bool configurationChanged = RequiresConfigurationRefresh(_snapshot, snapshot, _preview, CanEdit, forceConfiguration);
        if (configurationChanged)
        {
            // Rebase local edits only when the committed base changes. Each
            // optimistic edit already updated _desired, so telemetry cannot
            // require another full JSON round trip or another pending replay.
            var desired = ReplayPending(snapshot.Configuration.Copy());
            if (_canvasView is { } view) { desired.Graph.PanX = view.X; desired.Graph.PanY = view.Y; desired.Graph.Zoom = view.Zoom; }
            _desired = desired;
            ConfigurationRefreshCount++;
        }
        else TelemetryOnlySnapshotCount++;
        if (_preview || !ReferenceEquals(_snapshot?.Status, snapshot.Status)) MeterSampleSequence++;
        _snapshot = snapshot; CanEdit = true;
        // Keep inventory identity stable between discovery changes. Node
        // selectors can reuse their choices on ordinary meter refreshes.
        bool inventoryChanged = !InventoryEquals(_endpoints, snapshot.Endpoints);
        if (inventoryChanged)
        {
            Endpoints = snapshot.Endpoints;
            _automaticAssignmentAttempts.Clear();
        }
        if (!ApplicationInventoryEquals(_applications, snapshot.Applications))
        { _applications = snapshot.Applications; Changed(nameof(Applications)); }
        ConnectionStatus = _preview ? "Preview · no audio engine" : "Audio engine connected";
        FrameLabel = $"{snapshot.Status.AudioFrames:N0} frames · 48 kHz";
        if (_pending.Count == 0) SaveStatus = _preview ? "Preview · no settings are saved" : _editError.Length == 0 ? $"Saved · revision {snapshot.Configuration.Revision}" : "Change was not saved";
        if (configurationChanged || inventoryChanged)
        {
            var assignments = AutomaticAssignments()
                .Where(a => _preview || !_automaticAssignmentAttempts.Contains(a.AttemptKey)).ToArray();
            if (assignments.Length > 0)
            {
                VirtualDeviceBindings.Apply(_desired.Graph, assignments);
                foreach (var assignment in assignments) _automaticAssignmentAttempts.Add(assignment.AttemptKey);
                if (!_preview || QueuePreviewEdits)
                {
                    _pending.Add(new() { Change = c => VirtualDeviceBindings.Apply(c.Graph, assignments) });
                    _editDispatch.Queue(_clock.Elapsed);
                }
                SaveStatus = _preview ? "Preview · changes stay in this window" : "Saving changes…";
                configurationChanged = true;
            }
        }
        if (configurationChanged && !EditHistory.IsCompatible(_desired.Graph))
            ResetHistoryAfterConflict("Another change affected a node or connection in the edit history.");
        UpdateWarning();
        if (initial) { Changed(nameof(SettingsPath)); Changed(nameof(LogPath)); }
        ApplyDesired(configurationChanged);
        if (!_preview && _registeredStartup != snapshot.Configuration.StartAtLogin)
        {
            try { StartupRegistration.SetEnabled(snapshot.Configuration.StartAtLogin); _registeredStartup = snapshot.Configuration.StartAtLogin; _startupError = ""; }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            { _startupError = "Could not update start at login: " + error.Message; UpdateWarning(); _registeredStartup = snapshot.Configuration.StartAtLogin; }
        }
    }

    private void InvalidatePresentation()
    {
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool InventoryEquals(IReadOnlyList<Endpoint> left, IReadOnlyList<Endpoint> right)
    {
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
        {
            var a = left[i]; var b = right[i];
            if (a.Id != b.Id || a.StableId != b.StableId || a.Name != b.Name || a.Direction != b.Direction
                || a.Description != b.Description || a.InterfaceName != b.InterfaceName || a.FormFactor != b.FormFactor
                || a.State != b.State || a.Channels != b.Channels || a.Rate != b.Rate
                || a.IsVirtual != b.IsVirtual || a.Transport != b.Transport) return false;
        }
        return true;
    }

    private static bool ApplicationInventoryEquals(IReadOnlyList<AudioApplication> left, IReadOnlyList<AudioApplication> right)
    {
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
            if (left[i].ProcessId != right[i].ProcessId || left[i].Name != right[i].Name
                || left[i].ExecutablePath != right[i].ExecutablePath) return false;
        return true;
    }

    private void SetRestarting(bool value)
    {
        if (!Set(ref _restartingEngine, value, nameof(IsRestartingEngine))) return;
        Changed(nameof(CanRestartEngine)); Changed(nameof(CanEdit)); NotifyHistory();
        InvalidatePresentation();
    }

    private static bool RestartFailure(Exception error) => error is IOException or TimeoutException or OperationCanceledException
        or InvalidOperationException or JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;

    private PendingBatch PrepareRestartConfiguration(MixerConfiguration basis)
    {
        var configuration = basis.Copy();
        foreach (var edit in _pending)
        {
            var candidate = configuration.Copy();
            if (!edit.TryApply(candidate, out string error))
                throw new InvalidOperationException("Queued changes could not be reapplied: " + error);
            configuration = candidate;
        }
        if (_canvasView is { } view) { configuration.Graph.PanX = view.X; configuration.Graph.PanY = view.Y; configuration.Graph.Zoom = view.Zoom; }
        return new(configuration, _pending.ToArray());
    }

    private static bool SameSavedConfiguration(MixerConfiguration expected, MixerConfiguration actual)
    {
        var left = expected.Copy(); var right = actual.Copy();
        left.Revision = right.Revision = 0;
        return WorkspaceHistory.SameValue(left, right);
    }

    public async Task<EngineRestartResult> RestartEngineAsync()
    {
        if (!CanRestartEngine) return new(false, IsRestartingEngine ? "An audio engine restart is already in progress." : "The audio engine cannot be restarted right now.");
        var operations = _restartOperations!;
        SetRestarting(true);
        bool entered = false, attemptedRestart = false;
        bool hadPending = _pending.Count > 0;
        PendingBatch? unconfirmed = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        deadline.CancelAfter(operations.Deadline);
        try
        {
            await _engineGate.WaitAsync(deadline.Token); entered = true;
            deadline.Token.ThrowIfCancellationRequested();
            operations.ValidateTarget();
            if (_pending.Count > 0)
            {
                ConnectionStatus = "Saving changes before restart…";
                using var saving = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                saving.CancelAfter(TimeSpan.FromSeconds(4));
                try { await SavePendingAsync(null, saving.Token); }
                catch (Exception error) when (RestartFailure(error) && !deadline.IsCancellationRequested)
                { SaveStatus = "Changes kept in this window; retrying after restart"; }
            }
            deadline.Token.ThrowIfCancellationRequested();
            CanEdit = false; InvalidatePresentation();
            ConnectionStatus = "Restarting the audio engine…";
            attemptedRestart = true;
            await operations.Restart(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            operations.ResetConnection();
            ConnectionStatus = "Waiting for audio processing to resume…";
            MixerSnapshot? ready = null;
            ulong? firstFrames = null;
            while (ready is null)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (!operations.IsRunning()) throw new IOException("The restarted engine exited before audio processing resumed.");
                try
                {
                    var snapshot = await ReadSnapshotAsync(deadline.Token);
                    if (firstFrames is ulong first && snapshot.Status.AudioFrames > first) ready = snapshot;
                    else firstFrames = snapshot.Status.AudioFrames;
                }
                catch (Exception error) when (RestartFailure(error) && !deadline.IsCancellationRequested)
                { firstFrames = null; }
                if (ready is null) await Task.Delay(operations.RetryDelay, deadline.Token);
            }
            if (_pending.Count > 0)
            {
                ConnectionStatus = "Restoring queued changes…";
                ready = await SavePendingAsync(ready, deadline.Token);
            }
            deadline.Token.ThrowIfCancellationRequested();
            if (!operations.IsRunning()) throw new IOException("The restarted engine exited before reconnection finished.");
            // All queued operations are acknowledged before Accept can rebase
            // configuration or reconcile edit history against the new engine.
            Accept(ready, forceConfiguration: true);
            _lastFrames = ready.Status.AudioFrames;
            _lastFrameAdvance = _lastPoll = _clock.Elapsed.TotalSeconds;
            ConnectionStatus = "Audio engine restarted";
            return new(true, hadPending && _pending.Count == 0 ? "Audio engine restarted. Your changes are saved." : "Audio engine restarted.");
        }
        catch (Exception error) when (RestartFailure(error))
        {
            if (attemptedRestart) CanEdit = false;
            string reason = error is OperationCanceledException
                ? _stop.IsCancellationRequested ? "The application is closing." : "The restart timed out before audio processing resumed."
                : error.Message;
            ConnectionStatus = "Audio engine restart did not finish";
            if (_pending.Count > 0) SaveStatus = "Changes kept in this window; waiting to save";
            return new(false, (attemptedRestart ? "Could not finish restarting the audio engine. " : "The audio engine was not restarted. ")
                + reason + (_pending.Count > 0 ? " Your unsaved changes are still queued in this window." : ""));
        }
        finally
        {
            SetRestarting(false);
            if (entered) _engineGate.Release();
        }

        async Task<MixerSnapshot> ReadSnapshotAsync(CancellationToken token)
        {
            var response = await operations.Request("snapshot", null, null, token);
            if (!response.Ok || response.Snapshot is null)
                throw new IOException(response.Error.Length > 0 ? response.Error : "The engine did not return its saved settings.");
            return response.Snapshot;
        }

        async Task<MixerSnapshot> SavePendingAsync(MixerSnapshot? current, CancellationToken token)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                current ??= await ReadSnapshotAsync(token);
                // A configure may have committed before its response was lost.
                // Recognize that exact saved result before replaying an append.
                if (unconfirmed is not null && SameSavedConfiguration(unconfirmed.Configuration, current.Configuration))
                { AcknowledgePending(unconfirmed, null); unconfirmed = null; }
                if (_pending.Count == 0) return current;
                var batch = PrepareRestartConfiguration(current.Configuration);
                unconfirmed = batch;
                var saved = await operations.Request("configure", batch.Configuration, current.Configuration.Revision, token);
                if (saved.Ok && saved.Snapshot is not null)
                {
                    AcknowledgePending(batch, null); unconfirmed = null; current = saved.Snapshot;
                    if (_pending.Count == 0) return current;
                }
                else if (!saved.Ok)
                {
                    unconfirmed = null;
                    if (!saved.Error.Contains("revision", StringComparison.OrdinalIgnoreCase)
                        && !saved.Error.Contains("stale", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("The engine could not save queued changes: " + saved.Error);
                }
                current = null;
            }
            throw new IOException("The saved workspace kept changing. Queued changes were retained.");
        }
    }

    private async Task PollAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            double began = _clock.Elapsed.TotalSeconds;
            bool entered = false;
            try
            {
                await _engineGate.WaitAsync(_stop.Token); entered = true;
                if (IsRestartingEngine) continue;
                var response = await _connection!.RequestAsync("snapshot", null, null, _stop.Token);
                if (!response.Ok || response.Snapshot is null) throw new InvalidOperationException(response.Error.Length > 0 ? response.Error : "The audio engine did not provide a snapshot.");
                bool wasConnected = CanEdit;
                Accept(response.Snapshot);
                double now = _clock.Elapsed.TotalSeconds;
                if (!wasConnected || now - _lastPoll > 8 || _lastFrames != response.Snapshot.Status.AudioFrames)
                { _lastFrameAdvance = now; _lastFrames = response.Snapshot.Status.AudioFrames; }
                _lastPoll = now;
                if (now - _lastFrameAdvance >= 6)
                {
                    CanEdit = false;
                    InvalidatePresentation();
                    ConnectionStatus = await _supervisor!.RecoverStalledAsync(_stop.Token);
                    _lastFrameAdvance = _clock.Elapsed.TotalSeconds;
                    await Task.Delay(1000, _stop.Token);
                    continue;
                }
                if (_pending.Count > 0 && _editDispatch.IsDue(_clock.Elapsed))
                {
                    var batch = PreparePendingConfiguration(response.Snapshot.Configuration);
                    if (batch.Edits.Count == 0) continue;
                    _editDispatch.BeginDispatch(_clock.Elapsed);
                    var configured = await _connection.RequestAsync("configure", batch.Configuration, response.Snapshot.Configuration.Revision, _stop.Token);
                    if (configured.Ok)
                    {
                        AcknowledgePending(batch, configured.Snapshot);
                    }
                    else if (configured.Error.Contains("revision", StringComparison.OrdinalIgnoreCase) || configured.Error.Contains("stale", StringComparison.OrdinalIgnoreCase))
                    { _editDispatch.DeferRetry(_clock.Elapsed); SaveStatus = "Reconciling changes…"; }
                    else
                    {
                        RejectPending(batch, response.Snapshot, configured.Error);
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception error) when (error is System.IO.IOException or TimeoutException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                CanEdit = false; ConnectionStatus = _supervisor!.EnsureStarted();
                FrameLabel = "Engine offline"; SaveStatus = _pending.Count > 0 ? "Changes queued until reconnection" : "Waiting for connection";
                Warning = error is TimeoutException or OperationCanceledException ? "Waiting for the engine to respond…" : error.Message;
                InvalidatePresentation();
            }
            finally { if (entered) _engineGate.Release(); }
            try
            {
                int interval = !CanEdit ? 1000 : IsVisible || _pending.Count > 0 ? 33 : 1000;
                int remaining = Math.Max(1, interval - (int)((_clock.Elapsed.TotalSeconds - began) * 1000));
                await Task.Delay(remaining, _stop.Token);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task RetryAsync()
    {
        if (_preview && _restartOperations is null) { Warning = "Preview only · no devices are retried."; return; }
        if (IsRestartingEngine || _stop.IsCancellationRequested) return;
        bool entered = false;
        try
        {
            await _engineGate.WaitAsync(_stop.Token); entered = true;
            if (IsRestartingEngine) return;
            var response = await _restartOperations!.Request("retry", null, null, _stop.Token);
            if (response.Ok && response.Snapshot is not null) Accept(response.Snapshot);
            else if (!response.Ok) Warning = response.Error;
        }
        catch (Exception error) when (error is System.IO.IOException or TimeoutException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException) { Warning = error.Message; }
        finally { if (entered) _engineGate.Release(); }
    }

    public async Task<string> StopAsync()
    {
        if (_preview)
        {
            _stop.Cancel(); Changed(nameof(CanRestartEngine));
            await _engineGate.WaitAsync();
            try { CanEdit = false; InvalidatePresentation(); return ""; }
            finally { _engineGate.Release(); }
        }
        string saveFailure = "";
        _stop.Cancel();
        Changed(nameof(CanRestartEngine));
        if (_loop is not null) await _loop;
        await _engineGate.WaitAsync();
        try
        {
        CanEdit = false;
        // Flush the last queued edits before an explicit Quit when the engine is available.
        if (_pending.Count > 0)
        {
            using var saving = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                for (int attempt = 0; attempt < 2 && _pending.Count > 0; attempt++)
                {
                    var current = await _connection!.RequestAsync("snapshot", null, null, saving.Token);
                    if (!current.Ok || current.Snapshot is null) { saveFailure = current.Error.Length > 0 ? current.Error : "The engine could not provide its saved settings."; break; }
                    var batch = PreparePendingConfiguration(current.Snapshot.Configuration);
                    if (batch.Edits.Count == 0) { saveFailure = _editError; break; }
                    var saved = await _connection.RequestAsync("configure", batch.Configuration, current.Snapshot.Configuration.Revision, saving.Token);
                    if (saved.Ok)
                    {
                        AcknowledgePending(batch, saved.Snapshot); CanEdit = false;
                        saveFailure = "";
                        break;
                    }
                    saveFailure = saved.Error;
                    if (!saved.Error.Contains("revision", StringComparison.OrdinalIgnoreCase))
                    { RejectPending(batch, current.Snapshot, saved.Error); CanEdit = false; break; }
                }
            }
            catch (Exception error) when (error is System.IO.IOException or TimeoutException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { saveFailure = error is OperationCanceledException ? "The engine did not finish saving before the shutdown deadline." : error.Message; }
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try { await _connection!.RequestAsync("shutdown", null, null, timeout.Token); }
        catch (Exception error) when (error is System.IO.IOException or TimeoutException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException) { }
        await _supervisor!.StopForQuitAsync();
        return _pending.Count > 0 || saveFailure.Length > 0 ? "The most recent settings changes could not be saved. Audio Mixer has stopped.\n\n" + (saveFailure.Length > 0 ? saveFailure : "The settings changed repeatedly during shutdown.") : "";
        }
        finally { _engineGate.Release(); }
    }
    public void Dispose() { _stop.Cancel(); _connection?.Dispose(); _supervisor?.Dispose(); _stop.Dispose(); }
}
