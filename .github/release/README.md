# Publish a VineMix release

Use PowerShell 7.3+, GitHub CLI with repository write access, and a frozen source
commit. The script publishes one version without replacing earlier releases.
It never installs a package, restarts audio or changes repository visibility.

1. Update `release.json`, build Setup and standalone from the same source, run
   the relevant checks, and prepare signed metadata using
   [`prepare-update-release.ps1`](../../scripts/prepare-update-release.ps1).
   Keep its signing key and raw receipts outside release assets.
2. Put only the two canonical EXEs and `VineMix-<version>-Updates.json` in a
   staging directory. Write release notes with changes, installation guidance
   and actual known limitations. An optional audited exact-source ZIP must be
   named `VineMix-<version>-Source.zip` in that same directory.
3. Push the matching source commit, then validate and publish:

```powershell
./.github/release/Publish-Release.ps1 `
  -AssetDirectory '<release assets>' -NotesPath '<release notes.md>' -ValidateOnly

./.github/release/Publish-Release.ps1 `
  -AssetDirectory '<release assets>' -NotesPath '<release notes.md>' `
  -SourceCommit '<full source commit SHA>' -Publish
```

Pass `-SourceArchivePath '<release assets>/VineMix-<version>-Source.zip'` to both
commands when including that archive. Source ZIPs must be audited for contents
and metadata before publication; the script also rejects unsafe names, build
outputs, credentials and private artifacts. It generates `SHA256SUMS.txt` using
only the explicit asset basenames, never uploads build/preparation receipts and
does not use wildcard uploads.

Validation is offline. It checks executable architecture/version, production
RSA-PSS/SHA-256 signatures, update compatibility, canonical download URLs and
exact package hashes. Publication checks the remote commit's release identity,
refuses an existing version/tag, creates a draft, uploads the validated assets,
downloads and rehashes every asset, then publishes. Beta/alpha/RC versions are
marked as prereleases; stable versions become the latest release. A failure
retains its draft for inspection; it never
overwrites or deletes an earlier release.

For the historical Beta 1 only, use `-Version '0.3.0-beta.1'
-LegacyWithoutUpdater`. That version predates the updater and has two EXEs plus
checksums; newer versions must carry a valid signed manifest. Keep every
published version and its release notes in the Releases history.

Release validation authenticates the packages and their download channel.
Record complete application update/rollback qualification separately; successful
upload or signature verification does not prove that end-to-end flow.
