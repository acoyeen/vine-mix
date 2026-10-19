# Dynamics and alignment

The Effects library offers separate **Compressor**, **Limiter** and **Ducking**
nodes, alongside **Expander / Gate** and **Alignment Delay**. New compressors
expose compression controls without a Compress/Limit selector or Ducking preset.
Existing saved compressors retain their original modes, detector, settings and
routing. Their mode and detector selectors share one row, without a technical
compatibility banner. Updating does not insert effects,
convert external compressors into Ducking nodes or rewrite saved workspaces.

These processors use the existing node appearance, meters, grouped numeric dials,
bottom Bypass button and context-menu reset. Copy/paste and save/load carry their
settings. Reset preserves the node's identity, position, name and connections.
The interface sends continuous edits on the existing 100 ms target cadence;
audio processing adopts changes at block boundaries and retains its history.

Every adjustable scalar has a persistent label, dial and value/unit entry.
Related parameters share compact aligned groups without repeated category
headings. Selectors, actions and the bottom Bypass button remain separate.
One compact telemetry line places reduction or state on the left and the
detector/trigger level in dBFS on the right. The workspace footer keeps essential
dial instructions visible, so each node does not repeat them. Numeric-entry
borders stay quiet until hover or focus; validation remains visible.
Arrow keys adjust a focused dial, Page Up/Down use larger increments,
and Home or double-click resets the parameter. Focus is visible on both the dial
and entry. Frequency and broad positive time ranges use logarithmic travel.
Attack times and ratios retain decimals, Q retains hundredths and delay shows
milliseconds to three places. Entering an unchanged exact value preserves the
stored precision. The wheel zooms the canvas; it does not edit parameters.

## Expander / Gate

Use this node to reduce quiet background sound between phrases. **Expander**
progressively turns down audio below Threshold. **Gate** switches between an
open target and the chosen attenuation floor, with attack/release smoothing.
Both modes link the two stereo channels, preserving their relative balance.

| Control | Default | Range | Purpose |
|---|---:|---:|---|
| Mode | Expander | Expander / Gate | Gradual downward expansion or gating |
| Threshold | −40 dBFS | −80–0 dBFS | Level where the gate opens / expansion stops |
| Ratio | 2:1 | 1:1–20:1 | Amount of downward expansion; not used in Gate |
| Attack | 5 ms | 0.1–200 ms | How quickly attenuation opens |
| Hold | 50 ms | 0–1000 ms | Keeps Gate open before attenuation returns |
| Release | 150 ms | 5–2000 ms | How quickly attenuation closes |
| Range | 60 dB | 0–80 dB | Maximum attenuation, not an upward gain |
| Hysteresis | 3 dB | 0–12 dB | Gate closes below Threshold minus this margin |

Set Threshold above the unwanted background level and below normal speech.
The detector and Reduction readouts help show why the node is opening or closing.
Range limits how far quiet sound is reduced; it is not necessarily digital
silence. Range zero disables attenuation. Expander Ratio 1:1 also gives unity.
Expander shows five relevant dials; Gate shows six. Hold and Hysteresis are
hidden in Expander mode, and Ratio is hidden in Gate mode. Hidden values remain
saved. Both modes use two rows and keep the same node height.
A gate cannot remove noise while speech
is keeping it open, and fast settings can chop consonants or produce audible
modulation. This node introduces no lookahead buffer and cannot anticipate a
transient before it arrives.

The stereo-linked detector has a 0.5 ms attack and 20 ms decay, reducing sensitivity
to individual waveform zero crossings. In Expander mode, the target attenuation
below Threshold is `(Ratio − 1) × (Threshold − detector level)` in decibels,
capped by Range. Above Threshold it targets unity. Gate targets either unity or
the Range floor, using separate opening/closing thresholds and Hold. The selected
Attack and Release smooth the gain rather than adding a fixed buffer delay.
Continuous parameter targets and mode changes ramp over 10 ms.

The controls follow established gate/expander terminology; see the original
vendor explanations of [threshold, ratio and range](https://prod.fabfilter.com/help/pro-g/using/dynamiccontrols)
and [attack, release, hold and hysteresis](https://www.fabfilter.com/help/pro-g/using/timecontrols).
The mixer implements its own bounded processor rather than copying a vendor's
gate algorithm.

## Compressor

The Compressor applies the established stereo-linked feed-forward hard-knee
curve. Reduction varies with detector level above Threshold according to Ratio.
This differs from the dedicated Ducking node's fixed attenuation target.

| Control | Default | Range |
|---|---:|---:|
| Threshold | −18 dBFS | −60–0 dBFS |
| Ratio | 4:1 | 1:1–20:1 |
| Attack | 10 ms | 0.1–200 ms |
| Release | 120 ms | 5–2000 ms |
| Make-up | 0 dB | 0–24 dB |
| Detector | Main audio | Main audio / Sidechain |

Main-audio detection measures the main input. Sidechain detection uses only its
separate detector input; a missing or silent source releases compression toward
unity without falling back to the main input. Make-up and saved/linked gains
remain separate from that reduction. Detector level and reduction are visible.

## Ducking

Use **Ducking** to lower one signal when another becomes active:

1. Connect music to **Audio input**, then connect **Audio output** to the music
   destination: Music → Ducking → Output.
2. Connect a microphone's audio output to **Trigger / Sidechain · detector only**.
3. Adjust Threshold while watching trigger level and reduction. Route the
   microphone separately if it should also be audible.

| Control | Default | Range | Purpose |
|---|---:|---:|---|
| Threshold | −30 dBFS | −60–0 dBFS | Trigger level that activates ducking |
| Duck amount | 18 dB | 0–60 dB | Fixed attenuation target while active |
| Attack | 10 ms | 0.1–200 ms | How quickly main audio turns down |
| Release | 250 ms | 5–2000 ms | How quickly main audio recovers |

An active trigger targets `10^(−Duck amount / 20)` gain, independent of how far
the trigger exceeds Threshold. There is no compressor ratio in this model.
The detector measures the larger absolute stereo channel, smooths rises over
5 ms and falls over 50 ms, and activates at Threshold. It deactivates below
Threshold minus a fixed 3 dB hysteresis margin, preventing chatter. A separate
gain envelope approaches the active target with Attack and unity with Release.
These are smoothing time constants rather than fixed delays or lookahead.
Both audible channels share the same gain and preserve their stereo balance.

Missing or disconnected triggers immediately request release toward unity;
connected silent triggers first let the detector decay below the closing
threshold. The main audio is never used as a fallback detector. Zero Duck amount
targets unity; an already attenuated signal returns smoothly. Trigger audio is
never summed into the output, including when the main input is silent. Bypass
returns the main dry signal with the established 10 ms transition. Parameter
edits preserve detector/gain history, and trigger edges use the existing gain
transition so changes do not reset the audible envelope. There is no additional
lookahead, buffering latency, Hold control or adjustable hysteresis.

The node shows **Connect a trigger**, **Below threshold** or **Ducking**, with
trigger level and current reduction. These are passive readouts. The bottom
Bypass button remains distinct from parameters.

Compressor and Ducking each accept one sidechain connection. Combine multiple
detector sources in a Mix first. The diamond socket and lavender cable identify
audio used only for detection; square Linked control sockets carry parameter
control. Audio outputs may fan out to both audible and detector destinations.
Sidechain edges participate in cycle checks and copy/paste. Rewiring preserves
edge type. Ducking can be inserted into an ordinary audio cable, including while
its trigger is connected. Detach requires detector and linked-control cables
to be removed first; a refusal leaves every cable unchanged. Once eligible,
detach reconnects the single main input to its outputs. Ambiguous or invalid
rewires leave the graph unchanged.

## Limiter

**Limiter** is a separate Add/search entry with only **Ceiling** and **Release**
parameters and reduction feedback. Ceiling defaults to **−1 dBFS** and ranges
from **−24 to 0 dBFS**. Release defaults to **120 ms**, ranging from **5 to 2000 ms**.
It reuses the established stereo-linked sample-peak algorithm, including saved
node level and linked-control gain in the protected output. It always measures
its own programme signal; no sidechain can disable its protection.

Peaks receive immediate linked gain reduction and smoothed release. Lowering
Ceiling or enabling limiting protects the first processed sample; this safety
correction takes precedence over a gentle transition if that sample exceeds the
requested ceiling. It adds no lookahead latency. Bypass removes limiting and
fades retained attenuation out over the normal 10 ms transition.

The ceiling applies only at this node while enabled. A later gain node, summed
parallel route, resampler or output control can produce higher peaks. Put the
Limiter after the boosts and sums it should control, and avoid later boosts.
This is **sample-peak limiting with no lookahead**, not oversampled true-peak
limiting. Reconstructed audio can have peaks between stored samples, and heavy
instantaneous reduction can distort transients. No oversampling, true-peak
processing or unrelated latency change is introduced by this redesign.

## Existing compressor compatibility

`dynamicsUiVersion` is a presentation marker. An absent marker reads as `0`,
retaining the existing compressor controls, including Compress/Limit selection.
Mode and detector share one selector row, with no technical compatibility banner.
Newly created compressors use `1` and have no mode selector or
Ducking preset. The marker does not change DSP behavior. Existing internal and
external compression and limiter-mode nodes keep their identity, custom name,
position, cables, bypass, make-up, ceiling, inherited node gain and linked controls.
Settings hidden by the currently selected mode remain saved and effective when
that mode is selected again. A legacy limiter remains a compressor-kind record.

No automatic conversion interprets an external compressor or old Ducking preset
as fixed-depth Ducking: the two attenuation models are not equivalent. Loading
does not migrate or rewrite settings. Copy/paste preserves the marker and hidden
settings. An explicit node reset resets sound controls while retaining identity,
position, connections and the legacy presentation marker. Configuration stays
at version 2; the original-version and rolling last-good backup conventions
remain unchanged. The first later user edit follows the ordinary save path.

## Alignment Delay

Use this node to delay one route relative to another. It has a single exact
stored setting, presented as both **Milliseconds** and **Samples**. Both controls
edit the same value at the mixer's **48 kHz** internal sample rate:

`milliseconds = samples / 48`

The range is **0–96,000 samples**, or **0–2,000 ms**, default zero. One sample is
approximately **0.020833 ms**. The millisecond editor shows three decimal places;
the Samples dial and entry expose the exact integer and reject fractional sample
entries. Millisecond entries are rounded to the nearest
sample, then both fields show that representable value. The time is independent
of a hardware endpoint's negotiated sample rate.

Left and right receive the same delay. A zero setting passes the original
signal without a one-sample offset. Live changes crossfade between read positions
over 10 ms instead of sweeping a fractional read head. The buffer continues
following the input, and ordinary edits retain its contents. Rapid adjustments
keep only the latest requested destination while the current crossfade completes;
they do not allocate or grow a backlog.

**Bypass removes the delay** using the existing effect crossfade. Consequently,
bypassing a nonzero Alignment Delay deliberately changes the route's timing.
Changing delay can briefly combine differently timed copies; it is intended for
alignment, not vibrato, pitch shifting, echo feedback or a tempo delay.

The mixer does not automatically compensate other nodes or devices. For example,
its Denoiser adds 960 samples / 20 ms even in bypass: adding a 960-sample Alignment
Delay to an otherwise direct parallel route can compensate that known processing
delay. USB/HDMI/Bluetooth path delays still require measurement; this setting does
not make independent device clocks synchronous.

## Configuration and real-time boundary

The configuration remains version 2. New optional records have inert defaults
when absent from older node records:

- `expander`: `mode`, `thresholdDb`, `ratio`, `attackMs`, `holdMs`, `releaseMs`,
  `rangeDb`, `hysteresisDb`.
- `alignmentDelay`: integer `samples`.
- `compressor`: adds `mode` (`compress` or `limit`), `detector` (`internal` or
  `external`) and `ceilingDb`; existing compressor fields are preserved.
- New node kinds `limiter` and `ducking` are offered separately; the Limiter
  reuses `compressor.ceilingDb` and `compressor.releaseMs` storage.
- `ducking`: `thresholdDb`, `amountDb`, `attackMs`, `releaseMs`.
- `dynamicsUiVersion`: `0` for legacy compressor presentation (also the missing
  default), `1` for newly created compressors.
- A detector connection uses edge `kind: "sidechain"` and targets a Compressor
  or Ducking node. It remains separate from both audible and linked-control edges.

Native validation rejects malformed modes, non-finite values, out-of-range
settings, fractional sample counts, incompatible endpoints and cycles. Delay
storage is bounded and prepared before a graph enters audio processing. Detector,
gain and delay histories transfer across ordinary parameter edits. There is no
file access, allocation, device opening or waiting inside the processing loop.

## Verification and listening boundary

Focused synthetic checks cover trigger isolation, absent/disconnected/silent
triggers, threshold/depth/timing, stereo linking, zero amount, bypass and live
edits; standalone and legacy limiter ceilings with inherited/linked gain;
legacy internal/external compressors; exact delay; persistence and validation.
Detached preview graphs cover dial precision/bounds/defaults, routed keyboard
events, focus, wheel behavior, copy/reset, insertion, rewiring, cycle rejection,
and stable layouts at supported zoom, DPI and minimum window size.

Use synthetic audio, temporary settings and isolated preview graphs only. Do not
modify the personal live graph or send test audio to physical or virtual
outputs. Test definitions are not evidence that a particular candidate passed;
record each completed run separately. Mathematical measurements come from
in-memory DSP output. Listening quality, physical input behavior, hardware
latency, hotplug and endurance remain separate qualification work.
