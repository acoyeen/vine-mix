using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioMixer.Desktop;

/// <summary>
/// Each menu owns one tooltip. Row transitions reconcile the actual hit-tested
/// owner, independently of enter/leave order or a subsequent mouse movement.
/// </summary>
internal sealed class MenuHoverHelp
{
    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller", typeof(MenuHoverHelp), typeof(MenuHoverHelp), new PropertyMetadata(null));
    private readonly ContextMenu _menu;
    private readonly HashSet<MenuItem> _rows;
    private readonly ConditionalWeakTable<MenuItem, object> _hookedRows = new();
    private readonly HoverHelpSession _session;
    private MenuHoverHelpEnvironment? _environment;
    internal ToolTip Popup => _session.Popup;
    internal bool IsPending => _session.IsPending;
    internal MenuItem? Owner => _session.Owner as MenuItem;
    internal long PendingAt => _session.PendingAt;

    internal static MenuHoverHelp? Get(ContextMenu menu) => menu.GetValue(ControllerProperty) as MenuHoverHelp;

    internal static MenuHoverHelp Attach(ContextMenu menu)
    {
        if (Get(menu) is { } existing)
        {
            existing.RefreshRows();
            return existing;
        }
        var controller = new MenuHoverHelp(menu);
        menu.SetValue(ControllerProperty, controller);
        return controller;
    }

    // The detached verifier replaces only device/time/presentation boundaries.
    // It still uses the installed event handlers, real geometry and timer logic.
    internal static MenuHoverHelp AttachForVerification(ContextMenu menu, MenuHoverHelpEnvironment environment)
    {
        if (menu.IsOpen || PresentationSource.FromVisual(menu) is not null)
            throw new InvalidOperationException("A hover-help fixture must remain detached.");
        var controller = Attach(menu);
        controller._environment = environment;
        controller._session.UseEnvironment(new HoverHelpEnvironment(
            environment.MenuOpen, () => controller.HoveredRow(), environment.Now,
            owner => environment.CanShow((MenuItem)owner), environment.IsPopupOpen,
            owner => environment.Show((MenuItem)owner), environment.Close));
        return controller;
    }

    private MenuHoverHelp(ContextMenu menu)
    {
        _menu = menu;
        _rows = [];
        _session = new HoverHelpSession(menu.Dispatcher, () => IsMenuOpen,
            () => HoveredRow(), owner => owner is MenuItem row && Eligible(row));
        menu.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnMouseMove), true);
        menu.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => Dismiss()), true);
        menu.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnKeyDown), true);
        menu.MouseEnter += OnPointerTransition;
        menu.MouseLeave += OnPointerTransition;
        menu.Opened += (_, _) => RefreshRows();
        menu.Closed += (_, _) => _session.Dismiss(resetQuick: true);
        menu.Unloaded += (_, _) => _session.Dismiss(resetQuick: true);
        RefreshRows();
    }

    private void RefreshRows()
    {
        var current = Rows(_menu).ToHashSet();
        _rows.Clear(); _rows.UnionWith(current);
        if (Owner is { } owner && !_rows.Contains(owner)) Dismiss();
        foreach (MenuItem row in current)
        {
            if (_hookedRows.TryGetValue(row, out _)) continue;
            _hookedRows.Add(row, new object());
            // Keep the original help object and accessible help text, but do
            // not let the native service maintain a second owner/safe area.
            ToolTipService.SetIsEnabled(row, false);
            row.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(OnMouseMove), true);
            row.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => Dismiss()), true);
            row.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnKeyDown), true);
            row.MouseEnter += OnPointerTransition;
            row.MouseLeave += OnPointerTransition;
            row.Unloaded += (_, _) => { if (IsWithin(Owner, row)) Dismiss(); };
            row.SubmenuOpened += (_, args) => { if (ReferenceEquals(args.OriginalSource, row)) Dismiss(); };
            row.SubmenuClosed += (_, args) => { if (ReferenceEquals(args.OriginalSource, row) && IsWithin(Owner, row)) Dismiss(); };
            row.GotKeyboardFocus += (_, args) =>
            {
                if (InputManager.Current.MostRecentInputDevice is KeyboardDevice
                    && ReferenceEquals(args.OriginalSource, row)) RequestKeyboardHelp(row);
            };
            row.LostKeyboardFocus += (_, _) =>
            {
                if (_session.IsKeyboard && ReferenceEquals(Owner, row) && !row.IsKeyboardFocusWithin) Dismiss();
            };
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs args) => RequestPointerHelp(HoveredRow());

    private void OnPointerTransition(object sender, MouseEventArgs args)
    {
        // WPF may change the row under a stationary pointer during popup,
        // scrolling or filtering layout. A late leave from the previous row
        // must not cancel the next row, nor require another move to recover.
        if (!_session.IsKeyboard) RequestPointerHelp(HoveredRow());
    }

    internal void RequestPointerHelp(MenuItem? row) => _session.RequestPointerHelp(row);

    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Handled) return;
        Key key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (HandleKey(key, Keyboard.Modifiers)) args.Handled = true;
    }

    internal bool HandleKey(Key key, ModifierKeys modifiers)
    {
        // Building the help chord must not dismiss the tip before F10 can
        // toggle it. Navigation and action keys still clear the old owner.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return false;
        if (key == Key.F10 && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            DependencyObject? current = Keyboard.FocusedElement as DependencyObject;
            while (current is Visual && current is not MenuItem) current = VisualTreeHelper.GetParent(current);
            if (current is MenuItem row && Eligible(row))
            {
                ToggleKeyboardHelp(row);
                return true;
            }
            return false;
        }
        Dismiss();
        return false;
    }

    internal void ToggleKeyboardHelp(MenuItem row) => _session.ToggleKeyboardHelp(row);

    internal void RequestKeyboardHelp(MenuItem row) => _session.RequestKeyboardHelp(row);

    private MenuItem? HoveredRow()
    {
        if (!IsMenuOpen) return null;
        var pointer = _environment is { } environment ? environment.Pointer() : ReadPointer();
        return pointer is { } sample ? RowAt(sample.Surface, sample.Position) : null;
    }

    private static (Visual Surface, Point Position)? ReadPointer()
    {
        if (Mouse.DirectlyOver is not Visual visual) return null;
        Visual surface = visual;
        while (VisualTreeHelper.GetParent(surface) is Visual parent) surface = parent;
        if (surface is not IInputElement input) return null;
        return (surface, Mouse.GetPosition(input));
    }

    internal MenuItem? RowAt(Visual surface, Point position)
    {
        var hit = VisualTreeHelper.HitTest(surface, position)?.VisualHit;
        for (DependencyObject? current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not MenuItem row) continue;
            Point local = surface.TransformToDescendant(row).Transform(position);
            return _rows.Contains(row) && new Rect(row.RenderSize).Contains(local) ? row : null;
        }
        return null;
    }

    private bool IsMenuOpen => _environment?.MenuOpen() ?? _menu.IsOpen;

    private bool Eligible(MenuItem? row) => row is not null && _rows.Contains(row)
        && row.Visibility == Visibility.Visible && row.ToolTip is not null and not ToolTip && !row.IsSubmenuOpen
        && (row.IsEnabled || ToolTipService.GetShowOnDisabled(row));

    internal void ShowPending() => _session.ShowPending();

    private void Dismiss() => _session.Dismiss();

    private static bool IsWithin(MenuItem? row, MenuItem ancestor)
    {
        while (row is not null)
        {
            if (ReferenceEquals(row, ancestor)) return true;
            row = ItemsControl.ItemsControlFromItemContainer(row) as MenuItem;
        }
        return false;
    }

    private static IEnumerable<MenuItem> Rows(ItemsControl parent)
    {
        foreach (MenuItem row in parent.Items.OfType<MenuItem>())
        {
            yield return row;
            foreach (MenuItem child in Rows(row)) yield return child;
        }
    }
}

internal sealed record MenuHoverHelpEnvironment(
    Func<bool> MenuOpen,
    Func<(Visual Surface, Point Position)?> Pointer,
    Func<long> Now,
    Func<MenuItem, bool> CanShow,
    Func<bool> IsPopupOpen,
    Action<MenuItem> Show,
    Action Close);
