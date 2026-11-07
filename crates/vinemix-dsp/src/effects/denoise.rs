//! Denoiser: SpeexDSP spectral suppression with an RNNoise neural option.
//!
//! Both engines run on 10 ms blocks with fixed latency (960 and 1440 frames)
//! and a matching delayed dry path, so graph Bypass stays aligned.

use vinemix_denoise::{RnNoise, SpeexPreprocessor};
use vinemix_model::config::DenoiserSettings;
use vinemix_model::SAMPLE_RATE;
use vinemix_rt::Frame;

use crate::util::{clean, frame, Smooth};

const BLOCK: usize = 480;
pub const SPEEX_LATENCY: usize = 960;
pub const RNNOISE_LATENCY: usize = 1440;

/// Engine-processed and delayed-dry frames for one input frame.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Processed {
    pub wet: Frame,
    pub dry: Frame,
}

pub struct SpeexDenoise {
    active: [SpeexPreprocessor; 2],
    pristine: [SpeexPreprocessor; 2],
    input: [[i16; BLOCK]; 2],
    output: [[i16; BLOCK]; 2],
    dry: Box<[Frame; SPEEX_LATENCY]>,
    position: usize,
    dry_position: usize,
    pub suppression: i32,
    pub target_suppression: i32,
}

impl SpeexDenoise {
    fn new() -> Self {
        let make = || {
            // AGC, dereverberation and VAD are off by construction.
            let mut state = SpeexPreprocessor::new(BLOCK, SAMPLE_RATE);
            state.set_noise_suppress(-18);
            state
        };
        Self {
            active: [make(), make()],
            pristine: [make(), make()],
            input: [[0; BLOCK]; 2],
            output: [[0; BLOCK]; 2],
            dry: Box::new([Frame::SILENT; SPEEX_LATENCY]),
            position: 0,
            dry_position: 0,
            suppression: -18,
            target_suppression: -18,
        }
    }

    fn reset(&mut self) {
        for channel in 0..2 {
            self.active[channel].copy_state_from(&self.pristine[channel]);
        }
        self.input = [[0; BLOCK]; 2];
        self.output = [[0; BLOCK]; 2];
        self.dry.fill(Frame::SILENT);
        self.position = 0;
        self.dry_position = 0;
        self.suppression = -18;
        self.target_suppression = -18;
    }

    fn inherit(&mut self, old: &Self) {
        for channel in 0..2 {
            self.active[channel].copy_state_from(&old.active[channel]);
        }
        self.input = old.input;
        self.output = old.output;
        self.dry.copy_from_slice(&old.dry[..]);
        self.position = old.position;
        self.dry_position = old.dry_position;
        self.suppression = old.suppression;
        self.target_suppression = old.target_suppression;
    }

    fn apply_suppression(&mut self) {
        for state in &mut self.active {
            state.set_noise_suppress(self.suppression);
        }
    }

    fn to_pcm(sample: f32) -> i16 {
        (clean(f64::from(sample)).clamp(-1.0, 32767.0 / 32768.0) * 32768.0).round() as i16
    }

    fn process(&mut self, sample: Frame) -> Processed {
        let delayed = self.dry[self.dry_position];
        self.dry[self.dry_position] = sample;
        self.dry_position = (self.dry_position + 1) % SPEEX_LATENCY;
        let processed = Frame::new(
            f32::from(self.output[0][self.position]) / 32768.0,
            f32::from(self.output[1][self.position]) / 32768.0,
        );
        self.input[0][self.position] = Self::to_pcm(sample.left);
        self.input[1][self.position] = Self::to_pcm(sample.right);
        self.position += 1;
        if self.position == BLOCK {
            self.position = 0;
            // A live reduction edit advances at most 1 dB per 10 ms frame;
            // Speex's overlap-add handles the within-frame transition.
            if self.suppression != self.target_suppression {
                self.suppression += (self.target_suppression - self.suppression).clamp(-1, 1);
                self.apply_suppression();
            }
            for channel in 0..2 {
                self.output[channel] = self.input[channel];
                self.active[channel].run(&mut self.output[channel]);
            }
        }
        Processed { wet: processed, dry: delayed }
    }
}

pub struct NeuralDenoise {
    active: [Box<RnNoise>; 2],
    pristine: [Box<RnNoise>; 2],
    input: [[f32; BLOCK]; 2],
    output: [[f32; BLOCK]; 2],
    dry: Box<[Frame; RNNOISE_LATENCY]>,
    position: usize,
    dry_position: usize,
}

impl NeuralDenoise {
    fn new() -> Self {
        assert_eq!(RnNoise::FRAME_SIZE, BLOCK, "Unexpected RNNoise frame size.");
        Self {
            active: [RnNoise::new(), RnNoise::new()],
            pristine: [RnNoise::new(), RnNoise::new()],
            input: [[0.0; BLOCK]; 2],
            output: [[0.0; BLOCK]; 2],
            dry: Box::new([Frame::SILENT; RNNOISE_LATENCY]),
            position: 0,
            dry_position: 0,
        }
    }

    fn reset(&mut self) {
        for channel in 0..2 {
            self.active[channel].copy_state_from(&self.pristine[channel]);
        }
        self.input = [[0.0; BLOCK]; 2];
        self.output = [[0.0; BLOCK]; 2];
        self.dry.fill(Frame::SILENT);
        self.position = 0;
        self.dry_position = 0;
    }

    fn inherit(&mut self, old: &Self) {
        for channel in 0..2 {
            self.active[channel].copy_state_from(&old.active[channel]);
        }
        self.input = old.input;
        self.output = old.output;
        self.dry.copy_from_slice(&old.dry[..]);
        self.position = old.position;
        self.dry_position = old.dry_position;
    }

    fn process(&mut self, sample: Frame) -> Processed {
        let delayed = self.dry[self.dry_position];
        self.dry[self.dry_position] = sample;
        self.dry_position = (self.dry_position + 1) % RNNOISE_LATENCY;
        let processed =
            Frame::new(self.output[0][self.position] / 32768.0, self.output[1][self.position] / 32768.0);
        self.input[0][self.position] = (clean(f64::from(sample.left)).clamp(-1.0, 1.0) * 32768.0) as f32;
        self.input[1][self.position] = (clean(f64::from(sample.right)).clamp(-1.0, 1.0) * 32768.0) as f32;
        self.position += 1;
        if self.position == BLOCK {
            self.position = 0;
            for channel in 0..2 {
                self.active[channel].process_frame(&mut self.output[channel], &self.input[channel]);
            }
        }
        Processed { wet: processed, dry: delayed }
    }
}

pub struct Denoise {
    speex: SpeexDenoise,
    neural: NeuralDenoise,
    algorithm_mix: Smooth,
    neural_mix: Smooth,
    target_neural: bool,
    warmup: u32,
}

fn mix(a: Frame, b: Frame, amount: f64) -> Frame {
    if amount == 0.0 {
        return a;
    }
    if amount == 1.0 {
        return b;
    }
    frame(
        f64::from(a.left) + (f64::from(b.left) - f64::from(a.left)) * amount,
        f64::from(a.right) + (f64::from(b.right) - f64::from(a.right)) * amount,
    )
}

impl Denoise {
    pub fn new() -> Self {
        Self {
            speex: SpeexDenoise::new(),
            neural: NeuralDenoise::new(),
            algorithm_mix: Smooth::default(),
            neural_mix: Smooth::default(),
            target_neural: false,
            warmup: 0,
        }
    }

    pub fn configure(&mut self, setting: &DenoiserSettings, first: bool) {
        let next = setting.algorithm == "rnnoise";
        self.neural_mix.set(setting.rnnoise_mix);
        self.speex.target_suppression = setting.suppression_db.round() as i32;
        if first {
            self.target_neural = next;
            self.algorithm_mix.set(if next { 1.0 } else { 0.0 });
            self.speex.suppression = self.speex.target_suppression;
            self.speex.apply_suppression();
        } else if self.target_neural != next {
            self.target_neural = next;
            if next && self.algorithm_mix.value == 0.0 && self.algorithm_mix.remaining == 0 {
                // Warm the idle network for 100 ms before it becomes audible.
                self.neural.reset();
                self.warmup = 4800;
            } else if next {
                self.warmup = 0;
                self.algorithm_mix.set(1.0);
            } else {
                self.warmup = 0;
                self.algorithm_mix.set(0.0);
            }
        }
    }

    pub fn reset(&mut self) {
        self.speex.reset();
        self.neural.reset();
        self.algorithm_mix = Smooth::default();
        self.neural_mix = Smooth::default();
        self.target_neural = false;
        self.warmup = 0;
    }

    pub fn inherit(&mut self, old: &Self) {
        self.speex.inherit(&old.speex);
        self.neural.inherit(&old.neural);
        self.algorithm_mix = old.algorithm_mix;
        self.neural_mix = old.neural_mix;
        self.target_neural = old.target_neural;
        self.warmup = old.warmup;
    }

    pub fn process(&mut self, sample: Frame) -> Processed {
        let spectral = self.speex.process(sample);
        let strength = self.neural_mix.next();
        if !self.target_neural && self.algorithm_mix.value == 0.0 && self.algorithm_mix.remaining == 0 {
            return spectral;
        }
        let cleaned = self.neural.process(sample);
        if self.warmup != 0 {
            self.warmup -= 1;
            if self.warmup == 0 {
                self.algorithm_mix.set(1.0);
            }
        }
        let blend = self.algorithm_mix.next();
        Processed {
            wet: mix(spectral.wet, mix(cleaned.dry, cleaned.wet, strength), blend),
            dry: mix(spectral.dry, cleaned.dry, blend),
        }
    }
}

impl Default for Denoise {
    fn default() -> Self {
        Self::new()
    }
}

impl std::fmt::Debug for SpeexDenoise {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SpeexDenoise").field("suppression", &self.suppression).finish_non_exhaustive()
    }
}

impl std::fmt::Debug for NeuralDenoise {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("NeuralDenoise").field("position", &self.position).finish_non_exhaustive()
    }
}

impl std::fmt::Debug for Denoise {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Denoise").field("target_neural", &self.target_neural).finish_non_exhaustive()
    }
}
