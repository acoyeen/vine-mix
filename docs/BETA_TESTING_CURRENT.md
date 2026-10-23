# Current beta qualification

Target: **0.3.0-beta.4 / 0.3.0 · Beta 4**, Windows 11 x64.
Updated 23 October 2026. These results belong to the Beta 4 source review and
isolated verification. [Beta 3's record](https://github.com/acoyeen/vine-mix/blob/v0.3.0-beta.3/docs/BETA_TESTING_CURRENT.md)
and the [preceding local candidate](LOCAL_CANDIDATE_2026-10-23.md) describe their
own source snapshots and executable bytes.

| Gate | Beta 4 status |
| --- | --- |
| Native offline suites | All 17 passed in the full run, including engine lifecycle, endpoint selection, shared/exclusive output, timing and effects. No failed timing repeat or relaxed threshold was needed. |
| Detached desktop | 12,826 checks, 229 renders, zero binding errors. Includes layout lock and feedback, failed quit saves and recovery copies, release highlights, footer timers and device selection. Publish completed without compiler warnings/errors. |
| Update-core, worker and release preparation | 71 core, 97 worker and eight signing/preparation checks passed using isolated fixtures. |
| Activation safeguards | 34 activation and 54 interface-only activation checks passed using temporary files, synthetic state and an inert shell child. No personal app or engine was stopped, replaced or launched. |
| Documentation images | Existing generic-device images remain illustrative. They were not regenerated as Beta 4 screenshots. Detached render inspection is recorded in the audit. |
| Installed/Standalone smoke, runtime extraction and isolated Setup | Both final package smoke checks passed; all 17 standalone runtime checks and 189 isolated Setup checks passed. The two packages contain identical engine bytes. Exact executable identities and final results are retained in the source ZIP's SOURCE-MANIFEST.json. |
| Signature, source archive and published downloads | Passed: pinned-key signature, matching build receipts, audited exact-source ZIP and all five uploaded assets downloaded and rehashed before publication. The published tag and GitHub asset digests also match. Final identities are in SOURCE-MANIFEST.json and SHA256SUMS.txt. |
| Real-device, clean-machine, sustained-use and delivered update/rollback | Pending. Synthetic checks and package download verification do not establish these outcomes. |

The release review is recorded in [the Beta 4 audit](BETA4_AUDIT.md). Builds use
one frozen source commit and preserve all earlier version tags and downloads.
The numeric file version is **0.3.0.5**, above the earlier local candidate's
0.3.0.4. The exact-source archive records the release commit, build-input
inventory and hashes, package hashes and sanitized final verification results.
Raw diagnostics, settings, recovery backups and signing keys stay outside the
checkout and distributed assets.

Beta 4 was published on 23 October 2026 from source commit
`459c2bb96ecf68f24e27f55dfd970346d2b82bc3`. The 292 build inputs have snapshot
SHA-256 `0b1df54f340b56bc3ff028d5efd426deaa8cd6daac63838e56ff92a5a89394db`.
An independent archive audit confirmed all 423 committed files and all build
inputs match byte for byte, with neutral archive metadata and no private data.
The final desktop and native packages were not activated over the running app.
GitHub's [source checks](https://github.com/acoyeen/vine-mix/actions/runs/39962545477)
and independent [published-release checks](https://github.com/acoyeen/vine-mix/actions/runs/39964789273)
both passed for this exact release commit and its assets.

The historical unexpected-closure report remains unresolved. Actual streamed
headset playback/reconnect, physical display behavior, sleep/resume, eight-hour
real-audio use, clean-machine installation and complete delivered-app update,
rollback and interruption exercises remain unqualified. Publication does not
establish that these pending checks passed.

## Automated checks

Run PowerShell 7 from the repository root:

```powershell
./scripts/verify-offline.ps1
```

The suite builds and runs the native offline programs and a separate
`VineMix.Verification.exe`. It uses synthetic audio, temporary configuration
fixtures, private fake pipes and detached controls. It does not start the
personal engine, open real audio devices or change personal preferences.

The full desktop renderer covers editing, history, routing, effects, keyboard
access, caption behavior, help, high contrast, display scales, recovery,
migration and identity handshakes. Verification code and preview fixtures are
excluded from both release applications.

For a native-only run use `-NativeOnly`. Use a new `-ResultDirectory` to retain
every report; never overwrite earlier evidence. The current suites include:

- Graph and settings serialization, legacy migration and starter behavior.
- Synthetic engine lifecycle, publication and worker ownership.
- Recovery states, archive preservation, failed writes and unsupported schemas.
- Queue concurrency, drift, resampling and timing.
- Signal-based effects and processor regressions.
- Native Windows integration tested with fake endpoints, without real streams.

Update checks also have independent console fixtures:

```powershell
.\.tools\dotnet\dotnet.exe run --project desktop/VineMix.UpdateCore.Tests -c Release
.\.tools\dotnet\dotnet.exe run --project desktop/VineMix.UpdateWorkerTests -c Release
```

The first simulates GitHub, signatures and downloads; the second exercises the
real transaction/file code with an injected process/Setup platform. Neither
fixture proves an actual public-release update. Release preparation uses
[the signed update guide](UPDATE_RELEASES.md).

## Documentation screenshots

Use the **verification executable**, never the release app, with:

```text
--render-beta-docs --verification-dir <new-output-directory>
```

It renders real controls with inert three-node fixtures and generic hardware.
Inspect every image at documentation size and both tested display scales.
Require legible labels, a positive gap between output bodies, visible sockets
and cables, no clipped content and no personal hardware, identifiers or paths.

## Final-package checks

Build Installed and Standalone from one frozen source snapshot using
`scripts/build.ps1`; the build receipts identify the source and exact bytes.
Build Setup from that Installed receipt using `scripts/build-installer.ps1`.
Both packages include .NET and the same native engine payload.

Run `--release-smoke <new-evidence-directory>` on each actual delivered EXE.
The standalone smoke extracts only into its evidence directory. Neither smoke
starts audio, accesses personal settings or shows a window.

Run `scripts/verify-standalone-runtime.ps1` against the final standalone artifact
for archive hashes, cache reuse, corruption, locking and concurrent extraction.
Run `scripts/verify-installer.ps1` against the final Setup and staged installer
payload. It uses an isolated installation identity and disables driver installation
and normal app startup. It checks setup refusal/upgrade/downgrade/uninstall,
preserved unknown files and protected processes, settings, startup entries and
shortcuts. This is useful installer evidence, not a clean-machine exercise.

Record Authenticode status and SHA-256 for the exact files being supplied. Check
versions and notices, verify that both build receipts have the same source
snapshot and engine hash, and confirm the standalone folder contains one EXE.

## Live qualification

Preserve the current personal engine and settings while preparing these tests.
Perform personal activation only after an agreed audio interruption. Launch the
candidate using Explorer or its shortcut; never start the personal engine from
the redirected tool process. Resolve physical settings locations from open file
handles and preserve both old settings views before any recovery.

Required scenarios:

1. New user: three-node starter, physical devices unassigned, outputs muted;
   zero/one/multiple eligible VAC endpoints and deliberate cleared assignments.
2. Existing user: migrate workspace/preferences, preserving routing, effects,
   positions, camera and device identities. Conflicts require an explicit choice.
3. Full Quit and reopen through a real shortcut, tool-origin launch and a
   different working directory. Compare the entire workspace and physical store.
4. Installed/standalone and legacy/current launches in both orders. One active
   session; switching never automatically terminates the other session.
5. Valid backup, unreadable/missing established settings, interrupted save and
   interrupted migration. No automatic audio or configuration commit in recovery.
6. Real-device routing, deliberate output unmute, mute behavior, endpoint
   unplug/reconnect and Windows sleep/resume.
7. At least eight hours of sustained use on the candidate, with no unexplained
   exit, lost workspace or stalled processing.
8. Both distribution formats on a clean Windows 11 x64 machine/account without
   an installed .NET runtime; exercise missing/present drivers and installer
   cancellation/restart paths.

Keep physical audio levels conservative and select devices deliberately. Missing
hardware or a clean environment leaves the affected check **pending**, not passed.
Any reset or unexplained exit during qualification blocks readiness. If the earlier
closure cannot be reproduced or diagnosed, the maintainer must accept that uncertainty as
a known limitation before distribution. Publish versioned beta releases with these
remaining checks stated accurately; repository visibility is a separate maintainer
decision. Publication does not establish that the pending checks passed.

## Delivered updater qualification prerequisites

Beta 2 is the first delivered build with an updater. Beta 1 must be upgraded
manually, and Beta 2 deliberately refuses to update to itself. A complete test
therefore requires Beta 2 as the running client and the genuinely newer signed Beta 4
release, with matching source and both distribution assets. Changing a version
string or bypassing the newer-version check in a fixture does not qualify the
delivered application. See [the release test procedure](UPDATE_RELEASES.md#qualify-a-delivered-update).

A custom `--settings` file isolates workspace data, but does not isolate the
per-user updater directory, uninstall registration, session ownership or control
pipe. Use an authorized separate Windows test account or machine for the full
transaction. A suitable separate environment must be established before this exercise.
The previous host feasibility review did not establish one; that earlier
limitation is not a result for a new test environment.

## Historical evidence

[The preserved pre-beta testing log](history/TESTING_PRE_BETA1.md) contains earlier
research, manual procedures and results, including the settings-redirection
investigation. It retains the names and paths used at the time; they are not
current commands or proof that a new beta artifact passed qualification.
