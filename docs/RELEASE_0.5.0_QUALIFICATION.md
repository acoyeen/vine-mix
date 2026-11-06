# VineMix 0.5.0 Beta qualification

**6 November 2026 — final audit and release preparation in progress.**

The maintainer authorized the complete audit, fixes, documentation refresh and
publication. Identity: `0.5.0-beta`, Windows `0.5.0.0`, display **0.5.0 · Beta**.
The [release notes](releases/0.5.0-beta.md) explain the changes. Earlier local
implementation evidence is retained in [the execution record](IMPLEMENTATION_0.5.0.md).

## Scope

Review the native processing and graph lifecycle, desktop editing and navigation,
cross-language settings boundaries, restart/update identity, packaging, notices,
source provenance and published downloads. Fix demonstrated defects with focused
regressions, then qualify the combined source and actual delivered packages.

All interface verification uses detached controls and screenshots; no
computer-use tools are used. Audio tests use synthetic sources and simulated
devices. Raw reports, settings backups and signing material remain outside the
repository and release assets. Existing workspaces and levels are preserved.

## Evidence

All **19 native offline suites** passed after the audit correction, including
**289,921 graph assertions** and **8,138 qualification assertions**. The real
Release desktop/native configuration boundary passed **802 checks**. Invalid
settings retain their original and backup bytes, and older valid graphs retain
their settings. Shared endpoint routing performs no audio-thread heap operations.

The final quiet 30-Distortion routing-fade test measured **4.8399 ms p99** against
the 5-ms quantum (3.8794-ms median). This passes the measured budget with limited
margin; the separate RNNoise capacity diagnostic below remains a recorded limit.

The Release desktop suite passed **15,354 checks**, produced **328 previews**
and reported **zero binding errors**. The focused documentation run passed
**225 checks / 22 captures**, including normal/minimum windows and 96/192 DPI.
The separate keyboard suite passed **60 checks**. Both verification builds
completed without warnings or errors. Public screenshots use neutral fixtures;
their PNG chunks contain no private text or EXIF metadata.

Release and restart tooling passed **874 checks**: update core 93, worker 319,
signed release preparation 9, native version resources 5, shared activation 40,
UI activation 97, paired activation 229, Setup helper 31 and controller identity
51. The worker's first real-child fixture rejected a redirected default cache;
rerunning from an explicitly resolved physical cache passed without weakening
identity checks. The initial failure remains in private evidence.

The audit corrected channel/process JSON narrowing before range checks
([ISSUE-029](KNOWN_ISSUES.md#issue-029)), updated focused-dial Help and bundled
0.5.0 highlights, and removed stale starter/routing claims from current guides.
Earlier implementation counts are not reused as final qualification.

Final native, package, source-archive and publication results are recorded as
their corresponding checks complete. The source archive's `SOURCE-MANIFEST.json`
records its committed inputs and delivered-package verification; release
checksums and signed metadata identify the exact downloadable bytes.

## Reproduce the offline checks

From Windows 11 x64 with PowerShell 7:

```powershell
pwsh -File .\scripts\verify-offline.ps1
.\.tools\dotnet\dotnet.exe run --project desktop/VineMix.UpdateCore.Tests -c Release
.\.tools\dotnet\dotnet.exe run --project desktop/VineMix.UpdateWorkerTests -c Release
pwsh -File .\scripts\test-update-release-tools.ps1
```

Use new external evidence/build directories for isolated runs. Installer and
standalone checks run against the exact final packages; see
[installer verification](../installer/README.md#verification) and
[release publication](../.github/release/README.md).

## Limits

Detached verification does not establish native pointer, Narrator, IME or VR
controller behaviour. Fresh-machine vendor-driver consent, elevation, reboot,
hardware diversity and sustained audible reliability remain unperformed in this
pass. The real Windows account used for development is not an isolated clean
machine. Component and signed-worker checks do not establish every complete
delivered update, health-check, rollback and interruption transaction on a
separate account.

Distortion-heavy maximum graphs have limited processing margin during routing
fades. A separate 30-RNNoise diagnostic exceeded the 5-ms processing budget;
1- and 4-node cases stayed within it on the test host. See
[ISSUE-030](KNOWN_ISSUES.md#issue-030) for the actual timings and limits. The
32-node cap is routing capacity, not a promise for every effect combination.
Parallel paths have no automatic latency compensation, and the limiter
has no lookahead or true-peak detection. These are beta limitations, not claims
of universal hardware or audible reliability.
