# VineMix releases

Download a Windows installer or standalone EXE from the current version's
release page. Both formats include .NET.

**Current release: 0.4.0 Beta**, formerly developed as Beta 6. The
[release record](RELEASE_0.4.0_BETA.md) tracks final publication and retirement
of Beta 1–5 uploaded downloads. Their release posts, tags and history remain.

| Version | Release notes and downloads | Highlights |
| --- | --- | --- |
| **0.4.0 · Beta** | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.4.0-beta) · [Detailed notes](releases/0.4.0-beta.md) | Named groups, stereo mixing tools, precise Level controls, offline attribution and interface/save refinements. |
| **0.3.0 · Beta 5** | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.5) · [Detailed notes](releases/0.3.0-beta.5.md) | Corrected update-package metadata, reliable automatic-check scheduling and a simpler update window. |
| **0.3.0 · Beta 4** | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.4) · [Detailed notes](releases/0.3.0-beta.4.md) | Streamed headset output, layout lock, safer quitting, bundled update highlights and clearer workspace feedback. |
| **0.3.0 · Beta 3** | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.3) · [Detailed notes](releases/0.3.0-beta.3.md) | Clean development baseline, removal of experimental resize changes, correctness fixes and refreshed verification. |
| **0.3.0 · Beta 2** | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2) · [Detailed notes](releases/0.3.0-beta.2.md) | Streamed microphone discovery, clearer Setup choices, complete Add Node explanations and signed in-app updates. Retained provisional release. |
| **0.3.0 · Beta 1** | [GitHub release](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.1) · [Detailed notes](releases/0.3.0-beta.1.md) | Three-node starter, workspace recovery and migration, installed and standalone packages. |

Beta 1 users should install 0.4.0 Beta manually to gain the in-app updater.
Beta 2–4 packages have a known updater compatibility defect. Quit VineMix and
install 0.4.0 Beta over the existing installation, or replace the standalone EXE;
there is no need to uninstall or install intermediate betas. Earlier downloads
are retired after the verified replacement is available; their posts retain
historical notes and link to 0.4.0 Beta.
Later updates are available through **Settings → Check for updates**. Daily
checks can be turned on or off; download and installation are explicit actions.
See [installation and updates](INSTALLATION_AND_UPDATES.md).

Each release includes `SHA256SUMS.txt`. Beta 2 and later include signed
`VineMix-<version>-Updates.json`, which binds the executable versions, names,
lengths and hashes. The project update signature is separate from Windows
Authenticode signing. Known beta issues and unperformed hardware checks remain
listed with the affected version.

The original code is [MIT licensed](../LICENSE). Fonts, libraries, runtimes and
optional audio drivers retain their own notices and terms. Source accompanies
the version tags; a release may additionally provide an exact build-source ZIP
with a per-file manifest.
