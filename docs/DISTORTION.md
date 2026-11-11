# Distortion

Distortion is a self-contained effect in **Add → Effects**. It ranges from
gentle warmth to strong saturation, and it accepts and sends stereo audio like
the other effects. The design is VineMix's own, redesigned from first
principles on 11 November 2026; it does not reproduce any other product.

This page describes the node in the Rust edition. The 0.5 C++ engine keeps the
earlier design, an oversampled `tanh` curve, which the
[effects research](EFFECTS_RESEARCH.md#distortion-refinement-2026-11-06)
describes. [Why it changed](#why-the-design-changed) is at the end.

## Controls

| Control | Range | Default | Result |
|---|---|---|---|
| Drive | 0 to 36 dB | 6 dB | How strongly the sound is saturated. It changes the character, not the loudness |
| Tone | 500 to 18000 Hz | 6000 Hz | Brightness of the driven sound (a first-order low-pass) |
| Mix | 0 to 100% | 50% | Blend of the input and the driven sound |
| Output | −24 to 0 dB | 0 dB | Trims the whole blend, including at Mix 0% |

The standard bottom **Bypass** button passes the input unchanged and
undelayed, without the Output trim. At Mix 0% with Output at 0 dB the output
is also the input, sample for sample.

The default Output was −6 dB in the earlier design, where Drive made the sound
louder. It is 0 dB now. Saved nodes keep the value they were saved with.

## What the node does

Configuration stores `distortion: { driveDb, toneHz, mix, outputDb }`. There
are no hidden settings. Processing runs at 48 kHz, per frame, in this order.
Nothing is delayed: there is no oversampling and no lookahead.

### 1. The band that is driven

A first-order split at 5 kHz takes the sound below it for driving. What lies
above it passes clean, so sibilants are not torn into noise and consonants
stay clear over a saturated voice.

Inside that band the lows are turned down by half (6 dB) before the curve,
from 300 Hz down to 150 Hz, and restored exactly afterwards. The fundamental
of a voice then modulates the rest of the voice less, and low sounds collect
fewer harmonics than the rest.

### 2. A standard level

The node learns the level of the driven band: its mean power while it sounds.
Anything above −70 dBFS and within 20 dB of the level so far counts. The level
is learned in the first quarter of a second of sound and then averaged over
about 2.5 seconds. The band is brought to a standard level (−22 dBFS) before
the curve and taken back afterwards.

Drive therefore means the same thing at any input level. A quiet microphone
and a hot one, at the same Drive, get the same saturation.

- A source that becomes much louder (15 dB or more above the learned level)
  is taken up at once, so speech after a wait in a quiet room starts as it
  would mid-conversation.
- A source that drops for good is followed down. In the test, a voice that
  fell 20 dB was within 1.5 LU of its settled treatment 24 seconds later.
- For a quarter of a second from the first sound after the node is created
  or reset, it moves from the input's own level to the learned one.

### 3. The curve

The curve is `v / sqrt(1 + v²)` about a working point of 0.25, moved and
scaled so that it passes through zero with a slope of one. It is smooth and
monotonic, and it levels off at about +0.83 and −1.36. The two half-waves are
treated differently, so gentle settings add mostly second harmonic; the third
catches up at about Drive 20 dB and dominates above that.

The curve is applied as its exact average between each sample and the one
before it (first-order antiderivative antialiasing). For this curve the
average has a closed form with no division by a small difference, so it needs
no special cases. This suppresses aliasing without oversampling and without a
delay line.

On a steady 997 Hz tone at Mix 100%, at any input level:

| Drive | 0 dB | 6 dB | 12 dB | 18 dB | 24 dB | 36 dB |
|---|---:|---:|---:|---:|---:|---:|
| Second harmonic (dBc) | −35 | −29 | −24 | −21 | −21 | −30 |
| Third harmonic (dBc) | −60 | −48 | −36 | −25 | −17 | −11 |
| All harmonics (dB) | −35 | −29 | −23 | −19 | −15 | −10 |

### 4. Loudness

After the curve a makeup gain returns the driven band to the level it came
in at. Because the curve always sees the same standard level, that gain
depends on Drive alone, and it is worked out when the configuration is
compiled: the gain that returns a speech-like signal (a Laplace distribution
of sample values) at the standard level to its own level.

| Drive | 0 dB | 6 dB | 12 dB | 18 dB | 24 dB | 36 dB |
|---|---:|---:|---:|---:|---:|---:|
| Makeup (dB) | +0.1 | −5.7 | −10.9 | −15.1 | −18.1 | −21.3 |
| Gain for sound well below the level (dB) | +0.1 | +0.3 | +1.1 | +2.9 | +5.9 | +14.7 |

Saturation holds the loud parts and so lifts the quiet ones; the second row
is by how much. That lift is withheld from sound far below the learned level:
it is complete down to 30 dB below, and gone at 45 dB below. Room noise
between words is therefore not raised, at any Drive. Nothing is driven before
the node has heard a sound above −70 dBFS.

On the test voices the driven sound lies between 0.1 and 1.0 LU below the dry
loudness at every Drive (Tone at its default, Mix 100%).

### 5. Tone, Mix, ceiling and Output

- **Tone** is a first-order low-pass on the driven sound.
- **Mix** blends the input itself with the driven sound. The driven sound is
  the input plus what driving changed, not a delayed copy, so the blend does
  not comb-filter: for quiet sound at Mix 50% the response is flat within
  0.2 dB to 8 kHz with Tone fully open.
- **Ceiling.** The node never adds a peak above −1 dBFS. If its processing
  would, the gain is reduced at once and recovers over 80 ms. Where the input
  itself is hotter than −1 dBFS, the limit is the input's own recent peak.
  For ordinary speech this does not act: the driven sound's peaks are below
  the input's. It is a safety net, not a substitute for a Limiter node.
- **Output** trims the result.

## Live edits and compatibility

Each control glides to its new value over 10 ms. Drive and its makeup glide
together in decibels, so a change across the whole range in one step stays
within about 5 dB of the settled level on the way. Filter state and the
learned level carry across edits and keep following live input while the node
is bypassed. Reset, or removing and re-adding the node, starts the learning
again. Processing uses under a kilobyte of fixed state and never allocates;
one node costs about 0.13% of one core in the offline benchmark (about 28 ns
per frame on the development machine; the earlier design measured 160 to
200 ns there).

Drive must be in [0, 36] dB, Tone in [500, 18000] Hz, Mix in [0, 1] and
Output in [−24, 0] dB. The configuration format is unchanged and nothing is
migrated. Existing Distortion nodes intentionally sound different from the
earlier design, and one saved with a low Output to offset the old node's gain
will now be quieter than its input; raise Output.

The earlier design delayed its output by 32 frames (0.67 ms), bypassed or
not. This one adds no delay, so a route through it lines up with a parallel
route around it.

## Limits

- **Aliasing at extreme settings.** What is neither the tone nor one of its
  harmonics measures, relative to the tone:

  | Tone | Drive 12 dB | 18 dB | 24 dB | 36 dB |
  |---|---:|---:|---:|---:|
  | 220 Hz | −101 | −91 | −85 | −81 |
  | 997 Hz | −101 | −92 | −85 | −61 |
  | 3 kHz | −101 | −91 | −65 | −36 |
  | 5 kHz | −91 | −66 | −48 | −34 |

  Voices have little steady energy in the bottom-right corner of that table; a
  high, pure tone driven to the top of the range will alias audibly. The
  earlier design, with eight-times oversampling, stayed below −110 dB almost
  everywhere (−75 dB at 5 kHz, Drive 36 dB). This is the one measurement on
  which the new design is worse.
- **Noise close to the voice.** Noise within about 30 dB of the voice's level
  is treated as part of the sound and is lifted with it at high Drive
  (+6 dB at Drive 36 dB). Use a Gate or Denoiser before the node.
- **Loudness is matched for speech.** A steady tone at the same Drive comes
  out up to about 1.5 dB louder than it went in. Music has not been measured.
- **A long silence.** By design, and not separately tested: after a minute or
  two of room noise alone (above −70 dBFS), the node takes the room for the
  source. The noise is then driven, at its own loudness; the next loud sound
  is taken up at once.
- **Stereo.** The level is shared by both channels and the curve is applied
  to each, so strong saturation narrows a level difference between them.

## Tests and listening

`cargo test -p vinemix-dsp --test distortion` holds the behaviour above: the
input unchanged at Mix 0% and in Bypass, no delay, loudness within 1.5 LU of
dry for two test voices at six Drive settings, the same harmonics at input
levels 42 dB apart, harmonics that grow with Drive, mostly second harmonic at
gentle settings, the aliasing figures above, room noise left alone after
speech and before the first sound, a clean start after a quiet wait,
following a 20 dB drop and rise, the peak ceiling, linked channels, silent
output for silent input, the Tone response, the Output trim, a change of
Drive across its whole range in one step, and smooth output while every
control moves each 5 ms. Allocation and edit-path checks
are in `realtime_alloc` and `retarget_parity`.

Those tests use synthetic signals. They establish what the node does, not
whether it suits a voice. To hear it:

```powershell
.\scripts\render-distortion.ps1
```

writes, for every 48 kHz WAV file in `artifacts\distortion\source` (synthetic
speech is generated there if it is empty), the node's output at six settings,
a copy of each matched to the dry loudness, and a loudness table. Put a
recording of your own voice in that folder to hear the node on it. The folder
is ignored by Git; keep personal recordings out of the repository. Nothing is
played and no audio device is opened.

## Why the design changed

Measured on the same signals (two synthetic speech voices from the Windows
speech engine, two generated test voices and steady tones), at Mix 100% and
Output 0 dB:

| | Earlier design | This design |
|---|---|---|
| Loudness against dry, speech at −24 dBFS | +5.4 LU at Drive 6 dB, +22 LU at 36 dB | 0.1 to 1.0 LU quieter at every Drive |
| The same voice 20 dB quieter, Drive 36 dB | +33.5 LU | within 1 LU of dry |
| Harmonics at Drive 12 dB, tone at −6 and at −36 dBFS | −15 dB and −70 dB | −23 dB at both |
| Highest peak, speech with peaks at −7 dBFS, Drive 36 dB | +4.2 dBFS | −12.9 dBFS |
| Room noise 40 dB below the voice, Drive 36 dB | raised by 34 dB | not raised |
| Harmonics at gentle settings | odd only | mostly second |
| Delay | 32 frames, also when bypassed | none |
| Aliasing | below −110 dB almost everywhere | see Limits |
| Cost per frame | 160 to 200 ns | about 28 ns |

In the earlier node Drive was a gain into a fixed curve. The amount of
distortion therefore depended on the level of the microphone as much as on
the dial, the result grew louder by nearly as much as the dial was turned,
and its peaks could pass full scale. Output was there to turn it back down by
hand. Its oversampling filters kept aliasing very low at eight times the
processing and a fixed delay. These are measurements and design observations.
The maintainer's listening comparison of the two is still to come; the
figures do not say which sounds better.
