# Beta 3 maintenance audit

Date: 23 October 2026. Scope: the Windows desktop and engine, workspace identity
and persistence, update recovery, installer/release paths, repository hygiene
and current documentation. This was a source review with isolated verification;
it is not a claim of exhaustive security or hardware qualification.

## Corrected issues

| Area | Problem | Correction and evidence |
| --- | --- | --- |
| Control protocol and saved configuration | JSON numeric conversion accepted fractional protocol versions and could truncate or wrap invalid revision tokens. | Require the documented integer types and nonnegative revisions. Twenty-three malformed requests preserve saved bytes and revision; malformed saved data enters recovery. Unsigned integer boundaries remain exact. |
| Queued edits during save failure | A temporary settings-write failure was treated as an invalid edit and discarded queued work. | Classify persistence I/O separately, retain queued edits and history, show an unsaved warning and retry. Locked staging/primary files and a blocked directory preserve the committed state; the same request succeeds when the obstruction is removed. Invalid configurations remain permanent rejections. |
| Interrupted rollback | A file removed by a failed candidate and restored by an earlier rollback pass was rejected on retry. | Accept only the verified original hash at that path. A regression fails before the fix and passes afterward; a different file remains protected. |
| Completed update cleanup | Failure to remove recovery registration could overwrite a terminal success with a recovery-required state and prevent reopening after rollback. | Preserve terminal state, record a bounded private cleanup receipt and retry housekeeping without repeating install, rollback or launch. Completed, rolled-back and cancelled cases are covered. |
| Elevated updater staging | Windows can assign newly created files to the Administrators group, causing strict staging verification to reject the updater's own files. GitHub's elevated Windows runner reproduced the mismatch. | Create private staging files with explicit current-user ownership and user-only access. Keep strict verification and ordinary installation-file permissions. Require a valid fixture before testing rejection paths. |
| Physical-path verification | The fixture compared a physical path with the logical spelling of the temporary directory. | Compare the opened ancestor's actual identity and missing suffix. A case-alias reproduction failed before the fix and passes afterward. Production settings resolution is unchanged. |
| Engine-identity verification | The fake server flushed a writer during disposal after the client correctly rejected its identity. | Write complete replies explicitly and await server cleanup. Eight matching and mismatched identity scenarios retain the production mutation guard. |
| Release-tool verification | An expected rejection fixture leaked its nonzero process exit code after all assertions passed. | Reset the native exit status only after successful assertions and cleanup. Both the eight fixture checks and the calling process status pass. |

## Removed experiments and repository cleanup

The original resize redraw controller, its tests and added header/root paint
attributes were removed. Native caption and main-window frame defaults match
their prepatch behavior. Diagnostic labs, launch scripts and investigation notes
were removed from active source. No software-rendering, layered-window or
composition workaround is part of this release.

Verified backups and historical packages are retained outside the checkout.
Release tooling from GitHub was reconciled with local changes; earlier version
tags and downloads remain intact. Current documentation has one qualification
entry point and archived evidence links no longer pretend to be local files.
No personal settings, audio-engine processes or repository visibility were
changed. No computer-use tools were used.

## Review coverage and limits

The review traced protocol input through configuration persistence, physical
workspace identity, desktop identity handshakes, update manifest verification,
bounded downloads, payload ownership, guarded rollback, recovery journals and
installer process guards. Existing unrelated Beta 2 features remain present.
Final results and package identities belong in the
[qualification record](BETA_TESTING_CURRENT.md) and the versioned release assets.

The source review and synthetic tests do not establish real-device performance,
clean-machine installation or a complete delivered updater transaction.
Remaining manual cases are listed with the release and are not marked passed.
