//! Application-wide hover-help audit (RUST-REV-036).
//!
//! Hover help is attached to every drawn control whether or not the pointer
//! is over it. These tests drive the real application through its surfaces
//! and states, record every hover-help text that is attached, and hold all
//! of it to the content standard: a short control name and a brief purpose
//! or relevant state, with no interaction tutorial. State-dependent help
//! that needs conditions a preview cannot produce (device states, failed
//! values, cable kinds) is built with the same production functions and
//! held to the same standard. The recorded texts are the inventory behind
//! `docs/TOOLTIP_AUDIT_2026-11-10.md`.
//!
//! Headless, over preview data: no engine, device, settings file or network.

use std::collections::BTreeSet;

use eframe::egui::{Id, Key, Pos2, Vec2};
use vinemix_model::config::{DeviceBinding, GraphEdge};
use vinemix_model::status::{Endpoint, NodeStatus};

use super::app::tests::{AppHarness, Dialog};
use super::widgets::recorded_tips;
use crate::graph;

/// A preview workspace made of `kinds`, laid out in a grid and reported as
/// running so that live-only help (meters, spectra, status) is exercised.
fn workspace(kinds: &[&str]) -> crate::protocol::Snapshot {
    let mut snapshot = crate::preview::create_snapshot(false, true);
    let mut nodes = Vec::new();
    for (index, kind) in kinds.iter().enumerate() {
        let (column, row) = (index % 5, index / 5);
        let mut node = graph::new_node(kind, 40.0 + 700.0 * column as f64, 40.0 + 760.0 * row as f64);
        node.id = format!("audit-{kind}");
        nodes.push(node);
    }
    snapshot.status.nodes = nodes
        .iter()
        .map(|node| {
            let mut status = NodeStatus { id: node.id.clone(), channels: 2, peak: 0.25, peak_left: 0.25, peak_right: 0.2, ..NodeStatus::default() };
            status.path.state = "Running".into();
            status
        })
        .collect();
    snapshot.configuration.graph.nodes = nodes;
    snapshot.configuration.graph.edges.clear();
    snapshot.configuration.graph.zoom = 1.0;
    snapshot.configuration.graph.pan_x = 0.0;
    snapshot.configuration.graph.pan_y = 0.0;
    snapshot
}

/// What was attached since the last call, as a sorted set.
fn collected() -> BTreeSet<String> {
    recorded_tips().into_iter().collect()
}

fn record(app: &mut AppHarness, frames: usize) -> BTreeSet<String> {
    let _ = recorded_tips();
    app.settle(frames);
    collected()
}

/// Cable help is a route ("Source → Destination"); the device picker's help
/// also contains an arrow ("Settings → Audio device visibility").
fn is_cable_help(text: &str) -> bool {
    text.contains(" → ") && !text.contains("Settings")
}

/// Moves the pointer over the canvas until help for a cable is attached;
/// returns that point.
fn find_cable(app: &mut AppHarness) -> Option<Pos2> {
    let canvas = app.rect_of(Id::unique("vinemix-canvas"))?;
    let mut y = canvas.top() + 6.0;
    while y < canvas.bottom() {
        let mut x = canvas.left() + 6.0;
        while x < canvas.right() {
            let _ = recorded_tips();
            app.harness.move_to(Pos2::new(x, y));
            app.frame();
            if recorded_tips().iter().any(|t| is_cable_help(t)) {
                return Some(Pos2::new(x, y));
            }
            x += 14.0;
        }
        y += 14.0;
    }
    None
}

/// Every surface, with the family name used in the audit checklist.
fn sweep() -> Vec<(&'static str, BTreeSet<String>)> {
    let mut families: Vec<(&'static str, BTreeSet<String>)> = Vec::new();

    // Title bar, toolbar, footer and canvas of the standard preview
    // workspace, with one group so group headings are present.
    let standard = crate::preview::create_snapshot(false, true);
    let members: Vec<String> = standard.configuration.graph.nodes.iter().take(2).map(|n| n.id.clone()).collect();
    let group = crate::frames::Frame { id: "audit-group".into(), name: "Example group".into(), color_key: "sage".into(), node_ids: members };
    let mut app = AppHarness::with_workspace(1600.0, 1000.0, 1.0, standard, vec![group]);
    families.push(("window chrome, standard workspace and groups", record(&mut app, 3)));

    // Cables: help is attached while the pointer is on one. Sweep the canvas.
    let _ = recorded_tips();
    let canvas = app.rect_of(Id::unique("vinemix-canvas")).expect("the canvas is drawn");
    let mut y = canvas.top() + 6.0;
    while y < canvas.bottom() {
        let mut x = canvas.left() + 6.0;
        while x < canvas.right() {
            app.harness.move_to(Pos2::new(x, y));
            app.frame();
            x += 14.0;
        }
        y += 14.0;
    }
    families.push(("cables", collected().into_iter().filter(|t| is_cable_help(t)).collect()));

    // Overview zoom: node headings change to the read-only summary.
    app.set_zoom(0.3);
    families.push(("overview zoom", record(&mut app, 3)));
    app.set_zoom(1.0);

    // An open device list: its rows carry help too.
    let mut pickers = BTreeSet::new();
    if let Some(device) = app.harness.find_text("Example USB Microphone") {
        app.click(device + Vec2::new(8.0, 6.0));
        pickers.extend(record(&mut app, 3));
        app.key(Key::Escape);
        app.settle(3);
    }
    families.push(("device and application pickers", pickers));

    // Settings menu, its search and its submenu.
    app.open_settings();
    let mut settings = record(&mut app, 2);
    if let Some(row) = app.harness.find_text("Audio device visibility") {
        app.harness.move_to(row + Vec2::new(30.0, 6.0));
        settings.extend(record(&mut app, 4));
    }
    app.type_text("zoom");
    settings.extend(record(&mut app, 3));
    families.push(("settings menu", settings));
    app.key(Key::Escape);
    app.key(Key::Escape);
    app.settle(3);
    app.close_settings();

    // Canvas menus: empty canvas, node, cable, group, and the node library.
    let empty = Pos2::new(canvas.right() - 60.0, canvas.bottom() - 220.0);
    app.secondary_click(empty);
    let mut menus = record(&mut app, 3);
    for label in ["Align selected nodes", "Add to group"] {
        if let Some(row) = app.harness.find_text(label) {
            app.harness.move_to(row + Vec2::new(30.0, 6.0));
            menus.extend(record(&mut app, 4));
        }
    }
    app.key(Key::Escape);
    app.settle(3);
    if let Some(header) = app.harness.find_text("Microphone 1") {
        app.secondary_click(header + Vec2::new(10.0, 6.0));
        menus.extend(record(&mut app, 3));
        for label in ["Troubleshooting", "Add to group", "Align selected nodes"] {
            if let Some(row) = app.harness.find_text(label) {
                app.harness.move_to(row + Vec2::new(30.0, 6.0));
                menus.extend(record(&mut app, 4));
            }
        }
        app.key(Key::Escape);
        app.settle(3);
    }
    if let Some(cable) = find_cable(&mut app) {
        app.secondary_click(cable);
        menus.extend(record(&mut app, 3));
        app.key(Key::Escape);
        app.settle(3);
    }
    if let Some(heading) = app.harness.find_text("Example group") {
        app.secondary_click(heading + Vec2::new(4.0, 4.0));
        menus.extend(record(&mut app, 3));
        if let Some(row) = app.harness.find_text("Group color") {
            app.harness.move_to(row + Vec2::new(30.0, 6.0));
            menus.extend(record(&mut app, 4));
        }
        app.key(Key::Escape);
        app.settle(3);
    }
    let add = app.rect_of(Id::unique("tool-add")).expect("the Add button");
    app.click(add.center());
    menus.extend(record(&mut app, 3));
    for category in ["Inputs", "Outputs", "Effects", "Mixing", "Controls"] {
        if let Some(row) = app.harness.find_text(category) {
            app.harness.move_to(row + Vec2::new(30.0, 6.0));
            menus.extend(record(&mut app, 4));
        }
    }
    app.type_text("zzzz");
    menus.extend(record(&mut app, 3));
    app.key(Key::Escape);
    app.settle(3);
    families.push(("canvas menus and node library", menus));

    // Dialogs and overlays.
    let mut dialogs = BTreeSet::new();
    for open in [Dialog::About, Dialog::Credits, Dialog::Updates, Dialog::WhatsNew, Dialog::ProcessingLoad, Dialog::Search, Dialog::QuitFailed] {
        app.open_dialog(open);
        dialogs.extend(record(&mut app, 3));
        app.close_dialog();
    }
    families.push(("dialogs, updates and status", dialogs));

    // Help: every topic with its practice lesson.
    let mut help = BTreeSet::new();
    for topic in ["controls", "dials", "buttons", "search", "equalizer", "feedback"] {
        app.open_dialog(Dialog::Help(topic));
        help.extend(record(&mut app, 4));
        app.close_dialog();
    }
    families.push(("help and practice views", help));
    drop(app);

    // Every node kind at full detail: headings, dials, entries, sockets,
    // selectors, meters, status, EQ and Voice Color.
    let mut nodes = BTreeSet::new();
    for batch in graph::KINDS.chunks(15) {
        let mut app = AppHarness::with_workspace(3700.0, 2500.0, 1.0, workspace(batch), Vec::new());
        assert!((app.zoom() - 1.0).abs() < 0.01, "full detail needs 100% zoom, got {}", app.zoom());
        nodes.extend(record(&mut app, 3));
    }
    families.push(("every node kind: headings, dials, entries, sockets, meters, EQ, Voice Color", nodes));

    families.push(("state variants built with the production functions", state_variants()));
    families
}

/// Help that depends on states a preview workspace does not reach, built by
/// the same functions the interface calls.
fn state_variants() -> BTreeSet<String> {
    let mut texts = BTreeSet::new();
    // Device rows in every availability state, for inputs and outputs.
    for direction in ["capture", "render"] {
        let endpoint = |id: &str, name: &str, state: &str, transport: &str| Endpoint {
            id: id.into(),
            stable_id: format!("stable-{id}"),
            name: name.into(),
            direction: direction.into(),
            state: state.into(),
            transport: transport.into(),
            interface_name: "Example USB Audio".into(),
            form_factor: if id == "advanced" { 8 } else { 1 },
            channels: 2,
            ..Endpoint::default()
        };
        let inventory = vec![
            endpoint("active", "Example device", "Active", "physical"),
            endpoint("twin", "Example device", "Active", "physical"),
            endpoint("disabled", "Example disabled device", "Disabled", "physical"),
            endpoint("unplugged", "Example unplugged device", "Unplugged", "physical"),
            endpoint("cable", "CABLE Input (VB-Audio Virtual Cable)", "Active", "vbCable"),
            endpoint("advanced", "Example digital output", "Active", "physical"),
        ];
        let all = crate::availability::Visibility { disabled: true, disconnected: true, advanced: true };
        for binding in [
            DeviceBinding::default(),
            DeviceBinding { id: "active".into(), ..DeviceBinding::default() },
            DeviceBinding { id: "gone".into(), stable_id: "stable-gone".into(), name: "Example device".into(), channel: -1 },
        ] {
            for choice in crate::availability::build(&inventory, direction, &binding, all) {
                texts.insert(choice.help);
            }
        }
        texts.extend(crate::availability::build(&[], direction, &DeviceBinding::default(), all).into_iter().map(|c| c.help));
    }
    // Cables of every kind, on and off.
    let preview = crate::preview::create_snapshot(false, true);
    let graph = &preview.configuration.graph;
    if let Some(edge) = graph.edges.first() {
        for kind in ["audio", "control", "sidechain"] {
            for muted in [false, true] {
                let variant = GraphEdge { kind: kind.into(), muted, ..edge.clone() };
                texts.insert(super::canvas::connection_help(graph, &variant));
            }
        }
    }
    // The EQ plot: live and practice, with and without a band, bypassed, full.
    let mut eq = graph::new_node("eq", 0.0, 0.0);
    let bands = vinemix_model::config::default_eq_bands();
    for practice in [false, true] {
        for bypass in [false, true] {
            eq.bypass = bypass;
            texts.insert(super::eq::plot_help(&eq, &bands, None, None, false, practice));
            texts.insert(super::eq::plot_help(&eq, &[], None, None, false, practice));
            texts.insert(super::eq::plot_help(&eq, &bands, Some(0), Some(0), false, practice));
            texts.insert(super::eq::plot_help(&eq, &bands, Some(1), None, false, practice));
            texts.insert(super::eq::plot_help(&eq, &bands, None, None, true, practice));
        }
    }
    // Every dial's help and its rejected-entry message.
    for kind in graph::KINDS {
        let node = graph::new_node(kind, 0.0, 0.0);
        for spec in super::controls::dial_specs(&node) {
            texts.insert(spec.help());
            texts.insert(format!("Value not applied\n{}", spec.entry_requirement()));
        }
        texts.insert(super::controls::node_description(&node));
        texts.insert(format!("{}\n{}", graph::kind_label(kind), graph::catalog::summary(kind)));
    }
    for mode in ["static", "dynamic"] {
        texts.insert(super::controls::ducking_description(mode));
    }
    texts.remove("");
    texts
}

/// Phrases that teach how to operate a control. They belong in Help and in
/// accessible descriptions, not in hover help.
const TUTORIAL_PHRASES: [&str; 19] = [
    "drag",
    "double-click",
    "double click",
    "shift for",
    "hold shift",
    "page up",
    "page down",
    "arrow key",
    "enter applies",
    "escape cancels",
    "escape clears",
    "tab applies",
    "type a number",
    "click to",
    "click it",
    "right-click",
    "press ",
    "home or",
    "hover ",
];

/// Hover help that may exceed the ordinary limits, with the reason. Matched
/// by prefix. Every entry is an exception to the product's concise-help
/// requirement, so the list stays short and each one is justified.
const EXCEPTIONS: [(&str, &str); 2] = [
    ("Right-click drag to pan", "the setting's own name contains the gesture it enables"),
    ("Choose device · right-click", "a status line that names where the missing assignment is made"),
];

fn exception(text: &str) -> Option<&'static str> {
    EXCEPTIONS.iter().find(|(prefix, _)| text.starts_with(prefix)).map(|(_, reason)| *reason)
}

const MAXIMUM_LINES: usize = 3;
const MAXIMUM_CHARACTERS: usize = 170;

/// Why `text` breaks the content standard, if it does.
fn violation(text: &str) -> Option<String> {
    if exception(text).is_some() {
        return None;
    }
    let lines: Vec<&str> = text.split('\n').filter(|l| !l.trim().is_empty()).collect();
    if lines.len() > MAXIMUM_LINES {
        return Some(format!("{} lines (at most {MAXIMUM_LINES})", lines.len()));
    }
    let length = text.chars().count();
    if length > MAXIMUM_CHARACTERS {
        return Some(format!("{length} characters (at most {MAXIMUM_CHARACTERS})"));
    }
    let lower = text.to_lowercase();
    TUTORIAL_PHRASES.iter().find(|phrase| lower.contains(*phrase)).map(|phrase| format!("interaction tutorial ({phrase:?})"))
}

/// The whole application's hover help meets the content standard. Run with
/// `--nocapture` to print the inventory.
#[test]
fn every_hover_help_text_in_the_application_is_concise() {
    let families = sweep();
    let mut total = 0;
    let mut violations = Vec::new();
    for (family, tips) in &families {
        println!("=== {family}: {} texts", tips.len());
        assert!(!tips.is_empty(), "the audit did not reach: {family}");
        for tip in tips {
            total += 1;
            let shown = tip.replace('\n', " ⏎ ");
            match violation(tip) {
                Some(reason) => {
                    println!("  ✗ {shown}   [{reason}]");
                    violations.push(format!("{family}: {shown} [{reason}]"));
                }
                None => println!("  · {shown}"),
            }
        }
    }
    println!("{total} hover-help texts recorded, {} outside the standard", violations.len());
    assert!(violations.is_empty(), "hover help outside the content standard:\n{}", violations.join("\n"));

    // The sweep reached each tooltip family named in the audit scope.
    let all: BTreeSet<&str> = families.iter().flat_map(|(_, tips)| tips.iter().map(String::as_str)).collect();
    for (family, expected) in [
        ("node heading", "Reverb\nAdds room-like decay. Wet carries the reverb alone."),
        ("dial and numeric entry", "Level\nAdjusts this node's audio level. 0 dB leaves it unchanged."),
        ("rejected numeric entry", "Value not applied\nEnter a number from -60.0 dB to 12.0 dB."),
        ("socket", "Audio output\nCarries this node's audio."),
        ("trigger socket", "Trigger\nThe sound that causes ducking; never heard.\nA silent or unavailable source is a silent trigger."),
        ("cable", "Microphone 1 → What others hear"),
        ("muted cable", "Microphone 1 → What I hear\nOff"),
        ("device picker", "Audio device\nMore devices: Settings → Audio device visibility."),
        ("unavailable device row", "Example unplugged device\nExample USB Audio\nUnavailable. Reconnect or enable the device in Windows; saved assignments are retained."),
        ("meter", "Highest observed peak\nNo readings yet"),
        ("equalizer", "Equalizer\nLine · EQ response. Shaded · Measured output.\nNo bands · Audio passes without EQ changes"),
        ("practice equalizer", "Equalizer\nPractice only · Calculated response; no audio is measured or changed.\nNo bands · Audio passes without EQ changes"),
        ("voice color", "Voice Color\nAcross · Warm to Bright. Up · Natural to Broadcast.\nNeutral"),
        ("canvas control", "Minimap\nThe whole canvas, with the part in view outlined."),
        ("group heading", "Example group\n2 nodes"),
        ("menu", "Frame selection\nBrings the selected nodes into view"),
        ("node library", "Echo\nAdds fading echoes. The Echo output carries the echoes alone."),
        ("settings", "Show minimap\nShows the canvas overview in the bottom-right corner."),
        ("settings search", "Search settings and help\nFinds a setting or the instructions for a control."),
        ("dialog", "Close this window."),
        ("updates", "Check automatically\nChecks daily. You choose when to download and install."),
        ("status", "Preview · no audio engine"),
        ("practice view", "Restore this practice example to its starting state"),
        ("practice meter", "Sample peaks\nLeft · −18.0 dBFS · Right · −12.0 dBFS\nPractice values; no audio is measured."),
    ] {
        assert!(all.contains(expected), "{family}: expected hover help {expected:?} was not found.\nNearest: {:?}", {
            let key = expected.split('\n').next().unwrap_or_default();
            all.iter().filter(|t| t.starts_with(key)).collect::<Vec<_>>()
        });
    }
}

/// Practice-only surfaces never describe live measurement (RUST-REV-035).
#[test]
fn practice_help_never_claims_to_measure_audio() {
    let mut eq = graph::new_node("eq", 0.0, 0.0);
    let bands = vinemix_model::config::default_eq_bands();
    assert!(bands.len() >= 2);
    for bypass in [false, true] {
        eq.bypass = bypass;
        for shown in [&bands[..], &[][..]] {
            let practice = super::eq::plot_help(&eq, shown, None, None, false, true);
            assert!(practice.contains("Practice only") && practice.contains("no audio is measured"), "{practice}");
            assert!(!practice.to_lowercase().contains("measured output"), "{practice}");
            let live = super::eq::plot_help(&eq, shown, None, None, false, false);
            assert!(live.contains("Measured output") && !live.contains("Practice"), "{live}");
        }
    }
    // With a band selected or hovered, both modes describe the band itself.
    assert_eq!(super::eq::plot_help(&eq, &bands, Some(0), Some(0), false, true), super::eq::plot_help(&eq, &bands, Some(0), Some(0), false, false));
    // The practice lessons as drawn.
    let mut app = AppHarness::new(1600.0, 1000.0, 1.0);
    for topic in ["dials", "buttons", "search", "equalizer", "feedback"] {
        app.open_dialog(Dialog::Help(topic));
        for text in record(&mut app, 4) {
            let lower = text.to_lowercase();
            assert!(!lower.contains("measured output") && !lower.starts_with("audio peaks"), "the {topic} lesson implies live measurement: {text:?}");
        }
        app.close_dialog();
    }
}

/// RUST-REV-038 in the real canvas: help for a cable appears at the cable.
#[test]
fn cable_help_appears_at_the_hovered_cable() {
    for (width, height, pixels_per_point, zoom) in [(1600.0, 1000.0, 1.0, 1.0), (1280.0, 800.0, 1.5, 1.0), (1600.0, 1000.0, 1.0, 0.6), (1100.0, 700.0, 2.0, 0.8)] {
        let mut app = AppHarness::new(width, height, pixels_per_point);
        app.set_zoom(zoom);
        let cable = find_cable(&mut app).unwrap_or_else(|| panic!("a cable is on screen at {width}x{height}, zoom {zoom}"));
        // Move away, come back and rest on it.
        app.harness.move_to(cable + Vec2::new(0.0, -200.0));
        app.settle(2);
        app.hover(cable);
        let help = app.harness.painted_text().into_iter().find(|(text, _)| is_cable_help(text));
        let (text, position) = help.unwrap_or_else(|| panic!("cable help is shown at {width}x{height}, zoom {zoom}"));
        let distance = (position - cable).length();
        assert!(distance < 340.0, "{width}x{height} zoom {zoom}: help {text:?} at {position:?} is {distance} from the hovered cable point {cable:?}");
        assert!((position.y - cable.y).abs() < 80.0, "it sits beside the cable, not at the canvas edge: {position:?} vs {cable:?}");
        // Dragging the canvas dismisses it.
        app.harness.press(cable + Vec2::new(0.0, -200.0));
        app.frame();
        app.harness.move_to(cable + Vec2::new(40.0, -160.0));
        app.settle(2);
        assert!(app.harness.painted_text().iter().all(|(text, _)| !is_cable_help(text)), "help is gone while dragging");
        app.harness.release(cable + Vec2::new(40.0, -160.0));
        app.settle(2);
    }
}

