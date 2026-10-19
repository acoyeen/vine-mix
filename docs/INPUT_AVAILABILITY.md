# Input availability

An unavailable microphone, virtual input or application gently flashes its
existing source name red. A faint red glow sits outside the node, and a small
steady red warning triangle sits directly above the vertical volume meter in
the existing Audio row space. It clears with the glow when capture recovers.
It does not cover the meter's clipping caps or bars. The normal
header, controls, white selection border, size and socket positions stay intact.
There is no extra warning banner, coloured status band or recovery line.

The source text smoothly alternates between red and its normal colour over
1.6 seconds. It stays fully opaque and readable. The selector arrow, its face,
dropdown choices and other node controls do not flash. Reduced animation, or
Windows disabling client-area animation, keeps the source text steadily
highlighted. The glow uses faint vector strokes extending ten screen DIPs
outside the normal border, without rasterizing the node or its text.

Hover the source name for the specific reason and recovery guidance. These
include a missing, disabled or unplugged device, blocked access, a busy input,
an unavailable app, absent VB-CABLE, and an offline audio engine. Accessible
status and help expose the same reason. High contrast uses system colours.
Unassigned inputs and connection attempts retain their ordinary source controls;
healthy muted inputs are identified by the existing mute button.

The classifier uses the assigned identity and capture state. Running capture
wins over a delayed device inventory. Device resolution follows the engine's
exact-ID and unique active stable-ID rules. Friendly names are not identities.
An app absent from the picker is not enough to establish failure: its capture
path must report Waiting. Silence never creates an availability warning.
A missing or disabled source still warns when its mute button is on.

The warning clears when capture recovers. Display changes do not modify device
assignments, audio routing, levels, settings, node positions or Undo history.
Device/app selection stays available while the engine is connected. Virtual
sources keep Troubleshooting > Reassign device in their context menu.

Verification uses pure state fixtures and detached renders of the real WPF
controls. No desktop observation, input automation or real device changes are
used. Hardware unplug/replug and audible recovery remain manual checks.
The audio engine is unchanged from the accepted duplicate-input batch.

The warning-icon candidate passed **4,778 detached checks**, produced **136 renders**
and reported **zero binding errors** on 19 October 2026. See the
[final results](../artifacts/input-warning-icon-20261019/ui-final/results.json),
[red text phase](../artifacts/input-warning-icon-20261019/ui-final/input-device-pulse-red.png)
and [normal text phase](../artifacts/input-warning-icon-20261019/ui-final/input-device-pulse-normal.png).
