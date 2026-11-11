//! Voice Color: a two-control voice treatment.
//!
//! An original VineMix design; it has no counterpart in the 0.5 C++ engine and
//! is not checked against it. In order, per frame:
//!
//! 1. A contour of fixed filters with moving gains (rumble high-pass, tone
//!    shelves, body, mud, presence and air). The boosts above the vowels are
//!    relaxed while a sibilant is sounding, so brightening lifts the vowels
//!    more than it sharpens "s".
//! 2. Dynamics measured against the speaker's own level, which is learned
//!    while they talk. Quiet syllables are lifted and loud ones held, the same
//!    way at any microphone gain. Pauses and room noise are not lifted.
//! 3. Low-order harmonics generated from the band below the consonants, which
//!    cannot alias, for density.
//! 4. An output trim, compiled with the plan, that returns typical speech to
//!    its dry loudness, and a ceiling that stops the node adding peaks above
//!    -1 dBFS (or above the input's own peak, if that is higher).
//!
//! Nothing is delayed: there is no lookahead and no oversampling. At the
//! neutral pad position every stage contributes exactly zero and the output
//! equals the input. All state is plain data in this struct; `process` and
//! `configure` are bounded and never allocate.
//!
//! The filters are the trapezoidal state-variable and one-pole forms in
//! [`super::filters`].

use vinemix_model::voice::{self, VoiceContour};
use vinemix_rt::Frame;

use super::filters::{OnePole, Pair, Svf};
use crate::util::{denormal, frame, Smooth};

/// Frames between level, gain and guard updates (a third of a millisecond).
const CONTROL: u32 = 16;

/// One-pole coefficients, `exp(-1 / (seconds · rate))`; see `constants_match_their_times`.
const GUARD_ATTACK: f64 = 0.959_189_457_109_138_2; // 0.5 ms at 48 kHz
const GUARD_RELEASE: f64 = 0.998_612_075_170_908_3; // 15 ms
const ENVELOPE_ATTACK: f64 = 0.979_382_181_331_240_2; // 1 ms
const ENVELOPE_RELEASE: f64 = 0.999_167_013_792_458_3; // 25 ms
const INPUT_RELEASE: f64 = 0.999_791_688_366_548_6; // 100 ms
const LIMIT_RELEASE: f64 = 0.999_739_617_238_810_2; // 80 ms
const GAIN_ATTACK: f64 = 0.894_839_316_814_369_8; // 3 ms at the control rate
const GAIN_RELEASE: f64 = 0.996_303_146_547_149_4; // 90 ms at the control rate
const NOISE_FALL: f64 = 0.006_644_493_744_965_6; // 1 - exp(-1 / (50 ms · control rate))
const NOISE_RISE: f64 = 0.000_111_104_938_500_194_98; // 1 - exp(-1 / (3 s · control rate))

/// Sibilance guard: share of the input above [`SIBILANCE_HZ`] at which the
/// high boosts start to relax, and at which they are fully relaxed.
const SIBILANCE_HZ: f64 = 5_500.0;
const GUARD_FROM: f64 = 0.25;
const GUARD_TO: f64 = 0.55;
/// Extra weight of the air band in the dynamics detector, so a sibilant reads
/// as louder than it is and is lifted less than a vowel.
const EMPHASIS: f64 = 4.0;

/// The level tracker ignores anything below this (dBFS) or within
/// [`ABOVE_NOISE_DB`] of the tracked noise floor.
const GATE_DB: f64 = -60.0;
const ABOVE_NOISE_DB: f64 = 12.0;
const FLOOR_DB: f64 = -90.0;
/// Control ticks of speech until the dynamics are fully applied (0.25 s) and
/// until the level average reaches its long time constant (2.5 s).
const CONFIDENT: u32 = 750;
const LEARNED: u32 = 7_500;

/// The dynamics curve, in decibels relative to the speaker's level.
const RATIO: f64 = 3.5;
const THRESHOLD: f64 = -8.0;
const KNEE: f64 = 10.0;
/// The level that leaves the node at its input level: just above an
/// ordinary loud syllable, so the loudest words are held and the rest rise.
const LOUD: f64 = 6.0;
/// Quiet sounds are lifted fully down to here, and not at all below there.
const LIFT_FULL: f64 = -20.0;
const LIFT_NONE: f64 = -40.0;
/// Nothing within this many decibels of the noise floor is lifted; the lift
/// is complete this far above it.
const LIFT_CLEAR_FROM: f64 = 3.0;
const LIFT_CLEAR_TO: f64 = 12.0;
const MAXIMUM_OVER: f64 = 36.0;

/// Harmonics: the band they are made from, how far below the waveshaper's
/// limit the speaker's level sits, and the share of even harmonics (at most
/// 0.25, the largest value for which the shaper stays monotonic).
const DENSITY_HZ: f64 = 1_400.0;
const DENSITY_Q: f64 = 0.6;
const DRIVE_HEADROOM_DB: f64 = 6.0;
const EVEN: f64 = 0.25;
const DC_HZ: f64 = 20.0;

/// The node never adds a peak above this (-1 dBFS).
const CEILING: f64 = 0.891_250_938_133_745_6;

fn smoothstep(x: f64) -> f64 {
    let x = x.clamp(0.0, 1.0);
    x * x * (3.0 - 2.0 * x)
}

/// What the waveshaper adds to a normalised sample `u` in [-1, 1]: the odd
/// part of a cubic soft clipper and an even part that vanishes, with zero
/// slope, at both ends. `u + shaped(u)` is monotonic and stays within ±2/3 at
/// the limits, so it rounds peaks and never folds them over.
#[inline]
fn shaped(u: f64) -> f64 {
    let square = u * u;
    let rest = 1.0 - square;
    square * (EVEN * rest * rest - u / 3.0)
}

#[inline]
fn follow(envelope: f64, input: f64, attack: f64, release: f64) -> f64 {
    let coefficient = if input > envelope { attack } else { release };
    denormal(input + coefficient * (envelope - input))
}

/// Gain change in decibels for a sound `relative` dB from the speaker's
/// level and `above_noise` dB over the noise floor, at `depth` (0 to 1).
fn curve(relative: f64, above_noise: f64, depth: f64) -> f64 {
    // Depth scales the whole curve evenly, up to RATIO:1 at full depth.
    let slope = depth * (1.0 - 1.0 / RATIO);
    let over = (relative - THRESHOLD).min(MAXIMUM_OVER);
    let reduction = if over <= -KNEE / 2.0 {
        0.0
    } else if over >= KNEE / 2.0 {
        over
    } else {
        (over + KNEE / 2.0) * (over + KNEE / 2.0) / (2.0 * KNEE)
    };
    // Lift what is clearly speech: not the tail of the room, and nothing
    // within reach of the noise floor.
    let speech = smoothstep((relative - LIFT_NONE) / (LIFT_FULL - LIFT_NONE));
    let clear = smoothstep((above_noise - LIFT_CLEAR_FROM) / (LIFT_CLEAR_TO - LIFT_CLEAR_FROM));
    slope * ((LOUD - THRESHOLD) * speech.min(clear) - reduction)
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct VoiceColor {
    rumble_filter: Svf<2>,
    low_filter: OnePole,
    body_filter: Svf<2>,
    mud_filter: Svf<2>,
    presence_filter: Svf<2>,
    high_filter: OnePole,
    air_filter: OnePole,
    density_filter: Svf<2>,
    dc_filter: OnePole,
    sibilance_filter: Svf<1>,
    rumble: Smooth,
    low: Smooth,
    body: Smooth,
    mud: Smooth,
    presence: Smooth,
    high: Smooth,
    air: Smooth,
    amount: Smooth,
    trim: Smooth,
    hiss_envelope: f64,
    wide_envelope: f64,
    /// 1 leaves the high boosts in place; 0 removes them.
    guard: f64,
    guard_target: f64,
    guard_step: f64,
    envelope: f64,
    /// The speaker's level: the mean power of the detector while they talk,
    /// and the same in dBFS. A power mean follows the loud syllables and is
    /// barely moved by how much of each word's tail is counted.
    level_power: f64,
    level_db: f64,
    /// The noise floor, in dBFS of the detector.
    noise_db: f64,
    tracking: bool,
    ticks: u32,
    gain_db: f64,
    gain: f64,
    gain_target: f64,
    gain_step: f64,
    /// Share of the dynamics and harmonics in effect (amount × confidence).
    depth: f64,
    /// Linear level the waveshaper is normalised to, and its reciprocal.
    reference: f64,
    inverse: f64,
    input_envelope: f64,
    limit: f64,
    countdown: u32,
}

impl Default for VoiceColor {
    fn default() -> Self {
        Self {
            rumble_filter: Svf::new(voice::RUMBLE_HZ, voice::RUMBLE_Q),
            low_filter: OnePole::new(voice::LOW_SHELF_HZ),
            body_filter: Svf::new(voice::BODY_HZ, voice::BODY_Q),
            mud_filter: Svf::new(voice::MUD_HZ, voice::MUD_Q),
            presence_filter: Svf::new(voice::PRESENCE_HZ, voice::PRESENCE_Q),
            high_filter: OnePole::new(voice::HIGH_SHELF_HZ),
            air_filter: OnePole::new(voice::AIR_HZ),
            density_filter: Svf::new(DENSITY_HZ, DENSITY_Q),
            dc_filter: OnePole::new(DC_HZ),
            sibilance_filter: Svf::new(SIBILANCE_HZ, std::f64::consts::FRAC_1_SQRT_2),
            rumble: Smooth::default(),
            low: Smooth::default(),
            body: Smooth::default(),
            mud: Smooth::default(),
            presence: Smooth::default(),
            high: Smooth::default(),
            air: Smooth::default(),
            amount: Smooth::default(),
            trim: Smooth::default(),
            hiss_envelope: 0.0,
            wide_envelope: 0.0,
            guard: 1.0,
            guard_target: 1.0,
            guard_step: 0.0,
            envelope: 0.0,
            level_power: 0.0,
            level_db: FLOOR_DB,
            noise_db: FLOOR_DB,
            tracking: false,
            ticks: 0,
            gain_db: 0.0,
            gain: 1.0,
            gain_target: 1.0,
            gain_step: 0.0,
            depth: 0.0,
            reference: 1.0,
            inverse: 1.0,
            input_envelope: 0.0,
            limit: 1.0,
            countdown: 0,
        }
    }
}

impl VoiceColor {
    /// Retargets the contour. Filter state, the learned level and the gain
    /// carry on; each amount glides to its new value over 10 ms.
    pub fn configure(&mut self, contour: &VoiceContour) {
        self.rumble.set(contour.rumble);
        self.low.set(contour.low);
        self.body.set(contour.body);
        self.mud.set(contour.mud);
        self.presence.set(contour.presence);
        self.high.set(contour.high);
        self.air.set(contour.air);
        self.amount.set(contour.amount);
        self.trim.set(contour.trim);
    }

    /// Level, gain and guard decisions, every [`CONTROL`] frames.
    fn control(&mut self, amount: f64) {
        // Land exactly on the previous targets, so a settled node holds exact
        // values (unity, at neutral) rather than an accumulated approximation.
        self.guard = self.guard_target;
        self.gain = self.gain_target;
        let steps = f64::from(CONTROL);

        let share = self.hiss_envelope / (self.wide_envelope + 1e-9);
        self.guard_target = 1.0 - smoothstep((share - GUARD_FROM) / (GUARD_TO - GUARD_FROM));
        self.guard_step = (self.guard_target - self.guard) / steps;

        let level = (20.0 * self.envelope.max(1e-10).log10()).max(FLOOR_DB);
        if !self.tracking {
            // Wait for the first sound, so the floor starts from what the
            // input really is rather than from an arbitrary constant. Until
            // then the gain rests at unity.
            if level <= FLOOR_DB {
                return;
            }
            self.tracking = true;
            self.noise_db = level;
        }
        // The floor falls to any quieter moment quickly and creeps up slowly,
        // so it settles on the room between words.
        let rate = if level < self.noise_db { NOISE_FALL } else { NOISE_RISE };
        self.noise_db += (level - self.noise_db) * rate;
        if level > GATE_DB && level > self.noise_db + ABOVE_NOISE_DB {
            // A running mean while learning, then a 2.5 s average.
            self.ticks = (self.ticks + 1).min(LEARNED);
            self.level_power += (self.envelope * self.envelope - self.level_power) / f64::from(self.ticks);
            self.level_db = 10.0 * self.level_power.max(1e-20).log10();
            self.reference = 10f64.powf((self.level_db + DRIVE_HEADROOM_DB) / 20.0);
            self.inverse = 1.0 / self.reference;
        }
        self.depth = amount * (f64::from(self.ticks) / f64::from(CONFIDENT)).min(1.0);

        let target = if self.depth > 0.0 { curve(level - self.level_db, level - self.noise_db, self.depth) } else { 0.0 };
        let coefficient = if target < self.gain_db { GAIN_ATTACK } else { GAIN_RELEASE };
        self.gain_db = target + coefficient * (self.gain_db - target);
        if target == 0.0 && self.gain_db.abs() < 1e-6 {
            self.gain_db = 0.0;
        }
        self.gain_target = 10f64.powf(self.gain_db / 20.0);
        self.gain_step = (self.gain_target - self.gain) / steps;
    }

    pub fn process(&mut self, input: Frame) -> Frame {
        let x: Pair = [f64::from(input.left), f64::from(input.right)];
        let rumble = self.rumble.next();
        let low_gain = self.low.next();
        let body = self.body.next();
        let mud = self.mud.next();
        let presence = self.presence.next();
        let high = self.high.next();
        let air = self.air.next();
        let amount = self.amount.next();
        let trim = self.trim.next();

        // The guard listens to the input, so the boosts it relaxes cannot
        // feed back into its own decision.
        let mid = 0.5 * (x[0] + x[1]);
        let ([band], [low]) = self.sibilance_filter.step([mid]);
        let hiss = (mid - self.sibilance_filter.k * band - low).abs();
        self.hiss_envelope = follow(self.hiss_envelope, hiss, GUARD_ATTACK, GUARD_RELEASE);
        self.wide_envelope = follow(self.wide_envelope, mid.abs(), GUARD_ATTACK, GUARD_RELEASE);

        if self.countdown == 0 {
            self.control(amount);
            self.countdown = CONTROL;
        }
        self.countdown -= 1;
        self.guard += self.guard_step;
        self.gain += self.gain_step;
        let open = self.guard;
        let guarded = |k: f64| if k > 0.0 { k * open } else { k };

        // Contour: each stage adds a share of a fixed filter's output.
        let mut s = x;
        let (band, low) = self.rumble_filter.step(s);
        for c in 0..2 {
            s[c] -= rumble * (low[c] + self.rumble_filter.k * band[c]);
        }
        let low = self.low_filter.low(s);
        for c in 0..2 {
            s[c] += low_gain * low[c];
        }
        let (band, _) = self.body_filter.step(s);
        for c in 0..2 {
            s[c] += body * self.body_filter.k * band[c];
        }
        let (band, _) = self.mud_filter.step(s);
        for c in 0..2 {
            s[c] += mud * self.mud_filter.k * band[c];
        }
        let (band, _) = self.presence_filter.step(s);
        let presence = guarded(presence);
        for c in 0..2 {
            s[c] += presence * self.presence_filter.k * band[c];
        }
        let low = self.high_filter.low(s);
        let high = guarded(high);
        for c in 0..2 {
            s[c] += high * (s[c] - low[c]);
        }
        let low = self.air_filter.low(s);
        let top = [s[0] - low[0], s[1] - low[1]];
        let air = guarded(air);
        for c in 0..2 {
            s[c] += air * top[c];
        }

        // Linked detector, with the air band weighted up.
        let side = (s[0] + EMPHASIS * top[0]).abs().max((s[1] + EMPHASIS * top[1]).abs());
        self.envelope = follow(self.envelope, side, ENVELOPE_ATTACK, ENVELOPE_RELEASE);
        for value in &mut s {
            *value *= self.gain;
        }

        // Harmonics from the low band only: a low-order polynomial of a
        // signal limited to about 1.4 kHz stays far below the Nyquist
        // frequency.
        let (_, low) = self.density_filter.step(s);
        let mut harmonics = [0.0; 2];
        for c in 0..2 {
            harmonics[c] = shaped((low[c] * self.inverse).clamp(-1.0, 1.0));
        }
        let offset = self.dc_filter.low(harmonics);
        let scale = self.depth * self.reference;
        for c in 0..2 {
            s[c] = (s[c] + scale * (harmonics[c] - offset[c])) * trim;
        }

        // Never add a peak above the ceiling, or above the input's own peak.
        self.input_envelope = denormal(x[0].abs().max(x[1].abs()).max(self.input_envelope * INPUT_RELEASE));
        let allowed = CEILING.max(self.input_envelope);
        let peak = s[0].abs().max(s[1].abs());
        if peak * self.limit > allowed {
            self.limit = allowed / peak;
        } else {
            self.limit = 1.0 - (1.0 - self.limit) * LIMIT_RELEASE;
        }
        frame(s[0] * self.limit, s[1] * self.limit)
    }

    pub fn reset(&mut self) {
        *self = Self::default();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn constants_match_their_times() {
        let per_frame = |seconds: f64| (-1.0 / (seconds * 48_000.0)).exp();
        const CONTROL_RATE: f64 = 48_000.0 / CONTROL as f64;
        let per_tick = |seconds: f64| (-1.0 / (seconds * CONTROL_RATE)).exp();
        let close = |a: f64, b: f64| assert!((a - b).abs() < 1e-12, "{a} vs {b}");
        close(GUARD_ATTACK, per_frame(0.000_5));
        close(GUARD_RELEASE, per_frame(0.015));
        close(ENVELOPE_ATTACK, per_frame(0.001));
        close(ENVELOPE_RELEASE, per_frame(0.025));
        close(INPUT_RELEASE, per_frame(0.1));
        close(LIMIT_RELEASE, per_frame(0.08));
        close(GAIN_ATTACK, per_tick(0.003));
        close(GAIN_RELEASE, per_tick(0.09));
        close(NOISE_FALL, 1.0 - per_tick(0.05));
        close(NOISE_RISE, 1.0 - per_tick(3.0));
        close(CEILING, 10f64.powf(-1.0 / 20.0));
        // The level average's long time constant is LEARNED control ticks.
        close(f64::from(LEARNED) / CONTROL_RATE, 2.5);
        close(f64::from(CONFIDENT) / CONTROL_RATE, 0.25);
    }

    #[test]
    fn curve_lifts_quiet_speech_and_holds_loud_speech() {
        let clear = 60.0;
        assert_eq!(curve(0.0, clear, 0.0), 0.0, "no depth, no change");
        assert!(curve(LOUD, clear, 1.0).abs() < 1e-9, "the loudest ordinary syllable leaves at its input level");
        let quiet = curve(-18.0, clear, 1.0);
        assert!(quiet > 9.0 && quiet < 11.0, "a quiet syllable is lifted: {quiet}");
        assert!((curve(-18.0, clear, 0.5) - quiet / 2.0).abs() < 1e-9, "half the depth is half the change");
        assert!(curve(18.0, clear, 1.0) < -7.0, "a shout is held back");
        assert_eq!(curve(-45.0, clear, 1.0), 0.0, "the room's tail is left alone");
        assert_eq!(curve(-18.0, 3.0, 1.0), 0.0, "nothing at the noise floor is lifted");
        assert_eq!(curve(-18.0, 12.0, 1.0), quiet, "speech clear of the noise is lifted in full");
        // Above the lift region the gain only falls, without steps, and
        // never by more than the input rose: a louder input is never quieter
        // at the output.
        let mut previous = curve(-20.0, clear, 1.0);
        let mut x = -20.0;
        while x < 40.0 {
            x += 0.05;
            let now = curve(x, clear, 1.0);
            assert!(now <= previous + 1e-12, "gain rises at {x}: {previous} then {now}");
            assert!(previous - now < 0.05, "output level falls at {x}: {previous} then {now}");
            previous = now;
        }
        for step in 0..=100 {
            let depth = f64::from(step) / 100.0;
            assert!(curve(-18.0, clear, depth) >= 0.0 && curve(-18.0, clear, depth) <= quiet + 1e-12);
        }
    }

    #[test]
    fn waveshaper_is_monotonic_bounded_and_silent_at_rest() {
        assert_eq!(shaped(0.0), 0.0);
        let mut previous = -1.0 + shaped(-1.0);
        assert!((previous + 2.0 / 3.0).abs() < 1e-12);
        for step in 1..=2_000 {
            let u = -1.0 + f64::from(step) / 1_000.0;
            let now = u + shaped(u);
            assert!(now >= previous, "folds over at {u}");
            assert!(now.abs() <= 2.0 / 3.0 + 1e-12);
            previous = now;
        }
        assert!((previous - 2.0 / 3.0).abs() < 1e-12);
    }
}
