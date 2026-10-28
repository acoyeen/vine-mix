using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VineMix.Desktop;

public partial class MainWindow
{
    private DispatcherTimer? _whatsNewTimer;
    private WhatsNewState? _whatsNewState;
    private Window? _whatsNewDialog;
    private ulong? _whatsNewFirstFrames;
    private bool _whatsNewPending;

    internal void BeginWhatsNew(bool existingWorkspace)
    {
        if (_mixer.IsPreview || _whatsNewTimer is not null) return;
        _whatsNewState = new(Path.Combine(UpdateTransaction.Root, "whats-new.json"));
        _whatsNewPending = _whatsNewState.Pending(ReleaseInfo.Version, existingWorkspace);
        _whatsNewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _whatsNewTimer.Tick += (_, _) =>
        {
            // CanEdit remains false throughout update health/commit and recovery.
            if (!_mixer.CanEdit) { _whatsNewFirstFrames = null; return; }
            if (_whatsNewFirstFrames is null) { _whatsNewFirstFrames = _mixer.MeterAudioFrames; return; }
            if (_mixer.MeterAudioFrames <= _whatsNewFirstFrames) return;
            if (!_whatsNewPending)
            {
                try { if (!existingWorkspace) _whatsNewState.InitializeFresh(ReleaseInfo.Version); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { System.Diagnostics.Debug.WriteLine(error); }
                _whatsNewTimer.Stop(); return;
            }
            bool ready = CanPresentWhatsNew(IsVisible, WindowState == WindowState.Minimized, IsActive, IsEnabled,
                _mixer.CanEdit, _mixer.PendingEditCount, Workspace.HasActiveGesture,
                GraphWorkspace.IsTextEditor(Keyboard.FocusedElement as DependencyObject),
                Mouse.Captured is not null, IsHelpOpen || IsNodeSearchOpen || _settingsMenu.IsOpen || OwnedWindows.Count > 0);
            if (!ready) return;
            _whatsNewTimer.Stop();
            OpenWhatsNew(automatic: true);
        };
        _whatsNewTimer.Start();
        Closed += (_, _) => _whatsNewTimer?.Stop();
    }

    internal static bool CanPresentWhatsNew(bool visible, bool minimized, bool active, bool enabled,
        bool healthy, int pendingEdits, bool gesture, bool editor, bool captured, bool otherSurface) =>
        visible && !minimized && active && enabled && healthy && pendingEdits == 0
        && !gesture && !editor && !captured && !otherSurface;

    internal void OpenWhatsNew(bool automatic = false)
    {
        if (_mixer.IsPreview) return;
        if (_whatsNewDialog is not null) { _whatsNewDialog.Activate(); return; }
        if (Workspace.HasActiveGesture || GraphWorkspace.IsTextEditor(Keyboard.FocusedElement as DependencyObject)) return;
        _settingsMenu.IsOpen = false;
        CloseHelp();
        _whatsNewState ??= new(Path.Combine(UpdateTransaction.Root, "whats-new.json"));
        var highlights = WhatsNewContent.Since(automatic ? _whatsNewState.PreviousVersion : null, ReleaseInfo.Version);
        var dialog = WorkspaceDialog.Create(this, "What's new in VineMix", 580);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Height = 560;
        dialog.MinHeight = 320;
        dialog.Content = CreateWhatsNewContent(highlights, dialog.Close);
        dialog.Closed += (_, _) =>
        {
            _whatsNewDialog = null;
            // Shutdown is not a dismissal. Pending notices survive an interrupted launch.
            if (Application.Current is App app && app.IsQuitting) return;
            _whatsNewPending = false;
            _whatsNewTimer?.Stop();
            try { _whatsNewState.Dismiss(ReleaseInfo.Version); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { Workspace.SetMessage("What's new was closed, but its reminder could not be saved. It may appear next time.", true); }
        };
        _whatsNewDialog = dialog;
        dialog.Show();
    }

    internal static FrameworkElement CreateWhatsNewContent(IReadOnlyList<ReleaseHighlight> highlights, Action close)
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        var heading = new TextBlock { Text = "What's new", FontSize = 22, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        header.Children.Add(heading);
        var version = new TextBlock { Text = ReleaseInfo.DisplayVersion, FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
        version.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        header.Children.Add(version); root.Children.Add(header);
        var cards = new StackPanel();
        foreach (var highlight in highlights.GroupBy(item => item.Version).Reverse().SelectMany(group => group))
        {
            var title = new TextBlock { Text = highlight.Title, FontSize = 15, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) };
            AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
            cards.Children.Add(title);
            var description = new TextBlock { Text = highlight.Description, FontSize = 13, LineHeight = 20,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22) };
            description.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
            cards.Children.Add(description);
        }
        if (highlights.Count == 0) cards.Children.Add(new TextBlock { Text = "You're up to date with the bundled highlights.", TextWrapping = TextWrapping.Wrap });
        var scroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(scroll, "Bundled release highlights");
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var dismiss = WorkspaceDialog.CloseButton(close);
        dismiss.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(dismiss, 2); root.Children.Add(dismiss);
        return root;
    }
}
