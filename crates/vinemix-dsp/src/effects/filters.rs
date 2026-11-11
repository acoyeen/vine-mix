//! Fixed-tuning filters shared by the original effect designs.
//!
//! The trapezoidal state-variable and one-pole forms described by Andrew
//! Simper (Cytomic, 2013), implemented from the published equations.

use std::f64::consts::PI;

use vinemix_model::SAMPLE_RATE;

use crate::util::denormal;

pub(super) type Pair = [f64; 2];

/// A trapezoidal state-variable filter with fixed tuning.
#[derive(Clone, Copy, Debug, PartialEq)]
pub(super) struct Svf<const N: usize> {
    a1: f64,
    a2: f64,
    a3: f64,
    /// 1/Q: `k·band` is the band-pass with unit peak gain.
    pub(super) k: f64,
    ic1: [f64; N],
    ic2: [f64; N],
}

impl<const N: usize> Svf<N> {
    pub(super) fn new(hz: f64, q: f64) -> Self {
        let g = (PI * hz / f64::from(SAMPLE_RATE)).tan();
        let k = 1.0 / q;
        let a1 = 1.0 / (1.0 + g * (g + k));
        let a2 = g * a1;
        Self { a1, a2, a3: g * a2, k, ic1: [0.0; N], ic2: [0.0; N] }
    }

    /// Returns `(band, low)`; the high-pass is `x - k·band - low`.
    #[inline]
    pub(super) fn step(&mut self, x: [f64; N]) -> ([f64; N], [f64; N]) {
        let mut band = [0.0; N];
        let mut low = [0.0; N];
        for c in 0..N {
            let v3 = x[c] - self.ic2[c];
            let v1 = self.a1 * self.ic1[c] + self.a2 * v3;
            let v2 = self.ic2[c] + self.a2 * self.ic1[c] + self.a3 * v3;
            self.ic1[c] = denormal(2.0 * v1 - self.ic1[c]);
            self.ic2[c] = denormal(2.0 * v2 - self.ic2[c]);
            band[c] = v1;
            low[c] = v2;
        }
        (band, low)
    }
}

/// A trapezoidal one-pole low-pass with fixed tuning.
#[derive(Clone, Copy, Debug, PartialEq)]
pub(super) struct OnePole {
    g: f64,
    state: Pair,
}

impl OnePole {
    pub(super) fn new(hz: f64) -> Self {
        let g = (PI * hz / f64::from(SAMPLE_RATE)).tan();
        Self { g: g / (1.0 + g), state: [0.0; 2] }
    }

    #[inline]
    pub(super) fn low(&mut self, x: Pair) -> Pair {
        let mut out = [0.0; 2];
        for c in 0..2 {
            let v = (x[c] - self.state[c]) * self.g;
            out[c] = v + self.state[c];
            self.state[c] = denormal(out[c] + v);
        }
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn filters_have_the_documented_shapes() {
        // Unit-peak band-pass at its centre, and complementary one-pole halves.
        let mut bell: Svf<1> = Svf::new(1_000.0, 2.0);
        let mut peak = 0.0f64;
        for n in 0..48_000 {
            let x = (2.0 * PI * 1_000.0 * f64::from(n) / 48_000.0).sin();
            let ([band], _) = bell.step([x]);
            if n > 24_000 {
                peak = peak.max((bell.k * band).abs());
            }
        }
        assert!((peak - 1.0).abs() < 1e-3, "band peak {peak}");
        let mut pole = OnePole::new(1_000.0);
        let mut low = 0.0f64;
        for n in 0..48_000 {
            let x = (2.0 * PI * 1_000.0 * f64::from(n) / 48_000.0).sin();
            let out = pole.low([x, x]);
            if n > 24_000 {
                low = low.max(out[0].abs());
            }
        }
        assert!((low - std::f64::consts::FRAC_1_SQRT_2).abs() < 1e-3, "one-pole at its corner {low}");
    }
}
