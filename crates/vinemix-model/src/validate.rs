//! `validateConfiguration`: everything a worker or the store relies on before
//! a configuration is accepted, ending with full graph compilation.

use crate::config::{Configuration, DeviceBinding};
use crate::plan::{compile_graph, GraphPlan};
use crate::ModelError;

fn check_bus_gain(value: f32) -> Result<(), ModelError> {
    if !value.is_finite() || !(-60.0..=12.0).contains(&value) {
        return Err(ModelError::invalid("Volume must be between -60 and +12 dB."));
    }
    Ok(())
}

fn check_binding(device: &DeviceBinding) -> Result<(), ModelError> {
    if device.id.len() > 4096 || device.stable_id.len() > 4096 || device.name.len() > 1024 {
        return Err(ModelError::invalid("The device identifier is too long."));
    }
    if !(-1..=255).contains(&device.channel) {
        return Err(ModelError::invalid("The selected microphone channel is invalid."));
    }
    // The C++ engine also re-validates UTF-8 here; a Rust `String` is always
    // valid UTF-8, so the encoding check holds by construction.
    Ok(())
}

/// Proof that a configuration passed [`validate_and_compile`]. It borrows the
/// configuration, which therefore cannot change while the proof exists, so a
/// later boundary (the settings store) can rely on the validation that
/// already ran instead of compiling the graph again.
#[derive(Clone, Copy, Debug)]
pub struct Validated<'a>(&'a Configuration);

impl<'a> Validated<'a> {
    pub fn configuration(self) -> &'a Configuration {
        self.0
    }
}

pub fn validate_configuration(config: &Configuration) -> Result<(), ModelError> {
    validate_and_compile(config).map(|_| ())
}

/// Full validation, returning the compiled plan and the proof of validation
/// so that accepting an edit compiles its graph exactly once.
pub fn validate_and_compile(config: &Configuration) -> Result<(Validated<'_>, GraphPlan), ModelError> {
    if config.version != 2 {
        return Err(ModelError::invalid("This configuration version is not supported."));
    }
    for source in &config.sources {
        check_bus_gain(source.a.gain_db)?;
        check_bus_gain(source.b.gain_db)?;
    }
    check_bus_gain(config.bus_a.gain_db)?;
    check_bus_gain(config.bus_b.gain_db)?;
    for device in config.microphones.iter().chain(&config.main_outputs) {
        check_binding(device)?;
    }
    for device in [
        &config.virtual_input_capture,
        &config.virtual_input_playback,
        &config.virtual_output_render,
        &config.virtual_output_capture,
    ] {
        check_binding(device)?;
    }
    for node in &config.graph.nodes {
        check_binding(&node.device)?;
        if node.app.name.len() > 1024 || node.app.executable_path.len() > 32768 {
            return Err(ModelError::invalid("The saved application identity is too long."));
        }
        if node.kind == "appInput" && node.app.process_id != 0 && node.app.executable_path.is_empty() {
            return Err(ModelError::invalid(
                "Select the application again so its executable identity can be saved safely.",
            ));
        }
    }
    compile_graph(&config.graph).map(|plan| (Validated(config), plan))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::defaults::default_configuration;

    #[test]
    fn rejects_out_of_range_bindings_and_legacy_gains() {
        let mut config = default_configuration();
        config.sources[0].a.gain_db = 12.5;
        assert_eq!(validate_configuration(&config).unwrap_err().to_string(), "Volume must be between -60 and +12 dB.");
        let mut config = default_configuration();
        config.graph.nodes[0].device.channel = 256;
        assert_eq!(validate_configuration(&config).unwrap_err().to_string(), "The selected microphone channel is invalid.");
        let mut config = default_configuration();
        config.graph.nodes[0].kind = "appInput".into();
        config.graph.nodes[0].app.process_id = 42;
        assert!(validate_configuration(&config).unwrap_err().to_string().starts_with("Select the application again"));
    }
}
