using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Input;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

public partial class MainWindow : Window
{
    // Include the former outer top inset so centering uses the full visible
    // header while its divider and the workspace keep their existing position.
    public const double TitleBarHeight = 36;
    public const string WebsiteUrl = "https://vinemix.app";
    public static GridLength TitleBarRowHeight => new(TitleBarHeight);
    private readonly MixerViewModel _mixer;
    private readonly Action<string> _openExternal;
    private readonly ContextMenu _settingsMenu;
    private readonly Border _helpOverlay;
    private readonly Border _searchOverlay;
    private IInputElement? _focusBeforeHelp;
    private IInputElement? _focusBeforeSearch;
    internal WorkspaceHelpView HelpView { get; }
    internal bool IsHelpOpen => _helpOverlay.Visibility == Visibility.Visible;
    internal NodeSearchView SearchView { get; }
    internal bool IsNodeSearchOpen => _searchOverlay.Visibility == Visibility.Visible;
    internal TextBlock SaveIndicator { get; } = new()
    {
        Name = "SaveIndicator", FontSize = 12, TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center
    };
    public GraphWorkspace Workspace { get; }
    public MainWindow(MixerViewModel mixer, Action<string>? openExternal = null)
    {
        // CaptionHeight starts below the native resize border, not at y=0.
        // Use both header rows, ending exactly at the canvas; only real buttons
        // opt into client hit testing. Passive text must never create drag holes.
        ConsoleTheme.Initialize();
        NativeCaption.Attach(this, TitleBarHeight + 1 + GraphWorkspace.ToolbarHeight - NativeCaption.ResizeBorderSize, resizable: true);
        _mixer = mixer; _openExternal = openExternal ?? App.OpenExternal; InitializeComponent(); DataContext = mixer;
        SaveIndicator.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        SaveIndicator.SetBinding(TextBlock.TextProperty, new Binding(nameof(MixerViewModel.SaveStatus)) { Converter = CompactSaveStatus.Instance });
        SaveIndicator.SetBinding(ToolTipProperty, new Binding(nameof(MixerViewModel.SaveStatus)));
        SaveIndicator.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(MixerViewModel.SaveStatus)));
        if (mixer.IsPreview) Title = "VineMix · UI Preview";
        var minimise = NativeCaption.WindowButton(this, close: false);
        minimise.Margin = new Thickness(0, 0, 6, 0);
        WindowActions.Children.Add(minimise);
        var maximise = NativeCaption.MaximiseButton(this);
        maximise.Margin = new Thickness(0, 0, 6, 0);
        WindowActions.Children.Add(maximise);
        WindowActions.Children.Add(NativeCaption.WindowButton(this, close: true,
            caption: mixer.IsPreview ? "Close UI preview" : "Close to tray — audio keeps running"));
        foreach (Button button in WindowActions.Children) { button.SetResourceReference(StyleProperty, "WorkspaceToolbarButton"); button.Margin = new Thickness(0); }
        Workspace = new GraphWorkspace(mixer);
        Workspace.SetFooterStatus(SaveIndicator);
        InitializeLayoutLock();
        _settingsMenu = CreateSettingsMenu(mixer, Workspace.SetMessage, OpenHelp, OpenNodeSearch, OpenHelpEntry, OpenAbout, OpenUpdates,
            quit: QuitFromSettingsAsync, showWhatsNew: () => OpenWhatsNew());
        SettingsButton.PreviewMouseDown += (_, args) => { if (!TryPrepareUpdateEditors()) args.Handled = true; };
        ConsoleGrid.Children.Add(Workspace);
        HelpView = new WorkspaceHelpView(CloseHelp) { MaxWidth = 960, MaxHeight = 680,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        _helpOverlay = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 0, 0, 0)),
            Padding = new Thickness(16, 12, 16, 12), Child = HelpView, Visibility = Visibility.Collapsed
        };
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(_helpOverlay, true);
        KeyboardNavigation.SetTabNavigation(_helpOverlay, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(_helpOverlay, KeyboardNavigationMode.Cycle);
        ConsoleGrid.Children.Add(_helpOverlay);
        SearchView = new NodeSearchView(ChooseSearchNode, CloseNodeSearch) { MaxWidth = 580, MaxHeight = 460,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        _searchOverlay = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 0, 0, 0)),
            Padding = new Thickness(24, 16, 24, 16), Child = SearchView, Visibility = Visibility.Collapsed
        };
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(_searchOverlay, true);
        KeyboardNavigation.SetTabNavigation(_searchOverlay, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(_searchOverlay, KeyboardNavigationMode.Cycle);
        ConsoleGrid.Children.Add(_searchOverlay);
        PreviewKeyDown += (_, args) =>
        {
            if (TryNodeSearchShortcut(args.Key, Keyboard.Modifiers, Keyboard.FocusedElement as DependencyObject)) { args.Handled = true; return; }
            if (IsNodeSearchOpen) return;
            if (TryHelpShortcut(args.Key, Keyboard.Modifiers, Keyboard.FocusedElement as DependencyObject)) { args.Handled = true; return; }
            if (IsHelpOpen) return;
            if (Workspace.TryHistoryShortcut(args.Key, Keyboard.Modifiers, Keyboard.FocusedElement as DependencyObject))
                args.Handled = true;
        };
        Closing += OnClosing; IsVisibleChanged += (_, _) => UpdateVisibility(); StateChanged += (_, _) => UpdateVisibility();
    }
    public void OpenSettings()
    {
        if (IsHelpOpen || IsNodeSearchOpen) return;
        _settingsMenu.PlacementTarget = SettingsButton;
        _settingsMenu.Placement = PlacementMode.Bottom;
        _settingsMenu.HorizontalOffset = SettingsButton.ActualWidth - _settingsMenu.Width;
        _settingsMenu.IsOpen = true;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void Website_Click(object sender, RoutedEventArgs e) => _openExternal(WebsiteUrl);
    private void OpenAbout()
    {
        _settingsMenu.IsOpen = false;
        var dialog = new Window { Owner = this, Title = "About VineMix", Width = 520, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = FontFamily, FontSize = 13, ShowInTaskbar = false };
        dialog.SetResourceReference(BackgroundProperty, "ConsoleWindowBrush");
        dialog.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        dialog.Content = CreateAboutContent(dialog.Close, showUpdates: () => { dialog.Close(); OpenUpdates(); });
        dialog.ShowDialog();
    }
    internal static FrameworkElement CreateAboutContent(Action closeAbout, Action<string>? copyVersion = null, Action? showUpdates = null)
    {
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "VineMix", FontSize = 24, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Local audio routing · Beta", Margin = new Thickness(0, 4, 0, 16) });
        var version = new TextBox { Text = ReleaseInfo.FullInformation, IsReadOnly = true, IsReadOnlyCaretVisible = true, TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 16) };
        version.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        AutomationProperties.SetName(version, "Full VineMix version and build information");
        content.Children.Add(version);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = new Button { Content = "Copy version information", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
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
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(12, 6, 12, 6) };
        close.Click += (_, _) => closeAbout();
        if (showUpdates is not null)
        {
            var updates = new Button { Content = "Check for updates", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            updates.Click += (_, _) => showUpdates(); actions.Children.Add(updates);
        }
        actions.Children.Add(copy); actions.Children.Add(close); content.Children.Add(actions);
        return content;
    }
    internal void OpenHelp()
    {
        if (IsNodeSearchOpen) return;
        if (IsHelpOpen) return;
        _focusBeforeHelp = Keyboard.FocusedElement;
        _settingsMenu.IsOpen = false;
        Workspace.CancelActiveGesture();
        Workspace.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        HelpView.BeginSession();
        _helpOverlay.Visibility = Visibility.Visible;
        // Detached verification must never focus or create a native window.
        if (PresentationSource.FromVisual(this) is not null) HelpView.CloseButton.Focus();
    }
    internal void OpenHelpEntry(string entryId)
    {
        OpenHelp();
        if (IsHelpOpen) HelpView.ShowEntry(entryId);
    }
    internal void CloseHelp()
    {
        if (!IsHelpOpen) return;
        HelpView.EndSession();
        _helpOverlay.Visibility = Visibility.Collapsed;
        Workspace.IsEnabled = true;
        SettingsButton.IsEnabled = true;
        if (PresentationSource.FromVisual(this) is not null)
        {
            if (!(_focusBeforeHelp is UIElement { IsVisible: true, IsEnabled: true } previous
                && PresentationSource.FromVisual(previous) is not null && previous.Focus())) Workspace.Focus();
        }
        _focusBeforeHelp = null;
    }
    internal bool TryHelpShortcut(Key key, ModifierKeys modifiers, DependencyObject? focused = null)
    {
        if (modifiers != ModifierKeys.None) return false;
        // Blurring a numeric entry discards its uncommitted draft. Let the
        // editor finish before F1 opens a reference that would take its focus.
        if (key == Key.F1 && !GraphWorkspace.IsTextEditor(focused)) { OpenHelp(); return true; }
        if (key == Key.Escape && IsHelpOpen)
        {
            if (HelpView.TryCancelInteraction()) return true;
            CloseHelp(); return true;
        }
        return false;
    }
    internal void OpenNodeSearch()
    {
        if (IsHelpOpen || IsNodeSearchOpen || !Workspace.CanOpenNodeSearch) return;
        _focusBeforeSearch = Keyboard.FocusedElement;
        _settingsMenu.IsOpen = false;
        SearchView.SearchBox.Text = "";
        SearchView.Apply(_mixer.GraphConfig);
        _mixer.GraphChanged += RefreshNodeSearch;
        Workspace.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        _searchOverlay.Visibility = Visibility.Visible;
        if (PresentationSource.FromVisual(this) is not null) SearchView.SearchBox.Focus();
    }
    private void RefreshNodeSearch(object? sender, EventArgs args) => SearchView.Apply(_mixer.GraphConfig);
    internal void CloseNodeSearch()
    {
        if (!IsNodeSearchOpen) return;
        _mixer.GraphChanged -= RefreshNodeSearch;
        _searchOverlay.Visibility = Visibility.Collapsed;
        Workspace.IsEnabled = true;
        SettingsButton.IsEnabled = true;
        if (PresentationSource.FromVisual(this) is not null)
        {
            if (!(_focusBeforeSearch is UIElement { IsVisible: true, IsEnabled: true } previous
                && PresentationSource.FromVisual(previous) is not null && previous.Focus())) Workspace.Focus();
        }
        _focusBeforeSearch = null;
    }
    private bool ChooseSearchNode(string id)
    {
        if (!Workspace.FindAndFrameNode(id)) { SearchView.Apply(_mixer.GraphConfig); return false; }
        CloseNodeSearch();
        if (PresentationSource.FromVisual(this) is not null) Workspace.Focus();
        return true;
    }
    internal bool TryNodeSearchShortcut(Key key, ModifierKeys modifiers, DependencyObject? focused = null)
    {
        if (key == Key.Escape && modifiers == ModifierKeys.None && IsNodeSearchOpen) { CloseNodeSearch(); return true; }
        if (key != Key.F || modifiers != ModifierKeys.Control || IsHelpOpen) return false;
        if (IsNodeSearchOpen) return true;
        if (GraphWorkspace.IsTextEditor(focused) || !Workspace.CanOpenNodeSearch) return false;
        OpenNodeSearch(); return IsNodeSearchOpen;
    }
    internal static ContextMenu CreateSettingsMenu(MixerViewModel mixer, Action<string, bool>? feedback = null,
        Action? showHelp = null, Action? findNode = null, Action<string>? showHelpEntry = null, Action? showAbout = null, Action? showUpdates = null,
        Func<System.Threading.Tasks.Task>? quit = null, Action? showWhatsNew = null)
    {
        var menu = new ContextMenu { Width = 380, MaxHeight = 560 };
        AutomationProperties.SetName(menu, "VineMix settings");
        var find = new MenuItem { Header = "Find node…", InputGestureText = "Ctrl+F",
            ToolTip = "Find a node by name, type, device or app." };
        find.Click += (_, _) => findNode?.Invoke();
        menu.Items.Add(find);
        var help = new MenuItem { Header = "Help & shortcuts", InputGestureText = "F1",
            ToolTip = "Keyboard shortcuts and silent control practice." };
        AutomationProperties.SetHelpText(help, "Open shortcuts and practice controls. Escape first cancels an active example, then closes Help.");
        help.Click += (_, _) => showHelp?.Invoke();
        menu.Items.Add(help);
        menu.Items.Add(new Separator());

        MenuItem Preference(string title, string property, string help, ItemCollection? items = null)
        {
            var item = new MenuItem { Header = title, IsCheckable = true, StaysOpenOnClick = true,
                ToolTip = NodeToolTip.Create(title, help) };
            item.SetBinding(MenuItem.IsCheckedProperty, new Binding(property) { Source = mixer, Mode = BindingMode.TwoWay });
            AutomationProperties.SetHelpText(item, help);
            (items ?? menu.Items).Add(item);
            return item;
        }
        var rightPan = Preference("Right-click drag to pan", nameof(MixerViewModel.RightClickDragToPan),
            "Right-drag empty canvas to pan; release without dragging for its menu.");
        rightPan.ToolTip = NodeToolTip.Create("Right-click drag to pan", "Enables right-button canvas panning");
        Preference("Reduce meter animation", nameof(MixerViewModel.ReducedAnimation), "Use less motion in audio meters.");
        const string visibilityHelp = "Choose which extra audio devices appear in the input and output lists. Your assigned device always remains visible. These preferences do not change Windows devices or audio routing.";
        var visibility = new MenuItem { Header = "Audio device visibility", ToolTip = NodeToolTip.Create("Audio device visibility", visibilityHelp) };
        AutomationProperties.SetHelpText(visibility, visibilityHelp);
        Preference("Show disabled devices", nameof(MixerViewModel.ShowDisabledAudioDevices),
            "Show disabled Windows devices. Enable them in Windows sound settings before using them. This only changes list visibility.", visibility.Items);
        Preference("Show disconnected or missing devices", nameof(MixerViewModel.ShowDisconnectedAudioDevices),
            "Show unplugged or missing devices. This does not reconnect devices or change audio routing.", visibility.Items);
        Preference("Show advanced inputs and outputs", nameof(MixerViewModel.ShowAdvancedAudioDevices),
            "Show line-level inputs, telephone endpoints, and S/PDIF or digital passthrough devices. Ordinary and streamed microphones, speakers, headphones and HDMI outputs remain visible; unclassified active devices also remain visible. Availability controls still apply.", visibility.Items);
        menu.Items.Add(visibility);
        var startup = Preference("Start VineMix when I sign in", nameof(MixerViewModel.StartAtLogin),
            "Automatically start the mixer when you sign in to Windows.");
        startup.SetBinding(IsEnabledProperty, new Binding(nameof(MixerViewModel.CanEdit)) { Source = mixer });
        menu.Items.Add(new Separator());
        var sound = new MenuItem { Header = "Windows sound settings", ToolTip = "Open the Windows audio device settings." };
        sound.Click += (_, _) => App.OpenExternal("ms-settings:sound"); menu.Items.Add(sound);
        var retry = new MenuItem { Header = "Retry assigned devices", ToolTip = "Reconnect the audio devices already assigned to your nodes." };
        retry.SetBinding(IsEnabledProperty, new Binding(nameof(MixerViewModel.CanEdit)) { Source = mixer });
        retry.Click += async (_, _) => await mixer.RetryAsync(); menu.Items.Add(retry);
        const string restartHelp = "Briefly interrupts audio, then reconnects your existing routes. Your workspace stays open.";
        var restart = new MenuItem { Header = "Restart audio engine",
            ToolTip = NodeToolTip.Create("Restart audio engine", restartHelp) };
        AutomationProperties.SetHelpText(restart, restartHelp);
        restart.SetBinding(IsEnabledProperty, new Binding(nameof(MixerViewModel.CanRestartEngine)) { Source = mixer });
        restart.Click += async (_, _) =>
        {
            if (!mixer.CanRestartEngine) return;
            feedback?.Invoke("Restarting audio engine… Audio will briefly stop.", false);
            var result = await mixer.RestartEngineAsync();
            feedback?.Invoke(result.Message, !result.Success);
        };
        menu.Items.Add(restart);
        menu.Items.Add(new Separator());
        var updates = new MenuItem { Header = "Check for updates", ToolTip = "Check GitHub for a newer verified VineMix release." };
        AutomationProperties.SetHelpText(updates, "Check for a newer version. Installation only starts after you choose Install and restart; audio briefly stops then.");
        updates.Click += (_, _) => showUpdates?.Invoke(); menu.Items.Add(updates);
        Preference("Automatically check for updates", nameof(MixerViewModel.AutomaticallyCheckForUpdates),
            "Check daily while VineMix is running, with automatic retries after failed checks. Turning this on checks shortly. Downloads and installation require your choice.");
        const string whatsNewHelp = "Read the bundled release highlights.";
        var whatsNew = new MenuItem { Header = "What's new?", ToolTip = whatsNewHelp };
        AutomationProperties.SetName(whatsNew, "What's new?");
        AutomationProperties.SetHelpText(whatsNew, whatsNewHelp);
        whatsNew.Click += (_, _) => { menu.IsOpen = false; showWhatsNew?.Invoke(); };
        menu.Items.Add(whatsNew);
        var about = new MenuItem { Header = "About VineMix", ToolTip = "Version and build information to include in a bug report." };
        about.Click += (_, _) => showAbout?.Invoke();
        menu.Items.Add(about);
        menu.Items.Add(new Separator());
        const string quitHelp = "Save your workspace and stop audio.";
        var quitItem = new MenuItem { Header = "Quit VineMix", ToolTip = NodeToolTip.Create("Quit VineMix", quitHelp) };
        quitItem.SetResourceReference(ForegroundProperty, "ConsoleMuteBrush");
        AutomationProperties.SetName(quitItem, "Quit VineMix");
        AutomationProperties.SetHelpText(quitItem, quitHelp);
        quitItem.Click += async (_, _) => { menu.IsOpen = false; if (quit is not null) await quit(); };
        menu.Items.Add(quitItem);
        Action<string>? openEntry = showHelpEntry;
        if (openEntry is null && showHelp is not null) openEntry = _ => showHelp();
        SettingsSearchView.Attach(menu, openEntry);
        MenuHoverHelp.Attach(menu);
        return menu;
    }
    private void UpdateVisibility() => _mixer.IsVisible = IsVisible && WindowState != WindowState.Minimized;
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_mixer.IsPreview) { Application.Current.Shutdown(); return; }
        if (((App)Application.Current).IsQuitting) return;
        args.Cancel = true; Hide();
    }

    private sealed class CompactSaveStatus : IValueConverter
    {
        public static readonly CompactSaveStatus Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value as string ?? "";
            if (status.StartsWith("Saved ·", StringComparison.Ordinal)) return "Saved";
            if (status.StartsWith("Preview ·", StringComparison.Ordinal)) return "Preview";
            return status;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
