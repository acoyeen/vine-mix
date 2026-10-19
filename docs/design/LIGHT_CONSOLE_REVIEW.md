# Flat slate console revision

The user requested a lighter, more neutral VoiceMeeter-inspired surface on
17 October 2026. This supersedes the charcoal rounded-strip treatment.

The actual [VoiceMeeter reference](evidence/03-voicemeeter-reference.png) and the
[running prior console](implementation/07-live-console-handoff.png) were opened
before generating and inspecting [the revised mockup](light-console-mockup.png).
Both references were attached to generation. The selected product and its
functional inventory remain unchanged.

Use one medium slate-grey chassis with aligned strips and a few vertical rules.
Remove complete strip outlines, rounded containers and floating surfaces. Keep
control wells where they explain an editable value, selector or meter; focus
indicators remain visible. Green marks active routes and levels, with coral for
mute/clip/error. Preserve the native controller, two independent sends per source,
two A slots and one B endpoint.

Integrate minimise and close in the application's header rather than retaining
a second Windows title bar. The fixed 1120 × 680 main window and 880 × 600 Devices
window keep native drag, keyboard system menu and existing close/tray behaviour.
No transparency, resizing, maximising or new audio behaviour is required.

The generated mockup is a visual direction, not a substitute for actual controls.
Keep existing fader ranges/scales, real dB values, clip controls and truthful live
states even where generation omitted them. The measured 1060-DIP column grid
remains authoritative. Extra client space from removing title chrome increases
the fader height while keeping all destinations visible. Do not add new effects,
knobs, routes, logos or decorative graphics from the reference.

The current live baseline at the start of this revision is saved configuration
revision 3, A muted at −24 dB. Its SHA-256 is
`D082E5A80406C58E7ACD25EFD8B56FE57234C89F6780314F095177429EB1E0F5`.
Preserve this current state rather than restoring the older revision-1 example.
No audio playback or reliability test is part of this presentation change.
