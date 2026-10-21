using System;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Retained meter history uses deterministic telemetry and time, without windows or audio.</summary>
internal static class PeakMeterVerification
{
    internal static void Run(Action<bool, string> require)
    {
        ConsoleTheme.Initialize();
        CheckRetention(require);
        CheckClippingEvidence(require);
        CheckClearAndFreshness(require);
        CheckReducedAnimation(require);
        CheckObservationEpochs(require);
        CheckInvalidSamples(require);
        CheckNotifications(require);
        CheckClipFade(require);
        CheckSilentClipFade(require);
        CheckClipRearming(require);
        CheckClipMotionPreference(require);
        CheckReducedClipCadence(require);
    }

    private static void CheckRetention(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        require(!meter.HasPeakReadings && meter.RetainedPeakLeft == 0 && meter.RetainedPeakRight == 0 && !meter.ClipLatched,
            "A new meter has no retained readings or clipping indication.");
        meter.Update(.8, .2, 2, true, false, false, 0);
        meter.Update(.3, .7, 2, false, false, false, .01);
        require(meter.HasPeakReadings && meter.RetainedPeakLeft == .8 && meter.RetainedPeakRight == .7,
            "Stereo peak history retains each channel's independent maximum.");
        require(meter.ClipLatched && !meter.Clipped,
            "A previous clip remains latched when the current telemetry no longer reports clipping.");
        meter.Update(0, 0, 2, false, false, false, .02);
        meter.Advance(10);
        require(meter.Level == 0 && !meter.Clipped && meter.ClipLatched && meter.ClipIndicatorOpacity == 0
            && meter.RetainedPeakLeft == .8 && meter.RetainedPeakRight == .7,
            "Silence and completed bar release preserve hover peak history while the transient red cap returns to gray.");
        require(!meter.IsAnimationRunning,
            "A detached, fully decayed meter does not schedule animation for its retained clip or peaks.");

        var silent = new StereoNodeMeter();
        silent.Update(0, 0, 2, false, false, false, 0);
        require(silent.HasPeakReadings && silent.RetainedPeakLeft == 0 && silent.RetainedPeakRight == 0 && !silent.ClipLatched,
            "An observed silent sample is a valid zero reading, distinct from no readings yet.");
    }

    private static void CheckClippingEvidence(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        meter.Update(1, 1, 2, false, false, false, 0);
        require(meter.HasPeakReadings && meter.RetainedPeakLeft == 1 && meter.RetainedPeakRight == 1 && !meter.ClipLatched
            && meter.ClipIndicatorOpacity == 0,
            "A full-scale reading alone does not imply clipping.");
        meter.ClearPeakReadings();
        meter.Update(.25, .4, 2, true, false, false, .01);
        require(meter.ClipLatched && meter.RetainedPeakLeft == .25 && meter.RetainedPeakRight == .4,
            "The aggregate native clip flag latches even when both supplied peaks are below full scale.");
        meter.ClearPeakReadings();
        meter.Update(0, 0, 2, true, false, false, .02);
        require(meter.ClipLatched && meter.HasPeakReadings && meter.RetainedPeakLeft == 0 && meter.RetainedPeakRight == 0,
            "Aggregate clipping evidence does not fabricate a lane peak or require a nonzero sample.");
    }

    private static void CheckClearAndFreshness(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        var reference = new StereoNodeMeter();
        meter.Update(.8, .4, 2, true, false, false, 1);
        reference.Update(.8, .4, 2, true, false, false, 1);
        meter.ClearPeakReadings();
        require(Empty(meter) && meter.ClipIndicatorOpacity == 0 && meter.LeftLevel == reference.LeftLevel && meter.RightLevel == reference.RightLevel
            && meter.Clipped == reference.Clipped,
            "Manual reset clears retained readings without clearing live bars or instantaneous clipping.");
        meter.Advance(1.8);
        reference.Advance(1.8);
        require(meter.LeftLevel == reference.LeftLevel && meter.RightLevel == reference.RightLevel
            && meter.Clipped == reference.Clipped && Empty(meter),
            "Manual reset does not alter subsequent bar hold/release or record animated values as fresh peaks.");
        meter.Update(.8, .4, 2, true, false, false, 1.81, observePeak: false);
        require(Empty(meter) && meter.ClipIndicatorOpacity == 0 && meter.Clipped && meter.LeftLevel == .8 && meter.RightLevel == .4,
            "Reapplying old telemetry after reset can update live bars without relatching or recording peaks.");
        meter.Update(.2, .1, 2, false, false, false, 1.82);
        require(meter.HasPeakReadings && meter.RetainedPeakLeft == .2 && meter.RetainedPeakRight == .1 && !meter.ClipLatched,
            "The first fresh sample after reset starts new history rather than restoring old maxima.");
        meter.Update(.3, .2, 2, true, false, false, 1.83);
        require(meter.ClipLatched && meter.RetainedPeakLeft == .3 && meter.RetainedPeakRight == .2,
            "A new clipped sample can latch again after a manual reset.");
    }

    private static void CheckReducedAnimation(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        meter.Update(.9, .6, 2, true, false, false, 0);
        meter.Update(.1, .2, 2, false, false, true, .01, observePeak: false);
        require(meter.RetainedPeakLeft == .9 && meter.RetainedPeakRight == .6 && meter.ClipLatched,
            "Enabling reduced animation preserves observed peaks and clipping history.");
        meter.Update(.1, .2, 2, false, false, false, .02, observePeak: false);
        require(meter.RetainedPeakLeft == .9 && meter.RetainedPeakRight == .6 && meter.ClipLatched,
            "Disabling reduced animation preserves observed peaks and clipping history.");

        var pending = new StereoNodeMeter();
        var reference = new StereoNodeMeter();
        foreach (var current in new[] { pending, reference })
        {
            current.Update(.1, .2, 2, false, false, true, 0);
            current.Update(.8, .7, 2, false, false, true, .01);
        }
        require(pending.LeftLevel == .1 && pending.RetainedPeakLeft == .8 && pending.RetainedPeakRight == .7,
            "Reduced animation records a fresh transient before its delayed bar presentation.");
        pending.ClearPeakReadings();
        pending.Advance(.25);
        reference.Advance(.25);
        require(Empty(pending) && pending.LeftLevel == .8 && pending.RightLevel == .7
            && pending.LeftLevel == reference.LeftLevel && pending.RightLevel == reference.RightLevel,
            "Reset preserves pending reduced-animation bar updates without re-recording those old samples.");
    }

    private static void CheckObservationEpochs(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        meter.Update(.9, .8, 2, true, false, false, 10);
        meter.Update(.9, .8, 2, true, true, false, 10.1, observePeak: false);
        require(Empty(meter) && meter.ClipIndicatorOpacity == 0 && meter.Level == 0 && !meter.Clipped && meter.Muted,
            "Mute clears retained history and the clip latch even when the sample is not eligible for observation.");
        meter.Update(.2, .3, 2, false, false, false, 10.2);
        require(meter.HasPeakReadings && meter.RetainedPeakLeft == .2 && meter.RetainedPeakRight == .3 && !meter.ClipLatched,
            "Unmuting starts with the newly observed peaks rather than the preceding muted epoch.");
        meter.Update(.4, .9, 1, false, false, false, 10.3);
        require(meter.Channels == 1 && meter.HasPeakReadings && meter.RetainedPeakLeft == .4 && meter.RetainedPeakRight == 0
            && meter.ClipIndicatorOpacity == 0,
            "Changing to mono clears stereo history and ignores the unavailable right channel.");
        meter.Update(.5, .6, 2, true, false, false, 10.4, observePeak: false);
        require(Empty(meter) && meter.ClipIndicatorOpacity == 0, "A channel change clears old history and the red cap even when the replacement sample is not fresh.");
        meter.Update(.5, .6, 2, true, false, false, 10.5);
        meter.Update(.1, .2, 2, false, false, false, 9);
        require(meter.HasPeakReadings && meter.RetainedPeakLeft == .1 && meter.RetainedPeakRight == .2 && !meter.ClipLatched
            && meter.ClipIndicatorOpacity == 0,
            "A backwards sample clock starts a new history epoch and records only that new sample.");
        meter.Update(.4, .5, 2, true, false, false, 8, observePeak: false);
        require(Empty(meter) && meter.ClipIndicatorOpacity == 0, "A backwards clock clears history and the red cap without recording an ineligible sample.");
    }

    private static void CheckInvalidSamples(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        meter.Update(double.NaN, double.PositiveInfinity, 2, false, false, false, 0);
        require(meter.HasPeakReadings && meter.RetainedPeakLeft == 0 && meter.RetainedPeakRight == 0,
            "Nonfinite samples produce safe zero peak readings.");
        meter.Update(double.NegativeInfinity, -2, 2, false, false, false, double.NaN);
        require(meter.RetainedPeakLeft == 0 && meter.RetainedPeakRight == 0 && double.IsFinite(meter.Level),
            "Negative samples and an invalid timestamp cannot poison retained readings or live meter state.");
        meter.Update(4, double.MaxValue, 2, true, false, false, 1);
        require(meter.RetainedPeakLeft == 1 && meter.RetainedPeakRight == 1 && meter.ClipLatched,
            "Finite oversized samples clamp retained readings to full scale while retaining clipping evidence.");
        require(meter.LeftLevel == 4 && meter.RightLevel == double.MaxValue,
            "Retained peak bounds do not change the existing instantaneous meter sample behavior.");
        meter.ClearPeakReadings();
        meter.Update(.25, .9, 1, false, false, false, 2);
        require(meter.RetainedPeakLeft == .25 && meter.RetainedPeakRight == 0,
            "Mono peak readings never fabricate a second channel from the unused input value.");
    }

    private static void CheckNotifications(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        int changes = 0;
        meter.PeakReadingsChanged += (_, _) => changes++;
        meter.ClearPeakReadings();
        require(changes == 0, "Resetting an empty meter does not emit a redundant peak-history event.");
        meter.Update(0, 0, 2, false, false, false, 0);
        require(changes == 1, "The first silent observation notifies that peak readings are now available.");
        meter.Update(.5, .4, 2, false, false, false, .01);
        require(changes == 2, "A fresh stereo maximum emits one peak-history event for the combined state change.");
        meter.Update(.2, .3, 2, false, false, false, .02);
        meter.Update(.5, .4, 2, false, false, false, .03);
        meter.Advance(.04);
        require(changes == 2, "Quieter/repeated samples and animated release do not repeatedly notify unchanged retained history.");
        meter.Update(.2, .3, 2, true, false, false, .05);
        require(changes == 3, "New clipping evidence emits an event even without a larger peak reading.");
        meter.Update(.2, .3, 2, true, false, false, .06);
        require(changes == 3, "A repeated clip flag does not repeatedly notify an already latched clip.");
        meter.ClearPeakReadings();
        require(changes == 4 && Empty(meter), "Manual reset emits one event for clearing peaks and the clip latch together.");
        meter.ClearPeakReadings();
        meter.Update(.5, .4, 2, true, false, false, .07, observePeak: false);
        require(changes == 4 && Empty(meter), "Repeated reset and replayed telemetry leave cleared history and its event count unchanged.");
        meter.Update(0, 0, 2, false, false, false, .08);
        meter.Update(0, 0, 1, false, false, false, .09);
        require(changes == 6 && meter.HasPeakReadings && meter.RetainedPeakLeft == 0 && meter.RetainedPeakRight == 0,
            "A silent stereo-to-mono change notifies updated channel metadata even when retained amplitudes remain zero.");
        meter.Update(0, 0, 2, false, false, false, .1);
        require(changes == 7, "A silent mono-to-stereo change emits one metadata update for the second reading.");
    }

    private static void CheckClipFade(Action<bool, string> require)
    {
        require(StereoNodeMeter.ClipHoldSeconds == 2 && StereoNodeMeter.ClipFadeSeconds == 1,
            "The clipping cap has a two-second hold followed by a one-second fade.");
        foreach (bool reduced in new[] { false, true })
        {
            var meter = new StereoNodeMeter();
            int historyChanges = 0;
            meter.PeakReadingsChanged += (_, _) => historyChanges++;
            meter.Update(.8, .4, 2, true, false, reduced, 0);
            require(meter.ClipIndicatorOpacity == 1, "Fresh clipping lights the red cap immediately in either motion mode.");
            meter.Update(0, 0, 2, false, false, reduced, .01);
            double previous = 1;
            bool sawPartialFade = false;
            double end = StereoNodeMeter.ClipHoldSeconds + StereoNodeMeter.ClipFadeSeconds;
            // Quarter-second samples also exercise the reduced-animation cadence.
            for (double time = .25; time <= end; time += .25)
            {
                meter.Advance(time);
                double opacity = meter.ClipIndicatorOpacity;
                require(double.IsFinite(opacity) && opacity >= 0 && opacity <= previous,
                    "A clipping cap fades monotonically within its safe opacity range, including reduced animation.");
                if (time <= StereoNodeMeter.ClipHoldSeconds)
                    require(opacity == 1, "The clipping cap remains fully red during its brief hold despite subsequent silence.");
                else if (time < end)
                {
                    require(opacity > 0 && opacity < 1, "The cap visibly fades instead of switching directly from red to gray.");
                    sawPartialFade = true;
                }
                previous = opacity;
            }
            require(sawPartialFade && meter.ClipIndicatorOpacity == 0 && !meter.Clipped
                && meter.ClipLatched && meter.RetainedPeakLeft == .8 && meter.RetainedPeakRight == .4 && historyChanges == 1,
                "At the fade deadline the cap is gray, but hover maxima and clipping history remain unchanged without repeated history events.");
            meter.Advance(end + 10);
            require(meter.ClipIndicatorOpacity == 0 && meter.Level == 0 && !meter.IsAnimationRunning,
                "After the cap and bars finish decaying, a detached meter has no continuing animation despite retained history.");
        }
    }

    private static void CheckClipRearming(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        double firstFade = StereoNodeMeter.ClipHoldSeconds + StereoNodeMeter.ClipFadeSeconds / 2;
        meter.Update(.7, .3, 2, true, false, false, 0);
        meter.Advance(firstFade);
        require(meter.ClipIndicatorOpacity > 0 && meter.ClipIndicatorOpacity < 1, "A new clipping event can arrive while the previous cap is fading.");
        meter.Update(.6, .2, 2, true, false, false, firstFade);
        require(meter.ClipIndicatorOpacity == 1, "Fresh clipping during a fade immediately restores full red and starts a new hold.");
        meter.Advance(firstFade + StereoNodeMeter.ClipHoldSeconds);
        require(meter.ClipIndicatorOpacity == 1, "The new hold is measured from the latest fresh clipping event.");
        meter.Update(.6, .2, 2, true, false, false,
            firstFade + StereoNodeMeter.ClipHoldSeconds + StereoNodeMeter.ClipFadeSeconds / 2, observePeak: false);
        require(meter.ClipIndicatorOpacity > 0 && meter.ClipIndicatorOpacity < 1,
            "Repainting an old clipped packet cannot rearm a cap that is already fading.");
        double end = firstFade + StereoNodeMeter.ClipHoldSeconds + StereoNodeMeter.ClipFadeSeconds;
        meter.Advance(end);
        meter.Update(.6, .2, 2, true, false, false, end + .1, observePeak: false);
        require(meter.ClipIndicatorOpacity == 0 && meter.ClipLatched,
            "An old clipped packet cannot restore a cap after its fade has completed, although hover history remains.");
        meter.Update(1, 1, 2, false, false, false, end + .2);
        require(meter.ClipIndicatorOpacity == 0, "Full scale without explicit clipping evidence cannot light a faded cap.");
        meter.Update(.2, .1, 2, true, false, false, end + .3);
        require(meter.ClipIndicatorOpacity == 1, "A genuinely new clipped packet lights the cap again after it has returned to gray.");
        meter.ClearPeakReadings();
        meter.Advance(end + .4);
        require(meter.ClipIndicatorOpacity == 0 && Empty(meter), "Internal history clearing cancels an active hold/fade without leaving a delayed red cap.");
    }

    private static void CheckSilentClipFade(Action<bool, string> require)
    {
        foreach (bool reduced in new[] { false, true })
        {
            var meter = new StereoNodeMeter();
            meter.Update(0, 0, 2, true, false, reduced, 10);
            require(meter.Level == 0 && meter.ClipIndicatorOpacity == 1,
                "Aggregate clipping evidence lights the cap even when both supplied peak samples are zero.");
            // No following telemetry arrives; only display time advances.
            meter.Advance(10 + StereoNodeMeter.ClipHoldSeconds);
            require(meter.Level == 0 && !meter.Clipped && meter.ClipIndicatorOpacity == 1,
                "A silent cap outlives the instantaneous clip flag and reaches its hold boundary without additional telemetry.");
            meter.Advance(10 + StereoNodeMeter.ClipHoldSeconds + StereoNodeMeter.ClipFadeSeconds / 2);
            require(meter.Level == 0 && meter.ClipIndicatorOpacity > 0 && meter.ClipIndicatorOpacity < 1,
                "The cap alone continues fading when no bar is moving and no new samples arrive.");
            meter.Advance(10 + StereoNodeMeter.ClipHoldSeconds + StereoNodeMeter.ClipFadeSeconds);
            require(meter.Level == 0 && meter.ClipIndicatorOpacity == 0 && meter.ClipLatched
                && meter.HasPeakReadings && !meter.IsAnimationRunning,
                "A silent cap completes its fade and leaves only informational history, with no detached animation remaining.");
        }
    }

    private static void CheckClipMotionPreference(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        meter.Update(.8, .3, 2, true, false, false, 0);
        meter.Update(.1, .1, 2, false, false, true, 1, observePeak: false);
        require(meter.ClipIndicatorOpacity == 1, "Enabling reduced animation preserves the current clipping hold.");
        double middle = StereoNodeMeter.ClipHoldSeconds + StereoNodeMeter.ClipFadeSeconds / 2;
        meter.Advance(middle);
        double partial = meter.ClipIndicatorOpacity;
        meter.Update(.1, .1, 2, false, false, false, middle + .1, observePeak: false);
        require(meter.ClipIndicatorOpacity > 0 && meter.ClipIndicatorOpacity <= partial,
            "Disabling reduced animation during the fade preserves elapsed time rather than restarting or clearing the cap.");
        meter.Advance(StereoNodeMeter.ClipHoldSeconds + StereoNodeMeter.ClipFadeSeconds);
        require(meter.ClipIndicatorOpacity == 0 && meter.ClipLatched && meter.RetainedPeakLeft == .8,
            "Motion-preference changes do not extend the cap deadline or discard retained hover history.");
    }

    private static void CheckReducedClipCadence(Action<bool, string> require)
    {
        var meter = new StereoNodeMeter();
        meter.Update(.8, .3, 2, true, false, true, 0);
        double fade = StereoNodeMeter.ClipHoldSeconds;
        meter.Advance(fade + .25);
        double firstStep = meter.ClipIndicatorOpacity;
        require(firstStep > 0 && firstStep < 1, "The reduced-animation cap reaches a visible fade step after one quarter second.");
        for (int update = 1; update <= 6; update++)
        {
            bool fresh = update % 2 == 0;
            meter.Update(.1, .1, 2, clipped: !fresh, muted: false, reduced: true,
                now: fade + .25 + update * .033, observePeak: fresh);
            require(meter.ClipIndicatorOpacity == firstStep,
                "Frequent fresh nonclipped telemetry and stale clipped packets cannot redraw the reduced cap within one fade step.");
        }
        meter.Update(.1, .1, 2, false, false, true, fade + .5);
        require(meter.ClipIndicatorOpacity > 0 && meter.ClipIndicatorOpacity < firstStep,
            "The reduced cap changes at the next quarter-second fade boundary.");
        meter.Update(.2, .1, 2, true, false, true, fade + .55);
        require(meter.ClipIndicatorOpacity == 1,
            "Fresh clipping still lights the reduced-animation cap immediately between scheduled fade steps.");

        var switching = new StereoNodeMeter();
        switching.Update(.8, .3, 2, true, false, false, 0);
        switching.Advance(fade + .33);
        double beforeSwitch = switching.ClipIndicatorOpacity;
        switching.Update(.1, .1, 2, false, false, true, fade + .34, observePeak: false);
        require(switching.ClipIndicatorOpacity == beforeSwitch,
            "Switching into reduced animation between fade boundaries cannot brighten an already dimmer cap.");
        switching.Update(.1, .1, 2, false, false, true, fade + .45);
        require(switching.ClipIndicatorOpacity == beforeSwitch,
            "The mode switch holds the existing cap shade until the reduced fade catches up.");
        switching.Advance(fade + .5);
        require(switching.ClipIndicatorOpacity > 0 && switching.ClipIndicatorOpacity < beforeSwitch,
            "The next reduced fade boundary resumes dimming after a mid-step mode switch.");
    }

    private static bool Empty(StereoNodeMeter meter)
        => !meter.HasPeakReadings && meter.RetainedPeakLeft == 0 && meter.RetainedPeakRight == 0 && !meter.ClipLatched;
}
