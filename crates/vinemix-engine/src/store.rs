//! The settings store (`ConfigurationStore` in `configuration.cpp`).
//!
//! Commits are crash-safe: the workspace is established with a marker
//! before the first commit, each save writes and flushes `settings.json.new`,
//! then atomically replaces the primary while keeping the previous file as
//! `.bak`. Damaged primaries are preserved as `.corrupt-<tick>`, a version-1
//! original as `.v1.bak`, and recovery copies everything into a unique
//! `.recovery-<tick>` folder before it writes anything.

use std::path::{Path, PathBuf};

use vinemix_model::config::Configuration;
use vinemix_model::defaults::default_configuration;
use vinemix_model::limits::MAXIMUM_SETTINGS_BYTES;
use vinemix_model::validate::validate_configuration;
use vinemix_model::{json, ModelError};

/// How the store was found (`loadState` on the wire).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum LoadState {
    New,
    Loaded,
    BackupAvailable,
    RecoveryRequired,
    Unsupported,
}

impl LoadState {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::New => "new",
            Self::Loaded => "loaded",
            Self::BackupAvailable => "backupAvailable",
            Self::RecoveryRequired => "recoveryRequired",
            Self::Unsupported => "unsupported",
        }
    }
}

/// A store error. `write` failures are retryable (`settings_write_failed`);
/// everything else is a permanent rejection.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct StoreError {
    pub message: String,
    pub write: bool,
}

impl StoreError {
    fn permanent(message: impl Into<String>) -> Self {
        Self { message: message.into(), write: false }
    }

    fn write(message: impl Into<String>) -> Self {
        Self { message: message.into(), write: true }
    }
}

impl From<ModelError> for StoreError {
    fn from(error: ModelError) -> Self {
        Self::permanent(error.message)
    }
}

impl std::fmt::Display for StoreError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}

impl std::error::Error for StoreError {}

fn suffixed(path: &Path, suffix: &str) -> PathBuf {
    let mut text = path.as_os_str().to_owned();
    text.push(suffix);
    PathBuf::from(text)
}

/// Serialises a configuration exactly as the store writes it.
pub fn settings_text(configuration: &Configuration) -> String {
    json::dump_pretty(&configuration.to_json()) + "\n"
}

#[derive(Debug)]
pub struct ConfigurationStore {
    path: PathBuf,
    warning: String,
    load_state: LoadState,
    inspected: bool,
    primary_valid: bool,
    legacy_source: Option<PathBuf>,
}

/// Reads and validates one settings file. `Unsupported` errors keep their kind.
fn read_settings(path: &Path) -> Result<(Configuration, bool), ModelError> {
    let size = std::fs::metadata(path).map_err(|e| ModelError::invalid(e.to_string()))?.len();
    if size > MAXIMUM_SETTINGS_BYTES as u64 {
        return Err(ModelError::invalid("Settings file is too large."));
    }
    let bytes = std::fs::read(path).map_err(|_| ModelError::invalid("Cannot open settings."))?;
    let document = json::parse(&bytes)?;
    let configuration = Configuration::from_json(&document)?;
    validate_configuration(&configuration)?;
    let legacy = document.get("version").and_then(serde_json::Value::as_i64) == Some(1);
    Ok((configuration, legacy))
}

impl ConfigurationStore {
    /// A store at `path`, or the per-user default when `None`.
    pub fn new(path: Option<PathBuf>) -> Result<Self, StoreError> {
        let path = match path {
            Some(path) if !path.as_os_str().is_empty() => path,
            _ => application_data_directory()?.join("settings.json"),
        };
        Ok(Self {
            path,
            warning: String::new(),
            load_state: LoadState::New,
            inspected: false,
            primary_valid: true,
            legacy_source: None,
        })
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn load_state(&self) -> LoadState {
        self.load_state
    }

    pub fn warning(&self) -> &str {
        &self.warning
    }

    pub fn writable(&self) -> bool {
        matches!(self.load_state, LoadState::New | LoadState::Loaded)
    }

    pub fn backup_path(&self) -> PathBuf {
        suffixed(&self.path, ".bak")
    }

    /// The opened file's physical location (see `vinemix_platform::physical_path`).
    pub fn physical_path(&self) -> Result<PathBuf, String> {
        physical_path(&self.path)
    }

    fn read(&mut self, path: &Path) -> Result<Configuration, ModelError> {
        let (configuration, legacy) = read_settings(path)?;
        if legacy {
            self.legacy_source = Some(path.to_path_buf());
        }
        Ok(configuration)
    }

    /// Loads the workspace, falling back to the backup for review, or to
    /// defaults with audio paused. Never writes.
    pub fn load(&mut self) -> Configuration {
        self.inspected = true;
        // A reload after external repair must not carry old flags forward.
        self.warning.clear();
        self.load_state = LoadState::Loaded;
        self.primary_valid = true;
        self.legacy_source = None;
        let backup = suffixed(&self.path, ".bak");
        let markers = [&self.path, &backup, &suffixed(&self.path, ".established"), &suffixed(&self.path, ".new"), &suffixed(&self.path, ".v1.bak")];
        if markers.iter().all(|path| !path.exists()) {
            // A missing primary with any marker is recoverable; only a
            // genuinely new store gets defaults.
            self.load_state = LoadState::New;
            return default_configuration();
        }
        let path = self.path.clone();
        match self.read(&path) {
            Ok(configuration) => configuration,
            Err(error) if error.is_unsupported() => {
                self.load_state = LoadState::Unsupported;
                self.primary_valid = false;
                self.warning = error.message;
                default_configuration()
            }
            Err(_) => {
                self.primary_valid = false;
                match self.read(&backup) {
                    Ok(configuration) => {
                        self.load_state = LoadState::BackupAvailable;
                        self.warning = "Settings could not be loaded. A backup is available for review; audio is paused until you choose recovery.".into();
                        configuration
                    }
                    Err(error) if error.is_unsupported() => {
                        self.load_state = LoadState::Unsupported;
                        self.warning = error.message;
                        default_configuration()
                    }
                    Err(_) => {
                        self.load_state = LoadState::RecoveryRequired;
                        self.warning = "Your workspace could not be loaded. Audio and saving are paused. Retry or start fresh after preserving the originals.".into();
                        default_configuration()
                    }
                }
            }
        }
    }

    /// `retry`, `restoreBackup` or `startFresh`, preserving every original.
    pub fn recover(&mut self, action: &str) -> Result<Configuration, StoreError> {
        if self.writable() {
            return Err(StoreError::permanent("The workspace does not need recovery."));
        }
        if action == "retry" {
            return Ok(self.load());
        }
        if self.load_state == LoadState::Unsupported {
            return Err(StoreError::permanent("Open this workspace with a compatible newer version of VineMix."));
        }
        if action != "restoreBackup" && action != "startFresh" {
            return Err(StoreError::permanent("Unknown recovery action."));
        }
        let mut candidate = default_configuration();
        if action == "restoreBackup" {
            // Re-read and validate the backup itself; never commit a preview.
            let backup = suffixed(&self.path, ".bak");
            let size = std::fs::metadata(&backup).map_err(|e| StoreError::permanent(e.to_string()))?.len();
            if size > MAXIMUM_SETTINGS_BYTES as u64 {
                return Err(StoreError::permanent("Backup is too large."));
            }
            let bytes = std::fs::read(&backup).map_err(|e| StoreError::permanent(e.to_string()))?;
            candidate = Configuration::from_json(&json::parse(&bytes)?)?;
            validate_configuration(&candidate)?;
        }
        let preserved = suffixed(&self.path, &format!(".recovery-{}", tick_count()));
        std::fs::create_dir(&preserved).map_err(|_| StoreError::permanent("Cannot create a unique recovery folder."))?;
        for suffix in ["", ".bak", ".established", ".new", ".v1.bak"] {
            let source = suffixed(&self.path, suffix);
            if source.exists() {
                let name = source.file_name().map(std::ffi::OsStr::to_os_string).unwrap_or_default();
                std::fs::copy(&source, preserved.join(name)).map_err(|e| StoreError::permanent(e.to_string()))?;
            }
        }
        let previous = self.load_state;
        self.load_state = LoadState::Loaded;
        if let Err(error) = self.save(&candidate) {
            self.load_state = previous;
            return Err(error);
        }
        self.warning.clear();
        Ok(candidate)
    }

    /// Pauses saving and audio after a failed post-recovery start.
    pub fn pause_for_recovery(&mut self, message: &str) {
        self.load_state = LoadState::RecoveryRequired;
        self.warning = message.into();
    }

    /// Validates, serialises and durably commits a configuration.
    pub fn save(&mut self, configuration: &Configuration) -> Result<(), StoreError> {
        if !self.inspected {
            let _ = self.load();
        }
        if !self.writable() {
            return Err(StoreError::permanent("Choose workspace recovery before saving any changes."));
        }
        let (validated, _) = vinemix_model::validate::validate_and_compile(configuration)?;
        self.save_validated(validated)
    }

    /// Serialises and durably commits a configuration whose validation the
    /// caller already holds proof of. The store's boundary is unchanged:
    /// nothing unvalidated can reach it, because a [`Validated`] exists only
    /// for a configuration that passed full validation and cannot have been
    /// modified since.
    pub fn save_validated(&mut self, configuration: vinemix_model::validate::Validated<'_>) -> Result<(), StoreError> {
        let configuration = configuration.configuration();
        if !self.inspected {
            let _ = self.load();
        }
        if !self.writable() {
            return Err(StoreError::permanent("Choose workspace recovery before saving any changes."));
        }
        // A commit must also be readable on the next launch.
        let text = settings_text(configuration);
        if text.len() > MAXIMUM_SETTINGS_BYTES {
            return Err(StoreError::permanent(
                "Settings are too large to save. Shorten saved application or device details.",
            ));
        }
        // Only persistence failures below are retryable.
        if let Some(parent) = self.path.parent().filter(|p| !p.as_os_str().is_empty()) {
            std::fs::create_dir_all(parent).map_err(|e| StoreError::write(e.to_string()))?;
        }
        // Establish the store before committing: an interrupted first commit
        // must not look like a new workspace on the next launch.
        let established = suffixed(&self.path, ".established");
        if !established.exists() && create_new_durable(&established, b"VineMix workspace\n").is_err() {
            return Err(StoreError::write("Cannot record the workspace identity."));
        }
        if let Some(legacy) = &self.legacy_source {
            let preserved = suffixed(&self.path, ".v1.bak");
            if !preserved.exists() && !copy_new(legacy, &preserved) {
                return Err(StoreError::write("Cannot preserve the original mixer settings before upgrading the canvas."));
            }
        }
        let temporary = suffixed(&self.path, ".new");
        let backup = suffixed(&self.path, ".bak");
        if write_file_durable(&temporary, text.as_bytes()).is_err() {
            return Err(StoreError::write("Settings could not be written completely."));
        }
        if self.path.exists() {
            if !self.primary_valid {
                let damaged = suffixed(&self.path, &format!(".corrupt-{}", tick_count()));
                if !copy_new(&self.path, &damaged) {
                    return Err(StoreError::write("Cannot preserve the damaged settings file."));
                }
            }
            let keep = self.primary_valid.then_some(backup.as_path());
            if !replace_file(&self.path, &temporary, keep) {
                return Err(StoreError::write("Cannot replace settings; the previous file was preserved."));
            }
        } else if !move_write_through(&temporary, &self.path) {
            return Err(StoreError::write("Cannot commit the settings file."));
        }
        self.primary_valid = true;
        self.load_state = LoadState::Loaded;
        self.legacy_source = None;
        Ok(())
    }
}

/// `--validate-settings`: loads without writing and reports the outcome.
pub fn validate_settings_file(path: &Path) -> (ConfigurationStore, Configuration) {
    let mut store = ConfigurationStore {
        path: path.to_path_buf(),
        warning: String::new(),
        load_state: LoadState::New,
        inspected: false,
        primary_valid: true,
        legacy_source: None,
    };
    let configuration = store.load();
    (store, configuration)
}

#[cfg(windows)]
use vinemix_platform::{
    application_data_directory as platform_data_directory, copy_new, create_new_durable, move_write_through, physical_path, replace_file,
    tick_count, write_file_durable,
};

#[cfg(windows)]
fn application_data_directory() -> Result<PathBuf, StoreError> {
    platform_data_directory().map_err(StoreError::permanent)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_store(name: &str) -> (PathBuf, ConfigurationStore) {
        let directory = std::env::temp_dir().join(format!("vinemix-store-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&directory);
        std::fs::create_dir_all(&directory).unwrap();
        let path = directory.join("settings.json");
        (directory, ConfigurationStore::new(Some(path)).unwrap())
    }

    #[test]
    fn new_store_saves_and_reloads() {
        let (directory, mut store) = temp_store("new");
        let configuration = store.load();
        assert_eq!(store.load_state(), LoadState::New);
        store.save(&configuration).unwrap();
        assert!(directory.join("settings.json.established").exists());
        let mut again = ConfigurationStore::new(Some(directory.join("settings.json"))).unwrap();
        assert_eq!(again.load(), configuration);
        assert_eq!(again.load_state(), LoadState::Loaded);
        // A second save keeps the previous file as the backup.
        again.save(&configuration).unwrap();
        assert!(directory.join("settings.json.bak").exists());
        let _ = std::fs::remove_dir_all(directory);
    }

    #[test]
    fn damaged_primary_offers_backup_and_preserves_originals() {
        let (directory, mut store) = temp_store("damaged");
        let configuration = store.load();
        store.save(&configuration).unwrap();
        store.save(&configuration).unwrap();
        std::fs::write(directory.join("settings.json"), b"{ not json").unwrap();
        let mut broken = ConfigurationStore::new(Some(directory.join("settings.json"))).unwrap();
        broken.load();
        assert_eq!(broken.load_state(), LoadState::BackupAvailable);
        assert!(broken.save(&configuration).is_err(), "no saving before recovery");
        let restored = broken.recover("restoreBackup").unwrap();
        assert_eq!(restored, configuration);
        assert_eq!(broken.load_state(), LoadState::Loaded);
        let names: Vec<String> =
            std::fs::read_dir(&directory).unwrap().map(|e| e.unwrap().file_name().to_string_lossy().into_owned()).collect();
        assert!(names.iter().any(|n| n.starts_with("settings.json.recovery-")));
        assert!(names.iter().any(|n| n.starts_with("settings.json.corrupt-")));
        let _ = std::fs::remove_dir_all(directory);
    }

    #[test]
    fn future_workspace_is_never_overwritten() {
        let (directory, mut store) = temp_store("future");
        let mut value = default_configuration().to_json();
        value["version"] = serde_json::json!(3);
        std::fs::write(directory.join("settings.json"), serde_json::to_vec(&value).unwrap()).unwrap();
        store.load();
        assert_eq!(store.load_state(), LoadState::Unsupported);
        assert_eq!(store.recover("startFresh").unwrap_err().message, "Open this workspace with a compatible newer version of VineMix.");
        let _ = std::fs::remove_dir_all(directory);
    }

    #[test]
    fn version_one_migrates_and_keeps_the_original() {
        let (directory, mut store) = temp_store("legacy");
        let mut legacy = Configuration { version: 1, ..Configuration::default() }.to_json();
        legacy.as_object_mut().unwrap().remove("graph");
        std::fs::write(directory.join("settings.json"), serde_json::to_vec(&legacy).unwrap()).unwrap();
        let configuration = store.load();
        assert_eq!(configuration.version, 2);
        assert_eq!(configuration.graph.nodes.len(), 8);
        store.save(&configuration).unwrap();
        assert!(directory.join("settings.json.v1.bak").exists());
        let _ = std::fs::remove_dir_all(directory);
    }
}
