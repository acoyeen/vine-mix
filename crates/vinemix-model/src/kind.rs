//! Node kinds and audio ports (port of `parseKind`, `isSource`, `isSink`,
//! `audioPort` in `native/src/graph.cpp`).

macro_rules! graph_kinds {
    ($($variant:ident => $name:literal),+ $(,)?) => {
        /// Every node kind accepted by configuration v2, in the C++ enum order.
        #[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
        pub enum GraphKind { $($variant),+ }

        impl GraphKind {
            pub const ALL: &'static [GraphKind] = &[$(GraphKind::$variant),+];

            /// The exact wire / settings-file spelling.
            pub const fn as_str(self) -> &'static str {
                match self { $(GraphKind::$variant => $name),+ }
            }

            pub fn parse(value: &str) -> Result<Self, UnknownKind> {
                match value {
                    $($name => Ok(GraphKind::$variant),)+
                    _ => Err(UnknownKind(value.to_owned())),
                }
            }
        }
    };
}

graph_kinds! {
    Microphone => "microphone",
    VirtualInput => "virtualInput",
    AppInput => "appInput",
    Mix => "mix",
    Gain => "gain",
    Output => "output",
    VirtualOutput => "virtualOutput",
    Control => "control",
    Eq => "eq",
    Tone => "tone",
    Compressor => "compressor",
    Pan => "pan",
    Mono => "mono",
    Reroute => "reroute",
    HighPass => "highPass",
    LowPass => "lowPass",
    Denoiser => "denoiser",
    DeEsser => "deEsser",
    Plosive => "plosive",
    Reverb => "reverb",
    VoiceColor => "voiceColor",
    Expander => "expander",
    AlignmentDelay => "alignmentDelay",
    Limiter => "limiter",
    Ducking => "ducking",
    Gate => "gate",
    ChannelSwap => "channelSwap",
    StereoWidth => "stereoWidth",
    StereoPan => "stereoPan",
    Crossfade => "crossfade",
    StereoCombine => "stereoCombine",
    StereoSplit => "stereoSplit",
    Echo => "echo",
    Distortion => "distortion",
}

/// Error carrying the exact engine message for an unknown kind.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct UnknownKind(pub String);

impl std::fmt::Display for UnknownKind {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "Unknown node type: {}", self.0)
    }
}
impl std::error::Error for UnknownKind {}

impl GraphKind {
    pub const fn is_source(self) -> bool {
        matches!(self, GraphKind::Microphone | GraphKind::VirtualInput | GraphKind::AppInput)
    }

    pub const fn is_sink(self) -> bool {
        matches!(self, GraphKind::Output | GraphKind::VirtualOutput)
    }

    pub const fn is_filter(self) -> bool {
        matches!(self, GraphKind::Eq | GraphKind::Tone | GraphKind::HighPass | GraphKind::LowPass)
    }

    pub const fn is_compressor(self) -> bool {
        matches!(self, GraphKind::Compressor | GraphKind::Limiter)
    }

    /// Kinds implemented by the C++ `AdvancedEffect` class.
    pub const fn is_advanced_effect(self) -> bool {
        matches!(
            self,
            GraphKind::Denoiser
                | GraphKind::DeEsser
                | GraphKind::Plosive
                | GraphKind::Reverb
                | GraphKind::VoiceColor
                | GraphKind::Expander
                | GraphKind::Gate
                | GraphKind::AlignmentDelay
                | GraphKind::Echo
                | GraphKind::Distortion
        )
    }

    /// Nodes that accept and produce programme audio (the C++ `processor`).
    pub const fn is_processor(self) -> bool {
        matches!(
            self,
            GraphKind::Mix
                | GraphKind::Gain
                | GraphKind::Ducking
                | GraphKind::Pan
                | GraphKind::Mono
                | GraphKind::Reroute
                | GraphKind::ChannelSwap
                | GraphKind::StereoWidth
                | GraphKind::StereoPan
                | GraphKind::Crossfade
                | GraphKind::StereoCombine
                | GraphKind::StereoSplit
        ) || self.is_filter()
            || self.is_compressor()
            || self.is_advanced_effect()
    }

    /// Kinds that publish gain-reduction / detector telemetry.
    pub const fn has_dynamics_meter(self) -> bool {
        matches!(
            self,
            GraphKind::Compressor
                | GraphKind::Limiter
                | GraphKind::Ducking
                | GraphKind::Expander
                | GraphKind::Gate
                | GraphKind::DeEsser
                | GraphKind::Plosive
        )
    }

    /// Kinds that accept a detector-only Trigger (sidechain) cable.
    pub const fn accepts_trigger(self) -> bool {
        matches!(self, GraphKind::Ducking | GraphKind::Expander | GraphKind::Gate)
    }
}

/// Audio socket on a node.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub enum GraphPort {
    #[default]
    Audio,
    Left,
    Right,
    A,
    B,
    Wet,
}

impl GraphPort {
    /// Resolves a cable's port name for `kind`, exactly as the engine does.
    /// `output` selects the node's output side.
    pub fn resolve(kind: GraphKind, port: &str, output: bool) -> Option<Self> {
        if output && matches!(kind, GraphKind::Reverb | GraphKind::Echo) && port == "wet" {
            return Some(GraphPort::Wet);
        }
        if (output && kind == GraphKind::StereoSplit) || (!output && kind == GraphKind::StereoCombine) {
            return match port {
                "left" => Some(GraphPort::Left),
                "right" => Some(GraphPort::Right),
                _ => None,
            };
        }
        if !output && kind == GraphKind::Crossfade {
            return match port {
                "a" => Some(GraphPort::A),
                "b" => Some(GraphPort::B),
                _ => None,
            };
        }
        (port == "audio").then_some(GraphPort::Audio)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_kind_round_trips() {
        assert_eq!(GraphKind::ALL.len(), 34);
        for &kind in GraphKind::ALL {
            assert_eq!(GraphKind::parse(kind.as_str()), Ok(kind));
        }
        assert_eq!(
            GraphKind::parse("Microphone").unwrap_err().to_string(),
            "Unknown node type: Microphone",
            "kinds are case-sensitive"
        );
    }

    #[test]
    fn roles_match_engine() {
        let sources: Vec<_> = GraphKind::ALL.iter().filter(|k| k.is_source()).collect();
        assert_eq!(sources.len(), 3);
        assert!(!GraphKind::Control.is_processor());
        assert!(!GraphKind::Output.is_processor());
        assert!(GraphKind::Distortion.is_processor());
        // Every kind is exactly one of source, sink, control or processor.
        for &kind in GraphKind::ALL {
            let roles = [kind.is_source(), kind.is_sink(), kind == GraphKind::Control, kind.is_processor()];
            assert_eq!(roles.iter().filter(|r| **r).count(), 1, "{kind:?}");
        }
    }

    #[test]
    fn ports_follow_socket_rules() {
        use GraphKind::*;
        assert_eq!(GraphPort::resolve(Reverb, "wet", true), Some(GraphPort::Wet));
        assert_eq!(GraphPort::resolve(Reverb, "wet", false), None, "Wet is output-only");
        assert_eq!(GraphPort::resolve(StereoSplit, "left", true), Some(GraphPort::Left));
        assert_eq!(GraphPort::resolve(StereoSplit, "audio", true), None);
        assert_eq!(GraphPort::resolve(StereoSplit, "audio", false), Some(GraphPort::Audio));
        assert_eq!(GraphPort::resolve(StereoCombine, "right", false), Some(GraphPort::Right));
        assert_eq!(GraphPort::resolve(Crossfade, "b", false), Some(GraphPort::B));
        assert_eq!(GraphPort::resolve(Crossfade, "b", true), None);
        assert_eq!(GraphPort::resolve(Mix, "audio", true), Some(GraphPort::Audio));
    }
}
