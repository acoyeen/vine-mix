using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioMixer.Desktop.Controls;

/// <summary>A vector XY editor. Audio processing and edit cadence stay with its owner.</summary>
internal sealed class VoiceColorPad : FrameworkElement
{
    private bool _dragging, _bypassed, _themeSubscribed;
    private Point _lastPointer;
    private readonly Dictionary<(string Text, Brush Brush, double Size, double Dpi), FormattedText> _labels = [];
    private FontFamily? _labelFont;
    private string _labelCulture = "";
    private double _labelDpi;
    internal double Tone { get; private set; }
    internal double Ambience { get; private set; }
    internal bool IsAdjusting => _dragging;
    internal int SettingsRefreshCount { get; private set; }
    internal event Action<double, double>? PositionEdited;
    public event EventHandler? EditGestureStarted;
    public event EventHandler? EditGestureEnded;
    internal Rect PadBounds
    {
        get
        {
            double side = Math.Max(1, Math.Min(ActualWidth - 30, ActualHeight - 88));
            return new Rect((ActualWidth - side - 24) / 2, (ActualHeight - side - 84) / 2,
                side + 24, side + 84);
        }
    }
    // A square grid occupies the centre of the well. Internal caption strips
    // stay outside the handle's travel, including its radius at every extreme.
    internal Rect TravelBounds
    {
        get { Rect bounds = PadBounds; return new Rect(bounds.Left + 12, bounds.Top + 42, bounds.Width - 24, bounds.Height - 84); }
    }
    internal Point HandlePosition => new(TravelBounds.Left + (Tone + 1) * .5 * TravelBounds.Width,
        TravelBounds.Bottom - Ambience * TravelBounds.Height);
    internal Rect HandleBounds => new(HandlePosition.X - 10, HandlePosition.Y - 10, 20, 20);
    internal Point NeutralPosition => new(TravelBounds.Left + TravelBounds.Width / 2, TravelBounds.Bottom);
    internal bool NeutralMarkerVisible => (HandlePosition - NeutralPosition).Length >= 23;
    internal string PositionDescription => Tone == 0 && Ambience == 0 ? "Neutral"
        : (Tone == 0 ? "Balanced" : $"{(Tone < 0 ? "Warm" : "Bright")} {Math.Abs(Tone) * 100:0.#}%")
            + " · " + (Ambience == 0 ? "Natural" : $"Broadcast {Ambience * 100:0.#}%");
    internal IReadOnlyList<(string Text, Rect Bounds)> LabelBounds
    {
        get
        {
            var result = new List<(string Text, Rect Bounds)>();
            foreach (var label in LayoutLabels()) result.Add((label.Text, label.Bounds));
            return result;
        }
    }

    public VoiceColorPad()
    {
        Height = 388; Focusable = true; Cursor = Cursors.Cross;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        ToolTip = NodeToolTip.Create("Voice Color", "Tone · Warm to Bright",
            "Character · Natural to Broadcast", "Neutral · Original voice character");
        AutomationProperties.SetName(this, "Voice Color pad");
        AutomationProperties.SetHelpText(this,
            "Left and right adjust Warm to Bright. Up adds broadcast character; down keeps the voice natural. Arrow keys adjust by one percent; Shift uses a tenth of a percent. Home or double-click inside the pad resets to neutral. Tone and Character fields below accept exact values.");
        UpdateDescription();
        Loaded += (_, _) => { if (!_themeSubscribed) { ConsoleTheme.Changed += ThemeChanged; _themeSubscribed = true; } };
        Unloaded += (_, _) => { if (_themeSubscribed) { ConsoleTheme.Changed -= ThemeChanged; _themeSubscribed = false; } EndAdjustment(); };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) EndAdjustment(); InvalidateVisual(); };
        // Canvas zoom keeps ownership of the wheel. End a held pad gesture so
        // the changed coordinate transform cannot turn its next move into a jump.
        AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => EndAdjustment()), handledEventsToo: true);
    }

    internal void Apply(double tone, double ambience, bool bypassed)
    {
        tone = Math.Clamp(double.IsFinite(tone) ? tone : 0, -1, 1);
        ambience = Math.Clamp(double.IsFinite(ambience) ? ambience : 0, 0, 1);
        if (Tone == tone && Ambience == ambience && _bypassed == bypassed) return;
        Tone = tone; Ambience = ambience; _bypassed = bypassed;
        SettingsRefreshCount++; UpdateDescription(); InvalidateVisual();
    }

    internal bool SetPosition(double tone, double ambience)
    {
        if (!IsEnabled || !double.IsFinite(tone) || !double.IsFinite(ambience)) return false;
        double nextTone = Math.Clamp(tone, -1, 1), nextAmbience = Math.Clamp(ambience, 0, 1);
        if (Tone == nextTone && Ambience == nextAmbience) return false;
        Tone = nextTone; Ambience = nextAmbience;
        UpdateDescription(); InvalidateVisual(); PositionEdited?.Invoke(Tone, Ambience); return true;
    }

    internal bool ResetToNeutral()
    {
        EndAdjustment(); return SetPosition(0, 0);
    }

    internal bool BeginAdjustment(Point point)
    {
        if (!IsEnabled || !Finite(point)) return false;
        if (!AdjustmentBounds.Contains(point)) return false;
        bool starting = !_dragging;
        _dragging = true; _lastPointer = point;
        if (starting) EditGestureStarted?.Invoke(this, EventArgs.Empty);
        // Picking up the handle preserves the grab offset. Clicking elsewhere
        // places it directly, then all movement is relative for Shift precision.
        if ((point - HandlePosition).Length > 12)
            SetPosition((point.X - TravelBounds.Left) / TravelBounds.Width * 2 - 1,
                (TravelBounds.Bottom - point.Y) / TravelBounds.Height);
        return true;
    }

    internal void ContinueAdjustment(Point point, bool fine)
    {
        if (!_dragging || !IsEnabled || !Finite(point)) return;
        Vector delta = point - _lastPointer; _lastPointer = point;
        double scale = fine ? .1 : 1;
        SetPosition(Tone + delta.X / TravelBounds.Width * 2 * scale,
            Ambience - delta.Y / TravelBounds.Height * scale);
    }

    internal void EndAdjustment()
    {
        bool ending = _dragging;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (ending) EditGestureEnded?.Invoke(this, EventArgs.Empty);
    }

    internal bool ActivateAt(Point point, int clickCount)
    {
        if (!IsEnabled || !Finite(point) || !AdjustmentBounds.Contains(point)) return false;
        if (clickCount == 2) { ResetToNeutral(); return true; }
        return BeginAdjustment(point);
    }

    internal bool AdjustKey(Key key, ModifierKeys modifiers)
    {
        if (!IsEnabled || (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return false;
        double step = modifiers.HasFlag(ModifierKeys.Shift) ? .001 : .01;
        switch (key)
        {
            case Key.Left: SetPosition(Tone - step, Ambience); break;
            case Key.Right: SetPosition(Tone + step, Ambience); break;
            case Key.Down: SetPosition(Tone, Ambience - step); break;
            case Key.Up: SetPosition(Tone, Ambience + step); break;
            case Key.Home: ResetToNeutral(); break;
            case Key.Escape: EndAdjustment(); break;
            default: return false;
        }
        return true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled || Keyboard.IsKeyDown(Key.Space)) return;
        if (!ActivateAt(e.GetPosition(this), e.ClickCount)) return;
        Focus();
        if (_dragging && !CaptureMouse()) EndAdjustment();
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        ContinueAdjustment(e.GetPosition(this), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        EndAdjustment(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { EndAdjustment(); base.OnLostMouseCapture(e); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { InvalidateVisual(); base.OnGotKeyboardFocus(e); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { EndAdjustment(); InvalidateVisual(); base.OnLostKeyboardFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsKeyboardFocused && AdjustKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private void UpdateDescription() => AutomationProperties.SetItemStatus(this,
        $"Tone {Tone * 100:0.#} percent; character {Ambience * 100:0.#} percent; {PositionDescription}" + (_bypassed ? "; bypassed" : ""));
    // Include the full handle at the plot boundary without making captions editable.
    private Rect AdjustmentBounds
    {
        get { Rect bounds = TravelBounds; bounds.Inflate(10, 10); return bounds; }
    }
    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private void ThemeChanged(object? sender, EventArgs args) { _labels.Clear(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 30 || ActualHeight < 76) return;
        Rect bounds = PadBounds, travel = TravelBounds;
        Brush muted = ConsoleTheme.Get("ConsoleMutedBrush");
        Brush line = ConsoleTheme.Get("ConsoleControlBorderBrush"), accent = ConsoleTheme.Get("ConsoleABrush");
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.DrawRoundedRectangle(ConsoleTheme.Get("ConsoleWellBrush"),
            new Pen(ConsoleTheme.Get(IsKeyboardFocused ? "ConsoleFocusBrush" : "ConsoleControlBorderBrush"), IsKeyboardFocused ? 1.5 : 1), bounds, 4, 4);
        dc.PushOpacity(_bypassed || !IsEnabled ? .5 : 1);
        // Quarter divisions restore the pad's visual map. Keep the central
        // axes slightly stronger and the captions clear of every grid line.
        foreach (double fraction in new[] { .25, .5, .75 })
        {
            double x = travel.Left + travel.Width * fraction, y = travel.Top + travel.Height * fraction;
            dc.PushOpacity(fraction == .5 ? .9 : .55);
            dc.DrawLine(new Pen(line, 1), new Point(x, travel.Top), new Point(x, travel.Bottom));
            dc.DrawLine(new Pen(line, 1), new Point(travel.Left, y), new Point(travel.Right, y));
            dc.Pop();
        }
        dc.PushOpacity(.45);
        dc.DrawRectangle(null, new Pen(line, 1), travel);
        dc.Pop();
        if (NeutralMarkerVisible)
        {
            var neutral = NeutralPosition;
            dc.PushOpacity(.7);
            dc.DrawLine(new Pen(muted, 1), neutral + new Vector(0, -3), neutral + new Vector(3, 0));
            dc.DrawLine(new Pen(muted, 1), neutral + new Vector(3, 0), neutral + new Vector(0, 3));
            dc.DrawLine(new Pen(muted, 1), neutral + new Vector(0, 3), neutral + new Vector(-3, 0));
            dc.DrawLine(new Pen(muted, 1), neutral + new Vector(-3, 0), neutral + new Vector(0, -3));
            dc.Pop();
        }
        Point handle = HandlePosition;
        dc.PushOpacity(.3);
        dc.DrawLine(new Pen(accent, 1), new Point(handle.X, travel.Bottom), handle);
        dc.Pop();
        dc.DrawEllipse(ConsoleTheme.Get("ConsoleWellBrush"), new Pen(accent, 2), handle, 9, 9);
        dc.DrawEllipse(accent, null, handle, 3, 3);
        dc.Pop();
        foreach (var label in LayoutLabels()) dc.DrawText(label.Formatted, label.Bounds.TopLeft);
    }

    private IEnumerable<(string Text, Rect Bounds, FormattedText Formatted)> LayoutLabels()
    {
        Rect bounds = PadBounds;
        double centre = bounds.Left + bounds.Width / 2;
        Brush text = ConsoleTheme.Get("ConsoleTextBrush");
        yield return Label("Broadcast", text, 15, new Point(centre, bounds.Top + 8), TextAlignment.Center);
        yield return Label("Warm", text, 15, new Point(bounds.Left + 12, bounds.Bottom - 28), TextAlignment.Left);
        yield return Label("Natural", text, 15, new Point(centre, bounds.Bottom - 28), TextAlignment.Center);
        yield return Label("Bright", text, 15, new Point(bounds.Right - 12, bounds.Bottom - 28), TextAlignment.Right);
    }

    private (string Text, Rect Bounds, FormattedText Formatted) Label(string label, Brush brush, double size, Point position, TextAlignment alignment)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FontFamily family = TextElement.GetFontFamily(this);
        string culture = CultureInfo.CurrentUICulture.Name;
        if (_labelDpi != dpi || !Equals(_labelFont, family) || _labelCulture != culture)
        { _labels.Clear(); _labelDpi = dpi; _labelFont = family; _labelCulture = culture; }
        var key = (label, brush, size, dpi);
        if (!_labels.TryGetValue(key, out var text))
        {
            if (_labels.Count > 24) _labels.Clear();
            text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size, brush, dpi); _labels.Add(key, text);
        }
        double offset = alignment == TextAlignment.Right ? text.Width : alignment == TextAlignment.Center ? text.Width / 2 : 0;
        return (label, new Rect(position.X - offset, position.Y, text.Width, text.Height), text);
    }
}
