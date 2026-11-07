//! Windows platform services shared by the engine, the desktop and the
//! updater: the current user SID, per-user paths, the control pipe name,
//! the single-instance lock, release metadata and durable file commits.

pub mod release;

#[cfg(windows)]
mod windows_impl;

#[cfg(windows)]
pub use windows_impl::*;

/// UTF-16 with a terminating NUL for Win32 calls.
pub fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}
