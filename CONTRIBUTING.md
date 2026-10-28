# Contributing

Forks and modified versions are welcome. Follow [Crediting VineMix](ATTRIBUTION.md):
preserve the full MIT notice and please include a short acknowledgement of
VineMix with its website or repository link in your program's credits. The
policy distinguishes required licence notices from requested visible credit.

Use `vine-mix` for the repository and local checkout folder, and **VineMix** for
the product name. Clone `https://github.com/acoyeen/vine-mix.git` into a folder
named `vine-mix`. Keep C# project names such as `VineMix.Desktop` and CMake
identifiers such as `VINEMIX_OFFLINE_TESTS` in their language-appropriate forms.
See [name compatibility](docs/REBRAND_COMPATIBILITY.md) before changing legacy
settings, process, clipboard or upgrade identifiers.

Build on Windows 11 x64 with PowerShell 7 using `scripts/build.ps1`, then run
`scripts/verify-offline.ps1`. See [README.md](README.md) and [TESTING.md](TESTING.md)
for setup and the distinction between offline checks and hardware qualification.

Keep changes focused. Preserve saved-graph compatibility, current-user pipe
access, bounded audio queues and the rule that the audio callback does not
allocate or perform file/network I/O. Keep device enumeration, graph compilation
and settings persistence outside the processing loop. Add a meaningful regression
check when fixing a bug in these contracts.

For UI changes, use the detached renderer and inspect its output at the supported
window sizes and display scales. Do not run real-endpoint probes or activation
scripts as part of ordinary verification. Those tools can affect live audio and
are explicit manual qualification tools.

Open an issue with Windows version, app version, reproduction steps and the
expected/actual result. Remove device identifiers, executable paths and personal
settings before attaching diagnostics. For a contribution, describe what changes
for the user and which checks passed; identify any hardware checks not run.

Preserve upstream notices. Any new dependency or copied implementation must have
its source, exact version, licence and required credits documented in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), with required notice text included
under `licenses/`. Do not commit vendor driver installers, private settings,
credentials or generated packages.
