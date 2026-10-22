# Voice Color verification — 18 October 2026

> Historical development record. Results and procedures below belong to their
> named versions; they do not qualify the current beta. See
> [current qualification](BETA_TESTING_CURRENT.md) for the active release checks.

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

The separate Release candidate is in `artifacts/AudioMixer-VoiceColor`.
It contains the native engine, desktop application, runtime libraries and guides.
During development and offline verification, the running personal package was
left untouched. Following the user's request, both verified executables were
installed together and Audio Mixer was restarted on 18 October at 20:05 AEST.
Do not start a competing candidate controller against the running engine.

## Personal installation

Activation passed without rollback. The desktop and engine hashes match the
verified candidate. All 12 existing graph paths reported Running, the audio frame
counter advanced, and the desktop opened responsively. Configuration revision
3835 retained its 12 nodes, routes and settings; the settings file remained
byte-for-byte unchanged, including after the desktop opened. Voice Color is
available through **Add → Effects → Voice Color**; no node was inserted into the
user's existing audio routes automatically.

The recovery snapshot arrived after 0.292 seconds. This is a software status
measurement, not a measured audible interruption or end-to-end latency.
The new desktop/engine process IDs were 2864/46468. Prior binaries and settings
were backed up alongside the
activation evidence.

## Audio and maintenance

All **11 offline native suites passed**, including the existing graph/DSP,
configuration, engine lifecycle, queue/drift, advanced effects, spectrum,
de-esser quality, effect quality, graph performance and fake Windows inventory
checks, plus the new Voice Color suite.

The focused suite passed **107 assertions**. The production engine lifecycle
test, linked to synthetic workers, completed **264 commits** and verified Voice
Color updates and persistence without restarting workers or stalling its clock.
No test opened a real endpoint or connected to the personal control pipe.

Measured settled responses include the node's headroom compensation:

| Position | 60 Hz | 1.8 kHz | 12 kHz |
|---|---:|---:|---:|
| Warm, −100% Tone | +1.459 dB | −3.069 dB | −4.524 dB |
| Neutral | 0 dB | 0 dB | 0 dB |
| Bright, +100% Tone | −4.459 dB | +0.069 dB | +1.524 dB |

- Fresh neutral processing was bit-exact, undelayed and channel-independent.
- Every tested Dry position matched the existing EQ with the documented mapping;
  there was no room contribution.
- Room mix was 0.75%, 3% and 12% at 25%, 50% and 100% Ambience. Measured first
  wet energy appeared at 33.3125 ms; this does not delay the direct signal.
- Reversals every 5 ms, faster than the controller cadence, remained bounded.
  Largest adjacent-sample change in the DC movement fixture was 0.000243112.
  Tiny transitions through exact neutral passed DC, sine and broadband checks.
- A replacement processor inherited identical filter, room and in-flight ramp
  state. Bypass settled to the original signal; reset returned to neutral within
  1e-7 after settling. Recovery reset cleared history.
- Tracked configure, process, inheritance and recovery operations performed no
  C++ heap allocations or deallocations.
- Both axes and bypass round-tripped through configuration; malformed/nonfinite
  settings were rejected. Loading old graphs preserved their previous fields,
  layout and compiled processing plan.

Diagnostic processing times per 240-frame / 5-ms block on this run:

| Synthetic graph | Median | 99th percentile |
|---|---:|---:|
| One Voice Color node | 18.4 μs | 27.9 μs |
| Thirty Voice Color nodes | 543.3 μs | 922.9 μs |

These measure offline processing, without device I/O, and vary with system load.
They are not end-to-end latency or a hardware reliability qualification.

Native evidence:
`results.json`,
`Voice Color measurements`,
`engine lifecycle`.

## Desktop and visual inspection

Detached WPF verification passed **2,277 checks**, produced **88 renders**, and
reported **zero binding errors**. It showed no native windows and used synthetic
configuration, inventory and private test pipes only.

Checks cover pad boundaries, nonfinite rejection, rapid updates, keyboard/fine
adjustment, neutral reset, exact numeric editing, bypass and context-menu reset,
copy ownership, clipboard validation, save/load defaults and edit history.
They also check no-op telemetry, wheel zoom, gesture ownership, unchanged node
dimensions/sockets and complete controls at all 12 supported zoom stops, 30–200%.

Inspected neutral, warm/ambient, bypassed, 75%-zoom/144-DPI, 144-DPI close-up and
high-contrast renders. Labels, neutral marker, boundary handles and exact-value
fields remain visible without overlap or clipping. The 306-DIP square pad keeps
its world dimensions; zoom scales the complete node.

Desktop evidence:
`results.json`,
`neutral node`,
`75% workspace at 144 DPI`,
`high contrast`.

## Listening still required

In a later session, use an isolated low-volume microphone route. Compare neutral
with bypass, sweep Warm/Bright over speech, then raise Ambience gradually. Check
voice naturalness, consonants, bass, tails, and audible transitions during fast
movement and bypass. Confirm mouse and keyboard comfort on the actual display.
The effect deliberately allows modest EQ boosts; keep headroom in the full mix.

No microphone content was captured for these tests. Device latency, hot-plug,
sleep/resume and the deferred endurance run remain separate hardware checks.
See [the original mapping and limitations](VOICE_COLOR.md).
