//! RUST-REV-009: a polling client that names the snapshot parts it already
//! holds gets telemetry without an unchanged configuration and device list;
//! anything that changed, and every client that names nothing, still gets
//! the complete snapshot.
//!
//! The starter graph has no assigned devices, so no audio stream is opened.

#![cfg(windows)]

use serde_json::{json, Value};
use vinemix_engine::control::{handle, respond};
use vinemix_engine::Engine;

fn engine(name: &str) -> (Engine, std::path::PathBuf) {
    let directory = std::env::temp_dir().join(format!("vinemix-engine-gating-{name}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&directory);
    std::fs::create_dir_all(&directory).unwrap();
    (Engine::new(Some(directory.join("settings.json"))).unwrap(), directory)
}

fn snapshot(engine: &Engine, known: Option<&Value>) -> Value {
    let mut request = json!({"protocolVersion": 1, "requestId": "g", "command": "snapshot"});
    if let Some(known) = known {
        request["known"] = known.clone();
    }
    let response = handle(engine, &request);
    assert_eq!(response["ok"], true, "{response}");
    response["snapshot"].clone()
}

const STATIC_PARTS: [&str; 3] = ["configuration", "endpoints", "applications"];
const ALWAYS: [&str; 12] = [
    "tokens", "enginePid", "executablePath", "engineProtocolVersion", "engineVersion", "physicalSettingsPath", "status", "settingsPath",
    "logPath", "warning", "settingsState", "backupPath",
];

/// The device and application lists are this machine's own. They arrive a
/// moment after the engine starts (the first maintenance pass) and can change
/// whenever a device or an application comes or goes. A changed inventory is
/// correctly sent again, so a check that expects it to be left out means
/// something only while its token holds still.
///
/// Waits until the inventory token has not changed for 1.5 seconds (one full
/// maintenance pass and more), or panics after 20 seconds.
fn settled_inventory(engine: &Engine) -> Value {
    let started = std::time::Instant::now();
    let mut token = snapshot(engine, None)["tokens"]["inventory"].clone();
    let mut since = std::time::Instant::now();
    loop {
        assert!(started.elapsed() < std::time::Duration::from_secs(20), "the live inventory never held still for 1.5 s");
        std::thread::sleep(std::time::Duration::from_millis(100));
        let current = snapshot(engine, None)["tokens"]["inventory"].clone();
        if current != token {
            token = current;
            since = std::time::Instant::now();
        } else if since.elapsed() >= std::time::Duration::from_millis(1500) {
            return token;
        }
    }
}

/// One pass over the gating rules. Returns false, without judging anything
/// that depends on it, if the live inventory changed underneath the pass.
fn gating_pass(engine: &Engine) -> bool {
    let full = snapshot(engine, None);
    for key in STATIC_PARTS.iter().chain(&ALWAYS) {
        assert!(full.get(*key).is_some(), "a client that names nothing gets {key}");
    }
    let tokens = full["tokens"].clone();
    assert!(tokens["configuration"].is_string() && tokens["inventory"].is_string());
    // Whether a reply was produced against the inventory this pass holds.
    // When it was not, the engine must have sent the new lists.
    let same_inventory = |reply: &Value| {
        let same = reply["tokens"]["inventory"] == tokens["inventory"];
        if !same {
            assert!(reply.get("endpoints").is_some() && reply.get("applications").is_some(), "a changed inventory is always sent");
        }
        same
    };

    // Holding both parts: telemetry and identity only.
    let reduced = snapshot(engine, Some(&tokens));
    if !same_inventory(&reduced) {
        return false;
    }
    for key in STATIC_PARTS {
        assert!(reduced.get(key).is_none(), "{key} is unchanged and was not re-sent");
    }
    for key in ALWAYS {
        assert!(reduced.get(key).is_some(), "{key} is always present");
    }
    assert_eq!(reduced["tokens"], tokens);
    assert_eq!(reduced["status"]["nodes"].as_array().unwrap().len(), 7, "telemetry is complete");
    assert_eq!(reduced["physicalSettingsPath"], full["physicalSettingsPath"]);

    // Holding one part only.
    let only_inventory = snapshot(engine, Some(&json!({"inventory": tokens["inventory"]})));
    if !same_inventory(&only_inventory) {
        return false;
    }
    assert!(only_inventory.get("configuration").is_some() && only_inventory.get("endpoints").is_none());
    let only_configuration = snapshot(engine, Some(&json!({"configuration": tokens["configuration"]})));
    assert!(only_configuration.get("configuration").is_none() && only_configuration.get("endpoints").is_some());

    // Unknown, stale or malformed tokens never suppress anything.
    for known in [json!({"configuration": "c0-0", "inventory": "i0-0"}), json!({"configuration": 7}), json!("nonsense"), json!({})] {
        let reply = snapshot(engine, Some(&known));
        for key in STATIC_PARTS {
            assert!(reply.get(key).is_some(), "{known} must not hide {key}");
        }
    }

    // A configuration change is delivered to a client holding the old one,
    // in the configure reply and in later polls, with a new token.
    let mut configuration = full["configuration"].clone();
    let revision = configuration["revision"].as_u64().unwrap();
    let moved = configuration["graph"]["nodes"][2]["x"].as_f64().unwrap() + 111.0;
    configuration["graph"]["nodes"][2]["x"] = json!(moved);
    let configured = handle(
        engine,
        &json!({"protocolVersion": 1, "command": "configure", "configuration": configuration, "expectedRevision": revision, "known": tokens}),
    );
    assert_eq!(configured["ok"], true, "{configured}");
    let after = &configured["snapshot"];
    assert_eq!(after["configuration"]["revision"], revision + 1, "the new configuration is in the reply");
    assert_ne!(after["tokens"]["configuration"], tokens["configuration"]);
    if !same_inventory(after) {
        return false;
    }
    assert!(after.get("endpoints").is_none(), "the device list did not change");
    let polled = snapshot(engine, Some(&tokens));
    assert_eq!(polled["configuration"]["graph"]["nodes"][2]["x"], moved, "a poll with the old token still receives it");
    let current = snapshot(engine, Some(&after["tokens"]));
    assert!(current.get("configuration").is_none());

    // The reduced reply is what makes polling cheap.
    let line = |known: Option<&Value>| {
        let mut request = json!({"protocolVersion": 1, "command": "snapshot"});
        if let Some(known) = known {
            request["known"] = known.clone();
        }
        respond(engine, serde_json::to_string(&request).unwrap().as_bytes()).0
    };
    let (full_line, reduced_line) = (line(None), line(Some(&after["tokens"])));
    let reduced_reply: Value = serde_json::from_str(&reduced_line).unwrap();
    if !same_inventory(&reduced_reply["snapshot"]) {
        return false;
    }
    assert!(reduced_line.len() * 2 < full_line.len(), "reduced {} bytes, full {} bytes", reduced_line.len(), full_line.len());
    true
}

#[test]
fn unchanged_parts_are_left_out_only_for_a_client_that_holds_them() {
    let (engine, directory) = engine("parts");
    engine.start().unwrap();
    // Not before the first inventory has arrived: starting to judge at once
    // raced the engine's first maintenance pass and failed intermittently.
    settled_inventory(&engine);
    let completed = (0..5).any(|_| gating_pass(&engine) || {
        settled_inventory(&engine);
        false
    });
    assert!(completed, "the live inventory changed during five consecutive passes");
    engine.stop();
    let _ = std::fs::remove_dir_all(directory);
}

#[test]
fn another_engines_tokens_never_match() {
    let (first, first_directory) = engine("first");
    let (second, second_directory) = engine("second");
    let tokens = snapshot(&first, None)["tokens"].clone();
    let reply = snapshot(&second, Some(&tokens));
    for key in STATIC_PARTS {
        assert!(reply.get(key).is_some(), "a client reconnecting to a different engine gets {key} again");
    }
    assert_ne!(reply["tokens"]["configuration"], tokens["configuration"]);
    assert_ne!(reply["tokens"]["inventory"], tokens["inventory"]);
    drop((first, second));
    let _ = std::fs::remove_dir_all(first_directory);
    let _ = std::fs::remove_dir_all(second_directory);
}

/// How often this machine's live inventory changes, and which part of it.
/// The gating only saves work while the inventory token holds still, so a
/// list that changes on every maintenance pass would defeat it. Prints counts
/// and field names only, never device or application names. Run with
/// `cargo test -p vinemix-engine --test snapshot_gating inventory_churn -- --ignored --nocapture`.
#[test]
#[ignore = "diagnostic; prints how often the live inventory changes"]
fn inventory_churn() {
    let (engine, directory) = engine("churn");
    engine.start().unwrap();
    let started = std::time::Instant::now();
    let mut previous = snapshot(&engine, None);
    let mut changes = Vec::new();
    while started.elapsed() < std::time::Duration::from_secs(12) {
        std::thread::sleep(std::time::Duration::from_millis(100));
        let current = snapshot(&engine, None);
        if current["tokens"]["inventory"] != previous["tokens"]["inventory"] {
            let mut what = Vec::new();
            for part in ["endpoints", "applications"] {
                let (before, after) = (previous[part].as_array().unwrap(), current[part].as_array().unwrap());
                if before == after {
                    continue;
                }
                let mut fields = std::collections::BTreeSet::new();
                if before.len() == after.len() {
                    for (a, b) in before.iter().zip(after) {
                        for (key, value) in a.as_object().unwrap() {
                            if b.get(key) != Some(value) {
                                fields.insert(key.clone());
                            }
                        }
                    }
                }
                what.push(format!("{part}: {} -> {} entries, differing fields {fields:?}", before.len(), after.len()));
            }
            changes.push(format!("at {:.1} s: {}", started.elapsed().as_secs_f64(), what.join("; ")));
        }
        previous = current;
    }
    println!("inventory token changes in 12 s: {}", changes.len());
    for change in &changes {
        println!("  {change}");
    }
    engine.stop();
    let _ = std::fs::remove_dir_all(directory);
}

/// Polling cost on each path. Run with
/// `cargo test -p vinemix-engine --release --test snapshot_gating -- --ignored --nocapture`.
/// The device and application lists are this machine's own, so the absolute
/// sizes are not a portable baseline; the comparison is like for like.
#[test]
#[ignore = "benchmark; prints sizes and timings"]
fn polling_cost() {
    use std::time::Instant;
    let (engine, directory) = engine("cost");
    engine.start().unwrap();
    std::thread::sleep(std::time::Duration::from_millis(1500)); // let maintenance list applications
    let tokens = snapshot(&engine, None)["tokens"].clone();
    let full = serde_json::to_string(&json!({"protocolVersion": 1, "command": "snapshot"})).unwrap();
    let reduced = serde_json::to_string(&json!({"protocolVersion": 1, "command": "snapshot", "known": tokens})).unwrap();
    for (name, request) in [("full snapshot (no known tokens)", &full), ("reduced snapshot (known tokens)", &reduced)] {
        let mut times = Vec::new();
        let mut bytes = 0;
        for _ in 0..2000 {
            let started = Instant::now();
            bytes = respond(&engine, request.as_bytes()).0.len();
            times.push(started.elapsed());
        }
        times.sort();
        println!(
            "{name}: {bytes} bytes; engine time median {:?}, p99 {:?}; at 30 Hz: {:.1} kB/s",
            times[times.len() / 2],
            times[times.len() * 99 / 100],
            bytes as f64 * 30.0 / 1000.0
        );
    }
    engine.stop();
    let _ = std::fs::remove_dir_all(directory);
}
