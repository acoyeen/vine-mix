//! Device chooser lists and input/output availability notices
//! (`DeviceChoices`, `InputAvailability`, `OutputAvailability`).
//!
//! Presentation only: these never change an assignment, retry a stream or
//! infer availability from signal level.

use vinemix_model::config::{DeviceBinding, GraphNode};
use vinemix_model::identity;
use vinemix_model::status::{ApplicationInfo, Endpoint, NodeStatus, PathState};

use crate::graph::{bindings, reasons};
use crate::text;

#[derive(Clone, Debug, Default, PartialEq)]
pub struct EndpointChoice {
    pub id: String,
    pub stable_id: String,
    pub name: String,
    pub state: String,
    pub channels: u32,
    pub selectable: bool,
    pub current: bool,
    pub display_name: Option<String>,
    pub help: String,
}

impl EndpointChoice {
    fn unassigned() -> Self {
        Self { selectable: true, help: "Leave this node without a device assignment.".into(), ..Default::default() }
    }

    pub fn label(&self) -> String {
        if let Some(display) = &self.display_name {
            return display.clone();
        }
        if self.id.is_empty() && self.stable_id.is_empty() {
            return "Not assigned".into();
        }
        if self.state == "Active" { self.name.clone() } else { format!("{} · {}", self.name, self.state) }
    }

    /// Stable identity for list highlighting: the opaque endpoint ID and
    /// stable ID exactly as reported (never the display name or position).
    pub fn key(&self) -> String {
        format!("{}\u{1f}{}", self.id, self.stable_id)
    }

    pub fn binding(&self, channel: i32) -> DeviceBinding {
        DeviceBinding { id: self.id.clone(), stable_id: self.stable_id.clone(), name: self.name.clone(), channel }
    }

    pub fn is_unassigned(&self) -> bool {
        self.id.is_empty() && self.stable_id.is_empty() && self.display_name.is_none()
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Visibility {
    pub disabled: bool,
    pub disconnected: bool,
    pub advanced: bool,
}

pub fn advanced_purpose(endpoint: &Endpoint) -> &'static str {
    match endpoint.form_factor {
        0 => "Network audio endpoint",
        2 if endpoint.direction == "capture" => "Line input for an external mixer or audio source",
        6 => "Telephone handset audio",
        7 => "Digital audio passthrough",
        8 => "S/PDIF digital audio",
        _ => "",
    }
}

/// Known routing cables stay out of Listening output so VAC's dedicated
/// direct-render path cannot be replaced with shared playback by mistake.
pub fn compatible(endpoint: &Endpoint, direction: &str) -> bool {
    endpoint.direction == direction && (direction == "capture" || !matches!(endpoint.transport.as_str(), "vbCable" | "vacLite"))
}

pub fn is_advanced(endpoint: &Endpoint) -> bool {
    endpoint.form_factor != 0 && !advanced_purpose(endpoint).is_empty()
}

pub fn build(inventory: &[Endpoint], direction: &str, binding: &DeviceBinding, visibility: Visibility) -> Vec<EndpointChoice> {
    let assigned = binding.assigned();
    let directional: Vec<&Endpoint> = inventory.iter().filter(|e| e.direction == direction).collect();
    // The engine's resolution (`vinemix_model::identity`), so the chooser
    // shows as current exactly the endpoint the engine opens. An unresolved
    // saved endpoint stays visible for recovery.
    let exact = if assigned { identity::exact(inventory, binding, direction) } else { None };
    let resolved = if assigned { identity::resolve(inventory, binding, direction).active() } else { None };
    let current = resolved.or(exact);
    // An inactive record that Windows identifies (by stable ID) as an earlier
    // endpoint ID of a device that is active now is that device, not another
    // one: it is not offered as a separate, permanently missing choice.
    let superseded: Vec<&Endpoint> = identity::superseded(inventory, direction).into_iter().map(|i| &inventory[i]).collect();
    let visible = |e: &Endpoint| {
        if !compatible(e, direction) || superseded.iter().any(|s| std::ptr::eq(*s, e)) {
            return false;
        }
        let available = e.state == "Active"
            || (e.state == "Disabled" && visibility.disabled)
            || (matches!(e.state.as_str(), "Missing" | "Unplugged") && visibility.disconnected);
        available && (visibility.advanced || !is_advanced(e))
    };
    let is_current = |e: &Endpoint| current.is_some_and(|c| std::ptr::eq(c, e));
    let mut endpoints: Vec<Endpoint> = directional.iter().copied().filter(|e| is_current(e) || visible(e)).cloned().collect();
    let mut current_index = directional.iter().copied().filter(|e| is_current(e) || visible(e)).position(is_current);
    // Stable ordering: active first, then culture-insensitive name, then ID.
    let mut order: Vec<usize> = (0..endpoints.len()).collect();
    order.sort_by(|&a, &b| {
        let (x, y) = (&endpoints[a], &endpoints[b]);
        (x.state != "Active")
            .cmp(&(y.state != "Active"))
            .then_with(|| text::compare_ignore_case(&x.name, &y.name))
            .then_with(|| x.id.cmp(&y.id))
    });
    current_index = current_index.and_then(|i| order.iter().position(|&o| o == i));
    endpoints = order.into_iter().map(|i| endpoints[i].clone()).collect();
    // The saved device is not available, but an active device has its name.
    // A name proves nothing (identical devices share names), so nothing is
    // switched automatically; the saved row says how to recover.
    let unresolved = assigned && resolved.is_none();
    let saved_name = current.map_or(binding.name.as_str(), |e| e.name.as_str()).to_owned();
    let same_name_available = unresolved
        && !saved_name.is_empty()
        && directional.iter().any(|e| e.state == "Active" && !current.is_some_and(|c| std::ptr::eq(c, *e)) && text::eq_ignore_case(&e.name, &saved_name));
    if assigned && current_index.is_none() {
        endpoints.push(Endpoint {
            id: binding.id.clone(),
            stable_id: binding.stable_id.clone(),
            name: if binding.name.is_empty() { "Saved device".into() } else { binding.name.clone() },
            direction: direction.into(),
            state: "Missing".into(),
            channels: u32::try_from(binding.channel + 1).unwrap_or(1).max(1),
            ..Default::default()
        });
        current_index = Some(endpoints.len() - 1);
    }
    let mut choices = vec![EndpointChoice::unassigned()];
    for (index, endpoint) in endpoints.iter().enumerate() {
        let mut display = endpoint.name.clone();
        let duplicates: Vec<usize> =
            endpoints.iter().enumerate().filter(|(_, other)| text::eq_ignore_case(&other.name, &endpoint.name)).map(|(i, _)| i).collect();
        if duplicates.len() > 1 {
            let hardware = if endpoint.interface_name.is_empty() { &endpoint.description } else { &endpoint.interface_name };
            if !hardware.is_empty() && !text::contains_ignore_case(&display, hardware) {
                display = format!("{display} · {hardware}");
            }
            let ordinal = duplicates.iter().position(|&i| i == index).unwrap_or(0) + 1;
            display = format!("{display} · Device {ordinal}");
        }
        let is_compatible = compatible(endpoint, direction);
        let state = if endpoint.state == "Active" { String::new() } else { format!(" · {}", endpoint.state) };
        let mut help = endpoint.name.clone();
        if !endpoint.interface_name.is_empty() {
            help += &format!("\n{}", endpoint.interface_name);
        }
        let purpose = advanced_purpose(endpoint);
        if !purpose.is_empty() {
            help += &format!("\n{purpose}");
        }
        help += if !is_compatible {
            "\nThis is a routing cable. Choose a listening device here; use Virtual output for VAC."
        } else if endpoint.state == "Disabled" {
            "\nDisabled in Windows. Enable it in Windows sound settings before use."
        } else if endpoint.state != "Active" && current_index == Some(index) && same_name_available {
            "\nSaved device unavailable. A device with the same name is listed, but Windows reports it as a different device. Select it to use it."
        } else if endpoint.state != "Active" {
            "\nUnavailable. Reconnect or enable the device in Windows; saved assignments are retained."
        } else {
            // An available device needs no explanation beyond its identity.
            ""
        };
        choices.push(EndpointChoice {
            id: endpoint.id.clone(),
            stable_id: endpoint.stable_id.clone(),
            name: endpoint.name.clone(),
            state: endpoint.state.clone(),
            channels: endpoint.channels,
            display_name: Some(format!("{display}{state}{}", if is_compatible { "" } else { " · incompatible" })),
            selectable: is_compatible && endpoint.state == "Active",
            current: current_index == Some(index),
            help,
        });
    }
    if !endpoints.iter().any(|e| compatible(e, direction) && e.state == "Active") {
        choices.push(EndpointChoice {
            display_name: Some(format!("No available {} · see Settings", if direction == "capture" { "inputs" } else { "outputs" })),
            selectable: false,
            help: "Connect an audio device, or open Settings → Audio device visibility to inspect hidden devices.".into(),
            ..Default::default()
        });
    }
    choices
}

pub fn selected(choices: &[EndpointChoice], binding: &DeviceBinding) -> Option<usize> {
    if !binding.assigned() {
        return Some(0);
    }
    if let Some(index) = choices.iter().position(|c| c.current) {
        return Some(index);
    }
    if !binding.id.is_empty() {
        if let Some(index) = choices.iter().position(|c| c.id == binding.id) {
            return Some(index);
        }
    }
    if !binding.stable_id.is_empty() {
        return choices.iter().position(|c| c.stable_id == binding.stable_id);
    }
    None
}

pub fn can_assign(choice: &EndpointChoice, inventory: &[Endpoint], direction: &str) -> bool {
    choice.selectable
        && ((choice.id.is_empty() && choice.stable_id.is_empty())
            || inventory.iter().any(|e| e.id == choice.id && e.state == "Active" && compatible(e, direction)))
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum NoticeKind {
    None,
    Muted,
    Setup,
    Pending,
    Problem,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Availability {
    pub kind: NoticeKind,
    pub label: &'static str,
    pub detail: &'static str,
}

const AVAILABLE: Availability = Availability { kind: NoticeKind::None, label: "", detail: "" };

const fn notice(kind: NoticeKind, label: &'static str, detail: &'static str) -> Availability {
    Availability { kind, label, detail }
}

use NoticeKind::{Muted, Pending, Problem, Setup};

pub fn input(
    node: &GraphNode,
    status: Option<&NodeStatus>,
    endpoints: &[Endpoint],
    applications: &[ApplicationInfo],
    engine_available: bool,
) -> Availability {
    if !matches!(node.kind.as_str(), "microphone" | "virtualInput" | "appInput") {
        return AVAILABLE;
    }
    if !engine_available {
        return notice(Problem, "ENGINE OFFLINE", "Waiting for the audio engine");
    }
    let app = node.kind == "appInput";
    let assigned = if app { !node.app.executable_path.is_empty() } else { node.device.assigned() };
    if !assigned {
        if app {
            return notice(Setup, "CHOOSE AN APP", "Select an application below");
        }
        if node.kind == "virtualInput" {
            return unassigned_virtual(endpoints, "virtualInput");
        }
        return notice(Setup, "CHOOSE MICROPHONE", "Select a Windows or streamed microphone");
    }
    let path = status.map(|s| &s.path);
    let state = path.map_or("", |p| p.state.as_str());
    if state == "Running" {
        return if node.muted { notice(Muted, "MUTED", "Unmute this input to pass audio") } else { AVAILABLE };
    }
    match state {
        "AccessDenied" => notice(Problem, "ACCESS BLOCKED", "Check Windows sound and privacy settings"),
        "Busy" => notice(Problem, "INPUT BUSY", "Close other capture apps, then retry"),
        "ServiceUnavailable" => notice(Problem, "AUDIO SERVICE OFFLINE", "Check the Windows Audio service"),
        "Unsupported" => {
            notice(Problem, if app { "APP CAPTURE UNAVAILABLE" } else { "INPUT UNAVAILABLE" }, "Choose another capture method")
        }
        "Error" => {
            if app && reasons::for_path(path) == "access_denied" {
                notice(Problem, "ACCESS BLOCKED", "Check app access, then retry")
            } else if app && reasons::for_path(path) == "audio_service_unavailable" {
                notice(Problem, "AUDIO SERVICE OFFLINE", "Check the Windows Audio service")
            } else {
                notice(Problem, if app { "APP CAPTURE ERROR" } else { "INPUT ERROR" }, "Check this input; retry assigned devices")
            }
        }
        "Recovering" => notice(Pending, "RECONNECTING", "Waiting for this input to reconnect"),
        "Resolving" | "Opening" | "Priming" | "Unassigned" => notice(Pending, "CONNECTING", "Waiting for the input to connect"),
        "Waiting" => {
            if app {
                waiting_for_app(node, path, applications)
            } else {
                waiting_for_device(node, path, endpoints, "capture")
            }
        }
        _ => notice(Pending, "CHECKING INPUT", "Waiting for input status"),
    }
}

pub fn output(node: &GraphNode, status: Option<&NodeStatus>, endpoints: &[Endpoint], engine_available: bool) -> Availability {
    if !matches!(node.kind.as_str(), "output" | "virtualOutput") {
        return AVAILABLE;
    }
    if !engine_available {
        return notice(Problem, "ENGINE OFFLINE", "Waiting for the audio engine");
    }
    if !node.device.assigned() {
        return if node.kind == "virtualOutput" {
            unassigned_virtual(endpoints, "virtualOutput")
        } else {
            notice(Setup, "CHOOSE OUTPUT DEVICE", "Connect an output, then select it")
        };
    }
    let path = status.map(|s| &s.path);
    match path.map_or("", |p| p.state.as_str()) {
        "Running" => {
            if node.muted {
                notice(Muted, "MUTED", "Unmute this output to pass audio")
            } else {
                AVAILABLE
            }
        }
        "AccessDenied" => notice(Problem, "ACCESS BLOCKED", "Check Windows sound device settings"),
        "Busy" => notice(Problem, "OUTPUT BUSY", "Close other playback apps, then retry"),
        "ServiceUnavailable" => notice(Problem, "AUDIO SERVICE OFFLINE", "Check the Windows Audio service"),
        "Unsupported" => notice(Problem, "OUTPUT UNAVAILABLE", "Check the output format and settings"),
        "Error" => notice(Problem, "OUTPUT ERROR", "Check output; retry assigned devices"),
        "Recovering" => notice(Pending, "RECONNECTING", "Waiting for this output to reconnect"),
        "Resolving" | "Opening" | "Priming" | "Unassigned" => notice(Pending, "CONNECTING", "Waiting for the output to connect"),
        "Waiting" => waiting_for_device(node, path, endpoints, "render"),
        _ => notice(Pending, "CHECKING OUTPUT", "Waiting for output status"),
    }
}

fn waiting_for_device(node: &GraphNode, path: Option<&PathState>, endpoints: &[Endpoint], direction: &str) -> Availability {
    let capture = direction == "capture";
    if reasons::for_path(path) == "device_disabled" {
        return notice(Problem, if capture { "INPUT DISABLED" } else { "OUTPUT DISABLED" }, "Enable in Windows sound settings");
    }
    let waiting = notice(
        Pending,
        "RECONNECTING",
        if capture { "Device detected; waiting for capture" } else { "Device detected; waiting for playback" },
    );
    let exact = endpoints.iter().find(|e| e.direction == direction && !node.device.id.is_empty() && e.id == node.device.id);
    if exact.is_some_and(|e| e.state == "Active") {
        return waiting;
    }
    if !node.device.stable_id.is_empty() {
        let active = endpoints
            .iter()
            .filter(|e| e.direction == direction && e.state == "Active" && e.stable_id == node.device.stable_id)
            .take(2)
            .count();
        if active > 1 {
            return if capture {
                notice(Setup, "CHOOSE INPUT DEVICE", "Select the intended input again")
            } else {
                notice(Setup, "CHOOSE OUTPUT DEVICE", "Select the intended output again")
            };
        }
        if active == 1 {
            return waiting;
        }
    }
    match (exact.map(|e| e.state.as_str()), capture) {
        (Some("Disabled"), true) => notice(Problem, "INPUT DISABLED", "Enable in Windows sound settings"),
        (Some("Disabled"), false) => notice(Problem, "OUTPUT DISABLED", "Enable in Windows sound settings"),
        (Some("Unplugged"), true) => notice(Problem, "INPUT UNPLUGGED", "Reconnect or choose another input"),
        (Some("Unplugged"), false) => notice(Problem, "OUTPUT UNPLUGGED", "Reconnect or choose another output"),
        (None | Some("Missing"), true) => notice(Problem, "INPUT MISSING", "Reconnect or choose another input"),
        (None | Some("Missing"), false) => notice(Problem, "OUTPUT MISSING", "Reconnect or choose another output"),
        (_, true) => notice(Problem, "INPUT UNAVAILABLE", "Check Windows sound settings"),
        (_, false) => notice(Problem, "OUTPUT UNAVAILABLE", "Check Windows sound settings"),
    }
}

fn waiting_for_app(node: &GraphNode, path: Option<&PathState>, applications: &[ApplicationInfo]) -> Availability {
    let mut ambiguous = reasons::for_path(path) == "app_instance_ambiguous";
    if path.is_none_or(|p| p.reason.is_empty() && p.message.trim().is_empty()) {
        let same_path = |a: &ApplicationInfo| text::eq_ignore_case(&a.executable_path, &node.app.executable_path);
        let exact = node.app.process_id != 0 && applications.iter().any(|a| a.process_id == node.app.process_id && same_path(a));
        let mut distinct: Vec<u32> = applications.iter().filter(|a| same_path(a)).map(|a| a.process_id).collect();
        distinct.dedup();
        distinct.sort_unstable();
        distinct.dedup();
        ambiguous = !exact && distinct.len() > 1;
    }
    if ambiguous {
        notice(Setup, "CHOOSE APP INSTANCE", "Select the intended application below")
    } else {
        notice(Problem, "APP NOT DETECTED", "Open the app or select another below")
    }
}

fn unassigned_virtual(endpoints: &[Endpoint], kind: &str) -> Availability {
    let input = kind == "virtualInput";
    let matches: Vec<&Endpoint> = endpoints.iter().filter(|e| bindings::matches(kind, e)).collect();
    let (not_detected, choose, disabled, unplugged, unavailable) = if input {
        (
            notice(Problem, "VB-CABLE NOT DETECTED", "Check VB-CABLE in Windows sound settings"),
            notice(Setup, "CHOOSE VB-CABLE", "Virtual input needs assignment"),
            notice(Problem, "VB-CABLE DISABLED", "Enable VB-CABLE in sound settings"),
            notice(Problem, "VB-CABLE UNPLUGGED", "Check VB-CABLE in Windows sound settings"),
            notice(Problem, "VB-CABLE UNAVAILABLE", "Check VB-CABLE in Windows sound settings"),
        )
    } else {
        (
            notice(Problem, "VAC LITE NOT DETECTED", "Check VAC Lite in Windows sound settings"),
            notice(Setup, "CHOOSE VAC LITE", "Virtual output needs assignment"),
            notice(Problem, "VAC LITE DISABLED", "Enable VAC Lite in sound settings"),
            notice(Problem, "VAC LITE UNPLUGGED", "Check VAC Lite in Windows sound settings"),
            notice(Problem, "VAC LITE UNAVAILABLE", "Check VAC Lite in Windows sound settings"),
        )
    };
    if matches.is_empty() {
        return not_detected;
    }
    if matches.iter().any(|e| e.state == "Active" && (!e.id.is_empty() || !e.stable_id.is_empty())) {
        return choose;
    }
    if let [only] = matches.as_slice() {
        match only.state.as_str() {
            "Disabled" => return disabled,
            "Unplugged" => return unplugged,
            "Missing" => return not_detected,
            _ => {}
        }
    }
    unavailable
}

#[cfg(test)]
mod tests {
    use super::*;

    fn endpoint(id: &str, name: &str, direction: &str, state: &str) -> Endpoint {
        Endpoint {
            id: id.into(),
            stable_id: format!("s-{id}"),
            name: name.into(),
            direction: direction.into(),
            state: state.into(),
            transport: "physical".into(),
            channels: 2,
            ..Default::default()
        }
    }

    #[test]
    fn duplicates_and_missing_saved_device() {
        let inventory = vec![
            endpoint("b", "Mic", "capture", "Active"),
            endpoint("a", "Mic", "capture", "Active"),
            endpoint("c", "Line", "capture", "Disabled"),
        ];
        let saved = DeviceBinding { id: "z".into(), stable_id: "s-z".into(), name: "Old".into(), channel: -1 };
        let choices = build(&inventory, "capture", &saved, Visibility::default());
        let labels: Vec<String> = choices.iter().map(EndpointChoice::label).collect();
        assert_eq!(labels, ["Not assigned", "Mic · Device 1", "Mic · Device 2", "Old · Missing"]);
        assert_eq!(selected(&choices, &saved), Some(3));
        assert!(!choices[3].selectable);
    }

    // ----- RUST-REV-041: reconnected devices in the chooser -----------------

    fn device(id: &str, stable: &str, name: &str, direction: &str, state: &str) -> Endpoint {
        Endpoint { stable_id: stable.into(), ..endpoint(id, name, direction, state) }
    }

    fn saved(id: &str, stable: &str) -> DeviceBinding {
        DeviceBinding { id: id.into(), stable_id: stable.into(), name: "Example headset".into(), channel: -1 }
    }

    const ALL: Visibility = Visibility { disabled: true, disconnected: true, advanced: true };

    /// `(labels, label of the row shown as current)` — checked forwards and
    /// with the inventory reversed, which must not change anything.
    fn chooser(inventory: &[Endpoint], direction: &str, binding: &DeviceBinding, visibility: Visibility) -> (Vec<String>, String) {
        let view = |inventory: &[Endpoint]| {
            let choices = build(inventory, direction, binding, visibility);
            let labels: Vec<String> = choices.iter().map(EndpointChoice::label).collect();
            let current = selected(&choices, binding).map(|i| labels[i].clone()).unwrap_or_default();
            (labels, current)
        };
        let forwards = view(inventory);
        let mut reversed = inventory.to_vec();
        reversed.reverse();
        assert_eq!(forwards, view(&reversed), "inventory order changed the chooser");
        forwards
    }

    #[test]
    fn reconnected_device_is_one_current_row_when_identity_proves_it() {
        for direction in ["capture", "render"] {
            // Same endpoint ID returns: the original choice, active.
            let inventory = [device("a", "", "Example headset", direction, "Active")];
            assert_eq!(chooser(&inventory, direction, &saved("a", ""), ALL), (vec!["Not assigned".into(), "Example headset".into()], "Example headset".into()));

            // New endpoint ID with the saved stable ID; Windows still lists the
            // old ID as missing. One row, current and active — in both
            // visibility settings.
            let inventory = [device("a", "s", "Example headset", direction, "Missing"), device("b", "s", "Example headset", direction, "Active")];
            for visibility in [Visibility::default(), ALL] {
                let (labels, current) = chooser(&inventory, direction, &saved("a", "s"), visibility);
                assert_eq!(labels, ["Not assigned", "Example headset"], "{direction} {visibility:?}");
                assert_eq!(current, "Example headset");
            }
            let choices = build(&inventory, direction, &saved("a", "s"), ALL);
            let current = selected(&choices, &saved("a", "s")).unwrap();
            assert_eq!(choices[current].id, "b", "the row shown as current is the endpoint the engine opens");
            assert!(choices[current].selectable);

            // The binding was saved without a stable ID, but the old record
            // carries it: still one current row.
            let (labels, current) = chooser(&inventory, direction, &saved("a", ""), ALL);
            assert_eq!((labels.len(), current.as_str()), (2, "Example headset"));

            // Other nodes' choosers do not show the superseded record either.
            let (labels, _) = chooser(&inventory, direction, &DeviceBinding::default(), ALL);
            assert_eq!(labels, ["Not assigned", "Example headset"]);
        }
    }

    #[test]
    fn unproven_same_name_device_stays_separate_and_the_saved_row_explains() {
        for direction in ["capture", "render"] {
            // New endpoint ID, no matching stable ID anywhere: the active
            // device is offered, the saved one stays current and missing.
            for (inventory, binding) in [
                (vec![device("b", "t", "Example headset", direction, "Active")], saved("a", "s")),
                (vec![device("b", "t", "Example headset", direction, "Active")], saved("a", "")),
                (vec![device("a", "", "Example headset", direction, "Missing"), device("b", "t", "Example headset", direction, "Active")], saved("a", "")),
            ] {
                let (labels, current) = chooser(&inventory, direction, &binding, ALL);
                assert_eq!(labels.len(), 3, "{labels:?}");
                assert!(current.ends_with("· Missing"), "the saved assignment is kept, not switched by name: {current}");
                assert!(labels.iter().any(|l| !l.contains("Missing") && l.starts_with("Example headset")), "{labels:?}");
                let choices = build(&inventory, direction, &binding, ALL);
                let row = &choices[selected(&choices, &binding).unwrap()];
                assert!(!row.selectable);
                assert!(row.help.contains("Windows reports it as a different device") && row.help.contains("Select it"), "{}", row.help);
                let active = choices.iter().find(|c| c.id == "b").unwrap();
                assert!(active.selectable && !active.current);
            }

            // Two active endpoints share the saved stable ID: no guess.
            let inventory = [
                device("b", "s", "Example headset", direction, "Active"),
                device("c", "s", "Example headset", direction, "Active"),
            ];
            let (labels, current) = chooser(&inventory, direction, &saved("a", "s"), ALL);
            assert_eq!(labels.len(), 4, "both candidates and the saved row: {labels:?}");
            assert!(current.ends_with("· Missing"), "{current}");

            // Distinct devices with one name are never merged.
            let inventory = [
                device("m", "p", "Example headset", direction, "Active"),
                device("n", "q", "Example headset", direction, "Unplugged"),
            ];
            let (labels, current) = chooser(&inventory, direction, &saved("m", "p"), ALL);
            assert_eq!(labels.len(), 3, "{labels:?}");
            assert!(!current.contains("Unplugged"));
            // With disconnected devices hidden only the unrelated one goes.
            let (labels, _) = chooser(&inventory, direction, &saved("m", "p"), Visibility::default());
            assert_eq!(labels.len(), 2, "{labels:?}");

            // A missing saved device with no same-name candidate keeps the
            // ordinary unavailable wording.
            let inventory = [device("x", "y", "Other example", direction, "Active")];
            let choices = build(&inventory, direction, &saved("a", "s"), ALL);
            let row = &choices[selected(&choices, &saved("a", "s")).unwrap()];
            assert!(row.help.contains("saved assignments are retained"), "{}", row.help);
        }
    }

    #[test]
    fn notices() {
        let mut node = crate::graph::new_node("virtualOutput", 0.0, 0.0);
        assert_eq!(output(&node, None, &[], true).label, "VAC LITE NOT DETECTED");
        node.device.id = "x".into();
        assert_eq!(output(&node, None, &[], true).label, "CHECKING OUTPUT");
        assert_eq!(output(&node, None, &[], false).label, "ENGINE OFFLINE");
    }
}
