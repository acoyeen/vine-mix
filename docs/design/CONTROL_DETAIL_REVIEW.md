# Control contrast and console detail

The user requested clearer controls and some physical-console detail on
17 October 2026. Preserve the continuous slate layout and integrated window
actions; do not restore rounded strip cards.

The [fresh current capture](implementation/13-before-control-detail.png) and
[VoiceMeeter reference](evidence/03-voicemeeter-reference.png) were inspected and
attached to the [revised reference](detailed-console-mockup.png).

The prior #39474E control well differed from the #566168 chassis by only about
1.51:1. Text contrast alone did not make those controls identifiable. The change
uses a much darker recessed face, clear light/dark control edges, larger fader
caps and tracks, explicit A/B fader labels, and larger numeric/scale text.
Depth belongs to functional controls: native bevels and index/grip lines, with
one full-width channel-name band. No ornamental texture, effects, knobs or strip
containers are required.

The generated reference contains incorrect illustrative gain values and meter
activity. Preserve actual saved levels and truthful telemetry instead: A is
muted at −24 dB, source send levels remain unchanged, and no simulated activity
is used in normal operation. Filled fader travel indicates the saved gain
position, not audio activity. Off routes retain clear, fully visible grey caps.

No audio behaviour, routing, configuration or driver changes are part of this
revision. The current baseline remains revision 3 with settings SHA-256
`D082E5A80406C58E7ACD25EFD8B56FE57234C89F6780314F095177429EB1E0F5`.
