# Implemented development ideas

Moved out of the active ideas log on **20 October 2026**, after checking current
code, documentation, and the existing implementation/activation records.
Original IDs and entry text are retained for history. Earlier proposals and open
questions below describe the discussion at that time; implementation updates
record the decisions that followed. Recorded checks were not rerun for this
cleanup, and remaining hardware/listening checks are not claimed complete.

A second batch, IDEA-017 through IDEA-021, was implemented on 23 October 2026 as an unpublished local candidate. Its current verification and activation status is recorded separately; earlier release evidence is unchanged.

Return to the [active ideas log](IDEAS.md).

- [IDEA-007: Static and Dynamic ducking](#idea-007-static-and-dynamic-modes-in-the-ducking-node)
- [IDEA-008: Separate Expander and Gate nodes](#idea-008-separate-expander-and-gate-nodes)
- [IDEA-009: Unavailable output warnings](#idea-009-unavailable-output-warnings-matching-inputs)
- [IDEA-010: Empty EQ default and centered text](#idea-010-empty-eq-default-and-centered-plot-text)
- [IDEA-017: Visible layout lock](#idea-017)
- [IDEA-018: Minimise on close and Settings quit](#idea-018)
- [IDEA-019: Denoiser dropdown delay labels](#idea-019)
- [IDEA-020: Post-update What's new](#idea-020)
- [IDEA-021: Save recovery before quitting](#idea-021)
- [IDEA-023: Named organizational groups with automatic bounds](#idea-023)
- [IDEA-049: Cumulative website node story](#idea-049)

<a id="idea-023"></a>
## IDEA-023: Named organizational groups with automatic bounds

- **Release update, 2026-10-28:** Included in published
  [0.4.0 Beta](releases/0.4.0-beta.md), with the later group/color/heading
  refinements. See [final qualification](RELEASE_0.4.0_QUALIFICATION.md).

- **Captured:** 2026-10-23. **Clarified and implementation authorized:**
  2026-10-28. **Original priority:** P2. **Status:** Implemented and locally
  integrated into the installed Beta 6 app on 2026-10-28. Beta 6 remains in
  development; this does not finalize, publish or replace any release.
- **Maintainer request and rationale:** Select an area or a set of nodes, then
  right-click to group them in a named frame that makes a busy workspace easier
  to read. The 28 October clarification explicitly authorized implementation:
  groups are purely organizational, have no inputs or outputs, and do not change
  sound, routing or node identity. Moving the group moves every member together.
- **Required behavior:** Bounds automatically follow member positions and sizes.
  Names can be changed and remain readable while zoomed out. Colors come from a
  small designed palette rather than a color wheel. Frames, titles, menus and
  editing must fit the existing interface and leave nodes, cables, sockets,
  selection and warnings legible.
- **Implemented controls:** **Group selected nodes** or **Ctrl+G** creates a
  frame; its title moves the members. Double-click the title or select the frame
  and press **F2** to rename. Enter or loss of focus commits; Escape cancels.
  The palette is Sage, Teal, Blue, Violet, Rose, Amber and Slate. Titles retain
  at least 15 screen DIPs at 30% zoom. **Ungroup (keep nodes)** removes only the
  frame; Delete has that same effect when the frame is selected. Fit, Home and
  Frame selection include complete groups' headings. The first implementation
  left frame interiors transparent to ordinary canvas selection and navigation;
  the later refinement below supersedes that input choice.
- **Membership and copying decisions:** One group per node and no nested groups.
  Membership is explicit through **Add to group** and **Remove from group**;
  moving a member beyond the current frame expands the frame rather than
  removing membership. Creating a new group transfers the selected members
  from their previous groups. Empty groups disappear. Copying every member
  preserves the frame's name and color around the pasted copies with new IDs;
  partial copies contain only those nodes. These choices resolve the original
  proposal's open membership, movement, copy and nesting questions.
- **Scope, dependencies and tradeoffs:** Extends existing selection, shared node
  movement, history and clipboard paths. Group creation, names, colors,
  membership, ungrouping and movement support Undo/Redo. The layout lock blocks
  group edits while selection and copying remain available. Presentation and
  membership are saved in desktop workspace preferences, separately from the
  engine's configuration; frames never become processing objects. A one-node
  group dragged across a cable does not insert or detach its member. Local
  source inspection informed the implementation; no external feasibility
  research was needed. No additional dependency or audio-engine change was
  required.
- **Verification and integration (2026-10-28):** The final detached suite passed
  13,491 checks with 272 renders and zero binding errors. Real pointer/keyboard
  checks in an isolated preview covered box selection and right-click creation,
  double-click rename and Enter, whole-group movement, the color menu and Violet,
  empty frame-body interaction, member movement with automatic bounds, and title
  readability at 30% zoom. Guarded UI-only activation passed against the installed
  app, preserving the running engine and settings/preferences and verifying an
  actual Explorer shortcut launch. Details and limits are recorded in
  [Beta 6 development](BETA6_DEVELOPMENT.md#named-organizational-groups--28-october-2026),
  separately from published release qualification.
- **Follow-up requested 2026-10-28:** Brighter, more saturated, less transparent
  colors, selection/movement from exposed group backgrounds and shorter hover
  help are tracked in [TWEAK-017](TWEAKS.md#tweak-017). This supersedes the original
  faint tint and heading-only group interaction; modifier-assisted box selection
  remains available within frames. The overlapping-node selection defect is
  separate [ISSUE-022](KNOWN_ISSUES.md#issue-022). Both refinements are implemented
  and locally integrated; their detached checks, render review and guarded
  activation are recorded in the
  [follow-up evidence](BETA6_DEVELOPMENT.md#group-refinements-and-node-stacking--28-october-2026).
  Real-pointer checks of these changes were not completed; the original feature's
  pointer checks above do not qualify the refined interaction.
- **Later extensions:** Canvas notes and reusable subgraphs/exposed controls stay
  P4 possibilities; neither is required for organizational frames. Groups are
  not automatically saved presets (IDEA-022).
- **Next step:** Continue everyday testing in the installed Beta 6 app; qualify
  the final source and binaries together when the maintainer explicitly
  finalizes Beta 6.

<a id="idea-049"></a>
## IDEA-049: Cumulative website node story

- **Captured and implementation authorized:** 2026-10-25. **Status:** Implemented
  locally in `website-redesign`, with final local browser verification complete.
  Maintainer review remains; no public deployment or desktop release is implied.
- **Need and rationale:** Visitors should follow one evolving audio system as
  they scroll. Starting at “See where your sound goes,” the nodes stay in view
  while headings and routes adapt. The maintainer specifically requested room
  between nodes because the preceding lower ducking illustration had crossing,
  crowded cables.
- **Original request:** Begin with the simple three-node setup; insert a
  Low / Mid / High effect smoothly when the heading introduces effects, optionally
  changing its values with scroll; then introduce ducking and adapt the routing.
  The maintainer explicitly requested the cleanup first, followed by a goal to
  implement and locally host this direction.
- **Retained corrections:** The story must be cumulative: keep both the tone
  node and Listening output when ducking is added. Use actual native node artwork,
  replacing the initial compact HTML interpretation. Remove the extra Connect /
  Shape / Duck buttons, visible illustrative disclaimer and Keep exploring link;
  retain the thin progress line. Remove the unnecessary Mix node: the shared
  output combines the separate audible voice and ducked-music connections.
  A subsequent correction keeps Listening output muted and Virtual output
  unmuted, with silent simulated meters that continue moving while the scene is
  visible rather than only when the visitor scrolls.
- **Current behavior:** Three nodes become four with tone, then six with Running
  app and Ducking. Physical input → Low / Mid / High still feeds Listening output
  and Virtual output. Tone also feeds Ducking's detector, while Running app →
  Ducking feeds Virtual output separately. Nine native tone frames blend from
  zero to +3 / −2 / +2 dB. Headings change in place and reverse scrolling retraces
  the same layout. Native artwork uses neutral example devices without live audio.
  Physical input, Running app and Virtual output meter strips show smoothly
  varying synthetic levels at up to 24 fps. Listening output retains its original
  muted image without an overlay. One animation loop stops offscreen, when the
  page is inactive, or under pause, reduced motion and high contrast.
- **Scope, dependencies and tradeoffs:** Website markup, styles, native image
  assets, deterministic layout and the existing shared motion controller only.
  The approved opening remains. Small/short windows, reduced motion, high contrast
  and no JavaScript show the three chapters as readable static route diagrams.
  This sacrifices pinned motion where it would compromise legibility. The final
  headphone route remains connected but muted; the music application's original
  headphone playback continues, avoiding duplicate captured-music monitoring. No new
  application capability, driver installation or public hosting change is needed.
- **Evidence and research:** Local inspection on 2026-10-25 of the website,
  `docs/USER_GUIDE.md` and native node definitions informed routing and control
  ranges. The detector is not an audible voice path. No new external design or
  usability research was performed. Geometric checks sampled 1,001 scroll
  positions and 155,456 cable segments; browser evidence and limits belong in
  [website QA](../website-redesign/QA.md), not inferred from those code checks.
  A browser sample observed 12 distinct meter states over 1.6 seconds while
  stationary and confirmed the displayed output mute states. This verifies visual
  motion only, not live audio processing.
- **Next step:** Review the verified local result; current evidence is in
  [scroll QA](../website-redesign/SCROLL_QA.md).
  [Website README](../website-redesign/README.md) and the
  [cleanup baseline](../website-redesign/CLEANUP.md) document the implementation.
  Publication remains separate.

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
  [Current dynamics documentation](CORE_DYNAMICS.md#expander-and-gate),
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


<a id="idea-017"></a>
## IDEA-017: Visible layout lock

- **Implementation (2026-10-23):** Added the requested top-right red/closed and white/open padlock, with high-contrast shapes and accessible state. Session-only state survives hiding/reconnecting and resets unlocked after Quit. Move/add/delete/duplicate/paste/align, structural cable actions and structural or mixed Undo/Redo are blocked centrally, including stale callbacks. Selection, copy, inspection, pan/zoom, device choices, renaming and sound controls remain usable. No group feature was added.
- **Maintainer refinement (2026-10-23):** Replaced the prominent white unlocked symbol with a 16-pixel muted grey padlock matching Saved, while retaining the click target and red locked state. The shackle opens/closes briefly on toggle. Refused node/cable edits briefly highlight the padlock; ordinary selection stays quiet. Reduced motion uses an immediate state change and static emphasis. This supersedes the original white unlocked appearance below.
- **Second maintainer refinement (2026-10-23):** Locked now uses the meter's warning yellow. Refused edits smoothly turn the padlock red and enlarge it, then return it to normal. An initially requested affected-node outline became a fading yellow outer glow; the maintainer subsequently rejected that glow. The current behavior removes it and hides normal node selection outlines while locked, restoring them for the current selection on unlock. Missing-device warnings, selection state, sound controls and saved layout remain unchanged. The hover text is shorter.
- **Latest maintainer refinement (2026-10-23):** The locked padlock returns to constant red. Refused edits now change size only, with a 1.4× peak replacing the 1.25× emphasis and its colour transition. The first 400 ms pulse was then refined to grow over 120 ms, hold briefly, and shrink over 480 ms, making the return visibly slower than the enlargement. Locked node selection outlines remain hidden, with no yellow glow.
- **Delivery status:** Implemented in the unpublished local candidate; see [the verification and activation record](LOCAL_CANDIDATE_2026-10-23.md) for exact checks and remaining limits. Original proposal and research are retained below for history.

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P1, first.
- **Maintainer request and rationale:** Prevent accidental workspace edits with a
  padlock in the top right: **red closed padlock = locked**, **white open padlock
  = unlocked**. Make the state immediately recognizable during ordinary use or VR.
- **Proposed behavior:** Lock node movement and structural cable edits while
  leaving volume, mute, bypass and other sound controls usable. Preserve pan,
  zoom, selection and inspection. Provide state text/accessible naming as well as
  the requested color and shape; high contrast must remain usable.
- **Project fit / research:** `desktop/VineMix.Desktop/GraphWorkspace.cs` already
  centralizes drag, cable and selection actions. No layout-lock state is exposed.
  Repository review only; no external research or prototype performed.
- **Scope, dependencies and decisions:** Small to medium UI/control work, with
  no intended DSP change. Define one lock rule for move, delete, paste, align,
  add, group, reconnect and structural Undo/Redo so shortcuts cannot bypass it.
  Assumption: structural actions are blocked while ordinary parameter Undo can
  remain available. Decide whether lock state persists per workspace or session.
  IDEA-004 tasks already exclude structural edits.
- **Next step:** Review the icon placement and a short allowed/blocked action
  matrix, then verify mouse, keyboard and context-menu consistency.


<a id="idea-018"></a>
## IDEA-018: Minimise on close and a clear Settings quit action

- **Implementation decision (2026-10-23):** Retained the existing documented close-to-tray convention: closing minimises to the notification area and leaves audio running. Added a separated red **Quit VineMix** action as the final Settings item, with accessible intent. It invokes the same coordinated quit flow as the tray action. No close-behavior preference was added.
- **Delivery status:** Implemented in the unpublished local candidate; see [the verification and activation record](LOCAL_CANDIDATE_2026-10-23.md) for exact checks and remaining limits. Original proposal and research are retained below for history.

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P1.
- **Maintainer request and rationale:** The close button should minimise; put a
  **red Quit VineMix** action at the **bottom of Settings** so stopping the app
  is discoverable without finding the tray icon. A behavior chooser is not wanted.
- **Assumption to verify in design:** Interpret "minimise" as retaining today's
  hide-to-tray behavior and continuing audio. Current `MainWindow.OnClosing`
  calls `Hide()`, rather than minimizing to the taskbar. Taskbar minimization
  was not independently confirmed; do not silently change that distinction.
- **Project fit / research:** `desktop/VineMix.Desktop/MainWindow.cs` builds
  Settings; `App.xaml.cs` already provides the tray's coordinated `QuitAsync`.
  Reuse that path, with the normal save/engine shutdown handling. Local review
  only; no external research needed for the proposal.
- **Scope and tradeoffs:** Small, with clear spacing/separation at the bottom,
  readable red styling and an accessible action name. Closing remains reversible
  and leaves audio running; Quit intentionally ends the session. IDEA-021 improves
  the existing save-failure path without requiring a new shutdown implementation.
- **Next step:** Review the bottom-of-Settings placement and wording, confirm
  the tray assumption, then check both Quit entry points use the same behavior.


<a id="idea-019"></a>
## IDEA-019: Denoiser delay labels in the algorithm dropdown

- **Implementation (2026-10-23):** The real dropdown now reads **SpeexDSP · 20 ms** and **RNNoise · 30 ms**, with equivalent accessible help. The current source still uses 960 and 1440 frames at 48 kHz, and offline signal verification passed. Labels describe this node including bypass, not CPU cost, algorithm minima or total system latency. Saved values, defaults and DSP remain unchanged.
- **Delivery status:** Implemented in the unpublished local candidate; see [the verification and activation record](LOCAL_CANDIDATE_2026-10-23.md) for exact checks and remaining limits. Original proposal and research are retained below for history.

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P1.
- **Maintainer decision and rationale:** SpeexDSP is the faster/lower-quality
  choice in the maintainer's listening assessment; RNNoise trades more delay for
  better perceived cleanup. Users should see the delay when choosing an algorithm.
- **Proposed dropdown labels:** **SpeexDSP · 20 ms** and **RNNoise · 30 ms**.
  Brief help can explain the quality/delay tradeoff without promising the same
  ranking for every voice, noise source, humming or singing example.
- **Verified project evidence:** [Denoiser options](DENOISER_OPTIONS.md) records
  current measured node delays, including bypass. `GraphNodeView.cs` already
  shows selected delay elsewhere but its dropdown rows contain names alone.
  These are this implementation's delays, not universal algorithm minimums or
  the total microphone-to-output latency. "Faster" here does not assert a measured
  CPU-performance ranking. No new external research or listening test performed.
- **Scope and dependencies:** Small presentation change; preserve saved algorithm
  values, defaults and DSP. Ensure both rows fit and expose equivalent accessible
  text. The former general quality-comparison task is superseded by this decision;
  targeted regression listening remains appropriate when processing changes.
- **Next step:** Review the two labels and concise help in the existing selector.


<a id="idea-020"></a>
## IDEA-020: Post-update What's new screen

- **Implementation (2026-10-23):** Bundled plain-text highlights show after a healthy updated launch when the workspace is visible, active and idle. Background/tray launches, editors, gestures, pending edits, other surfaces and update/quit preparation defer the screen. **Settings → What's new?**, immediately above **About VineMix**, reopens it; the earlier Help-header action was removed in the later interface refinement. Fresh installs establish a baseline without a notice; older installations without state get one transition notice; skipped versions include all available bundled highlights. Dismissal alone records `lastSeenReleaseVersion` in separate update state; graph and Undo are untouched. Downgrades cannot lower the watermark. No remote HTML or forced tutorial was added.
- **Delivery status:** Implemented in the unpublished local candidate; see [the verification and activation record](LOCAL_CANDIDATE_2026-10-23.md) for exact checks and remaining limits. Original proposal and research are retained below for history.

- **Captured:** 2026-10-23; earlier installer/update request recovered in the
  chat review. **Status:** Captured. **Priority:** P1.
- **Maintainer request and rationale:** Tell users what changed after installing
  an update, once per version, and allow reopening **What's new** from Help.
- **Proposal:** Short, curated bundled highlights with clear feature explanations
  and an optional route to full release notes. Show after a healthy updated launch
  when the workspace is visible; defer background/tray launches and active edits.
  Dismiss without altering audio or forcing a tutorial. Track dismissal separately
  from graph settings; define first-install and skipped-version behavior.
- **Project fit / research:** `MainWindow.Updates.cs` already shows release notes
  during updating. It does not provide this post-update flow. Reuse the relevant
  [earlier feature-screen design](INSTALLATION_UPDATE_DESIGN_HISTORY.md#feature-and-release-screens),
  while ignoring superseded publication restrictions elsewhere in that history.
  Repository evidence only; no fresh external research.
- **Scope, tradeoffs and dependencies:** Small to medium UI and release-content
  work. Depends on existing release identity, not a new updater. Avoid repetitive
  prompts; decide how much to show after several skipped releases and whether
  optional Show me links open help or an inert example.
- **Next step:** Design one compact example and the first-install/update/dismiss/
  reopen state flow before adding a release-content format.


<a id="idea-021"></a>
## IDEA-021: Recover unsaved work before quitting

- **Implementation (2026-10-23):** Final Quit now saves before irreversible teardown. Failure offers Retry, save a recovery copy and quit, Cancel, or explicit Quit without saving. Recovery retains queued edits and reconciled newer saved changes, uses a complete workspace JSON file and refuses overwriting an existing file. Failed/cancelled recovery writes keep the decision open. Cancel restores the interactive session. Lost replies reconcile before replaying edits; structural replay conflicts preserve the original desired work for recovery. Both quit entry points share this flow.
- **Delivery status:** Implemented in the unpublished local candidate; see [the verification and activation record](LOCAL_CANDIDATE_2026-10-23.md) for exact checks and remaining limits. Original proposal and research are retained below for history.

- **Captured:** 2026-10-23. **Status:** Captured. **Priority:** P1.
- **Origin and need:** Earlier UI-review suggestion retained for review. A user
  should be able to rescue pending edits if the final save fails during Quit.
- **Proposal:** Offer Retry, save a recovery copy, cancel quitting, or explicitly
  quit without saving, with a concise explanation of what is unsaved. Do not add
  prompts to ordinary successful quits.
- **Project fit / research:** `App.xaml.cs` currently shows an OK-only warning
  from `MixerViewModel.StopAsync` and then shuts down. Beta 3's running save-retry
  fix is already implemented; this is the separate final-quit decision. Local
  code inspection only; external research not performed.
- **Scope, tradeoffs and dependencies:** Medium lifecycle work shared by the tray
  and IDEA-018 Settings action. The recovery decision must occur while saving or
  cancelling remains possible; do not offer Cancel after irreversible teardown.
  Recovery copies must retain queued graph changes and be clearly identified.
- **Next step:** Specify the failure-only state flow, including failed recovery
  copy writes and restoring an interactive window after Cancel.
