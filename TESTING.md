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
[the historical testing log](docs/history/TESTING_PRE_BETA1.md). Its older names,
paths and results describe their original source snapshots.
