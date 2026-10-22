# Current beta qualification

Target: **0.3.0-beta.3 / 0.3.0 · Beta 3**, Windows 11 x64.
Updated 23 October 2026. Results below belong to the Beta 3 baseline.
[Earlier beta evidence](BETA_IMPLEMENTATION.md) remains tied to its original files.

| Gate | Beta 3 status |
| --- | --- |
| Native offline suites | Seventeen suites covered. Sixteen passed together; AdvancedEffects exceeded its unchanged timing limit, then passed its isolated repeat (50,026 assertions). Configuration qualification passed 7,959 checks. See timing detail below. |
| Detached desktop | 12,142 checks, 215 renders, zero binding errors; includes 15 autosave regression checks. Publish completed without compiler warnings/errors. |
| Update-core, worker and release-preparation fixtures | 71 core checks, 90 worker checks and eight signing/preparation checks passed. These use ephemeral/private fixtures. |
| Documentation renders | 58 checks, six images at 96/192 DPI, zero binding errors. All images visually reviewed; devices are generic examples. About is an illustrative verification build, not the delivered package identity. |
| Installed/Standalone package smoke and payload audit | Both candidate formats passed with matching candidate-input hashes. Final-byte results and executable identities accompany `SOURCE-MANIFEST.json` in the release source ZIP. |
| Runtime extraction and isolated Setup checks | 17 runtime and 189 Setup checks passed on the candidate. The release process repeats these gates on the committed-source packages. No driver installation or personal engine activation is part of these checks. |
| Signed metadata, source archive and published checksum consistency | Required publication gate: pinned-key signature, matching build receipts, audited exact-source ZIP and every uploaded asset downloaded and rehashed before publication. Final identities are in the release source manifest and `SHA256SUMS.txt`. |
| Real-device, clean-machine, sustained-use and delivered update/rollback exercises | Pending; earlier limited checks do not establish these outcomes. |

Record the exact source identity, executable hashes, result and scope for each
completed gate. Keep unperformed cases pending and publish the remaining beta
limitations with the release. A successful build alone is not qualification.

The final canonical production input set contains 275 files with snapshot SHA-256
`3875412259c7f98f461f24ecc69ee80e7e33d8d71c2eebc711fd29409b567268`.
Local text line endings were normalized to the committed source after candidate
qualification; no runtime logic changed. Final packaging repeats the package
gates using that canonical source and committed-build metadata. The release
source ZIP records the final commit,
input inventory, exact executable identities and final package-check results;
raw diagnostics remain outside source and release assets.

The first AdvancedEffects run measured processing plus state-adoption p99 at
3,592 + 3,596 microseconds, above its five-millisecond block budget while other
verification work was active. The single repeat measured 2,171 + 631 microseconds
and passed the original thresholds. Both reports are retained; no DSP algorithm
or threshold was changed. This synthetic result does not establish sustained
real-device performance.

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
therefore requires Beta 2 as the running client and the genuinely newer signed Beta 3
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
