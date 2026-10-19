# Audio Mixer qualification — 17 October 2026

Date and time zone: **17 October 2026, Australia/Brisbane**. Source application
version: **0.2.0**. This records the current testing session and supplements the
earlier [implementation verification](NODE_VERIFICATION.md). It is not a full
release qualification.

**Current result:** the additional offline suites and bounded real-driver checks
passed within the scopes below. The **eight-hour silent endurance test is
DEFERRED**: it was stopped at the user's request on 17 October at 21:04:00
Brisbane time. This is neither a test failure nor an eight-hour pass. An
uninterrupted eight-hour run remains unqualified and requires a fresh run later.

## Completed evidence

| Check | Result | Scope and evidence |
|---|---|---|
| Existing graph regression | Passed — 3,347 assertions | All 64 routing migrations, sends, effects, validation, persistence and transitions. [Log](../build/qualification/graph-regression-results.txt). |
| Additional graph/settings/dynamics | Passed — 7,901 assertions; 4,527,360 synthetic frames | Corrupt/truncated/oversized/unknown-version settings, last-good recovery, failed saves, exact 32-node/96-cable limits, 80 seeded graph permutations against independent scalar results, compressor time constants, 40 EQ parameter extremes and transition slopes. [Log](../artifacts/qualification/qualification-results.txt). |
| Drift, queues and sample-rate conversion | Passed — 87,598 assertions in the recorded run | 16,000 queue/reference operations; concurrent producer/consumer and flush checks; all nine combinations of 44.1/48/96 kHz; 12 independent analytical clock models × one simulated hour; four actual-resampler models × 120 simulated seconds at ±750 ppm. [Log](../artifacts/qualification/drift-queue-results.txt). |
| Engine lifecycle with synthetic workers | Passed — 210 concurrent graph commits and snapshots | Actual engine, stub I/O: changing topology, rapid device rebinding, isolated loss/rejoin, retiring workers and shutdown. [Log](../artifacts/qualification/lifecycle-results.txt). |
| Real shared-mode workers with muted graph | Passed — 120.294 seconds plus three missing/restored-assignment cycles | VB-CABLE capture, xDuoo output, HDMI monitor output and VAC render opened concurrently; every graph node remained muted. [JSON](../artifacts/qualification/hardware-muted.json). |
| VB-CABLE stereo round trip with two shared consumers | Passed — short diagnostic | Both consumers identified the quiet stereo test chirp, with zero queue drops and nonfinite samples. [JSON](../artifacts/qualification/vb-direct.json). |
| Windows process capture | Passed — synthetic sibling process | Captured only the explicitly selected diagnostic process, using the production process-capture worker. [JSON](../artifacts/qualification/process-capture-first/results.json). This is not a Spotify/game compatibility result. |
| Spotify capture opening | Stream opening passed; music fidelity not confirmed | A ten-second passive capture reached Running and delivered 479,299 silent frames. Spotify's render session was inactive in the final inventory. [JSON](../artifacts/qualification/spotify-observation.json). |
| VAC simultaneous capture consumers | Passed — passive six-second observation | Two shared clients opened and received finite frames with zero queue drops. No stimulus was rendered; audio samples were discarded after counters were updated. [JSON](../artifacts/qualification/vac-shared-observation.json). No signal-quality or Discord/game claim. |

The queue concurrency count can vary with thread scheduling. The retained log is
the evidence for the count above; the invariant checks, not an exact interleaving,
determine success. All offline workloads used memory or private temporary files.
They did not open hardware or contact the personal engine's control pipe.

## Real hardware observation

The muted engine used these independently opened 48 kHz stereo paths:

| Role | Windows device name |
|---|---|
| Virtual capture | CABLE Output (VB-Audio Virtual Cable) |
| Listening output 1 | Speakers (xDuoo USB Audio 2.0) |
| Listening output 2 | MAG 271QP X28 (NVIDIA High Definition Audio) |
| Virtual render | Line 1 (Virtual Audio Cable) |

All four reached Running in approximately 52.6 ms. Replacing a listening
assignment with a deliberately nonexistent endpoint left its siblings and the
mixer heartbeat operating. Restoring the explicit assignments reopened the
physical streams in approximately 50.8–51.6 ms in three cycles. **These are
assignment-change tests, not physical unplug/replug measurements.** Their timing
does not establish the two-second physical recovery criterion.

Across the 120-second steady observation:

- Every graph meter stayed zero, with no generated sound from its outputs.
- Sampled queue fill reached at most 34.375 ms, below the 200 ms capacity.
- xDuoo and monitor output underrun counts stayed at zero. VB capture recorded
  three and VAC render one before the steady observation; neither increased
  during it. All path overrun counts remained zero. These results must not be
  described as zero underruns across startup.
- Handles declined from 337 to 331 and stayed at 331 after approximately
  30 seconds. There was no increasing handle trend in this short run.
- Private memory was 7,688,192 bytes initially and 8,024,064 bytes at the final
  periodic sample, with a transient maximum of 8,916,992 bytes. This is a net
  increase of 335,872 bytes, not proof of a leak. The diagnostic stores periodic
  JSON samples in memory, which contributes to its footprint. A short run cannot
  establish long-term memory stability.

The silent test captures only the assigned virtual receive path; both physical
microphone nodes are unassigned. It has private settings and no control server.
It does not disable devices, restart Windows Audio, change Windows defaults or
change endpoint/session volume. The initial [read-only device inventory](../artifacts/qualification/initial-endpoints.json)
shows xDuoo as the default render device for all three Windows roles, unmuted at
its existing level. The [final inventory](../artifacts/qualification/final-endpoints.json)
and [preservation comparison](../artifacts/qualification/preservation.json),
recorded at 20:35:49 Brisbane time, found no changes to Windows default assignments
or endpoint volume/mute settings. The original desktop and engine processes
remained running with the same IDs and start times (14800 and 42360). The personal
settings SHA-256 remained:

```text
D082E5A80406C58E7ACD25EFD8B56FE57234C89F6780314F095177429EB1E0F5
```

No personal package was replaced or restarted during that earlier check. These
observations establish configuration/process preservation at that time; they do
not measure acoustic continuity or cover the later user-authorized desktop update.

After stopping endurance at the user's request, the desktop interface was
replaced and launched at 21:04:01.943 Brisbane time. The [switch record](../artifacts/ui-switch-20261017-210400/result.json)
shows the new desktop process (46600) with an initialized window and input-idle
state. Engine process 42360 retained its start time and unchanged binary; its
frame counter advanced from 153,794,160 to 153,802,800 across the interface
replacement. Saved settings remained unchanged at revision 99. This documents
one controlled desktop replacement with a continuing engine, not full UI-fault,
engine-crash, Discord/game or audible-continuity qualification.

One routing item needs attention in the later call test: Discord had an active
render session on VAC as well as xDuoo, while its microphone capture used the
Shure directly. Before choosing the mixer's virtual output as Discord's
microphone, verify that incoming conversation audio stays outside VAC/B so it
cannot be returned to the call. No Discord routing was changed during this run.

## Signal probes and their limits

The active diagnostic stimulus was a 250 ms stereo chirp with a fixed peak of
−48 dBFS, routed only to an explicitly selected virtual playback endpoint.
Other applications can forward a virtual endpoint to physical audio or a call,
so current routing must be inspected before repeating it.

Both VB-CABLE consumers returned matching-channel correlations of approximately
0.939 left and 0.969–0.970 right. Opposite-channel correlations were below 0.0001.
The reported matching-template gains were approximately −0.54 dB left and
−0.26 dB right. These correlation-based estimates include time alignment and
resampling; they are not calibrated frequency-response or crosstalk measurements.
Each capture worker recorded one underrun; the render worker recorded zero.

Synthetic process capture returned correlations of 0.944 left and 0.978 right,
zero dropped frames and zero nonfinite samples. Its final state was
**Waiting for application** after the short-lived renderer exited. This shows
that exiting the source was recognized; app relaunch/rebinding remains a
separate test. The renderer recorded one underrun.

The separate passive Spotify check opened capture in approximately 16.6 ms and
observed 479,299 frames over ten seconds, with no nonfinite frames, dropped
frames, underruns or overruns. All observed samples were silent. The final
read-only inventory showed Spotify's existing render session inactive, which is
consistent with no music playing. The outcome `capture_observed_silent` is neither
a capture failure nor evidence of correct music fidelity, source isolation,
effects, protected-content handling or relaunch recovery. Playback was not
started or changed by the observer.

The approximate submission-to-capture-drain delays were 104.2–104.8 ms for the
direct VB probe and 66.2–66.8 ms for the separate-process probe. They include
diagnostic queues, delivery timing and transport buffering. They are **not**
microphone-monitoring latency, calibrated acoustic latency, or evidence that
the under-50-ms wired monitoring target has been met.

Passive VAC capture produced approximately 288,000 frames per consumer. One
consumer recorded zero underruns and the other one; both had zero overruns and
queue drops. This verifies shared opening/delivery on the installed driver,
without verifying the signal route, licence edition reminders, or actual
Discord/game behavior.

## Bug found and corrected

The settings loader refused documents larger than 262,144 bytes, but the save
path previously allowed individually valid strings to produce a larger file.
A successful edit could therefore become unreadable on restart. The save path
now checks the serialized size against the same limit **before any persistence
mutation**. A regression verifies that oversized saves fail and the previous
configuration stays byte-identical. Existing graph regression checks passed
against the fix. This records source verification; replacing or restarting the
running personal engine is a separate operation. The fix is in the source/test
build; the existing personal engine was not replaced during this qualification.

## Eight-hour endurance run — DEFERRED at the user's request

The user initially authorized an eight-hour actual elapsed background run using
real shared-mode drivers, a muted graph and private settings alongside ordinary
xDuoo playback. They later requested stopping and deferring it to open the new
Blender-inspired node interface. The diagnostic was stopped at that request;
the partial observation is not a completed endurance qualification.

| Run metadata | Value |
|---|---|
| Start | 17 October 2026, 20:34:53.824 Brisbane (UTC+10) |
| Originally expected completion | Approximately 18 October 2026, 04:34:54 Brisbane — cancelled |
| Stopped at user's request | 17 October 2026, 21:04:00.136 Brisbane (UTC+10) |
| Requested duration | 28,800 actual elapsed seconds |
| Observed process elapsed time at stop | 1,746.263 seconds (approximately 29 minutes 6 seconds) |
| Last recorded soak duration | 1,686.089 seconds (approximately 28 minutes 6 seconds) |
| Stopped process | 19452 — `build/native/AudioMixer.HardwareTests.exe` |
| Executable SHA-256 | `B461681E291F2FFFE034A16670554E1F21B1F820B1F0126DA1DA7F7E2F4992F2` |
| Metadata | [run.json](../artifacts/qualification/endurance-20261017-203453/run.json) |
| Preserved partial evidence | [results.json](../artifacts/qualification/endurance-20261017-203453/results.json) |
| Stop record | [stopped-by-user.json](../artifacts/qualification/endurance-20261017-203453/stopped-by-user.json) |
| Follow-up automation | `audio-mixer-endurance-results` — PAUSED |

The long-run harness records approximately one sample per minute, bounded to at
most 480 periodic observations over eight hours. This partial run used the same
physical and virtual paths listed above, with all graph nodes muted. Raw
`results.json` is preserved unchanged and ends with status `running` and
`passed: false`, because the process did not reach its normal terminal result.
Those interim fields do not mean the process is still running or that the test
failed; the separate stop record documents the user-requested termination.
The follow-up automation is paused and must not treat the partial result as a
failure or resume testing automatically.

When the user is ready, start a fresh explicitly authorized eight-hour run with
a new evidence directory. Record its exact start/finish time, binary identity,
endpoint identities, periodic status, frame progress, queue and underrun/overrun
counters, handle and memory trends, exit code and terminal result. Account for
the harness's own telemetry storage when assessing memory. Do not combine this
partial interval with a later run into an uninterrupted eight-hour pass.

## Not yet established

The earlier silent checks ran while the user was gaming. Audible checks,
deliberate disconnects and disruptive lifecycle tests remain deferred to a later
agreed session. The later request to open the updated interface does not qualify
those scenarios or the deferred eight-hour endurance test.

- Physical USB/DAC/HDMI/Bluetooth disconnect and reconnection, sleep/hibernate,
  Windows Audio service restart and default/profile/format changes.
- Two actual physical microphones operating together and verified channel
  selection, microphone permissions and audible monitor quality.
- Spotify music fidelity, browser variants, protected content, application relaunch and
  capture of the intended process tree under real application use.
- Actual Discord/game capture, simultaneous application consumers and the
  effects of their suppression or automatic gain settings.
- All four calibrated end-to-end latency paths and the wired under-50-ms target.
- Real UI failure/restart and engine crash recovery while a call/game is active.
- A full maximum-size processing graph under real multi-device load.
- Completion of eight actual hours and an overnight/full mixed-signal soak.
- A reminder-free test over 35 minutes that distinguishes VAC Lite from Trial;
  silence-only output checks cannot establish absence of spoken reminders in
  the capture stream.

Use [TESTING.md](../TESTING.md) for repeatable commands and the remaining manual
matrix. Preserve the user's current configuration and xDuoo playback while
planning any disruptive scenarios.

## Graphical EQ and readable nodes — 17 October, 21:35 Brisbane

The updated controller adds direct point editing for zero to eight EQ bands,
a fixed Low / Mid / High tone node, bundled Inter text, larger controls and
direct WPF rendering. Continuous edits use a bounded 100 ms target dispatch
cadence, with deterministic sustained-edit/in-flight/retry checks.

- Native graph suite: **48,522 assertions**; qualification suite: **7,901
  assertions over 4,527,360 synthetic frames**; synthetic engine lifecycle:
  **210 concurrent commits**. See the EQ/tone logs in TESTING.md.
- Final detached UI: **226 assertions**, **20 renders at 96/144/192 DPI**,
  zero binding errors; Release build and self-contained publish succeeded.
  The published single-file executable independently passed the same 226 checks.
- [Visual review and evidence](../design-qa.md) compares actual WPF renders
  against Blender's reference and the previous interface.
- [Personal activation](../artifacts/readable-update-20261017-213523/result.json):
  responsive UI, matching binaries, advancing frames, revision 207 and settings
  bytes unchanged. Previously running paths returned; A1 remains Waiting as
  before, and A2/microphone 2 remain Unassigned. The successful switch reached a
  running snapshot in 0.421 seconds, not an audible-gap measurement.
- Two earlier updater attempts returned to the prior app because of shutdown
  pipe cleanup/acknowledgement errors; both are documented in the visual review.
  They were not soak tests or hardware fault tests.

Actual pointer capture, keyboard traversal, cross-monitor DPI behavior, audible
effect sweeps and live latency remain for the later testing session. The
eight-hour endurance run is still **Deferred**, and its automation remains paused.

## Zoom/text correction — 17 October, 21:53 Brisbane

The controller now uses neutral node headers, grayscale scaled text, a cached
adaptive grid and retained cables/transforms. Unchanged telemetry avoids full
configuration copies and unnecessary EQ/dial refreshes.

- Release and published executable: **268 detached checks, 22 renders**, zero
  binding errors. The Release build and self-contained publish succeeded.
- At 30% zoom, identical eight-node telemetry/layout work fell from 2.325 to
  1.042 ms, with 98.4% less allocation. The 24-node case reduced allocation
  96.7%. [Benchmark methodology and limitations](ZOOM_RENDERING_BENCHMARK.md)
  retain all zoom results, including slower offscreen raster cases.
- [Controller-only replacement](../artifacts/zoom-ui-update-20261017-215316/result.json)
  preserved engine PID 43332, its start time/binary, revision 310 and exact
  settings bytes. The frame counter advanced and the new controller opened
  a responsive visible window. A1 stayed Waiting as before.
- No audio signal, engine restart, route/gain/device change or desktop input
  automation was used. These results do not measure audible continuity or
  actual mouse-to-display latency. The endurance run remains Deferred.

## Multi-node editing — 17 October, 22:11 Brisbane

- Published controller: **368 detached checks, 24 renders**, zero binding
  errors. [Raw results](design/group-edit-published/results.json) cover existing
  controls plus box selection, shared movement/cancel, clipboard payloads,
  repeated paste, group deletion and replay against a changed configuration.
- Source review addressed paste revalidation during pending-edit replay and
  context-menu selection/focus. Two new group views were visually inspected.
- [Controller-only replacement](../artifacts/group-ui-update-20261017-221057/result.json)
  preserved engine PID 43332/start time/binary, advancing frames, revision 549
  and exact settings bytes. New controller PID 43108 opened a responsive window.
- No live audio settings, engine restart, test signal, OS clipboard content or
  desktop input was involved in qualification. Windows clipboard integration
  and physical gestures remain for the agreed hands-on session; the endurance
  run stays Deferred.
