using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

internal static class WorkspaceHelpVerification
{
    internal static async Task RunAsync(MainWindow window, MixerViewModel mixer, FrameworkElement root,
        Func<Task> layout, Action<bool, string> require, Action<string> render,
        Action<string, double>? renderAtDpi = null)
    {
        var workspace = window.Workspace;
        string liveState = MixerState(mixer);
        var view = window.HelpView;
        async Task SelectTopic(string topic)
        {
            view.SelectTopic(topic);
            await layout();
        }
        async Task CaptureAlignment(string filename)
        {
            foreach (string section in new[] { "align", "view" })
            {
                string title = HelpReference.Entries.First(entry => entry.SectionId == section).Section;
                Descendants<Button>(view).Single(button => AutomationProperties.GetName(button) == title)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await layout();
                if (section == "align") render(filename);
            }
        }
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
        string[] topics = ["controls", "dials", "buttons", "search", "equalizer", "feedback"];
        require(view.TopicIds.SequenceEqual(topics) && view.CurrentTopic == "controls" && view.CurrentLesson is null,
            "A fresh Help session opens Controls alongside the five remaining control-example topics.");
        require(view.SelectedReferenceSection == "view",
            "Controls initially expands canvas navigation rather than mixing every shortcut into one long reference.");
        render("help-shortcuts.png");
        window.HelpView.ScrollViewer.ScrollToEnd();
        await layout();
        render("help-shortcuts-equalizer.png");
        window.HelpView.ScrollViewer.ScrollToTop();
        await layout();
        await CaptureAlignment("help-controls-alignment.png");

        var zoom = HelpReference.Entries.Single(entry => entry.Id == "zoom");
        var resetZoom = HelpReference.Entries.Single(entry => entry.Id == "reset-zoom");
        var fitAll = HelpReference.Entries.Single(entry => entry.Id == "fit-all");
        require(zoom.Keys.Contains("wheel", StringComparison.OrdinalIgnoreCase)
            && resetZoom.Keys.Contains("Ctrl+0", StringComparison.OrdinalIgnoreCase)
            && resetZoom.Description.Contains("100%", StringComparison.Ordinal)
            && fitAll.Keys.Contains("Home", StringComparison.OrdinalIgnoreCase),
            "The shared control reference distinguishes wheel zoom, returning to 100 percent and fitting all nodes.");
        view.ShowEntry("zoom"); await layout();
        string visibleReference = VisibleText(view);
        require(view.CurrentTopic == "controls" && view.SelectedReferenceSection == "view"
            && view.HighlightedReferenceEntry == "zoom" && visibleReference.Contains(zoom.Description, StringComparison.Ordinal),
            "A Zoom deep link opens Controls, expands its navigation section and displays the matching guidance.");
        var alignment = HelpReference.Entries.First(entry => entry.SectionId == "align");
        view.ShowEntry(alignment.Id); await layout();
        visibleReference = VisibleText(view);
        require(view.SelectedReferenceSection == "align" && view.HighlightedReferenceEntry == alignment.Id
            && visibleReference.Contains(alignment.Description, StringComparison.Ordinal)
            && !visibleReference.Contains(zoom.Description, StringComparison.Ordinal),
            "Opening alignment displays its own guidance and closes the unrelated navigation instructions.");
        view.ShowEntry("reset-zoom"); await layout();
        require(view.SelectedReferenceSection == "view" && view.HighlightedReferenceEntry == "reset-zoom"
            && VisibleText(view).Contains(resetZoom.Description, StringComparison.Ordinal)
            && !VisibleText(view).Contains(zoom.Description, StringComparison.Ordinal),
            "A second reference link highlights Reset zoom independently of ordinary wheel zoom.");
        render("help-controls-reset-zoom.png");
        var zoomAction = Descendants<Button>(view).Single(button => AutomationProperties.GetName(button) == zoom.Title);
        zoomAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await layout();
        require(view.HighlightedReferenceEntry == "zoom"
            && VisibleText(view).Contains(zoom.Description, StringComparison.Ordinal)
            && !VisibleText(view).Contains(resetZoom.Description, StringComparison.Ordinal),
            "Choosing an actual reference row shows its own details and hides the previous row's explanation.");

        foreach (string topic in topics)
        {
            await SelectTopic(topic);
            require(view.CurrentTopic == topic && !workspace.IsEnabled && window.IsHelpOpen,
                $"The {topic} topic stays inside the modal Help surface with the real canvas disabled.");
            if (view.CurrentLesson is { } lesson)
            {
                require(!ReferenceEquals(lesson.View.DataContext, mixer)
                    && Descendants<FrameworkElement>(lesson.View).All(element => !ReferenceEquals(element.DataContext, mixer)),
                    $"The {topic} example never inherits the real mixer's data context.");
                require(lesson.View is { ActualWidth: > 0, ActualHeight: > 0 }
                    && lesson.View.ActualWidth <= view.ScrollViewer.ViewportWidth + 1,
                    $"The {topic} example is laid out inside the reading column without horizontal clipping.");
            }
            require(MixerState(mixer) == liveState, $"Opening the {topic} topic leaves the real mixer state unchanged.");
            render($"help-topic-{topic}.png");
        }

        await SelectTopic("dials");
        var dials = (HelpControlLessons.DialLesson)view.CurrentLesson!;
        require(dials.Controls.Count == 3 && dials.VariantSelector.Items.Count == 3,
            "Dial practice includes linear gain, logarithmic frequency and compact whole-number samples.");
        require(dials.ActiveControl.TryCommitText(Number(-6.25) + " dB") && dials.ActiveControl.Value == -6.25,
            "The actual practice dial applies precise numeric entry without rounding it to the drag step.");
        var valueEntry = Descendants<TextBox>(dials.ActiveControl).Single();
        valueEntry.Text = "invalid practice draft";
        require(dials.ActiveControl.HandleEntryKey(Key.Tab, ModifierKeys.Shift)
            && dials.ActiveControl.Value == -6.25 && valueEntry.Text == "invalid practice draft",
            "An invalid reverse-Tab draft stays editable and does not change the committed practice value.");
        require(window.TryHelpShortcut(Key.Escape, ModifierKeys.None) && window.IsHelpOpen
            && dials.ActiveControl.Value == -6.25 && valueEntry.Text != "invalid practice draft",
            "The first Escape cancels an invalid practice draft while keeping Help open.");
        dials.VariantSelector.SelectedIndex = 1;
        require(dials.ActiveControl.TryCommitText("2000 Hz") && Math.Abs(dials.ActiveControl.NormalizedValue - 2.0 / 3) < 1e-10,
            "Frequency practice uses the real logarithmic scale and accepts a unit suffix.");
        dials.VariantSelector.SelectedIndex = 2;
        require(!dials.ActiveControl.TryCommitText(Number(480.5)) && dials.ActiveControl.TryCommitText("960")
            && dials.ActiveControl.Value == 960,
            "Sample-count practice rejects fractional values and accepts whole numbers.");
        view.ResetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(dials.VariantSelector.SelectedIndex == 0 && dials.Controls.All(control => control.Value == control.DefaultValue),
            "Reset example restores every dial variant and the initial selector through the actual reset button.");
        dials.ActiveControl.BeginAdjustment(100);
        require(dials.TryCancel() && !dials.TryCancel(),
            "Cancelling a detached dial gesture ends it once and leaves no lingering practice interaction.");

        await SelectTopic("buttons");
        var buttons = (HelpControlLessons.ButtonsLesson)view.CurrentLesson!;
        foreach (var button in new[] { buttons.MuteButton, buttons.BypassButton, buttons.MonoButton })
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(buttons.Muted && buttons.Bypassed && buttons.Mono
            && Equals(buttons.MuteButton.Content, "Muted") && Equals(buttons.BypassButton.Content, "Bypass"),
            "Example state buttons keep their distinct labels and update only their local states.");
        buttons.ModeSelector.SelectedIndex = 1;
        require(buttons.Ratio.Visibility == Visibility.Visible && buttons.Amount.Visibility == Visibility.Collapsed
            && buttons.Ratio.TryCommitText(Number(6.5) + ":1"),
            "Dynamic mode exposes an editable ratio in place of the static duck amount.");
        buttons.ModeSelector.SelectedIndex = 0;
        require(buttons.Ratio.Value == 6.5 && buttons.Amount.Visibility == Visibility.Visible
            && buttons.DeviceSelector.Items.OfType<ComboBoxItem>().Any(item => !item.IsEnabled && item.ToolTip is not null),
            "Switching example modes preserves hidden values and the mock device selector explains its unavailable choice.");
        var preferences = buttons.ExampleMenu.Items.OfType<MenuItem>().Single(item => item.Items.Count > 0);
        var preference = preferences.Items.OfType<MenuItem>().First();
        preference.IsChecked = true;
        preference.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(preference.IsCheckable && preference.StaysOpenOnClick && preference.IsChecked
            && ReferenceEquals(MenuHoverHelp.Attach(buttons.ExampleMenu), MenuHoverHelp.Attach(buttons.ExampleMenu)),
            "Nested example preferences keep their local checkmark and share one reusable hover-help controller.");
        view.ResetExample();
        require(!buttons.Muted && !buttons.Bypassed && !buttons.Mono && !preference.IsChecked
            && buttons.ModeSelector.SelectedIndex == 0 && buttons.Ratio.Value == 4 && buttons.Amount.Value == 18,
            "Reset clears example button states, mode values and checked preferences together.");

        await SelectTopic("search");
        var search = (HelpControlLessons.SearchLesson)view.CurrentLesson!;
        search.SearchView.SearchBox.Text = "headset";
        require(search.SearchView.Results.Items.Count == 1 && search.SearchView.TryChooseSelected()
            && search.ChosenNodeId == "help-out" && !search.ChooseExample("live-node-not-in-fixture"),
            "The real search component finds a fictional device, chooses it locally and rejects IDs outside its fixture.");
        search.SearchView.SearchBox.Text = "there is no such example";
        require(search.SearchView.Results.Items.Count == 0 && !search.SearchView.GoButton.IsEnabled,
            "An empty search result disables navigation instead of choosing a real workspace node.");
        view.ResetExample();
        require(search.SearchView.SearchBox.Text.Length == 0 && search.SearchView.Results.Items.Count == search.Fixture.Nodes.Count
            && search.ChosenNodeId is null,
            "Reset clears the local search query and result choice while restoring all fictional results.");

        await SelectTopic("equalizer");
        var eq = (HelpAdvancedLessons.EqualizerLesson)view.CurrentLesson!;
        int selected = eq.Plot.SelectedBandIndex;
        require(eq.Frequency.TryCommitText("1375 Hz") && eq.Gain.TryCommitText(Number(3.25) + " dB")
            && eq.Width.TryCommitText(Number(1.75)),
            "The practice EQ accepts exact active-band frequency, gain and Q through its real dials.");
        var band = eq.Plot.SnapshotBands()[selected];
        require(band.FrequencyHz == 1375 && band.GainDb == 3.25 && band.Q == 1.75
            && eq.Plot.MoveBand(selected, 2400, -2.5) && eq.Frequency.Value == 2400 && eq.Gain.Value == -2.5,
            "Practice EQ dials and plotted band movement stay synchronized in both directions.");
        require(eq.Plot.SetBandShape(selected, "notch") && !eq.Gain.IsEnabled && !eq.Plot.HasSpectrum,
            "The Notch example disables gain and the silent EQ never fabricates a measured spectrum.");
        int oldBandCount = eq.Plot.BandCount;
        require(eq.Plot.AddBandAt(3200, 4) && eq.Plot.BandCount == oldBandCount + 1,
            "Adding a practice band changes only the isolated EQ fixture.");
        eq.Plot.BeginBandSelection(eq.Plot.PlotBounds.TopLeft + new Vector(5, 5));
        require(eq.TryCancel() && !eq.TryCancel(), "Escape cancellation ends a practice EQ selection box exactly once.");
        view.ResetExample();
        require(eq.Plot.BandCount == EqBand.Defaults().Count && eq.Plot.SelectedBandIndex == 2
            && eq.Gain.IsEnabled && Json(eq.Plot.SnapshotBands()) == Json(EqBand.Defaults()),
            "Reset restores the initial EQ bands, active selection and control availability.");

        await SelectTopic("feedback");
        var feedback = (HelpControlLessons.FeedbackLesson)view.CurrentLesson!;
        double staticLevel = feedback.Meter.Level;
        require(staticLevel > 0 && !feedback.Meter.IsAnimationRunning && feedback.Meter.HasPeakReadings
            && feedback.PeakDisplay.PeakReadingText.Contains("−18.0", StringComparison.Ordinal),
            "The real meter shows clearly illustrative stereo peaks without starting an animation or telemetry timer.");
        await layout();
        require(feedback.Meter.Level == staticLevel && !feedback.Meter.IsAnimationRunning,
            "Ordinary layout leaves the illustrative meter frozen instead of decaying as stale live telemetry.");
        feedback.StateSelector.SelectedIndex = 1;
        require(feedback.Meter.ClipLatched && feedback.Meter.ClipIndicatorOpacity == 1 && !feedback.Meter.IsAnimationRunning,
            "The clipping illustration holds its cap and independent overload reading without a running timer.");
        feedback.StateSelector.SelectedIndex = 2;
        require(feedback.Meter.Muted && feedback.Meter.Level == 0 && !feedback.Meter.HasPeakReadings,
            "The muted illustration has no fabricated live peaks.");
        view.ResetExample();
        require(feedback.StateSelector.SelectedIndex == 0 && feedback.Meter.Level > 0 && !feedback.Meter.ClipLatched,
            "Reset restores the healthy static feedback illustration.");

        require(MixerState(mixer) == liveState,
            "All remaining example edits, menu choices, searches, resets and reference links leave the real graph, preferences, queued saves and history unchanged.");
        double oldWidth = root.Width, oldHeight = root.Height;
        try
        {
            root.Width = 900; root.Height = 580; await layout();
            foreach (string topic in topics)
            {
                await SelectTopic(topic);
                require(view.ActualWidth <= root.ActualWidth - 32
                    && view.ActualHeight <= root.ActualHeight - MainWindow.TitleBarHeight
                    && view.ScrollViewer.ActualWidth > 0 && view.ScrollViewer.ViewportHeight > 0,
                    $"At the minimum app size the {topic} topic stays inside the workspace with a usable scrolling viewport.");
                if (view.CurrentLesson is { } lesson)
                    require(lesson.View.ActualWidth <= view.ScrollViewer.ViewportWidth + 1,
                        $"The minimum-size {topic} example fits the reading column horizontally.");
                if (view.CurrentLesson is HelpControlLessons.DialLesson smallDials)
                {
                    double valueBeforeWheel = smallDials.ActiveControl.Value;
                    var entry = Descendants<TextBox>(smallDials.ActiveControl).Single();
                    var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                        { RoutedEvent = Mouse.PreviewMouseWheelEvent };
                    entry.RaiseEvent(wheel); await layout();
                    require(wheel.Handled && smallDials.ActiveControl.Value == valueBeforeWheel
                        && (view.ScrollViewer.ScrollableHeight <= 0 || view.ScrollViewer.VerticalOffset > 0),
                        "Wheel input over an actual scalar value scrolls Help without being swallowed or changing the practice parameter.");
                    int selectedBeforeWheel = smallDials.VariantSelector.SelectedIndex;
                    smallDials.VariantSelector.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                        { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                    require(smallDials.VariantSelector.SelectedIndex == selectedBeforeWheel,
                        "Wheel input over a closed practice dropdown scrolls Help without silently changing its selected variant.");
                    view.ScrollViewer.ScrollToTop(); await layout();
                }
                render($"help-topic-{topic}-small.png");
            }
            await SelectTopic("controls");
            require(view.ScrollViewer.ScrollableHeight > 0,
                "At the minimum app size the shortcut reference scrolls instead of clipping its final instructions.");
            render("help-shortcuts-small.png");
            await CaptureAlignment("help-controls-alignment-small.png");
        }
        finally { root.Width = oldWidth; root.Height = oldHeight; await layout(); }
        if (renderAtDpi is not null)
        {
            foreach (string topic in new[] { "dials", "equalizer" })
            {
                await SelectTopic(topic);
                foreach (double dpi in new[] { 144.0, 192.0 }) renderAtDpi($"help-topic-{topic}-{dpi:0}dpi.png", dpi);
            }
        }
        bool? previousContrast = ConsoleTheme.PreviewHighContrast;
        try
        {
            ConsoleTheme.SetPreviewHighContrast(true);
            foreach (string topic in new[] { "controls", "buttons", "equalizer" })
            {
                await SelectTopic(topic);
                require(ReferenceEquals(view.FindResource("ConsoleTextBrush"), SystemColors.WindowTextBrush)
                    && ReferenceEquals(view.FindResource("ConsolePanelBrush"), SystemColors.WindowBrush),
                    $"The {topic} help topic uses system text and surface colours in the process-local high-contrast preview.");
                render($"help-topic-{topic}-high-contrast.png");
                if (topic == "controls") await CaptureAlignment("help-controls-alignment-high-contrast.png");
            }
        }
        finally { ConsoleTheme.SetPreviewHighContrast(previousContrast); }
        await SelectTopic("controls");
        require(window.TryHelpShortcut(Key.Escape, ModifierKeys.None) && !window.IsHelpOpen && workspace.IsEnabled,
            "Escape closes help and restores the canvas without cancelling a graph edit.");
        require(!window.TryHelpShortcut(Key.Escape, ModifierKeys.None),
            "When help is hidden Escape remains available to the graph and its controls.");
        window.OpenHelp();
        window.HelpView.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(!window.IsHelpOpen && workspace.IsEnabled,
            "The actual Close button also restores the workspace.");
        window.OpenHelp(); await layout();
        require(view.CurrentTopic == "controls" && view.CurrentLesson is null,
            "Reopening the reference leaves no hidden practice interaction active.");
        await SelectTopic("dials");
        var reopenedDials = (HelpControlLessons.DialLesson)view.CurrentLesson!;
        reopenedDials.ActiveControl.TryCommitText("-9 dB");
        window.CloseHelp(); window.OpenHelp(); await layout();
        require(view.CurrentTopic == "dials" && view.CurrentLesson is HelpControlLessons.DialLesson freshDials
            && !ReferenceEquals(freshDials, reopenedDials) && freshDials.Controls.All(control => control.Value == control.DefaultValue),
            "Reopening a practice topic creates fresh default controls rather than retaining edited values from the previous session.");
        window.CloseHelp();
        require(MixerState(mixer) == liveState,
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

    private static string Number(double value) => value.ToString("G", CultureInfo.CurrentCulture);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static string VisibleText(DependencyObject root) => string.Join("\n",
        Descendants<TextBlock>(root).Where(IsDisplayed).Select(text => text.Text));

    private static bool IsDisplayed(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static string MixerState(MixerViewModel mixer) => Json(new
    {
        Graph = mixer.GraphConfig, Preferences = mixer.SerializeUiPreferences(), mixer.StartAtLogin,
        mixer.ConfigurationPresentationCount, mixer.PendingEditCount, mixer.SaveStatus, mixer.ConnectionStatus,
        mixer.SettingsPath, mixer.Endpoints, mixer.Applications,
        History = mixer.EditHistory.Entries, mixer.EditHistory.CanRedo, mixer.EditHistory.CanUndo,
        mixer.EditHistory.HasGestureEdit
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
