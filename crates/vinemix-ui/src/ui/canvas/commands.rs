//! Workspace commands: delete, copy/paste, duplicate, spawn, groups,
//! node reset/detach, edge changes and audio-pipeline navigation.

use std::collections::HashSet;

use eframe::egui::Pos2;
use vinemix_model::config::GraphEdge;

use super::{Canvas, Menu, PipelineChoice};
use crate::editing;
use crate::frames::Frame;
use crate::graph;
use crate::mixer::MixerState;
use crate::platform::clipboard;

impl Canvas {
    pub fn delete_selection(&mut self, mixer: &mut MixerState) {
        if let Some(frame) = self.selected_frame.clone() {
            self.remove_frame(mixer, &frame);
            return;
        }
        if self.selected_edge.is_none() && self.selected.is_empty() {
            return;
        }
        if !self.require_layout_edit(mixer) {
            return;
        }
        self.finish_gesture(mixer, None, true);
        self.cancel_cable();
        if let Some(edge) = self.selected_edge.take() {
            self.remove_edge(mixer, &edge);
        } else {
            let ids: HashSet<String> = self.selected.drain(..).collect();
            let count = ids.len();
            mixer.edit(move |w| {
                w.config.graph.nodes.retain(|n| !ids.contains(&n.id));
                w.config.graph.edges.retain(|e| !ids.contains(&e.from) && !ids.contains(&e.to));
            });
            self.set_message(mixer, &format!("{count} {} and their connections removed.", if count == 1 { "node" } else { "nodes" }), false);
        }
        self.cancel_cable();
    }

    pub fn remove_edge(&mut self, mixer: &mut MixerState, id: &str) {
        if !mixer.graph().edges.iter().any(|e| e.id == id) || !self.require_layout_edit(mixer) {
            return;
        }
        let id = id.to_owned();
        mixer.edit(move |w| w.config.graph.edges.retain(|e| e.id != id));
        self.set_message(mixer, "Connection removed.", false);
    }

    pub fn copy(&mut self, mixer: &mut MixerState) {
        if self.selected.is_empty() {
            self.set_message(mixer, "Select one or more nodes to copy.", false);
            return;
        }
        let ids: HashSet<String> = self.selected.iter().cloned().collect();
        let payload = editing::serialize_group(mixer.graph(), mixer.frames(), &ids);
        match clipboard::set(editing::CLIPBOARD_FORMAT, &payload) {
            Ok(()) => {
                self.last_pasted = None;
                self.paste_repeat = 0;
                let count = self.selected.len();
                self.set_message(mixer, &format!("Copied {count} {} and their internal connections.", if count == 1 { "node" } else { "nodes" }), false);
            }
            Err(error) => self.set_message(mixer, &error, true),
        }
    }

    pub fn clipboard_has_nodes() -> bool {
        clipboard::has(editing::CLIPBOARD_FORMAT) || clipboard::has("AudioMixer.NodeGroup.v1")
    }

    pub fn paste(&mut self, mixer: &mut MixerState, anchor: Option<Pos2>) {
        let payload = clipboard::get(editing::CLIPBOARD_FORMAT).or_else(|| clipboard::get("AudioMixer.NodeGroup.v1"));
        let Some(payload) = payload else {
            self.set_message(mixer, "Copy nodes in VineMix before pasting.", false);
            return;
        };
        let repeat = if self.last_pasted.as_deref() == Some(payload.as_str()) { self.paste_repeat } else { 0 };
        let mut target = anchor.unwrap_or_else(|| if self.viewport.contains(self.last_pointer_screen()) { self.pointer_world } else { self.visible_center() });
        if anchor.is_none() {
            target += eframe::egui::Vec2::splat(repeat as f32 * 24.0);
        }
        if self.paste_payload(mixer, &payload, target) {
            self.last_pasted = Some(payload);
            self.paste_repeat = repeat + 1;
        }
    }

    fn paste_payload(&mut self, mixer: &mut MixerState, payload: &str, anchor: Pos2) -> bool {
        if mixer.is_layout_locked() {
            self.require_layout_edit(mixer);
            return false;
        }
        if !mixer.can_edit() {
            self.set_message(mixer, "Wait for the audio engine before pasting nodes.", true);
            return false;
        }
        match editing::create_paste(payload, mixer.graph(), mixer.frames(), (f64::from(anchor.x), f64::from(anchor.y))) {
            Ok(paste) => {
                let count = paste.nodes.len();
                let outputs = paste.nodes.iter().any(|n| matches!(n.kind.as_str(), "output" | "virtualOutput"));
                let message = format!(
                    "Pasted {count} {}.{}",
                    if count == 1 { "node" } else { "nodes" },
                    if outputs { " Copied outputs keep their devices and start muted." } else { " Drag a selected header to position the group." }
                );
                self.apply_paste(mixer, paste, &message)
            }
            Err(error) => {
                self.set_message(mixer, &error, true);
                false
            }
        }
    }

    fn apply_paste(&mut self, mixer: &mut MixerState, paste: editing::Paste, message: &str) -> bool {
        self.finish_gesture(mixer, None, true);
        self.cancel_cable();
        let ids: Vec<String> = paste.nodes.iter().map(|n| n.id.clone()).collect();
        let replay = paste.clone();
        match mixer.try_edit(move |w| editing::append_paste(&mut w.config.graph, &mut w.frames, &replay)) {
            Ok(()) => {
                self.set_selection(mixer, &ids, true, false);
                self.set_message(mixer, message, false);
                true
            }
            Err(error) => {
                self.set_message(mixer, &format!("Paste could not be applied: {error}"), true);
                false
            }
        }
    }

    pub fn duplicate_endpoint(&mut self, mixer: &mut MixerState, id: &str, output: bool) -> bool {
        if mixer.is_layout_locked() {
            self.require_layout_edit(mixer);
            return false;
        }
        if !mixer.can_edit() {
            self.set_message(mixer, "Wait for the audio engine before duplicating a node.", true);
            return false;
        }
        let Some(node) = mixer.graph().nodes.iter().find(|n| n.id == id).cloned() else { return false };
        let valid = if output { matches!(node.kind.as_str(), "output" | "virtualOutput") } else { matches!(node.kind.as_str(), "microphone" | "virtualInput" | "appInput") };
        if !valid {
            return false;
        }
        let mut position = (node.x + 384.0, node.y + 32.0);
        while mixer.graph().nodes.iter().any(|n| (n.x - position.0).abs() < 16.0 && (n.y - position.1).abs() < 16.0) {
            position = (position.0 + 32.0, position.1 + 40.0);
        }
        let payload = editing::serialize_group(mixer.graph(), &[], &[id.to_owned()].into());
        let mut paste = match editing::create_paste(&payload, mixer.graph(), mixer.frames(), position) {
            Ok(paste) => paste,
            Err(error) => {
                self.set_message(mixer, &error, true);
                return false;
            }
        };
        let mut number = 1;
        let name = loop {
            let suffix = if number == 1 { " copy".to_owned() } else { format!(" copy {number}") };
            let mut bytes = 0;
            let mut prefix = String::new();
            for c in node.name.chars() {
                if bytes + c.len_utf8() > 256 - suffix.len() {
                    break;
                }
                bytes += c.len_utf8();
                prefix.push(c);
            }
            let candidate = prefix + &suffix;
            number += 1;
            if !mixer.graph().nodes.iter().any(|n| crate::text::eq_ignore_case(&n.name, &candidate)) {
                break candidate;
            }
        };
        paste.nodes[0].name = name;
        let message = if output {
            "Output duplicated with its own controls. Connect the new node, then unmute it when ready."
        } else {
            "Input duplicated with its own controls. Connect the new node to build another branch."
        };
        self.apply_paste(mixer, paste, message)
    }

    pub fn spawn(&mut self, mixer: &mut MixerState, kind: &str, position: Pos2) -> Option<String> {
        if mixer.graph().nodes.len() >= graph::MAXIMUM_NODES || !graph::catalog::can_create(kind) || !self.require_layout_edit(mixer) {
            return None;
        }
        let mut node = graph::new_node(kind, (f64::from(position.x) / 8.0).round() * 8.0, (f64::from(position.y) / 8.0).round() * 8.0);
        let count = mixer.graph().nodes.iter().filter(|n| n.kind == kind).count();
        if count > 0 {
            node.name = format!("{} {}", node.name, count + 1);
        }
        let id = node.id.clone();
        mixer.edit(move |w| {
            if w.config.graph.nodes.len() < graph::MAXIMUM_NODES && w.config.graph.nodes.iter().all(|n| n.id != node.id) {
                w.config.graph.nodes.push(node.clone());
            }
        });
        self.select_node(mixer, &id);
        let message = match kind {
            "virtualOutput" => "Virtual output added, muted. Check its connection and level before turning its mute off.",
            "output" => "Output added, muted. Choose a device and level before turning its mute off.",
            "appInput" => "Choose a running app. Capture makes a copy; the app's original output continues.",
            _ => "Node added. Choose its settings, then connect its sockets.",
        };
        self.set_message(mixer, message, false);
        Some(id)
    }

    pub fn reset_node(&mut self, mixer: &mut MixerState, id: &str) {
        if !mixer.can_edit() || !mixer.graph().nodes.iter().any(|n| n.id == id) {
            return;
        }
        self.cancel_active_gesture(mixer);
        let id = id.to_owned();
        mixer.edit(move |w| {
            if let Some(node) = w.node_mut(&id) {
                graph::reset_node_settings(node);
            }
        });
        self.set_message(mixer, "Node controls reset to defaults. Connections and assignments kept.", false);
    }

    pub fn detach_node(&mut self, mixer: &mut MixerState, id: &str) -> bool {
        if !self.require_layout_edit(mixer) {
            return false;
        }
        match editing::prepare_detach(mixer.graph(), id) {
            Ok(plan) => {
                self.cancel_cable();
                self.set_message(mixer, "Node detached; its audio path stays connected.", false);
                let _ = mixer.try_edit(move |w| editing::apply_node_plan(&mut w.config.graph, &plan));
                true
            }
            Err(error) => {
                self.set_message(mixer, &error, true);
                false
            }
        }
    }

    /// Changes a cable from its menu, guarding against a changed cable.
    pub fn change_edge(&mut self, mixer: &mut MixerState, expected: &GraphEdge, change: impl Fn(&mut Vec<GraphEdge>, usize) + Send + 'static, message: &str, allow_missing: bool) {
        if !mixer.can_edit() {
            return;
        }
        let expected = expected.clone();
        let result = mixer.try_edit(move |w| {
            let index = w.config.graph.edges.iter().position(|e| e.id == expected.id);
            match index {
                None if allow_missing => Ok(()),
                Some(index) if same_connection(&w.config.graph.edges[index], &expected) => {
                    change(&mut w.config.graph.edges, index);
                    Ok(())
                }
                _ => Err("This connection changed. Open its menu again.".into()),
            }
        });
        match result {
            Ok(()) => self.set_message(mixer, message, false),
            Err(error) => self.set_message(mixer, &error, true),
        }
    }

    // ----- groups -----------------------------------------------------------

    pub fn create_frame_from_selection(&mut self, mixer: &mut MixerState) -> Option<String> {
        let members: Vec<String> = self.selected.iter().filter(|id| mixer.graph().nodes.iter().any(|n| &n.id == *id)).cloned().collect();
        if members.is_empty() || !self.require_layout_edit(mixer) {
            return None;
        }
        let member_set: HashSet<&String> = members.iter().collect();
        if let Some(existing) = mixer.frames().iter().find(|f| f.node_ids.len() == members.len() && f.node_ids.iter().all(|id| member_set.contains(id))) {
            let id = existing.id.clone();
            self.select_frame(mixer, &id);
            return Some(id);
        }
        let id = graph::new_id();
        let mut name = "Group".to_owned();
        let mut index = 2;
        while mixer.frames().iter().any(|f| f.name == name) {
            name = format!("Group {index}");
            index += 1;
        }
        let created = Frame { id: id.clone(), name, color_key: "sage".into(), node_ids: members.clone() };
        mixer.edit(move |w| {
            for frame in &mut w.frames {
                frame.node_ids.retain(|id| !members.contains(id));
            }
            w.frames.retain(|f| !f.node_ids.is_empty());
            if w.frames.iter().all(|f| f.id != created.id) {
                w.frames.push(created.clone());
            }
        });
        self.select_frame(mixer, &id);
        self.set_message(mixer, "Group created.", false);
        Some(id)
    }

    pub fn recolor_frame(&mut self, mixer: &mut MixerState, id: &str, color: &'static str) {
        if !self.require_layout_edit(mixer) {
            return;
        }
        let id = id.to_owned();
        mixer.edit(move |w| {
            if let Some(frame) = w.frames.iter_mut().find(|f| f.id == id) {
                frame.color_key = color.into();
            }
        });
    }

    pub fn remove_frame(&mut self, mixer: &mut MixerState, id: &str) {
        if !self.require_layout_edit(mixer) {
            return;
        }
        if self.selected_frame.as_deref() == Some(id) {
            self.selected_frame = None;
        }
        let id = id.to_owned();
        mixer.edit(move |w| w.frames.retain(|f| f.id != id));
        self.set_message(mixer, "Ungrouped. Nodes and connections kept.", false);
    }

    pub fn add_selection_to_frame(&mut self, mixer: &mut MixerState, id: &str) {
        if !self.require_layout_edit(mixer) || !mixer.frames().iter().any(|f| f.id == id) {
            return;
        }
        let members = self.selected.clone();
        let target = id.to_owned();
        mixer.edit(move |w| {
            if !w.frames.iter().any(|f| f.id == target) {
                return;
            }
            for frame in w.frames.iter_mut().filter(|f| f.id != target) {
                frame.node_ids.retain(|id| !members.contains(id));
            }
            if let Some(frame) = w.frames.iter_mut().find(|f| f.id == target) {
                for member in &members {
                    if !frame.node_ids.contains(member) {
                        frame.node_ids.push(member.clone());
                    }
                }
            }
        });
        self.select_frame(mixer, id);
        self.set_message(mixer, "Selected nodes added to the group.", false);
    }

    pub fn remove_selection_from_frames(&mut self, mixer: &mut MixerState) {
        if !self.require_layout_edit(mixer) {
            return;
        }
        let members = self.selected.clone();
        self.selected_frame = None;
        mixer.edit(move |w| {
            for frame in &mut w.frames {
                frame.node_ids.retain(|id| !members.contains(id));
            }
        });
        self.set_message(mixer, "Selected nodes removed from their groups. Connections kept.", false);
    }

    // ----- pipeline navigation ---------------------------------------------

    /// Ctrl+Tab / Ctrl+Shift+Tab follow the audio pipeline (`TryPipelineShortcut`).
    pub fn pipeline_shortcut(&mut self, mixer: &mut MixerState, forward: bool) {
        self.finish_nudge(mixer, false);
        if !self.can_frame_selection(mixer) {
            return;
        }
        let graph = mixer.graph().clone();
        let current = if self.selected.len() == 1 { Some(self.selected[0].clone()) } else { None };
        let Some(current) = current else {
            let mut candidates: Vec<PipelineChoice> = graph
                .nodes
                .iter()
                .filter(|n| self.selected.is_empty() || self.selected.contains(&n.id))
                .map(|n| PipelineChoice {
                    node: n.id.clone(),
                    edge: None,
                    label: format!("{} · {}", n.name, graph::kind_label(&n.kind)),
                    from_port: None,
                    to_port: None,
                })
                .collect();
            candidates.sort_by(|a, b| crate::text::compare_ignore_case(&a.label, &b.label).then_with(|| a.node.cmp(&b.node)));
            self.show_pipeline(mixer, candidates, None, forward);
            return;
        };
        let routes: Vec<&GraphEdge> =
            graph.edges.iter().filter(|e| e.kind == "audio" && if forward { e.from == current } else { e.to == current }).collect();
        if routes.is_empty() {
            self.set_message(mixer, if forward { "End of this audio pipeline." } else { "Start of this audio pipeline." }, false);
            return;
        }
        if let Some((edge_id, other)) = self.pipeline_return.get(&(current.clone(), forward)).cloned() {
            if let Some(edge) = routes.iter().find(|e| e.id == edge_id && (if forward { &e.to } else { &e.from }) == &other) {
                let id = edge.id.clone();
                self.follow_pipeline(mixer, &current, &id, forward, None);
                return;
            }
        }
        if routes.len() == 1 {
            let id = routes[0].id.clone();
            self.follow_pipeline(mixer, &current, &id, forward, None);
            return;
        }
        let mut choices: Vec<PipelineChoice> = routes
            .iter()
            .map(|edge| {
                let id = if forward { &edge.to } else { &edge.from };
                let target = graph.nodes.iter().find(|n| &n.id == id);
                let from_kind = graph.nodes.iter().find(|n| n.id == edge.from).map(|n| n.kind.as_str());
                let to_kind = graph.nodes.iter().find(|n| n.id == edge.to).map(|n| n.kind.as_str());
                let ports = format!("{} → {}", graph::port_label(&edge.from_port, from_kind), graph::port_label(&edge.to_port, to_kind));
                PipelineChoice {
                    node: id.clone(),
                    edge: Some(edge.id.clone()),
                    label: format!("{} · {ports}{}", target.map_or("", |n| n.name.as_str()), if edge.muted { " · muted cable" } else { "" }),
                    from_port: Some(edge.from_port.clone()),
                    to_port: Some(edge.to_port.clone()),
                }
            })
            .collect();
        choices.sort_by(|a, b| crate::text::compare_ignore_case(&a.label, &b.label).then_with(|| a.edge.cmp(&b.edge)));
        self.show_pipeline(mixer, choices, Some(current), forward);
    }

    fn show_pipeline(&mut self, mixer: &MixerState, choices: Vec<PipelineChoice>, origin: Option<String>, forward: bool) {
        if choices.is_empty() {
            self.set_message(mixer, "Add a node to start navigating an audio pipeline.", false);
            return;
        }
        self.set_message(mixer, if origin.is_none() { "Choose a starting node." } else { "Choose the audio connection to follow." }, false);
        self.pipeline_choices = choices;
        self.pipeline_origin = origin;
        self.pipeline_forward = forward;
        let anchor = self.viewport.center();
        self.menu = Some((Menu::Pipeline, anchor));
        self.menu_opened = true;
    }

    pub fn choose_pipeline(&mut self, mixer: &mut MixerState, index: usize) -> bool {
        let Some(choice) = self.pipeline_choices.get(index).cloned() else { return false };
        let origin = self.pipeline_origin.take();
        let forward = self.pipeline_forward;
        self.pipeline_choices.clear();
        match (origin, &choice.edge) {
            (Some(origin), Some(edge)) => self.follow_pipeline(mixer, &origin, edge, forward, Some(&choice)),
            _ => self.focus_pipeline_node(mixer, &choice.node),
        }
    }

    fn follow_pipeline(&mut self, mixer: &mut MixerState, origin: &str, edge_id: &str, forward: bool, offered: Option<&PipelineChoice>) -> bool {
        let Some(edge) = mixer.graph().edges.iter().find(|e| e.id == edge_id && e.kind == "audio" && if forward { e.from == origin } else { e.to == origin }).cloned()
        else {
            self.set_message(mixer, "That connection changed. Choose the pipeline again.", false);
            return false;
        };
        let destination = if forward { edge.to.clone() } else { edge.from.clone() };
        if let Some(offered) = offered {
            if destination != offered.node || Some(&edge.from_port) != offered.from_port.as_ref() || Some(&edge.to_port) != offered.to_port.as_ref() {
                self.set_message(mixer, "That connection changed. Choose the pipeline again.", false);
                return false;
            }
        }
        if !self.focus_pipeline_node(mixer, &destination) {
            return false;
        }
        self.pipeline_return.insert((destination, !forward), (edge.id, origin.to_owned()));
        true
    }

    fn focus_pipeline_node(&mut self, mixer: &mut MixerState, id: &str) -> bool {
        let ctx = self.egui_ctx.clone();
        let Some(ctx) = ctx else { return false };
        if !self.reveal_node(&ctx, mixer, id) {
            return false;
        }
        let returns = std::mem::take(&mut self.pipeline_return);
        self.select_node(mixer, id);
        self.pipeline_return = returns;
        let name = mixer.graph().nodes.iter().find(|n| n.id == id).map(|n| n.name.clone()).unwrap_or_default();
        self.set_message(mixer, &format!("Selected {name}. Tab moves through its controls; Ctrl+Tab follows its audio pipeline."), false);
        true
    }
}

fn same_connection(current: &GraphEdge, expected: &GraphEdge) -> bool {
    current.id == expected.id
        && current.from == expected.from
        && current.to == expected.to
        && current.kind == expected.kind
        && current.from_port == expected.from_port
        && current.to_port == expected.to_port
}
