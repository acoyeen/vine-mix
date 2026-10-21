using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace VineMix.Desktop.Controls;

/// <summary>
/// An editable analytical EQ response. This control owns copies of settings,
/// never audio: its owner decides how to commit BandsEdited snapshots.
/// </summary>
public sealed class EqualizerEditor : FrameworkElement
{
    public const int MaximumBands = 8;
    private const double MinimumFrequency = 20, MaximumFrequency = 20000;
    private const double MinimumGain = -24, MaximumGain = 24;
    private const double PointRadius = 10, HitRadius = 15;
    private const string Instructions = "Ctrl-click points to toggle selection; drag empty plot space to box-select bands, or hold Ctrl to add to the selection. Ctrl+A selects all bands. Drag any selected point to move the group; hold Shift for finer movement. Arrow keys move selected bands; Ctrl+Left/Right selects a band, with Shift to extend selection. Home resets selected positions; Delete removes selected bands. Escape cancels a selection box. Double-click empty plot space to add a bell band; double-click a point to reset its frequency and gain. The dials below edit the active band. Right-click a point for its shape and actions; right-click empty space to reset the EQ to flat. The wheel zooms the canvas. The line shows the calculated EQ response. The shaded spectrum shows measured output audio against the right dBFS scale.";
    private List<EqBand> _bands = [];
    private readonly HashSet<int> _selection = [];
    private int _selected = -1, _hovered = -1, _dragged = -1;
    private bool _bypassed, _themeSubscribed, _publishingEdit;
    private Point _lastPointer;
    private Point? _selectionOrigin;
    private Point _selectionPointer;
    private int[] _selectionBeforeBox = [];
    private int _primaryBeforeBox = -1;
    private bool _selectionAdditive, _selectionBoxMoved;
    private string? _feedback;
    private ContextMenu? _activeMenu;
    private string? _interactionInstructions;
    internal string? InteractionInstructions
    {
        get => _interactionInstructions;
        set { _interactionInstructions = value; AutomationProperties.SetHelpText(this, value ?? Instructions); UpdateToolTip(); }
    }
    private Brush? _lastTextBrush;
    private readonly float[] _spectrum = new float[128];
    private ulong _spectrumFrame, _spectrumRevision;
    private double _spectrumReceivedAt;
    private bool _spectrumVisible, _responseDirty = true;
    private Rect _responsePlot;
    private double _responseDpi;
    private StreamGeometry? _responseGeometry;
    private readonly Dictionary<(string Text, double Size, Brush Brush, double Width, TextAlignment Alignment), FormattedText> _textCache = [];
    private FontFamily? _textCacheFamily;
    private Brush? _textCachePalette;
    private string _textCacheCulture = "";
    private double _textCacheDpi;
    internal int SettingsRefreshCount { get; private set; }
    internal int TextLayoutBuildCount { get; private set; }
    internal bool HasSpectrum => _spectrumVisible;
    internal ulong SpectrumFrame => _spectrumFrame;

    public event Action<int>? SelectedBandChanged;
    public event Action<IReadOnlyList<EqBand>>? BandsEdited;
    public event EventHandler? EditGestureStarted;
    public event EventHandler? EditGestureEnded;

    public int SelectedBandIndex => _selected;
    public IReadOnlyList<int> SelectedBandIndices => _selection.Order().ToArray();
    public int BandCount => _bands.Count;
    public Rect PlotBounds => new(44, 35, Math.Max(1, ActualWidth - 86), Math.Max(1, ActualHeight - 72));
    internal Rect BandSelectionBounds => _selectionOrigin is Point origin && _selectionBoxMoved
        ? new Rect(origin, _selectionPointer) : Rect.Empty;

    public EqualizerEditor()
    {
        Height = 300;
        MinWidth = 320;
        Focusable = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Cross;
        UpdateToolTip();
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        AutomationProperties.SetName(this, "Equalizer response and band editor");
        AutomationProperties.SetHelpText(this, Instructions);
        Loaded += (_, _) => { if (!_themeSubscribed) { ConsoleTheme.Changed += ThemeChanged; _themeSubscribed = true; } };
        Unloaded += (_, _) => { if (_themeSubscribed) { ConsoleTheme.Changed -= ThemeChanged; _themeSubscribed = false; } EndPointerAdjustment(); };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) EndPointerAdjustment(); InvalidateVisual(); };
    }

    /// <summary>Refresh from owner settings without sending either edit event.</summary>
    public void Apply(IReadOnlyList<EqBand> bands, bool bypassed, int selected)
    {
        ArgumentNullException.ThrowIfNull(bands);
        bool clearFeedback = _feedback is not null && bands.Count < MaximumBands;
        int oldCount = _bands.Count;
        bool preserveSelection = _publishingEdit || oldCount == bands.Count
            && (_selected == selected || _dragged >= 0 || _selectionOrigin is not null);
        int nextSelection = preserveSelection ? (_selected >= 0 && bands.Count > 0 ? Math.Min(_selected, bands.Count - 1) : -1)
            : selected >= 0 && bands.Count > 0 ? Math.Min(selected, bands.Count - 1) : -1;
        Brush textBrush = ConsoleTheme.Get("ConsoleTextBrush");
        bool same = oldCount == bands.Count;
        for (int index = 0; same && index < bands.Count; index++)
        {
            var old = _bands[index]; var next = bands[index];
            same = old.Type == next.Type && old.Enabled == next.Enabled && old.FrequencyHz == next.FrequencyHz
                && old.GainDb == next.GainDb && old.Q == next.Q
                && old.ResetFrequencyHz == (next.ResetFrequencyHz > 0 ? next.ResetFrequencyHz : next.FrequencyHz);
        }
        if (same && _selected == nextSelection && _bypassed == bypassed && ReferenceEquals(_lastTextBrush, textBrush) && !clearFeedback) return;
        _lastTextBrush = textBrush; SettingsRefreshCount++;
        if (!preserveSelection) EndPointerAdjustment();
        _bands = bands.Select(CopyBand).ToList();
        if (clearFeedback) _feedback = null;
        _bypassed = bypassed;
        _responseDirty = true;
        _selected = nextSelection;
        if (preserveSelection)
        {
            // Owner commits and dial refreshes synchronously re-enter Apply.
            // Band selection is local UI state, including the complete group.
            _selection.RemoveWhere(index => index < 0 || index >= _bands.Count);
            if (_selected >= 0) _selection.Add(_selected);
        }
        else { _selection.Clear(); if (_selected >= 0) _selection.Add(_selected); }
        if (_hovered >= _bands.Count) _hovered = -1;
        UpdateDescription();
        InvalidateVisual();
    }

    public IReadOnlyList<EqBand> SnapshotBands() => _bands.Select(CopyBand).ToArray();

    /// <summary>Measured output spectrum only; never edits EQ settings or invents animation.</summary>
    public void ApplySpectrum(IReadOnlyList<float>? bins, ulong frame, ulong revision, bool fresh = true)
        => ApplySpectrumAt(bins, frame, revision, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, fresh);

    internal void ApplySpectrumAt(IReadOnlyList<float>? bins, ulong frame, ulong revision, double nowSeconds, bool fresh = true)
    {
        // Engine frame numbers restart with the process. Forget the old stream
        // cursor when its path goes offline, even if the saved revision is the
        // same, so the next live stream need not catch up to hours-old frames.
        if (!fresh || frame == 0) { _spectrumFrame = _spectrumRevision = 0; }
        if (!fresh || frame == 0 || bins?.Count != _spectrum.Length || !double.IsFinite(nowSeconds)
            || bins.Any(value => !float.IsFinite(value)))
        {
            ClearSpectrum(); return;
        }
        if (revision == _spectrumRevision && frame <= _spectrumFrame)
        {
            if (nowSeconds - _spectrumReceivedAt > .25) ClearSpectrum();
            return;
        }
        for (int index = 0; index < _spectrum.Length; index++) _spectrum[index] = Math.Clamp(bins[index], -90, 0);
        _spectrumFrame = frame; _spectrumRevision = revision; _spectrumReceivedAt = nowSeconds;
        _spectrumVisible = true;
        InvalidateVisual();
    }

    private void ClearSpectrum()
    {
        if (!_spectrumVisible) return;
        _spectrumVisible = false; InvalidateVisual();
    }

    internal IReadOnlyList<Point> SpectrumPoints()
    {
        if (!_spectrumVisible) return [];
        Rect plot = PlotBounds;
        return _spectrum.Select((db, index) => new Point(plot.Left + plot.Width * index / (_spectrum.Length - 1),
            plot.Top + Math.Clamp(-db / 90d, 0, 1) * plot.Height)).ToArray();
    }

    public bool SelectBand(int index, bool toggle = false)
    {
        if (index < -1 || index >= _bands.Count) return false;
        if (!toggle || index < 0) SetSelection(index < 0 ? [] : [index], index);
        else
        {
            var selection = new HashSet<int>(_selection);
            if (!selection.Add(index)) selection.Remove(index);
            SetSelection(selection, selection.Contains(index) ? index : _selected);
        }
        return true;
    }

    public void SelectAllBands() => SetSelection(Enumerable.Range(0, _bands.Count), _selected >= 0 ? _selected : 0);

    private void SetSelection(IEnumerable<int> indices, int primary)
    {
        var selection = indices.Where(index => index >= 0 && index < _bands.Count).ToHashSet();
        primary = selection.Contains(primary) ? primary : selection.Count > 0 ? selection.Min() : -1;
        _feedback = null;
        if (_selected == primary && _selection.SetEquals(selection)) { UpdateToolTip(); return; }
        _selection.Clear(); _selection.UnionWith(selection);
        _selected = primary;
        UpdateDescription();
        InvalidateVisual();
        SelectedBandChanged?.Invoke(primary);
    }

    public bool AddBandAt(double frequency, double gain)
    {
        if (!IsEnabled || !double.IsFinite(frequency) || !double.IsFinite(gain)) return false;
        if (_bands.Count >= MaximumBands)
        {
            _feedback = "All 8 bands are in use. Remove a band to add another.";
            UpdateToolTip();
            InvalidateVisual();
            return false;
        }
        EndPointerAdjustment();
        double anchor = Math.Clamp(frequency, MinimumFrequency, MaximumFrequency);
        _bands.Add(new EqBand { Type = "peak", Enabled = true, FrequencyHz = anchor, ResetFrequencyHz = anchor,
            GainDb = Math.Clamp(gain, MinimumGain, MaximumGain), Q = 1 });
        _selected = _bands.Count - 1;
        _selection.Clear(); _selection.Add(_selected);
        PublishEdit(selectionChanged: true);
        return true;
    }

    public bool RemoveSelectedBand()
    {
        if (!IsEnabled || _selected < 0 || _selected >= _bands.Count) return false;
        EndPointerAdjustment();
        foreach (int index in _selection.OrderDescending()) _bands.RemoveAt(index);
        _selected = _bands.Count == 0 ? -1 : Math.Min(_selected, _bands.Count - 1);
        _selection.Clear(); if (_selected >= 0) _selection.Add(_selected);
        _hovered = -1;
        PublishEdit(selectionChanged: true);
        return true;
    }

    public bool MoveBand(int index, double frequency, double gain)
    {
        if (!IsEnabled || index < 0 || index >= _bands.Count || !double.IsFinite(frequency) || !double.IsFinite(gain)) return false;
        var band = _bands[index];
        double nextFrequency = Math.Clamp(frequency, MinimumFrequency, MaximumFrequency);
        double nextGain = SupportsGain(band) ? Math.Clamp(gain, MinimumGain, MaximumGain) : band.GainDb;
        if (band.FrequencyHz == nextFrequency && band.GainDb == nextGain) return false;
        band.FrequencyHz = nextFrequency;
        band.GainDb = nextGain;
        PublishEdit(selectionChanged: false);
        return true;
    }

    public bool SetBandQ(int index, double q)
    {
        if (!IsEnabled || index < 0 || index >= _bands.Count || !double.IsFinite(q)) return false;
        double next = Math.Clamp(q, .1, 20);
        if (_bands[index].Q == next) return false;
        _bands[index].Q = next;
        PublishEdit(selectionChanged: false);
        return true;
    }

    /// <summary>Move a selected group in logarithmic frequency and linear gain space.</summary>
    private bool MoveSelection(double frequencyRatio, double gainDelta)
    {
        if (!IsEnabled || _selection.Count == 0 || !double.IsFinite(frequencyRatio) || frequencyRatio <= 0
            || !double.IsFinite(gainDelta)) return false;
        // Bound the shared motion, not each point separately, so intervals and
        // gain differences survive contact with an edge of the graph.
        double minimumRatio = 0, maximumRatio = double.PositiveInfinity;
        double minimumDelta = double.NegativeInfinity, maximumDelta = double.PositiveInfinity;
        foreach (int index in _selection)
        {
            var band = _bands[index];
            minimumRatio = Math.Max(minimumRatio, MinimumFrequency / band.FrequencyHz);
            maximumRatio = Math.Min(maximumRatio, MaximumFrequency / band.FrequencyHz);
            if (!SupportsGain(band)) continue;
            minimumDelta = Math.Max(minimumDelta, MinimumGain - band.GainDb);
            maximumDelta = Math.Min(maximumDelta, MaximumGain - band.GainDb);
        }
        frequencyRatio = Math.Clamp(frequencyRatio, minimumRatio, maximumRatio);
        gainDelta = Math.Clamp(gainDelta, minimumDelta, maximumDelta);
        bool changed = false;
        foreach (int index in _selection)
        {
            var band = _bands[index];
            double frequency = Math.Clamp(band.FrequencyHz * frequencyRatio, MinimumFrequency, MaximumFrequency);
            double gain = SupportsGain(band) ? Math.Clamp(band.GainDb + gainDelta, MinimumGain, MaximumGain) : band.GainDb;
            changed |= frequency != band.FrequencyHz || gain != band.GainDb;
            band.FrequencyHz = frequency; band.GainDb = gain;
        }
        if (changed) PublishEdit(false);
        return changed;
    }

    private bool ResetSelectedPositions()
    {
        if (!IsEnabled || _selection.Count == 0) return false;
        EndPointerAdjustment();
        bool changed = false;
        foreach (int index in _selection)
        {
            var band = _bands[index];
            double frequency = Math.Clamp(band.ResetFrequencyHz > 0 ? band.ResetFrequencyHz : band.FrequencyHz, MinimumFrequency, MaximumFrequency);
            changed |= band.FrequencyHz != frequency || band.GainDb != 0;
            band.FrequencyHz = frequency; band.GainDb = 0;
        }
        if (changed) PublishEdit(false);
        return changed;
    }

    public bool ResetBandPosition(int index)
    {
        if (!IsEnabled || index < 0 || index >= _bands.Count) return false;
        EndPointerAdjustment();
        var band = _bands[index];
        double frequency = Math.Clamp(band.ResetFrequencyHz > 0 ? band.ResetFrequencyHz : band.FrequencyHz, MinimumFrequency, MaximumFrequency);
        if (band.FrequencyHz == frequency && band.GainDb == 0) return false;
        band.FrequencyHz = frequency; band.GainDb = 0;
        PublishEdit(false); return true;
    }

    public bool ResetEq()
    {
        if (!IsEnabled) return false;
        EndPointerAdjustment();
        if (_bands.Count == 0) return false;
        _bands = [];
        _selected = -1; _hovered = -1;
        _selection.Clear();
        PublishEdit(true); return true;
    }

    internal bool SetBandShape(int index, string type)
    {
        if (!IsEnabled || index < 0 || index >= _bands.Count || type is not ("peak" or "lowShelf" or "highShelf" or "notch")) return false;
        var band = _bands[index];
        if (band.Type == type) return false;
        EndPointerAdjustment();
        band.Type = type;
        if (type == "notch") band.GainDb = 0;
        PublishEdit(selectionChanged: false);
        return true;
    }

    /// <summary>Semantic drag methods also work without mouse capture for inert checks.</summary>
    public bool BeginAdjustment(int index, Point pointer)
    {
        if (!IsEnabled || index < 0 || index >= _bands.Count || !Finite(pointer)) return false;
        EndBandSelection();
        if (_selection.Contains(index)) SetSelection(_selection, index);
        else SelectBand(index);
        bool starting = _dragged < 0;
        _dragged = index;
        _lastPointer = pointer;
        if (starting) EditGestureStarted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool ContinueAdjustment(Point pointer, bool fine = false)
    {
        if (!IsEnabled || _dragged < 0 || _dragged >= _bands.Count || !Finite(pointer)) return false;
        Vector delta = pointer - _lastPointer;
        _lastPointer = pointer;
        if (delta.LengthSquared == 0) return false;
        // Incremental motion prevents a value jump when Shift changes mid-drag,
        // and reversing away from an axis limit responds immediately.
        double scale = fine ? .2 : 1;
        Rect plot = PlotBounds;
        double ratio = Math.Pow(MaximumFrequency / MinimumFrequency, Math.Clamp(delta.X * scale / plot.Width, -1, 1));
        double gain = -delta.Y * scale / plot.Height * (MaximumGain - MinimumGain);
        return MoveSelection(ratio, gain);
    }

    public void EndAdjustment()
    {
        bool ending = _dragged >= 0;
        _dragged = -1; Cursor = _hovered >= 0 ? Cursors.Hand : Cursors.Cross;
        if (ending) EditGestureEnded?.Invoke(this, EventArgs.Empty);
    }

    public bool BeginBandSelection(Point pointer, bool additive = false)
    {
        if (!IsEnabled || !Finite(pointer) || !PlotBounds.Contains(pointer)) return false;
        EndPointerAdjustment();
        _selectionBeforeBox = _selection.Order().ToArray(); _primaryBeforeBox = _selected;
        _selectionOrigin = _selectionPointer = pointer;
        _selectionAdditive = additive; _selectionBoxMoved = false;
        if (!additive) SelectBand(-1);
        return true;
    }

    public bool ContinueBandSelection(Point pointer)
    {
        if (!IsEnabled || _selectionOrigin is not Point origin || !Finite(pointer)) return false;
        Rect plot = PlotBounds;
        _selectionPointer = new Point(Math.Clamp(pointer.X, plot.Left, plot.Right), Math.Clamp(pointer.Y, plot.Top, plot.Bottom));
        if (!_selectionBoxMoved && (_selectionPointer - origin).Length < 3) return false;
        _selectionBoxMoved = true;
        Rect box = new(origin, _selectionPointer);
        var selection = _selectionAdditive ? _selectionBeforeBox.ToHashSet() : [];
        for (int index = 0; index < _bands.Count; index++) if (box.Contains(BandPosition(index))) selection.Add(index);
        SetSelection(selection, _primaryBeforeBox);
        InvalidateVisual();
        return true;
    }

    public void EndBandSelection(bool cancel = false)
    {
        if (_selectionOrigin is null) return;
        _selectionOrigin = null; _selectionBoxMoved = false;
        if (cancel) SetSelection(_selectionBeforeBox, _primaryBeforeBox);
        _selectionBeforeBox = [];
        InvalidateVisual();
    }

    public static Point BandToPoint(double frequency, double gain, Rect plot)
    {
        frequency = Math.Clamp(double.IsFinite(frequency) ? frequency : 1000, MinimumFrequency, MaximumFrequency);
        gain = Math.Clamp(double.IsFinite(gain) ? gain : 0, MinimumGain, MaximumGain);
        return new Point(plot.Left + Math.Log(frequency / MinimumFrequency) / Math.Log(MaximumFrequency / MinimumFrequency) * plot.Width,
            plot.Top + (MaximumGain - gain) / (MaximumGain - MinimumGain) * plot.Height);
    }

    public static (double FrequencyHz, double GainDb) PointToBand(Point point, Rect plot)
    {
        double x = Math.Clamp(Finite(point) ? (point.X - plot.Left) / Math.Max(1, plot.Width) : .5, 0, 1);
        double y = Math.Clamp(Finite(point) ? (point.Y - plot.Top) / Math.Max(1, plot.Height) : .5, 0, 1);
        return (MinimumFrequency * Math.Pow(MaximumFrequency / MinimumFrequency, x), MaximumGain - y * (MaximumGain - MinimumGain));
    }

    private void PublishEdit(bool selectionChanged)
    {
        _feedback = null;
        _responseDirty = true;
        int selection = _selected;
        var snapshot = SnapshotBands();
        // Store the changed list before selecting a newly added/reindexed band.
        // Owner handlers may synchronously call Apply with the committed data.
        _publishingEdit = true;
        try { BandsEdited?.Invoke(snapshot); }
        finally { _publishingEdit = false; }
        if (selectionChanged)
        {
            _selected = ValidSelection(selection);
            SelectedBandChanged?.Invoke(_selected);
        }
        UpdateDescription();
        InvalidateVisual();
    }

    private static EqBand CopyBand(EqBand source) => new() { Type = source.Type, Enabled = source.Enabled,
        FrequencyHz = source.FrequencyHz, GainDb = source.GainDb, Q = source.Q,
        ResetFrequencyHz = source.ResetFrequencyHz > 0 ? source.ResetFrequencyHz : source.FrequencyHz };
    private int ValidSelection(int selected) => selected >= 0 && _bands.Count > 0 ? Math.Min(selected, _bands.Count - 1) : -1;
    private static bool SupportsGain(EqBand band) => band.Type is not "lowPass" and not "highPass" and not "notch";
    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private Point BandPosition(int index) => BandToPoint(_bands[index].FrequencyHz, SupportsGain(_bands[index]) ? _bands[index].GainDb : 0, PlotBounds);
    private void ThemeChanged(object? sender, EventArgs args) => InvalidateVisual();

    private int BandAt(Point point)
    {
        if (_selected >= 0 && (BandPosition(_selected) - point).Length <= HitRadius) return _selected;
        int result = -1;
        double nearest = HitRadius;
        for (int index = 0; index < _bands.Count; index++)
        {
            double distance = (BandPosition(index) - point).Length;
            if (distance < nearest) { result = index; nearest = distance; }
        }
        return result;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        Focus();
        Point point = e.GetPosition(this);
        int index = BandAt(point);
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (index >= 0)
        {
            if (control) { EndPointerAdjustment(); SelectBand(index, toggle: true); }
            else if (e.ClickCount == 2) { SelectBand(index); ResetBandPosition(index); }
            else if (BeginAdjustment(index, point)) { Cursor = Cursors.SizeAll; if (!CaptureMouse()) EndAdjustment(); }
        }
        else if (e.ClickCount == 2 && PlotBounds.Contains(point))
        {
            var value = PointToBand(point, PlotBounds);
            AddBandAt(value.FrequencyHz, value.GainDb);
        }
        else if (BeginBandSelection(point, control)) { if (!CaptureMouse()) EndBandSelection(); }
        else if (!control) SelectBand(-1);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point point = e.GetPosition(this);
        if (_selectionOrigin is not null)
        {
            ContinueBandSelection(point);
            e.Handled = true;
        }
        else if (_dragged >= 0)
        {
            ContinueAdjustment(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else
        {
            int hovered = BandAt(point);
            if (hovered != _hovered) { _hovered = hovered; UpdateToolTip(); InvalidateVisual(); }
            Cursor = hovered >= 0 ? Cursors.Hand : Cursors.Cross;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragged < 0 && _selectionOrigin is null) return;
        if (_selectionOrigin is not null) ContinueBandSelection(e.GetPosition(this));
        EndPointerAdjustment();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { EndAdjustment(); EndBandSelection(); base.OnLostMouseCapture(e); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hovered = -1; UpdateToolTip(); InvalidateVisual(); base.OnMouseLeave(e); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { InvalidateVisual(); base.OnGotKeyboardFocus(e); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { EndPointerAdjustment(); InvalidateVisual(); base.OnLostKeyboardFocus(e); }

    private void EndPointerAdjustment()
    {
        EndAdjustment();
        EndBandSelection();
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    internal bool TryCancelPracticeInteraction()
    {
        if (_activeMenu is { IsOpen: true }) { _activeMenu.IsOpen = false; return true; }
        if (_dragged < 0 && _selectionOrigin is null && !IsMouseCaptured) return false;
        EndBandSelection(cancel: true);
        EndPointerAdjustment();
        return true;
    }

    internal void SuspendPracticeInteraction()
    {
        if (_activeMenu is not null) _activeMenu.IsOpen = false;
        EndBandSelection(cancel: true);
        EndPointerAdjustment();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (!IsEnabled) return;
        Focus();
        Point point = e.GetPosition(this);
        int index = BandAt(point);
        if (index >= 0)
        {
            if (_selection.Contains(index)) SetSelection(_selection, index);
            else SelectBand(index);
        }
        ShowBandMenu(index, point);
        e.Handled = true;
    }

    private void ShowBandMenu(int index, Point point)
    {
        EndPointerAdjustment();
        if (_activeMenu is not null) _activeMenu.IsOpen = false;
        _activeMenu = BuildBandMenu(index, point);
        _activeMenu.IsOpen = true;
    }

    /// <summary>Build the real band menu without opening a native popup.</summary>
    internal ContextMenu BuildBandMenu(int index, Point point)
    {
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.RelativePoint, HorizontalOffset = point.X, VerticalOffset = point.Y };
        if (index >= 0 && index < _bands.Count)
        {
            var shape = new MenuItem { Header = "Shape" };
            foreach (var (type, label) in new[] { ("peak", "Bell"), ("lowShelf", "Low shelf"), ("highShelf", "High shelf"), ("notch", "Notch") })
            {
                var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _bands[index].Type == type };
                item.Click += (_, _) => SetBandShape(index, type);
                shape.Items.Add(item);
            }
            if (_bands[index].Type is "highPass" or "lowPass")
                shape.ToolTip = NodeToolTip.Create("Saved pass filter", BandEffect(_bands[index]), "Retains its saved filter shape");
            var toggle = new MenuItem { Header = _bands[index].Enabled ? "Disable band" : "Enable band" };
            toggle.Click += (_, _) => { if (index < _bands.Count) { _bands[index].Enabled = !_bands[index].Enabled; PublishEdit(false); } };
            var reset = new MenuItem { Header = "Reset position", InputGestureText = "Double-click" };
            reset.Click += (_, _) => ResetBandPosition(index);
            bool group = _selection.Contains(index) && _selection.Count > 1;
            var remove = new MenuItem { Header = group ? "Remove selected bands" : "Remove band", InputGestureText = "Delete" };
            remove.Click += (_, _) => { if (!_selection.Contains(index)) SelectBand(index); RemoveSelectedBand(); };
            menu.Items.Add(shape); menu.Items.Add(new Separator());
            menu.Items.Add(toggle); menu.Items.Add(reset); menu.Items.Add(new Separator()); menu.Items.Add(remove);
        }
        else
        {
            var add = new MenuItem { Header = _bands.Count >= MaximumBands ? "All 8 bands are in use" : "Add bell band here", IsEnabled = _bands.Count < MaximumBands };
            add.Click += (_, _) => { var value = PointToBand(point, PlotBounds); AddBandAt(value.FrequencyHz, value.GainDb); };
            var reset = new MenuItem { Header = "Reset EQ to flat" };
            reset.Click += (_, _) => ResetEq();
            menu.Items.Add(add); menu.Items.Add(new Separator()); menu.Items.Add(reset);
        }
        MenuHoverHelp.Attach(menu);
        return menu;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsKeyboardFocused || !IsEnabled) return;
        bool fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Apps || e.Key == Key.F10 && fine)
        { ShowBandMenu(_selected, _selected >= 0 ? BandPosition(_selected) : new Point(PlotBounds.Left + PlotBounds.Width / 2, PlotBounds.Top + PlotBounds.Height / 2)); e.Handled = true; }
        else e.Handled = AdjustWithKey(e.Key, fine, control);
    }

    /// <summary>Shared keyboard decisions for real input and detached verification.</summary>
    internal bool AdjustWithKey(Key key, bool fine = false, bool control = false)
    {
        if (!IsEnabled) return false;
        if (key == Key.Escape) { EndBandSelection(cancel: true); EndPointerAdjustment(); return true; }
        if (control && key == Key.A) { EndPointerAdjustment(); SelectAllBands(); }
        else if (key is Key.Delete or Key.Back) RemoveSelectedBand();
        else if (key == Key.Insert) AddBandAt(1000, 0);
        else if (control && (key is Key.Left or Key.Right))
        {
            EndPointerAdjustment();
            if (_bands.Count > 0)
            {
                int next = _selected < 0 ? 0 : (_selected + (key == Key.Right ? 1 : _bands.Count - 1)) % _bands.Count;
                if (fine) SetSelection(_selection.Append(next), next);
                else SelectBand(next);
            }
        }
        else if (control) return false;
        else if (key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.PageUp or Key.PageDown)
        {
            EndPointerAdjustment();
            if (_selected < 0 && _bands.Count > 0) SelectBand(0);
            if (_selected >= 0)
            {
                var band = _bands[_selected];
                double frequencyStep = Math.Pow(2, fine ? 1.0 / 120 : 1.0 / 12);
                double gainStep = fine ? .1 : .5;
                if (key == Key.Left) MoveSelection(1 / frequencyStep, 0);
                else if (key == Key.Right) MoveSelection(frequencyStep, 0);
                else if (key == Key.Up) MoveSelection(1, gainStep);
                else if (key == Key.Down) MoveSelection(1, -gainStep);
                else if (key == Key.Home) ResetSelectedPositions();
                else SetBandQ(_selected, band.Q * Math.Pow(fine ? 1.02 : 1.1, key == Key.PageUp ? 1 : -1));
            }
        }
        else return false;
        return true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var well = ConsoleTheme.Get("ConsoleWellBrush");
        var text = ConsoleTheme.Get("ConsoleTextBrush");
        var muted = ConsoleTheme.Get("ConsoleMutedBrush");
        var accent = ConsoleTheme.Get("ConsoleABrush");
        dc.DrawRoundedRectangle(well, new Pen(ConsoleTheme.Get(IsKeyboardFocused ? "ConsoleFocusBrush" : "ConsoleControlBorderBrush"), 1), new Rect(.5, .5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 3, 3);
        Rect plot = PlotBounds;
        string caption = _feedback ?? (_bands.Count == 0 ? "Flat EQ · no bands" : _bypassed ? "Bypassed · band settings retained" : _selected >= 0 ? SelectionDescription() : $"{_bands.Count} / {MaximumBands} bands · select a point");
        // Keep chart text inside the plot's usable width, clear of both scales.
        DrawText(dc, caption, 13, text, new Point(plot.Left + 8, 6), Math.Max(1, plot.Width - 16), TextAlignment.Center);

        var gridPen = new Pen(ConsoleTheme.Get("ConsoleControlBorderBrush"), .7);
        var zeroPen = new Pen(ConsoleTheme.Get("ConsoleBorderBrush"), 1.2);
        foreach (double gain in new[] { -24d, -18, -12, -6, 0, 6, 12, 18, 24 })
        {
            double y = BandToPoint(1000, gain, plot).Y;
            dc.DrawLine(gain == 0 ? zeroPen : gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, gain.ToString("+0;-0;0", CultureInfo.InvariantCulture), 11, muted, new Point(5, y - 7), 31);
        }
        foreach (double frequency in new[] { 20d, 30, 50, 70, 100, 200, 300, 500, 700, 1000, 2000, 3000, 5000, 7000, 10000, 20000 })
        {
            double x = BandToPoint(frequency, 0, plot).X;
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        double lastLabelRight = double.NegativeInfinity;
        foreach (var (frequency, label) in new[] { (20d, "20"), (50d, "50"), (100d, "100"), (200d, "200"),
            (500d, "500"), (1000d, "1k"), (2000d, "2k"), (5000d, "5k"), (10000d, "10k"), (20000d, "20k") })
        {
            var formatted = Format(label, 11, muted);
            double x = Math.Clamp(BandToPoint(frequency, 0, plot).X - formatted.Width / 2, plot.Left, Math.Max(plot.Left, plot.Right - formatted.Width));
            if (x < lastLabelRight + 4) continue;
            dc.DrawText(formatted, new Point(x, plot.Bottom + 9));
            lastLabelRight = x + formatted.Width;
        }
        DrawText(dc, "dB", 10, muted, new Point(6, plot.Top - 18), 30);
        DrawText(dc, "Hz", 10, muted, new Point(plot.Right + 8, plot.Bottom + 9), 30);
        if (_spectrumVisible)
        {
            DrawText(dc, "dBFS", 9, muted, new Point(plot.Right + 7, plot.Top - 18), 32);
            foreach (double db in new[] { 0d, -18, -36, -54, -72, -90 })
                DrawText(dc, db.ToString("0", CultureInfo.InvariantCulture), 10, muted,
                    new Point(plot.Right + 8, plot.Top - db / 90 * plot.Height - 7), 30);
        }

        dc.PushClip(new RectangleGeometry(plot));
        if (_spectrumVisible)
        {
            var spectrum = new StreamGeometry();
            using (var path = spectrum.Open())
            {
                path.BeginFigure(new Point(plot.Left, plot.Bottom), true, true);
                foreach (var point in SpectrumPoints()) path.LineTo(point, true, false);
                path.LineTo(new Point(plot.Right, plot.Bottom), true, false);
            }
            spectrum.Freeze();
            var fill = muted.Clone(); fill.Opacity = .14; fill.Freeze();
            var edge = muted.Clone(); edge.Opacity = .48; edge.Freeze();
            dc.DrawGeometry(fill, new Pen(edge, 1), spectrum);
        }
        var responsePen = new Pen(_bypassed ? muted : accent, 2.2);
        if (_bypassed) responsePen.DashStyle = DashStyles.Dash;
        dc.DrawGeometry(null, responsePen, ResponseGeometry(plot));
        if (!BandSelectionBounds.IsEmpty)
        {
            var fill = ConsoleTheme.Get("NodeSelectedBrush").Clone(); fill.Opacity = .15; fill.Freeze();
            dc.DrawRectangle(fill, new Pen(ConsoleTheme.Get("NodeSelectedBrush"), 1) { DashStyle = DashStyles.Dash }, BandSelectionBounds);
        }
        dc.Pop();

        for (int index = 0; index < _bands.Count; index++) if (!_selection.Contains(index)) DrawBand(dc, index);
        foreach (int index in _selection.Order()) if (index != _selected) DrawBand(dc, index);
        if (_selected >= 0) DrawBand(dc, _selected);
        if (_bands.Count == 0)
            DrawText(dc, "Double-click to add a band", 13, muted, new Point(plot.Left + 8, plot.Top + plot.Height / 2 - 31), Math.Max(1, plot.Width - 16), TextAlignment.Center);
    }

    private StreamGeometry ResponseGeometry(Rect plot)
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (!_responseDirty && _responseGeometry is not null && _responsePlot == plot && _responseDpi == dpi)
            return _responseGeometry;
        var curve = new StreamGeometry();
        int steps = Math.Clamp((int)Math.Ceiling(plot.Width * dpi), 128, 1536);
        using (var path = curve.Open())
            for (int sample = 0; sample <= steps; sample++)
            {
                double frequency = MinimumFrequency * Math.Pow(MaximumFrequency / MinimumFrequency, sample / (double)steps);
                Point point = BandToPoint(frequency, CombinedResponseDb(_bands, _bypassed, frequency), plot);
                if (sample == 0) path.BeginFigure(point, false, false); else path.LineTo(point, true, false);
            }
        curve.Freeze(); _responseGeometry = curve; _responsePlot = plot; _responseDpi = dpi; _responseDirty = false;
        return curve;
    }

    private void DrawBand(DrawingContext dc, int index)
    {
        bool selected = _selection.Contains(index), enabled = _bands[index].Enabled;
        Point point = BandPosition(index);
        Brush outline = ConsoleTheme.Get(selected || index == _hovered ? "NodeSelectedBrush" : enabled ? "ConsoleABrush" : "ConsoleMutedBrush");
        Brush fill = ConsoleTheme.Get(selected ? "NodeSelectedBrush" : enabled ? "ConsoleABrush" : "ConsoleWellBrush");
        Brush number = ConsoleTheme.Get(selected || enabled ? "ConsoleInkBrush" : "ConsoleMutedBrush");
        dc.DrawEllipse(fill, new Pen(outline, selected ? 2 : 1.5), point, PointRadius, PointRadius);
        if (index == _selected) dc.DrawEllipse(null, new Pen(outline, 1), point, PointRadius + 3, PointRadius + 3);
        var label = Format((index + 1).ToString(CultureInfo.InvariantCulture), 13, number);
        dc.DrawText(label, new Point(point.X - label.Width / 2, point.Y - label.Height / 2));
        if (!enabled) dc.DrawLine(new Pen(ConsoleTheme.Get("ConsoleMutedBrush"), 1), new Point(point.X - 6, point.Y + 7), new Point(point.X + 6, point.Y - 7));
    }

    private FormattedText Format(string value, double size, Brush brush, double width = double.PositiveInfinity,
        TextAlignment alignment = TextAlignment.Left)
    {
        // Live spectrum frames redraw the chart many times a second. Its axis
        // labels, point numbers and unchanged caption do not need fresh glyph
        // shaping for each frame. Bound the cache because captions change while
        // bands are dragged, and invalidate on actual font/DPI/theme changes.
        var family = TextElement.GetFontFamily(this);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var palette = ConsoleTheme.Get("ConsoleTextBrush");
        string culture = CultureInfo.CurrentUICulture.Name;
        if (_textCacheDpi != dpi || !Equals(_textCacheFamily, family) || !ReferenceEquals(_textCachePalette, palette)
            || _textCacheCulture != culture)
        {
            _textCache.Clear(); _textCacheDpi = dpi; _textCacheFamily = family;
            _textCachePalette = palette; _textCacheCulture = culture;
        }
        var key = (value, size, brush, width, alignment);
        if (_textCache.TryGetValue(key, out var cached)) return cached;
        if (_textCache.Count >= 96) _textCache.Clear();
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, dpi)
            { TextAlignment = alignment };
        if (double.IsFinite(width))
        { text.MaxTextWidth = Math.Max(1, width); text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis; }
        _textCache.Add(key, text); TextLayoutBuildCount++;
        return text;
    }

    private void DrawText(DrawingContext dc, string value, double size, Brush brush, Point position, double width,
        TextAlignment alignment = TextAlignment.Left)
    {
        dc.DrawText(Format(value, size, brush, width, alignment), position);
    }

    private string BandDescription(int index)
    {
        var band = _bands[index];
        string frequency = band.FrequencyHz >= 1000 ? (band.FrequencyHz / 1000).ToString("0.##", CultureInfo.CurrentCulture) + " kHz" : band.FrequencyHz.ToString("0.#", CultureInfo.CurrentCulture) + " Hz";
        string gain = SupportsGain(band) ? band.GainDb.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB" : band.Type == "notch" ? "notch" : "cutoff";
        return $"Band {index + 1} · {frequency} · {gain} · Q {band.Q:0.0###}" + (band.Enabled ? "" : " · off");
    }

    private void UpdateDescription()
    {
        string state = _selected >= 0 ? SelectionDescription() : $"{_bands.Count} equalizer bands. No band selected.";
        AutomationProperties.SetItemStatus(this, (_bypassed ? "Bypassed. " : "") + state);
        UpdateToolTip();
    }

    private string SelectionDescription() => _selection.Count > 1
        ? $"{_selection.Count} selected · active {BandDescription(_selected)}" : BandDescription(_selected);

    private void UpdateToolTip()
    {
        if (_feedback is not null)
        {
            ToolTip = NodeToolTip.Create("All 8 bands are in use", "Maximum EQ capacity · 8 bands");
            return;
        }
        int bandIndex = _hovered >= 0 && _hovered < _bands.Count ? _hovered : _selected;
        if (bandIndex >= 0 && bandIndex < _bands.Count)
        {
            var band = _bands[bandIndex];
            ToolTip = NodeToolTip.Create(bandIndex == _selected ? $"Selected band {bandIndex + 1}" : $"Band {bandIndex + 1}",
                BandEffect(band), BandDescription(bandIndex),
                _bypassed ? "Bypassed · EQ changes are not applied"
                    : band.Enabled ? "Enabled · Contributes to the EQ response" : "Disabled · This band does not affect audio");
            return;
        }
        ToolTip = NodeToolTip.Create("Equalizer", "Shapes frequency balance with up to 8 bands",
            "Line · Calculated EQ response on the left dB scale",
            _interactionInstructions is null ? "Shaded spectrum · Measured output on the right dBFS scale" : "Practice only · Calculated response; no audio is measured or changed",
            _bypassed ? "Bypassed · EQ settings retained"
                : _bands.Count == 0 ? "No bands · Audio passes without EQ changes" : $"{_bands.Count} / {MaximumBands} bands");
    }

    private static string BandEffect(EqBand band) => band.Type switch
    {
        "lowShelf" => "Boosts or cuts frequencies below its centre",
        "highShelf" => "Boosts or cuts frequencies above its centre",
        "notch" => "Rejects a narrow band around its centre",
        "highPass" => "Reduces frequencies below its cutoff",
        "lowPass" => "Reduces frequencies above its cutoff",
        _ => "Boosts or cuts frequencies around its centre"
    };

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorAutomationPeer(this);
    private sealed class EditorAutomationPeer(EqualizerEditor owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(EqualizerEditor);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    }

    public static double CombinedResponseDb(IReadOnlyList<EqBand> bands, bool bypassed, double frequency)
        => bypassed ? 0 : bands.Sum(band => ResponseDb(band, frequency));

    /// <summary>The same 48 kHz RBJ biquad response used by the native EQ.</summary>
    public static double ResponseDb(EqBand band, double frequency)
    {
        if (!band.Enabled || !double.IsFinite(frequency) || !double.IsFinite(band.FrequencyHz) || !double.IsFinite(band.GainDb) || !double.IsFinite(band.Q)) return 0;
        double w0 = 2 * Math.PI * Math.Clamp(band.FrequencyHz, MinimumFrequency, MaximumFrequency) / 48000;
        double c = Math.Cos(w0), s = Math.Sin(w0), q = Math.Clamp(band.Q, .1, 20), alpha = s / (2 * q);
        double a = Math.Pow(10, Math.Clamp(band.GainDb, MinimumGain, MaximumGain) / 40), beta = 2 * Math.Sqrt(a) * alpha;
        double b0, b1, b2, a0, a1, a2;
        switch (band.Type)
        {
            case "lowShelf": b0 = a * ((a + 1) - (a - 1) * c + beta); b1 = 2 * a * ((a - 1) - (a + 1) * c); b2 = a * ((a + 1) - (a - 1) * c - beta); a0 = (a + 1) + (a - 1) * c + beta; a1 = -2 * ((a - 1) + (a + 1) * c); a2 = (a + 1) + (a - 1) * c - beta; break;
            case "highShelf": b0 = a * ((a + 1) + (a - 1) * c + beta); b1 = -2 * a * ((a - 1) + (a + 1) * c); b2 = a * ((a + 1) + (a - 1) * c - beta); a0 = (a + 1) - (a - 1) * c + beta; a1 = 2 * ((a - 1) - (a + 1) * c); a2 = (a + 1) - (a - 1) * c - beta; break;
            case "highPass": b0 = (1 + c) / 2; b1 = -(1 + c); b2 = b0; a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha; break;
            case "lowPass": b0 = (1 - c) / 2; b1 = 1 - c; b2 = b0; a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha; break;
            case "notch": b0 = 1; b1 = -2 * c; b2 = 1; a0 = 1 + alpha; a1 = -2 * c; a2 = 1 - alpha; break;
            default: b0 = 1 + alpha * a; b1 = -2 * c; b2 = 1 - alpha * a; a0 = 1 + alpha / a; a1 = -2 * c; a2 = 1 - alpha / a; break;
        }
        double w = 2 * Math.PI * Math.Clamp(frequency, MinimumFrequency, MaximumFrequency) / 48000;
        double nr = b0 + b1 * Math.Cos(w) + b2 * Math.Cos(2 * w), ni = -b1 * Math.Sin(w) - b2 * Math.Sin(2 * w);
        double dr = a0 + a1 * Math.Cos(w) + a2 * Math.Cos(2 * w), di = -a1 * Math.Sin(w) - a2 * Math.Sin(2 * w);
        double result = 10 * Math.Log10(Math.Max(1e-12, (nr * nr + ni * ni) / Math.Max(1e-20, dr * dr + di * di)));
        return double.IsFinite(result) ? result : 0;
    }
}
