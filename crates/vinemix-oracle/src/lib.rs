//! Test-only parity oracle: the unmodified 0.5 C++ graph processor,
//! SpeexDSP 1.2.1 and RNNoise v0.2 (and libsamplerate 0.2.2), compiled with the
//! release flags the 0.5 build used. Requires the pinned sources fetched by
//! `scripts/fetch-reference-sources.ps1`; without them the crate builds empty
//! and its tests are skipped.

#![allow(unsafe_code)]

/// Whether the reference sources were found when this crate was built.
pub const AVAILABLE: bool = cfg!(oracle_available);

#[cfg(oracle_available)]
pub mod ffi;
