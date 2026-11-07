//! Release identity, read from `release.json` at build time (the same file
//! the 0.5 CMake and .NET builds read).

/// The release version, for example `0.5.0-beta`.
pub const VERSION: &str = env!("VINEMIX_RELEASE_VERSION");
/// Four-part numeric version for Windows file resources.
pub const NUMERIC_VERSION: &str = env!("VINEMIX_NUMERIC_VERSION");
/// The display version, for example `0.5.0 · Beta`.
pub const DISPLAY_VERSION: &str = env!("VINEMIX_DISPLAY_VERSION");
