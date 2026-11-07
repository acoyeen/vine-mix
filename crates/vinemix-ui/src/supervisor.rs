//! Engine process supervision (`EngineSupervisor`, `SessionOwnership.ValidateEngine`).
//!
//! The desktop starts the engine next to itself with the workspace path,
//! recovers a stalled engine within a launch budget (three per minute,
//! three seconds apart), and only ever stops an engine whose identity it
//! launched or verified. The engine outlives the window.

use std::collections::VecDeque;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::{Duration, Instant};

use crate::platform::{self, Process};
use crate::text;

pub const ENGINE_FILE_NAME: &str = "VineMix.Engine.exe";
const LEGACY_ENGINE_FILE_NAME: &str = "AudioMixer.Engine.exe";

type Identity = (u32, u64);

static VERIFIED_IDENTITY: Mutex<Option<Identity>> = Mutex::new(None);

/// Records the engine that proved its workspace identity over the pipe.
pub fn confirm_engine_identity(pid: u32) {
    if let Some(process) = Process::open(pid) {
        if let Some(started) = process.start_time() {
            *VERIFIED_IDENTITY.lock().unwrap_or_else(std::sync::PoisonError::into_inner) = Some((pid, started));
        }
    }
}

fn verified_identity() -> Option<Identity> {
    *VERIFIED_IDENTITY.lock().unwrap_or_else(std::sync::PoisonError::into_inner)
}

fn matches_identity(process: &Process, identity: Option<Identity>) -> bool {
    identity.is_some_and(|(pid, started)| process.id() == pid && process.start_time() == Some(started))
}

/// Refuses to continue while another installation's engine runs in this
/// session. Returns whether this installation's engine is running.
pub fn validate_engine(expected: &Path) -> Result<bool, String> {
    let session = platform::current_session_id();
    let mut found = false;
    for name in [LEGACY_ENGINE_FILE_NAME, ENGINE_FILE_NAME] {
        for pid in platform::processes_named(name) {
            let Some(process) = Process::open(pid) else {
                // Exited during enumeration, or a different user's process.
                if platform::session_of(pid) == Some(session) {
                    return Err("The running engine could not be identified safely.".into());
                }
                continue;
            };
            if process.has_exited() || process.session() != Some(session) {
                continue;
            }
            let Some(image) = process.image_path() else {
                return Err("The running engine could not be identified safely.".into());
            };
            if !text::same_path(&image, expected) {
                // A development instance has its own pipe, mutexes and data
                // folder, so another installation's engine may keep running.
                if vinemix_platform::development_instance().is_some() {
                    continue;
                }
                return Err("Another version of VineMix is running. Quit it from its tray menu before opening this version. Your audio and workspace have not been changed.".into());
            }
            found = true;
        }
    }
    Ok(found)
}

#[derive(Debug)]
pub struct Supervisor {
    engine_path: Option<PathBuf>,
    settings_path: PathBuf,
    launches: VecDeque<Instant>,
    last_attempt: Option<Instant>,
    process: Option<Process>,
    launched_identity: Option<Identity>,
}

impl Supervisor {
    pub fn new(engine_path: Option<PathBuf>, settings_path: PathBuf) -> Self {
        Self { engine_path, settings_path, launches: VecDeque::new(), last_attempt: None, process: None, launched_identity: None }
    }

    pub fn engine_path_text(&self) -> String {
        self.engine_path
            .as_ref()
            .map_or_else(|| "VineMix.Engine.exe was not found next to the application.".into(), |p| p.display().to_string())
    }

    pub fn engine_path(&self) -> Option<&Path> {
        self.engine_path.as_deref()
    }

    pub fn can_restart(&self) -> bool {
        self.engine_path.is_some()
    }

    pub fn is_running(&mut self) -> bool {
        self.find_engine()
    }

    fn has_launch_budget(&mut self) -> bool {
        while self.launches.front().is_some_and(|t| t.elapsed() > Duration::from_secs(60)) {
            self.launches.pop_front();
        }
        self.launches.len() < 3
    }

    fn is_owned(&self, process: &Process) -> bool {
        let Some(engine) = &self.engine_path else { return false };
        !process.has_exited()
            && process.session() == Some(platform::current_session_id())
            && process.image_path().is_some_and(|p| text::same_path(&p, engine))
            && (matches_identity(process, self.launched_identity) || matches_identity(process, verified_identity()))
    }

    /// Finds this installation's engine in this session.
    fn find_engine(&mut self) -> bool {
        if let Some(process) = &self.process {
            if self.is_owned(process) {
                return true;
            }
            self.process = None;
        }
        let Some(engine) = self.engine_path.clone() else { return false };
        let session = platform::current_session_id();
        for pid in platform::processes_named(ENGINE_FILE_NAME) {
            let Some(candidate) = Process::open(pid) else { continue };
            if candidate.session() == Some(session) && candidate.image_path().is_some_and(|p| text::same_path(&p, &engine)) {
                self.process = Some(candidate);
                return true;
            }
        }
        false
    }

    fn start_arguments(&self) -> Vec<String> {
        // Every start opens the controller's workspace explicitly.
        vec!["--settings".into(), crate::paths::absolute(&self.settings_path).to_string_lossy().into_owned()]
    }

    fn launch(&mut self) -> Result<(), String> {
        let engine = self.engine_path.clone().ok_or_else(|| self.engine_path_text())?;
        let engine = crate::paths::absolute(&engine);
        let directory = engine.parent().map(Path::to_path_buf).unwrap_or_default();
        let process = platform::launch(&engine, &self.start_arguments(), &directory)?;
        self.launched_identity = process.start_time().map(|started| (process.id(), started));
        self.process = Some(process);
        Ok(())
    }

    pub fn validate_restart_target(&self) -> Result<(), String> {
        let engine = self.engine_path.as_ref().ok_or_else(|| self.engine_path_text())?;
        if !engine.is_file() {
            return Err("The audio engine executable is missing. Restore it before restarting audio.".into());
        }
        validate_engine(engine)?;
        let session = platform::current_session_id();
        for pid in platform::processes_named(LEGACY_ENGINE_FILE_NAME) {
            let Some(candidate) = Process::open(pid) else { continue };
            if candidate.has_exited() || candidate.session() != Some(session) {
                continue;
            }
            match candidate.image_path() {
                Some(image) if text::same_path(&image, engine) => {}
                Some(_) if vinemix_platform::development_instance().is_some() => {}
                Some(_) => return Err("Another VineMix installation is running. Close it before restarting this engine.".into()),
                None => return Err("The running audio engine could not be identified safely.".into()),
            }
        }
        Ok(())
    }

    /// An explicit restart: stop the owned engine, then launch it again.
    pub fn restart_explicit(&mut self, cancelled: &dyn Fn() -> bool) -> Result<(), String> {
        self.validate_restart_target()?;
        if self.find_engine() {
            if cancelled() {
                return Err("The restart was cancelled.".into());
            }
            let process = self.process.take().ok_or("The audio engine changed before it could be restarted.")?;
            if !self.is_owned(&process) {
                return Err("The audio engine changed before it could be restarted.".into());
            }
            process.kill()?;
            if !process.wait(Duration::from_secs(2)) {
                return Err("The restart timed out before audio processing resumed.".into());
            }
        }
        if cancelled() {
            return Err("The restart was cancelled.".into());
        }
        self.validate_restart_target()?;
        // A deliberate recovery is allowed after automatic recovery used its
        // budget, but still counts against it.
        let now = Instant::now();
        self.last_attempt = Some(now);
        self.launches.push_back(now);
        self.launch().map_err(|e| format!("The audio engine could not be started. {e}"))?;
        std::thread::sleep(Duration::from_millis(150));
        match &self.process {
            Some(process) if self.is_owned(process) => Ok(()),
            _ => Err("The audio engine exited before it could reconnect.".into()),
        }
    }

    pub fn ensure_started(&mut self) -> String {
        if self.engine_path.is_none() {
            return self.engine_path_text();
        }
        if self.find_engine() {
            return "Connecting to the audio engine…".into();
        }
        if !self.has_launch_budget() {
            return "Automatic recovery limit reached. Waiting before another attempt…".into();
        }
        if self.last_attempt.is_some_and(|t| t.elapsed() < Duration::from_secs(3)) {
            return "Starting the audio engine…".into();
        }
        let now = Instant::now();
        self.last_attempt = Some(now);
        // Failed attempts count too, so a missing runtime cannot loop.
        self.launches.push_back(now);
        let engine = self.engine_path.clone().unwrap_or_default();
        match validate_engine(&engine).and_then(|_| self.launch()) {
            Ok(()) => "Starting the audio engine…".into(),
            Err(error) => format!("Could not start the audio engine: {error}"),
        }
    }

    pub fn recover_stalled(&mut self) -> String {
        if !self.has_launch_budget() {
            return "Audio processing stalled. Recovery is paused to prevent repeated restarts.".into();
        }
        if self.find_engine() {
            let Some(process) = self.process.take() else { return self.ensure_started() };
            if !self.is_owned(&process) {
                self.process = Some(process);
                return "This engine's workspace has not been verified. Quit its owning session before switching.".into();
            }
            if let Err(error) = process.kill() {
                return format!("Audio processing stalled. Could not restart the engine: {error}");
            }
            if !process.wait(Duration::from_secs(2)) {
                return "Audio processing stalled. Could not restart the engine: The operation was canceled.".into();
            }
        }
        self.ensure_started()
    }

    /// After a cooperative shutdown request, wait up to two seconds and
    /// then terminate this controller's verified engine.
    pub fn stop_for_quit(&mut self) {
        if !self.find_engine() {
            return;
        }
        let Some(process) = self.process.take() else { return };
        if !self.is_owned(&process) {
            return;
        }
        if !process.wait(Duration::from_secs(2)) && !process.has_exited() {
            let _ = process.kill();
        }
    }

    /// Waits for the verified engine to exit for an update.
    pub fn wait_for_update_exit(&self, expected_pid: u32, timeout: Duration) -> Result<(), String> {
        let Some(identity) = verified_identity().filter(|(pid, _)| *pid == expected_pid) else {
            return Err("The audio engine owner was not verified for this update.".into());
        };
        let Some(process) = Process::open(expected_pid) else { return Ok(()) };
        if process.has_exited() {
            return Ok(());
        }
        let engine = self.engine_path.as_deref().unwrap_or(Path::new(""));
        if !matches_identity(&process, Some(identity)) || !process.image_path().is_some_and(|p| text::same_path(&p, engine)) {
            return Err("The audio engine identity changed before update shutdown completed.".into());
        }
        if process.wait(timeout) { Ok(()) } else { Err("The audio engine did not stop in time.".into()) }
    }
}

/// The engine shipped next to this executable.
pub fn resolve_engine_path() -> Result<PathBuf, String> {
    crate::runtime::resolve_engine_path(None)
}
