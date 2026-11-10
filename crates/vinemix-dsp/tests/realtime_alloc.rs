//! Realtime allocation guard for the mixer path (RUST-REV-001 / ISSUE-031).
//!
//! Every `GraphProcessor` call the engine makes on the mixer thread —
//! `process` (which also configures effects), `adopt` and `reset` — must
//! neither allocate nor free. A counting global allocator observes only the
//! calling thread while a guard is armed, so parallel tests cannot disturb the
//! count and nothing outside the guarded call is measured.
//!
//! This is evidence for the fixtures below (every advanced effect kind, first
//! use, steady state, parameter edits, adoption and reset). It is not a proof
//! for graphs or call sequences that are not exercised here.

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;

use vinemix_dsp::{GraphProcessor, Inputs};
use vinemix_model::config::{AudioGraph, GraphEdge, GraphNode};
use vinemix_model::plan::{compile_graph, GraphPlan};
use vinemix_rt::{Frame, QUANTUM_FRAMES};

struct Counting;

thread_local! {
    static ARMED: Cell<bool> = const { Cell::new(false) };
    static ALLOCATIONS: Cell<u64> = const { Cell::new(0) };
    static FREES: Cell<u64> = const { Cell::new(0) };
    static BYTES: Cell<u64> = const { Cell::new(0) };
}

fn count_bytes(size: usize) {
    let _ = ARMED.try_with(|armed| {
        if armed.get() {
            let _ = BYTES.try_with(|value| value.set(value.get() + size as u64));
        }
    });
}

fn count(counter: &'static std::thread::LocalKey<Cell<u64>>) {
    // `try_with`: the allocator also runs during thread teardown.
    let _ = ARMED.try_with(|armed| {
        if armed.get() {
            let _ = counter.try_with(|value| value.set(value.get() + 1));
        }
    });
}

// SAFETY: every method defers to `System`; the counters are plain
// thread-local cells with constant initialisers, so counting cannot allocate.
unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        count(&ALLOCATIONS);
        count_bytes(layout.size());
        // SAFETY: forwarded unchanged.
        unsafe { System.alloc(layout) }
    }

    unsafe fn dealloc(&self, pointer: *mut u8, layout: Layout) {
        count(&FREES);
        // SAFETY: forwarded unchanged.
        unsafe { System.dealloc(pointer, layout) }
    }

    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        count(&ALLOCATIONS);
        count_bytes(layout.size());
        // SAFETY: forwarded unchanged.
        unsafe { System.alloc_zeroed(layout) }
    }

    unsafe fn realloc(&self, pointer: *mut u8, layout: Layout, size: usize) -> *mut u8 {
        count(&ALLOCATIONS);
        count(&FREES);
        count_bytes(size);
        // SAFETY: forwarded unchanged.
        unsafe { System.realloc(pointer, layout, size) }
    }
}

#[global_allocator]
static GLOBAL: Counting = Counting;

/// Runs `work` and returns `(allocations, frees)` made by this thread.
fn measured(work: impl FnOnce()) -> (u64, u64) {
    ALLOCATIONS.with(|value| value.set(0));
    FREES.with(|value| value.set(0));
    BYTES.with(|value| value.set(0));
    ARMED.with(|armed| armed.set(true));
    work();
    ARMED.with(|armed| armed.set(false));
    (ALLOCATIONS.with(Cell::get), FREES.with(Cell::get))
}

#[track_caller]
fn assert_silent(label: &str, work: impl FnOnce()) {
    let (allocations, frees) = measured(work);
    assert_eq!((allocations, frees), (0, 0), "{label}: {allocations} allocation(s), {frees} free(s) on the mixer path");
}

fn node(id: &str, kind: &str) -> GraphNode {
    GraphNode::new(id, kind, id, 0.0, 0.0)
}

/// A chain through every kind with auxiliary effect state.
fn every_effect() -> AudioGraph {
    let mut gate = node("gt", "gate");
    gate.expander.mode = "gate".into();
    let nodes = vec![
        node("mic", "microphone"),
        node("eq", "eq"),
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
        node("out", "output"),
    ];
    let ids: Vec<String> = nodes.iter().map(|n| n.id.clone()).collect();
    let edges =
        ids.windows(2).enumerate().map(|(i, pair)| GraphEdge::audio(&format!("e{i}"), &pair[0], &pair[1])).collect();
    AudioGraph { nodes, edges, ..AudioGraph::default() }
}

/// The review's minimal fixture: microphone → reverb → output.
fn reverb_only() -> AudioGraph {
    let nodes = vec![node("mic", "microphone"), node("rv", "reverb"), node("out", "output")];
    let edges = vec![GraphEdge::audio("a", "mic", "rv"), GraphEdge::audio("b", "rv", "out")];
    AudioGraph { nodes, edges, ..AudioGraph::default() }
}

/// Every parameter family changed at once, including both mode switches.
fn edited(mut graph: AudioGraph) -> AudioGraph {
    for node in &mut graph.nodes {
        match node.kind.as_str() {
            "denoiser" => {
                node.denoiser.algorithm = "rnnoise".into();
                node.denoiser.suppression_db = -30.0;
                node.denoiser.rnnoise_mix = 0.5;
            }
            "deEsser" => node.de_esser.threshold_db = -35.0,
            "plosive" => node.plosive.threshold_db = -35.0,
            "expander" => node.expander.threshold_db = -55.0,
            "gate" => node.expander.hold_ms = 80.0,
            "voiceColor" => {
                node.voice_color.tone = 0.5;
                node.voice_color.ambience = 0.6;
            }
            "reverb" => {
                node.reverb.mix = 0.6;
                node.reverb.decay_seconds = 3.0;
            }
            "echo" => {
                node.echo.time_ms = 120.0;
                node.echo.repeats = 3;
            }
            "distortion" => node.distortion.drive_db = 24.0,
            "alignmentDelay" => node.alignment_delay.samples = 480,
            "eq" => node.eq_bands[0].gain_db = 5.0,
            "compressor" => node.compressor.threshold_db = -28.0,
            _ => {}
        }
    }
    graph
}

fn signal(seed: usize) -> Vec<Frame> {
    (0..QUANTUM_FRAMES)
        .map(|i| {
            let phase = (seed * QUANTUM_FRAMES + i) as f32 * 0.031;
            Frame::new(phase.sin() * 0.4, (phase * 1.3).cos() * 0.3)
        })
        .collect()
}

fn prepared(plan: &GraphPlan) -> GraphProcessor {
    let mut processor = GraphProcessor::new();
    processor.prepare(plan);
    processor
}

fn exercise(graph: AudioGraph) {
    let plan = compile_graph(&graph).expect("fixture compiles");
    let edited_plan = compile_graph(&edited(graph)).expect("edited fixture compiles");
    let blocks: Vec<Vec<Frame>> = (0..8).map(signal).collect();
    let mut inputs: Inputs<'_> = [&[]; vinemix_model::limits::MAXIMUM_GRAPH_NODES];

    let mut processor = prepared(&plan);
    inputs[0] = &blocks[0];
    assert_silent("first process after prepare", || processor.process(&plan, &inputs, false));
    inputs[0] = &blocks[1];
    assert_silent("steady process", || processor.process(&plan, &inputs, false));

    // A parameter edit that reaches the same processor (reconfiguration).
    inputs[0] = &blocks[2];
    assert_silent("process with edited settings", || processor.process(&edited_plan, &inputs, false));
    inputs[0] = &blocks[3];
    assert_silent("process returning to the original settings", || processor.process(&plan, &inputs, false));

    // The engine's edit path: a replacement prepared off the mixer thread,
    // adopted and then processed on it.
    let mut unchanged = prepared(&plan);
    assert_silent("adopt unchanged plan", || unchanged.adopt(&mut processor, &plan, &plan, false));
    inputs[0] = &blocks[4];
    assert_silent("process after unchanged adoption", || unchanged.process(&plan, &inputs, false));

    let mut replacement = prepared(&edited_plan);
    assert_silent("adopt edited plan", || replacement.adopt(&mut unchanged, &plan, &edited_plan, true));
    inputs[0] = &blocks[5];
    assert_silent("process after edited adoption", || replacement.process(&edited_plan, &inputs, false));

    // The parameter-edit path the engine uses when nothing but settings
    // changed: the running processor is retargeted in place.
    assert_silent("retarget to the original plan", || replacement.retarget(&edited_plan, &plan));
    inputs[0] = &blocks[6];
    assert_silent("process after retarget", || replacement.process(&plan, &inputs, false));
    assert_silent("retarget to the edited plan", || replacement.retarget(&plan, &edited_plan));

    assert_silent("reset", || replacement.reset());
    inputs[0] = &blocks[6];
    assert_silent("process after reset", || replacement.process(&edited_plan, &inputs, false));
    inputs[0] = &blocks[7];
    assert_silent("steady process after reset", || replacement.process(&edited_plan, &inputs, false));
}

#[test]
fn reverb_fixture_never_allocates_on_the_mixer_path() {
    exercise(reverb_only());
}

#[test]
fn every_effect_kind_never_allocates_on_the_mixer_path() {
    exercise(every_effect());
}

/// Cost of one parameter edit on each path, for RUST-REV-008. Run with
/// `cargo test -p vinemix-dsp --release --test realtime_alloc -- --ignored --nocapture`.
///
/// "Established" is what every parameter edit used to do: build a processor
/// (control thread), then adopt the running one into it (mixer thread).
/// "Retarget" is the new parameter-edit path (mixer thread only).
#[test]
#[ignore = "benchmark; prints timings"]
fn parameter_edit_cost() {
    use std::time::{Duration, Instant};
    let median = |mut samples: Vec<Duration>| {
        samples.sort();
        (samples[samples.len() / 2], samples[samples.len() * 99 / 100], *samples.last().unwrap())
    };
    for (name, graph) in [("reverb only", reverb_only()), ("every effect", every_effect())] {
        let plan = compile_graph(&graph).unwrap();
        let edited_plan = compile_graph(&edited(graph)).unwrap();
        let block = signal(0);
        let mut inputs: Inputs<'_> = [&[]; vinemix_model::limits::MAXIMUM_GRAPH_NODES];
        inputs[0] = &block;
        const ROUNDS: usize = 200;
        let (mut build, mut adopt, mut retarget, mut first_adopted, mut first_retargeted) = (Vec::new(), Vec::new(), Vec::new(), Vec::new(), Vec::new());
        let mut build_bytes = 0u64;
        let mut running = prepared(&plan);
        running.process(&plan, &inputs, true);
        let (mut from, mut to) = (&plan, &edited_plan);
        for _ in 0..ROUNDS {
            let started = Instant::now();
            let mut replacement = None;
            let (_, _) = measured(|| replacement = Some(prepared(to)));
            build.push(started.elapsed());
            build_bytes = BYTES.with(Cell::get);
            let mut replacement = replacement.unwrap();
            let started = Instant::now();
            replacement.adopt(&mut running, from, to, false);
            adopt.push(started.elapsed());
            let started = Instant::now();
            replacement.process(to, &inputs, true);
            first_adopted.push(started.elapsed());
            running = replacement;
            std::mem::swap(&mut from, &mut to);
        }
        for _ in 0..ROUNDS {
            let started = Instant::now();
            running.retarget(from, to);
            retarget.push(started.elapsed());
            let started = Instant::now();
            running.process(to, &inputs, true);
            first_retargeted.push(started.elapsed());
            std::mem::swap(&mut from, &mut to);
        }
        println!("--- {name} ({} nodes), {ROUNDS} edits each, median / p99 / max", plan.nodes.len());
        println!("established: build new processor (control thread) {:?}, {} bytes allocated per edit", median(build), build_bytes);
        println!("established: adopt on the mixer thread            {:?}", median(adopt));
        println!("established: first quantum after adoption         {:?}", median(first_adopted));
        println!("retarget:    on the mixer thread                  {:?}, 0 bytes allocated", median(retarget));
        println!("retarget:    first quantum after retarget         {:?}", median(first_retargeted));
    }
}

/// The guard itself must be able to fail: a counted allocation is reported.
#[test]
fn guard_detects_an_allocation() {
    let (allocations, frees) = measured(|| drop(std::hint::black_box(String::from("probe"))));
    assert_eq!((allocations, frees), (1, 1));
}
