//! Fixed-capacity single-producer/single-consumer frame queue.
//!
//! Port of `am::FrameQueue`. Neither side waits. An overflowing producer drops
//! the NEW frames; it never advances the read cursor or overwrites storage the
//! consumer could currently be reading.
//!
//! Improvement over the C++ contract: the single-producer/single-consumer rule
//! is enforced by the type system. [`frame_queue`] returns one [`Producer`] and
//! one [`Consumer`] (neither is `Clone`), plus a cloneable [`Monitor`] for any
//! thread that only observes occupancy or *requests* a flush.
//!
//! Dropping the last handle frees the storage. Engine code must keep a handle
//! owned off the audio thread so that release never happens on a realtime thread.

use crate::Frame;
use std::cell::UnsafeCell;
use std::ptr;
use std::sync::Arc;
use std::sync::atomic::{AtomicU64, Ordering};

#[repr(align(64))]
#[derive(Debug, Default)]
struct Padded(AtomicU64);

#[derive(Debug)]
struct Shared {
    frames: Box<[UnsafeCell<Frame>]>,
    write: Padded,
    read: Padded,
    clear_requested: Padded,
    clear_acknowledged: AtomicU64,
    dropped: AtomicU64,
    missing: AtomicU64,
    overruns: AtomicU64,
    underruns: AtomicU64,
}

// SAFETY: slot storage is only written by the unique Producer in slots the
// Consumer cannot observe (between read and write cursors' complement), and only
// read by the unique Consumer in slots published by a Release store of `write`.
unsafe impl Sync for Shared {}

impl Shared {
    #[inline]
    fn capacity(&self) -> usize {
        self.frames.len()
    }

    #[inline]
    fn base(&self) -> *mut Frame {
        // UnsafeCell<Frame> is repr(transparent), so the slice is a contiguous
        // array of Frame that may be mutated through a shared reference.
        UnsafeCell::raw_get(self.frames.as_ptr())
    }

    fn size(&self) -> usize {
        // Read first: a simultaneous consumer clear can otherwise make an older
        // write snapshot smaller than read. Observers may see an approximate
        // size, but never an underflowed count.
        let read = self.read.0.load(Ordering::Acquire);
        let write = self.write.0.load(Ordering::Acquire);
        (write.saturating_sub(read)).min(self.capacity() as u64) as usize
    }

    fn request_clear(&self) {
        self.clear_requested.0.fetch_add(1, Ordering::Release);
    }

    fn clear_pending(&self) -> bool {
        self.clear_requested.0.load(Ordering::Acquire)
            != self.clear_acknowledged.load(Ordering::Acquire)
    }

    fn stats(&self) -> QueueStats {
        QueueStats {
            dropped_frames: self.dropped.load(Ordering::Relaxed),
            underrun_frames: self.missing.load(Ordering::Relaxed),
            overrun_events: self.overruns.load(Ordering::Relaxed),
            underrun_events: self.underruns.load(Ordering::Relaxed),
        }
    }
}

/// Cumulative diagnostics, matching the C++ accessors.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct QueueStats {
    pub dropped_frames: u64,
    pub underrun_frames: u64,
    pub overrun_events: u64,
    pub underrun_events: u64,
}

/// Creates a queue holding `capacity_frames` frames.
///
/// # Panics
/// If `capacity_frames` is zero. Construct queues on control threads only.
pub fn frame_queue(capacity_frames: usize) -> (Producer, Consumer, Monitor) {
    assert!(capacity_frames > 0, "audio queue capacity must be positive");
    let frames = (0..capacity_frames)
        .map(|_| UnsafeCell::new(Frame::SILENT))
        .collect::<Vec<_>>()
        .into_boxed_slice();
    let shared = Arc::new(Shared {
        frames,
        write: Padded::default(),
        read: Padded::default(),
        clear_requested: Padded::default(),
        clear_acknowledged: AtomicU64::new(0),
        dropped: AtomicU64::new(0),
        missing: AtomicU64::new(0),
        overruns: AtomicU64::new(0),
        underruns: AtomicU64::new(0),
    });
    (
        Producer { shared: shared.clone() },
        Consumer { shared: shared.clone() },
        Monitor { shared },
    )
}

/// The unique writing side.
#[derive(Debug)]
pub struct Producer {
    shared: Arc<Shared>,
}

/// The unique reading side. Only the consumer may clear directly.
#[derive(Debug)]
pub struct Consumer {
    shared: Arc<Shared>,
}

/// Any-thread observer: occupancy, statistics and flush requests.
#[derive(Clone, Debug)]
pub struct Monitor {
    shared: Arc<Shared>,
}

impl Producer {
    /// Appends as many frames as fit; returns the number accepted.
    pub fn push(&mut self, input: &[Frame]) -> usize {
        let s = &*self.shared;
        let capacity = s.capacity();
        // Acquire on read synchronizes with the consumer's completed reads
        // before any reused slot is written.
        let write = s.write.0.load(Ordering::Relaxed);
        let read = s.read.0.load(Ordering::Acquire);
        let occupied = write.wrapping_sub(read) as usize;
        let available = capacity - occupied.min(capacity);
        let count = input.len().min(available);
        let offset = (write % capacity as u64) as usize;
        let first = count.min(capacity - offset);
        // SAFETY: slots [offset, offset+first) and [0, count-first) are free:
        // the consumer has released them (Acquire above) and cannot read them
        // until the Release store of `write` below. Sole producer by type.
        unsafe {
            ptr::copy_nonoverlapping(input.as_ptr(), s.base().add(offset), first);
            ptr::copy_nonoverlapping(input.as_ptr().add(first), s.base(), count - first);
        }
        s.write.0.store(write + count as u64, Ordering::Release);
        if count < input.len() {
            s.dropped.fetch_add((input.len() - count) as u64, Ordering::Relaxed);
            s.overruns.fetch_add(1, Ordering::Relaxed);
        }
        count
    }

    pub fn size(&self) -> usize {
        self.shared.size()
    }
    pub fn request_clear(&self) {
        self.shared.request_clear()
    }
    pub fn clear_pending(&self) -> bool {
        self.shared.clear_pending()
    }
    pub fn capacity(&self) -> usize {
        self.shared.capacity()
    }
    pub fn stats(&self) -> QueueStats {
        self.shared.stats()
    }
    pub fn monitor(&self) -> Monitor {
        Monitor { shared: self.shared.clone() }
    }
}

impl Consumer {
    fn acknowledge_clear(&mut self) {
        let s = &*self.shared;
        let requested = s.clear_requested.0.load(Ordering::Acquire);
        if requested != s.clear_acknowledged.load(Ordering::Relaxed) {
            s.read.0.store(s.write.0.load(Ordering::Acquire), Ordering::Release);
            s.clear_acknowledged.store(requested, Ordering::Release);
        }
    }

    /// Fills `output` from the queue; returns the number of frames read.
    /// A pending clear request is honoured first.
    pub fn pop(&mut self, output: &mut [Frame]) -> usize {
        self.acknowledge_clear();
        let s = &*self.shared;
        let capacity = s.capacity();
        let read = s.read.0.load(Ordering::Relaxed);
        let write = s.write.0.load(Ordering::Acquire);
        let count = output.len().min(write.wrapping_sub(read) as usize);
        let offset = (read % capacity as u64) as usize;
        let first = count.min(capacity - offset);
        // SAFETY: slots were published by the producer's Release store of
        // `write` (Acquire above) and are not rewritten until `read` advances.
        unsafe {
            ptr::copy_nonoverlapping(s.base().add(offset), output.as_mut_ptr(), first);
            ptr::copy_nonoverlapping(s.base(), output.as_mut_ptr().add(first), count - first);
        }
        s.read.0.store(read + count as u64, Ordering::Release);
        if count < output.len() {
            s.missing.fetch_add((output.len() - count) as u64, Ordering::Relaxed);
            s.underruns.fetch_add(1, Ordering::Relaxed);
        }
        count
    }

    /// Discards everything queued and acknowledges outstanding clear requests.
    pub fn clear(&mut self) {
        let s = &*self.shared;
        let requested = s.clear_requested.0.load(Ordering::Acquire);
        s.read.0.store(s.write.0.load(Ordering::Acquire), Ordering::Release);
        s.clear_acknowledged.store(requested, Ordering::Release);
    }

    pub fn size(&self) -> usize {
        self.shared.size()
    }
    pub fn request_clear(&self) {
        self.shared.request_clear()
    }
    pub fn clear_pending(&self) -> bool {
        self.shared.clear_pending()
    }
    pub fn capacity(&self) -> usize {
        self.shared.capacity()
    }
    pub fn stats(&self) -> QueueStats {
        self.shared.stats()
    }
    pub fn monitor(&self) -> Monitor {
        Monitor { shared: self.shared.clone() }
    }
}

impl Monitor {
    pub fn size(&self) -> usize {
        self.shared.size()
    }
    /// Asks the consumer to flush at its next `pop`.
    pub fn request_clear(&self) {
        self.shared.request_clear()
    }
    pub fn clear_pending(&self) -> bool {
        self.shared.clear_pending()
    }
    pub fn capacity(&self) -> usize {
        self.shared.capacity()
    }
    pub fn stats(&self) -> QueueStats {
        self.shared.stats()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ramp(start: usize, n: usize) -> Vec<Frame> {
        (start..start + n).map(|i| Frame::new(i as f32, -(i as f32))).collect()
    }

    #[test]
    fn wraps_and_preserves_order() {
        let (mut p, mut c, m) = frame_queue(8);
        let mut out = [Frame::SILENT; 8];
        let mut next_in = 0;
        let mut next_out = 0;
        for round in 0..50 {
            let n = 1 + round % 7;
            assert_eq!(p.push(&ramp(next_in, n)), n);
            next_in += n;
            assert_eq!(m.size(), n);
            assert_eq!(c.pop(&mut out[..n]), n);
            for f in &out[..n] {
                assert_eq!(f.left, next_out as f32);
                next_out += 1;
            }
        }
        assert_eq!(c.stats(), QueueStats::default());
    }

    #[test]
    fn overflow_drops_newest_frames() {
        let (mut p, mut c, _) = frame_queue(4);
        assert_eq!(p.push(&ramp(0, 6)), 4);
        let stats = p.stats();
        assert_eq!(stats.dropped_frames, 2);
        assert_eq!(stats.overrun_events, 1);
        let mut out = [Frame::SILENT; 4];
        assert_eq!(c.pop(&mut out), 4);
        assert_eq!(out[3].left, 3.0, "oldest frames are retained, newest dropped");
    }

    #[test]
    fn underrun_is_counted_and_partial() {
        let (mut p, mut c, _) = frame_queue(16);
        p.push(&ramp(0, 3));
        let mut out = [Frame::new(9.0, 9.0); 5];
        assert_eq!(c.pop(&mut out), 3);
        let stats = c.stats();
        assert_eq!(stats.underrun_frames, 2);
        assert_eq!(stats.underrun_events, 1);
    }

    #[test]
    fn requested_clear_is_applied_at_next_pop() {
        let (mut p, mut c, m) = frame_queue(16);
        p.push(&ramp(0, 10));
        m.request_clear();
        assert!(m.clear_pending());
        let mut out = [Frame::SILENT; 4];
        assert_eq!(c.pop(&mut out), 0, "stale frames are discarded, not replayed");
        assert!(!m.clear_pending());
        p.push(&ramp(100, 2));
        assert_eq!(c.pop(&mut out[..2]), 2);
        assert_eq!(out[0].left, 100.0);
    }

    #[test]
    fn consumer_clear_acknowledges_requests() {
        let (mut p, mut c, m) = frame_queue(16);
        p.push(&ramp(0, 10));
        p.request_clear();
        c.clear();
        assert_eq!(m.size(), 0);
        assert!(!m.clear_pending());
    }

    #[test]
    fn concurrent_producer_consumer_keep_sequence() {
        const TOTAL: usize = 200_000;
        let (mut p, mut c, _) = frame_queue(97);
        let producer = std::thread::spawn(move || {
            let mut sent = 0;
            while sent < TOTAL {
                let n = (TOTAL - sent).min(13);
                sent += p.push(&ramp(sent, n));
                std::hint::spin_loop();
            }
        });
        let mut expected = 0usize;
        let mut out = [Frame::SILENT; 11];
        while expected < TOTAL {
            let n = c.pop(&mut out);
            for f in &out[..n] {
                assert_eq!(f.left, expected as f32);
                assert_eq!(f.right, -(expected as f32));
                expected += 1;
            }
        }
        producer.join().unwrap();
    }
}
