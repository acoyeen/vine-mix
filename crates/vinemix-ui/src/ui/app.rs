//! The main window (`MainWindow`, `App`): custom title bar, workspace
//! toolbar, canvas, footer, recovery banner, settings menu, overlays,
//! dialogs, tray integration and the quit flow.

mod settings_menu;

use crate::ui::widgets::Tip as _;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, Sender};
use std::sync::Arc;

use eframe::egui::{self, Align2, Color32, Id, Key, Pos2, Rect, Sense, Stroke, Ui, Vec2};

use super::canvas::{self, Canvas};
use super::theme::{self, Weight};
use super::widgets::{self, ButtonStyle};
use crate::link::{EngineLink, LinkEvent};
use crate::mixer::{MixerState, QuitSaveResult};

pub const TITLE_BAR_HEIGHT: f32 = 36.0;
pub const WEBSITE: &str = "https://vinemix.app";

/// Commands from the tray icon and other launches.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Command {
    Show,
    SoundSettings,
    Quit,
}

#[derive(Debug)]
enum Overlay {
    None,
    Help(super::help_view::HelpState),
    Search,
    About(bool),
    Credits(usize),
    WhatsNew,
    Quit(QuitSaveResult),
    Updates,
    ProcessingLoad,
}

#[derive(Debug)]
enum QuitPhase {
    Idle,
    Saving(Receiver<Result<QuitSaveResult, String>>),
    Stopping(Receiver<()>),
}

pub struct App {
    link: Arc<EngineLink>,
    canvas: Canvas,
    overlay: Overlay,
    settings_open: bool,
    settings_search: settings_menu::SettingsSearch,
    node_search: super::node_search::NodeSearchState,
    last_warning: String,
    last_blocked: u64,
    lock_feedback_until: f64,
    lock_angle: f32,
    restart_in_progress: bool,
    quit: QuitPhase,
    quitting: bool,
    allow_close: bool,
    commands: Receiver<Command>,
    _command_sender: Sender<Command>,
    hidden: bool,
    background_start: bool,
    pub whats_new: super::whats_new::WhatsNew,
    pub updates: super::updates::UpdatesUi,
    last_load_refresh: f64,
    last_load_details: String,
    /// Where the footer's load bar is drawn: it glides to each new reading.
    load_shown: f32,
    dismissed_processing_notice: Option<u64>,
    preview: bool,
    exit_flag: Arc<AtomicBool>,
    exit_code: Arc<std::sync::atomic::AtomicI32>,
    /// False while an updated candidate confirms its health.
    window_enabled: bool,
    wordmark: super::brand::Wordmark,
    health: Option<Receiver<crate::update::session::HealthOutcome>>,
    automation: Option<super::automation::Automation>,
}

/// What `main` decided before the window opened.
pub struct Startup {
    pub background: bool,
    pub preview: bool,
    pub settings_existed: bool,
    pub exit_flag: Arc<AtomicBool>,
    pub exit_code: Arc<std::sync::atomic::AtomicI32>,
    /// `--update-health <transaction>`: run as the update candidate.
    pub update_health: Option<std::path::PathBuf>,
    /// The rollback notice for `--update-reverted`.
    pub rollback_notice: Option<String>,
    pub repaint: Arc<dyn Fn() + Send + Sync>,
}

impl std::fmt::Debug for Startup {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Startup").field("preview", &self.preview).field("update_health", &self.update_health).finish_non_exhaustive()
    }
}

impl std::fmt::Debug for App {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("App").finish_non_exhaustive()
    }
}

fn seconds() -> f64 {
    static START: std::sync::OnceLock<std::time::Instant> = std::sync::OnceLock::new();
    START.get_or_init(std::time::Instant::now).elapsed().as_secs_f64()
}

/// Save status in the footer (`CompactSaveStatus`).
/// The audio-load bar: the full width is 100% of the engine's time. `load`
/// fills it from the left; `peak` (the busiest moment of the last second)
/// is a mark on it. Green, then amber from 70%, then red: the same steps as
/// the engine's own "busy" and "at risk" states, which `state` carries.
fn load_bar(painter: &egui::Painter, rect: Rect, load: f32, peak: Option<f32>, state: &str) {
    painter.rect_filled(rect, 1.0, theme::METER_UNLIT);
    let at = |percent: f32| rect.left() + rect.width() * (percent / 100.0).clamp(0.0, 1.0);
    let color = match state {
        "overloaded" => theme::METER_HIGH,
        "busy" => theme::METER_MID,
        _ if load >= 100.0 => theme::METER_HIGH,
        _ if load >= 70.0 => theme::METER_MID,
        _ => theme::METER_LOW,
    };
    if load > 0.0 {
        // Never less than a sliver, so a working engine is visibly working.
        let right = at(load).max(rect.left() + 2.0);
        painter.rect_filled(Rect::from_min_max(rect.min, Pos2::new(right, rect.bottom())), 1.0, color);
    }
    if let Some(peak) = peak {
        let x = at(peak).clamp(rect.left() + 1.0, rect.right() - 1.0);
        let mark = if peak >= 100.0 { theme::METER_HIGH } else { theme::TEXT };
        painter.rect_filled(Rect::from_min_max(Pos2::new(x - 1.0, rect.top()), Pos2::new(x + 1.0, rect.bottom())), 0.0, mark);
    }
}

fn compact_status(status: &str) -> &str {
    if status.starts_with("Saved ·") {
        "Saved"
    } else if status.starts_with("Preview ·") {
        "Preview"
    } else {
        status
    }
}

impl App {
    #[allow(clippy::too_many_arguments)]
    pub fn new(cc: &eframe::CreationContext<'_>, link: Arc<EngineLink>, commands: Receiver<Command>, sender: Sender<Command>, startup: Startup) -> Self {
        let whats_new = super::whats_new::WhatsNew::begin(startup.settings_existed, startup.preview);
        Self::build(&cc.egui_ctx, link, commands, sender, startup, whats_new)
    }

    /// A complete application over a preview workspace and node groups, for
    /// headless interface tests: no engine, no settings or data files, no
    /// network, no window.
    #[cfg(test)]
    pub(crate) fn headless_with(ctx: &egui::Context, snapshot: crate::protocol::Snapshot, groups: Vec<crate::frames::Frame>) -> Self {
        let mut state = MixerState::new(true);
        state.apply_preview_snapshot(snapshot, groups).expect("the preview snapshot applies");
        let link = EngineLink::preview(state, Box::new(|| {}));
        let (sender, commands) = std::sync::mpsc::channel();
        let startup = Startup {
            background: false,
            preview: true,
            settings_existed: false,
            exit_flag: Arc::new(AtomicBool::new(false)),
            exit_code: Arc::new(std::sync::atomic::AtomicI32::new(0)),
            update_health: None,
            rollback_notice: None,
            repaint: Arc::new(|| {}),
        };
        Self::build(ctx, link, commands, sender, startup, super::whats_new::WhatsNew::inert())
    }

    fn build(
        ctx: &egui::Context,
        link: Arc<EngineLink>,
        commands: Receiver<Command>,
        sender: Sender<Command>,
        startup: Startup,
        whats_new: super::whats_new::WhatsNew,
    ) -> Self {
        theme::install(ctx);
        egui_extras::install_image_loaders(ctx);
        ctx.set_theme(egui::Theme::Dark);
        let Startup { background, preview, settings_existed: _, exit_flag, exit_code, update_health, rollback_notice, repaint } = startup;
        let health = update_health.map(|transaction| crate::update::session::observe_candidate(Arc::clone(&link), transaction, repaint));
        let mut app = Self {
            link,
            canvas: Canvas::default(),
            overlay: Overlay::None,
            settings_open: false,
            settings_search: settings_menu::SettingsSearch::default(),
            node_search: super::node_search::NodeSearchState::default(),
            last_warning: String::new(),
            last_blocked: 0,
            lock_feedback_until: 0.0,
            lock_angle: 50.0,
            restart_in_progress: false,
            quit: QuitPhase::Idle,
            quitting: false,
            allow_close: false,
            commands,
            _command_sender: sender,
            hidden: background,
            background_start: background,
            whats_new,
            last_load_refresh: 0.0,
            last_load_details: String::new(),
            load_shown: 0.0,
            dismissed_processing_notice: None,
            updates: {
                let mut updates = super::updates::UpdatesUi::default();
                updates.set_preview(preview);
                updates
            },
            preview,
            exit_flag,
            exit_code,
            window_enabled: health.is_none(),
            wordmark: super::brand::Wordmark::default(),
            health,
            automation: super::automation::Automation::from_environment(),
        };
        if let Some(notice) = rollback_notice {
            app.message(&notice, true);
        }
        app
    }

    /// Applies the candidate's health result (`ObserveUpdatedCandidateAsync`).
    fn poll_update_health(&mut self, ctx: &egui::Context) {
        let Some(receiver) = &self.health else { return };
        let Ok(outcome) = receiver.try_recv() else { return };
        self.health = None;
        match outcome {
            crate::update::session::HealthOutcome::Healthy => {
                self.link.state().set_update_health_mode(false);
                self.window_enabled = true;
                self.message(&format!("VineMix updated to {}. Your workspace is ready.", vinemix_platform::release::DISPLAY_VERSION), false);
            }
            crate::update::session::HealthOutcome::Failed { stopped: true, .. } => {
                self.quitting = true;
                self.allow_close = true;
                self.exit_code.store(1, Ordering::Release);
                self.link.detach();
                self.exit_flag.store(true, Ordering::Release);
                ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            }
            crate::update::session::HealthOutcome::Failed { stopped: false, message } => {
                self.link.state().allow_quit_after_failed_update_health();
                self.show_window(ctx);
                self.window_enabled = true;
                self.message(&format!("The update needs recovery. Quit VineMix from its tray menu, then reopen it to recover. {message}"), true);
            }
        }
    }

    fn mixer(&self) -> std::sync::MutexGuard<'_, MixerState> {
        self.link.state()
    }

    pub fn show_window(&mut self, ctx: &egui::Context) {
        if self.quitting {
            return;
        }
        self.hidden = false;
        self.link.state().visible = true;
        // End the hidden one-second poll wait now: fresh telemetry at once.
        self.link.wake();
        ctx.send_viewport_cmd(egui::ViewportCommand::Visible(true));
        ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(false));
        ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
        ctx.request_repaint();
    }

    fn hide_window(&mut self, ctx: &egui::Context) {
        if self.preview {
            self.allow_close = true;
            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            return;
        }
        self.hidden = true;
        self.link.state().visible = false;
        ctx.send_viewport_cmd(egui::ViewportCommand::Visible(false));
    }

    fn message(&mut self, text: &str, error: bool) {
        let mixer = self.link.state();
        self.canvas.set_message(&mixer, text, error);
    }

    // ----- quit ------------------------------------------------------------

    /// `App.QuitAsync`: save, resolve failures, then stop the engine.
    pub fn begin_quit(&mut self, ctx: &egui::Context) {
        if self.quitting || !matches!(self.quit, QuitPhase::Idle) {
            return;
        }
        // A check or download is cancelled and quitting continues through
        // the normal save flow; only the installation handoff has to finish.
        if !self.updates.cancel_for_quit() {
            self.message("VineMix is installing an update and will close by itself in a moment. Quit is not needed.", false);
            self.show_window(ctx);
            return;
        }
        {
            let mixer = self.link.state();
            if mixer.is_update_health_mode() && !mixer.can_quit_after_failed_update_health() {
                return;
            }
        }
        if self.canvas.has_active_gesture() {
            self.message("Finish moving or connecting the node before quitting.", true);
            self.show_window(ctx);
            return;
        }
        ctx.memory_mut(|m| {
            if let Some(focused) = m.focused() {
                m.surrender_focus(focused);
            }
        });
        if self.canvas.has_invalid_drafts() {
            self.message("Enter a valid value before quitting. Your draft has been kept.", true);
            self.show_window(ctx);
            return;
        }
        if self.link.state().is_restarting_engine() {
            self.message("Wait for the audio engine restart to finish before quitting.", true);
            self.show_window(ctx);
            return;
        }
        self.settings_open = false;
        self.start_quit_save(ctx);
    }

    fn start_quit_save(&mut self, ctx: &egui::Context) {
        let (sender, receiver) = std::sync::mpsc::channel();
        let link = Arc::clone(&self.link);
        let repaint = ctx.clone();
        std::thread::spawn(move || {
            let _ = sender.send(link.prepare_for_quit());
            repaint.request_repaint();
        });
        self.quit = QuitPhase::Saving(receiver);
    }

    fn stop_and_exit(&mut self, ctx: &egui::Context) {
        self.quitting = true;
        let (sender, receiver) = std::sync::mpsc::channel();
        let link = Arc::clone(&self.link);
        let repaint = ctx.clone();
        std::thread::spawn(move || {
            link.stop();
            let _ = sender.send(());
            repaint.request_repaint();
        });
        self.quit = QuitPhase::Stopping(receiver);
    }

    fn poll_quit(&mut self, ctx: &egui::Context) {
        match &self.quit {
            QuitPhase::Saving(receiver) => {
                if let Ok(result) = receiver.try_recv() {
                    self.quit = QuitPhase::Idle;
                    match result {
                        Ok(result) if result.saved => self.stop_and_exit(ctx),
                        Ok(result) => {
                            self.show_window(ctx);
                            self.overlay = Overlay::Quit(result);
                        }
                        Err(error) => {
                            self.link.cancel_quit_preparation();
                            self.message(&format!("VineMix stayed open because quitting did not finish. {error}"), true);
                            self.show_window(ctx);
                        }
                    }
                }
            }
            QuitPhase::Stopping(receiver) => {
                if receiver.try_recv().is_ok() {
                    self.quit = QuitPhase::Idle;
                    crate::paths::lifetime_log::write("Exiting explicitQuit=true");
                    self.allow_close = true;
                    self.exit_flag.store(true, Ordering::Release);
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
            }
            QuitPhase::Idle => {}
        }
    }

    fn save_recovery_copy(&mut self) -> Result<bool, String> {
        let name = format!("VineMix-recovery-{}.json", crate::time::file_stamp());
        let Some(path) = crate::platform::save_file_dialog("Save VineMix recovery copy", &name, "VineMix workspace recovery (*.json)", "*.json") else {
            return Ok(false);
        };
        self.link
            .state()
            .write_quit_recovery_copy(&path)
            .map(|()| true)
            .map_err(|e| format!("Choose a writable location and a filename that does not already exist. {e}"))
    }

    // ----- frame -----------------------------------------------------------

    fn handle_commands(&mut self, ctx: &egui::Context) {
        while let Ok(command) = self.commands.try_recv() {
            match command {
                Command::Show => self.show_window(ctx),
                Command::SoundSettings => {
                    let _ = crate::platform::shell_open("ms-settings:sound");
                }
                Command::Quit => self.begin_quit(ctx),
            }
        }
        for event in self.link.take_events() {
            match event {
                LinkEvent::RestartFinished { success, message } => {
                    self.restart_in_progress = false;
                    self.message(&message, !success);
                }
                LinkEvent::Message { text, error } => self.message(&text, error),
            }
        }
    }

    #[allow(clippy::too_many_lines)]
    fn title_bar(&mut self, ui: &mut Ui, rect: Rect) {
        let ctx = ui.ctx().clone();
        let painter = ui.painter().clone();
        painter.rect_filled(rect, 0.0, theme::WINDOW);
        let drag = ui.interact(rect, Id::unique("title-drag"), Sense::click_and_drag());
        if drag.drag_started_by(egui::PointerButton::Primary) {
            ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
        }
        if drag.double_clicked() {
            let maximized = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
            ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
        }
        let (can_edit, status, warning) = {
            let mixer = self.mixer();
            (mixer.can_edit(), mixer.connection_status.clone(), mixer.warning.clone())
        };
        let dot = Pos2::new(rect.left() + 20.0, rect.center().y);
        painter.circle_filled(dot, 4.0, if can_edit { theme::A } else { theme::MUTE });
        ui.interact(Rect::from_center_size(dot, Vec2::splat(12.0)), Id::unique("connection-dot"), Sense::hover()).tip(&status);
        let version = painter.text(
            Pos2::new(dot.x + 12.0, rect.center().y),
            Align2::LEFT_CENTER,
            vinemix_platform::release::DISPLAY_VERSION,
            theme::font(11.0, Weight::Regular),
            theme::MUTED,
        );
        // Centred wordmark (opens the website).
        let mark = Rect::from_center_size(rect.center(), Vec2::new(122.0, 20.0));
        let mark_response = ui.interact(mark, Id::unique("website"), Sense::click()).tip("Opens https://vinemix.app in your browser.");
        // No hover highlight behind the wordmark; the cursor still marks the link.
        if mark_response.hovered() {
            ctx.set_cursor_icon(egui::CursorIcon::PointingHand);
        }
        self.wordmark.paint(ui, Rect::from_center_size(mark.center(), Vec2::new(106.0, 20.0)), theme::TEXT);
        if mark_response.clicked() {
            let _ = crate::platform::shell_open(WEBSITE);
        }
        let warning_width = (mark.left() - 12.0 - (version.right() + 12.0)).max(0.0);
        if !warning.is_empty() {
            let shown = widgets::text_elided(
                &painter,
                Pos2::new(version.right() + 12.0, rect.center().y),
                Align2::LEFT_CENTER,
                &warning.replace('\n', " · "),
                theme::font(12.0, Weight::Regular),
                theme::ALERT_TEXT,
                warning_width,
            );
            ui.interact(shown, Id::unique("title-warning"), Sense::hover()).tip(&warning);
        }
        // Right: window buttons (34 x 30, flush), Settings (8 px gap) and the
        // layout lock (6 px gap), as in MainWindow.xaml.
        let mut x = rect.right();
        let mut button = || -> Rect {
            x -= 34.0;
            Rect::from_min_size(Pos2::new(x, rect.center().y - 15.0), Vec2::new(34.0, 30.0))
        };
        let close = button();
        let maximize = button();
        let minimize = button();
        let close_help = if self.preview { "Close UI preview" } else { "Close to tray — audio keeps running" };
        if self.window_button(ui, close, "close", close_help) {
            self.hide_window(&ctx);
        }
        let maximized = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
        if self.window_button(ui, maximize, if maximized { "restore" } else { "maximize" }, if maximized { "Restore" } else { "Maximise" }) {
            ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
        }
        if self.window_button(ui, minimize, "minimize", "Minimise") {
            ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(true));
        }
        x -= 8.0;
        let settings = Rect::from_min_size(Pos2::new(x - 78.0, rect.center().y - 13.0), Vec2::new(78.0, 26.0));
        x = settings.left() - 6.0;
        let settings_enabled = matches!(self.overlay, Overlay::None);
        if widgets::button(ui, Id::unique("settings"), settings, "Settings", ButtonStyle::toolbar(13.0), settings_enabled, 1.0).clicked() {
            self.settings_open = !self.settings_open;
            self.settings_search.reset();
        }
        // A verified update waits: mark Settings until it is handled.
        if let Some(version) = self.updates.available_version() {
            let dot = Pos2::new(settings.right() - 7.0, settings.top() + 6.0);
            painter.circle_filled(dot, 3.5, theme::A);
            ui.interact(Rect::from_center_size(dot, Vec2::splat(10.0)), Id::unique("update-dot"), Sense::hover()).tip(format!("VineMix {version} is available"));
        }
        let lock = Rect::from_min_size(Pos2::new(x - 34.0, rect.center().y - 14.0), Vec2::new(34.0, 28.0));
        self.layout_lock_button(ui, lock);
        if self.settings_open {
            self.settings_menu(ui, settings);
        }
        painter.line_segment([rect.left_bottom(), rect.right_bottom()], Stroke::new(1.0, theme::BORDER));
    }

    fn window_button(&self, ui: &mut Ui, rect: Rect, glyph: &str, help: &str) -> bool {
        let response = widgets::button(ui, Id::unique(("window", glyph)), rect, "", ButtonStyle::toolbar(13.0), true, 1.0);
        response.widget_info(|| egui::WidgetInfo::labeled(egui::accesskit::Role::Button, true, help));
        let response = response.tip(help);
        let painter = ui.painter();
        let c = rect.center();
        let stroke = Stroke::new(1.2, theme::TEXT);
        match glyph {
            "close" => {
                painter.line_segment([c + Vec2::new(-5.0, -5.0), c + Vec2::new(5.0, 5.0)], stroke);
                painter.line_segment([c + Vec2::new(5.0, -5.0), c + Vec2::new(-5.0, 5.0)], stroke);
            }
            "maximize" => {
                painter.rect_stroke(Rect::from_center_size(c, Vec2::splat(10.0)), 1.0, stroke, egui::StrokeKind::Middle);
            }
            "restore" => {
                painter.rect_stroke(Rect::from_center_size(c + Vec2::new(-1.5, 1.5), Vec2::splat(8.0)), 1.0, stroke, egui::StrokeKind::Middle);
                painter.line_segment([c + Vec2::new(-1.5, -4.5), c + Vec2::new(4.5, -4.5)], stroke);
                painter.line_segment([c + Vec2::new(4.5, -4.5), c + Vec2::new(4.5, 1.5)], stroke);
            }
            _ => {
                painter.line_segment([c + Vec2::new(-5.0, 0.0), c + Vec2::new(5.0, 0.0)], stroke);
            }
        }
        response.clicked()
    }

    fn layout_lock_button(&mut self, ui: &mut Ui, rect: Rect) {
        let locked = self.mixer().is_layout_locked();
        let help = if locked { "Unlock layout\nAllow node and cable changes." } else { "Lock layout\nKeep nodes and cables in place." };
        let response = widgets::button(ui, Id::unique("layout-lock"), rect, "", ButtonStyle::toolbar(13.0), true, 1.0).tip(help);
        if response.clicked() {
            let mut mixer = self.mixer();
            let next = !mixer.is_layout_locked();
            mixer.set_layout_locked(next);
            drop(mixer);
            if next || self.canvas_gesture_locked() {
                let link = Arc::clone(&self.link);
                let mut mixer = link.state();
                self.canvas.cancel_active_gesture(&mut mixer);
            }
            let text = if next {
                "Layout locked. Sound controls, selection, pan and zoom remain available."
            } else {
                "Layout unlocked. Nodes and connections can be edited."
            };
            self.message(text, false);
        }
        // Hinge animation (180 ms ease-out) and blocked-edit emphasis.
        let target = if locked { 0.0 } else { 50.0 };
        let motion = crate::platform::client_area_animation() && !self.mixer().reduced_animation();
        let dt = ui.input(|i| i.stable_dt).min(0.1);
        if motion {
            let step = 50.0 * dt / 0.18;
            self.lock_angle = if self.lock_angle < target { (self.lock_angle + step).min(target) } else { (self.lock_angle - step).max(target) };
            if (self.lock_angle - target).abs() > 0.01 {
                ui.ctx().request_repaint();
            }
        } else {
            self.lock_angle = target;
        }
        let blocked = self.mixer().layout_edit_blocked;
        let now = seconds();
        if blocked != self.last_blocked {
            self.last_blocked = blocked;
            if locked && now >= self.lock_feedback_until {
                self.lock_feedback_until = now + 0.71;
            }
        }
        let elapsed = 0.71 - (self.lock_feedback_until - now);
        let emphasis = if now < self.lock_feedback_until {
            ui.ctx().request_repaint();
            if !motion {
                1.4
            } else if elapsed < 0.12 {
                1.0 + 0.4 * (1.0 - (1.0 - elapsed / 0.12).powi(3))
            } else if elapsed < 0.18 {
                1.4
            } else {
                let t = ((elapsed - 0.18) / 0.48).min(1.0);
                1.4 - 0.4 * (1.0 - (std::f64::consts::PI * t).cos()) / 2.0
            }
        } else {
            1.0
        } as f32;
        let color = if locked { Color32::from_rgb(255, 100, 100) } else { theme::MUTED };
        let scale = 16.0 / 24.0 * emphasis;
        let origin = rect.center() - Vec2::splat(12.0 * scale);
        let p = |x: f32, y: f32| origin + Vec2::new(x, y) * scale;
        let stroke = Stroke::new(2.0 * scale, color);
        let painter = ui.painter();
        // Body.
        painter.rect_stroke(Rect::from_min_max(p(4.0, 10.0), p(18.0, 21.0)), 1.0 * scale, stroke, egui::StrokeKind::Middle);
        painter.line_segment([p(11.0, 14.0), p(11.0, 17.0)], stroke);
        // Shackle, rotated about the right hinge (16, 10).
        let angle = self.lock_angle.to_radians();
        let rotate = |x: f32, y: f32| {
            let (dx, dy) = (x - 16.0, y - 10.0);
            p(16.0 + dx * angle.cos() - dy * angle.sin(), 10.0 + dx * angle.sin() + dy * angle.cos())
        };
        let mut shackle = vec![rotate(6.0, 10.0), rotate(6.0, 7.0)];
        for i in 0..=16 {
            let a = std::f32::consts::PI + std::f32::consts::PI * i as f32 / 16.0;
            shackle.push(rotate(11.0 + 5.0 * a.cos(), 7.0 + 5.0 * a.sin()));
        }
        shackle.push(rotate(16.0, 10.0));
        painter.add(egui::Shape::line(shackle, stroke));
    }

    fn canvas_gesture_locked(&self) -> bool {
        self.canvas.has_active_gesture()
    }

    fn toolbar(&mut self, ui: &mut Ui, rect: Rect) {
        let ctx = ui.ctx().clone();
        let mut mixer = self.link.state();
        let can_layout = mixer.can_edit_layout();
        let row = rect.shrink2(Vec2::new(8.0, 4.0));
        let button = |x: f32, width: f32| Rect::from_min_size(Pos2::new(x, row.center().y - 13.0), Vec2::new(width, 26.0));
        // Toolbar buttons size to their text plus 10 px padding per side.
        let auto = |text: &str| widgets::text_width(&ctx, text, theme::font(13.0, Weight::Regular)).ceil() + 20.0;
        let mut x = row.left();
        let add = button(x, auto("Add"));
        let add_enabled = can_layout && mixer.graph().nodes.len() < crate::graph::MAXIMUM_NODES;
        if widgets::button(ui, Id::unique("tool-add"), add, "Add", ButtonStyle::toolbar_primary(13.0), add_enabled, 1.0).tip("Opens the node library.").clicked() {
            let center = self.canvas.visible_center();
            self.canvas.open_add_menu(&mut mixer, add.left_bottom(), center);
        }
        x = add.right() + 7.0;
        let remove = button(x, auto("Remove"));
        let remove_help =
            if self.canvas.selected_frame().is_some() { "Ungroups the selected frame and keeps its nodes and connections." } else { "Removes the selected nodes or cable." };
        let remove_enabled = can_layout && (!self.canvas.selected.is_empty() || self.canvas.has_selected_edge());
        if widgets::button(ui, Id::unique("tool-remove"), remove, "Remove", ButtonStyle::toolbar(13.0), remove_enabled, 1.0).tip(remove_help).clicked() {
            self.canvas.delete_selection(&mut mixer);
        }
        x = remove.right();
        for redo in [false, true] {
            let r = button(x, auto(if redo { "Redo" } else { "Undo" }));
            x = r.right();
            let (enabled, description, layout) = if redo {
                (mixer.can_redo(), mixer.history.redo_description(), mixer.history.redo_changes_layout())
            } else {
                (mixer.can_undo(), mixer.history.undo_description(), mixer.history.undo_changes_layout())
            };
            let verb = if redo { "Redo" } else { "Undo" };
            let help = if mixer.is_layout_locked() && layout {
                format!("Unlock layout to {}: {description}", verb.to_lowercase())
            } else if description.is_empty() {
                format!("Nothing to {}", verb.to_lowercase())
            } else {
                format!("{verb}: {description}")
            };
            if widgets::button(ui, Id::unique(("tool-history", redo)), r, verb, ButtonStyle::toolbar(13.0), enabled, 1.0).tip(help).clicked() {
                drop(mixer);
                self.restore_history(redo);
                mixer = self.link.state();
            }
        }
        // Centre: node count.
        let count = mixer.graph().nodes.len();
        let mut text = format!("{count} nodes");
        if !self.canvas.selected.is_empty() {
            text += &format!(" · {} selected", self.canvas.selected.len());
        }
        let shown = ui.painter().text(rect.center(), Align2::CENTER_CENTER, text, theme::font(13.0, Weight::Regular), theme::MUTED);
        ui.interact(shown, Id::unique("node-count"), Sense::hover()).tip(format!("{} connections · 32 node limit", mixer.graph().edges.len()));
        // Right: Fit, −, zoom %, +.
        let mut right = row.right();
        let plus = button(right - 32.0, 32.0);
        right = plus.left();
        let reset = Rect::from_min_size(Pos2::new(right - 4.0 - 62.0, row.center().y - 13.0), Vec2::new(62.0, 26.0));
        right = reset.left() - 4.0;
        let minus = button(right - 32.0, 32.0);
        right = minus.left() - 8.0;
        let fit_width = auto("Fit");
        let fit = button(right - fit_width, fit_width);
        if widgets::button(ui, Id::unique("tool-fit"), fit, "Fit", ButtonStyle::toolbar(13.0), true, 1.0).tip("Brings every node into view.").clicked() {
            self.canvas.fit_all(&ctx, &mut mixer);
        }
        if widgets::button(ui, Id::unique("tool-minus"), minus, "−", ButtonStyle::toolbar(13.0), true, 1.0).tip("Zoom out").clicked() {
            self.canvas.step_zoom(&ctx, &mut mixer, None, -1);
        }
        let percent = format!("{:.0}%", self.canvas.zoom * 100.0);
        if widgets::button(ui, Id::unique("tool-zoom"), reset, &percent, ButtonStyle::toolbar(13.0), true, 1.0)
            .tip("Reset zoom\nRestores 100% canvas zoom")
            .clicked()
        {
            self.canvas.reset_zoom(&ctx, &mut mixer);
        }
        if widgets::button(ui, Id::unique("tool-plus"), plus, "+", ButtonStyle::toolbar(13.0), true, 1.0).tip("Zoom in").clicked() {
            self.canvas.step_zoom(&ctx, &mut mixer, None, 1);
        }
    }

    pub fn restore_history(&mut self, redo: bool) -> bool {
        let mut mixer = self.link.state();
        self.canvas.cancel_active_gesture(&mut mixer);
        mixer.end_edit_gesture();
        let description = if redo { mixer.history.redo_description() } else { mixer.history.undo_description() };
        let restored = if redo { mixer.redo() } else { mixer.undo() };
        let layout = if redo { mixer.history.redo_changes_layout() } else { mixer.history.undo_changes_layout() };
        let text = if restored {
            format!("{}{description}.", if redo { "Redone: " } else { "Undone: " })
        } else if mixer.is_layout_locked() && layout {
            crate::mixer::LAYOUT_LOCKED_MESSAGE.into()
        } else if !mixer.warning.trim().is_empty() {
            let warning = mixer.warning.clone();
            self.canvas.set_message(&mixer, &warning, true);
            return false;
        } else if mixer.can_edit() {
            if redo { "Nothing to redo.".into() } else { "Nothing to undo.".into() }
        } else {
            "Reconnect to the audio engine before changing the graph.".into()
        };
        self.canvas.set_message(&mixer, &text, false);
        restored
    }

    fn footer(&mut self, ui: &mut Ui, rect: Rect) {
        let painter = ui.painter().clone();
        painter.rect_filled(rect, 0.0, theme::WINDOW);
        painter.line_segment([rect.left_top(), rect.right_top()], Stroke::new(1.0, theme::BORDER));
        let (status, reduced) = {
            let mixer = self.mixer();
            (mixer.save_status.clone(), mixer.reduced_animation())
        };
        // The load meter keeps 248 px on the right (232 px control, 6/8 px
        // margins); the save state stays centred with a symmetric reserve.
        let load = self.mixer().processing_load().clone();
        let alert = matches!(load.state, "busy" | "overloaded");
        let load_rect = Rect::from_min_size(Pos2::new(rect.right() - 8.0 - 232.0, rect.center().y - 13.0), Vec2::new(232.0, 26.0));
        let help = if load.available {
            "Audio load\nHow much of its time the audio engine is using. The mark is the busiest moment of the last second. Opens the details."
        } else {
            "Audio load\nThere is no reading right now. Opens the details."
        };
        let load_button = ui.interact(load_rect, Id::unique("processing-load"), Sense::click());
        load_button.widget_info(|| egui::WidgetInfo::labeled(egui::accesskit::Role::Button, true, format!("Audio load: {}", load.label.replace("Audio load ", ""))));
        {
            // The bar glides to each reading; with reduced animation, and
            // when there is no reading, it is simply drawn where it belongs.
            let target = if load.available { load.load as f32 } else { 0.0 };
            if reduced || !load.available || !crate::platform::client_area_animation() {
                self.load_shown = target;
            } else {
                let elapsed = ui.input(|i| i.stable_dt).clamp(0.0, 0.25);
                self.load_shown += (target - self.load_shown) * (1.0 - (-elapsed / 0.12).exp());
                if (target - self.load_shown).abs() > 0.05 {
                    ui.ctx().request_repaint_after(std::time::Duration::from_millis(33));
                } else {
                    self.load_shown = target;
                }
            }
            let face = if load_button.is_pointer_button_down_on() {
                theme::CONTROL_BORDER
            } else if load_button.hovered() {
                theme::BUTTON_HOVER
            } else {
                Color32::TRANSPARENT
            };
            painter.rect_filled(load_rect, 0.0, face);
            let font = theme::font(12.0, Weight::Regular);
            let text = if alert { theme::ALERT_TEXT } else { theme::MUTED };
            let name = painter.text(Pos2::new(load_rect.left() + 8.0, load_rect.center().y), Align2::LEFT_CENTER, "Audio load", font.clone(), text);
            // Room for "100%" at this size, so the bar keeps its width as the reading changes.
            let value_left = load_rect.right() - 8.0 - 34.0;
            painter.text(Pos2::new(load_rect.right() - 8.0, load_rect.center().y), Align2::RIGHT_CENTER, load.value(), font, if alert { theme::ALERT_TEXT } else { theme::TEXT });
            let bar = Rect::from_min_max(Pos2::new(name.right() + 10.0, load_rect.center().y - 4.0), Pos2::new(value_left - 8.0, load_rect.center().y + 4.0));
            load_bar(&painter, bar, self.load_shown, load.available.then_some(load.peak as f32), load.state);
            if load_button.has_focus() {
                painter.rect_stroke(load_rect.shrink(2.0), 2.0, Stroke::new(1.0, theme::FOCUS), egui::StrokeKind::Inside);
            }
            if load_button.hovered() {
                ui.ctx().set_cursor_icon(egui::CursorIcon::PointingHand);
            }
        }
        if load_button.tip(help).clicked() && matches!(self.overlay, Overlay::None) {
            self.mixer().refresh_processing_load(None);
            self.overlay = Overlay::ProcessingLoad;
        }
        let rect = Rect::from_min_max(rect.min, Pos2::new(rect.right() - 248.0, rect.bottom()));
        if let Some((text, error, remaining)) = self.canvas.feedback_view() {
            let dismiss = Rect::from_min_size(Pos2::new(rect.right() - 78.0, rect.center().y - 13.0), Vec2::new(78.0, 26.0));
            let text_rect = Rect::from_min_max(Pos2::new(rect.left() + 12.0, rect.top() + 4.0), Pos2::new(dismiss.left() - 12.0, rect.bottom() - 4.0));
            let height = widgets::wrapped_height(ui.ctx(), &text, theme::font(12.0, Weight::Regular), text_rect.width());
            widgets::text_wrapped(
                &painter,
                Rect::from_min_size(Pos2::new(text_rect.left(), rect.center().y - height / 2.0), text_rect.size()),
                &text,
                theme::font(12.0, Weight::Regular),
                if error { theme::ALERT_TEXT } else { theme::TEXT },
                egui::Align::Min,
            );
            if widgets::button(ui, Id::unique("dismiss-message"), dismiss, "Dismiss", ButtonStyle::toolbar(13.0), true, 1.0).clicked() {
                self.canvas.dismiss_message();
            }
            let motion = crate::platform::client_area_animation() && !reduced;
            let fraction = if motion { (remaining / 5.0).clamp(0.0, 1.0) as f32 } else { 1.0 };
            painter.rect_filled(Rect::from_min_size(rect.left_top(), Vec2::new(rect.width() * fraction, 2.0)), 0.0, theme::METER_LOW);
        } else {
            let centre = Pos2::new(rect.left() + (rect.width() + 248.0) / 2.0, rect.center().y);
            let shown = widgets::text_elided(&painter, centre, Align2::CENTER_CENTER, compact_status(&status), theme::font(12.0, Weight::Regular), theme::MUTED, rect.width() - 248.0 - 8.0);
            ui.interact(shown, Id::unique("save-status"), Sense::hover()).tip(&status);
        }
    }

    /// The processing notice strip (12,7 padding, alert text, Dismiss).
    fn processing_notice_text(&self) -> Option<String> {
        let mixer = self.link.state();
        let notice = mixer.processing_notice();
        (!notice.is_empty() && self.dismissed_processing_notice != Some(mixer.processing_notice_sequence())).then(|| notice.to_owned())
    }

    fn processing_notice_height(&self, ui: &Ui, width: f32) -> f32 {
        match self.processing_notice_text() {
            Some(text) => (widgets::wrapped_height(ui.ctx(), &text, theme::font(12.0, Weight::Regular), (width - 24.0 - 12.0 - 78.0).max(40.0)) + 14.0).max(26.0 + 14.0),
            None => 0.0,
        }
    }

    fn processing_notice(&mut self, ui: &mut Ui, rect: Rect) {
        let Some(text) = self.processing_notice_text() else { return };
        let painter = ui.painter().clone();
        painter.rect_filled(rect, 0.0, theme::WINDOW);
        let inner = rect.shrink2(Vec2::new(12.0, 7.0));
        let dismiss_width = widgets::text_width(ui.ctx(), "Dismiss", theme::font(13.0, Weight::Regular)).ceil() + 20.0;
        let dismiss = Rect::from_min_size(Pos2::new(inner.right() - dismiss_width, inner.center().y - 13.0), Vec2::new(dismiss_width, 26.0));
        let text_rect = Rect::from_min_max(inner.min, Pos2::new(dismiss.left() - 12.0, inner.bottom()));
        let height = widgets::wrapped_height(ui.ctx(), &text, theme::font(12.0, Weight::Regular), text_rect.width());
        widgets::text_wrapped(
            &painter,
            Rect::from_min_size(Pos2::new(text_rect.left(), inner.center().y - height / 2.0), text_rect.size()),
            &text,
            theme::font(12.0, Weight::Regular),
            theme::ALERT_TEXT,
            egui::Align::Min,
        );
        let response = widgets::button(ui, Id::unique("processing-dismiss"), dismiss, "Dismiss", ButtonStyle::toolbar(13.0), true, 1.0);
        response.widget_info(|| egui::WidgetInfo::labeled(egui::accesskit::Role::Button, true, "Dismiss audio processing notice"));
        if response.tip("Dismiss this notice. The load reading stays visible.").clicked() {
            let sequence = self.mixer().processing_notice_sequence();
            self.dismissed_processing_notice = Some(sequence);
        }
    }

    /// Audio load details: the same bar, larger, and a few plain sentences.
    fn processing_load_dialog(&mut self, ui: &mut Ui, area: Rect) -> bool {
        let load = self.mixer().processing_load().clone();
        let details = load.details.clone();
        let spec = super::dialog::Spec { footer_gap: 12.0, ..super::dialog::Spec::new("processing-load", 460.0f32.min(area.width() - 32.0)) };
        let closed = super::dialog::show(
            ui,
            area,
            spec,
            |ui| {
                super::dialog::text(ui, "Audio load", 18.0, Weight::SemiBold, theme::TEXT, None);
                ui.add_space(16.0);
                let (row, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), 24.0), Sense::hover());
                let alert = matches!(load.state, "busy" | "overloaded");
                ui.painter().text(row.right_center(), Align2::RIGHT_CENTER, load.value(), theme::font(20.0, Weight::SemiBold), if alert { theme::ALERT_TEXT } else { theme::TEXT });
                let bar = Rect::from_min_max(Pos2::new(row.left(), row.center().y - 7.0), Pos2::new(row.right() - 68.0, row.center().y + 7.0));
                load_bar(ui.painter(), bar, if load.available { load.load as f32 } else { 0.0 }, load.available.then_some(load.peak as f32), load.state);
                let (scale, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), 16.0), Sense::hover());
                let small = theme::font(11.0, Weight::Regular);
                ui.painter().text(Pos2::new(bar.left(), scale.center().y), Align2::LEFT_CENTER, "0%", small.clone(), theme::MUTED);
                ui.painter().text(Pos2::new(bar.right(), scale.center().y), Align2::RIGHT_CENTER, "100%", small, theme::MUTED);
                ui.add_space(12.0);
                super::dialog::text(ui, &details, 13.0, Weight::Regular, theme::TEXT, None);
            },
            |_| {},
        );
        if self.last_load_details != details {
            self.last_load_details = details;
            super::dialog::invalidate(ui.ctx(), "processing-load");
        }
        closed
    }

    fn footer_height(&self, ui: &Ui, width: f32) -> f32 {
        match self.canvas.feedback_view() {
            Some((text, _, _)) => (widgets::wrapped_height(ui.ctx(), &text, theme::font(12.0, Weight::Regular), (width - 248.0 - 102.0).max(40.0)) + 8.0).max(34.0),
            None => 34.0,
        }
    }

    fn recovery_banner(&mut self, ui: &mut Ui, rect: Rect) {
        let (description, can_restore, can_fresh) = {
            let mixer = self.mixer();
            (mixer.recovery_description(), mixer.can_restore_backup(), mixer.can_start_fresh())
        };
        // MainWindow.xaml: 16,12 padding, 16 px SemiBold title, the description
        // 6 px below and 10 px above 14,6-padded buttons 8 px apart.
        let mut child = ui.new_child(egui::UiBuilder::new().max_rect(rect.shrink2(Vec2::new(16.0, 12.0))));
        child.spacing_mut().item_spacing = Vec2::ZERO;
        super::dialog::text(&mut child, "Workspace recovery", 16.0, Weight::SemiBold, theme::TEXT, None);
        child.add_space(6.0);
        super::dialog::text(&mut child, &description, 13.0, Weight::Regular, theme::TEXT, None);
        child.add_space(10.0);
        child.horizontal(|ui| {
            ui.spacing_mut().item_spacing = Vec2::new(8.0, 0.0);
            if super::dialog::button(ui, "Retry", false, true, 14.0, 30.0).clicked() {
                self.link.recover_settings("recoveryRetry");
            }
            if super::dialog::button(ui, "Restore backup", false, can_restore, 14.0, 30.0).clicked() {
                self.link.recover_settings("restoreBackup");
            }
            if super::dialog::button(ui, "Start fresh · preserve originals", false, can_fresh, 14.0, 30.0).clicked() {
                self.link.recover_settings("startFresh");
            }
        });
    }

    fn recovery_height(&self, ui: &Ui, width: f32) -> f32 {
        if !self.mixer().has_settings_recovery() {
            return 0.0;
        }
        let description = self.mixer().recovery_description();
        24.0 + 21.0 + 6.0 + widgets::wrapped_height(ui.ctx(), &description, theme::font(13.0, Weight::Regular), width - 32.0) + 10.0 + 30.0
    }

    #[allow(clippy::too_many_lines)]
    fn run_action(&mut self, ctx: &egui::Context, action: &str) {
        match action {
            "find" => self.open_search(),
            "activity" => {
                let anchor = Pos2::new(ctx.content_rect().right() - 460.0, TITLE_BAR_HEIGHT + 4.0);
                self.canvas.open_history(anchor);
            }
            "retry" => self.link.retry(),
            "restart" => {
                if self.link.state().can_restart_engine() {
                    self.restart_in_progress = true;
                    self.message("Restarting audio engine… Audio will briefly stop.", false);
                    self.link.restart_engine();
                }
            }
            "updates" => {
                self.updates.open(ctx);
                self.overlay = Overlay::Updates;
            }
            "whatsnew" => {
                // A design preview never shows What's new (`OpenWhatsNew`).
                if !self.preview {
                    self.whats_new.open_manual();
                    self.overlay = Overlay::WhatsNew;
                }
            }
            "about" => self.overlay = Overlay::About(false),
            "quit" => self.begin_quit(ctx),
            "sound" => {
                let _ = crate::platform::shell_open("ms-settings:sound");
            }
            "help" => self.open_help(None),
            _ => {}
        }
    }

    fn open_help(&mut self, entry: Option<&'static str>) {
        if matches!(self.overlay, Overlay::Search) {
            return;
        }
        {
            let mut mixer = self.link.state();
            self.canvas.cancel_active_gesture(&mut mixer);
        }
        self.settings_open = false;
        if let (Overlay::Help(state), Some(entry)) = (&mut self.overlay, entry) {
            state.show_entry(entry);
            return;
        }
        self.overlay = Overlay::Help(super::help_view::HelpState::new(entry));
    }

    fn open_search(&mut self) {
        let can = {
            let mixer = self.link.state();
            self.canvas.can_frame_selection(&mixer)
        };
        if matches!(self.overlay, Overlay::Help(_)) || !can {
            return;
        }
        self.settings_open = false;
        self.node_search.begin();
        self.overlay = Overlay::Search;
    }

    /// Window-level shortcuts (`MainWindow.PreviewKeyDown`).
    fn window_shortcuts(&mut self, ctx: &egui::Context) {
        if !self.window_enabled {
            return;
        }
        // Only text editors keep these shortcuts (`IsTextEditor`).
        let text_focused = ctx.text_edit_focused();
        let (ctrl_f, f1, undo, redo, escape) = ctx.input(|i| {
            let ctrl = i.modifiers.ctrl && !i.modifiers.alt;
            (
                ctrl && !i.modifiers.shift && i.key_pressed(Key::F),
                i.modifiers.is_none() && i.key_pressed(Key::F1),
                ctrl && !i.modifiers.shift && i.key_pressed(Key::Z),
                (ctrl && !i.modifiers.shift && i.key_pressed(Key::Y)) || (ctrl && i.modifiers.shift && i.key_pressed(Key::Z)),
                i.key_pressed(Key::Escape),
            )
        });
        if matches!(self.overlay, Overlay::Search) {
            if escape {
                self.overlay = Overlay::None;
            }
            return;
        }
        if ctrl_f && !text_focused {
            self.open_search();
            return;
        }
        if matches!(self.overlay, Overlay::Help(_)) {
            return;
        }
        if f1 && !text_focused {
            self.open_help(None);
            return;
        }
        if !matches!(self.overlay, Overlay::None) {
            return;
        }
        if !text_focused && (undo || redo) {
            self.restore_history(redo);
        }
    }

    /// Modal overlays share one tracked Middle-order area above the
    /// workspace, so their dropdowns, menus and tooltips stay on top.
    fn overlays(&mut self, ui: &mut Ui, area: Rect) {
        if matches!(self.overlay, Overlay::None) {
            return;
        }
        let ctx = ui.ctx().clone();
        egui::Area::new(Id::unique("vinemix-overlay"))
            .order(egui::Order::Middle)
            .fixed_pos(area.min)
            .movable(false)
            .constrain(false)
            .show(&ctx, |ui| {
                ui.set_min_size(area.size());
                ui.set_max_size(area.size());
                self.overlay_contents(ui, area);
            });
    }

    #[allow(clippy::too_many_lines)]
    fn overlay_contents(&mut self, ui: &mut Ui, area: Rect) {
        let ctx = ui.ctx().clone();
        let overlay = std::mem::replace(&mut self.overlay, Overlay::None);
        self.overlay = match overlay {
            Overlay::None => Overlay::None,
            Overlay::Help(mut state) => {
                dim(ui, area);
                let close = super::help_view::show(ui, area, &mut state);
                if close { Overlay::None } else { Overlay::Help(state) }
            }
            Overlay::Search => {
                dim(ui, area);
                // The overlay pads 24,16; the view is at most 580 x 460.
                let room = area.shrink2(Vec2::new(24.0, 16.0));
                let rect = Rect::from_center_size(room.center(), Vec2::new(room.width().min(580.0), room.height().min(460.0)));
                let mut child = ui.new_child(egui::UiBuilder::new().max_rect(rect));
                child.painter().rect_filled(rect, 0.0, theme::PANEL);
                let outcome = {
                    let link = Arc::clone(&self.link);
                    let mixer = link.state();
                    let graph = mixer.graph().clone();
                    drop(mixer);
                    super::node_search::show(&mut child, Id::unique("find-node"), rect, &mut self.node_search, &graph, true, super::node_search::Buttons::Dialog)
                };
                match outcome {
                    super::node_search::Outcome::Close => Overlay::None,
                    super::node_search::Outcome::Choose(id) => {
                        let link = Arc::clone(&self.link);
                        let mut mixer = link.state();
                        if self.canvas.find_and_frame_node(&ctx, &mut mixer, &id) {
                            Overlay::None
                        } else {
                            Overlay::Search
                        }
                    }
                    super::node_search::Outcome::None => Overlay::Search,
                }
            }
            Overlay::About(mut copied) => match super::about::show_about(ui, area, &mut copied) {
                super::about::AboutAction::None => Overlay::About(copied),
                super::about::AboutAction::Close => Overlay::None,
                super::about::AboutAction::Credits => Overlay::Credits(0),
                super::about::AboutAction::Updates => {
                    self.updates.open(ui.ctx());
                    Overlay::Updates
                }
            },
            Overlay::Credits(mut selected) => {
                if super::about::show_credits(ui, area, &mut selected) {
                    Overlay::None
                } else {
                    Overlay::Credits(selected)
                }
            }
            Overlay::WhatsNew => {
                if super::whats_new::show(ui, area, &mut self.whats_new) {
                    if let Some(error) = self.whats_new.dismiss() {
                        self.message(&error, true);
                    }
                    Overlay::None
                } else {
                    Overlay::WhatsNew
                }
            }
            Overlay::ProcessingLoad => {
                if self.processing_load_dialog(ui, area) {
                    Overlay::None
                } else {
                    Overlay::ProcessingLoad
                }
            }
            Overlay::Updates => {
                let mut mixer_link = Arc::clone(&self.link);
                if self.updates.show(ui, area, &mut mixer_link) {
                    Overlay::None
                } else {
                    Overlay::Updates
                }
            }
            Overlay::Quit(result) => {
                // `CreateQuitRecoveryContent`: a 540 px modal window, 22 px margin.
                dim(ui, area);
                let mut next = Some(Overlay::Quit(result.clone()));
                let mut choice = None;
                let spec = super::dialog::Spec { can_close: false, margin: 22.0, footer: false, ..super::dialog::Spec::new("quit-recovery", 540.0) };
                super::dialog::show(
                    ui,
                    area,
                    spec,
                    |ui| {
                        super::dialog::text(ui, "Keep your unsaved changes", 20.0, Weight::SemiBold, theme::TEXT, None);
                        ui.add_space(12.0);
                        super::dialog::text(
                            ui,
                            "Your latest workspace changes have not been saved. VineMix is still open. Retry saving, keep a recovery copy before quitting, or return to the workspace.",
                            13.0,
                            Weight::Regular,
                            theme::TEXT,
                            None,
                        );
                        ui.add_space(10.0);
                        ui.scope(|ui| {
                            super::dialog::text_scroll_style(ui);
                            egui::ScrollArea::vertical().max_height(120.0).auto_shrink([false, true]).id_salt("vinemix-quit-detail").show(ui, |ui| {
                                super::dialog::text(ui, &result.error, 13.0, Weight::Regular, theme::ALERT_TEXT, None);
                            });
                        });
                        ui.add_space(14.0);
                        for (index, (label, help, color)) in [
                            ("Retry saving", "Try saving the queued workspace changes again.", theme::TEXT),
                            (
                                "Save recovery copy and quit…",
                                "Saves the complete workspace and queued edits to a new file, then quits. Existing files are never replaced.",
                                theme::TEXT,
                            ),
                            ("Cancel · keep VineMix open", "Keep the current session open and retain your pending changes.", theme::TEXT),
                            ("Quit without saving", "Stop VineMix and discard changes that were not saved.", theme::MUTE),
                        ]
                        .into_iter()
                        .enumerate()
                        {
                            ui.add_space(4.0);
                            if super::dialog::wide_button(ui, label, color, 34.0).tip(help).clicked() {
                                choice = Some(index);
                            }
                        }
                    },
                    |_| {},
                );
                // Escape is the dialog's cancel button (`IsCancel`).
                if ui.input(|i| i.key_pressed(Key::Escape)) {
                    choice = Some(2);
                }
                match choice {
                    Some(0) => {
                        next = None;
                        self.start_quit_save(&ctx);
                    }
                    Some(1) => match self.save_recovery_copy() {
                        Ok(true) => {
                            next = None;
                            self.stop_and_exit(&ctx);
                        }
                        Ok(false) => {}
                        Err(error) => {
                            next = Some(Overlay::Quit(QuitSaveResult { error: format!("The recovery copy was not saved. {error}"), ..result.clone() }));
                        }
                    },
                    Some(2) => {
                        next = None;
                        self.link.cancel_quit_preparation();
                    }
                    Some(_) => {
                        next = None;
                        self.stop_and_exit(&ctx);
                    }
                    None => {}
                }
                next.unwrap_or(Overlay::None)
            }
        };
    }

    fn resize_borders(&self, ctx: &egui::Context, ui: &mut Ui, rect: Rect) {
        let maximized = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
        if maximized {
            return;
        }
        use egui::viewport::ResizeDirection as D;
        let edge = 5.0;
        let zones = [
            (Rect::from_min_max(rect.left_top(), Pos2::new(rect.left() + edge * 2.0, rect.top() + edge * 2.0)), D::NorthWest, egui::CursorIcon::ResizeNorthWest),
            (Rect::from_min_max(Pos2::new(rect.right() - edge * 2.0, rect.top()), Pos2::new(rect.right(), rect.top() + edge * 2.0)), D::NorthEast, egui::CursorIcon::ResizeNorthEast),
            (Rect::from_min_max(Pos2::new(rect.left(), rect.bottom() - edge * 2.0), Pos2::new(rect.left() + edge * 2.0, rect.bottom())), D::SouthWest, egui::CursorIcon::ResizeSouthWest),
            (Rect::from_min_max(rect.right_bottom() - Vec2::splat(edge * 2.0), rect.right_bottom()), D::SouthEast, egui::CursorIcon::ResizeSouthEast),
            (Rect::from_min_max(rect.left_top(), Pos2::new(rect.right(), rect.top() + edge)), D::North, egui::CursorIcon::ResizeVertical),
            (Rect::from_min_max(Pos2::new(rect.left(), rect.bottom() - edge), rect.right_bottom()), D::South, egui::CursorIcon::ResizeVertical),
            (Rect::from_min_max(rect.left_top(), Pos2::new(rect.left() + edge, rect.bottom())), D::West, egui::CursorIcon::ResizeHorizontal),
            (Rect::from_min_max(Pos2::new(rect.right() - edge, rect.top()), rect.right_bottom()), D::East, egui::CursorIcon::ResizeHorizontal),
        ];
        for (index, (zone, direction, cursor)) in zones.into_iter().enumerate() {
            let response = ui.interact(zone, Id::unique(("resize", index)), Sense::drag());
            if response.hovered() || response.dragged() {
                ctx.set_cursor_icon(cursor);
            }
            if response.drag_started() {
                ctx.send_viewport_cmd(egui::ViewportCommand::BeginResize(direction));
            }
        }
    }
}

/// The overlay backdrop (`#BE000000`), which also swallows pointer input.
fn dim(ui: &mut Ui, area: Rect) {
    ui.painter().rect_filled(area, 0.0, Color32::from_black_alpha(190));
    ui.interact(area, Id::unique("overlay-blocker"), Sense::click_and_drag());
}

impl App {
    /// The automation state dump (development instances only).
    fn automation_dump(&self) -> String {
        let mixer = self.link.state();
        let graph = &mixer.desired().config.graph;
        let mut text = format!(
            "nodes={} edges={} frames={} zoom={:.3} pan=({:.1},{:.1}) selected=[{}] undo={} redo={} overlay={} settings={} connection='{}' save='{}'",
            graph.nodes.len(),
            graph.edges.len(),
            mixer.desired().frames.len(),
            self.canvas.zoom,
            self.canvas.pan.x,
            self.canvas.pan.y,
            self.canvas.selected.join(","),
            mixer.can_undo(),
            mixer.can_redo(),
            match &self.overlay {
                Overlay::None => "none",
                Overlay::Help(_) => "help",
                Overlay::Search => "search",
                Overlay::About(_) => "about",
                Overlay::Credits(_) => "credits",
                Overlay::ProcessingLoad => "processing-load",
                Overlay::WhatsNew => "whats-new",
                Overlay::Quit(_) => "quit",
                Overlay::Updates => "updates",
            },
            self.settings_open,
            mixer.connection_status,
            mixer.save_status,
        );
        if let Some((message, error, _)) = self.canvas.feedback_view() {
            text.push_str(&format!(" message{}='{}'", if error { "(error)" } else { "" }, message.replace('\n', " / ")));
        }
        for node in &graph.nodes {
            text.push_str(&format!("\n  node {} {} '{}' at ({:.0},{:.0}) bypass={} muted={}", node.id, node.kind, node.name, node.x, node.y, node.bypass, node.muted));
        }
        for edge in &graph.edges {
            text.push_str(&format!("\n  edge {}:{} -> {}:{}", edge.from, edge.from_port, edge.to, edge.to_port));
        }
        text
    }
}

impl eframe::App for App {
    fn raw_input_hook(&mut self, ctx: &egui::Context, raw_input: &mut egui::RawInput) {
        if let Some(automation) = self.automation.as_mut() {
            automation.inject(ctx, raw_input);
        }
    }

    fn logic(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.poll_update_health(ctx);
        if self.automation.as_ref().is_some_and(|a| a.dump_requested) {
            let dump = self.automation_dump();
            if let Some(automation) = self.automation.as_mut() {
                automation.dump_requested = false;
                automation.write(&dump);
            }
        }
        if self.automation.as_ref().is_some_and(|a| a.quit_requested) {
            self.automation = None;
            self.allow_close = true;
            self.link.detach();
            self.exit_flag.store(true, Ordering::Release);
            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
        }
        self.handle_commands(ctx);
        self.poll_quit(ctx);
        self.updates.poll(ctx, &self.link);
        if let Some((text, error, notice)) = self.updates.announcement.take() {
            if notice && !error {
                self.canvas.set_notice(&text);
            } else {
                self.message(&text, error);
            }
        }
        if self.updates.exit_requested && !self.quitting {
            self.quitting = true;
            self.allow_close = true;
            self.link.detach();
            self.exit_flag.store(true, Ordering::Release);
            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
        }
        if ctx.input(|i| i.viewport().close_requested()) && !self.allow_close {
            ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
            self.hide_window(ctx);
        }
        let minimized = ctx.input(|i| i.viewport().minimized.unwrap_or(false));
        let became_visible = {
            let mut state = self.link.state();
            let visible = !self.hidden && !minimized;
            let became_visible = visible && !state.visible;
            state.visible = visible;
            // Background preference saves report here even with no engine.
            state.poll_ui_preferences();
            became_visible
        };
        if became_visible {
            self.link.wake();
        }
        // While hidden only logic runs, and everything that needs a frame
        // asks for one: the engine link after each (one-second) poll, the
        // tray, update and quit workers when they finish, and the update
        // schedule for its next deadline. This is only a backstop at the
        // hidden polling cadence, not a four-times-a-second tick.
        if self.hidden {
            ctx.request_repaint_after(std::time::Duration::from_secs(1));
        }
    }

    #[allow(clippy::too_many_lines)]
    fn ui(&mut self, ui: &mut Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        // Keyboard access to hover help, before any control is drawn.
        widgets::help_frame(&ctx);
        if self.background_start {
            self.background_start = false;
            ctx.send_viewport_cmd(egui::ViewportCommand::Visible(false));
        }
        let full = ui.max_rect();
        ui.painter().rect_filled(full, 0.0, theme::WINDOW);
        // The whole window stays inert while an update candidate is checked.
        if !self.window_enabled {
            ui.disable();
        }
        let inner = Rect::from_min_max(Pos2::new(full.left() + 4.0, full.top()), Pos2::new(full.right() - 4.0, full.bottom() - 4.0));
        let title = Rect::from_min_size(inner.min, Vec2::new(inner.width(), TITLE_BAR_HEIGHT));
        // Sync the footer with model warnings.
        {
            let mixer = self.link.state();
            let previous = std::mem::take(&mut self.last_warning);
            self.canvas.sync_warning(&mixer, &previous);
            self.last_warning = mixer.warning.clone();
        }
        let recovery_height = self.recovery_height(ui, inner.width());
        let recovery = Rect::from_min_size(Pos2::new(inner.left(), title.bottom() + 1.0), Vec2::new(inner.width(), recovery_height));
        let toolbar = Rect::from_min_size(Pos2::new(inner.left(), recovery.bottom()), Vec2::new(inner.width(), canvas::TOOLBAR_HEIGHT));
        let footer_height = self.footer_height(ui, inner.width());
        let footer = Rect::from_min_max(Pos2::new(inner.left(), inner.bottom() - footer_height), inner.right_bottom());
        // Load readings refresh twice a second (`_processingLoadTimer`).
        let seconds = self.mixer().seconds();
        if seconds - self.last_load_refresh >= 0.5 {
            self.last_load_refresh = seconds;
            self.mixer().refresh_processing_load(None);
        }
        ctx.request_repaint_after(std::time::Duration::from_millis(500));
        let notice_height = self.processing_notice_height(ui, inner.width());
        let notice = Rect::from_min_max(Pos2::new(inner.left(), footer.top() - notice_height), Pos2::new(inner.right(), footer.top()));
        let viewport = Rect::from_min_max(Pos2::new(inner.left(), toolbar.bottom()), Pos2::new(inner.right(), notice.top()));
        let blocked = !matches!(self.overlay, Overlay::None);
        // Toolbar drag area doubles as caption (as in the original chrome).
        let caption = ui.interact(toolbar, Id::unique("toolbar-drag"), Sense::click_and_drag());
        if caption.drag_started_by(egui::PointerButton::Primary) {
            ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
        }
        if recovery_height > 0.0 {
            self.recovery_banner(ui, recovery);
        }
        {
            let mut ui = ui.new_child(egui::UiBuilder::new().max_rect(toolbar));
            if blocked {
                ui.disable();
            }
            self.toolbar(&mut ui, toolbar);
        }
        {
            let mut mixer = self.link.state();
            let mut canvas_ui = ui.new_child(egui::UiBuilder::new().max_rect(viewport));
            canvas_ui.set_clip_rect(viewport);
            if blocked {
                canvas_ui.disable();
            }
            self.canvas.show(&mut canvas_ui, viewport, &mut mixer);
            mixer.note_spectrum_viewers(super::eq::take_spectrum_viewers(canvas_ui.ctx()));
        }
        self.footer(ui, footer);
        if notice_height > 0.0 {
            self.processing_notice(ui, notice);
        }
        self.title_bar(ui, title);
        self.window_shortcuts(&ctx);
        if matches!(self.overlay, Overlay::None) && !self.settings_open {
            let (can_edit, frames, pending) = {
                let mixer = self.link.state();
                (mixer.can_edit(), mixer.meter_audio_frames(), mixer.pending_edit_count())
            };
            let idle = pending == 0 && !self.canvas.has_active_gesture() && !ctx.text_edit_focused() && !self.canvas.menu_open() && !self.hidden;
            if self.whats_new.tick(can_edit, frames, idle) {
                self.overlay = Overlay::WhatsNew;
            }
        }
        self.overlays(ui, Rect::from_min_max(Pos2::new(inner.left(), title.bottom() + 1.0), inner.max));
        self.resize_borders(&ctx, ui, full);
        if !matches!(self.quit, QuitPhase::Idle) {
            let layer = egui::LayerId::new(egui::Order::Foreground, Id::unique("quit-busy"));
            ctx.layer_painter(layer).rect_filled(full, 0.0, Color32::from_black_alpha(80));
        }
    }

    fn clear_color(&self, _visuals: &egui::Visuals) -> [f32; 4] {
        theme::WINDOW.to_normalized_gamma_f32()
    }

    fn on_exit(&mut self, _gl: Option<&eframe::glow::Context>) {
        if !self.quitting {
            // The window closed without Quit (shutdown or end task): the
            // engine outlives the desktop; only detach.
            self.link.detach();
        }
    }
}

#[cfg(test)]
#[path = "app_tests.rs"]
pub(crate) mod tests;
