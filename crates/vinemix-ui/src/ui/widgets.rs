//! Shared painted widgets matching the 0.5 control templates (`App.xaml`):
//! console buttons, selectors with dropdowns and elided text.

use eframe::egui::{self, Align2, Color32, FontId, Id, Key, Pos2, Rect, Response, Sense, Stroke, Ui, Vec2};

use super::theme::{self, Weight};

/// Paints single-line text, elided with `…` to `max_width`.
pub fn text_elided(painter: &egui::Painter, pos: Pos2, align: Align2, text: &str, font: FontId, color: Color32, max_width: f32) -> Rect {
    let mut job = egui::text::LayoutJob::single_section(text.to_owned(), egui::TextFormat::simple(font, color));
    job.wrap = egui::text::TextWrapping { max_width: max_width.max(1.0), max_rows: 1, break_anywhere: true, overflow_character: Some('…') };
    let galley = painter.layout_job(job);
    let rect = align.anchor_size(pos, galley.size());
    painter.galley(rect.min, galley, color);
    rect
}

/// Paints wrapped text in `rect.width()`, returning its height.
pub fn text_wrapped(painter: &egui::Painter, rect: Rect, text: &str, font: FontId, color: Color32, align: egui::Align) -> f32 {
    let mut job = egui::text::LayoutJob::single_section(text.to_owned(), egui::TextFormat::simple(font, color));
    job.wrap.max_width = rect.width().max(1.0);
    job.halign = align;
    let galley = painter.layout_job(job);
    let x = match align {
        egui::Align::Min => rect.left(),
        egui::Align::Center => rect.center().x,
        egui::Align::Max => rect.right(),
    };
    let height = galley.size().y;
    painter.galley(Pos2::new(x, rect.top()), galley, color);
    height
}

/// Height of wrapped text at `size` points in `width`.
/// A circle whose pen is centred on `radius`, like WPF's `DrawEllipse`.
/// (egui tessellates circle strokes outside the radius.)
pub fn circle(painter: &egui::Painter, center: Pos2, radius: f32, fill: Color32, stroke: Stroke) {
    painter.circle(center, (radius - stroke.width / 2.0).max(0.0), fill, stroke);
}

/// The single-line width of `text`, for auto-sized buttons (WPF measures
/// content and adds the horizontal padding).
pub fn text_width(ctx: &egui::Context, text: &str, font: FontId) -> f32 {
    ctx.fonts_mut(|fonts| fonts.layout_no_wrap(text.to_owned(), font, Color32::WHITE).size().x)
}

pub fn wrapped_height(ctx: &egui::Context, text: &str, font: FontId, width: f32) -> f32 {
    ctx.fonts_mut(|fonts| fonts.layout(text.to_owned(), font, Color32::WHITE, width.max(1.0)).size().y)
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ButtonStyle {
    pub fill: Color32,
    pub text: Color32,
    pub border: Color32,
    pub border_width: f32,
    pub font_size: f32,
    pub weight: Weight,
    /// Keep the fill on hover (state buttons) and only brighten the border.
    pub keep_fill: bool,
    /// `WorkspaceToolbarButton`: transparent square face, hover fill only.
    pub flat: bool,
}

impl ButtonStyle {
    pub fn console(font_size: f32) -> Self {
        Self {
            fill: theme::BUTTON_FACE,
            text: theme::TEXT,
            border: theme::CONTROL_BORDER,
            border_width: 1.0,
            font_size,
            weight: Weight::Regular,
            keep_fill: false,
            flat: false,
        }
    }

    /// `WorkspaceToolbarButton` (title bar, toolbar, window buttons).
    pub fn toolbar(font_size: f32) -> Self {
        Self { fill: Color32::TRANSPARENT, border: Color32::TRANSPARENT, border_width: 0.0, flat: true, ..Self::console(font_size) }
    }

    /// `PrimaryWorkspaceToolbarButton` (Add).
    pub fn toolbar_primary(font_size: f32) -> Self {
        Self { border: Color32::TRANSPARENT, flat: true, ..Self::primary(font_size) }
    }

    pub fn primary(font_size: f32) -> Self {
        Self { fill: theme::BRAND_ACTION, text: theme::BRAND_ACTION_TEXT, border: theme::BRAND_ACTION, ..Self::console(font_size) }
    }

    pub fn state(on: bool, on_fill: Color32, on_text: Color32, font_size: f32) -> Self {
        if on {
            Self { fill: on_fill, text: on_text, border: on_fill, keep_fill: true, ..Self::console(font_size) }
        } else {
            Self { keep_fill: true, ..Self::console(font_size) }
        }
    }
}

/// A console button in `rect` (screen space). `scale` scales strokes,
/// corner radius and font.
pub fn button(ui: &mut Ui, id: Id, rect: Rect, label: &str, style: ButtonStyle, enabled: bool, scale: f32) -> Response {
    let response = ui.interact(rect, id, if enabled { Sense::click() } else { Sense::hover() });
    // Screen readers (`AutomationProperties.Name`); icon buttons label themselves.
    if !label.is_empty() {
        response.widget_info(|| egui::WidgetInfo::labeled(egui::accesskit::Role::Button, enabled, label));
    }
    if !ui.is_rect_visible(rect) {
        return response;
    }
    let mut fill = style.fill;
    let mut border = style.border;
    let primary = style.fill == theme::BRAND_ACTION;
    if style.flat {
        if enabled && response.is_pointer_button_down_on() {
            if primary {
                border = theme::BRAND_ACTION_TEXT;
            } else {
                fill = theme::CONTROL_BORDER;
            }
        } else if enabled && response.hovered() {
            fill = if primary { theme::BRAND_HOVER } else { theme::BUTTON_HOVER };
        }
    } else if enabled && response.is_pointer_button_down_on() {
        if style.keep_fill {
            fill = fill.gamma_multiply(0.8);
        } else {
            fill = theme::WELL;
        }
    } else if enabled && response.hovered() {
        if style.keep_fill {
            border = theme::TEXT;
        } else if style.fill == theme::BRAND_ACTION {
            fill = theme::BRAND_HOVER;
        } else {
            fill = theme::BUTTON_HOVER;
        }
    }
    let opacity = match (enabled, style.flat) {
        (true, _) => 1.0,
        (false, true) => 0.4,
        (false, false) => 0.55,
    };
    let painter = ui.painter();
    let radius = if style.flat && !primary { 0.0 } else { 3.0 * scale };
    let border_width = if style.flat && primary { 1.0 } else { style.border_width };
    painter.rect(
        rect,
        radius,
        theme::with_opacity(fill, opacity),
        Stroke::new(border_width * scale, theme::with_opacity(border, opacity)),
        egui::StrokeKind::Inside,
    );
    text_elided(
        painter,
        rect.center(),
        Align2::CENTER_CENTER,
        label,
        theme::font(style.font_size * scale, style.weight),
        theme::with_opacity(style.text, opacity),
        rect.width() - 6.0 * scale,
    );
    if response.has_focus() {
        painter.rect_stroke(rect.shrink(2.0 * scale), 2.0 * scale, Stroke::new(scale.max(1.0), theme::FOCUS), egui::StrokeKind::Inside);
    }
    if enabled && response.hovered() {
        ui.ctx().set_cursor_icon(egui::CursorIcon::PointingHand);
    }
    response
}

/// A child `Ui` exactly covering `rect` for an inline text field on the
/// canvas: the theme's minimum interactive height is lifted so the field
/// fits the scaled box at every zoom.
pub fn field_ui(ui: &mut Ui, rect: Rect) -> Ui {
    let mut child = ui.new_child(egui::UiBuilder::new().max_rect(rect));
    child.spacing_mut().interact_size = Vec2::ZERO;
    child
}

/// One dropdown entry.
#[derive(Clone, Debug, PartialEq)]
pub struct Choice {
    /// Stable identity of the entry (endpoint ID, process identity, fixed
    /// option name). An open list tracks its highlight by this key, never by
    /// position: live inventories shrink and reorder between frames.
    pub key: String,
    pub label: String,
    pub enabled: bool,
    pub help: String,
}

impl Choice {
    /// An always-enabled entry of a fixed list, identified by its label.
    pub fn fixed(label: &str) -> Self {
        Self { key: label.to_owned(), label: label.to_owned(), enabled: true, help: String::new() }
    }
}

/// The entry an open list highlights: the remembered key if that entry still
/// exists, otherwise the current selection (if it is in range).
fn resolve_highlight(choices: &[Choice], key: Option<&str>, selected: Option<usize>) -> Option<usize> {
    key.and_then(|key| choices.iter().position(|c| c.key == key)).or(selected.filter(|&i| i < choices.len()))
}

/// The next enabled entry from `from` (or from the matching end), skipping
/// disabled entries; `None` when there is none in that direction.
fn next_enabled(choices: &[Choice], from: Option<usize>, forward: bool) -> Option<usize> {
    let count = choices.len();
    let mut index = from.filter(|&i| i < count);
    for _ in 0..count {
        let candidate = match (index, forward) {
            (None, true) => 0,
            (None, false) => count - 1,
            (Some(i), true) => (i + 1).min(count - 1),
            (Some(i), false) => i.saturating_sub(1),
        };
        if Some(candidate) == index {
            return None;
        }
        if choices[candidate].enabled {
            return Some(candidate);
        }
        index = Some(candidate);
    }
    None
}

/// Width of an open list: at least the selector face at the current zoom,
/// widened to the longest label at the fixed 13 pt list size so entries stay
/// readable when the canvas is zoomed out, and never wider than the window.
fn popup_width(ui: &Ui, face_width: f32, choices: &[Choice]) -> f32 {
    let widest = choices
        .iter()
        .map(|c| ui.fonts_mut(|f| f.layout_no_wrap(c.label.clone(), theme::font(13.0, Weight::Regular), theme::TEXT).size().x))
        .fold(0.0f32, f32::max);
    let limit = (ui.ctx().content_rect().width() - 8.0).max(64.0);
    face_width.max(widest + 16.0 + 4.0 + 2.0).min(limit)
}

/// A selector face with a dropdown (`ComboBox` template). Returns the index
/// the user picked this frame, if any. `text_color` lets availability
/// warnings recolour only the selected text.
#[allow(clippy::too_many_arguments)]
pub fn selector(
    ui: &mut Ui,
    id: Id,
    rect: Rect,
    selected: Option<usize>,
    selected_text: &str,
    choices: &[Choice],
    enabled: bool,
    scale: f32,
    text_color: Color32,
    help: &str,
) -> (Response, Option<usize>) {
    let response = ui.interact(rect, id, if enabled { Sense::click() } else { Sense::hover() });
    response.widget_info(|| egui::WidgetInfo {
        current_text_value: Some(selected_text.to_owned()),
        ..egui::WidgetInfo::labeled(egui::accesskit::Role::ComboBox, enabled, help.lines().next().unwrap_or(""))
    });
    let popup_id = id.with("popup");
    let open = egui::Popup::is_id_open(ui.ctx(), popup_id);
    let opacity = if enabled { 1.0 } else { 0.5 };
    let fill = if open {
        theme::WELL
    } else if enabled && response.hovered() {
        theme::BUTTON_HOVER
    } else {
        theme::BUTTON_FACE
    };
    let painter = ui.painter();
    painter.rect(
        rect,
        3.0 * scale,
        theme::with_opacity(fill, opacity),
        Stroke::new(scale, theme::with_opacity(theme::CONTROL_BORDER, opacity)),
        egui::StrokeKind::Inside,
    );
    text_elided(
        painter,
        Pos2::new(rect.left() + 7.0 * scale, rect.center().y),
        Align2::LEFT_CENTER,
        selected_text,
        theme::font(13.0 * scale, Weight::Regular),
        theme::with_opacity(text_color, opacity),
        rect.width() - 36.0 * scale,
    );
    // Chevron.
    let c = Pos2::new(rect.right() - 12.0 * scale, rect.center().y);
    let w = 4.0 * scale;
    painter.add(egui::Shape::line(
        vec![Pos2::new(c.x - w, c.y - w * 0.5), Pos2::new(c.x, c.y + w * 0.5), Pos2::new(c.x + w, c.y - w * 0.5)],
        Stroke::new(1.2 * scale.max(0.5), theme::with_opacity(theme::TEXT, opacity)),
    ));
    if response.has_focus() || open {
        painter.rect_stroke(rect, 3.0 * scale, Stroke::new(scale.max(1.0), theme::FOCUS), egui::StrokeKind::Inside);
    }
    let mut picked = None;
    // Only pointer clicks toggle the list; Enter is handled below
    // (egui otherwise reports Enter on a focused widget as a click).
    if enabled && response.clicked_by(egui::PointerButton::Primary) {
        egui::Popup::toggle_id(ui.ctx(), popup_id);
        response.request_focus();
    }
    // Arrows (and Escape while open) act on the selector, not focus movement.
    if response.has_focus() {
        let open = egui::Popup::is_id_open(ui.ctx(), popup_id);
        ui.memory_mut(|m| {
            m.set_focus_lock_filter(id, egui::EventFilter { tab: false, horizontal_arrows: true, vertical_arrows: true, escape: open })
        });
    }
    // Keyboard (`ComboBox`): closed, Up/Down change the selection and
    // Alt+Down / Alt+Up / F4 open; open, arrows move the highlight, Enter
    // chooses and Escape closes. Disabled entries are skipped.
    //
    // The highlight is remembered by entry key and resolved against this
    // frame's list, so an inventory that shrank or reordered while the list
    // was open can neither index out of range nor point at a different entry.
    let highlight_id = id.with("highlight");
    let selected = selected.filter(|&i| i < choices.len());
    let key_of = |index: Option<usize>| index.and_then(|i| choices.get(i)).map(|c| c.key.clone());
    let remembered = |ui: &Ui| ui.data(|d| d.get_temp::<Option<String>>(highlight_id)).flatten();
    let open_now = egui::Popup::is_id_open(ui.ctx(), popup_id);
    if enabled && response.has_focus() && !choices.is_empty() {
        let (up, down, enter, escape, alt, f4, home, end) = ui.input(|i| {
            (
                i.key_pressed(Key::ArrowUp),
                i.key_pressed(Key::ArrowDown),
                i.key_pressed(Key::Enter),
                i.key_pressed(Key::Escape),
                i.modifiers.alt,
                i.key_pressed(Key::F4),
                i.key_pressed(Key::Home),
                i.key_pressed(Key::End),
            )
        });
        if open_now {
            let current = resolve_highlight(choices, remembered(ui).as_deref(), selected);
            let mut highlight = current;
            if alt && (up || down) || f4 {
                egui::Popup::close_id(ui.ctx(), popup_id);
            } else if down {
                highlight = next_enabled(choices, current, true).or(current);
            } else if up {
                highlight = next_enabled(choices, current, false).or(current);
            } else if home {
                highlight = next_enabled(choices, None, true);
            } else if end {
                highlight = next_enabled(choices, None, false);
            }
            if enter {
                if let Some(index) = highlight.filter(|&i| choices.get(i).is_some_and(|c| c.enabled)) {
                    if Some(index) != selected {
                        picked = Some(index);
                    }
                }
                egui::Popup::close_id(ui.ctx(), popup_id);
            }
            if escape {
                egui::Popup::close_id(ui.ctx(), popup_id);
                // The list closes; Escape must not also close a host dialog.
                ui.input_mut(|i| i.consume_key(egui::Modifiers::NONE, Key::Escape));
            }
            if enter {
                ui.input_mut(|i| i.consume_key(egui::Modifiers::NONE, Key::Enter));
            }
            ui.data_mut(|d| d.insert_temp(highlight_id, key_of(highlight)));
        } else if alt && (up || down) || f4 {
            ui.data_mut(|d| d.insert_temp(highlight_id, key_of(selected)));
            egui::Popup::open_id(ui.ctx(), popup_id);
        } else if up || down || home || end {
            let target = if home {
                next_enabled(choices, None, true)
            } else if end {
                next_enabled(choices, None, false)
            } else {
                next_enabled(choices, selected, down)
            };
            if let Some(index) = target.filter(|&i| Some(i) != selected) {
                picked = Some(index);
            }
        }
    }
    if !open_now && response.clicked_by(egui::PointerButton::Primary) {
        ui.data_mut(|d| d.insert_temp(highlight_id, key_of(selected)));
    }
    // List geometry is only needed while the list is open: a closed selector
    // measures nothing (there can be one per node, every frame).
    if egui::Popup::is_id_open(ui.ctx(), popup_id) {
        let highlight = remembered(ui).and_then(|key| choices.iter().position(|c| c.key == key));
        let width = popup_width(ui, rect.width(), choices);
        egui::Popup::from_response(&response)
            .id(popup_id)
            .open_memory(None)
            .close_behavior(egui::PopupCloseBehavior::CloseOnClickOutside)
            .gap(0.0)
            .width(width)
            .frame(egui::Frame::new().fill(theme::WELL).stroke(Stroke::new(1.0, theme::BORDER)).corner_radius(3).inner_margin(2))
            .show(|ui| {
                // `Popup::width` is only the area's default: an open area
                // keeps its remembered size. Pin both bounds every frame so
                // the list follows its selector when the canvas zooms out as
                // well as in (frame: 2 px margin + 1 px border per side).
                ui.set_width((width - 6.0).max(1.0));
                ui.spacing_mut().item_spacing = Vec2::ZERO;
                egui::ScrollArea::vertical().max_height(280.0).show(ui, |ui| {
                    ui.spacing_mut().item_spacing = Vec2::ZERO;
                    for (index, choice) in choices.iter().enumerate() {
                        let is_selected = selected == Some(index);
                        let (row, row_response) = ui.allocate_exact_size(egui::vec2(ui.available_width(), 28.0), Sense::click());
                        let row_response = if choice.enabled { row_response } else { ui.interact(row, row_response.id, Sense::hover()) };
                        let pointer_moved = ui.input(|i| i.pointer.delta() != Vec2::ZERO);
                        if choice.enabled && row_response.hovered() && pointer_moved {
                            ui.data_mut(|d| d.insert_temp(highlight_id, Some(choice.key.clone())));
                        }
                        let highlighted = choice.enabled && (highlight == Some(index) || (highlight.is_none() && is_selected));
                        if highlighted {
                            ui.painter().rect_filled(row, 2.0, theme::A);
                            if !ui.is_rect_visible(row.shrink(4.0)) {
                                ui.scroll_to_rect(row, None);
                            }
                        }
                        let color = if highlighted { theme::INK } else { theme::TEXT };
                        text_elided(
                            ui.painter(),
                            Pos2::new(row.left() + 8.0, row.center().y),
                            Align2::LEFT_CENTER,
                            &choice.label,
                            theme::font(13.0, Weight::Regular),
                            if choice.enabled { color } else { theme::with_opacity(color, 0.5) },
                            row.width() - 16.0,
                        );
                        let row_response = if choice.help.is_empty() { row_response } else { row_response.tip(&choice.help).disabled_tip(&choice.help) };
                        if choice.enabled && row_response.clicked() {
                            if !is_selected {
                                picked = Some(index);
                            }
                            egui::Popup::close_id(ui.ctx(), popup_id);
                        }
                    }
                });
            });
    }
    // The selector's help yields to its open list (`SubmenuOpened`).
    let response = if help.is_empty() || egui::Popup::is_id_open(ui.ctx(), popup_id) { response } else { response.tip(help) };
    (response, picked)
}

/// Checkbox-style toggle row for menus and dialogs.
pub fn check_row(ui: &mut Ui, checked: bool, label: &str, help: &str, enabled: bool) -> bool {
    let response = checkbox(ui, checked, label, enabled);
    let response = if help.is_empty() { response } else { response.tip(help) };
    response.clicked()
}

/// The implicit `CheckBox` style: a 15 px box (2 px corners, muted border,
/// well fill; brand fill and a 1.6 px tick when checked; text border on
/// hover) in an 18 px column, the label 5 px after it, 26 px minimum height,
/// a 1 px focus edge with 1 px padding, and 50% opacity when disabled.
pub fn checkbox(ui: &mut Ui, checked: bool, label: &str, enabled: bool) -> Response {
    let font = theme::font(13.0, Weight::Regular);
    let width = 2.0 + 18.0 + 5.0 + text_width(ui.ctx(), label, font.clone()).ceil() + 2.0;
    let (rect, response) = ui.allocate_exact_size(Vec2::new(width, 26.0), if enabled { Sense::click() } else { Sense::hover() });
    response.widget_info(|| egui::WidgetInfo::selected(egui::accesskit::Role::CheckBox, enabled, checked, label));
    if ui.is_rect_visible(rect) {
        let opacity = if enabled { 1.0 } else { 0.5 };
        let painter = ui.painter();
        if response.has_focus() {
            painter.rect_stroke(rect, 2.0, Stroke::new(1.0, theme::FOCUS), egui::StrokeKind::Inside);
        }
        let column = Rect::from_min_size(Pos2::new(rect.left() + 2.0, rect.top()), Vec2::new(18.0, rect.height()));
        let ppp = ui.ctx().pixels_per_point();
        let snap = |v: f32| (v * ppp).round() / ppp;
        let boxed = Rect::from_min_size(Pos2::new(snap(column.center().x - 7.5), snap(column.center().y - 7.5)), Vec2::splat(15.0));
        let border = if enabled && response.hovered() { theme::TEXT } else { theme::MUTED };
        let fill = if checked { theme::BRAND_ACTION } else { theme::WELL };
        painter.rect(boxed, 2.0, theme::with_opacity(fill, opacity), Stroke::new(1.0, theme::with_opacity(border, opacity)), egui::StrokeKind::Inside);
        if checked {
            // `M 0,4 L 3,7 L 9,0` in a 9 x 7 box centred in the 18 px column.
            let origin = Pos2::new(column.center().x - 4.5, column.center().y - 3.5);
            let points = [Pos2::new(0.0, 4.0), Pos2::new(3.0, 7.0), Pos2::new(9.0, 0.0)].map(|p| origin + p.to_vec2());
            painter.add(egui::Shape::line(points.to_vec(), Stroke::new(1.6, theme::with_opacity(theme::BRAND_ACTION_TEXT, opacity))));
        }
        painter.text(Pos2::new(column.right() + 5.0, rect.center().y), Align2::LEFT_CENTER, label, font, theme::with_opacity(theme::TEXT, opacity));
    }
    if response.clicked() {
        response.request_focus();
    }
    response
}

// ── Menus (`ContextMenu` / `MenuItem` / `Separator` templates) ─────────────

/// Check column, arrow column, row height and horizontal padding.
const MENU_CHECK: f32 = 20.0;
const MENU_ARROW: f32 = 12.0;
const MENU_ROW: f32 = 28.0;
const MENU_PAD_X: f32 = 8.0;

/// The menu popup frame: panel fill, 1 px border, 2 px padding, square.
pub fn menu_frame() -> egui::Frame {
    egui::Frame::new().fill(theme::PANEL).stroke(Stroke::new(1.0, theme::BORDER)).inner_margin(2)
}

fn menu_width_key(ui: &Ui) -> Id {
    Id::unique(("vinemix-menu-width", ui.layer_id().id))
}

/// Prepares a menu body: no gaps between rows, as wide as its widest row
/// (measured last frame) and at least `min_width` (`ContextMenu.Width`).
pub fn menu_body(ui: &mut Ui, min_width: f32) {
    ui.spacing_mut().item_spacing = Vec2::ZERO;
    let key = menu_width_key(ui);
    let measured = ui.ctx().data_mut(|d| std::mem::take(d.get_temp_mut_or_default::<f32>(key)));
    ui.set_width(min_width.max(measured).max(1.0));
}

fn report_menu_width(ui: &Ui, desired: f32) {
    let key = menu_width_key(ui);
    ui.ctx().data_mut(|d| {
        let width = d.get_temp_mut_or_default::<f32>(key);
        *width = width.max(desired);
    });
}

fn open_submenu_key(ui: &Ui) -> Id {
    Id::unique(("vinemix-open-submenu", ui.layer_id().id))
}

fn set_open_submenu(ui: &Ui, row: Option<Id>) {
    let key = open_submenu_key(ui);
    ui.ctx().data_mut(|d| d.insert_temp(key, row));
}

fn open_submenu(ui: &Ui) -> Option<Id> {
    let key = open_submenu_key(ui);
    ui.ctx().data(|d| d.get_temp::<Option<Id>>(key)).flatten()
}

/// A menu row's leading icon (`MenuItem.Icon` rectangles).
#[derive(Clone, Copy, Debug)]
pub struct Swatch {
    pub color: Color32,
    pub size: f32,
    pub radius: f32,
}

impl Swatch {
    /// Node-category icon: a 10 px square.
    pub fn category(color: Color32) -> Self {
        Self { color, size: 10.0, radius: 0.0 }
    }

    /// Group-colour icon: 12 px with 3 px corners.
    pub fn group(color: Color32) -> Self {
        Self { color, size: 12.0, radius: 3.0 }
    }
}

struct MenuRow<'a> {
    label: &'a str,
    gesture: &'a str,
    enabled: bool,
    checked: bool,
    arrow: bool,
    color: Option<Color32>,
    swatch: Option<Swatch>,
}

fn menu_row(ui: &mut Ui, row: &MenuRow<'_>, highlighted: bool) -> Response {
    let font = theme::font(13.0, Weight::Regular);
    let gesture_font = theme::font(12.0, Weight::Mono);
    let label_width = ui.fonts_mut(|f| f.layout_no_wrap(row.label.to_owned(), font.clone(), Color32::WHITE).size().x);
    let gesture_width = if row.gesture.is_empty() {
        0.0
    } else {
        12.0 + 4.0 + ui.fonts_mut(|f| f.layout_no_wrap(row.gesture.to_owned(), gesture_font.clone(), Color32::WHITE).size().x)
    };
    let desired = MENU_PAD_X * 2.0 + MENU_CHECK + label_width + gesture_width + MENU_ARROW;
    report_menu_width(ui, desired);
    let width = ui.available_width().max(desired);
    let (rect, response) = ui.allocate_exact_size(Vec2::new(width, MENU_ROW), if row.enabled { Sense::click() } else { Sense::hover() });
    response.widget_info(|| {
        if row.checked {
            egui::WidgetInfo::selected(egui::accesskit::Role::MenuItemCheckBox, row.enabled, true, row.label)
        } else {
            egui::WidgetInfo::labeled(egui::accesskit::Role::MenuItem, row.enabled, row.label)
        }
    });
    if !ui.is_rect_visible(rect) {
        return response;
    }
    let painter = ui.painter();
    if row.enabled && (response.hovered() || highlighted) {
        let fill = if highlighted && !response.hovered() { theme::BUTTON_FACE } else { theme::BUTTON_HOVER };
        painter.rect_filled(rect, 0.0, fill);
    }
    if response.has_focus() {
        painter.rect_stroke(rect, 0.0, Stroke::new(1.0, theme::FOCUS), egui::StrokeKind::Inside);
    }
    let foreground = if row.enabled { row.color.unwrap_or(theme::TEXT) } else { theme::MUTED };
    let inner = rect.shrink2(Vec2::new(MENU_PAD_X, 4.0));
    if row.checked {
        // Path "M 1,5 L 4.5,8.5 L 11.5,1" in a 12x10 box with a 1 px left margin.
        let origin = Pos2::new(inner.left() + 1.0, inner.center().y - 5.0);
        let points = vec![origin + Vec2::new(1.0, 5.0), origin + Vec2::new(4.5, 8.5), origin + Vec2::new(11.5, 1.0)];
        painter.add(egui::Shape::line(points, Stroke::new(1.8, foreground)));
    } else if let Some(swatch) = row.swatch {
        let square = Rect::from_min_size(Pos2::new(inner.left(), inner.center().y - swatch.size / 2.0), Vec2::splat(swatch.size));
        painter.rect_filled(square, swatch.radius, swatch.color);
    }
    let label_left = inner.left() + MENU_CHECK;
    let right = inner.right() - MENU_ARROW;
    let mut label_right = right;
    if !row.gesture.is_empty() {
        let g = painter.text(Pos2::new(right - 4.0, inner.center().y), Align2::RIGHT_CENTER, row.gesture, gesture_font, theme::MUTED);
        label_right = g.left() - 12.0;
    }
    text_elided(painter, Pos2::new(label_left, inner.center().y), Align2::LEFT_CENTER, row.label, font, foreground, label_right - label_left);
    if row.arrow {
        // Path "M 0,0 L 4,4 L 0,8", right-aligned in the arrow column.
        let tip = Pos2::new(inner.right() - 0.5, inner.center().y);
        let points = vec![tip + Vec2::new(-4.0, -4.0), tip, tip + Vec2::new(-4.0, 4.0)];
        painter.add(egui::Shape::line(points, Stroke::new(1.2, foreground)));
    }
    response
}

fn with_help(response: Response, help: &str) -> Response {
    if help.is_empty() {
        response
    } else {
        response.tip(help).disabled_tip(help)
    }
}

/// A command row; returns true when chosen.
pub fn menu_item(ui: &mut Ui, label: &str, gesture: &str, enabled: bool, help: &str) -> bool {
    menu_item_styled(ui, label, gesture, enabled, help, None, None)
}

/// A command row with an optional foreground colour and leading swatch.
pub fn menu_item_styled(ui: &mut Ui, label: &str, gesture: &str, enabled: bool, help: &str, color: Option<Color32>, swatch: Option<Swatch>) -> bool {
    let row = MenuRow { label, gesture, enabled, checked: false, arrow: false, color, swatch };
    let response = menu_row(ui, &row, false);
    if response.hovered() {
        set_open_submenu(ui, None);
    }
    with_help(response, help).clicked()
}

/// A checkable preference row (`IsCheckable`, `StaysOpenOnClick`); returns
/// true when toggled.
pub fn menu_check(ui: &mut Ui, value: &mut bool, label: &str, enabled: bool, help: &str) -> bool {
    menu_check_with(ui, value, label, enabled, help, None)
}

/// A checkable row with an icon shown while unchecked.
pub fn menu_check_with(ui: &mut Ui, value: &mut bool, label: &str, enabled: bool, help: &str, swatch: Option<Swatch>) -> bool {
    let row = MenuRow { label, gesture: "", enabled, checked: *value, arrow: false, color: None, swatch };
    let response = menu_row(ui, &row, false);
    if response.hovered() {
        set_open_submenu(ui, None);
    }
    let response = with_help(response, help);
    if enabled && response.clicked() {
        *value = !*value;
        return true;
    }
    false
}

/// A disabled caption row inside a menu.
pub fn menu_caption(ui: &mut Ui, label: &str) {
    let row = MenuRow { label, gesture: "", enabled: false, checked: false, arrow: false, color: None, swatch: None };
    let response = menu_row(ui, &row, false);
    if response.hovered() {
        set_open_submenu(ui, None);
    }
}

/// `Separator`: a 1 px line with 8,5 margins.
pub fn menu_separator(ui: &mut Ui) {
    let (rect, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), 11.0), Sense::hover());
    let line = Rect::from_min_max(Pos2::new(rect.left() + 8.0, rect.top() + 5.0), Pos2::new(rect.right() - 8.0, rect.top() + 6.0));
    ui.painter().rect_filled(line, 0.0, theme::BORDER);
}

/// An expandable group in the same scrolling menu as its children.
pub fn menu_disclosure(ui: &mut Ui, label: &str, help: &str, expanded: &mut bool) {
    let row = MenuRow { label, gesture: "", enabled: true, checked: false, arrow: false, color: None, swatch: None };
    let response = menu_row(ui, &row, false);
    if response.clicked() {
        response.request_focus();
        *expanded = !*expanded;
    } else if response.has_focus() {
        if ui.input(|i| i.key_pressed(egui::Key::ArrowRight)) {
            *expanded = true;
        } else if ui.input(|i| i.key_pressed(egui::Key::ArrowLeft)) {
            *expanded = false;
        }
    }
    if response.has_focus() {
        ui.memory_mut(|m| m.set_focus_lock_filter(response.id, egui::EventFilter { horizontal_arrows: true, ..Default::default() }));
    }
    response.ctx.accesskit_node_builder(response.id, |node| node.set_expanded(*expanded));
    let center = Pos2::new(response.rect.right() - MENU_PAD_X - 5.0, response.rect.center().y);
    let offsets = if *expanded { [(-4.0, -2.0), (0.0, 2.0), (4.0, -2.0)] } else { [(-2.0, -4.0), (2.0, 0.0), (-2.0, 4.0)] };
    let points = offsets.into_iter().map(|(x, y)| center + Vec2::new(x, y)).collect();
    ui.painter().add(egui::Shape::line(points, Stroke::new(1.2, theme::TEXT)));
    with_help(response, help);
}

/// A row whose submenu opens to the right on hover, click or Right arrow.
pub fn submenu<R>(ui: &mut Ui, label: &str, enabled: bool, help: &str, content: impl FnOnce(&mut Ui) -> R) -> Option<R> {
    submenu_styled(ui, label, enabled, help, None, content)
}

pub fn submenu_styled<R>(ui: &mut Ui, label: &str, enabled: bool, help: &str, swatch: Option<Swatch>, content: impl FnOnce(&mut Ui) -> R) -> Option<R> {
    let row_id = ui.next_auto_id();
    let open = enabled && open_submenu(ui) == Some(row_id);
    let row = MenuRow { label, gesture: "", enabled, checked: false, arrow: true, color: None, swatch };
    let response = menu_row(ui, &row, open);
    let opens = enabled && (response.hovered() || response.clicked() || (response.has_focus() && ui.input(|i| i.key_pressed(egui::Key::ArrowRight))));
    if opens && !open {
        set_open_submenu(ui, Some(row_id));
    }
    // An open submenu dismisses its row's hover help (`SubmenuOpened`).
    let response = if open || opens { response } else { with_help(response, help) };
    if !(open || opens) {
        return None;
    }
    let anchor = Pos2::new(response.rect.right() + 2.0, response.rect.top() - 3.0);
    let inner = egui::Area::new(row_id.with("submenu"))
        .order(egui::Order::Foreground)
        .fixed_pos(anchor)
        .constrain(true)
        .show(ui.ctx(), |ui| {
            menu_frame()
                .show(ui, |ui| {
                    menu_body(ui, 206.0);
                    egui::ScrollArea::vertical().max_height(520.0).show(ui, content).inner
                })
                .inner
        });
    Some(inner.inner)
}

// ── Hover help (`ToolTip` style and `NodeToolTip.Create`) ──────────────────
//
// Content standard (TWEAK-018): hover help is a short control name and a
// brief purpose or relevant state. How to operate a control (drag, keys,
// ranges, reset) belongs in Help and in the control's accessible
// description, not in the popup. Actionable errors, disabled explanations
// and essential limitations stay.

/// Lays out hover help: a multi-line text is a `NodeToolTip` (SemiBold title,
/// then lines 5 px apart); a single line is a plain tooltip.
pub fn tip_ui(ui: &mut Ui, text: &str) {
    ui.set_max_width(300.0);
    ui.spacing_mut().item_spacing = Vec2::ZERO;
    let mut lines = text.split('\n').filter(|l| !l.trim().is_empty());
    let multi = text.contains('\n');
    if let Some(first) = lines.next() {
        let weight = if multi { Weight::SemiBold } else { Weight::Regular };
        ui.add(egui::Label::new(egui::RichText::new(first).font(theme::font(13.0, weight)).color(theme::TEXT)).wrap());
    }
    for line in lines {
        ui.add_space(5.0);
        ui.add(egui::Label::new(egui::RichText::new(line).font(theme::font(13.0, Weight::Regular)).color(theme::TEXT)).wrap());
    }
}

/// Keyboard access to hover help (`HoverHelp` / `MenuHoverHelp`): a control
/// reached with the keyboard shows its help after the usual delay, and
/// Ctrl+Shift+F10 shows or hides the focused control's help at once. Any
/// other key, a pointer press or a focus change dismisses it. Showing help
/// never moves focus.
#[derive(Clone, Copy, Debug, Default)]
struct KeyboardHelp {
    focused: Option<Id>,
    /// When the focused control received its focus.
    since: f64,
    /// Focus arrived by keyboard navigation and has not been dismissed.
    by_keyboard: bool,
    /// Explicitly shown with the shortcut.
    forced: bool,
    last_navigation: f64,
    last_pointer: f64,
}

fn keyboard_help_id() -> Id {
    Id::unique("vinemix-keyboard-help")
}

/// Keyboard navigation counts for this long when focus then moves.
const NAVIGATION_WINDOW: f64 = 0.25;

/// Updates keyboard help once per frame, before any control is drawn.
pub fn help_frame(ctx: &egui::Context) {
    let focused = ctx.memory(|m| m.focused());
    let delay = f64::from(ctx.global_style().interaction.tooltip_delay);
    let (now, chord, navigation, other_key, pointer) = ctx.input_mut(|i| {
        let chord = i.consume_key(egui::Modifiers::CTRL | egui::Modifiers::SHIFT, Key::F10);
        let is_navigation = |key: &Key| matches!(key, Key::Tab | Key::ArrowUp | Key::ArrowDown | Key::ArrowLeft | Key::ArrowRight);
        let mut navigation = false;
        let mut other_key = false;
        for event in &i.events {
            match event {
                egui::Event::Key { key, pressed: true, .. } if is_navigation(key) => navigation = true,
                egui::Event::Key { pressed: true, .. } | egui::Event::Text(_) => other_key = true,
                _ => {}
            }
        }
        (i.time, chord, navigation, other_key, i.pointer.any_pressed())
    });
    let mut state = ctx.data(|d| d.get_temp::<KeyboardHelp>(keyboard_help_id())).unwrap_or(KeyboardHelp {
        last_navigation: f64::NEG_INFINITY,
        last_pointer: f64::NEG_INFINITY,
        ..KeyboardHelp::default()
    });
    if navigation {
        state.last_navigation = now;
    }
    if pointer {
        state.last_pointer = now;
    }
    if focused != state.focused {
        state.focused = focused;
        state.since = now;
        state.forced = false;
        state.by_keyboard = focused.is_some() && now - state.last_navigation <= NAVIGATION_WINDOW && state.last_navigation > state.last_pointer;
    } else if pointer || other_key || navigation {
        // Typing, adjusting or clicking: get out of the way.
        state.by_keyboard = false;
        state.forced = false;
    }
    if chord && state.focused.is_some() {
        let showing = state.forced || (state.by_keyboard && now - state.since >= delay);
        state.forced = !showing;
        state.by_keyboard = false;
    }
    if state.by_keyboard && !state.forced {
        let remaining = delay - (now - state.since);
        if remaining > 0.0 {
            ctx.request_repaint_after_secs(remaining as f32);
        }
    }
    ctx.data_mut(|d| d.insert_temp(keyboard_help_id(), state));
}

/// Whether keyboard help is currently showing for the control `id`.
pub fn keyboard_help_shown(ctx: &egui::Context, id: Id) -> bool {
    let Some(state) = ctx.data(|d| d.get_temp::<KeyboardHelp>(keyboard_help_id())) else { return false };
    if state.focused != Some(id) {
        return false;
    }
    let delay = f64::from(ctx.global_style().interaction.tooltip_delay);
    state.forced || (state.by_keyboard && ctx.input(|i| i.time) - state.since >= delay)
}

/// The accessible description for hover help: everything after a title line
/// (the title repeats the control's accessible name), or the whole line.
fn description_of(text: &str) -> String {
    let mut lines = text.split('\n').map(str::trim).filter(|l| !l.is_empty());
    let first = lines.next().unwrap_or_default();
    let rest: Vec<&str> = lines.collect();
    if rest.is_empty() { first.to_owned() } else { rest.join(" ") }
}

#[cfg(test)]
thread_local! {
    /// Every hover-help text requested on this thread (see `recorded_tips`).
    static RECORDED_TIPS: std::cell::RefCell<Vec<String>> = const { std::cell::RefCell::new(Vec::new()) };
}

/// Hover help is attached to every drawn control whether or not it is
/// hovered, so recording what is attached while the interface is driven
/// through its states yields the application's complete tooltip content.
#[cfg(test)]
fn record_tip(text: &str) {
    RECORDED_TIPS.with(|tips| tips.borrow_mut().push(text.to_owned()));
}

/// Takes the hover-help texts recorded on this thread since the last call.
#[cfg(test)]
pub fn recorded_tips() -> Vec<String> {
    RECORDED_TIPS.with(|tips| std::mem::take(&mut *tips.borrow_mut()))
}

/// Sets a control's accessible description without any hover popup, for
/// content that is already on screen or one activation away.
pub fn describe(response: &Response, description: &str) {
    if !description.is_empty() {
        response.ctx.accesskit_node_builder(response.id, |node| node.set_description(description.to_owned()));
    }
}

/// Hover help on any response.
pub trait Tip: Sized {
    /// Concise hover help, also shown for keyboard focus (see
    /// [`help_frame`]) and exposed as the control's accessible description.
    fn tip(self, text: impl Into<String>) -> Self;
    /// As [`tip`](Tip::tip), with a separate, fuller accessible description
    /// (how to operate the control) that is not shown in the popup.
    fn tip_described(self, text: impl Into<String>, description: impl Into<String>) -> Self;
    /// Hover help placed at `anchor` (a screen rectangle inside a large
    /// owner, such as the hovered point of a cable on the canvas) instead of
    /// at the edge of the owner's whole rectangle.
    fn tip_at(self, anchor: Rect, text: impl Into<String>) -> Self;
    fn disabled_tip(self, text: impl Into<String>) -> Self;
}

impl Tip for Response {
    fn tip(self, text: impl Into<String>) -> Self {
        let text: String = text.into();
        let description = description_of(&text);
        self.tip_described(text, description)
    }

    fn tip_described(self, text: impl Into<String>, description: impl Into<String>) -> Self {
        let text: String = text.into();
        if text.is_empty() {
            return self;
        }
        #[cfg(test)]
        record_tip(&text);
        describe(&self, &description.into());
        if keyboard_help_shown(&self.ctx, self.id) {
            egui::Tooltip::for_widget(&self).show(|ui| tip_ui(ui, &text));
            return self;
        }
        self.on_hover_ui(move |ui| tip_ui(ui, &text))
    }

    fn tip_at(self, anchor: Rect, text: impl Into<String>) -> Self {
        let text: String = text.into();
        if text.is_empty() {
            return self;
        }
        #[cfg(test)]
        record_tip(&text);
        let mut tooltip = egui::Tooltip::for_enabled(&self);
        tooltip.popup = tooltip.popup.anchor(anchor);
        tooltip.show(|ui| tip_ui(ui, &text));
        self
    }

    fn disabled_tip(self, text: impl Into<String>) -> Self {
        let text: String = text.into();
        if text.is_empty() {
            return self;
        }
        #[cfg(test)]
        record_tip(&text);
        describe(&self, &description_of(&text));
        self.on_disabled_hover_ui(move |ui| tip_ui(ui, &text))
    }
}

#[cfg(test)]
#[path = "widgets_tests.rs"]
mod tests;
