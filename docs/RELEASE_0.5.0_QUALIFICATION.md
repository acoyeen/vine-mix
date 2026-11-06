# VineMix 0.5.0 Beta qualification

**6 November 2026 — published; source and delivered packages verified.**

[Download 0.5.0 Beta](https://github.com/acoyeen/vine-mix/releases/tag/v0.5.0-beta).
Both the [exact-source checks](https://github.com/acoyeen/vine-mix/actions/runs/42153394427)
and [published-asset checks](https://github.com/acoyeen/vine-mix/actions/runs/42154607689)
passed.

The maintainer authorized the complete audit, fixes, documentation refresh and
publication. Identity: `0.5.0-beta`, Windows `0.5.0.0`, display **0.5.0 · Beta**.
The [release notes](releases/0.5.0-beta.md) explain the changes. Earlier local
implementation evidence is retained in [the execution record](IMPLEMENTATION_0.5.0.md).

## Scope

Review the native processing and graph lifecycle, desktop editing and navigation,
cross-language settings boundaries, restart/update identity, packaging, notices,
source provenance and published downloads. Fix demonstrated defects with focused
regressions, then qualify the combined source and actual delivered packages.

All interface verification uses detached controls and screenshots; no
computer-use tools are used. Audio tests use synthetic sources and simulated
devices. Raw reports, settings backups and signing material remain outside the
repository and release assets. Existing workspaces and levels are preserved.

## Evidence

All **19 native offline suites** passed after the audit correction, including
**289,921 graph assertions** and **8,138 qualification assertions**. The real
Release desktop/native configuration boundary passed **802 checks**. Invalid
settings retain their original and backup bytes, and older valid graphs retain
their settings. Shared endpoint routing performs no audio-thread heap operations.

The final quiet 30-Distortion routing-fade test measured **4.8399 ms p99** against
the 5-ms quantum (3.8794-ms median). This passes the measured budget with limited
margin; the separate RNNoise capacity diagnostic below remains a recorded limit.

**Subsequent capacity failure:** the documentation-only main commit's
[cloud run](https://github.com/acoyeen/vine-mix/actions/runs/42155431986) failed
the same 30-Distortion routing-fade gate at **5.3432-ms median / 9.6201-ms p99**.
The native source and tests were unchanged; the earlier release cloud run measured
3.8406 / 3.9621 ms and passed. Every other suite and the preceding 460 creative
checks passed. This is host-dependent processing capacity, with a median already
over budget; it is not dismissed as a single outlier. The failed evidence and
released source/assets are preserved. See [ISSUE-027](KNOWN_ISSUES.md#issue-027).

The Release desktop suite passed **15,354 checks**, produced **328 previews**
and reported **zero binding errors**. The focused documentation run passed
**225 checks / 22 captures**, including normal/minimum windows and 96/192 DPI.
The separate keyboard suite passed **60 checks**. Both verification builds
completed without warnings or errors. Public screenshots use neutral fixtures;
their PNG chunks contain no private text or EXIF metadata.

Release and restart tooling passed **874 checks**: update core 93, worker 319,
signed release preparation 9, native version resources 5, shared activation 40,
UI activation 97, paired activation 229, Setup helper 31 and controller identity
51. The worker's first real-child fixture rejected a redirected default cache;
rerunning from an explicitly resolved physical cache passed without weakening
identity checks. The initial failure remains in private evidence.

The audit corrected channel/process JSON narrowing before range checks
([ISSUE-029](KNOWN_ISSUES.md#issue-029)), updated focused-dial Help and bundled
0.5.0 highlights, and removed stale starter/routing claims from current guides.
Earlier implementation counts are not reused as final qualification.

The actual delivered Setup passed **225 checks**, covering isolated installation,
repair, downgrade protection, uninstall and inert production smoke. The
standalone passed **21 extraction/integrity/version/notice checks** and its
production smoke. An additional **76 payload checks** verified receipt bytes,
notice equality, engine equality between formats and absence of targeted private
paths in 93 distributed files. Personal processes, settings, startup and shortcuts
were unchanged during isolated package qualification.

The final signed standalone passed **327 worker checks**, including **three
production-worker smoke runs**. The original corrected 0.4.0 UpdateCore and trust
key anonymously discover 0.5.0 as Available and verify both signed package hashes;
the same code reports the 0.5.0 identity UpToDate. These checks do not substitute
for the separate-account transaction limits below.

## Source and publication

Frozen source: `57052a200b0ec73d0d816f9ed12d2d5a2e13013d`.
Build-input SHA-256: `dcafff73acef21e1ebb8c0e5de28df483bae032ddc355488573e299a33ee7689`.
Both formats were built from the same clean checkout. The audited source ZIP
contains **632 entries**, including all **347 build inputs** and a sanitized
`SOURCE-MANIFEST.json`. Every archived file was rehashed against committed bytes;
archive names, privacy markers and metadata were checked.

Published at **03:07:41 UTC on 6 November 2026** as a normal GitHub prerelease.
All five uploaded assets were downloaded and rehashed before publication; the
production-pinned RSA-PSS/SHA-256 update signature passed. All five anonymous
download links return HTTP 200. Earlier releases and repository visibility were
preserved. No signing keys, raw diagnostics or personal settings were published.

| Asset | SHA-256 |
| --- | --- |
| Setup x64 | `b350784d48e0a499143ce126c269f2532e7203fa73185c219997d9a66a5d75ef` |
| Standalone x64 | `d60cc7b9304207f2e3e996a0809e087d7885b69fdfe3f9b27ccaa3739d6fe25c` |
| Source ZIP | `69824a77514b5e32f99a17cbcc16bf70764f572f8daaebf06e4a6b61bd73f1e9` |
| Signed Updates JSON | `e54db792a0afee6239ef343f7055d2cabb0b1db5018795d306252256fe99d693` |
| SHA256SUMS.txt | `e9ae570535ec96554d8bc90140ea2efa82d792d046c2ea2bf0df57c13f477214` |

The front page and current guides use the new starter and qualified screenshots.
The source tag retains the audit checkpoint at freeze; this current record adds
the subsequent package and publication outcomes without moving that tag.

The website was updated after release verification. All **85 public files**
match the prepared bytes and retain the three configured security headers.
Saved fallback links, anonymous release discovery and reconnection select 0.5.0;
hosting settings and domain routing were preserved. The
[website cutover record](../website-redesign/HOSTING.md#050-beta-release-cutover--6-november-2026)
contains deployment identity and scope. No browser/computer-use automation was used.

## Local installation

The exact published Setup installed 0.5.0 successfully through the trusted
Windows-shell path with optional driver installation disabled. All **37 payload
files**, the installed inventory, Windows registration and shortcuts were
verified. The engine used the same physical settings file, retained the saved
configuration and restored every previously running route. Frames advanced
through 8 seconds of engine observation, followed by 8 seconds with the actual
shortcut-launched UI. Workspace and preference files were unchanged; no volumes
were adjusted and no settings backup or package rollback was needed.

A separate read-only audit confirmed both process identities and hashes,
all **41 installed files**, **36 inventory entries** and **3 shortcuts**.
The successful installation and withheld first attempt were archived privately
with every copied evidence file rehashed.

The private installer wrapper first withheld activation before stopping any app
because its cold child-process list produced an invalid query. The correction
passed **32 isolated recovery checks** and **7 real read-only checks under the
trusted shell** before the successful retry. Complete previous-package,
registration, shortcut and settings backups remain outside the checkout.

## Reproduce the offline checks

From Windows 11 x64 with PowerShell 7:

```powershell
pwsh -File .\scripts\verify-offline.ps1
.\.tools\dotnet\dotnet.exe run --project desktop/VineMix.UpdateCore.Tests -c Release
.\.tools\dotnet\dotnet.exe run --project desktop/VineMix.UpdateWorkerTests -c Release
pwsh -File .\scripts\test-update-release-tools.ps1
```

Use new external evidence/build directories for isolated runs. Installer and
standalone checks run against the exact final packages; see
[installer verification](../installer/README.md#verification) and
[release publication](../.github/release/README.md).

The post-release CI adjustment adds `-HostedBenchmarkReport` for variable hosted
workers. It runs every creative benchmark and every correctness/allocation
assertion, records measured times and `budgetExceeded` separately, and reports
capacity in the CI summary. It does not change the application or the 5-ms budget.
The default command above remains strict and fails a measured budget overrun.
Hosted functional success must not be reported as strict performance qualification.

The test-only policy change passed both hosted-report and default-strict local
integration: all **19 native suites** and all **8 creative benchmark cases**.
The final **14 deterministic policy checks** prove strict over-budget rejection,
accurate report-mode exceedances, and fatal sound/allocation/invalid-evidence
failures in both modes. The strict follow-up measured 4.3839-ms p99 for the
30-Distortion routing fade; it does not replace the recorded cloud overrun.
The creative assertion count changes from 461 to 453 because two timing
assertions per case become one validity assertion plus explicit budget policy;
no sound, allocation or transition assertion was removed.

## Limits

Detached verification does not establish native pointer, Narrator, IME or VR
controller behaviour. Fresh-machine vendor-driver consent, elevation, reboot,
hardware diversity and sustained audible reliability remain unperformed in this
pass. The real Windows account used for development is not an isolated clean
machine. Component and signed-worker checks do not establish every complete
delivered update, health-check, rollback and interruption transaction on a
separate account.

Distortion-heavy maximum graphs can exceed the processing budget during routing
fades on some hosts. A separate 30-RNNoise diagnostic exceeded the 5-ms budget;
1- and 4-node cases stayed within it on the test host. See
[ISSUE-030](KNOWN_ISSUES.md#issue-030) for the actual timings and limits. The
32-node cap is routing capacity, not a promise for every effect combination.
Parallel paths have no automatic latency compensation, and the limiter
has no lookahead or true-peak detection. These are beta limitations, not claims
of universal hardware or audible reliability.
