using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace VineMix.Desktop;

public partial class MainWindow
{
    private Window? _aboutDialog;
    private Window? _creditsDialog;

    private void OpenAbout()
    {
        _settingsMenu.IsOpen = false;
        if (_aboutDialog is not null) { _aboutDialog.Activate(); return; }
        var dialog = WorkspaceDialog.Create(this, "About VineMix", 520);
        dialog.Content = CreateAboutContent(dialog.Close, showUpdates: () => { dialog.Close(); OpenUpdates(); },
            showCredits: () => { dialog.Close(); OpenCredits(); });
        dialog.Closed += (_, _) => _aboutDialog = null;
        _aboutDialog = dialog;
        dialog.Show();
    }
    private void OpenCredits()
    {
        if (_creditsDialog is not null) { _creditsDialog.Activate(); return; }
        var dialog = WorkspaceDialog.Create(this, "Credits and licences", Math.Min(720, SystemParameters.WorkArea.Width));
        dialog.MinWidth = Math.Min(440, SystemParameters.WorkArea.Width);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Height = Math.Min(680, SystemParameters.WorkArea.Height);
        dialog.Content = CreditsContent.Create(dialog.Close, _openExternal);
        dialog.Closed += (_, _) => _creditsDialog = null;
        _creditsDialog = dialog;
        dialog.Show();
    }

    internal static FrameworkElement CreateAboutContent(Action closeAbout, Action<string>? copyVersion = null,
        Action? showUpdates = null, Action? showCredits = null)
    {
        var content = new StackPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = "About VineMix", FontSize = 22, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        content.Children.Add(heading);
        var description = new TextBlock { Text = "Local audio routing · Beta", FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 24) };
        description.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        content.Children.Add(description);
        var version = new TextBox { Text = ReleaseInfo.FullInformation, IsReadOnly = true, IsReadOnlyCaretVisible = true, TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 16) };
        version.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        AutomationProperties.SetName(version, "Full VineMix version and build information");
        content.Children.Add(version);
        var credit = new TextBlock { Text = "VineMix by acoyeen", TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8) };
        credit.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush");
        content.Children.Add(credit);
        if (showCredits is not null)
        {
            var credits = new Button { Content = "Credits and licences", HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 7, 12, 7), MinHeight = 34, Margin = new Thickness(0, 0, 0, 14) };
            AutomationProperties.SetHelpText(credits, "Read the MIT licence, attribution policy and third-party notices offline.");
            credits.Click += (_, _) => showCredits(); content.Children.Add(credits);
        }
        var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var close = WorkspaceDialog.CloseButton(closeAbout);
        DockPanel.SetDock(close, Dock.Left); footer.Children.Add(close);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var copy = new Button { Content = "Copy version information", Padding = new Thickness(12, 7, 12, 7),
            MinHeight = 34, Margin = new Thickness(8, 0, 0, 0) };
        copy.Click += (_, _) =>
        {
            try
            {
                if (copyVersion is null) Clipboard.SetText(ReleaseInfo.FullInformation);
                else copyVersion(ReleaseInfo.FullInformation);
                copy.Content = "Copied";
            }
            catch (System.Runtime.InteropServices.ExternalException) { version.SelectAll(); version.Focus(); }
        };
        if (showUpdates is not null)
        {
            var updates = new Button { Content = "Check for updates", Padding = new Thickness(12, 7, 12, 7),
                MinHeight = 34, Margin = new Thickness(8, 0, 0, 0) };
            updates.Click += (_, _) => showUpdates(); actions.Children.Add(updates);
        }
        actions.Children.Add(copy); footer.Children.Add(actions); content.Children.Add(footer);
        return content;
    }
}
