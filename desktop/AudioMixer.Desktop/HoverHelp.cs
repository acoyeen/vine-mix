using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AudioMixer.Desktop;

/// <summary>
/// Application-wide ordinary-control help. Native owner-to-popup safe areas can
/// span the next control below; this service always follows the actual hit.
/// Context menus keep their scoped adapter over the same session engine.
/// </summary>
internal static class HoverHelp
{
    private static readonly DependencyProperty WindowHookProperty = DependencyProperty.RegisterAttached(
        "WindowHook", typeof(bool), typeof(HoverHelp), new PropertyMetadata(false));
    private static readonly DependencyPropertyDescriptor ToolTipChange = DependencyPropertyDescriptor.FromProperty(
        FrameworkElement.ToolTipProperty, typeof(FrameworkElement));
    private static HoverHelpSession? _session;
    private static FrameworkElement? _trackedOwner;
    private static DispatcherOperation? _reconcile;
    private static HoverHelpEnvironment? _verification;
    private static MouseEventArgs? _lastMove;
    internal static bool IsRegistered { get; private set; }
    internal static HoverHelpSession Session => _session
        ?? throw new InvalidOperationException("Hover help must be registered at application startup.");

    internal static void Register()
    {
        if (IsRegistered) return;
        _session = new HoverHelpSession(Application.Current.Dispatcher,
            () => Mouse.LeftButton == MouseButtonState.Released && Mouse.RightButton == MouseButtonState.Released,
            PointerOwner, Eligible, immediateHandoff: true);
        IsRegistered = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnUnloaded), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Mouse.PreviewMouseMoveEvent,
            new MouseEventHandler(OnPointerMove), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Mouse.MouseEnterEvent,
            new MouseEventHandler(OnPointerTransition), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Mouse.MouseLeaveEvent,
            new MouseEventHandler(OnPointerTransition), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler((_, _) => Suspend()), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnKeyDown), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnFocus), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((_, _) =>
            {
                if (Session.IsKeyboard && Session.Owner?.IsKeyboardFocusWithin != true) Suspend();
            }), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler((sender, args) =>
            {
                // A late native request must never open a second tooltip or
                // recreate its owner-to-popup safe area. Explicit ToolTip
                // instances keep their own lifecycle; the app uses content.
                if (sender is FrameworkElement { ToolTip: not ToolTip }) args.Handled = true;
            }), true);
        EventManager.RegisterClassHandler(typeof(ScrollViewer), ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => QueueReconcile()), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element && element.ToolTip is not ToolTip)
            ToolTipService.SetIsEnabled(element, false);
        if (sender is Window window && !(bool)window.GetValue(WindowHookProperty))
        {
            window.SetValue(WindowHookProperty, true);
            window.Deactivated += (_, _) => Suspend();
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element && _trackedOwner is { } owner
            && (ReferenceEquals(element, owner) || element.IsAncestorOf(owner))) Suspend();
    }

    private static void OnPointerMove(object sender, MouseEventArgs args)
    {
        // The class handler runs on every element along a preview route. Hit
        // test once per event, not once per ancestor of the same pointer hit.
        if (ReferenceEquals(_lastMove, args)) return;
        _lastMove = args;
        Session.RequestPointerHelp(PointerOwner());
    }

    private static void OnPointerTransition(object sender, MouseEventArgs args)
    {
        if (!Session.IsKeyboard) Session.RequestPointerHelp(PointerOwner());
    }

    private static FrameworkElement? PointerOwner()
    {
        FrameworkElement? owner;
        if (_verification is { } environment) owner = environment.PointerOwner();
        else
        {
            owner = null;
            if (Mouse.DirectlyOver is Visual surface)
            {
                while (VisualTreeHelper.GetParent(surface) is Visual parent) surface = parent;
                if (surface is IInputElement input) owner = ResolveOwner(surface, Mouse.GetPosition(input));
            }
        }
        Track(owner);
        return owner;
    }

    internal static FrameworkElement? ResolveOwner(Visual surface, Point point)
    {
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(surface, candidate =>
            candidate is UIElement element && (!element.IsHitTestVisible || element.Visibility != Visibility.Visible)
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        return FindOwner(hit);
    }

    private static FrameworkElement? FindOwner(DependencyObject? source)
    {
        // A managed menu owns the entire route, including help on an embedded
        // field. Do not let a window-scoped session compete with that adapter.
        for (var current = source; current is not null; current = Parent(current))
            if (current is MenuItem item && ManagedMenu(item)) return null;
        for (var current = source; current is not null; current = Parent(current))
        {
            if (current is not FrameworkElement element || !Eligible(element)) continue;
            ToolTipService.SetIsEnabled(element, false);
            return element;
        }
        return null;
    }

    private static bool ManagedMenu(MenuItem item)
    {
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(item);
        while (parent is MenuItem ancestor) parent = ItemsControl.ItemsControlFromItemContainer(ancestor);
        return parent is ContextMenu menu && MenuHoverHelp.Get(menu) is not null;
    }

    private static DependencyObject? Parent(DependencyObject current)
        => current is Visual || current is System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);

    private static bool Eligible(FrameworkElement element)
    {
        if (element.ToolTip is null or ToolTip || element is MenuItem { IsSubmenuOpen: true }
            || (!element.IsEnabled && !ToolTipService.GetShowOnDisabled(element))) return false;
        for (DependencyObject? current = element; current is not null; current = Parent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Handled) return;
        Key key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        if (key == Key.F10 && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
            && FindOwner(Keyboard.FocusedElement as DependencyObject) is { } owner)
        {
            Track(owner); Session.ToggleKeyboardHelp(owner); args.Handled = true;
        }
        else Suspend();
    }

    private static void OnFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (InputManager.Current.MostRecentInputDevice is not KeyboardDevice) return;
        var owner = FindOwner(args.NewFocus as DependencyObject);
        if (owner is null) { if (Session.IsKeyboard) Suspend(); return; }
        Track(owner); Session.RequestKeyboardHelp(owner);
    }

    private static void Track(FrameworkElement? owner)
    {
        if (ReferenceEquals(owner, _trackedOwner)) return;
        if (_trackedOwner is { } previous)
        {
            previous.IsVisibleChanged -= OnVisibility;
            previous.LayoutUpdated -= OnLayout;
            ToolTipChange.RemoveValueChanged(previous, OnHelpChanged);
        }
        _trackedOwner = owner;
        if (owner is null) return;
        owner.IsVisibleChanged += OnVisibility;
        owner.LayoutUpdated += OnLayout;
        ToolTipChange.AddValueChanged(owner, OnHelpChanged);
    }

    private static void OnVisibility(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is false) Suspend();
    }

    private static void OnLayout(object? sender, EventArgs args) => QueueReconcile();

    private static void OnHelpChanged(object? sender, EventArgs args)
    {
        if (_trackedOwner is { } owner && !Eligible(owner)) Suspend();
    }

    private static void QueueReconcile()
    {
        if (Session.Owner is null || Session.IsKeyboard || _reconcile?.Status == DispatcherOperationStatus.Pending) return;
        _reconcile = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _reconcile = null;
            if (Session.Owner is not null && !Session.IsKeyboard)
                Session.RequestPointerHelp(PointerOwner());
        }));
    }

    internal static void Suspend()
    {
        _reconcile?.Abort(); _reconcile = null;
        _lastMove = null;
        Session.Dismiss(); Track(null);
    }

    internal static void UseVerificationEnvironment(HoverHelpEnvironment environment)
    {
        if (Session.Popup.IsOpen || PresentationSource.FromVisual(Session.Popup) is not null)
            throw new InvalidOperationException("An ordinary hover-help fixture must remain detached.");
        Suspend(); _verification = environment;
        // Keep the production pointer resolver's owner tracking and lifecycle.
        Session.UseEnvironment(environment with { PointerOwner = PointerOwner });
    }

    internal static void RestoreRuntime()
    {
        Suspend(); Session.UseEnvironment(null); _verification = null;
    }
}
