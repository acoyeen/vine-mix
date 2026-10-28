# VineMix 0.4.0 Beta release plan

**Status: authorized cleanup and release qualification in progress. Updated 28 October 2026.**

This is the release hub for the work previously called **Beta 6**. The next
release is **0.4.0 · Beta**. It combines the application improvements already
under development, embeds the attribution policy, and retires the uploaded
Beta 1–5 downloads while retaining their release pages and tags.

The maintainer authorized end-to-end cleanup, qualification and publication on
28 October 2026, including the staged retirement of old attached downloads.
Final candidate qualification remains outstanding. Personal installation still
requires explicit permission for its brief audio restart. The retirement
direction below supersedes the earlier plan to keep those downloads available;
it does not finalize provisional Beta 2 or rewrite its release history.

## Release identity

| Purpose | Target |
| --- | --- |
| Application and website display | **0.4.0 · Beta** |
| Release title | **VineMix 0.4.0 Beta** |
| Canonical version and Git tag | `0.4.0-beta` and `v0.4.0-beta` |
| Proposed Windows numeric version | `0.4.0.0` |
| Small fixes and refinements | `0.4.1-beta`, `0.4.2-beta`, and so on |
| Larger feature updates | `0.5.0-beta`, `0.6.0-beta`, and so on |
| First stable release | `1.0.0`, without the Beta label |

The Beta label no longer has a sequence number. Each published build gets a
new version; do not replace a published package with different bytes under the
same version. Keep the `-beta` suffix internally because release discovery
requires the GitHub prerelease flag to agree with the version suffix.

During preparation, existing installed binaries and receipts still describe
`0.3.0-beta.6` / `0.3.0.7`. Change their identity only through a verified paired
app/engine build and installation. Do not merely relabel old binaries. The
eventual paired transition needs a brief audio restart; obtain the maintainer's
permission for that interruption before activating it on the personal system.

## Scope and evidence

These are candidate inclusions, with status taken from the development records
on 28 October. Earlier verification applies to its recorded source snapshot,
not automatically to the combined 0.4.0 candidate.

| Area | Intended release behavior | Evidence and remaining work |
| --- | --- | --- |
| Named node groups | Named frames, seven colors, automatic bounds, shared movement, explicit membership, saving, Undo/Redo and complete-group copying; no audio processing | [IDEA-023](IDEAS_IMPLEMENTED.md#idea-023): locally integrated. Preserve these behaviors during the follow-up below. |
| Group interaction refinement | Stronger colors, selection and movement from exposed backgrounds, concise hover help; nodes and cables keep input precedence | [TWEAK-017](TWEAKS.md#tweak-017): locally integrated; real pointer/VR review and final candidate qualification remain. |
| Overlapping nodes | The selected or dragged node remains above overlapping nodes | [ISSUE-022](KNOWN_ISSUES.md#issue-022): resolved locally and integrated; retain regression coverage and complete real pointer/VR review. |
| Mixing nodes | Mix, Pan, Mono, Channel Swap, Stereo Width, Stereo Pan, Crossfade, Stereo Combine and Stereo Split; port-aware routing, persistence and history | Latest [development record](BETA6_DEVELOPMENT.md#mixing-nodes--28-october-2026); requalify combined native and desktop paths. |
| Level and linked controls | One Level dial where Volume and Gain duplicated the same value; Linked control uses Multiplier; Ducking sidechain is labelled Trigger | Latest development follow-ups; retain migration, numeric editing and control-link behavior. |
| Group headings | Centered name only, preserving frame colors and interactions | Final TWEAK-020 direction supersedes the earlier color swatch; retain readable titles at all supported zoom levels. |
| Optional action notifications | A persistent Settings toggle hides routine timed messages while retaining errors, save status and Activity | [TWEAK-015](TWEAKS.md#tweak-015): locally integrated, including the concise tooltip follow-up. |
| Precise numeric editing | Valid values apply on focus loss with full precision; invalid drafts stay visible; Apply/Cancel are available in the text menu | [ISSUE-004](KNOWN_ISSUES.md#issue-004): locally integrated. Preserve keyboard, pointer and cancellation behavior. |
| Settings search | Placeholder, caret and typed text align across display scaling | [ISSUE-001](KNOWN_ISSUES.md#issue-001): locally integrated; retain installed visual verification in the release checklist. |
| Settings organization | Related workspace, audio, application and help actions are grouped using existing separators; search stays first and Quit separate | [TWEAK-016](TWEAKS.md#tweak-016), superseding earlier placement details in TWEAK-001/003. Locally integrated. |
| Workspace and information windows | Centered node count; consistent compact About, What's New and Updates windows with clear dismissal and update-install protection | [TWEAK-002](TWEAKS.md#tweak-002) and [TWEAK-004](TWEAKS.md#tweak-004): locally implemented; recheck after attribution is added. |
| Branding | Monochrome app identity and brighter soft-white Windows, taskbar, tray and shortcut icons | [TWEAK-013](TWEAKS.md#tweak-013): locally integrated; inspect actual delivered icon behavior. |
| Optional driver setup | Clear virtual-route requirements, missing eligible drivers selected by default, explicit deselections retained, separate consent, VAC Lite before VB-CABLE and restart guidance | [TWEAK-014](TWEAKS.md#tweak-014): isolated Setup checks recorded; real vendor installation, cancellation, elevation and reboot remain to qualify. |
| Attribution | Offline project credit and licence/policy access in the application and every delivered format | [Published policy](../ATTRIBUTION.md) exists; application embedding and final package verification are planned here. |
| Release information | Bundled What's New, About, update messages, filenames and documentation use the new identity | Currently mixed development/published identities; reconcile and test during preparation. |

The [Beta 6 development record](BETA6_DEVELOPMENT.md) retains exact source and
binary identities and local activation evidence. Its existing runs include
17 native offline suites and a later group/stacking detached run of 13,526
checks with 273 renders and zero binding errors. These are useful baselines, not final
0.4.0 qualification. The [earlier release draft](releases/0.3.0-beta.6.md)
retains the original feature descriptions and remaining live checks.

Website attribution and footer changes are already published; see
[website deployment evidence](../website-redesign/HOSTING.md). They are release
support work, not new desktop functionality. Other ideas and captured tweaks
stay in their existing logs unless explicitly selected for this release.

## Embed attribution in the application

- [ ] Add an accessible **Credits and licences** entry from About VineMix.
  Show **VineMix by acoyeen**, the project website and original repository.
  Keep the normal About window compact and make the longer policy readable
  in a separate scrollable view if needed.
- [ ] Make the full MIT licence and attribution policy readable offline, with
  optional links to `https://vinemix.app/attribution` and the original repository.
  Opening the policy must not require a network connection or disturb audio.
- [ ] Include the authoritative root `LICENSE` and `ATTRIBUTION.md` in Setup,
  installed files, standalone payload and matching source archive. Update the
  package allowlists, source-identity inventory, build receipts and installer
  verification to cover the policy file; preserve third-party notices.
- [ ] Check packaged text against the authoritative files, including the
  copyright notice identifying acoyeen, VineMix and `https://vinemix.app`.
- [ ] Check keyboard navigation, text wrapping, scrolling, high contrast and
  display scaling in the delivered interface, including an offline session.

**Licence boundary:** this release embeds the existing MIT licence and policy.
Required copyright/permission notice retention and the requested visible credit
inside forks must remain clearly distinguished. The current policy does not
make a fork's About-screen acknowledgement compulsory. A mandatory interface
credit would need a separately selected and reviewed licensing approach;
embedding this policy or changing the version number does not create one.
Earlier copies retain their original permissions.

## Prepare one integrated candidate

1. **Reconcile current work.** Start with the current application development
   checkout and its latest group, Settings and branding work. The attribution
   publication checkout alone does not contain all Beta 6 application changes.
   Inventory changes and outstanding checks before committing the combined
   source. Preserve other active work and personal settings.
2. **Qualify the integrated follow-ups.** Include the latest TWEAK-017,
   ISSUE-022 and shorter notification tooltip source and evidence. Their local
   integration completed during planning; real pointer/VR review remains.
   Explicitly defer any item that cannot be qualified and update release claims.
3. **Implement attribution embedding.** Complete the checklist above without
   adding dependencies merely to display static credit and licence text.
4. **Transition source identity.** Update authoritative `release.json`, generated
   app/engine version resources, bundled What's New keys and relevant tests.
   Verify `0.4.0.0` against installer, updater and rollback comparisons. Retain
   dated historical version strings and compatibility identifiers.
5. **Consolidate release documentation.** Prepare the final
   `docs/releases/0.4.0-beta.md`, changelog entry, user/help and installer guidance.
   Link them here when created. Keep the old Beta 6 record as evidence rather
   than copying its results into a new claim of release qualification.
6. **Freeze a candidate.** Commit reviewed source, record the exact commit and
   build identity, and build clean Setup and standalone packages from it.
   Any later code change requires the affected checks and package rebuilds.

## Qualify the release

- [ ] Run the required native, desktop, update, version-resource, installer and
  recovery checks against the integrated source; inspect required UI renders.
- [ ] Exercise both delivered formats in an isolated environment, including
  cold start, settings persistence, groups, numeric edits, Settings, attribution
  access, What's New and graceful shutdown/recovery.
- [ ] Verify fresh Setup and upgrades preserve workspace/preferences, shortcut
  launching, both physical settings views and consistent installed inventory.
  Reconcile Windows Installed apps and `installation.ini` with the actual
  delivered version; the earlier development overlay did not do that.
- [ ] Check real driver consent/install/cancel/elevation/reboot behavior on a
  suitable test system, and describe remaining audio, pointer/VR or hardware
  limitations accurately in the release notes.
- [ ] Test update discovery from Betas 2–5 and the local Beta 6 identity, with
  the existing signature/trust, compatibility and file-version checks intact.
  Verify installed and standalone transitions and interrupted-update recovery.
  Beta 1 needs a documented manual upgrade path because it predates the updater.
  Betas 2–4 also have recorded updater compatibility limitations: establish a
  direct manual upgrade to 0.4.0 for any unsupported automatic path, without
  depending on a Beta 5 download that will be retired.
- [ ] Test version ordering through `0.4.0-beta`, `0.4.1-beta`, `0.5.0-beta`
  and `1.0.0`; do not weaken release-identity checks to accommodate the rename.
- [ ] Recheck update discovery with Beta 1–5 release pages still present but
  their assets absent. Update website discovery and its offline fallback so
  neither offers a retired download.
- [ ] Audit final binaries, archive contents and metadata for private data;
  retain required third-party licences and neutral example devices.

## Publish 0.4.0 Beta

After maintainer finalization and qualification, publish one complete GitHub
prerelease at `v0.4.0-beta`, with matching source and all of these assets:

- `VineMix-0.4.0-beta-Setup-x64.exe`
- `VineMix-0.4.0-beta-Standalone-x64.exe`
- `VineMix-0.4.0-beta-Source.zip`
- `SHA256SUMS.txt`
- `VineMix-0.4.0-beta-Updates.json` with the required signed update envelope

Prepare the asset set before making the release public. Verify signed metadata,
checksums and source correspondence, then verify public downloads and old-client
update discovery after publication. Signed update metadata must not be described
as Windows executable signing unless the executables are also actually signed.

Update the README, release index, current user/install guidance, website release
fallback and download links to 0.4.0 Beta. Deploy the website and verify its
online and failure/fallback states. Complete this before retiring Beta 5 so
there is always a working advertised download and migration destination.

## Retire Beta 1 through 5 downloads

**Planned scope:** retain release pages, tags, source history, titles and historical
notes. Remove all manually uploaded attachments from these five releases once
the replacement is healthy. This interprets "downloads" as the complete old
uploaded asset set, including source ZIPs and related update/checksum files.

Read-only GitHub inventory on 28 October 2026:

| Release tag | Uploaded assets scheduled for retirement |
| --- | --- |
| `v0.3.0-beta.1` | Setup EXE, standalone EXE, Source ZIP, SHA256SUMS.txt: 4 |
| `v0.3.0-beta.2` | Same four plus signed Updates JSON: 5 |
| `v0.3.0-beta.3` | Same four plus signed Updates JSON: 5 |
| `v0.3.0-beta.4` | Same four plus signed Updates JSON: 5 |
| `v0.3.0-beta.5` | Same four plus signed Updates JSON: 5 |

There are **24 uploaded assets** in this snapshot. For each tag, filenames use
that exact version: `VineMix-<version>-Setup-x64.exe`,
`VineMix-<version>-Standalone-x64.exe`, `VineMix-<version>-Source.zip`,
`SHA256SUMS.txt`, and, where present, `VineMix-<version>-Updates.json`.

Retirement procedure:

1. Re-read each release and its asset list; check for changes and immutable
   release restrictions. Record exact asset IDs, filenames, sizes and hashes in
   a private recovery inventory. Preserve verified copies and original release
   note text outside the source checkout before deletion. Never broaden deletion
   to unknown assets, release pages or tags.
2. Verify 0.4.0 Beta's public assets, website links and updater behavior. If a
   qualification gate fails, stop retirement and keep the older assets available.
3. Delete the reviewed attached assets by their exact IDs. Preserve release
   identity, tag and historical notes. Do not invoke release deletion or rewrite
   Git history. Do not attempt to finalize or replace provisional Beta 2.
4. After confirming removal, add the notice below to the top of each release
   page and replace its old download instructions with a link to 0.4.0 Beta.
   Retain the original change history and verification limits below the notice.
   If removal is partial, record the actual state instead of claiming completion.
5. Re-query all five pages: they must remain accessible with their original
   tags and no manually attached assets. Check old asset links, current download
   links and update discovery. Save sanitized completion evidence here.

Proposed release-page notice, to use only after removal and with the actual date:

> **Historical release — downloads removed on [date].** The uploaded downloads
> for this version have been removed from GitHub. This page and its tag remain
> for version history. Download [VineMix 0.4.0 Beta](https://github.com/acoyeen/vine-mix/releases/tag/v0.4.0-beta)
> for the current beta. Existing copies retain their original licence terms.
> GitHub-generated source archives remain available from the retained tag.

GitHub's automatic **Source code (zip)** and **Source code (tar.gz)** links are
derived from tags and are not manually uploaded assets. They remain under this
plan. Removing uploaded downloads is distribution cleanup; it cannot revoke
permissions in existing copies or prevent reuse of retained historical source.

For rollback, keep the private recovery inventory and packages until the new
release is stable. If retirement has already started when a release problem is
found, use that inventory for a deliberate restoration of needed assets and
accurate notices. Do not silently replace published 0.4.0 binaries; a corrected
public build gets the next version and matching signed metadata.

## Completion record

### Authorized execution plan

1. Preserve the working changes outside the checkout; audit desktop, native,
   update and packaging code in parallel. Fix demonstrated defects and remove
   unused code only after checking references and compatibility.
2. Embed offline attribution, reconcile the latest feature inventory, transition
   app/engine and website identity, and test version ordering and retired assets.
3. Freeze reviewed source; run native, detached desktop, update/recovery,
   activation safeguards, installer and standalone checks. Inspect new renders
   and audit source/packages for personal data and required notices.
4. Build matching Setup and standalone packages, export exact source, prepare
   signed metadata and checksums, and validate all five release assets.
5. Publish the verified prerelease and website cutover, verify downloads and
   discovery, then back up and remove the exact Beta 1–5 attachments. Retain
   release posts/tags and add removal notices only after confirmed deletion.
6. Install the paired release on the maintainer's system after permission for
   the audio interruption; verify physical settings identity, inventory and
   actual Windows-shell shortcut launch. Record the final outcomes and limits.

| Milestone | Status |
| --- | --- |
| Release hub and feature inventory | Prepared 28 October 2026 |
| Updated policy in repository and website | Published previously; [evidence](../website-redesign/HOSTING.md) |
| Combined application source and selected follow-ups | Pending |
| Attribution embedded and packaged | Pending |
| 0.4.0 identity applied to source and delivered binaries | Pending |
| Final candidate qualification and maintainer finalization | Pending |
| 0.4.0 Beta release and website download cutover | Pending |
| Beta 1–5 attachments removed and pages annotated | Pending; no removals performed |

**Next action:** reconcile the current application development changes and the
latest integrated follow-ups, then implement offline attribution access. Update
this table with commit, qualification and publication evidence as each phase
finishes. Keep per-feature details in the linked issue/tweak/idea records.

## References

External release mechanics checked on 28 October 2026:

- [GitHub release management](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository): release editing and immutable-release restrictions.
- [GitHub release asset API](https://docs.github.com/en/rest/releases/assets#delete-a-release-asset): deleting an individual attachment separately from its release.
- [About GitHub releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases): automatically generated source ZIP and tarball links from retained tags.
- [Semantic Versioning](https://semver.org/): version ordering and prerelease identifiers.
- [MIT licence](https://opensource.org/license/mit): required copyright and permission notices; the project's [attribution policy](../ATTRIBUTION.md) explains the requested interface credit.
