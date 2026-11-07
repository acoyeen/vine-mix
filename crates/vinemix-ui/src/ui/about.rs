//! About VineMix and Credits and licences (`MainWindow.About`,
//! `CreditsContent`, `DistributionNotices`).

use crate::ui::widgets::Tip as _;
use eframe::egui::{self, Id, Rect, Sense, Stroke, Ui, Vec2};

use super::dialog::{self, Spec};
use super::theme::{self, Weight};

pub const REPOSITORY: &str = "https://github.com/acoyeen/vine-mix";
pub const ATTRIBUTION: &str = "https://vinemix.app/attribution";

const LICENSE: &str = include_str!("../../../../LICENSE");
const ATTRIBUTION_TEXT: &str = include_str!("../../../../ATTRIBUTION.md");
const NOTICES: &str = include_str!("../../../../THIRD_PARTY_NOTICES.md");

/// The embedded distribution notices (`DistributionNotices.FileNames`).
pub const NOTICE_FILES: [&str; 3] = ["LICENSE", "ATTRIBUTION.md", "THIRD_PARTY_NOTICES.md"];

/// A notice's exact embedded text.
pub fn notice_text(name: &str) -> Option<&'static str> {
    match name {
        "LICENSE" => Some(LICENSE),
        "ATTRIBUTION.md" => Some(ATTRIBUTION_TEXT),
        "THIRD_PARTY_NOTICES.md" => Some(NOTICES),
        _ => None,
    }
}

/// Version and build information for bug reports (`ReleaseInfo.FullInformation`).
/// The .NET runtime line becomes the Rust toolchain line.
pub fn full_information() -> String {
    let revision = option_env!("VINEMIX_SOURCE_REVISION").unwrap_or("unavailable");
    let build = match option_env!("VINEMIX_SOURCE_REVISION") {
        Some(revision) => format!("{}+{revision}", vinemix_platform::release::VERSION),
        None => vinemix_platform::release::VERSION.to_owned(),
    };
    let platform = match std::env::consts::ARCH {
        "x86_64" => "X64",
        "aarch64" => "Arm64",
        "x86" => "X86",
        other => other,
    };
    format!(
        "VineMix {}
Release: {}
File version: {}
Build: {build}
Source revision: {revision}
Source snapshot SHA-256: {}
Built at (UTC): {}
Configuration: {}
Distribution: {}
Platform: Windows {platform}
Rust: {}",
        vinemix_platform::release::DISPLAY_VERSION,
        vinemix_platform::release::VERSION,
        vinemix_platform::release::NUMERIC_VERSION,
        option_env!("VINEMIX_SOURCE_SNAPSHOT_SHA256").unwrap_or("unavailable"),
        option_env!("VINEMIX_BUILD_TIMESTAMP").unwrap_or("unavailable"),
        if cfg!(debug_assertions) { "Debug" } else { "Release" },
        crate::runtime::build_mode(),
        option_env!("VINEMIX_RUSTC_VERSION").unwrap_or("stable"),
    )
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum AboutAction {
    None,
    Close,
    Credits,
    Updates,
}

/// About VineMix (`CreateAboutContent`, 520 px window).
pub fn show_about(ui: &mut Ui, area: Rect, copied: &mut bool) -> AboutAction {
    let mut credits = false;
    let mut updates = false;
    let information = full_information();
    let spec = Spec { footer_gap: 6.0, ..Spec::new("about", 520.0) };
    let closed = dialog::show(
        ui,
        area,
        spec,
        |ui| {
            dialog::heading(ui, "About VineMix");
            ui.add_space(5.0);
            dialog::text(ui, "Local audio routing · Beta", 12.0, Weight::Regular, theme::MUTED, None);
            ui.add_space(24.0);
            dialog::selectable(ui, &information, 13.0, theme::TEXT);
            ui.add_space(16.0);
            dialog::text(ui, "VineMix by acoyeen", 13.0, Weight::Regular, theme::TEXT, None);
            ui.add_space(8.0);
            if dialog::button(ui, "Credits and licences", false, true, 12.0, 34.0)
                .tip("Read the MIT licence, attribution policy and third-party notices offline.")
                .clicked()
            {
                credits = true;
            }
            ui.add_space(14.0);
        },
        |ui| {
            if dialog::button(ui, if *copied { "Copied" } else { "Copy version information" }, false, true, 12.0, 34.0).clicked() {
                ui.ctx().copy_text(information.clone());
                *copied = true;
            }
            if dialog::button(ui, "Check for updates", false, true, 12.0, 34.0).clicked() {
                updates = true;
            }
        },
    );
    if credits {
        AboutAction::Credits
    } else if updates {
        AboutAction::Updates
    } else if closed {
        AboutAction::Close
    } else {
        AboutAction::None
    }
}

/// Markdown simplified for plain reading (`DistributionNotices.DisplayText`).
pub fn display_text(name: &str) -> String {
    let source = match name {
        "LICENSE" => return LICENSE.to_owned(),
        "ATTRIBUTION.md" => ATTRIBUTION_TEXT,
        _ => NOTICES,
    };
    let mut lines: Vec<String> = Vec::new();
    for line in source.lines() {
        let trimmed = line.trim_start();
        if trimmed.starts_with("```") {
            continue;
        }
        let mut text = line.to_owned();
        let hashes = text.chars().take_while(|&c| c == '#').count();
        if (1..=6).contains(&hashes) && text[hashes..].starts_with(' ') {
            text = text[hashes..].trim_start().to_owned();
        }
        if let Some(rest) = text.strip_prefix("> ") {
            text = rest.to_owned();
        } else if let Some(rest) = text.strip_prefix('>') {
            text = rest.to_owned();
        }
        // [label](url) → label (url)
        let mut out = String::new();
        let mut rest = text.as_str();
        while let Some(start) = rest.find('[') {
            let Some(close) = rest[start..].find("](") else { break };
            let label_end = start + close;
            let Some(url_end) = rest[label_end + 2..].find(')') else { break };
            out.push_str(&rest[..start]);
            out.push_str(&rest[start + 1..label_end]);
            out.push_str(" (");
            out.push_str(&rest[label_end + 2..label_end + 2 + url_end]);
            out.push(')');
            rest = &rest[label_end + 2 + url_end + 1..];
        }
        out.push_str(rest);
        text = out.replace('`', "");
        if text.trim_start().starts_with('|') {
            let inner = text.trim().trim_matches('|');
            if inner.chars().all(|c| matches!(c, ' ' | ':' | '|' | '-')) {
                continue;
            }
            for cell in inner.split('|') {
                lines.push(cell.trim().to_owned());
            }
            lines.push(String::new());
            continue;
        }
        lines.push(text);
    }
    // Join soft-wrapped paragraph lines.
    let mut joined: Vec<String> = Vec::new();
    for line in lines {
        let continues = !line.is_empty() && !line.starts_with('-') && !line.starts_with('|');
        match joined.last_mut() {
            Some(previous) if continues && !previous.is_empty() && !previous.starts_with('-') => {
                previous.push(' ');
                previous.push_str(&line);
            }
            _ => joined.push(line),
        }
    }
    joined.join("\n").trim().to_owned()
}

/// Credits and licences (`CreditsContent`, 720 x 680 window).
pub fn show_credits(ui: &mut Ui, area: Rect, selected: &mut usize) -> bool {
    let spec = Spec { height: Some(680.0), footer_gap: 14.0, ..Spec::new("credits", 720.0) };
    dialog::show(
        ui,
        area,
        spec,
        |ui| {
            dialog::heading(ui, "Credits and licences");
            ui.add_space(6.0);
            dialog::text(ui, "VineMix by acoyeen", 13.0, Weight::Regular, theme::TEXT, None);
            ui.add_space(5.0);
            dialog::text(ui, "These notices are included with VineMix and can be read offline.", 13.0, Weight::Regular, theme::MUTED, None);
            ui.add_space(10.0);
            ui.horizontal_wrapped(|ui| {
                ui.spacing_mut().item_spacing = Vec2::new(8.0, 6.0);
                for (label, url) in [("Project website", super::app::WEBSITE), ("Original source", REPOSITORY), ("Attribution online", ATTRIBUTION)] {
                    if dialog::button(ui, label, false, true, 10.0, 32.0).tip(url).clicked() {
                        let _ = crate::platform::shell_open(url);
                    }
                }
            });
            ui.add_space(6.0 + 14.0);
            let names = ["MIT licence", "Attribution policy", "Third-party notices"];
            let choices: Vec<super::widgets::Choice> = names.iter().map(|n| super::widgets::Choice { label: (*n).to_owned(), enabled: true, help: String::new() }).collect();
            let (rect, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), 34.0), Sense::hover());
            let (_, picked) = super::widgets::selector(ui, Id::unique("vinemix-notice"), rect, Some(*selected), names[*selected], &choices, true, 1.0, theme::TEXT, "Notice to read");
            if let Some(index) = picked {
                *selected = index;
            }
            ui.add_space(12.0);
            let file = NOTICE_FILES[*selected];
            let text = display_text(file);
            let body = Rect::from_min_max(ui.cursor().min, ui.max_rect().right_bottom());
            ui.painter().rect_stroke(body, 0.0, Stroke::new(1.0, theme::CONTROL_BORDER), egui::StrokeKind::Inside);
            let mut inner = ui.new_child(egui::UiBuilder::new().max_rect(body.shrink(1.0 + 12.0)));
            dialog::text_scroll_style(&mut inner);
            egui::ScrollArea::vertical().auto_shrink([false, false]).scroll_bar_visibility(egui::scroll_area::ScrollBarVisibility::VisibleWhenNeeded).id_salt(file).show(&mut inner, |ui| {
                dialog::selectable(ui, &text, 13.0, theme::TEXT);
            });
            ui.advance_cursor_after_rect(body);
        },
        |_| {},
    )
}

#[cfg(test)]
mod tests {
    #[test]
    fn notices_render() {
        let text = super::display_text("ATTRIBUTION.md");
        assert!(text.contains("Crediting VineMix"));
        assert!(!text.contains("]("));
        assert!(super::display_text("LICENSE").starts_with("MIT License"));
    }
}
