# VineMix beta qualification records

Current release: **0.3.0 · Beta 2**. Updated 22 October 2026.

| Version | Files, changes and known issues | Recorded verification |
| --- | --- | --- |
| [0.3.0-beta.2](releases/0.3.0-beta.2.md) | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2) | 17 native suites across first run/repeat; 12,188 desktop checks; 189 installer checks; 71 update-core and 66 worker checks; exact package smoke and signature checks. |
| [0.3.0-beta.1](releases/0.3.0-beta.1.md) | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.1) | 17 native suites; 10,860 desktop checks; 175 installer checks; 17 runtime and 68 migration/reopening checks. |

Every result belongs to its recorded source snapshot and exact artifact hashes.
Checksums and release assets are retained per version. Beta 2's detailed
[implementation evidence](TESTER_FIXES.md) includes the initial timing failure
and its controlled repeat; no failed check was silently reclassified.

Real headset/device lifecycle, physical display, clean-machine and sustained-use
coverage remains incomplete. The delivered update/rollback path still needs a
complete isolated exercise. These are recorded beta limitations, separate from
the component and package checks already performed. Do not describe a fixture
or an interrupted engine observation as a hardware or endurance pass.

Raw diagnostics and workspace recovery backups stay outside the source and
release assets. Use the [tester guide](BETA_TESTER_GUIDE.md) to report an issue
with reviewed, relevant logs and screenshots.
