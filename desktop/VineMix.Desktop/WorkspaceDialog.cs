using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Shared quiet chrome and owner-click dismissal for workspace information windows.</summary>
internal static class WorkspaceDialog
{
    internal static Window Create(Window owner, string title, double width, Func<bool>? canDismiss = null)
    {
        var dialog = new Window
        {
            Owner = owner, Title = title, Width = width, MinWidth = 440,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = owner.FontFamily, FontSize = 13
        };
        NativeCaption.Attach(dialog, 24, resizable: false, movable: false);
        dialog.SetResourceReference(Control.BackgroundProperty, "ConsoleWindowBrush");
        dialog.SetResourceReference(Control.ForegroundProperty, "ConsoleTextBrush");
        _ = new Dismissal(owner, dialog, canDismiss ?? (() => true));
        return dialog;
    }

    internal static Button CloseButton(Action close)
    {
        var button = new Button
        {
            Content = "Close", Padding = new Thickness(10, 7, 10, 7), MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0)
        };
        button.SetResourceReference(Control.ForegroundProperty, "ConsoleMutedBrush");
        AutomationProperties.SetName(button, "Close");
        AutomationProperties.SetHelpText(button, "Close this window. You can also press Escape or click the workspace.");
        button.Click += (_, _) => close();
        return button;
    }

    internal sealed class Dismissal
    {
        private readonly Window _owner;
        private readonly Window _dialog;
        private readonly Func<bool> _canDismiss;
        private HwndSource? _ownerSource;
        private bool _closed;

        internal Dismissal(Window owner, Window dialog, Func<bool> canDismiss)
        {
            _owner = owner; _dialog = dialog; _canDismiss = canDismiss;
            owner.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OwnerMouseDown), true);
            dialog.PreviewKeyDown += KeyDown;
            dialog.SourceInitialized += ConnectOwner;
            dialog.Closed += Closed;
        }

        internal bool DismissFromOwner()
        {
            if (_closed || !_canDismiss()) return false;
            _dialog.Close();
            return _closed;
        }

        internal bool DismissFromKey(Key key) => key == Key.Escape && DismissFromOwner();

        private void OwnerMouseDown(object sender, MouseButtonEventArgs args)
        {
            // Consume the first workspace click, so dismissing a notice cannot
            // also change a node, toggle audio, or start a drag underneath it.
            if (DismissFromOwner()) args.Handled = true;
        }

        private void KeyDown(object sender, KeyEventArgs args)
        {
            if (DismissFromKey(args.Key)) args.Handled = true;
        }

        private void ConnectOwner(object? sender, EventArgs args)
        {
            _ownerSource = HwndSource.FromHwnd(new WindowInteropHelper(_owner).Handle);
            _ownerSource?.AddHook(OwnerMessage);
        }

        private IntPtr OwnerMessage(IntPtr handle, int message, IntPtr parameter, IntPtr data, ref bool handled)
            => HandleOwnerMessage(message, ref handled);

        internal IntPtr HandleOwnerMessage(int message, ref bool handled)
        {
            // WM_MOUSEACTIVATE includes clicks on the native caption. Returning
            // MA_ACTIVATEANDEAT focuses the owner without forwarding that click.
            // No deactivation handler: switching to another app keeps the notice.
            if (message == 0x0021 && DismissFromOwner())
            { handled = true; return new IntPtr(2); }
            // The owner can already be active after keyboard task switching.
            if (message is 0x00A1 or 0x00A4 or 0x00A7 && DismissFromOwner()) handled = true;
            return IntPtr.Zero;
        }

        private void Closed(object? sender, EventArgs args)
        {
            _closed = true;
            _owner.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OwnerMouseDown));
            _dialog.PreviewKeyDown -= KeyDown;
            _dialog.SourceInitialized -= ConnectOwner;
            _dialog.Closed -= Closed;
            if (_ownerSource is { IsDisposed: false }) _ownerSource.RemoveHook(OwnerMessage);
            _ownerSource = null;
        }
    }
}
