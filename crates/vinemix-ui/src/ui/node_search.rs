//! Find node (`NodeSearchView`): a search box, the matching nodes, and
//! Close / Go to node. Shared by the workspace overlay and the Help lesson.

use eframe::egui::{self, Id, Key, Pos2, Rect, Sense, Ui, Vec2};
use vinemix_model::config::AudioGraph;

use super::theme::{self, Weight};
use super::widgets::{self, ButtonStyle, Tip as _};
use crate::graph::search;

#[derive(Debug, Default)]
pub struct NodeSearchState {
    pub query: String,
    selected: Option<String>,
    focus: bool,
    results: Vec<search::Result>,
}

impl NodeSearchState {
    pub fn begin(&mut self) {
        self.query.clear();
        self.selected = None;
        self.focus = true;
        self.results.clear();
    }

    pub fn clear(&mut self) {
        self.query.clear();
        self.selected = None;
        self.focus = true;
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Outcome {
    None,
    Choose(String),
    Close,
}

/// Placement of the Close / Go to node buttons.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Buttons {
    /// The standalone dialog: right-aligned under a rule.
    Dialog,
    /// The Help lesson: left-aligned, Close reads "Clear search".
    Lesson,
}

/// Draws the view in `rect` (its own 12 px margin included when `heading`).
pub fn show(ui: &mut Ui, id: Id, rect: Rect, state: &mut NodeSearchState, graph: &AudioGraph, heading: bool, buttons: Buttons) -> Outcome {
    let mut outcome = Outcome::None;
    let mut child = ui.new_child(egui::UiBuilder::new().max_rect(rect));
    let ui = &mut child;
    ui.spacing_mut().item_spacing = Vec2::ZERO;
    let inner = if heading { rect.shrink(12.0) } else { rect };
    let mut y = inner.top();
    if heading {
        let title = ui.painter().text(Pos2::new(inner.left(), y), egui::Align2::LEFT_TOP, "Find node", theme::font(16.0, Weight::SemiBold), theme::TEXT);
        y = title.bottom() + 10.0;
    }
    // Search box.
    let field = Rect::from_min_size(Pos2::new(inner.left(), y), Vec2::new(inner.width(), 30.0));
    let edit = egui::TextEdit::singleline(&mut state.query)
        .id(id.with("field"))
        .hint_text("Name, type, device or app")
        .desired_width(field.width())
        .min_size(field.size())
        .margin(egui::Margin::symmetric(8, 4))
        .font(theme::font(13.0, Weight::Regular));
    let mut field_ui = widgets::field_ui(ui, field);
    let response = field_ui.add(edit).tip("Name, type, device or app");
    if std::mem::take(&mut state.focus) {
        response.request_focus();
    }
    y = field.bottom() + 8.0;

    // Results, preserving the selection when it still matches.
    let matches = search::matches(graph, &state.query);
    if matches != state.results {
        state.results = matches;
        if !state.selected.as_ref().is_some_and(|id| state.results.iter().any(|r| &r.id == id)) {
            state.selected = state.results.first().map(|r| r.id.clone());
        }
    }
    let selected_index = state.selected.as_ref().and_then(|id| state.results.iter().position(|r| &r.id == id));
    if response.has_focus() {
        let (up, down, enter) = ui.input(|i| (i.key_pressed(Key::ArrowUp), i.key_pressed(Key::ArrowDown), i.key_pressed(Key::Enter)));
        if !state.results.is_empty() && (up || down) {
            let current = selected_index.unwrap_or(0) as isize;
            let next = (current + if down { 1 } else { -1 }).clamp(0, state.results.len() as isize - 1) as usize;
            state.selected = Some(state.results[next].id.clone());
        }
        if enter {
            if let Some(id) = state.selected.clone() {
                outcome = Outcome::Choose(id);
            }
        }
    }

    let footer_height = match buttons {
        Buttons::Dialog => 8.0 + 1.0 + 10.0 + 28.0,
        Buttons::Lesson => 8.0 + 28.0,
    };
    let list = Rect::from_min_max(Pos2::new(inner.left(), y), Pos2::new(inner.right(), inner.bottom() - footer_height));
    if state.results.is_empty() {
        let text = if graph.nodes.is_empty() { "No nodes in this workspace." } else { "No matching nodes." };
        widgets::text_wrapped(ui.painter(), Rect::from_min_max(list.min + Vec2::new(10.0, 10.0), list.max - Vec2::new(8.0, 0.0)), text, theme::font(13.0, Weight::Regular), theme::MUTED, egui::Align::Min);
    } else {
        let mut list_ui = ui.new_child(egui::UiBuilder::new().max_rect(list));
        egui::ScrollArea::vertical().id_salt(id.with("results")).auto_shrink([false, false]).show(&mut list_ui, |ui| {
            ui.spacing_mut().item_spacing = Vec2::ZERO;
            for result in &state.results {
                let width = ui.available_width();
                let text_width = (width - 2.0 - 16.0).max(40.0);
                let name = ui.fonts_mut(|f| f.layout(result.name.clone(), theme::font(13.0, Weight::Medium), theme::TEXT, text_width));
                let detail = ui.fonts_mut(|f| f.layout(result.detail.clone(), theme::font(12.0, Weight::Regular), theme::MUTED, text_width));
                let height = 5.0 + name.size().y + 1.0 + detail.size().y + 5.0;
                let (row, row_response) = ui.allocate_exact_size(Vec2::new(width, height), Sense::click());
                let selected = state.selected.as_deref() == Some(result.id.as_str());
                let painter = ui.painter();
                if row_response.hovered() {
                    painter.rect_filled(row, 0.0, theme::BUTTON_HOVER);
                } else if selected {
                    painter.rect_filled(row, 0.0, theme::BUTTON_FACE);
                }
                if selected {
                    painter.rect_filled(Rect::from_min_size(row.min, Vec2::new(2.0, row.height())), 0.0, theme::MUTED);
                }
                let name_height = name.size().y;
                painter.galley(row.min + Vec2::new(2.0 + 8.0, 5.0), name, theme::TEXT);
                painter.galley(row.min + Vec2::new(2.0 + 8.0, 5.0 + name_height + 1.0), detail, theme::MUTED);
                if row_response.clicked() {
                    state.selected = Some(result.id.clone());
                }
                if row_response.double_clicked() {
                    outcome = Outcome::Choose(result.id.clone());
                }
                if selected && state.focus {
                    ui.scroll_to_rect(row, None);
                }
            }
        });
    }

    // Buttons.
    let has_selection = state.selected.is_some();
    let button_top = inner.bottom() - 28.0;
    let (close_label, close_width) = match buttons {
        Buttons::Dialog => ("Close", 64.0),
        Buttons::Lesson => ("Clear search", 104.0),
    };
    let (close_rect, go_rect) = match buttons {
        Buttons::Dialog => {
            let rule = inner.bottom() - 28.0 - 10.0 - 1.0;
            ui.painter().rect_filled(Rect::from_min_size(Pos2::new(inner.left(), rule), Vec2::new(inner.width(), 1.0)), 0.0, theme::BORDER);
            let go = Rect::from_min_size(Pos2::new(inner.right() - 92.0, button_top), Vec2::new(92.0, 28.0));
            (Rect::from_min_size(Pos2::new(go.left() - 8.0 - close_width, button_top), Vec2::new(close_width, 28.0)), go)
        }
        Buttons::Lesson => {
            let close = Rect::from_min_size(Pos2::new(inner.left(), button_top), Vec2::new(close_width, 28.0));
            (close, Rect::from_min_size(Pos2::new(close.right() + 8.0, button_top), Vec2::new(92.0, 28.0)))
        }
    };
    let close_help = match buttons {
        Buttons::Dialog => "Close node search",
        Buttons::Lesson => "Clear practice search",
    };
    if widgets::button(ui, id.with("close"), close_rect, close_label, ButtonStyle::toolbar(13.0), true, 1.0).tip(close_help).clicked() {
        outcome = Outcome::Close;
    }
    if widgets::button(ui, id.with("go"), go_rect, "Go to node", ButtonStyle::toolbar_primary(13.0), has_selection, 1.0)
        .tip("Select the highlighted node.")
        .clicked()
    {
        if let Some(id) = state.selected.clone() {
            outcome = Outcome::Choose(id);
        }
    }
    outcome
}
