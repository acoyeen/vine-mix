//! The two-axis voice pad (`VoiceColorPad`): Warm ↔ Bright across,
//! Natural ↔ Broadcast character upward, neutral at bottom centre.

use crate::ui::widgets::Tip as _;
use eframe::egui::{self, Align2, Key, Pos2, Rect, Sense, Stroke, Vec2};

use super::node_view::ItemCtx;
use super::theme::{self, Weight};

#[derive(Clone, Debug, Default)]
pub struct VoiceState {
    dragging: bool,
    last: Pos2,
}

fn pad_bounds(size: Vec2) -> Rect {
    let side = (size.x - 30.0).min(size.y - 88.0).max(1.0);
    Rect::from_min_size(Pos2::new((size.x - side - 24.0) / 2.0, (size.y - side - 84.0) / 2.0), Vec2::new(side + 24.0, side + 84.0))
}

fn travel_bounds(size: Vec2) -> Rect {
    let b = pad_bounds(size);
    Rect::from_min_size(Pos2::new(b.left() + 12.0, b.top() + 42.0), Vec2::new(b.width() - 24.0, b.height() - 84.0))
}

pub fn description(tone: f64, ambience: f64) -> String {
    if tone == 0.0 && ambience == 0.0 {
        return "Neutral".into();
    }
    let tone_text = if tone == 0.0 {
        "Balanced".to_owned()
    } else {
        format!("{} {}%", if tone < 0.0 { "Warm" } else { "Bright" }, crate::ui::dial::format_number(tone.abs() * 100.0, "0.#"))
    };
    let character = if ambience == 0.0 { "Natural".to_owned() } else { format!("Broadcast {}%", crate::ui::dial::format_number(ambience * 100.0, "0.#")) };
    format!("{tone_text} · {character}")
}

pub fn show(ctx: &mut ItemCtx<'_, '_>, world: Rect) {
    let s = ctx.s;
    let node = ctx.node;
    let screen = ctx.screen(world);
    let size = world.size();
    let travel = travel_bounds(size);
    let bounds = pad_bounds(size);
    let to_screen = |p: Pos2| screen.min + p.to_vec2() * s;
    let to_local = |p: Pos2| ((p - screen.min) / s).to_pos2();
    let enabled = ctx.env.can_edit;
    let tone = node.voice_color.tone.clamp(-1.0, 1.0);
    let ambience = node.voice_color.ambience.clamp(0.0, 1.0);
    let handle = |tone: f64, ambience: f64| {
        Pos2::new(travel.left() + ((tone + 1.0) * 0.5) as f32 * travel.width(), travel.bottom() - ambience as f32 * travel.height())
    };
    let id = ctx.id.with("voice");
    let response = ctx.ui.interact(screen, id, if enabled { Sense::click_and_drag() } else { Sense::hover() });
    let adjustment = travel.expand(10.0);
    let mut next: Option<(f64, f64)> = None;
    let mut started = false;
    let mut ended = false;
    let state = &mut ctx.runtime.voice;
    let set = |t: f64, a: f64| -> Option<(f64, f64)> {
        let (t, a) = (t.clamp(-1.0, 1.0), a.clamp(0.0, 1.0));
        (t != tone || a != ambience).then_some((t, a))
    };
    if enabled {
        if response.double_clicked() {
            if response.interact_pointer_pos().is_some_and(|p| adjustment.contains(to_local(p))) {
                if state.dragging {
                    state.dragging = false;
                    ended = true;
                }
                next = set(0.0, 0.0);
            }
        } else if response.is_pointer_button_down_on() && ctx.ui.input(|i| i.pointer.primary_pressed()) {
            if let Some(origin) = ctx.ui.input(|i| i.pointer.press_origin()) {
                let point = to_local(origin);
                if adjustment.contains(point) {
                    response.request_focus();
                    if !state.dragging {
                        started = true;
                    }
                    state.dragging = true;
                    state.last = point;
                    if point.distance(handle(tone, ambience)) > 12.0 {
                        next = set(
                            f64::from((point.x - travel.left()) / travel.width()) * 2.0 - 1.0,
                            f64::from((travel.bottom() - point.y) / travel.height()),
                        );
                    }
                }
            }
        }
        if state.dragging {
            if let Some(pointer) = ctx.ui.input(|i| i.pointer.latest_pos()) {
                let point = to_local(pointer);
                let delta = point - state.last;
                state.last = point;
                if delta != Vec2::ZERO {
                    let fine = if ctx.ui.input(|i| i.modifiers.shift) { 0.1 } else { 1.0 };
                    let (t, a) = next.unwrap_or((tone, ambience));
                    next = set(t + f64::from(delta.x / travel.width()) * 2.0 * fine, a - f64::from(delta.y / travel.height()) * fine).or(next);
                }
            }
            if !ctx.ui.input(|i| i.pointer.primary_down()) {
                state.dragging = false;
                ended = true;
            }
        }
        if response.has_focus() {
            let (shift, blocked) = ctx.ui.input(|i| (i.modifiers.shift, i.modifiers.ctrl || i.modifiers.alt || i.modifiers.mac_cmd));
            if !blocked {
                let step = if shift { 0.001 } else { 0.01 };
                let pressed = |k: Key| ctx.ui.input(|i| i.key_pressed(k));
                let (t, a) = next.unwrap_or((tone, ambience));
                if pressed(Key::ArrowLeft) {
                    next = set(t - step, a);
                } else if pressed(Key::ArrowRight) {
                    next = set(t + step, a);
                } else if pressed(Key::ArrowDown) {
                    next = set(t, a - step);
                } else if pressed(Key::ArrowUp) {
                    next = set(t, a + step);
                } else if pressed(Key::Home) {
                    next = set(0.0, 0.0);
                } else if pressed(Key::Escape) && state.dragging {
                    state.dragging = false;
                    ended = true;
                }
            }
        }
    }
    let dragging = state.dragging;
    let (shown_tone, shown_ambience) = next.unwrap_or((tone, ambience));
    let painter = ctx.ui.painter().clone();
    let focused = response.has_focus();
    painter.rect(
        Rect::from_min_max(to_screen(bounds.min), to_screen(bounds.max)),
        4.0 * s,
        theme::WELL,
        Stroke::new(if focused { 1.5 } else { 1.0 } * s, if focused { theme::FOCUS } else { theme::CONTROL_BORDER }),
        egui::StrokeKind::Middle,
    );
    let dim = if node.bypass || !enabled { 0.5 } else { 1.0 };
    for fraction in [0.25f32, 0.5, 0.75] {
        let x = travel.left() + travel.width() * fraction;
        let y = travel.top() + travel.height() * fraction;
        let color = theme::with_opacity(theme::CONTROL_BORDER, if fraction == 0.5 { 0.9 } else { 0.55 } * dim);
        painter.line_segment([to_screen(Pos2::new(x, travel.top())), to_screen(Pos2::new(x, travel.bottom()))], Stroke::new(s, color));
        painter.line_segment([to_screen(Pos2::new(travel.left(), y)), to_screen(Pos2::new(travel.right(), y))], Stroke::new(s, color));
    }
    painter.rect_stroke(
        Rect::from_min_max(to_screen(travel.min), to_screen(travel.max)),
        0.0,
        Stroke::new(s, theme::with_opacity(theme::CONTROL_BORDER, 0.45 * dim)),
        egui::StrokeKind::Middle,
    );
    let handle_point = handle(shown_tone, shown_ambience);
    let neutral = Pos2::new(travel.center().x, travel.bottom());
    if handle_point.distance(neutral) >= 23.0 {
        let n = to_screen(neutral);
        let color = theme::with_opacity(theme::MUTED, 0.7 * dim);
        let d = 3.0 * s;
        painter.add(egui::Shape::closed_line(
            vec![n + Vec2::new(0.0, -d), n + Vec2::new(d, 0.0), n + Vec2::new(0.0, d), n + Vec2::new(-d, 0.0)],
            Stroke::new(s, color),
        ));
    }
    let h = to_screen(handle_point);
    painter.line_segment([to_screen(Pos2::new(handle_point.x, travel.bottom())), h], Stroke::new(s, theme::with_opacity(theme::A, 0.3 * dim)));
    super::widgets::circle(&painter, h, 9.0 * s, theme::with_opacity(theme::WELL, dim), Stroke::new(2.0 * s, theme::with_opacity(theme::A, dim)));
    painter.circle_filled(h, 3.0 * s, theme::with_opacity(theme::A, dim));
    let label_font = theme::font(15.0 * s, Weight::SemiBold);
    let centre = bounds.center().x;
    painter.text(to_screen(Pos2::new(centre, bounds.top() + 8.0)), Align2::CENTER_TOP, "Broadcast", label_font.clone(), theme::TEXT);
    painter.text(to_screen(Pos2::new(bounds.left() + 12.0, bounds.bottom() - 28.0)), Align2::LEFT_TOP, "Warm", label_font.clone(), theme::TEXT);
    painter.text(to_screen(Pos2::new(centre, bounds.bottom() - 28.0)), Align2::CENTER_TOP, "Natural", label_font.clone(), theme::TEXT);
    painter.text(to_screen(Pos2::new(bounds.right() - 12.0, bounds.bottom() - 28.0)), Align2::RIGHT_TOP, "Bright", label_font, theme::TEXT);
    let response = response.tip(format!(
        "Voice Color\nTone · Warm to Bright\nCharacter · Natural to Broadcast\nNeutral · Original voice character\n{}",
        description(shown_tone, shown_ambience)
    ));
    if response.hovered() && enabled {
        ctx.ui.ctx().set_cursor_icon(egui::CursorIcon::Crosshair);
    }
    if dragging {
        ctx.runtime.editing = true;
    }
    if started {
        ctx.begin_gesture();
    }
    if let Some((t, a)) = next {
        ctx.edit(move |n| {
            n.voice_color.tone = t;
            n.voice_color.ambience = a;
        });
    }
    if ended {
        ctx.end_gesture();
    }
}
