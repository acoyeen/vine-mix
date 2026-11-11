//! What the window shows of the engine's processing-load telemetry. Load is
//! measured by the engine; nothing here guesses it from node counts. Never
//! part of a saved workspace.
//!
//! The engine measures how long each 5 ms slice of sound takes to process.
//! One number is shown: the share of that time in use, averaged over the last
//! second. It fills a bar. The busiest single slice of that second is a mark
//! on the same bar rather than a second figure, because it answers a
//! different question (how close the engine came to running out of time,
//! which a single interruption by Windows can decide) and two percentages
//! side by side read as a disagreement.

use vinemix_model::status::{ProcessingLoadState, ProcessingLoadStatus};

/// Readings older than this are shown as unavailable.
pub const STALE_AFTER_SECONDS: f64 = 2.0;

#[derive(Clone, Debug, PartialEq)]
pub struct Presentation {
    /// The reading in words: `Audio load 12%`, with `· High` or `· At risk`.
    pub label: String,
    /// A few plain sentences for the details window.
    pub details: String,
    /// `normal`, `busy`, `overloaded` or `unavailable`.
    pub state: &'static str,
    pub available: bool,
    pub addition_advised: bool,
    pub notice: String,
    /// Share of the engine's time in use over the last second, in percent.
    pub load: f64,
    /// The busiest 5 ms slice of the last second, in percent.
    pub peak: f64,
    /// Slices that finished late since the engine started.
    pub late: u64,
}

impl Presentation {
    pub fn unavailable(reason: &str) -> Self {
        Self {
            label: "Audio load —".into(),
            details: format!("{reason}\n\nThe reading returns when the audio engine is measuring again."),
            state: "unavailable",
            available: false,
            addition_advised: false,
            notice: String::new(),
            load: 0.0,
            peak: 0.0,
            late: 0,
        }
    }

    /// The reading alone: `12%`, or a dash when there is none.
    pub fn value(&self) -> String {
        if self.available { percent(self.load) } else { "—".into() }
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
            return Presentation::unavailable("There is no reading while the audio engine is disconnected or paused.");
        }
        let Some(data) = sample.filter(|s| valid(s)) else {
            self.observed = false;
            return Presentation::unavailable("The audio engine has not reported a reading yet.");
        };
        if !self.observed || engine_pid != self.engine_pid || data.sample_count != self.sample {
            self.observed = true;
            self.engine_pid = engine_pid;
            self.sample = data.sample_count;
            self.advanced_at = now;
        }
        if !now.is_finite() || now - self.advanced_at >= STALE_AFTER_SECONDS {
            return Presentation::unavailable("The reading has stopped updating.");
        }
        let (state, suffix, notice) = match data.state {
            ProcessingLoadState::Busy => ("busy", " · High", "Audio processing is busy. Adding effects or changing connections may interrupt sound."),
            ProcessingLoadState::Overloaded => ("overloaded", " · At risk", "Audio processing is under pressure. Reduce the processing workload if you hear interruptions."),
            ProcessingLoadState::Normal => ("normal", "", ""),
            ProcessingLoadState::Unknown => unreachable!("filtered by valid()"),
        };
        // A slice that took too long and one that started too late both end
        // after their deadline; the larger count covers either.
        let late = data.deadline_misses.max(data.processing_overruns);
        let recently = data.recent_deadline_misses.max(data.recent_processing_overruns);
        let mut details = format!(
            "VineMix is using {} of the time it has to process sound. The mark on the bar is the busiest moment of the last second, {}.\n\n\
             Sound can only break up when a moment takes more than 100%, so short spikes well below that are normal. This is the audio engine's own workload, not your computer's overall CPU usage.\n\n",
            percent(data.average_percent),
            percent(data.peak_percent),
        );
        details.push_str(&match (late, recently) {
            (0, _) => "No moment has run late since the audio engine started.".to_owned(),
            (1, 0) => "One moment has run late since the audio engine started. You may have heard a click or a short gap then.".to_owned(),
            (_, 0) => format!("{} moments have run late since the audio engine started. You may have heard a click or a short gap each time.", count(late)),
            _ => format!(
                "{} moments have run late since the audio engine started, {} of them in the last second. You may be hearing clicks or gaps.",
                count(late),
                count(u64::from(recently))
            ),
        });
        if data.transition_active {
            details.push_str("\n\nA connection change is being blended in right now, which briefly doubles the work.");
        } else if state != "normal" || data.transition_headroom_limited {
            details.push_str("\n\nChanging connections briefly doubles the work, so adding effects or rewiring now may interrupt sound.");
        }
        Presentation {
            label: format!("Audio load {}{suffix}", percent(data.average_percent)),
            details,
            state,
            available: true,
            addition_advised: state != "normal" || data.transition_headroom_limited,
            notice: notice.into(),
            load: data.average_percent,
            peak: data.peak_percent,
            late,
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
        assert_eq!(normal.value(), "12%");
        assert_eq!((normal.load, normal.peak, normal.late), (12.4, 17.4, 0));
        assert!(normal.available && !normal.addition_advised && normal.notice.is_empty());
        assert!(normal.details.starts_with("VineMix is using 12% of the time it has to process sound. The mark on the bar is the busiest moment of the last second, 17%."));
        assert!(normal.details.ends_with("No moment has run late since the audio engine started."));
        let busy = presenter.present(Some(&sample(11, 71.0, ProcessingLoadState::Busy)), 7, true, 0.1);
        assert_eq!(busy.label, "Audio load 71% · High");
        assert!(busy.addition_advised && busy.notice.starts_with("Audio processing is busy."));
        assert!(busy.details.ends_with("adding effects or rewiring now may interrupt sound."));
        let risk = presenter.present(Some(&sample(12, 104.0, ProcessingLoadState::Overloaded)), 7, true, 0.2);
        assert_eq!(risk.label, "Audio load 104% · At risk");
        assert_eq!(risk.state, "overloaded");
    }

    /// One number and plain words: none of the engine's vocabulary reaches
    /// the reader, and late moments are counted once however they were late.
    #[test]
    fn details_are_plain_and_count_late_moments_once() {
        let mut presenter = Presenter::default();
        let mut late = sample(10, 30.0, ProcessingLoadState::Normal);
        late.deadline_misses = 1_234;
        late.processing_overruns = 20;
        late.recent_deadline_misses = 2;
        late.recent_processing_overruns = 1;
        let shown = presenter.present(Some(&late), 7, true, 0.0);
        assert_eq!(shown.late, 1_234);
        assert!(shown.details.contains("1,234 moments have run late since the audio engine started, 2 of them in the last second."));
        let mut once = sample(11, 30.0, ProcessingLoadState::Normal);
        once.processing_overruns = 1;
        let shown = presenter.present(Some(&once), 7, true, 0.1);
        assert!(shown.details.contains("One moment has run late since the audio engine started."));
        let mut blending = sample(12, 30.0, ProcessingLoadState::Normal);
        blending.transition_active = true;
        let shown = presenter.present(Some(&blending), 7, true, 0.2);
        assert!(shown.details.ends_with("which briefly doubles the work."));
        for text in [&shown.details, &shown.label] {
            for jargon in ["overrun", "deadline", "steady", "Steady", "Routing-change", "window", "quantum"] {
                assert!(!text.contains(jargon), "{jargon:?} in {text:?}");
            }
        }
    }

    #[test]
    fn never_shows_stale_or_invalid_readings_as_healthy() {
        let mut presenter = Presenter::default();
        let offline = presenter.present(Some(&sample(10, 10.0, ProcessingLoadState::Normal)), 7, false, 0.0);
        assert!(!offline.available && offline.value() == "—" && offline.load == 0.0);
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
        assert!(stale.details.starts_with("The reading has stopped updating."));
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
