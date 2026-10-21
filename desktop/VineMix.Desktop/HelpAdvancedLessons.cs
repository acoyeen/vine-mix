using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Real editors backed exclusively by disposable practice values.</summary>
internal static class HelpAdvancedLessons
{
    internal static IHelpLesson Equalizer() => new EqualizerLesson();
    internal static IHelpLesson VoiceColor() => new VoiceColorLesson();

    internal sealed class EqualizerLesson : IHelpLesson
    {
        private readonly WrapPanel _details;
        private List<EqBand> _bands = [];
        private int _selected = -1;
        private bool _updating, _disposed;

        internal EqualizerEditor Plot { get; }
        internal RotaryValueControl Frequency { get; }
        internal RotaryValueControl Gain { get; }
        internal RotaryValueControl Width { get; }
        public FrameworkElement View { get; }

        internal EqualizerLesson()
        {
            Plot = new EqualizerEditor
            {
                Height = 260, Margin = new Thickness(0, 0, 0, 8),
                InteractionInstructions = "Drag points to change the sample EQ. "
                    + "Ctrl-click points or drag empty plot space to select several bands; hold Ctrl to add to a selection. "
                    + "Arrow keys move selected bands; Shift makes smaller movements. Ctrl+A selects all bands. "
                    + "Home resets selected positions; Delete removes selected bands. Escape cancels an active selection or drag. "
                    + "Double-click empty plot space to add a bell band; double-click a point to reset its position. "
                    + "Right-click a point for its shape, enable, reset and remove actions. Right-click empty space to reset to flat. "
                    + "The three dials edit the active band. The line is the calculated response of these bands; "
                    + "no audio spectrum is shown. The mouse wheel scrolls Help."
            };
            AutomationProperties.SetName(Plot, "Practice equalizer response and band editor");
            Frequency = new RotaryValueControl("Frequency", 20, 20000, 1, "Hz", 1000,
                value => EditBand(band => band.FrequencyHz = value), logarithmic: true,
                tile: true, smallChange: 10, largeChange: 100,
                description: "Moves the active band's frequency.");
            Gain = new RotaryValueControl("Band gain", -24, 24, .1, "dB", 0,
                value => EditBand(band => band.GainDb = value), tile: true,
                smallChange: .5, largeChange: 3,
                description: "Boosts or cuts the active band.");
            Width = new RotaryValueControl("Width / Q", .1, 20, .01, "", 1,
                value => EditBand(band => band.Q = value), logarithmic: true,
                tile: true, smallChange: .1, largeChange: 1, format: "0.0###",
                description: "Changes the active band's width. Higher Q makes the response narrower.");
            _details = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var dial in new[] { Frequency, Gain, Width })
            {
                dial.Margin = new Thickness(0, 0, 8, 6);
                _details.Children.Add(dial);
            }
            View = HelpLessonUi.Section("", "", Plot, _details,
                HelpLessonUi.Hint("Drag a point; hold Shift for fine movement. Ctrl-click selects several bands."),
                HelpLessonUi.Hint("Double-click empty space to add a band. Right-click a point for shape and band actions."),
                HelpLessonUi.Hint("The dials edit the active band. Click a value to type it; a Notch has no gain control."));
            View.DataContext = new object();
            Plot.BandsEdited += BandsEdited;
            Plot.SelectedBandChanged += BandSelected;
            View.Unloaded += ViewUnloaded;
            Reset();
        }

        private void BandsEdited(IReadOnlyList<EqBand> bands)
        {
            if (_updating || _disposed) return;
            _bands = bands.Select(band => band.Copy()).ToList();
            _selected = Plot.SelectedBandIndex;
            Refresh(applyPlot: false);
        }

        private void BandSelected(int index)
        {
            if (_updating || _disposed) return;
            _selected = index;
            Refresh(applyPlot: false);
        }

        private void EditBand(Action<EqBand> edit)
        {
            if (_updating || _disposed || _selected < 0 || _selected >= _bands.Count) return;
            var band = _bands[_selected];
            if (band.ResetFrequencyHz <= 0) band.ResetFrequencyHz = band.FrequencyHz;
            edit(band);
            Refresh(applyPlot: true);
        }

        private void Refresh(bool applyPlot)
        {
            _updating = true;
            try
            {
                bool selected = _selected >= 0 && _selected < _bands.Count;
                _details.IsEnabled = selected;
                if (selected)
                {
                    var band = _bands[_selected];
                    Frequency.SetDefaultValue(band.ResetFrequencyHz > 0 ? band.ResetFrequencyHz : band.FrequencyHz);
                    Frequency.SetValue(band.FrequencyHz);
                    Gain.SetValue(band.GainDb);
                    Gain.IsEnabled = band.Type is not "notch" and not "lowPass" and not "highPass";
                    Width.SetDefaultValue(EqBand.DefaultQForType(band.Type));
                    Width.SetValue(band.Q);
                }
                if (applyPlot) Plot.Apply(_bands, bypassed: false, _selected);
            }
            finally { _updating = false; }
        }

        public void Reset()
        {
            if (_disposed) return;
            Suspend();
            _updating = true;
            try
            {
                // Clear local selection before restoring an equal-sized fixture.
                Plot.SelectBand(-1);
                _bands = EqBand.Defaults();
                _selected = 2;
            }
            finally { _updating = false; }
            Refresh(applyPlot: true);
        }

        public bool TryCancel()
        {
            if (_disposed) return false;
            return Plot.TryCancelPracticeInteraction()
                || Frequency.TryCancelPracticeInteraction()
                || Gain.TryCancelPracticeInteraction()
                || Width.TryCancelPracticeInteraction();
        }

        public void Suspend()
        {
            Plot.SuspendPracticeInteraction();
            Frequency.SuspendPracticeInteraction();
            Gain.SuspendPracticeInteraction();
            Width.SuspendPracticeInteraction();
        }

        private void ViewUnloaded(object sender, RoutedEventArgs args) => Suspend();

        public void Dispose()
        {
            if (_disposed) return;
            Suspend();
            _disposed = true;
            Plot.BandsEdited -= BandsEdited;
            Plot.SelectedBandChanged -= BandSelected;
            View.Unloaded -= ViewUnloaded;
        }
    }

    internal sealed class VoiceColorLesson : IHelpLesson
    {
        private readonly TextBlock _status;
        private double _tone, _ambience;
        private bool _updating, _disposed;

        internal VoiceColorPad Pad { get; }
        internal RotaryValueControl Tone { get; }
        internal RotaryValueControl Character { get; }
        public FrameworkElement View { get; }

        internal VoiceColorLesson()
        {
            Pad = new VoiceColorPad
            {
                Width = 280, Height = 320, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 16, 8)
            };
            AutomationProperties.SetName(Pad, "Practice Voice Color pad");
            AutomationProperties.SetHelpText(Pad,
                "Drag the sample point left and right for Warm to Bright, "
                + "and up and down for Natural to Broadcast. Arrow keys move one percent; "
                + "Shift moves a tenth of a percent. Home or double-click resets to neutral. "
                + "Tone and Character fields accept exact values. Escape ends an active drag. "
                + "The mouse wheel scrolls Help.");
            Tone = new RotaryValueControl("Tone", -100, 100, .1, "%", 0,
                value => SetPosition(value / 100, _ambience), tile: true,
                smallChange: 1, largeChange: 10,
                description: "Moves from Warm at -100% to Bright at 100%.");
            Character = new RotaryValueControl("Character", 0, 100, .1, "%", 0,
                value => SetPosition(_tone, value / 100), tile: true,
                smallChange: 1, largeChange: 10,
                description: "Moves from Natural at 0% to Broadcast at 100%.");
            var details = new StackPanel { Width = 136, Margin = new Thickness(0, 12, 0, 8) };
            Tone.Margin = Character.Margin = new Thickness(0, 0, 0, 12);
            details.Children.Add(Tone); details.Children.Add(Character);
            var controls = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            controls.Children.Add(Pad); controls.Children.Add(details);
            _status = HelpLessonUi.Hint("");
            AutomationProperties.SetName(_status, "Practice Voice Color state");
            View = HelpLessonUi.Section("", "", controls, _status,
                HelpLessonUi.Hint("Drag the point or use arrow keys. Hold Shift for fine movement."),
                HelpLessonUi.Hint("Type either value for an exact position. Double-click the pad or press Home to reset."));
            View.DataContext = new object();
            Pad.PositionEdited += PositionEdited;
            View.Unloaded += ViewUnloaded;
            Reset();
        }

        private void PositionEdited(double tone, double ambience)
        {
            if (_updating || _disposed) return;
            _tone = tone; _ambience = ambience;
            Refresh(applyPad: false);
        }

        private void SetPosition(double tone, double ambience)
        {
            if (_updating || _disposed) return;
            _tone = Math.Clamp(tone, -1, 1); _ambience = Math.Clamp(ambience, 0, 1);
            Refresh(applyPad: true);
        }

        private void Refresh(bool applyPad)
        {
            _updating = true;
            try
            {
                if (applyPad) Pad.Apply(_tone, _ambience, bypassed: false);
                Tone.SetValue(_tone * 100);
                Character.SetValue(_ambience * 100);
                _status.Text = Pad.PositionDescription;
            }
            finally { _updating = false; }
        }

        public void Reset()
        {
            if (_disposed) return;
            Suspend();
            _tone = _ambience = 0;
            Refresh(applyPad: true);
        }

        public bool TryCancel()
        {
            if (_disposed) return false;
            if (Pad.IsAdjusting) { Pad.EndAdjustment(); return true; }
            return Tone.TryCancelPracticeInteraction() || Character.TryCancelPracticeInteraction();
        }

        public void Suspend()
        {
            Pad.EndAdjustment();
            Tone.SuspendPracticeInteraction();
            Character.SuspendPracticeInteraction();
        }

        private void ViewUnloaded(object sender, RoutedEventArgs args) => Suspend();

        public void Dispose()
        {
            if (_disposed) return;
            Suspend();
            _disposed = true;
            Pad.PositionEdited -= PositionEdited;
            View.Unloaded -= ViewUnloaded;
        }
    }
}
