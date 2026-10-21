using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace VineMix.Desktop.Controls;

internal static class NativeCaption
{
    internal const double ResizeBorderSize = 8;
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr window, IntPtr rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    /// <summary>Own the title-bar visuals while retaining native move and system-menu behavior.</summary>
    public static void Attach(Window window, double captionHeight = 52, bool resizable = false)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = captionHeight,
            ResizeBorderThickness = new Thickness(resizable ? ResizeBorderSize : 0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        HwndSource? source = null;
        ResizeRedraw? resizeRedraw = null;
        IntPtr WindowHook(IntPtr handle, int message, IntPtr parameter, IntPtr data, ref bool handled)
        {
            if (resizable && ResizeRedraw.IsResizeNotification(message, parameter)) resizeRedraw?.Request();
            // Keep auxiliary fixed-size windows fixed. The workspace retains native
            // edge resizing, caption double-click, snap, and restore-on-drag behavior.
            if (!resizable && (message == 0x0112 && ((parameter.ToInt64() & 0xFFF0) is 0xF000 or 0xF030)
                || message == 0x00A3 && parameter.ToInt64() == 2))
                handled = true;
            if (resizable && message == 0x0024 && data != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(handle, 2); // MONITOR_DEFAULTTONEAREST
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    // These rectangles are physical pixels. Respect the active
                    // monitor's taskbar work area, including non-primary monitors.
                    var bounds = Marshal.PtrToStructure<MinMaxInfo>(data);
                    bounds.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                    bounds.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                    bounds.MaxSize.X = info.Work.Right - info.Work.Left;
                    bounds.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                    var dpi = VisualTreeHelper.GetDpi(window);
                    bounds.MinTrackSize.X = (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX);
                    bounds.MinTrackSize.Y = (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY);
                    Marshal.StructureToPtr(bounds, data, false);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
        void Apply(object? sender, EventArgs args)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int noRounding = 1;
            _ = DwmSetWindowAttribute(handle, 33, ref noRounding, sizeof(int));
        }
        void Initialize(object? sender, EventArgs args)
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (resizable)
                resizeRedraw = new ResizeRedraw(window.Dispatcher, () =>
                {
                    if (source is { IsDisposed: false } && window.IsVisible && window.WindowState != WindowState.Minimized)
                        _ = InvalidateRect(source.Handle, IntPtr.Zero, erase: false);
                });
            source?.AddHook(WindowHook);
            Apply(sender, args);
        }
        void SystemMenu(object sender, KeyEventArgs args)
        {
            if (args.Key != Key.System || args.SystemKey != Key.Space) return;
            args.Handled = true;
            var screen = window.PointToScreen(new Point(0, captionHeight));
            var dpi = VisualTreeHelper.GetDpi(window);
            SystemCommands.ShowSystemMenu(window, new Point(screen.X / dpi.DpiScaleX, screen.Y / dpi.DpiScaleY));
        }
        window.SourceInitialized += Initialize;
        window.PreviewKeyDown += SystemMenu;
        window.Closed += (_, _) =>
        {
            resizeRedraw?.Dispose();
            if (source is { IsDisposed: false }) source.RemoveHook(WindowHook);
            window.SourceInitialized -= Initialize;
            window.PreviewKeyDown -= SystemMenu;
        };
    }

    /// <summary>Accessible native window actions embedded in the console's own header.</summary>
    public static Button WindowButton(Window window, bool close, string? caption = null)
    {
        string name = caption ?? (close ? "Close window" : "Minimise window");
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = close ? "\uE8BB" : "\uE921",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            Width = 34,
            Height = 30,
            MinHeight = 30,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = name
        };
        AutomationProperties.SetName(button, name);
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += (_, _) =>
        {
            if (close) SystemCommands.CloseWindow(window);
            else SystemCommands.MinimizeWindow(window);
        };
        return button;
    }

    public static Button MaximiseButton(Window window)
    {
        var icon = new TextBlock
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        var button = new Button
        {
            Content = icon, Width = 34, Height = 30, MinHeight = 30, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        void Update(object? sender, EventArgs args)
        {
            bool maximised = window.WindowState == WindowState.Maximized;
            icon.Text = maximised ? "\uE923" : "\uE922";
            string name = maximised ? "Restore window" : "Maximise window";
            button.ToolTip = name;
            AutomationProperties.SetName(button, name);
        }
        button.Click += (_, _) =>
        {
            if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
            else SystemCommands.MaximizeWindow(window);
        };
        window.StateChanged += Update;
        window.Closed += (_, _) => window.StateChanged -= Update;
        Update(null, EventArgs.Empty);
        return button;
    }
}
