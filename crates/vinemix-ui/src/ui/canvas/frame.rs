//! One frame of the canvas: layout, drawing, pointer routing and keys.

use crate::ui::widgets::Tip as _;
use std::collections::HashMap;

use eframe::egui::{self, Align2, Color32, Id, Key, PointerButton, Pos2, Rect, Sense, Stroke, Ui, Vec2};
use vinemix_model::config::GraphNode;

use super::{connection_help, from_point, gradient_stroke, now, to_point, Canvas, Gesture, Menu, Projection, SocketRef};
use crate::editing;
use crate::frames;
use crate::graph::{self, canvas as policy, canvas::DetailLevel};
use crate::mixer::MixerState;
use crate::ui::node_view::{self, Action, Env};
use crate::ui::theme::{self, Weight};
use crate::ui::widgets;

/// What the pointer pressed this frame.
enum Press {
    Node(String, Pos2, egui::Modifiers),
    Socket(SocketRef, Pos2),
    Frame(String, Pos2, bool),
    Background(Pos2),
}

impl Canvas {
    /// Draws the canvas into `rect` and handles its input.
    #[allow(clippy::too_many_lines)]
    pub fn show(&mut self, ui: &mut Ui, rect: Rect, mixer: &mut MixerState) {
        let ctx = ui.ctx().clone();
        self.egui_ctx = Some(ctx.clone());
        self.viewport = rect;
        let t = now();
        let graph = mixer.graph().clone();
        let frames_list: Vec<frames::Frame> = mixer.frames().to_vec();
        let pointer = ctx.input(|i| i.pointer.latest_pos());
        if let Some(pointer) = pointer {
            self.pointer_world = self.camera().to_world(pointer);
        }
        if matches!(self.gesture, Gesture::None) && self.nudge.is_none() {
            self.sync_from_graph(&graph);
        }
        // Layouts in world units.
        self.layouts = graph.nodes.iter().map(|n| (n.id.clone(), node_view::layout(&ctx, n, &graph))).collect();
        self.reconcile_stacking(&graph);
        self.runtimes.retain(|id, _| graph.nodes.iter().any(|n| &n.id == id));
        self.selected.retain(|id| graph.nodes.iter().any(|n| &n.id == id));
        if self.selected_edge.as_ref().is_some_and(|id| !graph.edges.iter().any(|e| &e.id == id)) {
            self.selected_edge = None;
        }
        if self.selected_frame.as_ref().is_some_and(|id| !frames_list.iter().any(|f| &f.id == id)) {
            self.selected_frame = None;
        }
        if let Some(nudge) = &self.nudge {
            if !nudge.origins.iter().all(|(id, _)| graph.nodes.iter().any(|n| &n.id == id)) {
                let key = nudge.key;
                self.nudge = None;
                self.cancelled_nudge_keys.insert(key);
                self.set_message(mixer, "Move cancelled because a selected node was removed.", false);
            }
        }
        if let Some(rewire) = &self.rewire {
            if !graph.edges.iter().any(|e| e.id == rewire.id && e == rewire) {
                self.cancel_cable();
            }
        }
        if self.pending.as_ref().is_some_and(|p| !self.is_current_socket(mixer, p)) {
            self.cancel_cable();
        }
        if mixer.needs_initial_frame && mixer.can_edit() && !graph.nodes.is_empty() && rect.width() > 0.0 && rect.height() > 0.0 {
            self.fit_all(&ctx, mixer);
            mixer.needs_initial_frame = false;
        }

        let background_id = Id::unique("vinemix-canvas");
        let background = ui.interact(rect, background_id, Sense::click_and_drag());
        let painter = ui.painter_at(rect);
        self.paint_grid(&painter, rect);
        let cam = self.camera();
        let s = self.zoom;

        // Groups (below cables and nodes).
        let mut frame_responses: Vec<(String, egui::Response, Rect)> = Vec::new();
        for frame in &frames_list {
            let Some(bounds) = self.frame_bounds(frame, &graph) else { continue };
            let screen = cam.rect(bounds);
            let response = ui.interact(screen, background_id.with(("frame", frame.id.as_str())), Sense::click_and_drag());
            self.paint_frame(ui, mixer, frame, screen, &response);
            frame_responses.push((frame.id.clone(), response, screen));
        }

        // Cables.
        self.cable_detail = policy::detail_at(f64::from(s), self.cable_detail, false);
        let cut: Vec<String> = match &self.gesture {
            Gesture::Cut { cut, .. } => cut.clone(),
            _ => Vec::new(),
        };
        let mut hovered_edge: Option<String> = None;
        for edge in &graph.edges {
            let Some((from, to)) = self.cable_ends(mixer, edge) else { continue };
            if self.rewire.as_ref().is_some_and(|r| r.id == edge.id) {
                continue;
            }
            let highlighted = self.selected_edge.as_ref() == Some(&edge.id)
                || self.insertion.as_ref() == Some(&edge.id)
                || self.input_link_candidate.as_ref() == Some(&edge.id)
                || cut.contains(&edge.id);
            let source_node = graph.nodes.iter().find(|n| n.id == edge.from);
            let source_kind = if edge.kind == "control" { "control" } else { "audio" };
            let (mut source, mut destination) =
                (theme::socket_color(source_kind, true, &edge.from_port, source_node.map(|n| n.kind.as_str())), theme::socket_color(&edge.kind, false, &edge.to_port, None));
            if highlighted {
                source = theme::NODE_SELECTED;
                destination = theme::NODE_SELECTED;
            } else if edge.muted {
                source = theme::MUTED;
                destination = theme::MUTED;
            }
            let opacity = if highlighted {
                1.0
            } else if edge.muted {
                0.4
            } else if edge.kind == "control" {
                0.75
            } else {
                0.95
            };
            let width = if highlighted { 3.0 } else { (2.5 * s).max(1.6) };
            let points: Vec<Pos2> = editing::cable_points(to_point(from), to_point(to)).into_iter().map(|p| cam.to_screen(from_point(p))).collect();
            painter.add(egui::Shape::line(points.clone(), Stroke::new(width + 2.0, theme::CANVAS)));
            let (from_s, to_s) = (cam.to_screen(from), cam.to_screen(to));
            if edge.muted && !highlighted {
                for segment in dashed(&points, 2.0 * width, 3.0 * width) {
                    painter.add(egui::Shape::line(segment, gradient_stroke(width, from_s, to_s, theme::with_opacity(source, opacity), theme::with_opacity(destination, opacity))));
                }
            } else {
                painter.add(egui::Shape::line(points.clone(), gradient_stroke(width, from_s, to_s, theme::with_opacity(source, opacity), theme::with_opacity(destination, opacity))));
            }
            let marker_detail = self.cable_detail == DetailLevel::Full
                || self.runtimes.get(&edge.from).is_some_and(|r| r.detail() == DetailLevel::Full)
                || self.runtimes.get(&edge.to).is_some_and(|r| r.detail() == DetailLevel::Full);
            if !edge.muted && marker_detail {
                let middle = cam.to_screen(from + (to - from) * 0.5);
                painter.circle_filled(middle, 3.75 * s, theme::with_opacity(theme::mix(source, destination, 0.5), opacity));
            }
            if pointer.is_some_and(|p| rect.contains(p)) && hovered_edge.is_none() && background.hovered() {
                if let Some(p) = pointer {
                    let world = to_point(cam.to_world(p));
                    let line = editing::cable_points(to_point(from), to_point(to));
                    if editing::distance_to_polyline(&line, world) * f64::from(s) <= 8.0 {
                        hovered_edge = Some(edge.id.clone());
                    }
                }
            }
        }
        if let (Some(id), Some(pointer)) = (&hovered_edge, pointer) {
            if let Some(edge) = graph.edges.iter().find(|e| &e.id == id) {
                // The help belongs at the cable: anchor it to the hovered
                // point, not to the canvas (whose edge can be far away).
                let anchor = Rect::from_center_size(pointer, Vec2::splat(16.0));
                background.clone().tip_at(anchor, connection_help(&graph, edge));
                ctx.set_cursor_icon(egui::CursorIcon::PointingHand);
            }
        }

        // Nodes, back to front, each with its sockets.
        let env_graph = graph.clone();
        let endpoints = mixer.endpoints().to_vec();
        let applications = mixer.applications().to_vec();
        let mut actions: Vec<(String, Action)> = Vec::new();
        let mut socket_responses: Vec<(SocketRef, egui::Response)> = Vec::new();
        let mut surfaces: HashMap<String, egui::Response> = HashMap::new();
        let stacking = self.stacking.clone();
        let snap = self.snap.clone();
        let pending = self.pending.clone();
        for id in &stacking {
            let Some(node) = graph.nodes.iter().find(|n| &n.id == id) else { continue };
            let Some(layout) = self.layouts.get(id).cloned() else { continue };
            let origin = self.displayed_origin(node);
            let status = mixer.node_status(id).cloned();
            let env = Env {
                graph: &env_graph,
                endpoints: &endpoints,
                applications: &applications,
                inventory_generation: mixer.inventory_generation,
                visibility: mixer.device_visibility(),
                can_edit: mixer.can_edit(),
                preview: mixer.is_preview(),
                layout_locked: mixer.is_layout_locked(),
                reduced_animation: mixer.reduced_animation(),
                now: t,
                meter_sequence: mixer.meter_sample_sequence,
                audio_frames: mixer.meter_audio_frames(),
                acknowledged: mixer.acknowledged_node(id),
            };
            let runtime = self.runtimes.entry(id.clone()).or_default();
            let selected = self.selected.iter().any(|x| x == id);
            let mut node_actions = Vec::new();
            let screen_rect = cam.rect(Rect::from_min_size(origin, Vec2::new(layout.width, layout.height)));
            if !ui.is_rect_visible(screen_rect.expand(24.0)) {
                continue;
            }
            let surface = node_view::show(ui, cam, node, origin, &layout, status.as_ref(), &env, runtime, selected, &mut node_actions);
            surfaces.insert(id.clone(), surface);
            actions.extend(node_actions.into_iter().map(|a| (id.clone(), a)));
            // Sockets above this node.
            let overview = runtime.detail() == DetailLevel::Overview;
            for port in layout.outputs.iter().chain(&layout.inputs) {
                let socket = SocketRef { node: id.clone(), kind: port.kind, port: port.port, output: port.output };
                let center = cam.to_screen(node_view::socket_world(origin, &layout, port));
                let connected = graph.edges.iter().any(|e| {
                    if port.output {
                        e.from == *id && e.from_port == port.port && if port.kind == "audio" { matches!(e.kind.as_str(), "audio" | "sidechain") } else { e.kind == port.kind }
                    } else {
                        e.to == *id && e.to_port == port.port && e.kind == port.kind
                    }
                });
                let hit = Rect::from_center_size(center, Vec2::new(24.0, 20.0) * s);
                let response = ui.interact(hit, background_id.with(("socket", id.as_str(), port.kind, port.port, port.output)), if overview { Sense::hover() } else { Sense::click_and_drag() });
                let highlighted = snap.as_ref() == Some(&socket) || pending.as_ref() == Some(&socket) || response.hovered();
                let color = theme::socket_color(port.kind, port.output, port.port, Some(&node.kind));
                node_view::paint_socket(ui.painter(), center, port.kind, color, connected, highlighted, s);
                if !overview {
                    let response = response.tip(socket_help(&graph, node, port));
                    if response.hovered() {
                        ctx.set_cursor_icon(egui::CursorIcon::Crosshair);
                    }
                    socket_responses.push((socket, response));
                }
            }
        }

        // Connection preview.
        if let Some(pending) = &self.pending {
            if self.input_link.is_none() {
                if let Some(socket_world) = self.socket_world(mixer, pending) {
                    let target = self.snap.as_ref().and_then(|snap| self.socket_world(mixer, snap)).unwrap_or(self.pointer_world);
                    let (start, end) = if pending.output { (socket_world, target) } else { (target, socket_world) };
                    let source_socket = if pending.output { Some(pending) } else { self.snap.as_ref() };
                    let destination_socket = if pending.output { self.snap.as_ref() } else { Some(pending) };
                    let source_color = source_socket.map_or(theme::NODE_AUDIO, |s| theme::socket_color(s.kind, true, s.port, None));
                    let destination_color = destination_socket.map_or(source_color, |s| theme::socket_color(s.kind, false, s.port, None));
                    let points: Vec<Pos2> = editing::cable_points(to_point(start), to_point(end)).into_iter().map(|p| cam.to_screen(from_point(p))).collect();
                    for segment in dashed(&points, 6.0, 6.0) {
                        painter.add(egui::Shape::line(segment, gradient_stroke(2.0, cam.to_screen(start), cam.to_screen(end), source_color, destination_color)));
                    }
                }
            }
        }

        // Selection box and cut stroke.
        if let Gesture::Box { start, current, moved: true, .. } = &self.gesture {
            let area = Rect::from_two_pos(*start, *current);
            painter.rect(area, 0.0, Color32::from_rgba_unmultiplied(220, 220, 220, 28), Stroke::new(1.0, theme::NODE_SELECTED), egui::StrokeKind::Inside);
        }
        if let Gesture::Cut { points, .. } = &self.gesture {
            for segment in dashed(points, 6.0, 6.0) {
                painter.add(egui::Shape::line(segment, Stroke::new(2.0, theme::NODE_SELECTED)));
            }
        }

        // Empty workspace.
        if graph.nodes.is_empty() {
            let button = Rect::from_center_size(rect.center() - Vec2::new(0.0, 16.0), Vec2::new(130.0, 26.0));
            let enabled = mixer.can_edit_layout();
            if widgets::button(ui, background_id.with("empty-add"), button, "+  Add node", widgets::ButtonStyle::primary(13.0), enabled, 1.0)
                .tip("Opens the node library.")
                .clicked()
            {
                let center = self.visible_center();
                self.open_add_menu(mixer, button.left_bottom(), center);
            }
            painter.text(button.center_bottom() + Vec2::new(0.0, 10.0), Align2::CENTER_TOP, "Connect an input to an output.", theme::font(13.0, Weight::Regular), theme::MUTED);
        }

        // Minimap.
        let minimap = self.show_minimap(ui, mixer, rect);

        // ---- input ----
        let (primary_pressed, secondary_pressed, middle_pressed, primary_down, secondary_down, middle_down, modifiers, space) = ctx.input(|i| {
            (
                i.pointer.button_pressed(PointerButton::Primary),
                i.pointer.button_pressed(PointerButton::Secondary),
                i.pointer.button_pressed(PointerButton::Middle),
                i.pointer.button_down(PointerButton::Primary),
                i.pointer.button_down(PointerButton::Secondary),
                i.pointer.button_down(PointerButton::Middle),
                i.modifiers,
                i.key_down(Key::Space),
            )
        });
        let press_origin = ctx.input(|i| i.pointer.press_origin());
        let in_canvas = pointer.is_some_and(|p| rect.contains(p)) && !minimap.is_some_and(|m| pointer.is_some_and(|p| m.contains(p)));
        let over_popup = pointer.is_some_and(|p| ctx.layer_id_at(p).is_some_and(|l| l.order == egui::Order::Foreground || l.order == egui::Order::Tooltip));
        // A disabled canvas (an overlay is open) takes no keys or wheel.
        let interactive = ui.is_enabled();
        let canvas_keys = interactive && self.owns_keyboard(&ctx, background_id);

        // Node actions first (they may start gestures).
        let mut press: Option<Press> = None;
        for (id, action) in actions {
            match action {
                Action::Edit(node_id, change) => {
                    mixer.edit(move |w| {
                        if let Some(node) = w.node_mut(&node_id) {
                            change(node);
                        }
                    });
                }
                Action::BeginGesture => mixer.begin_edit_gesture(None),
                Action::EndGesture => mixer.end_edit_gesture(),
                Action::Message(text, error) => self.set_message(mixer, &text, error),
                Action::Press { modifiers, pointer } => {
                    if press.is_none() && !space && primary_pressed {
                        // Within snap reach of a socket, the socket wins
                        // over the node's drag surfaces (`CanvasPreviewMouseLeftButtonDown`).
                        let pending = self.pending.clone();
                        press = Some(match self.find_snap(mixer, pointer, pending.as_ref()).filter(|_| mixer.can_edit_layout() && modifiers.is_none()) {
                            Some(socket) => Press::Socket(socket, pointer),
                            None => Press::Node(id.clone(), pointer, modifiers),
                        });
                    }
                }
                Action::Rename(name) => {
                    let node_id = id.clone();
                    mixer.edit(move |w| {
                        if let Some(node) = w.node_mut(&node_id) {
                            node.name = name.clone();
                        }
                    });
                }
                Action::ContextMenu => {
                    if let Some(p) = pointer {
                        self.open_node_menu(mixer, &id, p);
                    }
                }
                Action::Reveal => {
                    self.reveal_node(&ctx, mixer, &id);
                }
            }
        }
        if press.is_none() && primary_pressed && !space {
            for (socket, response) in &socket_responses {
                if response.is_pointer_button_down_on() {
                    press = Some(Press::Socket(socket.clone(), press_origin.unwrap_or_default()));
                    break;
                }
            }
        }
        if press.is_none() && primary_pressed && !space {
            for (id, response, screen) in &frame_responses {
                if response.is_pointer_button_down_on() {
                    let origin = press_origin.unwrap_or_default();
                    let header = origin.y <= screen.top() + super::FRAME_HEADER * s;
                    press = Some(Press::Frame(id.clone(), origin, header && response.double_clicked()));
                    break;
                }
            }
        }
        if press.is_none() && primary_pressed && background.is_pointer_button_down_on() && !space {
            press = Some(Press::Background(press_origin.unwrap_or_default()));
        }
        for (id, response, _) in &frame_responses {
            if response.double_clicked() {
                if let Some(origin) = press_origin {
                    let screen = frame_responses.iter().find(|(f, _, _)| f == id).map(|(_, _, r)| *r).unwrap_or(Rect::NOTHING);
                    if origin.y <= screen.top() + super::FRAME_HEADER * s {
                        self.begin_frame_rename(&ctx, mixer, id);
                    }
                }
            }
            if response.secondary_clicked() && !matches!(self.gesture, Gesture::Pan { .. }) {
                self.select_frame(mixer, id);
                self.menu = Some((Menu::Frame(id.clone()), pointer.unwrap_or_default()));
                self.menu_opened = true;
            }
        }

        // Pan start: middle button or Space+left anywhere on the canvas.
        if (middle_pressed || (primary_pressed && space)) && in_canvas && !over_popup && matches!(self.gesture, Gesture::None) {
            self.cancel_active_gesture(mixer);
            let button = if middle_pressed { PointerButton::Middle } else { PointerButton::Primary };
            self.gesture = Gesture::Pan { button, start: pointer.unwrap_or_default(), original: self.pan, panning: true };
            background.request_focus();
            press = None;
        }
        if let Some(press) = press {
            if !mixer.can_edit() && !matches!(press, Press::Background(_)) {
                // Nothing editable while the engine is unavailable.
            } else {
                match press {
                    Press::Node(id, p, modifiers) => {
                        background.request_focus();
                        self.begin_node_pointer(mixer, &id, p, modifiers);
                    }
                    Press::Socket(socket, p) => {
                        background.request_focus();
                        self.socket_pressed(mixer, socket, p);
                    }
                    Press::Frame(id, p, rename) => {
                        if !(modifiers.shift || modifiers.ctrl) {
                            if self.has_active_gesture() {
                                self.cancel_active_gesture(mixer);
                            }
                            self.select_frame(mixer, &id);
                            background.request_focus();
                            if rename {
                                self.begin_frame_rename(&ctx, mixer, &id);
                            } else if mixer.is_layout_locked() {
                                self.gesture = Gesture::LockedLayout { start: p, notified: false };
                            } else if let Some(first) = frames_list.iter().find(|f| f.id == id).and_then(|f| f.node_ids.first().cloned()) {
                                if self.require_layout_edit(mixer) {
                                    self.begin_node_move(mixer, &first, p, false, false, true);
                                    self.selected_frame = Some(id);
                                }
                            }
                        }
                    }
                    Press::Background(p) => {
                        background.request_focus();
                        if mixer.can_edit() {
                            let pending = self.pending.clone();
                            if let Some(socket) = self.find_snap(mixer, p, pending.as_ref()).filter(|_| mixer.can_edit_layout()) {
                                self.socket_pressed(mixer, socket, p);
                            } else if let Some(edge) = self.edge_at(mixer, p) {
                                self.wire_pressed(mixer, &edge, p);
                            } else if self.pending.is_some() && !matches!(self.gesture, Gesture::Socket { dragging: true, .. }) {
                                self.cancel_cable();
                                self.set_message(mixer, "Connection cancelled.", false);
                            } else {
                                self.begin_box(mixer, p, modifiers.shift || modifiers.ctrl);
                            }
                        }
                    }
                }
            }
        }

        // Right button: cut (Ctrl), pan preference, or menus.
        if secondary_pressed && in_canvas && !over_popup && matches!(self.gesture, Gesture::None) {
            let p = pointer.unwrap_or_default();
            if modifiers.ctrl && mixer.can_edit() {
                if self.require_layout_edit(mixer) {
                    self.begin_cut(mixer, p);
                }
            } else if mixer.right_click_drag_to_pan() && background.hovered() && self.edge_at(mixer, p).is_none() {
                self.cancel_active_gesture(mixer);
                self.gesture = Gesture::Pan { button: PointerButton::Secondary, start: p, original: self.pan, panning: false };
                background.request_focus();
            }
        }
        if background.secondary_clicked() && matches!(self.gesture, Gesture::None) && !modifiers.ctrl {
            if let Some(p) = pointer {
                if let Some(edge) = self.edge_at(mixer, p) {
                    self.select_edge(mixer, &edge);
                    self.menu = Some((Menu::Edge(edge), p));
                } else if !mixer.right_click_drag_to_pan() {
                    let world = self.camera().to_world(p);
                    self.menu = Some((Menu::Canvas(world), p));
                }
                self.menu_opened = true;
            }
        }

        // Gesture progress.
        if let Some(p) = pointer {
            self.update_gesture(&ctx, mixer, p, modifiers);
        }
        // Gesture completion.
        let released = match &self.gesture {
            Gesture::Pan { button, .. } => !ctx.input(|i| i.pointer.button_down(*button)),
            Gesture::Cut { .. } => !secondary_down,
            Gesture::Minimap { .. } | Gesture::NodeMove { .. } | Gesture::Box { .. } | Gesture::LockedLayout { .. } => !primary_down,
            Gesture::Socket { dragging: true, .. } => !primary_down,
            _ => false,
        };
        if released {
            let p = pointer.unwrap_or_else(|| self.last_pointer_screen());
            match self.gesture.clone() {
                Gesture::Pan { button, start, panning, original } => {
                    if button == PointerButton::Secondary && !panning && rect.contains(p) {
                        self.gesture = Gesture::None;
                        let world = self.camera().to_world(start);
                        self.menu = Some((Menu::Canvas(world), start));
                        self.menu_opened = true;
                    } else {
                        self.gesture = Gesture::Pan { button, start, panning, original };
                        self.finish_gesture(mixer, Some(p), false);
                    }
                }
                Gesture::Cut { .. } => {
                    self.update_cut(mixer, p);
                    self.finish_cut(mixer, false);
                }
                Gesture::Socket { .. } => {
                    let blank = background.hovered() && self.find_snap(mixer, p, None).is_none() && self.edge_at(mixer, p).is_none();
                    self.finish_connection(mixer, p, blank);
                }
                Gesture::Minimap { last, moved, grab, projection, .. } => {
                    if !moved {
                        let center = projection.to_world(last);
                        self.center_on(center);
                    }
                    let _ = grab;
                    self.finish_gesture(mixer, Some(p), false);
                }
                Gesture::LockedLayout { .. } => self.gesture = Gesture::None,
                _ => self.finish_gesture(mixer, Some(p), false),
            }
        }
        if !primary_down && !middle_down && !secondary_down {
            if let Gesture::Socket { dragging: false, .. } = self.gesture {
                self.gesture = Gesture::None;
            }
        }

        // Hover snapping highlight.
        if matches!(self.gesture, Gesture::None | Gesture::Socket { .. }) {
            if let Some(p) = pointer {
                if self.pending.is_some() && self.input_link.is_none() {
                    let pending = self.pending.clone();
                    self.snap = self.find_snap(mixer, p, pending.as_ref());
                } else if self.pending.is_none() && mixer.can_edit_layout() && (background.hovered() || surfaces.values().any(egui::Response::hovered)) {
                    self.snap = self.find_snap(mixer, p, None);
                } else if self.pending.is_none() {
                    self.snap = None;
                }
            }
        }

        // Wheel zoom.
        if interactive && in_canvas && !over_popup {
            let delta = ctx.input(|i| {
                i.events
                    .iter()
                    .filter_map(|e| match e {
                        egui::Event::MouseWheel { unit, delta, .. } => Some(match unit {
                            egui::MouseWheelUnit::Line => (delta.y * 120.0) as i32,
                            egui::MouseWheelUnit::Page => (delta.y * 360.0) as i32,
                            egui::MouseWheelUnit::Point => (delta.y * 120.0 / 50.0) as i32,
                        }),
                        _ => None,
                    })
                    .sum::<i32>()
            });
            if delta != 0 {
                if self.has_wheel_blocking_gesture() {
                    // Ignored during drags, matching the original.
                } else if let Some(p) = pointer {
                    self.apply_wheel(&ctx, mixer, p, delta);
                }
            }
        }

        // Keyboard.
        if canvas_keys {
            self.handle_keys(&ctx, mixer);
        }
        self.commit_nudge(mixer);
        if !self.has_active_gesture() {
            self.snap = self.snap.take().filter(|s| self.is_current_socket(mixer, s));
        }
        if self.has_active_gesture() || self.feedback.deadline > 0.0 {
            ctx.request_repaint();
        }
        self.show_menus(ui, mixer);
    }

    fn has_wheel_blocking_gesture(&self) -> bool {
        matches!(self.gesture, Gesture::NodeMove { .. } | Gesture::LockedLayout { .. } | Gesture::Box { .. } | Gesture::Pan { .. } | Gesture::Minimap { .. } | Gesture::Cut { .. })
            || matches!(self.gesture, Gesture::Socket { dragging: true, .. })
    }

    /// The canvas owns keys when it, or nothing, holds focus; a focused
    /// text field or control keeps its own keys.
    fn owns_keyboard(&self, ctx: &egui::Context, background: Id) -> bool {
        let owns = ctx.memory(|m| m.focused().is_none_or(|f| f == background));
        if owns {
            // Arrows nudge or pan instead of moving focus between widgets.
            ctx.memory_mut(|m| m.set_focus_lock_filter(background, egui::EventFilter { tab: false, horizontal_arrows: true, vertical_arrows: true, escape: true }));
        }
        owns
    }

    pub fn visible_center(&self) -> Pos2 {
        let cam = self.camera();
        cam.to_world(self.viewport.min + Vec2::new(self.viewport.width() * 0.45, self.viewport.height() * 0.35))
    }

    fn center_on(&mut self, center: Pos2) {
        self.pan = Vec2::new(self.viewport.width() / 2.0 - center.x * self.zoom, self.viewport.height() / 2.0 - center.y * self.zoom);
    }

    fn begin_box(&mut self, mixer: &MixerState, start: Pos2, additive: bool) {
        self.cancel_cable();
        let original: std::collections::HashSet<String> = self.selected.iter().cloned().collect();
        self.box_stacking = Some(self.stacking.clone());
        if !additive {
            self.set_selection(mixer, &[], true, false);
        }
        let base = if additive { original.clone() } else { std::collections::HashSet::new() };
        self.gesture = Gesture::Box { start, current: start, base, original, moved: false };
    }

    #[allow(clippy::too_many_lines)]
    fn update_gesture(&mut self, ctx: &egui::Context, mixer: &mut MixerState, pointer: Pos2, modifiers: egui::Modifiers) {
        match self.gesture.clone() {
            Gesture::NodeMove { .. } => {
                self.update_node_move(mixer, pointer);
                ctx.set_cursor_icon(egui::CursorIcon::Move);
            }
            Gesture::LockedLayout { start, notified } => {
                if !notified && pointer.distance(start) > 3.0 && mixer.is_layout_locked() {
                    self.gesture = Gesture::LockedLayout { start, notified: true };
                    self.require_layout_edit(mixer);
                }
            }
            Gesture::Box { start, base, original, moved, .. } => {
                let moved = moved || pointer.distance(start) > 3.0;
                self.gesture = Gesture::Box { start, current: pointer, base: base.clone(), original, moved };
                if moved {
                    let area = Rect::from_two_pos(self.camera().to_world(start), self.camera().to_world(pointer));
                    let graph = mixer.graph().clone();
                    let mut selection: Vec<String> = base.iter().cloned().collect();
                    for node in &graph.nodes {
                        if self.node_bounds(node).is_some_and(|b| b.intersects(area)) && !selection.contains(&node.id) {
                            selection.push(node.id.clone());
                        }
                    }
                    let keep = self.gesture.clone();
                    self.set_selection(mixer, &selection, false, false);
                    self.gesture = keep;
                    if let Some(before) = &self.box_stacking {
                        self.stacking = before.clone();
                        let raise: Vec<String> = self.selected.iter().filter(|id| !base.contains(*id)).cloned().collect();
                        self.bring_to_front(&raise, None);
                    }
                }
            }
            Gesture::Pan { button, start, original, panning } => {
                let delta = pointer - start;
                if !panning && delta.length() < super::RIGHT_PAN_THRESHOLD {
                    return;
                }
                self.gesture = Gesture::Pan { button, start, original, panning: true };
                self.pan = original + delta;
                self.apply_view(ctx);
                ctx.set_cursor_icon(egui::CursorIcon::AllScroll);
            }
            Gesture::Socket { start, dragging, moved } => {
                self.pointer_world = self.camera().to_world(pointer);
                let moved = moved || pointer.distance(start) > 3.0;
                self.gesture = Gesture::Socket { start, dragging, moved };
                if self.input_link.is_some() {
                    self.choose_incoming_link(mixer, pointer);
                } else {
                    let pending = self.pending.clone();
                    self.snap = self.find_snap(mixer, pointer, pending.as_ref());
                }
            }
            Gesture::Cut { .. } => {
                self.update_cut(mixer, pointer);
                ctx.set_cursor_icon(egui::CursorIcon::Crosshair);
            }
            Gesture::Minimap { grab, start, original, projection, moved, .. } => {
                let moved = moved || pointer.distance(start) > 3.0;
                let center = projection.to_world(self.minimap_local(pointer)) - grab;
                self.gesture = Gesture::Minimap { grab, start, last: pointer, moved, original, projection };
                self.center_on(center);
                self.apply_view(ctx);
            }
            Gesture::None => {}
        }
        let _ = modifiers;
    }

    fn paint_grid(&self, painter: &egui::Painter, rect: Rect) {
        painter.rect_filled(rect, 0.0, theme::CANVAS);
        let mut spacing = 20.0 * self.zoom;
        while spacing < 16.0 {
            spacing *= 2.0;
        }
        let offset = Vec2::new(self.pan.x.rem_euclid(spacing), self.pan.y.rem_euclid(spacing));
        let ink = theme::with_opacity(theme::GRID, 0.8);
        let mut y = rect.top() + offset.y;
        let mut mesh = egui::Mesh::default();
        while y < rect.bottom() {
            let mut x = rect.left() + offset.x;
            while x < rect.right() {
                mesh.add_colored_rect(Rect::from_min_size(Pos2::new(x, y), Vec2::splat(1.0)), ink);
                x += spacing;
            }
            y += spacing;
        }
        painter.add(mesh);
    }

    fn paint_frame(&mut self, ui: &mut Ui, mixer: &mut MixerState, frame: &frames::Frame, screen: Rect, response: &egui::Response) {
        let s = self.zoom;
        let color = frames::color(&frame.color_key);
        let accent = Color32::from_rgb(color.accent[0], color.accent[1], color.accent[2]);
        let selected = self.selected_frame.as_deref() == Some(frame.id.as_str());
        let renaming = self.frame_rename.as_ref().is_some_and(|(id, _, _)| id == &frame.id);
        let previous = self.frame_detail.get(&frame.id).copied().unwrap_or(DetailLevel::Full);
        let detail = policy::detail_at(f64::from(s), previous, renaming);
        self.frame_detail.insert(frame.id.clone(), detail);
        let painter = ui.painter();
        painter.rect(
            screen,
            6.0 * s,
            Color32::from_rgba_unmultiplied(accent.r(), accent.g(), accent.b(), 44),
            Stroke::new(if selected { 1.5 } else { 1.0 }, if selected { theme::NODE_SELECTED } else { Color32::from_rgba_unmultiplied(accent.r(), accent.g(), accent.b(), 190) }),
            egui::StrokeKind::Inside,
        );
        let header = Rect::from_min_size(screen.min, Vec2::new(screen.width(), super::FRAME_HEADER * s));
        if detail == DetailLevel::Full {
            painter.rect_filled(
                header.shrink(1.0),
                egui::CornerRadius { nw: (6.0 * s) as u8, ne: (6.0 * s) as u8, sw: 0, se: 0 },
                Color32::from_rgba_unmultiplied(accent.r(), accent.g(), accent.b(), 34),
            );
            if renaming {
                let (id, draft, focus) = self.frame_rename.clone().unwrap_or_default();
                let mut text = draft;
                let field = header.shrink2(Vec2::new(18.0 * s, 4.0 * s));
                ui.painter().rect(field, 3.0 * s, theme::WELL, Stroke::new(1.0, theme::FOCUS), egui::StrokeKind::Inside);
                let edit_id = Id::unique(("frame-rename", id.as_str()));
                let output = egui::TextEdit::singleline(&mut text)
                    .id(edit_id)
                    .font(theme::font(24.0 * s, Weight::SemiBold))
                    .frame(egui::Frame::NONE)
                    .align(egui::Align2::CENTER_CENTER)
                    .char_limit(80)
                    .desired_width(field.width())
                    .min_size(field.size())
                    .show(&mut widgets::field_ui(ui, field));
                if focus {
                    output.response.request_focus();
                }
                let (enter, escape) = ui.input(|i| (i.key_pressed(Key::Enter), i.key_pressed(Key::Escape)));
                if escape {
                    self.frame_rename = None;
                } else if enter || output.response.lost_focus() {
                    self.frame_rename = None;
                    let name = text.trim();
                    self.rename_frame(mixer, &id, if name.is_empty() { "Group" } else { name });
                } else {
                    self.frame_rename = Some((id, text, false));
                }
            } else {
                widgets::text_elided(
                    ui.painter(),
                    header.center(),
                    Align2::CENTER_CENTER,
                    &frame.name,
                    theme::font(24.0 * s, Weight::SemiBold),
                    theme::TEXT,
                    header.width() - 36.0 * s,
                );
            }
        }
        let count = if frame.node_ids.len() == 1 { "1 node".to_owned() } else { format!("{} nodes", frame.node_ids.len()) };
        let locked = !mixer.can_edit_layout();
        response.clone().tip(format!("{}\n{}", frame.name, if locked { format!("{count} · Layout locked") } else { count }));
        if response.hovered() && !locked {
            ui.ctx().set_cursor_icon(egui::CursorIcon::Move);
        }
    }

    /// Bottom-right overview; returns its rect when shown.
    fn show_minimap(&mut self, ui: &mut Ui, mixer: &mut MixerState, viewport: Rect) -> Option<Rect> {
        let visible = mixer.show_minimap() && viewport.width() >= 120.0 && viewport.height() >= 100.0;
        if !visible {
            if matches!(self.gesture, Gesture::Minimap { .. }) {
                self.finish_gesture(mixer, None, true);
            }
            return None;
        }
        let size = Vec2::new((viewport.width() * 0.24).clamp(96.0, 220.0), (viewport.height() * 0.24).clamp(72.0, 144.0));
        let rect = Rect::from_min_size(viewport.max - size - Vec2::splat(12.0), size);
        let graph = mixer.graph().clone();
        let visible_world = Rect::from_min_size(Pos2::new(-self.pan.x / self.zoom, -self.pan.y / self.zoom), viewport.size() / self.zoom);
        let nodes: Vec<(Rect, bool)> =
            graph.nodes.iter().filter_map(|n| self.node_bounds_at(n).map(|b| (b, self.selected.contains(&n.id)))).collect();
        let projection = match &self.gesture {
            Gesture::Minimap { projection, .. } => *projection,
            _ => minimap_projection(&nodes, visible_world, size),
        };
        let response = ui.interact(rect, Id::unique("vinemix-minimap"), Sense::click_and_drag());
        widgets::describe(&response, "Minimap. Click to centre the view or drag to pan.");
        let painter = ui.painter();
        painter.rect(rect, 5.0, theme::WINDOW, Stroke::new(1.0, theme::BORDER), egui::StrokeKind::Inside);
        let clip = painter.with_clip_rect(rect.shrink(5.0));
        let to_screen = |p: Pos2| rect.min + projection.to_map(p).to_vec2();
        for (bounds, selected) in &nodes {
            clip.rect_filled(Rect::from_min_max(to_screen(bounds.min), to_screen(bounds.max)), 0.0, if *selected { theme::NODE_SELECTED } else { theme::CANVAS_OVERVIEW });
        }
        clip.rect_stroke(Rect::from_min_max(to_screen(visible_world.min), to_screen(visible_world.max)), 0.0, Stroke::new(1.5, theme::TEXT), egui::StrokeKind::Middle);
        if response.hovered() {
            ui.ctx().set_cursor_icon(egui::CursorIcon::PointingHand);
        }
        if response.is_pointer_button_down_on() && ui.input(|i| i.pointer.primary_pressed()) && self.can_frame_selection(mixer) {
            if let Some(origin) = ui.input(|i| i.pointer.press_origin()) {
                let local = origin - rect.min;
                let visible_map = Rect::from_min_max(projection.to_map(visible_world.min), projection.to_map(visible_world.max));
                let center = visible_world.center();
                let grab = if visible_map.contains(local.to_pos2()) { projection.to_world(local.to_pos2()) - center } else { Vec2::ZERO };
                self.minimap_origin = rect.min;
                self.gesture = Gesture::Minimap { grab, start: origin, last: origin, moved: false, original: self.pan, projection };
                let center = projection.to_world(local.to_pos2()) - grab;
                self.center_on(center);
            }
        }
        Some(rect)
    }

    fn minimap_local(&self, pointer: Pos2) -> Pos2 {
        (pointer - self.minimap_origin).to_pos2()
    }

    #[allow(clippy::too_many_lines)]
    fn handle_keys(&mut self, ctx: &egui::Context, mixer: &mut MixerState) {
        let events = ctx.input(|i| i.events.clone());
        for event in events {
            match event {
                egui::Event::Key { key, pressed: false, .. } => {
                    self.cancelled_nudge_keys.remove(&key);
                    if self.nudge.as_ref().is_some_and(|n| n.key == key) {
                        self.finish_nudge(mixer, false);
                    }
                }
                egui::Event::Key { key, pressed: true, repeat, modifiers, .. } => {
                    if matches!(self.gesture, Gesture::Minimap { .. }) {
                        if key == Key::Escape {
                            self.finish_gesture(mixer, None, true);
                            self.set_message(mixer, "Pan cancelled.", false);
                        }
                        continue;
                    }
                    if matches!(key, Key::ArrowLeft | Key::ArrowRight | Key::ArrowUp | Key::ArrowDown) && (modifiers.is_none() || modifiers == egui::Modifiers::SHIFT) {
                        if self.canvas_arrow(ctx, mixer, key, modifiers.shift, repeat) {
                            continue;
                        }
                    }
                    self.finish_nudge(mixer, false);
                    let command = modifiers.command_only() || (modifiers.ctrl && !modifiers.alt && !modifiers.shift);
                    if command && matches!(key, Key::Plus | Key::Equals | Key::Minus) && self.can_frame_selection(mixer) {
                        self.wheel_remainder = 0;
                        self.step_zoom(ctx, mixer, None, if key == Key::Minus { -1 } else { 1 });
                        continue;
                    }
                    if modifiers.ctrl && key == Key::Tab {
                        self.pipeline_shortcut(mixer, !modifiers.shift);
                        continue;
                    }
                    if modifiers.is_none() && key == Key::F2 && self.can_frame_selection(mixer) {
                        if let Some(frame) = self.selected_frame.clone() {
                            self.begin_frame_rename(ctx, mixer, &frame);
                        } else if self.selected_edge.is_none() && self.selected.len() == 1 {
                            let id = self.selected[0].clone();
                            self.begin_rename(ctx, mixer, &id);
                        }
                        continue;
                    }
                    if modifiers.alt && !modifiers.ctrl && !modifiers.shift && matches!(key, Key::X | Key::Y | Key::A) {
                        let alignment = match key {
                            Key::X => editing::Alignment::Top,
                            Key::Y => editing::Alignment::Left,
                            _ => editing::Alignment::Auto,
                        };
                        if self.selected.len() >= 2 && self.require_layout_edit(mixer) {
                            self.align_selected(mixer, alignment);
                        }
                        continue;
                    }
                    if modifiers.is_none() && key == Key::F {
                        self.frame_selection(ctx, mixer);
                        continue;
                    }
                    if key == Key::Escape {
                        let had = self.has_active_gesture();
                        self.cancel_active_gesture(mixer);
                        if had {
                            self.set_message(mixer, "Gesture cancelled.", false);
                        }
                        continue;
                    }
                    if self.has_wheel_blocking_gesture() {
                        continue;
                    }
                    match (key, modifiers.ctrl, modifiers.shift) {
                        (Key::Delete | Key::Backspace, false, false) => self.delete_selection(mixer),
                        (Key::G, true, false) => {
                            self.create_frame_from_selection(mixer);
                        }
                        (Key::C, true, false) => self.copy(mixer),
                        (Key::V, true, false) => self.paste(mixer, None),
                        (Key::A, true, false) => self.select_all(mixer),
                        (Key::Home, false, false) => self.fit_all(ctx, mixer),
                        (Key::Num0, true, false) => self.reset_zoom(ctx, mixer),
                        (Key::N, true, false) | (Key::A, false, true) | (Key::F3, false, false) => {
                            let center = self.visible_center();
                            let anchor = self.viewport.min + Vec2::new(8.0, 0.0);
                            self.open_add_menu(mixer, anchor, center);
                        }
                        _ => {}
                    }
                }
                _ => {}
            }
        }
    }

    pub fn begin_rename(&mut self, ctx: &egui::Context, mixer: &mut MixerState, id: &str) {
        if !mixer.can_edit() || !self.reveal_node(ctx, mixer, id) {
            return;
        }
        let Some(node) = mixer.graph().nodes.iter().find(|n| n.id == id) else { return };
        let runtime = self.runtimes.entry(id.to_owned()).or_default();
        runtime.rename = Some(node.name.clone());
        runtime.rename_focus = true;
    }

    pub fn begin_frame_rename(&mut self, ctx: &egui::Context, mixer: &mut MixerState, id: &str) {
        if !self.require_layout_edit(mixer) {
            return;
        }
        let Some(frame) = mixer.frames().iter().find(|f| f.id == id).cloned() else { return };
        if self.frame_detail.get(id).is_some_and(|d| *d != DetailLevel::Full) {
            if !self.can_frame_selection(mixer) {
                return;
            }
            let graph = mixer.graph().clone();
            if let Some(bounds) = self.frame_bounds(&frame, &graph) {
                self.zoom = self.zoom.max(0.6);
                self.pan.x = (self.viewport.width() - bounds.width() * self.zoom) / 2.0 - bounds.left() * self.zoom;
                self.pan.y = 24.0 - bounds.top() * self.zoom;
                self.apply_view(ctx);
                self.persist_view(mixer);
            }
            self.frame_detail.insert(id.to_owned(), DetailLevel::Full);
        }
        self.frame_rename = Some((id.to_owned(), frame.name, true));
    }

    pub fn rename_frame(&mut self, mixer: &mut MixerState, id: &str, name: &str) {
        if !self.require_layout_edit(mixer) {
            return;
        }
        let value = frames::normalize_name(name);
        let id = id.to_owned();
        mixer.edit(move |w| {
            if let Some(frame) = w.frames.iter_mut().find(|f| f.id == id) {
                frame.name = value.clone();
            }
        });
    }

    pub fn align_selected(&mut self, mixer: &mut MixerState, alignment: editing::Alignment) -> bool {
        if !mixer.can_edit() || self.selected.len() < 2 || !self.require_layout_edit(mixer) {
            return false;
        }
        let selected: std::collections::HashSet<String> = self.selected.iter().cloned().collect();
        let graph = mixer.graph().clone();
        let resolved = if alignment == editing::Alignment::Auto { editing::resolve_auto(&graph, &selected) } else { alignment };
        let sizes: HashMap<String, (f64, f64)> =
            self.layouts.iter().filter(|(id, _)| selected.contains(*id)).map(|(id, l)| (id.clone(), (f64::from(l.width), f64::from(l.height)))).collect();
        if editing::alignment_plan(&graph, &selected, resolved, &sizes).is_empty() {
            return false;
        }
        let layouts: HashMap<String, (String, i32)> =
            graph.nodes.iter().filter(|n| selected.contains(&n.id)).map(|n| (n.id.clone(), (n.kind.clone(), n.dynamics_ui_version))).collect();
        let description = match (alignment, resolved) {
            (editing::Alignment::Auto, editing::Alignment::Left) => "Auto aligned nodes vertically",
            (editing::Alignment::Auto, _) => "Auto aligned nodes horizontally",
            (_, editing::Alignment::Left) => "Aligned node left edges",
            _ => "Aligned node top edges",
        };
        mixer.begin_edit_gesture(Some(description));
        let result = mixer.try_edit(move |w| {
            if w.config.graph.nodes.iter().any(|n| layouts.get(&n.id).is_some_and(|l| *l != (n.kind.clone(), n.dynamics_ui_version))) {
                return Err("A selected node changed. Try aligning again.".into());
            }
            let positions = editing::alignment_plan(&w.config.graph, &selected, resolved, &sizes);
            for node in &mut w.config.graph.nodes {
                if let Some(&(x, y)) = positions.get(&node.id) {
                    node.x = x;
                    node.y = y;
                }
            }
            Ok(())
        });
        mixer.end_edit_gesture();
        match result {
            Ok(()) => {
                let text = if resolved == editing::Alignment::Left {
                    "Selected nodes aligned in a spaced column."
                } else {
                    "Selected nodes aligned in a spaced row."
                };
                self.set_message(mixer, text, false);
                true
            }
            Err(error) => {
                self.set_message(mixer, &error, true);
                false
            }
        }
    }
}

fn minimap_projection(nodes: &[(Rect, bool)], viewport: Rect, size: Vec2) -> Projection {
    let usable = |r: &Rect| r.width() > 0.0 && r.height() > 0.0 && r.min.is_finite() && r.max.is_finite();
    let mut bounds = if usable(&viewport) { viewport } else { Rect::from_min_size(Pos2::ZERO, Vec2::new(1000.0, 600.0)) };
    for (node, _) in nodes {
        if usable(node) {
            bounds = bounds.union(*node);
        }
    }
    let bounds = bounds.expand(32.0);
    let width = (size.x - 20.0).max(1.0);
    let height = (size.y - 20.0).max(1.0);
    let scale = (width / bounds.width()).min(height / bounds.height());
    Projection {
        scale,
        offset: Vec2::new(10.0 + (width - bounds.width() * scale) / 2.0 - bounds.left() * scale, 10.0 + (height - bounds.height() * scale) / 2.0 - bounds.top() * scale),
    }
}

/// Splits a polyline into dashes of `on` length separated by `off`.
fn dashed(points: &[Pos2], on: f32, off: f32) -> Vec<Vec<Pos2>> {
    let mut segments = Vec::new();
    let mut current: Vec<Pos2> = Vec::new();
    let mut drawing = true;
    let mut remaining = on;
    for pair in points.windows(2) {
        let (mut a, b) = (pair[0], pair[1]);
        let mut length = a.distance(b);
        while length > 0.0 {
            let step = remaining.min(length);
            let next = a + (b - a) * (step / length);
            if drawing {
                if current.is_empty() {
                    current.push(a);
                }
                current.push(next);
            }
            remaining -= step;
            length -= step;
            a = next;
            if remaining <= 0.0 {
                if drawing && current.len() > 1 {
                    segments.push(std::mem::take(&mut current));
                }
                current.clear();
                drawing = !drawing;
                remaining = if drawing { on } else { off };
            }
        }
    }
    if drawing && current.len() > 1 {
        segments.push(current);
    }
    segments
}

fn socket_help(graph: &vinemix_model::config::AudioGraph, node: &GraphNode, port: &node_view::Port) -> String {
    let kind = node.kind.as_str();
    match port.kind {
        "control" => {
            if port.output {
                "Level control output\nScales connected Level knobs.".into()
            } else if graph::linked::has_legacy_connection(graph, node) {
                "Level control\nExisting link retained; use Mix for new level control.".into()
            } else {
                "Level control\nLets a Linked control scale this node's level.".into()
            }
        }
        // A silent or unavailable trigger source is an essential limitation:
        // the node then sees silence, it does not fall back to its own input.
        "sidechain" => format!(
            "Trigger\n{}\nA silent or unavailable source is a silent trigger.",
            if matches!(kind, "gate" | "expander") {
                "Detector-only audio; never heard. Without a cable the node detects its own input."
            } else {
                "The sound that causes ducking; never heard."
            }
        ),
        _ if port.output => {
            let title = if port.port == "audio" { "Audio output".to_owned() } else { format!("{} output", graph::port_label(port.port, Some(kind))) };
            let body = match port.port {
                "audio" => "Carries this node's audio.",
                "wet" if kind == "echo" => "Echoes only, independent of Mix.",
                "wet" => "Reverb only, independent of Mix.",
                _ => "This channel as a mono signal.",
            };
            format!("{title}\n{body}")
        }
        _ => {
            let title = if port.port == "audio" { "Audio input".to_owned() } else { format!("{} input", graph::port_label(port.port, Some(kind))) };
            let body = match port.port {
                "left" | "right" => "Becomes this stereo side; stereo sources are combined to mono.",
                "a" | "b" => "One of the two sources to blend.",
                _ => "Combines audio from connected outputs.",
            };
            format!("{title}\n{body}")
        }
    }
}

