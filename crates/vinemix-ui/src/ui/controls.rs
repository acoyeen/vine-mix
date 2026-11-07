//! Per-kind node controls (`GraphNodeView.AddEffects` and friends): exact
//! labels, ranges, steps, defaults, formats and help text of the 0.5
//! desktop, as data the node view lays out and draws.

use std::sync::Arc;

use vinemix_model::config::GraphNode;

use super::dial::{Getter, Setter, Spec, Variant};
use crate::graph;

/// A dial bound to one node value.
#[derive(Clone)]
pub struct Dial {
    pub spec: Spec,
    pub get: Getter,
    pub set: Setter,
}

impl std::fmt::Debug for Dial {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Dial").field("key", &self.spec.key).finish_non_exhaustive()
    }
}

/// A cell in a dial group: one dial, or alternatives sharing one slot.
#[derive(Clone, Debug)]
pub enum Cell {
    Dial(Dial),
    /// Ducking: Duck amount (static) or Ratio (dynamic).
    Alternatives(Vec<Dial>, fn(&GraphNode) -> usize),
}

impl Cell {
    pub fn dials(&self) -> Vec<&Dial> {
        match self {
            Self::Dial(dial) => vec![dial],
            Self::Alternatives(dials, _) => dials.iter().collect(),
        }
    }
    pub fn primary(&self) -> bool {
        self.dials().iter().any(|d| d.spec.primary)
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ChoiceKey {
    CrossfadeCurve,
    DenoiserAlgorithm,
    CompressorMode,
    DuckingMode,
}

impl ChoiceKey {
    pub fn options(self) -> &'static [(&'static str, &'static str)] {
        match self {
            Self::CrossfadeCurve => &[("equalPower", "Equal power"), ("linear", "Linear")],
            Self::DenoiserAlgorithm => &[("speex", "SpeexDSP · 20 ms"), ("rnnoise", "RNNoise · 30 ms")],
            Self::CompressorMode => &[("compress", "Compress"), ("limit", "Limit")],
            Self::DuckingMode => &[("static", "Static ducking"), ("dynamic", "Dynamic ducking")],
        }
    }

    pub fn get(self, node: &GraphNode) -> &str {
        match self {
            Self::CrossfadeCurve => &node.crossfade_curve,
            Self::DenoiserAlgorithm => &node.denoiser.algorithm,
            Self::CompressorMode => &node.compressor.mode,
            Self::DuckingMode => &node.ducking.mode,
        }
    }

    pub fn set(self, node: &mut GraphNode, value: &str) {
        match self {
            Self::CrossfadeCurve => node.crossfade_curve = value.into(),
            Self::DenoiserAlgorithm => node.denoiser.algorithm = value.into(),
            Self::CompressorMode => node.compressor.mode = value.into(),
            Self::DuckingMode => node.ducking.mode = value.into(),
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Self::CrossfadeCurve => "Crossfade curve",
            Self::DenoiserAlgorithm => "Denoiser algorithm",
            Self::CompressorMode => "Compressor mode",
            Self::DuckingMode => "Ducking mode",
        }
    }

    pub fn help(self, node: &GraphNode) -> String {
        match self {
            Self::CrossfadeCurve => "Fade curve\nEqual power · Different sources\nLinear · Matching signals\nDelayed paths may need Alignment Delay".into(),
            Self::DenoiserAlgorithm => {
                "Algorithm\nSpeexDSP: faster, lighter cleanup.\nRNNoise: more delay, usually cleaner speech.\n20/30 ms is added node delay.".into()
            }
            Self::CompressorMode => "Compressor mode".into(),
            Self::DuckingMode => ducking_description(&node.ducking.mode),
        }
    }
}

pub fn ducking_description(mode: &str) -> String {
    if mode == "dynamic" {
        "Dynamic ducking\nLowers the audio more as the trigger sound gets louder above the threshold.\nTrigger audio is detector only\nStereo linked".into()
    } else {
        "Static ducking\nLowers the audio by a set amount when the trigger sound crosses the threshold.\nTrigger audio is detector only\nStereo linked".into()
    }
}

/// One element of a node's control column, top to bottom.
#[derive(Clone, Debug)]
pub enum Item {
    /// Status line (dynamics nodes add a detector readout and reduction strip).
    Status,
    VirtualDevice,
    Device,
    Mono,
    App,
    Choice(ChoiceKey),
    /// A 12 pt SemiBold caption.
    Caption(&'static str),
    /// 11 pt muted note.
    Note(&'static str),
    /// 12 pt help line (Alignment Delay).
    Help(&'static str),
    /// Denoiser delay line, chosen by algorithm.
    DenoiserDelay,
    Divider,
    /// A Row-variant dial (Level, tone bands).
    Dial(Dial, f32),
    /// Tone: three row dials in a padded stack.
    Tone(Vec<Dial>),
    /// A tile dial centred in the column (Linked control multiplier).
    Multiplier(Dial),
    LegacyMultiplier,
    Group { columns: usize, cells: Vec<Cell> },
    /// Alternatives occupying one measured slot (largest wins).
    Alternatives(Vec<Vec<Item>>, fn(&GraphNode) -> usize),
    Eq,
    VoicePad,
    Mute,
}

fn setter(f: impl Fn(&mut GraphNode, f64) + Send + Sync + 'static) -> Setter {
    Arc::new(f)
}

fn dial(kind: &str, label: &str, min: f64, max: f64, step: f64, unit: &'static str, get: Getter, set: Setter) -> Dial {
    let defaults = graph::new_node(kind, 0.0, 0.0);
    let spec = Spec::new(label, label, min, max, step, unit, get(&defaults), Variant::Tile);
    Dial { spec, get, set }
}

/// `EffectNumber`: a tile dial with the 0.5 format and description rules.
#[allow(clippy::too_many_arguments)]
fn effect(
    kind: &str,
    gate: bool,
    label: &str,
    min: f64,
    max: f64,
    step: f64,
    unit: &'static str,
    get: Getter,
    set: Setter,
    logarithmic: bool,
    integer: bool,
    primary: bool,
) -> Dial {
    let mut d = dial(kind, label, min, max, step, unit, get, set);
    d.spec = d.spec.logarithmic(logarithmic).primary(primary);
    if integer {
        d.spec = d.spec.integer();
    } else if matches!(unit, "ms" | "s") {
        d.spec = d.spec.format("0.###");
    } else if unit == ":1" {
        d.spec = d.spec.format("0.0##");
    } else if label.contains('Q') {
        d.spec = d.spec.format("0.0###");
    }
    d.spec = d.spec.description(&effect_description(kind, gate, label));
    d
}

fn effect_description(kind: &str, gate: bool, label: &str) -> String {
    let text = match (kind, label) {
        ("alignmentDelay", _) => "Delays this audio path to align it with another.",
        ("pan", _) => "Balances audio between the left and right channels.",
        ("stereoWidth", _) => "Sets stereo width. 0% is mono; 100% is unchanged. Wider settings can raise peaks. Mono input remains mono.",
        ("stereoPan", _) => "Moves a mono signal from left (-1) to centre (0) to right (1). Stereo input is combined to mono first; centre is -3 dB per side.",
        ("crossfade", _) => "Blends stereo inputs A and B. 0% plays A only; 100% plays B only. Bypass plays A only.",
        ("denoiser", "Reduction") => "Sets how strongly SpeexDSP reduces steady background noise.",
        ("denoiser", "Mix") => "Blends original and RNNoise audio. 0% is original; 100% is fully denoised.",
        ("deEsser", "Frequency") => "Sets the frequency of the harsh S and SH sounds to reduce.",
        ("deEsser", "Threshold") => "Sets the level above which harsh S and SH sounds are reduced.",
        ("plosive", "Frequency limit") => "Sets the upper frequency of the low P and B thumps to reduce.",
        ("plosive", "Threshold") => "Sets the level above which low P and B thumps are reduced.",
        (_, "Max reduction") => "Limits the amount of attenuation applied to detected sounds.",
        ("reverb", "Mix") => "Blends original audio with the reverberated sound.",
        ("reverb", "Decay") => "Sets how long the reverberation lasts.",
        ("reverb", "Damping") => "Softens high frequencies in the reverb tail.",
        ("reverb", "Pre-delay") => "Sets the gap before reverberation begins.",
        ("echo", "Time") => "Sets the gap before and between echoes. Very short times, such as 20 ms, thicken the sound; longer times separate the repeats. Changing Time smoothly restarts the echoes.",
        ("echo", "Repeats") => "Sets how many echoes follow the original: 1 gives one echo, 3 gives three. Each repeat is half as strong as the previous one, with the total kept controlled. Changing Repeats smoothly restarts the echoes.",
        ("echo", "Mix") => "Blends the Audio output: 0% is original sound only; 100% is echoes only. The separate Echo output is always echoes only and ignores Mix.",
        ("distortion", "Drive") => "Pushes the signal into stronger distortion. Higher values add harmonics and can increase loudness; balance the result with Output.",
        ("distortion", "Tone") => "Softens high frequencies in the distorted sound. Lower values sound darker.",
        ("distortion", "Mix") => "Blends original audio with the distorted sound. 0% is original, 100% is distorted; Output applies to both.",
        ("distortion", "Output") => "Trims the complete blend, including Mix at 0%. Bypass restores the original sound without this trim.",
        ("compressor", "Threshold") => "Sets the level above which louder passages are reduced.",
        ("compressor", "Ratio") => "Sets how strongly audio above the threshold is reduced.",
        ("compressor", "Make-up") => "Adds level after compression to compensate for the reduction.",
        (_, "Ceiling") => "Sets the sample-peak limit.",
        ("ducking", "Threshold") => "Sets the trigger level above which ducking reduces the main audio.",
        ("ducking", "Duck amount") => "Sets the fixed reduction while the trigger is above the threshold.",
        ("ducking", "Ratio") => "Sets how strongly the main audio is reduced as the trigger rises above the threshold.",
        ("ducking", "Attack") => "Sets how quickly the main audio becomes quieter when the trigger is active.",
        ("ducking", "Release") => "Sets how quickly the main audio returns to its original level after the trigger falls.",
        ("expander" | "gate", "Threshold") => {
            if gate {
                "Sets the signal level that opens the gate."
            } else {
                "Sets the level below which quieter passages are reduced."
            }
        }
        ("expander" | "gate", "Ratio") => "Sets how strongly audio below the threshold is reduced.",
        ("expander" | "gate", "Hysteresis") => "Closes the gate this many dB below its opening threshold.",
        ("expander" | "gate", "Range") => "Limits how far the signal can be turned down.",
        ("expander" | "gate", "Attack") => "Sets how quickly the signal returns to full level above the threshold.",
        ("expander" | "gate", "Hold") => "Keeps the gate open briefly after the signal drops.",
        ("expander" | "gate", "Release") => "Sets how quickly the signal becomes quieter below the threshold.",
        ("highPass", "Cutoff") => "Sets the frequency below which sound is reduced.",
        ("lowPass", "Cutoff") => "Sets the frequency above which sound is reduced.",
        (_, "Resonance / Q") => "Sets the emphasis around the cutoff frequency.",
        ("voiceColor", "Tone") => "Shifts the voice from warm to bright.",
        ("voiceColor", "Character") => "Adds a fuller broadcast character to the voice.",
        (_, "Attack") => "Sets how quickly gain reduction responds to louder audio.",
        (_, "Release") => "Sets how quickly the audio returns to its original level after gain reduction.",
        _ => return format!("Adjusts {}.", label.to_lowercase()),
    };
    text.into()
}

pub fn is_device(kind: &str) -> bool {
    matches!(kind, "microphone" | "virtualInput" | "appInput" | "output" | "virtualOutput")
}

pub fn is_gate(node: &GraphNode) -> bool {
    node.kind == "gate" || (node.kind == "expander" && node.expander.mode == "gate")
}

pub fn has_bypass(kind: &str) -> bool {
    !is_device(kind) && !matches!(kind, "stereoSplit" | "stereoCombine")
}

pub fn is_dynamics(kind: &str) -> bool {
    matches!(kind, "deEsser" | "plosive" | "compressor" | "limiter" | "ducking" | "expander" | "gate")
}

/// Node width by kind (world units).
pub fn node_width(kind: &str) -> f32 {
    match kind {
        "eq" => 620.0,
        "compressor" | "expander" | "gate" => 428.0,
        "voiceColor" => 392.0,
        "tone" => 376.0,
        "gain" | "pan" | "mono" | "reroute" | "channelSwap" | "stereoWidth" | "stereoPan" | "stereoCombine" | "stereoSplit" | "mix" => 316.0,
        "control" => 280.0,
        _ => 356.0,
    }
}

/// Builds the control column for a node.
pub fn items(node: &GraphNode) -> Vec<Item> {
    let kind = node.kind.as_str();
    let gate = is_gate(node);
    let mut items = Vec::new();
    if bindings_virtual(kind) {
        items.push(Item::VirtualDevice);
    }
    if matches!(kind, "microphone" | "output") {
        items.push(Item::Device);
        if kind == "microphone" {
            items.push(Item::Mono);
        }
    }
    if kind == "appInput" {
        items.push(Item::App);
    }
    items.push(Item::Status);
    effects(node, kind, gate, &mut items);
    if kind == "control" {
        if node.kind == "control" && graph::linked::has_legacy_value(node) {
            items.push(Item::LegacyMultiplier);
        } else {
            let mut d = dial(kind, "Multiplier", 0.0, 200.0, 0.1, "%", |n| graph::linked::percent(n), setter(|n, v| {
                let _ = graph::linked::set_percent(n, v);
            }));
            d.spec = d.spec.changes(1.0, 10.0).primary(true).format("0.#").description("Scales linked levels. 100% is unchanged; 0% is silent.");
            d.spec.default = 100.0;
            items.push(Item::Multiplier(d));
        }
    } else if graph::catalog::has_level_control(kind) {
        items.push(Item::Divider);
        let mut d = dial(kind, "Level", -60.0, 12.0, 0.1, "dB", |n| n.gain_db, setter(|n, v| n.gain_db = v));
        d.spec.variant = Variant::Row;
        d.spec = d.spec.changes(0.5, 3.0).description("Adjusts this node's audio level. 0 dB leaves it unchanged.");
        items.push(Item::Dial(d, 6.0));
    }
    if is_device(kind) {
        items.push(Item::Mute);
    }
    items
}

fn bindings_virtual(kind: &str) -> bool {
    graph::bindings::is_virtual(kind)
}

struct Builder<'a> {
    items: &'a mut Vec<Item>,
    group: Option<(usize, Vec<Cell>)>,
}

impl Builder<'_> {
    fn start_group(&mut self, columns: usize) {
        self.flush();
        self.group = Some((columns, Vec::new()));
    }
    fn push_dial(&mut self, mut dial: Dial) {
        if self.group.is_none() {
            self.start_group(2);
        }
        let (columns, cells) = self.group.as_mut().unwrap_or_else(|| unreachable!());
        if *columns == 3 {
            dial.spec.key = format!("{}#3", dial.spec.key);
        }
        cells.push(Cell::Dial(dial));
    }
    fn push_cell(&mut self, cell: Cell) {
        if self.group.is_none() {
            self.start_group(2);
        }
        self.group.as_mut().unwrap_or_else(|| unreachable!()).1.push(cell);
    }
    fn item(&mut self, item: Item) {
        self.flush();
        self.items.push(item);
    }
    fn flush(&mut self) {
        if let Some((columns, cells)) = self.group.take() {
            self.items.push(Item::Group { columns, cells });
        }
    }
}

#[allow(clippy::too_many_lines)]
fn effects(node: &GraphNode, kind: &str, gate: bool, items: &mut Vec<Item>) {
    let mut b = Builder { items, group: None };
    let e = |label: &str, min, max, step, unit, get: Getter, set: Setter, log, int, primary| {
        effect(kind, gate, label, min, max, step, unit, get, set, log, int, primary)
    };
    match kind {
        "eq" => b.item(Item::Eq),
        "voiceColor" => {
            b.item(Item::VoicePad);
            b.push_dial(e("Tone", -100.0, 100.0, 0.1, "%", |n| n.voice_color.tone * 100.0, setter(|n, v| n.voice_color.tone = v / 100.0), false, false, false));
            b.push_dial(e(
                "Character",
                0.0,
                100.0,
                0.1,
                "%",
                |n| n.voice_color.ambience * 100.0,
                setter(|n, v| n.voice_color.ambience = v / 100.0),
                false,
                false,
                false,
            ));
        }
        "tone" => {
            let labels = ["Low", "Mid", "High"];
            let ranges = ["200 Hz shelf", "1 kHz bell", "5 kHz shelf"];
            let getters: [Getter; 3] = [
                |n| n.eq_bands.first().map_or(0.0, |b| b.gain_db),
                |n| n.eq_bands.get(1).map_or(0.0, |b| b.gain_db),
                |n| n.eq_bands.get(2).map_or(0.0, |b| b.gain_db),
            ];
            let dials = (0..3)
                .map(|band| {
                    let spec = Spec::new(labels[band], labels[band], -12.0, 12.0, 0.1, "dB", 0.0, Variant::Row).detail(ranges[band]).changes(0.5, 2.0);
                    Dial {
                        spec,
                        get: getters[band],
                        set: setter(move |n, v| {
                            if let Some(b) = n.eq_bands.get_mut(band) {
                                b.gain_db = v;
                            }
                        }),
                    }
                })
                .collect();
            b.item(Item::Tone(dials));
        }
        "highPass" | "lowPass" => {
            let default_frequency = if kind == "highPass" { 80.0 } else { 12000.0 };
            b.start_group(2);
            let mut cutoff = e(
                "Cutoff",
                20.0,
                20000.0,
                1.0,
                "Hz",
                |n| if n.eq_bands.len() == 1 { n.eq_bands[0].frequency_hz } else if n.kind == "highPass" { 80.0 } else { 12000.0 },
                setter(|n, v| {
                    if n.eq_bands.len() == 1 {
                        n.eq_bands[0].frequency_hz = v;
                    }
                }),
                true,
                false,
                true,
            );
            cutoff.spec.default = default_frequency;
            b.push_dial(cutoff);
            b.push_dial(e(
                "Resonance / Q",
                0.1,
                20.0,
                0.01,
                "",
                |n| if n.eq_bands.len() == 1 { n.eq_bands[0].q } else { vinemix_model::config::BUTTERWORTH_Q },
                setter(|n, v| {
                    if n.eq_bands.len() == 1 {
                        n.eq_bands[0].q = v;
                    }
                }),
                true,
                false,
                false,
            ));
        }
        "compressor" => {
            let legacy = node.dynamics_ui_version == 0;
            let compression = |b: &mut Builder<'_>| {
                b.start_group(3);
                b.push_dial(e("Threshold", -60.0, 0.0, 0.1, "dBFS", |n| n.compressor.threshold_db, setter(|n, v| n.compressor.threshold_db = v), false, false, true));
                b.push_dial(e("Ratio", 1.0, 20.0, 0.1, ":1", |n| n.compressor.ratio, setter(|n, v| n.compressor.ratio = v), false, false, false));
                b.push_dial(e("Make-up", 0.0, 24.0, 0.1, "dB", |n| n.compressor.makeup_db, setter(|n, v| n.compressor.makeup_db = v), false, false, false));
                b.start_group(3);
                b.push_dial(e("Attack", 0.1, 200.0, 0.1, "ms", |n| n.compressor.attack_ms, setter(|n, v| n.compressor.attack_ms = v), true, false, false));
                b.push_dial(e("Release", 5.0, 2000.0, 1.0, "ms", |n| n.compressor.release_ms, setter(|n, v| n.compressor.release_ms = v), true, false, false));
                b.flush();
            };
            if legacy {
                b.item(Item::Choice(ChoiceKey::CompressorMode));
                let mut compress = Vec::new();
                compression(&mut Builder { items: &mut compress, group: None });
                let mut limit = Vec::new();
                limiter(kind, gate, &mut Builder { items: &mut limit, group: None });
                b.item(Item::Alternatives(vec![compress, limit], |n| usize::from(n.compressor.mode == "limit")));
            } else {
                compression(&mut b);
            }
        }
        "limiter" => limiter(kind, gate, &mut b),
        "ducking" => {
            b.item(Item::Choice(ChoiceKey::DuckingMode));
            b.start_group(2);
            b.push_dial(e("Threshold", -60.0, 0.0, 0.1, "dBFS", |n| n.ducking.threshold_db, setter(|n, v| n.ducking.threshold_db = v), false, false, false));
            let amount = e("Duck amount", 0.0, 60.0, 0.1, "dB", |n| n.ducking.amount_db, setter(|n, v| n.ducking.amount_db = v), false, false, true);
            let ratio = e("Ratio", 1.0, 20.0, 0.1, ":1", |n| n.ducking.ratio, setter(|n, v| n.ducking.ratio = v), false, false, true);
            b.push_cell(Cell::Alternatives(vec![amount, ratio], |n| usize::from(n.ducking.mode == "dynamic")));
            b.start_group(2);
            b.push_dial(e("Attack", 0.1, 200.0, 0.1, "ms", |n| n.ducking.attack_ms, setter(|n, v| n.ducking.attack_ms = v), true, false, false));
            b.push_dial(e("Release", 5.0, 2000.0, 1.0, "ms", |n| n.ducking.release_ms, setter(|n, v| n.ducking.release_ms = v), true, false, false));
        }
        "expander" | "gate" => {
            b.start_group(3);
            b.push_dial(e("Threshold", -80.0, 0.0, 0.5, "dBFS", |n| n.expander.threshold_db, setter(|n, v| n.expander.threshold_db = v), false, false, true));
            if gate {
                b.push_dial(e("Hysteresis", 0.0, 12.0, 0.1, "dB", |n| n.expander.hysteresis_db, setter(|n, v| n.expander.hysteresis_db = v), false, false, false));
            } else {
                b.push_dial(e("Ratio", 1.0, 20.0, 0.1, ":1", |n| n.expander.ratio, setter(|n, v| n.expander.ratio = v), false, false, false));
            }
            b.push_dial(e("Range", 0.0, 80.0, 0.5, "dB", |n| n.expander.range_db, setter(|n, v| n.expander.range_db = v), false, false, false));
            b.start_group(3);
            b.push_dial(e("Attack", 0.1, 200.0, 0.1, "ms", |n| n.expander.attack_ms, setter(|n, v| n.expander.attack_ms = v), true, false, false));
            if gate {
                b.push_dial(e("Hold", 0.0, 1000.0, 1.0, "ms", |n| n.expander.hold_ms, setter(|n, v| n.expander.hold_ms = v), false, false, false));
            }
            b.push_dial(e("Release", 5.0, 2000.0, 1.0, "ms", |n| n.expander.release_ms, setter(|n, v| n.expander.release_ms = v), true, false, false));
        }
        "alignmentDelay" => {
            b.push_dial(e(
                "Milliseconds",
                0.0,
                2000.0,
                0.001,
                "ms",
                |n| f64::from(n.alignment_delay.samples) / 48.0,
                setter(|n, v| n.alignment_delay.samples = ((v * 48.0).round().clamp(0.0, 96000.0)) as u32),
                false,
                false,
                true,
            ));
            b.push_dial(e(
                "Samples",
                0.0,
                96000.0,
                1.0,
                "",
                |n| f64::from(n.alignment_delay.samples),
                setter(|n, v| n.alignment_delay.samples = v.round().clamp(0.0, 96000.0) as u32),
                false,
                true,
                false,
            ));
            b.item(Item::Help("48 kHz · 48 samples = 1 ms"));
        }
        "pan" => {
            b.start_group(1);
            b.push_dial(e("L / R", -1.0, 1.0, 0.01, "", |n| n.balance, setter(|n, v| n.balance = v), false, false, true));
        }
        "stereoWidth" => {
            b.start_group(1);
            b.push_dial(e("Width", 0.0, 200.0, 1.0, "%", |n| n.stereo_width * 100.0, setter(|n, v| n.stereo_width = v / 100.0), false, false, true));
            b.item(Item::Note("0% Mono · 100% Original · 200% Wide"));
        }
        "stereoPan" => {
            b.start_group(1);
            b.push_dial(e("L / R", -1.0, 1.0, 0.01, "", |n| n.pan_position, setter(|n, v| n.pan_position = v), false, false, true));
            b.item(Item::Note("Stereo input is combined to mono."));
        }
        "crossfade" => {
            b.item(Item::Choice(ChoiceKey::CrossfadeCurve));
            b.start_group(1);
            b.push_dial(e(
                "A / B",
                0.0,
                100.0,
                1.0,
                "%",
                |n| n.crossfade_position * 100.0,
                setter(|n, v| n.crossfade_position = v / 100.0),
                false,
                false,
                true,
            ));
            b.item(Item::Note("0% A only · 100% B only"));
        }
        "channelSwap" => b.item(Item::Note("Left → Right\nRight → Left")),
        "stereoCombine" => b.item(Item::Note("Each input becomes one channel.\nAn empty side stays silent.")),
        "stereoSplit" => b.item(Item::Note("Each output is a mono signal.")),
        "denoiser" => {
            b.item(Item::Caption("Algorithm"));
            b.item(Item::Choice(ChoiceKey::DenoiserAlgorithm));
            let spectral = Item::Group {
                columns: 1,
                cells: vec![Cell::Dial(e(
                    "Reduction",
                    0.0,
                    40.0,
                    1.0,
                    "dB",
                    |n| -n.denoiser.suppression_db,
                    setter(|n, v| n.denoiser.suppression_db = -v),
                    false,
                    false,
                    true,
                ))],
            };
            let neural = Item::Group {
                columns: 1,
                cells: vec![Cell::Dial(e(
                    "Mix",
                    0.0,
                    100.0,
                    1.0,
                    "%",
                    |n| n.denoiser.rnnoise_mix * 100.0,
                    setter(|n, v| n.denoiser.rnnoise_mix = v / 100.0),
                    false,
                    false,
                    true,
                ))],
            };
            b.item(Item::Alternatives(vec![vec![spectral], vec![neural]], |n| usize::from(n.denoiser.algorithm == "rnnoise")));
            b.item(Item::DenoiserDelay);
        }
        "deEsser" => {
            b.push_dial(e("Frequency", 2000.0, 12000.0, 10.0, "Hz", |n| n.de_esser.frequency_hz, setter(|n, v| n.de_esser.frequency_hz = v), true, false, false));
            b.push_dial(e("Threshold", -60.0, 0.0, 1.0, "dBFS", |n| n.de_esser.threshold_db, setter(|n, v| n.de_esser.threshold_db = v), false, false, true));
            b.start_group(2);
            b.push_dial(e("Max reduction", 0.0, 24.0, 1.0, "dB", |n| n.de_esser.reduction_db, setter(|n, v| n.de_esser.reduction_db = v), false, false, false));
        }
        "plosive" => {
            b.push_dial(e("Frequency limit", 60.0, 300.0, 1.0, "Hz", |n| n.plosive.frequency_hz, setter(|n, v| n.plosive.frequency_hz = v), true, false, false));
            b.push_dial(e("Threshold", -60.0, 0.0, 1.0, "dBFS", |n| n.plosive.threshold_db, setter(|n, v| n.plosive.threshold_db = v), false, false, true));
            b.start_group(2);
            b.push_dial(e("Max reduction", 0.0, 24.0, 1.0, "dB", |n| n.plosive.reduction_db, setter(|n, v| n.plosive.reduction_db = v), false, false, false));
        }
        "echo" => {
            b.push_dial(e("Time", 20.0, 2000.0, 1.0, "ms", |n| n.echo.time_ms, setter(|n, v| n.echo.time_ms = v), true, false, true));
            b.push_dial(e("Repeats", 1.0, 8.0, 1.0, "", |n| f64::from(n.echo.repeats), setter(|n, v| n.echo.repeats = v.round() as i32), false, true, false));
            b.start_group(2);
            b.push_dial(e("Mix", 0.0, 100.0, 1.0, "%", |n| n.echo.mix * 100.0, setter(|n, v| n.echo.mix = v / 100.0), false, false, false));
            b.item(Item::Note("0% Original only · 100% Echoes only"));
        }
        "distortion" => {
            b.push_dial(e("Drive", 0.0, 36.0, 0.1, "dB", |n| n.distortion.drive_db, setter(|n, v| n.distortion.drive_db = v), false, false, true));
            b.push_dial(e("Tone", 500.0, 18000.0, 10.0, "Hz", |n| n.distortion.tone_hz, setter(|n, v| n.distortion.tone_hz = v), true, false, false));
            b.start_group(2);
            b.push_dial(e("Mix", 0.0, 100.0, 1.0, "%", |n| n.distortion.mix * 100.0, setter(|n, v| n.distortion.mix = v / 100.0), false, false, false));
            b.push_dial(e("Output", -24.0, 0.0, 0.1, "dB", |n| n.distortion.output_db, setter(|n, v| n.distortion.output_db = v), false, false, false));
        }
        "reverb" => {
            b.push_dial(e("Mix", 0.0, 100.0, 1.0, "%", |n| n.reverb.mix * 100.0, setter(|n, v| n.reverb.mix = v / 100.0), false, false, true));
            b.push_dial(e("Decay", 0.1, 5.0, 0.1, "s", |n| n.reverb.decay_seconds, setter(|n, v| n.reverb.decay_seconds = v), true, false, false));
            b.start_group(2);
            b.push_dial(e("Damping", 0.0, 100.0, 1.0, "%", |n| n.reverb.damping * 100.0, setter(|n, v| n.reverb.damping = v / 100.0), false, false, false));
            b.push_dial(e("Pre-delay", 0.0, 100.0, 1.0, "ms", |n| n.reverb.pre_delay_ms, setter(|n, v| n.reverb.pre_delay_ms = v), false, false, false));
        }
        _ => {}
    }
    b.flush();
}

fn limiter(kind: &str, gate: bool, b: &mut Builder<'_>) {
    b.start_group(2);
    b.push_dial(effect(kind, gate, "Ceiling", -24.0, 0.0, 0.1, "dBFS", |n| n.compressor.ceiling_db, setter(|n, v| n.compressor.ceiling_db = v), false, false, true));
    b.push_dial(effect(kind, gate, "Release", 5.0, 2000.0, 1.0, "ms", |n| n.compressor.release_ms, setter(|n, v| n.compressor.release_ms = v), true, false, false));
    b.item(Item::Note("Sample peaks · No lookahead\nBypass removes limiting.\nLater gain can exceed this ceiling."));
}

/// The header tooltip (`NodeDescription`).
pub fn node_description(node: &GraphNode) -> String {
    let kind = if is_gate(node) { "gate" } else { node.kind.as_str() };
    match kind {
        "mono" => "Mono\nCombines left and right equally".into(),
        "channelSwap" => "Channel Swap\nExchanges left and right\nChannel levels stay unchanged".into(),
        "stereoWidth" => "Stereo Width\n0% · Mono\n100% · Original stereo\nAbove 100% · Wider stereo\nMono input remains mono".into(),
        "stereoPan" => "Pan\nPositions a mono signal left to right\nStereo input is combined to mono first\nEqual-power curve · Centre is −3 dB per side".into(),
        "crossfade" => "Crossfade\nBlends stereo inputs A and B\n0% · A only\n100% · B only\nBypass · A only".into(),
        "stereoCombine" => "Stereo Combine\nLeft input → Left channel\nRight input → Right channel\nEach input is combined to mono\nAn empty input leaves its side silent".into(),
        "stereoSplit" => "Stereo Split\nExtracts left and right separately\nEach output is a centred mono signal\nUse Stereo Combine to rebuild stereo".into(),
        "control" => "Linked control\nScales connected levels while keeping their individual settings.".into(),
        "gain" => "Gain\nAdjusts the incoming level\n0 dB · Unchanged".into(),
        "mix" => "Mix\nCombines incoming audio\nLevel · Adjusts the combined volume".into(),
        "highPass" => "High-pass\nReduces sound below the cutoff".into(),
        "lowPass" => "Low-pass\nReduces sound above the cutoff".into(),
        "denoiser" => "Denoiser\nSpeexDSP: faster, lighter cleanup.\nRNNoise: more delay, usually cleaner speech.\n20/30 ms is added node delay.".into(),
        "deEsser" => "De-esser\nTames harsh S and SH sounds\nActs above the threshold".into(),
        "plosive" => "Plosive remover\nTames low P and B thumps\nActs above the threshold".into(),
        "reverb" => "Reverb\nAdds room-like decay\nAudio · Mix blends dry sound and reverb\nWet · Reverb only, independent of Mix\nBypass · Audio passes dry sound; Wet fades to silence\nThe reverb tail keeps advancing while bypassed".into(),
        "echo" => "Echo\nAdds a chosen number of fading echoes\nTime · Gap before and between echoes\nRepeats · Echoes after the original sound\nMix · 0% original only; 100% echoes only\nAudio · The Mix blend\nEcho output · Echoes only, independent of Mix\nChanging Time or Repeats smoothly restarts the echoes".into(),
        "distortion" => "Distortion\nAdds warmth through to stronger distortion\nDrive · Strength of the distortion\nTone · Brightness of the distorted sound\nMix · Original and distorted sound\nOutput · Final level adjustment\nAdds 0.67 ms delay, including Mix 0% and bypass".into(),
        "voiceColor" => "Voice Color\nWarm / Bright · Tone\nNatural / Broadcast · Character\nBody, clarity and controlled thickening\nBottom centre · Neutral".into(),
        "compressor" => "Compressor\nReduces its own louder passages\nRatio controls reduction above threshold".into(),
        "limiter" => "Limiter\nSample-peak ceiling · No lookahead\nBypass removes limiting\nLater gain can exceed this ceiling".into(),
        "ducking" => ducking_description(&node.ducking.mode),
        "expander" => "Expander\nReduces quieter passages below the threshold\nTrigger · Optional detector-only input; otherwise uses Audio\nRatio · Reduction below threshold\nRange · Maximum attenuation".into(),
        "gate" => "Gate\nCloses below the threshold\nTrigger · Optional detector-only input; otherwise uses Audio\nHold · Keeps the gate open briefly\nHysteresis · Separate closing threshold\nRange · Maximum attenuation".into(),
        "alignmentDelay" => "Alignment Delay\nDelay one path to align it with another\n48 samples = 1 ms · Up to 2 seconds\nBypass · Undelayed audio".into(),
        "reroute" => "Reroute\nA junction for organizing cables".into(),
        _ => graph::kind_label(kind).to_owned(),
    }
}
