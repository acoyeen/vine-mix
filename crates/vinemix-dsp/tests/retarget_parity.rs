//! RUST-REV-008: a parameter edit applied by retargeting the running
//! processor must produce exactly the samples of the established path, which
//! prepares a new processor and adopts the old one's state into it.
//!
//! Two processors run the same input through the same sequence of edits, one
//! on each path. Every node's output buffer and meter is compared bit for bit
//! after every quantum, including quanta inside the 10 ms ramps and EQ
//! crossfades that the edits start, and edits that arrive while an earlier
//! edit is still ramping. The established path is itself checked against the
//! 0.5 engine by `vinemix-oracle`.

use vinemix_dsp::{GraphProcessor, Inputs};
use vinemix_model::config::{tone_defaults, AudioGraph, EqBand, GraphEdge, GraphNode};
use vinemix_model::limits::MAXIMUM_GRAPH_NODES;
use vinemix_model::plan::{compile_graph, same_indexing, GraphPlan};
use vinemix_rt::{Frame, QUANTUM_FRAMES};

fn node(id: &str, kind: &str) -> GraphNode {
    GraphNode::new(id, kind, id, 0.0, 0.0)
}

fn edge(id: &str, from: &str, to: &str) -> GraphEdge {
    GraphEdge::audio(id, from, to)
}

fn with_ports(mut edge: GraphEdge, from_port: &str, to_port: &str) -> GraphEdge {
    edge.from_port = from_port.into();
    edge.to_port = to_port.into();
    edge
}

fn of_kind(mut edge: GraphEdge, kind: &str) -> GraphEdge {
    edge.kind = kind.into();
    edge
}

/// Every processing kind in one chain.
fn effects_chain() -> AudioGraph {
    let mut high_pass = node("hp", "highPass");
    high_pass.eq_bands = vec![EqBand::new("highPass", true, 120.0, 0.0, 0.8, 0.0)];
    let mut eq = node("eq", "eq");
    eq.eq_bands[0].gain_db = 4.0;
    let mut gate = node("gt", "gate");
    gate.expander.mode = "gate".into();
    let mut limiter = node("lim", "limiter");
    limiter.compressor.ceiling_db = -3.0;
    let mut tone = node("tone", "tone");
    tone.eq_bands = tone_defaults();
    let nodes = vec![
        node("mic", "microphone"),
        high_pass,
        eq,
        tone,
        node("comp", "compressor"),
        node("dn", "denoiser"),
        node("de", "deEsser"),
        node("pl", "plosive"),
        node("ex", "expander"),
        gate,
        node("vc", "voiceColor"),
        node("rv", "reverb"),
        node("ec", "echo"),
        node("ds", "distortion"),
        node("ad", "alignmentDelay"),
        node("gain", "gain"),
        limiter,
        node("out", "output"),
    ];
    let ids: Vec<String> = nodes.iter().map(|n| n.id.clone()).collect();
    let edges = ids.windows(2).enumerate().map(|(i, pair)| edge(&format!("e{i}"), &pair[0], &pair[1])).collect();
    AudioGraph { nodes, edges, ..AudioGraph::default() }
}

/// Stereo routing, Wet ports, a control link, ducking and a trigger.
fn routing() -> AudioGraph {
    let mut duck = node("duck", "ducking");
    duck.ducking.threshold_db = -35.0;
    let mut gate = node("gate", "gate");
    gate.expander.mode = "gate".into();
    let mut control = node("ctl", "control");
    control.gain_db = -4.0;
    let nodes = vec![
        node("mic", "microphone"),
        node("mic2", "microphone"),
        node("split", "stereoSplit"),
        node("combine", "stereoCombine"),
        node("width", "stereoWidth"),
        node("pan", "stereoPan"),
        node("bal", "pan"),
        node("fade", "crossfade"),
        node("rv", "reverb"),
        node("ec", "echo"),
        node("mix", "mix"),
        duck,
        gate,
        control,
        node("out", "output"),
        node("out2", "output"),
    ];
    let edges = vec![
        edge("a1", "mic", "split"),
        with_ports(edge("a2", "split", "combine"), "left", "right"),
        with_ports(edge("a3", "split", "combine"), "right", "left"),
        edge("a4", "combine", "width"),
        edge("a5", "width", "pan"),
        edge("a6", "pan", "bal"),
        with_ports(edge("a7", "bal", "fade"), "audio", "a"),
        with_ports(edge("a8", "mic2", "fade"), "audio", "b"),
        edge("a9", "fade", "rv"),
        with_ports(edge("a10", "rv", "ec"), "wet", "audio"),
        edge("a11", "rv", "mix"),
        with_ports(edge("a12", "ec", "mix"), "wet", "audio"),
        edge("a13", "mix", "duck"),
        of_kind(edge("s1", "mic2", "duck"), "sidechain"),
        edge("a14", "duck", "gate"),
        of_kind(edge("s2", "mic2", "gate"), "sidechain"),
        edge("a15", "gate", "out"),
        edge("a16", "mix", "out2"),
        of_kind(edge("c1", "ctl", "out2"), "control"),
    ];
    AudioGraph { nodes, edges, ..AudioGraph::default() }
}

/// A deterministic parameter-only variation of `graph` for step `step`:
/// every parameter family moves, including mode switches, mute and bypass.
fn variation(graph: &AudioGraph, step: usize) -> AudioGraph {
    let mut graph = graph.clone();
    // Bounded so every variation stays inside the validated ranges.
    let s = (step % 6) as f64;
    let flip = step % 2 == 1;
    for node in &mut graph.nodes {
        node.gain_db = ((step * 3 + node.id.len()) % 9) as f64 - 6.0;
        match node.kind.as_str() {
            "highPass" => node.eq_bands[0].frequency_hz = 80.0 + 20.0 * s,
            "eq" => {
                node.eq_bands[0].gain_db = 4.0 - s;
                node.eq_bands[1].q = 0.7 + 0.2 * s;
                node.eq_bands[2].enabled = !flip;
            }
            "tone" => {
                if let Some(band) = node.eq_bands.first_mut() {
                    band.gain_db = -3.0 + s;
                }
            }
            "compressor" => {
                node.compressor.threshold_db = -30.0 + 2.0 * s;
                node.compressor.makeup_db = s;
                node.compressor.mode = if step % 3 == 2 { "limit".into() } else { "compress".into() };
            }
            "limiter" => node.compressor.ceiling_db = -3.0 - 0.5 * s,
            "denoiser" => {
                node.denoiser.suppression_db = -18.0 - 2.0 * s;
                node.denoiser.algorithm = if flip { "rnnoise".into() } else { "speex".into() };
                node.denoiser.rnnoise_mix = if flip { 0.6 } else { 1.0 };
            }
            "deEsser" => node.de_esser.threshold_db = -40.0 + 3.0 * s,
            "plosive" => node.plosive.reduction_db = 6.0 + s,
            "expander" => {
                node.expander.threshold_db = -50.0 + 4.0 * s;
                node.expander.ratio = 2.0 + 0.5 * s;
            }
            "gate" => {
                node.expander.hold_ms = 50.0 + 10.0 * s;
                node.expander.range_db = 60.0 - 5.0 * s;
            }
            "voiceColor" => {
                node.voice_color.tone = -0.4 + 0.2 * s;
                node.voice_color.ambience = 0.1 * s;
            }
            "reverb" => {
                node.reverb.mix = 0.2 + 0.1 * s;
                node.reverb.decay_seconds = 1.0 + 0.4 * s;
                node.bypass = step % 4 == 3;
            }
            "echo" => {
                node.echo.time_ms = 40.0 + 15.0 * s;
                node.echo.repeats = 2 + (step % 4) as i32;
                node.echo.mix = 0.3 + 0.1 * s;
            }
            "distortion" => {
                node.distortion.drive_db = 6.0 + 3.0 * s;
                node.distortion.mix = 0.5 + 0.1 * s;
            }
            "alignmentDelay" => node.alignment_delay.samples = 100 + 77 * (step % 6) as u32,
            "stereoWidth" => node.stereo_width = 0.4 + 0.3 * s,
            "stereoPan" => node.pan_position = (-0.5 + 0.25 * s).clamp(-1.0, 1.0),
            "pan" => node.balance = (0.4 - 0.2 * s).clamp(-1.0, 1.0),
            "crossfade" => {
                node.crossfade_position = (0.2 * s).min(1.0);
                node.crossfade_curve = if flip { "linear".into() } else { "equalPower".into() };
            }
            "ducking" => {
                node.ducking.amount_db = 12.0 + 2.0 * s;
                node.ducking.mode = if flip { "dynamic".into() } else { "static".into() };
            }
            "control" => node.muted = step % 3 == 1,
            "microphone" => node.mono = flip,
            _ => {}
        }
    }
    for (index, edge) in graph.edges.iter_mut().enumerate() {
        edge.gain_db = ((step + index) % 7) as f64 - 3.0;
        edge.muted = (step + index) % 11 == 10;
    }
    graph
}

fn signal(quantum: usize, channel: usize) -> Vec<Frame> {
    (0..QUANTUM_FRAMES)
        .map(|i| {
            let t = (quantum * QUANTUM_FRAMES + i) as f32;
            // Speech-like: a decaying burst pattern over a tone, loud enough
            // to drive every dynamics stage.
            let envelope = ((t * 0.0007 + channel as f32).sin() * 0.5 + 0.5).powi(3);
            Frame::new((t * 0.031).sin() * 0.7 * envelope, (t * 0.047 + 1.0).cos() * 0.6 * envelope)
        })
        .collect()
}

fn prepared(plan: &GraphPlan) -> GraphProcessor {
    let mut processor = GraphProcessor::new();
    processor.prepare(plan);
    processor
}

fn assert_same(established: &GraphProcessor, retargeted: &GraphProcessor, plan: &GraphPlan, quantum: usize, step: usize) {
    for (i, node) in plan.nodes.iter().enumerate() {
        for (frame, (a, b)) in established.output(i).iter().zip(retargeted.output(i)).enumerate() {
            assert!(
                a.left.to_bits() == b.left.to_bits() && a.right.to_bits() == b.right.to_bits(),
                "node {} frame {frame} of quantum {quantum} (edit step {step}): adopted {a:?} != retargeted {b:?}",
                node.id
            );
        }
        let (a, b) = (established.meter(i), retargeted.meter(i));
        assert!(
            a.peak.to_bits() == b.peak.to_bits()
                && a.clipped == b.clipped
                && a.gain_reduction_db.to_bits() == b.gain_reduction_db.to_bits()
                && a.detector_db.to_bits() == b.detector_db.to_bits(),
            "node {} meter in quantum {quantum} (edit step {step}): {a:?} != {b:?}",
            node.id
        );
    }
}

fn exercise(graph: &AudioGraph) {
    let mut plan = compile_graph(graph).expect("fixture compiles");
    let mut established = prepared(&plan);
    let mut retargeted = prepared(&plan);
    let sources: Vec<usize> = plan.nodes.iter().enumerate().filter(|(_, n)| n.kind.is_source()).map(|(i, _)| i).collect();
    let mut quantum = 0usize;
    let mut audible = false;
    // Hold times from one quantum (an edit landing mid-ramp, since ramps and
    // EQ crossfades last two quanta) up to long enough to settle.
    let holds = [3usize, 1, 1, 2, 9, 1, 40, 2, 1, 5, 1, 1, 30, 7];
    for (step, hold) in holds.iter().enumerate() {
        if step > 0 {
            let next = compile_graph(&variation(graph, step)).expect("variation compiles");
            assert!(same_indexing(&plan, &next), "the fixture's edits are parameter-only");
            let mut replacement = prepared(&next);
            replacement.adopt(&mut established, &plan, &next, false);
            established = replacement;
            retargeted.retarget(&plan, &next);
            plan = next;
        }
        for _ in 0..*hold {
            let blocks: Vec<Vec<Frame>> = (0..sources.len()).map(|channel| signal(quantum, channel)).collect();
            let mut inputs: Inputs<'_> = [&[]; MAXIMUM_GRAPH_NODES];
            for (block, &index) in blocks.iter().zip(&sources) {
                inputs[index] = block;
            }
            established.process(&plan, &inputs, true);
            retargeted.process(&plan, &inputs, true);
            assert_same(&established, &retargeted, &plan, quantum, step);
            let sink = plan.nodes.iter().position(|n| n.kind.is_sink()).expect("a sink");
            audible |= established.output(sink).iter().any(|f| f.left != 0.0 || f.right != 0.0);
            quantum += 1;
        }
    }
    assert!(audible, "the comparison must not be between two silent graphs");
}

#[test]
fn retarget_matches_adoption_through_every_effect() {
    exercise(&effects_chain());
}

#[test]
fn retarget_matches_adoption_through_routing_and_control() {
    exercise(&routing());
}

/// The sharing rule itself: a reordered or rewired graph is not the same
/// indexing, even with the same nodes.
#[test]
fn same_indexing_rejects_reordering_and_rewiring() {
    let graph = effects_chain();
    let plan = compile_graph(&graph).unwrap();
    assert!(same_indexing(&plan, &compile_graph(&variation(&graph, 3)).unwrap()));
    let mut reordered = graph.clone();
    reordered.nodes.swap(2, 3);
    assert!(!same_indexing(&plan, &compile_graph(&reordered).unwrap()));
    let mut rewired = graph.clone();
    rewired.edges.swap(0, 1);
    assert!(!same_indexing(&plan, &compile_graph(&rewired).unwrap()));
    let mut retyped = graph;
    retyped.nodes[15] = node("gain", "mono");
    assert!(!same_indexing(&plan, &compile_graph(&retyped).unwrap()));
}
