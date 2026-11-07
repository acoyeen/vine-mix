//! Update transaction records (`UpdateTransactionData`, `UpdateJournal`,
//! signals and identities), serialized exactly as the 0.5 desktop's
//! System.Text.Json options do: camelCase names, numeric enums, unknown
//! members rejected, `DateTimeOffset` strings and .NET tick timestamps.

use std::path::Path;

use serde::{Deserialize, Deserializer, Serialize, Serializer};

use super::files;
use crate::platform::Process;

/// .NET ticks between 0001-01-01 and 1601-01-01 (the FILETIME epoch).
const FILETIME_EPOCH_TICKS: i64 = 504_911_232_000_000_000;
/// .NET ticks between 0001-01-01 and 1970-01-01.
const UNIX_EPOCH_TICKS: i64 = 621_355_968_000_000_000;
const TICKS_PER_SECOND: i64 = 10_000_000;

/// `process.StartTime.ToUniversalTime().Ticks` from a creation FILETIME.
pub fn ticks_from_filetime(filetime: u64) -> i64 {
    i64::try_from(filetime).unwrap_or(i64::MAX - FILETIME_EPOCH_TICKS) + FILETIME_EPOCH_TICKS
}

/// Days from 1970-01-01 to a civil date (Howard Hinnant's algorithm).
fn days_from_civil(year: i64, month: i64, day: i64) -> i64 {
    let year = if month <= 2 { year - 1 } else { year };
    let era = year.div_euclid(400);
    let yoe = year - era * 400;
    let doy = (153 * (month + if month > 2 { -3 } else { 9 }) + 2) / 5 + day - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    era * 146_097 + doe - 719_468
}

fn civil_from_days(days: i64) -> (i64, i64, i64) {
    let z = days + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let year = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = if mp < 10 { mp + 3 } else { mp - 9 };
    (if month <= 2 { year + 1 } else { year }, month, day)
}

/// A UTC instant as .NET ticks, serialized like `DateTimeOffset.UtcNow`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord)]
pub struct UtcTime(pub i64);

impl UtcTime {
    pub fn now() -> Self {
        let since = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default();
        Self(UNIX_EPOCH_TICKS + i64::try_from(since.as_nanos() / 100).unwrap_or(0))
    }

    pub fn plus_seconds(self, seconds: i64) -> Self {
        Self(self.0 + seconds * TICKS_PER_SECOND)
    }

    pub fn format(self) -> String {
        let unix = self.0 - UNIX_EPOCH_TICKS;
        let seconds = unix.div_euclid(TICKS_PER_SECOND);
        let fraction = unix.rem_euclid(TICKS_PER_SECOND);
        let days = seconds.div_euclid(86_400);
        let second_of_day = seconds.rem_euclid(86_400);
        let (year, month, day) = civil_from_days(days);
        format!(
            "{year:04}-{month:02}-{day:02}T{:02}:{:02}:{:02}.{fraction:07}+00:00",
            second_of_day / 3600,
            second_of_day / 60 % 60,
            second_of_day % 60
        )
    }

    /// ISO 8601 with optional fraction and a `Z` or `±hh:mm` offset.
    pub fn parse(text: &str) -> Option<Self> {
        let bytes = text.as_bytes();
        let number = |range: std::ops::Range<usize>| -> Option<i64> { text.get(range)?.parse().ok() };
        if bytes.len() < 19 || bytes[4] != b'-' || bytes[7] != b'-' || bytes[10] != b'T' || bytes[13] != b':' || bytes[16] != b':' {
            return None;
        }
        let (year, month, day) = (number(0..4)?, number(5..7)?, number(8..10)?);
        let (hour, minute, second) = (number(11..13)?, number(14..16)?, number(17..19)?);
        if !(1..=12).contains(&month) || !(1..=31).contains(&day) || hour > 23 || minute > 59 || second > 59 {
            return None;
        }
        let mut index = 19;
        let mut fraction = 0i64;
        if bytes.get(index) == Some(&b'.') {
            index += 1;
            let start = index;
            while bytes.get(index).is_some_and(u8::is_ascii_digit) {
                index += 1;
            }
            let digits = &text[start..index];
            if digits.is_empty() || digits.len() > 7 {
                return None;
            }
            fraction = digits.parse::<i64>().ok()? * 10i64.pow(7 - digits.len() as u32);
        }
        let offset_minutes = match &text[index..] {
            "Z" => 0,
            rest if rest.len() == 6 && (rest.starts_with('+') || rest.starts_with('-')) && rest.as_bytes()[3] == b':' => {
                let sign = if rest.starts_with('-') { -1 } else { 1 };
                sign * (rest[1..3].parse::<i64>().ok()? * 60 + rest[4..6].parse::<i64>().ok()?)
            }
            _ => return None,
        };
        let seconds = days_from_civil(year, month, day) * 86_400 + hour * 3600 + minute * 60 + second - offset_minutes * 60;
        Some(Self(UNIX_EPOCH_TICKS + seconds * TICKS_PER_SECOND + fraction))
    }
}

impl Serialize for UtcTime {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        serializer.serialize_str(&self.format())
    }
}

impl<'de> Deserialize<'de> for UtcTime {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let text = String::deserialize(deserializer)?;
        Self::parse(&text).ok_or_else(|| serde::de::Error::custom("invalid date"))
    }
}

/// `UpdateProcessIdentity`: PID, start time, image path and its digest.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ProcessIdentity {
    pub process_id: i32,
    pub start_time_utc_ticks: i64,
    pub image_path: String,
    pub sha256: String,
}

impl ProcessIdentity {
    /// Captures a running process (`UpdateProcessIdentity.Capture`).
    pub fn capture(pid: u32) -> files::Result<Self> {
        let process = Process::open(pid).ok_or("The process exited before its identity could be captured.")?;
        Self::of(&process).unwrap_or_else(|| Err("The process exited before its identity could be captured.".into()))
    }

    pub fn of(process: &Process) -> Option<files::Result<Self>> {
        if process.has_exited() {
            return None;
        }
        let path = files::full(&process.image_path()?);
        let started = process.start_time()?;
        Some(files::hash(&path).map(|sha256| Self {
            process_id: i32::try_from(process.id()).unwrap_or(i32::MAX),
            start_time_utc_ticks: ticks_from_filetime(started),
            image_path: path.to_string_lossy().into_owned(),
            sha256,
        }))
    }

    pub fn current() -> files::Result<Self> {
        Self::capture(std::process::id())
    }

    /// `ValidIdentity`.
    pub fn valid(&self) -> bool {
        self.process_id > 0
            && self.start_time_utc_ticks > 0
            && self.start_time_utc_ticks <= 3_155_378_975_999_999_999
            && !self.image_path.is_empty()
            && self.image_path.chars().count() <= 1024
            && Path::new(&self.image_path).is_absolute()
            && files::equal_hash(&self.sha256, &self.sha256)
    }

    /// `WindowsUpdatePlatform.SameProcess`.
    pub fn same(&self, other: &Self) -> bool {
        self.process_id == other.process_id
            && self.start_time_utc_ticks == other.start_time_utc_ticks
            && files::same_path(Path::new(&self.image_path), Path::new(&other.image_path))
            && files::equal_hash(&self.sha256, &other.sha256)
    }

    /// The same process is still alive.
    pub fn is_live(&self) -> bool {
        u32::try_from(self.process_id).ok().and_then(|pid| Self::capture(pid).ok()).is_some_and(|current| current.same(self))
    }
}

macro_rules! numeric_enum {
    ($name:ident { $($variant:ident = $value:expr),* $(,)? }) => {
        #[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
        pub enum $name { $($variant),* }

        impl $name {
            pub fn from_number(value: i64) -> Option<Self> {
                match value { $($value => Some(Self::$variant),)* _ => None }
            }
            pub fn number(self) -> i64 {
                match self { $(Self::$variant => $value),* }
            }
        }

        impl Serialize for $name {
            fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
                serializer.serialize_i64(self.number())
            }
        }

        impl<'de> Deserialize<'de> for $name {
            fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
                let value = i64::deserialize(deserializer)?;
                Self::from_number(value).ok_or_else(|| serde::de::Error::custom("undefined phase"))
            }
        }
    };
}

numeric_enum!(Phase {
    Prepared = 0,
    Ready = 1,
    Committed = 2,
    WaitingForExit = 3,
    BackedUp = 4,
    Applying = 5,
    CandidateInstalled = 6,
    AwaitingHealth = 7,
    HealthCommitted = 8,
    Completed = 9,
    RollingBack = 10,
    RolledBack = 11,
    Cancelled = 12,
    RecoveryRequired = 13,
});

impl Phase {
    /// Completed, rolled back or cancelled history is inert.
    pub fn terminal(self) -> bool {
        matches!(self, Self::Completed | Self::RolledBack | Self::Cancelled)
    }
}

/// `UpdateTransactionData`.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct TransactionData {
    #[serde(default = "format_one")]
    pub format_version: i32,
    pub id: String,
    pub version: String,
    pub current_version: String,
    pub token: String,
    pub envelope_sha256: String,
    pub asset_file_name: String,
    pub standalone: bool,
    pub parent: ProcessIdentity,
    #[serde(default)]
    pub engine: Option<ProcessIdentity>,
    #[serde(default)]
    pub settings_path: Option<String>,
    #[serde(default)]
    pub created_utc: UtcTime,
}

fn format_one() -> i32 {
    1
}

/// `UpdateFileEntry`.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct FileEntry {
    pub path: String,
    pub size: i64,
    pub sha256: String,
}

/// `UpdateRegistryValue` (`bytes` is base64, as System.Text.Json writes `byte[]`).
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct RegistryValue {
    pub name: String,
    pub kind: i32,
    pub strings: Vec<String>,
    #[serde(serialize_with = "base64_out", deserialize_with = "base64_in")]
    pub bytes: Vec<u8>,
    pub number: i64,
}

fn base64_out<S: Serializer>(bytes: &[u8], serializer: S) -> Result<S::Ok, S::Error> {
    serializer.serialize_str(&vinemix_update::crypto::base64_encode(bytes))
}

fn base64_in<'de, D: Deserializer<'de>>(deserializer: D) -> Result<Vec<u8>, D::Error> {
    let text = String::deserialize(deserializer)?;
    if text.is_empty() {
        return Ok(Vec::new());
    }
    vinemix_update::crypto::base64_decode(&text).ok_or_else(|| serde::de::Error::custom("invalid base64"))
}

/// `UpdateRegistrySnapshot`.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct RegistrySnapshot {
    pub key: String,
    pub target_directory: String,
    pub values: Vec<RegistryValue>,
}

/// `UpdateBackup`.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Backup {
    pub files: Vec<FileEntry>,
    #[serde(default)]
    pub registry: Option<RegistrySnapshot>,
}

/// `UpdateJournal`.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Journal {
    #[serde(default = "prepared")]
    pub phase: Phase,
    #[serde(default)]
    pub resume_phase: Option<Phase>,
    #[serde(default)]
    pub attempts: i32,
    #[serde(default)]
    pub message: String,
    #[serde(default)]
    pub backup: Option<Backup>,
    #[serde(default)]
    pub candidate: Option<Backup>,
    #[serde(default)]
    pub setup_process: Option<ProcessIdentity>,
    #[serde(default)]
    pub smoke_process: Option<ProcessIdentity>,
    #[serde(default)]
    pub candidate_process: Option<ProcessIdentity>,
    #[serde(default)]
    pub candidate_engine_sha256: Option<String>,
    #[serde(default)]
    pub updated_utc: UtcTime,
}

fn prepared() -> Phase {
    Phase::Prepared
}

impl Default for Journal {
    fn default() -> Self {
        Self {
            phase: Phase::Prepared,
            resume_phase: None,
            attempts: 0,
            message: String::new(),
            backup: None,
            candidate: None,
            setup_process: None,
            smoke_process: None,
            candidate_process: None,
            candidate_engine_sha256: None,
            updated_utc: UtcTime::now(),
        }
    }
}

/// `UpdateSignal` (ready, commit, abort and health markers).
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Signal {
    pub token: String,
    pub envelope_sha256: String,
    #[serde(default)]
    pub process_id: i32,
    #[serde(default)]
    pub start_time_utc_ticks: i64,
    #[serde(default)]
    pub success: bool,
    #[serde(default)]
    pub message: String,
}

/// `UpdateWorkspaceCheckpoint`.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct WorkspaceCheckpoint {
    pub token: String,
    pub envelope_sha256: String,
    pub path: String,
    pub size: i64,
    pub sha256: String,
}

/// `UpdateEngineSignal`.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct EngineSignal {
    pub token: String,
    pub envelope_sha256: String,
    pub identity: ProcessIdentity,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dates_match_dotnet() {
        // DateTimeOffset(2026, 11, 7, 2, 3, 4, TimeSpan.Zero).AddTicks(1234567).Ticks
        let time = UtcTime::parse("2026-11-07T02:03:04.1234567+00:00").unwrap();
        assert_eq!(time.format(), "2026-11-07T02:03:04.1234567+00:00");
        assert_eq!(UtcTime::parse("2026-11-07T02:03:04Z").unwrap().0, time.0 - 1_234_567);
        assert_eq!(UtcTime::parse("2026-11-07T03:03:04.1234567+01:00").unwrap(), time);
        // 1970-01-01 is the Unix epoch in ticks.
        assert_eq!(UtcTime::parse("1970-01-01T00:00:00Z").unwrap().0, UNIX_EPOCH_TICKS);
        assert!(UtcTime::parse("2026-13-07T02:03:04Z").is_none());
    }

    #[test]
    fn journal_uses_numeric_phases_and_rejects_unknown_members() {
        let journal = Journal { phase: Phase::AwaitingHealth, attempts: 1, ..Journal::default() };
        let text = serde_json::to_string(&journal).unwrap();
        assert!(text.contains("\"phase\":7"));
        let back: Journal = serde_json::from_str(&text).unwrap();
        assert_eq!(back.phase, Phase::AwaitingHealth);
        assert!(serde_json::from_str::<Journal>("{\"phase\":7,\"extra\":1}").is_err());
        assert!(serde_json::from_str::<Journal>("{\"phase\":99}").is_err());
    }

    #[test]
    fn registry_bytes_are_base64() {
        let value = RegistryValue { name: "x".into(), kind: 3, strings: vec![], bytes: vec![1, 2, 3, 250], number: 0 };
        let text = serde_json::to_string(&value).unwrap();
        assert!(text.contains("\"bytes\":\"AQID+g==\""));
        assert_eq!(serde_json::from_str::<RegistryValue>(&text).unwrap(), value);
    }

    #[test]
    fn filetime_ticks() {
        assert_eq!(ticks_from_filetime(0), FILETIME_EPOCH_TICKS);
    }
}
