# VineMix 0.4.0 Beta qualification

**28 October 2026 — qualification in progress; not yet published.**

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

Final source commit, exact build-input hash, test totals and package hashes will
be recorded after the combined source is frozen and the final packages pass.

The existing local-activation safeguards pass 40 shared, 97 interface-only and
80 paired-package checks. Native version-resource fixtures pass 5 checks and
synthetic signed-release preparation passes 9 checks. All ten website check
suites pass with unnumbered Beta labels and retained empty historical releases.

## Remaining beta qualification

Real pointer/VR use, fresh-machine vendor installer consent/cancel/elevation/
reboot, sustained real-device audio, and delivered update/rollback interruption
scenarios on a separate Windows account remain unperformed. These are beta
limitations, not inferred passes from synthetic tests or successful downloads.

Personal live activation is separate and requires permission for the brief audio
interruption. Raw diagnostics, settings, backups and signing material remain
outside source and downloadable artifacts.
