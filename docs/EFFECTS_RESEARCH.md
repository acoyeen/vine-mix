# Effects and node simplification

Implemented 18 October 2026. These are local, real-time audio processors; they do
not upload or record audio. The new effects are available in **Add → Effects**.
Installing this version does not insert them into an existing route.

## One Mix node

The previous Gain / Multiply and Add / Mix implementations both summed incoming
audio and applied the node's gain. They did not implement two different math
operations: Gain did not multiply one audio stream by another. A single **Mix**
node now covers both uses. One input provides volume adjustment; several inputs
are summed, with one overall level. Levels are amplitudes, so summing correlated
signals can clip; Mix does not normalize its inputs automatically.

Reroute also used the same processing path. A tiny, control-free cable junction
could justify a distinct reroute feature later, but the old full-sized node added
no unique operation. Gain and Reroute remain supported in saved graphs and copied
groups, but are absent from creation and search. There are 18 creatable kinds and
20 supported kinds including those two legacy types.

Generic Level rows have been removed from effects, Balance and Mono. Input,
output, Mix and Linked control levels remain. Compressor Make-up and Reverb Mix
remain because they control compression compensation and the dry/wet blend.
Nonzero levels saved on older effects still apply; the node shows the retained
level and provides a context action to reset it. Loading a workspace never
silently changes its sound.

## Denoiser

The node now offers an **Algorithm** dropdown with **SpeexDSP** and **RNNoise**.
Missing algorithm fields in older saved settings and clipboard groups default
to SpeexDSP without changing their Reduction setting. Selection, both settings,
Bypass, Undo/Redo, node Reset and copy/paste follow the normal node conventions.
Only the selected algorithm's dial is visible; switching keeps node dimensions
and port positions stable. Both algorithms run locally and neither is Shure's
hardware denoiser.

**SpeexDSP implementation:** the noise-suppression preprocessor in BSD-licensed
**SpeexDSP 1.2.1**, with independent channel states. Automatic gain control,
dereverberation and voice-activity gating are disabled. This is a spectral noise
estimator, rather than a gate that simply mutes quiet sections. The official
preprocessor API recommends short analysis frames and exposes noise suppression
separately from these other features. [Speex preprocessor API](https://www.speex.org/docs/api/speex-api-reference/group__SpeexPreprocessState.html),
[Speex manual](https://www.speex.org/docs/manual/speex-manual/node7.html).

The control is **Reduction**, 0–40 dB, default 18 dB. The setting describes the
requested noise floor, not a guarantee of that much reduction for every sound.
Live edits advance at most 1 dB per 10 ms analysis frame. Each channel processes
480 samples at 48 kHz. Measured algorithm plus block staging delay is **960
frames / 20 ms**. Bypass uses an equally delayed dry signal so toggling does not
mix two differently timed copies inside the node. Multiple denoisers accumulate
delay. The graph has no automatic delay compensation: combining direct and
denoised copies of the same input can cause comb filtering.

Speex's preprocessor boundary uses signed 16-bit PCM, despite the surrounding
float graph. Samples above full scale clip at that boundary. Keep input headroom.
SpeexDSP remains the default and retains its existing 20 ms processing and bypass
delay.

**RNNoise implementation:** Xiph RNNoise **v0.2** with its matching **0b50c45**
model compiled into the engine. Sources and model archives are independently
pinned by SHA-256 in CMake; runtime and first use need no network or model file.
The complete upstream BSD notice ships as `licenses/RNNoise-COPYING.txt`.
The build includes baseline, SSE4.1 and AVX2/FMA neural kernels with upstream
CPU dispatch. Independent channel states process 480 samples at 48 kHz. Its
float API uses a 32768 amplitude scale; the input is bounded to full scale at
the model boundary. Automatic level control and a VAD gate are not added.
[RNNoise v0.2](https://github.com/xiph/rnnoise/tree/v0.2),
[RNNoise licence](https://github.com/xiph/rnnoise/blob/v0.2/COPYING).

RNNoise exposes **Mix**, 0–100%, default 100%. This is a wet/dry blend, not a
promised dB reduction setting. Its dry path is time aligned with the neural
output: model lookahead, overlap/add and block staging measure **1440 frames /
30 ms**. Zero mix and settled bypass preserve the delayed original float audio.
The high-pass and pitch processing inside RNNoise still apply at nonzero Mix.
Neither algorithm guarantees preservation of singing, humming or music.

Both processor states are allocated outside the audio callback. Speex stays warm;
RNNoise inference runs only while selected, preparing to switch, or fading out.
Selecting RNNoise from a settled Speex state warms it for 100 ms behind the
existing signal, then crossfades over 10 ms. Returning to Speex crossfades over
10 ms. Mix edits also ramp over 10 ms. Each algorithm retains its own bypass
alignment; selecting another algorithm changes total latency by 10 ms and the
transition briefly blends differently delayed signals. Parallel routes still
need manual alignment.

RNNoise v0.2's state was inspected: histories are inline and the embedded model
uses only pointers into immutable static weights. Bounded snapshot copies preserve
all histories across graph adoption, and a prepared pristine state resets without
allocating or calling CPU discovery in the audio callback. This is specific to
the pinned version and embedded model and must be audited before an update or
external-model support.

Steady fan/hiss reduction and speech cleanup are the intended uses. Speech quality, music preservation,
keyboard transients and rapidly changing noise need listening evaluation. This
is not a promise of neural speech isolation or removal of another speaker.

## De-esser and Plosive remover

Both now use a second-order high/low-pass detector driving a dynamic shelving
filter. The original first-order split leaked too much target-band energy through
its unaffected side: attenuation at the selected frequency could never exceed
about 3 dB, even at maximum reduction. The replacement uses a topology-preserving
state-variable filter, following the published shelf mapping in
[Andrew Simper's SVF derivation](https://www.cytomic.com/files/dsp/SvfLinearTrapOptimised2.pdf).

A stereo-linked detector applies a fixed compression exponent equivalent to 4:1
above threshold, capped by Maximum reduction. Detector attack/release are 0.5/20
ms; gain attack/release are 1/50 ms. Shorter recovery prevents the next quiet
consonant remaining dulled for hundreds of milliseconds. There is no lookahead
or added frame buffer. Controls ramp over 10 ms, and node bypass also crossfades
over 10 ms. Zero reduction is exactly dry. The selected Frequency is the shelf
midpoint: attenuation there is half the shelf's reduction in decibels; farther
into the target band it approaches the full amount.

| Node | Band affected | Controls and defaults |
|---|---|---|
| De-esser | Above the crossover | Frequency 6 kHz (2–12 kHz); threshold −24 dB (−60–0); maximum reduction 9 dB (0–24) |
| Plosive remover | Below the crossover | Frequency limit 150 Hz (60–300 Hz); threshold −24 dB (−60–0); maximum reduction 12 dB (0–24) |

Split-band de-essing is established practice: it avoids turning down the entire
signal whenever high-frequency consonants trigger the detector.
[MathWorks split-band de-essing example](https://www.mathworks.com/help/audio/ug/SplitBandDeEssing.html).

Microphone plosives often concentrate at low frequencies. A frequency limit and
selective attenuation address those thumps without applying a permanent bass cut
to the entire recording. This motivation is supported by the documented
frequency-limited behavior of commercial de-plosive tools; no proprietary code
or algorithm was copied. [iZotope De-plosive documentation](https://downloads.izotope.com/docs/rx6/26-de-plosive/index.html).

The shelving transition is deliberately broad. De-esser can also attenuate cymbals
or other strong treble; Plosive remover can attenuate sustained loud bass. The
latter is threshold-driven low-band reduction, not a classifier that recognizes
only consonant bursts. The Reduction indicator reports actual shelf attenuation,
not a whole-signal volume change. Its detector readout helps set Threshold: a
high-frequency band below the threshold will remain unchanged regardless of
Maximum reduction. Adjust it using speech and compare with bypass. Software
cannot reconstruct a microphone signal already clipped by a burst of air; a
physical pop filter remains useful. [Shure KSM32 guide](https://www.shure.com/en-US/docs/guide/KSM32).

## Reverb

An original Schroeder-style stereo network uses eight parallel damped scattering
delay paths followed by four true allpass diffusion stages per channel, with
different left/right delay lengths and a pre-delay buffer. The paths preserve or
reduce steady energy rather than allowing their gain to rise with decay. This
replaces the initial unnormalized comb network after measured overloads were
found in the [effect quality audit](EFFECTS_QUALITY_AUDIT.md). It provides a small
algorithmic room effect without an impulse-response library, service or paid
dependency. [Schroeder allpass structures](https://www.dsprelated.com/freebooks/pasp/Schroeder_Allpass_Sections.html),
[allpass transfer functions](https://www.dsprelated.com/freebooks/pasp/Allpass_Two_Combs.html).

| Control | Range | Default | Purpose |
|---|---|---|---|
| Mix | 0–100% | 20% | Blend original audio and reverb |
| Decay | 0.1–5 seconds | 1.2 seconds | Nominal feedback decay time |
| Damping | 0–100% | 50% | Darken successive reflections |
| Pre-delay | 0–100 ms | 15 ms | Delay the wet signal's onset |

Decay is a nominal control, not a measured acoustic-room RT60: damping and
frequency affect the audible tail. The dry path is immediate; the wet path has
the chosen pre-delay plus 25.3125 ms to its first reflection. Parameter changes
are smoothed; changing pre-delay can briefly shift pitch. Bypass retains state
but crossfades to dry; resetting the audio timeline clears stored tails. Delay
buffers are bounded and preallocated. The fixed-parameter frequency/energy gain
is bounded by unity; this is not an instantaneous peak ceiling, and ordinary
input headroom remains necessary for transients.

## EQ and reset behavior

The EQ node is wider, with a 300-DIP-high plot. The response follows the actual
biquad formulas, including Width/Q, rather than a cosmetically rounded sketch.
Sharp/Medium/Smooth presets duplicated Q and have been removed; existing Q values
and new-band Q=1 remain unchanged. Shape choices remain distinct: Bell, Low shelf,
High shelf and Notch. [W3C Audio EQ Cookbook](https://www.w3.org/TR/audio-eq-cookbook/).

- Double-click a band, or choose **Reset position**, to restore its original
  frequency and zero gain while retaining its shape, width and enabled state.
  New points remember their creation frequency. Older bands without an anchor
  use their loaded frequency when first edited.
- Right-click empty plot space and choose **Reset EQ to flat** to restore the five
  default flat bands. This also removes added notches or legacy pass-filter bands.
- Right-click a node and choose **Reset node to defaults** to reset all sound
  controls, including EQ, bypass, mute, level and microphone mono/channel mode.
  Name, position, identity, assigned device/app and connections remain. Outputs
  return to their new-node muted defaults; weighted cable settings remain intact.

The EQ's underlay measures actual **post-EQ stereo output** in 128 log-frequency
bins. It has its own −90 to 0 dBFS scale, separate from the curve's ±24 dB gain
scale. Silence and stale telemetry clear the display. It does not generate a
fake spectrum from the curve. See [analyzer design and verification](EQ_SPECTRUM_ANALYZER.md).

## Licensing and implementation boundaries

SpeexDSP is fetched from the official Xiph release archive, pinned to SHA-256
`8c777343e4a6399569c72abc38a95b24db56882c83dbdb6c6424a5f4aeb54d3d`.
Its COPYING file is included in the personal package, alongside the updated
third-party notices. The application uses a local bridge to copy state into
preallocated instances across graph edits; it does not allocate vendor state in
the audio callback. The bridge is tied to this pinned release and must be reviewed
before a vendor update.

Effect storage is prepared off the audio thread. Existing states are transferred
at a processing boundary, parameter edits preserve ongoing noise estimates and
reverb tails, and discontinuities reset them. Processing performs no file access,
device enumeration or network work. Spectrum FFTs run on the control thread.
Tests instrument C++ and Speex allocation paths during processing, state transfer
and reset.

## Verification and remaining listening work

Silent tests cover settings bounds and round trips, old files with absent new
fields, bypass alignment, finite output, selective band reduction, denoiser
adaptation, reverb decay/tails/stereo, state transfers, repeated parameter edits,
maximum-size graphs and allocation-free processing. Detached WPF tests cover
control bindings, node resets, clipboard compatibility, dimensions, zoom and
rendering. Measured results and activation evidence are in
[design-qa.md](../design-qa.md).

These tests do not establish perceived voice quality, full microphone-to-headphone
latency, or hardware reliability. The audible checks remain in
[TESTING.md](../TESTING.md), including comparison with bypass, headroom, live calls
and the previously deferred endurance run. No test signal is sent into the user's
current listening or voice-call route during this development pass.
