//! Whole-application interface tests: the real `App` (title bar, toolbar,
//! canvas, menus, dialogs) over a preview model, run headlessly.
//!
//! No engine is started or contacted, no settings or data file is read or
//! written, and nothing is sent over the network. Geometry is in egui
//! logical points; native window behaviour, real rendering and assistive
//! technology are outside what these tests observe.

use eframe::egui::{Id, Key, Pos2, Rect};

use super::super::test_support::Harness;
use super::*;

/// The dialogs and overlays the application can show.
#[derive(Clone, Copy, Debug)]
pub enum Dialog {
    About,
    Credits,
    Updates,
    WhatsNew,
    ProcessingLoad,
    Search,
    /// The quit dialog after a failed save (its four recovery choices).
    QuitFailed,
    /// Help on a topic, with that topic's practice lesson.
    Help(&'static str),
}

pub struct AppHarness {
    pub harness: Harness,
    pub app: App,
    frame: eframe::Frame,
}

impl AppHarness {
    pub fn new(width: f32, height: f32, pixels_per_point: f32) -> Self {
        Self::with_workspace(width, height, pixels_per_point, crate::preview::create_snapshot(false, true), Vec::new())
    }

    /// The application over a given preview workspace and node groups.
    pub fn with_workspace(width: f32, height: f32, pixels_per_point: f32, snapshot: crate::protocol::Snapshot, groups: Vec<crate::frames::Frame>) -> Self {
        let harness = Harness::sized(width, height).pixels_per_point(pixels_per_point);
        let app = App::headless_with(&harness.ctx, snapshot, groups);
        // A steady caret, so its position can be read on any frame.
        harness.ctx.global_style_mut(|style| style.visuals.text_cursor.blink = false);
        let mut this = Self { harness, app, frame: eframe::Frame::_new_kittest() };
        this.settle(4);
        this
    }

    pub fn frame_after(&mut self, seconds: f64) {
        let (app, frame) = (&mut self.app, &mut self.frame);
        eframe::App::logic(app, &self.harness.ctx.clone(), frame);
        self.harness.frame_after(seconds, |ui| eframe::App::ui(app, ui, frame));
    }

    pub fn frame(&mut self) {
        self.frame_after(1.0 / 60.0);
    }

    pub fn settle(&mut self, frames: usize) {
        for _ in 0..frames {
            self.frame();
        }
    }

    /// Lets `seconds` pass (hover delays, double-click windows).
    pub fn wait(&mut self, seconds: f64) {
        self.frame_after(seconds);
        self.frame();
    }

    /// Bounds of the control with this accessible name (needs accessibility on).
    pub fn rect_of_named(&self, name: &str) -> Option<Rect> {
        let bounds = self.harness.accessible(name)?.bounds()?;
        Some(Rect::from_min_max(Pos2::new(bounds.x0 as f32, bounds.y0 as f32), Pos2::new(bounds.x1 as f32, bounds.y1 as f32)))
    }

    pub fn rect_of(&self, id: Id) -> Option<Rect> {
        self.harness.ctx.read_response(id).map(|response| response.rect)
    }

    pub fn click(&mut self, at: Pos2) {
        self.harness.move_to(at);
        self.frame();
        self.harness.press(at);
        self.frame();
        self.harness.release(at);
        self.frame();
        self.settle(2);
    }

    pub fn secondary_click(&mut self, at: Pos2) {
        self.harness.move_to(at);
        self.frame();
        self.harness.press_secondary(at);
        self.frame();
        self.harness.release_secondary(at);
        self.frame();
        self.settle(3);
    }

    pub fn hover(&mut self, at: Pos2) {
        self.harness.move_to(at);
        self.settle(2);
        self.wait(0.6);
    }

    pub fn key(&mut self, key: Key) {
        self.harness.key(key);
        self.frame();
    }

    pub fn type_text(&mut self, text: &str) {
        self.harness.text(text);
        self.frame();
        self.settle(2);
    }

    pub fn open_dialog(&mut self, dialog: Dialog) {
        self.app.settings_open = false;
        self.app.overlay = match dialog {
            Dialog::About => Overlay::About(false),
            Dialog::Credits => Overlay::Credits(0),
            Dialog::Updates => Overlay::Updates,
            Dialog::WhatsNew => Overlay::WhatsNew,
            Dialog::ProcessingLoad => Overlay::ProcessingLoad,
            Dialog::Search => Overlay::Search,
            Dialog::QuitFailed => Overlay::Quit(QuitSaveResult {
                saved: false,
                error: "Example: the workspace file could not be written.".into(),
                pending_changes: 2,
            }),
            Dialog::Help(topic) => Overlay::Help(super::super::help_view::HelpState::for_topic(topic)),
        };
        self.settle(3);
    }

    pub fn close_dialog(&mut self) {
        self.app.overlay = Overlay::None;
        self.settle(2);
    }

    pub fn set_zoom(&mut self, zoom: f32) {
        self.app.canvas.zoom = zoom;
        self.settle(2);
    }

    pub fn zoom(&self) -> f32 {
        self.app.canvas.zoom
    }

    pub fn close_settings(&mut self) {
        self.app.settings_open = false;
        self.settle(2);
    }

    pub fn open_settings(&mut self) {
        let button = self.rect_of(Id::unique("settings")).expect("the Settings button is drawn");
        self.click(button.center());
        assert!(self.app.settings_open, "the Settings menu opened");
        self.settle(4);
    }

    pub fn settings_menu(&self) -> Rect {
        self.harness.area_rect(Id::unique("settings-menu")).expect("the Settings menu is shown")
    }

    pub fn search_field(&self) -> Rect {
        self.rect_of(Id::unique("settings-search")).expect("the search field is drawn")
    }
}

#[test]
fn minimap_click_and_drag_move_the_view() {
    let mut app = AppHarness::new(1280.0, 800.0, 1.0);
    let map = app.rect_of(Id::unique("vinemix-minimap")).unwrap();
    let before = app.app.canvas.pan;
    app.click(map.center() + egui::vec2(30.0, 12.0));
    let after_click = app.app.canvas.pan;
    assert_ne!(before, after_click);
    let start = map.center();
    let end = start + egui::vec2(20.0, 10.0);
    app.harness.move_to(start);
    app.frame();
    app.harness.press(start);
    app.frame();
    let after_press = app.app.canvas.pan;
    app.harness.move_to(end);
    app.frame();
    app.harness.release(end);
    app.frame();
    assert_ne!(after_press, app.app.canvas.pan);
}

#[test]
fn the_whole_application_draws_headlessly() {
    let app = AppHarness::new(1280.0, 800.0, 1.0);
    let painted: Vec<String> = app.harness.painted_text().into_iter().map(|(text, _)| text).collect();
    assert!(painted.iter().any(|t| t == "Settings"), "title bar: {painted:?}");
    assert!(painted.iter().any(|t| t == "Add"), "toolbar");
    assert!(painted.len() > 20, "the preview workspace is on the canvas ({} text runs)", painted.len());
}

// ----- RUST-REV-039: Settings height after clearing the search -------------

fn menu_height_through_a_search(width: f32, height: f32, pixels_per_point: f32, query: &str, clear_with_escape: bool) -> (f32, f32, f32) {
    let mut app = AppHarness::new(width, height, pixels_per_point);
    app.open_settings();
    let before = app.settings_menu().height();
    app.type_text(query);
    app.settle(6);
    assert_eq!(app.app.settings_search.query, query, "the search field received the query");
    let during = app.settings_menu().height();
    if clear_with_escape {
        app.key(Key::Escape);
    } else {
        for _ in 0..query.chars().count() {
            app.key(Key::Backspace);
        }
    }
    app.settle(8);
    assert!(app.app.settings_search.query.is_empty(), "the search was cleared");
    assert!(app.app.settings_open, "clearing the search keeps the menu open");
    (before, during, app.settings_menu().height())
}

/// The reported sequence: search for something with few or no results, then
/// clear it. The menu used to stay a two-row scroll area.
#[test]
fn clearing_the_settings_search_restores_the_menu_height() {
    for (query, label) in [("zzzzzz", "no results"), ("minimap", "one setting"), ("devices", "several results")] {
        for clear_with_escape in [true, false] {
            let (before, during, after) = menu_height_through_a_search(1280.0, 800.0, 1.0, query, clear_with_escape);
            assert!(before > 300.0, "{label}: the full menu is tall ({before})");
            assert!((after - before).abs() <= 1.0, "{label}, escape {clear_with_escape}: {before} before, {during} while searching, {after} after clearing");
            if label == "no results" {
                assert!(during < before - 100.0, "{label}: the menu shrinks to its results while searching ({during})");
            }
        }
    }
}

#[test]
fn repeated_searches_keep_restoring_the_full_menu() {
    let mut app = AppHarness::new(1280.0, 800.0, 1.0);
    app.open_settings();
    let full = app.settings_menu().height();
    for query in ["zzzz", "quit", "zoom", "qqqq", "about"] {
        app.type_text(query);
        app.settle(6);
        app.key(Key::Escape);
        app.settle(8);
        let height = app.settings_menu().height();
        assert!((height - full).abs() <= 1.0, "after searching {query:?}: {height} instead of {full}");
    }
    // Every top-level row is back without reopening the menu.
    let painted: Vec<String> = app.harness.painted_text().into_iter().map(|(text, _)| text).collect();
    for row in ["Find node…", "Activity", "Show minimap", "Audio device visibility", "Help & shortcuts", "About VineMix", "Quit VineMix"] {
        assert!(painted.iter().any(|t| t == row), "{row} is listed again: {painted:?}");
    }
}

#[test]
fn device_visibility_expands_inside_settings_without_covering_other_rows() {
    for (width, height, scale) in [(499.0, 579.0, 1.0), (900.0, 500.0, 1.5), (1280.0, 800.0, 1.0)] {
        let mut app = AppHarness::new(width, height, scale);
        app.open_settings();
        let group = "Audio device visibility";
        let first = "Show disabled devices";
        assert!(app.harness.find_text(first).is_none());
        app.click(text_run(&app, group).center());
        app.settle(6);
        let menu = app.settings_menu();
        let parent = text_run(&app, group);
        let mut bottom = parent.bottom();
        for label in [first, "Show disconnected or missing devices", "Show advanced inputs and outputs"] {
            let child = text_run(&app, label);
            assert!(menu.contains_rect(child) && child.left() > parent.left() && child.top() > bottom, "{label}: {child:?} inside {menu:?}");
            bottom = child.bottom();
        }
        assert!(text_run(&app, "Windows sound settings").top() > bottom);
        let previous = app.app.link.state().device_visibility().disabled;
        app.click(text_run(&app, first).center());
        assert_ne!(app.app.link.state().device_visibility().disabled, previous);
        app.click(text_run(&app, group).center());
        assert!(app.harness.find_text(first).is_none());
        app.key(Key::ArrowRight);
        assert!(app.harness.find_text(first).is_some());
        app.key(Key::ArrowLeft);
        assert!(app.harness.find_text(first).is_none());
        app.click(app.search_field().center());
        app.type_text("disconnected");
        assert!(app.harness.find_text("Show disconnected or missing devices").is_some());
        assert!(app.harness.find_text(first).is_none());
    }
}

/// In a small window the menu is as tall as the window allows and scrolls;
/// clearing a search returns it to that same height, not to two rows.
#[test]
fn small_windows_and_display_scales_restore_to_the_available_height() {
    for (width, height, pixels_per_point) in [(900.0, 420.0, 1.0), (1000.0, 500.0, 1.5), (1280.0, 800.0, 1.25), (1600.0, 1000.0, 2.0), (760.0, 360.0, 1.0)] {
        let (before, during, after) = menu_height_through_a_search(width, height, pixels_per_point, "zzzzzz", true);
        assert!((after - before).abs() <= 1.0, "{width}x{height} at {pixels_per_point}: {before} before, {during} searching, {after} after");
        assert!(before <= height, "the menu fits the window: {before} in {height}");
        assert!(before > 150.0, "{width}x{height}: more than a couple of rows are visible ({before})");
    }
}

// ----- RUST-REV-040: Settings search caret, placeholder and text ------------

/// The vertical extent of the blinking caret, found as the thin vertical
/// line or rectangle painted inside the field.
fn caret(app: &AppHarness, field: Rect) -> Option<Rect> {
    app.harness.thin_vertical_marks(field.expand(1.0)).into_iter().next()
}

fn text_run(app: &AppHarness, needle: &str) -> Rect {
    app.harness.text_rect(needle).unwrap_or_else(|| panic!("{needle:?} is painted"))
}

#[test]
fn search_caret_placeholder_and_typed_text_share_one_line() {
    for pixels_per_point in [1.0, 1.25, 1.5, 2.0] {
        let mut app = AppHarness::new(1280.0, 800.0, pixels_per_point);
        app.open_settings();
        let field = app.search_field();
        assert!((field.height() - 30.0).abs() <= 0.5, "the field keeps its 30-point height: {}", field.height());
        // Empty and focused: placeholder and caret.
        let placeholder = text_run(&app, "Search settings and help");
        let empty_caret = caret(&app, field).expect("the caret is painted in the empty, focused field");
        let tolerance = 0.75;
        assert!((placeholder.center().y - field.center().y).abs() <= tolerance, "{pixels_per_point}: placeholder at {} in a field centred on {}", placeholder.center().y, field.center().y);
        assert!((empty_caret.center().y - field.center().y).abs() <= tolerance, "{pixels_per_point}: caret centred on {} in a field centred on {}", empty_caret.center().y, field.center().y);
        assert!((empty_caret.left() - placeholder.left()).abs() <= 1.5, "{pixels_per_point}: the caret starts where the placeholder starts ({} vs {})", empty_caret.left(), placeholder.left());
        // Typed text sits exactly where the placeholder was.
        app.type_text("zoom");
        let typed = text_run(&app, "zoom");
        assert!((typed.left() - placeholder.left()).abs() <= 0.5 && (typed.center().y - placeholder.center().y).abs() <= 0.5, "{pixels_per_point}: typed text at {typed:?}, placeholder was at {placeholder:?}");
        let typed_caret = caret(&app, field).expect("the caret follows the text");
        assert!((typed_caret.center().y - field.center().y).abs() <= tolerance);
        assert!((typed_caret.left() - typed.right()).abs() <= 2.0, "{pixels_per_point}: the caret is at the end of the text ({} vs {})", typed_caret.left(), typed.right());
        // A query longer than the field scrolls; clearing it must bring the
        // placeholder and caret back to the same origin.
        app.key(Key::Escape);
        app.settle(4);
        let long = "a very long search query that cannot possibly fit inside the settings search field";
        app.type_text(long);
        app.settle(4);
        let scrolled_caret = caret(&app, field).expect("the caret stays visible in a scrolled query");
        assert!(field.expand(1.0).contains(scrolled_caret.center()), "{pixels_per_point}: the caret stays inside the field: {scrolled_caret:?} in {field:?}");
        app.key(Key::Escape);
        app.settle(6);
        let field = app.search_field();
        let restored = text_run(&app, "Search settings and help");
        let restored_caret = caret(&app, field).expect("the caret returns after clearing");
        assert!((restored.left() - placeholder.left()).abs() <= 0.5, "{pixels_per_point}: placeholder at {} after clearing a long query, {} before", restored.left(), placeholder.left());
        assert!((restored_caret.left() - restored.left()).abs() <= 1.5, "{pixels_per_point}: caret at {} after clearing, placeholder at {}", restored_caret.left(), restored.left());
        assert!((restored_caret.center().y - field.center().y).abs() <= tolerance);
    }
}

// ----- RUST-REV-027: custom controls in the accessibility tree --------------

/// A preview workspace of the given kinds in a row, reported as running.
fn workspace_of(kinds: &[&str]) -> crate::protocol::Snapshot {
    let mut snapshot = crate::preview::create_snapshot(false, true);
    let nodes: Vec<_> = kinds
        .iter()
        .enumerate()
        .map(|(index, kind)| {
            let mut node = crate::graph::new_node(kind, 40.0 + 700.0 * index as f64, 40.0);
            node.id = format!("a11y-{kind}");
            node
        })
        .collect();
    snapshot.status.nodes = nodes
        .iter()
        .map(|node| {
            let mut status = vinemix_model::status::NodeStatus { id: node.id.clone(), channels: 2, ..Default::default() };
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

/// What the accessibility tree says about the painted controls. This is the
/// tree egui hands to the platform; it is not a screen-reader session.
#[test]
fn custom_controls_expose_names_values_and_descriptions() {
    use eframe::egui::accesskit::Role;
    let mut app = AppHarness::with_workspace(2400.0, 1400.0, 1.0, workspace_of(&["voiceColor", "eq", "gain"]), Vec::new());
    app.harness.enable_accessibility();
    app.settle(4);

    let pad = app.harness.accessible("Voice Color pad").expect("the Voice Color pad has an accessible node");
    assert_eq!(pad.role(), Role::Slider);
    assert_eq!(pad.value(), Some("Neutral"), "its value is the current character");
    let description = pad.description().unwrap_or_default();
    assert!(description.contains("arrows") && description.contains("Home"), "it says how to operate it: {description}");

    let plot = app.harness.accessible("Equalizer plot").expect("the EQ plot has an accessible node");
    assert_eq!(plot.value(), Some("0 of 8 bands. No band selected"));
    assert!(plot.description().unwrap_or_default().contains("Insert adds a band"), "{:?}", plot.description());

    // Dials: the hover help is concise, the accessible description keeps
    // the range and the operating instructions.
    let level = app.harness.accessible("Level").expect("the Level dial has an accessible node");
    assert_eq!(level.role(), Role::Slider);
    assert_eq!(level.numeric_value(), Some(0.0));
    let description = level.description().unwrap_or_default();
    for expected in ["Adjusts this node's audio level", "-60.0 dB to 12.0 dB", "Drag vertically", "Arrow keys", "Page Up", "resets to 0.0 dB"] {
        assert!(description.contains(expected), "the dial's description keeps {expected:?}: {description}");
    }
    let entry = app.harness.accessible("Level value").expect("the exact-value field is named");
    assert!(entry.description().unwrap_or_default().contains("Escape cancels"), "{:?}", entry.description());

    // Named, not anonymous: the other Voice Color and toolbar controls.
    let names: Vec<String> = app.harness.accessible_names().into_iter().map(|(_, name)| name).collect();
    for expected in ["Tone", "Character", "Tone value", "Add", "Settings", "Bypass"] {
        assert!(names.iter().any(|n| n == expected), "{expected} is in the accessibility tree: {names:?}");
    }
}

/// The Voice Color pad can be operated from the keyboard alone.
#[test]
fn voice_color_pad_is_keyboard_operable() {
    let mut app = AppHarness::with_workspace(1600.0, 1100.0, 1.0, workspace_of(&["voiceColor"]), Vec::new());
    let tone = |app: &AppHarness| app.app.link.state().graph().nodes[0].voice_color.tone;
    let character = |app: &AppHarness| app.app.link.state().graph().nodes[0].voice_color.ambience;
    assert_eq!((tone(&app), character(&app)), (0.0, 0.0));
    // Focus the pad with a press on its neutral handle (bottom centre), which
    // does not move it.
    let natural = app.harness.text_rect("Natural").expect("the pad's Natural label");
    let handle = Pos2::new(natural.center().x, natural.top() - 14.0);
    app.click(handle);
    let (start_tone, start_character) = (tone(&app), character(&app));
    for _ in 0..5 {
        app.key(Key::ArrowRight);
    }
    app.settle(2);
    assert!((tone(&app) - (start_tone + 0.05)).abs() < 1e-9, "Right moves Tone toward bright: {} from {start_tone}", tone(&app));
    for _ in 0..3 {
        app.key(Key::ArrowUp);
    }
    app.settle(2);
    assert!((character(&app) - (start_character + 0.03)).abs() < 1e-9, "Up moves Character toward broadcast: {}", character(&app));
    app.key(Key::Home);
    app.settle(2);
    assert_eq!((tone(&app), character(&app)), (0.0, 0.0), "Home returns to Neutral");
}

/// The EQ plot keeps focus while arrow keys move a band, so repeated steps
/// keep working (they used to move focus away after the first).
#[test]
fn equalizer_bands_follow_repeated_arrow_keys() {
    let mut snapshot = workspace_of(&["eq"]);
    snapshot.configuration.graph.nodes[0].eq_bands = vinemix_model::config::default_eq_bands();
    let mut app = AppHarness::with_workspace(1600.0, 1100.0, 1.0, snapshot, Vec::new());
    app.harness.enable_accessibility();
    app.settle(3);
    let gain = |app: &AppHarness, band: usize| app.app.link.state().graph().nodes[0].eq_bands[band].gain_db;
    // Insert adds and selects a band at 1 kHz, 0 dB, once the plot has focus.
    let plot = app.rect_of_named("Equalizer plot").expect("the EQ plot is in the accessibility tree");
    app.click(Pos2::new(plot.left() + 30.0, plot.top() + 30.0));
    app.key(Key::Insert);
    app.settle(2);
    let bands = app.app.link.state().graph().nodes[0].eq_bands.len();
    assert_eq!(bands, 6, "Insert added a band");
    let added = bands - 1;
    let before = gain(&app, added);
    for _ in 0..4 {
        app.key(Key::ArrowUp);
    }
    app.settle(2);
    let after = gain(&app, added);
    assert!(after > before + 1.5, "four Up presses raised the band four steps: {before} to {after}");
    let value = app.harness.accessible("Equalizer plot").and_then(|n| n.value().map(str::to_owned)).unwrap_or_default();
    assert!(value.starts_with("6 of 8 bands. Selected: Band 6"), "the accessible value follows the selection: {value}");
}

/// Where a rendered frame's time goes on a dense workspace (RUST-REV-013 and
/// RUST-REV-030). CPU-side layout and painting only: there is no GPU here.
/// `cargo test -p vinemix-ui --release --lib frame_cost -- --ignored --nocapture`
#[test]
#[ignore = "benchmark; prints timings"]
fn frame_cost() {
    use std::time::Instant;
    let median = |mut samples: Vec<f64>| {
        samples.sort_by(f64::total_cmp);
        samples[samples.len() / 2]
    };
    // 32 nodes (the limit): device nodes with pickers plus a long effects
    // chain, and a synthetic inventory of 40 endpoints and 30 applications.
    let kinds = [
        "microphone", "microphone", "appInput", "virtualInput", "eq", "compressor", "denoiser", "gate", "reverb", "echo", "distortion", "voiceColor",
        "tone", "highPass", "lowPass", "limiter", "ducking", "expander", "deEsser", "plosive", "mix", "mix", "pan", "stereoWidth", "crossfade",
        "alignmentDelay", "control", "output", "output", "virtualOutput", "mono", "channelSwap",
    ];
    let mut snapshot = crate::preview::create_snapshot(false, true);
    let nodes: Vec<_> = kinds
        .iter()
        .enumerate()
        .map(|(index, kind)| {
            let mut node = crate::graph::new_node(kind, 40.0 + 420.0 * (index % 8) as f64, 40.0 + 520.0 * (index / 8) as f64);
            node.id = format!("dense-{index}");
            node
        })
        .collect();
    let edges: Vec<_> = (4..26).map(|i| vinemix_model::config::GraphEdge::audio(&format!("dense-edge-{i}"), &format!("dense-{i}"), &format!("dense-{}", i + 1))).collect();
    snapshot.status.nodes = nodes
        .iter()
        .map(|node| {
            let mut status = vinemix_model::status::NodeStatus { id: node.id.clone(), channels: 2, peak: 0.2, ..Default::default() };
            status.path.state = "Running".into();
            status
        })
        .collect();
    snapshot.endpoints = (0..40)
        .map(|i| vinemix_model::status::Endpoint {
            id: format!("{{0.0.{}.00000000}}.{{example-endpoint-{i:04}}}", i % 2),
            stable_id: format!("example-stable-{i:04}"),
            name: format!("Example device {i} (Example Audio Interface)"),
            direction: if i % 2 == 0 { "capture".into() } else { "render".into() },
            state: if i % 5 == 0 { "Unplugged".into() } else { "Active".into() },
            transport: "physical".into(),
            interface_name: "Example Audio Interface".into(),
            channels: 2,
            rate: 48_000,
            ..Default::default()
        })
        .collect();
    snapshot.applications = (0..30)
        .map(|i| vinemix_model::status::ApplicationInfo {
            process_id: 1000 + i,
            executable_path: format!("C:\\Program Files\\Example {i}\\example{i}.exe"),
            name: format!("Example application {i}"),
            has_audio_session: i % 3 == 0,
        })
        .collect();
    snapshot.configuration.graph.nodes = nodes;
    snapshot.configuration.graph.edges = edges;
    snapshot.configuration.graph.zoom = 1.0;
    snapshot.configuration.graph.pan_x = 0.0;
    snapshot.configuration.graph.pan_y = 0.0;
    let mut app = AppHarness::with_workspace(3600.0, 2300.0, 1.0, snapshot, Vec::new());
    app.settle(30);
    let mut frames = Vec::new();
    for _ in 0..300 {
        let started = Instant::now();
        app.frame();
        frames.push(started.elapsed().as_secs_f64() * 1e6);
    }
    let frame = median(frames);

    // The copies the canvas makes on every frame, timed alone.
    let (graph, endpoints, applications, groups) = {
        let mixer = app.app.link.state();
        (mixer.graph().clone(), mixer.endpoints().to_vec(), mixer.applications().to_vec(), mixer.frames().to_vec())
    };
    let time = |work: &dyn Fn()| {
        let mut samples = Vec::new();
        for _ in 0..2000 {
            let started = Instant::now();
            work();
            samples.push(started.elapsed().as_secs_f64() * 1e6);
        }
        median(samples)
    };
    let graph_copy = time(&|| drop(std::hint::black_box(graph.clone())));
    let inventory_copy = time(&|| drop(std::hint::black_box((endpoints.to_vec(), applications.to_vec(), groups.to_vec()))));
    let ctx = app.harness.ctx.clone();
    let layouts = time(&|| {
        let map: std::collections::HashMap<String, _> = graph.nodes.iter().map(|n| (n.id.clone(), super::super::node_view::layout(&ctx, n, &graph))).collect();
        drop(std::hint::black_box(map));
    });
    // What building one device picker's choice list costs. Before the
    // picker cache (RUST-REV-013) every picker paid this on every frame.
    let binding = graph.nodes[0].device.clone();
    let visibility = crate::availability::Visibility::default();
    let choices = time(&|| {
        let built = crate::availability::build(&endpoints, "capture", &binding, visibility);
        let selected = crate::availability::selected(&built, &binding);
        let entries: Vec<widgets::Choice> = built.iter().map(|c| widgets::Choice { key: c.key(), label: c.label(), enabled: c.selectable, help: c.help.clone() }).collect();
        drop(std::hint::black_box((entries, selected)));
    });
    let pickers = graph.nodes.iter().filter(|n| matches!(n.kind.as_str(), "microphone" | "output")).count();
    println!("dense workspace: {} nodes, {} cables, {} endpoints, {} applications", graph.nodes.len(), graph.edges.len(), endpoints.len(), applications.len());
    println!("whole frame (logic + layout + paint commands), median: {frame:.0} us");
    println!("graph copy: {graph_copy:.1} us each; the canvas makes 2 per frame, the minimap 1  => {:.1} us ({:.1}% of the frame)", graph_copy * 3.0, graph_copy * 3.0 / frame * 100.0);
    println!("inventory and group copies: {inventory_copy:.1} us ({:.1}% of the frame)", inventory_copy / frame * 100.0);
    println!("node layout map rebuild: {layouts:.1} us ({:.1}% of the frame)", layouts / frame * 100.0);
    println!(
        "building one device picker's choice list: {choices:.1} us. {pickers} pickers used to do this every frame ({:.1} us); each now rebuilds only when the inventory, its binding or the visibility settings change, so that cost is no longer part of the frame above",
        choices * pickers as f64
    );
}
