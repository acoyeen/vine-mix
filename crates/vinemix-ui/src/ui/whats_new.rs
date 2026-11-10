//! Release highlights shown once after an update (`WhatsNewContent`,
//! `WhatsNewState`, `MainWindow.WhatsNew`).

use std::path::PathBuf;

use eframe::egui::{self, Rect, Ui};
use serde::{Deserialize, Serialize};
use vinemix_update::SemanticVersion;

use super::theme::{self, Weight};

#[derive(Debug)]
pub struct Highlight {
    pub version: &'static str,
    pub title: &'static str,
    pub description: &'static str,
}

const fn h(version: &'static str, title: &'static str, description: &'static str) -> Highlight {
    Highlight { version, title, description }
}

pub const ALL: &[Highlight] = &[
    h("0.3.0-beta.2", "More reliable device lists", "Find streamed microphones and keep assigned devices visible when they become unavailable."),
    h("0.3.0-beta.3", "A steadier workspace", "Normal Windows resizing, improved save retries and safer update recovery."),
    h("0.3.0-beta.4", "Listen through Virtual Desktop", "Choose your streamed headset's Windows playback device in Listening output. Dedicated cable routes keep their own nodes."),
    h("0.3.0-beta.4", "Lock your layout", "Use the top-right padlock to protect node positions and connections. Volume, mute and other sound controls remain available. The lock lasts until you quit."),
    h("0.3.0-beta.4", "Quit with your work protected", "Closing keeps audio running in the tray. Settings now has a red Quit VineMix action, with retry and recovery choices if the final save fails."),
    h("0.3.0-beta.4", "Choose denoising with delay in view", "The algorithm menu shows SpeexDSP at 20 ms and RNNoise at 30 ms. These are VineMix node delays, including bypass, rather than total system latency."),
    h("0.3.0-beta.4", "Clearer workspace feedback", "Save status sits in the centre of the footer. Action messages appear for five seconds with a countdown and a Dismiss option."),
    h("0.3.0-beta.4", "Catch up after updates", "These highlights appear after an updated launch when your workspace is ready. To reopen them, choose Settings → What's new?"),
    h("0.3.0-beta.5", "Updates that pass version checks", "Corrected package metadata prevents the update rejection that could restore your previous beta. You can skip intermediate betas."),
    h("0.3.0-beta.5", "A simpler update window", "See your installed version, release notes and the next action in a compact window that follows your theme."),
    h("0.3.0-beta.5", "Automatic checks keep trying", "Enabling automatic checks schedules a check shortly afterwards. Checks repeat daily and retry after temporary failures. Downloading and installing remain your choice."),
    h("0.4.0-beta", "Organize nodes into named groups", "Select nodes and press Ctrl+G. Colored frames fit their members and move them together. Rename with F2 or a double-click; Undo, Redo and complete-group copying preserve your organization."),
    h("0.4.0-beta", "More ways to mix stereo audio", "Mix, Balance, Mono, Channel Swap, Stereo Width, Pan, Crossfade, Stereo Combine and Stereo Split have their own Mixing category. Split and combine channels or blend two sources without losing their port assignments."),
    h("0.4.0-beta", "One clear Level control", "Level replaces duplicate Volume and Gain controls where both changed the same value. Linked control uses a Multiplier: 100% is unchanged, and 0% is silent."),
    h("0.4.0-beta", "Keep project credit close", "About VineMix now opens Credits and licences, including the full MIT licence and attribution policy offline. Beta releases now advance the version number, starting with 0.4.0 Beta."),
    h("0.4.0-beta", "Click away to apply exact values", "Valid numeric edits apply when you click away. Invalid text stays visible with an error. Right-click for Apply value or Cancel edit; Enter, Tab and Escape keep their existing roles."),
    h("0.4.0-beta", "Aligned Settings search", "The empty search prompt now lines up with the insertion caret and typed text, including at higher display scaling."),
    h("0.4.0-beta", "A quieter footer when you want it", "Turn off Show action notifications in Settings to hide routine timed messages. Errors and save status remain visible, and Activity keeps recent edits and the latest message. Your choice is remembered."),
    h("0.4.0-beta", "Consistent information windows", "What's New, About and Updates share a compact, centred style. Close them with Escape, Close or a click on the workspace; an update installation in progress remains protected."),
    h("0.4.0-beta", "A tidier workspace", "The node count sits beneath the logo. Settings groups workspace controls, audio tools, app preferences and help between familiar separators. Activity sits beside Find node; Help, What's new? and About stay together above the separate Quit action."),
    h("0.4.0-beta", "A matching monochrome identity", "The app logo follows the website's single-colour design. Brighter, slightly larger soft-white icons carry the same identity into the taskbar, tray and shortcuts."),
    h("0.4.0-beta", "Clearer virtual-route setup", "Setup explains which drivers virtual inputs and outputs need, selects missing drivers and installs VAC Lite before VB-CABLE. You can clear a selection; driver terms still require explicit acceptance."),
    h("0.5.0-beta", "A ready-to-connect voice chain", "New workspaces start with High-pass, RNNoise Denoiser, Expander and Compressor feeding two muted outputs. Choose your microphone and listening device when ready. Existing workspaces stay intact; RNNoise adds 30 ms of delay."),
    h("0.5.0-beta", "See more of your workspace", "Zoom out to 10% with quiet node blocks and a corner minimap. Click or drag the map to navigate, or turn off Show minimap in Settings. Ctrl+plus and Ctrl+minus also zoom the canvas."),
    h("0.5.0-beta", "Move through nodes with the keyboard", "Ctrl+Tab follows an audio pipeline; Ctrl+Shift+Tab goes back. Arrow keys nudge selected nodes or pan an empty selection, and F2 renames one selected node or group. Type on a focused dial to enter an exact value."),
    h("0.5.0-beta", "Echo and Distortion", "Echo adds one to eight fading repeats, with Time, Repeats and Mix controls plus a separate echoes-only output. Distortion adds warmth or stronger saturation with Drive, Tone, Mix and Output. Both live in the single Effects list."),
    h("0.5.0-beta", "More flexible effect routing", "Gate and Expander accept an optional Trigger input to detect another signal. Reverb's separate Wet output carries only its tail, independently of Mix, for further processing or blending."),
    h("0.5.0-beta", "Clearer cables and meters", "Meters blend smoothly through green, yellow and red. Cables blend their socket colours, effect returns use light blue, and short connections curve naturally. Midpoint markers follow detail level and disappear on disabled connections."),
    h("0.5.0-beta", "Independent routes to the same output", "Several output nodes can share a destination and keep separate levels and mutes. Duplicate output preserves its device but starts muted. Overload indicators also account for the combined signal."),
    h("0.5.0-beta", "More reliable workspace edits", "Fixed an unwanted Undo reset after level changes and clarified device-status handling. Renamed nodes update their accessibility labels while Find node still recognises the original node type."),
];

/// Highlights after `previous` up to `current`.
pub fn since(previous: Option<&str>, current: &str) -> Vec<&'static Highlight> {
    let Some(limit) = SemanticVersion::parse(current) else { return Vec::new() };
    let baseline = previous.and_then(SemanticVersion::parse);
    ALL.iter()
        .filter(|item| SemanticVersion::parse(item.version).is_some_and(|v| v <= limit && baseline.as_ref().is_none_or(|b| v > *b)))
        .collect()
}

#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
struct Progress {
    format_version: i32,
    baseline_release_version: Option<String>,
    last_seen_release_version: Option<String>,
}

fn path() -> PathBuf {
    crate::paths::data_directory().join("updates").join("whats-new.json")
}

/// What's New progress and automatic presentation.
#[derive(Debug, Default)]
pub struct WhatsNew {
    progress: Progress,
    pending: bool,
    existing: bool,
    first_frames: Option<u64>,
    done: bool,
    automatic: bool,
    preview: bool,
}

impl WhatsNew {
    pub fn begin(existing_workspace: bool, preview: bool) -> Self {
        let mut progress: Progress = std::fs::read(path()).ok().and_then(|b| serde_json::from_slice(&b).ok()).unwrap_or_default();
        if progress.format_version != 1 && progress.format_version != 0 {
            progress = Progress::default();
        }
        progress.format_version = 1;
        let current = vinemix_platform::release::VERSION;
        let previous = progress.last_seen_release_version.clone().or_else(|| progress.baseline_release_version.clone());
        let pending = match (SemanticVersion::parse(current), previous.as_deref().and_then(SemanticVersion::parse)) {
            (Some(version), Some(previous)) => version > previous,
            (Some(_), None) => existing_workspace,
            _ => false,
        };
        Self { progress, pending, existing: existing_workspace, first_frames: None, done: preview, automatic: false, preview }
    }

    /// Nothing pending and nothing read from or written to disk (tests).
    #[cfg(test)]
    pub(crate) fn inert() -> Self {
        Self { progress: Progress::default(), pending: false, existing: false, first_frames: None, done: true, automatic: false, preview: true }
    }

    pub fn should_show_now(&self) -> bool {
        false
    }

    fn previous(&self) -> Option<String> {
        self.progress.last_seen_release_version.clone().or_else(|| self.progress.baseline_release_version.clone())
    }

    fn save(&mut self, progress: Progress) -> Result<(), String> {
        let path = path();
        let directory = path.parent().ok_or("No update folder.")?;
        // The update root must stay owner-only (`UpdateFiles.CreatePrivateDirectory`):
        // pending-update recovery refuses a folder other accounts can reach.
        crate::update::files::create_private_directory(directory)?;
        let temporary = directory.join(format!(".whats-new-{}.tmp", crate::platform::random_hex_id()));
        let json = serde_json::to_string(&progress).map_err(|e| e.to_string())?;
        let result = std::fs::write(&temporary, json).and_then(|()| std::fs::rename(&temporary, &path)).map_err(|e| e.to_string());
        let _ = std::fs::remove_file(&temporary);
        result?;
        self.progress = progress;
        Ok(())
    }

    /// Called every frame: after audio has advanced on a healthy engine and
    /// the window is idle, presents automatically once.
    pub fn tick(&mut self, can_edit: bool, frames: u64, idle: bool) -> bool {
        if self.done || self.preview {
            return false;
        }
        if !can_edit {
            self.first_frames = None;
            return false;
        }
        let Some(first) = self.first_frames else {
            self.first_frames = Some(frames);
            return false;
        };
        if frames <= first {
            return false;
        }
        if !self.pending {
            if !self.existing && self.previous().is_none() {
                let _ = self.save(Progress { format_version: 1, baseline_release_version: Some(vinemix_platform::release::VERSION.into()), ..Progress::default() });
            }
            self.done = true;
            return false;
        }
        if !idle {
            return false;
        }
        self.done = true;
        self.automatic = true;
        true
    }

    pub fn open_manual(&mut self) {
        self.automatic = false;
    }

    /// Records the dismissal; returns an error message when it couldn't save.
    pub fn dismiss(&mut self) -> Option<String> {
        self.pending = false;
        self.done = true;
        let current = vinemix_platform::release::VERSION;
        if let (Some(previous), Some(version)) = (self.previous().as_deref().and_then(SemanticVersion::parse), SemanticVersion::parse(current)) {
            if previous > version {
                return None;
            }
        }
        let mut progress = self.progress.clone();
        progress.format_version = 1;
        progress.last_seen_release_version = Some(current.into());
        self.save(progress).err().map(|_| "What's new was closed, but its reminder could not be saved. It may appear next time.".into())
    }

    pub fn highlights(&self) -> Vec<&'static Highlight> {
        since(if self.automatic { self.previous() } else { None }.as_deref(), vinemix_platform::release::VERSION)
    }
}

/// Draws the window (`CreateWhatsNewContent`, 580 x 560); returns true when
/// closed.
pub fn show(ui: &mut Ui, area: Rect, state: &mut WhatsNew) -> bool {
    let spec = super::dialog::Spec { height: Some(560.0), footer_gap: 12.0 + 10.0, ..super::dialog::Spec::new("whats-new", 580.0) };
    let highlights = state.highlights();
    super::dialog::show(
        ui,
        area,
        spec,
        |ui| {
            super::dialog::heading(ui, "What's new");
            ui.add_space(5.0);
            super::dialog::text(ui, vinemix_platform::release::DISPLAY_VERSION, 12.0, Weight::Regular, theme::MUTED, None);
            ui.add_space(24.0);
            let mut groups: Vec<(&str, Vec<&Highlight>)> = Vec::new();
            for item in &highlights {
                match groups.iter_mut().find(|(v, _)| *v == item.version) {
                    Some((_, list)) => list.push(item),
                    None => groups.push((item.version, vec![item])),
                }
            }
            super::dialog::text_scroll_style(ui);
            egui::ScrollArea::vertical().auto_shrink([false, false]).scroll_bar_visibility(egui::scroll_area::ScrollBarVisibility::VisibleWhenNeeded).id_salt("vinemix-whats-new").show(ui, |ui| {
                ui.spacing_mut().item_spacing = egui::Vec2::ZERO;
                if highlights.is_empty() {
                    super::dialog::text(ui, "You're up to date with the bundled highlights.", 13.0, Weight::Regular, theme::TEXT, None);
                }
                for (_, items) in groups.iter().rev() {
                    for item in items {
                        super::dialog::text(ui, item.title, 15.0, Weight::SemiBold, theme::TEXT, None);
                        ui.add_space(5.0);
                        super::dialog::text(ui, item.description, 13.0, Weight::Regular, theme::MUTED, Some(20.0));
                        ui.add_space(22.0);
                    }
                }
            });
        },
        |_| {},
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn highlights_since() {
        assert_eq!(since(Some("0.4.0-beta"), "0.5.0-beta").len(), 8);
        assert_eq!(since(None, "0.3.0-beta.2").len(), 1);
        assert!(since(Some("0.5.0-beta"), "0.5.0-beta").is_empty());
    }
}
