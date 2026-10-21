# VineMix

An open-source audio mixer for **Windows 11 x64**. Connect microphones, apps,
effects and outputs with visible cables. Listen through headphones, send a mix
to a virtual microphone, and control each output independently.

**Current release: 0.3.0 · Beta 2** · [Release notes](https://github.com/acoyeen/vine-mix/releases/tag/v0.3.0-beta.2) · [All releases](https://github.com/acoyeen/vine-mix/releases)

| Download | Choose this when… |
| --- | --- |
| [Windows installer](https://github.com/acoyeen/vine-mix/releases/download/v0.3.0-beta.2/VineMix-0.3.0-beta.2-Setup-x64.exe) | You want Start menu shortcuts and a normal app installation. Optional virtual audio drivers are offered separately. |
| [Standalone EXE](https://github.com/acoyeen/vine-mix/releases/download/v0.3.0-beta.2/VineMix-0.3.0-beta.2-Standalone-x64.exe) | You want to launch one downloaded EXE without installing the app. Install virtual audio drivers separately if needed. |

Both include the .NET runtime. VineMix and its installer are currently unsigned;
Windows may show an unknown-publisher warning. Compare the release's SHA-256
checksums before running a download. Driver signatures and licences are separate.

![VineMix starter with example microphone and headphones, both outputs muted](docs/images/workspace.png)

This is a silent example rendered with real app controls. “Example USB Microphone”
and “Example Headphones” are placeholders, not a saved personal workspace.

## What you can do

- Route a physical microphone, streamed microphone, virtual cable or individual
  app into separate listening and virtual microphone outputs.
- Build effect chains with EQ, denoising, compression, limiting, ducking, reverb,
  Voice Color and alignment delay.
- Edit up to 32 nodes and 96 connections, with 24 node types, searchable creation,
  copy/paste and Undo/Redo.
- Keep independent output levels and mutes, automatic workspace saving and
  recoverable backups. Audio processing stays local.
- Check for updates in Settings, read release notes, then choose when to download
  and install. Daily checks can be turned on or off; installation requires your choice.

Beta 2 adds support for streamed and virtual microphones in Physical input,
clearer optional-driver setup, a resize redraw fix, and purpose help for every
Add Node choice. Virtual Desktop hardware confirmation and display-specific resize
checks remain part of beta testing. See [testing and known limits](TESTING.md).

## Get started

1. Run the installer or open the standalone EXE in File Explorer. The
   [installation guide](docs/BETA_TESTER_GUIDE.md#choose-a-package) explains both.
2. Choose your microphone in **Physical input** and headphones in **Listening
   output**. A fresh workspace already connects the input to both outputs.
3. Both outputs start **muted**. Listening output starts at **−24 dB**. Begin at
   low headphone volume, unmute that output, and adjust it gradually.
4. To send the mix to another app, install an appropriate VAC driver, confirm
   **Virtual output** uses **Line 1 (Virtual Audio Cable)**, and select **Line 1**
   recording as the microphone in the receiving app. Unmute Virtual output when
   ready; its mute is independent of your headphones.

![Fresh three-node workspace with physical devices unassigned and an unavailable virtual output](docs/images/first-run.png)

Physical microphone and headphone routes work without virtual audio drivers.
An existing Virtual Desktop microphone also needs neither bundled cable driver.

| Virtual route | Driver and endpoint names |
| --- | --- |
| Receive sound from another app's playback device | [VB-CABLE](https://vb-audio.com/Cable/): the other app plays to **CABLE Input**; VineMix captures **CABLE Output**. |
| Provide the mix as another app's microphone | [VAC](https://vac.muzychenko.net/en/download.htm): VineMix sends to **Line 1** playback; the other app records from **Line 1**. |

**VAC Lite permits private home use without income generation.** Review its
[licence](https://vac.muzychenko.net/en/license.txt) for other uses. The app's MIT
licence does not change third-party driver terms.

## Everyday controls

- **Add** creates an input, effect or output; drag between audio sockets to
  connect them. Green cables carry audio; lavender cables are Ducking triggers.
- Mouse wheel zooms, middle-drag pans, and **Home** fits the workspace.
  **Ctrl+Z** undoes, **Ctrl+F** finds a node, and **F1** opens Help & shortcuts.
- Changes save automatically. Closing the window keeps audio running in the
  tray; **Quit VineMix** stops the app and engine.
- For an unavailable device, reconnect it and use **Settings → Retry assigned
  devices**. **Restart audio engine** briefly interrupts audio and restores saved
  routes. See [recovery](docs/BETA_TESTER_GUIDE.md#recovery).
- **Settings → Check for updates** shows release notes and separate **Download
  update** and **Install and restart** actions. An installation briefly stops
  audio and reopens the saved workspace. Beta 1 users make their first upgrade
  by downloading and running Beta 2 manually.

## Documentation and feedback

- [Installation, first session, updates and recovery](docs/BETA_TESTER_GUIDE.md)
- [Update behavior and removal](docs/INSTALLATION_AND_UPDATES.md)
- [User guide](docs/USER_GUIDE.md), [application audio capture](docs/APP_AUDIO.md)
  and [device visibility](docs/DEVICE_VISIBILITY.md)
- [Changelog](CHANGELOG.md), [testing](TESTING.md) and [contributing](CONTRIBUTING.md)
- [Report a bug](https://github.com/acoyeen/vine-mix/issues/new/choose) with version
  information from **Settings → About VineMix** and steps to reproduce it.
- [Third-party notices](THIRD_PARTY_NOTICES.md) and [distribution conditions](docs/DISTRIBUTION.md)

Per-app capture leaves the app's original playback audible. Parallel paths have
no automatic latency compensation. The limiter handles sample peaks without
lookahead or true-peak detection. Long-session, reconnect and sleep/resume
testing is ongoing; beta status does not imply every hardware combination is qualified.

## Build from source

Requires Windows 11 x64, PowerShell 7.3 or later and
[ripgrep (`rg`)](https://github.com/BurntSushi/ripgrep) on PATH. The first build downloads
pinned project-local tools and dependencies:

```powershell
pwsh -File .\scripts\build.ps1 -BuildMode Installed
```

The package is created under `artifacts\VineMix-0.3.0-beta.2-Installed`.
Use `-BuildMode Standalone` for the single-EXE package or `-PackageDirectory` to
choose another destination. Build Setup with `scripts/build-installer.ps1`;
see the [installer guide](installer/README.md).

```powershell
pwsh -File .\scripts\verify-offline.ps1
```

Offline checks use synthetic audio, simulated devices and temporary data.
Release packages exclude the separate verification executable.

See [release source and provenance](docs/RELEASE_SOURCE.md) for version tags and
the exact build-source archives.

VineMix's original code is released under the [MIT licence](LICENSE).
Third-party components retain their own licences and notices.
