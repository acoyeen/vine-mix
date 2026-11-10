//! The node workspace canvas (`GraphWorkspace`): camera, grid, groups,
//! cables, nodes, sockets, selection, gestures, keyboard and minimap.
//!
//! Gestures follow the 0.5 desktop exactly: header drag moves (snapping to
//! 8 units, dropping onto a cable inserts, Alt+drag detaches), sockets drag
//! or click-connect, an input end drags an existing cable away, Ctrl+right
//! drag cuts cables, middle or Space+left drag pans, the wheel zooms by
//! fixed stops around the pointer.

mod commands;
mod frame;
mod menus;

pub use menus::item as menu_item;

use std::collections::{HashMap, HashSet};

use eframe::egui::{self, Color32, Key, Modifiers, PointerButton, Pos2, Rect, Vec2};
use vinemix_model::config::{AudioGraph, GraphEdge, GraphNode};

use super::node_view::{self, Camera, Layout, Runtime};
use super::theme;
use crate::editing;
use crate::frames::Frame;
use crate::graph::{self, canvas as policy, canvas::DetailLevel};
use crate::mixer::{MixerState, LAYOUT_LOCKED_MESSAGE};

pub const TOOLBAR_HEIGHT: f32 = 34.0;
const SOCKET_SNAP_RADIUS: f32 = 18.0;
const RIGHT_PAN_THRESHOLD: f32 = 6.0;
const FEEDBACK_SECONDS: f64 = 5.0;
pub const FRAME_PADDING: f32 = 24.0;
pub const FRAME_HEADER: f32 = 52.0;
pub const FRAME_MINIMUM_WIDTH: f32 = 260.0;

/// A socket identity on the canvas.
#[derive(Clone, Debug, PartialEq, Eq, Hash)]
pub struct SocketRef {
    pub node: String,
    pub kind: &'static str,
    pub port: &'static str,
    pub output: bool,
}

#[derive(Clone, Debug)]
enum Gesture {
    None,
    NodeMove { anchor: String, start: Pos2, origins: Vec<(String, Pos2)>, moved: bool, detach: bool, frame: bool, delta: Vec2 },
    LockedLayout { start: Pos2, notified: bool },
    Box { start: Pos2, current: Pos2, base: HashSet<String>, original: HashSet<String>, moved: bool },
    Pan { button: PointerButton, start: Pos2, original: Vec2, panning: bool },
    Socket { start: Pos2, dragging: bool, moved: bool },
    Cut { points: Vec<Pos2>, candidates: Vec<GraphEdge>, cut: Vec<String> },
    Minimap { grab: Vec2, start: Pos2, last: Pos2, moved: bool, original: Vec2, projection: Projection },
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct Projection {
    scale: f32,
    offset: Vec2,
}

impl Projection {
    fn to_map(self, p: Pos2) -> Pos2 {
        (p.to_vec2() * self.scale + self.offset).to_pos2()
    }
    fn to_world(self, p: Pos2) -> Pos2 {
        ((p.to_vec2() - self.offset) / self.scale).to_pos2()
    }
}

#[derive(Clone, Debug, Default)]
struct Feedback {
    text: String,
    error: bool,
    deadline: f64,
    model_warning: bool,
}

#[derive(Clone, Debug)]
pub enum Menu {
    Canvas(Pos2),
    Node(String, Pos2),
    Edge(String),
    Frame(String),
    Add(Pos2),
    History,
    Pipeline,
}

#[derive(Clone, Debug)]
struct Nudge {
    key: Key,
    origins: Vec<(String, Pos2)>,
    delta: Vec2,
}

#[derive(Clone, Debug)]
pub struct PipelineChoice {
    pub node: String,
    pub edge: Option<String>,
    pub label: String,
    pub from_port: Option<String>,
    pub to_port: Option<String>,
}

/// The workspace canvas state.
pub struct Canvas {
    pub pan: Vec2,
    pub zoom: f32,
    viewport: Rect,
    wheel_remainder: i32,
    pub selected: Vec<String>,
    selected_edge: Option<String>,
    selected_frame: Option<String>,
    stacking: Vec<String>,
    box_stacking: Option<Vec<String>>,
    runtimes: HashMap<String, Runtime>,
    layouts: HashMap<String, Layout>,
    gesture: Gesture,
    pending: Option<SocketRef>,
    snap: Option<SocketRef>,
    insertion: Option<String>,
    rewire: Option<GraphEdge>,
    input_link: Option<(SocketRef, Vec<GraphEdge>)>,
    input_link_candidate: Option<String>,
    pointer_world: Pos2,
    feedback: Feedback,
    last_message: String,
    last_pasted: Option<String>,
    paste_repeat: u32,
    nudge: Option<Nudge>,
    cancelled_nudge_keys: HashSet<Key>,
    pub menu: Option<(Menu, Pos2)>,
    menu_opened: bool,
    add_search: String,
    frame_rename: Option<(String, String, bool)>,
    frame_detail: HashMap<String, DetailLevel>,
    cable_detail: DetailLevel,
    pub pipeline_choices: Vec<PipelineChoice>,
    pipeline_origin: Option<String>,
    pipeline_forward: bool,
    pipeline_return: HashMap<(String, bool), (String, String)>,
    pub wants_add_menu: bool,
    pending_nudge_commit: Option<Nudge>,
    minimap_origin: Pos2,
    egui_ctx: Option<egui::Context>,
}

impl std::fmt::Debug for Canvas {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Canvas").field("zoom", &self.zoom).finish_non_exhaustive()
    }
}

impl Default for Canvas {
    fn default() -> Self {
        Self {
            pan: Vec2::new(20.0, 20.0),
            zoom: 1.0,
            viewport: Rect::NOTHING,
            wheel_remainder: 0,
            selected: Vec::new(),
            selected_edge: None,
            selected_frame: None,
            stacking: Vec::new(),
            box_stacking: None,
            runtimes: HashMap::new(),
            layouts: HashMap::new(),
            gesture: Gesture::None,
            pending: None,
            snap: None,
            insertion: None,
            rewire: None,
            input_link: None,
            input_link_candidate: None,
            pointer_world: Pos2::ZERO,
            feedback: Feedback::default(),
            last_message: String::new(),
            last_pasted: None,
            paste_repeat: 0,
            nudge: None,
            cancelled_nudge_keys: HashSet::new(),
            menu: None,
            menu_opened: false,
            add_search: String::new(),
            frame_rename: None,
            frame_detail: HashMap::new(),
            cable_detail: DetailLevel::Full,
            pipeline_choices: Vec::new(),
            pipeline_origin: None,
            pipeline_forward: true,
            pipeline_return: HashMap::new(),
            wants_add_menu: false,
            pending_nudge_commit: None,
            minimap_origin: Pos2::ZERO,
            egui_ctx: None,
        }
    }
}

fn now() -> f64 {
    static START: std::sync::OnceLock<std::time::Instant> = std::sync::OnceLock::new();
    START.get_or_init(std::time::Instant::now).elapsed().as_secs_f64()
}

fn to_point(p: Pos2) -> editing::Point {
    (f64::from(p.x), f64::from(p.y))
}

fn from_point(p: editing::Point) -> Pos2 {
    Pos2::new(p.0 as f32, p.1 as f32)
}

/// Gradient stroke along the from→to axis (absolute world mapping).
fn gradient_stroke(width: f32, from: Pos2, to: Pos2, source: Color32, destination: Color32) -> egui::epaint::PathStroke {
    if source == destination || from.distance_sq(to) < 1e-12 {
        return egui::epaint::PathStroke::new(width, source);
    }
    let axis = to - from;
    let length = axis.length_sq();
    egui::epaint::PathStroke::new_uv(width, move |_, p| {
        let t = ((p - from).dot(axis) / length).clamp(0.0, 1.0);
        theme::mix(source, destination, t)
    })
}

impl Canvas {
    pub fn camera(&self) -> Camera {
        Camera { origin: self.viewport.min, pan: self.pan, zoom: self.zoom }
    }

    pub fn viewport(&self) -> Rect {
        self.viewport
    }

    pub fn has_active_gesture(&self) -> bool {
        self.nudge.is_some() || self.pending.is_some() || !matches!(self.gesture, Gesture::None)
    }

    pub fn can_frame_selection(&self, mixer: &MixerState) -> bool {
        self.nudge.is_none() && matches!(self.gesture, Gesture::None) && self.pending.is_none() && !mixer.history.has_gesture_edit()
    }

    // ----- messages ------------------------------------------------------

    pub fn set_message(&mut self, mixer: &MixerState, text: &str, error: bool) {
        if text.trim().is_empty() {
            self.dismiss_message();
            return;
        }
        self.last_message = text.to_owned();
        if !error && !mixer.show_action_notifications() {
            return;
        }
        self.feedback = Feedback {
            text: if error { format!("Attention: {text}") } else { text.to_owned() },
            error,
            deadline: now() + FEEDBACK_SECONDS,
            model_warning: false,
        };
    }

    /// A message the user asked to hear about (update results): shown even
    /// when action notifications are off.
    pub fn set_notice(&mut self, text: &str) {
        self.last_message = text.to_owned();
        self.feedback = Feedback { text: text.to_owned(), error: false, deadline: now() + FEEDBACK_SECONDS, model_warning: false };
    }

    pub fn dismiss_message(&mut self) {
        self.feedback = Feedback::default();
    }

    pub fn dismiss_unless_error(&mut self) {
        if !self.feedback.error {
            self.dismiss_message();
        }
    }

    /// The footer message, its error state and seconds remaining.
    pub fn feedback_view(&self) -> Option<(String, bool, f64)> {
        if self.feedback.text.is_empty() {
            return None;
        }
        let remaining = self.feedback.deadline - now();
        if remaining <= 0.0 {
            return None;
        }
        Some((self.feedback.text.clone(), self.feedback.error, remaining))
    }

    /// Any numeric draft that would not apply (`UpdateEditorCommit`).
    pub fn has_invalid_drafts(&self) -> bool {
        self.runtimes.values().any(|r| r.dials.values().any(|d| d.invalid))
    }

    pub fn last_message(&self) -> &str {
        &self.last_message
    }

    /// Mirrors the model warning into the footer (`ModelPropertyChanged`).
    pub fn sync_warning(&mut self, mixer: &MixerState, previous: &str) {
        if mixer.warning != previous {
            if !mixer.warning.trim().is_empty() {
                let warning = mixer.warning.clone();
                self.set_message(mixer, &warning, true);
                self.feedback.model_warning = true;
            } else if self.feedback.model_warning {
                self.dismiss_message();
            }
        }
    }

    // ----- camera ----------------------------------------------------------

    fn sync_from_graph(&mut self, graph: &AudioGraph) {
        if matches!(self.gesture, Gesture::None | Gesture::Socket { .. } | Gesture::Cut { .. } | Gesture::LockedLayout { .. }) {
            self.pan = Vec2::new(if graph.pan_x.is_finite() { graph.pan_x as f32 } else { 20.0 }, if graph.pan_y.is_finite() { graph.pan_y as f32 } else { 20.0 });
            self.zoom = policy::clamp_zoom(graph.zoom) as f32;
        }
    }

    fn apply_view(&mut self, ctx: &egui::Context) {
        let ppp = ctx.pixels_per_point();
        let round = |v: f32| (v * ppp).round() / ppp;
        self.pan.x = policy::clamp_pan(f64::from(round(self.pan.x))) as f32;
        self.pan.y = policy::clamp_pan(f64::from(round(self.pan.y))) as f32;
    }

    fn persist_view(&mut self, mixer: &mut MixerState) {
        let graph = mixer.graph();
        let (x, y, z) = (f64::from(self.pan.x), f64::from(self.pan.y), f64::from(self.zoom));
        if graph.pan_x == x && graph.pan_y == y && graph.zoom == z {
            return;
        }
        mixer.set_canvas_view(x, y, z);
    }

    fn zoom_at(&mut self, ctx: &egui::Context, mixer: &mut MixerState, anchor: Pos2, zoom: f64) {
        let next = policy::clamp_zoom(zoom);
        if f64::from(self.zoom) == next {
            return;
        }
        let local = anchor - self.viewport.min;
        let view = policy::zoom_around(
            policy::View { pan_x: f64::from(self.pan.x), pan_y: f64::from(self.pan.y), zoom: f64::from(self.zoom) },
            (f64::from(local.x), f64::from(local.y)),
            next,
        );
        self.zoom = view.zoom as f32;
        self.pan = Vec2::new(view.pan_x as f32, view.pan_y as f32);
        self.apply_view(ctx);
        self.persist_view(mixer);
    }

    pub fn reset_zoom(&mut self, ctx: &egui::Context, mixer: &mut MixerState) {
        self.wheel_remainder = 0;
        let center = self.viewport.center();
        self.zoom_at(ctx, mixer, center, 1.0);
    }

    pub fn step_zoom(&mut self, ctx: &egui::Context, mixer: &mut MixerState, anchor: Option<Pos2>, steps: i32) {
        let anchor = anchor.unwrap_or_else(|| self.viewport.center());
        let mut next = f64::from(self.zoom);
        for _ in 0..steps.unsigned_abs().min(policy::ZOOM_STOPS.len() as u32) {
            next = if steps > 0 {
                policy::ZOOM_STOPS.iter().copied().find(|&v| v > next + 0.0001).unwrap_or(policy::ZOOM_STOPS[policy::ZOOM_STOPS.len() - 1])
            } else {
                policy::ZOOM_STOPS.iter().rev().copied().find(|&v| v < next - 0.0001).unwrap_or(policy::ZOOM_STOPS[0])
            };
        }
        self.zoom_at(ctx, mixer, anchor, next);
    }

    fn apply_wheel(&mut self, ctx: &egui::Context, mixer: &mut MixerState, anchor: Pos2, delta: i32) {
        if delta == 0 {
            return;
        }
        if delta.signum() != self.wheel_remainder.signum() {
            self.wheel_remainder = 0;
        }
        let accumulated = i64::from(self.wheel_remainder) + i64::from(delta);
        let steps = (accumulated / 120).clamp(-(policy::ZOOM_STOPS.len() as i64), policy::ZOOM_STOPS.len() as i64) as i32;
        self.wheel_remainder = (accumulated % 120) as i32;
        if steps != 0 {
            self.step_zoom(ctx, mixer, Some(anchor), steps);
        }
    }

    fn node_bounds(&self, node: &GraphNode) -> Option<Rect> {
        self.layouts.get(&node.id).map(|l| Rect::from_min_size(Pos2::new(node.x as f32, node.y as f32), Vec2::new(l.width, l.height)))
    }

    fn frame_bounds(&self, frame: &Frame, graph: &AudioGraph) -> Option<Rect> {
        let mut members: Option<Rect> = None;
        for id in &frame.node_ids {
            let Some(node) = graph.nodes.iter().find(|n| &n.id == id) else { continue };
            let Some(bounds) = self.node_bounds_at(node) else { continue };
            members = Some(members.map_or(bounds, |m| m.union(bounds)));
        }
        let members = members?;
        Some(Rect::from_min_size(
            Pos2::new(members.left() - FRAME_PADDING, members.top() - FRAME_PADDING - FRAME_HEADER),
            Vec2::new((members.width() + FRAME_PADDING * 2.0).max(FRAME_MINIMUM_WIDTH), members.height() + FRAME_PADDING * 2.0 + FRAME_HEADER),
        ))
    }

    /// The node's displayed position, including an in-progress drag.
    fn displayed_origin(&self, node: &GraphNode) -> Pos2 {
        let base = Pos2::new(node.x as f32, node.y as f32);
        if let Gesture::NodeMove { origins, moved: true, delta, .. } = &self.gesture {
            if let Some((_, origin)) = origins.iter().find(|(id, _)| id == &node.id) {
                return *origin + *delta;
            }
        }
        if let Some(nudge) = &self.nudge {
            if let Some((_, origin)) = nudge.origins.iter().find(|(id, _)| id == &node.id) {
                return *origin + nudge.delta;
            }
        }
        base
    }

    fn node_bounds_at(&self, node: &GraphNode) -> Option<Rect> {
        self.layouts.get(&node.id).map(|l| Rect::from_min_size(self.displayed_origin(node), Vec2::new(l.width, l.height)))
    }

    fn last_pointer_screen(&self) -> Pos2 {
        self.camera().to_screen(self.pointer_world)
    }

    /// Fits every node (and groups wholly selected) into view.
    pub fn fit_all(&mut self, ctx: &egui::Context, mixer: &mut MixerState) {
        self.wheel_remainder = 0;
        let graph = mixer.graph().clone();
        if graph.nodes.is_empty() {
            self.pan = Vec2::new(20.0, 20.0);
            self.zoom = 1.0;
            self.apply_view(ctx);
            self.persist_view(mixer);
            return;
        }
        let ids: Vec<String> = graph.nodes.iter().map(|n| n.id.clone()).collect();
        self.frame_ids(ctx, mixer, &ids);
    }

    fn frame_ids(&mut self, ctx: &egui::Context, mixer: &mut MixerState, ids: &[String]) -> bool {
        let graph = mixer.graph().clone();
        let mut bounds: Option<Rect> = None;
        for node in graph.nodes.iter().filter(|n| ids.contains(&n.id)) {
            if let Some(b) = self.node_bounds(node) {
                bounds = Some(bounds.map_or(b, |x| x.union(b)));
            }
        }
        let Some(mut bounds) = bounds else { return false };
        for frame in mixer.frames().iter().filter(|f| f.node_ids.iter().all(|id| ids.contains(id))) {
            if let Some(b) = self.frame_bounds(frame, &graph) {
                bounds = bounds.union(b);
            }
        }
        let available = ((self.viewport.width() - 64.0).max(0.0), (self.viewport.height() - 54.0).max(0.0));
        let zoom = policy::fit_zoom(
            Some((f64::from(bounds.left()), f64::from(bounds.top()), f64::from(bounds.width()), f64::from(bounds.height()))),
            (f64::from(available.0), f64::from(available.1)),
        );
        let view = policy::centre(
            (f64::from(bounds.left()), f64::from(bounds.top()), f64::from(bounds.width()), f64::from(bounds.height())),
            (f64::from(self.viewport.width()), f64::from(self.viewport.height())),
            zoom,
        );
        self.zoom = zoom as f32;
        self.pan = Vec2::new(view.pan_x as f32, view.pan_y as f32);
        self.apply_view(ctx);
        self.persist_view(mixer);
        true
    }

    pub fn frame_selection(&mut self, ctx: &egui::Context, mixer: &mut MixerState) -> bool {
        if !self.can_frame_selection(mixer) {
            return false;
        }
        let ids: Vec<String> = self.selected.iter().filter(|id| mixer.graph().nodes.iter().any(|n| &n.id == *id)).cloned().collect();
        if ids.is_empty() {
            self.set_message(mixer, "Select one or more nodes to frame.", false);
            return false;
        }
        self.wheel_remainder = 0;
        self.frame_ids(ctx, mixer, &ids)
    }

    pub fn find_and_frame_node(&mut self, ctx: &egui::Context, mixer: &mut MixerState, id: &str) -> bool {
        if !self.can_frame_selection(mixer) || !mixer.graph().nodes.iter().any(|n| n.id == id) || !self.layouts.contains_key(id) {
            return false;
        }
        self.select_node(mixer, id);
        self.frame_selection(ctx, mixer)
    }

    /// Zooms to at least 60% and centres a node so its controls are usable.
    pub fn reveal_node(&mut self, ctx: &egui::Context, mixer: &mut MixerState, id: &str) -> bool {
        if !self.can_frame_selection(mixer) {
            return false;
        }
        let Some(node) = mixer.graph().nodes.iter().find(|n| n.id == id).cloned() else { return false };
        let Some(bounds) = self.node_bounds(&node) else { return false };
        self.zoom = self.zoom.max(0.6);
        let centred_x = (self.viewport.width() - bounds.width() * self.zoom) / 2.0 - bounds.left() * self.zoom;
        let centred_y = (self.viewport.height() - bounds.height() * self.zoom) / 2.0 - bounds.top() * self.zoom;
        if self.zoom > 1.0 && (f64::from(centred_x.abs()) > policy::MAXIMUM_PAN || f64::from(centred_y.abs()) > policy::MAXIMUM_PAN) {
            self.zoom = 1.0;
        }
        self.pan.x = (self.viewport.width() - bounds.width() * self.zoom) / 2.0 - bounds.left() * self.zoom;
        self.pan.y = ((self.viewport.height() - bounds.height() * self.zoom) / 2.0).max(24.0) - bounds.top() * self.zoom;
        self.apply_view(ctx);
        self.persist_view(mixer);
        true
    }

    // ----- selection & stacking --------------------------------------------

    fn reconcile_stacking(&mut self, graph: &AudioGraph) {
        let present: HashSet<&str> = graph.nodes.iter().map(|n| n.id.as_str()).collect();
        self.stacking.retain(|id| present.contains(id.as_str()));
        for node in &graph.nodes {
            if !self.stacking.contains(&node.id) {
                self.stacking.push(node.id.clone());
            }
        }
    }

    fn bring_to_front(&mut self, ids: &[String], anchor: Option<&str>) {
        let mut raised: Vec<String> = self.stacking.iter().filter(|id| ids.contains(id)).cloned().collect();
        if raised.is_empty() {
            return;
        }
        self.stacking.retain(|id| !ids.contains(id));
        if let Some(anchor) = anchor {
            if let Some(position) = raised.iter().position(|id| id == anchor) {
                let a = raised.remove(position);
                raised.push(a);
            }
        }
        self.stacking.extend(raised);
    }

    fn set_selection(&mut self, mixer: &MixerState, ids: &[String], raise_new: bool, promote_whole: bool) {
        self.finish_nudge(mixer, false);
        self.pipeline_return.clear();
        self.selected_frame = None;
        let next: Vec<String> = ids.iter().filter(|id| mixer.graph().nodes.iter().any(|n| &n.id == *id)).cloned().fold(Vec::new(), |mut acc, id| {
            if !acc.contains(&id) {
                acc.push(id);
            }
            acc
        });
        if raise_new {
            let raise: Vec<String> = if promote_whole { next.clone() } else { next.iter().filter(|id| !self.selected.contains(id)).cloned().collect() };
            self.bring_to_front(&raise, None);
        }
        self.selected = next;
        self.selected_edge = None;
    }

    pub fn select_node(&mut self, mixer: &MixerState, id: &str) {
        self.set_selection(mixer, &[id.to_owned()], true, false);
        self.bring_to_front(&[id.to_owned()], None);
    }

    pub fn select_all(&mut self, mixer: &MixerState) {
        let ids: Vec<String> = mixer.graph().nodes.iter().map(|n| n.id.clone()).collect();
        self.set_selection(mixer, &ids, true, true);
    }

    fn select_node_for_pointer(&mut self, mixer: &MixerState, id: &str, modifiers: Modifiers) {
        self.selected_frame = None;
        if modifiers.shift || modifiers.ctrl {
            let mut next = self.selected.clone();
            if let Some(position) = next.iter().position(|x| x == id) {
                next.remove(position);
            } else {
                next.push(id.to_owned());
            }
            self.set_selection(mixer, &next, true, false);
            if next.iter().any(|x| x == id) {
                self.bring_to_front(&[id.to_owned()], None);
            }
        } else if !self.selected.iter().any(|x| x == id) {
            self.select_node(mixer, id);
        } else {
            self.bring_to_front(&[id.to_owned()], None);
        }
    }

    fn select_edge(&mut self, mixer: &MixerState, id: &str) {
        self.selected_frame = None;
        self.selected_edge = Some(id.to_owned());
        self.selected.clear();
        let Some(edge) = mixer.graph().edges.iter().find(|e| e.id == id) else { return };
        let (title, route, state, _) = describe_connection(mixer.graph(), edge);
        let tail = if mixer.is_layout_locked() { "Layout locked; sound controls remain available." } else { "Delete removes this cable." };
        self.set_message(mixer, &format!("{route} · {title} · {state} · {tail}"), false);
    }

    pub fn select_frame(&mut self, mixer: &MixerState, id: &str) {
        let Some(frame) = mixer.frames().iter().find(|f| f.id == id).cloned() else { return };
        self.set_selection(mixer, &frame.node_ids, false, false);
        self.bring_to_front(&frame.node_ids, None);
        self.selected_frame = Some(id.to_owned());
    }

    pub fn selected_frame(&self) -> Option<&str> {
        self.selected_frame.as_deref()
    }

    pub fn has_selected_edge(&self) -> bool {
        self.selected_edge.is_some()
    }

    fn require_layout_edit(&mut self, mixer: &mut MixerState) -> bool {
        if mixer.is_layout_locked() {
            self.set_message(mixer, LAYOUT_LOCKED_MESSAGE, false);
            mixer.notify_layout_edit_blocked();
            return false;
        }
        mixer.can_edit()
    }

    // ----- sockets -----------------------------------------------------------

    fn socket_world(&self, mixer: &MixerState, socket: &SocketRef) -> Option<Pos2> {
        let node = mixer.graph().nodes.iter().find(|n| n.id == socket.node)?;
        let layout = self.layouts.get(&socket.node)?;
        let list = if socket.output { &layout.outputs } else { &layout.inputs };
        let port = list.iter().find(|p| p.kind == socket.kind && p.port == socket.port)?;
        Some(node_view::socket_world(self.displayed_origin(node), layout, port))
    }

    fn is_current_socket(&self, mixer: &MixerState, socket: &SocketRef) -> bool {
        self.socket_world(mixer, socket).is_some()
    }

    fn sockets(&self, mixer: &MixerState) -> Vec<(SocketRef, Pos2, bool)> {
        let mut sockets = Vec::new();
        for node in &mixer.graph().nodes {
            let Some(layout) = self.layouts.get(&node.id) else { continue };
            let overview = self.runtimes.get(&node.id).is_some_and(|r| r.detail() == DetailLevel::Overview);
            let origin = self.displayed_origin(node);
            for port in layout.outputs.iter().chain(&layout.inputs) {
                sockets.push((
                    SocketRef { node: node.id.clone(), kind: port.kind, port: port.port, output: port.output },
                    node_view::socket_world(origin, layout, port),
                    !overview,
                ));
            }
        }
        sockets
    }

    fn compatible(first: &SocketRef, second: &SocketRef) -> bool {
        if first.output == second.output {
            return false;
        }
        let (from, to) = if first.output { (first, second) } else { (second, first) };
        if from.kind == "control" { to.kind == "control" } else { from.kind == "audio" && matches!(to.kind, "audio" | "sidechain") }
    }

    fn find_snap(&self, mixer: &MixerState, screen: Pos2, pending: Option<&SocketRef>) -> Option<SocketRef> {
        let cam = self.camera();
        let mut nearest = None;
        let mut closest = SOCKET_SNAP_RADIUS * SOCKET_SNAP_RADIUS;
        let graph = mixer.graph();
        for (socket, world, enabled) in self.sockets(mixer) {
            if !enabled || pending == Some(&socket) || pending.is_some_and(|p| !Self::compatible(p, &socket)) {
                continue;
            }
            let distance = cam.to_screen(world).distance_sq(screen);
            if distance > closest {
                continue;
            }
            if let Some(pending) = pending {
                let kind = if pending.output { socket.kind } else { pending.kind };
                if self.rewire.as_ref().is_some_and(|r| r.kind != kind) {
                    continue;
                }
                let candidate = GraphEdge {
                    from: if pending.output { pending.node.clone() } else { socket.node.clone() },
                    to: if pending.output { socket.node.clone() } else { pending.node.clone() },
                    kind: kind.into(),
                    from_port: if pending.output { pending.port } else { socket.port }.into(),
                    to_port: if pending.output { socket.port } else { pending.port }.into(),
                    ..GraphEdge::default()
                };
                let edges: Vec<GraphEdge> = match &self.rewire {
                    Some(original) => graph.edges.iter().filter(|e| e.id != original.id).cloned().collect(),
                    None => graph.edges.clone(),
                };
                let validation = AudioGraph { nodes: graph.nodes.clone(), edges, ..AudioGraph::default() };
                let original_endpoint = self.rewire.as_ref().is_some_and(|o| {
                    candidate.from == o.from && candidate.to == o.to && candidate.from_port == o.from_port && candidate.to_port == o.to_port
                });
                let error = if original_endpoint {
                    graph::connection_error(&validation.nodes, &validation.edges, &candidate)
                } else {
                    graph::new_connection_error(&validation, &candidate)
                };
                if !error.is_empty() {
                    continue;
                }
            }
            closest = distance;
            nearest = Some(socket);
        }
        nearest
    }

    fn cancel_cable(&mut self) {
        self.rewire = None;
        self.input_link = None;
        self.input_link_candidate = None;
        self.pending = None;
        self.snap = None;
        if matches!(self.gesture, Gesture::Socket { .. }) {
            self.gesture = Gesture::None;
        }
    }

    fn connect(&mut self, mixer: &mut MixerState, first: &SocketRef, second: &SocketRef) {
        if !Self::compatible(first, second) {
            self.set_message(mixer, "Connect an audio output to Audio or Trigger, or link matching control sockets.", true);
            return;
        }
        let (from, to) = if first.output { (first, second) } else { (second, first) };
        let error = self.connect_nodes(mixer, &from.node, &to.node, to.kind, from.port, to.port);
        if !error.is_empty() {
            self.set_message(mixer, &error, true);
            self.cancel_cable();
            return;
        }
        self.cancel_cable();
        let message = match to.kind {
            "control" => "Level control connected.",
            "sidechain" => "Ducking trigger connected.",
            _ => "Audio connected.",
        };
        self.set_message(mixer, message, false);
    }

    pub fn connect_nodes(&mut self, mixer: &mut MixerState, from: &str, to: &str, kind: &str, from_port: &str, to_port: &str) -> String {
        if mixer.is_layout_locked() {
            self.require_layout_edit(mixer);
            return LAYOUT_LOCKED_MESSAGE.into();
        }
        if !mixer.can_edit() {
            return "Wait for the audio engine before editing connections.".into();
        }
        let edge = GraphEdge {
            id: graph::new_id(),
            from: from.into(),
            to: to.into(),
            kind: kind.into(),
            from_port: from_port.into(),
            to_port: to_port.into(),
            ..GraphEdge::default()
        };
        let error = graph::new_connection_error(mixer.graph(), &edge);
        if !error.is_empty() {
            return error;
        }
        mixer.edit(move |w| {
            if graph::new_connection_error(&w.config.graph, &edge).is_empty() {
                w.config.graph.edges.push(edge.clone());
            }
        });
        String::new()
    }

    fn socket_pressed(&mut self, mixer: &mut MixerState, socket: SocketRef, pointer: Pos2) {
        if !mixer.can_edit() || !self.require_layout_edit(mixer) {
            return;
        }
        if let Some(pending) = self.pending.clone() {
            if pending != socket && !matches!(self.gesture, Gesture::Socket { dragging: true, .. }) {
                self.connect(mixer, &pending, &socket);
                return;
            }
        }
        if !socket.output {
            self.begin_input_link_drag(mixer, socket, pointer, None);
            return;
        }
        self.select_node(mixer, &socket.node);
        self.begin_connection_preview(socket, pointer, true);
        self.set_message(mixer, "Choose a compatible input socket. Escape cancels.", false);
    }

    fn begin_connection_preview(&mut self, socket: SocketRef, pointer: Pos2, dragging: bool) {
        self.cancel_cable();
        self.pending = Some(socket);
        self.pointer_world = self.camera().to_world(pointer);
        self.gesture = Gesture::Socket { start: pointer, dragging, moved: false };
    }

    fn begin_input_link_drag(&mut self, mixer: &mut MixerState, input: SocketRef, pointer: Pos2, edge_id: Option<&str>) -> bool {
        if input.output || !self.is_current_socket(mixer, &input) || !self.require_layout_edit(mixer) {
            return false;
        }
        let incoming: Vec<GraphEdge> =
            mixer.graph().edges.iter().filter(|e| e.to == input.node && e.kind == input.kind && e.to_port == input.port).cloned().collect();
        let selected = edge_id.map(str::to_owned).or_else(|| self.selected_edge.clone());
        let mut edge = incoming.iter().find(|e| Some(&e.id) == selected.as_ref()).cloned();
        self.cancel_cable();
        if incoming.is_empty() {
            self.select_node(mixer, &input.node);
            self.begin_connection_preview(input, pointer, true);
            self.set_message(mixer, "Choose a compatible output socket. Escape cancels.", false);
            return true;
        }
        if edge.is_none() && incoming.len() == 1 {
            edge = Some(incoming[0].clone());
        }
        self.pointer_world = self.camera().to_world(pointer);
        self.gesture = Gesture::Socket { start: pointer, dragging: true, moved: false };
        if let Some(edge) = edge {
            return self.begin_existing_link(mixer, &edge, &input);
        }
        self.select_node(mixer, &input.node);
        self.input_link = Some((input.clone(), incoming));
        self.pending = Some(input.clone());
        self.snap = Some(input);
        self.set_message(mixer, "Follow the cable you want away from this input, or select that cable first. Escape cancels.", false);
        true
    }

    fn begin_existing_link(&mut self, mixer: &mut MixerState, edge: &GraphEdge, input: &SocketRef) -> bool {
        let source_kind: &'static str = if edge.kind == "sidechain" || edge.kind == "audio" { "audio" } else { "control" };
        let Some(source) = mixer.graph().nodes.iter().find(|n| n.id == edge.from) else {
            self.cancel_cable();
            return false;
        };
        let (outputs, _) = node_view::ports(source, mixer.graph());
        let Some(port) = outputs.iter().find(|p| p.kind == source_kind && p.port == edge.from_port) else {
            self.cancel_cable();
            return false;
        };
        let output = SocketRef { node: edge.from.clone(), kind: port.kind, port: port.port, output: true };
        self.rewire = Some(edge.clone());
        self.pending = Some(output);
        self.input_link = None;
        self.input_link_candidate = None;
        self.selected_edge = Some(edge.id.clone());
        self.selected.clear();
        self.snap = Some(input.clone());
        self.set_message(mixer, "Move this cable to another input, or release on empty canvas to remove it. Escape cancels.", false);
        true
    }

    fn choose_incoming_link(&mut self, mixer: &mut MixerState, pointer: Pos2) {
        let Some((input, candidates)) = self.input_link.clone() else { return };
        let world = to_point(self.camera().to_world(pointer));
        let mut distances: Vec<(GraphEdge, f64)> = candidates
            .iter()
            .filter_map(|e| self.cable_points(mixer, e).map(|points| (e.clone(), editing::distance_to_polyline(&points, world) * f64::from(self.zoom))))
            .collect();
        distances.sort_by(|a, b| a.1.partial_cmp(&b.1).unwrap_or(std::cmp::Ordering::Equal));
        let unique = distances.first().is_some_and(|(_, d)| *d <= 12.0) && (distances.len() == 1 || distances[1].1 - distances[0].1 >= 1.5);
        self.input_link_candidate = if unique { Some(distances[0].0.id.clone()) } else { None };
        if !unique {
            self.set_message(mixer, "Follow one incoming cable. If they overlap, select the cable first, then drag this input.", false);
            return;
        }
        let Some(anchor) = self.socket_world(mixer, &input) else { return };
        if self.camera().to_screen(anchor).distance(pointer) < 12.0 {
            return;
        }
        let edge = distances[0].0.clone();
        if self.begin_existing_link(mixer, &edge, &input) {
            let pending = self.pending.clone();
            self.snap = self.find_snap(mixer, pointer, pending.as_ref());
        }
    }

    fn finish_connection(&mut self, mixer: &mut MixerState, pointer: Pos2, blank: bool) {
        if mixer.is_layout_locked() {
            self.cancel_cable();
            return;
        }
        let Gesture::Socket { dragging, moved, .. } = self.gesture.clone() else { return };
        if !dragging {
            return;
        }
        self.gesture = Gesture::None;
        let Some(pending) = self.pending.clone() else { return };
        if self.input_link.is_some() {
            self.cancel_cable();
            self.set_message(mixer, "No cable moved. Select the cable you want, then drag its input end.", false);
            return;
        }
        if let Some(original) = self.rewire.clone() {
            if !moved {
                self.cancel_cable();
                self.set_message(mixer, "Cable selected. Drag its input end to move or remove it.", false);
                return;
            }
            let target = self.find_snap(mixer, pointer, Some(&pending));
            let plan = match &target {
                Some(t) if t.node != original.to || t.port != original.to_port => editing::prepare_rewire(mixer.graph(), &original, &t.node, t.port).map(Some),
                Some(_) => Ok(None),
                None if blank => editing::prepare_removal(mixer.graph(), std::slice::from_ref(&original)).map(Some),
                None => Ok(None),
            };
            self.cancel_cable();
            match plan {
                Ok(Some(plan)) => {
                    self.set_message(mixer, if target.is_none() { "Cable removed." } else { "Cable moved." }, false);
                    let _ = mixer.try_edit(move |w| editing::apply_edge_plan(&mut w.config.graph, &plan));
                }
                Ok(None) => self.set_message(mixer, "Original cable kept.", false),
                Err(error) => self.set_message(mixer, &error, true),
            }
            return;
        }
        if moved {
            let target = self.find_snap(mixer, pointer, Some(&pending));
            match target {
                Some(target) if target != pending => self.connect(mixer, &pending, &target),
                _ => {
                    self.cancel_cable();
                    self.set_message(mixer, "Connection cancelled. Drag between matching sockets, or click them one at a time.", false);
                }
            }
        } else {
            // A click keeps the socket pending for click-to-connect.
            self.gesture = Gesture::None;
        }
    }

    // ----- node moves ----------------------------------------------------------

    fn begin_node_pointer(&mut self, mixer: &mut MixerState, id: &str, pointer: Pos2, modifiers: Modifiers) {
        if !mixer.can_edit() || !self.layouts.contains_key(id) {
            return;
        }
        self.select_node_for_pointer(mixer, id, modifiers);
        if !self.selected.iter().any(|x| x == id) {
            return;
        }
        if mixer.is_layout_locked() {
            self.gesture = Gesture::LockedLayout { start: pointer, notified: false };
        } else {
            self.begin_node_move(mixer, id, pointer, modifiers.alt, true, false);
        }
    }

    fn begin_node_move(&mut self, mixer: &mut MixerState, id: &str, pointer: Pos2, detach: bool, raise_anchor: bool, frame: bool) {
        self.finish_nudge(mixer, false);
        if !self.require_layout_edit(mixer) {
            return;
        }
        if !self.selected.iter().any(|x| x == id) {
            self.select_node(mixer, id);
        }
        let selected = self.selected.clone();
        self.bring_to_front(&selected, raise_anchor.then_some(id));
        self.cancel_cable();
        self.insertion = None;
        if detach {
            let text = if selected.len() == 1 {
                "Release to detach this node and keep its audio path connected. Escape cancels."
            } else {
                "Move the group normally. Detach works on one node at a time."
            };
            self.set_message(mixer, text, false);
        }
        let origins: Vec<(String, Pos2)> = mixer
            .graph()
            .nodes
            .iter()
            .filter(|n| selected.contains(&n.id))
            .map(|n| (n.id.clone(), Pos2::new(n.x as f32, n.y as f32)))
            .collect();
        self.gesture = Gesture::NodeMove { anchor: id.into(), start: pointer, origins, moved: false, detach, frame, delta: Vec2::ZERO };
    }

    fn update_node_move(&mut self, mixer: &MixerState, pointer: Pos2) {
        let zoom = self.zoom;
        let Gesture::NodeMove { start, origins, moved, delta, .. } = &mut self.gesture else { return };
        let pixels = pointer - *start;
        if pixels.length() > 3.0 {
            *moved = true;
        }
        if !*moved {
            return;
        }
        *delta = clamp_group_delta(origins, pixels / zoom);
        let single = origins.len() == 1;
        let (anchor, detach, frame) = match &self.gesture {
            Gesture::NodeMove { anchor, detach, frame, .. } => (anchor.clone(), *detach, *frame),
            _ => return,
        };
        let candidate = if !frame && !detach && single { self.insertion_candidate(mixer, &anchor) } else { None };
        if candidate != self.insertion {
            if candidate.is_some() {
                self.set_message(mixer, "Release to insert this node into the highlighted cable. Escape cancels.", false);
            } else if self.insertion.is_some() {
                self.dismiss_message();
            }
            self.insertion = candidate;
        }
    }

    fn finish_node_move(&mut self, mixer: &mut MixerState, cancel: bool) {
        let Gesture::NodeMove { anchor, origins, moved, detach, delta, .. } = std::mem::replace(&mut self.gesture, Gesture::None) else { return };
        let cancel = cancel || !mixer.can_edit_layout();
        // Commit exactly the cable shown while dragging, even if grid
        // rounding moves the node nearer another cable.
        let insertion = self.insertion.take();
        if cancel || !moved || !mixer.can_edit() {
            return;
        }
        let delta = clamp_group_delta(&origins, Vec2::new((delta.x / 8.0).round() * 8.0, (delta.y / 8.0).round() * 8.0));
        if delta == Vec2::ZERO {
            return;
        }
        let positions: HashMap<String, (f64, f64)> =
            origins.iter().map(|(id, o)| (id.clone(), (f64::from(o.x + delta.x), f64::from(o.y + delta.y)))).collect();
        let mut plan: Option<editing::NodePlan> = None;
        if detach && positions.len() == 1 {
            match editing::prepare_detach(mixer.graph(), &anchor) {
                Ok(p) => {
                    plan = Some(p);
                    self.set_message(mixer, "Node detached; its audio path stays connected.", false);
                }
                Err(error) => self.set_message(mixer, &error, false),
            }
        } else if !detach && positions.len() == 1 {
            if let Some(edge) = insertion {
                match editing::prepare_insertion(mixer.graph(), &anchor, &edge) {
                    Ok(p) => {
                        plan = Some(p);
                        self.set_message(mixer, "Node inserted into the audio cable.", false);
                    }
                    Err(error) => self.set_message(mixer, &error, false),
                }
            }
        }
        mixer.edit(move |w| {
            for node in &mut w.config.graph.nodes {
                if let Some(&(x, y)) = positions.get(&node.id) {
                    node.x = x;
                    node.y = y;
                }
            }
            if let Some(plan) = &plan {
                let _ = editing::apply_node_plan(&mut w.config.graph, plan);
            }
        });
    }

    /// The insertion target for a single dragged node (`FindInsertionCandidate`).
    fn insertion_candidate(&self, mixer: &MixerState, node_id: &str) -> Option<String> {
        let graph = mixer.graph();
        let node = graph.nodes.iter().find(|n| n.id == node_id)?;
        if !editing::is_insertable(node) || graph.edges.iter().any(|e| e.from == node_id || e.to == node_id) {
            return None;
        }
        let bounds = self.node_bounds_at(node)?;
        let center = bounds.center();
        let bounds = bounds.expand(8.0 / self.zoom);
        let mut nearest = None;
        let mut best = f32::INFINITY;
        for edge in graph.edges.iter().filter(|e| e.kind == "audio") {
            let Some((from, to)) = self.cable_ends(mixer, edge) else { continue };
            let (a, b) = editing::cable_controls(to_point(from), to_point(to));
            let (a, b) = (from_point(a), from_point(b));
            let length = from.distance(a) + a.distance(b) + b.distance(to);
            let steps = ((length * self.zoom / 12.0).ceil() as usize).clamp(16, 256);
            let mut previous = from;
            let mut distance = f32::INFINITY;
            for i in 1..=steps {
                let t = i as f32 / steps as f32;
                let u = 1.0 - t;
                let point = Pos2::new(
                    u * u * u * from.x + 3.0 * u * u * t * a.x + 3.0 * u * t * t * b.x + t * t * t * to.x,
                    u * u * u * from.y + 3.0 * u * u * t * a.y + 3.0 * u * t * t * b.y + t * t * t * to.y,
                );
                if segment_intersects(bounds, previous, point) {
                    let v = point - previous;
                    let fraction = if v.length_sq() > 0.0 { ((center - previous).dot(v) / v.length_sq()).clamp(0.0, 1.0) } else { 0.0 };
                    distance = distance.min((center - (previous + v * fraction)).length_sq());
                }
                previous = point;
            }
            if distance < best && editing::insertion_error(graph, node_id, &edge.id).is_empty() {
                nearest = Some(edge.id.clone());
                best = distance;
            }
        }
        nearest
    }

    // ----- cables --------------------------------------------------------------------

    fn cable_ends(&self, mixer: &MixerState, edge: &GraphEdge) -> Option<(Pos2, Pos2)> {
        let source_kind: &'static str = if edge.kind == "control" { "control" } else { "audio" };
        let target_kind: &'static str = match edge.kind.as_str() {
            "control" => "control",
            "sidechain" => "sidechain",
            _ => "audio",
        };
        let from_port = port_static(&edge.from_port);
        let to_port = port_static(&edge.to_port);
        let from = self.socket_world(mixer, &SocketRef { node: edge.from.clone(), kind: source_kind, port: from_port, output: true })?;
        let to = self.socket_world(mixer, &SocketRef { node: edge.to.clone(), kind: target_kind, port: to_port, output: false })?;
        Some((from, to))
    }

    fn cable_points(&self, mixer: &MixerState, edge: &GraphEdge) -> Option<Vec<editing::Point>> {
        self.cable_ends(mixer, edge).map(|(from, to)| editing::cable_points(to_point(from), to_point(to)))
    }

    fn edge_at(&self, mixer: &MixerState, screen: Pos2) -> Option<String> {
        let world = to_point(self.camera().to_world(screen));
        let mut best: Option<(String, f64)> = None;
        for edge in &mixer.graph().edges {
            if self.rewire.as_ref().is_some_and(|r| r.id == edge.id) {
                continue;
            }
            let Some(points) = self.cable_points(mixer, edge) else { continue };
            let distance = editing::distance_to_polyline(&points, world) * f64::from(self.zoom);
            if distance <= 8.0 && best.as_ref().is_none_or(|(_, d)| distance < *d) {
                best = Some((edge.id.clone(), distance));
            }
        }
        best.map(|(id, _)| id)
    }

    fn wire_pressed(&mut self, mixer: &mut MixerState, id: &str, pointer: Pos2) {
        self.select_edge(mixer, id);
        if !mixer.can_edit() {
            return;
        }
        let Some(edge) = mixer.graph().edges.iter().find(|e| e.id == id).cloned() else { return };
        let kind: &'static str = match edge.kind.as_str() {
            "control" => "control",
            "sidechain" => "sidechain",
            _ => "audio",
        };
        let input = SocketRef { node: edge.to.clone(), kind, port: port_static(&edge.to_port), output: false };
        let Some(endpoint) = self.socket_world(mixer, &input) else { return };
        if mixer.is_layout_locked() {
            self.gesture = Gesture::LockedLayout { start: pointer, notified: false };
            return;
        }
        if self.camera().to_screen(endpoint).distance(pointer) > 64.0 {
            return;
        }
        self.begin_input_link_drag(mixer, input, pointer, Some(id));
    }

    // ----- cut ---------------------------------------------------------------------

    fn begin_cut(&mut self, mixer: &mut MixerState, pointer: Pos2) {
        if !self.require_layout_edit(mixer) {
            return;
        }
        self.cancel_active_gesture(mixer);
        self.gesture = Gesture::Cut { points: vec![pointer], candidates: mixer.graph().edges.clone(), cut: Vec::new() };
    }

    fn update_cut(&mut self, mixer: &MixerState, pointer: Pos2) {
        let cam = self.camera();
        let zoom = self.zoom;
        let mut newly_cut = Vec::new();
        if let Gesture::Cut { points, candidates, cut } = &self.gesture {
            let last = *points.last().unwrap_or(&pointer);
            if last.distance(pointer) < 2.0 {
                return;
            }
            for edge in candidates {
                if cut.contains(&edge.id) {
                    continue;
                }
                if let Some(line) = self.cable_points(mixer, edge) {
                    if editing::polyline_intersects_stroke(&line, to_point(cam.to_world(last)), to_point(cam.to_world(pointer)), f64::from(3.0 / zoom)) {
                        newly_cut.push(edge.id.clone());
                    }
                }
            }
        }
        if let Gesture::Cut { points, cut, .. } = &mut self.gesture {
            if points.last().is_none_or(|l| l.distance(pointer) >= 2.0) {
                points.push(pointer);
            }
            cut.extend(newly_cut);
        }
    }

    fn finish_cut(&mut self, mixer: &mut MixerState, cancel: bool) {
        let Gesture::Cut { candidates, cut, .. } = std::mem::replace(&mut self.gesture, Gesture::None) else { return };
        let originals: Vec<GraphEdge> = candidates.into_iter().filter(|e| cut.contains(&e.id)).collect();
        if cancel || originals.is_empty() || !mixer.can_edit_layout() {
            return;
        }
        match editing::prepare_removal(mixer.graph(), &originals) {
            Ok(plan) => {
                let _ = mixer.try_edit(move |w| editing::apply_edge_plan(&mut w.config.graph, &plan));
                let count = originals.len();
                self.set_message(mixer, &format!("Removed {count} connection{}.", if count == 1 { "" } else { "s" }), false);
            }
            Err(error) => self.set_message(mixer, &error, true),
        }
    }

    // ----- gesture lifecycle ----------------------------------------------------------

    pub fn cancel_active_gesture(&mut self, mixer: &mut MixerState) {
        self.finish_nudge(mixer, true);
        self.pipeline_choices.clear();
        if let Gesture::Minimap { original, .. } = self.gesture {
            self.pan = original;
        }
        if matches!(self.gesture, Gesture::Cut { .. }) {
            self.finish_cut(mixer, true);
        }
        self.cancel_cable();
        self.finish_gesture(mixer, None, true);
    }

    fn finish_gesture(&mut self, mixer: &mut MixerState, pointer: Option<Pos2>, cancel: bool) {
        match std::mem::replace(&mut self.gesture, Gesture::None) {
            gesture @ Gesture::NodeMove { .. } => {
                self.gesture = gesture;
                if let Some(pointer) = pointer {
                    self.update_node_move(mixer, pointer);
                }
                self.finish_node_move(mixer, cancel);
            }
            Gesture::Box { original, .. } => {
                if cancel {
                    self.set_selection(mixer, &original.into_iter().collect::<Vec<_>>(), false, false);
                    if let Some(before) = self.box_stacking.take() {
                        self.stacking = before;
                    }
                }
                self.box_stacking = None;
            }
            Gesture::Pan { original, panning, .. } => {
                if panning {
                    if cancel {
                        self.pan = original;
                    } else {
                        self.persist_view(mixer);
                    }
                }
            }
            Gesture::Minimap { original, .. } => {
                if cancel {
                    self.pan = original;
                } else {
                    self.persist_view(mixer);
                }
            }
            other => {
                if !matches!(other, Gesture::LockedLayout { .. }) {
                    self.gesture = other;
                    if cancel {
                        self.gesture = Gesture::None;
                    }
                }
            }
        }
        self.insertion = None;
    }

    // ----- keyboard nudge -------------------------------------------------------

    fn finish_nudge(&mut self, _mixer: &MixerState, cancel: bool) {
        let Some(nudge) = self.nudge.take() else { return };
        if cancel {
            self.cancelled_nudge_keys.insert(nudge.key);
        }
        self.pending_nudge_commit = (!cancel && nudge.delta != Vec2::ZERO).then_some(nudge);
    }

    fn commit_nudge(&mut self, mixer: &mut MixerState) {
        let Some(nudge) = self.pending_nudge_commit.take() else { return };
        if !mixer.can_edit_layout() {
            return;
        }
        let origins: Vec<(String, (f64, f64))> = nudge.origins.iter().map(|(id, p)| (id.clone(), (f64::from(p.x), f64::from(p.y)))).collect();
        let delta = (f64::from(nudge.delta.x), f64::from(nudge.delta.y));
        let result = mixer.try_edit(move |w| {
            let nodes: Vec<Option<&GraphNode>> = origins.iter().map(|(id, _)| w.node(id)).collect();
            if nodes.iter().any(Option::is_none) {
                return Err("A selected node was removed during the move.".into());
            }
            let all_moved = origins.iter().all(|(id, o)| w.node(id).is_some_and(|n| n.x == o.0 + delta.0 && n.y == o.1 + delta.1));
            if all_moved {
                return Ok(());
            }
            if origins.iter().any(|(id, o)| w.node(id).is_some_and(|n| n.x != o.0 || n.y != o.1)) {
                return Err("A selected node moved elsewhere. The latest position was kept.".into());
            }
            for (id, o) in &origins {
                if let Some(node) = w.node_mut(id) {
                    node.x = o.0 + delta.0;
                    node.y = o.1 + delta.1;
                }
            }
            Ok(())
        });
        if let Err(error) = result {
            self.set_message(mixer, &error, true);
        }
    }
}

/// Mutable canvas state that outlives one frame but isn't part of Default
/// construction above (kept separate for readability).
impl Canvas {
    #[allow(clippy::too_many_lines)]
    fn canvas_arrow(&mut self, ctx: &egui::Context, mixer: &mut MixerState, key: Key, shift: bool, repeat: bool) -> bool {
        if !repeat {
            self.cancelled_nudge_keys.remove(&key);
        }
        if self.cancelled_nudge_keys.contains(&key) {
            return true;
        }
        if self.nudge.as_ref().is_some_and(|n| n.key != key) {
            self.finish_nudge(mixer, false);
            self.commit_nudge(mixer);
        }
        if self.nudge.is_none() && !self.can_frame_selection(mixer) {
            return false;
        }
        let direction = match key {
            Key::ArrowLeft => Vec2::new(-1.0, 0.0),
            Key::ArrowRight => Vec2::new(1.0, 0.0),
            Key::ArrowUp => Vec2::new(0.0, -1.0),
            _ => Vec2::new(0.0, 1.0),
        };
        let selected: Vec<GraphNode> = mixer.graph().nodes.iter().filter(|n| self.selected.contains(&n.id)).cloned().collect();
        if selected.is_empty() {
            self.pan = Vec2::new(
                (self.pan.x - direction.x * 40.0).clamp(-policy::MAXIMUM_PAN as f32, policy::MAXIMUM_PAN as f32),
                (self.pan.y - direction.y * 40.0).clamp(-policy::MAXIMUM_PAN as f32, policy::MAXIMUM_PAN as f32),
            );
            self.apply_view(ctx);
            self.persist_view(mixer);
            return true;
        }
        if !self.require_layout_edit(mixer) {
            return true;
        }
        if self.nudge.is_none() {
            self.nudge = Some(Nudge {
                key,
                origins: selected.iter().map(|n| (n.id.clone(), Pos2::new(n.x as f32, n.y as f32))).collect(),
                delta: Vec2::ZERO,
            });
        }
        if let Some(nudge) = self.nudge.as_mut() {
            let delta = nudge.delta + direction * if shift { 1.0 } else { 10.0 };
            nudge.delta = clamp_group_delta(&nudge.origins, delta);
        }
        true
    }
}

fn clamp_group_delta(origins: &[(String, Pos2)], delta: Vec2) -> Vec2 {
    if origins.is_empty() {
        return Vec2::ZERO;
    }
    let min_x = origins.iter().map(|(_, p)| p.x).fold(f32::INFINITY, f32::min);
    let max_x = origins.iter().map(|(_, p)| p.x).fold(f32::NEG_INFINITY, f32::max);
    let min_y = origins.iter().map(|(_, p)| p.y).fold(f32::INFINITY, f32::min);
    let max_y = origins.iter().map(|(_, p)| p.y).fold(f32::NEG_INFINITY, f32::max);
    Vec2::new(delta.x.clamp(-100_000.0 - min_x, 100_000.0 - max_x), delta.y.clamp(-100_000.0 - min_y, 100_000.0 - max_y))
}

fn segment_intersects(bounds: Rect, from: Pos2, to: Pos2) -> bool {
    let mut low = 0.0f32;
    let mut high = 1.0f32;
    let mut clip = |origin: f32, delta: f32, min: f32, max: f32| -> bool {
        if delta.abs() < 1e-7 {
            return origin >= min && origin <= max;
        }
        let (mut start, mut end) = ((min - origin) / delta, (max - origin) / delta);
        if start > end {
            std::mem::swap(&mut start, &mut end);
        }
        low = low.max(start);
        high = high.min(end);
        low <= high
    };
    clip(from.x, to.x - from.x, bounds.left(), bounds.right()) && clip(from.y, to.y - from.y, bounds.top(), bounds.bottom())
}

pub fn port_static(port: &str) -> &'static str {
    match port {
        "left" => "left",
        "right" => "right",
        "a" => "a",
        "b" => "b",
        "wet" => "wet",
        _ => "audio",
    }
}

/// Hover help for a cable: its route, and a second line only when the cable
/// is something other than an ordinary, enabled audio connection. (Saved
/// level, detector state and the explanation of each connection type are in
/// [`describe_connection`], Help and the cable's menu.)
pub fn connection_help(graph: &AudioGraph, edge: &GraphEdge) -> String {
    let (_, route, _, _) = describe_connection(graph, edge);
    let kind = match edge.kind.as_str() {
        "control" => "Linked control",
        "sidechain" => "Trigger",
        _ => "",
    };
    let state = if edge.muted { "Off" } else { "" };
    let detail = [kind, state].into_iter().filter(|part| !part.is_empty()).collect::<Vec<_>>().join(" · ");
    if detail.is_empty() { route } else { format!("{route}\n{detail}") }
}

/// Title, route, state and purpose of a cable (`DescribeConnection`).
pub fn describe_connection(graph: &AudioGraph, edge: &GraphEdge) -> (String, String, String, String) {
    let source = graph.nodes.iter().find(|n| n.id == edge.from);
    let destination = graph.nodes.iter().find(|n| n.id == edge.to);
    let mut from = source.map_or_else(|| "Missing source".to_owned(), |n| n.name.clone());
    let mut to = destination.map_or_else(|| "Missing destination".to_owned(), |n| n.name.clone());
    if edge.from_port != "audio" {
        from += &format!(" · {}", graph::port_label(&edge.from_port, source.map(|n| n.kind.as_str())));
    }
    if edge.to_port != "audio" {
        to += &format!(" · {}", graph::port_label(&edge.to_port, destination.map(|n| n.kind.as_str())));
    }
    let gain = format!("{}{} dB", if edge.gain_db > 0.0 { "+" } else { "" }, edge.gain_db);
    let route = format!("{from} → {to}");
    match edge.kind.as_str() {
        "control" => (
            "Linked control connection".into(),
            route,
            format!("Linked control {} · Saved offset {gain}", if edge.muted { "off" } else { "on" }),
            "Scales the linked level; carries no audio".into(),
        ),
        "sidechain" => (
            "Sidechain / trigger connection".into(),
            route,
            format!("Detector {} · Saved level {gain}", if edge.muted { "off" } else { "on" }),
            "Detector only; adds no audible audio".into(),
        ),
        _ => ("Audio connection".into(), route, format!("Connection {} · Saved level {gain}", if edge.muted { "off" } else { "on" }), String::new()),
    }
}
