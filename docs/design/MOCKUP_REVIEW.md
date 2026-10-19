# Fixed console mockup review

The initial generated reference was inspected before production UI edits on 17 October 2026 and is retained as `console-mockup-initial.png`. The user then explicitly requested neutral monochromatic green instead of blue/yellow. The revised, inspected `console-mockup.png` is the current style reference. Its raster is approximately 1.436 pixels per DIP relative to the 1120 × 680 target; native screenshots must be compared at a normalized density.

Accepted: five aligned strips, three pairs of A/B faders, one meter per strip, filled route/mute controls, compact device names, both A output rows, and one B microphone destination. The saved-state example has silent meters, microphone 1 B on, app A on, and A master muted at −24 dB.

The approved specification remains authoritative where generation is imperfect:

- Both A ON and B ON receive the same solid muted-green face; an outline alone is insufficient. Labels and position distinguish buses.
- Use flat charcoal surfaces, with no generated glossy gradients or decorative logo requirement.
- A and B destinations appear once, in the lower assignment area; duplicated master-header selectors are omitted to avoid crowding.
- The revised generated image moved assignments into an extra full-width footer despite the editing instruction. Keep the approved plan's assignments within their A/B master strips, with both A slots visible. The image is a style reference, while the measured plan governs layout and scope.
- Preserve the exact independent A/B gain semantics and explicit on/off text.
- Use short meaningful names on the mixer and complete names in Devices.
- No generated screenshot is embedded as the operational interface. All controls are native WPF, backed by the existing view models.

The measured layout uses 1060 DIP: three 200-DIP sources, two 208-DIP masters, three 8-DIP gaps and one 20-DIP group gap. The 1120 × 680 outer window retains native title chrome. The body budgets approximately 520 DIP vertically: device/header 95, route row 36, fader/meter area 290, mute 36, context 36 and spacing. The final native render is the proof of fit.
