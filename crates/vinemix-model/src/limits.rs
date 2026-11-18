//! Graph bounds and the shared identifier/volume rules, with the engine's
//! exact user-facing messages (`checkGain`/`checkId` in `graph.cpp`).

use crate::ModelError;

pub const MAXIMUM_GRAPH_NODES: usize = 32;
pub const MAXIMUM_GRAPH_EDGES: usize = 96;
pub const MAXIMUM_EQ_BANDS: usize = 8;
pub const MAXIMUM_SETTINGS_BYTES: usize = 262_144;
pub const MAXIMUM_CONTROL_MESSAGE_BYTES: usize = 256 * 1024;
/// Maximum payload for an explicitly negotiated length-prefixed snapshot.
/// Requests and ordinary v1 reply lines retain their original limit.
pub const MAXIMUM_CONTROL_RESPONSE_BYTES: usize = 16 * 1024 * 1024;
pub const CONFIGURATION_VERSION: i32 = 2;
pub const CONTROL_PROTOCOL_VERSION: i32 = 1;

/// Node and cable volumes: finite, -60 to +12 dB.
pub fn check_gain_db(value: f64) -> Result<(), ModelError> {
    if !value.is_finite() || !(-60.0..=12.0).contains(&value) {
        return Err(ModelError::invalid("Node and cable volumes must be between -60 and +12 dB."));
    }
    Ok(())
}

/// Identifiers: 1 to 64 bytes, none below 0x20.
pub fn check_id(id: &str) -> Result<(), ModelError> {
    if id.is_empty() || id.len() > 64 || id.bytes().any(|b| b < 32) {
        return Err(ModelError::invalid("Node and cable identifiers must contain 1 to 64 printable bytes."));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn gain_bounds() {
        assert!(check_gain_db(-60.0).is_ok());
        assert!(check_gain_db(12.0).is_ok());
        assert!(check_gain_db(12.000_001).is_err());
        assert!(check_gain_db(f64::NAN).is_err());
        assert!(check_gain_db(f64::NEG_INFINITY).is_err());
    }

    #[test]
    fn id_bounds_are_bytes_not_chars() {
        assert!(check_id("mic1").is_ok());
        assert!(check_id("").is_err());
        assert!(check_id(&"a".repeat(64)).is_ok());
        assert!(check_id(&"a".repeat(65)).is_err());
        assert!(check_id(&"é".repeat(33)).is_err(), "66 UTF-8 bytes");
        assert!(check_id("tab\there").is_err());
    }
}
