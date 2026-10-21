# Vine Mix user guide

For the supplied **VineMix 0.3.0-beta.1** files, start with the
[beta tester guide](BETA_TESTER_GUIDE.md). The reference below belongs to the
earlier committed source and uses its original names. See
[source status](../README.md#source-status) for the integration status.

A Windows 11 audio mixer built around draggable nodes and visible audio cables.
Create sources, processing chains and destinations on a resizable node canvas, with
settings inside each node. The C++ engine handles audio in a separate process;
the WPF desktop handles editing, telemetry and tray behavior.

## Node library

- **Inputs:** physical microphone, base VB-CABLE virtual input, running application.
- **Mixing:** Mix, stereo balance and mono; a linked control adjusts several levels together.
- **Processing:** interactive EQ with up to eight bands, High-pass, Low-pass,
  Low / Mid / High tone controls, Voice Color, Compressor, Limiter, Ducking,
  Expander, Gate, Alignment Delay, denoiser, de-esser, plosive remover and reverb.
- **Outputs:** physical listening output and VAC Lite virtual microphone output.

The creation menu contains **24 node types**. Mix acts as a volume control with
one incoming connection and combines sound when several connections arrive.
Gain / Multiply and Reroute are no longer offered for creation; existing saved
nodes still work, bringing the compatible engine library to 26 types.

The graph supports up to **32 nodes and 96 connections**. Sources can feed several
destinations, and each listening output has independent buffering and recovery.
New outputs start muted; physical outputs require explicit selection. Existing A/B routes migrate with their
saved levels, mutes, devices and disabled sends.

Several input nodes can use the same microphone, virtual input or running app.
Right-click an input and choose **Duplicate input** to create a selected copy
nearby, keeping its source and controls but starting without connections. Rename
and connect each copy wherever it fits your graph. Level, mute and Mono remain
independent per node, and duplication supports Undo/Redo. Copy/paste also retains
input assignments. If you route several copies into one destination, their audio
adds together.

Unavailable inputs and outputs gently flash their existing device/app name red and have a
faint red outer glow, with a red warning triangle above the volume meter.
Hover the device/app name for the reason and recovery guidance.
Reduced animation keeps the text steadily highlighted. A connected but muted
input or output keeps its ordinary appearance; silence alone never marks a device missing.
The warning clears when capture or playback reconnects. Device and app selectors remain
available while the engine is connected.

Hover a node's volume meter to see its highest observed left/right sample peaks
and any detected overloads. The readings are purely informative; clicking does
not clear them or change audio. Red caps stay lit for two seconds after the last
detected overload, then fade back to gray over one second. Hover readings retain
their history. Changing the source, muting it or losing its audio connection
clears its history.

Use **Settings → Restart audio engine** to restart audio processing and reconnect
your existing routes. Audio briefly stops during the restart. The workspace
stays open, and queued edits are preserved if they cannot be saved beforehand.

Virtual nodes show their device name and connection status without a dropdown.
The virtual input automatically binds the **CABLE Output** recording endpoint;
the virtual output binds the **Line 1** playback endpoint. Apps send sound to
**CABLE Input**, and Discord/games receive the mix through the **Line 1** recording
device. Automatic assignment requires a single eligible, active endpoint. Multiple
virtual inputs can share it; output devices remain assigned to one output node.
Saved selections stay assigned while absent.
For duplicate devices or a driver reinstall, right-click the virtual node and use
**Troubleshooting → Reassign device**.

Use **Add**, **Shift+A**, or **F3** to search. Drag a header or unused node space to move it; connect
matching sockets by dragging or clicking them. Middle-drag pans, the wheel zooms
over every node control (open dropdowns and menus keep their own scrolling)
through fixed stops, and **Home** fits the graph. Select one or more nodes and
press **F**, or right-click a selected node and choose **Frame selection**, to
focus that part of the graph. Click the zoom percentage or
press **Ctrl+0** to return to 100%. Select a cable or node and press **Delete** to remove
it. Nodes stay expanded. Audio sockets are round; shared-control
sockets are square; detector-only sidechain sockets are diamonds. Every scalar
numeric parameter uses the shared dial control. Related effect controls form
compact groups, with persistent labels, values and units. Drag up/down, hold
Shift for fine adjustment, or click the number and press Enter to apply an exact
value. Tab or Shift+Tab applies a valid value before moving to the next or previous
control; invalid text stays editable. Each dial and its value box has its own
Tab stop. Escape cancels. Double-click or Home resets the focused dial; EQ frequency
resets to the selected band's reset anchor. Arrow keys adjust the focused dial,
and Page Up/Down make larger changes. Focus remains visible. Decimal precision
is retained, and sample counts accept whole numbers only. The wheel zooms the
canvas without changing parameters. Essential dial instructions remain visible
inside nodes. Selectors, action buttons, toggles and passive readouts have
separate shapes and positions; the EQ graph and Voice Color pad remain interactive.
Tooltips describe each control's purpose or current state. Mouse and keyboard
instructions are in **Settings → Help & shortcuts**. Open **Settings** at the top
right for workspace preferences, start-at-login, Windows sound settings and
device recovery.

Physical device dropdowns show available devices by default. Under
**Settings → Audio device visibility**, optionally show disabled, disconnected
or advanced endpoints. Current saved selections stay visible while unavailable;
the filters never clear assignments or change audio routes. See the
[device visibility guide](DEVICE_VISIBILITY.md) for categories and recovery.

**Settings → Help & shortcuts** (or **F1** outside text entry) opens six topics:
**Controls**, **Dials & values**, **Buttons & menus**, **Search & results**,
**Equalizer**, and **Meters & status**. Controls shows one group at a time.
Shaded section headers have expand arrows; indented rows show each action and
its keys. Select an action row for its explanation.
The remaining topics use real controls with temporary sample values. Examples
do not play or record audio or change your live mix or its Undo history.

Choose **Reset example** to restore a lesson's starting values. Changing topics,
or closing and reopening Help, starts a fresh example; practice values are not
saved. Help remembers which topic you were viewing for the current app session.

The wheel scrolls Help, including over example dials and closed selectors;
open dropdowns and menus keep their own scrolling. **Escape** first
cancels an unfinished practice edit, gesture or open choice, then closes Help
when there is nothing to cancel. **Close** returns directly to the workspace.
Your live workspace is unavailable for editing while Help is open, and audio
continues running. At narrower sizes, the topic list becomes a dropdown and
the content scrolls, including at the supported minimum window size of 900 × 580.

Use **Search settings and help** at the top of Settings to find options and
instructions. For example, **Zoom** shows mouse-wheel zoom and zoom-reset help.
Information appears directly in the menu. Use Up/Down to choose an instruction,
then Enter or double-click to open its entry in Controls. Tab moves to matching
settings; Shift+Tab returns to search. Searching does not run commands or change
settings.

**Settings → Find node…** (or **Ctrl+F** outside text entry) searches the current
workspace by node name, type, assigned device or app. Choose a result to select
and frame it. Escape closes search without changing the view or selection.
To tidy a selection of two or more nodes, right-click and choose
**Align selected nodes → Left edges** or **Top edges**. Each alignment is one
Undo step and keeps the nodes' connections and sound controls. Nodes form a
column or row with a fixed 48-DIP gap between their actual bodies, closing larger
gaps and opening smaller ones. Cable direction
determines connected-node order, so shuffled chains align from source to destination.
With the canvas focused, **Alt+X** makes a horizontal row and **Alt+Y** makes a
vertical column. **Alt+A** runs **Auto Align**, choosing the direction the
selection already extends furthest; ties use a row. The same actions are in
the alignment submenu.

For VR controllers or mice without an accessible middle button, enable
**Settings → Right-click drag to pan**. Drag with the right button
on empty canvas to pan; release without dragging to open the context menu.
A small movement threshold ignores pointer jitter, and holding still keeps the
gesture a click. Middle-drag continues to pan and **Ctrl+right-drag** continues
to cut cables. The option is off by default and is saved as a workspace UI
preference; it does not change audio settings.

The Add menu groups nodes into **Inputs, Outputs, Effects, Mixing and Controls**,
using the same category colours as their headers. Search also accepts a category
name. The upper toolbar holds creation, selection, Undo, Redo, Activity, Fit and
zoom controls. Save and connection status stay in the header. The footer shows
action results and errors immediately; Dismiss returns to the shared dial guide.
Drag the title, passive header text or unused toolbar space to move the window;
buttons keep their normal actions and the canvas keeps its own gestures.
**Undo / Redo** restore up to 100 graph edits in the current app session. One
pointer drag is one step, including dial, EQ and Voice Color adjustments. Use
**Ctrl+Z**, **Ctrl+Y** or **Ctrl+Shift+Z**, or the toolbar buttons. Text fields keep
their own text undo. A new graph edit after Undo clears Redo. Canvas navigation
and app preferences are not part of graph history. **Activity** lists applied
edits and the latest workspace message. History starts fresh when the app opens.

Panning, zooming, selection and hover do not save audio settings. Actual node,
cable and parameter edits save automatically; unchanged values are ignored.
The current view is included in the next real edit, so navigation by itself is
session-only. Nodes reserve status space and keep their world dimensions when
devices reconnect, selection changes or parameter values change.
Layout-only saves preserve the active audio processing state; moving or naming
nodes no longer rebuilds their effect histories.

New EQ nodes start flat with no bands. Double-click an empty part of the plot
to add a bell band. The hint and band information are centred between the
decibel scales.
Ctrl-click points to toggle selection, drag empty plot space to box-select
(hold Ctrl to add), or press Ctrl+A with the plot focused to select all bands.
Drag a selected point or use the arrow keys to move the selection together;
hold Shift for fine movement. Delete removes selected bands, Home resets their
positions, and Escape cancels a selection box. The Frequency, Band gain and
**Width / Q** dials below the plot edit the active band only. Right-click a
point for **Shape** (Bell, Low shelf, High shelf, Notch), enable,
**Reset position** and removal. Double-click a band to return its frequency to
its reset anchor and its gain to zero; its shape and Q remain intact.
Right-click empty plot space and choose **Reset EQ to flat** to clear all bands.
The node's **Reset node to defaults** action also returns it to an empty EQ.
The EQ node is 620 DIPs wide with a 300-DIP-high editor, a smooth calculated
response line and a separate measured post-EQ audio spectrum underneath.
Bell Q remains 1 by default; lower Q broadens a bell. Width presets are removed.
Zero bands also gives a flat response; exact values stay directly below the graph.
Dedicated **High-pass** and **Low-pass** nodes expose cutoff
and resonance. Existing band settings are preserved. The **Low / Mid / High** node
offers three simpler dials, each from −12 to +12 dB: a 200 Hz low shelf, a 1 kHz
bell and a 5 kHz high shelf. Continuous control edits are coalesced on a 100 ms
target cadence, so new pointer movement does not postpone changes indefinitely.

Effects, Balance and Mono have no generic Level row. Their effect-specific
controls remain: for example, EQ band gains, compressor makeup and reverb mix.
Use a Mix node for a separate branch volume. Older saved nonzero effect levels
remain effective and are shown through the node's **Saved level** context action,
where they can be reset explicitly. Right-click any node for **Reset node to
defaults**: this resets sound controls while keeping its name, position, cables
and device/app assignment. Resetting an output restores its safe muted default.

The denoiser reduces steady noise, the de-esser controls loud high-frequency
consonants, the plosive remover controls low-frequency bursts, and reverb adds a
stereo room tail. See [the effect research and limitations](EFFECTS_RESEARCH.md)
and [the measured EQ analyzer](EQ_SPECTRUM_ANALYZER.md). The Denoiser node's
**Algorithm** dropdown offers **SpeexDSP** (Reduction, 0–40 dB) and **RNNoise**
(Mix, 0–100%). New denoiser nodes start with RNNoise at 100% Mix. Saved and
pasted nodes keep their chosen algorithm; older nodes without an algorithm
setting keep SpeexDSP. Reset keeps the chosen algorithm. Both run locally; RNNoise's model
is embedded. Denoising adds **20 ms with SpeexDSP / 30 ms with RNNoise**,
including while bypassed so toggling stays aligned. Parallel paths are not
automatically delay-compensated; mixing untreated and denoised copies can colour
the sound. New nodes are not connected automatically.

**Voice Color** combines tonal balance with broadcast character in one pad.
Move left for Warm, right for Bright, and upward from Natural toward Broadcast
to add body, presence, a low-mid cut, compression, density and subtle delayed
doubling with a small room. Larger labels sit inside the pad around a square
grid, clear of the handle. Bottom centre is transparent, with a discreet reset
marker and no Neutral text. Double-click resets both axes;
keyboard and exact Tone/Character dials provide precise adjustment. Existing
coordinates are retained and intentionally use the new sound treatment. This
original mapping does not reproduce Intellipan's exact sound or alter pitch.
See [Voice Color controls and mapping](VOICE_COLOR.md).

**Expander** and **Gate** are separate Add/search entries for reducing quiet
audio between phrases, with no mode dropdown. Expander has five dials:
Threshold, Ratio, Range, Attack and Release. Gate has six: Threshold, Hysteresis,
Range, Attack, Hold and Release. An older saved Expander in gate mode still
shows Gate controls and retains its saved settings without conversion.

**Compressor** reduces its own louder audio using Threshold, Ratio, Attack,
Release and Make-up gain. It has no detector selector or sidechain input.
Older compressors may keep their Compress/Limit selector; their saved detector
field no longer changes processing. **Limiter** exposes Ceiling and Release
and enforces a sample-peak ceiling with no lookahead. Bypass removes limiting,
and later gain can exceed this node's ceiling.

**Ducking** lowers main audio in response to a separate trigger: Music → Ducking
→ Output, with Microphone → Trigger. Choose **Static ducking** for a set Duck
amount after the trigger crosses Threshold, or **Dynamic ducking** for reduction
that grows with the trigger's level according to Ratio. Both show Threshold,
Attack and Release, with either Duck amount or Ratio. Static is the default,
with Threshold −30 dBFS and Duck amount 18 dB; Dynamic's Ratio starts at 4:1.
Switching modes keeps both values, the current gain envelope, node size and
connections. Trigger audio never joins the audible mix, and the main audio
never substitutes for a missing trigger. Missing or silent triggers release
toward unity. Only Ducking accepts a trigger cable.

**Alignment Delay** offers linked millisecond/sample editing from zero to two
seconds at 48 kHz. Settings, bypass, reset and copying follow the existing node
conventions. See [dynamics and alignment](CORE_DYNAMICS.md) for exact controls,
Static's detector smoothing/hysteresis, Dynamic's peak curve, trigger wiring,
timing and the limitations of sample-peak limiting.

The Shure MV7+'s **Real-time Denoiser runs inside the microphone**. Configure it
in MOTIV Mix and select the physical MV7+ USB input here to receive its processed
audio. The mixer cannot read or toggle that hardware setting, and its own
SpeexDSP / RNNoise Denoiser node is separate. No Shure software algorithm is bundled.
See [Shure denoiser setup and research](SHURE_DENOISER.md) for the supported
USB path, persistence evidence, and the later listening comparison.

The interface uses Blender-inspired coloured category headers, grouped dials,
typed sockets and a black canvas with near-black surfaces. Text uses scalable
font metrics and grayscale rendering to avoid coloured fringes at fractional
zoom. Zoom uniformly scales each complete node: controls stay visible and node
proportions stay fixed. At very small scales the text naturally becomes smaller;
Ctrl+0 returns to 100%. The meters retain faded green/yellow/red zones which
fill as audio reaches each range, with smooth falloff. Inputs and outputs have
**Mute**; every intermediate node has one bottom **Bypass** button, grey when
off and red when on, with the same label in both states. Bypass skips that node's
processing and level adjustment while retaining its settings and connections.
Settings no longer include duplicate incoming-send controls.
The window resizes from
900 × 580 and includes maximise/restore beside its other caption controls.
New graphs start at 100% zoom. Existing saved positions and zoom are retained,
without automatic rearrangement. See
[the visual implementation notes](BLENDER_NODE_STYLE.md).

Compatible sockets snap within a forgiving screen-space radius. The existing
port outline brightens without an extra hover circle. Audio ports are matching
circles centred on the node border; empty ports have coloured outlines and
connected ports are filled. Passive node surfaces use the
ordinary arrow cursor; the move cursor appears only during a drag. Drop an
unconnected effect or mix onto a highlighted cable to insert it into the path.
To unplug a cable, drag it from its **input socket** onto empty canvas. Drop it
on another compatible input to move that connection instead. Hold **Ctrl** and
right-drag across cables to cut them. These gestures preview changes until
release; **Escape** leaves the original connections intact.
**Alt-drag**, or right-click **Detach node (reconnect)**, lifts a node out while reconnecting its
single incoming path to its destinations. Ambiguous or unrepresentable rewires
leave the graph unchanged. Hovering previews the target; releasing commits;
**Escape** cancels. Existing saved cable gains and mutes remain intact.

Read the [workspace guide](NODE_WORKSPACE.md) for node behavior, architecture,
migration and the Blender/TouchDesigner references behind this direction.

## Running apps as nodes

Windows process-loopback capture makes a Spotify or browser node possible without
adding a virtual cable for each application. It **copies the app's audio**; original
playback continues. To make the mixer the only playback path, assign the app to
VB-CABLE in Windows Volume Mixer and receive it through a Virtual input node.
Do not feed both copies into the same destination.

The virtual microphone sends directly to VAC Lite using exclusive WASAPI, keeping
its audio out of Windows' shared playback mix used for system-audio capture.
The listening and virtual outputs retain independent mutes. Direct access must be
allowed for Line 1; an unavailable direct connection reports an error instead of
falling back to shared playback. See [microphone audio in Discord screen shares](APP_AUDIO.md#microphone-audio-in-discord-screen-shares)
for the fix, measured results, and capture scope.

App trees, protected content and simultaneous sessions have limitations. See
[app capture and its Microsoft sources](APP_AUDIO.md). Real Spotify/game
capture and audible routing still require the deferred qualification session.

## Run and build

After building, launch **`artifacts/AudioMixer/AudioMixer.exe`**. The native engine
is alongside it, and the package includes the .NET desktop runtime. Closing the
window hides it to the tray; right-click its tray icon and choose **Quit Vine Mix** to stop audio. Start at
login is optional and off by default.

```powershell
.\scripts\build.ps1 -NoBootstrap
```

While the personal mixer is running, build a separate candidate:

```powershell
.\scripts\build.ps1 -NoBootstrap -PackageDirectory "$PWD\artifacts\AudioMixer-Candidate"
```

The build refuses to overwrite a running controller or engine. Packages include
the current guides and licences; historical screenshots and test fixtures stay local.

Omit `-NoBootstrap` on a fresh checkout. Free project-local tooling consists of
.NET 10, LLVM-MinGW and CMake. Pinned libsamplerate, nlohmann/json, SpeexDSP and
RNNoise sources and the matching RNNoise model are fetched during native
configuration. The build script compiles and packages; it
does not run audio tests, install drivers or change Windows defaults.

For a silent interactive preview, launch the desktop with **`--preview`**.
The preview opens no audio devices, starts no engine and saves no settings.

## Driver setup

The application reuses separately installed **base VB-CABLE** for incoming app
audio and **Virtual Audio Cable Lite** for its virtual microphone output. Neither
requires VoiceMeeter. Install these drivers separately if you need virtual input or output nodes.

| Node / application | Windows endpoint |
|---|---|
| Apps assigned to virtual input | VB-CABLE playback, usually CABLE Input |
| Virtual input node | VB-CABLE recording, usually CABLE Output |
| Virtual output node | VAC Lite playback, usually Line 1 |
| Discord / game microphone | VAC Lite recording, usually Line 1 |

Keep both sides of each cable enabled and Windows “Listen to this device” off.
Adding nodes does not create additional Windows endpoints. Incoming conversation
audio should stay outside any route returned to the virtual microphone.

Obtain drivers from [VB-Audio](https://vb-audio.com/Cable/) and
[VAC's official downloads](https://vac.muzychenko.net/en/download.htm). VAC Lite's
[private-home-use licence](https://vac.muzychenko.net/en/license.txt) remains a
dependency assumption; the app does not redistribute these drivers.

## Persistence and implementation

Configuration is stored at `%LOCALAPPDATA%\AudioMixer\settings.json`. Version 2
contains a graph; version 1 is migrated in memory. Reading old settings does not
rewrite them. The first edit saves the graph with an original-version backup and
an ordinary last-good backup. No microphone or listening device is substituted
because its name resembles a missing selection.

The dynamics redesign retains version 2 and the existing backup conventions.
Missing `dynamicsUiVersion` means legacy compressor presentation (`0`); newly
created compressors use `1`. Loading does not migrate a compressor, alter its
sound or rewrite its settings. Copy/paste retains the marker and saved controls.

The engine processes 48 kHz float32 stereo, in 240-frame blocks. Graph validation
and compilation occur outside the audio loop. Device and application workers own
bounded queues; an unavailable endpoint silences only its path. The local
current-user named pipe carries settings and telemetry, never microphone content.
Logs contain operational metadata. See [the protocol](CONTROL_PROTOCOL.md).

## Verification and current limits

[TESTING.md](../TESTING.md) separates offline graph/DSP and inert desktop checks from
later audible, hardware, latency and soak qualification. Building or opening the
window does not establish those hardware acceptance criteria.
Current implementation evidence is recorded in [NODE_VERIFICATION.md](NODE_VERIFICATION.md).

Run `scripts/verify-offline.ps1` for the complete silent maintenance suite: synthetic
audio, isolated settings, simulated endpoint inventory, private test pipes and
detached WPF rendering. It does not use the personal engine or audio devices.
The [maintenance review](MAINTENANCE_REVIEW.md) records fixes, measurements
and the limits of those checks.

The effect-quality audit corrected weak de-essing/plosive attenuation and
excessive reverb wet gain, and added measured reduction feedback. See
[de-esser measurements](EFFECTS_QUALITY_AUDIT.md) and
[the other-effect review](EFFECTS_QUALITY_AUDIT.md). Run
`scripts/verify-effect-quality.ps1` for the dedicated silent quality regressions.

The earlier uniform-zoom controller passed 583 detached checks (local historical report)
and 50 renders with zero binding errors. They verify that controls, node
proportions and socket geometry stay unchanged from 30% through 200%, including
manual collapse, repeatable Fit and the resizable layouts. Visual review of
50%/75% effects and Fit in the smallest window found no scoped issue.

The EQ/tone native update passed 48,522 graph assertions, 7,901 qualification
assertions and the synthetic engine lifecycle suite. The earlier readable-UI
candidate passed 226 offscreen checks (local historical report)
with 20 actual WPF renders at 96/144/192 DPI, zero binding errors and a Release
build with no warnings or errors. Visual review found no remaining issue within
that scope. [design-qa.md](../design-qa.md) records the review and tracks package
activation separately. Audible/physical interaction qualification and the
stopped endurance run remain **Deferred**.

This version has no VST hosting, recording UI, MIDI, network audio, ASIO routing or automated echo cancellation. Virtual microphone output uses exclusive WASAPI; parallel paths have no automatic delay compensation.
The Limiter and legacy compressor Limit mode do not provide true-peak detection
or lookahead. Final output saturation
protects sample bounds but is not a substitute for sensible mixing headroom.

The earlier fixed-console screenshots and test records remain under
`docs/design/` locally as historical evidence; the node workspace supersedes that UI.
