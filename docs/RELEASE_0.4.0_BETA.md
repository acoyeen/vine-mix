# VineMix 0.4.0 Beta release record

**28 October 2026 — corrected 0.4.0 Beta published, verified and installed.**

[VineMix 0.4.0 Beta](https://github.com/acoyeen/vine-mix/releases/tag/v0.4.0-beta)
is the published successor to the development work called **Beta 6**. It brings
together the application improvements, cleanup and offline attribution policy.
The [website](https://vinemix.app/) offers this release, including when GitHub
release discovery is unavailable. All **24 manually uploaded Beta 1–5 assets**
have been removed; their **five release posts and tags** remain.

The maintainer authorized cleanup, qualification, publication and staged download
retirement on 28 October 2026. This superseded the earlier Beta 6 development
hold and the plan to preserve old attachments. It did not finalize provisional
Beta 2 or change the permissions granted with earlier copies.

The initial personal update attempt rolled back after Setup and the inert smoke
check succeeded. Diagnostics identified a process-identity race in the older
updater. A subsequent manual installation of the original 0.4.0 Beta succeeded:
all **37 payload hashes** matched, an actual Explorer launch ran the 0.4.0 app
and engine, and physical settings identity and hashes were unchanged.

The maintainer then explicitly authorized replacing the **same 0.4.0 Beta**
release with the updater correction. The complete corrected asset set was
republished on the existing release post at **13:36 UTC on 28 October 2026**.
Its matching source tag, both CI runs, public downloads and signed update
discovery passed verification. Manual installation of the corrected build also
passed all **37 payload hashes**, followed by an actual Explorer launch of the
matching 0.4.0 app and engine with unchanged settings hashes and physical file
identity. Original publication evidence below is historical.

## Release identity

| Purpose | Released value or future convention |
| --- | --- |
| Application and website display | **0.4.0 · Beta** |
| Release title | **VineMix 0.4.0 Beta** |
| Canonical version and Git tag | `0.4.0-beta` and `v0.4.0-beta` |
| Windows numeric version | `0.4.0.0` |
| Small fixes and refinements | `0.4.1-beta`, `0.4.2-beta`, and so on |
| Larger feature updates | `0.5.0-beta`, `0.6.0-beta`, and so on |
| First stable release | `1.0.0`, without the Beta label |

The Beta label has no sequence number. The ordinary policy gives changed public
builds a new version. The maintainer explicitly authorized an exception for this
updater correction: retain `0.4.0-beta`, `0.4.0.0` and the existing release post,
replace the complete asset set, and move its tag to the matching corrected
source. Preserve the original packages, notes, tag target and hashes privately.
This exception does not authorize replacing unrelated releases.

The `-beta` suffix agrees with GitHub's prerelease flag. Existing development
receipts retain their actual `0.3.0-beta.6` / `0.3.0.7` identity; they are not
relabelled as the new release. Existing original 0.4.0 installations will not
discover a same-version correction as a newer release; they require a manual
Setup reinstall or standalone replacement. Beta 5 and Beta 6 users should also
install the corrected Setup directly once, because their existing workers retain
the process-identity race until replaced.
Keep version comparison and release-identity checks intact.

## Scope and evidence

The following work is included in the frozen release source. Per-feature
history stays in its original logs; final combined verification is in the
[qualification record](RELEASE_0.4.0_QUALIFICATION.md), and user-facing changes
are in the [release notes](releases/0.4.0-beta.md).

| Area | Released behavior | Development record |
| --- | --- | --- |
| Named node groups | Named frames, seven colors, automatic bounds, shared movement, membership, saving, Undo/Redo and complete-group copying | [IDEA-023](IDEAS_IMPLEMENTED.md#idea-023) |
| Group interaction and headings | Selection/movement from exposed backgrounds, concise help, centered names and readable titles; node/cable input retains precedence | [TWEAK-017](TWEAKS.md#tweak-017), [TWEAK-020](TWEAKS.md#tweak-020) |
| Overlapping nodes | Selected and dragged nodes remain above overlapping nodes | [ISSUE-022](KNOWN_ISSUES.md#issue-022) |
| Mixing nodes | Port-aware mixing and stereo tools, including Channel Swap, Stereo Width, Pan, Crossfade, Stereo Combine and Stereo Split | [Mixing-node development](BETA6_DEVELOPMENT.md#mixing-nodes--28-october-2026) |
| Level and linked controls | One Level dial for the former duplicate value; Linked control uses Multiplier; Ducking sidechain is labelled Trigger | [Development record](BETA6_DEVELOPMENT.md) |
| Optional notifications | Saved toggle hides routine timed messages while retaining errors, save status and Activity | [TWEAK-015](TWEAKS.md#tweak-015) |
| Precise numeric entry | Valid values apply on focus loss at full precision; invalid drafts remain visible; Apply/Cancel stay available | [ISSUE-004](KNOWN_ISSUES.md#issue-004) |
| Settings search and organization | Aligned placeholder/caret/text, related actions grouped, search first and Quit separate | [ISSUE-001](KNOWN_ISSUES.md#issue-001), [TWEAK-016](TWEAKS.md#tweak-016) |
| Information windows and workspace | Centered node count and consistent compact About, What's New and Updates windows | [TWEAK-002](TWEAKS.md#tweak-002), [TWEAK-004](TWEAKS.md#tweak-004) |
| Branding | Monochrome identity and brighter Windows, taskbar, tray and shortcut icons | [TWEAK-013](TWEAKS.md#tweak-013) |
| Optional driver setup | Clear choices, missing eligible drivers selected initially, retained deselections, separate consent and restart guidance | [TWEAK-014](TWEAKS.md#tweak-014) |
| Attribution | Offline credit, MIT licence, attribution policy and third-party notices | [Attribution policy](../ATTRIBUTION.md) |

The cleanup also protects pending node/group names during Quit and update
preparation, preserves unsaved interface preferences during shutdown, and avoids
recomputing settled Pan/Crossfade coefficients while retaining per-sample
smoothing. Compatibility identifiers, normal Windows/WPF resizing and unrelated
work were preserved. Unselected ideas remain proposals in their existing logs.

## Attribution embedded and packaged

**Settings → About VineMix → Credits and licences** shows **VineMix by acoyeen**
and provides offline access to the full MIT licence, attribution policy and
third-party notices. Optional buttons open the website, original repository and
online attribution page. Notice text is selectable, wraps and scrolls separately
from the visible Close action.

The authoritative root `LICENSE`, `ATTRIBUTION.md` and `THIRD_PARTY_NOTICES.md`
are embedded in both desktop formats and included in installed files, the
standalone runtime and matching source archive. Package allowlists, source
identity, receipts and verification cover those files. Embedded and extracted
notice hashes match the authoritative source. Detached checks and visual review
cover narrow/standard layouts and high contrast; real pointer/VR qualification
remains separate.

**Licence boundary:** required MIT copyright/permission notice retention and the
requested visible acknowledgement inside forks remain distinct. This release
does not make a fork's About-screen credit compulsory or revoke earlier copies'
permissions. Required third-party notices retain their own terms.

## Corrected published source and qualification

- Frozen source and tag target: [`21516dcf827dc157fb154287c016f02a014fc3b2`](https://github.com/acoyeen/vine-mix/commit/21516dcf827dc157fb154287c016f02a014fc3b2).
- Build-input SHA-256: `c653d265d0694311e8ccfe47c228ef2f29c8667c534334b0e6a0565a6b68b4a3`.
- All **17 native offline suites**, including **289,903 graph assertions**, passed.
- The corrected updater passed **327 focused checks** and **three production-worker signed package smoke runs**.
- Combined detached desktop verification passed **14,253 checks**, produced **297 renders** and reported **zero binding errors**.
- Delivered Setup passed **225 checks**; standalone extraction/integrity passed **21 checks**. Embedded and extracted notices matched the authoritative source.
- The source ZIP contains **602 entries**, covering all **323 build inputs** and sanitized provenance. Every entry and archive metadata were audited.
- Corrected exact-source [build checks](https://github.com/acoyeen/vine-mix/actions/runs/40695412627) and independent [published-asset checks](https://github.com/acoyeen/vine-mix/actions/runs/40697928142) passed.

The existing release ID **426653672**, title, URL and prerelease status were
retained. All five original assets and release metadata were backed up privately.
The release was staged as a draft while the complete five-file replacement was
uploaded, downloaded, rehashed and signature-verified. The tag was moved from
the original source to the exact corrected source before publication. Final
verification confirmed the five expected asset digests, matching remote tag,
published state and HTTP 200 responses for all five anonymous download links.
Exact asset hashes are in the [qualification record](RELEASE_0.4.0_QUALIFICATION.md).
The update signature is not Windows Authenticode signing.

Anonymous discovery through the exact archived Beta 5 updater and the unchanged
Beta 6 update core verified the corrected signed envelope and package hashes.
The current 0.4.0 identity correctly reports up to date. Discovery does not fix
the older workers: direct Setup remains the upgrade route for them. The corrected
worker retains a process handle for identity checks; its successful inert smoke
also stays alive for at least two seconds as a bounded compatibility mitigation.
The old worker passed 11 of 12 corrected installed-package smoke attempts; its
remaining failure occurred during startup. This is not a complete legacy fix or
proof of a full automatic installation transaction. See
[ISSUE-023](KNOWN_ISSUES.md#issue-023).

## Original publication evidence — historical

The following describes the superseded original build, whose assets and metadata
are preserved privately; it does not identify the current downloads.

- Frozen source: [`4e7b31e36b24be435eb4283468eb1680d3a7617b`](https://github.com/acoyeen/vine-mix/commit/4e7b31e36b24be435eb4283468eb1680d3a7617b).
- Build-input SHA-256: `add276d50b99bcdff6cd2b5208008d145029dbbc53c8207ffc41a83bfd9e08bb`.
- All **17 native offline suites**, **93 update-core checks** and **111 update-worker checks** passed.
- Combined detached desktop verification passed **14,253 checks**, produced **297 renders** and reported **zero binding errors**.
- Delivered Setup passed **225 checks**; standalone extraction/integrity passed **21 checks**. Both production executables passed inert smoke checks.
- The source ZIP contains **601 entries**, covering all **322 build inputs** and sanitized provenance. Entry bytes and archive metadata were audited.
- Exact-source [build checks](https://github.com/acoyeen/vine-mix/actions/runs/40688774615) and independent [published-asset checks](https://github.com/acoyeen/vine-mix/actions/runs/40690410311) passed.

Both original packages came from that clean committed source. Their five assets
were downloaded and rehashed before initial publication, and their update
metadata passed production-pinned signature verification. The failed automatic
update and successful manual recovery described above prompted the explicitly
authorized correction. Beta 1–4 retain a direct manual upgrade path without any
retired intermediate download.

## Initial publication and website cutover

The published [GitHub prerelease](https://github.com/acoyeen/vine-mix/releases/tag/v0.4.0-beta)
contains all five release assets:

- `VineMix-0.4.0-beta-Setup-x64.exe`
- `VineMix-0.4.0-beta-Standalone-x64.exe`
- `VineMix-0.4.0-beta-Source.zip`
- `SHA256SUMS.txt`
- `VineMix-0.4.0-beta-Updates.json`

After those downloads were verified, the website cut over to 0.4.0 Beta.
Cloudflare deployment `949e6860-e6b0-4214-a950-52f5688e8572` serves Worker version
`89105feb-9f2a-4517-b780-a0c221881395` at 100%. All **85 public files** matched the
prepared bytes and retained the three configured security headers. Six isolated
browser cases at **320, 390 and 1440 pixels**, with GitHub discovery online and
blocked, showed the correct release and download/documentation/checksum links.
No horizontal overflow or browser errors were observed in those cases.

Worker settings and the previous version were preserved. The detailed website
record is in [HOSTING.md](../website-redesign/HOSTING.md#040-beta-release-cutover--28-october-2026).

## Beta 1 through 5 download retirement

**Completed 28 October 2026:** all **24 manually uploaded attachments** were
removed after the new release and website were verified. The five release posts,
tags, titles and historical change notes remain, with removal notices and links
to 0.4.0 Beta. All **24 former public asset links return HTTP 404**. Provisional
Beta 2 was not finalized or replaced.

| Retained release | Attachments removed | Attached assets remaining |
| --- | ---: | ---: |
| [Beta 1](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.1) | 4 | 0 |
| [Beta 2](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2) | 5 | 0 |
| [Beta 3](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.3) | 5 | 0 |
| [Beta 4](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.4) | 5 | 0 |
| [Beta 5](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.5) | 5 | 0 |

Removal covered Setup, standalone, manually uploaded source ZIPs, checksums and,
where present, signed update metadata. Recovery copies and original release-note
text are retained outside the checkout. None of these five release pages or
tags was deleted or moved.

GitHub-generated **Source code (zip)** and **Source code (tar.gz)** archives
remain available through retained tags. Download retirement changes distribution;
it does not revoke existing licence permissions or prevent reuse of historical
source. The explicitly authorized 0.4.0 correction kept source, packages,
checksums and signed metadata together; it did not alter the completed Beta 1–5
retirement.

## Completion record and remaining work

| Milestone | Status |
| --- | --- |
| Corrected combined source and cleanup | Frozen at `21516dcf827dc157fb154287c016f02a014fc3b2` |
| Offline attribution and package notices | Implemented and verified |
| 0.4.0 identity in source and released binaries | Verified |
| Automated qualification and published-asset checks | Passed within the documented scope |
| Initial GitHub prerelease and website cutover | Published and verified |
| Beta 1–5 attachments and historical notices | 24 attachments removed and former links return 404; five posts/tags retained |
| Original 0.4.0 live installation | Manual installation verified: 37 payload hashes, Explorer app/engine launch and preserved physical settings |
| Corrected 0.4.0 source, packages and republish | Same release post retained; all five replacements and matching tag verified; source and asset CI passed |
| Corrected 0.4.0 live installation | Manual installation verified: 37 payload hashes, Explorer app/engine launch and preserved physical settings |

Real pointer/VR use, fresh-machine vendor driver installation/consent/cancel/
elevation/reboot, sustained real-device audio and complete interrupted-update/
rollback scenarios on a separate Windows account remain unperformed. Synthetic
checks and successful downloads do not establish those outcomes.

**Remaining qualification:** exercise complete delivered update, health-check,
rollback and interruption scenarios on a separate Windows account. The corrected
manual installation and focused process tests do not establish those full
transactions. Keep direct Setup guidance for older workers and original 0.4.0
installations. Repository visibility is unchanged; raw diagnostics, settings and
recovery files remain outside project documentation and downloadable artifacts.

The [Beta 6 development record](BETA6_DEVELOPMENT.md) and
[earlier release draft](releases/0.3.0-beta.6.md) preserve dated development
identities and verification; they are not substituted for final qualification.

## References

Release mechanics and licence boundaries checked on 28 October 2026:

- [GitHub release management](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository).
- [GitHub release asset API](https://docs.github.com/en/rest/releases/assets#delete-a-release-asset).
- [About GitHub releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).
- [Semantic Versioning](https://semver.org/).
- [MIT licence](https://opensource.org/license/mit) and the project's [attribution policy](../ATTRIBUTION.md).
