//! One device stream per worker thread (`AudioWorker` in
//! `windows_audio.cpp`). Capture workers produce stereo 48 kHz frames into
//! their queue; render workers consume a queue the mixer fills. There is no
//! default-device substitution: a worker only ever opens the endpoint its
//! binding resolves to, and recovers locally with bounded backoff.

use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::thread::JoinHandle;
use std::time::{Duration, Instant};

use vinemix_model::config::DeviceBinding;
use vinemix_model::status::{Endpoint, PathState};
use vinemix_resample::AdaptiveResampler;
use vinemix_rt::{Consumer, DriftController, Frame, Monitor, Producer, StreamBufferPolicy, StreamStability, SAMPLE_RATE};
use windows::core::{HRESULT, PCWSTR};
use windows::Win32::Foundation::{E_FAIL, E_INVALIDARG};
use windows::Win32::Media::Audio::{
    IAudioCaptureClient, IAudioClient, IAudioClock, IAudioRenderClient, IAudioSessionControl2, IMMDevice, IMMDeviceEnumerator,
    MMDeviceEnumerator, AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY, AUDCLNT_BUFFERFLAGS_SILENT, AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR,
    AUDCLNT_E_BUFFER_ERROR, AUDCLNT_E_BUFFER_SIZE_ERROR, AUDCLNT_E_UNSUPPORTED_FORMAT,
};
use windows::Win32::System::Com::{CoCreateInstance, CLSCTX_ALL};
use windows::Win32::System::Threading::{
    AvRevertMmThreadCharacteristics, AvSetMmThreadCharacteristicsW, AvSetMmThreadPriority, AVRT_PRIORITY_HIGH,
};

use crate::catalog::{DeviceCatalog, StreamWatch};
use crate::clock::{failure_message, failure_reason, failure_state, ClockEstimate, E_ACCESSDENIED};
use crate::format::SampleFormat;
use crate::policy::{resolve_endpoint, stream_endpoint_rejection, TopologyGrace};
use crate::stream::{initialize_exclusive_render, initialize_shared_stream, render_frames_available};
use crate::win::{hresult_from_win32, wait_any, wide, Apartment, Event, Wait, ERROR_TIMEOUT, FOREVER};

/// The worker's end of its frame queue.
#[derive(Debug)]
pub enum QueueEnd {
    /// A capture worker produces frames for the mixer.
    Capture(Producer),
    /// A render worker consumes frames the mixer produced.
    Render(Consumer),
}

#[derive(Debug, Default)]
struct Guarded {
    desired: DeviceBinding,
    path: PathState,
}

#[derive(Debug)]
struct Shared {
    capture: bool,
    microphone: bool,
    required_transport: String,
    exclusive_render: bool,
    catalog: Arc<DeviceCatalog>,
    monitor: Monitor,
    stop_event: Event,
    control_event: Event,
    guarded: Mutex<Guarded>,
    configuration_serial: AtomicU64,
    stopped: AtomicBool,
    ready: AtomicBool,
    stream_underruns: AtomicU64,
    target_queue_frames: AtomicU32,
    pending_resampler_frames: AtomicU32,
    engine_period_frames: AtomicU32,
}

/// A device stream owned by its own thread.
#[derive(Debug)]
pub struct AudioWorker {
    shared: Arc<Shared>,
    thread: Mutex<Option<JoinHandle<()>>>,
}

impl AudioWorker {
    /// `required_transport`: `""` (any capture), `"listening"`, `"vbCable"`
    /// or `"vacLite"` (exclusive render).
    pub fn new(
        microphone: bool,
        queue: QueueEnd,
        monitor: Monitor,
        catalog: Arc<DeviceCatalog>,
        required_transport: &str,
    ) -> windows::core::Result<Self> {
        let capture = matches!(queue, QueueEnd::Capture(_));
        let shared = Arc::new(Shared {
            capture,
            microphone,
            required_transport: required_transport.to_owned(),
            exclusive_render: !capture && required_transport == "vacLite",
            catalog,
            monitor,
            stop_event: Event::new(true)?,
            control_event: Event::new(false)?,
            guarded: Mutex::new(Guarded::default()),
            configuration_serial: AtomicU64::new(0),
            stopped: AtomicBool::new(false),
            ready: AtomicBool::new(false),
            stream_underruns: AtomicU64::new(0),
            target_queue_frames: AtomicU32::new(0),
            pending_resampler_frames: AtomicU32::new(0),
            engine_period_frames: AtomicU32::new(0),
        });
        let worker = shared.clone();
        let thread = std::thread::Builder::new()
            .name(if capture { "vinemix-capture".into() } else { "vinemix-render".into() })
            .spawn(move || Runner::new(worker, queue).run())
            .map_err(|_| windows::core::Error::from(E_FAIL))?;
        Ok(Self { shared, thread: Mutex::new(Some(thread)) })
    }

    pub fn configure(&self, binding: DeviceBinding) {
        {
            let mut guarded = self.shared.lock();
            let old = &guarded.desired;
            if old.id == binding.id && old.stable_id == binding.stable_id && old.name == binding.name && old.channel == binding.channel {
                return;
            }
            let assigned = binding.assigned();
            guarded.desired = binding;
            // The worker thread reports its progress asynchronously. Until it
            // has looked at this assignment, a status read must not still say
            // "Unassigned" (a new worker's initial state): an assigned device
            // that is being connected is pending, never unassigned.
            if assigned && guarded.path.state == "Unassigned" {
                guarded.path.state = "Resolving".into();
                guarded.path.message = "Finding selected device".into();
                guarded.path.reason.clear();
            }
            self.shared.configuration_serial.fetch_add(1, Ordering::Release);
        }
        self.shared.control_event.set();
    }

    pub fn retry(&self) {
        self.shared.configuration_serial.fetch_add(1, Ordering::Release);
        self.shared.control_event.set();
    }

    pub fn ready(&self) -> bool {
        self.shared.ready.load(Ordering::Acquire)
    }

    /// Negotiated callback period in the graph's 48 kHz frames.
    pub fn period_frames(&self) -> u32 {
        self.shared.engine_period_frames.load(Ordering::Relaxed)
    }

    pub fn target_queue_frames(&self) -> u32 {
        self.shared.target_queue_frames.load(Ordering::Relaxed)
    }

    pub fn status(&self) -> PathState {
        let mut state = self.shared.lock().path.clone();
        let stats = self.shared.monitor.stats();
        let rate = f64::from(SAMPLE_RATE);
        state.queue_ms = self.shared.monitor.size() as f64 * 1000.0 / rate;
        state.target_queue_ms = f64::from(self.shared.target_queue_frames.load(Ordering::Relaxed)) * 1000.0 / rate;
        state.resampler_pending_ms = f64::from(self.shared.pending_resampler_frames.load(Ordering::Relaxed)) * 1000.0 / rate;
        state.underruns = stats.underrun_events + self.shared.stream_underruns.load(Ordering::Relaxed);
        state.overruns = stats.overrun_events;
        state
    }

    pub fn stop(&self) {
        self.shared.ready.store(false, Ordering::Release);
        if !self.shared.stopped.swap(true, Ordering::AcqRel) {
            self.shared.stop_event.set();
            self.shared.control_event.set();
        }
        if let Some(thread) = self.thread.lock().unwrap_or_else(|e| e.into_inner()).take() {
            let _ = thread.join();
        }
    }
}

impl Drop for AudioWorker {
    fn drop(&mut self) {
        self.stop();
    }
}

impl Shared {
    fn lock(&self) -> std::sync::MutexGuard<'_, Guarded> {
        self.guarded.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn state(&self, serial: u64, value: &str, message: &str, endpoint: Option<&Endpoint>, reason: &str) {
        let mut guarded = self.lock();
        if self.configuration_serial.load(Ordering::Acquire) != serial {
            return;
        }
        self.ready.store(value == "Running", Ordering::Release);
        if value != "Running" && value != "Priming" {
            self.target_queue_frames.store(0, Ordering::Relaxed);
            self.pending_resampler_frames.store(0, Ordering::Relaxed);
            self.engine_period_frames.store(0, Ordering::Relaxed);
        }
        let path = &mut guarded.path;
        path.state = value.into();
        path.message = message.into();
        path.reason = reason.into();
        if path.state != "Running" && path.state != "Priming" {
            path.period_ms = 0.0;
            path.buffer_ms = 0.0;
        }
        if let Some(endpoint) = endpoint {
            path.endpoint_id.clone_from(&endpoint.id);
            // Running preserves the format actually opened, including an
            // exclusive stream whose format differs from the shared mix.
            if value != "Running" {
                path.channels = endpoint.channels;
                path.rate = endpoint.rate;
            }
        } else if path.state == "Unassigned" {
            path.endpoint_id.clear();
            path.rate = 0;
            path.channels = 0;
        }
    }

    fn resolve(&self, selection: &DeviceBinding) -> Option<Endpoint> {
        resolve_endpoint(&self.catalog.enumerate(), selection, self.capture)
    }
}

/// The worker thread's private state.
struct Runner {
    shared: Arc<Shared>,
    queue: QueueEnd,
    observed_generation: u64,
    observed_configuration: u64,
    /// Explicit selection or retry resets this to true.
    prefer_low_latency: bool,
}

/// After the device inventory changes, glitches are attributed to the Windows
/// audio service rebuilding its graph, not to this stream — within the bounds
/// of [`TopologyGrace`]. Glitches just before the change was observed are
/// forgiven too.
const TOPOLOGY_LEAD_SECONDS: f64 = 2.0;

/// A stream ended: `Ok` means a deliberate reopen (S_FALSE in C++).
type StreamResult = Result<(), HRESULT>;

/// An MMCSS registration for the current thread.
#[derive(Debug)]
pub struct Mmcss(windows::Win32::Foundation::HANDLE);

impl Drop for Mmcss {
    fn drop(&mut self) {
        if !self.0.is_invalid() && !self.0 .0.is_null() {
            // SAFETY: reverts the characteristics set on this thread.
            unsafe {
                let _ = AvRevertMmThreadCharacteristics(self.0);
            }
        }
    }
}

pub(crate) fn pro_audio(task: &str, high: bool) -> Mmcss {
    let name = wide(task);
    let mut index = 0u32;
    // SAFETY: valid task name and index pointer.
    let handle = unsafe { AvSetMmThreadCharacteristicsW(PCWSTR(name.as_ptr()), &mut index) }.unwrap_or_default();
    if high && !handle.is_invalid() && !handle.0.is_null() {
        // SAFETY: valid MMCSS handle.
        unsafe {
            let _ = AvSetMmThreadPriority(handle, AVRT_PRIORITY_HIGH);
        }
    }
    Mmcss(handle)
}

pub(crate) use pro_audio as mmcss;

/// Registers the calling thread with an MMCSS task at normal priority; the
/// registration is reverted when the returned guard drops.
pub fn mmcss_task(task: &str) -> Mmcss {
    pro_audio(task, false)
}

struct StopOnDrop<'a>(&'a IAudioClient);

impl Drop for StopOnDrop<'_> {
    fn drop(&mut self) {
        // SAFETY: stopping a started client.
        unsafe {
            let _ = self.0.Stop();
        }
    }
}

impl Runner {
    fn new(shared: Arc<Shared>, queue: QueueEnd) -> Self {
        Self { shared, queue, observed_generation: 0, observed_configuration: 0, prefer_low_latency: true }
    }

    fn clear_queue(&mut self) {
        match &mut self.queue {
            QueueEnd::Capture(producer) => producer.request_clear(),
            QueueEnd::Render(consumer) => consumer.clear(),
        }
    }

    /// Whether the running stream should continue: the configuration is the
    /// one it opened for and the catalog has not found its endpoint changed.
    /// Runs every wake-up of the stream loop and only loads atomics — the
    /// catalog thread resolves bindings and compares inventories
    /// ([`StreamWatch`]); nothing here locks, allocates or copies.
    fn still_selected(&self, watch: &StreamWatch, serial: u64) -> bool {
        self.shared.configuration_serial.load(Ordering::Acquire) == serial && !watch.is_stale()
    }

    fn open_and_run(&mut self, endpoint: &Endpoint, selection: &DeviceBinding, serial: u64) -> StreamResult {
        let shared = self.shared.clone();
        // SAFETY: COM initialised on this thread by `run`.
        let enumerator: IMMDeviceEnumerator =
            unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) }.map_err(|e| e.code())?;
        let id = wide(&endpoint.id);
        // SAFETY: valid NUL-terminated id.
        let device: IMMDevice = unsafe { enumerator.GetDevice(PCWSTR(id.as_ptr())) }.map_err(|e| e.code())?;
        let (client, wave, mut period_frames) = if shared.exclusive_render {
            let (client, wave) = initialize_exclusive_render(&device)?;
            (client, wave, 0)
        } else {
            let stream = initialize_shared_stream(&device, shared.capture, self.prefer_low_latency)?;
            (stream.client, stream.format, stream.period_frames)
        };
        let format = SampleFormat::new(&wave.description()).ok_or(AUDCLNT_E_UNSUPPORTED_FORMAT)?;
        let channel = if shared.microphone { selection.channel } else { -1 };
        if shared.capture && channel >= i32::from(format.channels) {
            return Err(E_INVALIDARG);
        }
        let audio_event = Event::new(false).map_err(|e| e.code())?;
        // SAFETY: COM calls on the initialised client.
        unsafe { client.SetEventHandle(audio_event.handle()) }.map_err(|e| e.code())?;
        // SAFETY: as above.
        let buffer_frames = unsafe { client.GetBufferSize() }.map_err(|e| e.code())?;
        if buffer_frames == 0 || buffer_frames > format.rate * 4 {
            return Err(AUDCLNT_E_BUFFER_SIZE_ERROR);
        }
        // Exclusive event rendering consumes a whole buffer per event; a
        // legacy shared device with an unknown period uses its buffer size.
        if shared.exclusive_render || period_frames == 0 {
            period_frames = buffer_frames;
        }
        let mut buffering = StreamBufferPolicy::new(format.rate, period_frames, shared.capture).map_err(|_| E_FAIL)?;
        let engine_period = buffering.device_frames_to_engine_frames(period_frames);
        if shared.capture && engine_period.saturating_add(StreamBufferPolicy::CAPTURE_RESERVE_FRAMES) > shared.monitor.capacity() {
            return Err(AUDCLNT_E_BUFFER_SIZE_ERROR);
        }
        shared.engine_period_frames.store(engine_period as u32, Ordering::Relaxed);
        if !shared.capture {
            // SAFETY: COM service query on the client.
            if let Ok(session) = unsafe { client.GetService::<IAudioSessionControl2>() } {
                // SAFETY: as above.
                unsafe {
                    let _ = session.SetDuckingPreference(true);
                }
            }
        }
        let capture_client: Option<IAudioCaptureClient>;
        let render_client: Option<IAudioRenderClient>;
        if shared.capture {
            // SAFETY: as above.
            capture_client = Some(unsafe { client.GetService::<IAudioCaptureClient>() }.map_err(|e| e.code())?);
            render_client = None;
        } else {
            // SAFETY: as above.
            render_client = Some(unsafe { client.GetService::<IAudioRenderClient>() }.map_err(|e| e.code())?);
            capture_client = None;
        }
        // SAFETY: as above.
        let clock: Option<IAudioClock> = unsafe { client.GetService::<IAudioClock>() }.ok();
        // SAFETY: as above.
        let clock_frequency = clock.as_ref().and_then(|c| unsafe { c.GetFrequency() }.ok()).unwrap_or(0);

        let chunk_capacity = (buffer_frames as usize + 1024).max(4096);
        let mut input = vec![Frame::SILENT; chunk_capacity];
        let mut output = vec![Frame::SILENT; chunk_capacity];
        let (from_rate, to_rate) = if shared.capture { (format.rate, SAMPLE_RATE) } else { (SAMPLE_RATE, format.rate) };
        let mut resampler =
            AdaptiveResampler::new(from_rate, to_rate, (chunk_capacity * 4).max(32_768)).map_err(|_| AUDCLNT_E_UNSUPPORTED_FORMAT)?;
        let mut drift = DriftController::new();
        let mut clock_estimate = ClockEstimate::default();
        self.clear_queue_for_open();
        shared.state(serial, "Priming", "Preparing current live audio", Some(endpoint), "");
        {
            let mut guarded = shared.lock();
            if shared.configuration_serial.load(Ordering::Acquire) != serial {
                return Ok(());
            }
            guarded.path.rate = format.rate;
            guarded.path.channels = u32::from(format.channels);
            guarded.path.period_ms = f64::from(period_frames) * 1000.0 / f64::from(format.rate);
            guarded.path.buffer_ms = f64::from(buffer_frames) * 1000.0 / f64::from(format.rate);
        }
        shared.target_queue_frames.store(buffering.target_frames() as u32, Ordering::Relaxed);
        if let Some(render) = &render_client {
            // SAFETY: priming the whole buffer with silence.
            unsafe {
                render.GetBuffer(buffer_frames).map_err(|e| e.code())?;
                render.ReleaseBuffer(buffer_frames, AUDCLNT_BUFFERFLAGS_SILENT.0 as u32).map_err(|e| e.code())?;
            }
        }
        // SAFETY: starting the initialised client.
        unsafe { client.Start() }.map_err(|e| e.code())?;
        let _mmcss = pro_audio("Pro Audio", true);
        let _stop = StopOnDrop(&client);
        shared.lock().path.reconnects += 1;
        let running_message = if shared.exclusive_render {
            "Direct virtual microphone output (exclusive WASAPI)"
        } else if self.prefer_low_latency {
            ""
        } else {
            "Compatible audio timing after repeated interruptions"
        };
        shared.state(serial, "Running", running_message, Some(endpoint), "");
        let events = [shared.stop_event.handle(), shared.control_event.handle(), audio_event.handle()];
        let mut last_event = Instant::now();
        let stream_start = last_event;
        let mut stability = StreamStability::default();
        let mut grace = TopologyGrace::default();
        // Registered before the stream starts; judged against the inventory
        // as it is now, in case it changed since `endpoint` was resolved.
        let watch = shared.catalog.watch(selection.clone(), endpoint, shared.capture, shared.microphone, &shared.required_transport);
        let mut capture_check = last_event + Duration::from_secs(1);
        let mut previous_capture_underruns = shared.monitor.stats().underrun_events;
        let mut capture_warmed = false;
        let mut fade_frames: u64 = 0;
        let fade_length = u64::from((format.rate / 100).max(1));
        let mut render_primed = false;
        let mut render_underrun = false;
        let exclusive = shared.exclusive_render;

        while !shared.stopped.load(Ordering::Relaxed) {
            let wait = wait_any(&events, 250);
            match wait {
                Wait::Signalled(0) => return Ok(()),
                Wait::Failed => return Err(hresult_from_win32(std::io::Error::last_os_error().raw_os_error().unwrap_or(0) as u32)),
                _ => {}
            }
            if !self.still_selected(&watch, serial) {
                return Ok(());
            }
            let now = Instant::now();
            let stream_seconds = (now - stream_start).as_secs_f64();
            let generation = shared.catalog.generation();
            if generation != self.observed_generation {
                self.observed_generation = generation;
                // Bounded: a run of inventory changes shares one allowance.
                if grace.changed(stream_seconds) {
                    stability.forgive_since(stream_seconds - TOPOLOGY_LEAD_SECONDS);
                }
            }
            let settling = grace.settling(stream_seconds);
            if wait != Wait::Signalled(2) {
                // Silent buffers still signal; this catches a stopped stream.
                if now - last_event > Duration::from_secs(2) {
                    return Err(hresult_from_win32(ERROR_TIMEOUT));
                }
                continue;
            }
            let elapsed = (now - last_event).as_secs_f64().clamp(0.0001, 0.25);
            if now - last_event > Duration::from_secs(1) {
                return Err(hresult_from_win32(ERROR_TIMEOUT));
            }
            last_event = now;
            let mut needs_compatible_timing = |now: Instant, prefer: &mut bool| -> bool {
                if exclusive || !*prefer || settling {
                    return false;
                }
                if !stability.observe((now - stream_start).as_secs_f64()) {
                    return false;
                }
                *prefer = false;
                true
            };
            if let Some(capture) = &capture_client {
                let QueueEnd::Capture(producer) = &mut self.queue else { unreachable!("capture worker") };
                let mut feedback_elapsed = elapsed;
                // SAFETY: COM calls on the started capture client.
                let mut packet = unsafe { capture.GetNextPacketSize() }.map_err(|e| e.code())?;
                while packet != 0 {
                    let mut data: *mut u8 = std::ptr::null_mut();
                    let (mut count, mut flags, mut position, mut qpc) = (0u32, 0u32, 0u64, 0u64);
                    // SAFETY: valid output pointers; released below.
                    unsafe { capture.GetBuffer(&mut data, &mut count, &mut flags, Some(&mut position), Some(&mut qpc)) }
                        .map_err(|e| e.code())?;
                    if flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY.0 as u32 != 0 {
                        if needs_compatible_timing(now, &mut self.prefer_low_latency) {
                            // SAFETY: releasing the buffer obtained above.
                            unsafe {
                                let _ = capture.ReleaseBuffer(count);
                            }
                            return Ok(());
                        }
                        // Some input was lost upstream. The queued audio and the
                        // resampler history are still good: keep them, so a few
                        // missing milliseconds stay a few milliseconds instead
                        // of an emptied queue the mixer must re-prime. A short
                        // fade-in softens the splice; drift control re-centres
                        // the queue.
                        clock_estimate = ClockEstimate::default();
                        fade_frames = 0;
                        capture_warmed = false;
                        capture_check = now + Duration::from_secs(1);
                    }
                    if flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR.0 as u32 == 0 {
                        clock_estimate.observe(position, qpc, f64::from(format.rate));
                    }
                    let silent = flags & AUDCLNT_BUFFERFLAGS_SILENT.0 as u32 != 0;
                    let block = usize::from(format.block_align);
                    let packet_bytes = (!data.is_null()).then(|| {
                        // SAFETY: WASAPI guarantees `count` frames of `block` bytes.
                        unsafe { std::slice::from_raw_parts(data, count as usize * block) }
                    });
                    let mut offset = 0usize;
                    while offset < count as usize {
                        let frames = input.len().min(count as usize - offset);
                        let source = packet_bytes.map(|bytes| &bytes[offset * block..]);
                        format.decode(source, &mut input[..frames], channel, silent);
                        for frame in input.iter_mut().take(frames) {
                            fade_frames += 1;
                            let fade = (fade_frames.min(fade_length) as f32) / fade_length as f32;
                            frame.left *= fade;
                            frame.right *= fade;
                        }
                        let accepted = resampler.push(&input[..frames]);
                        if accepted != frames {
                            resampler.reset();
                            producer.request_clear();
                        }
                        // Several packets can arrive at one late callback:
                        // advance feedback time once, not once per packet.
                        let correction = drift.update(
                            producer.size() as f64,
                            buffering.target_frames() as f64,
                            std::mem::replace(&mut feedback_elapsed, 0.0),
                        ) / clock_estimate.multiplier;
                        loop {
                            let generated = resampler.produce(&mut output, correction);
                            if resampler.last_error() != 0 {
                                // SAFETY: releasing the buffer obtained above.
                                unsafe {
                                    let _ = capture.ReleaseBuffer(count);
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
                    unsafe { capture.ReleaseBuffer(count) }.map_err(|e| e.code())?;
                    if shared.stopped.load(Ordering::Relaxed) || shared.configuration_serial.load(Ordering::Acquire) != serial {
                        return Ok(());
                    }
                    // SAFETY: as above.
                    packet = unsafe { capture.GetNextPacketSize() }.map_err(|e| e.code())?;
                }
                // Expected startup starvation is ignored; sustained late
                // delivery earns a bounded extra block of jitter protection.
                if now >= capture_check {
                    let underruns = shared.monitor.stats().underrun_events;
                    // A device change is a one-off stall, not a reason to keep
                    // extra latency for the rest of this stream.
                    if capture_warmed && !settling && underruns - previous_capture_underruns > 2 {
                        buffering.observe_underrun();
                    }
                    previous_capture_underruns = underruns;
                    capture_warmed = true;
                    capture_check = now + Duration::from_secs(1);
                }
            } else if let Some(render) = &render_client {
                let QueueEnd::Render(consumer) = &mut self.queue else { unreachable!("render worker") };
                let available = render_frames_available(&client, exclusive, buffer_frames)? as usize;
                if available == 0 {
                    continue;
                }
                if let (Some(clock), true) = (&clock, clock_frequency != 0) {
                    let (mut position, mut qpc) = (0u64, 0u64);
                    // SAFETY: valid output pointers.
                    if unsafe { clock.GetPosition(&mut position, Some(&mut qpc)) }.is_ok() {
                        clock_estimate.observe(position, qpc, clock_frequency as f64);
                    }
                }
                // Discard a stalled output's backlog instead of replaying old
                // speech. Each render worker owns this consumer cursor.
                if consumer.clear_pending() || consumer.size() > SAMPLE_RATE as usize / 5 {
                    consumer.clear();
                    resampler.reset();
                    drift.reset();
                    fade_frames = 0;
                    render_primed = false;
                }
                let buffered = (consumer.size() + resampler.pending_input_frames()) as f64;
                let demand = ((available as u64 * u64::from(SAMPLE_RATE) + u64::from(format.rate) - 1) / u64::from(format.rate)) as usize;
                if !render_primed && buffered >= buffering.prime_frames(demand) as f64 {
                    render_primed = true;
                }
                let correction = drift.update(buffered, buffering.target_frames() as f64, elapsed) * clock_estimate.multiplier;
                let mut filled = 0usize;
                if render_primed {
                    while filled < available {
                        let generated = resampler.produce(&mut output[filled..available], correction);
                        if resampler.last_error() != 0 {
                            return Err(E_FAIL);
                        }
                        filled += generated;
                        if filled >= available {
                            break;
                        }
                        let queued = consumer.size().min(input.len());
                        if queued == 0 {
                            break;
                        }
                        let read = consumer.pop(&mut input[..queued]);
                        if read == 0 {
                            break;
                        }
                        if resampler.push(&input[..read]) != read {
                            resampler.reset();
                            fade_frames = 0;
                            break;
                        }
                    }
                }
                if filled < available {
                    output[filled..available].fill(Frame::SILENT);
                    if !render_underrun && render_primed {
                        shared.stream_underruns.fetch_add(1, Ordering::Relaxed);
                        if !settling {
                            buffering.observe_underrun();
                        }
                        if needs_compatible_timing(now, &mut self.prefer_low_latency) {
                            return Ok(());
                        }
                    }
                    render_underrun = true;
                    // Re-prime rather than alternate bursts and silence.
                    render_primed = false;
                    fade_frames = 0;
                } else {
                    render_underrun = false;
                }
                for frame in output.iter_mut().take(filled) {
                    fade_frames += 1;
                    let fade = (fade_frames.min(fade_length) as f32) / fade_length as f32;
                    frame.left *= fade;
                    frame.right *= fade;
                }
                // SAFETY: obtaining the device buffer for `available` frames.
                let data = match unsafe { render.GetBuffer(available as u32) } {
                    Ok(data) => data,
                    Err(error) if exclusive && error.code() == AUDCLNT_E_BUFFER_ERROR => {
                        // Exclusive drivers can briefly have no next buffer.
                        shared.stream_underruns.fetch_add(1, Ordering::Relaxed);
                        render_primed = false;
                        fade_frames = 0;
                        continue;
                    }
                    Err(error) => return Err(error.code()),
                };
                // SAFETY: the device buffer holds `available` frames of block_align bytes.
                let bytes = unsafe { std::slice::from_raw_parts_mut(data, available * usize::from(format.block_align)) };
                format.encode(&output[..available], bytes);
                let flags = if filled != 0 { 0 } else { AUDCLNT_BUFFERFLAGS_SILENT.0 as u32 };
                // SAFETY: releasing the buffer obtained above.
                unsafe { render.ReleaseBuffer(available as u32, flags) }.map_err(|e| e.code())?;
            }
            shared.target_queue_frames.store(buffering.target_frames() as u32, Ordering::Relaxed);
            let pending = resampler.pending_input_frames() as u64;
            let pending = if shared.capture {
                (pending * u64::from(SAMPLE_RATE) + u64::from(format.rate) - 1) / u64::from(format.rate)
            } else {
                pending
            };
            shared.pending_resampler_frames.store(pending as u32, Ordering::Relaxed);
        }
        Ok(())
    }

    fn clear_queue_for_open(&mut self) {
        self.clear_queue();
    }

    fn run(mut self) {
        let apartment = Apartment::enter();
        let shared = self.shared.clone();
        let delays = [250u32, 1000, 2000, 5000];
        let mut backoff = 0usize;
        let events = [shared.stop_event.handle(), shared.control_event.handle()];
        while !shared.stopped.load(Ordering::Relaxed) {
            let (selection, serial) = {
                let guarded = shared.lock();
                (guarded.desired.clone(), shared.configuration_serial.load(Ordering::Acquire))
            };
            let generation = shared.catalog.generation();
            if serial != self.observed_configuration {
                self.prefer_low_latency = true;
            }
            if serial != self.observed_configuration || generation != self.observed_generation {
                backoff = 0;
                self.observed_configuration = serial;
                self.observed_generation = generation;
            }
            let delay;
            if selection.id.is_empty() && selection.stable_id.is_empty() {
                shared.state(serial, "Unassigned", "", None, "");
                // No notification can resolve an empty assignment: sleep until
                // selection, retry or stop.
                if wait_any(&events, FOREVER) == Wait::Signalled(0) {
                    break;
                }
                continue;
            } else if !apartment.ok() {
                shared.state(serial, "Error", "Could not initialize Windows audio COM apartment", None, "");
                delay = 5000;
            } else {
                shared.state(serial, "Resolving", "Finding selected device", None, "");
                let endpoint = shared.resolve(&selection);
                match &endpoint {
                    Some(endpoint) if endpoint.state == "Active" => {
                        if let Some(rejection) =
                            stream_endpoint_rejection(endpoint, shared.capture, shared.microphone, &shared.required_transport)
                        {
                            shared.state(serial, "Error", rejection, Some(endpoint), "");
                        } else {
                            let opening =
                                if shared.exclusive_render { "Opening direct virtual microphone output" } else { "Opening shared-mode audio" };
                            shared.state(serial, "Opening", opening, Some(endpoint), "");
                            let result = self.open_and_run(endpoint, &selection, serial);
                            shared.ready.store(false, Ordering::Release);
                            self.clear_queue();
                            if shared.stopped.load(Ordering::Relaxed) {
                                break;
                            }
                            match result {
                                Ok(()) => {
                                    backoff = 0;
                                    continue;
                                }
                                Err(code) => {
                                    let mut message = failure_message(code.0, shared.exclusive_render);
                                    if code.0 == E_ACCESSDENIED && !shared.capture && !shared.exclusive_render {
                                        message = "Output access blocked; check Windows sound device settings".into();
                                    }
                                    shared.state(serial, failure_state(code.0), &message, Some(endpoint), failure_reason(code.0));
                                }
                            }
                        }
                    }
                    other => {
                        let disabled = other.as_ref().is_some_and(|e| e.state == "Disabled");
                        let message = if disabled { "Device disabled in Windows Sound settings" } else { "Waiting for device" };
                        let reason = if disabled { "device_disabled" } else { "device_unavailable" };
                        shared.state(serial, "Waiting", message, other.as_ref(), reason);
                    }
                }
                delay = delays[backoff.min(delays.len() - 1)];
                if backoff + 1 < delays.len() {
                    backoff += 1;
                }
            }
            // Poll the catalog generation while waiting so a fresh arrival
            // breaks a long backoff within 100 ms.
            let mut waited = 0u32;
            while waited < delay && !shared.stopped.load(Ordering::Relaxed) {
                let step = 100.min(delay - waited);
                match wait_any(&events, step) {
                    Wait::Signalled(0) => return,
                    Wait::Signalled(_) => break,
                    _ => {}
                }
                if shared.catalog.generation() != self.observed_generation {
                    break;
                }
                waited += step;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use vinemix_rt::frame_queue;

    /// RUST-REV-032: the status sequence of a newly created worker that is
    /// given an assigned device. The endpoint ID is synthetic and matches no
    /// device, so nothing is opened: the worker settles on "Waiting".
    #[test]
    fn assigned_worker_is_pending_never_unassigned() {
        for capture in [false, true] {
            let catalog = Arc::new(DeviceCatalog::new().expect("device catalog"));
            let (producer, consumer, monitor) = frame_queue(9600);
            let (queue, _other_end): (QueueEnd, Box<dyn std::any::Any>) =
                if capture { (QueueEnd::Capture(producer), Box::new(consumer)) } else { (QueueEnd::Render(consumer), Box::new(producer)) };
            let worker = AudioWorker::new(capture, queue, monitor, catalog, if capture { "" } else { "listening" }).expect("worker");
            assert_eq!(worker.status().state, "Unassigned", "nothing is assigned yet");
            worker.configure(DeviceBinding {
                id: "vinemix-test-endpoint-that-does-not-exist".into(),
                stable_id: String::new(),
                name: "Example returning device".into(),
                channel: -1,
            });
            // An iteration that captured the empty assignment before configure
            // must never publish it after the new binding becomes visible.
            worker.shared.state(0, "Unassigned", "", None, "");
            assert_ne!(worker.status().state, "Unassigned");
            // Sample from the very first read after assignment onwards.
            let mut seen: Vec<String> = Vec::new();
            let until = Instant::now() + Duration::from_millis(600);
            while Instant::now() < until {
                let state = worker.status().state;
                if seen.last() != Some(&state) {
                    seen.push(state);
                }
                std::thread::yield_now();
            }
            assert!(!seen.iter().any(|s| s == "Unassigned"), "an assigned device was reported as unassigned: {seen:?}");
            // It then alternates between looking and waiting with backoff.
            assert!(seen.iter().any(|s| s == "Waiting"), "{seen:?}");
            assert!(seen.iter().all(|s| s == "Resolving" || s == "Waiting"), "{seen:?}");
            // Clearing the assignment is the only way back to "Unassigned".
            worker.configure(DeviceBinding::default());
            let until = Instant::now() + Duration::from_secs(2);
            while worker.status().state != "Unassigned" && Instant::now() < until {
                std::thread::sleep(Duration::from_millis(5));
            }
            assert_eq!(worker.status().state, "Unassigned");
            worker.stop();
            let serial = worker.shared.configuration_serial.load(Ordering::Acquire);
            // A status transition must not overwrite an exclusive stream's
            // actual format with the endpoint's differently configured mix.
            {
                let mut path = worker.shared.lock();
                path.path.rate = 48_000;
                path.path.channels = 2;
            }
            worker.shared.state(serial, "Running", "", Some(&Endpoint { rate: 44_100, channels: 6, ..Default::default() }), "");
            let running = worker.status();
            assert_eq!((running.rate, running.channels), (48_000, 2));
            worker.shared.state(serial, "Unassigned", "", None, "");
        }
    }
}
