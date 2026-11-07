//! SpeexDSP 1.2.1 noise suppression, ported to Rust.
//!
//! A faithful port of the floating-point build of `preprocess.c`,
//! `filterbank.c` and the `smallft` FFT path (`FLOATING_POINT`,
//! `USE_SMALLFT`), restricted to what VineMix enables: spectral noise
//! suppression with AGC, dereverberation, VAD and echo suppression off.
//! Every expression keeps the C library's single/double precision mix so the
//! port reproduces its 16-bit output exactly (checked against the pinned C
//! sources by `tests/speex_reference.rs`).
//!
//! SpeexDSP is Copyright Jean-Marc Valin, Xiph.Org Foundation and contributors
//! under a BSD licence (see `licenses/SpeexDSP-COPYING.txt`).

pub mod smallft;

use smallft::DrftLookup;

const NB_BANDS: usize = 24;
const NOISE_SUPPRESS_DEFAULT: i32 = -15;
const ECHO_SUPPRESS_DEFAULT: i32 = -40;
const ECHO_SUPPRESS_ACTIVE_DEFAULT: i32 = -15;

/// Promote to double, as C does for `float` arguments of `<math.h>` calls.
#[inline]
fn d(value: f32) -> f64 {
    f64::from(value)
}

/// `toBARK` for the floating-point build.
fn to_bark(n: f32) -> f32 {
    (13.1f32 as f64 * d(0.00074f32 * n).atan() + 2.24f32 as f64 * d(n * n * 1.85e-8f32).atan() + d(1e-4f32 * n))
        as f32
}

#[derive(Clone, Debug, PartialEq)]
struct FilterBank {
    bank_left: Vec<usize>,
    bank_right: Vec<usize>,
    filter_left: Vec<f32>,
    filter_right: Vec<f32>,
    nb_banks: usize,
    len: usize,
}

impl FilterBank {
    fn new(banks: usize, sampling: f32, len: usize) -> Self {
        let df = sampling / (2.0f32 * len as f32);
        let max_mel = to_bark(sampling / 2.0);
        let mel_interval = max_mel / (banks - 1) as f32;
        let mut bank = Self {
            bank_left: vec![0; len],
            bank_right: vec![0; len],
            filter_left: vec![0.0; len],
            filter_right: vec![0.0; len],
            nb_banks: banks,
            len,
        };
        for i in 0..len {
            let curr_freq = i as f32 * df;
            let mel = to_bark(curr_freq);
            if mel > max_mel {
                break;
            }
            let mut id1 = d(mel / mel_interval).floor() as i32;
            let val;
            if id1 > banks as i32 - 2 {
                id1 = banks as i32 - 2;
                val = 1.0f32;
            } else {
                val = (mel - id1 as f32 * mel_interval) / mel_interval;
            }
            bank.bank_left[i] = id1 as usize;
            bank.filter_left[i] = 1.0 - val;
            bank.bank_right[i] = id1 as usize + 1;
            bank.filter_right[i] = val;
        }
        bank
    }

    /// Linear power spectrum to band energies.
    fn compute_bank32(&self, ps: &[f32], mel: &mut [f32]) {
        mel[..self.nb_banks].fill(0.0);
        for i in 0..self.len {
            mel[self.bank_left[i]] += self.filter_left[i] * ps[i];
            mel[self.bank_right[i]] += self.filter_right[i] * ps[i];
        }
    }

    /// Band values interpolated back to linear frequency.
    fn compute_psd16(&self, mel: &[f32], ps: &mut [f32]) {
        for i in 0..self.len {
            let mut tmp = mel[self.bank_left[i]] * self.filter_left[i];
            tmp += mel[self.bank_right[i]] * self.filter_right[i];
            ps[i] = tmp;
        }
    }
}

/// `conj_window` for the floating-point build.
fn conj_window(w: &mut [f32], len: usize) {
    for (i, value) in w.iter_mut().enumerate().take(len) {
        let mut x = (4.0f32 * i as f32) / len as f32;
        let mut inv = false;
        if x < 1.0 {
        } else if x < 2.0 {
            x = 2.0 - x;
            inv = true;
        } else if x < 3.0 {
            x -= 2.0;
            inv = true;
        } else {
            x = 2.0 - x + 2.0;
        }
        x *= 1.271903f32;
        let half_pi = 0.5f64 * std::f64::consts::PI;
        let cos = (half_pi * d(x)).cos();
        let inner = 0.5f64 - 0.5f64 * cos;
        let mut tmp = (inner * inner) as f32;
        if inv {
            tmp = 1.0 - tmp;
        }
        *value = d(tmp).sqrt() as f32;
    }
}

/// `hypergeom_gain` for the floating-point build.
fn hypergeom_gain(xx: f32) -> f32 {
    const TABLE: [f32; 21] = [
        0.82157, 1.02017, 1.20461, 1.37534, 1.53363, 1.68092, 1.81865, 1.94811, 2.07038, 2.18638, 2.29688, 2.40255,
        2.50391, 2.60144, 2.69551, 2.78647, 2.87458, 2.96015, 3.04333, 3.12431, 3.20326,
    ];
    let x = xx;
    let integer = d(2.0 * x).floor() as f32;
    let ind = integer as i32;
    if ind < 0 {
        return 1.0;
    }
    if ind > 19 {
        return (1.0f64 * (1.0 + 0.1296 / d(x))) as f32;
    }
    let frac = 2.0 * x - integer;
    let ind = ind as usize;
    (d((1.0 - frac) * TABLE[ind] + frac * TABLE[ind + 1]) / d(x + 0.0001f32).sqrt()) as f32
}

#[inline]
fn qcurve(x: f32) -> f32 {
    1.0 / (1.0 + 0.15 / x)
}

/// `WORD2INT` for the floating-point build.
#[inline]
fn word2int(x: f32) -> i16 {
    if x < -32767.5 {
        -32768
    } else if x > 32766.5 {
        32767
    } else {
        (0.5f64 + d(x)).floor() as i16
    }
}

/// One channel of the Speex preprocessor.
#[derive(Clone, Debug, PartialEq)]
pub struct SpeexPreprocessor {
    frame_size: usize,
    ps_size: usize,
    nbands: usize,
    bank: FilterBank,
    fft: DrftLookup,
    denoise_enabled: bool,
    noise_suppress: i32,
    echo_suppress: i32,
    echo_suppress_active: i32,
    speech_prob: f32,
    frame: Vec<f32>,
    ft: Vec<f32>,
    ps: Vec<f32>,
    gain2: Vec<f32>,
    gain_floor: Vec<f32>,
    window: Vec<f32>,
    noise: Vec<f32>,
    reverb_estimate: Vec<f32>,
    old_ps: Vec<f32>,
    gain: Vec<f32>,
    prior: Vec<f32>,
    post: Vec<f32>,
    s: Vec<f32>,
    smin: Vec<f32>,
    stmp: Vec<f32>,
    update_prob: Vec<bool>,
    zeta: Vec<f32>,
    echo_noise: Vec<f32>,
    inbuf: Vec<f32>,
    outbuf: Vec<f32>,
    nb_adapt: i32,
    min_count: i32,
}

impl SpeexPreprocessor {
    /// `speex_preprocess_state_init` (AGC, dereverb and VAD off).
    pub fn new(frame_size: usize, sampling_rate: u32) -> Self {
        let n = frame_size;
        let n3 = 2 * n - frame_size;
        let n4 = frame_size - n3;
        let m = NB_BANDS;
        let mut window = vec![0.0f32; 2 * n];
        conj_window(&mut window, 2 * n3);
        for value in window.iter_mut().skip(2 * n3) {
            *value = 1.0;
        }
        if n4 > 0 {
            for i in (0..n3).rev() {
                window[i + n3 + n4] = window[i + n3];
                window[i + n3] = 1.0;
            }
        }
        Self {
            frame_size,
            ps_size: n,
            nbands: m,
            bank: FilterBank::new(m, sampling_rate as f32, n),
            fft: DrftLookup::new(2 * n),
            denoise_enabled: true,
            noise_suppress: NOISE_SUPPRESS_DEFAULT,
            echo_suppress: ECHO_SUPPRESS_DEFAULT,
            echo_suppress_active: ECHO_SUPPRESS_ACTIVE_DEFAULT,
            speech_prob: 0.0,
            frame: vec![0.0; 2 * n],
            ft: vec![0.0; 2 * n],
            ps: vec![0.0; n + m],
            gain2: vec![0.0; n + m],
            gain_floor: vec![0.0; n + m],
            window,
            noise: vec![1.0; n + m],
            reverb_estimate: vec![0.0; n + m],
            old_ps: vec![1.0; n + m],
            gain: vec![1.0; n + m],
            prior: vec![1.0; n + m],
            post: vec![1.0; n + m],
            s: vec![0.0; n],
            smin: vec![0.0; n],
            stmp: vec![0.0; n],
            update_prob: vec![true; n],
            zeta: vec![0.0; n + m],
            echo_noise: vec![0.0; n + m],
            inbuf: vec![0.0; n3],
            outbuf: vec![0.0; n3],
            nb_adapt: 0,
            min_count: 0,
        }
    }

    /// `SPEEX_PREPROCESS_SET_NOISE_SUPPRESS`: maximum attenuation in dB.
    pub fn set_noise_suppress(&mut self, db: i32) {
        self.noise_suppress = -db.abs();
    }

    pub fn noise_suppress(&self) -> i32 {
        self.noise_suppress
    }

    pub fn set_denoise(&mut self, enabled: bool) {
        self.denoise_enabled = enabled;
    }

    /// Probability that the last frame was speech.
    pub fn speech_probability(&self) -> f32 {
        self.speech_prob
    }

    /// Copies all adaptive state from an identically configured processor
    /// without allocating (`am_speex_copy`).
    pub fn copy_state_from(&mut self, other: &Self) {
        debug_assert_eq!(self.frame_size, other.frame_size);
        self.denoise_enabled = other.denoise_enabled;
        self.noise_suppress = other.noise_suppress;
        self.echo_suppress = other.echo_suppress;
        self.echo_suppress_active = other.echo_suppress_active;
        self.speech_prob = other.speech_prob;
        for (a, b) in [
            (&mut self.frame, &other.frame),
            (&mut self.ft, &other.ft),
            (&mut self.ps, &other.ps),
            (&mut self.gain2, &other.gain2),
            (&mut self.gain_floor, &other.gain_floor),
            (&mut self.window, &other.window),
            (&mut self.noise, &other.noise),
            (&mut self.reverb_estimate, &other.reverb_estimate),
            (&mut self.old_ps, &other.old_ps),
            (&mut self.gain, &other.gain),
            (&mut self.prior, &other.prior),
            (&mut self.post, &other.post),
            (&mut self.s, &other.s),
            (&mut self.smin, &other.smin),
            (&mut self.stmp, &other.stmp),
            (&mut self.zeta, &other.zeta),
            (&mut self.echo_noise, &other.echo_noise),
            (&mut self.inbuf, &other.inbuf),
            (&mut self.outbuf, &other.outbuf),
        ] {
            a.copy_from_slice(b);
        }
        self.update_prob.copy_from_slice(&other.update_prob);
        self.nb_adapt = other.nb_adapt;
        self.min_count = other.min_count;
    }

    fn analysis(&mut self, x: &[i16]) {
        let n = self.ps_size;
        let n3 = 2 * n - self.frame_size;
        let n4 = self.frame_size - n3;
        self.frame[..n3].copy_from_slice(&self.inbuf);
        for (i, &sample) in x.iter().enumerate().take(self.frame_size) {
            self.frame[n3 + i] = f32::from(sample);
        }
        for i in 0..n3 {
            self.inbuf[i] = f32::from(x[n4 + i]);
        }
        for (sample, &weight) in self.frame.iter_mut().zip(&self.window) {
            *sample *= weight;
        }
        // spx_fft: scale on copy, then the forward real transform.
        let scale = (1.0f64 / (2 * n) as f64) as f32;
        for (out, &value) in self.ft.iter_mut().zip(&self.frame) {
            *out = scale * value;
        }
        self.fft.forward(&mut self.ft);
        self.ps[0] = self.ft[0] * self.ft[0];
        for i in 1..n {
            self.ps[i] = self.ft[2 * i - 1] * self.ft[2 * i - 1] + self.ft[2 * i] * self.ft[2 * i];
        }
        let (linear, bands) = self.ps.split_at_mut(n);
        self.bank.compute_bank32(linear, bands);
    }

    fn update_noise_prob(&mut self) {
        let n = self.ps_size;
        for i in 1..n - 1 {
            self.s[i] = 0.8f32 * self.s[i] + 0.05f32 * self.ps[i - 1] + 0.1f32 * self.ps[i] + 0.05f32 * self.ps[i + 1];
        }
        self.s[0] = 0.8f32 * self.s[0] + 0.2f32 * self.ps[0];
        self.s[n - 1] = 0.8f32 * self.s[n - 1] + 0.2f32 * self.ps[n - 1];
        if self.nb_adapt == 1 {
            self.smin.fill(0.0);
            self.stmp.fill(0.0);
        }
        let min_range = if self.nb_adapt < 100 {
            15
        } else if self.nb_adapt < 1000 {
            50
        } else if self.nb_adapt < 10000 {
            150
        } else {
            300
        };
        let min32 = |a: f32, b: f32| if a < b { a } else { b };
        if self.min_count > min_range {
            self.min_count = 0;
            for i in 0..n {
                self.smin[i] = min32(self.stmp[i], self.s[i]);
                self.stmp[i] = self.s[i];
            }
        } else {
            for i in 0..n {
                self.smin[i] = min32(self.smin[i], self.s[i]);
                self.stmp[i] = min32(self.stmp[i], self.s[i]);
            }
        }
        for i in 0..n {
            self.update_prob[i] = 0.4f32 * self.s[i] > self.smin[i];
        }
    }

    fn compute_gain_floor(&mut self, effective_echo_suppress: i32) {
        let n = self.ps_size;
        let noise_floor = d(0.230_258_5f32 * self.noise_suppress as f32).exp() as f32;
        let echo_floor = d(0.230_258_5f32 * effective_echo_suppress as f32).exp() as f32;
        for i in n..n + self.nbands {
            let noise = self.noise[i];
            let echo = self.echo_noise[i];
            self.gain_floor[i] =
                (1.0f64 * d(noise_floor * noise + echo_floor * echo).sqrt() / d(1.0 + noise + echo).sqrt()) as f32;
        }
    }

    /// `speex_preprocess_run`: denoises one frame of 16-bit PCM in place.
    pub fn run(&mut self, x: &mut [i16]) -> bool {
        let n = self.ps_size;
        let n3 = 2 * n - self.frame_size;
        let n4 = self.frame_size - n3;
        let m = self.nbands;
        self.nb_adapt += 1;
        if self.nb_adapt > 20000 {
            self.nb_adapt = 20000;
        }
        self.min_count += 1;
        let ratio = 1.0f32 / self.nb_adapt as f32;
        let beta = (if 0.03f64 > d(ratio) { 0.03f64 } else { d(ratio) }) as f32;
        let beta_1 = 1.0f32 - beta;
        // No echo canceller is attached.
        self.echo_noise.fill(0.0);
        self.analysis(x);
        self.update_noise_prob();

        for i in 0..n {
            if !self.update_prob[i] || self.ps[i] < self.noise[i] {
                let value = beta_1 * self.noise[i] + beta * self.ps[i];
                self.noise[i] = if 0.0 > value { 0.0 } else { value };
            }
        }
        {
            let (linear, bands) = self.noise.split_at_mut(n);
            self.bank.compute_bank32(linear, bands);
        }
        if self.nb_adapt == 1 {
            self.old_ps.copy_from_slice(&self.ps);
        }

        // A posteriori and a priori SNR.
        for i in 0..n + m {
            let tot_noise = 1.0f32 + self.noise[i] + self.echo_noise[i] + self.reverb_estimate[i];
            let mut post = self.ps[i] / tot_noise - 1.0;
            post = if post < 100.0 { post } else { 100.0 };
            self.post[i] = post;
            let ratio = self.old_ps[i] / (self.old_ps[i] + tot_noise);
            let gamma = 0.1f32 + 0.89f32 * (ratio * ratio);
            let positive = if 0.0 > post { 0.0 } else { post };
            let mut prior = gamma * positive + (1.0f32 - gamma) * (self.old_ps[i] / tot_noise);
            prior = if prior < 100.0 { prior } else { 100.0 };
            self.prior[i] = prior;
        }

        // Recursive average of the a priori SNR, smoothed across bins.
        self.zeta[0] = 0.7f32 * self.zeta[0] + 0.3f32 * self.prior[0];
        for i in 1..n - 1 {
            self.zeta[i] = 0.7f32 * self.zeta[i]
                + 0.15f32 * self.prior[i]
                + 0.075f32 * self.prior[i - 1]
                + 0.075f32 * self.prior[i + 1];
        }
        for i in n - 1..n + m {
            self.zeta[i] = 0.7f32 * self.zeta[i] + 0.3f32 * self.prior[i];
        }

        // Frame speech probability from the mean band a priori SNR.
        let mut zframe = 0.0f32;
        for i in n..n + m {
            zframe += self.zeta[i];
        }
        let pframe = 0.1f32 + 0.899f32 * qcurve(zframe / self.nbands as f32);
        let effective_echo_suppress = (1.0f32 - pframe) * self.echo_suppress as f32
            + pframe * self.echo_suppress_active as f32;
        // The C function takes `int`: the float truncates toward zero.
        self.compute_gain_floor(effective_echo_suppress as i32);

        // Ephraim-Malah gain and speech presence per Bark band.
        for i in n..n + m {
            let prior_ratio = self.prior[i] / (self.prior[i] + 1.0);
            let theta = prior_ratio * (1.0f32 + self.post[i]);
            let mm = hypergeom_gain(theta);
            let product = prior_ratio * mm;
            self.gain[i] = if 1.0 < product { 1.0 } else { product };
            self.old_ps[i] = 0.2f32 * self.old_ps[i] + (0.8f32 * (self.gain[i] * self.gain[i])) * self.ps[i];
            let p1 = 0.199f32 + 0.8f32 * qcurve(self.zeta[i]);
            let q = 1.0f32 - pframe * p1;
            self.gain2[i] = (1.0f64 / (1.0f64 + d((q / (1.0f32 - q)) * (1.0f32 + self.prior[i])) * (-d(theta)).exp())) as f32;
        }
        {
            let (linear, bands) = self.gain2.split_at_mut(n);
            self.bank.compute_psd16(bands, linear);
            let (linear, bands) = self.gain.split_at_mut(n);
            self.bank.compute_psd16(bands, linear);
            let (linear, bands) = self.gain_floor.split_at_mut(n);
            self.bank.compute_psd16(bands, linear);
        }

        // Linear-frequency gain, constrained near the Bark gain.
        for i in 0..n {
            let prior_ratio = self.prior[i] / (self.prior[i] + 1.0);
            let theta = prior_ratio * (1.0f32 + self.post[i]);
            let mm = hypergeom_gain(theta);
            let product = prior_ratio * mm;
            let mut g = if 1.0 < product { 1.0 } else { product };
            let p = self.gain2[i];
            if 0.333f32 * g > self.gain[i] {
                g = 3.0f32 * self.gain[i];
            }
            self.gain[i] = g;
            self.old_ps[i] = 0.2f32 * self.old_ps[i] + (0.8f32 * (self.gain[i] * self.gain[i])) * self.ps[i];
            if self.gain[i] < self.gain_floor[i] {
                self.gain[i] = self.gain_floor[i];
            }
            // Loudness-domain MMSE: gain2 = [p sqrt(gain) + (1-p) sqrt(floor)]^2.
            let tmp =
                (d(p) * d(self.gain[i]).sqrt() + d(1.0f32 - p) * d(self.gain_floor[i]).sqrt()) as f32;
            self.gain2[i] = tmp * tmp;
        }
        if !self.denoise_enabled {
            self.gain2.fill(1.0);
        }

        for i in 1..n {
            self.ft[2 * i - 1] *= self.gain2[i];
            self.ft[2 * i] *= self.gain2[i];
        }
        self.ft[0] *= self.gain2[0];
        self.ft[2 * n - 1] *= self.gain2[n - 1];

        // spx_ifft: copy, then the unscaled backward transform.
        self.frame.copy_from_slice(&self.ft);
        self.fft.backward(&mut self.frame);
        for (sample, &weight) in self.frame.iter_mut().zip(&self.window) {
            *sample *= weight;
        }
        for i in 0..n3 {
            x[i] = word2int(self.outbuf[i] + self.frame[i]);
        }
        for i in 0..n4 {
            x[n3 + i] = self.frame[n3 + i] as i16;
        }
        for i in 0..n3 {
            self.outbuf[i] = self.frame[self.frame_size + i];
        }
        self.speech_prob = pframe;
        true
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn suppresses_stationary_noise() {
        let mut state = SpeexPreprocessor::new(480, 48000);
        state.set_noise_suppress(-30);
        let mut seed = 7u32;
        let mut energy_in = 0.0f64;
        let mut energy_out = 0.0f64;
        for frame in 0..400 {
            let mut block = [0i16; 480];
            for sample in &mut block {
                seed = seed.wrapping_mul(1_103_515_245).wrapping_add(12345);
                *sample = ((seed >> 16) as i16) >> 4;
            }
            let input = block;
            state.run(&mut block);
            if frame > 200 {
                energy_in += input.iter().map(|&v| f64::from(v) * f64::from(v)).sum::<f64>();
                energy_out += block.iter().map(|&v| f64::from(v) * f64::from(v)).sum::<f64>();
            }
        }
        let db = 10.0 * (energy_out / energy_in).log10();
        assert!(db < -15.0, "suppression {db:.1} dB");
    }

    #[test]
    fn state_copy_continues_identically() {
        let mut a = SpeexPreprocessor::new(480, 48000);
        let mut block = [0i16; 480];
        for (i, sample) in block.iter_mut().enumerate() {
            *sample = ((i as f32 * 0.07).sin() * 8000.0) as i16;
        }
        for _ in 0..10 {
            let mut copy = block;
            a.run(&mut copy);
        }
        let mut b = SpeexPreprocessor::new(480, 48000);
        b.copy_state_from(&a);
        let (mut x, mut y) = (block, block);
        a.run(&mut x);
        b.run(&mut y);
        assert_eq!(x, y);
    }
}
