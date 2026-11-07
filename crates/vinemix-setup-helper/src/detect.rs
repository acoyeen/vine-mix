//! Windows evidence (`WindowsDetection`): SetupAPI devnodes including
//! nonpresent ones, published OEM INFs and their driver-store location,
//! and the current user's VineMix processes.

use std::path::Path;

use windows::core::{PCWSTR, PWSTR};
use windows::Win32::Devices::DeviceAndDriverInstallation::{
    CM_Get_DevNode_Status, SetupDiDestroyDeviceInfoList, SetupDiEnumDeviceInfo, SetupDiGetClassDevsW, SetupDiGetDeviceRegistryPropertyW,
    SetupGetInfDriverStoreLocationW, CM_DEVNODE_STATUS_FLAGS, CM_PROB, CR_SUCCESS, DIGCF_ALLCLASSES, SPDRP_DRIVER, SPDRP_HARDWAREID, SP_DEVINFO_DATA,
};
use windows::Win32::Foundation::{CloseHandle, GetLastError, ERROR_INSUFFICIENT_BUFFER, ERROR_INVALID_DATA, ERROR_INVALID_PARAMETER, ERROR_NO_MORE_ITEMS, HANDLE};
use windows::Win32::Security::{GetTokenInformation, TokenUser, TOKEN_QUERY, TOKEN_USER};
use windows::Win32::Storage::FileSystem::{CreateFileW, GetFinalPathNameByHandleW, FILE_FLAG_BACKUP_SEMANTICS, FILE_NAME_NORMALIZED, FILE_SHARE_DELETE, FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING};
use windows::Win32::System::Diagnostics::ToolHelp::{CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS};
use windows::Win32::System::Registry::{RegGetValueW, HKEY_LOCAL_MACHINE, RRF_RT_REG_SZ};
use windows::Win32::System::Threading::{OpenProcess, OpenProcessToken, QueryFullProcessImageNameW, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION};

use crate::inf::{read_inf_text, InfIdentity};
use crate::policy::{self, ApplicationResult, DeviceEvidence, Evidence, PackageEvidence, ProcessEvidence, FAMILIES};

fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

struct Handle(HANDLE);

impl Drop for Handle {
    fn drop(&mut self) {
        // SAFETY: owned handle.
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}

fn property(set: windows::Win32::Devices::DeviceAndDriverInstallation::HDEVINFO, data: &SP_DEVINFO_DATA, key: u32, succeeded: &mut bool) -> String {
    let mut required = 0u32;
    // SAFETY: size query.
    let first = unsafe { SetupDiGetDeviceRegistryPropertyW(set, data, windows::Win32::Devices::DeviceAndDriverInstallation::SETUP_DI_REGISTRY_PROPERTY(key), None, None, Some(&mut required)) };
    // SAFETY: plain query.
    let error = unsafe { GetLastError() };
    if first.is_ok() || required == 0 {
        if error != ERROR_INVALID_DATA {
            *succeeded = false;
        }
        return String::new();
    }
    if error != ERROR_INSUFFICIENT_BUFFER || required > 1024 * 1024 {
        *succeeded = false;
        return String::new();
    }
    let mut bytes = vec![0u8; required as usize];
    // SAFETY: buffer sized as reported.
    if unsafe { SetupDiGetDeviceRegistryPropertyW(set, data, windows::Win32::Devices::DeviceAndDriverInstallation::SETUP_DI_REGISTRY_PROPERTY(key), None, Some(&mut bytes), None) }.is_err() {
        *succeeded = false;
        return String::new();
    }
    let units: Vec<u16> = bytes.chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
    String::from_utf16_lossy(&units)
}

fn driver_version(registry_path: &str) -> String {
    let key = wide(&format!("SYSTEM\\CurrentControlSet\\Control\\Class\\{registry_path}"));
    let name = wide("DriverVersion");
    let mut buffer = vec![0u16; 512];
    let mut size = (buffer.len() * 2) as u32;
    // SAFETY: valid strings and buffer.
    let status = unsafe { RegGetValueW(HKEY_LOCAL_MACHINE, PCWSTR(key.as_ptr()), PCWSTR(name.as_ptr()), RRF_RT_REG_SZ, None, Some(buffer.as_mut_ptr().cast()), Some(&mut size)) };
    if status.is_err() {
        return String::new();
    }
    let length = buffer.iter().position(|&u| u == 0).unwrap_or(buffer.len());
    String::from_utf16_lossy(&buffer[..length])
}

/// Every devnode with a VB-CABLE or VAC hardware ID, present or not.
pub fn read_devices() -> Evidence<DeviceEvidence> {
    let mut items = Vec::new();
    // SAFETY: all-class enumeration; destroyed below.
    let Ok(set) = (unsafe { SetupDiGetClassDevsW(None, PCWSTR::null(), None, DIGCF_ALLCLASSES) }) else { return Evidence { items, succeeded: false } };
    let mut succeeded = true;
    for index in 0u32.. {
        let mut data = SP_DEVINFO_DATA { cbSize: std::mem::size_of::<SP_DEVINFO_DATA>() as u32, ..Default::default() };
        // SAFETY: valid set and structure.
        if unsafe { SetupDiEnumDeviceInfo(set, index, &mut data) }.is_err() {
            // SAFETY: plain query.
            if unsafe { GetLastError() } != ERROR_NO_MORE_ITEMS {
                succeeded = false;
            }
            break;
        }
        let ids: Vec<String> = property(set, &data, SPDRP_HARDWAREID.0, &mut succeeded).split('\0').filter(|s| !s.is_empty()).map(str::to_owned).collect();
        if !FAMILIES.iter().any(|&family| ids.iter().any(|id| policy::matches_hardware_id(id, family))) {
            continue;
        }
        let registry_path = property(set, &data, SPDRP_DRIVER.0, &mut succeeded).trim_end_matches('\0').to_owned();
        let version = driver_version(&registry_path);
        let (mut status, mut problem) = (CM_DEVNODE_STATUS_FLAGS(0), CM_PROB(0));
        // SAFETY: valid devnode and out pointers.
        let result = unsafe { CM_Get_DevNode_Status(&mut status, &mut problem, data.DevInst, 0) };
        let state = if result != CR_SUCCESS {
            "DeviceUnavailable"
        } else if problem.0 == 22 {
            "DeviceDisabled"
        } else if problem.0 != 0 {
            "DeviceNeedsAttention"
        } else if status.0 & 8 != 0 {
            "DevicePresent"
        } else {
            "DeviceUnavailable"
        };
        items.push(DeviceEvidence { hardware_ids: ids, version, state: state.into() });
    }
    // SAFETY: created above.
    unsafe {
        let _ = SetupDiDestroyDeviceInfoList(set);
    }
    Evidence { items, succeeded }
}

/// Published `oem*.inf` packages that declare a supported hardware ID.
pub fn read_packages() -> Evidence<PackageEvidence> {
    let mut items = Vec::new();
    let mut succeeded = true;
    let windows = std::env::var_os("SystemRoot").or_else(|| std::env::var_os("windir")).map(std::path::PathBuf::from).unwrap_or_else(|| "C:\\Windows".into());
    let directory = windows.join("INF");
    let Ok(entries) = std::fs::read_dir(&directory) else { return Evidence { items, succeeded: false } };
    for entry in entries {
        let Ok(entry) = entry else {
            succeeded = false;
            continue;
        };
        let name = entry.file_name().to_string_lossy().to_lowercase();
        if !(name.starts_with("oem") && name.ends_with(".inf")) {
            continue;
        }
        let path = entry.path();
        let Ok(bytes) = std::fs::read(&path) else {
            succeeded = false;
            continue;
        };
        let inf = InfIdentity::parse(&read_inf_text(&bytes));
        for family in FAMILIES {
            if !inf.identifies(family) {
                continue;
            }
            let file = wide(&path.to_string_lossy());
            let mut location = vec![0u16; 32768];
            // SAFETY: valid path and buffer.
            let staged = unsafe { SetupGetInfDriverStoreLocationW(PCWSTR(file.as_ptr()), None, PCWSTR::null(), &mut location, None) }.is_ok();
            items.push(PackageEvidence { family, version: inf.version(), in_driver_store: staged });
        }
    }
    Evidence { items, succeeded }
}

fn resolve_existing(path: &str) -> (Option<String>, bool) {
    let name = wide(path);
    // SAFETY: valid path; attribute-only open.
    let handle = unsafe { CreateFileW(PCWSTR(name.as_ptr()), 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, None, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, None) };
    let Ok(handle) = handle else {
        // SAFETY: plain query.
        let error = unsafe { GetLastError() }.0;
        return (None, error == 2 || error == 3);
    };
    let handle = Handle(handle);
    let mut buffer = vec![0u16; 32768];
    // SAFETY: valid handle and buffer.
    let length = unsafe { GetFinalPathNameByHandleW(handle.0, &mut buffer, FILE_NAME_NORMALIZED) } as usize;
    if length == 0 || length >= buffer.len() {
        return (None, false);
    }
    (Some(policy::normalize_final_path(&String::from_utf16_lossy(&buffer[..length]))), false)
}

/// Resolves existing link components; a new folder may have a missing suffix.
pub fn resolve_destination(path: &str) -> Option<String> {
    let mut current = std::path::absolute(path).ok()?.to_string_lossy().trim_end_matches('\\').to_owned();
    let mut suffix: Vec<String> = Vec::new();
    loop {
        let (resolved, missing) = resolve_existing(&current);
        if let Some(mut resolved) = resolved {
            while let Some(part) = suffix.pop() {
                resolved = Path::new(&resolved).join(part).to_string_lossy().into_owned();
            }
            return Some(resolved);
        }
        if !missing {
            return None;
        }
        let parent = Path::new(&current).parent()?.to_string_lossy().into_owned();
        if parent.eq_ignore_ascii_case(&current) || parent.is_empty() {
            return None;
        }
        suffix.push(Path::new(&current).file_name()?.to_string_lossy().into_owned());
        current = parent;
    }
}

fn known_folder(id: &windows::core::GUID) -> Option<String> {
    use windows::Win32::UI::Shell::{SHGetKnownFolderPath, KF_FLAG_DEFAULT};
    // SAFETY: valid folder id; the string is freed with CoTaskMemFree.
    unsafe {
        let path = SHGetKnownFolderPath(id, KF_FLAG_DEFAULT, None).ok()?;
        let text = path.to_string().ok();
        windows::Win32::System::Com::CoTaskMemFree(Some(path.0 as *const _));
        text
    }
}

fn is_safe_destination(directory: Option<&str>) -> bool {
    use windows::Win32::UI::Shell::{FOLDERID_Documents, FOLDERID_LocalAppData, FOLDERID_Profile, FOLDERID_System, FOLDERID_Windows};
    let Some(directory) = directory else { return false };
    let trim = |p: &str| if p.len() > 3 { p.trim_end_matches('\\').to_owned() } else { p.to_owned() };
    let local = known_folder(&FOLDERID_LocalAppData).unwrap_or_default();
    for name in ["AudioMixer", "VineMix"] {
        match resolve_destination(&Path::new(&local).join(name).to_string_lossy()) {
            Some(data) if !policy::is_same_or_within(directory, &data) => {}
            _ => return false,
        }
    }
    for folder in [&FOLDERID_Profile, &FOLDERID_LocalAppData, &FOLDERID_Documents, &FOLDERID_Windows, &FOLDERID_System] {
        let Some(path) = known_folder(folder).filter(|p| !p.is_empty()) else { continue };
        match resolve_destination(&path) {
            Some(resolved) if !trim(directory).eq_ignore_ascii_case(&trim(&resolved)) => {}
            _ => return false,
        }
    }
    let root = Path::new(directory).ancestors().last().map(|r| r.to_string_lossy().into_owned()).unwrap_or_default();
    !trim(directory).eq_ignore_ascii_case(&trim(&root))
}

fn process_user(process: HANDLE) -> Option<String> {
    let mut token = HANDLE::default();
    // SAFETY: valid process handle.
    unsafe { OpenProcessToken(process, TOKEN_QUERY, &mut token) }.ok()?;
    let token = Handle(token);
    let mut required = 0u32;
    // SAFETY: size query.
    let _ = unsafe { GetTokenInformation(token.0, TokenUser, None, 0, &mut required) };
    if required == 0 || required > 1024 * 1024 {
        return None;
    }
    let mut buffer = vec![0u8; required as usize];
    // SAFETY: buffer sized as reported.
    unsafe { GetTokenInformation(token.0, TokenUser, Some(buffer.as_mut_ptr().cast()), required, &mut required) }.ok()?;
    // SAFETY: the buffer holds a TOKEN_USER.
    let user = unsafe { std::ptr::read_unaligned(buffer.as_ptr().cast::<TOKEN_USER>()) };
    let mut text = PWSTR::null();
    // SAFETY: valid SID; freed with LocalFree.
    unsafe {
        windows::Win32::Security::Authorization::ConvertSidToStringSidW(user.User.Sid, &mut text).ok()?;
        let value = text.to_string().ok();
        let _ = windows::Win32::Foundation::LocalFree(Some(windows::Win32::Foundation::HLOCAL(text.0.cast())));
        value
    }
}

fn processes_named(names: &[&str]) -> Result<Vec<u32>, ()> {
    // SAFETY: snapshot owned below.
    let snapshot = Handle(unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) }.map_err(|_| ())?);
    let mut entry = PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
    let mut found = Vec::new();
    // SAFETY: valid snapshot and entry.
    let mut more = unsafe { Process32FirstW(snapshot.0, &mut entry) }.is_ok();
    while more {
        let length = entry.szExeFile.iter().position(|&c| c == 0).unwrap_or(entry.szExeFile.len());
        let name = String::from_utf16_lossy(&entry.szExeFile[..length]);
        if names.iter().any(|n| n.eq_ignore_ascii_case(&name)) {
            found.push(entry.th32ProcessID);
        }
        // SAFETY: as above.
        more = unsafe { Process32NextW(snapshot.0, &mut entry) }.is_ok();
    }
    Ok(found)
}

/// The current user's VineMix or Audio Mixer sessions and install path safety.
pub fn read_application(install_directory: Option<&Path>) -> Result<ApplicationResult, String> {
    let mut items = Vec::new();
    let mut succeeded = true;
    let resolved = install_directory.map(|d| resolve_destination(&d.to_string_lossy()));
    if matches!(resolved, Some(None)) {
        succeeded = false;
    }
    let resolved = resolved.flatten();
    let install_path_safe = install_directory.is_none() || is_safe_destination(resolved.as_deref());
    let own = vinemix_platform::current_user_sid().map_err(|_| "Current user cannot be identified.".to_owned())?;
    match processes_named(&["VineMix.exe", "VineMix.Engine.exe", "AudioMixer.exe", "AudioMixer.Engine.exe"]) {
        Err(()) => succeeded = false,
        Ok(pids) => {
            for pid in pids {
                // SAFETY: plain open; closed by Handle.
                let handle = match unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) } {
                    Ok(handle) => Handle(handle),
                    Err(_) => {
                        // SAFETY: plain query.
                        if unsafe { GetLastError() } != ERROR_INVALID_PARAMETER {
                            items.push(ProcessEvidence { same_user: None, path: None, still_running: true });
                        }
                        continue;
                    }
                };
                let same_user = process_user(handle.0).map(|user| user == own);
                let mut buffer = vec![0u16; 32768];
                let mut size = buffer.len() as u32;
                // SAFETY: valid handle and buffer.
                let path = unsafe { QueryFullProcessImageNameW(handle.0, PROCESS_NAME_WIN32, PWSTR(buffer.as_mut_ptr()), &mut size) }
                    .ok()
                    .and_then(|()| resolve_existing(&String::from_utf16_lossy(&buffer[..size as usize])).0);
                items.push(ProcessEvidence { same_user, path, still_running: true });
            }
        }
    }
    let mut result = policy::evaluate_processes(&items, succeeded, resolved.as_deref());
    result.install_path_safe = install_path_safe;
    Ok(result)
}
