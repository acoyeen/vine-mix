# Beta 2 tester fixes and qualification

Target: **0.3.0-beta.2 / 0.3.0 · Beta 2**, Windows 11 x64.
Release: [0.3.0-beta.2](releases/0.3.0-beta.2.md), published 22 October 2026.
Implementation checks were recorded on 21 October. This record describes those
checks and the remaining beta limitations; package hashes and downloads are
retained with the versioned release.

## Changed behavior

- **Microphones:** Physical input accepts Windows recording devices, including
  streamed and virtual microphones. Network capture endpoints are visible without
  enabling advanced output options. Exact endpoint identity and unique active
  stable-identity recovery remain required. Dedicated cable defaults are unchanged.
  See [device visibility](DEVICE_VISIBILITY.md) for evidence and tester steps.
- **Setup:** the app-only route is prominent. Both optional driver boxes begin
  unchecked, including on upgrade. Only selected drivers lead to a separate
  consent page. An existing Virtual Desktop microphone needs neither bundled cable.
- **Resize:** opaque clipped chrome and a coalesced full-client redraw address
  stale header/footer damage after size changes. This is a targeted mitigation;
  physical display confirmation remains pending. See [resize evidence](RESIZE_REDRAW.md).
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
  engine payload match. This is preparation, not publication or a real update.

The first broader native run passed 16 of 17 suites. The maximum legal graph
timing assertion in AdvancedEffects failed during concurrent compilation. That
result is retained in `artifacts/tester-fixes/offline-beta2-first-results.json`;
no timing threshold or test was weakened. The controlled repeat passed **50,026
assertions**; the heaviest processing/adoption p99 remained below the unchanged
5 ms threshold. All 17 suites passed across the initial run and repeat. This
does not establish live deadlines under contention. See
`artifacts/tester-fixes/native-beta2-results.json`.

Resize-test development corrected a fixture assumption: fractional-DPI layout can
legitimately move rounded socket anchors and rebuild cables. The final test
checks actual cable endpoints and cache reuse when anchors do not move, plus
unchanged configuration, camera, history, pending edits and save state. Each
synthetic DPI setup explicitly settles geometry; native monitor transitions
remain unperformed. The shared WPF suite also caught and fixed a read-only
release-notes binding error before packaging.

## Pending acceptance

- Actual Virtual Desktop microphone enumeration, recording, reconnect and saved
  assignment after reopening on the tester's machine.
- Physical continuous resizing, maximise/restore and display-scale changes;
  native tooltip keyboard behavior and an interactive Setup walkthrough.
- Installed upgrade and standalone replacement through the
  delivered updater, rollback/interruption exercises and a real public signed
  release source. Component verification is separate from this complete flow.
- Actual Beta 2 Explorer/shortcut launch and saved-workspace reopen on a tester
  account; prior Beta 1 launch evidence does not qualify these changed binaries.
- Clean Windows accounts for both formats, physical reconnect, sleep/resume and
  an eight-hour normal UI/audio session. Prior partial endurance monitoring was
  interrupted and is not a pass.
- The earlier unexplained closure remains unresolved. A new exit or workspace
  reset blocks beta readiness.

Raw diagnostics and recovery backups remain outside the source project. Only
sanitized summaries belong in `artifacts/tester-fixes`. Fixtures use example
devices and never load the personal workspace.
