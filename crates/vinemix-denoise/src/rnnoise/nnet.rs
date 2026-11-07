//! RNNoise v0.2 network inference: a port of `nnet.c`, `rnn.c` and the x86
//! kernels of `vec_avx.h` (BSD licence, Xiph.Org and contributors).
//!
//! Upstream selects a kernel family at run time (`rnn_select_arch`): an
//! SSE2 build for any x86-64 CPU and an AVX2 + FMA build where available.
//! The families differ in rounding (fused multiply-add) and both use the CPU's
//! approximate reciprocal instruction inside tanh/sigmoid, so this port
//! implements both with the same instructions and the same selection rule.
//! On a given machine it therefore produces the same values as the C library.

use std::sync::LazyLock;

/// Model blob generated from rnnoise_data-0b50c45 by
/// `tools/generate-rnnoise-data.py`.
static BLOB: &[u8] = include_bytes!("../../model/rnnoise-0b50c45.bin");

pub const CONV1_IN_SIZE: usize = 65;
pub const CONV1_OUT_SIZE: usize = 128;
pub const CONV1_STATE_SIZE: usize = 65 * 2;
pub const CONV2_IN_SIZE: usize = 128;
pub const CONV2_OUT_SIZE: usize = 384;
pub const CONV2_STATE_SIZE: usize = 128 * 2;
pub const GRU_SIZE: usize = 384;
pub const DENSE_OUT_SIZE: usize = 32;

/// Inference kernel family (upstream `arch`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Kernel {
    /// SSE2 baseline: separate multiply and add.
    Sse2,
    /// AVX2 with fused multiply-add.
    Avx2Fma,
}

impl Kernel {
    /// The family upstream selects on this CPU: AVX2 requires SSE4.1, AVX,
    /// AVX2 and FMA.
    pub fn detect() -> Self {
        #[cfg(target_arch = "x86_64")]
        {
            if std::arch::is_x86_feature_detected!("sse4.1")
                && std::arch::is_x86_feature_detected!("avx")
                && std::arch::is_x86_feature_detected!("avx2")
                && std::arch::is_x86_feature_detected!("fma")
            {
                return Kernel::Avx2Fma;
            }
        }
        Kernel::Sse2
    }

    /// Whether `kernel` can run on this CPU.
    pub fn available(self) -> bool {
        match self {
            Kernel::Sse2 => true,
            Kernel::Avx2Fma => Kernel::detect() == Kernel::Avx2Fma,
        }
    }
}

#[derive(Debug)]
pub struct Linear {
    bias: Vec<f32>,
    weights: Vec<f32>,
    idx: Option<Vec<i32>>,
    diag: Option<Vec<f32>>,
    inputs: usize,
    outputs: usize,
}

#[derive(Debug)]
pub struct Model {
    conv1: Linear,
    conv2: Linear,
    gru: [(Linear, Linear); 3],
    dense_out: Linear,
    vad_dense: Linear,
}

enum Array {
    Float(Vec<f32>),
    Int(Vec<i32>),
}

fn parse_blob(blob: &[u8]) -> std::collections::HashMap<String, Array> {
    let mut arrays = std::collections::HashMap::new();
    assert_eq!(&blob[..4], b"VMRN", "invalid RNNoise model blob");
    let word = |at: usize| u32::from_le_bytes(blob[at..at + 4].try_into().expect("four bytes"));
    assert_eq!(word(4), 1, "unsupported RNNoise model blob version");
    let count = word(8) as usize;
    let mut at = 12;
    for _ in 0..count {
        let length = blob[at] as usize;
        let name = std::str::from_utf8(&blob[at + 1..at + 1 + length]).expect("ASCII name").to_owned();
        at += 1 + length;
        let kind = blob[at];
        let size = word(at + 1) as usize;
        at += 5;
        let words = blob[at..at + 4 * size].chunks_exact(4).map(|c| u32::from_le_bytes(c.try_into().expect("word")));
        let array = if kind == 0 {
            Array::Float(words.map(f32::from_bits).collect())
        } else {
            Array::Int(words.map(|w| w as i32).collect())
        };
        at += 4 * size;
        arrays.insert(name, array);
    }
    arrays
}

impl Model {
    fn load() -> Self {
        let mut arrays = parse_blob(BLOB);
        let mut float = |name: &str| match arrays.remove(name) {
            Some(Array::Float(values)) => values,
            _ => panic!("RNNoise model array {name} missing"),
        };
        let mut layers = Vec::new();
        for (prefix, inputs, outputs, sparse) in [
            ("conv1", 195, 128, false),
            ("conv2", 384, 384, false),
            ("gru1_input", 384, 1152, true),
            ("gru1_recurrent", 384, 1152, true),
            ("gru2_input", 384, 1152, true),
            ("gru2_recurrent", 384, 1152, true),
            ("gru3_input", 384, 1152, true),
            ("gru3_recurrent", 384, 1152, true),
            ("dense_out", 384, 32, false),
            ("vad_dense", 384, 1, false),
        ] {
            let bias = float(&format!("{prefix}_bias"));
            let weights = float(&format!("{prefix}_weights_float"));
            let diag = prefix.ends_with("recurrent").then(|| float(&format!("{prefix}_weights_diag")));
            layers.push((prefix, bias, weights, diag, inputs, outputs, sparse));
        }
        let mut built = layers
            .into_iter()
            .map(|(prefix, bias, weights, diag, inputs, outputs, sparse)| {
                let idx = sparse.then(|| match arrays.remove(&format!("{prefix}_weights_idx")) {
                    Some(Array::Int(values)) => values,
                    _ => panic!("RNNoise model index {prefix} missing"),
                });
                assert_eq!(bias.len(), outputs);
                if idx.is_none() {
                    assert_eq!(weights.len(), inputs * outputs, "{prefix}");
                }
                Linear { bias, weights, idx, diag, inputs, outputs }
            })
            .collect::<Vec<_>>()
            .into_iter();
        let mut next = || built.next().expect("ten layers");
        Model {
            conv1: next(),
            conv2: next(),
            gru: [(next(), next()), (next(), next()), (next(), next())],
            dense_out: next(),
            vad_dense: next(),
        }
    }
}

/// The embedded model, parsed once and shared by every denoiser.
pub static MODEL: LazyLock<Model> = LazyLock::new(Model::load);

// ------------------------------------------------------------- kernels --

/// Dense column-major product; each output accumulates over `j` in order.
/// Rows are processed 16 at a time with the accumulators held in registers
/// (as upstream's `sgemv16x1`); the per-element arithmetic and its order are
/// unchanged, so results stay bit-exact.
#[inline(always)]
fn sgemv_blocked<const FMA: bool>(out: &mut [f32], weights: &[f32], rows: usize, cols: usize, col_stride: usize, x: &[f32]) {
    let step = |w: f32, xj: f32, y: f32| if FMA { w.mul_add(xj, y) } else { w * xj + y };
    let mut i = 0;
    while i + 16 <= rows {
        let mut acc = [0.0f32; 16];
        for (j, &xj) in x.iter().enumerate().take(cols) {
            let column: &[f32; 16] = weights[j * col_stride + i..j * col_stride + i + 16].try_into().expect("16 rows");
            for (y, &w) in acc.iter_mut().zip(column) {
                *y = step(w, xj, *y);
            }
        }
        out[i..i + 16].copy_from_slice(&acc);
        i += 16;
    }
    for (row, y) in out.iter_mut().enumerate().take(rows).skip(i) {
        let mut sum = 0.0f32;
        for (j, &xj) in x.iter().enumerate().take(cols) {
            sum = step(weights[j * col_stride + row], xj, sum);
        }
        *y = sum;
    }
}

fn sgemv_sse(out: &mut [f32], weights: &[f32], rows: usize, cols: usize, col_stride: usize, x: &[f32]) {
    sgemv_blocked::<false>(out, weights, rows, cols, col_stride, x);
}

/// Block-sparse 8x4 product (`sparse_sgemv8x4`): per 8-row block, a column
/// count, then column indices, each with a 4 x 8 weight tile. Fixed-size views
/// let the compiler keep the 8 accumulators in one vector register; the
/// arithmetic and its order per element are unchanged (bit-exact).
#[inline(always)]
fn sparse_blocked<const FMA: bool>(out: &mut [f32], weights: &[f32], idx: &[i32], rows: usize, x: &[f32]) {
    let mut w = 0;
    let mut k = 0;
    for block in out[..rows].chunks_exact_mut(8) {
        let mut acc = [0.0f32; 8];
        let cols = idx[k] as usize;
        k += 1;
        for &id in &idx[k..k + cols] {
            let id = id as usize;
            let xs: &[f32; 4] = x[id..id + 4].try_into().expect("four inputs");
            let tile: &[f32; 32] = weights[w..w + 32].try_into().expect("4 x 8 tile");
            for (t, &xj) in xs.iter().enumerate() {
                let lanes: &[f32; 8] = tile[t * 8..t * 8 + 8].try_into().expect("eight lanes");
                for (value, &wv) in acc.iter_mut().zip(lanes) {
                    *value = if FMA { wv.mul_add(xj, *value) } else { wv * xj + *value };
                }
            }
            w += 32;
        }
        k += cols;
        block.copy_from_slice(&acc);
    }
}

fn sparse_sgemv_sse(out: &mut [f32], weights: &[f32], idx: &[i32], rows: usize, x: &[f32]) {
    sparse_blocked::<false>(out, weights, idx, rows, x);
}

#[cfg(target_arch = "x86_64")]
#[target_feature(enable = "avx2,fma")]
fn sgemv_fma(out: &mut [f32], weights: &[f32], rows: usize, cols: usize, col_stride: usize, x: &[f32]) {
    sgemv_blocked::<true>(out, weights, rows, cols, col_stride, x);
}

#[cfg(target_arch = "x86_64")]
#[target_feature(enable = "avx2,fma")]
fn sparse_sgemv_fma(out: &mut [f32], weights: &[f32], idx: &[i32], rows: usize, x: &[f32]) {
    sparse_blocked::<true>(out, weights, idx, rows, x);
}

#[cfg(target_arch = "x86_64")]
#[target_feature(enable = "avx2,fma")]
fn diag_fma(out: &mut [f32], diag: &[f32], input: &[f32], m: usize) {
    for i in 0..m {
        out[i] = diag[i].mul_add(input[i], out[i]);
        out[i + m] = diag[i + m].mul_add(input[i], out[i + m]);
        out[i + 2 * m] = diag[i + 2 * m].mul_add(input[i], out[i + 2 * m]);
    }
}

fn compute_linear(kernel: Kernel, layer: &Linear, out: &mut [f32], input: &[f32]) {
    let (m, n) = (layer.inputs, layer.outputs);
    match (kernel, &layer.idx) {
        #[cfg(target_arch = "x86_64")]
        (Kernel::Avx2Fma, Some(idx)) => {
            // SAFETY: Avx2Fma is only selected when the CPU supports it.
            unsafe { sparse_sgemv_fma(out, &layer.weights, idx, n, input) }
        }
        #[cfg(target_arch = "x86_64")]
        (Kernel::Avx2Fma, None) => {
            // SAFETY: as above.
            unsafe { sgemv_fma(out, &layer.weights, n, m, n, input) }
        }
        (_, Some(idx)) => sparse_sgemv_sse(out, &layer.weights, idx, n, input),
        (_, None) => sgemv_sse(out, &layer.weights, n, m, n, input),
    }
    for (value, bias) in out[..n].iter_mut().zip(&layer.bias) {
        *value += bias;
    }
    if let Some(diag) = &layer.diag {
        // Diag is only used for GRU recurrent weights (3M == N). Compiled in
        // the AVX2 unit with FMA enabled, the compiler contracts this
        // multiply-add; the SSE2 unit has no FMA instruction.
        match kernel {
            #[cfg(target_arch = "x86_64")]
            Kernel::Avx2Fma => {
                // SAFETY: as above.
                unsafe { diag_fma(out, diag, input, m) }
            }
            _ => {
                for i in 0..m {
                    out[i] += diag[i] * input[i];
                    out[i + m] += diag[i + m] * input[i];
                    out[i + 2 * m] += diag[i + 2 * m] * input[i];
                }
            }
        }
    }
}

#[derive(Clone, Copy)]
enum Activation {
    Sigmoid,
    Tanh,
}

#[cfg(target_arch = "x86_64")]
mod approx {
    use std::arch::x86_64::*;

    #[inline]
    pub unsafe fn tanh4(x: __m128) -> __m128 {
        // SAFETY: SSE2 is part of the x86-64 baseline.
        unsafe {
            let x2 = _mm_mul_ps(x, x);
            let num = _mm_add_ps(
                _mm_mul_ps(_mm_add_ps(_mm_mul_ps(_mm_set1_ps(0.608_630_42), x2), _mm_set1_ps(96.392_356_87)), x2),
                _mm_set1_ps(952.528_015_14),
            );
            let den = _mm_add_ps(
                _mm_mul_ps(_mm_add_ps(_mm_mul_ps(_mm_set1_ps(11.886_009_22), x2), _mm_set1_ps(413.368_011_47)), x2),
                _mm_set1_ps(952.723_999_02),
            );
            let num = _mm_mul_ps(num, x);
            let den = _mm_rcp_ps(den);
            let num = _mm_mul_ps(num, den);
            _mm_max_ps(_mm_set1_ps(-1.0), _mm_min_ps(_mm_set1_ps(1.0), num))
        }
    }

    #[inline]
    pub unsafe fn sigmoid4(x: __m128) -> __m128 {
        // SAFETY: SSE2 is part of the x86-64 baseline.
        unsafe {
            let x2 = _mm_mul_ps(x, x);
            let num = _mm_add_ps(
                _mm_mul_ps(_mm_add_ps(_mm_mul_ps(_mm_set1_ps(0.009_509_85), x2), _mm_set1_ps(6.024_522_30)), x2),
                _mm_set1_ps(238.132_003_78),
            );
            let den = _mm_add_ps(
                _mm_mul_ps(_mm_add_ps(_mm_mul_ps(_mm_set1_ps(0.742_875_58), x2), _mm_set1_ps(103.342_002_87)), x2),
                _mm_set1_ps(952.723_999_02),
            );
            let num = _mm_mul_ps(num, x);
            let den = _mm_rcp_ps(den);
            let num = _mm_add_ps(_mm_mul_ps(num, den), _mm_set1_ps(0.5));
            _mm_max_ps(_mm_set1_ps(0.0), _mm_min_ps(_mm_set1_ps(1.0), num))
        }
    }

    #[target_feature(enable = "avx2,fma")]
    pub fn tanh8(x: __m256) -> __m256 {
        let x2 = _mm256_mul_ps(x, x);
        let num = _mm256_fmadd_ps(
            _mm256_fmadd_ps(_mm256_set1_ps(0.608_630_42), x2, _mm256_set1_ps(96.392_356_87)),
            x2,
            _mm256_set1_ps(952.528_015_14),
        );
        let den = _mm256_fmadd_ps(
            _mm256_fmadd_ps(_mm256_set1_ps(11.886_009_22), x2, _mm256_set1_ps(413.368_011_47)),
            x2,
            _mm256_set1_ps(952.723_999_02),
        );
        let num = _mm256_mul_ps(num, x);
        let den = _mm256_rcp_ps(den);
        let num = _mm256_mul_ps(num, den);
        _mm256_max_ps(_mm256_set1_ps(-1.0), _mm256_min_ps(_mm256_set1_ps(1.0), num))
    }

    #[target_feature(enable = "avx2,fma")]
    pub fn sigmoid8(x: __m256) -> __m256 {
        let x2 = _mm256_mul_ps(x, x);
        let num = _mm256_fmadd_ps(
            _mm256_fmadd_ps(_mm256_set1_ps(0.009_509_85), x2, _mm256_set1_ps(6.024_522_30)),
            x2,
            _mm256_set1_ps(238.132_003_78),
        );
        let den = _mm256_fmadd_ps(
            _mm256_fmadd_ps(_mm256_set1_ps(0.742_875_58), x2, _mm256_set1_ps(103.342_002_87)),
            x2,
            _mm256_set1_ps(952.723_999_02),
        );
        let num = _mm256_mul_ps(num, x);
        let den = _mm256_rcp_ps(den);
        let num = _mm256_fmadd_ps(num, den, _mm256_set1_ps(0.5));
        _mm256_max_ps(_mm256_set1_ps(0.0), _mm256_min_ps(_mm256_set1_ps(1.0), num))
    }

    /// `vec_tanh` / `vec_sigmoid`, SSE2 build.
    pub fn activate_sse(values: &mut [f32], sigmoid: bool) {
        let f = if sigmoid { sigmoid4 } else { tanh4 };
        let n = values.len();
        let mut i = 0;
        // SAFETY: unaligned loads/stores within bounds; SSE2 is baseline.
        unsafe {
            while i + 3 < n {
                let y = f(_mm_loadu_ps(values.as_ptr().add(i)));
                _mm_storeu_ps(values.as_mut_ptr().add(i), y);
                i += 4;
            }
            while i < n {
                values[i] = _mm_cvtss_f32(f(_mm_set1_ps(values[i])));
                i += 1;
            }
        }
    }

    /// `vec_tanh` / `vec_sigmoid`, AVX2 build.
    #[target_feature(enable = "avx2,fma")]
    pub fn activate_avx(values: &mut [f32], sigmoid: bool) {
        let n = values.len();
        let mut i = 0;
        while i + 7 < n {
            // SAFETY: unaligned loads/stores within bounds.
            unsafe {
                let x = _mm256_loadu_ps(values.as_ptr().add(i));
                let y = if sigmoid { sigmoid8(x) } else { tanh8(x) };
                _mm256_storeu_ps(values.as_mut_ptr().add(i), y);
            }
            i += 8;
        }
        while i < n {
            let x = _mm256_set1_ps(values[i]);
            let y = if sigmoid { sigmoid8(x) } else { tanh8(x) };
            values[i] = _mm256_cvtss_f32(y);
            i += 1;
        }
    }
}

fn activate(kernel: Kernel, values: &mut [f32], activation: Activation) {
    let sigmoid = matches!(activation, Activation::Sigmoid);
    #[cfg(target_arch = "x86_64")]
    match kernel {
        Kernel::Avx2Fma => {
            // SAFETY: Avx2Fma is only selected when the CPU supports it.
            unsafe { approx::activate_avx(values, sigmoid) }
        }
        Kernel::Sse2 => approx::activate_sse(values, sigmoid),
    }
    #[cfg(not(target_arch = "x86_64"))]
    {
        let _ = kernel;
        for value in values {
            *value = if sigmoid { 1.0 / (1.0 + (-*value).exp()) } else { value.tanh() };
        }
    }
}

/// Recurrent state of one RNNoise network instance.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct RnnState {
    conv1: [f32; CONV1_STATE_SIZE],
    conv2: [f32; CONV2_STATE_SIZE],
    gru: [[f32; GRU_SIZE]; 3],
}

impl Default for RnnState {
    fn default() -> Self {
        Self { conv1: [0.0; CONV1_STATE_SIZE], conv2: [0.0; CONV2_STATE_SIZE], gru: [[0.0; GRU_SIZE]; 3] }
    }
}

fn conv1d(kernel: Kernel, layer: &Linear, output: &mut [f32], mem: &mut [f32], input: &[f32], activation: Activation) {
    let mut tmp = [0.0f32; 1024];
    let history = layer.inputs - input.len();
    tmp[..history].copy_from_slice(&mem[..history]);
    tmp[history..layer.inputs].copy_from_slice(input);
    compute_linear(kernel, layer, output, &tmp[..layer.inputs]);
    activate(kernel, &mut output[..layer.outputs], activation);
    mem[..history].copy_from_slice(&tmp[input.len()..layer.inputs]);
}

fn gru(kernel: Kernel, input_weights: &Linear, recurrent_weights: &Linear, state: &mut [f32; GRU_SIZE], input: &[f32]) {
    let n = recurrent_weights.inputs;
    let mut zrh = [0.0f32; 3 * GRU_SIZE];
    let mut recur = [0.0f32; 3 * GRU_SIZE];
    compute_linear(kernel, input_weights, &mut zrh, input);
    compute_linear(kernel, recurrent_weights, &mut recur, state);
    for i in 0..2 * n {
        zrh[i] += recur[i];
    }
    activate(kernel, &mut zrh[..2 * n], Activation::Sigmoid);
    let (zr, h) = zrh.split_at_mut(2 * n);
    let (z, r) = zr.split_at(n);
    for i in 0..n {
        h[i] += recur[2 * n + i] * r[i];
    }
    activate(kernel, &mut h[..n], Activation::Tanh);
    for i in 0..n {
        h[i] = z[i] * state[i] + (1.0 - z[i]) * h[i];
    }
    state.copy_from_slice(&h[..n]);
}

/// `compute_rnn`: features in, band gains and voice probability out.
pub fn compute_rnn(kernel: Kernel, rnn: &mut RnnState, gains: &mut [f32; DENSE_OUT_SIZE], input: &[f32]) -> f32 {
    let model = &*MODEL;
    let mut tmp = [0.0f32; CONV1_OUT_SIZE];
    let mut tmp2 = [0.0f32; CONV2_OUT_SIZE];
    conv1d(kernel, &model.conv1, &mut tmp, &mut rnn.conv1, &input[..CONV1_IN_SIZE], Activation::Tanh);
    conv1d(kernel, &model.conv2, &mut tmp2, &mut rnn.conv2, &tmp[..CONV2_IN_SIZE], Activation::Tanh);
    gru(kernel, &model.gru[0].0, &model.gru[0].1, &mut rnn.gru[0], &tmp2);
    let first = rnn.gru[0];
    gru(kernel, &model.gru[1].0, &model.gru[1].1, &mut rnn.gru[1], &first);
    let second = rnn.gru[1];
    gru(kernel, &model.gru[2].0, &model.gru[2].1, &mut rnn.gru[2], &second);
    compute_linear(kernel, &model.dense_out, gains, &rnn.gru[2]);
    activate(kernel, gains, Activation::Sigmoid);
    let mut vad = [0.0f32; 1];
    compute_linear(kernel, &model.vad_dense, &mut vad, &rnn.gru[2]);
    activate(kernel, &mut vad, Activation::Sigmoid);
    vad[0]
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn model_loads_with_expected_shapes() {
        let model = &*MODEL;
        assert_eq!(model.conv1.weights.len(), 195 * 128);
        assert_eq!(model.gru[0].0.idx.as_ref().unwrap().len(), 4752);
        assert_eq!(model.gru[2].1.diag.as_ref().unwrap().len(), 1152);
        // Sparse blocks: 144 row groups, the rest column positions.
        let idx = model.gru[1].0.idx.as_ref().unwrap();
        let mut k = 0;
        let mut blocks = 0;
        for _ in 0..144 {
            let cols = idx[k] as usize;
            k += 1 + cols;
            blocks += cols;
        }
        assert_eq!(k, idx.len());
        assert_eq!(blocks * 32, model.gru[1].0.weights.len());
    }

    #[test]
    fn approximations_are_close_to_exact() {
        for kernel in [Kernel::Sse2, Kernel::Avx2Fma] {
            if !kernel.available() {
                continue;
            }
            let mut values: Vec<f32> = (-40..=40).map(|v| v as f32 / 8.0).collect();
            let inputs = values.clone();
            activate(kernel, &mut values, Activation::Tanh);
            for (y, x) in values.iter().zip(&inputs) {
                assert!((y - x.tanh()).abs() < 1e-3, "{kernel:?} tanh({x})");
            }
            let mut values = inputs.clone();
            activate(kernel, &mut values, Activation::Sigmoid);
            for (y, x) in values.iter().zip(&inputs) {
                assert!((y - 1.0 / (1.0 + (-x).exp())).abs() < 1e-3, "{kernel:?} sigmoid({x})");
            }
        }
    }
}
