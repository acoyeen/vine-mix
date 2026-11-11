//! Voice Color: what the node does to speech, measured.
//!
//! The signals are synthetic and deterministic (see `support::speech`): they
//! have syllables of different loudness, sibilants, plosives and a noise
//! floor, which is what the node reacts to, but they are not a human voice.
//! These tests hold the behaviour the design promises. Whether it sounds good
//! is a listening judgement they cannot make.
//!
//! The two ignored tests are tools, not checks. `voice_color_render` writes
//! listening files and a measurement table for WAV files you supply (use
//! `scripts/render-voice-color.ps1`); `voice_color_cost` prints the node's
//! processing cost. Run them with
//! `cargo test -p vinemix-dsp --release --test voice_color -- --ignored --nocapture`.

mod support;

use std::path::PathBuf;
use std::sync::OnceLock;

use support::*;
use vinemix_dsp::effects::voice::VoiceColor;
use vinemix_model::config::{GraphNode, VoiceColorSettings};
use vinemix_model::voice::VoiceContour;
use vinemix_rt::Frame;

fn pad(tone: f64, character: f64) -> impl FnOnce(&mut GraphNode) {
    move |node| {
        node.voice_color.tone = tone;
        node.voice_color.ambience = character;
    }
}

fn contour(tone: f64, character: f64) -> VoiceContour {
    VoiceContour::of(&VoiceColorSettings { tone, ambience: character })
}

fn voiced(input: &[f64], tone: f64, character: f64) -> Vec<f64> {
    left(&render("voiceColor", pad(tone, character), &mono(input)))
}

/// Six seconds of a low and of a high synthetic voice at -24 dBFS.
fn low_voice() -> &'static [f64] {
    static VOICE: OnceLock<Vec<f64>> = OnceLock::new();
    VOICE.get_or_init(|| speech(6.0, 110.0, 3))
}

fn high_voice() -> &'static [f64] {
    static VOICE: OnceLock<Vec<f64>> = OnceLock::new();
    VOICE.get_or_init(|| speech(6.0, 210.0, 5))
}

fn gained(samples: &[f64], db: f64) -> Vec<f64> {
    let gain = from_db(db);
    samples.iter().map(|s| s * gain).collect()
}

const CORNERS: [(f64, f64); 8] = [(0.0, 0.5), (0.0, 1.0), (-1.0, 0.0), (1.0, 0.0), (-1.0, 1.0), (1.0, 1.0), (-0.5, 0.5), (0.5, 0.5)];

#[test]
fn neutral_is_exactly_the_input() {
    let mut noise = Noise::new(11);
    let speechlike = low_voice();
    // Ordinary speech, a hot signal well over full scale, and different
    // material in each channel.
    let mut input: Vec<Frame> = speechlike.iter().map(|&s| Frame::new(s as f32, (s * 0.5) as f32)).collect();
    input.extend(speechlike.iter().take(48_000).map(|&s| Frame::new((s * 40.0) as f32, noise.next() as f32)));
    let output = render("voiceColor", pad(0.0, 0.0), &input);
    for (n, (a, b)) in input.iter().zip(&output).enumerate() {
        assert!(a.left == b.left && a.right == b.right, "frame {n}: {a:?} became {b:?}");
    }
}

#[test]
fn returning_to_neutral_becomes_exact_again() {
    let input = mono(low_voice());
    let mut rig = Rig::new(&single_effect("voiceColor", pad(1.0, 1.0)));
    rig.run(&input);
    rig.edit(&single_effect("voiceColor", pad(0.0, 0.0)));
    // The gains reach their targets in 10 ms; the dynamics and the ceiling
    // release back to exactly unity within a few seconds.
    rig.run(&input);
    let output = rig.run(&input);
    for (n, (a, b)) in input.iter().zip(&output).enumerate() {
        assert!(a.left == b.left && a.right == b.right, "frame {n}: {a:?} became {b:?}");
    }
}

#[test]
fn bypass_passes_the_input_untouched() {
    let input = mono(low_voice());
    let output = render(
        "voiceColor",
        |node| {
            node.voice_color.tone = 1.0;
            node.voice_color.ambience = 1.0;
            node.bypass = true;
        },
        &input,
    );
    assert!(input.iter().zip(&output).all(|(a, b)| a.left == b.left && a.right == b.right));
}

/// The filters in the processor are the ones the model describes: a quiet
/// probe tone, riding on a low carrier so the sibilance guard stays open,
/// comes out at the level the published response predicts.
#[test]
fn the_filters_match_the_published_contour() {
    let probes = [40.0, 75.0, 110.0, 165.0, 220.0, 420.0, 700.0, 1_000.0, 2_000.0, 3_300.0, 5_000.0, 9_000.0, 14_000.0];
    for (tone, character) in [(0.0, 1.0), (0.0, 0.4), (-1.0, 0.0), (1.0, 0.0), (0.6, 0.8), (-0.7, 1.0)] {
        // The contour alone: no dynamics or harmonics, and no trim.
        let shape = VoiceContour { amount: 0.0, trim: 1.0, ..contour(tone, character) };
        for probe in probes {
            let mut voice = VoiceColor::default();
            voice.configure(&shape);
            let mut output = Vec::with_capacity(96_000);
            for n in 0..96_000 {
                let t = f64::from(n) / RATE;
                let x = 0.1 * (2.0 * std::f64::consts::PI * 300.0 * t).sin() + 0.005 * (2.0 * std::f64::consts::PI * probe * t).sin();
                output.push(f64::from(voice.process(Frame::new(x as f32, x as f32)).left));
            }
            let measured = db(tone_level(&output[48_000..], probe) / 0.005);
            let expected = db(shape.response(probe));
            assert!((measured - expected).abs() < 0.1, "({tone}, {character}) at {probe} Hz: measured {measured:.2} dB, published {expected:.2} dB");
        }
    }
}

/// The old node lost 7 to 10 LU as Character rose, so "more" sounded like
/// "quieter". This one keeps speech at about its dry loudness everywhere.
#[test]
fn loudness_stays_close_to_dry_at_every_position() {
    for (name, voice) in [("low", low_voice()), ("high", high_voice())] {
        let dry = loudness(voice);
        for tone in [-1.0, -0.5, 0.0, 0.5, 1.0] {
            for character in [0.0, 0.5, 1.0] {
                let change = loudness(&voiced(voice, tone, character)) - dry;
                assert!(change.abs() < 2.5, "{name} voice at ({tone}, {character}): loudness changed by {change:.2} LU");
            }
        }
    }
}

/// The dynamics follow the speaker, not a fixed threshold: the same voice 20
/// dB quieter gets the same treatment, 20 dB quieter.
#[test]
fn a_quieter_microphone_gets_the_same_treatment() {
    for (tone, character) in [(0.0, 1.0), (1.0, 1.0), (-1.0, 0.5)] {
        let normal = voiced(low_voice(), tone, character);
        let quiet = voiced(&gained(low_voice(), -20.0), tone, character);
        let difference = loudness(&normal) - loudness(&quiet);
        assert!((difference - 20.0).abs() < 0.75, "({tone}, {character}): 20 dB less in gave {difference:.2} LU less out");
        let spread = loudness_spread(&normal) - loudness_spread(&quiet);
        assert!(spread.abs() < 0.75, "({tone}, {character}): spread differs by {spread:.2} dB");
        for (a, b) in band_levels(&normal).iter().zip(band_levels(&quiet)).skip(1) {
            assert!((a - b - 20.0).abs() < 1.0, "({tone}, {character}): a band moved by {:.2} dB", a - b - 20.0);
        }
    }
}

#[test]
fn character_makes_the_delivery_steadier() {
    for (name, voice) in [("low", low_voice()), ("high", high_voice())] {
        let dry = loudness_spread(voice);
        let half = loudness_spread(&voiced(voice, 0.0, 0.5));
        let full = loudness_spread(&voiced(voice, 0.0, 1.0));
        assert!(dry - half > 1.0, "{name}: spread {dry:.2} -> {half:.2} dB at half Character");
        assert!(dry - full > 3.0, "{name}: spread {dry:.2} -> {full:.2} dB at full Character");
        assert!(full < half, "{name}: more Character is steadier");
        // Tone alone leaves the dynamics alone.
        let tilted = loudness_spread(&voiced(voice, 1.0, 0.0));
        assert!((tilted - dry).abs() < 1.5, "{name}: Tone changed the spread from {dry:.2} to {tilted:.2}");
    }
}

/// Two words, one 14 dB quieter than the other, repeated. The quiet one is
/// brought up; the loud one stays about where it was.
#[test]
fn quiet_words_rise_and_loud_words_hold() {
    let word = (0.5 * RATE) as usize;
    let mut input = Vec::new();
    for index in 0..16 {
        let level = if index % 2 == 0 { 0.1 } else { 0.1 * from_db(-14.0) };
        for n in 0..word {
            let envelope = if n < word * 3 / 4 { (std::f64::consts::PI * n as f64 / (word * 3 / 4) as f64).sin().powf(0.5) } else { 0.0 };
            let t = n as f64 / RATE;
            let tone = (2.0 * std::f64::consts::PI * 140.0 * t).sin() + 0.5 * (2.0 * std::f64::consts::PI * 700.0 * t).sin() + 0.2 * (2.0 * std::f64::consts::PI * 1_200.0 * t).sin();
            input.push(level * envelope * tone);
        }
    }
    let output = voiced(&input, 0.0, 1.0);
    // Skip the first four words while the level is learned.
    let levels = |signal: &[f64], parity: usize| {
        let chosen: Vec<f64> = (4..16).filter(|i| i % 2 == parity).map(|i| db(rms(&signal[i * word..i * word + word * 3 / 4]))).collect();
        chosen.iter().sum::<f64>() / chosen.len() as f64
    };
    let gap_in = levels(&input, 0) - levels(&input, 1);
    let gap_out = levels(&output, 0) - levels(&output, 1);
    assert!(gap_in - gap_out > 5.0, "the 14 dB gap became {gap_out:.2} dB");
    assert!(gap_out > 2.0, "the louder word is still the louder one: {gap_out:.2} dB");
    let loud = levels(&output, 0) - levels(&input, 0);
    assert!(loud.abs() < 3.0, "the loud word moved by {loud:.2} dB");
}

/// Speech, then two seconds of the room alone. Whatever the pad does to the
/// voice, the room between words is not turned up.
#[test]
fn pauses_and_room_noise_are_not_lifted() {
    let mut noise = Noise::new(23);
    let room = from_db(-62.0) * 3f64.sqrt();
    let mut input: Vec<f64> = low_voice().iter().map(|s| s + room * noise.next()).collect();
    let pause = input.len();
    input.extend((0..96_000).map(|_| room * noise.next()));
    let tail = pause + 48_000..pause + 96_000;
    for character in [0.5, 1.0] {
        let output = voiced(&input, 0.0, character);
        // Compare where the contour is nearly flat, so this measures the
        // dynamics and not the equaliser.
        let before = db(rms(&band(&input[tail.clone()], 700.0, 1_600.0)));
        let after = db(rms(&band(&output[tail.clone()], 700.0, 1_600.0)));
        let trim = db(contour(0.0, character).trim * contour(0.0, character).response(1_000.0));
        assert!(after - before < trim + 1.0, "character {character}: the room changed by {:.2} dB (contour alone {trim:.2})", after - before);
        // The whole pause, in every band, never comes out louder than it
        // went in by more than the contour's own boost of that band.
        let overall = db(rms(&output[tail.clone()])) - db(rms(&input[tail.clone()]));
        assert!(overall < 3.0, "character {character}: the room is {overall:.2} dB louder overall");
    }
}

/// Brightening raises the presence of vowels by several decibels but does not
/// raise how far the sibilants stick out by anything like as much.
#[test]
fn brightening_does_not_sharpen_sibilants() {
    for (name, voice) in [("low", low_voice()), ("high", high_voice())] {
        let dry_ess = band_burst_db(voice, 5_000.0, 10_000.0);
        let dry_bands = band_levels(voice);
        for (tone, character, presence_gain, ess_limit) in [(0.0, 1.0, 4.0, 2.5), (1.0, 0.0, 2.0, 4.0), (1.0, 1.0, 6.0, 7.0)] {
            let output = voiced(voice, tone, character);
            let bands = band_levels(&output);
            // Presence relative to the mid band: the change in colour.
            let presence = (bands[4] - bands[3]) - (dry_bands[4] - dry_bands[3]);
            let ess = band_burst_db(&output, 5_000.0, 10_000.0) - dry_ess;
            // What a plain equaliser with this contour would do to a sibilant.
            let shape = contour(tone, character);
            let unguarded = db(shape.response(7_000.0) * shape.trim);
            assert!(presence > presence_gain, "{name} at ({tone}, {character}): presence rose only {presence:.2} dB");
            assert!(ess < ess_limit, "{name} at ({tone}, {character}): sibilants stick out {ess:.2} dB more");
            if tone > 0.0 {
                assert!(ess < unguarded - 2.0, "{name} at ({tone}, {character}): sibilants rose {ess:.2} dB; the contour alone would give {unguarded:.2} dB");
            }
        }
    }
}

#[test]
fn the_node_never_adds_peaks_above_the_ceiling() {
    let ceiling = from_db(-1.0);
    for hot in [-9.0, -3.0, 0.0, 6.0] {
        // Speech whose own peak is `hot` dBFS.
        let input = gained(low_voice(), hot - db(peak(low_voice())));
        let input_peak = peak(&input);
        for (tone, character) in CORNERS {
            let output = render("voiceColor", pad(tone, character), &mono(&input));
            let output_peak = peak(&left(&output)).max(peak(&right(&output)));
            assert!(
                output_peak <= ceiling.max(input_peak) * 1.000_001,
                "input peak {hot} dBFS at ({tone}, {character}): output peak {:.2} dBFS",
                db(output_peak)
            );
        }
    }
}

/// The harmonics are made from the band below the consonants, so they stay
/// below the Nyquist frequency instead of folding back as aliasing.
#[test]
fn harmonics_are_modest_and_do_not_alias() {
    // A vowel-range tone gains low-order harmonics.
    let low = voiced(&sine(250.0, 0.1, 144_000), 0.0, 1.0);
    let settled = &low[96_000..];
    let fundamental = windowed_tone_level(settled, 250.0);
    let second = db(windowed_tone_level(settled, 500.0) / fundamental);
    let third = db(windowed_tone_level(settled, 750.0) / fundamental);
    assert!(second > -50.0 && second < -15.0, "second harmonic {second:.1} dBc");
    assert!(third > -50.0 && third < -15.0, "third harmonic {third:.1} dBc");
    // Nothing appears between the harmonics.
    for stray in [375.0, 625.0, 1_100.0, 7_300.0, 15_050.0] {
        let level = db(windowed_tone_level(settled, stray) / fundamental);
        assert!(level < -90.0, "{stray} Hz at {level:.1} dBc");
    }
    // A consonant-range tone gains nothing measurable, at its harmonics or
    // where they would fold back to (25 kHz -> 23 kHz, 30 kHz -> 18 kHz).
    let high = voiced(&sine(5_000.0, 0.1, 144_000), 0.0, 1.0);
    let settled = &high[96_000..];
    let fundamental = windowed_tone_level(settled, 5_000.0);
    for (product, limit) in [(10_000.0, -65.0), (15_000.0, -65.0), (20_000.0, -65.0), (23_000.0, -90.0), (18_000.0, -90.0)] {
        let level = db(windowed_tone_level(settled, product) / fundamental);
        assert!(level < limit, "{product} Hz at {level:.1} dBc");
    }
    // At neutral there are no harmonics at all.
    let clean = voiced(&sine(250.0, 0.1, 96_000), 0.0, 0.0);
    assert!(tone_level(&clean[48_000..], 500.0) < 1e-7);
}

#[test]
fn channels_stay_linked() {
    // A mono voice stays exactly mono.
    for (tone, character) in CORNERS {
        let output = render("voiceColor", pad(tone, character), &mono(low_voice()));
        assert!(output.iter().all(|f| f.left == f.right), "({tone}, {character}) split a mono voice");
    }
    // An off-centre voice keeps its place, and a silent channel stays silent.
    let offset: Vec<Frame> = low_voice().iter().map(|&s| Frame::new(s as f32, (s * 0.5) as f32)).collect();
    let output = render("voiceColor", pad(0.3, 1.0), &offset);
    let balance = db(rms(&right(&output)) / rms(&left(&output)));
    // The gains are linked, so the image does not wander. The harmonics are
    // made per channel and round the louder channel's peaks a little more.
    assert!((balance + 6.02).abs() < 1.0, "a 6 dB offset became {balance:.2} dB");
    let one_sided: Vec<Frame> = low_voice().iter().map(|&s| Frame::new(s as f32, 0.0)).collect();
    let output = render("voiceColor", pad(0.3, 1.0), &one_sided);
    assert!(output.iter().all(|f| f.right == 0.0));
}

#[test]
fn the_node_adds_no_delay() {
    for (tone, character) in [(1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (1.0, 1.0)] {
        let mut input = vec![0.0; 4_800];
        input[1_000] = 0.5;
        let output = voiced(&input, tone, character);
        assert!(output[..1_000].iter().all(|&s| s == 0.0), "({tone}, {character}) sounded before its input");
        let loudest = output.iter().enumerate().fold((0, 0.0f64), |best, (n, s)| if s.abs() > best.1 { (n, s.abs()) } else { best }).0;
        assert_eq!(loudest, 1_000, "({tone}, {character}) moved the impulse");
    }
}

#[test]
fn silence_stays_silent_and_tails_end() {
    for (tone, character) in CORNERS {
        let silent = render("voiceColor", pad(tone, character), &vec![Frame::SILENT; 24_000]);
        assert!(silent.iter().all(|f| f.left == 0.0 && f.right == 0.0), "({tone}, {character}) made sound from silence");
    }
    let mut input = low_voice().to_vec();
    let end = input.len();
    input.extend(std::iter::repeat_n(0.0, 144_000));
    let output = voiced(&input, 1.0, 1.0);
    assert!(output[end + 96_000..].iter().all(|&s| s == 0.0), "the node rings on after its input stops");
}

/// The pad is dragged while audio runs: every quantum lands on a new position.
#[test]
fn pad_moves_are_smooth() {
    let mut noise = Noise::new(5);
    let mut rig = Rig::new(&single_effect("voiceColor", pad(0.0, 0.0)));
    let input = mono(&sine(200.0, 0.1, 96_000));
    let mut output = Vec::new();
    for block in input.chunks(240) {
        rig.edit(&single_effect("voiceColor", pad(noise.next(), noise.next().abs())));
        output.extend(rig.run(block));
    }
    let samples = left(&output);
    assert!(samples.iter().all(|s| s.is_finite()));
    // A 200 Hz tone at 0.1 moves 0.0026 per frame; even at the pad's largest
    // boost that is about 0.012. A click would be an order of magnitude more.
    let step = samples.windows(2).map(|w| (w[1] - w[0]).abs()).fold(0.0, f64::max);
    assert!(step < 0.025, "largest step between frames {step:.4}");
}

/// The speaker drops 20 dB (a lower microphone gain): within about fifteen
/// seconds the node treats the new level as it would have from the start.
#[test]
fn the_node_learns_a_new_level() {
    let phrase = low_voice();
    let quiet = gained(phrase, -20.0);
    let mut moved = phrase.to_vec();
    let mut settled = Vec::new();
    for _ in 0..4 {
        moved.extend_from_slice(&quiet);
        settled.extend_from_slice(&quiet);
    }
    let after = voiced(&moved, 0.0, 1.0);
    let reference = voiced(&settled, 0.0, 1.0);
    let last = phrase.len();
    let difference = loudness(&after[after.len() - last..]) - loudness(&reference[reference.len() - last..]);
    assert!(difference.abs() < 1.5, "after the drop the voice is still {difference:.2} LU from where it settles");
}

/// The pad positions used for listening files and measurement tables.
const POSITIONS: [(&str, f64, f64); 7] = [
    ("neutral", 0.0, 0.0),
    ("character-50", 0.0, 0.5),
    ("character-100", 0.0, 1.0),
    ("warm-natural", -1.0, 0.0),
    ("bright-natural", 1.0, 0.0),
    ("warm-broadcast", -1.0, 1.0),
    ("bright-broadcast", 1.0, 1.0),
];

struct Measured {
    loudness: f64,
    spread: f64,
    peak: f64,
    crest: f64,
    sibilance: f64,
    bands: Vec<f64>,
}

fn measure(frames: &[Frame]) -> Measured {
    let samples = left(frames);
    let active = active_rms(&samples);
    Measured {
        loudness: loudness(&samples),
        spread: loudness_spread(&samples),
        peak: db(peak(&samples).max(peak(&right(frames)))),
        crest: db(peak(&samples) / active.max(1e-12)),
        sibilance: band_burst_db(&samples, 5_000.0, 10_000.0),
        bands: band_levels(&samples),
    }
}

fn table(title: &str, rows: &[(String, Measured)]) -> String {
    let mut text = format!("{title}\n{:<22}{:>9}{:>8}{:>8}{:>8}{:>8}", "position", "LUFS", "spread", "peak", "crest", "ess");
    for (name, _, _) in BANDS {
        text.push_str(&format!("{name:>10}"));
    }
    text.push('\n');
    let dry = &rows[0].1;
    for (name, m) in rows {
        text.push_str(&format!("{name:<22}{:>9.2}{:>8.2}{:>8.2}{:>8.2}{:>8.2}", m.loudness, m.spread, m.peak, m.crest, m.sibilance));
        for (level, reference) in m.bands.iter().zip(&dry.bands) {
            // Band levels relative to the dry signal's, after removing the
            // overall loudness change: the shape, not the volume.
            text.push_str(&format!("{:>10.2}", (level - reference) - (m.loudness - dry.loudness)));
        }
        text.push('\n');
    }
    text
}

/// Writes listening files and measurements for supplied recordings.
///
/// `VINEMIX_VOICE_SOURCE` is a 48 kHz WAV file or a directory of them;
/// `VINEMIX_VOICE_OUTPUT` is the directory to write into. For every source
/// and pad position it writes the node's actual output (`*-actual.wav`) and a
/// copy matched to the dry loudness (`*-matched.wav`), plus a measurement
/// table. Columns: integrated loudness (LUFS, BS.1770), spread of momentary
/// loudness (dB), sample peak (dBFS), crest factor (dB), loudest 10 ms of
/// 5-10 kHz relative to the speech level (dB), then each band's change in
/// shape (dB, with the overall loudness change removed).
#[test]
#[ignore = "writes listening files; needs VINEMIX_VOICE_SOURCE and VINEMIX_VOICE_OUTPUT"]
fn voice_color_render() {
    let source = PathBuf::from(std::env::var("VINEMIX_VOICE_SOURCE").expect("VINEMIX_VOICE_SOURCE names a WAV file or directory"));
    let output = PathBuf::from(std::env::var("VINEMIX_VOICE_OUTPUT").expect("VINEMIX_VOICE_OUTPUT names a directory"));
    let label = std::env::var("VINEMIX_VOICE_LABEL").unwrap_or_else(|_| "voice-color".into());
    let mut sources: Vec<PathBuf> = if source.is_dir() {
        std::fs::read_dir(&source).unwrap().filter_map(|e| e.ok().map(|e| e.path())).filter(|p| p.extension().is_some_and(|e| e == "wav")).collect()
    } else {
        vec![source]
    };
    sources.sort();
    assert!(!sources.is_empty(), "no WAV files found");
    let mut report = String::new();
    let mut inputs: Vec<(String, Vec<Frame>)> =
        sources.iter().map(|path| (path.file_stem().unwrap().to_string_lossy().into_owned(), read_wav(path))).collect();
    // The deterministic signals the regression tests use, for reference.
    inputs.push(("synthetic-low".into(), mono(&speech(8.0, 110.0, 3))));
    inputs.push(("synthetic-high".into(), mono(&speech(8.0, 210.0, 5))));
    for (name, input) in &inputs {
        let mut rows = vec![("dry".to_owned(), measure(input))];
        let dry_loudness = rows[0].1.loudness;
        for (position, tone, character) in POSITIONS {
            let rendered = render("voiceColor", pad(tone, character), input);
            let measured = measure(&rendered);
            write_wav(&output.join(format!("{name}-{label}-{position}-actual.wav")), &rendered);
            write_wav(&output.join(format!("{name}-{label}-{position}-matched.wav")), &scaled(&rendered, from_db(dry_loudness - measured.loudness)));
            rows.push((position.to_owned(), measured));
        }
        let text = table(&format!("{name} ({label})"), &rows);
        println!("{text}");
        report.push_str(&text);
        report.push('\n');
    }
    std::fs::create_dir_all(&output).unwrap();
    std::fs::write(output.join(format!("measurements-{label}.txt")), report).unwrap();
}

/// Processing cost of one Voice Color node, by difference against the same
/// graph with a Gain node in its place.
#[test]
#[ignore = "benchmark; prints timings"]
fn voice_color_cost() {
    use std::time::Instant;
    let input = mono(&speech(10.0, 120.0, 7));
    let time = |kind: &str, tone: f64, character: f64| {
        let mut runs: Vec<f64> = (0..9)
            .map(|_| {
                let mut rig = Rig::new(&single_effect(kind, pad(tone, character)));
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
    for (position, tone, character) in POSITIONS {
        let seconds = time("voiceColor", tone, character) - baseline;
        println!(
            "voice color at {position:<18} {:>7.1} ns per frame, {:.3}% of one core in real time",
            seconds / input.len() as f64 * 1e9,
            seconds / 10.0 * 100.0
        );
    }
}
