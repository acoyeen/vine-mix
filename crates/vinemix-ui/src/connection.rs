//! The engine control connection (`EngineConnection`).
//!
//! One request at a time over the per-user pipe. Every new transport proves
//! the engine's identity (release, executable, process, workspace path and
//! physical workspace location) with a read-only snapshot before any
//! configure, recovery or shutdown request.

use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::{Duration, Instant};

use vinemix_model::config::Configuration;
use windows::core::PCWSTR;
use windows::Win32::Foundation::{
    GetLastError, LocalFree, ERROR_IO_PENDING, ERROR_PIPE_BUSY, GENERIC_READ, GENERIC_WRITE, HLOCAL, WAIT_OBJECT_0,
};
use windows::Win32::Security::Authorization::{ConvertSidToStringSidW, GetSecurityInfo, SE_KERNEL_OBJECT};
use windows::Win32::Security::{OWNER_SECURITY_INFORMATION, PSECURITY_DESCRIPTOR, PSID};
use windows::Win32::Storage::FileSystem::{
    CreateFileW, ReadFile, WriteFile, FILE_FLAG_OVERLAPPED, FILE_SHARE_NONE, OPEN_EXISTING,
};
use windows::Win32::System::Pipes::{GetNamedPipeServerProcessId, WaitNamedPipeW};
use windows::Win32::System::Threading::{CreateEventW, ResetEvent, WaitForSingleObject};
use windows::Win32::System::IO::{CancelIoEx, GetOverlappedResult, OVERLAPPED};

use crate::platform::{self, OwnedHandle};
use crate::protocol::{self, Response};
use crate::text;

pub const MAXIMUM_MESSAGE_BYTES: usize = vinemix_model::limits::MAXIMUM_CONTROL_MESSAGE_BYTES;

#[derive(Default)]
struct ReplyFrame {
    bytes: Vec<u8>,
    declared: Option<usize>,
}

impl ReplyFrame {
    fn push(&mut self, chunk: &[u8]) -> Result<bool, ConnectionError> {
        self.bytes.extend_from_slice(chunk);
        if self.declared.is_none() && self.bytes.first() == Some(&b'~') {
            // "~" followed by a decimal byte count and newline. The same
            // ReadFile may also deliver part or all of the reply.
            let header = self.bytes.iter().take(12).position(|&byte| byte == b'\n');
            let Some(end) = header else {
                return if self.bytes.len() < 12 { Ok(false) } else { Err(ConnectionError::InvalidData("Invalid snapshot length header.".into())) };
            };
            let length = std::str::from_utf8(&self.bytes[1..end]).ok()
                .filter(|s| !s.is_empty() && s.bytes().all(|b| b.is_ascii_digit()))
                .and_then(|s| s.parse::<usize>().ok())
                .filter(|n| *n > MAXIMUM_MESSAGE_BYTES && *n <= vinemix_model::limits::MAXIMUM_CONTROL_RESPONSE_BYTES)
                .ok_or_else(|| ConnectionError::InvalidData("Unsupported snapshot length.".into()))?;
            self.declared = Some(length);
            self.bytes.drain(..=end);
        }
        let limit = self.declared.unwrap_or(MAXIMUM_MESSAGE_BYTES);
        if self.bytes.len() > limit + 1 {
            return Err(ConnectionError::InvalidData("The engine response exceeds the control-message limit.".into()));
        }
        // Only inspect the new bytes. Scanning an accumulated large reply
        // after every ReadFile would make transfer quadratic.
        let from = self.bytes.len().saturating_sub(chunk.len());
        if let Some(index) = self.bytes[from..].iter().position(|&byte| byte == b'\n') {
            let size = from + index;
            if size + 1 != self.bytes.len() || self.declared.is_some_and(|expected| size != expected) {
                return Err(ConnectionError::InvalidData("The engine sent an invalid response boundary.".into()));
            }
            self.bytes.pop();
            return Ok(true);
        }
        if self.bytes.len() > limit {
            return Err(ConnectionError::InvalidData("The engine response exceeds its declared length.".into()));
        }
        Ok(false)
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ConnectionError {
    /// The transport failed or the engine refused its identity.
    Io(String),
    /// No response within the request deadline.
    Timeout,
    /// The application is closing.
    Cancelled,
    /// The engine sent something this application cannot use.
    InvalidData(String),
}

impl ConnectionError {
    pub fn message(&self) -> String {
        match self {
            Self::Io(message) | Self::InvalidData(message) => message.clone(),
            Self::Timeout => "Waiting for the engine to respond…".into(),
            Self::Cancelled => "The application is closing.".into(),
        }
    }

    pub fn is_timeout(&self) -> bool {
        matches!(self, Self::Timeout | Self::Cancelled)
    }
}

impl std::fmt::Display for ConnectionError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message())
    }
}

/// Engine identity expectations for the real per-user endpoint. A private
/// verification endpoint passes `None` and skips them.
#[derive(Clone, Debug)]
pub struct Expectations {
    pub engine_path: PathBuf,
    pub settings_path: PathBuf,
    pub release_version: String,
}

#[derive(Debug)]
pub struct Connection {
    pipe_name: String,
    expected: Option<Expectations>,
    pipe: Option<OwnedHandle>,
    event: OwnedHandle,
    identity_verified: bool,
    physical_settings: Option<String>,
    server_pid: u32,
    closed: Arc<AtomicBool>,
    request_timeout: Duration,
    /// The current exchange has put request bytes on the pipe.
    sent_any: bool,
    /// Snapshot parts held from this transport's earlier responses.
    cache: protocol::SnapshotCache,
    /// What the window is showing, sent with each request.
    interest: Option<serde_json::Value>,
    /// Whether the request being sent asks the engine to save.
    persist: bool,
    /// The same, for the next request ([`set_persist`](Self::set_persist)).
    persist_next: bool,
}

/// Remaining time of one request's deadline, honouring cancellation.
struct Deadline<'a> {
    until: Instant,
    closed: &'a AtomicBool,
    cancel: &'a AtomicBool,
}

impl Deadline<'_> {
    fn check(&self) -> Result<Duration, ConnectionError> {
        if self.closed.load(Ordering::Acquire) || self.cancel.load(Ordering::Acquire) {
            return Err(ConnectionError::Cancelled);
        }
        let now = Instant::now();
        if now >= self.until {
            return Err(ConnectionError::Timeout);
        }
        Ok(self.until - now)
    }
}

fn owner_sid(handle: &OwnedHandle) -> Option<String> {
    let mut owner = PSID::default();
    let mut descriptor = PSECURITY_DESCRIPTOR::default();
    // SAFETY: valid handle; the descriptor is freed below and `owner` points into it.
    let status = unsafe {
        GetSecurityInfo(
            handle.0,
            SE_KERNEL_OBJECT,
            OWNER_SECURITY_INFORMATION,
            Some(&mut owner),
            None,
            None,
            None,
            Some(&mut descriptor),
        )
    };
    if status.is_err() {
        return None;
    }
    let mut text = windows::core::PWSTR::null();
    // SAFETY: `owner` is valid while `descriptor` lives.
    let converted = unsafe { ConvertSidToStringSidW(owner, &mut text) }.ok().and_then(|()| {
        // SAFETY: allocated by ConvertSidToStringSidW.
        let value = unsafe { text.to_string() }.ok();
        // SAFETY: as above.
        unsafe {
            let _ = LocalFree(Some(HLOCAL(text.0.cast())));
        }
        value
    });
    // SAFETY: allocated by GetSecurityInfo.
    unsafe {
        let _ = LocalFree(Some(HLOCAL(descriptor.0)));
    }
    converted
}

impl Connection {
    /// The user's real engine endpoint.
    pub fn new(expected: Expectations) -> Result<Self, String> {
        Ok(Self::with_pipe(vinemix_platform::pipe_name()?, Some(expected)))
    }

    /// A private endpoint (tests). Identity checks are skipped.
    pub fn with_pipe(pipe_name: String, expected: Option<Expectations>) -> Self {
        // SAFETY: anonymous manual-reset event; owned below.
        let event = unsafe { CreateEventW(None, true, false, PCWSTR::null()) }.unwrap_or_default();
        Self {
            pipe_name,
            expected,
            pipe: None,
            event: OwnedHandle(event),
            identity_verified: false,
            physical_settings: None,
            server_pid: 0,
            closed: Arc::new(AtomicBool::new(false)),
            request_timeout: Duration::from_secs(3),
            sent_any: false,
            cache: protocol::SnapshotCache::default(),
            interest: None,
            persist: true,
            persist_next: true,
        }
    }

    /// Setting the returned flag cancels any request in progress and
    /// refuses later ones (`Dispose`).
    pub fn close_handle(&self) -> Arc<AtomicBool> {
        Arc::clone(&self.closed)
    }

    /// What the window currently shows (`None`: everything, the default).
    pub fn set_interest(&mut self, interest: Option<serde_json::Value>) {
        self.interest = interest;
    }

    /// Whether the next request, and only that one, asks the engine to write
    /// the settings file. False while a control is being dragged.
    pub fn set_persist(&mut self, persist: bool) {
        self.persist_next = persist;
    }

    pub fn set_request_timeout(&mut self, timeout: Duration) {
        self.request_timeout = timeout;
    }

    pub fn disconnect(&mut self) {
        // A new transport may reach a different engine: start from a full
        // snapshot again.
        self.cache.clear();
        self.identity_verified = false;
        self.physical_settings = None;
        self.pipe = None;
    }

    fn connect(&mut self, deadline: &Deadline<'_>) -> Result<(), ConnectionError> {
        self.disconnect();
        let name = platform::wide(&self.pipe_name);
        // .NET ConnectAsync(500): keep trying an absent or busy pipe for 500 ms.
        let connect_until = Instant::now() + Duration::from_millis(500);
        let handle = loop {
            deadline.check()?;
            // SAFETY: valid name; the handle is owned below.
            match unsafe {
                CreateFileW(
                    PCWSTR(name.as_ptr()),
                    (GENERIC_READ | GENERIC_WRITE).0,
                    FILE_SHARE_NONE,
                    None,
                    OPEN_EXISTING,
                    FILE_FLAG_OVERLAPPED,
                    None,
                )
            } {
                Ok(handle) => break OwnedHandle(handle),
                Err(error) => {
                    if Instant::now() >= connect_until {
                        return Err(ConnectionError::Timeout);
                    }
                    if error.code() == ERROR_PIPE_BUSY.to_hresult() {
                        // SAFETY: valid name.
                        let _ = unsafe { WaitNamedPipeW(PCWSTR(name.as_ptr()), 100) };
                    } else {
                        std::thread::sleep(Duration::from_millis(25));
                    }
                }
            }
        };
        // PipeOptions.CurrentUserOnly: the server must belong to this user.
        let user = vinemix_platform::current_user_sid().map_err(ConnectionError::Io)?;
        if owner_sid(&handle).as_deref() != Some(user.as_str()) {
            return Err(ConnectionError::Io("The audio engine connection is not owned by this Windows user.".into()));
        }
        if let Some(expected) = &self.expected {
            let mut pid = 0u32;
            // SAFETY: valid pipe handle.
            unsafe { GetNamedPipeServerProcessId(handle.0, &mut pid) }
                .map_err(|_| ConnectionError::Io("The audio engine owner could not be verified.".into()))?;
            self.server_pid = pid;
            let process = platform::Process::open(pid).ok_or_else(|| ConnectionError::Io("The audio engine owner could not be verified.".into()))?;
            let same_session = process.session() == Some(platform::current_session_id());
            let same_path = process.image_path().is_some_and(|p| text::same_path(&p, &expected.engine_path));
            if !same_session || !same_path {
                return Err(ConnectionError::Io("Another VineMix version owns this connection. Quit that version before switching.".into()));
            }
        }
        self.pipe = Some(handle);
        Ok(())
    }

    fn overlapped<F>(&self, deadline: &Deadline<'_>, start: F) -> Result<u32, ConnectionError>
    where
        F: FnOnce(&mut OVERLAPPED, &mut u32) -> windows::core::Result<()>,
    {
        let pipe = self.pipe.as_ref().ok_or_else(|| ConnectionError::Io("The audio engine is not connected.".into()))?;
        let mut operation = OVERLAPPED { hEvent: self.event.0, ..Default::default() };
        // SAFETY: valid event.
        unsafe {
            let _ = ResetEvent(self.event.0);
        }
        let mut count = 0u32;
        match start(&mut operation, &mut count) {
            Ok(()) => return Ok(count),
            // SAFETY: trivial Win32 call.
            Err(_) if unsafe { GetLastError() } == ERROR_IO_PENDING => {}
            Err(error) => return Err(ConnectionError::Io(format!("The audio engine connection failed. {}", error.message()))),
        }
        loop {
            let remaining = match deadline.check() {
                Ok(remaining) => remaining,
                Err(error) => {
                    // Cancel, then wait for completion so the OVERLAPPED and
                    // buffer are released before returning.
                    let mut bytes = 0u32;
                    // SAFETY: the operation belongs to this pipe and is still alive.
                    unsafe {
                        let _ = CancelIoEx(pipe.0, Some(&operation));
                        let _ = GetOverlappedResult(pipe.0, &operation, &mut bytes, true);
                    }
                    return Err(error);
                }
            };
            let slice = u32::try_from(remaining.as_millis().min(50)).unwrap_or(50);
            // SAFETY: valid event.
            if unsafe { WaitForSingleObject(self.event.0, slice) } == WAIT_OBJECT_0 {
                let mut bytes = 0u32;
                // SAFETY: the operation completed.
                return unsafe { GetOverlappedResult(pipe.0, &operation, &mut bytes, false) }
                    .map(|()| bytes)
                    .map_err(|e| ConnectionError::Io(format!("The audio engine connection failed. {}", e.message())));
            }
        }
    }

    fn exchange(
        &mut self,
        command: &str,
        configuration: Option<&Configuration>,
        revision: Option<u64>,
        deadline: &Deadline<'_>,
    ) -> Result<Response, ConnectionError> {
        let id = platform::random_hex_id();
        let mut message = protocol::request_with(&id, command, configuration, revision, self.cache.known(), self.interest.as_ref(), self.persist);
        if message.len() >= MAXIMUM_MESSAGE_BYTES {
            return Err(ConnectionError::InvalidData("The configuration exceeds the control-message limit.".into()));
        }
        message.push('\n');
        let bytes = message.as_bytes();
        self.sent_any = false;
        let mut offset = 0usize;
        while offset < bytes.len() {
            let pipe = self.pipe.as_ref().map(|p| p.0).unwrap_or_default();
            let chunk = &bytes[offset..bytes.len().min(offset + 65536)];
            // SAFETY: the buffer outlives the operation (`overlapped` waits or cancels).
            let written = self.overlapped(deadline, |operation, count| unsafe { WriteFile(pipe, Some(chunk), Some(count), Some(operation)) })?;
            if written == 0 {
                return Err(ConnectionError::Io("The audio engine closed its control connection.".into()));
            }
            self.sent_any = true;
            offset += written as usize;
        }
        let mut frame = ReplyFrame::default();
        let mut receive = [0u8; 4096];
        loop {
            let pipe = self.pipe.as_ref().map(|p| p.0).unwrap_or_default();
            let buffer = &mut receive;
            // SAFETY: as above.
            let count = self.overlapped(deadline, |operation, count| unsafe { ReadFile(pipe, Some(buffer), Some(count), Some(operation)) })?
                as usize;
            if count == 0 {
                return Err(ConnectionError::Io("The audio engine closed its control connection.".into()));
            }
            if frame.push(&receive[..count])? {
                break;
            }
        }
        let response = protocol::parse_response_cached(&frame.bytes, &mut self.cache).map_err(ConnectionError::InvalidData)?;
        if response.protocol_version != 1 || response.request_id != id {
            return Err(ConnectionError::InvalidData("The engine control protocol did not match this application.".into()));
        }
        if let (Some(expected), Some(snapshot)) = (&self.expected, &response.snapshot) {
            if !text::same_path(&crate::paths::absolute(std::path::Path::new(&snapshot.settings_path)), &crate::paths::absolute(&expected.settings_path)) {
                return Err(ConnectionError::Io(
                    "The engine uses a different workspace. Quit the current session before opening another settings file.".into(),
                ));
            }
            if snapshot.engine_protocol_version != 1
                || snapshot.engine_version != expected.release_version
                || snapshot.engine_pid != self.server_pid
                || !text::same_path(std::path::Path::new(&snapshot.executable_path), &expected.engine_path)
            {
                return Err(ConnectionError::Io("The audio engine does not match this VineMix release.".into()));
            }
            if self.physical_settings.is_none() {
                let physical = vinemix_platform::physical_path(&expected.settings_path).map_err(ConnectionError::Io)?;
                self.physical_settings = Some(physical.to_string_lossy().into_owned());
            }
            let reported = normalize_physical(&snapshot.physical_settings_path);
            if !verified_physical_match(&reported, self.physical_settings.as_deref()) {
                return Err(ConnectionError::Io(
                    "The engine opened a different physical settings location. Saving has been blocked to protect your workspace.".into(),
                ));
            }
            self.identity_verified = true;
            crate::supervisor::confirm_engine_identity(snapshot.engine_pid);
        }
        Ok(response)
    }

    /// One request/response exchange with a three-second deadline. `cancel`
    /// ends it early (the application is closing).
    pub fn request(
        &mut self,
        command: &str,
        configuration: Option<&Configuration>,
        revision: Option<u64>,
        cancel: &AtomicBool,
    ) -> Result<Response, ConnectionError> {
        // One request only, sent or not: the next configure saves unless it
        // is told otherwise again.
        self.persist = std::mem::replace(&mut self.persist_next, true);
        let closed = Arc::clone(&self.closed);
        let deadline = Deadline { until: Instant::now() + self.request_timeout, closed: &closed, cancel };
        deadline.check()?;
        let reused = self.pipe.is_some();
        let mut result = self.attempt(command, configuration, revision, &deadline);
        // The engine releases a connection that has been idle (it serves one
        // client at a time). If a kept connection fails before a single
        // request byte was accepted, nothing reached the engine, so the same
        // request is safe to send once on a fresh connection.
        if reused && !self.sent_any && matches!(result, Err(ConnectionError::Io(_))) {
            self.disconnect();
            result = self.attempt(command, configuration, revision, &deadline);
        }
        if result.is_err() {
            self.disconnect();
        }
        result
    }

    fn attempt(
        &mut self,
        command: &str,
        configuration: Option<&Configuration>,
        revision: Option<u64>,
        deadline: &Deadline<'_>,
    ) -> Result<Response, ConnectionError> {
        self.sent_any = false;
        if self.pipe.is_none() {
            self.connect(deadline)?;
        }
        if self.expected.is_some() && !self.identity_verified && command != "snapshot" {
            let handshake = self.exchange("snapshot", None, None, deadline)?;
            if !handshake.ok || handshake.snapshot.is_none() || !self.identity_verified {
                return Err(ConnectionError::Io("The engine did not verify its workspace before the requested action.".into()));
            }
        }
        self.exchange(command, configuration, revision, deadline)
    }
}

fn verified_physical_match(reported: &str, local: Option<&str>) -> bool {
    !reported.is_empty() && local.is_some_and(|expected| text::eq_ignore_case(reported, expected))
}

pub(crate) fn normalize_physical(path: &str) -> String {
    if let Some(rest) = path.strip_prefix(r"\\?\UNC\") {
        format!(r"\\{rest}")
    } else if let Some(rest) = path.strip_prefix(r"\\?\") {
        rest.to_owned()
    } else {
        path.to_owned()
    }
}

#[cfg(test)]
mod reply_frame_tests {
    use super::*;

    #[test]
    fn unresolved_remote_path_cannot_match_a_verified_desktop() {
        assert!(!verified_physical_match("", Some(r"C:\VineMix\settings.json")));
        assert!(!verified_physical_match("", None));
        assert!(verified_physical_match(r"c:\vinemix\settings.json", Some(r"C:\VineMix\settings.json")));
    }

    #[test]
    fn extended_response_is_bounded_and_can_split_mid_header_or_body() {
        let body = "x".repeat(MAXIMUM_MESSAGE_BYTES + 123);
        let wire = format!("~{}\n{body}\n", body.len());
        let mut frame = ReplyFrame::default();
        assert!(!frame.push(&wire.as_bytes()[..2]).unwrap());
        let mut complete = false;
        for chunk in wire.as_bytes()[2..].chunks(237) {
            assert!(!complete);
            complete = frame.push(chunk).unwrap();
        }
        assert!(complete);
        assert_eq!(frame.bytes, body.as_bytes());
        assert!(ReplyFrame::default().push(b"~999999999\n").is_err());
        assert!(ReplyFrame::default().push(b"~300000\n{}\n").is_err());
        assert!(ReplyFrame::default().push(b"{}\nextra").is_err());
        assert!(ReplyFrame::default().push(b"{}\n").unwrap());
    }
}
