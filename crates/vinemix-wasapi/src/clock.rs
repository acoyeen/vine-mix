//! Device clock estimation and stream failure presentation. Platform-free.

/// Estimates the device clock against QPC time (100 ns units), applying a
/// slow, bounded correction to the drift controller's target.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ClockEstimate {
    position: u64,
    qpc: u64,
    anchored: bool,
    pub multiplier: f64,
}

impl Default for ClockEstimate {
    fn default() -> Self {
        Self { position: 0, qpc: 0, anchored: false, multiplier: 1.0 }
    }
}

impl ClockEstimate {
    pub fn observe(&mut self, position: u64, qpc: u64, units_per_second: f64) {
        if qpc == 0 || units_per_second <= 0.0 {
            return;
        }
        if !self.anchored || position < self.position || qpc <= self.qpc {
            self.position = position;
            self.qpc = qpc;
            self.anchored = true;
            return;
        }
        let seconds = (qpc - self.qpc) as f64 / 10_000_000.0;
        if seconds < 0.5 {
            return;
        }
        let estimate = (position - self.position) as f64 / seconds / units_per_second;
        if seconds < 3.0 && estimate > 0.995 && estimate < 1.005 {
            self.multiplier += (estimate - self.multiplier) * 0.08;
        }
        self.position = position;
        self.qpc = qpc;
    }
}

pub const E_ACCESSDENIED: i32 = 0x8007_0005_u32 as i32;
pub const E_INVALIDARG: i32 = 0x8007_0057_u32 as i32;
pub const AUDCLNT_E_DEVICE_INVALIDATED: i32 = 0x8889_0004_u32 as i32;
pub const AUDCLNT_E_DEVICE_IN_USE: i32 = 0x8889_000A_u32 as i32;
pub const AUDCLNT_E_UNSUPPORTED_FORMAT: i32 = 0x8889_0008_u32 as i32;
pub const AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED: i32 = 0x8889_000E_u32 as i32;
pub const AUDCLNT_E_SERVICE_NOT_RUNNING: i32 = 0x8889_0010_u32 as i32;
pub const AUDCLNT_E_BUFFER_SIZE_ERROR: i32 = 0x8889_0016_u32 as i32;
pub const AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED: i32 = 0x8889_0019_u32 as i32;
pub const AUDCLNT_E_INVALID_DEVICE_PERIOD: i32 = 0x8889_0020_u32 as i32;

/// The user-facing message for a stream failure.
pub fn failure_message(result: i32, exclusive: bool) -> String {
    if exclusive {
        match result {
            AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED => {
                return "Direct virtual output is disabled; allow exclusive control in the VAC Lite playback device's Windows sound properties".into();
            }
            AUDCLNT_E_DEVICE_IN_USE => {
                return "Direct virtual output is busy; close other playback apps using this VAC Lite line, then retry audio".into();
            }
            AUDCLNT_E_UNSUPPORTED_FORMAT => {
                return "VAC Lite has no supported direct output format; virtual output remains stopped to avoid shared playback capture".into();
            }
            AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED | AUDCLNT_E_BUFFER_SIZE_ERROR | AUDCLNT_E_INVALID_DEVICE_PERIOD => {
                return "VAC Lite could not open a direct output buffer; retry audio or check its driver settings".into();
            }
            E_ACCESSDENIED => {
                return "Windows denied direct virtual output access; check the VAC Lite playback device settings".into();
            }
            _ => {}
        }
    }
    match result {
        AUDCLNT_E_DEVICE_INVALIDATED => "Waiting for device".into(),
        AUDCLNT_E_DEVICE_IN_USE | AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED => "Device busy; close its exclusive-mode application".into(),
        E_ACCESSDENIED => "Microphone access blocked; check Windows microphone privacy settings".into(),
        AUDCLNT_E_SERVICE_NOT_RUNNING => "Windows Audio service unavailable".into(),
        AUDCLNT_E_UNSUPPORTED_FORMAT => "Device audio format is unsupported".into(),
        other => format!("Audio stream error 0x{:x}", other as u32),
    }
}

/// The path state a failure leaves the stream in.
pub fn failure_state(result: i32) -> &'static str {
    match result {
        AUDCLNT_E_DEVICE_IN_USE | AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED => "Busy",
        E_ACCESSDENIED => "AccessDenied",
        AUDCLNT_E_SERVICE_NOT_RUNNING => "ServiceUnavailable",
        AUDCLNT_E_UNSUPPORTED_FORMAT | E_INVALIDARG => "Error",
        _ => "Recovering",
    }
}

/// The stable reason code for a failure.
pub fn failure_reason(result: i32) -> &'static str {
    match result {
        E_ACCESSDENIED => "access_denied",
        AUDCLNT_E_SERVICE_NOT_RUNNING => "audio_service_unavailable",
        _ => "stream_failure",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn clock_converges_within_bounds() {
        let mut clock = ClockEstimate::default();
        let rate = 48_000.0;
        let actual = 1.001;
        for step in 0..40u64 {
            let seconds = step as f64;
            clock.observe((seconds * rate * actual) as u64, 1 + step * 10_000_000, rate);
        }
        assert!((clock.multiplier - actual).abs() < 1e-4, "{}", clock.multiplier);
        let mut wild = ClockEstimate::default();
        wild.observe(0, 1, rate);
        wild.observe(96_000, 10_000_001, rate);
        assert_eq!(wild.multiplier, 1.0, "implausible estimates are ignored");
    }

    #[test]
    fn failures_have_stable_presentation() {
        assert_eq!(failure_state(AUDCLNT_E_DEVICE_IN_USE), "Busy");
        assert!(failure_message(AUDCLNT_E_DEVICE_IN_USE, true).starts_with("Direct virtual output is busy"));
        assert_eq!(failure_message(0x8000_4005_u32 as i32, false), "Audio stream error 0x80004005");
        assert_eq!(failure_reason(E_ACCESSDENIED), "access_denied");
    }
}
