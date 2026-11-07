//! Semantic versions with SemVer 2.0 precedence (`SemanticVersion`).

use std::cmp::Ordering;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SemanticVersion {
    text: String,
    pub major: u32,
    pub minor: u32,
    pub patch: u32,
    prerelease: Vec<String>,
}

fn numeric(value: &str) -> bool {
    !value.is_empty() && value.bytes().all(|b| b.is_ascii_digit())
}

fn identifiers(value: &str) -> Option<Vec<String>> {
    let parts: Vec<String> = value.split('.').map(str::to_owned).collect();
    parts.iter().all(|p| !p.is_empty() && p.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-')).then_some(parts)
}

fn core_number(value: &str) -> Option<u32> {
    if !numeric(value) || (value.len() > 1 && value.starts_with('0')) {
        return None;
    }
    value.parse().ok()
}

impl SemanticVersion {
    pub fn parse(value: &str) -> Option<Self> {
        if value.is_empty() || value.len() > 128 {
            return None;
        }
        let (main, build) = match value.split_once('+') {
            Some((main, build)) => (main, Some(build)),
            None => (value, None),
        };
        if let Some(build) = build {
            identifiers(build)?;
        }
        let (core, prerelease) = match main.split_once('-') {
            Some((core, pre)) => (core, Some(pre)),
            None => (main, None),
        };
        let mut numbers = core.split('.');
        let major = core_number(numbers.next()?)?;
        let minor = core_number(numbers.next()?)?;
        let patch = core_number(numbers.next()?)?;
        if numbers.next().is_some() {
            return None;
        }
        let prerelease = match prerelease {
            Some(pre) => identifiers(pre)?,
            None => Vec::new(),
        };
        if prerelease.iter().any(|p| numeric(p) && p.len() > 1 && p.starts_with('0')) {
            return None;
        }
        Some(Self { text: value.to_owned(), major, minor, patch, prerelease })
    }

    pub fn is_prerelease(&self) -> bool {
        !self.prerelease.is_empty()
    }
}

impl std::fmt::Display for SemanticVersion {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.text)
    }
}

impl PartialOrd for SemanticVersion {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for SemanticVersion {
    fn cmp(&self, other: &Self) -> Ordering {
        let core = (self.major, self.minor, self.patch).cmp(&(other.major, other.minor, other.patch));
        if core != Ordering::Equal {
            return core;
        }
        match (self.is_prerelease(), other.is_prerelease()) {
            (false, false) => return Ordering::Equal,
            (true, false) => return Ordering::Less,
            (false, true) => return Ordering::Greater,
            (true, true) => {}
        }
        for (left, right) in self.prerelease.iter().zip(&other.prerelease) {
            let (ln, rn) = (numeric(left), numeric(right));
            let compared = match (ln, rn) {
                (true, true) => left.len().cmp(&right.len()).then_with(|| left.cmp(right)),
                (true, false) => Ordering::Less,
                (false, true) => Ordering::Greater,
                (false, false) => left.cmp(right),
            };
            if compared != Ordering::Equal {
                return compared;
            }
        }
        self.prerelease.len().cmp(&other.prerelease.len())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn precedence() {
        let v = |s| SemanticVersion::parse(s).unwrap();
        assert!(v("0.5.0-beta") > v("0.4.0-beta"));
        assert!(v("0.3.0-beta.10") > v("0.3.0-beta.9"));
        assert!(v("1.0.0") > v("1.0.0-rc.1"));
        assert!(v("0.3.0-beta.5") > v("0.3.0-beta"));
        assert!(SemanticVersion::parse("01.0.0").is_none());
        assert!(SemanticVersion::parse("1.0.0-01").is_none());
        assert!(SemanticVersion::parse("1.0").is_none());
        assert_eq!(v("1.2.3+build.5").cmp(&v("1.2.3")), Ordering::Equal);
    }
}
