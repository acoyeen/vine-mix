//! Voice Color: how a pad position becomes processing amounts.
//!
//! The pad stores two numbers (`tone` and `ambience`, shown as Tone and
//! Character). This module turns them into the gains of a fixed filter set and
//! an output trim that keeps typical speech at about its dry loudness. The
//! processor in `vinemix-dsp` runs those filters; the frequency response here
//! is the same response, in closed form, and a test there holds the two
//! together.
//!
//! Every stage is one of three shapes around a fixed filter `F`:
//! `1 + k·F(s)`. Because the filters never move, only the gains `k` do, edits
//! and the sibilance guard can change the contour at any speed without
//! interpolating filter coefficients.

use std::f64::consts::{FRAC_1_SQRT_2, PI};

use crate::config::VoiceColorSettings;
use crate::SAMPLE_RATE;

/// Second-order high-pass that removes rumble as Character rises.
pub const RUMBLE_HZ: f64 = 75.0;
pub const RUMBLE_Q: f64 = FRAC_1_SQRT_2;
/// First-order shelves that tilt the voice for Tone.
pub const LOW_SHELF_HZ: f64 = 220.0;
pub const HIGH_SHELF_HZ: f64 = 3_600.0;
/// Chest resonance of a close microphone.
pub const BODY_HZ: f64 = 165.0;
pub const BODY_Q: f64 = 0.8;
/// The boxy low mids.
pub const MUD_HZ: f64 = 420.0;
pub const MUD_Q: f64 = 0.9;
/// Consonant clarity.
pub const PRESENCE_HZ: f64 = 3_300.0;
pub const PRESENCE_Q: f64 = 0.75;
/// First-order shelf above the sibilant range.
pub const AIR_HZ: f64 = 9_000.0;

/// Gains at the edges of the pad, in decibels.
pub const TONE_BOOST_DB: f64 = 6.0;
pub const TONE_CUT_DB: f64 = -5.0;
pub const BODY_DB: f64 = 7.0;
pub const MUD_DB: f64 = -5.0;
pub const PRESENCE_DB: f64 = 7.0;
pub const AIR_DB: f64 = 5.0;
/// Character at which the rumble filter is fully in.
pub const RUMBLE_FULL_AT: f64 = 0.35;

/// The processing amounts for one pad position. `Copy`, so the mixer thread
/// can store and compare it without touching the heap.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct VoiceContour {
    /// 0 leaves the lows alone; 1 is the full rumble high-pass.
    pub rumble: f64,
    /// Linear gains `k` of each stage (0 is flat, negative cuts).
    pub low: f64,
    pub body: f64,
    pub mud: f64,
    pub presence: f64,
    pub high: f64,
    pub air: f64,
    /// Depth of the dynamics and harmonics, 0 to 1.
    pub amount: f64,
    /// Linear output gain that returns typical speech to its dry loudness.
    pub trim: f64,
}

impl Default for VoiceContour {
    fn default() -> Self {
        Self::NEUTRAL
    }
}

fn gain(db: f64) -> f64 {
    10f64.powf(db / 20.0) - 1.0
}

/// Normalised analogue frequency of `hz` for a filter tuned to `corner`: the
/// trapezoidal filters in the processor have exactly the analogue response at
/// this warped frequency.
fn warped(hz: f64, corner: f64) -> f64 {
    let rate = f64::from(SAMPLE_RATE);
    (PI * hz / rate).tan() / (PI * corner / rate).tan()
}

type Complex = (f64, f64);

fn times(a: Complex, b: Complex) -> Complex {
    (a.0 * b.0 - a.1 * b.1, a.0 * b.1 + a.1 * b.0)
}

fn over(a: Complex, b: Complex) -> Complex {
    let d = b.0 * b.0 + b.1 * b.1;
    ((a.0 * b.0 + a.1 * b.1) / d, (a.1 * b.0 - a.0 * b.1) / d)
}

/// `1 + k·low-pass` (first order).
fn low_shelf(hz: f64, corner: f64, k: f64) -> Complex {
    let w = warped(hz, corner);
    let low = over((1.0, 0.0), (1.0, w));
    (1.0 + k * low.0, k * low.1)
}

/// `1 + k·high-pass` (first order).
fn high_shelf(hz: f64, corner: f64, k: f64) -> Complex {
    let w = warped(hz, corner);
    let high = over((0.0, w), (1.0, w));
    (1.0 + k * high.0, k * high.1)
}

/// `1 + k·band-pass` with unit peak gain: a bell of `1 + k` at its centre.
fn bell(hz: f64, centre: f64, q: f64, k: f64) -> Complex {
    let w = warped(hz, centre);
    let band = over((0.0, w / q), (1.0 - w * w, w / q));
    (1.0 + k * band.0, k * band.1)
}

/// `1 − r·(1 − high-pass)`: flat at 0, the second-order high-pass at 1.
fn rumble(hz: f64, r: f64) -> Complex {
    let w = warped(hz, RUMBLE_HZ);
    let denominator = (1.0 - w * w, w / RUMBLE_Q);
    let removed = over((1.0, w / RUMBLE_Q), denominator);
    (1.0 - r * removed.0, -r * removed.1)
}

/// Third-octave centres from 80 Hz to 12.5 kHz with an approximate long-term
/// average speech level in each (dB, arbitrary reference). Used only to weigh
/// the loudness trim; the dsp tests check the result on speech signals.
const SPEECH: [(f64, f64); 23] = [
    (80.0, 43.5),
    (100.0, 54.4),
    (125.0, 57.7),
    (160.0, 56.8),
    (200.0, 60.2),
    (250.0, 60.3),
    (315.0, 59.0),
    (400.0, 62.1),
    (500.0, 62.1),
    (630.0, 60.5),
    (800.0, 56.8),
    (1_000.0, 53.7),
    (1_250.0, 53.0),
    (1_600.0, 52.0),
    (2_000.0, 48.7),
    (2_500.0, 48.1),
    (3_150.0, 46.8),
    (4_000.0, 45.6),
    (5_000.0, 44.5),
    (6_300.0, 44.3),
    (8_000.0, 43.7),
    (10_000.0, 43.4),
    (12_500.0, 41.3),
];

/// Approximate loudness weighting (the K curve of ITU-R BS.1770: a 4 dB
/// shelf above about 1.5 kHz and a gentle low cut), as a power ratio.
fn loudness_weight(hz: f64) -> f64 {
    let shelf_db = 4.0 / (1.0 + (1_500.0 / hz).powi(2));
    10f64.powf(shelf_db / 10.0) / (1.0 + (38.0 / hz).powi(4))
}

impl VoiceContour {
    /// Flat and transparent.
    pub const NEUTRAL: Self =
        Self { rumble: 0.0, low: 0.0, body: 0.0, mud: 0.0, presence: 0.0, high: 0.0, air: 0.0, amount: 0.0, trim: 1.0 };

    /// The contour for a validated pad position.
    pub fn of(settings: &VoiceColorSettings) -> Self {
        let tone = settings.tone.clamp(-1.0, 1.0);
        let character = settings.ambience.clamp(0.0, 1.0);
        if tone == 0.0 && character == 0.0 {
            return Self::NEUTRAL;
        }
        let warm = (-tone).max(0.0);
        let bright = tone.max(0.0);
        let mut contour = Self {
            rumble: {
                let x = (character / RUMBLE_FULL_AT).min(1.0);
                x * x * (3.0 - 2.0 * x)
            },
            // Warm lifts the lows and eases the highs; Bright does the reverse.
            low: gain(TONE_BOOST_DB * warm + TONE_CUT_DB * bright),
            high: gain(TONE_BOOST_DB * bright + TONE_CUT_DB * warm),
            body: gain(BODY_DB * character),
            mud: gain(MUD_DB * character),
            presence: gain(PRESENCE_DB * character),
            air: gain(AIR_DB * character),
            amount: character,
            trim: 1.0,
        };
        // Return typical speech to its dry loudness, so the pad changes the
        // voice's colour and density rather than its volume.
        let (mut dry, mut shaped) = (0.0, 0.0);
        for &(hz, level) in &SPEECH {
            let power = 10f64.powf(level / 10.0) * loudness_weight(hz);
            let response = contour.response(hz);
            dry += power;
            shaped += power * response * response;
        }
        contour.trim = (dry / shaped).sqrt();
        contour
    }

    /// Linear magnitude of the static contour at `hz`, before the trim and
    /// with the sibilance guard open.
    pub fn response(&self, hz: f64) -> f64 {
        let mut h = rumble(hz, self.rumble);
        h = times(h, low_shelf(hz, LOW_SHELF_HZ, self.low));
        h = times(h, bell(hz, BODY_HZ, BODY_Q, self.body));
        h = times(h, bell(hz, MUD_HZ, MUD_Q, self.mud));
        h = times(h, bell(hz, PRESENCE_HZ, PRESENCE_Q, self.presence));
        h = times(h, high_shelf(hz, HIGH_SHELF_HZ, self.high));
        h = times(h, high_shelf(hz, AIR_HZ, self.air));
        (h.0 * h.0 + h.1 * h.1).sqrt()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(tone: f64, character: f64) -> VoiceContour {
        VoiceContour::of(&VoiceColorSettings { tone, ambience: character })
    }

    fn db(ratio: f64) -> f64 {
        20.0 * ratio.log10()
    }

    /// Prints the static contour in decibels at the corners of the pad. Run
    /// with `cargo test -p vinemix-model voice -- --ignored --nocapture`.
    #[test]
    #[ignore = "prints the contour table"]
    fn contour_table() {
        let positions = [("character 50%", 0.0, 0.5), ("character 100%", 0.0, 1.0), ("warm", -1.0, 0.0), ("bright", 1.0, 0.0), ("warm broadcast", -1.0, 1.0), ("bright broadcast", 1.0, 1.0)];
        let frequencies = [30.0, 50.0, 80.0, 120.0, 165.0, 250.0, 420.0, 700.0, 1_000.0, 2_000.0, 3_300.0, 5_000.0, 8_000.0, 12_000.0, 16_000.0];
        print!("{:<18}", "Hz");
        for hz in frequencies {
            print!("{hz:>7.0}");
        }
        println!("{:>8}", "trim");
        for (name, tone, character) in positions {
            let contour = at(tone, character);
            print!("{name:<18}");
            for hz in frequencies {
                print!("{:>7.1}", db(contour.response(hz)));
            }
            println!("{:>8.2}", db(contour.trim));
        }
    }

    #[test]
    fn neutral_is_exactly_flat() {
        let neutral = at(0.0, 0.0);
        assert_eq!(neutral, VoiceContour::NEUTRAL);
        for hz in [20.0, 100.0, 1_000.0, 10_000.0, 20_000.0] {
            assert_eq!(neutral.response(hz), 1.0);
        }
    }

    #[test]
    fn character_lifts_body_and_presence_and_cuts_rumble_and_mud() {
        let full = at(0.0, 1.0);
        let at_1k = db(full.response(1_000.0));
        assert!(db(full.response(30.0)) < -12.0, "rumble is removed: {}", db(full.response(30.0)));
        assert!(db(full.response(170.0)) - at_1k > 3.0, "body: {}", db(full.response(170.0)));
        assert!(db(full.response(420.0)) - at_1k < -2.0, "mud: {}", db(full.response(420.0)));
        assert!(db(full.response(3_300.0)) - at_1k > 4.0, "presence: {}", db(full.response(3_300.0)));
        assert!(db(full.response(12_000.0)) - at_1k > 3.0, "air: {}", db(full.response(12_000.0)));
        // No stage is extreme: the strongest point of the contour is modest.
        let top = (0..200).map(|i| db(full.response(20.0 * 1_000f64.powf(f64::from(i) / 199.0)))).fold(f64::MIN, f64::max);
        assert!(top < 12.0, "largest boost {top} dB");
    }

    #[test]
    fn tone_tilts_in_opposite_directions() {
        let (warm, bright) = (at(-1.0, 0.0), at(1.0, 0.0));
        let tilt = |c: &VoiceContour| db(c.response(8_000.0)) - db(c.response(100.0));
        assert!(tilt(&warm) < -8.0, "warm tilt {}", tilt(&warm));
        assert!(tilt(&bright) > 8.0, "bright tilt {}", tilt(&bright));
        // Tone alone never touches the dynamics or the rumble filter.
        assert_eq!((warm.amount, warm.rumble, bright.amount, bright.rumble), (0.0, 0.0, 0.0, 0.0));
    }

    #[test]
    fn every_position_is_finite_bounded_and_continuous() {
        let mut previous: Option<VoiceContour> = None;
        for step in 0..=200 {
            let character = f64::from(step) / 200.0;
            for tone in [-1.0, -0.5, 0.0, 0.5, 1.0] {
                let contour = at(tone, character);
                for value in [contour.rumble, contour.low, contour.body, contour.mud, contour.presence, contour.high, contour.air, contour.trim] {
                    assert!(value.is_finite());
                }
                assert!(contour.trim > 0.25 && contour.trim < 2.0, "trim {} at ({tone}, {character})", contour.trim);
                for k in [contour.low, contour.body, contour.mud, contour.presence, contour.high, contour.air] {
                    assert!(k > -1.0, "a gain of -1 or less would invert or null a band");
                }
            }
            let centre = at(0.0, character);
            if let Some(before) = previous {
                assert!((centre.trim - before.trim).abs() < 0.02, "trim jumps at character {character}");
                assert!((centre.presence - before.presence).abs() < 0.02);
            }
            previous = Some(centre);
        }
        // Arbitrarily close to neutral is arbitrarily close to flat.
        let near = at(1e-9, 1e-9);
        assert!((near.trim - 1.0).abs() < 1e-6 && near.presence.abs() < 1e-6);
    }
}
