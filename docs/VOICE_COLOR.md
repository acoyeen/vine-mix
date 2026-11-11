# Voice Color

Voice Color is a self-contained effect in **Add → Effects**, also available by
searching its name. It accepts and sends stereo audio like the other effects.
Its two-axis pad shapes a spoken voice: tonal balance across, and a fuller,
steadier broadcast delivery upward. The design is VineMix's own. It was first
built to recall Voicemeeter's voice pad and has since been redesigned from
first principles (11 November 2026); it does not reproduce Intellipan or any
other product, and it does not match a particular announcer's voice.

This page describes the node in the Rust edition. The 0.5 C++ engine keeps the
earlier design, which the [historical reviews](VOICE_COLOR_INTENSITY_REVIEW.md)
describe. [Why it changed](#why-the-design-changed) is at the end.

## Controls

| Direction | Control | Result |
|---|---|---|
| Left → right | Tone: Warm → Bright | Tilt the voice between fuller lows with softer highs and the reverse |
| Bottom → top | Character: Natural → Broadcast | Add body and presence, clear the low mids and rumble, steady the level and add density |
| Bottom centre | Neutral | The output is the input, sample for sample |

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
axis. Arrow/Page keys adjust a focused dial and Home resets it. The pad, labels
and dials stay visible and scale with the rest of the node.

The standard bottom **Bypass** button is grey when processing, red when
bypassed. It retains the pad position and uses the normal effect bypass
behavior. **Right-click → Reset node to defaults** resets both axes and bypass
while preserving the node's name, position and cables. There is no Level
control: the node keeps the voice at about its own loudness (see below). Use a
Mix or Gain node to change volume.

## What the node does

Configuration stores `voiceColor: { tone, ambience }`. Tone `t` spans −1 to
+1. The stored `ambience` value, called Character `c` in the interface, spans
0 to 1. Both default to zero. There are no hidden settings.

Processing runs at 48 kHz, per frame, in this order. Nothing is delayed: there
is no lookahead and no oversampling.

### 1. Contour

Seven fixed filters, each mixed in by a gain that follows the pad. Because the
filters themselves never move, the pad can be dragged at any speed without
zipper noise or unstable intermediate filters.

| Stage | Filter | Gain at the edge of the pad |
|---|---|---|
| Rumble | Second-order high-pass, 75 Hz | Fully in from Character 35% upward |
| Tone, lows | First-order shelf, 220 Hz | +6 dB at Warm, −5 dB at Bright |
| Body | Bell, 165 Hz, Q 0.8 | +7 dB at full Character |
| Low mids | Bell, 420 Hz, Q 0.9 | −5 dB at full Character |
| Presence | Bell, 3.3 kHz, Q 0.75 | +7 dB at full Character |
| Tone, highs | First-order shelf, 3.6 kHz | +6 dB at Bright, −5 dB at Warm |
| Air | First-order shelf, 9 kHz | +5 dB at full Character |

Gains scale in decibels with the pad: half Character is half of each figure.
The stages overlap, so the combined response is gentler than the list suggests.
Before the loudness trim it is:

| Hz | 30 | 80 | 165 | 420 | 1 k | 3.3 k | 8 k | 16 k |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Character 50% | −15.8 | −0.9 | +2.9 | −1.2 | +0.6 | +3.8 | +2.5 | +2.4 |
| Character 100% | −15.2 | +1.5 | +6.2 | −1.5 | +2.2 | +7.8 | +5.9 | +5.1 |
| Warm | +5.9 | +5.6 | +4.6 | +2.1 | +0.3 | −1.6 | −3.8 | −4.8 |
| Bright | −4.8 | −4.0 | −2.5 | −0.5 | +0.7 | +3.7 | +5.5 | +5.9 |
| Warm, Character 100% | −9.3 | +7.1 | +10.9 | +0.6 | +2.5 | +6.3 | +2.2 | +0.2 |
| Bright, Character 100% | −20.0 | −2.6 | +3.7 | −2.0 | +2.8 | +11.5 | +11.4 | +11.0 |

**Sibilance guard.** The boosts above the vowels (presence, the Bright shelf
and air) are relaxed while a sibilant is sounding. The node watches how much of
its input lies above 5.5 kHz: below a quarter, the boosts apply in full; above
about half, they are removed, within a millisecond, and return over about
15 ms. Cuts are never altered. Brightening therefore lifts the vowels without
sharpening "s" and "sh" by the same amount. This is not a de-esser: it removes
the node's own boost and never cuts below the input.

### 2. Level

Character steadies the delivery. The node measures the voice against the
speaker's own level, not against a fixed threshold, so it behaves the same at
any microphone gain.

- **The speaker's level** is the average power of the voice while they are
  talking. It is learned in the first quarter of a second of speech and then
  averaged over about 2.5 seconds. It follows a louder voice within about a
  second and a quieter one over about ten.
- **The noise floor** is tracked separately: it drops to any quieter moment
  within 50 ms and creeps up over seconds, so it settles on the room between
  words. Only sound at least 12 dB above it, and above −60 dBFS, counts as
  speech for the level.
- **The curve** at full Character is a 3.5:1 compression above 8 dB below the
  speaker's level (10 dB soft knee), with make-up set so that a sound 6 dB
  above the speaker's level leaves at the level it came in. The loudest words
  are held and everything quieter rises, by up to 10 dB. Half Character is
  half of that, in decibels.
- **What is not lifted.** The lift fades out for sound more than 20 dB below
  the speaker's level and is gone by 40 dB below. It also fades out within
  12 dB of the noise floor and is gone within 3 dB. Pauses, the tail of the
  room and background noise are not turned up.
- **Timing.** The detector rises in 1 ms and falls in 25 ms; the gain then
  moves down in 3 ms and up in 90 ms. Both channels share one detector and one
  gain. The air band is weighted up in the detector, so a sibilant counts as
  louder than it is and is lifted less than a vowel of the same level.

### 3. Density

A small amount of second and third harmonic is generated from the part of the
voice below about 1.4 kHz and added back, scaled by Character. It rounds the
largest peaks and thickens the lower voice. The amount follows the speaker's
level, like the dynamics, so it does not depend on microphone gain. Because
only the band below the consonants is shaped, the harmonics stay well below
half the sample rate and do not fold back as aliasing; no oversampling is
needed. On a steady 250 Hz tone at full Character the second, third and fourth
harmonics measure about 32, 28 and 37 dB below the fundamental.

### 4. Loudness and ceiling

- **Loudness trim.** Each pad position has an output trim, calculated from the
  contour and a typical long-term speech spectrum when the configuration is
  compiled, that returns ordinary speech to about its dry loudness. The pad
  changes the voice's colour and density, not its volume. On the test voices
  the result lies between about 1.5 LU quieter and 2 LU louder than dry
  everywhere on the pad.
- **Ceiling.** The node never adds a peak above −1 dBFS. If its processing
  would, the gain is reduced at once and recovers over 80 ms. Where the input
  itself is hotter than −1 dBFS, the limit is the input's own recent peak, so
  the node never makes a signal peak higher than it arrived. This has no
  lookahead; it is a safety net, not a substitute for a Limiter node.

At Neutral every stage contributes exactly zero and the output equals the
input. With Tone only (Character at Natural) the dynamics, density and rumble
filter are off.

## Live edits and compatibility

The node uses the normal controller update cadence. Each amount glides to its
new value over 10 ms when the engine adopts a new position. Filter history,
the learned level and the gain carry across ordinary edits, and keep following
live input at Natural or while bypassed, so restoring Character does not
reveal stale audio. Reset, or removing and re-adding the node, starts the
learning again. Processing uses under a kilobyte of fixed state and never
allocates; one node costs about 0.2% of one core in the offline benchmark
(about 40 ns per frame on the development machine; the earlier design
measured about 120 ns there).

Tone must be finite and in [−1, 1]; the stored Character value must be finite
and in [0, 1]. Invalid settings are rejected. Copy/paste, configuration
copies, saves and loads carry both axes. The configuration format and the
saved coordinates are unchanged: nothing is migrated or rewritten. Existing
Voice Color nodes away from Neutral intentionally sound different from the
earlier design. Neutral and Bypass are transparent, as before.

A mono voice stays mono through the node. The earlier design's delayed double
and room turned it into stereo; neither exists now. Use the Reverb node for a
room.

## Scope and listening

Voice Color does not shift pitch, remove noise or cancel echo, and it has no
stereo-position control. Its sibilance guard only withholds its own boosts;
use the De-esser for sibilance that is already too strong. Its dynamics follow
one speaker's level; a second, much quieter voice in the same microphone will
be lifted toward the first.

`cargo test -p vinemix-dsp --test voice_color` holds the behaviour above:
exact transparency at Neutral and in Bypass, the published filter responses
(within 0.1 dB), loudness within 2.5 LU of dry at fifteen pad positions for
two test voices, the same treatment 20 dB quieter, a narrower spread of
loudness, quiet words rising while loud words hold, an unraised room between
words, sibilants that do not rise with the presence, the peak ceiling, the
absence of aliasing and of delay, linked channels, silent output for silent
input, smooth output while the pad moves every 5 ms, and relearning after a
20 dB drop. Allocation and edit-path checks are in `realtime_alloc` and
`retarget_parity`.

Those tests use synthetic speech-like signals. They establish what the node
does, not whether it suits a voice. To hear it:

```powershell
.\scripts\render-voice-color.ps1
```

writes, for every 48 kHz WAV file in `artifacts\voice-color\source` (synthetic
speech is generated there if it is empty), the node's output at seven pad
positions, a copy of each matched to the dry loudness, and a measurement
table. Put a recording of your own voice in that folder to hear the node on
it. The folder is ignored by Git; keep personal recordings out of the
repository. Nothing is played and no audio device is opened.

## Why the design changed

Measured on the same files (two synthetic speech voices from the Windows
speech engine and two generated test voices), at full Character and centred
Tone:

| | Earlier design | This design |
|---|---|---|
| Loudness against dry | 5.0 to 6.9 LU quieter | 0.2 LU quieter to 2.2 LU louder |
| Spread of loudness on a voice with 15 dB between syllables | 9.2 → 7.4 dB | 9.2 → 6.1 dB |
| Sibilants against the voice | 3 to 6 dB sharper | within 1 dB |
| Same treatment 20 dB quieter | No: a fixed −28 dBFS threshold | Yes |
| Mono voice | Becomes stereo; colour changes when summed to mono | Stays mono |
| Cost per frame | about 120 ns | about 40 ns |

The earlier node grew quieter as Character rose, which made a stronger setting
sound weaker. Its compressor had a fixed threshold, so it did little on a
quiet microphone and squashed a hot one. Its "double" was two fixed delays,
which is a comb filter rather than a second voice. Its density stage shaped
the full band without oversampling, and its room was too quiet to hear but
cost most of the node's processing. These are measurements and design
observations. The maintainer's listening comparison of the two is still to
come; the figures do not say which sounds better.
