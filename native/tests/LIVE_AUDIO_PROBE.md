# Virtual audio probe

`live_audio_probe.cpp` is an opt-in diagnostic, separate from the desktop app. It links the production WASAPI workers, process capture worker, queues and resampler. It never writes personal settings, changes Windows defaults, opens physical endpoints, or writes audio samples to disk. Output JSON contains metrics only.

**Only run it after checking the current routing.** A virtual endpoint can still be routed to headphones or another person's call by a different application. The stimulus is a 250 ms stereo chirp at a fixed peak of −48 dBFS, after two seconds of silence. No automatic volume changes are made.

## Build

Run from the repository root in PowerShell (the normal build must already have fetched dependencies):

```powershell
& '.\.tools\llvm-mingw-20261006-ucrt-x86_64\bin\clang++.exe' `
  -std=c++20 -O2 -Wall -Wextra -Wpedantic `
  -DUNICODE -D_UNICODE -DNOMINMAX -DWIN32_LEAN_AND_MEAN -D_WIN32_WINNT=0x0A00 -DWINVER=0x0A00 `
  -I native/include -I build/native/_deps/json-src/single_include -I build/native/_deps/samplerate-src/include `
  native/tests/live_audio_probe.cpp native/src/windows_audio.cpp native/src/process_capture.cpp native/src/dsp.cpp `
  build/native/_deps/samplerate-build/src/libsamplerate.a `
  -lole32 -luuid -lavrt -lpropsys -ladvapi32 -lwinmm -luser32 -static-libgcc -static-libstdc++ `
  -o build/native/VineMix.LiveAudioProbe.exe
$env:PATH = "$PWD\.tools\llvm-mingw-20261006-ucrt-x86_64\bin;$env:PATH"
```

## Read-only inventory

```powershell
& .\build\native\VineMix.LiveAudioProbe.exe --list
```

This enumerates endpoints without opening streams. All streaming requires `--run`. Both render and capture endpoint IDs are required and validated as active, correctly directed VB-CABLE or VAC Lite endpoints. Physical endpoint IDs are rejected.

## Cable round trip

```powershell
& .\build\native\VineMix.LiveAudioProbe.exe --run `
  --render '<explicit virtual playback ID>' --capture '<explicit virtual recording ID>' `
  --consumers 2 --seconds 6 --output '<absolute result.json>'
```

Two consumers use two independent production WASAPI capture clients on the selected endpoint. Select the matching VB recording endpoint for a direct VB test. Select VAC recording only after an isolated engine graph explicitly routes VB capture to VAC render and the route is safe to stimulate.

## Passive shared-consumer compatibility

```powershell
& .\build\native\VineMix.LiveAudioProbe.exe --run --capture-only --observe-only `
  --capture '<explicit VAC virtual recording ID>' --consumers 2 --seconds 6 --output '<absolute result.json>'
```

This opens two shared capture clients on the existing virtual endpoint, renders nothing, and retains only running frame/peak/finite-value counters. Audio samples are discarded after counting; no reference chirp is expected. A pass establishes that both clients opened and delivered more than one second of finite audio without queue drops. Silence is valid. It does not establish virtual-output signal quality, end-to-end routing, or Discord/game processing behavior. Process targets and physical capture endpoints are refused in this mode.

## Per-process loopback

Production capture correctly rejects itself and its ancestors to prevent feedback. Use two **sibling probe processes** instead of weakening that protection:

1. Start a hidden render probe with `--run --render-only --render <VB playback ID> --start-file <unique gate path> --ready-file <renderer marker.json> --seconds 6 --output <renderer result.json>`. The gate must not already exist. Read its process ID from the marker. No streams are opened before the gate appears.
2. Start a second hidden probe with `--run --capture-only --process-target <renderer PID> --process-path <exact same probe executable> --seconds 8 --ready-file <capture marker.json> --output <capture result.json>`.
3. Wait until the capture marker's stage is `capture-ready`. Create the renderer gate promptly, before the eight-second capture window ends.
4. Wait for both processes and inspect their result files. Use unique paths for every run. Capture is restricted to another instance of this same diagnostic executable.

To measure the application's process-input node instead, bind that node to the waiting synthetic renderer PID and exact executable path, route it only to the isolated virtual sink, and use a separate `--capture-only --capture <virtual recording ID>` probe. No real app needs to play a test signal.

## Interpretation

Each capture reports matching-channel correlation, gain, opposite-channel correlation, observed frame rate, nonfinite count, queue drops, and worker state/counters. Built-in pass means both chirps were recognized with correlation above 0.85 left / 0.80 right and no nonfinite samples; check gain, crosstalk and counters separately against the intended route.

`arrivalLatencyMs` is an approximate submission-to-arrival result, including the diagnostic queues, WASAPI and virtual transports. It is **not calibrated acoustic latency**. For separate renderer/capture processes, subtract renderer `injectionClockSeconds` from capture-channel `arrivalClockSeconds` and multiply by 1000. Shared Windows monotonic clock values are used. Several milliseconds of capture delivery/queue timing uncertainty remain.

The probe temporarily requests 1 ms timer scheduling for its own process and releases that request on exit. It does not modify device sample formats. The production worker converts to internal 48 kHz stereo. Capture samples remain in memory for correlation analysis and disappear when the process exits.
