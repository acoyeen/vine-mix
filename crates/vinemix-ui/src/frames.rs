//! Desktop-only node groups (`GraphFrame`, `GraphFrames`, `GraphFramePalette`).
//!
//! A frame has no audio ports or processing state. Frames live in the
//! desktop preferences file and never enter the engine protocol.

use serde::{Deserialize, Serialize};

pub const MAXIMUM_NAME_LENGTH: usize = 80;
pub const MAXIMUM_FRAMES: usize = 32;

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Frame {
    pub id: String,
    pub name: String,
    pub color_key: String,
    pub node_ids: Vec<String>,
}

impl Default for Frame {
    fn default() -> Self {
        Self { id: String::new(), name: "Group".into(), color_key: "sage".into(), node_ids: Vec::new() }
    }
}

#[derive(Debug)]
pub struct FrameColor {
    pub key: &'static str,
    pub name: &'static str,
    pub accent: [u8; 3],
}

pub const PALETTE: [FrameColor; 7] = [
    FrameColor { key: "sage", name: "Sage", accent: [120, 190, 152] },
    FrameColor { key: "teal", name: "Teal", accent: [112, 183, 190] },
    FrameColor { key: "blue", name: "Blue", accent: [133, 164, 214] },
    FrameColor { key: "violet", name: "Violet", accent: [182, 157, 214] },
    FrameColor { key: "rose", name: "Rose", accent: [211, 148, 164] },
    FrameColor { key: "amber", name: "Amber", accent: [212, 180, 116] },
    FrameColor { key: "slate", name: "Slate", accent: [163, 173, 188] },
];

pub fn color(key: &str) -> &'static FrameColor {
    PALETTE.iter().find(|c| c.key.eq_ignore_ascii_case(key)).unwrap_or(&PALETTE[0])
}

pub fn normalize_key(key: &str) -> &'static str {
    color(key).key
}

/// Control characters become spaces; the result is trimmed and limited to 80
/// UTF-16 code units without splitting a surrogate pair.
pub fn normalize_name(name: &str) -> String {
    let text: String = name.chars().map(|c| if c.is_control() { ' ' } else { c }).collect();
    let text = text.trim();
    if text.is_empty() {
        return "Group".into();
    }
    let mut units = 0usize;
    let mut end = 0usize;
    for (index, character) in text.char_indices() {
        let width = character.len_utf16();
        if units + width > MAXIMUM_NAME_LENGTH {
            break;
        }
        units += width;
        end = index + character.len_utf8();
    }
    if end == text.len() {
        return text.to_owned();
    }
    text[..end].trim_end().to_owned()
}

pub fn valid_id(id: &str) -> bool {
    !id.trim().is_empty() && id.encode_utf16().count() <= 64 && !id.chars().any(char::is_control)
}

/// The first valid membership wins, which makes malformed or older
/// preferences deterministic and prevents nested groups. `None` node sets are
/// used only while preferences load before the first engine snapshot.
pub fn normalize(frames: &[Frame], node_ids: Option<&[&str]>) -> Vec<Frame> {
    let mut identities = std::collections::HashSet::new();
    let mut members = std::collections::HashSet::new();
    let mut result = Vec::new();
    for frame in frames {
        if result.len() == MAXIMUM_FRAMES {
            break;
        }
        if !valid_id(&frame.id) || !identities.insert(frame.id.clone()) {
            continue;
        }
        let mut included: Vec<String> = Vec::new();
        for id in &frame.node_ids {
            if included.len() == 32 {
                break;
            }
            if valid_id(id) && node_ids.is_none_or(|available| available.contains(&id.as_str())) && !members.contains(id) && !included.contains(id) {
                included.push(id.clone());
            }
        }
        if included.is_empty() {
            continue;
        }
        members.extend(included.iter().cloned());
        result.push(Frame {
            id: frame.id.clone(),
            name: normalize_name(&frame.name),
            color_key: normalize_key(&frame.color_key).into(),
            node_ids: included,
        });
    }
    result
}

pub fn normalize_for(frames: &[Frame], nodes: &[vinemix_model::config::GraphNode]) -> Vec<Frame> {
    let ids: Vec<&str> = nodes.iter().map(|n| n.id.as_str()).collect();
    normalize(frames, Some(&ids))
}

pub fn frame_of<'a>(frames: &'a [Frame], node_id: &str) -> Option<&'a Frame> {
    frames.iter().find(|f| f.node_ids.iter().any(|id| id == node_id))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn first_membership_wins_and_names_normalize() {
        let frames = vec![
            Frame { id: "a".into(), name: " \u{7}First ".into(), color_key: "TEAL".into(), node_ids: vec!["n1".into(), "n2".into()] },
            Frame { id: "b".into(), name: "".into(), color_key: "unknown".into(), node_ids: vec!["n2".into(), "gone".into()] },
            Frame { id: "a".into(), name: "dup".into(), color_key: "sage".into(), node_ids: vec!["n3".into()] },
        ];
        let normalized = normalize(&frames, Some(&["n1", "n2", "n3"]));
        assert_eq!(normalized.len(), 1);
        assert_eq!(normalized[0].name, "First");
        assert_eq!(normalized[0].color_key, "teal");
        let long = "x".repeat(79) + "😀";
        assert_eq!(normalize_name(&long).encode_utf16().count(), 79);
    }
}
