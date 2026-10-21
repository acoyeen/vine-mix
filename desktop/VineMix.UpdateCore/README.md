# Update core

This dependency-free .NET library checks public releases from
`acoyeen/vine-mix`. It never discovers credentials, starts the application or
engine, reads workspace settings, or publishes releases.

`UpdateTrust.Production` loads the embedded public RSA trust map from
`updates/release-trust.json`. `Verify` authenticates the exact decoded UTF-8
manifest bytes with RSA-PSS/SHA-256 before interpreting the manifest. RSA keys
must contain at least 3072 bits. Required fields, duplicate properties, unknown
fields, compatibility, package identity and canonical version-specific release
URLs are checked. The signed SHA-256 digest authenticates downloaded package
bytes independently of Windows Authenticode.

`GitHubUpdateSource.CheckAsync` returns an explicit status and an `UpdateOffer`
only after signature and published asset metadata checks. The stable channel
excludes prereleases; version ordering follows semantic version precedence.
Checks inspect at most four pages of 100 releases, bound each API response to
2 MiB, and time out after 45 seconds. Empty feeds, private/unpublished repository
responses, rate limits, offline errors and malformed releases are distinct from
an up-to-date result.

`UpdateDownloader.DownloadAsync` takes a verified offer, an asset kind and a
caller-selected staging parent. It creates a fresh random child directory,
streams a package of at most 1 GiB, and checks exact length and SHA-256 before
giving it its final filename. Failed or cancelled downloads clean their partial
staging directory. The 10-minute download timeout throws cancellation in the
same way as the supplied cancellation token. The caller owns successful staging
and must reverify it in the update worker immediately before execution or copy.

HTTPS redirects are followed explicitly, with a maximum of three hops, only to
GitHub's exact `release-assets.githubusercontent.com` or
`objects.githubusercontent.com` release CDN hosts. API redirects are rejected.
Automatic redirects, cookies and default server credentials are disabled in the
production HTTP handler. Injected handlers are for deterministic tests.

The adjacent console test project uses ephemeral signing keys, synthetic
packages, mocked HTTP and isolated temporary staging. It covers signature and
manifest faults, version/channel selection, public-feed errors, download
corruption/interruption/cancellation and redirect boundaries. It does not make
network requests or validate a real published update. Release publication and
an end-to-end update remain separate verification steps.
