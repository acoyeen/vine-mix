using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace VineMix.Desktop;

/// <summary>Exercise the actual dismissal routes without opening a window or sending desktop input.</summary>
internal static class WorkspaceDialogVerification
{
    internal static void Run(Action<bool, string> require)
    {
        var owner = new Window();
        try
        {
            var pointerDialog = new Window();
            int closed = 0;
            pointerDialog.Closed += (_, _) => closed++;
            _ = new WorkspaceDialog.Dismissal(owner, pointerDialog, () => true);
            var pointer = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent };
            owner.RaiseEvent(pointer);
            require(pointer.Handled && closed == 1,
                "Clicking the owner dismisses the information window and consumes the click before a workspace control acts.");
            var nextPointer = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent };
            owner.RaiseEvent(nextPointer);
            require(!nextPointer.Handled && closed == 1,
                "Closing a workspace information window removes its owner-click handler and releases subsequent clicks.");

            var keyboardDialog = new Window();
            var keyboard = new WorkspaceDialog.Dismissal(owner, keyboardDialog, () => true);
            bool handled = false;
            _ = keyboard.HandleOwnerMessage(0x0006, ref handled); // WM_ACTIVATE
            require(!handled && !keyboard.DismissFromKey(Key.Tab) && keyboard.DismissFromKey(Key.Escape),
                "Activation changes and ordinary keys leave the notice open; Escape explicitly dismisses it.");

            var nativeDialog = new Window();
            var native = new WorkspaceDialog.Dismissal(owner, nativeDialog, () => true);
            handled = false;
            require(native.HandleOwnerMessage(0x0021, ref handled) == new IntPtr(2) && handled,
                "Clicking an inactive owner activates it and consumes the native mouse click while dismissing the notice.");
            var captionDialog = new Window();
            var caption = new WorkspaceDialog.Dismissal(owner, captionDialog, () => true);
            handled = false;
            _ = caption.HandleOwnerMessage(0x00A1, ref handled);
            require(handled, "A click on an already active owner's native caption also dismisses its notice.");

            var installingDialog = new Window();
            bool installing = true;
            int installingClosed = 0;
            installingDialog.Closed += (_, _) => installingClosed++;
            var install = new WorkspaceDialog.Dismissal(owner, installingDialog, () => !installing);
            handled = false;
            _ = install.HandleOwnerMessage(0x0021, ref handled);
            require(!handled && !install.DismissFromOwner() && !install.DismissFromKey(Key.Escape) && installingClosed == 0,
                "Installation prevents dismissal from the owner, native caption and Escape through the shared availability guard.");
            installing = false;
            require(install.DismissFromKey(Key.Escape) && installingClosed == 1,
                "Dismissal becomes available again after installation preparation ends.");

            require(new WindowInteropHelper(owner).Handle == IntPtr.Zero
                && new WindowInteropHelper(pointerDialog).Handle == IntPtr.Zero
                && new WindowInteropHelper(installingDialog).Handle == IntPtr.Zero,
                "Dismissal verification constructs no native windows and cannot operate the personal workspace.");
        }
        finally { owner.Close(); }
    }
}
