//! Explicitly inert design fixtures (`UiPreview`). These identifiers never
//! reach WASAPI or the pipe.

use vinemix_model::config::{Configuration, DeviceBinding};
use vinemix_model::status::{ApplicationInfo, Endpoint, EngineStatus, NodeStatus, PathState};

use crate::protocol::Snapshot;

fn binding(id: &str, name: &str) -> DeviceBinding {
    // The desktop binding's own default channel is 0 (`DeviceBinding.Channel`),
    // so the fixture's legacy microphone presents Mono on, as in 0.5.
    DeviceBinding { id: format!("preview-{id}"), name: name.into(), channel: 0, ..Default::default() }
}

fn endpoint(binding: &DeviceBinding, direction: &str, transport: &str, channels: u32) -> Endpoint {
    Endpoint {
        id: binding.id.clone(),
        name: binding.name.clone(),
        direction: direction.into(),
        transport: transport.into(),
        is_virtual: transport != "physical",
        channels,
        rate: 48000,
        state: "Active".into(),
        ..Default::default()
    }
}

fn running(binding: &DeviceBinding, channels: u32) -> PathState {
    PathState { endpoint_id: binding.id.clone(), state: "Running".into(), channels, rate: 48000, ..Default::default() }
}

/// The migrated eight-node preview workspace (`UiPreview.CreateSnapshot`).
/// `waiting` marks the microphone missing and adds a waiting second output;
/// `signal` adds inert meter levels.
pub fn create_snapshot(waiting: bool, signal: bool) -> Snapshot {
    let mut config = Configuration { revision: 1, ..Default::default() };
    config.bus_a.muted = true;
    config.bus_a.gain_db = -24.0;
    config.microphones[0] = binding("mic1", "Example USB Microphone");
    config.main_outputs[0] = binding("a1", "Example Headphones");
    config.virtual_input_playback = binding("apps-play", "CABLE Input (VB-Audio Virtual Cable)");
    config.virtual_input_capture = binding("apps-capture", "CABLE Output (VB-Audio Virtual Cable)");
    config.virtual_output_render = binding("send-play", "Line 1 (Virtual Audio Cable)");
    config.virtual_output_capture = binding("send-capture", "Line 1 (Virtual Audio Cable)");
    config.sources[0].b.enabled = true;
    let mut endpoints = vec![
        endpoint(&config.microphones[0], "capture", "physical", 1),
        endpoint(&config.main_outputs[0], "render", "physical", 2),
        endpoint(&config.virtual_input_playback, "render", "vbCable", 2),
        endpoint(&config.virtual_input_capture, "capture", "vbCable", 2),
        endpoint(&config.virtual_output_render, "render", "vacLite", 2),
        endpoint(&config.virtual_output_capture, "capture", "vacLite", 2),
    ];
    endpoints.push(Endpoint {
        id: "preview-vb16".into(),
        name: "CABLE In 16ch (VB-Audio Virtual Cable)".into(),
        direction: "render".into(),
        transport: "vbCable".into(),
        is_virtual: true,
        channels: 2,
        rate: 48000,
        state: "Active".into(),
        ..Default::default()
    });
    let mut status = EngineStatus {
        audio_frames: 48000,
        inputs: [running(&config.microphones[0], 1), PathState::default(), running(&config.virtual_input_capture, 2)],
        outputs: [running(&config.main_outputs[0], 2), PathState::default(), running(&config.virtual_output_render, 2)],
        ..Default::default()
    };
    if waiting {
        endpoints[0].state = "Missing".into();
        status.inputs[0].state = "Waiting".into();
        config.main_outputs[1] = binding("a2", "Example Speakers");
        status.outputs[1] = PathState { state: "Waiting".into(), endpoint_id: config.main_outputs[1].id.clone(), ..Default::default() };
    }
    if signal {
        status.source_peaks = [0.12, 0.0, 0.4];
        status.peak_b = 0.12;
    }
    config.graph = crate::graph::from_legacy(&config);
    config.version = 2;
    let node = |id: &str, peak: f32, path: PathState| NodeStatus { id: id.into(), peak, path, ..Default::default() };
    let mut nodes = Vec::new();
    for (i, id) in ["mic1", "mic2", "app-input"].into_iter().enumerate() {
        nodes.push(node(id, status.source_peaks[i], status.inputs[i].clone()));
    }
    let mixing = || PathState { state: "Running".into(), ..Default::default() };
    nodes.push(node("mix-a", status.peak_a, mixing()));
    nodes.push(node("mix-b", status.peak_b, mixing()));
    for (i, id) in ["main-output-1", "main-output-2", "virtual-output"].into_iter().enumerate() {
        nodes.push(node(id, if i < 2 { status.peak_a } else { status.peak_b }, status.outputs[i].clone()));
    }
    for meter in &mut nodes {
        // These explicit fixture channels are inert preview signals only.
        meter.channels = if matches!(meter.id.as_str(), "mic1" | "mic2" | "mix-b" | "virtual-output") { 1 } else { 2 };
        meter.peak_left = meter.peak;
        meter.peak_right = if meter.channels == 1 { meter.peak } else { meter.peak * 0.6 };
    }
    status.nodes = nodes;
    Snapshot {
        settings_state: "loaded".into(),
        configuration: config,
        endpoints,
        status,
        applications: vec![
            ApplicationInfo { process_id: 100, name: "Spotify".into(), executable_path: "preview-spotify.exe".into(), ..Default::default() },
            ApplicationInfo { process_id: 200, name: "Browser".into(), executable_path: "preview-browser.exe".into(), ..Default::default() },
        ],
        settings_path: "UI preview \u{2014} no settings file".into(),
        log_path: "UI preview \u{2014} no audio logging".into(),
        ..Default::default()
    }
}

/// Replaces the fixture's graph, node telemetry and (optionally) endpoints
/// from a design fixture document `{ "graph": {..}, "status": [..],
/// "endpoints": [..] }`, the way 0.5's renderer derives each closeup from
/// `UiPreview.CreateSnapshot([])`. Graph fields use the settings format.
pub fn apply_fixture(snapshot: &mut Snapshot, text: &str) -> Result<(), String> {
    let value: serde_json::Value = serde_json::from_str(text).map_err(|e| format!("Fixture JSON: {e}"))?;
    match value.get("graph") {
        Some(serde_json::Value::String(name)) if name == "fresh" => snapshot.configuration.graph = crate::graph::fresh(),
        Some(graph) => snapshot.configuration.graph = vinemix_model::config::read_graph(graph).map_err(|e| format!("Fixture graph: {e:?}"))?,
        None => {}
    }
    if let Some(load) = value.get("processingLoad") {
        snapshot.status.processing_load = Some(serde_json::from_value(load.clone()).map_err(|e| format!("Fixture processing load: {e}"))?);
        snapshot.engine_pid = 1;
    }
    if let Some(warning) = value.get("warning").and_then(serde_json::Value::as_str) {
        snapshot.warning = warning.to_owned();
    }
    if let Some(status) = value.get("status") {
        snapshot.status.nodes = serde_json::from_value(status.clone()).map_err(|e| format!("Fixture status: {e}"))?;
    }
    if let Some(endpoints) = value.get("endpoints") {
        let extra: Vec<Endpoint> = serde_json::from_value(endpoints.clone()).map_err(|e| format!("Fixture endpoints: {e}"))?;
        snapshot.endpoints.extend(extra);
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    #[test]
    fn preview_matches_the_migrated_fixture() {
        let snapshot = super::create_snapshot(false, true);
        let graph = &snapshot.configuration.graph;
        assert_eq!(graph.nodes.len(), 8);
        assert_eq!(graph.edges.len(), 9);
        assert_eq!(snapshot.status.nodes.len(), 8);
        assert_eq!(crate::graph::validate(graph), "");
    }
}
