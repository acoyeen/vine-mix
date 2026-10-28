# VineMix later ideas

Supporting backlog for the [main ideas log and priority order](IDEAS.md).
These entries preserve the remaining suggestions recovered from earlier project
discussions and retained by the maintainer's review request on **2026-10-23**.
Except where a maintainer decision is stated, the proposed details originated in
earlier assistant reviews and remain unapproved suggestions. Recording them does
not authorize implementation, dependencies, installations, or publication.

**Evidence:** Current project documentation and relevant graph declarations were
reviewed on 2026-10-23. New external feasibility research was **not performed**;
algorithm, integration, licence, cost and performance questions remain open.
Scope estimates below are qualitative, not delivery estimates. Ease of use and
everyday quality-of-life work in the main log takes priority over this backlog.
The presentation and branding follow-ups formerly in IDEA-046 moved to
[tweaks](TWEAKS.md) on 2026-10-25; their original anchor remains below.

| ID | Idea | Priority | Status |
| --- | --- | --- | --- |
| [IDEA-043](#idea-043-sound-recall-across-processing-updates) | Sound recall across processing updates | P3 — After the core workflow | Captured |
| [IDEA-034](#idea-034-stereo-and-routing-utilities) | Stereo and routing utilities | P4 — Later | Captured |
| [IDEA-035](#idea-035-creative-effect-extensions) | Creative effect extensions | P4 — Later | Captured |
| [IDEA-036](#idea-036-recording-and-replay) | Recording and replay | P4 — Later | Captured |
| [IDEA-037](#idea-037-plugins-and-external-audio) | Plugins and external audio | P4 — Later | Captured |
| [IDEA-038](#idea-038-continuous-parameter-controllers) | Continuous parameter controllers | P4 — Later | Captured |
| [IDEA-039](#idea-039-solo-audition-and-talkback) | Solo, audition and talkback | P4 — Later | Captured |
| [IDEA-040](#idea-040-automatic-parallel-path-delay-compensation) | Automatic parallel-path delay compensation | P4 — Later | Captured |
| [IDEA-041](#idea-041-true-peak-limiting) | True-peak limiting | P4 — Later | Captured |
| [IDEA-042](#idea-042-test-signal-generator) | Test-signal generator | P4 — Later | Captured |
| [IDEA-044](#idea-044-offline-draft-workspace-editing) | Offline draft workspace editing | P4 — Later | Captured |
| [IDEA-045](#idea-045-scenes-and-ab-comparison) | Scenes and A/B comparison | Parked — Way later | Parked |

<a id="idea-034"></a>

## IDEA-034: Stereo and routing utilities

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Selected scope, 2026-10-28:** The maintainer authorized Channel Swap,
  Stereo Width, Pan, Crossfade, Stereo Combine and Stereo Split for local
  implementation and an installed-app restart for testing. These six are now
  tracked in [Beta 6 development](BETA6_DEVELOPMENT.md); this authorization does
  not extend to polarity inversion, mid/side, crossovers or mix-minus. Retain
  those remaining proposals here. This is not release finalization.
- **Need and proposal:** Offer focused tools for channel and route arrangements:
  polarity inversion; channel selection, swapping and copying; stereo width;
  mid/side processing; true panning; a crossfader; frequency crossover; and guided
  mix-minus arrangements that omit a participant's own return signal.
- **Current evidence:** The [node library](NODE_WORKSPACE.md#node-library) already
  has Mix, Balance, Mono and filters. Balance attenuates the opposite existing
  channel; it is not a general channel panner. Avoid relabelling existing behavior
  or presenting simple combinations of existing nodes as missing capabilities.
- **Scope, tradeoffs and dependencies:** Split into individually justified nodes
  or templates. Define channel semantics, bypass, headroom and saved settings;
  more routing choices must remain understandable. Crossovers and mid/side need
  separate sound-quality research; mix-minus may fit a tutorial/template first.
- **Assumptions/questions:** Stereo remains the initial scope. Which everyday
  problem needs a new node rather than a documented existing arrangement?
- **Next step:** Select one concrete use case and compare the smallest dedicated
  tool with an existing-node recipe before choosing any DSP implementation.

<a id="idea-035"></a>

## IDEA-035: Creative effect extensions

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Explore repeating echo, a voice doubler, chorus, flanger,
  phaser, pitch and formant shifting, convolution/impulse responses, binaural
  positioning, and telephone/radio/megaphone presets as separate candidates.
  Distortion remains in [IDEA-002](IDEAS.md#idea-002-distortion-and-related-creative-effects).
- **Current evidence:** [Current effects](EFFECTS_RESEARCH.md) include Reverb;
  [Voice Color](VOICE_COLOR.md) already includes subtle doubling and a small room.
  [Alignment Delay](NODE_WORKSPACE.md#node-library) is an exact timing offset,
  not a repeating-echo effect. Presets may reuse existing nodes.
- **Scope, tradeoffs and dependencies:** Each effect needs its own purpose,
  controls, latency and quality budget. Convolution includes asset handling;
  adopting third-party code or models requires separate provenance review.
- **Assumptions/questions:** These are options, not a requested full effects
  suite. Is the first unmet need natural voice enhancement or expressive effects?
- **Next step:** Rank a small set of example sounds and identify which can be
  delivered by [user node presets](IDEAS.md#idea-022) before adding algorithms.

<a id="idea-036"></a>

## IDEA-036: Recording and replay

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Record from a chosen graph point, compare a recorded
  passage during setup, and later consider synchronized multitrack capture and
  a bounded replay buffer that saves recent audio on demand.
- **Current evidence:** The [current graph kinds](../native/include/am/graph.hpp)
  have no recording node. [IDEA-013](IDEAS.md#idea-013-audio-file-inputs-and-soundboard-playback)
  covers file playback; recording is a separate capability that could reuse it.
- **Scope, tradeoffs and dependencies:** Start with one explicit recording tap.
  Define destination, format, disk limits, recording indication, stop/recovery
  behavior and audio-thread isolation. Multitrack synchronization and replay
  storage are later extensions, not prerequisites for a basic recorder.
- **Assumptions/questions:** Recording starts only through an intentional user
  action; no automatic microphone retention is proposed. Should replay retain
  audio in memory or on disk, for how long, and with what visible state?
- **Next step:** Specify a short single-route recording workflow and its storage
  lifecycle; leave scene/A/B features parked under IDEA-045.

<a id="idea-037"></a>

## IDEA-037: Plugins and external audio

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Investigate VST3 hosting, network audio send/receive and
  external hardware inserts as three independent ways to extend a route.
- **Current evidence:** [Graph kinds](../native/include/am/graph.hpp) and the
  [control protocol](CONTROL_PROTOCOL.md) describe built-in nodes and local audio
  endpoints; they do not define these integrations.
- **Scope, tradeoffs and dependencies:** Large and separate investigations.
  Plugin hosting needs lifecycle, compatibility and failure isolation decisions;
  network audio needs a transport and timing contract; hardware inserts need
  explicit send/return routing and latency handling. No SDK, protocol, licence,
  dependency or compatibility promise has been selected or researched here.
- **Assumptions/questions:** Decide whether these fit the product's accessible
  mixer focus before engineering a general workstation-style integration layer.
- **Next step:** Choose one representative workflow, document its boundaries,
  and perform a dated primary-source feasibility assessment before prototyping.

<a id="idea-038"></a>

## IDEA-038: Continuous parameter controllers

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Consider multi-parameter macro knobs, a general XY
  controller, audio-envelope followers, LFO modulation, MIDI mapping and general
  global shortcuts for levels, mute and effects.
- **Current evidence:** [Linked control](NODE_WORKSPACE.md#node-library) shares
  level offsets and Voice Color has its own pad. Neither is a general mapping
  system. [IDEA-004](IDEAS.md#idea-004-user-created-tasks-for-multiple-changes)
  covers discrete tasks; [IDEA-006](IDEAS.md#idea-006-external-parameters-and-conditional-task-triggers)
  already covers external conditions/OSC-related investigation.
- **Scope, tradeoffs and dependencies:** Share parameter identification and
  conflict rules with tasks. Define ranges, smoothing, ownership, undo and missing
  targets; high-rate modulation cannot simply mean repeated full-config saves.
- **Assumptions/questions:** Start with manual macro mapping if pursued; MIDI,
  global shortcuts and automatic modulation are separate optional extensions.
- **Next step:** Specify one macro affecting two controls and its manual-override
  behavior, then assess a suitable update path before adding input protocols.

<a id="idea-039"></a>

## IDEA-039: Solo, audition and talkback

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Listen to one source or processing point while adjusting
  it, and optionally provide a deliberate talkback route to a chosen destination.
- **Current evidence:** The [workspace node library](NODE_WORKSPACE.md#node-library)
  exposes levels, mute, bypass and explicit outputs; dedicated solo, audition and
  talkback semantics are not part of that documented set.
- **Scope, tradeoffs and dependencies:** Decide whether audition changes only a
  listening output or also the outgoing mix. Preserve original state and make
  temporary routing obvious. Talkback needs explicit source, destination and
  activation/release behavior, potentially sharing future shortcut support.
- **Assumptions/questions:** Monitoring-only audition is a candidate default,
  not an approved design. How should solo interact with shared upstream nodes,
  multiple listening outputs, mute and unavailable devices?
- **Next step:** Draw one route and specify exactly what each audience hears
  during audition and after release before designing controls.

<a id="idea-040"></a>

## IDEA-040: Automatic parallel-path delay compensation

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Where parallel copies of a signal recombine, optionally
  align branches using known processing delays without manual delay arithmetic.
- **Current evidence:** The [node library](NODE_WORKSPACE.md#node-library) has
  manual Alignment Delay and fixed denoiser delay. [Audio timing diagnostics](AUDIO_LATENCY.md)
  explicitly do not establish acoustic or complete end-to-end latency.
- **Scope, tradeoffs and dependencies:** Requires trustworthy per-node timing,
  path analysis, bypass/change handling, bounded buffers and clear reporting.
  Alignment would delay faster branches; it is not a way to remove device,
  driver or acoustic delay. External timing would need separate measurement.
- **Assumptions/questions:** Initially consider only known delays inside the
  graph. Decide where alignment is desirable and how intentional Alignment Delay
  settings are distinguished from delays that should be compensated.
- **Next step:** Model one split/rejoin example and its expected sample timing;
  evaluate usefulness against manual Alignment Delay before implementation.

<a id="idea-041"></a>

## IDEA-041: True-peak limiting

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Investigate a limiting mode aimed at reconstructed
  signal peaks, separately from the existing sample-peak ceiling.
- **Current evidence:** [Current limiter behavior](NODE_WORKSPACE.md) explicitly
  has no lookahead or oversampled true-peak detection. This is a proposed new
  capability, not evidence that today's documented sample limiter is faulty.
- **Scope, tradeoffs and dependencies:** Research a suitable detection/limiting
  method, latency and CPU budget, verification material and honest output claims.
  New behavior must remain understandable alongside current Ceiling/Release.
  Coordinate any added delay with IDEA-040 and compatible recall with IDEA-043.
- **Assumptions/questions:** Whether this is a mode or separate node is open;
  no standard-compliance or downstream-device protection claim is established.
- **Next step:** Define the intended output use and measurable acceptance
  criteria, then consult primary technical sources before selecting an algorithm.

<a id="idea-042"></a>

## IDEA-042: Test-signal generator

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Provide intentionally activated tones, noise and sweeps
  to check routing and processing without requiring live speech or another app.
- **Current evidence:** The [node catalog](../desktop/VineMix.Desktop/NodeCatalog.cs)
  and [graph kinds](../native/include/am/graph.hpp) do not provide a signal source
  of this kind. Existing [calibration ideas](IDEAS.md#idea-016-guided-node-calibration-and-learn-settings)
  concern measurement-led setting suggestions, a different workflow.
- **Scope, tradeoffs and dependencies:** Define bounded level, explicit start/
  stop, a prominent active state and routing selection. Keep any sweep analysis
  separate from basic generation; do not activate test audio on load or reconnect.
- **Assumptions/questions:** Proposed first scope is a simple tone source at a
  conservative level; waveform choices, duration and maximum level are undecided.
- **Next step:** Specify the smallest route-checking flow, including where its
  audio goes and what happens when the user closes or reloads the workspace.

<a id="idea-043"></a>

## IDEA-043: Sound recall across processing updates

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P3 — After the core workflow.
- **Need and proposal:** Make future sound-changing updates deliberate: investigate
  saved processing versions, compatible preset migration, or an explicit choice
  to adopt changed sound where preserving old behavior is practical.
- **Current evidence:** The [control protocol](CONTROL_PROTOCOL.md) documents
  Voice Color retaining saved coordinates while intentionally using newer
  processing. Configuration compatibility alone therefore does not promise
  identical sound. The existing `dynamicsUiVersion` is presentation metadata.
- **Scope, tradeoffs and dependencies:** Define a policy before the next deliberate
  algorithm change; retaining old algorithms adds maintenance and validation
  work. Coordinate saved node presets with [IDEA-022](IDEAS.md#idea-022), release
  notes and migration tests. Historical changes need not be reversed.
- **Assumptions/questions:** Preserve intent and predictable recall where possible;
  bit-identical output across all future versions is not a promised requirement.
- **Next step:** Classify corrections versus optional sound changes and document
  when compatibility, migration or a clear release-note explanation is appropriate.

<a id="idea-044"></a>

## IDEA-044: Offline draft workspace editing

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P4 — Later.
- **Need and proposal:** Allow deliberate preparation of a separate workspace
  draft, with an obvious draft/live distinction and explicit application on reconnect.
- **Current evidence:** [Current reconnect behavior](NODE_WORKSPACE.md) already
  retains queued edits; the [control protocol](CONTROL_PROTOCOL.md) rejects stale
  revisions and rebases pending ID-based changes. This proposal is a separate
  draft/review workflow, not a claim that all offline work is currently lost.
- **Scope, tradeoffs and dependencies:** Draft storage, validation without live
  devices, difference review, conflict handling and a deliberate Apply action.
  Avoid silently replacing newer live settings or presenting drafts as active
  audio routes. Coordinate with user presets and sound-recall policy.
- **Assumptions/questions:** Is a separate draft mode useful enough beyond queued
  edits and reusable presets? Whether a draft can open alongside live work is open.
- **Next step:** Describe a concrete workflow current reconnection cannot serve;
  only then choose draft persistence and conflict behavior.

<a id="idea-045"></a>

## IDEA-045: Scenes and A/B comparison

- **Captured:** 2026-10-23. **Status:** Parked. **Priority:** Parked — Way later.
- **Maintainer direction:** Scenes and A/B comparison should be explored **way
  later**. Nearer-term value is in saving selected node sections as reusable
  [User Presets, IDEA-022](IDEAS.md#idea-022); these must not depend on scenes.
- **Need and possible later behavior:** Named full-workspace snapshots and two
  alternative settings or routes, with switch/copy/swap and return-to-saved
  actions. Loudness matching is a possible comparison aid, not agreed scope.
- **Current evidence:** The [configuration protocol](CONTROL_PROTOCOL.md) saves
  the graph; legacy A/B fields are retained for old-format compatibility and do
  not constitute this feature. [IDEA-003](IDEAS.md#idea-003-inline-switch-and-toggle-nodes)
  remains a simple inline switch; its A/B route-selector extension is also deferred.
- **Scope, tradeoffs and dependencies:** Large state/identity, routing, transition,
  missing-device and undo decisions. Share persistence foundations only where
  useful; do not broaden tasks into structural edits contrary to IDEA-004.
- **Open question and next step:** Revisit only after everyday usability and node
  presets are established and the maintainer separately prioritizes this work.

<a id="idea-046"></a>

## IDEA-046: Website and Windows mixer branding follow-ups

Moved on 2026-10-25 to the tweaks log:

- [TWEAK-005](TWEAKS.md#tweak-005): homepage animation and retained prototype history.
- [TWEAK-010](TWEAKS.md#tweak-010): preferred website domains.
- [TWEAK-011](TWEAKS.md#tweak-011): Windows Volume Mixer name/icon consistency.

These refine existing presentation and branding. Their original dates, decisions,
unverified assumptions and current statuses are retained there. This ID remains
reserved so existing links continue to work.
