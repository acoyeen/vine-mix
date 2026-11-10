//! Stateful effects with auxiliary memory (`AdvancedEffect` in C++).
//!
//! All storage is allocated by [`AdvancedEffect::new`], on the control thread
//! during runtime preparation. Every later call is bounded, with no
//! allocation, locking or I/O.

pub mod colour;
pub mod delay;
pub mod denoise;
pub mod dynamics;

use vinemix_model::config::{
    AlignmentDelaySettings, DeEsserSettings, DenoiserSettings, DistortionSettings, EchoSettings, ExpanderSettings,
    PlosiveSettings, ReverbSettings, VoiceColorSettings,
};
use vinemix_model::plan::PlanNode;
use vinemix_model::GraphKind;
use vinemix_rt::Frame;

use crate::util::clean_f32;
use colour::{Distortion, Reverb, VoiceColor};
use delay::{AlignmentDelay, Echo};
use denoise::Denoise;
use dynamics::{BandReduction, Expander};

/// One frame of effect output.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct EffectResult {
    /// Processed main signal, including the effect's own Mix.
    pub wet: Frame,
    /// Dry signal aligned with `wet` (delayed by the effect's latency).
    pub dry: Frame,
    /// Reverb or Echo contribution before Mix, for the Wet output port.
    pub effect_wet: Frame,
}

enum Engine {
    Denoise(Box<Denoise>),
    Reverb(Box<Reverb>),
    VoiceColor(Box<VoiceColor>, Box<Reverb>),
    AlignmentDelay(Box<AlignmentDelay>),
    Echo(Box<Echo>),
    Distortion(Box<Distortion>),
    Expander(Expander),
    Band(BandReduction),
}

/// Copyable image of [`DenoiserSettings`]: the validated algorithm name is
/// reduced to a flag so storing a node's settings never touches the heap.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
struct DenoiserKey {
    suppression_db: f64,
    neural: bool,
    rnnoise_mix: f64,
}

impl DenoiserKey {
    fn of(s: &DenoiserSettings) -> Self {
        Self { suppression_db: s.suppression_db, neural: s.algorithm == "rnnoise", rnnoise_mix: s.rnnoise_mix }
    }
}

/// Copyable image of [`ExpanderSettings`] (mode name reduced to a flag).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
struct ExpanderKey {
    gate: bool,
    threshold_db: f64,
    ratio: f64,
    attack_ms: f64,
    hold_ms: f64,
    release_ms: f64,
    range_db: f64,
    hysteresis_db: f64,
}

impl ExpanderKey {
    fn of(s: &ExpanderSettings) -> Self {
        Self {
            gate: s.mode == "gate",
            threshold_db: s.threshold_db,
            ratio: s.ratio,
            attack_ms: s.attack_ms,
            hold_ms: s.hold_ms,
            release_ms: s.release_ms,
            range_db: s.range_db,
            hysteresis_db: s.hysteresis_db,
        }
    }
}

/// The settings an effect was last configured with. Every field is `Copy`:
/// `configure` runs inside `GraphProcessor::process` on the mixer thread, and
/// `inherit`/`adopt` run there too, so recording or comparing settings must
/// never allocate or free. Plan compilation has already validated the mode
/// names, so the flags carry the same information as the strings.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
struct Settings {
    denoiser: DenoiserKey,
    de_esser: DeEsserSettings,
    plosive: PlosiveSettings,
    reverb: ReverbSettings,
    voice_color: VoiceColorSettings,
    expander: ExpanderKey,
    alignment_delay: AlignmentDelaySettings,
    echo: EchoSettings,
    distortion: DistortionSettings,
}

impl Settings {
    fn of(node: &PlanNode) -> Self {
        Self {
            denoiser: DenoiserKey::of(&node.denoiser),
            de_esser: node.de_esser,
            plosive: node.plosive,
            reverb: node.reverb,
            voice_color: node.voice_color,
            expander: ExpanderKey::of(&node.expander),
            alignment_delay: node.alignment_delay,
            echo: node.echo,
            distortion: node.distortion,
        }
    }
}

pub struct AdvancedEffect {
    kind: GraphKind,
    engine: Engine,
    settings: Settings,
    configured: bool,
}

impl std::fmt::Debug for AdvancedEffect {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AdvancedEffect").field("kind", &self.kind).field("configured", &self.configured).finish()
    }
}

impl AdvancedEffect {
    pub fn supports(kind: GraphKind) -> bool {
        kind.is_advanced_effect()
    }

    /// Processing latency in frames, for alignment and display.
    pub fn latency_frames(kind: GraphKind, algorithm: &str) -> u32 {
        match kind {
            GraphKind::Denoiser if algorithm == "rnnoise" => 1440,
            GraphKind::Denoiser => 960,
            GraphKind::Distortion => 32,
            _ => 0,
        }
    }

    /// Allocates all state for `kind`. Call off the audio thread.
    pub fn new(kind: GraphKind) -> Option<Self> {
        let engine = match kind {
            GraphKind::Denoiser => Engine::Denoise(Box::new(Denoise::new())),
            GraphKind::Reverb => Engine::Reverb(Reverb::boxed()),
            GraphKind::VoiceColor => Engine::VoiceColor(Box::default(), Reverb::boxed()),
            GraphKind::AlignmentDelay => Engine::AlignmentDelay(Box::new(AlignmentDelay::new())),
            GraphKind::Echo => Engine::Echo(Box::new(Echo::new())),
            GraphKind::Distortion => Engine::Distortion(Box::new(Distortion::new())),
            GraphKind::Expander | GraphKind::Gate => Engine::Expander(Expander::default()),
            GraphKind::DeEsser => Engine::Band(BandReduction::new(true)),
            GraphKind::Plosive => Engine::Band(BandReduction::new(false)),
            _ => return None,
        };
        Some(Self { kind, engine, settings: Settings::default(), configured: false })
    }

    pub fn kind(&self) -> GraphKind {
        self.kind
    }

    /// Applies a node's settings. Unchanged settings are a no-op, so a plan
    /// re-published for an unrelated edit never restarts a ramp.
    pub fn configure(&mut self, node: &PlanNode) {
        let settings = Settings::of(node);
        if self.configured && self.settings == settings {
            return;
        }
        let first = !self.configured;
        self.settings = settings;
        self.configured = true;
        match &mut self.engine {
            Engine::Expander(expander) => expander.configure(&node.expander),
            Engine::AlignmentDelay(delay) => delay.configure(node.alignment_delay.samples),
            Engine::Echo(echo) => echo.configure(&node.echo),
            Engine::Distortion(distortion) => distortion.configure(&node.distortion),
            Engine::Denoise(denoise) => denoise.configure(&node.denoiser, first),
            Engine::VoiceColor(voice, reverb) => {
                voice.configure(node);
                reverb.configure(&node.reverb);
            }
            Engine::Reverb(reverb) => reverb.configure(&node.reverb),
            Engine::Band(band) => {
                if band.high {
                    band.configure(node.de_esser.frequency_hz, node.de_esser.threshold_db, node.de_esser.reduction_db);
                } else {
                    band.configure(node.plosive.frequency_hz, node.plosive.threshold_db, node.plosive.reduction_db);
                }
            }
        }
    }

    /// `detector` is the external trigger. `None` keeps self-detection; a
    /// connected silent trigger stays silent and never falls back.
    pub fn process(&mut self, input: Frame, detector: Option<Frame>) -> EffectResult {
        let input = Frame::new(clean_f32(input.left), clean_f32(input.right));
        let plain = |wet| EffectResult { wet, dry: input, effect_wet: Frame::SILENT };
        match &mut self.engine {
            Engine::Denoise(denoise) => {
                let result = denoise.process(input);
                EffectResult { wet: result.wet, dry: result.dry, effect_wet: Frame::SILENT }
            }
            Engine::AlignmentDelay(delay) => plain(delay.process(input)),
            Engine::Echo(echo) => {
                let result = echo.process(input);
                EffectResult { wet: result.mixed, dry: result.dry, effect_wet: result.wet }
            }
            Engine::Distortion(distortion) => {
                let (wet, dry) = distortion.process(input);
                EffectResult { wet, dry, effect_wet: Frame::SILENT }
            }
            Engine::Expander(expander) => {
                let signal = detector.map_or(input, |d| Frame::new(clean_f32(d.left), clean_f32(d.right)));
                plain(expander.process(input, signal))
            }
            Engine::VoiceColor(voice, reverb) => plain(reverb.process(voice.process(input)).mixed),
            Engine::Reverb(reverb) => {
                let result = reverb.process(input);
                EffectResult { wet: result.mixed, dry: input, effect_wet: result.wet }
            }
            Engine::Band(band) => plain(band.process(input)),
        }
    }

    /// Copies a predecessor's complete state (same kind only).
    pub fn inherit(&mut self, previous: &Self) {
        if self.kind != previous.kind {
            return;
        }
        match (&mut self.engine, &previous.engine) {
            (Engine::Denoise(a), Engine::Denoise(b)) => a.inherit(b),
            (Engine::Reverb(a), Engine::Reverb(b)) => **a = **b,
            (Engine::VoiceColor(a, ra), Engine::VoiceColor(b, rb)) => {
                **a = **b;
                **ra = **rb;
            }
            (Engine::AlignmentDelay(a), Engine::AlignmentDelay(b)) => a.copy_from(b),
            (Engine::Echo(a), Engine::Echo(b)) => a.inherit(b),
            (Engine::Distortion(a), Engine::Distortion(b)) => **a = **b,
            (Engine::Expander(a), Engine::Expander(b)) => *a = *b,
            (Engine::Band(a), Engine::Band(b)) => *a = *b,
            _ => {}
        }
        self.settings = previous.settings;
        self.configured = previous.configured;
    }

    /// Engine-only ownership transfer. Echo moves its long history instead of
    /// copying it; every other kind inherits.
    pub fn adopt(&mut self, previous: &mut Self, preserve_fade: bool) {
        if self.kind != previous.kind {
            return;
        }
        match (&mut self.engine, &mut previous.engine) {
            (Engine::Echo(a), Engine::Echo(b)) => {
                a.adopt(b, preserve_fade);
                self.settings.echo = previous.settings.echo;
                self.configured = previous.configured;
            }
            _ => self.inherit(previous),
        }
    }

    pub fn reset(&mut self) {
        match &mut self.engine {
            Engine::Denoise(denoise) => denoise.reset(),
            Engine::Reverb(reverb) => reverb.reset(),
            Engine::VoiceColor(voice, reverb) => {
                **voice = VoiceColor::default();
                reverb.reset();
            }
            Engine::Echo(echo) => echo.reset(),
            Engine::Distortion(distortion) => distortion.reset(),
            Engine::AlignmentDelay(delay) => delay.reset(),
            Engine::Expander(expander) => *expander = Expander::default(),
            Engine::Band(band) => *band = BandReduction::new(band.high),
        }
        self.configured = false;
    }

    /// Current gain reduction in dB (dynamics kinds only).
    pub fn gain_reduction_db(&self) -> f32 {
        let attenuation = match &self.engine {
            Engine::Expander(expander) => expander.attenuation,
            Engine::Band(band) => band.attenuation,
            _ => return 0.0,
        };
        (-20.0 * attenuation.clamp(1e-12, 1.0).log10()) as f32
    }

    /// Detector level in dBFS, floored at -96 dB.
    pub fn detector_db(&self) -> f32 {
        let envelope = match &self.engine {
            Engine::Expander(expander) => expander.envelope,
            Engine::Band(band) => band.envelope,
            _ => return -96.0,
        };
        (20.0 * envelope.max(1.584_893_192_461_114e-5).log10()) as f32
    }
}
