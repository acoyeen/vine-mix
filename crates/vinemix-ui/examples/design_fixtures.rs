//! Writes preview fixtures equivalent to 0.5's offscreen design renderer
//! (`OffscreenDesignRenderer`), for `VineMix.exe --preview --preview-fixture`.
//!
//! Usage: `cargo run -p vinemix-ui --example design_fixtures -- <new directory>`

#[cfg(windows)]
fn main() {
    use serde_json::{json, Value};
    use vinemix_model::config::{default_eq_bands, graph_json, AudioGraph, GraphNode};
    use vinemix_ui::graph::new_node;

    let directory = std::path::PathBuf::from(std::env::args().nth(1).expect("output directory"));
    std::fs::create_dir_all(&directory).expect("create output directory");
    let status = |node: &GraphNode| {
        json!({ "id": node.id, "peak": 0.12, "peakLeft": 0.12, "peakRight": 0.04, "channels": 2, "path": { "state": "Running" } })
    };
    let write = |name: &str, nodes: Vec<GraphNode>, quiet: bool| {
        let statuses: Vec<Value> = nodes.iter().map(|n| if quiet { json!({ "id": n.id, "path": { "state": "Running" } }) } else { status(n) }).collect();
        let graph = AudioGraph { nodes, zoom: 1.0, pan_x: 20.0, pan_y: 20.0, ..AudioGraph::default() };
        let document = json!({ "graph": graph_json(&graph), "status": statuses });
        std::fs::write(directory.join(format!("{name}.json")), serde_json::to_string_pretty(&document).unwrap()).unwrap();
    };

    // effects.png: a configured EQ, compressor and Low / Mid / High.
    let mut eq = new_node("eq", 30.0, 35.0);
    eq.eq_bands = default_eq_bands();
    eq.eq_bands[0].gain_db = 3.0;
    eq.eq_bands[2].frequency_hz = 1800.0;
    eq.eq_bands[2].gain_db = -6.0;
    eq.eq_bands[2].q = 1.4;
    eq.eq_bands[4].gain_db = 2.0;
    let mut compressor = new_node("compressor", 1090.0, 35.0);
    compressor.compressor.threshold_db = -22.0;
    compressor.compressor.ratio = 3.0;
    let mut tone = new_node("tone", 680.0, 35.0);
    tone.eq_bands[0].gain_db = 3.0;
    tone.eq_bands[1].gain_db = -2.0;
    tone.eq_bands[2].gain_db = 1.0;
    write("effects", vec![eq, compressor, tone], false);

    // One freshly created node of every catalogue kind, for closeups.
    for kind in [
        "microphone", "virtualInput", "appInput", "mix", "gain", "output", "virtualOutput", "control", "eq", "tone", "highPass", "lowPass",
        "compressor", "limiter", "ducking", "expander", "gate", "pan", "mono", "reroute", "denoiser", "deEsser", "plosive", "reverb", "echo",
        "distortion", "voiceColor", "alignmentDelay", "channelSwap", "stereoWidth", "stereoPan", "crossfade", "stereoCombine", "stereoSplit",
    ] {
        let mut node = new_node(kind, 0.0, 0.0);
        node.id = format!("closeup-{kind}");
        write(&format!("node-{kind}"), vec![node], true);
    }
    println!("{}", directory.display());
}

#[cfg(not(windows))]
fn main() {}
