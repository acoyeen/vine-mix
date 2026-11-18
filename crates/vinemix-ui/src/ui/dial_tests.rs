//! Exact-entry interaction of the shared dial (RUST-REV-037 and the
//! behaviour it must preserve). Production `dial::show`, headless, with
//! synthetic pointer and keyboard input. No value is applied to a live mix.

use eframe::egui::{self, Id, Key, Modifiers, Pos2, Rect, Vec2};

use super::super::test_support::Harness;
use super::*;

const VALUE: f64 = -12.3;
const TEXT: &str = "-12.3 dB";

fn level() -> Spec {
    Spec::new("level", "Level", -60.0, 12.0, 0.1, "dB", 0.0, Variant::Row)
}

fn id() -> Id {
    Id::unique("test-dial")
}

fn entry_id() -> Id {
    id().with("entry")
}

/// The layout of one dial at a canvas zoom.
struct Fixture {
    harness: Harness,
    state: DialState,
    value: f64,
    scale: f32,
    face: Rect,
    entry: Rect,
}

impl Fixture {
    fn new(scale: f32) -> Self {
        let face = Rect::from_min_size(Pos2::new(100.0, 100.0), Vec2::splat(86.0 * scale));
        let entry = Rect::from_min_size(Pos2::new(100.0, 100.0 + 100.0 * scale), Vec2::new(110.0 * scale, 28.0 * scale));
        let mut fixture = Self { harness: Harness::sized(1400.0, 900.0), state: DialState::default(), value: VALUE, scale, face, entry };
        fixture.settle(2);
        fixture
    }

    /// One frame; an applied change updates the value like the node would.
    fn frame(&mut self) -> Outcome {
        let (spec, id, face, entry, scale) = (level(), id(), self.face, self.entry, self.scale);
        let (state, value) = (&mut self.state, self.value);
        let outcome = self.harness.frame(|ui| show(ui, id, &spec, value, 0.0, state, face, entry, scale, true));
        if let Some(next) = outcome.changed {
            self.value = next;
        }
        outcome
    }

    fn settle(&mut self, frames: usize) {
        for _ in 0..frames {
            self.frame();
        }
    }

    /// Screen x of the boundary before character `index` of the shown text.
    fn boundary(&self, index: usize) -> f32 {
        let font = theme::font(12.0 * self.scale, Weight::Mono);
        let shown = self.state.draft.clone().unwrap_or_else(|| TEXT.to_owned());
        let width = |text: &str| self.harness.ctx.fonts_mut(|f| f.layout_no_wrap(text.to_owned(), font.clone(), Color32::WHITE).size().x);
        let prefix: String = shown.chars().take(index).collect();
        self.entry.center().x - width(&shown) / 2.0 + width(&prefix)
    }

    fn at(&self, index: usize) -> Pos2 {
        Pos2::new(self.boundary(index), self.entry.center().y)
    }

    /// Press at `from`, move to `to` in steps, release at `to`.
    fn drag(&mut self, from: Pos2, to: Pos2) {
        self.harness.move_to(from);
        self.frame();
        self.harness.press(from);
        self.frame();
        for step in 1..=6 {
            let t = step as f32 / 6.0;
            self.harness.move_to(from + (to - from) * t);
            self.frame();
        }
        self.harness.release(to);
        self.frame();
        self.settle(2);
    }

    fn click(&mut self, at: Pos2) {
        self.harness.move_to(at);
        self.frame();
        self.harness.press(at);
        self.frame();
        self.harness.release(at);
        self.frame();
        self.settle(2);
    }

    fn key(&mut self, key: Key) {
        self.harness.key(key);
        self.frame();
    }

    fn type_text(&mut self, text: &str) {
        self.harness.text(text);
        self.frame();
    }

    /// `(anchor, moving end)` of the entry's selection, in characters.
    fn selection(&self) -> Option<(usize, usize)> {
        let state = egui::TextEdit::load_state(&self.harness.ctx, entry_id())?;
        state.cursor.char_range().map(|range| (range.secondary.index.0, range.primary.index.0))
    }

    fn selected(&self) -> std::ops::Range<usize> {
        let (a, b) = self.selection().expect("the entry has a cursor");
        a.min(b)..a.max(b)
    }

    fn entry_focused(&self) -> bool {
        self.harness.ctx.memory(|m| m.has_focus(entry_id()))
    }

    /// Focus arriving from the keyboard: it moves while a frame is being
    /// built (as Tab does), before the entry is laid out.
    fn focus_entry_from_keyboard(&mut self) {
        let (spec, id, face, entry, scale) = (level(), id(), self.face, self.entry, self.scale);
        let (state, value) = (&mut self.state, self.value);
        self.harness.frame(|ui| {
            ui.memory_mut(|m| m.request_focus(entry_id()));
            show(ui, id, &spec, value, 0.0, state, face, entry, scale, true)
        });
        self.settle(2);
        assert!(self.entry_focused());
    }

    /// Lets time pass so the next click is not a double-click.
    fn pause(&mut self) {
        let (spec, id, face, entry, scale) = (level(), id(), self.face, self.entry, self.scale);
        let (state, value) = (&mut self.state, self.value);
        self.harness.frame_after(1.0, |ui| show(ui, id, &spec, value, 0.0, state, face, entry, scale, true));
    }
}

const ZOOMS: [f32; 3] = [0.75, 1.0, 2.0];
const LENGTH: usize = 8;

/// The reported defect: dragging from the end of the value back to its start
/// in an entry that did not yet have focus left nothing selected.
#[test]
fn backwards_drag_into_an_unfocused_entry_selects_the_value() {
    for scale in ZOOMS {
        let mut dial = Fixture::new(scale);
        let (from, to) = (dial.at(LENGTH) + Vec2::new(3.0, 0.0), dial.at(0) - Vec2::new(3.0, 0.0));
        dial.drag(from, to);
        assert!(dial.entry_focused(), "zoom {scale}");
        assert_eq!(dial.selection(), Some((LENGTH, 0)), "zoom {scale}: anchored where the press began, extended to the start");
        assert_eq!(dial.state.draft.as_deref(), Some(TEXT), "the draft is the exact value, untouched");
    }
}

#[test]
fn forwards_drag_into_an_unfocused_entry_selects_the_value() {
    for scale in ZOOMS {
        let mut dial = Fixture::new(scale);
        let (from, to) = (dial.at(0) - Vec2::new(3.0, 0.0), dial.at(LENGTH) + Vec2::new(3.0, 0.0));
        dial.drag(from, to);
        assert_eq!(dial.selection(), Some((0, LENGTH)), "zoom {scale}");
    }
}

/// A partial drag selection is kept as dragged, in either direction, whether
/// or not the entry already had focus — never widened to everything.
#[test]
fn partial_selections_keep_their_extent_and_direction() {
    for scale in ZOOMS {
        for focused_first in [false, true] {
            for (from, to) in [(6usize, 1usize), (1, 6), (LENGTH, 4), (3, LENGTH)] {
                let mut dial = Fixture::new(scale);
                if focused_first {
                    dial.focus_entry_from_keyboard();
                }
                let nudge = Vec2::new(0.25 * scale, 0.0);
                let (start, end) = (dial.at(from) + nudge, dial.at(to) + nudge);
                dial.drag(start, end);
                assert_eq!(
                    dial.selection(),
                    Some((from, to)),
                    "zoom {scale}, already focused {focused_first}: drag from character {from} to {to}"
                );
            }
        }
    }
}

#[test]
fn backwards_and_forwards_full_drags_work_in_an_already_focused_entry() {
    for scale in ZOOMS {
        let mut dial = Fixture::new(scale);
        dial.focus_entry_from_keyboard();
        let (right, left) = (dial.at(LENGTH) + Vec2::new(3.0, 0.0), dial.at(0) - Vec2::new(3.0, 0.0));
        dial.drag(right, left);
        assert_eq!(dial.selection(), Some((LENGTH, 0)), "zoom {scale}");
        dial.drag(left, right);
        assert_eq!(dial.selection(), Some((0, LENGTH)), "zoom {scale}");
    }
}

/// Releasing outside the field still completes the selection.
#[test]
fn drag_released_outside_the_entry_keeps_the_selection() {
    let mut dial = Fixture::new(1.0);
    let from = dial.at(5) + Vec2::new(0.25, 0.0);
    let outside = Pos2::new(dial.entry.left() - 60.0, dial.entry.center().y);
    dial.drag(from, outside);
    assert!(dial.entry_focused());
    assert_eq!(dial.selection(), Some((5, 0)), "released left of the field: selected back to the start");
    // Released past the right edge: forwards to the end.
    let mut dial = Fixture::new(1.0);
    let from = dial.at(2) + Vec2::new(0.25, 0.0);
    let outside = Pos2::new(dial.entry.right() + 60.0, dial.entry.center().y);
    dial.drag(from, outside);
    assert_eq!(dial.selection(), Some((2, LENGTH)));
}

/// The shortcut that motivated select-all is kept: one plain click on an
/// unfocused entry selects the whole value, so typing replaces it.
#[test]
fn plain_click_on_an_unfocused_entry_selects_everything_then_typing_replaces() {
    for scale in ZOOMS {
        let mut dial = Fixture::new(scale);
        let middle = dial.at(4) + Vec2::new(0.25 * scale, 0.0);
        dial.click(middle);
        assert!(dial.entry_focused());
        assert_eq!(dial.selected(), 0..LENGTH, "zoom {scale}");
        dial.type_text("5");
        assert_eq!(dial.state.draft.as_deref(), Some("5"), "typing replaced the selected value");
        dial.key(Key::Enter);
        dial.settle(2);
        assert_eq!(dial.value, 5.0, "Enter applies the typed value");
        assert!(!dial.entry_focused(), "and leaves the entry");
    }
}

/// A click in an entry that already has focus places the caret (ordinary
/// editing); it does not reselect everything.
#[test]
fn click_in_an_already_focused_entry_places_the_caret() {
    let mut dial = Fixture::new(1.0);
    dial.focus_entry_from_keyboard();
    assert_eq!(dial.selected(), 0..LENGTH, "keyboard focus selects everything");
    dial.pause();
    let position = dial.at(3) + Vec2::new(0.25, 0.0);
    dial.click(position);
    assert_eq!(dial.selection(), Some((3, 3)));
    dial.type_text("9");
    assert_eq!(dial.state.draft.as_deref(), Some("-129.3 dB"), "typing inserts at the caret");
}

#[test]
fn keyboard_focus_selects_everything_and_shift_arrows_extend() {
    let mut dial = Fixture::new(1.0);
    dial.focus_entry_from_keyboard();
    assert_eq!(dial.selected(), 0..LENGTH);
    // Collapse to the end, then extend backwards with Shift+Left.
    dial.key(Key::End);
    assert_eq!(dial.selection(), Some((LENGTH, LENGTH)));
    dial.harness.set_modifiers(Modifiers::SHIFT);
    dial.key(Key::ArrowLeft);
    dial.key(Key::ArrowLeft);
    dial.harness.set_modifiers(Modifiers::NONE);
    dial.frame();
    assert_eq!(dial.selection(), Some((LENGTH, LENGTH - 2)), "Shift+Left extends from the caret");
}

/// Typing a number on the focused dial face starts exact entry with exactly
/// what was typed, and further keys continue it. (The first keystroke used
/// to be entered twice and then replaced by the second: "12" became "2".)
#[test]
fn typing_on_the_dial_face_starts_exact_entry_and_continues_it() {
    for scale in ZOOMS {
        let mut dial = Fixture::new(scale);
        dial.harness.ctx.memory_mut(|m| m.request_focus(id().with("face")));
        dial.settle(2);
        dial.type_text("1");
        assert!(dial.entry_focused(), "the entry takes over");
        assert_eq!(dial.state.draft.as_deref(), Some("1"), "zoom {scale}: one keystroke, one character");
        assert_eq!(dial.selection(), Some((1, 1)), "the caret follows what was typed");
        dial.type_text("2");
        assert_eq!(dial.state.draft.as_deref(), Some("12"), "zoom {scale}: the second digit continues the number");
        dial.key(Key::Enter);
        dial.settle(2);
        assert_eq!(dial.value, 12.0, "zoom {scale}");
    }
    // A leading minus works the same way.
    let mut dial = Fixture::new(1.0);
    dial.harness.ctx.memory_mut(|m| m.request_focus(id().with("face")));
    dial.settle(2);
    for key in ["-", "6"] {
        dial.type_text(key);
    }
    assert_eq!(dial.state.draft.as_deref(), Some("-6"));
    dial.key(Key::Enter);
    dial.settle(2);
    assert_eq!(dial.value, -6.0);
}

#[test]
fn escape_cancels_and_invalid_entries_are_kept_for_correction() {
    let mut dial = Fixture::new(1.0);
    dial.click(dial.at(4));
    dial.type_text("3");
    dial.key(Key::Escape);
    dial.settle(2);
    assert_eq!(dial.value, VALUE, "Escape discards the draft");
    assert!(dial.state.draft.is_none() && !dial.entry_focused());
    // Out of range: not applied, kept and marked.
    dial.pause();
    dial.click(dial.at(4));
    dial.type_text("999");
    dial.key(Key::Enter);
    dial.settle(2);
    assert_eq!(dial.value, VALUE);
    assert!(dial.state.invalid);
    assert_eq!(dial.state.draft.as_deref(), Some("999"));
}

#[test]
fn double_click_on_the_entry_resets_to_the_default() {
    let mut dial = Fixture::new(1.0);
    let middle = dial.at(4) + Vec2::new(0.25, 0.0);
    dial.harness.move_to(middle);
    dial.frame();
    for _ in 0..2 {
        dial.harness.press(middle);
        dial.frame();
        dial.harness.release(middle);
        dial.frame();
    }
    dial.settle(2);
    assert_eq!(dial.value, 0.0, "the double-click reset contract is unchanged");
}

/// Dragging with the given button from the middle of the dial face upwards.
fn face_drag(dial: &mut Fixture, button: egui::PointerButton) -> (bool, bool) {
    let from = dial.face.center();
    let to = from - Vec2::new(0.0, 60.0 * dial.scale);
    let (mut started, mut ended) = (false, false);
    let mut frame = |dial: &mut Fixture| {
        let outcome = dial.frame();
        started |= outcome.gesture_started;
        ended |= outcome.gesture_ended;
    };
    dial.harness.move_to(from);
    frame(dial);
    dial.harness.event(egui::Event::PointerButton { pos: from, button, pressed: true, modifiers: Modifiers::NONE });
    frame(dial);
    for step in 1..=6 {
        dial.harness.move_to(from + (to - from) * (step as f32 / 6.0));
        frame(dial);
    }
    dial.harness.event(egui::Event::PointerButton { pos: to, button, pressed: false, modifiers: Modifiers::NONE });
    frame(dial);
    frame(dial);
    (started, ended)
}

/// The middle button pans the canvas. Starting that drag over a dial used to
/// turn the dial as well.
#[test]
fn only_the_primary_button_turns_a_dial() {
    for scale in ZOOMS {
        for button in [egui::PointerButton::Middle, egui::PointerButton::Secondary] {
            let mut dial = Fixture::new(scale);
            let (started, ended) = face_drag(&mut dial, button);
            assert_eq!(dial.value, VALUE, "zoom {scale}, {button:?}: the value is untouched");
            assert!(!started && !ended && !dial.state.dragging, "zoom {scale}, {button:?}: no edit gesture");
        }
        let mut dial = Fixture::new(scale);
        let (started, ended) = face_drag(&mut dial, egui::PointerButton::Primary);
        assert!(dial.value > VALUE, "zoom {scale}: dragging up raises the value");
        assert!(started && ended && !dial.state.dragging, "zoom {scale}: one complete gesture");
    }
}

#[test]
fn assistive_range_actions_change_the_dial_value() {
    use egui::accesskit::{Action, ActionData, ActionRequest, TreeId};
    let mut fixture = Fixture::new(1.0);
    fixture.harness.enable_accessibility();
    fixture.frame();
    let node = fixture.harness.accessible("Level").unwrap();
    assert_eq!((node.min_numeric_value(), node.max_numeric_value(), node.numeric_value_step()), (Some(-60.0), Some(12.0), Some(0.1)));
    for (action, data, expected) in [
        (Action::SetValue, Some(ActionData::NumericValue(-4.0)), -4.0),
        (Action::Increment, None, -3.9),
        (Action::Decrement, None, -4.0),
        (Action::SetValue, Some(ActionData::NumericValue(50.0)), 12.0),
    ] {
        fixture.harness.event(egui::Event::AccessKitActionRequest(ActionRequest {
            action, target_tree: TreeId::ROOT, target_node: id().with("face").accesskit_id(), data,
        }));
        fixture.frame();
        assert!((fixture.value - expected).abs() < 1e-10);
    }
}
