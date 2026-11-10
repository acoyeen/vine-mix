//! HTTPS GET through WinHTTP with the updater's redirect policy
//! (`UpdateHttp`): API requests stay on api.github.com; assets start on
//! github.com and may only redirect (at most three times) to GitHub's
//! release-asset CDN. Cookies and automatic authentication are disabled.
//!
//! WinHTTP is used synchronously, on a transport thread per request. The
//! caller waits for headers and body chunks with its cancellation callback
//! and one deadline for the whole exchange; on either it closes the request
//! handle, which ends a blocked send, receive or read instead of leaving it
//! to a socket timeout.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, SyncSender};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use windows::core::PCWSTR;
use windows::Win32::Networking::WinHttp::{
    WinHttpCloseHandle, WinHttpConnect, WinHttpOpen, WinHttpOpenRequest, WinHttpQueryHeaders, WinHttpReadData, WinHttpReceiveResponse,
    WinHttpSendRequest, WinHttpSetOption, WinHttpSetTimeouts, WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_ACCESS_TYPE_NO_PROXY, WINHTTP_DISABLE_AUTHENTICATION,
    WINHTTP_DISABLE_COOKIES, WINHTTP_FLAG_SECURE, WINHTTP_OPEN_REQUEST_FLAGS, WINHTTP_OPTION_DISABLE_FEATURE, WINHTTP_OPTION_REDIRECT_POLICY,
    WINHTTP_OPTION_REDIRECT_POLICY_NEVER, WINHTTP_QUERY_CONTENT_LENGTH, WINHTTP_QUERY_CUSTOM, WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_QUERY_LOCATION,
    WINHTTP_QUERY_RETRY_AFTER, WINHTTP_QUERY_STATUS_CODE,
};

use crate::Failure;

fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

struct Handle(*mut std::ffi::c_void);

impl Drop for Handle {
    fn drop(&mut self) {
        if !self.0.is_null() {
            // SAFETY: owned WinHTTP handle.
            unsafe {
                let _ = WinHttpCloseHandle(self.0);
            }
        }
    }
}

// SAFETY: WinHTTP handles may be used from any thread.
unsafe impl Send for Handle {}

/// A parsed `https://host/path` URL on port 443 without credentials.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Url {
    pub host: String,
    pub path: String,
}

impl Url {
    pub fn parse(text: &str) -> Option<Self> {
        let rest = text.strip_prefix("https://")?;
        let (authority, path) = match rest.find('/') {
            Some(index) => (&rest[..index], &rest[index..]),
            None => (rest, "/"),
        };
        if authority.is_empty() || authority.contains('@') || path.contains('#') {
            return None;
        }
        let host = match authority.split_once(':') {
            Some((host, "443")) => host,
            Some(_) => return None,
            None => authority,
        };
        if host.is_empty() || !host.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'.' || b == b'-') {
            return None;
        }
        Some(Self { host: host.to_ascii_lowercase(), path: path.to_owned() })
    }

    pub fn join(&self, location: &str) -> Option<Self> {
        if location.starts_with("https://") || location.starts_with("http://") {
            Self::parse(location)
        } else if location.starts_with('/') {
            Some(Self { host: self.host.clone(), path: location.to_owned() })
        } else {
            None
        }
    }

    pub fn to_text(&self) -> String {
        format!("https://{}{}", self.host, self.path)
    }
}

fn is_cdn(host: &str) -> bool {
    matches!(host, "release-assets.githubusercontent.com" | "objects.githubusercontent.com")
}

fn is_asset_host(host: &str) -> bool {
    host == "github.com" || is_cdn(host)
}

pub struct Response {
    transport: Transport,
    pub status: u32,
    pub content_length: Option<u64>,
    pub retry_after: bool,
    pub rate_limit_exhausted: bool,
    location: Option<String>,
    deadline: Instant,
}

impl std::fmt::Debug for Response {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Response").field("status", &self.status).finish_non_exhaustive()
    }
}

fn query_number(request: *mut std::ffi::c_void, info: u32) -> Option<u32> {
    let mut value = 0u32;
    let mut size = 4u32;
    // SAFETY: valid request; 4-byte output for numeric queries.
    unsafe { WinHttpQueryHeaders(request, info | WINHTTP_QUERY_FLAG_NUMBER, PCWSTR::null(), Some(std::ptr::from_mut(&mut value).cast()), &mut size, std::ptr::null_mut()) }
        .ok()
        .map(|()| value)
}

fn query_text(request: *mut std::ffi::c_void, info: u32, name: Option<&str>) -> Option<String> {
    let name = name.map(wide);
    let name_ptr = name.as_ref().map_or(PCWSTR::null(), |n| PCWSTR(n.as_ptr()));
    let mut buffer = vec![0u16; 4096];
    let mut size = (buffer.len() * 2) as u32;
    // SAFETY: valid request and buffer.
    unsafe { WinHttpQueryHeaders(request, info, name_ptr, Some(buffer.as_mut_ptr().cast()), &mut size, std::ptr::null_mut()) }.ok()?;
    Some(String::from_utf16_lossy(&buffer[..(size as usize / 2).min(buffer.len())]))
}

fn connection_error(error: &windows::core::Error) -> Failure {
    Failure::Unavailable(format!("The update service could not be reached. Check your internet connection and try again. ({})", error.code().0))
}

fn timed_out() -> Failure {
    Failure::Unavailable("The update service did not respond in time.".into())
}

/// Where one request goes. Production requests are always HTTPS on 443;
/// tests point the same transport at a local plain-HTTP listener.
#[derive(Clone, Debug)]
struct Target {
    host: String,
    port: u16,
    secure: bool,
    path: String,
}

impl Target {
    fn of(url: &Url) -> Self {
        Self { host: url.host.clone(), port: 443, secure: true, path: url.path.clone() }
    }
}

/// The request handle shared between the transport thread (which blocks in
/// WinHTTP) and the caller (which may abort it). WinHTTP documents closing a
/// request handle from another thread as the way to end an in-progress
/// synchronous operation; the handle is closed exactly once, by whoever takes
/// it out of the slot.
#[derive(Default)]
struct Slot {
    request: Mutex<Option<Handle>>,
    aborted: AtomicBool,
}

impl Slot {
    fn raw(&self) -> Option<*mut std::ffi::c_void> {
        if self.aborted.load(Ordering::Acquire) {
            return None;
        }
        self.request.lock().unwrap_or_else(std::sync::PoisonError::into_inner).as_ref().map(|h| h.0)
    }

    /// Ends any blocked send, receive or read and prevents further use.
    fn abort(&self) {
        self.aborted.store(true, Ordering::Release);
        let handle = self.request.lock().unwrap_or_else(std::sync::PoisonError::into_inner).take();
        drop(handle);
    }
}

struct Head {
    status: u32,
    content_length: Option<u64>,
    retry_after: bool,
    rate_limit_exhausted: bool,
    location: Option<String>,
}

enum Message {
    Head(Head),
    Chunk(Vec<u8>),
    End,
    Failed(Failure),
}

/// A request running on its own thread. Dropping it aborts the request.
struct Transport {
    slot: Arc<Slot>,
    messages: Receiver<Message>,
}

impl Drop for Transport {
    fn drop(&mut self) {
        self.slot.abort();
    }
}

/// How often a waiting caller re-checks cancellation.
const POLL: Duration = Duration::from_millis(25);

impl Transport {
    fn start(target: Target, asset: bool, deadline: Instant) -> Result<Self, Failure> {
        let slot = Arc::new(Slot::default());
        // A small bound gives the reader back-pressure without stalling it.
        let (sender, messages) = std::sync::mpsc::sync_channel(4);
        let worker = slot.clone();
        std::thread::Builder::new()
            .name("vinemix-update-http".into())
            .spawn(move || {
                if let Err(failure) = run(&target, asset, deadline, &worker, &sender) {
                    let _ = sender.send(Message::Failed(failure));
                }
                // The request (if still present) closes before its parents.
                worker.abort();
            })
            .map_err(|_| Failure::Unavailable("The update service could not be reached.".into()))?;
        Ok(Self { slot, messages })
    }

    /// The next message, or an error as soon as the caller cancels or the
    /// shared deadline passes. Either way the blocked transport is aborted,
    /// not merely abandoned.
    fn next(&self, deadline: Instant, cancelled: &dyn Fn() -> bool) -> Result<Message, Failure> {
        loop {
            if cancelled() {
                self.slot.abort();
                return Err(Failure::Cancelled);
            }
            let remaining = deadline.saturating_duration_since(Instant::now());
            if remaining.is_zero() {
                self.slot.abort();
                return Err(timed_out());
            }
            match self.messages.recv_timeout(remaining.min(POLL)) {
                Ok(Message::Failed(failure)) => return Err(failure),
                Ok(message) => return Ok(message),
                Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {}
                Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => {
                    return Err(Failure::Unavailable("The update response was interrupted. Try again.".into()));
                }
            }
        }
    }
}

/// The transport thread: one request, headers first, then the body in chunks.
fn run(target: &Target, asset: bool, deadline: Instant, slot: &Slot, sender: &SyncSender<Message>) -> Result<(), Failure> {
    let unreachable = || Failure::Unavailable("The update service could not be reached.".into());
    let remaining = deadline.saturating_duration_since(Instant::now());
    if remaining.is_zero() {
        return Err(timed_out());
    }
    // No single socket operation may outlast the time left for the whole
    // request (the caller enforces the total separately).
    let timeout = i32::try_from(remaining.as_millis().clamp(1, 60_000)).unwrap_or(60_000);
    let agent = wide("VineMix-Updater/1.0");
    let access = if target.secure { WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY } else { WINHTTP_ACCESS_TYPE_NO_PROXY };
    // SAFETY: valid strings; handles closed by `Handle`.
    let session = Handle(unsafe { WinHttpOpen(PCWSTR(agent.as_ptr()), access, PCWSTR::null(), PCWSTR::null(), 0) });
    if session.0.is_null() {
        return Err(unreachable());
    }
    // SAFETY: valid session.
    unsafe {
        let _ = WinHttpSetTimeouts(session.0, timeout, timeout, timeout, timeout);
    }
    let host = wide(&target.host);
    // SAFETY: valid session and host.
    let connection = Handle(unsafe { WinHttpConnect(session.0, PCWSTR(host.as_ptr()), target.port, 0) });
    if connection.0.is_null() {
        return Err(unreachable());
    }
    let verb = wide("GET");
    let path = wide(&target.path);
    let flags = if target.secure { WINHTTP_FLAG_SECURE.0 } else { 0 };
    // SAFETY: valid connection and strings.
    let request = Handle(unsafe {
        WinHttpOpenRequest(connection.0, PCWSTR(verb.as_ptr()), PCWSTR(path.as_ptr()), PCWSTR::null(), PCWSTR::null(), std::ptr::null(), WINHTTP_OPEN_REQUEST_FLAGS(flags))
    });
    if request.0.is_null() {
        return Err(unreachable());
    }
    let never = WINHTTP_OPTION_REDIRECT_POLICY_NEVER;
    let features = WINHTTP_DISABLE_COOKIES | WINHTTP_DISABLE_AUTHENTICATION;
    // SAFETY: valid request; option buffers are 4-byte values.
    unsafe {
        let _ = WinHttpSetOption(Some(request.0), WINHTTP_OPTION_REDIRECT_POLICY, Some(&never.to_ne_bytes()));
        let _ = WinHttpSetOption(Some(request.0), WINHTTP_OPTION_DISABLE_FEATURE, Some(&features.to_ne_bytes()));
    }
    // From here the caller can abort the request at any time.
    *slot.request.lock().unwrap_or_else(std::sync::PoisonError::into_inner) = Some(request);
    let stopped = || Failure::Cancelled;
    let headers = if asset {
        "Accept: application/octet-stream\r\n".to_owned()
    } else {
        "Accept: application/vnd.github+json\r\nX-GitHub-Api-Version: 2022-11-28\r\n".to_owned()
    };
    let headers = wide(&headers);
    let request = slot.raw().ok_or_else(stopped)?;
    // SAFETY: valid request and header buffer (length excludes the NUL).
    unsafe { WinHttpSendRequest(request, Some(&headers[..headers.len() - 1]), None, 0, 0, 0) }.map_err(|e| connection_error(&e))?;
    let request = slot.raw().ok_or_else(stopped)?;
    // SAFETY: valid request.
    unsafe { WinHttpReceiveResponse(request, std::ptr::null_mut()) }.map_err(|e| connection_error(&e))?;
    let request = slot.raw().ok_or_else(stopped)?;
    let head = Head {
        status: query_number(request, WINHTTP_QUERY_STATUS_CODE).unwrap_or(0),
        content_length: query_text(request, WINHTTP_QUERY_CONTENT_LENGTH, None).and_then(|t| t.trim().parse().ok()),
        retry_after: query_text(request, WINHTTP_QUERY_RETRY_AFTER, None).is_some(),
        rate_limit_exhausted: query_text(request, WINHTTP_QUERY_CUSTOM, Some("X-RateLimit-Remaining")).is_some_and(|v| v.trim() == "0"),
        location: query_text(request, WINHTTP_QUERY_LOCATION, None),
    };
    if sender.send(Message::Head(head)).is_err() {
        return Ok(());
    }
    let mut buffer = vec![0u8; 65536];
    loop {
        let request = slot.raw().ok_or_else(stopped)?;
        let mut read = 0u32;
        // SAFETY: valid request and buffer.
        unsafe { WinHttpReadData(request, buffer.as_mut_ptr().cast(), buffer.len() as u32, &mut read) }
            .map_err(|_| Failure::Unavailable("The update response was interrupted. Try again.".into()))?;
        let message = if read == 0 { Message::End } else { Message::Chunk(buffer[..read as usize].to_vec()) };
        let done = read == 0;
        if sender.send(message).is_err() || done {
            return Ok(());
        }
    }
}

/// One request without following redirects.
fn send(target: Target, asset: bool, deadline: Instant, cancelled: &dyn Fn() -> bool) -> Result<Response, Failure> {
    if cancelled() {
        return Err(Failure::Cancelled);
    }
    if deadline.saturating_duration_since(Instant::now()).is_zero() {
        return Err(timed_out());
    }
    let transport = Transport::start(target, asset, deadline)?;
    match transport.next(deadline, cancelled)? {
        Message::Head(head) => Ok(Response {
            transport,
            status: head.status,
            content_length: head.content_length,
            retry_after: head.retry_after,
            rate_limit_exhausted: head.rate_limit_exhausted,
            location: head.location,
            deadline,
        }),
        _ => Err(Failure::Unavailable("The update response was interrupted. Try again.".into())),
    }
}

/// GET with the updater's origin and redirect policy. `timeout` bounds the
/// whole exchange — every redirect and the body read share one deadline —
/// and `cancelled` is observed throughout, including while WinHTTP is
/// blocked waiting for the network.
pub fn get(url: &str, asset: bool, timeout: Duration, cancelled: &dyn Fn() -> bool) -> Result<Response, Failure> {
    let deadline = Instant::now() + timeout;
    let mut current = Url::parse(url).ok_or_else(|| Failure::Invalid("The update download redirected outside its trusted HTTPS origins.".into()))?;
    for redirect in 0..=3 {
        if if asset { !is_asset_host(&current.host) } else { current.host != "api.github.com" } {
            return Err(Failure::Invalid("The update download redirected outside its trusted HTTPS origins.".into()));
        }
        let response = send(Target::of(&current), asset, deadline, cancelled)?;
        if (300..=399).contains(&response.status) {
            let Some(location) = response.location.clone().filter(|_| asset && redirect < 3) else {
                return Err(Failure::Invalid("The update service returned an unexpected redirect.".into()));
            };
            let next = current.join(location.trim()).ok_or_else(|| Failure::Invalid("The update download redirected outside GitHub's release asset CDN.".into()))?;
            if !is_cdn(&next.host) || (is_cdn(&current.host) && next.host != current.host) {
                return Err(Failure::Invalid("The update download redirected outside GitHub's release asset CDN.".into()));
            }
            current = next;
            continue;
        }
        return Ok(response);
    }
    Err(Failure::Invalid("The update service returned too many redirects.".into()))
}

impl Response {
    pub fn success(&self) -> bool {
        (200..=299).contains(&self.status)
    }

    /// Reads the body in chunks into `sink`, refusing more than `maximum`.
    /// Cancellation and the request's deadline end a stalled read.
    pub fn read_with(&self, maximum: u64, cancelled: &dyn Fn() -> bool, mut sink: impl FnMut(&[u8]) -> Result<(), Failure>) -> Result<u64, Failure> {
        if self.content_length.is_some_and(|l| l > maximum) {
            return Err(Failure::Invalid("The update response exceeded its size limit.".into()));
        }
        let mut total = 0u64;
        loop {
            let chunk = match self.transport.next(self.deadline, cancelled)? {
                Message::Chunk(chunk) => chunk,
                Message::End => break,
                _ => return Err(Failure::Unavailable("The update response was interrupted. Try again.".into())),
            };
            total += chunk.len() as u64;
            if total > maximum {
                return Err(Failure::Invalid("The update response exceeded its size limit.".into()));
            }
            sink(&chunk)?;
        }
        if self.content_length.is_some_and(|l| l != total) {
            return Err(Failure::Invalid("The update response was interrupted or has the wrong length.".into()));
        }
        Ok(total)
    }

    pub fn read_bounded(&self, maximum: u64, cancelled: &dyn Fn() -> bool) -> Result<Vec<u8>, Failure> {
        let mut body = Vec::new();
        self.read_with(maximum, cancelled, |chunk| {
            body.extend_from_slice(chunk);
            Ok(())
        })?;
        Ok(body)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::{Read, Write};
    use std::net::{TcpListener, TcpStream};
    use std::sync::mpsc;

    #[test]
    fn url_policy() {
        assert_eq!(Url::parse("https://api.github.com/repos/x?y=1").unwrap().path, "/repos/x?y=1");
        assert!(Url::parse("http://api.github.com/").is_none());
        assert!(Url::parse("https://user@github.com/").is_none());
        assert!(Url::parse("https://github.com:8443/").is_none());
        assert!(Url::parse("https://github.com/a#b").is_none());
        let base = Url::parse("https://github.com/a").unwrap();
        assert_eq!(base.join("/b").unwrap().to_text(), "https://github.com/b");
    }

    /// What the local server does with each connection after the request
    /// headers arrive.
    #[derive(Clone, Copy)]
    enum Behaviour {
        /// Never answer (stalled before response headers).
        Silent,
        /// Headers promising 1000 bytes, 100 bytes of body, then nothing.
        PartialBody,
        /// A complete 1000-byte response.
        Complete,
    }

    /// A loopback HTTP listener. `closed` reports when the client's side of
    /// each connection goes away, which is how the tests tell an aborted
    /// transport from one that was merely abandoned.
    fn server(behaviour: Behaviour) -> (Target, mpsc::Receiver<Instant>) {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let (closed, observed) = mpsc::channel();
        std::thread::spawn(move || {
            for stream in listener.incoming() {
                let Ok(mut stream) = stream else { return };
                let closed = closed.clone();
                std::thread::spawn(move || {
                    let _ = read_request(&mut stream);
                    match behaviour {
                        Behaviour::Silent => {}
                        Behaviour::PartialBody => {
                            let _ = stream.write_all(b"HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n");
                            let _ = stream.write_all(&[7u8; 100]);
                            let _ = stream.flush();
                        }
                        Behaviour::Complete => {
                            let _ = stream.write_all(b"HTTP/1.1 200 OK\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n");
                            let _ = stream.write_all(&[7u8; 1000]);
                            let _ = stream.flush();
                        }
                    }
                    // Block until the client closes or resets the connection.
                    let mut sink = [0u8; 256];
                    while matches!(stream.read(&mut sink), Ok(n) if n > 0) {}
                    let _ = closed.send(Instant::now());
                });
            }
        });
        (Target { host: "127.0.0.1".into(), port, secure: false, path: "/example".into() }, observed)
    }

    fn read_request(stream: &mut TcpStream) -> std::io::Result<()> {
        let mut seen = Vec::new();
        let mut byte = [0u8; 1];
        while !seen.ends_with(b"\r\n\r\n") {
            if stream.read(&mut byte)? == 0 {
                break;
            }
            seen.push(byte[0]);
        }
        Ok(())
    }

    fn never() -> bool {
        false
    }

    const LONG: Duration = Duration::from_secs(60);
    /// Generous bound for "promptly" on a loaded test machine; the stalled
    /// operations themselves would otherwise last the full socket timeout.
    const PROMPT: Duration = Duration::from_secs(3);

    #[test]
    fn complete_response_is_read_normally() {
        let (target, _closed) = server(Behaviour::Complete);
        let response = send(target, false, Instant::now() + LONG, &never).unwrap();
        assert!(response.success());
        assert_eq!(response.content_length, Some(1000));
        assert_eq!(response.read_bounded(4096, &never).unwrap(), vec![7u8; 1000]);
    }

    #[test]
    fn cancel_interrupts_a_request_stalled_before_headers() {
        let (target, closed) = server(Behaviour::Silent);
        let started = Instant::now();
        let cancel_at = started + Duration::from_millis(300);
        let result = send(target, false, started + LONG, &|| Instant::now() >= cancel_at);
        let returned = started.elapsed();
        assert_eq!(result.unwrap_err(), Failure::Cancelled);
        assert!(returned >= Duration::from_millis(300) && returned < PROMPT, "cancel returned after {returned:?}");
        // The transport itself stopped: the server sees the connection go away.
        let observed = closed.recv_timeout(PROMPT).expect("the stalled connection is closed, not left to its socket timeout");
        assert!(observed.duration_since(started) < PROMPT);
    }

    #[test]
    fn total_deadline_bounds_a_request_stalled_before_headers() {
        let (target, closed) = server(Behaviour::Silent);
        let started = Instant::now();
        let result = send(target, false, started + Duration::from_millis(400), &never);
        let returned = started.elapsed();
        assert!(matches!(result, Err(Failure::Unavailable(ref text)) if text.contains("did not respond in time")), "{result:?}");
        assert!(returned >= Duration::from_millis(400) && returned < PROMPT, "deadline returned after {returned:?}");
        closed.recv_timeout(PROMPT).expect("the timed-out connection is closed");
    }

    #[test]
    fn cancel_interrupts_a_stalled_body_read() {
        let (target, closed) = server(Behaviour::PartialBody);
        let started = Instant::now();
        let response = send(target, true, started + LONG, &never).unwrap();
        assert_eq!(response.content_length, Some(1000));
        let cancel_at = Instant::now() + Duration::from_millis(300);
        let mut received = 0usize;
        let result = response.read_with(4096, &|| Instant::now() >= cancel_at, |chunk| {
            received += chunk.len();
            Ok(())
        });
        assert_eq!(result.unwrap_err(), Failure::Cancelled);
        // WinHTTP may hold a partial buffer while it waits for more data.
        assert!(received <= 100, "only bytes the server sent can be delivered: {received}");
        assert!(started.elapsed() < PROMPT, "cancel returned after {:?}", started.elapsed());
        closed.recv_timeout(PROMPT).expect("the stalled download connection is closed");
    }

    #[test]
    fn total_deadline_bounds_a_stalled_body_read() {
        let (target, closed) = server(Behaviour::PartialBody);
        let started = Instant::now();
        let response = send(target, true, started + Duration::from_millis(600), &never).unwrap();
        let result = response.read_bounded(4096, &never);
        assert!(matches!(result, Err(Failure::Unavailable(ref text)) if text.contains("did not respond in time")), "{result:?}");
        assert!(started.elapsed() < PROMPT, "deadline returned after {:?}", started.elapsed());
        closed.recv_timeout(PROMPT).expect("the timed-out download connection is closed");
    }

    #[test]
    fn already_cancelled_or_expired_requests_never_touch_the_network() {
        let (target, closed) = server(Behaviour::Complete);
        assert_eq!(send(target.clone(), false, Instant::now() + LONG, &|| true).unwrap_err(), Failure::Cancelled);
        assert!(matches!(send(target, false, Instant::now(), &never), Err(Failure::Unavailable(_))));
        assert!(closed.recv_timeout(Duration::from_millis(300)).is_err(), "no connection was made");
    }

    #[test]
    fn dropping_a_response_releases_its_connection() {
        let (target, closed) = server(Behaviour::PartialBody);
        let response = send(target, true, Instant::now() + LONG, &never).unwrap();
        drop(response);
        closed.recv_timeout(PROMPT).expect("an unread response does not hold its connection open");
    }

    #[test]
    fn refused_connection_fails_without_waiting_for_the_deadline() {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        drop(listener);
        let started = Instant::now();
        let result = send(Target { host: "127.0.0.1".into(), port, secure: false, path: "/".into() }, false, started + LONG, &never);
        assert!(matches!(result, Err(Failure::Unavailable(_))), "{result:?}");
        assert!(started.elapsed() < Duration::from_secs(20), "{:?}", started.elapsed());
    }
}
