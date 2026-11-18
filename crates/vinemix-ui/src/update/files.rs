//! Private update storage (`UpdateFiles`): owner-only ACLs, link refusal,
//! hashing, checked copies and atomic JSON metadata, byte-compatible with the
//! 0.5 desktop so either version's worker can read the other's transaction.

use std::ffi::c_void;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};

use serde::de::DeserializeOwned;
use serde::Serialize;
use windows::core::PCWSTR;
use windows::Win32::Foundation::{HLOCAL, LocalFree, GENERIC_WRITE};
use windows::Win32::Security::Authorization::{
    ConvertSidToStringSidW, ConvertStringSecurityDescriptorToSecurityDescriptorW, GetNamedSecurityInfoW, SDDL_REVISION_1, SE_FILE_OBJECT,
};
use windows::Win32::Security::{
    AclSizeInformation, GetAce, GetAclInformation, ACCESS_ALLOWED_ACE, ACE_HEADER, ACL, ACL_SIZE_INFORMATION, DACL_SECURITY_INFORMATION,
    OWNER_SECURITY_INFORMATION, PSECURITY_DESCRIPTOR, PSID, SECURITY_ATTRIBUTES,
};
use windows::Win32::Storage::FileSystem::{
    CreateDirectoryW, CreateFileW, FlushFileBuffers, GetFileAttributesW, MoveFileExW, ReplaceFileW, WriteFile, CREATE_NEW, FILE_ATTRIBUTE_NORMAL,
    FILE_ATTRIBUTE_REPARSE_POINT, FILE_SHARE_NONE, INVALID_FILE_ATTRIBUTES, MOVEFILE_WRITE_THROUGH, REPLACE_FILE_FLAGS,
};

use crate::platform::{wide, OwnedHandle};

/// An update-storage failure, reported to the user as its message.
pub type Result<T> = std::result::Result<T, String>;

/// `FileSystemRights.Write`: WriteData | AppendData | WriteExtendedAttributes | WriteAttributes.
const WRITE_RIGHTS: u32 = 0x0002 | 0x0004 | 0x0010 | 0x0100;
const ACCESS_ALLOWED_ACE_TYPE: u8 = 0;

fn wide_path(path: &Path) -> Vec<u16> {
    use std::os::windows::ffi::OsStrExt;
    path.as_os_str().encode_wide().chain(std::iter::once(0)).collect()
}

/// `Path.GetFullPath`.
pub fn full(path: &Path) -> PathBuf {
    std::path::absolute(path).unwrap_or_else(|_| path.to_path_buf())
}

fn normalized(path: &Path) -> String {
    let text = full(path).to_string_lossy().into_owned();
    text.trim_end_matches('\\').to_lowercase()
}

/// Case-insensitive comparison of full paths without trailing separators.
pub fn same_path(left: &Path, right: &Path) -> bool {
    normalized(left) == normalized(right)
}

/// Whether `path` lies strictly inside `directory`.
pub fn is_inside(path: &Path, directory: &Path) -> bool {
    normalized(path).starts_with(&(normalized(directory) + "\\"))
}

/// Two 64-digit hexadecimal digests that are equal ignoring case.
pub fn equal_hash(left: &str, right: &str) -> bool {
    let valid = |h: &str| h.len() == 64 && h.bytes().all(|b| b.is_ascii_hexdigit());
    valid(left) && valid(right) && left.eq_ignore_ascii_case(right)
}

/// Refuses symbolic links, junctions and other reparse points anywhere on the path.
pub fn no_reparse(path: &Path) -> Result<()> {
    let mut current = Some(full(path));
    while let Some(path) = current {
        let name = wide_path(&path);
        // SAFETY: NUL-terminated path.
        let attributes = unsafe { GetFileAttributesW(PCWSTR(name.as_ptr())) };
        if attributes != INVALID_FILE_ATTRIBUTES && attributes & FILE_ATTRIBUTE_REPARSE_POINT.0 != 0 {
            return Err("Updates cannot use links, junctions or reparse-point paths.".into());
        }
        current = path.parent().map(Path::to_path_buf).filter(|parent| parent != &path);
    }
    Ok(())
}

/// Lower-case SHA-256 of a file.
pub fn hash(path: &Path) -> Result<String> {
    no_reparse(path)?;
    let mut file = std::fs::File::open(path).map_err(|e| format!("An update file could not be read. {e}"))?;
    let mut digest = vinemix_update::crypto::Sha256::new()?;
    let mut buffer = vec![0u8; 1 << 16];
    loop {
        let read = file.read(&mut buffer).map_err(|e| format!("An update file could not be read. {e}"))?;
        if read == 0 {
            break;
        }
        digest.update(&buffer[..read])?;
    }
    Ok(vinemix_update::crypto::hex(&digest.finish()?))
}

pub fn file_size(path: &Path) -> Result<u64> {
    std::fs::metadata(path).map(|m| m.len()).map_err(|e| format!("An update file is unavailable. {e}"))
}

/// Size and digest must match the signed asset.
pub fn verify_asset(path: &Path, asset: &vinemix_update::Asset) -> Result<()> {
    no_reparse(path)?;
    if file_size(path)? != asset.size || !equal_hash(&hash(path)?, &asset.sha256) {
        return Err("The update payload size or SHA-256 no longer matches its signed release.".into());
    }
    Ok(())
}

/// A security descriptor granting only the current user full control.
struct PrivateDescriptor(PSECURITY_DESCRIPTOR);

impl PrivateDescriptor {
    fn new(directory: bool) -> Result<Self> {
        let sid = vinemix_platform::current_user_sid().map_err(|_| "The Windows user cannot be identified.".to_owned())?;
        let inheritance = if directory { "OICI" } else { "" };
        let sddl = wide(&format!("O:{sid}D:P(A;{inheritance};FA;;;{sid})"));
        let mut descriptor = PSECURITY_DESCRIPTOR::default();
        // SAFETY: valid SDDL string; the descriptor is freed in Drop.
        unsafe { ConvertStringSecurityDescriptorToSecurityDescriptorW(PCWSTR(sddl.as_ptr()), SDDL_REVISION_1, &mut descriptor, None) }
            .map_err(|e| format!("The private update permissions could not be prepared. {}", e.message()))?;
        Ok(Self(descriptor))
    }

    fn attributes(&self) -> SECURITY_ATTRIBUTES {
        SECURITY_ATTRIBUTES { nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32, lpSecurityDescriptor: self.0 .0, bInheritHandle: false.into() }
    }
}

impl Drop for PrivateDescriptor {
    fn drop(&mut self) {
        // SAFETY: allocated by ConvertStringSecurityDescriptorToSecurityDescriptorW.
        unsafe {
            let _ = LocalFree(Some(HLOCAL(self.0 .0)));
        }
    }
}

/// Creates a new file readable and writable only by the current user.
fn create_private_file(path: &Path) -> Result<OwnedHandle> {
    let descriptor = PrivateDescriptor::new(false)?;
    let attributes = descriptor.attributes();
    let name = wide_path(path);
    // SAFETY: valid path and security attributes for the duration of the call.
    let handle = unsafe { CreateFileW(PCWSTR(name.as_ptr()), GENERIC_WRITE.0, FILE_SHARE_NONE, Some(&attributes), CREATE_NEW, FILE_ATTRIBUTE_NORMAL, None) }
        .map_err(|e| format!("A private update file could not be created. {}", e.message()))?;
    Ok(OwnedHandle(handle))
}

fn write_all(handle: &OwnedHandle, mut data: &[u8]) -> Result<()> {
    while !data.is_empty() {
        let chunk = data.len().min(1 << 20);
        let mut written = 0u32;
        // SAFETY: valid handle and buffer.
        unsafe { WriteFile(handle.0, Some(&data[..chunk]), Some(&mut written), None) }.map_err(|e| format!("An update file could not be written. {}", e.message()))?;
        if written == 0 {
            return Err("An update file could not be written.".into());
        }
        data = &data[written as usize..];
    }
    Ok(())
}

fn flush(handle: &OwnedHandle) -> Result<()> {
    // SAFETY: valid handle.
    unsafe { FlushFileBuffers(handle.0) }.map_err(|e| format!("An update file could not be flushed. {}", e.message()))
}

/// Creates (if needed) and requires an owner-only directory.
pub fn create_private_directory(path: &Path) -> Result<()> {
    no_reparse(path)?;
    if !path.is_dir() {
        if let Some(parent) = full(path).parent() {
            std::fs::create_dir_all(parent).map_err(|e| format!("The update folder could not be created. {e}"))?;
        }
        let descriptor = PrivateDescriptor::new(true)?;
        let attributes = descriptor.attributes();
        let name = wide_path(path);
        // SAFETY: valid path and security attributes.
        unsafe { CreateDirectoryW(PCWSTR(name.as_ptr()), Some(&attributes)) }.map_err(|e| format!("The private update folder could not be created. {}", e.message()))?;
    }
    require_private(path)
}

fn sid_text(sid: PSID) -> Option<String> {
    let mut text = windows::core::PWSTR::null();
    // SAFETY: valid SID; the string is freed with LocalFree.
    unsafe {
        ConvertSidToStringSidW(sid, &mut text).ok()?;
        let value = text.to_string().ok();
        let _ = LocalFree(Some(HLOCAL(text.0.cast())));
        value
    }
}

/// The owner must be the current user, every allow entry must be that user,
/// and the user must be able to write (`RequirePrivate`).
pub fn require_private(path: &Path) -> Result<()> {
    no_reparse(path)?;
    let user = vinemix_platform::current_user_sid().map_err(|_| "The Windows user cannot be identified.".to_owned())?;
    let name = wide_path(path);
    let mut owner = PSID::default();
    let mut dacl: *mut ACL = std::ptr::null_mut();
    let mut descriptor = PSECURITY_DESCRIPTOR::default();
    // SAFETY: out-pointers are valid; the descriptor is freed below.
    let status = unsafe {
        GetNamedSecurityInfoW(
            PCWSTR(name.as_ptr()),
            SE_FILE_OBJECT,
            OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION,
            Some(&mut owner),
            None,
            Some(&mut dacl),
            None,
            &mut descriptor,
        )
    };
    if status.is_err() {
        return Err("The update staging permissions could not be read.".into());
    }
    let release = || {
        // SAFETY: allocated by GetNamedSecurityInfoW.
        unsafe {
            let _ = LocalFree(Some(HLOCAL(descriptor.0)));
        }
    };
    let result = (|| -> Result<()> {
        if sid_text(owner).as_deref() != Some(user.as_str()) {
            return Err("The update staging owner is not the current user.".into());
        }
        if dacl.is_null() {
            return Err("The update staging directory grants access to another account.".into());
        }
        let mut info = ACL_SIZE_INFORMATION::default();
        // SAFETY: valid ACL.
        unsafe { GetAclInformation(dacl, (&mut info as *mut ACL_SIZE_INFORMATION).cast::<c_void>(), std::mem::size_of::<ACL_SIZE_INFORMATION>() as u32, AclSizeInformation) }
            .map_err(|_| "The update staging permissions could not be read.".to_owned())?;
        let mut writable = false;
        for index in 0..info.AceCount {
            let mut ace: *mut c_void = std::ptr::null_mut();
            // SAFETY: index < AceCount.
            unsafe { GetAce(dacl, index, &mut ace) }.map_err(|_| "The update staging permissions could not be read.".to_owned())?;
            // SAFETY: every ACE begins with an ACE_HEADER.
            let header = unsafe { &*(ace as *const ACE_HEADER) };
            if header.AceType != ACCESS_ALLOWED_ACE_TYPE {
                if header.AceType == 1 {
                    continue; // Deny entries cannot widen access.
                }
                return Err("The update staging directory has an unsupported permission entry.".into());
            }
            // SAFETY: an allowed ACE is an ACCESS_ALLOWED_ACE whose SID starts at SidStart.
            let allowed = unsafe { &*(ace as *const ACCESS_ALLOWED_ACE) };
            let sid = PSID((&allowed.SidStart as *const u32).cast_mut().cast());
            if sid_text(sid).as_deref() != Some(user.as_str()) {
                return Err("The update staging directory grants access to another account.".into());
            }
            if allowed.Mask & WRITE_RIGHTS != 0 || allowed.Mask & 0x1000_0000 != 0 {
                writable = true;
            }
        }
        if !writable {
            return Err("The update staging directory is not writable by its owner.".into());
        }
        Ok(())
    })();
    release();
    result
}

/// Removes only the unique sibling this operation successfully created.
struct PendingCopy(PathBuf);

impl Drop for PendingCopy {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.0);
    }
}

fn copy_checked_impl(source: &Path, destination: &Path, size: u64, digest: &str, private: bool) -> Result<()> {
    no_reparse(source)?;
    no_reparse(destination)?;
    let mut input = std::fs::File::open(source).map_err(|e| format!("An update file could not be read. {e}"))?;
    if input.metadata().map(|m| m.len()).unwrap_or(u64::MAX) != size {
        return Err("An update file changed size before copying.".into());
    }
    let temporary = destination.with_file_name(format!(".vinemix-copy-{}.tmp", crate::platform::random_hex_id()));
    let pending;
    {
        let mut buffer = vec![0u8; 1 << 16];
        if private {
            let handle = create_private_file(&temporary)?;
            pending = PendingCopy(temporary);
            loop {
                let read = input.read(&mut buffer).map_err(|e| format!("An update file could not be read. {e}"))?;
                if read == 0 {
                    break;
                }
                write_all(&handle, &buffer[..read])?;
            }
            flush(&handle)?;
        } else {
            let mut output = std::fs::OpenOptions::new().write(true).create_new(true).open(&temporary).map_err(|e| format!("An update file could not be created. {e}"))?;
            pending = PendingCopy(temporary);
            std::io::copy(&mut input, &mut output).map_err(|e| format!("An update file could not be copied. {e}"))?;
            output.flush().map_err(|e| format!("An update file could not be copied. {e}"))?;
            output.sync_all().map_err(|e| format!("An update file could not be copied. {e}"))?;
        }
    }
    if file_size(&pending.0)? != size || !equal_hash(&hash(&pending.0)?, digest) {
        return Err("An update file changed while copying.".into());
    }
    // Same-volume, write-through publication without replacing an existing file.
    if !vinemix_platform::move_write_through(&pending.0, destination) {
        return Err("A verified update copy could not be published at its destination.".into());
    }
    Ok(())
}

/// A checked copy into an ordinary (installation) location.
pub fn copy_checked(source: &Path, destination: &Path, size: u64, digest: &str) -> Result<()> {
    copy_checked_impl(source, destination, size, digest, false)
}

/// A checked copy into a new owner-only file.
pub fn copy_private_checked(source: &Path, destination: &Path, size: u64, digest: &str) -> Result<()> {
    copy_checked_impl(source, destination, size, digest, true)
}

/// Reads a bounded, non-empty file.
pub fn read_bytes(path: &Path, maximum: u64) -> Result<Vec<u8>> {
    no_reparse(path)?;
    let size = file_size(path)?;
    if size == 0 || size > maximum {
        return Err("An update metadata file exceeds its size limit.".into());
    }
    std::fs::read(path).map_err(|e| format!("An update metadata file could not be read. {e}"))
}

/// Atomically replaces `path` with a new owner-only file holding `bytes`.
pub fn write_bytes(path: &Path, bytes: &[u8]) -> Result<()> {
    no_reparse(path)?;
    let temporary = PathBuf::from(format!("{}.{}.tmp", path.display(), crate::platform::random_hex_id()));
    {
        let handle = create_private_file(&temporary)?;
        write_all(&handle, bytes)?;
        flush(&handle)?;
    }
    let target = wide_path(path);
    let replacement = wide_path(&temporary);
    let result = if path.exists() {
        // SAFETY: valid paths.
        unsafe { ReplaceFileW(PCWSTR(target.as_ptr()), PCWSTR(replacement.as_ptr()), PCWSTR::null(), REPLACE_FILE_FLAGS(0), None, None) }
    } else {
        // SAFETY: valid paths.
        unsafe { MoveFileExW(PCWSTR(replacement.as_ptr()), PCWSTR(target.as_ptr()), MOVEFILE_WRITE_THROUGH) }
    };
    result.map_err(|e| {
        let _ = std::fs::remove_file(&temporary);
        format!("Update metadata could not be saved. {}", e.message())
    })
}

pub fn read_json<T: DeserializeOwned>(path: &Path, maximum: u64) -> Result<T> {
    let bytes = read_bytes(path, maximum)?;
    serde_json::from_slice(&bytes).map_err(|e| format!("Update metadata is invalid. {e}"))
}

pub fn write_json<T: Serialize>(path: &Path, value: &T) -> Result<()> {
    let bytes = serde_json::to_vec_pretty(value).map_err(|e| format!("Update metadata could not be prepared. {e}"))?;
    write_bytes(path, &bytes)
}

const RESERVED: [&str; 22] = [
    "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8",
    "LPT9",
];

/// Resolves a `/`-separated inventory path under `root`, refusing escapes,
/// user settings and update data (`RelativeFile`).
pub fn relative_file(root: &Path, relative: &str) -> Result<PathBuf> {
    if relative.is_empty()
        || relative.chars().count() > 240
        || relative.contains('\\')
        || relative.contains(':')
        || relative.split('/').any(|part| part.is_empty() || part == "." || part == ".." || part.ends_with('.') || part.ends_with(' '))
    {
        return Err("The update payload inventory has an unsafe relative path.".into());
    }
    let joined = full(&root.join(relative.replace('/', "\\")));
    if !is_inside(&joined, root) {
        return Err("The update inventory escapes its installation folder.".into());
    }
    let name = joined.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    if name.starts_with("settings.json") || name.starts_with("ui.json") || name.contains(".log") || relative.split('/').any(|part| part.eq_ignore_ascii_case("updates")) {
        return Err("User settings and update data cannot be part of an application payload inventory.".into());
    }
    no_reparse(&joined)?;
    Ok(joined)
}

/// Rejects reserved device names and invalid characters (`ValidateRelativePath`).
pub fn validate_runtime_path(path: &str) -> Result<()> {
    if path.is_empty() || path.starts_with('/') || path.contains('\\') || path.contains(':') {
        return Err("Unsafe runtime path.".into());
    }
    for part in path.split('/') {
        let stem = part.split('.').next().unwrap_or("");
        if part.is_empty()
            || part == "."
            || part == ".."
            || part.ends_with('.')
            || part.ends_with(' ')
            || part.chars().any(|c| c < ' ' || "<>:\"/\\|?*".contains(c))
            || RESERVED.iter().any(|r| r.eq_ignore_ascii_case(stem))
        {
            return Err("Unsafe runtime path.".into());
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn private_files_round_trip_and_are_private() {
        let root = std::env::temp_dir().join(format!("vinemix-update-files-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        create_private_directory(&root).unwrap();
        let file = root.join("value.json");
        write_json(&file, &serde_json::json!({"a": 1})).unwrap();
        require_private(&file).unwrap();
        write_json(&file, &serde_json::json!({"a": 2})).unwrap();
        let value: serde_json::Value = read_json(&file, 1024).unwrap();
        assert_eq!(value["a"], 2);
        let copy = root.join("copy.json");
        let digest = hash(&file).unwrap();
        copy_private_checked(&file, &copy, file_size(&file).unwrap(), &digest).unwrap();
        require_private(&copy).unwrap();
        assert!(copy_private_checked(&file, &copy, file_size(&file).unwrap(), &digest).is_err(), "never overwrites");
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn relative_paths_are_bounded() {
        let root = std::env::temp_dir();
        assert!(relative_file(&root, "VineMix.exe").is_ok());
        assert!(relative_file(&root, "SetupSupport/package-manifest.json").is_ok());
        for bad in ["../x", "a\\b", "C:/x", "settings.json", "updates/x", "a/./b", "x.", "trace.log"] {
            assert!(relative_file(&root, bad).is_err(), "{bad}");
        }
        assert!(validate_runtime_path("VineMix.Engine.exe").is_ok());
        assert!(validate_runtime_path("licenses/CON.txt").is_err());
    }

    #[test]
    fn hashes_compare_strictly() {
        let a = "ab".repeat(32);
        assert!(equal_hash(&a, &a.to_uppercase()));
        assert!(!equal_hash(&a, "ab"));
        assert!(!equal_hash(&"zz".repeat(32), &"zz".repeat(32)));
    }
}
