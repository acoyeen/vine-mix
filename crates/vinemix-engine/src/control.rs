//! Control protocol v1 (`control_server.cpp`): newline-delimited JSON over a
//! named pipe that only this Windows user and SYSTEM may open, rejecting
//! remote clients. One client at a time; requests are bounded to 256 KiB.
//!
//! Because there is a single pipe instance, a connected client that stops
//! talking would keep every other client out. Each connection is therefore
//! bounded in time as well as size ([`Timeouts`]): a client that sends
//! nothing, leaves a request line unfinished or does not read its response
//! is disconnected, and the next client is served. The desktop polls at
//! least once a second and transparently reconnects an idle connection, so
//! these limits are invisible to it.

use serde_json::{json, Value};
use vinemix_model::config::{read_revision, Configuration};
use vinemix_model::json as model_json;
use vinemix_model::limits::MAXIMUM_CONTROL_MESSAGE_BYTES;

use crate::engine::{Engine, EngineError, Interest, Known};

fn error_text(error: &EngineError) -> String {
    error.to_string()
}

/// Dispatches one already-framed request.
pub fn handle(engine: &Engine, request: &Value) -> Value {
    let mut response = json!({"protocolVersion": 1, "requestId": "", "ok": true});
    let result = (|| -> Result<(), EngineError> {
        let Some(object) = request.as_object() else {
            return Err(EngineError::Message("A control request must be a JSON object.".into()));
        };
        response["requestId"] = object.get("requestId").cloned().unwrap_or_else(|| json!(""));
        let protocol = model_json::at(request, "protocolVersion")?;
        if !model_json::is_integer(protocol) || protocol.as_i64() != Some(1) {
            return Err(EngineError::Message("Unsupported control protocol version.".into()));
        }
        let command = model_json::get_string(model_json::at(request, "command")?)?;
        // Optional and additive: the snapshot parts this client already has.
        let known = object.get("known");
        let token = |part: &str| known.and_then(|k| k.get(part)).and_then(Value::as_str);
        let known = Known { configuration: token("configuration"), inventory: token("inventory") };
        // Optional and additive: what this client is currently showing.
        let interest = object.get("interest");
        let spectrum: Option<Vec<&str>> = interest
            .and_then(|i| i.get("spectrum"))
            .and_then(Value::as_array)
            .map(|ids| ids.iter().filter_map(Value::as_str).collect());
        let interest = Interest {
            spectrum: spectrum.as_deref(),
            applications: interest.and_then(|i| i.get("applications")).and_then(Value::as_bool),
        };
        match command.as_str() {
            "snapshot" => response["snapshot"] = engine.snapshot_for(known, interest),
            "configure" => {
                let configuration = Configuration::from_json(model_json::at(request, "configuration")?)?;
                let expected = read_revision(model_json::at(request, "expectedRevision")?)?;
                // Optional and additive: `false` applies without writing the
                // settings file (a control is still being dragged).
                let persist = object.get("persist").and_then(Value::as_bool).unwrap_or(true);
                engine.configure(configuration, expected, persist)?;
                response["snapshot"] = engine.snapshot_for(known, interest);
            }
            "retry" => {
                engine.retry();
                response["snapshot"] = engine.snapshot_for(known, interest);
            }
            "recoveryRetry" | "restoreBackup" | "startFresh" => {
                engine.recover(if command == "recoveryRetry" { "retry" } else { &command })?;
                response["snapshot"] = engine.snapshot_for(known, interest);
            }
            "shutdown" => {
                engine.flush()?;
                response["message"] = json!("Audio engine stopped.");
            }
            _ => return Err(EngineError::Message("Unknown control command.".into())),
        }
        Ok(())
    })();
    if let Err(error) = result {
        response["ok"] = json!(false);
        response["error"] = json!(error_text(&error));
        match error {
            EngineError::Conflict => response["errorCode"] = json!("revision_conflict"),
            EngineError::SettingsWrite(_) => response["errorCode"] = json!("settings_write_failed"),
            EngineError::Message(_) => {}
        }
    }
    response
}

/// Processes one received line. Returns the response text and whether the
/// engine should stop after writing it.
pub fn respond(engine: &Engine, line: &[u8]) -> (String, bool) {
    let line = line.strip_suffix(b"\r").unwrap_or(line);
    match model_json::parse(line) {
        Ok(request) => {
            let response = handle(engine, &request);
            let shutdown = response["ok"] == json!(true) && request.get("command").and_then(Value::as_str) == Some("shutdown");
            (model_json::dump(&response) + "\n", shutdown)
        }
        Err(error) => {
            let response = json!({"protocolVersion": 1, "requestId": "", "ok": false, "error": error.to_string()});
            (model_json::dump(&response) + "\n", false)
        }
    }
}

#[cfg(windows)]
pub use pipe::{run, run_on, Timeouts};

#[cfg(windows)]
mod pipe {
    use super::*;
    use std::time::{Duration, Instant};
    use vinemix_wasapi::win::Event;
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::{
        CloseHandle, GetLastError, LocalFree, ERROR_IO_PENDING, ERROR_PIPE_CONNECTED, HANDLE, HLOCAL, INVALID_HANDLE_VALUE, WAIT_OBJECT_0,
        WAIT_TIMEOUT,
    };
    use windows::Win32::Security::Authorization::{ConvertStringSecurityDescriptorToSecurityDescriptorW, SDDL_REVISION_1};
    use windows::Win32::Security::{PSECURITY_DESCRIPTOR, SECURITY_ATTRIBUTES};
    use windows::Win32::Storage::FileSystem::{ReadFile, WriteFile, FILE_FLAG_OVERLAPPED, PIPE_ACCESS_DUPLEX};
    use windows::Win32::System::Pipes::{
        ConnectNamedPipe, CreateNamedPipeW, DisconnectNamedPipe, PIPE_READMODE_BYTE, PIPE_REJECT_REMOTE_CLIENTS, PIPE_TYPE_BYTE, PIPE_WAIT,
    };
    use windows::Win32::System::Threading::WaitForSingleObject;
    use windows::Win32::System::IO::{CancelIoEx, GetOverlappedResult, OVERLAPPED};

    struct Pipe(HANDLE);

    impl Drop for Pipe {
        fn drop(&mut self) {
            if !self.0.is_invalid() {
                // SAFETY: owned pipe handle.
                unsafe {
                    let _ = CloseHandle(self.0);
                }
            }
        }
    }

    /// Per-connection time limits.
    #[derive(Clone, Copy, Debug, PartialEq, Eq)]
    pub struct Timeouts {
        /// No bytes from a connected client between requests.
        pub idle: Duration,
        /// From the first byte of a request line to its newline.
        pub request: Duration,
        /// For the client to accept a response.
        pub response: Duration,
    }

    impl Default for Timeouts {
        fn default() -> Self {
            // The desktop's slowest polling interval is one second.
            Self { idle: Duration::from_secs(10), request: Duration::from_secs(5), response: Duration::from_secs(5) }
        }
    }

    /// Waits for an overlapped operation, cancelling it when the engine stops
    /// or `deadline` passes. The OVERLAPPED storage stays alive until
    /// cancellation completes.
    fn complete(pipe: HANDLE, operation: &mut OVERLAPPED, engine: &Engine, event: &Event, deadline: Option<Instant>) -> Option<u32> {
        while !engine.stopping() {
            let slice = match deadline {
                Some(deadline) => {
                    let remaining = deadline.saturating_duration_since(Instant::now());
                    if remaining.is_zero() {
                        break;
                    }
                    u32::try_from(remaining.as_millis()).unwrap_or(200).clamp(1, 200)
                }
                None => 200,
            };
            // SAFETY: valid event handle.
            let wait = unsafe { WaitForSingleObject(event.handle(), slice) };
            if wait == WAIT_OBJECT_0 {
                let mut bytes = 0u32;
                // SAFETY: the operation completed on this handle.
                return unsafe { GetOverlappedResult(pipe, operation, &mut bytes, false) }.ok().map(|()| bytes);
            }
            if wait != WAIT_TIMEOUT {
                break;
            }
        }
        let mut bytes = 0u32;
        // SAFETY: cancel, then wait for the cancellation to finish.
        unsafe {
            let _ = CancelIoEx(pipe, Some(operation));
            let _ = GetOverlappedResult(pipe, operation, &mut bytes, true);
        }
        None
    }

    fn read_chunk(pipe: HANDLE, buffer: &mut [u8], engine: &Engine, event: &Event, deadline: Instant) -> Option<u32> {
        let mut operation = OVERLAPPED { hEvent: event.handle(), ..Default::default() };
        event.reset();
        let mut count = 0u32;
        // SAFETY: the buffer and OVERLAPPED outlive the operation (we wait).
        match unsafe { ReadFile(pipe, Some(buffer), Some(&mut count), Some(&mut operation)) } {
            Ok(()) => Some(count),
            // SAFETY: trivial Win32 call.
            Err(_) if unsafe { GetLastError() } == ERROR_IO_PENDING => complete(pipe, &mut operation, engine, event, Some(deadline)),
            Err(_) => None,
        }
    }

    fn write_all(pipe: HANDLE, text: &[u8], engine: &Engine, event: &Event, deadline: Instant) -> bool {
        let mut offset = 0;
        while offset < text.len() {
            let requested = (text.len() - offset).min(32768);
            let mut operation = OVERLAPPED { hEvent: event.handle(), ..Default::default() };
            event.reset();
            let mut count = 0u32;
            // SAFETY: as in `read_chunk`.
            let written = match unsafe { WriteFile(pipe, Some(&text[offset..offset + requested]), Some(&mut count), Some(&mut operation)) }
            {
                Ok(()) => Some(count),
                // SAFETY: trivial Win32 call.
                Err(_) if unsafe { GetLastError() } == ERROR_IO_PENDING => complete(pipe, &mut operation, engine, event, Some(deadline)),
                Err(_) => None,
            };
            match written {
                Some(count) if count > 0 => offset += count as usize,
                _ => return false,
            }
        }
        true
    }

    /// Serves control clients on the per-user pipe until the engine stops.
    pub fn run(engine: &Engine) -> Result<(), String> {
        run_on(engine, &vinemix_platform::pipe_name()?, Timeouts::default())
    }

    /// Serves control clients on `pipe_name` (tests use a private name).
    pub fn run_on(engine: &Engine, pipe_name: &str, timeouts: Timeouts) -> Result<(), String> {
        let sid = vinemix_platform::current_user_sid()?;
        let sddl = vinemix_platform::wide(&format!("D:P(A;;GA;;;SY)(A;;GA;;;{sid})"));
        let mut descriptor = PSECURITY_DESCRIPTOR::default();
        // SAFETY: valid SDDL; the descriptor is freed below.
        unsafe { ConvertStringSecurityDescriptorToSecurityDescriptorW(PCWSTR(sddl.as_ptr()), SDDL_REVISION_1, &mut descriptor, None) }
            .map_err(|_| "Cannot secure the local control connection.".to_owned())?;
        struct FreeDescriptor(PSECURITY_DESCRIPTOR);
        impl Drop for FreeDescriptor {
            fn drop(&mut self) {
                // SAFETY: allocated by ConvertStringSecurityDescriptorToSecurityDescriptorW.
                unsafe {
                    let _ = LocalFree(Some(HLOCAL(self.0 .0)));
                }
            }
        }
        let _free = FreeDescriptor(descriptor);
        let security = SECURITY_ATTRIBUTES {
            nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
            lpSecurityDescriptor: descriptor.0,
            bInheritHandle: false.into(),
        };
        let name = vinemix_platform::wide(pipe_name);
        while !engine.stopping() {
            // SAFETY: valid name and security attributes.
            let pipe = unsafe {
                CreateNamedPipeW(
                    PCWSTR(name.as_ptr()),
                    PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
                    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                    1,
                    65536,
                    65536,
                    0,
                    Some(&security),
                )
            };
            if pipe == INVALID_HANDLE_VALUE {
                return Err("Cannot create the local control connection.".into());
            }
            let pipe = Pipe(pipe);
            let event = Event::new(true).map_err(|_| "Cannot create the control event.".to_owned())?;
            let mut operation = OVERLAPPED { hEvent: event.handle(), ..Default::default() };
            // SAFETY: OVERLAPPED outlives the operation (we wait or cancel).
            let connected = unsafe { ConnectNamedPipe(pipe.0, Some(&mut operation)) }.is_ok();
            if !connected {
                // SAFETY: trivial Win32 call.
                let error = unsafe { GetLastError() };
                if error != ERROR_PIPE_CONNECTED
                    && !(error == ERROR_IO_PENDING && complete(pipe.0, &mut operation, engine, &event, None).is_some())
                {
                    continue;
                }
            }
            let mut pending: Vec<u8> = Vec::with_capacity(8192);
            let mut buffer = [0u8; 8192];
            let mut keep_reading = true;
            // When the unfinished request line in `pending` began arriving.
            let mut request_began: Option<Instant> = None;
            while !engine.stopping() && keep_reading {
                // A silent client is bounded by `idle`; a client that started
                // a line must finish it within `request` of its first byte.
                let deadline = match request_began {
                    Some(began) => began + timeouts.request,
                    None => Instant::now() + timeouts.idle,
                };
                let Some(received) = read_chunk(pipe.0, &mut buffer, engine, &event, deadline).filter(|&n| n > 0) else { break };
                pending.extend_from_slice(&buffer[..received as usize]);
                if pending.len() > MAXIMUM_CONTROL_MESSAGE_BYTES {
                    break;
                }
                while let Some(newline) = pending.iter().position(|&b| b == b'\n') {
                    let line: Vec<u8> = pending.drain(..=newline).collect();
                    let (text, shutdown) = respond(engine, &line[..line.len() - 1]);
                    if !write_all(pipe.0, text.as_bytes(), engine, &event, Instant::now() + timeouts.response) {
                        keep_reading = false;
                        break;
                    }
                    if shutdown {
                        engine.request_stop();
                        keep_reading = false;
                        break;
                    }
                }
                request_began = if pending.is_empty() { None } else { request_began.or_else(|| Some(Instant::now())) };
            }
            // SAFETY: cancel outstanding I/O and disconnect the client.
            unsafe {
                let _ = CancelIoEx(pipe.0, None);
                let _ = DisconnectNamedPipe(pipe.0);
            }
            // A client that left in the middle of a drag never sent the
            // configure that saves it.
            engine.flush_logged();
        }
        Ok(())
    }
}
