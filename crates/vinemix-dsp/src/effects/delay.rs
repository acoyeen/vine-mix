//! Alignment Delay and Echo.

use vinemix_model::config::EchoSettings;
use vinemix_model::SAMPLE_RATE;
use vinemix_rt::Frame;

use crate::util::{denormal, frame, touched_frames, Smooth};

/// Alignment Delay history: 0 to 96000 samples plus the current frame.
pub const ALIGNMENT_CAPACITY: usize = 96_001;
/// Echo history: eight repeats of the longest 2 s time, plus interpolation.
pub const ECHO_HISTORY_FRAMES: usize = SAMPLE_RATE as usize * 2 * 8 + 2;
const TRANSITION: usize = 480;

pub struct AlignmentDelay {
    history: Box<[Frame]>,
    position: usize,
    current: u32,
    destination: u32,
    requested: u32,
    remaining: u32,
    configured: bool,
}

impl AlignmentDelay {
    pub fn new() -> Self {
        Self {
            history: touched_frames(ALIGNMENT_CAPACITY),
            position: 0,
            current: 0,
            destination: 0,
            requested: 0,
            remaining: 0,
            configured: false,
        }
    }

    pub fn configure(&mut self, samples: u32) {
        self.requested = samples;
        if !self.configured {
            self.current = samples;
            self.destination = samples;
            self.configured = true;
        }
    }

    fn at(&self, delay: u32) -> Frame {
        self.history[(self.position + ALIGNMENT_CAPACITY - delay as usize) % ALIGNMENT_CAPACITY]
    }

    pub fn process(&mut self, input: Frame) -> Frame {
        self.history[self.position] = input;
        if self.remaining == 0 && self.current != self.requested {
            self.destination = self.requested;
            self.remaining = TRANSITION as u32;
        }
        let mut result = self.at(self.current);
        if self.remaining != 0 {
            let next = self.at(self.destination);
            self.remaining -= 1;
            let mix = 1.0 - f64::from(self.remaining) / TRANSITION as f64;
            // The difference is single precision, as in the 0.5 engine.
            result = frame(
                f64::from(result.left) + f64::from(next.left - result.left) * mix,
                f64::from(result.right) + f64::from(next.right - result.right) * mix,
            );
            if self.remaining == 0 {
                self.current = self.destination;
            }
        }
        self.position = (self.position + 1) % ALIGNMENT_CAPACITY;
        result
    }

    /// Copies all state, including the history (audio thread, no allocation).
    pub fn copy_from(&mut self, other: &Self) {
        self.history.copy_from_slice(&other.history);
        self.position = other.position;
        self.current = other.current;
        self.destination = other.destination;
        self.requested = other.requested;
        self.remaining = other.remaining;
        self.configured = other.configured;
    }

    pub fn reset(&mut self) {
        self.history.fill(Frame::SILENT);
        self.position = 0;
        self.current = 0;
        self.destination = 0;
        self.requested = 0;
        self.remaining = 0;
        self.configured = false;
    }
}

impl Default for AlignmentDelay {
    fn default() -> Self {
        Self::new()
    }
}

/// Finite-tap echo: every repeat reads original input, never earlier repeats.
pub struct Echo {
    /// Allocated and touched during control-thread preparation. Reset is
    /// logical; no 6 MB clear or copy belongs on the audio thread.
    history: Box<[Frame]>,
    position: usize,
    valid: usize,
    current: f64,
    requested: f64,
    repeats: i32,
    requested_repeats: i32,
    restarting: u32,
    capture_fade: u32,
    configured: bool,
    mix: Smooth,
    weights: [f64; 8],
    retired_wet: [Frame; TRANSITION],
    retired_position: usize,
    retired_count: usize,
    retired: bool,
}

/// What an echo produces for one frame.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct EchoOutput {
    pub mixed: Frame,
    pub dry: Frame,
    pub wet: Frame,
}

impl Echo {
    pub fn new() -> Self {
        Self {
            history: touched_frames(ECHO_HISTORY_FRAMES),
            position: 0,
            valid: 0,
            current: 0.0,
            requested: 0.0,
            repeats: 3,
            requested_repeats: 3,
            restarting: 0,
            capture_fade: 0,
            configured: false,
            mix: Smooth::default(),
            weights: [0.0; 8],
            retired_wet: [Frame::SILENT; TRANSITION],
            retired_position: 0,
            retired_count: 0,
            retired: false,
        }
    }

    fn apply_requested(&mut self) {
        self.current = self.requested;
        self.repeats = self.requested_repeats;
        // Geometric weights normalised so the whole tail sums to one half.
        let mut weight = 0.5 / (1.0 - 1.0 / (1u64 << self.repeats) as f64);
        for tap in 0..self.repeats as usize {
            self.weights[tap] = weight;
            weight *= 0.5;
        }
    }

    pub fn configure(&mut self, setting: &EchoSettings) {
        self.requested = setting.time_ms * f64::from(SAMPLE_RATE) / 1000.0;
        self.requested_repeats = setting.repeats;
        if !self.configured {
            self.apply_requested();
            self.configured = true;
        }
        self.mix.set(setting.mix);
    }

    fn read(&self, delay: f64) -> Frame {
        let whole = delay as usize;
        let fraction = delay - whole as f64;
        let capacity = ECHO_HISTORY_FRAMES;
        let first = (self.position + capacity - whole) % capacity;
        let second = (first + capacity - 1) % capacity;
        let a = if whole <= self.valid { self.history[first] } else { Frame::SILENT };
        let b = if whole + 1 <= self.valid { self.history[second] } else { Frame::SILENT };
        frame(
            f64::from(a.left) * (1.0 - fraction) + f64::from(b.left) * fraction,
            f64::from(a.right) * (1.0 - fraction) + f64::from(b.right) * fraction,
        )
    }

    fn taps(&self, future: usize) -> Frame {
        let (mut left, mut right) = (0.0, 0.0);
        for tap in 0..self.repeats as usize {
            let value = self.read(self.current * (tap as f64 + 1.0) - future as f64);
            left += f64::from(value.left) * self.weights[tap];
            right += f64::from(value.right) * self.weights[tap];
        }
        frame(left, right)
    }

    fn requested_change(&self) -> bool {
        self.current != self.requested || self.repeats != self.requested_repeats
    }

    pub fn process(&mut self, input: Frame) -> EchoOutput {
        let mut delayed = Frame::SILENT;
        if self.retired {
            if self.retired_position < self.retired_count {
                delayed = self.retired_wet[self.retired_position];
                self.retired_position += 1;
            }
        } else {
            // Time/count edits fade the old tail, coalesce the latest request,
            // then discard its validity. No moving tap can replay old audio.
            if self.restarting == 0 && self.requested_change() {
                self.restarting = TRANSITION as u32;
            }
            delayed = self.taps(0);
            if self.restarting != 0 {
                self.restarting -= 1;
                let gain = f64::from(self.restarting) / TRANSITION as f64;
                delayed.left = (f64::from(delayed.left) * gain) as f32;
                delayed.right = (f64::from(delayed.right) * gain) as f32;
                if self.restarting == 0 {
                    self.apply_requested();
                    self.valid = 0;
                    self.position = 0;
                    self.capture_fade = TRANSITION as u32;
                }
            }
            let excitation = if self.capture_fade != 0 {
                self.capture_fade -= 1;
                1.0 - f64::from(self.capture_fade) / TRANSITION as f64
            } else {
                1.0
            };
            self.history[self.position] = frame(
                denormal(f64::from(input.left) * excitation),
                denormal(f64::from(input.right) * excitation),
            );
            self.position = (self.position + 1) % ECHO_HISTORY_FRAMES;
            self.valid = (self.valid + 1).min(ECHO_HISTORY_FRAMES - 1);
        }
        let amount = self.mix.next();
        EchoOutput {
            mixed: frame(
                f64::from(input.left) * (1.0 - amount) + f64::from(delayed.left) * amount,
                f64::from(input.right) * (1.0 - amount) + f64::from(delayed.right) * amount,
            ),
            dry: input,
            wet: delayed,
        }
    }

    fn copy_scalars(&mut self, previous: &Self) {
        self.position = previous.position;
        self.valid = previous.valid;
        self.current = previous.current;
        self.requested = previous.requested;
        self.repeats = previous.repeats;
        self.requested_repeats = previous.requested_repeats;
        self.restarting = previous.restarting;
        self.capture_fade = previous.capture_fade;
        self.configured = previous.configured;
        self.mix = previous.mix;
        self.weights = previous.weights;
        self.retired = previous.retired;
        self.retired_position = previous.retired_position;
        self.retired_count = previous.retired_count;
        if self.retired {
            self.retired_wet = previous.retired_wet;
        }
    }

    /// Copies only the initialised part of the logical history.
    pub fn inherit(&mut self, previous: &Self) {
        self.copy_scalars(previous);
        let capacity = ECHO_HISTORY_FRAMES;
        let begin = (self.position + capacity - self.valid) % capacity;
        let first = self.valid.min(capacity - begin);
        self.history[begin..begin + first].copy_from_slice(&previous.history[begin..begin + first]);
        let rest = self.valid - first;
        self.history[..rest].copy_from_slice(&previous.history[..rest]);
    }

    /// Engine-only ownership transfer of the long history. A fading
    /// predecessor keeps 10 ms of its own wet output.
    pub fn adopt(&mut self, previous: &mut Self, preserve_fade: bool) {
        self.copy_scalars(previous);
        if preserve_fade {
            // The engine fades an old graph for 480 frames. Echo's shortest
            // delay is 960 frames, so that whole wet continuation depends on
            // old history alone: cache it before transferring the buffer.
            let fade = if previous.restarting != 0 {
                previous.restarting as usize
            } else if previous.requested_change() {
                TRANSITION
            } else {
                0
            };
            for i in 0..TRANSITION {
                let wet = if fade != 0 && i + 1 >= fade { Frame::SILENT } else { previous.taps(i) };
                let gain = if fade != 0 { (fade - (i + 1).min(fade)) as f64 / TRANSITION as f64 } else { 1.0 };
                previous.retired_wet[i] = frame(f64::from(wet.left) * gain, f64::from(wet.right) * gain);
            }
        }
        std::mem::swap(&mut self.history, &mut previous.history);
        previous.retired = true;
        previous.retired_position = 0;
        previous.retired_count = if preserve_fade { TRANSITION } else { 0 };
    }

    pub fn reset(&mut self) {
        self.valid = 0;
        self.position = 0;
        self.current = 0.0;
        self.requested = 0.0;
        self.repeats = 3;
        self.requested_repeats = 3;
        self.restarting = 0;
        self.capture_fade = 0;
        self.configured = false;
        self.retired = false;
        self.retired_position = 0;
        self.retired_count = 0;
        self.mix = Smooth::default();
    }
}

impl Default for Echo {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn alignment_delays_by_exact_samples() {
        let mut delay = AlignmentDelay::new();
        delay.configure(3);
        let outputs: Vec<f32> = (1..=6).map(|n| delay.process(Frame::new(n as f32, 0.0)).left).collect();
        assert_eq!(outputs, vec![0.0, 0.0, 0.0, 1.0, 2.0, 3.0]);
    }

    #[test]
    fn echo_impulse_produces_weighted_repeats() {
        let mut echo = Echo::new();
        echo.configure(&EchoSettings { time_ms: 20.0, repeats: 2, mix: 1.0 });
        let mut wet = Vec::new();
        for n in 0..3000 {
            let input = if n == 0 { Frame::new(1.0, 1.0) } else { Frame::SILENT };
            wet.push(echo.process(input).wet.left);
        }
        // 20 ms = 960 frames; weights 0.5/(1-1/4) * {1, 1/2}.
        assert!((wet[960] - 2.0 / 3.0).abs() < 1e-6);
        assert!((wet[1920] - 1.0 / 3.0).abs() < 1e-6);
        assert_eq!(wet[2880], 0.0, "finite repeats");
    }

    #[test]
    fn adopt_transfers_history_without_copying() {
        let mut old = Echo::new();
        old.configure(&EchoSettings { time_ms: 20.0, repeats: 1, mix: 1.0 });
        old.process(Frame::new(1.0, 1.0));
        let mut new = Echo::new();
        new.adopt(&mut old, true);
        // The impulse was written at position 0; it is read 960 frames later.
        for _ in 0..959 {
            new.process(Frame::SILENT);
        }
        assert!((new.process(Frame::SILENT).wet.left - 1.0).abs() < 1e-6);
        // The predecessor plays only its cached 10 ms.
        for _ in 0..480 {
            old.process(Frame::SILENT);
        }
        assert_eq!(old.process(Frame::SILENT).wet, Frame::SILENT);
    }
}

impl std::fmt::Debug for AlignmentDelay {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AlignmentDelay").field("current", &self.current).field("requested", &self.requested).finish_non_exhaustive()
    }
}

impl std::fmt::Debug for Echo {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Echo").field("current", &self.current).field("repeats", &self.repeats).finish_non_exhaustive()
    }
}
