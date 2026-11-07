//! The graphical equalizer (`EqualizerEditor`) and its band controls.
//!
//! Up to eight bands on a log-frequency, ±24 dB plot with the combined
//! biquad response and the live output spectrum. Points drag (Shift for
//! fine), Ctrl-click toggles selection, empty-space drags box-select,
//! double-click adds or resets, and the context menu changes shape.

use crate::ui::widgets::Tip as _;
use std::collections::BTreeSet;

use eframe::egui::{self, Align2, Key, Pos2, Rect, Sense, Stroke, Vec2};
use vinemix_model::config::EqBand;

use super::dial::{Spec, Variant};
use super::node_view::ItemCtx;
use super::theme::{self, Weight};
use super::widgets;
use crate::graph;

pub const MAXIMUM_BANDS: usize = 8;
const MIN_F: f64 = 20.0;
const MAX_F: f64 = 20000.0;
const MIN_G: f64 = -24.0;
const MAX_G: f64 = 24.0;
const POINT_RADIUS: f32 = 10.0;
const HIT_RADIUS: f32 = 15.0;

#[derive(Clone, Debug)]
struct BoxSelection {
    origin: Pos2,
    pointer: Pos2,
    additive: bool,
    moved: bool,
    before: BTreeSet<usize>,
    primary_before: Option<usize>,
}

#[derive(Clone, Debug)]
pub struct EqState {
    selection: BTreeSet<usize>,
    selected: Option<usize>,
    hovered: Option<usize>,
    dragged: Option<usize>,
    last_pointer: Pos2,
    selecting: Option<BoxSelection>,
    feedback: Option<String>,
    band_count: usize,
    spectrum: [f32; 128],
    spectrum_frame: u64,
    spectrum_revision: u64,
    spectrum_received: f64,
    spectrum_visible: bool,
    menu_point: Pos2,
    menu_band: Option<usize>,
}

impl Default for EqState {
    fn default() -> Self {
        Self {
            selection: BTreeSet::new(),
            selected: Some(0),
            hovered: None,
            dragged: None,
            last_pointer: Pos2::ZERO,
            selecting: None,
            feedback: None,
            band_count: 0,
            spectrum: [-90.0; 128],
            spectrum_frame: 0,
            spectrum_revision: 0,
            spectrum_received: f64::NEG_INFINITY,
            spectrum_visible: false,
            menu_point: Pos2::ZERO,
            menu_band: None,
        }
    }
}

fn supports_gain(band: &EqBand) -> bool {
    !matches!(band.band_type.as_str(), "lowPass" | "highPass" | "notch")
}

fn plot_bounds(size: Vec2) -> Rect {
    Rect::from_min_size(Pos2::new(44.0, 35.0), Vec2::new((size.x - 86.0).max(1.0), (size.y - 72.0).max(1.0)))
}

fn band_to_point(frequency: f64, gain: f64, plot: Rect) -> Pos2 {
    let frequency = if frequency.is_finite() { frequency } else { 1000.0 }.clamp(MIN_F, MAX_F);
    let gain = if gain.is_finite() { gain } else { 0.0 }.clamp(MIN_G, MAX_G);
    Pos2::new(
        plot.left() + ((frequency / MIN_F).ln() / (MAX_F / MIN_F).ln()) as f32 * plot.width(),
        plot.top() + ((MAX_G - gain) / (MAX_G - MIN_G)) as f32 * plot.height(),
    )
}

fn point_to_band(point: Pos2, plot: Rect) -> (f64, f64) {
    let x = f64::from((point.x - plot.left()) / plot.width().max(1.0)).clamp(0.0, 1.0);
    let y = f64::from((point.y - plot.top()) / plot.height().max(1.0)).clamp(0.0, 1.0);
    (MIN_F * (MAX_F / MIN_F).powf(x), MAX_G - y * (MAX_G - MIN_G))
}

/// One band's magnitude response at `frequency` (RBJ cookbook, 48 kHz).
pub fn response_db(band: &EqBand, frequency: f64) -> f64 {
    if !band.enabled || !frequency.is_finite() || !band.frequency_hz.is_finite() || !band.gain_db.is_finite() || !band.q.is_finite() {
        return 0.0;
    }
    let w0 = 2.0 * std::f64::consts::PI * band.frequency_hz.clamp(MIN_F, MAX_F) / 48000.0;
    let (c, s) = (w0.cos(), w0.sin());
    let q = band.q.clamp(0.1, 20.0);
    let alpha = s / (2.0 * q);
    let a = 10f64.powf(band.gain_db.clamp(MIN_G, MAX_G) / 40.0);
    let beta = 2.0 * a.sqrt() * alpha;
    let (b0, b1, b2, a0, a1, a2) = match band.band_type.as_str() {
        "lowShelf" => (
            a * ((a + 1.0) - (a - 1.0) * c + beta),
            2.0 * a * ((a - 1.0) - (a + 1.0) * c),
            a * ((a + 1.0) - (a - 1.0) * c - beta),
            (a + 1.0) + (a - 1.0) * c + beta,
            -2.0 * ((a - 1.0) + (a + 1.0) * c),
            (a + 1.0) + (a - 1.0) * c - beta,
        ),
        "highShelf" => (
            a * ((a + 1.0) + (a - 1.0) * c + beta),
            -2.0 * a * ((a - 1.0) + (a + 1.0) * c),
            a * ((a + 1.0) + (a - 1.0) * c - beta),
            (a + 1.0) - (a - 1.0) * c + beta,
            2.0 * ((a - 1.0) - (a + 1.0) * c),
            (a + 1.0) - (a - 1.0) * c - beta,
        ),
        "highPass" => ((1.0 + c) / 2.0, -(1.0 + c), (1.0 + c) / 2.0, 1.0 + alpha, -2.0 * c, 1.0 - alpha),
        "lowPass" => ((1.0 - c) / 2.0, 1.0 - c, (1.0 - c) / 2.0, 1.0 + alpha, -2.0 * c, 1.0 - alpha),
        "notch" => (1.0, -2.0 * c, 1.0, 1.0 + alpha, -2.0 * c, 1.0 - alpha),
        _ => (1.0 + alpha * a, -2.0 * c, 1.0 - alpha * a, 1.0 + alpha / a, -2.0 * c, 1.0 - alpha / a),
    };
    let w = 2.0 * std::f64::consts::PI * frequency.clamp(MIN_F, MAX_F) / 48000.0;
    let nr = b0 + b1 * w.cos() + b2 * (2.0 * w).cos();
    let ni = -b1 * w.sin() - b2 * (2.0 * w).sin();
    let dr = a0 + a1 * w.cos() + a2 * (2.0 * w).cos();
    let di = -a1 * w.sin() - a2 * (2.0 * w).sin();
    let result = 10.0 * ((nr * nr + ni * ni) / (dr * dr + di * di).max(1e-20)).max(1e-12).log10();
    if result.is_finite() { result } else { 0.0 }
}

pub fn combined_response_db(bands: &[EqBand], bypassed: bool, frequency: f64) -> f64 {
    if bypassed { 0.0 } else { bands.iter().map(|b| response_db(b, frequency)).sum() }
}

fn band_effect(band: &EqBand) -> &'static str {
    match band.band_type.as_str() {
        "lowShelf" => "Boosts or cuts frequencies below its centre",
        "highShelf" => "Boosts or cuts frequencies above its centre",
        "notch" => "Rejects a narrow band around its centre",
        "highPass" => "Reduces frequencies below its cutoff",
        "lowPass" => "Reduces frequencies above its cutoff",
        _ => "Boosts or cuts frequencies around its centre",
    }
}

fn band_description(bands: &[EqBand], index: usize) -> String {
    let band = &bands[index];
    let frequency = if band.frequency_hz >= 1000.0 {
        format!("{} kHz", super::dial::format_number(band.frequency_hz / 1000.0, "0.##"))
    } else {
        format!("{} Hz", super::dial::format_number(band.frequency_hz, "0.#"))
    };
    let gain = if supports_gain(band) {
        let text = super::dial::format_number(band.gain_db.abs(), "0.0");
        format!("{}{text} dB", if band.gain_db > 0.0 && text != "0.0" { "+" } else if band.gain_db < 0.0 && text != "0.0" { "-" } else { "" })
    } else if band.band_type == "notch" {
        "notch".into()
    } else {
        "cutoff".into()
    };
    format!(
        "Band {} · {frequency} · {gain} · Q {}{}",
        index + 1,
        super::dial::format_number(band.q, "0.0###"),
        if band.enabled { "" } else { " · off" }
    )
}

/// Normalises bands the way `CopyBand` does before publishing.
fn publish_copy(bands: &[EqBand]) -> Vec<EqBand> {
    bands
        .iter()
        .map(|b| {
            let mut copy = b.clone();
            if copy.reset_frequency_hz <= 0.0 {
                copy.reset_frequency_hz = copy.frequency_hz;
            }
            copy
        })
        .collect()
}

impl EqState {
    /// Selects only `index` (or nothing), as a practice reset does.
    pub fn select_only(&mut self, index: Option<usize>, count: usize) {
        self.band_count = count;
        self.selection.clear();
        self.selected = index.filter(|&i| i < count);
        if let Some(i) = self.selected {
            self.selection.insert(i);
        }
        self.dragged = None;
        self.selecting = None;
    }

    /// The active band, if any.
    pub fn active(&self) -> Option<usize> {
        self.selected
    }

    fn reconcile(&mut self, count: usize) {
        if count != self.band_count {
            self.band_count = count;
            self.selection.retain(|&i| i < count);
            self.selected = self.selected.filter(|&i| i < count).or_else(|| (count > 0).then(|| count - 1).filter(|_| self.selected.is_some()));
            if let Some(selected) = self.selected {
                self.selected = Some(selected.min(count.saturating_sub(1)));
            }
            if count == 0 {
                self.selected = None;
            }
        }
        if let Some(selected) = self.selected {
            if selected >= count {
                self.selected = None;
            } else {
                self.selection.insert(selected);
            }
        }
        self.hovered = self.hovered.filter(|&i| i < count);
    }

    fn set_selection(&mut self, indices: BTreeSet<usize>, primary: Option<usize>, count: usize) {
        let indices: BTreeSet<usize> = indices.into_iter().filter(|&i| i < count).collect();
        let primary = match primary {
            Some(p) if indices.contains(&p) => Some(p),
            _ => indices.iter().next().copied(),
        };
        self.feedback = None;
        self.selection = indices;
        self.selected = primary;
    }

    fn select(&mut self, index: Option<usize>, toggle: bool, count: usize) {
        match (index, toggle) {
            (None, _) => self.set_selection(BTreeSet::new(), None, count),
            (Some(i), false) => self.set_selection([i].into(), Some(i), count),
            (Some(i), true) => {
                let mut selection = self.selection.clone();
                if !selection.insert(i) {
                    selection.remove(&i);
                }
                let primary = if selection.contains(&i) { Some(i) } else { self.selected };
                self.set_selection(selection, primary, count);
            }
        }
    }

    fn apply_spectrum(&mut self, bins: Option<&[f32]>, frame: u64, revision: u64, now: f64, fresh: bool) {
        if !fresh || frame == 0 {
            self.spectrum_frame = 0;
            self.spectrum_revision = 0;
        }
        let Some(bins) = bins.filter(|b| fresh && frame != 0 && b.len() == 128 && now.is_finite() && b.iter().all(|v| v.is_finite())) else {
            self.spectrum_visible = false;
            return;
        };
        if revision == self.spectrum_revision && frame <= self.spectrum_frame {
            if now - self.spectrum_received > 0.25 {
                self.spectrum_visible = false;
            }
            return;
        }
        for (target, value) in self.spectrum.iter_mut().zip(bins) {
            *target = value.clamp(-90.0, 0.0);
        }
        self.spectrum_frame = frame;
        self.spectrum_revision = revision;
        self.spectrum_received = now;
        self.spectrum_visible = true;
    }
}

/// Moves the selection by a frequency ratio and gain delta, clamped so no
/// selected band leaves the plot. Returns the new bands when changed.
fn move_selection(bands: &[EqBand], selection: &BTreeSet<usize>, ratio: f64, delta: f64) -> Option<Vec<EqBand>> {
    if selection.is_empty() || !ratio.is_finite() || ratio <= 0.0 || !delta.is_finite() {
        return None;
    }
    let (mut min_ratio, mut max_ratio) = (0.0f64, f64::INFINITY);
    let (mut min_delta, mut max_delta) = (f64::NEG_INFINITY, f64::INFINITY);
    for &index in selection {
        let band = &bands[index];
        min_ratio = min_ratio.max(MIN_F / band.frequency_hz);
        max_ratio = max_ratio.min(MAX_F / band.frequency_hz);
        if supports_gain(band) {
            min_delta = min_delta.max(MIN_G - band.gain_db);
            max_delta = max_delta.min(MAX_G - band.gain_db);
        }
    }
    let ratio = ratio.clamp(min_ratio, max_ratio);
    let delta = delta.clamp(min_delta, max_delta);
    let mut next = bands.to_vec();
    let mut changed = false;
    for &index in selection {
        let band = &mut next[index];
        let frequency = (band.frequency_hz * ratio).clamp(MIN_F, MAX_F);
        let gain = if supports_gain(band) { (band.gain_db + delta).clamp(MIN_G, MAX_G) } else { band.gain_db };
        changed |= frequency != band.frequency_hz || gain != band.gain_db;
        band.frequency_hz = frequency;
        band.gain_db = gain;
    }
    changed.then_some(next)
}

#[allow(clippy::too_many_lines)]
pub fn show(ctx: &mut ItemCtx<'_, '_>, plot_world: Rect, details_world: Rect) {
    let s = ctx.s;
    let node = ctx.node;
    let bands = node.eq_bands.clone();
    let count = bands.len();
    let enabled = ctx.env.can_edit;
    let screen = ctx.screen(plot_world);
    let local_size = plot_world.size();
    let plot = plot_bounds(local_size);
    let to_screen = |p: Pos2| screen.min + p.to_vec2() * s;
    let to_local = |p: Pos2| ((p - screen.min) / s).to_pos2();
    let id = ctx.id.with("eq");
    let now = ctx.env.now;
    let running = ctx.status.is_some_and(|st| st.path.state == "Running");
    let full = ctx.runtime.detail() == graph::canvas::DetailLevel::Full;
    let state = &mut ctx.runtime.eq;
    state.reconcile(count);
    let status = ctx.status;
    state.apply_spectrum(
        status.map(|st| st.spectrum_db.as_slice()),
        status.map_or(0, |st| st.spectrum_frame),
        status.map_or(0, |st| st.spectrum_revision),
        now,
        ctx.env.can_edit && running,
    );
    let response = ctx.ui.interact(screen, id, if enabled { Sense::click_and_drag() } else { Sense::hover() });
    let band_position = |bands: &[EqBand], i: usize| band_to_point(bands[i].frequency_hz, if supports_gain(&bands[i]) { bands[i].gain_db } else { 0.0 }, plot);
    let band_at = |bands: &[EqBand], selected: Option<usize>, point: Pos2| -> Option<usize> {
        if let Some(sel) = selected.filter(|&i| i < bands.len()) {
            if band_position(bands, sel).distance(point) <= HIT_RADIUS {
                return Some(sel);
            }
        }
        let mut result = None;
        let mut nearest = HIT_RADIUS;
        for i in 0..bands.len() {
            let distance = band_position(bands, i).distance(point);
            if distance < nearest {
                result = Some(i);
                nearest = distance;
            }
        }
        result
    };
    let mut published: Option<Vec<EqBand>> = None;
    let mut gesture_started = false;
    let mut gesture_ended = false;
    let modifiers = ctx.ui.input(|i| i.modifiers);
    if let Some(pointer) = response.hover_pos() {
        if state.dragged.is_none() && state.selecting.is_none() {
            state.hovered = band_at(&bands, state.selected, to_local(pointer));
        }
    } else if state.dragged.is_none() {
        state.hovered = None;
    }
    if enabled {
        let pressed = response.is_pointer_button_down_on() && ctx.ui.input(|i| i.pointer.primary_pressed());
        if response.double_clicked() {
            if let Some(pointer) = response.interact_pointer_pos() {
                let point = to_local(pointer);
                match band_at(&bands, state.selected, point) {
                    Some(index) => {
                        state.select(Some(index), false, count);
                        let mut next = bands.clone();
                        let band = &mut next[index];
                        let frequency = if band.reset_frequency_hz > 0.0 { band.reset_frequency_hz } else { band.frequency_hz }.clamp(MIN_F, MAX_F);
                        if band.frequency_hz != frequency || band.gain_db != 0.0 {
                            band.frequency_hz = frequency;
                            band.gain_db = 0.0;
                            published = Some(next);
                        }
                    }
                    None if plot.contains(point) => {
                        if count >= MAXIMUM_BANDS {
                            state.feedback = Some("All 8 bands are in use. Remove a band to add another.".into());
                        } else {
                            let (frequency, gain) = point_to_band(point, plot);
                            let anchor = frequency.clamp(MIN_F, MAX_F);
                            let mut next = bands.clone();
                            next.push(EqBand::new("peak", true, anchor, gain.clamp(MIN_G, MAX_G), 1.0, anchor));
                            state.set_selection([next.len() - 1].into(), Some(next.len() - 1), next.len());
                            state.band_count = next.len();
                            published = Some(next);
                        }
                    }
                    None => {}
                }
            }
            state.dragged = None;
            state.selecting = None;
        } else if pressed {
            response.request_focus();
            if let Some(pointer) = ctx.ui.input(|i| i.pointer.press_origin()) {
                let point = to_local(pointer);
                match band_at(&bands, state.selected, point) {
                    Some(index) if modifiers.ctrl => state.select(Some(index), true, count),
                    Some(index) => {
                        if state.selection.contains(&index) {
                            let selection = state.selection.clone();
                            state.set_selection(selection, Some(index), count);
                        } else {
                            state.select(Some(index), false, count);
                        }
                        if state.dragged.is_none() {
                            gesture_started = true;
                        }
                        state.dragged = Some(index);
                        state.last_pointer = point;
                    }
                    None if plot.contains(point) => {
                        state.selecting = Some(BoxSelection {
                            origin: point,
                            pointer: point,
                            additive: modifiers.ctrl,
                            moved: false,
                            before: state.selection.clone(),
                            primary_before: state.selected,
                        });
                        if !modifiers.ctrl {
                            state.select(None, false, count);
                        }
                    }
                    None if !modifiers.ctrl => state.select(None, false, count),
                    None => {}
                }
            }
        }
        if let Some(pointer) = ctx.ui.input(|i| i.pointer.latest_pos()) {
            let point = to_local(pointer);
            if let Some(index) = state.dragged {
                if response.dragged() {
                    let delta = point - state.last_pointer;
                    state.last_pointer = point;
                    if delta != Vec2::ZERO && index < count {
                        let scale = if modifiers.shift { 0.2 } else { 1.0 };
                        let ratio = (MAX_F / MIN_F).powf(f64::from(delta.x * scale / plot.width()).clamp(-1.0, 1.0));
                        let gain = -f64::from(delta.y * scale / plot.height()) * (MAX_G - MIN_G);
                        if let Some(next) = move_selection(&bands, &state.selection, ratio, gain) {
                            published = Some(next);
                        }
                    }
                }
            }
            if let Some(selecting) = state.selecting.as_mut() {
                selecting.pointer = Pos2::new(point.x.clamp(plot.left(), plot.right()), point.y.clamp(plot.top(), plot.bottom()));
                if selecting.moved || selecting.pointer.distance(selecting.origin) >= 3.0 {
                    selecting.moved = true;
                    let area = Rect::from_two_pos(selecting.origin, selecting.pointer);
                    let mut selection = if selecting.additive { selecting.before.clone() } else { BTreeSet::new() };
                    for i in 0..count {
                        if area.contains(band_position(&bands, i)) {
                            selection.insert(i);
                        }
                    }
                    let primary = selecting.primary_before;
                    state.set_selection(selection, primary, count);
                }
            }
        }
        if !ctx.ui.input(|i| i.pointer.primary_down()) {
            if state.dragged.take().is_some() {
                gesture_ended = true;
            }
            state.selecting = None;
        }
        if response.secondary_clicked() {
            if let Some(pointer) = response.interact_pointer_pos() {
                let point = to_local(pointer);
                let index = band_at(&bands, state.selected, point);
                if let Some(i) = index {
                    if state.selection.contains(&i) {
                        let selection = state.selection.clone();
                        state.set_selection(selection, Some(i), count);
                    } else {
                        state.select(Some(i), false, count);
                    }
                }
                state.menu_point = point;
                state.menu_band = index;
            }
        }
        if response.has_focus() {
            let keys = ctx.ui.input(|i| {
                [Key::Escape, Key::A, Key::Delete, Key::Backspace, Key::Insert, Key::ArrowLeft, Key::ArrowRight, Key::ArrowUp, Key::ArrowDown, Key::Home, Key::PageUp, Key::PageDown]
                    .into_iter()
                    .filter(|k| i.key_pressed(*k))
                    .collect::<Vec<_>>()
            });
            for key in keys {
                let current = published.clone().unwrap_or_else(|| bands.clone());
                let count = current.len();
                match key {
                    Key::Escape => {
                        if let Some(selecting) = state.selecting.take() {
                            state.set_selection(selecting.before, selecting.primary_before, count);
                        }
                        if state.dragged.take().is_some() {
                            gesture_ended = true;
                        }
                    }
                    Key::A if modifiers.ctrl => {
                        let primary = state.selected.or(Some(0));
                        state.set_selection((0..count).collect(), primary, count);
                    }
                    Key::Delete | Key::Backspace if !modifiers.ctrl => {
                        if let Some(selected) = state.selected {
                            let mut next = current.clone();
                            for &index in state.selection.iter().rev() {
                                if index < next.len() {
                                    next.remove(index);
                                }
                            }
                            let primary = if next.is_empty() { None } else { Some(selected.min(next.len() - 1)) };
                            state.set_selection(primary.into_iter().collect(), primary, next.len());
                            state.band_count = next.len();
                            published = Some(next);
                        }
                    }
                    Key::Insert if !modifiers.ctrl => {
                        if count >= MAXIMUM_BANDS {
                            state.feedback = Some("All 8 bands are in use. Remove a band to add another.".into());
                        } else {
                            let mut next = current.clone();
                            next.push(EqBand::new("peak", true, 1000.0, 0.0, 1.0, 1000.0));
                            state.set_selection([next.len() - 1].into(), Some(next.len() - 1), next.len());
                            state.band_count = next.len();
                            published = Some(next);
                        }
                    }
                    Key::ArrowLeft | Key::ArrowRight if modifiers.ctrl => {
                        if count > 0 {
                            let next = match state.selected {
                                None => 0,
                                Some(i) => (i + if key == Key::ArrowRight { 1 } else { count - 1 }) % count,
                            };
                            if modifiers.shift {
                                let mut selection = state.selection.clone();
                                selection.insert(next);
                                state.set_selection(selection, Some(next), count);
                            } else {
                                state.select(Some(next), false, count);
                            }
                        }
                    }
                    _ if modifiers.ctrl => {}
                    Key::ArrowLeft | Key::ArrowRight | Key::ArrowUp | Key::ArrowDown | Key::Home | Key::PageUp | Key::PageDown => {
                        if state.selected.is_none() && count > 0 {
                            state.select(Some(0), false, count);
                        }
                        let Some(selected) = state.selected else { continue };
                        let fine = modifiers.shift;
                        let frequency_step = 2f64.powf(if fine { 1.0 / 120.0 } else { 1.0 / 12.0 });
                        let gain_step = if fine { 0.1 } else { 0.5 };
                        let next = match key {
                            Key::ArrowLeft => move_selection(&current, &state.selection, 1.0 / frequency_step, 0.0),
                            Key::ArrowRight => move_selection(&current, &state.selection, frequency_step, 0.0),
                            Key::ArrowUp => move_selection(&current, &state.selection, 1.0, gain_step),
                            Key::ArrowDown => move_selection(&current, &state.selection, 1.0, -gain_step),
                            Key::Home => {
                                let mut next = current.clone();
                                let mut changed = false;
                                for &index in &state.selection {
                                    let band = &mut next[index];
                                    let frequency = if band.reset_frequency_hz > 0.0 { band.reset_frequency_hz } else { band.frequency_hz }.clamp(MIN_F, MAX_F);
                                    changed |= band.frequency_hz != frequency || band.gain_db != 0.0;
                                    band.frequency_hz = frequency;
                                    band.gain_db = 0.0;
                                }
                                changed.then_some(next)
                            }
                            _ => {
                                let mut next = current.clone();
                                let q = (next[selected].q * if fine { 1.02f64 } else { 1.1 }.powi(if key == Key::PageUp { 1 } else { -1 })).clamp(0.1, 20.0);
                                (q != next[selected].q).then(|| {
                                    next[selected].q = q;
                                    next
                                })
                            }
                        };
                        if let Some(next) = next {
                            published = Some(next);
                        }
                    }
                    _ => {}
                }
            }
        }
    }
    if response.lost_focus() && state.dragged.take().is_some() {
        gesture_ended = true;
    }

    // Context menu.
    let menu_point = state.menu_point;
    let menu_band = state.menu_band;
    let mut menu_edit: Option<Vec<EqBand>> = None;
    let mut menu_selection: Option<(BTreeSet<usize>, Option<usize>)> = None;
    let current_selection = state.selection.clone();
    egui::Popup::context_menu(&response).frame(widgets::menu_frame()).show(|ui| {
        widgets::menu_body(ui, 200.0);
        match menu_band.filter(|&i| i < count) {
            Some(index) => {
                widgets::submenu(ui, "Shape", true, "", |ui| {
                    for (kind, label) in [("peak", "Bell"), ("lowShelf", "Low shelf"), ("highShelf", "High shelf"), ("notch", "Notch")] {
                        let mut checked = bands[index].band_type == kind;
                        if widgets::menu_check(ui, &mut checked, label, true, "") && checked {
                            let mut next = bands.clone();
                            next[index].band_type = kind.into();
                            if kind == "notch" {
                                next[index].gain_db = 0.0;
                            }
                            menu_edit = Some(next);
                            ui.close();
                        }
                    }
                });
                widgets::menu_separator(ui);
                if widgets::menu_item(ui, if bands[index].enabled { "Disable band" } else { "Enable band" }, "", true, "") {
                    let mut next = bands.clone();
                    next[index].enabled = !next[index].enabled;
                    menu_edit = Some(next);
                    ui.close();
                }
                if widgets::menu_item(ui, "Reset position", "Double-click", true, "") {
                    let mut next = bands.clone();
                    let band = &mut next[index];
                    band.frequency_hz = if band.reset_frequency_hz > 0.0 { band.reset_frequency_hz } else { band.frequency_hz }.clamp(MIN_F, MAX_F);
                    band.gain_db = 0.0;
                    menu_edit = Some(next);
                    ui.close();
                }
                widgets::menu_separator(ui);
                let group = current_selection.contains(&index) && current_selection.len() > 1;
                if widgets::menu_item(ui, if group { "Remove selected bands" } else { "Remove band" }, "Delete", true, "") {
                    let removing: BTreeSet<usize> = if current_selection.contains(&index) { current_selection.clone() } else { [index].into() };
                    let mut next = bands.clone();
                    for &i in removing.iter().rev() {
                        if i < next.len() {
                            next.remove(i);
                        }
                    }
                    let primary = if next.is_empty() { None } else { Some(index.min(next.len() - 1)) };
                    menu_selection = Some((primary.into_iter().collect(), primary));
                    menu_edit = Some(next);
                    ui.close();
                }
            }
            None => {
                let full = count >= MAXIMUM_BANDS;
                if widgets::menu_item(ui, if full { "All 8 bands are in use" } else { "Add bell band here" }, "", !full, "") {
                    let (frequency, gain) = point_to_band(menu_point, plot);
                    let anchor = frequency.clamp(MIN_F, MAX_F);
                    let mut next = bands.clone();
                    next.push(EqBand::new("peak", true, anchor, gain.clamp(MIN_G, MAX_G), 1.0, anchor));
                    let last = next.len() - 1;
                    menu_selection = Some(([last].into(), Some(last)));
                    menu_edit = Some(next);
                    ui.close();
                }
                widgets::menu_separator(ui);
                if widgets::menu_item(ui, "Reset EQ to flat", "", true, "") {
                    menu_selection = Some((BTreeSet::new(), None));
                    menu_edit = Some(Vec::new());
                    ui.close();
                }
            }
        }
    });
    if let Some(next) = menu_edit {
        if let Some((selection, primary)) = menu_selection {
            state.set_selection(selection, primary, next.len());
        }
        state.band_count = next.len();
        published = Some(next);
    }

    // Paint.
    let shown = published.clone().unwrap_or_else(|| bands.clone());
    let painter = ctx.ui.painter().clone();
    let focused = response.has_focus();
    painter.rect(
        screen.shrink(0.5 * s),
        3.0 * s,
        theme::WELL,
        Stroke::new(s, if focused { theme::FOCUS } else { theme::CONTROL_BORDER }),
        egui::StrokeKind::Middle,
    );
    let font = |size: f32| theme::font(size * s, Weight::Regular);
    let caption = state.feedback.clone().unwrap_or_else(|| {
        if shown.is_empty() {
            "Flat EQ · no bands".into()
        } else if node.bypass {
            "Bypassed · band settings retained".into()
        } else if let Some(selected) = state.selected.filter(|&i| i < shown.len()) {
            if state.selection.len() > 1 {
                format!("{} selected · active {}", state.selection.len(), band_description(&shown, selected))
            } else {
                band_description(&shown, selected)
            }
        } else {
            format!("{} / {MAXIMUM_BANDS} bands · select a point", shown.len())
        }
    });
    widgets::text_elided(&painter, to_screen(Pos2::new(plot.center().x, 6.0)), Align2::CENTER_TOP, &caption, font(13.0), theme::TEXT, (plot.width() - 16.0) * s);
    let grid = Stroke::new(0.7 * s, theme::CONTROL_BORDER);
    let zero = Stroke::new(1.2 * s, theme::BORDER);
    for gain in [-24.0, -18.0, -12.0, -6.0, 0.0, 6.0, 12.0, 18.0, 24.0] {
        let y = band_to_point(1000.0, gain, plot).y;
        painter.line_segment([to_screen(Pos2::new(plot.left(), y)), to_screen(Pos2::new(plot.right(), y))], if gain == 0.0 { zero } else { grid });
        let label = if gain > 0.0 { format!("+{gain}") } else { format!("{gain}") };
        painter.text(to_screen(Pos2::new(5.0, y)), Align2::LEFT_CENTER, label, font(11.0), theme::MUTED);
    }
    for frequency in [20.0, 30.0, 50.0, 70.0, 100.0, 200.0, 300.0, 500.0, 700.0, 1000.0, 2000.0, 3000.0, 5000.0, 7000.0, 10000.0, 20000.0] {
        let x = band_to_point(frequency, 0.0, plot).x;
        painter.line_segment([to_screen(Pos2::new(x, plot.top())), to_screen(Pos2::new(x, plot.bottom()))], grid);
    }
    let mut last_right = f32::NEG_INFINITY;
    for (frequency, label) in
        [(20.0, "20"), (50.0, "50"), (100.0, "100"), (200.0, "200"), (500.0, "500"), (1000.0, "1k"), (2000.0, "2k"), (5000.0, "5k"), (10000.0, "10k"), (20000.0, "20k")]
    {
        let galley = painter.layout_no_wrap(label.into(), font(11.0), theme::MUTED);
        let width = galley.size().x / s;
        let x = (band_to_point(frequency, 0.0, plot).x - width / 2.0).clamp(plot.left(), (plot.right() - width).max(plot.left()));
        if x < last_right + 4.0 {
            continue;
        }
        painter.galley(to_screen(Pos2::new(x, plot.bottom() + 9.0)), galley, theme::MUTED);
        last_right = x + width;
    }
    painter.text(to_screen(Pos2::new(6.0, plot.top() - 18.0)), Align2::LEFT_TOP, "dB", font(10.0), theme::MUTED);
    painter.text(to_screen(Pos2::new(plot.right() + 8.0, plot.bottom() + 9.0)), Align2::LEFT_TOP, "Hz", font(10.0), theme::MUTED);
    let spectrum_visible = state.spectrum_visible && full;
    if spectrum_visible {
        painter.text(to_screen(Pos2::new(plot.right() + 7.0, plot.top() - 18.0)), Align2::LEFT_TOP, "dBFS", font(9.0), theme::MUTED);
        for db in [0.0f32, -18.0, -36.0, -54.0, -72.0, -90.0] {
            painter.text(
                to_screen(Pos2::new(plot.right() + 8.0, plot.top() - db / 90.0 * plot.height())),
                Align2::LEFT_CENTER,
                format!("{db}"),
                font(10.0),
                theme::MUTED,
            );
        }
    }
    let clip = painter.with_clip_rect(Rect::from_min_max(to_screen(plot.min), to_screen(plot.max)).intersect(painter.clip_rect()));
    if spectrum_visible {
        let points: Vec<Pos2> = state
            .spectrum
            .iter()
            .enumerate()
            .map(|(i, db)| to_screen(Pos2::new(plot.left() + plot.width() * i as f32 / 127.0, plot.top() + (-db / 90.0).clamp(0.0, 1.0) * plot.height())))
            .collect();
        let mut mesh = egui::Mesh::default();
        let fill = theme::with_opacity(theme::MUTED, 0.14);
        let bottom = to_screen(Pos2::new(0.0, plot.bottom())).y;
        for pair in points.windows(2) {
            let base = mesh.vertices.len() as u32;
            mesh.colored_vertex(pair[0], fill);
            mesh.colored_vertex(pair[1], fill);
            mesh.colored_vertex(Pos2::new(pair[1].x, bottom), fill);
            mesh.colored_vertex(Pos2::new(pair[0].x, bottom), fill);
            mesh.add_triangle(base, base + 1, base + 2);
            mesh.add_triangle(base, base + 2, base + 3);
        }
        clip.add(mesh);
        clip.add(egui::Shape::line(points, Stroke::new(s, theme::with_opacity(theme::MUTED, 0.48))));
        ctx.ui.ctx().request_repaint_after(std::time::Duration::from_millis(50));
    }
    let steps = ((plot.width() * s * ctx.ui.ctx().pixels_per_point()).ceil() as usize).clamp(128, 1536);
    let curve: Vec<Pos2> = (0..=steps)
        .map(|sample| {
            let frequency = MIN_F * (MAX_F / MIN_F).powf(sample as f64 / steps as f64);
            to_screen(band_to_point(frequency, combined_response_db(&shown, node.bypass, frequency), plot))
        })
        .collect();
    if node.bypass {
        clip.extend(egui::Shape::dashed_line(&curve, Stroke::new(2.2 * s, theme::MUTED), 4.4 * s, 4.4 * s));
    } else {
        clip.add(egui::Shape::line(curve, Stroke::new(2.2 * s, theme::A)));
    }
    if let Some(selecting) = state.selecting.as_ref().filter(|b| b.moved) {
        let area = Rect::from_two_pos(to_screen(selecting.origin), to_screen(selecting.pointer));
        clip.rect_filled(area, 0.0, theme::with_opacity(theme::NODE_SELECTED, 0.15));
        clip.extend(egui::Shape::dashed_line(
            &[area.left_top(), area.right_top(), area.right_bottom(), area.left_bottom(), area.left_top()],
            Stroke::new(s, theme::NODE_SELECTED),
            3.0 * s,
            3.0 * s,
        ));
    }
    let mut order: Vec<usize> = (0..shown.len()).filter(|i| !state.selection.contains(i)).collect();
    order.extend(state.selection.iter().copied().filter(|&i| Some(i) != state.selected && i < shown.len()));
    if let Some(selected) = state.selected.filter(|&i| i < shown.len()) {
        order.push(selected);
    }
    for index in order {
        let selected = state.selection.contains(&index);
        let band = &shown[index];
        let point = to_screen(band_position(&shown, index));
        let outline = if selected || state.hovered == Some(index) {
            theme::NODE_SELECTED
        } else if band.enabled {
            theme::A
        } else {
            theme::MUTED
        };
        let fill = if selected {
            theme::NODE_SELECTED
        } else if band.enabled {
            theme::A
        } else {
            theme::WELL
        };
        let number = if selected || band.enabled { theme::INK } else { theme::MUTED };
        super::widgets::circle(&painter, point, POINT_RADIUS * s, fill, Stroke::new(if selected { 2.0 } else { 1.5 } * s, outline));
        if state.selected == Some(index) {
            super::widgets::circle(&painter, point, (POINT_RADIUS + 3.0) * s, egui::Color32::TRANSPARENT, Stroke::new(s, outline));
        }
        painter.text(point, Align2::CENTER_CENTER, format!("{}", index + 1), font(13.0), number);
        if !band.enabled {
            painter.line_segment([point + Vec2::new(-6.0, 7.0) * s, point + Vec2::new(6.0, -7.0) * s], Stroke::new(s, theme::MUTED));
        }
    }
    if shown.is_empty() {
        painter.text(
            to_screen(Pos2::new(plot.center().x, plot.top() + plot.height() / 2.0 - 31.0)),
            Align2::CENTER_TOP,
            "Double-click to add a band",
            font(13.0),
            theme::MUTED,
        );
    }
    let tip = if state.feedback.is_some() {
        "All 8 bands are in use\nMaximum EQ capacity · 8 bands".to_owned()
    } else if let Some(index) = state.hovered.or(state.selected).filter(|&i| i < shown.len()) {
        let band = &shown[index];
        format!(
            "{}\n{}\n{}\n{}",
            if Some(index) == state.selected { format!("Selected band {}", index + 1) } else { format!("Band {}", index + 1) },
            band_effect(band),
            band_description(&shown, index),
            if node.bypass {
                "Bypassed · EQ changes are not applied"
            } else if band.enabled {
                "Enabled · Contributes to the EQ response"
            } else {
                "Disabled · This band does not affect audio"
            }
        )
    } else {
        format!(
            "Equalizer\nShapes frequency balance with up to 8 bands\nLine · Calculated EQ response on the left dB scale\nShaded spectrum · Measured output on the right dBFS scale\n{}",
            if node.bypass {
                "Bypassed · EQ settings retained".to_owned()
            } else if shown.is_empty() {
                "No bands · Audio passes without EQ changes".to_owned()
            } else {
                format!("{} / {MAXIMUM_BANDS} bands", shown.len())
            }
        )
    };
    let response = response.tip(tip);
    if response.hovered() && enabled {
        ctx.ui.ctx().set_cursor_icon(if state.dragged.is_some() {
            egui::CursorIcon::Move
        } else if state.hovered.is_some() {
            egui::CursorIcon::PointingHand
        } else {
            egui::CursorIcon::Crosshair
        });
    }
    let selected = state.selected.filter(|&i| i < shown.len());
    if gesture_started {
        ctx.begin_gesture();
    }
    if let Some(next) = published {
        let copy = publish_copy(&next);
        ctx.edit(move |n| n.eq_bands = copy.clone());
    }
    if gesture_ended {
        ctx.end_gesture();
    }
    if state_is_editing(&ctx.runtime.eq) {
        ctx.runtime.editing = true;
    }

    // Band controls.
    let third = details_world.width() / 3.0;
    let band = selected.map(|i| shown[i].clone());
    let has_band = band.is_some();
    let frequency_default = band.as_ref().map_or(1000.0, |b| if b.reset_frequency_hz > 0.0 { b.reset_frequency_hz } else { b.frequency_hz });
    let q_default = band.as_ref().map_or(1.0, |b| graph::default_q_for_type(&b.band_type));
    let index = selected.unwrap_or(0);
    let specs = [
        (
            Spec::new("eq-frequency", "Frequency", 20.0, 20000.0, 1.0, "Hz", frequency_default, Variant::Compact)
                .logarithmic(true)
                .changes(10.0, 100.0)
                .primary(true),
            band.as_ref().map_or(1000.0, |b| b.frequency_hz),
            0usize,
            has_band,
        ),
        (
            Spec::new("eq-gain", "Band gain", -24.0, 24.0, 0.1, "dB", 0.0, Variant::Compact).changes(0.5, 3.0).primary(true),
            band.as_ref().map_or(0.0, |b| b.gain_db),
            1,
            has_band && band.as_ref().is_some_and(supports_gain),
        ),
        (
            Spec::new("eq-q", "Width / Q", 0.1, 20.0, 0.01, "", q_default, Variant::Compact)
                .logarithmic(true)
                .changes(0.1, 1.0)
                .format("0.0###")
                .primary(true),
            band.as_ref().map_or(1.0, |b| b.q),
            2,
            has_band,
        ),
    ];
    for (spec, value, column, control_enabled) in specs {
        let cell = Rect::from_min_size(Pos2::new(details_world.left() + third * column as f32, details_world.top()), Vec2::new(third, 139.0));
        let title = ctx.screen(Rect::from_min_size(cell.min, Vec2::new(cell.width(), 18.0)));
        ctx.ui.painter().text(
            title.center(),
            Align2::CENTER_CENTER,
            &spec.label,
            theme::font(12.0 * s, Weight::SemiBold),
            if has_band { theme::TEXT } else { theme::with_opacity(theme::TEXT, 0.45) },
        );
        let face = Rect::from_center_size(Pos2::new(cell.center().x, cell.top() + 20.0 + 43.0), Vec2::splat(86.0));
        let entry_width = 110.0f32.min(cell.width());
        let entry = Rect::from_min_size(Pos2::new(cell.center().x - entry_width / 2.0, cell.top() + 20.0 + 87.0), Vec2::new(entry_width, 28.0));
        let face_screen = ctx.screen(face);
        let entry_screen = ctx.screen(entry);
        let enabled = ctx.env.can_edit && control_enabled;
        let state_key = spec.key.clone();
        let dial_id = ctx.id.with(("dial", state_key.as_str()));
        let dial_state = ctx.runtime.dials.entry(state_key).or_default();
        let outcome = super::dial::show(ctx.ui, dial_id, &spec, value, spec.default, dial_state, face_screen, entry_screen, s, enabled);
        ctx.runtime.editing |= outcome.editing || dial_state.dragging;
        if outcome.gesture_started {
            ctx.begin_gesture();
        }
        if let (Some(next), true) = (outcome.changed, has_band) {
            ctx.edit(move |n| {
                let Some(band) = n.eq_bands.get_mut(index) else { return };
                if band.reset_frequency_hz == 0.0 {
                    band.reset_frequency_hz = band.frequency_hz;
                }
                match column {
                    0 => band.frequency_hz = next,
                    1 => band.gain_db = next,
                    _ => band.q = next,
                }
            });
        }
        if outcome.gesture_ended {
            ctx.end_gesture();
        }
    }
}

fn state_is_editing(state: &EqState) -> bool {
    state.dragged.is_some() || state.selecting.is_some()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn responses() {
        let flat = EqBand::new("peak", true, 1000.0, 0.0, 1.0, 1000.0);
        assert!(response_db(&flat, 1000.0).abs() < 1e-9);
        let boost = EqBand::new("peak", true, 1000.0, 6.0, 1.0, 1000.0);
        assert!((response_db(&boost, 1000.0) - 6.0).abs() < 1e-6);
        let mut off = boost.clone();
        off.enabled = false;
        assert_eq!(response_db(&off, 1000.0), 0.0);
        let selection: BTreeSet<usize> = [0].into();
        let moved = move_selection(&[boost.clone()], &selection, 100.0, 30.0).unwrap();
        assert_eq!(moved[0].frequency_hz, 20000.0);
        assert_eq!(moved[0].gain_db, 24.0);
    }
}
