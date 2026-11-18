//! Windows effects of the update worker (`WindowsUpdatePlatform`).

use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

use windows::core::PCWSTR;
use windows::Win32::Foundation::{CloseHandle, ERROR_FILE_NOT_FOUND, ERROR_NO_MORE_ITEMS, ERROR_SUCCESS, HANDLE, WAIT_ABANDONED, WAIT_OBJECT_0};
use windows::Win32::Storage::FileSystem::{GetFileVersionInfoSizeW, GetFileVersionInfoW, VerQueryValueW, VS_FIXEDFILEINFO};
use windows::Win32::System::Registry::{
    RegCloseKey, RegCreateKeyExW, RegDeleteValueW, RegEnumValueW, RegFlushKey, RegOpenKeyExW, RegQueryInfoKeyW, RegSetValueExW, HKEY, HKEY_CURRENT_USER, KEY_READ,
    KEY_WOW64_64KEY, KEY_WRITE, REG_BINARY, REG_DWORD, REG_EXPAND_SZ, REG_MULTI_SZ, REG_OPTION_NON_VOLATILE, REG_QWORD, REG_SZ, REG_VALUE_TYPE,
};
use windows::Win32::System::Threading::{CreateMutexW, ReleaseMutex, WaitForSingleObject};

use super::files::{self, Result};
use super::model::{ProcessIdentity, RegistrySnapshot, RegistryValue};
use super::transaction::{self, Verified};
use super::worker::{Lock, Platform};
use crate::platform::{wide, Process};

/// The Inno Setup AppId shared with the 0.5 installer.
pub const APPLICATION_ID: &str = "bac29135-ce09-4029-af38-d22d1e177d4f";
const UNINSTALL_KEY: &str = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\bac29135-ce09-4029-af38-d22d1e177d4f_is1";
const RUN_ONCE_KEY: &str = "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce";

pub fn mutex_name() -> Result<String> {
    let sid = vinemix_platform::current_user_sid().map_err(|_| "The Windows user cannot be identified.".to_owned())?;
    Ok(format!("Global\\VineMix.Update.{sid}"))
}

/// Whether another process holds the update mutex right now.
pub fn update_in_progress() -> Result<bool> {
    let name = wide(&mutex_name()?);
    // SAFETY: plain named mutex; closed below.
    let Ok(handle) = (unsafe { CreateMutexW(None, false, PCWSTR(name.as_ptr())) }) else { return Ok(false) };
    // SAFETY: valid handle.
    let result = unsafe { WaitForSingleObject(handle, 0) };
    let acquired = result == WAIT_OBJECT_0 || result == WAIT_ABANDONED;
    // SAFETY: release only what this call acquired; then close.
    unsafe {
        if acquired {
            let _ = ReleaseMutex(handle);
        }
        let _ = CloseHandle(handle);
    }
    Ok(!acquired)
}

fn quote(value: &str) -> Result<String> {
    if value.contains('"') || value.chars().any(char::is_control) {
        return Err("The update path cannot be represented as a Windows launch argument.".into());
    }
    Ok(format!("\"{value}\""))
}

struct Key(HKEY);

impl Drop for Key {
    fn drop(&mut self) {
        // SAFETY: owned key.
        unsafe {
            let _ = RegCloseKey(self.0);
        }
    }
}

fn open_key(path: &str, write: bool) -> Option<Key> {
    let name = wide(path);
    let mut key = HKEY::default();
    let access = (if write { KEY_READ | KEY_WRITE } else { KEY_READ }) | KEY_WOW64_64KEY;
    // SAFETY: valid name and out pointer.
    let status = unsafe { RegOpenKeyExW(HKEY_CURRENT_USER, PCWSTR(name.as_ptr()), Some(0), access, &mut key) };
    (status == ERROR_SUCCESS).then_some(Key(key))
}

fn utf16_string(data: &[u8]) -> String {
    let units: Vec<u16> = data.chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
    let end = units.iter().position(|&u| u == 0).unwrap_or(units.len());
    String::from_utf16_lossy(&units[..end])
}

fn multi_strings(data: &[u8]) -> Vec<String> {
    let units: Vec<u16> = data.chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
    let mut strings: Vec<String> = units.split(|&u| u == 0).map(String::from_utf16_lossy).collect();
    while strings.last().is_some_and(String::is_empty) {
        strings.pop();
    }
    strings
}

fn snapshot_values(key: &Key) -> Result<Vec<RegistryValue>> {
    let mut values = Vec::new();
    for index in 0.. {
        let mut name = vec![0u16; 16384];
        let mut name_length = name.len() as u32;
        let mut kind = 0u32;
        let mut data = vec![0u8; 1 << 16];
        let mut data_length = data.len() as u32;
        // SAFETY: buffers and lengths are valid.
        let status = unsafe {
            RegEnumValueW(key.0, index, Some(windows::core::PWSTR(name.as_mut_ptr())), &mut name_length, None, Some(&mut kind), Some(data.as_mut_ptr()), Some(&mut data_length))
        };
        if status == ERROR_NO_MORE_ITEMS {
            break;
        }
        if status != ERROR_SUCCESS {
            return Err("The owned uninstall key could not be read; use manual repair.".into());
        }
        let name = String::from_utf16_lossy(&name[..name_length as usize]);
        let data = &data[..data_length as usize];
        let kind = REG_VALUE_TYPE(kind);
        let value = if kind == REG_SZ || kind == REG_EXPAND_SZ {
            RegistryValue { name, kind: kind.0 as i32, strings: vec![utf16_string(data)], bytes: Vec::new(), number: 0 }
        } else if kind == REG_MULTI_SZ {
            RegistryValue { name, kind: kind.0 as i32, strings: multi_strings(data), bytes: Vec::new(), number: 0 }
        } else if kind == REG_BINARY {
            RegistryValue { name, kind: kind.0 as i32, strings: Vec::new(), bytes: data.to_vec(), number: 0 }
        } else if kind == REG_DWORD && data.len() == 4 {
            RegistryValue { name, kind: kind.0 as i32, strings: Vec::new(), bytes: Vec::new(), number: i64::from(i32::from_le_bytes([data[0], data[1], data[2], data[3]])) }
        } else if kind == REG_QWORD && data.len() == 8 {
            let mut bytes = [0u8; 8];
            bytes.copy_from_slice(data);
            RegistryValue { name, kind: kind.0 as i32, strings: Vec::new(), bytes: Vec::new(), number: i64::from_le_bytes(bytes) }
        } else {
            return Err("The owned uninstall key contains an unsupported value type; use manual repair.".into());
        };
        values.push(value);
    }
    values.sort_by(|a, b| a.name.encode_utf16().cmp(b.name.encode_utf16()));
    Ok(values)
}

fn string_value<'a>(values: &'a [RegistryValue], name: &str) -> Option<&'a str> {
    values.iter().find(|v| v.name.eq_ignore_ascii_case(name) && (v.kind == REG_SZ.0 as i32 || v.kind == REG_EXPAND_SZ.0 as i32)).and_then(|v| v.strings.first()).map(String::as_str)
}

/// The per-user uninstall registration that owns exactly `target_directory`.
pub fn read_owned_registration(target_directory: &Path) -> Result<RegistrySnapshot> {
    let target = files::full(target_directory);
    files::no_reparse(&target)?;
    let data = crate::paths::data_directory();
    if files::same_path(&target, &data) || files::is_inside(&target, &data) {
        return Err("The application installation cannot be the user's saved-data directory.".into());
    }
    let key = open_key(UNINSTALL_KEY, false).ok_or("This copy has no owned per-user installation registration. Use Setup for manual repair.")?;
    let (mut subkeys, mut value_count) = (0u32, 0u32);
    // SAFETY: valid key and out pointers.
    let status = unsafe { RegQueryInfoKeyW(key.0, None, None, None, Some(&mut subkeys), None, None, Some(&mut value_count), None, None, None, None) };
    let values = snapshot_values(&key)?;
    let uninstall = format!("\"{}\"", target.join("unins000.exe").display());
    let owns = status == ERROR_SUCCESS
        && subkeys == 0
        && value_count <= 128
        && string_value(&values, "InstallLocation").is_some_and(|install| files::same_path(Path::new(install), &target))
        && string_value(&values, "UninstallString").is_some_and(|text| text.eq_ignore_ascii_case(&uninstall));
    if !owns {
        return Err("The existing uninstall registration does not own this exact application folder.".into());
    }
    let ini = target.join("installation.ini");
    files::no_reparse(&ini)?;
    let text = std::fs::read_to_string(&ini).map_err(|_| "This installation ownership marker is missing or belongs to another product.".to_owned())?;
    let (mut section, mut application) = (String::new(), String::new());
    for line in text.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with('[') && trimmed.ends_with(']') {
            section = trimmed[1..trimmed.len() - 1].to_owned();
        } else if section.eq_ignore_ascii_case("Installation") && trimmed.len() >= 14 && trimmed[..14].eq_ignore_ascii_case("ApplicationId=") {
            if !application.is_empty() {
                return Err("The installation ownership marker is ambiguous.".into());
            }
            application = trimmed[14..].trim().to_owned();
        }
    }
    if !application.eq_ignore_ascii_case(APPLICATION_ID) {
        return Err("This installation ownership marker is missing or belongs to another product.".into());
    }
    Ok(RegistrySnapshot { key: UNINSTALL_KEY.into(), target_directory: target.to_string_lossy().into_owned(), values })
}

fn set_value(key: &Key, value: &RegistryValue) -> Result<()> {
    let name = wide(&value.name);
    let kind = REG_VALUE_TYPE(value.kind as u32);
    let data: Vec<u8> = if kind == REG_SZ || kind == REG_EXPAND_SZ {
        let text = value.strings.first().ok_or("The saved uninstall registration has an unsupported value type.")?;
        wide(text).iter().flat_map(|u| u.to_le_bytes()).collect()
    } else if kind == REG_MULTI_SZ {
        let mut units: Vec<u16> = Vec::new();
        for text in &value.strings {
            units.extend(text.encode_utf16());
            units.push(0);
        }
        units.push(0);
        units.iter().flat_map(|u| u.to_le_bytes()).collect()
    } else if kind == REG_BINARY {
        value.bytes.clone()
    } else if kind == REG_DWORD {
        i32::try_from(value.number).map_err(|_| "The saved uninstall registration has an unsupported value type.".to_owned())?.to_le_bytes().to_vec()
    } else if kind == REG_QWORD {
        value.number.to_le_bytes().to_vec()
    } else {
        return Err("The saved uninstall registration has an unsupported value type.".into());
    };
    // SAFETY: valid key, name and data.
    let status = unsafe { RegSetValueExW(key.0, PCWSTR(name.as_ptr()), Some(0), kind, Some(&data)) };
    if status != ERROR_SUCCESS {
        return Err("The uninstall registration could not be restored.".into());
    }
    Ok(())
}

/// Reads the `StringFileInfo` FileVersion (as `FileVersionInfo.FileVersion`),
/// falling back to the fixed version numbers.
pub fn file_version(path: &Path) -> Option<String> {
    let name = files::full(path).to_string_lossy().into_owned();
    let name = wide(&name);
    // SAFETY: valid path.
    let size = unsafe { GetFileVersionInfoSizeW(PCWSTR(name.as_ptr()), None) };
    if size == 0 {
        return None;
    }
    let mut data = vec![0u8; size as usize];
    // SAFETY: buffer sized as reported.
    unsafe { GetFileVersionInfoW(PCWSTR(name.as_ptr()), Some(0), size, data.as_mut_ptr().cast()) }.ok()?;
    let query = |sub: &str| -> Option<(*const u8, u32)> {
        let sub = wide(sub);
        let mut pointer: *mut std::ffi::c_void = std::ptr::null_mut();
        let mut length = 0u32;
        // SAFETY: data holds a version resource; the result points inside it.
        unsafe { VerQueryValueW(data.as_ptr().cast(), PCWSTR(sub.as_ptr()), &mut pointer, &mut length) }.as_bool().then_some((pointer as *const u8, length))
    };
    if let Some((pointer, length)) = query("\\VarFileInfo\\Translation") {
        if length >= 4 {
            // SAFETY: at least one (language, code page) pair.
            let (language, codepage) = unsafe { (std::ptr::read_unaligned(pointer.cast::<u16>()), std::ptr::read_unaligned(pointer.add(2).cast::<u16>())) };
            if let Some((text, characters)) = query(&format!("\\StringFileInfo\\{language:04x}{codepage:04x}\\FileVersion")) {
                if characters > 0 {
                    // SAFETY: `characters` UTF-16 units inside the resource.
                    let units = unsafe { std::slice::from_raw_parts(text.cast::<u16>(), characters as usize) };
                    let value = String::from_utf16_lossy(units).trim_end_matches('\0').trim().to_owned();
                    if !value.is_empty() {
                        return Some(value);
                    }
                }
            }
        }
    }
    let (pointer, length) = query("\\")?;
    if (length as usize) < std::mem::size_of::<VS_FIXEDFILEINFO>() {
        return None;
    }
    // SAFETY: a VS_FIXEDFILEINFO inside the resource.
    let info = unsafe { std::ptr::read_unaligned(pointer.cast::<VS_FIXEDFILEINFO>()) };
    Some(format!("{}.{}.{}.{}", info.dwFileVersionMS >> 16, info.dwFileVersionMS & 0xFFFF, info.dwFileVersionLS >> 16, info.dwFileVersionLS & 0xFFFF))
}

/// `System.Version` parsing (2–4 numeric components).
fn parse_version(text: &str) -> Option<Vec<u32>> {
    let parts: Option<Vec<u32>> = text.trim().split('.').map(|p| p.parse().ok()).collect();
    parts.filter(|p| (2..=4).contains(&p.len()))
}

/// Captures a just-started child (`TryCaptureStarted`).
fn capture_started(process: &Process, expected: &Path) -> Result<Option<ProcessIdentity>> {
    let Some(identity) = ProcessIdentity::of(process) else { return Ok(None) };
    let identity = identity?;
    if !files::same_path(Path::new(&identity.image_path), expected) {
        return Err("The started update process does not use the verified executable.".into());
    }
    Ok(Some(identity))
}

fn start(path: &Path, arguments: &[String], directory: &Path) -> Result<Process> {
    files::no_reparse(path)?;
    crate::platform::launch(&files::full(path), arguments, directory)
}

/// The worker's Windows platform.
#[derive(Clone, Copy, Debug, Default)]
pub struct WindowsPlatform;

impl Platform for WindowsPlatform {
    fn acquire_lock(&self) -> Result<Lock> {
        let name = wide(&mutex_name()?);
        // SAFETY: named mutex owned by the returned lock.
        let handle = unsafe { CreateMutexW(None, false, PCWSTR(name.as_ptr())) }.map_err(|e| format!("The update lock could not be opened. {}", e.message()))?;
        // SAFETY: valid handle.
        let result = unsafe { WaitForSingleObject(handle, 0) };
        if result != WAIT_OBJECT_0 && result != WAIT_ABANDONED {
            // SAFETY: owned handle.
            unsafe {
                let _ = CloseHandle(handle);
            }
            return Err("Another update or update recovery is already active.".into());
        }
        let raw = handle.0 as isize;
        Ok(Lock::new(move || {
            let handle = HANDLE(raw as *mut _);
            // SAFETY: release the mutex this thread acquired, then close it.
            unsafe {
                let _ = ReleaseMutex(handle);
                let _ = CloseHandle(handle);
            }
        }))
    }

    fn watch_live_process(&self, identity: &ProcessIdentity) -> Box<dyn Fn() -> bool + '_> {
        let process = u32::try_from(identity.process_id).ok().and_then(Process::open).filter(|process| {
            ProcessIdentity::of(process).and_then(std::result::Result::ok).is_some_and(|current| identity.same(&current))
        });
        Box::new(move || process.as_ref().is_some_and(|p| !p.has_exited()))
    }

    fn wait_for_exit(&self, identity: &ProcessIdentity, timeout: Duration) -> Result<bool> {
        let Ok(pid) = u32::try_from(identity.process_id) else { return Ok(true) };
        let Some(process) = Process::open(pid) else { return Ok(true) };
        // A reused PID is a different process, never a target to wait for.
        if process.start_time().map(super::model::ticks_from_filetime) != Some(identity.start_time_utc_ticks) {
            return Ok(true);
        }
        let Some(image) = process.image_path() else { return Ok(true) };
        if !files::same_path(&image, Path::new(&identity.image_path)) {
            return Err("A process identity changed unexpectedly.".into());
        }
        Ok(process.wait(timeout))
    }

    fn verify_worker(&self, context: &Verified) -> Result<()> {
        let worker = std::env::current_exe().map_err(|_| "The update worker path cannot be identified.".to_owned())?;
        if !files::same_path(&worker, &context.worker_path())
            || !files::equal_hash(&files::hash(&worker)?, &context.data.parent.sha256)
            || context.data.current_version != vinemix_platform::release::VERSION
        {
            return Err("Update recovery must use the copied original application worker.".into());
        }
        Ok(())
    }

    fn check_file_version(&self, path: &Path, numeric_version: &str) -> Result<()> {
        files::no_reparse(path)?;
        let actual = file_version(path).and_then(|v| parse_version(&v));
        let expected = parse_version(numeric_version);
        if actual.is_none() || actual != expected {
            return Err("An update executable does not carry the expected release file version.".into());
        }
        Ok(())
    }

    fn read_registration(&self, context: &Verified) -> Result<Option<RegistrySnapshot>> {
        if context.data.standalone { Ok(None) } else { read_owned_registration(&context.target_directory()).map(Some) }
    }

    fn restore_registration(&self, original: &RegistrySnapshot, expected_current: Option<&RegistrySnapshot>) -> Result<()> {
        if original.key != UNINSTALL_KEY || Some(&read_owned_registration(Path::new(&original.target_directory))?) != expected_current {
            return Err("Owned uninstall registration changed before rollback.".into());
        }
        let key = open_key(UNINSTALL_KEY, true).ok_or("The owned uninstall registration is no longer present.")?;
        let current = snapshot_values(&key)?;
        for value in &current {
            if !original.values.iter().any(|v| v.name == value.name) {
                let name = wide(&value.name);
                // SAFETY: valid key and name.
                unsafe {
                    let _ = RegDeleteValueW(key.0, PCWSTR(name.as_ptr()));
                }
            }
        }
        for value in &original.values {
            set_value(&key, value)?;
        }
        // SAFETY: valid key.
        unsafe {
            let _ = RegFlushKey(key.0);
        }
        Ok(())
    }

    fn register_recovery(&self, context: &Verified, attempt: i32) -> Result<()> {
        if attempt >= 3 {
            return Ok(());
        }
        let command = recovery_command(context)?;
        if command.chars().count() > 260 {
            return Err("This update recovery command exceeds the Windows RunOnce limit. Use manual Setup for this path.".into());
        }
        let path = wide(RUN_ONCE_KEY);
        let mut key = HKEY::default();
        // SAFETY: valid name and out pointer.
        let status = unsafe { RegCreateKeyExW(HKEY_CURRENT_USER, PCWSTR(path.as_ptr()), Some(0), PCWSTR::null(), REG_OPTION_NON_VOLATILE, KEY_READ | KEY_WRITE, None, &mut key, None) };
        if status != ERROR_SUCCESS {
            return Err("The update recovery registration could not be written.".into());
        }
        let key = Key(key);
        let value = RegistryValue { name: format!("!VineMix.Update.{}.{}", context.data.id, attempt + 1), kind: REG_SZ.0 as i32, strings: vec![command], bytes: Vec::new(), number: 0 };
        set_value(&key, &value)?;
        // SAFETY: valid key.
        unsafe {
            let _ = RegFlushKey(key.0);
        }
        Ok(())
    }

    fn clear_recovery(&self, context: &Verified) -> Result<()> {
        let Some(key) = open_key(RUN_ONCE_KEY, true) else { return Ok(()) };
        let command = recovery_command(context)?;
        let values = snapshot_values(&key).unwrap_or_default();
        for attempt in 1..=3 {
            let name = format!("!VineMix.Update.{}.{attempt}", context.data.id);
            if values.iter().any(|v| v.name == name && v.strings.first() == Some(&command)) {
                let wide_name = wide(&name);
                // SAFETY: valid key and name.
                let status = unsafe { RegDeleteValueW(key.0, PCWSTR(wide_name.as_ptr())) };
                if status != ERROR_SUCCESS && status != ERROR_FILE_NOT_FOUND {
                    return Err("The update recovery registration could not be cleared.".into());
                }
            }
        }
        // SAFETY: valid key.
        unsafe {
            let _ = RegFlushKey(key.0);
        }
        Ok(())
    }

    fn run_setup(&self, context: &Verified, started: &mut dyn FnMut(ProcessIdentity) -> Result<()>) -> Result<i32> {
        read_owned_registration(&context.target_directory())?;
        files::no_reparse(&context.directory.join("setup.log"))?;
        let arguments: Vec<String> = [
            "/VERYSILENT".to_owned(),
            "/SUPPRESSMSGBOXES".to_owned(),
            "/NORESTART".to_owned(),
            "/ACCEPTTERMS=1".to_owned(),
            "/DRIVERS=none".to_owned(),
            format!("/DIR={}", context.target_directory().display()),
            format!("/LOG={}", context.directory.join("setup.log").display()),
        ]
        .into();
        let process = start(&context.asset_path(), &arguments, &context.directory).map_err(|e| format!("The verified Setup could not start. {e}"))?;
        if let Some(identity) = capture_started(&process, &context.asset_path())? {
            started(identity)?;
        }
        // Setup is never killed; a timeout leaves its identity journaled.
        if !process.wait(Duration::from_secs(10 * 60)) {
            return Err("Setup is still running. Its recovery journal was retained; no process was killed.".into());
        }
        Ok(process.exit_code().map_or(-1, |code| code as i32))
    }

    fn run_smoke(&self, context: &Verified, evidence: &Path, started: &mut dyn FnMut(ProcessIdentity) -> Result<()>) -> Result<bool> {
        let arguments = vec!["--release-smoke".to_owned(), evidence.to_string_lossy().into_owned()];
        let process = start(&context.target_path(), &arguments, &context.target_directory()).map_err(|e| format!("The candidate package check could not start. {e}"))?;
        if let Some(identity) = capture_started(&process, &context.target_path())? {
            started(identity)?;
        }
        if !process.wait(Duration::from_secs(60)) {
            return Err("The inert candidate check is still running. It was not stopped; recovery was retained.".into());
        }
        if process.exit_code() != Some(0) {
            return Ok(false);
        }
        let report: serde_json::Value =
            serde_json::from_slice(&files::read_bytes(&evidence.join("results.json"), 128 * 1024)?).map_err(|_| "The candidate package check report is invalid.".to_owned())?;
        let mode = if context.data.standalone { "Standalone" } else { "Installed" };
        Ok(report.get("passed").and_then(serde_json::Value::as_bool) == Some(true)
            && report.get("version").and_then(serde_json::Value::as_str) == Some(context.data.version.as_str())
            && report.get("mode").and_then(serde_json::Value::as_str) == Some(mode))
    }

    fn candidate_engine_hash(&self, context: &Verified, evidence: &Path) -> Result<String> {
        if !context.data.standalone {
            return files::hash(&context.target_directory().join("VineMix.Engine.exe"));
        }
        let report: serde_json::Value =
            serde_json::from_slice(&files::read_bytes(&evidence.join("results.json"), 128 * 1024)?).map_err(|_| "The candidate package check report is invalid.".to_owned())?;
        let engine = report.get("extractedEngine").and_then(serde_json::Value::as_str).ok_or("The verified candidate runtime evidence has no engine path.")?;
        let engine = PathBuf::from(engine);
        if !files::is_inside(&engine, evidence) || engine.file_name().is_none_or(|n| n != "VineMix.Engine.exe") {
            return Err("The verified engine evidence escapes its private extraction folder.".into());
        }
        self.check_file_version(&engine, &context.offer.manifest.numeric_version)?;
        files::hash(&engine)
    }

    fn start_candidate(&self, context: &Verified) -> Result<ProcessIdentity> {
        let mut arguments = vec!["--update-health".to_owned(), context.transaction_path().to_string_lossy().into_owned(), "--standalone-context".to_owned()];
        if let Some(settings) = &context.data.settings_path {
            arguments.push("--settings".into());
            arguments.push(settings.clone());
        }
        let process = start(&context.target_path(), &arguments, &context.target_directory()).map_err(|e| format!("The updated application could not start. {e}"))?;
        capture_started(&process, &context.target_path())?.ok_or_else(|| "The updated application exited before it could confirm startup health.".into())
    }

    fn reopen_previous(&self, context: &Verified) -> Result<()> {
        if transaction::read_journal(context)?.phase != super::model::Phase::RolledBack || !files::equal_hash(&files::hash(&context.target_path())?, &context.data.parent.sha256) {
            return Err("The restored launcher no longer matches its original application identity.".into());
        }
        self.check_file_version(&context.target_path(), vinemix_platform::release::NUMERIC_VERSION)?;
        let settings = context.data.settings_path.clone().filter(|s| !s.is_empty()).ok_or("The prior workspace path is unavailable. Reopen the original launcher manually.")?;
        let arguments = vec![
            "--update-reverted".to_owned(),
            context.transaction_path().to_string_lossy().into_owned(),
            "--settings".to_owned(),
            settings,
            "--standalone-context".to_owned(),
        ];
        start(&context.target_path(), &arguments, &context.target_directory()).map(|_| ()).map_err(|e| format!("The restored application could not start. {e}"))
    }

    fn wait_for_candidate_audio_exit(&self, context: &Verified, expected_engine_sha256: Option<&str>, timeout: Duration) -> Result<bool> {
        let Some(expected) = expected_engine_sha256 else { return Ok(true) };
        let recorded = context.directory.join("candidate-engine.json");
        if recorded.exists() {
            files::require_private(&recorded)?;
            let signal: super::model::EngineSignal = files::read_json(&recorded, 32768)?;
            if signal.token != context.data.token || !files::equal_hash(&signal.envelope_sha256, &context.data.envelope_sha256) || !files::equal_hash(&signal.identity.sha256, expected) {
                return Err("The candidate engine recovery identity is invalid.".into());
            }
            if !self.wait_for_exit(&signal.identity, timeout)? {
                return Ok(false);
            }
        }
        let session = crate::platform::current_session_id();
        let started = Instant::now();
        while started.elapsed() < timeout {
            let mut found = false;
            for name in ["VineMix.Engine.exe", "AudioMixer.Engine.exe"] {
                for pid in crate::platform::processes_named(name) {
                    if crate::platform::session_of(pid) != Some(session) {
                        continue;
                    }
                    if let Ok(identity) = ProcessIdentity::capture(pid) {
                        if files::equal_hash(&identity.sha256, expected) {
                            found = true;
                        }
                    }
                }
            }
            if !found {
                return Ok(true);
            }
            std::thread::sleep(Duration::from_millis(100));
        }
        Ok(false)
    }

    fn delay(&self, milliseconds: u64) {
        std::thread::sleep(Duration::from_millis(milliseconds));
    }
}

fn recovery_command(context: &Verified) -> Result<String> {
    Ok(format!("{} --apply-update {}", quote(&context.worker_path().to_string_lossy())?, quote(&context.transaction_path().to_string_lossy())?))
}
