# Core dynamics verification — 18 October 2026

The activated build is **`core-dynamics-20261018-202241`**. Implementation,
offline verification, activation and controlled checks through the real
application and engine completed. The original **13-node workspace** was restored
at revision **4093**; all its previously running paths were Running afterward.
The engine remained **PID 32956** throughout the live checks.

The final [running-version check](../artifacts/core-dynamics-verification/running-version.json)
confirmed the manifest hashes, engine PID 32956 and responsive controller PID
31728 at 20:35:43 AEST. The application remains running from the active package.

The active package is `artifacts/AudioMixer`; the separately built candidate is
`artifacts/AudioMixer-CoreDynamics`. The build identity records these SHA-256s:

| Binary | SHA-256 |
|---|---|
| AudioMixer.exe | `DFB95B6F224D3B1E533417066A43AFA968AC42B50DDDAEA5A900A96C7384D9CD` |
| AudioMixer.Engine.exe | `1D9347092206553FB257A93524F7EA5EA5D7884FF8FDBA10A87A9B504C156931` |

## Offline evidence

[Final native results](../artifacts/core-dynamics-verification/native-final/results.json)
passed **12 of 12 suites**. This includes existing graph, qualification,
device-independent lifecycle, drift, effects, EQ spectrum, de-esser quality,
performance, catalogue and Voice Color coverage, plus **80 focused Core Dynamics
checks**. The synthetic-worker engine lifecycle suite passed **296 commits**.

Focused checks cover:

- Exact preservation of the previous compressor's transfer and envelope, and
  identical compiled behavior when old JSON omits the new optional fields.
- External detection, stereo linking, no sidechain leakage into the programme,
  silence when the external trigger is absent/muted, and sidechain cycle checks.
- Sample-ceiling enforcement on the first impulse and during rapid ceiling
  edits, including saved node and linked-control gains; bypass transition,
  detector metering and rejection of non-finite input.
- Expander ratio law, Gate range floor, hold/hysteresis, stereo linking,
  transparent zero range/1:1 ratio and bypass.
- Exact stereo impulse positions at 0, 1, 48, 239, 240, 241, 48,000 and 96,000
  samples; rapid delay changes and zero-delay bypass.
- Parameter/history inheritance, bounded transitions, no tracked allocations or
  deallocations in processing/configuration adoption/reset, JSON persistence and
  malformed/out-of-range setting rejection.

The [final desktop report](../artifacts/core-dynamics-verification/candidate-ui/results.json)
passed **2,506 checks**, produced **92 WPF renders**, and reported **zero binding
errors**. These cover the real control classes and cable gestures in a detached
workspace: defaults, reset, copy/paste, persistence, mode switching, sidechain
snapping/removal and prevention of unintended audio/sidechain conversion. New
nodes retain their dimensions and complete controls across the 30–200% zoom
stops. Detached rendering is distinct from the live session below.

One initial performance run failed an existing timing assertion: the maximum
30-denoiser graph measured processing p99 **10,569.3 µs**, above the 5 ms quantum.
That failure is retained in
[the initial results](../artifacts/core-dynamics-verification/native/results.json).
The complete suite was rerun serially and passed; the same case then measured
processing p99 **1,242.3 µs**, with inheritance p99 **214.9 µs**. This difference
shows timing sensitivity to system load; it is not a universal real-time deadline
guarantee. In the final focused run, inheritance plus one 5 ms processing block
for **30 maximum-length Alignment Delays** measured median **452.3 µs**, p99
**669.5 µs**, maximum **749.7 µs**. These are offline processing measurements,
not microphone-to-output latency.

## Activation and real application checks

[Activation](../artifacts/core-dynamics-update-20261018-202252/result.json)
backed up the complete working package and configuration before replacing the
two verified executables. Both installed hashes matched the candidate. The
configuration, its file bytes and all 13 Running paths survived activation.
The new Running snapshot was obtained in **0.304 seconds**; that is a restart
observation, not a measurement of the audible interruption. Rollback was available
but was not needed.

The live session added seven temporary diagnostic nodes fed by an already
captured application. They ended in processing/meter nodes and had **no route to
any physical or virtual output**. Existing nodes and routes were retained. No
synthetic tone was played, no Windows defaults were changed, and test signals
were not sent to active calls. The live source was intentionally boosted only
inside these isolated branches to exercise attenuation and limiting.

Actual window interaction changed controls, selected modes, connected the
detector cable and toggled bypass. UI state and saved revisions are recorded in
[the interaction log](../artifacts/core-dynamics-verification/live/ui-interactions.json).
Engine snapshots were taken after briefly closing only the controller, then the
controller was reopened. The audio engine continued throughout.

| Addition | Observed through the actual app and running engine |
|---|---|
| Expander / Gate | Expander showed 10.5 dB reduction after a live threshold edit. Gate at 0 dBFS reached its 60 dB floor; at −80 dBFS it opened and passed the live signal. Bypass was also exercised. |
| Ducking | The actual preset selected external detection and the documented settings. A cable from Gate to the detector kept the programme separate. Closed Gate left ducking effectively at unity; opening Gate triggered approximately 21.3 dB reduction in a recorded snapshot. Bypass returned the programme level. |
| Limit | The actual mode and Ceiling controls selected −24 dBFS. Recorded enabled output peaks were capped at approximately **0.0630957335** (−24 dBFS), despite the boosted input. Bypass allowed higher peaks; re-enabling restored the ceiling. |
| Alignment Delay | The millisecond field committed **12.375 ms / 594 samples**. The sample field then committed **1 sample**, displayed as **0.021 ms**. Engine snapshots retained both exact settings across controller restarts; bypass was exercised. Exact waveform timing is established by the offline impulse checks, not the live meter. |

The three saved observation sets are
[20:27:33](../artifacts/core-dynamics-verification/live/observed-202733.json),
[20:29:04](../artifacts/core-dynamics-verification/live/observed-202904.json) and
[20:33:49](../artifacts/core-dynamics-verification/live/observed-203349.json).
Their frame counters advance and their saved configurations match the control
changes. Copy was invoked in the live context menu, but a completed live paste
or whole-node reset was **not** recorded. Reset/defaults and full copy/paste are
covered by the passing detached UI/model checks; they are not claimed as live
actions.

The [live evidence assertions](../artifacts/core-dynamics-verification/live/verification-result.json)
passed all 23 checks across 45 snapshots. All 30 enabled-Limit observations stayed
below the exact −24 dB ceiling; active Ducking reduction ranged from 18.78 to
22.60 dB. These checks also verify bypass, exact saved delay values, the detector
cable type, and that no temporary route entered an original node or output.

[Restoration](../artifacts/core-dynamics-verification/live/restoration-result.json)
removed all diagnostic nodes and routes and restored the original configuration
and layout, with only its committed revision advancing. All previously Running
paths were restored, and the frame counter advanced. The updated application was
relaunched with that original workspace.

## Remaining limitations

This verifies functioning controls, persistence and live-engine behavior without
auditioning the temporary branches. It does not qualify preferred vocal settings,
gate chatter on a particular microphone, musical pumping or distortion under
sustained heavy limiting. Limit is a sample-peak ceiling at its node, not an
oversampled true-peak limiter; bypass or downstream boosts/sums can exceed it.
Alignment Delay is manual and has no automatic device-latency compensation.

Hardware round-trip latency, reconnect/sleep behavior and the deferred long soak
were not repeated for this addition. See [the controls and limitations](CORE_DYNAMICS.md)
and [the broader qualification procedure](../TESTING.md).
