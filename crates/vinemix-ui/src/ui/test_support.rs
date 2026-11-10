//! Headless egui harness for interface regression tests.
//!
//! Runs production widgets against a real `egui::Context` with the production
//! theme and synthetic time and input. No window, engine, settings file or
//! audio device is involved. Geometry is in egui logical points; native
//! rendering, OS menus and assistive technology are outside what this
//! harness can observe.

use eframe::egui::{self, Event, Key, Modifiers, PointerButton, Pos2, Rect, Vec2};

#[allow(missing_debug_implementations)]
pub struct Harness {
    pub ctx: egui::Context,
    time: f64,
    size: Vec2,
    pixels_per_point: f32,
    modifiers: Modifiers,
    pending: Vec<Event>,
    shapes: Vec<egui::epaint::ClippedShape>,
    accessibility: Option<egui::accesskit::TreeUpdate>,
}

impl Default for Harness {
    fn default() -> Self {
        Self::new()
    }
}

impl Harness {
    pub fn new() -> Self {
        Self::sized(1000.0, 700.0)
    }

    pub fn sized(width: f32, height: f32) -> Self {
        let ctx = egui::Context::default();
        super::theme::install(&ctx);
        Self { ctx, time: 0.0, size: Vec2::new(width, height), pixels_per_point: 1.0, modifiers: Modifiers::NONE, pending: Vec::new(), shapes: Vec::new(), accessibility: None }
    }

    /// Display scale (DPI) for subsequent frames.
    pub fn pixels_per_point(mut self, value: f32) -> Self {
        self.pixels_per_point = value;
        self
    }

    pub fn resize(&mut self, width: f32, height: f32) {
        self.size = Vec2::new(width, height);
    }

    pub fn screen(&self) -> Rect {
        Rect::from_min_size(Pos2::ZERO, self.size)
    }

    /// Queues an event for the next frame.
    pub fn event(&mut self, event: Event) -> &mut Self {
        self.pending.push(event);
        self
    }

    pub fn move_to(&mut self, pos: Pos2) -> &mut Self {
        self.event(Event::PointerMoved(pos))
    }

    pub fn press(&mut self, pos: Pos2) -> &mut Self {
        let modifiers = self.modifiers;
        self.event(Event::PointerButton { pos, button: PointerButton::Primary, pressed: true, modifiers })
    }

    pub fn release(&mut self, pos: Pos2) -> &mut Self {
        let modifiers = self.modifiers;
        self.event(Event::PointerButton { pos, button: PointerButton::Primary, pressed: false, modifiers })
    }

    pub fn press_secondary(&mut self, pos: Pos2) -> &mut Self {
        let modifiers = self.modifiers;
        self.event(Event::PointerButton { pos, button: PointerButton::Secondary, pressed: true, modifiers })
    }

    pub fn release_secondary(&mut self, pos: Pos2) -> &mut Self {
        let modifiers = self.modifiers;
        self.event(Event::PointerButton { pos, button: PointerButton::Secondary, pressed: false, modifiers })
    }

    pub fn key(&mut self, key: Key) -> &mut Self {
        let modifiers = self.modifiers;
        self.event(Event::Key { key, physical_key: None, pressed: true, repeat: false, modifiers });
        self.event(Event::Key { key, physical_key: None, pressed: false, repeat: false, modifiers })
    }

    pub fn text(&mut self, text: &str) -> &mut Self {
        self.event(Event::Text(text.to_owned()))
    }

    pub fn set_modifiers(&mut self, modifiers: Modifiers) -> &mut Self {
        self.modifiers = modifiers;
        self.event(Event::ModifiersChanged(modifiers))
    }

    /// Runs one frame `dt` seconds after the previous one with the queued events.
    pub fn frame_after<R>(&mut self, dt: f64, mut ui: impl FnMut(&mut egui::Ui) -> R) -> R {
        self.time += dt;
        let mut viewports = egui::ViewportIdMap::default();
        viewports.insert(
            egui::ViewportId::ROOT,
            egui::ViewportInfo { native_pixels_per_point: Some(self.pixels_per_point), focused: Some(true), ..Default::default() },
        );
        let input = egui::RawInput {
            screen_rect: Some(self.screen()),
            time: Some(self.time),
            predicted_dt: dt as f32,
            events: std::mem::take(&mut self.pending),
            focused: true,
            viewports,
            ..Default::default()
        };
        let mut result = None;
        let mut output = self.ctx.run_ui(input, |root| result = Some(ui(root)));
        // No renderer: font-atlas updates are intentionally discarded.
        output.textures_delta.clear();
        self.shapes = std::mem::take(&mut output.shapes);
        if let Some(update) = output.platform_output.accesskit_update.take() {
            self.accessibility = Some(update);
        }
        result.expect("the frame ran")
    }

    /// One 16 ms frame.
    pub fn frame<R>(&mut self, ui: impl FnMut(&mut egui::Ui) -> R) -> R {
        self.frame_after(1.0 / 60.0, ui)
    }

    /// `count` frames with no new input, returning the last result.
    pub fn settle<R>(&mut self, count: usize, mut ui: impl FnMut(&mut egui::Ui) -> R) -> R {
        let mut last = None;
        for _ in 0..count.max(1) {
            last = Some(self.frame(&mut ui));
        }
        last.expect("at least one frame")
    }

    /// A full primary click at `pos`: move, press and release on separate frames.
    pub fn click<R>(&mut self, pos: Pos2, mut ui: impl FnMut(&mut egui::Ui) -> R) -> R {
        self.move_to(pos);
        self.frame(&mut ui);
        self.press(pos);
        self.frame(&mut ui);
        self.release(pos);
        self.frame(&mut ui)
    }

    /// Screen rectangle of an open area or popup, if it was shown.
    pub fn area_rect(&self, id: egui::Id) -> Option<Rect> {
        self.ctx.memory(|m| m.area_rect(id))
    }

    /// Every text run painted in the last frame, in paint order, with its
    /// top-left position (tooltips and popups included).
    pub fn painted_text(&self) -> Vec<(String, Pos2)> {
        fn collect(shape: &egui::Shape, out: &mut Vec<(String, Pos2)>) {
            match shape {
                egui::Shape::Text(text) => out.push((text.galley.text().to_owned(), text.pos)),
                egui::Shape::Vec(shapes) => shapes.iter().for_each(|s| collect(s, out)),
                _ => {}
            }
        }
        let mut out = Vec::new();
        for clipped in &self.shapes {
            collect(&clipped.shape, &mut out);
        }
        out
    }

    /// Turns on the accessibility tree, as an assistive technology would.
    pub fn enable_accessibility(&mut self) {
        self.ctx.enable_accesskit();
    }

    /// The accessibility node whose name is `label`, from the last frame.
    pub fn accessible(&self, label: &str) -> Option<&egui::accesskit::Node> {
        self.accessibility.as_ref()?.nodes.iter().map(|(_, node)| node).find(|node| node.label() == Some(label))
    }

    /// Every named accessibility node of the last frame: `(role, name)`.
    pub fn accessible_names(&self) -> Vec<(egui::accesskit::Role, String)> {
        self.accessibility
            .as_ref()
            .map(|update| update.nodes.iter().filter_map(|(_, node)| node.label().map(|label| (node.role(), label.to_owned()))).collect())
            .unwrap_or_default()
    }

    /// Screen rectangle of the first painted text run containing `needle`.
    pub fn text_rect(&self, needle: &str) -> Option<Rect> {
        fn find(shape: &egui::Shape, needle: &str) -> Option<Rect> {
            match shape {
                egui::Shape::Text(text) if text.galley.text().contains(needle) => Some(Rect::from_min_size(text.pos, text.galley.size())),
                egui::Shape::Vec(shapes) => shapes.iter().find_map(|s| find(s, needle)),
                _ => None,
            }
        }
        self.shapes.iter().find_map(|clipped| find(&clipped.shape, needle))
    }

    /// Thin vertical marks (a text caret is one) painted inside `within`,
    /// as rectangles spanning their height, left to right.
    pub fn thin_vertical_marks(&self, within: Rect) -> Vec<Rect> {
        fn collect(shape: &egui::Shape, within: Rect, out: &mut Vec<Rect>) {
            match shape {
                egui::Shape::LineSegment { points, .. } => {
                    let [a, b] = *points;
                    if (a.x - b.x).abs() < 0.5 && (a.y - b.y).abs() >= 6.0 && within.contains(a) && within.contains(b) {
                        out.push(Rect::from_two_pos(a, b));
                    }
                }
                egui::Shape::Rect(rect) => {
                    let r = rect.rect;
                    if r.width() <= 2.5 && r.height() >= 6.0 && within.contains_rect(r) {
                        out.push(r);
                    }
                }
                egui::Shape::Vec(shapes) => shapes.iter().for_each(|s| collect(s, within, out)),
                _ => {}
            }
        }
        let mut out = Vec::new();
        for clipped in &self.shapes {
            collect(&clipped.shape, within, &mut out);
        }
        out.sort_by(|a, b| a.left().total_cmp(&b.left()));
        out
    }

    /// Position of the first painted text run containing `needle`.
    pub fn find_text(&self, needle: &str) -> Option<Pos2> {
        self.painted_text().into_iter().find(|(text, _)| text.contains(needle)).map(|(_, pos)| pos)
    }
}
