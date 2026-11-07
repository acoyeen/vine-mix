//! Exposes release.json to the crate as environment variables.

use std::path::PathBuf;

fn field(text: &str, key: &str) -> String {
    let pattern = format!("\"{key}\"");
    let start = text.find(&pattern).unwrap_or_else(|| panic!("release.json lacks {key}"));
    let rest = &text[start + pattern.len()..];
    let open = rest.find('"').expect("string value") + 1;
    let close = rest[open..].find('"').expect("closing quote") + open;
    rest[open..close].to_owned()
}

fn main() {
    let root = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap()).join("../..");
    let path = root.join("release.json");
    println!("cargo::rerun-if-changed={}", path.display());
    let text = std::fs::read_to_string(&path).expect("release.json");
    // The C# build writes UTF-8 with a BOM; the display version contains a
    // middle dot, so read it as UTF-8 text.
    let text = text.trim_start_matches('\u{feff}');
    println!("cargo::rustc-env=VINEMIX_RELEASE_VERSION={}", field(text, "version"));
    println!("cargo::rustc-env=VINEMIX_NUMERIC_VERSION={}", field(text, "numericVersion"));
    println!("cargo::rustc-env=VINEMIX_DISPLAY_VERSION={}", field(text, "displayVersion"));
}
