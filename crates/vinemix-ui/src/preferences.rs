//! Storage for `ui.json` (desktop preferences and node groups).
//!
//! * Reads are bounded: an oversized file is treated as unreadable instead of
//!   being loaded whole.
//! * An unreadable regular file is moved aside (`ui.json.damaged-…`) before anything
//!   may replace it, and is never used to overwrite the last good copy.
//! * Each successful save first keeps the previous good file as `ui.json.bak`,
//!   then replaces `ui.json` with flushed data by rename.
//! * File writes run on a background thread and coalesce: the interface never
//!   waits on storage while editing, and quit waits for an acknowledgement.
//!
//! This is separate from the engine's `settings.json` recovery, which stays
//! an explicit choice in the engine.

use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Condvar, Mutex};
use std::time::{Duration, Instant};

/// Far above any real preferences file (a few kilobytes plus groups).
pub const MAXIMUM_BYTES: u64 = 4 * 1024 * 1024;

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ReadOutcome {
    Missing,
    Bytes(Vec<u8>),
    /// Present but not usable (too large or an I/O error).
    Unreadable(String),
}


/// Reads at most [`MAXIMUM_BYTES`].
pub fn read_bounded(path: &Path) -> ReadOutcome {
    let file = match std::fs::File::open(path) {
        Ok(file) => file,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return ReadOutcome::Missing,
        Err(error) => return ReadOutcome::Unreadable(error.to_string()),
    };
    let mut bytes = Vec::new();
    match file.take(MAXIMUM_BYTES + 1).read_to_end(&mut bytes) {
        Ok(_) if bytes.len() as u64 > MAXIMUM_BYTES => ReadOutcome::Unreadable("The file is larger than a preferences file can be.".into()),
        Ok(_) => ReadOutcome::Bytes(bytes),
        Err(error) => ReadOutcome::Unreadable(error.to_string()),
    }
}

fn sibling(path: &Path, suffix: &str) -> PathBuf {
    let mut name = path.file_name().map(|n| n.to_os_string()).unwrap_or_default();
    name.push(suffix);
    path.with_file_name(name)
}

/// The last-known-good copy kept beside the preferences file.
pub fn backup_path(path: &Path) -> PathBuf {
    sibling(path, ".bak")
}

/// Moves the damaged primary to a sibling without overwriting earlier evidence.
/// Moving within the directory avoids reading or duplicating oversized files.
pub fn preserve_damaged(path: &Path) -> Result<PathBuf, String> {
    use std::os::windows::ffi::OsStrExt;
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::{ERROR_ALREADY_EXISTS, ERROR_FILE_EXISTS};
    use windows::Win32::Storage::FileSystem::MoveFileW;

    let wide = |path: &Path| path.as_os_str().encode_wide().chain(Some(0)).collect::<Vec<_>>();
    let metadata = std::fs::symlink_metadata(path).map_err(|error| error.to_string())?;
    if !metadata.is_file() {
        return Err("The preferences path is not a regular file.".into());
    }
    let original = wide(path);
    let stamp = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_or(0, |d| d.as_secs());
    for attempt in 0..100u32 {
        let suffix = if attempt == 0 { format!(".damaged-{stamp}") } else { format!(".damaged-{stamp}-{attempt}") };
        let target = sibling(path, &suffix);
        let destination = wide(&target);
        // SAFETY: both buffers live through the call. MoveFileW refuses existing destinations.
        match unsafe { MoveFileW(PCWSTR(original.as_ptr()), PCWSTR(destination.as_ptr())) } {
            Ok(()) => return Ok(target),
            Err(e) if [ERROR_ALREADY_EXISTS.to_hresult(), ERROR_FILE_EXISTS.to_hresult()].contains(&e.code()) => continue,
            Err(e) => return Err(e.to_string()),
        }
    }
    Err("Too many preserved copies already exist.".into())
}

fn write_flushed(path: &Path, bytes: &[u8]) -> std::io::Result<()> {
    let mut file = std::fs::File::create(path)?;
    file.write_all(bytes)?;
    file.sync_all()
}

/// Replaces `path` with `json`. With `keep_backup`, the file being replaced
/// (which the caller knows to be good) first becomes `…​.bak`.
pub fn write_durable(path: &Path, json: &str, keep_backup: bool) -> Result<(), String> {
    (|| -> std::io::Result<()> {
        if let Some(directory) = path.parent() {
            std::fs::create_dir_all(directory)?;
        }
        let temporary = sibling(path, ".new");
        write_flushed(&temporary, json.as_bytes())?;
        if keep_backup && path.is_file() {
            // Build the backup completely before it takes the `.bak` name, so
            // an interruption never leaves a truncated last-good copy.
            let staging = sibling(path, ".bak.new");
            std::fs::copy(path, &staging)?;
            // Flush the copy before it takes the `.bak` name. Windows flushes
            // only through a handle opened for writing. The backup is a
            // best-effort safeguard, so failing to flush it never blocks the
            // save itself.
            let _ = std::fs::OpenOptions::new().write(true).open(&staging).and_then(|file| file.sync_all());
            std::fs::rename(&staging, backup_path(path))?;
        }
        std::fs::rename(&temporary, path)
    })()
    .map_err(|e| e.to_string())
}

/// Where a save goes: the real file, or a test/preview sink.
pub type Sink = Box<dyn FnMut(&str) -> Result<(), String> + Send>;

/// A sink that writes `path` durably. The first write keeps a backup only if
/// `primary_good`; every later write does (the file is then known good).
pub fn file_sink(path: PathBuf, mut primary_good: bool) -> Sink {
    Box::new(move |json| {
        write_durable(&path, json, primary_good)?;
        primary_good = true;
        Ok(())
    })
}

#[derive(Debug, Default)]
struct Shared {
    pending: Option<String>,
    submitted: u64,
    completed: u64,
    /// Result of the write that completed `completed`.
    error: Option<String>,
    writing: bool,
    stop: bool,
}

/// Outcome of the most recent completed write, reported once.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Completion {
    /// True when no newer save is queued or in progress.
    pub current: bool,
    pub result: Result<(), String>,
}

/// Coalescing background writer.
pub struct Writer {
    shared: Arc<(Mutex<Shared>, Condvar)>,
    thread: Option<std::thread::JoinHandle<()>>,
    reported: u64,
}

impl std::fmt::Debug for Writer {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Writer").field("reported", &self.reported).finish_non_exhaustive()
    }
}

fn guard<'a>(shared: &'a (Mutex<Shared>, Condvar)) -> std::sync::MutexGuard<'a, Shared> {
    shared.0.lock().unwrap_or_else(std::sync::PoisonError::into_inner)
}

impl Writer {
    pub fn new(mut sink: Sink) -> Result<Self, String> {
        let shared = Arc::new((Mutex::new(Shared::default()), Condvar::new()));
        let worker = shared.clone();
        let thread = std::thread::Builder::new()
            .name("vinemix-preferences".into())
            .spawn(move || loop {
                let (json, sequence) = {
                    let mut state = guard(&worker);
                    loop {
                        if let Some(json) = state.pending.take() {
                            state.writing = true;
                            break (json, state.submitted);
                        }
                        if state.stop {
                            return;
                        }
                        state = worker.1.wait(state).unwrap_or_else(std::sync::PoisonError::into_inner);
                    }
                };
                let result = sink(&json);
                let mut state = guard(&worker);
                state.writing = false;
                state.completed = sequence;
                state.error = result.err();
                worker.1.notify_all();
            })
            .map_err(|e| e.to_string())?;
        Ok(Self { shared, thread: Some(thread), reported: 0 })
    }

    /// Queues `json`, replacing any save that has not started yet.
    pub fn submit(&self, json: String) {
        let mut state = guard(&self.shared);
        state.pending = Some(json);
        state.submitted += 1;
        self.shared.1.notify_all();
    }

    pub fn idle(&self) -> bool {
        let state = guard(&self.shared);
        state.pending.is_none() && !state.writing
    }

    /// The newest completed write, if it has not been reported yet.
    pub fn poll(&mut self) -> Option<Completion> {
        let state = guard(&self.shared);
        if state.completed == self.reported {
            return None;
        }
        self.reported = state.completed;
        Some(Completion {
            current: state.completed == state.submitted && state.pending.is_none() && !state.writing,
            result: state.error.clone().map_or(Ok(()), Err),
        })
    }

    /// Waits until every submitted save has been written, up to `timeout`.
    /// Returns false if storage has not answered in time.
    pub fn flush(&self, timeout: Duration) -> bool {
        let deadline = Instant::now() + timeout;
        let mut state = guard(&self.shared);
        while state.pending.is_some() || state.writing {
            let Some(remaining) = deadline.checked_duration_since(Instant::now()).filter(|d| !d.is_zero()) else { return false };
            state = self.shared.1.wait_timeout(state, remaining).unwrap_or_else(std::sync::PoisonError::into_inner).0;
        }
        true
    }
}

impl Drop for Writer {
    fn drop(&mut self) {
        // Finish what was queued (bounded), then let the thread end. A sink
        // that never returns is abandoned rather than blocking exit.
        let finished = self.flush(Duration::from_secs(5));
        guard(&self.shared).stop = true;
        self.shared.1.notify_all();
        if let (true, Some(thread)) = (finished, self.thread.take()) {
            let _ = thread.join();
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    fn directory(name: &str) -> PathBuf {
        let path = std::env::temp_dir().join(format!("vinemix-preferences-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&path);
        std::fs::create_dir_all(&path).unwrap();
        path
    }

    #[test]
    fn bounded_read_refuses_oversized_files() {
        let dir = directory("bounded");
        let path = dir.join("ui.json");
        assert_eq!(read_bounded(&path), ReadOutcome::Missing);
        std::fs::write(&path, b"{}").unwrap();
        assert_eq!(read_bounded(&path), ReadOutcome::Bytes(b"{}".to_vec()));
        let file = std::fs::File::create(&path).unwrap();
        file.set_len(MAXIMUM_BYTES + 1).unwrap();
        drop(file);
        assert!(matches!(read_bounded(&path), ReadOutcome::Unreadable(_)));
        let _ = std::fs::remove_dir_all(dir);
    }

    #[test]
    fn damaged_copies_never_overwrite_each_other() {
        let dir = directory("damaged");
        let path = dir.join("ui.json");
        std::fs::write(&path, b"first damaged").unwrap();
        let first = preserve_damaged(&path).unwrap();
        std::fs::write(&path, b"second damaged").unwrap();
        let second = preserve_damaged(&path).unwrap();
        assert_ne!(first, second);
        assert_eq!(std::fs::read(&first).unwrap(), b"first damaged");
        assert_eq!(std::fs::read(&second).unwrap(), b"second damaged");
        assert!(!path.exists(), "a later launch cannot preserve the same damaged file twice");
        let _ = std::fs::remove_dir_all(dir);
    }

    #[test]
    fn durable_write_keeps_the_previous_good_file_as_backup() {
        let dir = directory("backup");
        let path = dir.join("ui.json");
        write_durable(&path, "one", true).unwrap();
        assert!(!backup_path(&path).exists(), "nothing to back up on the first save");
        write_durable(&path, "two", true).unwrap();
        assert_eq!(std::fs::read_to_string(backup_path(&path)).unwrap(), "one");
        // A damaged primary is replaced without destroying the good backup.
        std::fs::write(&path, b"damaged").unwrap();
        write_durable(&path, "three", false).unwrap();
        assert_eq!(std::fs::read_to_string(backup_path(&path)).unwrap(), "one");
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "three");
        // No temporary files remain.
        let names: Vec<String> = std::fs::read_dir(&dir).unwrap().map(|e| e.unwrap().file_name().to_string_lossy().into_owned()).collect();
        assert!(names.iter().all(|n| n == "ui.json" || n == "ui.json.bak"), "{names:?}");
        let _ = std::fs::remove_dir_all(dir);
    }

    #[test]
    fn interrupted_write_leaves_the_original_intact() {
        let dir = directory("interrupted");
        let path = dir.join("ui.json");
        write_durable(&path, "good", true).unwrap();
        // An interruption after the temporary file was written but before the
        // rename: the destination is untouched and the next save succeeds.
        std::fs::write(sibling(&path, ".new"), b"partial").unwrap();
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "good");
        write_durable(&path, "next", true).unwrap();
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "next");
        assert_eq!(std::fs::read_to_string(backup_path(&path)).unwrap(), "good");
        let _ = std::fs::remove_dir_all(dir);
    }

    #[test]
    fn slow_storage_does_not_block_submit_and_coalesces() {
        let writes = Arc::new(AtomicUsize::new(0));
        let last = Arc::new(Mutex::new(String::new()));
        let (count, seen) = (writes.clone(), last.clone());
        let mut writer = Writer::new(Box::new(move |json| {
            std::thread::sleep(Duration::from_millis(150));
            count.fetch_add(1, Ordering::SeqCst);
            *seen.lock().unwrap() = json.to_owned();
            Ok(())
        }))
        .unwrap();
        let started = Instant::now();
        for i in 0..50 {
            writer.submit(format!("save {i}"));
        }
        assert!(started.elapsed() < Duration::from_millis(100), "submitting must not wait for storage: {:?}", started.elapsed());
        assert!(!writer.flush(Duration::from_millis(1)), "a flush reports storage that has not answered");
        assert!(writer.flush(Duration::from_secs(5)));
        assert_eq!(*last.lock().unwrap(), "save 49", "the newest save always lands");
        assert!(writes.load(Ordering::SeqCst) <= 3, "50 saves coalesced into {} writes", writes.load(Ordering::SeqCst));
        assert_eq!(writer.poll(), Some(Completion { current: true, result: Ok(()) }));
        assert_eq!(writer.poll(), None, "a completion is reported once");
    }

    #[test]
    fn failing_storage_is_reported_and_a_later_save_recovers() {
        let fail = Arc::new(std::sync::atomic::AtomicBool::new(true));
        let flag = fail.clone();
        let mut writer = Writer::new(Box::new(move |_| if flag.load(Ordering::SeqCst) { Err("disk full".into()) } else { Ok(()) })).unwrap();
        writer.submit("a".into());
        assert!(writer.flush(Duration::from_secs(5)));
        assert_eq!(writer.poll(), Some(Completion { current: true, result: Err("disk full".into()) }));
        fail.store(false, Ordering::SeqCst);
        writer.submit("b".into());
        assert!(writer.flush(Duration::from_secs(5)));
        assert_eq!(writer.poll(), Some(Completion { current: true, result: Ok(()) }));
    }

    #[test]
    fn file_sink_backs_up_only_a_good_primary() {
        let dir = directory("sink");
        let path = dir.join("ui.json");
        std::fs::write(&path, b"damaged").unwrap();
        let mut sink = file_sink(path.clone(), false);
        sink("one").unwrap();
        assert!(!backup_path(&path).exists(), "a damaged primary never becomes the backup");
        sink("two").unwrap();
        assert_eq!(std::fs::read_to_string(backup_path(&path)).unwrap(), "one");
        let _ = std::fs::remove_dir_all(dir);
    }
}
