using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>
/// Exercises the ordinary-control service through real detached Help controls,
/// dropdown choices, geometry and routed callbacks. Pointer samples and popup
/// presentation are controlled; physical input and native popups are not tested.
/// </summary>
internal static class OrdinaryHoverHelpVerification
{
    private static readonly string[] ViewEntries =
        ["zoom", "reset-zoom", "fit-all", "frame-selection", "pan", "right-pan"];

    internal static async Task RunAsync(MainWindow window, MixerViewModel mixer, FrameworkElement root,
        Func<Task> layout, Action<bool, string> require)
    {
        string baseline = MixerState(mixer);
        IInputElement? focus = Keyboard.FocusedElement;
        double width = root.Width, height = root.Height;
        var view = window.HelpView;
        string initialTopic = view.CurrentTopic;
        require(HoverHelp.IsRegistered,
            "Ordinary tooltip ownership is registered by application startup before a test environment is installed.");
        require(!window.IsHelpOpen && mixer.IsPreview,
            "Ordinary hover verification starts with closed Help and an in-memory preview mixer.");
        window.OpenHelp();
        try
        {
            // This is deliberately before UseVerificationEnvironment. Installing
            // the recorder must not supply the missing production registration
            // or conceal a reference button still owned by ToolTipService.
            foreach (string topic in view.TopicIds)
            {
                view.SelectTopic(topic);
                await layout();
                var owners = TooltipOwners(view).ToArray();
                require(owners.Length > 0, $"The actual {topic} Help surface supplies tooltip owners to verify.");
                foreach (var owner in owners) CheckLoadedOwnership(owner, topic, require);
            }
            view.SelectTopic("controls");
            await layout();
            var rows = ReferenceRows(view);
            foreach (var row in rows) CheckLoadedOwnership(row, "Controls / View & zoom", require);
            require(rows.Length == 6 && rows.All(row => !ToolTipService.GetIsEnabled(row)),
                "All six actual View & zoom buttons, including Zoom and Reset zoom, disable native help before the recording seam is installed.");

            // These are the real Static/Dynamic choice items, constructed by
            // GraphNodeView. The node is never inserted into any graph, and its
            // selection callbacks are never invoked.
            var ducking = new GraphNodeView(window.Workspace, GraphDefaults.NewNode("ducking", 0, 0));
            var mode = Descendants<ComboBox>(ducking)
                .Single(choice => AutomationProperties.GetName(choice) == "Ducking mode");
            var modeItems = mode.Items.OfType<ComboBoxItem>().ToArray();
            object? initialMode = mode.SelectedItem;
            require(modeItems.Length == 2 && modeItems.All(item => item.ToolTip is not null),
                "The real ducking selector supplies distinct Static and Dynamic choice help without a graph edit.");
            foreach (var item in modeItems) CheckLoadedOwnership(item, "Ducking dropdown", require);

            var meter = new StereoNodeMeter { Height = 140 };
            meter.FreezePracticeSnapshot();
            var peak = new MeterPeakDisplay(meter) { Height = 140 };
            var peakSurface = new Border { Width = 80, Height = 160, Background = Brushes.Transparent, Child = peak };
            peakSurface.Measure(new Size(80, 160));
            peakSurface.Arrange(new Rect(0, 0, 80, 160));
            peakSurface.UpdateLayout();
            CheckLoadedOwnership(peak, "Peak meter wrapper", require);
            CheckLoadedOwnership(meter, "Non-interactive inner meter", require);

            using var pointer = new PointerFixture(require);
            pointer.RequireGeometry(peak, "Peak meter wrapper");
            require(!meter.IsHitTestVisible && pointer.Hits(peak) && ToolTipTitle(peak) == "Highest observed peak",
                "Actual meter geometry skips its non-hit-testable inner Audio level surface and resolves the Highest observed peak wrapper.");
            pointer.EnterAndShow(peak);
            require(ReferenceEquals(pointer.ShownContent, peak.PeakToolTipContent)
                && !ReferenceEquals(pointer.ShownContent, meter.ToolTip),
                "The real peak wrapper presents peak guidance rather than the inner meter's unrelated level guidance.");
            pointer.Reset();
            root.Width = 900; root.Height = 580;
            await layout();
            view.ScrollViewer.ScrollToTop();
            await layout();
            foreach (var row in rows) pointer.RequireGeometry(row, "Controls / View & zoom");
            for (int i = 0; i < rows.Length - 1; i++)
                ExerciseHandoffs(pointer, rows[i], rows[i + 1],
                    $"Controls {ViewEntries[i]} / {ViewEntries[i + 1]}", require);
            require(view.HighlightedReferenceEntry is null && view.SelectedReferenceSection == "view",
                "Hovering every View control neither activates reference details nor changes the selected section.");

            // Preserve a root-space point while the actual ScrollViewer moves
            // Reset zoom into Zoom's previous position. No pointer callback is
            // sent after scrolling; the production scroll/layout handlers run.
            pointer.Reset();
            pointer.EnterAndShow(rows[0]);
            Point fixedPoint = pointer.Position;
            double distance = rows[1].TranslatePoint(new Point(), view.ScrollViewer).Y
                - rows[0].TranslatePoint(new Point(), view.ScrollViewer).Y;
            require(view.ScrollViewer.ScrollableHeight >= distance && distance > 0,
                "The minimum-size Help viewport can scroll a real reference-row height for stationary-pointer coverage.");
            view.ScrollViewer.ScrollToVerticalOffset(distance);
            await layout();
            require(pointer.Position == fixedPoint && pointer.Hits(rows[1]),
                "Scrolling places the actual Reset zoom button beneath the unchanged Zoom pointer sample.");
            require(ReferenceEquals(pointer.Session.Owner, rows[1]),
                "The production scroll/layout callbacks transfer ordinary help beneath a stationary pointer without another mouse event.");
            pointer.ShowDue();
            pointer.RequireShown(rows[1], "Stationary Help scrolling");

            // Section collapse and topic changes must release removed owners
            // through production lifecycle/layout hooks, not a direct reset.
            view.ScrollViewer.ScrollToTop(); await layout();
            pointer.Reset(); pointer.EnterAndShow(rows[0]);
            var viewHeader = SectionHeader(view, "view");
            viewHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, viewHeader));
            await layout();
            require(view.SelectedReferenceSection == "" && !ReferenceEquals(pointer.Session.Owner, rows[0])
                && !ReferenceEquals(pointer.ShownOwner, rows[0]),
                "Collapsing the actual View section releases its presented tooltip rather than retaining an invisible reference owner.");

            view.ShowEntry("zoom"); await layout();
            rows = ReferenceRows(view);
            foreach (var row in rows) CheckLoadedOwnership(row, "Rebuilt Controls", require);
            pointer.Reset(); pointer.EnterAndShow(rows[0]);
            var removed = rows[0];
            view.SelectTopic("dials"); await layout();
            require(!ReferenceEquals(pointer.Session.Owner, removed) && !ReferenceEquals(pointer.ShownOwner, removed),
                "Selecting a different real Help topic releases the reference button that was removed by the rebuild.");
            pointer.PointOutside(root); Enter(removed);
            require(pointer.Session.Owner is null && !pointer.Session.IsPending && !pointer.PresenterOpen,
                "A late enter callback from a removed reference button cannot reclaim ordinary help ownership.");

            view.ShowEntry("zoom"); await layout();
            rows = ReferenceRows(view);
            foreach (var row in rows) CheckLoadedOwnership(row, "Reopened reference rows", require);
            pointer.Reset(); pointer.EnterAndShow(rows[0]);
            var closingOwner = rows[0];
            window.CloseHelp(); await layout();
            require(!window.IsHelpOpen && !ReferenceEquals(pointer.Session.Owner, closingOwner)
                && !ReferenceEquals(pointer.ShownOwner, closingOwner),
                "Closing Help releases the old reference owner through the actual close and layout lifecycle.");
            window.OpenHelp(); await layout();
            rows = ReferenceRows(view);
            foreach (var row in rows) CheckLoadedOwnership(row, "Reopened Help", require);
            require(!ReferenceEquals(rows[0], closingOwner),
                "Reopening Help constructs fresh reference buttons instead of reusing removed tooltip owners.");
            pointer.Reset(); pointer.EnterAndShow(rows[0]);
            pointer.RequireShown(rows[0], "Reopened Help");

            // Arrange only the closed dropdown's real content. No IsOpen,
            // IsDropDownOpen, focus, capture or native presentation is used.
            pointer.Reset();
            var dropdownSurface = ArrangeClosedDropdown(mode, require);
            await layout();
            ArrangeDropdownSurface(dropdownSurface);
            foreach (var item in modeItems) pointer.RequireGeometry(item, "Ducking dropdown");
            ExerciseHandoffs(pointer, modeItems[0], modeItems[1], "Ducking Static / Dynamic choices", require);
            require(ReferenceEquals(mode.SelectedItem, initialMode) && !mode.IsDropDownOpen
                && mode.Template.FindName("PART_Popup", mode) is Popup { IsOpen: false },
                "Ducking choice handoffs do not select a mode or open the real dropdown popup.");

            pointer.Reset();
            view.SelectTopic("buttons"); await layout();
            var buttons = (HelpControlLessons.ButtonsLesson)view.CurrentLesson!;
            var unavailable = (ComboBoxItem)buttons.DeviceSelector.Items[2];
            CheckLoadedOwnership(unavailable, "Unavailable Help device choice", require);
            var deviceSurface = ArrangeClosedDropdown(buttons.DeviceSelector, require);
            await layout(); ArrangeDropdownSurface(deviceSurface);
            pointer.RequireGeometry(unavailable, "Unavailable Help device choice");
            require(!unavailable.IsEnabled && ToolTipService.GetShowOnDisabled(unavailable),
                "The actual unavailable device choice permits explanatory help while remaining disabled.");
            pointer.EnterAndShow(unavailable);
            pointer.RequireShown(unavailable, "Disabled dropdown choice");
            ToolTipService.SetShowOnDisabled(unavailable, false);
            Move(unavailable);
            require(pointer.Session.Owner is null && !pointer.Session.IsPending && !pointer.PresenterOpen,
                "The same disabled choice is rejected by the actual resolver when disabled help is disallowed.");
            ToolTipService.SetShowOnDisabled(unavailable, true);
            pointer.EnterAndShow(unavailable);
            require(buttons.DeviceSelector.SelectedIndex == 0 && !buttons.DeviceSelector.IsDropDownOpen,
                "Inspecting disabled choice help neither selects the unavailable device nor opens its real dropdown.");

            // An actual lesson reset replaces readout tooltip content while
            // retaining the same control. The shared popup's content binding
            // must follow it even though the recording presenter stays native-free.
            pointer.Reset();
            view.SelectTopic("feedback"); await layout();
            var feedback = (HelpControlLessons.FeedbackLesson)view.CurrentLesson!;
            feedback.StateSelector.SelectedIndex = 1; await layout();
            var status = TooltipOwners(feedback.View).Single(owner => ToolTipTitle(owner) == "Sample status");
            CheckLoadedOwnership(status, "Feedback status", require);
            pointer.EnterAndShow(status);
            var binding = BindingOperations.GetBinding(pointer.Session.Popup, ContentControl.ContentProperty);
            require(binding is { Mode: BindingMode.OneWay } && ReferenceEquals(binding.Source, status),
                "The actual shared tooltip binds its content to the current feedback owner even under the recording presenter.");
            object? clippingHelp = status.ToolTip;
            view.ResetButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, view.ResetButton));
            await layout();
            require(!ReferenceEquals(status.ToolTip, clippingHelp),
                "The actual Reset example action replaces the feedback control's clipping guidance with its restored-state guidance.");
            require(ReferenceEquals(pointer.Session.Popup.Content, status.ToolTip)
                && !pointer.Session.Popup.IsOpen,
                "The closed shared tooltip follows a live owner's replacement help content without opening a native popup.");

            pointer.PointOutside(root); Move(view);
            require(pointer.Session.Owner is null && !pointer.Session.IsPending && !pointer.PresenterOpen,
                "A point outside the actual Help surface clears ordinary ownership instead of extending a neighboring control's safe area.");
            require(MixerState(mixer) == baseline,
                "Ordinary Help and dropdown hover callbacks preserve the graph, preferences, saves and edit history without executing settings or node actions.");
        }
        finally
        {
            view.SelectTopic(initialTopic);
            window.CloseHelp();
            HoverHelp.RestoreRuntime();
            root.Width = width; root.Height = height;
            await layout();
        }
        require(MixerState(mixer) == baseline && ReferenceEquals(Keyboard.FocusedElement, focus)
            && new WindowInteropHelper(window).Handle == IntPtr.Zero
            && PresentationSource.FromVisual(root) is null
            && !HoverHelp.Session.Popup.IsOpen && PresentationSource.FromVisual(HoverHelp.Session.Popup) is null,
            "Ordinary hover verification restores runtime state, preserves focus and creates no native window, popup or physical input.");
    }

    private static void CheckLoadedOwnership(FrameworkElement owner, string context, Action<bool, string> require)
    {
        owner.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, owner));
        require(owner.ToolTip is not null && !ToolTipService.GetIsEnabled(owner),
            $"The production Loaded handler owns native tooltip suppression for {context}: {OwnerName(owner)}.");
    }

    private static Button[] ReferenceRows(WorkspaceHelpView view) => ViewEntries.Select(id =>
    {
        string title = HelpReference.Entries.Single(entry => entry.Id == id).Title;
        return Descendants<Button>(view).Single(button => AutomationProperties.GetName(button) == title);
    }).ToArray();

    private static Button SectionHeader(WorkspaceHelpView view, string sectionId)
    {
        string title = HelpReference.Entries.First(entry => entry.SectionId == sectionId).Section;
        return Descendants<Button>(view).Single(button => AutomationProperties.GetName(button) == title);
    }

    private static IEnumerable<FrameworkElement> TooltipOwners(FrameworkElement root)
    {
        var elements = Descendants<FrameworkElement>(root).Prepend(root).ToArray();
        return elements.Concat(elements.OfType<ComboBox>().SelectMany(choice => choice.Items.OfType<FrameworkElement>()))
            .Where(element => element.ToolTip is not null and not ToolTip).Distinct();
    }

    private static string ToolTipTitle(FrameworkElement owner) => owner.ToolTip switch
    {
        string text => text,
        DependencyObject content => Descendants<TextBlock>(content).FirstOrDefault()?.Text ?? "",
        _ => ""
    };

    private static string OwnerName(FrameworkElement owner)
    {
        string name = AutomationProperties.GetName(owner);
        return name.Length > 0 ? name : ToolTipTitle(owner);
    }

    private static FrameworkElement ArrangeClosedDropdown(ComboBox choice, Action<bool, string> require)
    {
        choice.ApplyTemplate();
        var popup = choice.Template.FindName("PART_Popup", choice) as Popup;
        require(popup is { IsOpen: false, Child: FrameworkElement } && !choice.IsDropDownOpen,
            "The real dropdown exposes closed, detached content for geometry verification.");
        var surface = (FrameworkElement)popup!.Child;
        ArrangeDropdownSurface(surface);
        require(PresentationSource.FromVisual(surface) is null && !popup.IsOpen,
            "Measuring the real dropdown content does not create a native presentation source.");
        return surface;
    }

    private static void ArrangeDropdownSurface(FrameworkElement surface)
    {
        surface.Measure(new Size(300, 300));
        surface.Arrange(new Rect(0, 0, Math.Max(260, surface.DesiredSize.Width), Math.Max(60, surface.DesiredSize.Height)));
        surface.UpdateLayout();
    }

    private static void ExerciseHandoffs(PointerFixture pointer, FrameworkElement upper, FrameworkElement lower,
        string context, Action<bool, string> require)
    {
        foreach (var (from, to, direction) in new[] { (upper, lower, "downward"), (lower, upper, "upward") })
        {
            pointer.Reset(); pointer.EnterAndShow(from);
            pointer.PointAt(to); Leave(from); Enter(to);
            require(ReferenceEquals(pointer.Session.Owner, to)
                && (pointer.Session.IsPending || ReferenceEquals(pointer.ShownOwner, to)),
                $"{context}: {direction} enter/leave callbacks transfer ordinary help without a MouseMove event.");
            pointer.ShowDue(); pointer.RequireShown(to, $"{context}: {direction} handoff");

            pointer.Reset(); pointer.EnterAndShow(from);
            pointer.PointAt(to); Enter(to);
            long due = pointer.Session.PendingAt;
            Leave(from);
            require(ReferenceEquals(pointer.Session.Owner, to) && pointer.Session.PendingAt == due,
                $"{context}: a late {direction} leave from the old owner neither erases nor postpones the new owner.");
            pointer.ShowDue(); pointer.RequireShown(to, $"{context}: {direction} late leave");
        }

        pointer.Reset(); pointer.PointAt(upper); Enter(upper);
        long initialDue = pointer.Session.PendingAt;
        pointer.Now += 10; Enter(upper); Move(upper);
        require(ReferenceEquals(pointer.Session.Owner, upper) && pointer.Session.PendingAt == initialDue,
            $"{context}: repeated callbacks inside the same ordinary control preserve the original deadline.");
        pointer.ShowDue();
        int shows = pointer.ShowCount;
        Move(upper);
        require(ReferenceEquals(pointer.ShownOwner, upper) && !pointer.Session.IsPending && pointer.ShowCount == shows,
            $"{context}: movement within the shown owner does not close or restart its help.");

        pointer.Reset(); pointer.PointAt(upper); Enter(upper);
        pointer.PointAt(lower); pointer.ShowDue();
        require(ReferenceEquals(pointer.Session.Owner, lower),
            $"{context}: the actual pending callback retargets the changed geometric owner without another pointer event.");
        pointer.ShowDue(); pointer.RequireShown(lower, $"{context}: stale pending owner");
    }

    private sealed class PointerFixture : IDisposable
    {
        private readonly Action<bool, string> _require;
        private Visual? _surface;
        private bool _active = true;
        internal HoverHelpSession Session => HoverHelp.Session;
        internal Point Position { get; private set; }
        internal long Now { get; set; } = 1000;
        internal bool PresenterOpen { get; private set; }
        internal FrameworkElement? ShownOwner { get; private set; }
        internal object? ShownContent { get; private set; }
        internal int ShowCount { get; private set; }

        internal PointerFixture(Action<bool, string> require)
        {
            _require = require;
            HoverHelp.UseVerificationEnvironment(new HoverHelpEnvironment(
                () => _active, OwnerAtPoint, () => Now,
                owner => _active && DisplayedAtPoint(owner), () => PresenterOpen,
                owner => { ShownOwner = owner; ShownContent = owner.ToolTip; PresenterOpen = true; ShowCount++; },
                () => { PresenterOpen = false; ShownOwner = null; ShownContent = null; }));
        }

        private FrameworkElement? OwnerAtPoint() => _surface is null ? null : HoverHelp.ResolveOwner(_surface, Position);

        internal void PointAt(FrameworkElement element)
        {
            Visual surface = element;
            while (VisualTreeHelper.GetParent(surface) is Visual parent) surface = parent;
            _surface = surface;
            Position = element.TransformToAncestor(surface)
                .Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        }

        internal void PointOutside(Visual surface) { _surface = surface; Position = new Point(-8, -8); }
        internal bool Hits(FrameworkElement element) => ReferenceEquals(OwnerAtPoint(), element);

        internal void RequireGeometry(FrameworkElement element, string context)
        {
            PointAt(element);
            _require(element.ActualWidth > 0 && element.ActualHeight > 0
                && DisplayedAtPoint(element) && Hits(element),
                $"{context}: the actual arranged {OwnerName(element)} control is hit at its visible center.");
        }

        internal void EnterAndShow(FrameworkElement element)
        {
            PointAt(element); Enter(element);
            _require(ReferenceEquals(Session.Owner, element) && (Session.IsPending || PresenterOpen),
                "The installed ordinary-control enter callback adopts the owner at the real geometric point.");
            ShowDue(); RequireShown(element, "Ordinary control enter");
        }

        internal void ShowDue()
        {
            if (!Session.IsPending) return;
            Now = Math.Max(Now, Session.PendingAt);
            Session.ShowPending();
        }

        internal void RequireShown(FrameworkElement owner, string context)
            => _require(PresenterOpen && ReferenceEquals(ShownOwner, owner)
                && ReferenceEquals(ShownContent, owner.ToolTip) && !Session.IsPending && !Session.Popup.IsOpen,
                $"{context}: the recording presenter receives this control's own help while the real tooltip remains closed.");

        internal void Reset()
        {
            Session.Dismiss(resetQuick: true);
            _surface = null; Now += 1000;
        }

        private bool DisplayedAtPoint(FrameworkElement element)
        {
            if (_surface is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
            for (DependencyObject? current = element; current is not null; current = current is Visual
                ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            {
                if (current is not UIElement control) continue;
                if (control.Visibility != Visibility.Visible) return false;
                if (control is not Visual visual) continue;
                Point local = _surface.TransformToDescendant(visual).Transform(Position);
                if ((ReferenceEquals(control, element) || control.ClipToBounds)
                    && !new Rect(control.RenderSize).Contains(local)) return false;
                if (VisualTreeHelper.GetClip(visual) is { } clip && !clip.FillContains(local)) return false;
            }
            return true;
        }

        public void Dispose()
        {
            _active = false; _surface = null;
            HoverHelp.RestoreRuntime();
            _require(!Session.IsPending && Session.Owner is null && !Session.Popup.IsOpen
                && PresentationSource.FromVisual(Session.Popup) is null,
                "The ordinary-control recording fixture releases its timer, owner and content without a native tooltip.");
        }
    }

    private static void Enter(UIElement element) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        { RoutedEvent = Mouse.MouseEnterEvent });
    private static void Leave(UIElement element) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        { RoutedEvent = Mouse.MouseLeaveEvent });
    private static void Move(UIElement element) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        { RoutedEvent = Mouse.PreviewMouseMoveEvent });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static string MixerState(MixerViewModel mixer) => JsonSerializer.Serialize(new
    {
        Graph = mixer.GraphConfig, Preferences = mixer.SerializeUiPreferences(), mixer.StartAtLogin,
        mixer.ConfigurationPresentationCount, mixer.PendingEditCount, mixer.SaveStatus, mixer.Warning,
        History = mixer.EditHistory.Entries, mixer.EditHistory.CanUndo, mixer.EditHistory.CanRedo,
        mixer.EditHistory.HasGestureEdit
    }, Protocol.Json);
}
