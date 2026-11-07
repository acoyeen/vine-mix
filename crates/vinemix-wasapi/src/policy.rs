//! Device identity and stream-selection policy, shared by enumeration,
//! workers and validation (`windows_audio.cpp`, `engine.cpp`). Platform-free.

use vinemix_model::config::DeviceBinding;
use vinemix_model::status::Endpoint;

/// Whether a device description names a known virtual/routing driver.
pub fn virtual_name(description: &str) -> bool {
    let lower = description.to_ascii_lowercase();
    const MARKERS: [&str; 12] = [
        "virtual",
        "voicemeeter",
        "vb-audio",
        "vbaudio",
        "vb cable",
        "vb-cable",
        "cable input",
        "cable output",
        "motiv mix",
        "motive mix",
        "steelseries sonar",
        "wave link",
    ];
    MARKERS.iter().any(|marker| lower.contains(marker))
}

/// The routing-cable family named by a driver/interface description. A
/// user-facing endpoint name alone never identifies a cable family.
pub fn transport_family(device_description: &str, is_virtual: bool) -> &'static str {
    let compact: String = device_description
        .bytes()
        .filter(u8::is_ascii_alphanumeric)
        .map(|b| char::from(b.to_ascii_lowercase()))
        .collect();
    if compact.contains("voicemeeter") {
        return "otherVirtual";
    }
    if compact.contains("vbaudiovirtualcable") || compact.contains("vbcable") || compact.contains("vbaudiocable") {
        return "vbCable";
    }
    if compact.contains("virtualaudiocable") {
        return "vacLite";
    }
    if is_virtual { "otherVirtual" } else { "physical" }
}

/// `DEVICE_STATE_*` bits to the wire spelling.
pub fn endpoint_state(state: u32) -> &'static str {
    const ACTIVE: u32 = 0x1;
    const DISABLED: u32 = 0x2;
    const UNPLUGGED: u32 = 0x8;
    if state & ACTIVE != 0 {
        "Active"
    } else if state & DISABLED != 0 {
        "Disabled"
    } else if state & UNPLUGGED != 0 {
        "Unplugged"
    } else {
        "Missing"
    }
}

/// Worker resolution: an active exact ID, else a unique active stable ID,
/// else the (inactive) exact match for diagnostics. Names never select.
pub fn resolve_endpoint(devices: &[Endpoint], selection: &DeviceBinding, capture: bool) -> Option<Endpoint> {
    let direction = if capture { "capture" } else { "render" };
    let exact = devices
        .iter()
        .find(|endpoint| !selection.id.is_empty() && endpoint.id == selection.id && endpoint.direction == direction);
    if let Some(endpoint) = exact.filter(|e| e.state == "Active") {
        return Some(endpoint.clone());
    }
    if !selection.stable_id.is_empty() {
        let mut stable: Option<&Endpoint> = None;
        for endpoint in devices {
            if endpoint.direction == direction && endpoint.stable_id == selection.stable_id && endpoint.state == "Active" {
                if stable.is_some() {
                    return None; // Never guess between ambiguous identities.
                }
                stable = Some(endpoint);
            }
        }
        if let Some(endpoint) = stable {
            return Some(endpoint.clone());
        }
    }
    exact.cloned()
}

/// Why `endpoint` cannot back this stream, or `None` when it can.
pub fn stream_endpoint_rejection(
    endpoint: &Endpoint,
    capture: bool,
    microphone: bool,
    required_transport: &str,
) -> Option<&'static str> {
    if endpoint.direction != if capture { "capture" } else { "render" } {
        return Some("The device has the wrong audio direction for this node");
    }
    if required_transport == "listening" {
        return if !capture && endpoint.is_listening() {
            None
        } else {
            Some("Choose a listening device here; use Virtual output for VAC")
        };
    }
    if !required_transport.is_empty() && endpoint.transport != required_transport {
        return Some(match required_transport {
            "physical" => "Select a physical device in this slot",
            "vbCable" => "Select the VB-CABLE receive endpoint in setup",
            "vacLite" => "Select the VAC Lite send endpoint in setup",
            _ => "Device does not match the required audio transport",
        });
    }
    if capture && !microphone && !endpoint.is_virtual {
        return Some("Select the virtual cable receive endpoint in setup");
    }
    None
}

/// Field-wise equality used to decide whether the catalog changed.
pub fn same_endpoints(a: &[Endpoint], b: &[Endpoint]) -> bool {
    a == b
}

#[cfg(test)]
mod tests {
    use super::*;

    fn endpoint(id: &str, stable: &str, direction: &str, state: &str) -> Endpoint {
        Endpoint {
            id: id.into(),
            stable_id: stable.into(),
            direction: direction.into(),
            state: state.into(),
            transport: "physical".into(),
            ..Endpoint::default()
        }
    }

    #[test]
    fn exact_active_id_wins_then_unique_stable_id() {
        let devices = vec![
            endpoint("a", "s", "capture", "Unplugged"),
            endpoint("b", "s", "capture", "Active"),
            endpoint("c", "t", "capture", "Active"),
            endpoint("d", "t", "capture", "Active"),
        ];
        let bind = |id: &str, stable: &str| DeviceBinding { id: id.into(), stable_id: stable.into(), ..DeviceBinding::default() };
        assert_eq!(resolve_endpoint(&devices, &bind("a", "s"), true).unwrap().id, "b");
        assert!(resolve_endpoint(&devices, &bind("", "t"), true).is_none(), "ambiguous stable identity");
        assert_eq!(resolve_endpoint(&devices, &bind("a", "zz"), true).unwrap().id, "a", "diagnosable exact match");
        assert!(resolve_endpoint(&devices, &bind("a", "s"), false).is_none(), "direction matters");
    }

    #[test]
    fn transport_families() {
        assert_eq!(transport_family("VB-Audio Virtual Cable", true), "vbCable");
        assert_eq!(transport_family("Virtual Audio Cable", true), "vacLite");
        assert_eq!(transport_family("VoiceMeeter Input", true), "otherVirtual");
        assert_eq!(transport_family("Realtek(R) Audio", false), "physical");
        assert!(virtual_name("CABLE Output (VB-Audio Virtual Cable)"));
        assert!(!virtual_name("Headset Microphone"));
    }

    #[test]
    fn rejections_explain_the_slot() {
        let mut vac = endpoint("v", "", "render", "Active");
        vac.transport = "vacLite".into();
        assert_eq!(
            stream_endpoint_rejection(&vac, false, false, "listening"),
            Some("Choose a listening device here; use Virtual output for VAC")
        );
        assert_eq!(stream_endpoint_rejection(&vac, false, false, "vacLite"), None);
        let mic = endpoint("m", "", "capture", "Active");
        assert_eq!(stream_endpoint_rejection(&mic, true, true, ""), None);
        assert_eq!(
            stream_endpoint_rejection(&mic, true, false, "vbCable"),
            Some("Select the VB-CABLE receive endpoint in setup")
        );
    }
}
