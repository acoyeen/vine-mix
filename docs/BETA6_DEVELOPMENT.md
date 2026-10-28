# Beta 6 live development

**Release planning update — 28 October 2026:** This development work now targets
**0.4.0 · Beta**. Use the [0.4.0 Beta release hub](RELEASE_0.4.0_BETA.md) for scope,
remaining work, attribution embedding and planned retirement of Beta 1–5
downloads. The identities and verification records below describe actual earlier
builds and remain unchanged until a verified release transition occurs.

**Status: active in the installed live testing app — 28 October 2026.**

The maintainer designated the installed live app as the ongoing testing target
for Beta 6. The canonical development version is `0.3.0-beta.6`, Windows numeric
version `0.3.0.7`, displayed as **0.3.0 · Beta 6**. This is a development identity,
not a finalized or published release.

## Included work

- [TWEAK-016](TWEAKS.md#tweak-016): Settings regrouped by purpose using the
  existing separators, with search first and Quit separate at the bottom.
- [TWEAK-015](TWEAKS.md#tweak-015): saved **Show action notifications** toggle
  for routine footer messages, retaining errors, save status and Activity.
- [ISSUE-004](KNOWN_ISSUES.md#issue-004): valid exact numeric edits apply on focus
  loss; invalid drafts remain visible; the text menu offers Apply and Cancel.
- [ISSUE-001](KNOWN_ISSUES.md#issue-001): Settings search placeholder/caret
  alignment across scaling and themes.
- Implemented Settings, workspace and information-window refinements, with
  bundled Beta 6 highlights.
- [TWEAK-013](TWEAKS.md#tweak-013): monochrome in-app branding and the brighter,
  slightly larger soft-white Windows/tray/shortcut icons from concurrent work.
- [TWEAK-014](TWEAKS.md#tweak-014): optional-driver Setup wording, default
  selections, explicit consent, ordering and restart guidance. These are Setup
  changes; local application activation does not run vendor driver installers.

## Local integration

The previous live controller and engine reported Beta 5. After the complete
candidate passed verification, the maintainer explicitly approved the brief
audio restart. Both executables now report Beta 6, preserving the strict
controller/engine release check.

Guarded activation ran through the trusted Windows shell with exact hashes and
process identity, a real Explorer shortcut launch, physical settings paths from
file handles, stable saved files and private backups of ordinary and redirected
data views. Stable saved files are not an independent check of pending in-memory
edits. Raw diagnostics and recovery backups remain outside the source checkout.

### Verification — 28 October 2026

- Canonical Beta 6 detached WPF build and run: **13,356 checks, 268 renders,
  zero binding errors**. The Beta 6 What's New rendering was inspected.
- All **17 native offline suites passed**, using synthetic audio, fake endpoint
  inventories and temporary configuration fixtures. No real endpoint or
  personal engine was opened by those tests.
- Existing UI-only activation regressions: **97 checks passed**.
- Full package activation and recovery: **75 isolated checks passed**, covering
  paired executable/inventory replacement, rollback, competing updates and
  delayed process failure after shortcut launch. Shared activation checks passed
  **40 checks**; native file-version regressions passed **5 checks**.
- The Installed-mode production build and inert delivered-executable smoke
  check passed. Both executables carry `0.3.0-beta.6` / `0.3.0.7`. Build inputs
  remained stable, and the candidate still matched the current source after
  verification.
- Read-only shell verification found one installed controller/engine pair and
  an intact Beta 5 inventory. The physical saved-settings handle resolved to the
  ordinary desktop data location. The Beta 6 inventory replacement was prepared
  in private evidence, and all three native runtime DLLs match the live package.

The applied source snapshot is
`22f929f504f07be10b79bc3fd786c3536005ea7389228de36a5a25d488f0935d`.
Applied executable SHA-256 identities:

| File | SHA-256 |
| --- | --- |
| `VineMix.exe` | `1e2b087b82af425220e387a14b78299c129ecd2c5c29fad002e63a4cb5f712b3` |
| `VineMix.Engine.exe` | `794536a4a61f33c822cc642f4feb04dfb8c0cdcd4c62e8e5219815904c325d7f` |

### Live activation — 28 October 2026

The authorized transition succeeded without rollback. The installed app and
engine hashes match the verified build above, and the installed inventory now
records Beta 6 with the matching files and development source provenance.

- The engine reopened the same physical ordinary-desktop settings file and
  retained the exact configuration. Saved settings and workspace preferences
  remained byte-identical; retained ordinary, redirected and legacy workspace
  copies were preserved.
- Every previously running audio path recovered, and the frame counter advanced.
  Paths that had been waiting were not represented as newly working.
- The actual Windows shortcut reopened the expected workspace window. The same
  new app and engine instances remained alive during the eight-second
  post-launch observation, with the app responding and hashes/settings stable.
- Previous binaries, inventory and data-view backups remain in private recovery
  evidence. No settings backup was restored over the live workspace. No vendor
  driver installer ran, and nothing was published.

ISSUE-001 and ISSUE-004 and the implemented branding/interface refinements are
now available in the live Beta 6 testing app. This deployment verification does
not replace native pointer/VR, installed caret or fresh-machine driver testing.

This local development overlay replaces the application and engine and records
their inventory provenance. Setup support files, Windows Installed apps version
registration and `installation.ini` remain from the last Setup installation.
Consequently, its old Setup downgrade marker does not yet represent Beta 6.
Use matching final Beta 6 Setup at finalization to reconcile that bookkeeping;
do not use an older Setup to maintain the development installation.

### Action notification toggle — 28 October 2026

The installed live testing app now includes **Settings → Show action
notifications**, enabled by default. Turning it off immediately dismisses the
current routine message and suppresses future routine messages and countdowns.
Errors remain visible and suppressed messages do not overwrite an active error.
Activity retains recent edits and the latest message. The preference survives
relaunch and does not change audio routing or undo history; re-enabling it does
not replay old messages.

- Detached WPF run: **13,367 checks, 268 renders, zero binding errors**, including
  preference persistence, compatibility with older preference files, immediate
  toggling, timer cleanup, error preservation and Activity retention. The Settings
  menu render was visually reviewed. These checks did not operate the live toggle.
- Production Installed build and inert delivered-app smoke passed. Activation
  safeguard suites passed **97 interface-only checks and 40 shared checks**.
  The first production attempt encountered an existing CMake cache/compiler
  reconfiguration issue; a retry completed successfully without source changes.
- Interface-only activation ran through the trusted Windows shell and the
  existing Explorer shortcut, with exact hashes, physical settings paths from
  open handles, stable files, private backups and installed-inventory checks.
  It passed without rollback. The engine kept its process, start time, path and
  binary hash; audio frames advanced and previously running paths were retained.
  Configuration, saved workspace copies and preferences remained unchanged.
  The reopened app was responsive and the engine remained unchanged through
  the eight-second observation. No settings were restored.
- The staged interface overlay retained the existing verified engine and runtime
  libraries. All **58 native source/release inputs** matched the prior engine's
  build receipt; the overlay receipt records the retained runtime provenance.
  The original fresh production build is preserved separately. No engine restart,
  vendor driver installation, publication or release-version change occurred.

This interface update supersedes the earlier controller identity above:

| Identity | SHA-256 |
| --- | --- |
| Current source snapshot | `302fa2dea4f28efee3e5f33301be6dc5868cf23b4acc8312f82628e56806a3ad` |
| Installed `VineMix.exe` | `e177a06424d5392e3a98c6f2e0f6bff3257675e1ad72fb0ef8d87a2a4ccbcbc3` |
| Retained `VineMix.Engine.exe` | `794536a4a61f33c822cc642f4feb04dfb8c0cdcd4c62e8e5219815904c325d7f` |

Both binaries remain Beta 6 (`0.3.0.7`). Earlier native-suite results describe
the unchanged native source; native suites were not rerun for this UI preference.
Final packaging and the Setup bookkeeping distinction above remain outstanding.

### Settings grouping — 28 October 2026

The installed Beta 6 interface now groups existing Settings items by purpose:
workspace controls, audio tools, application preferences, help/information,
then the separate Quit action. Search remains first. Activity follows Find node;
device visibility sits with audio actions; startup sits with update controls;
Help, What's new? and About share a group. Existing separator styling is retained
without new headings or submenus. Existing preferences and callbacks are retained.

- Production Installed build and inert delivered-app smoke passed. Detached WPF
  verification passed **13,367 checks, 268 renders and zero binding errors**, with
  the menu render visually reviewed. Existing search, keyboard-routing and action
  checks passed; native pointer/VR review remains for the maintainer.
- Interface-only activation safeguards passed **97 checks**, and shared activation
  checks passed **40**. The exact source inventory remained stable throughout
  verification, build and activation. All **58 native source/release inputs**
  matched the retained engine's original verified receipt; native suites were
  not rerun for this menu-order change.
- Guarded activation through the trusted Windows shell and actual existing
  Explorer shortcut passed without rollback. The workspace reopened and remained
  responsive. The engine retained its process, start time, path and binary hash;
  frames advanced and running paths were preserved. Saved settings and preferences
  stayed unchanged, with physical settings paths checked through open handles,
  private backups and matching installed inventory. The post-launch engine
  observation lasted eight seconds. No settings were restored.

This update supersedes the preceding controller identity:

| Identity | SHA-256 |
| --- | --- |
| Source snapshot | `90e243179a19a725e6cf1939c006719d4b7281bee0a30ae6fcc2a2b3c5fce623` |
| Installed `VineMix.exe` | `68baf8db48e34e54361dc4e96095395eb281809a070536780182540ce4654c71` |
| Retained `VineMix.Engine.exe` | `794536a4a61f33c822cc642f4feb04dfb8c0cdcd4c62e8e5219815904c325d7f` |

The private overlay receipt records the retained engine/runtime provenance and
the separately preserved fresh production build. Only the controller and its
installed-inventory entry were replaced; other installed payload files and Setup
registration retain their existing identities. Both executables remain Beta 6
(`0.3.0.7`). No engine restart, driver installation or publication occurred.

## Attribution documentation — 28 October 2026

The source `LICENSE` now identifies VineMix and `https://vinemix.app` in the
existing acoyeen copyright notice. The MIT permission and disclaimer text are
unchanged. `ATTRIBUTION.md`, linked from the README, contribution guidance,
distribution guide and branding guide, distinguishes required notice retention
from the requested acknowledgement inside modified programs. Earlier releases
retain their original notices and permissions.

Local verification checked the unchanged MIT terms, documentation links and the
existing packaging paths that copy `LICENSE` into the application payload and
installer. No new binaries or packages were built or activated for this
documentation change, and no downloads or website were published. This records
source preparation only; the installed inventory continues to describe its
previously verified build. Final release qualification must verify the updated
notice in the actual delivered packages.

## Attribution policy publication — 28 October 2026

After the source-only preparation above, the maintainer separately authorized
GitHub and website publication of the attribution policy. GitHub commits
`1503c04` and `84c7abb` publish the policy, MIT notice update, linked guidance
and website source. The footer at `https://vinemix.app/` now links to
`https://vinemix.app/attribution`. All 85 public website files matched the
deployed output, and live mobile/desktop attribution navigation passed. See
`website-redesign/HOSTING.md` for deployment evidence and the existing broader
browser-suite limitation. This publication does not finalize Beta 6 or update
desktop binaries, release downloads or installed build receipts.

## Named organizational groups — 28 October 2026

[IDEA-023](IDEAS_IMPLEMENTED.md#idea-023) is implemented and integrated into the
installed Beta 6 interface. Select nodes with a rectangle or additive selection,
then choose **Group selected nodes** from the right-click menu or press **Ctrl+G**.
The frame derives its bounds from its members, follows their individual movement
and measured size, and moves every member when its heading is dragged. A group
has no input/output sockets, processing, routing or shared audio controls.

Headings retain at least 15 screen DIPs down to 30% zoom. Double-click a heading
or select it and press **F2** to rename it. Its menu offers seven named colors
that fit the interface palette. The empty frame interior keeps normal canvas
selection and navigation. Fit and Frame selection include complete group bounds,
including their headings. High-contrast styling preserves the visible frame.

Membership is explicit: **Add to group** transfers selected nodes, **Remove from
group** releases them, and **Ungroup (keep nodes)** removes only the frame.
Each node belongs to at most one group; groups do not nest. Moving a member
outside its previous bounds expands the frame. Empty groups disappear. Selecting
the heading and pressing Delete ungroups it; selecting member nodes and pressing
Delete retains the existing node-deletion behavior.

Frame identities, names, palette keys and member IDs live in `ui.json`, outside
engine configuration and settings JSON. Frame-only edits and their Undo/Redo do
not enqueue engine configuration changes. Existing node-position saves handle
group movement. Complete copied groups retain presentation with remapped member
and frame IDs; partial copies omit the frame. Layout locking protects all group
edits. Snapshot reconciliation, failed edits and history conflicts preserve or
restore the appropriate membership without overwriting newer local frame edits.

- Final detached WPF verification passed **13,491 checks**, produced **272
  renders**, and reported **zero binding errors**. Coverage includes automatic
  bounds, individual and shared movement/cancellation, member size changes,
  single-member group movement without cable insertion, title readability,
  high contrast, membership/lock behavior, clipboard remapping, preference
  reload, Undo/Redo and pending-edit acknowledgement/rejection/rebase conflicts.
- Real mouse and keyboard checks in an isolated preview confirmed rectangle
  selection, right-click creation, double-click rename and Enter, header drag,
  preset selection, ordinary clicks inside a frame, individual-member dragging
  with updated bounds, and heading readability at 30% zoom. Only synthetic
  example nodes were used. This is not a live audio-quality or VR qualification.
- Production Installed build and delivered-controller inert smoke passed with
  stable source inputs. Both executables retain `0.3.0-beta.6` / `0.3.0.7`.
  Interface-only activation safeguards passed **97 checks**; shared activation
  safeguards passed **40**. All **58 native source/release inputs** matched the
  retained engine's original verified receipt. Native audio suites were not
  rerun for this desktop-only feature.
- Guarded activation through the trusted Windows shell and the actual Explorer
  shortcut passed without rollback. The engine retained its process, start time,
  path and binary hash. Its frame counter advanced and running paths were
  preserved before the interface reopened; the same engine was observed for
  eight seconds afterward. Physical settings identity was checked using open
  handles, private recovery backups were retained, and saved settings and
  preferences were unchanged. No recovery snapshot was restored. These checks
  do not claim independent inspection of pending editor text.

This update supersedes the preceding controller identity:

| Identity | SHA-256 |
| --- | --- |
| Source snapshot | `10747e3be8f2ffb44c56e2f5a00b9de1858b3fb61cba6a53387a2bc42e397519` |
| Installed `VineMix.exe` | `676af204d89841f2aba635181ec99dc2f411b9d867442b0eacf93eb02c84e011` |
| Retained `VineMix.Engine.exe` | `794536a4a61f33c822cc642f4feb04dfb8c0cdcd4c62e8e5219815904c325d7f` |

The private overlay receipt records the new interface source and retained native
provenance alongside the separately preserved fresh production build. The
installed controller's inventory entry matches its bytes; the manifest's root
source metadata and other installed payload files retain their earlier release
identities. This is local Beta 6 development integration, not final package
qualification or publication. Earlier downloads and repository visibility are
unchanged.

## Group refinements and node stacking — 28 October 2026

The maintainer requested stronger group colors, selection from across the
exposed group background, shorter hover help and reliable overlap order.
Group fills and borders now have greater opacity. Their exposed backgrounds
select and move the group; node and cable controls retain input precedence.
Shift/Ctrl-drag still permits box selection, Space/middle-drag pans, and only
the heading's double-click starts renaming. Hover help shows the group name,
node count and locked state when relevant. The Settings notification tooltip
is now one short sentence.

Node paint order now follows explicit selection and dragging within the
session. Releasing a drag no longer resets the nodes to their old order.
Telemetry, zoom and recreated node views retain the interaction order; shared
group movement preserves member order. Stacking is interface state and does
not change graph storage order, engine configuration, preferences or Undo.
See [TWEAK-017](TWEAKS.md#tweak-017), [TWEAK-015](TWEAKS.md#tweak-015) and
[ISSUE-022](KNOWN_ISSUES.md#issue-022).

- Detached WPF verification passed **13,526 checks**, produced **273 renders**
  and reported **zero binding errors**. Added coverage exercises frame-body
  hit testing, background movement and modifier pass-through, selection and
  drag overlap order, cancellation, locked layouts, view recreation,
  multi-selection and group-order preservation. The stronger group render was
  visually reviewed. Real pointer verification of these refinements was not
  completed; computer-use testing stopped at the maintainer's request.
- Production Installed build and delivered-controller inert smoke passed.
  Source inputs were stable across verification, build and activation. Both
  executables retain `0.3.0-beta.6` / `0.3.0.7`. UI and shared activation guards
  passed **97** and **40** checks. All **58 native source/release inputs** still
  match the retained engine's original receipt; native suites were not rerun.
- Guarded trusted-shell activation and the existing Explorer shortcut launch
  passed without rollback. The engine retained its process, start time, path
  and binary hash, with advancing frames and preserved running paths before
  the interface reopened; it remained unchanged during the eight-second
  observation afterward. Physical settings paths were verified using open
  handles and private backups retained. The current saved workspace and
  preferences were unchanged, with no recovery snapshot restored. Pending
  editor text was not independently inspected.

This local update supersedes the preceding controller identity:

| Identity | SHA-256 |
| --- | --- |
| Source snapshot (313 files) | `4f5e931d9331a3c195df6aad474d966a188b5407400fa17af8d518456abca935` |
| Installed `VineMix.exe` | `ba097e58077edb5854745f68eec4c8b389fb32537bce8b5325905bca48eb691a` |
| Retained `VineMix.Engine.exe` | `794536a4a61f33c822cc642f4feb04dfb8c0cdcd4c62e8e5219815904c325d7f` |

The private overlay receipt records the new controller source and retained
native provenance alongside the fresh production build. Installed inventory
matches the controller bytes; the manifest's root source metadata and other
payload files retain their earlier release identities. This remains local
Beta 6 development integration, without finalization or publication.

## Mixing nodes — 28 October 2026

The maintainer authorized six additions and an installed-app restart for testing:
Channel Swap, Stereo Width, Pan, Crossfade, Stereo Combine and Stereo Split.
They share the existing Mixing colors, rotary controls, numerical entry,
meters, socket shapes and compact help. The Add menu now contains 30 types;
the two compatible legacy types remain available in saved workspaces.

Named audio sockets are preserved in graph storage, clipboard copies,
Undo/Redo, same-node rewiring, cable snapping, connection details and
single-input/output effect insertion/removal. Old cables default to `audio`
ports. Stereo Combine averages each input to mono and routes it to its named
side; empty inputs remain silent. Stereo Split extracts each side as dual mono.
Both structural nodes omit bypass. Pan folds to mono before constant-power
positioning, Width scales mid/side width, and Crossfade offers Equal power and
Linear curves with A as its initial and bypass signal. See the
[user guide](USER_GUIDE.md#node-library) and [protocol](CONTROL_PROTOCOL.md).

Synthetic verification covers level/channel isolation, per-sample split/combine
reconstruction without extra block delay, source-channel sidechain selection,
control smoothing, legacy JSON, port validation and channel metadata. All 17
native offline suites passed; expanded graph coverage passed **279,780
assertions**. Thirty Pan or Crossfade nodes measured below **245 microseconds
p99** against a 5,000-microsecond graph block in synthetic benchmarks. This is
not a hardware round-trip latency or audible quality measurement.

Activation comparison now fills only documented missing mixing defaults in
in-memory snapshot copies, allowing exact comparison with the older engine's
schema. It preserves explicit values, nulls and unrelated fields, never rewrites
settings, and retains the existing file-hash, physical-handle, backup, process,
inventory and trusted-shell safeguards. The package-activation suite passed
**79 checks**. Shared and interface activation guards also passed **40** and
**97** checks respectively.

The final detached WPF run passed **14,089 checks**, produced **279 renders**
and reported **zero binding errors**. Coverage includes real node controls,
named-port pointer gestures in detached previews, same-node rewiring, separate
socket fill/snapping, clipboard round trips and undo/replay conflicts. The six
new-node renders were visually inspected. No computer-use tools were used;
real-pointer and audible verification remain with the maintainer's live test.

The production Installed build and delivered-controller inert smoke passed.
Source inputs remained stable across final verification, build and activation.
The paired app/engine update succeeded through the trusted Windows shell and
the existing Explorer shortcut, with no rollback. Both retain
`0.3.0-beta.6` / `0.3.0.7`. Physical settings identity was verified using open
handles; private backups of ordinary and redirected data views were retained.
The saved settings file and workspace preferences were unchanged. The new
engine restored every previously running assigned path with advancing frames;
the reopened app/engine pair passed the eight-second observation. Installed
inventory records both new executable hashes and the matching source identity.
Pending editor text was not independently inspected; no recovery snapshot was
restored. This remains local Beta 6 development, without publication or
finalization.

| Identity | SHA-256 |
| --- | --- |
| Source snapshot | `0a3b19d082b976c1db1e524730725fea4b2a2d01c8e16bbd412ebd239b8d7f8f` |
| Installed `VineMix.exe` | `a07b8cc058ca2c6168cec7ffd3313d23a11778a5caa918b2013cca907e13baa4` |
| Installed `VineMix.Engine.exe` | `d68c050c53fe5f3f09190c6559e3b1a777a22b269f8ed26a7b7da1feadb0e99a` |

## Level control cleanup and Linked control Multiplier — 28 October 2026

The maintainer authorized [TWEAK-018](TWEAKS.md#tweak-018): Level control inputs
now appear on nodes with dedicated Level knobs. Inputs, outputs, Mix and saved
Gain/Reroute nodes retain those sockets. Effects and the other mixing utilities
use a downstream Mix when level control is needed. Existing effect-control
connections remain visible and supported until explicitly removed; new links
cannot target those nodes. No implicit graph migration changes processing order,
bypass behavior, limiter behavior or the saved workspace.

Linked control's dial is now **Multiplier**, **0–200%**, with **100%** as its
centre and reset value. Level and Level control names elsewhere are retained.
Tooltips are concise; Help's sample connects the controller to a listening
output. The native scalar makes 0% exact silence, with smooth transitions and
normal bypass returning to unity. The legacy `-1` sentinel retains existing
decibel controls until an explicit edit; stored gains above 200% display their
actual percentage with an explicit conversion action instead of silently
clamping. Clipboard, reset, undo, legacy links, named ports and meter/reduction
feedback use the same rules.

All **17 native offline suites passed**, including **281,251 graph assertions**.
The package-activation compatibility suite passed **80 checks**, including the
missing-field sentinel and preservation of an explicit zero multiplier. Rendered
Multiplier, Denoiser and Stereo Split nodes were visually inspected. The final
detached WPF suite passed **14,103 checks**, produced **283 renders**, and reported
**zero binding errors**. The production Installed build and delivered-controller
inert smoke passed with stable source inputs. Both executables retain
`0.3.0-beta.6` / `0.3.0.7`.

Trusted-shell paired activation and the existing Explorer shortcut launch
succeeded without rollback. Saved settings and workspace preferences were
unchanged; physical settings identity was checked using open handles and private
backups retained. The engine restored all previously running assigned paths
with advancing frames. The reopened app/engine pair passed the eight-second
observation and installed inventory matches both binaries and source identity.
No recovery snapshot was restored. Pending editor text was not independently
inspected. No computer-use tools were used, no release was published, and
real-pointer/audible use remains the maintainer's testing step.

| Identity | SHA-256 |
| --- | --- |
| Source snapshot | `143fa982b3a1d8695035db1e11e1c8371b8459a3a460cef1a6c105450da54e34` |
| Installed `VineMix.exe` | `47a4447c2c2c753a19fba314c44b6f7b098db045feb501c1d466200b64f1810f` |
| Installed `VineMix.Engine.exe` | `9c60f6039e039103a92d2280577050ef43f9a2971f8a42dd67a805492737d510` |

## Ducking Trigger label — 28 October 2026

[TWEAK-019](TWEAKS.md#tweak-019) shortens the purple Ducking input to **Trigger**,
with matching accessible naming, connection guidance and a one-sentence tooltip.
Only presentation and existing label expectations changed. The detached suite
passed **14,103 checks**, produced **283 renders** and reported **zero binding
errors**; the Ducking render was visually inspected. Production build and inert
smoke passed with stable source. All **58 native/release inputs** matched the
retained engine's verified build receipt; native suites were not rerun.

Trusted-shell interface-only activation and the existing Explorer shortcut
launch succeeded without rollback. The engine process, start time, path and
binary remained unchanged, with advancing frames and preserved running paths;
it remained unchanged during the eight-second post-launch observation. Physical
settings identity was verified through open handles, private backups retained,
and settings/preferences remained unchanged. Installed controller inventory
matches its bytes; the earlier package root provenance remains retained. No
recovery snapshot was restored and pending editor text was not independently
inspected. This is local Beta 6 development, not publication.

| Identity | SHA-256 |
| --- | --- |
| Source snapshot | `a65aa107e8266f7b9ed1b6df4fb588056f388d1638440054298580ce1dea0846` |
| Installed `VineMix.exe` | `ee70a41a60e6cf260141211571398f70d9aefc3983d2089eba792fec109723e7` |
| Retained `VineMix.Engine.exe` | `9c60f6039e039103a92d2280577050ef43f9a2971f8a42dd67a805492737d510` |

## Group color swatch — 28 October 2026

[TWEAK-020](TWEAKS.md#tweak-020) replaces the narrow heading bar with a rounded
square in the group's preset color. Its 14 × 14 screen-DIP size, 3-DIP corners
and 8-DIP title gap stay fixed throughout 30–200% canvas zoom. Group behavior
and high-contrast system colors are retained.

The existing detached WPF suite passed **14,115 checks**, produced **285 renders**
and reported **zero binding errors**. Heading renders at 30%, 100% and 200% zoom
were visually inspected. Production build and delivered-controller inert smoke
passed with stable source inputs. No computer-use tools or real-pointer tests
were used. All **58 native/release inputs** matched the latest Level control
engine receipt. Activation guard files were unchanged from the earlier passing
97 UI and 40 shared checks; those tests and native suites were not rerun.

Trusted-shell interface-only activation and the existing Explorer shortcut
launch passed without rollback. The engine retained its process, start time,
path and binary, with advancing frames and preserved running paths before the
interface reopened; it remained unchanged during the eight-second observation
afterward. Current saved settings/preferences were unchanged. Physical settings
identity was checked using open handles, private backups retained and installed
inventory verified. No recovery was restored or pending editor text independently
inspected. The version remains `0.3.0-beta.6` / `0.3.0.7`; this is local
development integration without release publication or finalization.

| Identity | SHA-256 |
| --- | --- |
| Source snapshot (318 files) | `b4faae3fed7dedd013c144943d0e3126513f3c3cf70063aedfed0fda89bacef7` |
| Installed `VineMix.exe` | `5856feac2733a6c0bb5b6d80586df6a8ba4c029059a307f417f2d5c73371f6ec` |
| Retained `VineMix.Engine.exe` | `9c60f6039e039103a92d2280577050ef43f9a2971f8a42dd67a805492737d510` |

The private overlay receipt pairs the new controller source with retained native
provenance. The installed controller inventory matches its bytes; the package's
root provenance and other payload identities remain from the earlier package.

## Centered group heading — 28 October 2026

The maintainer superseded the swatch design in [TWEAK-020](TWEAKS.md#tweak-020):
the heading now contains only the centered group name. The color marker and
visible node count are removed, with symmetric title padding and a centered
inline rename editor. The frame's existing colors and interactions remain.
The preceding swatch evidence records the earlier candidate, not this direction.

The detached suite passed **14,115 checks**, produced **285 renders** and reported
**zero binding errors**. The centered name-only heading was visually reviewed
in the 30%, 50% and 100% renders; the suite also produced the 200% view.
Production and delivered-controller inert smoke passed with stable source.
No computer-use tools were used. The 58 native/release inputs and runtime
binaries match the retained engine receipt. Unchanged activation guards retain
their earlier 97 UI and 40 shared passing checks; native suites were not rerun.

Trusted-shell UI-only activation through the actual Explorer shortcut passed
without rollback. The engine retained its process, start time, path and hash;
frames advanced and running paths were preserved before UI reopening, followed
by eight seconds of unchanged-engine observation. Current saved settings and
preferences were unchanged, with physical paths checked using open handles,
private backups retained and installed inventory verified. No recovery snapshot
was restored and pending editor text was not independently inspected.

| Identity | SHA-256 |
| --- | --- |
| Source snapshot | `210fb8931b8b34c3d635781c04df3c1ee881efad972b53414edce1a8707e30e5` |
| Installed `VineMix.exe` | `1669d9d246195c5e2f58041707ffbc5a6b7c5180120400bba8745c51666bbf7f` |
| Retained `VineMix.Engine.exe` | `9c60f6039e039103a92d2280577050ef43f9a2971f8a42dd67a805492737d510` |

The private overlay receipt preserves the new UI source and retained-native
provenance; installed controller inventory matches its bytes while earlier
package root provenance remains. Version `0.3.0-beta.6` / `0.3.0.7` is unchanged.
This is local development integration, not finalization or publication.

## Finalization

The successor release is **0.4.0 Beta**. Follow the
[release hub](RELEASE_0.4.0_BETA.md) for finalization, qualification, publication
and the subsequent retirement of uploaded Beta 1–5 assets while retaining their
pages and tags. The earlier instruction to preserve those downloads is
superseded by the maintainer's 28 October release-planning direction.

The existing local binaries retain their recorded `0.3.0-beta.6` identity until
the verified paired transition. Test discovery and installation of
`0.4.0-beta`, preserve settings and installed inventory, and obtain permission
for the brief engine restart before personal live activation. Earlier
development evidence does not qualify the final release packages.
