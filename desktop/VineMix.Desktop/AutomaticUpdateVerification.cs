using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

internal static class AutomaticUpdateVerification
{
    internal static async Task RunAsync(Action<bool, string> require)
    {
        var clock = new ScheduleClock();
        var results = new Queue<UpdateCheckStatus?>([UpdateCheckStatus.Unavailable, UpdateCheckStatus.RateLimited,
            UpdateCheckStatus.InvalidRelease, UpdateCheckStatus.Incompatible, UpdateCheckStatus.UpToDate, UpdateCheckStatus.Available]);
        var stamps = new List<DateTimeOffset>();
        var completed = new List<UpdateCheckStatus>();
        int checks = 0;
        using (var schedule = new AutomaticUpdateSchedule(_ => { checks++; return Task.FromResult(results.Dequeue()); },
            () => null, stamps.Add, completed.Add, () => clock.Now, clock.Delay))
        {
            schedule.SetEnabled(true);
            await clock.Expect(TimeSpan.FromSeconds(30), require);
            require(checks == 0 && stamps.Count == 0, "Automatic checks wait until startup settles and do not record an attempt as success.");
            for (int failure = 1; failure <= 4; failure++)
            {
                clock.Advance(); await clock.Expect(AutomaticUpdateSchedule.RetryInterval, require);
                require(checks == failure && stamps.Count == 0 && completed.Count == failure,
                    "Unavailable, rate-limited, invalid and incompatible responses retain a bounded retry without earning the daily cooldown.");
            }
            clock.Advance(); await clock.Expect(AutomaticUpdateSchedule.DailyInterval, require);
            require(checks == 5 && stamps.Count == 1 && stamps[0] == clock.Now && completed[^1] == UpdateCheckStatus.UpToDate,
                "A successful retry alone records the last successful check and resumes the daily interval.");
            clock.Advance(); await clock.Expect(AutomaticUpdateSchedule.DailyInterval, require);
            require(checks == 6 && stamps.Count == 2 && completed[^1] == UpdateCheckStatus.Available,
                "A continuously running app checks again daily and reports an available release without needing another startup.");
            schedule.SetEnabled(false); await schedule.Completion;
            require(checks == 6 && stamps.Count == 2, "Disabling automatic checks cancels the pending daily wait without another request or success stamp.");
        }

        clock = new ScheduleClock(); checks = 0; stamps.Clear();
        var previous = clock.Now.AddHours(-12);
        using (var schedule = new AutomaticUpdateSchedule(_ => { checks++; return Task.FromResult<UpdateCheckStatus?>(UpdateCheckStatus.UpToDate); },
            () => previous, stamps.Add, _ => { }, () => clock.Now, clock.Delay))
        {
            schedule.SetEnabled(true); clock.Advance();
            await clock.Expect(TimeSpan.FromHours(12) - TimeSpan.FromSeconds(30), require);
            require(checks == 0, "A recent successful check keeps its remaining cooldown without ending the automatic schedule.");
            clock.Advance(); await clock.Expect(AutomaticUpdateSchedule.DailyInterval, require);
            require(checks == 1 && stamps.Count == 1, "The next check occurs when the persisted success reaches one day old.");
            schedule.Dispose(); await schedule.Completion;
            schedule.SetEnabled(true, checkSoon: true);
            require(checks == 1, "A disposed automatic schedule cannot be restarted by a late preference callback.");
        }

        clock = new ScheduleClock(); checks = 0; stamps.Clear();
        using (var schedule = new AutomaticUpdateSchedule(_ => { checks++; return Task.FromResult<UpdateCheckStatus?>(checks == 1 ? null : UpdateCheckStatus.UpToDate); },
            () => clock.Now.AddDays(30), stamps.Add, _ => { }, () => clock.Now, clock.Delay))
        {
            schedule.SetEnabled(true); clock.Advance(); await clock.Expect(TimeSpan.FromMinutes(1), require);
            require(checks == 1 && stamps.Count == 0, "A future timestamp cannot suppress checking; a busy manual operation defers background work without claiming success.");
            clock.Advance(); await clock.Expect(AutomaticUpdateSchedule.DailyInterval, require);
            require(checks == 2 && stamps.Count == 1, "A busy controller is retried after one minute and returns to the daily cadence after success.");
            schedule.Dispose(); await schedule.Completion;
        }

        clock = new ScheduleClock(); checks = 0; stamps.Clear(); completed.Clear();
        var inFlight = new TaskCompletionSource<UpdateCheckStatus?>();
        var started = new TaskCompletionSource();
        CancellationToken operationToken = default;
        using (var schedule = new AutomaticUpdateSchedule(token => { checks++; operationToken = token; started.SetResult(); return inFlight.Task; },
            () => clock.Now, stamps.Add, completed.Add, () => clock.Now, clock.Delay))
        {
            schedule.SetEnabled(false);
            require(checks == 0 && clock.Pending == 0, "Disabled automatic checks schedule no timer or request.");
            schedule.SetEnabled(true, checkSoon: true);
            await clock.Expect(TimeSpan.FromSeconds(2), require); clock.Advance();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            require(checks == 1, "Enabling automatic checking schedules a prompt check even after a recent success.");
            schedule.SetEnabled(false);
            require(operationToken.IsCancellationRequested, "Disabling automatic checking cancels the in-flight background request.");
            inFlight.SetResult(UpdateCheckStatus.UpToDate); await schedule.Completion;
            require(stamps.Count == 0 && completed.Count == 0,
                "A late successful response after disabling produces neither a cooldown stamp nor stale interface feedback.");
        }

        using var controller = new UpdateController(preview: true);
        controller.Dispose(); await controller.CheckAsync(); await controller.DownloadAsync();
        require(!controller.CanCheck && !controller.CanDownload && !controller.CanInstall && !controller.IsBusy
            && !controller.Message.Contains("Preview only", StringComparison.Ordinal),
            "A disposed update controller refuses late checks and downloads before reaching any transport.");
    }

    private sealed class ScheduleClock
    {
        private readonly Queue<Wait> _pending = new();
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DateTimeOffset Now { get; private set; } = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        internal int Pending => _pending.Count;
        internal Task Delay(TimeSpan delay, CancellationToken token)
        {
            var wait = new Wait(delay); _pending.Enqueue(wait);
            token.Register(() => wait.Completion.TrySetCanceled(token));
            var changed = _changed; _changed = new(TaskCreationOptions.RunContinuationsAsynchronously); changed.TrySetResult();
            return wait.Completion.Task;
        }
        internal void Advance()
        {
            var wait = _pending.Dequeue(); Now += wait.Duration; wait.Completion.TrySetResult();
        }
        internal async Task Expect(TimeSpan duration, Action<bool, string> require)
        {
            while (_pending.Count == 0) await _changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            require(_pending.Peek().Duration == duration, "Automatic check delay matches the expected startup, retry or successful daily interval.");
        }
        private sealed record Wait(TimeSpan Duration)
        {
            internal TaskCompletionSource Completion { get; } = new();
        }
    }
}
