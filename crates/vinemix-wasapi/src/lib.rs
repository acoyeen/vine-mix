//! Windows audio I/O for the VineMix engine.
//!
//! * [`catalog::DeviceCatalog`]: cached endpoint inventory with notifications.
//! * [`worker::AudioWorker`]: one shared- or exclusive-mode device stream.
//! * [`process::ProcessCaptureWorker`]: per-application loopback capture.
//!
//! [`format`], [`policy`] and [`clock`] are platform-free and unit tested.

// Speaker downmix weights keep the 0.5 engine's literal 0.70710678 so mixes
// match it bit for bit.
#![allow(clippy::approx_constant)]

pub mod clock;
pub mod format;
pub mod policy;

#[cfg(windows)]
pub mod catalog;
#[cfg(windows)]
pub mod process;
#[cfg(windows)]
pub mod stream;
#[cfg(windows)]
pub mod win;
#[cfg(windows)]
pub mod worker;

#[cfg(windows)]
pub use catalog::DeviceCatalog;
#[cfg(windows)]
pub use process::{enumerate_audio_applications, process_capture_available, ProcessCaptureWorker};
#[cfg(windows)]
pub use worker::{AudioWorker, QueueEnd};
