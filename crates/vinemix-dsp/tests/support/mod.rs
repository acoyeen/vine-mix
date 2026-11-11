//! Measurement helpers for the effect listening tests: a deterministic
//! speech-like signal, WAV files, ITU-R BS.1770 loudness and band levels.
//!
//! Everything here is test-only and synthetic. Nothing opens an audio device.

#![allow(dead_code)]

use std::f64::consts::PI;
use std::path::Path;

use vinemix_dsp::{GraphProcessor, Inputs};
use vinemix_model::config::{AudioGraph, GraphEdge, GraphNode};
use vinemix_model::limits::MAXIMUM_GRAPH_NODES;
use vinemix_model::plan::{compile_graph, GraphPlan};
use vinemix_rt::{Frame, QUANTUM_FRAMES};

pub const RATE: f64 = 48_000.0;

/// Microphone → one effect node → output.
pub fn single_effect(kind: &str, configure: impl FnOnce(&mut GraphNode)) -> AudioGraph {
    let mut effect = GraphNode::new("fx", kind, "fx", 0.0, 0.0);
    configure(&mut effect);
    let nodes = vec![GraphNode::new("mic", "microphone", "mic", 0.0, 0.0), effect, GraphNode::new("out", "output", "out", 0.0, 0.0)];
    let edges = vec![GraphEdge::audio("a", "mic", "fx"), GraphEdge::audio("b", "fx", "out")];
    AudioGraph { nodes, edges, ..AudioGraph::default() }
}

/// A running graph fed one microphone.
pub struct Rig {
    pub plan: GraphPlan,
    pub processor: GraphProcessor,
    effect: usize,
}

impl Rig {
    pub fn new(graph: &AudioGraph) -> Self {
        let plan = compile_graph(graph).expect("fixture compiles");
        let mut processor = GraphProcessor::new();
        processor.prepare(&plan);
        let effect = plan.nodes.iter().position(|n| n.id == "fx").expect("an effect node");
        let mut rig = Self { plan, processor, effect };
        // A new graph fades its cables in over 10 ms. Let that finish on
        // silence, so measurements start with the graph at its set gains.
        rig.run(&[Frame::SILENT; 4 * QUANTUM_FRAMES]);
        rig
    }

    /// A parameter edit through the engine's in-place path.
    pub fn edit(&mut self, graph: &AudioGraph) {
        let next = compile_graph(graph).expect("edit compiles");
        self.processor.retarget(&self.plan, &next);
        self.plan = next;
    }

    /// Runs `input` through the graph and returns the effect node's output.
    /// A partial final quantum is padded with silence and trimmed again.
    pub fn run(&mut self, input: &[Frame]) -> Vec<Frame> {
        let mut output = Vec::with_capacity(input.len());
        let mut block = [Frame::SILENT; QUANTUM_FRAMES];
        for chunk in input.chunks(QUANTUM_FRAMES) {
            block[..chunk.len()].copy_from_slice(chunk);
            block[chunk.len()..].fill(Frame::SILENT);
            let mut inputs: Inputs<'_> = [&[]; MAXIMUM_GRAPH_NODES];
            inputs[0] = &block;
            self.processor.process(&self.plan, &inputs, true);
            output.extend_from_slice(&self.processor.output(self.effect)[..chunk.len()]);
        }
        output
    }
}

/// Runs `input` through one freshly prepared effect node.
pub fn render(kind: &str, configure: impl FnOnce(&mut GraphNode), input: &[Frame]) -> Vec<Frame> {
    Rig::new(&single_effect(kind, configure)).run(input)
}

pub fn mono(samples: &[f64]) -> Vec<Frame> {
    samples.iter().map(|&s| Frame::new(s as f32, s as f32)).collect()
}

pub fn left(frames: &[Frame]) -> Vec<f64> {
    frames.iter().map(|f| f64::from(f.left)).collect()
}

pub fn right(frames: &[Frame]) -> Vec<f64> {
    frames.iter().map(|f| f64::from(f.right)).collect()
}

pub fn scaled(frames: &[Frame], gain: f64) -> Vec<Frame> {
    frames.iter().map(|f| Frame::new((f64::from(f.left) * gain) as f32, (f64::from(f.right) * gain) as f32)).collect()
}

pub fn db(ratio: f64) -> f64 {
    20.0 * ratio.max(1e-12).log10()
}

pub fn from_db(db: f64) -> f64 {
    10f64.powf(db / 20.0)
}

pub fn sine(frequency: f64, amplitude: f64, frames: usize) -> Vec<f64> {
    (0..frames).map(|n| amplitude * (2.0 * PI * frequency * n as f64 / RATE).sin()).collect()
}

/// Deterministic uniform noise in [-1, 1).
pub struct Noise(u32);

impl Noise {
    pub fn new(seed: u32) -> Self {
        Self(seed)
    }

    pub fn next(&mut self) -> f64 {
        self.0 = self.0.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
        f64::from(self.0 >> 8) / f64::from(1u32 << 23) - 1.0
    }
}

/// A direct-form biquad for measurement filters (RBJ cookbook formulae).
#[derive(Clone, Copy)]
pub struct Section {
    b: [f64; 3],
    a: [f64; 2],
    z: [f64; 2],
}

impl Section {
    pub fn raw(b: [f64; 3], a: [f64; 2]) -> Self {
        Self { b, a, z: [0.0; 2] }
    }

    pub fn band_pass(frequency: f64, q: f64) -> Self {
        let w = 2.0 * PI * frequency / RATE;
        let alpha = w.sin() / (2.0 * q);
        let a0 = 1.0 + alpha;
        Self::raw([alpha / a0, 0.0, -alpha / a0], [-2.0 * w.cos() / a0, (1.0 - alpha) / a0])
    }

    pub fn low_pass(frequency: f64, q: f64) -> Self {
        let w = 2.0 * PI * frequency / RATE;
        let alpha = w.sin() / (2.0 * q);
        let a0 = 1.0 + alpha;
        let k = (1.0 - w.cos()) / a0;
        Self::raw([k / 2.0, k, k / 2.0], [-2.0 * w.cos() / a0, (1.0 - alpha) / a0])
    }

    pub fn high_pass(frequency: f64, q: f64) -> Self {
        let w = 2.0 * PI * frequency / RATE;
        let alpha = w.sin() / (2.0 * q);
        let a0 = 1.0 + alpha;
        let k = (1.0 + w.cos()) / a0;
        Self::raw([k / 2.0, -k, k / 2.0], [-2.0 * w.cos() / a0, (1.0 - alpha) / a0])
    }

    pub fn step(&mut self, x: f64) -> f64 {
        let y = self.b[0] * x + self.z[0];
        self.z[0] = self.b[1] * x - self.a[0] * y + self.z[1];
        self.z[1] = self.b[2] * x - self.a[1] * y;
        y
    }

    pub fn apply(mut self, samples: &[f64]) -> Vec<f64> {
        samples.iter().map(|&x| self.step(x)).collect()
    }
}

/// Fourth-order band limits (two cascaded second-order sections each way).
pub fn band(samples: &[f64], low: f64, high: f64) -> Vec<f64> {
    let q = std::f64::consts::FRAC_1_SQRT_2;
    let mut out = samples.to_vec();
    for _ in 0..2 {
        out = Section::high_pass(low, q).apply(&out);
        out = Section::low_pass(high, q).apply(&out);
    }
    out
}

pub fn rms(samples: &[f64]) -> f64 {
    if samples.is_empty() {
        return 0.0;
    }
    (samples.iter().map(|s| s * s).sum::<f64>() / samples.len() as f64).sqrt()
}

pub fn peak(samples: &[f64]) -> f64 {
    samples.iter().fold(0.0f64, |m, s| m.max(s.abs()))
}

/// RMS over the frames whose 20 ms neighbourhood is within 40 dB of the
/// loudest one: the level of the speech itself, not of its pauses.
pub fn active_rms(samples: &[f64]) -> f64 {
    let window = 960;
    let powers: Vec<f64> = samples.chunks(window).map(|c| c.iter().map(|s| s * s).sum::<f64>() / c.len() as f64).collect();
    let top = powers.iter().cloned().fold(0.0, f64::max);
    let kept: Vec<f64> = powers.into_iter().filter(|&p| p > top * 1e-4).collect();
    if kept.is_empty() { 0.0 } else { (kept.iter().sum::<f64>() / kept.len() as f64).sqrt() }
}

/// Amplitude of one frequency by correlation over `samples` (use a whole
/// number of periods).
pub fn tone_level(samples: &[f64], frequency: f64) -> f64 {
    let (mut re, mut im) = (0.0, 0.0);
    for (n, &s) in samples.iter().enumerate() {
        let phase = 2.0 * PI * frequency * n as f64 / RATE;
        re += s * phase.cos();
        im += s * phase.sin();
    }
    2.0 * (re * re + im * im).sqrt() / samples.len() as f64
}

/// As [`tone_level`], through a Hann window: a slowly drifting level does not
/// leak from a strong tone into the frequencies around it.
pub fn windowed_tone_level(samples: &[f64], frequency: f64) -> f64 {
    let (mut re, mut im) = (0.0, 0.0);
    for (n, &s) in samples.iter().enumerate() {
        let window = 1.0 - (2.0 * PI * n as f64 / samples.len() as f64).cos();
        let phase = 2.0 * PI * frequency * n as f64 / RATE;
        re += s * window * phase.cos();
        im += s * window * phase.sin();
    }
    2.0 * (re * re + im * im).sqrt() / samples.len() as f64
}

/// ITU-R BS.1770-4 K-weighting at 48 kHz.
pub fn k_weighted(samples: &[f64]) -> Vec<f64> {
    let shelf = Section::raw(
        [1.535_124_859_586_97, -2.691_696_189_406_38, 1.198_392_810_852_85],
        [-1.690_659_293_182_41, 0.732_480_774_215_85],
    );
    let high = Section::raw([1.0, -2.0, 1.0], [-1.990_047_454_833_98, 0.990_072_250_366_21]);
    high.apply(&shelf.apply(samples))
}

/// Momentary (400 ms, 75% overlap) loudness blocks of one channel, in LUFS.
pub fn loudness_blocks(samples: &[f64]) -> Vec<f64> {
    let weighted = k_weighted(samples);
    let (block, hop) = (19_200usize, 4_800usize);
    let mut blocks = Vec::new();
    let mut start = 0;
    while start + block <= weighted.len() {
        let power = weighted[start..start + block].iter().map(|s| s * s).sum::<f64>() / block as f64;
        blocks.push(-0.691 + 10.0 * power.max(1e-20).log10());
        start += hop;
    }
    blocks
}

fn gated(blocks: &[f64]) -> Vec<f64> {
    let energy = |l: f64| 10f64.powf((l + 0.691) / 10.0);
    let absolute: Vec<f64> = blocks.iter().cloned().filter(|&l| l > -70.0).collect();
    if absolute.is_empty() {
        return absolute;
    }
    let mean = absolute.iter().map(|&l| energy(l)).sum::<f64>() / absolute.len() as f64;
    let relative = -0.691 + 10.0 * mean.log10() - 10.0;
    absolute.into_iter().filter(|&l| l > relative).collect()
}

/// Gated integrated loudness of one channel, in LUFS (BS.1770-4 gating).
pub fn loudness(samples: &[f64]) -> f64 {
    let kept = gated(&loudness_blocks(samples));
    if kept.is_empty() {
        return -120.0;
    }
    let energy = kept.iter().map(|&l| 10f64.powf((l + 0.691) / 10.0)).sum::<f64>() / kept.len() as f64;
    -0.691 + 10.0 * energy.log10()
}

/// Spread of momentary loudness across the gated blocks (95th minus 10th
/// percentile), in dB: how much the delivery wanders in level.
pub fn loudness_spread(samples: &[f64]) -> f64 {
    let mut kept = gated(&loudness_blocks(samples));
    if kept.len() < 4 {
        return 0.0;
    }
    kept.sort_by(f64::total_cmp);
    let at = |q: f64| kept[((kept.len() - 1) as f64 * q).round() as usize];
    at(0.95) - at(0.10)
}

/// The loudest 10 ms of `samples` in a band, relative to the active level of
/// the whole signal: how far sibilants or plosives stick out.
pub fn band_burst_db(samples: &[f64], low: f64, high: f64) -> f64 {
    let limited = band(samples, low, high);
    let top = limited.chunks(480).map(rms).fold(0.0, f64::max);
    db(top / active_rms(samples).max(1e-12))
}

pub const BANDS: [(&str, f64, f64); 6] =
    [("rumble", 20.0, 80.0), ("body", 80.0, 250.0), ("low-mid", 250.0, 700.0), ("mid", 700.0, 2_000.0), ("presence", 2_000.0, 5_000.0), ("air", 5_000.0, 12_000.0)];

/// Level of each band in dBFS RMS over the whole signal.
pub fn band_levels(samples: &[f64]) -> Vec<f64> {
    BANDS.iter().map(|&(_, low, high)| db(rms(&band(samples, low, high)))).collect()
}

/// A deterministic speech-like signal: a harmonic source through vowel
/// formants in syllables of varying level, noise bursts for sibilants, short
/// low thumps for plosives, and a quiet noise floor. `pitch` is the
/// fundamental in hertz (about 110 for a low voice, 210 for a high one).
pub fn speech(seconds: f64, pitch: f64, seed: u32) -> Vec<f64> {
    let frames = (seconds * RATE) as usize;
    let mut noise = Noise::new(seed);
    let vowels: [[f64; 3]; 4] = [[700.0, 1220.0, 2600.0], [300.0, 2290.0, 3010.0], [450.0, 1030.0, 2380.0], [570.0, 840.0, 2410.0]];
    let levels = [0.0, -5.0, -11.0, -2.0, -8.0, -15.0, -4.0];
    let syllable = (0.24 * RATE) as usize;
    let voiced = (0.17 * RATE) as usize;
    let mut out = vec![0.0; frames];
    let mut phase = 0.0f64;
    let mut formants = [Section::band_pass(700.0, 6.0); 3];
    let mut hiss = [Section::high_pass(5_200.0, 0.8), Section::low_pass(9_500.0, 0.8)];
    let mut thump = Section::low_pass(90.0, 0.7);
    let mut current = usize::MAX;
    for (n, sample) in out.iter_mut().enumerate() {
        let index = n / syllable;
        let within = n % syllable;
        if index != current {
            current = index;
            let vowel = vowels[index % vowels.len()];
            for (formant, &frequency) in formants.iter_mut().zip(&vowel) {
                let state = formant.z;
                *formant = Section::band_pass(frequency, 6.0 + frequency / 900.0);
                formant.z = state;
            }
        }
        let level = from_db(levels[index % levels.len()]);
        let sibilant = index % 4 == 3;
        let plosive = index % 5 == 2;
        // Pitch glides down across each syllable, with a little vibrato.
        let f0 = pitch * (1.0 - 0.08 * within as f64 / syllable as f64) * (1.0 + 0.01 * (n as f64 * 2.0 * PI * 5.0 / RATE).sin());
        phase += f0 / RATE;
        phase -= phase.floor();
        let mut source = 0.0;
        let mut harmonic = 1.0;
        while harmonic * f0 < 6_000.0 {
            source += (2.0 * PI * harmonic * phase).sin() / harmonic;
            harmonic += 1.0;
        }
        // A sibilant is unvoiced: its vowel starts when the hiss ends.
        let onset = (0.09 * RATE) as usize;
        let start = if sibilant { onset } else { 0 };
        let envelope = if within >= start && within < voiced + start / 2 {
            let x = (within - start) as f64 / (voiced - start / 2) as f64;
            (PI * x).sin().powf(0.6)
        } else {
            0.0
        };
        let mut voice = 0.0;
        for (formant, weight) in formants.iter_mut().zip([1.0, 0.5, 0.25]) {
            voice += weight * formant.step(source * envelope);
        }
        let white = noise.next();
        let burst = if sibilant && within < onset { (PI * within as f64 / onset as f64).sin() } else { 0.0 };
        let high = hiss[0].step(white * burst);
        let ess = hiss[1].step(high) * 0.06;
        let pop = if plosive && within < 240 { 1.0 - within as f64 / 240.0 } else { 0.0 };
        let puff = thump.step(pop * 6.0);
        *sample = level * (0.28 * voice + if sibilant { ess } else { 0.0 } + if plosive { 0.05 * puff } else { 0.0 }) + white * 0.000_5;
    }
    // Normalise the active level to -24 dBFS RMS, an ordinary microphone level.
    let gain = from_db(-24.0) / active_rms(&out).max(1e-12);
    out.iter_mut().for_each(|s| *s *= gain);
    out
}

/// Reads a 48 kHz PCM16, PCM24 or float32 WAV file as frames.
pub fn read_wav(path: &Path) -> Vec<Frame> {
    let bytes = std::fs::read(path).unwrap_or_else(|e| panic!("cannot read {}: {e}", path.display()));
    assert!(bytes.len() > 12 && &bytes[0..4] == b"RIFF" && &bytes[8..12] == b"WAVE", "{} is not a WAV file", path.display());
    let u16_at = |i: usize| u16::from_le_bytes([bytes[i], bytes[i + 1]]);
    let u32_at = |i: usize| u32::from_le_bytes([bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]]);
    let (mut format, mut channels, mut rate, mut bits) = (0u16, 0usize, 0u32, 0usize);
    let mut data: &[u8] = &[];
    let mut at = 12;
    while at + 8 <= bytes.len() {
        let size = u32_at(at + 4) as usize;
        let body = at + 8;
        let end = (body + size).min(bytes.len());
        match &bytes[at..at + 4] {
            b"fmt " => {
                format = u16_at(body);
                channels = usize::from(u16_at(body + 2));
                rate = u32_at(body + 4);
                bits = usize::from(u16_at(body + 14));
                if format == 0xFFFE && size >= 26 {
                    format = u16_at(body + 24);
                }
            }
            b"data" => data = &bytes[body..end],
            _ => {}
        }
        at = body + size + (size & 1);
    }
    assert_eq!(rate, 48_000, "{}: only 48 kHz files are supported", path.display());
    assert!((1..=2).contains(&channels), "{}: mono or stereo only", path.display());
    let width = bits / 8;
    let value = |raw: &[u8]| -> f32 {
        match (format, bits) {
            (1, 16) => f32::from(i16::from_le_bytes([raw[0], raw[1]])) / 32_768.0,
            (1, 24) => (i32::from_le_bytes([0, raw[0], raw[1], raw[2]]) >> 8) as f32 / 8_388_608.0,
            (3, 32) => f32::from_le_bytes([raw[0], raw[1], raw[2], raw[3]]),
            _ => panic!("{}: unsupported sample format {format}/{bits}", path.display()),
        }
    };
    data.chunks_exact(width * channels)
        .map(|frame| {
            let l = value(&frame[..width]);
            let r = if channels == 2 { value(&frame[width..]) } else { l };
            Frame::new(l, r)
        })
        .collect()
}

/// Writes frames as a 48 kHz stereo float32 WAV file.
pub fn write_wav(path: &Path, frames: &[Frame]) {
    let data = (frames.len() * 8) as u32;
    let mut bytes = Vec::with_capacity(44 + frames.len() * 8);
    bytes.extend_from_slice(b"RIFF");
    bytes.extend_from_slice(&(36 + data).to_le_bytes());
    bytes.extend_from_slice(b"WAVEfmt ");
    bytes.extend_from_slice(&16u32.to_le_bytes());
    bytes.extend_from_slice(&3u16.to_le_bytes());
    bytes.extend_from_slice(&2u16.to_le_bytes());
    bytes.extend_from_slice(&48_000u32.to_le_bytes());
    bytes.extend_from_slice(&(48_000u32 * 8).to_le_bytes());
    bytes.extend_from_slice(&8u16.to_le_bytes());
    bytes.extend_from_slice(&32u16.to_le_bytes());
    bytes.extend_from_slice(b"data");
    bytes.extend_from_slice(&data.to_le_bytes());
    for frame in frames {
        bytes.extend_from_slice(&frame.left.to_le_bytes());
        bytes.extend_from_slice(&frame.right.to_le_bytes());
    }
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).expect("output directory");
    }
    std::fs::write(path, bytes).unwrap_or_else(|e| panic!("cannot write {}: {e}", path.display()));
}
