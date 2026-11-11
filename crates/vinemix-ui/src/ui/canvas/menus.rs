//! Canvas context menus, the node library and the activity list.

use crate::ui::widgets::Tip as _;
use eframe::egui::{self, Id, Pos2, Ui};

use super::{Canvas, Menu};
use crate::editing;
use crate::frames;
use crate::graph::{self, catalog};
use crate::mixer::{MixerState, LAYOUT_LOCKED_MESSAGE};
use crate::ui::{theme, widgets};

/// A menu row; returns true when activated.
pub fn item(ui: &mut Ui, label: &str, shortcut: &str, enabled: bool, help: &str) -> bool {
    widgets::menu_item(ui, label, shortcut, enabled, help)
}

impl Canvas {
    pub fn open_node_menu(&mut self, mixer: &mut MixerState, id: &str, pointer: Pos2) {
        self.selected_frame = None;
        if !self.selected.iter().any(|x| x == id) {
            self.select_node(mixer, id);
        }
        self.bring_to_front(&[id.to_owned()], None);
        let world = self.camera().to_world(pointer);
        self.menu = Some((Menu::Node(id.to_owned(), world), pointer));
        self.menu_opened = true;
    }

    pub fn open_add_menu(&mut self, mixer: &mut MixerState, anchor: Pos2, world: Pos2) {
        if !self.require_layout_edit(mixer) {
            return;
        }
        self.add_search.clear();
        self.menu = Some((Menu::Add(world), anchor));
        self.menu_opened = true;
    }

    pub fn open_history(&mut self, anchor: Pos2) {
        self.menu = Some((Menu::History, anchor));
        self.menu_opened = true;
    }

    pub fn close_menu(&mut self) {
        self.menu = None;
        self.pipeline_choices.clear();
    }

    pub fn menu_open(&self) -> bool {
        self.menu.is_some()
    }

    pub(super) fn show_menus(&mut self, ui: &mut Ui, mixer: &mut MixerState) {
        let Some((menu, anchor)) = self.menu.clone() else { return };
        let ctx = ui.ctx().clone();
        let just_opened = std::mem::take(&mut self.menu_opened);
        let mut close = false;
        let area = egui::Area::new(Id::unique("vinemix-canvas-menu"))
            .order(egui::Order::Foreground)
            .fixed_pos(anchor)
            .constrain(true)
            .show(&ctx, |ui| {
                widgets::menu_frame().show(ui, |ui| {
                    let width = match menu {
                        Menu::Add(_) => 296.0,
                        Menu::History => 436.0,
                        _ => 0.0,
                    };
                    widgets::menu_body(ui, width);
                    if width > 0.0 {
                        ui.set_max_width(width);
                    }
                    close = match &menu {
                        Menu::Canvas(world) => self.canvas_menu(ui, mixer, *world),
                        Menu::Node(id, world) => self.node_menu(ui, mixer, id, *world),
                        Menu::Edge(id) => self.edge_menu(ui, mixer, id),
                        Menu::Frame(id) => self.frame_menu(ui, mixer, id),
                        Menu::Add(world) => self.add_menu(ui, mixer, *world, just_opened),
                        Menu::History => self.history_menu(ui, mixer),
                        Menu::Pipeline => self.pipeline_menu(ui, mixer),
                    };
                });
            });
        let outside = ctx.input(|i| i.pointer.any_pressed() && i.pointer.interact_pos().is_some_and(|p| !area.response.rect.contains(p)));
        let escape = ctx.input(|i| i.key_pressed(egui::Key::Escape));
        // Submenus live in their own layers; a press there is inside.
        let in_submenu = ctx.input(|i| i.pointer.interact_pos()).and_then(|p| ctx.layer_id_at(p)).is_some_and(|l| l.order == egui::Order::Foreground && l.id != area.response.layer_id.id);
        if close || (!just_opened && outside && !in_submenu) || escape {
            if matches!(menu, Menu::Pipeline) {
                self.pipeline_choices.clear();
            }
            self.menu = None;
        }
    }

    fn selection_items(&mut self, ui: &mut Ui, mixer: &mut MixerState, world: Pos2) -> bool {
        let mut close = false;
        if item(ui, "Copy", "Ctrl+C", !self.selected.is_empty(), "") {
            self.copy(mixer);
            close = true;
        }
        if item(ui, "Paste", "Ctrl+V", mixer.can_edit_layout() && Self::clipboard_has_nodes(), "") {
            self.paste(mixer, Some(world));
            close = true;
        }
        let label = if self.selected_frame.is_some() {
            "Ungroup (keep nodes)".to_owned()
        } else if self.selected.len() > 1 {
            format!("Delete {} nodes", self.selected.len())
        } else {
            "Delete node".to_owned()
        };
        if item(ui, &label, "Delete", mixer.can_edit_layout() && !self.selected.is_empty(), "") {
            self.delete_selection(mixer);
            close = true;
        }
        let count = mixer.graph().nodes.iter().filter(|n| self.selected.contains(&n.id)).count();
        if count >= 2 {
            let can = !mixer.is_layout_locked() && mixer.can_edit() && self.can_frame_selection(mixer);
            {
                widgets::submenu(ui, "Align selected nodes", can, "", |ui| {
                    let selected: std::collections::HashSet<String> = self.selected.iter().cloned().collect();
                    let sizes: std::collections::HashMap<String, (f64, f64)> = self
                        .layouts
                        .iter()
                        .filter(|(id, _)| selected.contains(*id))
                        .map(|(id, l)| (id.clone(), (f64::from(l.width), f64::from(l.height))))
                        .collect();
                    for (title, alignment, shortcut) in
                        [("Left edges", editing::Alignment::Left, "Alt+Y"), ("Top edges", editing::Alignment::Top, "Alt+X"), ("Auto Align", editing::Alignment::Auto, "Alt+A")]
                    {
                        let resolved = if alignment == editing::Alignment::Auto { editing::resolve_auto(mixer.graph(), &selected) } else { alignment };
                        let enabled = !editing::alignment_plan(mixer.graph(), &selected, resolved, &sizes).is_empty();
                        if item(ui, title, shortcut, enabled, "") {
                            self.align_selected(mixer, alignment);
                            close = true;
                        }
                    }
                });
            }
        }
        if !self.selected.is_empty() {
            widgets::menu_separator(ui);
            if item(
                ui,
                "Group selected nodes",
                "Ctrl+G",
                mixer.can_edit_layout(),
                "Group selected nodes\nAdds a named, colored frame around these nodes\nOrganizes the canvas without changing sound or connections",
            ) {
                self.create_frame_from_selection(mixer);
                close = true;
            }
            let frames_list = mixer.frames().to_vec();
            if !frames_list.is_empty() {
                widgets::submenu(ui, "Add to group", mixer.can_edit_layout(), "", |ui| {
                    for frame in &frames_list {
                        let enabled = self.selected.iter().any(|m| !frame.node_ids.contains(m));
                        if item(ui, &frame.name, "", enabled, "") {
                            self.add_selection_to_frame(mixer, &frame.id);
                            close = true;
                        }
                    }
                });
            }
            if frames_list.iter().any(|f| f.node_ids.iter().any(|id| self.selected.contains(id))) && item(ui, "Remove from group", "", mixer.can_edit_layout(), "") {
                self.remove_selection_from_frames(mixer);
                close = true;
            }
        }
        close
    }

    fn canvas_menu(&mut self, ui: &mut Ui, mixer: &mut MixerState, world: Pos2) -> bool {
        let mut close = false;
        if item(ui, "Add node", "Shift+A", mixer.can_edit_layout() && mixer.graph().nodes.len() < graph::MAXIMUM_NODES, "") {
            let anchor = self.menu.as_ref().map_or(Pos2::ZERO, |(_, a)| *a);
            self.add_search.clear();
            self.menu = Some((Menu::Add(world), anchor));
            self.menu_opened = true;
            return false;
        }
        widgets::menu_separator(ui);
        close |= self.selection_items(ui, mixer, world);
        close
    }

    #[allow(clippy::too_many_lines)]
    fn node_menu(&mut self, ui: &mut Ui, mixer: &mut MixerState, id: &str, world: Pos2) -> bool {
        let mut close = self.selection_items(ui, mixer, world);
        let frame_enabled = self.can_frame_selection(mixer) && !self.selected.is_empty();
        if item(ui, "Frame selection", "F", frame_enabled, "Frame selection\nBrings the selected nodes into view") {
            let ctx = ui.ctx().clone();
            self.frame_selection(&ctx, mixer);
            close = true;
        }
        let Some(node) = mixer.graph().nodes.iter().find(|n| n.id == id).cloned() else { return true };
        let single = self.selected.len() == 1;
        let room = mixer.graph().nodes.len() < graph::MAXIMUM_NODES;
        if matches!(node.kind.as_str(), "microphone" | "virtualInput" | "appInput")
            && item(
                ui,
                "Duplicate input",
                "",
                mixer.can_edit_layout() && single && room,
                "Duplicate input\nUse the same source in another part of the graph\nCopies its controls; starts without connections",
            )
        {
            self.duplicate_endpoint(mixer, id, false);
            close = true;
        }
        if matches!(node.kind.as_str(), "output" | "virtualOutput")
            && item(
                ui,
                "Duplicate output",
                "",
                mixer.can_edit_layout() && single && room,
                "Duplicate output\nSend another branch to the same destination\nCopies its controls; starts muted and without connections",
            )
        {
            self.duplicate_endpoint(mixer, id, true);
            close = true;
        }
        widgets::menu_separator(ui);
        if item(
            ui,
            "Detach node (reconnect)",
            "Alt+drag",
            mixer.can_edit_layout() && single,
            "Detach node\nReconnects the surrounding audio path\nKeeps this node and its settings",
        ) {
            self.detach_node(mixer, id);
            close = true;
        }
        if item(ui, "Rename", "F2", mixer.can_edit() && single, "") {
            let ctx = ui.ctx().clone();
            self.begin_rename(&ctx, mixer, id);
            close = true;
        }
        let reset_help = if matches!(node.kind.as_str(), "output" | "virtualOutput") {
            "Reset node\nRestore default controls and mute this output\nKeep its connections, position and device"
        } else {
            "Reset node\nRestore default sound controls\nKeep its connections, position and device/app"
        };
        if item(ui, "Reset node to defaults", "", mixer.can_edit() && single, reset_help) {
            self.reset_node(mixer, id);
            close = true;
        }
        if graph::bindings::is_virtual(&node.kind) {
            {
                widgets::submenu(ui, "Troubleshooting", mixer.can_edit() && single, "", |ui| {
                    let virtual_help = "Virtual device\nChanges the virtual endpoint assigned to this node\nSaved selections stay assigned while disconnected";
                    widgets::submenu(ui, "Reassign device", true, virtual_help, |ui| {
                        let mut endpoints: Vec<_> = mixer.endpoints().iter().filter(|e| graph::bindings::matches(&node.kind, e)).cloned().collect();
                        endpoints.sort_by(|a, b| (a.state != "Active").cmp(&(b.state != "Active")).then_with(|| a.name.cmp(&b.name)));
                        if endpoints.is_empty() {
                            item(ui, if node.kind == "virtualInput" { "VB-CABLE not detected" } else { "VAC Lite not detected" }, "", false, "");
                        }
                        for endpoint in endpoints {
                            let selected = graph::bindings::same(&node.device, &endpoint);
                            let label = format!("{}{}", endpoint.name, if endpoint.state != "Active" { format!(" · {}", endpoint.state) } else { String::new() });
                            let mut checked = selected;
                            if widgets::menu_check(ui, &mut checked, &label, true, "") {
                                let binding = vinemix_model::config::DeviceBinding {
                                    id: endpoint.id.clone(),
                                    stable_id: endpoint.stable_id.clone(),
                                    name: endpoint.name.clone(),
                                    channel: -1,
                                };
                                let node_id = node.id.clone();
                                mixer.edit(move |w| {
                                    if let Some(current) = w.node_mut(&node_id) {
                                        if graph::bindings::matches(&current.kind, &vinemix_model::status::Endpoint { direction: endpoint.direction.clone(), transport: endpoint.transport.clone(), ..Default::default() }) {
                                            current.device = binding.clone();
                                        }
                                    }
                                });
                                close = true;
                            }
                        }
                    });
                });
            }
        }
        if node.kind != "control" && !catalog::has_level_control(&node.kind) && node.gain_db != 0.0 {
            let label = format!("Saved level: {} dB", crate::text::trim_number(node.gain_db, 1));
            widgets::submenu(ui, &label, true, "Saved level\nApplies an additional level retained from an older version", |ui| {
                if item(ui, "Reset saved level to 0 dB", "", mixer.can_edit(), "") {
                    let node_id = node.id.clone();
                    mixer.edit(move |w| {
                        if let Some(n) = w.node_mut(&node_id) {
                            n.gain_db = 0.0;
                        }
                    });
                    close = true;
                }
            });
        }
        close
    }

    fn edge_menu(&mut self, ui: &mut Ui, mixer: &mut MixerState, id: &str) -> bool {
        let Some(edge) = mixer.graph().edges.iter().find(|e| e.id == id).cloned() else { return true };
        let mut close = false;
        if edge.kind == "audio" {
            let label = if edge.muted { "Turn connection on" } else { "Turn connection off" };
            if item(ui, label, "", mixer.can_edit(), "") {
                let message = if edge.muted { "Connection turned on." } else { "Connection turned off." };
                self.change_edge(mixer, &edge, |edges, i| edges[i].muted = !edges[i].muted, message, false);
                close = true;
            }
        }
        if edge.gain_db != 0.0
            && item(
                ui,
                "Reset connection level to 0 dB",
                "",
                mixer.can_edit(),
                "Reset saved connection level\nChanges only this cable's retained level\nCan increase volume if this cable is attenuated\nKeeps its on/off state and routing",
            )
        {
            self.change_edge(mixer, &edge, |edges, i| edges[i].gain_db = 0.0, "Connection level reset to 0 dB. On/off state and routing kept.", false);
            close = true;
        }
        if item(ui, "Remove connection", "", mixer.can_edit_layout(), "") && self.require_layout_edit(mixer) {
            self.change_edge(
                mixer,
                &edge,
                |edges, i| {
                    edges.remove(i);
                },
                "Connection removed.",
                true,
            );
            close = true;
        }
        close
    }

    fn frame_menu(&mut self, ui: &mut Ui, mixer: &mut MixerState, id: &str) -> bool {
        let Some(frame) = mixer.frames().iter().find(|f| f.id == id).cloned() else { return true };
        let mut close = false;
        if item(ui, "Rename group", "F2", mixer.can_edit_layout(), "") {
            let ctx = ui.ctx().clone();
            self.begin_frame_rename(&ctx, mixer, id);
            close = true;
        }
        widgets::submenu(ui, "Group color", mixer.can_edit_layout(), "", |ui| {
            for color in &frames::PALETTE {
                let mut checked = frame.color_key == color.key;
                let swatch = widgets::Swatch::group(egui::Color32::from_rgb(color.accent[0], color.accent[1], color.accent[2]));
                if widgets::menu_check_with(ui, &mut checked, color.name, true, "", Some(swatch)) {
                    self.recolor_frame(mixer, id, color.key);
                    close = true;
                }
            }
        });
        widgets::menu_separator(ui);
        if item(ui, "Select member nodes", "", true, "") {
            self.set_selection(mixer, &frame.node_ids, true, false);
            close = true;
        }
        if item(ui, "Copy group and nodes", "Ctrl+C", true, "") {
            self.select_frame(mixer, id);
            self.copy(mixer);
            close = true;
        }
        if item(ui, "Ungroup (keep nodes)", "Delete", mixer.can_edit_layout(), "") {
            self.remove_frame(mixer, id);
            close = true;
        }
        close
    }

    fn add_menu(&mut self, ui: &mut Ui, mixer: &mut MixerState, world: Pos2, just_opened: bool) -> bool {
        let mut close = false;
        let room = mixer.graph().nodes.len() < graph::MAXIMUM_NODES;
        let enabled = mixer.can_edit_layout() && room;
        let unavailable = if !room {
            "The workspace has reached its limit of 32 nodes. Remove a node before adding another."
        } else if mixer.is_layout_locked() {
            LAYOUT_LOCKED_MESSAGE
        } else if !mixer.can_edit() {
            "Wait for the audio engine before adding a node."
        } else {
            ""
        };
        let search = ui
            .scope(|ui| {
                ui.spacing_mut().item_spacing = egui::vec2(0.0, 0.0);
                egui::Frame::new().inner_margin(egui::Margin::symmetric(8 + 3, 4 + 3)).show(ui, |ui| {
                    ui.add(egui::TextEdit::singleline(&mut self.add_search).hint_text("Search nodes…").desired_width(200.0).min_size(egui::vec2(200.0, 30.0)))
                        .tip("Search node names, categories or purpose")
                })
                .inner
            })
            .inner;
        if just_opened {
            search.request_focus();
        }
        widgets::menu_separator(ui);
        let query = self.add_search.trim().to_owned();
        let heavy_advice = mixer.processing_addition_advice("denoiser");
        let help = |kind: &str| {
            let mut text = format!("{}\n{}", graph::kind_label(kind), catalog::summary(kind));
            let advice = if kind == "denoiser" { heavy_advice } else { "" };
            if !advice.is_empty() {
                text += &format!("\n{advice}");
            }
            if !unavailable.is_empty() {
                // A row that cannot be chosen says why, and only that.
                text = format!("{}\n{unavailable}", graph::kind_label(kind));
            }
            text
        };
        if query.is_empty() {
            for category in &catalog::CATEGORIES {
                let colour = widgets::Swatch::category(theme::header_color(category.header));
                let category_help = format!("{}\n{}", category.title, category.description());
                widgets::submenu_styled(ui, category.title, true, &category_help, Some(colour), |ui| {
                    for kind in category.kinds {
                        if widgets::menu_item_styled(ui, graph::kind_label(kind), "", enabled, &help(kind), None, Some(colour)) {
                            self.spawn(mixer, kind, world);
                            close = true;
                        }
                    }
                });
            }
        } else {
            let matches: Vec<&str> = graph::KINDS.iter().copied().filter(|k| catalog::can_create(k) && catalog::matches_search(k, &query)).collect();
            if matches.is_empty() {
                item(ui, "No matching nodes", "", false, "No matching nodes\nTry a node name, category or purpose, or clear the search to browse all nodes.");
            }
            for kind in &matches {
                let colour = widgets::Swatch::category(theme::header_color(catalog::category_for(kind).header));
                if widgets::menu_item_styled(ui, graph::kind_label(kind), "", enabled, &help(kind), None, Some(colour)) {
                    self.spawn(mixer, kind, world);
                    close = true;
                }
            }
            if search.has_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)) && enabled {
                if let Some(first) = matches.first() {
                    self.spawn(mixer, first, world);
                    close = true;
                }
            }
        }
        close
    }

    fn history_menu(&mut self, ui: &mut Ui, mixer: &MixerState) -> bool {
        widgets::menu_caption(ui, "Activity · this session");
        widgets::menu_separator(ui);
        let entries = mixer.history.entries();
        if entries.is_empty() {
            widgets::menu_caption(ui, "No edits yet");
        }
        egui::ScrollArea::vertical().max_height(420.0).show(ui, |ui| {
            for entry in entries {
                widgets::menu_item(ui, &entry.description, &crate::time::clock(entry.time), false, "");
            }
        });
        if !self.last_message.is_empty() {
            widgets::menu_separator(ui);
            egui::Frame::new().inner_margin(egui::Margin::symmetric(8 + 20, 6)).show(ui, |ui| {
                ui.set_max_width(380.0);
                ui.add(egui::Label::new(egui::RichText::new(&self.last_message).color(theme::MUTED).font(theme::font(13.0, theme::Weight::Regular))).wrap());
            });
        }
        false
    }

    fn pipeline_menu(&mut self, ui: &mut Ui, mixer: &mut MixerState) -> bool {
        let header = if self.pipeline_origin.is_none() {
            "Choose a starting node"
        } else if self.pipeline_forward {
            "Follow audio to…"
        } else {
            "Follow audio from…"
        };
        ui.add_enabled(false, egui::Label::new(header));
        let labels: Vec<String> = self.pipeline_choices.iter().map(|c| c.label.clone()).collect();
        let mut chosen = None;
        egui::ScrollArea::vertical().max_height(440.0).show(ui, |ui| {
            for (index, label) in labels.iter().enumerate() {
                if item(ui, label, "", true, "") {
                    chosen = Some(index);
                }
            }
        });
        if let Some(index) = chosen {
            self.choose_pipeline(mixer, index);
            return true;
        }
        false
    }
}
