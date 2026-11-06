using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached draft and rendered-meter checks; no native focus, devices or audio.</summary>
internal static class ControlRefinementVerification
{
    internal static FrameworkElement Run(Action<bool, string> require)
    {
        ConsoleTheme.Initialize();
        VerifyDirectEntry(require);
        VerifyGradient(require);
        var columns = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (double db in new[] { -36d, -15, -9, -3, -.3 })
        {
            var column = new StackPanel { Width = 90 };
            column.Children.Add(new TextBlock { Text = db.ToString(CultureInfo.InvariantCulture) + " dBFS",
                Foreground = ConsoleTheme.Get("ConsoleTextBrush"), HorizontalAlignment = HorizontalAlignment.Center });
            column.Children.Add(Meter(db, channels: 2, height: 249));
            columns.Children.Add(column);
        }
        var panel = new StackPanel(); panel.Children.Add(columns);
        panel.Children.Add(new RotaryValueControl("Example value", -60, 12, .1, "dB", -6, _ => { }));
        var host = new Border { Width = 490, Padding = new Thickness(20), Child = panel,
            Background = ConsoleTheme.Get("ConsolePanelBrush") };
        host.Measure(new Size(490, double.PositiveInfinity));
        host.Height = host.DesiredSize.Height;
        host.Arrange(new Rect(0, 0, 490, host.DesiredSize.Height)); host.UpdateLayout();
        require(PresentationSource.FromVisual(host) is null
            && Descendants<StereoNodeMeter>(host).All(meter => !meter.IsAnimationRunning),
            "Control refinement evidence remains detached, with synthetic readings, no meter timers and no native window.");
        return host;
    }

    private static void VerifyDirectEntry(Action<bool, string> require)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string cultureName in new[] { "en-US", "fr-FR", "sv-SE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                string separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                var edits = new List<double>();
                var dial = new RotaryValueControl("Example value", -60, 12, .1, "dB", -6, edits.Add);
                var entry = Descendants<TextBox>(dial).Single();
                var slider = Descendants<Slider>(dial).Single();
                var input = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                    new TextComposition(InputManager.Current, slider, "2")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
                slider.RaiseEvent(input);
                require(!input.Handled && !dial.HasPendingEntry && dial.Value == -6 && edits.Count == 0,
                    "Routed numeric text on an unfocused dial is not intercepted merely because its node could be selected.");
                require(dial.TryBeginDirectEntry("2", ModifierKeys.None) && entry.Text == "2"
                    && entry.CaretIndex == 1 && entry.SelectionLength == 0 && dial.HasPendingEntry
                    && dial.Value == -6 && edits.Count == 0,
                    "Numeric starter text replaces the entire old value and unit, retaining the first digit without applying an edit.");
                entry.SelectedText = separator + "75";
                require(entry.Text == "2" + separator + "75" && dial.Value == -6 && edits.Count == 0,
                    "Subsequent entry text appends to the retained first character in the current culture without changing the saved value.");
                require(dial.HandleEntryKey(Key.Enter, ModifierKeys.None) && dial.Value == 2.75
                    && edits.SequenceEqual(new[] { 2.75 }) && !dial.HasPendingEntry,
                    "The original Enter path applies the complete localized number exactly once.");

                require(dial.TryBeginDirectEntry("-", ModifierKeys.None)
                    && !dial.TryCommitPendingEntry() && entry.Text == "-" && dial.Value == 2.75
                    && edits.Count == 1 && AutomationProperties.GetItemStatus(entry).Contains("Invalid value"),
                    "An incomplete leading minus remains visible with validation feedback and cannot change the saved value.");
                entry.CaretIndex = entry.Text.Length;
                entry.SelectedText = "0" + separator + "5";
                require(!dial.HandleEntryKey(Key.Tab, ModifierKeys.Shift) && dial.Value == -.5
                    && edits.SequenceEqual(new[] { 2.75, -.5 }),
                    "Completing a signed draft uses the existing reverse-Tab commit and permits normal focus traversal.");

                require(dial.TryBeginDirectEntry(separator, ModifierKeys.None)
                    && dial.HandleEntryKey(Key.Tab, ModifierKeys.None) && entry.Text == separator
                    && dial.Value == -.5 && edits.Count == 2 && dial.HasPendingEntry,
                    "An incomplete locale decimal starter blocks Tab and stays editable without a value change.");
                require(dial.HandleEntryKey(Key.Escape, ModifierKeys.None) && !dial.HasPendingEntry
                    && dial.Value == -.5 && edits.Count == 2,
                    "Escape cancels the numeric starter draft through the existing entry path without an edit.");

                require(dial.TryBeginDirectEntry("−", ModifierKeys.Shift),
                    "A composed minus remains available on layouts that require Shift.");
                entry.SelectedText = "3" + separator + "25";
                entry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, entry, null)
                    { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
                require(dial.Value == -3.25 && edits.Count == 3 && !dial.HasPendingEntry,
                    "A completed Unicode-minus draft commits once through the original focus-loss handler.");

                foreach (var modifier in new[] { ModifierKeys.Control, ModifierKeys.Alt, ModifierKeys.Windows,
                    ModifierKeys.Control | ModifierKeys.Shift })
                    require(!dial.TryBeginDirectEntry("7", modifier) && !dial.HasPendingEntry && edits.Count == 3,
                        "Modified shortcuts cannot start numeric entry or change a dial value.");
                foreach (string text in new[] { "", "a", "e", "+", " ", separator == "." ? "," : "." })
                    require(!dial.TryBeginDirectEntry(text, ModifierKeys.None) && !dial.HasPendingEntry && edits.Count == 3,
                        "Non-numeric starter text and another culture's decimal separator remain unintercepted.");
                entry.IsReadOnly = true;
                require(!dial.TryBeginDirectEntry("1", ModifierKeys.None), "A read-only value field rejects direct numeric entry.");
                entry.IsReadOnly = false; entry.IsEnabled = false;
                require(!dial.TryBeginDirectEntry("1", ModifierKeys.None), "A disabled value field rejects direct numeric entry.");
                entry.IsEnabled = true; dial.IsEnabled = false;
                require(!dial.TryBeginDirectEntry("1", ModifierKeys.None), "A disabled dial rejects direct numeric entry.");
                dial.IsEnabled = true;
                require(dial.TryBeginDirectEntry("999", ModifierKeys.None)
                    && !dial.TryCommitPendingEntry() && entry.Text == "999" && dial.Value == -3.25 && edits.Count == 3,
                    "An out-of-range composed number retains the existing validation and does not emit an edit.");
                dial.CancelPendingEntry();
                dial.SetValue(-6);
                require(dial.AdjustWithKey(Key.PageUp) && dial.Value == -5
                    && dial.AdjustWithKey(Key.PageDown) && dial.Value == -6 && edits.Count == 5,
                    "Page Up and Page Down retain the original larger-step adjustment and edit behavior.");
                require(PresentationSource.FromVisual(dial) is null && !slider.IsKeyboardFocused && !entry.IsKeyboardFocused,
                    "Draft tests call the production entry transition without native focus; physical keyboard and IME timing remain separate qualification.");
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void VerifyGradient(Action<bool, string> require)
    {
        bool? previous = ConsoleTheme.PreviewHighContrast;
        try
        {
            ConsoleTheme.SetPreviewHighContrast(false);
            VerifyGradientTopEdge(require);
            VerifyClipCap(require);
            var full = Meter(0); var fullPixels = Render(full);
            var quiet = Meter(-30); var quietPixels = Render(quiet);
            foreach (double db in new[] { -50d, -40, -32 })
                require(Pixel(fullPixels, full, db) == Pixel(quietPixels, quiet, db),
                    "The same dBFS height has identical colour in a full or quiet meter; the gradient never stretches to fill height.");
            Color green = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMeterLowBrush")).Color;
            Color yellow = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMeterMidBrush")).Color;
            Color red = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMeterHighBrush")).Color;
            require(Pixel(fullPixels, full, -30) == green && Pixel(fullPixels, full, -7.5) == yellow
                && Pixel(fullPixels, full, -.5) == red,
                "Quiet levels retain green, the warning region retains yellow, and near-full scale reaches red.");
            Color greenBlend = Pixel(fullPixels, full, -14);
            Color redBlend = Pixel(fullPixels, full, -3.5);
            require(greenBlend != green && greenBlend != yellow && redBlend != yellow && redBlend != red,
                "Rendered transition regions contain intermediate colours instead of abrupt zone boundaries.");
            foreach (double db in new[] { -12d, -3 })
                require(Distance(Pixel(fullPixels, full, db - .2), Pixel(fullPixels, full, db + .2)) < 12,
                    "Colours change smoothly across the former hard warning and red boundaries.");
            Color unlit = Pixel(quietPixels, quiet, -3);
            require(Distance(unlit, Pixel(fullPixels, full, -3)) > 70,
                "The unfilled upper gradient remains subdued rather than implying a high current reading.");
            var stereo = Meter(0, channels: 2); var stereoPixels = Render(stereo);
            require(Pixel(stereoPixels, stereo, -14) == greenBlend
                && Pixel(stereoPixels, stereo, -14, 1) == greenBlend,
                "Mono and both stereo lanes share the same fixed dBFS colour scale.");
            var muted = Meter(0); muted.Update(1, 1, 2, true, true, false, 0);
            var silence = Meter(-60, channels: 2);
            require(Pixel(Render(muted), muted, -30) == Pixel(Render(silence), silence, -30)
                && muted.ClipIndicatorOpacity == 0 && !muted.ClipLatched,
                "Muted and silent meters reveal only the subdued scale and mute cannot show clipping.");
            require(!string.Join(" ", Descendants<TextBlock>((DependencyObject)full.ToolTip).Select(text => text.Text)).Contains("Above"),
                "Meter help describes gradual colour transitions instead of obsolete hard thresholds.");

            ConsoleTheme.SetPreviewHighContrast(true);
            VerifyGradientTopEdge(require);
            VerifyClipCap(require);
            // A normal telemetry refresh must invalidate a previously rendered,
            // detached control when its theme brushes have changed.
            full.Update(1, 0, 1, false, false, false, .01);
            var contrastPixels = Render(full);
            Color highlight = SystemColors.HighlightColor;
            require(new[] { -40d, -12, -3 }.All(db => Pixel(contrastPixels, full, db) == highlight),
                "Changing to high contrast replaces the gradient with the current system highlight colour across its entire lit scale.");
        }
        finally { ConsoleTheme.SetPreviewHighContrast(previous); }
    }

    private static void VerifyGradientTopEdge(Action<bool, string> require)
    {
        foreach (int channels in new[] { 1, 2 })
        foreach (double db in new[] { -60d, -30, 0 })
        {
            var meter = Meter(db, channels, height: 249);
            var bitmap = Render(meter);
            var border = ((SolidColorBrush)ConsoleTheme.Get("ConsoleBorderBrush")).Color;
            foreach (var lane in meter.GetLaneBounds())
            {
                int left = (int)lane.Left, right = (int)lane.Right - 1, topRow = (int)lane.Top, bottom = (int)lane.Bottom - 1;
                require(Enumerable.Range(left, right - left + 1).All(x => PixelAt(bitmap, x, topRow) == border && PixelAt(bitmap, x, bottom) == border)
                    && Enumerable.Range(topRow, bottom - topRow + 1).All(y => PixelAt(bitmap, left, y) == border && PixelAt(bitmap, right, y) == border),
                    "The complete original one-DIP meter border retains its theme colour on all four sides, without a gradient tint or fill overlap.");
                int x = (int)(lane.Left + lane.Width / 2), y = topRow + 1;
                Color top = PixelAt(bitmap, x, y), next = PixelAt(bitmap, x, y + 1), third = PixelAt(bitmap, x, y + 2);
                require(top == next && next == third,
                    $"The gradient's first three inner red-plateau rows are continuous inside the border at {db} dBFS with {channels} channel(s), including high contrast; rendered colours: {top}, {next}, {third}.");
            }
        }
    }

    private static void VerifyClipCap(Action<bool, string> require)
    {
        foreach (int channels in new[] { 1, 2 })
        {
            var meter = Meter(0, channels);
            Color Cap(BitmapSource bitmap, int lane) => PixelAt(bitmap,
                (int)(meter.GetLaneBounds()[lane].Left + meter.GetLaneBounds()[lane].Width / 2), 2);
            var idle = Render(meter);
            var unlitColor = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMeterUnlitBrush")).Color;
            require(Enumerable.Range(0, channels).All(lane => Cap(idle, lane) == unlitColor)
                && !meter.ClipLatched,
                "The original idle peak/clipping cap remains visible in its unlit theme colour above mono and stereo gradients, including high contrast.");
            meter.Update(0, 0, channels, true, false, false, .01);
            var clipped = Render(meter);
            var clipColor = ((SolidColorBrush)ConsoleTheme.Get("ConsoleMeterHighBrush")).Color;
            require(Enumerable.Range(0, channels).All(lane => Cap(clipped, lane) == clipColor)
                && meter.ClipIndicatorOpacity == 1 && meter.ClipLatched,
                "Actual aggregate clipping still paints an obvious cap in every lane using the active theme's clipping colour.");
            meter.Advance(2.01);
            var held = Render(meter);
            require(Enumerable.Range(0, channels).All(lane => Cap(held, lane) == clipColor)
                && meter.ClipIndicatorOpacity == 1,
                "The clipping cap retains its complete two-second hold before fading toward its original unlit colour.");
            meter.Advance(2.51);
            var fading = Render(meter);
            require(Enumerable.Range(0, channels).All(lane => Cap(fading, lane).A == 255
                && Cap(fading, lane) != unlitColor && Cap(fading, lane) != clipColor),
                "The visible clipping cap fades through intermediate colours over its unchanged unlit background.");
            meter.Advance(3.01);
            var expired = Render(meter);
            require(Enumerable.Range(0, channels).All(lane => Cap(expired, lane) == unlitColor)
                && meter.ClipLatched && meter.RetainedPeakLeft == 1,
                "After clipping fades, the original unlit cap returns while retained peak and clipping history remain available.");
        }
    }

    private static StereoNodeMeter Meter(double db, int channels = 1, double height = 609)
    {
        var meter = new StereoNodeMeter { Width = 28, Height = height, HorizontalAlignment = HorizontalAlignment.Center };
        double value = db <= -60 ? 0 : Math.Pow(10, db / 20);
        meter.Update(value, channels == 2 ? value : 0, channels, false, false, false, 0);
        meter.Measure(new Size(28, height)); meter.Arrange(new Rect(0, 0, 28, height)); meter.UpdateLayout();
        return meter;
    }
    private static RenderTargetBitmap Render(FrameworkElement visual)
    {
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); return bitmap;
    }
    private static Color Pixel(BitmapSource bitmap, StereoNodeMeter meter, double db, int laneIndex = 0)
    {
        var lane = meter.GetLaneBounds()[laneIndex];
        int x = (int)(lane.Left + lane.Width / 2), y = (int)(lane.Bottom - 1 - (db + 60) / 60 * (lane.Height - 2));
        return PixelAt(bitmap, x, y);
    }
    private static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        byte[] pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static int Distance(Color first, Color second) => Math.Max(Math.Abs(first.R - second.R),
        Math.Max(Math.Abs(first.G - second.G), Math.Abs(first.B - second.B)));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
