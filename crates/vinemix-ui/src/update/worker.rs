//! The bounded, journaled update worker (`UpdateWorker`), run as the copied
//! original application with `--apply-update <transaction>`. No process is
//! killed and no personal settings are restored.

use std::collections::{BTreeMap, HashSet};
use std::os::windows::fs::OpenOptionsExt;
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use vinemix_update::Trust;
use windows::Win32::Storage::FileSystem::{FILE_FLAG_OPEN_REPARSE_POINT, FILE_READ_ATTRIBUTES, FILE_WRITE_ATTRIBUTES};

use super::files::{self, Result};
use super::model::{Backup, FileEntry, Journal, Phase, ProcessIdentity, RegistrySnapshot, UtcTime};
use super::transaction::{self, Verified};

/// Worker side effects, replaceable in tests (`IUpdateWorkerPlatform`).
pub trait Platform {
    fn acquire_lock(&self) -> Result<Lock>;
    /// Verify the identity once and poll the retained process object.
    fn watch_live_process(&self, identity: &ProcessIdentity) -> Box<dyn Fn() -> bool + '_>;
    fn wait_for_exit(&self, identity: &ProcessIdentity, timeout: Duration) -> Result<bool>;
    fn verify_worker(&self, context: &Verified) -> Result<()>;
    fn check_file_version(&self, path: &Path, numeric_version: &str) -> Result<()>;
    fn read_registration(&self, context: &Verified) -> Result<Option<RegistrySnapshot>>;
    fn restore_registration(&self, original: &RegistrySnapshot, expected_current: Option<&RegistrySnapshot>) -> Result<()>;
    fn register_recovery(&self, context: &Verified, attempt: i32) -> Result<()>;
    fn clear_recovery(&self, context: &Verified) -> Result<()>;
    fn run_setup(&self, context: &Verified, started: &mut dyn FnMut(ProcessIdentity) -> Result<()>) -> Result<i32>;
    fn run_smoke(&self, context: &Verified, evidence: &Path, started: &mut dyn FnMut(ProcessIdentity) -> Result<()>) -> Result<bool>;
    fn candidate_engine_hash(&self, context: &Verified, evidence: &Path) -> Result<String>;
    fn start_candidate(&self, context: &Verified) -> Result<ProcessIdentity>;
    fn reopen_previous(&self, context: &Verified) -> Result<()>;
    fn wait_for_candidate_audio_exit(&self, context: &Verified, expected_engine_sha256: Option<&str>, timeout: Duration) -> Result<bool>;
    fn delay(&self, milliseconds: u64);
}

/// A held lock, released when dropped.
pub struct Lock(Option<Box<dyn FnOnce()>>);

impl Lock {
    pub fn new(release: impl FnOnce() + 'static) -> Self {
        Self(Some(Box::new(release)))
    }

    /// A lock with nothing to release (tests).
    pub fn none() -> Self {
        Self(None)
    }
}

impl Drop for Lock {
    fn drop(&mut self) {
        if let Some(release) = self.0.take() {
            release();
        }
    }
}

impl std::fmt::Debug for Lock {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("Lock")
    }
}

/// Exit codes: 0 completed or terminal, 1 recovery required, 2 cancelled, 3 rolled back.
pub fn run(transaction_path: &Path, platform: &dyn Platform, trust: Option<&Trust>, root_override: Option<&Path>) -> i32 {
    let mut context: Option<Verified> = None;
    let mut journal: Option<Journal> = None;
    let mut reopen_previous = false;
    let mut ownership: Option<Lock> = None;
    let outcome = run_inner(transaction_path, platform, trust, root_override, &mut context, &mut journal, &mut ownership);
    let code = match outcome {
        Ok(code) => code,
        Err(error) => match (&context, &mut journal) {
            (Some(context), Some(journal)) => recover(context, journal, platform, &error, &mut reopen_previous),
            _ => 1, // Untrusted handoff: no writes.
        },
    };
    drop(ownership);
    // Ordinary startup must see the released update mutex and the durable
    // terminal rollback checkpoint before reopening audio.
    if reopen_previous {
        if let (Some(context), Some(journal)) = (&context, &journal) {
            if let Err(reopen) = platform.reopen_previous(context) {
                let mut retained = journal.clone();
                retained.phase = Phase::RolledBack;
                retained.message = format!("{} The previous application could not reopen automatically. Open the original launcher manually. {reopen}", journal.message);
                let _ = transaction::save_journal(context, &retained);
            }
        }
    }
    code
}

fn save(context: &Verified, journal: &mut Journal) -> Result<()> {
    journal.updated_utc = UtcTime::now();
    transaction::save_journal(context, journal)
}

#[allow(clippy::too_many_lines)]
fn run_inner(
    transaction_path: &Path,
    platform: &dyn Platform,
    trust: Option<&Trust>,
    root_override: Option<&Path>,
    context_slot: &mut Option<Verified>,
    journal_slot: &mut Option<Journal>,
    ownership: &mut Option<Lock>,
) -> Result<i32> {
    // Signature, asset bytes, private ACLs, path bounds and the staged
    // worker digest are checked before any lock, registry or file change.
    let context = transaction::verify(transaction_path, trust, root_override)?;
    platform.verify_worker(&context)?;
    platform.check_file_version(&context.asset_path(), &context.offer.manifest.numeric_version)?;
    *context_slot = Some(context.clone());
    *ownership = Some(platform.acquire_lock()?);
    let mut journal = transaction::read_journal(&context)?;
    *journal_slot = Some(journal.clone());
    macro_rules! commit_journal {
        () => {{
            save(&context, &mut journal)?;
            *journal_slot = Some(journal.clone());
        }};
    }
    if journal.phase.terminal() {
        clear_terminal_recovery(&context, &journal, platform);
        return Ok(0);
    }
    if journal.attempts >= 3 {
        return Err("Automatic recovery reached its three-attempt limit. Retain the update backup and use manual repair.".into());
    }
    if journal.phase == Phase::RecoveryRequired {
        let Some(resume) = journal.resume_phase else {
            return Err("This interrupted installation requires manual repair. Its backup is retained.".into());
        };
        journal.phase = resume;
        journal.resume_phase = None;
        commit_journal!();
    }
    let mut committed = transaction::try_signal(&context, "commit.json")?.is_some();
    if matches!(journal.phase, Phase::Prepared | Phase::Ready) && !committed {
        let parent_live = platform.watch_live_process(&context.data.parent);
        if UtcTime::now() > context.data.created_utc.plus_seconds(30 * 60) || !parent_live() {
            return Err("The original application is no longer available to approve this update.".into());
        }
        guard_original_target(&context)?;
        platform.read_registration(&context)?;
        journal.phase = Phase::Ready;
        journal.attempts += 1;
        commit_journal!();
        platform.register_recovery(&context, journal.attempts)?;
        files::write_json(&context.directory.join("ready.json"), &context.signal(false, ""))?;
        let started = Instant::now();
        while transaction::try_signal(&context, "commit.json")?.is_none() {
            if transaction::try_signal(&context, "abort.json")?.is_some() || started.elapsed() > Duration::from_secs(5 * 60) || !parent_live() {
                journal.phase = Phase::Cancelled;
                journal.message = "The update was cancelled before the application closed.".into();
                commit_journal!();
                clear_terminal_recovery(&context, &journal, platform);
                return Ok(2);
            }
            platform.delay(100);
        }
        committed = true;
    } else {
        journal.attempts += 1;
        commit_journal!();
        platform.register_recovery(&context, journal.attempts)?;
    }
    if !committed {
        return Err("This update never received its durable commit marker.".into());
    }
    if journal.phase == Phase::RollingBack {
        return Err("Resuming the interrupted application rollback.".into());
    }
    if matches!(journal.phase, Phase::Prepared | Phase::Ready) {
        journal.phase = Phase::Committed;
        commit_journal!();
    }
    if matches!(journal.phase, Phase::Committed | Phase::WaitingForExit) {
        journal.phase = Phase::WaitingForExit;
        commit_journal!();
        let parent_exited = platform.wait_for_exit(&context.data.parent, Duration::from_secs(45))?;
        let engine_exited = match &context.data.engine {
            Some(engine) => platform.wait_for_exit(engine, Duration::from_secs(45))?,
            None => true,
        };
        if !parent_exited || !engine_exited {
            return Err("The application or audio engine has not exited. No process was stopped; retry after quitting it normally.".into());
        }
        guard_original_target(&context)?;
        transaction::verify_workspace(&context)?;
        let backup = backup(&context, platform)?;
        journal.phase = Phase::BackedUp;
        journal.backup = Some(backup);
        commit_journal!();
    }
    if journal.phase == Phase::BackedUp {
        let original = require_backup(&journal)?.clone();
        guard_original_target(&context)?;
        guard_snapshot(&context.target_directory(), &original.files)?;
        // Recheck the signed bytes immediately before launching Setup or
        // replacing the launcher, even after a recovery restart.
        files::verify_asset(&context.asset_path(), context.asset())?;
        journal.phase = Phase::Applying;
        commit_journal!();
        let mut code = 0;
        if context.data.standalone {
            replace_standalone(&context)?;
        } else {
            let mut record = |identity: ProcessIdentity| -> Result<()> {
                journal.setup_process = Some(identity);
                *journal_slot = Some(journal.clone());
                save(&context, &mut journal)
            };
            code = platform.run_setup(&context, &mut record)?;
            *journal_slot = Some(journal.clone());
        }
        // Observe only explicit old/new payload inventories after this
        // bounded attempt. Unlisted user files never enter rollback.
        let observed = observe_candidate(&context, &original, platform, false)?;
        journal.phase = Phase::CandidateInstalled;
        journal.candidate = Some(observed);
        commit_journal!();
        if code != 0 {
            return Err(format!(
                "Setup did not complete successfully (exit code {code}). See setup.log in the update recovery folder. Restoring the prior owned application payload."
            ));
        }
    } else if journal.phase == Phase::Applying {
        // Power can fail after Setup changes one file and before the
        // postimage is journaled; there is no cross-file commit. Never guess.
        if let Some(setup) = &journal.setup_process {
            if !platform.wait_for_exit(setup, Duration::from_secs(60))? {
                return Err("Setup is still running. Keep the recovery folder and wait for Setup to finish.".into());
            }
        }
        let original = require_backup(&journal)?.clone();
        let target_hash = files::hash(&context.target_path())?;
        if context.data.standalone && files::equal_hash(&target_hash, &context.offer.manifest.standalone.sha256) {
            journal.phase = Phase::CandidateInstalled;
            journal.candidate = Some(observe_candidate(&context, &original, platform, true)?);
            commit_journal!();
        } else if context.data.standalone && files::equal_hash(&target_hash, &context.data.parent.sha256) {
            replace_standalone(&context)?;
            journal.phase = Phase::CandidateInstalled;
            journal.candidate = Some(observe_candidate(&context, &original, platform, true)?);
            commit_journal!();
        } else {
            return Err("Installation was interrupted before its postimage was recorded. The prior payload and uninstall registration are retained for manual repair; automatic rollback will not overwrite unknown changes.".into());
        }
    }
    if journal.phase == Phase::CandidateInstalled {
        require_backup(&journal)?;
        let candidate = journal.candidate.clone().ok_or("The update phase has no recorded candidate payload.")?;
        guard_snapshot(&context.target_directory(), &candidate.files)?;
        validate_candidate(&context, platform)?;
        transaction::verify_workspace(&context)?;
        let evidence = context.directory.join(format!("smoke-{}", crate::platform::random_hex_id()));
        let mut record = |identity: ProcessIdentity| -> Result<()> {
            journal.smoke_process = Some(identity);
            *journal_slot = Some(journal.clone());
            save(&context, &mut journal)
        };
        let passed = platform.run_smoke(&context, &evidence, &mut record)?;
        *journal_slot = Some(journal.clone());
        if !passed {
            return Err("The updated application failed its inert package check.".into());
        }
        let engine_hash = platform.candidate_engine_hash(&context, &evidence)?;
        journal.phase = Phase::AwaitingHealth;
        journal.candidate_engine_sha256 = Some(engine_hash);
        commit_journal!();
        let candidate = platform.start_candidate(&context)?;
        journal.candidate_process = Some(candidate);
        commit_journal!();
    }
    if journal.phase == Phase::AwaitingHealth {
        let Some(candidate) = journal.candidate_process.clone() else {
            return Err("The candidate launch was interrupted. Manual recovery must confirm no candidate is running before restoring the backup.".into());
        };
        let started = Instant::now();
        let candidate_live = platform.watch_live_process(&candidate);
        let mut health = None;
        while started.elapsed() < Duration::from_secs(120) {
            if let Some(signal) = transaction::try_signal(&context, "health.json")? {
                health = Some(signal);
                break;
            }
            if !candidate_live() {
                break;
            }
            platform.delay(100);
        }
        let healthy = health.as_ref().is_some_and(|h| h.success && h.process_id == candidate.process_id && h.start_time_utc_ticks == candidate.start_time_utc_ticks)
            && candidate_live();
        if !healthy {
            return Err("The updated application did not confirm saved-workspace, engine-protocol and advancing-audio health. The retained backup will be restored after the candidate exits normally.".into());
        }
        let installed = journal.candidate.clone().ok_or("The update phase has no recorded candidate payload.")?;
        guard_snapshot(&context.target_directory(), &installed.files)?;
        transaction::verify_workspace(&context)?;
        journal.phase = Phase::HealthCommitted;
        journal.message = "The new application confirmed workspace and audio-engine health.".into();
        commit_journal!();
    }
    if journal.phase == Phase::HealthCommitted {
        journal.phase = Phase::Completed;
        commit_journal!();
        clear_terminal_recovery(&context, &journal, platform);
        return Ok(0);
    }
    Err("This update requires manual recovery. Its previous application payload is retained.".into())
}

fn recover(context: &Verified, journal: &mut Journal, platform: &dyn Platform, error: &str, reopen_previous: &mut bool) -> i32 {
    let message = error.to_owned();
    let attempt = (|| -> Result<i32> {
        let can_restore = journal.backup.is_some() && journal.candidate.is_some() && matches!(journal.phase, Phase::CandidateInstalled | Phase::AwaitingHealth | Phase::RollingBack);
        if can_restore {
            // A retry must continue waiting on the already launched process.
            journal.phase = Phase::RollingBack;
            journal.message = message.clone();
            save(context, journal)?;
            if let Some(candidate) = &journal.candidate_process {
                if !platform.wait_for_exit(candidate, Duration::from_secs(30))? {
                    return Err("The candidate is still running. No process was killed and no application files were restored. Quit it normally before recovery.".into());
                }
            }
            if let Some(smoke) = &journal.smoke_process {
                if !platform.wait_for_exit(smoke, Duration::from_secs(30))? {
                    return Err("The inert candidate check is still running. It was not killed and application files were not restored.".into());
                }
            }
            if !platform.wait_for_candidate_audio_exit(context, journal.candidate_engine_sha256.as_deref(), Duration::from_secs(45))? {
                return Err("A candidate audio engine is still running. No process was killed and the prior application files were not restored.".into());
            }
            let original = journal.backup.clone().ok_or("The update recovery inventory is missing.")?;
            let candidate = journal.candidate.clone().ok_or("The update phase has no recorded candidate payload.")?;
            restore(context, &original, &candidate, platform)?;
            journal.phase = Phase::RolledBack;
            journal.message = format!("{message} The previous owned application payload was restored; personal settings were not replaced.");
            save(context, journal)?;
            clear_terminal_recovery(context, journal, platform);
            *reopen_previous = true;
            return Ok(3);
        }
        journal.resume_phase = safe_resume(context, journal);
        journal.phase = Phase::RecoveryRequired;
        journal.message = message.clone();
        save(context, journal)?;
        Ok(1)
    })();
    match attempt {
        Ok(code) => code,
        Err(recovery) => {
            let mut retained = journal.clone();
            retained.resume_phase = safe_resume(context, journal);
            retained.phase = Phase::RecoveryRequired;
            retained.message = format!("{message} Recovery stopped: {recovery}");
            let _ = save(context, &mut retained);
            1
        }
    }
}

fn clear_terminal_recovery(context: &Verified, journal: &Journal, platform: &dyn Platform) {
    let failure = platform.clear_recovery(context).err().map(|e| e.chars().take(512).collect::<String>());
    // Registration housekeeping cannot undo a durably completed operation;
    // a retained RunOnce command re-enters only this terminal cleanup.
    let receipt = context.directory.join("recovery-cleanup.json");
    if failure.is_none() && !receipt.exists() {
        return;
    }
    let value = serde_json::json!({
        "pending": failure.is_some(),
        "phase": format!("{:?}", journal.phase),
        "updatedUtc": UtcTime::now(),
        "message": failure.unwrap_or_else(|| "Recovery registration cleanup completed.".into()),
    });
    let _ = files::write_json(&receipt, &value);
}

fn safe_resume(context: &Verified, journal: &Journal) -> Option<Phase> {
    use Phase as P;
    match journal.phase {
        P::Committed | P::WaitingForExit | P::BackedUp | P::CandidateInstalled | P::AwaitingHealth | P::HealthCommitted | P::RollingBack => Some(journal.phase),
        P::Applying if journal.backup.is_some() && journal.candidate.is_some() => Some(P::RollingBack),
        P::Applying if context.data.standalone && journal.backup.is_some() && context.target_path().is_file() => {
            let digest = files::hash(&context.target_path()).ok()?;
            (files::equal_hash(&digest, &context.offer.manifest.standalone.sha256) || files::equal_hash(&digest, &context.data.parent.sha256)).then_some(P::Applying)
        }
        _ => None,
    }
}

fn guard_original_target(context: &Verified) -> Result<()> {
    if !files::equal_hash(&files::hash(&context.target_path())?, &context.data.parent.sha256) {
        return Err("The original application has changed since this update was prepared. Its files were not replaced.".into());
    }
    Ok(())
}

fn require_backup(journal: &Journal) -> Result<&Backup> {
    journal.backup.as_ref().filter(|b| !b.files.is_empty()).ok_or_else(|| "The update recovery inventory is missing.".into())
}

fn entry(root: &Path, relative: &str) -> Result<FileEntry> {
    let path = files::relative_file(root, relative)?;
    Ok(FileEntry { path: relative.into(), size: files::file_size(&path)? as i64, sha256: files::hash(&path)? })
}

/// Copies the owned payload into the private backup (`Backup`).
pub fn backup(context: &Verified, platform: &dyn Platform) -> Result<Backup> {
    let registry = platform.read_registration(context)?;
    let entries = if context.data.standalone {
        let name = context.target_path().file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
        vec![entry(&context.target_directory(), &name)?]
    } else {
        installed_inventory(context, &context.data.current_version, None)?
    };
    let root = context.directory.join("backup");
    files::create_private_directory(&root)?;
    for file in &entries {
        let source = files::relative_file(&context.target_directory(), &file.path)?;
        let destination = files::relative_file(&root, &file.path)?;
        if let Some(parent) = destination.parent() {
            std::fs::create_dir_all(parent).map_err(|e| format!("The update backup could not be created. {e}"))?;
        }
        if destination.exists() {
            guard_snapshot(&root, std::slice::from_ref(file))?;
        } else {
            files::copy_private_checked(&source, &destination, file.size as u64, &file.sha256)?;
        }
    }
    Ok(Backup { files: entries, registry })
}

/// Replaces the single standalone launcher beside itself (`ReplaceStandalone`).
pub fn replace_standalone(context: &Verified) -> Result<()> {
    guard_original_target(context)?;
    let target = context.target_path();
    let staged = PathBuf::from(format!("{}.vinemix-{}.new", target.display(), context.data.id));
    let retained = PathBuf::from(format!("{}.vinemix-{}.old", target.display(), context.data.id));
    let asset = &context.offer.manifest.standalone;
    if staged.exists() {
        files::verify_asset(&staged, asset)?;
    } else {
        files::copy_checked(&context.asset_path(), &staged, asset.size, &asset.sha256)?;
    }
    guard_original_target(context)?;
    files::verify_asset(&staged, asset)?;
    // The target can inherit Read-only when copied from removable media.
    let replace = || vinemix_platform::replace_file(&target, &staged, Some(&retained));
    let error = match replace() {
        Ok(()) => return Ok(()),
        Err(error) => error,
    };
    let failure = |error| format!("The standalone application could not be replaced. {error}");
    if error.code() != windows::Win32::Foundation::E_ACCESSDENIED || !target.metadata().is_ok_and(|m| m.permissions().readonly()) {
        return Err(failure(error));
    }

    guard_original_target(context)?;
    let original = std::fs::OpenOptions::new()
        .access_mode(FILE_READ_ATTRIBUTES.0 | FILE_WRITE_ATTRIBUTES.0)
        .custom_flags(FILE_FLAG_OPEN_REPARSE_POINT.0)
        .open(&target)
        .map_err(|e| format!("The standalone launcher's Read-only attribute could not be opened for updating. {e}"))?;
    let permissions = original.metadata().map_err(|e| format!("The standalone launcher's attributes could not be read. {e}"))?.permissions();
    let mut writable = permissions.clone();
    writable.set_readonly(false);
    original.set_permissions(writable).map_err(|e| format!("The standalone launcher's Read-only attribute could not be cleared. {e}"))?;
    let result = replace().map_err(failure);
    // The handle follows the old file if it becomes the retained backup.
    let restore = original.set_permissions(permissions).map_err(|e| format!("The original launcher's attributes could not be restored. {e}"));
    result.and(restore)
}

/// The payload present after Setup, limited to the old and new inventories.
pub fn observe_candidate(context: &Verified, original: &Backup, platform: &dyn Platform, require_valid_package: bool) -> Result<Backup> {
    let mut entries: BTreeMap<String, FileEntry> = BTreeMap::new();
    let key = |p: &str| p.to_lowercase();
    for previous in &original.files {
        let path = files::relative_file(&context.target_directory(), &previous.path)?;
        if path.is_file() {
            entries.insert(key(&previous.path), entry(&context.target_directory(), &previous.path)?);
        }
    }
    if !context.data.standalone {
        match installed_inventory(context, &context.data.version, Some(&context.offer.manifest.numeric_version)) {
            Ok(installed) => {
                for item in installed {
                    entries.insert(key(&item.path), item);
                }
            }
            Err(error) if require_valid_package => return Err(error),
            Err(_) => {}
        }
    }
    let mut files: Vec<FileEntry> = entries.into_values().collect();
    files.sort_by(|a, b| a.path.cmp(&b.path));
    Ok(Backup { files, registry: platform.read_registration(context)? })
}

fn validate_candidate(context: &Verified, platform: &dyn Platform) -> Result<()> {
    platform.check_file_version(&context.target_path(), &context.offer.manifest.numeric_version)?;
    if context.data.standalone {
        files::verify_asset(&context.target_path(), &context.offer.manifest.standalone)
    } else {
        installed_inventory(context, &context.data.version, Some(&context.offer.manifest.numeric_version))?;
        platform.check_file_version(&context.target_directory().join("VineMix.Engine.exe"), &context.offer.manifest.numeric_version)
    }
}

/// The installed package inventory (`SetupSupport/package-manifest.json`).
pub fn installed_inventory(context: &Verified, version: &str, numeric_version: Option<&str>) -> Result<Vec<FileEntry>> {
    let malformed = || "The installed package inventory is malformed. Retain the update backup and use Setup for manual repair.".to_owned();
    let directory = context.target_directory();
    let path = directory.join("SetupSupport").join("package-manifest.json");
    let bytes = files::read_bytes(&path, 2 * 1024 * 1024)?;
    let root: serde_json::Value = serde_json::from_slice(&bytes).map_err(|_| malformed())?;
    let text = |value: &serde_json::Value, key: &str| value.get(key).and_then(serde_json::Value::as_str).map(str::to_owned);
    if text(&root, "version").ok_or_else(malformed)? != version || numeric_version.is_some_and(|n| text(&root, "numericVersion").as_deref() != Some(n)) {
        return Err("The installed package inventory does not match the expected release. Use Setup for manual repair.".into());
    }
    let mut entries: BTreeMap<String, FileEntry> = BTreeMap::new();
    let mut total: i64 = 0;
    for item in root.get("files").and_then(serde_json::Value::as_array).ok_or_else(malformed)? {
        if entries.len() >= 2048 {
            return Err("The package inventory contains too many files.".into());
        }
        let relative = text(item, "path").ok_or_else(malformed)?;
        let size = item.get("size").and_then(serde_json::Value::as_i64).ok_or_else(malformed)?;
        let digest = text(item, "sha256").ok_or_else(malformed)?;
        let file = files::relative_file(&directory, &relative)?;
        protect_settings(context, &file)?;
        total = total.saturating_add(size);
        if size <= 0 || size > transaction::MAXIMUM_ASSET_BYTES || total > 2 * transaction::MAXIMUM_ASSET_BYTES || !files::equal_hash(&digest, &digest) || entries.contains_key(&relative.to_lowercase()) {
            return Err("The installed package inventory is invalid or ambiguous.".into());
        }
        if files::file_size(&file)? != size as u64 || !files::equal_hash(&files::hash(&file)?, &digest) {
            return Err("An installed application payload file differs from its recorded inventory. Use manual repair.".into());
        }
        entries.insert(relative.to_lowercase(), FileEntry { path: relative, size, sha256: digest });
    }
    if !entries.contains_key("vinemix.exe") || !entries.contains_key("vinemix.engine.exe") || !files::same_path(&context.target_path(), &directory.join("VineMix.exe")) {
        return Err("The inventory does not own this application and engine pair.".into());
    }
    for relative in ["SetupSupport/package-manifest.json", "installation.ini", "unins000.exe", "unins000.dat", "unins000.msg"] {
        let file = files::relative_file(&directory, relative)?;
        if file.is_file() {
            entries.insert(relative.to_lowercase(), entry(&directory, relative)?);
        } else if matches!(relative, "installation.ini" | "unins000.exe" | "unins000.dat") {
            return Err("The owned installation bookkeeping is incomplete. Use Setup for manual repair.".into());
        }
    }
    let mut list: Vec<FileEntry> = entries.into_values().collect();
    list.sort_by(|a, b| a.path.cmp(&b.path));
    Ok(list)
}

/// Every listed file still matches its recorded size and digest.
pub fn guard_snapshot(root: &Path, entries: &[FileEntry]) -> Result<()> {
    for item in entries {
        let path = files::relative_file(root, &item.path)?;
        if !path.is_file() || files::file_size(&path)? != item.size as u64 || !files::equal_hash(&files::hash(&path)?, &item.sha256) {
            return Err("An application payload file changed after its update checkpoint. Recovery preserved that newer file.".into());
        }
    }
    Ok(())
}

/// Restores the prior owned payload and registration (`Restore`).
pub fn restore(context: &Verified, original: &Backup, candidate: &Backup, platform: &dyn Platform) -> Result<()> {
    let target_directory = context.target_directory();
    let backup_root = context.directory.join("backup");
    for item in original.files.iter().chain(&candidate.files) {
        protect_settings(context, &files::relative_file(&target_directory, &item.path)?)?;
    }
    guard_snapshot(&backup_root, &original.files)?;
    let after: BTreeMap<String, &FileEntry> = candidate.files.iter().map(|e| (e.path.to_lowercase(), e)).collect();
    let before: BTreeMap<String, &FileEntry> = original.files.iter().map(|e| (e.path.to_lowercase(), e)).collect();
    let mut all: HashSet<String> = before.keys().cloned().collect();
    all.extend(after.keys().cloned());
    // Resuming accepts only the checkpoint's postimage or original bytes
    // already restored by an interrupted pass.
    for key in &all {
        let relative = after.get(key).or_else(|| before.get(key)).map(|e| e.path.clone()).unwrap_or_default();
        let path = files::relative_file(&target_directory, &relative)?;
        if !path.is_file() {
            if after.contains_key(key) && before.contains_key(key) {
                return Err("An expected payload file disappeared after the recovery checkpoint.".into());
            }
            continue;
        }
        let digest = files::hash(&path)?;
        let matches_after = after.get(key).is_some_and(|e| files::equal_hash(&digest, &e.sha256));
        let matches_before = before.get(key).is_some_and(|e| files::equal_hash(&digest, &e.sha256));
        if !matches_after && !matches_before {
            return Err("An application payload file changed after its update checkpoint. Recovery preserved that newer file.".into());
        }
    }
    for old in &original.files {
        let target = files::relative_file(&target_directory, &old.path)?;
        if !after.contains_key(&old.path.to_lowercase()) && target.is_file() && !files::equal_hash(&files::hash(&target)?, &old.sha256) {
            return Err("A formerly missing payload file was recreated after the checkpoint; recovery retained it.".into());
        }
    }
    // Check the uninstall postimage before any file mutation too.
    let current_registry = if original.registry.is_some() { platform.read_registration(context)? } else { None };
    if let Some(saved) = &original.registry {
        if current_registry.as_ref() != candidate.registry.as_ref() && current_registry.as_ref() != Some(saved) {
            return Err("Uninstall registration changed after the update checkpoint; recovery retained it.".into());
        }
    }
    for old in &original.files {
        let target = files::relative_file(&target_directory, &old.path)?;
        if target.is_file() && files::equal_hash(&files::hash(&target)?, &old.sha256) {
            continue;
        }
        if let Some(parent) = target.parent() {
            std::fs::create_dir_all(parent).map_err(|e| format!("A payload folder could not be restored. {e}"))?;
        }
        let temporary = PathBuf::from(format!("{}.vinemix-restore-{}", target.display(), context.data.id));
        if temporary.exists() {
            if files::file_size(&temporary)? != old.size as u64 || !files::equal_hash(&files::hash(&temporary)?, &old.sha256) {
                return Err("An application payload file changed after its update checkpoint. Recovery preserved that newer file.".into());
            }
        } else {
            files::copy_checked(&files::relative_file(&backup_root, &old.path)?, &temporary, old.size as u64, &old.sha256)?;
        }
        let replaced = if target.exists() { vinemix_platform::replace_file(&target, &temporary, None).is_ok() } else { std::fs::rename(&temporary, &target).is_ok() };
        if !replaced {
            return Err("A previous application file could not be restored.".into());
        }
    }
    let old_paths: HashSet<String> = original.files.iter().map(|e| e.path.to_lowercase()).collect();
    for added in candidate.files.iter().filter(|e| !old_paths.contains(&e.path.to_lowercase())) {
        let path = files::relative_file(&target_directory, &added.path)?;
        if !path.is_file() {
            continue;
        }
        if !files::equal_hash(&files::hash(&path)?, &added.sha256) {
            return Err("A new application file changed during recovery; it was retained.".into());
        }
        std::fs::remove_file(&path).map_err(|e| format!("A new application file could not be removed. {e}"))?;
    }
    if let Some(saved) = &original.registry {
        if current_registry.as_ref() != Some(saved) {
            platform.restore_registration(saved, candidate.registry.as_ref())?;
        }
    }
    Ok(())
}

fn protect_settings(context: &Verified, path: &Path) -> Result<()> {
    if let Some(settings) = &context.data.settings_path {
        let settings = Path::new(settings);
        let prefix = format!("{}.", files::full(settings).to_string_lossy().to_lowercase());
        if files::same_path(settings, path) || files::full(path).to_string_lossy().to_lowercase().starts_with(&prefix) {
            return Err("The application inventory includes the active custom workspace or its recovery files. Those settings will not be replaced.".into());
        }
    }
    Ok(())
}
