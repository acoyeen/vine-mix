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

/// `%LOCALAPPDATA%\VineMix`.
pub fn data_directory() -> PathBuf {
    vinemix_platform::application_data_directory().unwrap_or_else(|_| PathBuf::from("VineMix"))
}

pub fn settings() -> PathBuf {
    custom_settings().unwrap_or_else(|| data_directory().join("settings.json"))
}

/// `ui.json` beside the settings file.
pub fn preferences() -> PathBuf {
    settings().parent().map_or_else(|| PathBuf::from("ui.json"), |p| p.join("ui.json"))
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
