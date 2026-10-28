# VineMix 0.4.0 Beta qualification

**28 October 2026 — release qualification and beta limits.**

**Published:** [VineMix 0.4.0 Beta](https://github.com/acoyeen/vine-mix/releases/tag/v0.4.0-beta).
The exact [source build checks](https://github.com/acoyeen/vine-mix/actions/runs/40688774615)
and independent [published-asset checks](https://github.com/acoyeen/vine-mix/actions/runs/40690410311)
both passed.

The candidate uses `0.4.0-beta`, Windows version `0.4.0.0` and display
**0.4.0 · Beta**. The [release notes](releases/0.4.0-beta.md) describe the
integrated features; the [release plan](RELEASE_0.4.0_BETA.md) tracks publication
and retirement. Earlier Beta 6 evidence remains dated development evidence.

## Audit scope

Desktop review covers named frames, selection order, clipboard, port-aware
routing, numeric entry, shutdown and update preparation. Native review covers
the new stereo nodes and control smoothing. Release review covers identity,
offline notices, source provenance, package inventory, update trust, website
discovery and the fallback download.

Demonstrated defects are corrected with regressions. Existing compatibility
nodes and historical identifiers are retained. Website prototypes and unrelated
working changes are preserved outside the release scope.

## Verification record

Code qualification passes all **17 native offline suites**, including **289,903
graph assertions**, **93 update-core checks** and **111 update-worker checks**.
The integrated detached desktop suite passes **14,253 checks**, produces **297
renders** and reports **zero binding errors**. Offline attribution views were
visually reviewed at narrow/standard widths and high contrast; their embedded
text hashes match the authoritative root files.

The exact-source ZIP's `SOURCE-MANIFEST.json` records the final source commit,
build-input inventory and hash, exact package identities and final package
verification. Published checksums and signed update metadata identify the
downloaded bytes. The release hub records the subsequent publication outcome.

Final frozen source: `4e7b31e36b24be435eb4283468eb1680d3a7617b`.
Build-input SHA-256: `add276d50b99bcdff6cd2b5208008d145029dbbc53c8207ffc41a83bfd9e08bb`.
The archive contains **601 entries**, covering all **322 build inputs** and
the sanitized provenance manifest. Every entry was rehashed and archive metadata
was checked. Both packages came from the same clean committed source.

Delivered Setup passed **225 checks**; standalone extraction/integrity passed
**21 checks**. Both production executables passed inert smoke checks, and the
embedded/extracted licence, attribution and third-party notices match source.
Setup qualification used its isolated test identity and did not run vendor
installers or change the personal audio session.

All five uploaded release assets were downloaded and rehashed before the
prerelease was made visible. The production-pinned update signature passed.
Anonymous discovery using the exact archived Beta 5 updater offers 0.4.0 Beta;
the current updater offers it from the Beta 6 identity and reports 0.4.0 Beta
up to date. Signed package sizes/hashes match the final files. This verifies
discovery and compatibility, not a completed personal installation transaction.

| Asset | SHA-256 |
| --- | --- |
| Setup x64 | `aafed368f2b91152a4bb3829db42b334e5e4115e3a6d3688698707c821125d46` |
| Standalone x64 | `d8ad8c8da9dc8b54635936fcef8245752673f076e0f556ea12ce5b31fb4559cf` |
| Source ZIP | `ac9a4c21f05c3539c3e9fe898fcc8d455860edaf621d92c6f537fe0f14ac3a96` |
| Signed Updates JSON | `545633364d7680e6e68dcc647fe81802d16f3be088a6dfd254300ad53340f181` |
| SHA256SUMS.txt | `bd138b19dabb055955815af020cee57b60b0bc595e35a362b8412edec8477d88` |

The existing local-activation safeguards pass 40 shared, 97 interface-only and
80 paired-package checks. Native version-resource fixtures pass 5 checks and
synthetic signed-release preparation passes 9 checks. All ten website check
suites pass with unnumbered Beta labels and retained empty historical releases.

The synthetic 30-node median Pan/Crossfade benchmark improved from approximately
94 microseconds to 43/40 microseconds by reusing settled weights; benchmark
checksums remain unchanged. This is processing-cost evidence, not hardware
latency or an audible-quality claim. Per-sample transition regressions pass.

## Remaining beta qualification

The first real Beta 6 update attempt exposed [ISSUE-023](KNOWN_ISSUES.md#issue-023):
Setup and the inert package check succeeded, but the old worker could not
identify the short-lived process and rolled back the application payload.
Personal settings were not replaced. Manual installation of the same published
Setup then verified all 37 installed payload files, matching app/engine 0.4.0
identities and an actual Explorer shortcut launch with unchanged settings and
physical file identity. Automatic transaction completion was not established by
that manual recovery. A corrected 0.4.0 rebuild is being qualified at the
maintainer's explicit request; the initial release evidence above is retained.

Real pointer/VR use, fresh-machine vendor installer consent/cancel/elevation/
reboot, sustained real-device audio, and delivered update/rollback interruption
scenarios on a separate Windows account remain unperformed. These are beta
limitations, not inferred passes from synthetic tests or successful downloads.

Personal live activation is separate and requires permission for the brief audio
interruption. Raw diagnostics, settings, backups and signing material remain
outside source and downloadable artifacts.
