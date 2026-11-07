//! The per-quantum graph processor (`GraphProcessor` in `graph.cpp`).
//!
//! `process` runs on the audio thread: it walks the compiled plan in
//! topological order, never allocates, locks or searches, and keeps every
//! level change on a 10 ms ramp. Arithmetic order and precision follow the
//! 0.5 engine so the two produce the same samples.

use std::f64::consts::PI;

use vinemix_model::limits::{MAXIMUM_EQ_BANDS, MAXIMUM_GRAPH_EDGES, MAXIMUM_GRAPH_NODES};
use vinemix_model::plan::{Biquad, GraphPlan, PlanEdge};
use vinemix_model::{GraphKind, GraphPort};
use vinemix_rt::{Frame, QUANTUM_FRAMES};

use crate::effects::delay::ECHO_HISTORY_FRAMES;
use crate::effects::AdvancedEffect;
use crate::util::{bounded, clean_f32, linear};

const RAMP_FRAMES: u32 = 480;

/// A linear 10 ms gain ramp in single precision.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Ramp {
    pub current: f32,
    pub target: f32,
    pub step: f32,
    pub remaining: u32,
}

impl Ramp {
    #[inline]
    pub fn set(&mut self, value: f32) {
        if value == self.target {
            return;
        }
        self.target = value;
        self.step = (self.target - self.current) / RAMP_FRAMES as f32;
        self.remaining = RAMP_FRAMES;
    }

    #[inline]
    pub fn next(&mut self) -> f32 {
        if self.remaining != 0 {
            self.remaining -= 1;
            if self.remaining == 0 {
                self.current = self.target;
            } else {
                self.current += self.step;
            }
        }
        self.current
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq)]
struct FilterState {
    z1: f64,
    z2: f64,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct Effects {
    filters: [[FilterState; 2]; MAXIMUM_EQ_BANDS],
    previous_filters: [[FilterState; 2]; MAXIMUM_EQ_BANDS],
    previous_eq: [Biquad; MAXIMUM_EQ_BANDS],
    previous_eq_count: usize,
    eq_transition: u32,
    compression_gain: f64,
    limiter_gain: f64,
    ducking_gain: f64,
    ducking_detector: f64,
    ducking_active: bool,
    wet: Ramp,
    pan_left: Ramp,
    pan_right: Ramp,
    makeup: Ramp,
    mono: Ramp,
    limit_mix: Ramp,
    width: Ramp,
    position: Ramp,
    fade_position: Ramp,
    fade_curve: Ramp,
}

impl Effects {
    const INITIAL: Self = Self {
        filters: [[FilterState { z1: 0.0, z2: 0.0 }; 2]; MAXIMUM_EQ_BANDS],
        previous_filters: [[FilterState { z1: 0.0, z2: 0.0 }; 2]; MAXIMUM_EQ_BANDS],
        previous_eq: [Biquad { b0: 1.0, b1: 0.0, b2: 0.0, a1: 0.0, a2: 0.0 }; MAXIMUM_EQ_BANDS],
        previous_eq_count: 0,
        eq_transition: 0,
        compression_gain: 1.0,
        limiter_gain: 1.0,
        ducking_gain: 1.0,
        ducking_detector: 0.0,
        ducking_active: false,
        wet: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        pan_left: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        pan_right: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        makeup: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        mono: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        limit_mix: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        width: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        position: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        fade_position: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
        fade_curve: Ramp { current: 0.0, target: 0.0, step: 0.0, remaining: 0 },
    };
}

/// Per-node meter for one quantum.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Meter {
    pub peak: f32,
    pub peak_left: f32,
    pub peak_right: f32,
    pub clipped: bool,
    /// Actual dynamics state, independent of output level or make-up gain.
    pub gain_reduction_db: f32,
    pub detector_db: f32,
}

impl Default for Meter {
    fn default() -> Self {
        Self { peak: 0.0, peak_left: 0.0, peak_right: 0.0, clipped: false, gain_reduction_db: 0.0, detector_db: -96.0 }
    }
}

type Block = [Frame; QUANTUM_FRAMES];
const SILENT_BLOCK: Block = [Frame::SILENT; QUANTUM_FRAMES];

/// Capture input for each node index (empty for non-sources).
pub type Inputs<'a> = [&'a [Frame]; MAXIMUM_GRAPH_NODES];

fn pan_weights(position: f64) -> [f64; 2] {
    let position = position.clamp(-1.0, 1.0);
    let angle = (position + 1.0) * PI / 4.0;
    [if position == 1.0 { 0.0 } else { angle.cos() }, if position == -1.0 { 0.0 } else { angle.sin() }]
}

fn crossfade_weights(position: f64, curve: f64) -> [f64; 2] {
    let position = position.clamp(0.0, 1.0);
    [
        if position == 1.0 { 0.0 } else { (1.0 - position) * (1.0 - curve) + (position * PI / 2.0).cos() * curve },
        if position == 0.0 { 0.0 } else { position * (1.0 - curve) + (position * PI / 2.0).sin() * curve },
    ]
}

#[inline]
fn biquad(sample: f64, c: &Biquad, state: &mut FilterState) -> f64 {
    let output = c.b0 * sample + state.z1;
    state.z1 = c.b1 * sample - c.a1 * output + state.z2;
    state.z2 = c.b2 * sample - c.a2 * output;
    // Denormal tails must not become an unpredictable CPU load.
    if state.z1.abs() < 1e-30 {
        state.z1 = 0.0;
    }
    if state.z2.abs() < 1e-30 {
        state.z2 = 0.0;
    }
    output
}

fn is_dynamics(kind: GraphKind) -> bool {
    kind.has_dynamics_meter()
}

pub struct GraphProcessor {
    buffers: Box<[Block; MAXIMUM_GRAPH_NODES]>,
    wet_buffers: Box<[Block; MAXIMUM_GRAPH_NODES]>,
    node_ramps: [Ramp; MAXIMUM_GRAPH_NODES],
    edge_ramps: [Ramp; MAXIMUM_GRAPH_EDGES],
    meters: [Meter; MAXIMUM_GRAPH_NODES],
    effects: Box<[Effects; MAXIMUM_GRAPH_NODES]>,
    advanced: Vec<Option<AdvancedEffect>>,
}

impl std::fmt::Debug for GraphProcessor {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("GraphProcessor").finish_non_exhaustive()
    }
}

impl Default for GraphProcessor {
    fn default() -> Self {
        Self::new()
    }
}

impl GraphProcessor {
    pub fn new() -> Self {
        Self {
            buffers: Box::new([SILENT_BLOCK; MAXIMUM_GRAPH_NODES]),
            wet_buffers: Box::new([SILENT_BLOCK; MAXIMUM_GRAPH_NODES]),
            node_ramps: [Ramp::default(); MAXIMUM_GRAPH_NODES],
            edge_ramps: [Ramp::default(); MAXIMUM_GRAPH_EDGES],
            meters: [Meter::default(); MAXIMUM_GRAPH_NODES],
            effects: Box::new([Effects::INITIAL; MAXIMUM_GRAPH_NODES]),
            advanced: (0..MAXIMUM_GRAPH_NODES).map(|_| None).collect(),
        }
    }

    /// Builds effect state (FFT plans, delay lines) before publication, never
    /// on the audio thread. Ordinary graphs need no auxiliary allocation.
    pub fn prepare(&mut self, plan: &GraphPlan) {
        for (i, slot) in self.advanced.iter_mut().enumerate() {
            *slot = plan.nodes.get(i).and_then(|node| AdvancedEffect::new(node.kind));
        }
    }

    pub fn output(&self, index: usize) -> &[Frame] {
        &self.buffers[index]
    }

    pub fn meter(&self, index: usize) -> &Meter {
        &self.meters[index]
    }

    /// Bytes of Echo history a plan needs (bounded by the engine).
    pub fn echo_history_bytes(plan: &GraphPlan) -> usize {
        plan.nodes.iter().filter(|node| node.kind == GraphKind::Echo).count()
            * ECHO_HISTORY_FRAMES
            * std::mem::size_of::<Frame>()
    }

    #[inline]
    fn routed_output(&self, edge: &PlanEdge, frame: usize) -> Frame {
        if edge.from_port == GraphPort::Wet {
            return self.wet_buffers[edge.from][frame];
        }
        let sample = self.buffers[edge.from][frame];
        match edge.from_port {
            GraphPort::Left => Frame::new(sample.left, sample.left),
            GraphPort::Right => Frame::new(sample.right, sample.right),
            _ => sample,
        }
    }

    /// Processes one quantum. With `defer_sink_clipping` the engine clamps
    /// only after summing every logical output for a physical endpoint.
    pub fn process(&mut self, plan: &GraphPlan, inputs: &Inputs<'_>, defer_sink_clipping: bool) {
        self.meters = [Meter::default(); MAXIMUM_GRAPH_NODES];
        for (i, node) in plan.nodes.iter().enumerate() {
            // An intermediate bypass is transparent after incoming cable
            // weights: skip its level as well as its DSP. Saved mute flags
            // stay authoritative until an explicit user edit.
            let bypass = node.kind.is_processor() && node.bypass;
            let mut gain = if bypass { 0.0f32 } else { node.gain_db };
            let mut multiplier = 1.0f32;
            let mut muted = node.muted;
            if let Some(control_edge) = node.control_edge {
                let edge = &plan.edges[control_edge];
                let control = &plan.nodes[edge.from];
                if !edge.muted {
                    if !bypass && !control.bypass {
                        if control.control_multiplier < 0.0 {
                            gain += control.gain_db + edge.gain_db;
                        } else {
                            gain += edge.gain_db;
                            multiplier = control.control_multiplier;
                        }
                    }
                    muted = muted || control.muted;
                }
            }
            self.node_ramps[i].set(if muted { 0.0 } else { linear(gain) * multiplier });
            let effect = &mut self.effects[i];
            effect.wet.set(if node.bypass { 0.0 } else { 1.0 });
            effect.pan_left.set(node.pan_left);
            effect.pan_right.set(node.pan_right);
            effect.makeup.set(node.makeup as f32);
            effect.mono.set(if node.kind == GraphKind::Microphone && node.mono { 1.0 } else { 0.0 });
            effect.limit_mix.set(if node.limit { 1.0 } else { 0.0 });
            effect.width.set(node.stereo_width);
            effect.position.set(node.position);
            effect.fade_position.set(node.fade_position);
            effect.fade_curve.set(if node.equal_power { 1.0 } else { 0.0 });
            if let Some(advanced) = &mut self.advanced[i] {
                advanced.configure(node);
            }
        }
        for (i, edge) in plan.edges.iter().enumerate() {
            self.edge_ramps[i].set(if edge.muted { 0.0 } else { linear(edge.gain_db) });
        }
        for &i in &plan.order {
            self.process_node(plan, i, inputs, defer_sink_clipping);
        }
    }

    fn process_node(&mut self, plan: &GraphPlan, i: usize, inputs: &Inputs<'_>, defer_sink_clipping: bool) {
        let node = &plan.nodes[i];
        let kind = node.kind;
        let source = kind.is_source();
        let sink = kind.is_sink();
        let filter = kind.is_filter();
        let compressor = kind.is_compressor();
        let dynamics = is_dynamics(kind);
        let mut dynamics_audible = dynamics && !node.bypass && !node.muted;
        if let Some(control_edge) = node.control_edge {
            let edge = &plan.edges[control_edge];
            let control = &plan.nodes[edge.from];
            if !edge.muted && (control.muted || (!control.bypass && control.control_multiplier == 0.0)) {
                dynamics_audible = false;
            }
        }
        let mut minimum_compression_gain = 1.0f64;
        let mut detector_peak = 0.0f64;
        // Settled controls use the same weights for the whole quantum:
        // sample-accurate ramps while editing, no per-sample trigonometry once
        // Pan or Crossfade stops moving.
        let settled_pan = kind == GraphKind::StereoPan && self.effects[i].position.remaining == 0;
        let settled_fade = kind == GraphKind::Crossfade
            && self.effects[i].fade_position.remaining == 0
            && self.effects[i].fade_curve.remaining == 0;
        let settled_weights = if settled_pan {
            pan_weights(f64::from(self.effects[i].position.current))
        } else if settled_fade {
            crossfade_weights(f64::from(self.effects[i].fade_position.current), f64::from(self.effects[i].fade_curve.current))
        } else {
            [0.0; 2]
        };
        let input = inputs[i];
        let clip_sink = sink && !defer_sink_clipping;
        let ceiling = node.ceiling;
        // A round-to-nearest float conversion must not round above the
        // requested real-valued sample ceiling.
        let mut safe_ceiling = ceiling as f32;
        if f64::from(safe_ceiling) > ceiling {
            safe_ceiling = safe_ceiling.next_down();
        }
        for frame in 0..QUANTUM_FRAMES {
            let (mut left, mut right, mut b_left, mut b_right) = (0.0f64, 0.0f64, 0.0f64, 0.0f64);
            let mut effect_wet = Frame::SILENT;
            if source && frame < input.len() {
                left = f64::from(clean_f32(input[frame].left));
                right = f64::from(clean_f32(input[frame].right));
            }
            for &edge_index in &node.incoming {
                let gain = self.edge_ramps[edge_index].next();
                let route = &plan.edges[edge_index];
                let sample = self.routed_output(route, frame);
                if kind == GraphKind::StereoCombine {
                    let channel = (f64::from(sample.left) + f64::from(sample.right)) * 0.5 * f64::from(gain);
                    if route.to_port == GraphPort::Left {
                        left += channel;
                    } else {
                        right += channel;
                    }
                } else if kind == GraphKind::Crossfade && route.to_port == GraphPort::B {
                    b_left += f64::from(sample.left) * f64::from(gain);
                    b_right += f64::from(sample.right) * f64::from(gain);
                } else {
                    left += f64::from(sample.left) * f64::from(gain);
                    right += f64::from(sample.right) * f64::from(gain);
                }
            }
            let effect = &mut self.effects[i];
            let limit_mix = f64::from(effect.limit_mix.next());
            // A source control, independent of the device's channel selector;
            // keeping it in DSP avoids reopening the stream.
            let mono = f64::from(effect.mono.next());
            let average = (left + right) * 0.5;
            left += (average - left) * mono;
            right += (average - right) * mono;
            let mut dry_left = left;
            let mut dry_right = right;
            if filter {
                let mut old_left = left;
                let mut old_right = right;
                for (band, coefficient) in node.eq.iter().enumerate() {
                    left = biquad(left, coefficient, &mut effect.filters[band][0]);
                    right = biquad(right, coefficient, &mut effect.filters[band][1]);
                }
                if effect.eq_transition != 0 {
                    for band in 0..effect.previous_eq_count {
                        let coefficient = effect.previous_eq[band];
                        old_left = biquad(old_left, &coefficient, &mut effect.previous_filters[band][0]);
                        old_right = biquad(old_right, &coefficient, &mut effect.previous_filters[band][1]);
                    }
                    effect.eq_transition -= 1;
                    let mix = 1.0 - f64::from(effect.eq_transition) / 480.0;
                    left = old_left * (1.0 - mix) + left * mix;
                    right = old_right * (1.0 - mix) + right * mix;
                }
            } else if compressor {
                let peak = left.abs().max(right.abs());
                if !node.limit {
                    detector_peak = detector_peak.max(peak);
                }
                let makeup = f64::from(effect.makeup.next());
                // The established compressor transfer and envelope, driven by
                // its own stereo programme.
                let target = if peak > node.threshold { (peak / node.threshold).powf(1.0 / node.ratio - 1.0) } else { 1.0 };
                let coefficient = if target < effect.compression_gain { node.attack } else { node.release };
                effect.compression_gain = target + coefficient * (effect.compression_gain - target);
                let amount = if limit_mix == 0.0 {
                    effect.compression_gain * makeup
                } else {
                    1.0 + (effect.compression_gain * makeup - 1.0) * (1.0 - limit_mix)
                };
                left *= amount;
                right *= amount;
            } else if kind == GraphKind::Ducking {
                let mut trigger_peak = 0.0f64;
                if let Some(edge_index) = node.sidechain_edge {
                    let detector_gain = self.edge_ramps[edge_index].next();
                    let detector = self.routed_output(&plan.edges[edge_index], frame);
                    trigger_peak = f64::from(detector.left).abs().max(f64::from(detector.right).abs())
                        * f64::from(detector_gain);
                }
                let effect = &mut self.effects[i];
                // Detector-only audio never joins the programme. Fixed 5 ms
                // rise / 50 ms fall and a 3 dB closing band avoid chatter.
                const DETECTOR_ATTACK: f64 = 0.995_842_001_845_110_0; // exp(-1 / (0.005 * 48000))
                const DETECTOR_RELEASE: f64 = 0.999_583_420_126_833_8; // exp(-1 / (0.050 * 48000))
                let detector_coefficient =
                    if trigger_peak > effect.ducking_detector { DETECTOR_ATTACK } else { DETECTOR_RELEASE };
                effect.ducking_detector = trigger_peak + detector_coefficient * (effect.ducking_detector - trigger_peak);
                if effect.ducking_detector < 1e-30 {
                    effect.ducking_detector = 0.0;
                }
                if node.sidechain_edge.is_none() {
                    effect.ducking_active = false;
                } else if effect.ducking_detector >= node.threshold {
                    effect.ducking_active = true;
                } else if effect.ducking_detector < node.ducking_close_threshold {
                    effect.ducking_active = false;
                }
                // Dynamic ducking uses the compressor curve on the raw trigger;
                // the Static detector stays warm in both modes and the shared
                // gain envelope keeps mode changes continuous.
                let target = if node.dynamic_ducking {
                    if trigger_peak > node.threshold {
                        (trigger_peak / node.threshold).powf(1.0 / node.ratio - 1.0)
                    } else {
                        1.0
                    }
                } else if effect.ducking_active {
                    node.ducking_amount
                } else {
                    1.0
                };
                let coefficient = if target < effect.ducking_gain { node.attack } else { node.release };
                effect.ducking_gain = target + coefficient * (effect.ducking_gain - target);
                detector_peak =
                    detector_peak.max(if node.dynamic_ducking { trigger_peak } else { effect.ducking_detector });
                left *= effect.ducking_gain;
                right *= effect.ducking_gain;
            } else if kind == GraphKind::Pan {
                left *= f64::from(effect.pan_left.next());
                right *= f64::from(effect.pan_right.next());
            } else if kind == GraphKind::Mono {
                let average = (left + right) * 0.5;
                left = average;
                right = average;
            } else if kind == GraphKind::ChannelSwap {
                std::mem::swap(&mut left, &mut right);
            } else if kind == GraphKind::StereoWidth {
                let mid = (left + right) * 0.5;
                let side = (left - right) * 0.5 * f64::from(effect.width.next());
                left = mid + side;
                right = mid - side;
            } else if kind == GraphKind::StereoPan {
                let weights = if settled_pan { settled_weights } else { pan_weights(f64::from(effect.position.next())) };
                let signal = (left + right) * 0.5;
                left = signal * weights[0];
                right = signal * weights[1];
            } else if kind == GraphKind::Crossfade {
                let weights = if settled_fade {
                    settled_weights
                } else {
                    let position = f64::from(effect.fade_position.next());
                    let curve = f64::from(effect.fade_curve.next());
                    crossfade_weights(position, curve)
                };
                left = left * weights[0] + b_left * weights[1];
                right = right * weights[0] + b_right * weights[1];
            } else if self.advanced[i].is_some() {
                let detector = node.sidechain_edge.map(|edge_index| {
                    let sample = self.routed_output(&plan.edges[edge_index], frame);
                    let detector_gain = self.edge_ramps[edge_index].next();
                    Frame::new(
                        bounded(f64::from(sample.left * detector_gain), false),
                        bounded(f64::from(sample.right * detector_gain), false),
                    )
                });
                let input = Frame::new(bounded(left, false), bounded(right, false));
                let advanced = self.advanced[i].as_mut().expect("checked above");
                let processed = advanced.process(input, detector);
                left = f64::from(processed.wet.left);
                right = f64::from(processed.wet.right);
                dry_left = f64::from(processed.dry.left);
                dry_right = f64::from(processed.dry.right);
                effect_wet = processed.effect_wet;
            }
            let effect = &mut self.effects[i];
            let wet = f64::from(effect.wet.next());
            if compressor && !node.limit {
                minimum_compression_gain = minimum_compression_gain.min(1.0 + (effect.compression_gain - 1.0) * wet);
            }
            if kind == GraphKind::Ducking {
                minimum_compression_gain = minimum_compression_gain.min(1.0 + (effect.ducking_gain - 1.0) * wet);
            }
            left = dry_left + (left - dry_left) * wet;
            right = dry_right + (right - dry_right) * wet;
            let gain = f64::from(self.node_ramps[i].next());
            left *= gain;
            right *= gain;
            if matches!(kind, GraphKind::Reverb | GraphKind::Echo) {
                // The network keeps advancing during mute or bypass; Wet fades
                // to silence on bypass and never contains dry signal.
                self.wet_buffers[i][frame] = Frame::new(
                    bounded(f64::from(effect_wet.left) * wet * gain, false),
                    bounded(f64::from(effect_wet.right) * wet * gain, false),
                );
            }
            let effect = &mut self.effects[i];
            if !left.is_finite() || !right.is_finite() {
                left = 0.0;
                right = 0.0;
                effect.filters = Effects::INITIAL.filters;
                effect.previous_filters = Effects::INITIAL.previous_filters;
            }
            if compressor && node.limit && !node.bypass {
                // Zero-lookahead sample-peak limiter measured AFTER node and
                // control gain: an immediate linked reduction catches even the
                // first impulse, and no detector cable can defeat the ceiling.
                let peak = left.abs().max(right.abs());
                detector_peak = detector_peak.max(peak);
                let required = if peak > ceiling { ceiling / peak } else { 1.0 };
                effect.limiter_gain = if required < effect.limiter_gain {
                    required
                } else {
                    required + node.release * (effect.limiter_gain - required)
                };
                left *= effect.limiter_gain;
                right *= effect.limiter_gain;
                let limit = f64::from(safe_ceiling);
                left = left.clamp(-limit, limit);
                right = right.clamp(-limit, limit);
                minimum_compression_gain = minimum_compression_gain.min(effect.limiter_gain);
            } else if compressor && wet > 0.0 && (node.limit || limit_mix > 0.0) {
                // Leaving Limit crossfades its retained attenuation out while
                // the compressor fades in; bypass releases it on the wet ramp.
                let retained =
                    1.0 + (effect.limiter_gain - 1.0) * (if node.limit { wet } else { limit_mix * wet });
                left *= retained;
                right *= retained;
            }
            let peak = left.abs().max(right.abs());
            let meter = &mut self.meters[i];
            meter.peak_left = meter.peak_left.max(left.abs().min(1.0) as f32);
            meter.peak_right = meter.peak_right.max(right.abs().min(1.0) as f32);
            meter.peak = meter.peak_left.max(meter.peak_right);
            meter.clipped = meter.clipped || peak > 1.0;
            self.buffers[i][frame] = Frame::new(bounded(left, clip_sink), bounded(right, clip_sink));
        }
        if dynamics_audible {
            if compressor || kind == GraphKind::Ducking {
                self.meters[i].gain_reduction_db = (-20.0 * minimum_compression_gain.max(1e-6).log10()) as f32;
                self.meters[i].detector_db = (20.0 * 10f64.powf(-96.0 / 20.0).max(detector_peak).log10()) as f32;
            } else if let Some(advanced) = &self.advanced[i] {
                let attenuation = 10f64.powf(-f64::from(advanced.gain_reduction_db()) / 20.0);
                let audible_gain = 1.0 + (attenuation - 1.0) * f64::from(self.effects[i].wet.current);
                self.meters[i].gain_reduction_db = (-20.0 * audible_gain.max(1e-6).log10()) as f32;
                self.meters[i].detector_db = advanced.detector_db();
            }
        }
    }

    /// Clears all signal state (resume after suspend, or a discontinuity).
    pub fn reset(&mut self) {
        self.node_ramps = [Ramp::default(); MAXIMUM_GRAPH_NODES];
        self.edge_ramps = [Ramp::default(); MAXIMUM_GRAPH_EDGES];
        self.buffers.fill(SILENT_BLOCK);
        self.wet_buffers.fill(SILENT_BLOCK);
        self.meters = [Meter::default(); MAXIMUM_GRAPH_NODES];
        self.effects.fill(Effects::INITIAL);
        for effect in self.advanced.iter_mut().flatten() {
            effect.reset();
        }
    }

    /// Copies ramp and effect state by node and cable identity (const copy,
    /// used for offline comparisons).
    pub fn inherit(&mut self, old: &GraphProcessor, before: &GraphPlan, after: &GraphPlan) {
        self.inherit_scalars(old, before, after);
        for (i, j) in matching_nodes(before, after) {
            if let (Some(effect), Some(previous)) = (&mut self.advanced[i], &old.advanced[j]) {
                effect.inherit(previous);
            }
        }
    }

    /// Audio-thread adoption of an already-built plan: like [`inherit`], but
    /// Echo history moves instead of being copied.
    ///
    /// [`inherit`]: GraphProcessor::inherit
    pub fn adopt(&mut self, old: &mut GraphProcessor, before: &GraphPlan, after: &GraphPlan, preserve_fade: bool) {
        self.inherit_scalars(old, before, after);
        for (i, j) in matching_nodes(before, after) {
            if let (Some(effect), Some(previous)) = (&mut self.advanced[i], &mut old.advanced[j]) {
                effect.adopt(previous, preserve_fade);
            }
        }
    }

    fn inherit_scalars(&mut self, old: &GraphProcessor, before: &GraphPlan, after: &GraphPlan) {
        for (i, j) in matching_nodes(before, after) {
            self.node_ramps[i] = old.node_ramps[j];
            self.effects[i] = old.effects[j];
            let (now, then) = (&after.nodes[i], &before.nodes[j]);
            if now.kind.is_filter() && now.eq != then.eq {
                let effect = &mut self.effects[i];
                effect.previous_eq = Effects::INITIAL.previous_eq;
                effect.previous_eq[..then.eq.len()].copy_from_slice(&then.eq);
                effect.previous_eq_count = then.eq.len();
                effect.previous_filters = old.effects[j].filters;
                effect.filters = Effects::INITIAL.filters;
                effect.eq_transition = 480;
            }
        }
        for i in 0..after.edges.len() {
            if let Some(j) = vinemix_model::plan::matching_edge(after, i, before) {
                self.edge_ramps[i] = old.edge_ramps[j];
            }
        }
    }
}

/// Pairs `(after index, before index)` of nodes with the same id and kind.
fn matching_nodes<'a>(before: &'a GraphPlan, after: &'a GraphPlan) -> impl Iterator<Item = (usize, usize)> + 'a {
    after.nodes.iter().enumerate().filter_map(move |(i, node)| {
        before.nodes.iter().position(|other| other.id == node.id && other.kind == node.kind).map(|j| (i, j))
    })
}

#[cfg(test)]
mod tests;
