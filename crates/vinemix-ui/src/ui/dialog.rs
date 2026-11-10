//! Workspace information windows (`WorkspaceDialog`): About, Credits and
//! licences, Updates and What's new.
//!
//! 0.5 shows these as small owned windows: square corners, a 1 px frame and
//! the system shadow, centred on the workspace, which stays undimmed. A click
//! on the workspace or Escape dismisses them (when allowed) and the click is
//! consumed. Content windows size to their content; Credits and What's new
//! have a fixed height with a scrolling body. Every window has a 24 px margin
//! and a footer: a quiet Close on the left and its actions on the right.

use eframe::egui::{self, text::LayoutJob, Color32, Id, Key, Pos2, Rect, Sense, Stroke, TextFormat, Ui, UiBuilder, Vec2};

use super::theme::{self, Weight};
use super::widgets::{self, ButtonStyle, Tip as _};

/// Margin inside the window (`StackPanel.Margin = 24`).
pub const MARGIN: f32 = 24.0;
/// Footer row height (`MinHeight = 34`).
pub const FOOTER: f32 = 34.0;

#[derive(Clone, Copy, Debug)]
pub struct Spec<'a> {
    pub id: &'a str,
    pub width: f32,
    /// A fixed window height (Credits, What's new); `None` sizes to content.
    pub height: Option<f32>,
    /// Whether Close, Escape and workspace clicks may dismiss the window.
    pub can_close: bool,
    /// Space above the footer row.
    pub footer_gap: f32,
    /// Inside margin (24; the quit recovery window uses 22).
    pub margin: f32,
    /// Whether the window has the Close footer.
    pub footer: bool,
}

impl<'a> Spec<'a> {
    /// A workspace information window: 24 px margin and a Close footer.
    pub fn new(id: &'a str, width: f32) -> Self {
        Self { id, width, height: None, can_close: true, footer_gap: 0.0, margin: MARGIN, footer: true }
    }
}

/// Shows the window. `content` receives a top-down UI without item spacing;
/// for a fixed-height window its max rect ends above the footer. `actions`
/// receives a right-to-left UI for the footer buttons, in reverse visual
/// order. Returns true when the window was dismissed.
pub fn show(ui: &mut Ui, area: Rect, spec: Spec<'_>, content: impl FnOnce(&mut Ui), actions: impl FnOnce(&mut Ui)) -> bool {
    let ctx = ui.ctx().clone();
    let key = Id::unique(("vinemix-dialog-height", spec.id));
    let measured = ctx.data(|d| d.get_temp::<f32>(key));
    let width = spec.width.min(area.width() - 16.0).max(1.0);
    let height = spec.height.or(measured).unwrap_or(240.0).min(area.height() - 16.0).max(1.0);
    let ppp = ctx.pixels_per_point();
    let snap = |v: f32| (v * ppp).round() / ppp;
    let min = area.center() - Vec2::new(width, height) / 2.0;
    let rect = Rect::from_min_size(Pos2::new(snap(min.x), snap(min.y)), Vec2::new(width, height));

    // Owner clicks dismiss and are consumed (registered first, so the window's
    // own widgets above it win their hit tests).
    let owner = ui.interact(area, Id::unique(("vinemix-dialog-owner", spec.id)), Sense::click());
    let outside = owner.clicked() && owner.interact_pointer_pos().is_some_and(|p| !rect.contains(p));
    // The window surface swallows clicks that miss its controls.
    let _ = ui.interact(rect, Id::unique(("vinemix-dialog-surface", spec.id)), Sense::click());

    let painter = ui.painter().clone();
    painter.add(egui::epaint::Shadow { offset: [0, 6], blur: 22, spread: 0, color: Color32::from_black_alpha(130) }.as_shape(rect, 0));
    painter.rect(rect, 0.0, theme::WINDOW, Stroke::new(1.0, theme::BORDER), egui::StrokeKind::Inside);

    let inner = rect.shrink(spec.margin);
    let fixed = spec.height.is_some();
    let content_rect = if fixed {
        Rect::from_min_max(inner.min, Pos2::new(inner.right(), (inner.bottom() - FOOTER - spec.footer_gap).max(inner.top())))
    } else {
        Rect::from_min_size(inner.min, Vec2::new(inner.width(), area.height().max(inner.height())))
    };
    let mut builder = UiBuilder::new().max_rect(content_rect).layout(egui::Layout::top_down(egui::Align::Min));
    if !fixed && measured.is_none() {
        builder = builder.sizing_pass();
    }
    let mut body = ui.new_child(builder);
    body.spacing_mut().item_spacing = Vec2::ZERO;
    body.set_clip_rect(rect.intersect(ui.clip_rect()));
    content(&mut body);
    let footer_height = if spec.footer { FOOTER } else { 0.0 };
    let footer_top = if fixed { inner.bottom() - footer_height } else { body.min_rect().bottom() + spec.footer_gap };
    let footer = Rect::from_min_size(Pos2::new(inner.left(), footer_top), Vec2::new(inner.width(), footer_height));
    let mut close = false;
    if spec.footer {
        let mut row = ui.new_child(UiBuilder::new().max_rect(footer).layout(egui::Layout::right_to_left(egui::Align::Center)));
        row.spacing_mut().item_spacing = Vec2::new(8.0, 0.0);
        row.set_clip_rect(rect.intersect(ui.clip_rect()));
        if !fixed && measured.is_none() {
            row.set_invisible();
        }
        actions(&mut row);
        close = close_button(ui, Id::unique(("vinemix-dialog-close", spec.id)), footer.left_top(), spec.can_close);
    }

    if !fixed {
        let wanted = (footer.bottom() + spec.margin - rect.top()).ceil();
        if measured.is_none_or(|h| (h - wanted).abs() > 0.5) {
            ctx.data_mut(|d| d.insert_temp(key, wanted));
            ctx.request_discard("dialog sizing");
        }
    }
    let escape = ctx.input(|i| i.key_pressed(Key::Escape));
    spec.can_close && (close || outside || escape)
}

/// Forgets a window's measured height (call when its content changes size).
pub fn invalidate(ctx: &egui::Context, id: &str) {
    ctx.data_mut(|d| d.remove::<f32>(Id::unique(("vinemix-dialog-height", id))));
}

/// `WorkspaceDialog.CloseButton`: muted text, transparent face, 10 px
/// horizontal padding, 34 px high, hover fill.
fn close_button(ui: &mut Ui, id: Id, top_left: Pos2, enabled: bool) -> bool {
    let width = widgets::text_width(ui.ctx(), "Close", theme::font(13.0, Weight::Regular)).ceil() + 20.0;
    let rect = Rect::from_min_size(top_left, Vec2::new(width, FOOTER));
    let style = ButtonStyle { fill: Color32::TRANSPARENT, text: theme::MUTED, border: Color32::TRANSPARENT, border_width: 0.0, ..ButtonStyle::console(13.0) };
    widgets::button(ui, id, rect, "Close", style, enabled, 1.0).tip_described("Close this window.", "Close this window. You can also press Escape or click the workspace.").clicked()
}

/// A dialog button sized to its text plus `padding` per side, `height` high.
pub fn button(ui: &mut Ui, label: &str, primary: bool, enabled: bool, padding: f32, height: f32) -> egui::Response {
    let width = widgets::text_width(ui.ctx(), label, theme::font(13.0, Weight::Regular)).ceil() + padding * 2.0;
    let (rect, _) = ui.allocate_exact_size(Vec2::new(width, height), Sense::hover());
    let style = if primary { ButtonStyle::primary(13.0) } else { ButtonStyle::console(13.0) };
    widgets::button(ui, Id::unique(("dialog-button", label)), rect, label, style, enabled, 1.0)
}

/// A full-width button with left-aligned text (`HorizontalContentAlignment =
/// Left`, 12 px padding), `height` high.
pub fn wide_button(ui: &mut Ui, label: &str, text: Color32, height: f32) -> egui::Response {
    let (rect, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), height), Sense::hover());
    let response = widgets::button(ui, Id::unique(("dialog-wide", label)), rect, "", ButtonStyle::console(13.0), true, 1.0);
    ui.painter().text(egui::pos2(rect.left() + 12.0, rect.center().y), egui::Align2::LEFT_CENTER, label, theme::font(13.0, Weight::Regular), text);
    response
}

/// Wrapped text with an optional fixed line height (`TextBlock.LineHeight`).
pub fn text(ui: &mut Ui, text: &str, size: f32, weight: Weight, color: Color32, line_height: Option<f32>) -> egui::Response {
    let mut job = LayoutJob::single_section(text.to_owned(), TextFormat { font_id: theme::font(size, weight), color, line_height, ..Default::default() });
    job.wrap.max_width = ui.available_width();
    ui.add(egui::Label::new(job).wrap())
}

/// Selectable wrapped text (read-only `TextBox`).
pub fn selectable(ui: &mut Ui, text: &str, size: f32, color: Color32) -> egui::Response {
    let mut job = LayoutJob::single_section(text.to_owned(), TextFormat { font_id: theme::font(size, Weight::Regular), color, ..Default::default() });
    job.wrap.max_width = ui.available_width();
    ui.add(egui::Label::new(job).wrap().selectable(true))
}

/// The 22 px SemiBold window heading.
pub fn heading(ui: &mut Ui, title: &str) {
    text(ui, title, 22.0, Weight::SemiBold, theme::TEXT, None);
}

/// A 3 px indeterminate progress bar (`ProgressBar.IsIndeterminate`).
pub fn indeterminate(ui: &mut Ui) {
    let (rect, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), 3.0), Sense::hover());
    let painter = ui.painter();
    painter.rect_filled(rect, 0.0, theme::BORDER);
    let time = ui.input(|i| i.time) as f32;
    let span = rect.width() * 0.25;
    let phase = (time / 1.6).fract();
    let left = rect.left() - span + (rect.width() + span) * phase;
    let bar = Rect::from_min_max(Pos2::new(left.max(rect.left()), rect.top()), Pos2::new((left + span).min(rect.right()), rect.bottom()));
    if bar.width() > 0.0 {
        painter.rect_filled(bar, 0.0, theme::A);
    }
    ui.ctx().request_repaint();
}

/// The 0.5 `ScrollBar` look for read-only text: a 17 px lane with a square
/// 13 px grip (control border, muted on hover) and no track. Apply to a UI
/// that holds only text, since it restyles the widget states it contains.
pub fn text_scroll_style(ui: &mut Ui) {
    let style = ui.style_mut();
    style.spacing.scroll = egui::style::ScrollStyle {
        bar_width: 13.0,
        bar_inner_margin: 2.0,
        bar_outer_margin: 2.0,
        handle_min_length: 24.0,
        ..egui::style::ScrollStyle::solid()
    };
    style.visuals.extreme_bg_color = Color32::TRANSPARENT;
    let widgets = &mut style.visuals.widgets;
    widgets.inactive.bg_fill = theme::CONTROL_BORDER;
    widgets.hovered.bg_fill = theme::MUTED;
    widgets.active.bg_fill = theme::MUTED;
    for state in [&mut widgets.inactive, &mut widgets.hovered, &mut widgets.active] {
        state.corner_radius = egui::CornerRadius::ZERO;
    }
}
