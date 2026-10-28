# VineMix 0.4.0 Beta release record

**28 October 2026 — initial release and live installation verified; an authorized correction to the same 0.4.0 Beta release is in progress.**

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
release with the updater correction. Corrected source, packages and publication
are being prepared; the checks and hashes below identify the original published
build until a separate corrected-build record is added. The corrected build has
not yet been released or installed.

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
discover a same-version correction as a newer release; once the corrected files
are published, they require a manual Setup reinstall or standalone replacement.
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

## Original published source and qualification

- Frozen source: [`4e7b31e36b24be435eb4283468eb1680d3a7617b`](https://github.com/acoyeen/vine-mix/commit/4e7b31e36b24be435eb4283468eb1680d3a7617b).
- Build-input SHA-256: `add276d50b99bcdff6cd2b5208008d145029dbbc53c8207ffc41a83bfd9e08bb`.
- All **17 native offline suites**, **93 update-core checks** and **111 update-worker checks** passed.
- Combined detached desktop verification passed **14,253 checks**, produced **297 renders** and reported **zero binding errors**.
- Delivered Setup passed **225 checks**; standalone extraction/integrity passed **21 checks**. Both production executables passed inert smoke checks.
- The source ZIP contains **601 entries**, covering all **322 build inputs** and sanitized provenance. Entry bytes and archive metadata were audited.
- Exact-source [build checks](https://github.com/acoyeen/vine-mix/actions/runs/40688774615) and independent [published-asset checks](https://github.com/acoyeen/vine-mix/actions/runs/40690410311) passed.

Both packages came from the same clean committed source. All five uploaded
assets were downloaded and rehashed before publication. Signed update metadata
passed verification against the production-pinned key. Package hashes and the
full scope of checks are recorded in the [qualification record](RELEASE_0.4.0_QUALIFICATION.md).
The update signature is not Windows Authenticode signing.

Anonymous discovery through the exact archived Beta 5 updater offers 0.4.0 Beta.
The current updater offers it from the Beta 6 identity and reports 0.4.0 Beta up
to date. This proves discovery and signed package compatibility, not completion
of a personal installation transaction. Beta 1–4 retain the documented direct
manual upgrade path, without requiring any retired intermediate download.

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
to 0.4.0 Beta. Provisional Beta 2 was not finalized or replaced.

| Retained release | Attachments removed | Attached assets remaining |
| --- | ---: | ---: |
| [Beta 1](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.1) | 4 | 0 |
| [Beta 2](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2) | 5 | 0 |
| [Beta 3](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.3) | 5 | 0 |
| [Beta 4](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.4) | 5 | 0 |
| [Beta 5](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.5) | 5 | 0 |

Removal covered Setup, standalone, manually uploaded source ZIPs, checksums and,
where present, signed update metadata. Recovery copies and original release-note
text are retained outside the checkout. No release page or tag was deleted and
Git history was not rewritten.

GitHub-generated **Source code (zip)** and **Source code (tar.gz)** archives
remain available through retained tags. Download retirement changes distribution;
it does not revoke existing licence permissions or prevent reuse of historical
source. The explicitly authorized 0.4.0 correction described above must still
keep source, packages, checksums and signed metadata together; it does not alter
the completed Beta 1–5 retirement.

## Completion record and remaining work

| Milestone | Status |
| --- | --- |
| Original combined source and cleanup | Frozen at `4e7b31e36b24be435eb4283468eb1680d3a7617b` |
| Offline attribution and package notices | Implemented and verified |
| 0.4.0 identity in source and released binaries | Verified |
| Automated qualification and published-asset checks | Passed within the documented scope |
| Initial GitHub prerelease and website cutover | Published and verified |
| Beta 1–5 attachments and historical notices | 24 attachments removed; five posts/tags retained |
| Original 0.4.0 live installation | Manual installation verified: 37 payload hashes, Explorer app/engine launch and preserved physical settings |
| Corrected 0.4.0 source, packages and republish | Explicitly authorized; preparation and qualification in progress |
| Corrected 0.4.0 live installation | Pending corrected package qualification |

Real pointer/VR use, fresh-machine vendor driver installation/consent/cancel/
elevation/reboot, sustained real-device audio and complete interrupted-update/
rollback scenarios on a separate Windows account remain unperformed. Synthetic
checks and successful downloads do not establish those outcomes.

**Next action:** qualify the updater correction, freeze matching corrected
source and both package formats, and replace the same 0.4.0 Beta release as
explicitly authorized. Back up all five original assets and release metadata,
keep the release post, and verify the complete replacement asset set and moved
source tag before making it available. Run exact-source and published-asset CI
checks against the correction. Record its final source and hashes separately
from the original record, then qualify the corrected live installation with the
existing physical-settings, inventory and Windows-shell launch safeguards.
Preserve repository visibility and keep raw diagnostics, settings and recovery
files outside project documentation and downloadable artifacts.

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
