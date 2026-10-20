using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

// Measure the actual retained glyph drawing, rather than duplicating the
// editor's FormattedText alignment or positioning calculations.
internal static class EqTextLayoutVerification
{
    internal static void Run(Action<bool, string> require, Action<FrameworkElement, string, double> render)
    {
        int edits = 0;
        var editor = new EqualizerEditor { Width = 540, Height = 300 };
        editor.BandsEdited += _ => edits++;
        float[] spectrum = Enumerable.Range(0, 128).Select(index => (float)(-75 + 50 * Math.Exp(-Math.Pow((index - 72) / 18d, 2)))).ToArray();
        var bands = Enumerable.Range(0, 8).Select(index => new EqBand
        {
            FrequencyHz = 40 * Math.Pow(2, index), ResetFrequencyHz = 40 * Math.Pow(2, index),
            GainDb = 0, Q = 1, Type = "peak", Enabled = true
        }).ToArray();
        bands[^1] = new EqBand { FrequencyHz = 12345, ResetFrequencyHz = 12345, GainDb = -23.9,
            Q = 19.9999, Type = "highShelf", Enabled = false };
        ulong frame = 1;
        foreach (double width in new[] { 320d, 540d })
            foreach (double dpi in new[] { 96d, 144d, 192d })
            {
                editor.Width = width;
                VisualTreeHelper.SetRootDpi(editor, new DpiScale(dpi / 96, dpi / 96));
                editor.Apply([], false, -1);
                editor.ApplySpectrumAt(spectrum, frame++, 1, 1);
                Layout(editor);
                var size = editor.RenderSize; var plot = editor.PlotBounds;
                CheckState(empty: true, narrow: width == 320, "empty EQ", dpi);
                if (width == 540) render(editor, $"eq-empty-centred-{dpi:0}dpi.png", dpi);

                editor.Apply(bands, false, 7); Layout(editor);
                CheckState(empty: false, narrow: width == 320, "selected band", dpi);
                require(AutomationProperties.GetItemStatus(editor).Contains("Band 8", StringComparison.Ordinal)
                    && AutomationProperties.GetItemStatus(editor).Contains(12.35.ToString("0.##", CultureInfo.CurrentCulture) + " kHz", StringComparison.Ordinal)
                    && AutomationProperties.GetItemStatus(editor).Contains((-23.9).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB", StringComparison.Ordinal)
                    && AutomationProperties.GetItemStatus(editor).Contains(19.9999.ToString("0.0###", CultureInfo.CurrentCulture), StringComparison.Ordinal)
                    && AutomationProperties.GetItemStatus(editor).EndsWith(" · off", StringComparison.Ordinal),
                    "A visually shortened selected-band caption retains the complete band number, Q and disabled state for accessibility.");
                if (width == 540 && dpi == 144) render(editor, "eq-band-centred-144dpi.png", dpi);

                editor.SelectAllBands(); Layout(editor);
                CheckState(empty: false, narrow: width == 320, "multiple selected bands", dpi);
                require(AutomationProperties.GetItemStatus(editor).StartsWith("8 selected · active Band 8", StringComparison.Ordinal)
                    && AutomationProperties.GetItemStatus(editor).EndsWith(" · off", StringComparison.Ordinal),
                    "The multiselect caption preserves its entire selection and active-band details in accessible status.");
                if (width == 320 && dpi == 192) render(editor, "eq-multiselect-narrow-192dpi.png", dpi);
                require(editor.RenderSize == size && editor.PlotBounds == plot,
                    "Empty, selected and multiselect text leave the EQ plot bounds and control size unchanged at each width and DPI.");
            }
        try
        {
            ConsoleTheme.SetPreviewHighContrast(true);
            editor.Width = 320; VisualTreeHelper.SetRootDpi(editor, new DpiScale(1, 1));
            editor.Apply([], false, -1); Layout(editor);
            CheckState(empty: true, narrow: true, "high-contrast empty EQ", 96);
            render(editor, "eq-empty-centred-high-contrast.png", 96);
        }
        finally { ConsoleTheme.SetPreviewHighContrast(null); }
        require(edits == 0 && PresentationSource.FromVisual(editor) is null,
            "EQ text layout, selection, DPI and theme verification never publish settings edits or create a native window.");

        void CheckState(bool empty, bool narrow, string description, double dpi)
        {
            string settings = JsonSerializer.Serialize(editor.SnapshotBands(), Protocol.Json);
            Rect plot = editor.PlotBounds; Size size = editor.RenderSize;
            var glyphs = RenderedGlyphs(editor, dpi).ToArray();
            var caption = glyphs.Where(glyph => IsSize(glyph, 13) && glyph.Ink.Top < plot.Top - 6).ToArray();
            Rect captionInk = Union(caption);
            CheckCentred(captionInk, plot, description + " caption", require);
            require(captionInk.Top >= 4 && captionInk.Bottom < plot.Top - 6 && captionInk.Height <= 18,
                description + " keeps its top caption on one line above the plot.");
            if (narrow && !empty)
                require(caption.Any(glyph => glyph.Ellipsis),
                    description + " draws an actual ellipsis glyph when the narrow plot cannot fit its complete caption.");
            if (empty)
            {
                var hint = glyphs.Where(glyph => IsSize(glyph, 13) && glyph.Ink.Top > plot.Top && glyph.Ink.Bottom < plot.Bottom).ToArray();
                Rect hintInk = Union(hint);
                CheckCentred(hintInk, plot, description + " add-band hint", require);
                require(hintInk.Height <= 18 && hintInk.Bottom < plot.Top + plot.Height / 2 - 8,
                    description + " retains a single-line add-band hint above the flat zero-dB response.");
            }
            var dbfs = glyphs.Where(glyph => glyph.Text == "dBFS").ToArray();
            Rect dbfsInk = Union(dbfs);
            require(editor.HasSpectrum && !dbfsInk.IsEmpty && dbfsInk.Left > plot.Right
                && dbfsInk.Right < editor.ActualWidth && dbfsInk.Top < plot.Top && dbfsInk.Bottom > 0,
                description + " keeps the rendered spectrum dBFS label visible in the right axis gutter.");
            require(JsonSerializer.Serialize(editor.SnapshotBands(), Protocol.Json) == settings
                && editor.PlotBounds == plot && editor.RenderSize == size,
                description + " glyph rendering leaves all band settings, plot geometry and control dimensions untouched.");
        }
    }

    private static void CheckCentred(Rect ink, Rect plot, string description, Action<bool, string> require)
    {
        // Actual letter shapes have asymmetric side bearings; allow two DIPs
        // around their typographic centre without accepting a left-aligned run.
        require(!ink.IsEmpty && Math.Abs((ink.Left + ink.Right) / 2 - (plot.Left + plot.Right) / 2) <= 2
            && ink.Left >= plot.Left + 6 && ink.Right <= plot.Right - 6,
            description + " has centred rendered text ink with clear space beside both axis gutters.");
    }

    private sealed record GlyphInk(Rect Ink, double Size, string Text, bool Ellipsis);

    private static IEnumerable<GlyphInk> RenderedGlyphs(EqualizerEditor editor, double dpi)
    {
        Layout(editor);
        double scale = dpi / 96;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(editor.ActualWidth * scale),
            (int)Math.Ceiling(editor.ActualHeight * scale), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(editor);
        var drawing = VisualTreeHelper.GetDrawing(editor);
        return drawing is null ? [] : Glyphs(drawing, Matrix.Identity).ToArray();
    }

    private static IEnumerable<GlyphInk> Glyphs(Drawing drawing, Matrix parent)
    {
        if (drawing is DrawingGroup group)
        {
            Matrix transform = group.Transform?.Value ?? Matrix.Identity; transform.Append(parent);
            foreach (var child in group.Children)
                foreach (var glyph in Glyphs(child, transform)) yield return glyph;
        }
        else if (drawing is GlyphRunDrawing glyphDrawing && glyphDrawing.GlyphRun is GlyphRun run)
        {
            // GlyphRunDrawing.Bounds already includes the run's baseline origin.
            Rect ink = glyphDrawing.Bounds; ink.Transform(parent);
            if (ink.IsEmpty) yield break;
            string text = run.Characters is { Count: > 0 } characters ? new string(characters.ToArray()) : "";
            bool ellipsis = text.Contains('…') || run.GlyphTypeface.CharacterToGlyphMap.TryGetValue('…', out ushort index)
                && run.GlyphIndices.Contains(index);
            yield return new(ink, run.FontRenderingEmSize, text, ellipsis);
        }
    }

    private static bool IsSize(GlyphInk glyph, double size) => Math.Abs(glyph.Size - size) < .01;
    private static Rect Union(IEnumerable<GlyphInk> glyphs)
    {
        Rect result = Rect.Empty;
        foreach (var glyph in glyphs) result.Union(glyph.Ink);
        return result;
    }
    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height)); element.UpdateLayout();
    }
}
