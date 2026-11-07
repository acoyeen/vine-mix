//! Noise suppression for VineMix, entirely in Rust.
//!
//! * [`SpeexPreprocessor`]: SpeexDSP 1.2.1's spectral noise suppressor
//!   (floating-point build, smallft FFT).
//! * [`RnNoise`]: RNNoise v0.2 with the upstream `0b50c45` model.
//!
//! Both are faithful ports of the pinned C libraries the 0.5 engine linked,
//! verified sample-for-sample against them by the reference tests in
//! `tests/`. All state is allocated by the constructors; processing and
//! state copies never allocate.

// The ports keep the C libraries' literal constants (twiddles, 2*pi, 1/sqrt 2)
// so their output stays bit-exact with the references.
#![allow(clippy::approx_constant, clippy::excessive_precision)]

pub mod rnnoise;
pub mod speex;

pub use rnnoise::{Kernel, RnNoise};
pub use speex::SpeexPreprocessor;
