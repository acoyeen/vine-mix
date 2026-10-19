# Node workspace — implementation and verification

Date: 17 October 2026, Australia/Brisbane. Application version: **0.2.0**.

This replaces the fixed-console interface with an editable audio graph. The
library contains 13 implemented node types: microphone, virtual input, running
app, Add/Mix, Gain/Multiply, five-band EQ, compressor, balance, mono, reroute,
linked control, listening output and virtual output. The graph is bounded to
32 nodes and 96 connections. See [the workspace guide](NODE_WORKSPACE.md).

## Evidence and its limits

- **Native graph/DSP: passed 3,347 assertions.** This covers all 64 legacy routing
  combinations, independent levels/mutes, shared controls, EQ responses,
  compressor behavior, stereo operations, graph validation, transitions,
  migration, persistence and bounded fanout. Synthetic samples only.
- **Native lifecycle: passed 210 concurrent graph commits and snapshots.** The
  actual engine runs against stub workers for topology fades, rapid rebinding,
  independent output loss/rejoin, retiring plans and shutdown. No production
  capture/render worker is linked into this test executable.
- **Read-only application discovery:** applications were enumerated on this PC.
  Unassigned application-worker construction/retry/shutdown was exercised without
  opening a process audio stream.
- **Packaged desktop: passed 31 inert checks, with zero WPF binding errors.**
  Checks exercise node creation, connections, feedback rejection, effects and
  layout persistence, independent sends, deletion, migration, ambiguous app
  selection, collapse activation, reduced meter animation and missing-device
  presentation. See [the retained result](design/node-verification/ui-results.json).
  [Workspace](design/node-verification/workspace.png),
  [effect controls](design/node-verification/effects.png) and
  [simulated high contrast](design/node-verification/effects-high-contrast.png)
  were rendered from the real WPF controls and visually inspected.

- **Isolated control protocol: passed 12 checks against the packaged engine.**
  The test verifies its temporary settings path, unassigned devices, advancing
  frame counter, revision checks, atomic graph edits, rejection without mutation
  and control-client reconnection. See [the retained result](design/node-verification/engine-results.json).

These tests use inert or unassigned configurations. Neither edits the personal
settings file. They do not simulate every physical mouse gesture, exercise a
real screen reader, or establish behavior under actual Windows DPI transitions.

## Corrections made during verification

Review and verification found and corrected an inline WPF template startup
error; ambiguous and recycled process selection; collapse-button keyboard
activation; reduced-animation behavior; control-cable OFF semantics;
stale retired-worker reuse; and unbounded deleted-node log state.
New destinations start muted and unassigned.
An invalid graph leaves the last committed graph operating.

## Preserved personal setup

The baseline file is `%LOCALAPPDATA%\AudioMixer\settings.json`, version 1,
revision 3. Reading it migrates the graph in memory and does not rewrite it.
The listening mix retains its mute and −24 dB level; microphone monitoring to
that mix remains off. The original file's SHA-256 is:

```text
D082E5A80406C58E7ACD25EFD8B56FE57234C89F6780314F095177429EB1E0F5
```

The first user edit saves version 2 and retains a separate `.v1.bak` copy of the
original settings. App nodes and processing nodes are available to add; no new
routes or sources are inserted into the user's existing graph automatically.

## Normal engine startup

The packaged engine opened the personal configuration successfully. Its snapshot
reported version 2 in memory, revision 3, eight nodes and nine connections. The
audio frame counter advanced from 2,640 to 76,320 during the observation. The
personal settings file remained byte-for-byte identical to the hash above.

| Saved path | Observed startup state |
|---|---|
| Shure microphone | Running, 48 kHz mono |
| Second microphone | Unassigned |
| VB-CABLE app input | Running, 48 kHz stereo |
| PS VR2 listening output | Waiting for device; selection retained |
| Second listening output | Unassigned |
| VAC Lite virtual microphone output | Running, 48 kHz stereo |

Mix A remained muted at −24 dB. No generated sound, recording or hardware fault
test was started. This is startup evidence, not confirmation that the audio was
heard correctly. See [the startup observation](design/node-verification/startup-results.json).

The final desktop was then launched normally and attached to that engine. A
read-only observation of its real window reported **Audio engine connected**,
**Saved · revision 3**, the waiting listening device, and an advancing frame
count. The window was responsive, and the settings hash remained unchanged.
See [the desktop startup result](design/node-verification/desktop-results.json).
The application is left running for the user.

## Remaining qualification

The implementation checks do **not** establish audible quality, real Spotify
capture, simultaneous game/Discord consumers, latency, Bluetooth behavior,
physical-device recovery time or long-running stability. An analytical EQ curve
is not a live spectrum measurement. The compressor has no lookahead and is not a
true-peak limiter. Application capture copies audio; original playback continues.

Follow [TESTING.md](../TESTING.md) and [APP_AUDIO.md](APP_AUDIO.md) in the later
quiet audio session, after the planned Windows restart. Record unavailable
hardware and unperformed scenarios explicitly instead of treating them as passes.
