//! VineMix signal processing: the graph processor, filters, dynamics,
//! effects and the live spectrum analyser. Platform-free.
//!
//! Ported from `graph.cpp`, `advanced_effects.cpp` and `spectrum.hpp` of the
//! 0.5 engine with identical operation order and precision, so both engines
//! produce the same samples for the same plan and input. Effects are
//! dispatched through an enum rather than a virtual call per sample.
//!
//! Voice Color and Distortion are the exceptions: [`effects::voice`] and
//! [`effects::distortion`] are original designs that replaced the 0.5 nodes
//! and are not expected to match them.

pub mod effects;
pub mod processor;
pub mod spectrum;
pub mod util;

pub use effects::AdvancedEffect;
pub use processor::{GraphProcessor, Inputs, Meter, Ramp};
pub use spectrum::{live_spectrum, SpectrumConsumer, SpectrumProducer, View as SpectrumView};
