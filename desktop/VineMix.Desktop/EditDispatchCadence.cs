using System;

namespace VineMix.Desktop;

/// <summary>
/// Coalesces edits without waiting for pointer movement to stop. The caller uses
/// one monotonic clock and serializes requests; edits queued during a request
/// keep the next dispatch deadline instead of postponing it.
/// </summary>
internal sealed class EditDispatchCadence
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private TimeSpan? _due;

    public void Queue(TimeSpan now) => _due ??= now + Interval;
    public bool IsDue(TimeSpan now) => _due is { } due && now >= due;
    public void BeginDispatch(TimeSpan now) => _due = now + Interval;
    public void Complete(bool hasPending) { if (!hasPending) _due = null; }

    // A stale revision needs a fresh snapshot before retrying. New edits must
    // not extend this backoff, just as they must not extend a normal deadline.
    public void DeferRetry(TimeSpan now) => _due = now + Interval;
}
