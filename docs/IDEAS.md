# VineMix development ideas

A working log for rough ideas, researched options, and possible future features.
Entries are proposals, not delivery commitments or instructions to implement.
Existing plans elsewhere in `docs/` remain separate unless an idea links to them.

Reported defects and usability limitations belong in [known issues](KNOWN_ISSUES.md).
Small refinements to existing controls and layout belong in [tweaks](TWEAKS.md).
On 2026-10-24, IDEA-012 and IDEA-026 through IDEA-028 moved to known issues as
ISSUE-002 through ISSUE-005. Their original anchors remain below for existing
links; their previous priorities and research are retained in the issue entries.
The 2026-10-25 classification review moved IDEA-046's branding follow-ups and
the presentation-only portions of IDEA-011 and IDEA-032 to tweaks. New processing
and telemetry capabilities remain here, with links to the separate refinements.

Implemented items are removed from this active list and retained in the
[implemented ideas archive](IDEAS_IMPLEMENTED.md). IDs are never reused.
Entries still awaiting live/release qualification, or retaining unselected
alternatives, can remain here with an explicit source-complete status until closeout.
Cleanup on 20 October 2026 archived IDEA-007 through IDEA-010; their remaining
manual qualification notes are preserved there.

The maintainer's 25 October 2026 website scroll-story direction is recorded as
[IDEA-049](IDEAS_IMPLEMENTED.md#idea-049). It was explicitly authorized for local
implementation after the website cleanup; browser verification and maintainer
review remain distinct from publication.

The next website exploration is [IDEA-050](#idea-050): a section showcasing
existing effects above the open-source/beta information. Concept images are
authorized; the implementation direction remains to be selected.

[IDEA-051](#idea-051) records the matching quick-access header exploration.
The maintainer selected the quiet-frame header, now implemented locally with
visual review pending. A later correction replaces the two hero actions with
one Download link and gives header Docs a more prominent pill; pause remains removed.

<a id="release-0-5-0-plan"></a>
## Current 0.5.0 scope and implementation status

**Released — 6 November 2026:** The accepted 0.5.0 scope is published as
[VineMix 0.5.0 Beta](releases/0.5.0-beta.md), including Distortion (IDEA-002),
selected Trigger/Wet ports (IDEA-052), optional minimap (IDEA-053), integration
and status work (IDEA-054), Echo (IDEA-055), shared output devices (IDEA-056),
keyboard/navigation refinements and the seven-node voice starter. The final
interface uses one Effects menu and Quiet Blocks. Unselected alternatives and
broader future ideas remain proposals. [Final qualification](RELEASE_0.5.0_QUALIFICATION.md)
records shipped source, package checks, publication and remaining beta limits.
The dated planning and local-integration history below is retained for rationale.

**Audited: 2026-11-04.** This section is the authority for 0.5.0 scope and
recommended delivery order. Stable idea, issue and tweak entries remain the
owners of detailed requirements, research and dated evidence. The older backlog
priority table below does not override this release order.

**Release intent:** Make existing graphs easier to navigate and edit, preserve
reliable editing across the interface/engine boundary, and add focused audio
routing and creative effects. Keep this a bounded improvement to the current mixer.

**Authorized first-run refinement — 2026-11-06, verified and locally integrated:**
Use the seven-node voice starter recorded under [IDEA-031](#voice-focused-starting-graph-proposal--2026-11-06),
with evenly spaced processing nodes and stacked outputs. This replaces the
three-node default for new/explicitly reset workspaces only; existing saved
graphs and device/output policies remain intact.

**Current presentation selection — 2026-11-05, implemented, offline verified
and locally integrated:** The maintainer selected **Quiet Blocks** (concept 1)
under [TWEAK-025](TWEAKS.md#tweak-025). At the existing low-detail transition near
30%, retain plain solid charcoal node bodies and category-coloured header strips,
with no decorative type symbols, name/control text, miniature controls or meters.
Keep selection/device-warning indicators, node bounds, ports, cables and edit
guards; zooming in restores full controls. This supersedes
all symbol and control-silhouette directions below. No additional hover or
selection behavior is part of the selection. Earlier verification and local
integration evidence remain historical. Quiet Blocks passed 195 focused detached
checks covering all 34 real kinds and existing zoom/draft/reveal guards, plus
normal/high-contrast screenshot review and a production build with unchanged
engine. Guarded interface-only activation through the actual shortcut passed
eight seconds of observation, preserving the same engine, physical settings,
preferences, configuration, advancing frames and running paths without rollback
or restart. Maintainer visual follow-up and final release qualification remain.

**Earlier symbol refinement — 2026-11-05, now superseded:** The maintainer requested slightly larger,
thicker and more refined symbols. The 128-DIP/2.6-stroke update and simplified
internal details are offline verified and locally integrated under
[TWEAK-025](TWEAKS.md#tweak-025). Targeted symbol/zoom checks and all 34 real-node
probes pass; UI-only integration preserved the same engine and settings.
Maintainer visual follow-up remains. The initial symbol verification and local
build below remain historical evidence.

**Earlier symbol-colour correction — 2026-11-05, now superseded:** Replace bright white with
the earlier control-silhouette shade `#0B0D11`, retaining the refined shapes,
size/stroke and system foreground in high contrast. Offline verified and locally
integrated under [TWEAK-025](TWEAKS.md#tweak-025), preserving the same engine and
settings. Earlier white-colour requirements are superseded; follow-up review remains.

**Authorized scope extension — 2026-11-05, implemented, offline verified and locally integrated:** Before
the final release audit, checks and documentation pass, add Simple Effects and
Advanced Effects categories ([TWEAK-024](TWEAKS.md#tweak-024)), Echo
([IDEA-055](#idea-055)) and Distortion ([IDEA-002](#idea-002)). The maintainer
selected a continuous subtle-to-strong range for Distortion. The original extension
also replaced zoomed-out control silhouettes with a centred soft-white type
symbol ([TWEAK-025](TWEAKS.md#tweak-025)), later darkened and now superseded by
Quiet Blocks above. Coloured headers, hidden meters, fixed node/socket geometry
and existing detail/edit guards remain part of the current contract.
The prior recommendation to freeze features is superseded by this explicit
extension. Earlier qualification counts describe the earlier candidate, not
these additions. The new candidate passed 19 native suites (359 focused creative
checks), 15,164 detached UI checks/317 renders/zero binding errors, 732 native/
desktop boundary checks and 111 activation fixtures. All 34 node types passed
the separate symbol geometry/theme probe. Guarded paired activation through the
trusted shell and actual shortcut passed, preserving settings, preferences and
configuration and restoring the previously running paths. See the
[execution report](IMPLEMENTATION_0.5.0.md) for quality/timing limits. Maintainer
visual/listening follow-up and final release qualification remain; nothing was published.

**Current checkpoint — maintainer feedback, 2026-11-04:** The previously
integrated baseline remains unreleased. The maintainer reports successful manual
checks of minimap navigation and visibility persistence; Tab/Shift+Tab control
navigation; Ctrl+Tab pipeline navigation; arrow nudging with undo and arrow
panning; F2 rename and Find by custom name/node type; direct dial entry and Page
Up/Down; decimal-edit undo; both Trigger cases; and Reverb Wet independence from
Mix, its other controls and Bypass. These are maintainer-reported results, not
independently observed tests or comprehensive accessibility/listening qualification.
The checklist's Reverb Mute instruction was incorrect: Reverb has Bypass, not a
Mute control. No new Mute button is requested.

**Simplified meters and centring — 2026-11-04, implemented, offline verified and
locally integrated:** Hide meters in
both Compact and Overview at the existing low-detail transition below 30%,
respecting hysteresis and active-edit guards. Centre passive controls with
symmetric 16-world-DIP horizontal padding, without reserving a meter column.
Keep the global 72-DIP dial diameter, dark shade, coloured headers, full-detail
meters and stable node/socket geometry. The earlier policy retained Compact
meters and reserved their column; that policy is now superseded. The suite passed
14,752 checks / 314 renders / zero binding errors. The updated mixed-node fixture
checks meter visibility at both simplified levels and full detail, horizontal
centring, shared dial size and unchanged geometry/history. Reviewed 25%, 10%,
named-frame and 100% screenshots confirm hidden simplified meters and their
full-detail restoration. Guarded activation preserved the same engine process/
start time/path/hash and settings through eight seconds without rollback.
This remains unreleased; maintainer follow-up is pending.

**Corrected node-detail sizing — 2026-11-04, implemented, offline verified and
locally integrated:** Use one fixed,
modestly larger 72-world-DIP dial diameter across all simplified nodes, with
regular spacing. Circle size must not grow with node area. The maintainer
clarified that filling the entire box was not the intended request; the
oversized layout below is superseded. Keep the minimal shape types, dark shade,
full-detail geometry and existing navigation behavior. The suite passed 14,752
checks / 314 renders / zero binding errors. Its updated mixed 32-node fixture
checks every simplified circle against the common 72-DIP diameter across node
types. Reviewed 25%/10% and named-frame screenshots confirm matching single- and
multi-dial sizes with compact spacing. Guarded activation preserved the same
engine process/start time/path/hash and settings/preferences through eight seconds
without rollback. This remains unreleased; maintainer follow-up is pending.

**Earlier interior-filling layout — 2026-11-04, locally integrated, now
superseded:** Simplified controls filled the padded node interior, with
every dial circle within a node the same size, independently of full-detail
sizes. The minimal shape types, darker shade and full-detail geometry remain
unchanged. The existing suite passed 14,752 checks / 314 renders / zero binding
errors. Reviewed 25%/10% and varied-control screenshots show the dynamics node's
five equal circles in a 2/2/1 layout; this is visual evidence, not new automated
multi-dial coverage. Guarded activation preserved the same engine identity/hash
and settings/preferences through eight seconds without rollback. Maintainer
follow-up remains pending under ISSUE-005; the change is unreleased.

**Minimal dark silhouettes — 2026-11-04, implemented and locally integrated:** Simplify silhouettes
to dial circles and main selector/plot blocks only; remove numeric-entry bars and
all button/Bypass blocks. Shapes use a dedicated `NodeSimplifiedBrush`, normally
`#0B0D11` against the `#15181E` node background, with `WindowTextBrush` in high
contrast. The minimap remains `#4A5058`. Preserve coloured headers, geometry,
connections, hover/Find names and full controls on zoom-in. The previous matching
brighter shade and extra blocks below are superseded. The existing detached suite
passed 14,752 checks / 314 renders / zero binding errors, with screenshots and
exact darker-than-background pixels reviewed. Guarded interface-only activation
preserved the same engine identity/hash and settings/preferences through eight
seconds without rollback; all 36 inventory entries passed. This remains unreleased.

**Earlier node-detail direction — 2026-11-04, locally integrated:** Replace zoomed-out
centred names with low-detail silhouettes of the actual controls, such as dial
circles and field/button rectangles, following the supplied ComfyUI reference.
Keep header colours, geometry and connections; names remain available through
tooltips/Find and full controls return when zooming in. This supersedes the
fitted-name direction below, not the minimap shade or group-header behavior.
The detached suite passed 14,752 checks / 314 renders / zero binding errors;
25%/10% and varied-control screenshots were reviewed. Shapes are passive and port
text hides too. Guarded interface-only activation preserved the same engine
identity/hash and unchanged settings/preferences through eight seconds without
rollback. Mode-handling review remains source evidence, not additional live
interaction proof. Maintainer follow-up remains pending under ISSUE-005;
the change is unreleased.

**Earlier node-header correction — 2026-11-04:** Keep the coloured header bar
on simplified nodes; hide only its small title text. The centred actual/custom
name remains unchanged. Group-header behavior is unchanged. This narrow
ISSUE-005 correction is implemented, offline verified and locally integrated.

**Subsequent meter correction — 2026-11-04:** Restore the full meter border and
original peak indicator, including its idle cap. Only the tiny red sliver was
unwanted. The gradient is inset 1 DIP inside the restored border, preserving
actual clipping and peak hold/history. This TWEAK-021 correction is implemented,
offline verified and locally integrated. The earlier removals below are superseded.

**Latest correction qualification — 2026-11-04:** Both corrections passed
**14,748 detached checks, 314 renders and zero binding errors**, including
525 focused checks. Guarded interface-only activation through the
trusted shell and actual shortcut passed, preserving the same engine process,
start time, path and hash and unchanged settings/preferences through eight seconds
without rollback. Maintainer follow-up remains pending; this is unreleased
0.5.0 work. The 14,726-check evidence below belongs to the preceding candidate.

The preceding feedback candidate implemented these changes:
remove Escape cancellation of arrow nudges while retaining ordinary draft
cancellation; add Ctrl+plus/minus canvas zoom; simplify node/group headings;
darken the minimap and remove its header/title; remove the idle clipping strip
and unintended red top seam while preserving actual clipping indication; and
correct [ISSUE-025](KNOWN_ISSUES.md#issue-025) so Settings uses available height
while retaining scrolling for genuinely small work areas. All feedback
refinements passed source review and the final offline suite:
**14,726 detached checks, 314 renders, zero binding errors**.

Final guarded interface-only activation passed through the trusted shell and
actual shortcut. It preserved the same engine process/start time/path/hash,
current configuration, saved settings/preferences, advancing frames and running
paths through eight seconds of observation, with no rollback. Final independent
audit verified all 36 installed payload hashes and matching source/build receipt/
inventory identity; the final workspace screenshot was reviewed. Maintainer follow-up review
of the integrated refinements remains pending. Release identity and published
downloads stay **0.4.0 Beta**; this is unreleased 0.5.0 development.

**Earlier feedback checkpoint — 2026-11-04:** The 14,665-check / 309-render
pass preceded the Settings and final red-seam corrections. It is historical;
the final qualification above supersedes it.

**Earlier source and local integration checkpoint — 2026-11-04:** All items
accepted at that checkpoint were implemented,
verified offline and locally integrated for **unreleased 0.5.0**. The release
identity and published downloads remain **0.4.0 Beta**. The combined run passed 18 native
suites, 14,583 detached UI checks with 305 previews and zero binding errors,
712 production serialization/history boundary checks, 51 controller-identity
checks and 97 UI-activation fixtures. These isolated checks do not establish
native keyboard/Narrator interaction or listening results. The
[execution report](IMPLEMENTATION_0.5.0.md) owns commands and detailed evidence.

The maintainer separately authorized source implementation/testing after the
planning audit, then engine restarts and invasive testing. Guarded paired
activation through the trusted Windows shell and actual shortcut passed: the
engine became ready in 0.247 seconds, all 10 previously running paths returned,
and frames advanced. The final keyboard correction used an interface-only
update that preserved the same engine process and hash. Each update preserved
its then-current saved settings/preferences and verified the physical settings
file; newer workspace edits between updates were retained. Both settings
views and recovery backups remain outside the checkout. Final installed payload
hashes match 36/36 entries, with source receipt and inventory aligned. This
local integration does not publish a release or establish listening results
for the new Trigger/Wet routes. Actual keyboard/IME, Narrator and pointer/DPI usability
remain separate qualification. Computer-use tools remain excluded.

### Accepted scope and completion evidence

#### Reliable editing and maintainable implementation

| Work | Tracking | Completion evidence required |
| --- | --- | --- |
| Preserve gain precision through saved settings and undo history | [ISSUE-024](KNOWN_ISSUES.md#issue-024) | Decimal node/cable edits survive real serialization, acknowledgement, save/reload and undo/redo; genuine conflicting changes are still detected |
| Add production desktop/native boundary tests in CI | [IDEA-054](#idea-054) | A regression fails before the precision fix and passes after it, using the actual serializers and acknowledgement/history path; retain fast tests and separate full-engine qualification |
| Harden local UI activation identification | [ISSUE-011](KNOWN_ISSUES.md#issue-011) | Startup/exit cases are handled without accepting an unidentified process or weakening identity, settings, backup and rollback safeguards |
| Replace behavioural status-message matching with stable reason codes | [IDEA-054](#idea-054) | Reasons survive native/desktop serialization; wording changes do not change behaviour; unknown reasons and recovery transitions remain usable |
| Refactor workspace/node views incrementally | [IDEA-054](#idea-054) | Small, separately reviewable extractions preserve layout, focus, gestures, history and subscriptions; no framework rewrite or unrelated cleanup |

#### Canvas navigation and readable presentation

| Work | Tracking | Completion evidence required |
| --- | --- | --- |
| Smooth green/yellow/red meter gradients | [TWEAK-021](TWEAKS.md#tweak-021) | Fixed-scale colours remain meaningful at low/high levels; restore the full border and original peak/idle indicator, removing only the tiny red seam with an inset gradient; preserve actual clipping, readings, timing and high contrast |
| Settings menu uses available height | [ISSUE-025](KNOWN_ISSUES.md#issue-025) | Fixed-cap defect resolved locally and integrated; full menu fits available space while constrained work areas retain scrolling and keyboard access; follow-up review pending |
| One flat Effects menu | [TWEAK-024](TWEAKS.md#tweak-024) | Reunite the earlier Simple/Advanced split; creation/search/help share one catalog and existing kinds, saved graphs and header colours remain compatible |
| Secondary-output colours, cable gradients and midpoint markers | [TWEAK-026](TWEAKS.md#tweak-026) | Light-blue effect-return audio sockets retain their shape; cables blend between actual socket colours; markers follow full-detail visibility and stay under nodes; preserve selection/mute/high contrast and interactions |
| Quiet Blocks at low zoom | [TWEAK-025](TWEAKS.md#tweak-025) | Plain charcoal bodies and category-coloured headers across all kinds, without decorative symbols/name/control text/mini-controls/meters; retain selection/device warnings, geometry/ports and full-detail restoration; offline verified and locally integrated, maintainer follow-up pending |
| Zoom out further and progressively simplify nodes/groups | [ISSUE-005](KNOWN_ISSUES.md#issue-005) | Keep verified 10–200% range, guarded detail transitions, hidden simplified meters and fixed node/socket geometry; Quiet Blocks supersedes symbols under TWEAK-025 |
| Ctrl+plus / Ctrl+minus canvas zoom | [ISSUE-005](KNOWN_ISSUES.md#issue-005) | Reuse guarded canvas zoom and preserve focused editors' native shortcuts; verify alongside existing reset, wheel and Fit controls |
| Bottom-right minimap showing the visible canvas region | [IDEA-053](#idea-053) | Accurate viewport rectangle through pan/zoom/resize and a saved **Show minimap** setting; manual navigation/persistence reported successful; darker title-free refinement offline verified and integrated, follow-up review pending |

#### Keyboard editing and accessibility

| Work | Tracking | Completion evidence required |
| --- | --- | --- |
| Ctrl+Tab / Ctrl+Shift+Tab follow the pipeline; Tab / Shift+Tab traverse controls | [ISSUE-003](KNOWN_ISSUES.md#issue-003) | Predictable branches/endpoints, off-screen reveal and focus return; no trapped focus or accidental numeric commits |
| Arrow keys nudge selected nodes, otherwise pan | [ISSUE-003](KNOWN_ISSUES.md#issue-003) | Canvas-focus-only behaviour, coherent group/multi-selection moves, layout-lock protection and one undo step per held-key gesture; focused controls retain their arrows |
| F2 renames exactly one selected node or group | [TWEAK-022](TWEAKS.md#tweak-022) | Multiple/empty/non-renamable selections do nothing; one selected group is one logical target |
| Typing on a focused dial starts manual value entry | [TWEAK-023](TWEAKS.md#tweak-023) | First character retained, signed/decimal entry supported, validation and cancellation preserved; no audio change from an incomplete draft |
| Refresh the screen-reader name after rename | [ISSUE-003](KNOWN_ISSUES.md#issue-003) | Rename/undo/redo metadata verified in detached checks; actual screen-reader announcements still require qualification |

#### Focused audio additions

| Work | Tracking | Completion evidence required |
| --- | --- | --- |
| Gate and Expander Trigger inputs | [IDEA-052](#idea-052) | Unconnected nodes retain self-detection; connected trigger affects the main signal without being mixed into it; silent/missing/muted trigger cases, timing and stereo behaviour specified and tested |
| Separate Reverb Wet output | [IDEA-052](#idea-052) | Wet signal is independent of Mix, shaped by Decay/Damping/Pre-delay, and correctly routed/metered as stereo where appropriate; existing Audio output and saved routes retain their behaviour |
| Echo | [IDEA-055](#idea-055), [ISSUE-026](KNOWN_ISSUES.md#issue-026) | Exact Time/Repeats/Mix controls, finite decaying copies, fresh timing/count edits, separate echo-only output, bounded real-time processing and legacy-feedback migration; offline verified and locally integrated |
| Distortion | [IDEA-002](#idea-002) | Drive/Tone/Mix/Output from warmth to strong saturation, measured alias/DC/headroom behavior, aligned dry/wet/bypass, stereo and persistence/reset/history |
| Several output nodes for one device | [IDEA-056](#idea-056) | Independent controls; canonical endpoint ownership; one summed stream and final clip boundary; reconnect/alias/fade handling; duplicate/paste/undo/persistence coverage |

Page Up/Down larger-step dial adjustment and search by custom name plus node type
already exist. Preserve and verify them; do not count them as new features.
The activation-helper correction is development tooling, not a new in-app updater
feature. Refactoring and reason-code work address source maintenance risks, not
additional reproduced user defects.

### Settled implementation contracts — 2026-11-04

These contracts include the maintainer's later manual-feedback decisions.
The feedback refinements, including the Settings-menu correction, passed offline
verification and guarded local integration; maintainer follow-up review remains.
The later Quiet Blocks selection under TWEAK-025 supersedes control silhouettes,
dial sizing and type symbols; it is offline verified and locally integrated. Earlier silhouette,
symbol and meter-visibility implementation evidence remains in dated entries.
Offline verification and maintainer-reported results are distinct evidence.

| Decision | Accepted behavior and remaining qualification |
| --- | --- |
| Lower zoom and compatibility | Desktop uses 10–200%; native accepts 10–400%, retaining its historical upper bound. Older builds may reject new low zoom or ports; transparent downgrade is not promised |
| Low-detail thresholds and editing | Progressive detail with hysteresis preserves node bounds/socket anchors. Quiet Blocks retains solid charcoal bodies and coloured header strips, with no decorative type symbols, name/control text, miniature controls or meters. Existing selection/device-warning indicators remain. Zooming in restores full detail. Existing interactions and high contrast remain; no new hover/selection presentation is added. Group headers keep fixed geometry and their existing simplification. Active edits defer hiding; navigation reveals controls before focus. Overview sockets cannot be snapped to. Quiet Blocks is offline verified and locally integrated; maintainer follow-up remains |
| Canvas zoom shortcuts | Add Ctrl+plus and Ctrl+minus through the existing guarded zoom path; keep Ctrl+0 reset and native focused-control behavior |
| Minimap visibility and bounds | Default-on saved Show minimap toggle, bottom-right overview, click-to-centre and drag-to-pan. Mapping stays stable during dragging and includes the visible region outside node bounds. Darker presentation with no header/title is accepted; optional wheel zoom remains deferred |
| Pipeline branches and endpoints | Follow main audio edges, including muted routes, so silent paths can still be inspected. At splits/merges show a small named branch chooser; retain the last traversed edge for a predictable reverse step. At endpoints stay put with brief feedback. Disconnected nodes remain reachable through Find/ordinary focus; no automatic wrapping into unrelated pipelines |
| Focus, selection and drafts | Navigate from the focused node, or a single selected node when canvas-focused; multiple selections need an explicit starting-node choice. Ordinary Tab can leave the node/canvas. Menus, dialogs and controls keep their native keys. Before pipeline navigation, commit only a valid pending numeric draft; invalid/incomplete drafts retain focus with feedback and Escape cancellation |
| Nudge/pan distances | 10 world DIPs per nudge, Shift for 1-DIP fine nudging, and 40 screen DIPs per pan step. One held key is one guarded undo step. Remove Escape-to-cancel nudging at the maintainer's request; ordinary Escape cancellation of text/value drafts remains. Locked selected nodes do not switch to pan |
| Trigger disconnect versus silence | No cable means self-detection. A connected silent, muted or unavailable source means a silent detector, not automatic fallback to the main input. Deleting/disconnecting the cable restores self-detection. Preserve attack/hold/release and identify unavailable sources clearly |
| Reverb Wet bypass/mute/tails | Wet is independent of Mix and stereo where appropriate. The UI's Bypass passes dry Audio and fades Wet to silence without dry leakage. The model's retained muted state silences both outputs, but Reverb has no Mute control and this was not a reported manual test. The network advances its tail during bypass/mute; resuming fades in the current result. Explicit reset clears the tail |
| Stable reasons and bounded refactoring | Separate stream/device status reasons from configure-command error codes. Start with already identified behavioural text matches. Keep structural extraction separate from feature/fix commits and scoped to touched components; stop once the agreed seams and regression checks are delivered |

**Superseded contract, earlier 2026-11-04:** Arrow nudges previously supported
Escape cancellation and passed the corresponding held-key regression. The later
maintainer decision removes that feature; ordinary text/value draft cancellation
is retained. Earlier test counts describe the earlier implementation.

### Recommended delivery order

1. **Establish correctness:** Add the failing production-serialization/history
   regression, then correct gain precision and qualify genuine conflicts. Harden
   activation identity before relying on it for later live interface work.
2. **Prepare only necessary boundaries:** Define reason-code/schema compatibility
   and perform bounded workspace/node-view extractions, in separate changes.
   Do not make every later feature wait for a broad cleanup project.
3. **Deliver the canvas/keyboard work together:** Resolve focus and selection
   contracts first, then extend shared zoom bounds, simplified rendering,
   minimap/settings and pipeline navigation. Add F2/direct entry and qualify
   their interactions. Meter gradients can be a small independent change.
4. **Add the audio ports:** Agree detector and Wet-output contracts, implement
   desktop/native ports together, and verify old/new graphs, channel semantics,
   bypass, mute, missing sources and undo/copy/paste. Keep Gate/Expander and Reverb
   separately reviewable even if they share port infrastructure.
5. **Qualify the combined candidate:** Run the acceptance checks above and relevant
   existing suites; inspect real keyboard/pointer/Narrator behaviour, low-zoom
   rendering and audio continuity. Compare against the same baseline and example
   graphs before claiming a performance improvement. Preserve installed settings
   and qualify matching source/binaries separately from publication.

### Scope limits and release readiness

- Keep the existing 32-node/96-connection limits. More zoom is not authorization
  for larger graphs, new frameworks, a general modulation system or a full
  screen-reader graph inspector.
- Audio File input, Recording output, Desktop Audio, Compressor Trigger, Mix
  pre-Level, network/plugin integration and a Reverb Mix-label change remain
  outside confirmed 0.5.0 scope. Optional minimap extras are also deferred unless
  separately selected. All accepted work above remains in scope.
- Full accessibility compliance is not established by a renamed label and a few
  shortcuts. Complete focused workflow checks and report remaining limitations
  accurately; unresolved ISSUE-003 cable-inspection work is not silently added.
- Compatible existing workspaces must load with unchanged audio and preserve
  settings/history where applicable. Explicitly define what older builds do
  with new ports and zoom values; do not promise transparent downgrade support.
- The final package must have matching source/build identity, regression and
  integration evidence, release notes, checksums and signed update metadata.
  No version bump or publication is authorized by this work. Authorized guarded
  local activation passed, and the maintainer later reported the focused manual
  successes above; comprehensive listening/native accessibility qualification
  and maintainer follow-up review of the integrated refinements remain incomplete;
  repository visibility remains a separate maintainer decision.

### Audit conclusions — 2026-11-04

The plan is coherent as a graph editing and routing release. Its largest risks
are interaction between zoom/focus/selection, new audio-port semantics and an
open-ended refactor. Preserve the accepted features, bound the refactor and
qualify those behaviours before adding optional extras. Tests and reason codes
support the release rather than competing as separate user features.

This audit reorganized the previously flat 15-item list without dropping accepted
work, removed obsolete "awaiting agreement" language from current scope, separated
source-only completion from release qualification, exposed the native 15% zoom
limit and distinguished stream status reasons from command errors. Detailed
historical proposals and dated evidence remain in their stable entries. Source
was reviewed for these findings; no external research was refreshed and no app
code, tests, live process, settings or release identity changed during the audit.

## How this log works

Capture the useful intent behind each idea, check it against the current project,
and research relevant options where that will help a future decision. Keep facts,
assumptions, and open questions distinct. Include dated primary-source links for
external research; explicitly mark research that is still pending.

Use IDs such as `IDEA-001` and statuses such as **Captured**, **Researched**,
**Needs decision**, **Planned**, or **Parked**; move completed entries to the
archive. Update related entries instead of adding duplicates. A status of Planned
requires an explicit decision to pursue the idea.

## Priority and review order

**Reviewed: 23 October 2026.** The maintainer requested this consolidated backlog
after a review of all 31 earlier project chats. Prioritize ease of use and everyday
quality of life before adding large audio or integration features. The order below
is a proposed investigation/design sequence, not approval to implement or a release
commitment. IDs remain stable regardless of priority or document placement.

- **P1 — First:** focused, frequently useful controls and clear feedback.
- **P2 — Next:** workspace organization, learning and accessible interaction.
- **P3 — After the core workflow:** deeper diagnostics and useful new capabilities.
- **P4 — Later:** broad research candidates and advanced extensions.
- **Parked:** retain the direction, but do not actively pursue it without a new decision.

The table is the priority authority for both this file and
[later ideas](IDEAS_LATER.md). Existing dated research is retained; it was not
refreshed in this review. New entries use repository inspection on 2026-10-23;
external feasibility research is pending unless an entry explicitly says otherwise.
Earlier assistant suggestions are labelled separately from confirmed maintainer
preferences. Completed work stays in [the archive](IDEAS_IMPLEMENTED.md).

| Priority | ID | Idea | Status |
| --- | --- | --- | --- |
| P3 | [IDEA-052](#idea-052) | 0.5.0 node inputs and outputs exploration | Accepted ports integrated; focused manual passes reported; unreleased; other ports exploratory |
| P2 | [IDEA-053](#idea-053) | Bottom-right canvas minimap | Baseline manual passes reported; darker, title-free refinement offline verified and integrated; follow-up review pending; unreleased |
| P2 | [IDEA-054](#idea-054) | Integration coverage, incremental refactoring and stable status reasons | Offline verified and locally integrated; unreleased |
| P3 | [IDEA-055](#idea-055) | Echo effect and echo-only output | Researched refinement offline verified and locally integrated for unreleased 0.5.0 |
| P2 | [IDEA-056](#idea-056) | Multiple output nodes for the same device | Offline verified and locally integrated; unreleased 0.5.0 |
| P2 | [IDEA-022](#idea-022) | Save selected node sections as User Presets | Captured |
| P2 | [IDEA-024](#idea-024) | Guided introduction to nodes, noodles and navigation | Captured |
| P2 | [IDEA-025](#idea-025) | Why no sound? with a highlighted blocking node | Needs decision |
| P2 | [IDEA-005](#idea-005) | Pin nodes and tasks to the top of the window | Researched |
| P2 | [IDEA-011](#idea-011) | Separate Disable control and processing suspension | Captured |
| P2 | [IDEA-030](#idea-030) | Selected-node inspector at a readable size | Captured |
| P2 | [IDEA-051](#idea-051) | Website quick-access header and matching actions | Implemented locally; visual review pending |
| P3 | [IDEA-029](#idea-029) | Route inspection, diagnostics and useful activity history | Captured |
| P3 | [IDEA-031](#idea-031) | Task templates and favorite or recent node types | Voice starter verified and locally integrated for unreleased 0.5.0; other template/favorite work captured |
| P3 | [IDEA-032](#idea-032) | More informative overload readings | Captured |
| P3 | [IDEA-016](#idea-016) | Guided node calibration and Learn settings | Researched |
| P3 | [IDEA-033](#idea-033) | New voice cleanup and leveling nodes | Captured |
| P3 | [IDEA-001](#idea-001) | Dedicated visualization nodes | Researched |
| P3 | [IDEA-003](#idea-003) | Inline switch and toggle nodes | Researched |
| P3 | [IDEA-004](#idea-004) | User-created tasks for multiple changes | Researched |
| P3 | [IDEA-048](#idea-048) | Developer API for third-party apps and automation | Captured |
| P3 | [IDEA-013](#idea-013) | Audio-file inputs and soundboard playback | Researched |
| P3 | [IDEA-002](#idea-002) | Distortion and related creative effects | Distortion offline verified and locally integrated; unreleased 0.5.0 |
| P3 | [IDEA-015](#idea-015) | Support additional virtual audio drivers | Captured |
| P3 | [IDEA-043](IDEAS_LATER.md#idea-043) | Sound recall across processing updates | Captured |
| P3 | [IDEA-006](#idea-006) | External parameters and conditional task triggers | Researched |
| P4 | [IDEA-034](IDEAS_LATER.md#idea-034) | Stereo and routing utilities | Captured |
| P4 | [IDEA-035](IDEAS_LATER.md#idea-035) | Creative effect extensions | Captured |
| P4 | [IDEA-036](IDEAS_LATER.md#idea-036) | Recording and replay | Captured |
| P4 | [IDEA-037](IDEAS_LATER.md#idea-037) | Plugins and external audio | Captured |
| P4 | [IDEA-038](IDEAS_LATER.md#idea-038) | Continuous parameter controllers | Captured |
| P4 | [IDEA-039](IDEAS_LATER.md#idea-039) | Solo, audition and talkback | Captured |
| P4 | [IDEA-040](IDEAS_LATER.md#idea-040) | Automatic parallel-path delay compensation | Captured |
| P4 | [IDEA-041](IDEAS_LATER.md#idea-041) | True-peak limiting | Captured |
| P4 | [IDEA-042](IDEAS_LATER.md#idea-042) | Test-signal generator | Captured |
| P4 | [IDEA-044](IDEAS_LATER.md#idea-044) | Offline draft workspace editing | Captured |
| Parked | [IDEA-014](#idea-014) | Dedicated VineMix virtual audio driver | Parked |
| Parked | [IDEA-045](IDEAS_LATER.md#idea-045) | Scenes and A/B comparison — much later | Parked |
| Parked | [IDEA-047](#idea-047) | System-latency investigation or separate helper | Parked |

P1 entries IDEA-017 through IDEA-021 were implemented in the unpublished local
candidate on 23 October 2026 and moved to [the implemented archive](IDEAS_IMPLEMENTED.md).
The remaining priority rows retain their prior scope and status.

## Maintainer decisions from 23 October 2026

- The first usability priorities are the top-right padlock and a red **Quit
  VineMix** action at the bottom of Settings. Closing should minimise, without
  adding a close-behavior preference chooser; the tray/taskbar interpretation is
  recorded in IDEA-018.
- Preserve the precise preset, group and troubleshooting interactions below.
  Their detailed designs still need review; priority does not imply readiness.
- A guided introduction to node-and-noodle mixing replaces the proposed
  virtual-route setup wizard. It does not reinstate every previously removed
  Help lesson or duplicate driver installation flows.
- **Declined:** additional inherited-controller explanations such as "Muted by
  Main control". The maintainer considers the existing relationship simple
  enough. Do not reintroduce a dedicated explanation feature through diagnostics.
- **Deferred:** scenes and all A/B comparison workflows are for much later.
  Selected-section User Presets are the useful nearer-term direction.
- The maintainer's denoiser comparison is settled for product planning:
  SpeexDSP offers lower delay with lesser perceived cleanup quality; RNNoise
  offers the opposite tradeoff. Record that as a listening assessment, not a
  universal benchmark. Expose current node delays in the dropdown (IDEA-019);
  a broad comparison project is no longer an active backlog item.
- Do not invest heavily in further engine-latency work. The belief that most
  remaining delay is system-specific is not a calibrated measurement. A separate
  helper is only a parked possibility (IDEA-047).
- New voice cleanup/leveling nodes are welcomed as future candidates (IDEA-033).
  No algorithm, dependency or complete set of nodes has been selected.
- Prior choices remain: passive hover peak readings and fading overload caps,
  simple source-to-destination cable hover, and removal of resize experiments.
  Do not revive rejected features as unlogged improvements.

## Detailed ideas

Homepage animation decisions and prototype history now live in
[TWEAK-005](TWEAKS.md#tweak-005). Integration is implemented locally; it has not
been publicly deployed. The preserved reference and its handoff remain linked
from that entry.

Original entries below retain their dated research and meaningful changes in
direction. The new entries and [later backlog](IDEAS_LATER.md) consolidate only
the candidates reviewed with the maintainer, not every historical planning note.

<a id="idea-001"></a>
## IDEA-001: Dedicated visualization nodes

- **Captured:** 2026-10-19. **Status:** Researched.
- **Original intent:** Add an entire node category purely for visualizing
  information, including different graph types.
- **User need:** Inspect signals at useful points in a complicated graph without
  having to add an effect just to see what is happening.
- **Proposal:** A Visualizers category. Candidate types, proposed for discussion:
  spectrum analyzer, waveform/oscilloscope, spectrogram, level history, and stereo
  correlation or vectorscope. Parameter histories could later expose automation
  inputs and task progress (IDEA-006). The particular graph types are suggestions,
  not a confirmed list from the maintainer.
- **Review extension (2026-10-23):** Retain LUFS loudness and true-peak meters
  from the recovered feature research as candidate measurement types here,
  rather than duplicating the visualizer proposal. Their standards, algorithms
  and validation still need primary-source research; no meter is newly verified.
- **Project fit and research:** The existing EQ already measures its actual
  post-processing stereo signal with a fixed-buffer handoff, doing FFT work away
  from the audio thread. Reuse that ownership pattern and spectrum computation;
  it does not yet provide the histories or stereo phase information needed for
  every proposed display. See [EQ spectrum](EQ_SPECTRUM_ANALYZER.md),
  `native/include/am/spectrum.hpp`, and [node library](NODE_WORKSPACE.md#node-library).
- **Scope and tradeoffs:** Medium for a standalone spectrum node using existing
  machinery; broader for the full family (qualitative estimates only). Bound CPU,
  history length, and memory across many visualizers. A slow display should drop
  old visual updates rather than delay audio. Preserve stereo information where
  the visualization requires it. Decide whether these count toward the current
  32-node limit.
- **Open question:** Inline transparent nodes, side-branch monitoring nodes, or
  both? A side branch must not accidentally duplicate sound into an output.
- **Next step:** Specify the first two useful displays and their measurement
  points, then sketch how users attach them to an existing route.

<a id="idea-002"></a>
## IDEA-002: Distortion and related creative effects

- **Refinement authorized 2026-11-06 — offline verified and locally integrated:** Audit control transitions,
  bypass/mute, state continuity, alias/headroom behavior and full routing-fade
  performance after the Echo refinement. Preserve the selected sound and controls
  unless a demonstrated defect justifies a change. The effects-menu correction
  moves this node into the single Effects category under TWEAK-024.
- **Refinement result:** FIR work scheduling was optimized while preserving
  one million stereo frames bit-for-bit against the previous processor. Complete
  control/publication tests and maximum routing fades now run; all 19 native
  suites and 461 creative checks pass. Performance margin remains host-dependent;
  see [ISSUE-027](KNOWN_ISSUES.md#issue-027) and the execution report. No independent
  live listening claim is made.
- **Captured:** 2026-10-19. **Updated:** 2026-11-05. **Status:** Implemented, offline verified and locally integrated for unreleased 0.5.0.
- **Selected scope:** The maintainer authorized Distortion before final release
  qualification and chose a broad subtle-to-strong range. One soft-saturation
  algorithm, Drive 0–36 dB (default 6), Tone 500–18000 Hz (6000), Mix 0–100% (50),
  Output −24–0 dB (−6), and existing Bypass. Effects category. No mode
  dropdown, bitcrusher, new routing ports or dependency is requested.
- **Implementation contract:** Preserve stereo; smooth parameter changes; qualify
  harmonic character, alias suppression, DC, headroom and worst-case CPU using
  synthetic buffers. Match dry/wet/bypass timing and disclose any processing
  latency. Existing workspace loading, copy/paste, reset and undo/redo must work.
  This records authorization and a plan, not a completed listening or release check.
- **Original intent:** A distortion node and other effects in that general family.
- **Proposal:** Start with a dedicated Distortion node with Drive, Tone, Mix,
  Output level, and bypass. Soft saturation, hard clipping, and overdrive are
  candidate modes; bitcrushing or sample-rate reduction are possible later nodes,
  not requested commitments.
- **Project fit:** The native graph has effects and Voice Color but no dedicated
  distortion kind (`native/include/am/graph.hpp`). Existing effect settings,
  parameter smoothing, bypass, and telemetry provide integration patterns.
- **Research (2026-10-19):** Waveshaping is an established implementation approach;
  nonlinear processing needs attention to aliasing. Oversampling offers a
  quality/CPU tradeoff and introduces filter latency or phase effects. See
  [JUCE waveshaper reference](https://docs.juce.com/master/structjuce_1_1dsp_1_1WaveShaper.html)
  and [JUCE oversampling reference](https://docs.juce.com/master/classjuce_1_1dsp_1_1Oversampling.html).
  These are technical references, not a decision to adopt JUCE or copy its code.
- **Scope and tradeoffs:** Medium for one polished effect; quality modes and
  additional effect families expand scope. Address output headroom, DC offset,
  smooth controls, and dry/wet timing if oversampling is used. The mixer currently
  has no automatic compensation between parallel paths.
- **Earlier question, settled 2026-11-05:** The initial range covers both subtle
  voice coloration and strong distortion; modes listed above remain exploratory.
- **Verification:** Shared creative suite passes 359 synthetic checks, combined
  native/UI/boundary checks and guarded paired integration pass. Fixed 8×
  oversampling retains 32-frame dry/wet/bypass alignment; measured quality and
  timing limits are in the [execution report](IMPLEMENTATION_0.5.0.md).
- **Next step:** Maintainer listening follow-up and final combined release
  qualification/documentation; synthetic tests do not establish listening quality.

<a id="idea-003"></a>
## IDEA-003: Inline switch and toggle nodes

- **Captured:** 2026-10-19. **Status:** Researched.
- **Original intent:** A small node placed between other nodes whose single
  button or switch turns a line on or off.
- **Proposal:** A compact Switch node with one audio input, one audio output,
  and an unmistakable On/Off control. Off silences that branch while preserving
  the connections. Tasks (IDEA-004) can set its state, and it can be pinned
  (IDEA-005). A multi-route selector is a possible extension, not a requirement.
- **Review decision (2026-10-23):** A selector specifically for A/B source or
  chain comparison belongs with parked [IDEA-045](IDEAS_LATER.md#idea-045).
  Keep the simple On/Off switch independent; A/B work is explicitly much later.
- **Project fit and research:** Cables already retain a mute/on-off state and
  expose it through their context menu. Graph nodes also have saved mute state.
  However, today's intermediate-node UI exposes Bypass, which passes audio
  through; it would not implement the intended switch-off behavior. The existing
  audio-reactive Expander and Gate nodes perform different operations. See
  [workspace controls](NODE_WORKSPACE.md), `desktop/VineMix.Desktop/GraphModels.cs`,
  and `native/include/am/graph.hpp`.
- **Scope and tradeoffs:** Small to medium relative to the other proposals.
  Reuse graph insertion, persistence, undo, and short click-prevention gain ramps.
  Keep the visible state accurate when a task changes it. A switch costs a graph
  node; direct cable controls avoid that cost but do not give the requested
  prominent one-button interaction.
- **Open question:** Should Off silence only the branch output, or also suspend
  upstream processing when nothing else uses it? Initial suggestion: silence
  only, avoiding changes to other consumers and effect tails.
- **Next step:** Define the compact switch appearance and on/off semantics,
  including its relationship to existing node bypass and cable mute.

<a id="idea-004"></a>
## IDEA-004: User-created tasks for multiple changes

- **Captured:** 2026-10-19. **Status:** Researched.
- **Original intent:** Users create a named task that changes a list of cables
  ("noodles"), nodes, and particular settings in one action.
- **Confirmed scope clarification (2026-10-19):** Tasks modify only the state or
  parameters of existing nodes and cables. They cannot create, delete, move,
  reconnect, or otherwise change the structure or layout of nodes or cables.
  Disabling a cable retains its existing endpoints and saved connection.
- **User example:** Turn off microphone A, turn on microphone B, slightly raise
  one output, and change EQ settings, all from one task.
- **Proposal:** A task editor containing explicit target/action rows: set a cable
  On/Off, mute/unmute a source, set a switch, bypass/unbypass an effect, set a
  parameter, or apply a selected EQ configuration. Tasks can be run manually or
  by an external condition, and pinned for quick access. Timed parameter changes
  must support the five-second fade in IDEA-006. Whether EQ changes use a saved
  preset or embedded band settings remains open.
- **Project fit and research:** The control protocol currently commits a full
  configuration with revision checks. Graph objects have stable IDs, and undo
  handles grouped edits, but the inspected graph configuration has no task
  definitions. Existing Linked control nodes share gain offsets; they are not
  multi-action tasks. See [control protocol](CONTROL_PROTOCOL.md),
  [node workspace](NODE_WORKSPACE.md), and `desktop/VineMix.Desktop/GraphModels.cs`.
- **Suggested design:** Store references by ID rather than displayed name. Prefer
  explicit Set On/Off actions for predictable repeat execution; offer Toggle only
  when intentionally selected. Restrict the task action list and execution
  validation to state/parameter changes; exclude structural and layout edits.
  Validate immediate actions together and aim for
  one coherent configuration commit and undo operation. Timed actions need a
  runtime scheduler, cancellation, and a policy for overlapping changes; they
  cannot all be represented as one instantaneous state change.
- **Scope and tradeoffs:** Large: persistence, editor, execution feedback, grouped
  changes, time-based actions, deleted-target handling, and conflict behavior.
  Repeated automation must not save settings or recompile the graph on every
  audio sample. Determine whether tasks run while the UI is hidden or closed.
- **Open questions:** Run once versus paired states; simultaneous versus ordered
  actions; absolute values versus relative adjustments; what happens when a
  target is missing; and whether cancellation restores prior settings.
- **Next step:** Specify the microphone-switching example as the first task,
  plus a fade action. Agree on rerun, cancellation, and undo behavior.

<a id="idea-005"></a>
## IDEA-005: Pin nodes and tasks to the top of the window

- **Captured:** 2026-10-19. **Status:** Researched.
- **Original intent:** Pin both nodes and tasks at the top so they remain visible
  regardless of canvas panning.
- **Proposal:** A persistent top strip outside the canvas pan/zoom transform.
  Pin and unpin existing nodes or tasks, arrange their order, and operate them
  from there. Task buttons and compact node controls are a suggested first
  design; whether full nodes can be docked remains undecided.
- **Project fit and research:** The workspace already separates toolbar controls
  from a panning/zooming node canvas, and saves graph positions and camera state.
  The inspected graph model has no pin metadata. See [workspace](NODE_WORKSPACE.md),
  `desktop/VineMix.Desktop/GraphWorkspace.cs`, and
  `desktop/VineMix.Desktop/GraphModels.cs`.
- **Suggested design:** A pinned control references the original object, so both
  representations display the same live state and never process duplicate audio.
  Include a way to locate the node on the canvas. Persist pin order and handle
  deleted targets. Zoom independence is inferred from the always-visible intent;
  the exact representation is still open.
- **Scope and tradeoffs:** Medium; depends on IDEA-004 for task pins. The strip
  consumes vertical workspace and needs overflow behavior at the supported
  minimum window size. Pinning a large EQ node creates different layout demands
  from pinning a one-button switch.
- **Open question:** Full node, compact live controls, or a user-selectable mode?
  "Top of the screen" is provisionally interpreted as inside the app window;
  an operating-system overlay has not been requested.
- **Next step:** Sketch the top strip with a microphone, a switch, and two task
  buttons at the minimum supported window size.
- **Review relationship (2026-10-23):** Compare [IDEA-030](#idea-030)'s selected-node
  inspector with this persistent pinned strip. They share readable controls
  outside canvas zoom, but selection-following and pinning are distinct behaviors.

<a id="idea-006"></a>
## IDEA-006: External parameters and conditional task triggers

- **Captured:** 2026-10-19. **Status:** Researched.
- **Original intent:** Parameters from Baballonia (the maintainer said "Babylonia") or
  similar programs can trigger tasks after user-defined conditions are met.
- **User example:** If tracking indicates eyes have stayed closed longer than
  a chosen duration, trigger a task that gradually lowers an output volume over
  **five seconds**. The closed-eye hold duration and destination volume were
  deliberately unspecified. Falling asleep is the motivating use case; eye
  closure alone is only a heuristic and does not establish sleep.
- **Proposal:** Separate three reusable pieces: external parameter input,
  condition evaluation (comparison, AND/OR, continuous hold duration), and task
  execution. Show current values, connection health, condition progress, and the
  last trigger. Initially investigate OSC, retaining adapters for other sources.
- **Research (2026-10-19):**
  - [Baballonia's official repository](https://github.com/Project-Babble/Baballonia)
    confirms eye/face tracking, integration through VRCFaceTracking's Babble
    module, and an alternative native VRChat eye-look mode. This does not by
    itself establish a generic broadcast endpoint available to VineMix.
  - [VRCFaceTracking's parameter reference](https://docs.vrcft.io/docs/tutorial-avatars/tutorial-avatars-extras/parameters)
    documents left/right eyelid openness values and the widening range. Exact OSC
    prefixes, active parameters, and availability must be discovered or configured
    for the user's actual setup; do not assume a universal parameter address.
  - [VRChat OSC overview](https://docs.vrchat.com/docs/osc-overview) documents
    default receive port 9000, send port 9001, and configurable destinations.
    [Avatar parameters](https://docs.vrchat.com/docs/osc-avatar-parameters) can
    send configured values to external applications. An OSC relay or producer
    destination setting may be needed to coexist with other listeners.
  - [Native OSC eye tracking](https://docs.vrchat.com/docs/osc-eye-tracking)
    accepts `/tracking/eye/EyesClosedAmount`, a 0–1 closure amount. It is documented
    as an input to VRChat; do not assume it is echoed on VRChat's outbound stream.
    Its closure direction also differs from an openness parameter.
- **Feasibility conclusion:** Plausible through a verified producer feed,
  configured avatar output, or relay. The exact Baballonia/VRCFT data route and
  liveness signal remain unverified. A generic OSC receiver alone is insufficient
  without confirming which process sends which values to which destination.
- **Suggested condition behavior:** Require both eyes closed continuously when
  both measurements are available, with calibrated thresholds, hysteresis, and
  one trigger per closure episode. A blink or a reopened eye resets the hold
  timer. Rearm only after reopening; expose an automation enable/disable control.
  These are design suggestions, not additional requirements from the maintainer.
- **Tracking loss:** Do not interpret absent data as closed eyes. Invalid or
  disconnected tracking should reset a pending hold. Sources may send values
  only when they change, so silence alone cannot always distinguish a stable
  value from a dead source; investigate a heartbeat or validity signal before
  choosing a stale-data timeout. Specify what an already-running fade does on
  tracking loss, and allow manual cancellation.
- **Timed action:** Fade from the current output gain to a configured lower target
  over exactly five seconds. Choose an interpolation curve explicitly (for
  example, linear in dB), define manual override and overlapping-task behavior,
  and avoid automatic volume restoration until that behavior is chosen. The
  existing engine's short smoothing ramps do not constitute a user-duration fade
  scheduler. Keep network parsing and condition evaluation off the audio thread;
  execute bounded audio ramps without per-step settings writes.
- **Scope and dependencies:** Large; depends on tasks and timed actions in
  IDEA-004. Parameter visualization (IDEA-001) and pinned controls (IDEA-005)
  would make setup and intervention easier. Testable with synthetic OSC streams
  before real tracking hardware, but no integration has been tested yet.
- **Open questions:** Exact tracking software/version and feed; calibration;
  required hold duration; output and lower target; cancellation/restoration
  behavior; and whether automation must survive closing the desktop UI.
- **Next step:** Inspect a sample parameter stream from the intended setup to
  establish addresses, value directions, liveness, and coexistence with VRChat,
  then specify the closed-eyes-to-five-second-fade scenario end to end.

<a id="idea-011"></a>
<a id="idea-011-compact-bypass-control-and-clear-processing-semantics"></a>
## IDEA-011: Separate Disable control and processing suspension

- **Captured:** 2026-10-20 (UTC+10). **Status:** Captured.
- **Presentation split, 2026-10-25:** The original compact Bypass appearance,
  placement and state-wording request is now [TWEAK-006](TWEAKS.md#tweak-006).
  This idea retains the later request for a separate Disable capability and the
  engine investigation needed to define it. **Previous priority:** P2.
- **Original question:** Does bypass leave effect computation running even
  though its treatment is inaudible? The dated findings below answer that
  question and inform the proposed processing-suspension behavior.
- **Verified current behavior (local source review, 2026-10-20):**
  In `native/src/graph.cpp`, `GraphProcessor::process` sets the wet blend target
  to zero for bypass, but runs filter/compressor/ducking processing and calls
  `AdvancedEffect::process` before applying that blend. The latter continues
  denoiser, reverb, Voice Color, expander/gate, de-esser, plosive, and alignment
  delay processing (`native/src/advanced_effects.cpp`). After the short crossfade,
  the dry signal is used and the intermediate node's local/linked level adjustment
  is also bypassed. Thus bypass generally removes the audible treatment without
  suspending effect computation. It is not a CPU-off control. The limiter's final
  ceiling-protection stage is explicitly skipped when bypassed, so not every
  processing stage behaves identically.
- **Timing and state considerations:** Denoiser bypass preserves its delayed dry
  path (SpeexDSP 20 ms / RNNoise 30 ms); Alignment Delay bypass instead returns
  undelayed audio. See [effects](EFFECTS_RESEARCH.md) and
  [alignment behavior](CORE_DYNAMICS.md). Continued processing keeps histories
  updating, which can support smooth re-enabling; this is an engineering benefit
  inferred from the implementation, not a measured CPU/transition comparison.
  No external research, CPU benchmark, or live audio check was performed.
- **Presentation dependency:** Coordinate the future Disable control with
  [TWEAK-006](TWEAKS.md#tweak-006), without making existing Bypass polish depend
  on new engine behavior. A power icon or Enabled label must not imply that
  today's Bypass suspends computation. Effect history, tails, latency and
  re-enable transitions remain decisions for this idea.
- **Follow-up investigation (2026-10-20):** The maintainer asked whether fully stopping
  processing on click would disrupt the engine and whether it would improve
  latency. Stopping work need not restart the engine: a proposed implementation
  could crossfade to dry first, then suspend processing while keeping resources
  allocated. Re-enabling needs an effect-specific history reset/warm-up policy
  before fading back in, avoiding stale delay/reverb output or detector state.
  This is an option to evaluate, not an approved implementation.
  CPU savings and signal delay are separate: skipping an EQ or compressor saves
  work without automatically reducing the fixed audio block/device buffering.
  Removing a denoiser's 20/30 ms delay also requires switching its dry path to
  undelayed audio; merely suspending its algorithm while retaining that buffer
  does not remove latency. That timing change can skip/repeat a segment or cause
  phase artifacts during a blend and disturb parallel-route alignment. Compare
  a latency-preserving suspended mode with a direct, delay-removing mode; neither
  has been selected. No CPU savings or end-to-end latency improvements have been
  measured. Keep allocation, destruction, and blocking work out of the audio loop.
- **Updated user proposal (2026-10-20):** Offer both **Bypass** and **Disable**
  controls, with tooltips explaining the difference. Bypass omits the audible
  treatment while keeping effect processing running; Disable suspends effect
  processing. Preserve the earlier preference for compact controls and improved
  placement. This supersedes the question of whether to explore a distinct
  Disable control, but remains a future-development proposal.
- **Suggested tooltip wording (draft):**
  - Bypass: "Passes audio through without this effect's treatment while keeping
    its processing running for a quick return. Does not generally save CPU."
  - Disable: "Stops this effect's processing to save CPU and passes audio
    through. Re-enabling may need a brief warm-up."
  Do not promise reduced latency in the Disable tooltip until its delay policy
  is chosen; explain retained delay for relevant effects in contextual help.
- **Interaction decisions still open:** Whether Disable removes the effect's
  buffering delay; how Bypass and Disable interact; whether re-enabling restores
  the previous bypass state; and whether disabled intermediate nodes omit local
  and linked gain like bypass. Suggested presentation: distinguish Processing,
  Bypassed, and Disabled states and prevent contradictory active controls.
  Disable is provisionally a pass-through state, not mute, deletion, or cable
  disconnection; the detailed semantics still need specification.
- **Next step:** Define disable/resume and latency behavior, then coordinate the
  two-control layout with TWEAK-006. Measure savings and verify smooth transitions
  separately; presentation changes alone do not implement processing suspension.

<a id="idea-012-silence-an-apps-direct-playback-while-monitoring-through-vine-mix"></a>

<a id="idea-012"></a>
## IDEA-012: Silence an app's direct playback while monitoring through VineMix

Moved on 2026-10-24 to [ISSUE-002](KNOWN_ISSUES.md#issue-002).
The report, proposed remedies and research are maintained there; this ID is retained
for existing references and must not be reused.

<a id="idea-013"></a>
## IDEA-013: Audio-file inputs and soundboard playback

- **0.5.0 review, 2026-11-04:** Shortlisted for discussion in
  [IDEA-052](#idea-052); no implementation commitment. Source Reader feasibility
  was rechecked, while format coverage and playback/storage decisions remain open.

- **Captured:** 2026-10-20 (UTC+10). **Status:** Researched.
- **User's requested direction:** Drag MP3 and other supported audio files
  directly onto the canvas to create audio-file input nodes. Also offer an
  Audio File node through normal node creation. Allow looping and a play button
  for soundboard-style activation. In a later phase, let users bind a keyboard
  shortcut to trigger a sound even when the app window is not focused.
- **User need:** Play clips or looping audio through the same effects, mixes,
  listening outputs, and virtual microphone routes as other sources, without
  running a separate player and capturing it.
- **Project fit and local review (2026-10-20):**
  `desktop/VineMix.Desktop/NodeCatalog.cs` currently offers microphone,
  virtual-input, and running-app sources; `native/include/am/graph.hpp` has no
  audio-file source kind. Existing graph routing, resampling, bounded audio
  queues, and playback status offer reusable patterns, but file decoding and a
  playback transport would be new. Existing canvas node/cable dragging is not
  file-drop support. No playback prototype or shortcut registration was performed.
- **Primary-source research (2026-10-20):**
  [WPF drag and drop](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/drag-and-drop-overview)
  provides the desktop drop interaction. Windows
  [Media Foundation Source Reader](https://learn.microsoft.com/en-us/windows/win32/medfound/source-reader)
  can decode files into samples for an application's own pipeline, but does not
  manage playback timing; VineMix would schedule the samples itself. Microsoft's
  [format reference](https://learn.microsoft.com/en-us/windows/win32/medfound/supported-media-formats-in-media-foundation)
  lists MP3, WAVE, and AAC/MPEG-4 support. MP3/WAV are candidate initial formats;
  verify exact container/codec and Windows-installation coverage before promising
  other formats. A broader decoder library is an alternative, with dependency,
  packaging, and licence costs; none has been selected.
  [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
  provides system-wide shortcuts for the later phase, subject to reserved or
  already-registered combinations. Report conflicts rather than promising that
  every key binding works in every application.
- **Suggested initial behavior:** A dropped file creates a source at the drop
  position in canvas coordinates, named after the file, initially stopped. Its
  audio output uses ordinary cables. Provide Play/Stop, Loop, and clear loading,
  ready, playing, and missing/unsupported-file feedback. A file picker on the
  standalone node complements dropping. These details are suggestions; only
  file input, dropping, looping, and play activation were explicitly requested.
- **Scope and tradeoffs:** Medium to large: source configuration and persistence,
  file loading/decoding, transport commands/status, node UI, drops, and offline
  verification. Decode and read files away from the real-time audio thread and
  supply bounded/prepared samples in the engine's 48 kHz stereo format. Short
  predecoded clips reduce trigger delay but consume memory; streaming long files
  bounds memory but needs buffering and seeking. Compressed encoder delay/padding
  can affect loop boundaries, so gapless looping needs separate verification.
- **Open questions:** Which additional formats; one node per file on multi-drop;
  repeated Play restarts, overlaps, or toggles; whole-file versus region looping;
  file references versus copied assets and relinking moved files; startup playback
  policy; and whether shortcuts should toggle loops as well as trigger clips.
  Background shortcuts require a running shortcut handler; operation while hidden
  to tray versus after the interface exits remains to be specified.
- **Dependencies:** Manual playback and looping come first; global shortcuts are
  explicitly later. IDEA-005's pinned controls could make clips easier to reach.
  Future task activation would be a separate extension to IDEA-004, not an
  assumption that tasks may create or rearrange nodes.
- **Next step:** Define the first supported formats, replay/loop behavior, and
  file-storage policy; then specify an offline clip-to-effects-to-output check
  and drop-to-node interaction. Plan global shortcuts after manual playback works.
  This is a future-development note, not implementation authorization.

<a id="idea-014-dedicated-vine-mix-virtual-audio-driver"></a>

<a id="idea-014"></a>
## IDEA-014: Dedicated VineMix virtual audio driver

- **Review extension (2026-10-23):** Additional independently addressable Windows
  endpoint pairs are a later possibility beyond the original two-cable proposal.
  Keep this entire driver effort parked; extra endpoint count, naming and resource
  limits are not approved requirements. Compare compatibility work in IDEA-015.

- **Captured and researched:** 2026-10-20 (UTC+10).
  **Status:** Parked. The maintainer decided on 2026-10-20 to retain VB-CABLE for virtual
  input and VAC Lite for virtual output for now. Custom driver development is
  outside the current scope. The completed research is retained for a future
  reconsideration; broader application compatibility is recorded in IDEA-015.
- **User need:** A streamlined installer and predictable virtual input/output
  devices without paid cable dependencies or restrictions that prevent accepting
  donations or selling VineMix. The maintainer requested investigation of AudioMirror's
  legitimacy/reliability and the work needed to adapt it.
- **Proposal:** One maintained VineMix driver package with two independent
  cable paths: applications into the mixer, and the finished mix into recording
  applications. Initial assumption: Windows 11 x64, stereo 48 kHz. The maintainer confirmed
  that **four clearly labelled Windows endpoints are acceptable**, and that
  **the cables must also work between other applications with VineMix closed**.
  Each playback/recording pair therefore needs an autonomous internal connection.
- **Evidence and rationale:** See the dated
  [AudioMirror assessment](AUDIOMIRROR_ASSESSMENT.md) and subsequent
  [open-source driver survey](OPEN_SOURCE_DRIVER_SURVEY.md). The inspected AudioMirror default branch
  is at `a9618d1ab4114e3e35e8e50ae804a4205315d1f2` (2022-10-15).
  The author warns it is unfinished. Two buffer defects were reproduced in an
  isolated source harness; cleanup/removal defects and incomplete multi-cable
  support are visible in source. The upstream uninstall-fix PR remains unmerged.
  No AudioMirror kernel driver was built or installed, and no Windows 11 audio
  qualification was performed. Research sources, limits and test method are in
  the assessment.
- **Broader research follow-up (2026-10-20):** Inspected eight additional source
  repositories. The initial survey preferred MicDeck VAD for a conventional-cable
  prototype; its two portable suites passed here. AudioRouter's separately
  MS-PL-licensed driver provides multi-cable declarations and private engine
  transport; its host bridge suite passed 88,167 checks after supplying two
  missing no-op annotation macros. Virtual Cables plus BSD-licensed usbip-win2
  is a distinct alternative with transport/timing tradeoffs. These are prototype
  priorities, not reliability claims or a dependency decision. DeckX includes
  real but basic loopback; VirtualDrivers' inspected public capture path emits
  silence. Exact commits, licensing boundaries and limitations are in the survey.
- **Deeper comparison (2026-10-20):** See
  [MicDeck and AudioRouter foundations](VIRTUAL_DRIVER_FOUNDATION_COMPARISON.md).
  The updated first prototype recommendation is **AudioRouter's Windows driver
  framework with two new internal loopback paths**, retaining normal WASAPI and
  requiring no relay service. Its existing private bridge is not autonomous.
  MicDeck's portable transport passed two-million-frame stress and sanitizer
  checks, but exact-source tests reproduced a counter/frequency inversion and
  invalid cyclic-buffer positions; power/format integration is also incomplete.
  AudioRouter's upstream suite and additional transport tests passed here; newer
  upstream VM/Verifier records apply to an identical driver subtree, but remain
  self-reported and do not qualify our proposed transport change. Native WDK
  compilation and remaining live-test limits are recorded in the comparison.
- **Commercial-use conditions:** MicDeck's driver MIT and AudioRouter's explicit
  driver MS-PL grant allow commercial distribution/donations with their notices
  and conditions. AudioRouter's surrounding GPL Rust client/helper is outside
  that grant; our proposed autonomous design avoids importing its client and
  needs an independent installer helper. Preserve per-file provenance and
  Microsoft notices. No paid cable licence is needed, but our own accepted
  production signing/qualification path still needs a budget. AudioMirror's
  inherited Microsoft source requires the same careful component review.
- **Scope/tradeoffs/dependencies:** Large, with ongoing kernel-driver ownership.
  Requires a WDK build pipeline, buffer/lifetime repairs, two unique endpoint
  pairs, role-aware native/desktop binding, careful saved-device migration,
  installer rollback, test machines, Windows compatibility qualification and
  publisher signing. Current drivers remain the fallback. App installer/save-path
  improvements can proceed separately under their existing proposal.
- **Scope decision (2026-10-20):** Following the research, the maintainer chose to keep
  the existing VB-CABLE and VAC Lite dependencies for now and defer custom driver
  development. Rechecked
  [VB-Audio's terms](https://vb-audio.com/Services/licensing.htm) and the
  [VAC EULA](https://vac.muzychenko.net/en/license.txt): base VB-CABLE explicitly
  permits compliant free/commercial application bundles; VAC Lite restricts use
  to private homes without income generation and prohibits profiting from its
  provision to others. These are distinct from monetising VineMix's separate
  application. Paid-app/donation scenarios must not be represented as blanket
  permission to bundle Lite or use it for income-generating audio work. A
  driver-independent application with separately obtained, appropriately licensed
  drivers is a lower-maintenance route to assess before custom development.
- **Alternatives and unresolved decisions:** Retain MicDeck's portable transport,
  Virtual Cables/USB-IP and a current Microsoft SimpleAudioSample/SysVAD foundation
  as alternatives if the AudioRouter feasibility gate fails. The earlier
  AudioMirror-only and initial MicDeck-first priorities are superseded by the
  deeper findings. Decide supported Windows releases, transport precision,
  staffing, test target and signing budget if this idea is revisited. No new
  driver dependency has been adopted.
- **Practical next step:** Keep the current two-driver setup. Assess its
  distribution and revenue model separately; no custom driver work is scheduled.
  If custom implementation is
  later requested, run the separate
  one-to-two-week AudioRouter feasibility gate: build/package, add two isolated
  internal cable paths and test both simultaneously with VineMix closed in a
  disposable Windows target. A more complete prototype is estimated at two to
  five weeks and release qualification at several months, with explicit
  uncertainty and an experienced driver engineer assumed. This entry does not
  authorize installing or developing the driver.

<a id="idea-015"></a>
## IDEA-015: Support additional virtual audio drivers

- **Related implemented fix (2026-10-23):** The authorized Virtual Desktop
  headset-output fix now accepts ordinary streamed/software Windows playback
  endpoints in Listening output, alongside the earlier input fix. Discovery,
  selection, engine validation and worker policy were updated together; explicit
  bindings, dedicated VAC/VB-CABLE restrictions and ownership protection remain.
  This does not implement this broader alternative-cable proposal or add a driver.
  See [device behavior](DEVICE_VISIBILITY.md#streamed-headset-output-support--23-october-2026)
  and [verified scope and remaining limits](LOCAL_CANDIDATE_2026-10-23.md).

- **Review extension (2026-10-23):** Distinguish supporting alternative drivers
  from supporting more independent virtual routes/endpoints within a driver.
  The latter is a later candidate requiring an explicit endpoint contract and
  compatibility qualification, not an assumed consequence of adding a driver.

- **Captured:** 2026-10-20 (UTC+10). **Status:** Captured for later;
  the maintainer explicitly chose to keep the current two drivers for now.
- **User need:** Let users choose compatible virtual audio cables they already
  have, including appropriately licensed alternatives for commercial activity,
  without requiring VineMix to develop or maintain a kernel driver.
- **Proposal:** Extend **Troubleshooting → Reassign device** to offer compatible
  virtual recording devices for Virtual Input and compatible virtual playback
  devices for Virtual Output. Preserve explicit saved assignments, missing-device
  recovery and output-ownership protection. Keep the current VB-CABLE/VAC Lite
  defaults until a separate implementation decision is made. This is an
  application compatibility feature, distinct from the parked custom-driver
  proposal in [IDEA-014](#idea-014-dedicated-vinemix-virtual-audio-driver).
- **Project fit and feasibility:** Checked the current code on 2026-10-20.
  `VirtualDeviceBindings.Matches` restricts the menu to VB-CABLE capture and VAC
  playback endpoints. Native graph validation and audio workers enforce matching
  restrictions, so widening the menu alone would not enable other drivers.
  Initial assumption: standard Windows endpoints accessed through the existing
  WASAPI engine; private driver protocols or ASIO would require separate scope.
  See [virtual device selection](VIRTUAL_DEVICE_UI.md),
  `desktop/VineMix.Desktop/VirtualDeviceBindings.cs`,
  `native/src/engine.cpp` and `native/src/windows_audio.cpp`.
- **Scope and tradeoffs:** Update device classification, menu filtering, engine
  validation and audio-opening policy together. Qualify format support, shared
  versus exclusive access, latency, simultaneous clients and reconnect behavior
  for each supported driver. Compatibility does not grant redistribution or
  commercial-use rights; each dependency retains its own licence. No additional
  driver has been selected or qualified by this entry.
- **Practical next step:** When revisited, define the supported endpoint contract
  and choose a small driver compatibility matrix, then test both independent
  audio paths while preserving existing saved bindings. Recording this idea does
  not authorize implementation, installation or changes to the running app.

<a id="idea-016"></a>
## IDEA-016: Guided node calibration and Learn settings

- **Captured:** 2026-10-21. **Status:** Researched.
- **User's idea:** Add a Calculate/Learn option inside the de-esser. Let a user
  capture themselves making an "sss" sound and estimate an appropriate Frequency
  setting for their voice. Investigate which other nodes can suggest settings
  from measured audio rather than requiring users to set every dial manually.
- **User need:** Make voice-processing setup approachable and account for the
  user's voice, microphone, distance, input level, and upstream processing.
- **Current behavior verified locally (2026-10-21):**
  `native/src/advanced_effects.cpp` implements a high-pass detector driving a
  dynamic high shelf. Frequency controls the detector cutoff and shelf midpoint,
  not a narrow band centered on one exact "sss" pitch. The current range is
  2–12 kHz. Threshold determines when reduction activates; Maximum reduction
  caps it. Consequently, copying the loudest FFT frequency into Frequency is not
  sufficient calibration. See [effects](EFFECTS_RESEARCH.md#de-esser-and-plosive-remover)
  and [de-esser audit](DEESSER_QUALITY_AUDIT.md).
- **Suggested workflow, not yet approved detail:** An in-node Learn action guides
  a brief quiet-room sample, ordinary speech, and sibilant phrases (including
  "sss", "sh", and words containing them). Analyze a bounded temporary capture
  of the node's input before its own processing. Compare ordinary and sibilant
  frames across candidate cutoff frequencies, simulate the actual detector, and
  suggest Frequency and Threshold. Keep Maximum reduction conservative or
  user-selected: preferred strength is not uniquely measurable. Reject clipped,
  too-quiet, contaminated, or insufficiently separable samples with a helpful
  retry message rather than inventing a precise answer.
- **Result presentation:** Show proposed values, a short explanation and sample
  quality/confidence, allow an explicit Apply/Cancel and Undo, and retain manual
  controls. Optional playback audition can help assess dullness or a lisp;
  avoid automatic monitoring or feedback. Keep samples in temporary local memory
  by default, discard them after use, and persist settings rather than raw voice
  recordings. No capture, audio playback, model training, or uploads were performed
  for this note. Upstream gain/EQ/routing changes can invalidate calibration.
- **Primary-source research (2026-10-21):**
  [FabFilter de-esser metering](https://www.fabfilter.com/help/pro-ds/using/metering)
  distinguishes filtered detector level/Threshold from frequency selection.
  [iZotope Vocal Assistant](https://www.izotope.com/community/blog/nectar-vocal-assistant)
  demonstrates analysis-assisted level, EQ, and compressor setup; its user-selected
  targets also illustrate that results depend on the desired sound. These are
  feasibility precedents, not algorithms adopted by VineMix or evidence of equal
  results. [iZotope Dialogue De-noise Learn](https://help.izotope.com/docs/rx-plug-in-pack/Dialogue_De-noise.html)
  is a precedent for learning a noise reference, but VineMix's current RNNoise and
  SpeexDSP controls do not expose that same editable noise-profile architecture.
- **Other candidate nodes (engineering proposals):**

  | Node | Useful measurement and possible suggestion | Limit or required user choice |
  | --- | --- | --- |
  | Gate / Expander | Quiet room plus quiet/normal speech: Threshold, possibly Hysteresis and Range | Noise and quiet speech may overlap; Hold/Release and acceptable suppression need listening |
  | Compressor / input or Mix gain | Representative quiet, normal and loud speech: level statistics, initial Threshold/Ratio or headroom adjustment | Desired dynamics/target level must be chosen; do not automatically raise physical playback volume |
  | Ducking | Quiet and spoken trigger samples: Threshold; Dynamic Ratio as a starting point | Static Duck amount and preferred recovery timing express how much background audio the user wants |
  | Plosive remover | Normal speech plus "p/b" phrases: frequency limit and Threshold candidates | Avoid treating desired bass as a plosive; sample/mic position may not reproduce real problems |
  | EQ / high-pass | Speech spectrum: rumble cutoff or candidate resonances | No objectively correct tonal curve from voice spectrum alone; boosts/cuts need a target and review |
  | Denoiser | Background noise plus speech: suggest existing Reduction/Mix settings | RNNoise is already pretrained; a sample does not train a personal model, and denoising artifacts need listening |

- **Scope, tradeoffs, and dependencies:** Medium to large for reusable guided
  capture, analysis workers, results UI, cancellation, quality checks, and undo.
  Keep analysis, allocation, and storage work off the real-time audio thread.
  It need not add permanent processing latency after calibration. Shared analysis
  infrastructure could support IDEA-001 visualizers; IDEA-013 file playback could
  later allow calibrating from a clip, but neither is required for a first pass.
  Calibration accuracy and development effort are unmeasured; no implementation
  has been prototyped.
- **Open questions:** Calculate versus Learn naming; capture length/prompts;
  Frequency-only versus multi-control suggestions; confidence/retry policy;
  quiet versus live-use mode; and when to mark a result outdated after edits.
- **Next step:** Start with de-esser Frequency/Threshold suggestions and Gate
  Threshold as separate experiments. Evaluate against varied non-personal test
  clips and listening review, including counterexamples where calibration should
  decline. Specify the guided flow before implementing or capturing user audio.
- **Review decisions (2026-10-23):** The earlier optional A/B suggestion is now
  deferred with [IDEA-045](IDEAS_LATER.md#idea-045), not a prerequisite for Learn.
  Measured speaker/output-delay calibration is another possible P4 extension:
  estimate alignment between listening devices, beyond today's manual Alignment
  Delay. It needs a separate measurement/reference design and explicit audio-test
  flow, and is distinct from automatic internal branch compensation (IDEA-040).

<a id="idea-022"></a>
## IDEA-022: Save selected node sections as User Presets

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P2.
- **Maintainer request and rationale:** Reuse useful sections of a node setup.
  **Drag a selection area → right-click → Save Preset → enter a name.** Add a
  **User Presets** dropdown beside **Activity** for accessing saved sections.
  **Placement review, 2026-10-25:** Activity has since moved into Settings
  ([TWEAK-003](TWEAKS.md#tweak-003)); retain the original request above and review
  the preset dropdown's final location when designing this feature.
- **Proposed content:** Store selected nodes, their sound settings, relative
  layout and internal audio/control/sidechain connections. Reuse the existing
  box-selection and `GraphClipboard` remapping patterns. Loading a preset should
  insert an identifiable new section with fresh IDs, as a single Undo operation.
  This insertion behavior is a design assumption for review, not a confirmed
  replacement/overwrite workflow.
- **Project fit / research:** `GraphWorkspace.cs` has selection and clipboard
  controls; `GraphClipboard.cs` handles selected groups. No named preset
  library exists. Local review only; no external feasibility research performed.
- **Scope, tradeoffs and dependencies:** Medium persistence and UI work. Decide
  placement, name collisions, rename/delete, missing node versions and device/app
  bindings. Connections crossing the selection boundary need a clear policy;
  do not silently reconnect to unrelated existing nodes. Keep presets compatible
  with layout locking (IDEA-017) and future sound recall (IDEA-043).
- **Scope boundary:** Scenes and A/B comparison are explicitly much later
  (IDEA-045). Canvas groups (IDEA-023) organize nodes; they are not automatically
  saved presets. Reusable encapsulated subgraphs with exposed controls are a
  possible P4 extension, not part of this first saved-section workflow.
- **Next step:** Review saving and inserting one short effects chain, including
  an external input/output boundary, then define the portable preset data contract.

<a id="idea-023"></a>
## IDEA-023: Named organizational groups with automatic bounds

Implemented and locally integrated into Beta 6 on **2026-10-28**. The clarified
request, implementation decisions and verification are retained in the
[implemented entry](IDEAS_IMPLEMENTED.md#idea-023). Beta 6 remains in development;
this does not finalize or publish a release.

<a id="idea-024"></a>
## IDEA-024: Guided introduction to nodes, noodles and navigation

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P2.
- **Maintainer direction and need:** Keep the program explorable, but first teach
  users how to navigate and how nodes link together. This replaces the proposed
  guided virtual-route setup wizard as the preferred onboarding investment.
- **Proposal:** A short optional walkthrough covering pan/zoom, selecting/moving
  nodes, connecting sockets, following audio flow, and understanding listening
  versus outgoing destinations. Use an isolated example that cannot alter the
  live workspace or send practice audio to a call. Finish by letting users explore.
- **Project fit / research:** [Current Help](USER_GUIDE.md)
  already contains interactive control practice. Review its reusable components;
  the exact tutorial sequence and hosting surface remain to be designed. No new
  external research. The earlier removal of individual canvas/Voice Color Help
  lessons is retained; this is a newly scoped guided mixing introduction.
- **Scope, tradeoffs and dependencies:** Medium UI/content work. Prefer a few
  clear steps, skip/restart/exit controls and pointer/keyboard parity. Do not make
  driver installation a prerequisite for learning the graph. IDEA-031 templates
  may follow later; the tutorial does not depend on new DSP nodes.
- **Next step:** Review a storyboard with the maintainer and test whether a new
  user can trace a simple split route without adding permanent interface clutter.

<a id="idea-025"></a>
## IDEA-025: Why no sound? with a highlighted blocking node

- **Captured:** 2026-10-23. **Status:** Needs decision. **Priority:** P2.
- **Maintainer request and rationale:** Right-click a node and choose **Why no
  sound?** Highlight the node blocking that path until it is selected. Use a
  **strong yellow outer glow**, substantially more prominent than today's faint
  red missing-device glow. Development needs substantial guided UI review.
- **Initial scope assumption:** Offer the action on processing/output nodes and
  trace upstream within VineMix. Excluding input nodes was tentative; do not claim
  to diagnose a microphone, another application's settings or an entire system.
  An observed unavailable input can still be identified as an engine-visible
  route boundary when tracing from a downstream node.
- **Project fit / research:** `GraphNodeView.cs` has local availability states
  and an outer-glow pattern; `GraphWorkspace.cs` has graph topology/selection.
  Neither establishes a complete causal silence diagnosis. Local review only;
  no prototype or fresh external research.
- **Scope and unresolved behavior:** Medium to large diagnostic/UI work. Handle
  multiple incoming branches, muted cables, muted nodes, unavailable devices,
  closed gates and genuinely quiet sources. Do not flag a harmless muted branch
  as the sole blocker when another path supplies audio. A cable blocker needs an
  agreed node to highlight or a clear cable-specific result; do not invent a node
  fault. If evidence is inconclusive, say so briefly rather than guess.
- **Interaction assumptions:** Selection clears the diagnostic glow; Escape or
  a new request can also dismiss it. If the condition clears first, remove the
  stale highlight. Preserve red availability meaning, avoid rapid flashing and
  provide a short accessible explanation. Do not automatically unmute or reroute.
  A dedicated inherited-controller explanation remains declined.
- **Next step:** Review examples for one definite blocker, a muted cable, two
  upstream branches and no diagnosable blocker before committing to UI behavior.

<a id="idea-026"></a>
## IDEA-026: Complete graph keyboard and accessibility support

Moved on 2026-10-24 to [ISSUE-003](KNOWN_ISSUES.md#issue-003).
The report, proposed remedies and research are maintained there; this ID is retained
for existing references and must not be reused.

<a id="idea-027"></a>
## IDEA-027: Pointer-friendly precise value editing

Moved on 2026-10-24 to [ISSUE-004](KNOWN_ISSUES.md#issue-004).
The discarded-edit problem and apply/cancel remedies remain there. Optional
fine-adjustment/reset presentation is now [TWEAK-007](TWEAKS.md#tweak-007).
This ID is retained for existing references and must not be reused.

<a id="idea-028"></a>
## IDEA-028: Navigation through crowded and oversized graphs

Moved on 2026-10-24 to [ISSUE-005](KNOWN_ISSUES.md#issue-005).
The report, proposed remedies and research are maintained there; this ID is retained
for existing references and must not be reused.

<a id="idea-029"></a>
## IDEA-029: Route inspection, diagnostics and useful activity history

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P3.
- **Origin and need:** Prior UI/audio research suggestions. Understand what
  reaches an output and obtain useful troubleshooting evidence without a wall
  of technical controls in normal use.
- **Proposal:** An explicit command highlights a whole connected path or lists
  sources reaching the virtual microphone. Optional details show known processor
  delay, measured device/buffer/queue values, dropouts/reconnections and affected
  routes. Explain differently delayed copies combined at one output. Retain useful
  activity outcomes and offer a reviewed, privacy-conscious diagnostic copy/export.
- **Project fit / research:** [Audio timing](AUDIO_LATENCY.md) documents available
  snapshots and their limits. About already copies version information; Activity
  and node status already exist. No fresh external research. Sampled queue values
  must not be presented as measured acoustic end-to-end latency.
- **Scope, tradeoffs and dependencies:** Medium to large; reuse IDEA-025's route
  analysis where useful. Keep cable hover as the approved simple source/destination
  text. Do not revive declined inherited-controller explanation UI. Reports need
  explicit handling of device names, identifiers, paths and bounded log history;
  no automatic audio recording or upload is proposed.
- **Next step:** Specify one output-inspection view and a minimal sanitized
  report, separating known facts, estimates and unavailable measurements.

<a id="idea-030"></a>
## IDEA-030: Selected-node inspector at a readable size

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P2.
- **Origin and need:** Prior UI-review suggestion. Operate a selected effect
  while the canvas remains zoomed out to understand its route.
- **Proposal:** An optional fixed-size inspector showing the selected node's
  existing controls and live state. It references the same node and performs no
  duplicate processing. Selection changes must handle pending edits predictably.
- **Project fit / research:** Current controls are inside zoomed nodes. Compare
  this design with [pinning](#idea-005), which keeps chosen nodes visible rather
  than following selection. Repository review only; no prototype/research.
- **Scope and tradeoffs:** Medium; additional screen space, focus and shared
  control-binding ownership. Do not commit to both an inspector and a large
  pinned strip without checking their combined effect at minimum window size.
- **Next step:** Compare the selected-node view and pinning using an EQ and a
  compact dynamics node; select the least cluttered interaction.

<a id="idea-031"></a>
## IDEA-031: Task templates and favorite or recent node types

- **Captured:** 2026-10-23. **Status:** Voice starter verified and locally
  integrated for unreleased 0.5.0 on 2026-11-06; other proposals captured.
  **Priority:** P3.
- **Origin and need:** Prior UI-review suggestion. Reduce repeated setup after
  users understand the graph, without limiting exploration.
- **Proposal:** Optional examples for hearing a microphone, sending voice/music
  to a call, routing an app and ducking music. Offer favorite/recent node choices
  within creation UI. These are setup templates, not the multi-action tasks in
  IDEA-004; a clearer product label should avoid that ambiguity.
- **Project fit / research:** The three-node starter, Add search and purpose
  descriptions already exist. Reuse User Presets (IDEA-022) and build on the
  guided introduction (IDEA-024). Local inspection only; external research pending.
- **Scope and tradeoffs:** Small for favorites, medium for well-explained reusable
  examples. Templates need clear device placeholders and intentional output
  activation; they must not replace an existing workspace by surprise.
- **Next step:** Identify two frequently repeated setups and decide whether a
  curated preset is sufficient before building a separate template system.

### Voice-focused starting graph proposal — 2026-11-06

- **Status:** Authorized, implemented, verified and locally integrated for
  unreleased 0.5.0 on 2026-11-06.
  The maintainer accepted the revised order and requested a default with clean,
  generous spacing so cables remain visible. Other IDEA-031 template/favorite
  proposals remain unselected.
- **Need/proposed behavior:** Give new users a useful voice-processing route
  they can understand immediately: Microphone → High-pass → Expander →
  Compressor → Denoiser, with the final output connected to both Listening
  output and Virtual output. This develops the earlier optional-template idea
  into a potential first-run default. No additional splitter node is needed.
- **Selected order, 2026-11-06:** Microphone → High-pass →
  Denoiser → Expander → Compressor → both outputs. Putting denoising before
  dynamics lets them respond to the cleaned signal and gives RNNoise speech
  before expansion/compression alters its level envelope. This is engineering
  judgment, not a proven universal order or an independently listened-to result.
  Use gentle starter settings; do not assume the screenshot's exact thresholds
  or gains suit every microphone, room or speaking level. Use the existing
  new-node effect defaults shown in the accepted reference, neutral node names,
  and no copied device assignments.
- **Evidence/research, 2026-11-06:** The desktop `GraphDefaults.Fresh` and native
  `defaultConfiguration` at proposal time created only an input and two directly
  connected outputs. [OBS noise-suppression guidance](https://obsproject.com/kb/noise-suppression-filter)
  supports the noise-cleanup role. [OBS expander guidance](https://obsproject.com/kb/expander-filter)
  emphasizes threshold adjustment to avoid cutting speech and suggests an
  alternative placement after compression; order therefore remains a listening
  decision. The local RNNoise implementation reports 1,440 frames / 30 ms of
  delay, retained in bypass and at zero Mix; endpoint buffering adds further
  monitoring delay. No listening or performance test was run for this proposal.
- **Scope/dependencies:** Small fixed starter-graph change using existing nodes;
  coordinate desktop/native factories, first-run verification, layout/Fit and
  documentation. A preset framework is not required. If later exposed as a
  reusable optional example, coordinate with IDEA-022 and IDEA-024.
- **Assumptions/tradeoffs:** Voice/call/streaming is the intended first-use case;
  this is not a general music-processing default. Preserve both outputs muted,
  the existing conservative Listening level, explicit physical-device selection
  and recognized virtual-device binding policy. Do not store the screenshot's
  device assignments or personal graph, and never replace a saved workspace.
  More processing adds control complexity and monitoring latency; quiet speech
  may need a lower expander threshold. Review readability at normal and minimum
  window sizes rather than forcing the whole chain below full-detail zoom.
- **Selected layout:** Five processing/source nodes share the same top edge,
  with 95 canvas DIPs between each node body. Listening output sits above
  Virtual output at the right, also separated by 95 DIPs. Existing first-run
  Fit adapts the view to the available window; the saved fallback view is 40%.
  The preliminary 100-DIP spacing was narrowed slightly so minimum-width Fit
  reaches the full-detail threshold even after zooming out.
- **Verification/integration:** 225 detached checks / 22 renders / zero binding
  errors, 38 preparation/recovery checks, 802 real native/desktop boundary
  checks, 289,921 graph assertions and the full synthetic engine lifecycle
  passed. Normal/minimum-size screenshots were reviewed; minimum-width Fit
  restores full controls from Quiet Blocks at 30.05%. Packaged smoke and guarded
  paired integration passed, preserving the personal graph/settings/preferences
  and restoring all previously running paths. All installed/candidate payloads
  and source provenance were verified. See the
  [execution record](IMPLEMENTATION_0.5.0.md#voice-starter-workspace--6-november-2026).
- **Next step:** Maintainer visual follow-up and final release qualification.
  Listening remains separate; the layout change does not establish ideal
  processing values for every voice. Reusable templates/favorites remain proposals.

<a id="idea-032"></a>
## IDEA-032: More informative overload readings

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P3.
- **Origin and need:** Prior UI-review suggestion. Determine which channel and
  processing stage exceeded full scale, with a supported peak magnitude.
- **Proposal:** Improve engine telemetry and optional inspection to identify the
  first observed overloaded stage and channel, and report above-zero magnitude
  if measured. The separate optional moving-peak hold refinement moved to
  [TWEAK-008](TWEAKS.md#tweak-008) on 2026-10-25; it needs no new telemetry.
- **Project fit / research:** [Existing meter behavior](NODE_WORKSPACE.md) reports
  node-level overload and caps numerical readings at 0 dBFS; it is sampled
  telemetry, not complete lifetime history. Local evidence only.
- **Scope, tradeoffs and decisions:** Medium telemetry/UI work. Preserve passive
  hover readings and the accepted fading red caps. Do not add the rejected
  permanent clipping latch or click-to-reset interaction. LUFS/true-peak meters
  belong to IDEA-001, not this sample-overload refinement.
- **Next step:** Define the exact measurable fields and one compact hover/route
  view before changing capture cadence, meter persistence or audio processing.

<a id="idea-033"></a>
## IDEA-033: New voice cleanup and leveling nodes

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P3.
- **Maintainer direction and rationale:** The recovered voice-cleanup/leveling
  family is worth pursuing as future nodes, after basic usability work. Reduce
  manual correction for everyday voice use. This endorses the direction, not a
  complete algorithm list or demonstrated feasibility/quality.
- **Candidate behaviors:**
  - Automatic voice leveler: bounded ongoing compensation for changing speaking
    distance/level, with an explicit policy for silence and background noise.
  - Dynamic EQ: reduce troublesome frequency regions only when needed.
  - Hum removal: consider an existing-EQ preset before adding a redundant node.
  - Mouth-click reduction and dereverberation: explore separately, with listening
    checks for speech damage and actual processing delay.
  - Acoustic echo cancellation: investigate a playback-reference input and its
    routing/timing requirements before selecting an implementation.
  - Automatic multi-microphone mixing: reduce unwanted spill from unused mics
    while preserving simultaneous speakers and predictable transitions.
- **Project fit / research:** `NodeCatalog.cs` and `native/include/am/graph.hpp`
  lack these dedicated types. Existing EQ/dynamics provide reusable patterns;
  this does not prove they solve the new tasks. No fresh external algorithm or
  licensing research, benchmark or prototype has been performed.
- **Scope, tradeoffs and dependencies:** Medium to large per candidate, especially
  reference-based or model-based processing. Assess latency, CPU, quality, model
  and license needs independently. Ongoing leveling is distinct from one-time
  calibration (IDEA-016); avoid growing the catalogue with duplicate processors.
- **Next step:** Compare an automatic leveler and dynamic EQ as the first two
  candidates on paper, then choose one for primary-source feasibility research
  and representative non-personal listening fixtures.

<a id="idea-047"></a>
## IDEA-047: System-latency investigation or separate helper

- **Captured:** 2026-10-23. **Status / priority:** Parked, low investment.
- **Maintainer direction:** Further heavy engine-latency work is not worthwhile
  at present. The maintainer believes remaining delay is mainly specific to each
  user's system; a separate general system helper might merit light investigation
  much later. Keep that separate from VineMix feature development.
- **Evidence and qualification:** The first shared-device period/queue/scheduling
  improvements already landed ([audio latency](AUDIO_LATENCY.md)). Current
  denoiser nodes still add 20/30 ms. Earlier investigations did not isolate every
  upstream, engine and playback contribution with calibrated same-path hardware
  measurements. Therefore neither negligible engine delay nor a universal
  system patch is established. No new measurement or external research done.
- **Possible scope and tradeoffs:** A standalone diagnostic/helper application
  could identify specific system contributors and explain supported remedies.
  This is an unresearched possibility, not a proposal for indiscriminate driver,
  registry or device changes. Broad engine rewrites, smaller-block experiments
  and new denoiser latency work are deferred; accurate delay labels were delivered
  in the local candidate under [IDEA-019](IDEAS_IMPLEMENTED.md#idea-019).
- **Dependencies / next step:** Revisit only if a reproducible multi-user problem
  or an explicit new priority justifies it; first establish the affected path and
  evidence before deciding whether a separate tool has a useful general role.

<a id="idea-048"></a>
## IDEA-048: Developer API for third-party apps and automation

- **Captured:** 2026-10-23. **Status:** Captured. **Proposed priority:** P3;
  enable community integrations after the core usability work. This placement
  is a suggestion, not a maintainer commitment or authorization to implement.
- **Maintainer intent / rationale:** Let users develop their own applications
  that inspect and control VineMix, especially streaming tools and automation,
  without waiting for each integration to be implemented in the main product.
- **Proposed first scope:** A documented, versioned local control API. Discover
  nodes and available parameters with stable IDs, types, units and limits; read
  state; set volume, mute, bypass and supported effect parameters; subscribe to
  state changes and bounded meter updates. An external button should reflect
  changes made in VineMix as well as changes made by its own app. Publish small
  example clients and a simple command-line wrapper, with a reusable client
  library if useful. These are proposed interfaces, not existing commands.
- **Examples:** A community-built streaming button that mutes a microphone; a
  custom mixer dashboard; a script changing levels in response to another app;
  an integration that applies a coordinated set of existing-control changes.
  Support for named tasks/presets follows their implementation and is not a
  prerequisite for basic control. Existing DSP capabilities bound what external
  apps can do; this is not arbitrary audio-plugin execution inside the engine.
- **Repository evidence (2026-10-23):** [The internal protocol](CONTROL_PROTOCOL.md),
  `native/src/control_server.cpp` and
  `desktop/VineMix.Desktop/EngineConnection.cs` show JSON requests over a local
  current-user/SYSTEM named pipe, remote-client rejection, snapshots and full
  configuration commits guarded by revision. The server currently creates one
  pipe instance and serves that client until disconnection. It is therefore a
  useful foundation, not a ready multi-client developer API. Internal snapshots
  also include personal paths and process/device details that a public control
  view need not disclose. No running-engine experiment was performed.
- **Design direction and alternatives:** Consider JSON over a local WebSocket
  gateway for requests and events, versus a native pipe client library or an
  HTTP command interface with a separate event channel. The transport remains
  undecided. A gateway must share a command coordinator with the desktop or
  follow a deliberate engine multi-client change; simply attaching a second
  client to the current pipe is not sufficient. Prefer targeted, validated
  parameter operations over exposing full-settings replacement, recovery or
  shutdown commands by default. Keep external control working while the window
  is minimised; operation after the desktop process exits remains a lifecycle
  decision.
- **Research (2026-10-23):** [OBS's official WebSocket project](https://github.com/obsproject/obs-websocket)
  documents third-party automation, remote-control clients and language client
  libraries. This is evidence for the ecosystem pattern, not proof of VineMix
  integration or a decision to reuse its code. VineMix-specific transport,
  performance and compatibility prototyping remains pending.
- **Scope, tradeoffs and dependencies:** Medium-to-large foundational work:
  API compatibility, discovery, documentation, concurrent edits, reconnects,
  bounded event delivery and error handling. Use explicit API opt-in and
  revocable client credentials; start on the local machine, with network access
  a separate decision. Keep read/control permissions distinct and expose only
  needed data. A transport credential does not isolate all software running as
  the same Windows user. Define manual override, revision/conflict handling and
  undo behavior. Keep parsing off the audio thread and design continuous fader
  updates without a settings write or full graph rebuild per event.
- **Related ideas:** [Tasks](#idea-004), [external condition triggers](#idea-006)
  and [parameter controllers](IDEAS_LATER.md#idea-038) can share stable parameter
  identities and command semantics. This API is a separate developer-facing
  foundation; users may implement their own conditions outside VineMix without
  waiting for its built-in automation editor. Pinning (IDEA-005) remains an
  in-app presentation feature, not a requirement for external controls.
- **Next step:** Specify a minimal discovery/read/set/subscribe contract and a
  sample microphone mute/volume app. Test simultaneous desktop and external
  changes, reconnects, missing targets, rejected credentials and excessive
  update rates in isolated fixtures before exposing the personal engine.

<a id="idea-050"></a>
## IDEA-050: Website effects showcase

- **Captured:** 2026-10-26. **Status:** Needs decision; visual exploration
  requested, implementation and publication not yet selected.
- **User need:** Help homepage visitors discover the effects VineMix includes
  and understand what they do, beyond the current routing demonstration.
- **Placement:** Add a section immediately above **Open source and in beta.**
  (`.trust#open`), after the use-case section. The maintainer explicitly chose
  this temporary position rather than requiring it directly after the node story.
- **Directions being explored:** An **effects shelf**, with native nodes in a
  horizontally browsable gallery; an **effect spotlight**, with a grouped index
  and one readable native node; and **example effect chains**, pairing a useful
  outcome with several connected effects. The chains are illustrative website
  examples, not a claim that the application already supplies these presets.
- **Concept images, displayed order:**
  [1 — effects shelf](../website-redesign/reference-assets/effects-concepts/01-effects-shelf.png),
  [2 — effect spotlight](../website-redesign/reference-assets/effects-concepts/02-effect-spotlight.png),
  [3 — example chains](../website-redesign/reference-assets/effects-concepts/03-example-chains.png).
  These are image-generated layout proposals, not implemented screens. Native
  control details and cable socket alignment must be corrected from actual
  application renders in a selected implementation. The live site is unchanged.
- **Assumptions:** Continue the current dark page, mint accents, IBM Plex type
  and native node artwork. Keep examples silent. Show genuine current effects;
  do not imply planned pitch shifting, distortion, plugin hosting or recording.
- **Evidence:** Existing effects and limitations checked against
  [the user guide](USER_GUIDE.md#node-library) and local website source
  on 2026-10-26. Page and native-node screenshots ground the concept images.
  No external research or usability study has been performed for these layouts.
- **Scope and tradeoffs:** One responsive homepage section. The shelf shows
  breadth, the spotlight gives controls more readable space, and the chains
  explain combinations but repeat some of the preceding routing story. A later
  implementation needs current native effect renders and accessible selection
  controls with a static fallback; no audio engine or new DSP is required.
- **Dependencies:** Existing [scroll story](IDEAS_IMPLEMENTED.md#idea-049) and
  [website refinements](TWEAKS.md#tweak-009). Image-generated controls are concept
  illustrations; implementation should use verified native artwork.
- **Next step:** Review the three independent concept images, choose or refine
  a direction, then implement the selected section locally.

<a id="idea-051"></a>
## IDEA-051: Website quick-access header and matching actions

- **Captured:** 2026-10-26. **Status:** Implemented locally after the maintainer
  selected example 1; visual review pending. No public deployment.
- **User need:** Make the main sections easy to reach and give the landing
  actions even dimensions within a coherent header/button design.
- **Final paired concepts, displayed order:**
  [1 — quiet frame](../website-redesign/reference-assets/header-button-concepts/01-quiet-frame.png)
  pairs a minimal ruled header with separate rectangular actions;
  [2 — floating navigation](../website-redesign/reference-assets/header-button-concepts/02-floating-navigation.png)
  pairs a floating pill header with rounded actions;
  [3 — unified toolbar](../website-redesign/reference-assets/header-button-concepts/03-unified-toolbar.png)
  pairs a full-width toolbar with joined equal-width actions. These three
  image-generated proposals supersede the earlier two drafts containing a
  pause button. They illustrate layout, not exact font or spacing measurements.
- **Selected implementation:** The quiet-frame header uses the existing native
  VineMix wordmark in white, right-aligned links and a faint bottom rule.
  **How it works** links to `#sound`, **Setup** to `#setup`, **FAQ** to `#faq`,
  and **Docs** to the existing user guide. A later request replaces Docs' subdued
  fill with a white pill, black text and black arrow. A further correction reduces
  its height from 44 to 36 px, with 13 px text and a 12 px arrow; forced-colour
  system styling remains. Navigation stays within the hero and scrolls away;
  the logo now persists separately as described below.
  At 720 px and below, JavaScript enhances the links into a **Menu** disclosure
  with a compact panel. Without JavaScript, the compact links remain visible.
  Escape, outside click, link activation and focus leaving dismiss the panel;
  breakpoint changes manage keyboard focus. The latest hero uses a single
  centred Download action using Flow Hover's transparent rest and sweeping white
  fill, with a stationary download icon/label,
  replacing the earlier equal pair under
  [TWEAK-012](TWEAKS.md#tweak-012), and removes its platform/beta note. The removed pause control, dark/mint styling
  and reader sequence are retained.
- **Persistent logo extension, 2026-10-26:** The maintainer requested that the
  header logo smoothly centre as the header leaves view, retract its lettering
  into the VM symbol like the main hero, and provide a return-to-top link.
  The same native link now settles into a fixed 44 × 44 px target, centred
  12 px below the viewport top. The initial circular backing was subsequently
  rejected; only a tight SVG drop shadow remains, disabled in forced colours. Reverse
  scrolling restores the expanded logo. Activating it closes the mobile menu
  and returns to `#top`; ordinary modified links keep their native behavior.
  Reduced motion uses an instant change after header exit, and no JavaScript
  retains the expanded logo and native header links. The story and anchor
  offsets reserve 72 px above content for the persistent mark.
- **Evidence and scope:** Existing anchors, guide destination and styles were
  inspected in [homepage source](../website-redesign/src/index.html) and
  [styles](../website-redesign/src/styles.css) on 2026-10-26. No external design
  research or usability study was performed for the initial header. The later
  Download reference is documented in [provenance](../website-redesign/PROVENANCE.md).
  The selected implementation uses local
  brand assets rather than the generated image's approximate lettering. The
  initial fully scrolling header remains part of the design history; the later
  request makes only the brand persistent, with matching content clearance.
- **Correction and dependency:** The maintainer separately authorized removing
  the pause control and fixing the current action sizes; that local refinement
  is [TWEAK-012](TWEAKS.md#tweak-012). The subsequent explicit selection authorized
  implementing the quiet-frame header; the other two directions remain history.
- **Tradeoff:** The quiet frame leaves most attention on the hero;
  floating navigation has a stronger rounded shape; the toolbar provides a more
  continuous navigation surface.
- **Verification and next step:** Initial header build, source, actual-module
  interaction, bundled-font/layout and served-output checks passed. The later
  Download/boxed-Docs revision passed build, independent source review,
  header font-fit calculations and served-output checks. It has no new browser
  verification. The persistent-logo implementation passed build and all three
  check suites, including actual-module scroll/lifecycle checks and 72 px story
  clearance assertions, plus served-output and SVG/unique-ID checks. Browser and native
  capture connections are unavailable, so visual fidelity remains unverified.
  See [QA](../website-redesign/QA.md) and
  [the visual review](../website-redesign/design-qa.md). Capture desktop and phone
  implementations for comparison, then complete maintainer review.

<a id="idea-052"></a>
## IDEA-052: 0.5.0 node inputs and outputs exploration

- **Current status, 2026-11-04:** Accepted Gate/Expander Trigger and Reverb Wet
  ports are implemented, offline verified and locally integrated for unreleased 0.5.0.
  Other candidate ports and source/destination types remain exploratory.
- **Implemented behavior:** No Trigger cable preserves Gate/Expander self-detection;
  a connected silent, muted or unavailable source remains silent detection.
  Trigger never joins the programme. Reverb Wet is independent of Mix, retains
  stereo reflections and the existing decay/damping/pre-delay treatment.
  Bypass fades Wet to silence without dry leakage; mute silences both outputs.
  The network continues processing incoming audio and advancing tails during
  bypass/mute, then resumes the current result through the existing ramps.
- **Offline evidence:** Synthetic DSP checks cover detector isolation/timing,
  stereo, absent/silent sources, Wet independence, tail transitions and realtime
  allocation limits. Detached port checks cover sockets, snap, validation,
  JSON, clipboard serialization, undo and preserved Audio-route insertion/detach.
  Detach with a Wet branch is rejected rather than substituting dry audio.
  See the [combined execution report](IMPLEMENTATION_0.5.0.md).
- **Local integration and next step:** Matching binaries passed guarded live
  activation and preserved each update's then-current workspace/settings. At
  that earlier checkpoint, new-route listening had not been performed.
- **Maintainer-reported manual results, 2026-11-04:** The maintainer subsequently
  reports both Trigger cases passing, and successful Reverb Wet independence
  from Mix, other Reverb controls and Bypass. These results were not independently
  observed and do not extend to every silent/missing-source or tail-transition
  case. The manual checklist incorrectly referred to muting Reverb: the UI has
  Bypass and no Mute affordance. Retained model/DSP muted-state tests remain valid,
  but are not a manual UI result. No new Mute control is requested.
- **Next step:** Retain automated port regressions and these reported results;
  keep broader listening qualification separate. No release has been published.
  Unselected source/destination ideas need a separate decision.

### Planning and research history — 2026-11-04

- **Requested and updated:** 2026-11-04. **Status at planning:** Gate/Expander Trigger and
  Reverb Wet planned; other ports exploratory. The separately selected meter
  gradient belongs to TWEAK-021. **Target:** 0.5.0. **Priority:** P3, provisional.
- **Audit clarification, 2026-11-04:** The
  [current release plan](#release-0-5-0-plan) distinguishes accepted scope from
  recommended detector/bypass/mute/tail defaults. Those behaviour contracts must
  be settled before implementation. Extra ports require actual engine buffers
  and port-aware channel/telemetry handling, not just new sockets. In particular,
  `graphSignalChannels` currently treats every non-`audio` source port as mono
  because existing alternate outputs are Left/Right. A future Reverb Wet port
  must not inherit that assumption. Include stereo routing, fan-out, sidechain
  use, copy/paste/history and old workspace loading in qualification.
- **Maintainer direction:** Explore both additional connection sockets on existing
  nodes and new audio source/destination types. This clarifies the original broad
  request. The subsequent selection below supersedes the initial shortlist;
  remaining suggestions are not selected for implementation.
- **Confirmed first three changes, 2026-11-04:**
  1. Smooth green/yellow/red meter gradients, tracked in
     [TWEAK-021](TWEAKS.md#tweak-021).
  2. Gate/Expander Trigger inputs. Without a Trigger cable, use the node's own
     incoming audio as its detector, preserving current behaviour. With a cable,
     use that separate signal to control attenuation of the main audio; never
     mix the trigger into the output. A clean microphone branch controlling a
     processed voice branch is the agreed explanatory example. Connected-but-
     silent, muted and missing-source behaviour still needs specification.
  3. A separate Reverb Wet output, described by the maintainer as a "wetness
     output". Supply reverberation alone, independently of the Mix dial, while
     retaining the existing combined Audio output. Decay, Damping and Pre-delay
     still shape the wet signal. Mix remains the dry/wet balance for the normal
     Audio output. Final socket wording and bypass/mute/tail behaviour remain
     to be specified. The assistant's suggestion to rename Mix to Dry/Wet was
     not separately approved and is not a fourth confirmed change.
  These are confirmed feature-planning decisions, not completed implementation
  or authorization to install or publish a build.
- **Later scope addition, 2026-11-04:** The maintainer requested investigation
  of ISSUE-024 and ISSUE-011 and selected more zoom-out, progressive node detail
  reduction and a minimap for ISSUE-005. See the
  [current 0.5.0 plan](#release-0-5-0-plan); the first three remain unchanged.
- **Need:** Make useful routing, processing and playback/recording workflows
  possible without making ordinary nodes harder to understand.
- **Current source evidence (2026-11-04):** Reviewed `NodeCatalog.cs`,
  `GraphModels.cs`, `GraphNodeView.cs`, `native/include/am/graph.hpp`,
  `native/src/graph.cpp` and the existing backlog. Sources are Physical input,
  Virtual input and Running app; destinations are Listening output and Virtual
  output. Audio can already branch to multiple destinations and multiple cables
  can feed an input. Crossfade has A/B inputs, Stereo Combine has Left/Right
  inputs, and Stereo Split has Left/Right outputs. Only Ducking accepts a
  detector/Trigger cable. Linked control is deliberately limited to Level-knob
  nodes for new connections; retain [TWEAK-018](TWEAKS.md#tweak-018).
  Dynamic Ducking already follows an external signal with a compressor curve.
  The graph has typed connections, bounded processing and feedback prevention;
  arbitrary new sockets are not just a layout change.

### Additional sockets — proposed options

| Candidate | Useful behaviour | Fit, alternatives and likely scope |
| --- | --- | --- |
| Gate / Expander Trigger input | A clean voice branch opens a gate on a separately processed voice branch, without mixing the trigger into the sound. | Strong first investigation; medium scope. Reuse the detector-cable concept, but add detector-aware processing, validation, persistence and UI. No connection should preserve existing self-detection. Decide connected-but-silent, muted and missing-source behaviour explicitly. |
| Compressor Trigger input | Compress one signal according to another signal or a filtered detector branch. | Medium scope, but lower priority: Dynamic Ducking already overlaps substantially. Require an example that needs Compressor's own controls before adding another way to do the same job. Preserve Limiter's own-signal ceiling protection. |
| Reverb Wet output | Send only the reverb tail to a separate Mix for level/EQ, while keeping the existing mixed Audio output. | Medium scope. First compare the existing recipe: branch the original signal and set a separate Reverb to 100% wet. An extra socket is convenience unless simultaneous wet and mixed outputs are needed. Define bypass, mute and tail behaviour; do not expose an internal bypass-blend buffer as a new output without checking its meaning. |
| Mix pre-Level output | Feed a recording or auxiliary mix whose level is independent of that Mix's Level knob. | Medium scope. Branching upstream into another Mix already covers many cases. Specify whether mute and Linked control affect the tap; avoid silently bypassing mute. Add only if it materially simplifies real graphs. |
| Parameter-control inputs / detector-value outputs | A macro or envelope controls crossfade position, effect amount or another parameter. | Large scope; retain under [IDEA-038](IDEAS_LATER.md#idea-038). Needs typed/ranged control values, smoothing, ownership and transient updates. Existing Linked control is not a general modulation signal. |

Initial recommendation was to investigate Gate/Expander Trigger first and compare
existing recipes before selecting Reverb Wet or Mix pre-Level sockets. The
maintainer has now selected Gate/Expander Trigger and Reverb Wet above; Mix
pre-Level remains exploratory. Keep optional
sockets unobtrusive, but connected sockets always visible. This is a proposed UI
principle, not an approved interaction design.

### New sources and destinations — proposed options

| Candidate | User benefit | Assessment and existing owner |
| --- | --- | --- |
| Audio File input | Play clips, loops and soundboard audio directly into effects or a mix. | Strongest new-source candidate; medium-to-large scope. Continue [IDEA-013](#idea-013), including drag/drop, Play/Stop, Loop, missing-file handling and a bounded decoding pipeline. Global shortcuts remain a later phase. |
| Recording output | Connect any chosen graph point to an explicit recorder for a voice take or finished mix. | Strongest new-destination candidate; medium-to-large scope. Continue [IDEA-036](IDEAS_LATER.md#idea-036). Suggest one stereo WAV recording first, with explicit start/stop, path, elapsed time and failure feedback; multitrack/replay remain later. |
| Desktop Audio input | Capture the combined playback on a selected Windows output device. | Medium-to-large scope, useful when users want the whole device mix instead of selecting apps. Endpoint loopback is supported by Windows, but routing VineMix back to that device can recapture its output. Needs a concrete feedback-prevention policy, device-switch handling and duplicate-playback guidance before selection. |
| More virtual endpoints | Choose additional compatible virtual cables for existing input/output nodes. | Continue [IDEA-015](#idea-015); principally endpoint selection and compatibility work, rather than another socket family. Ordinary Windows and streamed devices already have coverage; do not present them as wholly missing. |
| Test Signal input | Supply a tone/noise source for checking routing and effects. | Smaller than file playback, but narrower everyday value. Continue [IDEA-042](IDEAS_LATER.md#idea-042), with explicit enable and conservative initial level. |
| Network send/receive, plugin audio ports, hardware insert | Connect other computers, plugins or an external processor. | Large separate investigations under [IDEA-037](IDEAS_LATER.md#idea-037). No protocol, SDK or dependency selected; defer from the initial 0.5.0 shortlist. |

### Research, boundaries and next decision

- **Primary sources checked 2026-11-04:**
  [Cockos ReaPlugs](https://www.cockos.com/reaper/reaplugs/index.php) documents
  compressor and gate sidechain capabilities, establishing a useful precedent,
  not VineMix compatibility or permission to reuse implementation code.
  [Microsoft WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
  documents capture of a playback endpoint's mix in shared mode. The recapture
  concern above is an inference from feeding that same endpoint.
  [Media Foundation Source Reader](https://learn.microsoft.com/en-us/windows/win32/medfound/source-reader)
  supports decoding samples but leaves presentation timing to the application;
  it is an option for IDEA-013, not a selected decoder. Recording format/library
  choices and network/plugin feasibility have not been researched in this pass.
- **Scope and dependencies:** Effort labels are comparative source-review
  estimates, not schedules. New sockets need matching desktop/engine validation,
  saved port identities, undo/copy compatibility, labels, metering semantics and
  routing checks. New audio sources/sinks also need lifecycle, buffering and
  unavailable-device/file handling. Keep file I/O and decoding off the audio
  callback. Preserve existing workspace sound and prevent feedback cycles.
- **Earlier assistant shortlist (superseded by the maintainer's selection):**
  Meter gradient (TWEAK-021), Gate/Expander Trigger,
  Audio File input and basic Recording output. Investigate these together, then
  size the work before committing all of them to 0.5.0. Desktop Audio is a
  secondary option pending its feedback design. Other rows remain alternatives.
- **Next step:** Specify remaining socket behaviours and meter transition widths
  for the selected changes, using the release plan's decision table. Further
  candidate exploration does not expand accepted 0.5.0 scope automatically.
  Audio File input and Recording output remain unconfirmed proposals, with file
  lifecycle decisions pending if selected. No app code, dependencies, live settings or release
  identity changed; no runtime prototype or performance verification is claimed.

<a id="idea-053"></a>
## IDEA-053: Bottom-right canvas minimap

- **Current node-detail selection, 2026-11-05:** Quiet Blocks under
  [TWEAK-025](TWEAKS.md#tweak-025) replaces all low-detail interior shapes and
  decorative symbols; it is implemented, offline verified and locally integrated. The minimap's
  own presentation and saved visibility setting are unchanged.
- **Earlier related node-detail correction, 2026-11-04, now superseded:** [ISSUE-005](KNOWN_ISSUES.md#issue-005)
  used fewer control silhouettes and a dedicated darker node-content shade,
  verified offline and locally integrated. The oversized interior-filling
  interpretation is superseded; one fixed 72-world-DIP dial diameter across
  simplified nodes with regular compact spacing is verified and locally integrated. The
  minimap stays `#4A5058`.
  Earlier matching-name-text and matching-silhouette-shade requirements
  below are retained as history, not the current node-detail direction.
- **Earlier matching-shade refinement, 2026-11-04 — implemented, offline verified and
  locally integrated:** Darker minimap node silhouettes and centred simplified-node
  names ([ISSUE-005](KNOWN_ISSUES.md#issue-005)) share the solid
  `CanvasOverviewBrush`, normally `#4A5058`, preserving selection highlighting
  and system high contrast. The maintainer found the previous `#626974` still too
  light. The suite passed 14,748 checks / 314 renders / zero
  binding errors, and screenshots were reviewed. Guarded interface-only activation
  preserved the same engine process/hash and unchanged settings/preferences
  through eight seconds of observation. This remains an unreleased 0.5.0
  refinement under the installed `0.4.0-beta` identity; earlier evidence below
  describes the preceding presentation.
- **Current status, 2026-11-04:** Implemented, offline verified and locally
  integrated for unreleased 0.5.0, including the subsequent visual refinement;
  maintainer follow-up review remains pending.
  The minimap is default-on at the bottom right and shows the
  visible canvas rectangle. Click centres the view; dragging pans with stable
  mapping. Bounds include the viewport when it is outside the graph.
- **Visibility and verification:** **Settings → Show minimap** persists separately
  from audio configuration, hides/restores the overview and leaves graph/audio
  state unchanged. Detached geometry, keyboard/capture, preference, high-contrast,
  viewport and maximum-size graph cases pass in the
  [combined execution report](IMPLEMENTATION_0.5.0.md). No live usability or
  performance claim follows from synthetic rendering alone.
- **Maintainer-reported manual results, 2026-11-04:** Minimap navigation and the
  Show minimap visibility preference's persistence passed the maintainer's checks.
  These were not independently observed and do not establish all pointer/DPI cases.
- **Accepted presentation refinement, 2026-11-04:** Darken the minimap and remove
  its header/title, keeping the useful canvas/viewport overview and existing saved
  visibility setting. This reduces visual clutter; no navigation or audio behavior
  change is requested. Source review and the combined offline run passed
  (14,726 detached checks, 314 renders, zero binding errors). Guarded local
  interface-only integration passed while preserving the engine and current
  workspace/settings. Maintainer follow-up review remains pending.
- **Next step:** Retain the darker, title-free presentation and
  geometry/navigation/persistence regressions, then review the integrated result. Computer-use
  automation is excluded and release remains separate. Optional wheel zoom and
  collapse controls stay deferred.

### Planning and research history — 2026-11-04

- **Requested:** 2026-11-04. **Status at planning:** Planned for 0.5.0; researched.
  **Priority:** P2. Research/planning only at this stage.
- **Confirmed understanding:** A small overview of the whole node canvas in the
  bottom-right corner, with a rectangle indicating the portion currently visible
  in the main workspace. The rectangle moves when panning and changes size when
  zooming. This is an overview navigator, not a magnified node preview or another
  set of zoom buttons. It accompanies the increased zoom-out and reduced-detail
  rendering selected under [ISSUE-005](KNOWN_ISSUES.md#issue-005).
- **Need:** Maintain orientation in a large graph and locate off-screen nodes
  without repeatedly zooming all the way out and back in.
- **Primary-source research, 2026-11-04:** Official ComfyUI frontend inspected at
  commit `59bde61e054ce44873e7b6267726ca27f5476482`:
  [MiniMap.vue](https://github.com/Comfy-Org/ComfyUI_frontend/blob/59bde61e054ce44873e7b6267726ca27f5476482/src/renderer/extensions/minimap/MiniMap.vue)
  anchors the map bottom-right;
  [viewport calculations](https://github.com/Comfy-Org/ComfyUI_frontend/blob/59bde61e054ce44873e7b6267726ca27f5476482/src/renderer/extensions/minimap/composables/useMinimapViewport.ts)
  map graph bounds and the visible world rectangle into the overview;
  [interaction handling](https://github.com/Comfy-Org/ComfyUI_frontend/blob/59bde61e054ce44873e7b6267726ca27f5476482/src/renderer/extensions/minimap/composables/useMinimapInteraction.ts)
  supports pointer capture, click/drag navigation and wheel zoom. Its wheel path
  uses 0.1–10 scale bounds; this is not a requirement to adopt ComfyUI's zoom range
  or wheel behaviour throughout VineMix. Source inspection only; no ComfyUI
  interaction session was run. No third-party source or assets were imported.
- **Proposed first design:** A compact approximately 220 x 140 DIP overlay inside
  the viewport, above footer messages, fixed in screen space. Show simplified
  category-coloured node rectangles, subdued group outlines and a contrasting
  visible-area rectangle. Selection can be highlighted; omit fine controls,
  live meters and tiny labels. Optional faint connections should be evaluated
  for clutter. Exact dimensions and a quick collapse/reopen control remain
  proposals; the Settings toggle is now a confirmed requirement below.
- **Confirmed visibility setting, 2026-11-04:** Add **Show minimap** in the
  workspace section of Settings. Off hides the overview completely; On restores
  it. Persist the preference across restarts, and keep the Settings action
  available while the minimap is hidden. Proposed default: on. Hiding must not
  change canvas position, zoom, selection or audio; stop hidden overview rendering
  work and handle focus safely if the map was focused. A temporary collapse
  button alone does not satisfy the requested setting. Verify immediate toggling,
  persistence and re-enabling, including keyboard and high-contrast use.
- **Proposed interaction:** Click the map to centre the main view there; drag
  the visible-area rectangle to pan while preserving the grab offset. Keep the
  zoom unchanged during these actions. Main-canvas wheel behaviour stays familiar;
  minimap wheel zoom is optional and deferred unless useful in review. Navigation
  works with layout locked. Capture/cancel must not leak clicks into covered nodes
  or change audio controls. Provide keyboard focus/arrow panning and an accessible
  name; retain existing Find/Frame shortcuts for precise navigation.
- **Local feasibility:** `GraphWorkspace` already owns node geometry, viewport
  size, pan/zoom and `ToWorld`. With its transform, visible world bounds are
  `(-panX / zoom, -panY / zoom, viewportWidth / zoom, viewportHeight / zoom)`.
  Reuse that authoritative transform and existing `SetCanvasView` behaviour:
  navigation changes the session view without engine configuration or an undo
  entry; the next substantive edit includes the view in its save. Only the new
  Show minimap preference must persist immediately through UI preferences.
  Do not silently introduce always-saved pan/zoom as part of this feature. Keep
  minimap geometry separate from full WPF node controls and from audio telemetry.
  Draw on graph/layout/view changes; stop unnecessary work while hidden.
- **Bounds policy proposal:** Fit node/group bounds with padding, incorporating
  the visible region when panning outside the graph so the indicator cannot
  disappear. Avoid continuously rescaling the map under an active drag; freeze
  its mapping for the gesture, then reconcile. Handle empty graphs, coincident
  nodes, negative coordinates, extreme spread, resized windows and DPI changes.
- **Scope/tradeoffs:** Medium UI work, shared with ISSUE-005's transform and
  zoom changes. The overlay uses canvas space; collapsing and size adaptation
  should keep it usable in smaller windows. It does not increase graph limits,
  move nodes, modify routes, add undo steps or change processing.
- **Next step:** Review normal and simplified canvas states plus minimap placement;
  settle interaction/bounds policy, then qualify viewport accuracy, round-trip
  pan/zoom, lock mode, input capture, high contrast and keyboard operation. Check
  32-node/96-connection examples for rendering cost before claiming performance.
  Implementation, live integration and publication are separate later work.

<a id="idea-054"></a>
## IDEA-054: Integration coverage, incremental refactoring and stable status reasons

- **Current status, 2026-11-04:** Implemented, offline verified and locally integrated for
  unreleased 0.5.0. CI now runs the production native/C# serialization and
  acknowledgement/history regression. It demonstrated the precision defect
  before the correction; expanded coverage passes 712 checks afterward.
- **Implemented boundaries:** Shared camera, detail, minimap and pipeline policies
  and focused workspace/node-view components support the new behavior without
  a framework rewrite. `path.reason` separates stable stream reasons from human
  diagnostics; typed configure conflicts use existing `errorCode` with
  `revision_conflict`. Unknown reasons retain general handling, exact legacy
  adapters are bounded, and recovery clears stale reasons. Stored graph gains
  remain double until explicit DSP conversion; [ISSUE-024](KNOWN_ISSUES.md#issue-024)
  owns that defect and its evidence.
- **Evidence and next step:** The combined offline run passed 18 native suites
  and 14,583 detached UI checks with zero binding errors; guarded paired and
  interface-only activation also passed. See the
  [execution report](IMPLEMENTATION_0.5.0.md). Preserve these regressions for
  future changes. Native interaction, new-route listening and release
  qualification remain separate; no broader refactor is implied.

### Planning and source-investigation history — 2026-11-04

- **Requested/date:** 2026-11-04. **Status at planning:** Planned for 0.5.0; documentation
  only. The maintainer selected the investigated engineering improvements for
  the release plan. No implementation, new dependencies or live changes are
  authorized by recording them.
- **Need/rationale:** Preserve reliable editing across the desktop/native
  boundary, reduce the chance of unrelated interface regressions and allow
  diagnostic wording to change without changing error classification.
- **Evidence:** Source review on 2026-11-04 found that
  `WorkspaceHistoryVerification.Snapshot` copies C# configuration rather than
  passing through native serialization. `GraphWorkspace` combines navigation,
  selection, connections, clipboard, feedback and rendering; `GraphNodeView`
  combines device selection, meters, availability and effect construction.
  `InputAvailability`, `OutputAvailability` and a microphone-privacy branch in
  `GraphNodeView` classify some statuses by exact English messages. These are
  verified source observations. The precision defect is tracked separately in
  [ISSUE-024](KNOWN_ISSUES.md#issue-024); no live failure from status wording or
  class size was reproduced. External technical research is not required for
  these repository-specific proposals; feasibility is based on source review,
  not a completed implementation or performance measurement.

### Real serialization and history integration tests

- Keep fast detached tests. Add an inert native test executable using production
  C++ deserialization/serialization, fed by the production C# model serializer.
  Deserialize its output in C# and pass the returned configuration through the
  real acknowledgement/history path. Do not reimplement native conversion in
  the test or substitute copied C# objects for the returned values.
- Cover node and cable gains, non-binary-exact decimals, limits, old settings,
  repeated save/reload, an edit arriving before an earlier acknowledgement,
  undo while saving, lost acknowledgements and reconnects. Audit other numeric
  model fields and add cases where their contracts differ. Genuine independent
  name/device/gain changes must still trigger the appropriate conflict handling.
- Require the regression to fail on the current precision defect and pass on
  its correction. Add the new checks to CI alongside existing checks. An inert
  serializer test does not qualify actual engine IPC, audio or live pointer use;
  retain separate isolated engine integration and release qualification.
- **Dependency/safety:** The existing engine probe uses a per-user connection
  and refuses to run alongside the personal engine. Preserve that refusal and
  app/engine identity checks; use a separate test environment for full engine
  communication. No personal engine restart is implied by this plan.

### Incremental workspace and node-view refactoring

- **Audit boundary, 2026-11-04:** Use the
  [release delivery order](#release-0-5-0-plan). Inventory responsibilities and
  pick a short list of extractions needed for this release before starting.
  Deliver them with characterization/regression evidence and stop; file size
  alone is not an acceptance metric. Unrelated rewrites and exhaustive cleanup
  remain outside scope. Feature work need not wait for every possible extraction.

- Fix and qualify ISSUE-024 first. Keep defect corrections and structural
  extractions in separate reviewable changes, preserving behaviour and public
  contracts. Start with connection descriptions/geometry and individual effect
  control builders; then separate selection/gesture handling with explicit
  state ownership. Splitting more methods into partial files alone does not
  satisfy the aim of reducing coupling.
- Check drag/cancel, layout lock, selection, undo grouping and pending edits
  around gesture extractions. Verify event unsubscription and disposal so view
  rebuilds do not duplicate handlers. Run relevant existing tests and compare
  rendered layouts where affected; qualify real pointer/keyboard flows before
  claiming interaction equivalence. Preserve concurrent branding and interface
  work, saved settings and normal WPF behaviour.
- **Scope/tradeoff:** Bounded extractions, not a wholesale architecture rewrite
  or new UI framework. Small steps increase review work but make regressions
  easier to identify and revert. Exact component boundaries remain an
  implementation design task, coordinated with the planned canvas features.

### Stable engine status reason codes

- **Audit clarification, 2026-11-04:** Separate path/status reasons from command
  failure codes. `ProtocolResponse.ErrorCode` already exists and distinguishes
  `settings_write_failed`, while `MixerViewModel` still uses message text for
  revision/stale responses. Reuse and extend that command field where supported;
  do not add a competing code field. Stream reasons belong with path status and
  its state/message. Inventory the affected comparisons and define both contracts
  before changing producers and consumers, preserving matching-release checks.

- Extend status with a stable machine-readable reason alongside overall state
  and human-readable message. Proposed example: state `Error`, reason
  `accessDenied`, message explaining the denial. Assign reasons at the native
  failure source and map interface warnings from the reason rather than text.
- Cover access denial, audio-service unavailability, disabled devices, ambiguous
  app instances and microphone-privacy presentation. Audit all behavioural
  message comparisons; do not mechanically replace harmless display comparisons.
  Each status transition replaces or clears stale reasons. Messages remain
  available for diagnostics and can be reworded independently.
- Missing/unknown reasons must produce a useful generic warning. Specify schema
  and supported-version behaviour before implementation. Any legacy text fallback
  must be narrowly scoped to an actually supported case; do not weaken matching
  app/engine release checks to allow mixed versions.
- Test native-to-desktop serialization, each reason mapping, recovery transitions,
  absent/unknown reasons and changed messages with unchanged reasons. Changing
  wording alone must leave classification unchanged; changing the reason must
  select the appropriate warning. Keep recovery/retry ownership in the engine.
- **Scope/tradeoff:** Coordinated native status, serialization, C# models and
  presentation change; adds protocol maintenance but removes wording coupling.
  Exact reason names and compatibility policy remain to be specified.

- **Practical next step:** Create the failing production-serialization/history
  regression for ISSUE-024, then implement and qualify the precision correction.
  Follow with separately reviewed extractions and the reason-code contract.
  Record actual local and release verification separately from this plan.

## Dependencies within the priority order

For the wider backlog, the priority index above supersedes the former unranked
investigation order. For accepted 0.5.0 work, the
[release plan](#release-0-5-0-plan) controls sequence and scope.
Lock and quit affordances are implemented in the local candidate. Presets and organizational
groups should agree on selection/identity handling; neither requires scenes or
A/B. Review the tutorial and silence-diagnostic flows with the maintainer before
large implementation work. Pinning may start with existing controls; task pins
depend on IDEA-004. Manual tasks and timed actions should precede external triggers.
Later DSP and integration entries require their own research and qualification.

<a id="idea-055"></a>
## IDEA-055: Echo effect

- **Current refinement, 2026-11-05 — implemented, offline verified and locally integrated:** The maintainer
  reports confusing Time-change replay and asks for a researched refinement,
  an actual repeat counter instead of Feedback percent, clearer Mix behavior,
  and an echo-only output for effects such as Reverb. The defect is tracked
  separately as [ISSUE-026](KNOWN_ISSUES.md#issue-026).
- **Selected refinement contract:** Time 20–2000 ms (default 300); integer
  Repeats 1–8 (default 3), counting copies after the original; Mix 0–100%
  (default 25). Original-fed repeats decay by half each time, with total wet
  weights normalized to avoid coherent level buildup. Time/Repeats changes
  briefly fade the previous effect before starting fresh; Mix does not reset it.
  Audio remains the chosen original/echo blend. A second **Echo** output carries
  only repeats, independently of Mix, and can feed Reverb or another effect.
- **Rationale/tradeoffs:** Finite copies make the displayed count truthful and
  prevent repeats becoming another generation of input. Eight repeats cap the
  tail at 16 seconds with the current maximum Time. Fresh timing changes trade
  uninterrupted old tails for avoiding replay/pitch artifacts. Very short Time
  remains available for thickening; long settings create distinguishable echoes.
  The three-control design uses fixed decay rather than adding another dial.
- **Compatibility:** Read legacy `feedback` 0–0.9 as an approximate count of
  repeats at or above −40 dB relative to the first, clamped to 1–8. Legacy 0 maps to 1,
  0.35 to 5, and 0.9 to 8. Explicit valid `repeats` takes priority, but malformed
  legacy values still fail validation. Time and Mix stay unchanged; new/absent
  settings default to 3 repeats. This deliberately changes the old infinite
  decay model, and is not exact preservation of its sound.
- **Research:** Primary delay manuals and DSP documentation reviewed
  2026-11-05; [evidence and original design choices](EFFECTS_RESEARCH.md#echo-refinement-2026-11-05).
- **Verification:** All 19 native suites pass, including 436 creative checks
  and actual synthetic-engine lifecycle/budget coverage; 15,175 detached UI
  checks/317 renders/zero binding errors, 198 focused UI/port checks, 781 native/
  desktop boundary checks and 229 isolated activation comparisons pass.
  [Evidence and timing limits](IMPLEMENTATION_0.5.0.md#echo-refinement-after-listening-feedback--2026-11-05).
- **Local integration:** Guarded paired replacement through the trusted shell
  and actual shortcut passed its eight-second observation. Settings/preferences,
  physical settings identity and running paths were preserved; only the
  documented in-memory Echo conversion was accepted. No rollback occurred.
- **Next step:** Maintainer listening follow-up and final release qualification.
  No independent live sound-quality pass is implied.

### Initial implementation history — superseded by the refinement above

- **Captured/authorized:** 2026-11-05. **Earlier status:** Implemented, offline verified and locally integrated for unreleased 0.5.0.
- **Need:** A dedicated repeating Echo effect, distinct from precise Alignment
  Delay and the diffuse Reverb tail. Promoted from the Echo candidate in
  [IDEA-035](IDEAS_LATER.md#idea-035); other creative effects remain deferred.
- **Selected behavior:** Simple Effects category; Time 20–2000 ms (default 300),
  Feedback 0–90% (35), Mix 0–100% (25), and normal Bypass. Standard stereo Audio
  input/output. No tempo sync, ping-pong mode or extra output is assumed.
- **Implementation choice:** Independent stereo feedback buffers, preallocated
  outside the audio callback, with smooth changes and crossfaded time edits.
  Feedback also reduces each fresh repeat's feed so accumulated echoes remain
  bounded; higher Feedback produces more, quieter repeats. Continue tails during
  bypass/mute, preserve history across ordinary edits and clear on explicit reset.
- **Scope/tradeoffs/dependencies:** Existing native effect lifecycle, graph
  validation/serialization and desktop controls. No new dependency. Synthetic
  impulse/stereo/feedback/headroom/state/continuity tests and persistence/UI checks
  precede live review. This is an original implementation contract, not an
  externally researched sound-quality or listening claim.
- **Verification:** Shared creative suite passes 359 synthetic checks; combined
  native/UI/boundary checks and guarded paired integration pass. Detailed scope
  and limits are in the [execution report](IMPLEMENTATION_0.5.0.md).
- **Next step:** Maintainer listening follow-up and final combined release
  audit/documentation. No independent hardware/listening qualification is claimed.

<a id="idea-056"></a>
## IDEA-056: Multiple output nodes for the same device

- **Captured/authorized:** 2026-11-06. **Status:** Implemented, offline verified
  and locally integrated for unreleased 0.5.0. The maintainer explicitly authorized all
  discussed features after the initial proposal, including shared outputs.
- **Need:** End independent pipelines near their own controls while sending
  them to the same physical or virtual destination, reducing long converging
  cables and matching the flexibility of repeated input nodes.
- **Implemented behavior:** Allow several output nodes to select
  the same destination. Each retains its own name, position, Level, mute and
  incoming connections. Sum their contributions once into one shared device
  render stream. Muting or removing one node affects only that contribution;
  do not automatically scale the mix according to how many output nodes exist.
- **Example:** A voice branch and a music branch each end in their own output
  node targeting the same example headphones. Both are heard together, with
  independent branch controls.
- **Local evidence, 2026-11-06:** `VirtualDeviceBindings.IsUsed` and the device
  selector intentionally block repeated output assignment. Native configuration
  validation also rejects repeated output bindings. `Engine::prepare` shares
  newly prepared source workers; sink dispatch currently takes the first node
  for a shared Port. Removing the restrictions alone would not correctly mix
  repeated sinks. `windows_audio.cpp` uses shared mode for ordinary listening
  outputs and exclusive mode for virtual outputs, making single-worker ownership
  especially important for the latter. Existing multiple cables into one output
  remain an alternative.
- **Research:** Microsoft's [WASAPI overview](https://learn.microsoft.com/windows/win32/coreaudio/wasapi)
  (reviewed 2026-11-06) documents shared access to an endpoint through the Windows
  audio engine. One VineMix render stream with internal summing is our proposed
  architecture, not a Windows requirement. No runtime prototype or live test
  had been performed when the proposal was first captured; current verification
  is recorded below.
- **Scope/dependencies/tradeoffs:** Coordinate desktop choices, virtual-device
  auto-assignment, native validation, physical endpoint resolution, shared worker
  lifetime, final summing/metering, reconnect and old/new graph crossfades.
  Repeated routing of identical audio adds level; delayed copies can alter the
  sound. Current sink processing clamps each output before dispatch; review that
  boundary when defining combined-output headroom and overload indication.
  Preserve independent node meters and existing muted defaults for new outputs.
  Distinct saved bindings that resolve to one physical endpoint need consistent
  ownership; preserve current exact-ID/stable-ID resolution without introducing
  automatic fallback to another device.
- **Verification:** Shared listening/exclusive virtual streams, aliases,
  reconnect/ambiguity, independent mute/gain, deletion/recreation, aggregate
  clipping/cancellation, one queue write, exact 480-sample fade and saved reopen
  pass in synthetic engine tests. The maximum 32-node/31-output run showed no
  mixer-thread C++ allocations/frees. Desktop duplicate/paste/history and 799
  real native/desktop boundary checks pass. Guarded local integration passed;
  [full evidence](IMPLEMENTATION_0.5.0.md#effects-cable-clarity-and-repeated-outputs--6-november-2026).
- **Next step:** Maintainer listening/workflow follow-up and final release
  qualification. Earlier rejection/implementation observations above describe
  the pre-refinement source; the current engine implements the shared behavior.

<!-- Entry guide: use only the detail warranted by the idea.
## IDEA-001: Short title
- Captured: YYYY-MM-DD
- Status: Captured
- Original idea: A short quote or faithful paraphrase.
- User need: What problem this would solve and for whom.
- Proposal: Concrete expected behavior; label assumptions.
- Project fit: Relevant current behavior, code, or related plans.
- Research: Findings, alternatives, dated source links, or "Pending".
- Scope and tradeoffs: Likely effort, dependencies, risks, and limitations.
- Open questions: Decisions or unknowns worth preserving.
- Next step: Smallest useful investigation or decision.
-->
