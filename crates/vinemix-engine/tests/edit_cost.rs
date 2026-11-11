//! RUST-REV-007 / RUST-REV-008 at the engine boundary: an accepted edit
//! compiles its graph once, rejected configurations still never reach the
//! store, and a burst of parameter edits is absorbed while audio keeps
//! running.
//!
//! One test in its own process, because the compilation counter is global.
//! The starter graph has no assigned devices, so no audio stream is opened.

#![cfg(windows)]

use serde_json::{json, Value};
use vinemix_engine::control::handle;
use vinemix_engine::Engine;
use vinemix_model::plan::compilation_count;

fn configure(engine: &Engine, configuration: &Value, expected: u64) -> Value {
    handle(engine, &json!({"protocolVersion": 1, "command": "configure", "configuration": configuration, "expectedRevision": expected}))
}

#[test]
fn accepted_edits_compile_once_and_parameter_bursts_keep_audio_running() {
    let directory = std::env::temp_dir().join(format!("vinemix-engine-edit-cost-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&directory);
    std::fs::create_dir_all(&directory).unwrap();
    let settings = directory.join("settings.json");
    let engine = Engine::new(Some(settings.clone())).unwrap();
    engine.start().unwrap();
    let mut configuration = engine.snapshot()["configuration"].clone();
    let mut revision = configuration["revision"].as_u64().unwrap();

    // A parameter edit: one compilation (it used to be three).
    configuration["graph"]["nodes"][1]["eqBands"][0]["frequencyHz"] = json!(120.0);
    let before = compilation_count();
    let reply = configure(&engine, &configuration, revision);
    assert_eq!(reply["ok"], true, "{reply}");
    assert_eq!(compilation_count() - before, 1, "an accepted parameter edit compiles its graph exactly once");
    revision += 1;

    // A layout-only edit: still one compilation, and it is persisted.
    configuration["graph"]["nodes"][2]["x"] = json!(1234.0);
    let before = compilation_count();
    assert_eq!(configure(&engine, &configuration, revision)["ok"], true);
    assert_eq!(compilation_count() - before, 1, "a layout edit compiles once");
    revision += 1;
    let persisted: Value = serde_json::from_slice(&std::fs::read(&settings).unwrap()).unwrap();
    assert_eq!(persisted["revision"], revision);
    assert_eq!(persisted["graph"]["nodes"][2]["x"], 1234.0);

    // The validation boundary is intact: an invalid configuration is
    // rejected with its user-facing message and never reaches the store.
    let mut invalid = configuration.clone();
    invalid["graph"]["nodes"][0]["gainDb"] = json!(40.0);
    let rejected = configure(&engine, &invalid, revision);
    assert_eq!(rejected["error"], "Node and cable volumes must be between -60 and +12 dB.");
    let mut unknown = configuration.clone();
    unknown["graph"]["nodes"][1]["kind"] = json!("notANode");
    assert_eq!(configure(&engine, &unknown, revision)["ok"], false);
    let persisted: Value = serde_json::from_slice(&std::fs::read(&settings).unwrap()).unwrap();
    assert_eq!(persisted["revision"], revision, "rejected configurations are not saved");
    assert_eq!(engine.snapshot()["configuration"]["revision"], revision);

    // A burst of parameter edits (a dragged dial): every one is accepted —
    // the engine never reports that it has stopped adopting changes — and
    // the mixer keeps producing audio throughout.
    let frames_before = engine.snapshot()["status"]["audioFrames"].as_u64().unwrap();
    let before = compilation_count();
    for step in 0..150u64 {
        configuration["graph"]["nodes"][1]["eqBands"][0]["frequencyHz"] = json!(60.0 + (step % 100) as f64);
        configuration["graph"]["nodes"][1]["gainDb"] = json!((step % 12) as f64 - 6.0);
        let reply = configure(&engine, &configuration, revision);
        assert_eq!(reply["ok"], true, "edit {step}: {reply}");
        revision += 1;
    }
    assert_eq!(compilation_count() - before, 150);
    std::thread::sleep(std::time::Duration::from_millis(100));
    let snapshot = engine.snapshot();
    assert!(snapshot["status"]["audioFrames"].as_u64().unwrap() > frames_before, "audio advanced during the burst");
    assert_eq!(snapshot["configuration"]["revision"], revision);
    assert_eq!(snapshot["configuration"]["graph"]["nodes"][1]["eqBands"][0]["frequencyHz"], 109.0, "the last edit is the live one");

    // A dragged control: its configures say `persist: false`. They take
    // effect, and the settings file is written once, by the configure that
    // ends the drag.
    let saved_before = std::fs::read(&settings).unwrap();
    for step in 0..20u64 {
        configuration["graph"]["nodes"][1]["gainDb"] = json!(step as f64 * 0.5 - 6.0);
        let reply = handle(
            &engine,
            &json!({"protocolVersion": 1, "command": "configure", "configuration": configuration, "expectedRevision": revision, "persist": false}),
        );
        assert_eq!(reply["ok"], true, "drag step {step}: {reply}");
        revision += 1;
        assert_eq!(reply["snapshot"]["configuration"]["revision"], revision);
    }
    assert_eq!(engine.snapshot()["configuration"]["graph"]["nodes"][1]["gainDb"], 3.5, "the drag is live");
    assert_eq!(std::fs::read(&settings).unwrap(), saved_before, "nothing was written during the drag");
    assert_eq!(configure(&engine, &configuration, revision)["ok"], true);
    revision += 1;
    let persisted: Value = serde_json::from_slice(&std::fs::read(&settings).unwrap()).unwrap();
    assert_eq!(persisted["revision"], revision);
    assert_eq!(persisted["graph"]["nodes"][1]["gainDb"], 3.5, "the release saves where the drag ended");

    // A client that leaves in the middle of a drag: the engine writes what
    // it is playing (the control server does this when a client disconnects).
    configuration["graph"]["nodes"][1]["gainDb"] = json!(-2.0);
    let reply = handle(
        &engine,
        &json!({"protocolVersion": 1, "command": "configure", "configuration": configuration, "expectedRevision": revision, "persist": false}),
    );
    assert_eq!(reply["ok"], true, "{reply}");
    revision += 1;
    engine.flush().unwrap();
    let persisted: Value = serde_json::from_slice(&std::fs::read(&settings).unwrap()).unwrap();
    assert_eq!(persisted["revision"], revision);
    assert_eq!(persisted["graph"]["nodes"][1]["gainDb"], -2.0);

    // A topology edit (removing a cable) still goes through the full path.
    let mut rewired = configuration.clone();
    let cables = rewired["graph"]["edges"].as_array_mut().unwrap();
    cables.pop();
    let reply = configure(&engine, &rewired, revision);
    assert_eq!(reply["ok"], true, "{reply}");
    std::thread::sleep(std::time::Duration::from_millis(50));
    assert_eq!(engine.snapshot()["status"]["nodes"].as_array().unwrap().len(), 7, "audio continues on the new graph");

    engine.stop();
    let _ = std::fs::remove_dir_all(directory);
}
