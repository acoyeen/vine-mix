//! The shortcut and control reference (`HelpReference`) and its search.



#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Entry {
    pub id: &'static str,
    pub section_id: &'static str,
    pub section: &'static str,
    pub title: &'static str,
    pub keys: &'static str,
    pub description: &'static str,
    pub keywords: &'static str,
}

const fn e(
    id: &'static str,
    section_id: &'static str,
    section: &'static str,
    title: &'static str,
    keys: &'static str,
    description: &'static str,
    keywords: &'static str,
) -> Entry {
    Entry { id, section_id, section, title, keys, description, keywords }
}

pub const ENTRIES: &[Entry] = &[
    e("zoom", "view", "View & zoom", "Zoom", "Mouse wheel", "Zoom around the pointer, including over node controls. Open menus and dropdowns keep their own scrolling.", "zooming magnify magnification enlarge smaller scroll wheel canvas view"),
    e("keyboard-zoom", "view", "View & zoom", "Zoom with the keyboard", "Ctrl+plus / Ctrl+minus", "Zoom around the centre of the canvas. Ctrl+= also zooms in; the number pad's plus and minus work too. Focused text fields and sound controls keep their own shortcuts.", "keyboard zoom in out magnify plus minus equals numpad canvas view"),
    e("reset-zoom", "view", "View & zoom", "Reset zoom", "Ctrl+0", "Return the canvas to 100%. You can also click the zoom percentage.", "100 percent actual size default zoom scale reset view"),
    e("fit-all", "view", "View & zoom", "Fit all nodes", "Home / Fit", "Bring the whole graph into view. Home applies while the canvas is focused.", "zoom out recover lost find everything show whole graph fit view"),
    e("frame-selection", "view", "View & zoom", "Frame selected nodes", "F", "Bring selected nodes into view while the canvas is focused, or choose Frame selection from a selected node's menu.", "zoom selection focus selected frame view"),
    e("pan", "view", "View & zoom", "Pan the canvas", "Middle drag / Space+drag", "Move the view without moving nodes.", "navigate navigation move around canvas view hand"),
    e("pipeline-navigation", "view", "View & zoom", "Follow an audio pipeline", "Ctrl+Tab / Ctrl+Shift+Tab", "Follow audio connections forward or backward, including muted cables. Choose a named route at a branch; reversing follows the connection just used. The next node is brought into view before its controls receive focus. Correct or cancel an unfinished value first. Tab and Shift+Tab keep moving between controls.", "keyboard node next previous branch pipeline navigation focus"),
    e("canvas-arrows", "view", "View & zoom", "Nudge nodes or pan", "Arrow keys / Shift+arrows", "With canvas focus, arrows move selected nodes by 10 units; hold Shift for 1 unit. A held key makes one Undo step. With no nodes selected, arrows pan by 40 screen units. Locked nodes stay in place. Controls keep their usual arrow behaviour.", "keyboard move nudge position fine pan arrows layout lock"),
    e("right-pan", "view", "View & zoom", "Pan with the right button", "Right drag", "Enable Right-click drag to pan in Settings, then drag empty canvas. Release without dragging to open its menu.", "optional pan navigation mouse VR controller right click drag preference"),
    e("find-node", "edit", "Nodes & editing", "Find a node", "Ctrl+F", "Search the current workspace by name, type, assigned device or app. Choose a result to select and frame it.", "search locate find microphone application device devices node"),
    e("add-node", "edit", "Nodes & editing", "Add a node", "Shift+A / F3 / Add", "Open the searchable node library. Effects keeps tone, dynamics, noise and creative processors together in one list. Search a name, category or purpose.", "create insert new effect node library search"),
    e("select-nodes", "edit", "Nodes & editing", "Select nodes", "Click / Shift+click", "Click a node to select it; Shift-click toggles it in the selection. Drag empty canvas to box-select, holding Shift or Ctrl to add.", "selection multiple group box rectangle"),
    e("move-nodes", "edit", "Nodes & editing", "Move or rename nodes", "Drag / double-click header", "Drag a header or unused node space to move the selected node or group. Double-click the header to rename; Enter applies and Escape cancels.", "position arrange drag name title rename"),
    e("rename-selection", "edit", "Nodes & editing", "Rename one selected item", "F2", "Rename exactly one selected node or group. Multiple selections and an empty selection do nothing. Enter applies the name and Escape cancels.", "node group rename title keyboard f2"),
    e("select-all-nodes", "edit", "Nodes & editing", "Select all nodes", "Ctrl+A", "Select every node while the canvas is focused.", "selection whole graph all"),
    e("named-groups", "edit", "Nodes & editing", "Group selected nodes", "Ctrl+G / right-click", "Create a named visual frame around selected nodes. It resizes with its members and has no audio inputs or outputs. Drag its exposed background to move every member.", "organize organisation organization frame group section colored colour color"),
    e("edit-group", "edit", "Nodes & editing", "Rename or color a group", "Double-click heading / F2", "Double-click a group heading to rename it, or press F2 with that group selected. Right-click its background to choose a named preset color. Enter applies a name and Escape cancels.", "frame title label palette colour color rename"),
    e("group-members", "edit", "Nodes & editing", "Change group membership", "Right-click nodes", "Choose Add to group or Remove from group. Moving a member keeps it grouped and resizes the frame. Each node belongs to at most one group; groups do not nest.", "frame member membership organize add remove"),
    e("ungroup", "edit", "Nodes & editing", "Ungroup and keep nodes", "Right-click group / Delete", "Choose Ungroup from the group menu, or select its background and press Delete. All member nodes and their connections remain.", "remove delete frame group keep nodes"),
    e("copy-nodes", "edit", "Nodes & editing", "Copy nodes", "Ctrl+C", "Copy selected nodes and the connections between them. Groups whose members are all selected keep their names and colors in the copy.", "clipboard duplicate copy selection"),
    e("paste-nodes", "edit", "Nodes & editing", "Paste nodes", "Ctrl+V", "Create copies of the copied selection with new node identities.", "clipboard duplicate paste selection"),
    e("undo", "edit", "Nodes & editing", "Undo", "Ctrl+Z", "Undo the latest graph edit. A pointer drag is one step. Text fields keep their own text undo.", "history revert previous edit"),
    e("redo", "edit", "Nodes & editing", "Redo", "Ctrl+Y / Ctrl+Shift+Z", "Restore an undone graph edit.", "history restore redo edit"),
    e("remove-selection", "edit", "Nodes & editing", "Remove selection", "Delete", "Remove selected nodes or the selected cable.", "delete remove node connection cable"),
    e("cancel-gesture", "edit", "Nodes & editing", "Cancel a gesture", "Escape", "Cancel a pointer gesture or pending connection before it is committed.", "abort stop cancel drag connection"),
    e("align-horizontal", "align", "Alignment", "Align horizontally", "Alt+X", "Space selected nodes in a horizontal row, following connection order.", "align row layout tidy left right spacing"),
    e("align-vertical", "align", "Alignment", "Align vertically", "Alt+Y", "Space selected nodes in a vertical column, following connection order.", "align column layout tidy top bottom spacing"),
    e("auto-align", "align", "Alignment", "Auto Align", "Alt+A", "Choose a row or column from the selection's current layout. Alignment is also available in a selected node's right-click menu.", "automatic align layout tidy arrangement spacing"),
    e("connect-sockets", "connections", "Sockets & cables", "Connect sockets", "Drag / click then click", "Join compatible output and input sockets. Drag between them or click one, then the other.", "connect wire routing audio patch cable socket"),
    e("socket-types", "connections", "Sockets & cables", "Recognise socket types", "Round / square / diamond", "Round sockets carry audio; squares link Level knobs; diamonds accept a detector-only sidechain trigger.", "port shape control link trigger detector ducking sidechain"),
    e("cable-actions", "connections", "Sockets & cables", "Cable actions", "Right-click cable", "Turn an audio connection on or off, or remove a connection.", "mute unmute enable disable cable connection menu"),
    e("move-cable", "connections", "Sockets & cables", "Move or unplug a cable", "Drag from input socket", "Drop on another compatible input to move a connection, or on empty canvas to unplug it. For several incoming cables, select one first.", "rewire disconnect unplug move remove cable connection"),
    e("cut-cables", "connections", "Sockets & cables", "Cut cables", "Ctrl+right-drag", "Draw across cables to remove crossed connections on release. Escape keeps the original connections.", "cut delete remove crossing wires cables"),
    e("insert-node", "connections", "Sockets & cables", "Insert a node into a cable", "Drop onto highlighted cable", "Drop an unconnected effect or mix node onto a highlighted audio cable to insert it into that path.", "insert effect mixing cable routing chain"),
    e("detach-node", "connections", "Sockets & cables", "Detach and reconnect", "Alt+drag", "Lift one node out while reconnecting its single incoming audio path to its destinations. Ambiguous rewires leave the graph unchanged.", "detach reconnect bypass lift node path routing"),
    e("dial-drag", "dials", "Dials & values", "Adjust a value", "Drag up / down", "Drag vertically. Hold Shift for fine adjustment.", "dial rotary knob fine precision shift parameter volume gain"),
    e("exact-value", "dials", "Dials & values", "Type an exact value", "Click value / type on a focused dial", "Click the value, or start typing a number while its dial is focused. Enter or clicking away applies a valid value; Escape cancels. Right-click for Apply value or Cancel edit. Invalid or out-of-range text stays visible and is not applied. Sample and repeat counts accept whole numbers only.", "numeric input text number exact precision units integer samples repeats focused keyboard validation"),
    e("value-tab", "dials", "Dials & values", "Apply and move", "Tab / Shift+Tab", "Apply a valid value and move to the next or previous control. Invalid text keeps focus so you can correct it.", "keyboard navigation focus next previous value entry"),
    e("dial-reset", "dials", "Dials & values", "Reset a dial", "Double-click / Home", "Restore its default value. EQ frequency resets to the active band's reset anchor.", "default reset restore dial knob parameter"),
    e("dial-small-step", "dials", "Dials & values", "Make a small adjustment", "Arrow keys", "Adjust the focused dial in small steps.", "keyboard dial increment decrement up down left right"),
    e("dial-large-step", "dials", "Dials & values", "Make a larger adjustment", "Page Up / Page Down", "Adjust the focused dial in larger steps.", "keyboard dial large increment decrement"),
    e("dial-maximum", "dials", "Dials & values", "Set the maximum value", "End", "Set the focused dial to the top of its range.", "maximum max dial range limit"),
    e("eq-add-band", "eq", "Equalizer", "Add a band", "Double-click empty plot / Insert", "Create a bell band, up to eight bands. Insert adds one at 1 kHz and 0 dB.", "equalizer eq frequency bell add point band"),
    e("eq-select-bands", "eq", "Equalizer", "Select multiple bands", "Ctrl+click", "Toggle a point in the selection.", "equalizer eq multiple points bands selection"),
    e("eq-box-select", "eq", "Equalizer", "Box-select bands", "Drag empty plot", "Drag a selection box. Hold Ctrl to add to the current selection; Escape cancels the box.", "equalizer eq selection rectangle group points"),
    e("eq-select-all", "eq", "Equalizer", "Select all bands", "Ctrl+A", "Select every band while the EQ plot is focused.", "equalizer eq all bands selection"),
    e("eq-move-bands", "eq", "Equalizer", "Move selected bands", "Drag point / Arrow keys", "Move the selection together. Hold Shift for finer movement. Dials below the plot edit the active band only.", "equalizer eq frequency gain move points fine precision"),
    e("eq-active-band", "eq", "Equalizer", "Choose the active band", "Ctrl+Left / Ctrl+Right", "Move to the previous or next band. Hold Shift to extend the selection.", "equalizer eq next previous active band focus"),
    e("eq-width", "eq", "Equalizer", "Adjust width / Q", "Page Up / Page Down", "Adjust the active band's width. Higher Q affects a narrower frequency range.", "equalizer eq q width resonance narrow broad band"),
    e("eq-reset-selection", "eq", "Equalizer", "Reset selected band positions", "Home", "Restore the selected bands' reset frequencies and set their gains to zero.", "equalizer eq reset frequency gain positions"),
    e("eq-remove-bands", "eq", "Equalizer", "Remove selected bands", "Delete", "Remove the selected EQ bands.", "equalizer eq delete remove points bands"),
    e("eq-reset-band", "eq", "Equalizer", "Reset one band position", "Double-click point", "Restore that band's frequency and gain, keeping its shape and width.", "equalizer eq reset point band frequency gain"),
    e("eq-band-actions", "eq", "Equalizer", "Band actions", "Right-click / Shift+F10", "Choose a band shape, enable or disable it, reset it or remove it. Right-click empty space to reset EQ to flat.", "equalizer eq bell shelf notch shape enabled disabled flat context menu"),
    e("menu-keyboard", "menus", "Menus & application", "Use controls by keyboard", "Tab / Shift+Tab", "Move focus. Space or Enter activates a focused button; arrow keys navigate choices. Alt+Down opens a dropdown.", "keyboard accessibility focus buttons dropdown selector menu"),
    e("menu-help", "menus", "Menus & application", "Read help by keyboard", "Ctrl+Shift+F10", "Show or hide the help for the focused control or menu item. Help also appears shortly after you move to a control with Tab or the arrow keys.", "tooltip hover menu explanation shortcut help keyboard focus"),
    e("open-help", "menus", "Menus & application", "Open Help", "F1", "Open Help outside text entry. Escape cancels an active example edit or open choice before closing Help.", "help shortcuts tutorial controls reference close escape"),
    e("close-window", "menus", "Menus & application", "Keep audio running", "Close window", "Closing the main window hides it in the tray. Double-click the tray icon to return.", "hide minimize close background tray continue audio"),
    e("quit-app", "menus", "Menus & application", "Quit VineMix", "Tray menu → Quit VineMix", "Quit closes the interface and stops audio processing.", "quit exit stop application audio engine tray"),
    // Detail that hover help deliberately leaves out (TWEAK-018): hover text
    // names a control and its purpose; how to use it and its finer points
    // are here, where the Settings search also finds them.
    e("minimap", "view", "View & zoom", "Use the minimap", "Click / drag the minimap", "The minimap in the bottom-right corner shows the whole canvas with the visible part outlined. Click it to centre the view there, or drag to pan. Show or hide it with Show minimap in Settings.", "minimap overview navigate canvas map pan centre"),
    e("settings-search", "menus", "Menus & application", "Search settings and help", "Type in the Settings menu", "Find a setting or the instructions for a control. Try zoom, devices or dials. Up and Down choose a help result; Enter opens its full instructions. Escape clears your search; Escape again closes the menu.", "settings search find filter help results"),
    e("close-dialog", "menus", "Menus & application", "Close a window", "Escape / Close", "Close a dialog with its Close button, by pressing Escape, or by clicking the workspace behind it.", "dialog window close dismiss escape"),
    e("device-lists", "menus", "Menus & application", "Choose an audio device", "Device list on a node", "A device list shows the Windows devices that can be used in that node. Disabled, disconnected and advanced devices appear when enabled under Settings → Audio device visibility. An unavailable saved device stays assigned and reconnects when Windows reports the same device again; VineMix never switches to another device because it has the same name.", "device input output microphone speakers list picker missing unavailable visibility"),
    e("app-capture", "menus", "Menus & application", "Capture an app", "App list on a Running app node", "App capture takes a copy of one running app's audio; the app keeps playing as before. If the app and VineMix play through the same speakers you hear that app twice.", "application capture app audio loopback doubled"),
    e("trigger-sockets", "connections", "Sockets & cables", "Use a trigger", "Diamond socket", "A trigger is detector-only audio: it controls a Gate, Expander or Ducking node and is never heard. With no trigger cable a Gate or Expander detects its own input. A connected trigger whose source is silent or unavailable is a silent trigger; the node does not fall back to its own input.", "trigger sidechain detector gate expander ducking silent"),
    e("cable-details", "connections", "Sockets & cables", "Read a cable", "Hover / right-click cable", "Hovering a cable shows its route, and whether it is off, a linked control or a trigger. A linked control scales the linked level and carries no audio. The cable's menu turns an audio connection on or off, removes it, and shows any level saved by an older version.", "cable connection route linked control trigger muted off saved level"),
    e("voice-color-pad", "dials", "Dials & values", "Shape Voice Color", "Drag the pad / arrow keys", "Drag the handle: left to right moves Tone from warm to bright, and upward moves Character from natural to broadcast. Hold Shift for fine movement. Arrow keys nudge the handle and Home or a double-click returns to Neutral at the bottom centre. The Tone and Character dials set the same two values exactly.", "voice color colour pad tone character warm bright natural broadcast neutral"),
    e("peak-meters", "dials", "Dials & values", "Read a peak meter", "Hover a meter", "A node's meter shows its current level. Hovering it shows the highest sample level observed for each channel, and whether a level over 0 dBFS was detected.", "meter peak level clip overload dbfs"),
    e("dynamics-status", "dials", "Dials & values", "Read gain reduction", "Status line on dynamics nodes", "Compressor, Limiter, Ducking, Expander, Gate, De-esser and Plosive remover show their detector (or trigger) level beside the status line. The strip beneath spans 0–24 dB of reduction. Hovering the status line shows the detector level against the threshold or ceiling.", "gain reduction detector threshold ceiling compressor limiter gate strip"),
    e("eq-display", "eq", "Equalizer", "Read the EQ display", "Line and shaded spectrum", "The line is the calculated EQ response on the left dB scale. The shaded spectrum is the measured output of that EQ on the right dBFS scale. The Equalizer practice example in Help calculates the response only: it measures and changes no audio.", "equalizer eq spectrum analyzer response curve measured practice"),
    e("node-reverb", "nodes", "Nodes & effects", "Reverb", "Effects", "Adds a room-like tail. The Audio output blends dry sound and reverb with Mix; the Wet output carries the reverb alone, independent of Mix, in stereo. Bypass passes the dry sound on Audio while Wet fades to silence; the reverb tail keeps advancing while bypassed.", "reverb wet dry mix decay damping pre-delay bypass tail room"),
    e("node-echo", "nodes", "Nodes & effects", "Echo", "Effects", "Adds 1–8 fading echoes. Time sets the gap before and between echoes: very short times, such as 20 ms, thicken the sound; longer times separate the repeats. Each repeat is half as strong as the one before. Mix blends original sound and echoes on Audio (0% original only, 100% echoes only); the Echo output always carries the echoes alone, and can be routed to Reverb. Changing Time or Repeats smoothly restarts the echoes.", "echo delay repeats feedback time mix wet"),
    e("node-distortion", "nodes", "Nodes & effects", "Distortion", "Effects", "Drive sets the strength, adding harmonics and loudness. Tone softens high frequencies in the distorted sound. Mix blends original and distorted sound, and Output trims the complete blend, including at Mix 0%. The node adds 0.67 ms of delay, including at Mix 0% and when bypassed; bypass restores the original sound without the Output trim.", "distortion drive tone mix output saturation overdrive warmth delay"),
    e("node-denoiser", "nodes", "Nodes & effects", "Denoiser", "Effects", "SpeexDSP is the faster, lighter cleanup, with a Reduction amount; it adds 20 ms of delay. RNNoise usually gives cleaner speech, with a Mix between original and denoised sound; it adds 30 ms. The delay is part of the node's path, including when bypassed.", "denoiser noise speex rnnoise delay reduction mix speech"),
    e("node-dynamics", "nodes", "Nodes & effects", "Compressor and Limiter", "Effects", "A Compressor reduces its own louder passages above Threshold by Ratio, with Attack and Release times and Make-up gain to restore level. A Limiter holds stereo sample peaks under its Ceiling with no lookahead; Bypass removes limiting, and gain applied later in the path can exceed the ceiling.", "compressor limiter threshold ratio attack release makeup ceiling peaks lookahead"),
    e("node-ducking", "nodes", "Nodes & effects", "Ducking", "Effects", "Lowers this node's audio while a trigger sound is present. Static ducking lowers by a set amount while the trigger is above the threshold. Dynamic ducking lowers more as the trigger gets louder above the threshold. The trigger is detector-only and both channels are reduced together.", "ducking sidechain trigger static dynamic music voice threshold"),
    e("node-gate", "nodes", "Nodes & effects", "Expander and Gate", "Effects", "An Expander reduces quieter passages below the threshold by Ratio, down to Range. A Gate closes below the threshold: Hold keeps it open briefly, Hysteresis sets a separate, lower closing threshold and Range limits how far the signal is turned down. Both accept an optional trigger.", "expander gate threshold ratio range hold hysteresis trigger"),
    e("node-stereo", "nodes", "Nodes & effects", "Stereo tools", "Mixing", "Stereo Width: 0% is mono, 100% is the original and higher values widen existing stereo detail, which can raise peaks; mono input stays mono. Pan positions a mono signal from left (-1) through centre (0) to right (1) with equal power, -3 dB per side at the centre; stereo input is combined to mono first. Balance reduces the opposite channel and leaves the centre unchanged. Stereo Combine places its Left and Right inputs on those channels, each combined to mono, and an empty side stays silent. Stereo Split gives left and right as separate centred mono outputs.", "stereo width pan balance combine split mono channel swap"),
    e("node-crossfade", "nodes", "Nodes & effects", "Crossfade", "Mixing", "Blends stereo inputs A and B: 0% is A only, 100% is B only, and Bypass plays A only. Equal power suits different sources; Linear suits matching signals. If one path is delayed more than the other, add an Alignment Delay to the earlier one.", "crossfade blend a b equal power linear curve"),
    e("node-alignment", "nodes", "Nodes & effects", "Alignment Delay", "Effects", "Delays one audio path by a precise number of milliseconds or samples to align it with another. At 48 kHz, 48 samples are 1 ms; the delay can be up to 2 seconds. Bypass passes undelayed audio.", "alignment delay latency samples milliseconds phase"),
    e("node-voice-color", "nodes", "Nodes & effects", "Voice Color", "Effects", "Warm to Bright sets Tone, and Natural to Broadcast sets Character, adding body, clarity and controlled thickening. The bottom centre of the pad is Neutral: the original voice character.", "voice color colour tone character warm bright broadcast neutral"),
    e("node-linked-control", "nodes", "Nodes & effects", "Linked control", "Controls", "Scales the levels of connected nodes together while keeping their individual settings. 100% is unchanged and 0% is silent. Bypass leaves the linked levels unchanged.", "linked control multiplier level volume group"),
];

/// Lowercase, collapse whitespace and drop combining marks.
fn normalize(value: &str) -> String {
    let mut text = String::with_capacity(value.len());
    let mut space = false;
    for c in value.chars() {
        if ('\u{0300}'..='\u{036F}').contains(&c) {
            continue;
        }
        if c.is_whitespace() {
            space = !text.is_empty();
            continue;
        }
        if space {
            text.push(' ');
            space = false;
        }
        text.extend(c.to_lowercase());
    }
    text
}

/// `HelpReference.Search`: every word must match; exact title, exact keys,
/// title prefix and title substring rank first.
pub fn search(query: &str) -> Vec<&'static Entry> {
    let normalized = normalize(query);
    if normalized.is_empty() {
        return ENTRIES.iter().collect();
    }
    let compact = normalized.replace(' ', "");
    let words: Vec<&str> = normalized.split(' ').filter(|w| !w.is_empty()).collect();
    let mut results: Vec<(usize, usize, &Entry)> = ENTRIES
        .iter()
        .enumerate()
        .filter_map(|(index, entry)| {
            let text = normalize(&[entry.id, entry.section_id, entry.section, entry.title, entry.keys, entry.description, entry.keywords].join(" "));
            let packed = text.replace(' ', "");
            if !words.iter().all(|w| text.contains(w) || packed.contains(w)) {
                return None;
            }
            let title = normalize(entry.title);
            let keys = normalize(entry.keys);
            let rank = if title.replace(' ', "") == compact {
                0
            } else if keys.replace(' ', "") == compact {
                1
            } else if title.starts_with(&normalized) {
                2
            } else if title.contains(&normalized) {
                3
            } else {
                4
            };
            Some((rank, index, entry))
        })
        .collect();
    results.sort_by_key(|(rank, index, _)| (*rank, *index));
    results.into_iter().map(|(_, _, e)| e).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn search_ranks_titles_first() {
        assert_eq!(search("undo")[0].id, "undo");
        assert_eq!(search("ctrl+0")[0].id, "reset-zoom");
        assert!(search("eq band").iter().all(|e| e.section_id == "eq" || e.description.to_lowercase().contains("band")));
        assert_eq!(search("").len(), ENTRIES.len());
    }

    /// The detail that hover help no longer carries is findable in Help.
    #[test]
    fn details_removed_from_hover_help_are_in_help() {
        for (query, entry) in [
            ("minimap", "minimap"),
            ("drag vertically", "dial-drag"),
            ("page up", "dial-large-step"),
            ("escape cancels", "exact-value"),
            ("reset default", "dial-reset"),
            ("silent trigger", "trigger-sockets"),
            ("linked control cable", "cable-details"),
            ("reverb tail bypass", "node-reverb"),
            ("echo repeats restart", "node-echo"),
            ("distortion delay", "node-distortion"),
            ("rnnoise delay", "node-denoiser"),
            ("limiter lookahead", "node-dynamics"),
            ("hysteresis", "node-gate"),
            ("equal power", "node-crossfade"),
            ("stereo width peaks", "node-stereo"),
            ("measured output", "eq-display"),
            ("escape clears", "settings-search"),
            ("click the workspace", "close-dialog"),
            ("same speakers", "app-capture"),
            ("voice color neutral", "voice-color-pad"),
        ] {
            assert!(search(query).iter().any(|e| e.id == entry), "searching {query:?} should find {entry}");
        }
        // Every entry id is unique.
        let mut ids: Vec<&str> = ENTRIES.iter().map(|e| e.id).collect();
        ids.sort_unstable();
        let count = ids.len();
        ids.dedup();
        assert_eq!(ids.len(), count);
    }
}
