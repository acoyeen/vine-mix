# Maintenance review — 18 October 2026

Scope: audio processing, device recovery, controller responsiveness, settings
integrity and obsolete code. Existing node types, saved graphs, routing, dials,
tooltips and compatibility with older settings remain supported.

## Changes

- Moving or renaming nodes, changing the canvas view as part of a real save, or
  changing EQ reset anchors no longer constructs and swaps an equivalent audio
  runtime. Settings are still validated, committed and saved. Existing effect
  histories and analyzer continuity survive these presentation-only changes.
- EQ/filter plans execute only active, non-identity filter sections. Flat bells,
  flat shelves, disabled bands and unused slots add no biquad work. Full EQs and
  audible filter responses retain their processing.
- Engine startup cleans up already-started workers if a later worker fails.
  Restarting the same engine object works; timer-creation failure releases its
  audio scheduling registration.
- A failed device enumeration retains the last complete inventory and retries.
  Optional metadata failures preserve previously known format/transport data for
  the exact same endpoint and direction. Successfully observed removals still
  take effect. Unassigned workers wait for control events; capture shutdown and
  reconfiguration can interrupt packet draining promptly.
- Settings recover from the last-good backup when the primary file is missing,
  as well as when it is corrupt. Recovery warnings clear after a successful load.
- The EQ spectrum accepts fresh frame counters after an engine restart. Add-menu
  search no longer offers retired Gain/Reroute types that cannot be created;
  existing saved nodes remain supported.
- Stable device/application inventories avoid rebuilding dropdown items on every
  telemetry update. Actual identity, name, format or availability changes still
  refresh them. EQ text layouts and static tooltip trees are reused; dynamics
  feedback updates existing text. Dials avoid duplicate redraws and cancel an
  active gesture when disabled or unloaded.
- Configuration copies directly clone every mutable branch instead of converting
  entire graphs to JSON and back. Tests check serialized equivalence and object
  independence across all current node types and legacy fields.
- Control responses reuse a bounded buffer and deserialize away from the UI
  continuation. Cancellation/disposal closes pending operations without flushing
  into an unresponsive engine. Races involving detached transport objects and
  invalid null configurations are covered. Supervisor process/identity handles
  are disposed correctly.
- Removed the retired fixed-console view, setup window, old view-model refresh
  pipeline and unused fader/toggle/meter controls and styles. Legacy configuration
  fields and migration code remain. Historical test evidence remains available.
- Builds can target a staged package and refuse to overwrite a running app.
  Packaging copies current guides, without accumulating historical render images
  and fixtures. A single offline runner now collects the silent checks.

## Measurements

Synthetic native benchmark: 240-frame blocks at 48 kHz, 100 warm-up blocks and
3,000 measured blocks per scenario. These deliberately large graphs expose the
cost of repeated filter work; ordinary graphs will have different totals.

| Graph | Median before | Median after | Change |
|---|---:|---:|---:|
| 30 empty EQs | 166.1 µs | 39.2 µs | 76% less processing time |
| 30 flat EQs | 165.7 µs | 39.2 µs | 76% less processing time |
| 30 high-pass filters | 166.7 µs | 59.5 µs | 64% less processing time |
| 30 fully populated eight-band EQs | 166.3 µs | 166.6 µs | Essentially unchanged |

Checksums matched before and after. These are single before/after observations,
subject to system load. See [native benchmark evidence](../build/maintenance-native-summary.json).

Copying the same 32-node, 31-edge graph 500 times in one process allocated
77,000,040 bytes with the previous JSON implementation versus 16,100,040 bytes
with direct copies: **79% fewer allocated bytes**. Recorded elapsed time was
310.8 ms versus 11.8 ms; timing is diagnostic rather than a pass/fail threshold.
See [copy benchmark evidence](design/maintenance-final-20261018-025804/model-copy-performance.json).

No processing quantum, hardware buffer size, queue target or effect delay was
reduced. These changes improve available processing headroom and UI work;
they do not establish a lower microphone-to-headphones latency.

## Verification

The staged Release package compiled successfully. Desktop compilation reported
no warnings/errors; native configuration retains upstream libsamplerate CMake
deprecation notices. No third-party source was modified to hide them.

[All ten offline native suites passed](../artifacts/maintenance-native-final/results.json):

| Suite | Evidence |
|---|---|
| Graph routing/DSP | 271,509 assertions |
| Engine lifecycle | 232 commits, including presentation-only saves and failed starts |
| Configuration/qualification | 7,911 assertions; 4,527,360 synthetic frames |
| Clock drift/queues | 87,598 assertions, bounded queues and simulated timing |
| Advanced effects | 49,999 assertions |
| Spectrum | 410 checks |
| De-esser quality | 62 checks |
| Other effect quality | 532 checks |
| Graph performance | Four maximum-size filter scenarios with matching checksums |
| Windows catalog | 27 checks using fake endpoint interfaces |

[Detached desktop verification](design/maintenance-final-20261018-025804/results.json)
passed **1,982 checks**, produced **79 renders**, and reported **zero binding
errors**. This includes 34 private-pipe transport checks, deep-copy integrity,
100 unchanged telemetry updates without selector rebuilds, repeated spectrum
renders without repeated glyph layout, spectrum recovery and existing graph
editing, dials, tooltip, zoom, DPI and geometry checks. Representative workspace,
EQ spectrum, tooltip and 75%/144-DPI effect renders were visually reviewed.

Independent code reviews checked copy ownership, inventory invalidation, pending
edit replay, removed bindings and lifecycle/recovery changes. Tests use synthetic
audio, temporary settings, fake endpoints and random private control pipes.
Detached rendering never shows a native window. No test signal, recording,
desktop input, default-device change or driver change was used.

The final transport pass also verifies the exact 256 KiB wire boundary including
the terminating newline. Oversized requests fail locally without partial writes.
The activation procedure retains previous binaries and attempts restoration and
restart even if an individual recovery step fails; it never rewrites settings.

## Remaining qualification

Actual listening quality, complete physical-path latency, device unplug/replug,
sleep/resume and the deferred eight-hour real-time soak still require the
hardware session described in [TESTING.md](../TESTING.md). Accelerated simulations
and a responsive window do not replace those checks. No claim is made that every
possible defect has been eliminated.

## Activation

[Activation passed](../artifacts/maintenance-update-20261018-025837/result.json)
at 02:58 Brisbane time. Controller 26784 is responsive; engine 43552 advances
its frame counter. All eleven previously running paths reported Running within
0.307 seconds of the engine restart. This is a software recovery observation,
not a measurement of the audible gap. Revision 3807, full configuration, and
the exact settings-file bytes remained unchanged, including after UI startup.

Installed binaries match the verified staged package:

- Desktop SHA-256: `F5B1AF6CA6C8BCC0A7F0BFC173B1C4C94AA5B65F0580EF0115C4C5A588FC1D17`
- Engine SHA-256: `D1285C8C09C1A02AB2C5FF567B831DADEB7162BC46B22DE021FEDC0C778BE68F`

Previous binaries and a settings snapshot are retained in the activation evidence
directory. The update did not rewrite settings, reinstall drivers or change
Windows defaults or device volumes. The eight-hour soak remains deferred.
