# Implemented development ideas

Moved out of the active ideas log on **20 October 2026**, after checking current
code, documentation, and the existing implementation/activation records.
Original IDs and entry text are retained for history. Earlier proposals and open
questions below describe the discussion at that time; implementation updates
record the decisions that followed. Recorded checks were not rerun for this
cleanup, and remaining hardware/listening checks are not claimed complete.

Return to the [active ideas log](IDEAS.md).

- [IDEA-007: Static and Dynamic ducking](#idea-007-static-and-dynamic-modes-in-the-ducking-node)
- [IDEA-008: Separate Expander and Gate nodes](#idea-008-separate-expander-and-gate-nodes)
- [IDEA-009: Unavailable output warnings](#idea-009-unavailable-output-warnings-matching-inputs)
- [IDEA-010: Empty EQ default and centered text](#idea-010-empty-eq-default-and-centered-plot-text)

## IDEA-007: Static and Dynamic modes in the Ducking node

- **Captured:** 2026-10-20 (UTC+10). **Status:** Implemented and activated.
- **Source:** The maintainer requested this proposal be passed from the chat
  "Explain sidechain processing" and recorded here for future development.
  The maintainer subsequently explicitly requested implementation on 2026-10-20.
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
- **Implementation decision (2026-10-20):** The maintainer confirmed no current
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
- **Implementation update (2026-10-20):** The maintainer explicitly requested this work.
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
- **Implementation update (2026-10-20):** The maintainer requested implementation of new
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
  five-band resets. The maintainer then reported that right-click reset still restored
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
