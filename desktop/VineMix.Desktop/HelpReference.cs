using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VineMix.Desktop;

internal sealed record HelpReferenceEntry(string Id, string SectionId, string Section,
    string Title, string Keys, string Description, string Keywords = "");

/// <summary>One reference catalogue shared by Help and Settings search.</summary>
internal static class HelpReference
{
    internal static IReadOnlyList<HelpReferenceEntry> Entries { get; } = Array.AsReadOnly<HelpReferenceEntry>(
    [
        new("zoom", "view", "View & zoom", "Zoom", "Mouse wheel",
            "Zoom around the pointer, including over node controls. Open menus and dropdowns keep their own scrolling.",
            "zooming magnify magnification enlarge smaller scroll wheel canvas view"),
        new("reset-zoom", "view", "View & zoom", "Reset zoom", "Ctrl+0",
            "Return the canvas to 100%. You can also click the zoom percentage.",
            "100 percent actual size default zoom scale reset view"),
        new("fit-all", "view", "View & zoom", "Fit all nodes", "Home / Fit",
            "Bring the whole graph into view. Home applies while the canvas is focused.",
            "zoom out recover lost find everything show whole graph fit view"),
        new("frame-selection", "view", "View & zoom", "Frame selected nodes", "F",
            "Bring selected nodes into view while the canvas is focused, or choose Frame selection from a selected node's menu.",
            "zoom selection focus selected frame view"),
        new("pan", "view", "View & zoom", "Pan the canvas", "Middle drag / Space+drag",
            "Move the view without moving nodes.", "navigate navigation move around canvas view hand"),
        new("right-pan", "view", "View & zoom", "Pan with the right button", "Right drag",
            "Enable Right-click drag to pan in Settings, then drag empty canvas. Release without dragging to open its menu.",
            "optional pan navigation mouse VR controller right click drag preference"),

        new("find-node", "edit", "Nodes & editing", "Find a node", "Ctrl+F",
            "Search the current workspace by name, type, assigned device or app. Choose a result to select and frame it.",
            "search locate find microphone application device devices node"),
        new("add-node", "edit", "Nodes & editing", "Add a node", "Shift+A / F3 / Add",
            "Open the searchable node library. Search a name, category or purpose.", "create insert new effect node library search"),
        new("select-nodes", "edit", "Nodes & editing", "Select nodes", "Click / Shift+click",
            "Click a node to select it; Shift-click toggles it in the selection. Drag empty canvas to box-select, holding Shift or Ctrl to add.",
            "selection multiple group box rectangle"),
        new("move-nodes", "edit", "Nodes & editing", "Move or rename nodes", "Drag / double-click header",
            "Drag a header or unused node space to move the selected node or group. Double-click the header to rename; Enter applies and Escape cancels.",
            "position arrange drag name title rename"),
        new("select-all-nodes", "edit", "Nodes & editing", "Select all nodes", "Ctrl+A",
            "Select every node while the canvas is focused.", "selection whole graph all"),
        new("copy-nodes", "edit", "Nodes & editing", "Copy nodes", "Ctrl+C",
            "Copy selected nodes and the connections between them.", "clipboard duplicate copy selection"),
        new("paste-nodes", "edit", "Nodes & editing", "Paste nodes", "Ctrl+V",
            "Create copies of the copied selection with new node identities.", "clipboard duplicate paste selection"),
        new("undo", "edit", "Nodes & editing", "Undo", "Ctrl+Z",
            "Undo the latest graph edit. A pointer drag is one step. Text fields keep their own text undo.", "history revert previous edit"),
        new("redo", "edit", "Nodes & editing", "Redo", "Ctrl+Y / Ctrl+Shift+Z",
            "Restore an undone graph edit.", "history restore redo edit"),
        new("remove-selection", "edit", "Nodes & editing", "Remove selection", "Delete",
            "Remove selected nodes or the selected cable.", "delete remove node connection cable"),
        new("cancel-gesture", "edit", "Nodes & editing", "Cancel a gesture", "Escape",
            "Cancel a canvas gesture or pending connection before it is committed.", "abort stop cancel drag connection"),

        new("align-horizontal", "align", "Alignment", "Align horizontally", "Alt+X",
            "Space selected nodes in a horizontal row, following connection order.", "align row layout tidy left right spacing"),
        new("align-vertical", "align", "Alignment", "Align vertically", "Alt+Y",
            "Space selected nodes in a vertical column, following connection order.", "align column layout tidy top bottom spacing"),
        new("auto-align", "align", "Alignment", "Auto Align", "Alt+A",
            "Choose a row or column from the selection's current layout. Alignment is also available in a selected node's right-click menu.",
            "automatic align layout tidy arrangement spacing"),

        new("connect-sockets", "connections", "Sockets & cables", "Connect sockets", "Drag / click then click",
            "Join compatible output and input sockets. Drag between them or click one, then the other.", "connect wire routing audio patch cable socket"),
        new("socket-types", "connections", "Sockets & cables", "Recognise socket types", "Round / square / diamond",
            "Round sockets carry audio; squares link levels; diamonds accept a detector-only sidechain trigger.",
            "port shape control link trigger detector ducking sidechain"),
        new("cable-actions", "connections", "Sockets & cables", "Cable actions", "Right-click cable",
            "Turn an audio connection on or off, or remove a connection.", "mute unmute enable disable cable connection menu"),
        new("move-cable", "connections", "Sockets & cables", "Move or unplug a cable", "Drag from input socket",
            "Drop on another compatible input to move a connection, or on empty canvas to unplug it. For several incoming cables, select one first.",
            "rewire disconnect unplug move remove cable connection"),
        new("cut-cables", "connections", "Sockets & cables", "Cut cables", "Ctrl+right-drag",
            "Draw across cables to remove crossed connections on release. Escape keeps the original connections.", "cut delete remove crossing wires cables"),
        new("insert-node", "connections", "Sockets & cables", "Insert a node into a cable", "Drop onto highlighted cable",
            "Drop an unconnected effect or mix node onto a highlighted audio cable to insert it into that path.", "insert effect mixing cable routing chain"),
        new("detach-node", "connections", "Sockets & cables", "Detach and reconnect", "Alt+drag",
            "Lift one node out while reconnecting its single incoming audio path to its destinations. Ambiguous rewires leave the graph unchanged.",
            "detach reconnect bypass lift node path routing"),

        new("dial-drag", "dials", "Dials & values", "Adjust a value", "Drag up / down",
            "Drag vertically. Hold Shift for fine adjustment.", "dial rotary knob fine precision shift parameter volume gain"),
        new("exact-value", "dials", "Dials & values", "Type an exact value", "Click value",
            "Type a number. Enter applies; Escape cancels. Invalid or out-of-range values stay editable. Sample counts accept whole numbers only.",
            "numeric input text number exact precision units integer samples validation"),
        new("value-tab", "dials", "Dials & values", "Apply and move", "Tab / Shift+Tab",
            "Apply a valid value and move to the next or previous control. Invalid text keeps focus so you can correct it.", "keyboard navigation focus next previous value entry"),
        new("dial-reset", "dials", "Dials & values", "Reset a dial", "Double-click / Home",
            "Restore its default value. EQ frequency resets to the active band's reset anchor.", "default reset restore dial knob parameter"),
        new("dial-small-step", "dials", "Dials & values", "Make a small adjustment", "Arrow keys",
            "Adjust the focused dial in small steps.", "keyboard dial increment decrement up down left right"),
        new("dial-large-step", "dials", "Dials & values", "Make a larger adjustment", "Page Up / Page Down",
            "Adjust the focused dial in larger steps.", "keyboard dial large increment decrement"),
        new("dial-maximum", "dials", "Dials & values", "Set the maximum value", "End",
            "Set the focused dial to the top of its range.", "maximum max dial range limit"),

        new("eq-add-band", "eq", "Equalizer", "Add a band", "Double-click empty plot / Insert",
            "Create a bell band, up to eight bands. Insert adds one at 1 kHz and 0 dB.", "equalizer eq frequency bell add point band"),
        new("eq-select-bands", "eq", "Equalizer", "Select multiple bands", "Ctrl+click",
            "Toggle a point in the selection.", "equalizer eq multiple points bands selection"),
        new("eq-box-select", "eq", "Equalizer", "Box-select bands", "Drag empty plot",
            "Drag a selection box. Hold Ctrl to add to the current selection; Escape cancels the box.", "equalizer eq selection rectangle group points"),
        new("eq-select-all", "eq", "Equalizer", "Select all bands", "Ctrl+A",
            "Select every band while the EQ plot is focused.", "equalizer eq all bands selection"),
        new("eq-move-bands", "eq", "Equalizer", "Move selected bands", "Drag point / Arrow keys",
            "Move the selection together. Hold Shift for finer movement. Dials below the plot edit the active band only.", "equalizer eq frequency gain move points fine precision"),
        new("eq-active-band", "eq", "Equalizer", "Choose the active band", "Ctrl+Left / Ctrl+Right",
            "Move to the previous or next band. Hold Shift to extend the selection.", "equalizer eq next previous active band focus"),
        new("eq-width", "eq", "Equalizer", "Adjust width / Q", "Page Up / Page Down",
            "Adjust the active band's width. Higher Q affects a narrower frequency range.", "equalizer eq q width resonance narrow broad band"),
        new("eq-reset-selection", "eq", "Equalizer", "Reset selected band positions", "Home",
            "Restore the selected bands' reset frequencies and set their gains to zero.", "equalizer eq reset frequency gain positions"),
        new("eq-remove-bands", "eq", "Equalizer", "Remove selected bands", "Delete",
            "Remove the selected EQ bands.", "equalizer eq delete remove points bands"),
        new("eq-reset-band", "eq", "Equalizer", "Reset one band position", "Double-click point",
            "Restore that band's frequency and gain, keeping its shape and width.", "equalizer eq reset point band frequency gain"),
        new("eq-band-actions", "eq", "Equalizer", "Band actions", "Right-click / Shift+F10",
            "Choose a band shape, enable or disable it, reset it or remove it. Right-click empty space to reset EQ to flat.",
            "equalizer eq bell shelf notch shape enabled disabled flat context menu"),

        new("menu-keyboard", "menus", "Menus & application", "Use controls by keyboard", "Tab / Shift+Tab",
            "Move focus. Space or Enter activates a focused button; arrow keys navigate choices. Alt+Down opens a dropdown.",
            "keyboard accessibility focus buttons dropdown selector menu"),
        new("menu-help", "menus", "Menus & application", "Read menu help", "Ctrl+Shift+F10",
            "Show or hide help for the focused menu item.", "tooltip hover menu explanation shortcut help"),
        new("open-help", "menus", "Menus & application", "Open Help", "F1",
            "Open Help outside text entry. Escape cancels an active example edit or open choice before closing Help.", "help shortcuts tutorial controls reference close escape"),
        new("close-window", "menus", "Menus & application", "Keep audio running", "Close window",
            "Closing the main window hides it in the tray. Double-click the tray icon to return.", "hide minimize close background tray continue audio"),
        new("quit-app", "menus", "Menus & application", "Quit VineMix", "Tray menu → Quit VineMix",
            "Quit closes the interface and stops audio processing.", "quit exit stop application audio engine tray"),
    ]);

    internal static IReadOnlyList<HelpReferenceEntry> Search(string query)
    {
        string normalized = Normalize(query ?? "");
        if (normalized.Length == 0) return Entries;
        string compactQuery = normalized.Replace(" ", "", StringComparison.Ordinal);
        string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Entries.Select((entry, index) => new
            {
                Entry = entry, Index = index,
                Title = Normalize(entry.Title), Keys = Normalize(entry.Keys),
                Text = Normalize(string.Join(' ', entry.Id, entry.SectionId, entry.Section,
                    entry.Title, entry.Keys, entry.Description, entry.Keywords))
            })
            .Where(item => words.All(word => item.Text.Contains(word, StringComparison.Ordinal)
                || item.Text.Replace(" ", "", StringComparison.Ordinal).Contains(word, StringComparison.Ordinal)))
            .OrderBy(item => item.Title.Replace(" ", "", StringComparison.Ordinal) == compactQuery ? 0
                : item.Keys.Replace(" ", "", StringComparison.Ordinal) == compactQuery ? 1
                : item.Title.StartsWith(normalized, StringComparison.Ordinal) ? 2
                : item.Title.Contains(normalized, StringComparison.Ordinal) ? 3 : 4)
            .ThenBy(item => item.Index)
            .Select(item => item.Entry).ToArray();
    }

    private static string Normalize(string value)
    {
        var text = new StringBuilder(value.Length);
        bool space = false;
        foreach (char character in value.Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (char.IsWhiteSpace(character)) { space = text.Length > 0; continue; }
            if (space) { text.Append(' '); space = false; }
            text.Append(char.ToLowerInvariant(character));
        }
        return text.ToString();
    }
}
