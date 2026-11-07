//! Stereo sample-peak meter with peak hold, release and clip latch
//! (`StereoNodeMeter`, `MeterPeakDisplay`).

use eframe::egui::{self, Color32, Painter, Pos2, Rect, Stroke};

use super::theme;

pub const PEAK_HOLD_SECONDS: f64 = 0.1;
pub const RELEASE_DB_PER_SECOND: f64 = 24.0;
pub const CLIP_HOLD_SECONDS: f64 = 2.0;
pub const CLIP_FADE_SECONDS: f64 = 1.0;
const FLOOR_DB: f64 = -60.0;
const FLOOR_AMPLITUDE: f64 = 0.001;
const REDUCED_PERIOD: f64 = 0.25;
const FRESHNESS_SECONDS: f64 = 0.5;

#[derive(Clone, Debug, Default)]
pub struct Meter {
    initialized: bool,
    pending: bool,
    pending_clip: bool,
    target_left: f64,
    target_right: f64,
    pending_left: f64,
    pending_right: f64,
    hold_left: f64,
    hold_right: f64,
    last_advance: f64,
    last_sample: f64,
    last_presentation: f64,
    last_clip_sample: Option<f64>,
    pub left: f64,
    pub right: f64,
    pub clipped: bool,
    pub retained_left: f64,
    pub retained_right: f64,
    pub has_readings: bool,
    pub clip_latched: bool,
    pub clip_opacity: f64,
    pub muted: bool,
    pub reduced: bool,
    pub channels: u8,
}

fn sample(value: f64) -> f64 {
    if value.is_finite() && value > 0.0 { value } else { 0.0 }
}

fn release(displayed: f64, target: f64, seconds: f64) -> f64 {
    if displayed <= target || seconds <= 0.0 {
        return displayed;
    }
    let released = displayed * 10f64.powf(-RELEASE_DB_PER_SECOND * seconds / 20.0);
    target.max(if released < FLOOR_AMPLITUDE { 0.0 } else { released })
}

impl Meter {
    pub fn level(&self) -> f64 {
        self.left.max(self.right)
    }

    #[allow(clippy::too_many_arguments, clippy::fn_params_excessive_bools)]
    pub fn update(&mut self, left: f64, right: f64, channels: u8, clipped: bool, muted: bool, reduced: bool, now: f64, observe_peak: bool) {
        let channels = if channels == 1 { 1 } else { 2 };
        let left = if muted { 0.0 } else { sample(left) };
        let right = if muted || channels == 1 { 0.0 } else { sample(right) };
        let clipped = clipped && !muted;
        let now = if now.is_finite() { now } else if self.initialized { self.last_advance } else { 0.0 };
        let reset_peak = !self.initialized || muted || now < self.last_advance || channels != self.channels;
        let reset = !self.initialized || now < self.last_advance || muted != self.muted || reduced != self.reduced || channels != self.channels;
        self.muted = muted;
        self.reduced = reduced;
        self.channels = channels;
        if reset {
            self.initialized = true;
            self.last_advance = now;
            self.last_sample = now;
            self.last_presentation = now;
            self.target_left = left;
            self.left = left;
            self.target_right = right;
            self.right = right;
            self.hold_left = if left > 0.0 { now + PEAK_HOLD_SECONDS } else { now };
            self.hold_right = if right > 0.0 { now + PEAK_HOLD_SECONDS } else { now };
            self.clipped = clipped;
            self.clear_pending();
        } else if muted {
            self.last_advance = now;
            self.last_sample = now;
            self.last_presentation = now;
            self.target_left = 0.0;
            self.target_right = 0.0;
            self.left = 0.0;
            self.right = 0.0;
            self.clipped = false;
            self.clear_pending();
        } else if reduced {
            self.last_sample = now;
            self.target_left = left;
            self.target_right = right;
            self.pending = true;
            self.pending_left = self.pending_left.max(left);
            self.pending_right = self.pending_right.max(right);
            self.pending_clip |= clipped;
            if (clipped && !self.clipped) || now - self.last_presentation >= REDUCED_PERIOD {
                self.publish_reduced(now);
            }
        } else {
            self.advance_levels(now);
            self.last_sample = now;
            self.target_left = left;
            self.target_right = right;
            self.attack(left, right, now);
            self.clipped = clipped;
        }
        let mut retained_left = if reset_peak { 0.0 } else { self.retained_left };
        let mut retained_right = if reset_peak { 0.0 } else { self.retained_right };
        let mut has_readings = !reset_peak && self.has_readings;
        let mut clip_latched = !reset_peak && self.clip_latched;
        if reset_peak {
            self.clear_clip_indicator();
        }
        if observe_peak && !muted {
            retained_left = retained_left.max(left.min(1.0));
            retained_right = retained_right.max(right.min(1.0));
            has_readings = true;
            clip_latched |= clipped;
            if clipped {
                self.last_clip_sample = Some(now);
                self.clip_opacity = 1.0;
            }
        }
        self.advance_clip_indicator(now);
        self.retained_left = retained_left;
        self.retained_right = retained_right;
        self.has_readings = has_readings;
        self.clip_latched = clip_latched;
    }

    pub fn clear_peak_readings(&mut self) {
        self.clear_clip_indicator();
        self.retained_left = 0.0;
        self.retained_right = 0.0;
        self.has_readings = false;
        self.clip_latched = false;
    }

    fn clear_clip_indicator(&mut self) {
        self.last_clip_sample = None;
        self.clip_opacity = 0.0;
    }

    fn advance_clip_indicator(&mut self, now: f64) {
        let Some(last) = self.last_clip_sample else {
            self.clip_opacity = 0.0;
            return;
        };
        let mut fade = now - last - CLIP_HOLD_SECONDS;
        if self.reduced && fade > 0.0 {
            fade = (fade / REDUCED_PERIOD).floor() * REDUCED_PERIOD;
        }
        let opacity = if self.muted { 0.0 } else { (1.0 - fade / CLIP_FADE_SECONDS).clamp(0.0, 1.0) };
        self.clip_opacity = self.clip_opacity.min(opacity);
    }

    /// Advances animation to `now`.
    pub fn advance(&mut self, now: f64) {
        if !self.initialized || !now.is_finite() || now < self.last_advance {
            return;
        }
        if self.reduced {
            if now - self.last_presentation >= REDUCED_PERIOD {
                self.publish_reduced(now);
            }
        } else {
            self.advance_levels(now);
            if now - self.last_sample >= FRESHNESS_SECONDS {
                self.clipped = false;
            }
        }
        self.advance_clip_indicator(now);
    }

    fn publish_reduced(&mut self, now: f64) {
        self.advance_levels(now);
        if self.pending {
            self.attack(self.pending_left, self.pending_right, now);
            self.clipped = self.pending_clip;
        } else if now - self.last_sample >= FRESHNESS_SECONDS {
            self.clipped = false;
        }
        self.clear_pending();
        self.last_presentation = now;
    }

    fn attack(&mut self, left: f64, right: f64, now: f64) {
        if left >= self.left {
            self.left = left;
            if left > 0.0 {
                self.hold_left = now + PEAK_HOLD_SECONDS;
            }
        }
        if right >= self.right {
            self.right = right;
            if right > 0.0 {
                self.hold_right = now + PEAK_HOLD_SECONDS;
            }
        }
    }

    fn advance_levels(&mut self, now: f64) {
        if now <= self.last_advance {
            return;
        }
        let expiry = self.last_sample + FRESHNESS_SECONDS;
        if self.last_advance < expiry && now >= expiry {
            self.release_to(expiry, self.target_left, self.target_right);
            self.release_to(now, 0.0, 0.0);
        } else if now >= expiry {
            self.release_to(now, 0.0, 0.0);
        } else {
            self.release_to(now, self.target_left, self.target_right);
        }
    }

    fn release_to(&mut self, now: f64, left: f64, right: f64) {
        self.left = release(self.left, left, 0f64.max(now - self.last_advance.max(self.hold_left)));
        self.right =
            if self.channels == 1 { 0.0 } else { release(self.right, right, 0f64.max(now - self.last_advance.max(self.hold_right))) };
        self.last_advance = now;
    }

    fn clear_pending(&mut self) {
        self.pending = false;
        self.pending_clip = false;
        self.pending_left = 0.0;
        self.pending_right = 0.0;
    }

    /// Seconds until the next visual change, if the meter is animating.
    pub fn next_frame(&self) -> Option<f64> {
        if !self.initialized
            || self.muted
            || (self.level() <= 0.0 && self.pending_left <= 0.0 && self.pending_right <= 0.0 && !self.pending_clip && !self.clipped && self.clip_opacity <= 0.0)
        {
            return None;
        }
        let mut interval = if self.reduced {
            REDUCED_PERIOD
        } else if self.left > self.target_left || self.right > self.target_right || self.last_advance >= self.last_sample + FRESHNESS_SECONDS {
            1.0 / 30.0
        } else {
            (1.0 / 30.0f64).max(self.last_sample + FRESHNESS_SECONDS - self.last_advance)
        };
        if !self.reduced && self.clip_opacity > 0.0 {
            let until_fade = self.last_clip_sample.unwrap_or(0.0) + CLIP_HOLD_SECONDS - self.last_advance;
            interval = if until_fade > 0.0 { interval.min((1.0 / 30.0f64).max(until_fade)) } else { 1.0 / 30.0 };
        }
        Some(interval)
    }

    /// Tooltip lines: highest observed peaks and clip latch.
    pub fn peak_lines(&self) -> Vec<String> {
        let mut lines = Vec::new();
        if !self.has_readings {
            lines.push("No readings yet".to_owned());
        } else if self.channels == 1 {
            lines.push(format!("Peak · {}", format_peak(self.retained_left)));
        } else {
            lines.push(format!("Left · {}", format_peak(self.retained_left)));
            lines.push(format!("Right · {}", format_peak(self.retained_right)));
        }
        if self.clip_latched {
            lines.push("Over 0 dBFS detected".to_owned());
        }
        lines
    }
}

pub fn format_peak(amplitude: f64) -> String {
    if !amplitude.is_finite() || amplitude <= 0.0 {
        return "−∞ dBFS".into();
    }
    let db = (20.0 * amplitude.min(1.0).log10()).min(0.0);
    let rounded = (db * 10.0).abs().round().copysign(db) / 10.0;
    if rounded == 0.0 {
        return "0.0 dBFS".into();
    }
    format!("{rounded:.1} dBFS").replace('-', "−")
}

/// Paints the meter lanes into `rect` (screen space). `scale` is the canvas
/// zoom, so the meter keeps its world-space proportions.
pub fn paint(painter: &Painter, rect: Rect, meter: &Meter, scale: f32) {
    let channels = if meter.channels == 1 { 1usize } else { 2 };
    let top = 7.0 * scale;
    let gap = 4.0 * scale;
    let bar_height = (rect.height() - top - 2.0 * scale).max(0.0);
    let bar_width = (if channels == 1 { 12.0 } else { 10.0 } * scale)
        .min(((rect.width() - 4.0 * scale - if channels == 1 { 0.0 } else { gap }) / channels as f32).max(0.0));
    let total = bar_width * channels as f32 + if channels == 1 { 0.0 } else { gap };
    let x = rect.left() + (rect.width() - total) / 2.0;
    for lane in 0..channels {
        let lane_rect = Rect::from_min_size(Pos2::new(x + lane as f32 * (bar_width + gap), rect.top() + top), egui::vec2(bar_width, bar_height));
        if lane_rect.width() <= 0.0 || lane_rect.height() <= 0.0 {
            continue;
        }
        painter.rect_filled(lane_rect, 0.0, theme::METER_WELL);
        painter.rect_stroke(lane_rect.shrink(0.5 * scale), 0.0, Stroke::new(scale, theme::BORDER), egui::StrokeKind::Middle);
        let cap = Rect::from_min_size(Pos2::new(lane_rect.left(), rect.top() + scale), egui::vec2(lane_rect.width(), 3.0 * scale));
        painter.rect_filled(cap, 0.0, theme::METER_UNLIT);
        if !meter.muted && meter.clip_opacity > 0.0 {
            painter.rect_filled(cap, 0.0, theme::with_opacity(theme::METER_HIGH, meter.clip_opacity as f32));
        }
        let value = if lane == 0 { meter.left } else { meter.right };
        let db = if !meter.muted && value > 0.0 { (20.0 * value.log10()).clamp(FLOOR_DB, 0.0) } else { FLOOR_DB };
        let scale_rect = lane_rect.shrink(scale);
        paint_scale(painter, scale_rect, 0.2);
        if db > FLOOR_DB {
            let fill = ((db - FLOOR_DB) / -FLOOR_DB) as f32 * scale_rect.height();
            let clip = Rect::from_min_max(Pos2::new(scale_rect.left(), scale_rect.bottom() - fill), scale_rect.max);
            paint_scale(&painter.with_clip_rect(clip.intersect(painter.clip_rect())), scale_rect, 1.0);
        }
    }
}

/// The low/mid/high gradient: green to −18 dB, yellow from −9 to −6 dB,
/// red from −1.5 dB.
fn paint_scale(painter: &Painter, rect: Rect, opacity: f32) {
    let stops: [(f64, Color32); 6] = [
        (FLOOR_DB, theme::METER_LOW),
        (-18.0, theme::METER_LOW),
        (-9.0, theme::METER_MID),
        (-6.0, theme::METER_MID),
        (-1.5, theme::METER_HIGH),
        (0.0, theme::METER_HIGH),
    ];
    let mut mesh = egui::Mesh::default();
    let y = |db: f64| rect.bottom() - ((db - FLOOR_DB) / -FLOOR_DB) as f32 * rect.height();
    for window in stops.windows(2) {
        let (from_db, from) = window[0];
        let (to_db, to) = window[1];
        let (bottom, top) = (y(from_db), y(to_db));
        if (bottom - top).abs() < f32::EPSILON {
            continue;
        }
        let base = mesh.vertices.len() as u32;
        let from = theme::with_opacity(from, opacity);
        let to = theme::with_opacity(to, opacity);
        mesh.colored_vertex(Pos2::new(rect.left(), bottom), from);
        mesh.colored_vertex(Pos2::new(rect.right(), bottom), from);
        mesh.colored_vertex(Pos2::new(rect.right(), top), to);
        mesh.colored_vertex(Pos2::new(rect.left(), top), to);
        mesh.add_triangle(base, base + 1, base + 2);
        mesh.add_triangle(base, base + 2, base + 3);
    }
    painter.add(mesh);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn hold_release_and_clip() {
        let mut meter = Meter::default();
        meter.update(0.5, 0.5, 2, false, false, false, 0.0, true);
        meter.update(0.0, 0.0, 2, false, false, false, 0.05, true);
        assert_eq!(meter.left, 0.5, "held for 100 ms");
        meter.advance(1.1);
        assert!(meter.left < 0.5 && meter.left > 0.0);
        meter.update(1.0, 0.2, 2, true, false, false, 1.2, true);
        assert!(meter.clip_latched);
        assert_eq!(meter.clip_opacity, 1.0);
        meter.advance(3.7);
        assert!(meter.clip_opacity < 1.0 && meter.clip_opacity > 0.0);
        assert_eq!(format_peak(1.0), "0.0 dBFS");
        assert_eq!(format_peak(0.5), "−6.0 dBFS");
    }
}
