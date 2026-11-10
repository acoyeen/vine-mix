//! Control protocol v1 records as the desktop reads them (`Models.cs`).
//!
//! The configuration uses the engine's own strict reader, so the desktop and
//! the engine can never disagree about a field's meaning. Presentation and
//! telemetry records are lenient: a newer engine may add fields.

use serde_json::{json, Value};
use vinemix_model::config::Configuration;
use vinemix_model::status::{ApplicationInfo, EngineStatus, Endpoint};

#[derive(Clone, Debug, Default)]
pub struct Snapshot {
    pub settings_state: String,
    pub backup_path: String,
    pub physical_settings_path: String,
    pub executable_path: String,
    pub engine_version: String,
    pub engine_protocol_version: i64,
    pub engine_pid: u32,
    pub applications: Vec<ApplicationInfo>,
    pub configuration: Configuration,
    pub endpoints: Vec<Endpoint>,
    pub status: EngineStatus,
    pub settings_path: String,
    pub log_path: String,
    pub warning: String,
}

/// The configuration and device lists of the last snapshot on a connection,
/// with the engine's tokens for them. The next request names those tokens;
/// the engine then leaves out whatever is unchanged and the cached copy is
/// used instead. An engine that sends no tokens always sends everything.
#[derive(Clone, Debug, Default)]
pub struct SnapshotCache {
    configuration: Option<(String, Configuration)>,
    inventory: Option<(String, Vec<Endpoint>, Vec<ApplicationInfo>)>,
}

impl SnapshotCache {
    /// The `known` object for the next request, if anything is cached.
    pub fn known(&self) -> Option<Value> {
        let mut known = serde_json::Map::new();
        if let Some((token, _)) = &self.configuration {
            known.insert("configuration".into(), json!(token));
        }
        if let Some((token, _, _)) = &self.inventory {
            known.insert("inventory".into(), json!(token));
        }
        (!known.is_empty()).then_some(Value::Object(known))
    }

    pub fn clear(&mut self) {
        *self = Self::default();
    }
}

impl Snapshot {
    pub fn is_settings_usable(&self) -> bool {
        matches!(self.settings_state.as_str(), "loaded" | "new")
    }
}

#[derive(Clone, Debug, Default)]
pub struct Response {
    pub protocol_version: i64,
    pub request_id: String,
    pub ok: bool,
    pub error: String,
    pub error_code: String,
    pub snapshot: Option<Snapshot>,
}

fn text(value: &Value, key: &str) -> String {
    value.get(key).and_then(Value::as_str).unwrap_or_default().to_owned()
}

fn list<T: serde::de::DeserializeOwned>(value: &Value, key: &str) -> Result<Vec<T>, String> {
    match value.get(key) {
        None | Some(Value::Null) => Ok(Vec::new()),
        Some(items) => serde_json::from_value(items.clone()).map_err(|e| format!("The engine sent an unreadable {key} list: {e}")),
    }
}

pub fn parse_snapshot(value: &Value) -> Result<Snapshot, String> {
    parse_snapshot_cached(value, &mut SnapshotCache::default())
}

/// Reads a snapshot, taking any part the engine left out from `cache` (only
/// when the engine's token for that part is the cached one) and remembering
/// the parts it did send.
pub fn parse_snapshot_cached(value: &Value, cache: &mut SnapshotCache) -> Result<Snapshot, String> {
    if !value.is_object() {
        return Err("The engine snapshot has an unsupported shape.".into());
    }
    let token = |part: &str| value.get("tokens").and_then(|t| t.get(part)).and_then(Value::as_str).map(str::to_owned);
    let configuration = match value.get("configuration") {
        Some(configuration) => {
            let configuration = Configuration::from_json(configuration)
                .map_err(|e| format!("The engine configuration has an unsupported shape. {}", e.message))?;
            cache.configuration = token("configuration").map(|token| (token, configuration.clone()));
            configuration
        }
        None => match (&cache.configuration, token("configuration")) {
            (Some((cached, configuration)), Some(current)) if *cached == current => configuration.clone(),
            _ => return Err("The engine configuration has an unsupported shape.".into()),
        },
    };
    let (endpoints, applications) = if value.get("endpoints").is_some() || value.get("applications").is_some() || token("inventory").is_none() {
        let lists: (Vec<Endpoint>, Vec<ApplicationInfo>) = (list(value, "endpoints")?, list(value, "applications")?);
        cache.inventory = token("inventory").map(|token| (token, lists.0.clone(), lists.1.clone()));
        lists
    } else {
        match (&cache.inventory, token("inventory")) {
            (Some((cached, endpoints, applications)), Some(current)) if *cached == current => (endpoints.clone(), applications.clone()),
            _ => return Err("The engine left out a device list this application does not have.".into()),
        }
    };
    let status = match value.get("status") {
        None | Some(Value::Null) => EngineStatus::default(),
        Some(status) => serde_json::from_value(status.clone()).map_err(|e| format!("The engine status is unreadable: {e}"))?,
    };
    Ok(Snapshot {
        settings_state: value.get("settingsState").and_then(Value::as_str).unwrap_or("loaded").to_owned(),
        backup_path: text(value, "backupPath"),
        physical_settings_path: text(value, "physicalSettingsPath"),
        executable_path: text(value, "executablePath"),
        engine_version: text(value, "engineVersion"),
        engine_protocol_version: value.get("engineProtocolVersion").and_then(Value::as_i64).unwrap_or(0),
        engine_pid: value.get("enginePid").and_then(Value::as_u64).and_then(|v| u32::try_from(v).ok()).unwrap_or(0),
        applications,
        configuration,
        endpoints,
        status,
        settings_path: text(value, "settingsPath"),
        log_path: text(value, "logPath"),
        warning: text(value, "warning"),
    })
}

pub fn parse_response(line: &[u8]) -> Result<Response, String> {
    parse_response_cached(line, &mut SnapshotCache::default())
}

pub fn parse_response_cached(line: &[u8], cache: &mut SnapshotCache) -> Result<Response, String> {
    let value = vinemix_model::json::parse(line).map_err(|e| format!("The engine returned an unreadable response: {}", e.message))?;
    if !value.is_object() {
        return Err("The engine returned an empty response.".into());
    }
    let snapshot = match value.get("snapshot") {
        None | Some(Value::Null) => None,
        Some(snapshot) => Some(parse_snapshot_cached(snapshot, cache)?),
    };
    Ok(Response {
        protocol_version: value.get("protocolVersion").and_then(Value::as_i64).unwrap_or(0),
        request_id: match value.get("requestId") {
            Some(Value::String(id)) => id.clone(),
            Some(other) => other.to_string(),
            None => String::new(),
        },
        ok: value.get("ok").and_then(Value::as_bool).unwrap_or(false),
        error: text(&value, "error"),
        error_code: text(&value, "errorCode"),
        snapshot,
    })
}

/// One request line, without its newline.
pub fn request(request_id: &str, command: &str, configuration: Option<&Configuration>, expected_revision: Option<u64>) -> String {
    request_knowing(request_id, command, configuration, expected_revision, None)
}

/// A request that also names the snapshot parts already held (`known`).
pub fn request_knowing(
    request_id: &str,
    command: &str,
    configuration: Option<&Configuration>,
    expected_revision: Option<u64>,
    known: Option<Value>,
) -> String {
    request_with(request_id, command, configuration, expected_revision, known, None)
}

/// A request that names held snapshot parts (`known`) and what the window is
/// showing (`interest`), so the engine skips work nobody will see.
pub fn request_with(
    request_id: &str,
    command: &str,
    configuration: Option<&Configuration>,
    expected_revision: Option<u64>,
    known: Option<Value>,
    interest: Option<&Value>,
) -> String {
    let mut message = json!({"protocolVersion": 1, "requestId": request_id, "command": command});
    if let Some(known) = known {
        message["known"] = known;
    }
    if let Some(interest) = interest {
        message["interest"] = interest.clone();
    }
    if let Some(configuration) = configuration {
        message["configuration"] = configuration.to_json();
    }
    if let Some(revision) = expected_revision {
        message["expectedRevision"] = json!(revision);
    }
    vinemix_model::json::dump(&message)
}

/// The saved configuration without its revision, for confirming that an
/// uncertain configure committed exactly what was sent.
pub fn same_saved_configuration(expected: &Configuration, actual: &Configuration) -> bool {
    let mut left = expected.clone();
    let mut right = actual.clone();
    left.revision = 0;
    right.revision = 0;
    left == right
}

#[cfg(test)]
mod cache_tests {
    use super::*;

    fn full(configuration_token: &str, inventory_token: &str, revision: u64) -> Value {
        let mut configuration = vinemix_model::default_configuration();
        configuration.revision = revision;
        json!({
            "configuration": configuration.to_json(),
            "endpoints": [{"id": "example-endpoint", "name": "Example speakers", "direction": "render", "state": "Active"}],
            "applications": [{"processId": 42, "executablePath": "C:\\Example\\app.exe", "name": "Example app"}],
            "tokens": {"configuration": configuration_token, "inventory": inventory_token},
            "status": {"audioFrames": 240},
            "settingsState": "loaded",
        })
    }

    fn reduced(configuration_token: &str, inventory_token: &str) -> Value {
        json!({"tokens": {"configuration": configuration_token, "inventory": inventory_token}, "status": {"audioFrames": 480}, "settingsState": "loaded"})
    }

    /// RUST-REV-009: parts the engine leaves out come from the cache, and
    /// only when the engine's token is the cached one.
    #[test]
    fn omitted_parts_are_restored_from_the_cache_by_token() {
        let mut cache = SnapshotCache::default();
        assert!(cache.known().is_none(), "nothing is claimed before the first snapshot");
        let first = parse_snapshot_cached(&full("c1", "i1", 5), &mut cache).unwrap();
        assert_eq!(cache.known().unwrap(), json!({"configuration": "c1", "inventory": "i1"}));
        let second = parse_snapshot_cached(&reduced("c1", "i1"), &mut cache).unwrap();
        assert_eq!(second.configuration, first.configuration);
        assert_eq!(second.endpoints, first.endpoints);
        assert_eq!(second.applications, first.applications);
        assert_eq!(second.status.audio_frames, 480, "telemetry is always the new reply's");
        // The engine moved on and (wrongly) omitted a part: never guess.
        assert!(parse_snapshot_cached(&reduced("c2", "i1"), &mut cache).is_err());
        assert!(parse_snapshot_cached(&reduced("c1", "i2"), &mut cache).is_err());
        // A changed part replaces the cached one.
        let mut changed = full("c2", "i1", 6);
        changed.as_object_mut().unwrap().remove("endpoints");
        changed.as_object_mut().unwrap().remove("applications");
        let third = parse_snapshot_cached(&changed, &mut cache).unwrap();
        assert_eq!(third.configuration.revision, 6);
        assert_eq!(third.endpoints, first.endpoints, "the unchanged device list is still the cached one");
        assert_eq!(cache.known().unwrap(), json!({"configuration": "c2", "inventory": "i1"}));
        cache.clear();
        assert!(cache.known().is_none());
        assert!(parse_snapshot_cached(&reduced("c2", "i1"), &mut cache).is_err(), "a new connection has no cache to draw on");
    }

    #[test]
    fn engines_without_tokens_always_send_and_are_never_cached() {
        let mut cache = SnapshotCache::default();
        let mut legacy = full("c1", "i1", 3);
        legacy.as_object_mut().unwrap().remove("tokens");
        let snapshot = parse_snapshot_cached(&legacy, &mut cache).unwrap();
        assert_eq!(snapshot.endpoints.len(), 1);
        assert!(cache.known().is_none(), "no token, nothing to claim next time");
        // And an empty device list from such an engine is just empty.
        let mut bare = legacy.clone();
        bare.as_object_mut().unwrap().remove("endpoints");
        bare.as_object_mut().unwrap().remove("applications");
        assert!(parse_snapshot_cached(&bare, &mut cache).unwrap().endpoints.is_empty());
        assert_eq!(parse_snapshot(&legacy).unwrap().configuration.revision, 3, "the uncached reader is unchanged");
    }

    #[test]
    fn requests_name_known_parts_only_when_asked() {
        let plain = request("a", "snapshot", None, None);
        assert!(!plain.contains("known"));
        let knowing = request_knowing("a", "snapshot", None, None, Some(json!({"configuration": "c1"})));
        let value: Value = serde_json::from_str(&knowing).unwrap();
        assert_eq!(value["known"]["configuration"], "c1");
        assert_eq!(value["command"], "snapshot");
    }
}
