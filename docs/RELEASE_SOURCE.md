# Beta 1 release source provenance

This tag contains the preserved **0.3.0-beta.1** production source, recovered from
its sanitized development snapshot. It contains all **247** source inputs listed
in the original build receipt: native engine, desktop, installer, scripts,
third-party notices, release metadata and build configuration. No Beta 2 runtime
changes have been backported into this historical source release.

The original Beta 1 package receipts identify the consumed source inventory as:

```text
cbc3c540cc03c58d3ec4209afb84706de94c200d09d74036a9764e945b4aeb7a
```

That is the **historical build-receipt identity**, not a Git commit or a claim
that the reconstructed checkout has the same aggregate byte hash. Repository
line endings and a privacy-only verification assertion differ from the original
working-tree bytes. The latter uses generic example-device wording; it does not
change the shipped audio engine or desktop behavior. Source inventory hashes
also include notices and development scripts.

The downloaded Setup and standalone EXEs are the original verified Beta 1 bytes;
they have not been rebuilt or replaced during release-history publication.
[Release notes](releases/0.3.0-beta.1.md) record their SHA-256 hashes and the checks
performed against those exact files. Source metadata, build timestamps and normal
packaging details mean this record does not promise byte-for-byte reproducible
executables.

Required third-party attribution is retained. Raw diagnostics, private build
receipts, recovery backups and signing material are not part of this source tag.
The product release direction is recorded in [AGENTS.md](../AGENTS.md).
