//! RUST-REV-006, desktop side: the engine releases an idle control
//! connection, and the desktop's `Connection` must ride through that without
//! reporting a failure.
//!
//! The real pipe server runs on a private pipe name with a short idle limit,
//! over an engine that is constructed on a disposable settings file and never
//! started (no mixer thread, no audio stream).

#![cfg(windows)]

use std::sync::atomic::AtomicBool;
use std::time::Duration;

use vinemix_engine::control::{run_on, Timeouts};
use vinemix_engine::Engine;
use vinemix_ui::connection::Connection;

#[test]
fn desktop_connection_survives_an_engine_side_idle_release() {
    let directory = std::env::temp_dir().join(format!("vinemix-ui-reconnect-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&directory);
    std::fs::create_dir_all(&directory).unwrap();
    // Engine startup installs the application's file logger. Keep test
    // assertion messages visible in the Windows verification report.
    let test_hook = std::panic::take_hook();
    let engine = Engine::new(Some(directory.join("settings.json")));
    std::panic::set_hook(test_hook);
    let engine = engine.unwrap();
    let pipe = format!(r"\\.\pipe\VineMix.ReconnectTest.{}", std::process::id());
    let limits = Timeouts { idle: Duration::from_millis(300), request: Duration::from_secs(2), response: Duration::from_secs(2) };
    let cancel = AtomicBool::new(false);
    std::thread::scope(|scope| {
        let served = scope.spawn(|| run_on(&engine, &pipe, limits));
        let outcome = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            // Identity expectations are for the real per-user endpoint.
            let mut connection = Connection::with_pipe(pipe.clone(), None);
            let first = connection.request("snapshot", None, None, &cancel).expect("first request");
            assert!(first.ok && first.snapshot.is_some());
            for round in 0..3 {
                // Longer than the idle limit: the engine has disconnected us.
                std::thread::sleep(limits.idle * 3);
                let next = connection.request("snapshot", None, None, &cancel);
                let next = next.unwrap_or_else(|error| panic!("round {round}: the idle release surfaced as an error: {error}"));
                assert!(next.ok && next.snapshot.is_some());
                // Requests inside the idle limit reuse the connection.
                let again = connection.request("snapshot", None, None, &cancel).expect("back-to-back request");
                assert!(again.ok);
            }
        }));
        engine.request_stop();
        served.join().unwrap().unwrap();
        if let Err(panic) = outcome {
            std::panic::resume_unwind(panic);
        }
    });
    let _ = std::fs::remove_dir_all(directory);
}
