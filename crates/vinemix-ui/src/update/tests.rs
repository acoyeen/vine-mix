//! Worker and transaction scenarios (`VineMix.UpdateWorkerTests`): ephemeral
//! signed releases, private temporary folders and a fake platform. No real
//! process, registry, Setup, engine or personal settings are touched.

use std::cell::{Cell, RefCell};
use std::rc::Rc;
use std::path::{Path, PathBuf};
use std::time::Duration;

use vinemix_update::testing::{manifest_payload, TestKey};
use vinemix_update::Trust;

use super::files;
use super::model::{Journal, Phase, ProcessIdentity, RegistrySnapshot, Signal, TransactionData, UtcTime};
use super::transaction::{self, Verified};
use super::worker::{self, Lock, Platform};

const OLD: &[u8] = b"old launcher bytes";
const NEW: &[u8] = b"new standalone launcher bytes";
const VERSION: &str = "9.1.0";
const KEY: &str = "vinemix-test-1";

fn sha(bytes: &[u8]) -> String {
    vinemix_update::crypto::hex(&vinemix_update::crypto::sha256(bytes).unwrap())
}

struct Fixture {
    base: PathBuf,
    root: PathBuf,
    install: PathBuf,
    trust: Trust,
    context: Verified,
}

impl Fixture {
    fn new(name: &str) -> Self {
        let base = std::env::temp_dir().join(format!("vinemix-update-{name}-{}-{}", std::process::id(), crate::platform::random_hex_id()));
        let root = base.join("updates");
        let install = base.join("install");
        std::fs::create_dir_all(&install).unwrap();
        files::create_private_directory(&root).unwrap();
        let target = install.join("VineMix.exe");
        std::fs::write(&target, OLD).unwrap();
        let key = TestKey::generate().unwrap();
        let payload = manifest_payload(VERSION, "9.1.0.0", (1234, &"ab".repeat(32)), (NEW.len() as u64, &sha(NEW)));
        let envelope = key.envelope(KEY, &payload).unwrap();
        let trust = key.trust(KEY);
        let offer = trust.verify(&envelope, VERSION, vinemix_update::Compatibility::current()).unwrap();
        let id = crate::platform::random_hex_id();
        let directory = root.join(&id);
        files::create_private_directory(&directory).unwrap();
        let asset_name = offer.manifest.standalone.file_name.clone();
        let scratch = base.join("asset.bin");
        std::fs::write(&scratch, NEW).unwrap();
        files::copy_private_checked(&scratch, &directory.join(&asset_name), NEW.len() as u64, &sha(NEW)).unwrap();
        files::copy_private_checked(&target, &directory.join("worker.exe"), OLD.len() as u64, &sha(OLD)).unwrap();
        files::write_bytes(&directory.join("envelope.json"), &envelope).unwrap();
        let current = ProcessIdentity::current().unwrap();
        let parent = ProcessIdentity { image_path: files::full(&target).to_string_lossy().into_owned(), sha256: sha(OLD), ..current.clone() };
        let engine = ProcessIdentity { process_id: current.process_id + 1, ..parent.clone() };
        let data = TransactionData {
            format_version: 1,
            id,
            version: VERSION.into(),
            current_version: vinemix_platform::release::VERSION.into(),
            token: "AB".repeat(32),
            envelope_sha256: offer.envelope_sha256.clone(),
            asset_file_name: asset_name,
            standalone: true,
            parent,
            engine: Some(engine),
            settings_path: None,
            created_utc: UtcTime::now(),
        };
        files::write_json(&directory.join("transaction.json"), &data).unwrap();
        files::write_json(&directory.join("journal.json"), &Journal::default()).unwrap();
        let context = transaction::verify(&directory.join("transaction.json"), Some(&trust), Some(&root)).unwrap();
        Self { base, root, install, trust, context }
    }

    fn commit(&self) {
        files::write_json(&self.context.directory.join("commit.json"), &self.context.signal(false, "")).unwrap();
    }

    fn run(&self, platform: &FakePlatform) -> i32 {
        worker::run(&self.context.transaction_path(), platform, Some(&self.trust), Some(&self.root))
    }

    fn target(&self) -> Vec<u8> {
        std::fs::read(self.install.join("VineMix.exe")).unwrap()
    }

    fn journal(&self) -> Journal {
        transaction::read_journal(&self.context).unwrap()
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.base);
    }
}

/// Records every side effect; the candidate reports health unless told not to.
struct FakePlatform {
    events: Rc<RefCell<Vec<String>>>,
    parent_live: Cell<bool>,
    parent_polls_remaining: Cell<usize>,
    parent_exits: Cell<bool>,
    healthy: bool,
    smoke_passes: bool,
    smoke_timeout: bool,
    smoke_process: RefCell<Option<ProcessIdentity>>,
    smoke_exits: Cell<bool>,
    candidate: RefCell<Option<ProcessIdentity>>,
}

impl FakePlatform {
    fn new() -> Self {
        Self {
            events: Rc::new(RefCell::new(Vec::new())), parent_live: Cell::new(true),
            parent_polls_remaining: Cell::new(usize::MAX), parent_exits: Cell::new(true),
            healthy: true, smoke_passes: true, smoke_timeout: false, smoke_process: RefCell::new(None),
            smoke_exits: Cell::new(true), candidate: RefCell::new(None),
        }
    }
    fn event(&self, name: &str) {
        self.events.borrow_mut().push(name.into());
    }
    fn has(&self, name: &str) -> bool {
        self.events.borrow().iter().any(|e| e == name)
    }
    fn index(&self, name: &str) -> usize {
        self.events.borrow().iter().position(|e| e == name).unwrap_or(usize::MAX)
    }
}

impl Platform for FakePlatform {
    fn acquire_lock(&self) -> files::Result<Lock> {
        self.event("lock");
        let events = Rc::clone(&self.events);
        Ok(Lock::new(move || events.borrow_mut().push("lock-released".into())))
    }
    fn watch_live_process(&self, identity: &ProcessIdentity) -> Box<dyn Fn() -> bool + '_> {
        let candidate = self.candidate.borrow().as_ref().is_some_and(|c| c.same(identity));
        self.event(if candidate { "verify-candidate" } else { "verify-parent" });
        Box::new(move || {
            if candidate { return self.healthy; }
            self.event("poll-parent");
            let remaining = self.parent_polls_remaining.get();
            self.parent_polls_remaining.set(remaining.saturating_sub(1));
            self.parent_live.get() && remaining > 0
        })
    }
    fn wait_for_exit(&self, identity: &ProcessIdentity, _: Duration) -> files::Result<bool> {
        if self.smoke_process.borrow().as_ref().is_some_and(|p| p.same(identity)) {
            self.event("smoke-exit");
            return Ok(self.smoke_exits.get());
        }
        let parent = self.candidate.borrow().as_ref().is_none_or(|c| !c.same(identity));
        if parent && identity.process_id == std::process::id() as i32 {
            self.event("parent-exit");
            return Ok(self.parent_exits.get());
        }
        self.event("engine-exit");
        Ok(true)
    }
    fn verify_worker(&self, _: &Verified) -> files::Result<()> {
        Ok(())
    }
    fn check_file_version(&self, _: &Path, _: &str) -> files::Result<()> {
        Ok(())
    }
    fn read_registration(&self, _: &Verified) -> files::Result<Option<RegistrySnapshot>> {
        Ok(None)
    }
    fn restore_registration(&self, _: &RegistrySnapshot, _: Option<&RegistrySnapshot>) -> files::Result<()> {
        Ok(())
    }
    fn register_recovery(&self, _: &Verified, attempt: i32) -> files::Result<()> {
        self.event(&format!("register-{attempt}"));
        Ok(())
    }
    fn clear_recovery(&self, _: &Verified) -> files::Result<()> {
        self.event("clear-recovery");
        Ok(())
    }
    fn run_setup(&self, _: &Verified, _: &mut dyn FnMut(ProcessIdentity) -> files::Result<()>) -> files::Result<i32> {
        self.event("setup");
        Ok(0)
    }
    fn run_smoke(&self, context: &Verified, _: &Path, started: &mut dyn FnMut(ProcessIdentity) -> files::Result<()>) -> files::Result<bool> {
        self.event("smoke");
        if self.smoke_timeout {
            let mut identity = context.data.parent.clone();
            identity.process_id += 8;
            identity.sha256 = sha(NEW);
            *self.smoke_process.borrow_mut() = Some(identity.clone());
            started(identity)?;
            return Err("Timed out while the smoke process was still running.".into());
        }
        Ok(self.smoke_passes)
    }
    fn candidate_engine_hash(&self, _: &Verified, _: &Path) -> files::Result<String> {
        Ok("cd".repeat(32))
    }
    fn start_candidate(&self, context: &Verified) -> files::Result<ProcessIdentity> {
        self.event("candidate-start");
        let mut identity = ProcessIdentity::current()?;
        identity.process_id += 7;
        identity.image_path = context.data.parent.image_path.clone();
        identity.sha256 = sha(NEW);
        *self.candidate.borrow_mut() = Some(identity.clone());
        if self.healthy {
            let signal = Signal {
                token: context.data.token.clone(),
                envelope_sha256: context.data.envelope_sha256.clone(),
                process_id: identity.process_id,
                start_time_utc_ticks: identity.start_time_utc_ticks,
                success: true,
                message: "healthy".into(),
            };
            files::write_json(&context.directory.join("health.json"), &signal)?;
        }
        Ok(identity)
    }
    fn reopen_previous(&self, _: &Verified) -> files::Result<()> {
        self.event("previous-reopened");
        Ok(())
    }
    fn wait_for_candidate_audio_exit(&self, _: &Verified, _: Option<&str>, _: Duration) -> files::Result<bool> {
        Ok(true)
    }
    fn delay(&self, _: u64) {
        std::thread::sleep(Duration::from_millis(5));
    }
}

#[test]
fn successful_standalone_update() {
    let fixture = Fixture::new("success");
    fixture.commit();
    let platform = FakePlatform::new();
    assert_eq!(fixture.run(&platform), 0, "{:?}", platform.events.borrow());
    assert_eq!(fixture.target(), NEW, "the exact signed bytes are installed");
    let retained = PathBuf::from(format!("{}.vinemix-{}.old", fixture.install.join("VineMix.exe").display(), fixture.context.data.id));
    assert_eq!(std::fs::read(retained).unwrap(), OLD, "the original launcher is retained beside it");
    assert_eq!(fixture.journal().phase, Phase::Completed);
    assert!(platform.index("parent-exit") < platform.index("smoke") && platform.index("engine-exit") < platform.index("candidate-start"));
    assert_eq!(platform.events.borrow().last().map(String::as_str), Some("lock-released"), "the lock spans completion");
}

#[test]
fn tampered_asset_is_rejected_before_any_effect() {
    let fixture = Fixture::new("tamper");
    fixture.commit();
    let asset = fixture.context.asset_path();
    let mut bytes = std::fs::read(&asset).unwrap();
    bytes.push(b'!');
    // The asset is private; rewrite it through the private writer.
    std::fs::remove_file(&asset).unwrap();
    files::write_bytes(&asset, &bytes).unwrap();
    let platform = FakePlatform::new();
    assert_eq!(fixture.run(&platform), 1);
    assert!(platform.events.borrow().is_empty(), "no lock, registry or process action");
    assert_eq!(fixture.target(), OLD);
}

#[test]
fn unhealthy_candidate_rolls_back_and_reopens_previous() {
    let fixture = Fixture::new("rollback");
    fixture.commit();
    let platform = FakePlatform { healthy: false, ..FakePlatform::new() };
    assert_eq!(fixture.run(&platform), 3, "{:?}", platform.events.borrow());
    assert_eq!(fixture.target(), OLD, "the previous launcher is restored");
    let journal = fixture.journal();
    assert_eq!(journal.phase, Phase::RolledBack);
    assert!(journal.message.contains("previous owned application payload was restored"));
    assert!(platform.has("previous-reopened"));
    assert!(platform.index("lock-released") < platform.index("previous-reopened"), "reopen only after releasing the update lock");
}

#[test]
fn failed_smoke_check_restores_before_candidate_start() {
    let fixture = Fixture::new("smoke");
    fixture.commit();
    let platform = FakePlatform { smoke_passes: false, ..FakePlatform::new() };
    assert_eq!(fixture.run(&platform), 3);
    assert_eq!(fixture.target(), OLD);
    assert!(!platform.has("candidate-start"));
}

#[test]
fn uncommitted_handoff_waits_then_cancels_on_abort() {
    let fixture = Fixture::new("abort");
    files::write_json(&fixture.context.directory.join("abort.json"), &fixture.context.signal(false, "")).unwrap();
    let platform = FakePlatform::new();
    assert_eq!(fixture.run(&platform), 2);
    assert_eq!(fixture.journal().phase, Phase::Cancelled);
    assert!(fixture.context.directory.join("ready.json").exists(), "the worker announced readiness first");
    assert_eq!(fixture.target(), OLD);
}

#[test]
fn abandoned_handoff_requires_recovery_without_changes() {
    let fixture = Fixture::new("abandoned");
    let platform = FakePlatform::new();
    platform.parent_live.set(false);
    assert_eq!(fixture.run(&platform), 1);
    let journal = fixture.journal();
    assert_eq!(journal.phase, Phase::RecoveryRequired);
    assert!(journal.resume_phase.is_none());
    assert_eq!(fixture.target(), OLD);
}

#[test]
fn pending_discovery_reports_committed_and_skips_terminal() {
    let fixture = Fixture::new("pending");
    // Uncommitted with a live parent: still owned by its application.
    let pending = transaction::find_pending(Some(&fixture.trust), Some(&fixture.root), &|_| true).unwrap();
    assert!(pending.is_empty());
    fixture.commit();
    let pending = transaction::find_pending(Some(&fixture.trust), Some(&fixture.root), &|_| true).unwrap();
    assert_eq!(pending.len(), 1);
    assert_eq!(pending[0].phase, Phase::Committed);
    assert!(pending[0].can_resume);
    let platform = FakePlatform::new();
    assert_eq!(fixture.run(&platform), 0);
    assert!(transaction::find_pending(Some(&fixture.trust), Some(&fixture.root), &|_| true).unwrap().is_empty(), "completed history is inert");
}

#[test]
fn transaction_outside_root_or_with_wrong_worker_is_rejected() {
    let fixture = Fixture::new("boundary");
    assert!(transaction::verify(&fixture.context.transaction_path(), Some(&fixture.trust), Some(&fixture.install)).is_err());
    let worker = fixture.context.worker_path();
    std::fs::remove_file(&worker).unwrap();
    files::write_bytes(&worker, b"different worker").unwrap();
    assert!(transaction::verify(&fixture.context.transaction_path(), Some(&fixture.trust), Some(&fixture.root)).is_err());
}

#[test]
fn malformed_journal_is_contained() {
    let fixture = Fixture::new("journal");
    std::fs::remove_file(fixture.context.journal_path()).unwrap();
    files::write_bytes(&fixture.context.journal_path(), br#"{"phase":4,"backup":{"files":[],"registry":null}}"#).unwrap();
    assert!(transaction::read_journal(&fixture.context).is_err(), "a backed-up phase with an empty inventory is rejected");
    let pending = transaction::find_pending(Some(&fixture.trust), Some(&fixture.root), &|_| true).unwrap();
    assert_eq!(pending.len(), 1);
    assert!(!pending[0].can_resume, "malformed metadata never resumes automatically");
}

#[test]
fn committed_shutdown_timeout_can_resume_after_normal_exit() {
    let fixture = Fixture::new("exit-retry");
    fixture.commit();
    let platform = FakePlatform::new();
    platform.parent_exits.set(false);
    assert_eq!(fixture.run(&platform), 1);
    let journal = fixture.journal();
    assert_eq!(journal.phase, Phase::RecoveryRequired);
    assert_eq!(journal.resume_phase, Some(Phase::WaitingForExit));
    let pending = transaction::find_pending(Some(&fixture.trust), Some(&fixture.root), &|_| false).unwrap();
    assert!(pending[0].can_resume);
    assert_eq!(fixture.target(), OLD);
    platform.parent_exits.set(true);
    assert_eq!(fixture.run(&platform), 0);
    assert_eq!(fixture.journal().phase, Phase::Completed);
}

#[test]
fn smoke_timeout_retains_identity_and_waits_before_rollback() {
    let fixture = Fixture::new("smoke-retry");
    fixture.commit();
    let platform = FakePlatform { smoke_timeout: true, ..FakePlatform::new() };
    platform.smoke_exits.set(false);
    assert_eq!(fixture.run(&platform), 1);
    let journal = fixture.journal();
    assert_eq!(journal.phase, Phase::RecoveryRequired);
    assert_eq!(journal.resume_phase, Some(Phase::RollingBack));
    assert_eq!(journal.smoke_process.as_ref(), platform.smoke_process.borrow().as_ref());
    assert!(platform.has("smoke-exit"));
    assert_eq!(fixture.target(), NEW);
    platform.smoke_exits.set(true);
    assert_eq!(fixture.run(&platform), 3);
    assert_eq!(fixture.target(), OLD);
    assert_eq!(platform.events.borrow().iter().filter(|e| *e == "smoke").count(), 1);
}

#[test]
fn approval_polls_one_verified_parent_process() {
    let fixture = Fixture::new("process-watch");
    let platform = FakePlatform::new();
    platform.parent_polls_remaining.set(4);
    assert_eq!(fixture.run(&platform), 2);
    let events = platform.events.borrow();
    assert_eq!(events.iter().filter(|e| *e == "verify-parent").count(), 1);
    assert_eq!(events.iter().filter(|e| *e == "poll-parent").count(), 5);
}

#[test]
fn incomplete_checked_copies_never_publish_or_replace_the_destination() {
    let fixture = Fixture::new("copy-retry");
    let source = fixture.base.join("source");
    std::fs::write(&source, NEW).unwrap();
    for (private, root) in [(true, &fixture.context.directory), (false, &fixture.install)] {
        let destination = root.join("checkpoint");
        let copy = if private { files::copy_private_checked } else { files::copy_checked };
        let size = NEW.len() as u64;
        assert!(copy(&source, &destination, size, &sha(OLD)).is_err());
        assert!(!destination.exists());
        // A killed process can leave an unpublished sibling. It never reserves the checkpoint.
        let abandoned = root.join(".vinemix-copy-abandoned.tmp");
        std::fs::write(&abandoned, b"partial").unwrap();
        copy(&source, &destination, size, &sha(NEW)).unwrap();
        assert_eq!(std::fs::read(&destination).unwrap(), NEW);
        assert_eq!(std::fs::read(&abandoned).unwrap(), b"partial");
        std::fs::write(&destination, b"unrelated").unwrap();
        assert!(copy(&source, &destination, size, &sha(NEW)).is_err());
        assert_eq!(std::fs::read(&destination).unwrap(), b"unrelated");
    }
}

/// Where the cross-language records are exchanged. These two tests need the
/// 0.5 C# serializer's side (`VineMix.UpdateWorkerTests --serializer-interop-*`,
/// driven by `scripts/verify-rust.ps1`), so they are ignored in an ordinary
/// run and say so. Run on purpose without their fixtures, they fail: neither
/// can pass without doing its work.
fn interop_directory() -> PathBuf {
    let base = std::env::var_os("VINEMIX_INTEROP_DIR").map(PathBuf::from).expect("VINEMIX_INTEROP_DIR must name the interop exchange directory");
    assert!(base.is_dir(), "the interop exchange directory {} does not exist", base.display());
    base
}

/// Writes Rust records for the C# cross-check.
#[test]
#[ignore = "cross-language check: needs VINEMIX_INTEROP_DIR (scripts/verify-rust.ps1 provides it)"]
fn interop_write() {
    let directory = interop_directory().join("from-rust");
    std::fs::create_dir_all(&directory).unwrap();
    let identity = ProcessIdentity { process_id: 4242, start_time_utc_ticks: 639_000_000_000_000_000, image_path: "C:\\Programs\\VineMix\\VineMix.exe".into(), sha256: "a".repeat(64) };
    let data = TransactionData {
        format_version: 1,
        id: "2".repeat(32),
        version: "0.6.0".into(),
        current_version: "0.5.0-beta".into(),
        token: "F".repeat(64),
        envelope_sha256: "b".repeat(64),
        asset_file_name: "VineMix-0.6.0-Setup-x64.exe".into(),
        standalone: false,
        parent: identity.clone(),
        engine: None,
        settings_path: Some("C:\\Users\\Example\\AppData\\Local\\VineMix\\settings.json".into()),
        created_utc: UtcTime::parse("2026-11-07T02:03:04.1234567+00:00").unwrap(),
    };
    let registry = RegistrySnapshot {
        key: "k".into(),
        target_directory: "C:\\Programs\\VineMix".into(),
        values: vec![super::model::RegistryValue { name: "Bin".into(), kind: 3, strings: vec![], bytes: vec![1, 2, 3, 250], number: 0 }],
    };
    let journal = Journal {
        phase: Phase::AwaitingHealth,
        resume_phase: Some(Phase::RollingBack),
        attempts: 1,
        backup: Some(super::model::Backup { files: vec![super::model::FileEntry { path: "VineMix.exe".into(), size: 10, sha256: "d".repeat(64) }], registry: Some(registry) }),
        candidate_engine_sha256: Some("c".repeat(64)),
        ..Journal::default()
    };
    let health = Signal { token: "F".repeat(64), envelope_sha256: "b".repeat(64), process_id: 4243, start_time_utc_ticks: 639_000_000_000_000_001, success: true, message: "ok".into() };
    let workspace = super::model::WorkspaceCheckpoint { token: "F".repeat(64), envelope_sha256: "b".repeat(64), path: "C:\\x\\settings.json".into(), size: 5, sha256: "e".repeat(64) };
    let engine = super::model::EngineSignal { token: "F".repeat(64), envelope_sha256: "b".repeat(64), identity };
    for (name, value) in [
        ("transaction.json", serde_json::to_vec_pretty(&data).unwrap()),
        ("journal.json", serde_json::to_vec_pretty(&journal).unwrap()),
        ("health.json", serde_json::to_vec_pretty(&health).unwrap()),
        ("workspace.json", serde_json::to_vec_pretty(&workspace).unwrap()),
        ("candidate-engine.json", serde_json::to_vec_pretty(&engine).unwrap()),
    ] {
        std::fs::write(directory.join(name), value).unwrap();
    }
}

/// Reads records written by the 0.5 C# serializer.
#[test]
#[ignore = "cross-language check: needs records from the 0.5 C# serializer (scripts/verify-rust.ps1 provides them)"]
fn interop_read() {
    let directory = interop_directory().join("to-rust");
    assert!(directory.is_dir(), "the 0.5 C# serializer has not written {}", directory.display());
    let data: TransactionData = serde_json::from_slice(&std::fs::read(directory.join("transaction.json")).unwrap()).unwrap();
    assert_eq!(data.parent.process_id, 77);
    assert_eq!(data.parent.start_time_utc_ticks, 638_999_999_999_999_999);
    assert!(data.engine.is_none() && data.settings_path.is_none());
    assert!(data.created_utc > UtcTime::parse("2026-01-01T00:00:00Z").unwrap());
    let journal: Journal = serde_json::from_slice(&std::fs::read(directory.join("journal.json")).unwrap()).unwrap();
    assert_eq!(journal.phase, Phase::AwaitingHealth);
    assert_eq!(journal.attempts, 2);
    let registry = journal.backup.as_ref().and_then(|b| b.registry.as_ref()).unwrap();
    assert_eq!(registry.values[0].bytes, vec![9, 8, 7]);
    assert_eq!(registry.values[1].number, -5);
    assert!(journal.candidate.as_ref().unwrap().registry.is_none());
    let ready: Signal = serde_json::from_slice(&std::fs::read(directory.join("ready.json")).unwrap()).unwrap();
    assert_eq!(ready.process_id, 0);
    assert!(!ready.success);
}
