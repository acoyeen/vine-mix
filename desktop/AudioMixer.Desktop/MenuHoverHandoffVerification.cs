using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AudioMixer.Desktop;

/// <summary>
/// Detached menu geometry and routed-callback regression with controlled pointer
/// samples and time. This does not validate physical input or native popups.
/// </summary>
internal static class MenuHoverHandoffVerification
{
    internal static async Task RunAsync(MainWindow window, MixerViewModel mixer, FrameworkElement root,
        Func<Task> layout, Action<bool, string> require)
    {
        string initialState = MixerState(mixer);
        IInputElement? initialFocus = Keyboard.FocusedElement;
        int clicked = 0;
        var settings = MainWindow.CreateSettingsMenu(mixer);
        var settingsSearch = SettingsSearchView.Get(settings)!;
        foreach (var row in Rows(settings)) row.Click += (_, _) => clicked++;
        using var settingsPointer = new PointerFixture(settings, require);
        await Arrange(settings);
        var upper = Row(settings, "Right-click drag to pan");
        var lower = Row(settings, "Reduce meter animation");
        settingsPointer.RequireGeometry(upper, lower, "Settings");
        ExerciseHandoffs(settingsPointer, upper, lower, "Settings", require);

        var deviceMenu = Row(settings, "Audio device visibility");
        deviceMenu.ApplyTemplate();
        var devicePopup = deviceMenu.Template.FindName("PART_Popup", deviceMenu) as Popup;
        require(devicePopup?.Child is FrameworkElement && !devicePopup.IsOpen,
            "The real device submenu content can be arranged without opening its popup.");
        if (devicePopup?.Child is FrameworkElement childSurface)
        {
            childSurface.Measure(new Size(380, 520));
            childSurface.Arrange(new Rect(0, 0, 380, childSurface.DesiredSize.Height));
            childSurface.UpdateLayout();
            await layout();
            var childUpper = Row(deviceMenu, "Show disabled devices");
            var childLower = Row(deviceMenu, "Show disconnected or missing devices");
            settingsPointer.RequireGeometry(childUpper, childLower, "Settings submenu");
            ExerciseHandoffs(settingsPointer, childUpper, childLower, "Settings submenu", require);
            require(!devicePopup.IsOpen && !deviceMenu.IsSubmenuOpen
                && PresentationSource.FromVisual(childSurface) is null,
                "Submenu handoff coverage leaves every real popup closed and detached.");
        }

        // The menu's own search field and separator are real non-row regions.
        settingsPointer.EnterAndShow(upper);
        settingsPointer.PointAt(settingsSearch.SearchBox);
        Move(settingsSearch.SearchBox);
        require(settingsPointer.Controller.Owner is null && !settingsPointer.Controller.IsPending
            && !settingsPointer.PresenterOpen,
            "Moving from a Settings row into the actual search field clears its tooltip ownership and pending timer.");
        settingsPointer.EnterAndShow(upper);
        var separator = settings.Items.OfType<Separator>()
            .First(item => item.Visibility == Visibility.Visible && item.ActualHeight > 0);
        settingsPointer.PointAt(separator);
        Move(separator);
        require(settingsPointer.Controller.Owner is null && !settingsPointer.Controller.IsPending
            && !settingsPointer.PresenterOpen,
            "The actual rendered separator is a no-help region rather than an extension of the adjacent row.");
        settingsPointer.EnterAndShow(lower);
        settingsPointer.PointAt(new Point(-4, -4)); Move(settings);
        require(settingsPointer.Controller.Owner is null && !settingsPointer.Controller.IsPending
            && !settingsPointer.PresenterOpen,
            "A geometric sample outside the menu clears help without transferring ownership to the nearest row.");

        settingsPointer.Reset();
        settingsPointer.PointAt(upper); Enter(upper);
        settingsSearch.SearchBox.Text = "restart";
        await Arrange(settings);
        settingsPointer.ShowDue();
        require(!ReferenceEquals(settingsPointer.Controller.Owner, upper)
            && !ReferenceEquals(settingsPointer.ShownOwner, upper),
            "Filtering a pending Settings row out of the menu cannot later present its obsolete tooltip.");
        settingsSearch.ResetQuery();
        await Arrange(settings);

        settingsPointer.Reset();
        settingsPointer.PointAt(upper); Enter(upper);
        settingsPointer.MenuActive = false;
        settings.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent, settings));
        settingsPointer.Controller.ShowPending();
        require(!settingsPointer.Controller.IsPending && settingsPointer.Controller.Owner is null,
            "An early queued callback cannot restart its timer after the menu closes.");
        settingsPointer.Now += 1000;
        settingsPointer.Controller.ShowPending();
        require(settingsPointer.Controller.Owner is null && !settingsPointer.Controller.IsPending
            && !settingsPointer.PresenterOpen,
            "The real menu Closed callback cancels pending help before an elapsed timer can present it.");
        settingsPointer.Reset();
        settingsPointer.EnterAndShow(lower);
        settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, settings));
        require(settingsPointer.Controller.Owner is null && !settingsPointer.Controller.IsPending
            && !settingsPointer.PresenterOpen,
            "The menu Unloaded callback releases presented help and stops its timer without a native popup lifecycle.");

        using var example = HelpControlLessons.Buttons();
        var exampleMenu = ((HelpControlLessons.ButtonsLesson)example).ExampleMenu;
        foreach (var row in Rows(exampleMenu)) row.Click += (_, _) => clicked++;
        using var examplePointer = new PointerFixture(exampleMenu, require);
        await Arrange(exampleMenu);
        var reset = Row(exampleMenu, "Reset buttons");
        var disabled = Row(exampleMenu, "Unavailable action");
        examplePointer.RequireGeometry(reset, disabled, "Help example");
        ExerciseHandoffs(examplePointer, reset, disabled, "Help example", require);
        require(!disabled.IsEnabled && ToolTipService.GetShowOnDisabled(disabled),
            "The real disabled example row explicitly allows explanatory help.");
        examplePointer.Reset();
        ToolTipService.SetShowOnDisabled(disabled, false);
        examplePointer.PointAt(disabled); Enter(disabled);
        require(examplePointer.Controller.Owner is null && !examplePointer.Controller.IsPending,
            "A disabled row that does not permit disabled help is not adopted as a tooltip owner.");
        ToolTipService.SetShowOnDisabled(disabled, true);
        examplePointer.EnterAndShow(disabled);
        require(ReferenceEquals(examplePointer.ShownOwner, disabled)
            && ReferenceEquals(examplePointer.ShownContent, disabled.ToolTip),
            "A disabled row that permits help presents its own explanatory content through the same geometry path.");

        var noHelp = new MenuItem { Header = "Action without help" };
        exampleMenu.Items.Add(noHelp);
        MenuHoverHelp.Attach(exampleMenu);
        await Arrange(exampleMenu);
        examplePointer.PointAt(noHelp); Leave(disabled); Enter(noHelp);
        require(examplePointer.Controller.Owner is null && !examplePointer.Controller.IsPending
            && !examplePointer.PresenterOpen,
            "A real arranged menu row without tooltip content clears the previous help instead of showing an empty tooltip.");

        // Reusing an attached controller must discover regenerated menu rows.
        var added = new MenuItem { Header = "Regenerated action", ToolTip = "Help for a regenerated menu action." };
        exampleMenu.Items.Add(added);
        require(ReferenceEquals(examplePointer.Controller, MenuHoverHelp.Attach(exampleMenu)),
            "Refreshing menu membership retains one controller instead of stacking another set of callbacks.");
        await Arrange(exampleMenu);
        examplePointer.Reset();
        examplePointer.PointAt(added); Enter(added);
        require(ReferenceEquals(examplePointer.Controller.Owner, added) && examplePointer.Controller.IsPending,
            "A row added after the first attachment receives the real enter callback when membership is refreshed.");
        examplePointer.ShowDue();
        require(ReferenceEquals(examplePointer.ShownOwner, added),
            "The regenerated row can present its own help rather than retaining the old row set.");
        exampleMenu.Items.Remove(added);
        MenuHoverHelp.Attach(exampleMenu);
        await Arrange(exampleMenu);
        examplePointer.PointAt(reset); Leave(added); Enter(reset);
        examplePointer.ShowDue();
        require(ReferenceEquals(examplePointer.ShownOwner, reset)
            && !ReferenceEquals(examplePointer.Controller.Owner, added),
            "Removing a regenerated row leaves remaining rows able to acquire help without a stale owner.");

        // The Add menu supplies real searched node-result rows and structured help.
        var addMenu = window.Workspace.CreateAddMenu(new Point(20, 20));
        var addSearch = addMenu.Items.OfType<MenuItem>()
            .SelectMany(item => HeaderTextBoxes(item.Header)).Single();
        addSearch.Text = "audio";
        foreach (var row in Rows(addMenu)) row.Click += (_, _) => clicked++;
        using var addPointer = new PointerFixture(addMenu, require);
        await Arrange(addMenu);
        var addRows = addMenu.Items.OfType<MenuItem>()
            .Where(item => item.Header is string && item.Visibility == Visibility.Visible && item.ToolTip is not null).Take(2).ToArray();
        require(addRows.Length == 2, "The real Add search provides two visible node rows with their actual help content.");
        if (addRows.Length == 2)
        {
            addPointer.RequireGeometry(addRows[0], addRows[1], "Add menu");
            ExerciseHandoffs(addPointer, addRows[0], addRows[1], "Add menu", require);
            addPointer.EnterAndShow(addRows[0]);
            addPointer.PointAt(addSearch); Move(addSearch);
            var searchRow = addMenu.Items.OfType<MenuItem>().First();
            require(ReferenceEquals(addPointer.Controller.Owner, searchRow) && addPointer.Controller.IsPending
                && addSearch.ToolTip is null && !ToolTipService.GetIsEnabled(searchRow),
                "The Add search field hands off to its own menu-scoped help, with no competing native child tooltip.");
            addPointer.ShowDue();
            require(ReferenceEquals(addPointer.ShownContent, searchRow.ToolTip),
                "The Add search field presents its own purpose text through the same shared presenter as result rows.");
            addPointer.PointAt(addRows[0]); Move(addRows[0]); addPointer.ShowDue();
            require(ReferenceEquals(addPointer.ShownOwner, addRows[0]),
                "Moving down from the Add search field restores the result row's own shared tooltip.");
        }

        require(clicked == 0 && MixerState(mixer) == initialState,
            "Menu handoff events and controlled timer callbacks invoke no commands and preserve graph, preferences, saves and edit history.");
        require(ReferenceEquals(Keyboard.FocusedElement, initialFocus)
            && new WindowInteropHelper(window).Handle == IntPtr.Zero
            && PresentationSource.FromVisual(root) is null,
            "Detached handoff verification leaves keyboard focus unchanged and creates no native window or physical input.");

        async Task Arrange(ContextMenu menu)
        {
            ArrangeMenu(menu);
            await layout();
            ArrangeMenu(menu);
            require(!menu.IsOpen && PresentationSource.FromVisual(menu) is null,
                "Menu geometry is measured without opening a popup or attaching a native presentation surface.");
        }
    }

    private static void ExerciseHandoffs(PointerFixture fixture, MenuItem upper, MenuItem lower,
        string name, Action<bool, string> require)
    {
        foreach (var (from, to, direction) in new[]
        {
            (upper, lower, "downward"), (lower, upper, "upward")
        })
        {
            fixture.Reset(); fixture.EnterAndShow(from);
            int shown = fixture.ShowCount;
            fixture.PointAt(to);
            Leave(from); Enter(to);
            require(ReferenceEquals(fixture.Controller.Owner, to) && fixture.Controller.IsPending,
                $"{name} {direction} leave/enter handoff schedules the geometrically hovered row without a MouseMove event.");
            fixture.ShowDue();
            require(fixture.ShowCount == shown + 1 && ReferenceEquals(fixture.ShownOwner, to)
                && ReferenceEquals(fixture.ShownContent, to.ToolTip),
                $"{name} {direction} handoff presents the new row's own help without requiring mouse jitter.");

            fixture.Reset(); fixture.EnterAndShow(from);
            fixture.PointAt(to); Enter(to);
            long due = fixture.Controller.PendingAt;
            Leave(from);
            require(ReferenceEquals(fixture.Controller.Owner, to) && fixture.Controller.IsPending
                && fixture.Controller.PendingAt == due,
                $"{name} {direction} late leave from the old row does not erase or postpone the new owner's pending help.");
            fixture.ShowDue();
            require(ReferenceEquals(fixture.ShownOwner, to),
                $"{name} {direction} enter-before-leave ordering still presents the new owner.");
        }

        fixture.Reset(); fixture.PointAt(upper); Enter(upper);
        long firstDue = fixture.Controller.PendingAt;
        fixture.Now += 10;
        Enter(upper); Move(upper);
        require(ReferenceEquals(fixture.Controller.Owner, upper) && fixture.Controller.PendingAt == firstDue,
            $"{name} repeated callbacks within the same row preserve the initial help deadline.");
        fixture.ShowDue();
        int stableShows = fixture.ShowCount;
        Move(upper);
        require(ReferenceEquals(fixture.ShownOwner, upper) && fixture.ShowCount == stableShows
            && !fixture.Controller.IsPending,
            $"{name} movement inside the current owner does not close or reschedule already presented help.");

        fixture.Reset(); fixture.PointAt(upper); Enter(upper);
        fixture.PointAt(lower);
        fixture.ShowDue();
        require(ReferenceEquals(fixture.Controller.Owner, lower)
            && (fixture.Controller.IsPending || fixture.PresenterOpen),
            $"{name} the actual timer callback retargets a changed geometric hit instead of dismissing it until another movement arrives.");
        if (fixture.Controller.IsPending) fixture.ShowDue();
        require(ReferenceEquals(fixture.ShownOwner, lower)
            && ReferenceEquals(fixture.ShownContent, lower.ToolTip),
            $"{name} a stale pending-owner sample hands off to the current row with no extra pointer event.");
        fixture.Reset();
    }

    private sealed class PointerFixture : IDisposable
    {
        private readonly ContextMenu _menu;
        private readonly Action<bool, string> _require;
        internal MenuHoverHelp Controller { get; }
        internal bool MenuActive { get; set; } = true;
        internal long Now { get; set; } = 1000;
        internal bool PresenterOpen { get; private set; }
        internal MenuItem? ShownOwner { get; private set; }
        internal object? ShownContent { get; private set; }
        internal int ShowCount { get; private set; }
        private (Visual Surface, Point Position)? _pointer;

        internal PointerFixture(ContextMenu menu, Action<bool, string> require)
        {
            _menu = menu; _require = require;
            var environment = new MenuHoverHelpEnvironment(
                () => MenuActive, () => _pointer, () => Now,
                row => MenuActive && row.ActualWidth > 0 && row.ActualHeight > 0 && Displayed(row),
                () => PresenterOpen,
                row => { ShownOwner = row; ShownContent = row.ToolTip; PresenterOpen = true; ShowCount++; },
                () => { PresenterOpen = false; ShownOwner = null; ShownContent = null; });
            Controller = MenuHoverHelp.AttachForVerification(menu, environment);
        }

        internal void PointAt(FrameworkElement element)
        {
            Visual surface = element;
            while (VisualTreeHelper.GetParent(surface) is Visual parent) surface = parent;
            _pointer = (surface, element.TransformToAncestor(surface)
                .Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2)));
        }
        internal void PointAt(Point position) => _pointer = (_menu, position);

        internal void RequireGeometry(MenuItem upper, MenuItem lower, string name)
        {
            PointAt(upper);
            _require(_pointer is { } first && ReferenceEquals(Controller.RowAt(first.Surface, first.Position), upper),
                $"{name} upper-row ownership is resolved by hit testing its real arranged geometry.");
            PointAt(lower);
            _require(_pointer is { } second && ReferenceEquals(Controller.RowAt(second.Surface, second.Position), lower),
                $"{name} lower-row ownership is resolved by hit testing its real arranged geometry.");
        }

        internal void EnterAndShow(MenuItem row)
        {
            PointAt(row); Enter(row);
            _require(ReferenceEquals(Controller.Owner, row) && Controller.IsPending,
                "The routed row-enter callback schedules the owner under the controlled geometric point.");
            ShowDue();
            _require(PresenterOpen && ReferenceEquals(ShownOwner, row) && !Controller.IsPending,
                "The shared pending callback presents help only through the recording presenter.");
        }

        internal void ShowDue()
        {
            Now = Math.Max(Now, Controller.PendingAt);
            Controller.ShowPending();
        }

        internal void Reset()
        {
            MenuActive = false;
            _menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent, _menu));
            _pointer = null;
            Now += 1000;
            MenuActive = true;
        }

        public void Dispose()
        {
            MenuActive = false;
            _pointer = null;
            _menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent, _menu));
            _require(!Controller.IsPending && !Controller.Popup.IsOpen
                && PresentationSource.FromVisual(_menu) is null
                && PresentationSource.FromVisual(Controller.Popup) is null,
                "The handoff fixture stops its timer and leaves both real menu and real tooltip detached and closed.");
        }
    }

    private static bool Displayed(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = current is Visual
            ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static void Enter(UIElement element) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        { RoutedEvent = Mouse.MouseEnterEvent });
    private static void Leave(UIElement element) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        { RoutedEvent = Mouse.MouseLeaveEvent });
    private static void Move(UIElement element) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        { RoutedEvent = Mouse.PreviewMouseMoveEvent });

    private static void ArrangeMenu(ContextMenu menu)
    {
        menu.Visibility = Visibility.Visible;
        menu.ApplyTemplate();
        menu.Height = double.NaN;
        menu.InvalidateMeasure(); menu.InvalidateArrange();
        double width = double.IsFinite(menu.Width) ? menu.Width : 340;
        menu.Measure(new Size(width, 560));
        menu.Height = Math.Max(80, Math.Min(560, menu.DesiredSize.Height));
        menu.Arrange(new Rect(0, 0, width, menu.Height));
        menu.UpdateLayout();
    }

    private static MenuItem Row(ItemsControl menu, string header)
        => menu.Items.OfType<MenuItem>().Single(row => Equals(row.Header, header));

    private static IEnumerable<MenuItem> Rows(ItemsControl menu)
    {
        foreach (var row in menu.Items.OfType<MenuItem>())
        {
            yield return row;
            foreach (var child in Rows(row)) yield return child;
        }
    }

    private static IEnumerable<TextBox> HeaderTextBoxes(object? header)
    {
        if (header is TextBox entry) yield return entry;
        if (header is Panel panel)
            foreach (UIElement child in panel.Children)
                foreach (var nested in HeaderTextBoxes(child)) yield return nested;
        if (header is ContentControl content)
            foreach (var nested in HeaderTextBoxes(content.Content)) yield return nested;
    }

    private static string MixerState(MixerViewModel mixer) => JsonSerializer.Serialize(new
    {
        Graph = mixer.GraphConfig, Preferences = mixer.SerializeUiPreferences(), mixer.StartAtLogin,
        mixer.ConfigurationPresentationCount, mixer.PendingEditCount, mixer.SaveStatus, mixer.Warning,
        History = mixer.EditHistory.Entries, mixer.EditHistory.CanUndo, mixer.EditHistory.CanRedo,
        mixer.EditHistory.HasGestureEdit
    }, Protocol.Json);
}
