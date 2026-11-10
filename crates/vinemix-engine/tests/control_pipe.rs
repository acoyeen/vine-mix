//! RUST-REV-006: one stalled control client must not keep the engine's single
//! pipe instance to itself.
//!
//! Each test serves the real pipe server on a private pipe name with short
//! time limits. The engine is constructed on a disposable settings file and
//! never started, so no mixer thread runs and no audio stream is opened.

#![cfg(windows)]

use std::io::{BufRead, BufReader, Write};
use std::sync::atomic::{AtomicU32, Ordering};
use std::time::{Duration, Instant};

use vinemix_engine::control::{run_on, Timeouts};
use vinemix_engine::Engine;

static NEXT: AtomicU32 = AtomicU32::new(0);

const LIMITS: Timeouts = Timeouts { idle: Duration::from_millis(500), request: Duration::from_millis(700), response: Duration::from_millis(700) };

/// Time allowed for a legitimate client to get through once a stalled client
/// has used up its limit (scheduling slack on a loaded machine).
const SLACK: Duration = Duration::from_secs(3);

struct Server {
    engine: Engine,
    pipe: String,
    settings: std::path::PathBuf,
}

fn server(name: &str) -> Server {
    let directory = std::env::temp_dir().join(format!("vinemix-control-{name}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&directory);
    std::fs::create_dir_all(&directory).unwrap();
    let pipe = format!(r"\\.\pipe\VineMix.ControlTest.{}.{}.{name}", std::process::id(), NEXT.fetch_add(1, Ordering::Relaxed));
    Server { engine: Engine::new(Some(directory.join("settings.json"))).unwrap(), pipe, settings: directory }
}

impl Server {
    /// Runs `clients` against the served pipe, then stops the server.
    fn serve(self, clients: impl FnOnce(&str)) {
        std::thread::scope(|scope| {
            let served = scope.spawn(|| run_on(&self.engine, &self.pipe, LIMITS));
            // Always stop the server, or a failed assertion would leave the
            // scope waiting on it forever.
            let outcome = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| clients(&self.pipe)));
            self.engine.request_stop();
            served.join().unwrap().unwrap();
            if let Err(panic) = outcome {
                std::panic::resume_unwind(panic);
            }
        });
        let _ = std::fs::remove_dir_all(&self.settings);
    }
}

fn connect(pipe: &str, within: Duration) -> Option<std::fs::File> {
    let until = Instant::now() + within;
    loop {
        match std::fs::OpenOptions::new().read(true).write(true).open(pipe) {
            Ok(file) => return Some(file),
            Err(_) if Instant::now() < until => std::thread::sleep(Duration::from_millis(10)),
            Err(_) => return None,
        }
    }
}

const SNAPSHOT: &[u8] = b"{\"protocolVersion\":1,\"requestId\":\"t\",\"command\":\"snapshot\"}\n";

fn snapshot(file: &mut std::fs::File) -> std::io::Result<String> {
    file.write_all(SNAPSHOT)?;
    let mut line = String::new();
    BufReader::new(&*file).read_line(&mut line)?;
    Ok(line)
}

/// A complete snapshot exchange by a newly connecting client; returns how
/// long it took to get through.
fn legitimate_client(pipe: &str, within: Duration) -> Duration {
    let started = Instant::now();
    let mut file = connect(pipe, within).unwrap_or_else(|| panic!("no connection within {within:?}: the pipe stayed monopolized"));
    let line = snapshot(&mut file).expect("the exchange completes");
    assert!(line.contains("\"ok\":true") && line.contains("\"settingsState\""), "{}", &line[..line.len().min(200)]);
    started.elapsed()
}

#[test]
fn ordinary_clients_are_served_and_a_polling_client_stays_connected() {
    server("ordinary").serve(|pipe| {
        legitimate_client(pipe, SLACK);
        // One connection polling faster than the idle limit is never dropped,
        // even across a total time several times that limit.
        let mut file = connect(pipe, SLACK).unwrap();
        for _ in 0..10 {
            assert!(snapshot(&mut file).unwrap().contains("\"ok\":true"));
            std::thread::sleep(LIMITS.idle / 3);
        }
    });
}

#[test]
fn silent_client_is_released_for_the_next_client() {
    server("silent").serve(|pipe| {
        let silent = connect(pipe, SLACK).expect("the first client connects");
        let waited = legitimate_client(pipe, LIMITS.idle + SLACK);
        assert!(waited >= LIMITS.idle / 2, "the idle client was given its time ({waited:?})");
        assert!(waited < LIMITS.idle + SLACK, "served after {waited:?}");
        drop(silent);
    });
}

#[test]
fn unfinished_request_line_is_bounded_from_its_first_byte() {
    server("partial").serve(|pipe| {
        let mut partial = connect(pipe, SLACK).expect("the first client connects");
        let trickle = std::thread::spawn(move || {
            // Never idle, never a newline: one byte every 100 ms.
            let _ = partial.write_all(b"{\"protocolVersion\":1,");
            for _ in 0..200 {
                std::thread::sleep(Duration::from_millis(100));
                if partial.write_all(b" ").is_err() {
                    return true;
                }
            }
            false
        });
        std::thread::sleep(Duration::from_millis(100));
        let waited = legitimate_client(pipe, LIMITS.request + SLACK);
        assert!(waited < LIMITS.request + SLACK, "served after {waited:?}");
        assert!(trickle.join().unwrap(), "the trickling client was disconnected");
    });
}

#[test]
fn client_that_never_reads_its_responses_is_released() {
    server("unread").serve(|pipe| {
        let mut greedy = connect(pipe, SLACK).expect("the first client connects");
        let writer = std::thread::spawn(move || {
            // Far more response data than the pipe buffers, never read. Only
            // the server's limit can end this: the handle stays open and the
            // client keeps asking.
            let until = Instant::now() + Duration::from_secs(20);
            while Instant::now() < until {
                if greedy.write_all(SNAPSHOT).is_err() {
                    return true;
                }
                std::thread::sleep(Duration::from_millis(5));
            }
            false
        });
        std::thread::sleep(Duration::from_millis(200));
        let waited = legitimate_client(pipe, LIMITS.response + LIMITS.idle + SLACK);
        assert!(waited < LIMITS.response + LIMITS.idle + SLACK, "served after {waited:?}");
        assert!(writer.join().unwrap(), "the unread client was disconnected while still sending");
    });
}

#[test]
fn stopping_the_engine_ends_a_connection_that_is_waiting() {
    let server = server("stop");
    let started = Instant::now();
    server.serve(|pipe| {
        let _idle = connect(pipe, SLACK).expect("the client connects");
        std::thread::sleep(Duration::from_millis(100));
    });
    assert!(started.elapsed() < SLACK, "shutdown did not wait for the idle limit: {:?}", started.elapsed());
}
