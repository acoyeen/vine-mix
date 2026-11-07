//! The application's side of updating (`App.Updates`): installing a verified
//! download through the private worker, recovering interrupted updates at
//! startup, and the updated candidate's health check.

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, Sender};
use std::sync::Arc;
use std::time::{Duration, Instant};

use super::files;
use super::model::{Phase, ProcessIdentity};
use super::transaction;
use crate::link::EngineLink;

/// `--update-reverted <transaction>`: the notice shown after a rollback.
pub fn rollback_notice(transaction_path: &Path) -> String {
    let result = (|| -> Result<String, String> {
        let context = transaction::verify(transaction_path, None, None)?;
        let journal = transaction::read_journal(&context)?;
        let current = ProcessIdentity::current()?;
        if journal.phase != Phase::RolledBack
            || vinemix_platform::release::VERSION != context.data.current_version
            || !files::same_path(Path::new(&current.image_path), &context.target_path())
            || !files::equal_hash(&current.sha256, &context.data.parent.sha256)
        {
            return Err("The restored application identity did not match its recovery record.".into());
        }
        Ok(format!(
            "The update to {} did not finish. VineMix {} was restored and reopened. Your saved workspace was preserved. Keep the update recovery folder if you report this problem.",
            context.data.version,
            vinemix_platform::release::VERSION
        ))
    })();
    result.unwrap_or_else(|_| {
        "A previous update reported a problem. This version has opened; keep its update recovery folder and contact the maintainer before retrying.".into()
    })
}

fn ask_yes_no(title: &str, text: &str) -> bool {
    use windows::core::PCWSTR;
    use windows::Win32::UI::WindowsAndMessaging::{MessageBoxW, IDYES, MB_ICONINFORMATION, MB_YESNO};
    let title = crate::platform::wide(title);
    let text = crate::platform::wide(text);
    // SAFETY: valid strings; no owner window.
    (unsafe { MessageBoxW(None, PCWSTR(text.as_ptr()), PCWSTR(title.as_ptr()), MB_YESNO | MB_ICONINFORMATION) }) == IDYES
}

/// Startup recovery (`HandlePendingUpdates`); true when this launch should
/// exit because an update or its recovery owns the session.
pub fn handle_pending_updates() -> Result<bool, String> {
    use crate::platform::{message_box, MessageKind};
    if super::windows::update_in_progress()? {
        message_box("VineMix update", "VineMix is finishing an update. It will reopen when the update is ready.", MessageKind::Information);
        return Ok(true);
    }
    let pending = transaction::find_pending(None, None, &ProcessIdentity::is_live)?;
    for item in pending {
        if matches!(item.phase, Phase::Prepared | Phase::Ready) && !item.can_resume {
            transaction::cancel_uncommitted(&item)?;
            continue;
        }
        if item.can_resume {
            if ask_yes_no("VineMix update recovery", "A VineMix update was interrupted. Resume its recovery now? Your saved workspace will be preserved.") {
                transaction::resume(&item)?;
            }
        } else {
            message_box(
                "VineMix update recovery",
                &format!(
                    "An earlier update needs repair before VineMix can open safely. Keep the update backup and contact the maintainer, or use the trusted installer for repair.\n\n{}",
                    item.message
                ),
                MessageKind::Information,
            );
        }
        return Ok(true);
    }
    Ok(false)
}

/// Installs a verified download (`InstallUpdateAsync`). On success the
/// engine has stopped and the worker owns the update: the caller exits.
pub fn install(link: &Arc<EngineLink>, download: &vinemix_update::Downloaded, offer: &vinemix_update::Offer) -> Result<(), String> {
    let deadline = Instant::now() + Duration::from_secs(55);
    let cancelled = move || Instant::now() >= deadline;
    let snapshot = link.prepare_for_update()?;
    let result = (|| -> Result<(), String> {
        if snapshot.engine_pid == 0 {
            return Err("The audio engine identity could not be confirmed for updating.".into());
        }
        let physical = crate::connection::normalize_physical(&snapshot.physical_settings_path);
        let resolved = vinemix_platform::physical_path(&crate::paths::settings()).map(|p| crate::connection::normalize_physical(&p.to_string_lossy())).unwrap_or_default();
        if physical.trim().is_empty() || !Path::new(&physical).is_absolute() || !files::same_path(Path::new(&physical), Path::new(&resolved)) {
            return Err("The engine's physical workspace location could not be confirmed. The current session has been kept open.".into());
        }
        let engine = ProcessIdentity::capture(snapshot.engine_pid)?;
        let mut prepared = transaction::prepare(download, offer, crate::runtime::standalone(), Some(engine), Some(Path::new(&physical)))?;
        prepared.start_worker_and_wait_ready(&cancelled)?;
        link.shutdown_for_update(&snapshot)?;
        prepared.commit()
    })();
    if result.is_err() {
        link.resume_after_update_preparation();
    }
    result
}

/// What the candidate's health check concluded.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum HealthOutcome {
    /// Confirmed and committed by the worker.
    Healthy,
    /// Failed; `stopped` means the candidate engine stopped and the app exits.
    Failed { stopped: bool, message: String },
}

/// Validates `--update-health` before startup: the worker registered this
/// process, and the version and workspace match.
pub fn begin_health(transaction_path: &Path, settings_argument: Option<&Path>) -> Result<(), String> {
    let expected = transaction::wait_for_candidate_registration(transaction_path, &|| false)?;
    let matches = expected.version == vinemix_platform::release::VERSION
        && expected.settings_path.as_deref().zip(settings_argument).is_some_and(|(expected, selected)| files::same_path(&files::full(selected), Path::new(expected)));
    if !matches {
        return Err("This update candidate does not match its approved workspace and version.".into());
    }
    Ok(())
}

/// Observes the updated candidate on a worker thread
/// (`ObserveUpdatedCandidateAsync`).
pub fn observe_candidate(link: Arc<EngineLink>, transaction_path: PathBuf, repaint: Arc<dyn Fn() + Send + Sync>) -> Receiver<HealthOutcome> {
    let (sender, receiver): (Sender<HealthOutcome>, Receiver<HealthOutcome>) = std::sync::mpsc::channel();
    std::thread::spawn(move || {
        let outcome = match check_candidate(&link, &transaction_path) {
            Ok(()) => HealthOutcome::Healthy,
            Err(error) => {
                let _ = transaction::report_health(&transaction_path, false, "The updated application did not finish its workspace and engine checks.");
                let stopped = link.stop_update_health();
                HealthOutcome::Failed { stopped, message: error }
            }
        };
        let _ = sender.send(outcome);
        repaint();
    });
    receiver
}

fn check_candidate(link: &Arc<EngineLink>, transaction_path: &Path) -> Result<(), String> {
    let deadline = Instant::now() + Duration::from_secs(30);
    let expired = AtomicBool::new(false);
    let cancelled = || {
        if Instant::now() >= deadline {
            expired.store(true, Ordering::Release);
        }
        expired.load(Ordering::Acquire)
    };
    let expected = transaction::wait_for_candidate_registration(transaction_path, &cancelled)?;
    let mut first_frames: Option<u64> = None;
    let mut engine_pid: Option<u32> = None;
    let healthy = loop {
        if cancelled() {
            return Err("The updated application did not confirm its health in time.".into());
        }
        let snapshot = link.state().snapshot().cloned();
        if let Some(snapshot) = snapshot {
            if snapshot.settings_state != "loaded"
                || i64::from(snapshot.configuration.version) != expected.settings_schema
                || snapshot.engine_protocol_version != expected.protocol_version
                || snapshot.engine_version != expected.version
                || snapshot.engine_pid == 0
            {
                return Err("The updated engine could not load the expected workspace and version.".into());
            }
            match engine_pid {
                None => {
                    engine_pid = Some(snapshot.engine_pid);
                    transaction::record_candidate_engine(transaction_path, &ProcessIdentity::capture(snapshot.engine_pid)?)?;
                    first_frames = Some(snapshot.status.audio_frames);
                }
                Some(pid) if pid != snapshot.engine_pid => return Err("The updated audio engine restarted during its health check.".into()),
                Some(_) => {}
            }
            if first_frames.is_some_and(|first| snapshot.status.audio_frames > first) {
                break snapshot;
            }
        }
        std::thread::sleep(Duration::from_millis(100));
    };
    let settings = crate::paths::settings();
    let resolved = vinemix_platform::physical_path(&settings).map(|p| crate::connection::normalize_physical(&p.to_string_lossy())).unwrap_or_default();
    let reported = crate::connection::normalize_physical(&healthy.physical_settings_path);
    let unchanged = match (&expected.settings_path, &expected.settings_sha256) {
        (Some(path), Some(digest)) => {
            files::same_path(Path::new(&reported), Path::new(path)) && files::same_path(Path::new(&resolved), Path::new(path)) && files::hash(&settings).is_ok_and(|h| files::equal_hash(&h, digest))
        }
        _ => false,
    };
    if !unchanged {
        return Err("The saved workspace changed before the update was confirmed. Its current contents have been preserved.".into());
    }
    transaction::report_health(transaction_path, true, "Expected workspace, engine identity/protocol and advancing audio frames confirmed.")?;
    let commit_deadline = Instant::now() + Duration::from_secs(30);
    if !transaction::wait_for_health_commit(transaction_path, &|| Instant::now() >= commit_deadline)? {
        return Err("The update worker did not commit its successful restart.".into());
    }
    Ok(())
}
