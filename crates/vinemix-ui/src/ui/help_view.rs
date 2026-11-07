//! Help & shortcuts (`WorkspaceHelpView`): the shortcut reference and
//! isolated practice lessons (`HelpControlLessons`, `HelpAdvancedLessons`).
//! Every lesson owns disposable, in-memory values; nothing reaches the mix.

use eframe::egui::{self, Align2, Color32, Id, Key, Pos2, Rect, Sense, Stroke, Ui, Vec2};
use vinemix_model::config::{AudioGraph, DeviceBinding, GraphNode, ProcessBinding};

use super::dial::{self, DialState, Spec, Variant};
use super::help::{self, Entry};
use super::meter::{self, Meter};
use super::node_search::{self, NodeSearchState};
use super::theme::{self, Weight};
use super::widgets::{self, ButtonStyle, Choice, Tip as _};

const TOPICS: [(&str, &str, &str); 6] = [
    ("controls", "Controls", "Open a section, then choose a control for details."),
    ("dials", "Dials & values", "Drag vertically, or click a value to type."),
    ("buttons", "Buttons & menus", "States, selectors and menu actions."),
    ("search", "Search & results", "Find a sample node by name, type, device or app."),
    ("equalizer", "Equalizer", "Select a band to edit its frequency, gain and width."),
    ("feedback", "Meters & status", "Levels, overloads and device or processing states."),
];

enum Lesson {
    Dials(Box<DialLesson>),
    Buttons(Box<ButtonsLesson>),
    Search(Box<SearchLesson>),
    Equalizer(Box<EqualizerLesson>),
    Feedback(Box<FeedbackLesson>),
}

impl Lesson {
    fn create(topic: &str) -> Option<Self> {
        Some(match topic {
            "dials" => Self::Dials(Box::default()),
            "buttons" => Self::Buttons(Box::default()),
            "search" => Self::Search(Box::default()),
            "equalizer" => Self::Equalizer(Box::default()),
            "feedback" => Self::Feedback(Box::default()),
            _ => return None,
        })
    }

    fn reset(&mut self) {
        match self {
            Self::Dials(l) => **l = DialLesson::default(),
            Self::Buttons(l) => **l = ButtonsLesson::default(),
            Self::Search(l) => **l = SearchLesson::default(),
            Self::Equalizer(l) => **l = EqualizerLesson::default(),
            Self::Feedback(l) => **l = FeedbackLesson::default(),
        }
    }

    /// Cancels an open menu, dropdown, drag or entry; true when one was.
    fn try_cancel(&mut self, ctx: &egui::Context) -> bool {
        if egui::Popup::is_any_open(ctx) {
            egui::Popup::close_all(ctx);
            return true;
        }
        match self {
            Self::Buttons(l) if l.menu_open => {
                l.menu_open = false;
                true
            }
            Self::Search(l) if !l.search.query.is_empty() => {
                l.search.clear();
                l.chosen = None;
                true
            }
            Self::Dials(l) => cancel_dials(l.states.iter_mut()),
            Self::Buttons(l) => cancel_dials([&mut l.amount_state, &mut l.ratio_state].into_iter()),
            _ => false,
        }
    }
}

fn cancel_dials<'a>(states: impl Iterator<Item = &'a mut DialState>) -> bool {
    let mut cancelled = false;
    for state in states {
        if state.draft.is_some() || state.invalid || state.dragging {
            state.draft = None;
            state.invalid = false;
            state.dragging = false;
            cancelled = true;
        }
    }
    cancelled
}

/// Help's state while it is open.
pub struct HelpState {
    topic: &'static str,
    section: String,
    highlighted: Option<&'static str>,
    scroll_to: Option<&'static str>,
    focus_close: bool,
    lesson: Option<Lesson>,
}

impl std::fmt::Debug for HelpState {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("HelpState").field("topic", &self.topic).finish_non_exhaustive()
    }
}

impl HelpState {
    /// Opens on Controls, or on `entry` with its section expanded.
    pub fn new(entry: Option<&'static str>) -> Self {
        let mut state = Self { topic: "controls", section: "view".into(), highlighted: None, scroll_to: None, focus_close: true, lesson: None };
        if let Some(entry) = entry {
            state.show_entry(entry);
        }
        state
    }

    pub fn show_entry(&mut self, id: &'static str) {
        let Some(entry) = help::ENTRIES.iter().find(|e| e.id == id) else { return };
        self.select_topic("controls");
        self.section = entry.section_id.to_owned();
        self.highlighted = Some(entry.id);
        self.scroll_to = Some(entry.id);
    }

    fn select_topic(&mut self, topic: &'static str) {
        self.topic = topic;
        self.lesson = Lesson::create(topic);
        self.highlighted = None;
        self.scroll_to = None;
    }

    /// Escape: first cancels an active example interaction (`TryCancelInteraction`).
    pub fn try_cancel(&mut self, ctx: &egui::Context) -> bool {
        self.lesson.as_mut().is_some_and(|l| l.try_cancel(ctx))
    }
}

// ── Layout helpers (`HelpLessonUi`) ────────────────────────────────────────

fn galley(ui: &Ui, text: &str, size: f32, weight: Weight, color: Color32, width: f32) -> std::sync::Arc<egui::Galley> {
    ui.fonts_mut(|f| f.layout(text.to_owned(), theme::font(size, weight), color, width.max(1.0)))
}

/// `HelpLessonUi.Text`: wrapped, 6 px below.
fn text(ui: &mut Ui, value: &str, size: f32, bold: bool, color: Color32) {
    let width = ui.available_width();
    let g = galley(ui, value, size, if bold { Weight::SemiBold } else { Weight::Regular }, color, width);
    let (rect, _) = ui.allocate_exact_size(Vec2::new(width, g.size().y), Sense::hover());
    ui.painter().galley(rect.min, g, color);
    ui.add_space(6.0);
}

fn hint(ui: &mut Ui, value: &str) {
    text(ui, value, 13.0, false, theme::MUTED);
}

/// `HelpLessonUi.Section`: optional heading and description, 16 px below.
fn section(ui: &mut Ui, title: &str, description: &str, content: impl FnOnce(&mut Ui)) {
    if !title.is_empty() {
        let width = ui.available_width();
        let g = galley(ui, title, 13.0, Weight::SemiBold, theme::TEXT, width);
        let (rect, _) = ui.allocate_exact_size(Vec2::new(width, g.size().y), Sense::hover());
        ui.painter().galley(rect.min, g, theme::TEXT);
        ui.add_space(8.0);
    }
    if !description.is_empty() {
        hint(ui, description);
    }
    content(ui);
    ui.add_space(16.0);
}

/// Two columns: keys (154, SemiBold) and what they do; 8 px below.
fn instruction(ui: &mut Ui, keys: &str, action: &str) {
    let width = ui.available_width();
    let left = galley(ui, keys, 13.0, Weight::SemiBold, theme::TEXT, 154.0 - 16.0);
    let right = galley(ui, action, 13.0, Weight::Regular, theme::TEXT, width - 154.0);
    let height = left.size().y.max(right.size().y);
    let (rect, _) = ui.allocate_exact_size(Vec2::new(width, height), Sense::hover());
    ui.painter().galley(rect.min, left, theme::TEXT);
    ui.painter().galley(rect.min + Vec2::new(154.0, 0.0), right, theme::TEXT);
    ui.add_space(8.0);
}

/// A 92 px label column and a 260 px dropdown; 8 px below.
fn selector_row(ui: &mut Ui, id: Id, label: &str, choices: &[Choice], selected: usize, help: &str) -> Option<usize> {
    let width = ui.available_width();
    let (rect, _) = ui.allocate_exact_size(Vec2::new(width, 28.0), Sense::hover());
    ui.painter().text(Pos2::new(rect.left(), rect.center().y), Align2::LEFT_CENTER, label, theme::font(13.0, Weight::Regular), theme::TEXT);
    let face = Rect::from_min_size(Pos2::new(rect.left() + 92.0, rect.top()), Vec2::new(260.0f32.min(width - 92.0), 28.0));
    let label = choices.get(selected).map(|c| c.label.clone()).unwrap_or_default();
    let (_, picked) = widgets::selector(ui, id, face, Some(selected), &label, choices, true, 1.0, theme::TEXT, help);
    ui.add_space(8.0);
    picked
}

fn choices(labels: &[&str]) -> Vec<Choice> {
    labels.iter().map(|l| Choice { label: (*l).to_owned(), enabled: true, help: String::new() }).collect()
}

/// A `WorkspaceToolbarButton` sized to its text (min 26 high, 8,3 padding).
fn toolbar_button(ui: &mut Ui, id: Id, label: &str, help: &str) -> egui::Response {
    let width = ui.fonts_mut(|f| f.layout_no_wrap(label.to_owned(), theme::font(13.0, Weight::Regular), theme::TEXT).size().x) + 16.0;
    let (rect, _) = ui.allocate_exact_size(Vec2::new(width, 26.0), Sense::hover());
    widgets::button(ui, id, rect, label, ButtonStyle::toolbar(13.0), true, 1.0).tip(help)
}

// ── The view ───────────────────────────────────────────────────────────────

/// Draws Help in `area` (the overlay below the title bar); returns true to close.
pub fn show(ui: &mut Ui, area: Rect, state: &mut HelpState) -> bool {
    let ctx = ui.ctx().clone();
    let mut close = false;
    // The overlay pads 16,12; the view is at most 960 x 680, centred.
    let room = area.shrink2(Vec2::new(16.0, 12.0));
    let rect = Rect::from_center_size(room.center(), Vec2::new(room.width().min(960.0), room.height().min(680.0)));
    let mut root = ui.new_child(egui::UiBuilder::new().max_rect(rect));
    let ui = &mut root;
    ui.spacing_mut().item_spacing = Vec2::ZERO;
    let painter = ui.painter().clone();
    painter.rect_filled(rect, 0.0, theme::PANEL);
    // Swallow pointer input on the panel background.
    ui.interact(rect, Id::unique("help-surface"), Sense::click());

    // Header: 14,8,12,8 margin, Close at the right, a rule beneath.
    let close_width = 16.0 + ui.fonts_mut(|f| f.layout_no_wrap("Close".into(), theme::font(13.0, Weight::Regular), theme::TEXT).size().x);
    let header = Rect::from_min_size(rect.min, Vec2::new(rect.width(), 8.0 + 26.0 + 8.0));
    let close_rect = Rect::from_min_size(Pos2::new(header.right() - 12.0 - close_width, header.top() + 8.0), Vec2::new(close_width, 26.0));
    painter.text(Pos2::new(header.left() + 14.0, close_rect.center().y), Align2::LEFT_CENTER, "Help & shortcuts", theme::font(16.0, Weight::SemiBold), theme::TEXT);
    let close_response = widgets::button(ui, Id::unique("help-close"), close_rect, "Close", ButtonStyle::toolbar(13.0), true, 1.0).tip("Close help and return to the workspace");
    if std::mem::take(&mut state.focus_close) {
        close_response.request_focus();
    }
    if close_response.clicked() {
        close = true;
    }
    painter.rect_filled(Rect::from_min_size(Pos2::new(rect.left(), header.bottom()), Vec2::new(rect.width(), 1.0)), 0.0, theme::BORDER);

    // Footer: Reset example (lessons only) and the isolation note.
    let footer = Rect::from_min_max(Pos2::new(rect.left(), rect.bottom() - (6.0 + 26.0 + 6.0)), rect.max);
    painter.rect_filled(Rect::from_min_size(Pos2::new(rect.left(), footer.top() - 1.0), Vec2::new(rect.width(), 1.0)), 0.0, theme::BORDER);
    let mut note_left = footer.left() + 12.0;
    if state.lesson.is_some() {
        let reset_width = 16.0 + ui.fonts_mut(|f| f.layout_no_wrap("Reset example".into(), theme::font(13.0, Weight::Regular), theme::TEXT).size().x);
        let reset = Rect::from_min_size(Pos2::new(note_left, footer.top() + 6.0), Vec2::new(reset_width, 26.0));
        if widgets::button(ui, Id::unique("help-reset"), reset, "Reset example", ButtonStyle::toolbar(13.0), true, 1.0)
            .tip("Restore this practice example to its starting state")
            .clicked()
        {
            egui::Popup::close_all(&ctx);
            if let Some(lesson) = state.lesson.as_mut() {
                lesson.reset();
            }
        }
        note_left = reset.right() + 14.0;
    }
    painter.text(Pos2::new(note_left, footer.center().y), Align2::LEFT_CENTER, "Examples only. Your mix is unchanged.", theme::font(13.0, Weight::Regular), theme::MUTED);

    // Body: 168 px topic list (hidden below 940 px) and the reading area.
    let body = Rect::from_min_max(Pos2::new(rect.left(), header.bottom() + 1.0), Pos2::new(rect.right(), footer.top() - 1.0));
    let compact = rect.width() < 940.0;
    let reading_left = if compact { body.left() } else { body.left() + 168.0 };
    if !compact {
        painter.rect_filled(Rect::from_min_size(Pos2::new(body.left() + 167.0, body.top()), Vec2::new(1.0, body.height())), 0.0, theme::BORDER);
        let mut y = body.top() + 8.0;
        for (id, title, _) in TOPICS {
            let row = Rect::from_min_size(Pos2::new(body.left() + 4.0, y), Vec2::new(168.0 - 4.0 - 6.0 - 1.0, 13.0 * 1.3 + 12.0));
            let response = ui.interact(row, Id::unique(("help-topic", id)), Sense::click());
            let selected = state.topic == id;
            if selected {
                painter.rect_filled(row, 0.0, theme::BUTTON_FACE);
            }
            let edge = if response.has_focus() {
                theme::FOCUS
            } else if selected {
                theme::A
            } else {
                Color32::TRANSPARENT
            };
            painter.rect_filled(Rect::from_min_size(row.min, Vec2::new(2.0, row.height())), 0.0, edge);
            painter.text(Pos2::new(row.left() + 2.0 + 8.0, row.center().y), Align2::LEFT_CENTER, title, theme::font(13.0, Weight::Regular), if selected { theme::TEXT } else { theme::MUTED });
            if response.clicked() && !selected {
                egui::Popup::close_all(&ctx);
                state.select_topic(id);
            }
            y = row.bottom() + 1.0;
        }
    }
    let reading = Rect::from_min_max(Pos2::new(reading_left + 14.0, body.top() + 12.0), Pos2::new(body.right() - 14.0, body.bottom()));
    let mut reading_ui = ui.new_child(egui::UiBuilder::new().max_rect(reading).layout(egui::Layout::top_down(egui::Align::Min)));
    reading_ui.spacing_mut().item_spacing = Vec2::ZERO;
    if compact {
        let (row, _) = reading_ui.allocate_exact_size(Vec2::new(reading.width(), 28.0), Sense::hover());
        let topic_choices: Vec<Choice> = TOPICS.iter().map(|(_, t, _)| Choice { label: (*t).to_owned(), enabled: true, help: String::new() }).collect();
        let current = TOPICS.iter().position(|(id, _, _)| *id == state.topic).unwrap_or(0);
        let (_, picked) = widgets::selector(&mut reading_ui, Id::unique("help-topic-compact"), row, Some(current), TOPICS[current].1, &topic_choices, true, 1.0, theme::TEXT, "Choose help topic");
        if let Some(index) = picked {
            state.select_topic(TOPICS[index].0);
        }
        reading_ui.add_space(12.0);
    }
    let topic = TOPICS.iter().find(|(id, _, _)| *id == state.topic).copied().unwrap_or(TOPICS[0]);
    egui::ScrollArea::vertical().id_salt(("help-scroll", topic.0)).auto_shrink([false, false]).show(&mut reading_ui, |ui| {
        ui.spacing_mut().item_spacing = Vec2::ZERO;
        ui.set_width(ui.available_width());
        text(ui, topic.1, 15.0, true, theme::TEXT);
        hint(ui, topic.2);
        match state.lesson.as_mut() {
            None => reference(ui, state),
            Some(Lesson::Dials(lesson)) => lesson.show(ui),
            Some(Lesson::Buttons(lesson)) => lesson.show(ui),
            Some(Lesson::Search(lesson)) => lesson.show(ui),
            Some(Lesson::Equalizer(lesson)) => lesson.show(ui),
            Some(Lesson::Feedback(lesson)) => lesson.show(ui),
        }
    });

    if ctx.input(|i| i.key_pressed(Key::Escape)) && !state.try_cancel(&ctx) {
        close = true;
    }
    close
}

/// The collapsible shortcut reference (`BuildReference`).
fn reference(ui: &mut Ui, state: &mut HelpState) {
    let mut sections: Vec<(&'static str, &'static str, Vec<&'static Entry>)> = Vec::new();
    for entry in help::ENTRIES.iter() {
        match sections.iter_mut().find(|(id, _, _)| *id == entry.section_id) {
            Some((_, _, entries)) => entries.push(entry),
            None => sections.push((entry.section_id, entry.section, vec![entry])),
        }
    }
    if !sections.iter().any(|(id, _, _)| *id == state.section) && !state.section.is_empty() {
        state.section = "view".into();
    }
    for (section_id, title, entries) in sections {
        let expanded = state.section == section_id;
        let width = ui.available_width();
        let label = galley(ui, title, 13.0, Weight::SemiBold, theme::TEXT, width - 20.0 - 28.0);
        let height = 9.0 + label.size().y + 9.0;
        let (header, response) = ui.allocate_exact_size(Vec2::new(width, height), Sense::click());
        let fill = if response.is_pointer_button_down_on() {
            theme::CONTROL_BORDER
        } else if response.hovered() || expanded {
            theme::BUTTON_HOVER
        } else {
            theme::BUTTON_FACE
        };
        let painter = ui.painter();
        painter.rect_filled(header, 0.0, fill);
        if response.has_focus() {
            painter.rect_stroke(header, 0.0, Stroke::new(1.0, theme::FOCUS), egui::StrokeKind::Inside);
        }
        painter.galley(Pos2::new(header.left() + 10.0, header.center().y - label.size().y / 2.0), label, theme::TEXT);
        let arrow = Rect::from_center_size(Pos2::new(header.right() - 10.0 - 2.0 - 5.0, header.center().y), Vec2::splat(10.0));
        let points: Vec<Pos2> = if expanded {
            [(1.0, 3.0), (5.0, 7.0), (9.0, 3.0)].iter().map(|(x, y)| arrow.min + Vec2::new(*x, *y)).collect()
        } else {
            [(3.0, 1.0), (7.0, 5.0), (3.0, 9.0)].iter().map(|(x, y)| arrow.min + Vec2::new(*x, *y)).collect()
        };
        painter.add(egui::Shape::line(points, Stroke::new(1.6, theme::TEXT)));
        painter.rect_filled(Rect::from_min_size(Pos2::new(header.left(), header.bottom() - 1.0), Vec2::new(width, 1.0)), 0.0, theme::BORDER);
        let response = response.tip(if expanded { "Choose to collapse this section." } else { "Choose to expand this section and close the others." });
        if response.clicked() {
            state.section = if expanded { String::new() } else { section_id.to_owned() };
            if state.highlighted.and_then(|h| help::ENTRIES.iter().find(|e| e.id == h)).is_some_and(|e| e.section_id != state.section) {
                state.highlighted = None;
            }
        }
        if expanded {
            ui.add_space(5.0);
            let body_top = ui.cursor().top();
            ui.horizontal_top(|ui| {
                ui.add_space(14.0 + 1.0 + 12.0);
                ui.vertical(|ui| {
                    ui.spacing_mut().item_spacing = Vec2::ZERO;
                    for entry in &entries {
                        reference_row(ui, state, entry);
                    }
                });
            });
            let body_bottom = ui.cursor().top();
            let x = ui.min_rect().left() + 14.0;
            ui.painter().rect_filled(Rect::from_min_max(Pos2::new(x, body_top), Pos2::new(x + 1.0, body_bottom)), 0.0, theme::BORDER);
            ui.add_space(4.0);
        }
        ui.add_space(10.0);
    }
}

fn reference_row(ui: &mut Ui, state: &mut HelpState, entry: &'static Entry) {
    let width = ui.available_width();
    let inner = width - 8.0 - 6.0;
    let title_width = (inner - 210.0 - 16.0).max(60.0);
    let title = galley(ui, entry.title, 13.0, Weight::Regular, theme::TEXT, title_width);
    let keys = galley(ui, entry.keys, 12.0, Weight::SemiBold, theme::MUTED, 210.0);
    let line = title.size().y.max(keys.size().y);
    let highlighted = state.highlighted == Some(entry.id);
    let description = highlighted.then(|| galley(ui, entry.description, 13.0, Weight::Regular, theme::MUTED, inner));
    let height = 8.0 + line + description.as_ref().map_or(0.0, |d| 8.0 + d.size().y) + 8.0;
    let (row, response) = ui.allocate_exact_size(Vec2::new(width, height + 1.0), Sense::click());
    let surface = Rect::from_min_size(row.min, Vec2::new(width, height));
    let painter = ui.painter();
    let fill = if response.is_pointer_button_down_on() {
        theme::CONTROL_BORDER
    } else if response.hovered() {
        theme::BUTTON_HOVER
    } else if highlighted {
        theme::BUTTON_FACE
    } else {
        Color32::TRANSPARENT
    };
    painter.rect_filled(surface, 0.0, fill);
    if response.has_focus() {
        painter.rect_stroke(surface, 0.0, Stroke::new(1.0, theme::FOCUS), egui::StrokeKind::Inside);
    }
    let top = surface.top() + 8.0;
    painter.galley(Pos2::new(surface.left() + 8.0, top + (line - title.size().y) / 2.0), title, theme::TEXT);
    painter.galley(Pos2::new(surface.right() - 6.0 - 210.0, top + (line - keys.size().y) / 2.0), keys, theme::MUTED);
    if let Some(description) = description {
        painter.galley(Pos2::new(surface.left() + 8.0, top + line + 8.0), description, theme::MUTED);
    }
    painter.rect_filled(Rect::from_min_size(Pos2::new(row.left(), row.bottom() - 1.0), Vec2::new(width, 1.0)), 0.0, theme::BORDER);
    let response = response.tip(format!("{}\n{}\n{}", entry.title, entry.keys, entry.description));
    if response.clicked() {
        state.highlighted = Some(entry.id);
    }
    if state.scroll_to == Some(entry.id) {
        ui.scroll_to_rect(row.with_max_y(row.top() + 8.0), Some(egui::Align::Min));
        state.scroll_to = None;
    }
}

// ── Dials & values ─────────────────────────────────────────────────────────

struct DialLesson {
    variant: usize,
    values: [f64; 3],
    states: [DialState; 3],
}

impl Default for DialLesson {
    fn default() -> Self {
        let specs = Self::specs();
        Self { variant: 0, values: [specs[0].default, specs[1].default, specs[2].default], states: Default::default() }
    }
}

impl DialLesson {
    fn specs() -> [Spec; 3] {
        [
            Spec::new("help-level", "Level", -60.0, 12.0, 0.1, "dB", 0.0, Variant::Compact)
                .primary(true)
                .changes(0.5, 3.0)
                .description("Adjusts level from −60 to +12 dB. 0 dB leaves it unchanged."),
            Spec::new("help-frequency", "Frequency", 20.0, 20000.0, 1.0, "Hz", 1000.0, Variant::Compact)
                .logarithmic(true)
                .primary(true)
                .changes(10.0, 100.0)
                .description("Adjusts frequency from 20 to 20,000 Hz on a logarithmic scale."),
            Spec::new("help-samples", "Samples", 0.0, 96000.0, 1.0, "", 480.0, Variant::Compact)
                .integer()
                .changes(1.0, 48.0)
                .description("Accepts whole samples. At 48 kHz, 48 samples equal one millisecond."),
        ]
    }

    fn show(&mut self, ui: &mut Ui) {
        let specs = Self::specs();
        section(ui, "", "", |ui| {
            let labels = choices(&["Level (dB)", "Frequency (Hz)", "Samples (whole numbers)"]);
            if let Some(index) = selector_row(ui, Id::unique("help-dial-variant"), "Control", &labels, self.variant, "Practice dial type") {
                if index != self.variant {
                    cancel_dials(self.states.iter_mut());
                    self.variant = index;
                }
            }
            // Demo: a 156 px dial column and the explanation beside it.
            ui.add_space(6.0);
            let spec = &specs[self.variant];
            let height = dial::tile_height(spec);
            let width = ui.available_width();
            let (demo, _) = ui.allocate_exact_size(Vec2::new(width, height), Sense::hover());
            let outcome = dial::tile(
                ui,
                Id::unique(("help-dial", self.variant)),
                spec,
                self.values[self.variant],
                spec.default,
                &mut self.states[self.variant],
                demo.min,
                136.0,
                true,
            );
            if let Some(value) = outcome.changed {
                self.values[self.variant] = value;
            }
            let example = match self.variant {
                1 => "20–20,000 Hz. Equal movement changes frequency proportionally.",
                2 => "0–96,000 samples. Whole numbers only; 480.5 is rejected.",
                _ => "−60 to +12 dB. Exact entry also accepts values such as −6.25 dB.",
            };
            let reading = match self.variant {
                1 => "Reset value: 1,000 Hz".to_owned(),
                2 => format!("{} ms at 48 kHz", dial::format_number(self.values[2] / 48.0, "0.###")),
                _ => "Reset value: 0 dB".to_owned(),
            };
            let detail_width = width - 156.0;
            let a = galley(ui, example, 13.0, Weight::Regular, theme::MUTED, detail_width);
            let b = galley(ui, &reading, 13.0, Weight::Regular, theme::TEXT, detail_width);
            let total = a.size().y + 6.0 + b.size().y + 6.0;
            let top = demo.center().y - total / 2.0;
            let b_top = top + a.size().y + 6.0;
            ui.painter().galley(Pos2::new(demo.left() + 156.0, top), a, theme::MUTED);
            ui.painter().galley(Pos2::new(demo.left() + 156.0, b_top), b, theme::TEXT);
            ui.add_space(4.0);
        });
        section(ui, "Adjustments", "", |ui| {
            instruction(ui, "Drag up / down", "Change the value. Hold Shift for finer movement.");
            instruction(ui, "Exact number", "Click the value, or type a number while its dial is focused. Enter or clicking away applies; Escape cancels. Right-click for Apply value or Cancel edit.");
            instruction(ui, "Tab / Shift+Tab", "Apply and move to the next / previous control. Correct an invalid value before moving on.");
            instruction(ui, "Arrow keys", "Make small adjustments to a focused dial.");
            instruction(ui, "Page Up / Down", "Make larger adjustments.");
            instruction(ui, "Double-click / Home", "Restore the default value.");
            hint(ui, "Wheel: scroll Help. On the workspace canvas, the wheel zooms without changing values.");
        });
    }
}

// ── Buttons & menus ────────────────────────────────────────────────────────

struct ButtonsLesson {
    muted: bool,
    bypassed: bool,
    mono: bool,
    device: usize,
    mode: usize,
    amount: f64,
    ratio: f64,
    amount_state: DialState,
    ratio_state: DialState,
    reduce_motion: bool,
    example_pan: bool,
    menu_open: bool,
    menu_anchor: Pos2,
}

impl Default for ButtonsLesson {
    fn default() -> Self {
        Self {
            muted: false,
            bypassed: false,
            mono: false,
            device: 0,
            mode: 0,
            amount: 18.0,
            ratio: 4.0,
            amount_state: DialState::default(),
            ratio_state: DialState::default(),
            reduce_motion: false,
            example_pan: false,
            menu_open: false,
            menu_anchor: Pos2::ZERO,
        }
    }
}

impl ButtonsLesson {
    fn state_row(ui: &mut Ui, id: &str, label: &str, on: bool, fill: Color32, help: &str, meaning: &str) -> bool {
        let width = ui.available_width();
        let description = galley(ui, meaning, 13.0, Weight::Regular, theme::TEXT, width - 112.0);
        let height = 26.0f32.max(description.size().y);
        let (row, _) = ui.allocate_exact_size(Vec2::new(width, height), Sense::hover());
        let button = Rect::from_min_size(Pos2::new(row.left(), row.center().y - 13.0), Vec2::new(90.0, 26.0));
        let style = ButtonStyle::state(on, fill, theme::INK, 13.0);
        let clicked = widgets::button(ui, Id::unique(("help-state", id)), button, label, style, true, 1.0).tip(help).clicked();
        ui.painter().galley(Pos2::new(row.left() + 112.0, row.center().y - description.size().y / 2.0), description, theme::TEXT);
        ui.add_space(6.0);
        clicked
    }

    fn show(&mut self, ui: &mut Ui) {
        section(ui, "States", "Click to switch on or off.", |ui| {
            if Self::state_row(ui, "mute", if self.muted { "Muted" } else { "Mute" }, self.muted, theme::MUTE, "Silence or restore an input or output.", "Silence an input or output.") {
                self.muted = !self.muted;
            }
            if Self::state_row(ui, "bypass", "Bypass", self.bypassed, theme::MUTE, "Skip this node's processing while audio passes through.", "Skip a node's processing while audio passes through.") {
                self.bypassed = !self.bypassed;
            }
            if Self::state_row(ui, "mono", "Mono", self.mono, theme::A, "Combine left and right into one centred signal.", "Combine left and right into one centred signal.") {
                self.mono = !self.mono;
            }
            let on = |v: bool| if v { "on" } else { "off" };
            text(
                ui,
                &format!(
                    "Mute {}; Bypass {}; Mono {}.\nReduced motion {}; right-drag pan {}.",
                    on(self.muted),
                    on(self.bypassed),
                    on(self.mono),
                    on(self.reduce_motion),
                    on(self.example_pan)
                ),
                13.0,
                false,
                theme::TEXT,
            );
        });
        section(ui, "Dropdowns", "Changing mode shows a different control; each mode keeps its value.", |ui| {
            let mut devices = choices(&["Practice microphone", "Practice headset", "Unavailable microphone"]);
            devices[2].enabled = false;
            devices[2].help = "Unavailable devices cannot be selected. A saved assignment stays visible while disconnected.".into();
            if let Some(index) = selector_row(ui, Id::unique("help-device"), "Device", &devices, self.device, "Example audio device") {
                self.device = index;
            }
            if let Some(index) = selector_row(ui, Id::unique("help-mode"), "Mode", &choices(&["Static ducking", "Dynamic ducking"]), self.mode, "Example ducking mode") {
                if index != self.mode {
                    cancel_dials([&mut self.amount_state, &mut self.ratio_state].into_iter());
                    self.mode = index;
                }
            }
            ui.add_space(4.0);
            let amount = Spec::new("help-amount", "Duck amount", 0.0, 60.0, 0.1, "dB", 18.0, Variant::Compact)
                .primary(true)
                .description("The fixed reduction applied when a Static ducking trigger exceeds its threshold.");
            let ratio = Spec::new("help-ratio", "Ratio", 1.0, 20.0, 0.1, ":1", 4.0, Variant::Compact)
                .primary(true)
                .description("How strongly Dynamic ducking reduces audio above its trigger threshold.");
            let (spec, value, state) = if self.mode == 1 { (&ratio, &mut self.ratio, &mut self.ratio_state) } else { (&amount, &mut self.amount, &mut self.amount_state) };
            let (area, _) = ui.allocate_exact_size(Vec2::new(ui.available_width(), dial::tile_height(spec)), Sense::hover());
            let outcome = dial::tile(ui, Id::unique(("help-mode-dial", self.mode)), spec, *value, spec.default, state, area.min + Vec2::new(92.0, 0.0), 136.0, true);
            if let Some(next) = outcome.changed {
                *value = next;
            }
            ui.add_space(6.0);
            hint(ui, "Alt+Down opens the list. Arrow keys choose, Enter confirms, Escape closes.");
        });
        section(ui, "Menus", "Open Preferences and change a checkmark. Hover an item for its help.", |ui| {
            let response = toolbar_button(ui, Id::unique("help-example-menu"), "Example menu", "Example menu");
            if response.clicked() {
                self.menu_open = !self.menu_open;
                self.menu_anchor = response.rect.left_bottom();
            }
            ui.add_space(6.0);
            hint(ui, "Arrow keys navigate. Ctrl+Shift+F10 toggles help for the focused item. Escape closes the menu.");
            if self.menu_open {
                self.example_menu(ui, response.rect);
            }
        });
    }

    fn example_menu(&mut self, ui: &mut Ui, button: Rect) {
        let ctx = ui.ctx().clone();
        let mut close = false;
        let area = egui::Area::new(Id::unique("help-example-menu-popup"))
            .order(egui::Order::Foreground)
            .fixed_pos(self.menu_anchor)
            .constrain(true)
            .show(&ctx, |ui| {
                widgets::menu_frame().show(ui, |ui| {
                    widgets::menu_body(ui, 0.0);
                    widgets::submenu(ui, "Preferences", true, "Open a submenu. Checked items are enabled.", |ui| {
                        widgets::menu_check(ui, &mut self.reduce_motion, "Reduce meter animation", true, "Use less movement in meters.");
                        widgets::menu_check(
                            ui,
                            &mut self.example_pan,
                            "Right-click drag to pan",
                            true,
                            "Drag empty canvas with the right button to pan; release without dragging for its menu.",
                        );
                    });
                    widgets::menu_separator(ui);
                    if widgets::menu_item(ui, "Reset buttons", "", true, "Turn Mute, Bypass and Mono off.") {
                        self.muted = false;
                        self.bypassed = false;
                        self.mono = false;
                        close = true;
                    }
                    widgets::menu_item(ui, "Unavailable action", "", false, "This action needs a connected device. Its disabled state prevents an unavailable operation.");
                });
            });
        let outside = ctx.input(|i| {
            i.pointer.any_pressed() && i.pointer.interact_pos().is_some_and(|p| !area.response.rect.contains(p) && !button.contains(p))
        });
        let in_submenu = ctx
            .input(|i| i.pointer.interact_pos())
            .and_then(|p| ctx.layer_id_at(p))
            .is_some_and(|l| matches!(l.order, egui::Order::Foreground | egui::Order::Tooltip) && l.id != area.response.layer_id.id);
        if close || (outside && !in_submenu) {
            self.menu_open = false;
        }
    }
}

// ── Search & results ───────────────────────────────────────────────────────

struct SearchLesson {
    fixture: AudioGraph,
    search: NodeSearchState,
    chosen: Option<String>,
}

impl Default for SearchLesson {
    fn default() -> Self {
        let mut nodes = Vec::new();
        let mut node = |id: &str, kind: &str, name: &str| {
            let mut n = GraphNode::new(id, kind, name, 0.0, 0.0);
            n.device = DeviceBinding::default();
            nodes.push(n);
        };
        node("help-mic", "microphone", "Voice");
        node("help-music", "appInput", "Music");
        node("help-eq", "eq", "Voice EQ");
        node("help-duck", "ducking", "Music ducking");
        node("help-mix", "mix", "Main mix");
        node("help-out", "output", "Headphones");
        nodes[0].device = DeviceBinding { id: "example-microphone".into(), stable_id: String::new(), name: "Practice microphone".into(), channel: -1 };
        nodes[1].app = ProcessBinding { process_id: 0, executable_path: "practice-only".into(), name: "Example music app".into() };
        nodes[5].device = DeviceBinding { id: "example-headphones".into(), stable_id: String::new(), name: "Practice headset".into(), channel: -1 };
        let fixture = AudioGraph { nodes, ..Default::default() };
        let mut search = NodeSearchState::default();
        search.begin();
        Self { fixture, search, chosen: None }
    }
}

impl SearchLesson {
    fn show(&mut self, ui: &mut Ui) {
        section(ui, "", "Search Voice, EQ, headset or music in these six sample nodes.", |ui| {
            let width = ui.available_width();
            let (rect, _) = ui.allocate_exact_size(Vec2::new(width, 300.0), Sense::hover());
            match node_search::show(ui, Id::unique("help-search"), rect, &mut self.search, &self.fixture, false, node_search::Buttons::Lesson) {
                node_search::Outcome::Choose(id) => self.chosen = Some(id),
                node_search::Outcome::Close => {
                    self.search.clear();
                    self.chosen = None;
                }
                node_search::Outcome::None => {}
            }
            ui.add_space(6.0);
            let selection = match self.chosen.as_ref().and_then(|id| self.fixture.nodes.iter().find(|n| &n.id == id)) {
                Some(node) => format!("Selected: {}", node.name),
                None => "No node selected.".into(),
            };
            hint(ui, &selection);
        });
        section(ui, "Search controls", "Names, node types, devices and apps are searchable. Multiple words narrow the results.", |ui| {
            instruction(ui, "Up / Down", "Choose a result.");
            instruction(ui, "Enter / double-click", "Select the result, or use Go to node.");
            instruction(ui, "Ctrl+F", "Find a node in the workspace and frame it.");
            instruction(ui, "Shift+A / F3", "Search the node library to add a new node.");
        });
    }
}

// ── Equalizer ──────────────────────────────────────────────────────────────

struct EqualizerLesson {
    node: GraphNode,
    runtime: super::node_view::Runtime,
}

impl Default for EqualizerLesson {
    fn default() -> Self {
        let mut node = GraphNode::new("help-equalizer", "eq", "Practice EQ", 0.0, 0.0);
        node.eq_bands = vinemix_model::config::default_eq_bands();
        let mut runtime = super::node_view::Runtime::default();
        runtime.eq.select_only(Some(2), node.eq_bands.len());
        Self { node, runtime }
    }
}

impl EqualizerLesson {
    fn show(&mut self, ui: &mut Ui) {
        let width = ui.available_width();
        let details_height = 139.0;
        let (area, _) = ui.allocate_exact_size(Vec2::new(width, 260.0 + 8.0 + details_height), Sense::hover());
        let graph = AudioGraph::default();
        let env = super::node_view::Env {
            graph: &graph,
            endpoints: &[],
            applications: &[],
            visibility: crate::availability::Visibility::default(),
            can_edit: true,
            preview: true,
            layout_locked: false,
            reduced_animation: false,
            now: ui.input(|i| i.time),
            meter_sequence: 0,
            audio_frames: 0,
            acknowledged: None,
        };
        let view = super::node_view::StatusView::default();
        let mut actions = Vec::new();
        let node = self.node.clone();
        {
            let mut ctx = super::node_view::ItemCtx {
                ui,
                cam: super::node_view::Camera { origin: Pos2::ZERO, pan: Vec2::ZERO, zoom: 1.0 },
                node: &node,
                status: None,
                origin: area.min,
                env: &env,
                runtime: &mut self.runtime,
                actions: &mut actions,
                id: Id::unique("help-equalizer"),
                view: &view,
                notice: None,
                s: 1.0,
            };
            let plot = Rect::from_min_size(Pos2::ZERO, Vec2::new(width, 260.0));
            let details = Rect::from_min_size(Pos2::new(0.0, 268.0), Vec2::new(width.min(3.0 * 144.0), details_height));
            super::eq::show(&mut ctx, plot, details);
        }
        for action in actions {
            if let super::node_view::Action::Edit(_, edit) = action {
                edit(&mut self.node);
            }
        }
        ui.add_space(6.0);
        hint(ui, "Drag a point; hold Shift for fine movement. Ctrl-click selects several bands.");
        hint(ui, "Double-click empty space to add a band. Right-click a point for shape and band actions.");
        hint(ui, "The dials edit the active band. Click a value to type it; a Notch has no gain control.");
        ui.add_space(16.0);
    }
}

// ── Meters & status ────────────────────────────────────────────────────────

struct FeedbackLesson {
    state: usize,
    meter: Meter,
}

impl Default for FeedbackLesson {
    fn default() -> Self {
        let mut lesson = Self { state: 0, meter: Meter::default() };
        lesson.apply();
        lesson
    }
}

impl FeedbackLesson {
    /// A static illustration: fresh peak history, timers never advanced.
    fn apply(&mut self) {
        let selected = self.state;
        let quiet = matches!(selected, 2 | 3);
        let left = if selected == 1 { 1.0 } else { 10f64.powf(-18.0 / 20.0) };
        let right = if selected == 1 { 0.75 } else { 10f64.powf(-12.0 / 20.0) };
        self.meter = Meter::default();
        self.meter.update(0.0, 0.0, 2, false, true, true, 0.0, true);
        self.meter.update(left, right, 2, selected == 1, quiet, true, 0.0, true);
    }

    fn show(&mut self, ui: &mut Ui) {
        section(ui, "", "Static sample values.", |ui| {
            let labels = choices(&["Healthy level", "Clipping", "Muted", "Device unavailable", "Processing with reduction", "Bypassed", "Waiting for trigger"]);
            if let Some(index) = selector_row(ui, Id::unique("help-feedback"), "Display", &labels, self.state, "Illustrative feedback state") {
                if index != self.state {
                    self.state = index;
                    self.apply();
                }
            }
            let selected = self.state;
            let quiet = matches!(selected, 2 | 3);
            let status = match selected {
                1 => "Clipping · lower the level",
                2 => "Muted",
                3 => "Practice microphone · disconnected",
                4 => "Processing · 6.0 dB reduction",
                5 => "Bypassed · audio passes through",
                6 => "Waiting for trigger",
                _ => "Connected",
            };
            let detector = match selected {
                4 => "Detector −24.0 dBFS\nThreshold −30.0 dBFS",
                6 => "Trigger — dBFS\nReduction 0.0 dB",
                _ => "Stereo",
            };
            let peak = if quiet {
                "No peak readings."
            } else if selected == 1 {
                "Left 0.0 dBFS; overload detected"
            } else {
                "Left −18.0 dBFS; right −12.0 dBFS"
            };
            let explanation = match selected {
                1 => "The lit cap confirms an overload. A reading of 0 dBFS alone does not prove clipping. This static sample holds the cap on.",
                2 => "Muted deliberately; the device remains connected.",
                3 => "The saved assignment stays in place until the device reconnects or you choose another.",
                4 => "Above threshold, with 6 dB of reduction. The strip shows the reduction amount.",
                5 => "Processing and level adjustment are skipped. Settings and connections remain in place.",
                6 => "Connect a source to the Trigger socket. A missing or silent trigger releases reduction toward zero.",
                _ => "Hover the meter to inspect the highest levels recorded for each channel.",
            };
            let width = ui.available_width();
            let readout_width = width - 28.0 - 20.0;
            let status_color = if matches!(selected, 1 | 3) { theme::ALERT_TEXT } else { theme::TEXT };
            let status_g = galley(ui, status, 13.0, Weight::SemiBold, status_color, readout_width);
            let detector_g = galley(ui, detector, 13.0, Weight::Regular, theme::TEXT, readout_width);
            let peak_g = galley(ui, peak, 13.0, Weight::Regular, theme::TEXT, readout_width);
            let explanation_g = galley(ui, explanation, 13.0, Weight::Regular, theme::MUTED, readout_width);
            let readouts = status_g.size().y + 6.0 + detector_g.size().y + 6.0 + peak_g.size().y + 6.0 + 4.0 + 3.0 + 12.0 + explanation_g.size().y + 6.0;
            let height = (140.0f32 + 12.0).max(readouts);
            let (rect, _) = ui.allocate_exact_size(Vec2::new(width, height), Sense::hover());
            let meter_rect = Rect::from_min_size(rect.min, Vec2::new(28.0, 140.0));
            meter::paint(ui.painter(), meter_rect, &self.meter, 1.0);
            let meter_response = ui.interact(meter_rect, Id::unique("help-meter"), Sense::hover());
            let lines = self.meter.peak_lines();
            if !lines.is_empty() {
                meter_response.tip(format!("Audio peaks\n{}", lines.join("\n")));
            }
            let x = rect.left() + 28.0 + 20.0;
            let mut y = rect.top();
            let painter = ui.painter();
            let status_rect = Rect::from_min_size(Pos2::new(x, y), status_g.size());
            painter.galley(Pos2::new(x, y), status_g, status_color);
            y += status_rect.height() + 6.0;
            let detector_rect = Rect::from_min_size(Pos2::new(x, y), detector_g.size());
            painter.galley(Pos2::new(x, y), detector_g, theme::TEXT);
            y += detector_rect.height() + 6.0;
            let peak_height = peak_g.size().y;
            painter.galley(Pos2::new(x, y), peak_g, theme::TEXT);
            y += peak_height + 6.0 + 4.0;
            let strip = Rect::from_min_size(Pos2::new(x, y), Vec2::new(180.0, 3.0));
            painter.rect_filled(strip, 0.0, theme::METER_UNLIT);
            if selected == 4 {
                painter.rect_filled(Rect::from_min_size(strip.min, Vec2::new(54.0, 3.0)), 0.0, theme::A);
            }
            y += 3.0 + 12.0;
            painter.galley(Pos2::new(x, y), explanation_g, theme::MUTED);
            ui.interact(status_rect, Id::unique("help-feedback-status"), Sense::hover()).tip(format!("Sample status\n{status}\n{explanation}"));
            ui.interact(detector_rect, Id::unique("help-feedback-detector"), Sense::hover()).tip(format!("Sample dynamics reading\n{detector}"));
        });
        section(ui, "Reading the display", "", |ui| {
            instruction(ui, "Meter lanes", "Separate left and right sample peaks.");
            instruction(ui, "Yellow / red", "Above −12 dBFS / above −3 dBFS.");
            instruction(ui, "Lit top cap", "An overload occurred. Holds for two seconds, then fades for one second.");
            instruction(ui, "Peak tooltip", "Highest observed levels. Clicking the meter does not clear them.");
            instruction(ui, "Detector / reduction", "Read-only feedback from dynamics processing.");
            instruction(ui, "Device warning", "Hover the highlighted device name for recovery guidance. Silence alone is not a missing-device warning.");
        });
    }
}
