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

/// Worker resolution ([`vinemix_model::identity`]): the active endpoint the
/// binding provably means, else the (inactive) exact match for diagnostics.
/// Names never select, and an ambiguous identity resolves to nothing.
pub fn resolve_endpoint(devices: &[Endpoint], selection: &DeviceBinding, capture: bool) -> Option<Endpoint> {
    use vinemix_model::identity::{exact, resolve, Resolution};
    let direction = if capture { "capture" } else { "render" };
    match resolve(devices, selection, direction) {
        Resolution::Active(endpoint, _) => Some(endpoint.clone()),
        Resolution::Ambiguous => None, // Never guess between ambiguous identities.
        Resolution::Unresolved => exact(devices, selection, direction).cloned(),
    }
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

/// Whether an open stream is still the right stream for `selection` in this
/// inventory: the binding still resolves to the same active endpoint with
/// the same mix format, and that endpoint is still acceptable for the slot.
/// Evaluated by the catalog thread for every open stream when the inventory
/// changes, so a stream thread never resolves bindings itself.
#[allow(clippy::too_many_arguments)]
pub fn stream_still_selected(
    devices: &[Endpoint],
    selection: &DeviceBinding,
    capture: bool,
    microphone: bool,
    required_transport: &str,
    endpoint_id: &str,
    channels: u32,
    rate: u32,
) -> bool {
    match resolve_endpoint(devices, selection, capture) {
        Some(selected) => {
            selected.state == "Active"
                && selected.id == endpoint_id
                && selected.channels == channels
                && selected.rate == rate
                && stream_endpoint_rejection(&selected, capture, microphone, required_transport).is_none()
        }
        None => false,
    }
}

/// Forgiveness for glitches around device-inventory changes, bounded so that
/// a continuous run of changes cannot exempt a stream indefinitely.
///
/// Each inventory change opens a [`TopologyGrace::SETTLE_SECONDS`] window in
/// which glitches are attributed to Windows rebuilding its audio graph. A
/// run of changes (each within [`TopologyGrace::QUIET_SECONDS`] of the last)
/// shares one allowance of [`TopologyGrace::MAXIMUM_SECONDS`] from its first
/// change; after that the stream is judged normally until the inventory has
/// been quiet for `QUIET_SECONDS`. Times are seconds on any monotonic clock.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct TopologyGrace {
    run_started: Option<f64>,
    last_change: Option<f64>,
}

impl TopologyGrace {
    pub const SETTLE_SECONDS: f64 = 5.0;
    pub const MAXIMUM_SECONDS: f64 = 15.0;
    pub const QUIET_SECONDS: f64 = 30.0;

    /// Records an inventory change at `now`. Returns whether it grants grace
    /// (recent glitches may be forgiven and the settle window applies).
    pub fn changed(&mut self, now: f64) -> bool {
        let continues = self.last_change.is_some_and(|last| now - last < Self::QUIET_SECONDS);
        if !continues {
            self.run_started = Some(now);
        }
        self.last_change = Some(now);
        self.run_started.is_some_and(|start| now - start < Self::MAXIMUM_SECONDS)
    }

    /// Whether glitches at `now` are still attributed to a recent change.
    pub fn settling(&self, now: f64) -> bool {
        match (self.run_started, self.last_change) {
            (Some(start), Some(last)) => now - last < Self::SETTLE_SECONDS && now - start < Self::MAXIMUM_SECONDS,
            _ => false,
        }
    }
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

    /// RUST-REV-015: a storm of inventory changes cannot renew the settle
    /// window forever, and ordinary isolated changes keep their full grace.
    #[test]
    fn topology_grace_is_bounded_across_a_storm() {
        let mut grace = TopologyGrace::default();
        assert!(!grace.settling(0.0));
        // An isolated change: the full settle window, then normal judgement.
        assert!(grace.changed(100.0));
        assert!(grace.settling(100.0) && grace.settling(104.9));
        assert!(!grace.settling(105.0));
        // A storm: a change every second for two minutes.
        let mut grace = TopologyGrace::default();
        let mut granted = 0;
        let mut settled_seconds = 0;
        for second in 0..120 {
            let now = 1000.0 + f64::from(second);
            if grace.changed(now) {
                granted += 1;
            }
            if grace.settling(now + 0.5) {
                settled_seconds += 1;
            }
        }
        assert_eq!(granted, 15, "grace is granted only inside the storm's first 15 s");
        assert_eq!(settled_seconds, 15, "a real underrun after that reaches the stability fallback");
        // The storm ends; after 30 quiet seconds a new change has full grace.
        assert!(!grace.settling(1125.0));
        assert!(!grace.changed(1140.0), "21 s after the last change is still the same run");
        assert!(grace.changed(1171.0), "a change after 30 quiet seconds starts a new allowance");
        assert!(grace.settling(1175.9));
        assert!(!grace.settling(1176.0));
    }

    #[test]
    fn open_stream_validity_follows_identity_format_and_slot() {
        let mut active = endpoint("a", "s", "capture", "Active");
        active.channels = 2;
        active.rate = 48_000;
        let other = endpoint("z", "t", "render", "Active");
        let bind = DeviceBinding { id: "a".into(), stable_id: "s".into(), ..DeviceBinding::default() };
        let valid = |devices: &[Endpoint]| stream_still_selected(devices, &bind, true, true, "", "a", 2, 48_000);
        assert!(valid(&[active.clone(), other.clone()]));
        // Unrelated endpoints coming, going or changing do not end the stream.
        assert!(valid(&[active.clone()]));
        let mut renamed = other.clone();
        renamed.name = "Renamed example".into();
        renamed.rate = 44_100;
        assert!(valid(&[active.clone(), renamed, endpoint("new", "", "capture", "Active")]));
        // This endpoint's format, state or identity changing does.
        let mut reformatted = active.clone();
        reformatted.rate = 44_100;
        assert!(!valid(&[reformatted]));
        let mut unplugged = active.clone();
        unplugged.state = "Unplugged".into();
        assert!(!valid(&[unplugged.clone()]));
        assert!(!valid(&[]));
        // A replacement with the same stable identity is a different stream.
        let mut replacement = endpoint("b", "s", "capture", "Active");
        replacement.channels = 2;
        replacement.rate = 48_000;
        assert!(!valid(&[unplugged, replacement]));
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
