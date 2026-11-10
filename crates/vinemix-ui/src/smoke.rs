//! `--release-smoke <new folder>` (`BetaReleaseSmoke`): inert checks of the
//! delivered executable, without user data, an engine, device enumeration or
//! a window. The update worker runs it against an installed candidate.

use std::path::Path;
use std::time::{Duration, Instant};

use eframe::egui;

use crate::mixer::MixerState;

const SCOPE: &str = "Delivered executable, metadata, runtime package integrity, embedded distribution notices, detached starter canvas and offline Credits layouts. No engine, personal settings, device enumeration or shown window.";

/// Lays out the starter workspace and Credits on a detached egui context.
fn detached_layouts() -> Result<MixerState, String> {
    let mut mixer = MixerState::new(true);
    let mut configuration = vinemix_model::config::Configuration { version: 2, ..Default::default() };
    configuration.graph = crate::graph::fresh();
    let snapshot = crate::protocol::Snapshot { settings_state: "loaded".into(), configuration, ..Default::default() };
    mixer.apply_preview_snapshot(snapshot, Vec::new())?;
    let ctx = egui::Context::default();
    crate::ui::theme::install(&ctx);
    let mut canvas = crate::ui::canvas::Canvas::default();
    let mut selected = 0usize;
    let screen = egui::Rect::from_min_size(egui::Pos2::ZERO, egui::vec2(1280.0, 800.0));
    for _ in 0..3 {
        let input = egui::RawInput { screen_rect: Some(screen), ..Default::default() };
        let mut output = ctx.run_ui(input, |ui| {
            canvas.show(ui, screen, &mut mixer);
            let area = egui::Rect::from_min_size(egui::Pos2::ZERO, egui::vec2(640.0, 580.0));
            crate::ui::about::show_credits(ui, area, &mut selected);
        });
        // Detached: no renderer applies the font atlas, so discard it.
        output.textures_delta.clear();
    }
    Ok(mixer)
}

fn checks(mixer: &MixerState) -> bool {
    let graph = mixer.graph();
    let unassigned = |kind: &str| graph.nodes.iter().filter(|n| n.kind == kind).all(|n| !n.device.assigned());
    graph.nodes.len() == 7
        && graph.edges.len() == 6
        && graph.nodes.iter().filter(|n| matches!(n.kind.as_str(), "output" | "virtualOutput")).all(|n| n.muted)
        && graph.nodes.iter().filter(|n| n.kind == "output").count() == 1
        && graph.nodes.iter().find(|n| n.kind == "output").is_some_and(|n| n.gain_db == -24.0)
        && graph.nodes.iter().filter(|n| n.kind == "denoiser").count() == 1
        && graph.nodes.iter().find(|n| n.kind == "denoiser").is_some_and(|n| n.denoiser.algorithm == "rnnoise")
        && unassigned("microphone")
        && unassigned("output")
        && crate::ui::about::notice_text("LICENSE").is_some_and(|t| t.contains("Copyright (c) 2026 acoyeen (VineMix, https://vinemix.app)"))
        && crate::ui::about::notice_text("ATTRIBUTION.md").is_some_and(|t| t.contains("not additional MIT licence conditions"))
}

/// Runs the check; the process exit code is 0 when it passed.
pub fn run(directory: &Path) -> i32 {
    let started = Instant::now();
    let target = crate::update::files::full(directory);
    if target.exists() {
        return 1; // Choose a new evidence directory.
    }
    if std::fs::create_dir_all(&target).is_err() {
        return 1;
    }
    let result = std::panic::catch_unwind(|| -> Result<serde_json::Value, String> {
        let payload = crate::runtime::validate_package()?;
        let extracted = if crate::runtime::standalone() {
            Some(crate::runtime::resolve_engine_path(Some(&target.join("runtime")))?.to_string_lossy().into_owned())
        } else {
            None
        };
        let mixer = detached_layouts()?;
        let notices: Vec<serde_json::Value> = crate::ui::about::NOTICE_FILES
            .iter()
            .map(|name| {
                let text = crate::ui::about::notice_text(name).unwrap_or_default();
                let digest = vinemix_update::crypto::sha256(text.as_bytes()).map(|d| vinemix_update::crypto::hex(&d)).unwrap_or_default();
                serde_json::json!({ "name": name, "sha256": digest })
            })
            .collect();
        Ok(serde_json::json!({
            "passed": checks(&mixer),
            "version": vinemix_platform::release::VERSION,
            "displayVersion": vinemix_platform::release::DISPLAY_VERSION,
            "mode": crate::runtime::build_mode(),
            "build": crate::ui::about::build_identity(),
            "payload": payload,
            "extractedEngine": extracted,
            "notices": notices,
            "scope": SCOPE,
        }))
    });
    let report = match result {
        Ok(Ok(report)) => report,
        Ok(Err(error)) => serde_json::json!({ "passed": false, "error": error }),
        Err(_) => serde_json::json!({ "passed": false, "error": "The detached layout check panicked." }),
    };
    let passed = report.get("passed").and_then(serde_json::Value::as_bool) == Some(true);
    let text = serde_json::to_string_pretty(&report).unwrap_or_default();
    if std::fs::write(target.join("results.json"), text).is_err() {
        return 1;
    }
    // Released update workers identify this child by its image immediately
    // after launch; keep a successful check alive briefly.
    if passed {
        let remaining = Duration::from_secs(2).saturating_sub(started.elapsed());
        std::thread::sleep(remaining);
    }
    i32::from(!passed)
}
