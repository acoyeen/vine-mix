//! The private, durable update handoff (`UpdateTransaction`,
//! `PreparedUpdate`). It never stops an engine or changes a workspace.

use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use vinemix_update::{Compatibility, Offer, SemanticVersion, Trust};

use super::files::{self, Result};
use super::model::{EngineSignal, Journal, Phase, ProcessIdentity, Signal, TransactionData, UtcTime, WorkspaceCheckpoint};

pub const MAXIMUM_ENVELOPE_BYTES: u64 = 256 * 1024;
pub const MAXIMUM_ASSET_BYTES: i64 = 1024 * 1024 * 1024;

/// `%LOCALAPPDATA%\VineMix\updates` (a development instance uses its own folder).
pub fn root() -> PathBuf {
    crate::paths::data_directory().join("updates")
}

fn is_guid_n(name: &str) -> bool {
    name.len() == 32 && name.bytes().all(|b| b.is_ascii_hexdigit())
}

/// A verified transaction (`VerifiedUpdate`).
#[derive(Clone, Debug)]
pub struct Verified {
    pub directory: PathBuf,
    pub data: TransactionData,
    pub offer: Offer,
}

impl Verified {
    pub fn transaction_path(&self) -> PathBuf {
        self.directory.join("transaction.json")
    }
    pub fn worker_path(&self) -> PathBuf {
        self.directory.join("worker.exe")
    }
    pub fn asset_path(&self) -> PathBuf {
        self.directory.join(&self.data.asset_file_name)
    }
    pub fn target_path(&self) -> PathBuf {
        PathBuf::from(&self.data.parent.image_path)
    }
    pub fn target_directory(&self) -> PathBuf {
        self.target_path().parent().map(Path::to_path_buf).unwrap_or_default()
    }
    pub fn journal_path(&self) -> PathBuf {
        self.directory.join("journal.json")
    }
    pub fn asset(&self) -> &vinemix_update::Asset {
        if self.data.standalone { &self.offer.manifest.standalone } else { &self.offer.manifest.installer }
    }
    pub fn signal(&self, success: bool, message: &str) -> Signal {
        Signal {
            token: self.data.token.clone(),
            envelope_sha256: self.data.envelope_sha256.clone(),
            process_id: 0,
            start_time_utc_ticks: 0,
            success,
            message: message.into(),
        }
    }
}

/// An unfinished transaction found at startup (`PendingUpdate`).
#[derive(Clone, Debug)]
pub struct Pending {
    pub transaction_path: PathBuf,
    pub version: String,
    pub phase: Phase,
    pub message: String,
    pub can_resume: bool,
}

/// Values the candidate checks during its health window (`UpdateHealthContext`).
#[derive(Clone, Debug)]
pub struct HealthContext {
    pub settings_path: Option<String>,
    pub settings_sha256: Option<String>,
    pub version: String,
    pub protocol_version: i64,
    pub settings_schema: i64,
    pub phase: Phase,
}

fn production_trust() -> Result<Trust> {
    Trust::production().map_err(|e| format!("The update signing key is unavailable. {e}"))
}

/// Verifies a staged transaction before anything trusts it (`Verify`).
pub fn verify(transaction_path: &Path, trust: Option<&Trust>, root_override: Option<&Path>) -> Result<Verified> {
    let root = files::full(&root_override.map_or_else(root, Path::to_path_buf));
    let path = files::full(transaction_path);
    let directory = path.parent().map(Path::to_path_buf).ok_or("The update transaction path is invalid.")?;
    let directory_name = directory.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    if path.file_name().is_none_or(|n| n != "transaction.json")
        || !is_guid_n(&directory_name)
        || !directory.parent().is_some_and(|parent| files::same_path(parent, &root))
    {
        return Err("The update transaction is outside its private staging directory.".into());
    }
    files::require_private(&root)?;
    files::require_private(&directory)?;
    files::require_private(&path)?;
    let data: TransactionData = files::read_json(&path, 32 * 1024)?;
    let identity_ok = |identity: &ProcessIdentity| identity.valid();
    if data.format_version != 1
        || data.id != directory_name
        || data.token.len() != 64
        || !data.token.bytes().all(|b| b.is_ascii_hexdigit())
        || !identity_ok(&data.parent)
        || data.engine.as_ref().is_some_and(|e| !identity_ok(e))
        || SemanticVersion::parse(&data.current_version).is_none()
        || SemanticVersion::parse(&data.version).is_none()
        || !files::equal_hash(&data.envelope_sha256, &data.envelope_sha256)
        || data.created_utc > UtcTime::now().plus_seconds(300)
    {
        return Err("The update handoff is malformed.".into());
    }
    if let Some(settings) = &data.settings_path {
        let candidate = Path::new(settings);
        if settings.is_empty() || settings.chars().count() > 1024 || !candidate.is_absolute() || !files::same_path(candidate, &files::full(candidate)) {
            return Err("The prepared workspace path is invalid.".into());
        }
    }
    let envelope = directory.join("envelope.json");
    files::require_private(&envelope)?;
    let bytes = files::read_bytes(&envelope, MAXIMUM_ENVELOPE_BYTES)?;
    let production;
    let trust = match trust {
        Some(trust) => trust,
        None => {
            production = production_trust()?;
            &production
        }
    };
    let offer = trust.verify(&bytes, &data.version, Compatibility::current()).map_err(|e| e.message())?;
    let newer = match (SemanticVersion::parse(&offer.manifest.version), SemanticVersion::parse(&data.current_version)) {
        (Some(offered), Some(current)) => offered > current,
        _ => false,
    };
    if !files::equal_hash(&data.envelope_sha256, &offer.envelope_sha256) || !newer {
        return Err("The update handoff does not match its signed release.".into());
    }
    let asset_name = if data.standalone { &offer.manifest.standalone.file_name } else { &offer.manifest.installer.file_name };
    if &data.asset_file_name != asset_name {
        return Err("The update distribution does not match its signed asset.".into());
    }
    let target = Path::new(&data.parent.image_path);
    let full_target = files::full(target);
    if full_target.to_string_lossy() != data.parent.image_path
        || !target.is_absolute()
        || data.parent.image_path.starts_with("\\\\")
        || !target.extension().is_some_and(|e| e.eq_ignore_ascii_case("exe"))
        || files::is_inside(target, &root)
    {
        return Err("The original application path is invalid.".into());
    }
    files::no_reparse(target)?;
    let context = Verified { directory, data, offer };
    files::require_private(&context.worker_path())?;
    files::require_private(&context.asset_path())?;
    if !files::equal_hash(&files::hash(&context.worker_path())?, &context.data.parent.sha256) {
        return Err("The staged update worker no longer matches the original application.".into());
    }
    files::verify_asset(&context.asset_path(), context.asset())?;
    Ok(context)
}

fn valid_backup(backup: Option<&super::model::Backup>) -> Result<()> {
    let Some(backup) = backup else { return Ok(()) };
    let mut seen = std::collections::HashSet::new();
    if backup.files.is_empty()
        || backup.files.len() > 4096
        || backup.files.iter().any(|file| {
            file.path.is_empty()
                || file.path.chars().count() > 240
                || file.size < 0
                || file.size > MAXIMUM_ASSET_BYTES
                || !files::equal_hash(&file.sha256, &file.sha256)
                || !seen.insert(file.path.to_lowercase())
        })
    {
        return Err("The update backup inventory is malformed.".into());
    }
    if backup.registry.as_ref().is_some_and(|registry| registry.values.len() > 128) {
        return Err("The saved uninstall registration is malformed.".into());
    }
    Ok(())
}

/// Reads and validates the journal (`ReadJournal`).
pub fn read_journal(context: &Verified) -> Result<Journal> {
    files::require_private(&context.journal_path())?;
    let journal: Journal = files::read_json(&context.journal_path(), 8 * 1024 * 1024)?;
    let identity = |i: &Option<ProcessIdentity>| i.as_ref().is_none_or(ProcessIdentity::valid);
    if !(0..=3).contains(&journal.attempts)
        || journal.message.chars().count() > 32768
        || !identity(&journal.setup_process)
        || !identity(&journal.smoke_process)
        || !identity(&journal.candidate_process)
        || journal.candidate_engine_sha256.as_ref().is_some_and(|h| !files::equal_hash(h, h))
    {
        return Err("The update journal is malformed.".into());
    }
    valid_backup(journal.backup.as_ref())?;
    valid_backup(journal.candidate.as_ref())?;
    use Phase as P;
    if matches!(journal.phase, P::BackedUp | P::Applying | P::CandidateInstalled | P::AwaitingHealth | P::HealthCommitted)
        || journal.phase == P::RollingBack && journal.backup.is_none()
    {
        if journal.backup.is_none() {
            return Err("The update phase has no retained payload backup.".into());
        }
    }
    if matches!(journal.phase, P::CandidateInstalled | P::AwaitingHealth | P::HealthCommitted) || journal.phase == P::RollingBack && journal.candidate.is_none() {
        if journal.candidate.is_none() {
            return Err("The update phase has no recorded candidate payload.".into());
        }
    }
    if matches!(journal.phase, P::AwaitingHealth | P::HealthCommitted) && journal.candidate_engine_sha256.is_none() {
        return Err("The update health checkpoint has no verified candidate engine digest.".into());
    }
    Ok(journal)
}

pub fn save_journal(context: &Verified, journal: &Journal) -> Result<()> {
    let mut journal = journal.clone();
    journal.updated_utc = UtcTime::now();
    files::write_json(&context.journal_path(), &journal)
}

/// Reads a handoff marker that must carry this transaction's token.
pub fn try_signal(context: &Verified, name: &str) -> Result<Option<Signal>> {
    let path = context.directory.join(name);
    if !path.exists() {
        return Ok(None);
    }
    files::require_private(&path)?;
    let signal: Signal = files::read_json(&path, 8192)?;
    if signal.token != context.data.token || !files::equal_hash(&signal.envelope_sha256, &context.data.envelope_sha256) {
        return Err("An update handoff marker does not match its transaction.".into());
    }
    Ok(Some(signal))
}

/// Starts `worker.exe --apply-update <transaction>` from the staging folder.
pub fn start_worker(context: &Verified) -> Result<()> {
    let arguments = vec!["--apply-update".to_owned(), context.transaction_path().to_string_lossy().into_owned()];
    crate::platform::launch(&context.worker_path(), &arguments, &context.directory).map(|_| ()).map_err(|e| format!("The private update worker could not start. {e}"))
}

/// Unfinished, non-terminal transactions (`FindPending`).
pub fn find_pending(trust: Option<&Trust>, root_override: Option<&Path>, is_live: &dyn Fn(&ProcessIdentity) -> bool) -> Result<Vec<Pending>> {
    let root = root_override.map_or_else(root, Path::to_path_buf);
    let mut pending = Vec::new();
    if !root.is_dir() {
        return Ok(pending);
    }
    files::require_private(&root)?;
    let mut directories = Vec::new();
    for entry in std::fs::read_dir(&root).map_err(|e| format!("The update history could not be read. {e}"))? {
        let Ok(entry) = entry else { continue };
        let path = entry.path();
        if !path.is_dir() || !is_guid_n(&entry.file_name().to_string_lossy()) {
            continue;
        }
        if directories.len() >= 4096 {
            return Err("The update history contains more than 4096 transactions. Retain unfinished recovery folders and ask the maintainer to archive completed update history before continuing.".into());
        }
        directories.push(path);
    }
    for directory in directories {
        let result = (|| -> Result<Option<Pending>> {
            files::require_private(&directory)?;
            let history_path = directory.join("journal.json");
            files::require_private(&history_path)?;
            let history: Journal = files::read_json(&history_path, 8 * 1024 * 1024)?;
            if history.phase.terminal() {
                return Ok(None);
            }
            let context = verify(&directory.join("transaction.json"), trust, Some(&root))?;
            let journal = read_journal(&context)?;
            if journal.phase.terminal() {
                return Ok(None);
            }
            let committed = try_signal(&context, "commit.json")?.is_some();
            let precommit = matches!(journal.phase, Phase::Prepared | Phase::Ready);
            if precommit && !committed && is_live(&context.data.parent) {
                return Ok(None);
            }
            let phase = if precommit && committed { Phase::Committed } else { journal.phase };
            Ok(Some(Pending {
                transaction_path: context.transaction_path(),
                version: context.data.version.clone(),
                phase,
                message: journal.message.clone(),
                can_resume: (committed || !precommit)
                    && journal.attempts < 3
                    && (journal.phase != Phase::RecoveryRequired || journal.resume_phase.is_some()),
            }))
        })();
        match result {
            Ok(Some(item)) => pending.push(item),
            Ok(None) => {}
            Err(_) => pending.push(Pending {
                transaction_path: directory.join("transaction.json"),
                version: "unknown".into(),
                phase: Phase::RecoveryRequired,
                message: "Update recovery information could not be verified. Keep this folder for manual repair.".into(),
                can_resume: false,
            }),
        }
    }
    Ok(pending)
}

/// Restarts the worker for an interrupted, committed transaction (`Resume`).
pub fn resume(pending: &Pending) -> Result<()> {
    let context = verify(&pending.transaction_path, None, None)?;
    let journal = read_journal(&context)?;
    if journal.phase.terminal() || journal.attempts >= 3 || matches!(journal.phase, Phase::Prepared | Phase::Ready) && try_signal(&context, "commit.json")?.is_none() {
        return Err("This transaction needs manual repair or was never committed.".into());
    }
    start_worker(&context)
}

/// Cancels a handoff abandoned before commit (`CancelUncommitted`).
pub fn cancel_uncommitted(pending: &Pending) -> Result<()> {
    let context = verify(&pending.transaction_path, None, None)?;
    let platform = super::windows::WindowsPlatform;
    let _ownership = super::worker::Platform::acquire_lock(&platform)?;
    let mut journal = read_journal(&context)?;
    if !matches!(journal.phase, Phase::Prepared | Phase::Ready) || context.directory.join("commit.json").exists() || context.data.parent.is_live() {
        return Err("The update is active, committed, or still owned by its original application.".into());
    }
    journal.phase = Phase::Cancelled;
    journal.message = "An abandoned update handoff was cancelled before any application or settings changes.".into();
    save_journal(&context, &journal)?;
    super::worker::Platform::clear_recovery(&platform, &context)
}

/// Candidate health report (`ReportHealth`), after strict workspace load,
/// a compatible engine and advancing audio frames.
pub fn report_health(transaction_path: &Path, success: bool, message: &str) -> Result<()> {
    let context = verify(transaction_path, None, None)?;
    let process = ProcessIdentity::current()?;
    let journal = read_journal(&context)?;
    let candidate_matches = journal.candidate_process.as_ref().is_some_and(|c| process.same(c));
    if !files::same_path(Path::new(&process.image_path), &context.target_path())
        || journal.phase != Phase::AwaitingHealth
        || !candidate_matches
        || vinemix_platform::release::VERSION != context.data.version
        || vinemix_platform::release::NUMERIC_VERSION != context.offer.manifest.numeric_version
    {
        return Err("This application is not the update candidate awaiting health confirmation.".into());
    }
    if success {
        verify_workspace(&context)?;
    }
    let message: String = message.chars().take(512).collect();
    let signal = Signal {
        token: context.data.token.clone(),
        envelope_sha256: context.data.envelope_sha256.clone(),
        process_id: process.process_id,
        start_time_utc_ticks: process.start_time_utc_ticks,
        success,
        message,
    };
    files::write_json(&context.directory.join("health.json"), &signal)
}

/// The saved-workspace checkpoint recorded at commit (`ReadWorkspace`).
pub fn read_workspace(context: &Verified) -> Result<Option<WorkspaceCheckpoint>> {
    let Some(settings) = &context.data.settings_path else { return Ok(None) };
    let path = context.directory.join("workspace.json");
    files::require_private(&path)?;
    let checkpoint: WorkspaceCheckpoint = files::read_json(&path, 32768)?;
    if checkpoint.token != context.data.token
        || !files::equal_hash(&checkpoint.envelope_sha256, &context.data.envelope_sha256)
        || !files::same_path(Path::new(&checkpoint.path), Path::new(settings))
        || checkpoint.size <= 0
        || checkpoint.size > 32 * 1024 * 1024
        || !files::equal_hash(&checkpoint.sha256, &files::hash(&context.directory.join("workspace-before.json"))?)
    {
        return Err("The saved-workspace update checkpoint is invalid.".into());
    }
    Ok(Some(checkpoint))
}

/// The workspace still matches its checkpoint (`VerifyWorkspace`).
pub fn verify_workspace(context: &Verified) -> Result<()> {
    if let Some(checkpoint) = read_workspace(context)? {
        let path = Path::new(&checkpoint.path);
        if files::file_size(path)? != checkpoint.size as u64 || !files::equal_hash(&files::hash(path)?, &checkpoint.sha256) {
            return Err("The saved workspace changed after the update checkpoint. The newer settings were retained and will not be restored from a backup.".into());
        }
    }
    Ok(())
}

pub fn health_context(transaction_path: &Path) -> Result<HealthContext> {
    let context = verify(transaction_path, None, None)?;
    let checkpoint = read_workspace(&context)?;
    Ok(HealthContext {
        settings_path: checkpoint.as_ref().map(|c| c.path.clone()),
        settings_sha256: checkpoint.as_ref().map(|c| c.sha256.clone()),
        version: context.data.version.clone(),
        protocol_version: context.offer.manifest.protocol_version,
        settings_schema: context.offer.manifest.settings_schema,
        phase: read_journal(&context)?.phase,
    })
}

/// Waits until the worker has registered this process as the candidate.
pub fn wait_for_candidate_registration(transaction_path: &Path, cancelled: &dyn Fn() -> bool) -> Result<HealthContext> {
    let context = verify(transaction_path, None, None)?;
    let current = ProcessIdentity::current()?;
    let started = Instant::now();
    while started.elapsed() < Duration::from_secs(10) {
        if cancelled() {
            return Err("The update health check was cancelled.".into());
        }
        let journal = read_journal(&context)?;
        if journal.phase == Phase::AwaitingHealth && journal.candidate_process.as_ref().is_some_and(|c| current.same(c)) {
            let checkpoint = read_workspace(&context)?;
            return Ok(HealthContext {
                settings_path: checkpoint.as_ref().map(|c| c.path.clone()),
                settings_sha256: checkpoint.as_ref().map(|c| c.sha256.clone()),
                version: context.data.version.clone(),
                protocol_version: context.offer.manifest.protocol_version,
                settings_schema: context.offer.manifest.settings_schema,
                phase: journal.phase,
            });
        }
        if matches!(journal.phase, Phase::RecoveryRequired | Phase::RollingBack | Phase::RolledBack) {
            break;
        }
        std::thread::sleep(Duration::from_millis(50));
    }
    Err("The update worker did not register this candidate process for health confirmation.".into())
}

/// Records the candidate's engine so recovery can wait for it.
pub fn record_candidate_engine(transaction_path: &Path, identity: &ProcessIdentity) -> Result<()> {
    let context = verify(transaction_path, None, None)?;
    let journal = read_journal(&context)?;
    let process = ProcessIdentity::current()?;
    if !identity.valid()
        || journal.phase != Phase::AwaitingHealth
        || !journal.candidate_process.as_ref().is_some_and(|c| process.same(c))
        || !journal.candidate_engine_sha256.as_ref().is_some_and(|h| files::equal_hash(&identity.sha256, h))
    {
        return Err("The candidate engine does not match this verified update transaction.".into());
    }
    let signal = EngineSignal { token: context.data.token.clone(), envelope_sha256: context.data.envelope_sha256.clone(), identity: identity.clone() };
    files::write_json(&context.directory.join("candidate-engine.json"), &signal)
}

/// Waits up to 130 s for the worker to commit the healthy restart.
pub fn wait_for_health_commit(transaction_path: &Path, cancelled: &dyn Fn() -> bool) -> Result<bool> {
    let context = verify(transaction_path, None, None)?;
    let started = Instant::now();
    while started.elapsed() < Duration::from_secs(130) {
        if cancelled() {
            return Ok(false);
        }
        match read_journal(&context)?.phase {
            Phase::HealthCommitted | Phase::Completed => return Ok(true),
            Phase::RollingBack | Phase::RolledBack | Phase::RecoveryRequired | Phase::Cancelled => return Ok(false),
            _ => {}
        }
        std::thread::sleep(Duration::from_millis(100));
    }
    Ok(false)
}

/// Stages a verified download for the worker (`PrepareAsync`).
pub fn prepare(download: &vinemix_update::Downloaded, offer: &Offer, standalone: bool, engine: Option<ProcessIdentity>, settings_path: Option<&Path>) -> Result<Prepared> {
    let trust = production_trust()?;
    let offer = trust.verify(&offer.envelope, &offer.manifest.version, Compatibility::current()).map_err(|e| e.message())?;
    let newer = match (SemanticVersion::parse(&offer.manifest.version), SemanticVersion::parse(vinemix_platform::release::VERSION)) {
        (Some(offered), Some(current)) => offered > current,
        _ => false,
    };
    if !newer {
        return Err("The selected release is not newer than this application.".into());
    }
    let expected = if standalone { offer.manifest.standalone.clone() } else { offer.manifest.installer.clone() };
    if download.asset != expected {
        return Err("The downloaded asset is not the selected distribution.".into());
    }
    files::verify_asset(&download.file, &expected)?;
    let parent = ProcessIdentity::current()?;
    files::no_reparse(Path::new(&parent.image_path))?;
    if !standalone {
        super::windows::read_owned_registration(&PathBuf::from(&parent.image_path).parent().map(Path::to_path_buf).unwrap_or_default())?;
    }
    let root = root();
    files::create_private_directory(&root)?;
    let id = crate::platform::random_hex_id();
    let directory = root.join(format!(".preparing-{id}"));
    files::create_private_directory(&directory)?;
    let mut token_bytes = [0u8; 32];
    // SAFETY: the system-preferred generator fills the buffer.
    unsafe {
        windows::Win32::Security::Cryptography::BCryptGenRandom(None, &mut token_bytes, windows::Win32::Security::Cryptography::BCRYPT_USE_SYSTEM_PREFERRED_RNG)
    }
    .ok()
    .map_err(|_| "A secure update token could not be generated.".to_owned())?;
    let data = TransactionData {
        format_version: 1,
        id: id.clone(),
        version: offer.manifest.version.clone(),
        current_version: vinemix_platform::release::VERSION.into(),
        token: vinemix_update::crypto::hex(&token_bytes).to_uppercase(),
        envelope_sha256: offer.envelope_sha256.clone(),
        asset_file_name: expected.file_name.clone(),
        standalone,
        parent: parent.clone(),
        engine,
        settings_path: settings_path.map(|p| files::full(p).to_string_lossy().into_owned()),
        created_utc: UtcTime::now(),
    };
    files::copy_private_checked(&download.file, &directory.join(&expected.file_name), expected.size, &expected.sha256)?;
    let worker_size = files::file_size(Path::new(&parent.image_path))?;
    files::copy_private_checked(Path::new(&parent.image_path), &directory.join("worker.exe"), worker_size, &parent.sha256)?;
    files::write_bytes(&directory.join("envelope.json"), &offer.envelope)?;
    files::write_json(&directory.join("transaction.json"), &data)?;
    files::write_json(&directory.join("journal.json"), &Journal::default())?;
    let complete = root.join(&id);
    std::fs::rename(&directory, &complete).map_err(|e| format!("The update could not be staged. {e}"))?;
    Ok(Prepared { context: Verified { directory: complete, data, offer }, committed: false })
}

/// A staged transaction owned by the running application (`PreparedUpdate`);
/// dropping it before commit writes the abort marker.
#[derive(Debug)]
pub struct Prepared {
    pub context: Verified,
    committed: bool,
}

impl Prepared {
    pub fn start_worker_and_wait_ready(&self, cancelled: &dyn Fn() -> bool) -> Result<()> {
        start_worker(&self.context)?;
        let started = Instant::now();
        while started.elapsed() < Duration::from_secs(30) {
            if cancelled() {
                return Err("The update was cancelled.".into());
            }
            if try_signal(&self.context, "ready.json")?.is_some() {
                return Ok(());
            }
            let journal = read_journal(&self.context)?;
            if matches!(journal.phase, Phase::RecoveryRequired | Phase::Cancelled) {
                return Err(journal.message);
            }
            std::thread::sleep(Duration::from_millis(100));
        }
        Err("The update worker did not become ready. The application has not been closed.".into())
    }

    /// Records the workspace checkpoint and the durable commit marker.
    pub fn commit(&mut self) -> Result<()> {
        let context = &self.context;
        if try_signal(context, "ready.json")?.is_none() || read_journal(context)?.phase != Phase::Ready {
            return Err("The update worker is not ready.".into());
        }
        if let Some(settings) = &context.data.settings_path {
            let settings = Path::new(settings);
            files::no_reparse(settings)?;
            let size = files::file_size(settings)?;
            if size == 0 || size > 32 * 1024 * 1024 {
                return Err("The saved workspace exceeds the update checkpoint size limit.".into());
            }
            let digest = files::hash(settings)?;
            files::copy_private_checked(settings, &context.directory.join("workspace-before.json"), size, &digest)?;
            let checkpoint = WorkspaceCheckpoint {
                token: context.data.token.clone(),
                envelope_sha256: context.data.envelope_sha256.clone(),
                path: settings.to_string_lossy().into_owned(),
                size: size as i64,
                sha256: digest,
            };
            files::write_json(&context.directory.join("workspace.json"), &checkpoint)?;
        }
        files::write_json(&context.directory.join("commit.json"), &context.signal(false, ""))?;
        self.committed = true;
        Ok(())
    }
}

impl Drop for Prepared {
    fn drop(&mut self) {
        if !self.committed {
            let _ = files::write_json(&self.context.directory.join("abort.json"), &self.context.signal(false, ""));
        }
    }
}
