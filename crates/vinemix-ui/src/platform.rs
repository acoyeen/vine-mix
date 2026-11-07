//! Win32 services for the desktop: identities, processes, named session
//! objects, the start-at-login registration and shell integration.

use std::ffi::OsStr;
use std::os::windows::ffi::OsStrExt;
use std::path::{Path, PathBuf};

use windows::core::{PCWSTR, PWSTR};
use windows::Win32::Foundation::{
    CloseHandle, GetLastError, ERROR_ALREADY_EXISTS, ERROR_FILE_NOT_FOUND, ERROR_SUCCESS, FILETIME, HANDLE, HWND, WAIT_OBJECT_0,
    WAIT_TIMEOUT,
};
use windows::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS,
};
use windows::Win32::System::Registry::{
    RegCloseKey, RegCreateKeyExW, RegDeleteValueW, RegQueryValueExW, RegSetValueExW, HKEY, HKEY_CURRENT_USER, KEY_QUERY_VALUE,
    KEY_SET_VALUE, REG_OPTION_NON_VOLATILE, REG_SZ, REG_VALUE_TYPE,
};
use windows::Win32::System::RemoteDesktop::ProcessIdToSessionId;
use windows::Win32::System::Threading::{
    CreateEventW, CreateMutexW, CreateProcessW, GetCurrentProcessId, GetExitCodeProcess, GetProcessTimes, OpenEventW, OpenProcess,
    QueryFullProcessImageNameW, ReleaseMutex, SetEvent, TerminateProcess, WaitForSingleObject, CREATE_NO_WINDOW, CREATE_UNICODE_ENVIRONMENT,
    EVENT_MODIFY_STATE, PROCESS_INFORMATION, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SYNCHRONIZE,
    PROCESS_TERMINATE, STARTUPINFOW,
};

pub fn wide(text: &str) -> Vec<u16> {
    vinemix_platform::wide(text)
}

pub fn wide_os(text: &OsStr) -> Vec<u16> {
    text.encode_wide().chain(std::iter::once(0)).collect()
}

/// A new random 32-hex-digit identity, formatted like .NET `Guid.ToString("N")`.
pub fn random_hex_id() -> String {
    // SAFETY: CoCreateGuid has no preconditions.
    match unsafe { windows::Win32::System::Com::CoCreateGuid() } {
        Ok(guid) => {
            let mut text = format!("{:08x}{:04x}{:04x}", guid.data1, guid.data2, guid.data3);
            for byte in guid.data4 {
                text += &format!("{byte:02x}");
            }
            text
        }
        Err(_) => {
            // CoCreateGuid cannot fail on supported Windows; keep a unique
            // fallback from the clock and process identity regardless.
            let nanos = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_nanos()).unwrap_or(0);
            format!("{:032x}", nanos ^ (u128::from(std::process::id()) << 96))
        }
    }
}

#[derive(Debug)]
pub struct OwnedHandle(pub HANDLE);

impl Drop for OwnedHandle {
    fn drop(&mut self) {
        if !self.0.is_invalid() && !self.0 .0.is_null() {
            // SAFETY: owned handle.
            unsafe {
                let _ = CloseHandle(self.0);
            }
        }
    }
}

// SAFETY: kernel handles are process-wide references.
unsafe impl Send for OwnedHandle {}
// SAFETY: as above; the wrapped calls are thread-safe.
unsafe impl Sync for OwnedHandle {}

pub fn current_session_id() -> u32 {
    session_of(std::process::id()).unwrap_or(u32::MAX)
}

pub fn session_of(pid: u32) -> Option<u32> {
    let mut session = 0u32;
    // SAFETY: valid out pointer.
    unsafe { ProcessIdToSessionId(pid, &mut session) }.ok().map(|()| session)
}

pub fn current_process_id() -> u32 {
    // SAFETY: trivial Win32 call.
    unsafe { GetCurrentProcessId() }
}

/// A process opened for identity checks, waiting and termination.
#[derive(Debug)]
pub struct Process {
    handle: OwnedHandle,
    pid: u32,
}

impl Process {
    pub fn open(pid: u32) -> Option<Self> {
        // SAFETY: plain open; the handle is owned by the result.
        let handle = unsafe {
            OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SYNCHRONIZE | PROCESS_TERMINATE, false, pid)
                .or_else(|_| OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SYNCHRONIZE, false, pid))
        }
        .ok()?;
        Some(Self { handle: OwnedHandle(handle), pid })
    }

    pub fn id(&self) -> u32 {
        self.pid
    }

    pub fn image_path(&self) -> Option<PathBuf> {
        let mut buffer = vec![0u16; 32768];
        let mut size = buffer.len() as u32;
        // SAFETY: valid handle and buffer.
        unsafe { QueryFullProcessImageNameW(self.handle.0, PROCESS_NAME_WIN32, PWSTR(buffer.as_mut_ptr()), &mut size) }.ok()?;
        Some(vinemix_platform::path_from_wide(&buffer[..size as usize]))
    }

    pub fn session(&self) -> Option<u32> {
        session_of(self.pid)
    }

    /// Creation time in 100 ns units: with the PID, a process identity that
    /// cannot be confused with a later process reusing the same PID.
    pub fn start_time(&self) -> Option<u64> {
        let (mut created, mut exited, mut kernel, mut user) = (FILETIME::default(), FILETIME::default(), FILETIME::default(), FILETIME::default());
        // SAFETY: valid handle and out pointers.
        unsafe { GetProcessTimes(self.handle.0, &mut created, &mut exited, &mut kernel, &mut user) }.ok()?;
        Some((u64::from(created.dwHighDateTime) << 32) | u64::from(created.dwLowDateTime))
    }

    pub fn has_exited(&self) -> bool {
        // SAFETY: valid handle.
        (unsafe { WaitForSingleObject(self.handle.0, 0) }) == WAIT_OBJECT_0
    }

    pub fn exit_code(&self) -> Option<u32> {
        let mut code = 0u32;
        // SAFETY: valid handle.
        unsafe { GetExitCodeProcess(self.handle.0, &mut code) }.ok().map(|()| code)
    }

    /// True when the process exited within the timeout.
    pub fn wait(&self, timeout: std::time::Duration) -> bool {
        let milliseconds = u32::try_from(timeout.as_millis()).unwrap_or(u32::MAX - 1);
        // SAFETY: valid handle.
        let result = unsafe { WaitForSingleObject(self.handle.0, milliseconds) };
        result == WAIT_OBJECT_0 || (result != WAIT_TIMEOUT && self.has_exited())
    }

    pub fn kill(&self) -> Result<(), String> {
        // SAFETY: valid handle opened with PROCESS_TERMINATE when permitted.
        unsafe { TerminateProcess(self.handle.0, 1) }.map_err(|e| e.message())
    }
}

/// Process IDs whose executable file name matches `name` (e.g. `VineMix.Engine.exe`).
pub fn processes_named(name: &str) -> Vec<u32> {
    let mut found = Vec::new();
    // SAFETY: snapshot handle owned below.
    let Ok(snapshot) = (unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) }) else { return found };
    let snapshot = OwnedHandle(snapshot);
    let mut entry = PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
    // SAFETY: valid snapshot and entry.
    let mut more = unsafe { Process32FirstW(snapshot.0, &mut entry) }.is_ok();
    while more {
        let length = entry.szExeFile.iter().position(|&c| c == 0).unwrap_or(entry.szExeFile.len());
        let exe = String::from_utf16_lossy(&entry.szExeFile[..length]);
        if exe.eq_ignore_ascii_case(name) {
            found.push(entry.th32ProcessID);
        }
        // SAFETY: as above.
        more = unsafe { Process32NextW(snapshot.0, &mut entry) }.is_ok();
    }
    found
}

/// Windows command-line quoting (`CommandLineToArgvW` rules).
pub fn quote_argument(value: &str) -> String {
    let mut quoted = String::from("\"");
    let mut backslashes = 0usize;
    for character in value.chars() {
        if character == '\\' {
            backslashes += 1;
            continue;
        }
        if character == '"' {
            quoted.push_str(&"\\".repeat(backslashes * 2 + 1));
            quoted.push('"');
        } else {
            quoted.push_str(&"\\".repeat(backslashes));
            quoted.push(character);
        }
        backslashes = 0;
    }
    quoted.push_str(&"\\".repeat(backslashes * 2));
    quoted.push('"');
    quoted
}

/// Starts `executable` without a console window.
pub fn launch(executable: &Path, arguments: &[String], working_directory: &Path) -> Result<Process, String> {
    let mut command = quote_argument(&executable.to_string_lossy());
    for argument in arguments {
        command.push(' ');
        command.push_str(&quote_argument(argument));
    }
    if command.encode_utf16().count() >= 32767 {
        return Err("The launch arguments are too long.".into());
    }
    let application = wide_os(executable.as_os_str());
    let mut command_line = wide(&command);
    let directory = wide_os(working_directory.as_os_str());
    let startup = STARTUPINFOW { cb: std::mem::size_of::<STARTUPINFOW>() as u32, ..Default::default() };
    let mut information = PROCESS_INFORMATION::default();
    // SAFETY: all buffers are NUL-terminated and outlive the call.
    unsafe {
        CreateProcessW(
            PCWSTR(application.as_ptr()),
            Some(PWSTR(command_line.as_mut_ptr())),
            None,
            None,
            false,
            CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
            None,
            PCWSTR(directory.as_ptr()),
            &startup,
            &mut information,
        )
    }
    .map_err(|e| e.message())?;
    let _thread = OwnedHandle(information.hThread);
    Ok(Process { handle: OwnedHandle(information.hProcess), pid: information.dwProcessId })
}

/// Relaunches with `--standalone-context` as a child of the desktop shell
/// (`StandaloneLaunch.StartProcess`), so this process and its engine inherit
/// the ordinary per-user file-system view instead of an inherited AppData
/// redirection.
pub fn relaunch_from_shell(executable: &Path, arguments: &[String], working_directory: &Path) -> Result<(), String> {
    use windows::Win32::System::Threading::{
        DeleteProcThreadAttributeList, InitializeProcThreadAttributeList, UpdateProcThreadAttribute, EXTENDED_STARTUPINFO_PRESENT, LPPROC_THREAD_ATTRIBUTE_LIST,
        PROCESS_CREATE_PROCESS, PROC_THREAD_ATTRIBUTE_PARENT_PROCESS, STARTUPINFOEXW,
    };
    use windows::Win32::UI::WindowsAndMessaging::{GetShellWindow, GetWindowThreadProcessId};
    let path = std::path::absolute(executable).map_err(|e| e.to_string())?;
    let mut command = quote_argument(&path.to_string_lossy());
    for argument in arguments {
        command.push(' ');
        command.push_str(&quote_argument(argument));
    }
    if command.encode_utf16().count() >= 32767 {
        return Err("The launch arguments are too long.".into());
    }
    // SAFETY: plain query of the desktop shell window.
    let shell = unsafe { GetShellWindow() };
    if shell.0.is_null() {
        return Err("The Windows desktop shell is not available.".into());
    }
    let mut shell_pid = 0u32;
    // SAFETY: valid window and out pointer.
    if unsafe { GetWindowThreadProcessId(shell, Some(&mut shell_pid)) } == 0 {
        return Err("The Windows desktop shell could not be identified.".into());
    }
    if session_of(shell_pid) != Some(current_session_id()) {
        return Err("The Windows desktop shell belongs to another session.".into());
    }
    // SAFETY: opened for use as the new process's parent; closed below.
    let parent = OwnedHandle(unsafe { OpenProcess(PROCESS_CREATE_PROCESS, false, shell_pid) }.map_err(|e| e.message())?);
    let mut size = 0usize;
    // SAFETY: size query.
    let _ = unsafe { InitializeProcThreadAttributeList(None, 1, None, &mut size) };
    if size == 0 {
        return Err("The launch attributes could not be prepared.".into());
    }
    let mut buffer = vec![0u8; size];
    let list = LPPROC_THREAD_ATTRIBUTE_LIST(buffer.as_mut_ptr().cast());
    // SAFETY: buffer sized as requested.
    unsafe { InitializeProcThreadAttributeList(Some(list), 1, None, &mut size) }.map_err(|e| e.message())?;
    let parent_value = parent.0;
    let result = (|| -> Result<(), String> {
        // SAFETY: the attribute value (a HANDLE) outlives CreateProcessW.
        unsafe {
            UpdateProcThreadAttribute(
                list,
                0,
                PROC_THREAD_ATTRIBUTE_PARENT_PROCESS as usize,
                Some((&parent_value as *const HANDLE).cast()),
                std::mem::size_of::<HANDLE>(),
                None,
                None,
            )
        }
        .map_err(|e| e.message())?;
        let mut startup = STARTUPINFOEXW::default();
        startup.StartupInfo.cb = std::mem::size_of::<STARTUPINFOEXW>() as u32;
        startup.lpAttributeList = list;
        let application = wide_os(path.as_os_str());
        let mut command_line = wide(&command);
        let directory = wide_os(std::path::absolute(working_directory).unwrap_or_else(|_| working_directory.to_path_buf()).as_os_str());
        let mut information = PROCESS_INFORMATION::default();
        // SAFETY: all buffers are NUL-terminated and outlive the call.
        unsafe {
            CreateProcessW(
                PCWSTR(application.as_ptr()),
                Some(PWSTR(command_line.as_mut_ptr())),
                None,
                None,
                false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                None,
                PCWSTR(directory.as_ptr()),
                &startup.StartupInfo,
                &mut information,
            )
        }
        .map_err(|e| e.message())?;
        drop(OwnedHandle(information.hThread));
        drop(OwnedHandle(information.hProcess));
        Ok(())
    })();
    // SAFETY: initialized above.
    unsafe { DeleteProcThreadAttributeList(list) };
    result
}

/// Named per-session ownership for a role, held for both product names so
/// VineMix and the older Audio Mixer exclude each other (`SessionOwnership`).
#[derive(Debug, Default)]
pub struct SessionOwnership {
    locks: Vec<OwnedHandle>,
}

impl SessionOwnership {
    pub fn acquire(role: &str, scope: &str) -> Option<Self> {
        let mut ownership = Self::default();
        for product in ["AudioMixer", "VineMix"] {
            let name = wide(&format!(r"Local\{product}.{role}.{scope}"));
            // SAFETY: valid name; the handle is owned by `ownership`.
            unsafe {
                windows::Win32::Foundation::SetLastError(ERROR_SUCCESS);
                let Ok(handle) = CreateMutexW(None, true, PCWSTR(name.as_ptr())) else { return None };
                if GetLastError() == ERROR_ALREADY_EXISTS {
                    let _ = CloseHandle(handle);
                    return None;
                }
                ownership.locks.push(OwnedHandle(handle));
            }
        }
        Some(ownership)
    }
}

impl Drop for SessionOwnership {
    fn drop(&mut self) {
        for lock in self.locks.iter().rev() {
            // SAFETY: owned mutex acquired by this thread at creation.
            unsafe {
                let _ = ReleaseMutex(lock.0);
            }
        }
    }
}

/// A named auto-reset event other launches use to show the running window.
#[derive(Debug)]
pub struct NamedEvent(OwnedHandle);

impl NamedEvent {
    pub fn create(name: &str) -> Option<Self> {
        let name = wide(name);
        // SAFETY: valid name; owned handle.
        unsafe { CreateEventW(None, false, false, PCWSTR(name.as_ptr())) }.ok().map(|h| Self(OwnedHandle(h)))
    }

    /// Signals an existing event; false when nobody created it.
    pub fn signal_existing(name: &str) -> bool {
        let name = wide(name);
        // SAFETY: valid name; the handle is closed immediately.
        match unsafe { OpenEventW(EVENT_MODIFY_STATE, false, PCWSTR(name.as_ptr())) } {
            Ok(handle) => {
                let handle = OwnedHandle(handle);
                // SAFETY: valid event handle.
                unsafe { SetEvent(handle.0) }.is_ok()
            }
            Err(_) => false,
        }
    }

    /// True when signalled within the timeout.
    pub fn wait(&self, timeout: std::time::Duration) -> bool {
        let milliseconds = u32::try_from(timeout.as_millis()).unwrap_or(u32::MAX - 1);
        // SAFETY: valid handle.
        (unsafe { WaitForSingleObject(self.0 .0, milliseconds) }) == WAIT_OBJECT_0
    }
}

/// `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
pub mod run_key {
    use super::*;

    const RUN: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";

    struct Key(HKEY);

    impl Drop for Key {
        fn drop(&mut self) {
            // SAFETY: owned key.
            unsafe {
                let _ = RegCloseKey(self.0);
            }
        }
    }

    fn open() -> Result<Key, String> {
        let path = wide(RUN);
        let mut key = HKEY::default();
        // SAFETY: valid path; key closed by `Key`.
        let status = unsafe {
            RegCreateKeyExW(
                HKEY_CURRENT_USER,
                PCWSTR(path.as_ptr()),
                None,
                PCWSTR::null(),
                REG_OPTION_NON_VOLATILE,
                KEY_QUERY_VALUE | KEY_SET_VALUE,
                None,
                &mut key,
                None,
            )
        };
        if status != ERROR_SUCCESS {
            return Err(format!("Windows registry access failed ({}).", status.0));
        }
        Ok(Key(key))
    }

    pub fn get(name: &str) -> Result<Option<String>, String> {
        let key = open()?;
        let name = wide(name);
        let mut kind = REG_VALUE_TYPE::default();
        let mut size = 0u32;
        // SAFETY: size query.
        let status = unsafe { RegQueryValueExW(key.0, PCWSTR(name.as_ptr()), None, Some(&mut kind), None, Some(&mut size)) };
        if status == ERROR_FILE_NOT_FOUND {
            return Ok(None);
        }
        if status != ERROR_SUCCESS {
            return Err(format!("Windows registry access failed ({}).", status.0));
        }
        let mut buffer = vec![0u16; (size as usize).div_ceil(2) + 1];
        let mut bytes = (buffer.len() * 2) as u32;
        // SAFETY: buffer sized from the query above.
        let status = unsafe {
            RegQueryValueExW(key.0, PCWSTR(name.as_ptr()), None, Some(&mut kind), Some(buffer.as_mut_ptr().cast()), Some(&mut bytes))
        };
        if status != ERROR_SUCCESS {
            return Err(format!("Windows registry access failed ({}).", status.0));
        }
        if kind != REG_SZ && kind != windows::Win32::System::Registry::REG_EXPAND_SZ {
            return Ok(Some(String::new()));
        }
        let units = (bytes as usize / 2).min(buffer.len());
        let end = buffer[..units].iter().position(|&c| c == 0).unwrap_or(units);
        Ok(Some(String::from_utf16_lossy(&buffer[..end])))
    }

    pub fn set(name: &str, value: &str) -> Result<(), String> {
        let key = open()?;
        let name = wide(name);
        let data: Vec<u8> = wide(value).iter().flat_map(|unit| unit.to_le_bytes()).collect();
        // SAFETY: valid key, name and data.
        let status = unsafe { RegSetValueExW(key.0, PCWSTR(name.as_ptr()), None, REG_SZ, Some(&data)) };
        if status != ERROR_SUCCESS {
            return Err(format!("Windows registry access failed ({}).", status.0));
        }
        Ok(())
    }

    pub fn delete(name: &str) -> Result<(), String> {
        let key = open()?;
        let name = wide(name);
        // SAFETY: valid key and name.
        let status = unsafe { RegDeleteValueW(key.0, PCWSTR(name.as_ptr())) };
        if status != ERROR_SUCCESS && status != ERROR_FILE_NOT_FOUND {
            return Err(format!("Windows registry access failed ({}).", status.0));
        }
        Ok(())
    }
}

/// Start at login (`StartupRegistration`): `"<exe>" --background` under
/// the `VineMix` value. Entries owned by another copy stay authoritative.
pub mod startup {
    use super::run_key;

    pub fn owns(command: Option<&str>, executable: &str) -> bool {
        let Some(command) = command else { return false };
        let full = std::path::absolute(executable).map(|p| p.to_string_lossy().into_owned()).unwrap_or_else(|_| executable.to_owned());
        crate::text::eq_ignore_case(command.trim(), &format!("\"{full}\" --background"))
    }

    pub fn set_enabled(enabled: bool) -> Result<(), String> {
        if vinemix_platform::development_instance().is_some() {
            // Development instances never own the per-user Run entry.
            return Ok(());
        }
        let executable = vinemix_platform::current_executable();
        if enabled {
            let current = run_key::get("VineMix")?;
            let legacy = run_key::get("AudioMixer")?;
            if legacy.is_some() && !owns(legacy.as_deref(), &executable) {
                return Err("An older VineMix copy is registered to start at login. Disable that copy's startup setting before enabling this copy.".into());
            }
            if current.is_some() && !owns(current.as_deref(), &executable) {
                return Err("Start at login belongs to another VineMix copy. Change it from that copy before switching launchers.".into());
            }
            run_key::set("VineMix", &format!("\"{executable}\" --background"))?;
        } else if owns(run_key::get("VineMix")?.as_deref(), &executable) {
            run_key::delete("VineMix")?;
        }
        if owns(run_key::get("AudioMixer")?.as_deref(), &executable) {
            run_key::delete("AudioMixer")?;
        }
        Ok(())
    }
}

/// Opens a URI, folder or file with its registered handler.
pub fn shell_open(target: &str) -> Result<(), String> {
    let operation = wide("open");
    let file = wide(target);
    // SAFETY: valid NUL-terminated strings.
    let result = unsafe {
        windows::Win32::UI::Shell::ShellExecuteW(
            None,
            PCWSTR(operation.as_ptr()),
            PCWSTR(file.as_ptr()),
            PCWSTR::null(),
            PCWSTR::null(),
            windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL,
        )
    };
    if result.0 as usize > 32 { Ok(()) } else { Err(format!("Windows could not open {target}.")) }
}

/// Reveals a file in Explorer, selecting it.
pub fn reveal_in_explorer(path: &Path) -> Result<(), String> {
    let explorer = PathBuf::from(std::env::var_os("WINDIR").unwrap_or_else(|| "C:\\Windows".into())).join("explorer.exe");
    let argument = format!("/select,{}", path.display());
    launch(&explorer, &[argument], path.parent().unwrap_or(Path::new("."))).map(|_| ())
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum MessageKind {
    Information,
    Warning,
}

/// A blocking native message box for failures before the window exists.
pub fn message_box(title: &str, text: &str, kind: MessageKind) {
    use windows::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONINFORMATION, MB_ICONWARNING, MB_OK};
    let title = wide(title);
    let text = wide(text);
    let icon = if kind == MessageKind::Warning { MB_ICONWARNING } else { MB_ICONINFORMATION };
    // SAFETY: valid strings; no owner window.
    unsafe {
        MessageBoxW(Some(HWND::default()), PCWSTR(text.as_ptr()), PCWSTR(title.as_ptr()), MB_OK | icon);
    }
}

/// Whether Windows client-area animations are enabled
/// (`SystemParameters.ClientAreaAnimation`).
pub fn client_area_animation() -> bool {
    use windows::Win32::UI::WindowsAndMessaging::{SystemParametersInfoW, SPI_GETCLIENTAREAANIMATION, SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS};
    let mut enabled = windows::core::BOOL(1);
    // SAFETY: valid out pointer for a BOOL-valued query.
    let ok = unsafe {
        SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, Some(std::ptr::from_mut(&mut enabled).cast()), SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS(0))
    };
    ok.is_err() || enabled.as_bool()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn quoting_and_ids() {
        assert_eq!(quote_argument(r"C:\a b\"), r#""C:\a b\\""#);
        assert_eq!(quote_argument(r#"x"y"#), r#""x\"y""#);
        let id = random_hex_id();
        assert_eq!(id.len(), 32);
        assert!(id.bytes().all(|b| b.is_ascii_hexdigit()));
        assert_ne!(id, random_hex_id());
    }
}

/// The Windows clipboard, for the WPF-compatible custom node-group format
/// (a NUL-terminated UTF-16 string in global memory).
pub mod clipboard {
    use super::*;
    use windows::Win32::Foundation::{HANDLE, HGLOBAL};
    use windows::Win32::System::DataExchange::{
        CloseClipboard, EmptyClipboard, GetClipboardData, IsClipboardFormatAvailable, OpenClipboard, RegisterClipboardFormatW, SetClipboardData,
    };
    use windows::Win32::Foundation::GlobalFree;
    use windows::Win32::System::Memory::{GlobalAlloc, GlobalLock, GlobalSize, GlobalUnlock, GMEM_MOVEABLE};

    fn format(name: &str) -> Option<u32> {
        let name = wide(name);
        // SAFETY: valid NUL-terminated name.
        let id = unsafe { RegisterClipboardFormatW(PCWSTR(name.as_ptr())) };
        (id != 0).then_some(id)
    }

    struct Open;

    impl Open {
        fn new() -> Option<Self> {
            for _ in 0..10 {
                // SAFETY: no owner window; closed by Drop.
                if unsafe { OpenClipboard(None) }.is_ok() {
                    return Some(Self);
                }
                std::thread::sleep(std::time::Duration::from_millis(10));
            }
            None
        }
    }

    impl Drop for Open {
        fn drop(&mut self) {
            // SAFETY: opened by `new`.
            unsafe {
                let _ = CloseClipboard();
            }
        }
    }

    pub fn has(name: &str) -> bool {
        // SAFETY: plain query.
        format(name).is_some_and(|f| unsafe { IsClipboardFormatAvailable(f) }.is_ok())
    }

    pub fn set(name: &str, text: &str) -> Result<(), String> {
        let id = format(name).ok_or("The clipboard is busy.")?;
        let units: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
        let _open = Open::new().ok_or("The clipboard is busy.")?;
        // SAFETY: the clipboard is open; ownership of the global block passes
        // to the system on success and is freed here on failure.
        unsafe {
            EmptyClipboard().map_err(|_| "The clipboard is busy.")?;
            let memory = GlobalAlloc(GMEM_MOVEABLE, units.len() * 2).map_err(|_| "The clipboard is busy.")?;
            let target = GlobalLock(memory).cast::<u16>();
            if target.is_null() {
                let _ = GlobalFree(Some(memory));
                return Err("The clipboard is busy.".into());
            }
            std::ptr::copy_nonoverlapping(units.as_ptr(), target, units.len());
            let _ = GlobalUnlock(memory);
            if SetClipboardData(id, Some(HANDLE(memory.0))).is_err() {
                let _ = GlobalFree(Some(memory));
                return Err("The clipboard is busy.".into());
            }
        }
        Ok(())
    }

    pub fn get(name: &str) -> Option<String> {
        let id = format(name)?;
        let _open = Open::new()?;
        // SAFETY: the clipboard is open; the handle stays owned by the system.
        unsafe {
            let handle = GetClipboardData(id).ok()?;
            let memory = HGLOBAL(handle.0);
            let size = GlobalSize(memory) / 2;
            let source = GlobalLock(memory).cast::<u16>();
            if source.is_null() {
                return None;
            }
            let units = std::slice::from_raw_parts(source, size);
            let end = units.iter().position(|&u| u == 0).unwrap_or(units.len());
            let text = String::from_utf16_lossy(&units[..end]);
            let _ = GlobalUnlock(memory);
            Some(text)
        }
    }
}

/// A native Save dialog; returns the chosen path. Existing files are not
/// offered for overwrite.
pub fn save_file_dialog(title: &str, file_name: &str, filter_name: &str, pattern: &str) -> Option<PathBuf> {
    use windows::Win32::System::Com::{CoCreateInstance, CoInitializeEx, CoTaskMemFree, CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED};
    use windows::Win32::UI::Shell::Common::COMDLG_FILTERSPEC;
    use windows::Win32::UI::Shell::{FileSaveDialog, IFileSaveDialog, FOS_NOREADONLYRETURN, FOS_OVERWRITEPROMPT, FOS_STRICTFILETYPES, SIGDN_FILESYSPATH};
    // SAFETY: COM calls on this thread; strings outlive the calls.
    unsafe {
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
        let dialog: IFileSaveDialog = CoCreateInstance(&FileSaveDialog, None, CLSCTX_INPROC_SERVER).ok()?;
        let title = wide(title);
        let name = wide(file_name);
        let filter_name = wide(filter_name);
        let pattern_w = wide(pattern);
        let extension = wide(pattern.trim_start_matches("*."));
        let _ = dialog.SetTitle(PCWSTR(title.as_ptr()));
        let _ = dialog.SetFileName(PCWSTR(name.as_ptr()));
        let _ = dialog.SetDefaultExtension(PCWSTR(extension.as_ptr()));
        let filters = [COMDLG_FILTERSPEC { pszName: PCWSTR(filter_name.as_ptr()), pszSpec: PCWSTR(pattern_w.as_ptr()) }];
        let _ = dialog.SetFileTypes(&filters);
        if let Ok(options) = dialog.GetOptions() {
            let _ = dialog.SetOptions((options | FOS_STRICTFILETYPES | FOS_NOREADONLYRETURN) & !FOS_OVERWRITEPROMPT);
        }
        dialog.Show(None).ok()?;
        let item = dialog.GetResult().ok()?;
        let path = item.GetDisplayName(SIGDN_FILESYSPATH).ok()?;
        let text = path.to_string().ok();
        CoTaskMemFree(Some(path.0.cast()));
        text.map(PathBuf::from)
    }
}
