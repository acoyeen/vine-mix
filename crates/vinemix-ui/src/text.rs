//! Case-insensitive text helpers standing in for .NET's current-culture and
//! ordinal-ignore-case comparisons. Simple Unicode lowercase folding matches
//! them for every name the desktop compares (device, node and app names).

use std::cmp::Ordering;

fn fold(text: &str) -> String {
    text.chars().flat_map(char::to_lowercase).collect()
}

pub fn contains_ignore_case(text: &str, query: &str) -> bool {
    query.is_empty() || fold(text).contains(&fold(query))
}

pub fn eq_ignore_case(left: &str, right: &str) -> bool {
    left.len() == right.len() && left.eq_ignore_ascii_case(right) || fold(left) == fold(right)
}

pub fn compare_ignore_case(left: &str, right: &str) -> Ordering {
    fold(left).cmp(&fold(right))
}

/// Paths compare case-insensitively on Windows after normalising separators.
pub fn same_path(left: &std::path::Path, right: &std::path::Path) -> bool {
    let normal = |p: &std::path::Path| fold(&p.to_string_lossy().replace('/', "\\")).trim_end_matches('\\').to_owned();
    normal(left) == normal(right)
}

/// `0.#`-style formatting: at most `decimals` places, trailing zeros removed.
pub fn trim_number(value: f64, decimals: usize) -> String {
    let text = format!("{value:.decimals$}");
    let text = if text.contains('.') { text.trim_end_matches('0').trim_end_matches('.').to_owned() } else { text };
    if text == "-0" { "0".into() } else { text }
}

/// `N0` formatting: thousands separators, no decimals.
pub fn thousands(value: u64) -> String {
    let digits = value.to_string();
    let mut out = String::with_capacity(digits.len() + digits.len() / 3);
    for (index, digit) in digits.chars().enumerate() {
        if index > 0 && (digits.len() - index) % 3 == 0 {
            out.push(',');
        }
        out.push(digit);
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn helpers() {
        assert!(contains_ignore_case("Listening Output", "OUT"));
        assert!(eq_ignore_case("ÄBC", "äbc"));
        assert_eq!(trim_number(12.50, 1), "12.5");
        assert_eq!(trim_number(-0.01, 1), "0");
        assert_eq!(thousands(1234567), "1,234,567");
        assert!(same_path(std::path::Path::new("C:/A/b"), std::path::Path::new("c:\\a\\B\\")));
    }
}
