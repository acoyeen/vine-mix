//! The single source of truth for VineMix's configuration model.
//!
//! The 0.5 product kept the schema, defaults, ranges and validation twice (C++
//! engine and C# desktop) in sync by hand. Engine, UI and tests all depend on
//! this crate instead. Validation messages are kept verbatim because the
//! desktop shows them to users.

pub mod config;
pub mod defaults;
pub mod distortion;
pub mod identity;
pub mod json;
pub mod kind;
pub mod limits;
pub mod plan;
pub mod status;
pub mod validate;
pub mod voice;

pub use config::{AudioGraph, Configuration, DeviceBinding, EqBand, GraphEdge, GraphNode, ProcessBinding};
pub use defaults::{default_configuration, migrate_legacy_graph};
pub use kind::{GraphKind, GraphPort, UnknownKind};
pub use limits::{check_gain_db, check_id};
pub use plan::{compile_graph, graph_signal_channels, same_graph_topology, same_indexing, Biquad, GraphPlan, PlanEdge, PlanNode};
pub use status::{ApplicationInfo, EngineStatus, Endpoint, NodeStatus, PathState, ProcessingLoadState, ProcessingLoadStatus};
pub use validate::{validate_and_compile, validate_configuration, Validated};

/// The engine's fixed processing rate.
pub const SAMPLE_RATE: u32 = 48_000;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ErrorKind {
    /// Malformed or out-of-range input; the message is shown to the user.
    Invalid,
    /// A workspace written by a newer VineMix. Never overwritten.
    Unsupported,
}

/// A rejected document or configuration, carrying the user-facing message.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ModelError {
    pub kind: ErrorKind,
    pub message: String,
}

impl ModelError {
    pub fn invalid(message: impl Into<String>) -> Self {
        Self { kind: ErrorKind::Invalid, message: message.into() }
    }

    pub fn unsupported(message: impl Into<String>) -> Self {
        Self { kind: ErrorKind::Unsupported, message: message.into() }
    }

    pub fn is_unsupported(&self) -> bool {
        self.kind == ErrorKind::Unsupported
    }
}

impl std::fmt::Display for ModelError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}

impl std::error::Error for ModelError {}
