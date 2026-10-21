using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>
/// Checks the actual packaged brand resources and detached WPF presentation.
/// No native window, menu, tooltip, engine connection or settings write is used.
/// </summary>
internal static class BrandingVerification
{
    internal static async Task RunAsync(MainWindow window, FrameworkElement root,
        Func<Task> layout, Action<bool, string> require)
    {
        require(window.DataContext is MixerViewModel { IsPreview: true }
            && PresentationSource.FromVisual(root) is null && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "Brand verification uses the real preview interface without an engine or native window.");

        var sans = (FontFamily)Application.Current.FindResource("NodeFontFamily");
        var mono = (FontFamily)Application.Current.FindResource("NodeNumericFontFamily");
        foreach (FontWeight weight in new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold })
            CheckFace(sans, weight, "IBM Plex Sans", require);
        GlyphTypeface digits = CheckFace(mono, FontWeights.Normal, "IBM Plex Mono", require);
        CheckFace(window.FontFamily, FontWeights.Normal, "IBM Plex Sans", require);
        const string values = "0123456789.,:+-/%dBHzms−∞·…×";
        require(values.All(character => digits.CharacterToGlyphMap.ContainsKey(character)),
            "The packaged numeric face contains the digits, signs, units and symbols used by real audio readouts.");
        double[] advances = "0123456789".Select(character => digits.AdvanceWidths[digits.CharacterToGlyphMap[character]]).ToArray();
        require(advances.All(width => Math.Abs(width - advances[0]) < .00001),
            "Actual Plex Mono digit glyphs have equal advances, so changing values does not change digit-column alignment.");

        await layout();
        var mark = Descendants<BrandWordmark>(root).Single();
        require(AutomationProperties.GetName(mark) == "VineMix" && !mark.Focusable
            && window.Title.Contains("VineMix", StringComparison.Ordinal),
            "The wide logo retains the accessible product name and Windows title without becoming an extra keyboard stop.");
        require(mark.ActualWidth == 106 && mark.ActualHeight == 20
            && window.WebsiteButton.Focusable && window.WebsiteButton.IsTabStop
            && AutomationProperties.GetName(window.WebsiteButton) == "Open VineMix website",
            "The 106 by 20 DIP wide artwork has its own padded, accessible website button with native keyboard activation.");
        var brandParent = VisualTreeHelper.GetParent(mark);
        require(brandParent is not null && !Descendants<TextBlock>(brandParent).Any(text => text.Text == "VineMix"),
            "The application header uses the vector wordmark without an extra font-rendered product label.");
        var accentArtwork = (GeometryGroup)BrandWordmark.AccentGeometry;
        var inkArtwork = (GeometryGroup)BrandWordmark.InkGeometry;
        require(accentArtwork.Children.Count == 6 && inkArtwork.Children.Count == 5
            && accentArtwork.Children[2].Bounds.Top == accentArtwork.Children[1].Bounds.Top
            && inkArtwork.Children[2].Bounds.Top == inkArtwork.Children[1].Bounds.Top
            && accentArtwork.Children[2].Bounds.Width == accentArtwork.Children[3].Bounds.Width
            && inkArtwork.Children[2].Bounds.Width == inkArtwork.Children[3].Bounds.Width
            && accentArtwork.Children[3].Bounds.Top == 318 && inkArtwork.Children[3].Bounds.Top == 318,
            "The packaged wide vector retains full-width i dots aligned to the capitals and the shortened i stems.");
        require(window.Icon is BitmapSource { PixelWidth: > 0, PixelHeight: > 0 },
            "The real main window has a decoded application icon rather than a generic Windows icon.");
        CheckIconResource("app.ico", require);
        CheckIconResource("tray.ico", require);
        using (var tray = new TrayBranding())
            require(tray.MenuFont.FontFamily.Name == "IBM Plex Sans" && tray.Icon.Width > 0,
                "The tray resolves its process-private Plex menu font and packaged icon without installing a system font or creating a NotifyIcon.");

        AuditText(root, "Main workspace", require);
        var numericEntries = Descendants<TextBox>(root)
            .Where(entry => ReferenceEquals(entry.Style, Application.Current.FindResource("NodeNumericEntry"))).ToArray();
        require(numericEntries.Length > 0 && numericEntries.All(entry => IsFamily(entry.FontFamily, entry.FontWeight, "IBM Plex Mono")),
            "Actual node numeric fields resolve Plex Mono independently of the interface's sans-serif face.");

        var mixer = (MixerViewModel)window.DataContext;
        ContextMenu[] menus = [window.Workspace.CreateAddMenu(new Point()), MainWindow.CreateSettingsMenu(mixer)];
        require(menus.All(menu => MenuItems(menu).All(item => !ToolTipService.GetIsEnabled(item))),
            "Settings and node-library rows share menu-scoped hover help without a competing native tooltip owner.");
        foreach (ContextMenu menu in menus)
        {
            MeasureDetached(menu, 370);
            var items = MenuItems(menu).ToArray();
            require(!menu.IsOpen && PresentationSource.FromVisual(menu) is null
                && IsFamily(menu.FontFamily, menu.FontWeight, "IBM Plex Sans")
                && items.Length > 0 && items.All(item => IsFamily(item.FontFamily, item.FontWeight, "IBM Plex Sans")),
                "Actual Add and Settings menus resolve the bundled interface face while their native popups remain closed.");
            AuditText(menu, "Detached menu", require);
        }

        var tip = new ToolTip { Content = NodeToolTip.Create("Sidechain detector", "Trigger audio controls ducking without entering the audible output.") };
        MeasureDetached(tip, 326);
        require(!tip.IsOpen && PresentationSource.FromVisual(tip) is null
            && IsFamily(tip.FontFamily, tip.FontWeight, "IBM Plex Sans"),
            "The independent tooltip template resolves Plex Sans without relying on Window font inheritance.");
        AuditText(tip, "Detached tooltip", require);

        CheckDrawnControlFonts(require);
        var uncheckedBox = new CheckBox { Content = "Enable effect", IsChecked = false };
        MeasureDetached(uncheckedBox, 200);
        var outline = uncheckedBox.Template.FindName("Box", uncheckedBox) as Border;
        require(outline?.BorderBrush is SolidColorBrush outlineBrush && outline.Background is SolidColorBrush fill
            && Contrast(outlineBrush.Color, fill.Color) >= 3,
            "The actual unchecked checkbox template has a visible outline with at least 3:1 contrast against its fill.");

        bool? previousHighContrast = ConsoleTheme.PreviewHighContrast;
        try
        {
            ConsoleTheme.SetPreviewHighContrast(false); await layout();
            CheckNormalPalette(require);
            Paint(root);
            var pieces = FilledGeometry(mark).ToArray();
            require(pieces.Length == 2 && pieces.All(piece => !piece.Geometry.Bounds.IsEmpty && piece.Geometry.GetArea() > 1)
                && Geometry.Combine(pieces[0].Geometry, pieces[1].Geometry, GeometryCombineMode.Intersect, null).GetArea() < .01,
                "The wide logo draws two non-overlapping vector colour groups, retaining the split symbol and outlined lettering.");
            require(pieces.Any(piece => SameBrush(piece.Brush, ConsoleTheme.Get("BrandAccentBrush")))
                && pieces.Any(piece => SameBrush(piece.Brush, ConsoleTheme.Get("ConsoleTextBrush"))),
                "The actual split logo draws the forest-green V and light M from live palette resources.");

            ConsoleTheme.SetPreviewHighContrast(true); await layout();
            Paint(root);
            require(SameBrush(ConsoleTheme.Get("BrandActionBrush"), SystemColors.HighlightBrush)
                && SameBrush(ConsoleTheme.Get("BrandActionTextBrush"), SystemColors.HighlightTextBrush)
                && SameBrush(ConsoleTheme.Get("BrandAccentBrush"), SystemColors.WindowTextBrush),
                "High contrast replaces branding roles with the user's system highlight and foreground colours.");
            var highContrastPieces = FilledGeometry(mark).ToArray();
            require(highContrastPieces.Length == 2
                && highContrastPieces.All(piece => SameBrush(piece.Brush, SystemColors.WindowTextBrush))
                && SameBrush(ConsoleTheme.Get("NodeAudioBrush"), SystemColors.WindowTextBrush)
                && SameBrush(ConsoleTheme.Get("NodeSidechainBrush"), SystemColors.WindowTextBrush),
                "The rendered logo and both route roles respond to high contrast without retaining fixed brand colours.");
            require(SameBrush(ConsoleTheme.Get("NodeSelectedBrush"), SystemColors.HighlightBrush)
                && SameBrush(ConsoleTheme.Get("ConsoleFocusBrush"), SystemColors.WindowTextBrush),
                "High contrast keeps separate system selection and keyboard-focus treatments.");

            ConsoleTheme.SetPreviewHighContrast(false); await layout(); Paint(root);
            CheckNormalPalette(require);
            require(FilledGeometry(mark).Any(piece => SameBrush(piece.Brush, ConsoleTheme.Get("BrandAccentBrush"))),
                "Leaving high contrast restores the rendered forest-green mark through dynamic resources.");
        }
        finally
        {
            ConsoleTheme.SetPreviewHighContrast(previousHighContrast);
            await layout();
        }

        require(PresentationSource.FromVisual(root) is null && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "Font, brand, colour and high-contrast checks finish without opening or activating a native window.");
    }

    private static GlyphTypeface CheckFace(FontFamily family, FontWeight weight, string expected, Action<bool, string> require)
    {
        bool found = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out var face);
        require(found && face is not null && HasFamily(face, expected)
            && face.FontUri.ToString().Contains("Assets/Fonts/", StringComparison.OrdinalIgnoreCase)
            && face.StyleSimulations == StyleSimulations.None,
            $"{expected} {weight} resolves an actual packaged face without a system fallback or synthesized weight.");
        return face!;
    }

    private static bool HasFamily(GlyphTypeface face, string expected) => face.FamilyNames.Values
        .Any(name => name.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase));

    private static bool IsFamily(FontFamily family, FontWeight weight, string expected) =>
        new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out var face)
        && HasFamily(face, expected) && face.StyleSimulations == StyleSimulations.None;

    private static bool IsSymbolFont(FontFamily family) => family.Source.Contains("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase)
        || family.Source.Contains("Segoe MDL2", StringComparison.OrdinalIgnoreCase);

    private static void AuditText(DependencyObject root, string context, Action<bool, string> require)
    {
        var text = Descendants<TextBlock>(root).Where(block => block.Text.Length > 0 && !IsSymbolFont(block.FontFamily)).ToArray();
        var invalid = text.Where(block => !IsFamily(block.FontFamily, block.FontWeight, "IBM Plex Sans")
            && !IsFamily(block.FontFamily, block.FontWeight, "IBM Plex Mono")).Select(block => block.Text).Distinct().ToArray();
        require(text.Length > 0 && invalid.Length == 0,
            context + " text resolves real Plex faces, preserving Fluent icon glyphs. " + string.Join("; ", invalid.Take(4)));
    }

    private static void CheckNormalPalette(Action<bool, string> require)
    {
        require(Colour("NodeAudioBrush") == (Color)ColorConverter.ConvertFromString("#78BE98")
            && Colour("NodeSidechainBrush") == (Color)ColorConverter.ConvertFromString("#B8A0D8")
            && Colour("NodeControlBrush") != Colour("NodeAudioBrush")
            && Colour("NodeControlBrush") != Colour("NodeSidechainBrush"),
            "Audio remains green, sidechain remains lavender and control connections retain a separate colour.");
        Color accent = Colour("BrandAccentBrush"), action = Colour("BrandActionBrush");
        require(accent.G > accent.R && accent.G > accent.B && action.G > action.R && action.G > action.B
            && accent != Colour("NodeAudioBrush") && action != Colour("NodeAudioBrush"),
            "Forest-green branding has dedicated roles distinct from the established audio-cable green.");
        require(Contrast(action, Colour("BrandActionTextBrush")) >= 4.5,
            "Normal-size labels on the forest-green primary action retain at least 4.5:1 contrast.");
        require(new[] { "ConsoleWindowBrush", "ConsolePanelBrush", "ConsoleHeaderBandBrush" }
                .All(surface => Contrast(accent, Colour(surface)) >= 3),
            "The forest-green mark and active indicators retain at least 3:1 contrast on their dark surfaces.");
        require(new[] { "ConsoleWindowBrush", "ConsolePanelBrush", "ConsoleWellBrush", "ConsoleButtonFaceBrush" }
                .All(surface => Contrast(Colour("ConsoleTextBrush"), Colour(surface)) >= 4.5
                    && Contrast(Colour("ConsoleMutedBrush"), Colour(surface)) >= 4.5),
            "Primary and secondary text retain at least 4.5:1 contrast across the working surfaces.");
    }

    private static void CheckDrawnControlFonts(Action<bool, string> require)
    {
        var eq = new EqualizerEditor { Width = 540, Height = 300 };
        eq.SetResourceReference(TextElement.FontFamilyProperty, "NodeFontFamily");
        eq.Apply([new EqBand { Type = "peak", FrequencyHz = 12345, GainDb = -23.9, Q = 19.9999 }], false, 0);
        var voice = new VoiceColorPad { Width = 320, Height = 388 };
        voice.SetResourceReference(TextElement.FontFamilyProperty, "NodeFontFamily");
        foreach (FrameworkElement control in new FrameworkElement[] { eq, voice })
        {
            foreach (double dpi in new[] { 96d, 144d, 192d })
            {
                VisualTreeHelper.SetRootDpi(control, new DpiScale(dpi / 96, dpi / 96));
                control.Measure(new Size(control.Width, control.Height));
                control.Arrange(new Rect(0, 0, control.Width, control.Height));
                control.UpdateLayout(); Paint(control, dpi);
                var runs = Glyphs(VisualTreeHelper.GetDrawing(control)).ToArray();
                var content = new Rect(0, 0, control.ActualWidth, control.ActualHeight); content.Inflate(1, 1);
                require(runs.Length > 0 && runs.All(run => HasFamily(run.Run.GlyphTypeface, "IBM Plex Sans")
                        || HasFamily(run.Run.GlyphTypeface, "IBM Plex Mono"))
                    && runs.All(run => content.Contains(run.Bounds)),
                    $"{control.GetType().Name} draws actual Plex glyphs inside its control at {dpi:0} DPI.");
            }
        }
        require(voice.LabelBounds.All(label => voice.PadBounds.Contains(label.Bounds)
                && !label.Bounds.IntersectsWith(voice.HandleBounds)),
            "Voice Color's measured Plex labels stay inside the pad and clear of its neutral handle.");
    }

    private static void CheckIconResource(string name, Action<bool, string> require)
    {
        var uri = new Uri("/" + typeof(App).Assembly.GetName().Name + ";component/Assets/Branding/" + name, UriKind.Relative);
        using Stream stream = Application.GetResourceStream(uri)!.Stream;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1,
            name + " is a packaged Windows icon resource with a valid ICO signature.");
        int count = reader.ReadUInt16();
        stream.Position = 0;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int[] expectedSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        require(count == decoder.Frames.Count && expectedSizes.All(size => decoder.Frames
                .Any(frame => frame.PixelWidth == size && frame.PixelHeight == size)),
            name + " decodes its real 16–256 px frames, including intermediate display-scaling sizes.");
        foreach (BitmapFrame frame in decoder.Frames)
        {
            var rgba = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = checked(frame.PixelWidth * 4);
            byte[] pixels = new byte[checked(stride * frame.PixelHeight)];
            rgba.CopyPixels(pixels, stride, 0);
            bool visible = false, transparent = false;
            for (int offset = 3; offset < pixels.Length; offset += 4)
            {
                visible |= pixels[offset] > 0;
                transparent |= pixels[offset] == 0;
            }
            require(visible && transparent,
                $"{name} {frame.PixelWidth}px contains visible artwork and transparent padding rather than an empty or opaque placeholder.");
            int padding = Math.Max(1, frame.PixelWidth / 10);
            bool clearBackground = true;
            for (int y = 0; y < frame.PixelHeight; y++)
            for (int x = 0; x < frame.PixelWidth; x++)
                if (x < padding || x >= frame.PixelWidth - padding || y < padding || y >= frame.PixelHeight - padding)
                    clearBackground &= pixels[y * stride + x * 4 + 3] == 0;
            int gapX = frame.PixelWidth / 2, gapTop = (int)(frame.PixelHeight * .48);
            int gapRows = Math.Max(1, (int)(frame.PixelHeight * .62) - gapTop);
            bool clearGap = Enumerable.Range(gapTop, gapRows).Any(y => pixels[y * stride + gapX * 4 + 3] == 0);
            require(clearBackground && clearGap,
                $"{name} {frame.PixelWidth}px has transparent outer margins and a transparent gap between the V and M, with no background plate.");
        }
    }

    private static void MeasureDetached(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(), element.DesiredSize)); element.UpdateLayout();
    }

    private static void Paint(FrameworkElement element, double dpi = 96)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi / 96)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi / 96)), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
    }

    private static IEnumerable<(GlyphRun Run, Rect Bounds)> Glyphs(Drawing? drawing, Matrix? parent = null)
    {
        Matrix transform = parent ?? Matrix.Identity;
        if (drawing is DrawingGroup group)
        {
            Matrix nested = group.Transform?.Value ?? Matrix.Identity; nested.Append(transform);
            foreach (Drawing child in group.Children)
                foreach (var run in Glyphs(child, nested)) yield return run;
        }
        else if (drawing is GlyphRunDrawing { GlyphRun: { } run } glyph)
        {
            Rect bounds = glyph.Bounds; bounds.Transform(transform);
            yield return (run, bounds);
        }
    }

    private static IEnumerable<(Geometry Geometry, Brush Brush)> FilledGeometry(Visual visual)
    {
        foreach (var piece in GeometryDrawings(VisualTreeHelper.GetDrawing(visual), Matrix.Identity)) yield return piece;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            if (VisualTreeHelper.GetChild(visual, index) is Visual child)
                foreach (var piece in FilledGeometry(child)) yield return piece;
    }

    private static IEnumerable<(Geometry Geometry, Brush Brush)> GeometryDrawings(Drawing? drawing, Matrix parent)
    {
        if (drawing is DrawingGroup group)
        {
            Matrix transform = group.Transform?.Value ?? Matrix.Identity; transform.Append(parent);
            foreach (Drawing child in group.Children)
                foreach (var piece in GeometryDrawings(child, transform)) yield return piece;
        }
        else if (drawing is GeometryDrawing { Brush: { } brush, Geometry: { } geometry })
        {
            Geometry copy = geometry.Clone();
            Matrix transform = copy.Transform?.Value ?? Matrix.Identity; transform.Append(parent);
            copy.Transform = new MatrixTransform(transform);
            yield return (copy, brush);
        }
    }

    private static IEnumerable<MenuItem> MenuItems(ItemsControl menu)
    {
        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (MenuItem child in MenuItems(item)) yield return child;
        }
    }

    private static Color Colour(string key) => ((SolidColorBrush)ConsoleTheme.Get(key)).Color;
    private static bool SameBrush(Brush first, Brush second) => first is SolidColorBrush left
        && second is SolidColorBrush right && left.Color == right.Color;
    private static double Contrast(Color first, Color second)
    {
        double Luminance(Color colour)
        {
            double Linear(byte value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Linear(colour.R) + .7152 * Linear(colour.G) + .0722 * Linear(colour.B);
        }
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }
}
