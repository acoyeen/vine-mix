# Audio Mixer console — design verification

Date: 17 October 2026, Australia/Brisbane.

Scope: the native WPF controller and its presentation. Audio, driver, latency,
clock-drift and recovery qualification remain deferred under [TESTING.md](TESTING.md).

## Findings

No actionable P0, P1 or P2 design differences remain after the fixes below.
The fixed console presents three sources, six independent sends, five mutes,
eight level faders, five meters, both A outputs and one B destination together.
The current saved configuration remains revision 1, with A muted at −24 dB.

## Source and rendered evidence

- Measured design contract: [UI overhaul plan](docs/UI_REDESIGN_PLAN.md).
- Visual source: [revised green mockup](docs/design/console-mockup.png),
  1609 × 978 pixels, approximately 1.437 pixels per target DIP.
- Reference interpretation: [mockup review](docs/design/MOCKUP_REVIEW.md), made
  before the production UI edits.
- Native implementation: [connected console](docs/design/implementation/04-live-console-final.png)
  and [handoff console](docs/design/implementation/07-live-console-handoff.png),
  both 1108 × 674 captured pixels. Windows capture omits the invisible outer
  resize borders; the actual WPF window remains 1120 × 680 DIP, with minimise
  available and resizing/maximising disabled.
- [Full-view comparison](docs/design/implementation/08-full-view-comparison.png)
  places the source and implementation together, each normalised to 1120 × 681
  pixels. This aligns overall scale; it does not imply pixel-identical native
  title chrome or a requirement to copy generated artwork.
- [Focused comparison](docs/design/implementation/09-controls-comparison.png)
  compares source/mute/fader and master/destination regions at that same scale.
  Small labels, units and state text were also inspected in the native captures.
- [Devices assignments](docs/design/implementation/05-live-devices-final.png)
  and [app connections](docs/design/implementation/06-live-app-connections-final.png)
  show the running controller. The dialog is fixed at 880 × 600 DIP; its native
  capture is 868 × 594 pixels.

The source and first connected capture share the saved-state example: microphone
1 B on, App Audio A on, A muted at −24 dB, B unmuted at 0 dB and silent meters.
At handoff PS VR2 had become unavailable independently of this work: A1 displays
**Waiting for device**, retains its saved name and does not replace it with a
different output. The engine remains connected and B shows Connected. This is
an observed UI state, not a controlled reconnection test.

## Comparison history and resolved findings

| Finding | Impact and fix | Revised evidence |
|---|---|---|
| P2: bare numeric gain editors | Added a visible dB unit under every readout; invalid text shows Check value and retains the previous level. | Handoff console, focused comparison and preview keyboard capture |
| P2: active mute label still read Mute | The active control now reads M · Muted, with explicit MUTED state and coral fill. | Connected/handoff console A master |
| P2: a reloaded meter could refresh its stale-data timestamp | Removed the load-time timestamp reset. Only received samples establish freshness; absent telemetry clears the display. | Final control implementation and offline harness capture |
| P1: Devices navigation template failed at runtime | Corrected the named template element and checked all three pages, persistent footer and native dropdown. | Final Devices captures and passing page/binding checks |

The initial preview evidence is retained in `docs/design/implementation/01-*`
and `02-*`. The revised full-view and focused comparisons were opened together
with their source evidence and reviewed after these fixes. No further visual fix
was identified in that final comparison.

## Required fidelity surfaces

| Surface | Review result |
|---|---|
| Fonts and typography | Segoe UI carries headings, labels, controls and scales; Consolas is restricted to editable numeric levels. The measured native scale takes precedence over rasterised lettering. Device names truncate with full names available in Devices/tooltips. Units, minus signs, focus outlines and validation text remain identifiable. No headline decoration or all-caps prose was added; compact ON/OFF/MUTED/CLIP state lettering follows the console convention. |
| Spacing and layout rhythm | Three 200-DIP sources, two 208-DIP masters, 8-DIP strip gaps and a 20-DIP group gap fit the 1060-DIP grid. Both physical outputs remain visible inside A. Mutes, route controls, typed levels and footer fit without main-window scrolling. Devices keeps its navigation and Done action visible; only detailed troubleshooting content scrolls. |
| Colours and tokens | Flat charcoal surfaces and one muted green replace blue/yellow bus coding. Both active A and active B use #8FC7AA. Coral #F47777 indicates mute/error/clip alongside words. Measured normal text/panel contrast is 12.27:1; secondary text 7.63:1; green/panel 7.55:1; inactive essential border/panel 3.01:1. The lightest and darkest meter colours remain green. High-contrast resources and keyboard focus were reviewed separately. |
| Image quality and assets | There is no product imagery, illustration or logo asset required by the measured plan. Generated gloss/logo artwork was explicitly excluded in the mockup review. The running interface uses native controls and vector-rendered functional scales/meters, not an embedded screenshot. The dropdown chevron uses Windows' Segoe Fluent Icons. Captures are lossless PNG; comparison resizing is for review only. |
| Copy and content | A Listen / What I hear and B Send / What others hear communicate destination and scope. Connected, Waiting for device, Not assigned and offline states remain separate from saved routing intent. Device setup uses exact current Windows names, explicit copy actions and concise app instructions. Driver identifiers and negotiation details remain under Troubleshooting. |

The [official Anthropic Frontend Design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md)
was read on 17 October 2026 at the user's request. Its guidance informed the
final review of product-specific hierarchy, restrained styling, meaningful
visual structure, typography, wording and state. The user's fixed console,
chosen palette and existing WPF stack remain authoritative. No additional
runtime dependency or web framework was introduced.

## Expected deviations from the generated reference

- The measured plan places destinations within their master strips. The image
  generator accidentally duplicated them and moved them to an extra footer;
  those mistakes were not reproduced.
- Readouts sit directly below their faders and support typed entry. The native
  version includes actual input/output peak values, clip holds and validation.
- Flat surfaces, modest native thumbs, Windows title chrome and the absence of
  decorative logos/gradients implement the agreed quiet console treatment.
- Real endpoint names and availability take precedence over invented mock text.
  Format details are kept in Troubleshooting rather than repeated in every strip.
- The preview captures explicitly say Preview · no audio engine. Normal operation
  reports the actual engine connection and contains no simulated meter activity.

## Verification and limits

The self-contained desktop publish completed with zero warnings and zero errors.
The [saved verification result](docs/design/verification/results.json) passed
28 inert UI checks with no WPF binding errors. The harness opens no named pipe,
audio device or real settings file. It checks all 64 source-route combinations
against the actual toggle controls, independent gains/mutes, saved route intent,
missing names, fixed bounds, cable filtering, typed gain validation, automation
patterns, contrast resources, observed clipping and disconnected editing.

Additional mouse and keyboard checks used an isolated preview: click/Space route
toggles, invalid input without a level change, A −6.5 dB independently of B,
Tab/arrow adjustment of B by 0.5 dB, readable dropdown selection and Devices
navigation. [The keyboard capture](docs/design/implementation/03-preview-keyboard-check.png)
records these preview values; they were never applied to the live configuration.

| Captures | Interpretation |
|---|---|
| `docs/design/verification/main-100.png` | Native client visual, 1106 × 643 pixels; title chrome excluded |
| `main-125.png`, `main-150.png` in the same folder | 1382 × 804 and 1658 × 964 render-density simulations, not actual Windows display-scaling changes |
| `main-high-contrast.png`, `devices-high-contrast.png` | Application resource palette verification; Windows settings unchanged |
| `main-offline.png` | No-engine fixture with editing unavailable and stale telemetry cleared |
| `devices-0.png`, `devices-1.png`, `devices-2.png` | All three Devices pages, 866 × 563 client pixels |

This is a Windows desktop app, so browser console/mobile breakpoints do not apply.
WPF binding errors were collected instead. Screen-reader control names and native
patterns were checked, but complete spoken screen-reader behaviour and actual
Windows DPI transitions still require a later session. No claim of complete
accessibility certification is made.

Only the desktop executable was replaced in the live package. The existing
native engine stayed running. The settings SHA-256 before and after the update
is `95D0A5A22067FC9049CFA4A6880DC9C29BDDBB1CD62795AA75F9C6BE3930423A`.
No route, assignment, gain, mute, Windows default or other audio application was
changed. No test sound, playback start or recording was initiated.

## Implementation checklist

- [x] Review the revised green reference before implementation.
- [x] Implement fixed main/Devices dimensions and complete control inventory.
- [x] Preserve independent sends, saved mute/route intent and endpoint names.
- [x] Review normal, focused, muted, missing-device and offline presentation.
- [x] Check contrast resources, UI automation semantics and preview keyboard input.
- [x] Resolve P0/P1/P2 findings and compare revised native evidence.
- [x] Leave the updated controller open on the existing engine/configuration.
- [ ] Complete actual OS scaling and full screen-reader interaction checks later.
- [ ] Reboot and qualify native audio/hardware under TESTING.md later.

Follow-up polish: none required for this UI handoff. The remaining checks above
are explicitly outside the completed inert/UI-only verification scope.

final result: passed
