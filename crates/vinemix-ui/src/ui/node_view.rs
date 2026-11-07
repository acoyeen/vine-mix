//! One node on the canvas (`GraphNodeView`): ports, world-space layout,
//! status and availability presentation, meters, controls and sockets.
//!
//! Layout follows the 0.5 WPF grid exactly so sockets, cables and saved
//! positions line up: a 32-unit header, output port rows, a content area
//! (meter column and control stack), input port rows and the Bypass bar.

use crate::ui::widgets::Tip as _;
use std::collections::HashMap;

use eframe::egui::{self, Align2, Color32, Id, Pos2, Rect, Sense, Stroke, Ui, Vec2};
use vinemix_model::config::{AudioGraph, GraphNode};
use vinemix_model::status::{ApplicationInfo, Endpoint, NodeStatus};

use super::controls::{self, Cell, ChoiceKey, Dial, Item};
use super::dial::{self, DialState, Variant};
use super::meter::{self, Meter};
use super::theme::{self, Weight};
use super::widgets::{self, ButtonStyle, Choice};
use crate::availability::{self, Availability, NoticeKind};
use crate::graph::{self, canvas::DetailLevel};

pub const HEADER: f32 = 32.0;
pub const ROW: f32 = 28.0;

/// A socket on a node edge.
#[derive(Clone, Debug, PartialEq)]
pub struct Port {
    /// `audio`, `sidechain` or `control`.
    pub kind: &'static str,
    pub port: &'static str,
    pub label: String,
    pub output: bool,
    /// Centre, relative to the node origin (world units).
    pub y: f32,
}

#[derive(Clone, Debug)]
pub struct Layout {
    pub width: f32,
    pub height: f32,
    pub outputs: Vec<Port>,
    pub inputs: Vec<Port>,
    pub outputs_height: f32,
    pub content_top: f32,
    pub content_height: f32,
    pub inputs_top: f32,
    pub items: Vec<(Item, Rect)>,
    pub body: Rect,
    pub meter: Option<Rect>,
    pub bypass: Option<Rect>,
}

pub fn ports(node: &GraphNode, graph: &AudioGraph) -> (Vec<Port>, Vec<Port>) {
    let kind = node.kind.as_str();
    let mut outputs = Vec::new();
    let output = |port: &'static str, kind_: &'static str, label: String| Port { kind: kind_, port, label, output: true, y: 0.0 };
    match kind {
        "stereoSplit" => {
            outputs.push(output("left", "audio", "Left · mono".into()));
            outputs.push(output("right", "audio", "Right · mono".into()));
        }
        "reverb" | "echo" => {
            outputs.push(output("audio", "audio", "Audio".into()));
            outputs.push(output("wet", "audio", graph::port_label("wet", Some(kind)).into()));
        }
        _ if graph::has_audio_output(kind) || kind == "control" => {
            let label = if kind == "control" {
                "Control"
            } else if matches!(kind, "stereoCombine" | "crossfade") {
                "Stereo"
            } else {
                "Audio"
            };
            outputs.push(output("audio", if kind == "control" { "control" } else { "audio" }, label.into()));
        }
        _ => {}
    }
    let mut inputs = Vec::new();
    let input = |port: &'static str, kind_: &'static str, label: &str| Port { kind: kind_, port, label: label.into(), output: false, y: 0.0 };
    match kind {
        "stereoCombine" => {
            inputs.push(input("left", "audio", "Left"));
            inputs.push(input("right", "audio", "Right"));
        }
        "crossfade" => {
            inputs.push(input("a", "audio", "A · stereo"));
            inputs.push(input("b", "audio", "B · stereo"));
        }
        _ if graph::has_audio_input(kind) => inputs.push(input("audio", "audio", if kind == "stereoSplit" { "Stereo" } else { "Audio" })),
        _ => {}
    }
    if graph::has_sidechain_input(kind) {
        inputs.push(input("audio", "sidechain", "Trigger"));
    }
    if graph::linked::show_input(graph, node) {
        inputs.push(input("audio", "control", "Level control"));
    }
    (outputs, inputs)
}

fn line(size: f32) -> f32 {
    size * 1.3
}

fn group_row_height(cells: &[Cell]) -> f32 {
    if cells.iter().any(Cell::primary) { 86.0 } else { 58.0 }
}

fn item_height(ctx: &egui::Context, item: &Item, width: f32) -> f32 {
    match item {
        Item::Status => 32.0,
        Item::VirtualDevice | Item::Device | Item::App | Item::Mute => 30.0,
        Item::Mono => 36.0,
        Item::Choice(_) => 35.0,
        Item::Caption(_) => line(12.0) + 4.0,
        Item::Note(text) => widgets::wrapped_height(ctx, text, theme::font(11.0, Weight::Regular), width) + 9.0,
        Item::Help(text) => widgets::wrapped_height(ctx, text, theme::font(12.0, Weight::Regular), width) + 10.0,
        Item::DenoiserDelay => line(12.0) + 5.0,
        Item::Divider => 15.0,
        Item::Dial(_, margin) => 91.0 + margin,
        Item::Tone(dials) => 13.0 + 95.0 * dials.len() as f32,
        Item::Multiplier(_) => 141.0,
        Item::LegacyMultiplier => line(16.0) + 24.0 + 26.0,
        Item::Group { columns, cells } => {
            let rows = cells.len().div_ceil((*columns).max(1));
            9.0 + rows as f32 * (group_row_height(cells) + 53.0)
        }
        Item::Alternatives(alternatives, _) => alternatives
            .iter()
            .map(|items| items.iter().map(|i| item_height(ctx, i, width)).sum::<f32>())
            .fold(0.0, f32::max),
        Item::Eq => 307.0 + 141.0,
        Item::VoicePad => 394.0,
    }
}

pub fn layout(ctx: &egui::Context, node: &GraphNode, graph: &AudioGraph) -> Layout {
    let kind = node.kind.as_str();
    let width = controls::node_width(kind);
    let (mut outputs, mut inputs) = ports(node, graph);
    let outputs_height = if matches!(kind, "stereoSplit" | "reverb" | "echo") {
        56.0
    } else if graph::has_audio_output(kind) || kind == "control" {
        ROW
    } else {
        0.0
    };
    for (index, port) in outputs.iter_mut().enumerate() {
        port.y = 1.0 + HEADER + 14.0 + ROW * index as f32;
    }
    let meter_column = if kind == "control" { 0.0 } else { 36.0 };
    let body_left = 1.0 + 12.0 + meter_column;
    let body_width = width - 2.0 - 24.0 - meter_column;
    let content_top = 1.0 + HEADER + outputs_height;
    let mut y = content_top + 7.0;
    let mut items = Vec::new();
    for item in controls::items(node) {
        let height = item_height(ctx, &item, body_width);
        items.push((item, Rect::from_min_size(Pos2::new(body_left, y), Vec2::new(body_width, height))));
        y += height;
    }
    let body_height = y - (content_top + 7.0);
    let output_device = matches!(kind, "output" | "virtualOutput");
    let meter_min = if kind == "control" {
        0.0
    } else if output_device {
        118.0
    } else {
        90.0
    };
    let content_height = body_height.max(meter_min) + 17.0;
    let meter = (kind != "control").then(|| {
        let top = content_top + 7.0 + if output_device { 28.0 } else { 0.0 };
        Rect::from_min_max(Pos2::new(13.0, top), Pos2::new(13.0 + 28.0, content_top + content_height - 10.0))
    });
    let inputs_top = content_top + content_height;
    for (index, port) in inputs.iter_mut().enumerate() {
        port.y = inputs_top + 3.0 + 14.0 + ROW * index as f32;
    }
    let inputs_height = 10.0 + ROW * inputs.len() as f32;
    let bypass_top = inputs_top + inputs_height;
    let bypass = controls::has_bypass(kind).then(|| Rect::from_min_size(Pos2::new(13.0, bypass_top), Vec2::new(width - 26.0, 36.0)));
    let height = bypass_top + if bypass.is_some() { 48.0 } else { 0.0 } + 1.0;
    Layout {
        width,
        height,
        outputs,
        inputs,
        outputs_height,
        content_top,
        content_height,
        inputs_top,
        items,
        body: Rect::from_min_size(Pos2::new(body_left, content_top + 7.0), Vec2::new(body_width, body_height)),
        meter,
        bypass,
    }
}

/// Live dynamics feedback (`ApplyDynamicsFeedbackAt`).
#[derive(Clone, Debug, Default)]
pub struct Dynamics {
    frame: u64,
    last_time: f64,
    reduction: f32,
    detector: f32,
    valid: bool,
    pub visible_reduction: f64,
}

/// Meter peak observation across reconfiguration (`ObservePeakSample`).
#[derive(Clone, Debug, Default)]
pub struct PeakObservation {
    sequence: u64,
    frame_known: bool,
    last_audio_frame: u64,
    source: Option<(String, String, i32, bool, u32, String)>,
    endpoint: String,
    skip: bool,
    await_advance: bool,
    boundary: u64,
}

/// Per-node interaction and animation state kept between frames.
#[derive(Debug, Default)]
pub struct Runtime {
    pub meter: Meter,
    pub dials: HashMap<String, DialState>,
    pub detail: Option<DetailLevel>,
    pub rename: Option<String>,
    pub rename_focus: bool,
    pub dynamics: Dynamics,
    pub peaks: PeakObservation,
    pub eq: super::eq::EqState,
    pub voice: super::voice::VoiceState,
    pub editing: bool,
}

impl Runtime {
    pub fn detail(&self) -> DetailLevel {
        self.detail.unwrap_or(DetailLevel::Full)
    }
}

/// Everything a node needs from its surroundings.
#[derive(Clone, Copy)]
pub struct Env<'a> {
    pub graph: &'a AudioGraph,
    pub endpoints: &'a [Endpoint],
    pub applications: &'a [ApplicationInfo],
    pub visibility: availability::Visibility,
    pub can_edit: bool,
    pub preview: bool,
    pub layout_locked: bool,
    pub reduced_animation: bool,
    pub now: f64,
    pub meter_sequence: u64,
    pub audio_frames: u64,
    pub acknowledged: Option<&'a GraphNode>,
}

impl std::fmt::Debug for Env<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Env").field("can_edit", &self.can_edit).finish_non_exhaustive()
    }
}

/// Requests a node makes of the workspace.
pub enum Action {
    Edit(String, Box<dyn Fn(&mut GraphNode) + Send>),
    BeginGesture,
    EndGesture,
    Message(String, bool),
    /// Pointer pressed on a drag surface (header or panel).
    Press { modifiers: egui::Modifiers, pointer: Pos2 },
    Rename(String),
    ContextMenu,
    Reveal,
}

impl std::fmt::Debug for Action {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Edit(id, _) => write!(f, "Edit({id})"),
            Self::BeginGesture => f.write_str("BeginGesture"),
            Self::EndGesture => f.write_str("EndGesture"),
            Self::Message(m, e) => write!(f, "Message({m}, {e})"),
            Self::Press { .. } => f.write_str("Press"),
            Self::Rename(name) => write!(f, "Rename({name})"),
            Self::ContextMenu => f.write_str("ContextMenu"),
            Self::Reveal => f.write_str("Reveal"),
        }
    }
}

fn edit(actions: &mut Vec<Action>, id: &str, change: impl Fn(&mut GraphNode) + Send + 'static) {
    actions.push(Action::Edit(id.to_owned(), Box::new(change)));
}

/// A linked control currently silences this node's level.
pub fn linked_level_silenced(graph: &AudioGraph, node: &GraphNode) -> bool {
    let active = !node.bypass || controls::is_device(&node.kind) || matches!(node.kind.as_str(), "stereoSplit" | "stereoCombine");
    graph.edges.iter().any(|edge| {
        edge.kind == "control"
            && edge.to == node.id
            && !edge.muted
            && graph.nodes.iter().any(|c| c.id == edge.from && (c.muted || (active && !c.bypass && c.control_multiplier == 0.0)))
    })
}

#[derive(Clone, Debug, Default)]
pub struct StatusView {
    pub text: String,
    pub color: Color32,
    pub visible: bool,
    pub help: String,
    pub detector: Option<String>,
    pub reduction: Option<f64>,
}

fn availability_of(node: &GraphNode, status: Option<&NodeStatus>, env: &Env<'_>) -> Availability {
    if matches!(node.kind.as_str(), "output" | "virtualOutput") {
        availability::output(node, status, env.endpoints, env.can_edit)
    } else {
        availability::input(node, status, env.endpoints, env.applications, env.can_edit)
    }
}

/// The status line (`Apply` and `ApplyDynamicsFeedbackAt`).
#[allow(clippy::too_many_lines)]
pub fn status_view(node: &GraphNode, status: Option<&NodeStatus>, env: &Env<'_>, runtime: &mut Runtime) -> StatusView {
    let kind = node.kind.as_str();
    let device = controls::is_device(kind);
    let legacy_muted = !device && node.muted;
    let state = status.map(|s| s.path.state.as_str());
    let running = state == Some("Running");
    let clipped = status.is_some_and(|s| s.clipped);
    let path_label = if kind == "appInput" && state == Some("Waiting") {
        "Waiting for app".to_owned()
    } else if let Some(state) = state {
        graph::reasons::path_display(state).to_owned()
    } else if env.preview {
        "Preview · no audio".into()
    } else {
        "Connecting".into()
    };
    let mut view = StatusView { color: theme::MUTED, ..Default::default() };
    view.text = if !env.can_edit {
        "Audio engine unavailable".into()
    } else if device && !running {
        format!("{path_label}{}", if node.muted { " · muted" } else { "" })
    } else if legacy_muted {
        "Muted · click Bypass to restore audio".into()
    } else if node.muted {
        "Muted".into()
    } else if clipped {
        "Clipping · lower the level".into()
    } else if node.bypass {
        "Bypassed · audio passes through".into()
    } else if running {
        if device { "Connected".into() } else { "Processing".into() }
    } else if kind == "control" {
        "Linked volume".into()
    } else if kind == "mix" {
        "Adds all incoming audio".into()
    } else {
        "Ready".into()
    };
    if clipped && !node.muted {
        view.color = theme::ALERT_TEXT;
    }
    view.help = status.map(|s| s.path.message.clone()).unwrap_or_default();
    if clipped && matches!(kind, "output" | "virtualOutput") {
        view.help = "Output clipping\nThis path or the combined device output exceeded its available level\nOutputs sharing a device add together. Lower the contributing levels.".into();
    }
    let virtual_device = graph::bindings::is_virtual(kind);
    view.visible = virtual_device || !env.can_edit || legacy_muted || (device && !running) || (clipped && !node.muted);
    if kind == "control" && env.can_edit && !legacy_muted {
        let legacy = graph::linked::has_legacy_value(node);
        view.text = if legacy {
            "Existing level retained".into()
        } else if node.bypass {
            "Multiplier bypassed".into()
        } else if node.control_multiplier == 0.0 {
            "Linked levels silenced".into()
        } else {
            "Linked levels".into()
        };
        view.visible = legacy || node.bypass || node.control_multiplier == 0.0;
        view.help.clear();
    }
    if virtual_device {
        if env.can_edit && !node.device.assigned() {
            let matches: Vec<&Endpoint> = env.endpoints.iter().filter(|e| graph::bindings::matches(kind, e)).collect();
            view.text = if matches.is_empty() {
                if kind == "virtualInput" { "Waiting for VB-CABLE".into() } else { "Waiting for VAC Lite".into() }
            } else if matches.iter().all(|e| e.state != "Active") {
                "Waiting for device".into()
            } else {
                "Choose device · right-click".into()
            };
        } else if env.can_edit && running && node.muted {
            view.text = "Connected · muted".into();
        }
    }
    let has_level = graph::catalog::has_level_control(kind) || kind == "control";
    if kind != "control" && !has_level && node.gain_db != 0.0 && !legacy_muted && env.can_edit && !clipped {
        view.text = format!("Saved level {} dB · right-click", crate::text::trim_number(node.gain_db, 1));
        view.help = "Saved effect level\nRetained level adjustment from an older version".into();
        view.visible = true;
    }
    if controls::is_dynamics(kind) {
        let presentation = env.can_edit && !legacy_muted && node.gain_db == 0.0 && !clipped;
        dynamics_feedback(node, status, env, runtime, presentation, &mut view);
    }
    let notice = availability_of(node, status, env);
    if notice.kind != NoticeKind::None && (notice.kind != NoticeKind::Muted || !matches!(kind, "output" | "virtualOutput")) {
        view.visible = false;
    }
    view
}

fn dynamics_feedback(node: &GraphNode, status: Option<&NodeStatus>, env: &Env<'_>, runtime: &mut Runtime, presentation: bool, view: &mut StatusView) {
    let d = &mut runtime.dynamics;
    if let Some(s) = status {
        if s.dynamics_frame > 0 && s.dynamics_frame != d.frame {
            d.frame = s.dynamics_frame;
            d.last_time = env.now;
            d.reduction = s.gain_reduction_db;
            d.detector = s.detector_db;
            d.valid = d.reduction.is_finite() && d.detector.is_finite();
        }
    }
    let fresh = status.is_some_and(|s| s.dynamics_frame > 0) && env.now >= d.last_time && env.now - d.last_time <= 0.25;
    let valid = fresh && status.is_some_and(|s| s.path.state == "Running") && d.valid;
    let linked_mute = linked_level_silenced(env.graph, node);
    d.visible_reduction = if presentation && valid && !node.bypass && !node.muted && !linked_mute {
        f64::from(d.reduction).clamp(0.0, 96.0)
    } else {
        0.0
    };
    view.reduction = presentation.then_some((d.visible_reduction / 24.0).min(1.0));
    if !presentation {
        view.detector = Some("— dBFS".into());
        return;
    }
    let kind = node.kind.as_str();
    let band = match kind {
        "deEsser" => "High-band",
        "plosive" => "Low-band",
        _ => "Signal",
    };
    let limiting = kind == "limiter" || (kind == "compressor" && node.compressor.mode == "limit");
    let threshold = match kind {
        "ducking" => node.ducking.threshold_db,
        "deEsser" => node.de_esser.threshold_db,
        "plosive" => node.plosive.threshold_db,
        "expander" | "gate" => node.expander.threshold_db,
        _ if limiting => node.compressor.ceiling_db,
        _ => node.compressor.threshold_db,
    };
    let reduction = d.visible_reduction;
    view.text = if node.muted || linked_mute {
        "Muted".into()
    } else if node.bypass {
        "Bypassed".into()
    } else if !valid {
        "Waiting for audio".into()
    } else if reduction >= 0.05 {
        format!("Reduction {reduction:.1} dB")
    } else if matches!(kind, "expander" | "gate") {
        "Open · Reduction 0.0 dB".into()
    } else if f64::from(d.detector) <= threshold {
        "Below threshold".into()
    } else {
        "Reduction 0.0 dB".into()
    };
    if kind == "ducking" && !node.bypass && !node.muted && !linked_mute {
        view.text = if !env.graph.edges.iter().any(|e| e.kind == "sidechain" && e.to == node.id && !e.muted) {
            "Connect a trigger".into()
        } else if !valid {
            "Waiting for trigger audio".into()
        } else if reduction >= 0.05 {
            "Ducking".into()
        } else if node.ducking.mode == "dynamic" && node.ducking.ratio == 1.0 {
            "No attenuation · ratio 1:1".into()
        } else if node.ducking.mode != "dynamic" && node.ducking.amount_db == 0.0 {
            "No attenuation · amount 0 dB".into()
        } else {
            "Below threshold".into()
        };
    }
    let detector = if valid { format!("{:.1}", d.detector) } else { "—".into() };
    let detector_line = format!("{} · {detector} dBFS", if kind == "ducking" { "Trigger" } else { "Detector" });
    view.detector = Some(format!("{detector} dBFS"));
    view.help = format!(
        "{band} reduction\n{detector_line}\n{} · {threshold:.1} dBFS\n{}",
        if limiting { "Ceiling" } else { "Threshold" },
        if matches!(kind, "compressor" | "limiter" | "ducking" | "expander" | "gate") {
            "Gain reduction · Bar 0–24 dB"
        } else {
            "Shelf amount · Bar 0–24 dB"
        }
    );
    view.visible = true;
    view.color = theme::MUTED;
}

/// Decides whether this sample's peak may be retained (`ObservePeakSample`).
pub fn observe_peak(node: &GraphNode, status: Option<&NodeStatus>, env: &Env<'_>, runtime: &mut Runtime) -> bool {
    let p = &mut runtime.peaks;
    let fresh = p.sequence != env.meter_sequence;
    p.sequence = env.meter_sequence;
    let frames = env.audio_frames;
    let restarted = fresh && p.frame_known && frames < p.last_audio_frame;
    if fresh {
        p.frame_known = true;
        p.last_audio_frame = frames;
    }
    let source = |n: &GraphNode| (n.device.id.clone(), n.device.stable_id.clone(), n.device.channel, n.mono, n.app.process_id, n.app.executable_path.clone());
    let current = source(node);
    let endpoint = status.map(|s| s.path.endpoint_id.clone()).unwrap_or_default();
    let changed = p.source.as_ref().is_some_and(|s| *s != current)
        || (!endpoint.is_empty() && !p.endpoint.is_empty() && endpoint != p.endpoint);
    p.source = Some(current.clone());
    if !endpoint.is_empty() {
        p.endpoint = endpoint;
    }
    if changed || restarted {
        runtime.meter.clear_peak_readings();
        p.skip = true;
        p.await_advance = false;
    }
    if env.acknowledged.is_none_or(|a| source(a) != current) {
        runtime.meter.clear_peak_readings();
        p.skip = true;
        p.await_advance = false;
        return false;
    }
    if !fresh || status.is_none() {
        return false;
    }
    if p.skip {
        p.skip = false;
        p.await_advance = true;
        p.boundary = frames;
        return false;
    }
    if p.await_advance {
        if frames > p.boundary {
            p.await_advance = false;
        }
        return false;
    }
    true
}

/// Camera: world → screen.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Camera {
    pub origin: Pos2,
    pub pan: Vec2,
    pub zoom: f32,
}

impl Camera {
    pub fn to_screen(&self, world: Pos2) -> Pos2 {
        self.origin + self.pan + world.to_vec2() * self.zoom
    }
    pub fn to_world(&self, screen: Pos2) -> Pos2 {
        ((screen - self.origin - self.pan) / self.zoom).to_pos2()
    }
    pub fn rect(&self, world: Rect) -> Rect {
        Rect::from_min_max(self.to_screen(world.min), self.to_screen(world.max))
    }
}

/// Draws and interacts with one node. `origin` is the node's world position.
#[allow(clippy::too_many_arguments, clippy::too_many_lines)]
pub fn show(
    ui: &mut Ui,
    cam: Camera,
    node: &GraphNode,
    origin: Pos2,
    lay: &Layout,
    status: Option<&NodeStatus>,
    env: &Env<'_>,
    runtime: &mut Runtime,
    selected: bool,
    actions: &mut Vec<Action>,
) -> egui::Response {
    let s = cam.zoom;
    let id = Id::unique(("node", node.id.as_str()));
    let to_screen = |r: Rect| cam.rect(r.translate(origin.to_vec2()));
    let node_rect = to_screen(Rect::from_min_size(Pos2::ZERO, Vec2::new(lay.width, lay.height)));
    let editing = runtime.editing || runtime.rename.is_some();
    let detail = graph::canvas::detail_at(f64::from(s), runtime.detail(), false);
    let detail = if detail > runtime.detail() && editing { runtime.detail() } else { detail };
    runtime.detail = Some(detail);
    let full = detail == DetailLevel::Full;
    let view = status_view(node, status, env, runtime);
    let notice = if crate::ui::controls::is_device(&node.kind) { Some(availability_of(node, status, env)) } else { None };
    let problem = notice.as_ref().is_some_and(|n| n.kind == NoticeKind::Problem);

    // The drag surface underneath every control: clicks select, drags move.
    let surface = ui.interact(node_rect, id.with("surface"), Sense::click_and_drag());
    let painter = ui.painter().clone();
    if problem {
        // Faint concentric strokes outside the border (`AvailabilityWarningGlow`).
        let mut offset = 10.0 - 0.25;
        while offset > 0.0 {
            let opacity = 0.35 * (1.0f32 - offset / 10.0).powi(2);
            painter.rect_stroke(
                node_rect.expand(offset),
                5.0 * s + offset,
                Stroke::new(0.5, theme::with_opacity(theme::MUTE, opacity)),
                egui::StrokeKind::Middle,
            );
            offset -= 0.5;
        }
    }
    painter.rect(node_rect, 5.0 * s, theme::PANEL, Stroke::new(s, theme::NODE_OUTLINE), egui::StrokeKind::Inside);
    let header = to_screen(Rect::from_min_size(Pos2::new(1.0, 1.0), Vec2::new(lay.width - 2.0, HEADER)));
    painter.rect_filled(
        header,
        egui::CornerRadius { nw: (4.0 * s) as u8, ne: (4.0 * s) as u8, sw: 0, se: 0 },
        theme::header_color(graph::catalog::category_for(&node.kind).header),
    );
    let header_response = ui.interact(header, id.with("header"), Sense::click_and_drag());
    let header_response = if full {
        header_response.tip(controls::node_description(node))
    } else {
        header_response.tip(format!(
            "{}\n{}\n{}\nZoom in to edit controls and connections",
            node.name,
            graph::kind_label(&node.kind),
            if node.muted {
                "Muted"
            } else if node.bypass {
                "Bypassed"
            } else {
                view.text.as_str()
            }
        ))
    };
    if full {
        if let Some(draft) = runtime.rename.as_mut() {
            let rename_rect = header.shrink2(Vec2::new(5.0 * s, 2.0 * s));
            let rename_rect = Rect::from_min_max(Pos2::new(header.left() + 10.0 * s, rename_rect.top()), rename_rect.max);
            let rename_id = id.with("rename");
            ui.painter().rect(rename_rect, 3.0 * s, theme::WELL, Stroke::new(s.max(1.0), theme::FOCUS), egui::StrokeKind::Inside);
            let response = egui::TextEdit::singleline(draft)
                .id(rename_id)
                .font(theme::font(14.0 * s, Weight::Regular))
                .frame(egui::Frame::NONE)
                .char_limit(80)
                .align(egui::Align2::LEFT_CENTER)
                .margin(egui::Margin::symmetric((3.0 * s) as i8, 0))
                .desired_width(rename_rect.width())
                .min_size(rename_rect.size())
                .show(&mut widgets::field_ui(ui, rename_rect))
                .response;
            if runtime.rename_focus {
                response.request_focus();
                runtime.rename_focus = false;
            }
            let (enter, escape) = ui.input(|i| (i.key_pressed(egui::Key::Enter), i.key_pressed(egui::Key::Escape)));
            if escape {
                runtime.rename = None;
            } else if enter || response.lost_focus() {
                let name = draft.trim().to_owned();
                runtime.rename = None;
                if !name.is_empty() && name != node.name {
                    actions.push(Action::Rename(name));
                }
            }
        } else {
            widgets::text_elided(
                &painter,
                Pos2::new(header.left() + 12.0 * s, header.center().y),
                Align2::LEFT_CENTER,
                &node.name,
                theme::font(15.0 * s, Weight::SemiBold),
                theme::TEXT,
                header.width() - 24.0 * s,
            );
        }
    }
    for response in [&surface, &header_response] {
        if response.drag_started_by(egui::PointerButton::Primary) || response.is_pointer_button_down_on() && ui.input(|i| i.pointer.primary_pressed()) {
            if let Some(pointer) = ui.input(|i| i.pointer.press_origin()) {
                actions.push(Action::Press { modifiers: ui.input(|i| i.modifiers), pointer });
            }
        }
        if response.double_clicked() && full && env.can_edit {
            runtime.rename = Some(node.name.clone());
            runtime.rename_focus = true;
            actions.push(Action::Reveal);
        }
        if response.secondary_clicked() {
            actions.push(Action::ContextMenu);
        }
    }

    if full {
        // Port labels.
        for port in &lay.outputs {
            let y = cam.to_screen(Pos2::new(0.0, origin.y + port.y)).y;
            widgets::text_elided(
                &painter,
                Pos2::new(cam.to_screen(Pos2::new(origin.x + lay.width - 13.0, 0.0)).x, y),
                Align2::RIGHT_CENTER,
                &port.label,
                theme::font(13.0 * s, Weight::Regular),
                theme::TEXT,
                (lay.width - 26.0) * s,
            );
        }
        for port in &lay.inputs {
            let y = cam.to_screen(Pos2::new(0.0, origin.y + port.y)).y;
            let label_rect = widgets::text_elided(
                &painter,
                Pos2::new(cam.to_screen(Pos2::new(origin.x + 13.0, 0.0)).x, y),
                Align2::LEFT_CENTER,
                &port.label,
                theme::font(13.0 * s, Weight::Regular),
                theme::TEXT,
                (lay.width - 26.0) * s,
            );
            if port.kind == "sidechain" && matches!(node.kind.as_str(), "gate" | "expander") {
                ui.interact(label_rect, id.with(("port-help", port.port, port.kind)), Sense::hover()).tip(
                    "Optional detector-only audio. No cable uses this node's Audio input; a connected silent or unavailable source stays silent.",
                );
            } else if port.kind == "control" && graph::linked::has_legacy_connection(env.graph, node) {
                ui.interact(label_rect, id.with(("port-help", port.port, port.kind)), Sense::hover())
                    .tip("Existing level link. Use a Mix node for new level control.");
            }
        }
        if let Some(meter_rect) = lay.meter {
            let meter_screen = to_screen(meter_rect);
            let hidden = node.muted || linked_level_silenced(env.graph, node) || !env.can_edit || status.is_none_or(|st| st.path.state != "Running");
            let stereo = status.is_some_and(|st| st.channels == 2);
            let (left, right) = status.map_or((0.0, 0.0), |st| {
                if stereo { (f64::from(st.peak_left), f64::from(st.peak_right)) } else { (f64::from(st.peak), f64::from(st.peak)) }
            });
            let observe = observe_peak(node, status, env, runtime);
            runtime.meter.update(left, right, if stereo { 2 } else { 1 }, status.is_some_and(|st| st.clipped), hidden, env.reduced_animation, env.now, observe);
            runtime.meter.advance(env.now);
            meter::paint(&painter, meter_screen, &runtime.meter, s);
            let lines = runtime.meter.peak_lines();
            ui.interact(meter_screen, id.with("meter"), Sense::hover()).tip(format!(
                "Highest observed peak\n{}\nHighest sample level observed per channel",
                lines.join("\n")
            ));
            if let Some(next) = runtime.meter.next_frame() {
                ui.ctx().request_repaint_after(std::time::Duration::from_secs_f64(next));
            }
        }
        if problem {
            let output_device = matches!(node.kind.as_str(), "output" | "virtualOutput");
            let icon = if output_device {
                Rect::from_min_size(Pos2::new(18.0, lay.content_top + 7.0), Vec2::new(18.0, 16.0))
            } else {
                Rect::from_min_size(Pos2::new(18.0, 1.0 + HEADER + 6.0), Vec2::new(18.0, 16.0))
            };
            paint_warning_icon(&painter, to_screen(icon));
        }
        runtime.editing = false;
        let mut ctx = ItemCtx { ui, cam, node, status, origin, env, runtime, actions, id, view: &view, notice: notice.as_ref(), s };
        for (item, rect) in &lay.items {
            ctx.item(item, *rect);
        }
        if let Some(bypass) = lay.bypass {
            let rect = to_screen(bypass);
            let legacy_muted = node.muted;
            let on = node.bypass;
            let style = ButtonStyle {
                fill: if on { theme::MUTE } else { theme::BUTTON_FACE },
                text: if on { theme::INK } else { theme::TEXT },
                border: if on { theme::MUTE } else { theme::MUTED },
                border_width: 1.5,
                font_size: 14.0,
                weight: Weight::SemiBold,
                keep_fill: true,
                flat: false,
            };
            let response = widgets::button(ui, id.with("bypass"), rect, "Bypass", style, env.can_edit, s);
            let help = if legacy_muted {
                "Previously muted\nRestores audio with this node's processing bypassed"
            } else if node.kind == "control" {
                if on { "Apply the multiplier again." } else { "Leave linked levels unchanged." }
            } else if on {
                "Restore this node's processing and level adjustment."
            } else if node.kind == "crossfade" {
                "Play input A only, without blending input B."
            } else {
                "Pass audio through without this node's processing or level adjustment."
            };
            if response.tip(help).clicked() {
                let value = node.muted || !node.bypass;
                edit(actions, &node.id, move |n| {
                    n.muted = false;
                    n.bypass = value;
                });
            }
        }
    }
    if selected && !env.layout_locked {
        painter.rect_stroke(node_rect.expand(0.0), 5.0 * s, Stroke::new(2.0, theme::NODE_SELECTED), egui::StrokeKind::Outside);
    }
    surface
}

fn paint_warning_icon(painter: &egui::Painter, rect: Rect) {
    let p = |x: f32, y: f32| Pos2::new(rect.left() + x / 18.0 * rect.width(), rect.top() + y / 16.0 * rect.height());
    painter.add(egui::Shape::convex_polygon(vec![p(9.0, 0.0), p(18.0, 16.0), p(0.0, 16.0)], theme::METER_HIGH, Stroke::NONE));
    painter.rect_filled(Rect::from_min_max(p(8.0, 5.0), p(10.0, 10.5)), 0.0, theme::PANEL);
    painter.rect_filled(Rect::from_min_max(p(8.0, 12.0), p(10.0, 14.0)), 0.0, theme::PANEL);
}

/// Availability warnings pulse only the selected device text.
fn warning_text_color(env: &Env<'_>, detail_full: bool) -> Color32 {
    let reduced = !detail_full || env.reduced_animation || !crate::platform::client_area_animation();
    if reduced {
        return theme::METER_HIGH;
    }
    let period = 1.6;
    let phase = (env.now % period) / period;
    // Sine ease in-out, red → normal → red.
    let t = if phase < 0.5 { phase * 2.0 } else { (1.0 - phase) * 2.0 };
    let eased = (1.0 - (std::f64::consts::PI * t).cos()) / 2.0;
    theme::mix(theme::METER_HIGH, theme::TEXT, eased as f32)
}

pub struct ItemCtx<'a, 'u> {
    pub(crate) ui: &'u mut Ui,
    pub(crate) cam: Camera,
    pub(crate) node: &'a GraphNode,
    pub(crate) status: Option<&'a NodeStatus>,
    pub(crate) origin: Pos2,
    pub(crate) env: &'a Env<'a>,
    pub(crate) runtime: &'a mut Runtime,
    pub(crate) actions: &'a mut Vec<Action>,
    pub(crate) id: Id,
    pub(crate) view: &'a StatusView,
    pub(crate) notice: Option<&'a Availability>,
    pub(crate) s: f32,
}

impl std::fmt::Debug for ItemCtx<'_, '_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("ItemCtx").field("node", &self.node.id).finish_non_exhaustive()
    }
}

impl ItemCtx<'_, '_> {
    pub(crate) fn screen(&self, r: Rect) -> Rect {
        self.cam.rect(r.translate(self.origin.to_vec2()))
    }

    fn device_text_color(&self) -> Color32 {
        if self.notice.is_some_and(|n| n.kind == NoticeKind::Problem) {
            self.ui.ctx().request_repaint_after(std::time::Duration::from_millis(33));
            warning_text_color(self.env, true)
        } else {
            theme::TEXT
        }
    }

    fn notice_help(&self, fallback: &str) -> String {
        match self.notice {
            Some(n) if !matches!(n.kind, NoticeKind::None | NoticeKind::Muted) => {
                let detail = graph::reasons::detail(self.node, None);
                let _ = detail;
                format!("{}\n{}", n.label, n.detail)
            }
            _ => fallback.to_owned(),
        }
    }

    #[allow(clippy::too_many_lines)]
    fn item(&mut self, item: &Item, rect: Rect) {
        let s = self.s;
        let node = self.node;
        match item {
            Item::Status => {
                let line = self.screen(Rect::from_min_size(rect.min + Vec2::new(0.0, 7.0), Vec2::new(rect.width(), 18.0)));
                let painter = self.ui.painter().clone();
                let detector_width = if self.view.detector.is_some() { 92.0 * s } else { 0.0 };
                if self.view.visible {
                    let shown = widgets::text_elided(
                        &painter,
                        Pos2::new(line.left(), line.center().y),
                        Align2::LEFT_CENTER,
                        &self.view.text,
                        theme::font(13.0 * s, Weight::Regular),
                        self.view.color,
                        line.width() - detector_width,
                    );
                    if !self.view.help.is_empty() {
                        self.ui.interact(shown, self.id.with("status"), Sense::hover()).tip(&self.view.help);
                    }
                }
                if let Some(detector) = &self.view.detector {
                    let r = widgets::text_elided(
                        &painter,
                        Pos2::new(line.right(), line.center().y),
                        Align2::RIGHT_CENTER,
                        detector,
                        theme::font(11.0 * s, Weight::Mono),
                        theme::MUTED,
                        detector_width,
                    );
                    self.ui
                        .interact(r, self.id.with("detector"), Sense::hover())
                        .tip(if node.kind == "ducking" { "Trigger level" } else { "Detector level" });
                }
                if let Some(amount) = self.view.reduction {
                    let strip = Rect::from_min_size(Pos2::new(line.left(), line.bottom() + 2.0 * s), Vec2::new(line.width(), 2.0 * s));
                    painter.rect_filled(strip, 0.0, theme::BORDER);
                    if amount > 0.0 {
                        painter.rect_filled(Rect::from_min_size(strip.min, Vec2::new(strip.width() * amount as f32, strip.height())), 0.0, theme::A);
                    }
                }
            }
            Item::VirtualDevice => {
                let r = self.screen(rect);
                let endpoint =
                    self.env.endpoints.iter().find(|e| graph::bindings::matches(&node.kind, e) && graph::bindings::same(&node.device, e));
                let text = match endpoint {
                    Some(e) if !e.name.is_empty() => e.name.clone(),
                    _ if !node.device.name.is_empty() => node.device.name.clone(),
                    _ if node.kind == "virtualInput" => "CABLE Output".into(),
                    _ => "Line 1".into(),
                };
                let color = self.device_text_color();
                let shown = widgets::text_elided(
                    self.ui.painter(),
                    Pos2::new(r.left(), r.center().y),
                    Align2::LEFT_CENTER,
                    &text,
                    theme::font(14.0 * s, Weight::Regular),
                    color,
                    r.width(),
                );
                let help = if node.kind == "virtualInput" {
                    format!("{text}\nReceives audio sent to the matching CABLE Input playback device")
                } else {
                    format!("{text}\nSends audio to the matching Line 1 recording device for Discord or games")
                };
                let help = self.notice_help(&help);
                self.ui.interact(shown.union(Rect::from_min_size(r.min, Vec2::new(r.width(), r.height()))), self.id.with("virtual"), Sense::hover()).tip(help);
            }
            Item::Device => self.device_choice(rect),
            Item::App => self.app_choice(rect),
            Item::Mono => {
                let r = self.screen(Rect::from_min_size(rect.min + Vec2::new(0.0, 6.0), Vec2::new(rect.width(), 30.0)));
                let mono = node.mono || node.device.channel >= 0;
                let style = ButtonStyle::state(mono, theme::BRAND_ACTION, theme::BRAND_ACTION_TEXT, 13.0);
                let response = widgets::button(self.ui, self.id.with("mono"), r, "Mono", style, self.env.can_edit, s)
                    .tip("Mono\nOn · Combine left and right\nOff · Keep stereo");
                if response.clicked() {
                    let value = !mono;
                    edit(self.actions, &node.id, move |n| {
                        n.device.channel = -1;
                        n.mono = value;
                    });
                }
            }
            Item::Mute => {
                let r = self.screen(rect);
                let style = ButtonStyle::state(node.muted, theme::MUTE, theme::INK, 13.0);
                let label = if node.muted { "Muted" } else { "Mute" };
                let response =
                    widgets::button(self.ui, self.id.with("mute"), r, label, style, self.env.can_edit, s).tip("Silence this input or output.");
                if response.clicked() {
                    let value = !node.muted;
                    edit(self.actions, &node.id, move |n| n.muted = value);
                }
            }
            Item::Choice(key) => {
                let r = self.screen(Rect::from_min_size(rect.min, Vec2::new(rect.width(), 30.0)));
                let options = key.options();
                let current = key.get(node);
                let selected = options.iter().position(|(value, _)| *value == current);
                let choices: Vec<Choice> = options
                    .iter()
                    .map(|(value, label)| Choice {
                        label: (*label).to_owned(),
                        enabled: true,
                        help: if *key == ChoiceKey::DuckingMode { controls::ducking_description(value) } else { String::new() },
                    })
                    .collect();
                let text = selected.map_or("", |i| options[i].1);
                let (_, picked) = widgets::selector(
                    self.ui,
                    self.id.with(("choice", key.name())),
                    r,
                    selected,
                    text,
                    &choices,
                    self.env.can_edit,
                    s,
                    theme::TEXT,
                    &key.help(node),
                );
                if let Some(index) = picked {
                    let key = *key;
                    let value = options[index].0;
                    if value != current {
                        edit(self.actions, &node.id, move |n| key.set(n, value));
                    }
                }
            }
            Item::Caption(text) => {
                let r = self.screen(rect);
                self.ui.painter().text(r.left_top(), Align2::LEFT_TOP, *text, theme::font(12.0 * s, Weight::SemiBold), theme::TEXT);
            }
            Item::Note(text) => {
                let r = self.screen(Rect::from_min_max(rect.min + Vec2::new(0.0, 3.0), rect.max));
                widgets::text_wrapped(self.ui.painter(), r, text, theme::font(11.0 * s, Weight::Regular), theme::MUTED, egui::Align::Min);
            }
            Item::Help(text) => {
                let r = self.screen(Rect::from_min_max(rect.min + Vec2::new(0.0, 3.0), rect.max));
                widgets::text_wrapped(self.ui.painter(), r, text, theme::font(12.0 * s, Weight::Regular), theme::TEXT, egui::Align::Min);
            }
            Item::DenoiserDelay => {
                let r = self.screen(rect);
                let text = if node.denoiser.algorithm == "rnnoise" { "Delay · 30 ms, including bypass" } else { "Delay · 20 ms, including bypass" };
                self.ui.painter().text(r.left_top(), Align2::LEFT_TOP, text, theme::font(12.0 * s, Weight::Regular), theme::TEXT);
            }
            Item::Divider => {
                let r = self.screen(Rect::from_min_size(rect.min + Vec2::new(0.0, 4.0), Vec2::new(rect.width(), 1.0)));
                self.ui.painter().rect_filled(r, 0.0, theme::BORDER);
            }
            Item::Dial(d, _) => self.row_dial(d, Rect::from_min_size(rect.min, Vec2::new(rect.width(), 91.0))),
            Item::Tone(dials) => {
                for (index, d) in dials.iter().enumerate() {
                    self.row_dial(d, Rect::from_min_size(rect.min + Vec2::new(0.0, 5.0 + 95.0 * index as f32), Vec2::new(rect.width(), 91.0)));
                }
            }
            Item::Multiplier(d) => {
                let tile = Rect::from_min_size(Pos2::new(rect.center().x - 68.0, rect.top()), Vec2::new(136.0, 135.0));
                self.tile_dial(d, tile, 86.0, true);
            }
            Item::LegacyMultiplier => {
                let r = self.screen(rect);
                let text = format!("Saved multiplier {}%", crate::text::trim_number(graph::linked::percent(node), 3));
                self.ui.painter().text(
                    Pos2::new(r.center().x, r.top() + 12.0 * s),
                    Align2::CENTER_TOP,
                    text,
                    theme::font(16.0 * s, Weight::SemiBold),
                    theme::TEXT,
                );
                let button = self.screen(Rect::from_min_size(Pos2::new(rect.left(), rect.bottom() - 26.0), Vec2::new(rect.width(), 26.0)));
                if widgets::button(self.ui, self.id.with("use-multiplier"), button, "Use multiplier", ButtonStyle::console(13.0), self.env.can_edit, s)
                    .tip("Set the multiplier to 100%.")
                    .clicked()
                {
                    edit(self.actions, &node.id, |n| {
                        let _ = graph::linked::set_percent(n, 100.0);
                    });
                }
            }
            Item::Group { columns, cells } => self.group(*columns, cells, rect),
            Item::Alternatives(alternatives, select) => {
                let chosen = select(node).min(alternatives.len().saturating_sub(1));
                let mut y = rect.top();
                for item in &alternatives[chosen] {
                    let height = item_height(self.ui.ctx(), item, rect.width());
                    self.item(item, Rect::from_min_size(Pos2::new(rect.left(), y), Vec2::new(rect.width(), height)));
                    y += height;
                }
            }
            Item::Eq => {
                let plot = Rect::from_min_size(rect.min, Vec2::new(rect.width(), 300.0));
                let details = Rect::from_min_size(rect.min + Vec2::new(0.0, 307.0), Vec2::new(rect.width(), 139.0));
                super::eq::show(self, plot, details);
            }
            Item::VoicePad => {
                let pad = Rect::from_min_size(rect.min, Vec2::new(rect.width(), 388.0));
                super::voice::show(self, pad);
            }
        }
    }

    fn group(&mut self, columns: usize, cells: &[Cell], rect: Rect) {
        let row_height = group_row_height(cells);
        let columns = columns.max(1);
        let cell_width = rect.width() / columns as f32;
        let tile_width: f32 = if columns == 3 { 120.0 } else { 136.0 };
        for (index, cell) in cells.iter().enumerate() {
            let row = index / columns;
            let column = index % columns;
            let cell_rect = Rect::from_min_size(
                Pos2::new(rect.left() + cell_width * column as f32, rect.top() + 6.0 + row as f32 * (row_height + 53.0)),
                Vec2::new(cell_width, row_height + 49.0),
            );
            let width = tile_width.min(cell_width);
            let tile = Rect::from_min_size(Pos2::new(cell_rect.center().x - width / 2.0, cell_rect.top()), Vec2::new(width, cell_rect.height()));
            match cell {
                Cell::Dial(d) => self.tile_dial(d, tile, row_height, d.spec.primary),
                Cell::Alternatives(dials, select) => {
                    let chosen = select(self.node).min(dials.len().saturating_sub(1));
                    self.tile_dial(&dials[chosen], tile, row_height, dials[chosen].spec.primary);
                }
            }
        }
    }

    /// Title (18), dial slot (`row_height` + 3), entry (28).
    fn tile_dial(&mut self, d: &Dial, tile: Rect, row_height: f32, primary: bool) {
        let s = self.s;
        let title = self.screen(Rect::from_min_size(tile.min, Vec2::new(tile.width(), 18.0)));
        self.ui.painter().text(title.center(), Align2::CENTER_CENTER, &d.spec.label, theme::font(12.0 * s, Weight::SemiBold), theme::TEXT);
        let face_size = if primary || d.spec.variant == Variant::Compact { 86.0 } else { 58.0 };
        let slot_top = tile.top() + 18.0 + 2.0;
        let face = Rect::from_center_size(Pos2::new(tile.center().x, slot_top + row_height / 2.0), Vec2::splat(face_size));
        let entry_width = 110.0f32.min(tile.width());
        let entry = Rect::from_min_size(Pos2::new(tile.center().x - entry_width / 2.0, slot_top + row_height + 1.0), Vec2::new(entry_width, 28.0));
        self.run_dial(d, face, entry);
    }

    /// Labels, an 88-unit dial column and a 118-unit entry column.
    fn row_dial(&mut self, d: &Dial, row: Rect) {
        let s = self.s;
        let entry_column = Rect::from_min_size(Pos2::new(row.right() - 118.0, row.top()), Vec2::new(118.0, row.height()));
        let dial_column = Rect::from_min_size(Pos2::new(entry_column.left() - 88.0, row.top()), Vec2::new(88.0, row.height()));
        let labels = Rect::from_min_max(row.min, Pos2::new(dial_column.left() - 6.0, row.bottom()));
        let painter = self.ui.painter().clone();
        let title_height = 13.0 * 1.3;
        let detail_height = if d.spec.detail.is_empty() { 0.0 } else { 5.0 + 12.0 * 1.3 };
        let top = labels.center().y - (title_height + detail_height) / 2.0;
        widgets::text_elided(
            &painter,
            self.cam.to_screen(Pos2::new(self.origin.x + labels.left(), self.origin.y + top)),
            Align2::LEFT_TOP,
            &d.spec.label,
            theme::font(13.0 * s, Weight::SemiBold),
            theme::TEXT,
            labels.width() * s,
        );
        if !d.spec.detail.is_empty() {
            widgets::text_elided(
                &painter,
                self.cam.to_screen(Pos2::new(self.origin.x + labels.left(), self.origin.y + top + title_height + 5.0)),
                Align2::LEFT_TOP,
                &d.spec.detail,
                theme::font(12.0 * s, Weight::Regular),
                theme::MUTED,
                labels.width() * s,
            );
        }
        let face = Rect::from_center_size(Pos2::new(dial_column.center().x, row.top() + 2.0 + 44.0), Vec2::splat(86.0));
        let entry = Rect::from_min_size(Pos2::new(entry_column.right() - 110.0, row.center().y - 14.0), Vec2::new(110.0, 28.0));
        self.run_dial(d, face, entry);
    }

    fn run_dial(&mut self, d: &Dial, face: Rect, entry: Rect) {
        let value = (d.get)(self.node);
        let face_screen = self.screen(face);
        let entry_screen = self.screen(entry);
        let state = self.runtime.dials.entry(d.spec.key.clone()).or_default();
        let id = self.id.with(("dial", d.spec.key.as_str()));
        let outcome = dial::show(self.ui, id, &d.spec, value, d.spec.default, state, face_screen, entry_screen, self.s, self.env.can_edit);
        self.runtime.editing |= outcome.editing || state.dragging;
        if outcome.gesture_started {
            self.actions.push(Action::BeginGesture);
        }
        if let Some(next) = outcome.changed {
            let set = d.set.clone();
            edit(self.actions, &self.node.id, move |n| set(n, next));
        }
        if outcome.gesture_ended {
            self.actions.push(Action::EndGesture);
        }
    }

    fn device_choice(&mut self, rect: Rect) {
        let node = self.node;
        let r = self.screen(rect);
        let direction = if node.kind == "microphone" { "capture" } else { "render" };
        let choices = availability::build(self.env.endpoints, direction, &node.device, self.env.visibility);
        let selected = availability::selected(&choices, &node.device);
        let text = selected.map(|i| choices[i].label()).unwrap_or_default();
        let entries: Vec<Choice> = choices.iter().map(|c| Choice { label: c.label(), enabled: c.selectable, help: c.help.clone() }).collect();
        let color = self.device_text_color();
        let help = self.notice_help("Choose an available audio device. Additional devices: Settings → Audio device visibility.");
        let (_, picked) = widgets::selector(self.ui, self.id.with("device"), r, selected, &text, &entries, self.env.can_edit, self.s, color, &help);
        if let Some(index) = picked {
            let choice = &choices[index];
            if !availability::can_assign(choice, self.env.endpoints, direction) {
                self.actions.push(Action::Message(format!("This device is unavailable. Reopen the list to see current devices. {}", choice.help), true));
                return;
            }
            let binding = choice.binding(-1);
            edit(self.actions, &node.id, move |n| {
                n.device = binding.clone();
                n.auto_assign_device = binding.assigned();
            });
        }
    }

    fn app_choice(&mut self, rect: Rect) {
        let node = self.node;
        let r = self.screen(rect);
        let mut applications: Vec<ApplicationInfo> = self.env.applications.to_vec();
        applications.sort_by(|a, b| a.name.cmp(&b.name));
        let same_path = |a: &ApplicationInfo| !node.app.executable_path.is_empty() && crate::text::eq_ignore_case(&a.executable_path, &node.app.executable_path);
        let mut selected = applications.iter().position(|a| {
            node.app.process_id != 0
                && a.process_id == node.app.process_id
                && (node.app.executable_path.is_empty() || crate::text::eq_ignore_case(&a.executable_path, &node.app.executable_path))
        });
        if selected.is_none() {
            let same: Vec<usize> = applications.iter().enumerate().filter(|(_, a)| same_path(a)).map(|(i, _)| i).collect();
            if same.len() == 1 {
                selected = Some(same[0]);
            }
        }
        if selected.is_none() && (node.app.process_id != 0 || !node.app.executable_path.is_empty()) {
            applications.push(ApplicationInfo {
                process_id: node.app.process_id,
                executable_path: node.app.executable_path.clone(),
                name: format!("{} · saved selection", node.app.name),
                has_audio_session: false,
            });
            selected = Some(applications.len() - 1);
        }
        applications.insert(0, ApplicationInfo { name: "Choose a running app".into(), ..Default::default() });
        let selected = Some(selected.map_or(0, |i| i + 1));
        let entries: Vec<Choice> =
            applications.iter().map(|a| Choice { label: graph::application_label(a), enabled: true, help: a.executable_path.clone() }).collect();
        let text = selected.map(|i| entries[i].label.clone()).unwrap_or_default();
        let color = self.device_text_color();
        let help = self.notice_help("App capture\nCaptures a copy of the app's audio\nOriginal playback continues\nSame speakers on both paths = doubled audio");
        let (_, picked) = widgets::selector(self.ui, self.id.with("app"), r, selected, &text, &entries, self.env.can_edit, self.s, color, &help);
        if let Some(index) = picked {
            let app = &applications[index];
            if app.process_id != 0 && app.executable_path.is_empty() {
                self.actions.push(Action::Message(
                    "Windows did not provide this app's identity. Choose another app or use the virtual input.".into(),
                    true,
                ));
                return;
            }
            let pid = app.process_id;
            let path = app.executable_path.clone();
            let name = if pid == 0 && path.is_empty() { String::new() } else { app.name.clone() };
            edit(self.actions, &node.id, move |n| {
                n.app = vinemix_model::config::ProcessBinding { process_id: pid, executable_path: path.clone(), name: name.clone() };
            });
        }
    }
}

impl ItemCtx<'_, '_> {
    pub fn edit(&mut self, change: impl Fn(&mut GraphNode) + Send + 'static) {
        edit(self.actions, &self.node.id, change);
    }
    pub fn begin_gesture(&mut self) {
        self.actions.push(Action::BeginGesture);
    }
    pub fn end_gesture(&mut self) {
        self.actions.push(Action::EndGesture);
    }
}

/// Sockets in world coordinates for cable anchoring and snapping.
pub fn socket_world(origin: Pos2, lay: &Layout, port: &Port) -> Pos2 {
    Pos2::new(origin.x + if port.output { lay.width - 0.5 } else { 0.5 }, origin.y + port.y)
}

/// Draws a socket glyph: circle (audio), diamond (trigger) or square (control).
pub fn paint_socket(painter: &egui::Painter, center: Pos2, kind: &str, color: Color32, connected: bool, highlighted: bool, s: f32) {
    let fill = if connected { color } else { theme::CANVAS };
    let stroke_color = if highlighted {
        theme::NODE_SELECTED
    } else if connected {
        theme::CANVAS
    } else {
        color
    };
    // WPF shapes keep their stroke inside the declared size: the geometry is
    // inset by half the 1.6 px stroke (12 px circle and diamond, 10 px square).
    let stroke = Stroke::new(1.6 * s, stroke_color);
    let inset = 0.8 * s;
    match kind {
        "control" => {
            painter.rect(Rect::from_center_size(center, Vec2::splat(10.0 * s - 2.0 * inset)), 1.5 * s, fill, stroke, egui::StrokeKind::Middle);
        }
        "sidechain" => {
            let r = 6.0 * s - inset;
            painter.add(egui::Shape::convex_polygon(
                vec![center + Vec2::new(-r, 0.0), center + Vec2::new(0.0, -r), center + Vec2::new(r, 0.0), center + Vec2::new(0.0, r)],
                fill,
                stroke,
            ));
        }
        _ => {
            super::widgets::circle(painter, center, 6.0 * s - inset, fill, stroke);
        }
    }
}
