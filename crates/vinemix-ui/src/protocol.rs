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
    if !value.is_object() {
        return Err("The engine snapshot has an unsupported shape.".into());
    }
    let configuration = value.get("configuration").ok_or("The engine configuration has an unsupported shape.")?;
    let configuration =
        Configuration::from_json(configuration).map_err(|e| format!("The engine configuration has an unsupported shape. {}", e.message))?;
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
        applications: list(value, "applications")?,
        configuration,
        endpoints: list(value, "endpoints")?,
        status,
        settings_path: text(value, "settingsPath"),
        log_path: text(value, "logPath"),
        warning: text(value, "warning"),
    })
}

pub fn parse_response(line: &[u8]) -> Result<Response, String> {
    let value = vinemix_model::json::parse(line).map_err(|e| format!("The engine returned an unreadable response: {}", e.message))?;
    if !value.is_object() {
        return Err("The engine returned an empty response.".into());
    }
    let snapshot = match value.get("snapshot") {
        None | Some(Value::Null) => None,
        Some(snapshot) => Some(parse_snapshot(snapshot)?),
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
    let mut message = json!({"protocolVersion": 1, "requestId": request_id, "command": command});
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
