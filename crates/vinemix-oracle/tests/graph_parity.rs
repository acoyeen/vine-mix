//! The Rust graph compiler and processor against the unmodified 0.5 C++
//! `compileGraph` / `GraphProcessor`: every node's output buffer and meter
//! must match bit for bit, quantum after quantum, across live edits.

#![cfg(oracle_available)]

use vinemix_dsp::GraphProcessor;
use vinemix_model::config::{graph_json, tone_defaults, AudioGraph, EqBand, GraphEdge, GraphNode};
use vinemix_model::plan::{compile_graph, graph_signal_channels, GraphPlan};
use vinemix_oracle::ffi::{self, Graph};
use vinemix_rt::{Frame, QUANTUM_FRAMES};

fn node(id: &str, kind: &str) -> GraphNode {
    GraphNode::new(id, kind, id, 0.0, 0.0)
}

fn edge(id: &str, from: &str, to: &str) -> GraphEdge {
    GraphEdge::audio(id, from, to)
}

fn port(mut edge: GraphEdge, from_port: &str, to_port: &str) -> GraphEdge {
    edge.from_port = from_port.into();
    edge.to_port = to_port.into();
    edge
}

fn kind(mut edge: GraphEdge, kind: &str) -> GraphEdge {
    edge.kind = kind.into();
    edge
}

fn json(graph: &AudioGraph) -> String {
    serde_json::to_string(&graph_json(graph)).unwrap()
}

/// A long chain through every processing effect.
fn effects_chain() -> AudioGraph {
    let mut nodes = vec![node("mic", "microphone")];
    let mut high_pass = node("hp", "highPass");
    high_pass.eq_bands = vec![EqBand::new("highPass", true, 120.0, 0.0, 0.8, 0.0)];
    let mut eq = node("eq", "eq");
    eq.eq_bands[0].gain_db = 4.0;
    eq.eq_bands[2].gain_db = -6.0;
    eq.eq_bands[2].q = 2.5;
    eq.eq_bands.push(EqBand::new("notch", true, 3500.0, 0.0, 4.0, 0.0));
    let mut tone = node("tone", "tone");
    tone.eq_bands = tone_defaults();
    tone.eq_bands[0].gain_db = 3.0;
    tone.eq_bands[2].gain_db = -2.5;
    let mut compressor = node("comp", "compressor");
    compressor.compressor.threshold_db = -30.0;
    compressor.compressor.makeup_db = 6.0;
    compressor.dynamics_ui_version = 1;
    let mut denoiser = node("dn", "denoiser");
    denoiser.denoiser.suppression_db = -25.0;
    let mut de_esser = node("de", "deEsser");
    de_esser.de_esser.threshold_db = -40.0;
    let mut plosive = node("pl", "plosive");
    plosive.plosive.threshold_db = -40.0;
    let mut expander = node("ex", "expander");
    expander.expander.threshold_db = -50.0;
    let mut gate = node("gt", "gate");
    gate.expander.mode = "gate".into();
    gate.expander.threshold_db = -45.0;
    let mut voice = node("vc", "voiceColor");
    voice.voice_color.tone = -0.4;
    voice.voice_color.ambience = 0.7;
    let mut reverb = node("rv", "reverb");
    reverb.reverb.mix = 0.3;
    let mut echo = node("ec", "echo");
    echo.echo.time_ms = 45.0;
    echo.echo.repeats = 4;
    let mut distortion = node("ds", "distortion");
    distortion.distortion.drive_db = 18.0;
    distortion.distortion.mix = 0.6;
    let mut delay = node("ad", "alignmentDelay");
    delay.alignment_delay.samples = 333;
    let mut limiter = node("lim", "limiter");
    limiter.compressor.ceiling_db = -3.0;
    let mut gain = node("gain", "gain");
    gain.gain_db = 9.0;
    let out = node("out", "output");
    nodes.extend([high_pass, eq, tone, compressor, denoiser, de_esser, plosive, expander, gate, voice, reverb, echo, distortion, delay, gain, limiter, out]);
    let ids: Vec<String> = nodes.iter().map(|n| n.id.clone()).collect();
    let edges = ids.windows(2).enumerate().map(|(i, pair)| edge(&format!("e{i}"), &pair[0], &pair[1])).collect();
    AudioGraph { nodes, edges, ..AudioGraph::default() }
}

/// Stereo routing, sockets, Wet ports, control links and triggers.
fn routing_graph() -> AudioGraph {
    let app = node("app", "appInput");
    let mic = node("mic", "microphone");
    let mut mono_mic = node("mic2", "microphone");
    mono_mic.mono = true;
    let vin = node("vin", "virtualInput");
    let split = node("split", "stereoSplit");
    let swap = node("swap", "channelSwap");
    let combine = node("combine", "stereoCombine");
    let mut width = node("width", "stereoWidth");
    width.stereo_width = 1.6;
    let mut pan = node("pan", "stereoPan");
    pan.pan_position = -0.35;
    let mut balance = node("bal", "pan");
    balance.balance = 0.4;
    let mut fade = node("fade", "crossfade");
    fade.crossfade_position = 0.3;
    let mono = node("mono", "mono");
    let mut reverb = node("rv", "reverb");
    reverb.reverb.mix = 0.5;
    reverb.reverb.decay_seconds = 2.5;
    let mut echo = node("ec", "echo");
    echo.echo.time_ms = 25.0;
    echo.echo.mix = 0.4;
    let mix = node("mix", "mix");
    let mut duck = node("duck", "ducking");
    duck.ducking.threshold_db = -35.0;
    let mut dyn_duck = node("dduck", "ducking");
    dyn_duck.ducking.mode = "dynamic".into();
    dyn_duck.ducking.threshold_db = -40.0;
    let mut gate = node("gate", "gate");
    gate.expander.mode = "gate".into();
    gate.expander.threshold_db = -30.0;
    let mut control = node("ctl", "control");
    control.gain_db = -4.0;
    let mut linked = node("ctl2", "control");
    linked.control_multiplier = 0.7;
    let mut comp_limit = node("cl", "compressor");
    comp_limit.compressor.mode = "limit".into();
    comp_limit.compressor.ceiling_db = -6.0;
    let mut out = node("out", "output");
    out.gain_db = -3.0;
    let vout = node("vout", "virtualOutput");
    let nodes = vec![
        app, mic, mono_mic, vin, split, swap, combine, width, pan, balance, fade, mono, reverb, echo, mix, duck, dyn_duck, gate,
        control, linked, comp_limit, out, vout,
    ];
    let edges = vec![
        edge("a1", "app", "split"),
        port(edge("a2", "split", "combine"), "left", "left"),
        port(edge("a3", "split", "swap"), "right", "audio"),
        port(edge("a4", "swap", "combine"), "audio", "right"),
        edge("a5", "combine", "width"),
        port(edge("a6", "width", "fade"), "audio", "a"),
        port(edge("a7", "mic", "fade"), "audio", "b"),
        edge("a8", "fade", "pan"),
        edge("a9", "pan", "bal"),
        edge("a10", "bal", "rv"),
        port(edge("a11", "rv", "mix"), "wet", "audio"),
        edge("a12", "rv", "ec"),
        port(edge("a13", "ec", "mix"), "wet", "audio"),
        edge("a14", "ec", "mix"),
        edge("a15", "mix", "duck"),
        kind(edge("t1", "mic2", "duck"), "sidechain"),
        edge("a16", "duck", "dduck"),
        kind(edge("t2", "vin", "dduck"), "sidechain"),
        edge("a17", "dduck", "gate"),
        kind(port(edge("t3", "split", "gate"), "left", "audio"), "sidechain"),
        edge("a18", "gate", "cl"),
        edge("a19", "cl", "out"),
        edge("a20", "mono", "vout"),
        edge("a21", "mic2", "mono"),
        edge("a22", "vin", "mono"),
        kind(edge("c1", "ctl", "mix"), "control"),
        kind(edge("c2", "ctl2", "out"), "control"),
        edge("a23", "cl", "vout"),
    ];
    AudioGraph { nodes, edges, ..AudioGraph::default() }
}

fn source_signal(node: usize, frame: u64, seed: &mut u32) -> Frame {
    *seed = seed.wrapping_mul(1_103_515_245).wrapping_add(12_345);
    let noise = ((*seed >> 9) as f32 / (1u32 << 23) as f32 - 0.5) * 0.01;
    let t = frame as f32 / 48_000.0;
    let f = 110.0 * (node as f32 + 1.0);
    let envelope = if (frame / 9_600 + node as u64) % 3 == 0 { 0.0 } else { 1.0 };
    let left = envelope * 0.6 * (2.0 * std::f32::consts::PI * f * t).sin() + noise;
    let right = envelope * 0.5 * (2.0 * std::f32::consts::PI * f * 1.5 * t).cos() - noise;
    // Occasional hot peaks and an injected NaN exercise clamping and cleanup.
    if frame % 24_001 == 777 {
        return Frame::new(f32::NAN, 3.5);
    }
    Frame::new(left, right)
}

struct Pair {
    graph: AudioGraph,
    plan: GraphPlan,
    rust: GraphProcessor,
    reference: Graph,
    frame: u64,
    seed: u32,
}

impl Pair {
    fn new(graph: AudioGraph) -> Self {
        let plan = compile_graph(&graph).expect("graph compiles in Rust");
        let reference = Graph::new(&json(&graph)).expect("graph compiles in C++");
        let mut rust = GraphProcessor::new();
        rust.prepare(&plan);
        Self { graph, plan, rust, reference, frame: 0, seed: 1 }
    }

    fn run(&mut self, quanta: usize, defer: bool) {
        for q in 0..quanta {
            let mut rows: Vec<Vec<f32>> = vec![Vec::new(); 32];
            let mut frames: Vec<Vec<Frame>> = vec![Vec::new(); 32];
            for (i, node) in self.plan.nodes.iter().enumerate() {
                if node.kind.is_source() {
                    for n in 0..QUANTUM_FRAMES {
                        let sample = source_signal(i, self.frame + n as u64, &mut self.seed);
                        frames[i].push(sample);
                        rows[i].extend([sample.left, sample.right]);
                    }
                }
            }
            let inputs: Vec<Option<&[f32]>> = rows.iter().map(|r| (!r.is_empty()).then_some(r.as_slice())).collect();
            self.reference.process(&inputs, defer);
            let views: [&[Frame]; 32] = std::array::from_fn(|i| frames[i].as_slice());
            self.rust.process(&self.plan, &views, defer);
            self.frame += QUANTUM_FRAMES as u64;
            for (i, node) in self.plan.nodes.iter().enumerate() {
                let expected = self.reference.output(i);
                let actual = self.rust.output(i);
                for (n, frame) in actual.iter().enumerate() {
                    let (l, r) = (expected[2 * n], expected[2 * n + 1]);
                    assert!(
                        frame.left.to_bits() == l.to_bits() && frame.right.to_bits() == r.to_bits(),
                        "quantum {q}, node {} ({:?}), frame {n}: rust ({}, {}) vs c++ ({l}, {r})",
                        node.id,
                        node.kind,
                        frame.left,
                        frame.right
                    );
                }
                let m = self.rust.meter(i);
                let e = self.reference.meter(i);
                let actual = [m.peak, m.peak_left, m.peak_right, if m.clipped { 1.0 } else { 0.0 }, m.gain_reduction_db, m.detector_db];
                for k in 0..6 {
                    assert!(actual[k].to_bits() == e[k].to_bits(), "quantum {q}, node {} meter {k}: {} vs {}", node.id, actual[k], e[k]);
                }
            }
        }
    }

    /// Applies an edit through both engines' plan transition.
    fn edit(&mut self, change: impl FnOnce(&mut AudioGraph), mode: i32) {
        let mut graph = self.graph.clone();
        change(&mut graph);
        let plan = compile_graph(&graph).expect("edited graph compiles");
        self.reference.update(&json(&graph), mode).expect("C++ accepts the edit");
        let mut next = GraphProcessor::new();
        next.prepare(&plan);
        if mode == 0 {
            next.inherit(&self.rust, &self.plan, &plan);
        } else {
            next.adopt(&mut self.rust, &self.plan, &plan, mode == 2);
        }
        self.rust = next;
        self.plan = plan;
        self.graph = graph;
    }
}

#[test]
fn effects_chain_matches_reference() {
    let mut pair = Pair::new(effects_chain());
    pair.run(800, false);
}

#[test]
fn routing_graph_matches_reference() {
    let mut pair = Pair::new(routing_graph());
    pair.run(600, true);
    let capture = [2i32; 32];
    let channels = graph_signal_channels(&pair.plan, &capture);
    assert_eq!(channels, pair.reference.signal_channels(&capture));
}

#[test]
fn live_edits_match_reference() {
    for mode in [0, 1, 2] {
        let mut pair = Pair::new(effects_chain());
        pair.run(120, false);
        pair.edit(|g| g.nodes[2].eq_bands[0].gain_db = -8.0, mode); // EQ crossfade
        pair.run(40, false);
        pair.edit(|g| g.nodes[4].compressor.mode = "limit".into(), mode);
        pair.run(40, false);
        pair.edit(|g| g.nodes[5].denoiser.algorithm = "rnnoise".into(), mode); // warm-up path
        pair.run(120, false);
        pair.edit(|g| g.nodes[5].denoiser.rnnoise_mix = 0.4, mode);
        pair.run(40, false);
        pair.edit(|g| g.nodes[12].echo.time_ms = 120.0, mode); // echo restart
        pair.run(60, false);
        pair.edit(|g| g.nodes[14].alignment_delay.samples = 4_000, mode);
        pair.run(40, false);
        pair.edit(|g| g.nodes[10].voice_color.tone = 0.8, mode);
        pair.run(40, false);
        pair.edit(|g| g.nodes[8].bypass = true, mode);
        pair.run(40, false);
        pair.edit(|g| g.nodes[16].muted = true, mode);
        pair.run(40, false);
        // Remove and re-add a node: ids keep state only while they survive.
        pair.edit(
            |g| {
                g.nodes.remove(13);
                g.edges.retain(|e| e.from != "ds" && e.to != "ds");
                g.edges.push(edge("bridge", "ec", "ad"));
            },
            mode,
        );
        pair.run(60, false);
    }
}

#[test]
fn routing_edits_match_reference() {
    let mut pair = Pair::new(routing_graph());
    pair.run(100, true);
    pair.edit(|g| g.nodes.iter_mut().find(|n| n.id == "fade").unwrap().crossfade_curve = "linear".into(), 2);
    pair.run(20, true);
    pair.edit(|g| g.nodes.iter_mut().find(|n| n.id == "pan").unwrap().pan_position = 0.9, 2);
    pair.run(20, true);
    pair.edit(|g| g.nodes.iter_mut().find(|n| n.id == "ctl2").unwrap().control_multiplier = 0.0, 2);
    pair.run(20, true);
    pair.edit(|g| g.nodes.iter_mut().find(|n| n.id == "dduck").unwrap().ducking.mode = "static".into(), 2);
    pair.run(40, true);
    pair.edit(|g| g.nodes.iter_mut().find(|n| n.id == "cl").unwrap().compressor.mode = "compress".into(), 2);
    pair.run(40, true);
    pair.edit(|g| g.edges.retain(|e| e.id != "a13"), 1);
    pair.run(40, true);
    pair.reference.reset();
    pair.rust.reset();
    pair.run(40, true);
}

#[test]
fn validation_messages_match_reference() {
    let mut cases: Vec<AudioGraph> = Vec::new();
    let base = effects_chain();
    let mut g = base.clone();
    g.nodes[3].eq_bands[1].frequency_hz = 900.0;
    cases.push(g);
    let mut g = base.clone();
    g.edges.push(edge("loop", "out", "mic"));
    cases.push(g);
    let mut g = base.clone();
    g.nodes[13].echo.repeats = 9;
    cases.push(g);
    let mut g = base.clone();
    g.nodes[9].kind = "gate".into();
    g.nodes[9].expander.mode = "expander".into();
    cases.push(g);
    let mut g = base.clone();
    g.edges.push(port(edge("bad", "rv", "ec"), "left", "audio"));
    cases.push(g);
    let mut g = base.clone();
    g.zoom = 5.0;
    cases.push(g);
    let mut g = routing_graph();
    g.edges.push(kind(edge("t9", "mic", "duck"), "sidechain"));
    cases.push(g);
    let mut g = routing_graph();
    g.edges.push(kind(edge("c9", "ctl2", "mix"), "control"));
    cases.push(g);
    for (index, graph) in cases.iter().enumerate() {
        let expected = ffi::validate(&json(graph)).unwrap_or_default();
        let actual = compile_graph(graph).err().map(|e| e.to_string()).unwrap_or_default();
        assert!(!expected.is_empty(), "case {index} should be rejected");
        assert_eq!(actual, expected, "case {index}");
    }
}

/// The 0.5 starter voice chain: microphone, high-pass, RNNoise, expander,
/// compressor, listening and virtual outputs.
fn voice_chain() -> AudioGraph {
    let mic = node("mic", "microphone");
    let mut high_pass = node("hp", "highPass");
    high_pass.eq_bands = vec![EqBand::new("highPass", true, 80.0, 0.0, vinemix_model::config::BUTTERWORTH_Q, 0.0)];
    let mut denoiser = node("dn", "denoiser");
    denoiser.denoiser.algorithm = "rnnoise".into();
    let ex = node("ex", "expander");
    let mut comp = node("comp", "compressor");
    comp.dynamics_ui_version = 1;
    let out = node("out", "output");
    let vout = node("vout", "virtualOutput");
    let edges = vec![edge("e1", "mic", "hp"), edge("e2", "hp", "dn"), edge("e3", "dn", "ex"), edge("e4", "ex", "comp"), edge("e5", "comp", "out"), edge("e6", "comp", "vout")];
    AudioGraph { nodes: vec![mic, high_pass, denoiser, ex, comp, out, vout], edges, ..AudioGraph::default() }
}

/// Processing cost of the Rust port against the unmodified 0.5 C++ engine
/// core on identical input (release builds; run with
/// `cargo test -p vinemix-oracle --release --test graph_parity -- --ignored --nocapture`).
#[test]
#[ignore = "benchmark"]
fn processing_benchmark() {
    const QUANTA: usize = 2_000; // 10 seconds of audio at 48 kHz.
    const RUNS: usize = 7;
    /// One quantum is 5 ms; anything longer has missed its deadline.
    const DEADLINE_US: f64 = 5_000.0;
    for (name, graph) in [("voice chain (starter)", voice_chain()), ("effects chain (17 effects)", effects_chain()), ("routing graph (23 nodes)", routing_graph())] {
        let plan = compile_graph(&graph).expect("graph compiles");
        let mut seed = 1u32;
        let mut frames_by_quantum: Vec<Vec<Vec<Frame>>> = Vec::with_capacity(QUANTA);
        for q in 0..QUANTA {
            let mut frames: Vec<Vec<Frame>> = vec![Vec::new(); 32];
            for (i, node) in plan.nodes.iter().enumerate() {
                if node.kind.is_source() {
                    frames[i] = (0..QUANTUM_FRAMES).map(|n| source_signal(i, (q * QUANTUM_FRAMES + n) as u64, &mut seed)).collect();
                }
            }
            frames_by_quantum.push(frames);
        }
        let rows_by_quantum: Vec<Vec<Vec<f32>>> =
            frames_by_quantum.iter().map(|q| q.iter().map(|f| f.iter().flat_map(|s| [s.left, s.right]).collect()).collect()).collect();
        // Both sides get their per-quantum input views built before the
        // clock starts: the timed loops contain the process call and nothing
        // else. (The C++ loop used to allocate its view vector every quantum
        // inside the timed region while the Rust loop did not.)
        let cpp_views: Vec<Vec<Option<&[f32]>>> =
            rows_by_quantum.iter().map(|rows| rows.iter().map(|r| (!r.is_empty()).then_some(r.as_slice())).collect()).collect();
        let rust_views: Vec<[&[Frame]; 32]> = frames_by_quantum.iter().map(|frames| std::array::from_fn(|i| frames[i].as_slice())).collect();
        // Every quantum of every run, in microseconds.
        let mut cpp: Vec<f64> = Vec::with_capacity(QUANTA * RUNS);
        let mut rust: Vec<f64> = Vec::with_capacity(QUANTA * RUNS);
        let (mut cpp_totals, mut rust_totals) = (Vec::new(), Vec::new());
        for _ in 0..RUNS {
            let mut reference = Graph::new(&json(&graph)).expect("graph compiles in C++");
            let run = std::time::Instant::now();
            for inputs in &cpp_views {
                let start = std::time::Instant::now();
                reference.process(inputs, false);
                cpp.push(start.elapsed().as_secs_f64() * 1e6);
            }
            cpp_totals.push(run.elapsed().as_secs_f64());
            let mut processor = GraphProcessor::new();
            processor.prepare(&plan);
            let run = std::time::Instant::now();
            for views in &rust_views {
                let start = std::time::Instant::now();
                processor.process(&plan, views, false);
                rust.push(start.elapsed().as_secs_f64() * 1e6);
            }
            rust_totals.push(run.elapsed().as_secs_f64());
        }
        let summary = |samples: &mut Vec<f64>| {
            samples.sort_by(f64::total_cmp);
            let at = |q: f64| samples[((samples.len() - 1) as f64 * q) as usize];
            (at(0.5), at(0.99), at(0.999), samples[samples.len() - 1], samples.iter().filter(|&&us| us > DEADLINE_US).count())
        };
        let median = |v: &mut Vec<f64>| {
            v.sort_by(f64::total_cmp);
            v[v.len() / 2]
        };
        let (c, r) = (summary(&mut cpp), summary(&mut rust));
        let (c_total, r_total) = (median(&mut cpp_totals), median(&mut rust_totals));
        println!("BENCH {name}: per quantum in us (median / p99 / p99.9 / max / over the 5 ms deadline), {} quanta each", QUANTA * RUNS);
        println!("  C++   {:8.2} / {:8.2} / {:8.2} / {:8.2} / {}", c.0, c.1, c.2, c.3, c.4);
        println!("  Rust  {:8.2} / {:8.2} / {:8.2} / {:8.2} / {}", r.0, r.1, r.2, r.3, r.4);
        println!(
            "  share of real time (median run): C++ {:.3}%, Rust {:.3}%; Rust/C++ = {:.3} (median quantum {:.3})",
            c_total / 10.0 * 100.0,
            r_total / 10.0 * 100.0,
            r_total / c_total,
            r.0 / c.0
        );
    }
}
