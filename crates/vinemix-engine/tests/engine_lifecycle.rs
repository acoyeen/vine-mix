//! In-process engine lifecycle through the protocol handler. The starter
//! graph has no assigned devices, so no audio stream is opened.

#![cfg(windows)]

use serde_json::{json, Value};
use vinemix_engine::control::{handle, respond};
use vinemix_engine::Engine;

fn temp_settings(name: &str) -> std::path::PathBuf {
    let directory = std::env::temp_dir().join(format!("vinemix-engine-{name}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&directory);
    std::fs::create_dir_all(&directory).unwrap();
    directory.join("settings.json")
}

fn request(engine: &Engine, body: Value) -> Value {
    handle(engine, &body)
}

#[test]
fn snapshot_configure_and_conflicts() {
    let settings = temp_settings("lifecycle");
    let engine = Engine::new(Some(settings.clone())).unwrap();
    engine.start().unwrap();
    let snapshot = request(&engine, json!({"protocolVersion": 1, "requestId": "a", "command": "snapshot"}));
    assert_eq!(snapshot["ok"], true);
    assert_eq!(snapshot["requestId"], "a");
    let body = &snapshot["snapshot"];
    for key in [
        "configuration", "endpoints", "applications", "enginePid", "executablePath", "engineProtocolVersion", "engineVersion",
        "displayVersion", "physicalSettingsPath", "appCaptureAvailable", "status", "settingsPath", "logPath", "warning",
        "settingsState", "backupPath",
    ] {
        assert!(body.get(key).is_some(), "snapshot lacks {key}");
    }
    assert_eq!(body["settingsState"], "new");
    assert_eq!(body["status"]["nodes"].as_array().unwrap().len(), 7);
    let mut configuration = body["configuration"].clone();
    let revision = configuration["revision"].as_u64().unwrap();
    configuration["graph"]["nodes"][1]["eqBands"][0]["frequencyHz"] = json!(120.0);
    let configured = request(
        &engine,
        json!({"protocolVersion": 1, "requestId": 7, "command": "configure", "configuration": configuration.clone(), "expectedRevision": revision}),
    );
    assert_eq!(configured["ok"], true, "{configured}");
    assert_eq!(configured["requestId"], 7);
    assert_eq!(configured["snapshot"]["configuration"]["revision"], revision + 1);
    assert_eq!(configured["snapshot"]["settingsState"], "loaded");
    // A stale revision is rejected with the stable code.
    let stale = request(
        &engine,
        json!({"protocolVersion": 1, "command": "configure", "configuration": configuration.clone(), "expectedRevision": revision}),
    );
    assert_eq!(stale["ok"], false);
    assert_eq!(stale["errorCode"], "revision_conflict");
    // Validation errors are user-facing messages without a code.
    let mut invalid = configuration.clone();
    invalid["graph"]["nodes"][0]["gainDb"] = json!(40.0);
    let rejected = request(
        &engine,
        json!({"protocolVersion": 1, "command": "configure", "configuration": invalid, "expectedRevision": revision + 1}),
    );
    assert_eq!(rejected["error"], "Node and cable volumes must be between -60 and +12 dB.");
    assert!(rejected.get("errorCode").is_none());
    // Position-only edits persist without rebuilding audio.
    let mut moved = configured["snapshot"]["configuration"].clone();
    moved["graph"]["nodes"][2]["x"] = json!(1000.0);
    let moved_ok = request(
        &engine,
        json!({"protocolVersion": 1, "command": "configure", "configuration": moved, "expectedRevision": revision + 1}),
    );
    assert_eq!(moved_ok["ok"], true);
    let persisted: Value = serde_json::from_slice(&std::fs::read(&settings).unwrap()).unwrap();
    assert_eq!(persisted["revision"], revision + 2);
    assert_eq!(persisted["graph"]["nodes"][2]["x"], 1000.0);
    // Protocol errors.
    assert_eq!(request(&engine, json!({"protocolVersion": 2, "command": "snapshot"}))["error"], "Unsupported control protocol version.");
    assert_eq!(request(&engine, json!({"protocolVersion": 1, "command": "dance"}))["error"], "Unknown control command.");
    assert_eq!(request(&engine, json!([1]))["error"], "A control request must be a JSON object.");
    let (text, shutdown) = respond(&engine, br#"{"protocolVersion":1,"command":"shutdown"}"#);
    assert!(shutdown);
    assert!(text.ends_with('\n'));
    let (text, shutdown) = respond(&engine, b"{not json");
    assert!(!shutdown);
    assert!(text.contains("\"ok\":false"));
    // Audio advanced while we worked.
    std::thread::sleep(std::time::Duration::from_millis(100));
    let frames = request(&engine, json!({"protocolVersion": 1, "command": "snapshot"}))["snapshot"]["status"]["audioFrames"]
        .as_u64()
        .unwrap();
    assert!(frames > 0);
    engine.stop();
    let _ = std::fs::remove_dir_all(settings.parent().unwrap());
}

#[test]
fn recovery_flow_restores_backup() {
    let settings = temp_settings("recovery");
    {
        let engine = Engine::new(Some(settings.clone())).unwrap();
        engine.start().unwrap();
        let configuration = engine.snapshot()["configuration"].clone();
        for revision in 0..2u64 {
            let mut next = configuration.clone();
            next["graph"]["nodes"][0]["name"] = json!(format!("Mic {revision}"));
            let reply = request(
                &engine,
                json!({"protocolVersion": 1, "command": "configure", "configuration": next, "expectedRevision": revision}),
            );
            assert_eq!(reply["ok"], true, "{reply}");
        }
        engine.stop();
    }
    std::fs::write(&settings, b"corrupted").unwrap();
    let engine = Engine::new(Some(settings.clone())).unwrap();
    engine.start().unwrap();
    let snapshot = engine.snapshot();
    assert_eq!(snapshot["settingsState"], "backupAvailable");
    assert!(snapshot["status"]["nodes"].as_array().unwrap().is_empty(), "audio stays paused");
    let configure = request(
        &engine,
        json!({"protocolVersion": 1, "command": "configure", "configuration": snapshot["configuration"].clone(), "expectedRevision": 1}),
    );
    assert_eq!(configure["error"], "Choose workspace recovery before changing the configuration.");
    let restored = request(&engine, json!({"protocolVersion": 1, "command": "restoreBackup"}));
    assert_eq!(restored["ok"], true, "{restored}");
    assert_eq!(restored["snapshot"]["settingsState"], "loaded");
    assert_eq!(restored["snapshot"]["configuration"]["graph"]["nodes"][0]["name"], "Mic 0");
    assert_eq!(restored["snapshot"]["status"]["nodes"].as_array().unwrap().len(), 7, "audio resumes");
    engine.stop();
    let _ = std::fs::remove_dir_all(settings.parent().unwrap());
}

/// Post-0.5 processing-load telemetry through the real mix loop (starter graph,
/// no devices): live measurements, stable reads, stop and same-object restart.
#[test]
fn processing_load_telemetry() {
    let settings = temp_settings("processing-load");
    let engine = Engine::new(Some(settings)).unwrap();
    let load = |engine: &Engine| request(engine, json!({"protocolVersion": 1, "command": "snapshot"}))["snapshot"]["status"]["processingLoad"].clone();
    let before = load(&engine);
    assert_eq!(before["available"], false, "unavailable before the first measured quantum");
    assert_eq!(before["budgetMs"], 5.0);
    let eventually = |engine: &Engine, ok: &dyn Fn(&Value) -> bool| {
        for _ in 0..400 {
            let value = load(engine);
            if ok(&value) {
                return value;
            }
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
        panic!("processing load never satisfied the condition: {}", load(engine));
    };
    engine.start().unwrap();
    let live = eventually(&engine, &|v| v["available"] == true && v["sampleCount"].as_u64().unwrap_or(0) >= 20);
    assert_eq!(live["state"], "normal");
    assert!(live["averagePercent"].as_f64().unwrap() >= 0.0 && live["peakPercent"].as_f64().unwrap().is_finite());
    assert!(live["windowSamples"].as_u64().unwrap() <= 200);
    let session = live["sampleCount"].as_u64().unwrap();
    engine.stop();
    let stopped = load(&engine);
    assert_eq!(stopped["available"], false, "a stopped engine is never reported healthy");
    assert!(stopped["sampleCount"].as_u64().unwrap() >= session);
    engine.start().unwrap();
    let restarted = load(&engine);
    assert!(restarted["sampleCount"].as_u64().unwrap() < session, "restart cannot leak the previous session's counters: {restarted}");
    eventually(&engine, &|v| v["available"] == true);
    engine.stop();
}

#[test]
fn concurrent_stops_complete_before_restart() {
    let settings = temp_settings("concurrent-stop");
    let engine = Engine::new(Some(settings.clone())).unwrap();
    for _ in 0..3 {
        engine.start().unwrap();
        let barrier = std::sync::Barrier::new(3);
        std::thread::scope(|scope| {
            for _ in 0..2 {
                scope.spawn(|| {
                    barrier.wait();
                    engine.stop();
                });
            }
            barrier.wait();
        });
        assert!(engine.stopping());
    }
    drop(engine);
    let _ = std::fs::remove_dir_all(settings.parent().unwrap());
}
