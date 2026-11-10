//! The rotary range control with exact numeric entry (`RotaryValueControl`).
//!
//! Drag vertically (240 world units per full range, Shift for 1/5 speed),
//! arrows and Page keys adjust, Home or double-click resets, and typing a
//! number starts exact entry. Entry applies on Enter, Tab or focus loss and
//! cancels on Escape; an invalid draft is kept and outlined until fixed.

use crate::ui::widgets::Tip as _;
use std::sync::{Arc, OnceLock};

use eframe::egui::{self, Color32, Id, Key, Pos2, Rect, Sense, Stroke, Ui, Vec2};
use vinemix_model::config::GraphNode;

use super::theme::{self, Weight};

pub type Getter = fn(&GraphNode) -> f64;
pub type Setter = Arc<dyn Fn(&mut GraphNode, f64) + Send + Sync>;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Variant {
    /// Label left, dial, entry right (Level, tone dials).
    Row,
    /// Label above, dial, entry below; fixed tile width.
    Tile,
    /// As Tile, but filling its cell (EQ band controls).
    Compact,
}

#[derive(Clone)]
pub struct Spec {
    pub key: String,
    pub label: String,
    pub detail: String,
    pub minimum: f64,
    pub maximum: f64,
    pub step: f64,
    pub unit: &'static str,
    pub default: f64,
    pub logarithmic: bool,
    pub integer: bool,
    pub primary: bool,
    pub format: String,
    pub small_change: f64,
    pub large_change: f64,
    pub description: String,
    pub variant: Variant,
}

impl std::fmt::Debug for Spec {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Spec").field("key", &self.key).finish_non_exhaustive()
    }
}

/// `StepFormat`: decimals implied by the step, at least one for dB.
fn step_format(step: f64, unit: &str) -> String {
    let mut decimals = 0usize;
    while decimals < 6 && (step - round_to(step, decimals)).abs() > step * 1e-9 {
        decimals += 1;
    }
    if matches!(unit, "dB" | "dBFS") {
        decimals = decimals.max(1);
    }
    if decimals == 0 { "0".into() } else { format!("0.{}", "0".repeat(decimals)) }
}

fn round_to(value: f64, decimals: usize) -> f64 {
    let factor = 10f64.powi(decimals as i32);
    (value * factor).round() / factor
}

impl Spec {
    #[allow(clippy::too_many_arguments)]
    pub fn new(key: &str, label: &str, minimum: f64, maximum: f64, step: f64, unit: &'static str, default: f64, variant: Variant) -> Self {
        Self {
            key: key.into(),
            label: label.into(),
            detail: String::new(),
            minimum,
            maximum,
            step,
            unit,
            default,
            logarithmic: false,
            integer: false,
            primary: false,
            format: step_format(step, unit),
            small_change: step,
            large_change: step * 10.0,
            description: default_description(label),
            variant,
        }
    }

    pub fn logarithmic(mut self, value: bool) -> Self {
        self.logarithmic = value;
        self
    }
    pub fn integer(mut self) -> Self {
        self.integer = true;
        self.step = self.step.round().max(1.0);
        self.small_change = self.small_change.round().max(1.0);
        self.large_change = self.large_change.round().max(1.0);
        self.format = "0".into();
        self
    }
    pub fn primary(mut self, value: bool) -> Self {
        self.primary = value;
        self
    }
    pub fn format(mut self, format: &str) -> Self {
        if !self.integer {
            self.format = format.into();
        }
        self
    }
    pub fn changes(mut self, small: f64, large: f64) -> Self {
        self.small_change = small;
        self.large_change = large;
        self
    }
    pub fn detail(mut self, detail: &str) -> Self {
        self.detail = detail.into();
        self
    }
    pub fn description(mut self, description: &str) -> Self {
        if !description.is_empty() {
            self.description = description.into();
        }
        self
    }

    pub fn bound(&self, value: f64) -> f64 {
        let value = if value.is_finite() { value } else if self.default.is_finite() { self.default } else { self.minimum };
        let value = if self.integer { value.round() } else { value };
        value.clamp(self.minimum, self.maximum)
    }

    pub fn normalize(&self, value: f64) -> f64 {
        if self.logarithmic {
            (value / self.minimum).ln() / (self.maximum / self.minimum).ln()
        } else {
            (value - self.minimum) / (self.maximum - self.minimum)
        }
    }

    pub fn denormalize(&self, position: f64) -> f64 {
        if self.logarithmic {
            self.minimum * (self.maximum / self.minimum).powf(position)
        } else {
            self.minimum + position * (self.maximum - self.minimum)
        }
    }

    fn with_unit(&self, value: &str) -> String {
        if self.unit.is_empty() {
            value.to_owned()
        } else if self.unit == ":1" {
            format!("{value}{}", self.unit)
        } else {
            format!("{value} {}", self.unit)
        }
    }

    pub fn format_value(&self, value: f64) -> String {
        self.with_unit(&format_number(value, &self.format))
    }

    /// The concise value when it round-trips, else the shortest exact one.
    pub fn format_exact(&self, value: f64) -> String {
        let concise = format_number(value, &self.format);
        let number = if parse_number(&concise) == Some(value) { concise } else { localize(&format!("{value}")) };
        self.with_unit(&number)
    }

    /// Parses entry text, accepting the unit suffix and a Unicode minus.
    pub fn parse(&self, text: &str) -> Option<f64> {
        let mut text = text.replace('−', "-").trim().to_owned();
        if !self.unit.is_empty() && text.to_lowercase().ends_with(&self.unit.to_lowercase()) {
            text = text[..text.len() - self.unit.len()].trim_end().to_owned();
        }
        let value = parse_number(&text)?;
        (value.is_finite() && value >= self.minimum && value <= self.maximum && (!self.integer || value == value.trunc())).then_some(value)
    }

    pub fn range_text(&self) -> String {
        format!("{} to {}", self.format_value(self.minimum), self.format_value(self.maximum))
    }

    pub fn entry_requirement(&self) -> String {
        format!(
            "Enter {} from {} to {}.",
            if self.integer { "a whole number" } else { "a number" },
            self.format_value(self.minimum),
            self.format_value(self.maximum)
        )
    }

    /// Hover help: the control's name and what it does. How to operate a
    /// dial is the same for every dial, so it lives in Help ("Dials &
    /// values") and in the accessible descriptions below, not here.
    pub fn help(&self) -> String {
        format!("{}\n{}", self.label, self.description)
    }

    /// Accessible description of the dial face: purpose, range and operation.
    pub fn face_description(&self, default: f64) -> String {
        format!(
            "{} {}. Drag vertically; Shift for fine adjustment. Arrow keys adjust; Page Up and Page Down adjust by {}. Type a number to edit its exact value. Home or double-click resets to {}.",
            self.description,
            self.range_text(),
            self.format_value(self.large_change),
            self.format_value(default)
        )
    }

    /// Accessible description of the exact-value entry.
    pub fn entry_description(&self, default: f64) -> String {
        format!(
            "{} {}. Enter applies; Tab applies and moves between controls; clicking away applies a valid value. Escape cancels. Double-click resets to {}.",
            self.description,
            self.range_text(),
            self.format_value(default)
        )
    }
}

fn default_description(label: &str) -> String {
    match label {
        "Level" => "Adjusts the audio level. 0 dB leaves it unchanged.".into(),
        "Low" => "Boosts or cuts bass around the 200 Hz shelf.".into(),
        "Mid" => "Boosts or cuts midrange around 1 kHz.".into(),
        "High" => "Boosts or cuts treble around the 5 kHz shelf.".into(),
        "Frequency" => "Sets the frequency affected by the selected EQ band.".into(),
        "Band gain" => "Boosts or cuts the selected EQ band.".into(),
        "Width / Q" => "Sets the band's width. Higher Q affects a narrower range of frequencies.".into(),
        _ => format!("Adjusts {}.", label.to_lowercase()),
    }
}

/// The user's decimal separator (current culture).
pub fn decimal_separator() -> char {
    static SEPARATOR: OnceLock<char> = OnceLock::new();
    *SEPARATOR.get_or_init(|| {
        use windows::Win32::Globalization::{GetLocaleInfoEx, LOCALE_SDECIMAL};
        let mut buffer = [0u16; 8];
        // SAFETY: valid buffer; null locale name means the user default.
        let length = unsafe { GetLocaleInfoEx(windows::core::PCWSTR::null(), LOCALE_SDECIMAL, Some(&mut buffer)) };
        if length > 1 {
            String::from_utf16_lossy(&buffer[..(length as usize - 1)]).chars().next().unwrap_or('.')
        } else {
            '.'
        }
    })
}

fn localize(text: &str) -> String {
    let separator = decimal_separator();
    if separator == '.' { text.to_owned() } else { text.replace('.', &separator.to_string()) }
}

pub fn parse_number(text: &str) -> Option<f64> {
    let separator = decimal_separator();
    let normalized: String = text.trim().chars().map(|c| if c == separator { '.' } else { c }).collect();
    if normalized.is_empty() || normalized.contains(char::is_whitespace) {
        return None;
    }
    normalized.parse::<f64>().ok()
}

/// .NET custom numeric format subset: `0`, `0.0`, `0.###`, `0.0##` …
pub fn format_number(value: f64, format: &str) -> String {
    let (required, optional) = match format.split_once('.') {
        Some((_, fraction)) => (fraction.chars().filter(|&c| c == '0').count(), fraction.chars().filter(|&c| c == '#').count()),
        None => (0, 0),
    };
    let decimals = required + optional;
    let rounded = {
        // Away-from-zero rounding, like .NET formatting.
        let factor = 10f64.powi(decimals as i32);
        let scaled = (value.abs() * factor).round() / factor;
        scaled.copysign(value)
    };
    let mut text = format!("{rounded:.decimals$}");
    if optional > 0 {
        let keep = text.find('.').map_or(text.len(), |dot| dot + 1 + required);
        while text.len() > keep && text.ends_with('0') {
            text.pop();
        }
        if text.ends_with('.') {
            text.pop();
        }
    }
    if text.starts_with('-') && text[1..].chars().all(|c| c == '0' || c == '.') {
        text.remove(0);
    }
    localize(&text)
}

/// Per-dial interaction state, kept between frames.
#[derive(Clone, Debug, Default)]
pub struct DialState {
    pub dragging: bool,
    drag_position: f64,
    pub draft: Option<String>,
    pub invalid: bool,
    entry_had_focus: bool,
    /// The entry took focus from a pointer press that is still held: whether
    /// that was a plain click (select everything) or the start of a drag
    /// selection (keep it) is decided when the button is released.
    select_all_on_release: bool,
    /// Exact entry was started by typing on the dial face: the entry opens
    /// with that text and the caret after it, so the next key continues it.
    continue_typing: bool,
}

/// What happened this frame.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Outcome {
    pub changed: Option<f64>,
    pub gesture_started: bool,
    pub gesture_ended: bool,
    pub editing: bool,
}

/// Paints the dial face (86-unit design scaled into `rect`).
pub fn paint_face(painter: &egui::Painter, rect: Rect, position: f64, default_position: f64, focused: bool, enabled: bool) {
    let s = rect.width() / 86.0;
    let center = rect.center();
    let at = |degrees: f64, radius: f32| {
        let angle = degrees.to_radians();
        Pos2::new(center.x + angle.sin() as f32 * radius * s, center.y - angle.cos() as f32 * radius * s)
    };
    let fade = |c: Color32| if enabled { c } else { c.gamma_multiply(0.45) };
    let arc = |first: f64, last: f64| -> Vec<Pos2> { (0..=48).map(|i| at(first + (last - first) * f64::from(i) / 48.0, 34.0)).collect() };
    painter.add(egui::Shape::line(arc(-135.0, 135.0), Stroke::new(3.0 * s, fade(theme::METER_UNLIT))));
    let angle = -135.0 + position * 270.0;
    let default_angle = -135.0 + default_position * 270.0;
    if (angle - default_angle).abs() > 0.001 {
        painter.add(egui::Shape::line(arc(default_angle, angle), Stroke::new(3.0 * s, fade(theme::NODE_AUDIO))));
    }
    for i in -2..=2 {
        painter.line_segment([at(f64::from(i) * 67.5, 39.0), at(f64::from(i) * 67.5, 42.0)], Stroke::new(s, fade(theme::MUTED)));
    }
    painter.line_segment([at(default_angle, 39.0), at(default_angle, 42.0)], Stroke::new(s, fade(theme::TEXT)));
    super::widgets::circle(
        painter,
        center,
        27.0 * s,
        fade(theme::BUTTON_FACE),
        Stroke::new(if focused { 2.0 } else { 1.0 } * s, fade(if focused { theme::FOCUS } else { theme::NODE_OUTLINE })),
    );
    let pointer = [at(angle, 13.0), at(angle, 23.0)];
    painter.line_segment(pointer, Stroke::new(2.5 * s, fade(theme::TEXT)));
    painter.circle_filled(pointer[0], 1.25 * s, fade(theme::TEXT));
    painter.circle_filled(pointer[1], 1.25 * s, fade(theme::TEXT));
}

/// Interacts with and paints one dial. `face` and `entry` are screen rects;
/// `scale` is the canvas zoom (drag distances are in world units).
#[allow(clippy::too_many_arguments)]
pub fn show(
    ui: &mut Ui,
    id: Id,
    spec: &Spec,
    value: f64,
    default: f64,
    state: &mut DialState,
    face: Rect,
    entry: Rect,
    scale: f32,
    enabled: bool,
) -> Outcome {
    let mut outcome = Outcome::default();
    let value = spec.bound(value);
    let default = spec.bound(default);
    let face_id = id.with("face");
    let response = ui.interact(face, face_id, if enabled { Sense::click_and_drag() } else { Sense::hover() });
    response.widget_info(|| egui::WidgetInfo::slider(enabled, value, &spec.label));
    let focused = response.has_focus();
    if enabled {
        if response.double_clicked() {
            state.dragging = false;
            if value != default {
                outcome.changed = Some(default);
            }
            state.draft = None;
            state.invalid = false;
        } else if response.drag_started() {
            response.request_focus();
            state.draft = None;
            state.invalid = false;
            state.dragging = true;
            state.drag_position = spec.normalize(value);
            outcome.gesture_started = true;
        }
        if state.dragging && response.dragged() {
            let movement = -f64::from(response.drag_delta().y) / f64::from(scale.max(0.01));
            if movement != 0.0 {
                let fine = ui.input(|i| i.modifiers.shift);
                state.drag_position = (state.drag_position + movement / 240.0 * if fine { 0.2 } else { 1.0 }).clamp(0.0, 1.0);
                let next = spec.bound((spec.denormalize(state.drag_position) / spec.step).round() * spec.step);
                if next != value {
                    outcome.changed = Some(next);
                }
            }
        }
        if state.dragging && (response.drag_stopped() || !response.dragged() && !ui.input(|i| i.pointer.primary_down())) {
            state.dragging = false;
            outcome.gesture_ended = true;
        }
        if focused {
            // Arrows adjust the value instead of moving focus.
            ui.memory_mut(|m| m.set_focus_lock_filter(face_id, egui::EventFilter { tab: false, horizontal_arrows: true, vertical_arrows: true, escape: false }));
        }
        if focused && state.draft.is_none() {
            let (key, typed) = ui.input(|i| {
                let key = [Key::ArrowLeft, Key::ArrowDown, Key::ArrowRight, Key::ArrowUp, Key::PageDown, Key::PageUp, Key::Home, Key::End]
                    .into_iter()
                    .find(|k| i.key_pressed(*k) && i.modifiers.is_none());
                let typed = i.events.iter().find_map(|e| match e {
                    egui::Event::Text(text) if !i.modifiers.ctrl && !i.modifiers.alt => Some(text.clone()),
                    _ => None,
                });
                (key, typed)
            });
            if let Some(key) = key {
                let next = match key {
                    Key::ArrowLeft | Key::ArrowDown => value - spec.small_change,
                    Key::ArrowRight | Key::ArrowUp => value + spec.small_change,
                    Key::PageDown => value - spec.large_change,
                    Key::PageUp => value + spec.large_change,
                    Key::Home => default,
                    _ => spec.maximum,
                };
                let next = spec.bound(next);
                if next != value {
                    outcome.changed = Some(next);
                }
            } else if let Some(text) = typed {
                let separator = decimal_separator().to_string();
                let direct = text.chars().all(|c| c.is_ascii_digit()) || text == "-" || text == "−" || text == separator;
                if direct && !text.is_empty() {
                    state.draft = Some(text);
                    state.invalid = false;
                    state.continue_typing = true;
                    ui.memory_mut(|m| m.request_focus(id.with("entry")));
                    // The entry takes focus within this frame; it must not
                    // insert the same keystroke a second time.
                    ui.input_mut(|i| i.events.retain(|event| !matches!(event, egui::Event::Text(_))));
                }
            }
        }
    }
    let shown = outcome.changed.unwrap_or(value);
    paint_face(ui.painter(), face, spec.normalize(shown), spec.normalize(default), focused, enabled);
    if response.hovered() || state.dragging {
        ui.ctx().set_cursor_icon(if state.dragging { egui::CursorIcon::ResizeVertical } else { egui::CursorIcon::Default });
    }
    let response = response.tip_described(spec.help(), spec.face_description(default));
    let _ = response;

    // Exact entry.
    let entry_id = id.with("entry");
    let entry_focused = ui.memory(|m| m.has_focus(entry_id));
    if entry_focused && !state.entry_had_focus && state.draft.is_none() {
        state.draft = Some(spec.format_exact(shown));
        state.invalid = false;
    }
    let mut text = state.draft.clone().unwrap_or_else(|| if entry_focused { spec.format_exact(shown) } else { spec.format_value(shown) });
    let border = if state.invalid {
        theme::MUTE
    } else if entry_focused {
        theme::FOCUS
    } else if ui.rect_contains_pointer(entry) {
        theme::CONTROL_BORDER
    } else {
        Color32::TRANSPARENT
    };
    let painter = ui.painter();
    painter.rect(entry, 3.0 * scale, theme::WELL, Stroke::new(scale.max(0.5), border), egui::StrokeKind::Inside);
    let font = theme::font(12.0 * scale, Weight::Mono);
    let mut edit = egui::TextEdit::singleline(&mut text)
        .id(entry_id)
        .font(font)
        .frame(egui::Frame::NONE)
        .align(egui::Align2::CENTER_CENTER)
        .char_limit(24)
        .desired_width(entry.width())
        .min_size(entry.size())
        .margin(egui::Margin::same((2.0 * scale) as i8))
        .text_color(if enabled { theme::TEXT } else { theme::with_opacity(theme::TEXT, 0.45) });
    if !enabled {
        edit = edit.interactive(false);
    }
    let output = edit.show(&mut super::widgets::field_ui(ui, entry));
    let response: egui::Response = output.response.response;
    // A bare text field is anonymous to assistive technology.
    ui.ctx().accesskit_node_builder(entry_id, |node| node.set_label(format!("{} value", spec.label)));
    let selection = output.cursor_range;
    let pointer_down = ui.input(|i| i.pointer.primary_down());
    // Selecting everything makes typing replace the value. It must not fight
    // a drag selection, though: the press that gives the entry focus also
    // sets the selection's anchor, and forcing a range over it mid-gesture
    // collapsed right-to-left drags. So focus from the keyboard (or a click
    // completed within one frame) selects everything at once, while focus
    // from a held press waits for the release and then selects everything
    // only if no selection was dragged (a plain click).
    let mut select_all = false;
    if response.gained_focus() && std::mem::take(&mut state.continue_typing) {
        // Typed on the dial face: keep what was typed and continue after it.
        let mut text_state = output.state.clone();
        let end = egui::text::CCursor::new(text.chars().count());
        text_state.cursor.set_char_range(Some(egui::text::CCursorRange::one(end)));
        text_state.store(ui.ctx(), entry_id);
    } else if response.gained_focus() {
        if pointer_down {
            state.select_all_on_release = true;
        } else {
            select_all = true;
        }
    } else if state.select_all_on_release && !pointer_down {
        state.select_all_on_release = false;
        select_all = response.has_focus() && selection.is_none_or(|range| range.primary.index == range.secondary.index);
    }
    if !response.has_focus() {
        state.select_all_on_release = false;
    }
    if select_all {
        let mut text_state = output.state;
        let length = text.chars().count();
        text_state.cursor.set_char_range(Some(egui::text::CCursorRange::two(egui::text::CCursor::new(0), egui::text::CCursor::new(length))));
        text_state.store(ui.ctx(), entry_id);
    }
    if response.has_focus() {
        state.draft = Some(text.clone());
        outcome.editing = true;
    }
    let (enter, escape, tab) = ui.input(|i| (i.key_pressed(Key::Enter), i.key_pressed(Key::Escape), i.key_pressed(Key::Tab)));
    if response.double_clicked() && enabled {
        if value != default {
            outcome.changed = Some(default);
        }
        state.draft = Some(spec.format_exact(default));
        state.invalid = false;
    }
    if state.entry_had_focus && escape {
        state.draft = None;
        state.invalid = false;
        response.surrender_focus();
    } else if response.lost_focus() || (response.has_focus() && enter) {
        if let Some(draft) = state.draft.take() {
            if draft != spec.format_exact(shown) && draft != spec.format_value(shown) {
                match spec.parse(&draft) {
                    Some(parsed) => {
                        state.invalid = false;
                        let parsed = spec.bound(parsed);
                        if parsed != value {
                            outcome.changed = Some(parsed);
                        }
                    }
                    None => {
                        state.invalid = true;
                        state.draft = Some(draft);
                        if enter || tab {
                            response.request_focus();
                        }
                    }
                }
            } else {
                state.invalid = false;
            }
        }
        if enter && !state.invalid {
            response.surrender_focus();
        }
    }
    let response = if state.invalid {
        response.tip(format!("Value not applied\n{}", spec.entry_requirement()))
    } else {
        response.tip_described(spec.help(), spec.entry_description(default))
    };
    state.entry_had_focus = response.has_focus();
    outcome.editing |= state.draft.is_some() || state.invalid;
    outcome
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn formats_like_dotnet() {
        assert_eq!(format_number(12.0, "0.###"), "12");
        assert_eq!(format_number(12.3456, "0.###"), "12.346");
        assert_eq!(format_number(4.0, "0.0##"), "4.0");
        assert_eq!(format_number(-0.04, "0.0"), "0.0");
        assert_eq!(format_number(2.5, "0"), "3");
        assert_eq!(step_format(0.1, "dB"), "0.0");
        assert_eq!(step_format(1.0, "dB"), "0.0");
        assert_eq!(step_format(1.0, "Hz"), "0");
        assert_eq!(step_format(0.01, ""), "0.00");
        let spec = Spec::new("g", "Level", -60.0, 12.0, 0.1, "dB", 0.0, Variant::Row);
        assert_eq!(spec.format_value(-6.0), "-6.0 dB");
        assert_eq!(spec.parse("−6 dB"), Some(-6.0));
        assert_eq!(spec.parse("13"), None);
        let ratio = Spec::new("r", "Ratio", 1.0, 20.0, 0.1, ":1", 4.0, Variant::Tile).format("0.0##");
        assert_eq!(ratio.format_value(4.0), "4.0:1");
        assert_eq!(ratio.format_exact(4.123_45), "4.12345:1");
    }
}

/// Height of a grouped (`compact` or `tile`) dial at scale 1: an 18 px title,
/// the face slot (86 primary, 58 secondary) with 2/1 margins and the entry.
pub fn tile_height(spec: &Spec) -> f32 {
    18.0 + 2.0 + if spec.primary { 86.0 } else { 58.0 } + 1.0 + 28.0
}

/// A grouped dial outside the canvas, `width` wide with its top at `top_left`.
#[allow(clippy::too_many_arguments)]
pub fn tile(ui: &mut Ui, id: Id, spec: &Spec, value: f64, default: f64, state: &mut DialState, top_left: egui::Pos2, width: f32, enabled: bool) -> Outcome {
    let face_size = if spec.primary { 86.0 } else { 58.0 };
    let title = Rect::from_min_size(top_left, Vec2::new(width, 18.0));
    let color = if enabled { theme::TEXT } else { theme::with_opacity(theme::TEXT, 0.45) };
    ui.painter().text(title.center(), egui::Align2::CENTER_CENTER, &spec.label, theme::font(12.0, Weight::SemiBold), color);
    let slot_top = title.bottom() + 2.0;
    let face = Rect::from_center_size(egui::Pos2::new(title.center().x, slot_top + face_size / 2.0), Vec2::splat(face_size));
    let entry_width = 110.0f32.min(width);
    let entry = Rect::from_min_size(egui::Pos2::new(title.center().x - entry_width / 2.0, slot_top + face_size + 1.0), Vec2::new(entry_width, 28.0));
    show(ui, id, spec, value, default, state, face, entry, 1.0, enabled)
}

#[cfg(test)]
#[path = "dial_tests.rs"]
mod interaction_tests;
