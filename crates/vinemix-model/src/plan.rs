//! Graph validation and compilation (`compileGraph` in `graph.cpp`).
//!
//! Compilation runs on a control thread. The resulting [`GraphPlan`] has
//! bounded, indexed routing: the audio thread never searches a dictionary,
//! parses a string or grows storage while processing it.

use std::collections::{HashMap, HashSet};
use std::f64::consts::PI;

use crate::config::{
    tone_defaults, AlignmentDelaySettings, AudioGraph, DeEsserSettings, DenoiserSettings, DistortionSettings, EchoSettings,
    EqBand, ExpanderSettings, PlosiveSettings, ReverbSettings, VoiceColorSettings,
};
use crate::kind::{GraphKind, GraphPort};
use crate::limits::{check_gain_db, check_id, MAXIMUM_EQ_BANDS, MAXIMUM_GRAPH_EDGES, MAXIMUM_GRAPH_NODES};
use crate::{ModelError, SAMPLE_RATE};

/// Normalised RBJ biquad coefficients. The default is the identity filter.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Biquad {
    pub b0: f64,
    pub b1: f64,
    pub b2: f64,
    pub a1: f64,
    pub a2: f64,
}

impl Default for Biquad {
    fn default() -> Self {
        Self { b0: 1.0, b1: 0.0, b2: 0.0, a1: 0.0, a2: 0.0 }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct PlanNode {
    pub id: String,
    pub kind: GraphKind,
    pub gain_db: f32,
    pub control_multiplier: f32,
    pub muted: bool,
    pub mono: bool,
    pub selected_microphone_channel: bool,
    pub control_edge: Option<usize>,
    pub sidechain_edge: Option<usize>,
    pub incoming: Vec<usize>,
    /// Active (non-identity) filter sections, at most eight.
    pub eq: Vec<Biquad>,
    pub threshold: f64,
    pub ratio: f64,
    pub attack: f64,
    pub release: f64,
    pub makeup: f64,
    pub limit: bool,
    pub ceiling: f64,
    pub ducking_amount: f64,
    pub ducking_close_threshold: f64,
    pub dynamic_ducking: bool,
    pub expander: ExpanderSettings,
    pub alignment_delay: AlignmentDelaySettings,
    pub pan_left: f32,
    pub pan_right: f32,
    pub stereo_width: f32,
    pub position: f32,
    pub fade_position: f32,
    pub equal_power: bool,
    pub bypass: bool,
    pub denoiser: DenoiserSettings,
    pub de_esser: DeEsserSettings,
    pub plosive: PlosiveSettings,
    pub reverb: ReverbSettings,
    pub echo: EchoSettings,
    pub distortion: DistortionSettings,
    pub voice_color: VoiceColorSettings,
    pub voice_color_gain: f64,
}

impl PlanNode {
    fn new(id: String, kind: GraphKind) -> Self {
        Self {
            id,
            kind,
            gain_db: 0.0,
            control_multiplier: -1.0,
            muted: false,
            mono: false,
            selected_microphone_channel: false,
            control_edge: None,
            sidechain_edge: None,
            incoming: Vec::new(),
            eq: Vec::new(),
            threshold: 0.125_892_54,
            ratio: 4.0,
            attack: 0.997_918_84,
            release: 0.999_826_4,
            makeup: 1.0,
            limit: false,
            ceiling: 0.891_250_938_133_745_6,
            ducking_amount: 1.0,
            ducking_close_threshold: 0.0,
            dynamic_ducking: false,
            expander: ExpanderSettings::default(),
            alignment_delay: AlignmentDelaySettings::default(),
            pan_left: 1.0,
            pan_right: 1.0,
            stereo_width: 1.0,
            position: 0.0,
            fade_position: 0.0,
            equal_power: true,
            bypass: false,
            denoiser: DenoiserSettings::default(),
            de_esser: DeEsserSettings::default(),
            plosive: PlosiveSettings::default(),
            reverb: ReverbSettings::default(),
            echo: EchoSettings::default(),
            distortion: DistortionSettings::default(),
            voice_color: VoiceColorSettings::default(),
            voice_color_gain: 1.0,
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct PlanEdge {
    pub id: String,
    pub from: usize,
    pub to: usize,
    pub control: bool,
    pub sidechain: bool,
    pub gain_db: f32,
    pub muted: bool,
    pub from_port: GraphPort,
    pub to_port: GraphPort,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct GraphPlan {
    pub nodes: Vec<PlanNode>,
    pub edges: Vec<PlanEdge>,
    /// Topological processing order (Kahn, node index order).
    pub order: Vec<usize>,
}

fn range(value: f64, low: f64, high: f64, message: &'static str) -> Result<(), ModelError> {
    if !value.is_finite() || value < low || value > high {
        return Err(ModelError::invalid(message));
    }
    Ok(())
}

/// RBJ cookbook coefficients for one EQ band, validating its ranges first.
pub fn coefficients(band: &EqBand, canonicalize_flat: bool) -> Result<Biquad, ModelError> {
    range(band.frequency_hz, 20.0, 20_000.0, "EQ frequency must be between 20 Hz and 20 kHz.")?;
    range(band.gain_db, -24.0, 24.0, "EQ gain must be between -24 and +24 dB.")?;
    range(band.q, 0.1, 20.0, "EQ Q must be between 0.1 and 20.")?;
    if band.reset_frequency_hz != 0.0 {
        range(
            band.reset_frequency_hz,
            20.0,
            20_000.0,
            "EQ reset frequency must be unset or between 20 Hz and 20 kHz.",
        )?;
    }
    let w = 2.0 * PI * band.frequency_hz / f64::from(SAMPLE_RATE);
    let (c, s) = (w.cos(), w.sin());
    let alpha = s / (2.0 * band.q);
    let a = 10f64.powf(band.gain_db / 40.0);
    let beta = 2.0 * a.sqrt() * alpha;
    let (b0, b1, b2, a0, a1, a2) = match band.band_type.as_str() {
        "peak" => (1.0 + alpha * a, -2.0 * c, 1.0 - alpha * a, 1.0 + alpha / a, -2.0 * c, 1.0 - alpha / a),
        "lowPass" => ((1.0 - c) / 2.0, 1.0 - c, (1.0 - c) / 2.0, 1.0 + alpha, -2.0 * c, 1.0 - alpha),
        "highPass" => ((1.0 + c) / 2.0, -(1.0 + c), (1.0 + c) / 2.0, 1.0 + alpha, -2.0 * c, 1.0 - alpha),
        "notch" => (1.0, -2.0 * c, 1.0, 1.0 + alpha, -2.0 * c, 1.0 - alpha),
        "lowShelf" => (
            a * ((a + 1.0) - (a - 1.0) * c + beta),
            2.0 * a * ((a - 1.0) - (a + 1.0) * c),
            a * ((a + 1.0) - (a - 1.0) * c - beta),
            (a + 1.0) + (a - 1.0) * c + beta,
            -2.0 * ((a - 1.0) + (a + 1.0) * c),
            (a + 1.0) + (a - 1.0) * c - beta,
        ),
        "highShelf" => (
            a * ((a + 1.0) + (a - 1.0) * c + beta),
            -2.0 * a * ((a - 1.0) + (a + 1.0) * c),
            a * ((a + 1.0) + (a - 1.0) * c - beta),
            (a + 1.0) - (a - 1.0) * c + beta,
            2.0 * ((a - 1.0) - (a + 1.0) * c),
            (a + 1.0) - (a - 1.0) * c - beta,
        ),
        _ => return Err(ModelError::invalid("Unknown EQ band type.")),
    };
    if !band.enabled {
        return Ok(Biquad::default());
    }
    // A zero-gain bell/shelf is exactly transparent: canonicalise it so it
    // needs no filter work or history on the audio thread.
    if canonicalize_flat
        && band.gain_db == 0.0
        && matches!(band.band_type.as_str(), "peak" | "lowShelf" | "highShelf")
    {
        return Ok(Biquad::default());
    }
    Ok(Biquad { b0: b0 / a0, b1: b1 / a0, b2: b2 / a0, a1: a1 / a0, a2: a2 / a0 })
}

fn time_constant(milliseconds: f64) -> f64 {
    (-1.0 / (milliseconds * 0.001 * f64::from(SAMPLE_RATE))).exp()
}

fn db_to_linear(db: f64) -> f64 {
    10f64.powf(db / 20.0)
}

/// Validates and compiles a canvas. Error messages are shown to users.
pub fn compile_graph(graph: &AudioGraph) -> Result<GraphPlan, ModelError> {
    if graph.nodes.len() > MAXIMUM_GRAPH_NODES || graph.edges.len() > MAXIMUM_GRAPH_EDGES {
        return Err(ModelError::invalid("A canvas supports up to 32 nodes and 96 cables."));
    }
    if !graph.pan_x.is_finite()
        || !graph.pan_y.is_finite()
        || !graph.zoom.is_finite()
        || graph.pan_x.abs() > 100_000.0
        || graph.pan_y.abs() > 100_000.0
        || graph.zoom < 0.1
        || graph.zoom > 4.0
    {
        return Err(ModelError::invalid("The canvas position or zoom is invalid."));
    }
    let mut plan = GraphPlan::default();
    let mut ids: HashMap<&str, usize> = HashMap::new();
    for (i, source) in graph.nodes.iter().enumerate() {
        check_id(&source.id)?;
        check_gain_db(source.gain_db)?;
        if ids.insert(source.id.as_str(), i).is_some() {
            return Err(ModelError::invalid("Two nodes have the same identifier."));
        }
        if source.name.len() > 256
            || !source.x.is_finite()
            || !source.y.is_finite()
            || source.x.abs() > 100_000.0
            || source.y.abs() > 100_000.0
        {
            return Err(ModelError::invalid("A node name or position is invalid."));
        }
        let kind = GraphKind::parse(&source.kind).map_err(|error| ModelError::invalid(error.to_string()))?;
        let mut node = PlanNode::new(source.id.clone(), kind);
        node.gain_db = source.gain_db as f32;
        let multiplier = source.control_multiplier;
        if !multiplier.is_finite() || (multiplier != -1.0 && !(0.0..=2.0).contains(&multiplier)) {
            return Err(ModelError::invalid("Linked control multiplier must be between 0 and 200 percent."));
        }
        node.control_multiplier = multiplier as f32;
        node.muted = source.muted;
        node.mono = source.mono;
        node.selected_microphone_channel = source.kind == "microphone" && source.device.channel >= 0;
        // Structural channel routing has no transparent alternative.
        node.bypass = source.bypass && source.kind != "stereoSplit" && source.kind != "stereoCombine";
        if !(0..=1).contains(&source.dynamics_ui_version) {
            return Err(ModelError::invalid("Unknown dynamics interface version."));
        }
        range(source.denoiser.suppression_db, -40.0, 0.0, "Denoiser suppression must be between -40 and 0 dB.")?;
        if source.denoiser.algorithm != "speex" && source.denoiser.algorithm != "rnnoise" {
            return Err(ModelError::invalid("Unknown denoiser algorithm."));
        }
        range(source.denoiser.rnnoise_mix, 0.0, 1.0, "RNNoise mix must be between 0 and 100 percent.")?;
        range(source.de_esser.frequency_hz, 2000.0, 12_000.0, "De-esser frequency must be between 2 and 12 kHz.")?;
        range(source.de_esser.threshold_db, -60.0, 0.0, "De-esser threshold must be between -60 and 0 dB.")?;
        range(source.de_esser.reduction_db, 0.0, 24.0, "De-esser reduction must be between 0 and 24 dB.")?;
        range(source.plosive.frequency_hz, 60.0, 300.0, "Plosive cutoff must be between 60 and 300 Hz.")?;
        range(source.plosive.threshold_db, -60.0, 0.0, "Plosive threshold must be between -60 and 0 dB.")?;
        range(source.plosive.reduction_db, 0.0, 24.0, "Plosive reduction must be between 0 and 24 dB.")?;
        range(source.reverb.mix, 0.0, 1.0, "Reverb mix must be between 0 and 1.")?;
        range(source.reverb.decay_seconds, 0.1, 5.0, "Reverb decay must be between 0.1 and 5 seconds.")?;
        range(source.reverb.damping, 0.0, 1.0, "Reverb damping must be between 0 and 1.")?;
        range(source.reverb.pre_delay_ms, 0.0, 100.0, "Reverb pre-delay must be between 0 and 100 ms.")?;
        range(source.echo.time_ms, 20.0, 2000.0, "Echo time must be between 20 and 2000 ms.")?;
        range(f64::from(source.echo.repeats), 1.0, 8.0, "Echo repeats must be a whole number between 1 and 8.")?;
        range(source.echo.mix, 0.0, 1.0, "Echo mix must be between 0 and 100 percent.")?;
        range(source.distortion.drive_db, 0.0, 36.0, "Distortion drive must be between 0 and 36 dB.")?;
        range(source.distortion.tone_hz, 500.0, 18_000.0, "Distortion tone must be between 500 and 18000 Hz.")?;
        range(source.distortion.mix, 0.0, 1.0, "Distortion mix must be between 0 and 100 percent.")?;
        range(source.distortion.output_db, -24.0, 0.0, "Distortion output must be between -24 and 0 dB.")?;
        range(source.voice_color.tone, -1.0, 1.0, "Voice Color tone must be between -1 (Warm) and +1 (Bright).")?;
        range(
            source.voice_color.ambience,
            0.0,
            1.0,
            "Voice Color character must be between 0 (Natural) and 1 (Broadcast).",
        )?;
        node.denoiser = source.denoiser.clone();
        node.de_esser = source.de_esser;
        node.plosive = source.plosive;
        node.reverb = source.reverb;
        node.echo = source.echo;
        node.distortion = source.distortion;
        node.voice_color = source.voice_color;
        let expander = &source.expander;
        if expander.mode != "expander" && expander.mode != "gate" {
            return Err(ModelError::invalid("Unknown Expander / Gate mode."));
        }
        if source.kind == "gate" && expander.mode != "gate" {
            return Err(ModelError::invalid("Gate nodes require Gate mode."));
        }
        range(expander.threshold_db, -80.0, 0.0, "Expander threshold must be between -80 and 0 dB.")?;
        range(expander.ratio, 1.0, 20.0, "Expander ratio must be between 1 and 20.")?;
        range(expander.attack_ms, 0.1, 200.0, "Expander attack must be between 0.1 and 200 ms.")?;
        range(expander.hold_ms, 0.0, 1000.0, "Expander hold must be between 0 and 1000 ms.")?;
        range(expander.release_ms, 5.0, 2000.0, "Expander release must be between 5 and 2000 ms.")?;
        range(expander.range_db, 0.0, 80.0, "Expander range must be between 0 and 80 dB.")?;
        range(expander.hysteresis_db, 0.0, 12.0, "Expander hysteresis must be between 0 and 12 dB.")?;
        if source.alignment_delay.samples > 96_000 {
            return Err(ModelError::invalid("Alignment Delay supports 0 to 96000 samples."));
        }
        node.expander = expander.clone();
        node.alignment_delay = source.alignment_delay;
        if source.eq_bands.len() > MAXIMUM_EQ_BANDS {
            return Err(ModelError::invalid("An EQ node supports up to eight bands."));
        }
        if source.kind == "highPass" || source.kind == "lowPass" {
            let valid = source.eq_bands.len() == 1
                && source.eq_bands[0].band_type == source.kind
                && source.eq_bands[0].enabled
                && source.eq_bands[0].gain_db == 0.0;
            if !valid {
                return Err(ModelError::invalid(
                    "High-pass and low-pass nodes require exactly one enabled matching filter with zero band gain. Use the node bypass switch to disable it.",
                ));
            }
        }
        if source.kind == "tone" {
            let expected = tone_defaults();
            if source.eq_bands.len() != expected.len() {
                return Err(ModelError::invalid("Low / Mid / High requires exactly three tone bands."));
            }
            for (actual, fixed) in source.eq_bands.iter().zip(&expected) {
                range(actual.gain_db, -12.0, 12.0, "Low / Mid / High levels must be between -12 and +12 dB.")?;
                if actual.band_type != fixed.band_type
                    || !actual.enabled
                    || actual.frequency_hz != fixed.frequency_hz
                    || !actual.q.is_finite()
                    || (actual.q - fixed.q).abs() > 1e-7
                {
                    return Err(ModelError::invalid(
                        "Low / Mid / High uses its fixed 200 Hz shelf, 1 kHz bell and 5 kHz shelf; use EQ for custom filters.",
                    ));
                }
            }
        }
        for band in &source.eq_bands {
            let coefficient = coefficients(band, true)?;
            if coefficient != Biquad::default() {
                node.eq.push(coefficient);
            }
        }
        if source.kind == "voiceColor" {
            let tone = source.voice_color.tone;
            // The strongest contour is reserved for the top of the pad; the
            // same curve drives dynamics and room in the processor.
            let character = source.voice_color.ambience.powf(1.3);
            // Keep cancelling pole/zero pairs at neutral (no canonicalisation):
            // identity coefficients would make an epsilon move near zero a
            // large coefficient jump during interpolation.
            let band = |kind: &str, frequency: f64, gain: f64, q: f64| {
                coefficients(&EqBand::new(kind, true, frequency, gain, q, 0.0), false)
            };
            node.eq = vec![
                band("lowShelf", 140.0, 12.0 * character - 12.0 * tone, std::f64::consts::FRAC_1_SQRT_2)?,
                band("peak", 450.0, -8.0 * character, 0.9)?,
                band("peak", 3000.0, 12.0 * character + 3.0 * tone, 0.85)?,
                band("highShelf", 7000.0, 4.0 * character + 9.0 * tone, std::f64::consts::FRAC_1_SQRT_2)?,
            ];
            // Warmth adds bass energy, so reserve headroom on that side only.
            node.voice_color_gain = 10f64.powf((-6.0 * (-tone).max(0.0)) / 20.0);
            // Only the wet amount moves; delay and decay stay fixed so dragging
            // the pad never modulates pitch.
            node.reverb = ReverbSettings { mix: 0.02 * character, decay_seconds: 0.3, damping: 0.65, pre_delay_ms: 8.0 };
        }
        let comp = &source.compressor;
        range(comp.threshold_db, -60.0, 0.0, "Compressor threshold must be between -60 and 0 dB.")?;
        range(comp.ratio, 1.0, 20.0, "Compressor ratio must be between 1 and 20.")?;
        range(comp.attack_ms, 0.1, 200.0, "Compressor attack must be between 0.1 and 200 ms.")?;
        range(comp.release_ms, 5.0, 2000.0, "Compressor release must be between 5 and 2000 ms.")?;
        range(comp.makeup_db, 0.0, 24.0, "Compressor makeup gain must be between 0 and +24 dB.")?;
        if comp.mode != "compress" && comp.mode != "limit" {
            return Err(ModelError::invalid("Unknown Compressor mode."));
        }
        if comp.detector != "internal" && comp.detector != "external" {
            return Err(ModelError::invalid("Unknown Compressor detector."));
        }
        range(comp.ceiling_db, -24.0, 0.0, "Limiter ceiling must be between -24 and 0 dB.")?;
        // Dedicated Limiter and saved Compressor limit mode share one
        // algorithm; Compressor always listens to its own programme.
        node.limit = source.kind == "limiter" || comp.mode == "limit";
        node.ceiling = db_to_linear(comp.ceiling_db);
        node.threshold = db_to_linear(comp.threshold_db);
        node.ratio = comp.ratio;
        node.attack = time_constant(comp.attack_ms);
        node.release = time_constant(comp.release_ms);
        node.makeup = db_to_linear(comp.makeup_db);
        let duck = &source.ducking;
        if duck.mode != "static" && duck.mode != "dynamic" {
            return Err(ModelError::invalid("Unknown Ducking mode."));
        }
        range(duck.ratio, 1.0, 20.0, "Dynamic Ducking ratio must be between 1 and 20.")?;
        range(duck.threshold_db, -60.0, 0.0, "Ducking threshold must be between -60 and 0 dB.")?;
        range(duck.amount_db, 0.0, 60.0, "Duck amount must be between 0 and 60 dB.")?;
        range(duck.attack_ms, 0.1, 200.0, "Ducking attack must be between 0.1 and 200 ms.")?;
        range(duck.release_ms, 5.0, 2000.0, "Ducking release must be between 5 and 2000 ms.")?;
        if source.kind == "ducking" {
            node.threshold = db_to_linear(duck.threshold_db);
            node.dynamic_ducking = duck.mode == "dynamic";
            node.ratio = duck.ratio;
            node.ducking_close_threshold = db_to_linear(duck.threshold_db - 3.0);
            node.ducking_amount = db_to_linear(-duck.amount_db);
            node.attack = time_constant(duck.attack_ms);
            node.release = time_constant(duck.release_ms);
        }
        range(source.balance, -1.0, 1.0, "Balance must be between left and right.")?;
        node.pan_left = (if source.balance > 0.0 { 1.0 - source.balance } else { 1.0 }) as f32;
        node.pan_right = (if source.balance < 0.0 { 1.0 + source.balance } else { 1.0 }) as f32;
        range(source.stereo_width, 0.0, 2.0, "Stereo Width must be between 0 and 200 percent.")?;
        range(source.pan_position, -1.0, 1.0, "Pan must be between left and right.")?;
        range(source.crossfade_position, 0.0, 1.0, "Crossfade must be between A and B.")?;
        if source.crossfade_curve != "equalPower" && source.crossfade_curve != "linear" {
            return Err(ModelError::invalid("Unknown crossfade curve."));
        }
        node.stereo_width = source.stereo_width as f32;
        node.position = source.pan_position as f32;
        node.fade_position = source.crossfade_position as f32;
        node.equal_power = source.crossfade_curve == "equalPower";
        plan.nodes.push(node);
    }

    let mut edge_ids: HashSet<&str> = HashSet::new();
    let mut connections: HashSet<String> = HashSet::new();
    let mut indegrees = vec![0usize; plan.nodes.len()];
    for (i, source) in graph.edges.iter().enumerate() {
        check_id(&source.id)?;
        check_gain_db(source.gain_db)?;
        if !edge_ids.insert(source.id.as_str()) {
            return Err(ModelError::invalid("Two cables have the same identifier."));
        }
        let (Some(&from), Some(&to)) = (ids.get(source.from.as_str()), ids.get(source.to.as_str())) else {
            return Err(ModelError::invalid("A cable refers to a missing node."));
        };
        if from == to {
            return Err(ModelError::invalid("A node cannot connect to itself."));
        }
        if !matches!(source.kind.as_str(), "audio" | "control" | "sidechain") {
            return Err(ModelError::invalid("Unknown cable type."));
        }
        let control = source.kind == "control";
        let sidechain = source.kind == "sidechain";
        let (from_kind, to_kind) = (plan.nodes[from].kind, plan.nodes[to].kind);
        let unavailable = || ModelError::invalid("A cable refers to an unavailable audio socket.");
        let mut from_port = GraphPort::Audio;
        let mut to_port = GraphPort::Audio;
        if control {
            if source.from_port != "audio" || source.to_port != "audio" {
                return Err(ModelError::invalid("A control cable cannot select an audio channel."));
            }
            if from_kind != GraphKind::Control || to_kind == GraphKind::Control {
                return Err(ModelError::invalid("A control cable must connect a control node to an audio node."));
            }
            if plan.nodes[to].control_edge.is_some() {
                return Err(ModelError::invalid("An audio node can have one shared control connection."));
            }
            plan.nodes[to].control_edge = Some(i);
        } else if sidechain {
            from_port = GraphPort::resolve(from_kind, &source.from_port, true).ok_or_else(unavailable)?;
            if source.to_port != "audio" {
                return Err(ModelError::invalid("A trigger cable cannot select a programme input."));
            }
            if (!from_kind.is_source() && !from_kind.is_processor()) || !to_kind.accepts_trigger() {
                return Err(ModelError::invalid(
                    "A sidechain connects an audio source or effect to a Ducking, Gate or Expander trigger.",
                ));
            }
            if plan.nodes[to].sidechain_edge.is_some() {
                return Err(ModelError::invalid("A dynamics node can have one trigger cable."));
            }
            plan.nodes[to].sidechain_edge = Some(i);
        } else {
            from_port = GraphPort::resolve(from_kind, &source.from_port, true).ok_or_else(unavailable)?;
            to_port = GraphPort::resolve(to_kind, &source.to_port, false).ok_or_else(unavailable)?;
            if (!from_kind.is_source() && !from_kind.is_processor()) || (!to_kind.is_processor() && !to_kind.is_sink()) {
                return Err(ModelError::invalid("Connect audio from an input or processor to a processor or output."));
            }
            plan.nodes[to].incoming.push(i);
        }
        // Explicit separators prevent collisions even when an ID contains a dash.
        let connection =
            [source.kind.as_str(), &source.from, &source.from_port, &source.to, &source.to_port].join("\0");
        if !connections.insert(connection) {
            return Err(ModelError::invalid("These nodes are already connected."));
        }
        plan.edges.push(PlanEdge {
            id: source.id.clone(),
            from,
            to,
            control,
            sidechain,
            gain_db: source.gain_db as f32,
            muted: source.muted,
            from_port,
            to_port,
        });
        indegrees[to] += 1;
    }

    let mut ready: Vec<usize> = (0..plan.nodes.len()).filter(|&i| indegrees[i] == 0).collect();
    let mut head = 0;
    while head < ready.len() {
        let node = ready[head];
        head += 1;
        plan.order.push(node);
        for edge in &plan.edges {
            if edge.from == node {
                indegrees[edge.to] -= 1;
                if indegrees[edge.to] == 0 {
                    ready.push(edge.to);
                }
            }
        }
    }
    if plan.order.len() != plan.nodes.len() {
        return Err(ModelError::invalid("This connection creates an audio feedback loop. Remove a cable first."));
    }
    Ok(plan)
}

/// Per-node signal layout: `0` unknown, `1` mono, `2` stereo. Capture counts
/// come from the observed endpoints. Runs outside the audio callback.
pub fn graph_signal_channels(plan: &GraphPlan, capture_channels: &[i32]) -> Vec<i32> {
    let mut channels = vec![0i32; plan.nodes.len()];
    for &i in &plan.order {
        let node = &plan.nodes[i];
        if node.kind == GraphKind::Control {
            continue;
        }
        if node.kind.is_source() {
            channels[i] = if node.kind == GraphKind::AppInput {
                2
            } else {
                capture_channels.get(i).copied().unwrap_or(0).clamp(0, 2)
            };
            if node.kind == GraphKind::Microphone && (node.mono || node.selected_microphone_channel) {
                channels[i] = 1;
            }
            continue;
        }
        let mut unknown = node.incoming.is_empty();
        for &edge_index in &node.incoming {
            let edge = &plan.edges[edge_index];
            if node.kind == GraphKind::Crossfade {
                if (node.bypass || node.fade_position == 0.0) && edge.to_port == GraphPort::B {
                    continue;
                }
                if !node.bypass && node.fade_position == 1.0 && edge.to_port == GraphPort::A {
                    continue;
                }
            }
            let mut upstream = channels[edge.from];
            if upstream != 0 && matches!(edge.from_port, GraphPort::Left | GraphPort::Right) {
                upstream = 1;
            }
            // Reverb creates stereo reflections even from mono; its Wet port is
            // independent of the main output's mix and bypass.
            if upstream != 0 && edge.from_port == GraphPort::Wet && plan.nodes[edge.from].kind == GraphKind::Reverb {
                upstream = 2;
            }
            unknown = unknown || upstream == 0;
            channels[i] = channels[i].max(upstream);
        }
        // A known stereo input establishes stereo even when another source is
        // absent; mono plus an unknown source cannot yet promise mono.
        if unknown && channels[i] < 2 {
            channels[i] = 0;
        }
        if !node.bypass && node.kind == GraphKind::Mono {
            channels[i] = 1;
        }
        if node.kind == GraphKind::StereoCombine {
            channels[i] = 2;
        }
        if !node.bypass && node.kind == GraphKind::StereoWidth && node.stereo_width == 0.0 {
            channels[i] = 1;
        }
        if !node.bypass && node.kind == GraphKind::StereoPan {
            channels[i] = if node.position == 0.0 { 1 } else { 2 };
        }
        if !node.bypass && node.kind == GraphKind::Pan && node.pan_left != node.pan_right && channels[i] != 0 {
            channels[i] = 2;
        }
        if !node.bypass
            && matches!(node.kind, GraphKind::Reverb | GraphKind::VoiceColor)
            && node.reverb.mix > 0.0
            && channels[i] != 0
        {
            channels[i] = 2;
        }
    }
    channels
}

fn same_edge(a: &GraphPlan, edge: &PlanEdge, b: &GraphPlan, other: &PlanEdge) -> bool {
    edge.id == other.id
        && edge.control == other.control
        && edge.sidechain == other.sidechain
        && edge.from_port == other.from_port
        && edge.to_port == other.to_port
        && a.nodes[edge.from].id == b.nodes[other.from].id
        && a.nodes[edge.to].id == b.nodes[other.to].id
}

/// Same node identities/kinds and the same routing, regardless of order.
pub fn same_graph_topology(a: &GraphPlan, b: &GraphPlan) -> bool {
    if a.nodes.len() != b.nodes.len() || a.edges.len() != b.edges.len() {
        return false;
    }
    a.nodes.iter().all(|node| b.nodes.iter().any(|other| node.id == other.id && node.kind == other.kind))
        && a.edges.iter().all(|edge| b.edges.iter().any(|other| same_edge(a, edge, b, other)))
}

/// Index in `before` of the edge that `after.edges[index]` continues.
pub fn matching_edge(after: &GraphPlan, index: usize, before: &GraphPlan) -> Option<usize> {
    let edge = &after.edges[index];
    before.edges.iter().position(|previous| same_edge(after, edge, before, previous))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{GraphEdge, GraphNode};
    use crate::defaults::default_configuration;

    fn graph(nodes: Vec<GraphNode>, edges: Vec<GraphEdge>) -> AudioGraph {
        AudioGraph { nodes, edges, ..AudioGraph::default() }
    }

    fn error(graph: &AudioGraph) -> String {
        compile_graph(graph).unwrap_err().to_string()
    }

    #[test]
    fn starter_graph_order_is_topological() {
        let plan = compile_graph(&default_configuration().graph).unwrap();
        assert_eq!(plan.order.len(), 7);
        for edge in &plan.edges {
            let from = plan.order.iter().position(|&n| n == edge.from).unwrap();
            let to = plan.order.iter().position(|&n| n == edge.to).unwrap();
            assert!(from < to);
        }
        // Default bands are flat: no filter work for ordinary nodes.
        assert!(plan.nodes.iter().filter(|n| n.kind != GraphKind::HighPass).all(|n| n.eq.is_empty()));
        assert_eq!(plan.nodes[1].eq.len(), 1);
    }

    #[test]
    fn rejects_loops_duplicates_and_bad_sockets() {
        let a = GraphNode::new("a", "gain", "", 0.0, 0.0);
        let b = GraphNode::new("b", "gain", "", 0.0, 0.0);
        let looped = graph(vec![a.clone(), b.clone()], vec![GraphEdge::audio("1", "a", "b"), GraphEdge::audio("2", "b", "a")]);
        assert_eq!(error(&looped), "This connection creates an audio feedback loop. Remove a cable first.");
        let twice = graph(vec![a.clone(), b.clone()], vec![GraphEdge::audio("1", "a", "b"), GraphEdge::audio("2", "a", "b")]);
        assert_eq!(error(&twice), "These nodes are already connected.");
        let mut socket = GraphEdge::audio("1", "a", "b");
        socket.from_port = "wet".into();
        assert_eq!(error(&graph(vec![a.clone(), b.clone()], vec![socket])), "A cable refers to an unavailable audio socket.");
        assert_eq!(error(&graph(vec![a.clone(), a.clone()], vec![])), "Two nodes have the same identifier.");
        let mut unknown = a.clone();
        unknown.kind = "Gain".into();
        assert_eq!(error(&graph(vec![unknown], vec![])), "Unknown node type: Gain");
        let output = GraphNode::new("o", "output", "", 0.0, 0.0);
        assert_eq!(
            error(&graph(vec![output, a], vec![GraphEdge::audio("1", "o", "a")])),
            "Connect audio from an input or processor to a processor or output."
        );
    }

    #[test]
    fn control_and_trigger_cables_are_single() {
        let control = GraphNode::new("c", "control", "", 0.0, 0.0);
        let control2 = GraphNode::new("d", "control", "", 0.0, 0.0);
        let gain = GraphNode::new("g", "gain", "", 0.0, 0.0);
        let mut first = GraphEdge::audio("1", "c", "g");
        first.kind = "control".into();
        let mut second = GraphEdge::audio("2", "d", "g");
        second.kind = "control".into();
        assert_eq!(
            error(&graph(vec![control, control2, gain], vec![first, second])),
            "An audio node can have one shared control connection."
        );
        let mic = GraphNode::new("m", "microphone", "", 0.0, 0.0);
        let duck = GraphNode::new("d", "ducking", "", 0.0, 0.0);
        let mut trigger = GraphEdge::audio("t", "m", "d");
        trigger.kind = "sidechain".into();
        let plan = compile_graph(&graph(vec![mic, duck], vec![trigger])).unwrap();
        assert_eq!(plan.nodes[1].sidechain_edge, Some(0));
        assert!(plan.nodes[1].incoming.is_empty(), "triggers never join the programme");
    }

    #[test]
    fn tone_and_pass_nodes_keep_fixed_bands() {
        let mut tone = GraphNode::new("t", "tone", "", 0.0, 0.0);
        tone.eq_bands = tone_defaults();
        tone.eq_bands[1].gain_db = 6.0;
        let plan = compile_graph(&graph(vec![tone.clone()], vec![])).unwrap();
        assert_eq!(plan.nodes[0].eq.len(), 1);
        tone.eq_bands[1].frequency_hz = 900.0;
        assert!(error(&graph(vec![tone], vec![])).starts_with("Low / Mid / High uses its fixed"));
        let mut pass = GraphNode::new("p", "lowPass", "", 0.0, 0.0);
        assert!(error(&graph(vec![pass.clone()], vec![])).starts_with("High-pass and low-pass nodes"));
        pass.eq_bands = vec![EqBand::new("lowPass", true, 8000.0, 0.0, 0.7, 0.0)];
        compile_graph(&graph(vec![pass], vec![])).unwrap();
    }

    #[test]
    fn voice_color_compiles_fixed_slots() {
        let mut voice = GraphNode::new("v", "voiceColor", "", 0.0, 0.0);
        let neutral = compile_graph(&graph(vec![voice.clone()], vec![])).unwrap();
        assert_eq!(neutral.nodes[0].eq.len(), 4, "neutral keeps four slots for interpolation");
        assert_eq!(neutral.nodes[0].reverb.mix, 0.0);
        voice.voice_color = VoiceColorSettings { tone: -1.0, ambience: 1.0 };
        let warm = compile_graph(&graph(vec![voice], vec![])).unwrap();
        assert!((warm.nodes[0].voice_color_gain - 10f64.powf(-6.0 / 20.0)).abs() < 1e-15);
        assert!((warm.nodes[0].reverb.mix - 0.02).abs() < 1e-15);
    }

    #[test]
    fn rbj_peak_matches_reference() {
        // Reference values computed by the C++ formula at 48 kHz.
        let b = coefficients(&EqBand::new("peak", true, 1000.0, 6.0, 1.0, 0.0), true).unwrap();
        let w = 2.0 * PI * 1000.0 / 48000.0;
        let alpha = w.sin() / 2.0;
        let a = 10f64.powf(6.0 / 40.0);
        let a0 = 1.0 + alpha / a;
        assert_eq!(b.b0, (1.0 + alpha * a) / a0);
        assert_eq!(b.a2, (1.0 - alpha / a) / a0);
        assert_eq!(
            coefficients(&EqBand::new("peak", true, 1000.0, 0.0, 1.0, 0.0), true).unwrap(),
            Biquad::default()
        );
        assert_eq!(
            coefficients(&EqBand::new("bell", true, 1000.0, 0.0, 1.0, 0.0), true).unwrap_err().to_string(),
            "Unknown EQ band type."
        );
    }

    #[test]
    fn channels_follow_signal_layout() {
        let mic = GraphNode::new("m", "microphone", "", 0.0, 0.0);
        let mut width = GraphNode::new("w", "stereoWidth", "", 0.0, 0.0);
        width.stereo_width = 0.0;
        let out = GraphNode::new("o", "output", "", 0.0, 0.0);
        let plan = compile_graph(&graph(
            vec![mic, width, out],
            vec![GraphEdge::audio("1", "m", "w"), GraphEdge::audio("2", "w", "o")],
        ))
        .unwrap();
        assert_eq!(graph_signal_channels(&plan, &[2, 0, 0]), vec![2, 1, 1]);
        assert_eq!(graph_signal_channels(&plan, &[0, 0, 0]), vec![0, 1, 1], "width 0 is mono regardless");
    }

    #[test]
    fn topology_ignores_order_and_values() {
        let config = default_configuration();
        let plan = compile_graph(&config.graph).unwrap();
        let mut reordered = config.graph.clone();
        reordered.nodes.reverse();
        reordered.edges.reverse();
        reordered.nodes[0].gain_db = -3.0;
        let other = compile_graph(&reordered).unwrap();
        assert!(same_graph_topology(&plan, &other));
        assert_ne!(plan, other);
        reordered.edges.pop();
        assert!(!same_graph_topology(&plan, &compile_graph(&reordered).unwrap()));
    }
}
