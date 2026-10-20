using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

namespace AudioMixer.Desktop;

/// <summary>
/// Shared timing and popup lifetime for hover help. Owners are supplied by the
/// containing surface, so tooltip placement never affects the next owner.
/// </summary>
internal sealed class HoverHelpSession
{
    private readonly Func<bool> _active;
    private readonly Func<FrameworkElement?> _pointerOwner;
    private readonly Func<FrameworkElement, bool> _eligible;
    private readonly bool _immediateHandoff;
    private readonly DispatcherTimer _timer;
    private HoverHelpEnvironment? _environment;
    private FrameworkElement? _owner;
    private bool _keyboard;
    private long _due, _quickUntil;

    internal ToolTip Popup { get; } = new()
    {
        StaysOpen = true, Focusable = false, IsHitTestVisible = false,
        Placement = PlacementMode.Right, HorizontalOffset = 8
    };
    internal FrameworkElement? Owner => _owner;
    internal bool IsKeyboard => _keyboard;
    internal bool IsPending => _timer.IsEnabled;
    internal long PendingAt => _due;

    internal HoverHelpSession(Dispatcher dispatcher, Func<bool> active,
        Func<FrameworkElement?> pointerOwner, Func<FrameworkElement, bool> eligible,
        bool immediateHandoff = false)
    {
        _active = active;
        _pointerOwner = pointerOwner;
        _eligible = eligible;
        _immediateHandoff = immediateHandoff;
        Popup.Resources[SystemParameters.ToolTipPopupAnimationKey] = PopupAnimation.None;
        Popup.Closed += (_, _) => ReleaseClosedPopup();
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _timer.Tick += (_, _) => ShowPending();
    }

    // Detached verification changes only device/time/presentation boundaries.
    // The same owner, deadline and popup-lifetime logic remains in use.
    internal void UseEnvironment(HoverHelpEnvironment? environment)
    {
        Dismiss(resetQuick: true);
        _environment = environment;
    }

    internal void RequestPointerHelp(FrameworkElement? owner) => Choose(owner, keyboard: false);

    internal void RequestKeyboardHelp(FrameworkElement owner) => Choose(owner, keyboard: true);

    internal void ToggleKeyboardHelp(FrameworkElement owner)
    {
        if (_keyboard && IsPopupOpen && ReferenceEquals(_owner, owner)) Dismiss();
        else Choose(owner, keyboard: true, immediate: true);
    }

    private bool IsActive => _environment?.Active() ?? _active();
    private bool IsPopupOpen => _environment?.IsPopupOpen() ?? Popup.IsOpen;
    private long Now => _environment?.Now() ?? Environment.TickCount64;
    private FrameworkElement? PointerOwner() => _environment is { } environment
        ? environment.PointerOwner() : _pointerOwner();
    private bool Eligible(FrameworkElement? owner) => owner is not null && _eligible(owner);

    private void Choose(FrameworkElement? owner, bool keyboard, bool immediate = false)
    {
        if (!IsActive || !Eligible(owner)) { Dismiss(); return; }
        if (ReferenceEquals(_owner, owner) && _keyboard == keyboard && !immediate) return;
        if (_immediateHandoff && !keyboard && !_keyboard && IsPopupOpen)
        {
            // Ordinary adjacent controls can reuse the visible popup directly.
            // The pointer owner is checked again before its content is shown.
            _timer.Stop();
            _owner = owner; _keyboard = false; _due = Now;
            ShowPending();
            return;
        }
        Dismiss();
        _owner = owner; _keyboard = keyboard;
        if (immediate) { _due = Now; ShowPending(); return; }
        int delay = Now < _quickUntil ? 50 : 350;
        _due = Now + delay;
        _timer.Interval = TimeSpan.FromMilliseconds(delay);
        _timer.Start();
    }

    internal void ShowPending()
    {
        _timer.Stop();
        if (!IsActive || !Eligible(_owner)) { Dismiss(); return; }
        long remaining = _due - Now;
        if (remaining > 0) { _timer.Interval = TimeSpan.FromMilliseconds(remaining); _timer.Start(); return; }
        if (!_keyboard)
        {
            var hovered = PointerOwner();
            if (!ReferenceEquals(hovered, _owner))
            {
                // A pointer can reach a new owner before the pending tick,
                // without another movement after its layout has settled.
                RequestPointerHelp(hovered);
                return;
            }
        }
        if ((_environment is { } environment ? !environment.CanShow(_owner!)
                : PresentationSource.FromVisual(_owner!) is null)
            || (_keyboard && !_owner!.IsKeyboardFocusWithin))
        { Dismiss(); return; }
        Popup.PlacementTarget = _owner;
        // Meter and node help can replace its content while the pointer stays
        // still. Keep the visible popup synchronized with that same owner.
        BindingOperations.SetBinding(Popup, ContentControl.ContentProperty,
            new Binding(nameof(FrameworkElement.ToolTip)) { Source = _owner, Mode = BindingMode.OneWay });
        if (_environment is { } presenter) { presenter.Show(_owner!); return; }
        Popup.IsOpen = true;
    }

    internal void Dismiss(bool resetQuick = false)
    {
        _timer.Stop();
        if (IsPopupOpen) _quickUntil = Now + 800;
        if (resetQuick) _quickUntil = 0;
        _owner = null; _keyboard = false;
        if (_environment is { } environment)
        {
            environment.Close();
            if (!Popup.IsOpen && PresentationSource.FromVisual(Popup) is null) ReleaseClosedPopup();
            return;
        }
        Popup.IsOpen = false;
        // IsOpen=false starts asynchronous native teardown. Clearing content
        // while that window still exists can briefly render an empty border.
        if (PresentationSource.FromVisual(Popup) is null) ReleaseClosedPopup();
    }

    private void ReleaseClosedPopup()
    {
        // A new owner can reopen the shared tooltip before an old close
        // notification arrives. Do not clear its content or placement target.
        if (Popup.IsOpen) return;
        Popup.Content = null;
        Popup.PlacementTarget = null;
    }
}

internal sealed record HoverHelpEnvironment(
    Func<bool> Active,
    Func<FrameworkElement?> PointerOwner,
    Func<long> Now,
    Func<FrameworkElement, bool> CanShow,
    Func<bool> IsPopupOpen,
    Action<FrameworkElement> Show,
    Action Close);
