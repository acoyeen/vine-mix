//! Distortion: how the four controls become processing amounts.
//!
//! The node stores Drive, Tone, Mix and Output. This module turns them into
//! the gains the processor in `vinemix-dsp` runs with, and holds the constants
//! and the transfer curve the two crates share.
//!
//! The processor brings the sound to a standard level before the curve and
//! takes it back afterwards, so Drive means the same thing at any input
//! level. The curve therefore always sees the same kind of signal, and the
//! gain that returns it to its original loudness depends on Drive alone: that
//! is [`makeup`], worked out here when the configuration is compiled.

use std::f64::consts::{PI, SQRT_2};

use crate::config::DistortionSettings;
use crate::SAMPLE_RATE;

/// Only the sound below this first-order split is driven; what lies above it
/// passes clean, so sibilants are not torn into noise.
pub const SPLIT_HZ: f64 = 5_000.0;
/// The lows are turned down by half before the curve, from the second of
/// these frequencies down to the first, and restored after it. The voice's
/// fundamental then modulates the rest of the voice less.
pub const EMPHASIS_FROM_HZ: f64 = 150.0;
pub const EMPHASIS_TO_HZ: f64 = 300.0;
/// Where the driven sound's level (its mean power while it sounds, in dBFS)
/// is brought to before the curve at Drive 0 dB. The curve's knee is at 0.
pub const STANDARD_DB: f64 = -22.0;
/// The curve's working point. Away from zero the two half-waves are treated
/// differently, which adds even harmonics.
pub const BIAS: f64 = 0.25;

fn sigmoid(v: f64) -> f64 {
    v / (1.0 + v * v).sqrt()
}

/// The curve's output for an input of zero before it is re-centred, and the
/// reciprocal of its slope there.
pub fn offset() -> f64 {
    sigmoid(BIAS)
}

pub fn inverse_slope() -> f64 {
    (1.0 + BIAS * BIAS).powf(1.5)
}

/// The transfer curve: `v / sqrt(1 + v²)` about the working point, moved and
/// scaled so that it passes through zero with a slope of one. It is smooth,
/// monotonic, and levels off at about +0.83 and -1.36.
pub fn curve(u: f64) -> f64 {
    (sigmoid(u + BIAS) - offset()) * inverse_slope()
}

/// The gain after the curve that returns a speech-like sound at the standard
/// level to its original level, for a linear Drive gain.
///
/// "Speech-like" is a Laplace distribution of sample values, which the long
/// run of speech follows closely. The expectation is taken by the midpoint
/// rule over the distribution's quantiles.
pub fn makeup(drive: f64) -> f64 {
    const POINTS: usize = 1_024;
    let standard = 10f64.powf(STANDARD_DB / 20.0);
    let scale = standard / SQRT_2;
    let (mut mean, mut square) = (0.0, 0.0);
    for point in 0..POINTS {
        let quantile = (point as f64 + 0.5) / POINTS as f64;
        let u = -scale * quantile.ln() * drive;
        let (up, down) = (curve(u), curve(-u));
        mean += 0.5 * (up + down);
        square += 0.5 * (up * up + down * down);
    }
    mean /= POINTS as f64;
    square /= POINTS as f64;
    // The steady offset of an uneven curve is removed by the processor.
    standard / (square - mean * mean).max(1e-30).sqrt()
}

/// The processing amounts for one group of settings. `Copy`, so the mixer
/// thread can store and compare it without touching the heap.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Saturation {
    /// Gain into the curve, on top of the level standardisation, and the
    /// gain after it (see [`makeup`]). Both are in decibels: the processor
    /// glides them together, and in decibels the makeup stays close to the
    /// one that belongs to the Drive of the moment.
    pub drive_db: f64,
    pub makeup_db: f64,
    /// Pre-warped corner of the Tone low-pass, `tan(pi · Hz / rate)`.
    pub tone: f64,
    /// Share of the driven sound in the blend, 0 to 1.
    pub mix: f64,
    /// Linear output trim.
    pub trim: f64,
}

impl Saturation {
    /// No drive and none of it in the blend: the placeholder for nodes that
    /// are not a Distortion.
    pub const CLEAN: Self = Self { drive_db: 0.0, makeup_db: 0.0, tone: 1.0, mix: 0.0, trim: 1.0 };

    pub fn of(settings: &DistortionSettings) -> Self {
        Self {
            drive_db: settings.drive_db,
            makeup_db: 20.0 * makeup(10f64.powf(settings.drive_db / 20.0)).log10(),
            tone: (PI * settings.tone_hz / f64::from(SAMPLE_RATE)).tan(),
            mix: settings.mix,
            trim: 10f64.powf(settings.output_db / 20.0),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_curve_is_centred_smooth_and_bounded() {
        assert_eq!(curve(0.0), 0.0);
        let slope = (curve(1e-6) - curve(-1e-6)) / 2e-6;
        assert!((slope - 1.0).abs() < 1e-9, "slope at rest {slope}");
        let mut previous = curve(-200.0);
        let mut u = -200.0;
        while u < 200.0 {
            u += 0.01;
            let now = curve(u);
            assert!(now > previous, "not monotonic at {u}");
            previous = now;
        }
        let (top, bottom) = (curve(1e9), curve(-1e9));
        assert!((top - 0.829_7).abs() < 1e-3 && (bottom + 1.360_8).abs() < 1e-3, "limits {top} and {bottom}");
    }

    #[test]
    fn makeup_returns_the_standard_signal_to_its_level() {
        // With next to no drive the curve is a straight line.
        let quiet = makeup(0.01);
        assert!((quiet * 0.01 - 1.0).abs() < 0.01, "small-signal gain {}", quiet * 0.01);
        // More drive needs less gain afterwards, without steps.
        let mut previous = makeup(1.0);
        assert!(previous > 0.98 && previous < 1.1, "makeup at Drive 0 dB is {previous}");
        for step in 1..=360 {
            let now = makeup(10f64.powf(f64::from(step) / 200.0));
            assert!(now < previous && now > previous * 0.97, "step {step}: {previous} then {now}");
            previous = now;
        }
        // At full drive the output sits near the curve's limits whatever the
        // input does, so the makeup approaches the standard level over them.
        let standard = 10f64.powf(STANDARD_DB / 20.0);
        assert!(previous > standard / 1.4 && previous < standard / 0.8, "makeup at Drive 36 dB is {previous}");
    }

    #[test]
    fn settings_become_processing_amounts() {
        let s = Saturation::of(&DistortionSettings { drive_db: 20.0, tone_hz: 12_000.0, mix: 0.25, output_db: -6.0 });
        assert_eq!(s.drive_db, 20.0);
        assert!((s.makeup_db - 20.0 * makeup(10.0).log10()).abs() < 1e-12);
        assert!((s.tone - 1.0).abs() < 1e-12, "a quarter of the sample rate pre-warps to one");
        assert_eq!(s.mix, 0.25);
        assert!((s.trim - 0.501_187_233_627_272_2).abs() < 1e-12);
    }

    /// Halfway through a glide from one Drive to another, the makeup in use
    /// is the mean of the two in decibels. It is never far from the makeup
    /// that belongs to the Drive at that moment, so a large jump of the dial
    /// does not arrive as a burst of level.
    #[test]
    fn makeup_follows_drive_through_a_glide() {
        let makeup_db = |drive_db: f64| 20.0 * makeup(10f64.powf(drive_db / 20.0)).log10();
        for (from, to) in [(0.0, 36.0), (0.0, 12.0), (12.0, 36.0), (24.0, 30.0)] {
            for step in 0..=20 {
                let share = f64::from(step) / 20.0;
                let gliding = makeup_db(from) + (makeup_db(to) - makeup_db(from)) * share;
                let exact = makeup_db(from + (to - from) * share);
                assert!((gliding - exact).abs() < 5.0, "{from} to {to} dB at {share}: {gliding:.2} against {exact:.2}");
            }
        }
    }

    /// Prints the makeup against Drive; run with `-- --ignored --nocapture`.
    #[test]
    #[ignore = "prints a table"]
    fn makeup_table() {
        for db in [0.0, 3.0, 6.0, 12.0, 18.0, 24.0, 30.0, 36.0] {
            let drive = 10f64.powf(db / 20.0);
            let m = makeup(drive);
            println!("drive {db:>4.0} dB: makeup {:>7.2} dB, quiet-signal gain {:>6.2} dB", 20.0 * m.log10(), 20.0 * (m * drive).log10());
        }
    }
}
