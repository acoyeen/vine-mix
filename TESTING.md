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

The [0.5 qualification guide](docs/RELEASE_0.5.0_QUALIFICATION.md) and
`scripts/verify-offline.ps1` cover the retained C#/C++ reference.

The previous testing document, including the settings-redirection
investigation, is retained as a sanitized historical record in
[the historical testing log](docs/history/TESTING_PRE_BETA1.md). Its older names,
paths and results describe their original source snapshots.
