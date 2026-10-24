# Beta 2 to Beta 4 update investigation

**Date:** 2026-10-24. **Status:** Confirmed package metadata defect; corrected in Beta 5 source and reproduced with isolated rollback fixtures. Beta 5 release preparation is in progress. The tester's own journal has not been inspected.

## Report and conclusion

A remote tester using the **installed** distribution reported that the automatic
update control appeared ineffective. A manual check offered Beta 4; after downloading,
installing and restarting, the application still displayed Beta 2. Whether a rollback
notice appeared has not yet been established.

**Skipping Beta 3 is supported.** The exact Beta 2 update-discovery source and its
original public trust key successfully discovered and authenticated the published
Beta 4 manifest during this investigation. Neither discovery nor the transaction
requires each intervening beta to be installed.

The automatic-check behavior has confirmed usability and retry limitations in
source. Further inspection of the local installed Beta 4 package established a
separate deterministic failure: the engine's textual Windows `FileVersion` is
`0.3.0-beta.4`, but the old worker requires the numeric value `0.3.0.5`. It rejects
the newly installed engine and restores the previous version before launching
the candidate. This reproduces the reported sequence in an isolated transaction
fixture. The tester's journal would confirm that this was their exact failure;
the package defect itself is confirmed independently.

## Confirmed metadata failure and correction

- All **35 local installed payload files** match the package manifest. The
  installed engine has SHA-256
  `ffa367de0e6bb8fb2d357ff65c37d31f032790e71eec703bb99417cf13744f1c`.
  Its fixed numeric Windows version is correctly `0.3.0.5`, while its textual
  `FileVersion` is `0.3.0-beta.4`. The installed desktop's text and fixed versions
  are both `0.3.0.5`.
- The exact `CheckFileVersion` and path guard from the original Beta 2 tag were
  compiled into a temporary read-only harness. They accept the delivered desktop
  and reject the delivered engine with **“An update executable does not carry
  the expected release file version.”** Neither application was launched.
- [UpdateWorker.cs](../desktop/VineMix.Desktop/UpdateWorker.cs) checks the
  installed engine in `ValidateCandidate` after Setup and before smoke/health.
  Its known postimage permits rollback and prior-version reopening. Standalone
  updates encounter the same defect when checking the extracted engine after
  smoke. Skipping an intermediate version is unrelated.
- The cause is [version.rc.in](../native/src/version.rc.in): its string resource
  used the semantic release version despite a numeric fixed resource. The local
  correction uses `VINEMIX_NUMERIC_VERSION` for `FileVersion`, retaining the
  semantic `ProductVersion` and engine protocol version. The unchanged Beta 2
  checker accepts the actual rebuilt engine. Only resource compilation and
  linking were needed; the running installation was not changed.
- Synthetic PE fixtures now exercise the real Windows version checker inside
  installed worker transactions. Malformed text with correct numeric fixed
  fields produces `RolledBack`, preserves the error, and reopens the previous
  version without reaching smoke. Numeric text succeeds and still requires
  candidate health. **111 worker checks pass.**
- Build, installer and standalone qualification now check actual executable
  version strings as well as fixed numeric fields and product identity. **Five
  native PE metadata regression fixtures and nine preparation checks pass.**
  The old tests accepted engine `--version` output and substituted a no-op for
  the worker's platform version checker, leaving this metadata mismatch uncovered.

Local update storage was inspected through its physical path and contained no
transaction journals, so it supplies no historical installer failure record.
Raw local evidence remains outside the checkout. These findings do not claim a
complete delivered update on the tester's machine or personal local workspace.

## Verified evidence

- **Exact original client code:** A temporary console harness compiled the
  `VineMix.UpdateCore` sources and `updates/release-trust.json` from the existing
  `v0.3.0-beta.2` tag. It performed an anonymous live release check with current
  version `0.3.0-beta.2` and prereleases enabled. The result was **Available**,
  offering `0.3.0-beta.4`. No GitHub credential, downloaded executable, application
  process, audio device or personal workspace was used by this harness.
- **Authenticated candidate metadata:** Beta 4 has Windows file version
  `0.3.0.5`, settings schema `2` and engine protocol `1`. A Windows file revision
  does not have to equal the beta suffix. Its signed Setup SHA-256 was
  `e98f9de3bf61449748da9d69a45b1b17fe1bd93ec9d5554d213ff153b91ed1ca`;
  its signed standalone SHA-256 was
  `e15eaac5eb614ced87f7423b4d5f212e118c20c3bcddffe05bcf91983f55fb8a`.
  Both match the GitHub release asset digests inspected on this date. This verifies
  discovery and metadata agreement, not execution of the delivered update.
- **Release identity:** The published
  [Beta 4 release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.4)
  targets source commit `459c2bb96ecf68f24e27f55dfd970346d2b82bc3`.
  Its source `release.json` declares `0.3.0-beta.4`, `0.3.0.5` and
  `0.3.0 · Beta 4`. The original
  [Beta 2 release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2)
  remains unchanged.
- **Version selection:**
  [GitHubUpdateSource.cs](../desktop/VineMix.UpdateCore/GitHubUpdateSource.cs)
  selects the highest verified compatible semantic version strictly newer than
  the running version. [UpdateTransaction.cs](../desktop/VineMix.Desktop/UpdateTransaction.cs)
  independently enforces that strictly-newer relationship. Neither imposes an
  adjacent-version restriction. UpdateCore, its trust map, schema and protocol
  expectations are unchanged between the original Beta 2 tag and Beta 4.
- **Displayed identity:**
  [ReleaseInfo.cs](../desktop/VineMix.Desktop/ReleaseInfo.cs) reads the label from
  the running assembly's embedded release manifest. A Beta 2 label in the reopened
  application's About window identifies a Beta 2 build; it is not derived from
  the downloaded package filename. The precise place where the tester saw the
  label still needs confirmation.
- **Installed handoff:** The transaction schema and required journal fields are
  compatible between these releases. Installed inventory validation uses the
  transaction's expected release and numeric version; its reader and package
  inventory generator have no intermediate-beta requirement.
- **Regression coverage added:**
  [UpdateCore.Tests/Program.cs](../desktop/VineMix.UpdateCore.Tests/Program.cs)
  now checks Beta 2 discovering Beta 4 with Beta 3 absent, selecting Beta 4 with
  Beta 3 present, and declining older/equal releases when already on Beta 4.
  The isolated suite passed **74 checks**, using synthetic signed releases and
  packages, with no network, personal settings, audio or application processes.

## Automatic-check behavior confirmed in the original release

The original
[Beta 2 update window and scheduler](https://github.com/acoyeen/vine-mix/blob/v0.3.0-beta.2/desktop/VineMix.Desktop/MainWindow.Updates.cs)
and
[preference setter](https://github.com/acoyeen/vine-mix/blob/v0.3.0-beta.2/desktop/VineMix.Desktop/MixerViewModel.Updates.cs)
show that:

- “Automatically check for updates” is a saved checkbox preference. Clicking it
  does not immediately perform a check.
- The scheduled check runs once, 30 seconds after startup, if enabled. There is
  no recurring timer during that running session.
- `last-check.txt` is written **before** the network result is known. A failed
  attempt can therefore suppress another automatic startup check for 24 hours.
- A successful offer produces a workspace message; automatic failures are not
  presented to the user. Manual checking can still succeed independently.

This can explain why automatic and manual behavior differ, especially after a
temporary connectivity or release-availability failure. It does not prove which
condition occurred on the tester's machine. Corrective work should make the
preference's purpose clear and distinguish successful checks from failed attempts.

### Corrections prepared for Beta 5

- Automatic checking now repeats daily while running and retries unsuccessful
  checks after 15 minutes. Only `Available` and `UpToDate` results persist a
  successful-check timestamp. Enabling the preference schedules a prompt check;
  disabling it or quitting cancels background work and suppresses late feedback.
  Automatic checking also initializes when reopening after rollback.
- The update dialog uses the shared theme and a compact hierarchy. It identifies
  the installed version, shows release notes only when present, and displays
  context-appropriate check, download, install and cancel actions. Its automatic
  preference explains that downloads and installation remain explicit choices.
- Failed Setup attempts retain their exit code in the recovery journal. Setup
  writes its detailed log inside the private transaction folder. The original
  Beta 2 worker cannot receive these improvements from a later download before
  it has completed its own update. This is improved diagnosis, not a confirmed
  repair by itself for the tester's installation failure. The native resource
  correction above addresses the confirmed rejection. See [worker logging](UPDATE_WORKER.md).

Verification on 2026-10-24: the production desktop build passed with zero warnings
or errors; updater core passed 74 checks; the initial updater worker suite passed
98 checks, extended to 111 by the metadata/rollback regression above. The
full detached desktop suite passed 12,905 checks, produced 236 renders and reported
zero binding errors. Seven update-state renders were visually inspected, including
high contrast. The update view passed 440/500-pixel widths at 96/144/192 DPI.
These are isolated fixture and rendering results, not a delivered installation
test or native pointer/keyboard test. The first contrast fixture lacked a logical
Window host for resource invalidation; matching the real window ownership fixed
the test without changing the application's palette.

## Why the application may reopen as Beta 2

[UpdateWorker.cs](../desktop/VineMix.Desktop/UpdateWorker.cs) validates the
installed files, runs the delivered candidate's inert package check, and then
requires a matching workspace, engine version/protocol and advancing audio frames.
When a recoverable failure occurs after installation, it can restore the previous
owned application files and reopen the old version. Personal settings are not
rolled back. [App.Updates.cs](../desktop/VineMix.Desktop/App.Updates.cs) supplies
a rollback notice when that restart includes the verified recovery record.

The confirmed version-resource rejection happens before candidate health. Other
failure paths below remain useful context, not competing explanations for the
reproduced metadata defect. The worker is a private copy of the **original running executable**. Updating
from Beta 2 therefore uses Beta 2's worker until the handoff finishes; fixes shipped
in later workers cannot repair that old worker in place. Beta 2 lacks the later
explicit file-ownership creation correction and recovery-cleanup/idempotence
improvements. Those are known differences, not established causes of this report.
The ownership defect normally blocks preparation before shutdown, making it a
weaker match for a completed restart.

Do not weaken signature, file-integrity, workspace-preservation or candidate-health
checks to make the symptom disappear. Determine the failing phase first.

## Evidence needed from the tester

Ask for a small, sanitized report rather than a complete workspace or update folder:

1. **About VineMix → Copy version information** from the reopened application;
   note whether it was reopened automatically or through an existing shortcut.
2. Whether an update-restored/recovery message appeared, its wording, and the
   approximate elapsed time between installation and the return of Beta 2.
3. From the most recent matching transaction beneath
   `%LOCALAPPDATA%\VineMix\updates\<transaction-id>`, the `journal.json` fields
   `phase`, `resumePhase`, `message`, `attempts` and `updatedUtc`; the
   `transaction.json` fields `currentVersion`, `version` and `standalone` can
   confirm the intended installed Beta 2 to Beta 4 handoff. Redact any personal
   paths or account/device information in messages. Do not share transaction
   tokens, full process identities, saved-workspace copies or full settings files.
4. If the journal points to Setup, record any displayed Setup error and whether
   the target was the existing default installation or a custom folder; a full
   personal path is unnecessary. If it points to the inert package check, include
   only the corresponding `smoke-…/results.json` pass/fail, version, mode and
   sanitized error. If it points to health, preserve the folder for a focused
   follow-up instead of deleting it or retrying repeatedly.

## Practical recovery and remaining qualification

A manual upgrade bypasses the confirmed in-app worker metadata rejection, though
the tester's complete installation has not been independently verified:
save the workspace, use **Quit VineMix**, and run the verified Beta 5 Setup against
the existing installation folder. Closing the window alone leaves the app running.
Keep working optional drivers unchanged, then check About after reopening from the
installed shortcut. **Beta 3 is not a prerequisite.** If startup requests recovery
or manual repair, retain the recovery folder and diagnose it before another attempt.
Do not uninstall or replace a newer workspace with a recovery snapshot.

No complete delivered Beta 2 to Beta 4 installation, candidate-health, rollback or
interruption scenario was executed for this investigation. Those remain separate
qualification tasks on an authorized isolated Windows account or machine; see
[the delivered-update procedure](UPDATE_RELEASES.md#qualify-a-delivered-update).
The personal application and audio engine were not started, stopped or replaced.
The investigation itself did not change any release, signing key, existing
version tag or download. Repository visibility was separately authorized.

### Release decision — 24 October 2026

The maintainer authorized publication of the correction. Prepare **Beta 5**
(`0.3.0-beta.5`, numeric `0.3.0.6`) with the metadata, scheduling, diagnostics and
interface corrections. Preserve the original Beta 2/3/4 tags, packages and
checksums; add a prominent update notice and Beta 5 recovery guidance to their
release notes. This keeps existing signatures and source identities meaningful,
preserves the diagnostic evidence, and does not finalize provisional Beta 2.

Final Beta 5 package qualification and publication remain in progress; the
[current qualification record](BETA_TESTING_CURRENT.md) tracks the evidence.
The original Beta 3/4 packages remain affected. Old workers can accept corrected
metadata without an intermediate beta, but that compatibility result alone
does not qualify a full delivered upgrade. Manual Beta 5 Setup after quitting
is the recommended recovery route; standalone users replace the old EXE after
quitting and retain their settings.
