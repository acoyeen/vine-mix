//! Drift-corrected stereo sample-rate conversion for device streams.
//!
//! [`SincConverter`] is a Rust port of libsamplerate 0.2.2's band-limited
//! sinc converter (`src_sinc.c`, `SRC_SINC_FASTEST`, stereo path) and the
//! parts of `samplerate.c` the engine calls. Erik de Castro Lopo's library
//! is 2-clause BSD (see `licenses/libsamplerate-COPYING.txt`). The port keeps
//! its fixed-point filter indexing and float/double promotions, so it is
//! sample-for-sample identical to the C library (see the oracle tests).
//!
//! [`AdaptiveResampler`] is the 0.5 engine's adapter (`dsp.cpp`): it owns the
//! pending input, applies a bounded drift correction, and never allocates
//! after construction.

#[rustfmt::skip]
mod fastest_coeffs;

use vinemix_rt::Frame;

const SHIFT_BITS: u32 = 12;
const FP_ONE: f64 = (1i32 << SHIFT_BITS) as f64;
const INV_FP_ONE: f64 = 1.0 / FP_ONE;
const SRC_MAX_RATIO: f64 = 256.0;
const CHANNELS: usize = 2;
/// Bounded drift correction applied by the adapter (+/- 2000 ppm).
pub const MAXIMUM_DRIFT: f64 = 0.002;

#[inline]
fn lrint(x: f64) -> i64 {
    x.round_ties_even() as i64
}

#[inline]
fn double_to_fp(x: f64) -> i32 {
    lrint(x * FP_ONE) as i32
}

#[inline]
fn int_to_fp(x: i32) -> i32 {
    x << SHIFT_BITS
}

#[inline]
fn fp_to_int(x: i32) -> i32 {
    x >> SHIFT_BITS
}

#[inline]
fn fp_to_double(x: i32) -> f64 {
    f64::from(x & ((1 << SHIFT_BITS) - 1)) * INV_FP_ONE
}

#[inline]
fn int_div_ceil(dividend: i32, divisor: i32) -> i32 {
    (dividend + (divisor - 1)) / divisor
}

#[inline]
fn fmod_one(x: f64) -> f64 {
    let res = x - lrint(x) as f64;
    if res < 0.0 { res + 1.0 } else { res }
}

/// Whether libsamplerate accepts `ratio` (`src_is_valid_ratio`).
pub fn is_valid_ratio(ratio: f64) -> bool {
    (1.0 / SRC_MAX_RATIO..=SRC_MAX_RATIO).contains(&ratio)
}

/// libsamplerate error codes the converter can report.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SrcError {
    BadSrcRatio = 6,
    BadInternalState = 22,
    SincPrepareDataBadLen = 24,
}

/// The result of one [`SincConverter::process`] call.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Processed {
    pub input_frames_used: usize,
    pub output_frames_generated: usize,
}

/// libsamplerate's fastest sinc converter, two channels.
#[derive(Clone, Debug, PartialEq)]
pub struct SincConverter {
    coeffs: &'static [f32],
    coeff_half_len: i32,
    index_inc: i32,
    in_count: i64,
    in_used: i64,
    out_count: i64,
    out_gen: i64,
    b_current: i32,
    b_end: i32,
    b_real_end: i32,
    b_len: i32,
    buffer: Vec<f32>,
    last_ratio: f64,
    last_position: f64,
}

impl Default for SincConverter {
    fn default() -> Self {
        Self::new()
    }
}

impl SincConverter {
    /// `src_new(SRC_SINC_FASTEST, 2)`.
    pub fn new() -> Self {
        let coeffs: &'static [f32] = &fastest_coeffs::COEFFS;
        let coeff_half_len = coeffs.len() as i32 - 2;
        let index_inc = fastest_coeffs::INCREMENT;
        let mut b_len = 3 * lrint((f64::from(coeff_half_len) + 2.0) / f64::from(index_inc) * SRC_MAX_RATIO + 1.0) as i32;
        b_len = b_len.max(4096);
        b_len *= CHANNELS as i32;
        // A `<=` check against samples in hand needs one spare sample.
        b_len += 1;
        let mut converter = Self {
            coeffs,
            coeff_half_len,
            index_inc,
            in_count: 0,
            in_used: 0,
            out_count: 0,
            out_gen: 0,
            b_current: 0,
            b_end: 0,
            b_real_end: -1,
            b_len,
            buffer: vec![0.0; b_len as usize + CHANNELS],
            last_ratio: 0.0,
            last_position: 0.0,
        };
        converter.reset();
        converter
    }

    /// `src_reset`: clears history without allocating.
    pub fn reset(&mut self) {
        self.b_current = 0;
        self.b_end = 0;
        self.b_real_end = -1;
        self.buffer[..self.b_len as usize].fill(0.0);
        // libsamplerate's sanity marker past the end of the buffer.
        for value in &mut self.buffer[self.b_len as usize..] {
            *value = f32::from_bits(0xAAAA_AAAA);
        }
        self.last_position = 0.0;
        self.last_ratio = 0.0;
    }

    /// `src_set_ratio`.
    pub fn set_ratio(&mut self, ratio: f64) -> Result<(), SrcError> {
        if !is_valid_ratio(ratio) {
            return Err(SrcError::BadSrcRatio);
        }
        self.last_ratio = ratio;
        Ok(())
    }

    fn prepare_data(&mut self, input: &[f32], half_filter_chan_len: i32) -> Result<(), SrcError> {
        let channels = CHANNELS as i32;
        if self.b_real_end >= 0 {
            return Ok(());
        }
        let mut len;
        if self.b_current == 0 {
            // Initial state: zeros at the start of the buffer, then new data.
            len = self.b_len - 2 * half_filter_chan_len;
            self.b_current = half_filter_chan_len;
            self.b_end = half_filter_chan_len;
        } else if self.b_end + half_filter_chan_len + channels < self.b_len {
            len = (self.b_len - self.b_current - half_filter_chan_len).max(0);
        } else {
            // Move the data at the end of the buffer back to the start.
            len = self.b_end - self.b_current;
            let from = (self.b_current - half_filter_chan_len) as usize;
            let count = (half_filter_chan_len + len) as usize;
            self.buffer.copy_within(from..from + count, 0);
            self.b_current = half_filter_chan_len;
            self.b_end = self.b_current + len;
            len = (self.b_len - self.b_current - half_filter_chan_len).max(0);
        }
        len = ((self.in_count - self.in_used) as i32).min(len);
        len -= len % channels;
        if len < 0 || self.b_end + len > self.b_len {
            return Err(SrcError::SincPrepareDataBadLen);
        }
        let start = self.in_used as usize;
        self.buffer[self.b_end as usize..(self.b_end + len) as usize].copy_from_slice(&input[start..start + len as usize]);
        self.b_end += len;
        self.in_used += i64::from(len);
        // The engine streams continuously (end_of_input is always zero), so
        // the end-of-stream padding branch never runs.
        Ok(())
    }

    fn calc_output_stereo(&self, increment: i32, start_filter_index: i32, scale: f64, output: &mut [f32; 2]) {
        let channels = CHANNELS as i32;
        let max_filter_index = int_to_fp(self.coeff_half_len);
        let coeff = |filter_index: i32| {
            let fraction = fp_to_double(filter_index);
            let index = fp_to_int(filter_index) as usize;
            // The coefficient difference is single precision, as in C.
            f64::from(self.coeffs[index]) + fraction * f64::from(self.coeffs[index + 1] - self.coeffs[index])
        };
        // Left half of the filter.
        let mut filter_index = start_filter_index;
        let mut coeff_count = (max_filter_index - filter_index) / increment;
        filter_index += coeff_count * increment;
        let mut data_index = self.b_current - channels * coeff_count;
        if data_index < 0 {
            // Avoid underflow access to the buffer.
            let steps = int_div_ceil(-data_index, 2);
            filter_index -= increment * steps;
            data_index += steps * 2;
        }
        let mut left = [0.0f64; 2];
        while filter_index >= 0 {
            let icoeff = coeff(filter_index);
            for (ch, value) in left.iter_mut().enumerate() {
                *value += icoeff * f64::from(self.buffer[data_index as usize + ch]);
            }
            filter_index -= increment;
            data_index += 2;
        }
        // Right half of the filter.
        filter_index = increment - start_filter_index;
        coeff_count = (max_filter_index - filter_index) / increment;
        filter_index += coeff_count * increment;
        data_index = self.b_current + channels * (1 + coeff_count);
        let mut right = [0.0f64; 2];
        loop {
            let icoeff = coeff(filter_index);
            for (ch, value) in right.iter_mut().enumerate() {
                *value += icoeff * f64::from(self.buffer[data_index as usize + ch]);
            }
            filter_index -= increment;
            data_index -= 2;
            if filter_index <= 0 {
                break;
            }
        }
        for ch in 0..2 {
            output[ch] = (scale * (left[ch] + right[ch])) as f32;
        }
    }

    /// `src_process` for interleaved stereo with `end_of_input = 0`.
    pub fn process(&mut self, input: &[f32], output: &mut [f32], src_ratio: f64) -> Result<Processed, SrcError> {
        if !is_valid_ratio(src_ratio) {
            return Err(SrcError::BadSrcRatio);
        }
        if self.last_ratio < 1.0 / SRC_MAX_RATIO {
            self.last_ratio = src_ratio;
        }
        let channels = CHANNELS as i64;
        let input_frames = (input.len() / CHANNELS) as i64;
        let output_frames = (output.len() / CHANNELS) as i64;
        self.in_count = input_frames * channels;
        self.out_count = output_frames * channels;
        self.in_used = 0;
        self.out_gen = 0;
        let mut ratio = self.last_ratio;
        if !is_valid_ratio(ratio) {
            return Err(SrcError::BadInternalState);
        }
        // Check the sample-rate ratio against the buffer length.
        let mut count = (f64::from(self.coeff_half_len) + 2.0) / f64::from(self.index_inc);
        let smallest = self.last_ratio.min(src_ratio);
        if smallest < 1.0 {
            count /= smallest;
        }
        let half_filter_chan_len = CHANNELS as i32 * (lrint(count) as i32 + 1);
        let mut input_index = self.last_position;
        let mut rem = fmod_one(input_index);
        self.b_current =
            ((i64::from(self.b_current) + channels * lrint(input_index - rem)) % i64::from(self.b_len)) as i32;
        input_index = rem;
        let terminate = 1.0 / ratio + 1e-20;
        while self.out_gen < self.out_count {
            let mut samples_in_hand = (self.b_end - self.b_current + self.b_len) % self.b_len;
            if samples_in_hand <= half_filter_chan_len {
                self.prepare_data(input, half_filter_chan_len)?;
                samples_in_hand = (self.b_end - self.b_current + self.b_len) % self.b_len;
                if samples_in_hand <= half_filter_chan_len {
                    break;
                }
            }
            if self.b_real_end >= 0 && f64::from(self.b_current) + input_index + terminate >= f64::from(self.b_real_end) {
                break;
            }
            if self.out_count > 0 && (self.last_ratio - src_ratio).abs() > 1e-10 {
                ratio = self.last_ratio + self.out_gen as f64 * (src_ratio - self.last_ratio) / self.out_count as f64;
            }
            let float_increment = f64::from(self.index_inc) * if ratio < 1.0 { ratio } else { 1.0 };
            let increment = double_to_fp(float_increment);
            let start_filter_index = double_to_fp(input_index * float_increment);
            let at = self.out_gen as usize;
            let mut frame = [0.0f32; 2];
            self.calc_output_stereo(increment, start_filter_index, float_increment / f64::from(self.index_inc), &mut frame);
            output[at..at + 2].copy_from_slice(&frame);
            self.out_gen += 2;
            input_index += 1.0 / ratio;
            rem = fmod_one(input_index);
            self.b_current =
                ((i64::from(self.b_current) + channels * lrint(input_index - rem)) % i64::from(self.b_len)) as i32;
            input_index = rem;
        }
        self.last_position = input_index;
        // Save the current ratio rather than the target ratio.
        self.last_ratio = ratio;
        Ok(Processed {
            input_frames_used: (self.in_used / channels) as usize,
            output_frames_generated: (self.out_gen / channels) as usize,
        })
    }
}

/// The engine's streaming stereo adapter around [`SincConverter`]
/// (`AdaptiveResampler` in `dsp.cpp`).
#[derive(Clone, Debug)]
pub struct AdaptiveResampler {
    converter: SincConverter,
    frame_capacity: usize,
    input: Vec<f32>,
    output: Vec<f32>,
    pending: usize,
    base_ratio: f64,
    active_ratio: f64,
    error: i32,
    dropped: u64,
}

/// Construction errors (raised before any stream opens).
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum ResamplerError {
    InvalidDimensions,
    InvalidRates,
    UnsupportedRatio,
}

impl std::fmt::Display for ResamplerError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(match self {
            Self::InvalidDimensions => "Invalid audio resampler dimensions",
            Self::InvalidRates => "Audio sample rates must be positive",
            Self::UnsupportedRatio => "Unsupported audio sample-rate ratio",
        })
    }
}

impl std::error::Error for ResamplerError {}

fn clean(sample: f32) -> f32 {
    if sample.is_finite() { sample } else { 0.0 }
}

impl AdaptiveResampler {
    pub const DEFAULT_CAPACITY: usize = 32_768;

    pub fn new(input_rate: u32, output_rate: u32, maximum_buffered_frames: usize) -> Result<Self, ResamplerError> {
        if maximum_buffered_frames == 0 || maximum_buffered_frames > i32::MAX as usize / 2 {
            return Err(ResamplerError::InvalidDimensions);
        }
        if input_rate == 0 || output_rate == 0 {
            return Err(ResamplerError::InvalidRates);
        }
        let base_ratio = f64::from(output_rate) / f64::from(input_rate);
        if !is_valid_ratio(base_ratio * (1.0 - MAXIMUM_DRIFT)) || !is_valid_ratio(base_ratio * (1.0 + MAXIMUM_DRIFT)) {
            return Err(ResamplerError::UnsupportedRatio);
        }
        let mut converter = SincConverter::new();
        converter.set_ratio(base_ratio).map_err(|_| ResamplerError::UnsupportedRatio)?;
        Ok(Self {
            converter,
            frame_capacity: maximum_buffered_frames,
            input: vec![0.0; maximum_buffered_frames * 2],
            output: vec![0.0; maximum_buffered_frames * 2],
            pending: 0,
            base_ratio,
            active_ratio: base_ratio,
            error: 0,
            dropped: 0,
        })
    }

    /// Retains input until consumed; returns how many frames were accepted.
    pub fn push(&mut self, input: &[Frame]) -> usize {
        let accepted = input.len().min(self.frame_capacity - self.pending);
        for (i, frame) in input.iter().take(accepted).enumerate() {
            self.input[(self.pending + i) * 2] = clean(frame.left);
            self.input[(self.pending + i) * 2 + 1] = clean(frame.right);
        }
        self.pending += accepted;
        self.dropped += (input.len() - accepted) as u64;
        accepted
    }

    /// Fills `output` as far as pending input allows, with the output/input
    /// ratio scaled by `correction` (clamped to +/- 2000 ppm).
    pub fn produce(&mut self, output: &mut [Frame], correction: f64) -> usize {
        let correction = if correction.is_finite() { correction } else { 1.0 };
        let requested_ratio = self.base_ratio * correction.clamp(1.0 - MAXIMUM_DRIFT, 1.0 + MAXIMUM_DRIFT);
        let mut generated = 0;
        while generated < output.len() {
            let requested = (output.len() - generated).min(self.frame_capacity);
            let result = self.converter.process(
                &self.input[..self.pending * 2],
                &mut self.output[..requested * 2],
                requested_ratio,
            );
            let processed = match result {
                Ok(processed) => {
                    self.error = 0;
                    processed
                }
                Err(error) => {
                    self.error = error as i32;
                    break;
                }
            };
            self.active_ratio = requested_ratio;
            let used = processed.input_frames_used;
            let made = processed.output_frames_generated;
            for i in 0..made {
                output[generated + i] = Frame::new(clean(self.output[i * 2]), clean(self.output[i * 2 + 1]));
            }
            self.pending -= used;
            if used != 0 && self.pending != 0 {
                self.input.copy_within(used * 2..(used + self.pending) * 2, 0);
            }
            generated += made;
            if made == 0 && used == 0 {
                break;
            }
        }
        generated
    }

    pub fn reset(&mut self) {
        self.converter.reset();
        self.pending = 0;
        self.active_ratio = self.base_ratio;
        self.error = self.converter.set_ratio(self.base_ratio).err().map_or(0, |e| e as i32);
    }

    pub fn pending_input_frames(&self) -> usize {
        self.pending
    }

    pub fn capacity(&self) -> usize {
        self.frame_capacity
    }

    pub fn ratio(&self) -> f64 {
        self.active_ratio
    }

    pub fn last_error(&self) -> i32 {
        self.error
    }

    pub fn dropped_input_frames(&self) -> u64 {
        self.dropped
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unity_ratio_is_a_delayed_identity() {
        let mut resampler = AdaptiveResampler::new(48_000, 48_000, 4096).unwrap();
        let input: Vec<Frame> = (0..4000).map(|n| Frame::new((n as f32 * 0.01).sin(), 0.0)).collect();
        resampler.push(&input);
        let mut output = vec![Frame::SILENT; 3000];
        let made = resampler.produce(&mut output, 1.0);
        assert!(made > 2000);
        let delay = (0..64).find(|&d| (output[1000 + d].left - input[1000].left).abs() < 1e-3).expect("aligned");
        for n in 1000..1500 {
            assert!((output[n + delay].left - input[n].left).abs() < 2e-3);
        }
    }

    #[test]
    fn converts_44_1_to_48_khz_tone_without_loss() {
        let mut resampler = AdaptiveResampler::new(44_100, 48_000, 32_768).unwrap();
        let input: Vec<Frame> = (0..44_100)
            .map(|n| {
                let x = (2.0 * std::f64::consts::PI * 1000.0 * n as f64 / 44_100.0).sin() as f32 * 0.5;
                Frame::new(x, -x)
            })
            .collect();
        // Stream like a device: push a period, then drain it.
        let mut output = Vec::new();
        let mut block = [Frame::SILENT; 480];
        for chunk in input.chunks(441) {
            resampler.push(chunk);
            let made = resampler.produce(&mut block, 1.0);
            output.extend_from_slice(&block[..made]);
        }
        assert!(output.len() > 46_000, "{}", output.len());
        let peak = output[2000..46_000].iter().fold(0.0f32, |p, f| p.max(f.left.abs()));
        assert!((peak - 0.5).abs() < 0.01, "{peak}");
    }

    #[test]
    fn rejects_bad_rates() {
        assert_eq!(AdaptiveResampler::new(0, 48_000, 16).unwrap_err(), ResamplerError::InvalidRates);
        assert_eq!(AdaptiveResampler::new(1, 48_000, 16).unwrap_err(), ResamplerError::UnsupportedRatio);
        assert_eq!(AdaptiveResampler::new(48_000, 48_000, 0).unwrap_err(), ResamplerError::InvalidDimensions);
    }

    #[test]
    fn nonfinite_ratios_leave_stream_history_intact() {
        let mut converter = SincConverter::new();
        let input = [0.25; 512];
        let mut output = [0.0; 256];
        converter.process(&input, &mut output, 1.0).unwrap();
        let mut expected = converter.clone();
        for ratio in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
            assert_eq!(converter.set_ratio(ratio), Err(SrcError::BadSrcRatio));
            assert_eq!(converter.process(&input, &mut output, ratio), Err(SrcError::BadSrcRatio));
        }
        let mut reference = [0.0; 256];
        assert_eq!(converter.process(&input, &mut output, 1.1), expected.process(&input, &mut reference, 1.1));
        assert_eq!(output, reference);
        assert_eq!(converter, expected);
    }
}
