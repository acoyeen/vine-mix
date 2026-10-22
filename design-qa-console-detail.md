# Audio Mixer — control detail and contrast review

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](docs/BETA_TESTING_CURRENT.md) for the active release checks.

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

Date: 17 October 2026, UTC+10.

## Findings and final result

No actionable P0/P1/P2 findings remain after this refinement. The controls are
more clearly separated from the slate chassis, with recessed faces, physical
fader detail, explicit A/B lane labels and larger level values. The continuous
layout, fixed dimensions and integrated window controls are preserved.

This is UI verification. Native audio and reliability qualification remain
separate under [TESTING.md](TESTING.md).

## Visual sources, state and comparison

- Fresh previous interface
  and VoiceMeeter reference
  were opened before the changes and attached to image generation.
- Revised detail reference, 1609 × 977
  pixels; [interpretation](docs/design/CONTROL_DETAIL_REVIEW.md).
- Final native render, exactly
  1120 × 680 pixels/DIP. Both Devices pages and troubleshooting remain 880 × 600.
- Equal-density full comparison
  places the source and final native render together at 1120 × 680 each. Only
  the source was reduced, using Lanczos; the implementation remains unscaled.
- Focused before/after comparison
  shows the actual old/new faders, route controls, units and readouts at equal
  native scale. The source and implementation were reviewed together, followed
  by the focused comparison.
- Live controller
  confirms Audio engine connected, current revision 3 and preserved live values.

The source image contains illustrative signal activity and an incorrect
microphone gain. These were deliberately excluded from implementation. The inert
comparison uses silent supplied telemetry, source levels 0 dB, microphone 1 B on,
App Audio A on, and A muted at −24 dB. Normal operation displays actual telemetry.
Filled fader travel shows gain position, not audio activity.

## Iteration history and fixes

The user's feedback identified shortcomings in the otherwise functional flat
slate build. Its [prior review](design-qa-slate.md) is historical.

| Finding | Resolution and evidence |
|---|---|
| P2: controls blend into the panel | Changed the recessed face from #39474E to #081014, increasing face/chassis contrast from 1.51:1 to 3.02:1. Device plates have navigation chevrons; actionable buttons have visible faces and upper/lower edges at rest. Final full/focused comparison shows the separation. |
| P2: fader tracks/caps are too weak | Replaced the 5-DIP slot and 32 × 15 cap with a 14-DIP outer track, 12-DIP gain fill and 40 × 24 cap. Caps have a dark centre index and two grip rules. Active travel is green; off routes retain fully visible grey controls. |
| P2: small readouts and unclear lane identity | Increased gain values from 12 to 18 DIP, scales from 9 to 10, and added A/B labels above each source lane. The first detail pass used 14-DIP values; the revised final comparison shows the enlarged values without clipping. |
| P2: removal of cards also removed visual hierarchy | Added one full-width channel-name band and restrained control bevels. Strip containers still have no outline or rounded corners; no decorative graphics or textures were added. |

The final comparison followed the fixes above. A final polish pass separated
slightly darker green travel from pale caps and softened only the small physical
cap corners. No enclosing strip cards were restored. A separate read-only visual
review found no remaining P1/P2 issues.

## Required fidelity surfaces

| Surface | Review |
|---|---|
| Typography | Segoe UI labels and Consolas numeric values remain native. Gain values are now 18 DIP and semi-bold; scales/units are readable at 10 DIP. Names truncate intentionally, with full-name details available. A/B lane labels and explicit ON/OFF/MUTED wording clarify scope. |
| Spacing and layout | The existing 1060-DIP grid, five strips, six sends, five mutes, eight faders and five meters fit. Both A slots and B destination remain visible. Depth belongs to controls; the chassis remains open and aligned. Enlarged caps fit the gain lanes and the keyboard-focus outline remains independent. |
| Colours and tokens | The chassis stays #566168. Text/chassis is 5.69:1 and secondary text 4.79:1. Recess/chassis is 3.02:1, essential edge/chassis 3.49:1, edge/off-face 6.49:1, off-face text 10.60:1 and inactive cap/chassis 4.67:1. Dark text on active green is 10.96:1. The new header supports text at 8.16:1. High contrast still uses Windows resources. |
| Images and assets | No product artwork or logo is required. Native control templates provide functional bevels, fader grips and tracks; Segoe Fluent Icons supplies navigation/window glyphs. Generated gloss/texture and invented meter activity were not copied. Evidence is lossless PNG. |
| Copy and content | Existing routing/status language remains accurate. Device plates visibly navigate to details, numeric values remain editable, and off routing does not disable gain adjustment or silently enable a route. The live close action still explains tray behaviour. |

## Verification and limitations

The final self-contained publish completed without warnings or errors. The
saved result passed **37 inert
UI checks**, exit 0, with **zero WPF binding errors**. In addition to all 64 route
combinations and prior control/state checks, it verifies larger caps, fully
visible off-route faders, A/B lane labels and measurable control separation.

In a silent isolated preview, clicking the new microphone 1 A cap exposed its
focus outline. Up changed A to +0.5 dB while B stayed at 0 dB and A routing stayed
off; refreshed accessibility values agreed with the visible readout. The preview
was then closed. No preview value was applied to the live configuration.

Normal, high-contrast, Devices and offline renders were inspected. The 125% and
150% renders are density simulations, not Windows display-setting changes. Full
screen-reader speech, actual DPI transitions and the previously unobserved
Alt+Space popup remain later manual checks. No claim of complete accessibility
or hardware/audio qualification is made.

## Live handoff

Only the desktop executable was replaced. The original audio engine and its
start time remained unchanged. Before and after replacement, saved revision 3
retained this SHA-256:

`<private-settings-sha256-omitted>`

A remains muted at −24 dB. No routing, gain, mute, assignment, Windows default,
driver or other audio application was changed. No test playback was started.

- [x] Inspect the actual interface/reference and revised visual target.
- [x] Resolve weak control separation, small readouts and low-detail faders.
- [x] Compare revised full/focused native captures and high-contrast states.
- [x] Pass 37 UI checks and preserve configuration during controller replacement.
- [x] Leave the updated console connected to the existing engine.
- [ ] Complete deferred manual UI/audio qualification separately.

final result: passed
