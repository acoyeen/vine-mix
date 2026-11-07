//! HTTPS GET through WinHTTP with the updater's redirect policy
//! (`UpdateHttp`): API requests stay on api.github.com; assets start on
//! github.com and may only redirect (at most three times) to GitHub's
//! release-asset CDN. Cookies and automatic authentication are disabled.

use std::time::{Duration, Instant};

use windows::core::PCWSTR;
use windows::Win32::Networking::WinHttp::{
    WinHttpCloseHandle, WinHttpConnect, WinHttpOpen, WinHttpOpenRequest, WinHttpQueryHeaders, WinHttpReadData, WinHttpReceiveResponse,
    WinHttpSendRequest, WinHttpSetOption, WinHttpSetTimeouts, WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_DISABLE_AUTHENTICATION,
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
    _session: Handle,
    _connection: Handle,
    request: Handle,
    pub status: u32,
    pub content_length: Option<u64>,
    pub retry_after: bool,
    pub rate_limit_exhausted: bool,
    deadline: Instant,
}

impl std::fmt::Debug for Response {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Response").field("status", &self.status).finish_non_exhaustive()
    }
}

fn query_number(request: &Handle, info: u32) -> Option<u32> {
    let mut value = 0u32;
    let mut size = 4u32;
    // SAFETY: valid request; 4-byte output for numeric queries.
    unsafe { WinHttpQueryHeaders(request.0, info | WINHTTP_QUERY_FLAG_NUMBER, PCWSTR::null(), Some(std::ptr::from_mut(&mut value).cast()), &mut size, std::ptr::null_mut()) }
        .ok()
        .map(|()| value)
}

fn query_text(request: &Handle, info: u32, name: Option<&str>) -> Option<String> {
    let name = name.map(wide);
    let name_ptr = name.as_ref().map_or(PCWSTR::null(), |n| PCWSTR(n.as_ptr()));
    let mut buffer = vec![0u16; 4096];
    let mut size = (buffer.len() * 2) as u32;
    // SAFETY: valid request and buffer.
    unsafe { WinHttpQueryHeaders(request.0, info, name_ptr, Some(buffer.as_mut_ptr().cast()), &mut size, std::ptr::null_mut()) }.ok()?;
    Some(String::from_utf16_lossy(&buffer[..(size as usize / 2).min(buffer.len())]))
}

fn connection_error(error: &windows::core::Error) -> Failure {
    Failure::Unavailable(format!("The update service could not be reached. Check your internet connection and try again. ({})", error.code().0))
}

/// One request without following redirects.
fn send(url: &Url, asset: bool, deadline: Instant) -> Result<Response, Failure> {
    let remaining = deadline.saturating_duration_since(Instant::now());
    if remaining.is_zero() {
        return Err(Failure::Unavailable("The update service did not respond in time.".into()));
    }
    let timeout = i32::try_from(remaining.as_millis().min(60_000)).unwrap_or(60_000);
    let agent = wide("VineMix-Updater/1.0");
    // SAFETY: valid strings; handles closed by `Handle`.
    let session = Handle(unsafe { WinHttpOpen(PCWSTR(agent.as_ptr()), WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, PCWSTR::null(), PCWSTR::null(), 0) });
    if session.0.is_null() {
        return Err(Failure::Unavailable("The update service could not be reached.".into()));
    }
    // SAFETY: valid session.
    unsafe {
        let _ = WinHttpSetTimeouts(session.0, timeout, timeout, timeout, timeout);
    }
    let host = wide(&url.host);
    // SAFETY: valid session and host.
    let connection = Handle(unsafe { WinHttpConnect(session.0, PCWSTR(host.as_ptr()), 443, 0) });
    if connection.0.is_null() {
        return Err(Failure::Unavailable("The update service could not be reached.".into()));
    }
    let verb = wide("GET");
    let path = wide(&url.path);
    // SAFETY: valid connection and strings.
    let request = Handle(unsafe {
        WinHttpOpenRequest(connection.0, PCWSTR(verb.as_ptr()), PCWSTR(path.as_ptr()), PCWSTR::null(), PCWSTR::null(), std::ptr::null(), WINHTTP_OPEN_REQUEST_FLAGS(WINHTTP_FLAG_SECURE.0))
    });
    if request.0.is_null() {
        return Err(Failure::Unavailable("The update service could not be reached.".into()));
    }
    let never = WINHTTP_OPTION_REDIRECT_POLICY_NEVER;
    let features = WINHTTP_DISABLE_COOKIES | WINHTTP_DISABLE_AUTHENTICATION;
    // SAFETY: valid request; option buffers are 4-byte values.
    unsafe {
        let _ = WinHttpSetOption(Some(request.0), WINHTTP_OPTION_REDIRECT_POLICY, Some(&never.to_ne_bytes()));
        let _ = WinHttpSetOption(Some(request.0), WINHTTP_OPTION_DISABLE_FEATURE, Some(&features.to_ne_bytes()));
    }
    let headers = if asset {
        "Accept: application/octet-stream\r\n".to_owned()
    } else {
        "Accept: application/vnd.github+json\r\nX-GitHub-Api-Version: 2022-11-28\r\n".to_owned()
    };
    let headers = wide(&headers);
    // SAFETY: valid request and header buffer (length excludes the NUL).
    unsafe { WinHttpSendRequest(request.0, Some(&headers[..headers.len() - 1]), None, 0, 0, 0) }.map_err(|e| connection_error(&e))?;
    // SAFETY: valid request.
    unsafe { WinHttpReceiveResponse(request.0, std::ptr::null_mut()) }.map_err(|e| connection_error(&e))?;
    let status = query_number(&request, WINHTTP_QUERY_STATUS_CODE).unwrap_or(0);
    let content_length = query_text(&request, WINHTTP_QUERY_CONTENT_LENGTH, None).and_then(|t| t.trim().parse().ok());
    let retry_after = query_text(&request, WINHTTP_QUERY_RETRY_AFTER, None).is_some();
    let rate_limit_exhausted = query_text(&request, WINHTTP_QUERY_CUSTOM, Some("X-RateLimit-Remaining")).is_some_and(|v| v.trim() == "0");
    Ok(Response { _session: session, _connection: connection, request, status, content_length, retry_after, rate_limit_exhausted, deadline })
}

/// GET with the updater's origin and redirect policy.
pub fn get(url: &str, asset: bool, timeout: Duration) -> Result<Response, Failure> {
    let deadline = Instant::now() + timeout;
    let mut current = Url::parse(url).ok_or_else(|| Failure::Invalid("The update download redirected outside its trusted HTTPS origins.".into()))?;
    for redirect in 0..=3 {
        if if asset { !is_asset_host(&current.host) } else { current.host != "api.github.com" } {
            return Err(Failure::Invalid("The update download redirected outside its trusted HTTPS origins.".into()));
        }
        let response = send(&current, asset, deadline)?;
        if (300..=399).contains(&response.status) {
            let location = query_text(&response.request, WINHTTP_QUERY_LOCATION, None);
            let Some(location) = location.filter(|_| asset && redirect < 3) else {
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
    pub fn read_with(&self, maximum: u64, mut sink: impl FnMut(&[u8]) -> Result<(), Failure>) -> Result<u64, Failure> {
        if self.content_length.is_some_and(|l| l > maximum) {
            return Err(Failure::Invalid("The update response exceeded its size limit.".into()));
        }
        let mut total = 0u64;
        let mut buffer = vec![0u8; 65536];
        loop {
            if Instant::now() >= self.deadline {
                return Err(Failure::Unavailable("The update service did not respond in time.".into()));
            }
            let mut read = 0u32;
            // SAFETY: valid request and buffer.
            unsafe { WinHttpReadData(self.request.0, buffer.as_mut_ptr().cast(), buffer.len() as u32, &mut read) }
                .map_err(|_| Failure::Unavailable("The update response was interrupted. Try again.".into()))?;
            if read == 0 {
                break;
            }
            total += u64::from(read);
            if total > maximum {
                return Err(Failure::Invalid("The update response exceeded its size limit.".into()));
            }
            sink(&buffer[..read as usize])?;
        }
        if self.content_length.is_some_and(|l| l != total) {
            return Err(Failure::Invalid("The update response was interrupted or has the wrong length.".into()));
        }
        Ok(total)
    }

    pub fn read_bounded(&self, maximum: u64) -> Result<Vec<u8>, Failure> {
        let mut body = Vec::new();
        self.read_with(maximum, |chunk| {
            body.extend_from_slice(chunk);
            Ok(())
        })?;
        Ok(body)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

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
}
