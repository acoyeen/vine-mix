# Vine Mix — node workspace

This development changes the mixer from a fixed console into an editable audio
graph. The earlier two-microphone/two-output limit is superseded by spawnable
nodes, bounded to 32 nodes and 96 connections in this version. Existing settings
migrate into an equivalent graph. Hardware and audible qualification remain a
separate session; see [testing](../TESTING.md).

## Interaction and visual direction

The application opens at 1280 × 800 DIPs, resizes down to 900 × 580, and includes
maximise/restore in its own caption controls. Black and near-black surfaces use
muted green signal accents. Coloured category
headers, grouped rotary parameters and edge sockets follow Blender's node-editor
structure; see [the visual implementation notes](BLENDER_NODE_STYLE.md). The canvas pans and zooms
inside the window. Bundled Inter 4.1 uses scalable font metrics and grayscale
rendering for fractional zoom. Zoom uniformly scales complete nodes, preserving
their proportions and every visible control. It does not automatically fold or
hide details. Text naturally becomes smaller at low zoom; Ctrl+0 returns to 100%.
Nodes stay expanded; there is no collapse button or H shortcut. Older saved
collapse flags remain compatible but do not hide controls. Whole-node shadow
effects are removed so text is not first rasterized into an intermediate
node bitmap. Node headers, sockets, recessed controls and thin separators
provide structure. A source sits at the left, processing in the middle, and a
destination at the right; layout never changes audio evaluation order.

Blender's node editors informed searchable creation, left-to-right sockets,
curved links, middle-button navigation and framing the graph. Its reroute concept
informed the original library; this mixer now keeps old Reroute nodes compatible
without offering another duplicate volume node for creation.
TouchDesigner's audio CHOPs informed the separation between sources, sum/gain
operations, filtering and dynamics. These are interaction and product references;
this application uses its own WPF implementation and native audio engine.

- [Blender node editors](https://docs.blender.org/manual/en/5.3/interface/controls/nodes/node_editors.html)
- [Blender reroute nodes](https://docs.blender.org/manual/en/latest/interface/controls/nodes/types/layout/reroute.html)
- [TouchDesigner Math CHOP](https://derivative.ca/UserGuide/Math_CHOP)
- [TouchDesigner Audio Para EQ](https://derivative.ca/UserGuide/Audio_Para_EQ_CHOP)
- [TouchDesigner Audio Dynamics](https://derivative.ca/UserGuide/Audio_Dynamics_CHOP)

Use **Add node**, Shift+A, or the canvas context menu. Drag headers to move nodes.
Connect matching sockets by dragging or clicking an output and then an input.
Multiple inputs may use the same microphone, virtual input or running app.
Right-click an input and choose **Duplicate input** to add a named copy nearby,
selected for placement. It retains the source and controls, starts without cables,
and can be undone/redone as one edit. Each copy has its own level, mute, Mono,
name, position and routing. Copy/paste retains input assignments too. Routing
several copies to the same destination adds their signals together. Physical and
virtual output assignments remain exclusive to one node per playback device.
The mouse wheel zooms through fixed stops; middle-drag pans; Home frames the
graph. Click the zoom percentage or press Ctrl+0 for 100%. **F** frames selected
nodes while the canvas is focused; the same action
is in the node context menu. It centers their rendered bounds at 30–100% zoom,
without moving nodes or creating an Undo entry. Fit and Home still frame all
nodes. With no selection, F gives a short instruction and leaves the view alone.
Open **Settings → Help & shortcuts**, or press **F1** outside text entry, for
the keyboard and mouse reference. It scrolls in smaller windows and closes with
Escape or Close. The workspace is unavailable for editing while help is open;
audio continues running. Closing help restores keyboard focus when possible.
**Settings → Find node…** or **Ctrl+F** outside text entry opens a search of the
current graph by name, type, device or app. Up/Down chooses a result; Enter or
Go to node selects that node and frames it. Duplicate names remain separate
results. Filtering and closing with Escape leave the selection and camera alone.
Search does not interrupt an active graph, dial or EQ drag and creates no Undo
entry or audio change.
With two or more nodes selected, right-click a selected node or empty canvas and
choose **Align selected nodes → Left edges** or **Top edges**. Alignment uses the
leftmost or topmost selected edge as its anchor. Left edges forms a column;
Top edges forms a row. Both set exactly 48 DIP between the actual node bodies,
ordering cable sources before the nodes they feed, including paths through
unselected nodes. Branches and unrelated nodes use their existing spatial order
to break ties. Every alignment closes larger gaps and opens smaller ones to the
same fixed spacing. Audio, sidechain and control cable directions
all contribute, including temporarily muted connections.
Each action creates one Undo step. Connections, sound controls, selection and
camera stay in place; already-aligned, spaced choices are unavailable.
With the canvas focused, **Alt+X** chooses a spaced horizontal row (Top edges),
**Alt+Y** a spaced vertical column (Left edges), and **Alt+A** runs **Auto Align**.
Auto compares the spread of selected node positions, choosing a row when the
horizontal spread is greater or equal, otherwise a column. Node sizes determine
clearance, not the chosen direction. Auto also appears in the alignment submenu;
its chosen direction stays fixed if a pending edit is replayed. Text fields,
other focused controls and active gestures keep their own keyboard behavior.
New graphs begin at
100%; new or version-1-migrated layouts use 330 DIP row spacing for larger source
and output nodes. Loading a saved graph retains its zoom and node positions.
The application does not automatically rearrange an existing workspace. Delete removes a
selected node or connection; Escape cancels a pending cable. Device selection,
selectors, parameter groups, feedback and actions live inside their nodes in
separate areas. Numeric parameters use a common dial with a persistent label
and value/unit entry. EQ's three dials, level and tone controls, and primary effect
controls use 86-DIP faces; secondary effect controls use 58-DIP faces. Primary
controls are Threshold for Compressor, Expander, De-esser and Plosive remover;
Ceiling for Limiter; Duck amount for Ducking; Cutoff for filters; Reduction for
Denoiser; Mix for Reverb; L/R balance; and Milliseconds for Alignment Delay.

The top-right **Settings** menu contains workspace preferences, start-at-login,
Windows sound settings, retrying assigned devices and **Restart audio engine**.
Restart briefly interrupts audio while keeping the workspace open. It saves queued
edits before restarting when possible; edits that cannot be saved remain queued
for reconnection. The action remains available when the engine is offline and
is disabled during a restart. The workspace message reports progress and the result.

An unavailable input gently flashes its existing source name red, with a faint
outer glow around the node and a red warning triangle above the volume meter.
Hover the source for the reason and recovery step.
The ordinary layout, header and white selection border remain intact. Reduced
animation keeps the source text steadily highlighted. Node sizes and socket
positions stay fixed, and healthy muted inputs retain their usual appearance.
See [input availability](INPUT_AVAILABILITY.md) for the state distinctions.

**Settings → Right-click drag to pan** adds right-button panning on
empty canvas while retaining middle-drag. It is off by default. With the option
enabled, pressing waits for movement; crossing a small drag threshold starts
panning, and release after a pan does not open a menu. Releasing without dragging
opens the existing canvas menu, even after a long hold. Nodes and cables keep
their context actions, and Ctrl+right-drag retains cable cutting. The setting is
stored alongside Reduced animation in `%LOCALAPPDATA%\AudioMixer\ui.json`,
independently of audio configuration; preview windows keep it only in memory.

All value fields remain 110 × 28 DIPs with 12-DIP type and the same quiet border
behavior. Categorical choices
use selectors, actions use buttons and passive meters/readouts stay distinct.
The workspace footer shows action results, connection instructions and errors
as they happen. Messages wrap at narrow window sizes and remain until replaced
or dismissed. Dismiss returns to the essential dial instructions: vertical
drag, Shift for fine adjustment, exact entry, Enter/Escape and reset.
Nodes omit repeated gesture instructions and parameter-category headings.
Arrow and Page Up/Down keys adjust focused dials; Home and double-click reset.
Dial and entry focus are visible; numeric-entry borders stay quiet until hover
or focus, while validation remains visible. Thresholds consistently use dBFS;
gain and attenuation use dB, ratios use `4.0:1` formatting, and EQ/filter Q values
share the same precision. Precision follows the parameter's increments,
and starting and confirming an unchanged edit preserves the stored exact value.
Enter applies a typed value. Tab and Shift+Tab apply a valid value before moving
to the next or previous control. Invalid text keeps its validation outline and
remains editable; Escape cancels. Sample counts reject fractional entries. Frequency and broad positive time
ranges use logarithmic travel; the EQ graph and Voice Color pad remain interactive.
The former **Incoming connections** panel and its per-connection level controls
are removed. Existing cable levels and mutes remain saved and applied. Hover a
cable to see only its source and destination. Selecting a cable shows its type,
level and on/off state in the workspace message. A cable's context menu can
still turn an audio connection on or off. When a saved cable level is nonzero,
**Reset connection level to 0 dB** restores that level while retaining its route
and on/off state; Undo restores the previous level. Use Mix nodes for new volume
adjustments.

To remove one cable, drag it away from its **input socket** and release over
empty canvas. Releasing near another compatible input moves that cable instead,
retaining its saved level and mute. The original audio route stays active during
the preview. Escape or loss of mouse capture cancels without changing it.
For several cables entering the same socket, drag along the incoming noodle you
want to pick up, or select that cable first. You can also grab the noodle near
its input end directly. Overlapping, ambiguous branches remain connected until
you choose a specific cable; their ordering in the saved file is irrelevant.
Hold **Ctrl** and right-drag a stroke across cables to cut the crossed connections
on release. Node and cable context menus remain available without Ctrl.

Drag one unconnected processing or Mix node onto an audio cable to
insert it. An eligible cable highlights before release. The existing cable's
level and mute stay on the connection entering the inserted node; the new
outgoing connection starts at 0 dB and unmuted. Sources, outputs and groups are
not inserted this way, and linked-control cables are unaffected.

Hold **Alt when starting a drag** to detach a node while moving it, or choose
**Detach node (reconnect)** from its context menu. A node with one incoming audio
connection and one or more outgoing audio connections can reconnect its source
directly to those destinations. The replacement connections retain the combined
cable levels and mutes. The detached node keeps its settings, but its processing,
level and mute leave the audio path with it. Reinsertion applies them once.
Nodes with attached linked-control or detector cables cannot be detached this
way; remove those cables first. A Ducking node can be inserted into an ordinary
audio cable while its trigger remains connected. If the resulting
connections would be invalid or their combined levels exceed the supported
range, the existing connections stay intact.

## Node library

The Add menu offers 23 types, using the same category mapping as node headers:
Inputs (microphone, virtual input, running app), Outputs (physical and virtual),
Effects (EQ, tone, Voice Color, high/low-pass, Compressor, Limiter, Ducking, Expander / Gate,
Alignment Delay, denoiser, de-esser, plosive remover, reverb), Mixing (Mix, Balance,
Mono), and Controls (linked control).
Gain / Multiply and Reroute are hidden from creation because Mix covers their
volume role with one incoming connection. Both remain supported for existing
graphs and copied legacy nodes, making 25 compatible types. Category submenus
keep the initial menu compact; typing searches
both node names and category names. Down/Enter/Escape navigate, add and dismiss.

The toolbar uses flat controls and contains the node count, Undo, Redo and
Activity. Save and connection status remain in the application header.
Undo/Redo retain up to 100 graph edits for the current controller session:
nodes, cables, assignments, parameters, mute, bypass, rename and layout edits.
One pointer drag is one step, regardless of its duration; separate clicks,
typed values and keyboard increments remain separate edits. A new graph edit
after Undo clears Redo. Use Ctrl+Z to undo and Ctrl+Y or Ctrl+Shift+Z to redo;
focused text fields retain their own local text undo. The buttons describe the
next available action in their tooltip and accessible name. Undo preserves the
current canvas view and does not change application preferences.

Activity lists applied edits and retains the latest workspace message, including
after that message is dismissed from the footer. Session history is not saved
across app restarts. Conflicting external changes and rejected engine edits
invalidate history rather than restoring an obsolete graph over newer work.

Navigation updates the local view without a configure request, history entry or
save-status change. A subsequent substantive edit includes that current view in
its save. Until then, pan/zoom is session-only. No-op setting changes are ignored.
Node widths remain explicit; status has a reserved line so device state and
clipping cannot change node height or socket positions. Real editing fields keep
the text cursor; passive node surfaces use the arrow until actively dragged.
Wheel input over closed selectors, text, sliders, dials and EQ zooms the canvas;
open dropdowns and menus retain scrolling. Audio sockets are matching circles
centred on each border. Empty sockets have coloured outlines; connected sockets
are filled. Control sockets remain rounded squares and sidechain sockets remain
diamonds. Hover, keyboard focus and connection preview brighten the existing
outline without changing the socket size. Their larger invisible hit targets
and screen-space snapping remain unchanged.

| Node | Function and controls |
|---|---|
| Microphone | Explicit physical input, Mono toggle, level, mute, channel meters and connection status |
| Virtual input | Recording side of base VB-CABLE; one combined app mix |
| Running app | Windows process-tree loopback capture; running-app picker and reconnect identity |
| Mix | One input: branch volume; several inputs: sum incoming stereo audio. Saved cable levels/mutes still apply; node level and bypass |
| Parametric EQ | Zero to eight independently enabled biquads; interactive points, frequency/gain/Q fields, shape menu, calculated response, measured post-EQ spectrum and bypass |
| High-pass filter | Cutoff and resonance/Q; starts at 80 Hz and Q 0.70710678; bypass |
| Low-pass filter | Cutoff and resonance/Q; starts at 12 kHz and Q 0.70710678; bypass |
| Low / Mid / High | Three gain dials, each −12 to +12 dB: 200 Hz low shelf, 1 kHz bell and 5 kHz high shelf; node bypass |
| Voice Color | Warm–Bright / Natural–Broadcast grid; body/presence contour, low-mid cut, compression, density and subtle doubling, with a small room; exact Tone/Character values, neutral reset and bypass |
| Compressor | Stereo-linked compression; threshold, ratio, attack, release and make-up; optional detector-only sidechain; bypass |
| Limiter | Sample-peak ceiling and release, with no lookahead; reduction feedback and bypass |
| Ducking | Lower main audio to a fixed attenuation target when its separate Trigger becomes active; threshold, duck amount, attack and release; bypass |
| Expander / Gate | Reduce quiet sound with downward expansion or a gate; five Expander dials or six Gate dials show only settings relevant to the selected mode; bypass |
| Alignment Delay | One exact stereo delay, edited in milliseconds or samples; 0–96,000 samples / 0–2 seconds at 48 kHz; bypass |
| Denoiser | Algorithm dropdown: SpeexDSP with Reduction, or RNNoise with Mix; adds 20 / 30 ms respectively, including bypass |
| De-esser | Stereo-linked dynamic high-band reduction for loud sibilants; frequency, threshold and maximum reduction |
| Plosive remover | Dynamic low-band reduction for bass bursts such as microphone pops; cutoff, threshold and maximum reduction |
| Reverb | Stereo room tail; wet/dry mix, decay, damping and pre-delay |
| Balance | Attenuate the opposite stereo channel; centre preserves the original stereo signal |
| Mono | Average left/right and send the result equally to both channels |
| Linked control | Share a level offset across connected nodes; bypass removes its contribution; carries settings, not audio |
| Listening output | Explicit physical device with its own worker, queue, level and mute |
| Virtual output | Playback side of VAC Lite, delivered as a microphone to Discord/games |

The compatible legacy **Gain / Multiply** node still adjusts level, and a legacy
**Reroute** still passes audio through its named intermediary. Neither appears
in Add or search. No automatic replacement changes an existing graph.

Effects, Balance and Mono omit the generic Level row. Their meaningful controls
remain, including EQ band gain, tone gains, compressor makeup and reverb mix.
An older nonzero node level is retained in audio and exposed through **Saved
level** in the node context menu; explicitly reset it there if unwanted. Add a
Mix node when an effect chain needs a separate branch volume.

Every node's context menu has **Reset node to defaults**. It restores the node's
sound controls, including stereo/Mono and bypass defaults, while preserving its
ID, name, position, connections and device/app identity. Output reset restores
the safe muted default. Reset can change audible gain; test it on a preview or
an isolated quiet route.

Each input can fan out to several nodes. Multiple audio connections arriving at
a processing or output node are summed. A linked control adds its dB offset to
the destination's own level. An audio node accepts at most one linked control.
Saved legacy control mutes still silence their destinations until explicitly
cleared, preserving the safety of existing routes.

A Compressor accepts one **Sidechain** connection; Ducking accepts one
**Trigger / Sidechain · detector only** connection. Their diamond sockets carry
audio for detection, never the audible sum. Square Linked control sockets carry
parameter control. For Ducking, connect Music → Audio input → Audio output →
Output and Microphone → Trigger. The microphone can also feed an audible
destination independently. Missing or silent triggers release to unity without
falling back to main-audio detection. Ducking targets a fixed Duck amount while
active; a Compressor's ratio instead varies reduction with detector level.
Trigger smoothing is 5 ms rising / 50 ms falling with fixed 3 dB hysteresis.
Sidechain cables participate in cycle validation and copy with selected nodes.

**Limiter** applies instantaneous stereo-linked peak reduction followed by
smoothed release. Ceiling and Release are its only parameters. It enforces the
chosen sample ceiling at that node while enabled, including saved node/control
gain. Bypass removes limiting; downstream boosts and later summing can exceed
the ceiling. It has no lookahead or oversampled true-peak detection. New
compressors do not contain a Compress/Limit selector or Ducking preset; legacy
compressors retain their original modes and all saved settings. Their mode and
detector selectors share one row, without a technical compatibility banner.
**Alignment Delay** crossfades delay changes, and its bypass removes the delay;
the rest of the graph is not automatically delay-compensated. See
[dynamics and alignment](CORE_DYNAMICS.md) for defaults, exact ranges, routing,
real-time behavior and listening limitations.

New microphones start in stereo with **Mono** off. Mono averages left and right
into a centred signal. Existing microphone channel selections remain active
until the Mono control is changed: switching it off restores stereo, then
switching it on uses the averaged signal. A genuinely mono source uses one meter
lane.

Audio nodes have vertical meters on the left, with separate live left/right
levels for stereo and one centred lane for mono. Dim colour zones remain visible
behind the levels. Peaks rise immediately, hold for 100 ms, then fall at 24 dB
per second toward the current level. Silence decays to zero; mute and an offline
path clear the meter immediately. Stale telemetry also decays instead of leaving
a frozen peak. Reduced animation updates at 4 Hz while retaining short peaks;
mute and clipping indications remain immediate.

Hover the meter for **Highest observed peak**, with independent left/right sample
peaks in dBFS (one reading for mono). Values stay in the tooltip, keeping the
node compact. The meter's red caps stay lit for two seconds after the last
observed overload, then fade back to gray over one second. Another overload
immediately restores the full red indication and restarts that interval. The
fade also completes if telemetry stops. Reduced animation updates the fade at
four steps per second. Fading the caps does not clear the hover peak history.
Both caps report a node-level event; they do not identify which channel exceeded
full scale. Exactly 0 dBFS alone does not light them.

The meter is purely informative. Clicking or pressing Enter/Space does not clear
readings or alter audio. Keyboard focus exposes the same information to assistive
technology. Silence, gain changes, bypass and reduced animation preserve the
readings; muting, unavailable audio, source reassignment and channel changes
clear them. Closing the window's process also clears this display history. The
moving bars continue to behave as described above.

These are sample peaks observed in this window's telemetry, not true-peak or
complete engine-lifetime measurements. Native readings stop at 0 dBFS; a
separate **Over 0 dBFS detected** message reports overload without inventing its
amount. After source reassignment, retained history waits for new audio-frame
progress and discards the transition measurements, which can include an
in-flight block from the previous source.

There is no arbitrary DC-offset or sample-wise arithmetic expression node in
this release. Mix exposes summing and branch volume through audio-specific
controls. Recording, VST hosting, MIDI, modulation oscillators,
reusable graph groups and group frames remain
future additions rather than nonfunctional entries in the node library.

## EQ and dynamics

The EQ supports zero to eight stereo biquads at the engine's 48 kHz processing
rate. The node is 620 DIPs wide and its editor is 300 DIPs high; those dimensions
stay fixed in canvas coordinates. Existing configurations retain every saved
band. New EQs use five default flat bands, with Q 1 for their bells. Removing
every band also produces a flat response without disconnecting the node.

Double-click an empty part of the plot to add a bell band at that frequency and
gain. Ctrl-click points to toggle selection, drag empty plot space to box-select
bands, or press Ctrl+A while the plot is focused to select all bands. Holding
Ctrl while drawing a selection box adds the enclosed bands to the current
selection; Escape cancels the box. A plain click on an unselected point selects
that band alone, while dragging a selected point moves the whole selection.
Horizontal movement changes frequency and vertical movement changes gain.
The group stops at plot limits while keeping its spacing; notch and saved pass
filters move horizontally only.
The arrow keys also move the selected bands; hold Shift for finer pointer or
keyboard adjustments. Delete removes the selection and Home resets each
selected band's position. The wheel zooms the canvas even over the graph.
Right-click a point to enable/disable it, choose **Reset position**, or remove
it. For a selected group, **Remove selected bands** removes the whole selection.
The same menu offers **Shape** choices
of Bell, Low shelf, High shelf and Notch, with a checkmark beside the current
choice. Width presets are removed. Adding a ninth band is rejected.

Double-clicking a band is the same as **Reset position**: its frequency returns
to its saved reset anchor and gain returns to zero, preserving shape, Q and
enabled state. New points retain their creation frequency as that anchor; an
older point without one adopts its existing frequency when edited. Right-click
empty plot space for **Reset EQ to flat**, which restores the five default flat
bands. Resetting the curve keeps the node's connections and other sound controls.

Frequency, Band gain and Width/Q use the larger 86-DIP dials below the plot;
these dials edit the active band only, including when several bands are
selected. There is no Precise values expander or filter-type dropdown.
Frequency spans 20 Hz–20 kHz, gain spans −24 to +24 dB, and Q spans 0.1–20. Q describes
resonance/bandwidth. Width/Q resets to 0.70710678 for shelf/pass shapes and 1 for
Bell or Notch. Its reset marker follows the selected band and shape without
changing the stored Q; only an explicit reset applies the default.
Notch gain editing is disabled because that shape uses
frequency and Q; choosing Notch clears its unused gain. Existing saved high-pass
or low-pass EQ bands keep their shape and values until explicitly changed,
with gain editing disabled for those shapes too. Changing one band preserves
the others. The smooth response line represents the actual filter equations;
the separate shaded underlay measures audio leaving the EQ against a right-hand
dBFS scale. Silence stays at the floor, and an adjustment only changes measured
energy where the source contains sound. The FFT uses independent stereo power,
so opposite-polarity channels do not cancel. See
[the analyzer contract and verification](EQ_SPECTRUM_ANALYZER.md).

Use the standalone **High-pass filter** and **Low-pass filter** nodes for new
pass filters. Each has one fixed filter shape, a Cutoff dial
(20 Hz–20 kHz) and Resonance/Q dial (0.1–20). Their defaults are 80 Hz and
12 kHz respectively, both with Q 0.70710678.

**Low / Mid / High** is a simpler node with three independent gain dials. Low
uses a 200 Hz shelf with Q 0.70710678, Mid a 1 kHz bell with Q 1, and High a
5 kHz shelf with Q 0.70710678. Each gain spans −12 to +12 dB and defaults to 0 dB.
Drag a dial vertically, use Shift for fine adjustment, or enter an exact gain.
The frequencies and shapes stay fixed; use the full EQ to customize filters.
All three bands remain enabled, while whole-node bypass retains their gains.

**Voice Color** combines tonal balance with a broadcast character on its pad.
Left is Warm, right is Bright; moving upward from Natural toward Broadcast
adds body and presence, a low-mid cut, compression, density and subtle delayed
doubling, with a small room. Bottom centre has flat EQ and transparent processing.
The square grid has quarter divisions, with larger semibold axis labels inside
the surrounding well. The handle stays clear of the labels at every extreme.
A discreet reset marker replaces the visible Neutral text. The pad is the primary
control; the two smaller numeric dials provide precise Tone and Character values.

Double-click the pad or press Home while it is focused to reset both axes.
Arrow keys move one percentage point; Shift reduces that to 0.1 point.
Tone spans −100% to +100% and Character spans 0% to 100%; double-click a dial
to reset just that axis. Shift-dragging the pad gives finer relative movement.
Pad editing changes sound without dragging the node; wheel input still zooms
the canvas. The pad and its controls retain their proportions at every zoom.

Storage retains `tone` from −1 to +1 and `ambience` from 0 to 1; the latter
now controls the combined Character treatment. Existing nonneutral coordinates
intentionally sound different with this mapping. Loading does not rewrite the
saved coordinates, routing, names or layout. The original two-axis interaction
does not claim to emulate Intellipan's exact sound. See
[the exact mapping and limitations](VOICE_COLOR.md).

All intermediate nodes have a full-width **Bypass** switch at the very bottom,
below the sockets. It is grey when off and red when on; the label stays Bypass.
Inputs and outputs alone have Mute. Bypass passes the incoming mix through
without the node's processing, local level or linked level adjustment. Incoming
and outgoing cable levels remain active. A bypassed linked control contributes
no gain. Switching preserves parameters and connections. Older saved mute flags
remain effective; a legacy muted intermediate explains its state, and clicking
Bypass explicitly clears its mute while bypassing its processing.

Coefficients follow the [W3C Audio EQ Cookbook](https://www.w3.org/TR/audio-eq-cookbook/),
adapted there from Robert Bristow-Johnson's formulas. The native engine computes
coefficients outside its audio loop and crossfades EQ, tone and pass-filter
changes over 480 frames (10 ms). Offline numerical
tests check known tone responses; these do not establish audible transparency
on hardware.

The controller coalesces edits on a 100 ms target cadence rather than postponing
the send whenever another movement arrives. Requests remain serialized, and
edits arriving during a request survive its acknowledgement. Polling and engine
response time can extend the interval; this is not an end-to-end audio latency
guarantee. The offline cadence check includes sustained edits, slow replies and
revision-conflict retries.

The **Compressor** links both stereo channels using the larger
instantaneous detector peak. It retains the original feed-forward hard-knee
curve and attack/release smoothing, with an optional external detector. The
separate **Limiter** enforces the selected sample ceiling at its output; saved
compressors in Limit mode retain the same limiter behavior. **Ducking** uses a
smoothed trigger and a fixed attenuation target with a stereo-linked gain
envelope. None uses lookahead or oversampled true-peak detection. The engine also
saturates final hardware output samples and reports clipping; enough headroom
is still needed when summing routes or boosting after a limiter.

The four additional effects are implemented rather than placeholder nodes:
**Denoiser** estimates and attenuates stationary background noise; **De-esser**
reduces a loud high-frequency band; **Plosive remover** reduces low-frequency
bursts; **Reverb** produces a stereo room tail. Their algorithms, controls and
limits are documented in [EFFECTS_RESEARCH.md](EFFECTS_RESEARCH.md). A physical
pop filter remains useful because processing cannot recover a clipped microphone.

De-esser, Plosive remover, Compressor, Limiter, Ducking and Expander / Gate use
one compact telemetry line: reduction or state on the left and detector/trigger
level in dBFS on the right. Ducking shows **Connect a trigger**, **Below threshold**
or **Ducking** in that line. Hover help supplies further detail.
**Below threshold** means the node is active but the detected signal is too
quiet to trigger compression or band reduction. Expander / Gate instead
attenuates below its threshold and displays **Open** at unity. Lower the
de-esser/plosive Threshold (make it more negative) to catch quieter consonants,
then use Maximum reduction to limit the effect. The high/low
band reading is shelf attenuation, not a change to the whole signal's loudness;
Frequency is the shelf midpoint. Saved settings are never automatically tuned.
See [the de-esser quality audit](DEESSER_QUALITY_AUDIT.md) for measured behavior.

Denoising adds 960 frames / **20 ms for SpeexDSP**, or 1440 frames / **30 ms for
RNNoise**, at 48 kHz. Choose either in the node's **Algorithm** dropdown. SpeexDSP
exposes Reduction in dB; RNNoise exposes Mix from original (0%) to fully denoised
(100%). New nodes start with RNNoise at 100% Mix. Each algorithm retains its own
setting when switching. Saved and pasted nodes retain their selected algorithm;
older nodes without an algorithm setting keep SpeexDSP. Reset restores both
processors' controls while keeping the selected algorithm. Both are local speech processors and may alter humming
or singing. Its dry bypass path carries the
same delay to avoid a time jump when toggling. There is no automatic compensation
between separate graph branches: combining a direct copy with a denoised copy
can create comb filtering. Other effect paths retain their documented direct
signal timing; reverb pre-delay applies to reflections. Audible quality and
complete-path latency require the later hardware qualification session.

## Running applications as sources

Windows supports capturing a selected process and its descendants independently
of its playback endpoint on supported builds (20348 or newer). Windows 11 meets
that minimum. The implementation checks capability and uses a dedicated worker
for each selected application. See Microsoft's
[application-loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/).

This copies rendered audio. It does not move or suppress the app's original
playback. Capturing Spotify and then sending that copy to its existing listening
device can double what you hear. To put playback entirely through this mixer,
assign the application to the base VB-CABLE playback endpoint in Windows Volume
Mixer and receive it through a Virtual input node. Do not simultaneously route
that cable mix and an app capture of the same audio into the same destination.

App capture does not create a new Windows playback device per node. Browser
tabs may share a process tree, protected content may not be available, and apps
with unusual process or audio architectures need qualification. The selected
executable path is persisted; a PID is only a current-process hint. The mixer
does not automatically launch missing apps. See [APP_AUDIO.md](APP_AUDIO.md) for
the implementation details and later compatibility tests.

## Audio graph and recovery

Configuration version 2 contains the graph, its layout and processing settings.
The engine validates node kinds, identities, numerical bounds, ports, duplicate
connections, cycles and endpoint roles before accepting an edit. The control
pipe carries settings and telemetry, never audio. Layout and level edits reuse
existing workers; an unrelated device is not restarted to apply them.

The graph is compiled outside the audio loop into a bounded processing plan.
Plan adoption happens at a block boundary. Processing remains 48 kHz float32
stereo with 240-frame blocks on the hardware-independent clock. Physical and
virtual endpoint workers retain independent queues, conversion, drift correction
and recovery. Input nodes with identical capture bindings share one worker: each
block is read once and supplied to every copy before its independent processing.
Legacy microphone channel selections remain part of the capture binding, so
different channel selections keep separate workers. App capture workers provide
the same stereo input contract. Output workers remain exclusive per endpoint.

Only the selected path goes silent when a device disappears or an app exits.
Returning paths discard stale buffered audio. Every listening output consumes
its own live queue. Audio cycles are rejected, including cycles made of muted
links, so enabling a connection cannot unexpectedly turn a saved graph cyclic.
These checks cannot detect feedback made outside the app, such as a speaker
feeding a microphone or an external application routing a virtual microphone
back into the mixer.

## Existing settings and startup

The previous graph becomes three sources, two named mix nodes and three outputs.
All six source sends are retained as cables, with disabled sends represented as
muted cables. Their individual dB values remain saved. The A/B master settings
become the corresponding mix-node settings. New output nodes start muted, and
new devices are unassigned until selected.

Reading version 1 does not rewrite the file. The first successful edit writes
version 2; the original version-1 settings are retained separately, as well as
the ordinary rolling last-good backup. The graph becomes authoritative after
migration; historical fixed-console fields are retained for the migration record.

The dynamics redesign does not migrate existing compressors. Missing
`dynamicsUiVersion` reads as `0` and retains the existing mode and detector controls;
new compressors use `1`. This presentation marker does not alter processing.
IDs, names, positions, internal/external detection, limiter mode, hidden mode
settings, sidechain cables, saved gain, linked controls and bypass are preserved.
An external compressor is never inferred to be fixed-depth Ducking. Reading
does not rewrite settings; later edits use the existing save/backup conventions.
Copy/paste retains the marker and hidden settings. Explicit reset changes sound
controls, preserving identity, routing and the legacy presentation marker.

The interface and engine remain separate processes. Closing the window hides it
to the tray, and explicit Quit stops mixing. A controller failure does not stop
the native engine. Start at login remains optional. The application does not
install drivers, change Windows defaults, launch apps or reassign their outputs.

## Qualification boundary

Build success, synthetic DSP tests, read-only application discovery and inert UI
checks are useful implementation evidence. They do not validate audible routing,
Spotify/protected-content capture, Bluetooth behavior, latency, reconnect timing,
or long-running stability. The later procedure in [TESTING.md](../TESTING.md)
records those separately and preserves the user's quiet initial listening setup.

The earlier uniform-zoom controller passed [583 detached checks](design/uniform-zoom-final/results.json)
and 50 renders with zero binding errors. Those tests compared fixed node dimensions,
fonts, borders, controls and socket positions across 30–200% zoom, verified the
then-available explicit collapse and repeatable Fit, and retained the resizable-window
checks. Visual
review of 50%/75% effects and the complete EQ fitted into the smallest window
found no scoped issue. This is detached rendering evidence, not a measurement
of physical-display sharpness or live zoom responsiveness.

The earlier EQ/tone update passed 48,522 native graph assertions, including all eight
bands, zero-band flat response, each tone dial's boost/cut, bypass, linked-control
continuity, validation and JSON round trips. The broader qualification suite
passed 7,901 assertions over 4,527,360 synthetic frames, and the stub-worker
engine lifecycle suite passed 210 concurrent commits. The earlier readable-UI
candidate passed [226 offscreen checks](design/blender-readable-final/results.json)
with 20 actual WPF renders at 96/144/192 DPI, zero binding errors and a Release
build with no warnings or errors. The visual review found no remaining scoped
blocking issue. Consult [design-qa.md](../design-qa.md) for that review and the
separate activation record. Real mouse/keyboard, audible and eight-hour endurance
qualification remain **Deferred**.
