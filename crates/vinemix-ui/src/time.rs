//! Local wall-clock formatting through Win32, without a date crate.

use windows::Win32::Foundation::SYSTEMTIME;
use windows::Win32::System::SystemInformation::GetLocalTime;

pub fn local_time() -> SYSTEMTIME {
    // SAFETY: trivial Win32 call.
    unsafe { GetLocalTime() }
}

/// ISO-8601 local timestamp with milliseconds, e.g. `2026-11-06T14:03:22.125`.
pub fn local_timestamp() -> String {
    let t = local_time();
    format!(
        "{:04}-{:02}-{:02}T{:02}:{:02}:{:02}.{:03}",
        t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond, t.wMilliseconds
    )
}

/// `HH:mm:ss` local clock time for activity entries (`Time.ToString("HH:mm:ss")`).
pub fn clock(time: std::time::SystemTime) -> String {
    let now = std::time::SystemTime::now();
    let ago = now.duration_since(time).unwrap_or_default();
    let t = local_time();
    let seconds_now = i64::from(t.wHour) * 3600 + i64::from(t.wMinute) * 60 + i64::from(t.wSecond);
    let total = (seconds_now - i64::try_from(ago.as_secs()).unwrap_or(0)).rem_euclid(24 * 3600);
    format!("{:02}:{:02}:{:02}", total / 3600, total / 60 % 60, total % 60)
}

/// Seconds since the Unix epoch.
pub fn unix_seconds() -> i64 {
    std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_or(0, |d| i64::try_from(d.as_secs()).unwrap_or(i64::MAX))
}

/// `yyyyMMdd-HHmmss` local time for file names.
pub fn file_stamp() -> String {
    let t = local_time();
    format!("{:04}{:02}{:02}-{:02}{:02}{:02}", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond)
}
