using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace VineMix.Desktop;

internal static class WhatsNewVerification
{
    internal static FrameworkElement Run(Action<bool, string> require)
    {
        string root = Path.Combine(Path.GetTempPath(), "VineMix-whats-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string old = "0.3.0-beta.1", current = "0.3.0-beta.5";
        try
        {
            string path = Path.Combine(root, "updates", "whats-new.json");
            var fresh = new WhatsNewState(path);
            require(!fresh.Pending(old, false) && !File.Exists(path), "Fresh install has no upgrade prompt and reading state does not write a dismissal.");
            fresh.InitializeFresh(old);
            require(fresh.Progress.LastSeenReleaseVersion is null && new WhatsNewState(path).PreviousVersion == old,
                "Healthy first install persists a baseline separately from the dismissal version.");
            var updated = new WhatsNewState(path);
            require(updated.Pending(current, true) && updated.Pending(current, false), "A newer version is pending across manual or automatic upgrades and restarts.");
            var highlights = WhatsNewContent.Since(updated.PreviousVersion, current);
            require(highlights.Any(item => item.Version == "0.3.0-beta.2") && highlights.Any(item => item.Version == "0.3.0-beta.3")
                && highlights.Any(item => item.Version == current) && highlights.All(item => !item.Description.Contains("http", StringComparison.OrdinalIgnoreCase)),
                "Skipped releases include all available bundled highlights after the previous version without remote HTML or downloads.");
            require(new WhatsNewState(path).Pending(current, true), "Reading or showing highlights leaves the notice pending until explicit dismissal.");
            updated.Dismiss(current);
            require(!new WhatsNewState(path).Pending(current, true) && updated.Progress.LastSeenReleaseVersion == current,
                "Dismissal is durable and suppresses repeat prompts for the same release.");
            updated.Dismiss(old);
            require(updated.Progress.LastSeenReleaseVersion == current && !updated.Pending(old, true),
                "Opening an older release never lowers the watermark or prompts on downgrade.");
            require(updated.Pending("0.3.0-beta.6", true), "A subsequent Beta 6 compares newer than Beta 5.");
            string legacy = Path.Combine(root, "legacy", "whats-new.json");
            require(new WhatsNewState(legacy).Pending(current, true) && !new WhatsNewState(legacy).Pending(current, false),
                "An existing pre-feature workspace gets one transition notice; a fresh workspace does not.");
            File.WriteAllText(path, "{invalid");
            var corrupt = new WhatsNewState(path);
            require(corrupt.Pending(current, true), "Malformed optional notice state cannot prevent startup and recovers with one notice.");
            string blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "fixture");
            var cannotSave = new WhatsNewState(Path.Combine(blocked, "whats-new.json"));
            bool failed = false;
            try { cannotSave.Dismiss(current); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
            require(failed && cannotSave.Progress.LastSeenReleaseVersion is null,
                "Failed reminder persistence reports failure and never claims a durable dismissal.");
            require(Directory.GetFiles(root, "settings.json", SearchOption.AllDirectories).Length == 0,
                "Notice state is independent of workspace graph settings and their Undo history.");
        }
        finally { Directory.Delete(root, recursive: true); }

        bool Ready(bool visible = true, bool minimized = false, bool active = true, bool enabled = true, bool healthy = true,
            int pending = 0, bool gesture = false, bool editor = false, bool captured = false, bool other = false) =>
            MainWindow.CanPresentWhatsNew(visible, minimized, active, enabled, healthy, pending, gesture, editor, captured, other);
        require(Ready() && !Ready(visible: false) && !Ready(minimized: true) && !Ready(active: false) && !Ready(enabled: false)
            && !Ready(healthy: false) && !Ready(pending: 1) && !Ready(gesture: true) && !Ready(editor: true)
            && !Ready(captured: true) && !Ready(other: true),
            "Automatic highlights defer background/tray, inactive/minimized windows, health or quit preparation, pending edits, numeric entry, gestures, capture and other dialogs.");
        int closed = 0;
        var content = MainWindow.CreateWhatsNewContent(WhatsNewContent.Since(old, current), () => closed++);
        var surface = new Border { Width = 580, Height = 560, Child = content };
        surface.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
        surface.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush");
        foreach (double width in new[] { 440d, 580d })
        foreach (double dpi in new[] { 96d, 144d, 192d })
        {
            VisualTreeHelper.SetRootDpi(surface, new DpiScale(dpi / 96, dpi / 96));
            surface.Width = width; surface.Measure(new Size(width, 560)); surface.Arrange(new Rect(0, 0, width, 560)); surface.UpdateLayout();
            var button = Descendants<Button>(surface).Single();
            var bounds = button.TransformToAncestor(surface).TransformBounds(new Rect(button.RenderSize));
            require(bounds.Left >= 0 && bounds.Right <= width && bounds.Bottom <= 560 && button.ActualHeight > 0,
                $"What's new keeps its dismiss action visible at width {width} and DPI {dpi}; highlights scroll independently.");
        }
        Descendants<Button>(surface).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(closed == 1 && Descendants<TextBlock>(surface).All(block => block.TextWrapping == TextWrapping.Wrap)
            && Descendants<ScrollViewer>(surface).Single().VerticalScrollBarVisibility == ScrollBarVisibility.Auto
            && PresentationSource.FromVisual(surface) is null,
            "Actual curated text wraps in a scrollable detached surface; dismissal is callable without a window or audio changes.");
        int opened = 0, aboutOpened = 0, helpOpened = 0;
        using var mixer = new MixerViewModel(preview: true);
        var settings = MainWindow.CreateSettingsMenu(mixer, showHelp: () => helpOpened++, showAbout: () => aboutOpened++,
            showWhatsNew: () => opened++);
        var whatsNew = settings.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "What's new?"));
        var about = settings.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "About VineMix"));
        require(settings.Items.IndexOf(about) == settings.Items.IndexOf(whatsNew) + 1
            && AutomationProperties.GetName(whatsNew) == "What's new?"
            && AutomationProperties.GetHelpText(whatsNew) == "Read the bundled release highlights.",
            "Settings places the accessible What's new? action immediately above About VineMix.");
        whatsNew.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(opened == 1 && aboutOpened == 0 && helpOpened == 0 && !settings.IsOpen && PresentationSource.FromVisual(settings) is null,
            "The real detached Settings What's new? action invokes its own callback once without opening Help, About or a native popup.");
        int helpClosed = 0;
        var help = new WorkspaceHelpView(() => helpClosed++);
        help.Measure(new Size(900, 600)); help.Arrange(new Rect(0, 0, 900, 600)); help.UpdateLayout();
        require(!Descendants<Button>(help).Any(button => button.Content is string text && text.StartsWith("What's new", StringComparison.Ordinal)),
            "The Help header no longer contains a What's new action.");
        help.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(helpClosed == 1 && opened == 1, "The remaining Help close action retains its own callback without reopening highlights.");
        return surface;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T matching) yield return matching;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
