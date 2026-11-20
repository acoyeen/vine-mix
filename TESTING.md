# VineMix testing

Use [the Rust build guide](docs/RUST_BUILD.md) to prepare the pinned compiler,
Windows target and reference sources, then run `scripts/verify-rust.ps1`.
Release candidates require every gate, including parity, Clippy and the
cross-language update serializer checks. Keep the verification report with
the exact source commit and candidate hashes.

On a disposable Windows installation, qualify both the published 0.5 Setup
and standalone distributions against the signed Rust update. Follow the
[delivered update procedure](docs/UPDATE_RELEASES.md#qualify-a-delivered-update)
for discovery, saved-workspace continuity, audio, interrupted handoffs, rollback
and a second launch. Exercise the Rust client's next update as well.
Hardware, installed upgrade and listening checks need separate evidence.

The 0.6.2 worker regression tests exercise Windows file replacement for a read-only standalone launcher and for a launcher held open without delete sharing. The first case checks the signed candidate and the retained original’s Read-only attribute. The second checks the Windows error, the original bytes and successful recovery once the lock is released. On Windows 11, clearing only Read-only on the released 0.6.0 test launcher allowed the signed 0.6.1 in-app update to complete with the saved workspace and preferences intact. A separate run reproduced the failure, cleared Read-only on the original executable and resumed the existing signed transaction successfully without replacing the file manually.

For 0.6.1, all 374 qualifying Rust tests passed, with 22 expected ignores across 30 suites. Setup and standalone each upgraded a 0.6.0 installation manually and passed a second Windows 11 launch with the same physical workspace, saved file hash, preferences and assigned emulated devices. The 276 archived build inputs matched the package receipt. All five GitHub downloads matched the staged files by SHA-256.

The published 0.6.0 Setup client also downloaded and applied the signed update in-app. The worker committed after the candidate confirmed advancing audio frames. The resulting workspace matched the updater's saved checkpoint byte for byte. The standalone client verified its download and reported a launcher replacement failure on two runs. After manual replacement, choosing **Yes** to resume recovery completed the transaction and passed the engine health check. The workspace matched the saved checkpoint byte for byte. See the [release notes](docs/releases/0.6.1.md) for recovery.

For 0.6.0, separate Windows 11 installations exercised both distributions.
The shipped worker completed updates from the post-approval checkpoint,
resumed after interruption and rolled back after a failed health check.
Rollback retained a later workspace edit. Both manual package upgrades
passed. The emulated microphone and speakers ran after upgrading, and a
normal restart retained the same workspace and preferences. Physical-device
and listening checks and live screen-reader testing remain open. See the
[release notes](docs/releases/0.6.0.md) for the 0.5 handoff limitation.

After publication, the unmodified 0.5 Setup and standalone clients each
found 0.6 through GitHub and downloaded the matching signed package while
the original mixer and engine remained running. Both completed an in-app
restart on Windows 11 with assigned emulated devices. The 0.6 health
record confirmed engine frame progress, and each final workspace matched
the old worker's saved checkpoint by SHA-256. Separate attempts exercised
the pre-install cancellation described in the release notes.

The [0.5 qualification guide](docs/RELEASE_0.5.0_QUALIFICATION.md) and
`scripts/verify-offline.ps1` cover the retained C#/C++ reference.

The previous testing document, including the settings-redirection
investigation, is retained as a sanitized historical record in
[the historical testing log](https://github.com/acoyeen/vine-mix/blob/272f0b78a3fb76b9e66a2807244b5a28044725b1/docs/history/TESTING_PRE_BETA1.md). Its older names,
paths and results describe their original source snapshots.
