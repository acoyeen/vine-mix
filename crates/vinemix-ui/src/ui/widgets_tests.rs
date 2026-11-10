//! Selector regressions: RUST-REV-002 (changing inventory), RUST-REV-033
//! (open list follows zoom both ways) and RUST-REV-013 (closed selectors
//! measure nothing). Production `selector`, headless.

use eframe::egui::{Id, Key, Pos2, Rect, Vec2};

use super::super::test_support::Harness;
use super::super::theme;
use super::*;

fn choice(key: &str, label: &str, enabled: bool) -> Choice {
    Choice { key: key.into(), label: label.into(), enabled, help: String::new() }
}

fn devices(keys: &[&str]) -> Vec<Choice> {
    keys.iter().map(|k| choice(k, &format!("Example device {k}"), true)).collect()
}

const FACE: Rect = Rect { min: Pos2 { x: 100.0, y: 100.0 }, max: Pos2 { x: 350.0, y: 130.0 } };

fn id() -> Id {
    Id::unique("test-selector")
}

fn show(harness: &mut Harness, selected: Option<usize>, choices: &[Choice], face: Rect, scale: f32) -> Option<usize> {
    harness.frame(|ui| selector(ui, id(), face, selected, "current", choices, true, scale, theme::TEXT, "").1)
}

fn open(harness: &mut Harness, selected: Option<usize>, choices: &[Choice]) {
    let picked = harness.click(FACE.center(), |ui| selector(ui, id(), FACE, selected, "current", choices, true, 1.0, theme::TEXT, "").1);
    assert_eq!(picked, None);
    show(harness, selected, choices, FACE, 1.0);
    assert!(egui::Popup::is_id_open(&harness.ctx, id().with("popup")), "the click opens the list");
}

fn is_open(harness: &Harness) -> bool {
    egui::Popup::is_id_open(&harness.ctx, id().with("popup"))
}

#[test]
fn highlight_helpers_are_bounded_and_identity_based() {
    let list = devices(&["a", "b", "c"]);
    assert_eq!(resolve_highlight(&list, Some("c"), Some(0)), Some(2));
    assert_eq!(resolve_highlight(&list, Some("gone"), Some(1)), Some(1), "a vanished entry falls back to the selection");
    assert_eq!(resolve_highlight(&list, Some("gone"), Some(9)), None, "an out-of-range selection is dropped");
    assert_eq!(resolve_highlight(&[], Some("a"), Some(0)), None);
    // A stale position beyond the list never indexes.
    assert_eq!(next_enabled(&list[..1], Some(2), false), Some(0));
    assert_eq!(next_enabled(&list[..1], Some(2), true), Some(0));
    assert_eq!(next_enabled(&[], None, true), None);
    let mixed = vec![choice("a", "A", false), choice("b", "B", true), choice("c", "C", false)];
    assert_eq!(next_enabled(&mixed, None, true), Some(1));
    assert_eq!(next_enabled(&mixed, None, false), Some(1));
    assert_eq!(next_enabled(&mixed, Some(1), true), None, "only disabled entries follow");
    assert_eq!(next_enabled(&mixed, Some(1), false), None);
}

/// The review's reproduction: three choices, highlight on the last, the
/// inventory shrinks to one, Enter. Previously `index out of bounds`.
#[test]
fn shrinking_inventory_then_enter_does_not_panic_or_pick_a_stranger() {
    let mut harness = Harness::new();
    let three = devices(&["a", "b", "c"]);
    open(&mut harness, Some(0), &three);
    harness.key(Key::ArrowDown);
    show(&mut harness, Some(0), &three, FACE, 1.0);
    harness.key(Key::ArrowDown);
    show(&mut harness, Some(0), &three, FACE, 1.0);
    // "b" and "c" disappear while the list is open with "c" highlighted.
    let one = devices(&["a"]);
    show(&mut harness, Some(0), &one, FACE, 1.0);
    harness.key(Key::Enter);
    let picked = show(&mut harness, Some(0), &one, FACE, 1.0);
    assert_eq!(picked, None, "the vanished highlight resolves to the unchanged selection");
    assert!(!is_open(&harness), "Enter still closes the list");
}

#[test]
fn backwards_navigation_after_a_shrink_stays_in_range() {
    let mut harness = Harness::new();
    let three = devices(&["a", "b", "c"]);
    open(&mut harness, Some(2), &three);
    let one = devices(&["a"]);
    // The caller's selection index is stale too (it pointed at "c").
    harness.key(Key::ArrowUp);
    assert_eq!(show(&mut harness, Some(2), &one, FACE, 1.0), None);
    harness.key(Key::End);
    show(&mut harness, None, &one, FACE, 1.0);
    harness.key(Key::Enter);
    assert_eq!(show(&mut harness, None, &one, FACE, 1.0), Some(0), "End then Enter picks the only remaining entry");
}

#[test]
fn reordered_inventory_keeps_the_highlight_on_the_same_identity() {
    let mut harness = Harness::new();
    let before = devices(&["a", "b", "c"]);
    open(&mut harness, Some(0), &before);
    harness.key(Key::ArrowDown);
    show(&mut harness, Some(0), &before, FACE, 1.0); // highlight "b" at position 1
    // Same entries, new order: "b" is now last and "c" sits at position 1.
    let after = devices(&["a", "c", "b"]);
    show(&mut harness, Some(0), &after, FACE, 1.0);
    harness.key(Key::Enter);
    let picked = show(&mut harness, Some(0), &after, FACE, 1.0);
    assert_eq!(picked.map(|i| after[i].key.as_str()), Some("b"), "Enter chooses the highlighted device, not its old position");
}

#[test]
fn highlighted_entry_that_becomes_disabled_is_not_chosen() {
    let mut harness = Harness::new();
    let before = devices(&["a", "b"]);
    open(&mut harness, Some(0), &before);
    harness.key(Key::ArrowDown);
    show(&mut harness, Some(0), &before, FACE, 1.0);
    let after = vec![choice("a", "Example device a", true), choice("b", "Example device b · Unplugged", false)];
    show(&mut harness, Some(0), &after, FACE, 1.0);
    harness.key(Key::Enter);
    assert_eq!(show(&mut harness, Some(0), &after, FACE, 1.0), None);
    assert!(!is_open(&harness));
}

#[test]
fn emptied_inventory_is_harmless_while_open() {
    let mut harness = Harness::new();
    let before = devices(&["a", "b"]);
    open(&mut harness, Some(1), &before);
    for key in [Key::ArrowUp, Key::ArrowDown, Key::Home, Key::End, Key::Enter] {
        harness.key(key);
        assert_eq!(show(&mut harness, Some(1), &[], FACE, 1.0), None);
    }
}

fn face_at(scale: f32) -> Rect {
    Rect::from_min_size(Pos2::new(100.0, 100.0), Vec2::new(250.0 * scale, 30.0 * scale))
}

fn popup_width_at(harness: &mut Harness, choices: &[Choice], scale: f32) -> f32 {
    let face = face_at(scale);
    harness.settle(8, |ui| selector(ui, id(), face, Some(0), "current", choices, true, scale, theme::TEXT, "").1);
    harness.area_rect(id().with("popup")).expect("the list stays open").width()
}

/// The review's table: open at 100 %, zoom in to 200 %, back out to 50 %
/// without closing. The list previously stayed 500 wide.
#[test]
fn open_list_shrinks_as_well_as_grows_with_zoom() {
    let mut harness = Harness::sized(1600.0, 1000.0);
    let choices = vec![Choice::fixed("One"), Choice::fixed("Two"), Choice::fixed("Three")];
    open(&mut harness, Some(0), &choices);
    let mut widths = Vec::new();
    for scale in [1.0, 1.5, 2.0, 1.5, 1.0, 0.75, 0.5] {
        widths.push((scale, popup_width_at(&mut harness, &choices, scale)));
        assert!(is_open(&harness), "zooming must not close the list");
    }
    for (scale, width) in &widths {
        let expected = 250.0 * scale;
        assert!((width - expected).abs() <= 1.0, "at {scale}x the list is {width} wide, expected {expected}: {widths:?}");
    }
    // Highlight and keyboard still work after the resize.
    harness.key(Key::ArrowDown);
    show(&mut harness, Some(0), &choices, face_at(0.5), 0.5);
    harness.key(Key::Enter);
    assert_eq!(show(&mut harness, Some(0), &choices, face_at(0.5), 0.5), Some(1));
}

/// Long labels keep a readable minimum: the list may stay wider than a
/// zoomed-out selector, but it still shrinks back to that minimum and never
/// exceeds the window.
#[test]
fn long_labels_keep_a_readable_minimum_within_the_window() {
    let mut harness = Harness::sized(900.0, 700.0);
    let long = "Example interface with a deliberately long endpoint name (Example Audio Device) · Device 2";
    let choices = vec![Choice::fixed("Not assigned"), Choice::fixed(long)];
    open(&mut harness, Some(0), &choices);
    let natural = popup_width_at(&mut harness, &choices, 1.0);
    assert!(natural > 250.0, "the label widens the list beyond the selector: {natural}");
    let zoomed_in = popup_width_at(&mut harness, &choices, 3.0);
    assert!((zoomed_in - 750.0).abs() <= 1.0, "a wide selector wins: {zoomed_in}");
    let zoomed_out = popup_width_at(&mut harness, &choices, 0.5);
    assert!((zoomed_out - natural).abs() <= 1.0, "zooming out returns to the label minimum {natural}, got {zoomed_out}");
    harness.resize(400.0, 700.0);
    let narrow = popup_width_at(&mut harness, &choices, 0.5);
    assert!(narrow <= 392.0 + 0.5, "the list is bounded by the window: {narrow}");
}

#[test]
fn display_scale_does_not_change_logical_list_width() {
    for ppp in [1.0, 1.25, 1.5, 2.0] {
        let mut harness = Harness::sized(1600.0, 1000.0).pixels_per_point(ppp);
        let choices = vec![Choice::fixed("One"), Choice::fixed("Two")];
        open(&mut harness, Some(0), &choices);
        popup_width_at(&mut harness, &choices, 2.0);
        let width = popup_width_at(&mut harness, &choices, 0.75);
        assert!((width - 187.5).abs() <= 1.0, "at {ppp} px/pt the list is {width} wide");
    }
}

/// A closed selector lays out only its face text, however long the list.
#[test]
fn closed_selector_does_not_measure_its_choices() {
    let mut harness = Harness::new();
    let choices: Vec<Choice> = (0..200).map(|i| Choice::fixed(&format!("Unique closed-list label {i}"))).collect();
    harness.settle(3, |ui| selector(ui, id(), FACE, Some(0), "current", &choices, true, 1.0, theme::TEXT, "").1);
    let before = harness.ctx.fonts(|f| f.num_galleys_in_cache());
    harness.settle(3, |ui| selector(ui, id(), FACE, Some(0), "current", &choices, true, 1.0, theme::TEXT, "").1);
    let closed = harness.ctx.fonts(|f| f.num_galleys_in_cache());
    assert!(closed <= before && closed < 50, "closed selector cached {closed} text layouts for 200 choices");
}

// ----- hover help: keyboard access (RUST-REV-034), placement (RUST-REV-038) --

use eframe::egui::Modifiers;

const A: Rect = Rect { min: Pos2 { x: 50.0, y: 50.0 }, max: Pos2 { x: 230.0, y: 80.0 } };
const B: Rect = Rect { min: Pos2 { x: 50.0, y: 200.0 }, max: Pos2 { x: 230.0, y: 230.0 } };

/// Two ordinary buttons with help; returns `(A clicked, B clicked)`.
fn two_buttons(ui: &mut Ui) -> (bool, bool) {
    help_frame(ui.ctx());
    let a = button(ui, Id::unique("help-a"), A, "Alpha", ButtonStyle::console(13.0), true, 1.0).tip("Alpha\nHelp for the first control.");
    let b = button(ui, Id::unique("help-b"), B, "Beta", ButtonStyle::console(13.0), true, 1.0).tip("Beta\nHelp for the second control.");
    (a.clicked(), b.clicked())
}

fn focused(harness: &Harness) -> Option<Id> {
    harness.ctx.memory(|m| m.focused())
}

fn wait(harness: &mut Harness, seconds: f64) {
    harness.frame_after(seconds, two_buttons);
    harness.frame(two_buttons);
}

#[test]
fn keyboard_focus_shows_help_after_the_delay_without_a_pointer() {
    let mut harness = Harness::new();
    harness.settle(2, two_buttons);
    harness.key(Key::Tab);
    harness.settle(2, two_buttons);
    assert_eq!(focused(&harness), Some(Id::unique("help-a")), "Tab reached the first control");
    assert!(harness.find_text("Help for the first control").is_none(), "not before the delay");
    wait(&mut harness, 0.5);
    let position = harness.find_text("Help for the first control").expect("help for the keyboard-focused control");
    assert!(position.y >= A.bottom() && position.y < A.bottom() + 40.0, "placed just below its control: {position:?}");
    assert!(position.x >= A.left() && position.x < A.right(), "{position:?}");
    assert_eq!(focused(&harness), Some(Id::unique("help-a")), "showing help does not move focus");
}

#[test]
fn keyboard_help_hands_over_with_focus_and_is_dismissed_by_other_input() {
    let mut harness = Harness::new();
    harness.settle(2, two_buttons);
    harness.key(Key::Tab);
    harness.settle(2, two_buttons);
    wait(&mut harness, 0.5);
    assert!(harness.find_text("first control").is_some());
    // Tab again: the first control's help goes at once, the second's follows.
    harness.key(Key::Tab);
    harness.settle(2, two_buttons);
    assert_eq!(focused(&harness), Some(Id::unique("help-b")));
    assert!(harness.find_text("first control").is_none(), "help left with focus");
    assert!(harness.find_text("second control").is_none(), "the new control waits for the delay");
    wait(&mut harness, 0.5);
    let position = harness.find_text("second control").expect("help followed focus");
    assert!(position.y >= B.bottom() && position.y < B.bottom() + 40.0, "{position:?}");
    // Another key dismisses it and keeps focus where it was.
    harness.key(Key::F2);
    harness.settle(2, two_buttons);
    assert!(harness.find_text("second control").is_none());
    assert_eq!(focused(&harness), Some(Id::unique("help-b")));
    wait(&mut harness, 1.0);
    assert!(harness.find_text("second control").is_none(), "it does not return by itself");
    // Escape dismisses too (egui also releases the focus itself on Escape).
    harness.set_modifiers(Modifiers::CTRL | Modifiers::SHIFT);
    harness.key(Key::F10);
    harness.frame(two_buttons);
    harness.set_modifiers(Modifiers::NONE);
    harness.frame(two_buttons);
    assert!(harness.find_text("second control").is_some());
    harness.key(Key::Escape);
    harness.settle(2, two_buttons);
    assert!(harness.find_text("second control").is_none());
}

#[test]
fn shortcut_toggles_help_for_the_focused_control_at_once() {
    let mut harness = Harness::new();
    harness.settle(2, two_buttons);
    // Focus by a pointer click: no automatic keyboard help.
    harness.click(A.center(), two_buttons);
    harness.move_to(Pos2::new(600.0, 500.0));
    harness.frame(two_buttons);
    harness.ctx.memory_mut(|m| m.request_focus(Id::unique("help-a")));
    wait(&mut harness, 1.0);
    assert!(harness.find_text("first control").is_none(), "pointer focus alone shows nothing");
    let chord = |harness: &mut Harness| {
        harness.set_modifiers(Modifiers::CTRL | Modifiers::SHIFT);
        harness.key(Key::F10);
        harness.frame(two_buttons);
        harness.set_modifiers(Modifiers::NONE);
        harness.frame(two_buttons);
    };
    chord(&mut harness);
    assert!(harness.find_text("first control").is_some(), "Ctrl+Shift+F10 shows it without waiting");
    assert_eq!(focused(&harness), Some(Id::unique("help-a")));
    chord(&mut harness);
    assert!(harness.find_text("first control").is_none(), "and hides it again");
    // Shown again, then dismissed by a pointer press elsewhere.
    chord(&mut harness);
    assert!(harness.find_text("first control").is_some());
    harness.press(Pos2::new(600.0, 500.0));
    harness.frame(two_buttons);
    harness.release(Pos2::new(600.0, 500.0));
    harness.settle(2, two_buttons);
    assert!(harness.find_text("first control").is_none());
}

#[test]
fn pointer_hover_help_is_unchanged() {
    let mut harness = Harness::new();
    harness.settle(2, two_buttons);
    harness.move_to(A.center());
    harness.frame(two_buttons);
    assert!(harness.find_text("first control").is_none());
    wait(&mut harness, 0.5);
    let position = harness.find_text("first control").expect("hover help after the delay");
    assert!(position.y >= A.bottom() && position.y < A.bottom() + 40.0, "{position:?}");
    harness.move_to(Pos2::new(600.0, 500.0));
    harness.settle(3, two_buttons);
    assert!(harness.find_text("first control").is_none());
}

fn menu(ui: &mut Ui) -> bool {
    help_frame(ui.ctx());
    let mut chosen = false;
    egui::Area::new(Id::unique("test-menu")).fixed_pos(Pos2::new(300.0, 100.0)).show(ui.ctx(), |ui| {
        menu_frame().show(ui, |ui| {
            menu_body(ui, 206.0);
            chosen |= menu_item(ui, "Example action", "", true, "Example action\nDoes the example thing.");
            menu_item(ui, "Unavailable action", "", false, "Unavailable action\nSelect a node first.");
        });
    });
    chosen
}

#[test]
fn menu_rows_show_help_by_keyboard_and_explain_disabled_rows_on_hover() {
    let mut harness = Harness::new();
    harness.settle(2, menu);
    harness.key(Key::Tab);
    harness.settle(2, menu);
    harness.frame_after(0.5, menu);
    harness.frame(menu);
    assert!(harness.find_text("Does the example thing").is_some(), "the focused menu row shows its help");
    harness.key(Key::Enter);
    assert!(harness.frame(menu), "the row still activates from the keyboard");
    // The disabled row explains itself on hover.
    let row = harness.find_text("Unavailable action").expect("the disabled row is painted");
    harness.move_to(row + Vec2::new(20.0, 6.0));
    harness.settle(2, menu);
    harness.frame_after(0.5, menu);
    harness.frame(menu);
    assert!(harness.find_text("Select a node first").is_some(), "disabled explanation on hover");
}

#[test]
fn help_is_also_the_accessible_description_without_being_shown() {
    let mut harness = Harness::new();
    harness.enable_accessibility();
    harness.settle(3, two_buttons);
    let node = harness.accessible("Alpha").expect("the button is in the accessibility tree");
    assert_eq!(node.description(), Some("Help for the first control."), "the popup's body, without repeating the name");
    assert!(harness.find_text("first control").is_none(), "nothing is popped up for it");
    // A fuller description can be given separately from the concise popup.
    harness.settle(3, |ui| {
        help_frame(ui.ctx());
        button(ui, Id::unique("help-c"), A, "Gamma", ButtonStyle::console(13.0), true, 1.0).tip_described("Gamma\nShort.", "Longer instructions for assistive technology.");
    });
    assert_eq!(harness.accessible("Gamma").and_then(|n| n.description()), Some("Longer instructions for assistive technology."));
}

/// The cable case at helper level: an owner as large as the canvas, help
/// requested for a point well inside it.
#[test]
fn anchored_help_appears_at_its_anchor_not_at_the_owners_edge() {
    let owner = Rect::from_min_max(Pos2::new(20.0, 50.0), Pos2::new(980.0, 650.0));
    for (pointer, label) in [
        (Pos2::new(320.0, 250.0), "centre"),
        (Pos2::new(40.0, 70.0), "top left"),
        (Pos2::new(960.0, 70.0), "top right"),
        (Pos2::new(40.0, 630.0), "bottom left"),
        (Pos2::new(960.0, 630.0), "bottom right"),
    ] {
        let mut harness = Harness::sized(1000.0, 700.0);
        let mut draw = |ui: &mut Ui| {
            help_frame(ui.ctx());
            let response = ui.interact(owner, Id::unique("big-owner"), Sense::click_and_drag());
            if let Some(position) = ui.input(|i| i.pointer.hover_pos()) {
                response.tip_at(Rect::from_center_size(position, Vec2::splat(12.0)), "Example source → Example destination");
            }
        };
        harness.move_to(pointer);
        harness.settle(2, &mut draw);
        harness.frame_after(0.5, &mut draw);
        harness.frame(&mut draw);
        let position = harness.find_text("Example source").unwrap_or_else(|| panic!("{label}: help was shown"));
        let distance = (position - pointer).length();
        assert!(distance < 360.0, "{label}: help at {position:?} is {distance} from the hovered point {pointer:?}");
        assert!((position.y - pointer.y).abs() < 60.0, "{label}: vertically at the hovered point, not the owner's edge: {position:?}");
        assert!(harness.screen().contains(position), "{label}: inside the window");
    }
}
