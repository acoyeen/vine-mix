# VineMix development ideas

A working log for rough ideas, researched options, and possible future features.
Entries are proposals, not delivery commitments or instructions to implement.
Existing plans elsewhere in `docs/` remain separate unless an idea links to them.

Implemented items are removed from this active list and retained in the
[implemented ideas archive](IDEAS_IMPLEMENTED.md). IDs are never reused.
Cleanup on 20 October 2026 archived IDEA-007 through IDEA-010; their remaining
manual qualification notes are preserved there.

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
| P2 | [IDEA-022](#idea-022) | Save selected node sections as User Presets | Captured |
| P2 | [IDEA-023](#idea-023) | Named organizational groups with automatic bounds | Captured |
| P2 | [IDEA-024](#idea-024) | Guided introduction to nodes, noodles and navigation | Captured |
| P2 | [IDEA-025](#idea-025) | Why no sound? with a highlighted blocking node | Needs decision |
| P2 | [IDEA-026](#idea-026) | Complete graph keyboard and accessibility support | Captured |
| P2 | [IDEA-027](#idea-027) | Pointer-friendly precise value editing | Captured |
| P2 | [IDEA-028](#idea-028) | Navigation through crowded and oversized graphs | Captured |
| P2 | [IDEA-005](#idea-005) | Pin nodes and tasks to the top of the window | Researched |
| P2 | [IDEA-011](#idea-011) | Compact bypass and separate disable controls | Captured |
| P2 | [IDEA-030](#idea-030) | Selected-node inspector at a readable size | Captured |
| P3 | [IDEA-029](#idea-029) | Route inspection, diagnostics and useful activity history | Captured |
| P3 | [IDEA-031](#idea-031) | Task templates and favorite or recent node types | Captured |
| P3 | [IDEA-032](#idea-032) | More informative overload readings | Captured |
| P3 | [IDEA-016](#idea-016) | Guided node calibration and Learn settings | Researched |
| P3 | [IDEA-033](#idea-033) | New voice cleanup and leveling nodes | Captured |
| P3 | [IDEA-001](#idea-001) | Dedicated visualization nodes | Researched |
| P3 | [IDEA-003](#idea-003) | Inline switch and toggle nodes | Researched |
| P3 | [IDEA-004](#idea-004) | User-created tasks for multiple changes | Researched |
| P3 | [IDEA-048](#idea-048) | Developer API for third-party apps and automation | Captured |
| P3 | [IDEA-012](#idea-012) | Silence an app's direct playback while monitoring through VineMix | Researched |
| P3 | [IDEA-013](#idea-013) | Audio-file inputs and soundboard playback | Researched |
| P3 | [IDEA-002](#idea-002) | Distortion and related creative effects | Researched |
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
| P4 | [IDEA-046](IDEAS_LATER.md#idea-046) | Website and Windows mixer branding follow-ups | Captured |
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

- **Captured:** 2026-10-19. **Status:** Researched.
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
- **Open question:** Is the initial target subtle voice coloration, aggressive
  distortion, or an adjustable range covering both?
- **Next step:** Define representative sounds and quality/latency expectations
  before choosing an algorithm or dependency.

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
## IDEA-011: Compact bypass control and clear processing semantics

- **Captured:** 2026-10-20 (UTC+10). **Status:** Captured.
- **User concern:** The current effect Bypass button's appearance, position, and
  state presentation feel unsatisfactory. Another chat suggested that bypass
  still computes the effect but leaves it out of the audible result; the maintainer asked
  whether that is true.
- **Requested direction:** Replace the full-width button with a compact control,
  potentially a circular button or a switch, and reconsider its position. Neither
  the final control shape nor placement has been chosen. This is a design note,
  not a request to implement the change now.
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
- **Current UI and likely scope:**
  `desktop/VineMix.Desktop/GraphNodeView.cs` creates the full-width bottom
  button, grey during normal processing and red when bypass is on. Explore a
  compact control, potentially in the header or beside effect controls (suggested
  placements only). Make processing-active versus bypass-active unambiguous,
  including keyboard focus, accessible state, readable contrast, and a usable
  click target. Preserve saved state, undo, and legacy-mute handling.
- **Tradeoffs and open questions:** A power icon or an "Enabled" switch can imply
  suspended computation or invert today's Bypass On meaning. Choose wording and
  state cues deliberately. The later two-control proposal below gives suspension
  its own Disable action; effect history, tails, latency, and re-enable transitions
  still need decisions before implementation.
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
- **Next step:** Compare compact layouts containing both controls, choose their
  placement/state wording, and define disable/resume and latency behavior before
  implementation. Measure savings and verify smooth transitions separately.

<a id="idea-012-silence-an-apps-direct-playback-while-monitoring-through-vine-mix"></a>

<a id="idea-012"></a>
## IDEA-012: Silence an app's direct playback while monitoring through VineMix

- **Captured:** 2026-10-20 (UTC+10). **Status:** Researched.
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

<a id="idea-013"></a>
## IDEA-013: Audio-file inputs and soundboard playback

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
- **Proposed content:** Store selected nodes, their sound settings, relative
  layout and internal audio/control/sidechain connections. Reuse the existing
  box-selection and `GraphClipboard` remapping patterns. Loading a preset should
  insert an identifiable new section with fresh IDs, as a single Undo operation.
  This insertion behavior is a design assumption for review, not a confirmed
  replacement/overwrite workflow.
- **Project fit / research:** `GraphWorkspace.cs` has selection, clipboard and
  Activity controls; `GraphClipboard.cs` handles selected groups. No named preset
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

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P2.
- **Maintainer request and rationale:** With one or more nodes highlighted,
  right-click to create an organizational group. Groups resize to encompass
  their nodes, can be renamed, and offer a curated list of designed colors
  instead of a color picker.
- **Proposal:** A labelled background frame with automatic padding around member
  bounds, updating as members move or change size. It must keep cables, sockets,
  selected nodes and warning glows legible. Grouping itself does not alter sound,
  routing or node identity.
- **Project fit / research:** `GraphWorkspace.cs` already tracks node selection
  and bounds; [workspace limitations](NODE_WORKSPACE.md) identify group frames as
  future work. Local evidence only; no external research or prototype.
- **Scope, tradeoffs and dependencies:** Medium graph-metadata, rendering and
  Undo work. Decide membership edits, whole-group movement, copy/preset behavior,
  overlapping frames and lock interaction (IDEA-017). Suggested first scope has
  no nested groups; deleting a frame leaves its nodes unless explicitly requested.
  These are assumptions for review, not settled controls.
- **Later extensions:** Canvas notes and reusable subgraphs/exposed controls stay
  P4 possibilities; neither is required for the requested organizational frames.
- **Next step:** Sketch Create group, rename and the small color palette; test
  automatic bounds conceptually with mixed-size nodes before deciding nesting.

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

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P2.
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

<a id="idea-027"></a>
## IDEA-027: Pointer-friendly precise value editing

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P2.
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

<a id="idea-028"></a>
## IDEA-028: Navigation through crowded and oversized graphs

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P2.
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

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P3.
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

<a id="idea-032"></a>
## IDEA-032: More informative overload readings

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P3.
- **Origin and need:** Prior UI-review suggestion. Determine which channel and
  processing stage exceeded full scale, with a supported peak magnitude.
- **Proposal:** Improve engine telemetry and optional inspection to identify the
  first observed overloaded stage and channel, and report above-zero magnitude
  if measured. Evaluate a longer moving-peak hold only as an optional refinement.
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

## Dependencies within the priority order

The priority index above supersedes the former unranked investigation order.
Lock and quit affordances are implemented in the local candidate. Presets and organizational
groups should agree on selection/identity handling; neither requires scenes or
A/B. Review the tutorial and silence-diagnostic flows with the maintainer before
large implementation work. Pinning may start with existing controls; task pins
depend on IDEA-004. Manual tasks and timed actions should precede external triggers.
Later DSP and integration entries require their own research and qualification.

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
