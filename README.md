# VineMix

An MIT-licensed audio mixer for **Windows 11 x64**. Connect microphones, apps,
effects and outputs using visible cables. Listen through headphones and send
your mix to a virtual microphone, with independent output levels and mutes.

**This source tag: 0.3.0 · Beta 1.** Packages built on 21 October 2026;
release history published on 22 October 2026.

## Downloads

- [Windows installer](https://github.com/acoyeen/vine-mix/releases/download/v0.3.0-beta.1/VineMix-0.3.0-beta.1-Setup-x64.exe)
- [Standalone EXE](https://github.com/acoyeen/vine-mix/releases/download/v0.3.0-beta.1/VineMix-0.3.0-beta.1-Standalone-x64.exe)
- [Checksums and complete release assets](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.1)
- [All releases](https://github.com/acoyeen/vine-mix/releases)

Both include .NET. Setup creates shortcuts and can offer optional audio drivers.
Standalone prepares its bundled app files automatically. The app and installer
are unsigned; compare the supplied SHA-256 checksums before running a download.
Driver licences and signatures are separate.

![VineMix starter with example microphone and headphones, both outputs muted](docs/images/workspace.png)

This is a silent example rendered with real app controls. Example device names
are placeholders; your devices will differ.

## Changes

- VineMix naming and copyable version/build information in Settings → About.
- A three-node starter: Physical input connected to Virtual output and Listening
  output. Physical devices are unassigned; both outputs start muted and listening
  starts at −24 dB. Existing workspaces retain their layout and assignments.
- Explicit recovery choices and preserved originals when a saved workspace
  cannot be loaded. Restartable migration retains legacy AudioMixer data.
- Installed and standalone copies coordinate one workspace and one session.
- Beginner installation, routing, recovery and bug-report guidance.

## Updating

Beta 1 predates the in-app updater. Download and run the
[Beta 2 installer or standalone EXE](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2)
after using **Quit VineMix** from the tray. Closing the main window alone keeps
the engine running. Keep the previous executable and data backup until the newer
version restores your workspace correctly.

## Verification and known issues

Recorded checks include 17 native suites, 10,860 detached desktop checks with
215 renders and zero binding errors, 175 installer checks, 17 runtime checks
and 68 scripted migration/reopening checks. These results belong to the exact
Beta 1 files in the [release notes](docs/releases/0.3.0-beta.1.md).

Virtual Desktop microphone visibility, header/footer resize trails and missing
Add Node explanations were reported against this version; Beta 2 addresses these
areas. Physical headset and display confirmation is still needed. Clean-machine,
reconnect/sleep and eight-hour normal audio/UI qualification were not completed.
An interrupted approximately 72-minute engine observation is not an eight-hour
pass. An earlier unexpected closure remains unexplained.

For setup, routing, recovery and reporting, use the
[current product guide](https://github.com/acoyeen/vine-mix/blob/main/docs/BETA_TESTER_GUIDE.md).

## Build from source

Use Windows 11 x64 and PowerShell 7. The first build downloads pinned tools and
dependencies. Both package formats include .NET:

```powershell
pwsh -File .\scripts\build.ps1 -BuildMode Installed
pwsh -File .\scripts\build.ps1 -BuildMode Standalone
```

Use `-PackageDirectory` with a new directory when keeping an existing package.
Builds prepare files separately from application activation. For source-only
verification:

```powershell
pwsh -File .\scripts\verify-offline.ps1
```

See [source provenance](docs/RELEASE_SOURCE.md) for this historical release and
[the MIT licence](LICENSE) for the original application. Optional driver and
third-party component licences remain separate.
