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

namespace VineMix.Desktop;

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

public sealed partial class MixerViewModel : Observable, IDisposable
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
    private MixerConfiguration? _recoveryPending;
    internal bool NeedsInitialFrame { get; private set; }
    internal void InitialFrameCompleted() => NeedsInitialFrame = false;
    private MixerConfiguration _desired = new();
    private (double X, double Y, double Zoom)? _canvasView;
    public WorkspaceHistory EditHistory { get; } = new();
    public bool CanUndo => CanEdit && EditHistory.CanUndo && (!IsLayoutLocked || !EditHistory.UndoChangesLayout);
    public bool CanRedo => CanEdit && EditHistory.CanRedo && (!IsLayoutLocked || !EditHistory.RedoChangesLayout);
    public string UndoDescription => EditHistory.UndoDescription;
    public string RedoDescription => EditHistory.RedoDescription;
    // Detached verification can exercise the real pending/acknowledgement path
    // without creating a pipe, changing preferences or starting an engine.
    internal bool QueuePreviewEdits { get; set; }
    // An optional in-memory writer exercises preference failures in detached
    // verification; ordinary preview models never touch the filesystem.
    internal Action<string>? PreviewUiPreferencesWriter { get; set; }
    private bool _uiPreferencesNeedSave;
    internal int PendingEditCount => _pending.Count;
    internal delegate bool CheckedEdit(MixerConfiguration configuration, out string error);
    internal sealed class PendingEdit
    {
        internal Action<MixerConfiguration>? Change { get; init; }
        internal CheckedEdit? CheckedChange { get; init; }
        internal WorkspaceGraphRestore? Restore { get; init; }
        internal IReadOnlyList<GraphFrame>? FramesBefore { get; init; }
        internal IReadOnlyList<GraphFrame>? FramesAfter { get; init; }
        internal bool TryApply(MixerConfiguration configuration, out string error)
        {
            error = "";
            if (CheckedChange is not null) return CheckedChange(configuration, out error);
            if (Restore is null) { Change!(configuration); return true; }
            if (!Restore.TryApply(configuration.Graph, out var restored, out error, restoreFrames: false)) return false;
            configuration.Graph = restored;
            return true;
        }
    }
    internal sealed record PendingBatch(MixerConfiguration Configuration, IReadOnlyList<PendingEdit> Edits);
    private double _lastFrameAdvance, _lastPoll;
    private ulong _lastFrames;
    private bool _connected, _visible = true, _startAtLogin, _reducedAnimation, _rightClickDragToPan;
    private bool _showDisabledAudioDevices, _showDisconnectedAudioDevices, _showAdvancedAudioDevices;
    private bool _showActionNotifications = true;
    private bool? _registeredStartup;
    private string _connectionStatus = "Connecting to the audio engine…", _warning = "", _editError = "", _saveError = "", _startupError = "", _uiError = "", _saveStatus = "", _frameLabel = "Engine offline";
    private string PendingSaveStatus => _saveError.Length > 0 ? "Changes not saved · retrying…" : "Saving changes…";
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
    public bool CanEdit { get => _connected && !IsRestartingEngine && !IsPreparingUpdate && !IsPreparingQuit && !IsUpdateHealthMode; private set { if (Set(ref _connected, value)) NotifyHistory(); } }
    public bool IsRestartingEngine => _restartingEngine;
    public bool CanRestartEngine => _restartOperations is not null && !IsRestartingEngine && !IsPreparingUpdate && !IsPreparingQuit && !IsUpdateHealthMode && !_stop.IsCancellationRequested
        && !HasSettingsRecovery && (_preview || _snapshot is not null && _supervisor?.CanRestart == true);
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
    public bool ShowActionNotifications
    {
        get => _showActionNotifications;
        set
        {
            if (Applying || !Set(ref _showActionNotifications, value)) return;
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
        public bool? AutomaticallyCheckForUpdates { get; set; }
        public bool? ReducedAnimation { get; set; }
        public bool? ShowActionNotifications { get; set; }
        public bool RightClickDragToPan { get; set; }
        public bool ShowDisabledAudioDevices { get; set; }
        public bool ShowDisconnectedAudioDevices { get; set; }
        public bool ShowAdvancedAudioDevices { get; set; }
        public List<GraphFrame> Frames { get; set; } = [];
    }
    // These JSON-only seams also let detached verification exercise the same
    // persistence format without reading or writing the user's settings file.
    internal void LoadUiPreferencesJson(string json)
    {
        var preferences = JsonSerializer.Deserialize<UiPreferences>(json, Protocol.Json);
        _automaticallyCheckForUpdates = preferences?.AutomaticallyCheckForUpdates ?? true;
        _reducedAnimation = preferences?.ReducedAnimation ?? _reducedAnimation;
        _showActionNotifications = preferences?.ShowActionNotifications ?? true;
        _rightClickDragToPan = preferences?.RightClickDragToPan ?? false;
        _showDisabledAudioDevices = preferences?.ShowDisabledAudioDevices ?? false;
        _showDisconnectedAudioDevices = preferences?.ShowDisconnectedAudioDevices ?? false;
        _showAdvancedAudioDevices = preferences?.ShowAdvancedAudioDevices ?? false;
        _visualFrames = GraphFrames.Normalize(preferences?.Frames);
    }
    internal string SerializeUiPreferences() => JsonSerializer.Serialize(new UiPreferences
    {
        AutomaticallyCheckForUpdates = _automaticallyCheckForUpdates,
        ReducedAnimation = _reducedAnimation,
        ShowActionNotifications = _showActionNotifications,
        RightClickDragToPan = _rightClickDragToPan,
        ShowDisabledAudioDevices = _showDisabledAudioDevices,
        ShowDisconnectedAudioDevices = _showDisconnectedAudioDevices,
        ShowAdvancedAudioDevices = _showAdvancedAudioDevices,
        Frames = _visualFrames.Select(frame => frame.Copy()).ToList()
    }, Protocol.Json);
    private static string UiPreferencesPath => WorkspacePaths.Preferences;
    private void SaveUiPreferences()
    {
        _uiPreferencesNeedSave = true;
        if (HasSettingsRecovery) return;
        if (_preview && PreviewUiPreferencesWriter is null) { _uiPreferencesNeedSave = false; return; }
        try
        {
            if (_preview) PreviewUiPreferencesWriter!(SerializeUiPreferences());
            else
            {
                string path = UiPreferencesPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = path + ".new";
                File.WriteAllText(temporary, SerializeUiPreferences());
                File.Move(temporary, path, true);
            }
            _uiPreferencesNeedSave = false;
            _uiError = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { _uiError = "Workspace preferences apply for this session, but could not be saved: " + error.Message; }
        UpdateWarning();
    }
    private void EnsureUiPreferencesSaved()
    {
        if (!_uiPreferencesNeedSave) return;
        SaveUiPreferences();
        if (_uiPreferencesNeedSave)
            throw new IOException("Workspace preferences, including node groups, could not be saved. Your changes remain in this session. "
                + (HasSettingsRecovery ? "Finish workspace recovery before continuing." : _uiError));
    }
    private void UpdateWarning() => Warning = string.Join("\n", new[] { _editError, _saveError, _startupError, _uiError, _snapshot?.Warning ?? "" }.Where(s => !string.IsNullOrWhiteSpace(s)));
    public bool HasSettingsRecovery => _snapshot is { SettingsState: not ("loaded" or "new") };
    public bool CanRestoreBackup => _snapshot?.SettingsState == "backupAvailable";
    public bool CanStartFresh => HasSettingsRecovery && _snapshot?.SettingsState != "unsupported";
    public string RecoveryDescription => _snapshot?.SettingsState == "unsupported"
        ? "This workspace needs a newer version of VineMix. Your files have been kept intact."
        : CanRestoreBackup ? "A backup is available, but it may predate recent edits. Audio and saving are paused. Backup: " + _snapshot?.BackupPath
        : "Your saved workspace could not be opened. Retry after repairing the files, or start fresh. Originals will be preserved.";

    public async Task RecoverSettingsAsync(string command)
    {
        if (_preview && _restartOperations is null || !HasSettingsRecovery || IsPreparingQuit) return;
        bool entered = false;
        try
        {
            await _engineGate.WaitAsync(_stop.Token); entered = true;
            if (!_preview && (command is "restoreBackup" or "startFresh") && _pending.Count > 0)
            {
                string preserved = WorkspacePaths.Settings + ".ui-pending-recovery-" + Guid.NewGuid().ToString("N") + ".json";
                using var file = new FileStream(preserved,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_recoveryPending ?? _desired,Protocol.Json));
                file.Write(bytes); file.Flush(true);
            }
            var response = await _restartOperations!.Request(command, null, null, _stop.Token);
            if (!response.Ok || response.Snapshot is null) throw new IOException(response.Error);
            if ((command is "restoreBackup" or "startFresh") && (response.Snapshot.SettingsState is "loaded" or "new"))
            { _pending.Clear(); _canvasView = null; EditHistory.Clear(); _recoveryPending = null; }
            NeedsInitialFrame = command == "startFresh";
            Accept(response.Snapshot, forceConfiguration: true);
        }
        catch (Exception error) when (RestartFailure(error)) { _editError = error.Message; UpdateWarning(); }
        finally { if (entered) _engineGate.Release(); }
    }
    public void Start() { if (!_preview) _loop ??= PollAsync(); }

    // The inert preview accepts supplied design data only. It never opens the
    // control pipe, starts/restarts an engine or changes Windows registration.
    public void ApplyPreviewSnapshot(MixerSnapshot snapshot)
    {
        if (!_preview) throw new InvalidOperationException("Design data can only be applied to a preview model.");
        if (!snapshot.Configuration.IsValidShape) throw new ArgumentException("Preview configuration has an unsupported shape.", nameof(snapshot));
        if (snapshot.Configuration.Graph.Frames.Count > 0)
            _visualFrames = GraphFrames.Normalize(snapshot.Configuration.Graph.Frames);
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
        var candidate = operation.CheckedChange is null && !IsLayoutLocked ? _desired : _desired.Copy();
        if (!operation.TryApply(candidate, out error)) return false;
        GraphFrames.Normalize(candidate.Graph);
        if (IsLayoutLocked && WorkspaceHistory.ChangesLayout(before.Graph, candidate.Graph))
        { error = LayoutLockedMessage; NotifyLayoutEditBlocked(); return false; }
        _desired = candidate;
        // A purely visual group edit never triggers device assignment or an
        // engine configure/save request. It still participates in graph Undo.
        bool framesChanged = !GraphFrames.Same(before.Graph.Frames, _desired.Graph.Frames);
        if (WorkspaceHistory.SameValue(before, _desired))
        {
            if (!framesChanged) return true;
            EditHistory.Record(before.Graph, _desired.Graph);
            SaveVisualFrames(); NotifyHistory(); ApplyDesired();
            return true;
        }
        var manualPolicies = new Dictionary<string, bool>();
        foreach (var node in _desired.Graph.Nodes.Where(n => VirtualDeviceBindings.IsVirtual(n.Kind)))
        {
            var previous = before.Graph.Nodes.FirstOrDefault(n => n.Id == node.Id);
            if (previous is null || WorkspaceHistory.SameValue(previous.Device, node.Device)) continue;
            node.AutoAssignDevice = VirtualDeviceBindings.IsAssigned(node.Device);
            manualPolicies[node.Id] = node.AutoAssignDevice;
        }
        var assignments = AutomaticAssignments();
        VirtualDeviceBindings.Apply(_desired.Graph, assignments);
        foreach (var assignment in assignments) _automaticAssignmentAttempts.Add(assignment.AttemptKey);
        if (WorkspaceHistory.SameValue(before, _desired) && !framesChanged) return true;
        if (framesChanged) SaveVisualFrames();
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
            foreach (var node in c.Graph.Nodes)
                if (manualPolicies.TryGetValue(node.Id, out bool automatic)) node.AutoAssignDevice = automatic;
            VirtualDeviceBindings.Apply(c.Graph, assignments);
            if (view is { } v) { c.Graph.PanX = v.X; c.Graph.PanY = v.Y; c.Graph.Zoom = v.Zoom; }
        }
        var framesBefore = framesChanged ? before.Graph.Frames.Select(frame => frame.Copy()).ToArray() : null;
        var framesAfter = framesChanged ? _desired.Graph.Frames.Select(frame => frame.Copy()).ToArray() : null;
        _pending.Add(operation.CheckedChange is null
            ? new() { Change = c => { operation.Change!(c); Complete(c); }, FramesBefore = framesBefore, FramesAfter = framesAfter }
            : new() { CheckedChange = (MixerConfiguration c, out string replayError) =>
                { if (!operation.TryApply(c, out replayError)) return false; Complete(c); return true; },
                FramesBefore = framesBefore, FramesAfter = framesAfter });
        _editDispatch.Queue(_clock.Elapsed);
        _editError = "";
        SaveStatus = PendingSaveStatus;
        UpdateWarning();
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
        Changed(nameof(CanEditLayout));
        Changed(nameof(CanUndo)); Changed(nameof(CanRedo));
        Changed(nameof(UndoDescription)); Changed(nameof(RedoDescription));
    }

    public bool Undo() => RestoreHistory(undo: true);
    public bool Redo() => RestoreHistory(undo: false);
    private bool RestoreHistory(bool undo)
    {
        if (Applying || !CanEdit) return false;
        if (IsLayoutLocked && (undo ? EditHistory.UndoChangesLayout : EditHistory.RedoChangesLayout))
        { NotifyLayoutEditBlocked(); return false; }
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
        if (!GraphFrames.Same(previousGraph.Frames, restored.Frames)) SaveVisualFrames();
        if (WorkspaceHistory.SameValue(previousGraph, restored))
        {
            _editError = "";
            UpdateWarning(); NotifyHistory(); ApplyDesired();
            return true;
        }
        // An explicit restoration of an unassigned node is authoritative for
        // this session, even if inventory changes would otherwise reassign it.
        foreach (var node in restored.Nodes.Where(node => VirtualDeviceBindings.IsVirtual(node.Kind)))
        {
            var previous = previousGraph.Nodes.FirstOrDefault(other => other.Id == node.Id);
            if (previous is not null && WorkspaceHistory.SameValue(previous.Device, node.Device)) continue;
            if (VirtualDeviceBindings.IsAssigned(node.Device)) _automaticAssignmentSuppressedNodes.Remove(node.Id);
            else { _automaticAssignmentSuppressedNodes.Add(node.Id); node.AutoAssignDevice = false; }
        }
        _editError = "";
        if (_preview && !QueuePreviewEdits) SaveStatus = "Preview · changes stay in this window";
        else
        {
            var policies = restored.Nodes.Where(n => !n.AutoAssignDevice).Select(n => n.Id).ToHashSet();
            _pending.Add(new() { Restore = restore,
                FramesBefore = previousGraph.Frames.Select(frame => frame.Copy()).ToArray(),
                FramesAfter = restored.Frames.Select(frame => frame.Copy()).ToArray(),
                CheckedChange = (MixerConfiguration c, out string replayError) =>
            {
                // Frame metadata already committed locally when Undo/Redo was
                // requested. The engine replay only restores audio/layout data.
                if (!restore!.TryApply(c.Graph, out var graph, out replayError, restoreFrames: false)) return false;
                foreach (var node in graph.Nodes) if (policies.Contains(node.Id)) node.AutoAssignDevice = false;
                c.Graph = graph; return true;
            } });
            _editDispatch.Queue(_clock.Elapsed);
            SaveStatus = PendingSaveStatus;
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

    private MixerConfiguration ReplayPending(MixerConfiguration configuration, out bool conflict)
    {
        conflict = false;
        var dropped = new List<PendingEdit>();
        foreach (var edit in _pending.ToArray())
        {
            if (conflict && edit.Restore is not null) { _pending.Remove(edit); dropped.Add(edit); continue; }
            var candidate = edit.CheckedChange is null ? configuration : configuration.Copy();
            if (edit.TryApply(candidate, out string error)) { configuration = candidate; continue; }
            // A refused operation must not leave later dependent redo/undo
            // commands in the queue. Ordinary newer edits remain rebasable.
            _pending.Remove(edit); dropped.Add(edit); conflict = true; ResetHistoryAfterConflict(error);
        }
        if (conflict)
        {
            _editDispatch.Complete(_pending.Count > 0);
            // Checked operations and later history restores can be dropped
            // during rebase just as they can be rejected by the engine. Their
            // optimistic local membership changes need the same guarded undo.
            _visualFramesNeedSave |= RestoreRejectedVisualFrames(dropped);
        }
        return configuration;
    }

    internal PendingBatch PreparePendingConfiguration(MixerConfiguration basis)
    {
        ReconcileUnconfirmedUpdate(basis);
        ReconcileUnconfirmedQuit(basis);
        var configuration = ReplayPending(basis.Copy(), out bool conflict);
        if (_canvasView is { } view) { configuration.Graph.PanX = view.X; configuration.Graph.PanY = view.Y; configuration.Graph.Zoom = view.Zoom; }
        if (conflict)
        {
            configuration.Graph.Frames = GraphFrames.Normalize(_visualFrames, configuration.Graph.Nodes.Select(node => node.Id));
            _desired = configuration.Copy();
            if (_visualFramesNeedSave || !GraphFrames.Same(_visualFrames, _desired.Graph.Frames)) SaveVisualFrames();
            ApplyDesired();
        }
        return new(configuration, _pending.ToArray());
    }

    internal bool IsPendingSaveDue(TimeSpan now) => _pending.Count > 0 && _editDispatch.IsDue(now);

    internal void HandleConfigureResponse(PendingBatch batch, MixerSnapshot basis, ProtocolResponse response, TimeSpan now)
    {
        if (response.Ok) AcknowledgePending(batch, response.Snapshot);
        else if (response.ErrorCode == "settings_write_failed")
        {
            // The engine validated the configuration but could not persist it.
            // Retain the exact queue and history; another edit or telemetry
            // refresh must not hide that these values are still unsaved.
            _saveError = "Changes are not saved. VineMix will keep retrying while it is running. " + response.Error;
            _editDispatch.DeferRetry(now, TimeSpan.FromSeconds(1));
            SaveStatus = PendingSaveStatus; UpdateWarning();
        }
        else if (response.Error.Contains("revision", StringComparison.OrdinalIgnoreCase) || response.Error.Contains("stale", StringComparison.OrdinalIgnoreCase))
        { _editDispatch.DeferRetry(now); SaveStatus = _saveError.Length > 0 ? PendingSaveStatus : "Reconciling changes…"; }
        else RejectPending(batch, basis, response.Error);
    }

    internal void AcknowledgePending(PendingBatch batch, MixerSnapshot? snapshot)
    {
        // Use identities, not a prefix count: newer edits or retry snapshots
        // can arrive while the configure request is awaiting its response.
        _pending.RemoveAll(edit => batch.Edits.Contains(edit));
        _editDispatch.Complete(_pending.Count > 0);
        _saveError = "";
        if (_pending.Count > 0) SaveStatus = PendingSaveStatus;
        UpdateWarning();
        if (snapshot is not null) Accept(snapshot, forceConfiguration: true);
    }

    internal void RejectPending(PendingBatch batch, MixerSnapshot snapshot, string error)
    {
        var rejected = _pending.Where(edit => batch.Edits.Contains(edit) || edit.Restore is not null).ToArray();
        bool framesRestored = RestoreRejectedVisualFrames(rejected);
        _pending.RemoveAll(rejected.Contains);
        _editDispatch.Complete(_pending.Count > 0);
        _saveError = "";
        EditHistory.Clear(); NotifyHistory();
        _editError = "The engine rejected a change: " + error + " Undo and redo were reset to keep the saved workspace consistent.";
        if (_pending.Count > 0) SaveStatus = PendingSaveStatus;
        Accept(snapshot, forceConfiguration: true);
        if (framesRestored && !HasSettingsRecovery) SaveVisualFrames();
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
            if (!_preview) throw new InvalidOperationException("The running engine needs the node-workspace update. Quit VineMix and reopen the new version.");
            snapshot.Configuration.Graph = GraphDefaults.FromLegacy(snapshot.Configuration);
            snapshot.Configuration.Version = 2;
        }
        ReconcileUnconfirmedUpdate(snapshot.Configuration);
        ReconcileUnconfirmedQuit(snapshot.Configuration);
        bool initial = _snapshot is null;
        bool recovery = snapshot.SettingsState is not ("loaded" or "new");
        if (recovery && !HasSettingsRecovery && _pending.Count > 0) _recoveryPending = _desired.Copy();
        if (initial && !_preview && snapshot.SettingsState == "new") NeedsInitialFrame = true;
        bool configurationChanged = RequiresConfigurationRefresh(_snapshot, snapshot, _preview, CanEdit, forceConfiguration);
        if (configurationChanged)
        {
            // Rebase local edits only when the committed base changes. Each
            // optimistic edit already updated _desired, so telemetry cannot
            // require another full JSON round trip or another pending replay.
            var desired = recovery ? snapshot.Configuration.Copy() : ReplayPending(snapshot.Configuration.Copy(), out _);
            desired.Graph.Frames = GraphFrames.Normalize(_visualFrames, desired.Graph.Nodes.Select(node => node.Id));
            if (!recovery && _canvasView is { } view) { desired.Graph.PanX = view.X; desired.Graph.PanY = view.Y; desired.Graph.Zoom = view.Zoom; }
            _desired = desired;
            ConfigurationRefreshCount++;
        }
        else TelemetryOnlySnapshotCount++;
        if (_preview || !ReferenceEquals(_snapshot?.Status, snapshot.Status)) MeterSampleSequence++;
        _snapshot = snapshot; CanEdit = !HasSettingsRecovery;
        if (configurationChanged && !recovery && (_visualFramesNeedSave || !GraphFrames.Same(_visualFrames, _desired.Graph.Frames))) SaveVisualFrames();
        Changed(nameof(HasSettingsRecovery)); Changed(nameof(CanRestoreBackup));
        Changed(nameof(CanStartFresh)); Changed(nameof(RecoveryDescription));
        Changed(nameof(CanRestartEngine));
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
        if (_pending.Count == 0)
        {
            _saveError = "";
            SaveStatus = _preview ? "Preview · no settings are saved"
                : _uiError.Length > 0 ? "Workspace preferences need attention"
                : _editError.Length == 0 ? $"Saved · revision {snapshot.Configuration.Revision}" : "Change was not saved";
        }
        if (!HasSettingsRecovery && !IsPreparingUpdate && !IsPreparingQuit && !IsUpdateHealthMode && (configurationChanged || inventoryChanged))
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
                SaveStatus = _preview && !QueuePreviewEdits ? "Preview · changes stay in this window" : PendingSaveStatus;
                configurationChanged = true;
            }
        }
        if (!HasSettingsRecovery && configurationChanged && !EditHistory.IsCompatible(_desired.Graph))
            ResetHistoryAfterConflict("Another change affected a node or connection in the edit history.");
        UpdateWarning();
        if (initial) { Changed(nameof(SettingsPath)); Changed(nameof(LogPath)); }
        ApplyDesired(configurationChanged);
        if (HasSettingsRecovery) SaveStatus = "Recovery required · nothing is being saved";
        if (!_preview && CanEdit && _registeredStartup != snapshot.Configuration.StartAtLogin)
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
        ReconcileUnconfirmedUpdate(basis);
        ReconcileUnconfirmedQuit(basis);
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
            if (IsPreparingUpdate || IsPreparingQuit)
            {
                try { await Task.Delay(100, _stop.Token); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            double began = _clock.Elapsed.TotalSeconds;
            bool entered = false;
            try
            {
                await _engineGate.WaitAsync(_stop.Token); entered = true;
                if (IsRestartingEngine || IsPreparingUpdate || IsPreparingQuit) continue;
                var response = await _connection!.RequestAsync("snapshot", null, null, _stop.Token);
                if (!response.Ok || response.Snapshot is null) throw new InvalidOperationException(response.Error.Length > 0 ? response.Error : "The audio engine did not provide a snapshot.");
                bool wasConnected = CanEdit;
                Accept(response.Snapshot);
                double now = _clock.Elapsed.TotalSeconds;
                if (!wasConnected || now - _lastPoll > 8 || _lastFrames != response.Snapshot.Status.AudioFrames)
                { _lastFrameAdvance = now; _lastFrames = response.Snapshot.Status.AudioFrames; }
                _lastPoll = now;
                if (!HasSettingsRecovery && !IsUpdateHealthMode && now - _lastFrameAdvance >= 6)
                {
                    CanEdit = false;
                    InvalidatePresentation();
                    ConnectionStatus = await _supervisor!.RecoverStalledAsync(_stop.Token);
                    _lastFrameAdvance = _clock.Elapsed.TotalSeconds;
                    await Task.Delay(1000, _stop.Token);
                    continue;
                }
                if (CanEdit && IsPendingSaveDue(_clock.Elapsed))
                {
                    var batch = PreparePendingConfiguration(response.Snapshot.Configuration);
                    if (batch.Edits.Count == 0) continue;
                    _editDispatch.BeginDispatch(_clock.Elapsed);
                    var configured = await _connection.RequestAsync("configure", batch.Configuration, response.Snapshot.Configuration.Revision, _stop.Token);
                    HandleConfigureResponse(batch, response.Snapshot, configured, _clock.Elapsed);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception error) when (error is System.IO.IOException or TimeoutException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                CanEdit = false; ConnectionStatus = IsUpdateHealthMode ? EnsureHealthEngineStarted() : _supervisor!.EnsureStarted();
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
        if (IsRestartingEngine || IsPreparingUpdate || IsPreparingQuit || _stop.IsCancellationRequested) return;
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

    public void Dispose() { _stop.Cancel(); _connection?.Dispose(); _supervisor?.Dispose(); _stop.Dispose(); }
}
