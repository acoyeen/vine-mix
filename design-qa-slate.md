# Audio Mixer — flat slate console review

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](docs/BETA_TESTING_CURRENT.md) for the active release checks.

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

Date: 17 October 2026, UTC+10.

## Findings

No actionable P0/P1/P2 visual issues remain in the revised interface. It now uses
a continuous medium slate-grey surface, thin strip dividers and flat square
controls. Minimise and close are embedded in the app header, with no separate
Windows title bar. Fixed dimensions and the complete audio-control inventory
remain intact.

This review covers presentation and window interaction. Audio and reliability
qualification remain deferred under [TESTING.md](TESTING.md).

## Source, state and comparison evidence

- Original visual reference: VoiceMeeter,
  1024 × 620 pixels. Its continuous chassis, grey palette, green controls and
  integrated window actions informed this revision.
- Revised reference: slate mockup,
  1609 × 977 pixels, approximately 1.437 pixels per target DIP. The actual
  VoiceMeeter image and prior app capture were attached to its generation.
- Measured interpretation: [revision review](docs/design/LIGHT_CONSOLE_REVIEW.md).
- Native implementation: UI render,
  exactly 1120 × 680 pixels/DIP at 100% rendering, including the integrated header.
  The live console shows
  the actual engine connection and saved revision 3 after replacement.
- Combined full-view comparison:
  both reference and implementation are together at 1120 × 680 each. The source
  was reduced with Lanczos; the native render was left at its original density.
- Focused comparison:
  header, source names, routing controls and scales were compared at equal scale.
- Devices captures are in light-verification:
  each page is exactly 880 × 600 pixels/DIP, with the persistent footer visible.

The inert comparison represents microphone 1 B on, App Audio A on, other sends
off, A muted at −24 dB and B unmuted at 0 dB. It explicitly says Preview, with
no engine access. The live screenshot instead reports current telemetry and
revision 3. Listening output A1 is currently Waiting for device while microphone 1, App Audio
and B show Connected. We did not disconnect hardware to create this state.

## Review history and fixes

The [previous charcoal review](design-qa-charcoal.md) records the earlier completed
layout. The user's subsequent feedback superseded its card treatment, palette
and native title chrome. This revision resolved those differences by:

1. Replacing near-black surfaces with #566168 across the continuous chassis.
2. Removing the five rounded strip outlines and square-box tab frames. Grouping
   now uses alignment, spacing, a few vertical rules and selected-tab underlines.
3. Removing resting outlines from ordinary buttons and numeric readouts. Focus,
   hover, dropdown and high-contrast affordances remain explicit.
4. Embedding minimise/close in both window headers using opaque WindowChrome.
5. Increasing fader travel into the space reclaimed from the former title bar.
6. Giving meter wells their own dark resources and warnings a readable light
   coral text resource. Small text does not reuse the lower-contrast mute fill.
7. Correcting keyboard system-menu coordinate conversion for Windows DPI scaling.

The revised full-view and focused comparisons were opened and inspected after
these fixes. The final build differs only in the DPI-coordinate correction;
its geometry, palette and controls are unchanged. Its UI checks and captures
were regenerated and saved in `docs/design/light-verification/`.

## Required fidelity surfaces

| Surface | Assessment |
|---|---|
| Typography | Segoe UI remains the native UI face; Consolas is confined to editable numeric readouts. The title, five strip names, routing state, units and destination names have a consistent hierarchy. Small scale ticks are intentionally compact. Truncated endpoint names retain full-name access in Devices and tooltips. |
| Spacing and layout | The fixed 1060-DIP grid contains three 200-DIP sources and two 208-DIP masters, with 8-DIP gaps and a 20-DIP group gap. There are no enclosing strip cards. The header, mutes, two A slots and B destination fit together without main scrolling. Devices uses open sections, row rules and a persistent Done action. |
| Colour and tokens | #566168 is visibly lighter than the previous #191C1E/#252A2C. Both A/B accents use #B9E3CB; position and labels distinguish the buses. Text contrast on the chassis is 5.69:1, secondary text 4.79:1, green text 4.51:1, alert text 4.71:1 and essential boundaries 3.49:1. Dark labels on active green and mute coral are 10.96:1 and 6.46:1. Low meter green against its well is 6.82:1. High contrast uses Windows system resources. |
| Images and assets | No illustration, logo or decorative image is required. The image generator's gloss and gradients were excluded. Functional meters/scales are rendered natively. Window and dropdown icons use Segoe Fluent Icons. PNG evidence is lossless; source resizing is limited to comparison. |
| Copy and content | A Listen / What I hear, B Send / What others hear, ON/OFF/MUTED, connection states and exact Windows names remain meaningful. The live close tooltip explains that audio continues in the tray. Setup keeps transport details away from the ordinary console. |

Expected differences from the mockup: native gain scales preserve the actual
−60…+12 dB range; existing clip controls and actual meter values remain visible;
window controls have no resting enclosing boxes; destinations use compact detail
buttons rather than misleading duplicate selectors. The surface is flat instead
of reproducing generated lighting. These choices implement the user's request
for fewer outlines while preserving existing behaviour.

## Verification

The final self-contained publish succeeded without warnings or errors. The
saved UI result reports exit 0,
passed true, **33 checks**, and **zero WPF binding errors**. It includes all 64
route-state combinations, independent levels/mutes, fixed bounds, missing-device
names, cable filtering, typed levels, UI automation patterns, clipping/offline
states, borderless chrome and named embedded window actions. No live pipe,
audio device or saved configuration is accessed by this harness.

In the isolated silent preview, the embedded minimise button produced an explicit
minimised-window response; activation restored it. Devices opened from the header
and closed through its embedded close button. The main preview also exited
through its embedded close button. Right-clicking the empty header displayed the
native Move/Minimise/Close menu, with Size/Maximise unavailable; Escape dismissed
it. No routing or level controls were changed during these window checks.

Residual checks: Alt+Space did not produce an observable popup through the
computer-use session; keyboard shortcut interception was not diagnosed. Actual
Windows DPI transitions, caption dragging across monitors and full screen-reader
speech interaction remain later manual checks. Render-density captures at 125%
and 150% are 1400 × 850 and 1680 × 1020; Windows settings were not changed.
High-contrast and offline captures were inspected. Browser/mobile tests are not
applicable to this fixed native Windows utility.

## Live handoff and checklist

Only the desktop executable was replaced. The original native engine process
and its start time remained unchanged. Saved revision 3 retained this SHA-256
before and after replacement:

`<private-settings-sha256-omitted>`

A remains muted at −24 dB. No assignment, gain, route, mute, driver, Windows default
or other audio application was changed. No test playback or recording was started.

- [x] Inspect the actual reference and revised mockup before production changes.
- [x] Replace rounded strip cards and native title bars with the requested console.
- [x] Preserve all controls, fixed dimensions and existing tray/quit behaviour.
- [x] Review full, focused, high-contrast and offline states.
- [x] Verify embedded window controls in an isolated preview.
- [x] Build, run 33 inert checks and leave the live controller connected.
- [ ] Complete the residual manual UI checks and deferred audio qualification later.

final result: passed
