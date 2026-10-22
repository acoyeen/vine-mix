# Audio Mixer v1 — deferred testing and hardware qualification

Historical package/evidence paths and descriptions of the running session below
refer to the original test date. Raw records and backups are retained privately
outside this checkout.

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](BETA_TESTING_CURRENT.md) for the active release checks.

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

## Status and purpose

Audio and hardware qualification was explicitly deferred by the user. This file
is a procedure for a later prompt. **Native and desktop compilation completed;
no automated native DSP test, live audio test, complete driver qualification,
latency measurement or soak test was performed.** Authorized startup and setup
checks on 17 October 2026 confirmed the window's engine connection and saved
revision 1. The current saved baseline is revision 3 after subsequent user
changes; the slate UI revision must preserve it rather than restore revision 1.
A remains muted at −24 dB. The user installed base VB-CABLE and VAC Lite after an earlier
non-elevated installation attempt was rejected. A reboot remains pending and is
agreed before reliability validation. See [the setup record](STARTUP_STATUS.md).
Opening assigned streams does not establish audio correctness or daily-use
suitability.

The fixed-size console has since passed the separate inert UI checks below. The
repository still has no deterministic **native DSP/queue test executable or CTest
suite**. A future testing pass must add that harness before reporting those cases
as passed. Running CTest with zero registered tests is not evidence of correctness.

The release candidate has exactly two physical microphone assignments, one combined
App Audio input, two physical A outputs, and one virtual B output. Three sources
have independent A/B sends. A is what the user hears; B is what other applications
receive as a microphone. These counts are acceptance requirements.

Record results as **Passed**, **Failed**, **Blocked**, or **Not run**. Never convert a
missing second microphone, unavailable game, short run, or subjective listen into a
pass for an unperformed scenario.

## UI-only verification completed

On 17 October 2026 the final control-detail build's `--verify-ui` mode passed
**37 checks** with zero WPF binding errors. Four added assertions cover large
fader caps, full-opacity off-route controls, explicit A/B lane labels and measured
separation of wells/tracks/inactive caps. The previous installed flat slate build
passed 33 checks; the earlier charcoal build passed 28. Those are historical
results. The final 18-DIP-readout build exited successfully with `passed: true`.
Verification
uses an inert preview view model: it opens no engine connection or
audio devices and does not read or change the user's saved mixer configuration.

To repeat it after publishing the desktop package:

```powershell
$verificationPath = Join-Path $PWD 'artifacts\ui-verification\repeat'
$verificationProcess = Start-Process -FilePath '.\artifacts\AudioMixer\AudioMixer.exe' `
  -ArgumentList @('--verify-ui', '--verification-dir', "`"$verificationPath`"") `
  -WindowStyle Hidden -Wait -PassThru
$verificationProcess.ExitCode
Get-Content (Join-Path $verificationPath 'results.json')
```

Inspect the process exit code and generated `results.json`; require both exit
code 0 and `passed: true`. The latest run is recorded in
`artifacts/ui-verification/detail-reviewed/results.json`, with captures in the same
directory. The final retained result is in
docs/design/detail-verification/results.json.
The older `light-console` and `reviewed` directories record superseded builds.
See [design-qa.md](../design-qa.md) for visual evidence and limits.

The checks cover the fixed 1120 × 680 DIP main window and 880 × 600 DIP Devices
dialog, control inventory and bounds, all **64 UI route-state combinations**,
independent A/B controls, saved mute intent, missing-device labels, cable-picker
filtering, typed gain validation, automation patterns, contrast targets, clipping
display, offline behavior and bindings. They also require a fixed opaque window
with named embedded minimise/close actions on both windows and a continuous
five-strip console without rounded card outlines. Dedicated input/output meters
and the high-contrast presentation remain part of the checks. Embedded minimise
and restore response, Devices navigation/close, preview main-window close and the
caption's right-click system menu were checked in a separate silent preview.
Mouse/keyboard interactions used no real audio. The app-header close action retains close-to-tray
behavior; explicit Quit retains its engine-stop meaning.

The control-detail refinement uses #081014 recessed wells against #566168 slate
(about 3.02:1, increased from about 1.51:1 for the prior well), square beveled
buttons, 40 × 24 DIP grooved fader caps, a 14-DIP outer track with 12-DIP filled
travel, explicit A/B labels and an 18-DIP gain readout with 10-DIP scale labels.
Active travel uses #79B99A green, distinct from the light #B9E3CB cap; off-route
caps remain grey and fully visible. The cap's 2-DIP corner radius is limited to
that functional control; strip-card frames remain absent.
Filled travel shows the gain position; it is not a signal meter. Generated
reference levels and meter activity must not replace saved levels or real
telemetry. No new DSP or audio function is included.

In the final silent preview, clicking a source A fader thumb and pressing Up
changed its readout to +0.5 dB while B remained 0 dB and the A route stayed off.
The preview was then closed; those edits were not applied to the live engine.

The 100%, 125% and 150% captures are **render-density simulations**. Windows display
settings were not changed. Actual display-scaling transitions and complete screen-reader
speech behavior remain later checks. The Alt+Space system-menu popup was not
observed in this environment and remains unverified; right-click caption access
was observed. UI route projection is not a test of the
native mixer's signal arithmetic, queues, drift correction, IPC recovery or audio
hardware. Keep every native and hardware case below unqualified until run.

## 1. Prepare and record the environment

1. Record the commit or source revision, local date/time with timezone, Windows
   edition/build, CPU, audio driver versions, and .NET/build-tool versions.
2. Inventory endpoint names, endpoint IDs, stable IDs, direction, state, channels,
   and sample rate. Retain IDs before changing friendly names. The diagnostic CLI
   lists devices without starting capture or playback:

   ```powershell
   & ".\artifacts\AudioMixer\AudioMixer.Engine.exe" --list-devices
   ```

3. Record the current Windows default playback, recording, and communications
   devices; the user's per-app assignments; and the Voicemeeter configuration.
   **Voicemeeter need not be removed.** Keep it installed for rollback. During live
   qualification, close its engine temporarily if it owns endpoints, changes
   device formats, or duplicates a route. Do not remove existing microphone or other
   hardware software.
4. Use headphones and modest levels for microphone monitoring. Keep Windows
   **Listen to this device** off on all virtual helper endpoints. Verify the test
   never routes the received Discord/game conversation back into B.
5. Confirm a quiet test period. Driver installation, changing Windows defaults,
   disabling endpoints, sleep/hibernate, and restarting Windows Audio affect other
   apps. Perform these only when included in the later user's authorized test
   scope; do not restart the service merely to obtain a complete report.
6. Record reference hardware actually present. Examples include a USB
   microphone, USB DAC output, HDMI monitor audio, and Bluetooth earbuds. Do not infer presence
   from the plan. Without a second physical microphone, mark simultaneous
   two-microphone hardware qualification blocked while testing both paths
   synthetically.

### Build first, without assuming success

From the repository in PowerShell, inspect prerequisites and then run:

```powershell
.\scripts\build.ps1
```

Preserve complete build errors. The intended package is
`artifacts/AudioMixer/AudioMixer.exe` plus `AudioMixer.Engine.exe` and the required
runtime/dependency files. Resolve build failures before live testing. A successful
compile is a separate result from any runtime test. Confirm the package launches
on the target Windows 11 x64 system without relying on a developer shell.

## 2. Qualify the free virtual-driver path

Use official x64 distributions of **base VB-CABLE** and **Virtual Audio Cable Lite**.
Do not confuse VAC Lite with Trial, and do not substitute paid cable bundles.
Record exact versions, source URLs, installer hashes, signer verification, and
whether Windows requires a reboot. Keep signed package contents intact and retain
the user's existing Windows security settings. A signature alone does not prove
the driver loads or that the transport is stable.

Authoritative setup references:

- [VB-CABLE download](https://vb-audio.com/Cable/),
  [reference manual](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf), and
  [licensing](https://vb-audio.com/Services/licensing.htm).
- [VAC downloads](https://vac.muzychenko.net/en/download.htm),
  [Lite restrictions](https://vac.muzychenko.net/en/manual/features.htm), and
  [licence](https://vac.muzychenko.net/en/license.txt).

Check the installed package's actual endpoint names and capabilities. The planned
mapping is:

| Windows list / driver role | Friendly name | Application binding | Enabled |
|---|---|---|---|
| Outputs / VB-CABLE ordinary playback | Audio Mixer Virtual Input | `virtualInputPlayback` (app destination) | Yes |
| Inputs / VB-CABLE recording | Audio Mixer Internal Receive | `virtualInputCapture` (engine capture) | Yes |
| Outputs / VAC Lite playback | Audio Mixer Internal Send | `virtualOutputRender` (engine B render) | Yes |
| Inputs / VAC Lite recording | Audio Mixer Virtual Output | `virtualOutputCapture` (Discord/game microphone) | Yes |
| Outputs / VB-CABLE alternate multichannel playback, if supplied | Audio Mixer Unused Multichannel | None | Disable only after the test below |

This is **five installed endpoints, normally four enabled**, for the package
described in the plan. The two internal helpers remain enabled. Renaming does not
make them unnecessary. If a downloaded version has a different endpoint inventory,
document the difference and investigate; do not disable an endpoint by guesswork.

Configure both virtual paths consistently for stereo 48 kHz. VAC Lite uses
16-bit transport. Keep shared-mode Windows audio; do not open exclusive WASAPI or
Kernel Streaming paths for this release. Physical devices may use their supported
shared-mode mix formats, with conversion at the engine boundary.

Run this gate before treating the mixer as a daily replacement:

1. Play a known stereo file from Spotify or a local player assigned specifically
   to **Audio Mixer Virtual Input**. The App Audio meter must move. With App Audio
   A enabled and B disabled, only the selected A hardware outputs carry it.
2. Select one physical microphone explicitly. Enable its B send and leave its A
   send off. A raw local capture app using **Audio Mixer Virtual Output** must
   receive it. Enable App Audio B briefly and confirm it joins the same capture.
   Do not use a call participant as a test instrument without their agreement.
3. Open B in Discord and a second ordinary shared-mode capture client at once.
   Confirm both receive current audio and neither invalidates the engine's B
   stream. Repeat with mono capture requested by a client. Record actual formats.
4. Continue for **more than 35 minutes**. Confirm the installed edition is Lite
   and that no Trial spoken reminders appear. This run alone does not establish
   eight-hour stability.
5. With all four required virtual endpoints healthy, disable only the unused
   VB-CABLE alternate. Restart the application, then reboot in a later authorized
   window. Recheck app input, A, and B after each action. If required partners are
   affected, re-enable the alternate and report the assumption as failed.
6. Recheck Windows default devices and restore unintended installer changes.

If the free driver path cannot pass, record a release blocker. Do not buy, bundle,
or silently substitute another driver. VAC Lite's private-home-use licence remains
a dependency assumption; public/commercial distribution requires a fresh review.

## 3. Add and run deterministic native checks

Use the actual `am::Mixer`, `am::FrameQueue`, `am::AdaptiveResampler`, and
`am::DriftController` implementation. Build a separate harness without physical
audio devices. Register a test target only as part of the future testing pass;
document its actual commands once added. Do not modify production behavior merely
to make test observations pass.

| Area | Cases and required assertions |
|---|---|
| All routing combinations | There are six independent send switches across three sources: **2^6 = 64** combinations. Supply distinct deterministic stereo signals; after ramps settle, verify A/B contain precisely the enabled sources, with no cross-bus leakage. |
| Levels and mutes | Exercise independent A/B gains, all three source mutes, and both bus masters/mutes. Changing A never changes B. Test accepted gain boundaries and rejected nonfinite/out-of-range control values. |
| Smooth transitions | Route, mute, and gain changes settle over 480 frames at 48 kHz (10 ms). Check continuity, interruption of an in-progress ramp, and startup fade without assuming an instantaneous target value. |
| Signal correctness | Verify silence, stereo preservation, boundary mono/channel conversion, gain arithmetic, saturation at [-1,1], clipping flags, and meter range [0,1]. NaN/Inf samples must become finite safe output; extreme finite float input must not overflow the accumulator into invalid output. |
| Queue integrity | Empty/full queues; wraparound; partial pushes/pops; arbitrary chunk sizes; dropped-newest semantics; counters; consumer-only clear; producer requestClear while the consumer is active. Use frame sequence numbers and a concurrent stress run to detect tearing, duplication, or overwrite races. |
| Independent fanout | Two A queues receive the same source timeline. Pausing, overflowing, clearing, or destroying one consumer never steals samples from or blocks the other queue or B. |
| Streaming conversion | Exercise 44.1/48/96 kHz and mono/multichannel boundary conversions. Split input into irregular packets and constrain output spans so unconsumed input and filter history must survive calls. Reset must discard pending old samples. Compare with a continuous reference using appropriate filter tolerances. |
| Clock drift | Simulate each source and output at ±100, ±500, and ±1000 ppm for at least one virtual hour. Introduce bounded scheduling jitter. Correction must remain finite within ±2000 ppm, change smoothly, and settle with bounded queues and stable delay after startup. |
| Discontinuities | Simulate long device absence, a sudden timestamp jump, queue overflow, format change, and resume. Reprime and reset locally; no burst of queued old speech, catch-up processing storm, or effect on healthy paths. |
| Realtime work | After initialization, instrument mixer/resampler/queue paths for allocation and blocking. Separate control-side copies, endpoint discovery, logging, and recovery from audio processing. Do not equate source inspection with a measured realtime guarantee. |

The stable endpoint identity test must use two devices with the same friendly name:
removing the selected one must not silently bind the other. Test exactly one desired
binding per distinct physical slot and rejection of duplicate microphone/A-output
assignments. Missing-device selections must remain in the configuration.

## 4. Exercise the desktop application and control protocol

Launch `artifacts/AudioMixer/AudioMixer.exe`. Use
[docs/CONTROL_PROTOCOL.md](CONTROL_PROTOCOL.md) to capture snapshots and revision
values; there is no documented `--snapshot` CLI command. The engine and UI are
separate processes. Audio remains in the engine, never in pipe messages.

### First launch and controls

- Use isolated settings or a backed-up test profile. Default microphones and
  hardware outputs are unassigned; microphone sends are off; App Audio A is on
  and B is off; start-at-login is off.
- Confirm exactly three source strips, exactly two physical A assignments, one B
  virtual output, independent send toggles/sliders, global source mutes, and A/B
  master controls. Helpers belong in setup rather than physical selectors.
- Bind actual devices. Verify channel selection with a multichannel input and
  dual mono for a chosen microphone channel; App Audio must retain stereo.
- Verify meters and connection states reflect current audio. Distinguish clipping
  in the mixer from clipping or processing introduced by Discord/the game.
- Change Windows defaults. Explicit assignments must stay on their selected IDs.
  A missing headphone must never switch automatically to unselected speakers.
- Close the window: it must stay in the tray and mix. Restore it and confirm live
  state. Explicit Quit must stop the engine, with no restart loop.

### Persistence and protocol

The default settings location is `%LOCALAPPDATA%\AudioMixer\settings.json`, with
`settings.json.bak` for last-good recovery and `.new` for a pending write. Use the
snapshot's actual `settingsPath` and `logPath` as the source of truth. Back up all
settings before deliberate damage and restore them afterwards.

1. Change assignments, channels, six sends, gains, mutes, masters, and startup
   choice. Confirm the committed revision advances. Quit/reopen and reboot in the
   authorized window; confirm every saved choice returns.
2. Send a configuration with a stale `expectedRevision`. It must be rejected;
   retrieve the current snapshot before retrying. Reconnect a controller and
   confirm it retrieves a full current snapshot rather than replaying stale edits.
3. Check malformed JSON, oversize requests, unsupported version, wrong array
   counts, invalid channel/gain, invalid UTF-8 device identifiers, wrong-direction
   devices, and virtual helpers assigned as physical devices. The engine must
   reject invalid changes without interrupting its current valid mix.
4. From a separate Windows user/session, verify the current-user pipe cannot be
   used to read or change the mix. Verify remote pipe access is rejected.
5. Stop the engine normally; use a **copy/test profile** to corrupt its primary
   settings while leaving a valid backup. Restart and verify the backup and clear
   warning. Repeat with both invalid: safe unassigned defaults and a warning must
   appear, with damaged originals preserved. Simulate an interrupted `.new` write;
   the committed primary must still load. Check a read-only/unwritable settings
   location reports failure and retains the current valid configuration.

### Process independence and recovery

- Record engine PID and advancing `audioFrames`. Terminate only the desktop UI
  process, then reopen it. Engine PID and current audio must survive, and controls
  must reconnect to the same configuration.
- Suspend/freeze the UI only. Audio must continue. Restore the UI and inspect the
  latest meters and configuration.
- With the tray controller active, terminate only the engine. Verify bounded
  restart attempts, committed settings restoration, and current live audio. Check
  whether already-open Discord/game capture clients recover without reselecting
  B. Preserve any client-specific failure as a compatibility result.
- Freeze the engine's audio processing while its control thread remains
  responsive, using a debugger or explicit fault harness. A non-advancing
  `audioFrames` heartbeat must trigger bounded recovery. A test that freezes only
  the pipe does not prove audio-stall detection.
- Repeated launch failures must produce a clear recovery-limit message instead
  of a rapid endless restart loop. Manually recover after correcting the cause.

## 5. Run the physical-device fault matrix

Use a continuous, uniquely identifiable live source and a raw B capture, then log
timestamps, endpoint state, affected stream, `audioFrames`, queue levels, underrun/
overrun/reconnect counts, and audible results. Start with conservative buffers.

For each device loss, check that **only the affected worker** recovers. Healthy
paths must not stop, reset their clients, lose assignments, or replay old audio.
Measure reconnection from Windows reporting an **active, successfully openable**
endpoint, not from the start of Bluetooth pairing. Target current audio within two
seconds on reference hardware; report actual values and outliers.

| Action | Required result |
|---|---|
| Remove A1, then reconnect it | A2 and B continue; A1 selection remains visible and rejoins current live audio. |
| Remove A2, then reconnect it | A1 and B continue; A2 recovers independently. |
| Remove both A outputs | B and mixer frame counter continue without a hardware master. Reconnect in both orders. |
| Remove either microphone | Only that source becomes silent. Other microphone, App Audio, A and B continue. |
| Start with saved devices absent | Desired selections/levels/routes remain. Connect each device individually; no reselection or global restart. |
| Disconnect/reconnect B helper | A continues. B reports its state, opens fresh transport on return, and sends current audio. |
| Disable/re-enable selected endpoint | Same local recovery; no substitution of a similarly named device. |
| Change shared format | Local renegotiation/conversion; no old-format distortion or global restart. |
| Endpoint held exclusively by another app | Affected path reports busy and retries after release; healthy paths continue. |
| Block microphone access | Accurate blocked/access-denied state, recover after permission returns. |
| USB unplug, DAC power cycle | Repeat for each actual hardware endpoint; no persistent dead handles. |
| HDMI display sleeps/disconnects | Other A and B remain live; reconnect when endpoint becomes usable. |
| Bluetooth earbuds disconnect/reconnect | No global stall; measure radio negotiation separately from stream reopening. |
| Select Bluetooth headset microphone | Record hands-free profile/format changes and resulting playback quality. Opening an unselected headset microphone is a failure. |
| Lock/unlock; sleep/resume; hibernate/resume | Clear old queues/timing state; resume current live audio without backlog. |
| Windows Audio service restart | Run only when explicitly authorized. Clear service-unavailable state and recover fresh endpoints after service returns; preserve selections. |

Repeat USB/A-output reconnects at least 20 times and alternate fast/unavailable
intervals. Test a notification arriving during an ongoing retry. Include repeated
format/property changes; a rename should not require replacing the saved endpoint.
For audible gap claims, use recording/analysis rather than a casual listen alone.

## 6. Measure application compatibility, latency, and stability

### Compatibility

Exercise Spotify, a browser, Discord, and representative games the user actually
uses. Assign music explicitly to **Audio Mixer Virtual Input**; assign each
capture client explicitly to **Audio Mixer Virtual Output**. Verify Windows/app
volume changes and ordinary default-device changes do not bypass explicit routes.

Capture B with an unprocessed local recorder before judging Discord quality.
Record Discord suppression/automatic gain settings and any changes made for the
test. Keep received conversation audio outside App Audio whenever App Audio is
sent to B. Test simultaneous shared capture clients, client restart, engine restart,
and client remaining open during B recovery. Do not claim support for an untested
game from one successful Discord call.

### End-to-end latency

Measure these four paths separately: microphone→A, microphone→B, app→A, app→B.
Report A1 and A2 individually; report Bluetooth separately. Use repeated impulses
or a known correlation sequence and simultaneous reference/output recordings with
a documented clock/loopback arrangement. For microphone paths, use an appropriate
physical or electrical reference and account for reference-capture delay. A
physical-output measurement must include actual DAC/transport latency, not merely
the engine's queue estimate.

Report method, format, device/buffer periods, at least 20 measurements, median,
95th percentile, minimum/maximum, and uncertainty. Use the native device timestamp
only where it has been shown reliable. Engine queue milliseconds are diagnostics,
not a substitute for end-to-end latency. Aim for wired microphone monitoring below
50 ms; do not state this target was met without measurement. Independent speakers
may have different audible delays; precise speaker alignment is outside v1.

### Long runs

- Run all available sources and both A outputs plus B for at least eight hours
  under representative CPU/UI/application load. Log process memory, CPU, frame
  progress, queue levels, errors and recoveries at a bounded rate. Check for
  continuing memory/latency growth after initialization settles.
- Run a separate overnight soak. Include authorized device absences/reconnections
  and ordinary lock/sleep/resume scenarios; annotate every intervention.
- Inspect queue stability and underrun/overrun trends, not just whether a process
  is still present. Investigate sustained counters or audible dropouts. Keep logs
  bounded and verify they contain no microphone samples or conversation content.
- Compare before/after route settings and endpoint identities. Save enough
  diagnostics to reproduce failures without recording private audio by default.

## 7. Release decision and result template

All required scenarios need evidence, or an explicit blocked/not-run status. A
release candidate is not qualified while compilation, driver transport, independent
recovery, or required compatibility is failed or untested. Distinguish code defects,
driver limitations, absent hardware, and transport-specific latency. Fix a discovered
defect and rerun the affected cases plus relevant regression cases; do not repeat
an entire overnight test after an unrelated documentation edit.

Use this report structure:

```text
Audio Mixer v1 qualification report
Source revision:
Test date/time/timezone:
Windows build and architecture:
Build tool/runtime/dependency versions:
VB-CABLE version/hash/signer/load result:
VAC edition/version/hash/signer/load result:
Physical hardware and endpoint IDs (redact identifiers in public reports):
Virtual endpoint role/name/ID/format mapping:
Build/package result and log:

Case ID and scenario:
Status: Passed | Failed | Blocked | Not run
Preconditions and exact routes/formats:
Action and timing/reference method:
Expected result:
Observed result:
Evidence: timestamps, snapshot revisions, frame counters, logs/recording analysis
Affected device only or other paths interrupted:
Recovery duration from active/openable endpoint:
Defect/reproduction steps and regression cases:

35+ minute Lite/Trial check duration/result:
DSP routing 64/64 and drift-harness results:
UI independence and engine restart results:
Microphone→A1/A2 latency statistics:
Microphone→B latency statistics:
App→A1/A2 latency statistics:
App→B latency statistics:
Bluetooth results separately:
Eight-hour run duration and memory/queue/counter trends:
Overnight run duration and trends:
Unavailable hardware/apps and remaining gates:
Windows/Voicemeeter/settings changes restored:
Release decision: Qualified | Not qualified
Reason and next concrete work:
```

## Copyable prompt for the later testing pass

```text
Test and qualify the Audio Mixer v1 in this repository using TESTING.md.
Audio and hardware testing was intentionally deferred. Read the recorded inert
UI checks separately; they do not establish native audio correctness. Read the
code and current build instructions first. Build the
application, add and run a deterministic native DSP/queue/resampling harness
covering all 64 routes and independent clock drift, and fix demonstrated defects.
Report build success separately from runtime and hardware evidence.

Inspect the actual endpoint inventory and use the free base VB-CABLE + VAC Lite
transport mapping. Keep existing mixer and microphone software installed; close Voicemeeter
temporarily during live audio tests where needed, and preserve rollback settings.
Do not uninstall or disable existing devices automatically. Ask me to perform
physical unplug/replug actions when needed and coordinate audible live tests.
Do not restart Windows Audio, reboot, sleep/hibernate, install drivers, or change
Windows defaults until we have explicitly agreed that disruptive test step.

Qualify independent A1/A2/B behavior, per-source routing and levels, missing-device
startup, automatic reconnection without stale audio, UI/engine process separation,
settings recovery and revision handling, Discord/game shared capture, 35+ minutes
without Trial reminders, measured end-to-end latency, an eight-hour run and an
overnight soak. If a long run cannot be completed in this turn, report it as not
run and arrange the next step with me; do not invent results or silently schedule
future work. Mark absent second-microphone hardware and unavailable applications
blocked. Keep microphone content out of diagnostics unless I authorize a specific
recording. Produce the report template from TESTING.md with evidence, remaining
gates, and an honest qualified/not-qualified decision.
```
