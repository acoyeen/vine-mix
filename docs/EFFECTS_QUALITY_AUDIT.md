# Effect quality audit — 18 October 2026

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](BETA_TESTING_CURRENT.md) for the active release checks.

This audit uses generated, memory-only audio at 48 kHz. It does not open an audio
device, record a microphone, change personal settings, or play a test signal.
`native/tests/effects_quality_tests.cpp` reads the processor output **before**
physical-output saturation so clipping cannot hide a defect. The executable
reports measured values as well as assertions. The de-esser and plosive-remover
investigation is covered separately by `deesser_quality_tests.cpp`.

## Findings and corrections

The EQ, tone controls, high/low-pass filters, compressor, Balance and Mono
performed their declared operations. The denoiser suppresses stationary noise
but has material speech-processing limitations. The original reverb had excessive
wet gain; its delay network has been corrected.

### Reverb: excessive gain corrected

The old fully wet reverb amplified a 30 Hz tone by **14.81 dB** at default decay
and damping, and a 1 kHz tone by **10.53 dB**. With a five-second decay, a 40 Hz
input whose peak was 0.5 produced a peak of **8.727** before output saturation.
Its feedback combs were not normalized, and its four diffusion stages used the
Freeverb approximation, which is not a unity-gain allpass. The distinction is
documented by Julius O. Smith's [Freeverb allpass derivation](https://www.dsprelated.com/freebooks/pasp/Freeverb_Allpass_Approximation.html).

The replacement retains eight parallel stereo delay paths and four diffusion
stages, but uses passive scattering sections and true Schroeder allpasses. The
implementation is original code based on these standard structures. Allpass
diffusion disperses reflections without introducing frequency-dependent gain;
see [Schroeder allpass sections](https://www.dsprelated.com/freebooks/pasp/Schroeder_Allpass_Sections.html)
and the [allpass transfer-function derivation](https://www.dsprelated.com/freebooks/pasp/Allpass_Two_Combs.html).

For a delay-path feedback coefficient `g`, use `c = sqrt(1 − g²)`, delayed
low-pass output `d`, and current input `x`:

```text
delay input = c*x + g*d
path output = c*d − g*x
```

If `L(z)` is the passive one-pole damping filter, the path transfer is
`(L(z) z^-D − g) / (1 − g L(z) z^-D)`. Since `|L(z)| ≤ 1` and `0 < g < 1`, its
magnitude cannot exceed one. The eight paths are averaged. The stereo input
matrix and subsequent true allpasses also do not increase steady energy. This
is a fixed-parameter frequency/energy bound, **not an instantaneous peak ceiling**:
phase changes and transients still require normal input headroom. No hard clipper
was added inside the effect to conceal overload.

| Measurement | Original | Corrected |
|---|---:|---:|
| Default fully wet gain, 30 Hz | +14.81 dB | −4.95 dB |
| Default fully wet gain, 1 kHz | +10.53 dB | −1.59 dB |
| 1.2 s decay, no damping, impulse energy relative to input | +10.89 dB | −1.33 dB |
| 5 s decay, 40 Hz input peak 0.5, maximum output sample | 8.727 | 0.627 |

The fixed-parameter sweep tests 35 simultaneous frequencies from 1 Hz to 23 kHz
at four decay values (0.1, 0.7, 1.2 and 5 seconds) and three damping values
(0%, 50%, 100%). Every measured transfer is within 0.02 dB of the unity upper
bound; the highest observed numerical result is effectively 0 dB. These samples
support the analytical bound; a finite sweep alone is not proof for every
frequency.

The wet onset remains the user pre-delay plus **25.3125 ms** of initial-reflection
delay, verified at 0, 15 and 100 ms user pre-delay. Zero mix and bypass remain
exactly dry and immediate. Default stereo impulse-tail correlation is about
0.04, showing decorrelation rather than duplicated mono; the active tail has
99.9% occupied samples in the 100–500 ms window. Recovery produces exact silence
for six seconds. During extreme live changes of decay, damping, mix and pre-delay,
input peaks at most 0.2 produced a maximum measured output peak of 0.231.

Decay remains a **nominal feedback-decay control**, not a promise of acoustic
RT60. For undamped impulse tests, nominal 0.1/0.7/1.2/5 seconds produce total
energy-decay times near 0.24/0.66/1.02/3.45 seconds. The short setting has a floor
from the initial reflection and diffusion network. Early reflections, averaging,
and damping affect this measurement. Changing pre-delay is smoothed but can
briefly change pitch; these checks do not certify the subjective texture of a
live parameter drag or that the room sounds natural on every source.

### EQ, Low/Mid/High and pass filters

- Flat EQ and tone controls reproduce the input within `1e-7` sample amplitude.
- Bell center gains agree with their requested gain within 0.02 dB for −24,
  −12, +6 and +24 dB at 40 Hz, 1 kHz, 12 kHz and 19 kHz.
- A 1 kHz Butterworth high-pass and low-pass both measure −3.0103 dB at cutoff.
  At one decade into their stop bands they measure −40.03 and −42.74 dB;
  bilinear-transform warping explains the high-frequency difference.
- Shelf endpoints, a deep notch, and the fixed tone control bands behave as
  specified. A +12 dB, 1 kHz bell test reports harmonic distortion below −100 dB
  (about −159 dB in this synthetic floating-point fixture).

These measurements check the implemented [RBJ biquad formulas](https://www.w3.org/TR/audio-eq-cookbook/).
High Q can intentionally ring or resonate; boosting several bands can exceed
full scale, so the absence of a generic effect-level slider does not remove the
need for headroom upstream.

### Compressor

Settled constant-level tests match the declared static compression curve at
three thresholds and three ratios. Ratio 1 with zero make-up gain and signals
below threshold are transparent. Stereo-linked gain preserves channel ratios
and polarity.

The current detector calculates an instantaneous peak-based gain target and
then smooths the gain with attack/release. It is not a brick-wall limiter or a
lookahead processor. For a 1 kHz sine with peak 0.5, threshold −18 dB, ratio 4,
10 ms attack and 120 ms release, the observed gain reduction is **7.72 dB**;
the peak-only static curve would predict **8.98 dB**. This 1.26 dB difference is
the existing detector's waveform-dependent behavior, not an unconnected
control. Distortion is about **−66 dB** in that fixture. The topology has not
been silently redesigned as part of this audit.

### Denoiser

The zero-reduction path has a measured residual of −70.66 dB relative to seeded
noise after aligning the **960-frame / 20 ms** delay. It is not bit-perfect:
the Speex interface quantizes to signed 16-bit PCM. Delay is identical for six
different impulse positions across analysis-block boundaries; a silent channel
stays silent. An isolated newly arriving transient retains about 91% of its peak.

| Requested suppression | Measured stationary-noise reduction |
|---:|---:|
| 12 dB | 11.72 dB |
| 18 dB | 16.66 dB |
| 30 dB | 24.85 dB |
| 40 dB | 29.62 dB |

These are seeded-noise fixtures, not guarantees for a particular room. A
sustained 440 Hz tone is eventually reduced by the full **18 dB** at default
settings: the estimator learns stationary material as noise. Therefore this
node is appropriate for speech cleanup, **not transparent music processing**.
Fast-changing background noise, other speakers and keyboard impacts still need
listening assessment. Inputs over full scale saturate at the 16-bit boundary;
parallel dry/denoised routes have uncompensated relative delay. Independent
channel estimators can also change the stereo balance of differing noises.
These are limitations of the selected [Speex preprocessor](https://www.speex.org/docs/manual/speex-manual/node7.html),
not evidence of neural speech isolation.

### Balance and Mono

Balance attenuates the opposite existing channel. It does not move right-only
material into the left channel when turned left. Mono computes `(L + R) / 2`,
outputs the same result on both channels, and cancels exactly opposite-polarity
inputs. Both behaviors match the controls' current names and intended operation.

## Repeating the audit

Build with `AUDIO_MIXER_OFFLINE_TESTS=ON` and run
`AudioMixer.EffectsQualityTests.exe`. The independent quality suite passed
**532 assertions** after the reverb correction. The graph/lifecycle and advanced
effect suites additionally check state inheritance, bypass/mute behavior,
allocation-free processing and control validation. The build outputs contain
measured values; raw audio is neither written nor played by this suite.

These checks establish numerical integrity for the tested conditions. They do
not substitute for level-matched listening on speech/music, different microphones,
or complete end-to-end latency and hardware qualification. Keep those claims
separate from silent synthetic pass counts.
