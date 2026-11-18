//! The audio engine (`Engine` in `engine.cpp`).
//!
//! Threads:
//! * control threads (pipe server) call [`Engine::configure`],
//!   [`Engine::snapshot`], [`Engine::retry`] and [`Engine::recover`] under the
//!   configuration mutex;
//! * the mixer thread processes one 5 ms quantum per timer tick with Pro
//!   Audio MMCSS priority;
//! * the maintenance thread refreshes devices and applications once a second.
//!
//! Runtime ownership is entirely on control/maintenance threads. The mixer
//! only reads published runtimes through raw pointers and acknowledges a
//! generation after its final use of older runtimes (including the 10 ms
//! routing crossfade); only then are they retired and dropped. Device
//! workers therefore never stop or join on the mixer thread.

use std::cell::UnsafeCell;
use std::collections::{HashMap, HashSet};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicPtr, AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};
use std::thread::JoinHandle;

use serde_json::Value;
use vinemix_dsp::{live_spectrum, GraphProcessor, SpectrumConsumer, SpectrumProducer};
use vinemix_model::config::{Configuration, DeviceBinding, GraphNode};
use vinemix_model::identity;
use vinemix_model::json::ObjectBuilder;
use vinemix_model::limits::MAXIMUM_GRAPH_NODES;
use vinemix_model::plan::{compile_graph, graph_signal_channels, same_graph_topology, same_indexing, GraphPlan};
use vinemix_model::status::{ApplicationInfo, Endpoint, EngineStatus, NodeStatus, PathState};
use vinemix_model::validate::validate_and_compile;
use vinemix_model::GraphKind;
use vinemix_rt::{frame_queue, CaptureBufferReader, Consumer, Frame, MixClock, Producer, QUANTUM_FRAMES, SAMPLE_RATE};
use vinemix_wasapi::{enumerate_audio_applications, process_capture_available, AudioWorker, DeviceCatalog, ProcessCaptureWorker, QueueEnd};

use crate::store::{ConfigurationStore, StoreError};

/// Bound on pending Echo histories (each ~6 MB) awaiting adoption.
const MAXIMUM_PENDING_ECHO_HISTORY_BYTES: usize = 256 * 1024 * 1024;
const MAXIMUM_RUNTIMES: usize = 128;
const QUEUE_FRAMES: usize = 9600;

/// State only the mixer thread touches after publication.
struct AudioCell<T>(UnsafeCell<T>);

// SAFETY: every `AudioCell` is mutated only by the single mixer thread (or
// before publication on the preparing thread, which hands it over through the
// release/acquire publication of the runtime pointer).
unsafe impl<T: Send> Sync for AudioCell<T> {}

impl<T> AudioCell<T> {
    fn new(value: T) -> Self {
        Self(UnsafeCell::new(value))
    }

    /// # Safety
    /// Only the mixer thread may call this after publication, and it must not
    /// hold two references to the same cell at once.
    #[allow(clippy::mut_from_ref)]
    unsafe fn get(&self) -> &mut T {
        // SAFETY: guaranteed by the caller.
        unsafe { &mut *self.0.get() }
    }

    /// Shared access for concurrent-safe readers (the processor's outputs
    /// are only read by the mixer itself).
    fn as_ptr(&self) -> *mut T {
        self.0.get()
    }
}

/// Errors returned to control clients.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum EngineError {
    /// `revision_conflict`
    Conflict,
    /// `settings_write_failed`
    SettingsWrite(String),
    Message(String),
}

impl std::fmt::Display for EngineError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Conflict => f.write_str("Configuration revision conflict. Refresh and retry your change."),
            Self::SettingsWrite(message) | Self::Message(message) => f.write_str(message),
        }
    }
}

impl std::error::Error for EngineError {}

impl From<StoreError> for EngineError {
    fn from(error: StoreError) -> Self {
        if error.write { Self::SettingsWrite(error.message) } else { Self::Message(error.message) }
    }
}

impl From<vinemix_model::ModelError> for EngineError {
    fn from(error: vinemix_model::ModelError) -> Self {
        Self::Message(error.message)
    }
}

fn message(text: &str) -> EngineError {
    EngineError::Message(text.into())
}

/// Where a logical output renders.
#[derive(Clone, Debug, PartialEq)]
struct OutputTarget {
    endpoint_id: String,
    transport: String,
    unavailable: PathState,
}

fn resolve_output(node: &GraphNode, devices: &[Endpoint]) -> OutputTarget {
    let assigned = node.device.assigned();
    let mut result = OutputTarget {
        endpoint_id: String::new(),
        transport: if node.kind == "virtualOutput" { "vacLite".into() } else { "listening".into() },
        unavailable: PathState::with(
            if assigned { "Waiting" } else { "Unassigned" },
            if assigned { "Waiting for device" } else { "" },
            if assigned { "device_unavailable" } else { "" },
        ),
    };
    // The same identity rule as input workers, validation and the chooser.
    let exact = identity::exact(devices, &node.device, "render");
    let matched = match identity::resolve(devices, &node.device, "render") {
        identity::Resolution::Active(endpoint, _) => Some(endpoint),
        identity::Resolution::Ambiguous => {
            // An ambiguous fallback stays unresolved.
            result.unavailable.message = "Several devices match the saved output; select the device again".into();
            return result;
        }
        identity::Resolution::Unresolved => None,
    };
    let Some(matched) = matched else {
        if let Some(exact) = exact {
            result.unavailable.endpoint_id.clone_from(&exact.id);
            result.unavailable.rate = exact.rate;
            result.unavailable.channels = exact.channels;
            if exact.state == "Disabled" {
                result.unavailable.reason = "device_disabled".into();
                result.unavailable.message = "Device disabled in Windows Sound settings".into();
            }
        }
        return result;
    };
    if (node.kind == "output" && !matched.is_listening()) || (node.kind == "virtualOutput" && matched.transport != "vacLite") {
        result.unavailable.message = if node.kind == "output" {
            "Choose a listening device here; use Virtual output for VAC".into()
        } else {
            "Choose the VAC Lite send endpoint for a virtual microphone output".into()
        };
        result.unavailable.state = "Error".into();
        result.unavailable.reason.clear();
        return result;
    }
    result.endpoint_id.clone_from(&matched.id);
    result
}

/// A device or application stream shared by every runtime that uses it.
struct Port {
    identity: GraphNode,
    output: Option<OutputTarget>,
    device: Option<AudioWorker>,
    app: Option<ProcessCaptureWorker>,
    /// Sources: the mixer's consumer and startup reader.
    consumer: Option<AudioCell<Consumer>>,
    reader: AudioCell<CaptureBufferReader>,
    /// Sinks: the mixer's producer.
    producer: Option<AudioCell<Producer>>,
    activated: Mutex<bool>,
}

impl Port {
    fn new(node: &GraphNode, kind: GraphKind, catalog: &Arc<DeviceCatalog>, output: Option<OutputTarget>) -> Result<Self, EngineError> {
        let (producer, consumer, monitor) = frame_queue(QUEUE_FRAMES);
        let failed = |_| message("Cannot start an audio device worker.");
        let (device, app, consumer, producer) = if kind == GraphKind::AppInput {
            let worker = ProcessCaptureWorker::new(producer, monitor.clone()).map_err(failed)?;
            (None, Some(worker), Some(AudioCell::new(consumer)), None)
        } else if kind.is_source() {
            let transport = if kind == GraphKind::VirtualInput { "vbCable" } else { "" };
            let worker = AudioWorker::new(
                kind == GraphKind::Microphone,
                QueueEnd::Capture(producer),
                monitor.clone(),
                catalog.clone(),
                transport,
            )
            .map_err(failed)?;
            (Some(worker), None, Some(AudioCell::new(consumer)), None)
        } else {
            let transport = if kind == GraphKind::VirtualOutput { "vacLite" } else { "listening" };
            let worker =
                AudioWorker::new(false, QueueEnd::Render(consumer), monitor.clone(), catalog.clone(), transport).map_err(failed)?;
            (Some(worker), None, None, Some(AudioCell::new(producer)))
        };
        Ok(Self {
            identity: node.clone(),
            output,
            device,
            app,
            consumer,
            reader: AudioCell::new(CaptureBufferReader::default()),
            producer,
            activated: Mutex::new(false),
        })
    }

    fn matches(&self, node: &GraphNode, target: Option<&OutputTarget>) -> bool {
        if self.output.is_some() || target.is_some() {
            let (Some(output), Some(target)) = (&self.output, target) else { return false };
            if output.transport != target.transport {
                return false;
            }
            if !output.endpoint_id.is_empty() || !target.endpoint_id.is_empty() {
                return output.endpoint_id == target.endpoint_id;
            }
            // Unresolved bindings cannot open a device until every logical
            // destination is reconciled against the same catalog.
            return self.identity.kind == node.kind
                && self.identity.device.id == node.device.id
                && self.identity.device.stable_id == node.device.stable_id
                && output.unavailable.message == target.unavailable.message
                && output.unavailable.endpoint_id == target.unavailable.endpoint_id
                && output.unavailable.channels == target.unavailable.channels
                && output.unavailable.rate == target.unavailable.rate;
        }
        self.identity.kind == node.kind
            && self.identity.device.same_device(&node.device)
            && self.identity.app.same_process(&node.app)
    }

    fn activate(&self) {
        let mut activated = self.activated.lock().unwrap_or_else(|e| e.into_inner());
        if *activated {
            return;
        }
        if let Some(device) = &self.device {
            // An output worker owns exactly one resolved endpoint; stable-ID
            // fallback is coordinated centrally, never raced by workers.
            let selection = match &self.output {
                Some(output) => DeviceBinding {
                    id: output.endpoint_id.clone(),
                    stable_id: String::new(),
                    name: self.identity.device.name.clone(),
                    channel: -1,
                },
                None => self.identity.device.clone(),
            };
            device.configure(selection);
        }
        if let Some(app) = &self.app {
            app.configure(self.identity.app.clone());
        }
        *activated = true;
    }

    fn ready(&self) -> bool {
        match (&self.device, &self.app) {
            (Some(device), _) => device.ready(),
            (_, Some(app)) => app.ready(),
            _ => false,
        }
    }

    fn status(&self) -> PathState {
        if let Some(output) = &self.output {
            if output.endpoint_id.is_empty() {
                return output.unavailable.clone();
            }
        }
        match (&self.device, &self.app) {
            (Some(device), _) => device.status(),
            (_, Some(app)) => app.status(),
            _ => PathState::default(),
        }
    }

    fn retry(&self) {
        if let Some(device) = &self.device {
            device.retry();
        } else if let Some(app) = &self.app {
            app.retry();
        }
    }
}

/// Lock-free per-node meters, held across runtimes by node id.
#[derive(Debug)]
struct Meter {
    peak_left: AtomicU32,
    peak_right: AtomicU32,
    gain_reduction_db: AtomicU32,
    detector_db: AtomicU32,
    dynamics_frame: AtomicU64,
    clipped: AtomicBool,
}

impl Default for Meter {
    fn default() -> Self {
        Self {
            peak_left: AtomicU32::new(0f32.to_bits()),
            peak_right: AtomicU32::new(0f32.to_bits()),
            gain_reduction_db: AtomicU32::new(0f32.to_bits()),
            detector_db: AtomicU32::new((-96f32).to_bits()),
            dynamics_frame: AtomicU64::new(0),
            clipped: AtomicBool::new(false),
        }
    }
}

fn raise_peak(target: &AtomicU32, value: f32) {
    let mut previous = target.load(Ordering::Relaxed);
    while f32::from_bits(previous) < value {
        match target.compare_exchange_weak(previous, value.to_bits(), Ordering::Relaxed, Ordering::Relaxed) {
            Ok(_) => break,
            Err(actual) => previous = actual,
        }
    }
}

fn exchange(target: &AtomicU32, value: f32) -> f32 {
    f32::from_bits(target.swap(value.to_bits(), Ordering::Relaxed))
}

/// An EQ node's analyser: the mixer feeds the producer, snapshots read.
struct Spectrum {
    producer: AudioCell<SpectrumProducer>,
    consumer: Mutex<SpectrumConsumer>,
}

impl Spectrum {
    fn new(revision: u64) -> Self {
        let (producer, consumer) = live_spectrum(revision);
        Self { producer: AudioCell::new(producer), consumer: Mutex::new(consumer) }
    }
}

/// One published graph with its streams, meters and analysers.
struct Runtime {
    generation: u64,
    plan: GraphPlan,
    /// Shared by consecutive runtimes that differ only in parameters: the
    /// mixer then retargets it in place instead of adopting a new one.
    processor: Arc<AudioCell<GraphProcessor>>,
    ports: Vec<Option<Arc<Port>>>,
    meters: Vec<Arc<Meter>>,
    spectra: Vec<Option<Arc<Spectrum>>>,
}

struct ConfigState {
    store: ConfigurationStore,
    configuration: Configuration,
    /// Advances whenever `configuration` is replaced (snapshot gating).
    configuration_generation: u64,
    // Boxed on purpose: the mixer holds raw pointers to published runtimes,
    // so a runtime must keep its address when this list grows or shrinks.
    #[allow(clippy::vec_box)]
    runtimes: Vec<Box<Runtime>>,
    next_generation: u64,
    warning: String,
    /// `configuration` holds changes applied with `persist: false` that the
    /// settings file does not have yet.
    unsaved: bool,
}

/// The device and application lists carried by snapshots.
#[derive(Debug, Default)]
struct Inventory {
    endpoints: Vec<Endpoint>,
    applications: Vec<ApplicationInfo>,
    /// Advances whenever either list changes (snapshot gating).
    generation: u64,
}

/// What a control client says it already holds, as the opaque tokens of an
/// earlier snapshot. A part whose token is still current is left out of the
/// reply: polling at 30 Hz then carries telemetry, not an unchanged
/// configuration and device list. Absent (older clients), everything is sent.
#[derive(Clone, Copy, Debug, Default)]
pub struct Known<'a> {
    pub configuration: Option<&'a str>,
    pub inventory: Option<&'a str>,
}

/// What a control client is currently showing. Work that only feeds a
/// display is done on demand: an EQ's spectrum is analysed only while some
/// client lists that node, and running applications are enumerated only
/// while a client wants the list. A client that says nothing (older
/// clients) is treated as wanting everything, as before.
#[derive(Clone, Copy, Debug, Default)]
pub struct Interest<'a> {
    /// EQ node ids whose spectrum is on screen; `None` means all.
    pub spectrum: Option<&'a [&'a str]>,
    /// Whether the application list is wanted; `None` means yes.
    pub applications: Option<bool>,
}

/// How long one request's interest in the application list lasts. The
/// desktop polls at least once a second while it wants the list.
const APPLICATION_INTEREST: std::time::Duration = std::time::Duration::from_secs(3);

struct Inner {
    state: Mutex<ConfigState>,
    published: AtomicPtr<Runtime>,
    active_generation: AtomicU64,
    stopping: AtomicBool,
    resume_requested: AtomicBool,
    audio_frames: AtomicU64,
    processing_load: crate::processing_load::ProcessingLoadPublication,
    spectrum_revision: AtomicU64,
    stop_event: vinemix_wasapi::win::Event,
    catalog: Arc<DeviceCatalog>,
    inventory: Mutex<Inventory>,
    /// Distinguishes this engine's snapshot tokens from any other engine's.
    token_seed: u64,
    /// When the engine was created; the clock for `applications_wanted_until`.
    created: std::time::Instant,
    /// Milliseconds after `created` until which a client wants the
    /// application list (0: nobody has asked).
    applications_wanted_until: AtomicU64,
    /// Wakes maintenance when interest in the application list resumes.
    maintenance_wake: vinemix_wasapi::win::Event,
    /// The settings file's physical location, refreshed by maintenance so
    /// that snapshots never open the file while holding the state lock.
    physical_settings: Mutex<Option<String>>,
    log: crate::log::Log,
    started: AtomicBool,
}

/// The engine. Dropping it stops audio and joins every thread.
pub struct Engine {
    inner: Arc<Inner>,
    threads: Mutex<Vec<JoinHandle<()>>>,
}

impl std::fmt::Debug for Engine {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Engine").finish_non_exhaustive()
    }
}

fn lock(inner: &Inner) -> MutexGuard<'_, ConfigState> {
    inner.state.lock().unwrap_or_else(|e| e.into_inner())
}

fn qpc() -> i64 {
    let mut value = 0i64;
    // SAFETY: valid output pointer.
    unsafe {
        let _ = windows::Win32::System::Performance::QueryPerformanceCounter(&mut value);
    }
    value
}

fn qpc_frequency() -> i64 {
    let mut value = 0i64;
    // SAFETY: valid output pointer.
    unsafe {
        let _ = windows::Win32::System::Performance::QueryPerformanceFrequency(&mut value);
    }
    value.max(1)
}

impl Engine {
    /// Loads settings and starts the device catalog. Audio starts with [`Engine::start`].
    pub fn new(settings: Option<PathBuf>) -> Result<Self, EngineError> {
        let mut store = ConfigurationStore::new(settings)?;
        let configuration = store.load();
        let warning = store.warning().to_owned();
        let log_path = store.path().parent().map(|p| p.join("engine.log")).unwrap_or_else(|| PathBuf::from("engine.log"));
        // Panics abort (no unwinding across the realtime threads); record the
        // message and source location so the restart has a reason in the log.
        let panic_log = crate::log::Log::new(log_path.clone());
        std::panic::set_hook(Box::new(move |info| panic_log.write(&format!("Fatal error: {info}"))));
        let catalog = Arc::new(DeviceCatalog::new().map_err(|_| message("Could not create device notification events"))?);
        let endpoints = catalog.enumerate();
        let physical_settings = physical_settings_path(&store);
        let inner = Arc::new(Inner {
            state: Mutex::new(ConfigState { store, configuration, configuration_generation: 1, runtimes: Vec::new(), next_generation: 0, warning, unsaved: false }),
            published: AtomicPtr::new(std::ptr::null_mut()),
            active_generation: AtomicU64::new(0),
            stopping: AtomicBool::new(false),
            resume_requested: AtomicBool::new(false),
            audio_frames: AtomicU64::new(0),
            processing_load: crate::processing_load::ProcessingLoadPublication::default(),
            // Seed visual continuity identities from the monotonic counter so a
            // reconnected UI never mistakes a new engine's spectrum for an old one.
            spectrum_revision: AtomicU64::new(qpc() as u64),
            stop_event: vinemix_wasapi::win::Event::new(true).map_err(|_| message("Cannot create the audio engine stop event."))?,
            catalog,
            inventory: Mutex::new(Inventory { endpoints, applications: Vec::new(), generation: 1 }),
            token_seed: (qpc() as u64) ^ (u64::from(std::process::id()) << 32),
            created: std::time::Instant::now(),
            applications_wanted_until: AtomicU64::new(0),
            maintenance_wake: vinemix_wasapi::win::Event::new(false).map_err(|_| message("Cannot create the maintenance event."))?,
            physical_settings: Mutex::new(physical_settings),
            log: crate::log::Log::new(log_path),
            started: AtomicBool::new(false),
        });
        Ok(Self { inner, threads: Mutex::new(Vec::new()) })
    }

    pub fn start(&self) -> Result<(), EngineError> {
        let inner = &self.inner;
        // Hold the lifecycle mutex through publication, thread creation and
        // any rollback. A concurrent stop must join this mixer before reclaiming.
        let mut threads = self.threads.lock().unwrap_or_else(|e| e.into_inner());
        if inner.started.swap(true, Ordering::AcqRel) {
            return Ok(());
        }
        // A new session, including a same-object restart.
        inner.processing_load.publish(&vinemix_model::status::ProcessingLoadStatus::default());
        inner.stopping.store(false, Ordering::Release);
        inner.stop_event.reset();
        let result = (|| -> Result<(), EngineError> {
            {
                let mut state = lock(inner);
                if state.store.writable() {
                    let plan = compile_graph(&state.configuration.graph)?;
                    let configuration = state.configuration.clone();
                    let runtime = prepare(inner, &mut state, &configuration, plan, None)?;
                    publish(inner, &mut state, runtime);
                }
            }
            let mixer = inner.clone();
            let maintenance = inner.clone();
            threads.push(
                std::thread::Builder::new()
                    .name("vinemix-mixer".into())
                    .spawn(move || mix_loop(&mixer))
                    .map_err(|_| message("Cannot start the mixer thread."))?,
            );
            threads.push(
                std::thread::Builder::new()
                    .name("vinemix-maintenance".into())
                    .spawn(move || maintenance_loop(&maintenance))
                    .map_err(|_| message("Cannot start the maintenance thread."))?,
            );
            inner.log.write("Canvas engine started. Device assignments are explicit; Windows defaults are unchanged.");
            Ok(())
        })();
        if result.is_err() {
            // A half-started engine must not stay alive.
            self.stop_locked(&mut threads);
        }
        result
    }

    pub fn request_stop(&self) {
        request_stop(&self.inner);
    }

    pub fn stopping(&self) -> bool {
        self.inner.stopping.load(Ordering::Acquire)
    }

    /// Stops audio, joins threads and stops every device worker.
    pub fn stop(&self) {
        let mut threads = self.threads.lock().unwrap_or_else(|e| e.into_inner());
        self.stop_locked(&mut threads);
    }

    fn stop_locked(&self, threads: &mut Vec<JoinHandle<()>>) {
        request_stop(&self.inner);
        for thread in threads.drain(..) {
            let _ = thread.join();
        }
        self.flush_logged();
        let mut state = lock(&self.inner);
        self.inner.published.store(std::ptr::null_mut(), Ordering::Release);
        // Shared ports stop and join here, never on the mixer thread.
        state.runtimes.clear();
        self.inner.started.store(false, Ordering::Release);
    }

    pub fn endpoints(&self) -> Vec<Endpoint> {
        self.inner.inventory.lock().unwrap_or_else(|e| e.into_inner()).endpoints.clone()
    }

    /// Applies a full configuration if `expected_revision` is current.
    ///
    /// With `persist` false the configuration takes effect but the settings
    /// file is left alone: a control being dragged sends many of these and
    /// one persisting configure when the drag ends. Whatever is still
    /// unwritten is written by [`flush`](Self::flush), which runs when the
    /// client disconnects and when the engine stops.
    pub fn configure(&self, mut config: Configuration, expected_revision: u64, persist: bool) -> Result<(), EngineError> {
        let inner = &*self.inner;
        let mut state = lock(inner);
        verify_workspace_location(&state.store)?;
        if !state.store.writable() {
            return Err(message("Choose workspace recovery before changing the configuration."));
        }
        if expected_revision != state.configuration.revision {
            return Err(EngineError::Conflict);
        }
        if state.configuration.revision == u64::MAX {
            return Err(message("The configuration revision cannot be advanced."));
        }
        retire(inner, &mut state);
        if state.runtimes.len() >= MAXIMUM_RUNTIMES {
            return Err(message("The audio engine is not adopting changes. Wait briefly or restart the engine."));
        }
        config.revision = state.configuration.revision + 1;
        // One validation and one compilation per accepted edit: the plan
        // built here is the one published, and the store accepts the proof.
        let (validated, plan) = validate_and_compile(&config)?;
        validate_endpoints(&self.endpoints(), &config)?;
        let current = inner.published.load(Ordering::Acquire);
        // SAFETY: published runtimes are owned by `state.runtimes`, which we hold.
        let current = unsafe { current.as_ref() };
        let same_plan = current.is_some_and(|c| c.plan == plan);
        // Every stream the published runtime uses is still the right one.
        let ports_unchanged = current.is_some_and(|c| {
            (same_plan || same_indexing(&c.plan, &plan)) && {
                let devices = inner.catalog.enumerate();
                plan.nodes.iter().enumerate().all(|(i, node)| {
                    let target = node.kind.is_sink().then(|| resolve_output(&config.graph.nodes[i], &devices));
                    c.ports[i].as_ref().is_none_or(|port| port.matches(&config.graph.nodes[i], target.as_ref()))
                })
            }
        });
        let audio_unchanged = same_plan && ports_unchanged;
        // A parameter edit (same nodes, cables and streams) keeps the
        // processor: no effect storage is allocated and no state is copied.
        let share = if ports_unchanged { current.map(|c| c.processor.clone()) } else { None };
        let runtime = if audio_unchanged { None } else { Some(prepare(inner, &mut state, &config, plan, share)?) };
        if persist {
            state.store.save_validated(validated)?;
            state.unsaved = false;
        } else {
            state.unsaved = true;
        }
        state.configuration = config;
        state.configuration_generation += 1;
        // Canvas positions, names and reset anchors persist, but they never
        // rebuild denoiser plans or reverb delays at the next audio boundary.
        if let Some(runtime) = runtime {
            publish(inner, &mut state, runtime);
        }
        Ok(())
    }

    /// Writes a configuration that was applied with `persist: false`.
    pub fn flush(&self) -> Result<(), EngineError> {
        let mut state = lock(&self.inner);
        if !state.unsaved {
            return Ok(());
        }
        if state.store.writable() {
            verify_workspace_location(&state.store)?;
            let configuration = state.configuration.clone();
            state.store.save(&configuration)?;
        }
        state.unsaved = false;
        Ok(())
    }

    /// [`flush`](Self::flush) where nobody is left to tell: the failure is
    /// recorded in the engine log.
    pub fn flush_logged(&self) {
        if let Err(error) = self.flush() {
            self.inner.log.write(&format!("The latest adjustment could not be saved: {error}"));
        }
    }

    /// Re-enumerates devices and reopens every stream.
    pub fn retry(&self) {
        retry(&self.inner);
    }

    pub fn recover(&self, action: &str) -> Result<(), EngineError> {
        let inner = &*self.inner;
        let mut state = lock(inner);
        verify_workspace_location(&state.store)?;
        let recovered = state.store.recover(action)?;
        state.configuration = recovered;
        state.unsaved = false;
        state.configuration_generation += 1;
        *inner.physical_settings.lock().unwrap_or_else(|e| e.into_inner()) = physical_settings_path(&state.store);
        state.warning = state.store.warning().to_owned();
        if !state.store.writable() || !inner.started.load(Ordering::Acquire) {
            return Ok(());
        }
        let configuration = state.configuration.clone();
        let result = compile_graph(&configuration.graph)
            .map_err(EngineError::from)
            .and_then(|plan| prepare(inner, &mut state, &configuration, plan, None));
        match result {
            Ok(runtime) => {
                publish(inner, &mut state, runtime);
                Ok(())
            }
            Err(error) => {
                state.store.pause_for_recovery(
                    "The workspace was recovered, but audio could not start. Audio and saving remain paused. Retry to reconnect safely.",
                );
                state.warning = state.store.warning().to_owned();
                Err(error)
            }
        }
    }

    /// The full engine snapshot (protocol v1 `snapshot` object).
    pub fn snapshot(&self) -> Value {
        snapshot(&self.inner, Known::default(), Interest::default())
    }

    /// The snapshot without the parts `known` shows the client already has,
    /// doing display-only work for what `interest` says is being shown.
    pub fn snapshot_for(&self, known: Known<'_>, interest: Interest<'_>) -> Value {
        snapshot(&self.inner, known, interest)
    }

    /// Whether maintenance is currently enumerating applications (a client
    /// asked for the list within the interest window).
    pub fn applications_wanted(&self) -> bool {
        applications_wanted(&self.inner)
    }

    /// The configuration revision and load state, cheaply.
    pub fn revision(&self) -> u64 {
        lock(&self.inner).configuration.revision
    }
}

impl Drop for Engine {
    fn drop(&mut self) {
        self.stop();
    }
}

fn request_stop(inner: &Inner) {
    inner.stopping.store(true, Ordering::Release);
    inner.stop_event.set();
}

fn retry(inner: &Inner) {
    inner.catalog.refresh();
    let state = lock(inner);
    let mut visited: HashSet<*const Port> = HashSet::new();
    for runtime in &state.runtimes {
        for port in runtime.ports.iter().flatten() {
            if visited.insert(Arc::as_ptr(port)) {
                port.retry();
            }
        }
    }
}

/// Endpoint checks shared with the desktop's device pickers.
pub fn validate_endpoints(devices: &[Endpoint], config: &Configuration) -> Result<(), EngineError> {
    for node in &config.graph.nodes {
        if !node.device.assigned() {
            continue;
        }
        let capture = node.kind == "microphone" || node.kind == "virtualInput";
        let render = node.kind == "output" || node.kind == "virtualOutput";
        if !capture && !render {
            continue;
        }
        let direction = if capture { "capture" } else { "render" };
        // Worker resolution: ambiguous identities stay saved, unresolved.
        let exact = identity::exact(devices, &node.device, direction);
        let matched = match identity::resolve(devices, &node.device, direction) {
            identity::Resolution::Active(endpoint, _) => Some(endpoint),
            identity::Resolution::Ambiguous => continue,
            identity::Resolution::Unresolved => None,
        };
        let matched = matched.or(exact);
        let Some(matched) = matched else {
            if !node.device.id.is_empty() && devices.iter().any(|e| e.id == node.device.id && e.direction != direction) {
                return Err(message("The device has the wrong audio direction for this node."));
            }
            continue;
        };
        if node.kind == "output" && !matched.is_listening() {
            return Err(message("Choose a listening device here; use Virtual output for VAC."));
        }
        if node.kind == "virtualInput" && matched.transport != "vbCable" {
            return Err(message("Choose the base VB-CABLE receive endpoint for a virtual input."));
        }
        if node.kind == "virtualOutput" && matched.transport != "vacLite" {
            return Err(message("Choose the VAC Lite send endpoint for a virtual microphone output."));
        }
        if node.kind == "microphone" && matched.channels != 0 && node.device.channel >= matched.channels as i32 {
            return Err(message("The selected microphone channel is not available."));
        }
    }
    Ok(())
}

/// Drops runtimes the mixer has acknowledged it no longer uses.
fn retire(inner: &Inner, state: &mut ConfigState) {
    let active = inner.active_generation.load(Ordering::Acquire);
    state.runtimes.retain(|runtime| runtime.generation >= active);
}

fn publish(inner: &Inner, state: &mut ConfigState, mut runtime: Box<Runtime>) {
    let pointer: *mut Runtime = &mut *runtime;
    state.runtimes.push(runtime);
    inner.published.store(pointer, Ordering::Release);
}

/// Builds a processor for `plan`, first bounding the long Echo buffers that
/// are waiting for adoption (the settled active runtime's are not pending,
/// and runtimes sharing one processor share one history).
fn new_processor(inner: &Inner, state: &ConfigState, plan: &GraphPlan) -> Result<Arc<AudioCell<GraphProcessor>>, EngineError> {
    let active = inner.active_generation.load(Ordering::Acquire);
    let settled: Vec<*const AudioCell<GraphProcessor>> =
        state.runtimes.iter().filter(|r| r.generation <= active).map(|r| Arc::as_ptr(&r.processor)).collect();
    let mut counted = settled.clone();
    let mut pending = GraphProcessor::echo_history_bytes(plan);
    for retained in &state.runtimes {
        let pointer = Arc::as_ptr(&retained.processor);
        if retained.generation > active && !counted.contains(&pointer) {
            counted.push(pointer);
            pending += GraphProcessor::echo_history_bytes(&retained.plan);
        }
    }
    if pending > MAXIMUM_PENDING_ECHO_HISTORY_BYTES {
        return Err(message("The audio engine is still applying Echo changes. Wait briefly and try again."));
    }
    let mut processor = GraphProcessor::new();
    processor.prepare(plan);
    Ok(Arc::new(AudioCell::new(processor)))
}

/// Prepares a runtime for `plan`. `share` is the published runtime's
/// processor when the caller found the edit to be parameters-only; it is used
/// only if every stream of the new runtime turns out to be the published
/// runtime's own stream object.
fn prepare(
    inner: &Inner,
    state: &mut ConfigState,
    config: &Configuration,
    plan: GraphPlan,
    share: Option<Arc<AudioCell<GraphProcessor>>>,
) -> Result<Box<Runtime>, EngineError> {
    retire(inner, state);
    // Bound pending long Echo buffers before allocating, opening workers or
    // saving. A shared processor allocates nothing.
    let processor = match share.clone() {
        Some(processor) => processor,
        None => new_processor(inner, state, &plan)?,
    };
    state.next_generation += 1;
    let count = plan.nodes.len();
    let mut runtime = Box::new(Runtime {
        generation: state.next_generation,
        plan,
        processor,
        ports: (0..count).map(|_| None).collect(),
        meters: Vec::with_capacity(count),
        spectra: (0..count).map(|_| None).collect(),
    });
    let previous = inner.published.load(Ordering::Acquire);
    // SAFETY: the published runtime is owned by `state.runtimes`, held here.
    let previous = unsafe { previous.as_ref() };
    let devices = inner.catalog.enumerate();
    for i in 0..count {
        let node = &config.graph.nodes[i];
        let kind = runtime.plan.nodes[i].kind;
        let mut meter = None;
        if let Some(previous) = previous {
            if let Some(j) = previous.plan.nodes.iter().position(|n| n.id == node.id) {
                meter = Some(previous.meters[j].clone());
                if kind == GraphKind::Eq && previous.plan.nodes[j].kind == GraphKind::Eq {
                    runtime.spectra[i].clone_from(&previous.spectra[j]);
                }
            }
        }
        runtime.meters.push(meter.unwrap_or_default());
        let target = kind.is_sink().then(|| resolve_output(node, &devices));
        if kind == GraphKind::Eq && runtime.spectra[i].is_none() {
            let revision = inner.spectrum_revision.fetch_add(1, Ordering::Relaxed) + 1;
            runtime.spectra[i] = Some(Arc::new(Spectrum::new(revision)));
        }
        if kind.is_source() || kind.is_sink() {
            // Capture is read once and fanned out; outputs sharing an
            // endpoint are summed into one queue write.
            let shared = runtime.ports[..i].iter().flatten().find(|port| port.matches(node, target.as_ref())).cloned();
            // A rapid A -> B -> A assignment can revisit a still-live older
            // plan: reuse its worker instead of opening the hardware twice.
            let retained = shared.or_else(|| {
                state
                    .runtimes
                    .iter()
                    .rev()
                    .find_map(|r| r.ports.iter().flatten().find(|port| port.matches(node, target.as_ref())).cloned())
            });
            let port = match retained {
                Some(port) => port,
                None => Arc::new(Port::new(node, kind, &inner.catalog, target)?),
            };
            port.activate();
            runtime.ports[i] = Some(port);
        }
    }
    if share.is_some() {
        // The mixer retargets a shared processor without a crossfade, which
        // is only right when no stream changed. Normally guaranteed by the
        // caller's check; verified here against the actual stream objects.
        let unchanged = previous.is_some_and(|previous| {
            previous.ports.len() == runtime.ports.len() && runtime.ports.iter().zip(&previous.ports).all(|(a, b)| same_port(a, b))
        });
        if !unchanged {
            runtime.processor = new_processor(inner, state, &runtime.plan)?;
        }
    }
    Ok(runtime)
}

/// Device return or alias convergence: a runtime-only reconciliation that
/// keeps saved choices, revision, controls and history intact.
fn refresh_output_ports(inner: &Inner, state: &mut ConfigState) {
    let current = inner.published.load(Ordering::Acquire);
    // SAFETY: owned by `state.runtimes`, held here.
    let Some(current) = (unsafe { current.as_ref() }) else { return };
    if !state.store.writable() || state.runtimes.len() >= MAXIMUM_RUNTIMES {
        return;
    }
    let devices = inner.catalog.enumerate();
    let stale = current.plan.nodes.iter().enumerate().any(|(i, plan_node)| {
        if !plan_node.kind.is_sink() {
            return false;
        }
        let node = &state.configuration.graph.nodes[i];
        let target = resolve_output(node, &devices);
        !current.ports[i].as_ref().is_some_and(|port| port.matches(node, Some(&target)))
    });
    if stale {
        let configuration = state.configuration.clone();
        let result = compile_graph(&configuration.graph)
            .map_err(EngineError::from)
            .and_then(|plan| prepare(inner, state, &configuration, plan, None));
        match result {
            Ok(runtime) => publish(inner, state, runtime),
            Err(error) => inner.log.write(&format!("Output refresh: {error}")),
        }
    }
}

fn verify_workspace_location(store: &ConfigurationStore) -> Result<(), EngineError> {
    store.physical_path().map(|_| ()).map_err(|_| message("Cannot verify the physical settings location. Workspace changes are paused."))
}

fn physical_settings_path(store: &ConfigurationStore) -> Option<String> {
    store.physical_path().ok().map(|p| p.to_string_lossy().into_owned())
}

fn applications_wanted(inner: &Inner) -> bool {
    (inner.created.elapsed().as_millis() as u64) < inner.applications_wanted_until.load(Ordering::Acquire)
}

fn snapshot(inner: &Inner, known: Known<'_>, interest: Interest<'_>) -> Value {
    if interest.applications != Some(false) {
        let resumed = !applications_wanted(inner);
        let until = (inner.created.elapsed() + APPLICATION_INTEREST).as_millis() as u64;
        inner.applications_wanted_until.fetch_max(until, Ordering::AcqRel);
        if resumed {
            // Refresh the list now rather than at the next maintenance tick.
            inner.maintenance_wake.set();
        }
    }
    let state = lock(inner);
    let mut status = EngineStatus { audio_frames: inner.audio_frames.load(Ordering::Relaxed), ..EngineStatus::default() };
    let mut load = inner.processing_load.read();
    if !inner.started.load(Ordering::Acquire) || inner.stopping.load(Ordering::Acquire) {
        load.available = false;
    }
    status.processing_load = Some(load);
    // Analysers to read once the state lock is released (status index, analyser).
    let mut analysers: Vec<(usize, Arc<Spectrum>)> = Vec::new();
    let published = inner.published.load(Ordering::Acquire);
    // SAFETY: owned by `state.runtimes`, held here.
    if let Some(runtime) = unsafe { published.as_ref() } {
        let mut capture_channels = [0i32; MAXIMUM_GRAPH_NODES];
        for (i, node) in runtime.plan.nodes.iter().enumerate() {
            let meter = &runtime.meters[i];
            let mut value = NodeStatus { id: node.id.clone(), ..NodeStatus::default() };
            value.peak_left = exchange(&meter.peak_left, 0.0);
            value.peak_right = exchange(&meter.peak_right, 0.0);
            value.peak = value.peak_left.max(value.peak_right);
            value.clipped = meter.clipped.swap(false, Ordering::Relaxed);
            value.dynamics_frame = meter.dynamics_frame.load(Ordering::Acquire);
            value.gain_reduction_db = exchange(&meter.gain_reduction_db, 0.0);
            value.detector_db = exchange(&meter.detector_db, -96.0);
            let mut audible = !node.bypass && !node.muted;
            if let Some(edge) = node.control_edge {
                let edge = &runtime.plan.edges[edge];
                if !edge.muted && runtime.plan.nodes[edge.from].muted {
                    audible = false;
                }
            }
            if !audible {
                value.gain_reduction_db = 0.0;
                value.detector_db = -96.0;
            }
            // The FFT is on demand: skipped for an EQ whose spectrum no
            // client is showing, and run after the state lock is released.
            // The mixer keeps the analyser's input current, so a returning
            // viewer gets live data at once.
            let shown = interest.spectrum.is_none_or(|ids| ids.contains(&node.id.as_str()));
            if let Some(spectrum) = runtime.spectra[i].as_ref().filter(|_| shown) {
                analysers.push((status.nodes.len(), Arc::clone(spectrum)));
            }
            match &runtime.ports[i] {
                Some(port) => value.path = port.status(),
                None => {
                    value.path.state = "Running".into();
                    value.path.message =
                        if node.kind == GraphKind::Control { "Shared volume control".into() } else { "Processing audio".into() };
                }
            }
            capture_channels[i] = value.path.channels.min(2) as i32;
            match node.id.as_str() {
                "mic1" => {
                    status.source_peaks[0] = value.peak;
                    status.inputs[0] = value.path.clone();
                }
                "mic2" => {
                    status.source_peaks[1] = value.peak;
                    status.inputs[1] = value.path.clone();
                }
                "app-input" => {
                    status.source_peaks[2] = value.peak;
                    status.inputs[2] = value.path.clone();
                }
                "main-output-1" => status.outputs[0] = value.path.clone(),
                "main-output-2" => status.outputs[1] = value.path.clone(),
                "virtual-output" => status.outputs[2] = value.path.clone(),
                "mix-a" => {
                    status.peak_a = value.peak;
                    status.clip_a = value.clipped;
                }
                "mix-b" => {
                    status.peak_b = value.peak;
                    status.clip_b = value.clipped;
                }
                _ => {}
            }
            status.nodes.push(value);
        }
        let channels = graph_signal_channels(&runtime.plan, &capture_channels);
        for (node, channels) in status.nodes.iter_mut().zip(channels) {
            node.channels = channels;
        }
    }
    // Everything that needs the state lock is copied out here; the JSON is
    // built after the lock is released, so configure and maintenance never
    // wait on serialisation.
    let configuration_token = format!("c{:x}-{:x}", inner.token_seed, state.configuration_generation);
    let configuration = (known.configuration != Some(configuration_token.as_str())).then(|| state.configuration.clone());
    let settings_path = state.store.path().to_string_lossy().into_owned();
    let warning = state.warning.clone();
    let settings_state = state.store.load_state().as_str();
    drop(state);
    // The analysers are shared with the runtime by reference count, so they
    // stay valid here even if that runtime has since been retired.
    for (index, spectrum) in analysers {
        let mut consumer = spectrum.consumer.lock().unwrap_or_else(|e| e.into_inner());
        let view = consumer.read();
        let value = &mut status.nodes[index];
        value.spectrum_frame = view.frame;
        value.spectrum_revision = view.revision;
        if view.frame != 0 {
            value.spectrum_db = view.db.to_vec();
        }
    }
    let (inventory_token, lists) = {
        let inventory = inner.inventory.lock().unwrap_or_else(|e| e.into_inner());
        let token = format!("i{:x}-{:x}", inner.token_seed, inventory.generation);
        let lists = (known.inventory != Some(token.as_str())).then(|| (inventory.endpoints.clone(), inventory.applications.clone()));
        (token, lists)
    };
    let physical = inner.physical_settings.lock().unwrap_or_else(|e| e.into_inner()).clone();
    let mut builder = ObjectBuilder::new();
    if let Some(configuration) = &configuration {
        builder = builder.set("configuration", configuration.to_json());
    }
    if let Some((endpoints, applications)) = &lists {
        builder = builder
            .set("endpoints", serde_json::to_value(endpoints).unwrap_or(Value::Null))
            .set("applications", serde_json::to_value(applications).unwrap_or(Value::Null));
    }
    builder
        .set("tokens", serde_json::json!({"configuration": configuration_token, "inventory": inventory_token}))
        .set("enginePid", std::process::id())
        .set("executablePath", vinemix_platform::current_executable())
        .set("engineProtocolVersion", 1)
        .set("engineVersion", vinemix_platform::release::VERSION)
        .set("displayVersion", vinemix_platform::release::DISPLAY_VERSION)
        .set("physicalSettingsPath", physical)
        .set("appCaptureAvailable", process_capture_available())
        .set("status", serde_json::to_value(&status).unwrap_or(Value::Null))
        .set("settingsPath", settings_path.clone())
        .set("logPath", inner.log.path().to_string_lossy().into_owned())
        .set("warning", warning)
        .set("settingsState", settings_state)
        .set("backupPath", format!("{settings_path}.bak"))
        .build()
}

type Block = [Frame; QUANTUM_FRAMES];

/// The mixer thread.
fn mix_loop(inner: &Inner) {
    use windows::Win32::Foundation::{CloseHandle, WAIT_OBJECT_0};
    use windows::Win32::System::Threading::{
        CancelWaitableTimer, CreateWaitableTimerExW, CreateWaitableTimerW, SetWaitableTimer, WaitForMultipleObjects,
        CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, INFINITE, TIMER_ALL_ACCESS,
    };
    let _mmcss = vinemix_wasapi::worker::mmcss_task("Pro Audio");
    // SAFETY: timer handle owned and closed at the end of the loop.
    let timer = unsafe {
        CreateWaitableTimerExW(None, windows::core::PCWSTR::null(), CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS.0)
            .or_else(|_| CreateWaitableTimerW(None, false, windows::core::PCWSTR::null()))
    };
    let Ok(timer) = timer else {
        request_stop(inner);
        return;
    };
    let frequency = qpc_frequency();
    let mut now = qpc();
    let ticks_per_block = frequency * QUANTUM_FRAMES as i64 / i64::from(SAMPLE_RATE);
    let mut clock = MixClock::new(ticks_per_block, now);
    let mut previous = now;
    let mut processing_load = crate::processing_load::ProcessingLoadMonitor::default();
    let mut previous_processing_end = now;
    let waits = [inner.stop_event.handle(), timer];
    let mut active: *mut Runtime = std::ptr::null_mut();
    let mut fading: *mut Runtime = std::ptr::null_mut();
    let mut fade_remaining: u32 = 0;
    let mut input: Box<[Block; MAXIMUM_GRAPH_NODES * 2]> = Box::new([[Frame::SILENT; QUANTUM_FRAMES]; MAXIMUM_GRAPH_NODES * 2]);
    let mut input_ports: [*const Port; MAXIMUM_GRAPH_NODES * 2] = [std::ptr::null(); MAXIMUM_GRAPH_NODES * 2];
    let mut output_ports: [*const Port; MAXIMUM_GRAPH_NODES * 2] = [std::ptr::null(); MAXIMUM_GRAPH_NODES * 2];
    let mut mixed: Block = [Frame::SILENT; QUANTUM_FRAMES];
    while !inner.stopping.load(Ordering::Acquire) {
        now = qpc();
        let quantum_start = now;
        let resumed = now - previous > frequency / 4;
        previous = now;
        let desired = inner.published.load(Ordering::Acquire);
        if fading.is_null() && !desired.is_null() && desired != active {
            if !active.is_null() {
                // SAFETY: `active` and `desired` are distinct live runtimes:
                // neither generation has been acknowledged as retired.
                let (old, new) = unsafe { (&*active, &*desired) };
                if Arc::ptr_eq(&old.processor, &new.processor) {
                    // A parameter edit: same nodes, cables and streams, and
                    // the processor both runtimes share already holds all the
                    // state. Nothing is adopted and nothing fades.
                    // SAFETY: the mixer is the only user of the processor.
                    unsafe { new.processor.get().retarget(&old.plan, &new.plan) };
                } else {
                    let mut changed = !same_graph_topology(&old.plan, &new.plan);
                    // Device/channel/app changes keep ids but the old stream must
                    // still fade out locally.
                    if !changed {
                        'outer: for (i, node) in new.plan.nodes.iter().enumerate() {
                            for (j, other) in old.plan.nodes.iter().enumerate() {
                                if node.id == other.id && !same_port(&new.ports[i], &old.ports[j]) {
                                    changed = true;
                                    break 'outer;
                                }
                            }
                        }
                    }
                    // SAFETY: the mixer is the only user of both processors.
                    unsafe { new.processor.get().adopt(old.processor.get(), &old.plan, &new.plan, changed) };
                    if changed {
                        fading = active;
                        fade_remaining = 480;
                    }
                }
            }
            active = desired;
        }
        if resumed {
            // A long idle or suspend gap re-primes telemetry, keeping totals.
            // Long work itself keeps accumulating overload evidence even when
            // the audio recovery path rebases its clock.
            if processing_load.reset_after_idle(quantum_start, previous_processing_end, frequency) {
                inner.processing_load.publish(&processing_load.read());
            }
            for runtime in [active, fading] {
                // SAFETY: live runtimes, see above.
                let Some(runtime) = (unsafe { runtime.as_ref() }) else { continue };
                if std::ptr::eq(runtime, active) {
                    // Only the active graph publishes spectra; shared
                    // analysers are cleared once, even during a crossfade.
                    for spectrum in runtime.spectra.iter().flatten() {
                        let revision = inner.spectrum_revision.fetch_add(1, Ordering::Relaxed) + 1;
                        // SAFETY: mixer-only producer.
                        unsafe { spectrum.producer.get().reset(revision) };
                    }
                }
                // SAFETY: mixer-only processor.
                unsafe { runtime.processor.get().reset() };
                for meter in &runtime.meters {
                    meter.gain_reduction_db.store(0f32.to_bits(), Ordering::Relaxed);
                    meter.detector_db.store((-96f32).to_bits(), Ordering::Relaxed);
                    meter.dynamics_frame.store(0, Ordering::Release);
                }
                for (i, port) in runtime.ports.iter().enumerate() {
                    let Some(port) = port else { continue };
                    if runtime.plan.nodes[i].kind.is_source() {
                        if let Some(consumer) = &port.consumer {
                            // SAFETY: mixer-only consumer and reader.
                            unsafe {
                                consumer.get().clear();
                                port.reader.get().reset();
                            }
                        }
                    } else if let Some(producer) = &port.producer {
                        // SAFETY: mixer-only producer.
                        unsafe { producer.get().request_clear() };
                    }
                }
            }
            clock.reset(now);
            inner.resume_requested.store(true, Ordering::Release);
        }
        let processing_transition = !fading.is_null();
        let mut captured = 0usize;
        let mut rendered = 0usize;
        let frame_end = inner.audio_frames.load(Ordering::Relaxed) + QUANTUM_FRAMES as u64;
        for runtime_pointer in [active, fading] {
            // SAFETY: live runtime, see above.
            let Some(runtime) = (unsafe { runtime_pointer.as_ref() }) else { continue };
            let mut slots = [usize::MAX; MAXIMUM_GRAPH_NODES];
            for (i, port) in runtime.ports.iter().enumerate() {
                let Some(port) = port else { continue };
                if !runtime.plan.nodes[i].kind.is_source() {
                    continue;
                }
                let pointer = Arc::as_ptr(port);
                let slot = input_ports[..captured].iter().position(|&p| p == pointer).unwrap_or(captured);
                if slot == captured {
                    input_ports[captured] = pointer;
                    captured += 1;
                    let block = &mut input[slot];
                    block.fill(Frame::SILENT);
                    let Some(consumer) = &port.consumer else { continue };
                    // SAFETY: mixer-only consumer and reader.
                    let (consumer, reader) = unsafe { (consumer.get(), port.reader.get()) };
                    if let Some(device) = &port.device {
                        // Acquire readiness before reading its published period.
                        let ready = device.ready();
                        let period = device.period_frames() as usize;
                        let reserve = device.target_queue_frames() as usize;
                        reader.read(consumer, block, ready, period, reserve);
                    } else if port.ready() {
                        // Process loopback keeps its separate delivery policy.
                        if consumer.size() > SAMPLE_RATE as usize / 10 {
                            consumer.clear();
                        }
                        consumer.pop(block);
                    } else {
                        consumer.clear();
                    }
                }
                slots[i] = slot;
            }
            let views: [&[Frame]; MAXIMUM_GRAPH_NODES] =
                std::array::from_fn(|i| if slots[i] != usize::MAX { &input[slots[i]][..] } else { &[][..] });
            // SAFETY: mixer-only processor.
            let processor = unsafe { runtime.processor.get() };
            processor.process(&runtime.plan, &views, true);
            if runtime_pointer == active {
                for (i, node) in runtime.plan.nodes.iter().enumerate() {
                    let meter = processor.meter(i);
                    let target = &runtime.meters[i];
                    raise_peak(&target.peak_left, meter.peak_left);
                    raise_peak(&target.peak_right, meter.peak_right);
                    if meter.clipped {
                        target.clipped.store(true, Ordering::Relaxed);
                    }
                    if node.kind.has_dynamics_meter() {
                        // Hold brief consonant peaks between UI polls, then
                        // consume them once; never a historical maximum.
                        raise_peak(&target.gain_reduction_db, meter.gain_reduction_db);
                        raise_peak(&target.detector_db, meter.detector_db);
                        target.dynamics_frame.store(frame_end, Ordering::Release);
                    }
                    if let Some(spectrum) = &runtime.spectra[i] {
                        // SAFETY: mixer-only producer.
                        unsafe { spectrum.producer.get().push(processor.output(i), frame_end) };
                    }
                }
            }
        }
        // Capture is read once per worker and fanned out. Topology edits
        // crossfade old and new graphs for 10 ms; a sink gets one write even
        // when both graphs use the same worker.
        for runtime_pointer in [active, fading] {
            // SAFETY: live runtime.
            let Some(runtime) = (unsafe { runtime_pointer.as_ref() }) else { continue };
            for (i, port) in runtime.ports.iter().enumerate() {
                let Some(port) = port else { continue };
                if !runtime.plan.nodes[i].kind.is_sink() {
                    continue;
                }
                let pointer = Arc::as_ptr(port);
                if output_ports[..rendered].contains(&pointer) {
                    continue;
                }
                output_ports[rendered] = pointer;
                rendered += 1;
                let Some(producer) = &port.producer else { continue };
                // SAFETY: mixer-only producer.
                let producer = unsafe { producer.get() };
                if !port.ready() {
                    if !producer.clear_pending() {
                        producer.request_clear();
                    }
                    continue;
                }
                let mut current: [&[Frame]; MAXIMUM_GRAPH_NODES] = [&[]; MAXIMUM_GRAPH_NODES];
                let mut old: [&[Frame]; MAXIMUM_GRAPH_NODES] = [&[]; MAXIMUM_GRAPH_NODES];
                let (mut current_count, mut old_count) = (0usize, 0usize);
                // SAFETY: live runtimes; outputs are read-only here.
                if let Some(active) = unsafe { active.as_ref() } {
                    // SAFETY: only this (mixer) thread touches a published
                    // runtime's processor, and this quantum's processing has
                    // finished: the outputs are only read from here on.
                    let processor = unsafe { &*active.processor.as_ptr() };
                    for (n, other) in active.ports.iter().enumerate() {
                        if other.as_ref().is_some_and(|o| Arc::as_ptr(o) == pointer) {
                            current[current_count] = processor.output(n);
                            current_count += 1;
                        }
                    }
                }
                // SAFETY: as above.
                if let Some(fading) = unsafe { fading.as_ref() } {
                    // SAFETY: as for the active runtime's processor above.
                    let processor = unsafe { &*fading.processor.as_ptr() };
                    for (n, other) in fading.ports.iter().enumerate() {
                        if other.as_ref().is_some_and(|o| Arc::as_ptr(o) == pointer) {
                            old[old_count] = processor.output(n);
                            old_count += 1;
                        }
                    }
                }
                let mut clipped = false;
                for (frame, out) in mixed.iter_mut().enumerate() {
                    let weight = if fading.is_null() {
                        1.0f32
                    } else {
                        1.0 - (fade_remaining - frame as u32 - 1) as f32 / 480.0
                    };
                    let (mut left, mut right, mut previous_left, mut previous_right) = (0.0f64, 0.0f64, 0.0f64, 0.0f64);
                    for buffer in &current[..current_count] {
                        left += f64::from(buffer[frame].left);
                        right += f64::from(buffer[frame].right);
                    }
                    for buffer in &old[..old_count] {
                        previous_left += f64::from(buffer[frame].left);
                        previous_right += f64::from(buffer[frame].right);
                    }
                    let weight = f64::from(weight);
                    left = left * weight + previous_left * (1.0 - weight);
                    right = right * weight + previous_right * (1.0 - weight);
                    clipped = clipped || left.abs() > 1.0 || right.abs() > 1.0;
                    // Clamp only the final endpoint sum; contributions may cancel.
                    *out = Frame::new(left.clamp(-1.0, 1.0) as f32, right.clamp(-1.0, 1.0) as f32);
                }
                // SAFETY: live runtime.
                if let (true, Some(active)) = (clipped, unsafe { active.as_ref() }) {
                    for (n, other) in active.ports.iter().enumerate() {
                        if other.as_ref().is_some_and(|o| Arc::as_ptr(o) == pointer) {
                            active.meters[n].clipped.store(true, Ordering::Relaxed);
                        }
                    }
                }
                if producer.size() > SAMPLE_RATE as usize / 10 {
                    producer.request_clear();
                }
                producer.push(&mixed);
            }
        }
        if !fading.is_null() {
            fade_remaining -= QUANTUM_FRAMES as u32;
            if fade_remaining == 0 {
                fading = std::ptr::null_mut();
            }
        }
        if !active.is_null() && fading.is_null() {
            // SAFETY: live runtime.
            let generation = unsafe { (*active).generation };
            inner.active_generation.store(generation, Ordering::Release);
        }
        inner.audio_frames.fetch_add(QUANTUM_FRAMES as u64, Ordering::Relaxed);
        now = qpc();
        // The interval covers adoption and reset, capture, both graphs during
        // a fade, meters and output dispatch; timer waits are excluded.
        previous_processing_end = now;
        if !active.is_null() {
            let old_state = processing_load.state();
            let old_transition = processing_load.transition_active();
            let valid = processing_load.observe_ticks(quantum_start, now, clock.completion_deadline(), frequency, processing_transition);
            if !valid
                || processing_load.window_samples() == 1
                || processing_load.sample_count() % crate::processing_load::ProcessingLoadMonitor::PUBLICATION_SAMPLES == 0
                || old_state != processing_load.state()
                || old_transition != processing_transition
            {
                inner.processing_load.publish(&processing_load.read());
            }
        }
        let next = clock.advance(now);
        if next <= now {
            continue;
        }
        let due = -((next - now) * 10_000_000 / frequency).max(1);
        // SAFETY: valid timer handle and due time.
        if unsafe { SetWaitableTimer(timer, &due, 0, None, None, false) }.is_err() {
            request_stop(inner);
            break;
        }
        // SAFETY: valid handles.
        if unsafe { WaitForMultipleObjects(&waits, false, INFINITE) } != windows::Win32::Foundation::WAIT_EVENT(WAIT_OBJECT_0.0 + 1) {
            break;
        }
    }
    // SAFETY: timer created above.
    unsafe {
        let _ = CancelWaitableTimer(timer);
        let _ = CloseHandle(timer);
    }
}

fn same_port(a: &Option<Arc<Port>>, b: &Option<Arc<Port>>) -> bool {
    match (a, b) {
        (Some(a), Some(b)) => Arc::ptr_eq(a, b),
        (None, None) => true,
        _ => false,
    }
}

/// A short, non-reversible token for an opaque Windows identifier, so the
/// log shows whether two identifiers are equal without recording them.
fn identity_token(value: &str) -> String {
    use std::hash::{Hash, Hasher};
    if value.is_empty() {
        return "none".into();
    }
    let mut hasher = std::collections::hash_map::DefaultHasher::new();
    value.hash(&mut hasher);
    format!("{:08x}", hasher.finish() as u32)
}

/// One line describing how a saved device binding resolves in `devices`:
/// what was saved, what matched and how, and how many active endpoints merely
/// share its name. Identifiers appear only as tokens and names not at all.
/// Written when it changes, this is the before/during/after record of a
/// device being switched off and on (RUST-REV-041).
fn identity_summary(node: &GraphNode, devices: &[Endpoint]) -> Option<String> {
    let direction = match node.kind.as_str() {
        "microphone" | "virtualInput" => "capture",
        "output" | "virtualOutput" => "render",
        _ => return None,
    };
    if !node.device.assigned() {
        return None;
    }
    let saved = format!("saved id#{} stable#{}", identity_token(&node.device.id), identity_token(&node.device.stable_id));
    let record = match identity::exact(devices, &node.device, direction) {
        Some(endpoint) => format!("saved-id record {} stable#{}", endpoint.state, identity_token(&endpoint.stable_id)),
        None => "saved-id record absent".into(),
    };
    let outcome = match identity::resolve(devices, &node.device, direction) {
        identity::Resolution::Active(endpoint, matched) => {
            format!("resolved by {} to id#{} stable#{}", matched.as_str(), identity_token(&endpoint.id), identity_token(&endpoint.stable_id))
        }
        identity::Resolution::Ambiguous => "unresolved: several active endpoints share the stable ID".into(),
        identity::Resolution::Unresolved => "unresolved: no active endpoint has the saved identity".into(),
    };
    let resolved_id = identity::resolve(devices, &node.device, direction).active().map(|e| e.id.as_str());
    let same_name = devices
        .iter()
        .filter(|e| {
            e.direction == direction
                && e.state == "Active"
                && Some(e.id.as_str()) != resolved_id
                && !node.device.name.is_empty()
                && e.name.eq_ignore_ascii_case(&node.device.name)
        })
        .count();
    Some(format!("{direction} {saved}; {record}; {outcome}; other active endpoints with the saved name: {same_name}"))
}

fn maintenance_loop(inner: &Inner) {
    let mut last_states: HashMap<String, String> = HashMap::new();
    let mut last_identities: HashMap<String, String> = HashMap::new();
    while !inner.stopping.load(Ordering::Acquire) {
        if inner.resume_requested.swap(false, Ordering::AcqRel) {
            retry(inner);
        }
        let devices = inner.catalog.enumerate();
        // The application list only feeds the desktop's pickers and notices
        // (capture workers find their own process): enumerate processes and
        // audio sessions only while a client wants it.
        let applications = applications_wanted(inner).then(enumerate_audio_applications);
        {
            let mut inventory = inner.inventory.lock().unwrap_or_else(|e| e.into_inner());
            let applications_changed = applications.as_ref().is_some_and(|list| *list != inventory.applications);
            if inventory.endpoints != devices || applications_changed {
                inventory.endpoints = devices;
                if let Some(applications) = applications {
                    inventory.applications = applications;
                }
                inventory.generation += 1;
            }
        }
        {
            // Outside the state lock's critical path: resolving the physical
            // location opens the settings file.
            let physical = {
                let state = lock(inner);
                state.store.path().to_path_buf()
            };
            let resolved = vinemix_platform::physical_path(&physical).ok().map(|p| p.to_string_lossy().into_owned());
            *inner.physical_settings.lock().unwrap_or_else(|e| e.into_inner()) = resolved;
        }
        {
            let mut state = lock(inner);
            retire(inner, &mut state);
            {
                let inventory = inner.inventory.lock().unwrap_or_else(|e| e.into_inner());
                last_identities.retain(|id, _| state.configuration.graph.nodes.iter().any(|n| &n.id == id));
                for node in &state.configuration.graph.nodes {
                    let Some(summary) = identity_summary(node, &inventory.endpoints) else {
                        last_identities.remove(&node.id);
                        continue;
                    };
                    if last_identities.get(&node.id) != Some(&summary) {
                        inner.log.write(&format!("Device identity {}: {summary}", node.id));
                        last_identities.insert(node.id.clone(), summary);
                    }
                }
            }
            refresh_output_ports(inner, &mut state);
            let published = inner.published.load(Ordering::Acquire);
            // SAFETY: owned by `state.runtimes`, held here.
            if let Some(runtime) = unsafe { published.as_ref() } {
                last_states.retain(|id, _| runtime.plan.nodes.iter().any(|n| &n.id == id));
                for (i, port) in runtime.ports.iter().enumerate() {
                    let Some(port) = port else { continue };
                    let state = port.status();
                    let summary = format!("{}: {}", state.state, state.message);
                    let id = &runtime.plan.nodes[i].id;
                    if last_states.get(id) != Some(&summary) {
                        inner.log.write(&format!("Node {id} {summary}"));
                        last_states.insert(id.clone(), summary);
                    }
                }
            }
        }
        let waits = [inner.stop_event.handle(), inner.maintenance_wake.handle()];
        if vinemix_wasapi::win::wait_any(&waits, 1000) == vinemix_wasapi::win::Wait::Signalled(0) {
            break;
        }
    }
}

#[cfg(test)]
mod identity_log_tests {
    use super::*;

    fn endpoint(id: &str, stable: &str, state: &str) -> Endpoint {
        Endpoint { id: id.into(), stable_id: stable.into(), name: "Example Headset".into(), direction: "render".into(), state: state.into(), ..Endpoint::default() }
    }

    /// The identity record distinguishes the cases RUST-REV-041 needs told
    /// apart, without containing any identifier or device name.
    #[test]
    fn identity_summary_is_anonymous_and_tells_the_cases_apart() {
        let mut node = GraphNode::new("main-output-1", "output", "Speakers", 0.0, 0.0);
        assert!(identity_summary(&node, &[]).is_none(), "unassigned nodes are not logged");
        node.device = DeviceBinding { id: "{raw-endpoint-id-A}".into(), stable_id: "raw-stable-S".into(), name: "Example Headset".into(), channel: -1 };
        let same = identity_summary(&node, &[endpoint("{raw-endpoint-id-A}", "raw-stable-S", "Active")]).unwrap();
        let replaced = identity_summary(&node, &[endpoint("{raw-endpoint-id-A}", "raw-stable-S", "Missing"), endpoint("{raw-endpoint-id-B}", "raw-stable-S", "Active")]).unwrap();
        let stranger = identity_summary(&node, &[endpoint("{raw-endpoint-id-B}", "raw-stable-T", "Active")]).unwrap();
        let ambiguous = identity_summary(&node, &[endpoint("{raw-endpoint-id-B}", "raw-stable-S", "Active"), endpoint("{raw-endpoint-id-C}", "raw-stable-S", "Active")]).unwrap();
        assert!(same.contains("resolved by endpoint ID"), "{same}");
        assert!(replaced.contains("saved-id record Missing") && replaced.contains("resolved by stable ID"), "{replaced}");
        assert!(stranger.contains("saved-id record absent") && stranger.contains("unresolved: no active endpoint") && stranger.ends_with("saved name: 1"), "{stranger}");
        assert!(ambiguous.contains("several active endpoints share"), "{ambiguous}");
        for line in [&same, &replaced, &stranger, &ambiguous] {
            assert!(!line.contains("raw-") && !line.contains("Example"), "identifiers and names stay out of the log: {line}");
        }
        // Equal identifiers give equal tokens; different ones differ.
        assert_eq!(identity_token("{raw-endpoint-id-A}"), identity_token("{raw-endpoint-id-A}"));
        assert_ne!(identity_token("{raw-endpoint-id-A}"), identity_token("{raw-endpoint-id-B}"));
        assert_eq!(identity_token(""), "none");
    }
}
