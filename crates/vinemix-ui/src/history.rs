//! Bounded, session-only graph history (`WorkspaceHistory`) and guarded
//! replayable transitions (`WorkspaceGraphRestore`).
//!
//! Camera and application preferences are not edits. A transition restores
//! only the entities it changed, so unrelated edits and the current camera
//! survive; a conflicting edit to the same node or cable refuses the whole
//! transition.

use vinemix_model::config::{AudioGraph, GraphEdge, GraphNode};

use crate::frames::{self, Frame};
use crate::graph::{self, bindings};

/// The graph and its desktop groups: the unit of history.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct GraphState {
    pub graph: AudioGraph,
    pub frames: Vec<Frame>,
}

impl GraphState {
    pub fn same_contents(&self, other: &Self) -> bool {
        self.graph.nodes == other.graph.nodes && self.graph.edges == other.graph.edges && self.frames == other.frames
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct Entry {
    pub time: std::time::SystemTime,
    pub description: String,
    pub key: String,
}

#[derive(Debug)]
struct Step {
    entry: Entry,
    before: GraphState,
    after: GraphState,
}

pub const CAPACITY: usize = 100;

#[derive(Debug, Default)]
pub struct History {
    steps: Vec<Step>,
    position: usize,
    gesture_depth: usize,
    gesture_before: Option<GraphState>,
    gesture_after: Option<GraphState>,
    gesture_description: Option<String>,
}

/// Topology and placement, never names, devices, sound controls or cable
/// level and mute.
pub fn changes_layout(before: &GraphState, after: &GraphState) -> bool {
    fn nodes(state: &GraphState) -> Vec<(&str, &str, f64, f64)> {
        let mut list: Vec<_> = state.graph.nodes.iter().map(|n| (n.id.as_str(), n.kind.as_str(), n.x, n.y)).collect();
        list.sort_by(|a, b| a.0.cmp(b.0));
        list
    }
    fn edges(state: &GraphState) -> Vec<(&str, &str, &str, &str, &str, &str)> {
        let mut list: Vec<_> = state
            .graph
            .edges
            .iter()
            .map(|e| (e.id.as_str(), e.from.as_str(), e.to.as_str(), e.kind.as_str(), e.from_port.as_str(), e.to_port.as_str()))
            .collect();
        list.sort_by(|a, b| a.0.cmp(b.0));
        list
    }
    fn sorted_frames(state: &GraphState) -> Vec<&Frame> {
        let mut list: Vec<&Frame> = state.frames.iter().collect();
        list.sort_by(|a, b| a.id.cmp(&b.id));
        list
    }
    nodes(before) != nodes(after) || edges(before) != edges(after) || sorted_frames(before) != sorted_frames(after)
}

impl History {
    pub fn has_gesture_edit(&self) -> bool {
        matches!((&self.gesture_before, &self.gesture_after), (Some(b), Some(a)) if !b.same_contents(a))
    }

    pub fn entries(&self) -> Vec<&Entry> {
        self.steps[..self.position].iter().rev().map(|s| &s.entry).collect()
    }

    pub fn can_undo(&self) -> bool {
        self.has_gesture_edit() || self.position > 0
    }

    pub fn can_redo(&self) -> bool {
        !self.has_gesture_edit() && self.position < self.steps.len()
    }

    pub fn undo_description(&self) -> String {
        if self.has_gesture_edit() {
            let (Some(before), Some(after)) = (&self.gesture_before, &self.gesture_after) else { return String::new() };
            return self.gesture_description.clone().unwrap_or_else(|| describe(before, after).description);
        }
        if self.position > 0 { self.steps[self.position - 1].entry.description.clone() } else { String::new() }
    }

    pub fn redo_description(&self) -> String {
        if self.can_redo() { self.steps[self.position].entry.description.clone() } else { String::new() }
    }

    pub fn undo_changes_layout(&self) -> bool {
        if self.has_gesture_edit() {
            return changes_layout(self.gesture_before.as_ref().unwrap(), self.gesture_after.as_ref().unwrap());
        }
        self.position > 0 && changes_layout(&self.steps[self.position - 1].before, &self.steps[self.position - 1].after)
    }

    pub fn redo_changes_layout(&self) -> bool {
        self.can_redo() && changes_layout(&self.steps[self.position].before, &self.steps[self.position].after)
    }

    pub fn begin_gesture(&mut self, description: Option<&str>) {
        if self.gesture_depth == 0 {
            self.gesture_description = description.map(str::to_owned);
        }
        self.gesture_depth += 1;
    }

    pub fn end_gesture(&mut self) {
        if self.gesture_depth == 0 {
            return;
        }
        self.gesture_depth -= 1;
        if self.gesture_depth != 0 {
            return;
        }
        self.finish_gesture();
    }

    pub fn complete_gesture(&mut self) {
        self.gesture_depth = 0;
        self.finish_gesture();
    }

    pub fn in_gesture(&self) -> bool {
        self.gesture_depth > 0
    }

    fn finish_gesture(&mut self) {
        let before = self.gesture_before.take();
        let after = self.gesture_after.take();
        let description = self.gesture_description.take();
        if let (Some(before), Some(after)) = (before, after) {
            self.add_step(before, after, description);
        }
    }

    pub fn record(&mut self, before: &GraphState, after: &GraphState) {
        if before.same_contents(after) {
            return;
        }
        if self.gesture_depth > 0 {
            if self.gesture_before.is_none() {
                self.gesture_before = Some(before.clone());
            }
            self.gesture_after = Some(after.clone());
        } else {
            self.add_step(before.clone(), after.clone(), None);
        }
    }

    fn add_step(&mut self, before: GraphState, after: GraphState, description: Option<String>) {
        if before.same_contents(&after) {
            return;
        }
        self.steps.truncate(self.position);
        let mut entry = describe(&before, &after);
        if let Some(description) = description.filter(|d| !d.trim().is_empty()) {
            entry.description = description;
        }
        self.steps.push(Step { entry, before, after });
        if self.steps.len() > CAPACITY {
            self.steps.remove(0);
        }
        self.position = self.steps.len();
    }

    pub fn clear(&mut self) {
        self.steps.clear();
        self.position = 0;
        self.gesture_depth = 0;
        self.gesture_before = None;
        self.gesture_after = None;
        self.gesture_description = None;
    }

    /// Ok(None) when there is nothing to move; Err when the transition no
    /// longer applies to the current graph.
    pub fn try_undo(&mut self, current: &GraphState) -> Result<Option<(GraphState, Restore)>, String> {
        self.try_move(current, true)
    }

    pub fn try_redo(&mut self, current: &GraphState) -> Result<Option<(GraphState, Restore)>, String> {
        self.try_move(current, false)
    }

    fn try_move(&mut self, current: &GraphState, undo: bool) -> Result<Option<(GraphState, Restore)>, String> {
        self.complete_gesture();
        if if undo { self.position == 0 } else { self.position == self.steps.len() } {
            return Ok(None);
        }
        let step = &self.steps[if undo { self.position - 1 } else { self.position }];
        let transition =
            if undo { Restore::new(&step.after, &step.before) } else { Restore::new(&step.before, &step.after) };
        let restored = transition.try_apply(current, true)?;
        if undo {
            self.position -= 1;
        } else {
            self.position += 1;
        }
        Ok(Some((restored, transition)))
    }

    pub fn is_compatible(&self, current: &GraphState) -> bool {
        if self.has_gesture_edit() {
            let (Some(before), Some(after)) = (&self.gesture_before, &self.gesture_after) else { return true };
            return Restore::new(after, before).try_apply(current, true).is_ok();
        }
        if self.position > 0 {
            let step = &self.steps[self.position - 1];
            if Restore::new(&step.after, &step.before).try_apply(current, true).is_err() {
                return false;
            }
        }
        if self.position < self.steps.len() {
            let step = &self.steps[self.position];
            if Restore::new(&step.before, &step.after).try_apply(current, true).is_err() {
                return false;
            }
        }
        true
    }
}

fn describe(before: &GraphState, after: &GraphState) -> Entry {
    let (b, a) = (&before.graph, &after.graph);
    let added: Vec<&GraphNode> = a.nodes.iter().filter(|n| b.nodes.iter().all(|old| old.id != n.id)).collect();
    let removed: Vec<&GraphNode> = b.nodes.iter().filter(|n| a.nodes.iter().all(|next| next.id != n.id)).collect();
    let changed: Vec<&GraphNode> = a.nodes.iter().filter(|n| b.nodes.iter().any(|old| old.id == n.id && old != *n)).collect();
    let cables = b.edges != a.edges;
    let (description, key);
    if let Some(first) = added.first() {
        description = if added.len() == 1 { format!("Added {}", first.name) } else { format!("Added {} nodes", added.len()) };
        key = format!("add:{}", first.id);
    } else if let Some(first) = removed.first() {
        description = if removed.len() == 1 { format!("Removed {}", first.name) } else { format!("Removed {} nodes", removed.len()) };
        key = format!("remove:{}", first.id);
    } else if cables {
        description = if a.edges.len() < b.edges.len() {
            "Removed connections".into()
        } else if a.edges.len() > b.edges.len() {
            "Connected nodes".into()
        } else {
            "Changed connections".into()
        };
        key = graph::new_id();
    } else if before.frames != after.frames {
        let added_frame = after.frames.iter().find(|f| before.frames.iter().all(|old| old.id != f.id));
        let removed_frame = before.frames.iter().find(|f| after.frames.iter().all(|next| next.id != f.id));
        let changed_frame = after.frames.iter().find(|f| before.frames.iter().any(|old| old.id == f.id && old != *f));
        let old_frame = changed_frame.and_then(|c| before.frames.iter().find(|f| f.id == c.id));
        description = if let Some(f) = added_frame {
            format!("Created group “{}”", f.name)
        } else if let Some(f) = removed_frame {
            format!("Ungrouped “{}”", f.name)
        } else if let (Some(c), Some(old)) = (changed_frame, old_frame) {
            if old.name != c.name {
                format!("Renamed group “{}”", c.name)
            } else if old.color_key != c.color_key {
                format!("Recolored group “{}”", c.name)
            } else {
                format!("Changed members of “{}”", c.name)
            }
        } else {
            "Reordered groups".into()
        };
        key = format!("frame:{}", added_frame.or(removed_frame).or(changed_frame).map(|f| f.id.as_str()).unwrap_or(""));
    } else {
        let moved = changed.iter().all(|n| {
            let Some(old) = b.nodes.iter().find(|o| o.id == n.id) else { return false };
            let mut old = old.clone();
            old.x = n.x;
            old.y = n.y;
            &old == *n
        });
        let subject = if changed.len() == 1 { changed[0].name.clone() } else { format!("{} nodes", changed.len()) };
        description = format!("{}{}", if moved { "Moved " } else { "Adjusted " }, subject);
        key = format!("{}{}", if moved { "move:" } else { "parameter:" }, changed.iter().map(|n| n.id.as_str()).collect::<Vec<_>>().join(","));
    }
    Entry { time: std::time::SystemTime::now(), description, key }
}

/// A guarded graph transition, safe to replay after the engine revision
/// changes.
#[derive(Clone, Debug)]
pub struct Restore {
    expected: GraphState,
    target: GraphState,
}

fn same_node(actual: Option<&GraphNode>, expected: Option<&GraphNode>) -> bool {
    if let (Some(actual), Some(expected)) = (actual, expected) {
        if bindings::is_virtual(&actual.kind) && !actual.device.assigned() && !expected.device.assigned() {
            // Restoring an unassigned node deliberately suppresses automatic
            // matching; that policy must not invalidate unrelated history.
            let mut normalized = actual.clone();
            normalized.auto_assign_device = expected.auto_assign_device;
            return &normalized == expected;
        }
    }
    actual == expected
}

fn union_ids<'a, T>(left: &'a [T], right: &'a [T], id: impl Fn(&T) -> &str) -> Vec<String> {
    let mut ids: Vec<String> = Vec::new();
    for item in left.iter().chain(right) {
        let value = id(item);
        if !ids.iter().any(|existing| existing == value) {
            ids.push(value.to_owned());
        }
    }
    ids
}

fn restore_list<T: Clone + PartialEq>(
    expected: &[T],
    target: &[T],
    next: &mut Vec<T>,
    id: impl Fn(&T) -> &str,
    same: impl Fn(Option<&T>, Option<&T>) -> bool,
    conflict: impl Fn(Option<&T>, Option<&T>, &str) -> String,
) -> Result<(), String> {
    for key in union_ids(expected, target, &id) {
        let before = expected.iter().find(|item| id(item) == key);
        let after = target.iter().find(|item| id(item) == key);
        if before == after {
            continue;
        }
        let actual = next.iter().find(|item| id(item) == key);
        if same(actual, after) {
            continue; // Already applied before a lost acknowledgement.
        }
        if !same(actual, before) {
            return Err(conflict(before, after, &key));
        }
        let index = next.iter().position(|item| id(item) == key);
        match (after, index) {
            (None, Some(index)) => {
                next.remove(index);
            }
            (Some(after), Some(index)) => next[index] = after.clone(),
            (Some(after), None) => {
                let position = target.iter().position(|item| id(item) == key).unwrap_or(0).min(next.len());
                next.insert(position, after.clone());
            }
            (None, None) => {}
        }
    }
    Ok(())
}

impl Restore {
    pub fn new(expected: &GraphState, target: &GraphState) -> Self {
        Self { expected: expected.clone(), target: target.clone() }
    }

    pub fn try_apply(&self, current: &GraphState, restore_frames: bool) -> Result<GraphState, String> {
        let mut next = current.clone();
        restore_list(
            &self.expected.graph.nodes,
            &self.target.graph.nodes,
            &mut next.graph.nodes,
            |n| n.id.as_str(),
            same_node,
            |before, after, id| {
                let name = before.map(|n| n.name.as_str()).or(after.map(|n| n.name.as_str())).unwrap_or(id);
                format!("The node “{name}” changed since this edit.")
            },
        )?;
        restore_list(
            &self.expected.graph.edges,
            &self.target.graph.edges,
            &mut next.graph.edges,
            |e: &GraphEdge| e.id.as_str(),
            |a, b| a == b,
            |_, _, _| "A connection changed since this edit.".into(),
        )?;
        if restore_frames {
            restore_list(
                &self.expected.frames,
                &self.target.frames,
                &mut next.frames,
                |f: &Frame| f.id.as_str(),
                |a, b| a == b,
                |before, after, id| {
                    let name = before.map(|f| f.name.as_str()).or(after.map(|f| f.name.as_str())).unwrap_or(id);
                    format!("The group “{name}” changed since this edit.")
                },
            )?;
            if frames::normalize_for(&next.frames, &next.graph.nodes) != next.frames {
                return Err("A group membership changed since this edit.".into());
            }
        }
        let error = graph::validate(&next.graph);
        if !error.is_empty() {
            return Err(error);
        }
        Ok(next)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn state() -> GraphState {
        GraphState { graph: graph::fresh(), frames: Vec::new() }
    }

    #[test]
    fn undo_redo_and_conflicts() {
        let mut history = History::default();
        let before = state();
        let mut after = before.clone();
        after.graph.nodes[1].gain_db = 3.0;
        history.record(&before, &after);
        assert_eq!(history.undo_description(), "Adjusted High-pass filter");
        assert!(!history.undo_changes_layout());
        let (restored, _) = history.try_undo(&after).unwrap().unwrap();
        assert_eq!(restored.graph.nodes[1].gain_db, 0.0);
        assert!(history.can_redo());
        // A conflicting edit refuses the transition atomically.
        let mut conflicting = restored.clone();
        conflicting.graph.nodes[1].gain_db = -3.0;
        assert_eq!(history.try_redo(&conflicting).unwrap_err(), "The node “High-pass filter” changed since this edit.");
        // Unrelated edits survive.
        let mut unrelated = restored.clone();
        unrelated.graph.nodes[2].gain_db = -1.0;
        let (redone, _) = history.try_redo(&unrelated).unwrap().unwrap();
        assert_eq!(redone.graph.nodes[1].gain_db, 3.0);
        assert_eq!(redone.graph.nodes[2].gain_db, -1.0);
    }

    #[test]
    fn gestures_coalesce() {
        let mut history = History::default();
        let start = state();
        history.begin_gesture(Some("Drag"));
        let mut a = start.clone();
        a.graph.nodes[0].x = 10.0;
        history.record(&start, &a);
        let mut b = a.clone();
        b.graph.nodes[0].x = 20.0;
        history.record(&a, &b);
        assert!(history.has_gesture_edit());
        assert!(history.undo_changes_layout());
        history.end_gesture();
        assert_eq!(history.entries().len(), 1);
        assert_eq!(history.undo_description(), "Drag");
    }
}
