# Current beta qualification

**Current qualification:** [0.4.0 Beta](RELEASE_0.4.0_QUALIFICATION.md).
The Beta 5 results below are retained as historical release evidence.

Target: **0.3.0-beta.5 / 0.3.0 · Beta 5**, Windows 11 x64.

Updated 24 October 2026. **Status: published; final source and packages verified.** Beta 5 corrects the
engine metadata responsible for update rollback, improves automatic checking
and refreshes the update window. The numeric Windows file version is
**0.3.0.6**. Earlier version tags and downloads remain intact; Beta 2 remains
provisional. See the [release notes](releases/0.3.0-beta.5.md) and
[confirmed investigation](UPDATE_INVESTIGATION_2026-10-24.md).

The results below cover source commit
`611a4d4c8708709a490cc4976b4ccc169fb9af42` and its actual Beta 5 packages.
The 297 build inputs have snapshot SHA-256
`3430ce20817bfef571578baf1a05f4f72d9289e05cfecf3b3b4df36fe1ce1e79`.

| Gate | Beta 5 status |
| --- | --- |
| Native metadata | The unchanged Beta 2 checker accepts numeric version 0.3.0.6 on the actual installed and standalone-extracted engine. Five compiled PE fixtures cover numeric text, semantic text, wrong numeric text, wrong fixed fields and wrong product identity. |
| Native offline suites | All 17 passed for the final source. |
| Detached desktop | 12,905 checks, 236 renders, zero binding errors. Seven update-state renders reviewed, including high contrast, with 440/500-pixel widths at 96/144/192 DPI. Production desktop build completed without warnings/errors. Update states and release highlights were visually reviewed. |
| Update-core, worker and release preparation | 74 core, 111 worker and nine signing/preparation checks passed, including real Windows metadata checks and an isolated installed transaction that reproduces rejection and rollback. |
| Activation safeguards | 34 activation and 54 interface-only activation checks passed using isolated fixtures. Preparation preserves the personal running app and engine. |
| Documentation images | Existing generic-device images remain illustrative. They are not final Beta 5 screenshots. |
| Installed/Standalone smoke, runtime extraction and isolated Setup | Both actual packages passed inert smoke checks; 18 standalone-runtime and 167 isolated Setup checks passed. Installed and extracted engines are byte-identical, with verified textual and fixed numeric versions. |
| Signature and old-client compatibility | The exact original Beta 2 update core and trust key anonymously discover signed Beta 5, numeric version 0.3.0.6, from the public release feed. Both signed package hashes match the final files, and its unchanged version checker accepts both actual engine payloads. This is compatibility evidence, not a full delivered update. |
| Source archive and publication | Audited source ZIP: 432 entries, comprising 431 committed files and SOURCE-MANIFEST.json; all 297 build inputs match. All five uploaded assets were downloaded from GitHub and rehashed before publication; the pinned-key signature passed. |
| Real-device, clean-machine, sustained-use and delivered update/rollback | Pending. Synthetic checks and package download verification do not establish these outcomes. |

For existing Beta 2, 3 or 4 users, the recommended recovery is to save, use
**Quit VineMix**, and run Beta 5 Setup against the existing installation without
uninstalling. Standalone users should replace the old EXE after quitting.
Settings are preserved. Intermediate versions are unnecessary; a full upgrade
through the older running client remains unqualified. The original Beta 3/4
packages retain the incompatible engine metadata.

Builds use one frozen source commit. The exact-source archive records the
release commit, build-input inventory and hashes, package hashes and sanitized
final verification results. Raw diagnostics, settings, recovery backups and
signing keys stay outside the checkout and distributed assets.

[Beta 5](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.5) was
published on 24 October 2026. GitHub's
[source checks](https://github.com/acoyeen/vine-mix/actions/runs/40084102531)
and independent [published-release checks](https://github.com/acoyeen/vine-mix/actions/runs/40084918736)
passed for the exact release source commit and its assets. Beta 2, 3 and 4's
live release notes now carry update notices and recovery guidance; their
original bodies, asset IDs, sizes, digests and tag objects were preserved.

[Beta 4's qualification record](https://github.com/acoyeen/vine-mix/blob/v0.3.0-beta.4/docs/BETA_TESTING_CURRENT.md),
[Beta 3's record](https://github.com/acoyeen/vine-mix/blob/v0.3.0-beta.3/docs/BETA_TESTING_CURRENT.md)
and the [preceding local candidate](LOCAL_CANDIDATE_2026-10-23.md) describe their
own source snapshots and bytes. Beta 4's original fixtures did not inspect the
engine's textual Windows version and therefore missed the confirmed packaging
defect. Historical passing results retain their original scope.

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
therefore requires Beta 2 as the running client and the genuinely newer signed Beta 5
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
