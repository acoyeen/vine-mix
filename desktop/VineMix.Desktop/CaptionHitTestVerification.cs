using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached hit geometry only; never creates a window or sends input.</summary>
internal static class CaptionHitTestVerification
{
    internal static async Task RunAsync(FrameworkElement root, MainWindow window, GraphWorkspace workspace,
        MixerViewModel mixer, Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(root) is null && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "Caption hit testing uses the real detached content with no HWND or native input.");
        var chrome = WindowChrome.GetWindowChrome(window)!;
        // This private WPF routine only compares Rect/Point values with chrome
        // dimensions. Do not invoke _HandleNCHitTest, which calls native APIs.
        var workerType = typeof(WindowChrome).Assembly.GetType("System.Windows.Shell.WindowChromeWorker", throwOnError: true)!;
        var worker = Activator.CreateInstance(workerType, nonPublic: true)!;
        workerType.GetField("_chromeInfo", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(worker, chrome);
        var classifyGeometry = workerType.GetMethod("_HitTestNca", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Visual dpiRoot = root;
        while (VisualTreeHelper.GetParent(dpiRoot) is Visual parent) dpiRoot = parent;
        var previousDpi = VisualTreeHelper.GetDpi(dpiRoot);
        double previousWidth = root.Width, previousHeight = root.Height;
        var viewport = ((Grid)workspace.Content).Children.OfType<Grid>().Single(grid => Grid.GetRow(grid) == 1);
        var fixture = UiPreview.CreateSnapshot([]);
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(new Point(), element.RenderSize));
        string Classify(Point point)
        {
            var hit = Hit(root, point);
            // Matches the precedence in WPF _HandleNCHitTest: interactive
            // chrome content opts out before caption/resize geometry is tested.
            if (hit is not null && (bool)hit.GetValue(WindowChrome.IsHitTestVisibleInChromeProperty)) return "CLIENT";
            var nativeHit = classifyGeometry.Invoke(worker, [new Rect(0, 0, root.Width, root.Height), point])!;
            // HTLEFT/HTSIZEFIRST and HTBOTTOMRIGHT/HTSIZELAST are enum aliases;
            // Enum.ToString may choose the range marker rather than the edge.
            return Convert.ToInt32(nativeHit) switch { 10 => "LEFT", 17 => "BOTTOMRIGHT", _ => nativeHit.ToString()! };
        }
        try
        {
            foreach (double dpi in new[] { 96d, 144d, 192d })
            foreach (var size in new[] { new Size(900, 580), new Size(1280, 800), new Size(1920, 1080) })
            {
                root.Width = size.Width; root.Height = size.Height;
                VisualTreeHelper.SetRootDpi(dpiRoot, new DpiScale(dpi / 96, dpi / 96));
                foreach (string warning in new[] { "", "Waiting for a selected audio device. Existing routes remain available. " + new string('W', 160) })
                {
                    fixture.Warning = warning; mixer.ApplyPreviewSnapshot(fixture);
                    workspace.SelectNode(mixer.GraphConfig.Nodes[0].Id);
                    await layout();
                    string scope = $"{size.Width:0}×{size.Height:0} at {dpi:0} DPI, {(warning.Length == 0 ? "empty" : "populated")} warning";
                    Rect canvas = Bounds(viewport);
                    var buttons = Descendants<Button>(root).Where(button => HasVisibleAncestors(button)
                        && button.ActualWidth > 0 && button.ActualHeight > 0
                        && Bounds(button).Bottom <= canvas.Top + .01).ToArray();
                    var buttonBounds = buttons.ToDictionary(button => button, Bounds);
                    var failures = new List<string>();
                    foreach (var button in buttons)
                    {
                        Rect bounds = buttonBounds[button];
                        foreach (Point point in new[] { Center(bounds), new Point(bounds.Left + 1, bounds.Top + bounds.Height / 2),
                            new Point(bounds.Right - 1, bounds.Top + bounds.Height / 2), new Point(bounds.Left + bounds.Width / 2, bounds.Top + 1),
                            new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - 1) })
                        {
                            var hit = Hit(root, point);
                            if (hit is null || !(bool)hit.GetValue(WindowChrome.IsHitTestVisibleInChromeProperty)
                                || Classify(point) != "CLIENT") failures.Add($"{button.Content}: {point} hit {hit?.GetType().Name ?? "none"}");
                        }
                    }
                    require(buttons.Length == 14 && failures.Count == 0,
                        $"All fourteen caption/toolbar buttons, including the website symbol and disabled history controls, remain client targets at their centres and inner edges ({scope}). {string.Join("; ", failures.Take(4))}");

                    failures.Clear();
                    var passive = Descendants<TextBlock>(root).Where(text => HasVisibleAncestors(text) && Bounds(text).Bottom <= canvas.Top
                        && !HasButtonAncestor(text)).ToArray();
                    foreach (var text in passive)
                    {
                        if (WindowChrome.GetIsHitTestVisibleInChrome(text)) failures.Add("Passive flag: " + text.Text);
                        Rect bounds = Bounds(text);
                        if (bounds.Width > 0 && bounds.Height > 0 && Classify(Center(bounds)) != "CAPTION")
                            failures.Add($"Text {text.Text}: {Center(bounds)} -> {Classify(Center(bounds))}");
                    }
                    // Scan both full rows, skipping actual button rectangles.
                    foreach (double y in new[] { Center(Bounds(window.HeaderBar)).Y, canvas.Top - GraphWorkspace.ToolbarHeight / 2 })
                    for (double x = 12; x < size.Width - 12; x += 17)
                    {
                        var point = new Point(x, y);
                        if (buttonBounds.Values.Any(bounds => bounds.Contains(point))) continue;
                        if (Classify(point) != "CAPTION") failures.Add($"Empty/passive {point} -> {Classify(point)}");
                    }
                    var brand = Descendants<BrandWordmark>(root).Single();
                    var website = buttons.Single(button => button.Name == "WebsiteButton");
                    Rect websiteBounds = Bounds(website);
                    Rect headerBounds = Bounds(window.HeaderBar), brandBounds = Bounds(brand);
                    Rect indicatorBounds = Bounds(window.ConnectionIndicator);
                    Rect warningBounds = Bounds(window.HeaderWarning), saveBounds = Bounds(window.SaveIndicator);
                    Rect settingsBounds = Bounds(window.SettingsButton), actionBounds = Bounds(window.WindowActions);
                    double pixelTolerance = 96 / dpi + .001;
                    Point headerCenter = Center(headerBounds), brandCenter = Center(brandBounds), websiteCenter = Center(websiteBounds);
                    require(Math.Abs(headerCenter.X - root.Width / 2) <= pixelTolerance
                        && Math.Abs(brandCenter.X - headerCenter.X) <= pixelTolerance
                        && Math.Abs(brandCenter.Y - headerCenter.Y) <= pixelTolerance
                        && Math.Abs(websiteCenter.X - headerCenter.X) <= pixelTolerance
                        && Math.Abs(websiteCenter.Y - headerCenter.Y) <= pixelTolerance,
                        $"The logo artwork and website target stay at the whole header's horizontal and vertical centre, independent of warning or right-side content ({scope}); header={headerBounds}, mark={brandBounds}, target={websiteBounds}.");
                    require(Math.Abs(indicatorBounds.Width - 8) <= pixelTolerance
                        && Math.Abs(indicatorBounds.Height - 8) <= pixelTolerance
                        && Math.Abs(indicatorBounds.Left - (headerBounds.Left + 16)) <= pixelTolerance
                        && Math.Abs(Center(indicatorBounds).Y - headerCenter.Y) <= pixelTolerance
                        && indicatorBounds.Right < websiteBounds.Left,
                        $"The larger 8 DIP connection indicator stays inset at the far left and vertically centred ({scope}); indicator={indicatorBounds}, header={headerBounds}.");
                    require(window.HeaderWarning.TextTrimming == TextTrimming.CharacterEllipsis
                        && window.HeaderWarning.TextWrapping == TextWrapping.NoWrap
                        && warningBounds.Left >= indicatorBounds.Right - pixelTolerance
                        && warningBounds.Right <= websiteBounds.Left + pixelTolerance
                        && saveBounds.Left >= websiteBounds.Right - pixelTolerance
                        && saveBounds.Right <= settingsBounds.Left + pixelTolerance
                        && settingsBounds.Right <= actionBounds.Left + pixelTolerance
                        && !Overlaps(warningBounds, saveBounds, pixelTolerance)
                        && !Overlaps(saveBounds, websiteBounds, pixelTolerance)
                        && !Overlaps(warningBounds, websiteBounds, pixelTolerance),
                        $"Warning and saved status retain separate header space without overlapping the centred logo, Settings or window actions ({scope}); warning={warningBounds}, saved={saveBounds}, logo={websiteBounds}, settings={settingsBounds}, actions={actionBounds}.");
                    require(WindowChrome.GetIsHitTestVisibleInChrome(website)
                        && HasButtonAncestor(brand) && Classify(Center(Bounds(brand))) == "CLIENT"
                        && website.Focusable && website.IsTabStop
                        && websiteBounds.Left >= chrome.ResizeBorderThickness.Left
                        && websiteBounds.Top >= chrome.ResizeBorderThickness.Top,
                        $"The wide website logo has a keyboard-focusable client target inside the resize border ({scope}).");
                    require(Classify(new Point(websiteBounds.Left - 1, websiteBounds.Top + websiteBounds.Height / 2)) == "CAPTION"
                        && Classify(new Point(websiteBounds.Right + 1, websiteBounds.Top + websiteBounds.Height / 2)) == "CAPTION"
                        && passive.Any(text => text.Name == "SaveIndicator")
                        && passive.Any(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path?.Path == nameof(MixerViewModel.Warning))
                        && failures.Count == 0,
                        $"Save status, warning and unused space around the website button and across both rows remain draggable ({scope}). {string.Join("; ", failures.Take(4))}");

                    double captionBottom = chrome.ResizeBorderThickness.Top + chrome.CaptionHeight;
                    double firstCanvasPixel = Math.Ceiling(canvas.Top * dpi / 96) * 96 / dpi;
                    require(!WindowChrome.GetIsHitTestVisibleInChrome(viewport)
                        && Math.Abs(captionBottom - canvas.Top) <= 96 / dpi + .001
                        && Classify(new Point(size.Width / 2, firstCanvasPixel + 96 / dpi)) == "CLIENT",
                        $"Caption ends at the canvas boundary within pixel rounding, and the canvas stays client content without overriding resizing ({scope}); caption={captionBottom}, canvas={canvas.Top}.");
                    var resizeTargets = new[] {
                        (new Point(size.Width / 2, 2), "TOP"), (new Point(2, 2), "TOPLEFT"),
                        (new Point(size.Width - 2, 2), "TOPRIGHT"), (new Point(6, size.Height / 2), "LEFT"),
                        (new Point(size.Width - 6, size.Height / 2), "RIGHT"), (new Point(size.Width / 2, size.Height - 6), "BOTTOM") };
                    var resizeFailures = resizeTargets.Where(target => Classify(target.Item1) != target.Item2)
                        .Select(target => $"{target.Item1}: expected {target.Item2}, got {Classify(target.Item1)}").ToArray();
                    require(resizeFailures.Length == 0,
                        $"Native top/corner resizing and the full side/bottom border remain available ({scope}). {string.Join("; ", resizeFailures)}");
                }
            }
        }
        finally
        {
            root.Width = previousWidth; root.Height = previousHeight;
            VisualTreeHelper.SetRootDpi(dpiRoot, previousDpi);
            fixture.Warning = ""; mixer.ApplyPreviewSnapshot(fixture);
            await layout();
        }
        require(PresentationSource.FromVisual(root) is null && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "Caption geometry verification finishes without creating a native window.");
    }

    private static Point Center(Rect bounds) => new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    private static bool Overlaps(Rect first, Rect second, double tolerance) =>
        Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left) > tolerance
        && Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top) > tolerance;
    private static DependencyObject? Hit(Visual root, Point point)
    {
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(root,
            visual => visual is UIElement element && (element.Visibility != Visibility.Visible || !element.IsHitTestVisible)
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        // InputHitTest ignores disabled input targets. Use the enabled visual
        // ancestor while keeping detached IsVisible out of the filtering.
        while (hit is not null && hit is not UIElement { IsEnabled: true }) hit = VisualTreeHelper.GetParent(hit);
        return hit;
    }
    private static bool HasButtonAncestor(DependencyObject item)
    {
        for (var parent = VisualTreeHelper.GetParent(item); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is Button) return true;
        return false;
    }
    private static bool HasVisibleAncestors(DependencyObject item)
    {
        for (DependencyObject? current = item; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement element && element.Visibility != Visibility.Visible) return false;
        return true;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
