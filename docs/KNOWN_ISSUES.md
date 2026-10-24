# VineMix known issues

A durable log of reported bugs, interface defects and existing usability limitations. Future feature proposals
belong in [development ideas](IDEAS.md). Recording an issue does not authorize
implementation or imply that it has been independently reproduced.

Use stable IDs such as `ISSUE-001`; never reuse them. Update related entries
instead of adding duplicates. Track status as **Reported**, **Confirmed**,
**In progress**, or **Resolved**, and record verification and the fixing version
when an issue is resolved. Keep reports distinct from suspected causes.
A documented limitation may be confirmed in source while its proposed remedy
remains undecided; record that distinction explicitly.

| ID | Issue | Status |
| --- | --- | --- |
| [ISSUE-001](#issue-001) | Settings search caret overlaps placeholder | Reported |
| [ISSUE-002](#issue-002) | Duplicate application monitoring | Reported limitation |
| [ISSUE-003](#issue-003) | Graph keyboard/accessibility gaps | Audit pending |
| [ISSUE-004](#issue-004) | Precise edits discarded on focus loss | Confirmed in source |
| [ISSUE-005](#issue-005) | Dense-graph navigation and Fit limits | Partly confirmed in source |
| [ISSUE-006](#issue-006) | Website node route is hard to follow | Resolved in local website |
| [ISSUE-007](#issue-007) | Automatic update checks can appear inactive and suppress failed retries | Fixed in Beta 5 source; release preparation |
| [ISSUE-008](#issue-008) | Installed Beta 2 reopens after an attempted Beta 4 update | Confirmed metadata defect; Beta 5 correction in preparation |
| [ISSUE-009](#issue-009) | Update window does not match the minimal application interface | Fixed in Beta 5 source; release preparation |

<a id="issue-007"></a>
## ISSUE-007: Automatic update checks can appear inactive and suppress failed retries

- **Reported and locally resolved:** 2026-10-24. **Status:** Fixed in Beta 5
  source; release preparation and final package verification in progress.
- **Reported:** A remote tester found the automatic-check control ineffective,
  while a manual check found Beta 4.
- **Verified in released source:** The checkbox only saves a preference. A
  single startup check waits 30 seconds, records an attempt before contacting
  GitHub, and suppresses subsequent startup checks for a day even after failure.
  There is no periodic check while the application remains open.
- **Expected:** Clear preference feedback, prompt checking when enabled, daily
  checks while running, and bounded retries after failure without downloading
  or installing automatically.
- **Local correction:** Record only successful checks, retry failures after 15
  minutes, defer to active manual operations, and cancel background work when
  disabled or quitting. Ignore the old attempt timestamp because it cannot prove
  success.
- **Verification:** Deterministic scheduler checks cover failure/retry, daily
  cadence, stored timestamps, busy manual operations and cancellation. The full
  detached desktop suite passes 12,905 checks with zero binding errors.
- **Next step:** Complete Beta 5 release verification and qualify the delivered
  correction on a separate test installation.
  The tester's exact automatic-check timing and network conditions remain unknown.
- **Evidence:** [Update investigation](UPDATE_INVESTIGATION_2026-10-24.md).

<a id="issue-008"></a>
## ISSUE-008: Installed Beta 2 reopens after an attempted Beta 4 update

- **Reported and locally corrected:** 2026-10-24. **Status:** Confirmed package
  metadata defect; isolated rollback reproduced. Corrected in Beta 5 source,
  with release preparation in progress. The tester's own journal has not been
  inspected.
- **Reported:** A remote tester using the installed distribution manually found
  Beta 4, downloaded and installed it, then saw Beta 2 after the application
  restarted.
- **Expected:** A verified successful update reopens Beta 4 in the existing
  installation and saved workspace; a failed update clearly reports recovery.
- **Verified:** The exact Beta 2 discovery code and signing trust accept the live
  Beta 4 release anonymously. Direct Beta 2-to-4 updates are allowed; Beta 3 is
  not a prerequisite. The displayed version comes from the running executable.
- **Confirmed cause:** The delivered engine's textual Windows `FileVersion`
  contains `0.3.0-beta.4`; the worker requires numeric `0.3.0.5`. All 35 local
  installed files match the package manifest. The exact original Beta 2 checker
  rejects that engine, despite its correct fixed numeric fields. The installed
  worker then rolls back before smoke/health. A synthetic transaction reproduces
  rollback and reopening; the same defect also affects standalone extraction.
- **Correction / verification:** Use numeric `FileVersion` in the native resource
  while retaining semantic `ProductVersion`. The unchanged old checker accepts
  the rebuilt engine. Real metadata and isolated rollback tests pass: 111 worker
  checks, five native metadata fixtures and nine release-preparation checks.
  New build and packaging gates verify actual delivered version metadata.
- **Diagnostic improvement:** Local source preserves the Setup exit code and
  writes its log in the private transaction folder. This does not retrofit the
  tester's already installed Beta 2 worker; the native metadata correction is
  what enables old workers to accept a future package.
- **Release decision (2026-10-24):** Prepare Beta 5 with corrected engine
  metadata, preserve the Beta 2/3/4 tags and downloadable bytes, and add notices
  to their release notes. Beta 2 remains provisional. Rebuilding older versions
  would create different bytes under the same version and erase useful evidence.
- **Recovery:** Save, **Quit VineMix**, then run Beta 5 Setup against the existing
  installation without uninstalling; standalone users replace the old EXE after
  quitting. Keep settings and recovery evidence. Intermediate betas are not
  required. Manual Setup bypasses the older worker's metadata check.
- **Next step:** Complete corrected-package release verification and qualify an
  actual delivered update on a separate test machine/account. Existing Beta 3/4
  packages remain affected. A sanitized tester journal can confirm the exact
  incident. Retain raw evidence outside the repository.
- **Evidence and workaround:** [Update investigation](UPDATE_INVESTIGATION_2026-10-24.md).

<a id="issue-009"></a>
## ISSUE-009: Update window does not match the minimal application interface

- **Reported and locally resolved:** 2026-10-24. **Status:** Fixed in Beta 5
  source; release preparation and final package verification in progress.
- **Reported / verified:** The maintainer requested a redesign. A detached render
  confirms the current window has a large blank area and a wrapped row of check,
  download, install, cancel and defer buttons, most disabled when idle.
- **Expected:** Match the application's typography, theme, spacing and quiet
  hierarchy, distinguish installed and available versions, and show relevant
  actions for checking, downloading, ready, installing and failure states.
- **Scope:** Compact themed content, selective action visibility, readable
  release notes, explicit automatic-check behavior and accessible status updates.
- **Verification:** Seven synthetic update-state previews were visually reviewed.
  Bindings, action visibility, preference behavior, editor validation, six
  width/DPI combinations and high contrast passed in detached WPF verification.
  The complete suite passed 12,905 checks and 236 renders with no binding errors;
  the production build passed with no warnings or errors.
- **Next step:** Complete Beta 5 release verification and check native
  keyboard/pointer behavior on a separate test installation. No personal
  application or audio-engine process was changed during local verification.

<a id="issue-006"></a>
## ISSUE-006: Website node route is hard to follow

- **Reported and resolved:** 2026-10-24. **Status:** Resolved in the local
  `website-redesign` preview; no application release was changed.
- **Reported / verified:** The second website section's lower connection
  detoured beneath a staggered middle node. A browser screenshot confirmed the
  layout and the repeated slogan text beneath it.
- **Expected:** Align “What you hear” and “What you share” vertically so both
  connections are easy to follow, and remove filler copy throughout the site.
- **Fix:** Stacked Listening output above Virtual output, connected each audio
  socket directly from Physical input, and simplified the page and credits copy.
- **Verification:** Desktop, tablet, phone and short-window layouts checked;
  no horizontal overflow. Static/reduced-motion routes and FAQ keyboard behavior
  checked. See [website QA](../website-redesign/QA.md) and the
  [updated preview](../website-redesign/nodes-preview.jpg).
- **Next step:** Review the local website revision. No public deployment or
  desktop application change is included.

<a id="issue-001"></a>
## ISSUE-001: Settings search caret overlaps the placeholder word

- **Reported:** 2026-10-24. **Status:** Reported.
- **Area:** Settings dropdown search field.
- **Observed:** The maintainer reports that the blinking text insertion caret
  appears in the middle of the first placeholder word within the search box.
- **Expected:** Adjust positioning so the empty field's caret aligns with the
  start of the placeholder and typed text, rather than sitting inside the word.
- **Impact:** Confusing visual alignment when beginning a settings search.
- **Reproduction to verify:** Open Settings, leave the search empty, and focus
  the search box. Inspect the caret against “Search settings and help”.
  Empty-field focus is inferred from the report; the exact trigger and affected
  build have not yet been confirmed.
- **Investigation:** Source inspected on 2026-10-24. The search box and its
  separate placeholder are built in
  [SettingsSearchView.cs](../desktop/VineMix.Desktop/SettingsSearchView.cs);
  the shared text-box template is in
  [App.xaml](../desktop/VineMix.Desktop/App.xaml). Their padding, margins and
  content alignment are investigation points, not a confirmed root cause.
  No live reproduction or external research has been performed.
- **Likely scope:** Search-field layout adjustment; check the shared template
  before deciding whether any change should apply beyond this field.
- **Next step:** Reproduce and compare caret, placeholder and typed-text
  positions, then verify the adjustment with an empty query, typing and clearing
  the query at supported interface scales. Preserve search and keyboard behavior.

<a id="issue-002"></a>
## ISSUE-002: Duplicate audio when monitoring an application

- **Originally captured:** 2026-10-20 as IDEA-012.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Reported limitation. **Previous priority:** P3.
- **Problem:** Original application playback and the captured, processed signal
  can reach the same headphones together. Users wanting processed-only listening
  can hear duplicate audio.
- **Expected outcome:** Provide a deliberate, understandable way to hear only the
  processed path while preserving capture and the user's Windows audio choices.
- **Evidence review (2026-10-24):** APP_AUDIO.md documents this routing limitation
  and the virtual-input workaround. This is expected loopback behavior with a
  usability consequence, not a newly confirmed capture-engine fault. No live
  audio check was performed. Earlier external research below was not refreshed.
- **Workaround:** Explicitly route the source app through the installed virtual
  input and use one capture path, as described in [application audio](APP_AUDIO.md).
- **Next step:** Qualify the proposed original-playback suppression in isolation;
  retain guided virtual-cable routing as an alternative if capture is also muted.

### Retained proposal and research from IDEA-012

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **User need:** Apply EQ or other effects to a running application's audio and
  hear only the processed result. Currently the app's original playback and Vine
  Mix's listening output can both reach the same headphones, producing duplicate
  audio.
- **Requested direction:** Add a button on running-app input nodes to silence
  the application's direct local playback while retaining capture and the
  processed listening route. This differs from the node's existing Mute, which
  silences its signal inside VineMix. Suggested label: **Mute original playback**,
  with a tooltip explaining the distinction; wording is not yet chosen.
- **Project fit:** [Application audio](APP_AUDIO.md) documents that process
  loopback copies the selected process tree's audio without suppressing original
  playback. `native/src/process_capture.cpp` implements that capture path, and
  application discovery already inspects Windows render sessions. Existing code
  deliberately does not alter application mute, volume, or endpoint assignments.
  This proposal would introduce an explicit user-controlled exception.
- **Primary-source research (2026-10-20):**
  [Microsoft ISimpleAudioVolume::SetMute](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmute)
  can mute a Windows audio session, and
  [Audio Sessions](https://learn.microsoft.com/en-us/windows/win32/coreaudio/audio-sessions)
  explains that a session groups streams and render-session mute can persist
  across restarts. The
  [application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
  documents endpoint-independent process-tree capture, but does not guarantee
  that muting the target's render session leaves this capture audible. Do not
  infer that guarantee from endpoint-master-volume or AEC loopback behavior.
- **Feasibility conclusion:** A session-mute control is technically available;
  whether it achieves original-playback suppression while retaining usable
  process-loopback audio must be tested with this engine and representative
  applications. No application was muted, Windows routing changed, or live audio
  test performed for this research. Do not present a simple mute API call as a
  proven solution to duplicate monitoring.
- **Alternative:** Explicitly route the source application to an installed virtual
  cable, then capture one path and listen through VineMix. This is already
  described in APP_AUDIO.md and in the
  [OBS application capture guide](https://obsproject.com/kb/application-audio-capture-guide).
  A guided routing action could be explored if session mute also silences capture,
  but introduces device/driver requirements and a separate Windows routing change.
  Do not enable Windows "Listen to this device" or another simultaneous capture
  route that restores a duplicate path.
- **Scope and tradeoffs:** Match all relevant render sessions to the selected
  process tree, including child processes and new sessions after app restarts or
  endpoint changes. A session may affect more than one stream; a browser target
  may include multiple tabs. Original playback mute affects the Windows session,
  not just the VineMix node, and might affect other consumers. Account for
  multiple nodes targeting the same app, external mute changes, and preserving a
  pre-existing user mute. Keep control and session discovery off the audio thread.
- **Open questions:** Reliable capture-after-session-mute behavior; intended scope
  when the app plays on several devices; ownership and restoration on unselection,
  node removal, shutdown, crash, or engine failure; and whether the button should
  require a healthy listening route. Avoid leaving the app unexpectedly muted or
  silently unmuting it over a later user change. Recovery policy remains undecided.
- **Next step:** Define an isolated, user-authorized local-audio qualification:
  compare capture signal presence and direct playback before/after Windows session
  mute using known non-protected material. Then check child sessions, relaunch,
  multiple nodes, and restoration. If capture is silenced, investigate guided
  virtual-cable routing. This entry authorizes research notes only.

<a id="issue-003"></a>
## ISSUE-003: Incomplete graph keyboard and accessibility workflows

- **Originally captured:** 2026-10-23 as IDEA-026.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Reported; workflow audit pending. **Previous priority:** P2.
- **Problem:** Earlier UI review identified gaps in operating and understanding
  a complete graph without a mouse, including traversal and accessible state.
- **Expected outcome:** Users can navigate, edit and inspect the graph with the
  keyboard and assistive technology, with current names and clear focus/state.
- **Evidence review (2026-10-24):** GraphWorkspace.cs and GraphNodeView.cs provide
  existing shortcuts, focus and automation labels. Their presence does not prove
  complete workflow coverage. Missing actions and rename/state announcements
  still need a focused audit; no live accessibility verification was performed.
- **Scope and next step:** Walk through create/select/connect/rename/inspect,
  document the exact failures, then choose the smallest corrections. The optional
  outline and traversal designs below remain candidate remedies.

### Retained proposal and research from IDEA-026

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **Origin and need:** Prior UI-review suggestion. Existing keyboard controls
  should extend to navigating and understanding a complete graph without a mouse.
- **Proposal:** Next/previous and connected-node traversal, keyboard positioning,
  F2 rename, automatic reveal of focused nodes, and an optional node/connection
  outline. Keep accessible node names current after rename; describe controls and
  real toggle states in context.
- **Project fit / research:** `GraphWorkspace.cs` and `GraphNodeView.cs` already
  provide shortcuts, focusable controls and sockets; [the guide](USER_GUIDE.md)
  records Find node, dial editing and high contrast. This completes remaining
  interactions rather than replacing existing support. Local review only.
- **Scope, tradeoffs and dependencies:** Medium; avoid shortcut conflicts and
  unexpected parameter edits during navigation. Coordinate lock (IDEA-017),
  groups and presets. Define traversal for branched graphs and editing fields.
- **Next step:** Walk through create/select/connect/rename/inspect using only a
  keyboard, then specify missing actions and assistive-technology expectations.

<a id="issue-004"></a>
## ISSUE-004: Precise value edits can be discarded on focus loss

- **Originally captured:** 2026-10-23 as IDEA-027.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Confirmed in source; live reproduction pending. **Previous priority:** P2.
- **Problem:** Pointer/VR users lack a convenient commit/cancel flow for precise
  values; clicking away can discard a pending edit without applying it.
- **Expected outcome:** Make pending values and their apply/cancel behavior clear
  and usable with a pointer, while preserving keyboard editing and validation.
- **Evidence review (2026-10-24):** RotaryValueControl.cs redraws the current value
  on LostKeyboardFocus. HandleEntryKey supports Enter and Tab to commit, and
  Escape to cancel; validation feedback already exists. This narrows the earlier
  proposal to remaining pointer usability and focus-loss behavior, rather than
  treating keyboard commit or validation as missing. No live check was run.
- **Workaround:** Commit a valid value with Enter or Tab before clicking away.
- **Scope and next step:** Reproduce pointer entry and focus changes; compare a
  compact apply/cancel affordance or another explicit pending-edit policy.
  Preserve one undoable edit per completed gesture. Fine adjustment and reset
  suggestions below remain optional, not prerequisites for correcting this issue.

### Retained proposal and research from IDEA-027

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **Origin and need:** Prior UI-review suggestion for mouse/VR users who cannot
  conveniently use Enter, Shift or double-click for precise edits.
- **Proposal:** Explore compact pointer Apply/Cancel, visible pending text,
  inline validation, a fine-adjustment option and a discoverable reset action.
  Preserve today's keyboard commit/cancel and deliberate wheel behavior.
- **Project fit / research:** `Controls/RotaryValueControl.cs` supports exact
  fields and keyboard gestures; pending text can be discarded on focus loss.
  Local review only. Controls and accepted compact styling already exist.
- **Scope, tradeoffs and dependencies:** Small to medium; the extra affordances
  must not overwhelm every node. Decide whether they appear only during editing
  or in a contextual editor. Keep one undoable edit per completed gesture.
- **Next step:** Compare one minimal contextual-edit design with the current
  control, using pointer-only entry, invalid text, cancellation and focus changes.

<a id="issue-005"></a>
## ISSUE-005: Dense-graph navigation and misleading Fit expectations

- **Originally captured:** 2026-10-23 as IDEA-028.
  **Moved to this log:** 2026-10-24. Original ID remains reserved.
- **Status:** Fit limitation confirmed in source; pointer workflow needs verification.
  **Previous priority:** P2.
- **Problem:** Fit promises to bring every node into view, but sufficiently large
  layouts exceed the minimum zoom. Earlier review also identified difficulty
  panning a full window of nodes using only the available pointer controls.
- **Expected outcome:** Explain when the graph cannot fully fit and provide a
  discoverable way to reach off-screen nodes in dense layouts.
- **Evidence review (2026-10-24):** GraphWorkspace.cs clamps FitAll to a minimum
  zoom of 0.3 while its tooltip says it brings every node into view. Middle-button
  and Space+drag panning already exist, so the earlier empty-canvas/right-drag
  note must not be read as the only panning route. Pointer-only accessibility
  and event handling over controls still need reproduction; no live check ran.
- **Workaround:** Use existing middle-button or Space+drag panning, Find node,
  and Frame selection as appropriate to the input device.
- **Scope and next step:** Check a dense graph beyond Fit's zoom limit and a
  pointer-only workflow; correct feedback and choose the smallest remaining
  navigation remedy. A hand mode or overview is a proposal, not a decided fix.

### Retained proposal and research from IDEA-028

These earlier notes preserve alternatives, tradeoffs and unresolved decisions.
They do not establish a chosen fix or implementation approval.

- **Origin and need:** Prior UI-review suggestion. Users need to pan even when
  nodes fill the window, and understand a Fit action that cannot show everything.
- **Proposal:** Explore a hand/pan mode or reserved navigation area, with an
  optional overview navigator. Make the minimum-zoom fit limit understandable
  rather than implying every graph will fit. Existing Find and Frame selection
  remain available.
- **Project fit / research:** `GraphWorkspace.cs` clamps zoom and initiates
  right-drag panning on empty space; [workspace controls](NODE_WORKSPACE.md)
  document existing navigation. Local inspection only; no external research.
- **Scope and tradeoffs:** Medium UI work; avoid intercepting dials, cable edits
  or selection. No decision to change supported zoom bounds or renderer. A hand
  mode may solve the main problem without an always-visible overview panel.
- **Next step:** Review a dense example at minimum zoom and select the smallest
  navigation addition that works with lock mode and pointer-only use.
