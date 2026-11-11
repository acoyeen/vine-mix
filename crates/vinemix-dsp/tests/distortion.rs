//! Distortion: what the node does, measured.
//!
//! The signals are synthetic and deterministic (steady tones, and the
//! speech-like signal of `support::speech`). These tests hold the behaviour
//! the design promises. Whether it sounds good is a listening judgement they
//! cannot make.
//!
//! The ignored tests are tools, not checks. `distortion_report` prints the
//! measurements the design notes quote; `distortion_render` writes listening
//! files for WAV files you supply (use `scripts/render-distortion.ps1`);
//! `distortion_cost` prints the processing cost. Run them with
//! `cargo test -p vinemix-dsp --release --test distortion -- --ignored --nocapture`.

mod support;

use std::path::PathBuf;
use std::sync::OnceLock;

use support::*;
use vinemix_model::config::GraphNode;
use vinemix_rt::Frame;

fn set(drive: f64, tone: f64, mix: f64, output: f64) -> impl FnOnce(&mut GraphNode) {
    move |node| {
        node.distortion.drive_db = drive;
        node.distortion.tone_hz = tone;
        node.distortion.mix = mix;
        node.distortion.output_db = output;
    }
}

fn driven(input: &[f64], drive: f64, tone: f64, mix: f64) -> Vec<f64> {
    left(&render("distortion", set(drive, tone, mix, 0.0), &mono(input)))
}

/// Harmonics of a steady tone and everything that is not one of them.
struct Spectrum {
    fundamental: f64,
    /// Second to fifth harmonic, dB relative to the fundamental.
    harmonics: [f64; 4],
    /// All harmonics above the fundamental together (total harmonic distortion).
    thd: f64,
    /// Everything that is neither the tone nor a harmonic nor DC: aliasing.
    stray: f64,
    dc: f64,
}

/// `settled` must hold a whole number of seconds and `frequency` a whole
/// number of hertz, so every harmonic and every folded product is exactly
/// orthogonal to the others over the window.
fn spectrum(settled: &[f64], frequency: f64) -> Spectrum {
    let fundamental = tone_level(settled, frequency);
    let mean = settled.iter().sum::<f64>() / settled.len() as f64;
    let total = settled.iter().map(|s| s * s).sum::<f64>() / settled.len() as f64 - mean * mean;
    let mut harmonic_power = 0.0;
    let mut harmonics = [-200.0; 4];
    let mut k = 2.0;
    while k * frequency < RATE / 2.0 {
        let level = tone_level(settled, k * frequency);
        harmonic_power += level * level / 2.0;
        if k <= 5.0 {
            harmonics[k as usize - 2] = db(level / fundamental);
        }
        k += 1.0;
    }
    let tone_power = fundamental * fundamental / 2.0;
    let stray = (total - tone_power - harmonic_power).max(0.0);
    Spectrum {
        fundamental,
        harmonics,
        thd: 10.0 * (harmonic_power / tone_power).max(1e-20).log10(),
        stray: 10.0 * (stray / tone_power).max(1e-20).log10(),
        dc: db(mean.abs() / fundamental),
    }
}

const DRIVES: [f64; 6] = [0.0, 6.0, 12.0, 18.0, 24.0, 36.0];

/// Six seconds of a low and of a high synthetic voice at -24 dBFS.
fn low_voice() -> &'static [f64] {
    static VOICE: OnceLock<Vec<f64>> = OnceLock::new();
    VOICE.get_or_init(|| speech(6.0, 110.0, 3))
}

fn high_voice() -> &'static [f64] {
    static VOICE: OnceLock<Vec<f64>> = OnceLock::new();
    VOICE.get_or_init(|| speech(6.0, 210.0, 5))
}

/// Steady noise `below` dB under the active level of `voice`.
fn room(voice: &[f64], below: f64, frames: usize, seed: u32) -> Vec<f64> {
    let mut noise = Noise::new(seed);
    // Uniform noise in [-1, 1) has an RMS of 1/sqrt(3).
    let level = active_rms(voice) * from_db(-below) * 3f64.sqrt();
    (0..frames).map(|_| noise.next() * level).collect()
}

/// A settled tone through the node: two seconds to learn, one to measure.
fn settled_tone(frequency: f64, level_db: f64, drive: f64) -> Spectrum {
    let output = driven(&sine(frequency, from_db(level_db), 3 * 48_000), drive, 18_000.0, 1.0);
    spectrum(&output[2 * 48_000..], frequency)
}

#[test]
fn mix_zero_is_exactly_the_input() {
    let input = mono(low_voice());
    for drive in [0.0, 12.0, 36.0] {
        let output = render("distortion", set(drive, 6_000.0, 0.0, 0.0), &input);
        assert!(output == input, "drive {drive} changed the sound at Mix 0%");
    }
}

#[test]
fn bypass_passes_the_input_untouched() {
    let input = mono(low_voice());
    let output = render(
        "distortion",
        |node| {
            set(36.0, 2_000.0, 1.0, -12.0)(node);
            node.bypass = true;
        },
        &input,
    );
    assert!(output == input, "a bypassed node changed or delayed the sound");
}

#[test]
fn the_node_adds_no_delay() {
    for (drive, mix) in [(12.0, 1.0), (36.0, 1.0), (12.0, 0.5)] {
        let mut input = vec![0.0; 4_800];
        input[1_000] = 0.25;
        let output = driven(&input, drive, 18_000.0, mix);
        assert!(output[..1_000].iter().all(|&s| s == 0.0), "drive {drive}, mix {mix} sounded before its input");
        let loudest = output.iter().enumerate().fold((0, 0.0f64), |best, (n, s)| if s.abs() > best.1 { (n, s.abs()) } else { best }).0;
        assert_eq!(loudest, 1_000, "drive {drive}, mix {mix} moved the impulse");
    }
    // Speech in the middle of a phrase lines up with itself too: the driven
    // sound is the input plus what driving changed, not a delayed copy.
    let voice = low_voice();
    let output = driven(voice, 6.0, 18_000.0, 1.0);
    let correlation = |lag: usize| voice[48_000..96_000].iter().zip(&output[48_000 + lag..]).map(|(a, b)| a * b).sum::<f64>();
    let best = (0..8).max_by(|&a, &b| correlation(a).total_cmp(&correlation(b))).unwrap();
    assert_eq!(best, 0, "the driven voice lags its input by {best} frames");
}

/// Drive is not a volume control: speech stays at about its dry loudness.
#[test]
fn drive_changes_the_character_not_the_loudness() {
    for (name, voice) in [("low", low_voice()), ("high", high_voice())] {
        let dry = loudness(voice);
        for drive in DRIVES {
            let change = loudness(&driven(voice, drive, 6_000.0, 1.0)) - dry;
            assert!(change.abs() < 1.5, "{name} voice at drive {drive}: {change:+.2} LU from dry");
        }
    }
}

/// Drive follows the level of the sound, so the same setting does the same
/// thing to a quiet source and to a loud one.
#[test]
fn a_quieter_source_gets_the_same_treatment() {
    let reference = settled_tone(997.0, -12.0, 12.0);
    for level in [-6.0, -24.0, -36.0, -48.0] {
        let s = settled_tone(997.0, level, 12.0);
        assert!((s.thd - reference.thd).abs() < 0.5, "a tone at {level} dBFS has {:.1} dB of harmonics, not {:.1}", s.thd, reference.thd);
        let gain = db(s.fundamental) - level;
        assert!(gain.abs() < 1.5, "a tone at {level} dBFS left {gain:+.2} dB from where it came in");
    }
    let voice = low_voice();
    let quiet: Vec<f64> = voice.iter().map(|s| s * 0.1).collect();
    for drive in [12.0, 36.0] {
        let loud = loudness(&driven(voice, drive, 6_000.0, 1.0));
        let soft = loudness(&driven(&quiet, drive, 6_000.0, 1.0));
        assert!((loud - soft - 20.0).abs() < 1.5, "drive {drive}: 20 dB less in became {:.2} dB less out", loud - soft);
    }
}

#[test]
fn more_drive_is_more_distortion() {
    let mut previous = -200.0;
    for drive in DRIVES {
        let s = settled_tone(997.0, -12.0, drive);
        assert!(s.thd > previous, "drive {drive} has {:.1} dB of harmonics, after {previous:.1}", s.thd);
        previous = s.thd;
    }
    assert!(settled_tone(997.0, -12.0, 0.0).thd < -30.0, "Drive 0 dB is nearly clean");
    assert!(previous > -14.0, "Drive 36 dB is strong: {previous:.1} dB");
}

/// The curve treats the two half-waves differently, so gentle settings add
/// mostly second harmonic.
#[test]
fn gentle_drive_is_mostly_second_harmonic() {
    for drive in [0.0, 6.0, 12.0] {
        let s = settled_tone(220.0, -12.0, drive);
        assert!(s.harmonics[0] > s.harmonics[1] + 8.0, "drive {drive}: second {:.1}, third {:.1} dBc", s.harmonics[0], s.harmonics[1]);
        assert!(s.harmonics[0] > -45.0 && s.harmonics[0] < -18.0, "drive {drive}: second harmonic {:.1} dBc", s.harmonics[0]);
        assert!(s.dc < -60.0, "drive {drive} leaves a steady offset of {:.1} dB", s.dc);
    }
}

/// What is neither the tone nor one of its harmonics is aliasing. It is far
/// below the harmonics across the range of a voice; the last rows are the
/// measured limit of the method for high, pure tones at extreme Drive.
#[test]
fn aliasing_stays_far_below_the_harmonics() {
    for (frequency, drive, limit) in [
        (220.0, 12.0, -90.0),
        (220.0, 24.0, -75.0),
        (220.0, 36.0, -60.0),
        (997.0, 12.0, -90.0),
        (997.0, 24.0, -75.0),
        (997.0, 36.0, -50.0),
        (2_999.0, 12.0, -90.0),
        (2_999.0, 18.0, -80.0),
        (2_999.0, 24.0, -55.0),
        (5_003.0, 12.0, -80.0),
        (5_003.0, 18.0, -55.0),
        (2_999.0, 36.0, -30.0),
        (5_003.0, 36.0, -28.0),
    ] {
        let s = settled_tone(frequency, -12.0, drive);
        assert!(s.stray < limit, "{frequency} Hz at drive {drive}: {:.1} dB of aliasing, limit {limit}", s.stray);
    }
}

/// Sound far below the level of the voice is left as it is, at any Drive.
#[test]
fn room_noise_between_words_is_not_raised() {
    let voice = low_voice();
    for below in [45.0, 60.0] {
        let mut input = voice.to_vec();
        input.extend(room(voice, below, 96_000, 9));
        for drive in [12.0, 36.0] {
            let output = driven(&input, drive, 18_000.0, 1.0);
            let n = input.len();
            // From 100 ms after the last word to the end of the pause.
            let change = db(rms(&output[n - 91_200..]) / rms(&input[n - 91_200..]));
            assert!(change < 0.5 && change > -3.0, "noise {below} dB below the voice changed by {change:+.2} dB at drive {drive}");
        }
    }
    // Nor before anything has been heard.
    let waiting = room(voice, 60.0, 96_000, 4);
    let output = driven(&waiting, 36.0, 18_000.0, 1.0);
    let change = db(rms(&output[48_000..]) / rms(&waiting[48_000..]));
    assert!(change < 0.5, "noise before the first sound changed by {change:+.2} dB");
}

/// The node waits in a quiet room, then the speaker starts: the first words
/// come out as they would mid-conversation.
#[test]
fn speech_after_a_quiet_wait_starts_cleanly() {
    let voice = low_voice();
    for drive in [12.0, 36.0] {
        let mut input = room(voice, 35.0, 144_000, 9);
        input.extend_from_slice(voice);
        let output = driven(&input, drive, 18_000.0, 1.0);
        let settled = driven(&[voice, voice].concat(), drive, 18_000.0, 1.0);
        let (after, reference) = (&output[144_000..], &settled[voice.len()..]);
        for (from, to, limit) in [(0, 2_400, 6.0), (2_400, 7_200, 3.0), (7_200, 48_000, 2.0)] {
            let difference = db(rms(&after[from..to]) / rms(&reference[from..to]));
            assert!(difference.abs() < limit, "drive {drive}, frames {from}-{to}: {difference:+.2} dB from a settled start");
        }
    }
}

/// A source that drops 20 dB for good is followed down, and one that comes
/// back up is followed at once.
#[test]
fn the_node_learns_a_new_level() {
    let phrase = low_voice();
    let quiet: Vec<f64> = phrase.iter().map(|s| s * 0.1).collect();
    let mut moved = phrase.to_vec();
    let mut settled = Vec::new();
    for _ in 0..5 {
        moved.extend_from_slice(&quiet);
        settled.extend_from_slice(&quiet);
    }
    let after = driven(&moved, 24.0, 6_000.0, 1.0);
    let reference = driven(&settled, 24.0, 6_000.0, 1.0);
    let last = phrase.len();
    let difference = loudness(&after[after.len() - last..]) - loudness(&reference[reference.len() - last..]);
    assert!(difference.abs() < 1.5, "after the drop the voice is still {difference:+.2} LU from where it settles");

    let mut back = quiet.clone();
    back.extend_from_slice(phrase);
    let after = driven(&back, 24.0, 6_000.0, 1.0);
    let reference = driven(&[phrase, phrase].concat(), 24.0, 6_000.0, 1.0);
    let difference = loudness(&after[after.len() - last..]) - loudness(&reference[reference.len() - last..]);
    assert!(difference.abs() < 1.0, "after the rise the voice is {difference:+.2} LU from where it settles");
}

/// The node never makes a peak higher than -1 dBFS, or than the input's own
/// peak where that is higher.
#[test]
fn peaks_stay_below_the_ceiling_or_the_input() {
    let ceiling = from_db(-1.0);
    let voice = low_voice();
    let top = peak(voice);
    let hot: Vec<f64> = voice.iter().map(|s| s / top).collect();
    let square: Vec<f64> = sine(110.0, 1.0, 96_000).iter().map(|s| 0.5 * s.signum()).collect();
    for (name, input) in [("speech", voice.to_vec()), ("full-scale speech", hot), ("a full-scale tone", sine(100.0, 1.0, 96_000)), ("a square wave", square)] {
        for drive in DRIVES {
            for mix in [0.5, 1.0] {
                let output = driven(&input, drive, 18_000.0, mix);
                let allowed = ceiling.max(peak(&input)) * 1.000_001;
                assert!(peak(&output) <= allowed, "{name} at drive {drive}, mix {mix}: peak {:.2} dBFS", db(peak(&output)));
            }
        }
    }
    // For ordinary speech the driven sound's peaks are below the input's.
    for drive in [12.0, 24.0, 36.0] {
        assert!(peak(&driven(voice, drive, 18_000.0, 1.0)) < peak(voice), "drive {drive} raised the peaks of speech");
    }
}

#[test]
fn channels_stay_linked() {
    // A mono source stays exactly mono.
    for drive in DRIVES {
        let output = render("distortion", set(drive, 6_000.0, 1.0, 0.0), &mono(low_voice()));
        assert!(output.iter().all(|f| f.left == f.right), "drive {drive} split a mono voice");
    }
    // A silent channel stays silent, and a gentle setting keeps an off-centre
    // source where it is. (Strong saturation flattens both channels, which
    // narrows a level difference between them; that is what saturation does.)
    let one_sided: Vec<Frame> = low_voice().iter().map(|&s| Frame::new(s as f32, 0.0)).collect();
    let output = render("distortion", set(36.0, 6_000.0, 1.0, 0.0), &one_sided);
    assert!(output.iter().all(|f| f.right == 0.0));
    let offset: Vec<Frame> = low_voice().iter().map(|&s| Frame::new(s as f32, (s * 0.5) as f32)).collect();
    let output = render("distortion", set(6.0, 6_000.0, 1.0, 0.0), &offset);
    let balance = db(rms(&right(&output)) / rms(&left(&output)));
    assert!((balance + 6.02).abs() < 1.0, "a 6 dB offset became {balance:.2} dB");
}

#[test]
fn silence_stays_silent_and_tails_end() {
    for drive in DRIVES {
        let silent = render("distortion", set(drive, 6_000.0, 1.0, 0.0), &vec![Frame::SILENT; 24_000]);
        assert!(silent.iter().all(|f| f.left == 0.0 && f.right == 0.0), "drive {drive} made sound from silence");
    }
    let mut input = low_voice().to_vec();
    let end = input.len();
    input.extend(std::iter::repeat_n(0.0, 144_000));
    let output = driven(&input, 36.0, 500.0, 1.0);
    assert!(output[end + 96_000..].iter().all(|&s| s == 0.0), "the node rings on after its input stops");
}

/// Tone is a first-order low-pass on the driven sound.
#[test]
fn tone_darkens_the_driven_sound() {
    for (tone, frequency, expected) in [(1_000.0, 1_000.0, -3.01), (1_000.0, 4_000.0, -12.5), (6_000.0, 6_000.0, -3.01), (6_000.0, 250.0, -0.01)] {
        let output = left(&render("distortion", set(0.0, tone, 1.0, 0.0), &mono(&sine(frequency, 0.05, 3 * 48_000))));
        let gain = db(tone_level(&output[2 * 48_000..], frequency) / 0.05);
        assert!((gain - expected).abs() < 0.6, "tone {tone} Hz at {frequency} Hz: {gain:.2} dB, expected {expected}");
    }
}

/// Output trims the whole blend, at any Mix.
#[test]
fn output_trims_the_whole_blend() {
    let input = mono(low_voice());
    for mix in [0.0, 0.5, 1.0] {
        let full = render("distortion", set(18.0, 6_000.0, mix, 0.0), &input);
        let trimmed = render("distortion", set(18.0, 6_000.0, mix, -12.0), &input);
        let gain = from_db(-12.0);
        let worst = full.iter().zip(&trimmed).map(|(a, b)| (f64::from(a.left) * gain - f64::from(b.left)).abs()).fold(0.0, f64::max);
        assert!(worst < 1e-6, "mix {mix}: Output -12 dB is off by {worst:e}");
    }
}

/// Drive is typed in or reset rather than dragged: the whole range in one
/// step. The makeup glides with it, so the change does not arrive as a burst.
#[test]
fn a_large_change_of_drive_arrives_without_a_burst() {
    let input = mono(&sine(220.0, 0.25, 4 * 48_000));
    for (from, to) in [(0.0, 36.0), (36.0, 0.0), (6.0, 30.0)] {
        let mut rig = Rig::new(&single_effect("distortion", set(from, 18_000.0, 1.0, 0.0)));
        let before = left(&rig.run(&input[..2 * 48_000]));
        rig.edit(&single_effect("distortion", set(to, 18_000.0, 1.0, 0.0)));
        let after = left(&rig.run(&input[2 * 48_000..]));
        // Whole periods of the tone: 5 ms windows through the 10 ms glide
        // and after it, against the settled level on either side.
        let settled = rms(&before[48_000..]).max(rms(&after[48_000..]));
        for (index, window) in after[..2_400].chunks(240).enumerate() {
            let over = db(rms(window) / settled);
            assert!(over < 5.0, "drive {from} to {to}: {over:+.2} dB over the settled level {} ms after the change", index * 5);
        }
    }
}

/// The dials are dragged while audio runs: every quantum lands on new settings.
#[test]
fn control_moves_are_smooth() {
    let mut noise = Noise::new(5);
    let mut rig = Rig::new(&single_effect("distortion", set(0.0, 6_000.0, 0.0, 0.0)));
    let input = mono(&sine(200.0, 0.1, 96_000));
    let mut output = Vec::new();
    for block in input.chunks(240) {
        let (drive, tone, mix, trim) = (noise.next().abs() * 36.0, 500.0 + noise.next().abs() * 17_500.0, noise.next().abs(), -6.0 * noise.next().abs());
        rig.edit(&single_effect("distortion", set(drive, tone, mix, trim)));
        output.extend(rig.run(block));
    }
    let samples = left(&output);
    assert!(samples.iter().all(|s| s.is_finite()));
    // A 200 Hz tone at 0.1 moves 0.0026 per frame, and a fully squared one
    // crosses zero in a few frames at about 0.03 each. A click from a control
    // jump would be several times that.
    let step = samples.windows(2).map(|w| (w[1] - w[0]).abs()).fold(0.0, f64::max);
    assert!(step < 0.06, "largest step between frames {step:.4}");
}

/// Prints what the node does to tones and to synthetic speech.
#[test]
#[ignore = "measurement tool; prints tables"]
fn distortion_report() {
    println!("delay");
    let mut impulse = vec![0.0; 4_800];
    impulse[1_000] = 0.25;
    for mix in [1.0, 0.5, 0.0] {
        let output = driven(&impulse, 12.0, 18_000.0, mix);
        let first = output.iter().position(|&s| s != 0.0).unwrap_or(0);
        let loudest = output.iter().enumerate().fold((0, 0.0f64), |best, (n, s)| if s.abs() > best.1 { (n, s.abs()) } else { best }).0;
        println!("  mix {mix:.1}: first sound at frame {first}, loudest at {loudest} (input at 1000)");
    }

    println!("\nsteady tone at -12 dBFS, tone 18 kHz, mix 100%: level, harmonics, aliasing");
    println!("{:>8}{:>8}{:>9}{:>8}{:>8}{:>8}{:>8}{:>8}{:>9}{:>8}", "Hz", "drive", "out dB", "H2", "H3", "H4", "H5", "THD", "stray", "DC");
    for frequency in [220.0, 997.0, 2_999.0, 5_003.0] {
        for drive in DRIVES {
            let output = driven(&sine(frequency, 0.25, 6 * 48_000), drive, 18_000.0, 1.0);
            let s = spectrum(&output[4 * 48_000..], frequency);
            println!(
                "{frequency:>8.0}{drive:>8.0}{:>9.2}{:>8.1}{:>8.1}{:>8.1}{:>8.1}{:>8.1}{:>9.1}{:>8.1}",
                db(s.fundamental / 0.25),
                s.harmonics[0],
                s.harmonics[1],
                s.harmonics[2],
                s.harmonics[3],
                s.thd,
                s.stray,
                s.dc
            );
        }
    }

    println!("\nthe same drive at different input levels (997 Hz, drive 12 dB): THD in dB");
    for level in [-6.0, -12.0, -24.0, -36.0, -48.0] {
        let output = driven(&sine(997.0, from_db(level), 8 * 48_000), 12.0, 18_000.0, 1.0);
        let s = spectrum(&output[6 * 48_000..], 997.0);
        println!("  input {level:>6.0} dBFS: output {:>7.2} dBFS, THD {:>7.1} dB", db(s.fundamental), s.thd);
    }

    println!("\nintermodulation: 150 Hz at -12 dBFS with 2 kHz at -24 dBFS; sidebands relative to the 2 kHz tone");
    for drive in DRIVES {
        let low = sine(150.0, 0.25, 6 * 48_000);
        let high = sine(2_000.0, 0.063, 6 * 48_000);
        let input: Vec<f64> = low.iter().zip(&high).map(|(a, b)| a + b).collect();
        let output = driven(&input, drive, 18_000.0, 1.0);
        let settled = &output[4 * 48_000..];
        let carrier = tone_level(settled, 2_000.0);
        let side = |f: f64| db(tone_level(settled, f) / carrier);
        println!(
            "  drive {drive:>4.0}: 2 kHz {:>7.2} dB, 150 Hz {:>7.2} dB, 1850 {:>6.1}, 2150 {:>6.1}, 1700 {:>6.1}, 2300 {:>6.1}",
            db(carrier / 0.063),
            db(tone_level(settled, 150.0) / 0.25),
            side(1_850.0),
            side(2_150.0),
            side(1_700.0),
            side(2_300.0)
        );
    }

    println!("\nquiet-signal response at mix 50% (tone 18 kHz, drive 6): gain in dB relative to the input");
    for frequency in [100.0, 1_000.0, 4_000.0, 8_000.0, 12_000.0, 16_000.0] {
        // Speech sets the node's working point; the probe follows it.
        let mut input = speech(4.0, 120.0, 3);
        input.extend(sine(frequency, 0.001, 48_000));
        let output = driven(&input, 6.0, 18_000.0, 0.5);
        let tail = &output[output.len() - 24_000..];
        println!("  {frequency:>6.0} Hz: {:>6.2} dB", db(tone_level(tail, frequency) / 0.001));
    }

    println!("\nroom noise for two seconds after speech (tone 18 kHz, mix 100%): change in the noise, dB");
    println!("{:>24}{:>8}{:>8}{:>8}{:>8}", "noise below the voice", "30 dB", "40 dB", "50 dB", "60 dB");
    for drive in DRIVES {
        let voice = low_voice();
        let mut row = format!("{:>24}", format!("drive {drive:.0}"));
        for below in [30.0, 40.0, 50.0, 60.0] {
            let mut input = voice.to_vec();
            input.extend(room(voice, below, 96_000, 9));
            let output = driven(&input, drive, 18_000.0, 1.0);
            let n = input.len();
            row.push_str(&format!("{:>8.2}", db(rms(&output[n - 91_200..]) / rms(&input[n - 91_200..]))));
        }
        println!("{row}");
    }

    for (name, voice) in [("low synthetic voice", speech(8.0, 110.0, 3)), ("high synthetic voice", speech(8.0, 210.0, 5))] {
        println!("\n{name} at -24 dBFS, tone 6 kHz, mix 100%, output 0 dB");
        println!("{:>8}{:>9}{:>9}{:>8}{:>8}{:>8}{:>8}", "drive", "LUFS", "vs dry", "spread", "peak", "crest", "ess");
        let dry = loudness(&voice);
        println!(
            "{:>8}{:>9.2}{:>9.2}{:>8.2}{:>8.2}{:>8.2}{:>8.2}",
            "dry",
            dry,
            0.0,
            loudness_spread(&voice),
            db(peak(&voice)),
            db(peak(&voice) / active_rms(&voice)),
            band_burst_db(&voice, 5_000.0, 10_000.0)
        );
        for drive in DRIVES {
            let output = driven(&voice, drive, 6_000.0, 1.0);
            let l = loudness(&output);
            println!(
                "{drive:>8.0}{l:>9.2}{:>9.2}{:>8.2}{:>8.2}{:>8.2}{:>8.2}",
                l - dry,
                loudness_spread(&output),
                db(peak(&output)),
                db(peak(&output) / active_rms(&output)),
                band_burst_db(&output, 5_000.0, 10_000.0)
            );
        }
        println!("  the same voice 20 dB quieter: loudness against its own dry level");
        let quiet: Vec<f64> = voice.iter().map(|s| s * 0.1).collect();
        for drive in DRIVES {
            let output = driven(&quiet, drive, 6_000.0, 1.0);
            println!("{drive:>8.0}{:>9.2}{:>9.2}", loudness(&output), loudness(&output) - loudness(&quiet));
        }
    }
}

/// The settings used for listening files.
const SETTINGS: [(&str, f64, f64, f64); 6] = [
    ("default", 6.0, 6_000.0, 0.5),
    ("drive-06", 6.0, 6_000.0, 1.0),
    ("drive-12", 12.0, 6_000.0, 1.0),
    ("drive-18", 18.0, 6_000.0, 1.0),
    ("drive-24", 24.0, 6_000.0, 1.0),
    ("drive-36", 36.0, 6_000.0, 1.0),
];

/// Writes listening files for supplied recordings.
///
/// `VINEMIX_DISTORTION_SOURCE` is a 48 kHz WAV file or a directory of them;
/// `VINEMIX_DISTORTION_OUTPUT` is the directory to write into. For every
/// source and setting (Output at 0 dB) it writes the node's actual output
/// (`*-actual.wav`) and a copy matched to the dry loudness (`*-matched.wav`),
/// plus the loudness of each.
#[test]
#[ignore = "writes listening files; needs VINEMIX_DISTORTION_SOURCE and VINEMIX_DISTORTION_OUTPUT"]
fn distortion_render() {
    let source = PathBuf::from(std::env::var("VINEMIX_DISTORTION_SOURCE").expect("VINEMIX_DISTORTION_SOURCE names a WAV file or directory"));
    let output = PathBuf::from(std::env::var("VINEMIX_DISTORTION_OUTPUT").expect("VINEMIX_DISTORTION_OUTPUT names a directory"));
    let label = std::env::var("VINEMIX_DISTORTION_LABEL").unwrap_or_else(|_| "distortion".into());
    let mut sources: Vec<PathBuf> = if source.is_dir() {
        std::fs::read_dir(&source).unwrap().filter_map(|e| e.ok().map(|e| e.path())).filter(|p| p.extension().is_some_and(|e| e == "wav")).collect()
    } else {
        vec![source]
    };
    sources.sort();
    assert!(!sources.is_empty(), "no WAV files found");
    let mut report = String::new();
    for path in &sources {
        let name = path.file_stem().unwrap().to_string_lossy().into_owned();
        let input = read_wav(path);
        let dry = left(&input);
        let dry_loudness = loudness(&dry);
        let mut text = format!("{name} ({label})\n{:<12}{:>9}{:>9}{:>8}{:>8}{:>8}\n", "setting", "LUFS", "vs dry", "peak", "crest", "ess");
        text.push_str(&format!(
            "{:<12}{dry_loudness:>9.2}{:>9.2}{:>8.2}{:>8.2}{:>8.2}\n",
            "dry",
            0.0,
            db(peak(&dry)),
            db(peak(&dry) / active_rms(&dry)),
            band_burst_db(&dry, 5_000.0, 10_000.0)
        ));
        for (setting, drive, tone, mix) in SETTINGS {
            let rendered = render("distortion", set(drive, tone, mix, 0.0), &input);
            let samples = left(&rendered);
            let l = loudness(&samples);
            write_wav(&output.join(format!("{name}-{label}-{setting}-actual.wav")), &rendered);
            write_wav(&output.join(format!("{name}-{label}-{setting}-matched.wav")), &scaled(&rendered, from_db(dry_loudness - l)));
            text.push_str(&format!(
                "{setting:<12}{l:>9.2}{:>9.2}{:>8.2}{:>8.2}{:>8.2}\n",
                l - dry_loudness,
                db(peak(&samples)),
                db(peak(&samples) / active_rms(&samples)),
                band_burst_db(&samples, 5_000.0, 10_000.0)
            ));
        }
        println!("{text}");
        report.push_str(&text);
        report.push('\n');
    }
    std::fs::create_dir_all(&output).unwrap();
    std::fs::write(output.join(format!("measurements-{label}.txt")), report).unwrap();
}

/// Processing cost of one Distortion node, by difference against the same
/// graph with a Gain node in its place.
#[test]
#[ignore = "benchmark; prints timings"]
fn distortion_cost() {
    use std::time::Instant;
    let input = mono(&speech(10.0, 120.0, 7));
    let time = |kind: &str, drive: f64, mix: f64| {
        let mut runs: Vec<f64> = (0..9)
            .map(|_| {
                let mut rig = Rig::new(&single_effect(kind, set(drive, 6_000.0, mix, 0.0)));
                let started = Instant::now();
                std::hint::black_box(rig.run(&input));
                started.elapsed().as_secs_f64()
            })
            .collect();
        runs.sort_by(f64::total_cmp);
        runs[runs.len() / 2]
    };
    let baseline = time("gain", 0.0, 0.0);
    println!("graph without the effect: {:.1} ns per frame", baseline / input.len() as f64 * 1e9);
    for (drive, mix) in [(6.0, 0.5), (24.0, 1.0), (36.0, 1.0)] {
        let seconds = time("distortion", drive, mix) - baseline;
        println!(
            "distortion at drive {drive:>2.0}, mix {mix:.1}: {:>7.1} ns per frame, {:.3}% of one core in real time",
            seconds / input.len() as f64 * 1e9,
            seconds / 10.0 * 100.0
        );
    }
}
