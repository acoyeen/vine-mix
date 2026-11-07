//! Graph editing operations that must stay replayable over a changed
//! engine revision: cable removal/rewire, node insertion into a cable,
//! detach, copy/paste (`GraphConnectionEditing`, `GraphClipboard`) and
//! node alignment (`GraphNodeAlignment`).

use std::collections::{HashMap, HashSet};

use serde_json::{json, Value};
use vinemix_model::config::{self, AudioGraph, GraphEdge, GraphNode};

use crate::frames::{self, Frame};
use crate::graph;

fn same_edge(a: &GraphEdge, b: &GraphEdge) -> bool {
    a.id == b.id
        && a.from == b.from
        && a.to == b.to
        && a.kind == b.kind
        && a.from_port == b.from_port
        && a.to_port == b.to_port
        && a.gain_db == b.gain_db
        && a.muted == b.muted
}

fn with_edges(graph: &AudioGraph, edges: Vec<GraphEdge>) -> AudioGraph {
    AudioGraph { nodes: graph.nodes.clone(), edges, pan_x: graph.pan_x, pan_y: graph.pan_y, zoom: graph.zoom }
}

/// Removed and added cables.
#[derive(Clone, Debug, PartialEq)]
pub struct EdgePlan {
    pub removed: Vec<GraphEdge>,
    pub added: Vec<GraphEdge>,
}

fn validate_edges(graph: &AudioGraph, plan: &EdgePlan) -> Result<(), String> {
    let removed: HashSet<&str> = plan.removed.iter().map(|e| e.id.as_str()).collect();
    if plan.removed.iter().any(|old| !graph.edges.iter().any(|e| same_edge(e, old)))
        || plan.added.iter().any(|a| graph.edges.iter().any(|e| e.id == a.id && !removed.contains(e.id.as_str())))
    {
        return Err("A cable changed during this gesture. Its current connections were kept.".into());
    }
    if plan.added.iter().any(|added| {
        added.kind == "control"
            && !plan.removed.iter().any(|old| {
                old.kind == "control" && old.from == added.from && old.to == added.to && old.from_port == added.from_port && old.to_port == added.to_port
            })
            && graph.nodes.iter().find(|n| n.id == added.to).is_some_and(|t| !graph::linked::supports_input(&t.kind))
    }) {
        return Err("Connect Level control to a node with a Level knob.".into());
    }
    let edges: Vec<GraphEdge> =
        graph.edges.iter().filter(|e| !removed.contains(e.id.as_str())).cloned().chain(plan.added.iter().cloned()).collect();
    let error = graph::validate(&with_edges(graph, edges));
    if error.is_empty() { Ok(()) } else { Err(error) }
}

pub fn prepare_removal(graph: &AudioGraph, originals: &[GraphEdge]) -> Result<EdgePlan, String> {
    let mut removed: Vec<GraphEdge> = Vec::new();
    for edge in originals {
        if !removed.iter().any(|e| e.id == edge.id) {
            removed.push(edge.clone());
        }
    }
    if removed.is_empty() {
        return Err("No cables were selected.".into());
    }
    let plan = EdgePlan { removed, added: Vec::new() };
    validate_edges(graph, &plan)?;
    Ok(plan)
}

pub fn prepare_rewire(graph: &AudioGraph, original: &GraphEdge, destination: &str, destination_port: &str) -> Result<EdgePlan, String> {
    let mut replacement = original.clone();
    replacement.to = destination.into();
    replacement.to_port = destination_port.into();
    if replacement.kind == "control"
        && (replacement.to != original.to || replacement.to_port != original.to_port)
        && graph.nodes.iter().find(|n| n.id == destination).is_some_and(|t| !graph::linked::supports_input(&t.kind))
    {
        return Err("Connect Level control to a node with a Level knob.".into());
    }
    let plan = EdgePlan { removed: vec![original.clone()], added: vec![replacement] };
    validate_edges(graph, &plan)?;
    Ok(plan)
}

pub fn apply_edge_plan(graph: &mut AudioGraph, plan: &EdgePlan) -> Result<(), String> {
    let already = plan.removed.iter().all(|old| {
        graph.edges.iter().all(|e| e.id != old.id) || plan.added.iter().any(|a| a.id == old.id && graph.edges.iter().any(|e| same_edge(e, a)))
    }) && plan.added.iter().all(|a| graph.edges.iter().any(|e| same_edge(e, a)));
    if already {
        return Ok(());
    }
    validate_edges(graph, plan)?;
    let removed: HashSet<String> = plan.removed.iter().map(|e| e.id.clone()).collect();
    graph.edges.retain(|e| !removed.contains(&e.id));
    graph.edges.extend(plan.added.iter().cloned());
    Ok(())
}

/// Insertion or detach of one node.
#[derive(Clone, Debug, PartialEq)]
pub struct NodePlan {
    pub node_id: String,
    pub node_kind: String,
    pub incident: Vec<GraphEdge>,
    pub removed: Vec<GraphEdge>,
    pub added: Vec<GraphEdge>,
}

pub fn is_insertable(node: &GraphNode) -> bool {
    graph::audio_input_ports(&node.kind) == ["audio"] && graph::audio_output_ports(&node.kind).contains(&"audio")
}

pub fn insertion_error(graph: &AudioGraph, node_id: &str, edge_id: &str) -> String {
    let node = graph.nodes.iter().find(|n| n.id == node_id);
    let edge = graph.edges.iter().find(|e| e.id == edge_id);
    let Some(node) = node.filter(|n| is_insertable(n)) else {
        return "Only processing, mix and reroute nodes can be inserted into an audio cable.".into();
    };
    if graph.edges.iter().any(|e| (e.from == node_id || e.to == node_id) && !(e.to == node_id && e.kind == "sidechain")) {
        return "Detach this node's existing audio or linked-control connections before inserting it into a cable.".into();
    }
    let _ = node;
    let Some(edge) = edge.filter(|e| e.kind == "audio") else { return "Drop the node onto an audio cable.".into() };
    if graph.edges.len() >= graph::MAXIMUM_EDGES {
        return "Inserting a node needs one spare connection slot (96 maximum).".into();
    }
    let mut edges: Vec<GraphEdge> = graph.edges.iter().filter(|e| e.id != edge_id).cloned().collect();
    let incoming = GraphEdge {
        from: edge.from.clone(),
        from_port: edge.from_port.clone(),
        to: node_id.into(),
        gain_db: edge.gain_db,
        muted: edge.muted,
        ..GraphEdge::default()
    };
    let error = graph::connection_error(&graph.nodes, &edges, &incoming);
    if !error.is_empty() {
        return error;
    }
    edges.push(incoming);
    let outgoing = GraphEdge { from: node_id.into(), to: edge.to.clone(), to_port: edge.to_port.clone(), ..GraphEdge::default() };
    graph::connection_error(&graph.nodes, &edges, &outgoing)
}

pub fn prepare_insertion(graph: &AudioGraph, node_id: &str, edge_id: &str) -> Result<NodePlan, String> {
    let error = insertion_error(graph, node_id, edge_id);
    if !error.is_empty() {
        return Err(error);
    }
    let node = graph.nodes.iter().find(|n| n.id == node_id).ok_or("Drop the node onto an audio cable.")?;
    let edge = graph.edges.iter().find(|e| e.id == edge_id).ok_or("Drop the node onto an audio cable.")?;
    let plan = NodePlan {
        node_id: node_id.into(),
        node_kind: node.kind.clone(),
        incident: graph.edges.iter().filter(|e| e.from == node_id || e.to == node_id).cloned().collect(),
        removed: vec![edge.clone()],
        added: vec![
            GraphEdge {
                id: graph::new_id(),
                from: edge.from.clone(),
                from_port: edge.from_port.clone(),
                to: node_id.into(),
                gain_db: edge.gain_db,
                muted: edge.muted,
                ..GraphEdge::default()
            },
            GraphEdge { id: graph::new_id(), from: node_id.into(), to: edge.to.clone(), to_port: edge.to_port.clone(), ..GraphEdge::default() },
        ],
    };
    validate_plan(graph, &plan)?;
    Ok(plan)
}

pub fn prepare_detach(graph: &AudioGraph, node_id: &str) -> Result<NodePlan, String> {
    let Some(node) = graph.nodes.iter().find(|n| n.id == node_id).filter(|n| is_insertable(n)) else {
        return Err("Detach applies to a processing, mix or reroute node between audio connections.".into());
    };
    let incident: Vec<GraphEdge> = graph.edges.iter().filter(|e| e.from == node_id || e.to == node_id).cloned().collect();
    if incident.iter().any(|e| e.kind != "audio") {
        return Err("This node has a linked control or detector cable. Remove those connections before detaching its audio path; no cables were changed.".into());
    }
    let inputs: Vec<&GraphEdge> = incident.iter().filter(|e| e.to == node_id).collect();
    let outputs: Vec<&GraphEdge> = incident.iter().filter(|e| e.from == node_id).collect();
    if outputs.iter().any(|e| e.from_port != "audio") {
        return Err("This node has a separate output connected. Remove those cables before detaching its main audio path; no cables were changed.".into());
    }
    if inputs.len() != 1 || outputs.is_empty() {
        return Err("Detach needs exactly one incoming audio cable and at least one outgoing cable; these connections are unchanged.".into());
    }
    let mut added = Vec::new();
    for output in &outputs {
        let gain = inputs[0].gain_db + output.gain_db;
        if !graph::is_valid_gain(gain) {
            return Err("The combined path level cannot fit in one cable's range. Detach left the connections unchanged.".into());
        }
        added.push(GraphEdge {
            id: graph::new_id(),
            from: inputs[0].from.clone(),
            to: output.to.clone(),
            from_port: inputs[0].from_port.clone(),
            to_port: output.to_port.clone(),
            gain_db: gain,
            muted: inputs[0].muted || output.muted,
            kind: "audio".into(),
        });
    }
    let plan = NodePlan { node_id: node.id.clone(), node_kind: node.kind.clone(), incident: incident.clone(), removed: incident, added };
    validate_plan(graph, &plan)?;
    Ok(plan)
}

fn validate_plan(graph: &AudioGraph, plan: &NodePlan) -> Result<(), String> {
    if !graph.nodes.iter().any(|n| n.id == plan.node_id && n.kind == plan.node_kind) {
        return Err("The node changed while editing; its connections were left unchanged.".into());
    }
    let incident: Vec<&GraphEdge> = graph.edges.iter().filter(|e| e.from == plan.node_id || e.to == plan.node_id).collect();
    if incident.len() != plan.incident.len()
        || incident.iter().any(|e| !plan.incident.iter().any(|x| same_edge(e, x)))
        || plan.removed.iter().any(|old| !graph.edges.iter().any(|e| same_edge(e, old)))
        || plan.added.iter().any(|a| graph.edges.iter().any(|e| e.id == a.id))
    {
        return Err("The cables changed while editing; no connections were replaced.".into());
    }
    let removed: HashSet<&str> = plan.removed.iter().map(|e| e.id.as_str()).collect();
    let edges: Vec<GraphEdge> =
        graph.edges.iter().filter(|e| !removed.contains(e.id.as_str())).cloned().chain(plan.added.iter().cloned()).collect();
    let error = graph::validate(&with_edges(graph, edges));
    if error.is_empty() { Ok(()) } else { Err(error) }
}

pub fn apply_node_plan(graph: &mut AudioGraph, plan: &NodePlan) -> Result<(), String> {
    if plan.removed.iter().all(|old| graph.edges.iter().all(|e| e.id != old.id)) && plan.added.iter().all(|a| graph.edges.iter().any(|e| same_edge(e, a))) {
        return Ok(());
    }
    validate_plan(graph, plan)?;
    let removed: HashSet<String> = plan.removed.iter().map(|e| e.id.clone()).collect();
    graph.edges.retain(|e| !removed.contains(&e.id));
    graph.edges.extend(plan.added.iter().cloned());
    Ok(())
}

// ----- geometry helpers ---------------------------------------------------

pub type Point = (f64, f64);

fn segment_distance_squared(from: Point, to: Point, point: Point) -> f64 {
    let d = (to.0 - from.0, to.1 - from.1);
    let length = d.0 * d.0 + d.1 * d.1;
    let fraction = if length > 0.0 { (((point.0 - from.0) * d.0 + (point.1 - from.1) * d.1) / length).clamp(0.0, 1.0) } else { 0.0 };
    let p = (from.0 + d.0 * fraction, from.1 + d.1 * fraction);
    (point.0 - p.0).powi(2) + (point.1 - p.1).powi(2)
}

pub fn distance_to_polyline(points: &[Point], point: Point) -> f64 {
    points.windows(2).map(|w| segment_distance_squared(w[0], w[1], point)).fold(f64::INFINITY, f64::min).sqrt()
}

pub fn polyline_intersects_stroke(points: &[Point], from: Point, to: Point, tolerance: f64) -> bool {
    let cross = |a: Point, b: Point| a.0 * b.1 - a.1 * b.0;
    let sub = |a: Point, b: Point| (a.0 - b.0, a.1 - b.1);
    let limit = tolerance * tolerance;
    for w in points.windows(2) {
        let (a, b) = (w[0], w[1]);
        let first = cross(sub(b, a), sub(from, a));
        let second = cross(sub(b, a), sub(to, a));
        let third = cross(sub(to, from), sub(a, from));
        let fourth = cross(sub(to, from), sub(b, from));
        if ((first < 0.0 && second > 0.0) || (first > 0.0 && second < 0.0)) && ((third < 0.0 && fourth > 0.0) || (third > 0.0 && fourth < 0.0)) {
            return true;
        }
        let nearest = segment_distance_squared(a, b, from)
            .min(segment_distance_squared(a, b, to))
            .min(segment_distance_squared(from, to, a))
            .min(segment_distance_squared(from, to, b));
        if nearest <= limit {
            return true;
        }
    }
    false
}

/// Bezier handles: horizontal tangents scaled with the socket gap.
pub fn cable_controls(from: Point, to: Point) -> (Point, Point) {
    let reach = ((to.0 - from.0).powi(2) + (to.1 - from.1).powi(2)).sqrt() * 0.25;
    ((from.0 + reach, from.1), (to.0 - reach, to.1))
}

pub fn cable_points(from: Point, to: Point) -> Vec<Point> {
    let (a, b) = cable_controls(from, to);
    let length = |p: Point, q: Point| ((q.0 - p.0).powi(2) + (q.1 - p.1).powi(2)).sqrt();
    let steps = ((length(from, a) + length(a, b) + length(b, to)) / 6.0).ceil().clamp(24.0, 512.0) as usize;
    (0..=steps)
        .map(|i| {
            let t = i as f64 / steps as f64;
            let u = 1.0 - t;
            (
                u * u * u * from.0 + 3.0 * u * u * t * a.0 + 3.0 * u * t * t * b.0 + t * t * t * to.0,
                u * u * u * from.1 + 3.0 * u * u * t * a.1 + 3.0 * u * t * t * b.1 + t * t * t * to.1,
            )
        })
        .collect()
}

// ----- clipboard -----------------------------------------------------------

pub const CLIPBOARD_FORMAT: &str = "VineMix.NodeGroup.v1";
pub const CLIPBOARD_MAXIMUM_BYTES: usize = 256 * 1024;

#[derive(Clone, Debug, Default, PartialEq)]
pub struct Paste {
    pub nodes: Vec<GraphNode>,
    pub edges: Vec<GraphEdge>,
    pub frames: Vec<Frame>,
}

pub fn serialize_group(graph: &AudioGraph, frames: &[Frame], ids: &HashSet<String>) -> String {
    let nodes: Vec<&GraphNode> = graph.nodes.iter().filter(|n| ids.contains(&n.id)).collect();
    if nodes.is_empty() {
        return String::new();
    }
    let included: HashSet<&str> = nodes.iter().map(|n| n.id.as_str()).collect();
    let edges: Vec<Value> =
        graph.edges.iter().filter(|e| included.contains(e.from.as_str()) && included.contains(e.to.as_str())).map(config::edge_json).collect();
    let copied: Vec<&Frame> = frames.iter().filter(|f| !f.node_ids.is_empty() && f.node_ids.iter().all(|id| included.contains(id.as_str()))).collect();
    let mut group = json!({
        "format": CLIPBOARD_FORMAT,
        "version": 1,
        "nodes": nodes.iter().map(|n| config::node_json(n)).collect::<Vec<_>>(),
        "edges": edges,
    });
    if !copied.is_empty() {
        group["frames"] = serde_json::to_value(&copied).unwrap_or(Value::Null);
    }
    vinemix_model::json::dump(&group)
}

fn exact_properties(value: &Value, names: &[&str]) -> bool {
    let Some(object) = value.as_object() else { return false };
    object.len() == names.len() && names.iter().all(|n| object.contains_key(*n))
}

fn with_optional<'a>(value: &Value, required: &[&'a str], optional: &[&'a str]) -> Vec<&'a str> {
    let mut fields = required.to_vec();
    for name in optional {
        if value.get(*name).is_some() {
            fields.push(name);
        }
    }
    fields
}

/// The strict clipboard shape (`GraphClipboard.ValidShape`).
fn valid_shape(root: &Value) -> bool {
    let root_fields = with_optional(root, &["format", "version", "nodes", "edges"], &["frames"]);
    if !exact_properties(root, &root_fields) {
        return false;
    }
    if let Some(frames) = root.get("frames") {
        let Some(frames) = frames.as_array().filter(|f| f.len() <= frames::MAXIMUM_FRAMES) else { return false };
        for frame in frames {
            if !exact_properties(frame, &["id", "name", "colorKey", "nodeIds"]) {
                return false;
            }
            match frame["nodeIds"].as_array() {
                Some(members) if (1..=32).contains(&members.len()) && members.iter().all(Value::is_string) => {}
                _ => return false,
            }
        }
    }
    let (Some(nodes), Some(edges)) = (root["nodes"].as_array(), root["edges"].as_array()) else { return false };
    if nodes.len() > 32 || edges.len() > graph::MAXIMUM_EDGES {
        return false;
    }
    for node in nodes {
        if !node.is_object() {
            return false;
        }
        let fields = with_optional(
            node,
            &["id", "kind", "name", "x", "y", "gainDb", "muted", "device", "app", "bypass", "collapsed", "balance", "eqBands", "compressor"],
            &[
                "controlMultiplier", "mono", "autoAssignDevice", "denoiser", "deEsser", "plosive", "reverb", "echo", "distortion", "voiceColor",
                "expander", "alignmentDelay", "dynamicsUiVersion", "ducking", "stereoWidth", "panPosition", "crossfadePosition", "crossfadeCurve",
            ],
        );
        if !exact_properties(node, &fields)
            || !exact_properties(&node["device"], &["id", "stableId", "name", "channel"])
            || !exact_properties(&node["app"], &["processId", "executablePath", "name"])
        {
            return false;
        }
        let compressor = &node["compressor"];
        if !compressor.is_object()
            || !exact_properties(compressor, &with_optional(compressor, &["thresholdDb", "ratio", "attackMs", "releaseMs", "makeupDb"], &["mode", "detector", "ceilingDb"]))
        {
            return false;
        }
        let nested: [(&str, &[&str], &[&str]); 9] = [
            ("ducking", &["thresholdDb", "amountDb", "attackMs", "releaseMs"], &["mode", "ratio"]),
            ("denoiser", &["suppressionDb"], &["algorithm", "rnnoiseMix"]),
            ("deEsser", &["frequencyHz", "thresholdDb", "reductionDb"], &[]),
            ("plosive", &["frequencyHz", "thresholdDb", "reductionDb"], &[]),
            ("reverb", &["mix", "decaySeconds", "damping", "preDelayMs"], &[]),
            ("echo", &[], &["timeMs", "repeats", "mix", "feedback"]),
            ("distortion", &["driveDb", "toneHz", "mix", "outputDb"], &[]),
            ("voiceColor", &["tone", "ambience"], &[]),
            ("expander", &["mode", "thresholdDb", "ratio", "attackMs", "holdMs", "releaseMs", "rangeDb", "hysteresisDb"], &[]),
        ];
        for (name, required, optional) in nested {
            if let Some(value) = node.get(name) {
                if !value.is_object() || !exact_properties(value, &with_optional(value, required, optional)) {
                    return false;
                }
            }
        }
        if let Some(delay) = node.get("alignmentDelay") {
            if !exact_properties(delay, &["samples"]) {
                return false;
            }
        }
        let Some(bands) = node["eqBands"].as_array().filter(|b| b.len() <= graph::MAXIMUM_EQ_BANDS) else { return false };
        for band in bands {
            if !band.is_object() || !exact_properties(band, &with_optional(band, &["type", "enabled", "frequencyHz", "gainDb", "q"], &["resetFrequencyHz"])) {
                return false;
            }
        }
    }
    edges.iter().all(|edge| edge.is_object() && exact_properties(edge, &with_optional(edge, &["id", "from", "to", "kind", "gainDb", "muted"], &["fromPort", "toPort"])))
}

fn valid_text(text: &str, maximum_bytes: usize) -> bool {
    text.len() <= maximum_bytes
}

fn valid_id(id: &str) -> bool {
    !id.trim().is_empty() && valid_text(id, 64) && !id.chars().any(|c| (c as u32) < 32)
}

fn in_range(value: f64, low: f64, high: f64) -> bool {
    value.is_finite() && value >= low && value <= high
}

fn validate_values(nodes: &[GraphNode], edges: &[GraphEdge], frames_list: &[Frame]) -> String {
    for node in nodes {
        if !valid_id(&node.id) || !valid_text(&node.name, 256) || !graph::KINDS.contains(&node.kind.as_str()) {
            return "A copied node has an invalid name or type.".into();
        }
        if !in_range(node.x, -100_000.0, 100_000.0) || !in_range(node.y, -100_000.0, 100_000.0) {
            return "The pasted group would extend beyond the canvas position limit. Paste closer to the centre.".into();
        }
        let d = &node.device;
        if !valid_text(&d.id, 4096) || !valid_text(&d.stable_id, 4096) || !valid_text(&d.name, 1024) || !(-1..=255).contains(&d.channel) {
            return "A copied node has an invalid device assignment.".into();
        }
        let a = &node.app;
        if !valid_text(&a.name, 1024) || !valid_text(&a.executable_path, 32768) || (node.kind == "appInput" && a.process_id != 0 && a.executable_path.is_empty()) {
            return "A copied node has an invalid application assignment.".into();
        }
        let c = &node.compressor;
        if !in_range(node.balance, -1.0, 1.0)
            || !in_range(c.threshold_db, -60.0, 0.0)
            || !in_range(c.ratio, 1.0, 20.0)
            || !in_range(c.attack_ms, 0.1, 200.0)
            || !in_range(c.release_ms, 5.0, 2000.0)
            || !in_range(c.makeup_db, 0.0, 24.0)
        {
            return "A copied node has an invalid balance or compressor setting.".into();
        }
    }
    if edges.iter().any(|e| !valid_id(&e.id) || !valid_id(&e.from) || !valid_id(&e.to)) {
        return "A copied connection has an invalid identity.".into();
    }
    if frames_list.len() > frames::MAXIMUM_FRAMES {
        return "There are too many copied groups.".into();
    }
    let node_ids: HashSet<&str> = nodes.iter().map(|n| n.id.as_str()).collect();
    let mut frame_ids = HashSet::new();
    let mut members = HashSet::new();
    for frame in frames_list {
        if !valid_id(&frame.id)
            || !frame_ids.insert(frame.id.as_str())
            || frame.name != frames::normalize_name(&frame.name)
            || frame.color_key != frames::normalize_key(&frame.color_key)
            || !(1..=32).contains(&frame.node_ids.len())
            || frame.node_ids.iter().any(|id| !node_ids.contains(id.as_str()) || !members.insert(id.as_str()))
        {
            return "A copied group has an invalid name, color or membership.".into();
        }
    }
    String::new()
}

fn reserve(reserved: &mut HashSet<String>) -> String {
    loop {
        let id = graph::new_id();
        if reserved.insert(id.clone()) {
            return id;
        }
    }
}

/// Validates a clipboard payload and re-identifies its contents at `anchor`.
pub fn create_paste(text: &str, target: &AudioGraph, target_frames: &[Frame], anchor: (f64, f64)) -> Result<Paste, String> {
    let invalid = || "The clipboard does not contain a valid VineMix node group.".to_owned();
    if text.trim().is_empty() {
        return Err(invalid());
    }
    if text.len() > CLIPBOARD_MAXIMUM_BYTES {
        return Err("The copied node group is too large.".into());
    }
    let root: Value = serde_json::from_str(text).map_err(|_| invalid())?;
    if !valid_shape(&root) {
        return Err(invalid());
    }
    if !matches!(root["format"].as_str(), Some(CLIPBOARD_FORMAT | "AudioMixer.NodeGroup.v1")) || root["version"].as_i64() != Some(1) {
        return Err(invalid());
    }
    if !anchor.0.is_finite() || !anchor.1.is_finite() {
        return Err("Choose a valid position on the canvas.".into());
    }
    let mut nodes: Vec<GraphNode> = root["nodes"].as_array().into_iter().flatten().map(config::read_node).collect::<Result<_, _>>().map_err(|_| invalid())?;
    let mut edges: Vec<GraphEdge> = root["edges"].as_array().into_iter().flatten().map(config::read_edge).collect::<Result<_, _>>().map_err(|_| invalid())?;
    let mut frames_list: Vec<Frame> = match root.get("frames") {
        Some(frames) => serde_json::from_value(frames.clone()).map_err(|_| invalid())?,
        None => Vec::new(),
    };
    if nodes.is_empty() {
        return Err("The copied group contains no nodes.".into());
    }
    if target.nodes.len() + nodes.len() > graph::MAXIMUM_NODES {
        return Err("Pasting this group would exceed the workspace limit of 32 nodes.".into());
    }
    if target.edges.len() + edges.len() > graph::MAXIMUM_EDGES {
        return Err("Pasting this group would exceed the workspace limit of 96 connections.".into());
    }
    let error = validate_values(&nodes, &edges, &frames_list);
    if !error.is_empty() {
        return Err(error);
    }
    let source = AudioGraph { nodes: nodes.clone(), edges: edges.clone(), ..AudioGraph::default() };
    let error = graph::validate(&source);
    if !error.is_empty() {
        return Err(error);
    }
    let left = nodes.iter().map(|n| n.x).fold(f64::INFINITY, f64::min);
    let top = nodes.iter().map(|n| n.y).fold(f64::INFINITY, f64::min);
    let mut reserved_nodes: HashSet<String> = target.nodes.iter().chain(&nodes).map(|n| n.id.clone()).collect();
    let mut reserved_edges: HashSet<String> = target.edges.iter().chain(&edges).map(|e| e.id.clone()).collect();
    let mut reserved_frames: HashSet<String> = target_frames.iter().chain(&frames_list).map(|f| f.id.clone()).collect();
    let mut remap: HashMap<String, String> = HashMap::new();
    for node in &mut nodes {
        let id = reserve(&mut reserved_nodes);
        remap.insert(node.id.clone(), id.clone());
        node.id = id;
        node.x = anchor.0 + (node.x - left);
        node.y = anchor.1 + (node.y - top);
        if matches!(node.kind.as_str(), "output" | "virtualOutput") {
            node.muted = true;
        }
    }
    for edge in &mut edges {
        edge.id = reserve(&mut reserved_edges);
        edge.from = remap.get(&edge.from).cloned().ok_or_else(invalid)?;
        edge.to = remap.get(&edge.to).cloned().ok_or_else(invalid)?;
    }
    for frame in &mut frames_list {
        frame.id = reserve(&mut reserved_frames);
        frame.node_ids = frame.node_ids.iter().map(|id| remap.get(id).cloned().ok_or_else(invalid)).collect::<Result<_, _>>()?;
    }
    let combined_nodes: Vec<GraphNode> = target.nodes.iter().chain(&nodes).cloned().collect();
    let combined_edges: Vec<GraphEdge> = target.edges.iter().chain(&edges).cloned().collect();
    let combined_frames: Vec<Frame> = target_frames.iter().chain(&frames_list).cloned().collect();
    let error = validate_values(&combined_nodes, &combined_edges, &combined_frames);
    if !error.is_empty() {
        return Err(error);
    }
    let combined = AudioGraph { nodes: combined_nodes, edges: combined_edges, pan_x: target.pan_x, pan_y: target.pan_y, zoom: target.zoom };
    let error = graph::validate(&combined);
    if !error.is_empty() {
        return Err(error);
    }
    Ok(Paste { nodes, edges, frames: frames_list })
}

/// Appends a prepared paste; idempotent when already applied.
pub fn append_paste(graph: &mut AudioGraph, frames_list: &mut Vec<Frame>, paste: &Paste) -> Result<(), String> {
    if paste.nodes.is_empty() {
        return Err("The copied node group is invalid.".into());
    }
    let error = validate_values(&paste.nodes, &paste.edges, &paste.frames);
    if !error.is_empty() {
        return Err(error);
    }
    let additions = AudioGraph { nodes: paste.nodes.clone(), edges: paste.edges.clone(), ..AudioGraph::default() };
    let error = graph::validate(&additions);
    if !error.is_empty() {
        return Err(error);
    }
    let error = graph::validate(graph);
    if !error.is_empty() {
        return Err(error);
    }
    let node_ids: HashSet<&str> = graph.nodes.iter().map(|n| n.id.as_str()).collect();
    let edge_ids: HashSet<&str> = graph.edges.iter().map(|e| e.id.as_str()).collect();
    if paste.nodes.iter().all(|n| node_ids.contains(n.id.as_str())) && paste.edges.iter().all(|e| edge_ids.contains(e.id.as_str())) {
        return Ok(());
    }
    if paste.nodes.iter().any(|n| node_ids.contains(n.id.as_str()))
        || paste.edges.iter().any(|e| edge_ids.contains(e.id.as_str()))
        || paste.frames.iter().any(|f| frames_list.iter().any(|x| x.id == f.id))
    {
        return Err("The workspace changed while pasting. Copy the group again.".into());
    }
    if graph.nodes.len() + paste.nodes.len() > graph::MAXIMUM_NODES || graph.edges.len() + paste.edges.len() > graph::MAXIMUM_EDGES {
        return Err("The workspace changed and no longer has room for the whole group (32 nodes, 96 connections).".into());
    }
    let mut combined = graph.clone();
    combined.nodes.extend(paste.nodes.iter().cloned());
    combined.edges.extend(paste.edges.iter().cloned());
    let error = graph::validate(&combined);
    if !error.is_empty() {
        return Err(error);
    }
    *graph = combined;
    frames_list.extend(paste.frames.iter().cloned());
    Ok(())
}

// ----- alignment -------------------------------------------------------------

pub const ALIGNMENT_GAP: f64 = 48.0;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Alignment {
    Left,
    Top,
    Auto,
}

fn valid_position(value: f64) -> bool {
    value.is_finite() && (-100_000.0..=100_000.0).contains(&value)
}

fn selected_nodes<'a>(graph: &'a AudioGraph, ids: &HashSet<String>) -> Vec<&'a GraphNode> {
    graph.nodes.iter().filter(|n| ids.contains(&n.id)).collect()
}

fn valid_nodes(nodes: &[&GraphNode]) -> bool {
    nodes.len() >= 2 && nodes.iter().all(|n| valid_position(n.x) && valid_position(n.y))
}

fn resolve_nodes(nodes: &[&GraphNode]) -> Alignment {
    let span = |f: fn(&GraphNode) -> f64| {
        let values: Vec<f64> = nodes.iter().map(|n| f(n)).collect();
        values.iter().copied().fold(f64::NEG_INFINITY, f64::max) - values.iter().copied().fold(f64::INFINITY, f64::min)
    };
    if span(|n| n.x) >= span(|n| n.y) { Alignment::Top } else { Alignment::Left }
}

pub fn resolve_auto(graph: &AudioGraph, ids: &HashSet<String>) -> Alignment {
    let nodes = selected_nodes(graph, ids);
    if valid_nodes(&nodes) { resolve_nodes(&nodes) } else { Alignment::Top }
}

fn signal_order<'a>(graph: &AudioGraph, spatial: &[&'a GraphNode]) -> Option<Vec<&'a GraphNode>> {
    let count = graph.nodes.len();
    if count > 32 {
        return None;
    }
    let mut indices = HashMap::new();
    for (i, node) in graph.nodes.iter().enumerate() {
        if node.id.trim().is_empty() || indices.insert(node.id.as_str(), i).is_some() {
            return None;
        }
    }
    let mut reaches = vec![vec![false; count]; count];
    for edge in &graph.edges {
        if matches!(edge.kind.as_str(), "audio" | "sidechain" | "control") {
            if let (Some(&from), Some(&to)) = (indices.get(edge.from.as_str()), indices.get(edge.to.as_str())) {
                reaches[from][to] = true;
            }
        }
    }
    for via in 0..count {
        for from in 0..count {
            if reaches[from][via] {
                for to in 0..count {
                    if reaches[via][to] {
                        reaches[from][to] = true;
                    }
                }
            }
        }
    }
    if (0..count).any(|i| reaches[i][i]) {
        return None;
    }
    let selected: Vec<usize> = spatial.iter().map(|n| indices[n.id.as_str()]).collect();
    let mut indegrees: Vec<i64> = selected.iter().map(|&to| selected.iter().filter(|&&from| reaches[from][to]).count() as i64).collect();
    let mut ordered = Vec::with_capacity(spatial.len());
    while ordered.len() < spatial.len() {
        let ready = indegrees.iter().position(|&d| d == 0)?;
        indegrees[ready] = -1;
        ordered.push(spatial[ready]);
        for i in 0..selected.len() {
            if indegrees[i] > 0 && reaches[selected[ready]][selected[i]] {
                indegrees[i] -= 1;
            }
        }
    }
    Some(ordered)
}

/// New positions for the selected nodes; empty when nothing changes or the
/// arrangement is impossible.
pub fn alignment_plan(graph: &AudioGraph, ids: &HashSet<String>, alignment: Alignment, sizes: &HashMap<String, (f64, f64)>) -> HashMap<String, (f64, f64)> {
    let nodes = selected_nodes(graph, ids);
    if !valid_nodes(&nodes) || nodes.iter().any(|n| !sizes.get(&n.id).is_some_and(|&(w, h)| w.is_finite() && h.is_finite() && w > 0.0 && h > 0.0)) {
        return HashMap::new();
    }
    let alignment = if alignment == Alignment::Auto { resolve_nodes(&nodes) } else { alignment };
    let left = alignment == Alignment::Left;
    let anchor = if left { nodes.iter().map(|n| n.x).fold(f64::INFINITY, f64::min) } else { nodes.iter().map(|n| n.y).fold(f64::INFINITY, f64::min) };
    let mut spatial = nodes.clone();
    spatial.sort_by(|a, b| {
        let (pa, pb) = if left { ((a.y, a.x), (b.y, b.x)) } else { ((a.x, a.y), (b.x, b.y)) };
        pa.partial_cmp(&pb).unwrap_or(std::cmp::Ordering::Equal)
    });
    let Some(ordered) = signal_order(graph, &spatial) else { return HashMap::new() };
    let mut next = if left { spatial[0].y } else { spatial[0].x };
    let mut positions = HashMap::new();
    for node in ordered {
        let position = next;
        if !valid_position(position) {
            return HashMap::new();
        }
        positions.insert(node.id.clone(), if left { (anchor, position) } else { (position, anchor) });
        let (w, h) = sizes[&node.id];
        next = position + if left { h } else { w } + ALIGNMENT_GAP;
        if !next.is_finite() {
            return HashMap::new();
        }
    }
    positions.retain(|id, position| nodes.iter().any(|n| &n.id == id && (n.x != position.0 || n.y != position.1)));
    positions
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn insertion_and_detach_round_trip() {
        let mut g = graph::fresh();
        g.edges.retain(|e| e.id != "starter-denoiser-route" && e.id != "starter-expander-route");
        g.edges.push(GraphEdge::audio("bridge", "starter-high-pass", "starter-expander"));
        let plan = prepare_insertion(&g, "starter-denoiser", "bridge").unwrap();
        apply_node_plan(&mut g, &plan).unwrap();
        assert!(g.edges.iter().any(|e| e.from == "starter-high-pass" && e.to == "starter-denoiser"));
        assert!(g.edges.iter().any(|e| e.from == "starter-denoiser" && e.to == "starter-expander"));
        // Applying again is idempotent.
        apply_node_plan(&mut g, &plan).unwrap();
        let detach = prepare_detach(&g, "starter-denoiser").unwrap();
        apply_node_plan(&mut g, &detach).unwrap();
        assert!(g.edges.iter().any(|e| e.from == "starter-high-pass" && e.to == "starter-expander"));
        assert!(!g.edges.iter().any(|e| e.from == "starter-denoiser" || e.to == "starter-denoiser"));
    }

    #[test]
    fn clipboard_round_trip() {
        let g = graph::fresh();
        let ids: HashSet<String> = ["starter-high-pass".to_owned(), "starter-denoiser".to_owned()].into();
        let frames_list = vec![Frame { id: "f".into(), name: "Clean".into(), color_key: "teal".into(), node_ids: ids.iter().cloned().collect() }];
        let text = serialize_group(&g, &frames_list, &ids);
        let paste = create_paste(&text, &g, &frames_list, (100.0, 200.0)).unwrap();
        assert_eq!(paste.nodes.len(), 2);
        assert_eq!(paste.edges.len(), 1);
        assert_eq!(paste.frames.len(), 1);
        assert!(paste.nodes.iter().any(|n| n.x == 100.0));
        let mut target = g.clone();
        let mut target_frames = frames_list.clone();
        append_paste(&mut target, &mut target_frames, &paste).unwrap();
        assert_eq!(target.nodes.len(), 9);
        append_paste(&mut target, &mut target_frames, &paste).unwrap();
        assert_eq!(target.nodes.len(), 9, "idempotent");
        assert!(create_paste(r#"{"format":"x"}"#, &g, &[], (0.0, 0.0)).is_err());
    }

    #[test]
    fn alignment_orders_by_signal() {
        let g = graph::fresh();
        let ids: HashSet<String> = ["starter-input".to_owned(), "starter-high-pass".to_owned()].into();
        let sizes: HashMap<String, (f64, f64)> = ids.iter().map(|id| (id.clone(), (356.0, 300.0))).collect();
        let plan = alignment_plan(&g, &ids, Alignment::Top, &sizes);
        assert_eq!(plan.get("starter-high-pass"), Some(&(48.0 + 356.0 + 48.0, 200.0)));
    }
}
