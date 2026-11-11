//! Reverb.

use vinemix_model::config::ReverbSettings;
use vinemix_model::SAMPLE_RATE;
use vinemix_rt::Frame;

use crate::util::{denormal, frame, Smooth};

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
