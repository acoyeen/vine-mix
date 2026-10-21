using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached checks for shared parameter dials, without desktop input or audio.</summary>
internal static class RotaryControlVerification
{
    internal static FrameworkElement Run(Action<bool, string> require)
    {
        VerifyTabCommit(require);
        int changes = 0;
        var level = new RotaryValueControl("Level", -60, 12, .1, "dB", 0, _ => changes++);
        var frequency = new RotaryValueControl("Frequency", 20, 20000, 1, "Hz", 1000, _ => changes++, logarithmic: true, compact: true, primary: true);
        var gain = new RotaryValueControl("Gain", -24, 24, .1, "dB", 0, _ => changes++, compact: true, primary: true);
        var q = new RotaryValueControl("Width / Q", .1, 20, .01, "", 1, _ => changes++, logarithmic: true, compact: true, primary: true);
        var tone = new RotaryGainControl("Low", "200 Hz", _ => changes++);
        var controls = new[] { level, frequency, gain, q, tone };
        var attack = new RotaryValueControl("Attack", .1, 1000, .1, "ms", .1, _ => changes++, logarithmic: true, tile: true);
        var ratio = new RotaryValueControl("Ratio", 1, 20, .1, ":1", 3.5, _ => changes++, tile: true);
        var delay = new RotaryValueControl("Delay", 0, 1000, .001, "ms", 1.234, _ => changes++, tile: true);
        var samples = new RotaryValueControl("Samples", -48000, 48000, 1, "samples", 0, _ => changes++, tile: true, integer: true);
        var threshold = new RotaryValueControl("Threshold", -60, 0, .1, "dBFS", -24.5, _ => changes++, tile: true, primary: true);
        var tiles = new[] { attack, ratio, delay, samples, threshold };
        require(changes == 0 && controls.Select(control => control.Value).SequenceEqual(new double[] { 0, 1000, 0, 1, 0 }),
            "Constructing all level, EQ and legacy tone dials shows their meaningful defaults without sending changes.");

        var panel = new StackPanel(); panel.Children.Add(level);
        var eqRow = new Grid();
        for (int i = 0; i < 3; i++) eqRow.ColumnDefinitions.Add(new());
        for (int i = 0; i < 3; i++) { Grid.SetColumn(controls[i + 1], i); eqRow.Children.Add(controls[i + 1]); }
        panel.Children.Add(eqRow); panel.Children.Add(tone);
        var tileRow = new WrapPanel();
        foreach (var tile in tiles) tileRow.Children.Add(tile);
        panel.Children.Add(tileRow);
        var host = new Border { Width = 600, Padding = new Thickness(16), Child = panel,
            Background = ConsoleTheme.Get("ConsolePanelBrush") };
        host.Measure(new Size(600, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, 600, host.DesiredSize.Height)); host.UpdateLayout();
        foreach (var control in controls.Concat(tiles))
        {
            var slider = Descendants<Slider>(control).Single();
            var entry = Descendants<TextBox>(control).Single();
            bool tile = tiles.Contains(control);
            bool grouped = tile || control == frequency || control == gain || control == q;
            bool primary = control == frequency || control == gain || control == q || control == threshold;
            double expectedFace = grouped && !primary ? 58 : 86;
            require(slider.ActualWidth == expectedFace && slider.ActualHeight == expectedFace
                && entry.ActualWidth == 110 && entry.ActualHeight == 28 && entry.FontSize == 12
                && slider.Cursor == Cursors.Arrow && !entry.IsReadOnly,
                "Primary EQ/effect dials and horizontal level/tone dials use 86-DIP faces; secondary dials use 58-DIP faces, with identical value-field dimensions and typography.");
            require(slider.Focusable && KeyboardNavigation.GetIsTabStop(slider) && entry.Focusable
                && KeyboardNavigation.GetIsTabStop(entry) && AutomationProperties.GetName(slider).Length > 0
                && AutomationProperties.GetName(entry) == AutomationProperties.GetName(slider) + " value"
                && AutomationProperties.GetHelpText(entry).Contains("Escape cancels"),
                "Dial faces and exact-entry fields both remain keyboard reachable with meaningful accessible names and editing instructions.");
            require(entry.BorderBrush is SolidColorBrush { Color.A: 0 },
                "Every EQ, level, tone and effect value field is borderless at rest.");
            entry.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            require(ReferenceEquals(entry.BorderBrush, ConsoleTheme.Get("ConsoleControlBorderBrush")),
                "Every value field shows the same hover outline.");
            entry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, entry)
                { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
            entry.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            require(ReferenceEquals(entry.BorderBrush, ConsoleTheme.Get("ConsoleFocusBrush")),
                "Keyboard focus remains visible when the pointer leaves any value field.");
            double savedValue = control.Value; int savedChanges = changes;
            entry.Text = "invalid"; PressKey(entry, Key.Enter);
            entry.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            require(ReferenceEquals(entry.BorderBrush, ConsoleTheme.Get("ConsoleMuteBrush"))
                && control.Value == savedValue && changes == savedChanges,
                "Validation takes precedence over hover and focus without changing any dial's saved value.");
            PressKey(entry, Key.Escape);
            entry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, entry, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            entry.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            require(entry.BorderBrush is SolidColorBrush { Color.A: 0 },
                "All value fields return to their quiet border after cancelling and leaving.");
            var range = (IRangeValueProvider)new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue);
            require(range.Minimum == slider.Minimum && range.Maximum == slider.Maximum && range.Value == control.Value
                && range.SmallChange == slider.SmallChange && !range.IsReadOnly,
                "Each dial exposes a real accessible slider range in the parameter's original units.");
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            double before = control.Value; int priorChanges = changes;
            slider.RaiseEvent(wheel);
            require(wheel.Handled && control.Value == before && changes == priorChanges,
                "Wheel input on a standalone rotary face never changes its parameter; canvas zoom remains the ancestor's action.");
            var entryWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            entry.RaiseEvent(entryWheel);
            require(entryWheel.Handled && control.Value == before && changes == priorChanges,
                "Wheel input over exact-entry fields also leaves the stored parameter unchanged.");
            if (tile)
            {
                var label = Descendants<TextBlock>(control).Single(text => text.Text == AutomationProperties.GetName(slider));
                Rect labelBounds = label.TransformToAncestor(control).TransformBounds(new Rect(label.RenderSize));
                Rect dialBounds = slider.TransformToAncestor(control).TransformBounds(new Rect(slider.RenderSize));
                Rect entryBounds = entry.TransformToAncestor(control).TransformBounds(new Rect(entry.RenderSize));
                require(control.ActualWidth == 136 && control.ActualHeight <= (primary ? 140 : 112) && entry.ActualWidth == 110
                    && labelBounds.Bottom <= dialBounds.Top && dialBounds.Bottom <= entryBounds.Top
                    && entryBounds.Bottom <= control.ActualHeight,
                    "Primary and secondary tiles retain the same 136-DIP width and label–dial–value order, fitting within 140 or 112 DIPs of height respectively.");
            }
        }
        require(threshold.ActualWidth == attack.ActualWidth && Math.Abs(threshold.ActualHeight - attack.ActualHeight - 28) < .001,
            "A primary tile adds only the 28-DIP face-size difference; entry size and spacing stay consistent with a secondary tile.");

        require(Descendants<TextBox>(attack).Single().Text == .1.ToString("0.0", CultureInfo.CurrentCulture) + " ms"
            && Descendants<TextBox>(ratio).Single().Text == 3.5.ToString("0.0", CultureInfo.CurrentCulture) + ":1"
            && Descendants<TextBox>(delay).Single().Text == 1.234.ToString("0.000", CultureInfo.CurrentCulture) + " ms"
            && Descendants<TextBox>(threshold).Single().Text == (-24.5).ToString("0.0", CultureInfo.CurrentCulture) + " dBFS"
            && Descendants<TextBox>(q).Single().Text == 1d.ToString("0.00", CultureInfo.CurrentCulture),
            "Short attack times, fractional ratios, Q, exact delays and dBFS thresholds retain the decimal places implied by their increments.");

        var ratioFace = Descendants<Slider>(ratio).Single();
        require(PressKey(ratioFace, Key.Up).Handled && Math.Abs(ratio.Value - 3.6) < 1e-9
            && PressKey(ratioFace, Key.PageUp).Handled && Math.Abs(ratio.Value - 4.6) < 1e-9
            && PressKey(ratioFace, Key.Down).Handled && Math.Abs(ratio.Value - 4.5) < 1e-9,
            "Actual dial key routes apply one small increment with arrows and one large increment with Page Up.");
        PressKey(ratioFace, Key.End); PressKey(ratioFace, Key.Up);
        require(ratio.Value == 20 && PressKey(ratioFace, Key.Home).Handled && ratio.Value == 3.5
            && !PressKey(ratioFace, Key.Tab).Handled,
            "Keyboard adjustment clamps at the maximum, Home restores the parameter default, and Tab remains available for navigation.");
        ratioFace.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, ratioFace)
            { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        require(ratio.HasVisibleKeyboardFocus, "The rotary face visibly marks keyboard focus when its focus event arrives.");
        ratioFace.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, ratioFace, null)
            { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        require(!ratio.HasVisibleKeyboardFocus, "The rotary face clears the focus ring when focus leaves.");

        var delayEntry = Descendants<TextBox>(delay).Single();
        delayEntry.Text = 1.23456789.ToString("G", CultureInfo.CurrentCulture) + " ms";
        require(PressKey(delayEntry, Key.Enter).Handled && delay.Value == 1.23456789,
            "Enter on the real entry route applies sub-millisecond precision without rounding it to the drag step.");
        delay.BeginTextEntry(); int beforeExactEntry = changes;
        PressKey(delayEntry, Key.Enter);
        require(delay.Value == 1.23456789 && changes == beforeExactEntry,
            "Entering a displayed exact delay unchanged preserves its stored value without emitting a redundant edit.");
        delayEntry.Text = "999";
        require(PressKey(delayEntry, Key.Escape).Handled && delay.Value == 1.23456789 && changes == beforeExactEntry
            && delayEntry.Text == 1.23456789.ToString("0.000", CultureInfo.CurrentCulture) + " ms",
            "Escape discards pending text on the real entry route and restores the stored delay's readout.");
        delayEntry.Text = "invalid"; PressKey(delayEntry, Key.Enter);
        require(delay.Value == 1.23456789 && changes == beforeExactEntry
            && ReferenceEquals(delayEntry.BorderBrush, ConsoleTheme.Get("ConsoleMuteBrush")),
            "Invalid Enter leaves the saved delay untouched and supplies a visible validation border.");
        PressKey(delayEntry, Key.Escape);
        delayEntry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, delayEntry)
            { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
        require(ReferenceEquals(delayEntry.BorderBrush, ConsoleTheme.Get("ConsoleFocusBrush")),
            "Exact-entry keyboard focus has a visible border as well as selected editable text.");

        int beforeFractionalSample = changes;
        require(!samples.TryCommitText("1.5 samples") && !samples.TryCommitText("-0.25")
            && samples.Value == 0 && changes == beforeFractionalSample && samples.TryCommitText("-12 samples") && samples.Value == -12,
            "Sample-count entry rejects fractional samples and accepts exact signed whole numbers.");
        samples.BeginAdjustment(0); samples.ContinueAdjustment(-.123, true);
        require(samples.Value == Math.Truncate(samples.Value), "Fine sample-count dragging always produces whole samples.");
        var sampleFace = Descendants<Slider>(samples).Single();
        double beforeSampleKey = samples.Value; PressKey(sampleFace, Key.Up);
        require(samples.Value == beforeSampleKey + 1, "Sample-count keyboard adjustment advances by exactly one sample.");
        int beforeAccessibleSamples = changes;
        var sampleRange = (IRangeValueProvider)new SliderAutomationPeer(sampleFace).GetPattern(PatternInterface.RangeValue);
        sampleRange.SetValue(3.4);
        require(samples.Value == 3 && changes == beforeAccessibleSamples + 1,
            "An accessible range write also respects whole-sample semantics and sends a single settled edit.");

        level.SetValue(-60); level.BeginAdjustment(0); level.ContinueAdjustment(-240, false);
        require(level.Value == 12, "A full level dial sweep reaches +12 dB from -60 dB without truncating the input/output volume range.");
        level.ContinueAdjustment(-10000, false); level.ContinueAdjustment(10000, false);
        require(level.Value == -60, "Large rotary drags saturate at the configured range and cannot exceed safe parameter bounds.");
        level.SetValue(0); int beforeFine = changes;
        level.BeginAdjustment(0); level.ContinueAdjustment(0, true);
        require(level.Value == 0 && changes == beforeFine, "Switching to fine movement without pointer displacement produces no jump or edit.");
        level.ContinueAdjustment(-10, true);
        require(Math.Abs(level.Value - .6) < .000001, "Shift movement adjusts a level dial five times more finely than normal dragging.");

        frequency.SetValue(20); frequency.BeginAdjustment(0); frequency.ContinueAdjustment(-80, false);
        require(frequency.Value == 200, "The frequency dial covers 20 Hz to 200 Hz with the same 80-pixel decade sweep used elsewhere in its logarithmic range.");
        frequency.ContinueAdjustment(-160, false);
        require(frequency.Value == 2000, "A second equal frequency sweep reaches 2 kHz rather than applying a linear-Hz jump.");
        frequency.ContinueAdjustment(-240, false);
        require(frequency.Value == 20000 && Math.Abs(frequency.NormalizedValue - 1) < .000001,
            "The frequency dial reaches 20 kHz at the end of the three-decade sweep.");
        q.SetValue(.1); q.BeginAdjustment(0); q.ContinueAdjustment(-120, false);
        require(Math.Abs(q.Value - 1.41) < .000001,
            "The Q dial uses a logarithmic halfway point, retaining fine access to broad low-Q settings.");

        int beforeRefresh = changes;
        foreach (var control in controls) control.SetValue(control.DefaultValue);
        int refreshes = controls.Sum(control => control.FaceRefreshCount);
        foreach (var control in controls) control.SetValue(control.DefaultValue);
        require(changes == beforeRefresh && controls.Sum(control => control.FaceRefreshCount) == refreshes,
            "Repeated configuration/telemetry refreshes preserve settings without redundant callbacks or dial redraws.");
        int beforeModelUpdate = level.FaceRefreshCount;
        level.SetValue(-9);
        require(level.FaceRefreshCount == beforeModelUpdate + 1 && changes == beforeRefresh,
            "A changed model value refreshes the rotary face exactly once without sending a new edit.");
        level.BeginAdjustment(0); level.IsEnabled = false; int beforeDisabled = changes;
        level.ContinueAdjustment(-120, false); level.BeginAdjustment(0); level.ContinueAdjustment(-120, false);
        require(level.Value == -9 && changes == beforeDisabled,
            "Disabling a rotary control cancels its active gesture and prevents starting another adjustment.");
        level.IsEnabled = true; level.BeginAdjustment(0);
        level.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); level.ContinueAdjustment(-120, false);
        require(level.Value == -9 && changes == beforeDisabled,
            "Unloading a rotary control cancels a pending gesture without changing its saved value.");
        require(level.TryCommitText("−3.125 dB") && level.Value == -3.125 && q.TryCommitText("1.2345") && q.Value == 1.2345
            && frequency.TryCommitText("1234.5 Hz") && frequency.Value == 1234.5,
            "Exact numeric entry retains decimals beyond drag increments, accepts the displayed unit and recognizes a Unicode minus.");
        int priorInvalid = changes;
        require(!level.TryCommitText("NaN") && !level.TryCommitText("Infinity") && !level.TryCommitText("12.1")
            && !frequency.TryCommitText("19 Hz") && !q.TryCommitText("0") && !q.TryCommitText("20.01")
            && changes == priorInvalid && level.Value == -3.125 && frequency.Value == 1234.5 && q.Value == 1.2345,
            "Invalid and out-of-range entries leave all live parameter values and edit counts unchanged.");

        q.TryCommitText(1.23456789.ToString("G", CultureInfo.CurrentCulture));
        q.BeginTextEntry();
        var qEntry = Descendants<TextBox>(q).Single();
        int beforeReentry = changes;
        require(q.TryCommitText(qEntry.Text) && q.Value == 1.23456789 && changes == beforeReentry,
            "Editing a precise saved Q value exposes its round-trip number, so pressing Enter alone never rounds it or emits an edit.");

        var entryLevel = Descendants<TextBox>(level).Single();
        entryLevel.Text = "unfinished"; level.BeginAdjustment(0);
        var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Control.PreviewMouseDoubleClickEvent };
        int beforeReset = changes; entryLevel.RaiseEvent(doubleClick);
        level.ContinueAdjustment(-100, false); level.ResetToDefault();
        require(doubleClick.Handled && level.Value == 0 && changes == beforeReset + 1
            && entryLevel.Text == 0d.ToString("0.0", CultureInfo.CurrentCulture) + " dB"
            && entryLevel.SelectionLength == entryLevel.Text.Length,
            "Double-click reset cancels pending text and drags, restores the parameter once, and remains idempotent.");
        frequency.SetDefaultValue(725); beforeReset = changes; frequency.ResetToDefault(); frequency.ResetToDefault();
        require(frequency.Value == 725 && changes == beforeReset + 1,
            "A selected EQ band's frequency reset can use its saved anchor, rather than resetting every band to 1 kHz.");

        var sizes = controls.Select(control => control.RenderSize).ToArray();
        foreach (double zoom in new[] { .3, .75, 1.5, 2.5 })
        {
            host.RenderTransform = new ScaleTransform(zoom, zoom); host.UpdateLayout();
            require(controls.Select(control => control.RenderSize).SequenceEqual(sizes),
                "Zoom scales the shared dial presentation uniformly without reflowing its layout or changing node proportions.");
        }
        host.RenderTransform = Transform.Identity;
        host.Height = host.DesiredSize.Height;
        require(PresentationSource.FromVisual(host) is null,
            "All rotary checks remain detached: no desktop input, window handle, audio stream or saved-setting mutation.");
        return host;
    }

    private static void VerifyTabCommit(Action<bool, string> require)
    {
        int changes = 0;
        var level = new RotaryValueControl("Level", -60, 12, .1, "dB", 0, _ => changes++);
        var entry = Descendants<TextBox>(level).Single();
        var peer = new TextBoxAutomationPeer(entry);
        string Text(double value) => value.ToString("G", CultureInfo.CurrentCulture) + " dB";
        void Blur() => entry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, entry, null)
            { RoutedEvent = Keyboard.LostKeyboardFocusEvent });

        entry.Text = Text(-3.125);
        require(!PressKey(entry, Key.Tab).Handled && level.Value == -3.125 && changes == 1,
            "Tab on the real numeric-entry route applies exact precision once and remains unhandled for normal forward navigation.");
        Blur();
        require(level.Value == -3.125 && changes == 1,
            "Focus loss after a successful Tab formats the readout without committing the value a second time.");
        level.BeginTextEntry();
        require(!PressKey(entry, Key.Tab).Handled && level.Value == -3.125 && changes == 1,
            "Tabbing through an unchanged precise value neither rounds it nor sends another edit.");

        entry.Text = Text(-6.875);
        require(!level.HandleEntryKey(Key.Tab, ModifierKeys.Shift) && level.Value == -6.875 && changes == 2,
            "Shift+Tab uses the same commit handler and leaves reverse navigation available.");
        Blur();
        require(changes == 2 && peer.GetItemStatus().Length == 0 && peer.GetHelpText().Contains("Tab or Shift+Tab"),
            "A successful reverse Tab has no duplicate blur edit and exposes the updated keyboard instructions.");

        foreach (string invalid in new[] { "invalid", "", "NaN", "Infinity", Text(12.1) })
        {
            entry.Text = invalid;
            require(PressKey(entry, Key.Tab).Handled && entry.Text == invalid && level.Value == -6.875 && changes == 2
                && ReferenceEquals(entry.BorderBrush, ConsoleTheme.Get("ConsoleMuteBrush"))
                && peer.GetItemStatus().Contains("Invalid value") && peer.GetHelpText().Contains("Escape cancels"),
                "Invalid Tab keeps its draft and red validation state, exposes an accessible error, and changes no audio value.");
        }
        require(level.HandleEntryKey(Key.Tab, ModifierKeys.Shift) && changes == 2,
            "Invalid Shift+Tab also keeps the editor active instead of committing or discarding its draft.");
        entry.Text = Text(-9.25);
        require(!PressKey(entry, Key.Tab).Handled && level.Value == -9.25 && changes == 3
            && peer.GetItemStatus().Length == 0 && !peer.GetHelpText().Contains("Invalid value"),
            "Correcting an invalid draft and pressing Tab commits once and clears accessible validation feedback.");

        foreach (var modifiers in new[] { ModifierKeys.Control, ModifierKeys.Alt, ModifierKeys.Windows,
            ModifierKeys.Control | ModifierKeys.Shift, ModifierKeys.Alt | ModifierKeys.Shift })
        {
            entry.Text = Text(-12);
            require(!level.HandleEntryKey(Key.Tab, modifiers) && entry.Text == Text(-12) && level.Value == -9.25 && changes == 3,
                $"{modifiers}+Tab passes through without applying or replacing the draft.");
        }
        entry.Text = "invalid"; PressKey(entry, Key.Tab);
        require(PressKey(entry, Key.Escape).Handled && level.Value == -9.25 && changes == 3
            && peer.GetItemStatus().Length == 0 && !peer.GetHelpText().Contains("Invalid value"),
            "Escape still cancels a rejected draft and clears its error without editing the saved value.");
        entry.Text = Text(-18); Blur();
        require(level.Value == -9.25 && changes == 3 && entry.Text != Text(-18),
            "Leaving a field without Enter or Tab retains the existing cancel-on-blur behavior.");

        var samples = new RotaryValueControl("Samples", 0, 96000, 1, "samples", 0, _ => changes++, integer: true);
        var samplesEntry = Descendants<TextBox>(samples).Single();
        samplesEntry.Text = 1.5.ToString(CultureInfo.CurrentCulture);
        require(PressKey(samplesEntry, Key.Tab).Handled && samples.Value == 0 && changes == 3
            && AutomationProperties.GetItemStatus(samplesEntry).Contains("whole number"),
            "Tab preserves whole-sample validation and explains the integer requirement accessibly.");
        samplesEntry.Text = "48 samples";
        require(!PressKey(samplesEntry, Key.Tab).Handled && samples.Value == 48 && changes == 4,
            "Tab accepts a valid whole-sample value with its unit suffix.");
        samples.IsEnabled = false; samplesEntry.Text = "96 samples";
        require(!samples.HandleEntryKey(Key.Tab, ModifierKeys.None) && samples.Value == 48 && changes == 4,
            "A disabled numeric editor cannot commit through the shared key handler.");
        require(PresentationSource.FromVisual(level) is null && PresentationSource.FromVisual(samples) is null,
            "Tab validation checks never focus a control, create a native source or inject keyboard input; native traversal remains untested.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static KeyEventArgs PressKey(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new DetachedKeySource(), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args);
        return args;
    }

    // KeyEventArgs requires a source. This inert source supplies no native
    // target, registers no window and never injects desktop keyboard input.
    private sealed class DetachedKeySource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
