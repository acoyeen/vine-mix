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

pub const MAXIMUM_MESSAGE_BYTES: usize = 256 * 1024;

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
        }
    }

    /// Setting the returned flag cancels any request in progress and
    /// refuses later ones (`Dispose`).
    pub fn close_handle(&self) -> Arc<AtomicBool> {
        Arc::clone(&self.closed)
    }

    pub fn set_request_timeout(&mut self, timeout: Duration) {
        self.request_timeout = timeout;
    }

    pub fn disconnect(&mut self) {
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
        let mut message = protocol::request(&id, command, configuration, revision);
        if message.len() >= MAXIMUM_MESSAGE_BYTES {
            return Err(ConnectionError::InvalidData("The configuration exceeds the control-message limit.".into()));
        }
        message.push('\n');
        let bytes = message.as_bytes();
        let mut offset = 0usize;
        while offset < bytes.len() {
            let pipe = self.pipe.as_ref().map(|p| p.0).unwrap_or_default();
            let chunk = &bytes[offset..bytes.len().min(offset + 65536)];
            // SAFETY: the buffer outlives the operation (`overlapped` waits or cancels).
            let written = self.overlapped(deadline, |operation, count| unsafe { WriteFile(pipe, Some(chunk), Some(count), Some(operation)) })?;
            if written == 0 {
                return Err(ConnectionError::Io("The audio engine closed its control connection.".into()));
            }
            offset += written as usize;
        }
        let mut line: Vec<u8> = Vec::with_capacity(4096);
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
            let newline = receive[..count].iter().position(|&b| b == b'\n');
            let take = newline.unwrap_or(count);
            if line.len() + take > MAXIMUM_MESSAGE_BYTES {
                return Err(ConnectionError::InvalidData("The engine response exceeds the control-message limit.".into()));
            }
            line.extend_from_slice(&receive[..take]);
            if let Some(newline) = newline {
                if newline + 1 != count {
                    return Err(ConnectionError::InvalidData("The engine sent an unsolicited control message.".into()));
                }
                break;
            }
        }
        let response = protocol::parse_response(&line).map_err(ConnectionError::InvalidData)?;
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
            if !text::eq_ignore_case(&reported, self.physical_settings.as_deref().unwrap_or_default()) {
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
        let closed = Arc::clone(&self.closed);
        let deadline = Deadline { until: Instant::now() + self.request_timeout, closed: &closed, cancel };
        deadline.check()?;
        let result = (|| {
            if self.pipe.is_none() {
                self.connect(&deadline)?;
            }
            if self.expected.is_some() && !self.identity_verified && command != "snapshot" {
                let handshake = self.exchange("snapshot", None, None, &deadline)?;
                if !handshake.ok || handshake.snapshot.is_none() || !self.identity_verified {
                    return Err(ConnectionError::Io("The engine did not verify its workspace before the requested action.".into()));
                }
            }
            self.exchange(command, configuration, revision, &deadline)
        })();
        if result.is_err() {
            self.disconnect();
        }
        result
    }
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
