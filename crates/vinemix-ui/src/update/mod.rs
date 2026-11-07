//! Update installation: the private transaction shared with the update
//! worker, the worker itself and its Windows effects (`UpdateTransaction`,
//! `UpdateWorker`, `WindowsUpdatePlatform`).

pub mod files;
pub mod model;
pub mod session;
pub mod transaction;
pub mod windows;
pub mod worker;

#[cfg(test)]
mod tests;
