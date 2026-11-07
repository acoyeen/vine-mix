//! Updates window and automatic checks (`UpdateController`,
//! `MainWindow.Updates`, `AutomaticUpdateSchedule`).
//!
//! Checking and downloading never interrupt audio. Install and restart
//! saves the workspace, stages a private signed transaction, starts the
//! update worker, stops the verified engine cleanly and exits; any failure
//! keeps the current session open.

use crate::ui::widgets::Tip as _;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::mpsc::Receiver;
use std::sync::Arc;
use std::time::{Duration, Instant};

use eframe::egui::{self, Id, Rect, Ui, Vec2};
use vinemix_update::{CheckResult, Downloaded, Failure, Offer};

use super::dialog::{self, Spec};
use super::theme::{self, Weight};
use super::widgets;
use crate::link::EngineLink;

enum Job {
    Check(Receiver<Result<CheckResult, Failure>>, bool),
    Download(Receiver<Result<Downloaded, Failure>>),
    Install(Receiver<Result<(), String>>),
}

pub struct UpdatesUi {
    message: String,
    offer: Option<Offer>,
    download: Option<Downloaded>,
    job: Option<Job>,
    cancel: Arc<AtomicBool>,
    progress: Arc<(AtomicU64, AtomicU64)>,
    next_automatic: Option<Instant>,
    /// The first scheduled check after startup honours the daily stamp.
    respect_stamp: bool,
    automatic_enabled: Option<bool>,
    /// Report the next automatic result (the preference was just turned on).
    show_automatic_result: bool,
    /// A design preview never contacts GitHub (`UpdateController(preview)`).
    preview: bool,
    pub exit_requested: bool,
    /// (text, error, notice): a notice shows even with action notifications off.
    pub announcement: Option<(String, bool, bool)>,
}

impl std::fmt::Debug for UpdatesUi {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("UpdatesUi").field("message", &self.message).finish_non_exhaustive()
    }
}

impl Default for UpdatesUi {
    fn default() -> Self {
        Self {
            message: "Check for a newer VineMix release on GitHub.".into(),
            offer: None,
            download: None,
            job: None,
            cancel: Arc::new(AtomicBool::new(false)),
            progress: Arc::new((AtomicU64::new(0), AtomicU64::new(0))),
            next_automatic: None,
            respect_stamp: false,
            automatic_enabled: None,
            show_automatic_result: false,
            preview: false,
            exit_requested: false,
            announcement: None,
        }
    }
}

fn updates_root() -> std::path::PathBuf {
    crate::paths::data_directory().join("updates")
}

/// `last-successful-check.txt`: the round-trip UTC time of the last automatic
/// check that reached GitHub (shared with 0.5).
fn stamp_path() -> std::path::PathBuf {
    updates_root().join("last-successful-check.txt")
}

fn read_stamp() -> Option<crate::update::model::UtcTime> {
    let text = std::fs::read_to_string(stamp_path()).ok()?;
    crate::update::model::UtcTime::parse(text.trim())
}

fn write_stamp() {
    // Keep the in-memory daily cadence when storage is unavailable.
    if crate::update::files::create_private_directory(&updates_root()).is_ok() {
        let _ = std::fs::write(stamp_path(), crate::update::model::UtcTime::now().format());
    }
}

const DAILY: Duration = Duration::from_secs(24 * 3600);
const RETRY: Duration = Duration::from_secs(15 * 60);

impl UpdatesUi {
    pub fn is_busy(&self) -> bool {
        self.job.is_some()
    }

    /// A verified newer release found by any check and not yet installed.
    pub fn available_version(&self) -> Option<String> {
        self.offer.as_ref().filter(|_| !self.installing()).map(|o| o.manifest.version.clone())
    }

    pub fn set_preview(&mut self, preview: bool) {
        self.preview = preview;
    }

    /// Opening the window starts a check (`OpenUpdates` -> `CheckAsync`).
    pub fn open(&mut self, ctx: &egui::Context) {
        self.start_check(ctx, false);
    }

    fn installing(&self) -> bool {
        matches!(self.job, Some(Job::Install(_)))
    }

    /// The preference changed in Settings or here.
    pub fn preference_changed(&mut self, enabled: bool) {
        self.automatic_enabled = Some(enabled);
        self.show_automatic_result = enabled;
        self.respect_stamp = false;
        self.next_automatic = (enabled && !self.preview).then(|| Instant::now() + Duration::from_secs(2));
        self.announcement = Some((
            if enabled {
                "Automatic update checks are on. Checking shortly…".into()
            } else {
                "Automatic update checks are off. You can still check from Settings.".into()
            },
            false,
            false,
        ));
    }

    fn start_check(&mut self, ctx: &egui::Context, automatic: bool) {
        if self.job.is_some() {
            return;
        }
        if self.preview {
            self.message = "Preview only · update checks are not sent to GitHub.".into();
            return;
        }
        self.message = "Checking GitHub for updates…".into();
        self.cancel.store(false, Ordering::Release);
        let (sender, receiver) = std::sync::mpsc::channel();
        let cancel = Arc::clone(&self.cancel);
        let repaint = ctx.clone();
        std::thread::spawn(move || {
            let current = vinemix_platform::release::VERSION;
            let beta = vinemix_update::SemanticVersion::parse(current).is_some_and(|v| v.is_prerelease());
            let result = vinemix_update::Trust::production()
                .map_err(Failure::Invalid)
                .and_then(|trust| vinemix_update::check(&trust, current, beta, vinemix_update::Compatibility::current(), &|| cancel.load(Ordering::Acquire)));
            let _ = sender.send(result);
            repaint.request_repaint();
        });
        self.job = Some(Job::Check(receiver, automatic));
    }

    fn start_download(&mut self, ctx: &egui::Context) {
        let Some(offer) = self.offer.clone() else { return };
        if self.job.is_some() || self.download.is_some() || self.preview {
            return;
        }
        self.message = "Downloading and verifying the update… You can keep using your audio.".into();
        self.cancel.store(false, Ordering::Release);
        self.progress.0.store(0, Ordering::Release);
        self.progress.1.store(offer.manifest.installer.size, Ordering::Release);
        let (sender, receiver) = std::sync::mpsc::channel();
        let cancel = Arc::clone(&self.cancel);
        let progress = Arc::clone(&self.progress);
        let repaint = ctx.clone();
        std::thread::spawn(move || {
            // Downloads inherit the owner-only update root (`DownloadAsync`).
            if let Err(error) = crate::update::files::create_private_directory(&updates_root()) {
                let _ = sender.send(Err(Failure::Unavailable(error)));
                repaint.request_repaint();
                return;
            }
            let parent = updates_root().join("downloads");
            let result = vinemix_update::download(
                &offer,
                if crate::runtime::standalone() { vinemix_update::AssetKind::Standalone } else { vinemix_update::AssetKind::Installer },
                &parent,
                &|done, total| {
                    progress.0.store(done, Ordering::Release);
                    progress.1.store(total, Ordering::Release);
                    repaint.request_repaint();
                },
                &|| cancel.load(Ordering::Acquire),
            );
            let _ = sender.send(result);
            repaint.request_repaint();
        });
        self.job = Some(Job::Download(receiver));
    }

    fn start_install(&mut self, ctx: &egui::Context, link: &Arc<EngineLink>) {
        let Some(download) = self.download.clone() else { return };
        let Some(offer) = self.offer.clone() else { return };
        if self.job.is_some() || self.preview {
            return;
        }
        if vinemix_platform::development_instance().is_some() {
            self.message = "Development instances do not install updates.".into();
            return;
        }
        self.message = "Saving your workspace and preparing the update…".into();
        let (sender, receiver) = std::sync::mpsc::channel();
        let link = Arc::clone(link);
        let repaint = ctx.clone();
        std::thread::spawn(move || {
            let result = crate::update::session::install(&link, &download, &offer);
            let _ = sender.send(result);
            repaint.request_repaint();
        });
        self.job = Some(Job::Install(receiver));
    }

    /// Polls background work and the automatic schedule every frame.
    pub fn poll(&mut self, ctx: &egui::Context, link: &Arc<EngineLink>) {
        // `AutomaticUpdateSchedule`: 30 s after startup (deferred until a day
        // after the last successful check), then daily; 15 minutes after a
        // failed check; one minute later while a manual operation is busy.
        let enabled = link.state().automatically_check_for_updates() && vinemix_platform::development_instance().is_none() && !self.preview;
        if self.automatic_enabled != Some(enabled) {
            self.automatic_enabled = Some(enabled);
            self.respect_stamp = enabled;
            self.next_automatic = enabled.then(|| Instant::now() + Duration::from_secs(30));
        }
        if enabled && self.next_automatic.is_some_and(|t| Instant::now() >= t) {
            if std::mem::take(&mut self.respect_stamp) {
                if let Some(last) = read_stamp() {
                    let age = (crate::update::model::UtcTime::now().0 - last.0) / 10_000_000;
                    // Future timestamps cannot defer checking after a clock change.
                    if (0..DAILY.as_secs() as i64).contains(&age) {
                        self.next_automatic = Some(Instant::now() + DAILY - Duration::from_secs(age as u64));
                    }
                }
            }
            if self.next_automatic.is_some_and(|t| Instant::now() >= t) {
                if self.job.is_some() {
                    self.next_automatic = Some(Instant::now() + Duration::from_secs(60));
                } else {
                    self.next_automatic = None;
                    self.start_check(ctx, true);
                }
            }
        }
        if let Some(next) = self.next_automatic {
            ctx.request_repaint_after(next.saturating_duration_since(Instant::now()).min(Duration::from_secs(60)));
        }
        let Some(job) = self.job.take() else { return };
        match job {
            Job::Check(receiver, automatic) => match receiver.try_recv() {
                Ok(result) => {
                    if self.finish_check(result, automatic, enabled) {
                        write_stamp();
                    }
                }
                Err(std::sync::mpsc::TryRecvError::Empty) => self.job = Some(Job::Check(receiver, automatic)),
                Err(_) => {}
            },
            Job::Download(receiver) => match receiver.try_recv() {
                Ok(Ok(download)) => {
                    let version = self.offer.as_ref().map(|o| o.manifest.version.clone()).unwrap_or_default();
                    self.message = format!("VineMix {version} is ready. Install and restart will briefly stop audio, then reopen your saved workspace.");
                    self.download = Some(download);
                }
                Ok(Err(Failure::Cancelled)) => self.message = "Download cancelled or timed out. Your current version is still running.".into(),
                Ok(Err(failure)) => self.message = format!("The update was not installed. {}", failure.message()),
                Err(std::sync::mpsc::TryRecvError::Empty) => self.job = Some(Job::Download(receiver)),
                Err(_) => {}
            },
            Job::Install(receiver) => match receiver.try_recv() {
                Ok(Ok(())) => {
                    // The worker now owns the update; the engine has stopped.
                    self.message = "Installing the update…".into();
                    self.exit_requested = true;
                }
                Ok(Err(error)) => self.message = format!("The update did not finish. {error}"),
                Err(std::sync::mpsc::TryRecvError::Empty) => self.job = Some(Job::Install(receiver)),
                Err(_) => {}
            },
        }
    }

    /// Applies a finished check; returns true when an automatic check reached
    /// GitHub (the caller records `last-successful-check.txt`).
    fn finish_check(&mut self, result: Result<CheckResult, Failure>, automatic: bool, enabled: bool) -> bool {
        let success = result.is_ok();
        let cancelled = matches!(result, Err(Failure::Cancelled));
        let available = matches!(result, Ok(CheckResult::Available(_)));
        match result {
            Ok(CheckResult::Available(offer)) => {
                if self.offer.as_ref().is_none_or(|o| o.envelope_sha256 != offer.envelope_sha256) {
                    self.download = None;
                }
                self.message = format!("VineMix {} is available.", offer.manifest.version);
                self.offer = Some(*offer);
            }
            Ok(CheckResult::UpToDate(message)) => {
                self.offer = None;
                self.download = None;
                self.message = message;
            }
            Err(Failure::Cancelled) => {
                self.message = if automatic { "Background check cancelled. You can check for updates at any time.".into() } else { "Update check cancelled.".into() };
            }
            Err(failure) => self.message = format!("Could not check for updates. {}", failure.message()),
        }
        if !automatic {
            return false;
        }
        if available {
            // Shown even with action notifications off; Settings also keeps a
            // marker until the update is handled.
            let version = self.offer.as_ref().map(|o| o.manifest.version.clone()).unwrap_or_default();
            self.announcement = Some((format!("VineMix {version} is available. Open Settings → Update available to download it."), false, true));
        } else if self.show_automatic_result && !cancelled {
            self.announcement = Some((
                if success {
                    "VineMix is up to date. Automatic checks are on.".into()
                } else {
                    "The automatic update check could not finish. It will retry in 15 minutes; you can also check from Settings.".into()
                },
                false,
                true,
            ));
        }
        self.show_automatic_result = false;
        if enabled {
            self.next_automatic = Some(Instant::now() + if success { DAILY } else { RETRY });
        }
        success
    }

    /// Draws the window (`CreateUpdateContent`, 500 px); returns true when
    /// closed. Closing cancels a check or download, never an install.
    pub fn show(&mut self, ui: &mut Ui, area: Rect, link: &mut Arc<EngineLink>) -> bool {
        let ctx = ui.ctx().clone();
        let installing = self.installing();
        let busy = self.job.is_some();
        let has_offer = self.offer.is_some();
        let downloaded = self.download.is_some();
        let notes = self.offer.as_ref().map(|o| o.manifest.notes.clone()).unwrap_or_default();
        let message = self.message.clone();
        let preview = self.preview;
        let spec = Spec { can_close: !installing, footer_gap: 22.0, ..Spec::new("updates", 500.0) };
        let mut check = false;
        let mut download = false;
        let mut install = false;
        let mut cancel = false;
        let mut toggled = None;
        let closed = dialog::show(
            ui,
            area,
            spec,
            |ui| {
                dialog::heading(ui, "Updates");
                ui.add_space(5.0);
                dialog::text(ui, &format!("Installed · {}", vinemix_platform::release::DISPLAY_VERSION), 12.0, Weight::Regular, theme::MUTED, None);
                ui.add_space(24.0);
                dialog::text(ui, &message, 14.0, Weight::Regular, theme::TEXT, Some(21.0));
                if busy {
                    ui.add_space(14.0);
                    dialog::indeterminate(ui);
                }
                if !notes.is_empty() {
                    ui.add_space(22.0);
                    dialog::text(ui, "What's new", 12.0, Weight::SemiBold, theme::MUTED, None);
                    ui.add_space(8.0);
                    ui.scope(|ui| {
                        dialog::text_scroll_style(ui);
                        egui::ScrollArea::vertical().max_height(180.0).auto_shrink([false, true]).id_salt("vinemix-update-notes").show(ui, |ui| {
                            dialog::selectable(ui, &notes, 13.0, theme::TEXT);
                        });
                    });
                }
                ui.add_space(22.0);
                let top = ui.cursor().top();
                ui.painter().hline(ui.max_rect().x_range(), top + 0.5, egui::Stroke::new(1.0, theme::BORDER));
                ui.add_space(1.0 + 16.0);
                let automatic = link.state().automatically_check_for_updates();
                if widgets::checkbox(ui, automatic, "Check automatically", !installing)
                    .tip("Checks daily while VineMix is running. Failed checks retry automatically. You choose when to download and install.")
                    .clicked()
                {
                    toggled = Some(!automatic);
                }
                ui.add_space(2.0);
                let (row, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), 0.0), egui::Sense::hover());
                let mut explanation = ui.new_child(egui::UiBuilder::new().max_rect(Rect::from_min_max(row.min + Vec2::new(25.0, 0.0), ui.max_rect().right_bottom())));
                dialog::text(&mut explanation, "Checks daily. You choose when to download and install.", 12.0, Weight::Regular, theme::MUTED, None);
                ui.advance_cursor_after_rect(explanation.min_rect());
            },
            |ui| {
                // Visual order: Check, Download, Install, Cancel (right-aligned).
                if busy && !installing {
                    cancel |= dialog::button(ui, "Cancel", false, true, 13.0, 34.0)
                        .tip("Cancel the update check or download. Your current version keeps running.")
                        .clicked();
                }
                if !busy && downloaded {
                    install |= dialog::button(ui, "Install and restart", true, !preview, 13.0, 34.0)
                        .tip("Save your workspace, briefly stop audio, install the verified update and reopen VineMix.")
                        .clicked();
                }
                if !busy && has_offer && !downloaded {
                    download |= dialog::button(ui, "Download update", true, !preview, 13.0, 34.0).clicked();
                }
                if !busy {
                    check |= if has_offer { quiet_button(ui, "Check for updates") } else { dialog::button(ui, "Check for updates", true, true, 13.0, 34.0) }.clicked();
                }
            },
        );
        if let Some(value) = toggled {
            link.state().set_automatically_check_for_updates(value);
            self.preference_changed(value);
        }
        if check {
            self.start_check(&ctx, false);
        }
        if download {
            self.start_download(&ctx);
        }
        if install {
            self.start_install(&ctx, link);
        }
        if cancel {
            self.cancel.store(true, Ordering::Release);
        }
        if self.job.is_some() != busy || self.message != message || self.offer.is_some() != has_offer {
            dialog::invalidate(&ctx, "updates");
        }
        if closed && !installing {
            self.cancel.store(true, Ordering::Release);
            return true;
        }
        false
    }
}

/// "Check for updates" once an offer exists: transparent, muted, no border.
fn quiet_button(ui: &mut Ui, label: &str) -> egui::Response {
    let width = widgets::text_width(ui.ctx(), label, theme::font(13.0, Weight::Regular)).ceil() + 26.0;
    let (rect, _) = ui.allocate_exact_size(Vec2::new(width, 34.0), egui::Sense::hover());
    let style = widgets::ButtonStyle { fill: egui::Color32::TRANSPARENT, text: theme::MUTED, border: egui::Color32::TRANSPARENT, border_width: 0.0, ..widgets::ButtonStyle::console(13.0) };
    widgets::button(ui, Id::unique(("dialog-button", label)), rect, label, style, true, 1.0)
}

#[cfg(test)]
mod tests {
    use super::*;
    use vinemix_update::testing::{manifest_payload, TestKey};

    fn offer(version: &str) -> Offer {
        let key = TestKey::generate().unwrap();
        let payload = manifest_payload(version, "9.1.0.0", (1234, &"ab".repeat(32)), (5678, &"cd".repeat(32)));
        let envelope = key.envelope("test", &payload).unwrap();
        key.trust("test").verify(&envelope, version, vinemix_update::Compatibility::current()).unwrap()
    }

    #[test]
    fn automatic_update_found_is_announced_and_kept() {
        let mut updates = UpdatesUi::default();
        assert!(updates.finish_check(Ok(CheckResult::Available(Box::new(offer("9.1.0-beta")))), true, true));
        assert_eq!(updates.available_version().as_deref(), Some("9.1.0-beta"));
        let (text, error, notice) = updates.announcement.take().unwrap();
        assert!(text.contains("9.1.0-beta") && !error && notice, "{text}");
        assert!(updates.next_automatic.is_some());
        // A later "up to date" result clears the marker.
        assert!(updates.finish_check(Ok(CheckResult::UpToDate("Up to date.".into())), true, true));
        assert!(updates.available_version().is_none());
        assert!(updates.announcement.is_none(), "quiet when the user did not just enable checks");
    }

    #[test]
    fn enabling_reports_the_next_result_once() {
        let mut updates = UpdatesUi::default();
        updates.preference_changed(true);
        assert!(updates.announcement.take().unwrap().0.contains("Checking shortly"));
        assert!(!updates.finish_check(Err(Failure::Unavailable("offline".into())), true, true));
        let (text, _, notice) = updates.announcement.take().unwrap();
        assert!(text.contains("retry in 15 minutes") && notice);
        assert!(!updates.finish_check(Err(Failure::Unavailable("offline".into())), true, true));
        assert!(updates.announcement.is_none());
    }

    #[test]
    fn manual_checks_never_record_the_daily_stamp() {
        let mut updates = UpdatesUi::default();
        assert!(!updates.finish_check(Ok(CheckResult::UpToDate("Up to date.".into())), false, true));
        assert!(updates.announcement.is_none());
    }

    #[test]
    fn preview_never_contacts_github() {
        let mut updates = UpdatesUi::default();
        updates.set_preview(true);
        updates.open(&egui::Context::default());
        assert!(!updates.is_busy());
        assert_eq!(updates.message, "Preview only · update checks are not sent to GitHub.");
    }
}
