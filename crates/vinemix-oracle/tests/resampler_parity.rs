//! The Rust sinc converter against libsamplerate 0.2.2 (SRC_SINC_FASTEST),
//! driven exactly as the engine's adapter drives it: streaming, varying
//! chunk sizes and a slowly moving drift correction.

#![cfg(oracle_available)]
#![allow(unsafe_code)]

use std::ffi::c_long;

use vinemix_oracle::ffi;
use vinemix_resample::SincConverter;

fn run(input_rate: u32, output_rate: u32) {
    let base = f64::from(output_rate) / f64::from(input_rate);
    let mut error = 0;
    // SAFETY: plain C API, freed at the end.
    let reference = unsafe { ffi::src_new(ffi::SRC_SINC_FASTEST, 2, &mut error) };
    assert!(!reference.is_null() && error == 0);
    // SAFETY: valid state.
    unsafe { ffi::src_set_ratio(reference, base) };
    let mut port = SincConverter::new();
    port.set_ratio(base).unwrap();
    let mut seed = 5u32;
    let mut phase = 0.0f64;
    let mut pending_c: Vec<f32> = Vec::new();
    let mut pending_rust: Vec<f32> = Vec::new();
    let mut total = 0usize;
    for block in 0..3000 {
        // Device-sized input chunks with jitter.
        seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
        let frames = 96 + (seed >> 24) as usize % 400;
        for _ in 0..frames {
            phase += 2.0 * std::f64::consts::PI * 997.0 / f64::from(input_rate);
            seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
            let noise = ((seed >> 9) as f32 / (1u32 << 23) as f32 - 0.5) * 0.05;
            let x = (phase.sin() * 0.7) as f32 + noise;
            pending_c.extend([x, -x * 0.5]);
        }
        pending_rust.clone_from(&pending_c);
        let correction = 1.0 + 0.0019 * ((block as f64) * 0.01).sin();
        let ratio = base * correction;
        let wanted = 240 + (block % 7) * 13;
        let mut out_c = vec![0.0f32; wanted * 2];
        let mut out_rust = vec![0.0f32; wanted * 2];
        let mut data = ffi::SrcData {
            data_in: pending_c.as_ptr(),
            data_out: out_c.as_mut_ptr(),
            input_frames: (pending_c.len() / 2) as c_long,
            output_frames: wanted as c_long,
            input_frames_used: 0,
            output_frames_gen: 0,
            end_of_input: 0,
            src_ratio: ratio,
        };
        // SAFETY: buffers sized as declared.
        let status = unsafe { ffi::src_process(reference, &mut data) };
        assert_eq!(status, 0);
        let result = port.process(&pending_rust, &mut out_rust, ratio).unwrap();
        assert_eq!(result.input_frames_used, data.input_frames_used as usize, "block {block}");
        assert_eq!(result.output_frames_generated, data.output_frames_gen as usize, "block {block}");
        for (n, (a, b)) in out_rust.iter().zip(&out_c).take(result.output_frames_generated * 2).enumerate() {
            assert!(a.to_bits() == b.to_bits(), "{input_rate}->{output_rate} block {block} sample {n}: {a} vs {b}");
        }
        total += result.output_frames_generated;
        pending_c.drain(..result.input_frames_used * 2);
        if block == 1500 {
            // SAFETY: valid state.
            unsafe {
                ffi::src_reset(reference);
                ffi::src_set_ratio(reference, base);
            }
            port.reset();
            port.set_ratio(base).unwrap();
            pending_c.clear();
        }
    }
    assert!(total > 100_000);
    // SAFETY: created above, deleted once.
    unsafe { ffi::src_delete(reference) };
}

#[test]
fn matches_libsamplerate_44_1_to_48() {
    run(44_100, 48_000);
}

#[test]
fn matches_libsamplerate_48_to_44_1() {
    run(48_000, 44_100);
}

#[test]
fn matches_libsamplerate_96_to_48_and_48_to_48() {
    run(96_000, 48_000);
    run(48_000, 48_000);
    run(16_000, 48_000);
}
