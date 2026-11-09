//! The endpoint catalog: a dedicated COM thread enumerates every capture and
//! render endpoint, caches the result and wakes on device notifications.
//! Notifications only advance a generation; they never touch an audio
//! client. A failed enumeration keeps the last complete inventory.

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Condvar, Mutex};
use std::thread::JoinHandle;
use std::time::Duration;

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
use windows::Win32::UI::Shell::PropertiesSystem::{IPropertyStore, PSGetPropertyKeyFromName};

use crate::policy::{endpoint_state, transport_family, virtual_name};
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
    let mut stable_key = PROPERTYKEY::default();
    let name = crate::win::wide("System.AudioEndpoint.StableId");
    // SAFETY: valid NUL-terminated name and output pointer.
    let has_stable_key = unsafe { PSGetPropertyKeyFromName(PCWSTR(name.as_ptr()), &mut stable_key).is_ok() };
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
                if has_stable_key {
                    if let Some(stable) = string_property(&store, &stable_key) {
                        endpoint.stable_id = stable;
                    }
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

#[implement(IMMNotificationClient)]
struct Notifications {
    generation: Arc<AtomicU64>,
    wake: Arc<Event>,
}

impl Notifications {
    fn changed(&self) -> windows::core::Result<()> {
        self.generation.fetch_add(1, Ordering::Relaxed);
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

    pub fn generation(&self) -> u64 {
        self.shared.generation.load(Ordering::Relaxed)
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
    let notifications: IMMNotificationClient =
        Notifications { generation: shared.generation.clone(), wake: shared.wake.clone() }.into();
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
