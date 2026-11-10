//! Which Windows endpoint a saved device binding means right now.
//!
//! One rule for every consumer — device workers, the output resolver,
//! configuration validation and the desktop's device chooser — so they can
//! never disagree about whether a returning device is the saved one:
//!
//! 1. the endpoint with the saved endpoint ID, if it is active;
//! 2. otherwise the single active endpoint whose stable ID equals the saved
//!    stable ID;
//! 3. otherwise, when the binding was saved without a stable ID, the single
//!    active endpoint whose stable ID equals the stable ID that Windows
//!    reports for the saved endpoint ID's own (inactive) record.
//!
//! Step 3 covers a device that came back under a new endpoint ID after its
//! binding was saved without a stable ID (the property was unavailable at
//! the time). It follows the saved ID's own record, so identity is still
//! proven by Windows, not inferred.
//!
//! IDs are opaque, case-sensitive strings and are compared exactly. Names,
//! enumeration order and Windows default-device roles never select a device,
//! and an ambiguous stable ID resolves to nothing.

use crate::config::DeviceBinding;
use crate::status::Endpoint;

/// How a binding matched its endpoint.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Match {
    /// The saved endpoint ID.
    EndpointId,
    /// The saved stable ID.
    StableId,
    /// The stable ID of the saved endpoint ID's inventory record.
    RecordStableId,
}

impl Match {
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::EndpointId => "endpoint ID",
            Self::StableId => "stable ID",
            Self::RecordStableId => "stable ID of the saved endpoint record",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Resolution<'a> {
    /// The active endpoint the binding means.
    Active(&'a Endpoint, Match),
    /// Several active endpoints share the identifying stable ID.
    Ambiguous,
    /// No active endpoint is provably the saved device.
    Unresolved,
}

impl<'a> Resolution<'a> {
    pub fn active(self) -> Option<&'a Endpoint> {
        match self {
            Self::Active(endpoint, _) => Some(endpoint),
            _ => None,
        }
    }
}

/// The inventory record with the saved endpoint ID, in any state.
pub fn exact<'a>(devices: &'a [Endpoint], binding: &DeviceBinding, direction: &str) -> Option<&'a Endpoint> {
    if binding.id.is_empty() {
        return None;
    }
    devices.iter().find(|endpoint| endpoint.id == binding.id && endpoint.direction == direction)
}

/// Resolves `binding` among `devices` for `direction` (`"capture"` or
/// `"render"`). The result does not depend on the order of `devices`.
pub fn resolve<'a>(devices: &'a [Endpoint], binding: &DeviceBinding, direction: &str) -> Resolution<'a> {
    let exact = exact(devices, binding, direction);
    if let Some(endpoint) = exact.filter(|endpoint| endpoint.state == "Active") {
        return Resolution::Active(endpoint, Match::EndpointId);
    }
    let (stable, matched) = if !binding.stable_id.is_empty() {
        (binding.stable_id.as_str(), Match::StableId)
    } else {
        match exact.map(|endpoint| endpoint.stable_id.as_str()).filter(|stable| !stable.is_empty()) {
            Some(stable) => (stable, Match::RecordStableId),
            None => return Resolution::Unresolved,
        }
    };
    let mut found: Option<&Endpoint> = None;
    for endpoint in devices {
        if endpoint.direction != direction || endpoint.state != "Active" || endpoint.stable_id != stable {
            continue;
        }
        if found.is_some() {
            return Resolution::Ambiguous;
        }
        found = Some(endpoint);
    }
    match found {
        Some(endpoint) => Resolution::Active(endpoint, matched),
        None => Resolution::Unresolved,
    }
}

/// Inactive records that Windows itself identifies as an earlier endpoint ID
/// of a device that is active now: same direction, same non-empty stable ID
/// as exactly one active endpoint. They are not separate devices and are not
/// offered as separate choices. Returns indices into `devices`.
pub fn superseded(devices: &[Endpoint], direction: &str) -> Vec<usize> {
    let mut result = Vec::new();
    for (index, endpoint) in devices.iter().enumerate() {
        if endpoint.direction != direction || endpoint.state == "Active" || endpoint.stable_id.is_empty() {
            continue;
        }
        let active = devices
            .iter()
            .filter(|other| other.direction == direction && other.state == "Active" && other.stable_id == endpoint.stable_id)
            .count();
        if active == 1 {
            result.push(index);
        }
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;

    fn endpoint(id: &str, stable: &str, name: &str, direction: &str, state: &str) -> Endpoint {
        Endpoint { id: id.into(), stable_id: stable.into(), name: name.into(), direction: direction.into(), state: state.into(), ..Endpoint::default() }
    }

    fn binding(id: &str, stable: &str) -> DeviceBinding {
        DeviceBinding { id: id.into(), stable_id: stable.into(), name: "Example device".into(), channel: -1 }
    }

    /// Enumeration order must not change the outcome.
    fn assert_order_independent(devices: &[Endpoint], binding: &DeviceBinding, direction: &str) {
        let forwards = resolve(devices, binding, direction);
        let mut reversed_devices = devices.to_vec();
        reversed_devices.reverse();
        let reversed = resolve(&reversed_devices, binding, direction);
        let id = |r: Resolution<'_>| r.active().map(|e| e.id.clone());
        assert_eq!(id(forwards), id(reversed), "order changed the resolved endpoint");
        assert_eq!(std::mem::discriminant(&forwards), std::mem::discriminant(&reversed), "order changed the outcome");
    }

    /// The RUST-REV-041 scenarios, for both directions.
    #[test]
    fn reconnection_matrix() {
        for direction in ["capture", "render"] {
            let other = if direction == "capture" { "render" } else { "capture" };
            // Same endpoint ID returns active, with or without a stable ID.
            let devices = [endpoint("a", "", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("a", ""), direction), Resolution::Active(&devices[0], Match::EndpointId));
            assert_eq!(resolve(&devices, &binding("a", "s"), direction), Resolution::Active(&devices[0], Match::EndpointId));

            // Endpoint ID changed; one active endpoint has the saved stable ID.
            let devices = [endpoint("b", "s", "Example", direction, "Active"), endpoint("x", "t", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("a", "s"), direction), Resolution::Active(&devices[0], Match::StableId));
            assert_order_independent(&devices, &binding("a", "s"), direction);

            // Endpoint ID changed; saved stable ID empty; the old record is
            // still listed by Windows and carries the stable ID.
            let devices = [endpoint("a", "s", "Example", direction, "Unplugged"), endpoint("b", "s", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("a", ""), direction), Resolution::Active(&devices[1], Match::RecordStableId));
            assert_order_independent(&devices, &binding("a", ""), direction);

            // Endpoint ID changed; saved stable ID empty; no record of the old
            // ID (or it has no stable ID): a same-name device is NOT adopted.
            let devices = [endpoint("b", "s", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("a", ""), direction), Resolution::Unresolved);
            let devices = [endpoint("a", "", "Example", direction, "Missing"), endpoint("b", "s", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("a", ""), direction), Resolution::Unresolved);

            // Endpoint ID and stable ID both differ: unresolved, by any name.
            let devices = [endpoint("b", "t", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("a", "s"), direction), Resolution::Unresolved);

            // Old missing record and new active endpoint share the stable ID.
            let devices = [endpoint("a", "s", "Example", direction, "Missing"), endpoint("b", "s", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("a", "s"), direction), Resolution::Active(&devices[1], Match::StableId));
            assert_eq!(superseded(&devices, direction), [0]);

            // Two active endpoints share the fallback stable ID: never guess.
            let devices = [
                endpoint("b", "s", "Example", direction, "Active"),
                endpoint("c", "s", "Example", direction, "Active"),
                endpoint("a", "s", "Example", direction, "Unplugged"),
            ];
            assert_eq!(resolve(&devices, &binding("a", "s"), direction), Resolution::Ambiguous);
            assert_eq!(resolve(&devices, &binding("a", ""), direction), Resolution::Ambiguous);
            assert_order_independent(&devices, &binding("a", "s"), direction);
            assert!(superseded(&devices, direction).is_empty(), "an ambiguous identity supersedes nothing");

            // An exact active match wins even when the stable ID is ambiguous.
            assert_eq!(resolve(&devices, &binding("c", "s"), direction), Resolution::Active(&devices[1], Match::EndpointId));

            // The other direction never matches, by ID or stable ID.
            let devices = [endpoint("a", "s", "Example", other, "Active")];
            assert_eq!(resolve(&devices, &binding("a", "s"), direction), Resolution::Unresolved);

            // IDs are opaque and case-sensitive.
            let devices = [endpoint("A", "S", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("a", "s"), direction), Resolution::Unresolved);

            // Identical names on distinct devices stay distinct.
            let devices = [endpoint("m", "p", "Example", direction, "Active"), endpoint("n", "q", "Example", direction, "Active")];
            assert_eq!(resolve(&devices, &binding("m", "p"), direction), Resolution::Active(&devices[0], Match::EndpointId));
            assert_eq!(resolve(&devices, &binding("gone", "gone"), direction), Resolution::Unresolved);
            assert!(superseded(&devices, direction).is_empty());
        }
    }

    #[test]
    fn unassigned_bindings_resolve_to_nothing() {
        let devices = [endpoint("", "", "Nameless", "capture", "Active"), endpoint("a", "", "Example", "capture", "Active")];
        assert_eq!(resolve(&devices, &DeviceBinding::default(), "capture"), Resolution::Unresolved);
        assert!(exact(&devices, &DeviceBinding::default(), "capture").is_none(), "an empty ID never matches an empty ID");
    }
}
