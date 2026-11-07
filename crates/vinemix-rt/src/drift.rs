//! Queue-occupancy drift controller (port of `am::DriftController`).
//!
//! Runs on the stream-owning thread. The queue may be a capture stream's
//! produced queue or a renderer's input queue: in both cases high occupancy
//! must lower the output/input ratio. Correction is bounded to ±2,000 ppm and
//! slewed.

/// Maximum resampling-ratio correction (±0.2 %).
pub const MAXIMUM_DRIFT: f64 = 0.002;

#[derive(Clone, Copy, Debug)]
pub struct DriftController {
    filtered_error: f64,
    integral: f64,
    correction: f64,
}

impl Default for DriftController {
    fn default() -> Self {
        Self { filtered_error: 0.0, integral: 0.0, correction: 1.0 }
    }
}

impl DriftController {
    pub fn new() -> Self {
        Self::default()
    }

    /// Updates the controller and returns the ratio multiplier to apply.
    pub fn update(&mut self, queue_frames: f64, target_frames: f64, elapsed_seconds: f64) -> f64 {
        if !queue_frames.is_finite()
            || !target_frames.is_finite()
            || !elapsed_seconds.is_finite()
            || target_frames <= 0.0
        {
            self.reset();
            return self.correction;
        }
        if elapsed_seconds <= 0.0 {
            return self.correction;
        }
        let dt = elapsed_seconds.min(0.25);
        let error = ((queue_frames - target_frames) / target_frames).clamp(-4.0, 4.0);
        let smoothing = 1.0 - (-dt / 0.4).exp();
        self.filtered_error += smoothing * (error - self.filtered_error);
        self.integral = (self.integral + self.filtered_error * dt * 0.0006).clamp(-0.0015, 0.0015);
        let desired = (1.0 - self.filtered_error * 0.001 - self.integral)
            .clamp(1.0 - MAXIMUM_DRIFT, 1.0 + MAXIMUM_DRIFT);
        let maximum_step = 0.0005 * dt;
        self.correction += (desired - self.correction).clamp(-maximum_step, maximum_step);
        self.correction
    }

    pub fn reset(&mut self) {
        *self = Self::default();
    }

    pub fn correction(&self) -> f64 {
        self.correction
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn steady_target_keeps_unity() {
        let mut drift = DriftController::new();
        for _ in 0..10_000 {
            assert_eq!(drift.update(720.0, 720.0, 0.005), 1.0);
        }
    }

    #[test]
    fn high_occupancy_lowers_ratio_within_bounds_and_slew() {
        let mut drift = DriftController::new();
        let mut previous = drift.correction();
        for _ in 0..100_000 {
            let next = drift.update(4000.0, 720.0, 0.01);
            assert!(next <= previous + 1e-15, "correction moves monotonically down");
            assert!(previous - next <= 0.0005 * 0.01 + 1e-15, "slew is bounded");
            previous = next;
        }
        assert!((previous - (1.0 - MAXIMUM_DRIFT)).abs() < 1e-12);
    }

    #[test]
    fn invalid_input_resets() {
        let mut drift = DriftController::new();
        drift.update(4000.0, 720.0, 0.25);
        assert!(drift.correction() < 1.0);
        assert_eq!(drift.update(f64::NAN, 720.0, 0.01), 1.0);
        drift.update(4000.0, 720.0, 0.25);
        assert_eq!(drift.update(100.0, 0.0, 0.01), 1.0);
    }

    #[test]
    fn non_positive_elapsed_is_ignored() {
        let mut drift = DriftController::new();
        let value = drift.update(4000.0, 720.0, 0.25);
        assert_eq!(drift.update(0.0, 720.0, 0.0), value);
        assert_eq!(drift.update(0.0, 720.0, -1.0), value);
    }
}
