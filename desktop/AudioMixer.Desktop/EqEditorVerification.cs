using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached EQ interaction and measured-spectrum presentation checks.</summary>
internal static class EqEditorVerification
{
    internal static FrameworkElement Run(Action<bool, string> require)
    {
        var editor = new EqualizerEditor { Width = 540, Height = 300 };
        Layout(editor);
        int edits = 0;
        editor.BandsEdited += _ => edits++;
        var defaults = EqBand.Defaults();
        editor.Apply(defaults, false, 2);
        require(defaults.All(band => band.ResetFrequencyHz == band.FrequencyHz)
            && defaults.Where(band => band.Type == "peak").All(band => band.Q == 1),
            "Default EQ points retain the established widths and carry persistent reset-frequency anchors.");
        editor.MoveBand(2, 1800, 9); editor.SetBandQ(2, 2.8);
        var menu = editor.BuildBandMenu(2, new Point(100, 100));
        require(!menu.IsOpen && !menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Width"))
            && menu.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Shape")),
            "EQ right-click keeps shape choices and removes duplicate width presets; Width / Q remains the sole width control.");
        var reset = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Reset position"));
        reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var restored = editor.SnapshotBands()[2];
        require(restored.FrequencyHz == defaults[2].FrequencyHz && restored.GainDb == 0
            && restored.Q == 2.8 && restored.Type == defaults[2].Type,
            "The actual Reset position menu action restores both default frequency and zero gain while preserving the chosen width and shape.");
        int before = edits;
        require(!editor.ResetBandPosition(2) && edits == before,
            "Resetting an already-centred point creates no redundant settings edit.");

        editor.AddBandAt(725, 4);
        int added = editor.BandCount - 1;
        editor.MoveBand(added, 2400, -8);
        var saved = JsonSerializer.Deserialize<EqBand[]>(JsonSerializer.Serialize(editor.SnapshotBands(), Protocol.Json), Protocol.Json)!;
        editor.Apply(saved, false, added);
        require(editor.ResetBandPosition(added) && editor.SnapshotBands()[added].FrequencyHz == 725
            && editor.SnapshotBands()[added].GainDb == 0 && editor.SnapshotBands()[added].Q == 1,
            "A newly-added point keeps its creation frequency through serialization and resets there with zero gain after later moves.");
        editor.Apply([new EqBand { FrequencyHz = 640, GainDb = 3, Q = 4, ResetFrequencyHz = 0 }], false, 0);
        editor.MoveBand(0, 3700, -6); editor.ResetBandPosition(0);
        require(editor.SnapshotBands()[0].FrequencyHz == 640 && editor.SnapshotBands()[0].ResetFrequencyHz == 640
            && editor.SnapshotBands()[0].GainDb == 0 && editor.SnapshotBands()[0].Q == 4,
            "An older saved point without an anchor remembers its loaded frequency on its first edit, preserving its existing width.");

        editor.Apply([
            new EqBand { Type = "notch", FrequencyHz = 1000, ResetFrequencyHz = 500, Q = 4 },
            new EqBand { Type = "highPass", FrequencyHz = 500, GainDb = 0 },
            new EqBand { Type = "lowPass", FrequencyHz = 4000, GainDb = 0 },
            new EqBand { Type = "peak", FrequencyHz = 2000, GainDb = 18 }
        ], false, 0);
        editor.ResetBandPosition(0);
        require(editor.SnapshotBands()[0].Type == "notch" && editor.SnapshotBands()[0].FrequencyHz == 500,
            "Resetting a notch point changes its position without secretly replacing its filter shape.");
        var emptyMenu = editor.BuildBandMenu(-1, new Point(editor.PlotBounds.Left + 10, editor.PlotBounds.Top + 10));
        before = edits;
        emptyMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Reset EQ to flat"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(edits == before + 1 && editor.BandCount == 0 && editor.SelectedBandIndex == -1
            && editor.SelectedBandIndices.Count == 0
            && Enumerable.Range(0, 201).All(index => Math.Abs(EqualizerEditor.CombinedResponseDb(editor.SnapshotBands(), false,
                20 * Math.Pow(1000, index / 200d))) < .000001),
            "The actual empty-space Reset EQ to flat action removes every notch/pass/bell band, clears selection and produces a flat 20 Hz–20 kHz response in one edit.");
        before = edits;
        require(!editor.ResetEq() && edits == before && editor.BandCount == 0 && editor.SelectedBandIndex == -1,
            "Resetting an already-empty EQ returns false without publishing another band edit.");

        var wide = new EqBand { FrequencyHz = 1000, GainDb = 12, Q = .5 };
        var narrow = new EqBand { FrequencyHz = 1000, GainDb = 12, Q = 4 };
        require(Math.Abs(EqualizerEditor.ResponseDb(wide, 1000) - 12) < .000001
            && Math.Abs(EqualizerEditor.ResponseDb(narrow, 1000) - 12) < .000001
            && EqualizerEditor.ResponseDb(wide, 500) > EqualizerEditor.ResponseDb(narrow, 500) + 3,
            "Response rendering uses the real biquad bandwidth: changing Q alters the actual broadness while keeping the requested centre gain.");

        // Keep the measured-spectrum regression fixture populated independently
        // of the empty reset behavior checked above.
        editor.Apply(defaults, false, -1);
        string settings = JsonSerializer.Serialize(editor.SnapshotBands(), Protocol.Json);
        before = edits;
        int refreshes = editor.SettingsRefreshCount;
        var noSpectrum = Render(editor);
        float[] bins = Enumerable.Range(0, 128).Select(index => (float)(-76 + 66 * Math.Exp(-Math.Pow((index - 72) / 18d, 2)))).ToArray();
        editor.ApplySpectrumAt(bins, 4096, 1, 1);
        Layout(editor);
        var points = editor.SpectrumPoints();
        var withSpectrum = Render(editor);
        require(editor.HasSpectrum && points.Count == 128 && points.All(editor.PlotBounds.Contains)
            && Math.Abs(points[0].X - editor.PlotBounds.Left) < .000001
            && Math.Abs(points[^1].X - editor.PlotBounds.Right) < .000001,
            "Measured spectrum bins occupy the complete logarithmic 20 Hz–20 kHz plot and stay inside its separate -90–0 dBFS bounds.");
        require(PixelDifference(noSpectrum, withSpectrum, editor.PlotBounds) > 1500,
            "Real detached rendering draws a visible measured-spectrum underlay inside the EQ plot.");
        editor.ApplySpectrumAt(Enumerable.Repeat(-90f, 128).ToArray(), 4096, 1, 1.1);
        require(editor.SpectrumPoints().SequenceEqual(points),
            "Duplicate spectrum frame identifiers cannot replace measured data or create artificial motion.");
        editor.ApplySpectrumAt(bins, 4096, 1, 1.26);
        require(!editor.HasSpectrum && editor.SpectrumPoints().Count == 0,
            "A spectrum that stops advancing disappears after 250 ms instead of showing stale audio indefinitely.");
        editor.ApplySpectrumAt(bins, 6144, 1, 1.3);
        require(editor.HasSpectrum, "A fresh measured frame restores the spectrum after a stall.");
        editor.ApplySpectrumAt(bins, 1, 2, 1.4);
        require(editor.HasSpectrum && editor.SpectrumFrame == 1,
            "A new audio continuity revision accepts restarted frame numbering.");
        editor.ApplySpectrumAt(bins, 2, 2, 1.5, fresh: false);
        require(!editor.HasSpectrum, "Engine-unavailable telemetry clears the spectrum immediately.");
        editor.ApplySpectrumAt(bins, 96000000, 2, 1.51);
        editor.ApplySpectrumAt(bins, 96000000, 2, 1.52, fresh: false);
        editor.ApplySpectrumAt(bins, 2048, 2, 1.53);
        require(editor.HasSpectrum && editor.SpectrumFrame == 2048,
            "An engine restart at the same saved revision accepts the first new spectrum instead of waiting for its old frame count.");
        editor.ApplySpectrumAt(bins, 0, 2, 1.54);
        editor.ApplySpectrumAt(bins, 1, 2, 1.55);
        require(editor.HasSpectrum && editor.SpectrumFrame == 1,
            "A cleared native spectrum cursor permits the restarted stream to begin again from its first measured frame.");
        var invalid = bins.ToArray(); invalid[30] = float.NaN;
        editor.ApplySpectrumAt(invalid, 3, 2, 1.6);
        require(!editor.HasSpectrum, "Non-finite spectrum values cannot enter drawing geometry.");
        editor.ApplySpectrumAt([0f], 4, 2, 1.7);
        require(!editor.HasSpectrum, "Incomplete spectrum frames remain hidden instead of being stretched into misleading data.");
        float[] extremes = Enumerable.Range(0, 128).Select(index => index % 2 == 0 ? 10f : -150f).ToArray();
        editor.ApplySpectrumAt(extremes, 5, 2, 1.8);
        require(editor.SpectrumPoints().All(editor.PlotBounds.Contains),
            "Finite out-of-range measurements are bounded to the displayed dBFS scale.");
        editor.ApplySpectrumAt(bins, 8192, 2, 2);
        Render(editor);
        int textLayouts = editor.TextLayoutBuildCount;
        for (int frame = 1; frame <= 12; frame++)
        {
            editor.ApplySpectrumAt(bins, (ulong)(8192 + frame * 2048), 2, 2 + frame * .02);
            Render(editor);
        }
        require(editor.TextLayoutBuildCount == textLayouts,
            "Twelve live spectrum redraws reuse all unchanged chart labels, point numbers and caption glyph layouts.");
        require(edits == before && editor.SettingsRefreshCount == refreshes
            && JsonSerializer.Serialize(editor.SnapshotBands(), Protocol.Json) == settings,
            "Spectrum updates, stalls, resets and invalid data never edit audio parameters or refresh the settings model.");
        require(PresentationSource.FromVisual(editor) is null && !menu.IsOpen && !emptyMenu.IsOpen,
            "EQ verification renders entirely detached, with no native window, physical input, audio device or live settings access.");
        return editor;
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height)); element.UpdateLayout();
    }

    private static byte[] Render(FrameworkElement element)
    {
        var image = new RenderTargetBitmap((int)element.Width, (int)element.Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        var pixels = new byte[(int)element.Width * (int)element.Height * 4];
        image.CopyPixels(pixels, (int)element.Width * 4, 0); return pixels;
    }

    private static int PixelDifference(byte[] before, byte[] after, Rect plot)
    {
        int changed = 0;
        for (int y = (int)Math.Ceiling(plot.Top); y < plot.Bottom; y++)
            for (int x = (int)Math.Ceiling(plot.Left); x < plot.Right; x++)
            {
                int offset = (y * 540 + x) * 4;
                if (before[offset] != after[offset] || before[offset + 1] != after[offset + 1] || before[offset + 2] != after[offset + 2]) changed++;
            }
        return changed;
    }
}
