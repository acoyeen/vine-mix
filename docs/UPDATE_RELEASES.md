# Preparing signed update releases

The updater reads versioned GitHub releases from `acoyeen/vine-mix`, using tags
`v<version>`. Beta versions are published as prereleases. Canonical release asset
URLs, signed update metadata and the embedded public trust key are configured
before publication. The client contains no GitHub credential.

Release signing prepares verified assets; the publication step uploads and
validates those exact bytes before making the version downloadable. The
maintainer's standing release policy is in [AGENTS.md](../AGENTS.md#product-releases-and-repository-visibility).

Build Setup and standalone candidates from the same stable source snapshot and
the authoritative `release.json`. Keep the existing installer `.exe.json` receipt
and standalone directory `.build.json` receipt. Write concise UTF-8 release notes.
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

The three update assets must be the two canonical `.exe` filenames and
the matching `Updates.json`, all attached to the same published `v<version>`
release. Mark prereleases consistently with their semantic version. Use the [release publisher](../.github/release/README.md) to validate and publish
the prepared version with release notes and checksums. Review archive contents, notices,
metadata and privacy before distribution, then verify release discovery/download
and the complete update/rollback flow on isolated installations.

These manifest signatures authenticate package hashes for VineMix. They do not
claim a Windows Authenticode certificate, SmartScreen reputation, or completion
of end-to-end update testing. Never publish a private key or add credentials to
the updater.

Run `./scripts/test-update-release-tools.ps1` for isolated synthetic preparation
checks. Research checked 21 October 2026:
[RSA.SignData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsa.signdata)
supports explicit SHA-256/PSS signing, and
[Inno Setup's product text version](https://jrsoftware.org/ishelp/topic_setup_versioninfoproducttextversion.htm)
supplies the semantic product version used by the executable metadata check.
