using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

internal static class CreditsVerification
{
    internal static void Run(Action<bool, string> require, Action<FrameworkElement, string> capture)
    {
        var requests = new List<string>();
        bool closed = false, opened = false;
        var about = MainWindow.CreateAboutContent(() => { }, _ => { }, showCredits: () => opened = true);
        about.Measure(new Size(520, 600)); about.Arrange(new Rect(0, 0, 520, 600)); about.UpdateLayout();
        Descendants<Button>(about).Single(button => Equals(button.Content, "Credits and licences"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(opened, "The actual About action opens Credits and licences through its callback.");
        require(Descendants<TextBlock>(about).Any(block => block.Text == "VineMix by acoyeen"),
            "About identifies the original project and maintainer without needing a network connection.");

        var view = CreditsContent.Create(() => closed = true, requests.Add);
        var host = new Border { Child = view, Width = 680, Height = 620 };
        host.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
        host.SetResourceReference(TextElement.ForegroundProperty, "ConsoleTextBrush");
        host.SetResourceReference(TextElement.FontFamilyProperty, "NodeFontFamily");
        TextElement.SetFontSize(host, 13);
        var owner = new Window { Content = host };
        bool? previousContrast = ConsoleTheme.PreviewHighContrast;
        try
        {
            Measure();
            var selector = Descendants<ComboBox>(view).Single();
            var text = Descendants<TextBox>(view).Single();
            require(text.IsReadOnly && text.IsReadOnlyCaretVisible && text.IsTabStop
                && text.TextWrapping == TextWrapping.Wrap && text.VerticalScrollBarVisibility == ScrollBarVisibility.Auto
                && text.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled,
                "Offline notice text supports keyboard selection, copying, wrapping and vertical scrolling.");
            require(selector.IsTabStop && AutomationProperties.GetName(selector) == "Notice to read",
                "The notice selector has a keyboard stop and an accessible name.");
            require(DistributionNotices.ReadText("LICENSE").Contains("Copyright (c) 2026 acoyeen (VineMix, https://vinemix.app)", StringComparison.Ordinal),
                "The embedded MIT licence carries the authoritative project, author and website notice.");
            string policy = DistributionNotices.ReadText("ATTRIBUTION.md");
            require(policy.Contains("not additional MIT licence conditions", StringComparison.Ordinal)
                && policy.Contains("does not retroactively require", StringComparison.Ordinal),
                "The full offline policy distinguishes requested interface credit from MIT conditions and preserves earlier permissions.");
            foreach (bool contrast in new[] { false, true })
            {
                ConsoleTheme.SetPreviewHighContrast(contrast);
                foreach (int width in new[] { 680, 440 })
                {
                    host.Width = width;
                    host.Height = width == 440 ? 540 : 620;
                    for (int index = 0; index < DistributionNotices.FileNames.Length; index++)
                    {
                        selector.SelectedIndex = index;
                        Measure();
                        string file = DistributionNotices.FileNames[index];
                        require(text.Text == DistributionNotices.DisplayText(file) && text.Text.Length > 500
                            && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(text)),
                            "Each notice selection loads its complete embedded content with an accessible document name.");
                        require(text.ActualWidth <= host.ActualWidth - 48 + 1 && text.ActualHeight > 120,
                            "The notice reader remains inside its narrow and standard-width layouts with readable document space.");
                        var closeButton = Descendants<Button>(view).Single(button => Equals(button.Content, "Close"));
                        Rect closeBounds = closeButton.TransformToAncestor(host).TransformBounds(new Rect(closeButton.RenderSize));
                        require(closeBounds.Bottom <= host.ActualHeight && closeBounds.Right <= host.ActualWidth,
                            "The close action stays visible independently of the document's scroll position.");
                        if (contrast)
                            require(Equals(text.Foreground, ConsoleTheme.Get("ConsoleTextBrush")),
                                "The offline reader follows the high-contrast foreground palette.");
                        capture(host, $"credits-{index}-{width}{(contrast ? "-high-contrast" : "")}.png");
                    }
                }
            }
            foreach (var label in new[] { "Project website", "Original source", "Attribution online" })
                Descendants<Button>(view).Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(requests.SequenceEqual(new[] { MainWindow.WebsiteUrl, CreditsContent.RepositoryUrl, CreditsContent.AttributionUrl }),
                "The optional online actions use the original website, repository and attribution URLs through an inert callback.");
            Descendants<Button>(view).Single(button => Equals(button.Content, "Close")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(closed && PresentationSource.FromVisual(host) is null,
                "The offline Credits Close action works without a native window, engine or network request.");
        }
        finally { ConsoleTheme.SetPreviewHighContrast(previousContrast); owner.Content = null; owner.Close(); }

        void Measure()
        {
            host.Measure(new Size(host.Width, host.Height));
            host.Arrange(new Rect(0, 0, host.Width, host.Height)); host.UpdateLayout();
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
