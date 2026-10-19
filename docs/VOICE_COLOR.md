# Voice Color

Voice Color is a self-contained effect in **Add → Effects**, also available by
searching its name. It accepts and sends stereo audio like the other effects.
Its two-axis pad combines tonal balance with body, presence and density for a
broadcast-style voice. The mapping is original; it does not claim to reproduce
Voicemeeter's Intellipan algorithm or match a particular announcer's voice.

## Controls

| Direction | Control | Result |
|---|---|---|
| Left → right | Tone: Warm → Bright | Change the balance of bass, presence and treble |
| Bottom → top | Character: Natural → Broadcast | Add strong body/presence shaping, a low-mid cut, compression and density, delayed doubling and a small room |
| Bottom centre | Neutral | Flat EQ and transparent processing |

The square grid has quarter divisions and slightly stronger centre guides.
Larger, semibold directional labels sit inside the surrounding well, in clear
strips above and below the grid. The entire handle stays inside the well at
every extreme without covering a label. There is no visible Neutral label or
duplicate position readout. A discreet reset diamond appears at bottom centre
when the handle moves away; assistive technology still reads exact coordinates.

Drag the handle or click a position inside the square. Hold **Shift** while
dragging for finer relative movement. Focus the pad and use **Left/Right** for
Tone or **Up/Down** for Character: each press moves one percentage point, or
0.1 point with Shift. **Home** or a double-click inside the square resets both
axes to neutral. Clicking the labels does not edit or reset the axes. The pad
handles its own editing gestures; dragging it does not move the node. The wheel
retains ordinary canvas zoom.

The smaller shared numeric dials show **Tone −100% to +100%** and
**Character 0% to 100%**, with persistent labels and percentage entries. Drag
vertically, hold Shift for fine adjustment, or type an exact value and press
Enter; Escape cancels. Double-clicking one dial or its value resets only that
axis. Arrow/Page keys adjust a focused dial and Home resets it. The workspace
footer supplies the shared dial instructions. The pad, labels and dials
stay visible and scale with the rest of the node.

The standard bottom **Bypass** button is grey when processing, red when
bypassed. It retains the XY position and uses the normal effect bypass behavior.
**Right-click → Reset node to defaults** resets both axes and bypass while
preserving the node's name, position and cables. There is no duplicate Level
control; use a Mix node for a separate branch volume.

## Parameter mapping

Configuration continues to store `voiceColor: { tone, ambience }`. Tone `t`
spans −1 to +1. The stored `ambience` value, called Character `c` in the UI,
spans 0 to 1. Both default to zero. Numerical editing percentages are normalized
on storage; there are no independent hidden user-adjustable EQ or dynamics
settings. The effective Character amount is `a = c^1.3`: its lower half retains
gentler adjustment while the upper range reaches a deliberately strong effect.

Processing runs at 48 kHz in this order: four EQ filters, headroom trim,
parallel compression, envelope-relative density, delayed doubling, then the
small room.

| EQ stage | Frequency | Q | Gain |
|---|---:|---:|---|
| Low shelf | 140 Hz | 0.70710678 | `12 × a − 12 × t` dB |
| Low-mid bell | 450 Hz | 0.9 | `−8 × a` dB |
| Broad presence bell | 3,000 Hz | 0.85 | `12 × a + 3 × t` dB |
| High shelf | 7,000 Hz | 0.70710678 | `4 × a + 9 × t` dB |
| Warm headroom trim | — | — | `−6 × max(−t, 0)` dB |

At centred Tone, increasing Character raises body and presence while reducing
muddy low mids. The upper edge is deliberately pronounced; the middle of the pad
provides a milder treatment. Tone spans a much wider warm/bright range. At maximum
Character, the low shelf reaches 24 dB at the Warm edge; the presence bell
reaches 15 dB and the high shelf 13 dB at the Bright edge. These are individual
filter gains before the headroom trim and dynamics. Trim reduces level toward
Warm to accommodate its stronger bass boost. Bright has no additional global
cut because its bass reduction already lowers typical speech levels. Character
does not add an automatic output cut. This is not a limiter or a loudness match.

The compression stage blends `0.4 × a` compressed signal as Character rises;
at least 60% of the direct signal remains even at the upper edge. This avoids
the large level loss from putting already-shaped speech through full-wet
compression. Both channels share the same detector and gain so their relative
balance is retained.

| Compression setting | Value |
|---|---:|
| Threshold | −28 dBFS |
| Ratio | 4.5:1 |
| Soft knee | 6 dB |
| Maximum gain reduction | 18 dB |
| Detector attack / release | 0.5 ms / 50 ms |
| Gain attack / release | 1 ms / 120 ms |
| Make-up gain | 4 dB |

Compression brings louder speech closer to quieter speech, and the make-up gain
adds level to its processed branch. Its effect depends on the incoming level;
it is not automatic normalization. The separate Compressor remains available
when explicit threshold, ratio or timing control is needed.

The density stage uses a stereo-linked 10 ms attack / 120 ms release envelope.
It blends up to 50% (`0.5 × a`) of a soft peak-rounding curve with drive 2.5,
scaled to the signal's own
envelope. This retains its character on quiet or already-compressed speech
without automatic input gain. Its gain stays between 0.5 and 1: it only rounds
peaks. Activity fades smoothly out between envelope levels of 0.0001 and 0.001
(−80 to −60 dBFS); silence cannot be amplified into sound. This nonlinear
colouring intentionally adds some harmonics and is not transparent limiting.

Delayed doubling blends in a second copy at up to `0.12 × a` wet mix, delayed
12 ms on the left and 19 ms on the right. There is no feedback or pitch shift.
The short offsets add thickness and some stereo spread while leaving the direct
path undelayed. This is a delayed-copy treatment rather than two independent
vocal performances. A mono sum can change its colour.

The room reuses the existing reverb processor with wet mix `0.02 × a`,
0.3-second decay, 0.65 damping and an 8 ms pre-delay setting. The room network
adds its own delay after that setting, so 8 ms is not the first audible reflection
time. Its small maximum mix supports the voice without the former pad's
prominent room tail. Use the separate Reverb for explicit room control.

At the Natural edge, compression, density, doubling and room mixes are zero. Tone still
changes the EQ there. At Neutral, all four EQ gains and the trim are zero too,
giving transparent processing. Fixed EQ slots retain their cancelling pole/zero
pairs at zero gain so coefficient movement stays continuous through neutral.

## Live edits and compatibility

The node uses the normal controller update cadence: continuous edits target
one configure request every 100 ms, without postponing indefinitely while the
pointer keeps moving. Requests are serialized and pending edits survive their
acknowledgements. Parameter and bypass transitions are smoothed over 10 ms when
the engine adopts a new target. The dynamics stage also uses its detector and
gain timing above. These times describe control handling and effect behavior,
not measured microphone-to-headphone latency.

Ordinary edits retain filter, dynamics, delayed-copy and room histories.
The effect state continues following live input at Natural or while bypassed,
so restoring Character does not reveal frozen old audio. Processing uses bounded
preallocated state; control validation and plan preparation remain outside the
audio loop. Tone must be finite and in [−1, 1]; the stored Character value must
be finite and in [0, 1]. Invalid settings are rejected.

Copy/paste, configuration copies, saves and loads carry both axes. Existing
graphs remain version 2, keep their names, layout, cables and saved coordinates,
and do not acquire a Voice Color node automatically. Older node records that
omit `voiceColor` receive an unused neutral default. Reset and bypass follow
the same conventions as the other effects.

The Broadcast treatment uses the existing saved coordinates without rewriting
settings or changing the configuration format. Existing Voice Color nodes with
nonzero axes intentionally sound different: the horizontal shaping has changed
and the vertical axis now controls the whole Character treatment. Neutral and
bypass retain transparent behavior. Earlier room-led and mild Broadcast mappings
are historical; their test counts did not establish a convincing audible change
on spoken voice. The current revision adds volume-matched spoken comparisons
to the technical checks.

## Scope and listening

Voice Color does not perform pitch shifting, denoising, de-essing or automatic
echo cancellation. It has no stereo-position control; its delayed double can
add stereo width. Use the separate nodes for noise and sibilance control.
Compression and EQ can bring up room noise or consonants as well as the voice.

Offline checks cover transparency, tonal direction, compression, delayed-copy
and room bounds, rapid edits, validation, persistence, copy/reset/bypass and
detached WPF rendering. See the [current review and evidence](VOICE_COLOR_INTENSITY_REVIEW.md).
They cannot establish whether the result suits a particular voice. Compare the
same spoken phrase at Neutral and with moderate Character, then choose the
Warm–Bright balance. Listen for excessive bass, harsh consonants or distracting
doubling, and adjust the amount accordingly. This is a coordinated voice effect,
not a vocal preset matcher or an Intellipan algorithm clone.
