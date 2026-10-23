using System;

namespace VineMix.Desktop;

public sealed partial class MixerViewModel
{
    // Session UI state: hiding/showing preserves the lock; each new session
    // starts unlocked. It is deliberately absent from saved graphs and Undo.
    private bool _layoutLocked;
    public event EventHandler? LayoutEditBlocked;
    internal void NotifyLayoutEditBlocked()
    {
        if (IsLayoutLocked) LayoutEditBlocked?.Invoke(this, EventArgs.Empty);
    }
    internal const string LayoutLockedMessage = "Layout locked. Unlock the padlock to move nodes or change connections.";
    public bool IsLayoutLocked
    {
        get => _layoutLocked;
        set
        {
            if (!Set(ref _layoutLocked, value)) return;
            NotifyHistory();
        }
    }
    public bool CanEditLayout => CanEdit && !IsLayoutLocked;
}
