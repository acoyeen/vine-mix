//! Pitch analysis for RNNoise: a port of the floating-point paths of
//! `pitch.c`, `pitch.h` and `celt_lpc.c` from RNNoise v0.2 (derived from
//! Opus/CELT; Copyright Xiph.Org, Octasic, Jean-Marc Valin; BSD licence).

use super::{PITCH_BUF_SIZE, PITCH_FRAME_SIZE, PITCH_MAX_PERIOD};

fn xcorr_kernel(x: &[f32], y: &[f32], sum: &mut [f32; 4], len: usize) {
    let mut xi = 0;
    let mut yi = 0;
    let mut y_0 = y[yi];
    let mut y_1 = y[yi + 1];
    let mut y_2 = y[yi + 2];
    yi += 3;
    let mut y_3 = 0.0f32;
    let mut j = 0;
    while j + 3 < len {
        let mut tmp = x[xi];
        y_3 = y[yi];
        sum[0] += tmp * y_0;
        sum[1] += tmp * y_1;
        sum[2] += tmp * y_2;
        sum[3] += tmp * y_3;
        tmp = x[xi + 1];
        y_0 = y[yi + 1];
        sum[0] += tmp * y_1;
        sum[1] += tmp * y_2;
        sum[2] += tmp * y_3;
        sum[3] += tmp * y_0;
        tmp = x[xi + 2];
        y_1 = y[yi + 2];
        sum[0] += tmp * y_2;
        sum[1] += tmp * y_3;
        sum[2] += tmp * y_0;
        sum[3] += tmp * y_1;
        tmp = x[xi + 3];
        y_2 = y[yi + 3];
        sum[0] += tmp * y_3;
        sum[1] += tmp * y_0;
        sum[2] += tmp * y_1;
        sum[3] += tmp * y_2;
        xi += 4;
        yi += 4;
        j += 4;
    }
    if j < len {
        let tmp = x[xi];
        xi += 1;
        y_3 = y[yi];
        yi += 1;
        sum[0] += tmp * y_0;
        sum[1] += tmp * y_1;
        sum[2] += tmp * y_2;
        sum[3] += tmp * y_3;
    }
    j += 1;
    if j < len {
        let tmp = x[xi];
        xi += 1;
        y_0 = y[yi];
        yi += 1;
        sum[0] += tmp * y_1;
        sum[1] += tmp * y_2;
        sum[2] += tmp * y_3;
        sum[3] += tmp * y_0;
    }
    j += 1;
    if j < len {
        let tmp = x[xi];
        y_1 = y[yi];
        sum[0] += tmp * y_2;
        sum[1] += tmp * y_3;
        sum[2] += tmp * y_0;
        sum[3] += tmp * y_1;
    }
}

#[inline]
fn inner_prod(x: &[f32], y: &[f32], n: usize) -> f32 {
    let mut xy = 0.0f32;
    for i in 0..n {
        xy += x[i] * y[i];
    }
    xy
}

#[inline]
fn dual_inner_prod(x: &[f32], y01: &[f32], y02: &[f32], n: usize) -> (f32, f32) {
    let (mut xy01, mut xy02) = (0.0f32, 0.0f32);
    for i in 0..n {
        xy01 += x[i] * y01[i];
        xy02 += x[i] * y02[i];
    }
    (xy01, xy02)
}

fn pitch_xcorr(x: &[f32], y: &[f32], xcorr: &mut [f32], len: usize, max_pitch: usize) {
    let mut i = 0;
    while i + 3 < max_pitch {
        let mut sum = [0.0f32; 4];
        xcorr_kernel(x, &y[i..], &mut sum, len);
        xcorr[i..i + 4].copy_from_slice(&sum);
        i += 4;
    }
    while i < max_pitch {
        xcorr[i] = inner_prod(x, &y[i..], len);
        i += 1;
    }
}

fn find_best_pitch(xcorr: &[f32], y: &[f32], len: usize, max_pitch: usize) -> [usize; 2] {
    let mut syy = 1.0f32;
    let mut best_num = [-1.0f32; 2];
    let mut best_den = [0.0f32; 2];
    let mut best_pitch = [0usize, 1];
    for value in y.iter().take(len) {
        syy += value * value;
    }
    for i in 0..max_pitch {
        if xcorr[i] > 0.0 {
            // Avoids both underflow and overflow when squaring.
            let xcorr16 = xcorr[i] * 1e-12f32;
            let num = xcorr16 * xcorr16;
            if num * best_den[1] > best_num[1] * syy {
                if num * best_den[0] > best_num[0] * syy {
                    best_num[1] = best_num[0];
                    best_den[1] = best_den[0];
                    best_pitch[1] = best_pitch[0];
                    best_num[0] = num;
                    best_den[0] = syy;
                    best_pitch[0] = i;
                } else {
                    best_num[1] = num;
                    best_den[1] = syy;
                    best_pitch[1] = i;
                }
            }
        }
        syy += y[i + len] * y[i + len] - y[i] * y[i];
        syy = if 1.0 > syy { 1.0 } else { syy };
    }
    best_pitch
}

fn autocorr(x: &[f32], ac: &mut [f32; 5], lag: usize, n: usize) {
    let fast_n = n - lag;
    pitch_xcorr(x, x, ac, fast_n, lag + 1);
    for k in 0..=lag {
        let mut d = 0.0f32;
        for i in k + fast_n..n {
            d += x[i] * x[i - k];
        }
        ac[k] += d;
    }
}

fn lpc(out: &mut [f32; 4], ac: &[f32; 5], p: usize) {
    let mut error = ac[0];
    out.fill(0.0);
    if ac[0] != 0.0 {
        for i in 0..p {
            let mut rr = 0.0f32;
            for j in 0..i {
                rr += out[j] * ac[i - j];
            }
            rr += ac[i + 1];
            let r = -rr / error;
            out[i] = r;
            for j in 0..(i + 1) >> 1 {
                let tmp1 = out[j];
                let tmp2 = out[i - 1 - j];
                out[j] = tmp1 + r * tmp2;
                out[i - 1 - j] = tmp2 + r * tmp1;
            }
            error -= r * r * error;
            if error < 0.001f32 * ac[0] {
                break;
            }
        }
    }
}

fn fir5(x: &mut [f32], num: &[f32; 5], n: usize, mem: &mut [f32; 5]) {
    let [num0, num1, num2, num3, num4] = *num;
    let [mut mem0, mut mem1, mut mem2, mut mem3, mut mem4] = *mem;
    for value in x.iter_mut().take(n) {
        let mut sum = *value;
        sum += num0 * mem0;
        sum += num1 * mem1;
        sum += num2 * mem2;
        sum += num3 * mem3;
        sum += num4 * mem4;
        mem4 = mem3;
        mem3 = mem2;
        mem2 = mem1;
        mem1 = mem0;
        mem0 = *value;
        *value = sum;
    }
    *mem = [mem0, mem1, mem2, mem3, mem4];
}

/// `rnn_pitch_downsample` for one channel.
pub fn pitch_downsample(x: &[f32], x_lp: &mut [f32], len: usize) {
    for i in 1..len >> 1 {
        x_lp[i] = 0.5f32 * (0.5f32 * (x[2 * i - 1] + x[2 * i + 1]) + x[2 * i]);
    }
    x_lp[0] = 0.5f32 * (0.5f32 * x[1] + x[0]);
    let mut ac = [0.0f32; 5];
    autocorr(x_lp, &mut ac, 4, len >> 1);
    ac[0] *= 1.0001f32;
    for (i, value) in ac.iter_mut().enumerate().skip(1) {
        let i = i as f32;
        *value -= *value * (0.008f32 * i) * (0.008f32 * i);
    }
    let mut coefficients = [0.0f32; 4];
    lpc(&mut coefficients, &ac, 4);
    let mut tmp = 1.0f32;
    for coefficient in &mut coefficients {
        tmp *= 0.9f32;
        *coefficient *= tmp;
    }
    let c1 = 0.8f32;
    let lpc2 = [
        coefficients[0] + 0.8f32,
        coefficients[1] + c1 * coefficients[0],
        coefficients[2] + c1 * coefficients[1],
        coefficients[3] + c1 * coefficients[2],
        c1 * coefficients[3],
    ];
    let mut mem = [0.0f32; 5];
    fir5(x_lp, &lpc2, len >> 1, &mut mem);
}

/// `rnn_pitch_search`: returns the pitch lag in samples of `y`.
pub fn pitch_search(x_lp: &[f32], y: &[f32], len: usize, max_pitch: usize) -> usize {
    let lag = len + max_pitch;
    let mut x_lp4 = [0.0f32; PITCH_FRAME_SIZE >> 2];
    let mut y_lp4 = [0.0f32; (PITCH_FRAME_SIZE + PITCH_MAX_PERIOD) >> 2];
    let mut xcorr = [0.0f32; PITCH_MAX_PERIOD >> 1];
    for j in 0..len >> 2 {
        x_lp4[j] = x_lp[2 * j];
    }
    for j in 0..lag >> 2 {
        y_lp4[j] = y[2 * j];
    }
    pitch_xcorr(&x_lp4, &y_lp4, &mut xcorr, len >> 2, max_pitch >> 2);
    let best_pitch = find_best_pitch(&xcorr, &y_lp4, len >> 2, max_pitch >> 2);
    for i in 0..max_pitch >> 1 {
        xcorr[i] = 0.0;
        let far = |b: usize| (i as isize - 2 * b as isize).abs() > 2;
        if far(best_pitch[0]) && far(best_pitch[1]) {
            continue;
        }
        let sum = inner_prod(x_lp, &y[i..], len >> 1);
        xcorr[i] = if -1.0 > sum { -1.0 } else { sum };
    }
    let best_pitch = find_best_pitch(&xcorr, y, len >> 1, max_pitch >> 1);
    let offset: isize = if best_pitch[0] > 0 && best_pitch[0] < (max_pitch >> 1) - 1 {
        let a = xcorr[best_pitch[0] - 1];
        let b = xcorr[best_pitch[0]];
        let c = xcorr[best_pitch[0] + 1];
        if (c - a) > 0.7f32 * (b - a) {
            1
        } else if (a - c) > 0.7f32 * (b - c) {
            -1
        } else {
            0
        }
    } else {
        0
    };
    (2 * best_pitch[0] as isize - offset) as usize
}

fn compute_pitch_gain(xy: f32, xx: f32, yy: f32) -> f32 {
    (f64::from(xy) / f64::from(1.0f32 + xx * yy).sqrt()) as f32
}

const SECOND_CHECK: [usize; 16] = [0, 0, 3, 2, 3, 2, 5, 2, 3, 2, 3, 2, 5, 2, 3, 2];

/// `rnn_remove_doubling`. `x` is the downsampled buffer; `t0` is updated.
pub fn remove_doubling(
    x_full: &[f32],
    maxperiod: usize,
    minperiod: usize,
    n: usize,
    t0: &mut usize,
    prev_period: usize,
    prev_gain: f32,
) -> f32 {
    let minperiod0 = minperiod;
    let maxperiod = maxperiod / 2;
    let minperiod = minperiod / 2;
    *t0 /= 2;
    let prev_period = (prev_period / 2) as isize;
    let n = n / 2;
    // `x` points maxperiod samples into the buffer; negative offsets reach back.
    let base = maxperiod;
    let at = |offset: isize| x_full[(base as isize + offset) as usize];
    let slice_from = |offset: isize| &x_full[(base as isize + offset) as usize..];
    if *t0 >= maxperiod {
        *t0 = maxperiod - 1;
    }
    let mut t = *t0;
    let t0v = *t0;
    let (xx, mut xy) = dual_inner_prod(slice_from(0), slice_from(0), slice_from(-(t0v as isize)), n);
    let mut yy_lookup = [0.0f32; PITCH_MAX_PERIOD + 1];
    yy_lookup[0] = xx;
    let mut yy = xx;
    for i in 1..=maxperiod as isize {
        yy = yy + at(-i) * at(-i) - at(n as isize - i) * at(n as isize - i);
        yy_lookup[i as usize] = if 0.0 > yy { 0.0 } else { yy };
    }
    yy = yy_lookup[t0v];
    let mut best_xy = xy;
    let mut best_yy = yy;
    let g0 = compute_pitch_gain(xy, xx, yy);
    let mut g = g0;
    for k in 2..=15usize {
        let t1 = (2 * t0v + k) / (2 * k);
        if t1 < minperiod {
            break;
        }
        let t1b = if k == 2 {
            if t1 + t0v > maxperiod { t0v } else { t0v + t1 }
        } else {
            (2 * SECOND_CHECK[k] * t0v + k) / (2 * k)
        };
        let (xy1, xy2) = dual_inner_prod(slice_from(0), slice_from(-(t1 as isize)), slice_from(-(t1b as isize)), n);
        xy = 0.5f32 * (xy1 + xy2);
        yy = 0.5f32 * (yy_lookup[t1] + yy_lookup[t1b]);
        let g1 = compute_pitch_gain(xy, xx, yy);
        let distance = (t1 as isize - prev_period).abs();
        let cont = if distance <= 1 {
            prev_gain
        } else if distance <= 2 && 5 * k * k < t0v {
            0.5f32 * prev_gain
        } else {
            0.0
        };
        let max16 = |a: f32, b: f32| if a > b { a } else { b };
        let mut thresh = max16(0.3f32, 0.7f32 * g0 - cont);
        // Bias against very short periods to avoid false positives from
        // short-term correlation.
        if t1 < 3 * minperiod {
            thresh = max16(0.4f32, 0.85f32 * g0 - cont);
        } else if t1 < 2 * minperiod {
            thresh = max16(0.5f32, 0.9f32 * g0 - cont);
        }
        if g1 > thresh {
            best_xy = xy;
            best_yy = yy;
            t = t1;
            g = g1;
        }
    }
    best_xy = if 0.0 > best_xy { 0.0 } else { best_xy };
    let mut pg = if best_yy <= best_xy { 1.0 } else { best_xy / (best_yy + 1.0) };
    let mut xcorr = [0.0f32; 3];
    for (k, value) in xcorr.iter_mut().enumerate() {
        *value = inner_prod(slice_from(0), slice_from(-((t + k) as isize - 1)), n);
    }
    let offset: isize = if (xcorr[2] - xcorr[0]) > 0.7f32 * (xcorr[1] - xcorr[0]) {
        1
    } else if (xcorr[0] - xcorr[2]) > 0.7f32 * (xcorr[1] - xcorr[2]) {
        -1
    } else {
        0
    };
    if pg > g {
        pg = g;
    }
    let mut result = 2 * t as isize + offset;
    if result < minperiod0 as isize {
        result = minperiod0 as isize;
    }
    *t0 = result as usize;
    pg
}

/// Downsampled pitch buffer length.
pub const DOWNSAMPLED: usize = PITCH_BUF_SIZE >> 1;
