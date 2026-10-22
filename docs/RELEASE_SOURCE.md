# Release source and provenance

Every published version has its own source tag and retained executable downloads.
The [release history](RELEASES.md) links each version's notes and assets.

## 0.3.0-beta.3

The Beta 3 packages are built from one committed source revision, identified by
their About information and retained build receipts. Its exact-source ZIP
contains the checked-in files plus `SOURCE-MANIFEST.json`, which records the
release commit, exact build-input sizes and SHA-256 hashes, and a sanitized
final-package verification record. The installer,
standalone package and signed update metadata must agree on that input hash.

Release qualification and remaining limits are recorded in the
[Beta 3 notes](releases/0.3.0-beta.3.md). Earlier versions retain their original
tags and downloads; Beta 3 does not replace the provisional Beta 2 files.

## 0.3.0-beta.2

The release source was captured from the same frozen 276-file build input set as
the Setup, standalone EXE and signed update manifest. Its recorded source
snapshot SHA-256 is
`d25f6868862875847085b8106f6aaf2b65678dd0a8ee61e21c0632fc6e0ad12f`.

The release's `VineMix-0.3.0-beta.2-Source.zip` preserves those build-input bytes,
including original line endings, and includes updated documentation and release
tooling. `SOURCE-MANIFEST.json` inside it lists the input scope, per-file sizes
and hashes, and corresponding release commit. GitHub's automatic source archive
uses the committed text normalization; it represents the same source revision
but need not have the same raw-byte inventory hash.

The original binaries were built from a frozen working snapshot before the
release-history commits were made. Their embedded build revision therefore names
the earlier base revision; the source snapshot hash and exact executable hashes
identify the consumed input and delivered bytes. A later rebuild can have
different build metadata and executable hashes. Do not describe this as a claim
of independently established bit-for-bit reproducible binaries.

## 0.3.0-beta.1

The historical version tag retains the sanitized Beta 1 production source.
Its original build receipts recorded
`cbc3c540cc03c58d3ec4209afb84706de94c200d09d74036a9764e945b4aeb7a`.
Newline normalization and a privacy-only verification wording correction mean
that hash is a historical build-receipt identity, not a claim that the restored
source ZIP has an identical aggregate byte hash. Runtime source corresponds to
Beta 1; Beta 2 implementation changes are kept in the newer version.

Source archives contain source, documentation and required licences, with neutral
archive metadata. Signing keys, personal diagnostics, settings, recovery backups,
downloaded tools and compiled application payloads are excluded.
