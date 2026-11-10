//! RUST-REV-010 / RUST-REV-011: display-only work follows what a client says
//! it is showing. An EQ's spectrum is analysed only for clients that list
//! it, and the application list is enumerated only while a client wants it.
//! Clients that say nothing get everything, as before.
//!
//! The graph has no assigned devices, so no audio stream is opened.

#![cfg(windows)]

use serde_json::{json, Value};
use vinemix_engine::control::handle;
use vinemix_engine::Engine;

fn snapshot(engine: &Engine, interest: Option<Value>) -> Value {
    let mut request = json!({"protocolVersion": 1, "command": "snapshot"});
    if let Some(interest) = interest {
        request["interest"] = interest;
    }
    let response = handle(engine, &request);
    assert_eq!(response["ok"], true, "{response}");
    response["snapshot"].clone()
}

fn node<'a>(snapshot: &'a Value, id: &str) -> &'a Value {
    snapshot["status"]["nodes"].as_array().unwrap().iter().find(|n| n["id"] == id).unwrap_or_else(|| panic!("no node {id}"))
}

/// Polls until the EQ's analyser has produced a frame for a full snapshot.
fn wait_for_spectrum(engine: &Engine, id: &str) -> Value {
    for _ in 0..400 {
        let reply = snapshot(engine, None);
        if node(&reply, id)["spectrumFrame"].as_u64().unwrap_or(0) != 0 {
            return reply;
        }
        std::thread::sleep(std::time::Duration::from_millis(10));
    }
    panic!("the analyser never produced a frame");
}

#[test]
fn spectrum_is_analysed_only_for_the_nodes_a_client_shows() {
    let directory = std::env::temp_dir().join(format!("vinemix-engine-interest-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&directory);
    std::fs::create_dir_all(&directory).unwrap();
    let engine = Engine::new(Some(directory.join("settings.json"))).unwrap();
    engine.start().unwrap();
    // Add two EQ nodes to the starter graph (unconnected: silence is enough
    // for the analyser to run and report its floor).
    let current = engine.snapshot()["configuration"].clone();
    let revision = current["revision"].as_u64().unwrap();
    let mut configuration = vinemix_model::Configuration::from_json(&current).unwrap();
    let existing = "interest-eq-1".to_owned();
    for (index, id) in [existing.as_str(), "interest-eq-2"].into_iter().enumerate() {
        configuration.graph.nodes.push(vinemix_model::GraphNode::new(id, "eq", "Example EQ", 40.0, 600.0 + 200.0 * index as f64));
    }
    let reply = handle(
        &engine,
        &json!({"protocolVersion": 1, "command": "configure", "configuration": configuration.to_json(), "expectedRevision": revision}),
    );
    assert_eq!(reply["ok"], true, "{reply}");

    // A client that says nothing gets every spectrum.
    let full = wait_for_spectrum(&engine, &existing);
    wait_for_spectrum(&engine, "interest-eq-2");
    assert_eq!(node(&full, &existing)["spectrumDb"].as_array().unwrap().len(), 128);
    let revision_token = node(&full, &existing)["spectrumRevision"].as_u64().unwrap();

    // A client showing only the second EQ: the first is not analysed.
    let partial = snapshot(&engine, Some(json!({"spectrum": ["interest-eq-2"]})));
    assert_eq!(node(&partial, &existing)["spectrumFrame"], 0, "an EQ nobody shows reports no spectrum frame");
    assert!(node(&partial, &existing)["spectrumDb"].as_array().is_none_or(Vec::is_empty));
    assert_ne!(node(&partial, "interest-eq-2")["spectrumFrame"], 0);
    assert_eq!(node(&partial, "interest-eq-2")["spectrumDb"].as_array().unwrap().len(), 128);
    // Meters and paths are unaffected by spectrum interest.
    assert_eq!(node(&partial, &existing)["path"], node(&full, &existing)["path"]);

    // A hidden window: none.
    let none = snapshot(&engine, Some(json!({"spectrum": [], "applications": false})));
    for id in [existing.as_str(), "interest-eq-2"] {
        assert_eq!(node(&none, id)["spectrumFrame"], 0);
    }

    // The viewer returns: live data at once, on the same analyser revision
    // (the desktop then resumes drawing; nothing was reset).
    std::thread::sleep(std::time::Duration::from_millis(60));
    let returned = snapshot(&engine, Some(json!({"spectrum": [existing.clone()]})));
    let back = node(&returned, &existing);
    assert_ne!(back["spectrumFrame"], 0);
    assert!(back["spectrumFrame"].as_u64().unwrap() > node(&full, &existing)["spectrumFrame"].as_u64().unwrap(), "the frame is current, not a stale one");
    assert_eq!(back["spectrumRevision"].as_u64().unwrap(), revision_token);
    assert_eq!(back["spectrumDb"].as_array().unwrap().len(), 128);

    engine.stop();
    let _ = std::fs::remove_dir_all(directory);
}

#[test]
fn application_list_is_wanted_only_while_a_client_asks() {
    let directory = std::env::temp_dir().join(format!("vinemix-engine-interest-apps-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&directory);
    std::fs::create_dir_all(&directory).unwrap();
    let engine = Engine::new(Some(directory.join("settings.json"))).unwrap();
    assert!(!engine.applications_wanted(), "nothing is enumerated before a client asks");
    // A hidden window polling says it does not need the list.
    for _ in 0..3 {
        snapshot(&engine, Some(json!({"applications": false})));
    }
    assert!(!engine.applications_wanted());
    // A visible window (or an older client that says nothing) does.
    snapshot(&engine, Some(json!({"applications": true})));
    assert!(engine.applications_wanted());
    let (other, other_directory) = {
        let directory = std::env::temp_dir().join(format!("vinemix-engine-interest-apps2-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&directory);
        std::fs::create_dir_all(&directory).unwrap();
        (Engine::new(Some(directory.join("settings.json"))).unwrap(), directory)
    };
    snapshot(&other, None);
    assert!(other.applications_wanted(), "saying nothing means wanting the list");
    // Interest lapses a few seconds after the last request that wanted it,
    // and hidden polls do not renew it.
    let until = std::time::Instant::now() + std::time::Duration::from_millis(3600);
    while std::time::Instant::now() < until {
        snapshot(&engine, Some(json!({"applications": false})));
        std::thread::sleep(std::time::Duration::from_millis(200));
    }
    assert!(!engine.applications_wanted());
    drop((engine, other));
    let _ = std::fs::remove_dir_all(directory);
    let _ = std::fs::remove_dir_all(other_directory);
}
