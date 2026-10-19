# Core dynamics and alignment — implementation checklist

This is a historical checklist for the earlier combined Compressor/Limit update.
Its live-session instructions and completed records do not apply to the later
shared-dial and dedicated Compressor/Limiter/Ducking redesign. Current behavior
is documented in [CORE_DYNAMICS.md](CORE_DYNAMICS.md); current verification uses
synthetic audio and isolated previews as specified in [TESTING.md](../TESTING.md).
Do not use the personal live graph or physical/virtual outputs for redesign tests.

Historical objective: implement, verify, package, activate and test through the
actual interface and running engine, then restore the original workspace.

## Constraints and agreed design

- Preserve old graph sound/layout; old Compressor defaults remain Compress with
  its existing internal detector and parameters.
- Expander / Gate is one node with two modes and standard bypass/reset.
- Compressor gains an external detector socket and a Ducking preset. Sidechain
  edges carry detector audio only, participate in cycle checks, and never enter
  the programme sum. Missing external detector means silence, not fallback.
- Limit is a sample-peak ceiling, with a stereo-linked peak guard, not a ratio
  preset or a true-peak claim. Bypass remains transparent and disengages limiting.
- Alignment Delay stores whole samples at 48 kHz (0–96,000 / 0–2,000 ms), exposes
  both units and crossfades delay changes. No automatic hardware calibration.
- Smooth live changes, preserve processing history, preallocate audio state.
- Candidate package: `artifacts/AudioMixer-CoreDynamics`; do not overwrite the
  active package until offline checks pass and backups exist.
- No Windows default changes, and no test audio sent to physical outputs or
  virtual microphones used by active calls. Use output-disconnected test branches.

## Progress

- [x] Persistent goal created.
- [x] Repository and ancestor instructions checked (no AGENTS.md found).
- [x] Existing graph, model, control protocol and candidate activation inspected.
- [x] Native DSP, graph validation, persistence and state inheritance implemented.
- [x] Desktop controls, sidechain interaction, defaults/reset/copy implemented.
- [x] Focused DSP and engine integration checks pass.
- [x] Existing offline maintenance checks pass.
- [x] UI interaction, persistence and render checks pass; evidence inspected.
- [x] Node library, workspace guide and protocol documentation updated.
- [x] Separate candidate built; executable hashes recorded.
- [x] Original configuration and working package backed up before activation.
- [x] Candidate activated; interface/engine restarted and healthy (rollback on failure).
- [x] Actual interface + running engine: Expander and Gate tested.
- [x] Actual interface + running engine: external-sidechain Ducking tested.
- [x] Actual interface + running engine: Limit ceiling tested.
- [x] Actual interface + running engine: precise Alignment Delay tested.
- [x] Original workspace restored and verified; updated app left running.
- [x] Verification report records running version, evidence and limitations.
- [x] Goal marked complete after required verification and restoration (18 October 2026).

## Evidence and handoff

Native implementation: `/root/core_dynamics_native`.
Desktop implementation: `/root/core_dynamics_ui`.
Independent review and documentation: `/root/core_dynamics_review`.
Primary agent owns build, activation, live UI testing and restoration.

Append decisions, evidence paths and unresolved failures here as work proceeds.

Implementation review fixed abrupt Limit bypass, cable-type isolation during
rewiring, and Limit detector telemetry after inherited gain. Pure Expander uses
its continuous law; Hold/Hysteresis are Gate-only. All old Compressor defaults
retain bit-exact output against the prior algorithm.

Focused native: 80 checks; engine lifecycle: 296 configuration commits with
healthy workers retained. Desktop final: 2,506 checks, 92 renders, no binding
errors (`artifacts/core-dynamics-ui-final`). Four new node closeups inspected.

Initial 12-suite native run passed 11; AdvancedEffects' 30-denoiser timing gate
failed during concurrent desktop verification/build work (p99 10.57 ms).
Its isolated repeat passed all 49,999 assertions, with processing + inheritance
p99 1.40 ms. Original failed evidence is retained. The serialized final maintenance run passed all 12 suites; this is not a claim of hardware latency or stress qualification.

## Final activation and live verification — 18 October 2026

- Running build: `core-dynamics-20261018-202241`, from `artifacts/AudioMixer`.
  Candidate: `artifacts/AudioMixer-CoreDynamics`; hashes match the build manifest.
- Backup: `artifacts/core-dynamics-update-20261018-202252/package-before`, with
  settings backups beside it. Activation passed; rollback was not needed.
- Final native maintenance: 12/12 suites. Exact candidate UI: 2,506 checks,
  92 renders, zero binding errors. Evidence: `artifacts/core-dynamics-verification`.
- Actual UI + production engine: Expander/Gate, Ducking preset and dragged
  detector cable, Limit ceiling/bypass, and both delay unit edits verified.
  45 engine observations; 23 saved live assertions. Enabled Limit stayed below
  -24 dB in 30 observations. Gate reached its 60 dB range; active Ducking reduced
  18.78–22.60 dB. Delay persisted as 594 samples (12.375 ms), then one sample.
- Live settings survived controller reconnection. All effect bypasses were
  exercised. Whole-node reset and paste/default semantics passed offline UI/DSP
  checks; a live Copy was invoked, but live paste/reset is not claimed.
- Only output-disconnected branches were used. No generated test audio, Windows
  default changes or recordings. The original 13-node/11-edge configuration,
  parameters, positions and saved view were restored at revision 4093.
- All original Running paths restored; engine PID 32956 continued through all live
  tests. Updated UI PID 31728 is responsive and left running.
- Live evidence: `artifacts/core-dynamics-verification/live/verification-result.json`,
  `restoration-result.json`, and `ui-interactions.json`.
- Limits: sample-peak (not true-peak), no limiter lookahead, delay quantised to
  48 kHz samples with manual alignment. Subjective listening, complete hardware
  latency and extended soak qualification remain separate from these checks.
