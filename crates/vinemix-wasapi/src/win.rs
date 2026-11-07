//! Small owned Windows primitives shared by the catalog and workers.

use std::sync::atomic::{AtomicBool, Ordering};

use windows::core::{HRESULT, PCWSTR};
use windows::Win32::Foundation::{CloseHandle, HANDLE, WAIT_EVENT, WAIT_FAILED, WAIT_OBJECT_0, WAIT_TIMEOUT};
use windows::Win32::System::Com::{CoInitializeEx, CoUninitialize, COINIT_MULTITHREADED};
use windows::Win32::System::Threading::{CreateEventW, ResetEvent, SetEvent, WaitForMultipleObjects, WaitForSingleObject, INFINITE};

/// An owned Win32 event handle. Kernel handles may be used from any thread.
#[derive(Debug)]
pub struct Event(HANDLE);

// SAFETY: a kernel event handle is a process-wide reference usable from any
// thread; all operations on it are thread-safe Win32 calls.
unsafe impl Send for Event {}
// SAFETY: as above.
unsafe impl Sync for Event {}

impl Event {
    /// A manual-reset (`manual = true`) or auto-reset event, initially clear.
    pub fn new(manual: bool) -> windows::core::Result<Self> {
        // SAFETY: plain Win32 call with no pointers.
        let handle = unsafe { CreateEventW(None, manual, false, PCWSTR::null())? };
        Ok(Self(handle))
    }

    pub fn handle(&self) -> HANDLE {
        self.0
    }

    pub fn set(&self) {
        // SAFETY: valid owned handle.
        unsafe {
            let _ = SetEvent(self.0);
        }
    }

    pub fn reset(&self) {
        // SAFETY: valid owned handle.
        unsafe {
            let _ = ResetEvent(self.0);
        }
    }

    /// Whether the event is signalled within `milliseconds`.
    pub fn wait(&self, milliseconds: u32) -> bool {
        // SAFETY: valid owned handle.
        unsafe { WaitForSingleObject(self.0, milliseconds) == WAIT_OBJECT_0 }
    }
}

impl Drop for Event {
    fn drop(&mut self) {
        // SAFETY: owned handle closed once.
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}

/// An owned process or other kernel handle.
#[derive(Debug)]
pub struct OwnedHandle(pub HANDLE);

// SAFETY: see `Event`.
unsafe impl Send for OwnedHandle {}

impl OwnedHandle {
    pub fn is_valid(&self) -> bool {
        !self.0.is_invalid() && !self.0 .0.is_null()
    }
}

impl Drop for OwnedHandle {
    fn drop(&mut self) {
        if self.is_valid() {
            // SAFETY: owned handle closed once.
            unsafe {
                let _ = CloseHandle(self.0);
            }
        }
    }
}

/// Result of waiting on several handles.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Wait {
    Signalled(usize),
    Timeout,
    Failed,
}

pub fn wait_any(handles: &[HANDLE], milliseconds: u32) -> Wait {
    // SAFETY: valid handles for the duration of the call.
    let result: WAIT_EVENT = unsafe { WaitForMultipleObjects(handles, false, milliseconds) };
    if result == WAIT_TIMEOUT {
        Wait::Timeout
    } else if result == WAIT_FAILED {
        Wait::Failed
    } else {
        let index = result.0.wrapping_sub(WAIT_OBJECT_0.0) as usize;
        if index < handles.len() { Wait::Signalled(index) } else { Wait::Failed }
    }
}

pub const FOREVER: u32 = INFINITE;

/// A COM multithreaded apartment for the current thread.
#[derive(Debug)]
pub struct Apartment {
    pub result: HRESULT,
}

impl Apartment {
    pub fn enter() -> Self {
        // SAFETY: balanced by Drop when it succeeded.
        let result = unsafe { CoInitializeEx(None, COINIT_MULTITHREADED) };
        Self { result }
    }

    pub fn ok(&self) -> bool {
        self.result.is_ok()
    }
}

impl Drop for Apartment {
    fn drop(&mut self) {
        if self.result.is_ok() {
            // SAFETY: balances the successful CoInitializeEx above.
            unsafe { CoUninitialize() };
        }
    }
}

/// UTF-16 for Win32 calls, NUL-terminated.
pub fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Lossless when Windows strings are valid UTF-16; lone surrogates become
/// U+FFFD rather than failing a whole inventory.
pub fn utf8(text: &[u16]) -> String {
    String::from_utf16_lossy(text)
}

/// Reads a NUL-terminated wide string.
///
/// # Safety
/// `pointer` must be null or point at a NUL-terminated UTF-16 string.
pub unsafe fn from_wide_ptr(pointer: *const u16) -> String {
    if pointer.is_null() {
        return String::new();
    }
    let mut length = 0;
    // SAFETY: the caller guarantees NUL termination.
    unsafe {
        while *pointer.add(length) != 0 {
            length += 1;
        }
        utf8(std::slice::from_raw_parts(pointer, length))
    }
}

/// A flag that turns `true` exactly once.
#[derive(Debug, Default)]
pub struct Once(AtomicBool);

impl Once {
    pub fn first(&self) -> bool {
        !self.0.swap(true, Ordering::AcqRel)
    }

    pub fn done(&self) -> bool {
        self.0.load(Ordering::Acquire)
    }
}

/// `HRESULT_FROM_WIN32`.
pub const fn hresult_from_win32(code: u32) -> HRESULT {
    if code == 0 { HRESULT(0) } else { HRESULT(((code & 0xFFFF) | 0x8007_0000) as i32) }
}

pub const S_FALSE: HRESULT = HRESULT(1);
pub const ERROR_TIMEOUT: u32 = 1460;
