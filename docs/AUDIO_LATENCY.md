# Audio latency

The engine requests shorter supported Windows shared-audio periods and keeps
smaller, device-aware application queues. This reduces avoidable buffering while
retaining the existing 48 kHz processing, 240-frame graph blocks, sinc resampling
and effect algorithms. It does not promise a particular microphone-to-headphone
latency on unmeasured hardware.

## Device opening and fallback

Physical microphones, virtual inputs and physical listening outputs use
event-driven shared WASAPI. When IAudioClient3 is available, the engine requests
the shortest legal period for the endpoint's existing mix format. Periods round
up to a multiple of the driver's fundamental period within its supported range.
The sample format, sample rate and processing category are preserved.

If initialization rejects that period, the engine tries the supported default
period, then legacy shared initialization. Each failed initialization releases
all client interfaces before a replacement is activated. Missing interfaces or
invalid period ranges go directly to the legacy path; retries are bounded.

Three observed capture discontinuities or playback supply underruns within ten
seconds, after a one-second startup grace, reopen that path using default timing.
The conservative preference lasts until a device selection or explicit Retry
Audio; reconnecting alone does not repeatedly select an unstable short period.
This is a software safeguard, not proof that all hardware glitches are detected.

The exclusive VAC Lite virtual-microphone send retains its format, buffer and
isolation policy. It never falls back to shared playback.

## Application queues and scheduling

All queue budgets below use 48 kHz graph frames, including with 44.1 or 96 kHz
devices. They describe application queues, not total audible delay.

- Capture's initial drift target is 360 frames / 7.5 ms, down from 720 / 15 ms.
  Its consumer waits for the larger of a graph block and a device period, plus
  that reserve, before delivery starts or resumes after starvation. Priming
  silence is not counted as an
  underrun. This builds the reserve promptly rather than waiting seconds for
  tiny clock-drift corrections to fill an empty queue.
  Application process-loopback capture retains its separate delivery policy.
- Playback's initial target is `max(device period, 240) + 240 + 64` graph frames.
  A period at or below 5 ms uses 544 frames / approximately 11.33 ms instead of
  15 ms. Longer periods retain sufficient callback and scheduling headroom.
  Priming counts both the external queue and pending resampler input, and leaves
  scheduling/converter reserve beyond the current callback's complete demand.
- Genuine playback shortfalls increase the reserve by one graph block. Capture
  adds a block after repeated consumer shortfalls in a one-second observation
  window, excluding startup. Growth is bounded per stream and resets on reopen.
  Capture recovery primes against the increased reserve immediately; feedback
  time advances once when a late callback drains several packets.
- Ring capacities and stale-audio flush limits remain bounded. A stopped or
  returning source clears stale audio and primes from current capture.
- The mixer preserves its absolute 5 ms timeline after short scheduling delays.
  It allows at most two immediate catch-up blocks and rebases after a long stall
  or sustained overload, avoiding both cumulative slowdown and unbounded bursts.

Buffer targets are sampled at different points: capture before producing a
packet, playback before consuming a callback. Adding the targets does not yield
an exact round-trip latency. Resampler filter history and Windows/hardware
buffers also contribute.

## Effects and monitoring

The denoisers retain their measured node delay: Speex 20 ms, RNNoise 30 ms.
Bypass and zero neural mix preserve the time-aligned dry signal. Changing device
timing does not remove those delays. EQ and dynamics retain their existing
processing quality, control smoothing and no-lookahead behavior.

For the fastest existing listening route, connect the microphone to a separate
headphone branch before the denoiser. The outgoing virtual-microphone branch can
still use denoising. Avoid recombining differently delayed copies of the same
voice at one output.

## Diagnostics and verification

Device path snapshots add `periodMs` (requested period, or the legacy default),
`bufferMs` (allocated WASAPI buffer), `targetQueueMs` and `resamplerPendingMs`.
Existing `queueMs` remains external ring occupancy. None is an acoustic latency
measurement; resampler pending input also excludes filter history.

`scripts/verify-offline.ps1` includes:

- `SharedAudioTests`: fake COM clients exercise legal periods, unchanged formats,
  fresh-client fallbacks, legacy drivers and runtime stability policy.
- `AudioTimingTests`: production queue policy, capture reader and sinc resampler
  under independently scheduled capture/mixer/playback clocks, multiple rates,
  callback periods/phases, clock drift and recovery scenarios.
- `MixClockTests`: deterministic deadline, catch-up, overload and resume checks.
- Existing graph, denoiser, converter, lifecycle and detached desktop checks.

Offline checks open no real audio endpoints or personal settings. Physical
microphone-to-headphone measurement, listening under load, hotplug and endurance
remain manual qualification. The existing virtual probe cannot measure calibrated
physical round-trip latency. Follow [testing guidance](../TESTING.md) before any
audible or live-routing session.

## Primary references

Checked 20 October 2026:

- [Microsoft low-latency audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio)
- [Supported shared-engine periods](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient3-getsharedmodeengineperiod)
- [Initializing a shared stream](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient3-initializesharedaudiostream)

These document the API's period constraints and capabilities. The queue budgets
and recovery thresholds above are project implementation choices, verified
synthetically rather than claims about a particular device's performance.
