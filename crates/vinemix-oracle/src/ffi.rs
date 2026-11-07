//! Raw bindings to the reference libraries and small safe owners.

use std::ffi::{c_char, c_int, c_long, c_void};

pub const QUANTUM: usize = 240;
pub const NODES: usize = 32;

unsafe extern "C" {
    pub fn am_oracle_create(graph_json: *const c_char, error: *mut c_char, error_length: c_int) -> *mut c_void;
    pub fn am_oracle_update(
        handle: *mut c_void,
        graph_json: *const c_char,
        mode: c_int,
        error: *mut c_char,
        error_length: c_int,
    ) -> c_int;
    pub fn am_oracle_destroy(handle: *mut c_void);
    pub fn am_oracle_process(handle: *mut c_void, inputs: *const *const f32, defer_sink_clipping: c_int);
    pub fn am_oracle_output(handle: *mut c_void, node: c_int) -> *const f32;
    pub fn am_oracle_meter(handle: *mut c_void, node: c_int, out: *mut f32);
    pub fn am_oracle_reset(handle: *mut c_void);
    pub fn am_oracle_signal_channels(handle: *mut c_void, capture: *const c_int, out: *mut c_int) -> c_int;
    pub fn am_oracle_validate(graph_json: *const c_char, error: *mut c_char, error_length: c_int);

    pub fn speex_preprocess_state_init(frame_size: c_int, sampling_rate: c_int) -> *mut c_void;
    pub fn speex_preprocess_state_destroy(state: *mut c_void);
    pub fn speex_preprocess_run(state: *mut c_void, x: *mut i16) -> c_int;
    pub fn speex_preprocess_ctl(state: *mut c_void, request: c_int, value: *mut c_void) -> c_int;

    pub fn rnnoise_create(model: *mut c_void) -> *mut c_void;
    pub fn rnnoise_destroy(state: *mut c_void);
    pub fn rnnoise_process_frame(state: *mut c_void, out: *mut f32, input: *const f32) -> f32;

    pub fn src_new(converter_type: c_int, channels: c_int, error: *mut c_int) -> *mut c_void;
    pub fn src_delete(state: *mut c_void) -> *mut c_void;
    pub fn src_process(state: *mut c_void, data: *mut SrcData) -> c_int;
    pub fn src_set_ratio(state: *mut c_void, ratio: f64) -> c_int;
    pub fn src_reset(state: *mut c_void) -> c_int;
}

/// `SRC_DATA` from samplerate.h.
#[repr(C)]
#[derive(Debug)]
pub struct SrcData {
    pub data_in: *const f32,
    pub data_out: *mut f32,
    pub input_frames: c_long,
    pub output_frames: c_long,
    pub input_frames_used: c_long,
    pub output_frames_gen: c_long,
    pub end_of_input: c_int,
    pub src_ratio: f64,
}

pub const SRC_SINC_FASTEST: c_int = 2;

pub const SPEEX_PREPROCESS_SET_DENOISE: c_int = 0;
pub const SPEEX_PREPROCESS_SET_AGC: c_int = 2;
pub const SPEEX_PREPROCESS_SET_DEREVERB: c_int = 8;
pub const SPEEX_PREPROCESS_SET_NOISE_SUPPRESS: c_int = 18;

/// Safe owner of a reference graph processor.
#[derive(Debug)]
pub struct Graph(*mut c_void);

fn error_text(buffer: &[u8]) -> String {
    let end = buffer.iter().position(|&b| b == 0).unwrap_or(buffer.len());
    String::from_utf8_lossy(&buffer[..end]).into_owned()
}

impl Graph {
    pub fn new(graph_json: &str) -> Result<Self, String> {
        let text = std::ffi::CString::new(graph_json).expect("no interior NUL");
        let mut error = [0u8; 512];
        // SAFETY: valid C string and buffer.
        let handle = unsafe { am_oracle_create(text.as_ptr(), error.as_mut_ptr().cast(), error.len() as c_int) };
        if handle.is_null() { Err(error_text(&error)) } else { Ok(Self(handle)) }
    }

    /// Mode 0 inherits (const copy), 1 adopts, 2 adopts preserving the fade.
    pub fn update(&mut self, graph_json: &str, mode: i32) -> Result<(), String> {
        let text = std::ffi::CString::new(graph_json).expect("no interior NUL");
        let mut error = [0u8; 512];
        // SAFETY: valid handle, C string and buffer.
        let ok = unsafe { am_oracle_update(self.0, text.as_ptr(), mode, error.as_mut_ptr().cast(), error.len() as c_int) };
        if ok != 0 { Ok(()) } else { Err(error_text(&error)) }
    }

    /// `inputs[i]` holds 240 interleaved stereo frames for source node `i`.
    pub fn process(&mut self, inputs: &[Option<&[f32]>], defer: bool) {
        let mut pointers = [std::ptr::null::<f32>(); NODES];
        for (pointer, input) in pointers.iter_mut().zip(inputs) {
            if let Some(samples) = input {
                assert!(samples.len() >= QUANTUM * 2);
                *pointer = samples.as_ptr();
            }
        }
        // SAFETY: each non-null row has 480 floats; the handle is live.
        unsafe { am_oracle_process(self.0, pointers.as_ptr(), c_int::from(defer)) }
    }

    pub fn output(&self, node: usize) -> &[f32] {
        // SAFETY: the processor owns 240 stereo frames per node slot.
        unsafe { std::slice::from_raw_parts(am_oracle_output(self.0, node as c_int), QUANTUM * 2) }
    }

    pub fn meter(&self, node: usize) -> [f32; 6] {
        let mut out = [0.0f32; 6];
        // SAFETY: six-float output buffer.
        unsafe { am_oracle_meter(self.0, node as c_int, out.as_mut_ptr()) };
        out
    }

    pub fn reset(&mut self) {
        // SAFETY: live handle.
        unsafe { am_oracle_reset(self.0) }
    }

    pub fn signal_channels(&self, capture: &[i32; NODES]) -> Vec<i32> {
        let mut out = [0i32; NODES];
        // SAFETY: 32-element buffers.
        let count = unsafe { am_oracle_signal_channels(self.0, capture.as_ptr(), out.as_mut_ptr()) };
        out[..count as usize].to_vec()
    }
}

impl Drop for Graph {
    fn drop(&mut self) {
        // SAFETY: created by am_oracle_create and dropped once.
        unsafe { am_oracle_destroy(self.0) }
    }
}

/// The C++ compiler's message for a graph, or `None` when it compiles.
pub fn validate(graph_json: &str) -> Option<String> {
    let text = std::ffi::CString::new(graph_json).expect("no interior NUL");
    let mut error = [0u8; 512];
    // SAFETY: valid C string and buffer.
    unsafe { am_oracle_validate(text.as_ptr(), error.as_mut_ptr().cast(), error.len() as c_int) };
    let message = error_text(&error);
    (!message.is_empty()).then_some(message)
}
