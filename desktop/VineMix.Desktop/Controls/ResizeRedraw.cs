using System;
using System.Windows.Threading;

namespace VineMix.Desktop.Controls;

/// <summary>Repaints the retained native surface after resize layout has settled.</summary>
internal sealed class ResizeRedraw(Dispatcher dispatcher, Action invalidate) : IDisposable
{
    private DispatcherOperation? _pending;
    private bool _disposed;

    internal static bool IsResizeNotification(int message, IntPtr parameter) =>
        message == 0x0005 && parameter.ToInt64() is 0 or 2 // WM_SIZE: SIZE_RESTORED / SIZE_MAXIMIZED
        || message == 0x0232; // WM_EXITSIZEMOVE: repaint the final restored surface too.

    internal void Request()
    {
        if (_disposed || _pending is not null || dispatcher.HasShutdownStarted) return;
        // WM_SIZE arrives before WPF updates its layout/render batch. Loaded is
        // below Render, so the compositor repaints the current scene rather than
        // preserving glyphs at their old centred/right/bottom-aligned positions.
        // Coalesce native size notifications; no timer or idle rendering loop.
        _pending = dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _pending = null;
            if (!_disposed) invalidate();
        }));
    }

    public void Dispose()
    {
        _disposed = true;
        _pending?.Abort();
        _pending = null;
    }
}
