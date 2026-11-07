//! `engine.log`: local-time lines, rotated to `engine.log.previous` at 2 MiB.
//! Logging must never stop routing, so every failure is ignored.

use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

const ROTATE_BYTES: u64 = 2 * 1024 * 1024;

#[derive(Debug)]
pub struct Log {
    path: PathBuf,
    lock: Mutex<()>,
}

impl Log {
    pub fn new(path: PathBuf) -> Self {
        Self { path, lock: Mutex::new(()) }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }

    pub fn write(&self, message: &str) {
        let _guard = self.lock.lock().unwrap_or_else(|e| e.into_inner());
        let _ = self.append(message);
    }

    fn append(&self, message: &str) -> std::io::Result<()> {
        if let Some(parent) = self.path.parent().filter(|p| !p.as_os_str().is_empty()) {
            std::fs::create_dir_all(parent)?;
        }
        if std::fs::metadata(&self.path).is_ok_and(|m| m.len() > ROTATE_BYTES) {
            let mut previous = self.path.as_os_str().to_owned();
            previous.push(".previous");
            let _ = std::fs::remove_file(&previous);
            let _ = std::fs::rename(&self.path, &previous);
        }
        let mut file = std::fs::OpenOptions::new().create(true).append(true).open(&self.path)?;
        writeln!(file, "{} {message}", timestamp())
    }
}

#[cfg(windows)]
fn timestamp() -> String {
    // SAFETY: trivial Win32 call.
    let time = unsafe { windows::Win32::System::SystemInformation::GetLocalTime() };
    format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02}",
        time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond
    )
}
