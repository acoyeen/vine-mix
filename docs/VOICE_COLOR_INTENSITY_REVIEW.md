# Voice Color intensity increase — 18 October 2026

The user requested substantially more intensity after testing the installed
`voice-character-20261018` build. This revision expands the sound range again;
the grid, captions and interaction remain the same.

## Changes

| Maximum setting | Previous build | This revision |
|---|---:|---:|
| Broadcast body shelf, centred Tone | +7.5 dB | +12 dB |
| Broadcast low-mid cut | −4.5 dB | −8 dB |
| Broadcast presence bell, centred Tone | +7 dB | +12 dB |
| Broadcast air shelf, centred Tone | +2 dB | +4 dB |
| Warm/Bright low-shelf range | ±8 dB | ±12 dB |
| Warm/Bright high-shelf range | ±6 dB | ±9 dB |
| Density blend | 20% | 50% |
| Density drive relative to envelope | 1.8 | 2.5 |

These EQ numbers describe individual filter gains, not resulting output volume.
The maximum low shelf reaches +24 dB at Warm/Broadcast, within the existing EQ
range. The Warm extreme uses 6 dB of trim instead of 4 dB. Bright has no global
trim: its bass reduction already lowers typical speech energy, so applying a
second cut there made the upper-right corner unnecessarily quiet.
Effective Character follows `c^1.3`, retaining gentler adjustment toward the
bottom while reaching the full expanded range at the top. All stages use this
same effective amount. Full-Character compression, delayed doubling and room
mixes remain 40%, 12% and 2%; the stronger character comes from the contour
and density rather than a larger room or echo.

Neutral remains exact, and bypass and saved coordinates keep their existing
meaning. The [mapping guide](VOICE_COLOR.md) describes the implementation.

## Verification

The speech comparison uses the immediately preceding installed build as its
baseline, preserved under `artifacts/voice-intensity/previous-male` and
`previous-female`, rather than the earlier mild Broadcast implementation.

At centred Tone and maximum Character, two generated spoken voices gave the
following changes relative to dry input. Spectral contrasts are measured after
matching output RMS to input; this is not a claim of equal perceived loudness.

| Measurement | Male: previous → final | Female: previous → final |
|---|---:|---:|
| Body relative to low mids | 5.37 → 9.78 dB | 4.76 → 8.19 dB |
| Presence relative to low mids | 6.79 → 12.38 dB | 6.64 → 11.89 dB |
| Crest-factor reduction | 2.29 → 5.58 dB | 2.25 → 3.63 dB |
| Raw output RMS change | −2.96 → −2.81 dB | −3.68 → −4.04 dB |

These use clean speech at −24 dBFS whole-clip RMS. Quiet −36 dBFS and previously
compressed speech are also covered. Across all 84 final cases, samples remain
finite and neutral stays exact. All 56 normal-input cases remain unclipped;
28 intentionally overloaded inputs are identified separately in the reports.

The first candidate retained symmetric Tone trim and lost 11.48–12.29 dB at
Bright/Broadcast. It was rejected. Removing the extra global cut on the Bright
side improves final normal-speech RMS change to −6.55/−7.58 dB, compared with
−8.54/−8.77 dB in the installed baseline. Centred Character is unaffected.
Output RMS still varies with the voice and position; this effect does not
normalize loudness or guarantee a peak ceiling.

All 144 focused native checks pass. These cover neutral, bounded density at
quiet levels, finite corner responses, stereo linking, folding to mono, rapid
updates, silence, bypass, state retention and allocation-free processing.
An isolated 300 Hz tone's third harmonic rises from −32.23 to −22.03 dBc at
maximum Character, retaining the stronger signature across a 20 dB input-level
change. This is an intentional harmonic increase, not a percentage of perceived
intensity. The saturation remains non-oversampled.

The desktop executable is unchanged from `voice-character-20261018`; its prior
4,064 checks and 119 renders remain applicable. No desktop input or audio
capture/playback was used in verification.

Final evidence:

- `artifacts/voice-intensity/focused-final-native.txt`
- `artifacts/voice-intensity/candidate-final-male/metrics.json`
- `artifacts/voice-intensity/candidate-final-female/metrics.json`
- `artifacts/voice-intensity/comparison-final.wav`
- `artifacts/voice-intensity/comparison-final-female.wav`
- `artifacts/voice-intensity-final-native/results.json`
- `artifacts/voice-intensity-final-native/build-provenance.json`

Both comparison files play dry, previous and revised speech in that order, with
matched RMS. They were written to files only.

All 12 final native suites pass. The actual engine update test also verifies
rendered output through a synthetic endpoint: 0.3 neutral → 0.598579 Warm →
0.0753566 Bright → 0.3 neutral. Source and engine hashes match final provenance;
the speech fixtures use those same source hashes.

Build `voice-intensity-20261018` was installed and opened at 22:51 AEST.
The installed binaries match the verified package, the controller is responsive,
all 15 previously running paths recovered, and the engine timeline advances.
The saved graph and settings file were unchanged. Activation evidence and the
previous package backup are in
`artifacts/voice-intensity-update-20261018-225123/`; `result.json` reports success.
