//! Realtime primitives shared by the engine's mixer and device threads.
//!
//! Ported from `native/include/am/{dsp,audio_timing,mix_clock,stream_stability}.hpp`
//! and `native/src/dsp.cpp`. Nothing in this crate allocates, locks or waits
//! after construction, and nothing depends on Windows, so every policy can be
//! tested on any platform.

pub mod drift;
pub mod frame_queue;
pub mod mix_clock;
pub mod stream_stability;
pub mod timing;

pub use drift::DriftController;
pub use frame_queue::{frame_queue, Consumer, Monitor, Producer};
pub use mix_clock::MixClock;
pub use stream_stability::StreamStability;
pub use timing::{CaptureBufferReader, StreamBufferPolicy};

/// Internal graph sample rate. Device rates are converted at the stream edge.
pub const SAMPLE_RATE: u32 = 48_000;
/// Frames per mixer block: 5 ms at 48 kHz.
pub const QUANTUM_FRAMES: usize = 240;

/// One stereo sample frame, laid out exactly like the C++ `am::Frame`.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[repr(C)]
pub struct Frame {
    pub left: f32,
    pub right: f32,
}

impl Frame {
    pub const SILENT: Frame = Frame { left: 0.0, right: 0.0 };

    #[inline]
    pub const fn new(left: f32, right: f32) -> Self {
        Self { left, right }
    }
}
