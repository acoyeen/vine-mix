//! The engine runtime (`EmbeddedRuntime`). The installed build ships
//! `VineMix.Engine.exe` beside the desktop; the standalone build carries a
//! reviewed archive inside `VineMix.exe` and extracts it once into an
//! immutable, hash-named cache under `%LOCALAPPDATA%\VineMix\runtime`.

use std::path::{Path, PathBuf};

use serde::Deserialize;

use crate::update::files;

pub const ENGINE: &str = "VineMix.Engine.exe";
const MAXIMUM_PAYLOAD: u64 = 128 * 1024 * 1024;
/// Files every runtime archive must contain.
const REQUIRED: [&str; 4] = [ENGINE, "LICENSE", "ATTRIBUTION.md", "THIRD_PARTY_NOTICES.md"];

/// Whether this executable is the standalone single-file distribution.
pub fn standalone() -> bool {
    cfg!(feature = "standalone")
}

/// `Installed` or `Standalone`, as release smoke reports it.
pub fn build_mode() -> &'static str {
    if standalone() { "Standalone" } else { "Installed" }
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RuntimeFile {
    path: String,
    size: u64,
    sha256: String,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct RuntimeManifest {
    schema_version: i32,
    engine: String,
    archive_size: u64,
    archive_sha256: String,
    files: Vec<RuntimeFile>,
}

#[cfg(feature = "standalone")]
mod embedded {
    pub static ARCHIVE: &[u8] = include_bytes!(env!("VINEMIX_RUNTIME_ZIP"));
    pub static MANIFEST: &[u8] = include_bytes!(env!("VINEMIX_RUNTIME_MANIFEST"));
}

#[cfg(not(feature = "standalone"))]
mod embedded {
    pub static ARCHIVE: &[u8] = &[];
    pub static MANIFEST: &[u8] = &[];
}

fn is_hash(text: &str) -> bool {
    text.len() == 64 && text.bytes().all(|b| b.is_ascii_hexdigit())
}

fn read_manifest() -> Result<RuntimeManifest, String> {
    if embedded::MANIFEST.is_empty() {
        return Err("The standalone VineMix runtime manifest is missing.".into());
    }
    if embedded::MANIFEST.len() > 1024 * 1024 {
        return Err("Runtime manifest exceeds its limit.".into());
    }
    let manifest: RuntimeManifest = serde_json::from_slice(embedded::MANIFEST).map_err(|_| "Runtime manifest is invalid.".to_owned())?;
    let mut names = std::collections::HashSet::new();
    if manifest.schema_version != 1
        || manifest.engine != ENGINE
        || manifest.archive_size == 0
        || manifest.archive_size > MAXIMUM_PAYLOAD
        || !is_hash(&manifest.archive_sha256)
        || manifest.files.len() < REQUIRED.len()
        || manifest.files.len() > 100
        || !manifest.files.iter().all(|f| names.insert(f.path.to_lowercase()))
    {
        return Err("Runtime manifest is invalid.".into());
    }
    let mut total = 0u64;
    for file in &manifest.files {
        files::validate_runtime_path(&file.path)?;
        if file.size > MAXIMUM_PAYLOAD || !is_hash(&file.sha256) {
            return Err("Runtime file metadata is invalid.".into());
        }
        total = total.checked_add(file.size).ok_or("Runtime file metadata is invalid.")?;
    }
    if total > MAXIMUM_PAYLOAD || !REQUIRED.iter().all(|name| manifest.files.iter().any(|f| f.path == *name)) {
        return Err("Runtime allowlist is incomplete.".into());
    }
    Ok(manifest)
}

fn read_archive(manifest: &RuntimeManifest) -> Result<&'static [u8], String> {
    if embedded::ARCHIVE.is_empty() {
        return Err("The standalone VineMix runtime archive is missing.".into());
    }
    if embedded::ARCHIVE.len() as u64 != manifest.archive_size {
        return Err("Runtime archive size mismatch.".into());
    }
    let digest = vinemix_update::crypto::sha256(embedded::ARCHIVE)?;
    if !vinemix_update::crypto::hex(&digest).eq_ignore_ascii_case(&manifest.archive_sha256) {
        return Err("Runtime archive hash mismatch.".into());
    }
    Ok(embedded::ARCHIVE)
}

/// One stored (uncompressed) ZIP entry.
#[derive(Debug)]
pub struct ZipEntry<'a> {
    pub name: String,
    pub data: &'a [u8],
    pub symlink: bool,
}

fn u16_at(data: &[u8], offset: usize) -> Option<u16> {
    Some(u16::from_le_bytes(data.get(offset..offset + 2)?.try_into().ok()?))
}

fn u32_at(data: &[u8], offset: usize) -> Option<u32> {
    Some(u32::from_le_bytes(data.get(offset..offset + 4)?.try_into().ok()?))
}

/// Reads a ZIP whose entries are all stored; anything else is refused.
pub fn read_stored_zip(archive: &[u8]) -> Result<Vec<ZipEntry<'_>>, String> {
    let invalid = || "Runtime archive contains an unexpected entry.".to_owned();
    let search_start = archive.len().saturating_sub(22 + 65535);
    let end = (search_start..=archive.len().saturating_sub(22)).rev().find(|&i| u32_at(archive, i) == Some(0x0605_4b50)).ok_or_else(invalid)?;
    let count = usize::from(u16_at(archive, end + 10).ok_or_else(invalid)?);
    let mut offset = u32_at(archive, end + 16).ok_or_else(invalid)? as usize;
    let mut entries = Vec::with_capacity(count);
    for _ in 0..count {
        if u32_at(archive, offset) != Some(0x0201_4b50) {
            return Err(invalid());
        }
        let method = u16_at(archive, offset + 10).ok_or_else(invalid)?;
        let compressed = u32_at(archive, offset + 20).ok_or_else(invalid)? as usize;
        let size = u32_at(archive, offset + 24).ok_or_else(invalid)? as usize;
        let name_length = usize::from(u16_at(archive, offset + 28).ok_or_else(invalid)?);
        let extra_length = usize::from(u16_at(archive, offset + 30).ok_or_else(invalid)?);
        let comment_length = usize::from(u16_at(archive, offset + 32).ok_or_else(invalid)?);
        let external = u32_at(archive, offset + 38).ok_or_else(invalid)?;
        let local = u32_at(archive, offset + 42).ok_or_else(invalid)? as usize;
        let name = std::str::from_utf8(archive.get(offset + 46..offset + 46 + name_length).ok_or_else(invalid)?).map_err(|_| invalid())?.to_owned();
        if method != 0 || compressed != size || u32_at(archive, local) != Some(0x0403_4b50) {
            return Err(invalid());
        }
        let local_name = usize::from(u16_at(archive, local + 26).ok_or_else(invalid)?);
        let local_extra = usize::from(u16_at(archive, local + 28).ok_or_else(invalid)?);
        let start = local + 30 + local_name + local_extra;
        let data = archive.get(start..start + size).ok_or_else(invalid)?;
        entries.push(ZipEntry { name, data, symlink: (external >> 16) & 0xF000 == 0xA000 });
        offset += 46 + name_length + extra_length + comment_length;
    }
    Ok(entries)
}

fn verify_archive(archive: &[u8], manifest: &RuntimeManifest, destination: Option<&Path>) -> Result<(), String> {
    let entries = read_stored_zip(archive)?;
    if entries.len() != manifest.files.len() {
        return Err("Runtime archive entry count mismatch.".into());
    }
    let mut seen = std::collections::HashSet::new();
    for entry in entries {
        files::validate_runtime_path(&entry.name)?;
        let file = manifest.files.iter().find(|f| f.path == entry.name);
        let Some(file) = file else { return Err("Runtime archive contains an unexpected entry.".into()) };
        if !seen.insert(entry.name.to_lowercase()) || entry.data.len() as u64 != file.size || entry.symlink {
            return Err("Runtime archive contains an unexpected entry.".into());
        }
        let digest = vinemix_update::crypto::hex(&vinemix_update::crypto::sha256(entry.data)?);
        if !digest.eq_ignore_ascii_case(&file.sha256) {
            return Err("Runtime entry failed its integrity check.".into());
        }
        if let Some(destination) = destination {
            let path = destination.join(file.path.replace('/', "\\"));
            if let Some(parent) = path.parent() {
                std::fs::create_dir_all(parent).map_err(|e| format!("The VineMix runtime could not be prepared. {e}"))?;
            }
            files::no_reparse(&path)?;
            use std::io::Write;
            let mut output = std::fs::OpenOptions::new().write(true).create_new(true).open(&path).map_err(|e| format!("The VineMix runtime could not be prepared. {e}"))?;
            output.write_all(entry.data).and_then(|()| output.sync_all()).map_err(|e| format!("The VineMix runtime could not be prepared. {e}"))?;
        }
    }
    Ok(())
}

fn all_files(directory: &Path, out: &mut Vec<PathBuf>) -> Result<(), String> {
    for entry in std::fs::read_dir(directory).map_err(|e| e.to_string())? {
        let path = entry.map_err(|e| e.to_string())?.path();
        files::no_reparse(&path)?;
        if path.is_dir() {
            all_files(&path, out)?;
        } else {
            out.push(path);
        }
    }
    Ok(())
}

fn validate_cache(directory: &Path, manifest: &RuntimeManifest) -> bool {
    if !directory.is_dir() || files::no_reparse(directory).is_err() {
        return false;
    }
    let mut present = Vec::new();
    if all_files(directory, &mut present).is_err() || present.len() != manifest.files.len() {
        return false;
    }
    manifest.files.iter().all(|file| {
        let path = directory.join(file.path.replace('/', "\\"));
        path.is_file() && files::file_size(&path).ok() == Some(file.size) && files::hash(&path).is_ok_and(|h| h.eq_ignore_ascii_case(&file.sha256))
    })
}

/// Checks the shipped runtime without starting it (`ValidatePackage`).
pub fn validate_package() -> Result<String, String> {
    if !standalone() {
        let directory = std::env::current_exe().ok().and_then(|p| p.parent().map(Path::to_path_buf)).ok_or("The VineMix installation folder is unavailable.")?;
        if !directory.join(ENGINE).is_file() {
            return Err("The installed VineMix runtime is incomplete.".into());
        }
        return Ok("Installed runtime present".into());
    }
    let manifest = read_manifest()?;
    let archive = read_archive(&manifest)?;
    verify_archive(archive, &manifest, None)?;
    Ok(manifest.archive_sha256.to_lowercase())
}

/// The engine to supervise (`ResolveEnginePath`).
pub fn resolve_engine_path(runtime_root_override: Option<&Path>) -> Result<PathBuf, String> {
    if !standalone() {
        validate_package()?;
        let executable = std::env::current_exe().map_err(|e| e.to_string())?;
        return Ok(executable.parent().ok_or("The VineMix installation folder is unavailable.")?.join(ENGINE));
    }
    let manifest = read_manifest()?;
    let root = runtime_root_override.map_or_else(|| crate::paths::data_directory().join("runtime"), files::full);
    resolve_standalone(&manifest, &root)
}

fn resolve_standalone(manifest: &RuntimeManifest, root: &Path) -> Result<PathBuf, String> {
    files::no_reparse(root)?;
    std::fs::create_dir_all(root).map_err(|e| format!("The VineMix runtime folder could not be created. {e}"))?;
    let hash = manifest.archive_sha256.to_lowercase();
    let cache = root.join(&hash);
    let sid = vinemix_platform::current_user_sid().map_err(|_| "Windows user identity is unavailable.".to_owned())?;
    let _guard = RuntimeMutex::acquire(&format!("Local\\VineMix.Runtime.{sid}.{hash}"))?;
    if validate_cache(&cache, manifest) {
        return Ok(cache.join(ENGINE));
    }
    // Never replace individual loaded files: keep a corrupt cache for
    // diagnosis, and refuse repair while any of its files are in use.
    if cache.exists() {
        files::no_reparse(&cache)?;
        let mut present = Vec::new();
        all_files(&cache, &mut present)?;
        for file in &present {
            std::fs::OpenOptions::new().read(true).write(true).open(file).map_err(|_| "Another VineMix window is using its runtime. Please try again.".to_owned())?;
        }
        std::fs::rename(&cache, root.join(format!("{hash}.invalid-{}", crate::platform::random_hex_id()))).map_err(|e| format!("The VineMix runtime could not be repaired. {e}"))?;
    }
    // A failed stage is retained: repair never deletes unfamiliar files.
    let stage = root.join(format!(".staging-{}", crate::platform::random_hex_id()));
    std::fs::create_dir_all(&stage).map_err(|e| format!("The VineMix runtime could not be prepared. {e}"))?;
    verify_archive(read_archive(manifest)?, manifest, Some(&stage))?;
    if !validate_cache(&stage, manifest) {
        return Err("The extracted VineMix runtime failed its integrity check.".into());
    }
    std::fs::rename(&stage, &cache).map_err(|e| format!("The VineMix runtime could not be prepared. {e}"))?;
    Ok(cache.join(ENGINE))
}

/// A named mutex held while preparing the runtime cache.
struct RuntimeMutex(windows::Win32::Foundation::HANDLE);

impl RuntimeMutex {
    fn acquire(name: &str) -> Result<Self, String> {
        use windows::Win32::Foundation::{CloseHandle, WAIT_ABANDONED, WAIT_OBJECT_0};
        use windows::Win32::System::Threading::{CreateMutexW, WaitForSingleObject};
        let wide = crate::platform::wide(name);
        // SAFETY: named mutex owned by the result.
        let handle = unsafe { CreateMutexW(None, false, windows::core::PCWSTR(wide.as_ptr())) }.map_err(|e| e.message())?;
        // SAFETY: valid handle.
        let result = unsafe { WaitForSingleObject(handle, 45_000) };
        if result != WAIT_OBJECT_0 && result != WAIT_ABANDONED {
            // SAFETY: owned handle.
            unsafe {
                let _ = CloseHandle(handle);
            }
            return Err("Another VineMix window is preparing its runtime. Please try again.".into());
        }
        Ok(Self(handle))
    }
}

impl Drop for RuntimeMutex {
    fn drop(&mut self) {
        // SAFETY: release what this thread acquired, then close.
        unsafe {
            let _ = windows::Win32::System::Threading::ReleaseMutex(self.0);
            let _ = windows::Win32::Foundation::CloseHandle(self.0);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Builds a stored ZIP in memory (as the packaging tool writes it).
    pub fn stored_zip(entries: &[(&str, &[u8])]) -> Vec<u8> {
        let mut out = Vec::new();
        let mut central = Vec::new();
        for (name, data) in entries {
            let offset = out.len() as u32;
            let crc = crc32(data);
            out.extend_from_slice(&0x0403_4b50u32.to_le_bytes());
            out.extend_from_slice(&[20, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            out.extend_from_slice(&crc.to_le_bytes());
            out.extend_from_slice(&(data.len() as u32).to_le_bytes());
            out.extend_from_slice(&(data.len() as u32).to_le_bytes());
            out.extend_from_slice(&(name.len() as u16).to_le_bytes());
            out.extend_from_slice(&0u16.to_le_bytes());
            out.extend_from_slice(name.as_bytes());
            out.extend_from_slice(data);
            central.extend_from_slice(&0x0201_4b50u32.to_le_bytes());
            central.extend_from_slice(&[20, 0, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            central.extend_from_slice(&crc.to_le_bytes());
            central.extend_from_slice(&(data.len() as u32).to_le_bytes());
            central.extend_from_slice(&(data.len() as u32).to_le_bytes());
            central.extend_from_slice(&(name.len() as u16).to_le_bytes());
            central.extend_from_slice(&[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            central.extend_from_slice(&offset.to_le_bytes());
            central.extend_from_slice(name.as_bytes());
        }
        let start = out.len() as u32;
        out.extend_from_slice(&central);
        out.extend_from_slice(&0x0605_4b50u32.to_le_bytes());
        out.extend_from_slice(&[0, 0, 0, 0]);
        out.extend_from_slice(&(entries.len() as u16).to_le_bytes());
        out.extend_from_slice(&(entries.len() as u16).to_le_bytes());
        out.extend_from_slice(&(central.len() as u32).to_le_bytes());
        out.extend_from_slice(&start.to_le_bytes());
        out.extend_from_slice(&0u16.to_le_bytes());
        out
    }

    fn crc32(data: &[u8]) -> u32 {
        let mut crc = !0u32;
        for &byte in data {
            crc ^= u32::from(byte);
            for _ in 0..8 {
                crc = if crc & 1 != 0 { (crc >> 1) ^ 0xEDB8_8320 } else { crc >> 1 };
            }
        }
        !crc
    }

    #[test]
    fn stored_archives_round_trip_and_reject_tampering() {
        let zip = stored_zip(&[("VineMix.Engine.exe", b"engine"), ("licenses/x.txt", b"notice")]);
        let entries = read_stored_zip(&zip).unwrap();
        assert_eq!(entries.len(), 2);
        assert_eq!(entries[1].name, "licenses/x.txt");
        assert_eq!(entries[1].data, b"notice");
        let manifest = RuntimeManifest {
            schema_version: 1,
            engine: ENGINE.into(),
            archive_size: zip.len() as u64,
            archive_sha256: vinemix_update::crypto::hex(&vinemix_update::crypto::sha256(&zip).unwrap()),
            files: entries
                .iter()
                .map(|e| RuntimeFile { path: e.name.clone(), size: e.data.len() as u64, sha256: vinemix_update::crypto::hex(&vinemix_update::crypto::sha256(e.data).unwrap()) })
                .collect(),
        };
        verify_archive(&zip, &manifest, None).unwrap();
        let mut changed = manifest.clone();
        changed.files[0].sha256 = "00".repeat(32);
        assert!(verify_archive(&zip, &changed, None).is_err());
        let root = std::env::temp_dir().join(format!("vinemix-runtime-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        verify_archive(&zip, &manifest, Some(&root)).unwrap();
        assert!(validate_cache(&root, &manifest));
        std::fs::write(root.join("extra.txt"), b"x").unwrap();
        assert!(!validate_cache(&root, &manifest), "unexpected files invalidate the cache");
        let _ = std::fs::remove_dir_all(&root);
    }
}
