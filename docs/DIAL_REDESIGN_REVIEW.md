# Audio Mixer dial and dynamics redesign — review

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](BETA_TESTING_CURRENT.md) for the active release checks.

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

18 October 2026 · Windows WPF desktop and C++ engine

The current candidate in `artifacts/AudioMixer-PrimaryDials` restores EQ's three
dials to 86-DIP faces and gives primary effect controls the same size; secondary
controls remain 58 DIPs. Level and tone dials keep their 86-DIP faces. All value
fields retain consistent quiet styling, 110 × 28-DIP dimensions and 12-DIP type.
EQ Width/Q resets to 0.70710678 for shelf/pass shapes and 1 for Bell/Notch; its
marker follows selection and shape without changing stored Q until an explicit
reset. This build passed 3,940 detached checks with 109 renders and zero binding
errors; evidence is in `artifacts/primary-dials-review`. Earlier results below
describe earlier builds. The updated interface is active and responsive; the
existing audio engine stayed running and settings bytes remained unchanged.

The previous consistency pass extends quiet numeric fields to EQ, level and tone
controls. Every numeric field now shares size, typography and hover/focus/error
states. Detector thresholds use dBFS, ratios
omit the space before `:1`, and EQ/filter Q displays share precision. This build
passed 3,926 detached checks with 109 renders and zero binding errors in
`artifacts/consistent-controls-final`. Its interface was activated with the same
running audio engine and unchanged settings.

The review and screenshots below record the original dial redesign. A subsequent
clutter correction removes repeated headings and gesture instructions, combines
telemetry into one line, quiets numeric-entry borders and shows only the relevant
Expander/Gate dials. Essential dial instructions remain in the workspace footer;
saved compressor selectors share one row without a technical banner. Processing
and saved settings are unchanged by that correction. That build passed
3,876 detached UI checks with 109 renders and zero binding errors; evidence is in
`artifacts/clean-nodes-review`. It was activated by replacing only the controller:
the audio engine kept running, the window reopened, and settings bytes stayed
unchanged. The Expander/Gate closeup is 232 pixels shorter and the saved Compressor
143 pixels shorter at 96 DPI, with the same width and text/dial sizes.

Archived image: Compact Expander

Archived image: Compact saved Compressor

The following sections retain the original review and its historical results.

Implemented and built as a separate candidate in `artifacts/AudioMixer-DialReview`.
The personal mixer package, personal workspace and audio endpoints were not used
for verification or changed. `Preview.cmd` in that candidate opens the existing
in-memory preview for manual exploration; it never connects to an engine.
The candidate has not been activated as the personal mixer.

## Control system

Every adjustable scalar in the nodes now uses the shared rotary component.
Filters, dynamics, denoising, de-essing, plosive reduction, reverb, balance,
alignment and Voice Color use compact groups. Compressor and Expander use three
columns; most effects use two. The existing EQ graph, EQ dials, tone dials and
Voice Color pad remain. The obsolete horizontal numeric control was removed.

Labels, values and units remain visible. Selectors stay above parameter groups;
meters and detector/reduction readouts have passive styling. Actions and bottom
Bypass remain separate. Visible instructions explain vertical drag, Shift,
exact entry, Enter/Escape and reset. Keyboard arrows, Page Up/Down, Home, focus
indicators and actual-unit accessibility remain available. Wheel input zooms.

Time fields retain up to three visible decimals, ratios retain fractional values,
Q retains its useful precision, and focused entry exposes the full stored value.
Unchanged entry never rounds the model. Samples accept only integers. Reset uses
the actual parameter default, including the listening output's −24 dB default.

The design favours recognition over minimum pixel area. Effect dials require more
space than the old 30-DIP fields, but grouped controls avoid a full-size row per
parameter. For example, the five Compressor dials occupy two rows. The dedicated
Limiter is smaller than the old combined Compressor/Limit interface. Existing
workspace positions are retained; there is no automatic rearrangement.

## Processing and compatibility

| Node | Current behavior |
|---|---|
| Compressor | Threshold, Ratio, Make-up, Attack, Release, plus main/sidechain detector selection. New nodes have no mode selector or Ducking preset. |
| Limiter | Ceiling and Release only. Reuses sample-peak limiting with no lookahead. Inherited and linked gain are included before ceiling enforcement. Bypass removes limiting; later gain or summing can exceed its ceiling. |
| Ducking | Threshold and fixed Duck amount, with Attack and Release. The maximum absolute trigger channel feeds a 5 ms rise / 50 ms fall detector and fixed 3 dB hysteresis. Both main channels share the smoothed attenuation. |

Ducking defaults: −30 dBFS threshold, 18 dB attenuation, 10 ms attack and 250 ms
release. Trigger level above the threshold selects one fixed attenuation target;
it does not apply a compressor ratio. Missing/disconnected trigger requests
smooth release immediately. A silent or muted connected trigger decays and
releases; the main input is never a fallback detector. Zero amount smoothly
returns to unity. Trigger audio never enters the audible output. Bypass uses the
existing 10 ms transition. Parameter changes and trigger rewires retain history.

Audio and sidechain connections keep distinct types. Ducking can be inserted
into an ordinary audio cable with its trigger already connected. Detector edges
participate in cycle checks. Typed rewiring retains cable identity, gain and mute.
The existing conservative detach rule refuses detector/linked-control ambiguity
and leaves every cable unchanged; remove those connections before detaching.

Saved compressor records remain compressors. An absent `dynamicsUiVersion`
means legacy presentation (`0`), retaining the mode selector and all old settings.
New compressors use presentation version `1`. No external compressor is inferred
to be a fixed-depth Ducking node. Node IDs, names, positions, cables, gains,
linked controls, bypass and settings hidden by the current mode survive loading,
copying and saving. Native processing of the old compressor is unchanged.
Loading does not save or migrate the workspace. Version 2 and the established
original/last-good backup conventions remain. Explicit reset preserves identity,
cables and presentation version while resetting sound controls.

See [the complete dynamics specification](CORE_DYNAMICS.md) for ranges, timing,
edge cases and limiting constraints.

## Before and after

These are captures of actual WPF controls rendered from isolated fixtures.
They are not design mockups or desktop captures. The original executable was
built and captured before implementation. Each closeup has 18 DIPs of padding.

| Before: combined compressor | After: focused Compressor |
|---|---|
| Archived image: Old combined compressor | Archived image: New Compressor |

| After: dedicated Limiter | After: fixed-depth Ducking |
|---|---|
| Archived image: New Limiter | Archived image: New Ducking |

| Before: filter | After: filter |
|---|---|
| Archived image: Old filter fields | Archived image: New filter dials |

| Before: reverb | After: reverb |
|---|---|
| Archived image: Old reverb fields | Archived image: New reverb dials |

| Before: Voice Color | After: Voice Color |
|---|---|
| Archived image: Old Voice Color | Archived image: New Voice Color |

The same populated baseline workspace, before and after:

Archived image: Populated workspace before

Archived image: Populated workspace after

The new populated routing example, with a detector-only voice branch and a
separate audible voice branch:

Archived image: Populated dynamics graph at 1920 by 1080

At the supported minimum 900 × 580 window, Fit presents the entire route at
approximately 33%. Dials and cable types remain identifiable; reading and precise
editing require zooming toward 100%. The node geometry stays fixed at every zoom.

Archived image: Minimum window

## Verification

| Check | Result and scope |
|---|---|
| Native offline suites | **Passed: 12/12.** Routing, stub engine lifecycle, settings recovery, clock drift, effect quality, spectrum, graph performance, mock endpoint catalogue, Voice Color and core dynamics. |
| Focused dynamics | **Passed: 139 checks.** Legacy compressor bit-exact reference, fixed-depth threshold/hysteresis, trigger isolation/removal/rewiring, stereo linking, attack/release timing, zero amount, bypass/live edits, limiter ceilings with inherited/linked gain, JSON and realtime allocation checks. |
| Published desktop candidate | **Passed: 3,865 detached checks; 108 renders; zero binding errors.** The final self-contained candidate itself ran the verifier. |
| Layout | **Passed.** All 12 supported zoom stops from 30–200%, fixed sizes/ports during telemetry, no dial overlap, bottom Bypass, 96/144/192 DPI captures and 900 × 580 minimum window. |
| Interaction and model | **Passed in detached fixtures.** Routed keyboard/wheel events, bounds, precise entry/reset, whole samples, accessible names/ranges/focus, insertion, typed rewiring, cycles, copying and persistence. |
| Build | **Passed.** Native engine and self-contained Windows desktop published separately. Desktop build has zero warnings/errors. Existing upstream CMake policy warnings remain. |
| Listening, real pointer/keyboard gestures and hardware | **Not run.** No physical/virtual audio playback, real endpoint capture, personal-graph testing, live application routing, monitor DPI transitions, acoustic latency or endurance claims. |

Raw evidence is retained privately outside the repository. The historical
identifiers are `artifacts/dial-redesign-before`, `artifacts/dial-redesign-final`
and `artifacts/dial-redesign-native`.
[Verification manifest](DIAL_REDESIGN_EVIDENCE.json) records their paths, result
counts and the hashes of the reviewed sources and candidate executables.
Earlier intermediate render failures were stale verification assumptions about
horizontal controls; those checks were updated to actual dial units and rerun.
The final published candidate passed the complete detached suite.

## Main implementation locations

- Shared controls: `desktop/AudioMixer.Desktop/Controls/RotaryValueControl.cs`.
- Layout and feedback: `desktop/AudioMixer.Desktop/GraphNodeView.cs`.
- Catalogue, persistence and wiring: `NodeCatalog.cs`, `GraphModels.cs`,
  `GraphClipboard.cs`, `GraphConnectionEditing.cs` and `GraphWorkspace.cs`.
- Audio processing and protocol: `native/src/graph.cpp`, `native/src/engine.cpp`,
  `native/include/am/types.hpp`, `graph.hpp` and `configuration.hpp`.
- Focused verification: `NodeDialRedesignVerification.cs`,
  `CoreDynamicsVerification.cs`, `RotaryControlVerification.cs` and
  `native/tests/core_dynamics_tests.cpp`, plus existing model/routing suites.
