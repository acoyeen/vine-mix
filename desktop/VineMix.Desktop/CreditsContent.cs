using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VineMix.Desktop;

internal static class CreditsContent
{
    internal const string RepositoryUrl = "https://github.com/acoyeen/vine-mix";
    internal const string AttributionUrl = "https://vinemix.app/attribution";

    internal static FrameworkElement Create(Action close, Action<string> openExternal)
    {
        var content = new Grid { Margin = new Thickness(24) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            content.RowDefinitions.Add(new RowDefinition { Height = height });
        KeyboardNavigation.SetTabNavigation(content, KeyboardNavigationMode.Cycle);
        var heading = new TextBlock { Text = "Credits and licences", FontSize = 22, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        content.Children.Add(heading);

        var introduction = new StackPanel { Margin = new Thickness(0, 6, 0, 14) };
        introduction.Children.Add(new TextBlock { Text = "VineMix by acoyeen", TextWrapping = TextWrapping.Wrap });
        var description = new TextBlock { Text = "These notices are included with VineMix and can be read offline.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10) };
        description.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        introduction.Children.Add(description);
        var links = new WrapPanel();
        foreach (var (label, url) in new[] { ("Project website", MainWindow.WebsiteUrl), ("Original source", RepositoryUrl), ("Attribution online", AttributionUrl) })
        {
            var link = new Button { Content = label, Padding = new Thickness(10, 6, 10, 6), MinHeight = 32,
                Margin = new Thickness(0, 0, 8, 6), ToolTip = url };
            AutomationProperties.SetName(link, label);
            AutomationProperties.SetHelpText(link, "Open " + url + " in your browser.");
            link.Click += (_, _) => openExternal(url);
            links.Children.Add(link);
        }
        introduction.Children.Add(links);
        Grid.SetRow(introduction, 1); content.Children.Add(introduction);

        var selector = new ComboBox { Margin = new Thickness(0, 0, 0, 12), MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        selector.Items.Add("MIT licence"); selector.Items.Add("Attribution policy"); selector.Items.Add("Third-party notices");
        AutomationProperties.SetName(selector, "Notice to read");
        Grid.SetRow(selector, 2); content.Children.Add(selector);
        var text = new TextBox
        {
            IsReadOnly = true, IsReadOnlyCaretVisible = true, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12),
            BorderThickness = new Thickness(1), Background = Brushes.Transparent
        };
        text.SetResourceReference(Control.ForegroundProperty, "ConsoleTextBrush");
        text.SetResourceReference(Control.BorderBrushProperty, "ConsoleControlBorderBrush");
        AutomationProperties.SetHelpText(text, "Read-only notice. Use arrow keys or Page Up and Page Down to read; Ctrl+A and Ctrl+C copy the text.");
        selector.SelectionChanged += (_, _) =>
        {
            int index = selector.SelectedIndex;
            if (index < 0 || index >= DistributionNotices.FileNames.Length) return;
            text.Text = DistributionNotices.DisplayText(DistributionNotices.FileNames[index]);
            AutomationProperties.SetName(text, (string)selector.SelectedItem);
            text.ScrollToHome();
        };
        Grid.SetRow(text, 3); content.Children.Add(text);
        selector.SelectedIndex = 0;
        var closeButton = WorkspaceDialog.CloseButton(close);
        closeButton.Margin = new Thickness(0, 14, 0, 0);
        Grid.SetRow(closeButton, 4); content.Children.Add(closeButton);
        return content;
    }
}
