//! Engine I/O for the mixer state: the polling loop and every multi-step
//! engine operation (restart, recovery, retry, quit and update handoff).
//!
//! The `io` mutex is the engine gate: one flow talks to the engine at a
//! time. The state mutex is taken only between requests, so the window
//! never waits on the pipe. Lock order is always `io`, then `state`.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Condvar, Mutex, MutexGuard};
use std::thread::JoinHandle;
use std::time::{Duration, Instant};

use crate::connection::{Connection, ConnectionError, Expectations};
use crate::mixer::{MixerState, QuitSaveResult};
use crate::protocol::{same_saved_configuration, Response, Snapshot};
use crate::supervisor::Supervisor;

/// Results delivered to the window.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum LinkEvent {
    RestartFinished { success: bool, message: String },
    Message { text: String, error: bool },
}

struct Io {
    connection: Option<Connection>,
    supervisor: Option<Supervisor>,
    expectations: Option<Expectations>,
    /// A private test endpoint, when set.
    private_pipe: Option<String>,
}

impl Io {
    fn new_connection(&self) -> Option<Connection> {
        if let Some(pipe) = &self.private_pipe {
            return Some(Connection::with_pipe(pipe.clone(), None));
        }
        self.expectations.clone().and_then(|e| Connection::new(e).ok())
    }
}

pub struct EngineLink {
    state: Mutex<MixerState>,
    io: Mutex<Io>,
    stop: Arc<AtomicBool>,
    repaint: Box<dyn Fn() + Send + Sync>,
    events: Mutex<Vec<LinkEvent>>,
    poller: Mutex<Option<JoinHandle<()>>>,
    /// Counts wake-ups; waiting threads sleep on the condition variable
    /// instead of polling the stop flag.
    wake: (Mutex<u64>, Condvar),
}

impl std::fmt::Debug for EngineLink {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("EngineLink").finish_non_exhaustive()
    }
}

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(std::sync::PoisonError::into_inner)
}

fn usable(snapshot: &Snapshot) -> bool {
    snapshot.is_settings_usable()
}

impl EngineLink {
    /// The real desktop link: the user's engine endpoint and supervisor.
    pub fn new(state: MixerState, expectations: Expectations, repaint: Box<dyn Fn() + Send + Sync>) -> Arc<Self> {
        let supervisor = Supervisor::new(Some(expectations.engine_path.clone()), expectations.settings_path.clone());
        let mut state = state;
        state.restart_supervisor_available(supervisor.can_restart());
        let io = Io { connection: None, supervisor: Some(supervisor), expectations: Some(expectations), private_pipe: None };
        let link = Arc::new(Self {
            state: Mutex::new(state),
            io: Mutex::new(io),
            stop: Arc::new(AtomicBool::new(false)),
            repaint,
            events: Mutex::new(Vec::new()),
            poller: Mutex::new(None),
            wake: (Mutex::new(0), Condvar::new()),
        });
        {
            let mut io = lock(&link.io);
            io.connection = io.new_connection();
        }
        link
    }

    /// A preview link with no engine at all.
    pub fn preview(state: MixerState, repaint: Box<dyn Fn() + Send + Sync>) -> Arc<Self> {
        Arc::new(Self {
            state: Mutex::new(state),
            io: Mutex::new(Io { connection: None, supervisor: None, expectations: None, private_pipe: None }),
            stop: Arc::new(AtomicBool::new(false)),
            repaint,
            events: Mutex::new(Vec::new()),
            poller: Mutex::new(None),
            wake: (Mutex::new(0), Condvar::new()),
        })
    }

    pub fn state(&self) -> MutexGuard<'_, MixerState> {
        lock(&self.state)
    }

    pub fn take_events(&self) -> Vec<LinkEvent> {
        std::mem::take(&mut *lock(&self.events))
    }

    fn post(&self, event: LinkEvent) {
        lock(&self.events).push(event);
        (self.repaint)();
    }

    pub fn is_stopping(&self) -> bool {
        self.stop.load(Ordering::Acquire)
    }

    fn request(
        &self,
        io: &mut Io,
        command: &str,
        configuration: Option<&vinemix_model::config::Configuration>,
        revision: Option<u64>,
        deadline: Option<Instant>,
    ) -> Result<Response, ConnectionError> {
        let stop = Arc::clone(&self.stop);
        let interest = self.state().interest();
        let connection = io.connection.as_mut().ok_or_else(|| ConnectionError::Io("The audio engine is not available.".into()))?;
        connection.set_interest(Some(interest));
        let mut timeout = Duration::from_secs(3);
        if let Some(deadline) = deadline {
            let remaining = deadline.saturating_duration_since(Instant::now());
            if remaining.is_zero() {
                return Err(ConnectionError::Timeout);
            }
            timeout = timeout.min(remaining);
        }
        connection.set_request_timeout(timeout);
        connection.request(command, configuration, revision, &stop)
    }

    /// Ends every wait early: something changed that the polling thread
    /// should react to now (the window was shown, an edit was queued, or the
    /// link is stopping).
    pub fn wake(&self) {
        *lock(&self.wake.0) += 1;
        self.wake.1.notify_all();
    }

    fn set_stopping(&self) {
        self.stop.store(true, Ordering::Release);
        self.wake();
    }

    /// Waits `duration`, returning false as soon as the link is stopping. One
    /// timer wake-up per wait instead of one every 10 ms.
    fn sleep(&self, duration: Duration) -> bool {
        self.wait(duration, false)
    }

    /// Like [`sleep`](Self::sleep), but also ends early on [`wake`](Self::wake).
    fn idle(&self, duration: Duration) -> bool {
        self.wait(duration, true)
    }

    fn wait(&self, duration: Duration, until_woken: bool) -> bool {
        let deadline = Instant::now() + duration;
        let mut wakes = lock(&self.wake.0);
        let seen = *wakes;
        loop {
            if self.is_stopping() {
                return false;
            }
            if until_woken && *wakes != seen {
                return true;
            }
            let remaining = deadline.saturating_duration_since(Instant::now());
            if remaining.is_zero() {
                return true;
            }
            wakes = self.wake.1.wait_timeout(wakes, remaining).unwrap_or_else(std::sync::PoisonError::into_inner).0;
        }
    }

    /// Starts the polling loop (`PollAsync`).
    pub fn start(self: &Arc<Self>) {
        if lock(&self.state).is_preview() {
            return;
        }
        let mut poller = lock(&self.poller);
        if poller.is_some() {
            return;
        }
        let link = Arc::clone(self);
        *poller = Some(std::thread::Builder::new().name("VineMix engine link".into()).spawn(move || link.poll_loop()).ok()).flatten();
    }

    fn poll_loop(self: Arc<Self>) {
        while !self.is_stopping() {
            {
                let state = self.state();
                if state.is_preparing_update() || state.is_preparing_quit() {
                    drop(state);
                    if !self.sleep(Duration::from_millis(100)) {
                        break;
                    }
                    continue;
                }
            }
            let began = Instant::now();
            let mut stalled_wait = false;
            {
                let mut io = lock(&self.io);
                let skip = {
                    let state = self.state();
                    state.is_restarting_engine() || state.is_preparing_update() || state.is_preparing_quit()
                };
                if !skip {
                    stalled_wait = self.poll_once(&mut io);
                }
            }
            (self.repaint)();
            if stalled_wait {
                if !self.sleep(Duration::from_secs(1)) {
                    break;
                }
                continue;
            }
            let interval = {
                let state = self.state();
                if !state.can_edit() {
                    1000
                } else if state.visible || state.pending_edit_count() > 0 {
                    33
                } else {
                    1000
                }
            };
            let elapsed = began.elapsed();
            let remaining = Duration::from_millis(interval).saturating_sub(elapsed).max(Duration::from_millis(1));
            // Hidden, this is a one-second wait that the window can end at
            // once by showing itself or queueing an edit.
            if !self.idle(remaining) {
                break;
            }
        }
    }

    /// One poll. Returns true when a stalled engine was just recovered.
    fn poll_once(&self, io: &mut Io) -> bool {
        let result: Result<bool, String> = (|| {
            let response = self.request(io, "snapshot", None, None, None).map_err(|e| e.message())?;
            let snapshot = match (response.ok, response.snapshot) {
                (true, Some(snapshot)) => snapshot,
                _ => {
                    return Err(if response.error.is_empty() { "The audio engine did not provide a snapshot.".into() } else { response.error });
                }
            };
            let mut state = self.state();
            let was_connected = state.can_edit();
            state.accept(snapshot.clone(), false)?;
            let now = state.seconds();
            if !was_connected || now - state.last_poll > 8.0 || state.last_frames != snapshot.status.audio_frames {
                state.last_frame_advance = now;
                state.last_frames = snapshot.status.audio_frames;
            }
            state.last_poll = now;
            if !state.has_settings_recovery() && !state.is_update_health_mode() && now - state.last_frame_advance >= 6.0 {
                state.set_connected(false);
                drop(state);
                let status = io.supervisor.as_mut().map_or_else(String::new, Supervisor::recover_stalled);
                let mut state = self.state();
                state.connection_status = status;
                state.last_frame_advance = state.seconds();
                return Ok(true);
            }
            if state.can_edit() && state.is_pending_save_due() {
                let batch = state.prepare_pending_configuration(&snapshot.configuration);
                if batch.is_empty() {
                    return Ok(false);
                }
                state.begin_dispatch();
                drop(state);
                let configured =
                    self.request(io, "configure", Some(&batch.configuration), Some(snapshot.configuration.revision), None).map_err(|e| e.message())?;
                self.state().handle_configure_response(&batch, &snapshot, configured)?;
            }
            Ok(false)
        })();
        match result {
            Ok(stalled) => stalled,
            Err(error) => {
                if self.is_stopping() {
                    return false;
                }
                let health = self.state().is_update_health_mode();
                let status = if health {
                    let mut state = self.state();
                    if state.health_engine_start_attempted {
                        "Checking the updated audio engine…".into()
                    } else {
                        state.health_engine_start_attempted = true;
                        drop(state);
                        io.supervisor.as_mut().map_or_else(String::new, Supervisor::ensure_started)
                    }
                } else {
                    io.supervisor.as_mut().map_or_else(String::new, Supervisor::ensure_started)
                };
                self.state().connection_lost(status, error);
                false
            }
        }
    }

    fn spawn(self: &Arc<Self>, name: &str, work: impl FnOnce(Arc<Self>) + Send + 'static) {
        let link = Arc::clone(self);
        let _ = std::thread::Builder::new().name(name.into()).spawn(move || work(link));
    }

    /// Retry assigned devices (`RetryAsync`).
    pub fn retry(self: &Arc<Self>) {
        {
            let mut state = self.state();
            if state.is_preview() {
                state.warning = "Preview only · no devices are retried.".into();
                return;
            }
            if state.is_restarting_engine() || state.is_preparing_update() || state.is_preparing_quit() || self.is_stopping() {
                return;
            }
        }
        self.spawn("VineMix retry", |link| {
            let mut io = lock(&link.io);
            if link.state().is_restarting_engine() {
                return;
            }
            match link.request(&mut io, "retry", None, None, None) {
                Ok(response) if response.ok => {
                    if let Some(snapshot) = response.snapshot {
                        let _ = link.state().accept(snapshot, false);
                    }
                }
                Ok(response) => link.state().warning = response.error,
                Err(error) => link.state().warning = error.message(),
            }
            drop(io);
            (link.repaint)();
        });
    }

    /// A recovery choice (`RecoverSettingsAsync`): `recoveryRetry`,
    /// `restoreBackup` or `startFresh`.
    pub fn recover_settings(self: &Arc<Self>, command: &'static str) {
        {
            let state = self.state();
            if state.is_preview() || !state.has_settings_recovery() || state.is_preparing_quit() {
                return;
            }
        }
        self.spawn("VineMix recovery", move |link| {
            let mut io = lock(&link.io);
            let result: Result<(), String> = (|| {
                if matches!(command, "restoreBackup" | "startFresh") {
                    if let Some(configuration) = link.state().recovery_preservation() {
                        let settings = crate::paths::settings();
                        let preserved = std::path::PathBuf::from(format!(
                            "{}.ui-pending-recovery-{}.json",
                            settings.display(),
                            crate::platform::random_hex_id()
                        ));
                        vinemix_platform::create_new_durable(&preserved, vinemix_model::json::dump(&configuration.to_json()).as_bytes())
                            .map_err(|()| "Queued changes could not be preserved before recovery.".to_owned())?;
                    }
                }
                let response = link.request(&mut io, command, None, None, None).map_err(|e| e.message())?;
                let snapshot = match (response.ok, response.snapshot) {
                    (true, Some(snapshot)) => snapshot,
                    _ => return Err(response.error),
                };
                link.state().finish_recovery(command, snapshot)
            })();
            if let Err(error) = result {
                link.state().set_edit_error(error);
            }
            drop(io);
            (link.repaint)();
        });
    }

    /// An explicit engine restart (`RestartEngineAsync`). The result arrives
    /// as [`LinkEvent::RestartFinished`].
    pub fn restart_engine(self: &Arc<Self>) {
        {
            let mut state = self.state();
            if !state.can_restart_engine() {
                let message = if state.is_restarting_engine() {
                    "An audio engine restart is already in progress."
                } else {
                    "The audio engine cannot be restarted right now."
                };
                drop(state);
                self.post(LinkEvent::RestartFinished { success: false, message: message.into() });
                return;
            }
            state.set_restarting(true);
        }
        (self.repaint)();
        self.spawn("VineMix restart", |link| {
            let (success, message) = link.restart_flow();
            link.state().set_restarting(false);
            link.post(LinkEvent::RestartFinished { success, message });
        });
    }

    fn restart_flow(self: &Arc<Self>) -> (bool, String) {
        let deadline = Instant::now() + Duration::from_secs(15);
        let mut io = lock(&self.io);
        let had_pending = self.state().pending_edit_count() > 0;
        let mut attempted_restart = false;
        let mut unconfirmed = None;
        let result: Result<String, String> = (|| {
            if Instant::now() >= deadline {
                return Err("The restart timed out before audio processing resumed.".into());
            }
            io.supervisor.as_ref().ok_or("The audio engine cannot be restarted right now.")?.validate_restart_target()?;
            if self.state().pending_edit_count() > 0 {
                self.state().connection_status = "Saving changes before restart…".into();
                let saving = (Instant::now() + Duration::from_secs(4)).min(deadline);
                if self.save_pending(&mut io, None, saving, &mut unconfirmed).is_err() && Instant::now() < deadline {
                    self.state().save_status = "Changes kept in this window; retrying after restart".into();
                }
            }
            if Instant::now() >= deadline {
                return Err("The restart timed out before audio processing resumed.".into());
            }
            {
                let mut state = self.state();
                state.set_connected(false);
                state.connection_status = "Restarting the audio engine…".into();
            }
            (self.repaint)();
            attempted_restart = true;
            let stop = Arc::clone(&self.stop);
            io.supervisor
                .as_mut()
                .ok_or("The audio engine cannot be restarted right now.")?
                .restart_explicit(&|| stop.load(Ordering::Acquire) || Instant::now() >= deadline)?;
            io.connection = io.new_connection();
            self.state().connection_status = "Waiting for audio processing to resume…".into();
            (self.repaint)();
            let mut ready: Option<Snapshot> = None;
            let mut first_frames: Option<u64> = None;
            while ready.is_none() {
                if self.is_stopping() {
                    return Err("The application is closing.".into());
                }
                if Instant::now() >= deadline {
                    return Err("The restart timed out before audio processing resumed.".into());
                }
                if !io.supervisor.as_mut().is_some_and(Supervisor::is_running) {
                    return Err("The restarted engine exited before audio processing resumed.".into());
                }
                match self.read_snapshot(&mut io, Some(deadline)) {
                    Ok(snapshot) => {
                        if first_frames.is_some_and(|first| snapshot.status.audio_frames > first) {
                            ready = Some(snapshot);
                        } else {
                            first_frames = Some(snapshot.status.audio_frames);
                        }
                    }
                    Err(_) => first_frames = None,
                }
                if ready.is_none() {
                    std::thread::sleep(Duration::from_millis(100));
                }
            }
            let mut ready = ready.unwrap_or_default();
            if self.state().pending_edit_count() > 0 {
                self.state().connection_status = "Restoring queued changes…".into();
                ready = self.save_pending(&mut io, Some(ready), deadline, &mut unconfirmed)?;
            }
            if !io.supervisor.as_mut().is_some_and(Supervisor::is_running) {
                return Err("The restarted engine exited before reconnection finished.".into());
            }
            let mut state = self.state();
            let frames = ready.status.audio_frames;
            state.accept(ready, true)?;
            state.last_frames = frames;
            let now = state.seconds();
            state.last_frame_advance = now;
            state.last_poll = now;
            state.connection_status = "Audio engine restarted".into();
            Ok(if had_pending && state.pending_edit_count() == 0 {
                "Audio engine restarted. Your changes are saved.".into()
            } else {
                "Audio engine restarted.".into()
            })
        })();
        match result {
            Ok(message) => (true, message),
            Err(reason) => {
                let mut state = self.state();
                if attempted_restart {
                    state.set_connected(false);
                }
                state.connection_status = "Audio engine restart did not finish".into();
                let pending = state.pending_edit_count() > 0;
                if pending {
                    state.save_status = "Changes kept in this window; waiting to save".into();
                }
                let reason = if self.is_stopping() { "The application is closing.".to_owned() } else { reason };
                (
                    false,
                    format!(
                        "{}{}{}",
                        if attempted_restart { "Could not finish restarting the audio engine. " } else { "The audio engine was not restarted. " },
                        reason,
                        if pending { " Your unsaved changes are still queued in this window." } else { "" }
                    ),
                )
            }
        }
    }

    fn read_snapshot(&self, io: &mut Io, deadline: Option<Instant>) -> Result<Snapshot, String> {
        let response = self.request(io, "snapshot", None, None, deadline).map_err(|e| e.message())?;
        match (response.ok, response.snapshot) {
            (true, Some(snapshot)) => Ok(snapshot),
            _ => Err(if response.error.is_empty() { "The engine did not return its saved settings.".into() } else { response.error }),
        }
    }

    /// Strictly replays and saves the queue, recognising a configure that
    /// committed before its response was lost.
    fn save_pending(
        &self,
        io: &mut Io,
        mut current: Option<Snapshot>,
        deadline: Instant,
        unconfirmed: &mut Option<crate::mixer::PendingBatch>,
    ) -> Result<Snapshot, String> {
        for _ in 0..3 {
            if Instant::now() >= deadline {
                return Err("The restart timed out before audio processing resumed.".into());
            }
            let snapshot = match current.take() {
                Some(snapshot) => snapshot,
                None => self.read_snapshot(io, Some(deadline))?,
            };
            let mut state = self.state();
            if let Some(batch) = unconfirmed.as_ref() {
                if same_saved_configuration(&batch.configuration, &snapshot.configuration) {
                    let batch = unconfirmed.take().unwrap_or_else(|| unreachable!());
                    state.acknowledge_pending(&batch, None)?;
                }
            }
            if state.pending_edit_count() == 0 {
                return Ok(snapshot);
            }
            let batch = state.prepare_restart_configuration(&snapshot.configuration)?;
            drop(state);
            *unconfirmed = Some(batch.clone());
            let saved = self
                .request(io, "configure", Some(&batch.configuration), Some(snapshot.configuration.revision), Some(deadline))
                .map_err(|e| e.message())?;
            if saved.ok {
                if let Some(saved_snapshot) = saved.snapshot {
                    let mut state = self.state();
                    state.acknowledge_pending(&batch, None)?;
                    *unconfirmed = None;
                    if state.pending_edit_count() == 0 {
                        return Ok(saved_snapshot);
                    }
                    current = Some(saved_snapshot);
                    continue;
                }
            } else {
                *unconfirmed = None;
                if !crate::graph::reasons::is_revision_conflict(&saved.error_code, &saved.error) {
                    return Err(format!("The engine could not save queued changes: {}", saved.error));
                }
            }
        }
        Err("The saved workspace kept changing. Queued changes were retained.".into())
    }

    /// Saves the queue before quitting (`PrepareForQuitAsync`). Reversible:
    /// polling is suspended but the engine stays running.
    pub fn prepare_for_quit(self: &Arc<Self>) -> Result<QuitSaveResult, String> {
        {
            let state = self.state();
            if state.is_restarting_engine()
                || ((state.is_preparing_update() || state.is_update_health_mode()) && !state.can_quit_after_failed_update_health())
                || self.is_stopping()
            {
                return Err("Wait for the current audio engine operation to finish before quitting.".into());
            }
        }
        let deadline = Instant::now() + Duration::from_secs(5);
        self.state().begin_quit_preparation();
        (self.repaint)();
        let fail = |link: &Self, detail: String| {
            let mut state = link.state();
            state.set_save_error(format!("Your latest changes have not been saved. {detail}"));
            state.save_status = "Changes not saved · quit paused".into();
            QuitSaveResult { saved: false, error: detail, pending_changes: state.pending_edit_count() }
        };
        if let Err(error) = self.state().ensure_ui_preferences_saved() {
            return Ok(fail(self, error));
        }
        let mut io = lock(&self.io);
        {
            let mut state = self.state();
            if let Err(error) = state.ensure_ui_preferences_saved() {
                drop(state);
                return Ok(fail(self, error));
            }
            state.remember_quit_recovery(None);
            if state.pending_edit_count() == 0 {
                return Ok(QuitSaveResult { saved: true, error: String::new(), pending_changes: 0 });
            }
            if state.is_update_health_mode() {
                return Ok(QuitSaveResult {
                    saved: false,
                    error: "The update health check failed. Keep a recovery copy of these queued changes or cancel quitting; saving to the update workspace remains blocked.".into(),
                    pending_changes: state.pending_edit_count(),
                });
            }
            if state.is_preview() {
                return Ok(QuitSaveResult {
                    saved: false,
                    error: "The audio engine is unavailable. Your queued edits are still in this window.".into(),
                    pending_changes: state.pending_edit_count(),
                });
            }
        }
        let read = |response: Response| -> Result<Snapshot, String> {
            let snapshot = if response.ok { response.snapshot } else { None };
            let snapshot = snapshot.ok_or_else(|| {
                if response.error.is_empty() { "The audio engine did not confirm its saved workspace.".to_owned() } else { response.error.clone() }
            })?;
            if !usable(&snapshot) || snapshot.configuration.version != 2 {
                return Err("Workspace recovery is required. Your queued edits can still be saved to a recovery copy.".into());
            }
            Ok(snapshot)
        };
        let result: Result<(), String> = (|| {
            for attempt in 0..3 {
                let current = read(self.request(&mut io, "snapshot", None, None, Some(deadline)).map_err(|e| self.quit_error(&e))?)?;
                let mut state = self.state();
                let configuration = current.configuration.clone();
                state.reconcile_unconfirmed_quit(&configuration);
                if state.pending_edit_count() == 0 {
                    state.accept(current, true)?;
                    state.ensure_ui_preferences_saved()?;
                    return Ok(());
                }
                let batch = state.prepare_restart_configuration(&configuration)?;
                state.remember_quit_recovery(Some(batch.configuration.clone()));
                state.set_unconfirmed_quit_batch(Some(batch.clone()));
                drop(state);
                let saved = match self.request(&mut io, "configure", Some(&batch.configuration), Some(configuration.revision), Some(deadline)) {
                    Ok(saved) => saved,
                    // Read back a committed-but-unconfirmed append before replaying it.
                    Err(_) if attempt < 2 && Instant::now() < deadline && !self.is_stopping() => continue,
                    Err(error) => return Err(self.quit_error(&error)),
                };
                if !saved.ok {
                    self.state().set_unconfirmed_quit_batch(None);
                    let lower = saved.error.to_lowercase();
                    if lower.contains("revision") || lower.contains("stale") {
                        continue;
                    }
                    return Err(if saved.error.is_empty() { "The audio engine could not save your changes.".into() } else { saved.error });
                }
                let confirmed = read(saved)?;
                if !same_saved_configuration(&batch.configuration, &confirmed.configuration) {
                    return Err("The audio engine did not confirm the saved changes.".into());
                }
                let mut state = self.state();
                state.acknowledge_pending(&batch, None)?;
                state.set_unconfirmed_quit_batch(None);
                state.accept(confirmed, true)?;
                state.ensure_ui_preferences_saved()?;
                return Ok(());
            }
            Err("The saved workspace kept changing. Your queued edits have been retained.".into())
        })();
        Ok(match result {
            Ok(()) => QuitSaveResult { saved: true, error: String::new(), pending_changes: 0 },
            Err(detail) => fail(self, detail),
        })
    }

    fn quit_error(&self, error: &ConnectionError) -> String {
        if error.is_timeout() { "The audio engine did not finish saving in time.".into() } else { error.message() }
    }

    pub fn cancel_quit_preparation(&self) {
        self.state().cancel_quit_preparation();
        (self.repaint)();
    }

    /// The irreversible phase (`StopAsync`): stop polling, ask the engine to
    /// shut down and wait for it.
    pub fn stop(self: &Arc<Self>) {
        self.set_stopping();
        self.state().stopping = true;
        if let Some(poller) = lock(&self.poller).take() {
            let _ = poller.join();
        }
        let mut io = lock(&self.io);
        self.state().set_connected(false);
        if self.state().is_preview() {
            return;
        }
        if let Some(connection) = io.connection.as_mut() {
            let never = AtomicBool::new(false);
            connection.set_request_timeout(Duration::from_secs(4));
            let _ = connection.request("shutdown", None, None, &never);
        }
        if let Some(supervisor) = io.supervisor.as_mut() {
            supervisor.stop_for_quit();
        }
    }

    /// Releases the window's hold without stopping the engine (the engine
    /// outlives the window when the process exits without Quit).
    pub fn detach(&self) {
        self.set_stopping();
        if let Some(poller) = lock(&self.poller).take() {
            let _ = poller.join();
        }
    }

    // ----- updates ------------------------------------------------------

    /// Saves everything before an update (`PrepareForUpdateAsync`).
    pub fn prepare_for_update(self: &Arc<Self>) -> Result<Snapshot, String> {
        {
            let mut state = self.state();
            if state.is_preparing_update() || !state.can_restart_engine() {
                return Err("Wait until the audio engine is connected and workspace recovery is complete before updating.".into());
            }
            state.set_preparing_update(true);
        }
        let result = self.prepare_for_update_inner();
        if result.is_err() {
            self.resume_after_update_preparation();
        }
        result
    }

    fn prepare_for_update_inner(self: &Arc<Self>) -> Result<Snapshot, String> {
        self.state().ensure_ui_preferences_saved()?;
        let deadline = Instant::now() + Duration::from_secs(20);
        let mut io = lock(&self.io);
        io.supervisor.as_ref().ok_or("The audio engine cannot be restarted right now.")?.validate_restart_target()?;
        self.state().connection_status = "Saving your workspace before updating…".into();
        let release = vinemix_platform::release::VERSION;
        let read = |response: Response, preview: bool| -> Result<Snapshot, String> {
            let snapshot = if response.ok { response.snapshot } else { None };
            let snapshot = snapshot.ok_or_else(|| {
                if response.error.is_empty() { "The audio engine did not confirm its saved workspace.".to_owned() } else { response.error.clone() }
            })?;
            if snapshot.configuration.version != 2
                || snapshot.engine_protocol_version > 1
                || (!preview && (snapshot.engine_protocol_version != 1 || snapshot.engine_version != release || snapshot.engine_pid == 0))
            {
                return Err("The audio engine returned an incompatible workspace. Finish recovery or reopen the matching VineMix build before updating.".into());
            }
            Ok(snapshot)
        };
        let preview = self.state().is_preview();
        for attempt in 0..3 {
            let current = read(self.request(&mut io, "snapshot", None, None, Some(deadline)).map_err(|e| e.message())?, preview)?;
            if !usable(&current) {
                return Err("Finish workspace recovery before updating. Your current session has been kept open.".into());
            }
            let mut state = self.state();
            if state.unconfirmed_update_batch().is_some_and(|b| same_saved_configuration(&b.configuration, &current.configuration)) {
                let batch = state.take_unconfirmed_update_batch().unwrap_or_else(|| unreachable!());
                state.acknowledge_pending(&batch, None)?;
                if state.pending_edit_count() == 0 {
                    state.accept(current.clone(), true)?;
                    state.ensure_ui_preferences_saved()?;
                    return Ok(current);
                }
            }
            let batch = state.prepare_restart_configuration(&current.configuration)?;
            state.set_unconfirmed_update_batch(Some(batch.clone()));
            drop(state);
            let saved = match self.request(&mut io, "configure", Some(&batch.configuration), Some(current.configuration.revision), Some(deadline)) {
                Ok(saved) => saved,
                Err(_) if attempt < 2 && !self.is_stopping() => continue,
                Err(error) => return Err(error.message()),
            };
            if !saved.ok {
                self.state().set_unconfirmed_update_batch(None);
                let lower = saved.error.to_lowercase();
                if lower.contains("revision") || lower.contains("stale") {
                    continue;
                }
                return Err(format!("The update was not started because your changes could not be saved. {}", saved.error));
            }
            let confirmed = read(saved, preview)?;
            if !usable(&confirmed) || !same_saved_configuration(&batch.configuration, &confirmed.configuration) {
                return Err("The saved workspace could not be confirmed. The update was not started.".into());
            }
            let mut state = self.state();
            state.acknowledge_pending(&batch, None)?;
            state.set_unconfirmed_update_batch(None);
            state.accept(confirmed.clone(), true)?;
            state.ensure_ui_preferences_saved()?;
            return Ok(confirmed);
        }
        Err("The workspace changed repeatedly while saving. Your edits have been retained; try the update again.".into())
    }

    pub fn resume_after_update_preparation(&self) {
        if self.is_stopping() {
            return;
        }
        {
            let mut io = lock(&self.io);
            if !self.state().is_preview() {
                io.connection = io.new_connection();
                self.state().set_connected(false);
            }
        }
        let mut state = self.state();
        state.set_preparing_update(false);
        state.connection_status = if state.pending_edit_count() > 0 {
            "Update cancelled · your changes are still queued".into()
        } else {
            "Update cancelled · audio session kept open".into()
        };
        drop(state);
        (self.repaint)();
    }

    /// Stops the saved engine for the update handoff (`ShutdownForUpdateAsync`).
    pub fn shutdown_for_update(self: &Arc<Self>, prepared: &Snapshot) -> Result<(), String> {
        if !self.state().is_preparing_update() {
            return Err("Save the workspace before stopping audio for an update.".into());
        }
        let result = (|| -> Result<(), String> {
            self.state().ensure_ui_preferences_saved()?;
            let deadline = Instant::now() + Duration::from_secs(10);
            let mut io = lock(&self.io);
            let latest = self.request(&mut io, "snapshot", None, None, Some(deadline)).map_err(|e| e.message())?;
            let unchanged = latest.ok
                && latest.snapshot.as_ref().is_some_and(|s| {
                    usable(s)
                        && s.configuration.version == 2
                        && same_saved_configuration(&prepared.configuration, &s.configuration)
                        && s.engine_pid == prepared.engine_pid
                })
                && self.state().pending_edit_count() == 0;
            if !unchanged {
                return Err("The workspace or audio engine changed during update preparation. Try again after it has saved.".into());
            }
            self.state().ensure_ui_preferences_saved()?;
            self.state().connection_status = "Stopping audio for the update…".into();
            let response = self.request(&mut io, "shutdown", None, None, Some(deadline)).map_err(|e| e.message())?;
            if !response.ok {
                return Err(format!("The audio engine did not accept a clean shutdown. {}", response.error));
            }
            if !self.state().is_preview() {
                let remaining = deadline.saturating_duration_since(Instant::now());
                io.supervisor.as_ref().ok_or("The audio engine is unavailable.")?.wait_for_update_exit(prepared.engine_pid, remaining)?;
            }
            Ok(())
        })();
        if result.is_err() {
            self.resume_after_update_preparation();
        }
        result
    }

    /// Stops the candidate engine after an update health check
    /// (`StopUpdateHealthAsync`).
    pub fn stop_update_health(self: &Arc<Self>) -> bool {
        if self.state().is_preview() {
            return true;
        }
        let deadline = Instant::now() + Duration::from_secs(5);
        self.state().set_preparing_update(true);
        let mut io = lock(&self.io);
        let Ok(response) = self.request(&mut io, "shutdown", None, None, Some(deadline)) else { return false };
        let pid = self.state().snapshot().map(|s| s.engine_pid);
        let (true, Some(pid)) = (response.ok, pid) else { return false };
        let remaining = deadline.saturating_duration_since(Instant::now());
        if io.supervisor.as_ref().is_none_or(|s| s.wait_for_update_exit(pid, remaining).is_err()) {
            return false;
        }
        self.set_stopping();
        true
    }
}

#[cfg(test)]
mod wait_tests {
    use super::*;

    fn link() -> Arc<EngineLink> {
        EngineLink::preview(MixerState::new(true), Box::new(|| {}))
    }

    /// RUST-REV-012: waits are interruptible instead of polled.
    #[test]
    fn idle_ends_at_once_when_woken_and_sleep_does_not() {
        let link = link();
        let waker = Arc::clone(&link);
        let started = Instant::now();
        let thread = std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(100));
            waker.wake();
        });
        assert!(link.idle(Duration::from_secs(30)), "woken, not stopping");
        let woke_after = started.elapsed();
        assert!(woke_after >= Duration::from_millis(100) && woke_after < Duration::from_secs(5), "{woke_after:?}");
        thread.join().unwrap();
        // A fixed-length sleep is not shortened by an ordinary wake.
        let waker = Arc::clone(&link);
        let started = Instant::now();
        let thread = std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(50));
            waker.wake();
        });
        assert!(link.sleep(Duration::from_millis(400)));
        assert!(started.elapsed() >= Duration::from_millis(400), "{:?}", started.elapsed());
        thread.join().unwrap();
    }

    #[test]
    fn stopping_ends_every_wait_promptly() {
        for until_woken in [false, true] {
            let link = link();
            let stopper = Arc::clone(&link);
            let started = Instant::now();
            let thread = std::thread::spawn(move || {
                std::thread::sleep(Duration::from_millis(100));
                stopper.set_stopping();
            });
            assert!(!link.wait(Duration::from_secs(30), until_woken), "a stopping link reports it");
            assert!(started.elapsed() < Duration::from_secs(5), "{:?}", started.elapsed());
            thread.join().unwrap();
            assert!(!link.sleep(Duration::from_secs(30)), "later waits return immediately");
        }
    }

    #[test]
    fn an_undisturbed_wait_lasts_its_duration() {
        let link = link();
        let started = Instant::now();
        assert!(link.idle(Duration::from_millis(250)));
        let elapsed = started.elapsed();
        assert!(elapsed >= Duration::from_millis(250) && elapsed < Duration::from_secs(3), "{elapsed:?}");
    }
}
