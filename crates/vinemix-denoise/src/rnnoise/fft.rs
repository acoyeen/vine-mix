//! The 960-point complex FFT RNNoise uses: a port of the floating-point
//! `kiss_fft.c` from RNNoise v0.2 (originally Opus/CELT's KISS FFT, BSD),
//! driven by the upstream precomputed bit-reverse and twiddle tables.

use super::tables::{FFT_BITREV, FFT_FACTORS, FFT_SCALE, FFT_TWIDDLES};

pub const NFFT: usize = 960;

#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Complex {
    pub r: f32,
    pub i: f32,
}

impl Complex {
    pub const ZERO: Self = Self { r: 0.0, i: 0.0 };

    #[inline]
    fn mul(a: Self, b: (f32, f32)) -> Self {
        Self { r: a.r * b.0 - a.i * b.1, i: a.r * b.1 + a.i * b.0 }
    }

    #[inline]
    fn add(a: Self, b: Self) -> Self {
        Self { r: a.r + b.r, i: a.i + b.i }
    }

    #[inline]
    fn sub(a: Self, b: Self) -> Self {
        Self { r: a.r - b.r, i: a.i - b.i }
    }
}

fn bfly2(fout: &mut [Complex], n: usize) {
    let tw: f32 = 0.707_106_781_2;
    let mut base = 0;
    for _ in 0..n {
        let f = &mut fout[base..base + 8];
        let mut t = f[4];
        f[4] = Complex::sub(f[0], t);
        f[0] = Complex::add(f[0], t);
        t = Complex { r: (f[5].r + f[5].i) * tw, i: (f[5].i - f[5].r) * tw };
        f[5] = Complex::sub(f[1], t);
        f[1] = Complex::add(f[1], t);
        t = Complex { r: f[6].i, i: -f[6].r };
        f[6] = Complex::sub(f[2], t);
        f[2] = Complex::add(f[2], t);
        t = Complex { r: (f[7].i - f[7].r) * tw, i: -(f[7].i + f[7].r) * tw };
        f[7] = Complex::sub(f[3], t);
        f[3] = Complex::add(f[3], t);
        base += 8;
    }
}

fn bfly4(fout: &mut [Complex], fstride: usize, m: usize, n: usize, mm: usize) {
    if m == 1 {
        let mut base = 0;
        for _ in 0..n {
            let f = &mut fout[base..base + 4];
            let scratch0 = Complex::sub(f[0], f[2]);
            f[0] = Complex::add(f[0], f[2]);
            let mut scratch1 = Complex::add(f[1], f[3]);
            f[2] = Complex::sub(f[0], scratch1);
            f[0] = Complex::add(f[0], scratch1);
            scratch1 = Complex::sub(f[1], f[3]);
            f[1] = Complex { r: scratch0.r + scratch1.i, i: scratch0.i - scratch1.r };
            f[3] = Complex { r: scratch0.r - scratch1.i, i: scratch0.i + scratch1.r };
            base += 4;
        }
        return;
    }
    let (m2, m3) = (2 * m, 3 * m);
    for i in 0..n {
        let start = i * mm;
        let (mut tw1, mut tw2, mut tw3) = (0, 0, 0);
        for j in 0..m {
            let k = start + j;
            let s0 = Complex::mul(fout[k + m], FFT_TWIDDLES[tw1]);
            let s1 = Complex::mul(fout[k + m2], FFT_TWIDDLES[tw2]);
            let s2 = Complex::mul(fout[k + m3], FFT_TWIDDLES[tw3]);
            let s5 = Complex::sub(fout[k], s1);
            fout[k] = Complex::add(fout[k], s1);
            let s3 = Complex::add(s0, s2);
            let s4 = Complex::sub(s0, s2);
            fout[k + m2] = Complex::sub(fout[k], s3);
            tw1 += fstride;
            tw2 += fstride * 2;
            tw3 += fstride * 3;
            fout[k] = Complex::add(fout[k], s3);
            fout[k + m] = Complex { r: s5.r + s4.i, i: s5.i - s4.r };
            fout[k + m3] = Complex { r: s5.r - s4.i, i: s5.i + s4.r };
        }
    }
}

fn bfly3(fout: &mut [Complex], fstride: usize, m: usize, n: usize, mm: usize) {
    let m2 = 2 * m;
    let epi3 = FFT_TWIDDLES[fstride * m];
    for i in 0..n {
        let start = i * mm;
        let (mut tw1, mut tw2) = (0, 0);
        for j in 0..m {
            let k = start + j;
            let s1 = Complex::mul(fout[k + m], FFT_TWIDDLES[tw1]);
            let s2 = Complex::mul(fout[k + m2], FFT_TWIDDLES[tw2]);
            let s3 = Complex::add(s1, s2);
            let mut s0 = Complex::sub(s1, s2);
            tw1 += fstride;
            tw2 += fstride * 2;
            fout[k + m] = Complex { r: fout[k].r - s3.r * 0.5, i: fout[k].i - s3.i * 0.5 };
            s0.r *= epi3.1;
            s0.i *= epi3.1;
            fout[k] = Complex::add(fout[k], s3);
            fout[k + m2] = Complex { r: fout[k + m].r + s0.i, i: fout[k + m].i - s0.r };
            fout[k + m] = Complex { r: fout[k + m].r - s0.i, i: fout[k + m].i + s0.r };
        }
    }
}

fn bfly5(fout: &mut [Complex], fstride: usize, m: usize, n: usize, mm: usize) {
    let ya = FFT_TWIDDLES[fstride * m];
    let yb = FFT_TWIDDLES[fstride * 2 * m];
    for i in 0..n {
        let start = i * mm;
        for u in 0..m {
            let (k0, k1, k2, k3, k4) = (start + u, start + m + u, start + 2 * m + u, start + 3 * m + u, start + 4 * m + u);
            let s0 = fout[k0];
            let s1 = Complex::mul(fout[k1], FFT_TWIDDLES[u * fstride]);
            let s2 = Complex::mul(fout[k2], FFT_TWIDDLES[2 * u * fstride]);
            let s3 = Complex::mul(fout[k3], FFT_TWIDDLES[3 * u * fstride]);
            let s4 = Complex::mul(fout[k4], FFT_TWIDDLES[4 * u * fstride]);
            let s7 = Complex::add(s1, s4);
            let s10 = Complex::sub(s1, s4);
            let s8 = Complex::add(s2, s3);
            let s9 = Complex::sub(s2, s3);
            fout[k0].r += s7.r + s8.r;
            fout[k0].i += s7.i + s8.i;
            let s5 = Complex { r: s0.r + (s7.r * ya.0 + s8.r * yb.0), i: s0.i + (s7.i * ya.0 + s8.i * yb.0) };
            let s6 = Complex { r: s10.i * ya.1 + s9.i * yb.1, i: -(s10.r * ya.1 + s9.r * yb.1) };
            fout[k1] = Complex::sub(s5, s6);
            fout[k4] = Complex::add(s5, s6);
            let s11 = Complex { r: s0.r + (s7.r * yb.0 + s8.r * ya.0), i: s0.i + (s7.i * yb.0 + s8.i * ya.0) };
            let s12 = Complex { r: s9.i * ya.1 - s10.i * yb.1, i: s10.r * yb.1 - s9.r * ya.1 };
            fout[k2] = Complex::add(s11, s12);
            fout[k3] = Complex::sub(s11, s12);
        }
    }
}

/// Forward FFT of `input` into `output` with 1/N scaling (`rnn_fft_c`).
pub fn fft(input: &[Complex; NFFT], output: &mut [Complex; NFFT]) {
    for (i, x) in input.iter().enumerate() {
        output[FFT_BITREV[i] as usize] = Complex { r: FFT_SCALE * x.r, i: FFT_SCALE * x.i };
    }
    let mut fstride = [0usize; 6];
    fstride[0] = 1;
    let mut stages = 0;
    loop {
        let p = FFT_FACTORS[2 * stages];
        let m = FFT_FACTORS[2 * stages + 1];
        fstride[stages + 1] = fstride[stages] * p;
        stages += 1;
        if m == 1 {
            break;
        }
    }
    let mut m = FFT_FACTORS[2 * stages - 1];
    for i in (0..stages).rev() {
        let m2 = if i != 0 { FFT_FACTORS[2 * i - 1] } else { 1 };
        match FFT_FACTORS[2 * i] {
            2 => bfly2(output, fstride[i]),
            4 => bfly4(output, fstride[i], m, fstride[i], m2),
            3 => bfly3(output, fstride[i], m, fstride[i], m2),
            5 => bfly5(output, fstride[i], m, fstride[i], m2),
            _ => unreachable!("unsupported radix"),
        }
        m = m2;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn matches_a_direct_dft() {
        let mut input = [Complex::ZERO; NFFT];
        for (n, x) in input.iter_mut().enumerate() {
            x.r = ((n * 37) % 23) as f32 / 11.0 - 1.0;
            x.i = ((n * 11) % 7) as f32 / 3.0 - 1.0;
        }
        let mut output = [Complex::ZERO; NFFT];
        fft(&input, &mut output);
        for k in [0usize, 1, 7, 100, 480, 959] {
            let (mut re, mut im) = (0.0f64, 0.0f64);
            for (n, x) in input.iter().enumerate() {
                let angle = -2.0 * std::f64::consts::PI * ((k * n) % NFFT) as f64 / NFFT as f64;
                re += f64::from(x.r) * angle.cos() - f64::from(x.i) * angle.sin();
                im += f64::from(x.r) * angle.sin() + f64::from(x.i) * angle.cos();
            }
            re /= NFFT as f64;
            im /= NFFT as f64;
            assert!((f64::from(output[k].r) - re).abs() < 1e-5, "bin {k}");
            assert!((f64::from(output[k].i) - im).abs() < 1e-5, "bin {k}");
        }
    }
}
