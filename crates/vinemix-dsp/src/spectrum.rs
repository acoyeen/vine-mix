//! Live EQ spectrum (`spectrum.hpp`).
//!
//! The mixer pushes audio into a [`SpectrumProducer`]; the control thread's
//! [`SpectrumConsumer`] performs the FFT and display smoothing. They share a
//! lock-free triple buffer: each side exclusively owns one slot and exchanges
//! the middle one, so a stalled UI loses intermediate views, never audio time.

use std::cell::UnsafeCell;
use std::f64::consts::PI;
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::Arc;

use vinemix_model::SAMPLE_RATE;
use vinemix_rt::Frame;

pub const FFT_FRAMES: usize = 4096;
pub const BIN_COUNT: usize = 128;
/// 20 Hz publication, independent of UI polling.
pub const HOP_FRAMES: usize = 2400;
pub const FLOOR_DB: f32 = -90.0;

struct Window {
    samples: [Frame; FFT_FRAMES],
    frame: u64,
    revision: u64,
}

struct Mailbox {
    slots: [UnsafeCell<Window>; 3],
    middle: AtomicU32,
}

// SAFETY: slot ownership is transferred only through `middle`. The producer
// touches only the slot index it owns, the consumer only its own, and an
// exchange with acquire-release ordering hands a slot over completely.
unsafe impl Sync for Mailbox {}
// SAFETY: as above; the mailbox owns plain data.
unsafe impl Send for Mailbox {}

const DIRTY: u32 = 4;
const INDEX_MASK: u32 = 3;

impl Mailbox {
    fn new() -> Arc<Self> {
        let window = || UnsafeCell::new(Window { samples: [Frame::SILENT; FFT_FRAMES], frame: 0, revision: 0 });
        Arc::new(Self { slots: [window(), window(), window()], middle: AtomicU32::new(1) })
    }
}

/// Audio-thread half: bounded copies only, no FFT, allocation or lock.
pub struct SpectrumProducer {
    mailbox: Arc<Mailbox>,
    slot: u32,
    ring: Box<[Frame; FFT_FRAMES]>,
    write: usize,
    filled: usize,
    since_publish: usize,
    producer_frame: u64,
    producer_revision: u64,
}

/// Control-thread half: FFT, band mapping and attack/release smoothing.
pub struct SpectrumConsumer {
    mailbox: Arc<Mailbox>,
    slot: u32,
    view: View,
    window: Box<[f64; FFT_FRAMES]>,
    reversed: Box<[usize; FFT_FRAMES]>,
    fft: Box<[(f64, f64); FFT_FRAMES]>,
    twiddles: Box<[(f64, f64); FFT_FRAMES / 2]>,
    powers: Box<[f64; FFT_FRAMES / 2 + 1]>,
    limits: [(f64, f64); BIN_COUNT],
    window_sum: f64,
}

/// The smoothed display: 128 log-spaced bins from 20 Hz to 20 kHz.
#[derive(Clone, Debug, PartialEq)]
pub struct View {
    pub db: [f32; BIN_COUNT],
    pub frame: u64,
    pub revision: u64,
}

impl std::fmt::Debug for SpectrumProducer {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SpectrumProducer").field("revision", &self.producer_revision).finish_non_exhaustive()
    }
}

impl std::fmt::Debug for SpectrumConsumer {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SpectrumConsumer").field("view", &self.view.frame).finish_non_exhaustive()
    }
}

/// Centre frequency of display bin `index`.
pub fn frequency_hz(index: usize) -> f64 {
    20.0 * 1000f64.powf(index as f64 / (BIN_COUNT - 1) as f64)
}

/// Creates a connected producer and consumer starting at `revision`.
pub fn live_spectrum(revision: u64) -> (SpectrumProducer, SpectrumConsumer) {
    let mailbox = Mailbox::new();
    let mut window = Box::new([0.0; FFT_FRAMES]);
    let mut reversed = Box::new([0usize; FFT_FRAMES]);
    let mut window_sum = 0.0;
    for n in 0..FFT_FRAMES {
        window[n] = 0.5 - 0.5 * (2.0 * PI * n as f64 / FFT_FRAMES as f64).cos();
        window_sum += window[n];
        let mut source = n;
        let mut reverse = 0;
        for _ in 0..12 {
            reverse = (reverse << 1) | (source & 1);
            source >>= 1;
        }
        reversed[n] = reverse;
    }
    let mut twiddles = Box::new([(0.0, 0.0); FFT_FRAMES / 2]);
    for (n, twiddle) in twiddles.iter_mut().enumerate() {
        let angle = -2.0 * PI * n as f64 / FFT_FRAMES as f64;
        *twiddle = (angle.cos(), angle.sin());
    }
    let half_step = 1000f64.powf(0.5 / (BIN_COUNT - 1) as f64);
    let mut limits = [(0.0, 0.0); BIN_COUNT];
    for (n, limit) in limits.iter_mut().enumerate() {
        let frequency = frequency_hz(n);
        *limit = (
            frequency / half_step * FFT_FRAMES as f64 / f64::from(SAMPLE_RATE),
            frequency * half_step * FFT_FRAMES as f64 / f64::from(SAMPLE_RATE),
        );
    }
    let producer = SpectrumProducer {
        mailbox: mailbox.clone(),
        slot: 2,
        ring: Box::new([Frame::SILENT; FFT_FRAMES]),
        write: 0,
        filled: 0,
        since_publish: 0,
        producer_frame: 0,
        producer_revision: revision,
    };
    let consumer = SpectrumConsumer {
        mailbox,
        slot: 0,
        view: View { db: [FLOOR_DB; BIN_COUNT], frame: 0, revision },
        window,
        reversed,
        fft: Box::new([(0.0, 0.0); FFT_FRAMES]),
        twiddles,
        powers: Box::new([0.0; FFT_FRAMES / 2 + 1]),
        limits,
        window_sum,
    };
    (producer, consumer)
}

impl SpectrumProducer {
    fn writable(&mut self) -> &mut Window {
        // SAFETY: `self.slot` is owned exclusively by the producer until the
        // next `publish` hands it to `middle`.
        unsafe { &mut *self.mailbox.slots[self.slot as usize].get() }
    }

    fn publish(&mut self) {
        self.slot = self.mailbox.middle.swap(self.slot | DIRTY, Ordering::AcqRel) & INDEX_MASK;
    }

    fn publish_empty(&mut self) {
        let revision = self.producer_revision;
        let slot = self.writable();
        slot.frame = 0;
        slot.revision = revision;
        self.publish();
    }

    /// After a suspend or discontinuity: start over under a new engine-wide
    /// token. The empty publication clears the old display immediately.
    pub fn reset(&mut self, revision: u64) {
        self.write = 0;
        self.filled = 0;
        self.since_publish = 0;
        self.producer_frame = 0;
        self.producer_revision = revision;
        self.publish_empty();
    }

    /// Appends one quantum ending at engine frame `end_frame`.
    pub fn push(&mut self, samples: &[Frame], end_frame: u64) {
        let count = samples.len() as u64;
        // A missing quantum cannot be bridged into an apparently continuous FFT.
        if self.producer_frame != 0 && (end_frame < count || self.producer_frame != end_frame - count) {
            self.write = 0;
            self.filled = 0;
            self.since_publish = 0;
            self.publish_empty();
        }
        for sample in samples {
            self.ring[self.write] = Frame::new(
                if sample.left.is_finite() { sample.left } else { 0.0 },
                if sample.right.is_finite() { sample.right } else { 0.0 },
            );
            self.write = (self.write + 1) & (FFT_FRAMES - 1);
        }
        self.filled = FFT_FRAMES.min(self.filled + samples.len());
        self.since_publish += samples.len();
        self.producer_frame = end_frame;
        if self.filled != FFT_FRAMES || self.since_publish < HOP_FRAMES {
            return;
        }
        self.since_publish = 0;
        let write = self.write;
        let revision = self.producer_revision;
        // SAFETY: as in `writable`; `ring` is a separate allocation.
        let slot = unsafe { &mut *self.mailbox.slots[self.slot as usize].get() };
        let tail = FFT_FRAMES - write;
        slot.samples[..tail].copy_from_slice(&self.ring[write..]);
        slot.samples[tail..].copy_from_slice(&self.ring[..write]);
        slot.frame = end_frame;
        slot.revision = revision;
        self.publish();
    }
}

impl SpectrumConsumer {
    fn consume(&mut self) -> Option<&Window> {
        if self.mailbox.middle.load(Ordering::Acquire) & DIRTY == 0 {
            return None;
        }
        self.slot = self.mailbox.middle.swap(self.slot, Ordering::AcqRel) & INDEX_MASK;
        // SAFETY: the consumer now exclusively owns `self.slot`.
        Some(unsafe { &*self.mailbox.slots[self.slot as usize].get() })
    }

    fn interpolate_power(&self, position: f64) -> f64 {
        let lower = position as usize;
        self.powers[lower] + (self.powers[lower + 1] - self.powers[lower]) * (position - lower as f64)
    }

    /// The latest view. A newer complete window always wins; repeated reads
    /// do no FFT work or smoothing.
    pub fn read(&mut self) -> &View {
        let Some(incoming) = self.consume() else { return &self.view };
        let (frame, revision) = (incoming.frame, incoming.revision);
        if frame == 0 {
            self.view = View { db: [FLOOR_DB; BIN_COUNT], frame: 0, revision };
            return &self.view;
        }
        // SAFETY: re-borrow the owned slot after the `&mut self` call above.
        let samples = unsafe { &(*self.mailbox.slots[self.slot as usize].get()).samples };
        let fresh = self.view.frame == 0
            || self.view.revision != revision
            || frame <= self.view.frame
            || frame - self.view.frame > u64::from(SAMPLE_RATE / 4);
        // Packing L + iR allows one complex FFT for both channels.
        for n in 0..FFT_FRAMES {
            self.fft[self.reversed[n]] =
                (f64::from(samples[n].left) * self.window[n], f64::from(samples[n].right) * self.window[n]);
        }
        let mut width = 2;
        while width <= FFT_FRAMES {
            let half = width / 2;
            let stride = FFT_FRAMES / width;
            let mut base = 0;
            while base < FFT_FRAMES {
                for n in 0..half {
                    let even = self.fft[base + n];
                    let (or, oi) = self.fft[base + n + half];
                    let (tr, ti) = self.twiddles[n * stride];
                    let odd = (or * tr - oi * ti, or * ti + oi * tr);
                    self.fft[base + n] = (even.0 + odd.0, even.1 + odd.1);
                    self.fft[base + n + half] = (even.0 - odd.0, even.1 - odd.1);
                }
                base += width;
            }
            width *= 2;
        }
        // The mean of independently measured channel powers, corrected for
        // Hann coherent gain: opposite-polarity stereo cannot cancel.
        let norm = |(re, im): (f64, f64)| re * re + im * im;
        let scale = self.window_sum * self.window_sum;
        for k in 1..FFT_FRAMES / 2 {
            self.powers[k] = (norm(self.fft[k]) + norm(self.fft[FFT_FRAMES - k])) / scale;
        }
        self.powers[0] = self.powers[1];
        self.powers[FFT_FRAMES / 2] = self.powers[FFT_FRAMES / 2 - 1];
        let seconds = if fresh { 0.0 } else { (frame - self.view.frame) as f64 / f64::from(SAMPLE_RATE) };
        let attack = (-seconds / 0.035).exp();
        let release = (-seconds / 0.180).exp();
        for n in 0..BIN_COUNT {
            let (low, high) = self.limits[n];
            let mut power = self.interpolate_power(low).max(self.interpolate_power(high));
            let mut k = low.ceil() as usize;
            while k <= high as usize {
                power = power.max(self.powers[k]);
                k += 1;
            }
            let measured = (10.0 * power.max(1e-9).log10()).clamp(-90.0, 0.0) as f32;
            let previous = self.view.db[n];
            let smoothing = if measured > previous { attack } else { release };
            self.view.db[n] = if fresh {
                measured
            } else {
                // The difference is single precision, as in the 0.5 engine.
                (f64::from(measured) + smoothing * f64::from(previous - measured)) as f32
            };
        }
        self.view.frame = frame;
        self.view.revision = revision;
        &self.view
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn tone(frequency: f64, amplitude: f64, start: usize, count: usize) -> Vec<Frame> {
        (start..start + count)
            .map(|n| {
                let x = (amplitude * (2.0 * PI * frequency * n as f64 / f64::from(SAMPLE_RATE)).sin()) as f32;
                Frame::new(x, x)
            })
            .collect()
    }

    #[test]
    fn full_scale_sine_reads_near_zero_db_in_its_bin() {
        let (mut producer, mut consumer) = live_spectrum(1);
        let mut frame = 0u64;
        for _ in 0..20 {
            let block = tone(1000.0, 1.0, frame as usize, 240);
            frame += 240;
            producer.push(&block, frame);
        }
        let view = consumer.read().clone();
        // The ring fills at 4320 frames; the next hop would be 2400 later.
        assert_eq!(view.frame, 4320);
        let (index, peak) = view.db.iter().enumerate().fold((0, f32::MIN), |a, (i, &v)| if v > a.1 { (i, v) } else { a });
        assert!((frequency_hz(index) - 1000.0).abs() < 60.0, "peak at {}", frequency_hz(index));
        assert!(peak > -1.5 && peak <= 0.0, "{peak}");
        assert!(view.db[0] < -60.0);
    }

    #[test]
    fn discontinuity_clears_the_view() {
        let (mut producer, mut consumer) = live_spectrum(1);
        for k in 1..=20u64 {
            producer.push(&tone(500.0, 0.5, 0, 240), k * 240);
        }
        assert!(consumer.read().frame > 0);
        producer.push(&tone(500.0, 0.5, 0, 240), 100_000);
        let view = consumer.read();
        assert_eq!(view.frame, 0);
        assert!(view.db.iter().all(|&db| db == FLOOR_DB));
    }

    /// RUST-REV-022: the triple-buffer hand-over under real contention. The
    /// producer publishes and resets as fast as it can while the consumer
    /// takes windows; every window the consumer sees must be one complete
    /// publication (a contiguous run of samples ending at its frame, under
    /// one revision), never a mixture of two.
    #[test]
    fn windows_are_never_torn_under_concurrent_publication_and_reset() {
        use std::sync::atomic::AtomicBool;
        let (mut producer, mut consumer) = live_spectrum(1);
        let stop = Arc::new(AtomicBool::new(false));
        let writer = {
            let stop = Arc::clone(&stop);
            std::thread::spawn(move || {
                let mut end = 0u64;
                let mut revision = 1u64;
                let mut quanta = 0u64;
                let mut published = 0u64;
                while !stop.load(Ordering::Acquire) {
                    // Each sample carries its own absolute frame number.
                    let block: Vec<Frame> = (end..end + 240).map(|n| Frame::new(n as f32, revision as f32)).collect();
                    end += 240;
                    producer.push(&block, end);
                    quanta += 1;
                    if quanta.is_multiple_of(10) {
                        published += 1;
                    }
                    if quanta.is_multiple_of(257) {
                        revision += 1;
                        producer.reset(revision);
                    }
                    // Stay below 2^24 so every frame number is exact in f32.
                    if end > 12_000_000 {
                        break;
                    }
                }
                published
            })
        };
        let mut seen = 0u64;
        let mut empties = 0u64;
        let mut last_revision = 0u64;
        let started = std::time::Instant::now();
        while started.elapsed() < std::time::Duration::from_millis(1500) && !writer.is_finished() {
            let Some(window) = consumer.consume() else {
                std::hint::spin_loop();
                continue;
            };
            assert!(window.revision >= last_revision, "revision went backwards: {} after {last_revision}", window.revision);
            last_revision = window.revision;
            if window.frame == 0 {
                empties += 1;
                continue;
            }
            let first = window.frame - FFT_FRAMES as u64;
            for (k, sample) in window.samples.iter().enumerate() {
                assert_eq!(sample.left as u64, first + k as u64, "window ending at {} is not one contiguous publication at sample {k}", window.frame);
                assert_eq!(sample.right as u64, window.revision, "window mixes revisions at sample {k}");
            }
            seen += 1;
        }
        stop.store(true, Ordering::Release);
        let published = writer.join().unwrap();
        assert!(seen > 0, "the consumer never received a window ({published} hops, {empties} resets seen)");
        assert!(empties > 0, "resets were exercised");
    }

    /// Cost of the on-demand part of a spectrum (RUST-REV-010): one
    /// `read()` after a new window, which is what a snapshot does per EQ.
    /// `cargo test -p vinemix-dsp --release --lib spectrum_read_cost -- --ignored --nocapture`
    #[test]
    #[ignore = "benchmark; prints timings"]
    fn spectrum_read_cost() {
        let (mut producer, mut consumer) = live_spectrum(1);
        let mut frame = 0u64;
        let mut reads = Vec::new();
        let mut pushes = Vec::new();
        let mut idle = Vec::new();
        for _ in 0..400 {
            for _ in 0..10 {
                let block = tone(1000.0, 0.5, frame as usize, 240);
                frame += 240;
                let started = std::time::Instant::now();
                producer.push(&block, frame);
                pushes.push(started.elapsed());
            }
            let started = std::time::Instant::now();
            std::hint::black_box(consumer.read().frame);
            reads.push(started.elapsed());
            let started = std::time::Instant::now();
            std::hint::black_box(consumer.read().frame);
            idle.push(started.elapsed());
        }
        let summary = |mut samples: Vec<std::time::Duration>| {
            samples.sort();
            (samples[samples.len() / 2], samples[samples.len() * 99 / 100])
        };
        println!("mixer-side push of one 5 ms quantum (median, p99): {:?}", summary(pushes));
        println!("read with a new window: FFT + smoothing (median, p99): {:?}", summary(reads));
        println!("read with no new window (median, p99): {:?}", summary(idle));
    }
}
