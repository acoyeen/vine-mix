# Audio Mixer — readable Blender nodes and graphical EQ

17 October 2026, Australia/Brisbane.

## 19 October — Minimal cable hover

At the user's request, routine cable hover now shows only **source → destination**.
The redundant type heading, on/off and saved-level line, role explanation and
three gesture instructions were removed. This establishes the preferred normal
surface: concise context, with instructions in Help and details available through
intentional selection/context actions. Hover text is retained through gain,
mute and kind-only changes; endpoint renames update it. Accessibility detail,
context actions and the negative-coordinate hover fix are preserved.

The existing detached suite passed **8,036 checks**, produced **155 renders** and
reported **zero binding errors**. The single-line rendered hover was inspected.
No new test suite or native work was added. Evidence:
`artifacts/cable-hover-simple-20261019/ui-final/results.json`.
UI PID 64732 is running the verified binary; engine PID 22628, configuration,
settings and advancing audio were retained. No Computer Use occurred. Activation
and process evidence are in `artifacts/cable-hover-simple-20261019/activation.json`
and `post-activation.json`. The next batch awaits user review.

## 19 October — Cable tooltip disappearance

The user's graph exposed a WPF tooltip-owner mismatch: cable geometry drawn at
negative world coordinates can lie outside a Path's layout rectangle, which WPF
uses as its tooltip safe area. Only the invisible hit path is now normalized to
padded local coordinates and translated exactly back into world space. Its
explicit unrounded dimensions contain the entire stroke, including at fractional
DPI. Visible/outline geometry, socket anchors, cached edit coordinates, routes
and sound processing are unchanged. Padding updates on zoom; routine telemetry
retains the same owner, geometry and tooltip. Empty space inside the owner
rectangle remains empty canvas for hit testing.

The final detached run passed **8,032 checks**, generated **155 renders** and
reported **zero binding errors**. Regression cases include negative X, negative
Y, both negative, origin crossing, reversed cables and positive coordinates at
96/120/144/192 DPI and 30–200% zoom. Actual cubic controls and world transforms
are compared at 0.000001 DIP; independent analytic samples are checked against
both actual rendered strokes. Native ink-bounds queries allow 0.001 DIP.
Two earlier verifier failures are retained: flattening an unfilled cable produces
an empty path, making its arc-length queries inappropriate. The final verifier
uses actual stroke containment instead. Evidence:
`artifacts/cable-hover-fix-20261019/ui-final-3/results.json`.

The verified UI reopened as PID 40804. Engine PID 22628, binary/runtime, settings
and configuration were retained, with advancing audio frames. No Computer Use
occurred. Activation and process checks are in
`artifacts/cable-hover-fix-20261019/activation.json` and `post-activation.json`.
Actual native tooltip lifetime remains for the user's review. Numeric Tab order
is unchanged: each dial and its value box remains a separate keyboard stop.

## 19 October — Numeric Tab commits and cable details

Valid Tab/Shift+Tab applies a typed dial value once before ordinary keyboard
navigation. Invalid drafts stay editable with the existing red outline and
accessible error status. Escape cancels; click-away keeps its previous behavior.
Detached key-route and graph tests cover precision, no-op edits, modifiers,
validation, blur and exact Undo/Redo. Actual native focus traversal remains for
the user's running-app review.

Cable hover and selection now identify source, destination, audio/control/detector
purpose, saved level and on/off state. Cached descriptions update after names,
routes or settings change without rebuilding on every telemetry refresh. Only
nonzero saved levels offer Reset connection level to 0 dB. The explicit action
preserves route/mute and is undoable. Menu actions also check connection identity
when queued edits replay, so removed or rewired cables are not mistaken for the
original menu target. No persistent canvas labels were added.

The combined detached suite passed **6,800 checks**, generated **155 renders**
and reported **zero binding errors**. Three real tooltip contents were rendered;
audio and control close-ups were inspected. The final source matches all 73
files in the immutable build snapshot. A post-snapshot local parameter rename
was reversed to retain the exact successfully compiled and tested source.
Evidence: `artifacts/keyboard-cables-20261019/ui-final/results.json` and
`verification.json`.

The verified UI was installed and reopened as PID 23048. Engine PID 22628,
its start time, binary and runtime were preserved. Read-only snapshots while the
UI was absent confirmed unchanged configuration, preserved running paths and
advancing audio frames. No Computer Use occurred. Activation evidence is
`artifacts/keyboard-cables-20261019/activation.json` and `post-activation.json`.
The next batch awaits user review.

## 19 October — RNNoise default and cable sockets

New denoisers start with RNNoise at 100% Mix. Existing, loaded and pasted nodes
retain their algorithm; older configurations still use SpeexDSP. Reset preserves
the selected processor while restoring its controls.

Audio sockets are matching 12-DIP circles centred on the rendered node border.
Control squares and sidechain diamonds retain their distinct shapes. Empty
sockets have coloured outlines; connected sockets are filled, with connection
status also exposed to accessibility. Hover/focus/snap changes the outline only.
The 24 × 20 invisible targets and 18-screen-DIP snapping radius are unchanged.
Node-position rounding now keeps cables aligned at fractional monitor DPI,
including before the next layout pass while dragging.

The final detached run passed **6,710 checks**, produced **152 renders**, and
reported **zero binding errors**. The first run exposed an inappropriate native
visibility filter in a detached test; the second exposed the real fractional-DPI
cable offset. Both earlier reports are preserved. Final report:
`artifacts/socket-refinement-20261019/ui-final-3/results.json`.
Connected/disconnected socket close-ups and the connection preview were inspected.

The verified UI was installed and reopened as PID 12800. Engine PID 22628 and its
start time/binary were retained; snapshots confirmed unchanged configuration,
preserved running paths and advancing audio frames. No Computer Use was performed.
Activation evidence: `artifacts/socket-refinement-20261019/activation.json` and
`post-activation-verified.json`. Native processing was unchanged from the previous
14-suite verified build. Live appearance and interaction remain for user review;
the next feature batch waits for that feedback.

## Result and scope

**Passed for the scoped visual, detached WPF and synthetic processing checks.**
The Release build has zero warnings/errors. The final offscreen run passed
**226 assertions**, with zero binding errors and 20 renders at 96, 144 and
192 DPI. No actionable blocking visual finding remains in the reviewed states.

These checks do not establish live audio quality, actual pointer capture,
keyboard focus navigation or monitor-to-monitor DPI transitions. The renderer
used detached controls and in-memory settings; it did not show windows, create
native window handles, connect to audio, or read/change personal settings.
The eight-hour endurance test remains **Deferred** at the user's request.

## Reference and comparison

- [Blender node anatomy](https://docs.blender.org/manual/en/latest/interface/controls/nodes/parts.html)
  and its unchanged [reference image](docs/design/blender-reference/node-parts-overview.png).
- [Blender default theme](https://github.com/blender/blender/blob/main/release/datafiles/userdef/userdef_default_theme.c).
- [FL Studio Parametric EQ 2](https://www.image-line.com/fl-studio-learning/fl-studio-online-manual/html/plugins/Fruity%20Parametric%20EQ%202.htm)
  as the interaction reference for direct point manipulation and band-width adjustment.
- [Inter 4.1](https://github.com/rsms/inter/releases/tag/v4.1), bundled under SIL OFL 1.1.

The reference, previous effects view, final effects, expanded EQ at 144 DPI,
high contrast, full workspace, waiting and empty states were loaded together
for comparison. The [effects overview](docs/design/blender-readable-final/effects.png)
uses 100% canvas zoom in a 1280 × 800 DIP window. The
[expanded EQ](docs/design/blender-readable-final/eq-closeup-144dpi.png) uses
144-DPI output pixels and the actual node class, independently of canvas zoom.
The reference has different controls and unspecified source zoom; this is a
comparison of visual structure and interaction, not a pixel-identical clone.

## Changes reviewed

- Bundled Inter, 13 DIP control text and 15 DIP node titles; stronger primary
  text, consistent 30-DIP fields and clearer spacing inside nodes.
- Direct WPF text rendering, display metrics, fixed hinting, layout rounding
  and pixel-aligned panning. Whole-node effects/bitmap caches are prohibited
  by the renderer checks because they can rasterize text before scaling.
- Blender-style category headers, edge sockets, compact fields, chevrons,
  thin selected outline and restrained charcoal canvas.
- Direct EQ editing: double-click to add, drag frequency/gain, scroll over a
  point for Q, and remove/disable/reset bands. Zero to eight bands are valid;
  existing five-band settings are preserved. Precise values are expandable.
- Three actual Low / Mid / High dials with exact numeric entry, Shift fine
  movement, keyboard adjustment and zero reset. Each uses a fixed filter and
  a −12 to +12 dB range.
- Fixed zoom stops and a clickable percentage/Ctrl+0 reset to 100%. Fit is an
  overview and may make text small. Existing saved positions/zoom are retained;
  new/legacy layouts have enough space for the larger nodes.

## Findings corrected

| Finding | Correction and evidence |
|---|---|
| Small text and soft whole-node rendering | Larger embedded Inter text and removal of the shadow effect; font resolution and absence of rasterizing ancestors checked at all three DPIs |
| EQ editing required band buttons and sliders | Direct editable points; checks compare the complete graph after moving, adding and removing points |
| Continuous dragging starved engine updates | Bounded 100 ms dispatch cadence instead of trailing debounce; sustained-edit, in-flight, retry and final-value regression checks |
| Deselection was overwritten during synchronous refresh | Preserve no-selection state and display a truthful hint; regression check uses the actual node binding |
| Old default rows overlapped enlarged nodes | Both new/legacy factories now use 330-DIP row spacing; saved version-2 positions/zoom round-trip unchanged |
| Expanded EQ clipped the high-contrast overview | Fit the complete high-contrast fixture and retain a separate expanded close-up |

## Evidence and limits

[Machine-readable UI results](docs/design/blender-readable-final/results.json)
record every assertion, image size, visual DPI and expanded/collapsed socket
geometry. Tests also verify that the actual Mid dial changes only its assigned
gain, incoming routes survive edits, a ninth EQ point is rejected, zero bands
remain valid, and Shift changes do not reinterpret prior drag movement.

Native processing passed **48,522 graph assertions**, **7,901 broader
qualification assertions over 4,527,360 synthetic frames**, and **210 synthetic
engine lifecycle commits**. These use generated data/stub workers, not live
microphone or listening tests. Logs and procedures are in [TESTING.md](TESTING.md).

The renderer exercises shared control methods programmatically; actual mouse,
keyboard, menu and monitor-change behavior still needs the later agreed UI
session. Hardware recovery, Discord/game audio, audible quality, latency and
the deferred soak remain tracked in the
[qualification report](docs/TEST_RESULTS_2026-10-17.md).

The previous style review and its earlier controller-only activation are
preserved in [design-qa-blender-initial.md](design-qa-blender-initial.md).
The current personal-package activation is recorded below.

## Installed personal update

At 21:35 Brisbane on 17 October, the updated single-file controller and matching
native engine were installed into the personal package and opened. The published
single-file build also passed the same 226 offscreen checks, including bundled
font resolution, before activation.

The [successful activation record](artifacts/readable-update-20261017-213523/result.json)
confirms a responsive visible window, matching candidate binaries, an advancing
audio frame counter, unchanged configuration revision 207 and identical settings
file bytes. All previously running paths returned to Running. Listening output 1
was Waiting before and after; output 2 and microphone 2 remain Unassigned.
No routes, gain settings, saved positions/zoom, Windows audio defaults or device
volumes were altered, and no test audio was generated.

The successful restart reached a running snapshot after 0.421 seconds. This is
control/status timing, not a measured audible gap or hardware recovery benchmark.
Two earlier updater attempts restarted the old application after shutdown-pipe
cleanup/acknowledgement handling failed:
[21:34 attempt](artifacts/readable-update-20261017-213437/result.json) and
[21:35 attempt](artifacts/readable-update-20261017-213501/result.json).
The updater was corrected to tolerate closure after shutdown and verify the exact
engine process's exit. Their backups and failure evidence remain intact.

## Zoom rendering correction — 17 October, 21:53 Brisbane

The user's overview screenshot showed multicoloured text fringes and excessive
grid density. The previous node theme also contained rose/blue/purple headers.
Scaled text now explicitly uses grayscale rendering, and every category header
uses neutral charcoal. Audio cables/meters retain green and mute retains its
alert colour. The new [30% view](docs/design/zoom-fix-v2/workspace-30percent.png)
was visually inspected; it has neutral text/headers and a sparse grid. At this
scale small controls are an overview, not a substitute for full-size editing.

The refresh path previously replaced its transform, redrew thousands of dots,
rebuilt cables and copied configuration on ordinary meter updates. It now:

- Updates the retained transform only when the view changes; accumulates small
  wheel deltas and handles multi-notch events in one update.
- Draws one cached grid tile with at least 16 DIP between visible grid dots.
- Rebuilds frozen cable geometry only for changed topology/socket positions.
- Separates live telemetry from configuration refresh; preserves optimistic
  edits and forces reconciliation on acknowledgement, rejection or reconnect.
- Skips unchanged EQ and dial drawings while keeping meters/status current.

[Microsoft's rendering modes](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.textrenderingmode?view=windowsdesktop-10.0)
explain the difference between RGB subpixels and grayscale text.
[WPF layout guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-layout-and-design)
recommends updating an existing transform instead of replacing it. The precise
cause of the screenshot's artefact is inferred from its RGB fringe pattern and
the previous Auto rendering mode; actual display output was not captured again.

Release and published builds passed **268 checks / 22 renders**, with zero
binding errors: [Release](docs/design/zoom-fix-v2/results.json),
[published](docs/design/zoom-fix-published/results.json). New checks cover
grayscale inheritance, neutral palette, static telemetry retention, wheel
accumulation, bounded grid density, geometry updates after movement/collapse,
and config refresh decisions. An initial test caught floating-point cable
anchor noise after zoom; the correction tolerates only 0.000001 world DIP,
well below a visible displacement. The earlier failure remains under
`docs/design/zoom-fix-final`.

The [detached benchmark](docs/ZOOM_RENDERING_BENCHMARK.md) compares identical
eight- and 24-node fixtures. At 30%, eight-node telemetry/layout dropped from
2.325 to 1.042 ms and temporary allocation from 3,280,957 to 53,960 bytes.
Both 30% offscreen rendering workloads improved. Some other zoom/raster cases
were slower, so this is not a universal rendering-speed or live-FPS claim.

The [controller-only activation](artifacts/zoom-ui-update-20261017-215316/result.json)
opened a responsive updated window while engine PID 43332 and its binary stayed
unchanged. Frames advanced from 51,544,800 to 51,560,880. Configuration revision
310 and settings bytes were identical before/after opening. No engine restart,
audio test signal, route/device/gain change or Windows audio change occurred.
The engine's existing A1 Waiting state was preserved. Physical pointer feel,
display antialiasing and monitor changes remain outside these detached checks;
the eight-hour endurance run stays Deferred.

## Multi-node editing — 17 October, 22:11 Brisbane

Implemented empty-canvas box selection, Shift-click toggling, additive boxes,
Ctrl+A, shared header dragging, group copy/paste and deletion. Selection itself
changes no audio configuration. A completed move, paste or deletion is one
configuration edit. Dragging preserves group spacing and keeps cables attached;
Escape restores the starting positions or selection.

Right-click menus and Ctrl+C/Ctrl+V use a versioned JSON clipboard format. Copies
snapshot all settings and internal audio/control cables, assign fresh identities
and omit cables to nodes outside the selection. Paste selects the new group.
Conflicting copied device/app identities are cleared so the originals remain
assigned; copied channel/settings remain intact. Size, schema, finite values,
graph limits and topology are checked before adding anything. Queued paste
replay rechecks the current graph and keeps generated identities stable.

Text editors keep normal clipboard/Delete behavior. EQ and dial editing keys
remain local to those controls. Node context menus refresh their selection and
actions when opened. This follows Microsoft's
[context-menu opening guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/how-to-handle-the-contextmenuopening-event).
The clipboard carries JSON strings, supported by the current
[WPF clipboard guidance](https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-migration-guide/winforms-wpf-ole-guidance),
and uses typed string retrieval instead of arbitrary-object deserialization.

The [published executable results](docs/design/group-edit-published/results.json)
record **368 passing checks, 24 renders and zero binding errors**. New tests use
the real selection/movement/paste/delete operations against detached WPF controls.
They cover reversed and additive selection, overlay bounds/contraction, cancel,
telemetry during movement, shared snapping/bounds, one-edit commits, deep copies,
internal edges, repeated paste, unrelated route preservation, invalid payloads,
capacity rejection, device conflicts and paste replay. The Release build and
self-contained publish succeeded. The actual
[selection box](docs/design/group-edit-published/group-box-selection.png) and
[pasted group](docs/design/group-edit-published/group-pasted.png) were inspected.
No desktop input, OS clipboard access or live audio was used in these tests;
physical gesture/Windows clipboard checks remain in TESTING.md.

The [controller-only activation](artifacts/group-ui-update-20261017-221057/result.json)
opened responsive controller PID 43108. Engine PID 43332, its start time and
binary stayed unchanged; frames advanced from 102,414,960 to 102,428,160.
Revision 549, the user's eight-node graph and settings bytes remained identical,
including after the new window opened. Existing node states were preserved;
no audio restart, test signal, device/default/volume change or endurance run
occurred.

## Dark canvas, fractional zoom and resizing — 17 October

The earlier grayscale-only fix removed coloured glyph fringes but retained
display-hinted text metrics and every miniature editor at low zoom. The new
candidate uses ideal text metrics with grayscale antialiasing and directly
renders nodes without a whole-node bitmap cache or effect. Below 80% canvas
zoom, controls become a routing overview with a title, ports and a live meter.
Titles retain at least 12 screen DIPs and port labels at least 11; zooming in
restores editing without changing explicit collapse states or graph settings.
This is an intentional detail transition, not a promise of full-size controls
at every zoom. Thin outlines/socket sizes remain stable on screen, and cable
anchors update when presentation changes.

The canvas is black, bodies and chrome are near black, and category headers
return in restrained plum, red, purple, blue-green and green. These colours
identify node categories; text itself stays neutral. Healthy status phrases,
repeated instructions and the frame-counter footer are removed from the main
view or moved to tooltips. The initial 1280 × 800 window can resize down to
900 × 580 or maximise using its integrated caption. Resizing preserves graph
positions and zoom rather than automatically rearranging nodes.

The final [published results](docs/design/oled-final/results.json) passed
**499 checks / 41 renders**, with zero binding errors. Actual WPF canvases are
rendered at 75%, 90%, 110% and 150% zoom and 96/144 DPI, with additional 30%/50%
overviews, EQ/dials at 90%/110%, attention states and 900 × 580, 1280 × 800 and
1920 × 1080 layouts. `zoomEvidence`
records each node's effective screen title size and transformed bounds;
`windowLayouts` records canvas and caption bounds. The requested DPI changes
the WPF visual DPI and output pixel dimensions before rendering; images are
not enlarged after capture. Existing group edits, cable handling, EQ/dials,
meter retention, high contrast and no-native-window checks remain included.

Visual review inspected [30% overview](docs/design/oled-final/workspace-30percent.png),
[90% detail](docs/design/oled-final/workspace-90percent.png),
[110% detail](docs/design/oled-final/workspace-110percent.png),
[minimum window](docs/design/oled-final/workspace-window-900x580.png),
[fractional effects](docs/design/oled-final/effects-110percent-144dpi.png),
[attention marks](docs/design/oled-final/overview-attention.png) and
[Fit in the small window](docs/design/oled-final/effects-fit-small-window.png).
The reviewed captures have neutral
text without the earlier RGB speckles, distinguishable category bands and
visible caption/toolbar controls. Long titles are ellipsised in the 30% overview
and retain their full tooltip. Nodes outside the visible canvas are clipped
normally at larger zoom and smaller window dimensions. No scoped visual blocker
was found in these captures; they do not measure live GPU zoom latency or
physical-display antialiasing. A stale group-test message in the effects fixture
was cleared before final captures.

The earlier [452-check candidate](docs/design/oled-first/results.json) preceded
additional attention/Fit coverage. Two subsequent published verification
attempts correctly failed: a status glyph's line box changed overview height,
then deferred WPF child layout invalidation made repeated Fit unstable. The
attention row now has a fixed height, and Fit settles layout at each bounded
candidate scale before measuring. Final tests keep the original tolerances:
Fit from both 30% and 100% reaches identical 79% zoom/pan and contains the
single EQ inside the 900 × 580 window. Earlier reports remain in
`docs/design/oled-published` and `docs/design/oled-published-v2`.

The [22:32 controller-only activation](artifacts/oled-ui-update-20261017-223221/result.json)
opened responsive controller PID 48788 matching the verified executable.
Engine PID 43332, its start time and binary stayed unchanged; frames advanced
from 164,072,160 to 164,086,560. Revision 578, the user's eight-node graph and
settings bytes were identical, including after the new window opened. No
engine restart, audio test signal, route/device/gain change or Windows audio
change occurred.

These checks do not establish audio reliability. Physical resize/maximise/snap
and monitor-DPI transitions remain in TESTING.md;
the eight-hour endurance run remains Deferred. Near-black styling makes no
claim about a particular OLED panel's power use or burn-in resistance.

## Uniform zoom correction — 17 October

The user clarified that zoom must preserve every control and each node's
proportions. The earlier automatic routing overview is therefore removed.
Zoom now applies one uniform transform to complete nodes: fonts, borders,
sockets, EQ plots, dials and other controls retain fixed dimensions within the
canvas and scale together on screen. Explicit folding through the chevron or H
remains available. Low zoom naturally makes text smaller; there is no automatic
detail cutoff or minimum on-screen title size. Ideal font metrics, grayscale
antialiasing and direct rendering remain, as do the dark surfaces, category
colours and resizable window.

The final [published result](docs/design/uniform-zoom-final/results.json) passed
**583 checks and 50 renders**, with zero binding errors. Tests compare node
dimensions, fonts, borders, editor visibility and socket geometry from 30%
through 200%, verify manual collapse and unchanged cable geometry during zoom,
and retain group-editing, EQ/dial, high-contrast and resize coverage. A complete
EQ fits inside the 900 × 580 window at approximately 75.7% (displayed as 76%).
Starting at either 30% or 100% and repeating Fit produces the same zoom and pan.

Visual review inspected [50% effects](docs/design/uniform-zoom-final/effects-50percent.png),
[75% effects](docs/design/uniform-zoom-final/effects-75percent.png) and
[small-window Fit](docs/design/uniform-zoom-final/effects-fit-small-window.png).
The controls remain visible with their original proportions; the fitted EQ is
fully contained. No scoped issue was found. The earlier overview screenshots,
499-check result and activation record above remain historical evidence rather
than the current zoom contract.

These checks used detached WPF visuals and in-memory fixtures, with no desktop
control, native window, audio stream, personal settings or engine connection.
They do not establish physical-display sharpness, native mouse/resize behavior
or audio reliability. The endurance run remains Deferred.

The [22:42 controller-only activation](artifacts/uniform-zoom-update-20261017-224232/result.json)
opened responsive controller PID 45816 with the verified executable hash.
Engine PID 43332, its start time and binary remained unchanged; frames advanced
from 193,418,640 to 193,431,840. Revision 733, the eight-node graph, full
configuration and settings bytes were unchanged, including after opening the
new window. The update did not restart the engine or change audio settings.

## Selection visibility and forgiving grab areas — 17 October, 22:56

Selection now uses a separate two-screen-DIP drawing outline. Its stroke is
inverse-scaled while normal borders, node dimensions, fonts, sockets and editor
layout remain unchanged. The outline is non-interactive and follows the theme's
selection colour. It also covers multi-selection and manually collapsed nodes.

Nodes can be moved from unused body space, with eight screen DIPs of grab
padding above the header and six at its sides. These transparent targets sit
outside layout and below sockets. Buttons, fields, numeric controls, expanders,
EQ plots and dials reject node dragging and retain their normal input behavior.

The [published results](docs/design/selection-grab-verified/results.json) passed
641 checks and 55 rendered views, with zero binding errors. Review of 30%, 100%
and collapsed 30% selection confirms a clear outline with unchanged contents.
The initial detached grab check used InputHitTest, which returned no target
without a native presentation source. The harness now uses visual hit testing
with visibility/hit-test filters and still requires the actual grab target and
node ancestry. No physical input was injected; hands-on checks remain documented
in TESTING.md.

The [activation record](artifacts/selection-grab-update-20261017-225617/result.json)
confirms responsive controller PID 47656 and the identical engine PID 43332,
start time and binary. Frames advanced from 233,022,960 to 233,035,440. Revision
1019, eight nodes, full configuration and settings bytes were preserved,
including after opening the updated controller. No audio tests, engine restart,
route/device/gain changes, or endurance testing occurred.

## Dropdown wheel isolation — 17 October, 23:23

Canvas wheel handling now recognises dropdown popup trees, item content,
logical/template ownership and the pointer's position over the open list.
Protected events remain unhandled on the preview route so the list can scroll;
they also clear pending canvas wheel fractions. Outside the list, only sources
retargeted to the captured ComboBox are replaced with the actual canvas hit.
Genuine context-menu and other editor sources retain their wheel protection.

The [published results](docs/design/dropdown-wheel-verified/results.json) passed
651 checks and 55 renders, with zero binding errors. Added detached fixtures
cover popup, content and template ancestry, editor protection, unchanged zoom,
cleared partial wheel input and normal canvas zoom afterward. No native popup
was opened and no physical mouse input was injected. The hands-on dropdown
scrolling procedure is recorded in TESTING.md for later verification.

The [activation record](artifacts/dropdown-wheel-update-20261017-232307/result.json)
confirms responsive controller PID 44112, with engine PID 43332, its start time
and binary unchanged. Frames advanced from 310,277,280 to 310,291,200. The
current one-node graph at revision 1080, full configuration and settings bytes
were preserved, including after reopening. No audio settings were changed.

## Closed dropdown wheel protection — 17 October, 23:31

The shared ComboBox style now consumes preview wheel events while its dropdown
is closed. This applies to node selectors and Setup selectors. Open lists retain
their existing scrolling; click and keyboard handlers are unchanged. WPF's
[mouse event promotion](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Input/MouseDevice.cs)
only creates the selection-changing bubbling wheel event when preview input is
unhandled, so no focus manipulation or selection rollback is needed.

The [published results](docs/design/closed-dropdown-verified/results.json) passed
656 checks and 55 renders, with zero binding errors. The new fixture uses the
actual application style and raises preview events from its template surface
in both directions. Both are consumed, selection stays unchanged and no
SelectionChanged event fires. Existing popup/canvas checks also pass. These are
detached checks without a native window, focus changes or physical input; later
hands-on coverage, including keyboard selection, is recorded in TESTING.md.

The [controller activation](artifacts/closed-dropdown-update-20261017-233149/result.json)
opened responsive controller PID 50152. Engine PID 43332, its start time and
binary were unchanged; frames advanced from 335,330,400 to 335,343,360. The
one-node graph at revision 1080, full configuration and settings bytes were
preserved, including after reopening.

## Numeric entry and double-click reset — 17 October, 23:50

Numeric fields now use a shared text-entry template without the extra bright
focus rectangle. Their text host, caret, selection and validation border remain.
The slider/label and numeric value both support double-click reset; tone-dial
values also share their dial's reset action. Levels reset to 0 dB and other
parameters use model defaults, documented in TESTING.md and control tooltips.
Reset cancels the control's active drag, replaces unfinished text, clears stale
validation feedback and dispatches a change only when the value differs.

The [final published results](docs/design/numeric-reset-final/results.json) passed
669 checks and 55 renders, with no binding errors. Added checks cover actual
numeric templates, validation borders, reset callbacks, cancelled dragging,
uncommitted text, explicit nonzero defaults and local double-click event routes.
The first detached attempt checked dynamic resources before theme initialization;
moving that check after the unshown MainWindow constructor fixed the harness
without weakening assertions. No native input, focus changes or audio testing
occurred. The rendered effects view retains the previous layout; focused native
interaction remains a later hands-on check.

The first activation deferred because settings were changing. The subsequent
[successful controller activation](artifacts/numeric-reset-update-20261017-235027/result.json)
opened responsive controller PID 26284. Engine PID 43332, its start time and
binary remained unchanged, with frames advancing from 388,995,120 to 389,007,600.
The current two-node graph at revision 1365, full configuration and settings
bytes were preserved, including after reopening.

## Mono and vertical stereo meters — 18 October, 00:14

Microphone nodes replace the channel dropdown with a full-width Mono button.
New microphones default to stereo; Mono averages left and right with a 10 ms
transition. Existing explicit channel selections stay intact until the button
is changed. Independent post-gain left/right peaks now travel from the native
engine to a pair of thicker vertical bars at the left of each audio node.
Known mono signals use one centred bar. Nodes are wider to retain their usable
control width, with each option on its own full-width row. Tone dials use three
stacked rows; EQ and incoming-connection controls follow the same alignment.

The meter has immediate attack, 100 ms hold and 24 dB/second release, driven by
elapsed time. Mute and unavailable paths clear it immediately. Stale telemetry
decays to silence; reduced animation retains peaks at four presentations per
second. The display timer stops for detached, hidden, muted or silent meters.

[Native verification](artifacts/verification/native-stereo-mono-20261018.txt)
passed 48,561 offline assertions and 210 concurrent synthetic graph commits.
Coverage includes independent channel peaks, averaging/cancellation, transitions,
channel propagation, preserved settings and no worker recreation for Mono edits.
These tests opened no hardware audio devices. The
[detached UI results](docs/design/stereo-meters-final/results.json) passed 720
checks and 57 renders with zero binding errors, covering smoothing cadence,
stale samples, reduced animation, Mono interaction, full-width layout and the
existing zoom/selection/clipboard behavior. The first run found a strict
clipboard shape check rejecting the new optional flag; accepting that flag while
retaining version-one compatibility resolved it, with added regression checks.
The microphone stereo/mono closeups and effects view were visually inspected.

The [activation record](artifacts/stereo-meter-update-20261018-001409/result.json)
confirms the new engine and responsive controller were loaded. All three
previously running paths reported Running within 0.309 seconds of beginning
the engine restart, and the audio frame counter advanced. This is a software
recovery observation, not a measured audible gap. Full configuration at revision
1908 and saved settings bytes remained unchanged, including after opening the
controller. No test sounds, driver operations or Windows routing changes were
performed. Physical listening and interaction checks remain in TESTING.md.

## Cable editing, filter nodes and simplified controls — 18 October, 00:33

Node meters retain dim green/yellow/red regions and illuminate each region as
its channel reaches that level (yellow -12 dB, red -3 dB), with the existing
time-based falloff. Pixel checks verify the actual faded/full colours and
independent stereo fill. Nodes are always expanded, including when old data
contains `Collapsed=true`; collapse controls and H folding are removed. The
Incoming connections panel and duplicate send-volume controls are removed.
Previously saved cable gains and mutes remain unchanged in the engine.

Effect nodes have a contrasting full-width footer labelled Effect on or
Bypassed. EQ numbers stay directly below the graph. The point context menu
offers Bell, Low shelf, High shelf and Notch, plus Smooth/Medium/Sharp width
presets and enable/reset/remove. Dedicated High-pass and Low-pass nodes provide
cutoff and resonance/Q. Their default 80 Hz and 12 kHz cutoffs use 12 dB/octave
biquads; saved EQ pass bands remain compatible. The formulas and analytical
drawing agree with the [W3C Audio EQ Cookbook](https://www.w3.org/TR/audio-eq-cookbook/).

Cable interaction follows the insertion and Alt-drag detachment direction in
[Blender's node-editing manual](https://docs.blender.org/manual/id/5.0/interface/controls/nodes/editing.html).
Compatible sockets snap within 18 screen DIPs with a 22-DIP halo. Cable selection
has a 16-DIP hit region. Dropping an unconnected processor/mix/reroute onto the
highlighted audio cable commits its position and split together. The original
cable's gain/mute is retained once; the other segment is unity. Alt-drag or the
Detach context action reconnects a single input to its destinations and retains
the node. Its processing, gain and mute leave the path with it. Ambiguous inputs,
linked controls, duplicate routes, cycles, full graphs and unrepresentable cable
gain sums are rejected without partial rewiring. Hover and cancellation do not
change audio routing. Grid rounding cannot switch the highlighted target.

[Native results](artifacts/verification/native-filters-notch-20261018.txt) passed
265,572 offline assertions and 210 synthetic lifecycle commits. New coverage
includes filter response and slopes, notch depth/width, stereo independence,
extreme parameters, bypass, coefficient crossfades and persistence. The real
audio worker implementations are not linked into those tests.

The [verified published UI](docs/design/node-refinements-verified/results.json)
passed 896 checks and 61 rendered previews with zero binding errors. Checks
exercise actual effect buttons, filter numeric controls and unopened EQ menus;
legacy collapsed data; clipboard compatibility; fixed-size snap targets across
zoom; cancellation, stale target deletion and replay-safe atomic edits. Review
caught and fixed compounded gain/mute after detach/reinsert, stale snap references,
capture loss and grid rounding selecting a different cable. Regression cases
cover all four. A null/scalar clipboard-node guard was also added. The initial
run stopped at an obsolete selection-hint assertion after redundant text was
removed; it now verifies the plot's accessible hint and disabled numeric fields.
Final previews were inspected for meter fill, EQ/notch layout, pass filters,
effect on/bypass contrast, socket halo and insertion highlighting. The wider
gesture previews show both endpoints without changing the saved fixture view.

[Activation](artifacts/node-refinement-update-20261018-003325/result.json) loaded
the matching controller and engine successfully. All four previously running
nodes reported Running within 0.299 seconds of starting the engine restart;
the fifth node remained Unassigned. Frames advanced and the window responded.
Full configuration at revision 2386 and saved settings bytes stayed identical,
including after reopening. The 0.299 seconds is a software-state observation,
not a measured audible gap. No desktop input, test signals, Windows routing,
drivers or security settings were changed. Hands-on interaction, listening and
the deferred endurance qualification remain documented in TESTING.md.

## Cable unplugging and workspace cleanup — 18 October, 01:02

Blender research distinguished input-link dragging from whole-node detachment.
The [manual](https://docs.blender.org/manual/en/3.4/interface/controls/nodes/editing.html)
documents dragging an input link into empty space and Ctrl-right-drag cutting.
The [current operator source](https://github.com/blender/blender/blob/main/source/blender/editors/space_node/node_relationships.cc)
and [move macros](https://github.com/blender/blender/blob/main/source/blender/editors/space_node/node_ops.cc)
confirm that whole-node detachment attempts to reconnect neighbours; that
existing Alt-drag behavior is retained and labelled Detach node (reconnect).

Connected-input dragging now picks up an existing cable, moves it to another
compatible input or removes it on a blank-canvas drop. Multiple incoming cables
can be identified by following their curve, selecting one first or grabbing its
input end. Ambiguous overlapping curves stay connected. Invalid drops, another
cable, node bodies, Escape, capture loss and concurrent cable changes preserve
the current route. Ctrl-right-drag previews and atomically removes crossed
cables on release. Hover never edits routing; cable identity, gain and mute
survive rewiring. Preparation and replay checks guard the entire edit.

Creation now uses coloured Inputs, Outputs, Effects, Mixing and Controls groups
from the same catalog as node headers. Search covers labels and groups. Flat
toolbar/caption controls, dark context menus and scrollbars replace framed
controls and Windows' white menu gutter. The node count and save/warning state
are in the header; both footers are removed. History contains up to 100 actual
edits from this controller session, coalesces rapid parameter adjustments, and
provides the latest editor hint. It is a read-only history, not undo/redo.

Pan, zoom, Fit, selection and hover do not queue configuration requests. No-op
setting changes are ignored; real graph edits include the latest local view.
Navigation alone is session-only. Reserved status space prevents node size and
socket shifts through waiting/running/clipping changes. Header/body grab areas
and the captured drag use the move cursor; editable fields retain the I-beam.

The [final published build](docs/design/cable-editing-20261018-010205/results.json)
passed **1,001 detached checks and 65 rendered previews**, with no binding errors.
Coverage includes gesture commit/cancel/stale-state cases, multi-input ambiguity,
cutting at 30/100/200% zoom, actual zoom/Fit without saves, no-op edits, history
coalescing, grouped search and every node's size/socket invariants across state
and control changes. Actual node, header, menu, history, rewire and cutting
previews were inspected. A custom menu-style/Separator incompatibility found
during rendering was corrected before publication. Native audio code is unchanged.

[Controller-only activation](artifacts/cable-editing-update-20261018-010241/result.json)
replaced UI 45340 with responsive UI 49792. Engine 32504 retained its start time
and binary hash, and frames advanced from 84,299,520 to 84,311,040. Configuration
revision 2775 and settings bytes remained identical, including after reopening.
Five assigned paths remained Running and one Unassigned. No desktop input,
test audio, device/default changes or driver operations were performed. Native
popup interaction and audible qualification remain explicitly deferred in
TESTING.md; detached rendering does not establish physical-pointer feel.

## Window dragging — 18 October, 01:18

The stretched warning field, save indicator and connection dot incorrectly
opted into client hit testing. Even an empty warning therefore intercepted
caption dragging over much of the title bar. The native caption also extended
partway into the toolbar because its height starts below the resize border.
Passive header content now participates in native caption dragging, and the
caption covers both header rows, ending at the canvas. Only actual buttons opt
out. Shared row-height constants keep the geometry aligned; toolbar margins
now leave the full 26 DIP button height available for clicking.

The [published verification](docs/design/window-drag-20261018-011715/results.json)
passed **1,075 detached checks and 65 renders**, with no binding errors. The new
checks use WPF's actual caption geometry routine and detached visual hit tests
across 900×580, 1280×800 and 1920×1080 at 96/144/192 DPI, with empty and populated
warnings. They cover all twelve buttons at centres and inner edges, passive
text and both header rows, the canvas boundary and native resize edges. The
small-window render was inspected. No HWND or desktop input was created for
these checks; native snap/restore and physical dragging remain hands-on checks
in TESTING.md.

[Controller-only activation](artifacts/window-drag-update-20261018-011745/result.json)
replaced controller 49792 with responsive controller 46792. Engine 32504 retained
its start time and binary hash, with frames advancing from 127,682,400 to
127,694,160. Configuration revision 2935 and settings bytes stayed identical,
including after reopening. Four assigned paths remained Running and one
Unassigned. No test audio, device/default changes or driver operations occurred.

## Node controls, hover and wheel routing — 18 October, 01:28

Inputs and outputs alone retain Mute. Every intermediate node now has one bottom
button labelled **Bypass** in both states, grey when off and red when on. Audio
intermediate bypass skips its processing and local/linked gain; a bypassed
control contributes no gain. Incoming/outgoing cable levels and input/output
controls remain active. Saved legacy mute flags remain effective until explicit
user action. A legacy-muted intermediate displays that state; its first Bypass
click clears only its own mute while selecting bypass. No migration edits saved
settings or silently exposes a muted route.

Socket hover/snap feedback now brightens the existing port without an added
circle or altered geometry. Screen-space snapping remains generous. The wheel
zooms over closed dropdowns, text, sliders, EQ, dials, buttons and sockets; open
dropdowns and menus retain scrolling. EQ width remains available through its
numeric field and right-click presets. Passive node surfaces use Arrow, actual
text fields retain I-beam, and SizeAll appears only while holding a node or EQ
point for a drag. Release/cancellation/capture loss restores the ordinary cursor.

The [final published controller](docs/design/node-interactions-20261018-012712/results.json)
passed **1,200 detached checks and 65 renders**, with no binding errors. Actual
PreviewMouseWheel routes cover representative child controls, including a text
field's internal ScrollViewer, with unchanged audio parameters, dropdown
selection and edit/save/history counts. Tests cover all sixteen node kinds,
static-label grey/red bypass states, legacy mute recovery, active/idle cursors,
and socket-highlight geometry/cleanup at 30%, 75%, 100% and 200% zoom. Grey/red
pass-filter renders and the socket snap preview were inspected.

Native silent verification passed [271,481 graph assertions](docs/design/node-interactions-20261018-012712/node-bypass-graph-results.txt),
[7,901 qualification assertions over 4,527,360 synthetic frames](docs/design/node-interactions-20261018-012712/node-bypass-qualification-results.txt),
and [210 lifecycle commits](docs/design/node-interactions-20261018-012712/node-bypass-lifecycle-results.txt).
Coverage includes transparent stereo bypass for every intermediate kind,
multiple-input mixing, linked controls, legacy mutes, cable weights, preserved
settings/topology and reversible 10 ms level ramps. No audio endpoints were
opened by these tests.

[Activation](artifacts/node-interaction-update-20261018-012622/result.json)
started engine 41704; all four previously active paths reported Running within
0.291 seconds of the restart, and frames advanced. This is a software-state
measurement, not an audible-gap measurement. Configuration revision 2963 and
settings bytes stayed identical. A final tooltip wording cleanup was applied
by [controller-only replacement](artifacts/node-interaction-copy-update-20261018-012729/result.json),
without another engine restart. Responsive controller 48564 matches the final
verified build. The user's then-current six-node configuration at revision 2967
and settings bytes were preserved, with five Running paths and one Unassigned.
No desktop input, test signals, Windows routing/defaults or drivers were changed.
Hands-on pointer feel and audible bypass qualification remain in TESTING.md.

## Effects, EQ spectrum and node resets — 18 October, 02:04

The creation library now offers 18 kinds, with Gain and Reroute retained only
for saved-graph/clipboard compatibility. Inspection confirmed Gain and Mix both
summed incoming audio and applied gain, so **Mix** now serves as a single-source
volume control or a multi-source sum. No existing nodes or connections are
automatically replaced. Generic Level rows are removed from effects, Balance
and Mono; saved nonzero levels remain effective and visible through the node's
status/context menu until explicitly reset. Compressor makeup and Reverb Mix
remain as effect-specific controls.

EQ nodes are 620 DIPs wide, with a 300-DIP plot and separate response/spectrum
scales. Q remains the single width control, with original defaults and saved
shapes intact. Band double-click and **Reset position** restore frequency/gain
using a persisted anchor. Empty-plot **Reset EQ to flat** restores five default
bands. Every node has **Reset node to defaults**, preserving identity, name,
position, assignments and connections while restoring its sound controls.
Outputs reset to their muted new-node defaults. No-op resets do not save.

The underlay measures actual post-EQ stereo power through a 4096-sample Hann
FFT and 128 log bins. The audio thread only copies to preallocated handoff
storage; FFT work occurs in control snapshots. Frame and continuity identities
prevent stale displays after restart, delayed polling or timeline resets.
The analyzer adds no audio delay. Its display window is about 85 ms.

New Denoiser, De-esser, Plosive remover and Reverb nodes have working native
processing, editor controls, validation, serialization and clipboard support.
The denoiser uses pinned SpeexDSP 1.2.1 and adds 20 ms, including bypass. Its
16-bit boundary requires input headroom. There is no automatic delay
compensation between graph branches. Plosive remover is dynamic low-band
reduction and can also affect sustained bass; audible quality remains to be
qualified. Research, algorithms, licences and limitations are recorded in
[EFFECTS_RESEARCH.md](docs/EFFECTS_RESEARCH.md).

The [final packaged controller verification](docs/design/effects-eq-20261018-020322/results.json)
passed **1,474 detached checks and 74 renders**, with zero binding errors.
Coverage includes all 20 supported node resets, unchanged identity/assignments/
weighted connections, 18-kind creation, old/new clipboard payloads and malformed
nested settings, all 11 new effect parameter bindings, EQ resets/freshness and
the existing canvas/caption/zoom checks. New effect nodes at 100%, 75%, 50% and
144 DPI, the full EQ node and the spectrum plot were inspected. Controls remain
visible, aligned and unclipped. The spectrum fixture image is clearly synthetic
verification data; production only displays measured samples.

Silent native results:

- [49,995 advanced-effect assertions](docs/design/effects-eq-20261018-020322/advanced-effects-results.txt):
  denoiser delay/adaptation, band-selective dynamics, reverb tails/stereo,
  threshold/transient/extreme settings, parameter smoothing, bypass, state
  inheritance/reset and allocation instrumentation.
- [271,481 graph assertions](docs/design/effects-eq-20261018-020322/advanced-effects-graph-results.txt)
  and [7,901 qualification assertions](docs/design/effects-eq-20261018-020322/advanced-effects-qualification-results.txt)
  over 4,527,360 synthetic frames.
- [410 spectrum checks](docs/design/effects-eq-20261018-020322/advanced-effects-spectrum-results.txt)
  and [212 lifecycle commits](docs/design/effects-eq-20261018-020322/advanced-effects-lifecycle-results.txt),
  using synthetic endpoint workers, including engine restart continuity.

The eight-effect stereo chain measured 167 μs p99 per 5 ms quantum. The maximum
32-node graph with 30 denoisers measured 1,107 μs p99 processing and 112 μs p99
state transfer; 30 reverbs measured 570 μs and 135 μs respectively. No C++ or
Speex allocations/frees occurred during instrumented processing, inheritance,
reset or parameter edits. These are offline CPU observations, not live latency
or whole-PC scheduling guarantees. Independent native review found no blocking
ownership, stability or audio-thread allocation defect.

[Activation](artifacts/effects-eq-update-20261018-020354/result.json) replaced
engine 41704 with 39456 and controller 48564 with responsive controller 43488.
All seven previously running paths reported Running within **0.319 seconds**
of restart, and the audio frame counter advanced. This is a software-state
measurement, not an audible-gap measurement. Configuration revision 3588,
all seven nodes and the exact settings-file bytes were preserved, including
after opening the new controller. Comparison normalizes only absent optional
default fields in memory; it never rewrites the user's configuration. Both
installed executables match the verified candidates, and the Speex notice is
included. Previous binaries/settings were backed up before activation.

No desktop input, test signals, Windows defaults, device selections or drivers
were changed. No new effect was inserted into the live route. Listening quality,
physical pointer feel, hardware recovery and endurance remain explicitly
deferred to the procedure in TESTING.md.

## Effect quality audit and corrections — 18 October, 02:21

The user's report of little audible de-essing exposed a gap in the earlier
verification: broad high-frequency attenuation and finite output did not prove
effective reduction near the selected frequency. Independent quality probes
now test actual response, target-band consonant bursts, release recovery,
distortion, wet energy, latency and control extremes.

The original first-order de-esser/plosive split leaked target energy through
the untouched band. At its crossover, even unlimited selected-band attenuation
could reduce the complete signal by only 3.01 dB. Both processors now use
second-order detectors and dynamic TPT shelves. At a 6 kHz midpoint with 24 dB
range and a strongly triggered detector, the de-esser measures 12.00 dB
attenuation instead of 2.99; at 10 kHz it measures 20.32 instead of 6.41 dB.
Residual attenuation 200 ms after a consonant fell from 3.57 to 0.25 dB.
Zero-range/below-threshold transparency, opposite-band voice preservation and
stereo linking remain intact. There is no added frame buffer or automatic
threshold adjustment. [De-esser audit](docs/DEESSER_QUALITY_AUDIT.md).

The original reverb also had excessive wet gain: a 0.5-peak 40 Hz input with a
long decay produced an 8.727 peak before sink clipping. Passive scattering delay
paths and true allpass diffusion now bound fixed-setting frequency/energy gain
while retaining useful wet volume. The same regression peaks at 0.627; this is
not an instantaneous peak limiter. EQ/tone/pass filters, Balance and Mono match
their expected responses. Compressor static-curve and distortion checks pass,
with documented waveform-dependent ballistics. Denoiser suppresses steady
noise but can treat sustained musical tones as noise; its 16-bit boundary and
20 ms delay remain explicit. [Other-effects audit](docs/EFFECTS_QUALITY_AUDIT.md).

De-esser, Plosive remover and Compressor now show actual Reduction and a thin
bar in the existing status slot. Hover gives detector level versus threshold;
Below threshold explains an enabled processor doing no reduction. Telemetry
comes from DSP state, excludes makeup, and is transient. The UI holds fresh
duplicate-frame data, expires stalled readings after 250 ms and clears on
bypass, mute or loss of audio. It does not add scalar reduction meters to
denoiser/reverb or change node dimensions. Existing clipping/error and legacy
level messages retain priority.

The [published UI verification](docs/design/effects-audit-20261018-021825/results.json)
passes **1,543 checks and 77 renders**, zero binding errors. The actual reduction
render was inspected. The [dedicated quality probes](docs/design/effects-audit-20261018-021825/native-results.json)
pass **62 de-esser/plosive and 532 other-effect quality checks**. Raw before/after
measurements are preserved beside that manifest. These probes use generated
audio in memory and read effect output before sink saturation.

Additional rebuilt regression suites passed:

- 49,999 advanced-effect assertions, including allocation-free processing,
  inheritance/reset, extremes and maximum-size graph CPU budgets.
- 271,495 graph assertions, including new truthful dynamics telemetry tests.
- 7,901 qualification assertions over 4,527,360 synthetic frames.
- 410 spectrum checks and 212 stub-worker lifecycle commits.

The eight-effect chain measured 212 μs p99 per 5 ms block. Thirty de-essers
measured 297 μs p99 and thirty plosive processors 325 μs; thirty reverbs measured
709 μs. These are silent synthetic CPU measurements, not end-to-end latency
claims. Independent final review found no blocker in the shelf equations,
reverb energy argument, buffer bounds, state transfer/reset or feedback semantics.
The source build completed without compiler warnings.

[Activation](artifacts/effects-quality-update-20261018-022058/result.json)
started engine 48740 and responsive controller 44848. All eleven previously
running paths returned to Running within 0.322 seconds of restart, frames
advanced and actual dynamics fields were present. This measures software state,
not the audible interruption. Revision 3788, all settings/routes and exact
settings bytes stayed unchanged, including after opening the controller. Both
installed binaries match the verified candidates. No test signal, recording,
device/default/driver change or desktop input occurred. Real-voice listening
quality and the earlier deferred hardware/endurance tests remain unqualified.

## Level/EQ dials, readable hover help and Shure research — 18 October, 02:38

All generic Level controls in the node workspace now use the same rotary design
as Low / Mid / High. The EQ has three equal-width dial columns for frequency,
band gain and width/Q under its existing 300-DIP plot. Frequency and Q use
logarithmic travel; exact typed values are preserved beyond display precision.
Editing exposes the round-trip numeric value, so Enter alone cannot round a
saved value. Double-click resets a dial; EQ frequency uses the selected band's
saved reset anchor. Empty/unsupported band controls disable without reflow.
The normal pointer and canvas wheel-zoom behavior remain intact.

Hover help has a short heading and separate instruction lines in a narrow dark
popup, with grayscale text. Longer explanations live in the Guide. Full device
errors still wrap without truncation. Static tooltip trees are reused, and EQ
hover does not rebuild its tree for every move within the same band.

[Shure research](docs/SHURE_DENOISER.md) establishes the supported reuse path:
configure the MV7+'s onboard Real-time Denoiser in MOTIV Mix, then capture its
physical USB endpoint. The Guide explains this path and distinguishes it from
our SpeexDSP node. No supported public SDK or redistributable Shure algorithm
was found. Hardware state was not queried or changed; persistence and listening
quality on this exact firmware remain later checks, not measured claims.

The final staged build completed without warnings or errors. The detached run
passed **1,598 checks**, rendered **79 images**, and reported **zero binding
errors**: [results](docs/design/dials-tooltips-20261018-023804/results.json).
Coverage includes rotary ranges, fine drags, logarithmic mapping, exact entry,
idempotent resets, selected-band changes, accessible values, wheel routing,
tooltips, fixed node geometry, zoom, DPI and high contrast. Earlier failed runs
exposed test-harness assumptions about old slider names/templates and parented
ToolTips; those assumptions were corrected before the passing final run.

Reviewed renders: [EQ](docs/design/dials-tooltips-20261018-023804/eq-closeup.png),
[tooltips](docs/design/dials-tooltips-20261018-023804/tooltips.png),
[microphone](docs/design/dials-tooltips-20261018-023804/microphone-stereo-closeup.png),
[high contrast](docs/design/dials-tooltips-20261018-023804/eq-high-contrast-closeup.png).
These are detached WPF renders, not desktop screenshots or real pointer tests.
Native popup placement and hands-on gestures remain in TESTING.md.

[UI activation](artifacts/dials-tooltips-update-20261018-023836/result.json)
opened responsive controller 48280 while keeping engine 48740 and its binary
unchanged. Frames advanced from 50,761,200 to 50,773,680 during replacement;
all eleven paths reported Running. Revision 3796, full configuration and exact
settings bytes remained unchanged, including after the new UI opened. The
installed UI matches the verified candidate SHA-256
`2E5DB2FAA7A44C7FC0ADD770C19B168CB85BE0B242E8E660CA1AAA1391B31B7D`.
No audio device, driver, Windows default, Shure setting, recording, test signal
or desktop input was used for verification. The deferred soak remains deferred.

## General maintenance — 18 October, 02:58

See [the maintenance review](docs/MAINTENANCE_REVIEW.md) for findings, before/after
measurements, code cleanup and exact verification scope. Layout-only saves no
longer rebuild effect state. Inert filters, JSON-based copies, repeated selector
rebuilding and repeated EQ text layout were removed from frequent paths. Recovery,
missing-primary backup loading, spectrum restart and control-connection races
received targeted fixes. The retired console UI was removed while its saved
configuration compatibility remains.

The final staged Release build and all ten offline native suites passed.
[Detached verification](docs/design/maintenance-final-20261018-025804/results.json)
passed **1,982 checks**, rendered **79 images** and reported **zero binding
errors**, including 34 private control-pipe checks. Representative actual WPF
renders were reviewed for workspace, EQ spectrum, hover help and scaled effects.
No desktop input or real audio test signal was used.

[Activation](artifacts/maintenance-update-20261018-025837/result.json) passed:
engine 43552, responsive controller 26784, eleven paths Running and frame counter
advancing. Software recovery to Running took 0.307 seconds; audible interruption
was not measured. Full configuration, revision 3807 and exact settings bytes
were unchanged. Both installed binaries match the verified candidates. Physical
latency, hands-on device recovery and the deferred soak remain later qualification.
