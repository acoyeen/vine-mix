# Input and output availability

Evidence note: raw reports, historical renders and reference captures cited
here are retained privately outside the repository. Their labels below identify
archived evidence, not downloadable files; recorded results apply only to the
versions and checks described.

An unavailable microphone, virtual input, application, listening output or virtual
output gently flashes its existing device/app name red. A faint red glow sits
outside the node, and a small steady red warning triangle sits directly above the vertical volume meter in
the existing Audio row space for inputs. Outputs reserve space inside the meter
column above the bars, keeping controls, node dimensions and sockets fixed as
warnings appear and clear. It clears with the glow when capture or playback recovers.
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
include a missing, disabled or unplugged device, blocked access, a busy input or
output, an unavailable app, absent VB-CABLE or VAC Lite, and an offline audio
engine. Accessible status and help expose the same reason. High contrast uses system colours.
Unassigned physical devices and connection attempts retain their ordinary controls;
an unassigned virtual node warns when its required transport is absent or
unavailable. Healthy muted inputs and outputs use the existing mute button.

The classifiers use the assigned identity and capture/playback state. A running
stream wins over a delayed device inventory. Device resolution follows the engine's
exact-ID and unique active stable-ID rules. Friendly names are not identities.
An app absent from the picker is not enough to establish failure: its capture
path must report Waiting. Silence never creates an availability warning.
A missing or disabled input or output still warns when its mute button is on.

The warning clears when capture or playback recovers. Display changes do not
modify device assignments, audio routing, levels, settings, node positions or Undo history.
Device/app selection stays available while the engine is connected. Virtual
nodes keep Troubleshooting > Reassign device in their context menu.

Verification uses pure state fixtures and detached renders of the real WPF
controls. No desktop observation, input automation or real device changes are
used. Hardware unplug/replug and audible recovery remain manual checks.
The output-warning update changes only desktop presentation; audio processing
and device recovery are unchanged.

The output-warning candidate passed **9,507 detached checks**, produced **174
renders**, and reported **zero binding errors** on 20 October 2026. Physical and
virtual output cases cover missing devices, muted failure, recovery despite stale
inventory, setup/connection states, reduced animation, high contrast and offline
help. The real control renders were inspected beside the input warnings.
Evidence: `artifacts/output-availability-20261020/ui-final-3/results.json`.
The verified interface was activated after a Saved/idle handoff, retaining the
same engine process, advancing audio frames, saved configuration and personal
files. Activation evidence: `artifacts/output-availability-20261020/activation.json`.
Actual device unplug/replug and listening checks remain manual.

The warning-icon candidate passed **4,778 detached checks**, produced **136 renders**
and reported **zero binding errors** on 19 October 2026. See the
final results,
red text phase
and normal text phase.
