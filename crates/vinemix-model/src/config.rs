//! Configuration v2: the saved workspace and the `configure` request body.
//!
//! Field names, defaults, optional/required keys and conversion rules match
//! `types.hpp` and `configuration.hpp` of the 0.5 engine. Strings that name an
//! enumeration (node kind, cable kind, modes) stay strings here so a document
//! round-trips unchanged and is rejected only by validation, with the same
//! message and ordering as the C++ engine.

use serde_json::Value;

use crate::json::{self, ObjectBuilder};
use crate::ModelError;

pub const SOURCE_COUNT: usize = 3;

#[derive(Clone, Debug, Default, PartialEq)]
pub struct SendControl {
    pub enabled: bool,
    pub gain_db: f32,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct SourceControl {
    pub muted: bool,
    pub a: SendControl,
    pub b: SendControl,
}

#[derive(Clone, Debug, Default, PartialEq)]
pub struct BusControl {
    pub muted: bool,
    pub gain_db: f32,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DeviceBinding {
    pub id: String,
    pub stable_id: String,
    pub name: String,
    pub channel: i32,
}

impl Default for DeviceBinding {
    fn default() -> Self {
        Self { id: String::new(), stable_id: String::new(), name: String::new(), channel: -1 }
    }
}

impl DeviceBinding {
    /// A binding names a device when either identity is present.
    pub fn assigned(&self) -> bool {
        !self.id.is_empty() || !self.stable_id.is_empty()
    }

    pub fn same_device(&self, other: &Self) -> bool {
        self.id == other.id && self.stable_id == other.stable_id && self.channel == other.channel
    }
}

/// A PID is only a live hint; the executable path identifies an application.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ProcessBinding {
    pub process_id: u32,
    pub executable_path: String,
    pub name: String,
}

impl ProcessBinding {
    pub fn same_process(&self, other: &Self) -> bool {
        self.process_id == other.process_id && self.executable_path == other.executable_path
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct EqBand {
    pub band_type: String,
    pub enabled: bool,
    pub frequency_hz: f64,
    pub gain_db: f64,
    pub q: f64,
    pub reset_frequency_hz: f64,
}

impl Default for EqBand {
    fn default() -> Self {
        Self::new("peak", true, 1000.0, 0.0, 1.0, 0.0)
    }
}

impl EqBand {
    pub fn new(band_type: &str, enabled: bool, frequency_hz: f64, gain_db: f64, q: f64, reset_frequency_hz: f64) -> Self {
        Self { band_type: band_type.to_owned(), enabled, frequency_hz, gain_db, q, reset_frequency_hz }
    }
}

// 0.5 stores this exact truncated literal in settings files; 1/sqrt(2) would
// change saved workspaces and break byte-for-byte parity.
#[allow(clippy::approx_constant)]
pub const BUTTERWORTH_Q: f64 = 0.70710678;

/// The fixed Low / Mid / High layout (`toneDefaults`).
pub fn tone_defaults() -> Vec<EqBand> {
    vec![
        EqBand::new("lowShelf", true, 200.0, 0.0, BUTTERWORTH_Q, 0.0),
        EqBand::new("peak", true, 1000.0, 0.0, 1.0, 0.0),
        EqBand::new("highShelf", true, 5000.0, 0.0, BUTTERWORTH_Q, 0.0),
    ]
}

/// The five-band EQ every new node starts with.
pub fn default_eq_bands() -> Vec<EqBand> {
    vec![
        EqBand::new("lowShelf", true, 80.0, 0.0, BUTTERWORTH_Q, 80.0),
        EqBand::new("peak", true, 250.0, 0.0, 1.0, 250.0),
        EqBand::new("peak", true, 1000.0, 0.0, 1.0, 1000.0),
        EqBand::new("peak", true, 4000.0, 0.0, 1.0, 4000.0),
        EqBand::new("highShelf", true, 10000.0, 0.0, BUTTERWORTH_Q, 10000.0),
    ]
}

#[derive(Clone, Debug, PartialEq)]
pub struct CompressorSettings {
    pub threshold_db: f64,
    pub ratio: f64,
    pub attack_ms: f64,
    pub release_ms: f64,
    pub makeup_db: f64,
    pub mode: String,
    pub detector: String,
    pub ceiling_db: f64,
}

impl Default for CompressorSettings {
    fn default() -> Self {
        Self {
            threshold_db: -18.0,
            ratio: 4.0,
            attack_ms: 10.0,
            release_ms: 120.0,
            makeup_db: 0.0,
            mode: "compress".into(),
            detector: "internal".into(),
            ceiling_db: -1.0,
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct DuckingSettings {
    pub threshold_db: f64,
    pub amount_db: f64,
    pub attack_ms: f64,
    pub release_ms: f64,
    pub mode: String,
    pub ratio: f64,
}

impl Default for DuckingSettings {
    fn default() -> Self {
        Self { threshold_db: -30.0, amount_db: 18.0, attack_ms: 10.0, release_ms: 250.0, mode: "static".into(), ratio: 4.0 }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct ExpanderSettings {
    pub mode: String,
    pub threshold_db: f64,
    pub ratio: f64,
    pub attack_ms: f64,
    pub hold_ms: f64,
    pub release_ms: f64,
    pub range_db: f64,
    pub hysteresis_db: f64,
}

impl Default for ExpanderSettings {
    fn default() -> Self {
        Self {
            mode: "expander".into(),
            threshold_db: -40.0,
            ratio: 2.0,
            attack_ms: 5.0,
            hold_ms: 50.0,
            release_ms: 150.0,
            range_db: 60.0,
            hysteresis_db: 3.0,
        }
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct AlignmentDelaySettings {
    pub samples: u32,
}

#[derive(Clone, Debug, PartialEq)]
pub struct DenoiserSettings {
    pub suppression_db: f64,
    pub algorithm: String,
    pub rnnoise_mix: f64,
}

impl Default for DenoiserSettings {
    fn default() -> Self {
        Self { suppression_db: -18.0, algorithm: "speex".into(), rnnoise_mix: 1.0 }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct DeEsserSettings {
    pub frequency_hz: f64,
    pub threshold_db: f64,
    pub reduction_db: f64,
}

impl Default for DeEsserSettings {
    fn default() -> Self {
        Self { frequency_hz: 6000.0, threshold_db: -24.0, reduction_db: 9.0 }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PlosiveSettings {
    pub frequency_hz: f64,
    pub threshold_db: f64,
    pub reduction_db: f64,
}

impl Default for PlosiveSettings {
    fn default() -> Self {
        Self { frequency_hz: 150.0, threshold_db: -24.0, reduction_db: 12.0 }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ReverbSettings {
    pub mix: f64,
    pub decay_seconds: f64,
    pub damping: f64,
    pub pre_delay_ms: f64,
}

impl Default for ReverbSettings {
    fn default() -> Self {
        Self { mix: 0.2, decay_seconds: 1.2, damping: 0.5, pre_delay_ms: 15.0 }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct EchoSettings {
    pub time_ms: f64,
    pub repeats: i32,
    pub mix: f64,
}

impl Default for EchoSettings {
    fn default() -> Self {
        Self { time_ms: 300.0, repeats: 3, mix: 0.25 }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct DistortionSettings {
    pub drive_db: f64,
    pub tone_hz: f64,
    pub mix: f64,
    pub output_db: f64,
}

impl Default for DistortionSettings {
    fn default() -> Self {
        Self { drive_db: 6.0, tone_hz: 6000.0, mix: 0.5, output_db: 0.0 }
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct VoiceColorSettings {
    pub tone: f64,
    pub ambience: f64,
}

#[derive(Clone, Debug, PartialEq)]
pub struct GraphNode {
    pub id: String,
    pub kind: String,
    pub name: String,
    pub x: f64,
    pub y: f64,
    /// Saved and acknowledged exactly as the desktop's double; only the
    /// compiled DSP plan narrows gains to `f32`.
    pub gain_db: f64,
    pub muted: bool,
    pub mono: bool,
    /// `-1` preserves the decibel-based controller of older workspaces.
    pub control_multiplier: f64,
    pub device: DeviceBinding,
    pub app: ProcessBinding,
    pub eq_bands: Vec<EqBand>,
    pub compressor: CompressorSettings,
    pub ducking: DuckingSettings,
    pub dynamics_ui_version: i32,
    pub expander: ExpanderSettings,
    pub alignment_delay: AlignmentDelaySettings,
    pub denoiser: DenoiserSettings,
    pub de_esser: DeEsserSettings,
    pub plosive: PlosiveSettings,
    pub reverb: ReverbSettings,
    pub echo: EchoSettings,
    pub distortion: DistortionSettings,
    pub voice_color: VoiceColorSettings,
    pub balance: f64,
    pub stereo_width: f64,
    pub pan_position: f64,
    pub crossfade_position: f64,
    pub crossfade_curve: String,
    pub bypass: bool,
    pub collapsed: bool,
    pub auto_assign_device: bool,
}

impl Default for GraphNode {
    fn default() -> Self {
        Self {
            id: String::new(),
            kind: "gain".into(),
            name: String::new(),
            x: 0.0,
            y: 0.0,
            gain_db: 0.0,
            muted: false,
            mono: false,
            control_multiplier: -1.0,
            device: DeviceBinding::default(),
            app: ProcessBinding::default(),
            eq_bands: default_eq_bands(),
            compressor: CompressorSettings::default(),
            ducking: DuckingSettings::default(),
            dynamics_ui_version: 0,
            expander: ExpanderSettings::default(),
            alignment_delay: AlignmentDelaySettings::default(),
            denoiser: DenoiserSettings::default(),
            de_esser: DeEsserSettings::default(),
            plosive: PlosiveSettings::default(),
            reverb: ReverbSettings::default(),
            echo: EchoSettings::default(),
            distortion: DistortionSettings::default(),
            voice_color: VoiceColorSettings::default(),
            balance: 0.0,
            stereo_width: 1.0,
            pan_position: 0.0,
            crossfade_position: 0.0,
            crossfade_curve: "equalPower".into(),
            bypass: false,
            collapsed: false,
            auto_assign_device: true,
        }
    }
}

impl GraphNode {
    pub fn new(id: &str, kind: &str, name: &str, x: f64, y: f64) -> Self {
        Self { id: id.into(), kind: kind.into(), name: name.into(), x, y, ..Self::default() }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct GraphEdge {
    pub id: String,
    pub from: String,
    pub to: String,
    pub kind: String,
    pub gain_db: f64,
    pub muted: bool,
    pub from_port: String,
    pub to_port: String,
}

impl Default for GraphEdge {
    fn default() -> Self {
        Self {
            id: String::new(),
            from: String::new(),
            to: String::new(),
            kind: "audio".into(),
            gain_db: 0.0,
            muted: false,
            from_port: "audio".into(),
            to_port: "audio".into(),
        }
    }
}

impl GraphEdge {
    pub fn audio(id: &str, from: &str, to: &str) -> Self {
        Self { id: id.into(), from: from.into(), to: to.into(), ..Self::default() }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct AudioGraph {
    pub nodes: Vec<GraphNode>,
    pub edges: Vec<GraphEdge>,
    pub pan_x: f64,
    pub pan_y: f64,
    pub zoom: f64,
}

impl Default for AudioGraph {
    fn default() -> Self {
        Self { nodes: Vec::new(), edges: Vec::new(), pan_x: 20.0, pan_y: 20.0, zoom: 1.0 }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct Configuration {
    pub version: i32,
    pub revision: u64,
    pub microphones: [DeviceBinding; 2],
    pub virtual_input_capture: DeviceBinding,
    pub virtual_input_playback: DeviceBinding,
    pub main_outputs: [DeviceBinding; 2],
    pub virtual_output_render: DeviceBinding,
    pub virtual_output_capture: DeviceBinding,
    pub sources: [SourceControl; SOURCE_COUNT],
    pub bus_a: BusControl,
    pub bus_b: BusControl,
    pub start_at_login: bool,
    pub graph: AudioGraph,
}

impl Default for Configuration {
    fn default() -> Self {
        let send = |enabled| SendControl { enabled, gain_db: 0.0 };
        Self {
            version: 2,
            revision: 0,
            microphones: Default::default(),
            virtual_input_capture: DeviceBinding::default(),
            virtual_input_playback: DeviceBinding::default(),
            main_outputs: Default::default(),
            virtual_output_render: DeviceBinding::default(),
            virtual_output_capture: DeviceBinding::default(),
            sources: [
                SourceControl { muted: false, a: send(false), b: send(false) },
                SourceControl { muted: false, a: send(false), b: send(false) },
                SourceControl { muted: false, a: send(true), b: send(false) },
            ],
            bus_a: BusControl::default(),
            bus_b: BusControl::default(),
            start_at_login: false,
            graph: AudioGraph::default(),
        }
    }
}

// ---------------------------------------------------------------- reading --

fn read_send(value: &Value) -> Result<SendControl, ModelError> {
    Ok(SendControl {
        enabled: json::get_bool(json::at(value, "enabled")?)?,
        gain_db: json::get_f64(json::at(value, "gainDb")?)? as f32,
    })
}

fn read_source(value: &Value) -> Result<SourceControl, ModelError> {
    Ok(SourceControl {
        muted: json::get_bool(json::at(value, "muted")?)?,
        a: read_send(json::at(value, "a")?)?,
        b: read_send(json::at(value, "b")?)?,
    })
}

fn read_bus(value: &Value) -> Result<BusControl, ModelError> {
    Ok(BusControl {
        muted: json::get_bool(json::at(value, "muted")?)?,
        gain_db: json::get_f64(json::at(value, "gainDb")?)? as f32,
    })
}

pub fn read_device(value: &Value) -> Result<DeviceBinding, ModelError> {
    let id = json::get_string(json::at(value, "id")?)?;
    let stable_id = json::get_string(json::at(value, "stableId")?)?;
    let name = json::get_string(json::at(value, "name")?)?;
    let channel = json::read_integer_setting(
        json::at(value, "channel")?,
        -1,
        255,
        "The selected microphone channel must be a whole number between -1 and 255.",
    )? as i32;
    Ok(DeviceBinding { id, stable_id, name, channel })
}

fn read_process(value: &Value) -> Result<ProcessBinding, ModelError> {
    if !value.is_object() {
        return Err(ModelError::invalid("Application binding must be an object."));
    }
    let process_id = match json::optional(value, "processId")? {
        None => 0,
        Some(id) => json::read_integer_setting(
            id,
            0,
            i64::from(u32::MAX),
            "The application process ID must be a nonnegative 32-bit whole number.",
        )? as u32,
    };
    Ok(ProcessBinding {
        process_id,
        executable_path: json::value_string(value, "executablePath", "")?,
        name: json::value_string(value, "name", "")?,
    })
}

fn read_eq_band(value: &Value) -> Result<EqBand, ModelError> {
    let d = EqBand::default();
    Ok(EqBand {
        band_type: json::value_string(value, "type", &d.band_type)?,
        enabled: json::value_bool(value, "enabled", d.enabled)?,
        frequency_hz: json::value_f64(value, "frequencyHz", d.frequency_hz)?,
        gain_db: json::value_f64(value, "gainDb", d.gain_db)?,
        q: json::value_f64(value, "q", d.q)?,
        reset_frequency_hz: json::value_f64(value, "resetFrequencyHz", d.reset_frequency_hz)?,
    })
}

fn read_compressor(value: &Value) -> Result<CompressorSettings, ModelError> {
    let d = CompressorSettings::default();
    Ok(CompressorSettings {
        threshold_db: json::value_f64(value, "thresholdDb", d.threshold_db)?,
        ratio: json::value_f64(value, "ratio", d.ratio)?,
        attack_ms: json::value_f64(value, "attackMs", d.attack_ms)?,
        release_ms: json::value_f64(value, "releaseMs", d.release_ms)?,
        makeup_db: json::value_f64(value, "makeupDb", d.makeup_db)?,
        mode: json::value_string(value, "mode", &d.mode)?,
        detector: json::value_string(value, "detector", &d.detector)?,
        ceiling_db: json::value_f64(value, "ceilingDb", d.ceiling_db)?,
    })
}

fn read_ducking(value: &Value) -> Result<DuckingSettings, ModelError> {
    if !value.is_object() {
        return Err(ModelError::invalid("Ducking settings must be an object."));
    }
    Ok(DuckingSettings {
        threshold_db: json::value_f64(value, "thresholdDb", -30.0)?,
        amount_db: json::value_f64(value, "amountDb", 18.0)?,
        attack_ms: json::value_f64(value, "attackMs", 10.0)?,
        release_ms: json::value_f64(value, "releaseMs", 250.0)?,
        mode: json::value_string(value, "mode", "static")?,
        ratio: json::value_f64(value, "ratio", 4.0)?,
    })
}

fn read_expander(value: &Value) -> Result<ExpanderSettings, ModelError> {
    if !value.is_object() {
        return Err(ModelError::invalid("Expander settings must be an object."));
    }
    Ok(ExpanderSettings {
        mode: json::value_string(value, "mode", "expander")?,
        threshold_db: json::value_f64(value, "thresholdDb", -40.0)?,
        ratio: json::value_f64(value, "ratio", 2.0)?,
        attack_ms: json::value_f64(value, "attackMs", 5.0)?,
        hold_ms: json::value_f64(value, "holdMs", 50.0)?,
        release_ms: json::value_f64(value, "releaseMs", 150.0)?,
        range_db: json::value_f64(value, "rangeDb", 60.0)?,
        hysteresis_db: json::value_f64(value, "hysteresisDb", 3.0)?,
    })
}

fn read_alignment_delay(value: &Value) -> Result<AlignmentDelaySettings, ModelError> {
    if !value.is_object() {
        return Err(ModelError::invalid("Alignment Delay settings must be an object."));
    }
    let message = "Alignment Delay must contain 0 to 96000 whole samples.";
    let samples = match json::optional(value, "samples")? {
        None => 0,
        Some(samples) => {
            if !json::is_integer(samples) {
                return Err(ModelError::invalid(message));
            }
            let count = json::get_f64(samples)?;
            if !(0.0..=96_000.0).contains(&count) {
                return Err(ModelError::invalid(message));
            }
            count as u32
        }
    };
    Ok(AlignmentDelaySettings { samples })
}

fn read_denoiser(value: &Value) -> Result<DenoiserSettings, ModelError> {
    let d = DenoiserSettings::default();
    Ok(DenoiserSettings {
        suppression_db: json::value_f64(value, "suppressionDb", d.suppression_db)?,
        algorithm: json::value_string(value, "algorithm", &d.algorithm)?,
        rnnoise_mix: json::value_f64(value, "rnnoiseMix", d.rnnoise_mix)?,
    })
}

fn read_de_esser(value: &Value) -> Result<DeEsserSettings, ModelError> {
    let d = DeEsserSettings::default();
    Ok(DeEsserSettings {
        frequency_hz: json::value_f64(value, "frequencyHz", d.frequency_hz)?,
        threshold_db: json::value_f64(value, "thresholdDb", d.threshold_db)?,
        reduction_db: json::value_f64(value, "reductionDb", d.reduction_db)?,
    })
}

fn read_plosive(value: &Value) -> Result<PlosiveSettings, ModelError> {
    let d = PlosiveSettings::default();
    Ok(PlosiveSettings {
        frequency_hz: json::value_f64(value, "frequencyHz", d.frequency_hz)?,
        threshold_db: json::value_f64(value, "thresholdDb", d.threshold_db)?,
        reduction_db: json::value_f64(value, "reductionDb", d.reduction_db)?,
    })
}

fn read_reverb(value: &Value) -> Result<ReverbSettings, ModelError> {
    let d = ReverbSettings::default();
    Ok(ReverbSettings {
        mix: json::value_f64(value, "mix", d.mix)?,
        decay_seconds: json::value_f64(value, "decaySeconds", d.decay_seconds)?,
        damping: json::value_f64(value, "damping", d.damping)?,
        pre_delay_ms: json::value_f64(value, "preDelayMs", d.pre_delay_ms)?,
    })
}

fn read_echo(value: &Value) -> Result<EchoSettings, ModelError> {
    if !value.is_object() {
        return Err(ModelError::invalid("Echo settings must be an object."));
    }
    let number = |name: &str, fallback: f64| -> Result<f64, ModelError> {
        match json::optional(value, name)? {
            None => Ok(fallback),
            Some(Value::Number(_)) => {
                let result = json::get_f64(json::at(value, name)?)?;
                if result.is_finite() {
                    Ok(result)
                } else {
                    Err(ModelError::invalid("Echo settings must contain finite numbers."))
                }
            }
            Some(_) => Err(ModelError::invalid("Echo settings must contain finite numbers.")),
        }
    };
    let mut settings = EchoSettings::default();
    settings.time_ms = number("timeMs", settings.time_ms)?;
    settings.mix = number("mix", settings.mix)?;
    // Feedback was an unpublished development setting. Validate it even when
    // the explicit repeat count wins, then emit only the canonical fields.
    let feedback = number("feedback", 0.0)?;
    if !(0.0..=0.9).contains(&feedback) {
        return Err(ModelError::invalid("Legacy Echo feedback must be between 0 and 90 percent."));
    }
    let has = |name: &str| value.as_object().is_some_and(|map| map.contains_key(name));
    if has("repeats") {
        let repeats = number("repeats", 3.0)?;
        if !(1.0..=8.0).contains(&repeats) || repeats != repeats.floor() {
            return Err(ModelError::invalid("Echo repeats must be a whole number between 1 and 8."));
        }
        settings.repeats = repeats as i32;
    } else if has("feedback") {
        settings.repeats = 1;
        let mut level = feedback;
        while settings.repeats < 8 && level >= 0.01 {
            settings.repeats += 1;
            level *= feedback;
        }
    }
    Ok(settings)
}

fn read_distortion(value: &Value) -> Result<DistortionSettings, ModelError> {
    let d = DistortionSettings::default();
    Ok(DistortionSettings {
        drive_db: json::value_f64(value, "driveDb", d.drive_db)?,
        tone_hz: json::value_f64(value, "toneHz", d.tone_hz)?,
        mix: json::value_f64(value, "mix", d.mix)?,
        output_db: json::value_f64(value, "outputDb", d.output_db)?,
    })
}

fn read_voice_color(value: &Value) -> Result<VoiceColorSettings, ModelError> {
    if !value.is_object() {
        return Err(ModelError::invalid("Voice Color settings must be an object."));
    }
    Ok(VoiceColorSettings {
        tone: json::value_f64(value, "tone", 0.0)?,
        ambience: json::value_f64(value, "ambience", 0.0)?,
    })
}

fn nested<T>(value: &Value, key: &str, default: T, read: impl Fn(&Value) -> Result<T, ModelError>) -> Result<T, ModelError> {
    match json::optional(value, key)? {
        Some(item) => read(item),
        None => Ok(default),
    }
}

fn read_array<T>(value: &Value, read: impl Fn(&Value) -> Result<T, ModelError>) -> Result<Vec<T>, ModelError> {
    json::get_array(value)?.iter().map(read).collect()
}

pub fn read_node(value: &Value) -> Result<GraphNode, ModelError> {
    let d = GraphNode::default();
    Ok(GraphNode {
        id: json::value_string(value, "id", &d.id)?,
        kind: json::value_string(value, "kind", &d.kind)?,
        name: json::value_string(value, "name", &d.name)?,
        x: json::value_f64(value, "x", d.x)?,
        y: json::value_f64(value, "y", d.y)?,
        gain_db: json::value_f64(value, "gainDb", d.gain_db)?,
        muted: json::value_bool(value, "muted", d.muted)?,
        mono: json::value_bool(value, "mono", d.mono)?,
        device: nested(value, "device", d.device, read_device)?,
        app: nested(value, "app", d.app, read_process)?,
        eq_bands: nested(value, "eqBands", d.eq_bands, |v| read_array(v, read_eq_band))?,
        compressor: nested(value, "compressor", d.compressor, read_compressor)?,
        ducking: nested(value, "ducking", d.ducking, read_ducking)?,
        dynamics_ui_version: nested(value, "dynamicsUiVersion", d.dynamics_ui_version, |v| {
            json::read_integer_setting(v, 0, 1, "Unknown dynamics interface version.").map(|version| version as i32)
        })?,
        expander: nested(value, "expander", d.expander, read_expander)?,
        alignment_delay: nested(value, "alignmentDelay", d.alignment_delay, read_alignment_delay)?,
        denoiser: nested(value, "denoiser", d.denoiser, read_denoiser)?,
        de_esser: nested(value, "deEsser", d.de_esser, read_de_esser)?,
        plosive: nested(value, "plosive", d.plosive, read_plosive)?,
        reverb: nested(value, "reverb", d.reverb, read_reverb)?,
        echo: nested(value, "echo", d.echo, read_echo)?,
        distortion: nested(value, "distortion", d.distortion, read_distortion)?,
        voice_color: nested(value, "voiceColor", d.voice_color, read_voice_color)?,
        balance: json::value_f64(value, "balance", d.balance)?,
        bypass: json::value_bool(value, "bypass", d.bypass)?,
        collapsed: json::value_bool(value, "collapsed", d.collapsed)?,
        auto_assign_device: json::value_bool(value, "autoAssignDevice", d.auto_assign_device)?,
        stereo_width: json::value_f64(value, "stereoWidth", d.stereo_width)?,
        pan_position: json::value_f64(value, "panPosition", d.pan_position)?,
        crossfade_position: json::value_f64(value, "crossfadePosition", d.crossfade_position)?,
        crossfade_curve: json::value_string(value, "crossfadeCurve", &d.crossfade_curve)?,
        control_multiplier: json::value_f64(value, "controlMultiplier", d.control_multiplier)?,
    })
}

pub fn read_edge(value: &Value) -> Result<GraphEdge, ModelError> {
    let d = GraphEdge::default();
    Ok(GraphEdge {
        id: json::value_string(value, "id", &d.id)?,
        from: json::value_string(value, "from", &d.from)?,
        to: json::value_string(value, "to", &d.to)?,
        kind: json::value_string(value, "kind", &d.kind)?,
        gain_db: json::value_f64(value, "gainDb", d.gain_db)?,
        muted: json::value_bool(value, "muted", d.muted)?,
        from_port: json::value_string(value, "fromPort", &d.from_port)?,
        to_port: json::value_string(value, "toPort", &d.to_port)?,
    })
}

pub fn read_graph(value: &Value) -> Result<AudioGraph, ModelError> {
    let d = AudioGraph::default();
    Ok(AudioGraph {
        nodes: nested(value, "nodes", d.nodes, |v| read_array(v, read_node))?,
        edges: nested(value, "edges", d.edges, |v| read_array(v, read_edge))?,
        pan_x: json::value_f64(value, "panX", d.pan_x)?,
        pan_y: json::value_f64(value, "panY", d.pan_y)?,
        zoom: json::value_f64(value, "zoom", d.zoom)?,
    })
}

/// `readRevision`: revisions are exact conflict tokens, never quantities.
pub fn read_revision(value: &Value) -> Result<u64, ModelError> {
    match value {
        Value::Number(number) if number.is_u64() => Ok(number.as_u64().unwrap_or_default()),
        _ => Err(ModelError::invalid("Configuration revision must be a nonnegative whole number.")),
    }
}

fn pair<T>(items: Vec<T>) -> [T; 2] {
    items.try_into().unwrap_or_else(|_| unreachable!("length checked before conversion"))
}

impl Configuration {
    /// Reads a settings document or `configure` body (`from_json`).
    pub fn from_json(value: &Value) -> Result<Self, ModelError> {
        if !value.is_object() {
            return Err(ModelError::invalid("Settings must be a JSON object."));
        }
        let schema = json::at(value, "version")?;
        if !json::is_integer(schema) {
            return Err(ModelError::invalid("The configuration version must be a whole number."));
        }
        // Compare before narrowing: a future 64-bit schema must never wrap to
        // a supported int and become writable by an older engine.
        let future = match schema.as_u64() {
            Some(unsigned) => unsigned > 2,
            None => schema.as_i64().is_some_and(|signed| signed > 2),
        };
        if future {
            return Err(ModelError::unsupported("This workspace requires a newer version of VineMix."));
        }
        let version = schema.as_i64().unwrap_or(0);
        if version != 1 && version != 2 {
            return Err(ModelError::invalid("This configuration version is not supported."));
        }
        let sized = |key: &str, size: usize| -> Result<bool, ModelError> {
            Ok(json::at(value, key)?.as_array().is_some_and(|items| items.len() == size))
        };
        if !sized("microphones", 2)? || !sized("mainOutputs", 2)? || !sized("sources", 3)? {
            return Err(ModelError::invalid(
                "Settings require exactly two microphones, two A outputs, and three sources.",
            ));
        }
        let sources: Vec<SourceControl> = read_array(json::at(value, "sources")?, read_source)?;
        let mut config = Configuration {
            version: version as i32,
            revision: read_revision(json::at(value, "revision")?)?,
            microphones: pair(read_array(json::at(value, "microphones")?, read_device)?),
            main_outputs: pair(read_array(json::at(value, "mainOutputs")?, read_device)?),
            virtual_input_capture: read_device(json::at(value, "virtualInputCapture")?)?,
            virtual_input_playback: read_device(json::at(value, "virtualInputPlayback")?)?,
            virtual_output_render: read_device(json::at(value, "virtualOutputRender")?)?,
            virtual_output_capture: read_device(json::at(value, "virtualOutputCapture")?)?,
            sources: sources.try_into().unwrap_or_else(|_| unreachable!("length checked")),
            bus_a: read_bus(json::at(value, "busA")?)?,
            bus_b: read_bus(json::at(value, "busB")?)?,
            start_at_login: json::get_bool(json::at(value, "startAtLogin")?)?,
            graph: AudioGraph::default(),
        };
        if version == 1 {
            crate::defaults::migrate_legacy_graph(&mut config);
        } else {
            config.graph = read_graph(json::at(value, "graph")?)?;
        }
        Ok(config)
    }

    pub fn to_json(&self) -> Value {
        let sources: Vec<Value> = self.sources.iter().map(source_json).collect();
        ObjectBuilder::new()
            .set("version", self.version)
            .set("revision", self.revision)
            .set("microphones", self.microphones.iter().map(device_json).collect::<Vec<_>>())
            .set("virtualInputCapture", device_json(&self.virtual_input_capture))
            .set("virtualInputPlayback", device_json(&self.virtual_input_playback))
            .set("mainOutputs", self.main_outputs.iter().map(device_json).collect::<Vec<_>>())
            .set("virtualOutputRender", device_json(&self.virtual_output_render))
            .set("virtualOutputCapture", device_json(&self.virtual_output_capture))
            .set("sources", sources)
            .set("busA", bus_json(&self.bus_a))
            .set("busB", bus_json(&self.bus_b))
            .set("startAtLogin", self.start_at_login)
            .set("graph", graph_json(&self.graph))
            .build()
    }
}

// ---------------------------------------------------------------- writing --

fn send_json(send: &SendControl) -> Value {
    ObjectBuilder::new().set("enabled", send.enabled).set_f32("gainDb", send.gain_db).build()
}

fn source_json(source: &SourceControl) -> Value {
    ObjectBuilder::new().set("muted", source.muted).set("a", send_json(&source.a)).set("b", send_json(&source.b)).build()
}

fn bus_json(bus: &BusControl) -> Value {
    ObjectBuilder::new().set("muted", bus.muted).set_f32("gainDb", bus.gain_db).build()
}

pub fn device_json(device: &DeviceBinding) -> Value {
    ObjectBuilder::new()
        .set("id", device.id.as_str())
        .set("stableId", device.stable_id.as_str())
        .set("name", device.name.as_str())
        .set("channel", device.channel)
        .build()
}

pub fn process_json(app: &ProcessBinding) -> Value {
    ObjectBuilder::new()
        .set("processId", app.process_id)
        .set("executablePath", app.executable_path.as_str())
        .set("name", app.name.as_str())
        .build()
}

fn eq_band_json(band: &EqBand) -> Value {
    ObjectBuilder::new()
        .set("type", band.band_type.as_str())
        .set("enabled", band.enabled)
        .set_f64("frequencyHz", band.frequency_hz)
        .set_f64("gainDb", band.gain_db)
        .set_f64("q", band.q)
        .set_f64("resetFrequencyHz", band.reset_frequency_hz)
        .build()
}

pub fn node_json(node: &GraphNode) -> Value {
    let c = &node.compressor;
    let d = &node.ducking;
    let e = &node.expander;
    ObjectBuilder::new()
        .set("id", node.id.as_str())
        .set("kind", node.kind.as_str())
        .set("name", node.name.as_str())
        .set_f64("x", node.x)
        .set_f64("y", node.y)
        .set_f64("gainDb", node.gain_db)
        .set("muted", node.muted)
        .set("mono", node.mono)
        .set("device", device_json(&node.device))
        .set("app", process_json(&node.app))
        .set("eqBands", node.eq_bands.iter().map(eq_band_json).collect::<Vec<_>>())
        .set(
            "compressor",
            ObjectBuilder::new()
                .set_f64("thresholdDb", c.threshold_db)
                .set_f64("ratio", c.ratio)
                .set_f64("attackMs", c.attack_ms)
                .set_f64("releaseMs", c.release_ms)
                .set_f64("makeupDb", c.makeup_db)
                .set("mode", c.mode.as_str())
                .set("detector", c.detector.as_str())
                .set_f64("ceilingDb", c.ceiling_db)
                .build(),
        )
        .set(
            "ducking",
            ObjectBuilder::new()
                .set_f64("thresholdDb", d.threshold_db)
                .set_f64("amountDb", d.amount_db)
                .set_f64("attackMs", d.attack_ms)
                .set_f64("releaseMs", d.release_ms)
                .set("mode", d.mode.as_str())
                .set_f64("ratio", d.ratio)
                .build(),
        )
        .set("dynamicsUiVersion", node.dynamics_ui_version)
        .set(
            "expander",
            ObjectBuilder::new()
                .set("mode", e.mode.as_str())
                .set_f64("thresholdDb", e.threshold_db)
                .set_f64("ratio", e.ratio)
                .set_f64("attackMs", e.attack_ms)
                .set_f64("holdMs", e.hold_ms)
                .set_f64("releaseMs", e.release_ms)
                .set_f64("rangeDb", e.range_db)
                .set_f64("hysteresisDb", e.hysteresis_db)
                .build(),
        )
        .set("alignmentDelay", ObjectBuilder::new().set("samples", node.alignment_delay.samples).build())
        .set(
            "denoiser",
            ObjectBuilder::new()
                .set_f64("suppressionDb", node.denoiser.suppression_db)
                .set("algorithm", node.denoiser.algorithm.as_str())
                .set_f64("rnnoiseMix", node.denoiser.rnnoise_mix)
                .build(),
        )
        .set(
            "deEsser",
            ObjectBuilder::new()
                .set_f64("frequencyHz", node.de_esser.frequency_hz)
                .set_f64("thresholdDb", node.de_esser.threshold_db)
                .set_f64("reductionDb", node.de_esser.reduction_db)
                .build(),
        )
        .set(
            "plosive",
            ObjectBuilder::new()
                .set_f64("frequencyHz", node.plosive.frequency_hz)
                .set_f64("thresholdDb", node.plosive.threshold_db)
                .set_f64("reductionDb", node.plosive.reduction_db)
                .build(),
        )
        .set(
            "reverb",
            ObjectBuilder::new()
                .set_f64("mix", node.reverb.mix)
                .set_f64("decaySeconds", node.reverb.decay_seconds)
                .set_f64("damping", node.reverb.damping)
                .set_f64("preDelayMs", node.reverb.pre_delay_ms)
                .build(),
        )
        .set(
            "echo",
            ObjectBuilder::new()
                .set_f64("timeMs", node.echo.time_ms)
                .set("repeats", node.echo.repeats)
                .set_f64("mix", node.echo.mix)
                .build(),
        )
        .set(
            "distortion",
            ObjectBuilder::new()
                .set_f64("driveDb", node.distortion.drive_db)
                .set_f64("toneHz", node.distortion.tone_hz)
                .set_f64("mix", node.distortion.mix)
                .set_f64("outputDb", node.distortion.output_db)
                .build(),
        )
        .set(
            "voiceColor",
            ObjectBuilder::new().set_f64("tone", node.voice_color.tone).set_f64("ambience", node.voice_color.ambience).build(),
        )
        .set_f64("balance", node.balance)
        .set("bypass", node.bypass)
        .set("collapsed", node.collapsed)
        .set("autoAssignDevice", node.auto_assign_device)
        .set_f64("stereoWidth", node.stereo_width)
        .set_f64("panPosition", node.pan_position)
        .set_f64("crossfadePosition", node.crossfade_position)
        .set("crossfadeCurve", node.crossfade_curve.as_str())
        .set_f64("controlMultiplier", node.control_multiplier)
        .build()
}

pub fn edge_json(edge: &GraphEdge) -> Value {
    ObjectBuilder::new()
        .set("id", edge.id.as_str())
        .set("from", edge.from.as_str())
        .set("to", edge.to.as_str())
        .set("kind", edge.kind.as_str())
        .set_f64("gainDb", edge.gain_db)
        .set("muted", edge.muted)
        .set("fromPort", edge.from_port.as_str())
        .set("toPort", edge.to_port.as_str())
        .build()
}

pub fn graph_json(graph: &AudioGraph) -> Value {
    ObjectBuilder::new()
        .set("nodes", graph.nodes.iter().map(node_json).collect::<Vec<_>>())
        .set("edges", graph.edges.iter().map(edge_json).collect::<Vec<_>>())
        .set_f64("panX", graph.pan_x)
        .set_f64("panY", graph.pan_y)
        .set_f64("zoom", graph.zoom)
        .build()
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn dynamics_ui_version_is_a_bounded_integer_before_conversion() {
        assert_eq!(read_node(&json!({})).unwrap().dynamics_ui_version, 0);
        for version in [0, 1] {
            assert_eq!(read_node(&json!({"dynamicsUiVersion": version})).unwrap().dynamics_ui_version, version);
        }
        for invalid in [json!(1.5), json!(-1), json!(2), json!(4294967296u64)] {
            assert!(read_node(&json!({"dynamicsUiVersion": invalid})).is_err());
        }
    }

    #[test]
    fn default_round_trips() {
        let config = crate::defaults::default_configuration();
        let back = Configuration::from_json(&config.to_json()).unwrap();
        assert_eq!(back, config);
    }

    #[test]
    fn future_schema_is_unsupported_not_invalid() {
        let mut value = Configuration::default().to_json();
        value["version"] = json!(u64::MAX);
        let error = Configuration::from_json(&value).unwrap_err();
        assert!(error.is_unsupported());
        assert_eq!(error.to_string(), "This workspace requires a newer version of VineMix.");
        value["version"] = json!(0);
        assert_eq!(Configuration::from_json(&value).unwrap_err().to_string(), "This configuration version is not supported.");
        value["version"] = json!(2.0);
        assert_eq!(
            Configuration::from_json(&value).unwrap_err().to_string(),
            "The configuration version must be a whole number."
        );
    }

    #[test]
    fn revision_must_be_exact() {
        for bad in [json!(-1), json!(1.5), json!("1")] {
            assert!(read_revision(&bad).is_err());
        }
        assert_eq!(read_revision(&json!(u64::MAX)).unwrap(), u64::MAX);
    }

    #[test]
    fn legacy_echo_feedback_converts_to_repeats() {
        let echo = read_echo(&json!({"feedback": 0.5})).unwrap();
        // 0.5, 0.25, ... 0.0078 < 0.01 after six products.
        assert_eq!(echo.repeats, 7);
        assert_eq!(read_echo(&json!({"feedback": 0.0})).unwrap().repeats, 1);
        assert_eq!(read_echo(&json!({"feedback": 0.9, "repeats": 2})).unwrap().repeats, 2);
        assert!(read_echo(&json!({"feedback": 0.95})).is_err());
        assert!(read_echo(&json!({"repeats": 2.5})).is_err());
        assert!(read_echo(&json!({"timeMs": "300"})).is_err());
    }

    #[test]
    fn missing_node_fields_take_defaults() {
        let node = read_node(&json!({"id": "a", "kind": "eq"})).unwrap();
        assert_eq!(node.eq_bands, default_eq_bands());
        assert_eq!(node.control_multiplier, -1.0);
        assert!(node.auto_assign_device);
        assert!(read_node(&json!({"device": {"id": ""}})).is_err(), "a present device needs every key");
    }

    #[test]
    fn alignment_delay_requires_whole_samples() {
        assert_eq!(read_alignment_delay(&json!({"samples": 96000})).unwrap().samples, 96000);
        assert!(read_alignment_delay(&json!({"samples": 96001})).is_err());
        assert!(read_alignment_delay(&json!({"samples": 1.0})).is_err());
        assert!(read_alignment_delay(&json!({"samples": -1})).is_err());
    }
}
