using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AudioMixer.Desktop;

internal static class WorkspaceHelpVerification
{
    internal static async Task RunAsync(MainWindow window, MixerViewModel mixer, FrameworkElement root,
        Func<Task> layout, Action<bool, string> require, Action<string> render)
    {
        var workspace = window.Workspace;
        string graph = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        long edits = mixer.ConfigurationPresentationCount;
        int history = mixer.EditHistory.Entries.Count;
        string saveStatus = mixer.SaveStatus;
        require(!window.IsHelpOpen && workspace.IsEnabled, "The shortcut reference starts hidden with the canvas usable.");
        require(!window.TryHelpShortcut(Key.F1, ModifierKeys.Control) && !window.IsHelpOpen,
            "Modified F1 is not consumed as the reference shortcut.");
        var draft = new TextBox { Text = "-12.5" };
        require(!window.TryHelpShortcut(Key.F1, ModifierKeys.None, draft) && !window.IsHelpOpen && draft.Text == "-12.5",
            "F1 leaves a focused text editor and its uncommitted draft alone.");
        var menu = MainWindow.CreateSettingsMenu(mixer, workspace.SetMessage, window.OpenHelp);
        var help = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Help & shortcuts"));
        require(help.InputGestureText == "F1" && help.IsEnabled,
            "Settings exposes the reference and advertises its F1 shortcut.");
        help.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await layout();
        require(window.IsHelpOpen && !workspace.IsEnabled && !menu.IsOpen,
            "The detached Settings action opens the reference and prevents underlying canvas edits without a popup.");
        require(window.TryHelpShortcut(Key.F1, ModifierKeys.None) && window.IsHelpOpen,
            "Repeated F1 keeps the existing reference open instead of toggling it or creating duplicates.");
        var overlay = (DependencyObject)VisualTreeHelper.GetParent(window.HelpView);
        require(KeyboardNavigation.GetTabNavigation(overlay) == KeyboardNavigationMode.Cycle
            && KeyboardNavigation.GetControlTabNavigation(overlay) == KeyboardNavigationMode.Cycle,
            "Tab and Ctrl+Tab stay within the reference while it is open.");
        require(window.HelpView.CloseButton.IsEnabled && window.HelpView.CloseButton.IsTabStop
            && window.HelpView.ScrollViewer.Focusable,
            "The reference has a keyboard-reachable Close action and scrollable reading area.");
        render("help-shortcuts.png");
        window.HelpView.ScrollViewer.ScrollToEnd();
        await layout();
        render("help-shortcuts-equalizer.png");
        window.HelpView.ScrollViewer.ScrollToTop();
        await layout();
        double oldWidth = root.Width, oldHeight = root.Height;
        try
        {
            root.Width = 900; root.Height = 580; await layout();
            require(window.HelpView.ActualWidth <= root.ActualWidth - 48
                && window.HelpView.ActualHeight <= root.ActualHeight - MainWindow.TitleBarHeight,
                "At the minimum app size the help surface remains inside the workspace.");
            require(window.HelpView.ScrollViewer.ScrollableHeight > 0,
                "At the minimum app size the reference can scroll instead of clipping its final instructions.");
            render("help-shortcuts-small.png");
        }
        finally { root.Width = oldWidth; root.Height = oldHeight; await layout(); }
        require(window.TryHelpShortcut(Key.Escape, ModifierKeys.None) && !window.IsHelpOpen && workspace.IsEnabled,
            "Escape closes help and restores the canvas without cancelling a graph edit.");
        require(!window.TryHelpShortcut(Key.Escape, ModifierKeys.None),
            "When help is hidden Escape remains available to the graph and its controls.");
        window.OpenHelp();
        window.HelpView.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(!window.IsHelpOpen && workspace.IsEnabled,
            "The actual Close button also restores the workspace.");
        require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == graph
            && mixer.ConfigurationPresentationCount == edits && mixer.EditHistory.Entries.Count == history
            && mixer.SaveStatus == saveStatus,
            "Opening, reading and closing help leaves graph, camera, save status, queued dispatch and edit history unchanged.");
        using var offline = new MixerViewModel(preview: true);
        bool invoked = false;
        var offlineHelp = MainWindow.CreateSettingsMenu(offline, showHelp: () => invoked = true)
            .Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Help & shortcuts"));
        require(!offline.CanEdit && offlineHelp.IsEnabled, "Help remains available with the audio engine offline.");
        offlineHelp.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(invoked, "An offline help action reaches its callback without requesting an engine connection.");
        require(new WindowInteropHelper(window).Handle == IntPtr.Zero && PresentationSource.FromVisual(root) is null,
            "Reference verification creates no native window, focuses no control and performs no Computer Use.");
    }
}
