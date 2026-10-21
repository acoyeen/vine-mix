using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace VineMix.Desktop;

/// <summary>Real Settings filtering and Help links, entirely detached from native input and popups.</summary>
internal static class SettingsSearchVerification
{
    internal static async Task RunAsync(MainWindow window, MixerViewModel mixer, FrameworkElement root,
        Func<Task> layout, Action<bool, string> require, Action<ContextMenu, string>? render = null)
    {
        string initialState = MixerState(mixer);
        IInputElement? initialFocus = Keyboard.FocusedElement;
        int settingsClicks = 0, helpLinks = 0;
        string? chosenEntry = null;
        var menu = MainWindow.CreateSettingsMenu(mixer, showHelpEntry: id =>
        {
            helpLinks++; chosenEntry = id;
            window.OpenHelpEntry(id);
        });
        var search = SettingsSearchView.Get(menu);
        require(search is not null && ReferenceEquals(search, SettingsSearchView.Attach(menu)),
            "Settings owns one reusable search component instead of attaching duplicate controls or handlers.");
        if (search is null) return;
        var originalRows = Rows(menu).Where(row => row.Header is string).ToArray();
        var originalSeparators = menu.Items.OfType<Separator>()
            .Where(separator => separator.Visibility == Visibility.Visible).ToArray();
        foreach (var row in originalRows) row.Click += (_, _) => settingsClicks++;
        try
        {
            require(search.SearchBox.Text.Length == 0 && search.Matches.Count == 0
                && originalRows.All(row => row.Visibility == Visibility.Visible),
                "An empty Settings query leaves the normal menu available without an unrelated list of help results.");

            search.SearchBox.Text = "Zoom";
            await layout();
            var zoom = search.Matches.FirstOrDefault(entry => entry.Id == "zoom");
            var reset = search.Matches.FirstOrDefault(entry => entry.Id == "reset-zoom");
            require(zoom is not null && reset is not null
                && zoom.Keys.Contains("wheel", StringComparison.OrdinalIgnoreCase)
                && reset.Keys.Contains("Ctrl+0", StringComparison.OrdinalIgnoreCase)
                && reset.Description.Contains("100%", StringComparison.Ordinal),
                "Searching Zoom shows the actual wheel gesture and the separate shortcut for returning to 100 percent.");
            require(search.Results.Items.Count == search.Matches.Count && helpLinks == 0 && settingsClicks == 0,
                "Typing a control query displays instructions without choosing a Help link or running a settings command.");
            render?.Invoke(menu, "settings-search-zoom.png");

            search.SearchBox.Text = "  ReSeT   ZOOM  ";
            require(search.Matches.Any(entry => entry.Id == "reset-zoom"),
                "Control search ignores letter case and extra whitespace while matching multiple words.");
            search.SearchBox.Text = "mouse wheel";
            require(search.Matches.Any(entry => entry.Id == "zoom"),
                "The mouse-wheel alias finds canvas zoom even without using its title.");
            search.SearchBox.Text = "fit all";
            require(search.Matches.Any(entry => entry.Id == "fit-all"),
                "A multiword framing query finds fitting every node rather than resetting the zoom percentage.");
            search.SearchBox.Text = "startup";
            require(search.MatchingSettings.Any(row => Header(row).Contains("sign in", StringComparison.OrdinalIgnoreCase)),
                "A familiar startup alias finds the sign-in setting without requiring its exact menu wording.");
            search.SearchBox.Text = "show disconnected";
            require(originalRows.Any(row => Header(row).Contains("Show disconnected", StringComparison.OrdinalIgnoreCase)
                    && row.Visibility == Visibility.Visible)
                && originalRows.Any(row => Header(row) == "Audio device visibility" && row.Visibility == Visibility.Visible),
                "A matching nested device setting remains reachable through its visible parent menu.");
            search.SearchBox.Text = "devices";
            require(search.Matches.Count > 0 && search.MatchingSettings.Count > 0
                && !search.TrySearchKey(Key.Tab, ModifierKeys.None)
                && !search.TrySettingKey(Key.Tab, ModifierKeys.Shift, originalRows[0])
                && ReferenceEquals(Keyboard.FocusedElement, initialFocus),
                "Mixed results keep explicit Tab routes between settings and help, while detached checks never request native focus.");

            search.SearchBox.Text = "restart";
            require(search.MatchingSettings.Any(row => Header(row).Contains("Restart audio engine", StringComparison.OrdinalIgnoreCase))
                && settingsClicks == 0 && helpLinks == 0 && MixerState(mixer) == initialState,
                "Searching an engine action filters to that setting without activating it or changing the mixer.");
            await layout();
            render?.Invoke(menu, "settings-search-setting.png");

            search.SearchBox.Text = "no-such-setting-or-control-7811";
            require(search.Matches.Count == 0 && search.MatchingSettings.Count == 0
                && !search.TryChooseSelected() && helpLinks == 0 && settingsClicks == 0,
                "A no-match query has no selectable result and cannot run a setting or open unrelated Help.");
            require(MenuText(menu).Any(text => text.Contains("No matching settings or controls", StringComparison.Ordinal)),
                "No matches produces readable guidance instead of an empty, unexplained Settings surface.");
            await layout();
            render?.Invoke(menu, "settings-search-no-results.png");
            if (render is not null)
            {
                var message = VisualChildren(menu).OfType<TextBlock>()
                    .FirstOrDefault(text => text.Text.StartsWith("No matching settings or controls", StringComparison.Ordinal));
                require(message is not null && message.ActualWidth > 0 && message.ActualHeight > 0
                    && message.TransformToAncestor(menu).TransformBounds(new Rect(message.RenderSize))
                        .IntersectsWith(new Rect(menu.RenderSize)) && HasVisibleArea(message, menu),
                    "The no-match guidance occupies visible space in the rendered menu, beyond existing only in its logical tree.");
            }
            require(search.TrySearchKey(Key.Escape, ModifierKeys.None) && search.SearchBox.Text.Length == 0,
                "Escape clears a nonempty query before the menu's own close behavior takes over.");
            require(search.Matches.Count == 0 && originalRows.All(row => row.Visibility == Visibility.Visible)
                && originalSeparators.All(separator => separator.Visibility == Visibility.Visible),
                "Clearing the actual search field restores the original menu rows and separators.");
            require(!search.TrySearchKey(Key.Escape, ModifierKeys.None),
                "Escape with an empty query remains available to close Settings.");
            search.SearchBox.Text = " \t \r\n ";
            require(search.Matches.Count == 0 && originalRows.All(row => row.Visibility == Visibility.Visible),
                "Whitespace alone behaves as an empty query rather than hiding the Settings menu.");
            search.SearchBox.Clear();
            search.SearchBox.Text = "restart";
            search.ResetQuery();
            require(search.SearchBox.Text.Length == 0 && search.Matches.Count == 0
                && originalRows.All(row => row.Visibility == Visibility.Visible),
                "The same reset used by popup open and close restores a fresh query without opening or focusing a popup.");

            search.SearchBox.Text = "zoom";
            require(search.Results.Items.Count >= 2 && search.Results.SelectedItem is HelpReferenceEntry,
                "Zoom provides keyboard-selectable results for both the wheel gesture and its reset action.");
            int selectedBefore = search.Results.SelectedIndex;
            require(!search.TrySearchKey(Key.Down, ModifierKeys.Control)
                && search.Results.SelectedIndex == selectedBefore,
                "Modified navigation keys do not silently change the chosen instruction.");
            require(search.TrySearchKey(Key.Down, ModifierKeys.None)
                && search.Results.SelectedIndex > selectedBefore && helpLinks == 0,
                "Down moves to another result without running it or moving native focus.");
            var selected = search.Results.SelectedItem as HelpReferenceEntry;
            require(selected is not null && search.TrySearchKey(Key.Enter, ModifierKeys.None)
                && helpLinks == 1 && chosenEntry == selected.Id && !menu.IsOpen
                && window.IsHelpOpen && window.HelpView.CurrentTopic == "controls"
                && window.HelpView.SelectedReferenceSection == selected.SectionId
                && window.HelpView.HighlightedReferenceEntry == selected.Id,
                "Enter follows the selected result through the real Settings callback to its expanded, highlighted Help instruction.");
            await layout();
            require(settingsClicks == 0 && MixerState(mixer) == initialState,
                "Following a control instruction changes only Help presentation, preserving graph, history, preferences and queued work.");
            window.CloseHelp();

            using var offline = new MixerViewModel(preview: true);
            string offlineState = MixerState(offline);
            string? offlineChoice = null;
            var offlineMenu = MainWindow.CreateSettingsMenu(offline, showHelpEntry: id => offlineChoice = id);
            var offlineSearch = SettingsSearchView.Get(offlineMenu);
            require(offlineSearch is not null && !offline.CanEdit,
                "Settings search exists before an audio engine or editable graph is available.");
            if (offlineSearch is not null)
            {
                offlineSearch.SearchBox.Text = "zoom";
                require(offlineSearch.Matches.Any(entry => entry.Id == "zoom")
                    && offlineSearch.TryChooseSelected() && offlineChoice is not null
                    && MixerState(offline) == offlineState,
                    "An offline search still opens a control instruction without requesting an engine connection or changing settings.");
            }
            require(MixerState(mixer) == initialState
                && ReferenceEquals(Keyboard.FocusedElement, initialFocus)
                && new WindowInteropHelper(window).Handle == IntPtr.Zero
                && PresentationSource.FromVisual(root) is null && PresentationSource.FromVisual(menu) is null,
                "Settings-search verification remains detached, preserves focus and mixer state, and creates no native window or popup.");
        }
        finally
        {
            menu.IsOpen = false;
            search.SearchBox.Clear();
            window.CloseHelp();
            await layout();
        }
    }

    private static string Header(MenuItem item) => item.Header?.ToString() ?? "";

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in VisualChildren(child)) yield return nested;
        }
    }

    private static bool HasVisibleArea(FrameworkElement message, FrameworkElement root)
    {
        var bounds = new Rect(message.RenderSize);
        for (Visual current = message; !ReferenceEquals(current, root);)
        {
            if (VisualTreeHelper.GetClip(current) is { } clip) bounds.Intersect(clip.Bounds);
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return false;
            if (VisualTreeHelper.GetParent(current) is not Visual parent) return false;
            bounds = current.TransformToAncestor(parent).TransformBounds(bounds);
            current = parent;
        }
        bounds.Intersect(new Rect(root.RenderSize));
        return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0;
    }

    private static IEnumerable<string> MenuText(DependencyObject element)
    {
        if (element is UIElement { Visibility: not Visibility.Visible }) yield break;
        if (element is TextBlock text) yield return text.Text;
        if (element is HeaderedItemsControl { Header: DependencyObject header })
            foreach (string value in MenuText(header)) yield return value;
        if (element is ItemsControl items)
            foreach (var child in items.Items.OfType<DependencyObject>())
                foreach (string value in MenuText(child)) yield return value;
        if (element is Panel panel)
            foreach (UIElement child in panel.Children)
                foreach (string value in MenuText(child)) yield return value;
        if (element is ContentControl { Content: DependencyObject content })
            foreach (string value in MenuText(content)) yield return value;
    }

    private static IEnumerable<MenuItem> Rows(ItemsControl parent)
    {
        foreach (var item in parent.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in Rows(item)) yield return child;
        }
    }

    private static string MixerState(MixerViewModel mixer) => JsonSerializer.Serialize(new
    {
        Graph = mixer.GraphConfig, Preferences = mixer.SerializeUiPreferences(), mixer.StartAtLogin,
        mixer.ConfigurationPresentationCount, mixer.PendingEditCount, mixer.SaveStatus, mixer.ConnectionStatus,
        mixer.Warning, mixer.IsRestartingEngine, mixer.SettingsPath, mixer.Endpoints, mixer.Applications,
        History = mixer.EditHistory.Entries, mixer.EditHistory.CanRedo, mixer.EditHistory.CanUndo,
        mixer.EditHistory.HasGestureEdit
    }, Protocol.Json);
}
