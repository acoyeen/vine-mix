//! The Rust SpeexDSP and RNNoise ports against the pinned C libraries, built
//! exactly as the 0.5 engine built them. Outputs must match bit for bit.

#![cfg(oracle_available)]
#![allow(unsafe_code)]

use std::ffi::c_void;

use vinemix_denoise::{Kernel, RnNoise, SpeexPreprocessor};
use vinemix_oracle::ffi;

/// Deterministic test programme: noise floor, voiced tone bursts, silence,
/// clipping-level peaks and level steps, so every code path is exercised.
fn programme(frame: usize, n: usize, seed: &mut u32) -> f32 {
    *seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
    let noise = ((*seed >> 8) as f32 / (1u32 << 24) as f32 - 0.5) * 0.02;
    let t = (frame * 480 + n) as f32 / 48_000.0;
    let section = (frame / 40) % 6;
    let voice = match section {
        0 => 0.0,
        1 => 0.3 * (2.0 * std::f32::consts::PI * 180.0 * t).sin() * (1.0 + 0.5 * (2.0 * std::f32::consts::PI * 3.0 * t).sin()),
        2 => 0.0,
        3 => 0.8 * (2.0 * std::f32::consts::PI * 440.0 * t).sin() + 0.2 * (2.0 * std::f32::consts::PI * 2900.0 * t).sin(),
        4 => -1.0 + 2.0 * ((t * 220.0) % 1.0),
        _ => 0.05 * (2.0 * std::f32::consts::PI * 90.0 * t).sin(),
    };
    if section == 2 && frame % 40 > 20 { 0.0 } else { voice + noise }
}

#[test]
fn speex_matches_reference_bit_for_bit() {
    // SAFETY: plain C API, state owned for the test's duration.
    unsafe {
        let reference = ffi::speex_preprocess_state_init(480, 48_000);
        let mut off = 0i32;
        ffi::speex_preprocess_ctl(reference, ffi::SPEEX_PREPROCESS_SET_AGC, (&raw mut off).cast::<c_void>());
        ffi::speex_preprocess_ctl(reference, ffi::SPEEX_PREPROCESS_SET_DEREVERB, (&raw mut off).cast::<c_void>());
        let mut port = SpeexPreprocessor::new(480, 48_000);
        let mut seed = 99u32;
        let mut compared = 0usize;
        for frame in 0..2400 {
            // Live suppression edits, as the engine performs them.
            let mut suppress = -18 - ((frame / 300) as i32 % 4) * 7;
            ffi::speex_preprocess_ctl(reference, ffi::SPEEX_PREPROCESS_SET_NOISE_SUPPRESS, (&raw mut suppress).cast::<c_void>());
            port.set_noise_suppress(suppress);
            let mut block = [0i16; 480];
            for (n, sample) in block.iter_mut().enumerate() {
                let value = programme(frame, n, &mut seed).clamp(-1.0, 32767.0 / 32768.0);
                *sample = (value * 32768.0).round() as i16;
            }
            let mut expected = block;
            ffi::speex_preprocess_run(reference, expected.as_mut_ptr());
            port.run(&mut block);
            assert_eq!(block, expected, "frame {frame}");
            compared += 480;
        }
        assert_eq!(compared, 2400 * 480);
        ffi::speex_preprocess_state_destroy(reference);
    }
}

fn rnnoise_parity(kernel: Kernel) {
    // SAFETY: plain C API; RNNoise selects its kernel with the same rule.
    unsafe {
        let reference = match kernel {
            Kernel::Sse2 => ffi::am_oracle_rnnoise_create_baseline(),
            Kernel::Avx2Fma => ffi::rnnoise_create(std::ptr::null_mut()),
        };
        assert!(!reference.is_null());
        let mut port = RnNoise::with_kernel(kernel);
        let mut seed = 7u32;
        let mut mismatches = 0usize;
        let mut worst = 0.0f32;
        for frame in 0..1500 {
            let mut input = [0.0f32; 480];
            for (n, sample) in input.iter_mut().enumerate() {
                *sample = programme(frame, n, &mut seed).clamp(-1.0, 1.0) * 32768.0;
            }
            let mut expected = [0.0f32; 480];
            let mut actual = [0.0f32; 480];
            let vad_expected = ffi::rnnoise_process_frame(reference, expected.as_mut_ptr(), input.as_ptr());
            let vad_actual = port.process_frame(&mut actual, &input);
            assert_eq!(vad_actual.to_bits(), vad_expected.to_bits(), "{kernel:?} VAD probability at frame {frame}: {vad_actual} vs {vad_expected}");
            for (a, e) in actual.iter().zip(&expected) {
                if a.to_bits() != e.to_bits() {
                    mismatches += 1;
                    worst = worst.max((a - e).abs());
                }
            }
            assert!(mismatches == 0, "frame {frame}: {mismatches} samples differ (worst {worst}); vad {vad_actual} vs {vad_expected}");
        }
        ffi::rnnoise_destroy(reference);
    }
}

#[test]
fn rnnoise_matches_reference_bit_for_bit() {
    for kernel in [Kernel::Sse2, Kernel::Avx2Fma] {
        if kernel.available() {
            println!("Comparing RNNoise audio and voice probability with {kernel:?}");
            rnnoise_parity(kernel);
        }
    }
}

#[test]
fn speex_state_copy_continues_like_the_original() {
    let mut a = SpeexPreprocessor::new(480, 48_000);
    let mut seed = 3u32;
    for frame in 0..50 {
        let mut block = [0i16; 480];
        for (n, sample) in block.iter_mut().enumerate() {
            *sample = (programme(frame, n, &mut seed) * 20_000.0) as i16;
        }
        a.run(&mut block);
    }
    let mut b = SpeexPreprocessor::new(480, 48_000);
    b.copy_state_from(&a);
    for frame in 50..80 {
        let mut x = [0i16; 480];
        for (n, sample) in x.iter_mut().enumerate() {
            *sample = (programme(frame, n, &mut seed) * 20_000.0) as i16;
        }
        let mut y = x;
        a.run(&mut x);
        b.run(&mut y);
        assert_eq!(x, y);
    }
}

/// RNNoise and Speex alone: the port against the pinned C libraries
/// (`cargo test -p vinemix-oracle --release --test denoise_parity -- --ignored --nocapture`).
#[test]
#[ignore = "benchmark"]
fn denoise_benchmark() {
    const FRAMES: usize = 2_000;
    let mut seed = 7u32;
    let inputs: Vec<[f32; 480]> = (0..FRAMES)
        .map(|frame| {
            let mut input = [0.0f32; 480];
            for (n, sample) in input.iter_mut().enumerate() {
                *sample = programme(frame, n, &mut seed).clamp(-1.0, 1.0) * 32768.0;
            }
            input
        })
        .collect();
    let median = |mut v: Vec<f64>| {
        v.sort_by(f64::total_cmp);
        v[v.len() / 2]
    };
    let (mut c, mut r) = (Vec::new(), Vec::new());
    for _ in 0..5 {
        // SAFETY: plain C API.
        unsafe {
            let reference = ffi::rnnoise_create(std::ptr::null_mut());
            let mut out = [0.0f32; 480];
            let start = std::time::Instant::now();
            for input in &inputs {
                ffi::rnnoise_process_frame(reference, out.as_mut_ptr(), input.as_ptr());
            }
            c.push(start.elapsed().as_secs_f64());
            ffi::rnnoise_destroy(reference);
        }
        let mut port = RnNoise::with_kernel(Kernel::detect());
        let mut out = [0.0f32; 480];
        let start = std::time::Instant::now();
        for input in &inputs {
            port.process_frame(&mut out, input);
        }
        r.push(start.elapsed().as_secs_f64());
    }
    let (c, r) = (median(c), median(r));
    println!("BENCH rnnoise: C {:.2} us/frame, Rust {:.2} us/frame, Rust/C = {:.3}", c / FRAMES as f64 * 1e6, r / FRAMES as f64 * 1e6, r / c);
}
