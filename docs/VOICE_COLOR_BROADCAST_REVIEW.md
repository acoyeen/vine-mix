# Voice Color broadcast character and pad — review

18 October 2026 · Windows WPF desktop and C++ engine

Candidate: `artifacts/AudioMixer-VoiceBroadcast`.
Native evidence: `artifacts/voice-broadcast-native`.
Detached desktop evidence: `artifacts/voice-broadcast-review`.
Desktop verification passed **4,042 checks, 117 renders and zero binding errors**.
All **12 native offline suites passed**, including **137 focused Voice Color
assertions**. The verified desktop and engine are installed and running. Earlier Voice Color, dial and primary-control
results describe earlier builds.

## Problem and resulting behavior

The previous pad traded warmth against clarity along one axis and added room
along the other. Increasing those amounts did not provide the combination of
full body, presence and steady delivery requested for an announcer-like voice.
The new vertical Character axis moves from Natural to Broadcast and coordinates
body/presence shaping, compression, subtle delayed doubling and a small room.
The horizontal Tone axis still moves from Warm to Bright. Neutral remains at
bottom centre with transparent processing.

The pad is the node's primary control. Its directional labels sit outside the
usable area, and its full handle stays inside the pad at both endpoints and
corners. Tone and Character use the shared smaller numeric dials for precise
entry. Broadcast sits above the square; Warm, Natural and Bright sit below it.
A separate fixed readout shows Neutral or the current direction and amount.
A small origin diamond appears only when the handle is away. The dense grid
is replaced by a centre guide, small edge ticks and subtle upper shading.
Labels and the readout do not change values when clicked or double-clicked.
The revised pad keeps the reset marker and handle clear of the labels.

This is an original voice treatment, not an implementation of Voicemeeter's
algorithm. The exact stages and bounds are documented in [Voice Color](VOICE_COLOR.md).

## Interaction and compatibility

Dragging and clicking edit the pad without moving the node. Shift-dragging gives
fine relative movement. Focused arrow keys adjust either axis; Shift reduces
the step. Home and double-click reset both axes. Each numeric dial can reset
its own axis. Wheel input keeps the workspace's normal zoom behavior. Bypass
retains the coordinates, and the ordinary node reset retains identity and cables.

The version-2 configuration continues to store `voiceColor.tone` in [−1, 1]
and `voiceColor.ambience` in [0, 1]. Character is a UI label for the existing
`ambience` coordinate. No migration rewrites saved coordinates. Existing
nonneutral coordinates intentionally sound different on this engine; neutral
and bypass retain their transparent intent. Other nodes and their saved settings
are outside this change.

## Verification record

All 12 native suites passed, including 137 focused assertions covering neutral
and bypass behavior, body/presence direction, dynamic control, delayed doubling
and tail bounds, transitions and persistence. At centred Tone and full Character,
the measured below-threshold response increased 80 Hz by 5.13 dB and 2.5 kHz by
5.30 dB, versus 1.77 dB at 650 Hz. Synthetic vocal-burst loud/quiet contrast fell
from 16.52 dB to 5.31 dB, and crest factor fell 2.26 dB against an RMS-matched
EQ-only reference. The representative output peak was 0.189. These are fixture
measurements, not guarantees for arbitrary input or a claim of peak limiting.

Thirty nodes took 1.021 ms median and 1.705 ms at the 99th percentile per 5 ms
audio quantum on this test run. Process, reconfigure, inheritance and reset had
no tracked C++ heap allocations/deallocations. Detached WPF verification passed
all 4,042 checks with 117 renders
and zero binding errors. These cover axis labels, full-handle containment at the
corners, neutral-marker separation, numeric synchronization, keyboard and reset
behavior, zoom and DPI rendering. Visual inspection covered neutral, a middle
position, full Warm/Broadcast and high contrast. These checks use synthetic audio
and detached views without operating the user's live graph or computer input.

Synthetic results and visual renders cannot establish the preferred sound for
a particular voice or microphone. Listening to the same spoken phrase at
neutral and at modest Character remains a separate subjective check. The direct
path and effect timing should be described by implementation evidence, not
confused with measured end-to-end hardware latency.

## Activation record

Activated build `voice-broadcast-20261018` at 22:14 AEST on 18 October 2026.
The controller opened responsively and both installed binary hashes matched
the verified candidate. All 15 existing paths returned Running and the engine
frame counter advanced. Configuration revision 4237 and the saved settings bytes
were unchanged, including after the updated window opened. No graph edits were
made during activation.

The first restored Running snapshot arrived after 0.44 seconds; this is software
status timing, not an audible-gap or end-to-end latency measurement. The prior
package and settings are backed up with the activation evidence in
`artifacts/voice-broadcast-update-20261018-221408/result.json`. No rollback was
needed. The new controller and engine process IDs were 57824 and 46480.
