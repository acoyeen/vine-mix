using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace VineMix.Desktop;

/// <summary>
/// Shown-preview regression for Settings help popup ownership and cleanup.
/// Uses real WPF focus and popup lifecycles without invoking menu actions or
/// injecting native input. It belongs only in the --verify-ui harness.
/// </summary>
internal static class MenuHoverHelpVerification
{
    internal static async Task RunAsync(MainWindow window, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require)
    {
        require(mixer.IsPreview && window.IsVisible && new WindowInteropHelper(window).Handle != IntPtr.Zero,
            "Settings-help lifecycle verification uses the shown preview window and an in-memory mixer.");
        string graph = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        var preferences = Preferences(mixer);
        long dispatches = mixer.ConfigurationPresentationCount;
        int history = mixer.EditHistory.Entries.Count;
        string saveStatus = mixer.SaveStatus;
        IInputElement? previousFocus = Keyboard.FocusedElement;

        ContextMenu menu = MainWindow.CreateSettingsMenu(mixer);
        MenuHoverHelp controller = MenuHoverHelp.Attach(menu);
        require(ReferenceEquals(controller, MenuHoverHelp.Attach(menu)),
            "Attaching Settings hover help repeatedly reuses one controller and one tooltip.");
        var first = Row(menu, "Right-click drag to pan");
        var second = Row(menu, "Reduce meter animation");
        var plain = Row(menu, "Help & shortcuts");
        var parent = Row(menu, "Audio device visibility");
        var childFirst = Row(parent, "Show disabled devices");
        var childSecond = Row(parent, "Show disconnected or missing devices");
        menu.PlacementTarget = window;
        menu.Placement = PlacementMode.Relative;
        menu.HorizontalOffset = 28;
        menu.VerticalOffset = 72;

        async Task Flush()
        {
            await layout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            // WPF destroys a ToolTip popup asynchronously. Its content must
            // remain intact until that real popup has finished closing.
            for (int attempt = 0; attempt < 20 && !controller.Popup.IsOpen
                && (controller.Popup.Content is not null || controller.Popup.PlacementTarget is not null); attempt++)
            {
                await Task.Delay(25);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            }
        }
        void FocusRow(MenuItem row)
        {
            Keyboard.Focus(row);
            require(row.IsKeyboardFocusWithin && PresentationSource.FromVisual(row) is not null,
                $"The actual {row.Header} row receives keyboard focus in its live menu popup.");
        }
        async Task ShowHelp(MenuItem row)
        {
            // Do not yield between focus and the immediate shortcut path: an
            // ordinary delayed focus-help request must not race the toggle.
            FocusRow(row);
            controller.ToggleKeyboardHelp(row);
            await Flush();
            require(controller.Popup.IsOpen && ReferenceEquals(controller.Popup.PlacementTarget, row)
                && ReferenceEquals(controller.Popup.Content, row.ToolTip)
                && PresentationSource.FromVisual(controller.Popup) is not null
                && !controller.Popup.Focusable && !controller.Popup.IsHitTestVisible,
                $"{row.Header} opens its own real tooltip beside that row without stealing menu focus.");
        }
        void RequireCleared(string description) => require(!controller.Popup.IsOpen
            && controller.Popup.PlacementTarget is null && controller.Popup.Content is null,
            description);

        try
        {
            menu.IsOpen = true; await Flush();
            require(menu.IsOpen && PresentationSource.FromVisual(menu) is not null,
                "The real Settings popup opens on the preview window for lifecycle checks.");
            await ShowHelp(first);
            object firstContent = controller.Popup.Content;
            await ShowHelp(second);
            require(!ReferenceEquals(firstContent, controller.Popup.Content),
                "Moving help from the first preference to the second replaces the content and placement owner.");

            var separator = menu.Items.OfType<Separator>().First();
            Point separatorPoint = separator.TranslatePoint(new Point(separator.ActualWidth / 2, separator.ActualHeight / 2), menu);
            DependencyObject? hit = VisualTreeHelper.HitTest(menu, separatorPoint)?.VisualHit;
            bool hitsRow = false;
            for (DependencyObject? current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
                hitsRow |= current is MenuItem;
            require(separator.ActualHeight > 0 && hit is not null && !hitsRow,
                "The rendered Settings separator is a real no-row region rather than another help owner.");
            using (var closing = new PopupCloseObserver(controller.Popup))
            {
                closing.Begin(); controller.RequestPointerHelp(null);
                require(!controller.Popup.IsOpen && !controller.IsPending && !closing.ClearedEarly,
                    "Leaving help for a separator closes it without clearing content or placement from a still-live popup before Closed.");
                FocusRow(first); controller.ToggleKeyboardHelp(first); await Flush();
                require(controller.Popup.IsOpen && ReferenceEquals(controller.Popup.Content, first.ToolTip)
                    && ReferenceEquals(controller.Popup.PlacementTarget, first),
                    "A rapid separator exit and new-row reopen survives the previous popup's asynchronous teardown.");
                closing.Begin(); controller.RequestPointerHelp(null);
                require(!closing.ClearedEarly,
                    "A second separator exit preserves the tooltip's visual references until its real Closed event.");
                await Flush();
                require(closing.ClosedObserved && !closing.ClearedEarly,
                    "Separator dismissal finishes through the real ToolTip Closed lifecycle without an empty live frame.");
                RequireCleared("After separator dismissal finishes, tooltip content and placement references are released.");
            }
            FocusRow(second); controller.RequestKeyboardHelp(second);
            require(controller.IsPending, "A delayed help request exists before moving into the separator region.");
            controller.RequestPointerHelp(null);
            require(!controller.IsPending, "Moving onto a separator cancels pending help as well as visible help.");
            await Task.Delay(400); await Flush();
            RequireCleared("A separator cannot produce a late empty tooltip after the help delay expires.");
            await ShowHelp(second);

            controller.HandleKey(Key.LeftCtrl, ModifierKeys.Control);
            controller.HandleKey(Key.LeftShift, ModifierKeys.Control | ModifierKeys.Shift);
            require(controller.Popup.IsOpen && ReferenceEquals(controller.Popup.PlacementTarget, second),
                "Pressing the help shortcut's modifier keys preserves the open tooltip until F10 arrives.");
            require(controller.HandleKey(Key.F10, ModifierKeys.Control | ModifierKeys.Shift),
                "The completed help chord is handled for the actual focused Settings row.");
            await Flush();
            RequireCleared("Repeating the focused row's help shortcut dismisses and releases the popup content.");
            controller.HandleKey(Key.RightCtrl, ModifierKeys.Control);
            controller.HandleKey(Key.RightShift, ModifierKeys.Control | ModifierKeys.Shift);
            controller.HandleKey(Key.F10, ModifierKeys.Control | ModifierKeys.Shift);
            await Flush();
            require(controller.Popup.IsOpen && ReferenceEquals(controller.Popup.PlacementTarget, second)
                && ReferenceEquals(controller.Popup.Content, second.ToolTip),
                "Repeating the shortcut again reopens help for the same focused row.");
            require(!controller.HandleKey(Key.F6, ModifierKeys.None),
                "Ordinary navigation keys remain unhandled by Settings help.");
            await Flush();
            RequireCleared("An ordinary navigation key dismisses the preceding row's help.");

            await ShowHelp(plain);
            require(controller.Popup.Content is string,
                "Plain-string Settings help follows the same popup lifecycle as structured help panels.");

            parent.IsSubmenuOpen = true; await Flush();
            RequireCleared("Opening the device-visibility submenu dismisses help from its parent menu.");
            require(parent.IsSubmenuOpen && PresentationSource.FromVisual(childFirst) is not null,
                "The device-visibility submenu creates its real child presentation surface.");
            await ShowHelp(childFirst);
            object childContent = controller.Popup.Content;
            await ShowHelp(childSecond);
            require(!ReferenceEquals(childContent, controller.Popup.Content),
                "Adjacent submenu rows switch their real help content and target without retaining the preceding row.");
            parent.IsSubmenuOpen = false; await Flush();
            RequireCleared("Closing a submenu releases its child tooltip, placement target and content.");

            await ShowHelp(first);
            menu.IsOpen = false; await Flush();
            RequireCleared("Closing Settings releases an open tooltip and all references to its menu row.");

            menu.IsOpen = true; await Flush();
            FocusRow(second);
            controller.RequestKeyboardHelp(second);
            require(controller.IsPending && !controller.Popup.IsOpen,
                "The production focus-help path schedules a delayed tooltip before Settings closes.");
            menu.IsOpen = false; await Flush();
            RequireCleared("Closing Settings also clears a pending focus-help request before it opens.");
            require(!controller.IsPending, "Closing Settings stops the outstanding help timer.");
            await Task.Delay(450); await Flush();
            RequireCleared("A cancelled help timer cannot reopen its tooltip after the initial delay has elapsed.");
        }
        finally
        {
            parent.IsSubmenuOpen = false;
            menu.IsOpen = false;
            await Flush();
            if (previousFocus is UIElement { IsVisible: true, IsEnabled: true } element
                && Window.GetWindow(element) == window) Keyboard.Focus(previousFocus);
        }

        require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == graph
            && Preferences(mixer) == preferences && mixer.ConfigurationPresentationCount == dispatches
            && mixer.EditHistory.Entries.Count == history && mixer.SaveStatus == saveStatus,
            "Opening, switching and closing Settings help preserves graph, preferences, save state, dispatches and edit history.");
    }

    private static MenuItem Row(ItemsControl menu, string header) => menu.Items.OfType<MenuItem>()
        .Single(row => Equals(row.Header, header));

    private sealed class PopupCloseObserver : IDisposable
    {
        private static readonly ConditionalWeakTable<ToolTip, PopupCloseObserver> Active = new();
        private static readonly DependencyPropertyDescriptor ContentChange = DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ToolTip));
        private static readonly DependencyPropertyDescriptor TargetChange = DependencyPropertyDescriptor.FromProperty(ToolTip.PlacementTargetProperty, typeof(ToolTip));
        private readonly ToolTip _popup;
        internal bool ClosedObserved { get; private set; }
        internal bool ClearedEarly { get; private set; }
        private bool _watching;
        static PopupCloseObserver()
        {
            // Class handlers precede production instance handlers, so cleanup
            // performed inside Closed is correctly recognised as safe.
            EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.ClosedEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is ToolTip { IsOpen: false } popup && Active.TryGetValue(popup, out var observer))
                    observer.ClosedObserved = true;
            }));
        }
        internal PopupCloseObserver(ToolTip popup)
        {
            _popup = popup; Active.Add(popup, this);
            ContentChange.AddValueChanged(popup, Changed); TargetChange.AddValueChanged(popup, Changed);
        }
        internal void Begin() { ClosedObserved = ClearedEarly = false; _watching = true; }
        private void Changed(object? sender, EventArgs args)
        {
            if (_watching && !ClosedObserved && !_popup.IsOpen && PresentationSource.FromVisual(_popup) is not null
                && (_popup.Content is null || _popup.PlacementTarget is null)) ClearedEarly = true;
        }
        public void Dispose()
        {
            ContentChange.RemoveValueChanged(_popup, Changed); TargetChange.RemoveValueChanged(_popup, Changed);
            Active.Remove(_popup);
        }
    }

    private static (bool RightPan, bool ReducedAnimation, bool Disabled, bool Disconnected, bool Advanced, bool StartAtLogin)
        Preferences(MixerViewModel mixer) => (mixer.RightClickDragToPan, mixer.ReducedAnimation,
            mixer.ShowDisabledAudioDevices, mixer.ShowDisconnectedAudioDevices, mixer.ShowAdvancedAudioDevices,
            mixer.StartAtLogin);
}
