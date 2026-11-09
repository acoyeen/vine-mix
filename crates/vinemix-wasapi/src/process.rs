//! Per-application capture through Windows process loopback, and read-only
//! discovery of capturable applications (`process_capture.cpp`).
//!
//! Capture copies an application's rendered audio (and its process tree); it
//! never changes the application's own playback device, mute or volume.

use std::collections::{BTreeMap, BTreeSet};
use std::mem::ManuallyDrop;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use std::time::{Duration, Instant};

use vinemix_model::config::ProcessBinding;
use vinemix_model::status::{ApplicationInfo, PathState};
use vinemix_resample::AdaptiveResampler;
use vinemix_rt::{DriftController, Frame, Monitor, Producer, SAMPLE_RATE};
use windows::core::{implement, Interface, BOOL, HRESULT, PCWSTR, PWSTR};
use windows::Win32::Foundation::{
    E_ACCESSDENIED, E_FAIL, E_NOTIMPL, E_POINTER, FILETIME, HWND, LPARAM, NTSTATUS,
};
use windows::Win32::Globalization::{CompareStringOrdinal, CSTR_EQUAL};
use windows::Win32::Storage::FileSystem::CompareFileTime;
use windows::Win32::Media::Audio::{
    ActivateAudioInterfaceAsync, IActivateAudioInterfaceAsyncOperation, IActivateAudioInterfaceCompletionHandler,
    IActivateAudioInterfaceCompletionHandler_Impl, IAudioCaptureClient, IAudioClient, IAudioSessionControl2, IAudioSessionManager2,
    IMMDeviceEnumerator, MMDeviceEnumerator, AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY, AUDCLNT_BUFFERFLAGS_SILENT,
    AUDCLNT_E_SERVICE_NOT_RUNNING, AUDCLNT_E_UNSUPPORTED_FORMAT, AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
    AUDCLNT_STREAMFLAGS_EVENTCALLBACK, AUDCLNT_STREAMFLAGS_LOOPBACK, AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, AUDIOCLIENT_ACTIVATION_PARAMS,
    AUDIOCLIENT_ACTIVATION_PARAMS_0, AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK, AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS,
    DEVICE_STATE_ACTIVE, PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE, VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, WAVEFORMATEX,
    eRender,
};
use windows::Win32::System::Com::StructuredStorage::{PROPVARIANT, PROPVARIANT_0, PROPVARIANT_0_0, PROPVARIANT_0_0_0};
use windows::Win32::System::Com::{CoCreateInstance, IAgileObject, IAgileObject_Impl, BLOB, CLSCTX_ALL};
use windows::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS,
};
use windows::Win32::System::LibraryLoader::{GetModuleHandleW, GetProcAddress};
use windows::Win32::System::RemoteDesktop::ProcessIdToSessionId;
use windows::Win32::System::SystemInformation::OSVERSIONINFOW;
use windows::Win32::System::Threading::{
    GetCurrentProcessId, GetProcessTimes, OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32,
    PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_SYNCHRONIZE,
};
use windows::Win32::System::Variant::VT_BLOB;
use windows::Win32::UI::WindowsAndMessaging::{
    EnumWindows, GetWindow, GetWindowTextLengthW, GetWindowTextW, GetWindowThreadProcessId, IsWindowVisible, GW_OWNER,
};

use crate::win::{hresult_from_win32, wait_any, wide, Apartment, Event, OwnedHandle, Wait, ERROR_TIMEOUT, FOREVER};
use crate::worker::mmcss;

fn same_path(a: &[u16], b: &[u16]) -> bool {
    // SAFETY: valid slices; CompareStringOrdinal reads exactly their lengths.
    !a.is_empty() && !b.is_empty() && unsafe { CompareStringOrdinal(a, b, true) } == CSTR_EQUAL
}

fn strip_nul(text: &[u16]) -> &[u16] {
    let end = text.iter().position(|&c| c == 0).unwrap_or(text.len());
    &text[..end]
}

fn image_path_of(process: &OwnedHandle) -> Vec<u16> {
    let mut buffer = vec![0u16; 32768];
    let mut size = buffer.len() as u32;
    // SAFETY: valid handle and buffer.
    let ok = unsafe { QueryFullProcessImageNameW(process.0, PROCESS_NAME_WIN32, PWSTR(buffer.as_mut_ptr()), &mut size) }.is_ok();
    if !ok {
        return Vec::new();
    }
    buffer.truncate(size as usize);
    buffer
}

fn open(pid: u32, access: windows::Win32::System::Threading::PROCESS_ACCESS_RIGHTS) -> OwnedHandle {
    // SAFETY: plain Win32 call; the handle is owned and closed on drop.
    OwnedHandle(unsafe { OpenProcess(access, false, pid) }.unwrap_or_default())
}

fn image_path(pid: u32) -> Vec<u16> {
    let process = open(pid, PROCESS_QUERY_LIMITED_INFORMATION);
    if process.is_valid() { image_path_of(&process) } else { Vec::new() }
}

fn creation_time(pid: u32) -> Option<FILETIME> {
    let process = open(pid, PROCESS_QUERY_LIMITED_INFORMATION);
    if !process.is_valid() {
        return None;
    }
    let (mut created, mut a, mut b, mut c) = (FILETIME::default(), FILETIME::default(), FILETIME::default(), FILETIME::default());
    // SAFETY: valid handle and output pointers.
    unsafe { GetProcessTimes(process.0, &mut created, &mut a, &mut b, &mut c) }.ok()?;
    Some(created)
}

fn parent_predates_child(parent: u32, child: u32) -> bool {
    match (creation_time(parent), creation_time(child)) {
        // SAFETY: valid FILETIME pointers.
        (Some(p), Some(c)) => (unsafe { CompareFileTime(&p, &c) }) <= 0,
        _ => false,
    }
}

fn basename(path: &[u16]) -> &[u16] {
    let start = path.iter().rposition(|&c| c == u16::from(b'\\') || c == u16::from(b'/')).map_or(0, |i| i + 1);
    &path[start..]
}

/// Whether `path` is a VineMix (or legacy Audio Mixer) process.
fn is_mixer(path: &[u16]) -> bool {
    let name = basename(path);
    [
        "VineMix.Engine.exe",
        "VineMix.exe",
        "VineMix.Desktop.exe",
        "vinemix-engine.exe",
        "vinemix.exe",
        "AudioMixer.Engine.exe",
        "AudioMixer.exe",
        "AudioMixer.Desktop.exe",
    ]
    .iter()
    .any(|candidate| same_path(name, strip_nul(&wide(candidate))))
}

#[derive(Clone, Debug)]
struct ProcessEntry {
    parent: u32,
    filename: Vec<u16>,
}

type ProcessTable = BTreeMap<u32, ProcessEntry>;

/// Processes in this Windows session.
fn process_table() -> ProcessTable {
    let mut result = ProcessTable::new();
    // SAFETY: snapshot handle owned and closed on drop.
    let Ok(snapshot) = (unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) }) else { return result };
    let snapshot = OwnedHandle(snapshot);
    let mut this_session = 0u32;
    // SAFETY: valid output pointer.
    if unsafe { ProcessIdToSessionId(GetCurrentProcessId(), &mut this_session) }.is_err() {
        return result;
    }
    let mut entry = PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
    // SAFETY: valid snapshot and entry.
    if unsafe { Process32FirstW(snapshot.0, &mut entry) }.is_err() {
        return result;
    }
    loop {
        let mut session = 0u32;
        // SAFETY: valid output pointer.
        if entry.th32ProcessID != 0
            && unsafe { ProcessIdToSessionId(entry.th32ProcessID, &mut session) }.is_ok()
            && session == this_session
        {
            result.insert(
                entry.th32ProcessID,
                ProcessEntry { parent: entry.th32ParentProcessID, filename: strip_nul(&entry.szExeFile).to_vec() },
            );
        }
        // SAFETY: as above.
        if unsafe { Process32NextW(snapshot.0, &mut entry) }.is_err() {
            break;
        }
    }
    result
}

/// Whether `root`'s process tree contains this engine (feedback guard).
fn contains_mixer_process(root: u32, table: &ProcessTable) -> bool {
    // SAFETY: trivial Win32 call.
    let mut current = unsafe { GetCurrentProcessId() };
    let mut visited = BTreeSet::new();
    while current != 0 && visited.insert(current) {
        if current == root {
            return true;
        }
        match table.get(&current) {
            Some(entry) => current = entry.parent,
            None => break,
        }
    }
    false
}

/// Walks up through same-executable parents to the application root.
fn same_application_root(mut pid: u32, path: &[u16], table: &ProcessTable) -> u32 {
    let mut visited = BTreeSet::new();
    while visited.insert(pid) {
        let Some(child) = table.get(&pid) else { break };
        if child.parent == pid {
            break;
        }
        let Some(parent) = table.get(&child.parent) else { break };
        if !same_path(&parent.filename, basename(path)) {
            break;
        }
        // Toolhelp keeps a numeric parent PID after the parent exits; never
        // treat a newer process reusing that PID as the original parent.
        if !parent_predates_child(child.parent, pid) {
            break;
        }
        if !same_path(&image_path(child.parent), path) {
            break;
        }
        pid = child.parent;
    }
    pid
}

#[derive(Debug, Default)]
struct Candidate {
    audio: bool,
}

extern "system" fn collect_window(window: HWND, context: LPARAM) -> BOOL {
    // SAFETY: `context` is the candidates map passed to EnumWindows below,
    // alive for the whole enumeration on this thread.
    unsafe {
        let candidates = &mut *(context.0 as *mut BTreeMap<u32, Candidate>);
        if !IsWindowVisible(window).as_bool() || GetWindow(window, GW_OWNER).is_ok_and(|w| !w.0.is_null()) {
            return true.into();
        }
        let length = GetWindowTextLengthW(window);
        if length <= 0 {
            return true.into();
        }
        let mut pid = 0u32;
        GetWindowThreadProcessId(window, Some(&mut pid));
        let mut title = vec![0u16; length as usize + 1];
        let copied = GetWindowTextW(window, &mut title);
        if copied > 0 {
            candidates.entry(pid).or_default();
        }
    }
    true.into()
}

fn collect_audio_sessions(candidates: &mut BTreeMap<u32, Candidate>) {
    // SAFETY: COM calls on this MTA thread.
    unsafe {
        let Ok(enumerator): windows::core::Result<IMMDeviceEnumerator> = CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) else {
            return;
        };
        let Ok(endpoints) = enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE) else { return };
        let Ok(count) = endpoints.GetCount() else { return };
        for i in 0..count {
            let Ok(endpoint) = endpoints.Item(i) else { continue };
            let Ok(manager) = endpoint.Activate::<IAudioSessionManager2>(CLSCTX_ALL, None) else { continue };
            let Ok(sessions) = manager.GetSessionEnumerator() else { continue };
            let Ok(session_count) = sessions.GetCount() else { continue };
            for j in 0..session_count {
                let Ok(session) = sessions.GetSession(j) else { continue };
                let Ok(session2) = session.cast::<IAudioSessionControl2>() else { continue };
                if session2.IsSystemSoundsSession() == HRESULT(0) {
                    continue;
                }
                if let Ok(pid) = session2.GetProcessId() {
                    if pid != 0 {
                        candidates.entry(pid).or_default().audio = true;
                    }
                }
            }
        }
    }
}

fn supported_windows() -> bool {
    type Version = unsafe extern "system" fn(*mut OSVERSIONINFOW) -> NTSTATUS;
    let module = wide("ntdll.dll");
    // SAFETY: ntdll is always loaded; RtlGetVersion has this signature.
    unsafe {
        let Ok(module) = GetModuleHandleW(PCWSTR(module.as_ptr())) else { return false };
        let Some(function) = GetProcAddress(module, windows::core::s!("RtlGetVersion")) else { return false };
        let function: Version = std::mem::transmute(function);
        let mut version = OSVERSIONINFOW { dwOSVersionInfoSize: std::mem::size_of::<OSVERSIONINFOW>() as u32, ..Default::default() };
        function(&mut version).is_ok()
            && (version.dwMajorVersion > 10 || (version.dwMajorVersion == 10 && version.dwBuildNumber >= 20348))
    }
}

/// Process loopback needs Windows build 20348 or later.
pub fn process_capture_available() -> bool {
    supported_windows()
}

/// Applications with visible windows or audio sessions, grouped by their
/// root process and labelled by executable name (stable while titles change).
pub fn enumerate_audio_applications() -> Vec<ApplicationInfo> {
    let apartment = Apartment::enter();
    let table = process_table();
    let mut candidates: BTreeMap<u32, Candidate> = BTreeMap::new();
    // SAFETY: the callback only touches `candidates` during this call.
    unsafe {
        let _ = EnumWindows(Some(collect_window), LPARAM(&raw mut candidates as isize));
    }
    if apartment.ok() {
        collect_audio_sessions(&mut candidates);
    }
    let mut roots: BTreeMap<u32, ApplicationInfo> = BTreeMap::new();
    for (&pid, candidate) in &candidates {
        if !table.contains_key(&pid) {
            continue;
        }
        let path = image_path(pid);
        if path.is_empty() || is_mixer(&path) {
            continue;
        }
        let root = same_application_root(pid, &path, &table);
        // Capturing a launcher ancestor would include this engine's renders.
        if contains_mixer_process(root, &table) {
            continue;
        }
        let info = roots.entry(root).or_default();
        info.process_id = root;
        info.executable_path = String::from_utf16_lossy(&path);
        let mut name = basename(&path).to_vec();
        if name.len() > 4 && same_path(&name[name.len() - 4..], strip_nul(&wide(".exe"))) {
            name.truncate(name.len() - 4);
        }
        info.name = String::from_utf16_lossy(&name);
        info.has_audio_session = info.has_audio_session || candidate.audio;
    }
    let mut result: Vec<ApplicationInfo> = roots.into_values().collect();
    result.sort_by(|a, b| {
        b.has_audio_session
            .cmp(&a.has_audio_session)
            .then_with(|| a.name.cmp(&b.name))
            .then_with(|| a.process_id.cmp(&b.process_id))
    });
    result
}

/// Shared by the worker and the completion handler. The activation
/// parameters live here (not on the worker's stack): Windows reads them
/// asynchronously, and a worker that stops waiting (the app exited, Stop,
/// a new selection, a timeout) must not free memory an in-flight activation
/// still uses. The handler keeps this alive until `ActivateCompleted`, as the
/// 0.5 engine's `ActivationState` does.
struct ActivationState {
    completed: Event,
    result: Mutex<Option<windows::core::Result<IAudioClient>>>,
    params: AUDIOCLIENT_ACTIVATION_PARAMS,
    /// Never dropped: the `windows` crate's `Drop for PROPVARIANT` calls
    /// `PropVariantClear`, which would hand `params` (Rust memory) to
    /// `CoTaskMemFree` and corrupt the heap. This variant owns nothing.
    variant: ManuallyDrop<PROPVARIANT>,
}

// SAFETY: `variant` only points into `params` in the same allocation; both
// are written once before the state is shared and are read-only afterwards.
unsafe impl Send for ActivationState {}
// SAFETY: as above; the mutable parts are an Event handle and a Mutex.
unsafe impl Sync for ActivationState {}

impl std::fmt::Debug for ActivationState {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("ActivationState").field("target", &unsafe { self.params.Anonymous.ProcessLoopbackParams.TargetProcessId }).finish_non_exhaustive()
    }
}

/// Starts process-loopback activation for `pid` (its whole process tree).
/// The returned state stays valid for as long as Windows may still use it,
/// whether or not the caller waits for completion.
fn begin_activation(pid: u32) -> windows::core::Result<(IActivateAudioInterfaceAsyncOperation, Arc<ActivationState>)> {
    let mut state = Arc::new(ActivationState {
        completed: Event::new(true)?,
        result: Mutex::new(None),
        params: AUDIOCLIENT_ACTIVATION_PARAMS {
            ActivationType: AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
            Anonymous: AUDIOCLIENT_ACTIVATION_PARAMS_0 {
                ProcessLoopbackParams: AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS {
                    TargetProcessId: pid,
                    ProcessLoopbackMode: PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE,
                },
            },
        },
        variant: ManuallyDrop::new(PROPVARIANT::default()),
    });
    {
        let unique = Arc::get_mut(&mut state).expect("not yet shared");
        let blob = BLOB { cbSize: std::mem::size_of::<AUDIOCLIENT_ACTIVATION_PARAMS>() as u32, pBlobData: (&raw mut unique.params).cast() };
        unique.variant = ManuallyDrop::new(PROPVARIANT {
            Anonymous: PROPVARIANT_0 {
                Anonymous: ManuallyDrop::new(PROPVARIANT_0_0 { vt: VT_BLOB, wReserved1: 0, wReserved2: 0, wReserved3: 0, Anonymous: PROPVARIANT_0_0_0 { blob } }),
            },
        });
    }
    let handler: IActivateAudioInterfaceCompletionHandler = ActivationHandler { state: state.clone() }.into();
    // SAFETY: `state.variant` and the `params` it points to are owned by the
    // handler's reference, which Windows holds until `ActivateCompleted`.
    let operation = unsafe { ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, &IAudioClient::IID, Some(&*state.variant), &handler)? };
    Ok((operation, state))
}

#[implement(IActivateAudioInterfaceCompletionHandler, IAgileObject)]
struct ActivationHandler {
    state: Arc<ActivationState>,
}

impl IAgileObject_Impl for ActivationHandler_Impl {}

impl IActivateAudioInterfaceCompletionHandler_Impl for ActivationHandler_Impl {
    fn ActivateCompleted(&self, operation: windows::core::Ref<IActivateAudioInterfaceAsyncOperation>) -> windows::core::Result<()> {
        let result = (|| -> windows::core::Result<IAudioClient> {
            let operation = operation.ok()?;
            let mut activated = HRESULT(0);
            let mut value: Option<windows::core::IUnknown> = None;
            // SAFETY: valid output pointers for the completed operation.
            unsafe { operation.GetActivateResult(&mut activated, &mut value)? };
            activated.ok()?;
            value.ok_or_else(|| windows::core::Error::from(E_POINTER))?.cast::<IAudioClient>()
        })();
        *self.state.result.lock().unwrap_or_else(|e| e.into_inner()) = Some(result);
        self.state.completed.set();
        Ok(())
    }
}

fn process_failure_message(result: HRESULT) -> String {
    if result == E_ACCESSDENIED {
        "Windows denied app audio capture".into()
    } else if result == AUDCLNT_E_SERVICE_NOT_RUNNING {
        "Windows Audio service unavailable".into()
    } else if result == AUDCLNT_E_UNSUPPORTED_FORMAT {
        "App capture format unavailable".into()
    } else if result == hresult_from_win32(ERROR_TIMEOUT) {
        "Windows app capture did not respond; use Retry audio".into()
    } else {
        format!("App capture unavailable (0x{:x})", result.0 as u32)
    }
}

#[derive(Debug, Default)]
struct Guarded {
    desired: ProcessBinding,
    path: PathState,
}

#[derive(Debug)]
struct Shared {
    monitor: Monitor,
    stop_event: Event,
    control_event: Event,
    guarded: Mutex<Guarded>,
    serial: AtomicU64,
    stopped: AtomicBool,
    running: AtomicBool,
}

impl Shared {
    fn lock(&self) -> std::sync::MutexGuard<'_, Guarded> {
        self.guarded.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn state(&self, status: &str, message: &str, pid: u32, reason: &str) {
        self.running.store(status == "Running", Ordering::Release);
        let mut guarded = self.lock();
        let path = &mut guarded.path;
        path.state = status.into();
        path.message = message.into();
        path.reason = reason.into();
        path.endpoint_id = if pid != 0 { format!("process:{pid}") } else { String::new() };
        path.channels = if pid != 0 { 2 } else { 0 };
        path.rate = if pid != 0 { SAMPLE_RATE } else { 0 };
    }
}

/// Captures one application's audio tree into a frame queue.
#[derive(Debug)]
pub struct ProcessCaptureWorker {
    shared: Arc<Shared>,
    thread: Mutex<Option<JoinHandle<()>>>,
}

impl ProcessCaptureWorker {
    pub fn new(producer: Producer, monitor: Monitor) -> windows::core::Result<Self> {
        let shared = Arc::new(Shared {
            monitor,
            stop_event: Event::new(true)?,
            control_event: Event::new(false)?,
            guarded: Mutex::new(Guarded::default()),
            serial: AtomicU64::new(0),
            stopped: AtomicBool::new(false),
            running: AtomicBool::new(false),
        });
        let worker = shared.clone();
        let thread = std::thread::Builder::new()
            .name("vinemix-app-capture".into())
            .spawn(move || run(&worker, producer))
            .map_err(|_| windows::core::Error::from(E_FAIL))?;
        Ok(Self { shared, thread: Mutex::new(Some(thread)) })
    }

    pub fn configure(&self, binding: ProcessBinding) {
        {
            let mut guarded = self.shared.lock();
            let old = &guarded.desired;
            if old.process_id == binding.process_id && old.executable_path == binding.executable_path && old.name == binding.name {
                return;
            }
            guarded.desired = binding;
            self.shared.serial.fetch_add(1, Ordering::Release);
        }
        self.shared.control_event.set();
    }

    pub fn retry(&self) {
        self.shared.serial.fetch_add(1, Ordering::Release);
        self.shared.control_event.set();
    }

    pub fn ready(&self) -> bool {
        self.shared.running.load(Ordering::Acquire)
    }

    pub fn status(&self) -> PathState {
        let mut path = self.shared.lock().path.clone();
        let stats = self.shared.monitor.stats();
        path.queue_ms = self.shared.monitor.size() as f64 * 1000.0 / f64::from(SAMPLE_RATE);
        path.underruns = stats.underrun_events;
        path.overruns = stats.overrun_events;
        path
    }

    pub fn stop(&self) {
        self.shared.running.store(false, Ordering::Release);
        if !self.shared.stopped.swap(true, Ordering::AcqRel) {
            self.shared.stop_event.set();
            self.shared.control_event.set();
        }
        if let Some(thread) = self.thread.lock().unwrap_or_else(|e| e.into_inner()).take() {
            let _ = thread.join();
        }
    }
}

impl Drop for ProcessCaptureWorker {
    fn drop(&mut self) {
        self.stop();
    }
}

fn resolve(selection: &ProcessBinding, ambiguous: &mut bool) -> Option<u32> {
    let path = strip_nul(&wide(&selection.executable_path)).to_vec();
    if path.is_empty() || is_mixer(&path) {
        return None;
    }
    let table = process_table();
    if selection.process_id != 0 && table.contains_key(&selection.process_id) && same_path(&image_path(selection.process_id), &path) {
        let root = same_application_root(selection.process_id, &path, &table);
        return (!contains_mixer_process(root, &table)).then_some(root);
    }
    let mut roots = BTreeSet::new();
    for (&pid, entry) in &table {
        if same_path(&entry.filename, basename(&path)) && same_path(&image_path(pid), &path) {
            let root = same_application_root(pid, &path, &table);
            if !contains_mixer_process(root, &table) {
                roots.insert(root);
            }
        }
    }
    *ambiguous = roots.len() > 1;
    if roots.len() == 1 { roots.into_iter().next() } else { None }
}

/// A stream that ended deliberately is `Ok`.
fn capture(shared: &Shared, producer: &mut Producer, pid: u32, selection: &ProcessBinding, selected_serial: u64) -> Result<(), HRESULT> {
    let process = open(pid, PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SYNCHRONIZE);
    if !process.is_valid() {
        return Err(hresult_from_win32(std::io::Error::last_os_error().raw_os_error().unwrap_or(0) as u32));
    }
    if !same_path(&image_path_of(&process), strip_nul(&wide(&selection.executable_path))) {
        return Ok(());
    }
    // Early returns below may abandon an activation in flight (Chrome closing
    // exits many processes at once); `begin_activation` keeps that safe.
    let (operation, state) = begin_activation(pid).map_err(|e| e.code())?;
    let activation_events = [shared.stop_event.handle(), shared.control_event.handle(), process.0, state.completed.handle()];
    let deadline = Instant::now() + Duration::from_secs(10);
    loop {
        let wait = wait_any(&activation_events, 250);
        if matches!(wait, Wait::Signalled(0) | Wait::Signalled(2)) || shared.serial.load(Ordering::Acquire) != selected_serial {
            return Ok(());
        }
        if wait == Wait::Signalled(3) {
            break;
        }
        if wait == Wait::Failed {
            return Err(E_FAIL);
        }
        if Instant::now() >= deadline {
            return Err(hresult_from_win32(ERROR_TIMEOUT));
        }
    }
    drop(operation);
    let client = state
        .result
        .lock()
        .unwrap_or_else(|e| e.into_inner())
        .take()
        .unwrap_or_else(|| Err(E_POINTER.into()))
        .map_err(|e| e.code())?;
    let format = WAVEFORMATEX {
        wFormatTag: crate::format::WAVE_FORMAT_IEEE_FLOAT,
        nChannels: 2,
        nSamplesPerSec: SAMPLE_RATE,
        nAvgBytesPerSec: SAMPLE_RATE * 8,
        nBlockAlign: 8,
        wBitsPerSample: 32,
        cbSize: 0,
    };
    // SAFETY: valid format for the duration of the call.
    unsafe {
        client.Initialize(
            AUDCLNT_SHAREMODE_SHARED,
            AUDCLNT_STREAMFLAGS_LOOPBACK
                | AUDCLNT_STREAMFLAGS_EVENTCALLBACK
                | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
            0,
            0,
            &format,
            None,
        )
    }
    .map_err(|e| e.code())?;
    let audio_event = Event::new(false).map_err(|e| e.code())?;
    // SAFETY: COM calls on the initialised client.
    unsafe { client.SetEventHandle(audio_event.handle()) }.map_err(|e| e.code())?;
    // SAFETY: as above.
    let capture_client: IAudioCaptureClient = unsafe { client.GetService() }.map_err(|e| e.code())?;
    let mut input = [Frame::SILENT; 4096];
    let mut output = [Frame::SILENT; 4096];
    let mut resampler = AdaptiveResampler::new(SAMPLE_RATE, SAMPLE_RATE, AdaptiveResampler::DEFAULT_CAPACITY).map_err(|_| E_FAIL)?;
    let mut drift = DriftController::new();
    let _mmcss = mmcss("Audio", false);
    // SAFETY: starting the initialised client.
    unsafe { client.Start() }.map_err(|e| e.code())?;
    struct Stop<'a>(&'a IAudioClient);
    impl Drop for Stop<'_> {
        fn drop(&mut self) {
            // SAFETY: stopping a started client.
            unsafe {
                let _ = self.0.Stop();
            }
        }
    }
    let _stop = Stop(&client);
    producer.request_clear();
    shared.lock().path.reconnects += 1;
    shared.state("Running", "Capturing app audio; original playback remains unchanged", pid, "");
    let events = [shared.stop_event.handle(), shared.control_event.handle(), process.0, audio_event.handle()];
    let mut last_packet = Instant::now();
    let fade_length = SAMPLE_RATE / 100;
    let mut fade = 0u32;
    let mut suspended = false;
    while !shared.stopped.load(Ordering::Relaxed) {
        let wait = wait_any(&events, 250);
        if matches!(wait, Wait::Signalled(0) | Wait::Signalled(2)) || shared.serial.load(Ordering::Acquire) != selected_serial {
            return Ok(());
        }
        if wait == Wait::Failed {
            return Err(E_FAIL);
        }
        let now = Instant::now();
        // A paused app is healthy silence: never reopen it, just discard
        // stale filter state.
        if now - last_packet > Duration::from_millis(250) && !suspended {
            resampler.reset();
            drift.reset();
            producer.request_clear();
            fade = 0;
            suspended = true;
        }
        // SAFETY: COM calls on the started capture client.
        let mut packet = unsafe { capture_client.GetNextPacketSize() }.map_err(|e| e.code())?;
        while packet != 0 {
            let mut data: *mut u8 = std::ptr::null_mut();
            let (mut count, mut flags) = (0u32, 0u32);
            // SAFETY: valid output pointers; released below.
            unsafe { capture_client.GetBuffer(&mut data, &mut count, &mut flags, None, None) }.map_err(|e| e.code())?;
            let elapsed = (now - last_packet).as_secs_f64().clamp(0.001, 0.25);
            if flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY.0 as u32 != 0 {
                // Keep the queued audio and resampler history (see the device
                // worker); only soften the splice.
                fade = 0;
            }
            last_packet = now;
            suspended = false;
            let correction = drift.update(producer.size() as f64, f64::from(SAMPLE_RATE) * 0.015, elapsed);
            let silent = flags & AUDCLNT_BUFFERFLAGS_SILENT.0 as u32 != 0;
            let mut offset = 0usize;
            while offset < count as usize {
                let frames = input.len().min(count as usize - offset);
                for (i, frame) in input.iter_mut().enumerate().take(frames) {
                    let mut sample = Frame::SILENT;
                    if !silent && !data.is_null() {
                        // SAFETY: the packet holds `count` float stereo frames.
                        sample = unsafe { std::ptr::read_unaligned(data.cast::<Frame>().add(offset + i)) };
                    }
                    fade += 1;
                    let gain = fade.min(fade_length) as f32 / fade_length as f32;
                    // Keep the bounded fade counter from wrapping on a long soak.
                    fade = fade.min(fade_length);
                    *frame = Frame::new(
                        if sample.left.is_finite() { sample.left * gain } else { 0.0 },
                        if sample.right.is_finite() { sample.right * gain } else { 0.0 },
                    );
                }
                if resampler.push(&input[..frames]) != frames {
                    resampler.reset();
                    producer.request_clear();
                    fade = 0;
                }
                loop {
                    let generated = resampler.produce(&mut output, correction);
                    if resampler.last_error() != 0 {
                        // SAFETY: releasing the buffer obtained above.
                        unsafe {
                            let _ = capture_client.ReleaseBuffer(count);
                        }
                        return Err(E_FAIL);
                    }
                    if generated == 0 {
                        break;
                    }
                    if producer.push(&output[..generated]) != generated {
                        producer.request_clear();
                    }
                    if generated < output.len() {
                        break;
                    }
                }
                offset += frames;
            }
            // SAFETY: releasing the buffer obtained above.
            unsafe { capture_client.ReleaseBuffer(count) }.map_err(|e| e.code())?;
            // SAFETY: as above.
            packet = unsafe { capture_client.GetNextPacketSize() }.map_err(|e| e.code())?;
            if shared.stopped.load(Ordering::Relaxed) || shared.serial.load(Ordering::Acquire) != selected_serial {
                return Ok(());
            }
        }
    }
    Ok(())
}

fn run(shared: &Shared, mut producer: Producer) {
    let apartment = Apartment::enter();
    let events = [shared.stop_event.handle(), shared.control_event.handle()];
    let delays = [250u32, 1000, 2000, 5000];
    let mut failures = 0usize;
    let mut previous_serial = 0u64;
    while !shared.stopped.load(Ordering::Relaxed) {
        let (selection, selected_serial) = {
            let guarded = shared.lock();
            (guarded.desired.clone(), shared.serial.load(Ordering::Acquire))
        };
        if selected_serial != previous_serial {
            failures = 0;
            previous_serial = selected_serial;
        }
        let mut delay = 1000u32;
        if selection.executable_path.is_empty() {
            shared.state("Unassigned", "Choose a running application", 0, "");
            delay = FOREVER;
        } else if !process_capture_available() {
            shared.state("Unsupported", "App capture requires Windows build 20348 or later", 0, "");
            delay = FOREVER;
        } else if !apartment.ok() {
            shared.state("Error", "Windows audio initialization failed", 0, "");
            delay = FOREVER;
        } else {
            let mut ambiguous = false;
            match resolve(&selection, &mut ambiguous) {
                None => {
                    let (message, reason) = if ambiguous {
                        ("Several instances are running; select the app again", "app_instance_ambiguous")
                    } else {
                        ("Waiting for application", "app_not_found")
                    };
                    shared.state("Waiting", message, 0, reason);
                }
                Some(pid) => {
                    shared.state("Opening", "Opening app audio", pid, "");
                    let result = capture(shared, &mut producer, pid, &selection, selected_serial);
                    shared.running.store(false, Ordering::Release);
                    producer.request_clear();
                    if shared.stopped.load(Ordering::Relaxed) {
                        break;
                    }
                    match result {
                        Ok(()) => {
                            failures = 0;
                            continue;
                        }
                        Err(code) => {
                            let reason = if code == E_ACCESSDENIED {
                                "access_denied"
                            } else if code == AUDCLNT_E_SERVICE_NOT_RUNNING {
                                "audio_service_unavailable"
                            } else {
                                "stream_failure"
                            };
                            shared.state("Error", &process_failure_message(code), pid, reason);
                            // An unanswered activation is not retried in a
                            // loop; a deliberate retry can reopen it.
                            delay = if code == hresult_from_win32(ERROR_TIMEOUT) {
                                FOREVER
                            } else {
                                let value = delays[failures.min(3)];
                                failures += 1;
                                value
                            };
                        }
                    }
                }
            }
        }
        if wait_any(&events, delay) == Wait::Signalled(0) {
            break;
        }
    }
    let _ = E_NOTIMPL;
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Writes over the stack region a returned activation frame occupied.
    #[inline(never)]
    fn scribble_stack() -> u64 {
        let mut noise = [0xA5u8; 16384];
        for (i, byte) in noise.iter_mut().enumerate() {
            *byte ^= i as u8;
        }
        std::hint::black_box(&mut noise);
        noise.iter().map(|&b| u64::from(b)).sum()
    }

    fn short_lived_child() -> std::process::Child {
        std::process::Command::new("cmd.exe").args(["/d", "/c", "exit"]).spawn().expect("spawn cmd")
    }

    /// Chrome closing: activations are abandoned while their targets exit.
    /// Each activation's parameters must stay valid until Windows completes it.
    #[test]
    fn abandoned_activations_stay_valid() {
        if !process_capture_available() {
            return;
        }
        let apartment = Apartment::enter();
        assert!(apartment.ok());
        let mut states = Vec::new();
        for _ in 0..300 {
            let mut child = short_lived_child();
            match begin_activation(child.id()) {
                Ok((operation, state)) => {
                    drop(operation); // the worker stops waiting immediately
                    states.push(Arc::downgrade(&state));
                }
                Err(_) => {}
            }
            std::hint::black_box(scribble_stack());
            let _ = child.wait();
        }
        // Every activation completes and then releases its state.
        let deadline = Instant::now() + Duration::from_secs(20);
        while states.iter().any(|w| w.strong_count() > 0) && Instant::now() < deadline {
            std::thread::sleep(Duration::from_millis(20));
        }
        assert!(states.iter().all(|w| w.strong_count() == 0), "Windows released every abandoned activation");
        // The process heap is intact (the pre-fix drop freed Rust memory with
        // CoTaskMemFree and failed here or crashed with 0xC0000374).
        // SAFETY: validating the process heap.
        let heap = unsafe { windows::Win32::System::Memory::GetProcessHeap() }.unwrap();
        assert!(unsafe { windows::Win32::System::Memory::HeapValidate(heap, windows::Win32::System::Memory::HEAP_FLAGS(0), None) }.as_bool());
    }
}
