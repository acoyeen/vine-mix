using System;
using System.Threading;
using System.Threading.Tasks;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

// Runs on the caller's synchronization context so callbacks can update WPF.
// Only a completed, successful check earns the daily cooldown; offline or
// invalid responses retry without requiring the user to restart the mixer.
internal sealed class AutomaticUpdateSchedule(
    Func<CancellationToken, Task<UpdateCheckStatus?>> check,
    Func<DateTimeOffset?> readSuccess,
    Action<DateTimeOffset> writeSuccess,
    Action<UpdateCheckStatus> completed,
    Func<DateTimeOffset>? utcNow = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IDisposable
{
    internal static readonly TimeSpan DailyInterval = TimeSpan.FromDays(1);
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private CancellationTokenSource? _run;
    private bool _disposed;
    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal void SetEnabled(bool enabled, bool checkSoon = false)
    {
        if (_disposed) return;
        if (!enabled) { _run?.Cancel(); _run = null; return; }
        if (_run is not null && !checkSoon) return;
        _run?.Cancel();
        var run = new CancellationTokenSource();
        _run = run;
        Completion = RunAsync(run, checkSoon);
    }

    private async Task RunAsync(CancellationTokenSource run, bool checkSoon)
    {
        var token = run.Token;
        try
        {
            await _delay(checkSoon ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(30), token);
            token.ThrowIfCancellationRequested();
            if (!checkSoon && readSuccess() is { } last)
            {
                var age = _utcNow() - last;
                // Future timestamps cannot defer checking indefinitely after a clock change.
                if (age >= TimeSpan.Zero && age < DailyInterval) await _delay(DailyInterval - age, token);
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var status = await check(token);
                token.ThrowIfCancellationRequested();
                bool success = status is UpdateCheckStatus.Available or UpdateCheckStatus.UpToDate;
                if (success) writeSuccess(_utcNow());
                if (status is { } result) completed(result);
                // A manual operation owns the controller while busy; try again
                // shortly without replacing it or counting it as a failed check.
                await _delay(success ? DailyInterval : status is null ? TimeSpan.FromMinutes(1) : RetryInterval, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_run, run)) _run = null;
            run.Dispose();
        }
    }

    public void Dispose() { _disposed = true; _run?.Cancel(); _run = null; }
}
