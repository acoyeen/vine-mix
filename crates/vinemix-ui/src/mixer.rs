//! The desktop's mixer state (`MixerViewModel` and its partials).
//!
//! The window edits an optimistic `desired` workspace immediately. Each edit
//! is also queued as a replayable operation; the queue is rebased onto every
//! newly acknowledged engine configuration and sent as one coalesced
//! configure at most every 100 ms. A refused operation drops only itself and
//! resets history; settings-write failures keep the exact queue for retry.
//!
//! This type is pure state: blocking engine I/O happens in [`crate::link`],
//! which locks this state only between requests, exactly as the 0.5
//! desktop's async continuations interleaved with the UI thread.

use std::collections::HashSet;
use std::time::{Duration, Instant};

use serde::{Deserialize, Serialize};
use vinemix_model::config::{AudioGraph, Configuration, GraphNode};
use vinemix_model::status::{ApplicationInfo, Endpoint, NodeStatus};

use crate::frames::{self, Frame};
use crate::graph::bindings;
use crate::history::{self, GraphState, History};
use crate::protocol::{Response, Snapshot};

/// A configuration plus its desktop-only groups (`MixerConfiguration` with
/// `Graph.Frames`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Workspace {
    pub config: Configuration,
    pub frames: Vec<Frame>,
}

impl Workspace {
    pub fn from_config(config: Configuration) -> Self {
        Self { config, frames: Vec::new() }
    }

    pub fn graph(&self) -> &AudioGraph {
        &self.config.graph
    }

    pub fn graph_state(&self) -> GraphState {
        GraphState { graph: self.config.graph.clone(), frames: self.frames.clone() }
    }

    pub fn set_graph_state(&mut self, state: GraphState) {
        self.config.graph = state.graph;
        self.frames = state.frames;
    }

    pub fn normalize_frames(&mut self) {
        self.frames = frames::normalize_for(&self.frames, &self.config.graph.nodes);
    }

    pub fn node(&self, id: &str) -> Option<&GraphNode> {
        self.config.graph.nodes.iter().find(|n| n.id == id)
    }

    pub fn node_mut(&mut self, id: &str) -> Option<&mut GraphNode> {
        self.config.graph.nodes.iter_mut().find(|n| n.id == id)
    }
}

/// A replayable edit. Unchecked edits always succeed; checked edits may
/// refuse a changed destination.
pub type EditFn = dyn Fn(&mut Workspace) -> Result<(), String> + Send;

struct PendingEdit {
    id: u64,
    apply: Box<EditFn>,
    is_restore: bool,
    frames_before: Option<Vec<Frame>>,
    frames_after: Option<Vec<Frame>>,
}

#[derive(Clone, Debug)]
pub struct PendingBatch {
    pub configuration: Configuration,
    edits: Vec<u64>,
}

impl PendingBatch {
    pub fn is_empty(&self) -> bool {
        self.edits.is_empty()
    }
}

/// Coalesces edits without waiting for pointer movement to stop
/// (`EditDispatchCadence`).
#[derive(Debug, Default)]
struct Cadence {
    due: Option<Duration>,
}

const DISPATCH_INTERVAL: Duration = Duration::from_millis(100);

impl Cadence {
    fn queue(&mut self, now: Duration) {
        if self.due.is_none() {
            self.due = Some(now + DISPATCH_INTERVAL);
        }
    }
    fn is_due(&self, now: Duration) -> bool {
        self.due.is_some_and(|due| now >= due)
    }
    fn begin_dispatch(&mut self, now: Duration) {
        self.due = Some(now + DISPATCH_INTERVAL);
    }
    fn complete(&mut self, has_pending: bool) {
        if !has_pending {
            self.due = None;
        }
    }
    fn defer_retry(&mut self, now: Duration, delay: Option<Duration>) {
        self.due = Some(now + delay.unwrap_or(DISPATCH_INTERVAL));
    }
}

fn nullable<'de, D, T>(deserializer: D) -> Result<T, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Default + Deserialize<'de>,
{
    Ok(Option::<T>::deserialize(deserializer)?.unwrap_or_default())
}

/// `ui.json`: desktop preferences and groups.
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
struct UiPreferences {
    automatically_check_for_updates: Option<bool>,
    reduced_animation: Option<bool>,
    show_action_notifications: Option<bool>,
    right_click_drag_to_pan: bool,
    show_minimap: Option<bool>,
    show_disabled_audio_devices: bool,
    show_disconnected_audio_devices: bool,
    show_advanced_audio_devices: bool,
    #[serde(deserialize_with = "nullable")]
    frames: Vec<Option<Frame>>,
}

pub const LAYOUT_LOCKED_MESSAGE: &str = "Layout locked. Unlock the padlock to move nodes or change connections.";

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct QuitSaveResult {
    pub saved: bool,
    pub error: String,
    pub pending_changes: usize,
}

pub struct MixerState {
    preview: bool,
    /// Detached verification: queue edits even in preview.
    pub queue_preview_edits: bool,
    pending: Vec<PendingEdit>,
    next_edit_id: u64,
    automatic_assignment_attempts: HashSet<String>,
    automatic_assignment_suppressed: HashSet<String>,
    cadence: Cadence,
    clock: Instant,
    // Processing-load presentation (post-0.5 IDEA-057).
    load_presenter: crate::processing_load::Presenter,
    processing_load: crate::processing_load::Presentation,
    processing_notice: String,
    processing_advisory: String,
    processing_advisory_until: f64,
    processing_advised_at: f64,
    processing_notice_sequence: u64,
    snapshot: Option<Snapshot>,
    recovery_pending: Option<Workspace>,
    pub needs_initial_frame: bool,
    desired: Workspace,
    canvas_view: Option<(f64, f64, f64)>,
    pub history: History,
    ui_preferences_need_save: bool,
    pub(crate) last_frame_advance: f64,
    pub(crate) last_poll: f64,
    pub(crate) last_frames: u64,
    connected: bool,
    pub visible: bool,
    start_at_login: bool,
    reduced_animation: bool,
    right_click_drag_to_pan: bool,
    show_disabled_audio_devices: bool,
    show_disconnected_audio_devices: bool,
    show_advanced_audio_devices: bool,
    show_action_notifications: bool,
    show_minimap: bool,
    automatically_check_for_updates: bool,
    registered_startup: Option<bool>,
    pub connection_status: String,
    pub warning: String,
    edit_error: String,
    save_error: String,
    startup_error: String,
    ui_error: String,
    pub save_status: String,
    pub frame_label: String,
    endpoints: Vec<Endpoint>,
    applications: Vec<ApplicationInfo>,
    applying: bool,
    restarting_engine: bool,
    preparing_quit: bool,
    preparing_update: bool,
    update_health_mode: bool,
    failed_update_health_quit_allowed: bool,
    pub health_engine_start_attempted: bool,
    layout_locked: bool,
    visual_frames: Vec<Frame>,
    visual_frames_need_save: bool,
    unconfirmed_quit_batch: Option<PendingBatch>,
    unconfirmed_update_batch: Option<PendingBatch>,
    quit_recovery_configuration: Option<Workspace>,
    pub(crate) stopping: bool,
    pub(crate) can_restart_supervisor: bool,
    /// Incremented for every telemetry or configuration refresh.
    pub meter_sample_sequence: u64,
    pub configuration_refresh_count: u64,
    pub telemetry_only_snapshot_count: u64,
    pub configuration_presentation_count: u64,
    /// Incremented when a locked layout refused an edit.
    pub layout_edit_blocked: u64,
    /// Incremented when node-selector inventories change.
    pub inventory_generation: u64,
    preferences_writer: Option<Box<dyn FnMut(&str) -> Result<(), String> + Send>>,
    /// Where `ui.json` lives for this session (`None` in previews).
    preferences_path: Option<std::path::PathBuf>,
    /// Background writer for `preferences_path`, started on the first save.
    preferences_file: Option<crate::preferences::Writer>,
    /// The file on disk is the last loaded or saved good data, so a save may
    /// keep it as the backup. False after an unreadable load.
    preferences_primary_good: bool,
    /// An unreadable file could not be copied aside: never overwrite it.
    preferences_blocked: bool,
    /// The newest preferences have been handed to the writer.
    preferences_submitted: bool,
    /// What happened to unreadable preferences at startup (kept all session).
    preferences_notice: String,
    /// EQ nodes whose spectrum plot was drawn, with when (model seconds).
    spectrum_viewers: std::collections::HashMap<String, f64>,
}

impl std::fmt::Debug for MixerState {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("MixerState").field("pending", &self.pending.len()).field("connected", &self.connected).finish_non_exhaustive()
    }
}

impl MixerState {
    pub fn new(preview: bool) -> Self {
        let mut state = Self {
            preview,
            queue_preview_edits: false,
            pending: Vec::new(),
            next_edit_id: 1,
            automatic_assignment_attempts: HashSet::new(),
            automatic_assignment_suppressed: HashSet::new(),
            cadence: Cadence::default(),
            clock: Instant::now(),
            load_presenter: crate::processing_load::Presenter::default(),
            processing_load: crate::processing_load::Presentation::unavailable("Waiting for audio processing measurements."),
            processing_notice: String::new(),
            processing_advisory: String::new(),
            processing_advisory_until: 0.0,
            processing_advised_at: f64::NEG_INFINITY,
            processing_notice_sequence: 0,
            snapshot: None,
            recovery_pending: None,
            needs_initial_frame: false,
            desired: Workspace::default(),
            canvas_view: None,
            history: History::default(),
            ui_preferences_need_save: false,
            last_frame_advance: 0.0,
            last_poll: 0.0,
            last_frames: 0,
            connected: false,
            visible: true,
            start_at_login: false,
            reduced_animation: !crate::platform::client_area_animation(),
            right_click_drag_to_pan: false,
            show_disabled_audio_devices: false,
            show_disconnected_audio_devices: false,
            show_advanced_audio_devices: false,
            show_action_notifications: true,
            show_minimap: true,
            // Opt-in: automatic checks contact GitHub, so they start off.
            automatically_check_for_updates: false,
            registered_startup: None,
            connection_status: "Connecting to the audio engine…".into(),
            warning: String::new(),
            edit_error: String::new(),
            save_error: String::new(),
            startup_error: String::new(),
            ui_error: String::new(),
            save_status: String::new(),
            frame_label: "Engine offline".into(),
            endpoints: Vec::new(),
            applications: Vec::new(),
            applying: false,
            restarting_engine: false,
            preparing_quit: false,
            preparing_update: false,
            update_health_mode: false,
            failed_update_health_quit_allowed: false,
            health_engine_start_attempted: false,
            layout_locked: false,
            visual_frames: Vec::new(),
            visual_frames_need_save: false,
            unconfirmed_quit_batch: None,
            unconfirmed_update_batch: None,
            quit_recovery_configuration: None,
            stopping: false,
            can_restart_supervisor: false,
            meter_sample_sequence: 0,
            configuration_refresh_count: 0,
            telemetry_only_snapshot_count: 0,
            configuration_presentation_count: 0,
            layout_edit_blocked: 0,
            inventory_generation: 0,
            preferences_writer: None,
            preferences_path: None,
            preferences_file: None,
            preferences_primary_good: true,
            preferences_blocked: false,
            preferences_submitted: false,
            preferences_notice: String::new(),
            spectrum_viewers: std::collections::HashMap::new(),
        };
        if !preview {
            state.load_ui_preferences_file(crate::paths::preferences());
        }
        state
    }

    /// A non-preview state whose preferences live at `path` (tests use a
    /// disposable file; the application uses [`crate::paths::preferences`]).
    pub fn with_preferences_file(path: std::path::PathBuf) -> Self {
        let mut state = Self::new(true);
        state.preview = false;
        state.load_ui_preferences_file(path);
        state
    }

    /// Loads `ui.json`. Unreadable data is copied aside before anything can
    /// replace it; the last good backup is used when there is one.
    fn load_ui_preferences_file(&mut self, path: std::path::PathBuf) {
        use crate::preferences::{backup_path, preserve_damaged, read_bounded, ReadOutcome};
        self.preferences_path = Some(path.clone());
        let damage = match read_bounded(&path) {
            ReadOutcome::Missing => return,
            ReadOutcome::Bytes(bytes) => match self.load_ui_preferences_json(&bytes) {
                Ok(()) => return,
                Err(error) => error,
            },
            ReadOutcome::Unreadable(error) => error,
        };
        crate::paths::lifetime_log::write(&format!("Workspace preferences unreadable: {damage}"));
        self.preferences_primary_good = false;
        let kept = match preserve_damaged(&path) {
            Ok(copy) => format!("The unreadable file was kept as {}.", copy.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()),
            Err(_) => {
                self.preferences_blocked = true;
                "The unreadable file could not be copied aside, so it is left untouched and preference changes last for this session only.".to_owned()
            }
        };
        let restored = match read_bounded(&backup_path(&path)) {
            ReadOutcome::Bytes(bytes) => self.load_ui_preferences_json(&bytes).is_ok(),
            _ => false,
        };
        self.preferences_notice = if restored {
            format!("Workspace preferences could not be read. The previous saved copy, including its node groups, was restored. {kept}")
        } else {
            format!("Workspace preferences could not be read. Default navigation, device visibility and Windows animation preferences are being used, and saved node groups are not shown. {kept}")
        };
        self.update_warning();
    }

    /// The EQ nodes whose spectrum was drawn in the frame just painted.
    pub fn note_spectrum_viewers(&mut self, nodes: impl IntoIterator<Item = String>) {
        let now = self.seconds();
        for node in nodes {
            self.spectrum_viewers.insert(node, now);
        }
    }

    /// What the engine should compute for display right now: spectra only
    /// for EQ plots drawn within the last half second of a visible window,
    /// and the list of running applications only while the window is visible
    /// and the workspace has an app-capture node to show it in (its picker
    /// and its "app not detected" notice are the list's only uses).
    pub fn interest(&mut self) -> serde_json::Value {
        let now = self.seconds();
        self.spectrum_viewers.retain(|_, seen| now - *seen <= Self::SPECTRUM_INTEREST_SECONDS);
        let mut spectrum: Vec<&str> = if self.visible { self.spectrum_viewers.keys().map(String::as_str).collect() } else { Vec::new() };
        spectrum.sort_unstable();
        let applications = self.visible && self.desired.config.graph.nodes.iter().any(|node| node.kind == "appInput");
        serde_json::json!({"spectrum": spectrum, "applications": applications})
    }

    /// Long enough to bridge frames between polls, short enough that a plot
    /// scrolled out of view stops costing an FFT almost at once.
    const SPECTRUM_INTEREST_SECONDS: f64 = 0.5;

    /// The startup notice about unreadable preferences, if any.
    pub fn preferences_notice(&self) -> &str {
        &self.preferences_notice
    }

    /// A preview whose preferences go to `writer` instead of a file.
    pub fn set_preview_preferences_writer(&mut self, writer: Box<dyn FnMut(&str) -> Result<(), String> + Send>) {
        self.preferences_writer = Some(writer);
    }

    pub fn now(&self) -> Duration {
        self.clock.elapsed()
    }

    /// Re-evaluates the load reading (`RefreshProcessingLoad`); `now` is for
    /// tests, otherwise the model clock.
    pub fn refresh_processing_load(&mut self, now: Option<f64>) {
        let now = now.unwrap_or_else(|| self.seconds());
        let connected = self.can_edit() && !self.has_settings_recovery();
        let (sample, pid) = match &self.snapshot {
            Some(snapshot) => (snapshot.status.processing_load.clone(), snapshot.engine_pid),
            None => (None, 0),
        };
        self.processing_load = self.load_presenter.present(sample.as_ref(), pid, connected, now);
        if !self.processing_load.available || now >= self.processing_advisory_until {
            self.processing_advisory.clear();
        }
        self.update_processing_notice();
    }

    fn update_processing_notice(&mut self) {
        let notice = if self.processing_load.notice.is_empty() { self.processing_advisory.clone() } else { self.processing_load.notice.clone() };
        if self.processing_notice != notice {
            self.processing_notice = notice;
            self.processing_notice_sequence += 1;
        }
    }

    pub fn processing_load(&self) -> &crate::processing_load::Presentation {
        &self.processing_load
    }

    /// The current processing notice (a load warning or a recent advisory).
    pub fn processing_notice(&self) -> &str {
        &self.processing_notice
    }

    /// Changes whenever the notice text changes, so a dismissal applies only
    /// to the notice that was showing.
    pub fn processing_notice_sequence(&self) -> u64 {
        self.processing_notice_sequence
    }

    /// Advice in the add menu when capacity is limited (`ProcessingAdditionAdvice`).
    pub fn processing_addition_advice(&self, kind: &str) -> &'static str {
        if self.processing_load.available && self.processing_load.addition_advised && matches!(kind, "distortion" | "denoiser") {
            "Limited spare processing capacity: adding this effect may cause interruptions."
        } else {
            ""
        }
    }

    /// After an edit that adds Distortion or RNNoise while capacity is limited,
    /// advise once per 30 s for 10 s (`ConsiderProcessingLoadEdit`).
    fn consider_processing_load_edit(&mut self, before: &vinemix_model::config::AudioGraph, after: &vinemix_model::config::AudioGraph) {
        self.refresh_processing_load(None);
        if !self.processing_load.available || !self.processing_load.addition_advised {
            return;
        }
        let heavy = |node: &vinemix_model::config::GraphNode| node.kind == "distortion" || (node.kind == "denoiser" && node.denoiser.algorithm == "rnnoise");
        let added = after.nodes.iter().any(|node| heavy(node) && !before.nodes.iter().any(|old| old.id == node.id && heavy(old)));
        let now = self.seconds();
        if !added || now - self.processing_advised_at < 30.0 {
            return;
        }
        self.processing_advised_at = now;
        self.processing_advisory_until = now + 10.0;
        self.processing_advisory = "This effect adds substantial processing. Limited spare capacity may cause interruptions, especially while changing connections.".into();
        self.update_processing_notice();
    }

    pub fn seconds(&self) -> f64 {
        self.clock.elapsed().as_secs_f64()
    }

    // ----- preferences --------------------------------------------------

    pub fn load_ui_preferences_json(&mut self, json: &[u8]) -> Result<(), String> {
        let preferences: Option<UiPreferences> = serde_json::from_slice(json).map_err(|e| e.to_string())?;
        let preferences = preferences.unwrap_or_default();
        self.automatically_check_for_updates = preferences.automatically_check_for_updates.unwrap_or(false);
        self.reduced_animation = preferences.reduced_animation.unwrap_or(self.reduced_animation);
        self.show_action_notifications = preferences.show_action_notifications.unwrap_or(true);
        self.right_click_drag_to_pan = preferences.right_click_drag_to_pan;
        self.show_minimap = preferences.show_minimap.unwrap_or(true);
        self.show_disabled_audio_devices = preferences.show_disabled_audio_devices;
        self.show_disconnected_audio_devices = preferences.show_disconnected_audio_devices;
        self.show_advanced_audio_devices = preferences.show_advanced_audio_devices;
        let frames: Vec<Frame> = preferences.frames.into_iter().flatten().collect();
        self.visual_frames = frames::normalize(&frames, None);
        Ok(())
    }

    pub fn serialize_ui_preferences(&self) -> String {
        let preferences = UiPreferences {
            automatically_check_for_updates: Some(self.automatically_check_for_updates),
            reduced_animation: Some(self.reduced_animation),
            show_action_notifications: Some(self.show_action_notifications),
            right_click_drag_to_pan: self.right_click_drag_to_pan,
            show_minimap: Some(self.show_minimap),
            show_disabled_audio_devices: self.show_disabled_audio_devices,
            show_disconnected_audio_devices: self.show_disconnected_audio_devices,
            show_advanced_audio_devices: self.show_advanced_audio_devices,
            frames: self.visual_frames.iter().cloned().map(Some).collect(),
        };
        serde_json::to_string(&preferences).unwrap_or_default()
    }

    fn save_ui_preferences(&mut self) {
        self.ui_preferences_need_save = true;
        if self.has_settings_recovery() {
            return;
        }
        if self.preview && self.preferences_writer.is_none() {
            self.ui_preferences_need_save = false;
            return;
        }
        let json = self.serialize_ui_preferences();
        if let Some(writer) = self.preferences_writer.as_mut() {
            // Previews and tests: an immediate, synchronous sink.
            let result = writer(&json);
            self.finish_ui_preferences_save(result, true);
            return;
        }
        if self.preferences_blocked {
            self.finish_ui_preferences_save(Err("the unreadable preferences file is being left untouched".into()), false);
            return;
        }
        if self.preferences_file.is_none() {
            let path = self.preferences_path.clone().unwrap_or_else(crate::paths::preferences);
            match crate::preferences::Writer::new(crate::preferences::file_sink(path, self.preferences_primary_good)) {
                Ok(writer) => self.preferences_file = Some(writer),
                Err(error) => {
                    self.finish_ui_preferences_save(Err(error), false);
                    return;
                }
            }
        }
        // Storage runs off this thread; the result arrives through
        // `poll_ui_preferences`, and quit waits for it.
        if let Some(writer) = &self.preferences_file {
            writer.submit(json);
            self.preferences_submitted = true;
        }
        self.poll_ui_preferences();
    }

    fn finish_ui_preferences_save(&mut self, result: Result<(), String>, current: bool) {
        match result {
            Ok(()) => {
                if current {
                    self.ui_preferences_need_save = false;
                }
                self.ui_error.clear();
            }
            Err(error) => {
                self.ui_error = format!("Workspace preferences apply for this session, but could not be saved: {error}");
            }
        }
        self.update_warning();
    }

    /// Applies the outcome of a completed background save, if one finished.
    pub fn poll_ui_preferences(&mut self) {
        let Some(completion) = self.preferences_file.as_mut().and_then(crate::preferences::Writer::poll) else { return };
        let failed = completion.result.is_err();
        let current = completion.current && self.preferences_submitted;
        self.finish_ui_preferences_save(completion.result, current);
        if failed {
            // Group changes are part of this file: retry with the next save.
            self.visual_frames_need_save = true;
            if self.pending.is_empty() {
                self.save_status = "Workspace preferences need attention".into();
            }
        }
    }

    /// How long quit and other save boundaries wait for storage to answer.
    const PREFERENCES_FLUSH: Duration = Duration::from_secs(5);

    pub fn ensure_ui_preferences_saved(&mut self) -> Result<(), String> {
        self.poll_ui_preferences();
        if !self.ui_preferences_need_save {
            return Ok(());
        }
        // Nothing in flight means the last attempt failed or never started.
        if self.preferences_file.as_ref().is_none_or(crate::preferences::Writer::idle) {
            self.save_ui_preferences();
        }
        if let Some(writer) = &self.preferences_file
            && !writer.flush(Self::PREFERENCES_FLUSH)
        {
            return Err("Workspace preferences, including node groups, are still being written. Storage has not responded; try again in a moment.".into());
        }
        self.poll_ui_preferences();
        if self.ui_preferences_need_save {
            return Err(format!(
                "Workspace preferences, including node groups, could not be saved. Your changes remain in this session. {}",
                if self.has_settings_recovery() { "Finish workspace recovery before continuing." } else { self.ui_error.as_str() }
            ));
        }
        Ok(())
    }

    fn update_warning(&mut self) {
        let snapshot_warning = self.snapshot.as_ref().map(|s| s.warning.clone()).unwrap_or_default();
        self.warning = [&self.edit_error, &self.save_error, &self.startup_error, &self.preferences_notice, &self.ui_error, &snapshot_warning]
            .into_iter()
            .filter(|s| !s.trim().is_empty())
            .cloned()
            .collect::<Vec<_>>()
            .join("\n");
    }

    pub fn reduced_animation(&self) -> bool {
        self.reduced_animation
    }
    pub fn set_reduced_animation(&mut self, value: bool) {
        if self.applying || self.reduced_animation == value {
            return;
        }
        self.reduced_animation = value;
        self.save_ui_preferences();
    }
    pub fn show_action_notifications(&self) -> bool {
        self.show_action_notifications
    }
    pub fn set_show_action_notifications(&mut self, value: bool) {
        if self.applying || self.show_action_notifications == value {
            return;
        }
        self.show_action_notifications = value;
        self.save_ui_preferences();
    }
    pub fn right_click_drag_to_pan(&self) -> bool {
        self.right_click_drag_to_pan
    }
    pub fn set_right_click_drag_to_pan(&mut self, value: bool) {
        if self.applying || self.right_click_drag_to_pan == value {
            return;
        }
        self.right_click_drag_to_pan = value;
        self.save_ui_preferences();
    }
    pub fn show_minimap(&self) -> bool {
        self.show_minimap
    }
    pub fn set_show_minimap(&mut self, value: bool) {
        if self.applying || self.show_minimap == value {
            return;
        }
        self.show_minimap = value;
        self.save_ui_preferences();
    }
    pub fn automatically_check_for_updates(&self) -> bool {
        self.automatically_check_for_updates
    }
    pub fn set_automatically_check_for_updates(&mut self, value: bool) {
        if self.applying || self.automatically_check_for_updates == value {
            return;
        }
        self.automatically_check_for_updates = value;
        self.save_ui_preferences();
    }
    pub fn device_visibility(&self) -> crate::availability::Visibility {
        crate::availability::Visibility {
            disabled: self.show_disabled_audio_devices,
            disconnected: self.show_disconnected_audio_devices,
            advanced: self.show_advanced_audio_devices,
        }
    }
    /// List visibility is local: no configure request and no history entry.
    pub fn set_device_visibility(&mut self, visibility: crate::availability::Visibility) {
        if self.applying || self.device_visibility() == visibility {
            return;
        }
        self.show_disabled_audio_devices = visibility.disabled;
        self.show_disconnected_audio_devices = visibility.disconnected;
        self.show_advanced_audio_devices = visibility.advanced;
        self.save_ui_preferences();
        self.inventory_generation += 1;
    }

    // ----- status -------------------------------------------------------

    pub fn is_preview(&self) -> bool {
        self.preview
    }
    pub fn can_edit(&self) -> bool {
        self.connected && !self.restarting_engine && !self.preparing_update && !self.preparing_quit && !self.update_health_mode
    }
    pub(crate) fn set_connected(&mut self, value: bool) {
        self.connected = value;
    }
    pub fn is_connected(&self) -> bool {
        self.connected
    }
    pub fn can_edit_layout(&self) -> bool {
        self.can_edit() && !self.layout_locked
    }
    pub fn can_undo(&self) -> bool {
        self.can_edit() && self.history.can_undo() && (!self.layout_locked || !self.history.undo_changes_layout())
    }
    pub fn can_redo(&self) -> bool {
        self.can_edit() && self.history.can_redo() && (!self.layout_locked || !self.history.redo_changes_layout())
    }
    pub fn is_restarting_engine(&self) -> bool {
        self.restarting_engine
    }
    pub(crate) fn set_restarting(&mut self, value: bool) {
        self.restarting_engine = value;
    }
    pub fn is_preparing_quit(&self) -> bool {
        self.preparing_quit
    }
    pub fn is_preparing_update(&self) -> bool {
        self.preparing_update
    }
    pub(crate) fn set_preparing_update(&mut self, value: bool) {
        self.preparing_update = value;
    }
    pub fn is_update_health_mode(&self) -> bool {
        self.update_health_mode
    }
    pub fn set_update_health_mode(&mut self, value: bool) {
        self.update_health_mode = value;
    }
    pub fn can_quit_after_failed_update_health(&self) -> bool {
        self.update_health_mode && self.failed_update_health_quit_allowed
    }
    pub fn allow_quit_after_failed_update_health(&mut self) {
        self.failed_update_health_quit_allowed = true;
    }
    pub fn can_restart_engine(&self) -> bool {
        !self.restarting_engine
            && !self.preparing_update
            && !self.preparing_quit
            && !self.update_health_mode
            && !self.stopping
            && !self.has_settings_recovery()
            // A design preview has no restart operations (0.5's
            // `_restartOperations` is null there), so it never offers restart.
            && !self.preview
            && self.snapshot.is_some()
            && self.can_restart_supervisor
    }
    pub fn is_layout_locked(&self) -> bool {
        self.layout_locked
    }
    pub fn set_layout_locked(&mut self, value: bool) {
        self.layout_locked = value;
    }
    pub fn notify_layout_edit_blocked(&mut self) {
        if self.layout_locked {
            self.layout_edit_blocked += 1;
        }
    }
    pub fn pending_edit_count(&self) -> usize {
        self.pending.len()
    }
    pub fn has_pending_ui_preferences(&self) -> bool {
        self.ui_preferences_need_save
    }

    pub fn snapshot(&self) -> Option<&Snapshot> {
        self.snapshot.as_ref()
    }
    pub fn desired(&self) -> &Workspace {
        &self.desired
    }
    pub fn graph(&self) -> &AudioGraph {
        &self.desired.config.graph
    }
    pub fn frames(&self) -> &[Frame] {
        &self.desired.frames
    }
    pub fn endpoints(&self) -> &[Endpoint] {
        &self.endpoints
    }
    pub fn applications(&self) -> &[ApplicationInfo] {
        &self.applications
    }
    pub fn node_status(&self, id: &str) -> Option<&NodeStatus> {
        self.snapshot.as_ref()?.status.nodes.iter().find(|s| s.id == id)
    }
    pub fn acknowledged_node(&self, id: &str) -> Option<&GraphNode> {
        self.snapshot.as_ref()?.configuration.graph.nodes.iter().find(|n| n.id == id)
    }
    pub fn meter_audio_frames(&self) -> u64 {
        self.snapshot.as_ref().map_or(0, |s| s.status.audio_frames)
    }
    pub fn start_at_login(&self) -> bool {
        self.start_at_login
    }
    pub fn set_start_at_login(&mut self, value: bool) {
        if self.can_edit() && !self.applying && self.start_at_login != value {
            self.start_at_login = value;
            self.edit(move |w| w.config.start_at_login = value);
        }
    }
    pub fn settings_path(&self) -> String {
        if self.preview {
            "Preview · no settings file".into()
        } else {
            self.snapshot.as_ref().map_or_else(|| "Not connected".into(), |s| s.settings_path.clone())
        }
    }
    pub fn log_path(&self) -> String {
        if self.preview {
            "Preview · no engine log".into()
        } else {
            self.snapshot.as_ref().map_or_else(|| "Not connected".into(), |s| s.log_path.clone())
        }
    }

    // ----- recovery -----------------------------------------------------

    pub fn has_settings_recovery(&self) -> bool {
        self.snapshot.as_ref().is_some_and(|s| !s.is_settings_usable())
    }
    pub fn can_restore_backup(&self) -> bool {
        self.snapshot.as_ref().is_some_and(|s| s.settings_state == "backupAvailable")
    }
    pub fn can_start_fresh(&self) -> bool {
        self.has_settings_recovery() && self.snapshot.as_ref().is_some_and(|s| s.settings_state != "unsupported")
    }
    pub fn recovery_description(&self) -> String {
        let Some(snapshot) = &self.snapshot else { return String::new() };
        if snapshot.settings_state == "unsupported" {
            "This workspace needs a newer version of VineMix. Your files have been kept intact.".into()
        } else if self.can_restore_backup() {
            format!(
                "A backup is available, but it may predate recent edits. Audio and saving are paused. Backup: {}",
                snapshot.backup_path
            )
        } else {
            "Your saved workspace could not be opened. Retry after repairing the files, or start fresh. Originals will be preserved.".into()
        }
    }

    /// The configuration to preserve before a destructive recovery choice.
    pub(crate) fn recovery_preservation(&self) -> Option<Configuration> {
        (!self.pending.is_empty()).then(|| self.recovery_pending.as_ref().unwrap_or(&self.desired).config.clone())
    }

    pub(crate) fn finish_recovery(&mut self, command: &str, snapshot: Snapshot) -> Result<(), String> {
        if matches!(command, "restoreBackup" | "startFresh") && snapshot.is_settings_usable() {
            self.pending.clear();
            self.canvas_view = None;
            self.history.clear();
            self.recovery_pending = None;
        }
        self.needs_initial_frame = command == "startFresh";
        self.accept(snapshot, true)
    }

    pub(crate) fn set_edit_error(&mut self, error: String) {
        self.edit_error = error;
        self.update_warning();
    }

    // ----- edits --------------------------------------------------------

    /// An unchecked edit: always applies.
    pub fn edit(&mut self, change: impl Fn(&mut Workspace) + Send + 'static) -> bool {
        self.apply_edit(Box::new(move |w| {
            change(w);
            Ok(())
        }), false)
        .is_ok()
    }

    /// A checked edit that may refuse; nothing is published on failure.
    pub fn try_edit(&mut self, change: impl Fn(&mut Workspace) -> Result<(), String> + Send + 'static) -> Result<(), String> {
        self.apply_edit(Box::new(change), true)
    }

    fn apply_edit(&mut self, operation: Box<EditFn>, checked: bool) -> Result<(), String> {
        if self.applying || !self.can_edit() {
            return Err("Wait for the audio engine before changing the workspace.".into());
        }
        let _ = checked;
        let before = self.desired.clone();
        let mut candidate = self.desired.clone();
        operation(&mut candidate)?;
        candidate.normalize_frames();
        if self.layout_locked && history::changes_layout(&before.graph_state(), &candidate.graph_state()) {
            self.notify_layout_edit_blocked();
            return Err(LAYOUT_LOCKED_MESSAGE.into());
        }
        self.desired = candidate;
        let frames_changed = before.frames != self.desired.frames;
        if before.config == self.desired.config {
            if !frames_changed {
                return Ok(());
            }
            self.history.record(&before.graph_state(), &self.desired.graph_state());
            self.save_visual_frames();
            self.apply_desired(true);
            return Ok(());
        }
        let mut manual_policies: Vec<(String, bool)> = Vec::new();
        for node in self.desired.config.graph.nodes.iter_mut().filter(|n| bindings::is_virtual(&n.kind)) {
            let Some(previous) = before.node(&node.id) else { continue };
            if previous.device == node.device {
                continue;
            }
            node.auto_assign_device = node.device.assigned();
            manual_policies.push((node.id.clone(), node.auto_assign_device));
        }
        let assignments = self.automatic_assignments();
        bindings::apply(&mut self.desired.config.graph, &assignments);
        for assignment in &assignments {
            self.automatic_assignment_attempts.insert(assignment.attempt_key());
        }
        if before.config == self.desired.config && !frames_changed {
            return Ok(());
        }
        if frames_changed {
            self.save_visual_frames();
        }
        self.history.record(&before.graph_state(), &self.desired.graph_state());
        self.consider_processing_load_edit(&before.config.graph, &self.desired.config.graph.clone());
        if self.preview && !self.queue_preview_edits {
            self.save_status = "Preview · changes stay in this window".into();
            self.apply_desired(true);
            return Ok(());
        }
        let view = self.canvas_view;
        let complete = move |w: &mut Workspace| {
            for node in &mut w.config.graph.nodes {
                if let Some((_, automatic)) = manual_policies.iter().find(|(id, _)| *id == node.id) {
                    node.auto_assign_device = *automatic;
                }
            }
            bindings::apply(&mut w.config.graph, &assignments);
            if let Some((x, y, zoom)) = view {
                w.config.graph.pan_x = x;
                w.config.graph.pan_y = y;
                w.config.graph.zoom = zoom;
            }
        };
        let (frames_before, frames_after) =
            if frames_changed { (Some(before.frames.clone()), Some(self.desired.frames.clone())) } else { (None, None) };
        self.push_pending(
            Box::new(move |w| {
                operation(w)?;
                complete(w);
                Ok(())
            }),
            false,
            frames_before,
            frames_after,
        );
        self.edit_error.clear();
        self.save_status = self.pending_save_status();
        self.update_warning();
        self.apply_desired(true);
        Ok(())
    }

    fn push_pending(&mut self, apply: Box<EditFn>, is_restore: bool, frames_before: Option<Vec<Frame>>, frames_after: Option<Vec<Frame>>) {
        let id = self.next_edit_id;
        self.next_edit_id += 1;
        self.pending.push(PendingEdit { id, apply, is_restore, frames_before, frames_after });
        let now = self.now();
        self.cadence.queue(now);
    }

    fn pending_save_status(&self) -> String {
        if self.save_error.is_empty() { "Saving changes…".into() } else { "Changes not saved · retrying…".into() }
    }

    fn automatic_assignments(&self) -> Vec<bindings::Assignment> {
        bindings::find_automatic_assignments(&self.desired.config.graph, &self.endpoints)
            .into_iter()
            .filter(|a| !self.automatic_assignment_suppressed.contains(&a.node_id))
            .collect()
    }

    pub fn begin_edit_gesture(&mut self, description: Option<&str>) {
        if !self.applying && self.can_edit() {
            self.history.begin_gesture(description);
        }
    }

    pub fn end_edit_gesture(&mut self) {
        self.history.end_gesture();
    }

    pub fn undo(&mut self) -> bool {
        self.restore_history(true)
    }

    pub fn redo(&mut self) -> bool {
        self.restore_history(false)
    }

    fn restore_history(&mut self, undo: bool) -> bool {
        if self.applying || !self.can_edit() {
            return false;
        }
        if self.layout_locked && (if undo { self.history.undo_changes_layout() } else { self.history.redo_changes_layout() }) {
            self.notify_layout_edit_blocked();
            return false;
        }
        let current = self.desired.graph_state();
        let moved = if undo { self.history.try_undo(&current) } else { self.history.try_redo(&current) };
        let (restored, restore) = match moved {
            Ok(Some(result)) => result,
            Ok(None) => return false,
            Err(error) => {
                self.reset_history_after_conflict(&error);
                return false;
            }
        };
        let previous = current;
        self.desired.set_graph_state(restored.clone());
        if previous.frames != restored.frames {
            self.save_visual_frames();
        }
        if previous.graph == restored.graph && previous.frames == restored.frames {
            self.edit_error.clear();
            self.update_warning();
            self.apply_desired(true);
            return true;
        }
        // An explicit restoration of an unassigned node is authoritative for
        // this session, even if inventory changes would otherwise reassign it.
        let mut suppress: Vec<String> = Vec::new();
        let mut release: Vec<String> = Vec::new();
        for node in self.desired.config.graph.nodes.iter_mut().filter(|n| bindings::is_virtual(&n.kind)) {
            if previous.graph.nodes.iter().find(|o| o.id == node.id).is_some_and(|p| p.device == node.device) {
                continue;
            }
            if node.device.assigned() {
                release.push(node.id.clone());
            } else {
                suppress.push(node.id.clone());
                node.auto_assign_device = false;
            }
        }
        for id in release {
            self.automatic_assignment_suppressed.remove(&id);
        }
        self.automatic_assignment_suppressed.extend(suppress);
        self.edit_error.clear();
        if self.preview && !self.queue_preview_edits {
            self.save_status = "Preview · changes stay in this window".into();
        } else {
            let policies: HashSet<String> =
                self.desired.config.graph.nodes.iter().filter(|n| !n.auto_assign_device).map(|n| n.id.clone()).collect();
            let frames_before = Some(previous.frames.clone());
            let frames_after = Some(restored.frames.clone());
            self.push_pending(
                Box::new(move |w: &mut Workspace| {
                    // Frame metadata already committed locally. The engine
                    // replay restores audio and layout data only.
                    let mut graph = restore.try_apply(&w.graph_state(), false)?.graph;
                    for node in &mut graph.nodes {
                        if policies.contains(&node.id) {
                            node.auto_assign_device = false;
                        }
                    }
                    w.config.graph = graph;
                    Ok(())
                }),
                true,
                frames_before,
                frames_after,
            );
            self.save_status = self.pending_save_status();
        }
        self.update_warning();
        self.apply_desired(true);
        true
    }

    fn reset_history_after_conflict(&mut self, detail: &str) {
        self.history.clear();
        self.edit_error = format!("Undo and redo were reset because the workspace changed. {detail}");
        self.update_warning();
    }

    fn replay_pending(&mut self, mut workspace: Workspace) -> (Workspace, bool) {
        let mut conflict = false;
        let mut dropped: Vec<PendingEdit> = Vec::new();
        let mut kept: Vec<PendingEdit> = Vec::with_capacity(self.pending.len());
        for edit in std::mem::take(&mut self.pending) {
            if conflict && edit.is_restore {
                dropped.push(edit);
                continue;
            }
            let mut candidate = workspace.clone();
            match (edit.apply)(&mut candidate) {
                Ok(()) => {
                    workspace = candidate;
                    kept.push(edit);
                }
                Err(error) => {
                    // A refused operation must not leave later dependent
                    // history restores queued. Ordinary newer edits rebase.
                    dropped.push(edit);
                    conflict = true;
                    self.reset_history_after_conflict(&error);
                }
            }
        }
        self.pending = kept;
        if conflict {
            self.cadence.complete(!self.pending.is_empty());
            let restored = self.restore_rejected_visual_frames(&dropped);
            self.visual_frames_need_save |= restored;
        }
        (workspace, conflict)
    }

    /// The coalesced configuration for the next configure request.
    pub fn prepare_pending_configuration(&mut self, basis: &Configuration) -> PendingBatch {
        self.reconcile_unconfirmed_update(basis);
        self.reconcile_unconfirmed_quit(basis);
        let (mut workspace, conflict) = self.replay_pending(Workspace::from_config(basis.clone()));
        if let Some((x, y, zoom)) = self.canvas_view {
            workspace.config.graph.pan_x = x;
            workspace.config.graph.pan_y = y;
            workspace.config.graph.zoom = zoom;
        }
        if conflict {
            workspace.frames = frames::normalize_for(&self.visual_frames, &workspace.config.graph.nodes);
            self.desired = workspace.clone();
            if self.visual_frames_need_save || self.visual_frames != self.desired.frames {
                self.save_visual_frames();
            }
            self.apply_desired(true);
        }
        PendingBatch { configuration: workspace.config, edits: self.pending.iter().map(|e| e.id).collect() }
    }

    pub fn is_pending_save_due(&self) -> bool {
        !self.pending.is_empty() && self.cadence.is_due(self.now())
    }

    pub fn begin_dispatch(&mut self) {
        let now = self.now();
        self.cadence.begin_dispatch(now);
    }

    pub fn handle_configure_response(&mut self, batch: &PendingBatch, basis: &Snapshot, response: Response) -> Result<(), String> {
        let now = self.now();
        if response.ok {
            return self.acknowledge_pending(batch, response.snapshot);
        }
        if response.error_code == "settings_write_failed" {
            // Validated but not persisted: keep the exact queue and history.
            self.save_error = format!("Changes are not saved. VineMix will keep retrying while it is running. {}", response.error);
            self.cadence.defer_retry(now, Some(Duration::from_secs(1)));
            self.save_status = self.pending_save_status();
            self.update_warning();
        } else if crate::graph::reasons::is_revision_conflict(&response.error_code, &response.error) {
            self.cadence.defer_retry(now, None);
            self.save_status = if self.save_error.is_empty() { "Reconciling changes…".into() } else { self.pending_save_status() };
        } else {
            return self.reject_pending(batch, basis.clone(), &response.error);
        }
        Ok(())
    }

    pub(crate) fn acknowledge_pending(&mut self, batch: &PendingBatch, snapshot: Option<Snapshot>) -> Result<(), String> {
        // Identities, not a prefix count: newer edits may have arrived.
        self.pending.retain(|e| !batch.edits.contains(&e.id));
        self.cadence.complete(!self.pending.is_empty());
        self.save_error.clear();
        if !self.pending.is_empty() {
            self.save_status = self.pending_save_status();
        }
        self.update_warning();
        if let Some(snapshot) = snapshot {
            self.accept(snapshot, true)?;
        }
        Ok(())
    }

    fn reject_pending(&mut self, batch: &PendingBatch, snapshot: Snapshot, error: &str) -> Result<(), String> {
        let (rejected, kept): (Vec<PendingEdit>, Vec<PendingEdit>) =
            std::mem::take(&mut self.pending).into_iter().partition(|e| batch.edits.contains(&e.id) || e.is_restore);
        let frames_restored = self.restore_rejected_visual_frames(&rejected);
        self.pending = kept;
        self.cadence.complete(!self.pending.is_empty());
        self.save_error.clear();
        self.history.clear();
        self.edit_error =
            format!("The engine rejected a change: {error} Undo and redo were reset to keep the saved workspace consistent.");
        if !self.pending.is_empty() {
            self.save_status = self.pending_save_status();
        }
        self.accept(snapshot, true)?;
        if frames_restored && !self.has_settings_recovery() {
            self.save_visual_frames();
        }
        Ok(())
    }

    /// Navigation never queues a configure or creates an edit; the next
    /// real edit carries the latest view in its one save.
    pub fn set_canvas_view(&mut self, x: f64, y: f64, zoom: f64) {
        self.canvas_view = Some((x, y, zoom));
        self.desired.config.graph.pan_x = x;
        self.desired.config.graph.pan_y = y;
        self.desired.config.graph.zoom = zoom;
    }

    fn apply_desired(&mut self, configuration_changed: bool) {
        if self.snapshot.is_none() {
            return;
        }
        if configuration_changed {
            self.configuration_presentation_count += 1;
            self.applying = true;
            self.start_at_login = self.desired.config.start_at_login;
            self.applying = false;
        }
    }

    fn requires_configuration_refresh(&self, next: &Snapshot, force: bool) -> bool {
        let Some(previous) = &self.snapshot else { return true };
        self.preview
            || force
            || !self.can_edit()
            || previous.configuration.revision != next.configuration.revision
            || next.status.audio_frames < previous.status.audio_frames
            || !crate::text::eq_ignore_case(&previous.settings_path, &next.settings_path)
            || previous.configuration.start_at_login != next.configuration.start_at_login
    }

    /// Applies an engine snapshot (`Accept`).
    pub fn accept(&mut self, mut snapshot: Snapshot, force_configuration: bool) -> Result<(), String> {
        if snapshot.configuration.version == 1 {
            if !self.preview {
                return Err("The running engine needs the node-workspace update. Quit VineMix and reopen the new version.".into());
            }
            snapshot.configuration.graph = crate::graph::from_legacy(&snapshot.configuration);
            snapshot.configuration.version = 2;
        }
        self.reconcile_unconfirmed_update(&snapshot.configuration);
        self.reconcile_unconfirmed_quit(&snapshot.configuration);
        let initial = self.snapshot.is_none();
        let recovery = !snapshot.is_settings_usable();
        if recovery && !self.has_settings_recovery() && !self.pending.is_empty() {
            self.recovery_pending = Some(self.desired.clone());
        }
        if initial && !self.preview && snapshot.settings_state == "new" {
            self.needs_initial_frame = true;
        }
        let mut configuration_changed = self.requires_configuration_refresh(&snapshot, force_configuration);
        if configuration_changed {
            // Rebase local edits only when the committed base changes.
            let mut desired = if recovery {
                Workspace::from_config(snapshot.configuration.clone())
            } else {
                self.replay_pending(Workspace::from_config(snapshot.configuration.clone())).0
            };
            desired.frames = frames::normalize_for(&self.visual_frames, &desired.config.graph.nodes);
            if !recovery {
                if let Some((x, y, zoom)) = self.canvas_view {
                    desired.config.graph.pan_x = x;
                    desired.config.graph.pan_y = y;
                    desired.config.graph.zoom = zoom;
                }
            }
            self.desired = desired;
            self.configuration_refresh_count += 1;
        } else {
            self.telemetry_only_snapshot_count += 1;
        }
        self.meter_sample_sequence += 1;
        let inventory_changed = self.endpoints != snapshot.endpoints;
        let applications_changed = !same_applications(&self.applications, &snapshot.applications);
        let audio_frames = snapshot.status.audio_frames;
        let revision = snapshot.configuration.revision;
        let registered = snapshot.configuration.start_at_login;
        if inventory_changed {
            self.endpoints = snapshot.endpoints.clone();
            self.automatic_assignment_attempts.clear();
            self.inventory_generation += 1;
        }
        if applications_changed {
            self.applications = snapshot.applications.clone();
            self.inventory_generation += 1;
        }
        self.snapshot = Some(snapshot);
        self.connected = !self.has_settings_recovery();
        self.refresh_processing_load(None);
        self.poll_ui_preferences();
        if configuration_changed && !recovery && (self.visual_frames_need_save || self.visual_frames != self.desired.frames) {
            self.save_visual_frames();
        }
        self.connection_status = if self.preview { "Preview · no audio engine".into() } else { "Audio engine connected".into() };
        self.frame_label = format!("{} frames · 48 kHz", crate::text::thousands(audio_frames));
        if self.pending.is_empty() {
            self.save_error.clear();
            self.save_status = if self.preview {
                "Preview · no settings are saved".into()
            } else if !self.ui_error.is_empty() {
                "Workspace preferences need attention".into()
            } else if self.edit_error.is_empty() {
                format!("Saved · revision {revision}")
            } else {
                "Change was not saved".into()
            };
        }
        if !self.has_settings_recovery()
            && !self.preparing_update
            && !self.preparing_quit
            && !self.update_health_mode
            && (configuration_changed || inventory_changed)
        {
            let assignments: Vec<_> = self
                .automatic_assignments()
                .into_iter()
                .filter(|a| self.preview || !self.automatic_assignment_attempts.contains(&a.attempt_key()))
                .collect();
            if !assignments.is_empty() {
                bindings::apply(&mut self.desired.config.graph, &assignments);
                for assignment in &assignments {
                    self.automatic_assignment_attempts.insert(assignment.attempt_key());
                }
                if !self.preview || self.queue_preview_edits {
                    let replay = assignments.clone();
                    self.push_pending(
                        Box::new(move |w| {
                            bindings::apply(&mut w.config.graph, &replay);
                            Ok(())
                        }),
                        false,
                        None,
                        None,
                    );
                }
                self.save_status = if self.preview && !self.queue_preview_edits {
                    "Preview · changes stay in this window".into()
                } else {
                    self.pending_save_status()
                };
                configuration_changed = true;
            }
        }
        if !self.has_settings_recovery() && configuration_changed && !self.history.is_compatible(&self.desired.graph_state()) {
            self.reset_history_after_conflict("Another change affected a node or connection in the edit history.");
        }
        self.update_warning();
        let _ = initial;
        self.apply_desired(configuration_changed);
        if self.has_settings_recovery() {
            self.save_status = "Recovery required · nothing is being saved".into();
        }
        if !self.preview && self.can_edit() && self.registered_startup != Some(registered) {
            match crate::platform::startup::set_enabled(registered) {
                Ok(()) => self.startup_error.clear(),
                Err(error) => {
                    self.startup_error = format!("Could not update start at login: {error}");
                    self.update_warning();
                }
            }
            self.registered_startup = Some(registered);
        }
        Ok(())
    }

    /// The connection failed: show it and wait for reconnection.
    pub(crate) fn connection_lost(&mut self, status: String, warning: String) {
        self.connected = false;
        self.connection_status = status;
        self.frame_label = "Engine offline".into();
        self.save_status = if self.pending.is_empty() { "Waiting for connection".into() } else { "Changes queued until reconnection".into() };
        self.warning = warning;
    }

    // ----- strict replay (restart, quit, update) -------------------------

    pub(crate) fn prepare_restart_configuration(&mut self, basis: &Configuration) -> Result<PendingBatch, String> {
        self.reconcile_unconfirmed_update(basis);
        self.reconcile_unconfirmed_quit(basis);
        let mut workspace = Workspace::from_config(basis.clone());
        for edit in &self.pending {
            let mut candidate = workspace.clone();
            (edit.apply)(&mut candidate).map_err(|e| format!("Queued changes could not be reapplied: {e}"))?;
            workspace = candidate;
        }
        if let Some((x, y, zoom)) = self.canvas_view {
            workspace.config.graph.pan_x = x;
            workspace.config.graph.pan_y = y;
            workspace.config.graph.zoom = zoom;
        }
        Ok(PendingBatch { configuration: workspace.config, edits: self.pending.iter().map(|e| e.id).collect() })
    }

    pub(crate) fn reconcile_unconfirmed_quit(&mut self, saved: &Configuration) {
        let Some(batch) = self.unconfirmed_quit_batch.as_ref() else { return };
        if !crate::protocol::same_saved_configuration(&batch.configuration, saved) {
            return;
        }
        let batch = self.unconfirmed_quit_batch.take().unwrap_or_else(|| unreachable!());
        let _ = self.acknowledge_pending(&batch, None);
    }

    fn reconcile_unconfirmed_update(&mut self, saved: &Configuration) {
        let Some(batch) = self.unconfirmed_update_batch.as_ref() else { return };
        if !crate::protocol::same_saved_configuration(&batch.configuration, saved) {
            return;
        }
        let batch = self.unconfirmed_update_batch.take().unwrap_or_else(|| unreachable!());
        let _ = self.acknowledge_pending(&batch, None);
    }

    pub(crate) fn set_unconfirmed_quit_batch(&mut self, batch: Option<PendingBatch>) {
        self.unconfirmed_quit_batch = batch;
    }
    pub(crate) fn set_unconfirmed_update_batch(&mut self, batch: Option<PendingBatch>) {
        self.unconfirmed_update_batch = batch;
    }
    pub(crate) fn unconfirmed_update_batch(&self) -> Option<&PendingBatch> {
        self.unconfirmed_update_batch.as_ref()
    }
    pub(crate) fn take_unconfirmed_update_batch(&mut self) -> Option<PendingBatch> {
        self.unconfirmed_update_batch.take()
    }
    pub(crate) fn set_save_error(&mut self, error: String) {
        self.save_error = error;
        self.update_warning();
    }

    // ----- quit ---------------------------------------------------------

    pub(crate) fn begin_quit_preparation(&mut self) {
        self.preparing_quit = true;
    }

    pub(crate) fn remember_quit_recovery(&mut self, configuration: Option<Configuration>) {
        match configuration {
            Some(configuration) => {
                let frames = self.desired.frames.clone();
                self.quit_recovery_configuration = Some(Workspace { config: configuration, frames });
            }
            None => {
                if self.quit_recovery_configuration.is_none() {
                    self.quit_recovery_configuration = Some(self.recovery_pending.clone().unwrap_or_else(|| self.desired.clone()));
                }
            }
        }
    }

    pub fn cancel_quit_preparation(&mut self) {
        if self.stopping {
            return;
        }
        self.preparing_quit = false;
        self.quit_recovery_configuration = None;
        self.save_status = if !self.pending.is_empty() {
            self.pending_save_status()
        } else if self.ui_preferences_need_save {
            "Workspace preferences need attention".into()
        } else if self.preview {
            "Preview · no settings are saved".into()
        } else {
            format!("Saved · revision {}", self.snapshot.as_ref().map_or(0, |s| s.configuration.revision))
        };
        self.connection_status = if self.pending.is_empty() {
            "Quit cancelled · audio session kept open".into()
        } else {
            "Quit cancelled · your changes are still queued".into()
        };
    }

    /// The workspace JSON a quit recovery copy contains.
    pub fn serialize_quit_recovery(&mut self) -> Result<String, String> {
        if !self.preparing_quit {
            return Err("Prepare the pending workspace before making a quit recovery copy.".into());
        }
        self.ensure_ui_preferences_saved()?;
        let mut configuration = self
            .quit_recovery_configuration
            .clone()
            .or_else(|| self.recovery_pending.clone())
            .unwrap_or_else(|| self.desired.clone())
            .config;
        if let Some((x, y, zoom)) = self.canvas_view {
            configuration.graph.pan_x = x;
            configuration.graph.pan_y = y;
            configuration.graph.zoom = zoom;
        }
        Ok(vinemix_model::json::dump(&configuration.to_json()))
    }

    /// Writes a recovery copy without ever replacing an existing file or
    /// leaving a valid-looking partial file.
    pub fn write_quit_recovery_copy(&mut self, path: &std::path::Path) -> Result<(), String> {
        let json = self.serialize_quit_recovery()?;
        let path = crate::paths::absolute(path);
        let directory = path.parent().ok_or("Choose a folder for the recovery copy.")?;
        let temporary = directory.join(format!(".vinemix-recovery-{}.tmp", crate::platform::random_hex_id()));
        let result = (|| -> Result<(), String> {
            vinemix_platform::create_new_durable(&temporary, json.as_bytes()).map_err(|()| "The recovery copy could not be written.".to_owned())?;
            if path.exists() {
                return Err("A file with that name already exists.".into());
            }
            std::fs::rename(&temporary, &path).map_err(|e| e.to_string())
        })();
        let _ = std::fs::remove_file(&temporary);
        result
    }

    // ----- groups -------------------------------------------------------

    fn save_visual_frames(&mut self) {
        self.visual_frames = self.desired.frames.clone();
        self.save_ui_preferences();
        self.visual_frames_need_save = !self.preview && (self.has_settings_recovery() || !self.ui_error.is_empty());
        if self.pending.is_empty() {
            self.save_status = if self.preview {
                "Preview · changes stay in this window".into()
            } else if !self.ui_error.is_empty() {
                "Group changes are not saved".into()
            } else if !self.edit_error.is_empty() {
                "Change was not saved".into()
            } else if let Some(snapshot) = &self.snapshot {
                format!("Saved · revision {}", snapshot.configuration.revision)
            } else {
                "Saved".into()
            };
        }
    }

    /// Restores group fields that still match a rejected edit's result;
    /// later explicit group edits keep precedence.
    fn restore_rejected_visual_frames(&mut self, rejected: &[PendingEdit]) -> bool {
        let before_rollback = self.visual_frames.clone();
        for edit in rejected.iter().rev() {
            let (Some(before), Some(after)) = (&edit.frames_before, &edit.frames_after) else { continue };
            let mut ids: Vec<&str> = before.iter().map(|f| f.id.as_str()).collect();
            for frame in after {
                if !ids.contains(&frame.id.as_str()) {
                    ids.push(&frame.id);
                }
            }
            for id in ids {
                let original = before.iter().find(|f| f.id == id);
                let applied = after.iter().find(|f| f.id == id);
                if original == applied {
                    continue;
                }
                let current = self.visual_frames.iter().position(|f| f.id == id);
                let Some(index) = current else {
                    if let (None, Some(original)) = (applied, original) {
                        self.visual_frames.push(original.clone());
                    }
                    continue;
                };
                let Some(original) = original else {
                    if Some(&self.visual_frames[index]) == applied {
                        self.visual_frames.remove(index);
                    }
                    continue;
                };
                let Some(applied) = applied else { continue };
                let current = &mut self.visual_frames[index];
                if current.name == applied.name {
                    current.name = original.name.clone();
                }
                if current.color_key == applied.color_key {
                    current.color_key = original.color_key.clone();
                }
                if current.node_ids == applied.node_ids {
                    current.node_ids = original.node_ids.clone();
                }
            }
        }
        before_rollback != self.visual_frames
    }

    /// Preview only: replace the state with supplied design data.
    pub fn apply_preview_snapshot(&mut self, snapshot: Snapshot, frames: Vec<Frame>) -> Result<(), String> {
        if !self.preview {
            return Err("Design data can only be applied to a preview model.".into());
        }
        if !frames.is_empty() {
            self.visual_frames = frames::normalize(&frames, None);
        }
        if !self.queue_preview_edits {
            self.history.clear();
        }
        self.canvas_view = None;
        self.accept(snapshot, false)
    }

    pub fn refresh_preview_telemetry(&mut self) {
        if self.preview && self.can_edit() && self.snapshot.is_some() {
            self.meter_sample_sequence += 1;
        }
    }

    pub fn restart_supervisor_available(&mut self, available: bool) {
        self.can_restart_supervisor = available;
    }

    pub fn status_nodes(&self) -> &[NodeStatus] {
        self.snapshot.as_ref().map_or(&[], |s| s.status.nodes.as_slice())
    }
}

fn same_applications(left: &[ApplicationInfo], right: &[ApplicationInfo]) -> bool {
    left.len() == right.len()
        && left
            .iter()
            .zip(right)
            .all(|(a, b)| a.process_id == b.process_id && a.name == b.name && a.executable_path == b.executable_path)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn snapshot(revision: u64) -> Snapshot {
        let mut configuration = Configuration { version: 2, revision, ..Default::default() };
        configuration.graph = crate::graph::fresh();
        Snapshot {
            settings_state: "loaded".into(),
            configuration,
            status: vinemix_model::status::EngineStatus { audio_frames: 1000 + revision, ..Default::default() },
            settings_path: "x".into(),
            ..Default::default()
        }
    }

    fn connected() -> MixerState {
        let mut state = MixerState::new(true);
        state.queue_preview_edits = true;
        state.accept(snapshot(1), false).unwrap();
        state
    }

    #[test]
    fn edits_queue_replay_and_acknowledge() {
        let mut state = connected();
        assert!(state.can_edit());
        assert!(state.edit(|w| w.config.graph.nodes[1].gain_db = 3.0));
        assert_eq!(state.pending_edit_count(), 1);
        assert_eq!(state.graph().nodes[1].gain_db, 3.0);
        // A telemetry snapshot at the same revision does not rebase.
        state.accept(snapshot(1), false).unwrap();
        assert_eq!(state.graph().nodes[1].gain_db, 3.0);
        // A newer revision from elsewhere is rebased under the pending edit.
        let mut newer = snapshot(2);
        newer.configuration.graph.nodes[2].gain_db = -6.0;
        state.accept(newer.clone(), false).unwrap();
        assert_eq!(state.graph().nodes[1].gain_db, 3.0);
        assert_eq!(state.graph().nodes[2].gain_db, -6.0);
        let batch = state.prepare_pending_configuration(&newer.configuration);
        assert_eq!(batch.configuration.graph.nodes[1].gain_db, 3.0);
        let mut saved = newer.clone();
        saved.configuration = batch.configuration.clone();
        saved.configuration.revision = 3;
        state.acknowledge_pending(&batch, Some(saved)).unwrap();
        assert_eq!(state.pending_edit_count(), 0);
        assert_eq!(state.save_status, "Preview · no settings are saved");
        assert_eq!(state.snapshot().unwrap().configuration.revision, 3);
        assert!(state.can_undo());
    }

    #[test]
    fn rejection_resets_history_and_keeps_newer_edits() {
        let mut state = connected();
        state.edit(|w| w.config.graph.nodes[1].gain_db = 3.0);
        let basis = state.snapshot().unwrap().clone();
        let batch = state.prepare_pending_configuration(&basis.configuration);
        state.edit(|w| w.config.graph.nodes[2].gain_db = 2.0);
        let response = Response { ok: false, error: "Nope.".into(), ..Default::default() };
        state.handle_configure_response(&batch, &basis, response).unwrap();
        assert_eq!(state.pending_edit_count(), 1);
        assert_eq!(state.graph().nodes[1].gain_db, 0.0);
        assert_eq!(state.graph().nodes[2].gain_db, 2.0);
        assert!(!state.can_undo());
        assert!(state.warning.starts_with("The engine rejected a change: Nope."));
    }

    #[test]
    fn layout_lock_refuses_structural_edits() {
        let mut state = connected();
        state.set_layout_locked(true);
        let error = state.try_edit(|w| {
            w.config.graph.nodes[0].x += 10.0;
            Ok(())
        });
        assert_eq!(error.unwrap_err(), LAYOUT_LOCKED_MESSAGE);
        assert_eq!(state.layout_edit_blocked, 1);
        assert!(state.edit(|w| w.config.graph.nodes[0].gain_db = -3.0));
    }

    #[test]
    fn undo_queues_a_guarded_restore() {
        let mut state = connected();
        state.edit(|w| w.config.graph.nodes[1].gain_db = 3.0);
        let basis = state.snapshot().unwrap().clone();
        let batch = state.prepare_pending_configuration(&basis.configuration);
        let mut saved = basis.clone();
        saved.configuration = batch.configuration.clone();
        saved.configuration.revision = 2;
        state.acknowledge_pending(&batch, Some(saved.clone())).unwrap();
        assert!(state.undo());
        assert_eq!(state.graph().nodes[1].gain_db, 0.0);
        let batch = state.prepare_pending_configuration(&saved.configuration);
        assert_eq!(batch.configuration.graph.nodes[1].gain_db, 0.0);
        assert!(state.can_redo());
    }

    #[test]
    fn preferences_round_trip() {
        let mut state = MixerState::new(true);
        state
            .load_ui_preferences_json(
                br#"{"rightClickDragToPan":true,"showMinimap":false,"frames":[null,{"id":"f","name":"G","colorKey":"rose","nodeIds":["a"]}]}"#,
            )
            .unwrap();
        assert!(state.right_click_drag_to_pan());
        assert!(!state.show_minimap());
        let json = state.serialize_ui_preferences();
        assert!(json.contains(r#""colorKey":"rose""#));
        // Automatic update checks are opt-in: a file without the setting reads
        // as off, and an explicit choice is kept.
        assert!(json.contains(r#""automaticallyCheckForUpdates":false"#));
        let mut chosen = MixerState::new(true);
        chosen.load_ui_preferences_json(br#"{"automaticallyCheckForUpdates":true}"#).unwrap();
        assert!(chosen.automatically_check_for_updates());
    }

    /// RUST-REV-010 / RUST-REV-011: the window asks only for what it shows.
    #[test]
    fn interest_follows_visibility_and_drawn_spectrum_plots() {
        let mut state = connected();
        state.visible = true;
        let has_app_node = state.graph().nodes.iter().any(|n| n.kind == "appInput");
        assert_eq!(state.interest(), serde_json::json!({"spectrum": [], "applications": has_app_node}), "no EQ plot is on screen");
        state.note_spectrum_viewers(["eq-b".to_owned(), "eq-a".to_owned()]);
        assert_eq!(state.interest()["spectrum"], serde_json::json!(["eq-a", "eq-b"]));
        // The application list is wanted exactly while an app-capture node
        // exists in a visible window.
        if !has_app_node {
            assert!(state.edit(|w| w.config.graph.nodes.push(crate::graph::new_node("appInput", 0.0, 0.0))));
        }
        assert_eq!(state.interest()["applications"], true);
        assert!(state.edit(|w| w.config.graph.nodes.retain(|n| n.kind != "appInput")));
        assert_eq!(state.interest()["applications"], false, "no app-capture node, no list");
        assert!(state.edit(|w| w.config.graph.nodes.push(crate::graph::new_node("appInput", 0.0, 0.0))));
        // Hidden: nothing is drawn, so nothing is analysed or enumerated.
        state.visible = false;
        assert_eq!(state.interest(), serde_json::json!({"spectrum": [], "applications": false}));
        // Shown again while the plots are still recent.
        state.visible = true;
        assert_eq!(state.interest()["spectrum"].as_array().unwrap().len(), 2);
        assert_eq!(state.interest()["applications"], true);
        // A plot that is no longer drawn stops being requested.
        std::thread::sleep(std::time::Duration::from_millis(600));
        state.note_spectrum_viewers(["eq-a".to_owned()]);
        assert_eq!(state.interest()["spectrum"], serde_json::json!(["eq-a"]));
    }

    // ----- RUST-REV-003 / RUST-REV-014: damaged preferences ----------------

    const EXAMPLE_GROUP: &str =
        r#"{"showMinimap":true,"frames":[{"id":"example-group","name":"Example group","colorKey":"rose","nodeIds":["mic1"]}]}"#;

    fn preferences_directory(name: &str) -> std::path::PathBuf {
        let path = std::env::temp_dir().join(format!("vinemix-ui-preferences-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&path);
        std::fs::create_dir_all(&path).unwrap();
        path
    }

    fn preserved_copies(directory: &std::path::Path) -> Vec<std::path::PathBuf> {
        let mut copies: Vec<_> = std::fs::read_dir(directory)
            .unwrap()
            .map(|entry| entry.unwrap().path())
            .filter(|path| path.file_name().is_some_and(|n| n.to_string_lossy().starts_with("ui.json.damaged-")))
            .collect();
        copies.sort();
        copies
    }

    /// The review's reproduction: a damaged `ui.json` holding a group, then a
    /// minimap toggle. The original bytes must survive beside the new file.
    #[test]
    fn damaged_preferences_are_preserved_before_any_replacement() {
        let directory = preferences_directory("damaged");
        let path = directory.join("ui.json");
        let damaged = format!("{EXAMPLE_GROUP} trailing damage {{");
        std::fs::write(&path, &damaged).unwrap();

        let mut state = MixerState::with_preferences_file(path.clone());
        assert!(!state.serialize_ui_preferences().contains("example-group"), "nothing usable was read");
        assert!(state.preferences_notice().contains("could not be read"), "{}", state.preferences_notice());
        assert!(state.warning.contains("was kept as ui.json.damaged-"), "{}", state.warning);
        let copies = preserved_copies(&directory);
        assert_eq!(copies.len(), 1, "preserved at load, before any save");
        assert_eq!(std::fs::read_to_string(&copies[0]).unwrap(), damaged);
        assert_eq!(std::fs::read_to_string(&path).unwrap(), damaged, "loading alone changes nothing");

        state.set_show_minimap(false);
        state.ensure_ui_preferences_saved().unwrap();
        let replaced = std::fs::read_to_string(&path).unwrap();
        assert!(replaced.contains(r#""showMinimap":false"#), "{replaced}");
        assert_eq!(std::fs::read_to_string(&copies[0]).unwrap(), damaged, "the damaged original, with its group, is still recoverable");
        assert!(!crate::preferences::backup_path(&path).exists(), "damaged data never becomes the last-good backup");
        assert!(state.warning.contains("was kept as"), "the recovery notice outlives a successful save: {}", state.warning);
        drop(state);
        let _ = std::fs::remove_dir_all(directory);
    }

    #[test]
    fn damaged_preferences_recover_groups_from_the_last_good_backup() {
        let directory = preferences_directory("backup");
        let path = directory.join("ui.json");
        std::fs::write(&path, EXAMPLE_GROUP).unwrap();
        {
            // Two ordinary saves: the second keeps the first as the backup.
            let mut state = MixerState::with_preferences_file(path.clone());
            assert!(state.serialize_ui_preferences().contains("example-group"));
            state.set_show_minimap(false);
            state.ensure_ui_preferences_saved().unwrap();
            state.set_right_click_drag_to_pan(true);
            state.ensure_ui_preferences_saved().unwrap();
        }
        let backup = std::fs::read_to_string(crate::preferences::backup_path(&path)).unwrap();
        assert!(backup.contains("example-group") && backup.contains(r#""showMinimap":false"#), "{backup}");
        std::fs::write(&path, b"\0\0 not json").unwrap();

        let mut state = MixerState::with_preferences_file(path.clone());
        assert!(state.serialize_ui_preferences().contains(r#""name":"Example group""#), "the group returns from the backup");
        assert!(!state.show_minimap());
        assert!(state.preferences_notice().contains("previous saved copy"), "{}", state.preferences_notice());
        assert_eq!(preserved_copies(&directory).len(), 1);
        // The first save after recovery must not replace the good backup with
        // the damaged primary.
        state.set_show_minimap(true);
        state.ensure_ui_preferences_saved().unwrap();
        assert_eq!(std::fs::read_to_string(crate::preferences::backup_path(&path)).unwrap(), backup);
        let saved = std::fs::read_to_string(&path).unwrap();
        assert!(saved.contains("example-group") && saved.contains(r#""showMinimap":true"#), "{saved}");
        drop(state);
        let _ = std::fs::remove_dir_all(directory);
    }

    #[test]
    fn oversized_preferences_are_treated_as_unreadable_not_loaded() {
        let directory = preferences_directory("oversized");
        let path = directory.join("ui.json");
        let file = std::fs::File::create(&path).unwrap();
        file.set_len(crate::preferences::MAXIMUM_BYTES + 1).unwrap();
        drop(file);
        let state = MixerState::with_preferences_file(path.clone());
        assert!(state.preferences_notice().contains("could not be read"));
        assert_eq!(preserved_copies(&directory).len(), 1);
        drop(state);
        let _ = std::fs::remove_dir_all(directory);
    }

    #[test]
    fn failed_preference_save_is_reported_and_blocks_the_quit_acknowledgement() {
        let directory = preferences_directory("failing");
        // The "file" is a directory, so every write fails.
        let path = directory.join("ui.json");
        let mut state = MixerState::with_preferences_file(path.clone());
        std::fs::create_dir_all(&path).unwrap();
        state.set_show_minimap(false);
        let error = state.ensure_ui_preferences_saved().unwrap_err();
        assert!(error.contains("could not be saved"), "{error}");
        assert!(state.has_pending_ui_preferences());
        assert!(state.warning.contains("could not be saved"), "{}", state.warning);
        // Storage recovers: the same pending change is written and acknowledged.
        std::fs::remove_dir_all(&path).unwrap();
        state.ensure_ui_preferences_saved().unwrap();
        assert!(!state.has_pending_ui_preferences());
        assert!(std::fs::read_to_string(&path).unwrap().contains(r#""showMinimap":false"#));
        assert!(state.warning.is_empty(), "{}", state.warning);
        drop(state);
        let _ = std::fs::remove_dir_all(directory);
    }
}
