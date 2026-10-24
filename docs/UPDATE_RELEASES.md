# Preparing signed update releases

The updater reads GitHub releases from `acoyeen/vine-mix`, using tags `v<version>`.
Publish normal versioned prereleases, notes, checksums, matching source and signed
update metadata. Prepare that complete release path before a repository visibility
change; visibility is a separate maintainer decision. The updater uses anonymous
requests and never embeds a repository credential. Verify anonymous discovery when
the release service is accessible. This preparation tool itself does not upload,
change visibility, install a candidate or restart the application/audio engine.

Build Setup and standalone candidates from the same stable source snapshot and
the authoritative `release.json`. Keep the existing installer `.exe.json` receipt
and standalone directory `.build.json` receipt. Write concise UTF-8 release notes.

The native engine's textual Windows `FileVersion` must be the four-part
`numericVersion`, matching its fixed resource fields; `ProductVersion` retains
the semantic release version. Already-shipped update workers parse that text.
Build and qualification scripts enforce this contract on actual binaries through
`assert-release-file-version.ps1`; regression fixtures run with
`test-release-file-version.ps1`. A valid manifest/signature or matching `--version`
output alone does not establish updater compatibility. See the
[Beta 4 metadata defect](UPDATE_INVESTIGATION_2026-10-24.md).

Use PowerShell 7.3 or later:

```powershell
./scripts/prepare-update-release.ps1 `
  -InstallerPath '<candidate>/VineMix-<version>-Setup-x64.exe' `
  -StandalonePath '<standalone>/VineMix-<version>-Standalone-x64.exe' `
  -PrivateKeyPath '<private signing directory>/updates-key.pem' `
  -NotesPath '<private preparation directory>/release-notes.txt' `
  -OutputDirectory '<new release staging directory>'
```

The key must remain outside the checkout and output directory. It must match
exactly one RSA public key in `updates/release-trust.json`. Keep private signing
material in private storage owned by and accessible only to the maintainer. The tool
requires RSA3072 or stronger and never copies a private key or raw build receipt
into release output.
Keep a separate protected backup of the signing key. Losing every copy prevents
signing releases trusted by existing builds; replacing trust then requires a
manually supplied build or a planned key rotation signed by a still-trusted key.

Preparation checks package names, embedded product/version metadata, receipt
lengths/hashes, matching source snapshots, and current checkout identity. It
copies the two verified packages into a fresh output directory and creates
`VineMix-<version>-Updates.json` with an RSA-PSS/SHA-256 signature over the exact
UTF-8 manifest bytes, stored as Base64 in the envelope. The actual updater parser verifies that envelope.
The fourth output, `preparation-receipt.json`, contains sanitized local provenance;
it is not a release asset. A source change during preparation invalidates the
output metadata, and the candidates must be rebuilt.

Publish the two canonical `.exe` files, the matching `Updates.json`, a matching
source ZIP and `SHA256SUMS.txt` together on the same `v<version>` release.
Checksums must cover the two executables, signed update metadata and source ZIP. Mark prereleases consistently with their semantic version. Upload and
publication are separate release operations. Review archive contents, notices,
metadata and privacy before distribution, then verify public discovery/download
and the complete update/rollback flow on isolated installations.

These manifest signatures authenticate package hashes for VineMix. They do not
claim a Windows Authenticode certificate, SmartScreen reputation, or completion
of end-to-end update testing. Never publish a private key or add credentials to
the updater.

## Qualify a delivered update

The first updater-enabled release is `0.3.0-beta.2`. Beta 1 has no update control,
and Beta 2 accepts only a strictly newer release. Qualify the actual Beta 2 client
against the legitimate signed Beta 5 release; do not relabel existing files, bypass
version checks or publish a dummy version merely to produce a passing result.
Signed metadata verification and isolated worker tests remain separate evidence.

Use an authorized separate Windows account or machine, with no personal workspace.
`--settings` alone leaves the updater storage, uninstall registration and session
coordination shared with the current user. Retain raw logs and backups outside
the source checkout and record only sanitized results.

For each distribution, retain the old and new executable hashes, versions,
matching source identities, verified release envelope and the actual scenario
outcome:

1. Install or open the exact older delivered client normally. Create a small
   example workspace, deliberately assign available test devices, wait for Saved,
   then quit and reopen to establish its baseline. Record the complete workspace
   and its physical settings path from an open file handle.
2. Use **Check for updates** against the real release channel. Confirm the newer
   version and release notes. **Download update** must retain audio and verify the
   signed artifact. Declining installation must leave the old app usable.
3. Choose **Install and restart**. Confirm a saved final edit, normal UI/engine
   shutdown, unchanged drivers and startup preference, and exactly one replacement
   session. For Setup, check the existing directory and owned uninstall entry;
   for standalone, check that the original launcher path now has the new bytes.
4. Confirm the candidate version, the same physical workspace, routing, effects,
   assignments, positions, camera and preferences. Editing must resume only after
   candidate health commits. Check advancing engine frames and actual audio;
   an inert smoke result or a surviving process is insufficient. Quit and reopen
   again using the original shortcut or launcher.
5. On fresh disposable copies, exercise failed candidate health and interrupted
   handoffs at the supported journal checkpoints. Verify safe rollback reopens
   the exact previous bytes, newer settings remain intact and no process is killed.
   An ambiguous installed-Setup postimage must preserve its backup and request
   manual repair instead of overwriting unfamiliar files. Preserve the observed
   journals and actual failure evidence; do not simulate success by writing health
   markers from a fixture.

Leave each unperformed branch pending. The delivered updater's installed and
standalone success, rollback and interruption cases must be reported separately.
Physical resize, headset, clean-machine and sustained-use checks have their own
scope and are not established by this procedure.

Run `./scripts/test-update-release-tools.ps1` for isolated synthetic preparation
checks. Research checked 21 October 2026:
[RSA.SignData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa.signdata)
supports explicit SHA-256/PSS signing, and
[Inno Setup's product text version](https://jrsoftware.org/ishelp/topic_setup_versioninfoproducttextversion.htm)
supplies the semantic product version used by the executable metadata check.
