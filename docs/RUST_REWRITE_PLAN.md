# VineMix in Rust — rewrite and optimization plan

Status: **in implementation** on branch `rust-rewrite` (plan drafted 6 November
2026; target is the released 0.5.0 Beta). The legacy trees (`native/`,
`desktop/`, `installer/`) stay in place as the behavioural oracle until each
Rust replacement has proven parity.

## 0. Implementation status (updated 7 November 2026)

Done and verified:

- **Model and DSP core**: `vinemix-model`, `vinemix-dsp`, `vinemix-denoise`,
  `vinemix-resample` are bit-exact against the pinned 0.5 C/C++ through
  `vinemix-oracle`. (Since 11 November 2026 Voice Color is a deliberate
  exception; see section 0.3.)
- **Engine**: `vinemix-engine` (`VineMix.Engine.exe`) with WASAPI and process
  capture, the configuration store, the control protocol and single-instance
  ownership.
- **Desktop**: `vinemix-ui` (`VineMix.exe`, egui/eframe) with the node canvas,
  all node controls, the EQ editor, Voice Color pad, meters, menus using the
  original `MenuItem` template, the settings menu with filtered search and
  control results, Find node, Help with the shortcut reference and all five
  practice lessons, About, Credits, What's new, quit and recovery flows, tray,
  start at login, legacy workspace migration and update check/download.
- **End to end**: the Rust desktop drives the Rust engine. Connection, live
  status, edits, saves, undo/redo, keyboard nudges and a clean Settings → Quit
  (save, stop engine, exit) were exercised against an isolated test workspace.
- **Updates** (`crates/vinemix-ui/src/update`): private owner-only staging,
  the signed handoff, the `--apply-update` worker (backup, Setup or standalone
  replacement, smoke check, candidate health, guarded rollback and RunOnce
  recovery), `--update-health`, `--update-reverted` and pending-update recovery
  at startup. Worker scenarios run against ephemeral signed releases. The
  transaction records were cross-checked in both directions against the 0.5
  desktop's own record types and `JsonSerializerOptions`, so an upgrade from
  the installed 0.5 release (whose C# worker drives it) hands over to the Rust
  candidate.
- **Distribution**: `--release-smoke`, the standalone single-file build
  (`--features standalone` with an embedded, hash-verified runtime archive),
  the shell-parented data-context relaunch (`--standalone-context`), the Rust
  Setup detection helper (`crates/vinemix-setup-helper`), version resources
  with the numeric `FileVersion` the update gate requires, the application
  manifest and icon, the Rust crate licence inventory
  (`tools/rust-notices.py` → `licenses/Rust-crates.txt`) and
  `scripts/build-rust-release.ps1` (installed payload with its package
  inventory, standalone executable, Setup).

Development isolation: `VINEMIX_DEV_INSTANCE=<name>` scopes the pipe, mutexes,
show events and data folder (`%LOCALAPPDATA%\VineMix.dev-<name>`), never owns
the per-user Run entry, never installs updates and may coexist with an
installed copy's engine. `VINEMIX_UI_AUTOMATION=<script>` (development
instances only) drives the desktop with scripted input and saves egui
screenshots; see `crates/vinemix-ui/src/ui/automation.rs`.

Not yet exercised: a real installed upgrade (0.5 → Rust through Setup and the
worker on a disposable Windows account or VM), signing, and publication. These
change the user's machine or public release state, so they need the
maintainer's go-ahead. Remaining documentation work: `docs/DISTRIBUTION.md`
and the user guides still describe the .NET/C++ packages.

Reviewed on 10 November 2026: [RUST_REVIEW_2026-11-10.md](RUST_REVIEW_2026-11-10.md)
records 41 findings against the 7 November state and what was done about
each. "Done and verified" above means what it meant on 7 November: parity and
the listed scenarios. It does not mean release-qualified. Section 0.3 lists
where the code differs from the plan below, and [RUST_BUILD.md](RUST_BUILD.md)
is the current build and verification entry point.

Baseline: the 0.5.0 release commit (`6acb162`) **plus** the maintainer's
post-release working-tree changes, which the installed build already
contains. The only product change among them is the processing-load
telemetry (IDEA-057, `docs/PROCESSING_LOAD.md` in the original tree), now
ported; see 0.2. The rest of those changes are website, docs and tests.

### 0.1 Visual and behavioural audit (7 November 2026)

References: the unchanged 0.5 `desktop/` built in verification mode renders
every screen and node offscreen (`--render-design --verification-dir`). The
Rust side reproduces the same fixtures with `--preview [--signal|--waiting]`
(a port of `UiPreview`), `--preview-fixture` and
`cargo run -p vinemix-ui --example design_fixtures`.

Matched after fixes: the main workspace, effects scene, all 27 node closeups,
settings menu, add menu, Find node, Help, About, Credits, Updates and What's
new. Fixed during the audit:

- Wordmark aspect (fitted by painted bounds), caption buttons (34 × 30, flush),
  auto-width toolbar buttons.
- Socket, dial-face, EQ-point and Voice Color handle sizes: egui strokes circles
  outside the radius; WPF centres pens (and keeps Shape strokes inside).
- `--preview` loaded an empty graph; it now uses the migrated 0.5 fixture and
  the desktop's own `FromLegacy` (also applied to version-1 preview data).
- Preview no longer offers Restart audio engine or What's new, and never
  contacts GitHub. Opening Updates starts a check; closing cancels it.
- The automatic schedule honours `last-successful-check.txt` (30 s, then daily;
  15 minutes after failures; 2 s after enabling) with 0.5's messages.
- What's new and downloads created the update root without the owner-only ACL,
  which made the next launch stop at update recovery. Both now use
  `create_private_directory`.
- About/Updates/Credits/What's new/quit recovery rebuilt on `ui/dialog.rs`
  (`WorkspaceDialog`): content-sized windows, quiet Close left, actions right,
  0.5 checkbox and scrollbar styles. About reports the real distribution,
  build revision and platform.
- Activity times use `HH:mm:ss`.
- Panic hooks log to `engine.log` / `desktop.log`; release binaries drop debug
  information (desktop 46.8 → 11.7 MB, engine 9.9 → 5.7 MB).
- RNNoise sparse/dense kernels register-blocked (bit-exact): 139 → 95 µs per
  10 ms frame against 91 µs for the C library.
- Accessibility: the custom widgets exposed nothing to UI Automation (0.5 sets
  323 `AutomationProperties`). Buttons, window buttons, selectors (with their
  current value), checkboxes, menu items and dials (as sliders with values) now
  report names and states through AccessKit. Still open: node-qualified names
  ("Microphone 1 level"), sockets, the layout lock, help text and live-region
  announcements for feedback messages.

Benchmarks (`cargo test -p vinemix-oracle --release --test graph_parity --
--ignored --nocapture`): starter voice chain 146 vs 135 µs per 5 ms quantum
(Rust vs C++), effects chain 183 vs 196, routing graph 75 vs 71 — all within
3–4% of one core in real time.

### 0.2 Post-release changes (7 November 2026)

**Automatic update checks (deliberate change from 0.5).** The check itself
works (`cargo run -p vinemix-update --example check_live -- 0.4.0-beta`), but
0.5 reported results only as a five-second workspace message, which
`SetMessage` drops when action notifications are off and nobody sees while
VineMix is in the tray. Now:

- the preference is opt-in: new installs and preference files without
  `automaticallyCheckForUpdates` start with it off; an explicit value is kept;
- a found update stays visible until handled: a dot on Settings, the menu item
  "Update available · <version>", and a notice that shows even with action
  notifications off (also for the "up to date"/retry result right after
  enabling).

**Processing load (IDEA-057).** Ported from the maintainer's local tree:

- engine: `vinemix-engine/src/processing_load.rs` (monitor, policy and
  lock-free publication, with every `processing_load_tests.cpp` case) and the
  mix-loop measurement; `status.processingLoad` in snapshots, unavailable
  before the first quantum, after stop, and fresh after a restart
  (`tests/engine_lifecycle.rs`);
- desktop: `processing_load.rs` (presenter, validation, 2 s staleness), mixer
  advisory after adding Distortion/RNNoise under limited headroom, the footer
  "Audio load" readout with a 248 px reserve, the dismissible notice strip,
  the details window and Add-menu advice. Matched against the local tree's
  `processing-load-*` renders.

**Device changes no longer disturb running streams (deliberate change from
0.5, 9 November 2026).** Adding or removing any audio device makes the
Windows audio service stall every stream briefly while it rebuilds. 0.5
turned that small glitch into audible stutter in three ways, and the Rust
engine now avoids all three:

- a capture discontinuity emptied the queue and reset the resampler, so the
  mixer re-primed (tens of ms of silence); the queue and resampler history are
  now kept, with only a 10 ms fade-in at the splice (device and app capture);
- the third glitch within 10 s closed and reopened the stream with slower
  "compatible" timing for the rest of its life (0.5's log shows three devices
  doing this in the same second). Glitches within 5 s after a device
  notification, or 2 s before it, are now forgiven, and glitches within 250 ms
  of each other count as one incident (`StreamStability`);
- each such underrun permanently added 5 ms of buffering; it no longer does
  during that settling window.

The device catalog also stopped activating an `IAudioClient` on every active
endpoint for each notification: mix formats are cached per endpoint and
re-read only for new endpoints, endpoints that just became active, a changed
`PKEY_AudioEngine_DeviceFormat`, or an explicit retry
(`cargo run --release -p vinemix-wasapi --example catalog_timing` confirms an
identical inventory). `examples/device_change_probe.rs` measures reopens,
underruns and capture gaps on the physical endpoints while a device is added
or removed.

### 0.3 Delivered and planned: how to read the rest of this plan (10 November 2026)

Sections 1 to 14 below are the plan as drafted on 6 November 2026. They are
kept as written, as the record of the design intent. Several of their targets
were not built, or were built differently. The table states what the code does
today; where it disagrees with a later section, the table is current and the
section is a proposal. Build and verification steps for what exists are in
[RUST_BUILD.md](RUST_BUILD.md). Findings and their evidence are in
[the 10 November 2026 review](RUST_REVIEW_2026-11-10.md).

| Topic (section) | The plan says | Delivered today |
|---|---|---|
| Renderer (1, 6.2, 8) | egui with eframe and wgpu | egui and eframe 0.37 with the glow (OpenGL) backend and AccessKit. No wgpu. |
| UI tests (6.3) | `egui_kittest` driving the UI through AccessKit, with wgpu snapshot images | An in-crate headless harness (`crates/vinemix-ui/src/ui/test_support.rs`, `app_tests.rs`) that runs the real `App` with synthetic input and reads the AccessKit tree. No image snapshots. |
| Engine and desktop protocol (1, 5.4, O3) | Protocol version 2: negotiation, pushed telemetry, id-based edit patches | Protocol version 1, request and reply. The desktop polls about 30 times a second while visible and once a second otherwise. Two optional request fields, added 10 November 2026, let the engine leave an unchanged configuration and device list out of a reply and skip display-only work ([CONTROL_PROTOCOL.md](CONTROL_PROTOCOL.md)). Nothing is pushed and edits send the whole configuration. `vinemix-ipc` contains no code. |
| Parameter edits (5.1, O1) | A `ParamBlock` triple-buffer mailbox; no `Runtime` per edit | No mailbox. Since 10 November 2026 an edit that changes only parameters reuses the running `GraphProcessor` and retargets it in place: no DSP state is allocated or transferred. A new `Runtime` object is still built and published for every accepted edit. A change to nodes, cables or streams still builds and adopts a new processor. |
| Engine persistence (5.5, O2) | A write-behind persistence thread | Not built. The engine still commits each accepted configuration durably before it replies. The desktop's own preferences file is written by a background, coalescing writer (10 November 2026). |
| Telemetry lock (O3) | Read telemetry from atomics without the configuration mutex | The snapshot copies what it needs under the lock, then runs spectrum analysis and builds the JSON after releasing it. |
| Block kernels (O4), approximations (O5) | Block-wise kernels; bounded-error math | Not built. Processing is per sample. Only the exact part of O5 exists: the expander skips `pow` when its result is exactly 1. |
| Spectrum (O6) | `rustfft`; only for EQ nodes on display | The ported radix-2 FFT. It runs only for EQ plots the desktop reports as drawn (10 November 2026). |
| Application discovery (O7) | Only while needed; cache image paths | Runs only while a visible desktop window has an application-capture node (10 November 2026). No image-path cache. |
| Mix-format reads (O8) | Cache per endpoint | Done (0.2 above). The O8 row's "Today" column describes the 0.5 engine, not this one. |
| Hidden window (6.2) | "A hidden window costs nothing" | A hidden desktop repaints at most once a second as a backstop and its engine link waits on a condition variable. It is cheap, not free. |
| Third-party DSP (1, Phase 6) | Link the pinned C sources first; pure Rust later, as an optional gate | The shipped executables contain Rust translations only, verified bit-exact against the pinned C sources. C and C++ are compiled only into the test-only `vinemix-oracle`. |
| Parity with 0.5 (5, Phase 2) | Every node bit-exact against the 0.5 engine | Every node except Voice Color. That node was redesigned in Rust on 11 November 2026 ([VOICE_COLOR.md](VOICE_COLOR.md)) and is held by its own measurements in `crates/vinemix-dsp/tests/voice_color.rs`. In the parity graphs it sits bypassed, so its validation, its bypass path and every node after it are still compared. The 0.5 C++ sources are unchanged. |
| Realtime guards (5.1) | A `loom` model test; no-alloc zones in debug and CI builds | An allocation-counting integration test of the processor (`crates/vinemix-dsp/tests/realtime_alloc.rs`) and stress tests of the queue and spectrum mailbox. No `loom` models, and the running mixer and device loops are not instrumented as no-alloc zones. |
| Continuous integration (9) | — | `.github/workflows/verify.yml` has a `rust` job that runs `scripts/verify-rust.ps1`. It had not yet run on GitHub when this table was written. |

A target that appears above as not built is a proposal, not a missing feature
of the candidate: each needs its own measurement and decision before work
starts.

---

## 1. Recommendation in one page

1. **Rewrite the engine first, behind the existing wire protocol.** Build a Rust
   `VineMix.Engine.exe` that speaks control protocol v1 byte-for-byte and reads
   and writes configuration v2 unchanged. The current WPF desktop then drives
   the Rust engine without modification, and the existing desktop verification
   suite plus `scripts/verify-offline.ps1` become the first acceptance gate. This
   is the single most important de-risking move: the audio core can ship on its
   own before any UI work starts.
2. **Keep two processes** (engine and UI). Audio continues while the window is
   closed, the supervisor can restart a stalled engine, and a UI or GPU-driver
   crash can never take audio down. What changes is the protocol between them
   (§5.4): push telemetry and edit patches replace 30 Hz full-JSON polling.
3. **One shared model crate.** Today the configuration schema, defaults, ranges,
   legacy Echo migration and validation are implemented twice (C++ and C#) and
   kept in sync by hand. In Rust, `vinemix-model` is the single source of truth
   used by engine, UI, updater and tests.
4. **UI: egui (eframe + wgpu) with a purpose-built node canvas**, AccessKit for
   Windows UI Automation and `egui_kittest` for the replacement of the 92
   `*Verification.cs` harnesses. Slint is the credible alternative (§6.2); this is
   the main decision the maintainer needs to make.
5. **Port third-party C DSP in two steps.** First link the *same pinned*
   libsamplerate 0.2.2, SpeexDSP 1.2.1 and RNNoise v0.2 sources through `cc`
   for bit-level parity; later replace each with pure Rust (rubato, a Rust port
   of the Speex preprocessor with `Clone` state, a Rust port of RNNoise v0.2
   inference) only after objective and listening comparisons pass.
6. **Optimize where it measurably matters** (§7). The largest wins are not
   micro-DSP: they are (a) knob drags rebuilding the whole runtime — an Echo
   node allocates and touches ~6.1 MB per edit batch, (b) a synchronous
   `FlushFileBuffers` + `ReplaceFileW` for every 100 ms edit batch, and (c) 30 Hz
   full snapshots that serialize the entire configuration, endpoint inventory
   and application list while holding the configuration mutex.

Rough effort for one experienced Rust/audio engineer: **5–8 months** to full
replacement, with the Rust engine shippable after roughly **8–11 weeks**
(§11). These are planning estimates, not commitments.

---

## 2. What exists today (measured from this checkout)

| Area | Language | Size | Notes |
|---|---|---|---|
| Engine sources | C++20 | ~4,800 lines (`native/src`, `native/include`) | WASAPI, graph compiler, DSP, effects, config store, named-pipe server |
| Engine tests/probes | C++ | ~10,300 lines, 25 programs | offline (fake COM, synthetic audio) and opt-in hardware probes |
| Vendor shims | C | 4 files | SpeexDSP alloc hooks + state copy bridge, RNNoise alloc hooks |
| Desktop app | C# / WPF (.NET 10) | ~20,500 production lines | custom node canvas, dials, meters, EQ editor, voice pad, help, settings, updates, tray |
| Desktop verification | C# | ~20,400 lines, 92 files | detached UI verification and offscreen design rendering |
| Update core + tests | C# | ~900 lines | GitHub release source, RSA-PSS-SHA256 manifest trust, downloader |
| Setup helper | C# | ~530 lines | Inno Setup companion |
| Build/release | PowerShell, CMake, Inno Setup | ~30 scripts | project-local pinned toolchains (CMake, llvm-mingw, .NET, Inno) |

Third-party native code: libsamplerate 0.2.2 (`SRC_SINC_FASTEST`), SpeexDSP 1.2.1
preprocessor, RNNoise v0.2 + model `0b50c45`, nlohmann/json 3.12.0.

### 2.1 Engine architecture as implemented

```
 WASAPI capture threads (per unique endpoint, MMCSS "Pro Audio")
   decode -> fade-in -> libsamplerate (drift-corrected ratio) -> FrameQueue (SPSC)
                                                                     |
 Mixer thread (MMCSS, high-res waitable timer, 240 frames = 5 ms @ 48 kHz)
   MixClock deadlines -> CaptureBufferReader -> GraphProcessor.process(plan)
   -> per-endpoint sum (+10 ms crossfade old/new runtime) -> FrameQueue (SPSC)
                                                                     |
 WASAPI render threads (per resolved endpoint; VAC Lite = exclusive, others shared)
   FrameQueue -> libsamplerate (drift-corrected) -> encode -> device buffer

 Control thread: named pipe JSON server (snapshot / configure / retry / recovery)
 Maintenance thread (1 Hz): device + application inventory, output reconciliation, logging
 Device catalog thread: IMMNotificationClient -> generation counter -> re-enumerate
```

Runtime publication: a fully prepared `Runtime` (plan + processor + ports +
meters + spectra) is published through an atomic pointer; the mixer adopts it at
a block boundary, transfers state by node/edge id, crossfades 480 frames when
topology or ports change, then acknowledges its generation; the control thread
destroys retired runtimes. No allocation, lock or I/O on the mixer thread.

The legacy fixed-console `Mixer` class in `dsp.cpp` is no longer used by the
engine or any test target and will not be ported.

---

## 3. Goals, non-goals and invariants

### Goals
- Entire shipped program in Rust: engine, UI, tray, updater worker, setup helper,
  build tooling (`xtask`). Inno Setup may remain as the installer script host.
- Equal or better audio behaviour, verified objectively against the C++ oracle.
- Lower CPU, memory, latency of edits, startup time and package size.
- Fewer duplicated rules, stronger compile-time guarantees for realtime safety.

### Non-goals (for the parity phases)
- New audio features, new node kinds or changed sound. Optimizations that change
  output (fast math, FTZ, different resampler) are separate, opt-in phases.
- Cross-platform support. Pure crates (`model`, `dsp`, `rt`) will build and test
  on any OS, but the product remains Windows 11 x64.

### Invariants that must survive the rewrite
These are contracts users or released builds already depend on.

1. **Configuration v2 file format**, including the retained fixed-console fields,
   the `.bak`, `.new`, `.established`, `.v1.bak`, `.corrupt-*` and
   `.recovery-*` conventions, the 256 KiB limit, v1 → v2 in-memory migration and
   the legacy Echo `feedback` → `repeats` rule. Golden files from today's engine
   must round-trip byte-identically (same 2-space JSON layout and number
   formatting — see §9.3 on float formatting).
2. **Load states** `new / loaded / backupAvailable / recoveryRequired /
   unsupported` and their write/activation rules; future-schema protection.
3. **Device policy**: explicit assignments only, no default-device substitution,
   exact id then unique active stable id, never names; transport families
   (`vbCable`, `vacLite`, `listening`, `physical`, `otherVirtual`); VAC Lite
   exclusive with no shared fallback; IAudioClient3 shortest period → default →
   legacy; three-glitches-in-ten-seconds compatible-timing latch.
4. **Realtime rules**: no allocation, lock, wait, I/O or unbounded work on the
   mixer or device threads after stream start; bounded queues; stale-audio flush.
5. **Control protocol v1** (for the transition period) and the named pipe
   `\\.\pipe\VineMix.Control.<user SID>` with the protected DACL (user + SYSTEM)
   and `PIPE_REJECT_REMOTE_CLIENTS`.
6. **Single-instance and compatibility locks**: `Local\AudioMixer.Engine.<SID>`
   and `Local\VineMix.Engine.<SID>`, plus the desktop role mutexes.
7. **Workspace identity checks**: physical settings path from an open handle,
   Windows-shell launch normalization, AudioMixer → VineMix migration.
8. **Update trust**: RSA-PSS-SHA256 envelopes, key id `vinemix-updates-2026-01`,
   manifest file naming, asset URL scheme and size limits. The **installed C#
   0.4/0.5 updater must be able to install the first Rust release** (§10.3).
9. **Privacy rules from `AGENTS.md`**: no personal names, paths, hardware or
   workspace data in fixtures, docs, history or packages.

---

## 4. Target repository layout

```
Cargo.toml                    workspace, shared lints, release profiles
rust-toolchain.toml           pinned stable toolchain + targets
crates/
  vinemix-model/      serde types, defaults, validation, v1 migration, Echo rule  (no Windows deps)
  vinemix-rt/         FrameQueue, DriftController, StreamBufferPolicy, CaptureBufferReader,
                      MixClock, StreamStability, triple-buffer mailbox, retire queue (no Windows deps)
  vinemix-dsp/        biquads, dynamics, effects, spectrum, graph compiler, GraphProcessor (no Windows deps)
  vinemix-resample/   AdaptiveResampler trait; libsamplerate (FFI, parity) + rubato (pure) backends
  vinemix-denoise/    Speex preprocess + RNNoise; FFI (parity) then pure-Rust backends
  vinemix-wasapi/     DeviceCatalog, AudioWorker, ProcessCaptureWorker, sample formats, COM seams
  vinemix-engine/     Engine, Runtime publication, mixer + maintenance loops, ConfigurationStore, log
  vinemix-ipc/        protocol v1 (JSON) + v2 (framed, push), secured pipe server/client, identity checks
  vinemix-platform/   SID, LOCALAPPDATA, physical paths, mutexes, start-at-login, migration, shell relaunch
  vinemix-update/     release source, manifest trust, downloader, transaction, worker logic
  vinemix-ui/         egui application: canvas, nodes, widgets, panels, help, settings, history
apps/
  vinemix/            UI + tray executable (VineMix.exe)
  vinemix-engine/     headless engine executable (VineMix.Engine.exe)
  vinemix-update-worker/
  vinemix-setup-helper/
xtask/                build, package, verify, release-metadata (replaces most PowerShell scripts)
oracle/               C++ golden-render harness built from the legacy sources (§9.1)
fixtures/             sanitized settings files, graphs, signals, expected renders
```

The three platform-free crates (`model`, `rt`, `dsp`) are the heart of the
product and can be tested, fuzzed and benchmarked on any CI runner.

---

## 5. Engine design in Rust

### 5.1 Realtime ownership model
- Port the proven generation scheme rather than inventing a new one: control
  thread owns `Vec<Box<Runtime>>`; mixer reads `AtomicPtr<Runtime>`; mixer
  publishes `active_generation` only after its final use of a retired runtime,
  including the 480-frame routing crossfade. Wrap the raw pointer in a small,
  heavily documented `unsafe` module with a `loom` model test.
- Shared ports (`Arc<Port>`) are never dropped on the mixer thread: the mixer
  holds `&Port` borrowed from runtimes it does not own. This is the same
  guarantee the C++ gets from `shared_ptr` held by `runtimes_`.
- **Parameter fast path (new, §7 O1).** Edits that do not change topology,
  ports or effect allocation produce a compact `ParamBlock` (gains, ramps
  targets, coefficients, effect settings) written to a triple-buffer mailbox.
  The mixer swaps it in at a block boundary. No `Runtime`, no `GraphProcessor`,
  no effect state is allocated.
- Debug and CI builds install `assert_no_alloc` (or an equivalent counting
  global allocator) and mark the mixer and device loops as no-alloc zones,
  replacing today's Speex/RNNoise allocation-hook instrumentation.
- Engine release profile: `panic = "abort"` with a panic hook that appends to
  `engine.log`. The desktop supervisor already restarts a stalled or exited
  engine within its launch budget.

### 5.2 DSP port
- `GraphKind`, `GraphPort` become Rust enums parsed once at compile time from
  the model's strings (today the C++ already does this; strings never reach
  the audio thread).
- `GraphPlan` keeps fixed arrays (`[Node; 32]`, `[Edge; 96]`, topological
  `order`), so the plan stays `Copy`-free but allocation-free to process.
- **Parity-first implementation**: identical operation order, `f64` where the
  C++ uses `double`, `f32` storage where it uses `Frame`. Rust's `f64::powf`,
  `exp`, `tanh`, `log10` call the platform libm; small ULP differences are
  expected and handled by the tolerance policy in §9.2.
- Effects become an `enum Effect { Denoise(Box<Denoise>), Reverb(Box<Reverb>), … }`
  dispatched once per block per node instead of a virtual call per sample.
- Echo history (768,002 frames ≈ 6.1 MB) and Alignment Delay (96,001 frames ≈
  768 KB) are allocated and pre-touched on the control thread exactly as today;
  `adopt` moves the `Box` rather than copying.

### 5.3 Windows audio I/O
- Use the `windows` crate (0.62.x observed) directly, not the higher-level
  `wasapi` crate (0.26.0, Oct 2026). VineMix needs things the wrapper does not
  clearly expose: `IAudioClient3::GetSharedModeEnginePeriod` /
  `InitializeSharedAudioStream`, the exclusive VAC format probe order with
  aligned-buffer retry, `IAudioClient2` category, fresh-client retries after a
  poisoned initialization, and `ActivateAudioInterfaceAsync` process loopback
  with an `IAgileObject` completion handler. Keep the C++ seams that let tests
  inject fake `IMMDeviceEnumerator` / `IMMDevice` / `IAudioClient` objects by
  writing the policy functions against small traits implemented for both the
  COM types and test fakes.
- Pure policy functions (`resolve_endpoint`, `stream_endpoint_rejection`,
  `transport_family`, `virtual_name`, `SampleFormat` decode/encode, buffer
  policy) live outside the COM code so they are unit-testable everywhere.

### 5.4 Control protocol
- **v1 (compatibility)**: newline-delimited JSON, 256 KiB limit, one request
  outstanding, identical field names, error codes and snapshot contents. Served
  until the Rust UI ships and for one release afterwards so a mixed install or
  rollback still works.
- **v2 (negotiated by `hello`)**: length-prefixed frames on the same secured
  pipe; `postcard` or `bincode` encoding of `vinemix-model` types.
  - `Subscribe{telemetry_hz, include_spectrum_for, include_apps}` — the engine
    pushes compact telemetry frames (peaks, clips, dynamics, spectrum for
    visible EQ nodes only, path state deltas) instead of the UI pulling the
    whole snapshot 30 times a second.
  - `State` is sent only when the committed configuration, endpoint inventory
    or application list actually changes (each has its own generation).
  - `Edit{expected_revision, ops: [Patch]}` — id-based operations (set node
    param, move nodes, add/remove node/edge, replace graph) instead of the full
    configuration. The engine applies them to its committed copy, validates,
    and classifies the result as *param-only* (fast path), *layout-only*
    (persist, no audio work) or *structural* (new runtime).
  - Revision conflicts keep the current rebase semantics.

### 5.5 Configuration store
- Same file names, atomic `ReplaceFileW` with backup, `.established` marker,
  `.v1.bak` preservation, damaged-file preservation and recovery folders.
- **Write-behind persistence (§7 O2, needs maintainer approval).** Configure
  acknowledges after validation and publication; a persistence thread writes the
  newest revision after 300 ms of quiet or 2 s at most, always on shutdown, quit,
  update handoff and explicit save. Failures still surface as
  `settings_write_failed` with retry. Today's contract ("validate, save,
  publish, acknowledge") is therefore relaxed to "durable within ≤ 2 s"; the
  UI's quit/update paths already request a strict flush and keep that.

---

## 6. Desktop application in Rust

### 6.1 Scope to port (~20,500 production lines)
Shell, custom caption and window chrome; tray (keep audio running when closed);
engine supervisor and identity verification; graph canvas (pan/zoom 10–200 %,
progressive detail, minimap, grid, selection box, cut stroke, wire insertion and
detach, socket snapping, stacking, alignment tools, named frames/groups, layout
lock); 32 node kinds' panels; rotary dials with exact-value drafts; stereo
meters with peak hold; EQ editor with live spectrum; Voice Color pad; device and
application pickers with availability states; undo/redo history with layout
classification; clipboard; search (nodes, settings); hover/menu help and help
lessons; What's new; About/Credits/licences (offline); settings (start at login,
minimap, device visibility, notifications, reduced animation, right-drag pan);
update check/download/install UI; recovery banners; workspace migration.

Disappears entirely in Rust: `EmbeddedRuntime` and the standalone runtime ZIP
extraction (a Rust executable is self-contained), and most of the .NET-specific
launch plumbing.

### 6.2 UI toolkit decision

| | **egui / eframe (recommended)** | Slint | iced | Tauri + web |
|---|---|---|---|---|
| Node canvas | Excellent: immediate-mode painter, trivial hit testing, `egui-snarl` exists for reference | Possible with `Path` + absolute layout; more work for wires, cut strokes, minimap | Possible via `canvas` widget | Excellent (DOM/SVG) but UI not in Rust |
| Accessibility (UIA) | AccessKit integrated; custom widgets must supply `WidgetInfo` | AccessKit in winit backend | Upstream still WIP | WebView2 accessibility is mature |
| Testing | `egui_kittest` (0.34.x): AccessKit queries + wgpu snapshot images | Rust tests + screenshot tooling | limited | Playwright |
| Styling fidelity | Fully custom painting; theme tokens map well from `ConsoleTheme` | Strong declarative styling | Moderate | Strongest |
| Licence | MIT/Apache | GPLv3 or royalty-free licence (attribution terms to confirm) | MIT | MIT/Apache |
| Fit with "entire program in Rust" | Yes | Yes (`.slint` markup + Rust) | Yes | No (TS/HTML front end) |

Why egui: the product *is* the canvas. The graph is capped at 32 nodes and 96
cables, so immediate-mode cost is negligible, while custom interactions
(insertion-on-wire, cut gestures, socket snapping, progressive detail) are far
simpler to express. Repaint is event-driven plus `request_repaint_after(33 ms)`
only while visible meters move; a hidden window costs nothing. The main cost is
that every custom widget (dial, meter, EQ, pad, sockets, wires) must publish
AccessKit roles, names, values and actions explicitly — budgeted in Phase 4.

### 6.3 Verification
The 92 `*Verification.cs` harnesses are behavioural specs. Each becomes one or
more `egui_kittest` tests driving the real UI through AccessKit (find by role
and label, click, type, assert) with snapshot images where pixels matter
(branding, dial redesign, socket presentation). `OffscreenDesignRenderer`
becomes a `--render-design` mode using the same wgpu snapshot path.

---

## 7. Optimization catalogue

Each item lists the current behaviour (from the code), the change, and whether
audio output changes. Wins are expectations to be confirmed by the benchmarks in
§9.5, not measured results.

| # | Today | Change | Output changes? |
|---|---|---|---|
| O1 | Every accepted edit with a different `GraphPlan` builds a new `Runtime`: fresh `GraphProcessor`, fresh effects (Echo: 6.1 MB allocate + touch; Alignment Delay 768 KB; Speex/RNNoise states), state transfer and a 480-frame crossfade when ports change. Knob drags dispatch every 100 ms. | Param-only edits go through the `ParamBlock` mailbox; ramps already smooth targets. Runtime rebuild only for topology/port/effect-allocation changes. | No (ramps identical) |
| O2 | `configure` serializes the full JSON, `FlushFileBuffers`, then `ReplaceFileW` while holding the configuration mutex — up to 10×/s during a drag. | Write-behind persistence thread (§5.5). | No |
| O3 | UI polls `snapshot` ~30 Hz; each reply carries the whole configuration, endpoints, applications, all node telemetry, and the snapshot holds the configuration mutex (contending with `configure`). | Protocol v2 push telemetry + generation-gated state; telemetry read from atomics without the configuration mutex. | No |
| O4 | Per sample, per node: long `if/else` chain on node kind, per-edge `Ramp::next()`, per-sample `effects_[i]` ramps for unrelated controls (pan, width, fade, makeup, mono, limitMix are advanced for every node kind). | Block-wise kernels per node kind; settled ramps collapse to constant gains; edge summing as vectorizable multiply-add over 240 frames; only ramps relevant to the node kind advance (others stay settled — must prove equivalence because inherited ramp state transfers between plans). | Parity-preserving if done carefully; verified by oracle |
| O5 | `pow` per sample in Compressor (above threshold), Dynamic Ducking, Expander (every sample), Voice Color (`log10` + `pow` every sample); `tanh` 8× per sample per channel in Distortion. | Phase 7 only: log-domain gain computers with bounded-error `exp2/log2` approximations, skip the expander `pow` when the envelope is above threshold (result is exactly 1), SIMD `tanh` approximation for oversampled distortion. | Yes (tiny, bounded) — opt-in after parity |
| O6 | Spectrum FFT: hand-written radix-2 complex FFT of 4096 points, L+iR packed, on the snapshot thread. | `rustfft` with a cached plan (same packing trick). Runs only for EQ nodes the UI is displaying (v2 subscription). | No (display only) |
| O7 | Maintenance loop runs every second: `EnumWindows`, Toolhelp snapshot, `OpenProcess` + `QueryFullProcessImageNameW` per candidate, audio-session enumeration on every render endpoint. | Refresh applications only while the UI has the app picker or an unresolved app node; cache image paths by (pid, creation time). | No |
| O8 | Device catalog activates an `IAudioClient` on every active endpoint at every refresh to read its mix format. | Cache per endpoint id; refresh on notification for that id or on property change. | No |
| O9 | Desktop needs the .NET 10 runtime; Standalone embeds and extracts a runtime ZIP on first launch. | Single static Rust executable for each app; no extraction step. Targets to measure: package size, cold start, idle RAM, idle CPU. | n/a |
| O10 | Mixer runs on its own 5 ms timer; every device path has its own SPSC queue + drift-corrected resampler. | Research (Phase 7): optionally clock the graph from one "master" device callback when all paths share a clock domain, removing one queue hop. | Latency only |
| O11 | Denormals flushed manually in many places. | Phase 7: set FTZ/DAZ on audio threads; keep manual flushes until parity is retired. | Bounded |

---

## 8. Crate selection (observed November 2026 — re-verify when pinning)

| Need | Choice | Notes |
|---|---|---|
| Win32/COM | `windows` 0.62.x | Same family the `wasapi` crate builds on |
| Serialization | `serde`, `serde_json` | Custom formatter if needed for byte-identical settings output |
| SPSC queues | Port `FrameQueue` (needs `requestClear`/`clearPending` semantics) — `rtrb` 0.3 as reference | The clear-request protocol is VineMix-specific |
| Allocation guard | `assert_no_alloc` (or in-house counting allocator) | Debug/CI only |
| Resampling | libsamplerate via `cc` (parity) → `rubato` 5.x `Async` sinc (pure) | rubato supports on-the-fly ratio changes and allocation-free `process_into_buffer` |
| Denoise | SpeexDSP 1.2.1 + RNNoise v0.2 via `cc` (parity) → pure-Rust ports | `nnnoiseless` 0.5.x ports the *older* RNNoise model, not v0.2; `speexdsp` 0.1.2 has a partial pure-Rust port. Plan own ports with `Clone` state (replaces `am_speex_copy`) |
| FFT | `rustfft` / `realfft` | Spectrum only |
| UI | `eframe`/`egui` (0.34.x line observed via `egui_kittest` 0.34.3), `egui-wgpu`, AccessKit | |
| Tray / menus | `tray-icon` + `muda` | Must live on the Win32 event-loop thread |
| HTTP | `ureq` with native TLS (SChannel) or `reqwest` | Release metadata and assets from GitHub |
| Signatures | `rsa` (PSS verify) + `sha2` | Verification only; no private keys in the app |
| Concurrency tests | `loom` | Runtime publication, FrameQueue, mailboxes |
| Property / fuzz | `proptest`, `cargo-fuzz` | Graph compiler, settings parser, protocol decoder |
| Benchmarks | `criterion` | Per node kind, full 32-node graphs |

Toolchain: **`x86_64-pc-windows-msvc`** (tier 1) is recommended; it needs the
Visual Studio Build Tools. The `x86_64-pc-windows-gnullvm` target could reuse
the project's pinned llvm-mingw and avoid Visual Studio, but recent reports show
linker-packaging friction for native builds; treat it as a fallback.

---

## 9. Verification strategy

### 9.1 Golden oracle
Build a small C++ program (`oracle/`) from the existing `graph.cpp`,
`advanced_effects.cpp`, `dsp.cpp`, `configuration.cpp`, spectrum and timing
headers. It reads a fixture (graph JSON + signal spec), renders N blocks through
`GraphProcessor` (including plan swaps via `adopt`/`inherit`), and writes raw
`f32` output, meters and dynamics telemetry. Rust tests render the same fixture
and compare. The corpus covers: every node kind × parameter sweeps; bypass/mute
transitions; plan swaps mid-tail (Echo, Reverb, Denoiser); sidechains; control
edges; Split/Combine/Crossfade ports; NaN/Inf/denormal input; maximum graphs.

### 9.2 Tolerance policy
- Graph routing, gains, ramps, biquads, ports: **bit-exact** expected.
- Paths through libm transcendentals: max absolute error ≤ 1e-6 (−120 dBFS)
  and identical meter clip flags.
- Pure-Rust replacements of C libraries (Phase 6): objective metrics (SNR vs
  reference, PESQ/ViSQOL-style speech scores for denoisers, spectral distance)
  plus maintainer listening sign-off.

### 9.3 Settings and protocol compatibility
- Round-trip every historical fixture (v1 and v2) and compare bytes with the
  C++ engine's output. nlohmann/json prints doubles with shortest round-trip
  digits; Rust's `ryu` also prints shortest round-trip but can differ in
  exponent formatting (`1e-05` vs `1e-5`). A custom `serde_json` formatter
  will be added if the corpus shows any divergence.
- Replay recorded v1 request/response transcripts against the Rust engine.

### 9.4 Port of existing native tests
All 25 C++ test programs map to Rust integration tests in the owning crate
(e.g. `drift_queue_tests.cpp` → `vinemix-rt/tests/drift_queue.rs`,
`exclusive_render_tests.cpp` → `vinemix-wasapi/tests/exclusive_render.rs` with
fake COM). Hardware probes stay opt-in binaries behind a `hardware` feature.

### 9.5 Performance
`criterion` benches per node kind and for worst-case 32-node graphs, plus an
end-to-end soak harness recording mixer block-time histograms, CPU, RSS and
allocation counts. Every optimization in §7 lands with a before/after table.

---

## 10. Migration phases

### Phase 0 — Foundations (1–2 weeks)
Toolchain pin, workspace, lints (`unsafe_op_in_unsafe_fn`, `clippy::pedantic`
subset), CI (GitHub Actions `windows-latest` + Linux for pure crates), oracle
harness, sanitized fixture corpus, `xtask` skeleton.

### Phase 1 — Platform-free core (3–4 weeks)
`vinemix-model` (types, defaults, validation messages verbatim, v1 migration,
Echo rule), `vinemix-rt`, `vinemix-dsp` with all effects using FFI denoisers and
libsamplerate. Gate: oracle parity on the full corpus; property tests on the
graph compiler (cycle detection, port rules, duplicate detection).

### Phase 2 — Rust engine, drop-in (3–5 weeks)
`vinemix-wasapi`, `vinemix-engine`, `vinemix-ipc` v1, `vinemix-platform`,
`apps/vinemix-engine`. Gate: unchanged WPF desktop passes its verification
suite against the Rust engine; offline scripts pass; manual hardware
qualification (mic → headphones, VB-CABLE input, VAC Lite exclusive output,
app capture, hot-plug, sleep/resume, 8-hour soak). **First shippable milestone.**

### Phase 3 — Engine optimizations (2–3 weeks)
O1 param fast path, O2 write-behind (if approved), O3 protocol v2, O6–O8.
v1 stays served for the WPF UI.

### Phase 4 — Rust UI (10–14 weeks)
Vertical slices, each with its kittest suite: shell/tray/supervisor/connection →
canvas camera, grid, minimap → nodes, sockets, wires, selection, drag, lock →
dials, meters, pickers → EQ editor + spectrum, Voice Color pad → history,
clipboard, frames, search, keyboard navigation → settings, help, What's new,
About/Credits → recovery and migration flows. Accessibility pass with Narrator
and NVDA on every slice.

### Phase 5 — Updater, setup helper, packaging (2–3 weeks)
Port UpdateCore, update worker and transaction logic, setup helper; Inno Setup
script points at Rust binaries; standalone = single EXE.

**10.3 Cross-generation update path (critical).** Existing installs run the C#
updater. The first Rust release must keep the exact asset names, manifest
format v1, signing key and Setup command-line contract so the old updater can
install it, and the Rust Setup must migrate `SetupSupport/package-manifest.json`
ownership correctly. Rehearse 0.4.0 → Rust and 0.5.x → Rust updates on clean VMs
before publication.

### Phase 6 — Pure-Rust third-party DSP (3–5 weeks, optional gate)
rubato backend, Speex preprocessor port, RNNoise v0.2 inference port, each
behind a feature flag with A/B comparison. Then delete `native/`, `desktop/`,
CMake and .NET from the build.

### Phase 7 — Beyond parity (open-ended)
O5 fast math, O10 master-clock research, O11 FTZ/DAZ, SIMD kernels, further
latency work.

---

## 11. Effort summary

| Phase | Estimate |
|---|---|
| 0 Foundations | 1–2 weeks |
| 1 Core model/rt/dsp | 3–4 weeks |
| 2 Engine drop-in | 3–5 weeks |
| 3 Engine optimizations | 2–3 weeks |
| 4 UI | 10–14 weeks |
| 5 Updater/packaging | 2–3 weeks |
| 6 Pure-Rust DSP deps | 3–5 weeks |
| **Total** | **~24–36 weeks** |

---

## 12. Risk register

| Risk | Impact | Mitigation |
|---|---|---|
| Subtle realtime regressions (glitches only on some hardware) | High | Port policies verbatim; fake-COM tests; soak harness; staged hardware qualification before shipping Phase 2 |
| Settings byte-compatibility (float formatting) | Medium | Corpus round-trip in CI; custom formatter |
| Old updater cannot install Rust release | High | §10.3 rehearsal on VMs; keep asset/manifest contract |
| UI accessibility regressions vs WPF automation peers | Medium | AccessKit for every custom widget; Narrator/NVDA checklist per slice |
| egui visual fidelity vs current design | Medium | Theme tokens from `ConsoleTheme`; snapshot tests from the existing design-QA renders |
| Pure-Rust denoiser sounds different | Medium | FFI parity path ships first; pure ports gated on metrics + listening |
| Scope creep (new features during port) | High | Parity phases accept bug-for-bug compatibility; new ideas go to `docs/IDEAS.md` |

## 13. Decisions needed from the maintainer

1. UI toolkit: egui (recommended) or Slint.
2. Approve write-behind persistence (O2) and its ≤ 2 s durability window.
3. Toolchain: MSVC Build Tools (recommended) or llvm-mingw `gnullvm`.
4. Whether pure-Rust replacements of libsamplerate/SpeexDSP/RNNoise are required
   for "entirely Rust", or whether pinned C sources compiled by `cc` are acceptable.
5. Release naming: ship the Rust engine as 0.6.0 under the existing WPF UI, or
   hold everything for a single 1.0 switch.

## 14. File mapping (C++ → Rust)

| C++ | Rust |
|---|---|
| `types.hpp`, `application_types.hpp`, `configuration.hpp/.cpp` (types, JSON, validation, migration) | `vinemix-model` |
| `configuration.cpp` (store, physical path, SID, pipe name) | `vinemix-engine::store`, `vinemix-platform` |
| `dsp.hpp/.cpp` (FrameQueue, DriftController) | `vinemix-rt` |
| `dsp.hpp/.cpp` (AdaptiveResampler) | `vinemix-resample` |
| `audio_timing.hpp`, `mix_clock.hpp`, `stream_stability.hpp` | `vinemix-rt` |
| `spectrum.hpp` | `vinemix-dsp::spectrum` (+ mailbox in `vinemix-rt`) |
| `graph.hpp/.cpp` | `vinemix-dsp::graph` |
| `advanced_effects.hpp/.cpp` | `vinemix-dsp::effects` + `vinemix-denoise` |
| `windows_audio*.hpp/.cpp` | `vinemix-wasapi` |
| `process_capture.hpp/.cpp` | `vinemix-wasapi::process` |
| `engine.hpp/.cpp`, `engine_instance.hpp` | `vinemix-engine` |
| `control_server.hpp/.cpp` | `vinemix-ipc` |
| `main.cpp` | `apps/vinemix-engine` |
