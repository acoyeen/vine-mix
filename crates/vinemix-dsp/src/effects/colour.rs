//! Distortion and Reverb.

use std::f64::consts::PI;

use vinemix_model::config::{DistortionSettings, ReverbSettings};
use vinemix_model::SAMPLE_RATE;
use vinemix_rt::Frame;

use crate::util::{denormal, frame, Smooth};

const FACTOR: usize = 8;
const TAPS: usize = 257;
const INPUT_TAPS: usize = 33;
/// Fixed latency of the oversampling filters, in input frames.
pub const DISTORTION_LATENCY: usize = 32;

/// 8x oversampled tanh waveshaper with a 257-tap Blackman FIR.
///
/// Original conventional FIR interpolation, nonlinear processing and
/// decimation; no third-party DSP source is incorporated. Fixed 8x processing
/// reduces aliasing; it does not eliminate it.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Distortion {
    filter: [f64; TAPS],
    interpolation: [[f64; FACTOR]; INPUT_TAPS],
    input_history: [Frame; INPUT_TAPS],
    /// Mirrored storage keeps both sides of the symmetric FIR contiguous.
    shaped_history: [Frame; TAPS * 2],
    dry_history: [Frame; DISTORTION_LATENCY],
    shaped_position: usize,
    dry_position: usize,
    dc_input: [f64; 2],
    dc_output: [f64; 2],
    tone_state: [f64; 2],
    drive: Smooth,
    tone_g: Smooth,
    mix: Smooth,
    output: Smooth,
}

impl Distortion {
    /// Builds coefficients once, on the preparation thread: a Blackman
    /// windowed low-pass at 0.0525 of the 384 kHz processing rate.
    pub fn new() -> Self {
        let mut filter = [0.0; TAPS];
        let mut sum = 0.0;
        for (i, coefficient) in filter.iter_mut().enumerate() {
            let x = i as f64 - ((TAPS - 1) / 2) as f64;
            let sinc = if x == 0.0 { 0.105 } else { (2.0 * PI * 0.0525 * x).sin() / (PI * x) };
            let phase = 2.0 * PI * i as f64 / (TAPS - 1) as f64;
            *coefficient = sinc * (0.42 - 0.5 * phase.cos() + 0.08 * (2.0 * phase).cos());
            sum += *coefficient;
        }
        for coefficient in &mut filter {
            *coefficient /= sum;
        }
        let mut interpolation = [[0.0; FACTOR]; INPUT_TAPS];
        for phase in 0..FACTOR {
            for (sample, row) in interpolation.iter_mut().enumerate() {
                let tap = sample * FACTOR + phase;
                row[phase] = if tap < TAPS { filter[tap] * FACTOR as f64 } else { 0.0 };
            }
        }
        Self {
            filter,
            interpolation,
            input_history: [Frame::SILENT; INPUT_TAPS],
            shaped_history: [Frame::SILENT; TAPS * 2],
            dry_history: [Frame::SILENT; DISTORTION_LATENCY],
            shaped_position: 0,
            dry_position: 0,
            dc_input: [0.0; 2],
            dc_output: [0.0; 2],
            tone_state: [0.0; 2],
            drive: Smooth::default(),
            tone_g: Smooth::default(),
            mix: Smooth::default(),
            output: Smooth::default(),
        }
    }

    pub fn configure(&mut self, setting: &DistortionSettings) {
        self.drive.set(10f64.powf(setting.drive_db / 20.0));
        self.tone_g.set((PI * setting.tone_hz / f64::from(SAMPLE_RATE)).tan());
        self.mix.set(setting.mix);
        self.output.set(10f64.powf(setting.output_db / 20.0));
    }

    /// Returns `(processed, delayed dry)`.
    pub fn process(&mut self, input: Frame) -> (Frame, Frame) {
        self.input_history.copy_within(0..INPUT_TAPS - 1, 1);
        // Keep subnormal float samples out of the oversampling histories;
        // the separately delayed dry path retains its exact input values.
        self.input_history[0] = frame(denormal(f64::from(input.left)), denormal(f64::from(input.right)));
        let boost = self.drive.next();
        let mut up = [[0.0f64; 2]; FACTOR];
        for sample in 0..INPUT_TAPS {
            let history = self.input_history[sample];
            for phase in 0..FACTOR {
                up[phase][0] += self.interpolation[sample][phase] * f64::from(history.left);
                up[phase][1] += self.interpolation[sample][phase] * f64::from(history.right);
            }
        }
        let (mut left, mut right) = (0.0f64, 0.0f64);
        for (phase, value) in up.iter().enumerate() {
            let shaped = frame((value[0] * boost).tanh(), (value[1] * boost).tanh());
            self.shaped_history[self.shaped_position] = shaped;
            self.shaped_history[self.shaped_position + TAPS] = shaped;
            // Decimate phase zero: the two 128-high-rate-frame filter delays
            // add to exactly 32 input frames.
            if phase == 0 {
                let recent = self.shaped_position + TAPS;
                let oldest = self.shaped_position + 1;
                // Symmetric taps are paired; four lanes hide add latency.
                let mut sums = [[0.0f64; 2]; 4];
                let mut tap = 0;
                while tap < TAPS / 2 {
                    for (lane, sum) in sums.iter_mut().enumerate() {
                        let index = tap + lane;
                        let a = self.shaped_history[recent - index];
                        let b = self.shaped_history[oldest + index];
                        sum[0] += self.filter[index] * (f64::from(a.left) + f64::from(b.left));
                        sum[1] += self.filter[index] * (f64::from(a.right) + f64::from(b.right));
                    }
                    tap += 4;
                }
                for sum in &sums {
                    left += sum[0];
                    right += sum[1];
                }
                let centre = self.shaped_history[recent - TAPS / 2];
                left += self.filter[TAPS / 2] * f64::from(centre.left);
                right += self.filter[TAPS / 2] * f64::from(centre.right);
            }
            self.shaped_position = (self.shaped_position + 1) % TAPS;
        }
        const DC_POLE: f64 = 0.997_385_430_079_362_9; // 20 Hz at 48 kHz.
        let g = self.tone_g.next();
        let amount = self.mix.next();
        let trim = self.output.next();
        let mut colour = |value: f64, channel: usize| {
            let high = denormal((1.0 + DC_POLE) * 0.5 * (value - self.dc_input[channel]) + DC_POLE * self.dc_output[channel]);
            self.dc_input[channel] = value;
            self.dc_output[channel] = high;
            let band = (high - self.tone_state[channel]) * g / (1.0 + g);
            let low = band + self.tone_state[channel];
            self.tone_state[channel] = denormal(low + band);
            low
        };
        left = colour(left, 0);
        right = colour(right, 1);
        let dry = self.dry_history[self.dry_position];
        self.dry_history[self.dry_position] = input;
        self.dry_position = (self.dry_position + 1) % DISTORTION_LATENCY;
        // Output trims the whole effect blend, even at Mix 0. The dry result
        // is untrimmed and equally delayed, so graph Bypass stays transparent.
        let processed = frame(
            (f64::from(dry.left) + (left - f64::from(dry.left)) * amount) * trim,
            (f64::from(dry.right) + (right - f64::from(dry.right)) * amount) * trim,
        );
        (processed, dry)
    }

    pub fn reset(&mut self) {
        self.input_history = [Frame::SILENT; INPUT_TAPS];
        self.shaped_history = [Frame::SILENT; TAPS * 2];
        self.dry_history = [Frame::SILENT; DISTORTION_LATENCY];
        self.shaped_position = 0;
        self.dry_position = 0;
        self.dc_input = [0.0; 2];
        self.dc_output = [0.0; 2];
        self.tone_state = [0.0; 2];
        self.drive = Smooth::default();
        self.tone_g = Smooth::default();
        self.mix = Smooth::default();
        self.output = Smooth::default();
    }
}

impl Default for Distortion {
    fn default() -> Self {
        Self::new()
    }
}

const COMB_SIZE: usize = 2048;
const ALLPASS_SIZE: usize = 640;
const PRE_SIZE: usize = 6042;
pub const COMB_LENGTHS: [usize; 8] = [1215, 1293, 1390, 1476, 1547, 1623, 1695, 1760];
const ALLPASS_LENGTHS: [usize; 4] = [605, 480, 371, 245];

#[derive(Clone, Copy, Debug, PartialEq)]
struct ReverbChannel {
    comb: [[f32; COMB_SIZE]; 8],
    allpass: [[f32; ALLPASS_SIZE]; 4],
    comb_position: [usize; 8],
    allpass_position: [usize; 4],
    low: [f64; 8],
}

impl ReverbChannel {
    const SILENT: Self = Self {
        comb: [[0.0; COMB_SIZE]; 8],
        allpass: [[0.0; ALLPASS_SIZE]; 4],
        comb_position: [0; 8],
        allpass_position: [0; 4],
        low: [0.0; 8],
    };
}

/// Scattering-junction comb network with true Schroeder allpass diffusion.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Reverb {
    channels: [ReverbChannel; 2],
    predelay: [Frame; PRE_SIZE],
    pre_position: usize,
    mix: Smooth,
    damping: Smooth,
    pre_samples: Smooth,
    feedback: [[Smooth; 8]; 2],
}

/// Reverb output: the main signal (dry plus Mix) and the pure wet tail.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct ReverbOutput {
    pub mixed: Frame,
    pub wet: Frame,
}

impl Reverb {
    pub const SILENT: Self = Self {
        channels: [ReverbChannel::SILENT; 2],
        predelay: [Frame::SILENT; PRE_SIZE],
        pre_position: 0,
        mix: Smooth { value: 0.0, target: 0.0, step: 0.0, remaining: 0, initialized: false },
        damping: Smooth { value: 0.0, target: 0.0, step: 0.0, remaining: 0, initialized: false },
        pre_samples: Smooth { value: 0.0, target: 0.0, step: 0.0, remaining: 0, initialized: false },
        feedback: [[Smooth { value: 0.0, target: 0.0, step: 0.0, remaining: 0, initialized: false }; 8]; 2],
    };

    /// Heap-allocates a silent reverb without a large stack temporary.
    pub fn boxed() -> Box<Self> {
        let mut reverb: Box<std::mem::MaybeUninit<Self>> = Box::new_uninit();
        // SAFETY: SILENT is plain old data; writing it fully initialises the box.
        unsafe {
            std::ptr::copy_nonoverlapping(&Self::SILENT, reverb.as_mut_ptr(), 1);
            reverb.assume_init()
        }
    }

    pub fn reset(&mut self) {
        *self = Self::SILENT;
    }

    pub fn configure(&mut self, setting: &ReverbSettings) {
        self.mix.set(setting.mix);
        self.damping.set(setting.damping * 0.9);
        // The scattering sections have a feed-forward term: keep the original
        // first-reflection onset before the user's pre-delay.
        self.pre_samples.set(setting.pre_delay_ms * 48.0 + 1215.0);
        for side in 0..2 {
            for band in 0..8 {
                let length = (COMB_LENGTHS[band] + side * 25) as f64;
                self.feedback[side][band].set(0.001f64.powf(length / (f64::from(SAMPLE_RATE) * setting.decay_seconds)));
            }
        }
    }

    pub fn process(&mut self, input: Frame) -> ReverbOutput {
        self.predelay[self.pre_position] = input;
        let delay = self.pre_samples.next();
        let whole = delay as usize;
        let fraction = delay - whole as f64;
        let first = (self.pre_position + PRE_SIZE - whole) % PRE_SIZE;
        let second = (first + PRE_SIZE - 1) % PRE_SIZE;
        let (a, b) = (self.predelay[first], self.predelay[second]);
        let delayed = frame(
            f64::from(a.left) * (1.0 - fraction) + f64::from(b.left) * fraction,
            f64::from(a.right) * (1.0 - fraction) + f64::from(b.right) * fraction,
        );
        self.pre_position = (self.pre_position + 1) % PRE_SIZE;
        let damp = self.damping.next();
        let amount = self.mix.next();
        let mut result = [0.0f64; 2];
        for side in 0..2 {
            let c = &mut self.channels[side];
            let excitation = if side == 0 {
                0.75 * f64::from(delayed.left) + 0.25 * f64::from(delayed.right)
            } else {
                0.75 * f64::from(delayed.right) + 0.25 * f64::from(delayed.left)
            }
            .clamp(-16.0, 16.0);
            let mut value = 0.0;
            for band in 0..8 {
                let length = COMB_LENGTHS[band] + side * 25;
                let position = c.comb_position[band];
                let previous = f64::from(c.comb[band][position]);
                c.low[band] = denormal(previous * (1.0 - damp) + c.low[band] * damp);
                let feedback_gain = self.feedback[side][band].next();
                // A lossless two-port scattering junction with the damped
                // delay loop as its second port: never above unity, so longer
                // decay cannot multiply steady-state energy.
                let coupling = (1.0 - feedback_gain * feedback_gain).sqrt();
                c.comb[band][position] = (excitation * coupling + c.low[band] * feedback_gain) as f32;
                c.comb_position[band] = (position + 1) % length;
                value += (c.low[band] * coupling - excitation * feedback_gain) * 0.125;
            }
            for band in 0..4 {
                let length = ALLPASS_LENGTHS[band] + side * 25;
                let position = c.allpass_position[band];
                let previous = f64::from(c.allpass[band][position]);
                // True Schroeder allpass: H(z) = (z^-D - g) / (1 - g z^-D).
                let next = previous - value * 0.5;
                c.allpass[band][position] = denormal(value + next * 0.5) as f32;
                c.allpass_position[band] = (position + 1) % length;
                value = next;
            }
            result[side] = value;
        }
        ReverbOutput {
            mixed: frame(
                f64::from(input.left) * (1.0 - amount) + result[0] * amount,
                f64::from(input.right) * (1.0 - amount) + result[1] * amount,
            ),
            wet: frame(result[0], result[1]),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn distortion_latency_is_exactly_32_frames() {
        let mut distortion = Distortion::new();
        distortion.configure(&DistortionSettings { mix: 0.0, output_db: 0.0, ..DistortionSettings::default() });
        let mut outputs = Vec::new();
        for n in 0..40 {
            let x = if n == 0 { 0.5 } else { 0.0 };
            outputs.push(distortion.process(Frame::new(x, x)).0.left);
        }
        assert_eq!(outputs[32], 0.5);
        assert!(outputs.iter().enumerate().all(|(n, &v)| n == 32 || v == 0.0));
    }

    #[test]
    fn distortion_filter_has_unit_dc_gain() {
        let distortion = Distortion::new();
        let sum: f64 = distortion.filter.iter().sum();
        assert!((sum - 1.0).abs() < 1e-12);
        for phase in 0..FACTOR {
            let gain: f64 = distortion.interpolation.iter().map(|row| row[phase]).sum();
            assert!((gain - 1.0).abs() < 0.01, "phase {phase} gain {gain}");
        }
    }

    #[test]
    fn reverb_is_passive() {
        let mut reverb = Reverb::boxed();
        reverb.configure(&ReverbSettings { mix: 1.0, decay_seconds: 5.0, damping: 0.0, pre_delay_ms: 0.0 });
        let mut energy_in = 0.0;
        let mut energy_out = 0.0;
        let mut seed = 1u32;
        for _ in 0..96_000 {
            seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
            let x = (seed >> 8) as f32 / (1u32 << 24) as f32 - 0.5;
            energy_in += f64::from(x * x);
            let out = reverb.process(Frame::new(x, x)).wet;
            energy_out += f64::from(out.left * out.left);
        }
        assert!(energy_out < energy_in * 1.05, "{energy_out} vs {energy_in}");
        assert!(energy_out > 0.0);
    }
}
