//! Whole-mixer elapsed time against the 5 ms window (port of
//! `native/include/am/processing_load.hpp`, post-0.5 IDEA-057).
//!
//! Not process CPU utilization or proof of an audible dropout. The audio
//! thread owns the fixed-size window; readers only use the atomic
//! publication. Measured intervals include every node and both graphs during
//! a routing fade. Nothing here changes scheduling, sound or graph limits.

use std::sync::atomic::{AtomicU64, Ordering};

use vinemix_model::status::{ProcessingLoadState, ProcessingLoadStatus};
use vinemix_model::SAMPLE_RATE;
use vinemix_rt::QUANTUM_FRAMES;

const WINDOW: usize = SAMPLE_RATE as usize / QUANTUM_FRAMES; // one second of audio

#[derive(Clone, Copy, Debug, Default)]
struct Sample {
    percent: f64,
    overrun: bool,
    deadline_miss: bool,
    transition: bool,
}

/// The audio thread's rolling one-second window and warning policy.
#[derive(Debug)]
pub struct ProcessingLoadMonitor {
    samples: [Sample; WINDOW],
    position: usize,
    count: usize,
    sum: f64,
    sample_count: u64,
    overruns: u64,
    misses: u64,
    recent_overruns: u32,
    recent_misses: u32,
    recovery: u32,
    state: ProcessingLoadState,
    transition: bool,
}

impl Default for ProcessingLoadMonitor {
    fn default() -> Self {
        Self {
            samples: [Sample::default(); WINDOW],
            position: 0,
            count: 0,
            sum: 0.0,
            sample_count: 0,
            overruns: 0,
            misses: 0,
            recent_overruns: 0,
            recent_misses: 0,
            recovery: 0,
            state: ProcessingLoadState::Normal,
            transition: false,
        }
    }
}

impl ProcessingLoadMonitor {
    pub const WINDOW_CAPACITY: usize = WINDOW;
    pub const RECOVERY_SAMPLES: u32 = 2 * WINDOW as u32;
    /// Publications at least 10 times a second.
    pub const PUBLICATION_SAMPLES: u64 = WINDOW as u64 / 10;
    // Engineering policy, not a universal hardware guarantee: a full
    // one-second window at 70% is busy; repeated misses or a 100% average are
    // overloaded. Recovery needs two seconds below 60% with no new misses. A
    // steady peak at 40% leaves less than a 20% reserve if old/new work doubles.
    pub const BUSY_PERCENT: f64 = 70.0;
    pub const RECOVERY_PERCENT: f64 = 60.0;
    pub const TRANSITION_ADVISORY_PERCENT: f64 = 40.0;
    pub const REPEATED_MISSES: u32 = 3;

    pub fn reset_recent(&mut self) {
        self.samples = [Sample::default(); WINDOW];
        self.position = 0;
        self.count = 0;
        self.sum = 0.0;
        self.recent_overruns = 0;
        self.recent_misses = 0;
        self.recovery = 0;
        self.state = ProcessingLoadState::Normal;
        self.transition = false;
    }

    /// Uses the gap since completed work, not start-to-start time: repeated
    /// work taking more than 250 ms is severe overload and must not keep
    /// erasing itself.
    pub fn reset_after_idle(&mut self, start: i64, previous_completion: i64, frequency: i64) -> bool {
        if start < 0 || previous_completion < 0 || frequency <= 0 || start < previous_completion || start - previous_completion <= frequency / 4 {
            return false;
        }
        self.reset_recent();
        true
    }

    /// A late wake can miss its absolute deadline even when its DSP takes
    /// less than 5 ms.
    pub fn observe_ticks(&mut self, start: i64, end: i64, completion_deadline: i64, frequency: i64, transition: bool) -> bool {
        if start < 0 || end < start || completion_deadline < 0 || frequency <= 0 {
            self.reset_recent();
            return false;
        }
        let percent = ((end - start) as f64 / frequency as f64) * (100.0 * f64::from(SAMPLE_RATE) / QUANTUM_FRAMES as f64);
        self.observe(percent, end > completion_deadline, transition)
    }

    pub fn observe(&mut self, percent: f64, deadline_miss: bool, transition: bool) -> bool {
        // Invalid timing must never create a false healthy zero or poison
        // later readings: retain session counters, make recent data
        // unavailable, then resume from the next valid quantum.
        if !percent.is_finite() || percent < 0.0 || percent > 1.0e12 {
            self.reset_recent();
            return false;
        }
        let slot = &mut self.samples[self.position];
        if self.count == WINDOW {
            self.sum -= slot.percent;
            self.recent_overruns -= u32::from(slot.overrun);
            self.recent_misses -= u32::from(slot.deadline_miss);
        } else {
            self.count += 1;
        }
        *slot = Sample { percent, overrun: percent > 100.0, deadline_miss, transition };
        let overrun = slot.overrun;
        self.sum += percent;
        self.recent_overruns += u32::from(overrun);
        self.recent_misses += u32::from(deadline_miss);
        self.position = (self.position + 1) % WINDOW;
        self.sample_count = self.sample_count.saturating_add(1);
        if overrun {
            self.overruns = self.overruns.saturating_add(1);
        }
        if deadline_miss {
            self.misses = self.misses.saturating_add(1);
        }
        self.transition = transition;
        let average = self.sum.max(0.0) / self.count as f64;
        if self.recent_overruns >= Self::REPEATED_MISSES || self.recent_misses >= Self::REPEATED_MISSES || (self.count == WINDOW && average >= 100.0) {
            self.state = ProcessingLoadState::Overloaded;
            self.recovery = 0;
        } else if self.count == WINDOW && average >= Self::BUSY_PERCENT {
            if self.state != ProcessingLoadState::Overloaded {
                self.state = ProcessingLoadState::Busy;
            }
            self.recovery = 0;
        } else if self.state != ProcessingLoadState::Normal {
            if average < Self::RECOVERY_PERCENT && !overrun && !deadline_miss {
                self.recovery += 1;
            } else {
                self.recovery = 0;
            }
            if self.recovery >= Self::RECOVERY_SAMPLES {
                self.state = ProcessingLoadState::Normal;
                self.recovery = 0;
            }
        }
        true
    }

    pub fn state(&self) -> ProcessingLoadState {
        self.state
    }

    pub fn sample_count(&self) -> u64 {
        self.sample_count
    }

    pub fn window_samples(&self) -> usize {
        self.count
    }

    pub fn transition_active(&self) -> bool {
        self.transition
    }

    pub fn read(&self) -> ProcessingLoadStatus {
        let mut status = ProcessingLoadStatus {
            available: self.count != 0,
            sample_count: self.sample_count,
            window_samples: self.count as u32,
            average_percent: if self.count == 0 { 0.0 } else { self.sum.max(0.0) / self.count as f64 },
            processing_overruns: self.overruns,
            deadline_misses: self.misses,
            recent_processing_overruns: self.recent_overruns,
            recent_deadline_misses: self.recent_misses,
            state: self.state,
            transition_active: self.transition,
            ..ProcessingLoadStatus::default()
        };
        for slot in &self.samples[..self.count] {
            status.peak_percent = status.peak_percent.max(slot.percent);
            let peak = if slot.transition { &mut status.transition_peak_percent } else { &mut status.steady_peak_percent };
            *peak = peak.max(slot.percent);
        }
        status.transition_headroom_limited = status.steady_peak_percent >= Self::TRANSITION_ADVISORY_PERCENT;
        status
    }
}

/// Single audio-thread writer, any number of status readers. Every payload
/// word is atomic (no data race); a reader overlapping four publications
/// returns "unavailable" rather than mixed generations. Bounded work: no
/// locks, allocation or waiting. Polling never resets peaks or counters.
#[derive(Debug, Default)]
pub struct ProcessingLoadPublication {
    sequence: AtomicU64,
    words: [AtomicU64; 14],
}

impl ProcessingLoadPublication {
    pub fn publish(&self, value: &ProcessingLoadStatus) {
        let words = [
            u64::from(value.available),
            value.sample_count,
            u64::from(value.window_samples),
            value.average_percent.to_bits(),
            value.peak_percent.to_bits(),
            value.steady_peak_percent.to_bits(),
            value.transition_peak_percent.to_bits(),
            value.processing_overruns,
            value.deadline_misses,
            u64::from(value.recent_processing_overruns),
            u64::from(value.recent_deadline_misses),
            value.state as u64,
            u64::from(value.transition_active),
            u64::from(value.transition_headroom_limited),
        ];
        self.sequence.fetch_add(1, Ordering::SeqCst);
        for (slot, word) in self.words.iter().zip(words) {
            slot.store(word, Ordering::SeqCst);
        }
        self.sequence.fetch_add(1, Ordering::SeqCst);
    }

    pub fn read(&self) -> ProcessingLoadStatus {
        for _ in 0..4 {
            let before = self.sequence.load(Ordering::SeqCst);
            if before & 1 != 0 {
                continue;
            }
            let words: [u64; 14] = std::array::from_fn(|i| self.words[i].load(Ordering::SeqCst));
            if self.sequence.load(Ordering::SeqCst) != before {
                continue;
            }
            return ProcessingLoadStatus {
                available: words[0] != 0,
                sample_count: words[1],
                window_samples: words[2] as u32,
                average_percent: f64::from_bits(words[3]),
                peak_percent: f64::from_bits(words[4]),
                steady_peak_percent: f64::from_bits(words[5]),
                transition_peak_percent: f64::from_bits(words[6]),
                processing_overruns: words[7],
                deadline_misses: words[8],
                recent_processing_overruns: words[9] as u32,
                recent_deadline_misses: words[10] as u32,
                state: match words[11] {
                    1 => ProcessingLoadState::Busy,
                    2 => ProcessingLoadState::Overloaded,
                    _ => ProcessingLoadState::Normal,
                },
                transition_active: words[12] != 0,
                transition_headroom_limited: words[13] != 0,
                ..ProcessingLoadStatus::default()
            };
        }
        ProcessingLoadStatus::default()
    }
}

/// Port of `native/tests/processing_load_tests.cpp`.
#[cfg(test)]
mod tests {
    use super::*;
    use vinemix_rt::mix_clock::MixClock;

    fn close(a: f64, b: f64) -> bool {
        (a - b).abs() < 1e-9
    }

    #[test]
    fn timing_and_boundaries() {
        let mut monitor = ProcessingLoadMonitor::default();
        let value = monitor.read();
        assert!(!value.available && value.sample_count == 0 && value.window_samples == 0 && value.budget_ms == 5.0);
        let mut clock = MixClock::new(50, 1000);
        assert_eq!(clock.completion_deadline(), 1050);
        assert!(monitor.observe_ticks(1000, 1050, clock.completion_deadline(), 10000, false));
        let value = monitor.read();
        assert!(close(value.average_percent, 100.0) && value.processing_overruns == 0 && value.deadline_misses == 0, "exactly 5 ms is 100% but neither an overrun nor a miss");
        let _ = clock.advance(1051);
        assert_eq!(clock.completion_deadline(), 1100);
        monitor.observe_ticks(1110, 1111, clock.completion_deadline(), 10000, false);
        let value = monitor.read();
        assert!(value.processing_overruns == 0 && value.deadline_misses == 1 && close(value.average_percent, 51.0), "a late wake is a deadline miss");
        monitor.observe_ticks(0, 51, 100, 10000, false);
        let value = monitor.read();
        assert!(value.processing_overruns == 1 && value.deadline_misses == 1 && close(value.peak_percent, 102.0));
        clock.reset(10000);
        assert_eq!(clock.completion_deadline(), 10050);
    }

    #[test]
    fn warning_policy() {
        let mut spike = ProcessingLoadMonitor::default();
        spike.observe(180.0, true, false);
        assert_eq!(spike.read().state, ProcessingLoadState::Normal);
        for _ in 0..ProcessingLoadMonitor::WINDOW_CAPACITY {
            spike.observe(10.0, false, false);
        }
        let value = spike.read();
        assert!(value.state == ProcessingLoadState::Normal && value.peak_percent == 10.0 && value.recent_processing_overruns == 0 && value.recent_deadline_misses == 0 && value.processing_overruns == 1 && value.deadline_misses == 1);

        let mut sustained = ProcessingLoadMonitor::default();
        for _ in 0..199 {
            sustained.observe(70.0, false, false);
        }
        assert_eq!(sustained.read().state, ProcessingLoadState::Normal, "busy needs a full second");
        sustained.observe(70.0, false, false);
        assert_eq!(sustained.read().state, ProcessingLoadState::Busy);
        for _ in 1..428 {
            sustained.observe(0.0, false, false);
            assert_eq!(sustained.read().state, ProcessingLoadState::Busy, "recovery waits 400 quanta below 60%");
        }
        sustained.observe(0.0, false, false);
        assert_eq!(sustained.read().state, ProcessingLoadState::Normal);

        let mut misses = ProcessingLoadMonitor::default();
        misses.observe(10.0, true, false);
        misses.observe(10.0, true, false);
        assert_eq!(misses.read().state, ProcessingLoadState::Normal);
        misses.observe(10.0, true, false);
        assert!(misses.read().state == ProcessingLoadState::Overloaded && misses.read().processing_overruns == 0);
        let mut overruns = ProcessingLoadMonitor::default();
        for _ in 0..3 {
            overruns.observe(101.0, false, false);
        }
        assert!(overruns.read().state == ProcessingLoadState::Overloaded && overruns.read().deadline_misses == 0);
        let mut boundary = ProcessingLoadMonitor::default();
        for _ in 0..200 {
            boundary.observe(100.0, false, false);
        }
        assert!(boundary.read().state == ProcessingLoadState::Overloaded && boundary.read().processing_overruns == 0);
        for _ in 0..1000 {
            boundary.observe(60.0, false, false);
        }
        assert_eq!(boundary.read().state, ProcessingLoadState::Overloaded, "exactly 60% does not recover");
        for _ in 0..600 {
            boundary.observe(59.0, false, false);
        }
        assert_eq!(boundary.read().state, ProcessingLoadState::Normal);
    }

    #[test]
    fn transition_and_reset() {
        let mut monitor = ProcessingLoadMonitor::default();
        monitor.observe(39.9, false, false);
        assert!(!monitor.read().transition_headroom_limited);
        monitor.observe(40.0, false, false);
        assert!(monitor.read().transition_headroom_limited && monitor.read().state == ProcessingLoadState::Normal);
        monitor.observe(80.0, false, true);
        monitor.observe(90.0, false, true);
        let value = monitor.read();
        assert!(value.transition_active && value.steady_peak_percent == 40.0 && value.transition_peak_percent == 90.0 && value.peak_percent == 90.0 && close(value.average_percent, 62.475));
        monitor.observe(30.0, false, false);
        assert!(!monitor.read().transition_active && monitor.read().transition_peak_percent == 90.0);
        for _ in 0..200 {
            monitor.observe(10.0, false, false);
        }
        assert!(monitor.read().transition_peak_percent == 0.0 && !monitor.read().transition_headroom_limited);
        monitor.observe(130.0, true, false);
        let total = monitor.sample_count();
        monitor.reset_recent();
        let value = monitor.read();
        assert!(!value.available && value.window_samples == 0 && value.sample_count == total && value.deadline_misses == 1 && value.processing_overruns == 1 && value.state == ProcessingLoadState::Normal && !value.transition_headroom_limited);
        monitor.observe(20.0, false, false);
        assert!(monitor.read().available && monitor.read().window_samples == 1 && monitor.sample_count() == total + 1);
        for invalid in [f64::NAN, f64::INFINITY, -1.0, f64::MAX] {
            let before = monitor.sample_count();
            assert!(!monitor.observe(invalid, true, true) && !monitor.read().available && monitor.sample_count() == before);
            monitor.observe(10.0, false, false);
        }
        assert!(!monitor.observe_ticks(2, 1, 5, 10000, false) && !monitor.read().available);
        assert!(!monitor.observe_ticks(0, 1, 5, 0, false));
        assert!(!monitor.observe_ticks(-1, 1, 5, 10000, false));
        assert!(!monitor.observe_ticks(0, 1, -1, 10000, false));
    }

    #[test]
    fn long_work_versus_idle() {
        let mut monitor = ProcessingLoadMonitor::default();
        let frequency = 10000;
        let mut previous_end = 0;
        for _ in 0..3 {
            let start = previous_end + 50;
            assert!(!monitor.reset_after_idle(start, previous_end, frequency), "long work is not idle");
            let end = start + 3000;
            monitor.observe_ticks(start, end, start + 50, frequency, false);
            previous_end = end;
        }
        let value = monitor.read();
        assert!(value.state == ProcessingLoadState::Overloaded && value.sample_count == 3 && value.recent_processing_overruns == 3 && value.recent_deadline_misses == 3 && close(value.average_percent, 6000.0));
        assert!(!monitor.reset_after_idle(previous_end + 2500, previous_end, frequency), "exactly 250 ms keeps the window");
        assert!(monitor.reset_after_idle(previous_end + 2501, previous_end, frequency), "a longer idle gap re-primes telemetry");
        let value = monitor.read();
        assert!(!value.available && value.sample_count == 3 && value.processing_overruns == 3 && value.deadline_misses == 3 && value.state == ProcessingLoadState::Normal);
        monitor.observe_ticks(previous_end + 2501, previous_end + 2502, previous_end + 2551, frequency, false);
        assert!(monitor.read().available && monitor.read().window_samples == 1 && close(monitor.read().average_percent, 2.0));
    }

    #[test]
    fn rolling_reference() {
        let mut monitor = ProcessingLoadMonitor::default();
        let publication = ProcessingLoadPublication::default();
        let mut reference = [0.0f64; 200];
        let mut transitions = [false; 200];
        let mut misses = [false; 200];
        let mut seed: u32 = 0x0050_0051;
        for step in 0..4096usize {
            seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
            let percent = f64::from(seed % 180_001) / 1000.0;
            let (transition, missed) = (step % 41 < 2, step % 17 == 0);
            let slot = step % 200;
            reference[slot] = percent;
            transitions[slot] = transition;
            misses[slot] = missed;
            monitor.observe(percent, missed, transition);
            let count = (step + 1).min(200);
            let (mut sum, mut peak, mut steady, mut fade, mut overruns, mut missed_count) = (0.0, 0.0f64, 0.0f64, 0.0f64, 0u32, 0u32);
            for i in 0..count {
                sum += reference[i];
                peak = peak.max(reference[i]);
                if transitions[i] { fade = fade.max(reference[i]) } else { steady = steady.max(reference[i]) }
                overruns += u32::from(reference[i] > 100.0);
                missed_count += u32::from(misses[i]);
            }
            publication.publish(&monitor.read());
            let value = publication.read();
            assert!(value.available && value.sample_count == step as u64 + 1 && value.window_samples as usize == count && (value.average_percent - sum / count as f64).abs() < 1e-6 && value.peak_percent == peak && value.steady_peak_percent == steady && value.transition_peak_percent == fade && value.recent_processing_overruns == overruns && value.recent_deadline_misses == missed_count, "step {step}");
            assert_eq!(publication.read(), value, "reading never consumes peaks or counters");
        }
    }

    #[test]
    fn concurrent_publication_is_never_torn() {
        use std::sync::atomic::AtomicBool;
        use std::sync::Arc;
        let publication = Arc::new(ProcessingLoadPublication::default());
        let make = |n: u64| ProcessingLoadStatus {
            available: true,
            sample_count: n,
            window_samples: (n % 200 + 1) as u32,
            average_percent: n as f64,
            peak_percent: n as f64 + 1.0,
            steady_peak_percent: n as f64 + 2.0,
            transition_peak_percent: n as f64 + 3.0,
            processing_overruns: n + 4,
            deadline_misses: n + 5,
            ..ProcessingLoadStatus::default()
        };
        publication.publish(&make(0));
        let done = Arc::new(AtomicBool::new(false));
        let readers: Vec<_> = (0..2)
            .map(|_| {
                let (publication, done) = (Arc::clone(&publication), Arc::clone(&done));
                std::thread::spawn(move || {
                    let mut observed = 0u64;
                    loop {
                        let finished = done.load(Ordering::SeqCst);
                        let value = publication.read();
                        if value.available {
                            let n = value.sample_count;
                            assert!(value.average_percent == n as f64 && value.peak_percent == n as f64 + 1.0 && value.transition_peak_percent == n as f64 + 3.0 && value.deadline_misses == n + 5 && value.window_samples as u64 == n % 200 + 1, "torn read at {n}");
                            observed += 1;
                        }
                        if finished {
                            return observed;
                        }
                    }
                })
            })
            .collect();
        for n in 1..=20_000 {
            publication.publish(&make(n));
        }
        done.store(true, Ordering::SeqCst);
        for reader in readers {
            assert!(reader.join().unwrap() >= 1);
        }
        publication.publish(&ProcessingLoadStatus::default());
        assert!(!publication.read().available && publication.read().sample_count == 0, "a new session clears the publication");
    }

    #[test]
    fn publication_round_trips() {
        let publication = ProcessingLoadPublication::default();
        assert!(!publication.read().available);
        let mut monitor = ProcessingLoadMonitor::default();
        monitor.observe(42.5, true, true);
        let value = monitor.read();
        publication.publish(&value);
        assert_eq!(publication.read(), value);
        let json = serde_json::to_value(&value).unwrap();
        assert_eq!(json["state"], "normal");
        assert_eq!(json["budgetMs"], 5.0);
        assert_eq!(json["recentDeadlineMisses"], 1);
    }
}
