//! Win32 implementations (`configuration.cpp`, `engine_instance.hpp`).

use std::ffi::OsString;
use std::os::windows::ffi::{OsStrExt, OsStringExt};
use std::path::{Path, PathBuf};

use windows::core::{PCWSTR, PWSTR};
use windows::Win32::Foundation::{
    CloseHandle, GetLastError, LocalFree, ERROR_ALREADY_EXISTS, ERROR_FILE_NOT_FOUND, ERROR_PATH_NOT_FOUND, ERROR_SUCCESS,
    GENERIC_WRITE, HANDLE, HLOCAL, SetLastError,
};
use windows::Win32::Security::Authorization::ConvertSidToStringSidW;
use windows::Win32::Security::{GetTokenInformation, TokenUser, TOKEN_QUERY, TOKEN_USER};
use windows::Win32::Storage::FileSystem::{
    CopyFileW, CreateFileW, FlushFileBuffers, GetFinalPathNameByHandleW, MoveFileExW, ReplaceFileW, WriteFile, CREATE_ALWAYS, CREATE_NEW,
    FILE_ATTRIBUTE_NORMAL, FILE_FLAG_BACKUP_SEMANTICS, FILE_NAME_NORMALIZED, FILE_READ_ATTRIBUTES, FILE_SHARE_DELETE, FILE_SHARE_READ,
    FILE_SHARE_WRITE, MOVEFILE_WRITE_THROUGH, OPEN_EXISTING, REPLACEFILE_IGNORE_MERGE_ERRORS,
};
use windows::Win32::System::Threading::{CreateMutexW, GetCurrentProcess, OpenProcessToken, ReleaseMutex};

fn wide_path(path: &Path) -> Vec<u16> {
    path.as_os_str().encode_wide().chain(std::iter::once(0)).collect()
}

/// The signed-in user's SID string (`S-1-5-21-...`).
pub fn current_user_sid() -> Result<String, String> {
    // SAFETY: token handle closed below; buffers sized by the first call.
    unsafe {
        let mut token = HANDLE::default();
        OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token).map_err(|_| "Cannot determine the current Windows user.")?;
        let mut bytes = 0u32;
        let _ = GetTokenInformation(token, TokenUser, None, 0, &mut bytes);
        let mut storage = vec![0u64; (bytes as usize).div_ceil(8)];
        let read = GetTokenInformation(token, TokenUser, Some(storage.as_mut_ptr().cast()), bytes, &mut bytes);
        let _ = CloseHandle(token);
        read.map_err(|_| "Cannot read the current Windows user.")?;
        let user = &*(storage.as_ptr().cast::<TOKEN_USER>());
        let mut sid = PWSTR::null();
        ConvertSidToStringSidW(user.User.Sid, &mut sid).map_err(|_| "Cannot format the current Windows user.")?;
        let text = sid.to_string().map_err(|_| "Cannot format the current Windows user.");
        let _ = LocalFree(Some(HLOCAL(sid.0.cast())));
        Ok(text?)
    }
}

/// The per-user scope for every named kernel object and pipe: the user's
/// SID, plus an isolated development instance when `VINEMIX_DEV_INSTANCE`
/// names one (ASCII letters and digits, at most 32). A development instance
/// never shares a pipe, lock or show event with the user's normal session.
pub fn user_scope() -> Result<String, String> {
    let sid = current_user_sid()?;
    Ok(match checked_development_instance()? {
        Some(instance) => format!("{sid}.dev-{instance}"),
        None => sid,
    })
}

/// The validated development instance name, if one is set.
pub fn development_instance() -> Option<String> {
    checked_development_instance().ok().flatten()
}

/// Distinguishes an ordinary launch from an invalid isolation request.
/// The executables check this before accessing user data or named objects.
pub fn checked_development_instance() -> Result<Option<String>, String> {
    parse_development_instance(std::env::var_os("VINEMIX_DEV_INSTANCE"))
}

fn parse_development_instance(value: Option<OsString>) -> Result<Option<String>, String> {
    let Some(value) = value else { return Ok(None) };
    let value = value.into_string().ok().filter(|s| !s.is_empty() && s.len() <= 32 && s.bytes().all(|b| b.is_ascii_alphanumeric()));
    value.map(Some).ok_or_else(|| "VINEMIX_DEV_INSTANCE must contain 1 to 32 ASCII letters or digits.".to_owned())
}

/// `\\.\pipe\VineMix.Control.<SID>`.
pub fn pipe_name() -> Result<String, String> {
    Ok(format!(r"\\.\pipe\VineMix.Control.{}", user_scope()?))
}

/// `%LOCALAPPDATA%\VineMix` (`VineMix.dev-<instance>` for a development
/// instance, so test runs never touch an installed copy's data).
pub fn application_data_directory() -> Result<PathBuf, String> {
    application_data_directory_from(std::env::var_os("LOCALAPPDATA"), checked_development_instance()?.as_deref())
}

/// [`application_data_directory`] for an explicit `LOCALAPPDATA` value. The
/// location must be an absolute path: an unset, empty or relative value is an
/// error, never a folder relative to the working directory.
pub fn application_data_directory_from(local_app_data: Option<OsString>, instance: Option<&str>) -> Result<PathBuf, String> {
    let folder = match instance {
        Some(instance) => format!("VineMix.dev-{instance}"),
        None => "VineMix".to_owned(),
    };
    match local_app_data {
        Some(value) if !value.is_empty() => {
            let root = PathBuf::from(value);
            if root.is_absolute() {
                Ok(root.join(folder))
            } else {
                Err("LOCALAPPDATA is not an absolute folder path.".into())
            }
        }
        _ => Err("LOCALAPPDATA is unavailable.".into()),
    }
}

/// The opened object's physical path: under Windows filesystem redirection
/// the printed LOCALAPPDATA string can name a different file. Missing
/// trailing components are resolved against their nearest existing parent.
pub fn physical_path(path: &Path) -> Result<PathBuf, String> {
    let mut candidate = std::path::absolute(path).map_err(|_| "Cannot verify the physical settings location.")?;
    let mut missing: Vec<OsString> = Vec::new();
    let handle = loop {
        let name = wide_path(&candidate);
        // SAFETY: valid path; handle closed below.
        let opened = unsafe {
            CreateFileW(
                PCWSTR(name.as_ptr()),
                FILE_READ_ATTRIBUTES.0,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                None,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                None,
            )
        };
        match opened {
            Ok(handle) => break handle,
            Err(error) => {
                let code = error.code();
                if code != ERROR_FILE_NOT_FOUND.to_hresult() && code != ERROR_PATH_NOT_FOUND.to_hresult() {
                    return Err("Cannot verify the physical settings location.".into());
                }
                let Some(parent) = candidate.parent().map(Path::to_path_buf) else {
                    return Err("Cannot resolve the settings volume.".into());
                };
                if parent.as_os_str().is_empty() || parent == candidate {
                    return Err("Cannot resolve the settings volume.".into());
                }
                missing.push(candidate.file_name().map(OsString::from).unwrap_or_default());
                candidate = parent;
            }
        }
    };
    let mut buffer = vec![0u16; 32768];
    // SAFETY: valid handle and buffer.
    let length = unsafe { GetFinalPathNameByHandleW(handle, &mut buffer, FILE_NAME_NORMALIZED) } as usize;
    // SAFETY: handle opened above.
    unsafe {
        let _ = CloseHandle(handle);
    }
    if length == 0 || length >= buffer.len() {
        return Err("Cannot resolve the physical settings location.".into());
    }
    let mut text = String::from_utf16_lossy(&buffer[..length]);
    if let Some(rest) = text.strip_prefix(r"\\?\UNC\") {
        text = format!(r"\\{rest}");
    } else if let Some(rest) = text.strip_prefix(r"\\?\") {
        text = rest.to_owned();
    }
    let mut resolved = PathBuf::from(text);
    for component in missing.iter().rev() {
        resolved.push(component);
    }
    Ok(resolved)
}

/// Creates `path` exclusively and writes `contents` durably.
pub fn create_new_durable(path: &Path, contents: &[u8]) -> Result<(), ()> {
    write_durable(path, contents, true)
}

/// Creates or truncates `path` and writes `contents` durably.
pub fn write_file_durable(path: &Path, contents: &[u8]) -> Result<(), ()> {
    write_durable(path, contents, false)
}

fn write_durable(path: &Path, contents: &[u8], exclusive: bool) -> Result<(), ()> {
    let name = wide_path(path);
    // SAFETY: valid path; handle closed below.
    let handle = unsafe {
        CreateFileW(
            PCWSTR(name.as_ptr()),
            GENERIC_WRITE.0,
            Default::default(),
            None,
            if exclusive { CREATE_NEW } else { CREATE_ALWAYS },
            FILE_ATTRIBUTE_NORMAL,
            None,
        )
    }
    .map_err(|_| ())?;
    let mut written = 0u32;
    // SAFETY: valid handle and buffer.
    let ok = unsafe {
        WriteFile(handle, Some(contents), Some(&mut written), None).is_ok()
            && written as usize == contents.len()
            && FlushFileBuffers(handle).is_ok()
    };
    // SAFETY: handle opened above.
    unsafe {
        let _ = CloseHandle(handle);
    }
    if ok { Ok(()) } else { Err(()) }
}

/// `CopyFileW(source, destination, failIfExists = TRUE)`.
pub fn copy_new(source: &Path, destination: &Path) -> bool {
    let (from, to) = (wide_path(source), wide_path(destination));
    // SAFETY: valid paths.
    unsafe { CopyFileW(PCWSTR(from.as_ptr()), PCWSTR(to.as_ptr()), true).is_ok() }
}

/// `ReplaceFileW(target, replacement, backup?, IGNORE_MERGE_ERRORS)`.
pub fn replace_file(target: &Path, replacement: &Path, backup: Option<&Path>) -> windows::core::Result<()> {
    let (to, from) = (wide_path(target), wide_path(replacement));
    let backup = backup.map(wide_path);
    // SAFETY: valid paths.
    unsafe {
        ReplaceFileW(
            PCWSTR(to.as_ptr()),
            PCWSTR(from.as_ptr()),
            backup.as_ref().map_or(PCWSTR::null(), |b| PCWSTR(b.as_ptr())),
            REPLACEFILE_IGNORE_MERGE_ERRORS,
            None,
            None,
        )
    }
}

/// `MoveFileExW(source, destination, MOVEFILE_WRITE_THROUGH)`.
pub fn move_write_through(source: &Path, destination: &Path) -> bool {
    let (from, to) = (wide_path(source), wide_path(destination));
    // SAFETY: valid paths.
    unsafe { MoveFileExW(PCWSTR(from.as_ptr()), PCWSTR(to.as_ptr()), MOVEFILE_WRITE_THROUGH).is_ok() }
}

/// Milliseconds since boot (`GetTickCount64`), used for unique file names.
pub fn tick_count() -> u64 {
    // SAFETY: trivial Win32 call.
    unsafe { windows::Win32::System::SystemInformation::GetTickCount64() }
}

/// The per-user engine lock. The legacy Audio Mixer name is held too, so
/// either product excludes the other for this Windows user.
#[derive(Debug)]
pub struct EngineInstance {
    handles: Vec<HANDLE>,
}

// SAFETY: mutex handles are process-wide kernel references.
unsafe impl Send for EngineInstance {}

impl EngineInstance {
    pub fn acquire(identity: &str) -> Result<Self, String> {
        let mut instance = Self { handles: Vec::new() };
        for name in [format!(r"Local\AudioMixer.Engine.{identity}"), format!(r"Local\VineMix.Engine.{identity}")] {
            let name = crate::wide(&name);
            // SAFETY: valid name; handle owned by `instance`.
            unsafe {
                SetLastError(ERROR_SUCCESS);
                let handle = CreateMutexW(None, true, PCWSTR(name.as_ptr())).map_err(|_| "Cannot create the engine instance lock.")?;
                let error = GetLastError();
                if error == ERROR_ALREADY_EXISTS {
                    let _ = CloseHandle(handle);
                    return Err("Another VineMix or Audio Mixer engine is already running for this Windows user.".into());
                }
                instance.handles.push(handle);
            }
        }
        Ok(instance)
    }
}

impl Drop for EngineInstance {
    fn drop(&mut self) {
        for handle in self.handles.drain(..) {
            // SAFETY: owned mutex handles.
            unsafe {
                let _ = ReleaseMutex(handle);
                let _ = CloseHandle(handle);
            }
        }
    }
}

/// The running executable's path.
pub fn current_executable() -> String {
    std::env::current_exe().map(|p| p.to_string_lossy().into_owned()).unwrap_or_default()
}

/// Converts a wide buffer to a path.
pub fn path_from_wide(buffer: &[u16]) -> PathBuf {
    PathBuf::from(OsString::from_wide(buffer))
}

#[cfg(test)]
mod data_directory_tests {
    use super::*;

    #[test]
    fn data_directory_is_absolute_or_an_error() {
        let ok = application_data_directory_from(Some(r"C:\Users\Example\AppData\Local".into()), None).unwrap();
        assert!(ok.is_absolute() && ok.ends_with("VineMix"), "{ok:?}");
        let dev = application_data_directory_from(Some(r"C:\Users\Example\AppData\Local".into()), Some("probe")).unwrap();
        assert!(dev.ends_with("VineMix.dev-probe"), "{dev:?}");
        for unusable in [None, Some(OsString::new()), Some("AppData".into()), Some(r".\Local".into()), Some(r"C:Local".into())] {
            let result = application_data_directory_from(unusable.clone(), None);
            assert!(result.is_err(), "{unusable:?} must not resolve, got {result:?}");
        }
    }
}

#[cfg(test)]
mod development_instance_tests {
    use super::*;

    #[test]
    fn rejects_invalid_isolation_requests_instead_of_selecting_the_default() {
        assert_eq!(parse_development_instance(None).unwrap(), None);
        assert_eq!(parse_development_instance(Some("Qa17".into())).unwrap().as_deref(), Some("Qa17"));
        for bad in ["", "qa-audio", "../qa", "123456789012345678901234567890123"] {
            assert!(parse_development_instance(Some(bad.into())).is_err());
        }
        assert!(parse_development_instance(Some(OsString::from_wide(&[0xd800]))).is_err());
    }
}
