# Application audio nodes

An **Application** node can capture a running Windows application's audio without adding a separate virtual cable for that application. Choose Spotify, a browser, a game, or another listed application, then connect its audio socket to a mix, volume node, or output. The list includes applications with audio sessions and ordinary visible applications, so an application can be selected before it starts playing. The mixer never opens a microphone or starts playback while discovering this list.

## What Windows makes possible

Windows provides process loopback capture through `ActivateAudioInterfaceAsync`. It captures the selected process and its descendants, independently of the playback endpoint. Microsoft documents a minimum of Windows build **20348**; the project's Windows 11 target meets that requirement. An application with no rendering streams contributes silence. [Microsoft application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/), [process-loopback parameters](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params)

**Capture copies audio; it does not move or silence the application's original playback.** If Spotify already plays to your headphones and its node is also connected to those headphones, you can hear it twice. A useful first use is sending selected application audio to the virtual microphone output while its ordinary playback remains your listening path. For mixer-only listening, explicitly assign that application's playback to the installed virtual input in Windows Volume Mixer, then choose one capture path in the graph. Do not simultaneously connect both its Application node and the combined Virtual input to the same destination: both paths can contain the same audio. This is a routing consequence of loopback capture, not an automatic routing feature. [Microsoft loopback recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)

Applications that render through a shared browser process may arrive together; a browser tab is not generally an independently selectable application. Protected playback may be unavailable to loopback capture. The mixer does not bypass protected content or change Windows audio policies. [Microsoft process-loopback parameters](https://learn.microsoft.com/en-us/windows/win32/api/audioclientactivationparams/ns-audioclientactivationparams-audioclient_process_loopback_params), [Microsoft loopback recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)

## Microphone audio in Discord screen shares

**Listening output mute only silences that output node.** A microphone can feed
both a physical listening output and a virtual microphone output. Muting the
physical branch leaves the virtual branch available to Discord voice chat.
Muting the microphone source silences its contribution to every connected branch;
muting the virtual output silences the mix sent to Line 1.

The virtual microphone renders audio to **Line 1's playback endpoint**; VAC Lite
exposes it through the matching recording endpoint. Earlier builds used shared
playback for this send. Windows process loopback can capture render streams across
endpoints, so the virtual send could appear in screen-share audio even while the
physical output was muted. Discord's whole-screen/system-audio option includes
other applications' playback.
[Microsoft application loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/),
[Discord sharing guide](https://discord.com/blog/how-to-stream-to-discord-from-desktop-or-mobile)

**Virtual output now connects directly to VAC Lite using exclusive WASAPI.**
This keeps the microphone send out of Windows' shared playback mix while the
paired Line 1 recording endpoint remains available to voice applications.
Physical listening outputs and microphone capture still use shared mode. The
change needs no graph migration and does not link the two output mute controls.
[Windows initialization modes](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient-initialize),
[VAC audio layers](https://vac.muzychenko.net/en/manual/layers.htm)

The renderer negotiates a supported format and event period, handles aligned
buffers with a fresh client, and services complete exclusive-mode buffers. If
direct access is disabled, busy, or unsupported, the virtual node reports the
problem and stays stopped. It never silently falls back to shared playback,
which would reintroduce the leak. If Windows disallows access, enable **Allow
applications to take exclusive control** for the Line 1 playback device.

To silence the mix delivered through Line 1, mute **Virtual output**; to silence
only the microphone across mixer routes, mute its **Microphone** node. Discord
must use Line 1 as its voice input for virtual-output mute to control voice chat;
selecting the physical microphone bypasses this route. An application deliberately
recording Line 1 still receives the virtual microphone. Unmuting a physical output
that carries the microphone also makes that physical playback capturable.

The 19 October 2026 investigation found this saved graph:

```text
Shure MV7+ -> Mix -> Voice Color -> Expander / Gate
                                    |-> Listening output (xDuoo): muted
                                    `-> Virtual output (Line 1): unmuted
```

The user reported whole-screen sharing with system audio. An observation-only
probe confirmed the shared-mode leak, then measured the same paths after the
requested update and restart on Windows 11 build 26200:

| Ten-second observation | Line 1 recording RMS | Mixer process-loopback RMS | Process frames |
|---|---:|---:|---:|
| Before: shared virtual send | 0.0153695 | 0.0156277 | 480,000 |
| After: direct virtual send | 0.0286722 | **0 exactly** | 480,000 |

The two intervals contain naturally occurring input, not an identical test signal;
their levels are evidence of signal presence, not a gain comparison. The direct
path retained live Line 1 audio with no capture queue drops, while every measured
mixer loopback sample was zero. System capture still received unrelated playback.
No test tone was played and no audio recording was saved. This verifies Windows
capture behavior with the physical branch muted; an end-to-end Discord receiver
listening check remains separate.

The candidate passed 226 fake-client exclusive-render checks and 27 endpoint
inventory checks. The prior graph test also covers independent physical/virtual
mutes and state adoption. Activation restored all previously running assigned
paths, opened a responsive app window, and preserved the saved settings file byte
for byte. Evidence and the original engine backup are in
[`artifacts/stream-isolation-20261019-125828`](../artifacts/stream-isolation-20261019-125828/).

## Implemented behavior

- Discovery combines visible applications and Windows render audio sessions in the current login session. Multi-process applications are grouped under a parent with the same executable path where possible. The displayed name stays stable as a song or browser title changes.
- The saved binding contains the exact executable path and a live process-ID hint. A restart reconnects only to that path. If several independent instances are found after the selected instance exits, the node waits for a new explicit selection instead of guessing.
- Selecting a process uses Windows' include-process-tree mode. Other unrelated applications are excluded. The mixer and its launcher ancestors are excluded to prevent capture of its own output through a process tree.
- Each application has its own worker, event, bounded queue, drift adjustment, status, and recovery. An absent or paused application does not stop other sources or outputs.
- The worker requests stereo 48 kHz float capture and converts through the existing streaming resampler for gradual clock correction. Returning audio fades in over 10 ms. Disconnects, long pauses, and discontinuities discard stale queued/filter audio.
- Activation happens asynchronously with an independently owned callback lifetime. Stop and selection changes interrupt the wait. A Windows activation timeout waits for a deliberate retry rather than creating an unbounded series of outstanding activation requests.
- Discovery and capture never change application session volume, mute, output assignment, Windows defaults, or driver settings. No microphone content is recorded to disk.

The bundled MinGW headers do not include `audioclientactivationparams.h`, so the implementation declares the documented 12-byte activation structure locally and resolves the Windows function at runtime. The constants and layout follow [Microsoft's published SDK header](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/audioclientactivationparams.h). The capture sequence follows the [Microsoft sample implementation](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/ApplicationLoopback/cpp/LoopbackCapture.cpp).

## Later validation prompt

Use this prompt after you are ready to hear and qualify audio:

> Qualify Application nodes using docs/APP_AUDIO.md and TESTING.md. Preserve Windows defaults, existing app assignments, and current listening levels unless I explicitly approve a change. First inspect the graph for duplicate paths. Begin with output muted and a low saved level. Use a local non-protected audio file in one application; check that another application's audio is absent from the app node. Test Spotify only as a separate compatibility check. Verify pause/resume, app exit/relaunch, multiple instances, process children, output-device changes, and sleep/resume. Measure bounded queue occupancy and reconnection time. Confirm original playback remains audible independently when expected and prevent doubled monitoring. Record exact Windows/app versions, steps, observed results, and limitations; do not claim reliability from a successful compile.

Required acceptance checks:

1. **Isolation:** two applications play different local test material; the selected application's capture contains only its intended process tree.
2. **Routing:** app → virtual microphone works while either or both physical listening outputs are missing. A source with no downstream connection stays silent at outputs.
3. **Original playback:** disconnecting its node does not alter the application's original playback, mute, volume, or Windows assignment.
4. **No doubled source:** compare a direct Application node with a Virtual input route. Ensure only the intended copy feeds each destination.
5. **Idle/recovery:** pause for at least 30 seconds, resume, exit, relaunch, and restart after sleep; no stored music resumes from the previous session.
6. **Identity:** relaunching the same path reconnects. Reused PIDs, a similarly named executable elsewhere, and ambiguous independent instances do not substitute another source.
7. **Multiprocess:** test Spotify/browser child-process restarts and document whether one browser node contains multiple tabs.
8. **Failure:** unavailable/denied/protected capture produces a readable state or silence without failing another graph path; activation timeout and cancellation remain bounded.
9. **Soak:** monitor memory, queue depth, underruns, reconnects, and latency over the existing eight-hour/overnight qualification plan.

Implementation and compilation do not constitute hardware/application qualification. No live application audio capture or listening test was performed while implementing this feature.

Development checks on 17 October 2026: the application-capture translation unit compiled with C++20 and warnings enabled; read-only discovery returned 13 distinct applications with valid saved identities; 25 unassigned worker start/retry/stop cycles completed in 5 ms with empty queues. Those checks did not configure a capture target or open a render stream.
