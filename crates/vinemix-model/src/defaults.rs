//! Starter workspace and version-1 migration (`defaultConfiguration` and
//! `migrateLegacyGraph`). Both must match the desktop's `GraphDefaults`.

use crate::config::{AudioGraph, Configuration, DeviceBinding, EqBand, GraphEdge, GraphNode, BUTTERWORTH_Q};

/// The graph a new store, or an explicit Start fresh, adopts.
pub fn default_configuration() -> Configuration {
    let mut input = GraphNode::new("starter-input", "microphone", "Microphone", 48.0, 200.0);
    input.auto_assign_device = false;
    let mut high_pass = GraphNode::new("starter-high-pass", "highPass", "High-pass filter", 499.0, 200.0);
    high_pass.eq_bands = vec![EqBand::new("highPass", true, 80.0, 0.0, BUTTERWORTH_Q, 0.0)];
    let mut denoiser = GraphNode::new("starter-denoiser", "denoiser", "Denoiser", 950.0, 200.0);
    denoiser.denoiser.algorithm = "rnnoise".into();
    let expander = GraphNode::new("starter-expander", "expander", "Expander", 1401.0, 200.0);
    let mut compressor = GraphNode::new("starter-compressor", "compressor", "Compressor", 1924.0, 200.0);
    compressor.dynamics_ui_version = 1;
    let mut virtual_output = GraphNode::new("starter-virtual-output", "virtualOutput", "Virtual output", 2447.0, 464.0);
    virtual_output.muted = true;
    let mut output = GraphNode::new("starter-listening-output", "output", "Listening output", 2447.0, 48.0);
    output.gain_db = -24.0;
    output.muted = true;
    output.auto_assign_device = false;
    let edges = vec![
        GraphEdge::audio("starter-high-pass-route", &input.id, &high_pass.id),
        GraphEdge::audio("starter-denoiser-route", &high_pass.id, &denoiser.id),
        GraphEdge::audio("starter-expander-route", &denoiser.id, &expander.id),
        GraphEdge::audio("starter-compressor-route", &expander.id, &compressor.id),
        GraphEdge::audio("starter-listening-route", &compressor.id, &output.id),
        GraphEdge::audio("starter-virtual-route", &compressor.id, &virtual_output.id),
    ];
    Configuration {
        graph: AudioGraph {
            nodes: vec![input, high_pass, denoiser, expander, compressor, output, virtual_output],
            edges,
            zoom: 0.4,
            ..AudioGraph::default()
        },
        ..Configuration::default()
    }
}

/// Rebuilds the fixed version-1 mixer as a canvas (`GraphDefaults.FromLegacy`).
pub fn migrate_legacy_graph(config: &mut Configuration) {
    let mut graph = AudioGraph::default();
    let mut node = |id: &str, kind: &str, name: &str, x: f64, y: f64, binding: &DeviceBinding, gain: f32, muted: bool| {
        let mut value = GraphNode::new(id, kind, name, x, y);
        value.device = binding.clone();
        value.gain_db = f64::from(gain);
        value.muted = muted;
        graph.nodes.push(value);
    };
    let none = DeviceBinding::default();
    node("mic1", "microphone", "Microphone 1", 48.0, 60.0, &config.microphones[0], 0.0, config.sources[0].muted);
    node("mic2", "microphone", "Microphone 2", 48.0, 390.0, &config.microphones[1], 0.0, config.sources[1].muted);
    node("app-input", "virtualInput", "App Audio", 48.0, 720.0, &config.virtual_input_capture, 0.0, config.sources[2].muted);
    node("mix-a", "mix", "What I hear", 448.0, 100.0, &none, config.bus_a.gain_db, config.bus_a.muted);
    node("mix-b", "mix", "What others hear", 448.0, 550.0, &none, config.bus_b.gain_db, config.bus_b.muted);
    node("main-output-1", "output", "Listening output 1", 848.0, 60.0, &config.main_outputs[0], 0.0, false);
    node("main-output-2", "output", "Listening output 2", 848.0, 390.0, &config.main_outputs[1], 0.0, false);
    node("virtual-output", "virtualOutput", "Virtual microphone", 848.0, 720.0, &config.virtual_output_render, 0.0, false);
    for (index, id) in ["mic1", "mic2", "app-input"].into_iter().enumerate() {
        let source = &config.sources[index];
        for (bus, send, mix) in [("a", &source.a, "mix-a"), ("b", &source.b, "mix-b")] {
            let mut edge = GraphEdge::audio(&format!("{id}-{bus}"), id, mix);
            edge.gain_db = f64::from(send.gain_db);
            edge.muted = !send.enabled;
            graph.edges.push(edge);
        }
    }
    graph.edges.push(GraphEdge::audio("main-output-1-in", "mix-a", "main-output-1"));
    graph.edges.push(GraphEdge::audio("main-output-2-in", "mix-a", "main-output-2"));
    graph.edges.push(GraphEdge::audio("virtual-output-in", "mix-b", "virtual-output"));
    config.graph = graph;
    config.version = 2;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn starter_graph_compiles() {
        let config = default_configuration();
        crate::validate::validate_configuration(&config).unwrap();
        assert_eq!(config.graph.nodes.len(), 7);
        assert_eq!(config.graph.zoom, 0.4);
    }

    #[test]
    fn legacy_migration_matches_fixed_mixer() {
        let mut config = Configuration { version: 1, ..Configuration::default() };
        config.sources[1].a.enabled = true;
        config.sources[1].a.gain_db = -6.0;
        config.bus_b.muted = true;
        migrate_legacy_graph(&mut config);
        assert_eq!(config.version, 2);
        assert_eq!(config.graph.nodes.len(), 8);
        assert_eq!(config.graph.edges.len(), 9);
        let edge = config.graph.edges.iter().find(|e| e.id == "mic2-a").unwrap();
        assert!(!edge.muted);
        assert_eq!(edge.gain_db, -6.0);
        assert!(config.graph.edges.iter().find(|e| e.id == "mic1-a").unwrap().muted);
        assert!(config.graph.nodes.iter().find(|n| n.id == "mix-b").unwrap().muted);
        crate::validate::validate_configuration(&config).unwrap();
    }
}
