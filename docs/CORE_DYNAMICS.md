# Dynamics and alignment

The Effects library offers separate **Compressor**, **Limiter**, **Ducking**,
**Expander**, **Gate** and **Alignment Delay** nodes. The Add/search library has
24 creatable types and supports 26 types including legacy Gain and Reroute.
Compressor always measures its own audio, with no detector selector or sidechain
input. Ducking owns the external trigger workflow through its **Static ducking**
and **Dynamic ducking** modes. Existing compressor records may retain their
Compress/Limit selector; the saved detector field remains serialized but no
longer changes processing.

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

## Expander and Gate

Use these separate Add/search entries to reduce quiet background sound between
phrases. **Expander** progressively turns down audio below Threshold. **Gate**
switches between an open target and the chosen attenuation floor, with
attack/release smoothing. Both link the two stereo channels, preserving their
relative balance. Neither node has a mode dropdown.

| Control | Applies to | Default | Range | Purpose |
|---|---|---:|---:|---|
| Threshold | Both | −40 dBFS | −80–0 dBFS | Level where the gate opens / expansion stops |
| Ratio | Expander | 2:1 | 1:1–20:1 | Amount of downward expansion |
| Attack | Both | 5 ms | 0.1–200 ms | How quickly attenuation opens |
| Hold | Gate | 50 ms | 0–1000 ms | Keeps Gate open before attenuation returns |
| Release | Both | 150 ms | 5–2000 ms | How quickly attenuation closes |
| Range | Both | 60 dB | 0–80 dB | Maximum attenuation, not an upward gain |
| Hysteresis | Gate | 3 dB | 0–12 dB | Gate closes below Threshold minus this margin |

Set Threshold above the unwanted background level and below normal speech.
The detector and Reduction readouts help show why the node is opening or closing.
Range limits how far quiet sound is reduced; it is not necessarily digital
silence. Range zero disables attenuation. Expander Ratio 1:1 also gives unity.
Expander has five dials: Threshold, Ratio, Range, Attack and Release. Gate has
six: Threshold, Hysteresis, Range, Attack, Hold and Release. Other saved fields
remain retained without appearing as controls. Both use the same two-row layout
and node dimensions. An older `expander` record with `expander.mode: "gate"`
renders Gate controls and keeps its saved kind and settings; loading does not
migrate it into a new `gate` record. Reset keeps that older Gate's gate mode.
A gate cannot remove noise while speech is keeping it open, and fast settings
can chop consonants or produce audible modulation. These processors introduce
no lookahead buffer and cannot anticipate a
transient before it arrives.

The stereo-linked detector has a 0.5 ms attack and 20 ms decay, reducing sensitivity
to individual waveform zero crossings. For Expander, the target attenuation
below Threshold is `(Ratio − 1) × (Threshold − detector level)` in decibels,
capped by Range. Above Threshold it targets unity. Gate targets either unity or
the Range floor, using separate opening/closing thresholds and Hold. The selected
Attack and Release smooth the gain rather than adding a fixed buffer delay.
Continuous parameter targets ramp over 10 ms.

The controls follow established gate/expander terminology; see the original
vendor explanations of [threshold, ratio and range](https://prod.fabfilter.com/help/pro-g/using/dynamiccontrols)
and [attack, release, hold and hysteresis](https://www.fabfilter.com/help/pro-g/using/timecontrols).
The mixer implements its own bounded processor rather than copying a vendor's
gate algorithm.

## Compressor

The Compressor applies the established stereo-linked feed-forward hard-knee
curve to its own main input. Reduction varies with the larger absolute stereo
peak above Threshold according to Ratio. It has no detector selector or
sidechain socket; use Dynamic ducking for compression driven by another signal.

| Control | Default | Range |
|---|---:|---:|
| Threshold | −18 dBFS | −60–0 dBFS |
| Ratio | 4:1 | 1:1–20:1 |
| Attack | 10 ms | 0.1–200 ms |
| Release | 120 ms | 5–2000 ms |
| Make-up | 0 dB | 0–24 dB |

Make-up and saved/linked gains remain separate from compression reduction.
Detector level and reduction are visible. Saved `compressor.detector` metadata
remains in settings and copies, but both `internal` and `external` values now
measure the Compressor's own input.

## Ducking

Use **Ducking** to lower one signal when another becomes active:

1. Connect music to **Audio input**, then connect **Audio output** to the music
   destination: Music → Ducking → Output.
2. Connect a microphone's audio output to **Trigger / Sidechain · detector only**.
3. Adjust Threshold while watching trigger level and reduction. Route the
   microphone separately if it should also be audible.

The **Ducking mode** dropdown offers **Static ducking** (the default) and
**Dynamic ducking**. Threshold, Attack and Release appear in both modes. The
mode-specific dial is Duck amount in Static or Ratio in Dynamic. Switching keeps node
geometry and all sockets fixed, and retains both mode-specific values.

| Control | Applies to | Default | Range | Purpose |
|---|---|---:|---:|---|
| Threshold | Both | −30 dBFS | −60–0 dBFS | Trigger level that activates ducking |
| Duck amount | Static | 18 dB | 0–60 dB | Fixed attenuation target while active |
| Ratio | Dynamic | 4:1 | 1:1–20:1 | Reduction as the trigger exceeds Threshold |
| Attack | Both | 10 ms | 0.1–200 ms | How quickly main audio turns down |
| Release | Both | 250 ms | 5–2000 ms | How quickly main audio recovers |

**Static ducking** lowers the audio by a set amount when the trigger sound
crosses the threshold. An active trigger targets `10^(−Duck amount / 20)` gain,
independent of how far the trigger exceeds Threshold. Its detector measures the
larger absolute stereo channel, smooths rises over 5 ms and falls over 50 ms,
and activates at Threshold. It deactivates below Threshold minus a fixed 3 dB
hysteresis margin. This existing Static behavior is unchanged.

**Dynamic ducking** lowers the audio more as the trigger sound gets louder above
the threshold. It uses the raw larger absolute stereo trigger peak, without
Static's detector smoothing or hysteresis. Above the linear threshold, its gain
target is `pow(peak / threshold, 1 / ratio − 1)`; at or below Threshold it targets
unity. Ratio 1:1 also targets unity.

Both modes share one gain envelope: Attack approaches a lower target and
Release approaches a higher target. These are smoothing time constants rather
than fixed delays or lookahead. Switching modes retains the current gain and
detector history; the gain envelope approaches the new target without a reset.
Static's detector continues updating while Dynamic is selected.
Both audible channels share the gain and preserve their stereo balance.

Missing or disconnected triggers request release toward unity. A connected
silent trigger lets Static's detector decay below its closing threshold;
Dynamic targets unity as soon as its raw peak is at or below Threshold. The main
audio is never used as a fallback detector. Zero Duck amount targets unity in
Static. Trigger audio is never summed into the output, including when the main
input is silent. Bypass returns the main dry signal with the established 10 ms
transition. Parameter edits preserve detector/gain history, and trigger edges
use the existing gain transition. There is no additional lookahead, buffering
latency, Hold control or adjustable hysteresis.

The node shows **Connect a trigger**, **Below threshold** or **Ducking**, with
trigger level and current reduction. These are passive readouts. The bottom
Bypass button remains distinct from parameters.

Ducking accepts one trigger connection. Compressor accepts none. Combine
multiple trigger sources in a Mix first. The diamond socket and lavender cable
identify audio used only for detection; square Linked control sockets carry parameter
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
retaining Compress/Limit selection. Newly created compressors use `1` and have
no mode selector or Ducking preset. Neither presentation has a detector selector
or sidechain socket. A legacy limiter remains a compressor-kind record.

The saved `compressor.detector` field remains serialized but is ignored by
processing; all Compressors use their own main signal. A sidechain edge must now
target Ducking. No automatic conversion turns an old external compressor or
Ducking preset into a Ducking node. Hidden compression/limiting settings remain
saved and become effective when that legacy mode is selected again. Copies
retain the marker and saved metadata. Explicit reset resets sound controls
while retaining identity, position, connections and the presentation marker.
Configuration stays at version 2; ordinary save and backup conventions remain
unchanged.

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
- `gate` is a separate node kind using the same `expander` settings record;
  its `expander.mode` must be `gate`. Older `expander` records may still use
  gate mode without changing their kind.
- `alignmentDelay`: integer `samples`.
- `compressor`: adds `mode` (`compress` or `limit`), `detector` (`internal` or
  `external`) and `ceilingDb`; existing compressor fields are preserved.
- New node kinds `limiter` and `ducking` are offered separately; the Limiter
  reuses `compressor.ceilingDb` and `compressor.releaseMs` storage.
- `ducking`: `thresholdDb`, `amountDb`, `attackMs`, `releaseMs`, plus optional
  `mode` (`static` or `dynamic`, default `static`) and `ratio` (default `4`).
  Both amount and ratio remain saved regardless of the selected mode.
- `dynamicsUiVersion`: `0` for legacy compressor presentation (also the missing
  default), `1` for newly created compressors.
- A trigger connection uses edge `kind: "sidechain"` and targets only a Ducking
  node. It remains separate from both audible and linked-control edges.

Native validation rejects malformed modes, non-finite values, out-of-range
settings, fractional sample counts, incompatible endpoints and cycles. Delay
storage is bounded and prepared before a graph enters audio processing. Detector,
gain and delay histories transfer across ordinary parameter edits. There is no
file access, allocation, device opening or waiting inside the processing loop.

## Verification and listening boundary

Focused verification cases cover Static/Dynamic trigger isolation,
absent/disconnected/silent triggers, threshold/depth/ratio/timing, stereo linking,
zero amount, unity ratio, bypass and live edits; separate Expander/Gate controls;
standalone and legacy limiter ceilings with inherited/linked gain; main-signal
compression; exact delay; persistence and validation.
Detached preview graphs cover dial precision/bounds/defaults, routed keyboard
events, focus, wheel behavior, copy/reset, insertion, rewiring, cycle rejection,
and stable layouts at supported zoom, DPI and minimum window size.

Use synthetic audio, temporary settings and isolated preview graphs only. Do not
modify the personal live graph or send test audio to physical or virtual
outputs. Test definitions are not evidence that a particular candidate passed;
record each completed run separately. Mathematical measurements come from
in-memory DSP output. Listening quality, physical input behavior, hardware
latency, hotplug and endurance remain separate qualification work.
