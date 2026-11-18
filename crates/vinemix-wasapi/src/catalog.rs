//! The endpoint catalog: a dedicated COM thread enumerates every capture and
//! render endpoint, caches the result and wakes on device notifications.
//! Notifications only wake that thread; they never touch an audio client. A
//! failed enumeration keeps the last complete inventory.
//!
//! The generation advances only when the inventory actually changed (or on an
//! explicit refresh). Default-device and property notifications that leave
//! every endpoint as it was are therefore invisible to streams. When the
//! inventory does change, the catalog thread also decides, for every open
//! stream, whether that stream's endpoint is still the selected one
//! ([`StreamWatch`]): a running stream thread only reads two atomics.

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Condvar, Mutex, Weak};
use std::thread::JoinHandle;
use std::time::Duration;

use vinemix_model::config::DeviceBinding;
use vinemix_model::status::Endpoint;
use windows::core::{implement, Interface, GUID, PCWSTR, PWSTR};
use windows::Win32::Foundation::PROPERTYKEY;
use windows::Win32::Media::Audio::{
    eCapture, eRender, EDataFlow, ERole, IAudioClient, IMMDevice, IMMDeviceEnumerator, IMMNotificationClient,
    IMMNotificationClient_Impl, MMDeviceEnumerator, DEVICE_STATE, DEVICE_STATEMASK_ALL, DEVICE_STATE_ACTIVE,
};
use windows::Win32::System::Com::StructuredStorage::PROPVARIANT;
use windows::Win32::System::Com::{CoCreateInstance, CoTaskMemFree, CLSCTX_ALL, STGM_READ};
use windows::Win32::System::Variant::{VT_BLOB, VT_LPWSTR, VT_UI4};
use windows::Win32::UI::Shell::PropertiesSystem::IPropertyStore;

use crate::policy::{endpoint_state, stream_still_selected, transport_family, virtual_name};
use crate::win::{wait_any, Apartment, Event, Wait};

const FRIENDLY_NAME: PROPERTYKEY = PROPERTYKEY {
    fmtid: GUID::from_values(0xa45c254e, 0xdf1c, 0x4efd, [0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0]),
    pid: 14,
};
const DESCRIPTION: PROPERTYKEY = PROPERTYKEY {
    fmtid: GUID::from_values(0xa45c254e, 0xdf1c, 0x4efd, [0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0]),
    pid: 2,
};
const INTERFACE_NAME: PROPERTYKEY = PROPERTYKEY {
    fmtid: GUID::from_values(0x026e516e, 0xb814, 0x414b, [0x83, 0xcd, 0x85, 0x6d, 0x6f, 0xef, 0x48, 0x22]),
    pid: 2,
};
const FORM_FACTOR: PROPERTYKEY = PROPERTYKEY {
    fmtid: GUID::from_values(0x1da5d803, 0xd492, 0x4edd, [0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e]),
    pid: 0,
};
/// `PKEY_AudioEndpoint_StableId` (Mmdeviceapi.h, Windows 11 24H2 SDK
/// 10.0.26100): an opaque `VT_LPWSTR` that Windows tries to keep when it
/// replaces an endpoint ID after a driver or OS update. It is read by key:
/// the property system has no canonical name for it, so a name lookup
/// (`PSGetPropertyKeyFromName("System.AudioEndpoint.StableId")`) fails with
/// TYPE_E_ELEMENTNOTFOUND and left every stable ID empty. Older Windows
/// reports the property as absent, which `string_property` treats as `None`.
const STABLE_ID: PROPERTYKEY = PROPERTYKEY {
    fmtid: GUID::from_values(0x1da5d803, 0xd492, 0x4edd, [0x8c, 0x23, 0xe0, 0xc0, 0xff, 0xee, 0x7f, 0x0e]),
    pid: 12,
};
/// `PKEY_AudioEngine_DeviceFormat`: the shared-mode format chosen in Sound
/// settings, readable without activating an audio client.
const DEVICE_FORMAT: PROPERTYKEY = PROPERTYKEY {
    fmtid: GUID::from_values(0xf19f064d, 0x082c, 0x4e27, [0xbc, 0x73, 0x68, 0x82, 0xa1, 0xbb, 0x8e, 0x4c]),
    pid: 0,
};
/// `EndpointFormFactor_enum_count`.
const FORM_FACTOR_COUNT: u32 = 14;

/// Reads a string property; `None` when absent, failed or empty.
fn string_property(store: &IPropertyStore, key: &PROPERTYKEY) -> Option<String> {
    // SAFETY: GetValue returns an owned PROPVARIANT; the `windows` crate's
    // `Drop for PROPVARIANT` clears it (PropVariantClear).
    unsafe {
        let value: PROPVARIANT = store.GetValue(key).ok()?;
        let inner = &value.Anonymous.Anonymous;
        let text = if inner.vt == VT_LPWSTR && !inner.Anonymous.pwszVal.is_null() {
            inner.Anonymous.pwszVal.to_string().ok()
        } else {
            None
        };
        text.filter(|t| !t.is_empty())
    }
}

/// `PKEY_AudioEndpoint_FormFactor`, rejecting malformed values.
fn form_factor(store: &IPropertyStore) -> Option<i32> {
    // SAFETY: as above.
    unsafe {
        let value: PROPVARIANT = store.GetValue(&FORM_FACTOR).ok()?;
        let inner = &value.Anonymous.Anonymous;
        let result = (inner.vt == VT_UI4 && inner.Anonymous.ulVal < FORM_FACTOR_COUNT).then(|| inner.Anonymous.ulVal as i32);
        result
    }
}

/// `(nSamplesPerSec, nChannels)` from `PKEY_AudioEngine_DeviceFormat`.
fn device_format(store: &IPropertyStore) -> Option<(u32, u16)> {
    // SAFETY: as above; the blob is a WAVEFORMATEX of at least 16 bytes.
    unsafe {
        let value: PROPVARIANT = store.GetValue(&DEVICE_FORMAT).ok()?;
        let inner = &value.Anonymous.Anonymous;
        if inner.vt != VT_BLOB || inner.Anonymous.blob.pBlobData.is_null() || inner.Anonymous.blob.cbSize < 16 {
            return None;
        }
        let bytes = std::slice::from_raw_parts(inner.Anonymous.blob.pBlobData, 16);
        let channels = u16::from_le_bytes([bytes[2], bytes[3]]);
        let rate = u32::from_le_bytes([bytes[4], bytes[5], bytes[6], bytes[7]]);
        Some((rate, channels))
    }
}

/// The mix format last read by activating an endpoint's audio client, keyed
/// by endpoint id, with the device format seen at that time.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct MixFormatProbe {
    device_format: Option<(u32, u16)>,
    active: bool,
    channels: u32,
    rate: u32,
}

/// Per-endpoint mix format cache shared across enumerations.
pub type MixFormatCache = HashMap<String, MixFormatProbe>;

fn device_id(device: &IMMDevice) -> Option<String> {
    // SAFETY: GetId returns a CoTaskMem string we free.
    unsafe {
        let id: PWSTR = device.GetId().ok()?;
        if id.is_null() {
            return None;
        }
        let text = id.to_string().ok();
        CoTaskMemFree(Some(id.0 as *const _));
        text
    }
}

/// One complete inventory, or `None` when Windows failed mid-way (the caller
/// keeps its previous snapshot). Optional metadata failures keep the known
/// values for that exact identity.
///
/// Activating an `IAudioClient` to read the mix format is a round trip into
/// the Windows audio service for every endpoint, and device arrival raises a
/// burst of notifications while that service is busiest. The client is only
/// activated for an endpoint that is new to `formats`, has just become active
/// or whose device format changed; otherwise the cached mix format is kept.
pub fn collect_endpoints(enumerator: &IMMDeviceEnumerator, previous: &[Endpoint], formats: &mut MixFormatCache) -> Option<Vec<Endpoint>> {
    let mut result = Vec::new();
    for (flow, direction) in [(eCapture, "capture"), (eRender, "render")] {
        // SAFETY: COM calls on a live enumerator.
        let devices = unsafe { enumerator.EnumAudioEndpoints(flow, DEVICE_STATE(DEVICE_STATEMASK_ALL)).ok()? };
        // SAFETY: as above.
        let count = unsafe { devices.GetCount().ok()? };
        for index in 0..count {
            // SAFETY: index within count.
            let device = unsafe { devices.Item(index).ok()? };
            let id = device_id(&device)?;
            let known = previous.iter().find(|e| e.id == id && e.direction == direction);
            let mut endpoint = known.cloned().unwrap_or_else(|| Endpoint { id: id.clone(), ..Endpoint::default() });
            endpoint.id = id;
            endpoint.direction = direction.into();
            // SAFETY: COM call on a live device.
            let state: DEVICE_STATE = unsafe { device.GetState().ok()? };
            endpoint.state = endpoint_state(state.0).into();
            if endpoint.transport.is_empty() {
                endpoint.transport = "physical".into();
            }
            let mut current_device_format = None;
            // SAFETY: as above.
            if let Ok(store) = unsafe { device.OpenPropertyStore(STGM_READ) } {
                current_device_format = device_format(&store);
                if let Some(name) = string_property(&store, &FRIENDLY_NAME) {
                    endpoint.name = name;
                }
                if let Some(stable) = string_property(&store, &STABLE_ID) {
                    endpoint.stable_id = stable;
                }
                let description = string_property(&store, &DESCRIPTION);
                let interface = string_property(&store, &INTERFACE_NAME);
                if let Some(text) = &description {
                    endpoint.description = text.clone();
                }
                if let Some(text) = &interface {
                    endpoint.interface_name = text.clone();
                }
                if let Some(factor) = form_factor(&store) {
                    endpoint.form_factor = factor;
                }
                let driver = format!(
                    "{} {}",
                    description.as_deref().unwrap_or_default(),
                    interface.as_deref().unwrap_or_default()
                );
                if (description.is_some() && interface.is_some()) || known.is_none() {
                    let is_virtual = virtual_name(&format!("{driver} {}", endpoint.name));
                    endpoint.transport = transport_family(&driver, is_virtual).into();
                    endpoint.is_virtual = endpoint.transport != "physical";
                }
            }
            if endpoint.name.is_empty() {
                endpoint.name = endpoint.id.clone();
            }
            let active = state.0 & DEVICE_STATE_ACTIVE.0 != 0;
            let cached = formats.get(&endpoint.id).copied();
            let reusable = cached.filter(|probe| {
                active && probe.active && current_device_format.is_some() && probe.device_format == current_device_format
            });
            if let Some(probe) = reusable {
                endpoint.channels = probe.channels;
                endpoint.rate = probe.rate;
            } else if active {
                // SAFETY: activation and format query on a live device.
                let mix = unsafe {
                    device.Activate::<IAudioClient>(CLSCTX_ALL, None).ok().and_then(|client| {
                        let format = client.GetMixFormat().ok()?;
                        if format.is_null() {
                            return None;
                        }
                        let result = (u32::from((*format).nChannels), (*format).nSamplesPerSec);
                        CoTaskMemFree(Some(format as *const _));
                        Some(result)
                    })
                };
                if let Some((channels, rate)) = mix {
                    endpoint.channels = channels;
                    endpoint.rate = rate;
                    formats.insert(endpoint.id.clone(), MixFormatProbe { device_format: current_device_format, active, channels, rate });
                } else {
                    // Retry the activation on the next enumeration.
                    formats.remove(&endpoint.id);
                }
            } else {
                formats.insert(
                    endpoint.id.clone(),
                    MixFormatProbe { device_format: current_device_format, active, channels: endpoint.channels, rate: endpoint.rate },
                );
            }
            result.push(endpoint);
        }
    }
    result.sort_by(|a, b| a.id.cmp(&b.id));
    formats.retain(|id, _| result.iter().any(|e| &e.id == id));
    Some(result)
}

/// What an open stream depends on. The catalog thread re-evaluates it after
/// every inventory change and latches `stale` when the stream must reopen.
#[derive(Debug)]
pub struct StreamWatch {
    selection: DeviceBinding,
    endpoint_id: String,
    channels: u32,
    rate: u32,
    capture: bool,
    microphone: bool,
    required_transport: String,
    stale: AtomicBool,
}

impl StreamWatch {
    pub fn new(selection: DeviceBinding, endpoint: &Endpoint, capture: bool, microphone: bool, required_transport: &str) -> Arc<Self> {
        Arc::new(Self {
            selection,
            endpoint_id: endpoint.id.clone(),
            channels: endpoint.channels,
            rate: endpoint.rate,
            capture,
            microphone,
            required_transport: required_transport.to_owned(),
            stale: AtomicBool::new(false),
        })
    }

    /// Whether the stream's endpoint stopped being the selected one. A single
    /// atomic load: safe on the stream thread at any time.
    #[inline]
    pub fn is_stale(&self) -> bool {
        self.stale.load(Ordering::Acquire)
    }

    fn evaluate(&self, devices: &[Endpoint]) {
        let valid = stream_still_selected(
            devices,
            &self.selection,
            self.capture,
            self.microphone,
            &self.required_transport,
            &self.endpoint_id,
            self.channels,
            self.rate,
        );
        if !valid {
            self.stale.store(true, Ordering::Release);
        }
    }
}

/// The open streams to re-evaluate when the inventory changes.
#[derive(Debug, Default)]
pub struct StreamWatches(Mutex<Vec<Weak<StreamWatch>>>);

impl StreamWatches {
    /// Starts watching, judged immediately against `devices` so a change
    /// between the caller's own resolution and this call is not missed.
    pub fn register(&self, watch: &Arc<StreamWatch>, devices: &[Endpoint]) {
        watch.evaluate(devices);
        let mut watches = self.0.lock().unwrap_or_else(|e| e.into_inner());
        watches.retain(|w| w.strong_count() > 0);
        watches.push(Arc::downgrade(watch));
    }

    /// Re-evaluates every live watch against a new inventory.
    pub fn evaluate(&self, devices: &[Endpoint]) {
        let mut watches = self.0.lock().unwrap_or_else(|e| e.into_inner());
        watches.retain(|watch| match watch.upgrade() {
            Some(watch) => {
                watch.evaluate(devices);
                true
            }
            None => false,
        });
    }

    pub fn len(&self) -> usize {
        self.0.lock().unwrap_or_else(|e| e.into_inner()).iter().filter(|w| w.strong_count() > 0).count()
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

#[implement(IMMNotificationClient)]
struct Notifications {
    wake: Arc<Event>,
}

impl Notifications {
    /// Something may have changed: have the catalog thread look. Whether the
    /// inventory really changed is decided there, not here.
    fn changed(&self) -> windows::core::Result<()> {
        self.wake.set();
        Ok(())
    }
}

impl IMMNotificationClient_Impl for Notifications_Impl {
    fn OnDeviceStateChanged(&self, _: &PCWSTR, _: DEVICE_STATE) -> windows::core::Result<()> {
        self.changed()
    }
    fn OnDeviceAdded(&self, _: &PCWSTR) -> windows::core::Result<()> {
        self.changed()
    }
    fn OnDeviceRemoved(&self, _: &PCWSTR) -> windows::core::Result<()> {
        self.changed()
    }
    fn OnDefaultDeviceChanged(&self, _: EDataFlow, _: ERole, _: &PCWSTR) -> windows::core::Result<()> {
        self.changed()
    }
    fn OnPropertyValueChanged(&self, _: &PCWSTR, _: &PROPERTYKEY) -> windows::core::Result<()> {
        self.changed()
    }
}

#[derive(Debug, Default)]
struct Inventory {
    endpoints: Vec<Endpoint>,
    initialized: bool,
}

#[derive(Debug)]
struct Shared {
    stop: Event,
    wake: Arc<Event>,
    generation: Arc<AtomicU64>,
    /// An explicit retry re-reads every mix format.
    reprobe: std::sync::atomic::AtomicBool,
    inventory: Mutex<Inventory>,
    watches: StreamWatches,
    ready: Condvar,
}

/// Cached endpoint inventory with change notifications.
#[derive(Debug)]
pub struct DeviceCatalog {
    shared: Arc<Shared>,
    thread: Option<JoinHandle<()>>,
}

impl DeviceCatalog {
    /// Starts the catalog thread and waits up to 3 s for a first inventory.
    pub fn new() -> windows::core::Result<Self> {
        let shared = Arc::new(Shared {
            stop: Event::new(true)?,
            wake: Arc::new(Event::new(false)?),
            generation: Arc::new(AtomicU64::new(1)),
            reprobe: std::sync::atomic::AtomicBool::new(false),
            inventory: Mutex::new(Inventory::default()),
            watches: StreamWatches::default(),
            ready: Condvar::new(),
        });
        let worker = shared.clone();
        let thread = std::thread::Builder::new()
            .name("vinemix-device-catalog".into())
            .spawn(move || run(&worker))
            .map_err(|_| windows::core::Error::from(windows::Win32::Foundation::E_FAIL))?;
        {
            let inventory = shared.inventory.lock().unwrap_or_else(|e| e.into_inner());
            let _ = shared
                .ready
                .wait_timeout_while(inventory, Duration::from_secs(3), |inventory| !inventory.initialized);
        }
        Ok(Self { shared, thread: Some(thread) })
    }

    pub fn enumerate(&self) -> Vec<Endpoint> {
        self.shared.inventory.lock().unwrap_or_else(|e| e.into_inner()).endpoints.clone()
    }

    /// Advances when the inventory changed or a refresh was requested.
    pub fn generation(&self) -> u64 {
        self.shared.generation.load(Ordering::Relaxed)
    }

    /// Watches an open stream on `endpoint` for `selection`. Called by the
    /// worker while opening (never from its running loop); the returned
    /// watch turns stale when the catalog thread finds the endpoint is no
    /// longer the selected, unchanged one.
    pub fn watch(&self, selection: DeviceBinding, endpoint: &Endpoint, capture: bool, microphone: bool, required_transport: &str) -> Arc<StreamWatch> {
        let watch = StreamWatch::new(selection, endpoint, capture, microphone, required_transport);
        let inventory = self.shared.inventory.lock().unwrap_or_else(|e| e.into_inner());
        self.shared.watches.register(&watch, &inventory.endpoints);
        watch
    }

    /// Forces a re-enumeration (explicit retry).
    pub fn refresh(&self) {
        self.shared.reprobe.store(true, Ordering::Release);
        self.shared.generation.fetch_add(1, Ordering::Relaxed);
        self.shared.wake.set();
    }
}

impl Drop for DeviceCatalog {
    fn drop(&mut self) {
        self.shared.stop.set();
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

fn run(shared: &Shared) {
    let apartment = Apartment::enter();
    let mut enumerator: Option<IMMDeviceEnumerator> = None;
    let notifications: IMMNotificationClient = Notifications { wake: shared.wake.clone() }.into();
    let mut registered = false;
    let mut formats = MixFormatCache::new();
    loop {
        if shared.stop.wait(0) {
            break;
        }
        if enumerator.is_none() && apartment.ok() {
            // SAFETY: COM initialised on this thread.
            enumerator = unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL).ok() };
        }
        if let Some(enumerator) = &enumerator {
            if !registered {
                // SAFETY: live enumerator and notification object.
                registered = unsafe { enumerator.RegisterEndpointNotificationCallback(&notifications).is_ok() };
            }
        }
        let mut delay = 5000;
        if shared.reprobe.swap(false, Ordering::AcqRel) {
            formats.clear();
        }
        if let Some(enumerator) = &enumerator {
            let previous = shared.inventory.lock().unwrap_or_else(|e| e.into_inner()).endpoints.clone();
            match collect_endpoints(enumerator, &previous, &mut formats) {
                Some(next) => {
                    let mut inventory = shared.inventory.lock().unwrap_or_else(|e| e.into_inner());
                    if inventory.endpoints != next {
                        inventory.endpoints = next;
                        // Open streams learn their verdict before they can
                        // observe the new generation.
                        shared.watches.evaluate(&inventory.endpoints);
                        shared.generation.fetch_add(1, Ordering::Relaxed);
                    }
                }
                // A transient failure is not evidence every endpoint vanished.
                None => delay = 1000,
            }
        }
        {
            let mut inventory = shared.inventory.lock().unwrap_or_else(|e| e.into_inner());
            inventory.initialized = true;
        }
        shared.ready.notify_all();
        if wait_any(&[shared.stop.handle(), shared.wake.handle()], delay) == Wait::Signalled(0) {
            break;
        }
    }
    if registered {
        if let Some(enumerator) = &enumerator {
            // SAFETY: unregistering the object registered above.
            unsafe {
                let _ = enumerator.UnregisterEndpointNotificationCallback(&notifications);
            }
        }
    }
    let _ = notifications.as_raw();
}

#[cfg(test)]
mod tests {
    use super::*;

    fn endpoint(id: &str, state: &str) -> Endpoint {
        Endpoint { id: id.into(), direction: "capture".into(), state: state.into(), channels: 2, rate: 48_000, transport: "physical".into(), ..Endpoint::default() }
    }

    fn binding(id: &str) -> DeviceBinding {
        DeviceBinding { id: id.into(), ..DeviceBinding::default() }
    }

    /// RUST-REV-016: the verdict a running stream reads is computed by
    /// whoever changes the inventory, and only its own endpoint matters.
    #[test]
    fn watches_turn_stale_only_for_their_own_endpoint() {
        let watches = StreamWatches::default();
        let inventory = vec![endpoint("a", "Active"), endpoint("b", "Active")];
        let a = StreamWatch::new(binding("a"), &inventory[0], true, true, "");
        let b = StreamWatch::new(binding("b"), &inventory[1], true, true, "");
        watches.register(&a, &inventory);
        watches.register(&b, &inventory);
        assert!(!a.is_stale() && !b.is_stale());
        // An unrelated arrival and a change to "b" leave "a" running.
        let mut changed = vec![endpoint("a", "Active"), endpoint("b", "Active"), endpoint("c", "Active")];
        changed[1].rate = 44_100;
        watches.evaluate(&changed);
        assert!(!a.is_stale(), "an unrelated change must not reopen this stream");
        assert!(b.is_stale(), "a format change on the stream's own endpoint must");
        // Staleness latches: a later inventory cannot revive a stream that
        // was already told to reopen.
        watches.evaluate(&inventory);
        assert!(b.is_stale());
        // Dropped streams are forgotten.
        drop(b);
        watches.evaluate(&inventory);
        assert_eq!(watches.len(), 1);
        drop(a);
        assert!(watches.is_empty());
    }

    /// Live and read-only (Windows 11 24H2 or later): the inventory carries
    /// Windows' stable ID for every endpoint, and Windows resolves each one
    /// back to the same endpoint, which is what lets a binding follow a
    /// device whose endpoint ID was replaced. Opens no stream.
    /// `cargo test -p vinemix-wasapi -- --ignored live_inventory_reads_stable_ids`
    #[test]
    #[ignore = "live Windows 11 24H2+ audio inventory"]
    fn live_inventory_reads_stable_ids() {
        let _apartment = Apartment::enter();
        // SAFETY: COM is initialised on this thread for the call's duration.
        let enumerator: IMMDeviceEnumerator = unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL) }.expect("enumerator");
        let endpoints = collect_endpoints(&enumerator, &[], &mut MixFormatCache::new()).expect("inventory");
        assert!(!endpoints.is_empty(), "no audio endpoints on this machine");
        for endpoint in &endpoints {
            assert!(!endpoint.stable_id.is_empty(), "{} ({}) has no stable ID", endpoint.name, endpoint.id);
            let wide = crate::win::wide(&endpoint.stable_id);
            // SAFETY: valid NUL-terminated ID on a live enumerator.
            let device = unsafe { enumerator.GetDevice(PCWSTR(wide.as_ptr())) }.expect("Windows resolves the stable ID");
            assert_eq!(device_id(&device).as_deref(), Some(endpoint.id.as_str()), "{}", endpoint.name);
        }
    }

    #[test]
    fn a_watch_registered_after_its_endpoint_changed_is_stale_at_once() {
        let watches = StreamWatches::default();
        let resolved = endpoint("a", "Active");
        // The inventory moved on between resolution and registration.
        let now = vec![endpoint("a", "Unplugged")];
        let watch = StreamWatch::new(binding("a"), &resolved, true, true, "");
        watches.register(&watch, &now);
        assert!(watch.is_stale());
    }
}
