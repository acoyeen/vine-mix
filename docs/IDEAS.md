# Vine Mix development ideas

A working log for rough ideas, researched options, and possible future features.
Entries are proposals, not delivery commitments or instructions to implement.
Existing plans elsewhere in `docs/` remain separate unless an idea links to them.

## How this log works

Capture the useful intent behind each idea, check it against the current project,
and research relevant options where that will help a future decision. Keep facts,
assumptions, and open questions distinct. Include dated primary-source links for
external research; explicitly mark research that is still pending.

Use IDs such as `IDEA-001` and statuses such as **Captured**, **Researched**,
**Needs decision**, **Planned**, **Implemented**, or **Parked**. Update related
entries instead of adding duplicates. A status of Planned requires an explicit
decision to pursue the idea.

## Ideas

First ideas captured and initially researched on **19 October 2026**.
Research below is a feasibility pass, not a prototype or hardware verification.
Proposed examples and implementation choices are distinguished from the maintainer's
requested behavior. Initial research did not approve implementation or a delivery
order; subsequent implementation decisions are recorded in the individual entries.

| ID | Idea | Status |
| --- | --- | --- |
| [IDEA-001](#idea-001-dedicated-visualization-nodes) | Dedicated visualization nodes | Researched |
| [IDEA-002](#idea-002-distortion-and-related-creative-effects) | Distortion and related creative effects | Researched |
| [IDEA-003](#idea-003-inline-switch-and-toggle-nodes) | Inline switch and toggle nodes | Researched |
| [IDEA-004](#idea-004-user-created-tasks-for-multiple-changes) | User-created tasks for multiple changes | Researched |
| [IDEA-005](#idea-005-pin-nodes-and-tasks-to-the-top-of-the-window) | Pin nodes and tasks to the top of the window | Researched |
| [IDEA-006](#idea-006-external-parameters-and-conditional-task-triggers) | External parameters and conditional task triggers | Researched |
| [IDEA-007](#idea-007-static-and-dynamic-modes-in-the-ducking-node) | Static and Dynamic modes in the Ducking node | Implemented |
| [IDEA-008](#idea-008-separate-expander-and-gate-nodes) | Separate Expander and Gate nodes | Implemented |
| [IDEA-009](#idea-009-unavailable-output-warnings-matching-inputs) | Unavailable output warnings matching inputs | Implemented |
| [IDEA-010](#idea-010-empty-eq-default-and-centered-plot-text) | Empty EQ default and centered plot text | Implemented |
| [IDEA-011](#idea-011-compact-bypass-control-and-clear-processing-semantics) | Compact bypass control and clear processing semantics | Captured |
| [IDEA-012](#idea-012-silence-an-apps-direct-playback-while-monitoring-through-vine-mix) | Silence an app's direct playback while monitoring through Vine Mix | Researched |

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

## IDEA-003: Inline switch and toggle nodes

- **Captured:** 2026-10-19. **Status:** Researched.
- **Original intent:** A small node placed between other nodes whose single
  button or switch turns a line on or off.
- **Proposal:** A compact Switch node with one audio input, one audio output,
  and an unmistakable On/Off control. Off silences that branch while preserving
  the connections. Tasks (IDEA-004) can set its state, and it can be pinned
  (IDEA-005). A multi-route selector is a possible extension, not a requirement.
- **Project fit and research:** Cables already retain a mute/on-off state and
  expose it through their context menu. Graph nodes also have saved mute state.
  However, today's intermediate-node UI exposes Bypass, which passes audio
  through; it would not implement the intended switch-off behavior. The existing
  audio-reactive Expander/Gate is also a different operation. See
  [workspace controls](NODE_WORKSPACE.md), `desktop/AudioMixer.Desktop/GraphModels.cs`,
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
  [node workspace](NODE_WORKSPACE.md), and `desktop/AudioMixer.Desktop/GraphModels.cs`.
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
  `desktop/AudioMixer.Desktop/GraphWorkspace.cs`, and
  `desktop/AudioMixer.Desktop/GraphModels.cs`.
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
    itself establish a generic broadcast endpoint available to Vine Mix.
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

## IDEA-007: Static and Dynamic modes in the Ducking node

- **Captured:** 2026-10-20 (UTC+10). **Status:** Implemented and activated.
- **Source:** the maintainer requested this proposal be passed from the chat
  "Explain sidechain processing" and recorded here for future development.
  the maintainer subsequently explicitly requested implementation on 2026-10-20.
- **User need and rationale:** Organize controls around lowering one signal in
  response to another, without requiring users to understand compressor detector
  terminology.
- **User's desired direction:** Group both forms of ducking in the Ducking node
  through a dropdown:
  - **Static ducking:** Apply a fixed attenuation target once the external trigger
    exceeds Threshold. Show Threshold, Duck amount, Attack, and Release.
  - **Dynamic ducking:** Use external sidechain compression; reduction increases
    with how far the trigger exceeds Threshold, according to Ratio. Show
    Threshold, Ratio, Attack, and Release.
  Both modes retain the same main-audio input/output and detector-only trigger
  connection. Both require crossing Threshold; neither should react to just any
  microphone sound. Trigger audio never joins the audible output.
- **Compressor direction:** Remove the Main audio / Sidechain detector dropdown
  from the ordinary Compressor's normal interface, focusing it on reducing its
  own louder passages using its main audio. Preserve existing saved compressor
  behavior and routing; the exact compatibility and presentation strategy remains
  unresolved. Do not silently convert existing external compressors or presets.
- **Proposed hover tooltips:**
  - Static: "Lowers the audio by a set amount when the trigger sound crosses the
    threshold."
  - Dynamic: "Lowers the audio more as the trigger sound gets louder above the
    threshold."
- **Internal evidence checked (2026-10-20):**
  [Dynamics documentation](CORE_DYNAMICS.md), `native/src/graph.cpp`, and
  `desktop/AudioMixer.Desktop/GraphNodeView.cs` describe/implement the current
  fixed-depth Ducking node and the Compressor's main/external detector choice.
  Static ducking currently smooths its detector with 5 ms rise / 50 ms fall and
  uses a fixed 3 dB hysteresis margin, then applies a separate attack/release gain
  envelope. External compression uses its existing peak/ratio curve. These are
  different algorithms; introducing a mode selector does not establish identical
  detector, timing, threshold-release, or transition behavior between them.
  No external research or new runtime/hardware verification was performed for
  this entry; these are local code/documentation findings.
- **Considerations, not user-approved requirements:** "Fixed amount" and
  "Voice responsive" might be friendlier alternate labels, and persistent help
  could complement hover tooltips. Static ducking creates consistent space for
  speech; Dynamic responds more to loud speech but may leave less space for
  quiet speech. Removing external detection from the Compressor's normal UI
  sacrifices a conventional advanced workflow, so decide whether an advanced
  route remains accessible.
- **Likely scope and dependencies:** Ducking mode selector, mode-specific controls
  and help, dynamic-mode processing/configuration representation, Compressor
  presentation, persistence and backward compatibility, and focused verification.
  Existing sidechain connections and compressor processing are useful foundations;
  reusing them requires explicit behavior and migration decisions. No dependency
  on the task or external-parameter proposals is required.
- **Open questions:** Final mode labels; detector and timing semantics in each
  mode; whether advanced external compression stays accessible; how existing
  saved external compressors are displayed and edited; retention of mode-specific
  values; and behavior during mode changes. Keep existing saved setups intact
  until a compatibility strategy is explicitly chosen.
- **Implementation decision (2026-10-20):** the maintainer confirmed no current
  compressors use sidechains and that preserving such node/cable setups is not
  a concern. Compressor now always measures its own programme signal and has no
  detector selector or trigger socket. Only Ducking accepts trigger cables.
  Older detector strings remain readable but do not select external processing.
  Static ducking is the unchanged fixed-amount algorithm. Dynamic ducking uses
  the existing peak/ratio transfer above Threshold, with no make-up gain or
  main-input fallback. Both modes share the gain envelope across changes;
  Static's detector remains warm. Amount and Ratio remain saved independently.
  The dropdown shows **Static ducking** and **Dynamic ducking**, with the proposed
  tooltips and four visible controls in each mode. Ratio defaults to 4:1.
- **Verification:** All 17 native suites and 9,945 detached desktop checks,
  189 renders, zero binding errors. Evidence:
  `artifacts/dynamics-organization-20261020/native-final/results.json` and
  `artifacts/dynamics-organization-20261020/ui-final-2/results.json`.
- **Activation (2026-10-20):** The matched engine/interface package is running.
  All previously running audio paths resumed, audio frames advanced, and the
  saved settings file was unchanged. Configuration matched after accounting for
  the newly introduced Ducking defaults. Evidence:
  `artifacts/dynamics-organization-20261020/activation/result.json`.
- **Next step:** Review real speech/music behavior. Synthetic checks do not
  establish listening quality.

## IDEA-008: Separate Expander and Gate nodes

- **Captured:** 2026-10-20 (UTC+10). **Status:** Implemented and activated.
- **User's requested direction:** Split the combined Expander / Gate node into
  two distinct nodes: **Expander** and **Gate**.
- **Rationale (inferred):** Make each processor directly discoverable and give it
  a focused interface instead of requiring a mode change within a combined node.
- **Proposal:** Offer separate Add/search entries and node identities, each
  showing its relevant controls. Based on the existing modes, Expander would show
  Threshold, Ratio, Attack, Release, and Range; Gate would show Threshold, Attack,
  Hold, Release, Range, and Hysteresis. These control lists are a suggested reuse
  of current behavior, not newly confirmed requirements.
- **Project fit and internal review (2026-10-20):**
  [Current dynamics documentation](CORE_DYNAMICS.md#expander--gate),
  `desktop/AudioMixer.Desktop/NodeCatalog.cs`, and
  `desktop/AudioMixer.Desktop/GraphModels.cs` describe one `expander` node kind
  with a saved mode and separate mode-specific settings. Existing processing
  could remain shared internally while presenting two nodes; the requested split
  does not itself require changing either audio algorithm. No external research
  or runtime verification was needed for this capture.
- **Scope and tradeoffs:** Node catalog/search, dedicated presentation and
  defaults, configuration representation, copy/paste/reset, documentation, and
  compatibility checks. Two entries improve direct access but enlarge the node
  library. Preserve existing saved nodes' settings, routing, and audible behavior;
  decide how their stored mode maps to the separate node identities.
- **Open questions:** Compatibility/migration approach; whether users can later
  convert one type to the other; and how to retain settings previously saved for
  the inactive mode. No conversion behavior has been requested or approved.
- **Implementation update (2026-10-20):** Added dedicated Expander and Gate
  creation/search entries with five and six controls respectively, without a
  mode selector. New Gate records use kind `gate` and the existing gate algorithm.
  Existing `expander` records saved in gate mode still display Gate controls and
  retain that mode on reset, without automatic identity/settings migration.
  Save/load, copying, reset and Undo/Redo retain settings and routing. No new
  node-conversion action was added.
- **Verification:** The same 17 native suites and 9,945 desktop checks above
  include dedicated/earlier Gate sample parity, bypass, history, telemetry,
  parameter validation and allocation guards. Both node renders were inspected.
- **Activation (2026-10-20):** Installed with IDEA-007; saved settings and all
  previously running paths were preserved. Evidence:
  `artifacts/dynamics-organization-20261020/activation/result.json`.
- **Next step:** Review real speech and background noise. Hardware/listening
  checks have not been performed.

## IDEA-009: Unavailable output warnings matching inputs

- **Captured:** 2026-10-20 (UTC+10). **Status:** Implemented and activated.
- **User observation and requested direction:** Missing inputs have a red
  outline/glow and flashing warning, but missing outputs do not receive the same
  treatment. Extend that availability warning to outputs as well.
- **User need:** Make a missing listening or virtual output as easy to notice as
  a missing input, so users can identify why a route cannot deliver sound.
- **Proposal:** Apply the existing input-warning treatment to assigned,
  unavailable physical and virtual outputs: red device-name pulse, faint outer
  glow, warning triangle, and a specific reason/recovery tooltip. Clear the
  warning when playback recovers. Including both output types and matching all
  existing warning details is the proposed interpretation of "outputs as well."
- **Project fit and internal review (2026-10-20):**
  [Input availability](INPUT_AVAILABILITY.md) documents the current input-only
  warning, reduced-animation behavior, accessible status, and rules that silence
  and healthy mute do not imply failure. [Device visibility](DEVICE_VISIBILITY.md)
  already retains unavailable assigned output devices in selectors.
  `desktop/AudioMixer.Desktop/GraphNodeView.cs` contains input-specific warning
  state alongside output device controls. No external research or live device
  checks were performed for this capture.
- **Suggested behavior and scope:** Reuse the visual/accessibility treatment,
  but classify output availability from playback/device state rather than capture
  state. Preserve selection outlines, node geometry, and socket positions.
  Respect reduced animation and high contrast. An assigned unavailable output
  should still warn while muted; healthy mute, silence, an unassigned output, or
  a connection attempt should not alone be classified as a missing device.
  These details carry forward the existing input conventions as suggestions.
- **Tradeoffs and open questions:** Distinguish a missing/disabled/unplugged
  device from playback startup/failure and an offline engine. Confirm which
  existing output statuses provide reliable evidence and suitable recovery text.
  A visual warning must not change assignments, routing, levels, or saved setups.
- **Implementation update (2026-10-20):** the maintainer explicitly requested this work.
  Physical and virtual outputs now share the input warning presentation: red
  outer glow, steady triangle above the meter, and a 1.6-second device-name pulse.
  Playback state and render-device identities determine the warning; running
  playback clears it even when inventory lags. Healthy mute, silence and
  connection attempts do not trigger it. Missing VAC Lite warns on an unassigned
  virtual output, matching the existing missing-VB-CABLE input behavior. Output
  access help corrects the older engine's microphone-specific access wording.
  Controls and socket positions remain fixed through warning/recovery changes;
  no audio-engine, routing or personal settings changes are part of this update.
- **Verification:** 9,507 detached desktop checks, 174 renders, zero binding
  errors; output/input warning renders inspected. Evidence:
  `artifacts/output-availability-20261020/ui-final-3/results.json`.
- **Activation:** Applied after the maintainer confirmed Saved/idle. The interface was
  replaced while the same audio-engine process continued running; configuration,
  audio settings, UI preferences and previously running paths were preserved.
  Evidence: `artifacts/output-availability-20261020/activation.json`.
- **Next step:** Manually check real output unplug/replug and audible recovery.
  Those hardware/listening checks have not been performed.

## IDEA-010: Empty EQ default and centered plot text

- **Captured:** 2026-10-20 (UTC+10). **Status:** Implemented and activated.
- **User observation:** EQ currently starts with five bands; bands 1 and 5 have
  a different Width/Q. The band information at the top overlaps/clips with the
  decibel reading on the left.
- **Requested behavior:** New EQ nodes start with **zero bands**. Center the
  "double-click to add a band" hint, and center the band information at the top
  of the EQ so it does not collide with the left decibel labels.
- **Rationale:** Start with a clean, flat EQ that users build as needed, and keep
  instructions and active-band details legible.
- **Project fit and internal review (2026-10-20):**
  [EQ documentation](NODE_WORKSPACE.md#eq-and-dynamics) already permits zero to
  eight bands and a flat response with no bands. In
  `desktop/AudioMixer.Desktop/GraphModels.cs`, `EqBand.Defaults()` creates five
  bands: low/high shelves at the ends use Q 0.70710678, while the middle bells use
  Q 1. This explains the differing defaults; the user has not requested changing
  shape-specific Q values. `desktop/AudioMixer.Desktop/Controls/EqualizerEditor.cs`
  owns the plot interaction and text rendering. The clipping is user-reported;
  no new visual reproduction or external research was performed for this note.
- **Scope and tradeoffs:** Update new-EQ defaults and plot text layout, preserve
  existing saved bands, and keep band creation discoverable in the empty state.
  Center within the usable plot area with clearance for axis labels rather than
  allowing long band details to overlap either scale. Keep Tone and dedicated
  filter defaults independent of this change.
- **Implementation update (2026-10-20):** the maintainer requested implementation of new
  zero-band EQs and centred text. New workspace EQs now start empty and flat;
  explicit empty arrays remain empty through persistence, copying and Undo/Redo.
  Saved populated bands and older settings that omit the band field keep their
  existing behavior. Tone and dedicated filter defaults are unchanged. Band
  controls become available when the first band is added.
  The top caption and empty hint are centred within the plot's usable width,
  excluding both axis gutters. The hint stays above the flat response line;
  narrow captions use one-line ellipsis with full details in accessible status.
  Alignment is part of the existing text cache, preserving redraw reuse.
- **Reset follow-up (2026-10-20):** The first creation-only update retained
  five-band resets. the maintainer then reported that right-click reset still restored
  the old defaults. Both **Reset node to defaults** and **Reset EQ to flat**
  now clear all bands, matching a new EQ. An already empty EQ does not publish
  another curve-reset edit. Undo retains the ability to restore populated bands;
  individual band-position reset behavior is unchanged.
  The correction passed 9,701 detached checks, 182 renders and zero binding
  errors: `artifacts/eq-reset-empty-20261020/ui-final/results.json`.
  It was activated with the same engine, configuration, settings and preferences
  preserved: `artifacts/eq-reset-empty-20261020/activation.json`.
- **Verification:** 9,692 detached desktop checks, 182 renders, zero binding
  errors. Actual text ink was checked at 320/540 DIP widths and 96/144/192 DPI;
  empty/first-band nodes and selected/multiselect captions were visually reviewed.
  Evidence: `artifacts/eq-empty-centred-20261020/ui-final/results.json`.
- **Activation:** Applied under the maintainer's instruction to proceed without further
  Saved/idle prompts. The same audio engine continued running, with configuration,
  settings, preferences and previously running paths preserved. Evidence:
  `artifacts/eq-empty-centred-20261020/activation.json`.
- **Next step:** Review creation and pointer interaction in the running workspace.
  No audio processing changes.

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
  `desktop/AudioMixer.Desktop/GraphNodeView.cs` creates the full-width bottom
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
- **Follow-up investigation (2026-10-20):** the maintainer asked whether fully stopping
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

## IDEA-012: Silence an app's direct playback while monitoring through Vine Mix

- **Captured:** 2026-10-20 (UTC+10). **Status:** Researched.
- **User need:** Apply EQ or other effects to a running application's audio and
  hear only the processed result. Currently the app's original playback and Vine
  Mix's listening output can both reach the same headphones, producing duplicate
  audio.
- **Requested direction:** Add a button on running-app input nodes to silence
  the application's direct local playback while retaining capture and the
  processed listening route. This differs from the node's existing Mute, which
  silences its signal inside Vine Mix. Suggested label: **Mute original playback**,
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
  cable, then capture one path and listen through Vine Mix. This is already
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
  not just the Vine Mix node, and might affect other consumers. Account for
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

## Relationships and possible investigation order

Switches and visualizers can be explored independently. Pinning can begin with
existing node controls, then add task buttons once tasks exist. A reusable task
and timed-action system should precede external condition triggers, so manual
and automatic actions use the same behavior. Distortion is an independent audio
quality investigation. This is a dependency observation, not an agreed priority.

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
