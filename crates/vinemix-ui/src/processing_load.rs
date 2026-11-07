//! Display text for the engine's processing-load telemetry (port of the
//! post-0.5 `ProcessingLoad.cs`, IDEA-057). Load is measured by the engine;
//! nothing here guesses it from node counts. Never part of a saved workspace.

use vinemix_model::status::{ProcessingLoadState, ProcessingLoadStatus};

/// Readings older than this are shown as unavailable.
pub const STALE_AFTER_SECONDS: f64 = 2.0;

#[derive(Clone, Debug, PartialEq)]
pub struct Presentation {
    pub label: String,
    pub details: String,
    /// `normal`, `busy`, `overloaded` or `unavailable`.
    pub state: &'static str,
    pub available: bool,
    pub addition_advised: bool,
    pub notice: String,
}

impl Presentation {
    pub fn unavailable(reason: &str) -> Self {
        Self {
            label: "Audio load —".into(),
            details: format!("{reason}\n\nThe processing window remains 5 ms. Load readings will return when fresh audio measurements are available."),
            state: "unavailable",
            available: false,
            addition_advised: false,
            notice: String::new(),
        }
    }
}

/// Tracks whether measurements are still advancing.
#[derive(Clone, Debug, Default)]
pub struct Presenter {
    engine_pid: u32,
    sample: u64,
    advanced_at: f64,
    observed: bool,
}

fn percent(value: f64) -> String {
    format!("{value:.0}%")
}

/// `N0`: thousands separators (the en-US current culture).
fn count(value: u64) -> String {
    let digits = value.to_string();
    let mut out = String::new();
    for (i, c) in digits.chars().enumerate() {
        if i > 0 && (digits.len() - i) % 3 == 0 {
            out.push(',');
        }
        out.push(c);
    }
    out
}

fn valid(value: &ProcessingLoadStatus) -> bool {
    let positive = |n: f64| n.is_finite() && n >= 0.0;
    value.available
        && value.state != ProcessingLoadState::Unknown
        && value.sample_count > 0
        && (1..=200).contains(&value.window_samples)
        && (value.budget_ms - 5.0).abs() < 0.000_001
        && positive(value.average_percent)
        && positive(value.peak_percent)
        && positive(value.steady_peak_percent)
        && positive(value.transition_peak_percent)
        && value.average_percent <= value.peak_percent + 0.000_001
        && value.recent_processing_overruns <= value.window_samples
        && value.recent_deadline_misses <= value.window_samples
        && u64::from(value.recent_processing_overruns) <= value.processing_overruns
        && u64::from(value.recent_deadline_misses) <= value.deadline_misses
}

impl Presenter {
    pub fn present(&mut self, sample: Option<&ProcessingLoadStatus>, engine_pid: u32, connected: bool, now: f64) -> Presentation {
        if !connected {
            self.observed = false;
            return Presentation::unavailable("Audio processing measurements are unavailable while the engine is disconnected or paused.");
        }
        let Some(data) = sample.filter(|s| valid(s)) else {
            self.observed = false;
            return Presentation::unavailable("The engine has not provided a valid audio processing measurement yet.");
        };
        if !self.observed || engine_pid != self.engine_pid || data.sample_count != self.sample {
            self.observed = true;
            self.engine_pid = engine_pid;
            self.sample = data.sample_count;
            self.advanced_at = now;
        }
        if !now.is_finite() || now - self.advanced_at >= STALE_AFTER_SECONDS {
            return Presentation::unavailable("Audio processing measurements have stopped updating.");
        }
        let (state, suffix, notice) = match data.state {
            ProcessingLoadState::Busy => ("busy", " · High", "Audio processing is busy. Adding effects or changing connections may interrupt sound."),
            ProcessingLoadState::Overloaded => ("overloaded", " · At risk", "Audio processing is under pressure. Reduce the processing workload if you hear interruptions."),
            ProcessingLoadState::Normal => ("normal", "", ""),
            ProcessingLoadState::Unknown => unreachable!("filtered by valid()"),
        };
        let details = format!(
            "Average load: {}\nRecent peak: {}\nSteady peak: {} · Routing-change peak: {}\nProcessing window: 5 ms\n\
             Recent processing overruns: {}\nRecent missed deadlines: {}\n\
             This engine session: {} processing overruns; {} missed deadlines.\n\n\
             Load covers the whole canvas, including routing changes. It is not overall computer CPU usage. 100% uses the full window; higher values take longer. The 5 ms window is not total audible delay.\n\n\
             Recent readings cover up to one second of processed audio. An overrun takes longer than 5 ms; a missed deadline can also include a late start. Neither proves an audible dropout.\n\n\
             {}{}Capacity is not guaranteed. Sharing a processed signal avoids repeated effects; bypass does not necessarily stop processing.",
            percent(data.average_percent),
            percent(data.peak_percent),
            percent(data.steady_peak_percent),
            percent(data.transition_peak_percent),
            count(u64::from(data.recent_processing_overruns)),
            count(u64::from(data.recent_deadline_misses)),
            count(data.processing_overruns),
            count(data.deadline_misses),
            if data.transition_active { "A routing change is processing both graphs now. " } else { "Routing changes briefly process both graphs. " },
            if data.transition_headroom_limited { "Estimated spare capacity is limited. " } else { "" },
        );
        Presentation {
            label: format!("Audio load {}{suffix}", percent(data.average_percent)),
            details,
            state,
            available: true,
            addition_advised: state != "normal" || data.transition_headroom_limited,
            notice: notice.into(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample(n: u64, average: f64, state: ProcessingLoadState) -> ProcessingLoadStatus {
        ProcessingLoadStatus { available: true, sample_count: n, window_samples: 200, average_percent: average, peak_percent: average + 5.0, steady_peak_percent: average, state, ..ProcessingLoadStatus::default() }
    }

    #[test]
    fn presents_measured_load_and_warnings() {
        let mut presenter = Presenter::default();
        let normal = presenter.present(Some(&sample(10, 12.4, ProcessingLoadState::Normal)), 7, true, 0.0);
        assert_eq!(normal.label, "Audio load 12%");
        assert!(normal.available && !normal.addition_advised && normal.notice.is_empty());
        assert!(normal.details.starts_with("Average load: 12%\nRecent peak: 17%"));
        let busy = presenter.present(Some(&sample(11, 71.0, ProcessingLoadState::Busy)), 7, true, 0.1);
        assert_eq!(busy.label, "Audio load 71% · High");
        assert!(busy.addition_advised && busy.notice.starts_with("Audio processing is busy."));
        let risk = presenter.present(Some(&sample(12, 104.0, ProcessingLoadState::Overloaded)), 7, true, 0.2);
        assert_eq!(risk.label, "Audio load 104% · At risk");
        assert_eq!(risk.state, "overloaded");
    }

    #[test]
    fn never_shows_stale_or_invalid_readings_as_healthy() {
        let mut presenter = Presenter::default();
        assert!(!presenter.present(Some(&sample(10, 10.0, ProcessingLoadState::Normal)), 7, false, 0.0).available);
        assert!(!presenter.present(None, 7, true, 0.0).available);
        let mut torn = sample(10, 10.0, ProcessingLoadState::Normal);
        torn.recent_deadline_misses = 3; // more recent misses than session misses
        assert!(!presenter.present(Some(&torn), 7, true, 0.0).available);
        let mut wrong_budget = sample(10, 10.0, ProcessingLoadState::Normal);
        wrong_budget.budget_ms = 10.0;
        assert!(!presenter.present(Some(&wrong_budget), 7, true, 0.0).available);
        assert!(presenter.present(Some(&sample(10, 10.0, ProcessingLoadState::Normal)), 7, true, 0.0).available);
        assert!(presenter.present(Some(&sample(10, 10.0, ProcessingLoadState::Normal)), 7, true, 1.9).available);
        let stale = presenter.present(Some(&sample(10, 10.0, ProcessingLoadState::Normal)), 7, true, 2.0);
        assert_eq!(stale.label, "Audio load —");
        assert!(stale.details.starts_with("Audio processing measurements have stopped updating."));
        // A restarted engine with the same counter is a fresh observation.
        assert!(presenter.present(Some(&sample(10, 10.0, ProcessingLoadState::Normal)), 8, true, 2.1).available);
    }

    #[test]
    fn counts_use_group_separators() {
        assert_eq!(count(0), "0");
        assert_eq!(count(1234), "1,234");
        assert_eq!(count(1_234_567), "1,234,567");
    }
}
