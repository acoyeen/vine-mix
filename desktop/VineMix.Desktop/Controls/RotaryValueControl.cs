using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VineMix.Desktop.Controls;

/// <summary>A rotary range control with actual-unit accessibility and precise numeric entry.</summary>
internal class RotaryValueControl : StackPanel
{
    private readonly Slider _range;
    private readonly TextBox _entry;
    private readonly DialFace _face;
    private readonly Grid _dialSlot;
    private readonly ContextMenu _entryMenu;
    private readonly Action<double> _changed;
    private readonly double _minimum, _maximum, _step, _smallChange, _largeChange;
    private readonly bool _logarithmic, _integer;
    private readonly string _label, _unit, _format, _description;
    private bool _applying, _dragging, _entryHovered, _entryFocused, _entryInvalid;
    private bool _entryDirty, _renderingEntry, _entryMenuActive;
    private double _lastY, _dragPosition, _defaultValue;
    private Brush? _lastTextBrush;
    private object? _entryHelp;
    public event EventHandler? EditGestureStarted;
    public event EventHandler? EditGestureEnded;
    internal int FaceRefreshCount { get; private set; }
    internal double Value => _range.Value;
    internal double DefaultValue => _defaultValue;
    internal double NormalizedValue => Normalize(_range.Value);
    internal bool HasVisibleKeyboardFocus => _face.Focused;
    internal bool HasPendingEntry => _entryDirty || _entryInvalid;
    // Align labels and values across a mixed-size row without enlarging the
    // secondary dial itself. Rows containing only secondary dials stay compact.
    internal void SetDialRowHeight(double height) => _dialSlot.Height = height;

    public RotaryValueControl(string label, double minimum, double maximum, double step, string unit,
        double defaultValue, Action<double> changed, bool logarithmic = false, bool compact = false,
        string detail = "", double? smallChange = null, double? largeChange = null,
        bool tile = false, bool integer = false, string? format = null, bool primary = false,
        string description = "")
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum >= maximum
            || !double.IsFinite(step) || step <= 0 || (logarithmic && minimum <= 0))
            throw new ArgumentOutOfRangeException(nameof(minimum), "A dial requires a finite increasing range and a positive step.");
        if (integer && (minimum != Math.Truncate(minimum) || maximum != Math.Truncate(maximum)))
            throw new ArgumentOutOfRangeException(nameof(minimum), "A whole-number dial requires whole-number bounds.");
        _minimum = minimum; _maximum = maximum; _integer = integer; _logarithmic = logarithmic;
        _step = integer ? Math.Max(1, Math.Round(step)) : step;
        _smallChange = smallChange ?? _step; _largeChange = largeChange ?? _step * 10;
        if (!double.IsFinite(_smallChange) || _smallChange <= 0 || !double.IsFinite(_largeChange) || _largeChange <= 0)
            throw new ArgumentOutOfRangeException(nameof(smallChange));
        if (integer)
        {
            _smallChange = Math.Max(1, Math.Round(_smallChange));
            _largeChange = Math.Max(1, Math.Round(_largeChange));
        }
        _label = label; _unit = unit; _changed = changed;
        _description = description.Length > 0 ? description : label switch
        {
            "Level" => "Adjusts the audio level. 0 dB leaves it unchanged.",
            "Low" => "Boosts or cuts bass around the 200 Hz shelf.",
            "Mid" => "Boosts or cuts midrange around 1 kHz.",
            "High" => "Boosts or cuts treble around the 5 kHz shelf.",
            "Frequency" => "Sets the frequency affected by the selected EQ band.",
            "Band gain" => "Boosts or cuts the selected EQ band.",
            "Width / Q" => "Sets the band's width. Higher Q affects a narrower range of frequencies.",
            _ => "Adjusts " + label.ToLowerInvariant() + "."
        };
        _defaultValue = Bound(defaultValue);
        _format = integer ? "0" : format ?? StepFormat(step, unit);
        Margin = new Thickness(0, 0, 0, 4);
        if (tile) Width = 136;
        bool grouped = compact || tile;

        var row = new Grid(); Children.Add(row);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = GraphWorkspace.Text(label, grouped ? 12 : 13, true);
        if (grouped) { title.Height = 18; title.TextAlignment = TextAlignment.Center; }
        labels.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var detailText = GraphWorkspace.Text(detail, 12);
            detailText.Margin = new Thickness(0, 5, 0, 0);
            detailText.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
            labels.Children.Add(detailText);
        }
        // Importance selects the face size independently of row/tile layout.
        // All variants retain the same exact-entry styling and behavior.
        double faceSize = grouped && !primary ? 58 : 86;
        var dial = _dialSlot = new Grid { Height = grouped ? faceSize : 88, Margin = new Thickness(0, 2, 0, 1) };
        _face = new DialFace { Width = faceSize, Height = faceSize, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        dial.Children.Add(_face);
        _range = new Slider { Minimum = minimum, Maximum = maximum, SmallChange = _smallChange,
            LargeChange = _largeChange, Height = faceSize, Width = faceSize,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Arrow };
        var template = new ControlTemplate(typeof(Slider));
        var target = new FrameworkElementFactory(typeof(Border));
        target.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        template.VisualTree = target; _range.Template = template;
        AutomationProperties.SetName(_range, label);
        dial.Children.Add(_range);
        _entry = new TextBox { Height = 28, Width = 110, FontSize = 12,
            TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2), MaxLength = 24, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = grouped ? HorizontalAlignment.Center : HorizontalAlignment.Right };
        _entry.SetResourceReference(StyleProperty, "NodeNumericEntry");
        AutomationProperties.SetName(_entry, label + " value");
        _entryMenu = new ContextMenu();
        var applyEntry = new MenuItem { Header = "Apply value" };
        var cancelEntry = new MenuItem { Header = "Cancel edit" };
        applyEntry.Click += (_, _) => TryCommitPendingEntry();
        cancelEntry.Click += (_, _) => CancelPendingEntry();
        _entryMenu.Items.Add(applyEntry); _entryMenu.Items.Add(cancelEntry);
        _entryMenu.Items.Add(new Separator());
        foreach (var command in new[] { ApplicationCommands.Undo, ApplicationCommands.Cut,
            ApplicationCommands.Copy, ApplicationCommands.Paste, ApplicationCommands.SelectAll })
            _entryMenu.Items.Add(new MenuItem { Header = command.Text, Command = command, CommandTarget = _entry });
        _entry.ContextMenu = _entryMenu;
        _entry.ContextMenuOpening += (_, _) =>
        {
            // Opening a menu moves keyboard focus before its action runs. Keep
            // that move inside this edit so Cancel cannot accidentally apply.
            _entryMenuActive = true;
            applyEntry.IsEnabled = cancelEntry.IsEnabled = HasPendingEntry && IsEnabled && !_entry.IsReadOnly;
        };
        _entryMenu.Closed += (_, _) =>
        {
            _entryMenuActive = false;
            // WPF restores focus while leaving menu mode. Inspect it after
            // that transition, so a text-menu action can return to editing.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (!_entryMenuActive && !_entry.IsKeyboardFocusWithin) FinishEntryOnFocusLoss();
            }));
        };
        _entry.TextChanged += (_, _) => { if (!_renderingEntry) _entryDirty = true; };

        if (grouped)
        {
            row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            labels.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(dial, 1); Grid.SetRow(_entry, 2);
        }
        else
        {
            row.ColumnDefinitions.Add(new());
            row.ColumnDefinitions.Add(new() { Width = new GridLength(88) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(118) });
            labels.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(dial, 1); Grid.SetColumn(_entry, 2);
        }
        row.Children.Add(labels); row.Children.Add(dial); row.Children.Add(_entry);
        RefreshHelp();

        _range.ValueChanged += (_, _) =>
        {
            // UI Automation can write the underlying range directly too.
            double bounded = Bound(_range.Value);
            if (_range.Value != bounded) { _range.Value = bounded; return; }
            // A stored value change supersedes a draft, including external
            // automation writes. Unchanged telemetry/theme refreshes do not.
            Render(); RenderEntry();
            if (!_applying) _changed(_range.Value);
        };
        _range.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _range.Focus(); e.Handled = true;
            if (e.ClickCount == 2) { ResetToDefault(); return; }
            BeginAdjustment(e.GetPosition(this).Y);
            if (_range.CaptureMouse()) _range.Cursor = Cursors.SizeNS;
            else StopAdjustment();
        };
        _range.PreviewMouseMove += (_, e) =>
        {
            if (!_dragging) return;
            ContinueAdjustment(e.GetPosition(this).Y, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true;
        };
        _range.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            StopAdjustment(); e.Handled = true;
        };
        _range.LostMouseCapture += (_, _) => StopAdjustment();
        // The workspace handles its preview event first and zooms. If used outside
        // that canvas, swallowing the wheel prevents Slider's value-changing default.
        _range.PreviewMouseWheel += (_, e) => e.Handled = true;
        _entry.PreviewMouseWheel += (_, e) => e.Handled = true;
        _range.PreviewKeyDown += (_, e) =>
        {
            if (AdjustWithKey(e.Key)) e.Handled = true;
        };
        _range.GotKeyboardFocus += (_, _) => { _face.Focused = true; _face.InvalidateVisual(); };
        _range.LostKeyboardFocus += (_, _) => { StopAdjustment(); _face.Focused = false; _face.InvalidateVisual(); };
        _entry.GotKeyboardFocus += (_, _) =>
        {
            _entryFocused = true;
            BeginTextEntry();
        };
        _entry.LostKeyboardFocus += (_, _) =>
        {
            _entryFocused = false;
            if (!_entryMenuActive) FinishEntryOnFocusLoss();
            RefreshEntryBorder();
        };
        _entry.MouseEnter += (_, _) => { _entryHovered = true; RefreshEntryBorder(); };
        _entry.MouseLeave += (_, _) => { _entryHovered = false; RefreshEntryBorder(); };
        _entry.PreviewMouseDoubleClick += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            ResetToDefault(); _entry.SelectAll(); e.Handled = true;
        };
        _entry.PreviewKeyDown += (_, e) =>
        {
            if (HandleEntryKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
        };
        SetValue(_defaultValue);
        IsEnabledChanged += (_, _) => { Opacity = IsEnabled ? 1 : .45; if (!IsEnabled) SuspendPracticeInteraction(); };
        Unloaded += (_, _) => SuspendPracticeInteraction();
    }

    public void SetValue(double value)
    {
        value = Bound(value);
        if (_range.Value == value && ReferenceEquals(_lastTextBrush, ConsoleTheme.Get("ConsoleTextBrush"))) return;
        _applying = true;
        try
        {
            // ValueChanged renders a changed range. Only unchanged values need
            // an explicit refresh here (for example after a theme change).
            if (_range.Value != value) _range.Value = value;
            else Render();
        }
        finally { _applying = false; }
    }

    public void SetDefaultValue(double value)
    {
        value = Bound(value);
        if (_defaultValue == value) return;
        _defaultValue = value; RefreshHelp(); Render();
    }

    internal void ResetToDefault()
    {
        bool changed = _range.Value != _defaultValue;
        StopAdjustment();
        if (_entry.IsMouseCaptured) _entry.ReleaseMouseCapture();
        SetValue(_defaultValue);
        // Explicit resets must replace even focused, invalid pending entry text.
        RenderEntry();
        if (changed) _changed(_defaultValue);
    }

    internal void BeginAdjustment(double y)
    {
        if (!IsEnabled || !double.IsFinite(y)) return;
        CancelPendingEntry();
        bool starting = !_dragging;
        _dragging = true; _lastY = y; _dragPosition = Normalize(_range.Value);
        if (starting) EditGestureStarted?.Invoke(this, EventArgs.Empty);
    }

    internal void ContinueAdjustment(double y, bool fine)
    {
        if (!_dragging || !double.IsFinite(y)) return;
        double movement = _lastY - y; _lastY = y;
        if (movement == 0) return;
        _dragPosition = Math.Clamp(_dragPosition + movement / 240 * (fine ? .2 : 1), 0, 1);
        double value = Denormalize(_dragPosition);
        Commit(Math.Round(value / _step) * _step);
    }

    internal bool TryCommitText(string text)
    {
        if (!IsEnabled || !_entry.IsEnabled || _entry.IsReadOnly) return false;
        text = text.Replace('−', '-').Trim();
        if (_unit.Length > 0 && text.EndsWith(_unit, StringComparison.OrdinalIgnoreCase))
            text = text[..^_unit.Length].TrimEnd();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            || !double.IsFinite(value) || value < _minimum || value > _maximum
            || (_integer && value != Math.Truncate(value))) return false;
        // Numeric entry may be more precise than drag/key increments.
        StopAdjustment(); Commit(value); return true;
    }

    internal bool TryCommitPendingEntry()
    {
        if (!HasPendingEntry) return true;
        return ApplyPendingEntry(refocusOnError: true);
    }

    private bool ApplyPendingEntry(bool refocusOnError)
    {
        if (!IsEnabled || !_entry.IsEnabled || _entry.IsReadOnly) return false;
        if (TryCommitText(_entry.Text)) { RenderEntry(); return true; }
        _entryInvalid = true; RefreshEntryBorder(); RefreshEntryAccessibility();
        _entry.ToolTip = NodeToolTip.Create("Value not applied", EntryRequirement());
        if (refocusOnError) _entry.Focus();
        return false;
    }

    private void FinishEntryOnFocusLoss()
    {
        if (!IsEnabled || !_entry.IsEnabled || _entry.IsReadOnly) { CancelPendingEntry(); return; }
        if (HasPendingEntry) ApplyPendingEntry(refocusOnError: false);
        else RenderEntry();
    }

    internal void CancelPendingEntry() => RenderEntry();

    internal bool HandleEntryKey(Key key, ModifierKeys modifiers)
    {
        if (!IsEnabled || !_entry.IsEnabled || _entry.IsReadOnly || (modifiers & ~ModifierKeys.Shift) != ModifierKeys.None) return false;
        if (key == Key.Escape) { RenderEntry(); return true; }
        if (key is not (Key.Enter or Key.Tab)) return false;
        if (ApplyPendingEntry(refocusOnError: false))
        {
            // WPF retains forward/reverse Tab navigation. Only a rejected
            // value consumes Tab, keeping the draft in its current editor.
            return key == Key.Enter;
        }
        return true;
    }

    internal bool AdjustWithKey(Key key)
    {
        if (!IsEnabled) return false;
        double? value = key switch
        {
            Key.Left or Key.Down => _range.Value - _smallChange,
            Key.Right or Key.Up => _range.Value + _smallChange,
            Key.PageDown => _range.Value - _largeChange,
            Key.PageUp => _range.Value + _largeChange,
            Key.Home => _defaultValue,
            Key.End => _maximum,
            _ => null
        };
        if (!value.HasValue) return false;
        CancelPendingEntry();
        if (key == Key.Home) ResetToDefault(); else Commit(value.Value);
        return true;
    }

    internal void BeginTextEntry()
    {
        // Returning from the context menu or correcting a rejected draft must
        // preserve both text and selection, including on an unchanged refresh.
        if (HasPendingEntry || _entryMenuActive) { RefreshEntryBorder(); return; }
        // The resting readout stays concise. Editing must expose a round-trip
        // number so pressing Enter alone cannot round a precise saved value.
        WriteEntry(FormatExact(_range.Value)); _entry.SelectAll();
        _entryInvalid = false; RefreshEntryBorder(); RefreshEntryAccessibility();
        _entry.ToolTip = _entryHelp;
    }

    internal bool TryCancelPracticeInteraction()
    {
        bool active = _dragging || HasPendingEntry
            || _entry.IsKeyboardFocusWithin && _entry.Text != FormatExact(_range.Value);
        if (active) SuspendPracticeInteraction();
        return active;
    }

    internal void SuspendPracticeInteraction()
    {
        StopAdjustment();
        if (_entry.IsMouseCaptured) _entry.ReleaseMouseCapture();
        RenderEntry();
        _entryMenuActive = false;
        _entryMenu.IsOpen = false;
    }

    private void StopAdjustment()
    {
        bool ending = _dragging;
        _dragging = false; _range.Cursor = Cursors.Arrow;
        if (_range.IsMouseCaptured) _range.ReleaseMouseCapture();
        if (ending) EditGestureEnded?.Invoke(this, EventArgs.Empty);
    }
    private double Bound(double value)
    {
        value = double.IsFinite(value) ? value : (double.IsFinite(_defaultValue) ? _defaultValue : _minimum);
        return Math.Clamp(_integer ? Math.Round(value) : value, _minimum, _maximum);
    }
    private double Normalize(double value) => _logarithmic
        ? Math.Log(value / _minimum) / Math.Log(_maximum / _minimum)
        : (value - _minimum) / (_maximum - _minimum);
    private double Denormalize(double position) => _logarithmic
        ? _minimum * Math.Pow(_maximum / _minimum, position)
        : _minimum + position * (_maximum - _minimum);
    private void Commit(double value) => _range.Value = Bound(value);
    private static string StepFormat(double step, string unit)
    {
        int decimals = 0;
        while (decimals < 6 && Math.Abs(step - Math.Round(step, decimals)) > step * 1e-9) decimals++;
        if (unit is "dB" or "dBFS") decimals = Math.Max(1, decimals);
        return decimals == 0 ? "0" : "0." + new string('0', decimals);
    }
    private string WithUnit(string value) => value + (_unit.Length == 0 ? "" : (_unit == ":1" ? "" : " ") + _unit);
    private string Format(double value) => WithUnit(value.ToString(_format, CultureInfo.CurrentCulture));
    private string FormatExact(double value)
    {
        string concise = value.ToString(_format, CultureInfo.CurrentCulture);
        string number = double.TryParse(concise, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed) && parsed == value
            ? concise : value.ToString("G", CultureInfo.CurrentCulture);
        return WithUnit(number);
    }
    private void RefreshHelp()
    {
        _range.ToolTip = NodeToolTip.Create(_label, _description);
        AutomationProperties.SetHelpText(_range,
            $"{Format(_minimum)} to {Format(_maximum)}. Drag vertically; Shift for fine adjustment. Arrow keys adjust; Page Up and Page Down adjust by {Format(_largeChange)}. Home or double-click resets to {Format(_defaultValue)}.");
        RefreshEntryAccessibility();
        _entryHelp = EntryHelp();
        _entry.ToolTip = _entryHelp;
    }
    private string EntryRequirement() => $"Enter {(_integer ? "a whole number" : "a number")} from {Format(_minimum)} to {Format(_maximum)}.";
    private void RefreshEntryAccessibility()
    {
        string error = _entryInvalid ? "Invalid value. Not applied. " + EntryRequirement() : "";
        AutomationProperties.SetItemStatus(_entry, error);
        AutomationProperties.SetHelpText(_entry,
            (error.Length > 0 ? error + " " : (_integer ? "Whole numbers only. " : "") + $"{Format(_minimum)} to {Format(_maximum)}. ")
            + $"Enter applies; Tab or Shift+Tab applies and moves between controls; clicking away applies a valid value. Escape cancels. Right-click for Apply value or Cancel edit. Double-click resets to {Format(_defaultValue)}.");
    }
    private object EntryHelp() => NodeToolTip.Create(_label, _description);
    private void Render()
    {
        _lastTextBrush = ConsoleTheme.Get("ConsoleTextBrush"); FaceRefreshCount++;
        _face.Position = Normalize(_range.Value); _face.DefaultPosition = Normalize(_defaultValue); _face.InvalidateVisual();
        if (!_entry.IsKeyboardFocusWithin && !HasPendingEntry && !_entryMenuActive) RenderEntry();
        else RefreshEntryBorder();
    }
    private void WriteEntry(string text)
    {
        _renderingEntry = true;
        try { _entry.Text = text; }
        finally { _renderingEntry = false; }
        _entryDirty = false;
    }
    private void RenderEntry()
    {
        WriteEntry(_entry.IsKeyboardFocusWithin ? FormatExact(_range.Value) : Format(_range.Value));
        _entryInvalid = false; RefreshEntryBorder(); RefreshEntryAccessibility();
        _entry.ToolTip = _entryHelp;
    }

    private void RefreshEntryBorder()
    {
        // Every dial uses the same quiet value field, regardless of layout.
        // Outline only the field being interacted with, or an invalid entry.
        string? resource = _entryInvalid ? "ConsoleMuteBrush"
            : _entryFocused ? "ConsoleFocusBrush"
            : _entryHovered ? "ConsoleControlBorderBrush" : null;
        if (resource is null) _entry.BorderBrush = Brushes.Transparent;
        else _entry.SetResourceReference(Control.BorderBrushProperty, resource);
    }

    private sealed class DialFace : FrameworkElement
    {
        public double Position, DefaultPosition;
        public bool Focused;
        protected override void OnRender(DrawingContext dc)
        {
            // One face geometry serves both full-size EQ controls and compact
            // parameter tiles; scale the entire dial rather than clipping it.
            dc.PushTransform(new ScaleTransform(ActualWidth / 86, ActualHeight / 86));
            Point center = new(43, 43);
            Point At(double degrees, double radius)
            {
                double angle = degrees * Math.PI / 180;
                return new Point(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius);
            }
            Geometry Arc(double first, double last, double radius)
            {
                var path = new StreamGeometry();
                using (var drawing = path.Open())
                {
                    drawing.BeginFigure(At(first, radius), false, false);
                    for (int i = 1; i <= 48; i++) drawing.LineTo(At(first + (last - first) * i / 48, radius), true, false);
                }
                return path;
            }
            dc.DrawGeometry(null, new Pen(ConsoleTheme.Get("ConsoleMeterUnlitBrush"), 3), Arc(-135, 135, 34));
            double angle = -135 + Position * 270, defaultAngle = -135 + DefaultPosition * 270;
            if (Math.Abs(angle - defaultAngle) > .001)
                dc.DrawGeometry(null, new Pen(ConsoleTheme.Get("NodeAudioBrush"), 3), Arc(defaultAngle, angle, 34));
            for (int i = -2; i <= 2; i++)
                dc.DrawLine(new Pen(ConsoleTheme.Get("ConsoleMutedBrush"), 1), At(i * 67.5, 39), At(i * 67.5, 42));
            dc.DrawLine(new Pen(ConsoleTheme.Get("ConsoleTextBrush"), 1), At(defaultAngle, 39), At(defaultAngle, 42));
            dc.DrawEllipse(ConsoleTheme.Get("ConsoleButtonFaceBrush"),
                new Pen(ConsoleTheme.Get(Focused ? "ConsoleFocusBrush" : "NodeOutlineBrush"), Focused ? 2 : 1), center, 27, 27);
            dc.DrawLine(new Pen(ConsoleTheme.Get("ConsoleTextBrush"), 2.5)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, At(angle, 13), At(angle, 23));
            dc.Pop();
        }
    }
}
