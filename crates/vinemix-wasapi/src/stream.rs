//! Opening WASAPI streams (`initializeExclusiveRender`,
//! `initializeSharedStream`, `renderFramesAvailable`).

use windows::core::{Interface, HRESULT};
use windows::Win32::Foundation::{E_INVALIDARG, E_POINTER};
use windows::Win32::Media::Audio::{
    AudioCategory_Media, AudioCategory_Other, AudioClientProperties, IAudioClient, IAudioClient2, IAudioClient3, IMMDevice,
    AUDCLNT_E_BUFFER_ERROR, AUDCLNT_E_BUFFER_SIZE_ERROR, AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED, AUDCLNT_E_INVALID_DEVICE_PERIOD,
    AUDCLNT_E_UNSUPPORTED_FORMAT, AUDCLNT_SHAREMODE_EXCLUSIVE, AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
    AUDCLNT_STREAMFLAGS_NOPERSIST, WAVEFORMATEX,
};
use windows::Win32::System::Com::{CoTaskMemFree, CLSCTX_ALL};

use crate::format::{
    SampleFormat, WaveDescription, EXTENSIBLE_EXTRA, SPEAKER_FRONT_LEFT, SPEAKER_FRONT_RIGHT, SUBTYPE_TAIL, WAVE_FORMAT_EXTENSIBLE,
    WAVE_FORMAT_IEEE_FLOAT, WAVE_FORMAT_PCM,
};

/// An owned copy of a `WAVEFORMATEX` (and its extensible tail).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FormatBytes(Vec<u8>);

impl FormatBytes {
    /// Copies and frees a CoTaskMem format.
    ///
    /// # Safety
    /// `format` must be a valid CoTaskMem-allocated `WAVEFORMATEX`.
    pub unsafe fn take(format: *mut WAVEFORMATEX) -> Self {
        // SAFETY: the caller guarantees a valid header; cbSize bounds the tail.
        unsafe {
            let extra = usize::from((*format).cbSize);
            let length = std::mem::size_of::<WAVEFORMATEX>() + extra;
            let bytes = std::slice::from_raw_parts(format.cast::<u8>(), length).to_vec();
            CoTaskMemFree(Some(format as *const _));
            Self(bytes)
        }
    }

    pub fn from_description(wave: &WaveDescription) -> Self {
        let mut bytes = Vec::with_capacity(40);
        let bits = wave.bits_per_sample;
        bytes.extend_from_slice(&wave.format_tag.to_le_bytes());
        bytes.extend_from_slice(&wave.channels.to_le_bytes());
        bytes.extend_from_slice(&wave.samples_per_sec.to_le_bytes());
        let average = wave.samples_per_sec * u32::from(wave.block_align);
        bytes.extend_from_slice(&average.to_le_bytes());
        bytes.extend_from_slice(&wave.block_align.to_le_bytes());
        bytes.extend_from_slice(&bits.to_le_bytes());
        bytes.extend_from_slice(&wave.cb_size.to_le_bytes());
        if wave.cb_size >= EXTENSIBLE_EXTRA {
            bytes.extend_from_slice(&wave.valid_bits_per_sample.to_le_bytes());
            bytes.extend_from_slice(&wave.channel_mask.to_le_bytes());
            bytes.extend_from_slice(&wave.sub_format.unwrap_or(0).to_le_bytes());
            bytes.extend_from_slice(&0u16.to_le_bytes());
            bytes.extend_from_slice(&0x0010u16.to_le_bytes());
            bytes.extend_from_slice(&SUBTYPE_TAIL);
        }
        Self(bytes)
    }

    pub fn as_ptr(&self) -> *const WAVEFORMATEX {
        self.0.as_ptr().cast()
    }

    fn u16_at(&self, at: usize) -> u16 {
        self.0.get(at..at + 2).map_or(0, |b| u16::from_le_bytes([b[0], b[1]]))
    }

    fn u32_at(&self, at: usize) -> u32 {
        self.0.get(at..at + 4).map_or(0, |b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
    }

    pub fn description(&self) -> WaveDescription {
        let cb_size = self.u16_at(16);
        let extended = cb_size >= EXTENSIBLE_EXTRA && self.0.len() >= 40;
        let sub_format = extended
            .then(|| {
                let tail_matches = self.u16_at(28) == 0 && self.u16_at(30) == 0x0010 && self.0[32..40] == SUBTYPE_TAIL;
                tail_matches.then(|| self.u32_at(24))
            })
            .flatten();
        WaveDescription {
            format_tag: self.u16_at(0),
            channels: self.u16_at(2),
            samples_per_sec: self.u32_at(4),
            block_align: self.u16_at(12),
            bits_per_sample: self.u16_at(14),
            cb_size,
            valid_bits_per_sample: if extended { self.u16_at(18) } else { 0 },
            channel_mask: if extended { self.u32_at(20) } else { 0 },
            sub_format,
        }
    }
}

fn activate(device: &IMMDevice) -> windows::core::Result<IAudioClient> {
    // SAFETY: COM activation on a live device.
    unsafe { device.Activate::<IAudioClient>(CLSCTX_ALL, None) }
}

/// Opens an event-driven exclusive render stream for a VAC Lite endpoint.
/// A failed exclusive open never falls back to shared rendering: that would
/// silently reintroduce screen-share capture of the virtual microphone.
pub fn initialize_exclusive_render(device: &IMMDevice) -> Result<(IAudioClient, FormatBytes), HRESULT> {
    let mut client = activate(device).map_err(|e| e.code())?;
    let mut selected: Option<FormatBytes> = None;
    let consider = |client: &IAudioClient, candidate: &FormatBytes, selected: &mut Option<FormatBytes>| -> HRESULT {
        let description = candidate.description();
        if SampleFormat::new(&description).is_none() || description.cb_size > EXTENSIBLE_EXTRA {
            return AUDCLNT_E_UNSUPPORTED_FORMAT;
        }
        // The converter writes full-width PCM: no padding-bit containers.
        if description.format_tag == WAVE_FORMAT_EXTENSIBLE && description.valid_bits_per_sample != description.bits_per_sample {
            return AUDCLNT_E_UNSUPPORTED_FORMAT;
        }
        // SAFETY: valid format pointer for the duration of the call.
        let answer = unsafe { client.IsFormatSupported(AUDCLNT_SHAREMODE_EXCLUSIVE, candidate.as_ptr(), None) };
        if answer == HRESULT(0) {
            *selected = Some(candidate.clone());
        }
        answer
    };
    let mut result = HRESULT(0);
    'search: for rate in [48_000u32, 44_100, 96_000] {
        for tag in [WAVE_FORMAT_PCM, WAVE_FORMAT_IEEE_FLOAT] {
            for bits in [16u16, 32] {
                if tag == WAVE_FORMAT_IEEE_FLOAT && bits != 32 {
                    continue;
                }
                for extended in [false, true] {
                    let candidate = FormatBytes::from_description(&WaveDescription {
                        format_tag: if extended { WAVE_FORMAT_EXTENSIBLE } else { tag },
                        channels: 2,
                        samples_per_sec: rate,
                        block_align: 2 * bits / 8,
                        bits_per_sample: bits,
                        cb_size: if extended { EXTENSIBLE_EXTRA } else { 0 },
                        valid_bits_per_sample: bits,
                        channel_mask: SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT,
                        sub_format: Some(u32::from(tag)),
                    });
                    result = consider(&client, &candidate, &mut selected);
                    if selected.is_some() {
                        break 'search;
                    }
                    if result.is_err() && result != AUDCLNT_E_UNSUPPORTED_FORMAT && result != E_INVALIDARG {
                        return Err(result);
                    }
                }
            }
        }
    }
    if selected.is_none() {
        // SAFETY: COM call; the returned format is freed by FormatBytes::take.
        match unsafe { client.GetMixFormat() } {
            Ok(mix) if !mix.is_null() => {
                // SAFETY: valid CoTaskMem format.
                let mix = unsafe { FormatBytes::take(mix) };
                result = consider(&client, &mix, &mut selected);
            }
            Ok(_) => {}
            Err(error) => result = error.code(),
        }
        if selected.is_none() {
            return Err(if result.is_err() { result } else { AUDCLNT_E_UNSUPPORTED_FORMAT });
        }
    }
    let selected = selected.expect("checked above");
    let (mut default_period, mut minimum_period) = (0i64, 0i64);
    // SAFETY: valid output pointers.
    unsafe { client.GetDevicePeriod(Some(&mut default_period), Some(&mut minimum_period)) }.map_err(|e| e.code())?;
    let mut period = default_period.max(minimum_period);
    if period <= 0 || period > 10_000_000 {
        return Err(AUDCLNT_E_INVALID_DEVICE_PERIOD);
    }
    let flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
    // SAFETY: valid format pointer.
    let mut initialized =
        unsafe { client.Initialize(AUDCLNT_SHAREMODE_EXCLUSIVE, flags, period, period, selected.as_ptr(), None) };
    if let Err(error) = &initialized {
        if error.code() == AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED {
            // SAFETY: COM call on the client that reported the aligned size.
            let aligned = unsafe { client.GetBufferSize() }.map_err(|e| e.code())?;
            let rate = selected.description().samples_per_sec;
            if aligned == 0 || aligned > rate {
                return Err(AUDCLNT_E_BUFFER_SIZE_ERROR);
            }
            period = ((10_000_000u64 * u64::from(aligned) + u64::from(rate / 2)) / u64::from(rate)) as i64;
            client = activate(device).map_err(|e| e.code())?;
            // SAFETY: valid format pointer.
            initialized =
                unsafe { client.Initialize(AUDCLNT_SHAREMODE_EXCLUSIVE, flags, period, period, selected.as_ptr(), None) };
        }
    }
    initialized.map_err(|e| e.code())?;
    Ok((client, selected))
}

/// An opened shared-mode stream.
#[derive(Debug)]
pub struct SharedStream {
    pub client: IAudioClient,
    pub format: FormatBytes,
    /// Device-rate frames per period (0 when a legacy period is unknown).
    pub period_frames: u32,
}

/// Opens an event-driven shared stream at the shortest legal period
/// (IAudioClient3), retrying on a fresh client at the default period and
/// finally with legacy shared I/O. The mix format and processing category
/// stay unchanged; the period changes scheduling, not quality.
pub fn initialize_shared_stream(device: &IMMDevice, capture: bool, prefer_low_latency: bool) -> Result<SharedStream, HRESULT> {
    #[derive(PartialEq)]
    enum Attempt {
        Shortest,
        Normal,
        Legacy,
    }
    let mut attempt = if prefer_low_latency { Attempt::Shortest } else { Attempt::Normal };
    let mut failed_period = 0u32;
    loop {
        let client = activate(device).map_err(|e| e.code())?;
        if let Ok(client2) = client.cast::<IAudioClient2>() {
            let properties = AudioClientProperties {
                cbSize: std::mem::size_of::<AudioClientProperties>() as u32,
                eCategory: if capture { AudioCategory_Other } else { AudioCategory_Media },
                ..AudioClientProperties::default()
            };
            // SAFETY: valid properties pointer.
            unsafe {
                let _ = client2.SetClientProperties(&properties);
            }
        }
        // SAFETY: the returned format is freed by FormatBytes::take.
        let mix = unsafe { client.GetMixFormat() }.map_err(|e| e.code())?;
        if mix.is_null() {
            return Err(AUDCLNT_E_UNSUPPORTED_FORMAT);
        }
        // SAFETY: valid CoTaskMem format.
        let wave = unsafe { FormatBytes::take(mix) };
        if wave.description().samples_per_sec == 0 {
            return Err(AUDCLNT_E_UNSUPPORTED_FORMAT);
        }
        let mut selected = 0u32;
        if attempt != Attempt::Legacy {
            if let Ok(client3) = client.cast::<IAudioClient3>() {
                let (mut normal, mut fundamental, mut minimum, mut maximum) = (0u32, 0u32, 0u32, 0u32);
                // SAFETY: valid pointers.
                let queried = unsafe {
                    client3.GetSharedModeEnginePeriod(wave.as_ptr(), &mut normal, &mut fundamental, &mut minimum, &mut maximum)
                };
                if queried.is_ok() && fundamental != 0 && minimum != 0 && minimum <= maximum {
                    let shortest = (u64::from(minimum) + u64::from(fundamental) - 1) / u64::from(fundamental) * u64::from(fundamental);
                    if shortest <= u64::from(maximum) {
                        if attempt == Attempt::Shortest {
                            selected = shortest as u32;
                        } else if normal >= minimum && normal <= maximum && normal % fundamental == 0 && normal != failed_period {
                            selected = normal;
                        }
                    }
                }
                if selected != 0 {
                    // SAFETY: valid format pointer.
                    let result = unsafe {
                        client3.InitializeSharedAudioStream(AUDCLNT_STREAMFLAGS_EVENTCALLBACK, selected, wave.as_ptr(), None)
                    };
                    drop(client3);
                    if result.is_ok() {
                        return Ok(SharedStream { client, format: wave, period_frames: selected });
                    }
                    // Some drivers poison a client even when Initialize fails:
                    // retry on a fresh activation.
                    failed_period = selected;
                    attempt = if attempt == Attempt::Shortest { Attempt::Normal } else { Attempt::Legacy };
                    continue;
                }
            }
        }
        // SAFETY: valid format pointer.
        unsafe {
            client.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_NOPERSIST,
                0,
                0,
                wave.as_ptr(),
                None,
            )
        }
        .map_err(|e| e.code())?;
        let (mut normal, mut minimum) = (0i64, 0i64);
        let mut period_frames = 0;
        // SAFETY: valid output pointers.
        if unsafe { client.GetDevicePeriod(Some(&mut normal), Some(&mut minimum)) }.is_ok() && normal > 0 && normal <= 10_000_000 {
            let rate = u64::from(wave.description().samples_per_sec);
            period_frames = ((normal as u64 * rate + 9_999_999) / 10_000_000) as u32;
        }
        return Ok(SharedStream { client, format: wave, period_frames });
    }
}

/// Frames writable now. Event-driven exclusive rendering always services
/// the whole buffer and must not use the shared-mode padding calculation.
pub fn render_frames_available(client: &IAudioClient, exclusive: bool, buffer_frames: u32) -> Result<u32, HRESULT> {
    if exclusive {
        return Ok(buffer_frames);
    }
    // SAFETY: COM call on a live client.
    let padding = unsafe { client.GetCurrentPadding() }.map_err(|e| e.code())?;
    if padding > buffer_frames {
        return Err(AUDCLNT_E_BUFFER_ERROR);
    }
    Ok(buffer_frames - padding)
}

pub const POINTER_ERROR: HRESULT = E_POINTER;
