# Live equalizer spectrum

The equalizer underlay measures the **actual stereo samples leaving that EQ node**, after its filters and bypass blend. It is not drawn from the EQ curve or the peak meters. A boosted or cut band therefore changes the measured display only when the input contains sound in that frequency range. The analyzer does not change the audio path or add audio latency.

## Measurement

- Internal audio: 48 kHz stereo floating point.
- Analysis window: 4,096 samples, approximately 85.3 ms; periodic Hann window.
- Window publication: every 2,400 samples (50 ms / 20 Hz), after a complete initial window is available. The engine's 240-frame quantum makes the first complete publication occur at frame 4,320.
- FFT: an in-place radix-two transform with precomputed bit reversal, Hann values and twiddle factors. Left and right are packed into one complex transform; their independent powers are then recovered and averaged. Inverting one channel does not cancel the displayed signal.
- Display: 128 logarithmically spaced bins, with centers `20 × 1000^(index/127)` Hz. Each display bin preserves the maximum measured FFT power in its frequency interval, including interpolation at the boundaries. This preserves narrow peaks at high frequencies while providing a continuous view at low frequencies.
- Scaling: one-sided sinusoidal peak amplitude, corrected by the Hann window's coherent gain; `10 log10(mean channel power)`. Identical full-scale stereo sine waves at an FFT-bin center read approximately 0 dBFS. One active channel and one silent channel read approximately 3 dB lower, because the display represents mean stereo energy.
- Display range: −90 to 0 dBFS. It is a spectral shape guide rather than a replacement for the independent clipping meter. It is not a calibrated broadband-noise power-density meter.
- Smoothing: 35 ms attack and 180 ms release in the displayed decibel domain, based on actual frame deltas. Duplicate polls do not change the smoothing state. Large gaps discard the previous display history.

The Hann window reduces spectral leakage, while preserving useful frequency resolution. Its remaining scalloping means an off-bin tone can appear roughly 1.4 dB lower than a perfectly aligned tone. At this window length, FFT bins are 11.72 Hz apart; the 128 display points do not imply finer physical resolution. [Julius O. Smith, *Spectrum Analysis Windows*](https://www.dsprelated.com/freebooks/SASP/Spectrum_Analysis_Windows.html), [Arm CMSIS-DSP Hann window characteristics](https://arm-software.github.io/CMSIS-DSP/v1.14.3/group__WindowHANNING.html).

Packing two real sequences into one complex transform relies on the conjugate symmetry of the real transforms. This implementation computes the mean left/right power as `(abs(Z[k])² + abs(Z[N−k])²) / sum(window)²`, including the one-sided amplitude and coherent-gain corrections. The original implementation is local; no FFT library or additional licensing dependency is introduced. [Arm real FFT documentation](https://arm-software.github.io/CMSIS-DSP/v1.16.0/group__RealFFT.html), [Julius O. Smith, windowed sinusoid analysis](https://www.dsprelated.com/freebooks/mdft/Spectrum_Analysis_Sinusoid_Windowing.html).

## Real-time and ownership rules

`LiveSpectrum::push` runs on the mix thread and only sanitizes/copies samples into fixed storage. It performs no allocation, lock, wait, FFT, file access or UI operation. It retains no pointers to graph buffers. Publication uses three preallocated windows and an atomic ownership exchange. The producer and consumer each own one window; the third is the handoff slot. The producer can replace an unread window but can never overwrite the window being analyzed.

`LiveSpectrum::read` runs on the control thread inside the existing serialized snapshot operation. It analyzes at most the latest completed window. A stopped or slow interface loses intermediate visual updates and never creates a queue of delayed sound or analyzer work. An EQ's analyzer survives ordinary parameter/position changes. Removing/recreating it produces a new continuity token. Resume clears prior windows before collecting current audio; a skipped audio quantum also clears the display until a new complete window is available.

The native node-status contract is:

| Field | Meaning |
|---|---|
| `spectrumDb` | 128 finite float values; empty for other nodes or before priming |
| `spectrumFrame` | Absolute end frame of the analyzed window; zero while unprimed/reset |
| `spectrumRevision` | Analyzer continuity token; changes when a new analyzer is created or on resume |

The engine seeds continuity identities from Windows' monotonic counter so a process restart cannot look like an old frame of the prior engine. The UI should clear unavailable data, ignore repeated frame stamps and discard its own stale display if the engine stops advancing. None of these values is persisted in the graph or settings. No audio is recorded or written to disk.

## Silent verification

`AudioMixer.SpectrumTests` exercises silence/nonfinite handling, sine-frequency localization, coherent-gain calibration, off-bin scalloping, antiphase stereo, one-channel energy, cadence, duplicate polling, slow-consumer freshness, resume/gap clearing, real GraphProcessor EQ boost/cut/bypass output, concurrent handoff ownership, bounded memory and allocation-free producer processing across 32 analyzers. Its budget output reports observed timings; these are test-machine observations, not an audio latency promise. The test links no real endpoint implementation and cannot open a microphone or playback stream.

On 18 October 2026, all 410 checks passed. A 32-analyzer synthetic run measured 5.24 microseconds average producer time per 5 ms audio block, with 157.3 microseconds as the largest observed sample. Analyzing one window for all 32 nodes took 1.63 ms outside the audio thread. Fixed storage was 313,992 bytes per EQ and the producer allocation counter stayed at zero. The separate stub-engine lifecycle suite also passed its 212 graph commits, including spectrum serialization, continuity across edits, and deletion/recreation. No real audio endpoints were opened by either suite.
