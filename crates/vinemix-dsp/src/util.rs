//! Small numeric helpers shared by the processor and effects.

use vinemix_rt::Frame;

/// Replaces NaN and infinities with silence.
#[inline]
pub fn clean_f32(sample: f32) -> f32 {
    if sample.is_finite() { sample } else { 0.0 }
}

#[inline]
pub fn clean(sample: f64) -> f64 {
    if sample.is_finite() { sample } else { 0.0 }
}

/// Flushes denormal-range values so decaying tails never become an
/// unpredictable CPU load.
#[inline]
pub fn denormal(value: f64) -> f64 {
    if value.abs() < 1e-30 { 0.0 } else { value }
}

/// Decibels to linear gain in single precision (`linear` in `graph.cpp`).
#[inline]
pub fn linear(db: f32) -> f32 {
    10f32.powf(db.clamp(-180.0, 36.0) / 20.0)
}

/// Saturates to the representable range, or to full scale at a sink.
#[inline]
pub fn bounded(sample: f64, sink: bool) -> f32 {
    let maximum = if sink { 1.0 } else { f64::from(f32::MAX) };
    sample.clamp(-maximum, maximum) as f32
}

/// A one-pole parameter smoother with a fixed 480-frame (10 ms) ramp. The
/// first value is adopted immediately.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Smooth {
    pub value: f64,
    pub target: f64,
    pub step: f64,
    pub remaining: u32,
    pub initialized: bool,
}

impl Smooth {
    pub const RAMP: u32 = 480;

    #[inline]
    pub fn set(&mut self, next: f64) {
        if !self.initialized {
            self.value = next;
            self.target = next;
            self.initialized = true;
            return;
        }
        if self.target == next {
            return;
        }
        self.target = next;
        self.remaining = Self::RAMP;
        self.step = (self.target - self.value) / f64::from(Self::RAMP);
    }

    #[inline]
    pub fn next(&mut self) -> f64 {
        if self.remaining != 0 {
            self.remaining -= 1;
            if self.remaining == 0 {
                self.value = self.target;
            } else {
                self.value += self.step;
            }
        }
        self.value
    }
}

/// Allocates a frame buffer and writes every element now, so its pages are
/// resident before the audio thread first touches them.
pub fn touched_frames(count: usize) -> Box<[Frame]> {
    let mut frames = Vec::with_capacity(count);
    frames.resize(count, Frame::SILENT);
    std::hint::black_box(&mut frames);
    frames.into_boxed_slice()
}

#[inline]
pub fn frame(left: f64, right: f64) -> Frame {
    Frame::new(left as f32, right as f32)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn smooth_adopts_first_value_then_ramps() {
        let mut smooth = Smooth::default();
        smooth.set(1.0);
        assert_eq!(smooth.next(), 1.0);
        smooth.set(2.0);
        let mut last = 0.0;
        for _ in 0..479 {
            last = smooth.next();
        }
        assert!(last < 2.0);
        assert_eq!(smooth.next(), 2.0, "the final step lands exactly");
        assert_eq!(smooth.next(), 2.0);
    }

    #[test]
    fn bounded_and_linear() {
        assert_eq!(bounded(2.0, true), 1.0);
        assert_eq!(bounded(1e300, false), f32::MAX);
        assert_eq!(linear(0.0), 1.0);
        assert_eq!(denormal(1e-31), 0.0);
    }
}
