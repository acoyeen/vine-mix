# Blender-inspired audio node editor

Updated 18 October 2026. This implements the requested Blender node-editor direction in
the existing native Windows/WPF controller. The current update simplifies the
creation library and effect controls, gives EQ more editing space and a measured
audio underlay, and adds denoising, de-essing, plosive reduction and reverb.
Existing EQ settings, saved routes and separately installed drivers are preserved.

The current scalar-control redesign gives every adjustable number a recognisable
shared dial, compact related parameter groups and persistent operating hints.
Compressor, Limiter and Ducking are separate creation entries. Existing
compressors keep their original mode/detector/settings in a labelled legacy
interface; no automatic conversion or load-time rewrite changes their sound.

The subsequent workspace cleanup groups creation into Inputs, Outputs, Effects,
Mixing and Controls, sharing category colours with node headers. Flat toolbar and
caption controls replace framed buttons; dark menus include category search and
a read-only session History. Both footer rows are reclaimed for canvas. Node
status space is fixed so live states cannot resize a node, and grab cursors match
the full draggable area. Navigation no longer queues configuration saves; only
actual edits do, with duplicate/no-op edits ignored.

The passive title/header and unused toolbar now share the native window-drag
region. Only actual buttons opt out of caption hit testing; passive status text
does not. The caption ends exactly at the canvas, calculated from the same row
height constants as the layout and accounting for the eight-DIP resize border.
This follows Microsoft's [caption-area definition](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome.captionheight?view=windowsdesktop-10.0).

## Visual reference

The reference is Blender's default dark node editor, grounded in its
[official node anatomy](https://docs.blender.org/manual/en/latest/interface/controls/nodes/parts.html),
[default theme](https://github.com/blender/blender/blob/main/release/datafiles/userdef/userdef_default_theme.c),
[category mapping](https://github.com/blender/blender/blob/main/source/blender/editors/interface/resources.cc),
and [layout constants](https://github.com/blender/blender/blob/main/source/blender/editors/space_node/node_intern.hh).
The unchanged [manual reference image](design/blender-reference/node-parts-overview.png)
is credited in [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
WPF controls implement the appearance independently; Blender source code is not
compiled or copied into the application.

Connection gestures take inspiration from Blender's
[node editing manual](https://docs.blender.org/manual/en/latest/interface/controls/nodes/editing.html),
including dragging a link away from its input, cutting links with Ctrl-right-drag,
dragging a node over a link and detaching a node while moving it.
The [link operators](https://github.com/blender/blender/blob/main/source/blender/editors/space_node/node_relationships.cc)
clarify that detaching a whole node attempts to reconnect its neighbours; the
manual's phrase "cut all the links" does not fully describe that operation.
This mixer previews the proposed cable edit and applies it on release;
automatic reconnection is limited to an unambiguous audio path. It does not
automatically rearrange neighboring nodes. Filter response follows the biquad
formulae in the [W3C Audio EQ Cookbook](https://www.w3.org/TR/audio-eq-cookbook/),
covering bell, shelving, pass and notch shapes. The cookbook defines the DSP
response; the node controls and their arrangement are application choices.

## Implemented

- Resizable window starting at 1280 × 800 DIP, with a 900 × 580 minimum and no
  fixed maximum. The integrated caption contains minimise, maximise/restore
  and close controls; resizing exposes more canvas without rearranging nodes.
- Black canvas (#000000), near-black node bodies (#121416) and window chrome
  (#0C0D0F), a restrained dot grid (#1B1F23), contrasted control faces (#24282D)
  and text (#E3E6EA).
- Shallow coloured category headers distinguish inputs, outputs, processors,
  utilities and linked controls. Green continues to identify audio sockets and
  meters. Header colour identifies type; it is separate from text antialiasing.
  Inputs use #583747, outputs #4A2D34, processors #40365B, utilities #294650 and
  linked controls #374D3C.
- Single-line titles, permanently expanded controls, small
  corners and no whole-node shadow effect. Removing that effect avoids an
  intermediate bitmap of the text and controls before display scaling.
- Selection uses a separate two-screen-DIP outline at every zoom, without
  changing the node's layout border or proportions. Header grab margins extend
  eight screen DIPs above and six to either side; unused node space can also
  start a move. Settings, sockets and effect editors retain their own input.
- Bundled Inter 4.1 Regular/SemiBold, 13 DIP ordinary control text and 15 DIP
  node titles, with layout rounding and device-pixel alignment. Small chart
  annotations remain distinct from primary control text.
- Scaled node text uses ideal vector metrics and grayscale antialiasing. A
  uniform canvas transform scales the complete node, including titles, sockets,
  controls and meters. Node proportions remain fixed, and zoom never hides
  controls or folds details. The collapse button and H shortcut are removed;
  an old saved `Collapsed` value is retained as inert compatibility data.
  Text naturally becomes smaller at low zoom; Ctrl+0 restores 100% for editing.
  The grid uses a retained drawing tile with coarser divisions as needed at low
  zoom. Unchanged telemetry preserves the view transform, cable geometry, EQ
  drawings and dial faces; explicit layout changes update cable endpoints.
- Device state, mute, clipping and processing controls remain in their nodes.
  Healthy nodes omit repeated status text. Fit centres the measured node bounds,
  including in the minimum-size window, without changing node proportions.
- Outputs at the upper-right; inputs at the lower-left. Audio uses green sockets
  and smooth curved connections; linked controls use grey square sockets.
  Multi-input audio sockets have an elongated shape. Connections have a thin
  dark outline, and muted routes remain dashed.
- A dragged cable snaps to a nearby compatible socket. An unconnected processor
  can be dragged onto a highlighted audio cable to insert it on release. Hover
  only previews the candidate; Escape cancels without editing connections.
  Socket highlighting changes the existing port's fill, without an extra ring
  or a size change. The forgiving snap radius remains fixed in screen space.
- Dragging a connected input grabs its existing cable. Release on empty canvas
  to remove it, or another compatible input to move it with its level and mute
  intact. Ctrl-right-drag cuts cables crossed by the preview stroke. Cancellation
  leaves the original routes unchanged.
- Alt-drag or right-click **Detach node (reconnect)** removes a processor from its path. A single
  input reconnects to one or more destinations; multiple inputs are not
  guessed. Detach retains the node and its settings, and removes
  its processing, level and mute from the path. Only cable levels/mutes are
  carried into the replacement connection, so reinsertion does not apply the
  node's level twice.
- Every scalar numeric parameter uses the same rotary implementation. Compact
  effect tiles are 136 DIPs wide with an 18-DIP label, a 58-DIP dial and a
  110 × 28-DIP exact-entry field. Related parameters share aligned rows and
  group captions. The established larger Level, EQ and tone dials use the same
  control. Values and units stay visible; dimensions do not follow telemetry.
- Dials drag vertically, Shift slows movement, Enter applies exact text and
  Escape cancels. Double-click or Home resets to the parameter default; EQ
  frequency uses its band's anchor. Arrow/Page keys adjust a focused dial.
  Dial and entry focus are visible. Decimal formatting follows increments,
  unchanged entry preserves stored precision, and sample counts stay integral.
  Logarithmic travel serves frequency and broad positive time ranges. The
  mouse wheel continues to zoom, including over focused parameter entries.
- Essential adjustment/reset instructions stay visible in nodes. Dropdowns
  identify categorical choices, labelled buttons identify actions and on/off
  controls, and detector/reduction readouts and meters remain passive. Grouping
  and shape reinforce their differences; colour is not the only distinction.
  The interactive EQ graph and Voice Color pad remain alongside their dials.
- Continuous edits use a 100 ms target dispatch cadence. Later pointer movement
  cannot postpone a queued batch indefinitely; requests remain serialized and
  edits made during a request stay pending for the next batch.
- Device choices, Mono, parameter groups, level and mute use separate areas
  inside the node. EQ numbers remain directly visible; there is no **Precise
  values** expander. Inputs and outputs have Mute; all intermediate nodes have
  one bottom **Bypass** toggle: grey when off, red when on, always labelled
  Bypass. It skips processing and local/linked level adjustments, retaining
  parameters and cables. Bypassed linked controls contribute no level offset.
  Existing saved mute flags remain effective until explicitly cleared; a legacy
  muted intermediate shows its state and can be restored by clicking Bypass.
- Effects, Balance and Mono have no generic Level row. Band gains, compressor
  makeup and reverb mix retain their distinct functions. Existing nonzero effect
  levels stay effective and are disclosed through the **Saved level** context
  action, where they can be reset explicitly.
- The creation menu has 23 types in Inputs, Outputs, Effects, Mixing and Controls.
  Gain / Multiply and Reroute are excluded from Add and search but remain
  compatible with saved/copy-pasted graphs. Mix supplies a branch volume with
  one connection and combines sources with several; the engine supports all 25
  types without silently replacing old nodes.
- Compressor exposes threshold, ratio, attack, release and make-up with a
  detector selector and visible feedback. Its normal new interface has neither
  a Compress/Limit selector nor a Ducking preset. Limiter exposes only Ceiling
  and Release; visible text identifies sample-peak/no-lookahead behavior,
  bypass removing protection and later gain exceeding this node's ceiling.
- Ducking has **Audio input**, **Trigger / Sidechain · detector only** and
  **Audio output** sockets. Its fixed-depth model uses Threshold, Duck amount,
  Attack and Release in two related groups. Trigger level, reduction and
  Connect a trigger / Below threshold / Ducking states remain passive and
  visible. The detector diamond and lavender cable differ from round audio
  sockets and square parameter-control sockets. See [the behavior and
  compatibility contract](CORE_DYNAMICS.md) for timing, trigger cases and saved
  compressor preservation.
- The Incoming panel and per-cable send-level controls are removed. Existing
  saved cable gains and mutes remain in the graph and continue to apply. Use a
  Mix node on an individual branch when a visible branch-level control is needed.
- Microphones expose a **Mono** button instead of a channel dropdown. New nodes
  start in stereo with Mono off. Mono averages left and right into a centred
  signal. Existing saved single-channel assignments remain unchanged and display
  Mono on until explicitly toggled; switching it off restores stereo. Advanced
  channel selection remains available in Setup.
- Audio nodes have a 28-DIP vertical meter area to the left of their controls.
  Stereo shows separate measured L/R lanes; mono shows one centred lane. Peaks
  rise immediately, hold for 100 ms, then fall at 24 dB per second toward the
  latest level. Silence settles to zero; mute and unavailable sources clear
  immediately. Stale telemetry releases to silence rather than holding a frozen
  peak. This smoothing affects only the display. Reduced animation updates at
  4 Hz and retains each channel's peaks, with immediate clipping and mute response.
  Dim green, yellow and red zones remain visible at silence; the current level
  illuminates them. Yellow begins at −12 dBFS and red at −3 dBFS. Red is a
  headroom warning; the separate clipping indication reports actual overload.
- The EQ node is 620 DIPs wide with a 300-DIP-high editor and supports zero to
  eight bands. Double-click empty plot space to add a bell, drag a point for
  frequency/gain, and edit **Width / Q** below. Bells retain Q 1 by default;
  lower values make them broader. A smoothly sampled response shows the real
  biquad equation, with a separate measured post-EQ spectrum underneath and a
  right-hand dBFS scale. Opposite-polarity stereo retains its measured energy.
- Right-click an EQ band for **Shape** (Bell, Low shelf, High shelf, Notch),
  enable, **Reset position** and removal. Width presets are removed. Double-click
  a band to reset its frequency to its anchor and gain to zero while preserving
  Q, shape and enabled state. Right-click empty space for **Reset EQ to flat**,
  restoring the five default flat bands. Removing every band is also flat.
  Exact fields remain below the plot; pass/notch bands omit gain. Existing
  saved high/low-pass bands are retained until explicitly edited or reset.
- **High-pass filter** and **Low-pass filter** are separate nodes with cutoff
  and Q controls, using a second-order 12 dB/octave response. Defaults are 80 Hz
  high-pass and 12 kHz low-pass at Q 0.70710678. Cutoff spans 20 Hz–20 kHz and Q
  spans 0.1–20. Each contains one enabled matching filter; **Bypass** provides
  whole-node bypass without changing its cutoff or Q.
- A **Low / Mid / High** node exposes three dials from −12 to +12 dB: a 200 Hz
  low shelf, 1 kHz bell and 5 kHz high shelf. Each has its own full-width row and
  accepts an exact gain.
- **Denoiser**, **De-esser**, **Plosive remover** and **Reverb** use the same
  effect header colour and bottom Bypass control. Only their relevant parameters
  appear. Denoiser's 20 ms latency remains during bypass; separate graph paths
  have no automatic delay compensation. Research and algorithm limits are in
  [EFFECTS_RESEARCH.md](EFFECTS_RESEARCH.md); measured spectrum behavior is in
  [EQ_SPECTRUM_ANALYZER.md](EQ_SPECTRUM_ANALYZER.md).
- Every node context menu offers **Reset node to defaults**. Sound controls
  return to defaults while name, position, connections and device/app identity
  stay intact. Stereo/Mono and bypass reset too, and outputs return to their
  safe muted default. This is distinct from resetting only the EQ curve.
- Left-drag empty canvas to box-select nodes. Shift-drag adds to the selection;
  Shift-click toggles one node, and Ctrl+A selects all. Drag any selected header
  to move the group while retaining its relative spacing. Escape cancels the move.
  Hovering passive node surfaces uses the arrow; the move cursor appears only
  while a node is held for dragging. Actual text editors retain the I-beam.
- Ctrl+C copies selected nodes and their internal audio/control cables. Ctrl+V
  pastes at the pointer, with a small offset for repeated Ctrl+V. Right-click
  offers Copy, Paste and Delete. Paste selects the new group and retains its
  settings; it excludes connections to nodes outside the copied group.
- Copied microphone, virtual-input and app nodes retain their source assignments
  and independent controls, Mono and saved microphone channel selection. The
  input context menu also offers **Duplicate input**, creating a named copy
  nearby without connections. Copied outputs become unassigned when their device
  is already in use. Originals remain unchanged.
  The whole paste is rejected if it would exceed 32 nodes or 96 connections.
- Shift+A and F3 open node search; Home frames all.
  Middle drag pans, wheel zooms through fixed stops over node controls as well
  as the canvas. Open dropdowns and menus retain list scrolling. Delete removes the selection,
  and Escape cancels pending connections. Click the zoom percentage or press
  Ctrl+0 to return to 100%. New graphs start at 100%, with source/output rows
  spaced 330 DIP apart. Saved zoom and positions remain intact. Existing layouts
  are never automatically rearranged.

## Intentional adaptations

This is an audio editor with Blender's visual structure, not Blender embedded
inside the mixer. Audio-specific status, mute buttons, meters and level controls
remain. Device nodes are wider than simple processors so Windows names fit.
Inter is embedded in the package and needs no system installation; its version
and licence are recorded in [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
Windows Fluent remains the icon font. Green is the application's audio type;
it is not an official Blender audio socket type.
Selected nodes share the white active-node outline. Group movement and deletion
commit as one edit, and input fields/EQ retain their own clipboard and Delete keys.
Existing Reroute nodes remain compatible but are hidden from creation because
their current audio role duplicates Mix. Blender's point-only reroutes and group
frames are not added by this update. EQ is deliberately wider than ordinary
nodes to give its curve and live spectrum room without shrinking other controls.

## Verification and availability

Final verification counts and package activation for the current effect,
EQ and library update are tracked in [design-qa.md](../design-qa.md).
The later physical gesture and audible checks are in [TESTING.md](../TESTING.md).
They remain separate from detached rendering and synthetic DSP verification.
The records below are historical results for their named builds; references to
collapse, old expanders or send controls describe those earlier interfaces.

The earlier Mono, full-width controls and stereo-meter update passed
[720 detached checks and 57 renders](design/stereo-meters-final/results.json),
with zero binding errors. Coverage includes independent L/R values, lane geometry,
time-based release, reduced animation, toggle behavior and saved channel
assignments. The later hands-on stereo-source procedure is in
[TESTING.md](../TESTING.md); it has not been run against real audio or physical
controls. Build and activation evidence is recorded in [design-qa.md](../design-qa.md).

The selection/grab [published results](design/selection-grab-verified/results.json)
passed **641 checks and 55 renders**, with zero binding errors. They cover
constant two-screen-DIP selection outlines at 30–200%, multi-selection,
collapsed nodes, unchanged node geometry and passive hit tests of the header
grab area. Editable controls reject node dragging. The
[22:56 activation](../artifacts/selection-grab-update-20261017-225617/result.json)
replaced only the controller; the existing engine and saved configuration stayed
unchanged. Physical pointer gestures remain a separate check in TESTING.md.

The earlier uniform-zoom [published results](design/uniform-zoom-final/results.json)
passed **583 checks and 50 renders**, with zero binding errors. They verify
unchanged node proportions, fonts, borders, visible editors and socket geometry
from 30% through 200%, explicit collapse, and repeated Fit from 30% and 100%.
Both Fit cases reach the same proportional scale of approximately 75.7%
(displayed as 76%), with the complete EQ inside the 900 × 580 window. Review of
[50% effects](design/uniform-zoom-final/effects-50percent.png),
[75% effects](design/uniform-zoom-final/effects-75percent.png) and
[small-window Fit](design/uniform-zoom-final/effects-fit-small-window.png)
found no scoped issue. Existing dark/category colours, resize layouts, group
editing and high-contrast checks remain covered. Physical-display sharpness and
native gestures remain separate checks in [TESTING.md](../TESTING.md).

The [22:42 controller-only activation](../artifacts/uniform-zoom-update-20261017-224232/result.json)
opened responsive controller PID 45816 matching the verified package. Engine
PID 43332, its start time and binary remained unchanged, and its frame counter
advanced. Revision 733, the eight-node graph and settings bytes were preserved,
including after opening the new window.

The earlier dark/resizable controller, before the uniform-zoom correction, has
[detached results](design/oled-final/results.json) that
passed **499 checks and 41 renders**, with no binding errors. Fractional-zoom
captures include 75%, 90%, 110% and 150% at 96/144 DPI, including EQ/dials at
90%/110%, overview attention states and minimum/default/large window layouts.
Visual review found neutral text, distinct category bands, visible caption
controls and correctly contained Fit results. This establishes rendering and
layout; physical-monitor sharpness and native resize gestures remain separate.
See [design-qa.md](../design-qa.md) and [TESTING.md](../TESTING.md).

At 22:32 Brisbane, the [controller-only activation](../artifacts/oled-ui-update-20261017-223221/result.json)
opened responsive controller PID 48788 matching the verified package. Original
engine PID 43332, start time and binary remained unchanged, its frames advanced,
and revision 578, the eight-node graph and settings bytes were preserved,
including after opening. No engine restart or audio-route/device change occurred.

At 22:11 Brisbane, multi-node editing was installed as a controller-only update.
The [published results](design/group-edit-published/results.json) contain 368
passing checks and 24 detached renders with no binding errors, including group
selection, move/cancel, clipboard validation/replay and deletion. The
[activation record](../artifacts/group-ui-update-20261017-221057/result.json)
confirms a responsive new window, the same advancing engine process/binary and
unchanged revision 549/settings. Physical gestures and the Windows clipboard
remain separate checks in [TESTING.md](../TESTING.md).

At 21:53 Brisbane, the zoom/rendering correction replaced only the controller.
The [activation record](../artifacts/zoom-ui-update-20261017-215316/result.json)
confirms that the original engine process and binary stayed unchanged, its
frame counter advanced, and revision 310, settings bytes and saved layout were
preserved. The installed UI opened a responsive window. Both the ordinary
Release build and the published single-file executable passed **268 detached
checks and 22 renders**, with no binding errors. See the
[published results](design/zoom-fix-published/results.json),
[benchmark](ZOOM_RENDERING_BENCHMARK.md) and [review](../design-qa.md).
These are controlled rendering results; live pointer feel and physical display
rendering remain for the user to assess without desktop automation.

The updated controller and matching native engine were installed and opened at
21:35 Brisbane on 17 October. The [activation record](../artifacts/readable-update-20261017-213523/result.json)
confirms a responsive window, advancing frames and the identical saved
configuration. See [design-qa.md](../design-qa.md) for scope, the two updater
retries and the remaining interaction checks. The earlier
[61-check style result](design/blender-final/results.json) and
[21:04 controller-only activation](../artifacts/ui-switch-20261017-210400/result.json)
remain historical evidence.

The inert `--render-design --verification-dir <new directory>` mode renders
actual WPF controls from in-memory fixtures. It never shows or activates a
window, starts/connects to an engine, reads personal settings, or changes audio.
It renders controls at 96, 144 and 192 DPI with appropriately sized output
bitmaps, including full EQ close-ups. Current nodes always stay expanded; older
result sets also contain collapsed fixtures. Checks verify actual visual
DPI, bundled font resolution, absence of whole-node/ancestor effects or bitmap
caches, socket bounds, bindings and described programmatic edits. These checks
do not exercise physical mouse/keyboard gestures or a real monitor DPI change.

The final readable candidate's [results](design/blender-readable-final/results.json)
contain 226 passing scoped checks and 20 actual WPF renders at 96/144/192 DPI,
with zero binding errors. Its Release build completed with zero warnings and
errors. Visual review compared the official Blender reference, previous effects
view, final effects, 144-DPI EQ, high contrast, workspace, waiting and empty states;
no remaining scoped blocking issue was found. The cadence tests model two
seconds of edits every 8 ms: with 33 ms polling and a 70 ms fake request, batches
dispatch 100–133 ms apart, preserve intervening edits and commit the final value.
This measured simulation is separate from live audio or physical gesture tests.
Native EQ/tone tests passed 48,522 graph assertions; broader qualification
passed 7,901 assertions plus the synthetic engine lifecycle suite. See
[TESTING.md](../TESTING.md) for logs and the later interaction procedure.

The eight-hour endurance test remains **Deferred** after being stopped at the
user's request. Its partial results are incomplete and its follow-up automation
is paused. Audible and real mouse/keyboard qualification are also deferred;
none of these UI or synthetic checks substitutes for them. Package activation
is a separate step and is not established by these candidate results.
