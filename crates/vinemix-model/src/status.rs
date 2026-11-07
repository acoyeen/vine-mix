//! Engine status, endpoint and application records carried by `snapshot`.
//!
//! These are presentation and telemetry records, so they use serde with
//! lenient defaults: a newer engine may add fields an older UI ignores.

use serde::{Deserialize, Serialize};

/// A Windows audio endpoint as enumerated by the device catalog.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Endpoint {
    pub id: String,
    pub stable_id: String,
    pub name: String,
    /// `"capture"` or `"render"`.
    pub direction: String,
    /// `"Active"`, `"Disabled"`, `"NotPresent"` or `"Unplugged"`.
    pub state: String,
    pub is_virtual: bool,
    pub channels: u32,
    pub rate: u32,
    /// Routing-cable family (`"vbCable"`, `"vacLite"`) or bus type.
    pub transport: String,
    /// Windows `EndpointFormFactor`; `-1` when the property is unavailable.
    pub form_factor: i32,
    pub description: String,
    pub interface_name: String,
}

impl Default for Endpoint {
    fn default() -> Self {
        Self {
            id: String::new(),
            stable_id: String::new(),
            name: String::new(),
            direction: String::new(),
            state: String::new(),
            is_virtual: false,
            channels: 0,
            rate: 0,
            transport: String::new(),
            form_factor: -1,
            description: String::new(),
            interface_name: String::new(),
        }
    }
}

impl Endpoint {
    /// A streamed headset is a listening destination even when its driver is
    /// virtual; known routing cables keep their dedicated node policy.
    pub fn is_listening(&self) -> bool {
        self.direction == "render" && self.transport != "vbCable" && self.transport != "vacLite"
    }

    pub fn is_active(&self) -> bool {
        self.state == "Active"
    }
}

#[derive(Clone, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ApplicationInfo {
    pub process_id: u32,
    pub executable_path: String,
    pub name: String,
    pub has_audio_session: bool,
}

/// One stream path's health. `reason` is the stable behaviour contract;
/// `message` is presentation only.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct PathState {
    pub state: String,
    pub message: String,
    pub reason: String,
    pub endpoint_id: String,
    pub rate: u32,
    pub channels: u32,
    pub underruns: u64,
    pub overruns: u64,
    pub reconnects: u64,
    pub queue_ms: f64,
    pub period_ms: f64,
    pub buffer_ms: f64,
    pub target_queue_ms: f64,
    pub resampler_pending_ms: f64,
}

impl Default for PathState {
    fn default() -> Self {
        Self {
            state: "Unassigned".into(),
            message: String::new(),
            reason: String::new(),
            endpoint_id: String::new(),
            rate: 0,
            channels: 0,
            underruns: 0,
            overruns: 0,
            reconnects: 0,
            queue_ms: 0.0,
            period_ms: 0.0,
            buffer_ms: 0.0,
            target_queue_ms: 0.0,
            resampler_pending_ms: 0.0,
        }
    }
}

impl PathState {
    pub fn with(state: &str, message: &str, reason: &str) -> Self {
        Self { state: state.into(), message: message.into(), reason: reason.into(), ..Self::default() }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct NodeStatus {
    pub id: String,
    pub peak: f32,
    pub peak_left: f32,
    pub peak_right: f32,
    pub channels: i32,
    pub clipped: bool,
    pub gain_reduction_db: f32,
    pub detector_db: f32,
    pub dynamics_frame: u64,
    pub spectrum_db: Vec<f32>,
    pub spectrum_frame: u64,
    pub spectrum_revision: u64,
    pub path: PathState,
}

impl Default for NodeStatus {
    fn default() -> Self {
        Self {
            id: String::new(),
            peak: 0.0,
            peak_left: 0.0,
            peak_right: 0.0,
            channels: 0,
            clipped: false,
            gain_reduction_db: 0.0,
            detector_db: -96.0,
            dynamics_frame: 0,
            spectrum_db: Vec::new(),
            spectrum_frame: 0,
            spectrum_revision: 0,
            path: PathState::default(),
        }
    }
}

/// Whole-mixer processing pressure (`ProcessingLoadState`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum ProcessingLoadState {
    #[default]
    Normal,
    Busy,
    Overloaded,
    /// A state this build does not know; presented as unavailable.
    #[serde(other)]
    Unknown,
}

/// Measured mixer time against the 5 ms processing window
/// (`ProcessingLoadStatus`). Diagnostics only: never saved, never used to
/// change scheduling, sound or graph limits.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct ProcessingLoadStatus {
    pub available: bool,
    pub budget_ms: f64,
    pub sample_count: u64,
    pub window_samples: u32,
    pub average_percent: f64,
    pub peak_percent: f64,
    pub steady_peak_percent: f64,
    pub transition_peak_percent: f64,
    pub processing_overruns: u64,
    pub deadline_misses: u64,
    pub recent_processing_overruns: u32,
    pub recent_deadline_misses: u32,
    pub state: ProcessingLoadState,
    pub transition_active: bool,
    pub transition_headroom_limited: bool,
}

impl Default for ProcessingLoadStatus {
    fn default() -> Self {
        Self {
            available: false,
            // 240 frames at 48 kHz (`quantumFrames` / `sampleRate`).
            budget_ms: 1000.0 * 240.0 / f64::from(crate::SAMPLE_RATE),
            sample_count: 0,
            window_samples: 0,
            average_percent: 0.0,
            peak_percent: 0.0,
            steady_peak_percent: 0.0,
            transition_peak_percent: 0.0,
            processing_overruns: 0,
            deadline_misses: 0,
            recent_processing_overruns: 0,
            recent_deadline_misses: 0,
            state: ProcessingLoadState::Normal,
            transition_active: false,
            transition_headroom_limited: false,
        }
    }
}

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct EngineStatus {
    pub audio_frames: u64,
    /// Absent from engines that predate load telemetry.
    pub processing_load: Option<ProcessingLoadStatus>,
    pub source_peaks: [f32; 3],
    pub peak_a: f32,
    pub peak_b: f32,
    pub clip_a: bool,
    pub clip_b: bool,
    pub inputs: [PathState; 3],
    pub outputs: [PathState; 3],
    pub nodes: Vec<NodeStatus>,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn status_uses_wire_names_and_defaults() {
        let value = serde_json::to_value(NodeStatus::default()).unwrap();
        assert_eq!(value["detectorDb"], -96.0);
        assert_eq!(value["path"]["state"], "Unassigned");
        let parsed: Endpoint = serde_json::from_str(r#"{"id":"x","isVirtual":true,"extra":1}"#).unwrap();
        assert!(parsed.is_virtual);
        assert_eq!(parsed.form_factor, -1);
    }
}
