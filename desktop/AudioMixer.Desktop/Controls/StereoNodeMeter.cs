using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;

namespace AudioMixer.Desktop.Controls;

/// <summary>One or two real peak channels, with display-only peak hold and release.</summary>
internal class StereoNodeMeter : FrameworkElement
{
    internal const double PeakHoldSeconds = .1;
    internal const double ReleaseDbPerSecond = 24;
    internal const double ClipHoldSeconds = 2;
    internal const double ClipFadeSeconds = 1;
    private const double FloorDb = -60;
    private const double FloorAmplitude = .001;
    private const double ReducedPeriod = .25;
    private const double FreshnessSeconds = .5;
    private readonly DispatcherTimer _timer;
    private bool _initialized, _attached, _pending, _pendingClip;
    private bool _practiceSnapshot;
    private double _targetLeft, _targetRight, _pendingLeft, _pendingRight;
    private double _holdLeft, _holdRight, _lastAdvance, _lastSample, _lastPresentation;
    private double _lastClipSample = double.NegativeInfinity;
    private Brush? _paletteBrush;

    public double Level => Math.Max(LeftLevel, RightLevel);
    public double LeftLevel { get; private set; }
    public double RightLevel { get; private set; }
    public bool Clipped { get; private set; }
    public double RetainedPeakLeft { get; private set; }
    public double RetainedPeakRight { get; private set; }
    public bool HasPeakReadings { get; private set; }
    public bool ClipLatched { get; private set; }
    internal double ClipIndicatorOpacity { get; private set; }
    public event EventHandler? PeakReadingsChanged;
    public bool Muted { get; private set; }
    public bool ReducedAnimation { get; private set; }
    public int Channels { get; private set; } = 1;
    internal bool IsAnimationRunning => _timer.IsEnabled;

    internal void FreezePracticeSnapshot() { _practiceSnapshot = true; _timer.Stop(); }

    public StereoNodeMeter()
    {
        Width = 28; MinWidth = 24; MinHeight = 90;
        SnapsToDevicePixels = true; UseLayoutRounding = true;
        AutomationProperties.SetName(this, "Audio level");
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Off);
        ToolTip = NodeToolTip.Create("Audio level", "Bars · Current channel sample peaks",
            "Stereo · Separate left and right levels", "Yellow · Above −12 dBFS", "Red · Above −3 dBFS", "Lit cap · Recent clipping");
        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher);
        _timer.Tick += (_, _) => Advance(Now);
        Loaded += (_, _) =>
        {
            if (_attached) return;
            _attached = true; ConsoleTheme.Changed += ThemeChanged; RefreshTimer();
        };
        Unloaded += (_, _) =>
        {
            _attached = false; _timer.Stop(); ConsoleTheme.Changed -= ThemeChanged;
        };
        IsVisibleChanged += (_, _) => RefreshTimer();
    }

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private static double Sample(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    // Older snapshots supply only a single aggregate channel. Do not fabricate
    // independent left/right telemetry for those callers.
    public void Update(double level, bool clipped, bool muted, bool reduced, double now)
        => Update(level, 0, 1, clipped, muted, reduced, now);

    public void Update(double left, double right, int channels, bool clipped, bool muted, bool reduced, double now, bool observePeak = true)
    {
        channels = channels == 1 ? 1 : 2;
        left = muted ? 0 : Sample(left);
        right = muted || channels == 1 ? 0 : Sample(right);
        clipped &= !muted;
        now = double.IsFinite(now) ? now : _initialized ? _lastAdvance : 0;
        var before = Presentation();
        bool channelsChanged = channels != Channels;
        bool resetPeakReadings = !_initialized || muted || now < _lastAdvance || channels != Channels;
        bool reset = !_initialized || now < _lastAdvance || muted != Muted || reduced != ReducedAnimation || channels != Channels;
        Muted = muted; ReducedAnimation = reduced; Channels = channels;
        if (reset)
        {
            _initialized = true; _lastAdvance = _lastSample = _lastPresentation = now;
            _targetLeft = LeftLevel = left; _targetRight = RightLevel = right;
            _holdLeft = left > 0 ? now + PeakHoldSeconds : now;
            _holdRight = right > 0 ? now + PeakHoldSeconds : now;
            Clipped = clipped; ClearPending();
        }
        else if (muted)
        {
            _lastAdvance = _lastSample = _lastPresentation = now;
            _targetLeft = _targetRight = LeftLevel = RightLevel = 0;
            Clipped = false; ClearPending();
        }
        else if (reduced)
        {
            _lastSample = now; _targetLeft = left; _targetRight = right;
            _pending = true; _pendingLeft = Math.Max(_pendingLeft, left); _pendingRight = Math.Max(_pendingRight, right);
            _pendingClip |= clipped;
            if (clipped && !Clipped || now - _lastPresentation >= ReducedPeriod) PublishReduced(now);
        }
        else
        {
            AdvanceLevels(now);
            _lastSample = now; _targetLeft = left; _targetRight = right;
            Attack(left, right, now);
            Clipped = clipped;
        }
        // Retained measurements use fresh telemetry, not the animated bars.
        // Reapplying a snapshot can still update presentation without recording
        // it again after a deliberate clear. Motion preferences do not reset history.
        double retainedLeft = resetPeakReadings ? 0 : RetainedPeakLeft;
        double retainedRight = resetPeakReadings ? 0 : RetainedPeakRight;
        bool hasReadings = !resetPeakReadings && HasPeakReadings;
        bool clipLatched = !resetPeakReadings && ClipLatched;
        if (resetPeakReadings) ClearClipIndicator();
        if (observePeak && !muted)
        {
            retainedLeft = Math.Max(retainedLeft, Math.Min(left, 1));
            retainedRight = Math.Max(retainedRight, Math.Min(right, 1));
            hasReadings = true;
            // Native peaks are capped at full scale; exactly 1 is not proof
            // of clipping, and the flag does not identify a particular lane.
            clipLatched |= clipped;
            if (clipped) { _lastClipSample = now; SetClipIndicatorOpacity(1); }
        }
        AdvanceClipIndicator(now);
        SetPeakReadings(retainedLeft, retainedRight, hasReadings, clipLatched, channelsChanged);
        InvalidateIfChanged(before);
        RefreshTimer();
    }

    /// <summary>Clear observed history without changing current levels or their release.</summary>
    public void ClearPeakReadings()
    {
        ClearClipIndicator();
        SetPeakReadings(0, 0, false, false);
        RefreshTimer();
    }

    private void ClearClipIndicator()
    {
        _lastClipSample = double.NegativeInfinity;
        SetClipIndicatorOpacity(0);
    }

    private void AdvanceClipIndicator(double now)
    {
        // The cap is a recent-event cue; the independent hover history keeps
        // its maxima. Only a fresh clipped sample can extend this deadline.
        double fadeElapsed = now - _lastClipSample - ClipHoldSeconds;
        if (ReducedAnimation && fadeElapsed > 0)
            fadeElapsed = Math.Floor(fadeElapsed / ReducedPeriod) * ReducedPeriod;
        double opacity = Muted ? 0 : Math.Clamp(1 - fadeElapsed / ClipFadeSeconds, 0, 1);
        SetClipIndicatorOpacity(Math.Min(ClipIndicatorOpacity, opacity));
    }

    private void SetClipIndicatorOpacity(double opacity)
    {
        if (ClipIndicatorOpacity == opacity) return;
        ClipIndicatorOpacity = opacity;
        InvalidateVisual();
    }

    private void SetPeakReadings(double left, double right, bool hasReadings, bool clipLatched, bool channelsChanged = false)
    {
        if (!channelsChanged && RetainedPeakLeft == left && RetainedPeakRight == right && HasPeakReadings == hasReadings && ClipLatched == clipLatched) return;
        RetainedPeakLeft = left; RetainedPeakRight = right;
        HasPeakReadings = hasReadings; ClipLatched = clipLatched;
        PeakReadingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Advance display time without supplying or changing an audio sample.</summary>
    public void Advance(double now)
    {
        if (!_initialized || !double.IsFinite(now) || now < _lastAdvance) return;
        var before = Presentation();
        if (ReducedAnimation)
        {
            if (now - _lastPresentation >= ReducedPeriod) PublishReduced(now);
        }
        else
        {
            AdvanceLevels(now);
            if (now - _lastSample >= FreshnessSeconds) Clipped = false;
        }
        AdvanceClipIndicator(now);
        InvalidateIfChanged(before);
        RefreshTimer();
    }

    private void PublishReduced(double now)
    {
        // Retain every observed peak between presentations. A timer tick must
        // not discard a short transient just because a quieter sample followed.
        AdvanceLevels(now);
        if (_pending)
        {
            Attack(_pendingLeft, _pendingRight, now);
            Clipped = _pendingClip;
        }
        else if (now - _lastSample >= FreshnessSeconds) Clipped = false;
        ClearPending(); _lastPresentation = now;
    }

    private void Attack(double left, double right, double now)
    {
        if (left >= LeftLevel)
        {
            LeftLevel = left;
            if (left > 0) _holdLeft = now + PeakHoldSeconds;
        }
        if (right >= RightLevel)
        {
            RightLevel = right;
            if (right > 0) _holdRight = now + PeakHoldSeconds;
        }
    }

    private void AdvanceLevels(double now)
    {
        if (now <= _lastAdvance) return;
        // A missing telemetry update is not evidence of a sustained tone. Once
        // it becomes stale, release towards silence at the same measured rate.
        // Splitting at the expiry instant makes coarse/fine Advance calls agree.
        double expiry = _lastSample + FreshnessSeconds;
        if (_lastAdvance < expiry && now >= expiry)
        {
            ReleaseTo(expiry, _targetLeft, _targetRight);
            ReleaseTo(now, 0, 0);
        }
        else ReleaseTo(now, now >= expiry ? 0 : _targetLeft, now >= expiry ? 0 : _targetRight);
    }

    private void ReleaseTo(double now, double left, double right)
    {
        LeftLevel = Release(LeftLevel, left, Math.Max(0, now - Math.Max(_lastAdvance, _holdLeft)));
        RightLevel = Channels == 1 ? 0 : Release(RightLevel, right, Math.Max(0, now - Math.Max(_lastAdvance, _holdRight)));
        _lastAdvance = now;
    }

    private static double Release(double displayed, double target, double seconds)
    {
        if (displayed <= target || seconds <= 0) return displayed;
        double released = displayed * Math.Pow(10, -ReleaseDbPerSecond * seconds / 20);
        return Math.Max(target, released < FloorAmplitude ? 0 : released);
    }

    private void ClearPending() { _pending = _pendingClip = false; _pendingLeft = _pendingRight = 0; }
    private (double Left, double Right, bool Clip, bool Mute, bool Reduced, int Channels) Presentation()
        => (LeftLevel, RightLevel, Clipped, Muted, ReducedAnimation, Channels);
    private void InvalidateIfChanged((double Left, double Right, bool Clip, bool Mute, bool Reduced, int Channels) previous)
    {
        var brush = ConsoleTheme.Get("ConsoleMeterMidBrush");
        if (previous != Presentation() || !ReferenceEquals(_paletteBrush, brush))
        {
            _paletteBrush = brush;
            InvalidateVisual();
        }
    }

    private void RefreshTimer()
    {
        if (_practiceSnapshot || !_attached || !IsVisible || !_initialized || Muted || Level <= 0 && _pendingLeft <= 0 && _pendingRight <= 0 && !_pendingClip && !Clipped && ClipIndicatorOpacity <= 0)
        {
            _timer.Stop(); return;
        }
        double interval;
        if (ReducedAnimation) interval = ReducedPeriod;
        else if (LeftLevel > _targetLeft || RightLevel > _targetRight || _lastAdvance >= _lastSample + FreshnessSeconds) interval = 1.0 / 30;
        else interval = Math.Max(1.0 / 30, _lastSample + FreshnessSeconds - _lastAdvance);
        if (!ReducedAnimation && ClipIndicatorOpacity > 0)
        {
            double untilFade = _lastClipSample + ClipHoldSeconds - _lastAdvance;
            interval = untilFade > 0 ? Math.Min(interval, Math.Max(1.0 / 30, untilFade)) : 1.0 / 30;
        }
        var period = TimeSpan.FromSeconds(interval);
        if (_timer.Interval != period) _timer.Interval = period;
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void ThemeChanged(object? sender, EventArgs args) { _paletteBrush = ConsoleTheme.Get("ConsoleMeterMidBrush"); InvalidateVisual(); }

    /// <summary>Actual bar bounds, excluding the small clipping indicators.</summary>
    internal Rect[] GetLaneBounds()
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return [];
        const double top = 7, gap = 4;
        double barHeight = Math.Max(0, height - top - 2);
        double barWidth = Math.Min(Channels == 1 ? 12 : 10, Math.Max(0, (width - 4 - (Channels == 1 ? 0 : gap)) / Channels));
        double total = barWidth * Channels + (Channels == 1 ? 0 : gap);
        double x = (width - total) / 2;
        return Channels == 1
            ? [new Rect(x, top, barWidth, barHeight)]
            : [new Rect(x, top, barWidth, barHeight), new Rect(x + barWidth + gap, top, barWidth, barHeight)];
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var lanes = GetLaneBounds();
        for (int i = 0; i < lanes.Length; i++)
        {
            var lane = lanes[i];
            if (lane.Width <= 0 || lane.Height <= 0) continue;
            drawing.DrawRectangle(ConsoleTheme.Get("ConsoleMeterWellBrush"), null, lane);
            drawing.DrawRectangle(null, new Pen(ConsoleTheme.Get("ConsoleBorderBrush"), 1),
                new Rect(lane.Left + .5, lane.Top + .5, Math.Max(0, lane.Width - 1), Math.Max(0, lane.Height - 1)));
            var cap = new Rect(lane.Left, 1, lane.Width, 3);
            drawing.DrawRectangle(ConsoleTheme.Get("ConsoleMeterUnlitBrush"), null, cap);
            if (!Muted && ClipIndicatorOpacity > 0)
            {
                drawing.PushOpacity(ClipIndicatorOpacity);
                drawing.DrawRectangle(ConsoleTheme.Get("ConsoleMeterHighBrush"), null, cap);
                drawing.Pop();
            }
            double value = i == 0 ? LeftLevel : RightLevel;
            double db = !Muted && value > 0 ? Math.Clamp(20 * Math.Log10(value), FloorDb, 0) : FloorDb;
            // The entire scale stays visible at a subdued intensity. The same
            // colours fill from the bottom as the measured signal reaches them.
            drawing.PushOpacity(.2);
            DrawZone(FloorDb, -12, "ConsoleMeterLowBrush", -12);
            DrawZone(-12, -3, "ConsoleMeterMidBrush", -3);
            DrawZone(-3, 0, "ConsoleMeterHighBrush", 0);
            drawing.Pop();
            DrawZone(FloorDb, -12, "ConsoleMeterLowBrush");
            DrawZone(-12, -3, "ConsoleMeterMidBrush");
            DrawZone(-3, 0, "ConsoleMeterHighBrush");
            void DrawZone(double low, double high, string brush, double? fillTo = null)
            {
                double end = Math.Min(fillTo ?? db, high);
                if (end <= low) return;
                double y = lane.Bottom - (end - FloorDb) / -FloorDb * lane.Height;
                double fillHeight = (end - low) / -FloorDb * lane.Height;
                drawing.DrawRectangle(ConsoleTheme.Get(brush), null,
                    new Rect(lane.Left + 1, y, Math.Max(0, lane.Width - 2), fillHeight));
            }
        }
    }
}
