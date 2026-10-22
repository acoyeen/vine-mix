# Beta 1 implementation and qualification history

Historical record for **0.3.0-beta.1**, captured 21 October 2026.
Paths and procedures below identify earlier evidence; they are not current
installation instructions. Raw diagnostics and recovery backups are retained
privately outside this repository. See [current qualification](../BETA_TESTING_CURRENT.md).

Target: **0.3.0-beta.1**, displayed as **0.3.0 · Beta 1**. Updated 21 October 2026.
Implementation and local activation completed for the original Beta 1 candidate.
The checks below describe that candidate only. Beta 1 was subsequently published
as a GitHub prerelease; its recorded limitations and unsigned Authenticode status
remain part of that release history.

## Implemented behavior

- Current product, project, binaries and build targets use VineMix. Intentional
  old identifiers are listed in [name compatibility](../REBRAND_COMPATIBILITY.md).
- Startup normalizes through the Windows shell before touching saved data or
  preparing an engine. Connections verify the actual engine process, release,
  protocol and physical settings location before a first mutating request.
- Recovery has explicit load states, read-only backup inspection and Retry,
  Restore backup and Start fresh actions. Saving and audio remain blocked until
  recovery is resolved. Original primary, backup and interrupted-save artifacts
  are preserved; unresolved Retry retains queued local edits and history.
- Migration uses validated staging, hashes, a journal, writer exclusion and a
  final completion marker. Existing and damaged destination workspaces take
  precedence; stale or conflicting recovery artifacts are not overwritten.
- A fresh workspace has one unassigned physical input wired to virtual and
  listening outputs. Both outputs start muted; listening starts at -24 dB.
  Exactly one eligible VAC destination can be selected automatically. Deliberate
  unassignment survives reopening. Existing layouts and cameras are preserved.
- Header and About identify the beta and exact build. A separate verification
  target contains test/render fixtures; the shipped apps exclude that code.
- Standalone extracts its embedded, hash-verified runtime atomically into a
  content-addressed per-user cache. Installed and standalone copies coordinate
  with supported legacy copies and never automatically take over their engines.
- Beginner instructions, a tester guide, issue form and screenshots use generic
  example hardware. The prior testing log is retained in the historical archive.

## Exact candidate and downloads

Local stage: `artifacts/closed-beta-20261021-184809`.
Only its `Delivery` folder is intended for sharing. Other stage folders retain
sanitized qualification summaries. Original private evidence and settings backups
were relocated outside the checkout during privacy cleanup.

| File in Delivery | Bytes | SHA-256 |
| --- | ---: | --- |
| `VineMix-0.3.0-beta.1-Standalone-x64.exe` | 182,727,709 | `DC86A5373EB8D79269A40F7B92CD47B14A85D2B1FC3EB77B9E0C4EE0AF962CDE` |
| `VineMix-0.3.0-beta.1-Setup-x64.exe` | 102,790,362 | `6B81D71420EC59AB9FDD837F681380C2041E3661B1F387AC1BA46297F73AF96E` |

`SHA256SUMS.txt` accompanies both files. Both packages share source snapshot
`cbc3c540cc03c58d3ec4209afb84706de94c200d09d74036a9764e945b4aeb7a`.
Git HEAD was `f8b3533c396f2f0e50f2f4d8734ebb483a8c1aef` with uncommitted changes;
the snapshot hash, rather than HEAD alone, identifies the consumed source.

The native engine hash in both formats is
`49FCE3820C9B92C9B0D9ED68380DA593712F58919ED02AEBD0163F51617A6F17`.
VineMix and its helper/Setup are unsigned. Vendor driver signatures are separate
and do not establish VineMix's publisher. VB-Audio integration permission was
confirmed by the maintainer on 21 October; the original packages and separate terms are
preserved. VAC Lite's private-home/non-income restriction remains documented.

## Passed checks

| Area | Evidence and scope |
| --- | --- |
| Native offline | All 17 suites passed. The final position-only adjustment was followed by passing graph, lifecycle and qualification checks: 278,760 graph assertions and 7,922 qualification assertions. Synthetic workers never opened real devices. |
| Desktop | Final-source detached suite: **10,860 checks**, **215 renders**, **zero binding errors**. Includes recovery, interrupted/conflicting migration, private identity handshakes, assignment persistence, history, restart and cancellation regressions. |
| Documentation | **58 checks**, six inspected images at 96/192 DPI, zero binding errors. No personal devices, live engine, personal settings or clipboard access. |
| Runtime | **17 checks** using the actual final embedded payload: extraction, reuse, corruption, locking, path validation and concurrency. Both delivered EXEs passed their own inert release smoke checks. |
| Installer | **175 checks** against final Setup: isolated install/reinstall, downgrade refusal, running-target protection, exact payload, uninstall preservation and actual installed release smoke. Drivers and normal app startup were disabled for these isolated checks. Helper policy checks also passed. |
| Live activation | **68 scripted checks**: existing launch fix, genuine installation, explicit active-workspace migration, both legacy/current launch orders, installed/standalone switching, an intentional engine-exit/recovery test, real Explorer/Start menu and Codex-origin launches from a different working directory. No computer-use tools were used. |

The live migration explicitly selected the physically verified workspace loaded
by the running engine, rather than choosing by timestamps. Both legacy stores
were backed up and retained. The existing graph, assignments, effects, mute state and camera survived; saved workspace bytes did
not change through migration, engine recovery or distribution switching.

There is now a genuine per-user installation at
`%LOCALAPPDATA%\Programs\VineMix`; its active data is
`%LOCALAPPDATA%\VineMix`. The existing pinned shortcut was backed up and its
target updated to this installation. The installer-created Start menu shortcut
was also exercised. Live shutdown tests stopped the idle controller and used
the engine's graceful shutdown protocol; the tray's Quit gesture was not driven
through UI automation.

Local privacy-sanitized evidence summaries (not public download links; original
diagnostics and personal workspace backups are retained outside the project):

- `artifacts/beta-native-complete-20261021-1827/results.json`
- `artifacts/beta-native-layout-final-20261021-182906/results.json`
- `artifacts/beta-preparation/full-ui-exact/results.json`
- `artifacts/beta-preparation/beta-docs-4/results.json`
- Final stage: `runtime-verification`, `installer-verification` and
  `live-qualification` summaries. Original smoke reports and build receipts are
  retained with the private evidence outside the source checkout.

## Measured cleanup and startup observations

| Comparable package category | Before | After |
| --- | ---: | ---: |
| Local core application package | 244,332,334 bytes / 659 files | 186,852,894 bytes / 28 files |
| Full installer payload, including setup support | 263,096,920 bytes / 64 files | 260,476,254 bytes / 36 files |

The core-package reduction is about 23.5%; the installer payload reduction is
about 1.0%. These are separate categories. Historical documents, accumulated
images and unused Inter fonts no longer enter the release packages; all required
notices remain. The standalone download is about 174.26 MiB; Setup is 98.03 MiB.
WPF trimming was not enabled.

Single local observations from shortcut launch to a responsive window were
1.462/1.637 seconds for the old first/warm launches, 1.654/1.581 seconds for the
installed beta first/warm launches, and 1.665 seconds for standalone. These are
not controlled OS-cold benchmarks and do not establish a speed improvement.
Initial UI working sets were broadly similar (about 150–154 MiB warm for old and
new installed copies). They are launch-time samples, not a long-term UI memory
qualification. Exact bytes and timings are in the private live report.

## Remaining qualification gates

- **Interrupted; eight-hour gate pending:** the native-engine monitoring interval
  that began at 20:35 UTC+10 on 21 October stopped at 21:47 when the desktop was
  opened. Monitoring deliberately yielded the control connection. The partial
  interval lasted 4,315.464 seconds (about 72 minutes), with 429 samples, advancing
  audio frames, unchanged workspace bytes and a maximum sample gap of 10.034
  seconds. No error was reported. This is not an eight-hour pass. The engine
  remained running when the result was checked; opening and closing the desktop
  does not automatically restart this monitor. Its completed hourly follow-up
  was removed. A fresh uninterrupted eight-hour interval is still required.
  The earlier interval that began at 19:08 was interrupted when private diagnostic
  logging was relocated outside the project. It is not an eight-hour pass.
  The audio engine and workspace remained unchanged during that relocation.
  The native-only procedure observes frame advancement, path availability,
  process identity, memory and unchanged workspace bytes. It does not record audio
  or change routes. The desktop stays closed during monitoring to release the
  single control connection; opening it yields monitoring to the user.
- **Pending:** a clean Windows 11 x64 exercise for each format. Hyper-V access
  was denied in this environment; the isolated installer test is not a substitute.
- **Pending:** physical unplug/reconnect, Windows sleep/resume and interactive
  vendor-driver installation/cancellation/restart checks. Existing drivers were
  deliberately preserved during actual installation (`DRIVERS=none`).
- **Pending:** an eight-hour interactive desktop/UI memory session; the native
  engine procedure has a narrower scope.
- **Unresolved:** the earlier unexplained application closure was not reproduced
  or explained by available crash records. Recorded errors concerned older test
  executables. Deliberate shutdowns and the injected engine exit in qualification
  are identified as tests. Acceptance of the earlier uncertainty as a known beta
  limitation remains required before distribution if it cannot be diagnosed.

Any new unexplained exit or workspace reset blocks readiness. No pending gate is
reported as passed, and no release publication or distribution is authorized by
this record alone.

The privacy cleanup changed documentation, historical evidence handling and one
verification-only fixture assertion. Both delivered application EXEs retain the
exact qualified bytes and hashes above. The installation bundle's README and
checksums were regenerated with neutral maintainer wording. See
[privacy cleanup](../PRIVACY_CLEANUP.md) for its audit scope and historical-cache limits.
