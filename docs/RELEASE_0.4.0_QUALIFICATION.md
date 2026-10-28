# VineMix 0.4.0 Beta qualification

**28 October 2026 — corrected release published and installed; qualification and beta limits.**

**Published:** [VineMix 0.4.0 Beta](https://github.com/acoyeen/vine-mix/releases/tag/v0.4.0-beta).
The corrected exact [source build checks](https://github.com/acoyeen/vine-mix/actions/runs/40695412627)
and independent [published-asset checks](https://github.com/acoyeen/vine-mix/actions/runs/40697928142)
both passed.

The release uses `0.4.0-beta`, Windows version `0.4.0.0` and display
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
graph assertions**. The corrected updater passes **327 focused checks** and
**three production-worker signed package smoke runs**.
The integrated detached desktop suite passes **14,253 checks**, produces **297
renders** and reports **zero binding errors**. Offline attribution views were
visually reviewed at narrow/standard widths and high contrast; their embedded
text hashes match the authoritative root files.

The exact-source ZIP's `SOURCE-MANIFEST.json` records the final source commit,
build-input inventory and hash, exact package identities and final package
verification. Published checksums and signed update metadata identify the
downloaded bytes. The release hub records the subsequent publication outcome.

Final frozen source: `21516dcf827dc157fb154287c016f02a014fc3b2`.
Build-input SHA-256: `c653d265d0694311e8ccfe47c228ef2f29c8667c534334b0e6a0565a6b68b4a3`.
The archive contains **602 entries**, covering all **323 build inputs** and
the sanitized provenance manifest. Every entry was rehashed and archive metadata
was checked. Both packages came from the same clean committed source.

Delivered Setup passed **225 checks**; standalone extraction/integrity passed
**21 checks**. Both production executables passed inert smoke checks, and the
embedded/extracted licence, attribution and third-party notices match source.
Setup qualification used its isolated test identity and did not run vendor
installers or change the personal audio session.

The same release post, ID **426653672**, was republished at **13:36 UTC on
28 October 2026** after all five corrected assets were downloaded and rehashed
and the production-pinned update signature passed. The tag matches the frozen
source. All five anonymous download links return HTTP 200. Exact archived Beta 5
and unchanged Beta 6 update-core discovery verify the corrected signed envelope
and package hashes; the current 0.4.0 identity reports up to date. Discovery
does not establish a complete automatic installation transaction. The project
update signature is not Windows Authenticode signing.

| Asset | SHA-256 |
| --- | --- |
| Setup x64 | `43f25d239577752d784281fdeb62ac852d8200916c2f0f190e8183e83a0a7e48` |
| Standalone x64 | `389f97583786535df011e88f4f93014d4eae39ebfbf278d5b45741f7f40039df` |
| Source ZIP | `5a7e115824967e2eef4b72c120ae1d01fd101d234f2acc226a2ea1fdafaa71a9` |
| Signed Updates JSON | `0f0aec1076510957361f6394390fccd2515a2b08f5f846a51288a2e8481e8110` |
| SHA256SUMS.txt | `599ba1e65c36cdbdb750e2d74883fe3b3cd19724313b2f32c38ded43fcfdc651` |

The existing local-activation safeguards pass 40 shared, 97 interface-only and
80 paired-package checks. Native version-resource fixtures pass 5 checks and
synthetic signed-release preparation passes 9 checks. All ten website check
suites pass with unnumbered Beta labels and retained empty historical releases.

The synthetic 30-node median Pan/Crossfade benchmark improved from approximately
94 microseconds to 43/40 microseconds by reusing settled weights; benchmark
checksums remain unchanged. This is processing-cost evidence, not hardware
latency or an audible-quality claim. Per-sample transition regressions pass.

## Correction, installation and remaining beta qualification

The first real Beta 6 update attempt exposed [ISSUE-023](KNOWN_ISSUES.md#issue-023):
Setup and the inert package check succeeded, but the old worker could not
identify the short-lived process and rolled back the application payload.
Personal settings were not replaced. Manual installation of the same published
Setup then verified all 37 installed payload files, matching app/engine 0.4.0
identities and an actual Explorer shortcut launch with unchanged settings and
physical file identity. Automatic transaction completion was not established by
that manual recovery. The initial source
`4e7b31e36b24be435eb4283468eb1680d3a7617b`, its assets, hashes and metadata remain
in private recovery evidence; the [release record](RELEASE_0.4.0_BETA.md) labels
its qualification and CI results as historical.

The maintainer explicitly authorized the corrected same-version replacement.
Its manual installation subsequently passed all **37 payload hashes** and a real
Explorer shortcut launch of the matching corrected app and engine, with unchanged
settings hashes and physical file identity. Installed app SHA-256:
`de386dd83616717a4eadbc84b3a471bffca3fa38f25cc156e3ce82e09c33aa2b`;
engine SHA-256: `e19dead79c4c0ed725fe5a564d04ddef434fabcff92649aa67ad3cf0becc63b4`.

The corrected worker uses the retained process handle for identity and accepts a
completed child only with its actual exit code and required result checks. The
old worker passed **11 of 12** corrected installed-package smoke attempts; an
early **4.56 ms** failure exposed the startup lookup race before initialization.
Keeping successful inert smoke alive for at least two seconds is a bounded
mitigation, not a repair of old worker code. Beta 5/Beta 6 users should use direct
Setup once. Original 0.4.0 users also need a manual reinstall or standalone
replacement because version comparison is unchanged. Full delivered automatic
update health, rollback and interruption transactions remain unqualified.

Real pointer/VR use, fresh-machine vendor installer consent/cancel/elevation/
reboot, sustained real-device audio, and delivered update/rollback interruption
scenarios on a separate Windows account remain unperformed. These are beta
limitations, not inferred passes from synthetic tests or successful downloads.

Personal live activation is separate and requires permission for the brief audio
interruption. Raw diagnostics, settings, backups and signing material remain
outside source and downloadable artifacts.
