# VineMix — testing and hardware qualification

Historical package/evidence paths and descriptions of the running session below
refer to the original test date. Raw records and backups are retained privately
outside this checkout.

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](../BETA_TESTING_CURRENT.md) for the active release checks.

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

## Beta 1 qualification — 21 October 2026

Target **0.3.0-beta.1**, shown as **0.3.0 · Beta 1**. The current beta changes are
in progress. Historical passes below describe their named source snapshots and
artifacts; they do not independently qualify the new package. The canonical
current status and exact final-artifact evidence are in
[the beta implementation record](../BETA_IMPLEMENTATION.md).

Required gates still need final evidence before distribution:

- Normal Explorer/shortcut and tool-origin launch persistence, including the
  physical file path from an open settings handle, saved graph/assignments,
  independent desktop reads, shutdown and reopen.
- Detached recovery, ownership, restart-safe migration and starter checks; fresh
  data, damaged/missing established settings, backup restore, preserved originals,
  interrupted migration and conflicting legacy copies.
- Final standalone EXE and Setup identity, extracted content, .NET/runtime files,
  notices, hashes, process coordination and safe replacement/uninstall behavior.
- Clean Windows 11 x64 installation of both packages, optional-driver success,
  cancellation, restart, no-driver operation, repair/upgrade and removal.
- Real-device reconnect, sleep/resume, independent listening/virtual output mutes,
  and an eight-hour sustained session with recorded observations.
- Investigation of the earlier unexplained closure; low-memory correlation alone
  is not an established cause or proof of a fix.

Run detached verification with `scripts/verify-offline.ps1`. The verification
build contains the render/test implementation and uses temporary state and
synthetic audio; the release build excludes it. Render beta documentation with
the verification executable's `--render-beta-docs --verification-dir <new-folder>`.
Its fresh and configured fixtures contain exactly the three starter nodes and
two routes, muted outputs, synthetic IDs and generic physical device names.
Inspect every delivered screenshot; no personal workspace should be loaded to
make a documentation image. This render mode is unavailable in the release app.

The documentation render on 21 October passed **58 detached checks**, produced
**six images** and reported **zero binding errors**:
`artifacts/beta-preparation/beta-docs-4/results.json`. All six images were visually
inspected, including fresh/configured starters at 96/192 DPI, Settings and About.
The actual About Copy/Close handlers used inert callbacks, without accessing the
system clipboard. Output bodies have a checked visible gap. The three selected
repository images use synthetic devices and no personal workspace. This is
documentation/UI evidence, separate from package and hardware qualification.

For manual beta testing and useful bug reports, use
[the beta tester guide](../BETA_TESTER_GUIDE.md). Record **Not run** when a real
scenario is unavailable; offline simulations do not establish audible or hardware
reliability. Keep exact source/build/version, artifact checksum, report path,
date and tester context with a result. Review raw diagnostics for private paths
and device identifiers before sharing.

## Earlier verification evidence

The entries below are retained as the record of work before Beta 1.

## Run the current checks

The Windows launch-context correction on **21 October 2026** passed **10,821
detached desktop checks / 215 renders / zero binding errors**, plus **eight
detached activation-process tracking checks**. Reports:
`artifacts/startup-priority-20261021/ui/results.json` and
`artifacts/startup-priority-20261021/activation-tracking-results.json`.

The earlier explicit `--settings` correction passed a tool-launched reopen but
did not fix the cause. The next recurrence proved that identical printed paths
opened two different physical files:

| Launch context | Physical storage | Observed workspace |
| --- | --- | --- |
| Codex execution environment | Codex package `LocalCache\Local\AudioMixer` | Custom revision 6376, eleven nodes / eleven connections |
| Ordinary Explorer/shortcut | `%LOCALAPPDATA%\AudioMixer` | Default revision 1, eight nodes / nine connections |

Win32 file handles, native streams, `ConfigurationStore` and managed file reads
all reproduced the split. Both probe processes reported no package identity,
so that API alone cannot identify inherited AppData redirection. Passing an
absolute settings filename or applying desktop-app policy 1 also failed to
normalize the file view. The successful exact-source probe launches with the
current desktop shell as its documented process parent. Ordinary application
startup now performs that handoff before preferences, singleton or engine setup;
preview/render modes remain detached. Checks cover argument preservation,
Windows quoting, background mode, loop prevention and safe launch failures.

Both settings views were backed up. The custom primary, last-good backup and UI
preferences were then recovered byte-for-byte into the ordinary desktop folder.
Full engine shutdown/reopen was verified separately through an Explorer shortcut
and through the Codex tool environment. Both restored the exact saved graph,
controls, assignments and camera without warnings; audio frames advanced. A final
independent desktop-context read resolved the real folder and matched all three
recovered file hashes. Native engine and runtime bytes were unchanged.

Evidence is under `artifacts/startup-priority-20261021/`: `file-probe/`,
`standalone-csharp-probe/shell-hidden-results.json`, `workspace-recovery.json`,
`live-activation.json`, `tool-cold-reopen.json` and `final-state.json`.
Activation scripts now check physical file paths before changing processes, and
wait for the final responsive controller rather than its short-lived launcher.

No application-specific Windows crash report was found for the unexpected exit.
Windows logged low virtual memory at 15:39 UTC+10; a causal relationship is
unproven. Normal desktop starts, exits and unhandled exceptions now write to
`%LOCALAPPDATA%\AudioMixer\desktop.log` for future diagnosis. This verification
does not replace listening or long-duration hardware qualification.

Primary references verified **21 October 2026**:
[Microsoft's AppData virtualization documentation](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
and [Microsoft's shell-parent process launch example](https://devblogs.microsoft.com/oldnewthing/20190425-00/?p=102443).

The GitHub synchronization review on **20 October 2026** rebuilt the current
source and passed all **17 native offline suites** and **10,806 detached desktop
checks / 215 renders / zero binding errors**. Reports:
`artifacts/github-sync-20261020/offline/results.json` and
`artifacts/github-sync-20261020/offline/ui/results.json`.

A separate self-contained package build reproduced both installed executables
byte for byte: desktop SHA-256
`2D94EFE8F2C67D5658515F152550E75EB5CC2B525D6B681598D07CB6459DA01F`
and engine SHA-256
`8937839894EC93A92831294DE587F9075B1DE0D223A4DD6059ABEB84E39E6875`.
The normal and 200% workspace renders, minimum-size Controls view and Settings
menu were visually inspected. Repository screenshots now use these detached
fixtures and show the current wide logo and Settings search. The logo has no
hover tooltip; its website action and accessible identity remain.

Verification used synthetic audio, temporary settings, fake devices and detached
controls. It did not stop or replace the installed application, use its control
pipe, change personal settings, or perform audible/hardware qualification.

The Controls hierarchy update on 20 October 2026 passed **10,812 detached
checks / 215 renders / zero binding errors**:
`artifacts/controls-hierarchy-20261020/ui-final/results.json`.
Section headers use flat shaded bars and larger right-side chevrons; action rows
are indented beneath a vertical guide. Alignment was captured without a selected
action at normal size, minimum size and in high contrast; those layouts were
visually inspected. Existing Controls selection, help and tooltip checks remain
enabled. The self-contained executable was hash-stable during verification.
No Computer Use, physical input or shown-window automation was performed.
Engine and runtime bytes are unchanged.

The ordinary-control tooltip correction on 20 October 2026 passed **10,794
detached checks / 212 renders / zero binding errors**:
`artifacts/tooltip-owners-20261020/ui-final/results.json`.
The exact Controls → View & zoom reference buttons are checked before recording
injection to prove production native-service suppression. Every adjacent pair
is exercised in both directions, with immediate visible handoff, no extra move,
late leave, pending retarget and unchanged same-owner deadlines. Checks also
cover all Help-topic tooltip owners, stationary scrolling, collapse/topic/reopen,
actual Static/Dynamic and disabled device choices, non-hit-testable meter internals
and live feedback-tooltip replacement. The startup-registration omission variant
fails before test setup can install replacement behavior:
`artifacts/tooltip-owners-20261020/regression-no-registration/results.json`.
The previous context-menu regression remains enabled over the shared core.
Source inventory: `artifacts/tooltip-owners-20261020/tooltip-inventory.txt`.
No Computer Use, physical input, native popup/window or focus automation was
performed. Native pointer presentation remains outside detached verification.
The self-contained executable was hash-stable and engine/runtime bytes unchanged.

The menu-tooltip handoff correction on 20 October 2026 passed **10,481 detached
checks / 212 renders / zero binding errors**:
`artifacts/tooltip-handoff-20261020/ui-final/results.json`.
Controlled pointer samples hit-test real Settings, device-submenu, Help-example
and Add-menu geometry. Installed routed handlers are exercised upward/downward,
without extra movement, in both enter/leave orders, with stale pending owners,
same-row deadlines, separators, search fields, disabled rows, filtering, closure,
unload and regenerated menu rows. Two legacy behavior variants independently fail
the intended entry-scheduling and pending-retarget assertions; reports are under
`artifacts/tooltip-handoff-20261020/regression-*`.
Menu commands, graph, preferences, save state, history and keyboard focus remain
unchanged. The recording presenter opens no native tooltip or menu. No Computer
Use, physical input or shown-window automation was performed; native pointer/root
selection and popup placement remain outside these detached checks. Engine and
runtime bytes are unchanged.

The simplified Controls and Settings search update on 20 October 2026 passed
**10,347 detached checks / 212 renders / zero binding errors**:
`artifacts/controls-search-20261020/ui-final/results.json`.
Help now exposes six topics; the removed Voice Color, Nodes & cables and Canvas
navigation lessons are absent. Controls checks cover one expanded section,
separated action/key rows, selected explanations and exact-entry links. Settings
checks cover inline Zoom/reset instructions, aliases, case/whitespace/multiword
queries, matching submenu reachability, keyboard decisions, query cleanup,
offline use and unchanged mixer state. The no-match message is checked against
ancestor clipping as well as its logical text. Normal, minimum-size and
high-contrast Controls, Settings and search renders were inspected.
The self-contained binary was hash-stable throughout verification. No Computer
Use, native input or shown-window automation was performed. Native popup/focus
behavior remains outside detached verification; engine/runtime bytes are unchanged.

The flat menu presentation update on 20 October 2026 passed **10,399 detached
checks / 220 renders / zero binding errors**:
`artifacts/menu-style-20261020/ui-final-2/results.json`.
Help topics, compact/minimum-size layouts, EQ/Voice Color examples and the
Settings, Add, Activity and Find node surfaces were visually inspected using
detached renders. The existing suite continues to verify shortcut decisions,
search, tutorial editing/reset/isolation and high-contrast resources. No Computer
Use or shown-window automation was performed for this revision. Native input,
focus and popup placement remain outside detached verification. Audio processing
and the running engine/runtime are unchanged.

The interactive Help & shortcuts update on 20 October 2026 passed **10,399
detached desktop checks / 220 renders / zero binding errors**:
`artifacts/help-menu-20261020/ui-final-2/results.json`.
All nine topics have normal and minimum-window renders. Dial, EQ, Voice Color
and canvas examples also render at 144/192 DPI; reference, buttons, EQ, Voice
Color and connections have process-local high-contrast renders. The rendered
layouts were inspected. The checks cover actual reusable controls, exact-entry
validation and cancellation, local reset/reopen, control synchronization, wheel
routing, private node copy/paste, local graph history and unchanged real-mixer
state. Canvas examples stay disabled until explicitly entered. Final verification
uses detached WPF fixtures rather than desktop input or shown-window automation;
native pointer/focus behavior and screen-reader use remain separate checks.
No native audio code or engine/runtime binary changed for this update.

The tooltip simplification on 20 October 2026 passed **9,945 detached desktop
checks / 189 renders / zero binding errors**:
`artifacts/tooltip-purpose-20261020/ui-final-2/results.json`.
The real dial, EQ/band, Voice Color and meter tooltip renders were inspected.
Hover text describes purpose, settings and diagnostics; gesture references and
accessible keyboard help remain available. No native processing changed.
The interface update is running; the same engine process, advancing audio,
configuration, previously running paths, audio settings and UI preferences were
preserved. Evidence: `artifacts/tooltip-purpose-20261020/activation.json`.

The dynamics organization candidate on 20 October 2026 passed all **17 native
offline suites** and **9,945 detached desktop checks / 189 renders / zero binding
errors**. Evidence:
`artifacts/dynamics-organization-20261020/native-final/results.json` and
`artifacts/dynamics-organization-20261020/ui-final-2/results.json`.
Checks cover dedicated Gate parity with the earlier algorithm, Static ducking
compatibility, Dynamic ratio/threshold behavior and mode transitions, trigger
isolation and missing-trigger release, self-detecting Compressor, allocation-free
processing, persistence, clipboard, reset/Undo/Redo and fixed mode-change geometry.
Expander, Gate, both Ducking modes and the self-only Compressor renders were
visually inspected. Hardware/listening checks remain separate.

The matched interface/engine package was activated at 13:29 UTC+10. The new
engine became ready in 0.39 seconds, all ten previously running paths resumed,
and the frame counter advanced. Configuration matched with only the new omitted
Ducking defaults normalized; the saved settings file was byte-for-byte unchanged.
The new interface opened and responded. Evidence:
`artifacts/dynamics-organization-20261020/activation/result.json`.

The EQ reset correction on 20 October 2026 passed **9,701 detached desktop
checks / 182 renders / zero binding errors**:
`artifacts/eq-reset-empty-20261020/ui-final/results.json`.
Both reset menus clear populated EQs to zero bands, deselect the editor and
disable band controls. Undo/Redo restores and clears the exact custom bands;
empty resets do not add edits. Node reset also clears stale maximum-band help.
Saved/legacy EQ compatibility and Tone/filter behavior remain covered.
Interface-only activation preserved the engine, configuration, running paths
and personal files: `artifacts/eq-reset-empty-20261020/activation.json`.

The empty-EQ/centred-text candidate on 20 October 2026 passed **9,692 detached
desktop checks / 182 renders / zero binding errors**:
`artifacts/eq-empty-centred-20261020/ui-final/results.json`.
Coverage includes new zero-band creation, persistence, legacy/saved-band retention,
first-band controls, clipboard, Undo/Redo and both unchanged reset commands.
Actual rendered glyph bounds verify centred hints/captions clear of both axis
gutters at 320/540 DIP widths and 96/144/192 DPI, including narrow multiselect
ellipsis, measured spectrum labels and high contrast. Full-node empty/first-band
renders were inspected. No native processing changed or hardware checks ran.
Interface-only activation retained the original audio engine, advancing frames,
running paths, configuration and personal files:
`artifacts/eq-empty-centred-20261020/activation.json`.

The output-availability candidate on 20 October 2026 passed **9,507 detached
desktop checks / 174 renders / zero binding errors**:
`artifacts/output-availability-20261020/ui-final-3/results.json`.
Physical and virtual output warning cases cover missing/disabled/unplugged
devices, healthy mute and silence, muted failure, connection attempts, stale
inventory versus running playback, VAC Lite absence, reduced animation, high
contrast, offline help, stable geometry, and unchanged graph/history/edit queues.
The output warning renders were visually inspected beside the existing inputs.
No native processing changed; no new live audio or hardware checks were run.
Interface-only activation retained the original engine process, previously
running paths, advancing audio frames, configuration, settings and UI preferences.
Evidence: `artifacts/output-availability-20261020/activation.json`.

Use PowerShell 7 on Windows 11 x64. On a fresh checkout, run
`scripts/build.ps1` first to obtain the project-local tools, then run
`scripts/verify-offline.ps1`. This builds and runs 17 native suites and the detached
desktop checks. Use `-NativeOnly` for the native portion or `-SkipBuild` only when
the matching verification binaries have already been built. Reports and renders
are written to a new directory under `artifacts/`.

The initial publication review on 19 October 2026 passed all 14 native suites and
**9,139 desktop checks / 166 renders / zero binding errors**. The native
qualification suite includes **7,914 assertions** and the engine lifecycle suite
checks relative settings and logging without opening audio endpoints. Details
and evidence locations are recorded in [PUBLICATION_REVIEW.md](../PUBLICATION_REVIEW.md).

Historical evidence paths below refer to local, excluded build/render outputs.
They document prior checks; they are not downloadable release artifacts. Current
checks are reproducible with the command above. Hardware and audible checks
remain separate and incomplete.

The node workspace supersedes the fixed console. The original detailed procedure
is preserved in [V1_TESTING_HISTORY.md](../V1_TESTING_HISTORY.md) for reference.
The current graph supports up to 32 nodes and 96 cables; the old fixed slot counts
are no longer acceptance requirements.

The current Add/search library has 24 types. Gain / Multiply and Reroute remain
supported only for existing graphs, giving 26 compatible engine types. Effects,
Balance and Mono have no generic Level row. See [the workspace guide](../NODE_WORKSPACE.md),
[effect research](../EFFECTS_RESEARCH.md) and [the measured EQ analyzer](../EQ_SPECTRUM_ANALYZER.md)
before qualifying the expanded effects library.

### Device dropdown cleanup and combined EQ verification

The combined candidate passed **8,178 detached desktop checks**, generated
**159 renders** and reported **zero binding errors** on 19 October 2026:
`artifacts/device-visibility-20261019/ui-final/results.json`.
All **14 offline native suites** also passed:
`artifacts/device-visibility-20261019/native/results.json`.
The fake Windows catalog suite includes 44 checks for metadata retention,
form-factor validity, serialization and catalog failure handling.

Device checks cover all visibility combinations, legacy preference loading,
offline settings bindings, current hidden/unavailable selections, exact/stable
identity resolution, ambiguous stable IDs, duplicate friendly names, unknown
classification, transport compatibility, stale selection rejection and list
caching across meter refreshes. Real detached node controls verify immediate
settings refresh and unchanged graph/history/engine edit queues.

The other chat's EQ multiselect code was independently reviewed and included
in this build. Six additional integration checks exercise the actual primary
band dial callbacks while several bands are selected, including unchanged other
bands, selection retention and Undo/Redo. Group and selection-box renders were
inspected. Native pointer dispatch, popup scrolling and audible results remain
for the user's running-app test. No computer use was performed.

See [device visibility](../DEVICE_VISIBILITY.md) for defaults and supported
advanced categories. Local activation backs up both executables, preserves
personal settings, and verifies prior running paths recover before opening the
updated desktop. Activation evidence is recorded separately from these tests.

### EQ band group selection and movement

The EQ update passed 8,099 detached desktop checks, produced 158 renders and
reported zero binding errors:
`artifacts/eq-multiselection-20261019/ui-final/results.json`.

Ctrl-click toggles band selection; dragging empty plot space draws a selection
box, with Ctrl to add and Escape to cancel. Ctrl+A selects every band while the
plot is focused. Drag a selected point or use the arrows to move the group;
Shift makes movement finer. Home resets selected positions and Delete removes
selected bands. The dials below still edit the active band only.

Detached checks cover shared logarithmic frequency motion, gain offsets, common
limits and immediate reversal, mixed filter types, selection surviving owner
refresh, atomic edits and one-step Undo/Redo. Group, box and scrolled help renders
were inspected. Native pointer dispatch and the feel of selection at different
canvas zoom levels remain for running-app review. The build is staged separately
from the running desktop and engine.

### Minimal cable hover

The route-only hover batch passed 8,036 detached desktop checks, produced 155
renders and reported zero binding errors:
`artifacts/cable-hover-simple-20261019/ui-final/results.json`.
Hover contains only source → destination for every cable type. Connection
state, saved level, role descriptions and repeated gesture instructions are
absent from hover. Selection and the context menu retain their detailed controls.

### Cable tooltip bounds

The fix passed 8,032 detached desktop checks, produced 155 renders and reported
zero binding errors: `artifacts/cable-hover-fix-20261019/ui-final-3/results.json`.

Hover along cables after moving nodes above or left of the graph's original
position. The details should remain available while hovering, including after
small pointer movements along the same cable. Check at different zoom and
display scales. Empty canvas inside a cable's rectangular extent must remain
empty canvas for pointer input.

The invisible cable path uses local positive geometry translated back to its
exact world position. Detached regression checks inspect the actual arranged
owner bounds and stroke at negative, positive and origin-crossing positions.
Native popup lifetime remains for the user's running-app review.

### Numeric entry and cable details

The batch passed 6,800 detached desktop checks, produced 155 renders and reported
zero binding errors: `artifacts/keyboard-cables-20261019/ui-final/results.json`.

Type an exact dial value, then use Tab or Shift+Tab. Valid values should apply
once before moving focus; invalid text should remain editable with a red outline
and an accessible error. Escape cancels. Verify one Undo restores the previous
value, Redo restores the typed value, and leaving the field adds no second edit.

Hover audio, linked-control and sidechain cables. The tooltip should show only
**source → destination**, with no repeated instructions, state or level lines.
Rename an endpoint and check that the same cable updates its route text. Changing
gain or mute should retain the same route-only hover. Selecting a cable still
shows its type, level and state in the workspace message. Inspection must leave
the graph unchanged. For a nonzero saved cable
level, its context menu offers **Reset connection level to 0 dB**. The reset must
preserve the route and on/off state, and Undo must restore the exact old level.

Detached checks cover key handling and graph edits, tooltip reuse, menu actions,
offline editing and stale queued actions. Actual native focus traversal remains
part of the user's running-app review.

### Denoiser defaults and cable sockets

The socket/default batch passed 6,710 detached desktop checks, generated 152
renders and reported zero binding errors:
`artifacts/socket-refinement-20261019/ui-final-3/results.json`.

New denoiser nodes should open with RNNoise and 100% Mix. Saved and pasted
nodes retain their selected algorithm; legacy nodes without a saved selection
keep SpeexDSP. Reset keeps that choice and restores both processors' controls.

Audio inputs and outputs now use matching circles centred on the node border.
Control sockets remain rounded squares and sidechain sockets remain diamonds.
Unconnected sockets have coloured outlines; connected sockets are filled.
Hover, keyboard focus and connection previews brighten the existing outline.
Check connecting, disconnecting, multiple outgoing cables and Undo at different
zoom levels. The invisible targets and snapping radius are unchanged.

`SocketPresentationVerification` checks measured glyph, border and cable alignment,
detached hit geometry, state transitions and screen-space snapping. It also checks
real 120/144/192-DPI layout at 30–200% canvas zoom, negative/fractional node
positions, and cable motion before the next layout pass. Native input dispatch
and the visual feel remain for user review.

### Selection framing and shortcut reference

The navigation/help batch passed 5,138 detached desktop checks, generated 146
renders and reported zero binding errors:
`artifacts/navigation-help-20261019/ui-final/results.json`.
`FrameSelectionVerification` checks selected rendered bounds, negative positions,
zoom limits, empty/disappeared selections, menu dispatch, focus and gesture guards,
and unchanged routing/save/history state. `WorkspaceHelpVerification` checks the
Settings action, F1/Escape/Close, text-entry protection, keyboard containment,
offline availability and scrolling at the minimum window size.

Detached renders of the reference at 1280×800 and 900×580, plus a selected group,
were inspected. No native test window, desktop input or live-audio test was used.
Live keyboard focus restoration and the overall feel remain for user review.

Before combining this batch with the other chat's denoiser implementation, all
14 offline native suites were independently rerun from its paired native build:
`artifacts/navigation-help-20261019/native-independent/results.json`. All passed.
The RNNoise checks include 22 assertions covering default compatibility, settings
round trips, noise reduction, stereo isolation, 30 ms wet/dry alignment, mixing,
bypass, switching/state continuity and real-time allocation guards. Synthetic
humming remained finite; perceived voice quality and tonal artifacts still need
the user's listening test. The two chats produced the identical desktop binary,
so its 5,138-check report covers the denoiser controls and navigation additions
together.

Current implementation and activation evidence is in [design-qa.md](../../design-qa.md).
The hardware qualification record is
[TEST_RESULTS_2026-10-17.md](../TEST_RESULTS_2026-10-17.md). Additional offline
coverage and short real-driver observations passed. The eight-hour silent
endurance run was **DEFERRED / stopped at the user's request** on 17 October at
21:04:00 UTC+10, after approximately 29 minutes of process runtime. It is
neither a test failure nor an endurance pass. Full release qualification remains open.

Keep implementation checks separate from real audio qualification. Compilation,
synthetic sample processing and an inert editor preview do not prove audible
routing, latency or recovery on the user's devices. Record **Passed**, **Failed**,
**Blocked**, **Deferred** or **Not run**, with evidence. Missing hardware never
counts as a pass.

The shared-dial and dedicated-dynamics redesign is verified with synthetic audio
and isolated preview graphs. Do not use the personal live graph or send test
audio to physical or virtual outputs for these checks. Historical live-session
records below describe older builds; they do not authorize or establish testing,
activation or hardware qualification of this redesign.

## Work that can run silently

### Complete offline maintenance suite

```powershell
.\scripts\verify-offline.ps1
```

This builds and runs an explicit allow-list of fourteen native tests, then publishes
a separate verification controller and renders detached WPF layouts. It uses
synthetic audio, temporary configuration fixtures, mock endpoint interfaces and
random private named pipes. It does not open audio endpoints, show windows,
control the desktop, contact the personal engine, or edit personal settings.
Results and images are retained in a new `artifacts/offline-verification-*`
directory. Use `-NativeOnly` to skip desktop checks; use `-SkipBuild` only after
building the current native tests and `artifacts/AudioMixer-Verification` desktop.

The Denoiser now has an **Algorithm** dropdown for **SpeexDSP** and **RNNoise**.
`AudioMixer.DenoiserTests` verifies the embedded RNNoise model, reduction of
seeded noise, independent stereo, measured 30 ms wet/dry alignment, Mix, saved
algorithm compatibility, invalid values, state copying, rapid switching and
allocation-free processing/recovery. The existing Speex checks retain their
20 ms expectations. Detached UI fixtures exercise both dropdown selections,
the relevant dial, stable geometry, Undo/Redo, clipboard compatibility and Reset.
Actual speech, fan/keyboard noise and sustained humming/singing still require
matched listening comparisons with Shure's hardware denoiser disabled. Synthetic
tests do not establish freedom from audible artifacts.

`AudioMixer.ExclusiveRenderTests` exercises the direct virtual-output opening
sequence with fake WASAPI clients: supported formats, valid-bit handling, buffer
alignment retry, bounded failures, no shared fallback, and complete exclusive
buffers. It never starts a real stream. `AudioMixer.CaptureIsolationProbe` is a
separate opt-in live diagnostic requiring an exact process ID and virtual capture
endpoint. It observes existing audio, renders nothing, and writes only aggregate
statistics. It distinguishes actual silence from no packets and insufficient
input. See [the stream-isolation results](../APP_AUDIO.md#microphone-audio-in-discord-screen-shares).

The suite covers graph routing and effect quality, bounded clock-drift queues,
engine start/stop and failed-start cleanup, layout edits preserving DSP history,
backup recovery when the primary settings file is absent, partial device
inventory failures, transport fragmentation/cancellation/reconnects, independent
configuration copies, selector caching, dials, tooltips and EQ spectrum recovery.
Performance measurements describe processing work and allocation, not audible
end-to-end latency. See [the maintenance report](../MAINTENANCE_REVIEW.md).

### Peak readings and clipping

Detached meter fixtures check independent retained peaks, full-scale versus
overload, silence, reset, invalid samples, mute, channel changes and reduced
animation. Presentation fixtures exercise the informational meter and tooltip,
fresh telemetry versus repeated display updates, source reassignment and
acknowledgement, linked mute, unchanged node geometry and accessible read-only
semantics without an Invoke action or reset instructions. These checks use
in-memory controls without desktop input or live audio.

Clipping-cap fixtures advance a deterministic clock through the two-second hold
and one-second fade, including silence, stopped telemetry, renewed clipping,
repeated old samples and reduced animation. Retained hover readings are checked
independently from the temporary cap indication.

The clip-fade candidate passed 4,999 checks with 141 renders and zero binding
errors on 19 October 2026. Results are retained in
`artifacts/clip-fade-20261019/ui-final/results.json`.

The peak-meter candidate passed 4,862 checks with 139 renders and zero binding
errors on 19 October 2026. Results are retained in
`artifacts/peak-meter-20261019/ui-final/results.json`.

### Settings engine restart

The detached restart fixtures use injected lifecycle and protocol operations to
exercise the production restart coordinator. They cover saving queued edits,
offline recovery, rejected saves, lost configure replies, advancing-frame
readiness, duplicate requests, serialization with device retry and cancellation
when quitting. Settings fixtures invoke the detached menu handler and verify its
progress/result feedback, enabled states, preferences and undo preservation.
No real engine is stopped or launched by these fixtures. Process identity and
stop/start code are reviewed separately; live audio recovery remains a manual
check using **Settings → Restart audio engine** when an interruption is acceptable.

The informational-meter/restart candidate passed 4,909 checks with 140 renders
and zero binding errors on 19 October 2026. Results are retained in
`artifacts/informative-restart-20261019/ui-final/results.json`.

### Input availability warnings

The detached `--render-design` suite classifies microphone, virtual-input and
application states using synthetic inventories and path telemetry. It checks
missing, disabled, unplugged, access-blocked, busy, unassigned, reconnecting,
engine-offline and healthy-muted inputs. A Running path wins over stale device
inventory; silence and an application absent from the narrower picker do not
by themselves become missing-device warnings.

Presentation checks cover the selected source text's red pulse, the subtle outer
glow, a red warning icon centred above the meter without overlapping its bars,
unchanged selection and controls, normal and high-contrast themes, reduced
animation, fractional zoom, stable node/socket geometry and clearing the warning
after reconnection. Explicit animation-clock seeks exercise both colour phases
without desktop input or waiting for physical display frames. Checks also verify
that status changes do not create graph edits or
Undo history. The fixtures use no desktop observation, computer input, live
device switching or personal settings. Actual unplug/replug behaviour remains
a separate hands-on hardware check.

### Shared dials and dedicated dynamics

Use a separate candidate build, temporary configuration files, in-memory audio
buffers and detached `--render-design` fixtures. Record the candidate path,
results and representative before/after images. The following are required
checks, not a claim that the current candidate has already passed:

| Area | Required evidence |
|---|---|
| Catalogue | 24 creatable / 26 supported types; separate Expander, Gate, Compressor, Limiter and Ducking entries and searchable descriptions |
| Ducking isolation | Main-only, trigger-only, missing/disconnected/silent trigger, zero amount; trigger never enters output and no main-detector fallback |
| Ducking curve and time | Static fixed target, 3 dB hysteresis and 5/50 ms detector smoothing; Dynamic raw-peak ratio curve and unity below threshold; shared attack/release envelope, mode changes, stereo linking, bypass, edits and trigger changes |
| Limiter | First-sample and steady ceiling enforcement with inherited and linked gain; release and bypass; ceiling edits; accurately disclosed no-lookahead sample-peak behavior |
| Legacy compatibility | Compressor always measures its own audio; saved detector metadata remains readable but is ignored, and old Compressor trigger edges are rejected without converting nodes. Preserve legacy Compress/Limit behavior, IDs, names, positions, gains, hidden settings and bypass; loading leaves files untouched. Older Expander records in gate mode retain that mode and their settings. See the explicit compatibility decision in [IDEA-007](../IDEAS.md#idea-007-static-and-dynamic-modes-in-the-ducking-node). |
| Routing | Audio insertion with an attached trigger; typed rewiring; detector cycle rejection, including muted edges; detach refusal with detector/control connections and unchanged graph on failure |
| Persistence | Temporary save/load, copy/paste and explicit reset for dedicated dynamics kinds and both Ducking modes; absent Ducking fields default to Static/4:1; marker 0 legacy / 1 new compressor; unchanged backup conventions |
| Dials | Bounds, parameter defaults, vertical/Shift drag, exact entry, routed Enter/Escape and arrow/Page/Home keys, visible focus, accessible names and wheel-only canvas zoom |
| Precision | Fractional attack/ratio/Q, three-decimal delay, round-trip unchanged edits, rejection of fractional sample entries, integer drag/key/accessible writes |
| Layout | Label–dial–value reading order; compact related groups; distinct selectors/actions/toggles/readouts; persistent instructions; stable dimensions during telemetry; bottom Bypass and usable sockets |
| Workspace | Representative close-ups and populated graph at supported zoom; 96/144/192 DPI; minimum 900 × 580 window; no clipping, overlap or unnecessary blank space |
| Real-time boundary | No audio-loop allocation, waiting, file access or device operations; ordinary edits preserve processor histories |

`RotaryControlVerification` raises control-local keyboard/focus events using an
inert presentation source. This verifies event handling and visual state without
injecting desktop input. It does not establish physical keyboard navigation,
monitor scaling transitions, listening quality or hardware latency. Record
those separately as not run or deferred unless explicitly performed later.

### Effect quality regressions

Run `scripts/verify-effect-quality.ps1` for the dedicated silent quality probes.
It builds only offline processor tests and saves measured results to a new
artifact directory. It neither opens audio devices nor changes the running
application. Use `-SkipBuild` only when those test executables are current.

These probes cover near-cutoff attenuation, realistic band-limited consonant
bursts, post-consonant recovery, quiet/zero-effect transparency, stereo behavior,
EQ/filter response, compressor distortion, denoiser noise-versus-tone behavior,
and reverb gain/energy/decay. Ordinary finite-output and routing tests are still
required, but cannot substitute for these quality measurements. Read
[the de-esser audit](../DEESSER_QUALITY_AUDIT.md) and
[the other-effects audit](../EFFECTS_QUALITY_AUDIT.md) for findings and limits.

During later listening checks, compare the same phrase with Bypass grey and red.
Watch the Reduction indicator on De-esser/Plosive/Compressor. If it remains
**Below threshold**, lower Threshold (more negative) while speaking; Maximum
reduction alone cannot make a below-threshold signal trigger. Hover the readout
to compare actual detector level with the threshold. Keep voice natural and
check that the next quiet consonant recovers promptly. Do not treat the total
output volume as a substitute for band-specific reduction.

### While the user keeps using the computer

Use headless synthetic tests, temporary test settings and read-only source/log
inspection. Do not take desktop control, open verification windows, change real
audio routes, send requests to the personal engine's control pipe, restart it,
or replace binaries in its running package. Run any longer synthetic workload at
low priority and keep its scope explicit; an accelerated simulated duration is
not an equally long real-time soak.

The graph/DSP and stub-worker lifecycle executables below can run alongside the
personal mixer. A separate test build can add coverage without updating the live
package. Focus additional testing on uncovered behavior instead of repeatedly
running the same already-passing assertions.

The legacy `--verify-ui` WPF harness is **audio-silent but opens windows**, so defer it when the user
wants to keep uninterrupted control of the desktop. The control-protocol harness
requires the personal mixer to be quit because it uses the same per-user pipe;
defer it too. Use `build.ps1 -PackageDirectory` to build a separate candidate;
the build script rejects a destination containing a running mixer process.
The new offline suite uses private pipes and detached rendering instead.

For visual-only work, the separate `--render-design` mode creates images of the
actual WPF layout without showing windows or starting an engine. It uses in-memory
fixtures, never the personal configuration. Build into an isolated directory and
give each render a new output directory:

```powershell
$env:DOTNET_ROOT = Join-Path $PWD '.tools\dotnet'
& '.\.tools\dotnet\dotnet.exe' build desktop/AudioMixer.Desktop/AudioMixer.Desktop.csproj `
  -c Release -o build/design-review --nologo -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
$designRun = Join-Path $PWD ('artifacts/design-review-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$renderProcess = Start-Process -FilePath (Join-Path $PWD 'build/design-review/AudioMixer.exe') `
  -ArgumentList @('--render-design', '--verification-dir', ('"' + $designRun + '"')) `
  -WindowStyle Hidden -PassThru
$renderProcess.WaitForExit()
Get-Content (Join-Path $designRun 'results.json')
```

Review the PNGs as well as `results.json`. These checks cover layout, binding,
resource propagation and explicitly described in-memory control changes. The
renderer uses actual WPF visuals at 96, 144 and 192 DPI, scaled bitmap dimensions,
full EQ close-ups, bundled Inter font resolution and checks for
effects or bitmap caches that would rasterize entire nodes. This establishes
offscreen DPI behavior, not a monitor transition. It does not verify physical
mouse/keyboard gestures, native popup windows, audio routing or hardware recovery.
Use the interactive WPF harness in an agreed desktop session for those behaviors.
Current nodes always remain expanded; collapsed fixtures in older result sets
are historical evidence for the earlier interface.

Additional background coverage completed on 17 October:

1. Clock drift and resampling: 44.1/48/96 kHz conversion, independent synthetic
   clocks, correction direction and bounds, queue stability and resets. These
   classes are covered by `AudioMixer.DriftQueueTests`.
2. Settings recovery: truncated/corrupt primary files, backup recovery, unsupported
   versions and failed-save preservation, using temporary files exclusively.
3. Graph limits and generated graphs: exact node/edge limits, ordering invariance
   and malformed controls, preserving the committed state after rejection.
4. Transient DSP: compressor attack/release envelopes, extreme valid EQ settings
   and continuity during bypass/parameter changes, beyond steady-state checks.
5. Queue concurrency: simultaneous producer/consumer wraparound, overrun,
   underrun and clear requests; verify ordering and no stale replay.

Items 2–4 are covered by `AudioMixer.QualificationTests`; queue concurrency is
covered by `AudioMixer.DriftQueueTests`. Detailed results and scope limits are in
[the current report](../TEST_RESULTS_2026-10-17.md). The earlier UI/control
baseline remains in [NODE_VERIFICATION.md](../NODE_VERIFICATION.md).

### Current effects, EQ spectrum and reset coverage

`AudioMixer.AdvancedEffectsTests` runs synthetic audio through the actual native
effects: stationary-noise reduction and voiced-signal retention; isolated stereo;
measured denoiser delay; de-esser/plosive band selectivity; reverb decay, pre-delay
and stereo output; bypass/parameter transitions; inherited and reset state;
settings validation; and bounded processing/adoption costs at maximum graph size.
It checks both C++ allocations and SpeexDSP memory operations. No device worker
or real audio stream is linked into this executable.

`AudioMixer.SpectrumTests` measures known frequencies and levels, stereo power
including opposite polarity, silence, actual post-EQ boosts/cuts/bypass, 20 Hz
publication, duplicate and stalled polling, fresh-window recovery, concurrent
buffer ownership and allocation-free producer behavior. The stub engine lifecycle
suite checks serialized spectrum fields, continuity across edits, deletion/recreation
and a new continuity token after an engine restart. These tests do not record audio.

In detached preview fixtures, also check the following before any live session:

1. Add/search exposes 24 types and excludes Gain / Multiply and Reroute. A copied
   legacy graph still loads, renders, processes and copy/pastes both types without
   changing their values or connections. Mix with one input acts as volume;
   several inputs sum. Category colours and keyboard search remain consistent.
2. Effects, Balance and Mono omit generic Level. Meaningful gains remain on EQ
   bands, tone controls and compressor makeup. Load a legacy effect with nonzero
   gain: it must retain that level, disclose **Saved level**, and change only after
   the explicit context reset. Navigation and opening a context menu must not save.
3. Right-click each type and choose **Reset node to defaults**. Compare ID, name,
   position, cables and device/app identity before/after: they must be unchanged.
   Compare sound controls with a new node of that kind, including stereo/Mono,
   bypass and safe muted outputs. Repeat reset at defaults: no duplicate edit.
4. Inspect the 620-DIP-wide EQ with its 300-DIP editor at 30–200% canvas zoom and
   96/144/192 DPI. Its dimensions divided by zoom must remain fixed. The response
   should be smoothly drawn, and default bell Q must remain 1. Width presets must
   be absent; the Width / Q dial and exact entry remain usable.
5. Double-click an EQ point and use **Reset position**: return to its reset-anchor
   frequency and zero gain while preserving its Q, shape and enabled state.
   Test default, newly added and legacy bands, then save/reopen copied settings
   and repeat. Other bands and the node's connections must remain unchanged.
6. Right-click empty EQ space and choose **Reset EQ to flat**. Restore the five
   default flat bands without resetting bypass, saved node gain or routes.
   Repeat with zero bands, eight bands, a notch and a legacy pass band.
7. Feed measured-spectrum fixtures with fresh/repeated/stale frames, opposite
   stereo polarity and a changed continuity token. Geometry must follow supplied
   dBFS data independently of the response curve. Silence/unavailable/stale data
   must not retain old peaks. Spectrum updates must not save or enter History.

Record current counts and build/activation evidence in [design-qa.md](../../design-qa.md).
These checks establish implementation behavior; audible quality, physical
gestures and hardware latency remain separate qualifications.

### Historical EQ, tone and readability update evidence

The native update compiled and passed these silent checks without touching the
personal engine, audio devices or settings:

| Suite | Observed result | Log |
|---|---|---|
| GraphTests | 48,522 assertions, including zero/eight-band EQ, each tone band's boost/cut, bypass, control continuity, validation and exact JSON round trips with saved-layout preservation | Graph results |
| QualificationTests | 7,901 assertions over 4,527,360 synthetic frames | Qualification results |
| EngineLifecycleTests | 210 graph commits with concurrent snapshots, worker reuse, synthetic output loss/rejoin and shutdown | Lifecycle results |

The final readable candidate's offscreen result
contains 226 passing scoped checks, 20 actual WPF renders at 96/144/192 DPI and
zero binding errors. The Release build completed with zero warnings and errors.
Visual comparison with the official Blender reference and earlier effects view,
plus review of final effects, EQ at 144 DPI, high contrast, workspace, waiting
and empty states, found no remaining scoped blocking issue.

Continuous editing now uses a 100 ms target dispatch cadence: queuing another
edit does not restart the deadline. A synthetic two-second stream of edits every
8 ms with 33 ms polling and 70 ms fake requests dispatched batches 100–133 ms
apart. Tests also verify retained in-flight edits, final-value acknowledgement,
idle behavior, bounded retry scheduling and immediate eligibility after a slow
request. Actual request latency can extend the interval; these checks do not
measure an audible response time or operate the personal engine.

[design-qa.md](../../design-qa.md) tracks candidate review and package activation
separately. The final result does not itself establish installation. Audible,
physical mouse/keyboard and eight-hour endurance qualification remain **Deferred**.

The current update's final counts and activation record belong in
[design-qa.md](../../design-qa.md). The historical counts above do not qualify the new
connection gestures, meter zones or filter controls.

In a later agreed desktop session, verify the following with isolated preview
fixtures, preserving the personal live graph:

1. At 100% zoom, inspect bundled Inter text, 13 DIP controls and 15 DIP titles.
   Check 100%, 150% and 200% Windows display scaling, including a monitor change.
   Look for blurry text, clipped values and incorrect socket/cable positions in
   fully expanded nodes. The entire node should have no shadow effect. An old
   saved `collapsed: true` must not hide controls, and H must not fold a node.
2. Scroll the canvas through its fixed zoom stops, click the percentage and use
   Ctrl+0. Both reset actions must yield 100%; Home may fit to a smaller scale.
   Load a copied old layout and confirm positions and saved zoom stay unchanged.
3. In EQ, double-click empty plot space to add a bell at the chosen frequency
   and gain. Drag its point; Shift should refine movement without a jump. Use
   Width / Q to alter Q. There must be no Width preset submenu. Scrolling over the plot must
   zoom without editing any band. Other bands must remain
   unchanged. Pass filters allow frequency/Q but have no gain control.
4. Right-click a point and test Shape: Bell, Low shelf, High shelf and Notch;
   verify only the selected band changes. Test double-click and **Reset position**
   against the band's reset anchor and zero gain, retaining its Q and shape.
   Check enable/disable and removal, plus the selected band's Remove
   button. Exact frequency/gain/Q values stay directly visible without a
   Precise expander; validate typed entries and Enter/Escape behavior. Notch and
   pass filters omit gain. Band removal retains the EQ node and its connections.
5. Add through eight bands and confirm a ninth is refused. Remove all bands:
   response becomes flat, and adding a new point still works. Reload a copied
   five-band configuration and compare every saved band value. Right-click empty
   space and **Reset EQ to flat**: every band is cleared, with routes and
   other node controls retained. **Reset node to defaults** also clears all bands;
   Undo/Redo restores and clears their exact saved values.
6. Spawn Low / Mid / High. Adjust each −12 to +12 dB dial independently by
   dragging and exact entry; Shift must refine movement. Confirm 0 dB defaults,
   low shelf 200 Hz, bell 1 kHz and high shelf 5 kHz. The bottom Bypass toggle must retain
   all three gains, and an unrelated band/node must not change.
7. Move an EQ point or dial continuously for several seconds. Changes should
   continue reaching the engine during the gesture rather than wait for release;
   the final displayed value must match the committed value. Record this only
   during a separately agreed live session, with quiet levels and current routes.
8. Spawn High-pass filter and Low-pass filter. Confirm defaults of 80 Hz and
   12 kHz at Q 0.70710678, editable 20 Hz–20 kHz cutoff and Q 0.1–20, and the
   displayed 12 dB/octave response. Red Bypass skips processing and node level
   without losing the selected values; grey Bypass applies them.
9. Save and reopen test graphs containing zero/eight-band EQ, notch, tone and
   dedicated filter nodes; preserve gains, bypass, routing and layout. Existing
   EQ high/low-pass bands must retain their values. Distinguish these interaction
   observations from the synthetic DSP results and later audible qualification.

### Commands for an agreed verification session

1. For a full application build, use `scripts/build.ps1 -NoBootstrap` only when
   replacing the personal package is appropriate. Background checks instead
   build only diagnostic targets below, using dependencies from the initial
   build; they do not update the running package.
2. Opt in to the native offline suites after the initial build:

   ```powershell
   $env:PATH = (Join-Path $PWD '.tools\llvm-mingw-20261006-ucrt-x86_64\bin') + ';' + $env:PATH
   & '.\.tools\cmake-4.4.4-windows-x86_64\bin\cmake.exe' -S . -B build/native -DAUDIO_MIXER_OFFLINE_TESTS=ON
   & '.\.tools\cmake-4.4.4-windows-x86_64\bin\cmake.exe' --build build/native --parallel 4 `
       --target AudioMixer.GraphTests AudioMixer.EngineLifecycleTests AudioMixer.QualificationTests AudioMixer.DriftQueueTests AudioMixer.AdvancedEffectsTests AudioMixer.SpectrumTests
   & '.\build\native\AudioMixer.GraphTests.exe'
   & '.\build\native\AudioMixer.EngineLifecycleTests.exe'
   & '.\build\native\AudioMixer.QualificationTests.exe' `
       (Join-Path $PWD ('build\qualification\fixtures-' + [guid]::NewGuid().ToString('N')))
   & '.\build\native\AudioMixer.DriftQueueTests.exe'
   & '.\build\native\AudioMixer.AdvancedEffectsTests.exe'
   & '.\build\native\AudioMixer.SpectrumTests.exe'
   ```

   Check each exit code. GraphTests processes synthetic samples in memory.
   EngineLifecycleTests uses the actual engine with stub device workers; its
   executable does not link production WASAPI or process-capture workers.
   QualificationTests requires a new fixture directory and tests settings there.
   DriftQueueTests uses only synthetic clocks, queues and sample conversion.
   AdvancedEffectsTests and SpectrumTests run generated signals in memory only.
   None of these six suites opens real audio devices or reads personal settings.
3. Run the inert editor harness:

   ```powershell
   $check = Start-Process -FilePath '.\artifacts\AudioMixer\AudioMixer.exe' `
       -ArgumentList @('--verify-ui','--verification-dir','"<repository>\artifacts\graph-ui-verification\repeat"') `
       -WindowStyle Hidden -Wait -PassThru
   $check.ExitCode
   Get-Content '.\artifacts\graph-ui-verification\repeat\results.json'
   ```

   Require exit code 0 and `passed: true`. Inspect generated screenshots. This
   mode opens no control connection or real audio stream and changes no saved
   configuration or Windows settings. The node harness replaces the old
   console-specific `--verify-ui` checks.

4. Read-only application discovery can be checked separately. Discovery must
   never change application volume, playback routing or Windows defaults.
5. Review graph persistence and migration using copied files or temporary
   settings. Keep the real settings file unchanged during synthetic checks.
6. With the personal mixer explicitly quit, run the isolated control integration:

   ```powershell
   .\scripts\verify-node-engine.ps1 -EnginePath '.\artifacts\AudioMixer\AudioMixer.Engine.exe'
   ```

   It refuses to run alongside an existing engine. It creates a unique temporary
   settings path, verifies that path before edits, leaves all sources and outputs
   unassigned, checks graph commits and heartbeat, and stops only its own engine.
   It does not alter the personal configuration or open audio streams.

Core cases: all 64 legacy routing combinations, independent send levels,
source/master/control mutes, fanout, missing inputs as silence, zero-route output,
finite/clipped samples, EQ response, dynamics, stereo balance and mono, plan
changes preserving filter state, cycle/type/duplicate rejection, malformed
configuration, and the graph's resource limits.

Editor cases: spawn each type, drag and persist positions, connect by click and
drag, select/delete cables, cascade removal of nodes, typed numeric validation,
editable effects, selected EQ band and shape/reset menus, measured spectrum, Bypass toggle, always-expanded
nodes, socket snapping, cable insertion/detachment, missing-device labels,
reconnect snapshots, app identity, search keyboard navigation, pan/zoom, fit,
integrated window controls, high contrast and no binding errors.

## Opt-in real-device diagnostic helpers

These executables are built separately from the silent suites and are never run
automatically by CMake. Building them does not change the personal package:

```powershell
$env:PATH = (Join-Path $PWD '.tools\llvm-mingw-20261006-ucrt-x86_64\bin') + ';' + $env:PATH
& '.\.tools\cmake-4.4.4-windows-x86_64\bin\cmake.exe' -S . -B build/native `
    -DAUDIO_MIXER_HARDWARE_TESTS=ON
& '.\.tools\cmake-4.4.4-windows-x86_64\bin\cmake.exe' --build build/native --parallel 4 `
    --target AudioMixer.EndpointInventory AudioMixer.HardwareTests AudioMixer.LiveAudioProbe AudioMixer.ApplicationObserve
```

Do not rebuild a diagnostic executable while an existing run is using it. Both
test-build options default to OFF for a new CMake build directory. Enabling them
in an existing directory is remembered until explicitly disabled.

| Helper | Operation | Required interpretation |
|---|---|---|
| `AudioMixer.EndpointInventory` | Read-only default devices, endpoint levels and active sessions; no streams | Compare before/after snapshots. It does not verify what the user hears. |
| `AudioMixer.HardwareTests` | Explicit muted shared-mode streams with private settings and no control pipe | No generated sound; absent-ID reconfiguration is not physical unplugging. |
| `AudioMixer.LiveAudioProbe` | Explicit virtual paths; quiet stereo test signal or passive shared-capture observation | Read current virtual routes first; another application can forward the signal. |
| `AudioMixer.ApplicationObserve` | Passive capture of one exact discovered application; aggregate counters only | Silence is inconclusive if the app is not playing. No playback, microphone or route changes. |

Capture baseline and final inventories with unique filenames:

```powershell
& '.\build\native\AudioMixer.EndpointInventory.exe' > '.\artifacts\qualification\inventory-before-new-run.json'
```

Resolve exact current endpoint IDs from that inventory; do not select by a stale
friendly name. For a short muted run, supply two physical render endpoints,
base VB-CABLE capture and VAC Lite render:

```powershell
& '.\build\native\AudioMixer.HardwareTests.exe' --run-muted `
    --a1 '<current first physical render ID>' --a2 '<current second physical render ID>' `
    --vb-capture '<current VB-CABLE recording ID>' --vac-render '<current VAC playback ID>' `
    --seconds 120 --cycles 3 --output '<new absolute result.json path>'
```

The helper verifies that every node is muted before startup and every graph
commit. Physical microphones are unassigned. It creates its own unique settings
file beside the result; it never contacts the personal mixer. Preserve endpoint
defaults, session levels and the personal configuration. A healthy shared-mode
open is expected to coexist with ordinary playback through the selected output, but actual listening
quality remains a separate observation.

For an explicitly authorized eight-hour background run use `--seconds 28800
--cycles 0`, a new result location, and a hidden child process. Record its process
ID/start time and executable hash; later termination must target only that exact
process. The long-run diagnostic samples approximately once per minute and
stores at most 480 observations over the eight-hour duration. Inspect memory
with the retained telemetry overhead in mind. Successful completion requires
the final result, full observed duration and healthy frame/counter trends;
an interim `passed: false` is not a terminal result.

The 17 October run is stopped, with its stop record
preserved separately from the unchanged partial `results.json`, which still
ends with status `running`. Its follow-up automation,
`audio-mixer-endurance-results`, is **PAUSED**. Do not infer liveness or failure
from that stale interim result, resume automatically, or combine its partial
duration with later intervals. A future uninterrupted eight-hour qualification
requires fresh user authorization, a new run and a new evidence directory.

For virtual signal tests, passive dual VAC capture and the coordinated synthetic
process test, follow [LIVE_AUDIO_PROBE.md](../../native/tests/LIVE_AUDIO_PROBE.md).
The coordinating script creates hidden child processes, waits for capture
readiness and saves exact evidence:

```powershell
.\scripts\verify-process-capture.ps1 -RenderEndpoint '<current VB-CABLE playback ID>' `
    -ResultDirectory '<new absolute evidence directory>'
```

For an already running application, passive observation is available without
starting or changing its playback:

```powershell
& '.\build\native\AudioMixer.ApplicationObserve.exe' --run `
    --application-name 'Spotify' --seconds 10 --output '<new absolute observation.json path>'
```

Read `outcome` and the observed signal counters. `capture_observed_silent`
(exit code 6) means the stream opened but carried silence; do not label it a
full audio-quality pass or a failure solely from that outcome. The application
name must resolve unambiguously, and the helper binds its exact executable
identity. It writes statistics, never audio samples.

## Prepare a later audio session

- Record build/version, Windows build, physical devices, virtual-driver versions,
  and current Windows playback/recording/default-communications devices.
- Back up `%LOCALAPPDATA%\AudioMixer\settings.json` and its backups. Start from the
  user's current graph, never blindly restore historical revision 1 or 3.
- Verify the user has completed the planned reboot before reliability claims.
  VoiceMeeter was removed at the user's request; do not reinstall or depend on it.
- Check that base VB-CABLE and VAC Lite are active. Do not remove microphone software or other
  unrelated device software. Keep both transport sides enabled.
- Keep “Listen to this device” disabled. Use modest headphone volume and agree
  a quiet test period before any sound is generated or routed differently.
- Inspect the actual intended listening device with the user. The historical
  listening-output assignment is not proof that it is today's intended headphones.
- Confirm incoming call audio is outside any path sent back to the call.
- Measure complete end-to-end paths; do not infer latency from a queue size.

## Graph and processing qualification

| Scenario | Required result |
|---|---|
| Microphone → physical output | New nodes preserve stereo; Mono averages left/right into a centred signal; saved single-channel assignments remain intact until explicitly changed |
| Microphone → virtual output | Raw local capture receives the selected source; no other sources leak |
| Virtual input → independent destinations | Separate one-input Mix nodes control branch levels independently; legacy Gain nodes and saved cable gains/mutes retain their exact behavior |
| Running app → output | Only the selected process tree is captured; original playback behavior is understood |
| Add/Mix multiple sources | Sum matches offline expectation and clipping is indicated |
| Source fans out to two physical outputs | Both receive the same current signal independently |
| Linked control fans out | Level offset and mute affect only connected targets and preserve local settings |
| EQ bands | Zero to eight bands; frequency/type/Q/gain agree with measured response; per-band disable and whole-node bypass work |
| EQ edit while streaming | Add/remove/drag bands without persistent silence, unstable samples or disruptive discontinuity; zero bands returns flat audio |
| EQ shape and width | Bell/shelves/notch and Width / Q modify only the selected band; smooth response line agrees with the processed signal; default bell Q is 1 |
| EQ measured spectrum | Shaded underlay follows actual post-EQ energy at the right frequencies; EQ boost/cut/bypass change measurement where sound exists; silence and stale/restarted telemetry clear correctly |
| EQ reset gestures | Point double-click and Reset position restore anchor frequency/zero gain; Reset EQ to flat and Reset node to defaults clear every band; routes remain intact and Undo/Redo restores exact bands |
| High-pass / Low-pass | Adjustable cutoff/Q, 12 dB/octave response, stereo independence and node bypass match the offline results |
| Low / Mid / High | Each −12 to +12 dB dial independently controls its fixed 200 Hz shelf, 1 kHz bell or 5 kHz shelf; bypass retains values |
| Compressor | Threshold/ratio/attack/release and makeup behave as documented; stereo remains linked |
| Denoiser | Compare stationary background noise and speech retention; inspect artefacts; measure the additional 20 ms, including bypass, without changing the source's normal playback |
| De-esser / Plosive remover | Reduce the selected high/low band during bursts while retaining distant voice frequencies; threshold and maximum reduction remain usable and stereo-linked |
| Reverb | Mix, decay, damping and pre-delay affect the stereo tail; zero mix and bypass retain dry audio; reset/recovery does not replay stale sound |
| Balance/mono | Correct left/right behavior; mono averages channels |
| Insert/detach/reconnect cables live | Smooth committed changes, no crash, no old speech replay; hover and Escape do not edit the audio graph |
| Invalid/cyclic graph | Rejected and previous working graph continues |

Do not equate final sample saturation with a true-peak limiter. Test headroom with
multiple sources and EQ boosts. The EQ line is an analytical response; its shaded
underlay is a separate measured post-EQ spectrum, with its own dBFS scale. Verify
bypass comparisons at sensible equal levels. Denoiser's dry bypass remains delayed
by 20 ms to align toggles. There is no automatic delay compensation between
branches, so do not treat a direct-plus-denoised parallel mix as a clean reference.

### Stereo meters and microphone Mono — later hands-on

These checks are **Not run** on physical controls or live audio. Detached fixtures
and synthetic processing checks do not establish the audible result.

1. Use an isolated test graph with no connection to Virtual output or the current
   call. Keep current system/call routing intact. Begin with listening outputs
   disconnected; if listening is needed, use the agreed headphones at low volume.
2. Feed a known quiet stereo file through a Virtual input or app node: left-only,
   right-only, then unequal L/R levels. The two vertical lanes must respond
   independently. Follow the same signal through a processor and output node;
   compare against a raw local capture when qualifying the audio path.
3. With a stereo-capable microphone/interface, create a new microphone node:
   **Mono** starts off, and left/right remain separate. Enable Mono and verify
   both output channels contain their average, with one meter lane. A physical
   mono microphone naturally uses one lane even with Mono off. Loading an older
   selected-channel assignment must preserve it and show Mono on; explicitly
   switching Mono off returns it to stereo. Advanced channel selectors in Setup
   remain separate controls.
4. Raise and lower the signal. Peaks rise immediately; a fall holds for 100 ms,
   then releases at 24 dB per second toward the current level. Silence reaches
   zero; mute and an unavailable source clear the display immediately. Meter
   smoothing must not delay or change the audio itself.
5. Enable reduced animation: display updates occur at 4 Hz while retaining each
   channel's peak between updates. Clipping, mute and connection-state changes
   still respond immediately. Inspect the stacked full-width device, Mono, Level
   and Mute rows and the thicker meter to their left at normal and fractional zoom.
6. In a preview first, inspect silence: dim green/yellow/red zones must remain
   visible behind the fill. With a quiet known source in a later audio session,
   confirm independent L/R fill and boundaries at −12 dBFS (yellow) and −3 dBFS
   (red). Red below 0 dBFS is a headroom warning, not a clipping report. Verify
   mute clears illuminated fill while retaining the dim scale; high contrast
   must keep the levels legible. Do not increase headphone volume to test colour.

### Header, creation menu and navigation saving — later hands-on

Window dragging: try the title, connection dot, save text, warning-text area
(empty and populated), and unused toolbar space at 100%, 150% and 200% Windows
display scaling. Each should begin a normal window move without hunting for a
gap. Guide/Menu/window actions and Add/Remove/History/Fit/zoom must still click
normally. Double-click passive header space to maximise/restore, right-click
for the Windows system menu, drag from maximised to restore, and move between
monitors. Canvas drags must still select/move nodes, and the outer resize ring
must retain resizing. Native window movement remains a hands-on check; detached
checks verify caption boundaries and actual WPF input-hit surfaces only.

On an isolated preview graph, open Add with the mouse, Shift+A and F3. Check
Inputs, Outputs, Effects, Mixing and Controls, category colours against node
headers, submenus, search by node/category, no matches, Down/Enter/Escape, and
popup placement near screen edges. Check submenu checkmarks and dark scrolling
in EQ menus and History. History is session-only, read-only and capped at 100
entries; rapid parameter changes should combine rather than flood the list.

Pan, zoom, Fit, select and hover without changing the graph: the save indicator
and engine configuration revision must stay unchanged. Move a node, move/delete
a cable, or change a parameter: exactly that edit should be saved. Re-enter the
same value: no save. Check navigation survives a later telemetry/configuration
refresh, and that a real edit also saves the current view. Navigation after the
last edit is intentionally not persisted by itself.

At 900×580 and larger, inspect the flat header controls and maximise/restore;
the canvas should extend to the bottom with no action/status footer. Hover the
node title, grab margins and unused body: the ordinary arrow should remain.
Only during node dragging should it become a move cursor; text editors retain an
I-beam. Check width/height/socket positions while selecting, muting, bypassing,
changing values and switching between waiting/running/clipping states. Zoom
should uniformly scale the node; ordinary actions must not resize it.

### Cable interaction and simplified controls

These checks are **Not run** with physical input. Start with an in-memory preview
or copied test graph, not the user's current listening route. Final automated
evidence and its limits are recorded separately in [design-qa.md](../../design-qa.md).

For input-socket unplugging and cable cutting, also check:

- Drag a connected input onto empty canvas: only the chosen cable disappears
  on release. A click without movement, Escape and capture loss preserve it.
- Move that cable onto a compatible input: preserve its gain/mute and all other
  connections. Releasing onto its original input changes nothing. An invalid
  destination must not remove it. Repeat with audio and linked-control cables.
- With several cables entering one audio input, verify that the intended noodle
  is visibly identified before committing and the others remain connected.
- Hold Ctrl and right-drag across several noodles. Only the crossed cables
  highlight and are cut on release. Escape cancels; ordinary right-click still
  opens the context menu. Repeat at 30%, 100% and 200% zoom.
- During each preview, verify that the live graph stays unchanged. These are
  editor checks; any audible route changes belong to a separate quiet test.

1. At 30%, 50%, 100% and 200% zoom, drag an audio cable near a compatible socket,
   slightly outside its visible shape. Check that the intended target highlights
   and release connects to it. The existing socket outline brightens, without an
   added circle or a geometry change; cancel clears the preview. Incompatible sockets, duplicate connections and
   feedback paths must not be accepted. Hover alone must not change the graph.
2. Drag one unconnected processor over an existing audio cable. Only the proposed
   cable should highlight. Release to replace the original path with source →
   processor → destination in one edit. Confirm unrelated paths stay unchanged,
   and repeat with a high/low-pass filter. Moving away must cancel the insertion
   preview and preserve the original connections. Escape before release must
   also restore the node's starting position.
3. Alt-drag an inserted processor away; repeat with **Detach node (reconnect)**. An
   unambiguous incoming/outgoing audio path reconnects around the retained node.
   One incoming path can reconnect to multiple destinations; ambiguous multiple
   inputs must not be guessed. Check node settings remain,
   and Escape during Alt-drag restores the original position and connections.
   Repeat detach/reinsert on a node with nonzero gain and with node mute: its
   processing, gain and mute leave the path on detach and apply only once when
   reinserted. Cable levels/mutes must not compound or retain the node's mute.
   Repeat near graph capacity and with invalid candidate connections; rejection
   must leave a complete valid graph rather than a partial edit.
4. Inspect every node type: only inputs/outputs have Mute. Every intermediate
   has one bottom button labelled Bypass in both states: grey off, red on.
   Bypass skips processing and local/linked gain, preserving all values and
   cables. A bypassed linked controller contributes no gain. Load a copied
   legacy-muted intermediate: it must stay silent, explain its state, and the
   first deliberate Bypass click clears only its own mute with bypass enabled.
   Its button colour must reflect bypass independently of a legacy mute.
   Linked legacy mute and cable mute remain effective. The Incoming panel,
   per-cable send-volume UI and Precise expander must be absent. Load a copied
   graph containing nonzero cable gain and muted cables: their saved values and
   behavior must remain unchanged after moving nodes or opening the new UI.
5. Load a copied graph containing `collapsed: true`, select nodes and press H.
   All controls remain expanded and usable, with no collapse button. Zoom, Fit,
   moves, socket snapping and insertion must use the full current node bounds.

For an audible follow-up, use an isolated quiet route and a raw local recording.
Compare high/low-pass cutoff response, notch attenuation, stereo independence
and node bypass at matched levels. Document any click or silence when committing
a route or filter edit. This step remains deferred until the user agrees to
change the test audio route.

## Application-node qualification

Follow [APP_AUDIO.md](../APP_AUDIO.md), recording per application:

1. Capture Spotify while it plays; confirm browser/call audio is excluded.
2. Stop and restart the app. The node should wait, then resolve the same executable
   identity. A reused PID must never capture an unrelated program.
3. Change the app's playback endpoint. Check process capture and ordinary
   playback independently.
4. Confirm whether original playback is also audible. For a single managed
   listening path, assign the app to VB-CABLE and use the Virtual input instead.
5. Test browser multiprocess behavior and explain whether several tabs are
   included. Test app/store variants separately when executable paths differ.
6. Test silence/idle/resume, protected-content restrictions and inaccessible apps.
7. Test simultaneous shared consumers of VAC Lite: raw recorder, Discord, and a
   representative game. Keep Discord suppression/AGC out of raw mixer validation.

Capture failure is a compatibility result. Do not silently switch to whole-system
loopback or another app as a fallback.

## Independent recovery and Windows lifecycle

| Event | Acceptance |
|---|---|
| Remove either physical output | Other output and virtual microphone continue; no graph-wide restart |
| Remove all physical outputs | Virtual microphone and mixer timeline continue |
| Disconnect a microphone | Only that source becomes silent |
| Exit a selected app | Only that app node waits; other paths continue |
| Return USB / DAC / HDMI / wireless device | Saved selection remains; current live audio resumes |
| Start with saved devices absent | Graph opens, nodes wait, devices rejoin individually |
| Format/profile/default-device change | Explicit selections persist and only affected workers recover |
| Sleep/hibernate/resume | Stale audio discarded, no catch-up burst |
| Audio service restart | Independent workers recover when Windows becomes usable |
| Freeze/kill desktop | Healthy engine continues; reopening desktop reconnects |
| Kill/restart engine | Last committed graph resumes; existing game/call capture is checked |
| Reboot / optional login startup | Graph, layout, levels and assignments restore correctly |

The reference reconnection target remains two seconds from Windows reporting an
active, successfully openable endpoint. Measure Bluetooth radio/pairing time
separately. Opening a Classic Bluetooth microphone may change its playback
profile; record that rather than claiming the mixer eliminates the transport limit.

## Endurance, performance and migration

- Run eight hours of mixed source/output load and an overnight soak.
- Repeat disconnect/reconnect and app exit/relaunch cycles; inspect memory,
  handles, queue bounds, underruns and metadata logs.
- Exercise the maximum graph with EQs, compressors, denoisers and reverbs as well as independent
  devices. Record CPU and underruns rather than assuming the configured limit
  guarantees a particular PC's latency.
- Measure microphone→physical, microphone→virtual, app→physical and app→virtual
  paths separately. Report Bluetooth separately. Wired monitoring aims below
  50 ms but requires complete-path measurement.
- Run longer than 35 minutes to distinguish VAC Lite from the reminder-producing
  Trial edition.
- Migrate a copied version-1 configuration, preserving all sends, levels, mutes,
  channel selections and identities; verify its separate original backup.
- Restart after editing the version-2 graph. Reorder and remove nodes, corrupt a
  copied primary file, and verify last-good recovery.
- Confirm adding/moving/inserting/detaching nodes never creates a Windows audio endpoint
  or modifies default devices.
- Confirm the app's removal leaves separately installed transport drivers intact.

## Selection, group movement and clipboard

The published 17 October group-edit build passed **368 detached checks and 24
renders**, including existing rendering/control checks. See the
results and
controller-only activation.

Use a detached preview graph for the later hands-on UI session. These checks do
not require changing live audio or taking over the user's current desktop.

1. Left-drag empty canvas in both directions at 30%, 50%, 100% and 200% zoom,
   including after panning. Every node intersecting the box should be selected.
   Shift-drag adds nodes; Shift-click toggles a node; Ctrl+A selects all.
2. Drag a selected header. Every selected node and its cables should follow,
   while unselected nodes stay put. Release once to save the whole movement;
   Escape should restore the starting positions. Check while meters are updating.
3. Copy an EQ/compressor/control group with Ctrl+C and paste with Ctrl+V. Repeat
   using right-click Copy/Paste. Verify settings, relative spacing and internal
   audio/control cables; external cables should not reconnect to the new group.
   Pasted groups should be selected and repeated pastes should use distinct IDs.
   Repeated Ctrl+V should apply a small offset rather than exactly covering the
   preceding paste; right-click Paste uses the clicked canvas position.
4. Copy a selected microphone, output or app. If that identity is already in the
   graph, its copy should become unassigned while retaining settings/channel.
   Original nodes and assignments must remain unchanged. Test another copied
   graph without conflicts separately to confirm its assignments are retained.
5. Use Delete and right-click Delete on single nodes and groups. Their incident
   cables should disappear; unrelated nodes and cables should remain. An empty
   selection should do nothing. A selected cable should still delete by itself.
6. Edit a node name, number, dropdown, slider, EQ point and tone dial. Delete,
   Ctrl+C and Ctrl+V must stay with the focused control rather than deleting or
   duplicating its node. Test clipboard contention and unrelated clipboard data:
   the application should report the problem and keep the graph intact.
7. At the 32-node/96-connection limits, paste a group too large to fit. The whole
   operation should be rejected without partial nodes or cables. Restart the
   controller after completing preview edits to check saved positions separately.

`GraphGroupVerification` extends `--render-design` using the same selection,
movement, copy/paste and delete operations as the real handlers. It checks zoom
and pan conversion, reversed/additive rectangles, movement during telemetry,
shared grid snapping, cancellation, one-edit commits, coordinate bounds, deep
copy, internal cables, unique identities, deletion, editor keyboard guards and
strict payload/capacity validation. It uses only in-memory preview data and
string payloads: no native window, OS clipboard, input injection, engine access
or personal settings. Passing these checks does not substitute for physical
mouse/keyboard or Windows clipboard integration testing.

## Zoom and text rendering regression

The 17 October correction passed 268 detached checks and 22 renders in both
the Release and published controller. Evidence:
published results,
[before/after benchmark](../ZOOM_RENDERING_BENCHMARK.md) and
controller-only activation.
No live audio settings or engine binary changed for this correction.

In the later agreed UI session, use a copied preview graph:

1. Inspect 100%, 50% and 30% on the actual monitor. Text should remain neutral,
   without RGB speckles. At 30%, the same controls remain visible and text will
   naturally be small.
2. Scroll slowly and rapidly, including a high-resolution wheel/trackpad if
   available. Quarter-notch deltas accumulate instead of jumping a whole stop.
   Check cursor-centred zoom, pan, Fit and Ctrl+0 at 100/150/200% display scaling.
3. Move fully expanded nodes, edit EQ numbers and connect/insert/detach cables.
   Cable endpoints must follow sockets immediately and remain correct after zoom.
4. Keep telemetry active while moving the view. Change mute/gain/EQ/dials and
   confirm immediate local feedback and eventual saved values. Meter activity,
   clipping, device waiting/recovery and high contrast must still update.
5. Compare the same eight- and 24-node graphs. Record frame/input timing under
   the same system load; detached RenderTargetBitmap timing is not live FPS.

These gestures do not authorize changing the current listening configuration,
interrupting a call, or restarting the deferred endurance run.

## Dark theme, fractional zoom and window resizing

The selection/grab update passed **641 detached checks and 55 renders**, with
zero binding errors: results.
Checks cover fixed on-screen selection thickness, unchanged node proportions,
multi-selection/collapse, header grab geometry and protection of setting
controls from node dragging. Header tests use visual hit testing on the detached
tree, with hidden and non-interactive branches excluded; they do not inject
pointer events or substitute for the physical gesture checks below.

The earlier uniform-zoom published controller passed **583 detached checks and
50 renders**, with zero binding errors:
results. The checks compare node
dimensions, fonts, borders, editor visibility and socket geometry from 30–200%
zoom, plus explicit collapse, group editing, window sizes and repeatable Fit.
The single-EQ Fit case reaches approximately 75.7% with all controls visible in
the minimum-size window, regardless of starting at 30% or 100%. Visual review
of 50%/75% effects and the fitted EQ found no scoped issue. These results do not
establish physical-display sharpness or live pointer responsiveness.

The earlier dark/resizable controller, before the uniform-zoom correction,
passed **499 detached checks and 41 renders**, with no binding errors:
results.
The separate controller-only activation
confirmed a responsive window while the same engine kept advancing and saved
settings remained unchanged. Neither result establishes native resize gestures
or physical-display sharpness.

Use the detached `--render-design` path first. It builds the actual WPF controls
at 75%, 90%, 110% and 150% canvas zoom and 96/144 DPI, along with complete
nodes at 30%/50% and EQ/dials at 90%/110%. The PNG pixel dimensions follow their
requested display DPI; they are not resized screenshots. It also measures and
renders the main content at
900 × 580, 1280 × 800 and 1920 × 1080 DIPs. The accompanying `zoomEvidence` and
`windowLayouts` records distinguish canvas scale, screen text size and layout
bounds. Native windows, personal settings, engines and audio are excluded.

The following remain checks for a later agreed hands-on UI session:

1. At 100%, 150% and 200% Windows display scaling, inspect 75%, 90%, 110% and
   150% canvas zoom. Text, numeric controls, dials and EQ lines should retain
   smooth edges without coloured glyph fringes. Compare at native display
   resolution rather than enlarging a screenshot. Fractional zoom does not
   promise that every stroke falls on one physical pixel.
2. Zoom through 30%, 50%, 75%, 90%, 100%, 150% and 200% in both directions.
   Every node must keep the same expanded controls and proportions. Compare node
   dimensions and socket positions after dividing out the canvas scale; zoom
   must not reshape nodes or hide their editors. Confirm old collapsed settings
   stay inert and there is no collapse chevron or H folding action.
   Verify sockets and cables remain aligned during zoom, movement and group
   movement. Check mute, waiting, unassigned and clipping information at low
   zoom as well as 100%; it should scale with the rest of the node.
   Use Fit on a single EQ in the minimum-size window from 30% and 100%: its
   final displayed bounds should fit, and repeated Fit should remain stable.
3. Resize from 900 × 580 to the available desktop size. Use each window edge,
   corner, maximise/restore and Windows snapping. Check that the taskbar remains
   accessible, caption actions remain visible and existing nodes/zoom are kept.
   Restoring the window must not leave unreachable caption controls.
4. Move the window between displays with different DPI settings. Verify sharp
   text and correctly aligned socket hit targets after Windows changes its DPI.
   This requires a real window and is not established by detached PNGs.
5. Confirm category colours remain distinct against the dark theme and selected
   nodes remain clear from 30% to 200% zoom, including multi-selection and
   full effect nodes. The bright selection stroke should keep its on-screen
   thickness while the node contents scale normally. Enable the existing high-contrast preview to check that
   system colours replace decorative colours and controls remain legible.
6. Drag from unused node space and just above/beside the header at low and high
   zoom. The expanded grab area must move the intended node or selected group;
   Escape must still cancel. Buttons, fields, sockets, EQ plots and tone dials
   must retain their own interactions and must not accidentally move the node.
7. Open a node's device or app dropdown, or an EQ Shape context submenu without changing
   its selection. Hover its items, padding and scrollbar, then wheel in both
   directions, including at the first and last item. The list should scroll
   where possible; canvas zoom and node positions must stay unchanged. Move
   back over empty canvas, or close the dropdown, and confirm normal zoom
   resumes. Repeat at low and high zoom. Context menus must also keep wheel
   input away from canvas zoom. These checks require a later hands-on session;
   detached tests cover popup ancestry and zoom decisions without opening a
   native popup or injecting input.
8. With each node dropdown closed, hover it and wheel both ways, including after
   focusing it or selecting an option. The canvas must zoom while its selected
   option stays unchanged. Repeat over numeric text, sliders, EQ points, tone
   dials, buttons, sockets and node labels: zoom only, without parameter edits
   or new save/history entries. Setup selectors have no canvas and must still
   leave their selection unchanged on wheel input while closed. Open
   the list and confirm wheel scrolling still works; clicking an option and
   normal keyboard selection should continue to work.
9. On an isolated preview, click a numeric value and type: the entry border must
   visibly indicate focus. Single-click typing, Enter or Tab/Shift+Tab to apply, Escape to cancel
   and vertical dial dragging must work.
   Double-click the level dial or its number: it resets to 0 dB immediately,
   including while text is uncommitted or invalid. The next pointer movement
   must not continue an old drag. Repeat on Mix and legacy Gain nodes, EQ gains and tone-dial values.
   Other numeric parameters reset to their defaults: compressor threshold -18
   dB, ratio 4:1, attack 10 ms, release 120 ms, make-up 0 dB; balance 0; EQ
   frequency dial to the selected band's saved frequency anchor, and Q to 1.
   Resetting the frequency dial leaves band gain and Q intact; double-clicking
   a plot point restores that anchor and zero gain together.
   Repeating reset at the default must not send another
   edit. Use only the isolated preview for this hands-on check.

10. Check Level dials on each input, output, Mix and Control node, the EQ's three
    dials, and all numeric controls on dynamics, filters, denoising, de-essing,
    plosive reduction, reverb, Balance, Alignment Delay and Voice Color.
    Drag vertically and change Shift midway through a
    drag: sensitivity changes without a value jump. Enter a precise value,
    cancel uncommitted text, and reset. Frequency, Q and broad positive time
    ranges use logarithmic travel. Confirm .1 ms attack, fractional ratios/Q,
    and .001 ms readouts remain readable. Confirming an unchanged exact entry
    must not round saved precision. Samples must reject fractions. Tab reaches
    each face and entry with visible focus; arrows/Page Up/Down adjust and Home
    resets. Instructions remain visible without hovering.
    Switching selected EQ bands must show that band's values and reset anchor;
    zero bands disables the three dials without changing node dimensions.
    Wheel over each dial and its focused numeric entry: zoom only, no edit.
11. Hover a dial, numeric entry, cable, socket, EQ band and menu action. Help
    should have a clear heading and short separated rows, stay readable inside
    a narrow dark popup, and disappear normally when moving away. Verify at
    100% and fractional zoom and at Windows 100%, 150% and 200% display scale
    when convenient. Full device error messages must wrap without clipping.
    Detached renders cover styles/layout; native popup placement needs this
    later hands-on check.
12. For the MV7+'s hardware denoiser comparison and persistence check, follow
    [the Shure research and setup steps](../SHURE_DENOISER.md). The app's
    software Denoiser and Shure's hardware processing are separate. No Shure
    hardware setting was changed or read back during this UI update.

These visual checks do not establish OLED panel compatibility, power use or
burn-in resistance. The theme uses near-black surfaces; no monitor settings
are changed. They also do not restart the deferred endurance run.

## Suggested later prompt

> Qualify the node-based Vine Mix using TESTING.md and docs/APP_AUDIO.md.
> First inspect the current saved graph, headphones, Windows defaults and driver
> state. Preserve my current assignments. Verify offline graph/UI results, then
> guide me through quiet audible checks, EQ/dynamics, per-app capture, independent
> device recovery, latency and soak testing. Ask before anything that can change
> what I hear, restart Windows or interrupt a call. Record actual evidence and
> leave unavailable scenarios as Not run or Blocked.

## Find node batch — 19 October 2026

The combined current-source build passed 8,266 detached interface checks with
162 renders and zero binding errors. This includes the other chat's EQ band
multiselect and streamlined device-list checks. All 14 native offline suites
also passed, including the fake endpoint-catalog checks. An explicit engine
build produced exactly the installed engine; this batch refreshes only the UI.

Find node is available from Settings or Ctrl+F outside text entry. Verification
covers name/type/assigned device/app matching, duplicate names with distinct IDs,
Up/Down and Enter, actual Settings/Go/Close actions, cancellation preserving
selection and camera, stale results, active gesture/editor guards, unchanged
audio/history, stable list containers during telemetry, accessibility names and
minimum-window layout. Detached previews were inspected for results, no matches,
and the minimum app size. Native mouse/focus interaction and live listening are
left to the user; no Computer Use was performed.

Evidence: `artifacts/node-search-20261019/ui-final/results.json`,
`artifacts/node-search-20261019/native/results.json`, and
`artifacts/node-search-20261019/native-evidence/native-evidence.json`. The new
package identity replaces the stale prior identity left by the device-list
integration; that historical identity is preserved in this batch's evidence.

## Selected-node alignment batch — 19 October 2026

The final combined build passed 8,414 detached interface checks, 164 renders
and zero binding errors. The preceding candidate also passed 8,396 checks;
the final verifier reads cable curve endpoints directly. The native engine,
runtime and all 48 native source inputs still match the preceding batch's
14-suite verification; no native rebuild or suite rerun was needed here.

Right-click a selection of at least two nodes, then choose Align selected nodes
→ Left edges or Top edges. Checks cover actual menu actions, different node
sizes, negative/fractional positions, unchanged sound settings/connections/camera/
selection, rendered cable updates, a single exact Undo/Redo step, no-op choices,
stale menus/selections, active gesture guards and offline behavior. The normal
workspace gains no permanent control. Detached alignment renders were inspected;
alignment preserves the other axis and does not automatically space nodes.
No native mouse/focus interaction, live listening or Computer Use was performed.

Final evidence: `artifacts/node-alignment-20261019/ui-final-2/results.json`.
The package identity binds the exact desktop snapshot, report and binary, and
retains the previously verified combined engine, EQ multiselect, device lists,
Find node and earlier usability changes.

## Alignment spacing correction — 19 October 2026

The previous edge-only alignment could overlay nodes when their other axis
already overlapped. Both actions now reserve at least 24 world DIP between
selected node bodies, using their actual rendered width and height. Left edges
forms a vertical column; Top edges forms a horizontal row. Existing sufficient
gaps remain, spatial order is stable, and each complete arrangement is one
Undo step. This supersedes the earlier other-axis-preservation behavior.

The combined build passed 8,576 detached interface checks, 166 renders and
zero binding errors. Regression fixtures include same-row Left alignment,
same-column Top alignment, fully coincident positions, different node sizes,
legacy Collapsed flags, repeated no-op alignment, coordinate bounds, unchanged
sound/routes/selection/camera and exact single Undo/Redo. Actual rendered bounds
and cable endpoints are checked, and the completed row/column renders were
inspected. Pending-edit checks cover independent external changes and atomic
refusal when a selected node's layout identity changes before save replay.

Final evidence: `artifacts/node-alignment-spacing-20261019/ui-final/results.json`.
Native sources, engine and runtime remain unchanged against the preceding
14-suite verification. No native suites were rerun, and no Computer Use,
native focus/input, live listening or personal-setting changes were performed
by these checks. The app window will be refreshed under the user's standing
authorization while retaining the running audio engine.

## Alignment shortcuts and cable order — 19 October 2026

The combined build passed 8,962 detached interface checks, 166 renders and zero
binding errors. With the canvas focused, Alt+X makes a spaced horizontal row,
Alt+Y a spaced vertical column, and Alt+A runs Auto Align. The submenu displays
each shortcut and includes Auto Align. Auto chooses from selected origin spans,
with a row on ties, and keeps that chosen axis during pending-edit replay.

All alignment modes now order cable sources before targets, including audio,
sidechain and linked-control dependencies, muted cables and paths through
unselected nodes. Stable spatial ties keep unrelated nodes/branches predictable.
Shuffled connected selections compact from their original minimum coordinate;
already-valid order preserves sufficient existing gaps. Actual rendered sizes
provide at least 24 world DIP clearance and each action remains one Undo step.

Checks cover exact modifiers, protected editors/gestures/offline/modal states,
consumed repeated no-ops, Auto menu/current geometry, both explicit axes, queued
Auto with an externally flipped aspect ratio, scrambled/reversed chains,
branches/merges, unselected intermediates, duplicate dependencies, cycle refusal,
spacing, unchanged routing/audio/selection/camera and exact Undo/Redo. The final
scrambled five-node chain renders were inspected in both row and column order.
Physical Alt-key delivery is left to the user's app review; no Computer Use,
native input/focus or live listening was performed.

Evidence: `artifacts/node-alignment-shortcuts-20261019/ui-final/results.json`.
Native engine, runtime and all 48 native source inputs still match prior verified
14-suite evidence; native tests were not rerun for this UI-only batch.

## Fixed alignment spacing — 19 October 2026

Every alignment now normalizes selected nodes to exactly 48 world DIPs between
actual node bodies, doubling the previous 24-DIP minimum. Oversized gaps close,
small gaps open, and the existing cable order, Auto direction, shortcuts,
selection/camera/audio preservation and one-step Undo/Redo remain unchanged.

The combined build passed 9,128 detached interface checks, 166 renders and zero
binding errors. Rendered regressions cover coincident origins, 12-DIP gaps and
1200-DIP gaps in both axes, exact 48 after alignment/Redo, exact original Undo,
no repeated-alignment drift, and existing routing/gesture/bounds/replay guards.
The final horizontal chain render was inspected. No new native verification was
needed: the engine and native inputs remain bound to the prior 14-suite evidence.
No Computer Use or native input/focus was performed.

Evidence: `artifacts/node-alignment-fixed-gap-20261019/ui-final/results.json`.

## Lower audio latency — 20 October 2026

All 17 native suites passed after the final engine review. The complete offline
run also passed 9,139 detached WPF checks, produced 166 renders and reported zero
binding errors. Verification used synthetic audio, fake COM clients, temporary
settings and detached UI; no real endpoints or production control pipe opened.

New regression coverage:

- SharedAudioTests: 981 assertions for shortest legal shared periods, unchanged
  formats/categories, fresh-client default/legacy fallback, malformed drivers,
  COM ownership and bounded runtime stability downgrade.
- AudioTimingTests: 3,093,622 assertions with production sinc conversion, capture
  pre-roll and queue policy; 48 asynchronous capture/playback scenarios at
  44.1/48/96 kHz, approximately 1/5/10/20 ms periods, independent phases and
  +/-750 ppm clocks. Includes large/batched render demands, a producer stall,
  adaptive capture reserve, partial reads, discontinuities and stale queues.
- MixClockTests: 20,016 deterministic cadence, short-miss, bounded catch-up,
  sustained overload and resume assertions.

In the representative synthetic 48 kHz / 128-frame-period comparison, mean
combined application queue occupancy was 23.09 ms versus 30.16 ms with the
previous 720-frame queue targets under matched clocks and conversion. This
roughly 7.07 ms difference is a queue-model result; it excludes device, effect,
filter-history and acoustic delay and is not measured hardware improvement.
Existing denoiser, sample-conversion and effect quality checks passed unchanged.

Evidence: `artifacts/latency-verification-20261020/results.json` (native and UI)
and `artifacts/latency-verification-final-20261020/results.json` (final native
recheck). Reproduce with `scripts/verify-offline.ps1`.

Actual microphone-to-headphone latency, listening under load, driver-specific
minimum-period reliability, hotplug and endurance remain manual qualifications.
The separate candidate package does not replace or restart the running app.
See [audio latency](../AUDIO_LATENCY.md) for policy, diagnostics and primary
Windows API references.


## Vine Mix rebrand - 20 October 2026

The staged Release package built without desktop warnings or errors. All 17
native offline suites passed (`artifacts/rebrand-20261020-native/results.json`).
Detached WPF passed **10,012 checks**, produced **189 renders** and reported
**zero binding errors**. The shown-window harness passed **43 checks**, also
with zero binding errors. Final UI report paths and installed identity are in
[the implementation record](../BRANDING_IMPLEMENTATION_PLAN.md).

`BrandingVerification` inspects real packaged Plex Sans Regular/Medium/SemiBold
and Mono Regular without simulated styles, numeric glyph alignment, real
node/menu/tooltip fonts, EQ/Voice Color drawing at 96/144/192 DPI, independent
vector pieces, all nine ICO sizes, contrast and high-contrast restoration. It
checks green audio/lavender sidechain roles and the actual unchecked-checkbox
outline. Caption checks include the mark. Existing full-suite coverage for
routing, editing/history, all creatable nodes, controls, search/help, unavailable
devices, zoom and window sizes passed. Sparse text validation checks contrasting
ink when the antialias palette is small.

Computer Use exercised actual preview Fit, Settings, Ctrl+F/filtering, Escape,
F1, maximise/restore and close, then inspected the installed workspace and
Settings popup. Activation evidence records the verified UI hash, responsive UI,
unchanged engine PID/binary and advancing frames. Configuration, revision, exact
settings bytes and device path states were unchanged. The candidate engine is
byte-identical to the running engine.

No real audio test signal, device changes, audible measurement, screen-reader
speech check, physical monitor transition or hardware soak was performed. The
rebrand preserves functionality within these measured bounds.

## Caption and Settings help - 20 October 2026

The final Release UI passed **10,051 detached checks**, **189 renders** and
**77 shown-window checks**, with zero binding errors. Evidence:
`artifacts/caption-menu-20261020/ui-final-2/results.json` and
`artifacts/caption-menu-20261020/native-ui-final/results.json`.

Caption checks exercise the actual website-button handler with an intercepted
external opener, requiring exactly `https://vinemix.app`, and retain native
dragging/resize checks at each display scale. The mark is 20 x 20 DIPs with inset
button bounds. The shared menu tick is a drawn path, independent of font glyphs.

`MenuHoverHelpVerification` opens the real Settings/submenu presentation surfaces
on an inert preview. It checks focus, shared popup ownership, adjacent structured
and string help, modifier/F10 chord processing, dismissal/reopening, ordinary-key
passthrough, submenu/root cleanup and cancellation of a pending timer. Graph,
preferences, save status, dispatches and history must remain unchanged. It does
not inject native input or connect to the engine.

Computer Use additionally verified real pointer transitions in both directions,
submenu children, returning to the parent menu, disabled rows and closure. The
installed UI was checked for its inset mark, drawn tick, adjacent-row handoff,
keyboard navigation and actual Ctrl+Shift+F10 dismissal/reopening. Activation
preserved the engine process/binary, advancing frames, committed configuration,
revision 5983 and exact settings bytes. This follow-up changes only the UI;
native audio suites were not rerun. Activation evidence:
`artifacts/caption-menu-20261020-activation/result.json`.

## Settings separator close regression - 20 October 2026

The new regression fails against the previous cleanup order: it observes tooltip
content or placement being cleared while the native presentation source still
exists and before `ToolTip.Closed`. It hits the real separator geometry, checks
the no-owner dismissal path, rapid close/reopen, eventual reference release and
pending-help cancellation. The corrected UI passes **90 shown-window checks**
and **10,051 detached checks**, with **189 renders** and zero binding errors.
Evidence: `artifacts/tooltip-separator-20261020/regression-before/results.json`,
`artifacts/tooltip-separator-20261020/native-ui-final/results.json` and
`artifacts/tooltip-separator-20261020/ui-final/results.json`.

The logo's dedicated template has no underline; a separate keyboard-only focus
outline remains. Existing caption, branding and website-handler checks pass.
Validation used source review and application-owned WPF tests without desktop
input injection or Computer Use tools. No visual hover test was performed.
Activation preserved engine PID/binary, advancing frames, configuration, revision
5986 and exact settings bytes:
`artifacts/tooltip-separator-20261020-activation/result.json`.

## Header alignment - 20 October 2026

Release passed **10,105 detached checks**, **189 renders** and **90 shown-window
checks**, with zero binding errors. New assertions cover whole-header logo
centering, equal vertical spacing, the larger left connection dot and separation
of warning/status/action regions at three window sizes, three display scales and
both empty/long warnings. Existing website activation, 14-button hit targets,
caption dragging, native resize edges and menu lifecycle checks pass.

Reports: `artifacts/header-alignment-20261020/ui-final/results.json` and
`artifacts/header-alignment-20261020/native-ui-final/results.json`. The detached
workspace image was inspected; no Computer Use or desktop input injection was
used. Activation preserved the engine process/binary, advancing frames,
configuration, revision 5986 and exact settings bytes:
`artifacts/header-alignment-20261020-activation/result.json`.
