//! The wide identity in the title bar (`BrandWordmark`): the packaged vector
//! artwork, fitted by its painted bounds (not the SVG canvas) with a uniform
//! scale and centred, rasterised at the display's pixel density.

use eframe::egui::{self, Color32, ColorImage, Pos2, Rect, TextureHandle, Vec2};

const SVG: &[u8] = include_bytes!("../../assets/branding/wordmark.svg");
/// The SVG canvas size (`viewBox` width and height).
const CANVAS: Vec2 = Vec2::new(1230.0, 320.0);

/// Painted bounds in canvas units, found once from a high-resolution render.
fn painted_bounds() -> Rect {
    static BOUNDS: std::sync::OnceLock<Rect> = std::sync::OnceLock::new();
    *BOUNDS.get_or_init(|| {
        let Ok(image) = egui_extras::image::load_svg_bytes_with_size(SVG, egui::SizeHint::Width(2460), &Default::default()) else {
            return Rect::from_min_size(Pos2::ZERO, CANVAS);
        };
        let [width, height] = image.size;
        let (mut min_x, mut min_y, mut max_x, mut max_y) = (width, height, 0usize, 0usize);
        for y in 0..height {
            for x in 0..width {
                if image.pixels[y * width + x].a() > 8 {
                    min_x = min_x.min(x);
                    min_y = min_y.min(y);
                    max_x = max_x.max(x + 1);
                    max_y = max_y.max(y + 1);
                }
            }
        }
        if max_x <= min_x || max_y <= min_y {
            return Rect::from_min_size(Pos2::ZERO, CANVAS);
        }
        let scale = CANVAS.x / width as f32;
        Rect::from_min_max(Pos2::new(min_x as f32 * scale, min_y as f32 * scale), Pos2::new(max_x as f32 * scale, max_y as f32 * scale))
    })
}

/// The rasterised, cropped artwork for one pixel density.
#[derive(Default)]
pub struct Wordmark {
    texture: Option<(TextureHandle, f32)>,
}

impl std::fmt::Debug for Wordmark {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("Wordmark")
    }
}

impl Wordmark {
    /// Paints the artwork fitted into `rect` (106 × 20 points in the title bar).
    pub fn paint(&mut self, ui: &egui::Ui, rect: Rect, ink: Color32) {
        let bounds = painted_bounds();
        let scale = (rect.width() / bounds.width()).min(rect.height() / bounds.height());
        if !scale.is_finite() || scale <= 0.0 {
            return;
        }
        let pixels_per_point = ui.ctx().pixels_per_point();
        let density = scale * pixels_per_point;
        let current = self.texture.as_ref().is_some_and(|(_, d)| (d - density).abs() < 1e-3);
        if !current {
            let width = (CANVAS.x * density).round().max(1.0) as u32;
            if let Ok(full) = egui_extras::image::load_svg_bytes_with_size(SVG, egui::SizeHint::Width(width), &Default::default()) {
                let [full_width, full_height] = full.size;
                let factor = full_width as f32 / CANVAS.x;
                let x0 = ((bounds.left() * factor).floor() as usize).min(full_width);
                let y0 = ((bounds.top() * factor).floor() as usize).min(full_height);
                let x1 = ((bounds.right() * factor).ceil() as usize).clamp(x0 + 1, full_width);
                let y1 = ((bounds.bottom() * factor).ceil() as usize).clamp(y0 + 1, full_height);
                let mut pixels = Vec::with_capacity((x1 - x0) * (y1 - y0));
                for y in y0..y1 {
                    pixels.extend_from_slice(&full.pixels[y * full_width + x0..y * full_width + x1]);
                }
                let cropped = ColorImage::new([x1 - x0, y1 - y0], pixels);
                let texture = ui.ctx().load_texture("vinemix-wordmark", cropped, egui::TextureOptions::LINEAR);
                self.texture = Some((texture, density));
            }
        }
        let Some((texture, _)) = &self.texture else { return };
        let size = bounds.size() * scale;
        // Snap to whole pixels so the raster maps 1:1 onto the display.
        let min = rect.center() - size / 2.0;
        let min = Pos2::new((min.x * pixels_per_point).round() / pixels_per_point, (min.y * pixels_per_point).round() / pixels_per_point);
        let [w, h] = texture.size();
        let painted = Rect::from_min_size(min, Vec2::new(w as f32, h as f32) / pixels_per_point);
        ui.painter().image(texture.id(), painted, Rect::from_min_max(Pos2::ZERO, Pos2::new(1.0, 1.0)), ink);
    }
}
