//! The 0.5 console palette (`Theme.xaml`, `ConsoleTheme`), IBM Plex fonts
//! and egui visuals.

use std::sync::Arc;

use eframe::egui::{self, Color32, FontData, FontDefinitions, FontFamily, FontId};

const fn hex(rgb: u32) -> Color32 {
    Color32::from_rgb((rgb >> 16) as u8, (rgb >> 8) as u8, rgb as u8)
}

pub const BRAND_ACTION: Color32 = hex(0x2D6A4F);
pub const BRAND_ACTION_TEXT: Color32 = hex(0xF5F8F6);
pub const BRAND_HOVER: Color32 = hex(0x347B5B);
pub const WINDOW: Color32 = hex(0x0C0D0F);
pub const PANEL: Color32 = hex(0x15181E);
pub const TEXT: Color32 = hex(0xE8EBF2);
pub const MUTED: Color32 = hex(0xA5ADBA);
pub const BORDER: Color32 = hex(0x303744);
pub const CONTROL_BORDER: Color32 = hex(0x343D49);
pub const WELL: Color32 = hex(0x080A0C);
pub const BUTTON_FACE: Color32 = hex(0x242A32);
pub const BUTTON_HOVER: Color32 = hex(0x303B40);
pub const HEADER_BAND: Color32 = hex(0x171A1E);
pub const A: Color32 = hex(0x78BE98);
pub const MUTE: Color32 = hex(0xD57370);
pub const ALERT_TEXT: Color32 = hex(0xF1AAA4);
pub const FOCUS: Color32 = hex(0xDCE5EF);
pub const INK: Color32 = hex(0x08110D);
pub const METER_WELL: Color32 = hex(0x050607);
pub const METER_UNLIT: Color32 = hex(0x20262B);
pub const METER_LOW: Color32 = hex(0x65B88B);
pub const METER_MID: Color32 = hex(0xE6BB5A);
pub const METER_HIGH: Color32 = hex(0xE56F70);
pub const CANVAS: Color32 = hex(0x000000);
pub const GRID: Color32 = hex(0x1B1F23);
pub const NODE_OUTLINE: Color32 = hex(0x30363D);
pub const NODE_SELECTED: Color32 = hex(0xDCE5EF);
pub const CANVAS_OVERVIEW: Color32 = hex(0x4A5058);
pub const NODE_AUDIO: Color32 = hex(0x78BE98);
pub const NODE_CONTROL: Color32 = hex(0xA0A8B3);
pub const NODE_SIDECHAIN: Color32 = hex(0xB8A0D8);
pub const NODE_EFFECT_RETURN: Color32 = hex(0x8EC8E8);
pub const HEADER_INPUT: Color32 = hex(0x583747);
pub const HEADER_OUTPUT: Color32 = hex(0x4A2D34);
pub const HEADER_PROCESSOR: Color32 = hex(0x40365B);
pub const HEADER_UTILITY: Color32 = hex(0x294650);
pub const HEADER_CONTROLLER: Color32 = hex(0x374D3C);

pub fn header_color(header: crate::graph::catalog::Header) -> Color32 {
    use crate::graph::catalog::Header;
    match header {
        Header::Input => HEADER_INPUT,
        Header::Output => HEADER_OUTPUT,
        Header::Processor => HEADER_PROCESSOR,
        Header::Utility => HEADER_UTILITY,
        Header::Controller => HEADER_CONTROLLER,
    }
}

/// Socket colour (`SocketBrushKey`).
pub fn socket_color(kind: &str, output: bool, port: &str, node_kind: Option<&str>) -> Color32 {
    match kind {
        "control" => NODE_CONTROL,
        "sidechain" => NODE_SIDECHAIN,
        _ if output && port == "wet" && matches!(node_kind, Some("reverb" | "echo")) => NODE_EFFECT_RETURN,
        _ => NODE_AUDIO,
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Weight {
    Regular,
    Medium,
    SemiBold,
    Mono,
}

pub fn family(weight: Weight) -> FontFamily {
    match weight {
        Weight::Regular => FontFamily::Proportional,
        Weight::Medium => FontFamily::Name("plex-medium".into()),
        Weight::SemiBold => FontFamily::Name("plex-semibold".into()),
        Weight::Mono => FontFamily::Monospace,
    }
}

/// A font at `size` points. Sizes are quantised to quarter points so a
/// continuously zoomed canvas reuses a bounded set of glyph rasterisations.
pub fn font(size: f32, weight: Weight) -> FontId {
    FontId::new((size * 4.0).round().max(4.0) / 4.0, family(weight))
}

pub fn install(ctx: &egui::Context) {
    let mut fonts = FontDefinitions::default();
    let regular = include_bytes!("../../assets/fonts/IBM-Plex-Sans-Regular.ttf");
    let medium = include_bytes!("../../assets/fonts/IBM-Plex-Sans-Medium.ttf");
    let semibold = include_bytes!("../../assets/fonts/IBM-Plex-Sans-SemiBold.ttf");
    let mono = include_bytes!("../../assets/fonts/IBM-Plex-Mono-Regular.ttf");
    fonts.font_data.insert("plex".into(), Arc::new(FontData::from_static(regular)));
    fonts.font_data.insert("plex-medium".into(), Arc::new(FontData::from_static(medium)));
    fonts.font_data.insert("plex-semibold".into(), Arc::new(FontData::from_static(semibold)));
    fonts.font_data.insert("plex-mono".into(), Arc::new(FontData::from_static(mono)));
    // Keep egui's bundled fonts as fallbacks for symbols Plex lacks.
    let fallbacks: Vec<String> = fonts.families.get(&FontFamily::Proportional).cloned().unwrap_or_default();
    let with = |primary: &str| -> Vec<String> {
        let mut list = vec![primary.to_owned()];
        list.extend(fallbacks.iter().cloned());
        list
    };
    fonts.families.insert(FontFamily::Proportional, with("plex"));
    fonts.families.insert(FontFamily::Name("plex-medium".into()), with("plex-medium"));
    fonts.families.insert(FontFamily::Name("plex-semibold".into()), with("plex-semibold"));
    let mut monospace = vec!["plex-mono".to_owned()];
    monospace.extend(fallbacks.iter().cloned());
    fonts.families.insert(FontFamily::Monospace, monospace);
    ctx.set_fonts(fonts);

    ctx.global_style_mut(|style| {
        use egui::{FontId as F, TextStyle};
        style.text_styles = [
            (TextStyle::Small, F::new(11.0, FontFamily::Proportional)),
            (TextStyle::Body, F::new(13.0, FontFamily::Proportional)),
            (TextStyle::Button, F::new(13.0, FontFamily::Proportional)),
            (TextStyle::Heading, F::new(16.0, family(Weight::SemiBold))),
            (TextStyle::Monospace, F::new(12.0, FontFamily::Monospace)),
        ]
        .into();
        style.spacing.item_spacing = egui::vec2(8.0, 6.0);
        style.spacing.button_padding = egui::vec2(9.0, 4.0);
        style.spacing.interact_size.y = 26.0;
        // Popups and tooltips use the `ToolTip` style: 12,9 padding.
        style.spacing.menu_margin = egui::Margin::symmetric(12, 9);
        style.interaction.tooltip_delay = 0.4;
        // Popups size to their content; the window still constrains them.
        style.spacing.default_area_size = egui::vec2(4096.0, 4096.0);
        style.interaction.show_tooltips_only_when_still = false;
        let visuals = &mut style.visuals;
        *visuals = egui::Visuals::dark();
        visuals.override_text_color = Some(TEXT);
        visuals.panel_fill = WINDOW;
        visuals.window_fill = WELL;
        visuals.window_stroke = egui::Stroke::new(1.0, CONTROL_BORDER);
        visuals.popup_shadow = egui::Shadow::NONE;
        visuals.extreme_bg_color = WELL;
        visuals.faint_bg_color = PANEL;
        visuals.code_bg_color = WELL;
        visuals.selection.bg_fill = A.gamma_multiply(0.35);
        visuals.selection.stroke = egui::Stroke::new(1.0, FOCUS);
        visuals.hyperlink_color = A;
        visuals.warn_fg_color = METER_MID;
        visuals.error_fg_color = ALERT_TEXT;
        visuals.menu_corner_radius = egui::CornerRadius::same(2);
        visuals.window_corner_radius = egui::CornerRadius::same(4);
        let widgets = &mut visuals.widgets;
        widgets.noninteractive.bg_fill = PANEL;
        widgets.noninteractive.weak_bg_fill = PANEL;
        widgets.noninteractive.bg_stroke = egui::Stroke::new(1.0, BORDER);
        widgets.noninteractive.fg_stroke = egui::Stroke::new(1.0, TEXT);
        for state in [&mut widgets.inactive, &mut widgets.hovered, &mut widgets.active, &mut widgets.open] {
            state.corner_radius = egui::CornerRadius::same(3);
            state.fg_stroke = egui::Stroke::new(1.0, TEXT);
            state.bg_stroke = egui::Stroke::new(1.0, CONTROL_BORDER);
            state.expansion = 0.0;
        }
        widgets.inactive.weak_bg_fill = BUTTON_FACE;
        widgets.inactive.bg_fill = BUTTON_FACE;
        widgets.hovered.weak_bg_fill = BUTTON_HOVER;
        widgets.hovered.bg_fill = BUTTON_HOVER;
        widgets.hovered.bg_stroke = egui::Stroke::new(1.0, MUTED);
        widgets.active.weak_bg_fill = WELL;
        widgets.active.bg_fill = WELL;
        widgets.open.weak_bg_fill = WELL;
        widgets.open.bg_fill = WELL;
    });
}

/// Linear interpolation in sRGB space (WPF `SRgbLinearInterpolation`).
pub fn mix(a: Color32, b: Color32, t: f32) -> Color32 {
    let t = t.clamp(0.0, 1.0);
    let lerp = |x: u8, y: u8| (f32::from(x) + (f32::from(y) - f32::from(x)) * t).round() as u8;
    Color32::from_rgba_unmultiplied(lerp(a.r(), b.r()), lerp(a.g(), b.g()), lerp(a.b(), b.b()), lerp(a.a(), b.a()))
}

pub fn with_opacity(color: Color32, opacity: f32) -> Color32 {
    color.gamma_multiply(opacity.clamp(0.0, 1.0))
}
