# Beta 2 implementation and qualification history

Historical record for **0.3.0-beta.2**, Windows 11 x64, captured 21–22 October 2026.
These results identify the original Beta 2 release files and their limited test
scope. They do not qualify Beta 3. See [current qualification](BETA_TESTING_CURRENT.md)
and the [published Beta 2 notes](releases/0.3.0-beta.2.md).

## Changed behavior

- **Microphones:** Physical input accepts Windows recording devices, including
  streamed and virtual microphones. Network capture endpoints are visible without
  enabling advanced output options. Exact endpoint identity and unique active
  stable-identity recovery remain required. Dedicated cable defaults are unchanged.
  See [device visibility](DEVICE_VISIBILITY.md) for evidence and tester steps.
- **Setup:** the app-only route is prominent. Both optional driver boxes begin
  unchecked, including on upgrade. Only selected drivers lead to a separate
  consent page. An existing Virtual Desktop microphone needs neither bundled cable.
- **Add Node:** all creation choices and categories have purpose help, including
  filtered results, disabled choices and accessible descriptions. Detached routed
  help checks do not establish native popup focus or screen-reader behavior.
  Hit testing now skips hidden results so a collapsed row cannot intercept help
  for the visible filtered choice.
- **Updates:** Settings and About link to a public GitHub release checker.
  Checking, downloading and installing are separate actions. Optional daily checks
  never install silently. Signed release information binds the version, format,
  compatibility, exact artifact URLs, sizes and SHA-256 hashes.

Update installation first confirms saved edits and the physical settings file,
then cooperatively stops the verified engine. A private copied worker backs up
owned application files, runs Setup without drivers or replaces the standalone
launcher, and reopens the saved workspace. Candidate editing stays disabled
until workspace identity, protocol and advancing engine frames are confirmed.
No updater path kills a process or restores old settings over current data.
After a safe rollback, the verified previous version reopens with a failure notice.
Interrupted or ambiguous installation can require manual repair; see
[worker recovery limits](UPDATE_WORKER.md) and [release preparation](UPDATE_RELEASES.md).

## Evidence

Passed isolated checks:

- Native Windows catalog policy: 65 checks; graph: 278,760 assertions; engine
  lifecycle includes a synthetic streamed microphone. No real headset was opened.
- Update discovery/signatures/downloads: 71 injected HTTP and cryptographic checks.
- Update transaction/recovery worker: 66 checks, including exit gates, preserved
  newer files, interrupted handoffs, rollback reopening and private staging.
- Full detached WPF suite: **12,188 checks, 215 renders, zero binding errors**;
  final compilation had zero warnings or errors. All existing suites remain.
- Documentation: **58 checks**, six inspected images with generic devices and no
  text/EXIF metadata. These are inert renders of real controls.
- Signing/preparation tooling: eight synthetic checks, with zero build warnings.
- Final delivered Installed/Standalone EXEs: both inert smoke checks passed.
  The standalone runtime passed 17 integrity/extraction checks. Exact Setup
  passed **189 isolated installation/reinstall/downgrade/uninstall checks**;
  drivers and normal app startup were disabled. Helper policy passed 31 checks.
- Production-key update metadata was signed locally and independently verified
  through the actual updater parser. Both asset hashes and the common native
  engine payload match. This validates preparation, not a real update transaction.

The first broader native run passed 16 of 17 suites. The maximum legal graph
timing assertion in AdvancedEffects failed during concurrent compilation. That
result is retained privately as `offline-beta2-first-results.json`;
no timing threshold or test was weakened. The controlled repeat passed **50,026
assertions**; the heaviest processing/adoption p99 remained below the unchanged
5 ms threshold. All 17 suites passed across the initial run and repeat. This
does not establish live deadlines under contention. See
the privately retained `native-beta2-results.json`.

The shared WPF suite caught and fixed a read-only release-notes binding error
before packaging.

The anonymous GitHub releases endpoint returned **HTTP 404** when checked on
21 October. The checker treats that as unavailable, rather than claiming the app
is up to date. This observation limits online qualification; it does not block
preparing or publishing versioned releases. Evidence:
the privately retained `public-update-channel.json`.

## Exact candidate files

The [Beta 2 release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2)
preserves both EXEs, signed update metadata, checksums and matching source.
Local preparation receipts and raw diagnostics remain in the private archive.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `VineMix-0.3.0-beta.2-Standalone-x64.exe` | 182,909,594 | `803b263c3e3be1a5c6cc67e7519a985c0d0ab9bdabab7760a6b025781e9a7d15` |
| `VineMix-0.3.0-beta.2-Setup-x64.exe` | 102,849,481 | `874fdd8d6c0e24ac6a3e38616077b6edc1469121a61e21b436baca2c259a11af` |

Captured source snapshot:
`d25f6868862875847085b8106f6aaf2b65678dd0a8ee61e21c0632fc6e0ad12f`.
Git HEAD: `e4296face15790d0215c7434a746f601063a5596`, with uncommitted work
preserved. Raw receipts, superseded candidates and diagnostics remain outside
the checkout. The executables are **not Authenticode signed**. Update metadata
uses project RSA-PSS/SHA-256 key `vinemix-updates-2026-01`; its private key stays
outside the project and delivery files.

Beta 2 is an MIT-licensed open-source beta. Its historical About wording does
not alter the licence or the versioned release record. Later builds use the
current product wording; the original release bytes remain preserved.

Compared with Beta 1, standalone grew by 181,885 bytes (about 0.10%) and Setup
by 59,119 bytes (about 0.06%). Installed core is 187,030,171 bytes/28 files;
the full installer payload is 260,655,844 bytes/36 files; extracted native runtime
is 12,308,121 bytes. No updater dependency package, WPF trimming or compression
change was introduced. Beta 2 startup time and idle memory were not measured
because this work preserved the running personal session; inert smoke is not a
startup benchmark. Source/input allowlists, screenshot metadata and final
delivery privacy checks passed with their scopes recorded in the local summaries.

## Pending acceptance

The 22 October read-only feasibility review confirmed two prerequisites for the
complete delivered updater test. Beta 2 is the first updater-enabled client and
requires a strictly newer signed offer, so neither a Beta 1-to-Beta 2 button flow
nor a Beta 2-to-itself update is a valid test. No usable separate Windows test
environment is established here: guest management is access-denied and Sandbox
is absent. Limited host queries do not establish whether other guests exist.
Custom settings alone do not isolate the user's updater directory, installation
registration or session coordination. The next legitimate signed release and an
authorized isolated Windows environment are required; see
[the delivered-update procedure](UPDATE_RELEASES.md#qualify-a-delivered-update).
No source, delivered application bytes, settings or registry data changed during
that review.

- Actual Virtual Desktop microphone enumeration, recording, reconnect and saved
  assignment after reopening on the tester's machine.
- Physical display-scale checks, native tooltip keyboard behavior and an
  interactive Setup walkthrough.
- Installed upgrade and standalone replacement through the
  delivered updater, rollback/interruption exercises and a real public signed
  release source. Release publication is handled separately; repository visibility
  is not a reason to leave the prerelease unpublished.
- Actual Beta 2 Explorer/shortcut launch and saved-workspace reopen on a tester
  account; prior Beta 1 launch evidence does not qualify these changed binaries.
- Clean Windows accounts for both formats, physical reconnect, sleep/resume and
  an eight-hour normal UI/audio session. Prior partial endurance monitoring was
  interrupted and is not a pass.
- The earlier unexplained closure remains unresolved. A new exit or workspace
  reset blocks beta readiness.

Raw diagnostics, preparation receipts and recovery backups remain outside the
source project. This document preserves the sanitized results. Fixtures use
example devices and never load the personal workspace.
