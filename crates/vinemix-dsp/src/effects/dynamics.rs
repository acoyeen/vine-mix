//! Expander / Gate and the De-esser / Plosive band reducer.

use std::f64::consts::PI;

use vinemix_model::config::ExpanderSettings;
use vinemix_model::SAMPLE_RATE;
use vinemix_rt::Frame;

use crate::util::{denormal, Smooth};

/// 0.5 ms and 20 ms one-pole coefficients at 48 kHz.
pub const DETECTOR_ATTACK: f64 = 0.959_189_457_109_138_2;
pub const DETECTOR_RELEASE_20MS: f64 = 0.998_958_875_679_724_5;
/// 1 ms and 50 ms gain coefficients.
pub const GAIN_ATTACK_1MS: f64 = 0.979_382_181_331_240_1;
pub const RELEASE_50MS: f64 = 0.999_583_420_126_833_8;

fn time_constant(milliseconds: f64) -> f64 {
    (-1.0 / (milliseconds * 0.001 * f64::from(SAMPLE_RATE))).exp()
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Expander {
    threshold: Smooth,
    ratio: Smooth,
    attack: Smooth,
    release: Smooth,
    floor: Smooth,
    hysteresis: Smooth,
    gate_mix: Smooth,
    hold_samples: Smooth,
    pub envelope: f64,
    pub attenuation: f64,
    hold: u32,
    open: bool,
}

impl Default for Expander {
    fn default() -> Self {
        Self {
            threshold: Smooth::default(),
            ratio: Smooth::default(),
            attack: Smooth::default(),
            release: Smooth::default(),
            floor: Smooth::default(),
            hysteresis: Smooth::default(),
            gate_mix: Smooth::default(),
            hold_samples: Smooth::default(),
            envelope: 0.0,
            attenuation: 1.0,
            hold: 0,
            open: false,
        }
    }
}

/// `base.powf(exponent)` for the expander's transfer curve, where `base` has
/// already been limited to at most 1. At or above the threshold the base is
/// exactly 1, and `1.0.powf(x)` is exactly 1 for every `x` (IEEE 754 `pow`),
/// so the power is skipped there. The result is bit-identical to always
/// calling `powf`; it only avoids the call for signal above the threshold,
/// which is where an open gate or idle expander spends its time.
#[inline]
fn expansion(base: f64, exponent: f64) -> f64 {
    if base == 1.0 { 1.0 } else { base.powf(exponent) }
}

impl Expander {
    pub fn configure(&mut self, s: &ExpanderSettings) {
        self.threshold.set(10f64.powf(s.threshold_db / 20.0));
        self.ratio.set(s.ratio);
        self.attack.set(time_constant(s.attack_ms));
        self.release.set(time_constant(s.release_ms));
        self.floor.set(10f64.powf(-s.range_db / 20.0));
        self.hysteresis.set(10f64.powf(-s.hysteresis_db / 20.0));
        self.hold_samples.set(s.hold_ms * 48.0);
        self.gate_mix.set(if s.mode == "gate" { 1.0 } else { 0.0 });
    }

    pub fn process(&mut self, input: Frame, detector: Frame) -> Frame {
        let peak = f64::from(detector.left.abs().max(detector.right.abs()));
        // Stereo-linked peak detector: fast enough to reopen on speech onset,
        // with a 20 ms decay so zero crossings cannot chatter the gate.
        let coefficient = if peak > self.envelope { DETECTOR_ATTACK } else { DETECTOR_RELEASE_20MS };
        self.envelope = denormal(peak + coefficient * (self.envelope - peak));
        let level = self.threshold.next();
        let exponent = self.ratio.next() - 1.0;
        let minimum = self.floor.next();
        let close_level = level * self.hysteresis.next();
        let amount = self.gate_mix.next();
        let duration = self.hold_samples.next().round() as u32;
        if self.envelope >= level {
            self.open = true;
            self.hold = duration;
        } else if self.open && self.envelope >= close_level {
            self.hold = duration;
        } else if self.hold != 0 {
            self.hold -= 1;
        } else {
            self.open = false;
        }
        let expanded = minimum.max(expansion((self.envelope / level).min(1.0), exponent));
        let gated = if self.open { 1.0 } else { minimum };
        let target = expanded + (gated - expanded) * amount;
        let opening = self.attack.next();
        let closing = self.release.next();
        let coefficient = if target > self.attenuation { opening } else { closing };
        self.attenuation = target + coefficient * (self.attenuation - target);
        Frame::new(
            (f64::from(input.left) * self.attenuation) as f32,
            (f64::from(input.right) * self.attenuation) as f32,
        )
    }

    /// The transfer curve as it was before the shortcut (benchmark baseline).
    #[cfg(test)]
    fn process_always_powf(&mut self, input: Frame, detector: Frame) -> Frame {
        let peak = f64::from(detector.left.abs().max(detector.right.abs()));
        // Stereo-linked peak detector: fast enough to reopen on speech onset,
        // with a 20 ms decay so zero crossings cannot chatter the gate.
        let coefficient = if peak > self.envelope { DETECTOR_ATTACK } else { DETECTOR_RELEASE_20MS };
        self.envelope = denormal(peak + coefficient * (self.envelope - peak));
        let level = self.threshold.next();
        let exponent = self.ratio.next() - 1.0;
        let minimum = self.floor.next();
        let close_level = level * self.hysteresis.next();
        let amount = self.gate_mix.next();
        let duration = self.hold_samples.next().round() as u32;
        if self.envelope >= level {
            self.open = true;
            self.hold = duration;
        } else if self.open && self.envelope >= close_level {
            self.hold = duration;
        } else if self.hold != 0 {
            self.hold -= 1;
        } else {
            self.open = false;
        }
        let expanded = minimum.max((self.envelope / level).min(1.0).powf(exponent));
        let gated = if self.open { 1.0 } else { minimum };
        let target = expanded + (gated - expanded) * amount;
        let opening = self.attack.next();
        let closing = self.release.next();
        let coefficient = if target > self.attenuation { opening } else { closing };
        self.attenuation = target + coefficient * (self.attenuation - target);
        Frame::new(
            (f64::from(input.left) * self.attenuation) as f32,
            (f64::from(input.right) * self.attenuation) as f32,
        )
    }
}

/// Topology-preserving state-variable filter (Andrew Simper, Cytomic).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Svf {
    integrator1: f64,
    integrator2: f64,
}

pub const SQRT_2: f64 = std::f64::consts::SQRT_2; // the same double as 0.5's literal

impl Svf {
    /// Returns `[low, band, high]`.
    #[inline]
    pub fn process(&mut self, input: f64, g: f64) -> [f64; 3] {
        let k = SQRT_2;
        let a1 = 1.0 / (1.0 + g * (g + k));
        let a2 = g * a1;
        let a3 = g * a2;
        let v3 = input - self.integrator2;
        let band = a1 * self.integrator1 + a2 * v3;
        let low = self.integrator2 + a2 * self.integrator1 + a3 * v3;
        self.integrator1 = denormal(2.0 * band - self.integrator1);
        self.integrator2 = denormal(2.0 * low - self.integrator2);
        [low, band, input - k * band - low]
    }
}

/// Dynamic second-order shelf for De-esser (high) and Plosive (low).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct BandReduction {
    detector: [Svf; 2],
    shelf: [Svf; 2],
    pub envelope: f64,
    pub attenuation: f64,
    pub frequency_g: Smooth,
    pub threshold: Smooth,
    pub floor: Smooth,
    pub high: bool,
}

impl BandReduction {
    pub fn new(high: bool) -> Self {
        Self {
            detector: [Svf::default(); 2],
            shelf: [Svf::default(); 2],
            envelope: 0.0,
            attenuation: 1.0,
            frequency_g: Smooth::default(),
            threshold: Smooth::default(),
            floor: Smooth::default(),
            high,
        }
    }

    pub fn configure(&mut self, frequency_hz: f64, threshold_db: f64, reduction_db: f64) {
        self.frequency_g.set((PI * frequency_hz / f64::from(SAMPLE_RATE)).tan());
        self.threshold.set(10f64.powf(threshold_db / 20.0));
        self.floor.set(10f64.powf(-reduction_db / 20.0));
    }

    pub fn process(&mut self, input: Frame) -> Frame {
        let g = self.frequency_g.next();
        let left_detector = self.detector[0].process(f64::from(input.left), g);
        let right_detector = self.detector[1].process(f64::from(input.right), g);
        let index = if self.high { 2 } else { 0 };
        let peak = left_detector[index].abs().max(right_detector[index].abs());
        // A stereo-linked sidechain keeps the image steady on consonants.
        let coefficient = if peak > self.envelope { DETECTOR_ATTACK } else { DETECTOR_RELEASE_20MS };
        self.envelope = peak + coefficient * (self.envelope - peak);
        let threshold = self.threshold.next();
        let minimum = self.floor.next();
        let desired =
            if self.envelope > threshold { minimum.max((threshold / self.envelope).powf(0.75)) } else { 1.0 };
        let coefficient = if desired < self.attenuation { GAIN_ATTACK_1MS } else { RELEASE_50MS };
        self.attenuation = desired + coefficient * (self.attenuation - desired);
        // A is the square root of shelf amplitude; Frequency is the midpoint
        // (half the shelf's dB reduction). At unity the output is exactly dry.
        let k = SQRT_2;
        let a = self.attenuation.sqrt();
        let root_a = a.sqrt();
        let shelf_g = if self.high { g * root_a } else { g / root_a };
        let m0 = if self.high { self.attenuation } else { 1.0 };
        let m1 = if self.high { k * (1.0 - a) * a } else { k * (a - 1.0) };
        let m2 = if self.high { 1.0 - self.attenuation } else { self.attenuation - 1.0 };
        let left = self.shelf[0].process(f64::from(input.left), shelf_g);
        let right = self.shelf[1].process(f64::from(input.right), shelf_g);
        Frame::new(
            (m0 * f64::from(input.left) + m1 * left[1] + m2 * left[0]) as f32,
            (m0 * f64::from(input.right) + m1 * right[1] + m2 * right[0]) as f32,
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// RUST-REV-018: the skipped power is exact, including at the edges.
    #[test]
    fn expansion_shortcut_is_bit_identical_to_powf() {
        let exponents = [0.0, 1.0, 0.25, 3.0, 19.0, -0.5, 1e-300, f64::MAX, f64::INFINITY, f64::NEG_INFINITY, f64::NAN];
        for exponent in exponents {
            assert_eq!(expansion(1.0, exponent).to_bits(), 1.0f64.powf(exponent).to_bits(), "exponent {exponent}");
        }
        let mut state = 0x9E37_79B9_7F4A_7C15u64;
        let mut next = || {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            (state >> 11) as f64 / (1u64 << 53) as f64
        };
        for _ in 0..200_000 {
            let base = if next() < 0.3 { 1.0 } else { next() };
            let exponent = next() * 19.0;
            assert_eq!(expansion(base, exponent).to_bits(), base.powf(exponent).to_bits(), "{base} ^ {exponent}");
        }
        for base in [0.0, f64::MIN_POSITIVE, 1.0 - f64::EPSILON, 0.999_999_999_999_999_9] {
            assert_eq!(expansion(base, 3.0).to_bits(), base.powf(3.0).to_bits());
        }
    }

    /// Cost of the expander with signal above its threshold, with and
    /// without the shortcut. Run with
    /// `cargo test -p vinemix-dsp --release --lib expander_cost -- --ignored --nocapture`.
    #[test]
    #[ignore = "benchmark; prints timings"]
    fn expander_cost() {
        const FRAMES: usize = 48_000 * 20;
        let run = |shortcut: bool, amplitude: f32| {
            let mut samples = Vec::new();
            for _ in 0..5 {
                let mut expander = Expander::default();
                expander.configure(&ExpanderSettings::default());
                let started = std::time::Instant::now();
                let mut checksum = 0.0f64;
                for n in 0..FRAMES {
                    let x = (n as f32 * 0.02).sin() * amplitude;
                    let frame = Frame::new(x, x);
                    if shortcut {
                        checksum += f64::from(expander.process(frame, frame).left);
                    } else {
                        checksum += f64::from(expander.process_always_powf(frame, frame).left);
                    }
                }
                std::hint::black_box(checksum);
                samples.push(started.elapsed().as_secs_f64());
            }
            samples.sort_by(f64::total_cmp);
            samples[2] / 20.0 * 100.0
        };
        for (label, amplitude) in [("above the threshold (open)", 0.5f32), ("below the threshold (reducing)", 0.001)] {
            println!("expander, signal {label}: always powf {:.4}% of a core, with the shortcut {:.4}%", run(false, amplitude), run(true, amplitude));
        }
    }

    #[test]
    fn constants_match_their_time_constants() {
        assert!((DETECTOR_ATTACK - time_constant(0.5)).abs() < 1e-15);
        assert!((DETECTOR_RELEASE_20MS - time_constant(20.0)).abs() < 1e-15);
        assert!((GAIN_ATTACK_1MS - time_constant(1.0)).abs() < 1e-15);
        assert!((RELEASE_50MS - time_constant(50.0)).abs() < 1e-15);
    }

    #[test]
    fn gate_closes_to_its_range_and_reopens() {
        let mut gate = Expander::default();
        let settings = ExpanderSettings { mode: "gate".into(), hold_ms: 0.0, ..ExpanderSettings::default() };
        gate.configure(&settings);
        let quiet = Frame::new(0.0001, 0.0001);
        // 150 ms release: 5 s is over 33 time constants.
        for _ in 0..240_000 {
            gate.process(quiet, quiet);
        }
        assert!((gate.attenuation - 10f64.powf(-60.0 / 20.0)).abs() < 1e-6, "{} {} {}", gate.attenuation, gate.envelope, gate.open);
        let loud = Frame::new(0.5, 0.5);
        for _ in 0..4_800 {
            gate.process(loud, loud);
        }
        assert!(gate.attenuation > 0.99);
    }

    #[test]
    fn unity_band_reduction_is_transparent() {
        let mut band = BandReduction::new(true);
        band.configure(6000.0, 0.0, 9.0);
        for n in 0..1000 {
            let x = ((n as f64) * 0.01).sin() as f32 * 0.1;
            let out = band.process(Frame::new(x, -x));
            assert!((out.left - x).abs() < 1e-6, "below threshold the shelf is flat");
        }
    }
}
