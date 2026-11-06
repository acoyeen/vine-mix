# VineMix 0.5.0 implementation and verification

**Published — 6 November 2026:** [0.5.0 Beta](releases/0.5.0-beta.md) is released
from commit `57052a200b0ec73d0d816f9ed12d2d5a2e13013d`, with matching source,
Setup, standalone, checksums and signed update metadata. Both exact-source and
published-asset CI passed. The [final qualification record](RELEASE_0.5.0_QUALIFICATION.md)
is the current outcome; earlier local-only checkpoints below remain historical.

**Final release pass authorized — 6 November 2026:** The maintainer requested a
complete audit, fixes, thorough checks, refreshed documentation and screenshots,
then explicitly authorized publication. This supersedes the earlier boundaries
below that retained the 0.4.0 identity. The release uses `0.5.0-beta`, Windows
version `0.5.0.0` and display `0.5.0 · Beta`. No computer-use tools; existing
restart permission and physical-settings, backup, identity and launch safeguards
continue to apply. Earlier implementation checkpoints remain dated evidence.
See the [final qualification record](RELEASE_0.5.0_QUALIFICATION.md).

Authorized 2026-11-04. Scope comes from the
[accepted release plan](IDEAS.md#release-0-5-0-plan); stable issue, idea and tweak
IDs retain their requirements and history. This is an execution plan, not a
release announcement. Keep the current release identity until a release is
separately authorized.

## Safety boundary

No computer-use tools. Initial qualification uses source-level tooling, synthetic
audio buffers, temporary configuration fixtures, private test pipes and detached
verification processes. These must not connect to production control pipes,
start the personal engine, open actual audio endpoints, or read/write personal
workspace settings. Keep private evidence and backups outside the checkout.

Before any live volume, routing, device or settings change, audible/invasive
test, engine restart or installation, describe the exact action and wait for
explicit maintainer approval and time to turn off sensitive audio equipment.
No automatic approval, timeout or previously allowed interface update overrides
this boundary. Source changes to volume controls and synthetic test levels are
permitted; applying them to the running setup requires the approval above.

**Live authorization, 2026-11-04:** after resuming the work, the maintainer
confirmed that connected sound equipment was at zero volume and explicitly
authorized engine restarts and invasive testing. This satisfies the live-test
approval boundary for this implementation pass. Preserve existing volume,
routing and workspace settings; use the guarded paired-package activation and
Windows-shell launch path. Review screenshots without computer-use tools.
Publication and a release-version transition remain outside this authorization.

## Implementation order

**Refinement authorized 2026-11-06 — implemented, verified and locally integrated:** Audit Distortion with primary
sources and full transition/performance tests, merge effects into one flat menu,
colour secondary effect outputs light blue, blend cables between socket colours,
add full-detail midpoint markers with intentional node occlusion, and support
multiple independent output nodes for one destination (TWEAK-024/026, IDEA-056).
Implementation order: establish shared-output ownership/summing and audit DSP;
update device selection/copy/history and cable presentation in parallel; run
targeted native and detached interface checks; review screenshots and adversarial
cases; run integrated regression/boundary/performance checks; stage a matching
source/package; perform guarded local activation and verify preserved settings.
No computer use, release publication or volume changes are authorized by this
refinement. Existing explicit restart authorization remains applicable.

**Extension authorized 2026-11-05:** The initial integrated baseline below is
extended with Simple/Advanced effects categories (TWEAK-024), Echo (IDEA-055),
Distortion (IDEA-002), and white node-type symbols replacing low-detail control
silhouettes (TWEAK-025). Complete these before the final release audit, checks
and documentation pass. Earlier suite counts and installed hashes do not qualify
these additions. No new release identity or publication is authorized here.

Extension order:
1. Agree effect settings/defaults and category mapping; retain compatible old nodes.
2. Implement native settings, validation, processing and lifecycle alongside
   desktop controls, catalog, persistence/history and help.
3. Replace passive silhouettes with original symbols covering all node types,
   retaining detail thresholds, coloured headers, editing guards and geometry.
4. Run synthetic signal/serialization checks and detached UI/keyboard/render
   checks; review screenshots and independently inspect DSP edge cases.
5. Build the combined candidate, then apply existing guarded local-integration
   safeguards. Final release audit, package/update qualification and publication
   remain separate from implementation evidence.

Stages are dependency order, not time estimates. Independent meter/control and
activation work may proceed alongside the first stage. Keep changes small enough
to review and preserve unrelated concurrent work.

| Stage | Work and reason for its position | Evidence before proceeding |
| --- | --- | --- |
| 1 | Add the real desktop/native serialization and acknowledgement/history regression, then preserve stored gain precision (ISSUE-024, IDEA-054). Future graph editing needs trustworthy undo and persistence. | Demonstrate the decimal failure before the fix, then pass node/cable decimal edits, acknowledgement, queued edits, conflict detection and legacy round trips. Register the inert boundary check in CI. |
| 2 | Harden activation process identity (ISSUE-011), add stable status reasons and use existing command error codes (IDEA-054). These remove fragile identity/message decisions before later integration. | Inert process-race tests retain fail-closed identity checks; native/desktop status serialization, changed wording, legacy/unknown reason and recovery tests pass. Real Explorer activation remains approval-gated. |
| 3 | Extract only shared canvas/navigation/port policies needed by these features (IDEA-054). Establish focus, selection, history and draft ownership before adding more keyboard behaviour. | Existing graph, gesture, history and detached UI checks pass after each bounded extraction; no framework or wholesale view rewrite. |
| 4 | Add meter gradients and numeric dial entry (TWEAK-021, TWEAK-023). These independent controls are small and can be qualified early. | Fixed-scale gradient and clipping/high-contrast checks; first-character, signed/localized draft, valid commit, invalid draft, Escape, Page Up/Down and shortcut coexistence checks. |
| 5 | Centralize zoom limits, add progressive detail, then the minimap and saved Show minimap preference (ISSUE-005, IDEA-053). The map and focus reveal depend on consistent canvas coordinates. | 10–200% UI range, native load/save compatibility, Fit/Frame, stable geometry/socket anchors, detail transitions, accurate viewport mapping and preference persistence. Exercise 32 nodes/96 edges with synthetic fixtures. |
| 6 | Add pipeline navigation, canvas arrows and F2; qualify accessible rename (ISSUE-003, TWEAK-022). These use the focus/reveal and gesture boundaries from stages 3/5. | Branches, reverse traversal, endpoints, selection, locked layout, off-screen reveal, held-key undo, draft protection, native control keys and rename/undo/redo labels. |
| 7 | Add Gate/Expander Trigger ports using shared port validation (IDEA-052). Keep this independently reviewable from Reverb. | Synthetic self/external detector, connected silence/mute/missing source, disconnect, attack/hold/release, stereo, cycle checks, saved old/new graphs and copy/paste/undo. |
| 8 | Add Reverb Wet output and preserve existing Audio output (IDEA-052). Extend alternate-output channel rules explicitly. | Synthetic Wet independence from Mix, decay/damping/pre-delay, stereo routing/metering, mute/bypass/tail transitions and existing graph compatibility. |
| 9 | Integrate and qualify the combined source candidate. Update focused help/shortcut/schema documentation and stable logs with actual evidence. | Build native/desktop; run relevant offline suites and boundary tests; review diff and scope. Report unperformed interactive, Narrator and hardware checks honestly. |
| 10 | Guarded local installation and live qualification, authorized on 2026-11-04. Release packaging/publication is separate. | Preserve settings/backup/physical identity and matching-binary safeguards. Never claim hardware or published verification from isolated tests. |

## Behaviour contracts for implementation

- **Zoom:** start with 10% minimum and retain 200% UI maximum. Share constants
  across desktop navigation/validation and update native validation. Older builds
  may reject new low zoom/ports; do not promise transparent downgrade. Keep
  existing legacy/native upper compatibility where required.
- **Detail:** use stable full-size world geometry and socket anchors. Hide costly
  control detail progressively with hysteresis; stop presentation timers at low
  detail, never processing or retained engine peaks. Hidden controls cannot take
  focus. Reveal a node before accessing controls and defer simplification during
  an active draft/gesture. Following manual review, simplified nodes retain the
  coloured header bar and show only dark dial circles and main selector/plot
  blocks; node/control/port text and numeric-entry/button blocks hide. Names remain
  available through hover/Find, and zooming in restores full controls. **Superseded
  presentation, 2026-11-05:** the dark control shapes are replaced with one distinct
  soft-white node-type symbol at a shared size; controls/meters still hide below
  the guarded threshold. Group headers hide
  at the same detail threshold, with fixed
  world-space bounds so they cannot expand into neighbouring nodes.
- **Minimap:** default visible, bottom right, click to centre and drag to pan;
  stable mapping during a drag, viewport included when outside node bounds.
  Save Show minimap in interface preferences independently of audio config. Do
  not add immediate persistent pan/zoom writes or optional map features.
  The manual-review refinement removes the visible title/header space and
  slightly darkens unselected silhouettes, retaining selection/high contrast.
- **Pipeline keys:** Ctrl+Tab forward and Ctrl+Shift+Tab backward along main
  audio routes, including muted ones; Tab/Shift+Tab retain ordinary focus order.
  Offer named choices at ambiguous branches/starting selections, remember the
  reverse route, and stay at endpoints with feedback. Valid numeric drafts may
  commit; incomplete/invalid drafts keep focus and can be cancelled with Escape.
- **Canvas arrows:** only canvas focus: selected nodes move 10 world DIPs (Shift
  1), otherwise the view pans 40 screen DIPs. One held-key gesture is one undo
  entry. Layout lock blocks node movement, not silently switches to pan. Controls
  retain their own arrow behaviour. Explicit Escape-to-cancel nudging was removed
  at the maintainer's request after manual review; Undo reverses the move.
  Ordinary text/pointer cancellation and internal invalidation guards remain.
- **Keyboard zoom:** Ctrl+plus/equal and Ctrl+minus, including numpad operators,
  use the existing centred zoom stops and bounds. The shared plus/equal key also
  works with Shift. Focused editors and controls retain their own shortcuts.
- **F2/rename:** exactly one logical node or group is eligible. None, multiple or
  non-renamable selections do nothing. Refresh accessible names after rename,
  undo and redo. Existing Find matching custom name/type/device is preserved.
- **Trigger:** absent cable uses the node's own input; connected silent, muted or
  unavailable sources mean silent detection. Removing the cable restores self
  detection. Detector audio never enters the main output.
- **Wet:** independent of Mix; Decay, Damping and Pre-delay still apply. Existing
  Audio remains the current dry/wet blend. Extra Wet remains stereo when its
  input is stereo. Bypass fades Wet to silence with no dry leakage; mute silences
  both. Internal tails continue advancing during bypass/mute, so resuming uses
  the current tail rather than a frozen one; reset clears the tail. Reverb Wet
  has explicit stereo channel metadata, including a mono-input reverb tail.
- **Reasons:** additive stable stream reason field; existing command ErrorCode
  for configure failures. Human text remains display-only. Unknown/missing
  reasons produce safe general status, with an explicitly bounded legacy adapter
  if older engines need compatibility. Recovery must clear obsolete reasons.

## Initial implementation progress and evidence

This table records the initial integration, before the manual-feedback pass
below. Its Escape-nudge behaviour was subsequently removed at the maintainer's
request; the current contracts above supersede that earlier behaviour.

| Stage | State | Evidence / outstanding work |
| --- | --- | --- |
| 1 | Implemented; offline verified | Production C++/C# boundary reproduced 156 failures before the fix; the expanded final suite passes 712 checks. Native stored-precision coverage passes 48,030 checks. CI runs the real boundary probe. |
| 2 | Implemented; offline verified | Retained process-handle identity, bounded unknown-identity retries and fail-closed positive mismatches pass 51 checks; existing UI activation fixtures pass 97. Stable stream reasons and typed revision conflicts pass native/desktop serialization and behavior checks. |
| 3 | Implemented; offline verified | Shared pure canvas/detail policies and bounded Camera, Reveal, Keyboard and Minimap partials replace duplicated decisions. Existing full UI suite passes alongside new features. |
| 4 | Implemented; offline verified | Fixed-scale meter gradients and direct numeric draft entry pass focused controls checks and the combined suite; detached screenshots reviewed. |
| 5 | Implemented; offline verified | 10–200% desktop zoom, progressive detail, stable geometry/anchors, protected drafts, accurate minimap navigation and persisted visibility tested. Wide graphs exercise the existing 32-node/96-edge limits. |
| 6 | Implemented; offline verified | Pipeline navigation, held-key nudge history, canvas pan, single-selection F2 and accessible rename pass 50 focused checks and the combined suite. Escape suppresses remaining held-key repeats until release. Native control navigation remains intact. |
| 7–8 | Implemented; offline verified | Optional Gate/Expander Trigger and independent stereo Reverb Wet pass DSP, persistence and 23 focused desktop-port checks, including real detached socket gestures, undo/redo and alternate-output detach protection. |
| 9 | Source qualification complete | 18 native suites; 14,583 detached UI checks and 305 rendered previews with zero binding errors; 712 production-boundary checks. Production native and Installed desktop builds succeed. Help, user guide and protocol documentation updated. |
| 10 | Installed locally; guarded live checks passed | Matching paired activation recovered all 10 previously running paths with advancing frames. Final keyboard correction used an interface-only update that preserved the same engine. Physical settings, backups, current configuration/preferences, actual shortcut launch and installed inventory verified. No release published. |

### Qualification evidence — 2026-11-04

- Native suites include 289,905 graph checks, 48,030 configuration-precision
  checks, 50,050 advanced-effects checks and 223 core-dynamics checks. Engine
  lifecycle exercises real command dispatch with synthetic workers and 301
  commits; it does not open personal audio devices.
- The boundary harness runs the production native JSON serializer and desktop
  acknowledgement/history code in an isolated process. Its 712 checks do not
  claim a live personal control-pipe session.
- The combined desktop verification uses detached WPF controls and rendered
  screenshots. Reviewed examples include gradients, Trigger/Wet sockets,
  minimap projection, and compact/overview nodes at 25%/10% zoom. It does not
  establish physical-keyboard, IME or Narrator usability.
- The renamed checkout was built with fresh external native caches. Raw logs,
  screenshot evidence, staged binaries and recovery data remain outside the
  source tree. The package receipt records the exact source snapshot and binary
  hashes. Published release identity remains `0.4.0-beta` / `0.4.0.0`; these are
  local development changes for the planned 0.5.0 release.

### Local integration and final review — 2026-11-04

The Installed-mode candidate was built through `scripts/build.ps1`, with a
fresh external package, build cache and adjacent build receipt. The paired
activation used `scripts/activate-local-package.ps1` through the production
trusted-shell launcher and the existing Windows shortcut. Its additional
package-activation fixture suite passed 80 checks before the live update.

The live update passed: the new engine became ready in 0.247 seconds, all 10
previously running paths returned, frames advanced, the running engine's
physical settings file was verified, and the saved configuration/settings and
preferences matched their pre-update state. Both ordinary and redirected data
views, original binaries and inventory were backed up outside the checkout.
The controller remained healthy through the eight-second observation period;
no rollback was needed. This readiness timing is not a measured end-to-end
audio interruption or listening test.

Final review found and fixed an Escape/autorepeat edge case: cancelling a held
arrow nudge could start another nudge on the next repeat. The fix latches the
cancelled key until release. Its 50 focused keyboard checks and the full
14,583-check detached suite passed. A second Installed candidate retained a
byte-identical engine; `scripts/activate-local-ui.ps1` applied the corrected UI
while preserving that engine's process, start time, path and hash. Its settings,
preferences, frames, running paths and eight-second observation checks passed.

The workspace changed between the two updates (node/connection counts, camera
position and revision). Each update preserved its own then-current baseline;
the newer workspace was retained and no older settings backup was restored.
No test adjusted personal volume controls or patched personal audio routing.

The UI-only helper updates payload hashes but retains prior source metadata.
For this final build, the existing guarded inventory helpers validated the
paired receipt and aligned only the installed inventory's source metadata,
with both running processes preserved. A separate final audit passes all 36
installed payload entries and confirms source/receipt/inventory agreement.
Live window screenshots were reviewed without sending mouse or keyboard input.

| Initial local identity, before manual refinements | Value |
| --- | --- |
| Release identity retained | `0.4.0-beta` / `0.4.0.0` |
| Source revision (working tree has changes) | `7342dedada136f3843c310c57cb6b1b029394339` |
| Source snapshot SHA-256 | `0a8c7263e400e8dc97a37e422df113538e29b4199340d4aef8011da24ed6bc64` |
| Desktop SHA-256 | `558AB0E15E0542F71E93EB0054F59C1AFD7B1AD4B0994F5DEC9DF1B8FCC9BA1F` |
| Engine SHA-256 | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

Verification entry points are `scripts/verify-offline.ps1` (all isolated checks,
with external `-ResultDirectory` and `-BuildDirectory`),
`VineMix.Verification.exe --render-design --verification-dir <private-directory>`,
and `--verify-configuration-boundary --native-probe <probe> --report <private-json>`.
Targeted keyboard verification uses `--verify-workspace-keyboard`; activation
fixtures use `test-controller-identity.ps1`, `test-local-ui-activation.ps1` and
`test-local-package-activation.ps1`. Raw evidence remains private and external.

### Initial qualification limits

The accepted implementation and automated checks are complete and locally
integrated. Actual keyboard/IME interaction, Narrator, varied monitor/DPI pointer
usability, and listening tests of newly connected Trigger/Wet routes remain
manual qualification; detached controls and screenshots do not establish those
results. The wider accessibility backlog remains open. The original activation
incident's exact failing identity field is still unknown despite successful
guarded live activations. A 0.5.0 version transition, installer/download release,
signed update metadata and publication require a separate release pass.

## Manual-feedback refinements — 2026-11-04

The maintainer reported that minimap navigation and visibility persistence,
control/pipeline keyboard navigation, node nudging and Undo, canvas pan, F2 and
Find, direct dial entry/Page keys, decimal-level history, Gate/Expander Trigger
behaviour, and Reverb Wet/Mix independence and other controls worked as expected.
These are maintainer-reported manual outcomes, distinct from automated tests or
independently observed keyboard/listening sessions. No Narrator or IME result
was supplied.

The earlier follow-up changes remove the idle meter cap while retaining the real clip
indicator; centre simplified node names and hide their headers; stop group
headers/bounds growing as zoom decreases; darken minimap nodes and remove its
visible heading; remove special Escape cancellation for nudging; and add
Ctrl+plus/minus zoom. Reverb has no Mute button or menu command: the checklist was
wrong. User-facing help now correctly explains Bypass (dry Audio, silent Wet);
the existing model/DSP muted-state semantics remain intact.

**Subsequent correction, 2026-11-04:** The maintainer clarified that simplified
nodes must keep their coloured header bar and hide only its small text; the
centred actual/custom name remains unchanged. Group headers are unchanged.
The maintainer also clarified that the meter must retain its complete border and
original peak indicator, including the idle cap; only the tiny red sliver was
unwanted. Inset the gradient inside the border while preserving actual clipping
and peak hold/history. Both narrow corrections are implemented and locally
integrated; their final verification is recorded below. The following earlier
counts and integration evidence describe the preceding candidate, which
hid the whole node header and removed the meter's top border and idle strip.

The final combined candidate passed **14,726 detached checks, 314 renders and
zero binding errors**. The focused keyboard run passed 60 checks. Simplified-node
and group screenshots were reviewed at 10% and 25%, with stable geometry and
protected rename drafts. No audio processing change is part of this pass.

In the preceding candidate, the lighter red top row was reproduced in a rendered meter: the grey
horizontal border showed through the translucent red gradient. Removing that
top edge (retaining side/bottom outlines and the active clipping cue) makes the
first rows continuous. Eighteen new pixel assertions cover silent, quiet and
full mono/stereo lanes and high contrast. The combined focused meter/control/
minimap run passed 503 checks. The unused idle clipping strip was also removed;
actual clipping still displays its retained warning.

[ISSUE-025](KNOWN_ISSUES.md#issue-025) removes the Settings menu's fixed 560-DIP
height cap. The regression failed against the old cap, then passed after the
fix. At adequate available height the complete menu measures 380 by 585 DIPs,
with every command through Quit visible and no scrollbar. Constrained 360-DIP
cases retain scrolling and reach the complete final command at both 96 and 192
DPI. The detached menu renderer was corrected to allow adequate height instead
of introducing its own unrelated 520-DIP capture cap.

Guarded interface-only activation through the trusted shell and actual shortcut
passed. The same engine process, start time, path and hash were retained;
frames advanced and prior running paths were preserved. Each update preserved
its current configuration/settings/preferences and verified physical settings,
backups and the eight-second post-launch observation. No volume or routing
adjustments, engine restarts or computer-use actions were performed in this
feedback pass. Live screenshots were reviewed. The final metadata-only receipt
alignment and independent inventory audit passed all 36 payload entries.

| Preceding refined local build, superseded by restoration below | Value |
| --- | --- |
| Source snapshot SHA-256 | `9ab0828330d544d661512d6e83cf60358042f8d904f1fdc8f93d30fc626704be` |
| Desktop SHA-256 | `678E706825B0843A145BA2B161E7200C7F2BF835EFB72D21BAD4E3B0D147D1B2` |
| Engine SHA-256, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |
| Release identity retained | `0.4.0-beta` / `0.4.0.0`; 0.5.0 remains unreleased |

The requested refinements and Settings correction are locally implemented and
verified. A fresh maintainer check of the revised visuals and shortcuts remains
useful; the reported passes above cover the earlier candidate. Narrator, broad
IME coverage, all display/work-area combinations and independent hardware
listening qualification are not claimed by the detached checks.

### Header colour and meter restoration — 2026-11-04

The corrected candidate retains the coloured node header at every zoom and
hides only the small header text when simplified. The actual/custom name stays
centred. Group-header behaviour is unchanged.

The full original meter border and original idle peak/clipping indicator are
restored. Both gradient layers are inset one DIP inside all four border edges;
the fill uses those same inner bounds. This removes the thin red tint caused
by painting the translucent gradient over the border, without removing any
border edge or peak indication. Peak history, clipping hold/fade, stereo and
high-contrast behaviour remain intact.

The existing zoom regression now checks persistent header colour and hidden
small text. Pixel assertions check every border edge, uninterrupted inner
gradient rows and the idle/active/held/faded indicator. The focused run passed
525 checks; the final combined run passed **14,748 checks, 314 renders and zero
binding errors**. Simplified-node and meter screenshots were reviewed.

Guarded interface-only activation passed through the trusted shell and actual
shortcut, retaining the same engine process/start time/path/hash, advancing
frames and running paths. Current settings/preferences, physical settings,
backups and the eight-second observation passed; no rollback, engine restart,
volume adjustment or computer-use action occurred. Final source/receipt/
inventory alignment passes all 36 installed payload entries. The corrections
are locally integrated for unreleased 0.5.0, retaining `0.4.0-beta` / `0.4.0.0`.

| Restoration build before shade refinement | SHA-256 |
| --- | --- |
| Source snapshot | `a5f90445dc0cc4493fd24ba57fd06826a5f834f0f2839f58a15d665b0000363a` |
| Desktop | `D1EF3087E430FC9AA64D88B84C50151758DE1A63937BC34E8B966B3BB4EB5385` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

### Matching overview shades — 2026-11-04

The minimap's unselected nodes are darker, using the shared solid `#626974`
colour. Centred names inside simplified canvas nodes use that exact shade.
Selected minimap nodes retain their existing highlight; high contrast uses the
system foreground for both surfaces. Coloured node headers remain visible.

The combined detached run passed **14,748 checks, 314 renders and zero binding
errors**. The existing minimap pixel check verifies the shared colour, and
minimap and simplified-node screenshots were reviewed. Guarded interface-only
activation passed with the same engine process/start time/path/hash and current
settings/preferences preserved through the eight-second observation. The final
source/receipt/inventory audit passed all 36 installed payload entries. No volume
adjustment, engine restart or computer-use action occurred. This refinement is
locally integrated for unreleased 0.5.0 under `0.4.0-beta` / `0.4.0.0`.

| Previous shade refinement, superseded below | SHA-256 |
| --- | --- |
| Source snapshot | `2760102aface0e2135d88fc51d2759408ed0bbbdc55ccf4263a114a4e2bbed13` |
| Desktop | `E10E36C02E9134527B5DCE5E7B133FDCD8C0BAB9D991984C20F6A67264637A47` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

### Further darkening of matching shades — 2026-11-04

The maintainer found `#626974` still too light. Both minimap node silhouettes
and centred simplified-node names now share `#4A5058`; selection highlighting,
system high contrast and coloured headers are unchanged. The detached run passed
**14,748 checks, 314 renders and zero binding errors**, with both views reviewed.
Guarded interface-only activation passed through the trusted shell and actual
shortcut, preserving the same engine process/start time/path/hash and unchanged
settings/preferences through eight seconds without rollback. Source/receipt/
inventory alignment verified all 36 installed payload entries. The update is
locally integrated, retaining `0.4.0-beta` / `0.4.0.0`; 0.5.0 remains unreleased.

| Darker-shade build before name fitting | SHA-256 |
| --- | --- |
| Source snapshot | `364acd928af9c463e0bd45a39e7f6d5e93f8647b76182285531d6113b9f765ea` |
| Desktop | `1F0209929F9359BA657281653CA8FC7BA91C87B3510304797173D282D309C03F` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

### Fit simplified names to their nodes — 2026-11-04

Centred simplified names now grow to fit their own padded node box, replacing
the fixed screen-size target and 40-DIP cap. Longer names wrap and shrink as
needed. The fitted size is independent of zoom and cached until the name,
available space, node dimensions or font metrics change. Padding clears the
retained header, meters and port rows; node geometry and socket anchors stay
unchanged. The shared darker `#4A5058` shade is retained.

The existing detached suite passed **14,748 checks, 314 renders and zero binding
errors**. Screenshots at 25% and 10%, including varied existing node names, were
reviewed. Long unbroken-name and Unicode wrapping were reviewed in source;
no additional automated coverage for those cases is claimed. Guarded
interface-only activation through the trusted shell and actual shortcut passed,
preserving the same engine process/start time/path/hash and settings/preferences
through eight seconds without rollback. Source/receipt/inventory alignment
verified all 36 installed payload entries. No volume adjustment, engine restart
or computer-use action occurred. This is locally integrated for unreleased
0.5.0 under `0.4.0-beta` / `0.4.0.0`.

| Fitted-name build, superseded by control silhouettes below | SHA-256 |
| --- | --- |
| Source snapshot | `067c33b084a65f4fad61c25b07d84212e93a453405a182ae2d5fe1282bd79fea` |
| Desktop | `52B1DD4573D7EF7360EA45F2C445B15008413B99C3EC138636E2841267E34E31` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

### Simplified control silhouettes — 2026-11-04

The maintainer rejected the fitted-name presentation and supplied a reference
for low-detail node interiors. Compact and overview nodes now replace their
names and control text with passive shapes from the actual arranged controls:
circles for dial faces, blocks for fields/selectors/buttons and plain areas for
EQ and Voice Color plots. Tiny port labels also hide. Coloured headers, sockets,
connections and the existing compact/overview meter policy are preserved.
Names remain available through hover information, accessibility and Find;
zooming in restores the full controls. The fitted-name code was removed.

Shapes are cached, with layout and active-mode visibility changes refreshing
their geometry. They add no focus targets, input handlers or node sizing.
The shared `#4A5058` shade and system high-contrast foreground are retained.
Existing zoom assertions were updated for this changed presentation, retaining
node/socket geometry, focus and draft protection checks. The detached suite
passed **14,752 checks, 314 renders and zero binding errors**. Reviewed 25% and
10% screenshots, including varied controls, show circles and blocks without
node-name text. Mode-branch/cache handling received source review; no new live
mode-switch or listening qualification is claimed.

Guarded interface-only activation through the trusted shell and actual shortcut
passed, retaining the same engine process/start time/path/hash and unchanged
settings/preferences through eight seconds without rollback. The final source,
receipt and installed inventory audit passed all 36 payload entries. No volume
adjustment, engine restart or computer-use action occurred. This is locally
integrated for unreleased 0.5.0 under `0.4.0-beta` / `0.4.0.0`.

| Earlier control-silhouette build, superseded below | SHA-256 |
| --- | --- |
| Source snapshot | `d88cc29b61cf4964f595e13c654a5e92f1fc70519927b076741b26c7e913841c` |
| Desktop | `8998F7AE0DCC5B7B6F647A9B37D52933640AAE20AA7D5D8BC0997D86C0F8D872` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

### Minimal darker silhouettes — 2026-11-04

The next locally integrated refinement retains only dial circles and main
selector/plot blocks, removing numeric-entry bars and all button/Bypass blocks.
Its dedicated `NodeSimplifiedBrush` is `#0B0D11` against the normal `#15181E`
node background; system high contrast is retained. The minimap stays `#4A5058`.
The existing detached suite passed **14,752 checks, 314 renders and zero binding
errors**. Reviewed 25%/10% and varied-control screenshots show the reduced shapes;
read-only pixel samples confirmed `#FF0B0D11` against `#FF15181E`, with every
shape RGB channel darker. No additional test or harness was added.

Guarded interface-only activation passed, preserving the same engine process,
start time, path and hash plus settings/preferences through eight seconds without
rollback. Source/receipt/inventory alignment passed all 36 payload entries.
This remains locally integrated, unreleased 0.5.0 work under `0.4.0-beta` / `0.4.0.0`.

| Minimal dark silhouette build | SHA-256 |
| --- | --- |
| Source snapshot | `5d7bdfb582c38104ac15876329c4c231be1509fded9b21f33e10f5735cd9b684` |
| Desktop | `45A63102B834772A23172B5FFB25B5CA0DEB4F05A79156157452BF515983BE7F` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

### Fill simplified interiors with uniform dials — 2026-11-04

**Superseded interpretation:** The maintainer clarified that filling each node
was too literal and produced inconsistent sizes across nodes. The following
build remains historical; the shared-size correction below replaces it.

The minimal dark shapes now fill the padded node interior. Selector and plot
blocks use available space, and dial circles use an evenly spaced grid with a
common diameter within each node. The final incomplete row is centred. This
layout is independent of full-detail dial sizes and reuses space previously
reserved for labels, values and buttons. Header/meter padding, real control
geometry, node/socket positions and the darker `#0B0D11` shade are preserved.

The existing detached suite passed **14,752 checks, 314 renders and zero binding
errors**. The existing simplified-shape assertion includes equal-circle sizing;
its mix fixture contains a single dial, so no new automated multi-dial coverage
is claimed. Reviewed 25%/10% and varied-control screenshots show larger circles
filling the interior; the five-dial example has equal circles in a 2/2/1 grid.
Pixel inspection again confirms the darker shape/background colours.

Guarded interface-only activation passed through the trusted shell and actual
shortcut, preserving the same engine process/start time/path/hash and unchanged
settings/preferences through eight seconds without rollback. Source/receipt/
inventory alignment verified all 36 payload entries. No volume adjustment,
engine restart or computer-use action occurred. The refinement is locally
integrated for unreleased 0.5.0 under `0.4.0-beta` / `0.4.0.0`.

| Superseded filled-interior build | SHA-256 |
| --- | --- |
| Source snapshot | `85bc0cc58bfc4c41d3a58faa188f211f6ce03669eae97c9ef22ab4f1bfb50c11` |
| Desktop | `239413B54DB4DDF38DFD90899BA851107AD171ED24A6162D2704027B43206CBE` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

### One simplified dial size across all nodes — 2026-11-04

Every simplified dial now uses the same 72-world-DIP diameter, independent of
node dimensions, dial count or full-detail size. Fixed 24-DIP spacing produces
compact centred groups with room around them, rather than enlarging the shapes
to occupy each node. Selector bars use a shared 32-DIP height and plot blocks
have a bounded height. The dark `#0B0D11` shade, coloured headers, meters,
node/socket geometry and full-detail controls are retained.

The existing 32-node/96-edge zoom fixture now repeats Gain, Mix, Compressor,
Expander and Low / Mid / High, covering different node widths and dial counts.
Its existing shape assertion checks every circle against the shared diameter
across all nodes. The detached suite passed **14,752 checks, 314 renders and
zero binding errors**. Reviewed 25%/10% and varied-control screenshots confirm
matching single/multi-dial sizes and compact spacing. Pixel samples retain the
darker shape/background colours.

Guarded interface-only activation through the trusted shell and actual shortcut
passed, retaining the same engine process/start time/path/hash and unchanged
settings/preferences through eight seconds without rollback. The final source,
receipt and installed inventory audit passed all 36 payload entries. No volume
adjustment, engine restart or computer-use action occurred. This correction is
locally integrated for unreleased 0.5.0 under `0.4.0-beta` / `0.4.0.0`.

| Shared-size build before meter hiding and centring | SHA-256 |
| --- | --- |
| Source snapshot | `ef360c88cc5eaec5256e44d134c1508ab339e7c1bbb650b11871b02baf406de8` |
| Desktop | `1EE59C6DE522C1686526334DF953957B00FAD9A9ED36D83ED60880AC98B4E20E` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

### Hide simplified meters and centre the controls — 2026-11-04

Audio meters now hide in both Compact and Overview, at the same guarded
transition as the other full-detail controls. The earlier Compact-meter policy
is superseded. Equal 16-DIP side padding replaces the reserved meter column,
centring simplified shapes across the node body. Shared dial size, dark shade,
header colour, hysteresis and active-edit protection remain unchanged. Full
detail restores the original meters and retains peak/clipping state; hiding
uses `Hidden`, preserving node and connection geometry.

The existing mixed-node checks now verify meter visibility at every detail
level and the border-adjusted horizontal centre of the combined shape bounds.
The suite passed **14,752 checks, 314 renders and zero binding errors**. Reviewed
25%, 10%, varied-control and 100% screenshots confirm hidden simplified meters,
centred shapes and restored full-detail meters.

Guarded interface-only activation passed through the trusted shell and actual
shortcut, preserving the same engine process/start time/path/hash and unchanged
settings/preferences through eight seconds without rollback. The source,
receipt and installed inventory audit passed all 36 payload entries. No volume
adjustment, engine restart or computer-use action occurred. This is locally
integrated for unreleased 0.5.0 under `0.4.0-beta` / `0.4.0.0`.

| Current centred low-detail build | SHA-256 |
| --- | --- |
| Source snapshot | `77ba25225c97b5fdce978d7cdf4cd7e9174255bd9a3969a00bc083912251a310` |
| Desktop | `93A117BAAB638D6D63799E5553A750240967449F7E4A6FE9C3AE95EA56FBA9DC` |
| Engine, unchanged | `054B9BDAC65E9DAA8539A2E9FD2B8CD1457A715F33CC1557B05AB38E8E9B1CB8` |

Record commands, sanitized outcomes and limitations here as stages complete.
Keep source implementation, isolated verification, live verification and released
status distinct. Do not mark the goal complete while accepted implementation or
required safe tests remain unfinished.

### Creative effects, categories and node symbols — 2026-11-05

Source implementation, offline checks and guarded local integration are complete. This section supersedes earlier
low-detail silhouette requirements without changing the established zoom limits,
geometry, edit guards, headers or minimap.

- Simple Effects and Advanced Effects share the existing effect colour and one
  creation/search/help catalog. No existing graph kind is converted or renamed.
- Echo has Time/Feedback/Mix and independent stereo history. Normalized feedback
  bounds accumulation, time changes crossfade fixed taps over 480 frames and
  rapid changes retain the latest requested time. Bypass/mute keep history warm;
  reset clears it without allocating on the processing thread.
- Distortion has Drive/Tone/Mix/Output and one continuous soft-saturation curve.
  Original 8× oversampling uses symmetric 257-tap Blackman FIR stages, a wet
  20-Hz DC-removal filter and smoothed Tone filter. The 32-frame delay is aligned
  across dry, wet, zero Mix and Bypass. Output trims the blend; Bypass is untrimmed.
  No new dependency, plugin host, output port or mode menu was added.
- Thirty-two creatable types and two legacy types have original soft-white line
  symbols. Frozen geometry and cached layout replace control-shape collection.
  Every real node kind passed the separate 112×112-symbol, geometry/socket,
  Compact/Overview/full-detail and dynamic high-contrast probe.

The complete detached interface run passed **15,164 checks, 317 renders and zero
binding errors**, including 79 focused creative-effect checks. The production
C#/native boundary passed **732 checks**, including exact defaults, decimal
creative-setting acknowledgements, history and reload. Normal themed Echo and
Distortion screenshots, the 34-symbol sheet and 25% canvas were reviewed.

The paired-activation comparison now supplies only wholly absent Echo/Distortion
records with the exact documented defaults in an in-memory copy. It never writes
settings or erases explicit null, partial, malformed, custom or unknown fields.
The old/default compatibility case failed before the correction; the expanded
inert helper suite passes **111 checks**, preserving rejection of changed values.

Independent DSP review found no blocking source defect and caught test-fixture
issues that were corrected: startup ramps must settle before exact impulse
measurements, and nonlinear quality must be measured before output-endpoint
clipping. The original 4× version failed the selected high-frequency/high-drive
alias target; 8× improved it while retaining the same latency. Earlier unoptimized
8× runs exceeded the timing gate; contiguous interpolation tables and mirrored
symmetric FIR history reduced cost without relaxing the acceptance limits. Earlier
failed measurements remain in private evidence.

The final native regression passed **all 19 suites**, including **359 focused
creative-effect checks**. Ten selected frequency/drive comparisons measured
14.95–104.17 dB less normalized folded energy than the unoversampled reference;
the hardest case was 18 kHz at 0.5 input amplitude and 36-dB Drive. This is a
selected-bin comparison, not a claim of alias-free processing. The default 1-kHz
reference level changed by −2.64 dB. Synthetic tests cover exact echo impulses,
bounded feedback, stereo isolation, silence/reset, state inheritance, smooth
edits, nonfinite input and no processing-thread allocation.

In the final 240-frame/5-ms quantum benchmark, four parallel Distortion nodes
measured 0.302-ms median and 0.842-ms p99 processing. Thirty parallel Distortion
nodes measured 2.283-ms median, 3.107-ms p95 and 3.786-ms p99, plus 0.052-ms p99
state adoption. Thirty Echo nodes measured 0.227-ms p99 processing plus 0.619-ms
p99 adoption. These are measurements on the test host under the recorded run's
load, not guarantees for every device or background workload. Production Installed
build passed and its source hash matches the build receipt; live integration is recorded below.

Primary references reviewed on 2026-11-05 informed the algorithm review:
[DAFx 2016 nonlinear antialiasing paper](https://www.dafx.de/paper-archive/2016/dafxpapers/20-DAFx-16_paper_41-PN.pdf),
[JUCE oversampling documentation](https://docs.juce.com/master/classjuce_1_1dsp_1_1Oversampling.html),
and [Faust antialiased nonlinearities documentation](https://faustlibraries.grame.fr/libs/aanl/).
No JUCE/Faust source or dependency was added. Fixed oversampling reduces rather
than eliminates aliasing; the wet path rolls off near Nyquist. The graph does not
automatically compensate parallel paths for Distortion's approximately 0.67-ms
delay. Synthetic checks do not establish listening quality, all-device timing
or final release qualification.

#### Guarded local integration — 2026-11-05

The matching production candidate passed paired activation under the trusted
Windows-shell parent and existing shortcut. The engine reported ready in 0.648
seconds; all previously running paths returned and frames advanced. This is not
a measured end-to-end audio interruption or listening result. Physical settings
identity, saved settings bytes, comparable configuration and workspace preferences
were unchanged through the eight-second observation. No rollback, settings
recovery, volume adjustment, routing edit or computer-use action occurred.

An independent final audit verified all 36 installed payloads and 29 staged
candidate payloads, current source/build receipt/installed inventory agreement,
and one running controller and engine at their canonical paths. Private raw
logs, synthetic screenshots and recovery backups remain outside the checkout.
The identity remains `0.4.0-beta` / `0.4.0.0`; these are locally integrated,
unreleased 0.5.0 features. Maintainer visual/listening follow-up and final release
qualification, version transition, packaging/update checks and publication remain.

| Creative-effects and white-symbol build | SHA-256 |
| --- | --- |
| Source snapshot | `73531b9803d2ba2ec2c1e4503ca670305daad20e5f36b41183a0dd47dd5490cf` |
| Desktop | `F5EE4DCDD271FB7F51E8E795AC50721925CFBD9BF83C2897C8BA8513CF2EF4D0` |
| Engine | `F66B7B4C0A6A3EB6F37BEEBA5A565DA4996304118DEA65179FD0A21FB12E3CEA` |

### Larger, bolder node symbols — 2026-11-05

The maintainer found the first symbols functional but unrefined and too thin.
The shared viewport is now 128 world DIPs instead of 112, with a 2.6-unit rounded
stroke instead of 1.8. Crowded details and intersections were simplified; small
gaps were opened, tone bars made solid and small circular paths cleaned up.
Every kind retains a distinct symbol, the same soft-white/high-contrast resource,
coloured header, stable node/socket geometry and full-detail restoration.

The existing symbol sheet verification now measures the actual rounded pen.
Targeted checks passed: **139 symbol/zoom checks**, plus geometry, Compact and
Overview interaction, full-detail restoration and dynamic-theme probes for all
**34 real node kinds**. The complete symbol sheet, actual-node 25% sheet and
25%/10%/100% wide-graph screenshots were reviewed. The isolated harness's toolbar
uses default styling; these checks concern the symbols and node presentation.
No audio processing changed, so native/audio suites were not repeated.

The production build passed with a byte-identical engine. Guarded UI-only
activation through the trusted shell and actual shortcut preserved the engine's
process/start/path/hash, physical settings identity, saved configuration/settings,
preferences and running paths/frames through eight seconds without rollback.
The guarded inventory update records that only the desktop binary was replaced;
the final independent audit verifies all 36 installed payloads and agreement
between current source, build receipt and installed inventory.
No engine restart, volume/routing change or computer-use action occurred.
This remains locally integrated, unreleased 0.5.0 work under the current
`0.4.0-beta` / `0.4.0.0` identity; maintainer visual follow-up remains.

| Refined-symbol build | SHA-256 |
| --- | --- |
| Source snapshot | `007b87da0ddfb007cd07ff25c6ddd38edf96d480c20540d9a254892ea45fb84d` |
| Desktop | `A02A6CC46EC26CB5A3F68E5BF0AF4EB95338D7122EF6AA9BAF48A6DE9E97FA8A` |
| Engine, unchanged | `F66B7B4C0A6A3EB6F37BEEBA5A565DA4996304118DEA65179FD0A21FB12E3CEA` |

### Dark node symbols — 2026-11-05

The maintainer requested the earlier dark control-silhouette shade instead of
bright symbols. A dedicated `NodeSymbolBrush` uses `#0B0D11` against the existing
`#15181E` node panel, with system foreground in high contrast. Refined shapes,
128-DIP size, 2.6-unit stroke, headers, minimap and full-detail text are unchanged.

The existing 72 symbol checks and all 34 real-kind palette/theme, geometry and
mode probes passed. Normal-to-high-contrast-to-normal changes on existing symbol
instances were verified, and the actual-node 25% screenshot was reviewed.
The production build passed with a byte-identical engine; native/audio and full
UI suites were not repeated for this colour-only change.

Guarded UI-only activation through the trusted shell and actual shortcut passed
its eight-second observation, preserving the engine process/start/path/hash,
physical settings, saved configuration/settings/preferences and running paths/
frames. No engine restart, volume/routing change, rollback or computer-use action
occurred. The work remains locally integrated and unreleased, retaining the
`0.4.0-beta` / `0.4.0.0` identity; maintainer visual follow-up remains.

| Dark-symbol build | SHA-256 |
| --- | --- |
| Source snapshot | `8cd685558539d8815e1805130d24833ec16c340517b3d2144c2253ec83615414` |
| Desktop | `E17B48BFD490026A49C3603CA5B176E6CE499221F341C6042556B76316B7FEEA` |
| Engine, unchanged | `F66B7B4C0A6A3EB6F37BEEBA5A565DA4996304118DEA65179FD0A21FB12E3CEA` |

### Quiet Blocks, selected concept 1 — 2026-11-05

The maintainer selected the first of six visual concepts and requested
implementation. Below the existing guarded detail threshold, nodes now retain
their plain solid charcoal bodies and category-coloured header strips, with no
interior type symbols, names, miniature controls or audio meters. The obsolete
symbol layer, geometry catalog, palette resource and symbol-only verifier were
removed. Existing header/port geometry, selection and availability indicators,
tooltips, interaction guards and full-detail restoration are preserved. This
supersedes the preceding bright/dark-symbol iterations; their evidence remains
historical.

The verification build passed without warnings or errors. **195 focused detached
checks passed**: 136 across all **34 real node kinds**, 58 existing zoom checks
and one no-native-window guard. Checks cover solid central body pixels at 25%
and 10%, stable node sizes and socket anchors, Compact/Overview interaction,
full-detail restoration, pending numeric drafts, control reveal and the bounded
32-node/96-cable graph. Earlier symbol-only assertions were removed with that
feature. Native/audio and the full unrelated UI suite were not repeated for
this presentation-only change.

The actual-node 25% sheet and 25%/10%/100% wide graph renders were reviewed.
Separate normal/high-contrast screenshots use the actual MainWindow and an
eight-node example matching the concept's pipeline arrangement. They retain
real 25% node geometry; the generated concept's enlarged illustrative footprints
are not a request to resize nodes. [Design QA](../design-qa.md) passed. The
private render-only host loads App resources without app startup and omits its
ComboBox wheel event hookup; this does not claim native interaction coverage.

The production build passed, all 29 staged payloads match its receipt, and the
engine remains byte-identical. Guarded UI-only activation through the trusted
shell and actual shortcut passed its eight-second observation, preserving the
same engine process/start/path/hash, physical settings identity, saved settings,
preferences, configuration and running paths/frame progress. No engine restart,
volume/routing change or rollback occurred. Metadata alignment and an independent
final audit passed for all 36 installed and 29 candidate payloads; current source,
build receipt and installed inventory agree. Private recovery backups remain
outside the checkout. No computer-use tools were used.
The work is locally integrated but unreleased, retaining `0.4.0-beta` / `0.4.0.0`.

| Quiet Blocks build | SHA-256 |
| --- | --- |
| Source snapshot | `a71e696d0be314b6cbb3e69c6e5ece53cf0325477edcf95083d6702f217f0d93` |
| Desktop | `75E52B692BB5A1F4455F8EB2F22BAF6360DDC993742D86B89E99C72D20BA5372` |
| Engine, unchanged | `F66B7B4C0A6A3EB6F37BEEBA5A565DA4996304118DEA65179FD0A21FB12E3CEA` |

### Echo refinement after listening feedback — 2026-11-05

**Status:** implemented, offline verified and locally integrated. The requested repeat-count
controls and echo-only output are tracked under [IDEA-055](IDEAS.md#idea-055),
with the retiming defect under [ISSUE-026](KNOWN_ISSUES.md#issue-026).
[Primary-source research](EFFECTS_RESEARCH.md#echo-refinement-2026-11-05)
distinguishes established delay behaviors from VineMix's chosen contract.

Baseline reproduction confirms the reported mechanism: with Feedback zero,
changing Time from 100 to 400 ms after an impulse's original repeat can emit
that impulse again at 400 ms. With Feedback 0.5, changing 100 to 500 ms replays
the earlier repeat sequence at 500, 600 and 700 ms. This establishes old-history
replay, not a measured hardware clipping or audible-latency diagnosis.

The revised processor reads finite, original-fed taps. Repeats is an integer
1–8, default 3; Time retains 20–2000 ms/default 300; Mix retains 0–100%/default
25. Successive copies halve in amplitude with normalized total weights.
Time/Repeats edits fade the old wet tail over 10 ms, clear it, apply the latest
request and fade fresh excitation into history over 10 ms.
Mix changes retain history. Audio is the selected blend; the **Echo** output
uses internal port `wet`, ignores Mix and preserves mono/stereo channels.
Bypass fades Echo to zero, and normal node/control mute silences both outputs.

Legacy feedback is validated and mapped to a bounded approximate count. Exact
Time/Mix values survive. Explicit integral numeric counts, including `3.0` and
`3e0`, are accepted; fractions are rejected rather than truncated. Canonical
serialization writes only `timeMs`, `repeats`, `mix`. The old sound cannot be
preserved exactly while replacing an indefinite feedback loop with finite
fixed-decay copies. Existing settings files are not proactively rewritten by
the compatibility reader.

Long history makes the existing copying approach unsuitable for real-time
adoption: an isolated 30-history copy benchmark measured 9.88 ms median,
12.57 ms p95 and 35.23 ms p99 against the 5 ms processing quantum. The explicit
engine adoption path transfers Echo history ownership instead. A fading
predecessor keeps only its next 480 wet frames; the minimum 960-frame delay
makes those independent of fresh input. Its live dry/Mix handling remains
separate. Existing const-copy behavior is retained for independent reference
continuations. Storage is allocated and touched off the audio thread, reset is
logical, and a pending-history budget prevents the old 128-runtime limit from
permitting tens of gigabytes of Echo buffers. Ownership/fade, allocation,
admission/rejection-before-save and timing tests qualify these changes.

Desktop checks completed so far: **15,175 detached checks, 317 renders and zero
binding errors**, followed by **198 focused creative/port checks** covering the
expanded 31-case raw JSON set, controls, clipboard, history and routing. Normal
Echo controls and Audio/Echo sockets were reviewed in screenshots. The isolated
activation comparator suite passes **229 checks**, retaining unknown/malformed
field and unrelated-configuration mismatch detection while normalizing only
the documented migration on detached copies. **781 native/desktop boundary
checks** also pass, including the same 31 raw JSON fixtures, wholly absent
records, canonical acknowledgements/undo history, exact legacy Time/Mix and the
Echo-to-Reverb alternate route. Native audio/lifecycle results and guarded paired
integration are recorded below.

The populated-history creative run passes **436 checks**. With 30 Echo nodes,
eight taps and a fully populated 16-second history per node, normal processing
p99 was 1.010 ms and ownership adoption p99 0.0764 ms. In the routing-change
case, new-graph processing p99 was 0.6715 ms and old-tail caching/adoption plus
one predecessor quantum p99 0.9003 ms (sum 1.5718 ms against the 5 ms quantum).
These are local synthetic measurements, not universal hardware guarantees or
one combined percentile. Two prepared maximum runtimes took 41.7–43.6 ms to
allocate and touch on the control thread. One 30-Echo runtime owns 175.78 MiB
of raw history; pending history is limited to 256 MiB separately from settled
active state. Other graph/heap state is additional. The memory guard is tested
before allocation, worker changes, revision/publication and persistence.
The deterministic lifecycle test also checks canvas saves while full, retry
after adoption, and retention through the predecessor's final fade quantum.
Final combined native regression passes **all 19 suites**, including the
436-check creative suite and the synthetic-engine lifecycle coverage. Independent
read-only reviews of the DSP/output contract and state ownership/budget found no
blocking defect. In that final run, the 30-Echo p99 values were 0.586 ms processing
plus 0.0359 ms adoption; routing-change processing was 0.5508 ms plus 0.8476 ms
for caching/adoption/predecessor processing (sum 1.3984 ms). Earlier measurements
above are retained; sums of separately measured percentiles are not a single
end-to-end percentile. Production packaging and guarded paired local integration
are recorded below. No independent live listening or hardware sound-quality pass is claimed.

The production package passes its build and all 29 staged payload hashes. Fresh
physical-settings provenance confirms complete explicit legacy Echo records,
so the documented conversion is unambiguous for this local activation. A missing
record would need its actual saved provenance distinguished from an old engine's
materialized default; the comparator does not accept either count arbitrarily.
The candidate remains unpublished `0.4.0-beta` / `0.4.0.0`.

| Refined Echo build | SHA-256 |
| --- | --- |
| Source snapshot | `3a494886f1856e3aeebf76081959da9f10c823dcf0fee2c95c2c1fb3de7f32fc` |
| Desktop | `693F1A9BB081D509942986F8A5E5687D1DDEC37A9F1562910AA4690F2B5A0167` |
| Engine | `C320E837639DCA47EC72A8D68761D99AB4D3E0CA30663E62B0391616F7815576` |

Guarded paired local integration passed through the trusted Windows shell and
actual shortcut. The replacement engine was ready in 0.672 seconds; all
previously running assigned paths recovered and frame progress continued
through eight seconds without rollback or recovery error. Physical settings
identity, settings bytes, preferences and routing were preserved, and the
configuration comparison accepted only the documented Echo migration. No volume
adjustment, settings restoration, extra live sound test or computer-use action
occurred. Private recovery backups remain outside the checkout.

The independent final audit verified all **36 installed** and **29 candidate**
payloads, and agreement among the current 345-file source inventory, build
receipt and installed inventory. This is locally integrated, unreleased 0.5.0
work under the unchanged `0.4.0-beta` / `0.4.0.0` identity. Maintainer listening
follow-up and final release qualification remain separate.

## Effects, cable clarity and repeated outputs — 6 November 2026

**Status:** implemented, offline verified and locally integrated; unreleased
0.5.0 work. This explicitly authorized refinement covers TWEAK-024/026,
IDEA-056 and the Distortion audit under IDEA-002/ISSUE-027.

### Implemented behavior

- One flat Effects category replaces the Simple/Advanced split; saved kinds and
  effect header colours are unchanged.
- Reverb Wet and Echo Echo sockets use light blue `#8EC8E8` and remain circular.
  Stereo Split channels remain ordinary audio. Cables blend between actual
  endpoint socket colours, retaining selection, mute and high-contrast styling.
- Small stationary midpoint circles follow the true cable curve. Their original
  six-screen-DIP size is superseded by the proportional sizing correction below.
  They share the existing full-detail threshold/hysteresis and remain visible
  for incident cables when an active edit keeps an endpoint detailed. They stay
  behind nodes by design and disappear with simplified nodes. A rendered review
  caught a positioned-Ellipse layout offset after zoom; world-coordinate circle
  geometry corrected it, and new rendered-position checks cover the regression.
- Repeated logical output nodes share one render worker per resolved endpoint.
  Contributions sum after independent Level/mute controls, without node-count
  normalization, and clip at the final device boundary. Routing fades blend the
  complete old/new sums. Independent meters remain; aggregate overload also
  marks outputs using that device. Exact/stable alias resolution and reconnect
  ownership are coordinated centrally, preserving saved bindings and revisions.
  Existing physical/virtual device-role restrictions remain.
- Duplicate output and copy/paste retain the destination and controls; newly
  copied outputs start muted. Duplicate output starts disconnected and supports
  undo/redo. Device selectors, auto-assignment and history permit repeated valid
  output destinations. Existing nodes and settings are not automatically changed.
- Distortion retains its algorithm, filter, oversampling, controls/defaults,
  10-ms smoothing and 32-frame latency. Independent FIR computations are grouped
  to reduce processing cost. Help clarifies Drive loudness and that Output trims
  even a zero-Mix blend, while Bypass restores untrimmed delayed original audio.

### Targeted verification

The final detached interface run passed **15,249 checks / 322 previews / zero
binding errors**, including **47 focused cable checks** and shared-output
selector, duplicate, clipboard, history and layout-lock checks. Normal, high
contrast, low-detail and occluded-marker images were reviewed. Source, endpoint
colour direction, negative/reversed/vertical geometry, selection/mute, draft
retention, telemetry caching and restored zoom positions are covered.

Native output verification passed **289,910 graph assertions** and the complete
synthetic engine lifecycle suite. Tests include listening/exclusive virtual
sharing, exact/stable aliases, return/convergence/ambiguity, independent controls,
burst retargeting, deletion/recreation, saved reopen and shutdown. An exact
480-sample aggregate deletion fade from 0.45 to 0.30 preserved every contribution.
The actual engine loop processed 401 quanta with the maximum 32 nodes (31 shared
outputs), measuring p50 **0.0531 ms**, p99 **0.1313 ms**, max **0.1652 ms**, and
zero mixer-thread C++ heap allocation/free operations, including adoption.

Distortion/Echo creative checks passed **461 assertions**. Complete ramps,
160-block rapid edit/publication/bypass/mute continuity, latency, finite extremes,
silence/reset, aliasing and headroom are covered. Independent before/after runs
over one million stereo frames produced **6,000,000 bit-identical output floats**
with zero maximum/RMS error after the FIR scheduling changes.

Synthetic timing now measures the whole process/adoption/old-graph interval,
using the engine's Pro Audio MMCSS class and 5-ms MixClock pacing, with verified
restoration afterwards. No affinity or stronger priority is added. Earlier raw
normal-priority/outlier runs are retained rather than discarded. The targeted
30-Distortion routing-fade p99 was **4.9125 ms** against 5 ms: this is tight margin,
not a broad performance guarantee. A strong full-wet/zero-trim transient reached
1.178; Distortion is not a brickwall limiter. Alias reduction remains unchanged,
not elimination. See [primary research](EFFECTS_RESEARCH.md#distortion-refinement-2026-11-06).

### Verification boundary and remaining work

No computer-use tools or intentional live audio/volume/routing edits were used.
One private screenshot helper instantiated the production Application merely to
load resources; dispatcher pumping entered its startup path. The existing-instance
guard prevented a new engine/settings model, but may have signalled the installed
window to show. Read-only checks found unchanged installed process identities,
start times and binary hashes. No before-run settings snapshot existed, so no
independent byte-preservation claim is made for that helper. It was replaced by
a plain inert Application; final full screenshots used the established early
`--render-design` verification branch. Raw incident evidence stays outside the
checkout. The final merged regression, guarded paired activation and physical
settings/payload audit are recorded below.

### Final merged qualification

All **19 native suites passed**, including 461 creative checks and the expanded
actual-engine lifecycle suite. The separately rebuilt Release-mode detached
desktop passed **15,249 checks / 322 renders / zero binding errors**. The real
native/desktop serialization and acknowledgement/history boundary passed **799
checks**, including repeated-output creation, controls, undo and redo.

In this final merged run, measured combined 30-Distortion routing-fade time was
median **3.2764 ms**, p99 **4.1845 ms**; parameter handoff p99 **2.3063 ms**.
Thirty Echo nodes during routing fade measured **1.1953 ms** combined p99.
The earlier 4.9125-ms Distortion result remains relevant to available margin;
these are measured synthetic results on this host, not universal deadlines.

Production staging succeeded from the same **347-file source inventory** used
for qualification. All **29 candidate payload hashes** matched the build receipt,
and the source remained unchanged through compilation. Version remains
`0.4.0-beta` / `0.4.0.0`; no publication or repository-visibility change occurred.

| Routing-refinement candidate | SHA-256 |
| --- | --- |
| Source snapshot | `3f3592d6eadf156a3a787744c569be234a9ed1b129c7508e5805e57ce99bc404` |
| Desktop | `1C2A58912C2CB75D854A2C9CC41B2F1899378742BB1704058C415F67C038ADBC` |
| Engine | `B42EB7305DFF6DF7C727B36E5DC49161BD2DFC8AD3C65E7B23A08B673D4B6986` |

### Local integration and final audit

Guarded paired activation through the trusted Windows shell and actual shortcut
passed. The engine was ready in **0.273 seconds** and all previously running
paths recovered. Eight seconds of observation passed with advancing audio frames,
preserved configuration, no rollback and no settings restoration. Saved settings
and preferences remained byte-identical through activation and the final audit;
their physical identity was verified through an open handle. Volume and routing
settings were not adjusted.

The final audit verified every **36 installed payloads** and **29 candidate
payloads**, plus agreement among the current 347-file source identity, qualified
source, build receipt and installed inventory. The hashes above identify the
installed build. Independent code review found no blocking issue in endpoint
ownership, aggregate clipping/fades, copy/history or the FIR optimization.
Documentation and the manual checklist were updated; maintainer sound/interaction
follow-up and final release qualification remain separate. Nothing was published.

### Maintainer manual checklist

- [ ] Open Add: one Effects menu contains both simple and detailed effects,
  including Echo and Distortion; search still finds them.
- [ ] Check Reverb Wet and Echo Echo sockets are light blue and circular.
  Ordinary audio and Stereo Split channels remain green.
- [ ] Connect green Audio to purple Trigger and a blue effect return to green
  Audio. Each cable blends between the two socket colours; green-to-green stays
  solid. Select/mute a cable and confirm its state remains clear.
- [ ] At full detail, check the small circle sits midway along each cable.
  Zoom out until details disappear, then back in: circles hide/return with them
  and stay on the curves. A circle covered by a node stays hidden deliberately.
- [ ] Add or duplicate two outputs for the same listening device; repeat with
  a virtual output if used. New/copied outputs start muted. When ready to listen,
  route distinct sources into them and confirm both are heard after unmuting.
- [ ] Change Level or mute one output, then remove it. The other output keeps
  its controls and audio. Undo/redo restores the changed node and binding.
- [ ] Copy/paste an output or connected group: device choices remain, copied
  outputs start muted. Reopen the app and confirm repeated assignments persist.
- [ ] Adjust Distortion Drive, Tone, Mix and Output, including while speaking.
  Listen for unexpected clicks or stalls. Mix zero retains the original sound
  with Output trim; Bypass restores untrimmed original sound and can be louder.
- [ ] Check existing Echo, Reverb, Trigger and listening routes still behave as
  before. Report subjective sound issues separately from automated test results.

## Midpoint sizing correction — 6 November 2026

The maintainer clarified that markers should retain their size relative to nodes.
Use a fixed three-canvas-DIP radius, removing inverse-zoom compensation. Dots now
scale with the canvas, while colours, midpoint placement, occlusion and guarded
detail visibility remain unchanged. This supersedes the initial fixed on-screen
diameter in TWEAK-026.

The safe detached focused suite passed **60 checks**, including actual transformed
diameters of 1.8 / 3.3 / 6 / 12 screen DIPs at 30 / 55 / 100 / 200 percent zoom.
Position, previews, high contrast, draft retention and hidden/full transitions
remain correct. The focused preview was visually checked; it uses inert helper
chrome and is evidence of cable geometry rather than a full application-style
review. Build completed without warnings/errors. Production staging matched the
qualified 347-file source and all 29 candidate payloads; the engine binary hash
remained exactly unchanged.

Two guarded activation attempts were withheld before stopping any process
because genuine settings saves occurred during their guard intervals. The newer
workspace was preserved. Following a ten-second stable observation, a fresh
interface-only activation passed its eight-second health check with the same
engine process, start time and binary hash, unchanged configuration/settings/
preferences, and no rollback or settings restoration. Installed source metadata
is reconciled to the new receipt after payload validation, with a private backup.
The final audit verified all 36 installed files and 29 candidate files, with
matching qualified source, build receipt and installed inventory. Only source
hash and local-activation provenance fields were reconciled; the replacement
record names the desktop alone. Release identity remains unchanged and unpublished.

| Marker-size build | SHA-256 |
| --- | --- |
| Source snapshot | `8d8f22f30524f2c8a8ae65bde43058ea894e0ad813c2b6a92f77c175d1f1d432` |
| Desktop | `413BB7B9ADDEF2D60AB5D513AC2426D4FC5947F9236C196651C5063435F87D90` |
| Unchanged engine | `B42EB7305DFF6DF7C727B36E5DC49161BD2DFC8AD3C65E7B23A08B673D4B6986` |

## Slightly larger midpoint markers — 6 November 2026

The maintainer requested a small enlargement after the proportional-sizing
correction. Diameter increases 25%, from 6 to 7.5 canvas DIPs (radius 3.75),
retaining its size relative to nodes. Placement, colours, occlusion, interaction
and detail visibility are unchanged. Existing focused checks passed **60/60**,
including rendered diameters of 2.25 / 4.125 / 7.5 / 15 screen DIPs at 30 / 55 /
100 / 200 percent zoom. The inert focused preview was reviewed. This is a
presentation-only refinement under TWEAK-026.

The clean production build matched the qualified 347-file source. Guarded
interface-only activation passed on its first attempt and completed the
eight-second observation, preserving the same engine process/start time/hash,
physical settings location, saved settings, preferences and routing. No engine
restart, rollback or settings restoration occurred. The final audit verified all
36 installed and 29 candidate payloads, with agreement between qualified source,
build receipt and installed inventory. Inventory source/provenance metadata was
reconciled after payload validation and a private backup; other payload and
release fields remain unchanged. This remains locally integrated, unreleased
0.5.0 work under the existing 0.4.0 Beta identity.

| Enlarged-marker build | SHA-256 |
| --- | --- |
| Source snapshot | `6c7a33f3415d409d3efbf2339725760727e876cf40c18486f0d9e3936fd08ef3` |
| Desktop | `1C4A113DAD502FB99CB18F27F3887332914F771AA9CECABBCDF74878C8A80BF6` |
| Unchanged engine | `B42EB7305DFF6DF7C727B36E5DC49161BD2DFC8AD3C65E7B23A08B673D4B6986` |

## Short-cable flow correction — 6 November 2026

[ISSUE-028](KNOWN_ISSUES.md#issue-028) records the maintainer's screenshot of a
short Echo-to-Audio cable folding back on itself. The forced 70-canvas-DIP
minimum handle reach causes this when the sockets are closer than that reach.
Research of ComfyUI's ordinary spline path confirmed a distance-proportional
alternative; the issue entry links the reviewed source revision.

Use one quarter of the actual endpoint distance for each symmetric horizontal
handle, without a minimum. Drawing, drag previews, cut/rewire sampling and
node-insertion sampling share the control-point calculation. The curve changes
continuously as nodes move and retains its symmetric half-length midpoint,
colours, 7.5-canvas-DIP dot, detail visibility and node occlusion. Tall vertical
routes can have wider shoulders than before; backward routes retain the bends
needed to meet socket directions. This remains a spline renderer, without
obstacle avoidance.

Safe detached verification passed **1,527 focused checks** across cable
presentation, hover bounds and connection model/interaction suites. Regression
coverage verifies that close forward links do not fold, the removed fold no
longer receives hits/cuts, and rewire previews match the established curve.
Zero/tiny/vertical/reversed/long routes, continuous movement, midpoint placement,
zoom, negative coordinates, DPI and existing insertion/rounding behavior pass.
The close-gap, tall-vertical and reverse previews were visually reviewed. These
checks use an inert plain-Application driver without production startup, a live
engine connection, personal settings reads or native windows.

The production build passed. All 29 candidate payloads and the 347-file source
snapshot match the qualified receipt, and the engine binary is unchanged.
Guarded interface-only activation succeeded on the first attempt and completed
the eight-second observation. The same engine process, start time and binary
hash remained in place, with unchanged settings/preferences and running paths;
advancing frames were verified. No engine restart, rollback or settings
restoration occurred. Final audit verified all 36 installed and 29 candidate
payloads, with current source, receipt and installed inventory in agreement.
After a private backup and payload validation, metadata reconciliation changed
only the source snapshot and local-activation base-inventory hashes; payload and
release fields remain unchanged. This remains unreleased 0.5.0 work under the
existing 0.4.0 Beta identity. Maintainer visual follow-up remains separate.

| Adaptive-cable build | SHA-256 |
| --- | --- |
| Source snapshot | `2c7d8ae0b58515f68a1c70f430f1fba54e78569cce86571491f5c4d1809fba64` |
| Desktop | `15AAA16CC8AF7CD09EEE609918132ADFBF84431C00F70E7F6B23031739032ADD` |
| Unchanged engine | `B42EB7305DFF6DF7C727B36E5DC49161BD2DFC8AD3C65E7B23A08B673D4B6986` |

## Hide disabled-cable markers — 6 November 2026

The maintainer requested no midpoint marker on a disabled connection. Cache the
connection's muted state with its visual and require an enabled connection in
the shared marker visibility decision. Dashed cables remain selectable, while
selection, zoom, high contrast and retained node controls cannot restore a
disabled marker. Re-enabling follows the usual detail visibility policy. The
recent curve correction, dot size and audio behavior remain unchanged.

All **110 focused cable checks** passed, including disabled selection, zoom,
high contrast, retained control editing, re-enabling and undo. The detached
disabled-cable screenshot was reviewed; both verification builds completed
without warnings/errors. Production staging passed with all 29 candidate
payloads verified and the 347-file source matching its receipt. The engine
binary is unchanged. Guarded interface-only activation passed on the first
attempt and completed its eight-second observation. The same engine process,
start time and hash remained running, with unchanged settings/preferences and
running paths; advancing frames were verified. No engine restart, rollback or
settings restoration occurred. All 36 installed and 29 candidate payloads passed
the final audit; source, receipt and installed inventory agree. After backup and
payload validation, inventory reconciliation changed only the source snapshot
and local-activation base-inventory hashes. Release identity is unchanged; this
remains locally integrated, unreleased 0.5.0 work under TWEAK-026.

| Disabled-marker build | SHA-256 |
| --- | --- |
| Source snapshot | `1512048a0318c98ef2a299b357f023d1f9781d99fa12c6505d2cba006d8ee4a0` |
| Desktop | `CFA2763F19B0E8C60FFAB9270988D899174102A8D0258C950CC7F06125771A26` |
| Unchanged engine | `B42EB7305DFF6DF7C727B36E5DC49161BD2DFC8AD3C65E7B23A08B673D4B6986` |

## Voice starter workspace — 6 November 2026

The maintainer authorized [IDEA-031's voice starter](IDEAS.md#voice-focused-starting-graph-proposal--2026-11-06)
as the new default, with clear spacing for cables. Native and desktop factories
now define Microphone → High-pass filter → Denoiser → Expander → Compressor,
then two separate cables to Listening output and Virtual output. Existing
new-node processing settings are retained, including RNNoise, the 80-Hz
high-pass and zero compressor make-up. Neutral names replace the numbered
names in the reference; no personal device assignments are copied.

Each chain node shares a top coordinate of 200, with left coordinates
48 / 499 / 950 / 1401 / 1924. Outputs share left coordinate 2447; Listening is
at top 48 and Virtual at 464. The node widths leave 95 canvas DIPs between
every consecutive column and between the stacked output bodies. A 40% fallback
view accompanies the existing automatic first-run Fit; no general camera or
saved-workspace framing behavior changes.

The initial 100-DIP layout fitted just below 30% at minimum window width. It
retained full controls on first load through hysteresis, but Fit after a deeper
zoom-out could retain simplified nodes. The final 95-DIP spacing corrects that
edge case without changing camera or detail-transition logic.

Both outputs start muted, Listening at −24 dB. Physical devices require explicit
selection; automatic binding of a recognized virtual output keeps its existing
policy. Existing version-2 graphs and legacy migration are preserved. The new
graph is adopted only for a new store or explicit Start fresh recovery; invalid
settings retain the existing blocked recovery behavior and preservation rules.
RNNoise still adds 30 ms including in bypass. This layout work does not establish
ideal processing thresholds or listening quality for every voice.

Native verification passed **289,921 graph assertions** and the full synthetic
engine lifecycle suite. The latter covers a missing store receiving the new
starter, no unassigned endpoint opening, assigned outputs remaining exactly
silent while muted, saved edits/reopen, retention of the former three-node
starter and intentionally empty workspaces, and existing backup/Start fresh/
corrupt/future-schema recovery cases. No personal engine or real audio devices
were used in those checks.

Desktop verification passed **225 checks / 22 renders / zero binding errors**
plus **38 preparation/recovery checks**. Normal 1280 × 800 and minimum
900 × 580 layouts were rendered at 96/192 DPI and reviewed. Final Fit zooms are
approximately 43.85% and 30.05%; the minimum-width Quiet Blocks → Fit regression
restores full controls. Neither node bodies nor controls overlap the minimap.
The earlier subpixel border touch in the preliminary layout is eliminated.

The real native/desktop boundary suite passed **802 checks**, including equality
of every field in the two independently constructed starter graphs and native
compile/round-trip preservation. Production build and its inert packaged
starter/credits smoke check passed. All 29 candidate payloads and the 347-file
source match the qualified build receipt. No live settings or audio devices
were used for these offline checks.

Guarded paired activation through the trusted shell and actual shortcut passed
on the first attempt. The new engine became ready in 0.327 seconds, restored
all 16 previously running paths and advanced frames through the eight-second
observation. The exact personal configuration, settings and preferences were
preserved; no new starter was written into the saved workspace. No rollback or
settings restoration occurred. Final audit verified all 36 installed and 29
candidate payloads, with current source, receipt and installed inventory in
agreement. Release identity remains 0.4.0 Beta; this is locally integrated,
unreleased 0.5.0 development. Maintainer visual/listening follow-up and final
release qualification remain separate.

| Voice-starter build | SHA-256 |
| --- | --- |
| Source snapshot | `38d8fc974209ab2a580516e14d86e1377f23a5d9ea427b116fde13ef215c6c6d` |
| Desktop | `B397639DACBFA900E3D83AA85FE555F46289FC2CB1455CF847FFBD8E2252184A` |
| Engine | `CF9A36EBE68FED00D2743F3DDA4AFF0E957624095AC3AFB1590D5F02F2175EC2` |
