//! Device sample formats: decoding captured packets to stereo frames and
//! encoding stereo frames for rendering (`SampleFormat` in
//! `windows_audio.cpp`). Platform-free so it can be unit tested anywhere.
//!
//! Decoding never relies on the alignment of a driver's packed samples.

use vinemix_rt::Frame;

pub const WAVE_FORMAT_PCM: u16 = 1;
pub const WAVE_FORMAT_IEEE_FLOAT: u16 = 3;
pub const WAVE_FORMAT_EXTENSIBLE: u16 = 0xFFFE;

pub const SPEAKER_FRONT_LEFT: u32 = 0x1;
pub const SPEAKER_FRONT_RIGHT: u32 = 0x2;
pub const SPEAKER_FRONT_CENTER: u32 = 0x4;
pub const SPEAKER_LOW_FREQUENCY: u32 = 0x8;
pub const SPEAKER_BACK_LEFT: u32 = 0x10;
pub const SPEAKER_BACK_RIGHT: u32 = 0x20;
pub const SPEAKER_FRONT_LEFT_OF_CENTER: u32 = 0x40;
pub const SPEAKER_FRONT_RIGHT_OF_CENTER: u32 = 0x80;
pub const SPEAKER_SIDE_LEFT: u32 = 0x200;
pub const SPEAKER_SIDE_RIGHT: u32 = 0x400;

/// `KSDATAFORMAT_SUBTYPE_PCM` / `_IEEE_FLOAT` share this GUID tail.
pub const SUBTYPE_TAIL: [u8; 8] = [0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71];

/// The fields of a `WAVEFORMATEX(TENSIBLE)` the converter needs.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct WaveDescription {
    pub format_tag: u16,
    pub channels: u16,
    pub samples_per_sec: u32,
    pub block_align: u16,
    pub bits_per_sample: u16,
    pub cb_size: u16,
    /// Extensible formats only.
    pub valid_bits_per_sample: u16,
    pub channel_mask: u32,
    /// `SubFormat.Data1` when the GUID tail matches the KS audio subtypes,
    /// otherwise `None` (an unsupported subtype).
    pub sub_format: Option<u32>,
}

/// Extensible-format extra bytes (`sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX)`).
pub const EXTENSIBLE_EXTRA: u16 = 22;

#[derive(Clone, Debug, PartialEq)]
pub struct SampleFormat {
    pub rate: u32,
    pub channels: u16,
    pub bytes: u16,
    pub block_align: u16,
    pub channel_mask: u32,
    pub floating: bool,
    left: [f32; 64],
    right: [f32; 64],
}

impl SampleFormat {
    /// Accepts the formats the converter can read and write.
    pub fn new(wave: &WaveDescription) -> Option<Self> {
        let mut tag = wave.format_tag;
        let mut channel_mask = 0;
        if tag == WAVE_FORMAT_EXTENSIBLE {
            if wave.cb_size < EXTENSIBLE_EXTRA {
                return None;
            }
            channel_mask = wave.channel_mask;
            tag = match wave.sub_format {
                Some(1) => WAVE_FORMAT_PCM,
                Some(3) => WAVE_FORMAT_IEEE_FLOAT,
                _ => return None,
            };
        }
        let floating = tag == WAVE_FORMAT_IEEE_FLOAT;
        let channels = wave.channels;
        let bytes = wave.bits_per_sample / 8;
        let valid_size = if floating { bytes == 4 || bytes == 8 } else { matches!(bytes, 1..=4) };
        if (!floating && tag != WAVE_FORMAT_PCM)
            || channels == 0
            || channels > 64
            || !(8000..=384_000).contains(&wave.samples_per_sec)
            || wave.bits_per_sample % 8 != 0
            || u32::from(wave.block_align) < u32::from(channels) * u32::from(bytes)
            || !valid_size
        {
            return None;
        }
        if channel_mask != 0 && channel_mask.count_ones() != u32::from(channels) {
            channel_mask = 0;
        }
        let mut format = Self {
            rate: wave.samples_per_sec,
            channels,
            bytes,
            block_align: wave.block_align,
            channel_mask,
            floating,
            left: [0.0; 64],
            right: [0.0; 64],
        };
        if channels == 1 {
            format.left[0] = 1.0;
            format.right[0] = 1.0;
        } else if channel_mask == 0 {
            format.left[0] = 1.0;
            format.right[1] = 1.0;
        } else {
            let mut index = 0usize;
            for bit in 0..32 {
                if index >= usize::from(channels) {
                    break;
                }
                let speaker = 1u32 << bit;
                if channel_mask & speaker == 0 {
                    continue;
                }
                match speaker {
                    SPEAKER_FRONT_LEFT => format.left[index] = 1.0,
                    SPEAKER_FRONT_RIGHT => format.right[index] = 1.0,
                    SPEAKER_FRONT_CENTER => {
                        format.left[index] = 0.707_106_78;
                        format.right[index] = 0.707_106_78;
                    }
                    SPEAKER_LOW_FREQUENCY => {
                        format.left[index] = 0.25;
                        format.right[index] = 0.25;
                    }
                    SPEAKER_BACK_LEFT | SPEAKER_SIDE_LEFT | SPEAKER_FRONT_LEFT_OF_CENTER => {
                        format.left[index] = 0.707_106_78;
                    }
                    SPEAKER_BACK_RIGHT | SPEAKER_SIDE_RIGHT | SPEAKER_FRONT_RIGHT_OF_CENTER => {
                        format.right[index] = 0.707_106_78;
                    }
                    _ => {
                        format.left[index] = 0.5;
                        format.right[index] = 0.5;
                    }
                }
                index += 1;
            }
        }
        Some(format)
    }

    fn read(&self, source: &[u8]) -> f32 {
        if self.floating {
            let value = if self.bytes == 4 {
                f64::from(f32::from_le_bytes(source[..4].try_into().expect("4 bytes")))
            } else {
                f64::from_le_bytes(source[..8].try_into().expect("8 bytes"))
            };
            return if value.is_finite() { value.clamp(-32.0, 32.0) as f32 } else { 0.0 };
        }
        match self.bytes {
            1 => (i32::from(source[0]) - 128) as f32 / 128.0,
            2 => f32::from(i16::from_le_bytes([source[0], source[1]])) / 32768.0,
            3 => {
                let mut value = i32::from(source[0]) | (i32::from(source[1]) << 8) | (i32::from(source[2]) << 16);
                if value & 0x80_0000 != 0 {
                    value |= 0xff00_0000u32 as i32;
                }
                value as f32 / 8_388_608.0
            }
            _ => (f64::from(i32::from_le_bytes(source[..4].try_into().expect("4 bytes"))) / 2_147_483_648.0) as f32,
        }
    }

    /// Decodes `frames.len()` frames. `channel >= 0` selects one device
    /// channel for both sides; `silent` (or no data) yields silence.
    pub fn decode(&self, data: Option<&[u8]>, frames: &mut [Frame], channel: i32, silent: bool) {
        let Some(data) = data.filter(|_| !silent) else {
            frames.fill(Frame::SILENT);
            return;
        };
        let block = usize::from(self.block_align);
        let bytes = usize::from(self.bytes);
        for (n, frame) in frames.iter_mut().enumerate() {
            let base = &data[n * block..];
            if channel >= 0 {
                let value = self.read(&base[channel as usize * bytes..]);
                *frame = Frame::new(value, value);
            } else {
                *frame = Frame::SILENT;
                for c in 0..usize::from(self.channels) {
                    let value = self.read(&base[c * bytes..]);
                    frame.left += value * self.left[c];
                    frame.right += value * self.right[c];
                }
            }
        }
    }

    fn write(&self, destination: &mut [u8], value: f32) {
        let value = if value.is_finite() { value.clamp(-1.0, 1.0) } else { 0.0 };
        if self.floating {
            if self.bytes == 4 {
                destination[..4].copy_from_slice(&value.to_le_bytes());
            } else {
                destination[..8].copy_from_slice(&f64::from(value).to_le_bytes());
            }
            return;
        }
        match self.bytes {
            1 => destination[0] = ((value * 128.0 + 128.0).round() as i64).clamp(0, 255) as u8,
            2 => {
                let sample = ((value * 32768.0).round() as i64).clamp(-32768, 32767) as i16;
                destination[..2].copy_from_slice(&sample.to_le_bytes());
            }
            3 => {
                let sample = ((f64::from(value) * 8_388_608.0).round() as i64).clamp(-8_388_608, 8_388_607) as i32;
                let bits = sample as u32;
                destination[0] = bits as u8;
                destination[1] = (bits >> 8) as u8;
                destination[2] = (bits >> 16) as u8;
            }
            _ => {
                let sample =
                    ((f64::from(value) * 2_147_483_648.0).round() as i64).clamp(-2_147_483_648, 2_147_483_647) as i32;
                destination[..4].copy_from_slice(&sample.to_le_bytes());
            }
        }
    }

    /// Encodes stereo frames: front left/right only (or the centre speaker
    /// when the layout has no front pair); mono devices receive the average.
    pub fn encode(&self, frames: &[Frame], destination: &mut [u8]) {
        let block = usize::from(self.block_align);
        let bytes = usize::from(self.bytes);
        for (n, frame) in frames.iter().enumerate() {
            let base = &mut destination[n * block..];
            if self.channels == 1 {
                self.write(base, (frame.left + frame.right) * 0.5);
                continue;
            }
            for c in 0..usize::from(self.channels) {
                let mut sample = 0.0f32;
                if self.channel_mask == 0 {
                    if c == 0 {
                        sample = frame.left;
                    }
                    if c == 1 {
                        sample = frame.right;
                    }
                } else {
                    let mut mask = self.channel_mask;
                    let mut bit = 0u32;
                    let mut i = 0;
                    while i <= c && mask != 0 {
                        bit = mask.trailing_zeros();
                        mask &= mask - 1;
                        i += 1;
                    }
                    let speaker = 1u32 << bit;
                    if speaker == SPEAKER_FRONT_LEFT {
                        sample = frame.left;
                    }
                    if speaker == SPEAKER_FRONT_RIGHT {
                        sample = frame.right;
                    }
                    if self.channel_mask & (SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT) == 0 && speaker == SPEAKER_FRONT_CENTER {
                        sample = (frame.left + frame.right) * 0.5;
                    }
                }
                self.write(&mut base[c * bytes..], sample);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn pcm(channels: u16, bits: u16) -> WaveDescription {
        WaveDescription {
            format_tag: WAVE_FORMAT_PCM,
            channels,
            samples_per_sec: 48_000,
            block_align: channels * bits / 8,
            bits_per_sample: bits,
            ..WaveDescription::default()
        }
    }

    #[test]
    fn pcm_round_trips_every_width() {
        for bits in [8u16, 16, 24, 32] {
            let format = SampleFormat::new(&pcm(2, bits)).unwrap();
            let frames = [Frame::new(0.5, -0.25), Frame::new(-1.0, 0.999)];
            let mut bytes = vec![0u8; 2 * usize::from(format.block_align)];
            format.encode(&frames, &mut bytes);
            let mut back = [Frame::SILENT; 2];
            format.decode(Some(&bytes), &mut back, -1, false);
            let tolerance = 2.0 / f32::powi(2.0, i32::from(bits) - 1);
            for (a, b) in back.iter().zip(&frames) {
                assert!((a.left - b.left).abs() <= tolerance && (a.right - b.right).abs() <= tolerance, "{bits}");
            }
        }
    }

    #[test]
    fn surround_downmix_and_front_only_render() {
        let mask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT | SPEAKER_FRONT_CENTER | SPEAKER_LOW_FREQUENCY;
        let wave = WaveDescription {
            format_tag: WAVE_FORMAT_EXTENSIBLE,
            channels: 4,
            samples_per_sec: 48_000,
            block_align: 16,
            bits_per_sample: 32,
            cb_size: EXTENSIBLE_EXTRA,
            valid_bits_per_sample: 32,
            channel_mask: mask,
            sub_format: Some(3),
        };
        let format = SampleFormat::new(&wave).unwrap();
        let mut bytes = vec![0u8; 16];
        format.encode(&[Frame::new(0.5, -0.5)], &mut bytes);
        let values: Vec<f32> = bytes.chunks(4).map(|c| f32::from_le_bytes(c.try_into().unwrap())).collect();
        assert_eq!(values, vec![0.5, -0.5, 0.0, 0.0], "speech never reaches centre or LFE");
        let mut frame = [Frame::SILENT];
        format.decode(Some(&bytes), &mut frame, -1, false);
        assert_eq!(frame[0], Frame::new(0.5, -0.5));
        let mut one = [Frame::SILENT];
        format.decode(Some(&bytes), &mut one, 1, false);
        assert_eq!(one[0], Frame::new(-0.5, -0.5), "a selected channel feeds both sides");
    }

    #[test]
    fn rejects_unsupported_layouts() {
        let mut bad = pcm(2, 12);
        assert!(SampleFormat::new(&bad).is_none());
        bad = pcm(0, 16);
        assert!(SampleFormat::new(&bad).is_none());
        let mut extensible = pcm(2, 16);
        extensible.format_tag = WAVE_FORMAT_EXTENSIBLE;
        extensible.cb_size = EXTENSIBLE_EXTRA;
        extensible.sub_format = None;
        assert!(SampleFormat::new(&extensible).is_none());
    }
}
