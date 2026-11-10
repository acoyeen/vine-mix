//! Workspace locations (`WorkspacePaths`) and the desktop lifetime log
//! (`AppLifetimeLog`).

use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock, RwLock};

static CUSTOM_SETTINGS: RwLock<Option<PathBuf>> = RwLock::new(None);

pub fn set_custom_settings(path: Option<PathBuf>) {
    *CUSTOM_SETTINGS.write().unwrap_or_else(std::sync::PoisonError::into_inner) = path;
}

pub fn custom_settings() -> Option<PathBuf> {
    CUSTOM_SETTINGS.read().unwrap_or_else(std::sync::PoisonError::into_inner).clone()
}

/// `%LOCALAPPDATA%\VineMix`, or why it cannot be located.
pub fn try_data_directory() -> Result<PathBuf, String> {
    vinemix_platform::application_data_directory()
}

/// Stands in for the data directory when it cannot be located: an absolute
/// path whose name Windows rejects (`<` and `>` are never valid in a file
/// name), so nothing exists there and nothing can be created. Reads find no
/// file and writes fail visibly; nothing is ever read from or written to a
/// folder relative to the working directory.
pub fn unavailable_data_directory() -> PathBuf {
    PathBuf::from(r"C:\<VineMix data location unavailable>")
}

/// `%LOCALAPPDATA%\VineMix`. `main` refuses to start a normal session when
/// [`try_data_directory`] fails, so the inert stand-in is only ever seen by
/// code paths that then report their own read or write failure.
pub fn data_directory() -> PathBuf {
    try_data_directory().unwrap_or_else(|_| unavailable_data_directory())
}

pub fn settings() -> PathBuf {
    custom_settings().unwrap_or_else(|| data_directory().join("settings.json"))
}

/// `ui.json` beside the settings file.
pub fn preferences() -> PathBuf {
    settings().parent().map_or_else(|| data_directory().join("ui.json"), |p| p.join("ui.json"))
}

pub fn absolute(path: &Path) -> PathBuf {
    std::path::absolute(path).unwrap_or_else(|_| path.to_path_buf())
}

/// Bounded local evidence for exits that cannot otherwise be reconstructed.
pub mod lifetime_log {
    use super::*;
    use std::io::Write;

    static PATH: OnceLock<PathBuf> = OnceLock::new();
    static GATE: Mutex<()> = Mutex::new(());

    pub fn start() {
        let Some(directory) = super::settings().parent().map(Path::to_path_buf) else { return };
        if std::fs::create_dir_all(&directory).is_err() {
            return;
        }
        let path = directory.join("desktop.log");
        if std::fs::metadata(&path).is_ok_and(|m| m.len() > 512 * 1024) {
            let _ = std::fs::rename(&path, directory.join("desktop.log.previous"));
        }
        let _ = PATH.set(path);
        write("Started");
    }

    pub fn write(message: &str) {
        let Some(path) = PATH.get() else { return };
        let _guard = GATE.lock();
        let now = crate::time::local_timestamp();
        if let Ok(mut file) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
            let _ = writeln!(file, "{now} pid={} {message}", std::process::id());
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// RUST-REV-029: the stand-in used when the data location cannot be
    /// resolved is absolute and inert from any working directory.
    #[test]
    fn unavailable_data_directory_is_absolute_and_inert() {
        let directory = unavailable_data_directory();
        assert!(directory.is_absolute(), "{directory:?}");
        assert!(!directory.join("ui.json").is_file());
        assert!(std::fs::create_dir_all(directory.join("updates")).is_err(), "nothing can be created there");
        assert!(std::fs::write(directory.join("ui.json"), b"{}").is_err());
        // Joining never yields a working-directory-relative path.
        assert_eq!(absolute(&directory.join("settings.json")), directory.join("settings.json"));
    }
}
