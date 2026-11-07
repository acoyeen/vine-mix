//! Application queue budgets (port of `native/include/am/audio_timing.hpp`).
//!
//! Queue budgets use the graph's 48 kHz frames, including on non-48 kHz
//! devices. This is application buffering only; it does not estimate
//! end-to-end latency. Growth belongs to a real, running stream shortfall,
//! never silent priming.

use crate::frame_queue::Consumer;
use crate::{Frame, QUANTUM_FRAMES, SAMPLE_RATE};

/// Returned when a device reports a zero sample rate.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ZeroDeviceRate;

impl std::fmt::Display for ZeroDeviceRate {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("Audio device rate must be positive")
    }
}
impl std::error::Error for ZeroDeviceRate {}

#[derive(Clone, Copy, Debug)]
pub struct StreamBufferPolicy {
    device_rate: u32,
    capture: bool,
    period_frames: usize,
    target_frames: usize,
    maximum_target_frames: usize,
}

impl StreamBufferPolicy {
    pub const RESAMPLER_RESERVE_FRAMES: usize = 64;
    pub const CAPTURE_RESERVE_FRAMES: usize = QUANTUM_FRAMES + QUANTUM_FRAMES / 2;

    /// `period_device_frames == 0` means the device exposes no negotiated
    /// period; a conservative 10 ms is assumed until demand is measured.
    pub fn new(device_rate: u32, period_device_frames: u32, capture: bool) -> Result<Self, ZeroDeviceRate> {
        if device_rate == 0 {
            return Err(ZeroDeviceRate);
        }
        let mut policy = Self { device_rate, capture, period_frames: 0, target_frames: 0, maximum_target_frames: 0 };
        let sample_rate = SAMPLE_RATE as usize;
        policy.period_frames = if period_device_frames != 0 {
            policy.device_frames_to_engine_frames(period_device_frames)
        } else {
            sample_rate / 100
        };
        policy.target_frames = if capture {
            Self::CAPTURE_RESERVE_FRAMES
        } else {
            policy.period_frames.max(QUANTUM_FRAMES) + QUANTUM_FRAMES + Self::RESAMPLER_RESERVE_FRAMES
        };
        // Step back toward legacy buffering after actual shortfalls, with a
        // finite ceiling. Very long device periods still need their base budget.
        policy.maximum_target_frames = policy
            .target_frames
            .max((sample_rate / 20).min(sample_rate * 15 / 1000 + policy.period_frames));
        Ok(policy)
    }

    /// Converts device-rate frames to graph frames, rounding up.
    pub fn device_frames_to_engine_frames(&self, frames: u32) -> usize {
        let rate = u64::from(self.device_rate);
        ((u64::from(frames) * u64::from(SAMPLE_RATE) + rate - 1) / rate) as usize
    }

    pub fn target_frames(&self) -> usize {
        self.target_frames
    }

    pub fn maximum_target_frames(&self) -> usize {
        self.maximum_target_frames
    }

    /// Demand is the current callback size in graph frames. Readiness must
    /// count both the external queue and pending converter input.
    pub fn prime_frames(&self, demand_engine_frames: usize) -> usize {
        if self.capture {
            return self.target_frames;
        }
        let reserve = (QUANTUM_FRAMES + Self::RESAMPLER_RESERVE_FRAMES).min(usize::MAX - demand_engine_frames);
        self.target_frames.max(demand_engine_frames + reserve)
    }

    /// Returns whether the target grew. The owner decides which events are
    /// genuine shortfalls and recreates the policy when reopening a stream.
    pub fn observe_underrun(&mut self) -> bool {
        let increase = QUANTUM_FRAMES.min(self.maximum_target_frames - self.target_frames);
        self.target_frames += increase;
        increase != 0
    }
}

/// Consumer-owned capture startup/recovery. Waiting for the requested block
/// plus the capture reserve avoids building the initial queue through tiny
/// drift corrections over several seconds. Priming silence is not an underrun.
#[derive(Clone, Copy, Debug, Default)]
pub struct CaptureBufferReader {
    primed: bool,
}

impl CaptureBufferReader {
    pub fn read(
        &mut self,
        queue: &mut Consumer,
        output: &mut [Frame],
        ready: bool,
        period_engine_frames: usize,
        reserve_engine_frames: usize,
    ) -> usize {
        output.fill(Frame::SILENT);
        if !ready || queue.clear_pending() || queue.size() > SAMPLE_RATE as usize / 10 {
            queue.clear();
            self.reset();
            return 0;
        }
        if output.is_empty() {
            return 0;
        }
        if !self.primed {
            let block = output.len().max(period_engine_frames);
            let reserve = StreamBufferPolicy::CAPTURE_RESERVE_FRAMES
                .max(reserve_engine_frames)
                .min(usize::MAX - block);
            if queue.size() < block + reserve {
                return 0;
            }
            self.primed = true;
        }
        let count = queue.pop(output);
        if count != output.len() {
            self.reset();
        }
        count
    }

    pub fn reset(&mut self) {
        self.primed = false;
    }

    pub fn primed(&self) -> bool {
        self.primed
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::frame_queue::frame_queue;

    #[test]
    fn documented_budgets() {
        // docs/AUDIO_LATENCY.md: capture starts at 360 frames (7.5 ms).
        let capture = StreamBufferPolicy::new(48_000, 480, true).unwrap();
        assert_eq!(capture.target_frames(), 360);
        // A period at or below 5 ms uses 544 frames (about 11.33 ms).
        let short = StreamBufferPolicy::new(48_000, 240, false).unwrap();
        assert_eq!(short.target_frames(), 544);
        let shorter = StreamBufferPolicy::new(48_000, 128, false).unwrap();
        assert_eq!(shorter.target_frames(), 544);
        // Unknown legacy period assumes 10 ms.
        let legacy = StreamBufferPolicy::new(44_100, 0, false).unwrap();
        assert_eq!(legacy.target_frames(), 480 + 240 + 64);
    }

    #[test]
    fn rate_conversion_rounds_up() {
        let policy = StreamBufferPolicy::new(44_100, 441, false).unwrap();
        assert_eq!(policy.device_frames_to_engine_frames(441), 480);
        assert_eq!(policy.device_frames_to_engine_frames(1), 2);
        assert_eq!(StreamBufferPolicy::new(0, 0, false).unwrap_err(), ZeroDeviceRate);
    }

    #[test]
    fn underrun_growth_is_bounded() {
        let mut policy = StreamBufferPolicy::new(48_000, 240, false).unwrap();
        let ceiling = policy.maximum_target_frames();
        assert_eq!(ceiling, 2400.min(720 + 240).max(544));
        let mut grown = 0;
        while policy.observe_underrun() {
            grown += 1;
            assert!(grown < 100);
        }
        assert_eq!(policy.target_frames(), ceiling);
        assert!(!policy.observe_underrun());
    }

    #[test]
    fn render_priming_covers_demand_plus_reserve() {
        let policy = StreamBufferPolicy::new(48_000, 240, false).unwrap();
        assert_eq!(policy.prime_frames(0), 544);
        assert_eq!(policy.prime_frames(1000), 1000 + 240 + 64);
        assert_eq!(policy.prime_frames(usize::MAX), usize::MAX);
    }

    #[test]
    fn capture_reader_primes_then_streams() {
        let (mut producer, mut consumer, _) = frame_queue(9600);
        let mut reader = CaptureBufferReader::default();
        let mut block = [Frame::new(1.0, 1.0); QUANTUM_FRAMES];
        let ones = [Frame::new(0.5, 0.5); 1000];

        producer.push(&ones[..500]);
        assert_eq!(reader.read(&mut consumer, &mut block, true, 0, 0), 0, "240 + 360 needed");
        assert!(block.iter().all(|f| *f == Frame::SILENT), "priming outputs silence");
        producer.push(&ones[..100]);
        assert_eq!(reader.read(&mut consumer, &mut block, true, 0, 0), QUANTUM_FRAMES);
        assert!(reader.primed());

        // A not-ready stream clears and re-primes.
        assert_eq!(reader.read(&mut consumer, &mut block, false, 0, 0), 0);
        assert!(!reader.primed());
        assert_eq!(consumer.size(), 0);
    }

    #[test]
    fn capture_reader_flushes_stale_backlog() {
        let (mut producer, mut consumer, _) = frame_queue(9600);
        let mut reader = CaptureBufferReader::default();
        let mut block = [Frame::SILENT; QUANTUM_FRAMES];
        let many = vec![Frame::new(0.1, 0.1); 4801];
        producer.push(&many);
        assert_eq!(reader.read(&mut consumer, &mut block, true, 0, 0), 0);
        assert_eq!(consumer.size(), 0, "more than 100 ms queued is discarded, never replayed");
    }
}
