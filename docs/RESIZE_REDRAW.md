# Resize redraw behavior

Research date: 21 October 2026.

The reported symptom is a trail of old header/footer elements while resizing;
the canvas is less affected. The window is already opaque and uses WPF
`WindowChrome` with no glass. There is no evidence establishing a particular
graphics driver or composition fault. A detached render cannot reproduce or
prove the absence of that physical display symptom.

The resizable caption now coalesces native `WM_SIZE` notifications and the final
`WM_EXITSIZEMOVE` into one full client-area invalidation after WPF's pending
layout/render work. Minimise, ordinary movement and paint notifications do not
schedule it. Closing cancels pending work. The root/header also explicitly draw
their opaque theme background and clip to their bounds. This addresses the
plausible stale-damage path without software rendering, bitmap caches, polling,
changing canvas transforms or changing audio/settings behavior. It is a targeted
mitigation; the reported live symptom still needs physical resize verification.

The implementation deliberately retains `WindowChrome`'s native hit testing,
maximise/restore and monitor work-area handling. It does not override
`WM_NCCALCSIZE`: WPF already returns `WVR_REDRAW` for that message.

Primary evidence:

- [Microsoft: InvalidateRect](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-invalidaterect)
  documents that a null rectangle invalidates the full client area. The call
  supplies the existing window handle and requests no GDI background erase.
- [Microsoft: DispatcherPriority](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatcherpriority)
  places `Loaded` after layout/render and below `Render` priority.
- [WPF HwndTarget source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndTarget.cs)
  shows `WM_PAINT` forwarding its update region to the retained compositor.
- [WPF WindowChromeWorker source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Shell/WindowChromeWorker.cs)
  shows the existing `WVR_REDRAW` behavior.

`ResizeRedrawVerification` checks coalescing, priority ordering, cancellation,
minimise/move/paint exclusions, and repeated growth/shrinkage between minimum,
normal and large layouts at 100%, 125%, 150% and 200% DPI. It compares header and
footer pixels painted over an uncleared target against a fresh render, and checks
that layout preserves configuration, graph/camera, pending edits, history and save
status. Resizing must preserve canvas transforms and keep cable endpoints at the
actual socket anchors; unchanged anchors must reuse cached geometry. Fractional
DPI layout can change rounded node/socket bounds during a size change, requiring
new cable geometry. A diagnostic full-suite run observed 18 cable rebuilds with
unchanged configuration/presentation, history, save status and canvas-transform
counters. This establishes
opaque coverage and scheduling only. It creates no HWND and sends no input;
native sizing-loop presentation and graphics-driver behavior are not covered.
Each synthetic DPI transition explicitly remeasures the detached tree and updates
cable geometry before the resize sequence, as the existing render harness does;
changing a detached root's DPI does not simulate a native DPI-change message.
