//! INF identity (`InfIdentity`): only real `[Manufacturer]` model
//! declarations identify a driver package. Words in comments, friendly
//! names or AddReg values never do.

use std::collections::HashMap;

use crate::policy::{matches_hardware_id, DriverFamily};

#[derive(Debug, Default)]
pub struct InfIdentity {
    sections: HashMap<String, Vec<(String, String)>>,
    strings: HashMap<String, String>,
}

fn unquote(value: &str) -> String {
    value.trim().trim_matches('"').to_owned()
}

fn strip_comment(line: &str) -> &str {
    let mut quoted = false;
    for (index, c) in line.char_indices() {
        if c == '"' {
            quoted = !quoted;
        }
        if c == ';' && !quoted {
            return &line[..index];
        }
    }
    line
}

fn split_fields(value: &str) -> Vec<String> {
    let mut fields = Vec::new();
    let mut quoted = false;
    let mut start = 0;
    for (index, c) in value.char_indices() {
        if c == '"' {
            quoted = !quoted;
        }
        if c == ',' && !quoted {
            fields.push(value[start..index].trim().to_owned());
            start = index + 1;
        }
    }
    fields.push(value[start..].trim().to_owned());
    fields
}

impl InfIdentity {
    pub fn parse(text: &str) -> Self {
        let mut identity = Self::default();
        let mut section = String::new();
        let mut continuation = String::new();
        for physical in text.split('\n') {
            let mut line = continuation.clone() + strip_comment(physical).trim();
            if line.ends_with('\\') {
                line.pop();
                continuation = line;
                continue;
            }
            continuation.clear();
            if line.starts_with('[') && line.ends_with(']') && line.len() >= 2 {
                section = line[1..line.len() - 1].trim().to_lowercase();
                identity.sections.entry(section.clone()).or_default();
            } else if let Some(equals) = line.find('=') {
                if !section.is_empty() {
                    let entry = (line[..equals].trim().to_owned(), line[equals + 1..].trim().to_owned());
                    identity.sections.entry(section.clone()).or_default().push(entry);
                }
            }
        }
        for name in ["strings", "strings.0409"] {
            if let Some(entries) = identity.sections.get(name) {
                for (key, value) in entries {
                    identity.strings.insert(key.to_lowercase(), unquote(value));
                }
            }
        }
        identity
    }

    /// Replaces `%token%` from `[Strings]`, up to eight levels deep.
    fn expand(&self, value: &str) -> String {
        let mut value = value.to_owned();
        for _ in 0..8 {
            let mut expanded = String::with_capacity(value.len());
            let mut rest = value.as_str();
            while let Some(start) = rest.find('%') {
                let after = &rest[start + 1..];
                match after.find('%') {
                    Some(end) if end > 0 => {
                        let token = &after[..end];
                        expanded.push_str(&rest[..start]);
                        match self.strings.get(&token.to_lowercase()) {
                            Some(replacement) => expanded.push_str(replacement),
                            None => {
                                expanded.push('%');
                                expanded.push_str(token);
                                expanded.push('%');
                            }
                        }
                        rest = &after[end + 1..];
                    }
                    _ => {
                        expanded.push_str(&rest[..=start]);
                        rest = after;
                    }
                }
            }
            expanded.push_str(rest);
            if expanded == value {
                break;
            }
            value = expanded;
        }
        value
    }

    fn value(&self, section: &str, key: &str) -> String {
        self.sections
            .get(&section.to_lowercase())
            .and_then(|entries| entries.iter().find(|(k, _)| k.eq_ignore_ascii_case(key)))
            .map(|(_, v)| unquote(&self.expand(v)))
            .unwrap_or_default()
    }

    pub fn identifies(&self, family: DriverFamily) -> bool {
        let provider = match family {
            DriverFamily::VBCable => "VB-Audio Software",
            DriverFamily::Vac => "Eugene Muzychenko",
        };
        if !self.value("Version", "Class").eq_ignore_ascii_case("MEDIA") || !self.value("Version", "Provider").eq_ignore_ascii_case(provider) {
            return false;
        }
        let Some(manufacturers) = self.sections.get("manufacturer") else { return false };
        for (_, declaration) in manufacturers {
            let fields = split_fields(&self.expand(declaration));
            let Some(first) = fields.first() else { continue };
            let base = unquote(first);
            let mut models = vec![base.clone()];
            models.extend(fields.iter().skip(1).map(|decoration| format!("{base}.{}", unquote(decoration))));
            for model in models {
                let Some(entries) = self.sections.get(&model.to_lowercase()) else { continue };
                for (_, entry) in entries {
                    if split_fields(&self.expand(entry)).iter().skip(1).any(|id| matches_hardware_id(id, family)) {
                        return true;
                    }
                }
            }
        }
        false
    }

    pub fn version(&self) -> String {
        let fields = split_fields(&self.value("Version", "DriverVer"));
        fields.get(1).map(|v| unquote(v)).unwrap_or_default()
    }
}

/// Decodes an INF by its byte-order mark; legacy files are Latin-1.
pub fn read_inf_text(bytes: &[u8]) -> String {
    let utf16 = |data: &[u8], little: bool| -> String {
        let units: Vec<u16> = data.chunks_exact(2).map(|c| if little { u16::from_le_bytes([c[0], c[1]]) } else { u16::from_be_bytes([c[0], c[1]]) }).collect();
        String::from_utf16_lossy(&units)
    };
    match bytes {
        [0xff, 0xfe, rest @ ..] => utf16(rest, true),
        [0xfe, 0xff, rest @ ..] => utf16(rest, false),
        [0xef, 0xbb, 0xbf, rest @ ..] => String::from_utf8_lossy(rest).into_owned(),
        _ => bytes.iter().map(|&b| char::from(b)).collect(),
    }
}
