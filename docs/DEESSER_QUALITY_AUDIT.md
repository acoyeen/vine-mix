# De-esser and plosive quality audit

18 October 2026. All measurements below use generated signals in memory. No
audio device, microphone recording, desktop input, personal settings or running
engine was accessed by this audit. Listening quality on the user's voice remains
a separate evaluation; passing these tests is not a listening assessment.

## Finding

The original de-esser was processing audio, but its first-order split imposed a
substantial reduction limit near the selected frequency. Its output was
`low + attenuation × high`, where the nominally unaffected low band still
contained much of the high-frequency signal. At the crossover, the magnitude
was `sqrt((1 + attenuation²) / 2)`. Even setting attenuation to zero therefore
could not cut the crossover frequency by more than 3.01 dB. This explained why
increasing Maximum reduction often produced little further change. The plosive
processor used the mirrored arrangement and had the same limitation.

The existing tests looked for a broad amount of reduction using a loud 10 kHz
tone with a 4 kHz crossover. That demonstrated activity, but missed the
near-crossover limit, quiet-consonant behavior and post-consonant recovery.

There was a second, independent setup issue: the default −24 dBFS threshold is
an absolute detector threshold. If high-frequency consonants stay below it, the
correct output is unchanged. An upstream denoiser can further reduce the energy
that triggers it. No fixed threshold works equally well for every microphone
level. This is why an actual detector/reduction readout matters. FabFilter's
official documentation likewise distinguishes threshold, maximum reduction and
frequency selection, and uses detector metering to help set the threshold.
[FabFilter basic controls](https://www.fabfilter.com/help/pro-ds/using/basiccontrols),
[FabFilter metering](https://www.fabfilter.com/help/pro-ds/using/metering).

## Correction under test

The replacement uses a separate second-order Butterworth detector and a dynamic
second-order shelving filter. The shelf uses the topology-preserving state
variable formulation described by Andrew Simper. It is exactly dry at zero
reduction, has no added sample buffer, and can approach the requested reduction
inside the affected frequency range. Frequency now denotes the shelf midpoint:
when the full 12 dB range is active, approximately 6 dB applies at that frequency.
The selected range describes the shelf's plateau, not attenuation of every
frequency in the entire output signal.
[Andrew Simper, Cytomic, state variable filter derivation](https://cytomic.com/files/dsp/SvfLinearTrapOptimised2.pdf).

The detector remains stereo linked so a consonant does not pull the stereo image
to one side. It uses 0.5 ms attack and 20 ms release. Gain uses 1 ms attack and
50 ms release. Separate detector and gain smoothing avoid abrupt switching; the
shorter release also avoids the old pair of 80 ms release stages holding down
subsequent quiet treble for several hundred milliseconds. There is no lookahead.
Frequency and threshold controls still transition over 10 ms.

Band-selective dynamics is an established de-essing approach. This implementation
does not claim a speech/sibilance classifier: cymbals or other loud treble can
also trigger it. Plosive remover similarly reduces loud low-frequency energy,
including sustained bass; it does not identify consonants semantically.
[MathWorks split-band de-essing example](https://www.mathworks.com/help/audio/ug/SplitBandDeEssing.html).

## Measured before and after

Positive numbers in these tables mean attenuation. The test source is
`native/tests/deesser_quality_tests.cpp`. Baseline used the previously built
effect library; the corrected run compiled the updated production source
directly. Both used 48 kHz stereo and the same generated samples.

### Maximum-range response

Settings: 6 kHz selected frequency, −60 dBFS threshold, 24 dB maximum reduction.
Input tone peak: 0.2 (−13.98 dBFS). Values measured over the settled final half of
a one-second tone.

| Tone frequency | Original attenuation | Corrected attenuation |
|---|---:|---:|
| 1 kHz | 0.107 dB | 0.008 dB |
| 3 kHz | 0.897 dB | 2.640 dB |
| 6 kHz | 2.993 dB | 12.000 dB |
| 8 kHz | 4.654 dB | 16.913 dB |
| 10 kHz | 6.407 dB | 20.320 dB |
| 12 kHz | 8.244 dB | 22.345 dB |
| 16 kHz | 12.376 dB | 23.781 dB |
| 20 kHz | 17.931 dB | 23.990 dB |

The 3 kHz result also shows a practical limit: an extreme 24 dB shelf cut has a
transition region. It should not be interpreted as a brick-wall boundary that
leaves everything below 6 kHz untouched. At normal reductions this transition
is gentler; the 200 Hz preservation test stays within 0.1 dB even at maximum cut.

### Noise-like consonants mixed with a voiced body

An independent 129-tap FIR produces and measures a seeded 6–12 kHz noise burst.
The voiced body contains 180, 360 and 720 Hz harmonics. The noise burst lasts
500 ms; the measurement uses the final 250 ms. These are synthetic fixtures,
not recorded speech or a claim of speech intelligibility.

Selected frequency remains 6 kHz; maximum reduction is 9 dB.

| Consonant-band RMS | Threshold | Original attenuation | Corrected attenuation |
|---|---:|---:|---:|
| −32.04 dBFS | −24 dBFS | 0.000 dB | 0.000 dB |
| −32.04 dBFS | −30 dBFS | 1.701 dB | 1.614 dB |
| −32.04 dBFS | −36 dBFS | 3.689 dB | 5.251 dB |
| −26.02 dBFS | −24 dBFS | 1.671 dB | 1.626 dB |
| −26.02 dBFS | −30 dBFS | 3.672 dB | 5.263 dB |
| −26.02 dBFS | −36 dBFS | 4.198 dB | 7.236 dB |
| −20.00 dBFS | −24 dBFS | 3.672 dB | 5.276 dB |

The body fundamental stays within 0.1 dB in every case (measured deviations round
to approximately 0.000 dB after the correction). Results are deliberately not
presented as “always stronger”: a mildly triggered fixture can remain similar.
The fix removes the ceiling; the threshold must still be appropriate.

### Release and plosive behavior

A 50 ms, 10 kHz burst at −12 dBFS peak precedes quiet treble. Settings are a
6 kHz midpoint, −36 dBFS threshold and 12 dB range. At 200 ms after the burst,
remaining attenuation improves from **3.569 dB to 0.246 dB**. The initial
high-amplitude transition is excluded from this recovery measurement because
filter ringing can temporarily be large relative to the much quieter following
probe. The probe prints it rather than hiding it.

The mirrored plosive shelf passes the same midpoint, range, transparency,
monotonicity and channel-linkage criteria. At default settings and 0.2 peak
input, a 60 Hz tone changes from **5.063 dB to 7.072 dB** attenuation, while a
1 kHz tone is unchanged. Plosive cleanup cannot repair an input already clipped
at the microphone or recover speech overwritten by a physical burst of air.

## Verification scope

The corrected run passes **62 assertions**, including:

- Maximum-range response at the selected frequency and inside the target band.
- Monotonic effect of lowering Threshold and increasing Maximum reduction.
- Below-threshold and zero-range transparency.
- Voice-body preservation during noise-like consonants.
- Stereo proportionality and preservation of opposite-band voice in the other
  channel when only one channel triggers the detector.
- Finite, positive, bounded gain-reduction telemetry and usable detector levels.
- Full-span live parameter edits on continuous material, with bounded output.
- Exact silence and cleared recursive state after recovery/reset.
- Recovery of quiet treble after a consonant burst.

Full-span edits produced peak amplitudes of 0.237 for de-esser and 0.226 for
plosive on a signal bounded by 0.25. This is a regression fixture rather than a
general proof that all time-varying filters are peak preserving. The original
implementation failed five of the original 50 quality assertions. The extra
12 assertions cover the new telemetry and dynamic-edit behavior.

Unqualified areas: recorded-voice listening comparisons, perceptual artifacts
on unusual voices, interaction with Discord processing, and hardware latency.
No real audio was recorded or played by this suite.
