//! Distortion: saturation from gentle warmth to strong.
//!
//! An original VineMix design; it has no counterpart in the 0.5 C++ engine and
//! is not checked against it. Per frame:
//!
//! 1. The sound below a first-order split is taken for driving; its lows are
//!    turned down before the curve and restored after it. What lies above the
//!    split passes clean.
//! 2. That band is brought to a standard level, learned from the sound itself
//!    while it plays, so Drive acts the same way at any input level.
//! 3. A smooth, slightly uneven curve saturates it. The curve is applied as
//!    its exact average between consecutive samples (first-order
//!    antiderivative antialiasing: Parker, Zavalishin and Le Bivic, 2016;
//!    Bilbao, Esqueda, Parker and Välimäki, 2017), which needs no oversampling
//!    and no delay line.
//! 4. The result returns to the original level with a makeup gain compiled
//!    from Drive, so Drive changes the character and not the loudness. Sound
//!    far below the learned level (the room between words) is not raised.
//! 5. Tone, Mix, a ceiling that stops the node adding peaks above -1 dBFS (or
//!    above the input's own peak, if that is higher), and Output.
//!
//! The driven band is added as the difference from its own undriven copy, so
//! the dry path is the input itself: nothing is delayed, and at Mix 0% with
//! Output 0 dB the output equals the input. All state is plain data in this
//! struct; `process` and `configure` are bounded and never allocate.

use std::f64::consts::PI;

use vinemix_model::distortion::{self, Saturation};
use vinemix_model::SAMPLE_RATE;
use vinemix_rt::Frame;

use super::filters::{OnePole, Pair};
use crate::util::{denormal, frame, Smooth};

/// Frames between level decisions (a third of a millisecond).
const CONTROL: u32 = 16;

/// One-pole coefficients, `exp(-1 / (seconds · rate))`; see `constants_match_their_times`.
const DETECTOR: f64 = 0.995_842_001_845_11; // 5 ms at 48 kHz
const INPUT_RELEASE: f64 = 0.999_791_688_366_548_6; // 100 ms
const LIMIT_RELEASE: f64 = 0.999_739_617_238_810_2; // 80 ms
const LEAK: f64 = 0.999_983_333_472_221_5; // 20 s at the control rate

/// The level is the mean power of the driven band while it sounds: anything
/// above [`GATE`] (-70 dBFS) and within 20 dB of the level so far counts.
/// Quieter sound lets the level leak away slowly instead, so the node can
/// follow a source that has dropped for good.
const GATE: f64 = 1e-7;
const RELATIVE_GATE: f64 = 0.01;
/// The level is never left more than 15 dB below what is sounding now: a
/// source that much louder is taken up at once, keeping only the last 25 ms.
const SURPRISE: f64 = 32.0;
const RELEARN: u32 = 75;
/// Control ticks of sound until the learned level is fully in use (0.25 s)
/// and until the average reaches its long time constant (2.5 s).
const CONFIDENT: u32 = 750;
const LEARNED: u32 = 7_500;
/// The standardising gain never exceeds this, either way.
const MAXIMUM_NORMALISE_DB: f64 = 36.0;

/// Sound this far below the learned level (dB) is driven in full, and sound
/// that far below is left as it is, so the room between words is not raised.
const LIFT_FULL: f64 = -30.0;
const LIFT_NONE: f64 = -45.0;

const DC_HZ: f64 = 20.0;
/// The node never adds a peak above this (-1 dBFS).
const CEILING: f64 = 0.891_250_938_133_745_6;

fn smoothstep(x: f64) -> f64 {
    let x = x.clamp(0.0, 1.0);
    x * x * (3.0 - 2.0 * x)
}

/// A first-order section, `(b0 + b1·z⁻¹) / (1 + a1·z⁻¹)`.
#[derive(Clone, Copy, Debug, PartialEq)]
struct FirstOrder {
    b0: f64,
    b1: f64,
    a1: f64,
    state: Pair,
}

impl FirstOrder {
    /// A shelf that halves everything below `from` relative to everything
    /// above `to` (`(s + from) / (s + to)`), and its exact inverse.
    fn shelves(from_hz: f64, to_hz: f64) -> (Self, Self) {
        let zero = (PI * from_hz / f64::from(SAMPLE_RATE)).tan();
        let pole = (PI * to_hz / f64::from(SAMPLE_RATE)).tan();
        let section = |n: f64, d: f64| Self { b0: (1.0 + n) / (1.0 + d), b1: (n - 1.0) / (1.0 + d), a1: (d - 1.0) / (1.0 + d), state: [0.0; 2] };
        (section(zero, pole), section(pole, zero))
    }

    #[inline]
    fn step(&mut self, x: Pair) -> Pair {
        let mut out = [0.0; 2];
        for c in 0..2 {
            out[c] = self.b0 * x[c] + self.state[c];
            self.state[c] = denormal(self.b1 * x[c] - self.a1 * out[c]);
        }
        out
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Distortion {
    split: OnePole,
    emphasis: FirstOrder,
    restore: FirstOrder,
    dc: OnePole,
    tone_state: Pair,
    /// The curve's working point terms, and the previous frame's curve input,
    /// its `sqrt(1 + v²)` and the undriven band.
    offset: f64,
    inverse_slope: f64,
    shaped_input: Pair,
    shaped_root: Pair,
    band: Pair,
    /// Drive and makeup glide in decibels; the linear gains are worked out
    /// again only while one of them is moving.
    drive: Smooth,
    makeup: Smooth,
    drive_db: f64,
    makeup_db: f64,
    drive_gain: f64,
    makeup_gain: f64,
    tone: Smooth,
    mix: Smooth,
    trim: Smooth,
    /// Short-term power of the driven band, and its level: the mean of that
    /// power while it sounds.
    power: f64,
    level: f64,
    /// Control ticks in the level average, and since the first sound.
    ticks: u32,
    heard: u32,
    /// Gain to the standard level and back, and the share of the driven
    /// difference in effect; each moves in a straight line between decisions.
    normalise: f64,
    normalise_target: f64,
    normalise_step: f64,
    restore_gain: f64,
    restore_target: f64,
    restore_step: f64,
    lift: f64,
    lift_target: f64,
    lift_step: f64,
    input_envelope: f64,
    limit: f64,
    countdown: u32,
}

impl Distortion {
    pub fn new() -> Self {
        let (emphasis, restore) = FirstOrder::shelves(distortion::EMPHASIS_FROM_HZ, distortion::EMPHASIS_TO_HZ);
        Self {
            split: OnePole::new(distortion::SPLIT_HZ),
            emphasis,
            restore,
            dc: OnePole::new(DC_HZ),
            tone_state: [0.0; 2],
            offset: distortion::offset(),
            inverse_slope: distortion::inverse_slope(),
            // The curve at rest: an input of zero sits at the working point.
            shaped_input: [distortion::BIAS; 2],
            shaped_root: [(1.0 + distortion::BIAS * distortion::BIAS).sqrt(); 2],
            band: [0.0; 2],
            drive: Smooth::default(),
            makeup: Smooth::default(),
            drive_db: 0.0,
            makeup_db: 0.0,
            drive_gain: 1.0,
            makeup_gain: 1.0,
            tone: Smooth::default(),
            mix: Smooth::default(),
            trim: Smooth::default(),
            power: 0.0,
            level: 0.0,
            ticks: 0,
            heard: 0,
            normalise: 1.0,
            normalise_target: 1.0,
            normalise_step: 0.0,
            restore_gain: 1.0,
            restore_target: 1.0,
            restore_step: 0.0,
            // Nothing is driven until something has been heard.
            lift: 0.0,
            lift_target: 0.0,
            lift_step: 0.0,
            input_envelope: 0.0,
            limit: 1.0,
            countdown: 0,
        }
    }

    /// Retargets the controls. Filter state and the learned level carry on;
    /// each amount glides to its new value over 10 ms.
    pub fn configure(&mut self, setting: &Saturation) {
        self.drive.set(setting.drive_db);
        self.makeup.set(setting.makeup_db);
        self.tone.set(setting.tone);
        self.mix.set(setting.mix);
        self.trim.set(setting.trim);
    }

    /// Level decisions, every [`CONTROL`] frames.
    fn control(&mut self) {
        // Land exactly on the previous targets rather than on a sum of steps.
        self.normalise = self.normalise_target;
        self.restore_gain = self.restore_target;
        self.lift = self.lift_target;
        if self.power > GATE {
            if self.ticks != 0 && self.power > self.level * SURPRISE {
                // Clearly a louder source than the one learned (speech after
                // a wait in a quiet room): take it up at once.
                self.level = self.power / SURPRISE;
                self.ticks = self.ticks.min(RELEARN);
            }
            if self.ticks == 0 || self.power > self.level * RELATIVE_GATE {
                // A running mean while learning, then a 2.5 s average.
                self.ticks = (self.ticks + 1).min(LEARNED);
                self.level += (self.power - self.level) / f64::from(self.ticks);
            } else {
                self.level *= LEAK;
            }
        }
        if self.ticks == 0 {
            // Nothing heard yet: stay at rest.
            self.normalise_step = 0.0;
            self.restore_step = 0.0;
            self.lift_step = 0.0;
            return;
        }
        // Start from the input as it is and move to the learned level as it
        // becomes trustworthy, so the first sound is not driven by a guess.
        self.heard = (self.heard + 1).min(CONFIDENT);
        let confidence = f64::from(self.heard) / f64::from(CONFIDENT);
        let level_db = 10.0 * self.level.log10();
        let normalise_db = (confidence * (distortion::STANDARD_DB - level_db)).clamp(-MAXIMUM_NORMALISE_DB, MAXIMUM_NORMALISE_DB);
        self.normalise_target = 10f64.powf(normalise_db / 20.0);
        self.restore_target = 1.0 / self.normalise_target;
        let relative = 10.0 * self.power.max(1e-30).log10() - level_db;
        self.lift_target = smoothstep((relative - LIFT_NONE) / (LIFT_FULL - LIFT_NONE));
        let steps = f64::from(CONTROL);
        self.normalise_step = (self.normalise_target - self.normalise) / steps;
        self.restore_step = (self.restore_target - self.restore_gain) / steps;
        self.lift_step = (self.lift_target - self.lift) / steps;
    }

    pub fn process(&mut self, input: Frame) -> Frame {
        let x: Pair = [f64::from(input.left), f64::from(input.right)];
        let (drive_db, makeup_db) = (self.drive.next(), self.makeup.next());
        if drive_db != self.drive_db || makeup_db != self.makeup_db {
            self.drive_db = drive_db;
            self.makeup_db = makeup_db;
            self.drive_gain = 10f64.powf(drive_db / 20.0);
            self.makeup_gain = 10f64.powf(makeup_db / 20.0);
        }
        let tone = self.tone.next();
        let amount = self.mix.next();
        let trim = self.trim.next();

        // The band to drive, with its lows turned down.
        let band = self.emphasis.step(self.split.low(x));
        let loudest = (band[0] * band[0]).max(band[1] * band[1]);
        self.power = denormal(loudest + DETECTOR * (self.power - loudest));
        if self.countdown == 0 {
            self.control();
            self.countdown = CONTROL;
        }
        self.countdown -= 1;
        self.normalise += self.normalise_step;
        self.restore_gain += self.restore_step;
        self.lift += self.lift_step;

        // The curve's exact average between the previous sample and this one,
        // less the same average of the undriven band: what driving changed.
        let before = self.drive_gain * self.normalise;
        let after = self.makeup_gain * self.restore_gain;
        let mut difference = [0.0; 2];
        for c in 0..2 {
            let v = band[c] * before + distortion::BIAS;
            let root = (1.0 + v * v).sqrt();
            let shaped = ((v + self.shaped_input[c]) / (root + self.shaped_root[c]) - self.offset) * self.inverse_slope;
            difference[c] = after * shaped - 0.5 * (band[c] + self.band[c]);
            self.shaped_input[c] = v;
            self.shaped_root[c] = root;
            self.band[c] = band[c];
        }
        // An uneven curve leaves a steady offset; remove it before the lows
        // are restored.
        let steady = self.dc.low(difference);
        for c in 0..2 {
            difference[c] = (difference[c] - steady[c]) * self.lift;
        }
        let difference = self.restore.step(difference);

        // Tone on the driven sound, then the blend with the input itself.
        let k = tone / (1.0 + tone);
        let mut mixed = [0.0; 2];
        for c in 0..2 {
            let driven = x[c] + difference[c];
            let step = (driven - self.tone_state[c]) * k;
            let low = step + self.tone_state[c];
            self.tone_state[c] = denormal(low + step);
            mixed[c] = x[c] + (low - x[c]) * amount;
        }

        // Never add a peak above the ceiling, or above the input's own peak.
        self.input_envelope = denormal(x[0].abs().max(x[1].abs()).max(self.input_envelope * INPUT_RELEASE));
        let allowed = CEILING.max(self.input_envelope);
        let peak = mixed[0].abs().max(mixed[1].abs());
        let recovered = 1.0 - (1.0 - self.limit) * LIMIT_RELEASE;
        self.limit = if peak * recovered > allowed { allowed / peak } else { recovered };
        let gain = self.limit * trim;
        frame(mixed[0] * gain, mixed[1] * gain)
    }

    pub fn reset(&mut self) {
        *self = Self::new();
    }
}

impl Default for Distortion {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn constants_match_their_times() {
        let per_frame = |seconds: f64| (-1.0 / (seconds * 48_000.0)).exp();
        const CONTROL_RATE: f64 = 48_000.0 / CONTROL as f64;
        let close = |a: f64, b: f64| assert!((a - b).abs() < 1e-12, "{a} vs {b}");
        close(DETECTOR, per_frame(0.005));
        close(INPUT_RELEASE, per_frame(0.1));
        close(LIMIT_RELEASE, per_frame(0.08));
        close(LEAK, (-1.0 / (20.0 * CONTROL_RATE)).exp());
        close(CEILING, 10f64.powf(-1.0 / 20.0));
        close(f64::from(LEARNED) / CONTROL_RATE, 2.5);
        close(f64::from(CONFIDENT) / CONTROL_RATE, 0.25);
    }

    #[test]
    fn the_shelves_undo_each_other() {
        let (mut cut, mut restore) = FirstOrder::shelves(distortion::EMPHASIS_FROM_HZ, distortion::EMPHASIS_TO_HZ);
        let mut seed = 1u32;
        let mut worst = 0.0f64;
        for _ in 0..48_000 {
            seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
            let x = f64::from(seed >> 8) / f64::from(1u32 << 23) - 1.0;
            let y = restore.step(cut.step([x, -x]));
            worst = worst.max((y[0] - x).abs()).max((y[1] + x).abs());
        }
        assert!(worst < 1e-12, "the pair leaves an error of {worst}");
        // Half at the bottom, unity at the top.
        let (mut cut, _) = FirstOrder::shelves(distortion::EMPHASIS_FROM_HZ, distortion::EMPHASIS_TO_HZ);
        let mut low = 0.0;
        for _ in 0..48_000 {
            low = cut.step([1.0, 1.0])[0];
        }
        assert!((low - 0.5).abs() < 0.01, "gain at the bottom {low}");
        let (mut cut, _) = FirstOrder::shelves(distortion::EMPHASIS_FROM_HZ, distortion::EMPHASIS_TO_HZ);
        let mut high = 0.0f64;
        for n in 0..4_800 {
            let x = if n % 2 == 0 { 1.0 } else { -1.0 };
            high = cut.step([x, x])[0].abs();
        }
        assert!((high - 1.0).abs() < 1e-9, "gain at the top {high}");
    }

    /// The averaged curve is the curve itself for a steady input and its mean
    /// between two different inputs.
    #[test]
    fn the_averaged_curve_matches_the_curve() {
        let average = |a: f64, b: f64| {
            let (va, vb) = (a + distortion::BIAS, b + distortion::BIAS);
            ((va + vb) / ((1.0 + va * va).sqrt() + (1.0 + vb * vb).sqrt()) - distortion::offset()) * distortion::inverse_slope()
        };
        for u in [-30.0, -2.0, -0.3, 0.0, 0.004, 0.7, 5.0, 60.0] {
            assert!((average(u, u) - distortion::curve(u)).abs() < 1e-12, "steady input {u}");
        }
        assert_eq!(average(0.0, 0.0), 0.0, "rest is exactly silent");
        for (a, b) in [(-1.0, 1.5), (0.2, 0.200_001), (-40.0, 40.0), (3.0, -0.5)] {
            let steps = 20_000;
            let mean = (0..steps).map(|i| distortion::curve(a + (b - a) * (f64::from(i) + 0.5) / f64::from(steps))).sum::<f64>() / f64::from(steps);
            assert!((average(a, b) - mean).abs() < 1e-6, "between {a} and {b}: {} against {mean}", average(a, b));
        }
    }
}
