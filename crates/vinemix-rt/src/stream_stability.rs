//! Running-stream glitch tracker (port of `stream_stability.hpp`).
//!
//! Called only for genuine running-stream glitches, with monotonic seconds
//! since the stream opened. Startup discontinuities do not judge a device's
//! shortest period. Three glitches within ten seconds request a conservative
//! reopen; the owner keeps that preference until an explicit retry or change.

#[derive(Clone, Copy, Debug, Default)]
pub struct StreamStability {
    glitches: [f64; 3],
    count: usize,
    latched: bool,
}

impl StreamStability {
    const WARMUP_SECONDS: f64 = 1.0;
    const WINDOW_SECONDS: f64 = 10.0;
    /// Glitches this close together are one incident (one hiccup can flag
    /// several consecutive packets).
    const BURST_SECONDS: f64 = 0.25;

    /// Records a glitch; returns `true` once compatible timing is required.
    #[must_use]
    pub fn observe(&mut self, seconds_since_start: f64) -> bool {
        if self.latched {
            return true;
        }
        if !seconds_since_start.is_finite() || seconds_since_start < Self::WARMUP_SECONDS {
            return false;
        }
        // A reset clock must not combine a new stream's incidents with older
        // timestamps if an owner reuses this otherwise per-open tracker.
        if self.count > 0 && seconds_since_start < self.glitches[self.count - 1] {
            self.count = 0;
        }
        if self.count > 0 && seconds_since_start - self.glitches[self.count - 1] < Self::BURST_SECONDS {
            return false;
        }
        let mut retained = 0;
        for i in 0..self.count {
            if seconds_since_start - self.glitches[i] <= Self::WINDOW_SECONDS {
                self.glitches[retained] = self.glitches[i];
                retained += 1;
            }
        }
        self.count = retained;
        self.glitches[self.count] = seconds_since_start;
        self.count += 1;
        self.latched = self.count == self.glitches.len();
        self.latched
    }

    /// Forgets incidents at or after `seconds_since_start`: a device was added,
    /// removed or reconfigured, and the Windows audio service briefly stalls
    /// every stream while it rebuilds. That is not evidence this stream needs
    /// slower timing. A latched tracker stays latched.
    pub fn forgive_since(&mut self, seconds_since_start: f64) {
        if self.latched {
            return;
        }
        let mut retained = 0;
        for i in 0..self.count {
            if self.glitches[i] < seconds_since_start {
                self.glitches[retained] = self.glitches[i];
                retained += 1;
            }
        }
        self.count = retained;
    }
}

#[cfg(test)]
mod tests {
    use super::StreamStability;

    #[test]
    fn warmup_glitches_are_ignored() {
        let mut s = StreamStability::default();
        for t in [0.0, 0.2, 0.5, 0.99] {
            assert!(!s.observe(t));
        }
        assert!(!s.observe(f64::NAN));
    }

    #[test]
    fn a_burst_is_one_incident() {
        let mut s = StreamStability::default();
        for t in [2.0, 2.01, 2.05, 2.2] {
            assert!(!s.observe(t));
        }
        assert!(!s.observe(3.0));
        assert!(s.observe(4.0), "three separate incidents still latch");
    }

    #[test]
    fn device_changes_are_forgiven() {
        let mut s = StreamStability::default();
        assert!(!s.observe(2.0));
        assert!(!s.observe(4.0));
        s.forgive_since(3.0);
        assert!(!s.observe(5.0), "the 4.0 incident was forgiven");
        assert!(s.observe(6.0));
        s.forgive_since(0.0);
        assert!(s.observe(7.0), "latched trackers are not reset");
    }

    #[test]
    fn three_within_ten_seconds_latch() {
        let mut s = StreamStability::default();
        assert!(!s.observe(1.0));
        assert!(!s.observe(5.0));
        assert!(s.observe(11.0));
        assert!(s.observe(500.0), "latched until the owner reopens with a new tracker");
    }

    #[test]
    fn spaced_glitches_never_latch() {
        let mut s = StreamStability::default();
        for i in 0..100 {
            assert!(!s.observe(1.0 + f64::from(i) * 5.1));
        }
    }

    #[test]
    fn clock_reset_discards_older_incidents() {
        let mut s = StreamStability::default();
        assert!(!s.observe(20.0));
        assert!(!s.observe(21.0));
        assert!(!s.observe(2.0), "an earlier timestamp starts a fresh window");
        assert!(!s.observe(3.0));
        assert!(s.observe(4.0));
    }
}
