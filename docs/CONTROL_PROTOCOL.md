# Local control protocol — wire version 1, configuration version 2

The native engine is `VineMix.Engine.exe`; the WPF controller is
`VineMix.exe`. They communicate using
`\\.\pipe\VineMix.Control.<current-user-SID>`. The engine restricts the pipe
to the current user and SYSTEM and rejects remote clients. No audio crosses this
connection.

Messages are UTF-8 JSON, one object per newline, maximum 256 KiB. One request is
outstanding per connection. Property names are camelCase. Visible UI snapshots
are requested at approximately 30 Hz; hidden/offline polling is slower.

```json
{"protocolVersion":1,"requestId":"unique","command":"snapshot"}
```

Responses carry the same `requestId`, `protocolVersion:1` and `ok`. A successful
snapshot/configure response includes `snapshot`; a failure includes `error`.
An optional `errorCode: "settings_write_failed"` identifies a persistence I/O
failure after configuration validation. The committed configuration and revision
are unchanged. The desktop keeps edits and history queued, shows an unsaved
warning and retries at its existing bounded cadence. Validation failures remain
permanent rejections. Older clients can ignore the additive field.

Commands:

- `snapshot`: full committed configuration, endpoints, applications and telemetry.
- `configure`: full `configuration` plus `expectedRevision`. Validate, save,
  publish the compiled plan, then acknowledge with a new snapshot.
- `retry`: retry assigned workers.
- `recoveryRetry`: reload settings after an external repair. A successful reply
  can still require recovery; inspect `snapshot.settingsState`.
- `restoreBackup`: preserve the original files, commit the validated backup,
  then activate its routing. The backup can predate recent edits.
- `startFresh`: preserve originals, then commit the unassigned three-node starter.
- `shutdown`: explicit engine shutdown, after the response is sent.

A stale revision is rejected. The controller rebases pending id-based edits on a
fresh snapshot rather than overwriting concurrent changes.

Snapshots include `settingsState` (`new`, `loaded`, `backupAvailable`,
`recoveryRequired`, `unsupported`), `backupPath`, `physicalSettingsPath`,
`enginePid`, `executablePath`, `engineVersion` and `engineProtocolVersion`.
Only `new` and `loaded` permit configuration writes and audio activation. A valid
backup is a read-only preview until restoration is chosen. Unsupported settings
require a compatible newer app; they cannot be replaced by the recovery commands.

The desktop verifies the named-pipe server PID/path and performs a read-only
snapshot handshake before its first mutating request. It compares release,
protocol and the physical settings location resolved from an open Windows handle.
Matching printed AppData paths alone does not establish workspace identity.

## Graph configuration

The configuration's `version` is now **2**. `revision` and `startAtLogin` remain
top-level. Existing fixed-console fields are still serialized and required for
shape compatibility/migration; in v2 the graph alone drives audio. Do not edit
legacy A/B controls expecting a graph change.

```json
{
  "nodes": [
    {
      "id":"source-1","kind":"microphone","name":"Desk microphone",
      "x":40,"y":80,"gainDb":0,"muted":false,"collapsed":false,
      "device":{"id":"","stableId":"","name":"","channel":0},
      "app":{"processId":0,"executablePath":"","name":""},
      "bypass":false,"balance":0,
      "eqBands":[
        {"type":"lowShelf","enabled":true,"frequencyHz":80,"gainDb":0,"q":0.7071},
        {"type":"peak","enabled":true,"frequencyHz":250,"gainDb":0,"q":1},
        {"type":"peak","enabled":true,"frequencyHz":1000,"gainDb":0,"q":1},
        {"type":"peak","enabled":true,"frequencyHz":4000,"gainDb":0,"q":1},
        {"type":"highShelf","enabled":true,"frequencyHz":10000,"gainDb":0,"q":0.7071}
      ],
      "compressor":{"thresholdDb":-18,"ratio":4,"attackMs":10,"releaseMs":120,"makeupDb":0}
    }
  ],
  "edges":[],
  "panX":20,"panY":20,"zoom":0.75
}
```

This object is stored under `configuration.graph`, alongside the retained legacy
fields. IDs are stable and independent of names or positions. Node kinds:
`microphone`, `virtualInput`, `appInput`, `mix`, `eq`, `tone`, `highPass`, `lowPass`,
`compressor`, `limiter`, `ducking`, `expander`, `gate`, `alignmentDelay`, `denoiser`, `deEsser`, `plosive`, `reverb`, `voiceColor`, `pan`, `mono`, `output`,
`virtualOutput`, `control`, `channelSwap`, `stereoWidth`, `stereoPan`, `crossfade`,
`stereoCombine`, `stereoSplit`. Legacy `gain` and `reroute` remain supported for
existing graphs and clipboard compatibility, but are absent from the Add menu.

Nodes also serialize the optional effect records below. Missing records use these
defaults when reading older configuration files; reading does not save the file.
Values are finite and validated before plan construction.

Mixing nodes add `stereoWidth` (default 1, range 0–2), `panPosition` (default 0,
range −1–1), `crossfadePosition` (default 0, range 0–1), and `crossfadeCurve`
(default `equalPower`, alternatively `linear`). Pan averages its input to mono
before constant-power positioning. Width scales the side component while
preserving mid. Crossfade bypass selects A. Split and Combine retain their
routing even if a legacy client supplies bypass.

Edges add `fromPort` and `toPort`, both defaulting to `audio` when omitted.
Stereo Split exposes `left` and `right` output ports; Stereo Combine accepts
`left` and `right` input ports; Crossfade accepts `a` and `b` input ports.
Other audio ports remain `audio`. Control cables keep both fields `audio`;
sidechain cables use `toPort: audio` and can select a Split output with
`fromPort`. Port selection participates in duplicate detection and topology.
An extracted Split output is dual mono internally; Combine averages each
incoming frame before routing it to the selected side. Missing inputs are silent.

| Field | Defaults | Ranges |
|---|---|---|
| `denoiser` | `suppressionDb: -18` | −40–0 dB |
| `deEsser` | `frequencyHz: 6000, thresholdDb: -24, reductionDb: 9` | 2000–12000 Hz; −60–0 dB; 0–24 dB |
| `plosive` | `frequencyHz: 150, thresholdDb: -24, reductionDb: 12` | 60–300 Hz; −60–0 dB; 0–24 dB |
| `reverb` | `mix: 0.2, decaySeconds: 1.2, damping: 0.5, preDelayMs: 15` | 0–1; 0.1–5 s; 0–1; 0–100 ms |
| `voiceColor` | `tone: 0, ambience: 0` | −1–1; 0–1 |
| `expander` | `mode: "expander", thresholdDb: -40, ratio: 2, attackMs: 5, holdMs: 50, releaseMs: 150, rangeDb: 60, hysteresisDb: 3` | Mode `expander` / `gate`; −80–0 dB; 1–20; 0.1–200 ms; 0–1000 ms; 5–2000 ms; 0–80 dB; 0–12 dB |
| `alignmentDelay` | `samples: 0` | Integer 0–96000, measured at the internal 48 kHz rate |
| `ducking` | `thresholdDb: -30, amountDb: 18, attackMs: 10, releaseMs: 250, mode: "static", ratio: 4` | −60–0 dBFS; 0–60 dB attenuation; 0.1–200 ms; 5–2000 ms; mode `static` / `dynamic`; ratio 1–20 |

The `gate` kind requires `expander.mode: "gate"`; it shares the Expander settings
record. Older `expander` nodes already in gate mode retain their kind and settings
and use Gate controls. Reset preserves that legacy gate mode.

Voice Color keeps its existing wire fields and ranges. The UI labels
`voiceColor.ambience` as **Character**, from Natural to Broadcast; it coordinates
body/presence EQ, a low-mid cut, parallel compression, envelope-relative density,
subtle delayed doubling and a small room. `tone` retains Warm–Bright travel. Both zero is
transparent neutral. Loading does not rewrite either coordinate or migrate the
configuration format; nonneutral saved coordinates intentionally use the new
processing. See [the Voice Color mapping](VOICE_COLOR.md).

The existing `compressor` record adds optional `mode: "compress"`,
`detector: "internal"`, and `ceilingDb: -1`. Mode accepts `compress` / `limit`,
detector accepts `internal` / `external`, and ceiling spans −24–0 dBFS. The saved
detector field is retained but ignored: Compressor always measures its own audio.
Limit uses its
programme signal and ceiling, independently of the saved detector selection,
threshold, ratio, attack and makeup. Its release setting remains active.
The standalone `limiter` kind always runs this limiter algorithm and reuses
`compressor.ceilingDb` and `compressor.releaseMs` storage, independent of mode.
The `ducking` kind uses its own record. Static mode uses a fixed attenuation
target with stereo-linked detector smoothing of 5 ms rise / 50 ms fall and
3 dB hysteresis. Dynamic mode uses the raw larger absolute stereo trigger channel
and `pow(peak / threshold, 1 / ratio - 1)` above the linear-amplitude threshold,
or unity below it. Both modes share the configured attack/release gain envelope
and retain its current gain when switching; Amount and Ratio both remain saved.

Optional node `dynamicsUiVersion` accepts integer `0` or `1`, defaulting to `0`
when absent. It is presentation metadata: `0` retains the legacy compressor
mode interface, and newly created compressors use `1`. It does not select an
audio algorithm. Legacy compression/limiter mode, hidden settings and saved gain
remain available, but neither interface has a detector selector or trigger input.
Old Compressor sidechain edges are rejected; nodes are not automatically converted
to Ducking. This intentional compatibility change is recorded in the explicit
[IDEA-007 decision](IDEAS_IMPLEMENTED.md#idea-007-static-and-dynamic-modes-in-the-ducking-node).
Reading these defaults never rewrites an old file; normal backup/save behavior
is unchanged. Copy/paste carries the marker and all effect records.

EQ bands additionally serialize `resetFrequencyHz`: 20–20000 Hz when set, or
zero for an older band without a reset anchor. It is editor metadata and does
not alter filter processing. Node `mono` defaults to false. `collapsed` remains
compatible metadata; the current desktop always shows expanded nodes.

Connections:

```json
{"id":"send-1","from":"source-1","to":"mix-1","kind":"audio","gainDb":-6,"muted":false}
```

An audio edge connects a source/processor to a processor/output. A control edge
connects a `control` node to an audio node. `controlMultiplier` defaults to `-1`
for compatibility: this sentinel uses the existing control `gainDb`. An explicit
finite value from 0 to 2 replaces the control's dB offset with a linear amplitude
multiplier; 0 is exact silence, 1 unity, and 2 double amplitude. The control
cable's dB offset still applies. Control bypass removes the multiplier, including
zero; explicit legacy node mute remains authoritative. New desktop controls use
multiplier 1. Muting the control node silences its connected targets.
Muting the control connection disengages that shared control, restoring the
target's local level and mute settings.
A target accepts one control edge. No edge can reference a missing node,
duplicate another connection or introduce feedback. Muted audio edges also
participate in cycle checks.

An edge with `kind: "sidechain"` carries audio only to a Ducking trigger.
It must originate at an audio-producing source or processor and terminate at
`ducking`; at most one sidechain edge targets each Ducking node. It is kept
outside the programme input list, contributes no audible samples, and does not
change the main signal's channel layout. Sidechain dependencies take part in
topological ordering and cycle validation even when muted. The same source may
have distinct audio and sidechain edges to one detector-capable node. Sidechain gain and
mute use the existing bounded edge fields, without becoming audible sends.
Trigger detection with no usable sidechain supplies silence. There is no
automatic main-input fallback. Ducking with a missing/disconnected trigger
releases toward unity. In Static mode, a connected silent trigger lets the
smoothed detector fall below its closing threshold before release, and zero
amount targets unity. In Dynamic mode, a trigger at or below Threshold targets
unity and Ratio controls attenuation. Compressor and Limiter have no sidechain
socket. All trigger dependencies remain subject to cycle validation.

All device endpoints remain explicit. `microphone`/`output` accept physical
devices, `virtualInput` accepts VB-CABLE capture, and `virtualOutput` accepts
VAC Lite render. Input assignments may be repeated across nodes; identical capture
bindings share a worker while each node processes and routes its own copy.
Duplicate output assignments remain rejected; connect branches to the existing
output node to combine them.
An application binding persists its exact executable path and a current PID hint.

The graph is bounded to 32 nodes and 96 edges. Numeric values must be finite.
Node/send gain is -60 to +12 dB. Effect-specific bounds are enforced by the native
validator and mirrored by editor controls. Channel -1 preserves stereo; a
nonnegative microphone channel extracts that channel as dual mono. Layout
coordinates, zoom and labels are also bounded.

## Snapshot and telemetry

The snapshot includes:

- `configuration`: committed version-2 graph and retained migration fields.
- `endpoints`: cached Windows endpoint descriptors (id, stableId, name, direction,
  state, transport, channels, rate, isVirtual).
- `applications`: cached app candidates with processId, executablePath, name,
  hasAudioSession. Discovery does not open capture streams.
- `appCaptureAvailable`: whether the host supports process-loopback activation.
- `status.audioFrames`: advancing mixer frame counter, independent of hardware.
- `status.nodes`: per-node `{id,peak,peakLeft,peakRight,channels,clipped,path}`
  records, plus `spectrumDb`, `spectrumFrame`, `spectrumRevision`,
  `gainReductionDb`, `detectorDb` and `dynamicsFrame`.
- `settingsPath`, `logPath`, `warning`.

A path contains state, message, endpointId, rate, channels, underruns, overruns,
reconnects and queueMs. Device paths also report periodMs (requested shared
period or legacy default), bufferMs (allocated WASAPI buffer), targetQueueMs
and resamplerPendingMs. These are timing diagnostics, not measured round-trip
latency; queueMs excludes pending resampler input and filter history. See
[audio latency](AUDIO_LATENCY.md). Relevant states include Unassigned, Resolving, Opening,
Priming, Running, Waiting, Busy, AccessDenied, ServiceUnavailable, Recovering,
Unsupported and Error. Friendly names do not replace identities. App paths may
wait for an exact executable to return.

The original `status.inputs`, `outputs`, sourcePeaks and A/B peaks remain for
legacy diagnostics. Node telemetry is authoritative. Clipping flags and peaks
are consumed by snapshots; do not treat polling faster as additional audio.

For EQ nodes, `spectrumDb` contains 128 log-frequency bins over 20 Hz–20 kHz,
in the −90–0 dBFS display range, measured from post-EQ stereo output. It is empty
for other kinds and before priming. `spectrumFrame` is the analyzed window's end
frame; `spectrumRevision` is a continuity token that changes across reset or
recreation, including engine restart. A repeated frame is not new audio. These
fields are transient and never saved. See [spectrum details](EQ_SPECTRUM_ANALYZER.md).

Compressor, Limiter, Ducking, Expander, Gate, de-esser and plosive nodes publish actual dynamics state:
`gainReductionDb` is nonnegative attenuation (before compressor makeup),
`detectorDb` is the level used to trigger dynamics in dBFS, and `dynamicsFrame`
identifies the latest processed audio block. Measurements are held between
snapshots and consumed on read. Duplicate frame stamps do not represent fresh
data; the desktop retains the last reading briefly, then clears stalled data.
Muted, bypassed and unavailable paths show no active reduction. De-esser/plosive
reduction describes the shelf plateau, not the overall signal or attenuation at
every frequency. Denoiser/reverb do not invent a scalar reduction measurement.
For Compressor, `detectorDb` describes its own programme peak. For Ducking it
describes the smoothed trigger in Static mode or raw stereo trigger peak in
Dynamic mode. For standalone Limiter and legacy Limit it describes the programme
peak used by the limiter. Alignment Delay does not publish a fabricated
gain-reduction reading.

## Commit and realtime boundary

Graph validation, coefficient calculation, persistence and worker management run
outside realtime processing. The engine publishes a bounded plan at a processing
boundary, retains the old plan while a transition needs it, and retires it off the
audio thread. Unchanged device workers survive settings/layout edits. Graph
changes retain smooth gain state and transition between old/new processing.

Reading version 1 migrates it in memory without modifying the original file.
The first v2 save preserves an immutable `.v1.bak` copy in addition to the normal
rolling backup. A reconnecting controller must request a full snapshot before
editing; it must not infer graph state from old fixed-console controls.
