# VineMix beta qualification records

Current release: **0.3.0 · Beta 3**. Updated 23 October 2026.
See [current qualification](BETA_TESTING_CURRENT.md) for the Beta 3 checks and
remaining gates. Earlier passes do not qualify a new source snapshot or package.

| Version | Release information | Recorded verification |
| --- | --- | --- |
| [0.3.0-beta.3](releases/0.3.0-beta.3.md) | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.3) | 17 native suites across first run/repeat; 12,142 desktop checks; 189 Setup and 17 runtime checks; 71 update-core, 90 worker and eight signing/preparation checks. Final file identities and package results accompany the release source manifest. |
| [0.3.0-beta.2](releases/0.3.0-beta.2.md) | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2) | 17 native suites across first run/repeat; 12,188 desktop checks; 189 installer checks; 71 update-core and 66 worker checks; exact-package smoke and signature checks. |
| [0.3.0-beta.1](releases/0.3.0-beta.1.md) | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.1) | 17 native suites; 10,860 desktop checks; 175 installer checks; 17 runtime and 68 migration/reopening checks. |

Every result belongs to its recorded source snapshot and artifact hashes.
[Beta 1 implementation history](history/BETA1_IMPLEMENTATION.md) and
[Beta 2 test evidence](TESTER_FIXES.md) preserve the scope of those checks,
including the original timing failure and its controlled repeat.

Real-device lifecycle, physical display, clean-machine and sustained-use coverage
remains incomplete. The delivered update/rollback path still needs a complete
isolated exercise. These are beta limitations, separate from component and package
checks. A fixture or interrupted observation is not a hardware or endurance pass.

Raw diagnostics and workspace recovery backups remain outside source and release
assets. Use the [tester guide](BETA_TESTER_GUIDE.md) to report issues with reviewed,
relevant logs and screenshots.
