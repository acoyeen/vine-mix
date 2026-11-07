//! Desktop graph rules (`GraphDefaults`, `NodeCatalog`, `LinkedLevelControl`,
//! `VirtualDeviceBindings`, `NodeSearch`, `StatusReasons`).
//!
//! These are the desktop's own presentation and editing rules. They are kept
//! separate from the engine validator: the desktop refuses an edit before it
//! is queued, and the engine remains the final authority.

use vinemix_model::config::{
    default_eq_bands, tone_defaults, AudioGraph, DeviceBinding, EqBand, GraphEdge, GraphNode, BUTTERWORTH_Q,
};
use vinemix_model::status::{ApplicationInfo, Endpoint, PathState};

/// Every kind the desktop accepts in a saved graph.
pub const KINDS: [&str; 34] = [
    "microphone", "virtualInput", "appInput", "mix", "gain", "eq", "tone", "highPass", "lowPass", "compressor", "limiter", "ducking", "pan",
    "mono", "reroute", "output", "virtualOutput", "control", "denoiser", "deEsser", "plosive", "reverb", "voiceColor", "expander", "gate",
    "alignmentDelay", "channelSwap", "stereoWidth", "stereoPan", "crossfade", "stereoCombine", "stereoSplit", "echo", "distortion",
];

pub const MAXIMUM_NODES: usize = 32;
pub const MAXIMUM_EDGES: usize = 96;
pub const MAXIMUM_EQ_BANDS: usize = 8;

/// Canvas navigation limits shared by validation, the camera and the minimap.
pub mod canvas {
    pub const MINIMUM_ZOOM: f64 = 0.1;
    pub const MAXIMUM_ZOOM: f64 = 2.0;
    pub const MAXIMUM_PAN: f64 = 100_000.0;
    pub const ZOOM_STOPS: [f64; 16] = [0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5, 0.6, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0];

    pub fn clamp_zoom(zoom: f64) -> f64 {
        if zoom.is_finite() { zoom.clamp(MINIMUM_ZOOM, MAXIMUM_ZOOM) } else { 1.0 }
    }

    pub fn clamp_pan(pan: f64) -> f64 {
        if pan.is_nan() { 20.0 } else { pan.clamp(-MAXIMUM_PAN, MAXIMUM_PAN) }
    }

    /// A camera: screen = world * zoom + pan.
    #[derive(Clone, Copy, Debug, PartialEq)]
    pub struct View {
        pub pan_x: f64,
        pub pan_y: f64,
        pub zoom: f64,
    }

    impl View {
        pub fn to_world(&self, x: f64, y: f64) -> (f64, f64) {
            ((x - self.pan_x) / self.zoom, (y - self.pan_y) / self.zoom)
        }
    }

    pub fn zoom_around(current: View, anchor: (f64, f64), zoom: f64) -> View {
        let (wx, wy) = current.to_world(anchor.0, anchor.1);
        let next = clamp_zoom(zoom);
        View { pan_x: anchor.0 - wx * next, pan_y: anchor.1 - wy * next, zoom: next }
    }

    /// Bounds are (left, top, width, height).
    pub fn fit_zoom(bounds: Option<(f64, f64, f64, f64)>, available: (f64, f64)) -> f64 {
        match bounds {
            Some((_, _, w, h)) if w > 0.0 && h > 0.0 => (available.0 / w).min(available.1 / h).clamp(MINIMUM_ZOOM, 1.0),
            _ => 1.0,
        }
    }

    pub fn centre(bounds: (f64, f64, f64, f64), viewport: (f64, f64), zoom: f64) -> View {
        let (left, top, w, h) = bounds;
        View { pan_x: (viewport.0 - w * zoom) / 2.0 - left * zoom, pan_y: (viewport.1 - h * zoom) / 2.0 - top * zoom, zoom }
    }

    #[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
    pub enum DetailLevel {
        Full,
        Compact,
        Overview,
    }

    /// Hysteresis between the three node presentations.
    pub fn detail_at(scale: f64, previous: DetailLevel, editing: bool) -> DetailLevel {
        let next = if scale >= 0.30 {
            DetailLevel::Full
        } else if previous == DetailLevel::Full && scale >= 0.28 {
            previous
        } else if scale < 0.16 {
            DetailLevel::Overview
        } else if previous == DetailLevel::Overview && scale < 0.20 {
            previous
        } else {
            DetailLevel::Compact
        };
        if editing && next > previous { previous } else { next }
    }
}

pub fn has_audio_input(kind: &str) -> bool {
    matches!(
        kind,
        "echo" | "distortion" | "mix" | "gain" | "output" | "virtualOutput" | "eq" | "tone" | "highPass" | "lowPass" | "compressor" | "limiter"
            | "ducking" | "pan" | "mono" | "reroute" | "denoiser" | "deEsser" | "plosive" | "reverb" | "voiceColor" | "expander" | "gate"
            | "alignmentDelay" | "channelSwap" | "stereoWidth" | "stereoPan" | "crossfade" | "stereoCombine" | "stereoSplit"
    )
}

pub fn has_audio_output(kind: &str) -> bool {
    matches!(
        kind,
        "echo" | "distortion" | "microphone" | "virtualInput" | "appInput" | "mix" | "gain" | "eq" | "tone" | "highPass" | "lowPass"
            | "compressor" | "limiter" | "ducking" | "pan" | "mono" | "reroute" | "denoiser" | "deEsser" | "plosive" | "reverb" | "voiceColor"
            | "expander" | "gate" | "alignmentDelay" | "channelSwap" | "stereoWidth" | "stereoPan" | "crossfade" | "stereoCombine"
            | "stereoSplit"
    )
}

pub fn has_sidechain_input(kind: &str) -> bool {
    matches!(kind, "ducking" | "expander" | "gate")
}

pub fn audio_input_ports(kind: &str) -> &'static [&'static str] {
    match kind {
        "stereoCombine" => &["left", "right"],
        "crossfade" => &["a", "b"],
        _ if has_audio_input(kind) => &["audio"],
        _ => &[],
    }
}

pub fn audio_output_ports(kind: &str) -> &'static [&'static str] {
    match kind {
        "stereoSplit" => &["left", "right"],
        "reverb" | "echo" => &["audio", "wet"],
        _ if has_audio_output(kind) => &["audio"],
        _ => &[],
    }
}

pub fn port_label(port: &str, node_kind: Option<&str>) -> &'static str {
    match port {
        "left" => "Left",
        "right" => "Right",
        "a" => "A",
        "b" => "B",
        "wet" => {
            if node_kind == Some("echo") {
                "Echo"
            } else {
                "Wet"
            }
        }
        _ => "Audio",
    }
}

pub fn kind_label(kind: &str) -> &str {
    match kind {
        "echo" => "Echo",
        "distortion" => "Distortion",
        "microphone" => "Microphone",
        "virtualInput" => "Virtual input",
        "appInput" => "Running app",
        "expander" => "Expander",
        "gate" => "Gate",
        "alignmentDelay" => "Alignment Delay",
        "mix" => "Mix",
        "gain" => "Gain",
        "output" => "Listening output",
        "virtualOutput" => "Virtual output",
        "control" => "Linked control",
        "eq" => "Equalizer",
        "tone" => "Low / Mid / High",
        "highPass" => "High-pass filter",
        "lowPass" => "Low-pass filter",
        "compressor" => "Compressor",
        "limiter" => "Limiter",
        "ducking" => "Ducking",
        "pan" => "Balance",
        "mono" => "Mono",
        "reroute" => "Reroute",
        "denoiser" => "Denoiser",
        "deEsser" => "De-esser",
        "plosive" => "Plosive remover",
        "reverb" => "Reverb",
        "voiceColor" => "Voice Color",
        "channelSwap" => "Channel Swap",
        "stereoWidth" => "Stereo Width",
        "stereoPan" => "Pan",
        "crossfade" => "Crossfade",
        "stereoCombine" => "Stereo Combine",
        "stereoSplit" => "Stereo Split",
        _ => kind,
    }
}

pub fn default_q_for_type(band_type: &str) -> f64 {
    if matches!(band_type, "lowShelf" | "highShelf" | "highPass" | "lowPass") { BUTTERWORTH_Q } else { 1.0 }
}

pub fn filter_defaults(kind: &str) -> Vec<EqBand> {
    let frequency = if kind == "highPass" { 80.0 } else { 12000.0 };
    vec![EqBand::new(kind, true, frequency, 0.0, BUTTERWORTH_Q, 0.0)]
}

/// A new random identity in the 32-hex-digit form the 0.5 desktop uses.
pub fn new_id() -> String {
    crate::platform::random_hex_id()
}

pub fn new_node(kind: &str, x: f64, y: f64) -> GraphNode {
    let mut node = GraphNode::new(&new_id(), kind, kind_label(kind), x, y);
    node.device = DeviceBinding::default();
    node.denoiser.algorithm = if kind == "denoiser" { "rnnoise".into() } else { "speex".into() };
    node.dynamics_ui_version = i32::from(kind == "compressor");
    node.expander.mode = if kind == "gate" { "gate".into() } else { "expander".into() };
    node.gain_db = if kind == "output" { -24.0 } else { 0.0 };
    node.control_multiplier = if kind == "control" { 1.0 } else { -1.0 };
    node.muted = matches!(kind, "output" | "virtualOutput");
    node.eq_bands = match kind {
        "eq" => Vec::new(),
        "tone" => tone_defaults(),
        "highPass" | "lowPass" => filter_defaults(kind),
        _ => default_eq_bands(),
    };
    node
}

/// Resets sound controls, never identity, routing, placement or assignment.
pub fn reset_node_settings(node: &mut GraphNode) {
    let mut defaults = new_node(&node.kind, node.x, node.y);
    if node.kind == "denoiser" {
        defaults.denoiser.algorithm = node.denoiser.algorithm.clone();
    }
    if node.kind == "expander" && node.expander.mode == "gate" {
        defaults.expander.mode = "gate".into();
    }
    node.gain_db = defaults.gain_db;
    node.muted = defaults.muted;
    node.control_multiplier = defaults.control_multiplier;
    node.mono = defaults.mono;
    node.device.channel = -1;
    node.bypass = defaults.bypass;
    node.balance = defaults.balance;
    node.collapsed = false;
    node.stereo_width = defaults.stereo_width;
    node.pan_position = defaults.pan_position;
    node.crossfade_position = defaults.crossfade_position;
    node.crossfade_curve = defaults.crossfade_curve;
    node.eq_bands = defaults.eq_bands;
    node.compressor = defaults.compressor;
    node.ducking = defaults.ducking;
    node.denoiser = defaults.denoiser;
    node.de_esser = defaults.de_esser;
    node.plosive = defaults.plosive;
    node.reverb = defaults.reverb;
    node.echo = defaults.echo;
    node.distortion = defaults.distortion;
    node.voice_color = defaults.voice_color;
    node.expander = defaults.expander;
    node.alignment_delay = defaults.alignment_delay;
}

/// The desktop's own fixed-mixer migration (`GraphDefaults.FromLegacy`),
/// used for version-1 preview data. It differs from the engine's migration
/// in its virtual-output name and edge order, exactly as in 0.5.
pub fn from_legacy(configuration: &vinemix_model::config::Configuration) -> AudioGraph {
    use vinemix_model::config::GraphEdge;
    let mut graph = AudioGraph::default();
    let sources = ["mic1", "mic2", "app-input"];
    for (i, id) in sources.into_iter().enumerate() {
        let (kind, name) = if i < 2 { ("microphone", format!("Microphone {}", i + 1)) } else { ("virtualInput", "App Audio".to_owned()) };
        let mut node = GraphNode::new(id, kind, &name, 48.0, 60.0 + i as f64 * 330.0);
        node.muted = configuration.sources[i].muted;
        node.device = if i < 2 { configuration.microphones[i].clone() } else { configuration.virtual_input_capture.clone() };
        graph.nodes.push(node);
    }
    for (id, name, y, bus) in [("mix-a", "What I hear", 100.0, &configuration.bus_a), ("mix-b", "What others hear", 550.0, &configuration.bus_b)] {
        let mut node = GraphNode::new(id, "mix", name, 448.0, y);
        node.gain_db = f64::from(bus.gain_db);
        node.muted = bus.muted;
        graph.nodes.push(node);
    }
    for i in 0..2 {
        let id = format!("main-output-{}", i + 1);
        let mut node = GraphNode::new(&id, "output", &format!("Listening output {}", i + 1), 848.0, 60.0 + i as f64 * 330.0);
        node.device = configuration.main_outputs[i].clone();
        graph.nodes.push(node);
        graph.edges.push(GraphEdge::audio(&format!("{id}-in"), "mix-a", &id));
    }
    let mut output = GraphNode::new("virtual-output", "virtualOutput", "My virtual microphone", 848.0, 720.0);
    output.device = configuration.virtual_output_render.clone();
    graph.nodes.push(output);
    graph.edges.push(GraphEdge::audio("virtual-output-in", "mix-b", "virtual-output"));
    for (i, id) in sources.into_iter().enumerate() {
        let source = &configuration.sources[i];
        for (bus, send, mix) in [("a", &source.a, "mix-a"), ("b", &source.b, "mix-b")] {
            let mut edge = GraphEdge::audio(&format!("{id}-{bus}"), id, mix);
            edge.gain_db = f64::from(send.gain_db);
            edge.muted = !send.enabled;
            graph.edges.push(edge);
        }
    }
    graph
}

/// The starter workspace a fresh desktop session creates.
pub fn fresh() -> AudioGraph {
    let mut input = new_node("microphone", 48.0, 200.0);
    input.id = "starter-input".into();
    input.name = "Microphone".into();
    input.auto_assign_device = false;
    let mut high_pass = new_node("highPass", 499.0, 200.0);
    high_pass.id = "starter-high-pass".into();
    let mut denoiser = new_node("denoiser", 950.0, 200.0);
    denoiser.id = "starter-denoiser".into();
    let mut expander = new_node("expander", 1401.0, 200.0);
    expander.id = "starter-expander".into();
    let mut compressor = new_node("compressor", 1924.0, 200.0);
    compressor.id = "starter-compressor".into();
    let mut virtual_output = new_node("virtualOutput", 2447.0, 464.0);
    virtual_output.id = "starter-virtual-output".into();
    virtual_output.name = "Virtual output".into();
    let mut listening = new_node("output", 2447.0, 48.0);
    listening.id = "starter-listening-output".into();
    listening.name = "Listening output".into();
    listening.auto_assign_device = false;
    let edges = vec![
        GraphEdge::audio("starter-high-pass-route", &input.id, &high_pass.id),
        GraphEdge::audio("starter-denoiser-route", &high_pass.id, &denoiser.id),
        GraphEdge::audio("starter-expander-route", &denoiser.id, &expander.id),
        GraphEdge::audio("starter-compressor-route", &expander.id, &compressor.id),
        GraphEdge::audio("starter-listening-route", &compressor.id, &listening.id),
        GraphEdge::audio("starter-virtual-route", &compressor.id, &virtual_output.id),
    ];
    AudioGraph {
        nodes: vec![input, high_pass, denoiser, expander, compressor, listening, virtual_output],
        edges,
        pan_x: 20.0,
        pan_y: 20.0,
        zoom: 0.4,
    }
}

pub fn is_valid_gain(value: f64) -> bool {
    value.is_finite() && (-60.0..=12.0).contains(&value)
}

/// Interactive connection rule: a Level control only links to a node with a
/// Level knob.
pub fn new_connection_error(graph: &AudioGraph, candidate: &GraphEdge) -> String {
    if candidate.kind == "control" {
        if let Some(target) = graph.nodes.iter().find(|n| n.id == candidate.to) {
            if !linked::supports_input(&target.kind) {
                return "Connect Level control to a node with a Level knob.".into();
            }
        }
    }
    connection_error(&graph.nodes, &graph.edges, candidate)
}

pub fn connection_error(nodes: &[GraphNode], edges: &[GraphEdge], candidate: &GraphEdge) -> String {
    if edges.len() >= MAXIMUM_EDGES {
        return "This workspace can contain up to 96 connections.".into();
    }
    let (Some(from), Some(to)) = (nodes.iter().find(|n| n.id == candidate.from), nodes.iter().find(|n| n.id == candidate.to)) else {
        return "Choose two nodes in this workspace.".into();
    };
    if from.id == to.id {
        return "A node cannot connect to itself.".into();
    }
    if edges.iter().any(|e| {
        e.from == from.id && e.to == to.id && e.kind == candidate.kind && e.from_port == candidate.from_port && e.to_port == candidate.to_port
    }) {
        return "Those sockets are already connected.".into();
    }
    if candidate.kind == "control" {
        if from.kind != "control" || to.kind == "control" || candidate.from_port != "audio" || candidate.to_port != "audio" {
            return "Connect a linked control to the small square control socket.".into();
        }
        if edges.iter().any(|e| e.kind == "control" && e.to == to.id) {
            return "This node already has a linked control. Remove that connection first.".into();
        }
        return String::new();
    }
    if candidate.kind == "sidechain" {
        if !audio_output_ports(&from.kind).contains(&candidate.from_port.as_str()) || candidate.to_port != "audio" {
            return "Choose a valid audio output and trigger socket.".into();
        }
        if !has_audio_output(&from.kind) || !has_sidechain_input(&to.kind) {
            return "Connect an audio output to a Ducking, Gate or Expander trigger input.".into();
        }
        if edges.iter().any(|e| e.kind == "sidechain" && e.to == to.id) {
            return "This node already has a detector cable. Remove that connection first.".into();
        }
    } else if candidate.kind != "audio"
        || !audio_output_ports(&from.kind).contains(&candidate.from_port.as_str())
        || !audio_input_ports(&to.kind).contains(&candidate.to_port.as_str())
    {
        return "Connect an audio output on the right to an audio input on the left.".into();
    }
    let mut seen = std::collections::HashSet::new();
    let mut pending = vec![to.id.as_str()];
    while let Some(id) = pending.pop() {
        if id == from.id {
            return "This connection would send audio back into itself. Choose a different destination.".into();
        }
        if !seen.insert(id) {
            continue;
        }
        for edge in edges.iter().filter(|e| (e.kind == "audio" || e.kind == "sidechain") && e.from == id) {
            pending.push(&edge.to);
        }
    }
    String::new()
}

fn in_range(value: f64, low: f64, high: f64) -> bool {
    value.is_finite() && value >= low && value <= high
}

/// The desktop's complete graph validation (`GraphDefaults.Validate`).
pub fn validate(graph: &AudioGraph) -> String {
    if graph.nodes.len() > MAXIMUM_NODES {
        return "This workspace can contain up to 32 nodes.".into();
    }
    if graph.edges.len() > MAXIMUM_EDGES {
        return "This workspace can contain up to 96 connections.".into();
    }
    if !graph.pan_x.is_finite()
        || !graph.pan_y.is_finite()
        || graph.pan_x.abs() > canvas::MAXIMUM_PAN
        || graph.pan_y.abs() > canvas::MAXIMUM_PAN
        || !graph.zoom.is_finite()
        || graph.zoom < canvas::MINIMUM_ZOOM
        || graph.zoom > canvas::MAXIMUM_ZOOM
    {
        return "The canvas view is invalid.".into();
    }
    let mut ids = std::collections::HashSet::new();
    if graph.nodes.iter().any(|n| n.id.trim().is_empty() || !ids.insert(n.id.as_str())) {
        return "Node identities must be unique.".into();
    }
    let mut ids = std::collections::HashSet::new();
    if graph.edges.iter().any(|e| e.id.trim().is_empty() || !ids.insert(e.id.as_str())) {
        return "Connection identities must be unique.".into();
    }
    for node in &graph.nodes {
        if !KINDS.contains(&node.kind.as_str())
            || !node.x.is_finite()
            || !node.y.is_finite()
            || !is_valid_gain(node.gain_db)
            || !(0..=1).contains(&node.dynamics_ui_version)
        {
            return "A node contains an invalid setting.".into();
        }
        if node.control_multiplier != -1.0 && !in_range(node.control_multiplier, 0.0, 2.0) {
            return "A linked multiplier must be between 0 and 200%.".into();
        }
        if !in_range(node.stereo_width, 0.0, 2.0)
            || !in_range(node.pan_position, -1.0, 1.0)
            || !in_range(node.crossfade_position, 0.0, 1.0)
            || !matches!(node.crossfade_curve.as_str(), "equalPower" | "linear")
        {
            return "A mixing node contains an invalid setting.".into();
        }
        let d = &node.denoiser;
        let (c, k, x) = (&node.compressor, &node.ducking, &node.expander);
        let invalid = !in_range(d.suppression_db, -40.0, 0.0)
            || !matches!(d.algorithm.as_str(), "speex" | "rnnoise")
            || !in_range(d.rnnoise_mix, 0.0, 1.0)
            || !in_range(node.de_esser.frequency_hz, 2000.0, 12000.0)
            || !in_range(node.de_esser.threshold_db, -60.0, 0.0)
            || !in_range(node.de_esser.reduction_db, 0.0, 24.0)
            || !in_range(node.plosive.frequency_hz, 60.0, 300.0)
            || !in_range(node.plosive.threshold_db, -60.0, 0.0)
            || !in_range(node.plosive.reduction_db, 0.0, 24.0)
            || !in_range(node.reverb.mix, 0.0, 1.0)
            || !in_range(node.reverb.decay_seconds, 0.1, 5.0)
            || !in_range(node.reverb.damping, 0.0, 1.0)
            || !in_range(node.reverb.pre_delay_ms, 0.0, 100.0)
            || !in_range(node.echo.time_ms, 20.0, 2000.0)
            || !(1..=8).contains(&node.echo.repeats)
            || !in_range(node.echo.mix, 0.0, 1.0)
            || !in_range(node.distortion.drive_db, 0.0, 36.0)
            || !in_range(node.distortion.tone_hz, 500.0, 18000.0)
            || !in_range(node.distortion.mix, 0.0, 1.0)
            || !in_range(node.distortion.output_db, -24.0, 0.0)
            || !in_range(node.voice_color.tone, -1.0, 1.0)
            || !in_range(node.voice_color.ambience, 0.0, 1.0)
            || !matches!(c.mode.as_str(), "compress" | "limit")
            || !matches!(c.detector.as_str(), "internal" | "external")
            || !in_range(c.ceiling_db, -24.0, 0.0)
            || !in_range(c.threshold_db, -60.0, 0.0)
            || !in_range(c.ratio, 1.0, 20.0)
            || !in_range(c.attack_ms, 0.1, 200.0)
            || !in_range(c.release_ms, 5.0, 2000.0)
            || !in_range(c.makeup_db, 0.0, 24.0)
            || !matches!(k.mode.as_str(), "static" | "dynamic")
            || !in_range(k.ratio, 1.0, 20.0)
            || !in_range(k.threshold_db, -60.0, 0.0)
            || !in_range(k.amount_db, 0.0, 60.0)
            || !in_range(k.attack_ms, 0.1, 200.0)
            || !in_range(k.release_ms, 5.0, 2000.0)
            || !matches!(x.mode.as_str(), "expander" | "gate")
            || !in_range(x.threshold_db, -80.0, 0.0)
            || !in_range(x.ratio, 1.0, 20.0)
            || !in_range(x.attack_ms, 0.1, 200.0)
            || !in_range(x.hold_ms, 0.0, 1000.0)
            || !in_range(x.release_ms, 5.0, 2000.0)
            || !in_range(x.range_db, 0.0, 80.0)
            || !in_range(x.hysteresis_db, 0.0, 12.0)
            || node.alignment_delay.samples > 96000;
        if invalid {
            return "An effect contains an invalid setting.".into();
        }
        if node.kind == "gate" && x.mode != "gate" {
            return "A Gate node requires gate processing mode.".into();
        }
        if node.eq_bands.len() > MAXIMUM_EQ_BANDS {
            return "An EQ node supports up to eight bands.".into();
        }
        for band in &node.eq_bands {
            if !matches!(band.band_type.as_str(), "peak" | "lowShelf" | "highShelf" | "highPass" | "lowPass" | "notch")
                || !in_range(band.frequency_hz, 20.0, 20000.0)
                || !in_range(band.gain_db, -24.0, 24.0)
                || !in_range(band.q, 0.1, 20.0)
                || !band.reset_frequency_hz.is_finite()
                || (band.reset_frequency_hz != 0.0 && !in_range(band.reset_frequency_hz, 20.0, 20000.0))
            {
                return "An EQ band contains an invalid filter, frequency, gain or width.".into();
            }
        }
        if matches!(node.kind.as_str(), "highPass" | "lowPass") {
            let bands = &node.eq_bands;
            if bands.len() != 1 || bands[0].band_type != node.kind || !bands[0].enabled || bands[0].gain_db != 0.0 {
                return "High-pass and low-pass nodes require one enabled matching filter with zero band gain. Use bypass to disable the filter."
                    .into();
            }
        }
        if node.kind == "tone" {
            let expected = tone_defaults();
            if node.eq_bands.len() != expected.len() {
                return "Low / Mid / High requires exactly three tone bands.".into();
            }
            for (actual, fixed) in node.eq_bands.iter().zip(&expected) {
                if !(-12.0..=12.0).contains(&actual.gain_db) {
                    return "Low / Mid / High levels must be between -12 and +12 dB.".into();
                }
                if actual.band_type != fixed.band_type
                    || !actual.enabled
                    || actual.frequency_hz != fixed.frequency_hz
                    || (actual.q - fixed.q).abs() > 1e-7
                {
                    return "Low / Mid / High uses fixed filters; use EQ for custom bands.".into();
                }
            }
        }
    }
    let mut checking: Vec<GraphEdge> = Vec::with_capacity(graph.edges.len());
    for edge in &graph.edges {
        if !is_valid_gain(edge.gain_db) {
            return "A connection contains an invalid volume.".into();
        }
        let error = connection_error(&graph.nodes, &checking, edge);
        if !error.is_empty() {
            return error;
        }
        checking.push(edge.clone());
    }
    String::new()
}

/// Node categories for the creation menu and header colours.
pub mod catalog {
    use super::kind_label;

    #[derive(Clone, Copy, Debug, PartialEq, Eq)]
    pub enum Header {
        Input,
        Output,
        Processor,
        Utility,
        Controller,
    }

    #[derive(Debug)]
    pub struct Category {
        pub title: &'static str,
        pub header: Header,
        pub kinds: &'static [&'static str],
    }

    impl Category {
        pub fn description(&self) -> &'static str {
            match self.title {
                "Inputs" => "Bring sound into the workspace from a microphone, a virtual cable or a running app.",
                "Outputs" => "Send the finished mix to a listening device or to a virtual microphone for another app.",
                "Effects" => "Shape tone, dynamics and noise, or add reverb, echoes and distortion.",
                "Mixing" => "Combine, split and blend audio paths, or adjust their stereo position and width.",
                "Controls" => "Scale several node levels together using a linked multiplier.",
                _ => "",
            }
        }
    }

    pub const CATEGORIES: [Category; 5] = [
        Category { title: "Inputs", header: Header::Input, kinds: &["microphone", "virtualInput", "appInput"] },
        Category { title: "Outputs", header: Header::Output, kinds: &["output", "virtualOutput"] },
        Category {
            title: "Effects",
            header: Header::Processor,
            kinds: &[
                "tone", "voiceColor", "highPass", "lowPass", "limiter", "denoiser", "reverb", "echo", "distortion", "eq", "compressor", "ducking",
                "expander", "gate", "alignmentDelay", "plosive", "deEsser",
            ],
        },
        Category {
            title: "Mixing",
            header: Header::Utility,
            kinds: &["mix", "pan", "mono", "channelSwap", "stereoWidth", "stereoPan", "crossfade", "stereoCombine", "stereoSplit"],
        },
        Category { title: "Controls", header: Header::Controller, kinds: &["control"] },
    ];

    pub fn category_for(kind: &str) -> &'static Category {
        let key = if matches!(kind, "gain" | "reroute") { "mix" } else { kind };
        CATEGORIES.iter().find(|c| c.kinds.contains(&key)).unwrap_or(&CATEGORIES[2])
    }

    pub fn can_create(kind: &str) -> bool {
        CATEGORIES.iter().any(|c| c.kinds.contains(&kind))
    }

    pub fn has_level_control(kind: &str) -> bool {
        matches!(kind, "microphone" | "virtualInput" | "appInput" | "output" | "virtualOutput" | "mix" | "gain" | "reroute")
    }

    pub fn description(kind: &str) -> &'static str {
        match kind {
            "microphone" => "Bring audio from a Windows recording device into the workspace, including a physical, virtual or streamed microphone.",
            "virtualInput" => "Receive the combined audio sent to the VB-CABLE virtual cable by other apps.",
            "appInput" => "Capture a running app's audio as a separate source. Its original playback continues.",
            "output" => "Send audio to headphones, speakers or a streamed headset such as Virtual Desktop. Starts muted so you can choose a device and level first.",
            "virtualOutput" => "Send audio to VAC Lite so another app can use your mix as its microphone. Starts muted.",
            "eq" => "Shape specific parts of the sound with up to eight adjustable frequency bands.",
            "tone" => "Adjust bass, midrange and treble with three simple tone controls.",
            "voiceColor" => "Shape a voice from warm to bright and from natural to a fuller broadcast character.",
            "highPass" => "Remove low-frequency rumble below a chosen cutoff while keeping higher sounds.",
            "lowPass" => "Remove high-frequency hiss or brightness above a chosen cutoff while keeping lower sounds.",
            "compressor" => "Reduce louder passages of its own audio with threshold, ratio, attack, release and make-up gain.",
            "limiter" => "Limit stereo sample peaks to a ceiling, with no lookahead.",
            "ducking" => "Static ducking lowers by a fixed amount; dynamic ducking follows trigger loudness above threshold. Trigger audio is detector only.",
            "expander" => "Gradually reduce quiet audio below the threshold. An optional Trigger input can detect a separate signal.",
            "gate" => "Reduce quiet audio with hold, range and hysteresis. An optional Trigger input can detect a separate signal.",
            "alignmentDelay" => "Delay one audio path by a precise number of milliseconds or samples to align it with another.",
            "denoiser" => "Reduce background noise in speech with RNNoise or SpeexDSP. Adds a short processing delay, including when bypassed.",
            "plosive" => "Reduce low-frequency P and B thumps or microphone pops when they exceed a threshold.",
            "deEsser" => "Reduce harsh S and SH sounds when their high-frequency level exceeds a threshold.",
            "reverb" => "Add a room-like tail. Audio blends dry sound and reverb; the extra Wet output carries reverb alone, independent of Mix.",
            "echo" => "Add 1–8 fading echoes. Time sets spacing and Mix blends original sound with echoes on Audio. The separate Echo output carries only echoes, independent of Mix.",
            "distortion" => "Add warmth or stronger distortion with Drive, Tone, Mix and Output. Adds 0.67 ms delay, including bypass.",
            "mix" => "Combine several incoming audio paths and adjust their shared level, or use one input to control a single branch.",
            "pan" => "Adjust left/right stereo balance by reducing the opposite channel. Centre preserves the original stereo sound.",
            "mono" => "Combine left and right equally and send the same centred sound to both channels.",
            "channelSwap" => "Exchange the left and right channels without changing their levels.",
            "stereoWidth" => "Adjust stereo width: 0% is mono, 100% keeps the original sound, and 200% widens existing stereo detail.",
            "stereoPan" => "Position a mono signal from left to right using equal-power panning. Stereo input is first combined to mono.",
            "crossfade" => "Blend stereo inputs A and B with one control. Equal power suits different sources; Linear suits matching signals. Bypass plays A only.",
            "stereoCombine" => "Place the Left input in the left channel and the Right input in the right channel of one stereo output. Each input is combined to mono; an empty side stays silent.",
            "stereoSplit" => "Extract left and right from stereo audio as separate mono outputs, ready for independent processing or Stereo Combine.",
            "control" => "Scale connected node levels together. 100% is unchanged; 0% is silent.",
            "gain" => "Adjust the level of one audio path. Kept for existing workspaces; use Mix when adding a node.",
            "reroute" => "Pass audio through a named point in an existing route. Kept for existing workspaces.",
            _ => "",
        }
    }

    /// Case-insensitive substring search over title, kind, category and description.
    pub fn matches_search(kind: &str, query: &str) -> bool {
        let extra = match kind {
            "ducking" => " sidechain music voice trigger",
            "control" => " multiplier volume",
            "echo" => " delay repeats feedback",
            "distortion" => " saturation overdrive warmth",
            _ => "",
        };
        let text = format!("{} {} {} {}{}", kind_label(kind), kind, category_for(kind).title, description(kind), extra);
        crate::text::contains_ignore_case(&text, query)
    }
}

/// Shared-volume presentation policy (`LinkedLevelControl`).
pub mod linked {
    use super::*;

    pub fn supports_input(kind: &str) -> bool {
        matches!(kind, "microphone" | "virtualInput" | "appInput" | "output" | "virtualOutput" | "mix" | "gain" | "reroute")
    }

    pub fn has_legacy_connection(graph: &AudioGraph, node: &GraphNode) -> bool {
        !supports_input(&node.kind) && node.kind != "control" && graph.edges.iter().any(|e| e.kind == "control" && e.to == node.id)
    }

    pub fn show_input(graph: &AudioGraph, node: &GraphNode) -> bool {
        supports_input(&node.kind) || has_legacy_connection(graph, node)
    }

    pub fn percent(node: &GraphNode) -> f64 {
        if node.control_multiplier >= 0.0 {
            node.control_multiplier * 100.0
        } else {
            100.0 * 10f64.powf(node.gain_db / 20.0)
        }
    }

    pub fn has_legacy_value(node: &GraphNode) -> bool {
        node.control_multiplier < 0.0 && percent(node) > 200.0
    }

    pub fn set_percent(node: &mut GraphNode, percent: f64) -> Result<(), String> {
        if node.kind != "control" || !percent.is_finite() || !(0.0..=200.0).contains(&percent) {
            return Err("A linked multiplier must be between 0 and 200%.".into());
        }
        node.control_multiplier = percent / 100.0;
        node.gain_db = 0.0;
        node.muted = false;
        Ok(())
    }
}

/// Identity-based defaults for the two supported virtual transports.
pub mod bindings {
    use super::*;

    #[derive(Clone, Debug, PartialEq)]
    pub struct Assignment {
        pub node_id: String,
        pub kind: String,
        pub id: String,
        pub stable_id: String,
        pub name: String,
    }

    impl Assignment {
        pub fn attempt_key(&self) -> String {
            format!("{}\0{}\0{}\0{}", self.node_id, self.kind, self.id, self.stable_id)
        }

        pub fn binding(&self) -> DeviceBinding {
            DeviceBinding { id: self.id.clone(), stable_id: self.stable_id.clone(), name: self.name.clone(), channel: -1 }
        }
    }

    pub fn is_virtual(kind: &str) -> bool {
        matches!(kind, "virtualInput" | "virtualOutput")
    }

    pub fn is_assigned(binding: &DeviceBinding) -> bool {
        binding.assigned()
    }

    pub fn matches(kind: &str, endpoint: &Endpoint) -> bool {
        match kind {
            "virtualInput" => endpoint.direction == "capture" && endpoint.transport == "vbCable",
            "virtualOutput" => endpoint.direction == "render" && endpoint.transport == "vacLite",
            _ => false,
        }
    }

    pub fn same(binding: &DeviceBinding, endpoint: &Endpoint) -> bool {
        (!binding.id.is_empty() && binding.id == endpoint.id) || (!binding.stable_id.is_empty() && binding.stable_id == endpoint.stable_id)
    }

    pub fn find_automatic_assignments(graph: &AudioGraph, endpoints: &[Endpoint]) -> Vec<Assignment> {
        let mut assignments = Vec::new();
        for node in &graph.nodes {
            if !node.auto_assign_device || !is_virtual(&node.kind) || is_assigned(&node.device) {
                continue;
            }
            let candidates: Vec<&Endpoint> = endpoints
                .iter()
                .filter(|e| matches(&node.kind, e) && e.state == "Active" && (!e.id.is_empty() || !e.stable_id.is_empty()))
                .take(2)
                .collect();
            if let [endpoint] = candidates.as_slice() {
                assignments.push(Assignment {
                    node_id: node.id.clone(),
                    kind: node.kind.clone(),
                    id: endpoint.id.clone(),
                    stable_id: endpoint.stable_id.clone(),
                    name: endpoint.name.clone(),
                });
            }
        }
        assignments
    }

    pub fn apply(graph: &mut AudioGraph, assignments: &[Assignment]) -> usize {
        let mut changed = 0;
        for assignment in assignments {
            let Some(node) = graph.nodes.iter_mut().find(|n| n.id == assignment.node_id && n.kind == assignment.kind) else { continue };
            if !node.auto_assign_device || is_assigned(&node.device) {
                continue;
            }
            node.device = assignment.binding();
            changed += 1;
        }
        changed
    }
}

/// Stable status reasons, with the exact legacy diagnostics as fallback.
pub mod reasons {
    use super::*;

    pub fn for_path(path: Option<&PathState>) -> &str {
        let Some(path) = path else { return "" };
        if !path.reason.is_empty() {
            return &path.reason;
        }
        match path.message.as_str() {
            "Device disabled in Windows Sound settings" => "device_disabled",
            "Several instances are running; select the app again" => "app_instance_ambiguous",
            "Windows denied app audio capture" => "access_denied",
            "Windows Audio service unavailable" => "audio_service_unavailable",
            _ => "",
        }
    }

    pub fn is_revision_conflict(error_code: &str, error: &str) -> bool {
        error_code == "revision_conflict" || (error_code.is_empty() && error == "Configuration revision conflict. Refresh and retry your change.")
    }

    pub fn detail(node: &GraphNode, path: Option<&PathState>) -> String {
        if let Some(path) = path {
            if matches!(node.kind.as_str(), "output" | "virtualOutput")
                && path.state == "AccessDenied"
                && path.reason.is_empty()
                && path.message == "Microphone access blocked; check Windows microphone privacy settings"
            {
                return "Output access blocked; check Windows sound device settings".into();
            }
            return path.message.clone();
        }
        String::new()
    }

    pub fn path_display(state: &str) -> &'static str {
        match state {
            "Unassigned" => "Not assigned",
            "Resolving" | "Opening" | "Priming" => "Connecting",
            "Running" => "Connected",
            "Waiting" => "Waiting for device",
            "Busy" => "Device busy",
            "AccessDenied" => "Microphone access blocked",
            "ServiceUnavailable" => "Audio service unavailable",
            "Recovering" => "Reconnecting",
            "Unsupported" => "App capture unavailable",
            "Error" => "Audio setup needs attention",
            _ => "Connecting",
        }
    }
}

/// Searches the current graph without changing its nodes or view.
pub mod search {
    use super::*;

    #[derive(Clone, Debug, PartialEq)]
    pub struct Result {
        pub id: String,
        pub name: String,
        pub detail: String,
    }

    fn assigned_name(node: &GraphNode) -> &str {
        match node.kind.as_str() {
            "appInput" if node.app.process_id != 0 || !node.app.executable_path.is_empty() => &node.app.name,
            "microphone" | "virtualInput" | "output" | "virtualOutput" if node.device.assigned() => &node.device.name,
            _ => "",
        }
    }

    pub fn matches(graph: &AudioGraph, query: &str) -> Vec<Result> {
        let terms: Vec<&str> = query.split_whitespace().collect();
        let mut results = Vec::new();
        for node in &graph.nodes {
            let kind = kind_label(&node.kind);
            let name = if node.name.trim().is_empty() { kind } else { node.name.as_str() };
            let source = assigned_name(node).trim();
            let contains = crate::text::contains_ignore_case;
            if !terms.iter().all(|t| contains(name, t) || contains(kind, t) || contains(source, t)) {
                continue;
            }
            let detail = if !source.is_empty()
                && !crate::text::eq_ignore_case(source, name.trim())
                && !crate::text::eq_ignore_case(source, kind)
            {
                format!("{kind} · {source}")
            } else {
                kind.to_owned()
            };
            results.push(Result { id: node.id.clone(), name: name.to_owned(), detail });
        }
        results
    }
}

/// `AudioApplication.Label`.
pub fn application_label(application: &ApplicationInfo) -> String {
    if application.process_id == 0 {
        if application.name.is_empty() { "Choose a running app".into() } else { application.name.clone() }
    } else {
        let name = if application.name.is_empty() { "Application" } else { application.name.as_str() };
        format!("{name} · PID {}", application.process_id)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fresh_graph_is_valid_and_matches_engine_rules() {
        let graph = fresh();
        assert_eq!(validate(&graph), "");
        assert_eq!(graph.nodes.len(), 7);
        let configuration = vinemix_model::config::Configuration { graph: graph.clone(), version: 2, ..Default::default() };
        assert!(vinemix_model::validate::validate_configuration(&configuration).is_ok());
    }

    #[test]
    fn connection_rules() {
        let mut graph = fresh();
        let cycle = GraphEdge::audio("x", "starter-compressor", "starter-high-pass");
        assert_eq!(
            connection_error(&graph.nodes, &graph.edges, &cycle),
            "This connection would send audio back into itself. Choose a different destination."
        );
        let control = new_node("control", 0.0, 0.0);
        let id = control.id.clone();
        graph.nodes.push(control);
        let mut link = GraphEdge::audio("c", &id, "starter-denoiser");
        link.kind = "control".into();
        assert_eq!(new_connection_error(&graph, &link), "Connect Level control to a node with a Level knob.");
        link.to = "starter-input".into();
        assert_eq!(new_connection_error(&graph, &link), "");
    }

    #[test]
    fn detail_hysteresis() {
        use canvas::{detail_at, DetailLevel::*};
        assert_eq!(detail_at(0.29, Full, false), Full);
        assert_eq!(detail_at(0.29, Compact, false), Compact);
        assert_eq!(detail_at(0.18, Overview, false), Overview);
        assert_eq!(detail_at(0.1, Full, true), Full);
    }

    #[test]
    fn search_terms_all_match() {
        let graph = fresh();
        let found = search::matches(&graph, "listen out");
        assert_eq!(found.len(), 1);
        assert_eq!(found[0].id, "starter-listening-output");
        assert!(catalog::matches_search("ducking", "SIDECHAIN"));
    }
}
