//! The Settings menu (`MainWindow.CreateSettingsMenu`) with its search
//! (`SettingsSearchView`): typing filters the real rows and lists matching
//! control instructions; nothing runs until a row or result is chosen.

use eframe::egui::{self, Id, Key, Pos2, Rect, Sense, Stroke, Ui, Vec2};

use super::App;
use crate::ui::help;
use crate::ui::theme::{self, Weight};
use crate::ui::widgets::{self, Tip as _};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Act {
    Find,
    Activity,
    Sound,
    Retry,
    Restart,
    Updates,
    Help,
    WhatsNew,
    About,
    Quit,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Pref {
    RightPan,
    Minimap,
    Reduced,
    Notifications,
    Disabled,
    Disconnected,
    Advanced,
    Startup,
    AutoUpdates,
}

enum Row {
    Command { label: &'static str, gesture: &'static str, tip: &'static str, help: &'static str, enabled: bool, act: Act },
    Check { label: &'static str, tip: &'static str, help: &'static str, enabled: bool, pref: Pref },
    Sub { label: &'static str, tip: &'static str, help: &'static str, children: Vec<Row> },
    Separator,
}

/// Extra search words for some rows (`SettingsSearchView.Aliases`).
fn aliases(label: &str) -> &'static str {
    match label {
        "Right-click drag to pan" => "canvas navigation mouse panning",
        "Show minimap" => "canvas overview navigation zoom map panning",
        "Reduce meter animation" => "motion accessibility meters",
        "Audio device visibility" => "filters hidden inputs outputs",
        "Start VineMix when I sign in" => "startup windows login launch automatic",
        "Windows sound settings" => "audio hardware system devices",
        "Retry assigned devices" => "reconnect refresh recovery devices",
        "Restart audio engine" => "restart service reconnect recovery",
        _ => "",
    }
}

/// Lower-case with combining marks removed (`SettingsSearchView.Normalize`).
fn normalize(value: &str) -> String {
    value.chars().filter(|c| !('\u{0300}'..='\u{036F}').contains(c)).flat_map(char::to_lowercase).collect()
}

const ACTIVITY_HELP: &str = "Recent workspace edits and messages from this session.";
const VISIBILITY_HELP: &str = "Choose which extra audio devices appear in the input and output lists. Your assigned device always remains visible. These preferences do not change Windows devices or audio routing.";
const RESTART_HELP: &str = "Briefly interrupts audio, then reconnects your existing routes. Your workspace stays open.";
const WHATS_NEW_HELP: &str = "Read the bundled release highlights.";
const QUIT_HELP: &str = "Save your workspace and stop audio.";

fn rows(can_edit: bool, can_restart: bool) -> Vec<Row> {
    use Row::{Check, Command, Separator, Sub};
    vec![
        Command { label: "Find node…", gesture: "Ctrl+F", tip: "Find a node by name, type, device or app.", help: "", enabled: true, act: Act::Find },
        Command { label: "Activity", gesture: "", tip: ACTIVITY_HELP, help: ACTIVITY_HELP, enabled: true, act: Act::Activity },
        Check {
            label: "Right-click drag to pan",
            tip: "Right-click drag to pan\nEnables right-button canvas panning",
            help: "Right-drag empty canvas to pan; release without dragging for its menu.",
            enabled: true,
            pref: Pref::RightPan,
        },
        Check {
            label: "Show minimap",
            tip: "Show minimap\nShows the canvas overview in the bottom-right corner.",
            help: "Show the canvas overview in the bottom-right corner. Click it to centre the view or drag to pan.",
            enabled: true,
            pref: Pref::Minimap,
        },
        Check {
            label: "Reduce meter animation",
            tip: "Reduce meter animation\nUse less motion in audio meters.",
            help: "Use less motion in audio meters.",
            enabled: true,
            pref: Pref::Reduced,
        },
        Check {
            label: "Show action notifications",
            tip: "Show action notifications\nShow brief messages after workspace actions.",
            help: "Show brief messages after workspace actions.",
            enabled: true,
            pref: Pref::Notifications,
        },
        Separator,
        Sub {
            label: "Audio device visibility",
            tip: "Audio device visibility\nChooses which extra devices appear in device lists. Routing is not changed.",
            help: VISIBILITY_HELP,
            children: vec![
                Check {
                    label: "Show disabled devices",
                    tip: "Show disabled devices\nLists devices disabled in Windows. They must be enabled there before use.",
                    help: "Show disabled Windows devices. Enable them in Windows sound settings before using them. This only changes list visibility.",
                    enabled: true,
                    pref: Pref::Disabled,
                },
                Check {
                    label: "Show disconnected or missing devices",
                    tip: "Show disconnected or missing devices\nLists unplugged or missing devices. Nothing is reconnected or rerouted.",
                    help: "Show unplugged or missing devices. This does not reconnect devices or change audio routing.",
                    enabled: true,
                    pref: Pref::Disconnected,
                },
                Check {
                    label: "Show advanced inputs and outputs",
                    tip: "Show advanced inputs and outputs\nLists line-level, telephone and S/PDIF or digital passthrough devices.",
                    help: "Show line-level inputs, telephone endpoints, and S/PDIF or digital passthrough devices. Ordinary and streamed microphones, speakers, headphones and HDMI outputs remain visible; unclassified active devices also remain visible. Availability controls still apply.",
                    enabled: true,
                    pref: Pref::Advanced,
                },
            ],
        },
        Command { label: "Windows sound settings", gesture: "", tip: "Open the Windows audio device settings.", help: "", enabled: true, act: Act::Sound },
        Command {
            label: "Retry assigned devices",
            gesture: "",
            tip: "Reconnect the audio devices already assigned to your nodes.",
            help: "",
            enabled: can_edit,
            act: Act::Retry,
        },
        Command { label: "Restart audio engine", gesture: "", tip: "Restart audio engine\nBriefly interrupts audio, then reconnects your routes.", help: RESTART_HELP, enabled: can_restart, act: Act::Restart },
        Separator,
        Check {
            label: "Start VineMix when I sign in",
            tip: "Start VineMix when I sign in\nAutomatically start the mixer when you sign in to Windows.",
            help: "Automatically start the mixer when you sign in to Windows.",
            enabled: can_edit,
            pref: Pref::Startup,
        },
        Command {
            label: "Check for updates",
            gesture: "",
            tip: "Check GitHub for a newer verified VineMix release.",
            help: "Check for a newer version. Installation only starts after you choose Install and restart; audio briefly stops then.",
            enabled: true,
            act: Act::Updates,
        },
        Check {
            label: "Automatically check for updates",
            tip: "Automatically check for updates\nChecks daily. You choose when to download and install.",
            help: "Check daily while VineMix is running, with automatic retries after failed checks. Turning this on checks shortly. Downloads and installation require your choice.",
            enabled: true,
            pref: Pref::AutoUpdates,
        },
        Separator,
        Command {
            label: "Help & shortcuts",
            gesture: "F1",
            tip: "Keyboard shortcuts and silent control practice.",
            help: "Open shortcuts and practice controls. Escape first cancels an active example, then closes Help.",
            enabled: true,
            act: Act::Help,
        },
        Command { label: "What's new?", gesture: "", tip: WHATS_NEW_HELP, help: WHATS_NEW_HELP, enabled: true, act: Act::WhatsNew },
        Command { label: "About VineMix", gesture: "", tip: "Version and build information to include in a bug report.", help: "", enabled: true, act: Act::About },
        Separator,
        Command { label: "Quit VineMix", gesture: "", tip: "Quit VineMix\nSave your workspace and stop audio.", help: QUIT_HELP, enabled: true, act: Act::Quit },
    ]
}

/// Whether `row` (or, for a submenu, any child) matches every term.
fn visible(row: &Row, terms: &[String], parent_matches: bool) -> bool {
    let (label, tip, help, children) = match row {
        Row::Command { label, tip, help, .. } | Row::Check { label, tip, help, .. } => (*label, *tip, *help, None),
        Row::Sub { label, tip, help, children } => (*label, *tip, *help, Some(children)),
        Row::Separator => return terms.is_empty(),
    };
    if terms.is_empty() {
        return true;
    }
    let text = normalize(&[label, help, tip, aliases(label)].join(" "));
    let own = parent_matches || terms.iter().all(|t| text.contains(t.as_str()));
    let children_match = children.is_some_and(|c| c.iter().any(|r| visible(r, terms, own)));
    own || children_match
}

/// Persistent search state for one opening of the menu.
#[derive(Debug, Default)]
pub(super) struct SettingsSearch {
    pub query: String,
    selected: usize,
    focus: bool,
}

impl SettingsSearch {
    pub fn reset(&mut self) {
        self.query.clear();
        self.selected = 0;
        self.focus = true;
    }
}

impl App {
    fn pref(&self, pref: Pref) -> bool {
        let mixer = self.link.state();
        match pref {
            Pref::RightPan => mixer.right_click_drag_to_pan(),
            Pref::Minimap => mixer.show_minimap(),
            Pref::Reduced => mixer.reduced_animation(),
            Pref::Notifications => mixer.show_action_notifications(),
            Pref::Disabled => mixer.device_visibility().disabled,
            Pref::Disconnected => mixer.device_visibility().disconnected,
            Pref::Advanced => mixer.device_visibility().advanced,
            Pref::Startup => mixer.start_at_login(),
            Pref::AutoUpdates => mixer.automatically_check_for_updates(),
        }
    }

    fn set_pref(&mut self, pref: Pref, value: bool) {
        let mut mixer = self.link.state();
        match pref {
            Pref::RightPan => mixer.set_right_click_drag_to_pan(value),
            Pref::Minimap => mixer.set_show_minimap(value),
            Pref::Reduced => mixer.set_reduced_animation(value),
            Pref::Notifications => {
                mixer.set_show_action_notifications(value);
                drop(mixer);
                if !value {
                    self.canvas.dismiss_unless_error();
                }
            }
            Pref::Disabled | Pref::Disconnected | Pref::Advanced => {
                let mut visibility = mixer.device_visibility();
                match pref {
                    Pref::Disabled => visibility.disabled = value,
                    Pref::Disconnected => visibility.disconnected = value,
                    _ => visibility.advanced = value,
                }
                mixer.set_device_visibility(visibility);
            }
            Pref::Startup => mixer.set_start_at_login(value),
            Pref::AutoUpdates => {
                mixer.set_automatically_check_for_updates(value);
                drop(mixer);
                self.updates.preference_changed(value);
            }
        }
    }

    /// Draws `rows`; returns the chosen command, if any.
    fn draw_rows(&mut self, ui: &mut Ui, rows: &[Row], terms: &[String], parent_matches: bool) -> Option<Act> {
        let mut chosen = None;
        for row in rows {
            if !visible(row, terms, parent_matches) {
                continue;
            }
            match row {
                Row::Separator => widgets::menu_separator(ui),
                Row::Command { label, gesture, tip, enabled, act, .. } => {
                    let pending = (*act == Act::Updates).then(|| self.updates.available_version()).flatten();
                    let (label, color) = match &pending {
                        Some(version) => (format!("Update available · {version}"), Some(theme::A)),
                        None => ((*label).to_owned(), (*act == Act::Quit).then_some(theme::MUTE)),
                    };
                    if widgets::menu_item_styled(ui, &label, gesture, *enabled, tip, color, None) {
                        chosen = Some(*act);
                    }
                }
                Row::Check { label, tip, enabled, pref, .. } => {
                    let mut value = self.pref(*pref);
                    if widgets::menu_check(ui, &mut value, label, *enabled, tip) {
                        self.set_pref(*pref, value);
                    }
                }
                Row::Sub { label, tip, help, children } => {
                    let text = normalize(&[*label, *help, *tip, aliases(label)].join(" "));
                    let own = parent_matches || (!terms.is_empty() && terms.iter().all(|t| text.contains(t.as_str())));
                    if let Some(Some(act)) = widgets::submenu(ui, label, true, tip, |ui| self.draw_rows(ui, children, terms, own)) {
                        chosen = Some(act);
                    }
                }
            }
        }
        chosen
    }

    pub(super) fn settings_menu(&mut self, ui: &mut Ui, anchor: Rect) {
        let ctx = ui.ctx().clone();
        let (can_edit, can_restart) = {
            let mixer = self.link.state();
            (mixer.can_edit(), mixer.can_restart_engine())
        };
        let all = rows(can_edit, can_restart);
        let mut chosen: Option<Act> = None;
        let mut open_entry: Option<&'static str> = None;
        let mut escape_handled = false;
        let area = egui::Area::new(Id::unique("settings-menu"))
            .order(egui::Order::Foreground)
            .fixed_pos(Pos2::new(anchor.right() - 380.0, anchor.bottom()))
            .constrain(true)
            .show(&ctx, |ui| {
                // The area remembers the size of its last contents. After a
                // search with few results that is a short menu, and a scroll
                // area can only use the room its parent offers: without this
                // the full list came back inside a two-row viewport. Offer
                // the whole space below the title bar on every frame; the
                // menu still sizes itself to its contents.
                let room = (ctx.content_rect().bottom() - anchor.bottom() - 8.0).max(120.0);
                ui.set_max_height(room);
                widgets::menu_frame().show(ui, |ui| {
                    widgets::menu_body(ui, 376.0);
                    ui.set_max_width(376.0);
                    ui.set_max_height(room - 6.0);
                    // Search field (6,6,6,5 margin; 30 px; 8,4 padding).
                    let query_before = self.settings_search.query.clone();
                    let field = egui::Frame::new().inner_margin(egui::Margin { left: 6, right: 6, top: 6, bottom: 5 }).show(ui, |ui| {
                        ui.add(
                            egui::TextEdit::singleline(&mut self.settings_search.query)
                                .id(Id::unique("settings-search"))
                                .hint_text("Search settings and help")
                                .desired_width(f32::INFINITY)
                                .min_size(Vec2::new(0.0, 30.0))
                                .margin(egui::Margin::symmetric(8, 4))
                                // The text line is shorter than the 30-point
                                // field: centre it, so the caret, the
                                // placeholder and typed text sit mid-field
                                // instead of 2.5 points high.
                                .align(egui::Align2::LEFT_CENTER)
                                .font(theme::font(13.0, Weight::Regular)),
                        )
                        .tip_described(
                            "Search settings and help\nFinds a setting or the instructions for a control.",
                            "Finds a setting or the instructions for a control. Try zoom, devices or dials. Up and Down choose a help result; Enter opens its full instructions. Escape clears your search.",
                        )
                    });
                    let search = field.inner;
                    if std::mem::take(&mut self.settings_search.focus) {
                        search.request_focus();
                    }
                    if search.has_focus() {
                        // Escape with a query clears the query (below). egui
                        // otherwise takes focus away on Escape before this
                        // code runs, and the menu closed instead.
                        let escape = !self.settings_search.query.is_empty();
                        ui.memory_mut(|m| {
                            m.set_focus_lock_filter(search.id, egui::EventFilter { tab: false, horizontal_arrows: true, vertical_arrows: true, escape });
                        });
                    }
                    if self.settings_search.query != query_before {
                        self.settings_search.selected = 0;
                    }
                    widgets::menu_separator(ui);
                    let query = self.settings_search.query.trim().to_owned();
                    let terms: Vec<String> = normalize(&query).split_whitespace().map(str::to_owned).collect();
                    let searching = !terms.is_empty();
                    let matches = if searching { help::search(&query) } else { Vec::new() };
                    if search.has_focus() {
                        let (up, down, enter, escape) =
                            ui.input(|i| (i.key_pressed(Key::ArrowUp), i.key_pressed(Key::ArrowDown), i.key_pressed(Key::Enter), i.key_pressed(Key::Escape)));
                        if !matches.is_empty() {
                            let last = matches.len() - 1;
                            if down {
                                self.settings_search.selected = (self.settings_search.selected + 1).min(last);
                            }
                            if up {
                                self.settings_search.selected = self.settings_search.selected.saturating_sub(1);
                            }
                        }
                        if enter {
                            open_entry = matches.get(self.settings_search.selected).map(|e| e.id);
                        }
                        if escape && !self.settings_search.query.is_empty() {
                            self.settings_search.query.clear();
                            self.settings_search.focus = true;
                            escape_handled = true;
                        }
                    }
                    let any_setting = all.iter().any(|r| !matches!(r, Row::Separator) && visible(r, &terms, false));
                    // What is left of the offered room after the search
                    // field and separator; the list scrolls only when the
                    // window is too small to show it all.
                    let list_room = (ctx.content_rect().bottom() - ui.cursor().top() - 14.0).max(84.0);
                    egui::ScrollArea::vertical().max_height(list_room).show(ui, |ui| {
                        chosen = self.draw_rows(ui, &all, &terms, false);
                        if searching && any_setting && !matches.is_empty() {
                            widgets::menu_separator(ui);
                        }
                        if searching && !any_setting && matches.is_empty() {
                            egui::Frame::new().inner_margin(egui::Margin::symmetric(12, 10)).show(ui, |ui| {
                                ui.add(
                                    egui::Label::new(
                                        egui::RichText::new("No matching settings or controls. Try zoom, devices or dials.")
                                            .font(theme::font(13.0, Weight::Regular))
                                            .color(theme::MUTED),
                                    )
                                    .wrap(),
                                );
                            });
                        }
                        if !matches.is_empty() {
                            if let Some(entry) = self.search_results(ui, &matches) {
                                open_entry = Some(entry);
                            }
                        }
                    });
                });
            });
        let outside = ctx.input(|i| i.pointer.any_pressed() && i.pointer.interact_pos().is_some_and(|p| !area.response.rect.contains(p) && !anchor.contains(p)));
        let in_submenu = ctx
            .input(|i| i.pointer.interact_pos())
            .and_then(|p| ctx.layer_id_at(p))
            .is_some_and(|l| l.order == egui::Order::Foreground && l.id != area.response.layer_id.id);
        let escape = !escape_handled && ctx.input(|i| i.key_pressed(Key::Escape));
        if chosen.is_some() || open_entry.is_some() || (outside && !in_submenu) || escape {
            self.settings_open = false;
        }
        if let Some(entry) = open_entry {
            self.open_help(Some(entry));
        }
        if let Some(act) = chosen {
            self.run(&ctx, act);
        }
    }

    /// "Controls & shortcuts" results (`SettingsSearchView.Results`).
    fn search_results(&mut self, ui: &mut Ui, matches: &[&'static help::Entry]) -> Option<&'static str> {
        let mut open = None;
        egui::Frame::new().inner_margin(egui::Margin { left: 6, right: 6, top: 3, bottom: 4 }).show(ui, |ui| {
            egui::Frame::new().inner_margin(egui::Margin { left: 6, right: 6, top: 3, bottom: 3 }).show(ui, |ui| {
                ui.label(egui::RichText::new("Controls & shortcuts").font(theme::font(13.0, Weight::Medium)).color(theme::MUTED));
            });
            egui::ScrollArea::vertical().id_salt("settings-results").max_height(260.0).show(ui, |ui| {
                ui.spacing_mut().item_spacing = Vec2::ZERO;
                for (index, entry) in matches.iter().enumerate() {
                    let width = ui.available_width();
                    let text_width = width - 14.0;
                    let keys_galley = ui.fonts_mut(|f| f.layout_no_wrap(entry.keys.to_owned(), theme::font(11.0, Weight::Mono), theme::MUTED));
                    let title_width = (text_width - keys_galley.size().x - 10.0).max(60.0);
                    let title = ui.fonts_mut(|f| f.layout(entry.title.to_owned(), theme::font(13.0, Weight::Medium), theme::TEXT, title_width));
                    let description = ui.fonts_mut(|f| f.layout(entry.description.to_owned(), theme::font(12.0, Weight::Regular), theme::MUTED, text_width));
                    let height = 6.0 + title.size().y.max(keys_galley.size().y + 1.0) + 2.0 + description.size().y + 7.0;
                    let (rect, response) = ui.allocate_exact_size(Vec2::new(width, height), Sense::click());
                    let selected = index == self.settings_search.selected;
                    let painter = ui.painter();
                    if response.hovered() {
                        painter.rect_filled(rect, 0.0, theme::BUTTON_HOVER);
                    } else if selected {
                        painter.rect_filled(rect, 0.0, theme::BUTTON_FACE);
                    }
                    if selected {
                        painter.rect_filled(Rect::from_min_size(rect.min, Vec2::new(2.0, rect.height())), 0.0, theme::MUTED);
                    }
                    let left = rect.left() + 2.0 + 7.0;
                    painter.galley(Pos2::new(rect.right() - 7.0 - keys_galley.size().x, rect.top() + 7.0), keys_galley, theme::MUTED);
                    let title_height = title.size().y;
                    painter.galley(Pos2::new(left, rect.top() + 6.0), title, theme::TEXT);
                    painter.galley(Pos2::new(left, rect.top() + 6.0 + title_height + 2.0), description, theme::MUTED);
                    if response.clicked() {
                        self.settings_search.selected = index;
                    }
                    if response.double_clicked() {
                        open = Some(entry.id);
                    }
                    if selected && response.has_focus() {
                        painter.rect_stroke(rect, 0.0, Stroke::new(1.0, theme::FOCUS), egui::StrokeKind::Inside);
                    }
                }
            });
        });
        open
    }

    fn run(&mut self, ctx: &egui::Context, act: Act) {
        match act {
            Act::Find => self.run_action(ctx, "find"),
            Act::Activity => self.run_action(ctx, "activity"),
            Act::Sound => {
                let _ = crate::platform::shell_open("ms-settings:sound");
            }
            Act::Retry => self.run_action(ctx, "retry"),
            Act::Restart => self.run_action(ctx, "restart"),
            Act::Updates => self.run_action(ctx, "updates"),
            Act::Help => self.open_help(None),
            Act::WhatsNew => self.run_action(ctx, "whatsnew"),
            Act::About => self.run_action(ctx, "about"),
            Act::Quit => self.run_action(ctx, "quit"),
        }
    }
}
