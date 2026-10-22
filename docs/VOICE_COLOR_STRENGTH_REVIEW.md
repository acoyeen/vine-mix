# Voice Color strength and grid review — 18 October 2026

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](BETA_TESTING_CURRENT.md) for the active release checks.

The previous Broadcast treatment remained too subtle on the user's isolated
input → Voice Color → output chain. The user confirmed that Character reached
100% and muting the output silenced the monitored voice. Source tracing found
no lost parameter update or alternate path; the processing needed a stronger
and better-tested voice treatment.

## Result

The revised node combines a stronger body/presence contour and low-mid cut with
bounded parallel compression, signal-relative density and short delayed doubling.
Warm/Bright travel is wider. The Natural/centred position remains exactly
transparent. The [mapping guide](VOICE_COLOR.md) documents the coefficients,
envelope timing, smoothing and compatibility behavior.

The pad restores a square quarter-grid. Larger semibold Broadcast, Warm,
Natural and Bright labels sit inside the surrounding well, clear of handle
travel. There is no visible Neutral label or duplicate coordinate readout.
The small reset diamond, exact numeric controls, keyboard controls, fine drag
and reset gestures remain available.

## Evidence

The final native suite passed all 12 suites, including 144 focused Voice Color
assertions. Checks cover exact neutral, tonal extremes, low-level density,
stereo linking, silence, bypass, rapid changes, state retention and bounded
allocation-free processing. The actual Engine configuration test also feeds
synthetic audio through Voice Color and checks output after Warm, Bright and
reset updates, while retaining workers. This verifies the full engine update
path in addition to the individual processor.

Two locally generated spoken voices were processed through the production
GraphProcessor, including quiet input and already-compressed input. Across 84
final cases all samples were finite and neutral was exact. All 56 normal-input
cases remained unclipped. The other 28 cases deliberately overload the input
and are identified separately in the metrics.

At centred Tone and full Character, the male fixture's body-to-low-mid contrast
increased by 5.37 dB and presence-to-low-mid contrast by 6.79 dB after matching
RMS. Crest factor fell 2.29 dB. The female holdout showed 4.76 dB, 6.64 dB and
2.25 dB respectively. This demonstrates a stronger spectral and density change;
difference-energy percentages are not interpreted as perceived effect strength.

An initial stronger candidate lost about 10.7 dB on the male fixture and was
rejected. The final parallel blend avoids that large loss: ordinary full-Character
speech was about 3–3.7 dB quieter, with voice-dependent variation. The node is
not an automatic loudness normalizer. The comparison export matches RMS across
dry, previous and revised versions so added loudness cannot explain the change.
RMS matching does not guarantee equal perceived loudness.

Evidence paths, relative to the repository:

- `artifacts/voice-character-final-native/results.json`
- `artifacts/voice-character-final-native/build-provenance.json`
- `artifacts/voice-character-speech/README.md`
- `artifacts/voice-character-speech/candidate-final/metrics.json`
- `artifacts/voice-character-speech/candidate-final-hazel/metrics.json`
- `artifacts/voice-character-speech/dry-previous-revised.wav`
- `artifacts/voice-character-ui-review/results.json`

The comparison WAV contains dry speech at 0.00 seconds, the previous effect at
11.73 seconds and the revised effect at 23.46 seconds. No microphone was captured
and no audio was played automatically. These are offline measurements and
render checks, not a subjective audition of the user's own voice. The nonlinear
density stage is not oversampled; no alias-free or true-peak claim is made.

## Desktop and activation

The final desktop build passed 4,064 checks and produced 119 detached renders
with zero binding errors. Visual inspection covered the neutral position,
Warm/Broadcast and Bright/Natural corners, the full node, high contrast and
144 DPI. The quarter-grid, inside captions and handle clearance were confirmed.

Build `voice-character-20261018` was installed and opened at 22:33 AEST.
Both installed executable hashes match the verified candidate. The controller
opened a responsive window, all 15 previously running paths recovered, the
engine frame counter advanced, and the saved settings file and configuration
were unchanged. Evidence and the previous package backup are in
`artifacts/voice-character-update-20261018-223336/`; `result.json` reports success.
