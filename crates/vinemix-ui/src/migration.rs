//! One-time migration of an Audio Mixer workspace into `%LOCALAPPDATA%\VineMix`
//! (`WorkspaceMigration`). Files are staged, hashed, validated by the engine
//! and committed through a journal, so an interruption at any point leaves
//! every original intact and resumes safely.

use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

const NAMES: [&str; 6] = ["settings.json", "settings.json.bak", "ui.json", "settings.json.established", "settings.json.new", "settings.json.v1.bak"];

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Entry {
    pub name: String,
    pub hash: String,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Journal {
    pub source: String,
    pub files: Vec<Entry>,
}

fn io(error: std::io::Error) -> String {
    error.to_string()
}

pub fn legacy_directories() -> Vec<PathBuf> {
    let Some(local) = std::env::var_os("LOCALAPPDATA").map(PathBuf::from) else { return Vec::new() };
    let mut candidates = vec![local.join("AudioMixer")];
    if let Ok(entries) = std::fs::read_dir(local.join("Packages")) {
        for entry in entries.flatten() {
            if entry.file_name().to_string_lossy().starts_with("OpenAI.Codex_") {
                candidates.push(entry.path().join("LocalCache").join("Local").join("AudioMixer"));
            }
        }
    }
    candidates.into_iter().filter(|c| has_workspace(c)).collect()
}

fn has_workspace(directory: &Path) -> bool {
    NAMES.iter().filter(|n| **n != "ui.json").any(|n| directory.join(n).is_file()) || directory.join("migration.complete.json").is_file()
}

fn hash(file: &Path) -> Result<String, String> {
    let bytes = std::fs::read(file).map_err(io)?;
    Ok(vinemix_update::crypto::hex(&vinemix_update::crypto::sha256(&bytes)?).to_uppercase())
}

fn describe(directory: &Path) -> Result<Vec<Entry>, String> {
    NAMES
        .iter()
        .filter(|n| directory.join(n).is_file())
        .map(|n| Ok(Entry { name: (*n).into(), hash: hash(&directory.join(n))? }))
        .collect()
}

fn durable_write(path: &Path, text: &str) -> Result<(), String> {
    vinemix_platform::create_new_durable(path, text.as_bytes()).map_err(|()| format!("Could not write {}.", path.display()))
}

fn copy_durable(from: &Path, to: &Path) -> Result<(), String> {
    use std::io::Write;
    let bytes = std::fs::read(from).map_err(io)?;
    let mut file = std::fs::OpenOptions::new().write(true).create_new(true).open(to).map_err(io)?;
    file.write_all(&bytes).map_err(io)?;
    file.sync_all().map_err(io)
}

fn interrupted(path: &Path) -> PathBuf {
    PathBuf::from(format!("{}.interrupted-{}", path.display(), crate::platform::random_hex_id()))
}

/// Runs the migration (`WorkspaceMigration.Run`).
pub fn run(destination: &Path, sources: &[PathBuf], select: &dyn Fn(&[PathBuf]) -> Option<PathBuf>, validate: &dyn Fn(&Path) -> Result<(), String>) -> Result<(), String> {
    let stage = destination.join(".migration-staging");
    let journal_path = stage.join("journal.json");
    let complete = destination.join("migration.complete.json");
    if complete.is_file() {
        return Ok(());
    }
    if !journal_path.is_file() {
        if has_workspace(destination) {
            return Ok(());
        }
        let mut candidates: Vec<PathBuf> = Vec::new();
        for source in sources.iter().filter(|s| has_workspace(s)) {
            if !candidates.iter().any(|c| crate::text::same_path(c, source)) {
                candidates.push(source.clone());
            }
        }
        let Some(first) = candidates.first().cloned() else { return Ok(()) };
        let mut source = first.clone();
        let signature = describe(&first)?;
        let mut differs = false;
        for candidate in candidates.iter().skip(1) {
            differs |= describe(candidate)? != signature;
        }
        if differs {
            source = select(&candidates).ok_or("Workspace migration was cancelled. Your existing files have not been changed.")?;
            if !candidates.iter().any(|c| crate::text::same_path(c, &source)) {
                return Err("Choose one of the existing workspace copies.".into());
            }
        }
        if stage.exists() {
            std::fs::rename(&stage, PathBuf::from(format!("{}.incomplete-{}", stage.display(), crate::platform::random_hex_id()))).map_err(io)?;
        }
        std::fs::create_dir_all(&stage).map_err(io)?;
        let entries = describe(&source)?;
        for entry in &entries {
            let from = source.join(&entry.name);
            let to = stage.join(&entry.name);
            copy_durable(&from, &to)?;
            if hash(&from)? != entry.hash || hash(&to)? != entry.hash {
                return Err("The source workspace changed while being copied. Retry after closing its application.".into());
            }
        }
        if describe(&source)? != entries {
            return Err("The source workspace changed during migration. Close it and retry.".into());
        }
        validate(&stage)?;
        let pending = stage.join("journal.pending.json");
        let journal = Journal { source: source.to_string_lossy().into_owned(), files: entries };
        durable_write(&pending, &serde_json::to_string(&journal).map_err(|e| e.to_string())?)?;
        std::fs::rename(&pending, &journal_path).map_err(io)?;
    }
    let journal: Journal = serde_json::from_slice(&std::fs::read(&journal_path).map_err(io)?)
        .map_err(|_| "The migration record is damaged. Original and staged workspace files have been retained.".to_owned())?;
    let mut names: Vec<&str> = journal.files.iter().map(|f| f.name.as_str()).collect();
    names.sort_unstable();
    names.dedup();
    let source = PathBuf::from(&journal.source);
    if journal.source.trim().is_empty() || !source.is_absolute() || journal.files.is_empty() || names.len() != journal.files.len() {
        return Err("The migration journal is incomplete.".into());
    }
    if has_workspace(&source) && describe(&source)? != journal.files {
        return Err("The legacy workspace changed after migration was interrupted. Both copies have been retained; do not restore the older staging copy over it.".into());
    }
    for existing in describe(destination)? {
        let marker = existing.name == "settings.json.established"
            && std::fs::read_to_string(destination.join(&existing.name)).is_ok_and(|t| t == "VineMix migrated workspace\n");
        if !journal.files.iter().any(|e| e.name == existing.name) && !marker {
            return Err("New workspace files appeared after migration was interrupted. No existing files were replaced.".into());
        }
    }
    for entry in &journal.files {
        if !NAMES.contains(&entry.name.as_str()) || hash(&stage.join(&entry.name))? != entry.hash {
            return Err("Migration staging is incomplete or damaged. Your original workspace is preserved.".into());
        }
        let target = destination.join(&entry.name);
        if target.is_file() && hash(&target)? != entry.hash {
            return Err("A newer workspace conflicts with the interrupted migration. No files were replaced.".into());
        }
    }
    validate(&stage)?;
    for entry in &journal.files {
        let target = destination.join(&entry.name);
        if !target.is_file() {
            let pending = destination.join(format!(".migration-{}.new", entry.name));
            if pending.exists() {
                std::fs::rename(&pending, interrupted(&pending)).map_err(io)?;
            }
            copy_durable(&stage.join(&entry.name), &pending)?;
            if hash(&pending)? != entry.hash {
                return Err("The staged workspace could not be committed intact.".into());
            }
            std::fs::rename(&pending, &target).map_err(io)?;
        }
    }
    let established = destination.join("settings.json.established");
    if !established.is_file() {
        durable_write(&established, "VineMix migrated workspace\n")?;
    }
    let pending_complete = destination.join(".migration-complete.new");
    if pending_complete.exists() {
        std::fs::rename(&pending_complete, interrupted(&pending_complete)).map_err(io)?;
    }
    durable_write(&pending_complete, &serde_json::to_string(&journal).map_err(|e| e.to_string())?)?;
    std::fs::rename(&pending_complete, &complete).map_err(io)
}

/// Asks the engine to validate the staged settings without starting audio.
pub fn validate_staging(stage: &Path, engine: &Path) -> Result<(), String> {
    let output = std::process::Command::new(engine)
        .arg("--validate-settings")
        .arg(stage.join("settings.json"))
        .current_dir(engine.parent().unwrap_or(Path::new(".")))
        .stdin(std::process::Stdio::null())
        .output()
        .map_err(|_| "The staged workspace could not be validated.".to_owned())?;
    match output.status.code() {
        Some(0 | 2) => {}
        _ => return Err(format!("The workspace validator failed: {}", String::from_utf8_lossy(&output.stderr))),
    }
    let report: serde_json::Value =
        serde_json::from_slice(&output.stdout).map_err(|_| "The workspace validator did not return a readable result.".to_owned())?;
    match report["settingsState"].as_str() {
        Some("loaded" | "backupAvailable" | "recoveryRequired" | "unsupported") => Ok(()),
        _ => Err("The staged workspace did not have an established recovery identity.".into()),
    }
}

/// Lets the user choose between differing workspace copies.
pub fn choose_source(sources: &[PathBuf]) -> Option<PathBuf> {
    use windows::core::PCWSTR;
    use windows::Win32::UI::WindowsAndMessaging::{MessageBoxW, IDCANCEL, IDYES, MB_ICONQUESTION, MB_YESNOCANCEL};
    let title = crate::platform::wide("VineMix · Choose your saved workspace");
    for (index, source) in sources.iter().enumerate() {
        let text = crate::platform::wide(&format!(
            "Different saved workspaces were found. Choose the copy you want to keep using. Both originals will be preserved.\n\nWorkspace {} of {}:\n{}\n\nYes uses this workspace. No shows the next copy. Cancel stops without changing anything.",
            index + 1,
            sources.len(),
            source.display()
        ));
        // SAFETY: valid strings; no owner window.
        let answer = unsafe { MessageBoxW(None, PCWSTR(text.as_ptr()), PCWSTR(title.as_ptr()), MB_YESNOCANCEL | MB_ICONQUESTION) };
        if answer == IDYES {
            return Some(source.clone());
        }
        if answer == IDCANCEL {
            return None;
        }
    }
    None
}

/// The desktop's startup migration guard (`App.OnStartup`).
pub fn run_if_needed(scope: &str, engine: &Path) -> Result<(), String> {
    let destination = crate::paths::data_directory();
    match crate::platform::SessionOwnership::acquire("Engine", scope) {
        Some(guard) => {
            std::fs::create_dir_all(&destination).map_err(io)?;
            let result = run(&destination, &legacy_directories(), &choose_source, &|stage| validate_staging(stage, engine));
            drop(guard);
            result
        }
        None => {
            let running = crate::supervisor::validate_engine(engine)?;
            if !running || (destination.join(".migration-staging").exists() && !destination.join("migration.complete.json").is_file()) {
                return Err("Quit the existing VineMix session before migrating this workspace.".into());
            }
            Ok(())
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn migrates_once_with_journal() {
        let root = std::env::temp_dir().join(format!("vinemix-migration-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let source = root.join("AudioMixer");
        let destination = root.join("VineMix");
        std::fs::create_dir_all(&source).unwrap();
        std::fs::create_dir_all(&destination).unwrap();
        std::fs::write(source.join("settings.json"), b"{}").unwrap();
        std::fs::write(source.join("ui.json"), b"{}").unwrap();
        run(&destination, std::slice::from_ref(&source), &|_| None, &|_| Ok(())).unwrap();
        assert!(destination.join("migration.complete.json").is_file());
        assert_eq!(std::fs::read(destination.join("settings.json")).unwrap(), b"{}");
        assert!(source.join("settings.json").is_file(), "originals are kept");
        run(&destination, &[source], &|_| None, &|_| Ok(())).unwrap();
        let _ = std::fs::remove_dir_all(&root);
    }
}
