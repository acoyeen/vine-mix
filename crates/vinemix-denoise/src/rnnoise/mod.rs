//! RNNoise v0.2 (Jean-Marc Valin, Xiph.Org; BSD licence) ported to Rust,
//! with the upstream 0b50c45 model embedded.
//!
//! A port of `denoise.c` (`rnnoise_process_frame` and its analysis/synthesis)
//! keeping the C library's float/double promotions exactly. With the same
//! kernel family selected, its output matches the C library sample for
//! sample (checked by `tests/rnnoise_reference.rs`).

pub mod fft;
pub mod nnet;
pub mod pitch;
#[rustfmt::skip]
pub mod tables;

use fft::{Complex, NFFT};
pub use nnet::Kernel;
use nnet::{RnnState, DENSE_OUT_SIZE};
use tables::{DCT_TABLE, HALF_WINDOW};

pub const FRAME_SIZE: usize = 480;
const WINDOW_SIZE: usize = 2 * FRAME_SIZE;
const FREQ_SIZE: usize = FRAME_SIZE + 1;
const NB_BANDS: usize = 32;
const NB_FEATURES: usize = 2 * NB_BANDS + 1;
pub(crate) const PITCH_MIN_PERIOD: usize = 60;
pub(crate) const PITCH_MAX_PERIOD: usize = 768;
pub(crate) const PITCH_FRAME_SIZE: usize = 960;
pub(crate) const PITCH_BUF_SIZE: usize = PITCH_MAX_PERIOD + PITCH_FRAME_SIZE;

/// ERB-like band edges in 50 Hz bins.
const EBAND20MS: [usize; NB_BANDS + 2] = [
    0, 2, 4, 6, 8, 10, 12, 15, 18, 21, 24, 28, 32, 36, 41, 47, 53, 60, 68, 77, 87, 98, 110, 124, 140, 157, 176, 198,
    223, 251, 282, 317, 356, 400,
];

const _: () = assert!(NFFT == WINDOW_SIZE);

fn compute_band_energy(band_e: &mut [f32; NB_BANDS], x: &[Complex; FREQ_SIZE]) {
    let mut sum = [0.0f32; NB_BANDS + 2];
    for i in 0..NB_BANDS + 1 {
        let band_size = EBAND20MS[i + 1] - EBAND20MS[i];
        for j in 0..band_size {
            let frac = j as f32 / band_size as f32;
            let bin = x[EBAND20MS[i] + j];
            let mut tmp = bin.r * bin.r;
            tmp += bin.i * bin.i;
            sum[i] += (1.0 - frac) * tmp;
            sum[i + 1] += frac * tmp;
        }
    }
    sum[1] = (sum[0] + sum[1]) * 2.0 / 3.0;
    sum[NB_BANDS] = (sum[NB_BANDS] + sum[NB_BANDS + 1]) * 2.0 / 3.0;
    band_e.copy_from_slice(&sum[1..=NB_BANDS]);
}

fn compute_band_corr(band_e: &mut [f32; NB_BANDS], x: &[Complex; FREQ_SIZE], p: &[Complex; FREQ_SIZE]) {
    let mut sum = [0.0f32; NB_BANDS + 2];
    for i in 0..NB_BANDS + 1 {
        let band_size = EBAND20MS[i + 1] - EBAND20MS[i];
        for j in 0..band_size {
            let frac = j as f32 / band_size as f32;
            let (a, b) = (x[EBAND20MS[i] + j], p[EBAND20MS[i] + j]);
            let mut tmp = a.r * b.r;
            tmp += a.i * b.i;
            sum[i] += (1.0 - frac) * tmp;
            sum[i + 1] += frac * tmp;
        }
    }
    sum[1] = (sum[0] + sum[1]) * 2.0 / 3.0;
    sum[NB_BANDS] = (sum[NB_BANDS] + sum[NB_BANDS + 1]) * 2.0 / 3.0;
    band_e.copy_from_slice(&sum[1..=NB_BANDS]);
}

/// Interpolates band gains to bins. Bins above the last band (20 kHz) are
/// left as the caller initialised them, exactly as upstream.
fn interp_band_gain(g: &mut [f32; FREQ_SIZE], band_e: &[f32; NB_BANDS]) {
    for i in 1..NB_BANDS {
        let band_size = EBAND20MS[i + 1] - EBAND20MS[i];
        for j in 0..band_size {
            let frac = j as f32 / band_size as f32;
            g[EBAND20MS[i] + j] = (1.0 - frac) * band_e[i - 1] + frac * band_e[i];
        }
    }
    for value in g.iter_mut().take(EBAND20MS[1]) {
        *value = band_e[0];
    }
    for value in g.iter_mut().take(EBAND20MS[NB_BANDS + 1]).skip(EBAND20MS[NB_BANDS]) {
        *value = band_e[NB_BANDS - 1];
    }
}

fn dct(out: &mut [f32], input: &[f32; NB_BANDS]) {
    let scale = (2.0f64 / 22.0).sqrt();
    for i in 0..NB_BANDS {
        let mut sum = 0.0f32;
        for j in 0..NB_BANDS {
            sum += input[j] * DCT_TABLE[j * NB_BANDS + i];
        }
        out[i] = (f64::from(sum) * scale) as f32;
    }
}

fn forward_transform(out: &mut [Complex; FREQ_SIZE], input: &[f32; WINDOW_SIZE]) {
    let mut x = [Complex::ZERO; WINDOW_SIZE];
    for (value, &sample) in x.iter_mut().zip(input) {
        value.r = sample;
    }
    let mut y = [Complex::ZERO; WINDOW_SIZE];
    fft::fft(&x, &mut y);
    out.copy_from_slice(&y[..FREQ_SIZE]);
}

fn inverse_transform(out: &mut [f32; WINDOW_SIZE], input: &[Complex; FREQ_SIZE]) {
    let mut x = [Complex::ZERO; WINDOW_SIZE];
    x[..FREQ_SIZE].copy_from_slice(input);
    for i in FREQ_SIZE..WINDOW_SIZE {
        x[i] = Complex { r: x[WINDOW_SIZE - i].r, i: -x[WINDOW_SIZE - i].i };
    }
    let mut y = [Complex::ZERO; WINDOW_SIZE];
    fft::fft(&x, &mut y);
    // Output in reverse order for the inverse transform.
    out[0] = WINDOW_SIZE as f32 * y[0].r;
    for i in 1..WINDOW_SIZE {
        out[i] = WINDOW_SIZE as f32 * y[WINDOW_SIZE - i].r;
    }
}

fn apply_window(x: &mut [f32; WINDOW_SIZE]) {
    for i in 0..FRAME_SIZE {
        x[i] *= HALF_WINDOW[i];
        x[WINDOW_SIZE - 1 - i] *= HALF_WINDOW[i];
    }
}

/// One RNNoise denoiser instance (`DenoiseState`). About 30 KiB; the model
/// weights are shared.
#[derive(Clone, Debug, PartialEq)]
pub struct RnNoise {
    kernel: Kernel,
    analysis_mem: [f32; FRAME_SIZE],
    synthesis_mem: [f32; FRAME_SIZE],
    pitch_buf: [f32; PITCH_BUF_SIZE],
    last_gain: f32,
    last_period: usize,
    mem_hp_x: [f32; 2],
    lastg: [f32; NB_BANDS],
    rnn: RnnState,
    delayed_x: [Complex; FREQ_SIZE],
    delayed_p: [Complex; FREQ_SIZE],
    delayed_ex: [f32; NB_BANDS],
    delayed_ep: [f32; NB_BANDS],
    delayed_exp: [f32; NB_BANDS],
}

impl RnNoise {
    pub const FRAME_SIZE: usize = FRAME_SIZE;

    /// A new denoiser using the kernel family upstream would select.
    pub fn new() -> Box<Self> {
        Self::with_kernel(Kernel::detect())
    }

    /// A new denoiser with an explicit kernel family (tests and diagnostics).
    pub fn with_kernel(kernel: Kernel) -> Box<Self> {
        assert!(kernel.available(), "RNNoise kernel {kernel:?} is not supported by this CPU");
        // Parse the shared model now, on the preparing thread, never during
        // the first audio frame.
        std::sync::LazyLock::force(&nnet::MODEL);
        Box::new(Self {
            kernel,
            analysis_mem: [0.0; FRAME_SIZE],
            synthesis_mem: [0.0; FRAME_SIZE],
            pitch_buf: [0.0; PITCH_BUF_SIZE],
            last_gain: 0.0,
            last_period: 0,
            mem_hp_x: [0.0; 2],
            lastg: [0.0; NB_BANDS],
            rnn: RnnState::default(),
            delayed_x: [Complex::ZERO; FREQ_SIZE],
            delayed_p: [Complex::ZERO; FREQ_SIZE],
            delayed_ex: [0.0; NB_BANDS],
            delayed_ep: [0.0; NB_BANDS],
            delayed_exp: [0.0; NB_BANDS],
        })
    }

    pub fn kernel(&self) -> Kernel {
        self.kernel
    }

    /// Copies all state from another instance without allocating.
    pub fn copy_state_from(&mut self, other: &Self) {
        *self = other.clone();
    }

    fn frame_analysis(&mut self, x_out: &mut [Complex; FREQ_SIZE], ex: &mut [f32; NB_BANDS], input: &[f32; FRAME_SIZE]) {
        let mut x = [0.0f32; WINDOW_SIZE];
        x[..FRAME_SIZE].copy_from_slice(&self.analysis_mem);
        x[FRAME_SIZE..].copy_from_slice(input);
        self.analysis_mem.copy_from_slice(input);
        apply_window(&mut x);
        forward_transform(x_out, &x);
        compute_band_energy(ex, x_out);
    }

    #[allow(clippy::too_many_arguments)]
    fn compute_frame_features(
        &mut self,
        x: &mut [Complex; FREQ_SIZE],
        p: &mut [Complex; FREQ_SIZE],
        ex: &mut [f32; NB_BANDS],
        ep: &mut [f32; NB_BANDS],
        exp: &mut [f32; NB_BANDS],
        features: &mut [f32; NB_FEATURES],
        input: &[f32; FRAME_SIZE],
    ) -> bool {
        self.frame_analysis(x, ex, input);
        self.pitch_buf.copy_within(FRAME_SIZE.., 0);
        self.pitch_buf[PITCH_BUF_SIZE - FRAME_SIZE..].copy_from_slice(input);
        let mut pitch_buf = [0.0f32; PITCH_BUF_SIZE >> 1];
        pitch::pitch_downsample(&self.pitch_buf, &mut pitch_buf, PITCH_BUF_SIZE);
        let found = pitch::pitch_search(
            &pitch_buf[PITCH_MAX_PERIOD >> 1..],
            &pitch_buf,
            PITCH_FRAME_SIZE,
            PITCH_MAX_PERIOD - 3 * PITCH_MIN_PERIOD,
        );
        let mut pitch_index = PITCH_MAX_PERIOD - found;
        let gain = pitch::remove_doubling(
            &pitch_buf,
            PITCH_MAX_PERIOD,
            PITCH_MIN_PERIOD,
            PITCH_FRAME_SIZE,
            &mut pitch_index,
            self.last_period,
            self.last_gain,
        );
        self.last_period = pitch_index;
        self.last_gain = gain;
        let mut window = [0.0f32; WINDOW_SIZE];
        window.copy_from_slice(&self.pitch_buf[PITCH_BUF_SIZE - WINDOW_SIZE - pitch_index..][..WINDOW_SIZE]);
        apply_window(&mut window);
        forward_transform(p, &window);
        compute_band_energy(ep, p);
        compute_band_corr(exp, x, p);
        for i in 0..NB_BANDS {
            exp[i] = (f64::from(exp[i]) / (0.001f64 + f64::from(ex[i] * ep[i])).sqrt()) as f32;
        }
        let (cepstrum, rest) = features.split_at_mut(NB_BANDS);
        dct(&mut rest[..NB_BANDS], exp);
        rest[NB_BANDS] = (0.01f64 * (pitch_index as f64 - 300.0)) as f32;
        let mut log_max = -2.0f32;
        let mut follow = -2.0f32;
        let mut ly = [0.0f32; NB_BANDS];
        let mut energy = 0.0f32;
        for i in 0..NB_BANDS {
            ly[i] = (1e-2f64 + f64::from(ex[i])).log10() as f32;
            let lower = f64::from(follow) - 1.5;
            let inner = if lower > f64::from(ly[i]) { lower } else { f64::from(ly[i]) };
            let floor = f64::from(log_max - 7.0);
            ly[i] = (if floor > inner { floor } else { inner }) as f32;
            log_max = if log_max > ly[i] { log_max } else { ly[i] };
            let lower = f64::from(follow) - 1.5;
            follow = (if lower > f64::from(ly[i]) { lower } else { f64::from(ly[i]) }) as f32;
            energy += ex[i];
        }
        if f64::from(energy) < 0.04 {
            // No audio: avoid disturbing the network state.
            features.fill(0.0);
            return true;
        }
        dct(cepstrum, &ly);
        cepstrum[0] -= 12.0;
        cepstrum[1] -= 4.0;
        false
    }

    fn frame_synthesis(&mut self, out: &mut [f32], y: &[Complex; FREQ_SIZE]) {
        let mut x = [0.0f32; WINDOW_SIZE];
        inverse_transform(&mut x, y);
        apply_window(&mut x);
        for i in 0..FRAME_SIZE {
            out[i] = x[i] + self.synthesis_mem[i];
        }
        self.synthesis_mem.copy_from_slice(&x[FRAME_SIZE..]);
    }

    fn biquad(&mut self, y: &mut [f32; FRAME_SIZE], x: &[f32]) {
        let a: [f32; 2] = [-1.99599, 0.99600];
        let b: [f32; 2] = [-2.0, 1.0];
        let mem = &mut self.mem_hp_x;
        for i in 0..FRAME_SIZE {
            let xi = x[i];
            let yi = x[i] + mem[0];
            mem[0] = (f64::from(mem[1]) + (f64::from(b[0]) * f64::from(xi) - f64::from(a[0]) * f64::from(yi))) as f32;
            mem[1] = (f64::from(b[1]) * f64::from(xi) - f64::from(a[1]) * f64::from(yi)) as f32;
            y[i] = yi;
        }
    }

    fn pitch_filter(&mut self, g: &[f32; NB_BANDS]) {
        let mut r = [0.0f32; NB_BANDS];
        let mut rf = [0.0f32; FREQ_SIZE];
        let mut new_e = [0.0f32; NB_BANDS];
        let mut norm = [0.0f32; NB_BANDS];
        let mut normf = [0.0f32; FREQ_SIZE];
        for i in 0..NB_BANDS {
            let (exp, gain) = (self.delayed_exp[i], g[i]);
            r[i] = if exp > gain {
                1.0
            } else {
                let numerator = (exp * exp) * (1.0 - gain * gain);
                (f64::from(numerator) / (0.001f64 + f64::from((gain * gain) * (1.0 - exp * exp)))) as f32
            };
            let clamped = if 0.0 > r[i] { 0.0 } else { r[i] };
            let clamped = if 1.0 < clamped { 1.0 } else { clamped };
            r[i] = f64::from(clamped).sqrt() as f32;
            let ratio = f64::from(self.delayed_ex[i]) / (1e-8f64 + f64::from(self.delayed_ep[i]));
            r[i] = (f64::from(r[i]) * ratio.sqrt()) as f32;
        }
        interp_band_gain(&mut rf, &r);
        for i in 0..FREQ_SIZE {
            self.delayed_x[i].r += rf[i] * self.delayed_p[i].r;
            self.delayed_x[i].i += rf[i] * self.delayed_p[i].i;
        }
        compute_band_energy(&mut new_e, &self.delayed_x);
        for i in 0..NB_BANDS {
            norm[i] = (f64::from(self.delayed_ex[i]) / (1e-8f64 + f64::from(new_e[i]))).sqrt() as f32;
        }
        interp_band_gain(&mut normf, &norm);
        for i in 0..FREQ_SIZE {
            self.delayed_x[i].r *= normf[i];
            self.delayed_x[i].i *= normf[i];
        }
    }

    /// `rnnoise_process_frame`: 480 samples (16-bit scale) in, denoised
    /// samples out (one frame of latency). Returns the voice probability.
    pub fn process_frame(&mut self, out: &mut [f32], input: &[f32]) -> f32 {
        assert!(out.len() >= FRAME_SIZE && input.len() >= FRAME_SIZE);
        let mut x_buf = [0.0f32; FRAME_SIZE];
        self.biquad(&mut x_buf, input);
        let mut x = [Complex::ZERO; FREQ_SIZE];
        let mut p = [Complex::ZERO; FREQ_SIZE];
        let mut ex = [0.0f32; NB_BANDS];
        let mut ep = [0.0f32; NB_BANDS];
        let mut exp = [0.0f32; NB_BANDS];
        let mut features = [0.0f32; NB_FEATURES];
        let mut g = [0.0f32; DENSE_OUT_SIZE];
        let mut gf = [0.0f32; FREQ_SIZE];
        gf[0] = 1.0;
        let mut vad_prob = 0.0;
        let silence = self.compute_frame_features(&mut x, &mut p, &mut ex, &mut ep, &mut exp, &mut features, &x_buf);
        if !silence {
            vad_prob = nnet::compute_rnn(self.kernel, &mut self.rnn, &mut g, &features);
            self.pitch_filter(&g);
            for i in 0..NB_BANDS {
                let alpha = 0.6f32;
                let held = alpha * self.lastg[i];
                g[i] = if g[i] > held { g[i] } else { held };
                self.lastg[i] = g[i];
            }
            interp_band_gain(&mut gf, &g);
            for i in 0..FREQ_SIZE {
                self.delayed_x[i].r *= gf[i];
                self.delayed_x[i].i *= gf[i];
            }
        }
        let delayed = self.delayed_x;
        self.frame_synthesis(out, &delayed);
        self.delayed_x = x;
        self.delayed_p = p;
        self.delayed_ex = ex;
        self.delayed_ep = ep;
        self.delayed_exp = exp;
        vad_prob
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn removes_white_noise_and_keeps_level_bounded() {
        let mut state = RnNoise::new();
        let mut seed = 1u32;
        let (mut energy_in, mut energy_out) = (0.0f64, 0.0f64);
        for frame in 0..200 {
            let mut input = [0.0f32; FRAME_SIZE];
            for sample in &mut input {
                seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
                *sample = ((seed >> 8) as f32 / (1u32 << 24) as f32 - 0.5) * 2000.0;
            }
            let mut output = [0.0f32; FRAME_SIZE];
            state.process_frame(&mut output, &input);
            if frame > 50 {
                energy_in += input.iter().map(|&v| f64::from(v * v)).sum::<f64>();
                energy_out += output.iter().map(|&v| f64::from(v * v)).sum::<f64>();
            }
        }
        let db = 10.0 * (energy_out / energy_in).log10();
        assert!(db < -10.0, "attenuation {db:.1} dB");
    }

    #[test]
    fn silence_stays_silent() {
        let mut state = RnNoise::new();
        let mut output = [1.0f32; FRAME_SIZE];
        for _ in 0..5 {
            state.process_frame(&mut output, &[0.0; FRAME_SIZE]);
        }
        assert!(output.iter().all(|&v| v == 0.0));
    }
}
