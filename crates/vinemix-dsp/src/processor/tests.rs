//! Unit tests for the graph processor's own state handling. Sample-exact
//! agreement with the 0.5 engine is checked separately by `vinemix-oracle`;
//! the mixer-path allocation guard and retarget parity live in `tests/`.

use super::*;
use vinemix_model::config::{AudioGraph, GraphEdge, GraphNode};
use vinemix_model::plan::compile_graph;

fn node(id: &str, kind: &str) -> GraphNode {
    GraphNode::new(id, kind, id, 0.0, 0.0)
}

fn chain(kinds: &[(&str, &str)]) -> AudioGraph {
    let nodes: Vec<GraphNode> = kinds.iter().map(|(id, kind)| node(id, kind)).collect();
    let edges = kinds.windows(2).enumerate().map(|(i, pair)| GraphEdge::audio(&format!("e{i}"), pair[0].0, pair[1].0)).collect();
    AudioGraph { nodes, edges, ..AudioGraph::default() }
}

fn run(processor: &mut GraphProcessor, plan: &GraphPlan, input: &[Frame]) {
    let mut inputs: Inputs<'_> = [&[]; MAXIMUM_GRAPH_NODES];
    inputs[0] = input;
    processor.process(plan, &inputs, false);
}

fn constant(value: f32) -> Vec<Frame> {
    vec![Frame::new(value, value); QUANTUM_FRAMES]
}

#[test]
fn ramp_reaches_its_target_in_exactly_ten_milliseconds() {
    let mut ramp = Ramp::default();
    ramp.set(1.0);
    assert_eq!(ramp.remaining, RAMP_FRAMES);
    let mut previous = 0.0;
    for step in 1..=RAMP_FRAMES {
        let value = ramp.next();
        assert!(value >= previous, "monotonic at step {step}");
        previous = value;
    }
    assert_eq!(ramp.current, 1.0, "the final step lands exactly on the target");
    assert_eq!(ramp.remaining, 0);
    assert_eq!(ramp.next(), 1.0, "a settled ramp stays put");
    // Setting the same target again does not restart the ramp.
    ramp.set(1.0);
    assert_eq!(ramp.remaining, 0);
    // A new target mid-ramp restarts from the current value, not from zero.
    ramp.set(0.0);
    for _ in 0..100 {
        ramp.next();
    }
    let midway = ramp.current;
    ramp.set(1.0);
    assert_eq!(ramp.current, midway);
    assert_eq!(ramp.remaining, RAMP_FRAMES);
}

#[test]
fn pan_and_crossfade_weights_are_exact_at_their_ends() {
    assert_eq!(pan_weights(-1.0), [1.0, 0.0]);
    assert_eq!(pan_weights(1.0), [0.0, 1.0]);
    let centre = pan_weights(0.0);
    assert!((centre[0] - centre[1]).abs() < 1e-12 && (centre[0] - std::f64::consts::FRAC_1_SQRT_2).abs() < 1e-12);
    // Out-of-range positions clamp instead of wrapping.
    assert_eq!(pan_weights(7.0), pan_weights(1.0));
    for curve in [0.0, 1.0] {
        assert_eq!(crossfade_weights(0.0, curve), [1.0, 0.0]);
        assert_eq!(crossfade_weights(1.0, curve), [0.0, 1.0]);
    }
    assert_eq!(crossfade_weights(0.5, 0.0), [0.5, 0.5], "linear midpoint");
    let equal_power = crossfade_weights(0.5, 1.0);
    assert!((equal_power[0] * equal_power[0] + equal_power[1] * equal_power[1] - 1.0).abs() < 1e-12);
}

#[test]
fn biquad_flushes_denormal_state_to_zero() {
    let identity = Biquad { b0: 1.0, b1: 0.0, b2: 0.0, a1: 0.0, a2: 0.0 };
    let mut state = FilterState { z1: 1e-31, z2: -1e-31 };
    assert_eq!(biquad(0.0, &identity, &mut state), 1e-31);
    assert_eq!((state.z1, state.z2), (0.0, 0.0));
}

#[test]
fn settled_gain_passes_audio_through_and_meters_it() {
    let plan = compile_graph(&chain(&[("mic", "microphone"), ("gain", "gain"), ("out", "output")])).unwrap();
    let mut processor = GraphProcessor::new();
    processor.prepare(&plan);
    let input = constant(0.25);
    // Ramps from silence take two quanta; the third is settled.
    for _ in 0..3 {
        run(&mut processor, &plan, &input);
    }
    for index in 0..3 {
        assert!(processor.output(index).iter().all(|f| f.left == 0.25 && f.right == 0.25), "node {index}");
        assert_eq!(processor.meter(index).peak, 0.25);
        assert!(!processor.meter(index).clipped);
    }
}

#[test]
fn non_finite_input_is_contained_and_the_graph_recovers() {
    let plan = compile_graph(&chain(&[("mic", "microphone"), ("eq", "eq"), ("out", "output")])).unwrap();
    let mut processor = GraphProcessor::new();
    processor.prepare(&plan);
    let mut poisoned = constant(0.1);
    poisoned[7] = Frame::new(f32::NAN, f32::INFINITY);
    poisoned[8] = Frame::new(f32::NEG_INFINITY, 0.2);
    run(&mut processor, &plan, &poisoned);
    for index in 0..3 {
        assert!(processor.output(index).iter().all(|f| f.left.is_finite() && f.right.is_finite()), "node {index} emitted a non-finite sample");
    }
    // Clean input afterwards produces clean, non-silent output again.
    for _ in 0..4 {
        run(&mut processor, &plan, &constant(0.1));
    }
    let out = processor.output(2);
    assert!(out.iter().all(|f| f.left.is_finite()) && out.iter().any(|f| f.left != 0.0));
}

#[test]
fn sink_clipping_is_deferred_only_when_asked() {
    let mut gain = node("gain", "gain");
    gain.gain_db = 12.0;
    let graph = AudioGraph {
        nodes: vec![node("mic", "microphone"), gain, node("out", "output")],
        edges: vec![GraphEdge::audio("a", "mic", "gain"), GraphEdge::audio("b", "gain", "out")],
        ..AudioGraph::default()
    };
    let plan = compile_graph(&graph).unwrap();
    for (defer, expect_clamped) in [(false, true), (true, false)] {
        let mut processor = GraphProcessor::new();
        processor.prepare(&plan);
        let input = constant(0.9);
        let mut inputs: Inputs<'_> = [&[]; MAXIMUM_GRAPH_NODES];
        inputs[0] = &input;
        for _ in 0..4 {
            processor.process(&plan, &inputs, defer);
        }
        let peak = processor.output(2).iter().fold(0.0f32, |a, f| a.max(f.left.abs()));
        assert_eq!(peak <= 1.0, expect_clamped, "defer={defer}: sink peak {peak}");
        assert!(processor.meter(2).clipped, "the meter reports the overload either way");
    }
}

#[test]
fn reset_returns_to_silence_and_initial_ramps() {
    let plan = compile_graph(&chain(&[("mic", "microphone"), ("rv", "reverb"), ("out", "output")])).unwrap();
    let mut processor = GraphProcessor::new();
    processor.prepare(&plan);
    for _ in 0..20 {
        run(&mut processor, &plan, &constant(0.5));
    }
    assert!(processor.output(2).iter().any(|f| f.left != 0.0));
    processor.reset();
    assert!(processor.output(2).iter().all(|f| *f == Frame::SILENT));
    assert_eq!(processor.node_ramps[0], Ramp::default());
    // Silence in, silence out: no reverb tail survives a reset.
    run(&mut processor, &plan, &constant(0.0));
    assert!(processor.output(2).iter().all(|f| f.left == 0.0 && f.right == 0.0));
}

#[test]
fn adoption_matches_nodes_by_identity_not_position() {
    let before = compile_graph(&chain(&[("mic", "microphone"), ("a", "gain"), ("out", "output")])).unwrap();
    // The same nodes plus one inserted first: every index shifts.
    let mut graph = chain(&[("mic", "microphone"), ("a", "gain"), ("out", "output")]);
    graph.nodes.insert(0, node("extra", "microphone"));
    let after = compile_graph(&graph).unwrap();
    let pairs: Vec<(usize, usize)> = matching_nodes(&before, &after).collect();
    assert_eq!(pairs, [(1, 0), (2, 1), (3, 2)]);
    let mut old = GraphProcessor::new();
    old.prepare(&before);
    for _ in 0..3 {
        run(&mut old, &before, &constant(0.25));
    }
    let settled = old.node_ramps[1];
    assert_eq!(settled.current, 1.0);
    let mut new = GraphProcessor::new();
    new.prepare(&after);
    new.adopt(&mut old, &before, &after, true);
    assert_eq!(new.node_ramps[2], settled, "the gain node kept its ramp at its new index");
    assert_eq!(new.node_ramps[0], Ramp::default(), "the new node starts from silence");
    // A node whose kind changed under the same id is a different node.
    let mut retyped = chain(&[("mic", "microphone"), ("a", "mono"), ("out", "output")]);
    retyped.nodes[1].id = "a".into();
    let retyped = compile_graph(&retyped).unwrap();
    assert_eq!(matching_nodes(&before, &retyped).count(), 2);
}

#[test]
fn retarget_starts_an_eq_crossfade_only_when_coefficients_changed() {
    let graph = chain(&[("mic", "microphone"), ("eq", "eq"), ("out", "output")]);
    let before = compile_graph(&graph).unwrap();
    let mut edited = graph.clone();
    edited.nodes[1].eq_bands[0].gain_db = 6.0;
    let after = compile_graph(&edited).unwrap();
    let mut moved = graph.clone();
    moved.nodes[1].gain_db = -3.0;
    let level_only = compile_graph(&moved).unwrap();
    let mut processor = GraphProcessor::new();
    processor.prepare(&before);
    for _ in 0..4 {
        run(&mut processor, &before, &constant(0.3));
    }
    processor.retarget(&before, &level_only);
    assert_eq!(processor.effects[1].eq_transition, 0, "a level edit does not disturb the filters");
    let filters = processor.effects[1].filters;
    processor.retarget(&before, &after);
    assert_eq!(processor.effects[1].eq_transition, 480);
    assert_eq!(processor.effects[1].previous_eq_count, before.nodes[1].eq.len());
    assert_eq!(processor.effects[1].previous_filters, filters, "the old filter state keeps running during the crossfade");
    assert_eq!(processor.effects[1].filters, Effects::INITIAL.filters);
}
